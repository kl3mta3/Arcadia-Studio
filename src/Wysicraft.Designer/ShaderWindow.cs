using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wysicraft.Core;
using Wysicraft.Models;
using Validation = Wysicraft.Core.Validation;
namespace Wysicraft.Designer;

// Advanced → Shaders (web): GLSL ES 3.00 fragment shaders run over what the WebGL2 batch layer drew — the tilemap,
// particles and plain sprites. Named on the manifest like particle effects, so several screens can use the same one;
// a screen picks one in Screen settings.
//
// There is no GPU here to compile against, so this window checks the shape and leaves the real compile to Preview,
// which reports a shader that will not build in its log. That is also what the runtime does: a broken shader costs
// the effect, not the screen.
public partial class MainWindow
{
    const string ShaderHeader = "#version 300 es\nprecision mediump float;\nin vec2 v_uv;\nuniform sampler2D u_scene;   // what the layer drew\nuniform vec2 u_resolution;\nuniform float u_time;        // seconds\nout vec4 outColor;\n";

    static readonly (string Name, string Body)[] ShaderPresets =
    [
        ("Pass through", "void main() {\n    outColor = texture(u_scene, v_uv);\n}\n"),
        ("Vignette", "uniform float u_amount;   // ui.setShaderValue('u_amount', 0.8)\nvoid main() {\n    vec4 c = texture(u_scene, v_uv);\n    float d = distance(v_uv, vec2(0.5));\n    float amount = u_amount == 0.0 ? 0.8 : u_amount;\n    outColor = vec4(c.rgb * (1.0 - d * d * 2.0 * amount), c.a);\n}\n"),
        ("Scanlines (CRT)", "void main() {\n    vec4 c = texture(u_scene, v_uv);\n    float line = 0.88 + 0.12 * sin(v_uv.y * u_resolution.y * 1.6);\n    outColor = vec4(c.rgb * line, c.a);\n}\n"),
        ("Chromatic split", "uniform float u_amount;\nvoid main() {\n    float k = (u_amount == 0.0 ? 1.5 : u_amount) / u_resolution.x;\n    float r = texture(u_scene, v_uv + vec2(k, 0.0)).r;\n    float b = texture(u_scene, v_uv - vec2(k, 0.0)).b;\n    vec4 c = texture(u_scene, v_uv);\n    outColor = vec4(r, c.g, b, c.a);\n}\n"),
        ("Colour drain", "uniform float u_amount;   // 0 = full colour, 1 = grey\nvoid main() {\n    vec4 c = texture(u_scene, v_uv);\n    float grey = dot(c.rgb, vec3(0.299, 0.587, 0.114));\n    outColor = vec4(mix(c.rgb, vec3(grey), clamp(u_amount, 0.0, 1.0)), c.a);\n}\n"),
        ("Heat wobble", "void main() {\n    float wobble = sin(v_uv.y * 40.0 + u_time * 3.0) * 0.0025;\n    outColor = texture(u_scene, v_uv + vec2(wobble, 0.0));\n}\n"),
        ("Bloomish glow", "void main() {\n    vec2 step = 1.5 / u_resolution;\n    vec4 c = texture(u_scene, v_uv);\n    vec4 blur = texture(u_scene, v_uv + vec2(step.x, 0.0)) + texture(u_scene, v_uv - vec2(step.x, 0.0))\n              + texture(u_scene, v_uv + vec2(0.0, step.y)) + texture(u_scene, v_uv - vec2(0.0, step.y));\n    outColor = vec4(c.rgb + blur.rgb * 0.18, c.a);\n}\n"),
    ];

    void ShowShadersWindow()
    {
        var shaders = project.Manifest.Shaders.Select(s => new ShaderEffect { Id = s.Id, Source = s.Source }).ToList();
        var window = new Window { Owner = this, Title = "Shaders · web", Width = 860, Height = 640, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new Thickness(10) }; window.Content = root;
        var left = new DockPanel { Width = 190, Margin = new Thickness(0, 0, 10, 0) }; DockPanel.SetDock(left, Dock.Left); root.Children.Add(left);
        var add = new Button { Content = "+ New shader", Margin = new Thickness(0, 0, 0, 4) }; DockPanel.SetDock(add, Dock.Top); left.Children.Add(add);
        // The stock ones, added by name in a click: nobody should have to write GLSL to get a vignette.
        var stock = new Menu { Margin = new Thickness(0, 0, 0, 4), Background = Brushes.Transparent };
        var stockItem = new MenuItem { Header = "+ Add a stock shader ▾" };
        stock.Items.Add(stockItem); DockPanel.SetDock(stock, Dock.Top); left.Children.Add(stock);
        var remove = new Button { Content = "Delete shader", Margin = new Thickness(0, 4, 0, 0) }; DockPanel.SetDock(remove, Dock.Bottom); left.Children.Add(remove);
        var picker = new ListBox(); left.Children.Add(picker);
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);

        var head = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) }; DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);
        var note = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 11, Margin = new Thickness(0, 6, 0, 0) }; DockPanel.SetDock(note, Dock.Top); root.Children.Add(note);
        var source = new TextBox { AcceptsReturn = true, AcceptsTab = true, FontFamily = new FontFamily("Consolas, Cascadia Mono, monospace"), FontSize = 12, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.NoWrap };
        root.Children.Add(source);

        ShaderEffect? current = null;
        bool loading = false;
        void Pick() { picker.ItemsSource = shaders.Select(s => s.Id).ToList(); if (current != null) picker.SelectedItem = current.Id; }
        void Show()
        {
            head.Children.Clear(); note.Text = "";
            loading = true;
            source.Text = current?.Source ?? "";
            source.IsEnabled = current != null;
            loading = false;
            if (current == null)
            {
                note.Text = "A shader runs over what the batch layer drew — the tilemap, particles and plain sprites — after everything else on that run. "
                          + "Pick one for a screen in Screen settings. It gets u_scene (what was drawn), u_resolution, u_time in seconds, and any \"uniform float u_name\" a script sets with ui.setShaderValue('u_name', value). "
                          + "Web only: there is no batch layer in Minecraft.";
                return;
            }
            head.Children.Add(new TextBlock { Text = "ID ", VerticalAlignment = VerticalAlignment.Center });
            var id = new TextBox { Text = current.Id, Width = 150, Margin = new Thickness(0, 0, 12, 0) };
            id.LostFocus += (_, _) => { if (current != null) { current.Id = id.Text.Trim(); Pick(); } };
            head.Children.Add(id);
            var presets = new ComboBox { Width = 180, Margin = new Thickness(0, 0, 8, 0), ItemsSource = ShaderPresets.Select(p => p.Name).ToList(), SelectedIndex = -1 };
            presets.SelectionChanged += (_, _) => Guard(() =>
            {
                if (current == null || presets.SelectedIndex < 0) return;
                current.Source = ShaderHeader + "\n" + ShaderPresets[presets.SelectedIndex].Body;
                loading = true; source.Text = current.Source; loading = false;
                note.Text = "Loaded the " + ShaderPresets[presets.SelectedIndex].Name + " preset. Preview (F5) to see it run.";
            });
            head.Children.Add(new TextBlock { Text = "Start from ", VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(presets);
            var used = project.Screens.Where(s => s.Shader == current.Id).Select(s => s.Id).ToList();
            head.Children.Add(new TextBlock { Text = used.Count > 0 ? "Used by " + string.Join(", ", used) : "Not used by any screen yet", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
        }
        source.TextChanged += (_, _) => { if (!loading && current != null) current.Source = source.Text; };
        picker.SelectionChanged += (_, _) => { current = shaders.FirstOrDefault(s => s.Id == picker.SelectedItem as string); Show(); };
        add.Click += (_, _) => Guard(() =>
        {
            int n = 1; while (shaders.Any(s => s.Id == "shader" + n)) n++;
            current = new ShaderEffect { Id = "shader" + n, Source = ShaderHeader + "\n" + ShaderPresets[0].Body };
            shaders.Add(current); Pick(); Show();
        });
        foreach (var (name, body) in ShaderPresets)
        {
            if (name == "Pass through") continue;   // that one is what "+ New shader" already gives you
            var item = new MenuItem { Header = name };
            item.Click += (_, _) => Guard(() =>
            {
                string id = System.Text.RegularExpressions.Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
                string unique = id; int n = 2; while (shaders.Any(s => s.Id == unique)) unique = id + "_" + n++;
                current = new ShaderEffect { Id = unique, Source = ShaderHeader + "\n" + body };
                shaders.Add(current); Pick(); Show();
            });
            stockItem.Items.Add(item);
        }
        remove.Click += (_, _) => Guard(() =>
        {
            if (current == null) return;
            var using_ = project.Screens.Where(s => s.Shader == current.Id).Select(s => s.Id).ToList();
            if (using_.Count > 0) throw new InvalidOperationException($"\"{current.Id}\" is still used by {string.Join(", ", using_)}. Clear the screen's Shader first.");
            shaders.Remove(current); current = shaders.FirstOrDefault(); Pick(); Show();
        });

        var apply = new Button { Content = "Apply", IsDefault = true, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 3, 12, 3) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(12, 3, 12, 3) };
        apply.Click += (_, _) => Guard(() =>
        {
            System.Windows.Input.Keyboard.ClearFocus();
            var ids = new HashSet<string>();
            foreach (var shader in shaders)
            {
                if (!Validation.Id(shader.Id) || !ids.Add(shader.Id)) throw new InvalidOperationException($"\"{shader.Id}\": shader IDs must be unique lowercase IDs.");
                if (!shader.Source.TrimStart().StartsWith("#version 300 es")) throw new InvalidOperationException($"\"{shader.Id}\": start the shader with \"#version 300 es\" — WebGL2 uses GLSL ES 3.00.");
                if (!shader.Source.Contains("void main")) throw new InvalidOperationException($"\"{shader.Id}\": a fragment shader needs a void main().");
            }
            Change(); project.Manifest.Shaders = shaders; window.Close(); RefreshInspector();
        });
        cancel.Click += (_, _) => window.Close();
        bottom.Children.Add(new TextBlock { Text = "Preview (F5) compiles it; a shader that won't build is reported there. ", Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        bottom.Children.Add(apply); bottom.Children.Add(cancel);
        current = shaders.FirstOrDefault(); Pick(); Show(); window.ShowDialog();
    }
}
