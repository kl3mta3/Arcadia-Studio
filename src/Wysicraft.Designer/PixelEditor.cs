using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Wysicraft.Core;
namespace Wysicraft.Designer;

/// <summary>Pixel editor: layered, animated pixel art with transparency, saved into the project as a PNG (several
/// frames become a sprite sheet; the layers are kept beside it for next time). Left click paints the left color,
/// right click the right one.</summary>
sealed class PixelEditor : Window
{
    public sealed record SaveRequest(PixelImage Sheet, int FrameWidth, int FrameHeight, int Frames, string Name, bool AsNew, string? Path, byte[]? Layers, double? AnimateFps = null);
    enum Tool { Pencil, Eraser, Fill, Line, Rectangle, Ellipse, Select, Wand, Move, Picker, Brush, Smooth }
    enum SelectMode { Replace, Add, Subtract }
    sealed record Snapshot(PixelDocument Doc, int Frame, int[] LayerPath, PixelMask? Selection);

    static readonly string PaletteFile = Wysicraft.Core.AppFolders.Path("pixel-palette.json");
    static readonly string[] DefaultPalette = [
        "#000000", "#222222", "#444444", "#666666", "#888888", "#AAAAAA", "#CCCCCC", "#FFFFFF",
        "#5A1E1E", "#A83232", "#E04848", "#F08C3C", "#F8C850", "#FFF0A0", "#6A4A2E", "#A8784A",
        "#1E4620", "#3C8C3C", "#78C850", "#C0E878", "#1A2A5A", "#2E5AAC", "#4C9AE0", "#9CD8F8",
        "#4A2A6A", "#8C4CB0", "#D07CD8", "#F4B8E0", "#2A4A4A", "#3C8C8C", "#78D0C0", "#C8F0E8"];
    const string ClipboardFormat = "Wysicraft.Pixels";
    static readonly Brush ActiveTool = new SolidColorBrush(Color.FromRgb(23, 107, 145)), ActiveToolBorder = new SolidColorBrush(Color.FromRgb(55, 148, 255));
    static readonly Brush Heading = Brushes.LightSkyBlue;

    readonly Func<SaveRequest, string> save;
    readonly Action<string, int, int, int>? addToScreen;
    PixelDocument doc;
    PixelLayer layer;
    int current, zoom = 8, brush = 1;
    string? assetPath; string name;
    Tool tool = Tool.Pencil;
    uint left = 0xFF000000, right = 0;
    bool mirror, grid = true, onion = true, filledShapes, blend, dirty, animate;
    readonly List<Snapshot> undo = [], redo = [];
    readonly List<uint> palette = [];
    readonly HashSet<PixelLayer> collapsed = [];

    // Selection (any shape), and a selection that has been picked up to move ("floating") but not put down yet:
    // its pixels and shape, the size of the selection's box, at floatX/floatY.
    PixelMask? selection;
    PixelImage? floating; PixelMask? floatMask; int floatX, floatY; PixelLayer? floatLayer; int floatFrame;
    PixelMask? selectBase; SelectMode selectMode; SelectMode? testMode; bool touchingOnly = true, diagonals;
    int tolerancePercent; // how different a color can be and still count as the same, for the wand and fill
    int Tolerance => (int)Math.Round(tolerancePercent * 2.55);

    // Gesture in progress.
    bool painting, moved; uint strokeColor; (int X, int Y) start, last, grab; PixelImage? strokeBase, strokeMask;
    // The soft brush and the smooth tool draw through a coverage map (PixelSoft): how far each pixel has been painted
    // in this stroke. softness is how much of the brush fades, 0–100.
    byte[]? strokeCoverage; int softness = 50;
    double Hardness => 1 - softness / 100.0;
    List<(PixelLayer Layer, PixelImage Base)>? moveBases;
    string notice = "";

    readonly Canvas stage = new() { SnapsToDevicePixels = true, Focusable = true, Cursor = Cursors.Cross, Background = Brushes.Transparent }; // a background, so the canvas itself gets the mouse (everything drawn on it ignores the mouse)
    readonly Rectangle checker = new() { IsHitTestVisible = false }, gridLines = new() { IsHitTestVisible = false }, hover = new() { Stroke = Brushes.DeepSkyBlue, StrokeThickness = 1, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly System.Windows.Shapes.Path antsLight = new() { Stroke = Brushes.White, StrokeThickness = 1, StrokeDashArray = [4, 4], IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly System.Windows.Shapes.Path antsDark = new() { Stroke = Brushes.Black, StrokeThickness = 1, StrokeDashArray = [4, 4], StrokeDashOffset = 4, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    PixelMask? outlined; int outlinedZoom;
    readonly Image picture = new() { Stretch = Stretch.Fill, IsHitTestVisible = false }, onionPicture = new() { Stretch = Stretch.Fill, Opacity = 0.3, IsHitTestVisible = false }, preview = new() { Width = 96, Height = 96, Stretch = Stretch.Uniform };
    readonly Line mirrorLine = new() { Stroke = Brushes.Orange, StrokeThickness = 1, StrokeDashArray = [4, 3], IsHitTestVisible = false };
    readonly ScrollViewer scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = new SolidColorBrush(Color.FromRgb(36, 40, 48)) };
    readonly TextBlock status = new() { Margin = new Thickness(8, 4, 8, 4) };
    readonly Dictionary<Tool, Button> toolButtons = [];
    readonly Border leftSwatch = new(), rightSwatch = new();
    readonly TextBox hex = new() { Width = 90 };
    readonly WrapPanel paletteView = new() { Width = 216 };
    readonly ListBox frameList = new() { Height = 72 }, layerList = new() { Height = 230 };
    readonly CheckBox mirrorBox = new() { Content = "Mirror (M)" }, gridBox = new() { Content = "Pixel grid", IsChecked = true }, onionBox = new() { Content = "Onion skin", IsChecked = true }, filledBox = new() { Content = "Filled shapes" }, touchingBox = new() { Content = "Wand: touching only", IsChecked = true }, diagonalBox = new() { Content = "Diagonals count as touching" }, blendBox = new() { Content = "Blend colors" }, playBox = new() { Content = "Play" }, animateBox = new() { Content = "Plays on its own" };
    readonly TextBox fpsBox = new() { Text = "8", Width = 36 };
    readonly Slider toleranceSlider = new() { Minimum = 0, Maximum = 100, Value = 0, IsSnapToTickEnabled = true, TickFrequency = 1, Width = 118 };
    readonly TextBlock toleranceLabel = new() { Width = 40, VerticalAlignment = VerticalAlignment.Center, Text = "0%" };
    readonly Slider brushSlider = new() { Minimum = 1, Maximum = PixelSoft.MaxSize, Value = 1, IsSnapToTickEnabled = true, TickFrequency = 1, Width = 200 };
    readonly Slider softSlider = new() { Minimum = 0, Maximum = 100, Value = 50, IsSnapToTickEnabled = true, TickFrequency = 5, Width = 200, ToolTip = "Soft brush and Smooth: how much of the brush fades out. 0% is a round brush with a clean, smooth outline; 100% fades all the way from the middle." };
    readonly TextBlock brushLabel = new() { Margin = new Thickness(0, 8, 0, 2) }, softLabel = new() { Margin = new Thickness(0, 6, 0, 2) };
    readonly Slider opacitySlider = new() { Minimum = 0, Maximum = 100, Value = 100, IsSnapToTickEnabled = true, TickFrequency = 1, Width = 150 };
    readonly TextBlock opacityLabel = new() { Width = 40, VerticalAlignment = VerticalAlignment.Center };
    readonly List<Button> layerButtons = [];
    readonly DispatcherTimer player = new();
    WriteableBitmap? bitmap;
    PixelImage shown;
    int playFrame; bool syncing, opacityEditing;
    (int X, int Y) hoverAt = (-1, -1);

    public PixelEditor(Window owner, PixelDocument document, string? assetPath, string name, Func<SaveRequest, string> save, Action<string, int, int, int>? addToScreen, bool animated = false, double fps = 8)
    {
        animate = animated; animateBox.IsChecked = animated; fpsBox.Text = fps.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        Owner = owner; doc = document; layer = doc.DrawingLayers().Last(); this.assetPath = assetPath; this.name = name; this.save = save; this.addToScreen = addToScreen;
        shown = new PixelImage(doc.Width, doc.Height);
        SetResourceReference(StyleProperty, typeof(Window)); // the app's dark window style only applies to Window itself, not subclasses
        Width = 1220; Height = 800; WindowStartupLocation = WindowStartupLocation.CenterOwner; MinWidth = 900; MinHeight = 600;
        zoom = FitZoom();
        LoadPalette();
        var root = new DockPanel(); Content = root;
        root.Children.Add(Dock(BuildToolbar(), System.Windows.Controls.Dock.Top));
        root.Children.Add(Dock(status, System.Windows.Controls.Dock.Bottom));
        root.Children.Add(Dock(Parts["frames"] = BuildFrames(), System.Windows.Controls.Dock.Bottom));
        root.Children.Add(Dock(Parts["tools"] = BuildLeft(), System.Windows.Controls.Dock.Left));
        root.Children.Add(Dock(Parts["layers"] = BuildRight(), System.Windows.Controls.Dock.Right));
        foreach (var el in new UIElement[] { checker, onionPicture, picture, gridLines, mirrorLine, antsLight, antsDark, hover }) stage.Children.Add(el);
        foreach (var image in new[] { picture, onionPicture, preview }) RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        checker.Fill = CheckerBrush();
        var centre = new Grid { Margin = new Thickness(24) }; centre.Children.Add(stage); stage.HorizontalAlignment = HorizontalAlignment.Center; stage.VerticalAlignment = VerticalAlignment.Center;
        scroller.Content = centre; root.Children.Add(scroller);
        stage.MouseLeftButtonDown += (_, e) => BeginStroke(e, false); stage.MouseRightButtonDown += (_, e) => BeginStroke(e, true);
        stage.MouseMove += (_, e) => MoveStroke(e); stage.MouseLeftButtonUp += (_, _) => EndStroke(); stage.MouseRightButtonUp += (_, _) => EndStroke();
        stage.MouseLeave += (_, _) => { hoverAt = (-1, -1); hover.Visibility = Visibility.Collapsed; UpdateStatus(); };
        scroller.PreviewMouseWheel += (_, e) => { if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return; ZoomAt(e.Delta > 0 ? 1 : -1, e.GetPosition(stage), e.GetPosition(scroller)); e.Handled = true; };
        PreviewKeyDown += Keys;
        player.Tick += (_, _) => { playFrame = (playFrame + 1) % doc.FrameCount; preview.Source = Bitmap(doc.Compose(playFrame)); };
        Closing += (_, e) => { if (!ConfirmDiscard("closing")) e.Cancel = true; };
        Closed += (_, _) => player.Stop();
        SelectTool(Tool.Pencil); RefreshColors(); RefreshPalette(); Layout(); RefreshAll();
        Loaded += (_, _) => stage.Focus();
    }
    int W => doc.Width; int H => doc.Height;
    int FitZoom() => Math.Clamp(520 / Math.Max(doc.Width, doc.Height), 1, 48);
    static T Dock<T>(T element, System.Windows.Controls.Dock side) where T : UIElement { DockPanel.SetDock(element, side); return element; }
    /// <summary>Parts of the editor an assistant can point at while teaching (tutorial target pixel:&lt;part&gt;).</summary>
    internal readonly Dictionary<string, FrameworkElement> Parts = new(StringComparer.OrdinalIgnoreCase);
    internal static readonly string[] PartNames = ["tools", "layers", "frames", "duplicate", "preview"];
    void UpdateTitle() => Title = "Pixel editor — " + (assetPath ?? name + " (not saved yet)") + (dirty ? " •" : "") + $"  ({W}×{H}, {doc.FrameCount} frame{(doc.FrameCount == 1 ? "" : "s")}, {doc.DrawingLayers().Count()} layer{(doc.DrawingLayers().Count() == 1 ? "" : "s")})";
    static TextBlock Title2(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Foreground = Heading, Margin = new Thickness(0, 12, 0, 4) };
    static TextBlock Hint(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.6, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) };

    // ---- Layout ----
    FrameworkElement BuildToolbar()
    {
        var bar = new WrapPanel { Margin = new Thickness(6, 6, 6, 2) };
        void Add(string text, string tip, Action action, bool primary = false)
        {
            var b = new Button { Content = text, ToolTip = tip, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(8, 3, 8, 3) };
            if (primary) b.FontWeight = FontWeights.SemiBold;
            b.Click += (_, _) => Guard(action); bar.Children.Add(b);
        }
        void Gap() => bar.Children.Add(new Border { Width = 12 });
        Add("New…", "Start a new picture", NewCanvas); Add("Open PNG…", "Open a PNG from your computer", OpenPng);
        Gap(); Add("Canvas size…", "Change the size of every frame and layer (the top-left corner stays put)", ResizeCanvas); Add("Split into frames…", "Cut this picture into frames of a size you choose (for sprite sheets)", SplitFrames);
        Gap(); Add("Undo", "Ctrl+Z", Undo); Add("Redo", "Ctrl+Y", Redo);
        Gap(); Add("Select all", "Ctrl+A", SelectAll); Add("Deselect", "Ctrl+D or Esc", Deselect);
        Add("Flip ↔", "Flip the selection, or the whole layer, left to right", () => Flip(PixelArt.FlipHorizontal, m => m.FlipHorizontal())); Add("Flip ↕", "Flip the selection, or the whole layer, upside down", () => Flip(PixelArt.FlipVertical, m => m.FlipVertical())); Add("Clear", "Erase the selection, or the whole layer, in this frame (Delete)", ClearArea);
        Add("Smooth edges", "Soften the jagged edges of the selection, or the whole layer, in this frame. Flat areas stay as they are; press it again for more.", SmoothEdges);
        Gap(); Add("Save to project", "Save into the project's images (Ctrl+S). Several frames are saved as one sprite sheet; your layers are kept for next time.", () => Save(false), true);
        Add("Save as new…", "Save as a new image in the project", () => Save(true)); Add("Export PNG…", "Save a PNG file on your computer", ExportPng);
        if (addToScreen != null) Add("Add to screen", "Save, then place it on the current screen (a Sprite control when it has several frames)", AddToScreen);
        return bar;
    }
    FrameworkElement BuildLeft()
    {
        var panel = new StackPanel { Margin = new Thickness(8), Width = 216 };
        panel.Children.Add(new TextBlock { Text = "TOOLS", FontWeight = FontWeights.SemiBold, Foreground = Heading, Margin = new Thickness(0, 0, 0, 4) });
        var tools = new UniformGrid { Columns = 2 };
        foreach (var (t, label, key, icon) in new[] { (Tool.Pencil, "Pencil", "B", "tool_pencil"), (Tool.Brush, "Soft brush", "A", "tool_brush"), (Tool.Eraser, "Eraser", "E", "tool_eraser"), (Tool.Smooth, "Smooth", "U", "tool_smooth"), (Tool.Fill, "Fill", "G", "tool_fill"), (Tool.Line, "Line", "L", "tool_line"), (Tool.Rectangle, "Rectangle", "R", "rectangle"), (Tool.Ellipse, "Ellipse", "O", "ellipse"), (Tool.Select, "Select", "S", "tool_select"), (Tool.Wand, "Magic wand", "W", "tool_wand"), (Tool.Move, "Move", "V", "tool_move"), (Tool.Picker, "Pick color", "I", "tool_picker") })
        {
            var b = new Button { Content = Icons.WithText(icon, label), ToolTip = $"{label} ({key})", Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(6, 4, 6, 4), HorizontalContentAlignment = HorizontalAlignment.Left };
            b.Click += (_, _) => Guard(() => SelectTool(t)); toolButtons[t] = b; tools.Children.Add(b);
        }
        panel.Children.Add(tools);
        void BrushText() { brushLabel.Text = $"Brush size: {brush}  ([ and ])"; softLabel.Text = $"Soft edge: {softness}%"; }
        brushSlider.ValueChanged += (_, _) => { brush = (int)brushSlider.Value; BrushText(); }; panel.Children.Add(brushLabel); panel.Children.Add(brushSlider);
        softSlider.ValueChanged += (_, _) => { softness = (int)softSlider.Value; BrushText(); }; panel.Children.Add(softLabel); panel.Children.Add(softSlider);
        BrushText();
        foreach (var box in new[] { mirrorBox, filledBox, blendBox, touchingBox, diagonalBox }) { box.Margin = new Thickness(0, 4, 0, 0); panel.Children.Add(box); }
        mirrorBox.ToolTip = "Everything you draw is copied onto the other half, mirrored left to right";
        blendBox.ToolTip = "See-through colors mix with what's already there, instead of replacing it";
        mirrorBox.Click += (_, _) => { mirror = mirrorBox.IsChecked == true; Layout(); }; filledBox.Click += (_, _) => filledShapes = filledBox.IsChecked == true; blendBox.Click += (_, _) => blend = blendBox.IsChecked == true; touchingBox.Click += (_, _) => touchingOnly = touchingBox.IsChecked == true; diagonalBox.Click += (_, _) => diagonals = diagonalBox.IsChecked == true;
        diagonalBox.Margin = new Thickness(18, 2, 0, 0); diagonalBox.ToolTip = "Wand: pixels that only meet at a corner count as touching, so one click takes a whole diagonal line.";
        touchingBox.ToolTip = "On: the wand picks only the area of that color touching where you click. Off: every pixel of that color.";
        var toleranceRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0), ToolTip = "Wand and fill: 0% takes exactly the color you click; higher also takes colors close to it (shading, anti-aliasing)." };
        toleranceRow.Children.Add(new TextBlock { Text = "Tolerance ", VerticalAlignment = VerticalAlignment.Center }); toleranceRow.Children.Add(toleranceSlider); toleranceRow.Children.Add(toleranceLabel);
        toleranceSlider.ValueChanged += (_, _) => { tolerancePercent = (int)toleranceSlider.Value; toleranceLabel.Text = $"{tolerancePercent}%"; };
        panel.Children.Add(toleranceRow);
        panel.Children.Add(Hint("Soft brush lays the color over what's there and fades at its edge; with the transparent color it erases softly. Smooth softens jagged edges where you drag (Smooth edges, at the top, does the whole layer or selection)."));
        panel.Children.Add(Hint("Alt+click picks a color. Shift keeps rectangles and ellipses square. Select a box or use the magic wand (Shift adds to the selection, Ctrl takes away), then Move or the arrow keys move it; Ctrl+C / X / V copy, cut and paste."));

        panel.Children.Add(Title2("COLORS"));
        var swatches = new StackPanel { Orientation = Orientation.Horizontal };
        FrameworkElement Swatch(Border inner, string label, bool isRight)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            var outer = new Border { Width = 44, Height = 44, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Background = CheckerBrush(6), Child = inner, Cursor = Cursors.Hand, ToolTip = "Click to choose the color" };
            outer.MouseLeftButtonUp += (_, _) => PickColor(isRight);
            stack.Children.Add(outer); stack.Children.Add(new TextBlock { Text = label, FontSize = 11, Opacity = 0.7 }); return stack;
        }
        swatches.Children.Add(Swatch(leftSwatch, "Left click", false)); swatches.Children.Add(Swatch(rightSwatch, "Right click", true));
        var swap = new Button { Content = "⇄", ToolTip = "Swap colors (X)", VerticalAlignment = VerticalAlignment.Top, Padding = new Thickness(6, 2, 6, 2) }; swap.Click += (_, _) => SwapColors(); swatches.Children.Add(swap);
        panel.Children.Add(swatches);
        var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        hexRow.Children.Add(new TextBlock { Text = "Left color ", VerticalAlignment = VerticalAlignment.Center }); hexRow.Children.Add(hex);
        hex.ToolTip = "#RRGGBB, or #AARRGGBB for see-through colors (#00000000 is fully transparent)";
        hex.LostKeyboardFocus += (_, _) => ApplyHex(); hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyHex(); stage.Focus(); } };
        panel.Children.Add(hexRow);
        var clearRight = new Button { Content = "Right click erases", ToolTip = "Make the right-click color transparent", Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        clearRight.Click += (_, _) => { right = 0; RefreshColors(); }; panel.Children.Add(clearRight);

        panel.Children.Add(Title2("PALETTE"));
        panel.Children.Add(paletteView);
        var paletteButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var addColor = new Button { Content = "Add left color", Margin = new Thickness(0, 0, 4, 0) }; addColor.Click += (_, _) => { if (!palette.Contains(left) && left >> 24 != 0) { palette.Add(left); SavePalette(); RefreshPalette(); } };
        var reset = new Button { Content = "Reset", ToolTip = "Go back to the standard palette" }; reset.Click += (_, _) => { palette.Clear(); palette.AddRange(DefaultPalette.Select(Parse)); SavePalette(); RefreshPalette(); };
        paletteButtons.Children.Add(addColor); paletteButtons.Children.Add(reset); panel.Children.Add(paletteButtons);
        panel.Children.Add(Hint("Left click a swatch for the left color, right click for the right one. Ctrl+click removes it."));
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    FrameworkElement BuildRight()
    {
        var panel = new StackPanel { Margin = new Thickness(8), Width = 250 };
        panel.Children.Add(new TextBlock { Text = "LAYERS", FontWeight = FontWeights.SemiBold, Foreground = Heading, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(layerList);
        layerList.SelectionChanged += (_, _) => { if (!syncing && layerList.SelectedItem is ListBoxItem { Tag: PixelLayer picked } && picked != layer) Guard(() => { Commit(); layer = picked; RefreshLayers(); UpdateStatus(); }); };
        var buttons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        void Add(string text, string tip, Action action, bool needsLayer = false, string? icon = null)
        {
            var b = new Button { Content = icon != null ? Icons.WithText(icon, text.TrimStart('+', ' ')) : text, ToolTip = tip, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(6, 2, 6, 2), Tag = needsLayer };
            b.Click += (_, _) => Guard(action); buttons.Children.Add(b); layerButtons.Add(b);
        }
        Add("+ Layer", "Add a layer above this one (inside it, for a group)", NewLayer, icon: "add"); Add("+ Group", "Add a group to gather layers in", NewGroup, icon: "folder");
        Add("Duplicate", "Copy this layer or group", DuplicateLayer, icon: "duplicate"); Add("Delete", "Delete this layer or group", DeleteLayer, icon: "delete");
        Add("▲", "Move up (drawn in front)", () => LayerOp(() => doc.Move(layer, 1))); Add("▼", "Move down (drawn behind)", () => LayerOp(() => doc.Move(layer, -1)));
        Add("Into group", "Move into the group next to it", () => LayerOp(() => doc.IntoGroup(layer))); Add("Out of group", "Move out of its group", () => LayerOp(() => doc.OutOfGroup(layer)));
        Add("Merge down", "Combine this layer with the one under it", MergeDown, true); Add("Rename…", "Rename (or double-click it)", RenameLayer);
        panel.Children.Add(buttons);
        var opacityRow = new StackPanel { Orientation = Orientation.Horizontal };
        opacityRow.Children.Add(new TextBlock { Text = "Opacity ", VerticalAlignment = VerticalAlignment.Center }); opacityRow.Children.Add(opacitySlider); opacityRow.Children.Add(opacityLabel);
        opacitySlider.ValueChanged += (_, _) =>
        {
            opacityLabel.Text = $"{opacitySlider.Value:0}%"; if (syncing) return;
            if (!opacityEditing) { Checkpoint(); opacityEditing = true; }
            layer.Opacity = opacitySlider.Value / 100; dirty = true; Render(); UpdateTitle();
        };
        opacitySlider.PreviewMouseUp += (_, _) => { if (opacityEditing) { opacityEditing = false; RefreshAll(); } };
        opacitySlider.LostKeyboardFocus += (_, _) => { if (opacityEditing) { opacityEditing = false; RefreshAll(); } };
        panel.Children.Add(opacityRow);
        panel.Children.Add(Hint("The eye hides a layer (hidden layers aren't saved into the PNG); the lock stops it being drawn on or moved. Selecting a group lets Move shift everything in it."));

        panel.Children.Add(Title2("VIEW"));
        foreach (var box in new[] { gridBox, onionBox }) { box.Margin = new Thickness(0, 0, 0, 3); panel.Children.Add(box); }
        onionBox.ToolTip = "Shows the previous frame faintly underneath, for animating";
        gridBox.Click += (_, _) => { grid = gridBox.IsChecked == true; Layout(); }; onionBox.Click += (_, _) => { onion = onionBox.IsChecked == true; Render(); };
        panel.Children.Add(Title2("PREVIEW"));
        panel.Children.Add(Parts["preview"] = new Border { Background = CheckerBrush(6), Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Left, Child = preview });
        var play = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        play.Children.Add(playBox); play.Children.Add(new TextBlock { Text = "  frames/s ", VerticalAlignment = VerticalAlignment.Center }); play.Children.Add(fpsBox);
        playBox.Click += (_, _) => UpdatePlayback(); fpsBox.LostKeyboardFocus += (_, _) => UpdatePlayback();
        panel.Children.Add(play);
        animateBox.Margin = new Thickness(0, 6, 0, 0); animateBox.Click += (_, _) => { animate = animateBox.IsChecked == true; dirty = true; UpdateTitle(); };
        animateBox.ToolTip = "Saves the frames as an animated image that plays by itself at this speed wherever it's used (Image controls, skins, Minecraft, web). Leave it off to use the frames as a sprite sheet, with clips you control.";
        panel.Children.Add(animateBox);
        panel.Children.Add(Hint("Off: a sprite sheet (Add to screen makes a Sprite with clips you control). On: an animated image that loops by itself at this speed."));
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    FrameworkElement BuildFrames()
    {
        var panel = new DockPanel { Margin = new Thickness(6, 2, 6, 2) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Button Add(string text, string tip, Action action) { var b = new Button { Content = text, ToolTip = tip, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(6, 2, 6, 2) }; b.Click += (_, _) => Guard(action); buttons.Children.Add(b); return b; }
        buttons.Children.Add(new TextBlock { Text = "FRAMES ", FontWeight = FontWeights.SemiBold, Foreground = Heading, VerticalAlignment = VerticalAlignment.Center });
        Add("Add", "Add an empty frame after this one", AddFrame);
        Parts["duplicate"] = Add("Duplicate", "Copy the selected frames (every layer), right after them", () => DuplicateFrames(false));
        Add("Delete", "Delete the selected frames", DeleteFrames);
        Add("Move left", "Move the selected frames one place earlier", () => MoveFrames(-1));
        Add("Move right", "Move the selected frames one place later", () => MoveFrames(1));
        DockPanel.SetDock(buttons, System.Windows.Controls.Dock.Left); panel.Children.Add(buttons);
        var row = new FrameworkElementFactory(typeof(VirtualizingStackPanel)); row.SetValue(VirtualizingStackPanel.OrientationProperty, Orientation.Horizontal);
        frameList.ItemsPanel = new ItemsPanelTemplate(row);
        ScrollViewer.SetVerticalScrollBarVisibility(frameList, ScrollBarVisibility.Disabled); ScrollViewer.SetHorizontalScrollBarVisibility(frameList, ScrollBarVisibility.Auto);
        // Click a frame to look at it; Shift or Ctrl+click picks several (the one clicked last is shown).
        frameList.SelectionMode = SelectionMode.Extended;
        frameList.ToolTip = "Click a frame to show it. Shift+click or Ctrl+click picks several. Right-click for more.";
        frameList.SelectionChanged += (_, e) =>
        {
            if (syncing) return;
            int shown = e.AddedItems.Count > 0 ? frameList.Items.IndexOf(e.AddedItems[^1]) : frameList.SelectedIndex;
            if (shown >= 0 && shown != current) Guard(() => GoToFrame(shown));
        };
        var frameMenu = new ContextMenu();
        MenuItem MenuEntry(string header, string tip, Action action) { var item = new MenuItem { Header = header, ToolTip = tip }; item.Click += (_, _) => Guard(action); return item; }
        var duplicate = new MenuItem { Header = "Duplicate" };
        duplicate.Items.Add(MenuEntry("Standard", "Copies of the selected frames, in the same order, right after them", () => DuplicateFrames(false)));
        duplicate.Items.Add(MenuEntry("Mirrored (plays back)", "Copies of the selected frames in reverse order right after them, so the animation plays forward and then back: 1 2 3 4 becomes 1 2 3 4 4 3 2 1", () => DuplicateFrames(true)));
        frameMenu.Items.Add(duplicate);
        frameMenu.Items.Add(MenuEntry("Add empty frame after", "An empty frame after the selected ones", AddFrame));
        frameMenu.Items.Add(new Separator());
        frameMenu.Items.Add(MenuEntry("Move left", "One place earlier", () => MoveFrames(-1)));
        frameMenu.Items.Add(MenuEntry("Move right", "One place later", () => MoveFrames(1)));
        frameMenu.Items.Add(new Separator());
        frameMenu.Items.Add(MenuEntry("Delete", "Delete the selected frames", DeleteFrames));
        frameList.ContextMenu = frameMenu;
        panel.Children.Add(frameList);
        return panel;
    }

    // ---- Which layer can be drawn on ----
    PixelImage Cell => layer.Cells[current];
    string? CannotDraw() =>
        layer.IsGroup ? "Pick a layer to draw on (groups only hold layers)." :
        doc.LockedHere(layer) ? $"\"{layer.Name}\" is locked." :
        !doc.ShownOnCanvas(layer) ? $"\"{layer.Name}\" is hidden. Show it to draw on it." : null;
    bool Ready() { var why = CannotDraw(); if (why == null) return true; notice = why; UpdateStatus(); return false; }
    // The cell is replaced by a copy before it changes, so undo snapshots keep the old pixels.
    void Own(PixelLayer target, int frame) => target.Cells[frame] = target.Cells[frame].Clone();

    // ---- Mouse ----
    (int X, int Y) PixelAt(Point p) => ((int)Math.Floor(p.X / zoom), (int)Math.Floor(p.Y / zoom));
    (int X, int Y) Clamp((int X, int Y) p) => (Math.Clamp(p.X, 0, W - 1), Math.Clamp(p.Y, 0, H - 1));
    PixelMask BoxBetween((int X, int Y) a, (int X, int Y) b) => PixelMask.Box(W, H, Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X) + 1, Math.Abs(a.Y - b.Y) + 1);
    SelectMode Mode() => testMode ?? (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? SelectMode.Add : Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? SelectMode.Subtract : SelectMode.Replace);
    // Shift adds to what's selected, Ctrl takes away; an empty result means nothing is selected.
    static PixelMask? Combined(PixelMask? before, PixelMask piece, SelectMode mode)
    {
        var result = mode switch { SelectMode.Add => before?.Union(piece) ?? piece, SelectMode.Subtract => before?.Subtract(piece), _ => piece };
        return result == null || result.IsEmpty ? null : result;
    }
    void BeginStroke(MouseButtonEventArgs e, bool isRight)
    {
        stage.Focus(); Guard(() => BeginAt(PixelAt(e.GetPosition(stage)), isRight, Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)));
        if (painting) stage.CaptureMouse();
        e.Handled = true;
    }
    void BeginAt((int X, int Y) at, bool isRight, bool pick = false)
    {
        notice = ""; moved = false;
        if (tool == Tool.Picker || pick)
        {
            if (shown.Contains(at.X, at.Y)) { if (isRight) right = shown.Get(at.X, at.Y); else left = shown.Get(at.X, at.Y); RefreshColors(); }
            return;
        }
        if (tool == Tool.Select) { Commit(); selectMode = Mode(); selectBase = selection; start = last = Clamp(at); selection = Combined(selectBase, BoxBetween(start, start), selectMode); painting = true; PlaceSelection(); return; }
        if (tool == Tool.Wand)
        {
            Commit(); var source = layer.IsGroup ? shown : Cell; // a layer's own pixels; for a group, what's shown
            if (!source.Contains(at.X, at.Y)) return;
            selection = Combined(selection, PixelWand.Select(source, at.X, at.Y, touchingOnly, Tolerance, diagonals), Mode()); PlaceSelection(); UpdateStatus(); return;
        }
        if (tool == Tool.Move)
        {
            if (selection != null)
            {
                if (floating == null) { if (!Ready()) return; Lift(); }
                grab = (at.X - floatX, at.Y - floatY); start = last = at; painting = true; return;
            }
            var targets = layer.Self().Where(l => !l.IsGroup && !doc.LockedHere(l)).ToList();
            if (targets.Count == 0) { notice = "Nothing here can be moved (locked or empty group)."; UpdateStatus(); return; }
            Checkpoint(); moveBases = targets.Select(l => (l, l.Cells[current])).ToList(); start = last = at; painting = true; return;
        }
        if (!Ready()) return;
        Commit(); Checkpoint(); Own(layer, current);
        bool soft = tool is Tool.Brush or Tool.Smooth;
        strokeBase = Cell.Clone(); strokeMask = blend && tool != Tool.Eraser && !soft ? new PixelImage(W, H) : null; strokeCoverage = soft ? new byte[W * H] : null;
        strokeColor = tool == Tool.Eraser ? 0 : isRight ? right : left;
        start = last = at; painting = true;
        Apply(at);
        if (tool == Tool.Fill) EndStroke();
    }
    void MoveStroke(MouseEventArgs e) { var at = PixelAt(e.GetPosition(stage)); ShowHover(at); MoveTo(at); }
    void MoveTo((int X, int Y) at)
    {
        if (!painting || at == last) return;
        moved = true;
        switch (tool)
        {
            case Tool.Select: selection = Combined(selectBase, BoxBetween(start, Clamp(at)), selectMode); PlaceSelection(); break;
            case Tool.Move when floating != null: floatX = at.X - grab.X; floatY = at.Y - grab.Y; selection = FloatSelection(); Render(); break;
            case Tool.Move when moveBases != null: foreach (var (l, b) in moveBases) l.Cells[current] = PixelArt.Shift(b, at.X - start.X, at.Y - start.Y); Render(); break;
            default: Apply(at); break;
        }
        last = at;
    }
    void EndStroke()
    {
        if (!painting) return;
        painting = false; if (stage.IsMouseCaptured) stage.ReleaseMouseCapture();
        switch (tool)
        {
            case Tool.Select: if (!moved && selectMode == SelectMode.Replace) selection = null; selectBase = null; PlaceSelection(); UpdateStatus(); return;
            case Tool.Move when moveBases != null:
                moveBases = null;
                if (last == start) DropCheckpoint(); else { dirty = true; RefreshFrame(current); }
                return;
            case Tool.Move: if (moved) { dirty = true; UpdateTitle(); } return;
        }
        // A click that changed nothing (same color, outside the canvas) doesn't leave an empty undo step.
        if (strokeBase != null && strokeBase.Pixels.AsSpan().SequenceEqual(Cell.Pixels)) DropCheckpoint();
        else { dirty = true; RefreshThumbnail(current); RefreshLayers(); UpdateTitle(); }
        strokeBase = strokeMask = null; strokeCoverage = null; Render();
    }
    int Mirrored(int x) => W - 1 - x;
    void Apply((int X, int Y) at)
    {
        var cell = Cell; var paint = strokeMask ?? cell; // with blending, strokes are drawn on their own and then laid over
        switch (tool)
        {
            case Tool.Pencil or Tool.Eraser:
                PixelArt.Line(paint, last.X, last.Y, at.X, at.Y, strokeColor, brush);
                if (mirror) PixelArt.Line(paint, Mirrored(last.X), last.Y, Mirrored(at.X), at.Y, strokeColor, brush);
                break;
            case Tool.Brush or Tool.Smooth:
                // Each move adds dabs to the stroke's coverage, then redraws the part they can have touched from the
                // picture as it was before the stroke.
                void Soft(int x0, int y0, int x1, int y1)
                {
                    PixelSoft.Line(strokeCoverage!, W, H, x0, y0, x1, y1, brush, Hardness);
                    var (l, t, r, bottom) = PixelSoft.Reach(W, H, x0, y0, x1, y1, brush);
                    if (tool == Tool.Brush) PixelSoft.Paint(strokeBase!, cell, strokeCoverage!, strokeColor, l, t, r, bottom);
                    else PixelSoft.Smooth(strokeBase!, cell, strokeCoverage, l, t, r, bottom);
                }
                Soft(last.X, last.Y, at.X, at.Y);
                if (mirror) Soft(Mirrored(last.X), last.Y, Mirrored(at.X), at.Y);
                break;
            case Tool.Fill:
                void FillAt(int x, int y)
                {
                    if (strokeMask == null) { PixelArt.Fill(cell, x, y, strokeColor, Tolerance); return; }
                    // Blended fill: find the area on the original, then mark it in the stroke.
                    var area = PixelWand.Select(strokeBase!, x, y, true, Tolerance);
                    for (int py = 0; py < H; py++) for (int px = 0; px < W; px++) if (area[px, py]) strokeMask.Pixels[py * W + px] = strokeColor;
                }
                FillAt(at.X, at.Y); if (mirror) FillAt(Mirrored(at.X), at.Y);
                break;
            case Tool.Line or Tool.Rectangle or Tool.Ellipse:
                if (strokeMask != null) Array.Clear(strokeMask.Pixels); else Array.Copy(strokeBase!.Pixels, cell.Pixels, cell.Pixels.Length);
                var end = at;
                if (tool != Tool.Line && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    int size = Math.Max(Math.Abs(at.X - start.X), Math.Abs(at.Y - start.Y));
                    end = (start.X + (at.X >= start.X ? size : -size), start.Y + (at.Y >= start.Y ? size : -size));
                }
                void Shape(int x0, int y0, int x1, int y1)
                {
                    if (tool == Tool.Line) PixelArt.Line(paint, x0, y0, x1, y1, strokeColor, brush);
                    else if (tool == Tool.Rectangle) PixelArt.Rectangle(paint, x0, y0, x1, y1, strokeColor, filledShapes, brush);
                    else PixelArt.Ellipse(paint, x0, y0, x1, y1, strokeColor, filledShapes);
                }
                Shape(start.X, start.Y, end.X, end.Y);
                if (mirror) Shape(Mirrored(start.X), start.Y, Mirrored(end.X), end.Y);
                break;
        }
        var b = strokeBase!.Pixels; var c = cell.Pixels;
        if (strokeMask != null) { var m = strokeMask.Pixels; for (int i = 0; i < c.Length; i++) c[i] = m[i] >> 24 != 0 ? PixelArt.Over(m[i], b[i]) : b[i]; }
        // With a selection, only the inside changes.
        if (selection is PixelMask s) for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (!s[x, y]) c[y * W + x] = b[y * W + x];
        Render();
    }
    void ShowHover((int X, int Y) at)
    {
        hoverAt = at;
        int size = tool is Tool.Pencil or Tool.Eraser or Tool.Brush or Tool.Smooth ? brush : 1, offset = -(size - 1) / 2;
        hover.Width = size * zoom + 1; hover.Height = size * zoom + 1; Canvas.SetLeft(hover, (at.X + offset) * zoom - 0.5); Canvas.SetTop(hover, (at.Y + offset) * zoom - 0.5);
        hover.Visibility = shown.Contains(at.X, at.Y) && tool is not (Tool.Move or Tool.Select) ? Visibility.Visible : Visibility.Collapsed;
        UpdateStatus();
    }
    void UpdateStatus()
    {
        var parts = new List<string>();
        if (notice.Length > 0) parts.Add("⚠ " + notice);
        if (shown.Contains(hoverAt.X, hoverAt.Y)) { uint c = shown.Get(hoverAt.X, hoverAt.Y); parts.Add($"x {hoverAt.X}, y {hoverAt.Y}   {(c >> 24 == 0 ? "transparent" : PixelArt.ToHex(c))}"); }
        else parts.Add($"{W} × {H}");
        if (selection is PixelMask s) { var box = s.Bounds; parts.Add($"selection {box.W} × {box.H}{(floating != null ? " (moving: Enter puts it down)" : "")}"); }
        parts.Add($"layer \"{layer.Name}\""); parts.Add($"zoom {zoom}×"); parts.Add($"frame {current + 1} of {doc.FrameCount}");
        status.Text = string.Join("   •   ", parts); if (notice.Length > 0) status.Foreground = Brushes.Orange; else status.ClearValue(TextBlock.ForegroundProperty);
    }

    // ---- Selection and moving ----
    void PlaceSelection()
    {
        if (selection == null) { antsLight.Visibility = antsDark.Visibility = Visibility.Collapsed; outlined = null; return; }
        antsLight.Visibility = antsDark.Visibility = Visibility.Visible;
        if (ReferenceEquals(selection, outlined) && outlinedZoom == zoom) return; // the outline only changes with the selection or zoom
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
            foreach (var (x1, y1, x2, y2) in selection.Outline()) { g.BeginFigure(new Point(x1 * zoom + 0.5, y1 * zoom + 0.5), false, false); g.LineTo(new Point(x2 * zoom + 0.5, y2 * zoom + 0.5), true, false); }
        geometry.Freeze(); antsLight.Data = antsDark.Data = geometry; outlined = selection; outlinedZoom = zoom;
    }
    // Picks the selected pixels up off the layer so they can move (one undo step until they're put down).
    void Lift()
    {
        var (x, y, w, h) = selection!.Bounds; Checkpoint(); Own(layer, current);
        floatMask = selection.Crop(x, y, w, h); floating = PixelArt.Crop(Cell, x, y, w, h);
        for (int row = 0; row < h; row++) for (int col = 0; col < w; col++)
            if (floatMask[col, row]) Cell.Set(x + col, y + row, 0); else floating.Pixels[row * w + col] = 0; // only the selected shape comes along
        floatX = x; floatY = y; floatLayer = layer; floatFrame = current; dirty = true;
    }
    // Puts moving pixels down where they are.
    void Commit()
    {
        if (floating == null || floatLayer == null) return;
        PixelArt.Paste(floating, floatLayer.Cells[floatFrame], floatX, floatY, blend);
        selection = FloatSelection(); if (selection.IsEmpty) selection = null;
        int frame = floatFrame; floating = null; floatMask = null; floatLayer = null; dirty = true; RefreshFrame(frame);
    }
    // After one frame changed: its thumbnail, the layer thumbnails and the canvas.
    void RefreshFrame(int frame) { RefreshThumbnail(frame); RefreshLayers(); Render(); UpdateTitle(); }
    // The moving pixels' shape, where they are now.
    PixelMask FloatSelection() => PixelMask.Place(floatMask ?? PixelMask.Full(floating!.Width, floating.Height), floatX, floatY, W, H);
    void SelectAll() { Commit(); selection = PixelMask.Full(W, H); PlaceSelection(); UpdateStatus(); }
    void Deselect() { Commit(); selection = null; PlaceSelection(); UpdateStatus(); }
    void Nudge(int dx, int dy)
    {
        if (selection != null)
        {
            if (floating == null) { if (!Ready()) return; Lift(); }
            floatX += dx; floatY += dy; selection = FloatSelection(); Render(); return;
        }
        var targets = layer.Self().Where(l => !l.IsGroup && !doc.LockedHere(l)).ToList(); if (targets.Count == 0) return;
        Checkpoint(); foreach (var l in targets) l.Cells[current] = PixelArt.Shift(l.Cells[current], dx, dy);
        dirty = true; RefreshFrame(current);
    }
    void Flip(Func<PixelImage, PixelImage> flip, Func<PixelMask, PixelMask> flipShape)
    {
        if (selection != null && floating == null) { if (!Ready()) return; Lift(); }
        if (floating != null) { floating = flip(floating); floatMask = flipShape(floatMask ?? PixelMask.Full(floating.Width, floating.Height)); selection = FloatSelection(); Render(); return; }
        var targets = layer.Self().Where(l => !l.IsGroup && !doc.LockedHere(l)).ToList(); if (targets.Count == 0) return;
        Checkpoint(); foreach (var l in targets) l.Cells[current] = flip(l.Cells[current]); dirty = true; RefreshFrame(current);
    }
    /// <summary>Smooth edges: the selection, or the whole layer, in this frame. One Undo step.</summary>
    void SmoothEdges()
    {
        Commit(); if (!Ready()) return;
        Checkpoint(); Own(layer, current);
        var smoothed = PixelSoft.SmoothEdges(Cell, selection);
        if (smoothed.Pixels.AsSpan().SequenceEqual(Cell.Pixels)) { DropCheckpoint(); notice = "There were no edges to smooth here."; UpdateStatus(); return; }
        Array.Copy(smoothed.Pixels, Cell.Pixels, Cell.Pixels.Length); dirty = true; RefreshFrame(current);
    }
    void ClearArea()
    {
        if (floating != null) { floating = null; floatMask = null; floatLayer = null; selection = null; dirty = true; RefreshFrame(current); return; } // it was already lifted off the layer
        if (selection is PixelMask s) { if (!Ready()) return; Checkpoint(); Own(layer, current); for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (s[x, y]) Cell.Set(x, y, 0); dirty = true; RefreshFrame(current); return; }
        var targets = layer.Self().Where(l => !l.IsGroup && !doc.LockedHere(l)).ToList(); if (targets.Count == 0) return;
        Checkpoint(); foreach (var l in targets) l.Cells[current] = new PixelImage(W, H); dirty = true; RefreshFrame(current);
    }
    void Copy(bool cut)
    {
        PixelImage? picked; (int X, int Y) at;
        if (floating != null) { picked = floating.Clone(); at = (floatX, floatY); }
        else
        {
            if (layer.IsGroup) { notice = "Pick a layer to copy from."; UpdateStatus(); return; }
            var s = selection ?? PixelMask.Full(W, H); var (x, y, w, h) = s.Bounds; picked = PixelArt.Crop(Cell, x, y, w, h); at = (x, y);
            for (int row = 0; row < h; row++) for (int col = 0; col < w; col++) if (!s[x + col, y + row]) picked.Pixels[row * w + col] = 0;
        }
        var data = new DataObject();
        var raw = new byte[8 + picked.Pixels.Length * 4]; BitConverter.TryWriteBytes(raw.AsSpan(0, 4), picked.Width); BitConverter.TryWriteBytes(raw.AsSpan(4, 4), picked.Height);
        System.Runtime.InteropServices.MemoryMarshal.AsBytes(picked.Pixels.AsSpan()).CopyTo(raw.AsSpan(8));
        data.SetData(ClipboardFormat, new MemoryStream(raw)); data.SetData(ClipboardFormat + ".At", $"{at.X},{at.Y}"); data.SetImage(Bitmap(picked));
        Clipboard.SetDataObject(data, true);
        if (cut) ClearArea();
    }
    void Paste()
    {
        Commit(); if (!Ready()) return;
        PixelImage? image = null; (int X, int Y) at = (0, 0);
        if (Clipboard.GetData(ClipboardFormat) is MemoryStream stream && stream.ToArray() is { Length: >= 8 } raw)
        {
            int w = BitConverter.ToInt32(raw, 0), h = BitConverter.ToInt32(raw, 4);
            if (w is >= 1 and <= PixelArt.MaxSheetSize && h is >= 1 and <= PixelArt.MaxSheetSize && raw.Length == 8 + w * h * 4)
            {
                image = new PixelImage(w, h); raw.AsSpan(8).CopyTo(System.Runtime.InteropServices.MemoryMarshal.AsBytes(image.Pixels.AsSpan()));
                if (Clipboard.GetData(ClipboardFormat + ".At") is string text && text.Split(',') is [var xs, var ys] && int.TryParse(xs, out var x) && int.TryParse(ys, out var y)) at = (x, y);
            }
        }
        else if (Clipboard.ContainsImage() && Clipboard.GetImage() is BitmapSource source)
        {
            var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            if (bgra.PixelWidth > PixelArt.MaxSheetSize || bgra.PixelHeight > PixelArt.MaxSheetSize) throw new InvalidOperationException("That picture is too large to paste.");
            image = new PixelImage(bgra.PixelWidth, bgra.PixelHeight); bgra.CopyPixels(image.Pixels, bgra.PixelWidth * 4, 0);
        }
        if (image == null) { notice = "There's no picture to paste."; UpdateStatus(); return; }
        if (at.X >= W || at.Y >= H || at.X + image.Width <= 0 || at.Y + image.Height <= 0) at = (0, 0);
        Checkpoint(); Own(layer, current);
        floating = image; floatMask = PixelMask.Full(image.Width, image.Height); floatX = at.X; floatY = at.Y; floatLayer = layer; floatFrame = current; selection = FloatSelection();
        dirty = true; SelectTool(Tool.Move); Render();
    }

    // ---- Undo ----
    int[] PathOf(PixelLayer target) { var path = new List<int>(); for (PixelLayer? l = target; l != null; l = doc.ParentOf(l)) path.Insert(0, doc.SiblingsOf(l).IndexOf(l)); return [.. path]; }
    PixelLayer Resolve(int[] path)
    {
        List<PixelLayer> list = doc.Layers; PixelLayer? found = null;
        foreach (int i in path) { if (list.Count == 0) break; found = list[Math.Clamp(i, 0, list.Count - 1)]; if (found.Children == null) break; list = found.Children; }
        return found ?? doc.DrawingLayers().Last();
    }
    Snapshot Capture() => new(doc.Snapshot(), current, PathOf(layer), selection);
    void Checkpoint() { undo.Add(Capture()); while (undo.Count > PixelArt.UndoSteps(doc.Width, doc.Height)) undo.RemoveAt(0); redo.Clear(); }
    void DropCheckpoint()
    {
        if (undo.Count == 0) return;
        var s = undo[^1]; undo.RemoveAt(undo.Count - 1);
        doc = s.Doc; current = s.Frame; layer = Resolve(s.LayerPath);
    }
    void Undo() { Commit(); if (undo.Count == 0) return; redo.Add(Capture()); Restore(undo[^1]); undo.RemoveAt(undo.Count - 1); }
    void Redo() { Commit(); if (redo.Count == 0) return; undo.Add(Capture()); Restore(redo[^1]); redo.RemoveAt(redo.Count - 1); }
    void Restore(Snapshot s)
    {
        bool resized = s.Doc.Width != W || s.Doc.Height != H;
        doc = s.Doc; current = Math.Min(s.Frame, doc.FrameCount - 1); layer = Resolve(s.LayerPath); selection = s.Selection; dirty = true;
        if (resized) Layout(); RefreshAll();
    }
    // Layer and frame changes: put down anything moving, then one undo step.
    void LayerOp(Func<bool> change)
    {
        Commit(); Checkpoint();
        bool changed; try { changed = change(); } catch { DropCheckpoint(); throw; } // a refused change leaves no empty undo step
        if (!changed) { DropCheckpoint(); return; }
        dirty = true; RefreshAll();
    }
    void FrameOp(Action change, IEnumerable<int>? pick = null) { Commit(); Checkpoint(); change(); dirty = true; RefreshAll(); if (pick != null) PickFrames(pick); }
    void PickFrames(IEnumerable<int> frames)
    {
        syncing = true;
        try { frameList.SelectedItems.Clear(); foreach (int f in frames.Append(current).Distinct()) if (f >= 0 && f < frameList.Items.Count) frameList.SelectedItems.Add(frameList.Items[f]); }
        finally { syncing = false; }
    }

    // ---- Layers ----
    string UniqueName(string stem) { int n = 1; while (doc.All().Any(l => l.Name == $"{stem} {n}")) n++; return $"{stem} {n}"; }
    void Place(PixelLayer item)
    {
        if (layer.IsGroup && !collapsed.Contains(layer)) { if (doc.Depth(layer) + 1 + PixelDocument.Nesting(item) > PixelDocument.MaxDepth) throw new InvalidOperationException($"Groups nest at most {PixelDocument.MaxDepth} deep."); layer.Children!.Add(item); }
        else doc.Insert(item, layer);
        layer = item;
    }
    void NewLayer() => LayerOp(() => { Place(doc.NewLayer(UniqueName("Layer"))); return true; });
    void NewGroup() => LayerOp(() => { Place(PixelDocument.NewGroup(UniqueName("Group"))); return true; });
    void DuplicateLayer() => LayerOp(() => { layer = doc.Duplicate(layer); return true; });
    void DeleteLayer() => LayerOp(() =>
    {
        var list = doc.SiblingsOf(layer); int i = list.IndexOf(layer); var parent = doc.ParentOf(layer);
        doc.Remove(layer);
        layer = list.Count > 0 ? list[Math.Max(0, i - 1)] : parent ?? doc.DrawingLayers().Last();
        return true;
    });
    void MergeDown() => LayerOp(() => { layer = doc.MergeDown(layer); return true; });
    void RenameLayer()
    {
        var text = MainWindow.Prompt("Rename", layer.IsGroup ? "Group name" : "Layer name", layer.Name)?.Trim(); if (string.IsNullOrEmpty(text) || text == layer.Name) return;
        LayerOp(() => { layer.Name = text.Length > 64 ? text[..64] : text; return true; });
    }
    void RefreshLayers()
    {
        syncing = true;
        try
        {
            layerList.Items.Clear(); ListBoxItem? chosen = null;
            foreach (var (l, depth) in doc.Rows(g => !collapsed.Contains(g)))
            {
                var row = new DockPanel { LastChildFill = true };
                Button Toggle(string icon, bool on, string tip, Action flip)
                {
                    var b = new Button { Content = Icons.Get(icon, size: 13), Opacity = on ? 1 : 0.4, ToolTip = tip, Style = (Style)FindResource("IconButton"), Padding = new Thickness(2), Margin = new Thickness(0, 0, 2, 0) };
                    b.Click += (_, e) => { Guard(flip); e.Handled = true; }; DockPanel.SetDock(b, System.Windows.Controls.Dock.Left); row.Children.Add(b); return b;
                }
                var target = l;
                Toggle(l.Visible ? "eye" : "eye-off", l.Visible, "Show / hide", () => { Commit(); target.Visible = !target.Visible; dirty = true; RefreshAll(); });
                Toggle(l.Locked ? "lock" : "unlock", l.Locked, "Lock (can't be drawn on or moved)", () => { Commit(); target.Locked = !target.Locked; dirty = true; RefreshLayers(); });
                var label = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(depth * 14, 0, 0, 0) };
                if (l.IsGroup)
                {
                    var expand = new Button { Content = collapsed.Contains(l) ? "▸" : "▾", Style = (Style)FindResource("IconButton"), Padding = new Thickness(3, 0, 3, 0), ToolTip = "Open / close the group" };
                    expand.Click += (_, e) => { if (!collapsed.Add(target)) collapsed.Remove(target); RefreshLayers(); e.Handled = true; };
                    label.Children.Add(expand); label.Children.Add(Icons.Get("folder"));
                }
                else
                {
                    var thumb = new Image { Source = Bitmap(l.Cells[current]), Width = 22, Height = 22, Stretch = Stretch.Uniform }; RenderOptions.SetBitmapScalingMode(thumb, BitmapScalingMode.NearestNeighbor);
                    label.Children.Add(new Border { Background = CheckerBrush(3), Child = thumb, Margin = new Thickness(0, 0, 2, 0) });
                }
                label.Children.Add(new TextBlock { Text = " " + l.Name + (l.Opacity < 1 ? $"  {l.Opacity * 100:0}%" : ""), VerticalAlignment = VerticalAlignment.Center, FontWeight = l.IsGroup ? FontWeights.SemiBold : FontWeights.Normal, Opacity = doc.ShownOnCanvas(l) ? 1 : 0.5 });
                row.Children.Add(label);
                var item = new ListBoxItem { Content = row, Tag = l, Padding = new Thickness(2, 1, 2, 1), HorizontalContentAlignment = HorizontalAlignment.Stretch };
                item.MouseDoubleClick += (_, e) => { if (e.OriginalSource is not Button) { layer = target; Guard(RenameLayer); } };
                layerList.Items.Add(item); if (l == layer) chosen = item;
            }
            layerList.SelectedItem = chosen; chosen?.BringIntoView();
            opacitySlider.Value = Math.Round(layer.Opacity * 100); opacityLabel.Text = $"{opacitySlider.Value:0}%";
            foreach (var b in layerButtons) if (b.Tag is true) b.IsEnabled = !layer.IsGroup;
        }
        finally { syncing = false; }
    }

    // ---- Frames ----
    void GoToFrame(int frame)
    {
        Commit(); current = Math.Clamp(frame, 0, doc.FrameCount - 1);
        // Keep a multi-frame selection when the shown frame is part of it.
        if (current >= frameList.Items.Count || !frameList.SelectedItems.Contains(frameList.Items[current])) { syncing = true; try { frameList.SelectedIndex = current; } finally { syncing = false; } }
        RefreshLayers(); Render();
    }
    // The frames picked in the strip (at least the one being shown), in order.
    List<int> PickedFrames()
    {
        var picked = frameList.SelectedItems.Cast<object>().Select(i => frameList.Items.IndexOf(i)).Where(i => i >= 0 && i < doc.FrameCount).Distinct().Order().ToList();
        if (!picked.Contains(current)) picked = [current];
        return picked;
    }
    void AddFrame() { int after = PickedFrames()[^1]; FrameOp(() => { doc.InsertFrame(after + 1); current = after + 1; }); }
    void DuplicateFrame() => DuplicateFrames(false);
    // Standard: copies of the picked frames right after them. Mirrored: the copies go in reverse order, so the
    // animation plays forward then back (1 2 3 4 becomes 1 2 3 4 4 3 2 1).
    void DuplicateFrames(bool mirrored)
    {
        var picked = PickedFrames(); var sources = mirrored ? Enumerable.Reverse(picked).ToList() : picked;
        if (doc.FrameCount + sources.Count > PixelArt.MaxFrames) throw new InvalidOperationException($"At most {PixelArt.MaxFrames} frames.");
        int at = picked[^1] + 1;
        FrameOp(() => { for (int k = 0; k < sources.Count; k++) doc.InsertFrame(at + k, sources[k]); current = at; }, Enumerable.Range(at, sources.Count));
    }
    void DeleteFrames()
    {
        var picked = PickedFrames();
        if (picked.Count >= doc.FrameCount) throw new InvalidOperationException("A picture needs at least one frame; leave one unselected.");
        FrameOp(() => { foreach (int f in Enumerable.Reverse(picked)) doc.RemoveFrame(f); current = Math.Min(picked[0], doc.FrameCount - 1); });
    }
    // Moves the picked frames one place left or right; the frames in the way hop over them.
    void MoveFrames(int by)
    {
        var picked = PickedFrames();
        if (by < 0 ? picked[0] == 0 : picked[^1] == doc.FrameCount - 1) return;
        FrameOp(() =>
        {
            foreach (int f in by < 0 ? picked : Enumerable.Reverse(picked)) doc.SwapFrames(f, f + by);
            current += by;
        }, picked.Select(f => f + by));
    }

    // ---- View ----
    void Layout()
    {
        double w = W * zoom, h = H * zoom;
        stage.Width = w; stage.Height = h;
        foreach (var el in new FrameworkElement[] { checker, picture, onionPicture, gridLines }) { el.Width = w; el.Height = h; }
        gridLines.Fill = grid && zoom >= 6 ? GridBrush(zoom) : null;
        mirrorLine.Visibility = mirror ? Visibility.Visible : Visibility.Collapsed; mirrorLine.X1 = mirrorLine.X2 = w / 2; mirrorLine.Y1 = 0; mirrorLine.Y2 = h;
        PlaceSelection();
    }
    void ZoomAt(int direction, Point onStage, Point inViewport)
    {
        int[] steps = [1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48];
        int index = Array.FindIndex(steps, s => s >= zoom); if (index < 0) index = steps.Length - 1;
        int next = steps[Math.Clamp(index + direction, 0, steps.Length - 1)]; if (next == zoom) return;
        double px = onStage.X / zoom, py = onStage.Y / zoom; zoom = next; Layout(); UpdateLayout();
        var origin = stage.TranslatePoint(new Point(0, 0), (UIElement)scroller.Content);
        scroller.ScrollToHorizontalOffset(origin.X + px * zoom - inViewport.X); scroller.ScrollToVerticalOffset(origin.Y + py * zoom - inViewport.Y);
        ShowHover(((int)px, (int)py));
    }
    void Render()
    {
        if (floating != null && floatLayer != null && floatFrame == current)
        {
            var withFloat = floatLayer.Cells[floatFrame].Clone(); PixelArt.Paste(floating, withFloat, floatX, floatY, blend);
            shown = doc.Compose(current, floatLayer, withFloat);
        }
        else shown = doc.Compose(current);
        if (bitmap == null || bitmap.PixelWidth != W || bitmap.PixelHeight != H) { bitmap = new WriteableBitmap(W, H, 96, 96, PixelFormats.Bgra32, null); picture.Source = bitmap; }
        bitmap.WritePixels(new Int32Rect(0, 0, W, H), shown.Pixels, W * 4, 0);
        onionPicture.Source = onion && current > 0 ? Bitmap(doc.Compose(current - 1)) : null;
        if (playBox.IsChecked != true) preview.Source = bitmap;
        PlaceSelection(); UpdateStatus();
    }
    void RefreshAll() { Layout(); RefreshFrames(); RefreshLayers(); Render(); UpdateTitle(); }
    static BitmapSource Bitmap(PixelImage image) { var b = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Pixels, image.Width * 4); b.Freeze(); return b; }
    void RefreshFrames()
    {
        syncing = true;
        try { frameList.Items.Clear(); for (int i = 0; i < doc.FrameCount; i++) frameList.Items.Add(Thumbnail(i)); frameList.SelectedIndex = current; }
        finally { syncing = false; }
        UpdatePlayback();
    }
    FrameworkElement Thumbnail(int index)
    {
        var image = new Image { Source = Bitmap(doc.Compose(index)), Width = 48, Height = 48, Stretch = Stretch.Uniform }; RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        var stack = new StackPanel(); stack.Children.Add(new Border { Background = CheckerBrush(4), Child = image }); stack.Children.Add(new TextBlock { Text = index.ToString(), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 10, Opacity = 0.7 });
        stack.ToolTip = $"Frame {index} (Sprite controls count frames from 0)"; return stack;
    }
    void RefreshThumbnail(int index)
    {
        if (index >= frameList.Items.Count) return;
        var picked = PickedFrames();
        syncing = true; try { frameList.Items[index] = Thumbnail(index); } finally { syncing = false; }
        PickFrames(picked);
    }
    void UpdatePlayback()
    {
        double fps = double.TryParse(fpsBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 0.5, 60) : 8;
        player.Interval = TimeSpan.FromMilliseconds(1000 / fps);
        if (playBox.IsChecked == true && doc.FrameCount > 1) player.Start(); else { player.Stop(); preview.Source = bitmap; }
    }
    static Brush CheckerBrush(double size = 8)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(230, 230, 230)), null, new RectangleGeometry(new Rect(0, 0, size * 2, size * 2))));
        var dark = new SolidColorBrush(Color.FromRgb(200, 200, 200));
        group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(0, 0, size, size))));
        group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(size, size, size, size))));
        var brush = new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, size * 2, size * 2), ViewportUnits = BrushMappingMode.Absolute }; brush.Freeze(); return brush;
    }
    static Brush GridBrush(int cell)
    {
        var line = new SolidColorBrush(Color.FromArgb(70, 60, 60, 60));
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, cell, cell))));
        group.Children.Add(new GeometryDrawing(line, null, new RectangleGeometry(new Rect(0, 0, cell, 1))));
        group.Children.Add(new GeometryDrawing(line, null, new RectangleGeometry(new Rect(0, 0, 1, cell))));
        var brush = new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, cell, cell), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, cell, cell), ViewboxUnits = BrushMappingMode.Absolute }; brush.Freeze(); return brush;
    }

    // ---- Colors ----
    static Color ToColor(uint c) => Color.FromArgb((byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c);
    static uint Parse(string text) => PixelArt.TryParseColor(text, out var c) ? c : 0xFF000000;
    void RefreshColors()
    {
        leftSwatch.Background = new SolidColorBrush(ToColor(left)); rightSwatch.Background = new SolidColorBrush(ToColor(right));
        leftSwatch.ToolTip = left >> 24 == 0 ? "Transparent" : PixelArt.ToHex(left); rightSwatch.ToolTip = right >> 24 == 0 ? "Transparent (erases)" : PixelArt.ToHex(right);
        if (!hex.IsKeyboardFocused) hex.Text = PixelArt.ToHex(left);
    }
    void ApplyHex() { if (PixelArt.TryParseColor(hex.Text, out var c)) { left = c; RefreshColors(); } else hex.Text = PixelArt.ToHex(left); }
    void SwapColors() { (left, right) = (right, left); RefreshColors(); }
    void PickColor(bool isRight)
    {
        uint initial = isRight ? right : left;
        new ColorPicker(this, PixelArt.ToHex(initial == 0 ? 0xFF000000 : initial), text => { if (PixelArt.TryParseColor(text, out var c)) { if (isRight) right = c; else left = c; RefreshColors(); } }).ShowDialog();
    }
    void RefreshPalette()
    {
        paletteView.Children.Clear();
        foreach (var color in palette.ToList())
        {
            var swatch = new Border { Width = 24, Height = 24, Margin = new Thickness(0, 0, 3, 3), Background = new SolidColorBrush(ToColor(color)), BorderBrush = Brushes.Black, BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = PixelArt.ToHex(color) };
            swatch.MouseLeftButtonUp += (_, _) => { if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { palette.Remove(color); SavePalette(); RefreshPalette(); } else { left = color; RefreshColors(); } };
            swatch.MouseRightButtonUp += (_, e) => { right = color; RefreshColors(); e.Handled = true; };
            paletteView.Children.Add(swatch);
        }
    }
    void LoadPalette()
    {
        try { if (File.Exists(PaletteFile)) palette.AddRange(Wysicraft.Models.Json.Read<List<string>>(File.ReadAllText(PaletteFile)).Take(128).Select(Parse)); } catch { palette.Clear(); }
        if (palette.Count == 0) palette.AddRange(DefaultPalette.Select(Parse));
    }
    void SavePalette()
    {
        try { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PaletteFile)!); File.WriteAllText(PaletteFile, Wysicraft.Models.Json.Write(palette.Select(PixelArt.ToHex).ToList())); } catch { }
    }

    // ---- Tools and keys ----
    void SelectTool(Tool t)
    {
        if (t != Tool.Move) Commit();
        tool = t; notice = "";
        foreach (var (key, button) in toolButtons)
            if (key == t) { button.Background = ActiveTool; button.BorderBrush = ActiveToolBorder; button.FontWeight = FontWeights.SemiBold; }
            else { button.ClearValue(BackgroundProperty); button.ClearValue(BorderBrushProperty); button.ClearValue(FontWeightProperty); }
        stage.Cursor = t switch { Tool.Picker => Cursors.Pen, Tool.Move => Cursors.SizeAll, _ => Cursors.Cross };
        UpdateStatus();
    }
    void Keys(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        int step = shift ? 10 : 1;
        switch (e.Key)
        {
            case Key.Z when ctrl && shift: case Key.Y when ctrl: Redo(); break;
            case Key.Z when ctrl: Undo(); break;
            case Key.S when ctrl: Guard(() => Save(false)); break;
            case Key.A when ctrl: SelectAll(); break;
            case Key.D when ctrl: Deselect(); break;
            case Key.C when ctrl: Guard(() => Copy(false)); break;
            case Key.X when ctrl: Guard(() => Copy(true)); break;
            case Key.V when ctrl: Guard(Paste); break;
            case Key.Escape: Deselect(); break;
            case Key.Enter or Key.Return: Commit(); break;
            case Key.Delete or Key.Back: Guard(ClearArea); break;
            case Key.Left or Key.Right or Key.Up or Key.Down when tool == Tool.Move || floating != null:
                Guard(() => Nudge(e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0, e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0)); break;
            case Key.B or Key.P when !ctrl: SelectTool(Tool.Pencil); break;
            case Key.E when !ctrl: SelectTool(Tool.Eraser); break;
            case Key.A when !ctrl: SelectTool(Tool.Brush); break;
            case Key.U when !ctrl: SelectTool(Tool.Smooth); break;
            case Key.G when !ctrl: SelectTool(Tool.Fill); break;
            case Key.L when !ctrl: SelectTool(Tool.Line); break;
            case Key.R when !ctrl: SelectTool(Tool.Rectangle); break;
            case Key.O when !ctrl: SelectTool(Tool.Ellipse); break;
            case Key.S when !ctrl: SelectTool(Tool.Select); break;
            case Key.V when !ctrl: SelectTool(Tool.Move); break;
            case Key.W when !ctrl: SelectTool(Tool.Wand); break;
            case Key.I when !ctrl: SelectTool(Tool.Picker); break;
            case Key.X when !ctrl: SwapColors(); break;
            case Key.M when !ctrl: mirror = !mirror; mirrorBox.IsChecked = mirror; Layout(); break;
            case Key.OemOpenBrackets: brushSlider.Value = Math.Max(1, brush - 1); break;
            case Key.OemCloseBrackets: brushSlider.Value = Math.Min(PixelSoft.MaxSize, brush + 1); break;
            case Key.OemPlus or Key.Add: ZoomAt(1, new Point(stage.Width / 2, stage.Height / 2), new Point(scroller.ViewportWidth / 2, scroller.ViewportHeight / 2)); break;
            case Key.OemMinus or Key.Subtract: ZoomAt(-1, new Point(stage.Width / 2, stage.Height / 2), new Point(scroller.ViewportWidth / 2, scroller.ViewportHeight / 2)); break;
            case Key.OemComma: if (current > 0) GoToFrame(current - 1); break;
            case Key.OemPeriod: if (current < doc.FrameCount - 1) GoToFrame(current + 1); break;
            default: return;
        }
        e.Handled = true;
    }

    // ---- Files ----
    void Guard(Action action) { try { action(); } catch (OperationCanceledException) { } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Pixel editor", MessageBoxButton.OK, MessageBoxImage.Warning); } }
    bool ConfirmDiscard(string doing)
    {
        Commit();
        if (!dirty) return true;
        var answer = MessageBox.Show(this, $"Save your pixel art to the project before {doing}?", "Pixel editor", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel) return false;
        if (answer == MessageBoxResult.Yes) { try { Save(false); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Pixel editor"); return false; } }
        return !dirty || answer == MessageBoxResult.No;
    }
    static (int W, int H)? AskSize(string title, string label, string initial)
    {
        var text = MainWindow.Prompt(title, label, initial); if (text == null) return null;
        var parts = text.ToLowerInvariant().Replace(" ", "").Split('x', '×', ',');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int w) || !int.TryParse(parts[1], out int h) || w < 1 || h < 1 || w > PixelArt.MaxSize || h > PixelArt.MaxSize)
            throw new InvalidOperationException($"Write the size as width × height, each 1–{PixelArt.MaxSize}, for example 32x32.");
        return (w, h);
    }
    void Replace(PixelDocument next, string? path, string newName, bool isDirty)
    {
        undo.Clear(); redo.Clear(); floating = null; floatMask = null; floatLayer = null; selection = null; collapsed.Clear();
        doc = next; layer = doc.DrawingLayers().Last(); current = 0; assetPath = path; name = newName; dirty = isDirty;
        zoom = FitZoom(); RefreshAll();
    }
    void NewCanvas()
    {
        if (!ConfirmDiscard("starting a new picture")) return;
        if (AskSize("New pixel art", "Size in pixels (width × height)", $"{W}x{H}") is not var (w, h)) return;
        var next = new PixelDocument(w, h); next.Layers.Add(next.NewLayer("Layer 1"));
        Replace(next, null, "pixel_art", false);
    }
    void OpenPng()
    {
        if (!ConfirmDiscard("opening another picture")) return;
        var dialog = new OpenFileDialog { Filter = "PNG image|*.png" }; if (dialog.ShowDialog(this) != true) return;
        var image = MainWindow.DecodePixels(File.ReadAllBytes(dialog.FileName));
        List<PixelImage> opened;
        if (image.Width > PixelArt.MaxSize || image.Height > PixelArt.MaxSize)
        {
            if (AskSize("Large image", $"This picture is {image.Width}×{image.Height}. Cut it into frames of (width × height):", "32x32") is not var (fw, fh)) return;
            opened = PixelArt.Unpack(image, fw, fh);
        }
        else opened = [image];
        Replace(PixelDocument.FromFrames(opened), null, SafeName(System.IO.Path.GetFileNameWithoutExtension(dialog.FileName)), true);
    }
    void ResizeCanvas()
    {
        if (AskSize("Canvas size", "New size (width × height). The top-left corner stays where it is.", $"{W}x{H}") is not var (w, h)) return;
        FrameOp(() => { doc.Resize(w, h); selection = null; });
    }
    void SplitFrames()
    {
        if (doc.FrameCount > 1) throw new InvalidOperationException("Splitting works on a single picture (a sprite sheet). This one already has frames.");
        if (AskSize("Split into frames", "Frame size (width × height). Frames are read left to right, then top to bottom.", $"{Math.Min(W, H)}x{Math.Min(W, H)}") is not var (w, h)) return;
        FrameOp(() => { doc.SplitIntoFrames(w, h); current = 0; selection = null; });
        zoom = FitZoom(); RefreshAll();
    }
    internal static string SafeName(string text) { var s = System.Text.RegularExpressions.Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9_-]", "_").Trim('_'); return s.Length == 0 ? "pixel_art" : s[..Math.Min(s.Length, 48)]; }
    // One plain layer needs nothing beyond the PNG; anything more is kept beside it.
    // (Several frames are always noted beside it too, so Assets knows the picture is a sprite sheet.)
    bool Plain() => doc.FrameCount == 1 && doc.Layers is [{ IsGroup: false, Visible: true, Locked: false, Opacity: 1 }];
    double Fps() => double.TryParse(fpsBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 0.5, 20) : 8;
    void Save(bool asNew)
    {
        string saveName = name;
        if (asNew || assetPath == null)
        {
            var typed = MainWindow.Prompt("Save to project", "Image name (lowercase letters, numbers, _ and -)", name); if (typed == null) return;
            saveName = SafeName(typed);
        }
        SaveNamed(saveName, asNew);
    }
    void SaveNamed(string saveName, bool asNew)
    {
        Commit();
        var sheet = PixelArt.Pack(doc.ComposeAll());
        assetPath = save(new SaveRequest(sheet, W, H, doc.FrameCount, saveName, asNew || assetPath == null, assetPath, Plain() ? null : doc.Save(), animate && doc.FrameCount > 1 ? Fps() : null));
        name = saveName; dirty = false; UpdateTitle();
    }
    void ExportPng()
    {
        Commit();
        var dialog = new SaveFileDialog { Filter = "PNG image|*.png", FileName = name + ".png" }; if (dialog.ShowDialog(this) != true) return;
        File.WriteAllBytes(dialog.FileName, MainWindow.EncodePixels(PixelArt.Pack(doc.ComposeAll())));
    }
    void AddToScreen()
    {
        if (dirty || assetPath == null) Save(false);
        if (assetPath == null) return;
        addToScreen!(assetPath, W, H, animate ? 1 : doc.FrameCount); // an animated image plays itself in an Image control
    }

    // ---- Scripted checks (editor smoke test): the same paths the mouse, keys and buttons use ----
    internal PixelDocument TestDoc => doc;
    internal PixelImage TestShown(int frame) => doc.Compose(frame);
    internal PixelImage TestDisplayed => shown; // what the canvas shows, including a selection still being moved
    internal PixelImage TestCell(string layerName, int frame) => doc.All().First(l => l.Name == layerName).Cells[frame];
    internal int TestCurrent => current;
    internal string TestLayer => layer.Name;
    internal string? TestAssetPath => assetPath;
    internal bool TestDirty => dirty;
    internal bool TestFloating => floating != null;
    internal string TestNotice => notice;
    internal void TestTool(string tool) => SelectTool(Enum.Parse<Tool>(tool));
    internal void TestColors(uint leftColor, uint rightColor) { left = leftColor; right = rightColor; RefreshColors(); }
    internal void TestMirror(bool on) { mirror = on; mirrorBox.IsChecked = on; Layout(); }
    internal void TestFilled(bool on) { filledShapes = on; filledBox.IsChecked = on; }
    internal void TestBlend(bool on) { blend = on; blendBox.IsChecked = on; }
    internal void TestBrush(int size, int softPercent) { brushSlider.Value = size; softSlider.Value = softPercent; }
    internal void TestSmoothEdges() => SmoothEdges();
    internal (double Maximum, string Size, string Soft) TestBrushPanel => (brushSlider.Maximum, brushLabel.Text, softLabel.Text);
    internal bool TestHasTool(string label) => toolButtons.Values.Any(b => (b.ToolTip as string ?? "").StartsWith(label + " ("));
    internal void TestStroke(bool isRight, params (int X, int Y)[] points) { BeginAt(points[0], isRight); foreach (var p in points.Skip(1)) MoveTo(p); EndStroke(); }
    internal void TestUndo() => Undo();
    internal void TestRedo() => Redo();
    internal void TestAddFrame() => AddFrame();
    internal void TestDuplicateFrame() => DuplicateFrame();
    internal void TestDiscard() { dirty = false; Close(); }
    /// <summary>Plays the frames in the Preview box (its Play tick box), as an assistant drawing live does when it's done.</summary>
    internal void TestPlay(bool on) { playBox.IsChecked = on; UpdatePlayback(); }
    internal void TestAnimate(bool on, double fps) { animate = on; animateBox.IsChecked = on; fpsBox.Text = fps.ToString(System.Globalization.CultureInfo.InvariantCulture); }
    internal void TestPickFrames(params int[] frames) { current = frames[^1]; RefreshFrames(); PickFrames(frames); Render(); }
    internal void TestDuplicateFrames(bool mirrored) => DuplicateFrames(mirrored);
    internal void TestMoveFrames(int by) => MoveFrames(by);
    internal void TestDeleteFrames() => DeleteFrames();
    internal List<int> TestPicked => PickedFrames();
    internal bool TestOnion => onion && onionPicture.Source != null;
    internal void TestFrame(int frame) => GoToFrame(frame);
    internal void TestFlip() => Flip(PixelArt.FlipHorizontal, m => m.FlipHorizontal());
    internal void TestSelectMode(string? mode) => testMode = mode == null ? null : Enum.Parse<SelectMode>(mode);
    internal void TestTouching(bool on) { touchingOnly = on; touchingBox.IsChecked = on; }
    internal void TestTolerance(int percent) => toleranceSlider.Value = percent;
    internal void TestDiagonals(bool on) { diagonals = on; diagonalBox.IsChecked = on; }
    // Hit-tests the window at the middle of the canvas the way a real click is routed, so a canvas that lets
    // clicks fall through (and so never draws) is caught.
    internal bool TestCanvasTakesMouse()
    {
        UpdateLayout();
        var middle = stage.TranslatePoint(new Point(stage.ActualWidth / 2, stage.ActualHeight / 2), this);
        // InputHitTest is what real mouse input uses (it skips anything that ignores the mouse); VisualTreeHelper.HitTest doesn't.
        for (DependencyObject? d = InputHitTest(middle) as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d)) if (d == stage) return true;
        return false;
    }
    internal PixelMask? TestSelection => selection;
    internal void TestSave(string saveName) => SaveNamed(saveName, assetPath == null);
    /// <summary>After an assistant saved what it drew here (live pixel_art): the editor now edits that image, saved.</summary>
    internal void ShowSaved(string path, string savedName) { assetPath = path; name = savedName; dirty = false; notice = "Saved to the project as " + System.IO.Path.GetFileName(path) + "."; UpdateTitle(); UpdateStatus(); }
    internal void TestAddToScreen() => AddToScreen();
    internal void TestPickLayer(string layerName) { Commit(); layer = doc.All().First(l => l.Name == layerName); RefreshLayers(); }
    internal void TestNewLayer() => NewLayer();
    internal void TestNewGroup() => NewGroup();
    internal void TestIntoGroup() => LayerOp(() => doc.IntoGroup(layer));
    internal void TestOutOfGroup() => LayerOp(() => doc.OutOfGroup(layer));
    internal void TestMergeDown() => MergeDown();
    internal void TestDeleteLayer() => DeleteLayer();
    internal void TestOpacity(double percent) { opacitySlider.Value = percent; opacityEditing = false; RefreshAll(); }
    internal void TestToggleVisible(string layerName) { var l = doc.All().First(x => x.Name == layerName); l.Visible = !l.Visible; dirty = true; RefreshAll(); }
    internal void TestDeselect() => Deselect();
    internal void TestCopy(bool cut) => Copy(cut);
    internal void TestPaste() => Paste();
    internal void TestNudge(int dx, int dy) => Nudge(dx, dy);
    internal void TestCommit() => Commit();
    internal void TestClear() => ClearArea();
    internal int TestLayerRows => layerList.Items.Count;
    internal int TestUndoCount => undo.Count;
}
