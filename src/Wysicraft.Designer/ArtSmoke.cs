using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wysicraft.Core;
using Wysicraft.Models;
using Validation = Wysicraft.Core.Validation;
namespace Wysicraft.Designer;

// --smoke-art: what illustrated games need, in the real window. Rotation and scale (Properties fields, the canvas
// showing a control turned and a panel carrying what's attached inside it), wrapped text, smooth pictures (the canvas
// and the export), Minecraft being told what it can't run, and the same project in Preview: the game draws the card
// turned, wraps its text, and a script fades, turns and reorders controls. Pictures of the canvas and of Preview are
// written beside the result.
public partial class MainWindow
{
    internal async Task VerifyArtAsync(string output)
    {
        void Expect(bool ok, string what) { if (!ok) throw new Exception("Illustrated art: " + what); }
        project = new Project(); project.Manifest.Id = "artsmoke"; project.Manifest.Name = "Art smoke"; project.Manifest.Target = "web";
        ui = project.Screens[0]; ui.Size.Width = 320; ui.Size.Height = 200; history.Clear(); selected.Clear();

        // A soft-edged picture (a round glow), which shows at a glance whether scaling is smooth or blocky.
        static byte[] Glow(int size)
        {
            var pixels = new byte[size * size * 4];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                double d = Math.Sqrt(Math.Pow(x - size / 2.0 + 0.5, 2) + Math.Pow(y - size / 2.0 + 0.5, 2)) / (size / 2.0);
                int i = (y * size + x) * 4; byte light = (byte)(255 * Math.Clamp(1.2 - d, 0, 1));
                pixels[i] = (byte)(light / 3); pixels[i + 1] = (byte)(light * 2 / 3); pixels[i + 2] = light; pixels[i + 3] = 255;
            }
            var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
        }
        project.Assets[TextureAssets.Path(project.Manifest.Id, "glow.png", "image")] = Glow(16);
        string glow = TextureAssets.Resource(project.Manifest.Id, "glow.png", "image");

        // Two cards: a panel with a picture, a title and a description attached inside it.
        Element Part(string id, string type, string parent, double x, double y, double w, double h) => new() { Id = id, Type = type, Parent = parent, Bounds = new() { X = x, Y = y, Width = w, Height = h } };
        void Card(string id, double x, double rotation, double scale, string title, string text)
        {
            var card = Part(id, "panel", "", x, 30, 100, 140); card.Background = "#2B3A55"; card.CornerRadius = 8; card.BorderWidth = 2; card.BorderColor = "#C9A44C"; card.Rotation = rotation; card.Scale = scale;
            var art = Part(id + "_art", "image", id, x + 10, 40, 80, 60); art.Texture = glow; art.FillEnabled = false;
            var name = Part(id + "_title", "label", id, x + 6, 102, 88, 14); name.Text = title; name.Bold = true; name.Alignment = "center"; name.FillEnabled = false; name.Foreground = "#FFFFFF";
            var body = Part(id + "_text", "label", id, x + 8, 118, 84, 46); body.Text = text; body.Wrap = true; body.FillEnabled = false; body.Foreground = "#D9E6F1"; body.FontScale = 0.8;
            ui.Elements.AddRange([card, art, name, body]);
        }
        ui.Elements.Clear();
        var table = Part("table", "panel", "", 0, 0, 320, 200); table.Background = "#16202E"; ui.Elements.Add(table);
        Card("strike", 70, -8, 1, "Strike", "Deal 6 damage. Draw a card if the enemy is weak.");
        Card("defend", 150, 8, 1.1, "Defend", "Gain 5 block.\\nIt lasts until your next turn.");
        var plain = Part("plain", "label", "", 4, 4, 60, 12); plain.Text = "No turn"; plain.FillEnabled = false; ui.Elements.Add(plain);
        RefreshAll();
        Element E(string id) => ui.Elements.Single(e => e.Id == id);
        Expect(Validation.Errors(project).Count == 0, "the project validates: " + string.Join("; ", Validation.Errors(project).Take(3)));

        // Properties: Rotation, Scale and Wrap text in a web & desktop project, and none of them for Minecraft.
        selected.Clear(); selected.Add("strike"); RefreshInspector();
        var fields = InspectorText();
        Expect(fields.Contains("ROTATION (°)") && fields.Contains("SCALE"), "a control's Properties have Rotation and Scale: " + string.Join("|", fields.Take(30)));
        selected.Clear(); selected.Add("strike_text"); RefreshInspector();
        Expect(InspectorText().Contains("WRAP TEXT"), "a label's Properties have Wrap text");
        project.Manifest.Target = "minecraft"; RefreshInspector();
        Expect(!InspectorText().Contains("WRAP TEXT") && !InspectorText().Contains("ROTATION (°)"), "a project made for Minecraft doesn't offer them");
        project.Manifest.Target = "web"; RefreshInspector();
        bool refused = false; try { E("strike").Scale = 0; Validation.Check(project); refused = Validation.Errors(project).Count > 0; } finally { E("strike").Scale = 1; }
        Expect(refused, "a scale of nothing is an error");

        // The canvas shows them as the game draws them: the card turned, and what's attached inside it along with it.
        selected.Clear(); Draw(); UpdateLayout();
        Border On(string id) => Surface.Children.OfType<Border>().First(b => (b.Tag as string) == id);
        Matrix M(string id) => On(id).RenderTransform.Value;
        double degrees = Math.Atan2(M("strike").M12, M("strike").M11) * 180 / Math.PI;
        Expect(Math.Abs(degrees + 8) < 0.01 && Math.Abs(Math.Sqrt(M("defend").M11 * M("defend").M11 + M("defend").M12 * M("defend").M12) - 1.1) < 0.001, $"the canvas turns the card by its rotation and scale: {degrees:0.##}°");
        Expect(!M("strike_text").IsIdentity && Math.Abs(Math.Atan2(M("strike_text").M12, M("strike_text").M11) * 180 / Math.PI + 8) < 0.01, "a control attached inside a turned panel is turned with it");
        Expect(M("plain").IsIdentity && M("table").IsIdentity, "and anything else is left alone");
        // The centre of the card stays where it was; a corner of its description moves with the card.
        var centre = On("strike").TransformToAncestor(Surface).Transform(new Point(100, 140));
        Expect(Math.Abs(centre.X - (70 + 50) * 2) < 0.5 && Math.Abs(centre.Y - (30 + 70) * 2) < 0.5, $"it turns about its own centre: {centre}");
        var corner = On("strike_text").TransformToAncestor(Surface).Transform(new Point(0, 0));
        Expect(Math.Abs(corner.X - 78 * 2) > 4 || Math.Abs(corner.Y - 118 * 2) > 4, $"the description moved with the card: {corner}");
        var wrapped = FindText(On("strike_text")); var single = FindText(On("plain"));
        Expect(wrapped?.TextWrapping == TextWrapping.Wrap && single?.TextWrapping == TextWrapping.NoWrap && FindText(On("defend_text"))!.Text.Contains('\n'), "wrapped text is shown wrapped, and a typed \\n is a line break");
        Expect(RenderOptions.GetBitmapScalingMode(Surface) == BitmapScalingMode.NearestNeighbor, "pictures on the canvas keep hard pixel edges by default");
        SaveCanvasPicture(output + ".canvas.png");
        project.Manifest.SmoothImages = true; Draw(); UpdateLayout();
        Expect(RenderOptions.GetBitmapScalingMode(Surface) == BitmapScalingMode.HighQuality, "and are smooth with Smooth pictures on");
        SaveCanvasPicture(output + ".canvas-smooth.png");

        // Minecraft is told what it can't run.
        project.Manifest.Target = "both";
        var uses = Compatibility.MinecraftProblems(project).Select(Compatibility.Describe).ToList();
        Expect(uses.Any(u => u.Contains("strike") && u.Contains("rotation")) && uses.Any(u => u.Contains("defend") && u.Contains("rotation and scale")) && uses.Any(u => u.Contains("wrapped text")) && uses.Any(u => u.Contains("Smooth pictures")),
            "a project also made for Minecraft lists rotation, scale, wrapped text and smooth pictures: " + string.Join(" | ", uses));
        project.Manifest.Target = "web";

        // Preview: the game itself. The export carries the settings; the runtime draws them; a script changes them.
        var preview = new PreviewSession(this, Json.CloneProject(project), ui.Id);
        try
        {
            preview.Window.Show(); await preview.WaitReady();
            const string read = "JSON.stringify((a => ({ smooth: a.smooth, rotation: a.ui.elements.find(e => e.id === 'strike').rotation, scale: a.ui.elements.find(e => e.id === 'defend').scale, " +
                "opacity: a.ui.elements.find(e => e.id === 'defend').opacity, wrap: a.ui.elements.find(e => e.id === 'strike_text').wrap, lines: ((a._wrap && a._wrap.get('strike_text')) || { lines: [] }).lines.length, " +
                "breaks: ((a._wrap && a._wrap.get('defend_text')) || { lines: [] }).lines.length, order: a.ui.elements.map(e => e.id).join(' ') }))(Wysicraft._app))";
            JsonNode state = null!;
            for (int i = 0; i < 100; i++) { state = JsonNode.Parse(JsonSerializer.Deserialize<string>(await preview.EvalForTest(read))!)!; if ((int)state["lines"]! > 0) break; await Task.Delay(100); }
            Expect((bool)state["smooth"]! && (double)state["rotation"]! == -8 && (double)state["scale"]! == 1.1 && (bool)state["wrap"]!, "the game gets smooth pictures, rotation, scale and wrap: " + state.ToJsonString());
            Expect((int)state["lines"]! >= 3 && (int)state["breaks"]! >= 2, "and wraps the descriptions onto several lines: " + state.ToJsonString());
            Expect(((string)state["order"]!).EndsWith("defend defend_art defend_title defend_text plain"), "drawn in the order of the screen: " + state["order"]);
            await preview.CaptureCanvas(output + ".preview.png");
            await preview.RunScriptAsync("ui.setRotation('strike', 20); ui.setScale('strike', 1.25); ui.setOpacity('defend', 0.4); ui.bringToFront('strike');");
            await preview.WaitReady();
            state = JsonNode.Parse(JsonSerializer.Deserialize<string>(await preview.EvalForTest(read))!)!;
            Expect((double)state["rotation"]! == 20 && (double)state["opacity"]! == 0.4 && ((string)state["order"]!).EndsWith("plain strike strike_art strike_title strike_text"),
                "a script turns, fades and brings a card to the front (with what's attached inside it): " + state.ToJsonString());
            await preview.CaptureCanvas(output + ".preview-after.png");
        }
        finally { await preview.CloseAsync(); }

        dirty = false;
        File.WriteAllText(output, "PASS: illustrated art: Rotation, Scale and Wrap text in Properties (web & desktop only), the canvas turning a control about its centre and a panel carrying what's attached inside it, wrapped text and \\n, hard or smooth pictures on the canvas, Minecraft told what it can't run, and in Preview the game drawing them and a script turning, scaling, fading and reordering a card");
    }
    static TextBlock? FindText(DependencyObject root)
    {
        if (root is TextBlock text) return text;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) if (FindText(VisualTreeHelper.GetChild(root, i)) is TextBlock found) return found;
        return null;
    }
}
