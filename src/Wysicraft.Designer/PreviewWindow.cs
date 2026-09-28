using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Wysicraft.Core;
using Wysicraft.Models;
using Wysicraft.Packaging;
using Validation = Wysicraft.Core.Validation;
namespace Wysicraft.Designer;

// Preview runs the project on the web runtime (wysicraft-web.js) in WebView2: the same engine as web, Windows and
// Electron exports, and a close match for the Minecraft runtime. Minecraft textures and item icons come from the
// player's own game files and are only ever used here, never exported.
public partial class MainWindow
{
    static Task<CoreWebView2Environment>? previewEnvironment;
    // Games play sound from the start (a title theme), as they would in the desktop app, rather than waiting for a click.
    static Task<CoreWebView2Environment> PreviewEnvironment() => previewEnvironment ??= CoreWebView2Environment.CreateAsync(null,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wysicraft", "PreviewWebView2"),
        new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));

    void Preview()
    {
        SaveScriptText(); if (Validation.Errors(project).Count > 0) { Validate(); return; }
        activePreview?.Window.Close(); activePreview = new PreviewSession(this, Json.CloneProject(project), ui.Id); previewRevision = Revision(); activePreview.Window.Show();
    }

    // Minecraft textures (and their animations) and item icons the project uses, from the loaded game JAR.
    Dictionary<string, byte[]> PreviewMinecraftFiles(Project p)
    {
        var files = new Dictionary<string, byte[]>();
        IEnumerable<Element> All(IEnumerable<Element> elements) => elements.SelectMany(e => new[] { e }.Concat(All(e.RowElements)));
        var screens = p.Screens.ToList(); var elements = screens.SelectMany(s => All(s.Elements)).ToList();
        var actions = screens.SelectMany(s => s.Events.Values.Concat(s.Elements.SelectMany(e => e.Events.Values))).SelectMany(ev => ev.Client.Actions.Concat(ev.Server.Actions)).ToList();
        var textures = elements.Select(e => e.Texture).Concat(actions.Where(a => a.Type == "change_texture").Select(a => a.Value)).Where(t => t.Contains(':') && !t.StartsWith(p.Manifest.Id + ":")).Distinct();
        foreach (var resource in textures)
        {
            if (!Validation.Resource(resource) || !TryTexture(resource, out var png)) continue;
            string path = "assets/" + resource.Replace(':', '/'); files[path] = png;
            try { if (minecraftAssets.TextureMeta(resource) is string meta) files[path + ".mcmeta"] = System.Text.Encoding.UTF8.GetBytes(meta); } catch { }
        }
        var items = elements.Where(e => e.Type == "item").Select(e => e.Item)
            .Concat(elements.Where(e => e.Type == "item_list").SelectMany(e => { try { return ItemRows.Parse(e.Value).Select(r => r.Item); } catch { return []; } }))
            .Concat(actions.Where(a => a.Type == "set_item").Select(a => a.Value)).Where(Validation.Resource).Distinct();
        foreach (var item in items)
        {
            if (ItemImage(item) is not BitmapSource icon) continue;
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(icon)); using var stream = new MemoryStream(); encoder.Save(stream);
            var parts = item.Split(':', 2); files.TryAdd($"assets/{parts[0]}/textures/item/{parts[1]}.png", stream.ToArray());
        }
        return files;
    }

    sealed class PreviewSession
    {
        const string Host = "preview.wysicraft";
        readonly MainWindow designer;
        readonly Project project;
        readonly string initialUi, folder;
        readonly WebView2 view = new();
        readonly TextBox output = new() { IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.Wrap };
        readonly TextBox code = new() { AcceptsReturn = true, AcceptsTab = true, FontFamily = new FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Text = "console.log('Hello from the preview!');\n// ui.setText('status', 'It works!');" };
        readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool closed; Task? closing;
        public Window Window { get; }
        public PreviewSession(MainWindow designer, Project project, string id)
        {
            this.designer = designer; this.project = project; initialUi = id;
            folder = Path.Combine(Path.GetTempPath(), "Wysicraft", "Preview", Guid.NewGuid().ToString("N"));
            var screen = project.Screens.First(s => s.Id == id);
            Window = new Window { Title = "Wysicraft • Interactive Preview", Owner = designer, Width = Math.Max(760, screen.Size.Width * 2 + 60), Height = Math.Max(650, screen.Size.Height * 2 + 330), Background = new SolidColorBrush(Color.FromRgb(29, 32, 37)), Foreground = Brushes.White, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var layout = new DockPanel(); Window.Content = layout;
            var tools = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(tools, Dock.Top); layout.Children.Add(tools);
            var reset = new Button { Content = "Reset preview" }; reset.Click += (_, _) => { if (view.CoreWebView2 != null) { output.Clear(); Print("RESET", "Starting again from " + initialUi); view.CoreWebView2.Reload(); } }; tools.Children.Add(reset);
            var clear = new Button { Content = "Clear console" }; clear.Click += (_, _) => output.Clear(); tools.Children.Add(clear);
            // Collider outlines are a Preview aid (apps never draw them); the runtime reads this setting every frame.
            var colliders = new CheckBox { Content = "Show colliders", IsChecked = true, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), ToolTip = "Outline physics colliders. They're never drawn in exported apps." };
            colliders.Click += async (_, _) => await Script($"window.wysicraftHost.showColliders = {(colliders.IsChecked == true ? "true" : "false")}; 0");
            tools.Children.Add(colliders);
            tools.Children.Add(new TextBlock { Text = "Click controls and use the keyboard to test • Server operations are simulated", Margin = new Thickness(12, 6, 4, 6), VerticalAlignment = VerticalAlignment.Center });
            var sizes = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(sizes, Dock.Top); layout.Children.Add(sizes);
            sizes.Children.Add(new TextBlock { Text = "Layout size (GUI pixels)", Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Center });
            var vw = new TextBox { Text = screen.Size.Width.ToString(), Width = 60 }; var vh = new TextBox { Text = screen.Size.Height.ToString(), Width = 60 }; sizes.Children.Add(vw); sizes.Children.Add(vh);
            var resize = new Button { Content = "Apply size" }; sizes.Children.Add(resize);
            var fit = new Button { Content = "Fit window" }; sizes.Children.Add(fit);
            resize.Click += async (_, _) => {
                if (!int.TryParse(vw.Text, out int w) || !int.TryParse(vh.Text, out int h) || w < 16 || h < 16 || w > 16384 || h > 16384) { Print("LAYOUT", "Use sizes from 16 to 16384."); return; }
                await Script($"Wysicraft.app.setViewport({w},{h})"); Print("LAYOUT", project.Screens.Any(s => s.Responsive) ? $"Laid out for {w} × {h}." : "This screen uses a fixed layout. Enable Responsive layout in screen settings to resize controls.");
            };
            fit.Click += async (_, _) => { await Script("Wysicraft.app.setViewport(0,0)"); Print("LAYOUT", "Laid out for the preview window."); };
            var bottom = new Grid { Height = 235 }; bottom.ColumnDefinitions.Add(new ColumnDefinition()); bottom.ColumnDefinitions.Add(new ColumnDefinition()); DockPanel.SetDock(bottom, Dock.Bottom); layout.Children.Add(bottom);
            var consolePanel = new DockPanel(); consolePanel.Children.Add(Header("CONSOLE • events, actions and script output")); consolePanel.Children.Add(output); bottom.Children.Add(consolePanel);
            var scriptPanel = new DockPanel(); Grid.SetColumn(scriptPanel, 1); bottom.Children.Add(scriptPanel); scriptPanel.Children.Add(Header("JAVASCRIPT • runs like a client script on this screen"));
            var run = new Button { Content = "Run JavaScript", HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(run, Dock.Bottom); scriptPanel.Children.Add(run); scriptPanel.Children.Add(code);
            run.Click += async (_, _) => { await ready.Task; await Script("Wysicraft.app.runScript(" + JsonSerializer.Serialize(code.Text) + ")"); };
            view.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x15, 0x18, 0x1D);
            layout.Children.Add(view);
            Window.Closing += (_, args) => { if (!closed) { args.Cancel = true; closing ??= CloseAsync(); } };
            Window.Closed += (_, _) => { closed = true; view.Dispose(); try { Directory.Delete(folder, true); } catch { } };
            Window.Loaded += async (_, _) => await StartAsync();
        }
        static TextBlock Header(string title) { var label = new TextBlock { Text = title, Margin = new Thickness(6), Foreground = Brushes.LightSkyBlue, FontSize = 11 }; DockPanel.SetDock(label, Dock.Top); return label; }
        void Print(string category, string text)
        {
            if (closed) return;
            if (output.Text.Length > 100000) output.Text = output.Text[^50000..];
            output.AppendText($"[{category}] {text}\n"); output.ScrollToEnd();
        }
        async Task StartAsync()
        {
            try
            {
                var files = WebExport.Files(project, new(ExtraAssets: designer.PreviewMinecraftFiles(project), HostScript: PreviewHost, Screen: initialUi));
                foreach (var (name, bytes) in files) { var path = Path.Combine(folder, name.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); }
                await view.EnsureCoreWebView2Async(await PreviewEnvironment());
                if (closed) return;
                var web = view.CoreWebView2;
                web.Settings.AreDefaultContextMenusEnabled = false; web.Settings.IsStatusBarEnabled = false; web.Settings.IsZoomControlEnabled = false; web.Settings.AreBrowserAcceleratorKeysEnabled = false;
                web.SetVirtualHostNameToFolderMapping(Host, folder, CoreWebView2HostResourceAccessKind.DenyCors);
                web.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith($"https://{Host}/")) e.Cancel = true; };
                web.NewWindowRequested += (_, e) => e.Handled = true;
                web.WebMessageReceived += (_, e) => Received(e.WebMessageAsJson);
                web.ProcessFailed += (_, e) => Print("ERROR", "The preview stopped (" + e.ProcessFailedKind + "). Click Reset preview to start again.");
                Print("READY", "Buttons and keys are live. Every click is logged, even without an assigned action.");
                Print("SCRIPTS", "Scripts run in the same web runtime as HTML and desktop exports. Server operations here are simulated.");
                web.Navigate($"https://{Host}/index.html");
                view.Focus();
            }
            catch (Exception ex) when (ex is WebView2RuntimeNotFoundException)
            { Print("ERROR", "Preview needs the Microsoft Edge WebView2 Runtime, which comes with Windows 10 and 11: https://go.microsoft.com/fwlink/p/?LinkId=2124703"); ready.TrySetException(ex); }
            catch (Exception ex) { Print("ERROR", ex.Message); ready.TrySetException(ex); }
        }
        // Messages from the page (see PreviewHost).
        void Received(string json)
        {
            try
            {
                var m = JsonNode.Parse(json)!.AsObject(); string kind = (string?)m["kind"] ?? "";
                string S(string key) => m[key]?.ToString() ?? "";
                switch (kind)
                {
                    case "ready": ready.TrySetResult(); break;
                    case "event":
                    {
                        string ev = S("event"), where = S("screen") + "." + (S("element").Length > 0 ? S("element") + "." : "") + ev;
                        bool assigned = m["assigned"]?.GetValue<bool>() ?? false;
                        if (ev is "tick" or "key" or "hover" or "mouse_enter" or "mouse_leave") { if (assigned && ev is not ("tick" or "key" or "hover")) Print("EVENT", where); break; }
                        Print("EVENT", where); if (!assigned) Print("INFO", "No actions or script assigned to this event."); break;
                    }
                    case "script": if (S("script") != "(scratchpad)") Print(m["server"]?.GetValue<bool>() == true ? "SIMULATED SERVER SCRIPT" : "SCRIPT", S("script") + " → " + S("function")); break;
                    case "log": Print(S("level") switch { "error" => "ERROR", "warn" => "WARNING", "info" => "INFO", _ => "LOG" }, S("text")); break;
                    case "server": Print("SIMULATED SERVER", S("text")); break;
                    case "message": Print("MESSAGE", S("text")); break;
                    case "sound": Print("SOUND", S("text")); break;
                    case "closed": Print("CLOSED", S("screen") + " closed."); if (!closed) Window.Close(); break;
                }
            }
            catch (Exception ex) { Print("ERROR", ex.Message); }
        }
        const string PreviewHost = """
            // Preview host: reports what happens to the Wysicraft editor's console.
            (function () {
              const post = m => { try { window.chrome.webview.postMessage(m); } catch (e) { } };
              window.wysicraftHost = {
                closeOnEscape: false,
                showColliders: true, // colliders are invisible in apps; Preview outlines them
                onEvent(e) { post(Object.assign({ kind: 'event' }, e)); },
                onScript(s) { post(Object.assign({ kind: 'script' }, s)); },
                onLog(level, text) { post({ kind: 'log', level, text: String(text) }); },
                onCommand(command) { post({ kind: 'server', text: 'command ' + command }); },
                onServerFunction(name, value) { post({ kind: 'server', text: 'server_function ' + name + ' ' + value }); },
                onServerAction(a) { post({ kind: 'server', text: a.type + ' ' + a.target + ' ' + a.value }); },
                onMessage(text) { post({ kind: 'message', text }); },
                onSound(sound) { post({ kind: 'sound', text: sound }); },
                onClose(screen) { post({ kind: 'closed', screen }); }
              };
              window.addEventListener('load', () => setTimeout(() => post({ kind: 'ready' }), 0));
            })();
            """;
        async Task<string> Script(string code)
        {
            if (closed || view.CoreWebView2 == null) return "null";
            return await view.CoreWebView2.ExecuteScriptAsync(code);
        }
        internal async Task WaitReady()
        {
            await ready.Task;
            for (int i = 0; i < 500 && !closed; i++) { if (await Script("Wysicraft.app.busy") != "true") return; await Task.Delay(20); }
        }
        internal async Task<string> SnapshotAsync()
        {
            await WaitReady();
            var node = JsonNode.Parse(JsonSerializer.Deserialize<string>(await Script("JSON.stringify(Wysicraft.app.snapshot())")) ?? "{}")!.AsObject();
            node["logs"] = output.Text.Length > 12000 ? output.Text[^12000..] : output.Text;
            return node.ToJsonString();
        }
        async Task<UiDefinition> CurrentScreenAsync() { await WaitReady(); string id = JsonSerializer.Deserialize<string>(await Script("Wysicraft.app.screen")) ?? initialUi; return project.Screens.First(s => s.Id == id); }
        internal async Task RunMcpEvent(string id, string eventName, string value)
        {
            var screen = await CurrentScreenAsync();
            var events = id.Length == 0 ? screen.Events : screen.Elements.Single(e => e.Id == id).Events;
            if (!events.ContainsKey(eventName)) throw new InvalidOperationException("No assigned event: " + id + "." + eventName);
            if (id.Length > 0 && eventName == "value_changed")
            {
                var target = screen.Elements.Single(e => e.Id == id);
                if (target.Type == "slider" && (!double.TryParse(value, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number) || number < target.Minimum || number > target.Maximum)) throw new InvalidOperationException("Slider value is outside its range.");
                if (target.Type == "dropdown" && (!int.TryParse(value, out int index) || index < 0 || index >= target.Options.Count)) throw new InvalidOperationException("Dropdown index is outside its options.");
            }
            await Script($"Wysicraft.app.testEvent({JsonSerializer.Serialize(id)},{JsonSerializer.Serialize(eventName)},{JsonSerializer.Serialize(value)})");
            await WaitReady();
        }
        // Runs JavaScript as a client script on the current screen (same API and limits as scripts), then waits for it.
        internal async Task RunScriptAsync(string source)
        {
            await WaitReady();
            // The preview is the web runtime, whatever the project targets, so it uses the web limit.
            if (Limits.SizeOf(source) > Limits.Web.ScriptBytes) throw new InvalidOperationException($"Scripts are at most {Limits.Web.ScriptBytes / 1024} KiB.");
            await Script("Wysicraft.app.runScript(" + JsonSerializer.Serialize(source) + ")"); await Task.Delay(30); await WaitReady();
        }
        internal async void TriggerTest(string element, string eventName)
        {
            try
            {
                await WaitReady(); Print("TEST", (element == "" ? initialUi : element) + "." + eventName);
                string value = eventName switch { "checked" => "true", "unchecked" => "false", _ => "" };
                await Script($"Wysicraft.app.testEvent({JsonSerializer.Serialize(element)},{JsonSerializer.Serialize(eventName)},{(value.Length > 0 ? JsonSerializer.Serialize(value) : "undefined")})");
            }
            catch (Exception ex) { Print("ERROR", ex.Message); }
        }
        internal async Task CaptureCanvas(string path)
        {
            await WaitReady(); await Task.Delay(100);
            using var stream = File.Create(path);
            await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        }
        internal async Task CloseAsync()
        {
            // Yield until WPF has returned from its Closing event before closing again.
            await Task.Yield();
            if (!closed && ready.Task.IsCompletedSuccessfully) { try { await Script("Wysicraft._app.process({ ui: Wysicraft._app.ui, element: null, event: 'close', value: '' })"); await Task.Delay(50); } catch { } }
            closed = true; Window.Close();
        }
        internal async Task VerifyClickAsync()
        {
            await WaitReady();
            var screen = project.Screens.First(s => s.Id == initialUi);
            var button = screen.Elements.First(e => e.Type == "button");
            await Script($"Wysicraft._app.project.scripts['scripts/client/preview_test.js'] = \"function clicked(ctx) {{ console.log('Clicked!', ctx.elementId); ui.setText('status', 'Script ran'); }}\";" +
                         $"Wysicraft._app.ui.elements.find(e => e.id === {JsonSerializer.Serialize(button.Id)}).events.click = {{ client: {{ actions: [], script: 'scripts/client/preview_test.js', function: 'clicked' }}, server: {{ actions: [], script: '', function: '' }} }}; 0");
            await Script($"Wysicraft.app.testEvent({JsonSerializer.Serialize(button.Id)},'click','')"); await WaitReady();
            var state = JsonNode.Parse(await SnapshotAsync())!;
            if (!output.Text.Contains("Clicked!") || state["elements"]!.AsArray().First(e => (string?)e!["id"] == "status")!["text"]!.ToString() != "Script ran") throw new InvalidOperationException("Preview button did not execute its assigned JavaScript:\n" + output.Text);
            // A script stuck in a loop is stopped after 2 seconds and the preview keeps working.
            await Script("Wysicraft.app.runScript('while (true) {}')"); await WaitReady();
            if (!output.Text.Contains("longer than 2 seconds")) throw new InvalidOperationException("Infinite script was not stopped:\n" + output.Text);
            await Script("Wysicraft.app.runScript(\"console.log('exposed', typeof fetch, typeof XMLHttpRequest, typeof document)\")"); await WaitReady();
            if (!output.Text.Contains("exposed undefined undefined undefined")) throw new InvalidOperationException("Unexpected script API exposure:\n" + output.Text);
            await VerifyCameraAsync(button.Id);
        }
        // A camera over the top-left quarter of the screen shows it at 2×; points map through it both ways, controls
        // outside it can't be clicked, and scripts can resize it.
        async Task VerifyCameraAsync(string buttonId)
        {
            string result = await Script("(() => { const app = Wysicraft._app, ui = app.ui, w = ui.size.width, h = ui.size.height;" +
                "const cam = { id: 'cam_test', type: 'camera', parent: '', visible: true, enabled: true, visibleIf: '', enabledIf: '', events: {}, fillEnabled: false, borderWidth: 0, bounds: { x: 0, y: 0, width: w / 2, height: h / 2 } };" +
                "ui.elements.push(cam); app.render(); const v = app.view(cam), r = app.canvas.getBoundingClientRect();" +
                "const wx = app.originX + 10, wy = app.originY + 12, p = app.point({ clientX: r.left + (v.ox + wx * v.k) * app.scale, clientY: r.top + (v.oy + wy * v.k) * app.scale });" +
                $"const b = ui.elements.find(e => e.id === {JsonSerializer.Serialize(buttonId)}), outside = b.bounds.x + 1 < w / 2 || b.bounds.y + 1 < h / 2;" +
                "cam.bounds.x = w / 2; cam.bounds.y = h / 2; app.render(); const blocked = outside ? !app.inside(b, app.x(b) + 1, app.y(b) + 1) : true;" +
                "cam.bounds.x = 0; cam.bounds.y = 0;" +
                "return JSON.stringify({ active: app._cam && app._cam.id, k: v.k, dx: Math.abs(p.x - wx), dy: Math.abs(p.y - wy), blocked }); })()");
            var check = JsonNode.Parse(JsonNode.Parse(result)!.GetValue<string>())!;
            if ((string?)check["active"] != "cam_test" || Math.Abs((double)check["k"]! - 2) > 0.001 || (double)check["dx"]! > 0.01 || (double)check["dy"]! > 0.01 || check["blocked"]!.GetValue<bool>() != true)
                throw new InvalidOperationException("Camera view is wrong: " + check.ToJsonString());
            await Script("Wysicraft.app.runScript(\"ui.setSize('cam_test', 120, 60)\")"); await WaitReady();
            string size = await Script("(() => { const c = Wysicraft._app.ui.elements.find(e => e.id === 'cam_test'); const s = c.bounds.width + 'x' + c.bounds.height; Wysicraft._app.ui.elements.splice(Wysicraft._app.ui.elements.indexOf(c), 1); Wysicraft._app.render(); return s; })()");
            if (JsonNode.Parse(size)!.GetValue<string>() != "120x60") throw new InvalidOperationException("ui.setSize did not resize the camera: " + size);
        }
        internal async Task VerifyEventTestAsync(string element, string eventName, string expected)
        {
            await WaitReady(); TriggerTest(element, eventName); await Task.Delay(100); await WaitReady();
            for (int i = 0; i < 50 && !output.Text.Contains(expected); i++) await Task.Delay(20);
            if (!output.Text.Contains(expected)) throw new InvalidOperationException("Assigned event test failed:\n" + output.Text);
        }
    }
    internal async Task VerifyPreviewAsync(string capture)
    {
        var preview = new PreviewSession(this, Json.CloneProject(project), ui.Id);
        try { preview.Window.Show(); await preview.VerifyClickAsync(); await preview.CaptureCanvas(capture); }
        finally { await preview.CloseAsync(); }
    }
}
