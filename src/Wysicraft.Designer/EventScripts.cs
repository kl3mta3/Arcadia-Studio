using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wysicraft.Models;
using Wysicraft.Core;
using Wysicraft.Packaging;
using Handler = Wysicraft.Models.EventHandler;
namespace Wysicraft.Designer;

public partial class MainWindow
{
    void ChooseScriptTemplate(ScriptTemplate template) {
        var element = template.Global ? null : ui.Elements.FirstOrDefault(e=>selected.Contains(e.Id) && Wysicraft.Core.Registry.Controls[e.Type].Events.Contains("click"));
        EditEventScript(element?.Events ?? ui.Events, element == null ? "open" : "click", element?.Id ?? "", template.Server, true, template);
        RefreshScripts(editingScript);
        RefreshInspector();
    }
    void BuildEventScripts(StackPanel panel, Dictionary<string, UiEvent> events, string eventName, bool server, System.Action refresh)
    {
        string elementId = ui.Elements.FirstOrDefault(e => ReferenceEquals(e.Events, events))?.Id ?? "";
        var handler = events.TryGetValue(eventName, out var ev) ? server ? ev.Server : ev.Client : null;
        Heading(panel, "Script");
        var templates = new ComboBox { ItemsSource = ScriptTemplate.All.Where(t=>t.Server==server).ToList(), DisplayMemberPath = "Title", Margin = new Thickness(4), ToolTip = "Choose a template, edit its placeholder constants, then Save & Assign." };
        panel.Children.Add(templates);
        templates.SelectionChanged += (_,_) => Guard(()=> { if(templates.SelectedItem is ScriptTemplate template) { EditEventScript(events,eventName,elementId,server,true,template); refresh(); } });
        var description = new TextBlock { Text = handler?.Script.Length > 0 ? "Attached: " + handler.Script + " → " + handler.Function + " (" + handler.ScriptEngine + ")" : "No script attached. Click New Script to create one.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) };
        panel.Children.Add(description);
        var buttons = new WrapPanel(); panel.Children.Add(buttons);
        void Button(string title, System.Action action, bool enabled = true) { var button = new Button { Content = title, IsEnabled = enabled }; button.Click += (_, _) => Guard(action); buttons.Children.Add(button); }
        Button("New Script", () => { EditEventScript(events, eventName, elementId, server, true); refresh(); });
        Button("Edit Script", () => { EditEventScript(events, eventName, elementId, server, false); refresh(); }, handler?.Script.Length > 0);
        Button("Test Event", () => TestEvent(elementId, eventName));
        Button("Detach", () => { Change(); if (events.TryGetValue(eventName, out var entry)) { var current = server ? entry.Server : entry.Client; current.Script = ""; current.Function = ""; } refresh(); }, handler?.Script.Length > 0);
        panel.Children.Add(new TextBlock { Text = "New → write code → Save & Assign. Test Event runs client behavior and simulates server behavior.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(4) });
        var advanced = new StackPanel();
        var existing = new ComboBox { IsEditable = true, ItemsSource = project.Scripts.Keys.Where(p => p.StartsWith(server ? "scripts/server/" : "scripts/client/")).ToList(), Text = handler?.Script ?? "" };
        var function = new TextBox { Text = handler?.Function ?? "", ToolTip = "Function name" };
        advanced.Children.Add(new TextBlock { Text = "Existing file", Margin = new Thickness(4) }); advanced.Children.Add(existing);
        advanced.Children.Add(new TextBlock { Text = "Function", Margin = new Thickness(4) }); advanced.Children.Add(function);
        var attach = new Button { Content = "Attach existing script" }; attach.Click += (_, _) => Guard(() =>
        {
            string path = existing.Text; ValidateScriptPath(path);
            if (!path.StartsWith(server ? "scripts/server/" : "scripts/client/") || !project.Scripts.ContainsKey(path)) throw new InvalidDataException("Choose an existing script for this side.");
            CheckFunction(function.Text); Change(); var target = EnsureEventHandler(events, eventName, server); target.Script = path; target.Function = function.Text; refresh();
        }); advanced.Children.Add(attach);
        panel.Children.Add(new Expander { Header = "Use an existing script…", Content = advanced, Foreground = Brushes.LightGray, Margin = new Thickness(4) });
    }
    static Handler EnsureEventHandler(Dictionary<string, UiEvent> events, string name, bool server)
    {
        if (!events.TryGetValue(name, out var ev)) events[name] = ev = new(); return server ? ev.Server : ev.Client;
    }
    static void CheckFunction(string function)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(function, "^[a-zA-Z_][a-zA-Z0-9_]*$")) throw new InvalidDataException("Use a function name such as on_click.");
    }
    (string Path, string Function, string Source) NewEventScript(string element, string eventName, bool server)
    {
        string stem = ui.Id + "_" + (element.Length == 0 ? "screen" : element) + "_" + eventName;
        string prefix = server ? "scripts/server/" : "scripts/client/"; string path = prefix + stem + ".js"; int n = 2;
        while (project.Scripts.ContainsKey(path)) path = prefix + stem + "_" + n++ + ".js";
        string function = "on_" + eventName;
        string message = (element.Length == 0 ? ui.Id : element) + "." + eventName + " fired!";
        return (path, function, "function " + function + "(ctx) {\n    console.log(" + Json.Write(message) + ");\n    // Example: ctx.ui.setText(\"status\", \"It works!\");\n}\n");
    }
    void SaveEventScript(Dictionary<string, UiEvent> events, string eventName, bool server, string path, string function, string source, string engine = "standard")
    {
        ValidateScriptPath(path); CheckFunction(function);
        if (!path.StartsWith(server ? "scripts/server/" : "scripts/client/") || Limits.SizeOf(source) > Limits.For(project).ScriptBytes) throw new InvalidDataException($"Wrong script side or script exceeds {Limits.For(project).ScriptBytes / 1024} KiB.");
        Jint.Engine.PrepareScript(source); // Parse only: saving never executes user code.
        Change(); project.Scripts[path] = source;
        var handler = EnsureEventHandler(events, eventName, server); handler.Script = path; handler.Function = function; handler.ScriptEngine = engine;
        RefreshScripts(path);
        if (folder != null) { ProjectStore.SaveProject(project, folder); dirty = false; }
        Log("Saved and assigned " + function + " to " + eventName + (server ? ".Server" : ".Client") + (folder == null ? ". Save the project to choose its file." : "."));
    }
    void EditEventScript(Dictionary<string, UiEvent> events, string eventName, string element, bool server, bool create, ScriptTemplate? template = null)
    {
        SaveScriptText(); var handler = events.TryGetValue(eventName, out var ev) ? server ? ev.Server : ev.Client : null;
        var draft = NewEventScript(element, eventName, server);
        if (!create && handler?.Script.Length > 0) draft = (handler.Script, handler.Function, project.Scripts.GetValueOrDefault(handler.Script, ""));
        if (template != null) draft.Source = template.Source(draft.Function,project.Manifest.Id).Replace("__PROJECT__",project.Manifest.Id);
        var window = new Window { Owner = this, Title = $"{(element.Length == 0 ? ui.Id : element)} • {eventName}.{(server ? "Server" : "Client")}", Width = 820, Height = 580, Background = Brush("#25282E"), Foreground = Brushes.White, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var layout = new DockPanel { Margin = new Thickness(10) }; window.Content = layout;
        var info = new TextBlock { Text = "Automatically saved to " + draft.Path, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 4, 4, 10) }; DockPanel.SetDock(info, Dock.Top); layout.Children.Add(info);
        var top = new DockPanel(); DockPanel.SetDock(top, Dock.Top); layout.Children.Add(top); top.Children.Add(new TextBlock { Text = "Function", Width = 75, VerticalAlignment = VerticalAlignment.Center });
        var function = new TextBox { Text = draft.Function }; top.Children.Add(function);
        var engine = new ComboBox { ItemsSource = server ? new[] { "standard", "kubejs" } : new[] { "standard" }, SelectedItem = handler?.ScriptEngine ?? "standard", Margin = new Thickness(4), ToolTip = "KubeJS scripts run in Minecraft on the server. Export using Export for KubeJS." }; DockPanel.SetDock(engine, Dock.Top); layout.Children.Add(engine);
        if (template != null) engine.SelectedItem = template.Engine;
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        var status = new TextBlock { Text = "Save & Assign attaches this code to the selected event automatically.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) }; footer.Children.Add(status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; footer.Children.Add(actions);
        var editor = new TextBox { Text = draft.Source, AcceptsReturn = true, AcceptsTab = true, FontFamily = new FontFamily("Consolas"), FontSize = 14, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto }; layout.Children.Add(editor);
        var test = new Button { Content = "Test Function" }; actions.Children.Add(test);
        test.Click += (_, _) =>
        {
            try
            {
                CheckFunction(function.Text); Jint.Engine.PrepareScript(editor.Text);
                var staging = Json.CloneProject(project); var screen = staging.Screens.First(s => s.Id == ui.Id);
                staging.Scripts[draft.Path] = editor.Text;
                var target = EnsureEventHandler(element == "" ? screen.Events : screen.Elements.First(e => e.Id == element).Events, eventName, server);
                target.Script = draft.Path; target.Function = function.Text; target.ScriptEngine = (string)engine.SelectedItem;
                TestEvent(element, eventName, staging, window); status.Text = "Test finished. Save & Assign to keep this code.";
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        var save = new Button { Content = "Save & Assign" }; actions.Children.Add(save);
        save.Click += (_, _) => { try { SaveEventScript(events, eventName, server, draft.Path, function.Text, editor.Text, (string)engine.SelectedItem); window.DialogResult = true; } catch (Exception ex) { status.Text = ex.Message; } };
        if (server) {
            var example = new Button { Content = "KubeJS example" }; actions.Children.Insert(0, example);
            example.Click += (_, _) => {
                if (editor.Text != draft.Source && MessageBox.Show(window, "Replace this draft with the KubeJS example?", "Script example", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                engine.SelectedItem = "kubejs";
                editor.Text = "function " + function.Text + "(event) {\n    event.message('Button reached the Minecraft server!');\n    // event.runCommand('portal'); // Runs as the clicking player.\n    // event.ui.setText('status', 'Updated by KubeJS');\n    // event.server.runCommandSilent('say Hello'); // Server authority.\n}\n";
                status.Text = "Save & Assign, then Export for KubeJS. Test in Minecraft; Preview does not run KubeJS.";
            };
        }
        var cancel = new Button { Content = "Cancel", IsCancel = true }; actions.Children.Add(cancel);
        window.ShowDialog();
    }
    void TestEvent(string element, string eventName, Project? staging = null, Window? owner = null)
    {
        SaveScriptText();
        var preview = new PreviewSession(this, staging ?? Json.CloneProject(project), ui.Id);
        preview.Window.Owner = owner ?? this;
        preview.Window.Loaded += (_, _) => { if (element.Length > 0 || eventName != "open") preview.TriggerTest(element, eventName); };
        preview.Window.ShowDialog();
    }
    internal async Task VerifyEventScriptsAsync(string capture)
    {
        var original = Json.CloneProject(project); string originalUi = ui.Id;
        PreviewSession? preview = null;
        try
        {
            var element = ui.Elements.First(e => e.Type == "button");
            var draft = NewEventScript(element.Id, "click", false);
            SaveEventScript(element.Events, "click", false, draft.Path, draft.Function, draft.Source);
            var handler = element.Events["click"].Client;
            if (handler.Script != draft.Path || handler.Function != draft.Function || project.Scripts[draft.Path] != draft.Source) throw new InvalidOperationException("Save & Assign did not attach the new script");
            preview = new PreviewSession(this, Json.CloneProject(project), ui.Id); preview.Window.Show();
            await preview.VerifyEventTestAsync(element.Id, "click", element.Id + ".click fired!");
            await preview.CaptureCanvas(capture);
        }
        finally
        {
            if (preview != null) await preview.CloseAsync();
            project = original; ui = project.Screens.First(s => s.Id == originalUi); editingScript = null; selected.Clear(); dirty = false; history.Clear(); RefreshAll();
        }
    }
}
