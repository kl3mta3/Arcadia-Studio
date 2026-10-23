using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Wysicraft.Core;
using Wysicraft.Models;
namespace Wysicraft.Designer;

// Tints and the Canvas control on the design surface, drawn the way the web runtime draws them.
public partial class MainWindow
{
    /// <summary>The controls a tint changes: the ones that are a picture.</summary>
    internal static bool Tintable(Element e) => e.Type is "image" or "texture_region" or "sprite" or "tilemap" or "canvas";

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BitmapSource, Dictionary<string, BitmapSource>> TintedPictures = new();
    /// <summary>A picture multiplied by a tint (#RRGGBB) with its transparency kept, as the runtime's batch does: white
    /// leaves it as drawn. The picture itself for no tint, or for one that isn't #RRGGBB. Kept per picture and tint.</summary>
    internal static BitmapSource Tinted(BitmapSource source, string tint)
    {
        if (!TintRgb(tint, out byte r, out byte g, out byte b)) return source;
        var byTint = TintedPictures.GetOrCreateValue(source);
        if (byTint.TryGetValue(tint, out var made)) return made;
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4; var pixels = new byte[stride * h];
        bgra.CopyPixels(pixels, stride, 0);
        // Premultiplied, so multiplying the colour channels alone is the same as tinting and then premultiplying.
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = (byte)(pixels[i] * b / 255); pixels[i + 1] = (byte)(pixels[i + 1] * g / 255); pixels[i + 2] = (byte)(pixels[i + 2] * r / 255); }
        var tinted = BitmapSource.Create(w, h, source.DpiX, source.DpiY, PixelFormats.Pbgra32, null, pixels, stride); tinted.Freeze();
        if (byTint.Count >= 16) byTint.Clear();
        return byTint[tint] = tinted;
    }
    static bool TintRgb(string tint, out byte r, out byte g, out byte b)
    {
        r = g = b = 255;
        if (tint is not { Length: 7 } || tint[0] != '#' || !int.TryParse(tint.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out int n)) return false;
        r = (byte)(n >> 16); g = (byte)(n >> 8); b = (byte)n; return true;
    }

    /// <summary>A Canvas control: the picture it starts as, stretched with hard edges, or (with none) a dashed frame
    /// naming its pixel size, since an empty canvas is see-through in the game until a script draws on it.</summary>
    FrameworkElement RenderCanvasControl(Element e)
    {
        var grid = new Grid();
        if (TryTexture(e.Texture, out var png))
        {
            var image = new Image { Source = Tinted(DecodeTexture(png), e.Tint), Stretch = Stretch.Fill }; RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            grid.Children.Add(image);
            return grid;
        }
        var ink = new SolidColorBrush(Color.FromRgb(150, 210, 140));
        grid.Children.Add(new Rectangle { Stroke = ink, StrokeThickness = 1, StrokeDashArray = [3, 2], Opacity = 0.8, IsHitTestVisible = false });
        grid.Children.Add(new TextBlock { Text = $"▦ canvas {e.Columns} × {e.Rows}", Foreground = ink, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false });
        return grid;
    }

    /// <summary>Properties → Tint: a #RRGGBB box (empty is none), the colour wheel, and None.</summary>
    void TintField(StackPanel panel, Element e)
    {
        var row = new DockPanel { ToolTip = "The picture is multiplied by this colour: white leaves it as drawn, so a tint darkens or shifts colours and never brightens them. Draw the parts you will recolour in white or light grey. Web and desktop only." };
        row.Children.Add(new TextBlock { Text = "Tint", Width = 90, Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Center });
        var hex = new TextBox { Text = e.Tint, MinWidth = 85, ToolTip = "#RRGGBB, or empty for none" };
        var none = new Button { Content = "None", Padding = new Thickness(6, 1, 6, 1), ToolTip = "No tint: the picture as drawn" };
        var wheel = new Button { Content = "◉", Width = 32, Padding = new Thickness(2), Background = e.Tint.Length > 0 ? Brush(e.Tint) : Brushes.White, ToolTip = "Open color wheel" };
        DockPanel.SetDock(none, Dock.Right); DockPanel.SetDock(wheel, Dock.Right);
        row.Children.Add(none); row.Children.Add(wheel); row.Children.Add(hex); panel.Children.Add(row);
        bool transaction = false, updating = false;
        void Apply(string tint)
        {
            if (tint == e.Tint) return;
            if (!transaction) { Change(); transaction = true; }
            e.Tint = tint; wheel.Background = tint.Length > 0 ? Brush(tint) : Brushes.White; Draw();
        }
        void Show(string tint) { updating = true; hex.Text = tint; updating = false; }
        hex.TextChanged += (_, _) =>
        {
            if (updating) return;
            string text = hex.Text.Trim();
            if (text.Length == 0 || System.Text.RegularExpressions.Regex.IsMatch(text, "^#[0-9a-fA-F]{6}$")) { hex.BorderBrush = Brushes.Gray; Apply(text.ToUpperInvariant()); }
            else hex.BorderBrush = Brushes.IndianRed;
        };
        hex.LostKeyboardFocus += (_, _) => { Show(e.Tint); transaction = false; };
        none.Click += (_, _) => { transaction = false; Apply(""); Show(""); transaction = false; };
        wheel.Click += (_, _) =>
        {
            transaction = false;
            // A tint has no transparency of its own (opacity does that), so the wheel's alpha is left out.
            var picker = new ColorPicker(this, e.Tint.Length > 0 ? e.Tint : "#FFFFFF", color => { string tint = color.Length == 9 ? "#" + color[3..] : color; Apply(tint); Show(tint); });
            picker.ShowDialog(); transaction = false;
        };
    }

    /// <summary>--smoke-advanced part: a canvas from the toolbox is sized and see-through, tints validate and show on the
    /// surface, and both are listed as things Minecraft can't run.</summary>
    void VerifyCanvasAndTint()
    {
        void Expect(bool ok, string what) { if (!ok) throw new Exception("Canvas and tint: " + what); }
        AddControl("canvas", 40, 40); Draw(); RefreshInspector();
        var canvas = ui.Elements.Last(e => e.Type == "canvas");
        Expect(canvas.Id.StartsWith("canvas") && !canvas.FillEnabled && canvas.Columns == 64 && canvas.Rows == 64 && canvas.Bounds.Width == 128 && canvas.Bounds.Height == 128, "the toolbox preset makes a 64 × 64 canvas shown at 2 × with no fill");
        Expect(Wysicraft.Core.Validation.Errors(project).Count == 0, "a new canvas validates: " + string.Join("; ", Wysicraft.Core.Validation.Errors(project)));
        canvas.Columns = Registry.MaxCanvas + 1;
        Expect(Wysicraft.Core.Validation.Errors(project).Any(i => i.Element == canvas.Id && i.Message.Contains("pixels each way")), "a canvas past the size limit is reported");
        canvas.Columns = 64;
        canvas.Tint = "red";
        Expect(Wysicraft.Core.Validation.Errors(project).Any(i => i.Element == canvas.Id && i.Message.StartsWith("Tint must be")), "a tint that isn't #RRGGBB is reported");
        canvas.Tint = "#FF8000";
        Expect(Wysicraft.Core.Validation.Errors(project).Count == 0, "a #RRGGBB tint validates");
        var uses = Compatibility.MinecraftProblems(project).Select(Compatibility.Describe).ToList();
        Expect(uses.Any(u => u.Contains(canvas.Id) && u.Contains("canvas control")) && uses.Any(u => u.Contains(canvas.Id) && u.Contains("tint #FF8000")), "Minecraft's check lists the canvas and its tint: " + string.Join("; ", uses));
        // A white picture tinted orange is orange, and its see-through pixels stay see-through.
        var white = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Pbgra32, null, new byte[] { 255, 255, 255, 255, 0, 0, 0, 0 }, 8);
        var px = new byte[8]; Tinted(white, "#FF8000").CopyPixels(px, 8, 0);
        Expect(px[0] == 0 && px[1] == 128 && px[2] == 255 && px[3] == 255 && px[7] == 0, "a picture is multiplied by its tint: " + string.Join(",", px));
        Expect(ReferenceEquals(Tinted(white, ""), white) && ReferenceEquals(Tinted(white, "#FF8000"), Tinted(white, "#FF8000")), "no tint is the picture itself, and a tinted copy is made once");
        canvas.Tint = "";
    }
}
