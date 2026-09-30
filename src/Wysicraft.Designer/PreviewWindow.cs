using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
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
        Wysicraft.Core.AppFolders.Path("PreviewWebView2"),
        new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));

    void Preview()
    {
        if (ui.IsLeaderboard) { PreviewLeaderboard(); return; }
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

    // The work area (the screen less the taskbar) of the monitor a window is on, in WPF units.
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    static Rect WorkArea(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (handle == IntPtr.Zero || !GetMonitorInfo(MonitorFromWindow(handle, 2 /* nearest */), ref info)) return SystemParameters.WorkArea;
        var toWpf = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return new Rect(toWpf.Transform(new Point(info.Work.Left, info.Work.Top)), toWpf.Transform(new Point(info.Work.Right, info.Work.Bottom)));
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
        readonly CheckBox profilerBox = new() { Content = "Profiler", IsChecked = false, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), ToolTip = "Show frame, engine, drawing and script times and counts over the game. Never shown in exported apps." };
        readonly CheckBox fitBox = new() { Content = "Fit game to window", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), ToolTip = "Draw the game scaled to fill this window, smaller or larger, as the window and console change. Only Preview's zoom changes: the game's size and layout are its own." };
        readonly CheckBox muteBox = new() { Content = "Mute", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), ToolTip = "Turn the preview's sound off. The game itself is unchanged; kept for the next preview." };
        // The console and JavaScript panels, and the bar that minimizes them; while minimized, the bar counts what's new.
        readonly Grid consoleArea = new() { Height = 210 };
        readonly Button consoleToggle = new() { Padding = new Thickness(10, 1, 10, 1), Margin = new Thickness(0, 2, 4, 2) };
        readonly TextBlock consoleNews = new() { Foreground = Brushes.LightGray, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        int unseen; bool unseenError;
        bool closed, profiling; Task? closing;
        public Window Window { get; }
        public PreviewSession(MainWindow designer, Project project, string id)
        {
            this.designer = designer; this.project = project; initialUi = id;
            folder = Path.Combine(Path.GetTempPath(), "Arcadia Studio", "Preview", Guid.NewGuid().ToString("N"));
            var screen = project.Screens.First(s => s.Id == id);
            // Twice the game's size where there's room; never bigger than the screen the editor is on.
            var area = WorkArea(designer);
            Window = new Window { Title = "Arcadia Studio • Interactive Preview", Owner = designer, Width = Math.Min(area.Width, Math.Max(760, screen.Size.Width * 2 + 60)), Height = Math.Min(area.Height, Math.Max(650, screen.Size.Height * 2 + 330)), Background = new SolidColorBrush(Color.FromRgb(29, 32, 37)), Foreground = Brushes.White, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var layout = new DockPanel(); Window.Content = layout;
            var tools = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(tools, Dock.Top); layout.Children.Add(tools);
            var reset = new Button { Content = "Reset preview" }; reset.Click += (_, _) => { if (view.CoreWebView2 != null) { output.Clear(); Print("RESET", "Starting again from " + initialUi); view.CoreWebView2.Reload(); } }; tools.Children.Add(reset);
            var clear = new Button { Content = "Clear console" }; clear.Click += (_, _) => output.Clear(); tools.Children.Add(clear);
            // Collider outlines are a Preview aid (apps never draw them); the runtime reads this setting every frame.
            var colliders = new CheckBox { Content = "Show colliders", IsChecked = true, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), ToolTip = "Outline physics colliders. They're never drawn in exported apps." };
            colliders.Click += async (_, _) => await Script($"window.wysicraftHost.showColliders = {(colliders.IsChecked == true ? "true" : "false")}; 0");
            tools.Children.Add(colliders);
            // The live profiler: frame, engine, draw and script times and what's on screen, over the game.
            profilerBox.Click += async (_, _) => { profiling = profilerBox.IsChecked == true; await Script(ProfilerScript(profiling)); };
            tools.Children.Add(profilerBox);
            // Sound off for this preview window only (WebView2 mutes the page); the game is unchanged.
            muteBox.IsChecked = designer.Prefs().PreviewMuted;
            muteBox.Click += (_, _) => { bool muted = muteBox.IsChecked == true; if (view.CoreWebView2 != null) view.CoreWebView2.IsMuted = muted; designer.Prefs().PreviewMuted = muted; designer.SavePrefs(); };
            tools.Children.Add(muteBox);
            tools.Children.Add(new TextBlock { Text = "Click controls and use the keyboard to test • Server operations are simulated", Margin = new Thickness(12, 6, 4, 6), VerticalAlignment = VerticalAlignment.Center });
            var sizes = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(sizes, Dock.Top); layout.Children.Add(sizes);
            sizes.Children.Add(new TextBlock { Text = "Layout size (GUI pixels)", Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Center });
            var vw = new TextBox { Text = screen.Size.Width.ToString(), Width = 60 }; var vh = new TextBox { Text = screen.Size.Height.ToString(), Width = 60 }; sizes.Children.Add(vw); sizes.Children.Add(vh);
            var resize = new Button { Content = "Apply size" }; sizes.Children.Add(resize);
            var follow = new Button { Content = "Reset size", ToolTip = "Lay the game out for this window again, after Apply size." }; sizes.Children.Add(follow);
            fitBox.IsChecked = designer.Prefs().PreviewFitGame; sizes.Children.Add(fitBox);
            resize.Click += async (_, _) => {
                if (!int.TryParse(vw.Text, out int w) || !int.TryParse(vh.Text, out int h) || w < 16 || h < 16 || w > 16384 || h > 16384) { Print("LAYOUT", "Use sizes from 16 to 16384."); return; }
                await Script($"Wysicraft.app.setViewport({w},{h})"); Print("LAYOUT", project.Screens.Any(s => s.Responsive) ? $"Laid out for {w} × {h}." : "This screen uses a fixed layout. Enable Responsive layout in screen settings to resize controls.");
                await RefitAsync();
            };
            follow.Click += async (_, _) => { await Script("Wysicraft.app.setViewport(0,0)"); Print("LAYOUT", "Laid out for the preview window."); await RefitAsync(); };
            fitBox.Click += async (_, _) => { designer.Prefs().PreviewFitGame = fitBox.IsChecked == true; designer.SavePrefs(); await RefitAsync(); };
            // The space for the game changes with the window and with the console folding away.
            view.SizeChanged += async (_, _) => await RefitAsync();
            // The console and JavaScript panels, under a bar that minimizes them to give the game the room.
            var bottom = new DockPanel(); DockPanel.SetDock(bottom, Dock.Bottom); layout.Children.Add(bottom);
            var bar = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(37, 41, 48)) }; DockPanel.SetDock(bar, Dock.Top); bottom.Children.Add(bar);
            DockPanel.SetDock(consoleToggle, Dock.Right); bar.Children.Add(consoleToggle);
            var barTitle = new StackPanel { Orientation = Orientation.Horizontal };
            barTitle.Children.Add(new TextBlock { Text = "CONSOLE & JAVASCRIPT", Foreground = Brushes.LightSkyBlue, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
            barTitle.Children.Add(consoleNews); bar.Children.Add(barTitle);
            consoleToggle.Click += (_, _) => { designer.Prefs().PreviewConsoleMinimized = consoleArea.Visibility == Visibility.Visible; designer.SavePrefs(); ShowConsole(); };
            consoleArea.ColumnDefinitions.Add(new ColumnDefinition()); consoleArea.ColumnDefinitions.Add(new ColumnDefinition()); bottom.Children.Add(consoleArea);
            var consolePanel = new DockPanel(); consolePanel.Children.Add(Header("CONSOLE • events, actions and script output")); consolePanel.Children.Add(output); consoleArea.Children.Add(consolePanel);
            var scriptPanel = new DockPanel(); Grid.SetColumn(scriptPanel, 1); consoleArea.Children.Add(scriptPanel); scriptPanel.Children.Add(Header("JAVASCRIPT • runs like a client script on this screen"));
            ShowConsole();
            var run = new Button { Content = "Run JavaScript", HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(run, Dock.Bottom); scriptPanel.Children.Add(run); scriptPanel.Children.Add(code);
            run.Click += async (_, _) => { await ready.Task; await Script("Wysicraft.app.runScript(" + JsonSerializer.Serialize(code.Text) + ")"); };
            view.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x15, 0x18, 0x1D);
            layout.Children.Add(view);
            Window.Closing += (_, args) => { if (!closed) { args.Cancel = true; closing ??= CloseAsync(); } };
            Window.Closed += (_, _) => { closed = true; view.Dispose(); try { Directory.Delete(folder, true); } catch { } };
            Window.Loaded += async (_, _) =>
            {
                KeepOnScreen();
                await StartAsync();
            };
        }
        /// <summary>Minimized, the panels fold into their bar (which counts new console lines) and the game gets the room.</summary>
        void ShowConsole()
        {
            bool minimized = designer.Prefs().PreviewConsoleMinimized;
            consoleArea.Visibility = minimized ? Visibility.Collapsed : Visibility.Visible;
            consoleToggle.Content = minimized ? "Restore ▴" : "Minimize ▾";
            consoleToggle.ToolTip = minimized ? "Show the console and JavaScript panels." : "Fold the console and JavaScript panels away to give the game more room.";
            if (!minimized) { unseen = 0; unseenError = false; output.ScrollToEnd(); }
            consoleNews.Text = !minimized || unseen == 0 ? "" : unseen + " new line" + (unseen == 1 ? "" : "s") + (unseenError ? ", with errors" : "");
            consoleNews.Foreground = unseenError ? new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x7A)) : Brushes.LightGray;
        }
        /// <summary>A window that opened bigger than its screen, or partly off it, is brought fully onto it.</summary>
        void KeepOnScreen()
        {
            if (Window.WindowState != WindowState.Normal) return;
            var area = WorkArea(Window);
            double w = Math.Min(Window.ActualWidth, area.Width), h = Math.Min(Window.ActualHeight, area.Height);
            if (w < Window.ActualWidth) Window.Width = w; if (h < Window.ActualHeight) Window.Height = h;
            Window.Left = Math.Clamp(Window.Left, area.Left, Math.Max(area.Left, area.Right - w)); Window.Top = Math.Clamp(Window.Top, area.Top, Math.Max(area.Top, area.Bottom - h));
        }
        string shownScreen = "";
        /// <summary>Fit game to window: Preview zooms so the game's screen (with the margins the runtime leaves round it)
        /// exactly fills the space it has, smaller or larger. Only the zoom of this window changes, never the window's size
        /// or the game: a fixed screen is fitted at its design size, a responsive one at the size set with Apply size. A
        /// responsive screen with no size set lays itself out for the window, so it isn't zoomed.</summary>
        internal async Task RefitAsync()
        {
            if (closed || view.CoreWebView2 == null || view.ActualWidth < 1 || view.ActualHeight < 1) return;
            double zoom = 1;
            if (fitBox.IsChecked == true)
                try
                {
                    var info = JsonNode.Parse(await Script("(function(){ const a = Wysicraft._app; if (!a || !a.design) return null; const d = a.design;" +
                        " return { w: d.size.width, h: d.size.height, f: !!d.showFrame, r: !!d.responsive, vw: a.viewport ? a.viewport.width : 0, vh: a.viewport ? a.viewport.height : 0," +
                        " px: typeof a.padX === 'number' ? a.padX : -1, py: typeof a.padY === 'number' ? a.padY : -1 }; })()"));
                    if (info is JsonObject o)
                    {
                        double vw = (double)o["vw"]!, vh = (double)o["vh"]!;
                        // A fixed screen is always its design size; a responsive one is the size set with Apply size (if any).
                        (double w, double h) = !(bool)o["r"]! ? ((double)o["w"]!, (double)o["h"]!) : vw > 0 && vh > 0 ? (vw, vh) : (0, 0);
                        // The runtime's own margins round the screen (none for a web game without a frame).
                        double px = (double)o["px"]!, py = (double)o["py"]!;
                        if (px < 0) { px = 12; py = (bool)o["f"]! ? 32 : 12; }
                        if (w > 0 && h > 0) zoom = Math.Clamp(Math.Min(view.ActualWidth / (w + px), view.ActualHeight / (h + py)), 0.25, 5);
                    }
                }
                catch (Exception) { }
            if (!closed && Math.Abs(view.ZoomFactor - zoom) > 0.001) view.ZoomFactor = zoom;
        }
        static string ProfilerScript(bool on) => $"window.wysicraftHost.profiler = {(on ? "true" : "false")}; Wysicraft.app.setProfiler({(on ? "true" : "false")}); 0";
        static TextBlock Header(string title) { var label = new TextBlock { Text = title, Margin = new Thickness(6), Foreground = Brushes.LightSkyBlue, FontSize = 11 }; DockPanel.SetDock(label, Dock.Top); return label; }
        void Print(string category, string text)
        {
            if (closed) return;
            if (output.Text.Length > 100000) output.Text = output.Text[^50000..];
            output.AppendText($"[{category}] {text}\n"); output.ScrollToEnd();
            if (consoleArea.Visibility != Visibility.Visible) { unseen++; unseenError |= category == "ERROR"; ShowConsole(); }
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
                web.IsMuted = muteBox.IsChecked == true;
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
                    case "ready": ready.TrySetResult(); if (profiling) _ = Script(ProfilerScript(true)); _ = RefitAsync(); break;
                    case "event":
                    {
                        // Another screen may be another size.
                        if (S("screen").Length > 0 && S("screen") != shownScreen) { shownScreen = S("screen"); _ = RefitAsync(); }
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
        // MCP: ticks or clears the Profiler box, then (on) waits for its first figures.
        internal async Task SetProfilerAsync(bool on)
        {
            await WaitReady(); profiling = on; profilerBox.IsChecked = on; await Script(ProfilerScript(on));
            if (on) for (int i = 0; i < 50 && await Script("Wysicraft.app.profile() !== null") != "true"; i++) await Task.Delay(20);
        }
        internal async Task<string> SnapshotAsync()
        {
            await WaitReady();
            var node = JsonNode.Parse(JsonSerializer.Deserialize<string>(await Script("JSON.stringify(Wysicraft.app.snapshot())")) ?? "{}")!.AsObject();
            node["logs"] = output.Text.Length > 12000 ? output.Text[^12000..] : output.Text;
            if (profiling && JsonSerializer.Deserialize<string>(await Script("JSON.stringify(Wysicraft.app.profile())")) is string figures && figures != "null") node["profile"] = JsonNode.Parse(figures);
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
        // The game screen as a PNG, as players see it: collider outlines (a Preview aid) are left out, and only the
        // screen's own area is taken, without the space around it.
        internal async Task<byte[]> CaptureScreenAsync()
        {
            await WaitReady();
            string result = await Script("(() => { const app = Wysicraft._app, host = app.host, had = host.showColliders; host.showColliders = false; app.render();" +
                "const k = app.scale * app.dpr, w = Math.max(1, Math.round(app.ui.size.width * k)), h = Math.max(1, Math.round(app.ui.size.height * k));" +
                "const c = document.createElement('canvas'); c.width = w; c.height = h; c.getContext('2d').drawImage(app.canvas, Math.round(app.originX * k), Math.round(app.originY * k), w, h, 0, 0, w, h);" +
                "host.showColliders = had; app.render(); return c.toDataURL('image/png'); })()");
            string url = JsonSerializer.Deserialize<string>(result) ?? "";
            int comma = url.IndexOf(','); if (!url.StartsWith("data:image/png;base64,") || comma < 0) throw new InvalidOperationException("Preview couldn't capture the screen.");
            return Convert.FromBase64String(url[(comma + 1)..]);
        }
        internal bool IsOpen => !closed && Window.IsVisible;
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
        /// <summary>The window tools: Mute mutes the page, Minimize folds the console away (the game gets the room and new
        /// lines are counted), and Fit game to window zooms the game to fill the window without changing either.</summary>
        internal async Task VerifyWindowToolsAsync()
        {
            await WaitReady();
            void Click(System.Windows.Controls.Primitives.ButtonBase b) => b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            void Expect(bool ok, string what) { if (!ok) throw new InvalidOperationException("Preview window tools: " + what + "\n" + output.Text[Math.Max(0, output.Text.Length - 600)..]); }
            muteBox.IsChecked = true; Click(muteBox);
            Expect(view.CoreWebView2.IsMuted && designer.Prefs().PreviewMuted, "Mute mutes the preview");
            muteBox.IsChecked = false; Click(muteBox);
            Expect(!view.CoreWebView2.IsMuted && !designer.Prefs().PreviewMuted, "unticking Mute turns the sound back on");

            Window.UpdateLayout(); double before = view.ActualHeight;
            Click(consoleToggle); Window.UpdateLayout();
            Expect(consoleArea.Visibility == Visibility.Collapsed && view.ActualHeight >= before + 200 && (string)consoleToggle.Content == "Restore ▴", "Minimize folds the console away and the game gets the room");
            Print("TEST", "one"); Print("ERROR", "two");
            Expect(consoleNews.Text == "2 new lines, with errors", "while minimized, new lines are counted: " + consoleNews.Text);
            Click(consoleToggle); Window.UpdateLayout();
            Expect(consoleArea.Visibility == Visibility.Visible && Math.Abs(view.ActualHeight - before) < 1 && consoleNews.Text == "", "Restore brings it back and clears the count");

            // A game bigger than the window: with Fit game to window it's zoomed down to fit, and fills it; the window
            // and the game (its layout size and design) stay exactly as they were.
            string game = await Script("JSON.stringify({ v: Wysicraft._app.viewport, w: Wysicraft._app.design.size.width, h: Wysicraft._app.design.size.height })");
            Window.WindowState = WindowState.Normal; Window.Width = 560; Window.Height = 600; Window.UpdateLayout();
            async Task<JsonObject> Shown()
            {
                for (int i = 0; i < 20; i++) { await Task.Delay(50); await RefitAsync(); }
                var shown = JsonNode.Parse(JsonNode.Parse(await Script("JSON.stringify((function(){ const a = Wysicraft._app, s = a.scale; return { gw: (a.ui.size.width + a.padX) * s, gh: (a.ui.size.height + a.padY) * s, W: a.container.clientWidth, H: a.container.clientHeight }; })())"))!.GetValue<string>())!.AsObject();
                return shown;
            }
            fitBox.IsChecked = true; Click(fitBox);
            var fitted = await Shown();
            double gw = (double)fitted["gw"]!, gh = (double)fitted["gh"]!, W = (double)fitted["W"]!, H = (double)fitted["H"]!;
            Expect(gw <= W + 1 && gh <= H + 1 && (gw >= W - 2 || gh >= H - 2), $"the game is zoomed to fill the space: zoom {view.ZoomFactor:0.###}, game {gw:0}×{gh:0} in {W:0}×{H:0}");
            // A smaller window: the game is squeezed down with it, and still fits.
            double wider = view.ZoomFactor; Window.Height = 430; Window.UpdateLayout(); fitted = await Shown();
            (gw, gh, W, H) = ((double)fitted["gw"]!, (double)fitted["gh"]!, (double)fitted["W"]!, (double)fitted["H"]!);
            Expect(view.ZoomFactor < wider - 0.01 && gw <= W + 1 && gh <= H + 1 && (gw >= W - 2 || gh >= H - 2), $"a smaller window squeezes the game to fit: zoom {wider:0.###} → {view.ZoomFactor:0.###}, game {gw:0}×{gh:0} in {W:0}×{H:0}");
            Expect(Math.Abs(Window.Width - 560) < 1 && Math.Abs(Window.Height - 430) < 1, "fitting never resizes the window");
            Expect(await Script("JSON.stringify({ v: Wysicraft._app.viewport, w: Wysicraft._app.design.size.width, h: Wysicraft._app.design.size.height })") == game, "fitting leaves the game (its layout size and design) alone");
            // Folding the console away gives the game more room, and the fit follows.
            double zoomBefore = view.ZoomFactor;
            Click(consoleToggle); Window.UpdateLayout(); await Shown();
            Expect(view.ZoomFactor > zoomBefore + 0.01, $"the fit follows the space: zoom {zoomBefore:0.###} → {view.ZoomFactor:0.###} with the console folded");
            Click(consoleToggle); Window.UpdateLayout();
            fitBox.IsChecked = false; Click(fitBox); await Shown();
            Expect(Math.Abs(view.ZoomFactor - 1) < 0.001 && !designer.Prefs().PreviewFitGame, "unticked, the game is shown at its own size again");
            fitBox.IsChecked = true; Click(fitBox);
            await Script("Wysicraft.app.setViewport(0,0)");
            // Opening a screen of another size changes the fit's zoom, which resizes the page without a window resize:
            // the game must lay itself out again, or it draws small in a corner and clicks land in the wrong place.
            await Shown(); double zoomBeforeSwitch = view.ZoomFactor;
            await Script("(() => { const a = Wysicraft._app, other = Object.keys(a.screens).find(k => k !== a.ui.id); a.screens[other].size = { width: 256, height: 224 }; a.api().open(other); })()");
            string laid = "";
            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(50);
                laid = JsonNode.Parse(await Script("JSON.stringify((() => { const a = Wysicraft._app; return { cw: a.canvas.clientWidth, ch: a.canvas.clientHeight, W: a.container.clientWidth, H: a.container.clientHeight, dpr: a.dpr, real: window.devicePixelRatio, screen: a.ui.id }; })())"))!.GetValue<string>();
                var l = JsonNode.Parse(laid)!;
                if (Math.Abs((double)l["cw"]! - (double)l["W"]!) <= 1 && Math.Abs((double)l["ch"]! - (double)l["H"]!) <= 1 && Math.Abs((double)l["dpr"]! - (double)l["real"]!) < 0.001 && i > 10) break;
            }
            var after = JsonNode.Parse(laid)!;
            Expect(Math.Abs((double)after["cw"]! - (double)after["W"]!) <= 1 && Math.Abs((double)after["ch"]! - (double)after["H"]!) <= 1 && Math.Abs((double)after["dpr"]! - (double)after["real"]!) < 0.001,
                "after opening a screen of another size, the game is laid out for the page it has now: " + laid);
            Expect(Math.Abs(view.ZoomFactor - zoomBeforeSwitch) > 0.01, $"(the switch changed the fit: zoom {zoomBeforeSwitch:0.###} → {view.ZoomFactor:0.###})");
        }
        /// <summary>The window around the game (toolbars and console), for a look at the layout; the game itself draws elsewhere.</summary>
        internal void SaveWindowPicture(string path)
        {
            Window.UpdateLayout(); var root = (FrameworkElement)Window.Content;
            var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); png.Save(file);
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
        var prefs = Prefs(); bool muted = prefs.PreviewMuted, minimized = prefs.PreviewConsoleMinimized, fitGame = prefs.PreviewFitGame;
        prefs.PreviewConsoleMinimized = false;
        try { preview.Window.Show(); await preview.VerifyClickAsync(); await preview.VerifyWindowToolsAsync(); await preview.CaptureCanvas(capture); preview.SaveWindowPicture(capture + ".window.png"); }
        finally { await preview.CloseAsync(); prefs.PreviewMuted = muted; prefs.PreviewConsoleMinimized = minimized; prefs.PreviewFitGame = fitGame; SavePrefs(); }
    }
}
