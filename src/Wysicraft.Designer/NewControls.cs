using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Wysicraft.Core;
using Wysicraft.Models;
namespace Wysicraft.Designer;

// Canvas drawing for sprites, shapes and colliders.
public partial class MainWindow
{
    FrameworkElement RenderNewControl(Element e)
    {
        double w = Math.Max(1, e.Bounds.Width) * Zoom, h = Math.Max(1, e.Bounds.Height) * Zoom;
        var grid = new Grid();
        switch (e.Type)
        {
            case "slots":
            {
                // Minecraft's sunken slots; the player's slots show which inventory slots they are.
                var canvas = new Canvas { Width = w, Height = h, ClipToBounds = true };
                void Slot(double x, double y, double size, string label)
                {
                    double s = size * Zoom;
                    canvas.Children.Add(new System.Windows.Shapes.Rectangle { Width = s, Height = s, Fill = new SolidColorBrush(Color.FromRgb(0x8B, 0x8B, 0x8B)) }.At(x * Zoom, y * Zoom));
                    canvas.Children.Add(new System.Windows.Shapes.Polyline { Points = [new(0, s), new(0, 0), new(s, 0)], Stroke = new SolidColorBrush(Color.FromRgb(0x37, 0x37, 0x37)), StrokeThickness = Zoom }.At(x * Zoom, y * Zoom));
                    canvas.Children.Add(new System.Windows.Shapes.Polyline { Points = [new(s, 0), new(s, s), new(0, s)], Stroke = Brushes.White, StrokeThickness = Zoom }.At(x * Zoom, y * Zoom));
                    if (label.Length > 0) canvas.Children.Add(new TextBlock { Text = label, FontSize = Math.Max(6, 5.5 * Zoom), Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0x30, 0x30, 0x30)) }.At((x + 2) * Zoom, (y + 1) * Zoom));
                }
                if (e.SlotKind == "result") Slot((e.Bounds.Width - 26) / 2, (e.Bounds.Height - 26) / 2, 26, "");
                else for (int row = 0; row < Math.Clamp(e.Rows, 1, 6); row++) for (int column = 0; column < Math.Clamp(e.Columns, 1, 9); column++)
                    Slot(column * 18, row * 18, 18, e.SlotKind == "player" ? (e.SlotStart + row * e.Columns + column).ToString() : "");
                grid.Children.Add(canvas);
                break;
            }
            case "shape":
            {
                var path = new Path { Data = ShapeGeometry(e.Shape, w, h), Fill = SkinBrush(e), Stretch = Stretch.None, StrokeLineJoin = PenLineJoin.Round };
                if (e.BorderWidth > 0) { path.Stroke = Brush(e.BorderColor); path.StrokeThickness = Math.Clamp(e.BorderWidth, 0, 32) * Zoom; }
                grid.Children.Add(path);
                if (e.Text.Length > 0 && e.Text != "Shape") grid.Children.Add(new ContentPresenter { Content = StyledText(e), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                break;
            }
            case "sprite":
            {
                if (!TryTexture(e.Texture, out var png)) { grid.Children.Add(new TextBlock { Text = "▦ sprite", Foreground = Brushes.LightGray, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }); break; }
                var image = new Image { Stretch = Stretch.Fill }; RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                SetSpriteFrames(image, DecodeTexture(png), e); grid.Children.Add(image);
                break;
            }
            case "collider":
            {
                // Colliders are invisible in apps; on the canvas they show as a see-through hatched outline.
                var hatch = new DrawingBrush(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(70, 20, 200, 255)), null, new RectangleGeometry(new Rect(0, 0, 6, 6)))) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 8, 8), ViewportUnits = BrushMappingMode.Absolute };
                var outline = e.Collider switch
                {
                    "circle" => (Geometry)new EllipseGeometry(new Rect(0, 0, w, h)),
                    "polygon" when e.ColliderPoints.Count >= 3 => Polygon(e.ColliderPoints.Select(p => new System.Windows.Point(p.X * Zoom, p.Y * Zoom))),
                    _ => new RectangleGeometry(new Rect(0, 0, w, h))
                };
                grid.Children.Add(new Path { Data = outline, Fill = hatch, Stroke = new SolidColorBrush(Color.FromRgb(20, 200, 255)), StrokeThickness = 1.5, StrokeDashArray = [4, 2] });
                break;
            }
            case "particles":
            {
                // Emitters draw nothing in apps; on the canvas they show as a burst mark at the point they throw from,
                // so the spot can be seen and moved without guessing at an empty box.
                var ink = new SolidColorBrush(Color.FromRgb(120, 200, 255));
                var burst = new Path { Stroke = ink, StrokeThickness = 1.5, IsHitTestVisible = false, Data = Geometry.Parse("M8,0 L8,5 M8,11 L8,16 M0,8 L5,8 M11,8 L16,8 M2.5,2.5 L5.5,5.5 M10.5,10.5 L13.5,13.5 M13.5,2.5 L10.5,5.5 M5.5,10.5 L2.5,13.5"), Stretch = Stretch.Uniform, Margin = new Thickness(2) };
                grid.Children.Add(new Ellipse { Stroke = ink, StrokeThickness = 1, StrokeDashArray = [2, 2], Opacity = 0.7, IsHitTestVisible = false });
                grid.Children.Add(burst);
                if (e.Effect.Length > 0) grid.Children.Add(new TextBlock { Text = e.Effect, Foreground = ink, FontSize = 10, Margin = new Thickness(0, -13, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false });
                break;
            }
            case "tilemap":
            {
                var painted = TilemapImage(e);
                if (painted == null) { grid.Children.Add(new TextBlock { Text = "▦ tilemap", Foreground = Brushes.LightGray, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }); break; }
                var image = new Image { Source = painted, Stretch = Stretch.Fill }; RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                grid.Children.Add(image);
                break;
            }
            case "camera":
            {
                // Cameras draw nothing in apps; on the canvas they show as a dashed view frame with a zoom note.
                // Only the frame is solid, so clicks inside reach the controls underneath.
                var frame = new SolidColorBrush(Color.FromRgb(255, 176, 64));
                grid.Children.Add(new Rectangle { Stroke = frame, StrokeThickness = 2, StrokeDashArray = [5, 3], IsHitTestVisible = false });
                double zoom = Math.Min(ui.Size.Width / Math.Max(1, e.Bounds.Width), ui.Size.Height / Math.Max(1, e.Bounds.Height));
                grid.Children.Add(new TextBlock { Text = "📷 " + e.Id + (Math.Abs(zoom - 1) > 0.01 ? $"  ×{zoom:0.##}" : ""), Foreground = frame, FontSize = 11, Margin = new Thickness(4, 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false });
                break;
            }
        }
        return grid;
    }
    // A tilemap painted once into a single bitmap, rather than a control per tile: a full 256x256 map is 65,536 tiles,
    // and the canvas would not survive that as elements. The picture is kept until the map, its sheet or its size
    // changes, so dragging a map around costs nothing.
    readonly Dictionary<string, ImageSource?> tilemapPictures = [];
    ImageSource? TilemapImage(Element e)
    {
        string key = string.Join('\u0001', e.Texture, e.Tiles, e.Columns, e.Rows, e.TileWidth, e.TileHeight);
        if (tilemapPictures.TryGetValue(key, out var cached)) return cached;
        if (tilemapPictures.Count > 32) tilemapPictures.Clear();
        return tilemapPictures[key] = PaintTilemap(e);
    }
    ImageSource? PaintTilemap(Element e)
    {
        if (!TryTexture(e.Texture, out var png)) return null;
        var sheet = DecodeTexture(png);
        int tw = Math.Clamp(e.TileWidth, 1, sheet.PixelWidth), th = Math.Clamp(e.TileHeight, 1, sheet.PixelHeight);
        int across = Math.Max(1, sheet.PixelWidth / tw), down = Math.Max(1, sheet.PixelHeight / th);
        int columns = Math.Max(1, e.Columns), rows = Math.Max(1, e.Rows);
        // The picture is capped: a 256x256 map of 16px tiles would otherwise be a 4096x4096 bitmap, 67 MB of it.
        double scale = Math.Min(1, 2048.0 / Math.Max(columns * tw, rows * th));
        var grid = Tilemaps.Read(e.Tiles, columns, rows);
        var tiles = new Dictionary<int, CroppedBitmap>();
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            for (int r = 0; r < rows; r++) for (int c = 0; c < columns; c++)
            {
                int index = grid[r * columns + c];
                if (index < 0 || index >= across * down) continue;
                if (!tiles.TryGetValue(index, out var tile))
                {
                    tile = new CroppedBitmap(sheet, new Int32Rect(index % across * tw, index / across * th, tw, th));
                    tile.Freeze(); tiles[index] = tile;
                }
                dc.DrawImage(tile, new Rect(c * tw * scale, r * th * scale, tw * scale, th * scale));
            }
        var picture = new RenderTargetBitmap(Math.Max(1, (int)Math.Round(columns * tw * scale)), Math.Max(1, (int)Math.Round(rows * th * scale)), 96, 96, PixelFormats.Pbgra32);
        picture.Render(visual); picture.Freeze();
        return picture;
    }
    static Geometry Polygon(IEnumerable<System.Windows.Point> points)
    {
        var list = points.ToList(); var figure = new PathFigure { StartPoint = list[0], IsClosed = true, IsFilled = true };
        figure.Segments.Add(new PolyLineSegment(list.Skip(1), true)); var geometry = new PathGeometry([figure]); geometry.Freeze(); return geometry;
    }
    static Geometry ShapeGeometry(string shape, double w, double h) => Polygon(Shapes.Outline(shape).Select(p => new System.Windows.Point(p.X * w, p.Y * h)));

    // A sprite shows its current clip (Value), or its first clip, or frame 0; clips animate on the canvas too.
    void SetSpriteFrames(Image image, BitmapSource sheet, Element e)
    {
        int fw = Math.Clamp(e.FrameWidth, 1, sheet.PixelWidth), fh = Math.Clamp(e.FrameHeight, 1, sheet.PixelHeight), columns = Math.Max(1, sheet.PixelWidth / fw), count = columns * Math.Max(1, sheet.PixelHeight / fh);
        BitmapSource Frame(int index) { index = Math.Clamp(index, 0, count - 1); var crop = new CroppedBitmap(sheet, new Int32Rect(index % columns * fw, index / columns * fh, fw, fh)); crop.Freeze(); return crop; }
        Dictionary<string, SpriteClips.Clip> clips; try { clips = SpriteClips.Parse(e.Clips); } catch { clips = []; }
        var clip = clips.GetValueOrDefault(e.Value) ?? clips.Values.FirstOrDefault();
        if (clip == null) { image.Source = Frame(0); return; }
        image.Source = Frame(clip.Frames[0]);
        if (clip.Frames.Length < 2) return;
        var animation = new ObjectAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(clip.Frames.Length / clip.Fps), RepeatBehavior = clip.Loop ? RepeatBehavior.Forever : new RepeatBehavior(1), FillBehavior = FillBehavior.HoldEnd };
        for (int i = 0; i < clip.Frames.Length; i++) animation.KeyFrames.Add(new DiscreteObjectKeyFrame(Frame(clip.Frames[i]), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(i / clip.Fps))));
        image.BeginAnimation(Image.SourceProperty, animation);
    }
}

static class CanvasPlacement
{
    /// <summary>Places a control on a Canvas at x, y and returns it, for building small drawings inline.</summary>
    public static T At<T>(this T element, double x, double y) where T : UIElement { Canvas.SetLeft(element, x); Canvas.SetTop(element, y); return element; }
}

public partial class MainWindow
{
    /// <summary>The Crafting table stamp: a 3 × 3 crafting grid, an arrow, its result, and the player's inventory and
    /// hotbar below, where Minecraft's crafting table has them (176 × 166 GUI pixels from x, y). One Undo step.</summary>
    void AddCraftingTable(double x, double y)
    {
        if (ui.Elements.Any(e => e.Type == "slots" && e.SlotKind is "crafting" or "result")) throw new InvalidOperationException("This screen already has a crafting grid; a screen has at most one.");
        Change();
        Element Slots(string id, string kind, double left, double top, int columns, int rows, int start)
        {
            var e = new Element { Id = Unique(id), Type = "slots", SlotKind = kind, Columns = columns, Rows = rows, SlotStart = start, Text = "", FillEnabled = false, BorderWidth = 0, Bounds = new() { X = x + left, Y = y + top } };
            FitSlots(e); e.LayerGroup = isolatedGroup; ui.Elements.Add(e); return e;
        }
        Element Label(string id, string text, double left, double top, double width)
        {
            var e = new Element { Id = Unique(id), Type = "label", Text = text, Foreground = "#404040", FillEnabled = false, BorderWidth = 0, Bounds = new() { X = x + left, Y = y + top, Width = width, Height = 10 } };
            e.LayerGroup = isolatedGroup; ui.Elements.Add(e); return e;
        }
        var back = new Element { Id = Unique("crafting_background"), Type = "panel", Text = "", Background = "#C6C6C6", BorderColor = "#555555", BorderWidth = 1, CornerRadius = 3, Bounds = new() { X = x, Y = y, Width = 176, Height = 166 } };
        back.LayerGroup = isolatedGroup; ui.Elements.Add(back);
        Label("crafting_title", "Crafting", 28, 6, 80);
        var grid = Slots("crafting_grid", "crafting", 29, 16, 3, 3, 0);
        Label("crafting_arrow", "→", 90, 31, 20).FontScale = 2;
        var result = Slots("crafting_result", "result", 119, 30, 1, 1, 0);
        Label("inventory_title", "Inventory", 8, 72, 80);
        var inventory = Slots("inventory", "player", 7, 83, 9, 3, 9);
        var hotbar = Slots("hotbar", "player", 7, 141, 9, 1, 0);
        selected.Clear(); foreach (var e in new[] { back, grid, result, inventory, hotbar }) selected.Add(e.Id);
        Draw(); RefreshInspector();
        Log("Added a crafting table: a 3 × 3 grid with its result, and the player's inventory and hotbar. In Minecraft items move for real and recipes work; left-over items go back to the player when the screen closes.");
    }

    /// <summary>--smoke-slots: the Crafting table stamp on a fresh screen validates, refuses a second one, and exports a
    /// Minecraft pack (written to the output path) that the runtime's own tests then load (SlotTests).</summary>
    internal void VerifyCraftingTable(string output)
    {
        void Expect(bool ok, string what) { if (!ok) throw new Exception("Crafting table: " + what); }
        var screen = new UiDefinition { Id = "crafting", Title = "Crafting", Size = new() { Width = 200, Height = 180 }, Elements = [] };
        project.Screens.Add(screen); ui = screen; project.Manifest.Target = "minecraft";
        AddCraftingTable(8, 6);
        // A picture of the design surface with the stamp on it, beside the output.
        selected.Clear(); Draw(); UpdateLayout();
        var picture = new System.Windows.Media.Imaging.RenderTargetBitmap(Math.Max(1, (int)Surface.ActualWidth), Math.Max(1, (int)Surface.ActualHeight), 96, 96, PixelFormats.Pbgra32); picture.Render(Surface);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(picture)); using (var file = System.IO.File.Create(output + ".png")) png.Save(file);
        var slots = ui.Elements.Where(e => e.Type == "slots").ToList();
        Expect(slots.Count == 4 && slots.Single(e => e.SlotKind == "crafting").Bounds.Width == 54 && slots.Single(e => e.SlotKind == "result").Bounds.Width == 26, "a grid, a result and two player grids, sized to their slots");
        Expect(slots.Where(e => e.SlotKind == "player").Select(e => (e.SlotStart, e.Columns * e.Rows)).OrderBy(p => p).SequenceEqual([(0, 9), (9, 27)]), "the hotbar is slots 0-8 and the inventory 9-35");
        var problems = Wysicraft.Core.Validation.Errors(project).Where(i => i.Ui == "crafting").ToList();
        Expect(problems.Count == 0, "it validates: " + string.Join("; ", problems));
        bool refused = false; try { AddCraftingTable(8, 6); } catch (InvalidOperationException) { refused = true; }
        Expect(refused && ui.Elements.Count(e => e.Type == "slots") == 4, "a second crafting table on the same screen is refused");
        ui.Responsive = true; Expect(Wysicraft.Core.Validation.Errors(project).Any(i => i.Message.Contains("fixed layout")), "a responsive screen with slots is reported"); ui.Responsive = false;
        Wysicraft.Packaging.ProjectStore.Export(project, output);
        // A project copy that opens on this screen, for a look at the canvas (--smoke <project> <png>).
        project.Manifest.DefaultUi = "crafting"; project.Screens.Remove(screen); project.Screens.Insert(0, screen); Wysicraft.Packaging.ProjectStore.SaveProject(project, output + ".arcadia");
        dirty = false;
    }
}
