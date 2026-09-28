using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wysicraft.Core;
using Wysicraft.Models;
using Validation = Wysicraft.Core.Validation;
namespace Wysicraft.Designer;

// Advanced → State graphs (web and desktop): sprite state machines for the current screen. A graph drives one
// sprite: each state plays one of its clips, and each way out of a state is a condition. The runtime advances it
// every frame, so idle → run → jump → land needs no script.
//
// Shaped like the Animations window beside it: a list of graphs on the left, the selected one edited on the right,
// working on a copy until Apply.
public partial class MainWindow
{
    // The conditions worth offering, since they cover nearly every graph anyone writes.
    static readonly (string When, string Means)[] ConditionExamples =
    [
        ("clipDone", "the clip has finished (a one-shot clip: 'jump: 4,5 @12 once')"),
        ("input:jump", "that input is held down"),
        ("!input:move", "that input is not held"),
        ("clipStep >= 3", "three frames into the clip"),
        ("hp < 20", "a screen variable"),
        ("clipDone && input:move", "both at once"),
        ("", "always — it moves on the next frame"),
    ];

    void ShowStateGraphsWindow()
    {
        var screen = ui;
        var graphs = Json.Clone(screen.StateGraphs);
        var window = new Window { Owner = this, Title = "State graphs · " + screen.Id + " · web & desktop", Width = 880, Height = 620, WindowStartupLocation = WindowStartupLocation.Manual, Left = Left + Math.Max(0, ActualWidth - 900), Top = Top + 80 };
        var root = new DockPanel { Margin = new Thickness(10) }; window.Content = root;
        var left = new DockPanel { Width = 190, Margin = new Thickness(0, 0, 10, 0) }; DockPanel.SetDock(left, Dock.Left); root.Children.Add(left);
        var addGraph = new Button { Content = "+ New graph", Margin = new Thickness(0, 0, 0, 4) }; DockPanel.SetDock(addGraph, Dock.Top); left.Children.Add(addGraph);
        var removeGraph = new Button { Content = "Delete graph", Margin = new Thickness(0, 4, 0, 0) }; DockPanel.SetDock(removeGraph, Dock.Bottom); left.Children.Add(removeGraph);
        var picker = new ListBox(); left.Children.Add(picker);
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var editor = new StackPanel(); root.Children.Add(new ScrollViewer { Content = editor, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        StateGraph? current = null;

        var sprites = screen.Elements.Where(e => e.Type == "sprite").Select(e => e.Id).ToList();
        List<string> ClipsOf(string target)
        {
            var e = screen.Elements.FirstOrDefault(x => x.Id == target);
            if (e == null) return [];
            try { return [.. SpriteClips.Parse(e.Clips).Keys]; } catch (FormatException) { return []; }
        }
        void Pick() { picker.ItemsSource = graphs.Select(g => g.Id).ToList(); if (current != null) picker.SelectedItem = current.Id; }
        picker.SelectionChanged += (_, _) => { current = graphs.FirstOrDefault(g => g.Id == picker.SelectedItem as string); Edit(); };
        addGraph.Click += (_, _) => Guard(() =>
        {
            if (sprites.Count == 0) throw new InvalidOperationException("A state graph drives a sprite. Add a Sprite control to this screen first.");
            int n = 1; while (graphs.Any(g => g.Id == "graph" + n)) n++;
            // A graph that already works: the target's first clip as an idle state.
            var target = selected.FirstOrDefault(s => sprites.Contains(s)) ?? sprites[0];
            var clips = ClipsOf(target);
            current = new StateGraph { Id = "graph" + n, Target = target, States = [new() { Name = "idle", Clip = clips.FirstOrDefault() ?? "" }] };
            graphs.Add(current); Pick(); Edit();
        });
        removeGraph.Click += (_, _) => { if (current == null) return; graphs.Remove(current); current = graphs.FirstOrDefault(); Pick(); Edit(); };

        TextBox Number(int value, Action<int> set, double width = 56)
        {
            var box = new TextBox { Text = value.ToString(CultureInfo.InvariantCulture), Width = width, Margin = new Thickness(0, 0, 6, 0) };
            box.LostFocus += (_, _) => { if (int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) set(v); else box.BorderBrush = Brushes.IndianRed; };
            return box;
        }
        void Edit()
        {
            editor.Children.Clear();
            if (current == null)
            {
                editor.Children.Add(new TextBlock
                {
                    Text = "A state graph decides which clip a sprite plays. Each state names one of the sprite's clips; each way out of a state is a condition, checked every frame. "
                         + "Build idle → run → jump → land here and the game needs no animation script at all.\n\n"
                         + "Conditions are ordinary screen conditions, plus:\n"
                         + string.Join('\n', ConditionExamples.Where(c => c.When.Length > 0).Select(c => "    " + c.When + "   — " + c.Means))
                         + "\n\nThe highest priority that is true wins, and one transition happens per frame. Entering a state restarts its clip.",
                    TextWrapping = TextWrapping.Wrap, Opacity = 0.8
                });
                return;
            }
            var g = current;
            var clips = ClipsOf(g.Target);
            var head = new WrapPanel(); editor.Children.Add(head);
            head.Children.Add(new TextBlock { Text = "ID ", VerticalAlignment = VerticalAlignment.Center });
            var id = new TextBox { Text = g.Id, Width = 130, Margin = new Thickness(0, 0, 12, 0) }; id.LostFocus += (_, _) => { g.Id = id.Text.Trim(); Pick(); }; head.Children.Add(id);
            head.Children.Add(new TextBlock { Text = "Sprite ", VerticalAlignment = VerticalAlignment.Center });
            var target = new ComboBox { ItemsSource = sprites, SelectedItem = g.Target, Width = 150, Margin = new Thickness(0, 0, 12, 0), ToolTip = "The sprite this graph plays clips on" };
            target.SelectionChanged += (_, _) => { g.Target = target.SelectedItem as string ?? ""; Edit(); }; head.Children.Add(target);
            head.Children.Add(new TextBlock { Text = "Starts in ", VerticalAlignment = VerticalAlignment.Center });
            var start = new ComboBox { ItemsSource = g.States.Select(s => s.Name).ToList(), SelectedItem = g.Start.Length > 0 ? g.Start : g.States.FirstOrDefault()?.Name, Width = 120, ToolTip = "The state the screen opens in" };
            start.SelectionChanged += (_, _) => g.Start = start.SelectedItem as string ?? ""; head.Children.Add(start);
            if (clips.Count == 0)
                editor.Children.Add(new TextBlock { Text = "That sprite has no clips yet. Set its Clips (for example \"idle: 0; run: 1-6 @12; jump: 7,8 @8 once\") in the Inspector or the Sprite sheet editor, then come back.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Goldenrod, Margin = new Thickness(0, 6, 0, 0) });

            foreach (var state in g.States.ToList())
            {
                var card = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(69, 75, 86)), BorderThickness = new Thickness(1), Padding = new Thickness(8), Margin = new Thickness(0, 8, 0, 0) };
                var body = new StackPanel(); card.Child = body; editor.Children.Add(card);
                var row = new WrapPanel(); body.Children.Add(row);
                row.Children.Add(new TextBlock { Text = "State ", VerticalAlignment = VerticalAlignment.Center });
                var name = new TextBox { Text = state.Name, Width = 120, Margin = new Thickness(0, 0, 10, 0) };
                name.LostFocus += (_, _) =>
                {
                    string was = state.Name, now = name.Text.Trim();
                    if (now == was || now.Length == 0) return;
                    // Anything pointing at the old name follows it, so renaming a state can't quietly break the graph.
                    state.Name = now;
                    foreach (var t in g.States.SelectMany(s => s.Transitions).Where(t => t.To == was)) t.To = now;
                    if (g.Start == was) g.Start = now;
                    Edit();
                };
                row.Children.Add(name);
                row.Children.Add(new TextBlock { Text = "plays clip ", VerticalAlignment = VerticalAlignment.Center });
                var clip = new ComboBox { ItemsSource = clips, SelectedItem = state.Clip, Width = 130, Margin = new Thickness(0, 0, 10, 0), IsEditable = true, Text = state.Clip };
                clip.SelectionChanged += (_, _) => { if (clip.SelectedItem is string c) state.Clip = c; };
                clip.LostFocus += (_, _) => state.Clip = clip.Text.Trim();
                row.Children.Add(clip);
                var removeState = new Button { Content = "Remove state" };
                removeState.Click += (_, _) => { g.States.Remove(state); Edit(); };
                row.Children.Add(removeState);

                foreach (var way in state.Transitions.ToList())
                {
                    var line = new WrapPanel { Margin = new Thickness(14, 4, 0, 0) }; body.Children.Add(line);
                    line.Children.Add(new TextBlock { Text = "→ ", VerticalAlignment = VerticalAlignment.Center });
                    var to = new ComboBox { ItemsSource = g.States.Select(s => s.Name).ToList(), SelectedItem = way.To, Width = 120, Margin = new Thickness(0, 0, 8, 0) };
                    to.SelectionChanged += (_, _) => way.To = to.SelectedItem as string ?? ""; line.Children.Add(to);
                    line.Children.Add(new TextBlock { Text = "when ", VerticalAlignment = VerticalAlignment.Center });
                    var when = new ComboBox
                    {
                        IsEditable = true, Text = way.When, Width = 220, Margin = new Thickness(0, 0, 8, 0),
                        ItemsSource = ConditionExamples.Select(c => c.When).ToList(),
                        ToolTip = "Checked every frame. " + string.Join("   ", ConditionExamples.Where(c => c.When.Length > 0).Select(c => c.When + " = " + c.Means))
                    };
                    when.LostFocus += (_, _) => way.When = when.Text.Trim();
                    when.SelectionChanged += (_, _) => { if (when.SelectedItem is string c) way.When = c; };
                    line.Children.Add(when);
                    line.Children.Add(new TextBlock { Text = "priority ", VerticalAlignment = VerticalAlignment.Center });
                    line.Children.Add(Number(way.Priority, v => way.Priority = Math.Clamp(v, -1000, 1000)));
                    var removeWay = new Button { Content = "✕", ToolTip = "Remove this way out" };
                    removeWay.Click += (_, _) => { state.Transitions.Remove(way); Edit(); }; line.Children.Add(removeWay);
                }
                var addWay = new Button { Content = "+ Way out of " + state.Name, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(14, 6, 0, 0) };
                addWay.Click += (_, _) =>
                {
                    // Defaults to the commonest transition there is: this clip has finished, go somewhere else.
                    state.Transitions.Add(new StateTransition { To = g.States.FirstOrDefault(s => s.Name != state.Name)?.Name ?? state.Name, When = "clipDone" });
                    Edit();
                };
                body.Children.Add(addWay);
            }
            var addState = new Button { Content = "+ State", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
            addState.Click += (_, _) =>
            {
                int n = 1; while (g.States.Any(s => s.Name == "state" + n)) n++;
                g.States.Add(new AnimationState { Name = "state" + n, Clip = clips.FirstOrDefault(c => !g.States.Any(s => s.Clip == c)) ?? clips.FirstOrDefault() ?? "" });
                Edit();
            };
            editor.Children.Add(addState);
        }

        var apply = new Button { Content = "Apply", IsDefault = true, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 3, 12, 3) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(12, 3, 12, 3) };
        apply.Click += (_, _) => Guard(() =>
        {
            System.Windows.Input.Keyboard.ClearFocus();
            var ids = new HashSet<string>();
            foreach (var g in graphs)
            {
                if (!Validation.Id(g.Id) || !ids.Add(g.Id)) throw new InvalidOperationException($"\"{g.Id}\": state graph IDs must be unique lowercase IDs.");
                if (g.States.Count == 0) throw new InvalidOperationException($"\"{g.Id}\" has no states.");
                var names = new HashSet<string>();
                foreach (var s in g.States)
                    if (!Validation.Variable(s.Name) || !names.Add(s.Name)) throw new InvalidOperationException($"\"{g.Id}\": state names must be unique, using letters, digits and _ ({s.Name}).");
                foreach (var s in g.States) foreach (var t in s.Transitions)
                    if (!names.Contains(t.To)) throw new InvalidOperationException($"\"{g.Id}\": {s.Name} goes to \"{t.To}\", which is not one of its states.");
            }
            Change(); screen.StateGraphs = graphs; window.Close(); RefreshInspector();
        });
        cancel.Click += (_, _) => window.Close();
        bottom.Children.Add(new TextBlock { Text = "Preview (F5) to watch it run. ", Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        bottom.Children.Add(apply); bottom.Children.Add(cancel);
        current = graphs.FirstOrDefault(); Pick(); Edit(); window.Show();
    }
}
