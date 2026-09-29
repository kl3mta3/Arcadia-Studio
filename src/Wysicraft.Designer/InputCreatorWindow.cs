using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wysicraft.Core;
using Wysicraft.Models;
using Validation = Wysicraft.Core.Validation;
namespace Wysicraft.Designer;

// Advanced → Input creator: bind a key or pad button and say, in words, what you want it to do. The assistant on the
// other end of MCP writes the code; nothing reaches the project until it has been reviewed and saved.
//
// It needs MCP running, because the assistant is the thing that writes the code — so the Create buttons are the only
// ones disabled without it. Everything else (picking bindings, reviewing, deleting) works regardless.
public partial class MainWindow
{
    static readonly (string Id, string Name)[] InputDevices =
        [("keyboard", "Keyboard"), ("gamepad", "Gamepad (generic)"), ("xbox", "Xbox controller"), ("playstation", "PlayStation controller")];

    void ShowInputCreator()
    {
        var window = new Window { Owner = this, Title = "Input creator", Width = 920, Height = 650, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new Thickness(12) }; window.Content = root;

        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);
        var deviceRow = new WrapPanel();
        deviceRow.Children.Add(new TextBlock { Text = "Device ", VerticalAlignment = VerticalAlignment.Center });
        string device = "keyboard";
        var devices = new ComboBox { Width = 200, ItemsSource = InputDevices.Select(d => d.Name).ToList(), SelectedIndex = 0, Margin = new Thickness(0, 0, 12, 0) };
        deviceRow.Children.Add(devices);
        var add = new Button { Content = "+ Input", Padding = new Thickness(12, 2, 12, 2), ToolTip = "Add a row: pick a free button, then say what it should do." };
        deviceRow.Children.Add(add);
        head.Children.Add(deviceRow);
        var mcpNote = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), FontSize = 11 };
        head.Children.Add(mcpNote);

        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Opacity = 0.85 };
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);

        var rows = new StackPanel();
        root.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        bool Connected() => McpRunning;
        // The queue is shared with Ask Agent; this window is only about the input rows.
        List<AgentRequest> Mine() => [.. agentRequests.Where(r => r.Kind == "input")];
        var createAll = new Button { Content = "Create all", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 3, 12, 3) };
        var reviewAll = new Button { Content = "Review all", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 3, 12, 3) };
        var saveAll = new Button { Content = "Save all to project", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 3, 12, 3) };
        var deleteAll = new Button { Content = "Delete all", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 3, 12, 3) };
        var close = new Button { Content = "Close", IsCancel = true, Padding = new Thickness(12, 3, 12, 3) };
        foreach (var b in new[] { createAll, reviewAll, saveAll, deleteAll, close }) bottom.Children.Add(b);

        void Redraw()
        {
            rows.Children.Clear();
            mcpNote.Text = Connected()
                ? "MCP is running: Create sends the request to the assistant, which writes the code and sends it back. Review shows what it wrote, Test checks it, and nothing touches the project until you press Save."
                : "MCP is not running — Create needs it, because the assistant is what writes the code. Press Start MCP on the main window, then come back. You can still add rows and pick buttons.";
            mcpNote.Foreground = Connected() ? Brushes.LightGray : Brushes.Goldenrod;
            foreach (var request in Mine()) rows.Children.Add(Row(request));
            int waiting = Mine().Count(r => r.Status == "waiting");
            createAll.IsEnabled = Connected() && Mine().Any(r => r.Status is "draft" or "failed");
            reviewAll.IsEnabled = Mine().Any(r => r.Status == "answered");
            saveAll.IsEnabled = Mine().Any(r => r.Status == "answered");
            deleteAll.IsEnabled = Mine().Any();
            if (waiting > 0) status.Text = $"{waiting} request(s) queued. The assistant does not get interrupted — say \"check Arcadia Studio\" in your chat with it. This list updates by itself when it answers.";
        }
        Action watch = () => Dispatcher.Invoke(Redraw);
        agentRequestWatchers.Add(watch);
        window.Closed += (_, _) => agentRequestWatchers.Remove(watch);

        Border Row(AgentRequest request)
        {
            var body = new StackPanel();
            var card = new Border
            {
                BorderBrush = new SolidColorBrush(request.Status switch
                {
                    "answered" => Color.FromRgb(90, 140, 90), "waiting" => Color.FromRgb(150, 130, 60),
                    "saved" => Color.FromRgb(70, 90, 130), "failed" => Color.FromRgb(150, 80, 80), _ => Color.FromRgb(69, 75, 86)
                }),
                BorderThickness = new Thickness(1), Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 8), Child = body
            };

            var line = new WrapPanel();
            line.Children.Add(new TextBlock { Text = "Button ", VerticalAlignment = VerticalAlignment.Center });
            var free = FreeBindings(request.Device);
            if (request.Binding.Length > 0 && !free.Contains(request.Binding)) free.Insert(0, request.Binding);
            var binding = new ComboBox
            {
                Width = 190, Margin = new Thickness(0, 0, 10, 0),
                ItemsSource = free.Select(b => BindingLabel(request.Device, b)).ToList(),
                SelectedIndex = Math.Max(0, free.IndexOf(request.Binding)),
                ToolTip = "Only buttons no input already uses are listed."
            };
            binding.SelectionChanged += (_, _) => { if (binding.SelectedIndex >= 0 && binding.SelectedIndex < free.Count) request.Binding = free[binding.SelectedIndex]; };
            if (free.Count > 0 && request.Binding.Length == 0) request.Binding = free[0];
            line.Children.Add(binding);

            line.Children.Add(new TextBlock { Text = "Name ", VerticalAlignment = VerticalAlignment.Center });
            var name = new TextBox { Width = 120, Text = request.Name, Margin = new Thickness(0, 0, 10, 0), ToolTip = "The input's name in the project. Leave it empty and the assistant will pick one." };
            name.LostFocus += (_, _) => request.Name = name.Text.Trim();
            line.Children.Add(name);

            line.Children.Add(new TextBlock { Text = "Action ", VerticalAlignment = VerticalAlignment.Center });
            var actions = new List<string> { "(let the assistant choose)" };
            actions.AddRange(Registry.ClientActions.OrderBy(a => a));
            var action = new ComboBox { Width = 170, ItemsSource = actions, SelectedIndex = Math.Max(0, actions.IndexOf(request.Action)), ToolTip = "Lean on one kind of action, or leave it open." };
            action.SelectionChanged += (_, _) => request.Action = action.SelectedIndex <= 0 ? "" : actions[action.SelectedIndex];
            line.Children.Add(action);
            line.Children.Add(new TextBlock { Text = "   " + request.Status, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, FontSize = 11 });
            body.Children.Add(line);

            var want = new TextBox
            {
                Text = request.Want, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 44,
                Margin = new Thickness(0, 6, 0, 0),
                ToolTip = "What should this button do? Plain words. The assistant can see the open project, so name your controls and variables."
            };
            if (request.Want.Length == 0) want.Text = "";
            want.LostFocus += (_, _) => request.Want = want.Text.Trim();
            body.Children.Add(want);
            body.Children.Add(new TextBlock { Text = "For example: \"pause the game and show the menu panel\", or \"fire a shot from the player towards the mouse\".", Opacity = 0.55, FontSize = 10, Margin = new Thickness(0, 2, 0, 0) });

            if (request.Notes.Length > 0) body.Children.Add(new TextBlock { Text = request.Notes, TextWrapping = TextWrapping.Wrap, Opacity = 0.8, FontSize = 11, Margin = new Thickness(0, 6, 0, 0) });
            if (request.Problem.Length > 0) body.Children.Add(new TextBlock { Text = request.Problem, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed, FontSize = 11, Margin = new Thickness(0, 6, 0, 0) });

            var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            Button Small(string text, string tip, Action click, bool enabled = true)
            {
                var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 2, 10, 2), ToolTip = tip, IsEnabled = enabled };
                b.Click += (_, _) => Guard(click);
                return b;
            }
            buttons.Children.Add(Small("Create", "Send this to the assistant to write.", () => Create(request), Connected() && request.Status is "draft" or "failed" or "answered"));
            buttons.Children.Add(Small("Review", "Show exactly what will be added.", () => Review(request), request.Status is "answered" or "saved"));
            buttons.Children.Add(Small("Test", "Check the script parses and the project would still validate with it.", () => Test(request), request.Status == "answered"));
            buttons.Children.Add(Small("Save to project", "Add the input and its script, as one undo step.", () => Save(request), request.Status == "answered"));
            buttons.Children.Add(Small("Delete", "Remove this row.", () => Delete(request)));
            body.Children.Add(buttons);
            return card;
        }

        async void Create(AgentRequest request)
        {
            if (!Connected()) throw new InvalidOperationException("Start MCP first — the assistant is what writes the code.");
            if (request.Binding.Length == 0) throw new InvalidOperationException("Pick a button for this row first.");
            if (request.Want.Trim().Length == 0) throw new InvalidOperationException("Say what the button should do first.");
            request.Status = "waiting"; request.Problem = ""; request.Notes = "";
            Redraw();
            // Being honest about this matters: the assistant is not listening, it is asked. Without saying so, a
            // request that is sitting there perfectly happily looks exactly like the app having hung.
            Log("Input creator: " + request.Id + " is waiting for the assistant (" + request.Want + ").");
            // Start an assistant of our own if one can be started; otherwise it stays queued to be picked up in chat.
            if (CanRunAgent)
            {
                status.Text = "Starting the assistant…";
                var run = await RunAgentAsync(AgentPrompt(request), line => Dispatcher.Invoke(() => status.Text = line));
                if (!run.Ok) { request.Problem = run.Trouble; Log("Input creator: " + run.Trouble); }
                Redraw();
                if (run.Ok) { status.Text = run.Text.Length > 0 ? run.Text : "The assistant finished; review what it wrote."; return; }
            }
            status.Text = (CanAskDirectly ? "Queued." : WhyQueued) + " Say \"check Arcadia Studio\" in your chat with the assistant and it will pick this up and reply here.";
        }
        void Review(AgentRequest request)
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine("Input:   " + (request.Name.Length > 0 ? request.Name : "(unnamed)"));
            report.AppendLine("Device:  " + request.Device + "  ·  button " + request.Binding);
            report.AppendLine("Asked:   " + request.Want);
            if (request.Notes.Length > 0) report.AppendLine("Notes:   " + request.Notes);
            report.AppendLine();
            report.AppendLine(request.Script.Length > 0 ? $"Script scripts/client/{request.Name}_input.js, function {request.Function}:\n\n{request.Script}" : "No script — this one only adds the input.");
            var view = new Window
            {
                Owner = window, Title = "Review · " + request.Id, Width = 720, Height = 520, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new ScrollViewer { Content = new TextBox { Text = report.ToString(), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas, monospace"), FontSize = 12, Margin = new Thickness(8) }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
            };
            view.ShowDialog();
        }
        void Test(AgentRequest request)
        {
            // Applied to a copy: the answer is checked against the real project without touching it.
            var trial = Json.Clone(project);
            string problem = ApplyRequest(trial, trial.Screens.First(s => s.Id == ui.Id), request);
            if (problem.Length > 0) { request.Problem = problem; Redraw(); throw new InvalidOperationException(problem); }
            var errors = Validation.Errors(trial);
            if (errors.Count > 0) { request.Problem = "It would not validate: " + string.Join("; ", errors.Take(3)); Redraw(); throw new InvalidOperationException(request.Problem); }
            request.Problem = "";
            status.Text = $"{request.Id}: the script parses and the project still validates with it. Save to project adds it for real.";
            Redraw();
        }
        void Save(AgentRequest request)
        {
            Change();
            string problem = ApplyRequest(project, ui, request);
            if (problem.Length > 0) { history.Undo(); request.Problem = problem; Redraw(); throw new InvalidOperationException(problem); }
            var errors = Validation.Errors(project);
            if (errors.Count > 0) { history.Undo(); request.Problem = "It would not validate: " + string.Join("; ", errors.Take(3)); Redraw(); throw new InvalidOperationException(request.Problem); }
            request.Status = "saved"; request.Problem = "";
            RefreshAll(); Redraw();
            Log($"Input creator: added the \"{request.Name}\" input" + (request.Script.Length > 0 ? " and its script." : "."));
        }
        void Delete(AgentRequest request)
        {
            if (MessageBox.Show(window, $"Delete this row?\n\n{request.Name} · {request.Binding} · {request.Want}", "Input creator", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            agentRequests.Remove(request); Redraw();
        }

        devices.SelectionChanged += (_, _) => { device = InputDevices[devices.SelectedIndex].Id; Redraw(); };
        add.Click += (_, _) => Guard(() =>
        {
            if (Mine().Count >= 32) throw new InvalidOperationException("That is enough rows for one sitting.");
            var free = FreeBindings(device);
            if (free.Count == 0) throw new InvalidOperationException("Every button on that device is already bound to an input.");
            agentRequests.Add(new AgentRequest { Device = device, Binding = free[0] });
            Redraw();
        });
        createAll.Click += (_, _) => Guard(() =>
        {
            foreach (var r in Mine().Where(r => r.Status is "draft" or "failed").ToList())
                if (r.Binding.Length > 0 && r.Want.Trim().Length > 0) Create(r);
            Redraw();
        });
        reviewAll.Click += (_, _) => Guard(() => { foreach (var r in Mine().Where(r => r.Status == "answered").ToList()) Review(r); });
        saveAll.Click += (_, _) => Guard(() =>
        {
            var ready = Mine().Where(r => r.Status == "answered").ToList();
            if (ready.Count == 0) return;
            if (MessageBox.Show(window, $"Add {ready.Count} input(s) and their scripts to the project?", "Input creator", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            foreach (var r in ready) Save(r);
        });
        deleteAll.Click += (_, _) => Guard(() =>
        {
            if (!Mine().Any()) return;
            if (MessageBox.Show(window, $"Delete all {Mine().Count} row(s)? Anything not saved to the project is lost.", "Input creator", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            agentRequests.RemoveAll(r => r.Kind == "input"); Redraw();
        });
        close.Click += (_, _) => window.Close();

        Redraw(); window.Show();
    }

    /// <summary>Puts an answered request into a project: the input, and its script wired to input_pressed. Returns
    /// what went wrong, or "". Used by both Test (on a copy) and Save (for real), so they can never disagree.</summary>
    string ApplyRequest(Project target, UiDefinition screen, AgentRequest request)
    {
        if (request.Name.Length == 0 || !Validation.Variable(request.Name)) return "That input has no usable name.";
        if (target.Manifest.Inputs.Any(i => i.Name == request.Name)) return $"An input called \"{request.Name}\" already exists.";
        if (request.Binding.Length == 0) return "No button was chosen.";
        bool keyboard = request.Device == "keyboard";
        target.Manifest.Inputs.Add(new GameInput
        {
            Name = request.Name,
            Keys = keyboard ? [request.Binding] : [],
            Buttons = keyboard ? [] : [request.Binding]
        });
        if (request.Script.Length == 0) return "";
        string path = $"scripts/client/{request.Name}_input.js";
        target.Scripts[path] = request.Script;
        if (!screen.Events.TryGetValue("input_pressed", out var handler)) screen.Events["input_pressed"] = handler = new UiEvent();
        if (handler.Client.Script.Length > 0 && handler.Client.Script != path)
            return $"The screen's input_pressed event already runs {handler.Client.Script}. Have the assistant add to that script instead.";
        handler.Client.Script = path; handler.Client.Function = request.Function;
        if (screen.TickInterval == 0 && request.Script.Contains("tick")) screen.TickInterval = 16;
        return "";
    }
}
