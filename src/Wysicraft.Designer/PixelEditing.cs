using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Wysicraft.Core;
using Wysicraft.Models;
using Wysicraft.Packaging;
namespace Wysicraft.Designer;

// Pixel editor hosting (saving into Assets, placing on the screen) and the Sprite sheet editor, where clips are made
// by clicking frames on the sheet. Both write the same data as before: a PNG asset, frame size and the clips text.
public partial class MainWindow
{
    internal static PixelImage DecodePixels(byte[] png)
    {
        var frame = BitmapFrame.Create(new MemoryStream(png), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        if (bgra.PixelWidth > PixelArt.MaxSheetSize || bgra.PixelHeight > PixelArt.MaxSheetSize) throw new InvalidOperationException($"The image is larger than {PixelArt.MaxSheetSize} pixels.");
        var pixels = new uint[bgra.PixelWidth * bgra.PixelHeight]; bgra.CopyPixels(pixels, bgra.PixelWidth * 4, 0);
        return new PixelImage(bgra.PixelWidth, bgra.PixelHeight, pixels);
    }
    internal static byte[] EncodePixels(PixelImage image)
    {
        var source = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Pixels, image.Width * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }

    /// <summary>Opens the pixel editor. With a PNG, it's cut into frames of the given size (0: one picture); a project
    /// asset path means saving updates that image. <paramref name="saved"/> gets the asset path and frame layout.</summary>
    PixelEditor OpenPixelEditor(Window? owner = null, byte[]? png = null, string? assetPath = null, int frameWidth = 0, int frameHeight = 0, int newWidth = 32, int newHeight = 32, Action<string, int, int, int>? saved = null, bool canPlace = true)
    {
        List<PixelImage> frames;
        if (png != null)
        {
            var image = DecodePixels(png);
            if (frameWidth > 0 && frameHeight > 0 && (frameWidth < image.Width || frameHeight < image.Height) && image.Width % frameWidth == 0 && image.Height % frameHeight == 0 && frameWidth <= PixelArt.MaxSize && frameHeight <= PixelArt.MaxSize)
                frames = PixelArt.Unpack(image, frameWidth, frameHeight);
            else if (PixelArt.Fits(image.Width, image.Height, 1, out string tooBig)) frames = [image];
            else throw new InvalidOperationException($"This image is {image.Width}×{image.Height}. {tooBig}");
        }
        else frames = [new PixelImage(Math.Clamp(newWidth, 1, PixelArt.MaxSize), Math.Clamp(newHeight, 1, PixelArt.MaxSize))];
        // An animated image (with a .png.mcmeta) opens as its frames, still set to play on its own.
        TextureAnimation? animation = null;
        if (png != null && assetPath != null && project.Assets.TryGetValue(assetPath + ".mcmeta", out var meta))
        {
            var size = ProjectStore.TextureSize(png); animation = TextureAnimation.Parse(System.Text.Encoding.UTF8.GetString(meta), size.Width, size.Height);
            if (animation != null && frameWidth == 0 && animation.FrameWidth <= PixelArt.MaxSize && animation.FrameHeight <= PixelArt.MaxSize && (animation.Columns > 1 || animation.Rows > 1))
                frames = PixelArt.Unpack(DecodePixels(png), animation.FrameWidth, animation.FrameHeight, animation.Frames.Max(f => f.Index) + 1);
        }
        var document = PixelDocument.FromFrames(frames);
        // Layers saved last time come back, as long as the PNG hasn't been changed some other way since.
        if (png != null && assetPath != null && project.Assets.TryGetValue(assetPath + TextureAssets.LayersSuffix, out var layers))
        {
            try { var saved2 = PixelDocument.Load(layers); if (saved2.Produces(DecodePixels(png))) document = saved2; else Log("The layers saved with " + System.IO.Path.GetFileName(assetPath) + " are out of date (the image was changed elsewhere), so it opens as one layer."); }
            catch (Exception ex) { Log("Couldn't read the layers saved with " + System.IO.Path.GetFileName(assetPath) + ": " + ex.Message); }
        }
        string name = assetPath != null ? System.IO.Path.GetFileNameWithoutExtension(assetPath) : "pixel_art";
        var file = Watch(assetPath); PixelEditor? editor = null;
        editor = new PixelEditor(owner ?? this, document, assetPath, name, request => { CheckOverwrite(file, request.AsNew ? null : request.Path, editor!); var path = SavePixelArt(request, saved); Saw(file, path); return path; }, canPlace ? AddPixelArtToScreen : null, animation != null, animation != null ? 20.0 / animation.Frames[0].Ticks : 8);
        // From the main window it opens beside it; opened from another editor's own dialog (the sprite sheet editor) it stays in front of that.
        if (owner != null && owner != this) { artEditorsOpen++; try { editor.ShowDialog(); } finally { artEditorsOpen--; } }
        else OpenBeside(editor);
        return editor;
    }
    string SavePixelArt(PixelEditor.SaveRequest request, Action<string, int, int, int>? saved)
    {
        var png = EncodePixels(request.Sheet);
        if (request.Layers is { Length: > ProjectStore.MaxEntry }) throw new InvalidOperationException("There are too many layers and frames to keep with this image. Merge or delete some layers and save again.");
        string path;
        if (!request.AsNew && request.Path != null) path = request.Path;
        else { path = TextureAssets.Path(project.Manifest.Id, request.Name + ".png"); int n = 1; while (project.Assets.ContainsKey(path)) path = TextureAssets.Path(project.Manifest.Id, request.Name + "_" + n++ + ".png"); }
        Change(); project.Assets[path] = png;
        if (request.Layers != null) project.Assets[path + TextureAssets.LayersSuffix] = request.Layers; else project.Assets.Remove(path + TextureAssets.LayersSuffix);
        // "Plays on its own": a .png.mcmeta beside it, which every runtime plays (frames left to right, then down).
        if (request.AnimateFps is double fps && request.Frames > 1)
        {
            int cells = request.Sheet.Width / request.FrameWidth * (request.Sheet.Height / request.FrameHeight);
            project.Assets[path + ".mcmeta"] = System.Text.Encoding.UTF8.GetBytes(TextureAnimation.Write(request.FrameWidth, request.FrameHeight, request.Frames, cells, TextureAnimation.TicksFor(fps)));
        }
        else project.Assets.Remove(path + ".mcmeta");
        RefreshAssetBrowser(); Draw(); RefreshInspector();
        Log($"Saved {System.IO.Path.GetFileName(path)} ({request.Sheet.Width}×{request.Sheet.Height}{(request.Frames > 1 ? $", {request.Frames} frames of {request.FrameWidth}×{request.FrameHeight}" : "")}) to Assets.");
        saved?.Invoke(path, request.FrameWidth, request.FrameHeight, request.Frames);
        return path;
    }
    /// <summary>For a picture saved from the pixel editor with several frames (and not set to play on its own): the
    /// frame size and count, so it can be used as a sprite straight away. Null otherwise.</summary>
    internal (int FrameWidth, int FrameHeight, int Frames)? SheetFrames(string path)
    {
        if (!project.Assets.TryGetValue(path, out var png) || project.Assets.ContainsKey(path + ".mcmeta") || !project.Assets.TryGetValue(path + TextureAssets.LayersSuffix, out var layers)) return null;
        if (PixelDocument.ReadFrames(layers) is not var (w, h, n) || n < 2) return null;
        try
        {
            var size = ProjectStore.TextureSize(png); int columns = PixelArt.SheetColumns(w, n), rows = (n + columns - 1) / columns;
            return size.Width == w * columns && size.Height == h * rows ? (w, h, n) : null; // the PNG was replaced some other way
        }
        catch (InvalidDataException) { return null; }
    }
    // A Sprite control for a sprite sheet, playing all its frames (a clip called "play"), enlarged in whole steps to at least 32 px.
    Element SpriteFor(string path, int frameWidth, int frameHeight, int frames, double x, double y)
    {
        int scale = Math.Max(1, (int)Math.Ceiling(32.0 / Math.Max(frameWidth, frameHeight)));
        return new Element { Id = Unique("sprite"), Type = "sprite", FillEnabled = false, Texture = AssetResource(path), FrameWidth = frameWidth, FrameHeight = frameHeight,
            Clips = $"play: 0-{frames - 1} @8", Value = "play", Bounds = new() { X = Snap(x), Y = Snap(y), Width = frameWidth * scale, Height = frameHeight * scale }, LayerGroup = isolatedGroup };
    }
    // One picture becomes an Image control; several frames become a Sprite playing them all. Small art is shown
    // enlarged (whole-number steps, so pixels stay square) to at least 32 px.
    void AddPixelArtToScreen(string path, int frameWidth, int frameHeight, int frames)
    {
        int scale = Math.Max(1, (int)Math.Ceiling(32.0 / Math.Max(frameWidth, frameHeight)));
        double w = frameWidth * scale, h = frameHeight * scale;
        var element = new Element { Id = Unique(frames > 1 ? "sprite" : "image"), Type = frames > 1 ? "sprite" : "image", FillEnabled = false, Texture = AssetResource(path),
            Bounds = new() { X = Snap(Math.Max(0, (ui.Size.Width - w) / 2)), Y = Snap(Math.Max(0, (ui.Size.Height - h) / 2)), Width = w, Height = h }, LayerGroup = isolatedGroup };
        if (frames > 1) { element.FrameWidth = frameWidth; element.FrameHeight = frameHeight; element.Clips = $"play: 0-{frames - 1} @8"; element.Value = "play"; }
        Change(); ui.Elements.Add(element); selected.Clear(); selected.Add(element.Id); Draw(); RefreshInspector();
        Log($"Added {element.Id} to {ui.Id}.");
    }
    // The pixel editor from the menu or Assets: the selected image, or a new picture.
    void PixelEditorForAsset(bool newPicture)
    {
        if (newPicture) { OpenPixelEditor(); return; }
        var entry = ChosenAsset();
        if (!entry.Id.EndsWith(".png")) throw new InvalidOperationException("Select an image (PNG) in Assets.");
        OpenPixelEditor(png: project.Assets[entry.Id], assetPath: entry.Id);
    }
    // "Draw…" / "Edit pixels…" in a control's Properties: edits its image (a copy, for Minecraft's own textures), or
    // draws a new one at the control's size and assigns it.
    void PixelEditorForElement(Element e)
    {
        void Assign(string path, int fw, int fh, int frames)
        {
            e.Texture = AssetResource(path);
            if (e.Type == "sprite") { e.FrameWidth = fw; e.FrameHeight = fh; if (string.IsNullOrWhiteSpace(e.Clips)) { e.Clips = $"play: 0-{Math.Max(0, frames - 1)} @8"; e.Value = "play"; } }
            else if (e.Type is not ("image" or "texture_region")) e.FillEnabled = true;
            Draw(); RefreshInspector();
        }
        if (e.Texture.Length > 0 && TryTexture(e.Texture, out var bytes))
            OpenPixelEditor(png: bytes, assetPath: TextureAssets.PathOf(project, e.Texture), frameWidth: e.Type == "sprite" ? e.FrameWidth : 0, frameHeight: e.Type == "sprite" ? e.FrameHeight : 0, saved: Assign, canPlace: false);
        else
            OpenPixelEditor(newWidth: (int)Math.Clamp(e.Type == "sprite" ? e.FrameWidth : e.Bounds.Width, 1, 128), newHeight: (int)Math.Clamp(e.Type == "sprite" ? e.FrameHeight : e.Bounds.Height, 1, 128), saved: Assign, canPlace: false);
    }

    sealed class ClipDraft { public string Name = ""; public List<int> Frames = []; public double Fps = 8; public bool Once; }

    // ---- Sprite sheet editor ----
    void ShowSpriteSheetEditor(Element element, Action<Window>? test = null)
    {
        string texture = element.Texture; int fw = Math.Max(1, element.FrameWidth), fh = Math.Max(1, element.FrameHeight);
        var clips = new List<ClipDraft>(); string? warning = null;
        try { clips = SpriteClips.Parse(element.Clips).Values.Select(c => new ClipDraft { Name = c.Name, Frames = [.. c.Frames], Fps = c.Fps, Once = !c.Loop }).ToList(); }
        catch (Exception ex) { warning = "The clips text couldn't be read (" + ex.Message + "). Applying replaces it with what you make here."; }
        string start = element.Value;
        BitmapSource? sheet = null; double scale = 1; int selectedClip = clips.Count > 0 ? 0 : -1, selectedChip = -1; bool changed = false, syncing = false, applied = false;
        var clock = System.Diagnostics.Stopwatch.StartNew(); string previewKey = "";

        var window = new Window { Owner = this, Title = "Sprite sheet editor — " + element.Id, Width = 1040, Height = 720, WindowStartupLocation = WindowStartupLocation.CenterOwner, MinWidth = 820, MinHeight = 520 };
        var root = new DockPanel(); window.Content = root;
        var top = new WrapPanel { Margin = new Thickness(8, 8, 8, 4) }; DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var bottom = new DockPanel { Margin = new Thickness(8, 4, 8, 8) }; DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var side = new StackPanel { Width = 280, Margin = new Thickness(8) }; var sideScroll = new ScrollViewer { Content = side, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; DockPanel.SetDock(sideScroll, Dock.Right); root.Children.Add(sideScroll);
        var canvas = new Canvas { Background = Brushes.Transparent, Cursor = Cursors.Hand, SnapsToDevicePixels = true };
        var sheetImage = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false }; RenderOptions.SetBitmapScalingMode(sheetImage, BitmapScalingMode.NearestNeighbor);
        var holder = new Grid { Margin = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; holder.Children.Add(canvas);
        root.Children.Add(new ScrollViewer { Content = holder, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = new SolidColorBrush(Color.FromRgb(36, 40, 48)) });

        // Top bar: which sheet, and its frame size.
        var sheetLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), FontWeight = FontWeights.SemiBold };
        var choose = new Button { Content = "Choose image…", Margin = new Thickness(0, 0, 4, 0) };
        var drawNew = new Button { Content = "Draw new…", Margin = new Thickness(0, 0, 4, 0), ToolTip = "Draw a new sprite sheet in the pixel editor" };
        var editPixels = new Button { Content = "Edit pixels…", Margin = new Thickness(0, 0, 16, 0), ToolTip = "Open this sheet in the pixel editor, one frame at a time" };
        var fwBox = new TextBox { Width = 44 }; var fhBox = new TextBox { Width = 44 }; var colsBox = new TextBox { Width = 36 }; var rowsBox = new TextBox { Width = 36 };
        top.Children.Add(sheetLabel); top.Children.Add(choose); top.Children.Add(drawNew); top.Children.Add(editPixels);
        foreach (var (label, box) in new[] { ("Frame width", fwBox), ("height", fhBox), ("   Columns", colsBox), ("rows", rowsBox) })
        { top.Children.Add(new TextBlock { Text = label + " ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) }); top.Children.Add(box); }

        // Side: clips, the selected clip's frames, speed, preview.
        TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Foreground = Brushes.LightSkyBlue, Margin = new Thickness(0, 10, 0, 4) };
        side.Children.Add(Heading("CLIPS"));
        var clipList = new ListBox { Height = 130 }; side.Children.Add(clipList);
        var clipButtons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) }; side.Children.Add(clipButtons);
        Button Small(string text, string tip, Panel into) { var b = new Button { Content = text, ToolTip = tip, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(6, 2, 6, 2) }; into.Children.Add(b); return b; }
        var addClip = Small("New clip", "Add a clip, then click frames on the sheet", clipButtons); var renameClip = Small("Rename", "Rename this clip", clipButtons); var deleteClip = Small("Delete", "Delete this clip", clipButtons);
        var clipHeading = Heading("FRAMES"); side.Children.Add(clipHeading);
        var chips = new WrapPanel(); side.Children.Add(chips);
        var chipButtons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) }; side.Children.Add(chipButtons);
        var earlier = Small("◀", "Move the selected frame earlier", chipButtons); var later = Small("▶", "Move the selected frame later", chipButtons);
        var removeChip = Small("Remove", "Remove the selected frame (Delete)", chipButtons); var clearChips = Small("Clear", "Remove every frame from this clip", chipButtons);
        side.Children.Add(new TextBlock { Text = "Click a frame on the sheet to add it. Shift+click adds every frame from the last one up to it. Right-click removes it.", TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11 });
        var speedRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var fpsBox = new TextBox { Width = 44 }; var once = new CheckBox { Content = "Play once (stop on the last frame)", Margin = new Thickness(0, 6, 0, 0) };
        speedRow.Children.Add(new TextBlock { Text = "Speed ", VerticalAlignment = VerticalAlignment.Center }); speedRow.Children.Add(fpsBox); speedRow.Children.Add(new TextBlock { Text = " frames per second", VerticalAlignment = VerticalAlignment.Center });
        side.Children.Add(speedRow); side.Children.Add(once);
        side.Children.Add(Heading("PREVIEW"));
        var preview = new Image { Width = 112, Height = 112, Stretch = Stretch.Uniform }; RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.NearestNeighbor);
        side.Children.Add(new Border { Width = 112, Height = 112, HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Color.FromRgb(52, 58, 67)), Child = preview });
        var startRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var startBox = new ComboBox { Width = 150 }; startRow.Children.Add(new TextBlock { Text = "Plays first ", VerticalAlignment = VerticalAlignment.Center }); startRow.Children.Add(startBox);
        side.Children.Add(startRow);
        side.Children.Add(new TextBlock { Text = "The clip showing when the screen opens. Scripts switch with ui.play(id, 'name').", TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11 });

        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(buttons, Dock.Right); bottom.Children.Add(buttons); bottom.Children.Add(status);
        var apply = new Button { Content = "Apply", IsDefault = true, Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(8, 0, 0, 0), FontWeight = FontWeights.SemiBold };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(8, 0, 0, 0) };
        buttons.Children.Add(apply); buttons.Children.Add(cancel); cancel.Click += (_, _) => window.Close();

        int Columns() => sheet == null ? 0 : Math.Max(0, sheet.PixelWidth / fw);
        int Rows() => sheet == null ? 0 : Math.Max(0, sheet.PixelHeight / fh);
        int FrameCount() => Columns() * Rows();
        ClipDraft? Clip() => selectedClip >= 0 && selectedClip < clips.Count ? clips[selectedClip] : null;
        var crops = new Dictionary<int, BitmapSource>();
        BitmapSource? FrameImage(int index)
        {
            if (sheet == null || index < 0 || index >= FrameCount()) return null;
            if (!crops.TryGetValue(index, out var crop)) { crop = new CroppedBitmap(sheet, new Int32Rect(index % Columns() * fw, index / Columns() * fh, fw, fh)); crop.Freeze(); crops[index] = crop; }
            return crop;
        }
        // The selected clip, playing at its speed (restarting whenever the clip changes).
        void UpdatePreview()
        {
            var c = Clip(); if (c == null || c.Frames.Count == 0 || sheet == null) { preview.Source = null; return; }
            string key = c.Name + string.Join(",", c.Frames) + c.Fps + c.Once; if (key != previewKey) { previewKey = key; clock.Restart(); }
            preview.Source = FrameImage(SpriteClips.FrameAt(new SpriteClips.Clip(c.Name, [.. c.Frames], c.Fps, !c.Once), clock.Elapsed.TotalMilliseconds));
        }
        void LoadSheet()
        {
            sheet = null; crops.Clear();
            // Decoded at full size (the canvas preview cache shrinks very large images, which would upset frame maths).
            if (texture.Length > 0 && TryTexture(texture, out var bytes)) try { var px = DecodePixels(bytes); var b = BitmapSource.Create(px.Width, px.Height, 96, 96, PixelFormats.Bgra32, null, px.Pixels, px.Width * 4); b.Freeze(); sheet = b; } catch { sheet = null; }
            sheetLabel.Text = sheet == null ? (texture.Length == 0 ? "No image yet" : "Image not found: " + texture) : $"{texture.Split('/').Last()}  ({sheet.PixelWidth}×{sheet.PixelHeight})";
            editPixels.IsEnabled = sheet != null;
            if (sheet != null) scale = Math.Clamp(Math.Min(640.0 / sheet.PixelWidth, 480.0 / sheet.PixelHeight), 1, 24);
            if (sheet != null) scale = scale >= 2 ? Math.Floor(scale) : scale;
        }
        void Redraw()
        {
            canvas.Children.Clear(); crops.Clear();
            fwBox.Text = fw.ToString(); fhBox.Text = fh.ToString(); colsBox.Text = Columns().ToString(); rowsBox.Text = Rows().ToString();
            if (sheet != null)
            {
                canvas.Width = sheet.PixelWidth * scale; canvas.Height = sheet.PixelHeight * scale;
                canvas.Children.Add(new Rectangle { Width = canvas.Width, Height = canvas.Height, Fill = new SolidColorBrush(Color.FromRgb(52, 58, 67)), IsHitTestVisible = false });
                sheetImage.Width = canvas.Width; sheetImage.Height = canvas.Height; sheetImage.Source = sheet; canvas.Children.Add(sheetImage);
                var clip = Clip(); var used = clip?.Frames ?? [];
                for (int i = 0; i < FrameCount(); i++)
                {
                    double x = i % Columns() * fw * scale, y = i / Columns() * fh * scale;
                    int order = used.IndexOf(i); bool inClip = order >= 0;
                    var cell = new Rectangle { Width = fw * scale, Height = fh * scale, Stroke = inClip ? Brushes.DeepSkyBlue : new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), StrokeThickness = inClip ? 2 : 1, Fill = inClip ? new SolidColorBrush(Color.FromArgb(40, 0, 191, 255)) : Brushes.Transparent, Tag = i, ToolTip = $"Frame {i}" };
                    Canvas.SetLeft(cell, x); Canvas.SetTop(cell, y); canvas.Children.Add(cell);
                    if (fw * scale >= 18 && fh * scale >= 14)
                    {
                        var label = new TextBlock { Text = inClip ? $"{i} · #{order + 1}" : i.ToString(), FontSize = 10, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(inClip ? (byte)200 : (byte)140, inClip ? (byte)0 : (byte)0, inClip ? (byte)110 : (byte)0, inClip ? (byte)170 : (byte)0)), Padding = new Thickness(2, 0, 2, 0), IsHitTestVisible = false };
                        Canvas.SetLeft(label, x + 1); Canvas.SetTop(label, y + 1); canvas.Children.Add(label);
                    }
                }
            }
            else { canvas.Width = 420; canvas.Height = 120; canvas.Children.Add(new TextBlock { Text = "Choose an image, or draw one with Draw new…", Foreground = Brushes.LightGray, Margin = new Thickness(8) }); }
            clipList.Items.Clear();
            foreach (var c in clips) clipList.Items.Add($"{c.Name}   ({c.Frames.Count} frame{(c.Frames.Count == 1 ? "" : "s")}, {c.Fps:0.##}/s{(c.Once ? ", once" : "")})");
            clipList.SelectedIndex = selectedClip;
            var current = Clip();
            clipHeading.Text = current == null ? "FRAMES" : "FRAMES IN " + current.Name.ToUpperInvariant();
            chips.Children.Clear();
            if (current != null)
                for (int i = 0; i < current.Frames.Count; i++)
                {
                    int index = i; bool bad = current.Frames[i] >= FrameCount();
                    var chip = new Border { Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 4, 4), CornerRadius = new CornerRadius(3), Cursor = Cursors.Hand, BorderThickness = new Thickness(1),
                        BorderBrush = i == selectedChip ? Brushes.White : Brushes.Transparent, Background = new SolidColorBrush(bad ? Color.FromRgb(140, 50, 50) : Color.FromRgb(23, 107, 145)),
                        Child = new TextBlock { Text = current.Frames[i].ToString(), Foreground = Brushes.White }, ToolTip = bad ? "This frame isn't on the sheet" : $"Step {i + 1}: frame {current.Frames[i]}" };
                    chip.MouseLeftButtonUp += (_, _) => { selectedChip = index; Redraw(); };
                    chips.Children.Add(chip);
                }
            foreach (var control in new UIElement[] { renameClip, deleteClip, earlier, later, removeChip, clearChips, fpsBox, once }) control.IsEnabled = current != null;
            if (current != null) { if (!fpsBox.IsKeyboardFocused) fpsBox.Text = current.Fps.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); once.IsChecked = current.Once; }
            syncing = true;
            startBox.Items.Clear(); startBox.Items.Add("(first clip)"); foreach (var c in clips) startBox.Items.Add(c.Name);
            startBox.SelectedItem = clips.Any(c => c.Name == start) ? start : "(first clip)";
            syncing = false;
            var problems = new List<string>();
            if (warning != null) problems.Add(warning);
            if (sheet != null && (sheet.PixelWidth % fw != 0 || sheet.PixelHeight % fh != 0)) problems.Add($"The {sheet.PixelWidth}×{sheet.PixelHeight} sheet doesn't divide evenly into {fw}×{fh} frames; the leftover edge is ignored.");
            if (clips.Any(c => c.Frames.Count == 0)) problems.Add("Clips with no frames are left out when you apply.");
            if (clips.Any(c => c.Frames.Any(f => f >= FrameCount())) && sheet != null) problems.Add("Some frames (red) aren't on the sheet.");
            status.Text = problems.Count > 0 ? "⚠ " + string.Join("  ", problems) : sheet == null ? "" : $"{FrameCount()} frames of {fw}×{fh}, counted left to right, then top to bottom.";
            status.Foreground = problems.Count > 0 ? Brushes.Orange : Brushes.LightGray;
            UpdatePreview();
        }
        void Edit(Action change) { change(); changed = true; Redraw(); }
        string UniqueClip(string stem) { string n = stem; int i = 1; while (clips.Any(c => c.Name == n)) n = stem + i++; return n; }
        ClipDraft EnsureClip() { if (Clip() is ClipDraft c) return c; var made = new ClipDraft { Name = UniqueClip(clips.Count == 0 ? "idle" : "clip") }; clips.Add(made); selectedClip = clips.Count - 1; return made; }

        canvas.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not Rectangle { Tag: int frame }) return;
            Edit(() =>
            {
                var clip = EnsureClip();
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && clip.Frames.Count > 0)
                {
                    int from = clip.Frames[^1], step = frame >= from ? 1 : -1;
                    for (int f = from + step; f != frame + step; f += step) clip.Frames.Add(f);
                }
                else clip.Frames.Add(frame);
                selectedChip = clip.Frames.Count - 1;
            });
        };
        canvas.MouseRightButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not Rectangle { Tag: int frame } || Clip() is not ClipDraft clip) return;
            int at = clip.Frames.LastIndexOf(frame); if (at >= 0) Edit(() => { clip.Frames.RemoveAt(at); selectedChip = Math.Min(selectedChip, clip.Frames.Count - 1); });
        };
        clipList.SelectionChanged += (_, _) => { if (clipList.SelectedIndex >= 0 && clipList.SelectedIndex != selectedClip) { selectedClip = clipList.SelectedIndex; selectedChip = -1; Redraw(); } };
        addClip.Click += (_, _) =>
        {
            var name = Prompt("New clip", "Clip name (letters, numbers and _)", UniqueClip(clips.Count == 0 ? "idle" : "run"));
            if (name == null) return; name = name.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]{0,63}$")) { MessageBox.Show(window, "Use letters, numbers and _, starting with a letter.", "New clip"); return; }
            if (clips.Any(c => c.Name == name)) { MessageBox.Show(window, "There's already a clip called " + name + ".", "New clip"); return; }
            Edit(() => { clips.Add(new ClipDraft { Name = name }); selectedClip = clips.Count - 1; selectedChip = -1; });
        };
        renameClip.Click += (_, _) =>
        {
            if (Clip() is not ClipDraft clip) return;
            var name = Prompt("Rename clip", "Clip name", clip.Name)?.Trim(); if (name == null || name == clip.Name) return;
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]{0,63}$") || clips.Any(c => c.Name == name)) { MessageBox.Show(window, "Use a new name made of letters, numbers and _.", "Rename clip"); return; }
            Edit(() => { if (start == clip.Name) start = name; clip.Name = name; });
        };
        deleteClip.Click += (_, _) => { if (Clip() is ClipDraft clip) Edit(() => { clips.Remove(clip); selectedClip = Math.Min(selectedClip, clips.Count - 1); selectedChip = -1; }); };
        void MoveChip(int by) { if (Clip() is ClipDraft c && selectedChip >= 0 && selectedChip + by >= 0 && selectedChip + by < c.Frames.Count) Edit(() => { (c.Frames[selectedChip], c.Frames[selectedChip + by]) = (c.Frames[selectedChip + by], c.Frames[selectedChip]); selectedChip += by; }); }
        earlier.Click += (_, _) => MoveChip(-1); later.Click += (_, _) => MoveChip(1);
        void RemoveChip() { if (Clip() is ClipDraft c && selectedChip >= 0 && selectedChip < c.Frames.Count) Edit(() => { c.Frames.RemoveAt(selectedChip); selectedChip = Math.Min(selectedChip, c.Frames.Count - 1); }); }
        removeChip.Click += (_, _) => RemoveChip();
        clearChips.Click += (_, _) => { if (Clip() is ClipDraft c) Edit(() => { c.Frames.Clear(); selectedChip = -1; }); };
        window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { window.Close(); e.Handled = true; return; } if (e.Key == Key.Delete && e.OriginalSource is not TextBox) { RemoveChip(); e.Handled = true; } };
        fpsBox.LostKeyboardFocus += (_, _) =>
        {
            if (Clip() is not ClipDraft c) return;
            if (double.TryParse(fpsBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fps) && fps is >= 0.1 and <= 120) { if (fps != c.Fps) Edit(() => c.Fps = Math.Round(fps, 2)); }
            else fpsBox.Text = c.Fps.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        };
        once.Click += (_, _) => { if (Clip() is ClipDraft c) Edit(() => c.Once = once.IsChecked == true); };
        startBox.SelectionChanged += (_, _) => { if (!syncing && startBox.SelectedItem is string s) { var value = s == "(first clip)" ? "" : s; if (value != start) { start = value; changed = true; } } };
        void ApplySize(TextBox box, bool width, bool columns)
        {
            if (!int.TryParse(box.Text, out var n) || n < 1) { Redraw(); return; }
            if (sheet == null && columns) { Redraw(); return; }
            int size = columns ? Math.Max(1, (width ? sheet!.PixelWidth : sheet!.PixelHeight) / n) : Math.Min(n, PixelArt.MaxSize);
            if (width && size != fw) Edit(() => fw = size); else if (!width && size != fh) Edit(() => fh = size); else Redraw();
        }
        fwBox.LostKeyboardFocus += (_, _) => ApplySize(fwBox, true, false); fhBox.LostKeyboardFocus += (_, _) => ApplySize(fhBox, false, false);
        colsBox.LostKeyboardFocus += (_, _) => ApplySize(colsBox, true, true); rowsBox.LostKeyboardFocus += (_, _) => ApplySize(rowsBox, false, true);
        foreach (var box in new[] { fwBox, fhBox, colsBox, rowsBox }) box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { canvas.Focus(); Keyboard.ClearFocus(); } };

        void Reload(string path, int w, int h, int frames) { texture = AssetResource(path); fw = w; fh = h; changed = true; LoadSheet(); if (clips.Count == 0) { clips.Add(new ClipDraft { Name = "play", Frames = [.. Enumerable.Range(0, frames)] }); selectedClip = 0; } Redraw(); }
        choose.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = choose };
            foreach (var path in project.Assets.Keys.Where(p => p.EndsWith(".png") && p.Contains("/textures/")).OrderBy(p => p))
            {
                var item = new MenuItem { Header = path.Split('/').Last(), ToolTip = path };
                item.Click += (_, _) => { texture = AssetResource(path); if (SheetFrames(path) is var (sw, sh, _)) { fw = sw; fh = sh; } changed = true; LoadSheet(); Redraw(); };
                menu.Items.Add(item);
            }
            var import = new MenuItem { Header = "Import a PNG…" };
            import.Click += (_, _) => Guard(() => { var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "PNG image|*.png" }; if (dialog.ShowDialog(window) != true) return; var added = ImportImageFiles([dialog.FileName]); if (added.Count > 0) { texture = AssetResource(added[0]); changed = true; LoadSheet(); Redraw(); } });
            if (menu.Items.Count > 0) menu.Items.Add(new Separator()); menu.Items.Add(import);
            menu.IsOpen = true;
        };
        drawNew.Click += (_, _) => Guard(() => OpenPixelEditor(window, newWidth: fw <= PixelArt.MaxSize ? fw : 32, newHeight: fh <= PixelArt.MaxSize ? fh : 32, saved: Reload, canPlace: false));
        editPixels.Click += (_, _) => Guard(() =>
        {
            if (!TryTexture(texture, out var bytes)) return;
            OpenPixelEditor(window, bytes, TextureAssets.PathOf(project, texture), fw, fh, saved: Reload, canPlace: false);
        });

        // Preview: the selected clip, playing at its speed.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (_, _) => UpdatePreview();
        timer.Start(); window.Closed += (_, _) => timer.Stop();

        apply.Click += (_, _) =>
        {
            apply.Focus(); // Enter applies straight from a text box: commit what was typed there first
            var kept = clips.Where(c => c.Frames.Count > 0).ToList();
            string text = SpriteClips.Format(kept.Select(c => new SpriteClips.Clip(c.Name, [.. c.Frames], c.Fps, !c.Once)));
            try { SpriteClips.Parse(text); } catch (Exception ex) { MessageBox.Show(window, ex.Message, "Sprite sheet editor"); return; }
            Change();
            element.Texture = texture; element.FrameWidth = fw; element.FrameHeight = fh; element.Clips = text;
            element.Value = kept.Any(c => c.Name == start) ? start : "";
            applied = true; window.Close();
        };
        window.Closing += (_, e) =>
        {
            if (applied || !changed) return;
            if (MessageBox.Show(window, "Close without applying your changes?", "Sprite sheet editor", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) e.Cancel = true;
        };
        artEditorsOpen++; window.Closed += (_, _) => artEditorsOpen--;
        window.Closed += (_, _) => { if (applied) { Draw(); RefreshInspector(); Log($"Updated {element.Id}'s sprite sheet and clips."); } };
        LoadSheet(); Redraw();
        if (test != null) { window.Show(); test(window); } else window.ShowDialog();
    }
}
