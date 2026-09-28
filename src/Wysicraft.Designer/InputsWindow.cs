using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wysicraft.Core;
using Wysicraft.Models;
using Validation = Wysicraft.Core.Validation;
namespace Wysicraft.Designer;

// Advanced → Inputs (web and desktop): named inputs such as "jump", each pressed by keys and/or gamepad buttons.
// Screens react with input_pressed / input_released events, scripts with ctx.input.isDown(name) and
// ctx.input.axis(name), and a control's "Presses input" lets an on-screen button press one too.
// Clicks, hover and the Key event keep working exactly as before; inputs only add to them.
public partial class MainWindow
{
    static readonly Dictionary<string, Dictionary<string, string>> ButtonLabels = new()
    {
        ["Xbox"] = new() { ["a"] = "A", ["b"] = "B", ["x"] = "X", ["y"] = "Y", ["lb"] = "LB", ["rb"] = "RB", ["lt"] = "LT", ["rt"] = "RT", ["back"] = "View", ["start"] = "Menu", ["ls"] = "Left stick press", ["rs"] = "Right stick press", ["dpad_up"] = "D-pad up", ["dpad_down"] = "D-pad down", ["dpad_left"] = "D-pad left", ["dpad_right"] = "D-pad right", ["home"] = "Xbox button" },
        ["PlayStation"] = new() { ["a"] = "Cross ✕", ["b"] = "Circle ○", ["x"] = "Square □", ["y"] = "Triangle △", ["lb"] = "L1", ["rb"] = "R1", ["lt"] = "L2", ["rt"] = "R2", ["back"] = "Create", ["start"] = "Options", ["ls"] = "L3", ["rs"] = "R3", ["dpad_up"] = "D-pad up", ["dpad_down"] = "D-pad down", ["dpad_left"] = "D-pad left", ["dpad_right"] = "D-pad right", ["home"] = "PS button" },
        ["Generic"] = new() { ["a"] = "Button 1 (bottom)", ["b"] = "Button 2 (right)", ["x"] = "Button 3 (left)", ["y"] = "Button 4 (top)", ["lb"] = "Left bumper", ["rb"] = "Right bumper", ["lt"] = "Left trigger", ["rt"] = "Right trigger", ["back"] = "Select", ["start"] = "Start", ["ls"] = "Left stick press", ["rs"] = "Right stick press", ["dpad_up"] = "D-pad up", ["dpad_down"] = "D-pad down", ["dpad_left"] = "D-pad left", ["dpad_right"] = "D-pad right", ["home"] = "Home" },
    };
    // Presets add typical inputs; existing inputs with the same name get the extra keys or buttons.
    static readonly (string Name, string[] Keys, string[] Buttons, string Axis)[] KeyboardPreset =
        [("left", ["a", "left"], [], ""), ("right", ["d", "right"], [], ""), ("up", ["w", "up"], [], ""), ("down", ["s", "down"], [], ""), ("jump", ["space"], [], ""), ("action", ["e", "enter"], [], ""), ("pause", ["p"], [], "")];
    static readonly (string Name, string[] Keys, string[] Buttons, string Axis)[] GamepadPreset =
        [("left", [], ["dpad_left"], "left_x-"), ("right", [], ["dpad_right"], "left_x+"), ("up", [], ["dpad_up"], "left_y-"), ("down", [], ["dpad_down"], "left_y+"), ("jump", [], ["a"], ""), ("action", [], ["x"], ""), ("back", [], ["b"], ""), ("pause", [], ["start"], "")];

    void ShowInputsWindow()
    {
        var inputs = Json.Clone(project.Manifest.Inputs);
        var window = new Window { Owner = this, Title = "Inputs · web & desktop", Width = 760, Height = 560, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new Thickness(12) }; window.Content = root;
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "An input is a name like \"jump\" that keys and gamepad buttons press. Screens get input_pressed / input_released events (value = the name); scripts can ask ctx.input.isDown('jump') and ctx.input.axis('left'). Clicks, hover and the Key event are unchanged.", TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
        var presets = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) }; top.Children.Add(presets);
        var labelStyle = new ComboBox { ItemsSource = ButtonLabels.Keys.ToList(), SelectedIndex = 0, Width = 120, Margin = new Thickness(0, 0, 12, 0) };
        presets.Children.Add(new TextBlock { Text = "Show gamepad buttons as ", VerticalAlignment = VerticalAlignment.Center }); presets.Children.Add(labelStyle);
        presets.Children.Add(new TextBlock { Text = "Add preset: ", VerticalAlignment = VerticalAlignment.Center });
        var list = new StackPanel(); root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Insert(1, bottom);
        void Preset((string Name, string[] Keys, string[] Buttons, string Axis)[] preset)
        {
            foreach (var p in preset)
            {
                var input = inputs.FirstOrDefault(i => i.Name == p.Name); if (input == null) { input = new GameInput { Name = p.Name }; inputs.Add(input); }
                foreach (var k in p.Keys) if (!input.Keys.Contains(k)) input.Keys.Add(k);
                foreach (var b in p.Buttons) if (!input.Buttons.Contains(b)) input.Buttons.Add(b);
                if (p.Axis.Length > 0 && input.Axis.Length == 0) input.Axis = p.Axis;
                // A direction also gets the matching drag, so the preset is playable on a touch screen as it stands.
                if (input.Touch.Length == 0 && p.Name is "left" or "right" or "up" or "down") input.Touch = "drag_" + p.Name;
            }
            Fill();
        }
        foreach (var (label, preset) in new[] { ("Keyboard", KeyboardPreset), ("Xbox", GamepadPreset), ("PlayStation", GamepadPreset), ("Generic gamepad", GamepadPreset) })
        { var b = new Button { Content = label, Margin = new Thickness(0, 0, 6, 0) }; b.Click += (_, _) => { if (ButtonLabels.ContainsKey(label)) labelStyle.SelectedItem = label; else if (label.StartsWith("Generic")) labelStyle.SelectedItem = "Generic"; Preset(preset); }; presets.Children.Add(b); }
        labelStyle.SelectionChanged += (_, _) => Fill();
        void Fill()
        {
            list.Children.Clear();
            var labels = ButtonLabels[(string)labelStyle.SelectedItem];
            foreach (var input in inputs.ToList())
            {
                var card = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(69, 75, 86)), BorderThickness = new Thickness(1), Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 6) };
                var body = new StackPanel(); card.Child = body; list.Children.Add(card);
                var head = new DockPanel(); body.Children.Add(head);
                var remove = new Button { Content = "Remove", Margin = new Thickness(8, 0, 0, 0) }; DockPanel.SetDock(remove, Dock.Right); head.Children.Add(remove);
                remove.Click += (_, _) => { inputs.Remove(input); Fill(); };
                var name = new TextBox { Text = input.Name, FontWeight = FontWeights.SemiBold, ToolTip = "Name used in events and scripts" };
                name.LostFocus += (_, _) => input.Name = name.Text.Trim(); head.Children.Add(name);
                // Keys
                var keys = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) }; body.Children.Add(keys);
                keys.Children.Add(new TextBlock { Text = "Keys: ", VerticalAlignment = VerticalAlignment.Center, Width = 70 });
                foreach (var key in input.Keys.ToList()) { var chip = new Button { Content = key + "  ✕", Margin = new Thickness(0, 0, 4, 2), ToolTip = "Remove" }; chip.Click += (_, _) => { input.Keys.Remove(key); Fill(); }; keys.Children.Add(chip); }
                var capture = new Button { Content = "Press a key…", Margin = new Thickness(0, 0, 4, 2) };
                capture.Click += (_, _) => { capture.Content = "Press a key now (Esc cancels)"; capture.Focus(); };
                capture.PreviewKeyDown += (_, e) =>
                {
                    if (!Equals(capture.Content, "Press a key now (Esc cancels)")) return; e.Handled = true;
                    var keyName = e.Key == Key.Escape ? null : KeyNameFor(e.Key == Key.System ? e.SystemKey : e.Key);
                    if (keyName != null && !input.Keys.Contains(keyName)) input.Keys.Add(keyName);
                    Fill();
                };
                keys.Children.Add(capture);
                // Gamepad buttons
                var buttons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) }; body.Children.Add(buttons);
                buttons.Children.Add(new TextBlock { Text = "Buttons: ", VerticalAlignment = VerticalAlignment.Center, Width = 70 });
                foreach (var b in input.Buttons.ToList()) { var chip = new Button { Content = labels.GetValueOrDefault(b, b) + "  ✕", Margin = new Thickness(0, 0, 4, 2) }; chip.Click += (_, _) => { input.Buttons.Remove(b); Fill(); }; buttons.Children.Add(chip); }
                var add = new ComboBox { Width = 170, ItemsSource = new[] { "+ add button" }.Concat(Registry.GamepadButtons.Where(b => !input.Buttons.Contains(b)).Select(b => labels[b])).ToList(), SelectedIndex = 0 };
                add.SelectionChanged += (_, _) => { if (add.SelectedIndex <= 0) return; var chosen = Registry.GamepadButtons.First(b => labels[b] == (string)add.SelectedItem); input.Buttons.Add(chosen); Fill(); };
                buttons.Children.Add(add);
                // Stick
                var axis = new DockPanel { Margin = new Thickness(0, 4, 0, 0) }; body.Children.Add(axis);
                axis.Children.Add(new TextBlock { Text = "Stick: ", VerticalAlignment = VerticalAlignment.Center, Width = 70 });
                var axes = new (string, string)[] { ("", "(none)"), ("left_x-", "Left stick ←"), ("left_x+", "Left stick →"), ("left_y-", "Left stick ↑"), ("left_y+", "Left stick ↓"), ("right_x-", "Right stick ←"), ("right_x+", "Right stick →"), ("right_y-", "Right stick ↑"), ("right_y+", "Right stick ↓") };
                var pick = new ComboBox { ItemsSource = axes.Select(a => a.Item2).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(axes, a => a.Item1 == input.Axis)), Width = 200, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "ctx.input.axis(name) gives 0 to 1: how far the stick is pushed that way. Past halfway also presses the input." };
                pick.SelectionChanged += (_, _) => input.Axis = axes[pick.SelectedIndex].Item1; axis.Children.Add(pick);
                // Touch: a drag anywhere the controls are not, from wherever the finger lands.
                var touch = new DockPanel { Margin = new Thickness(0, 4, 0, 0) }; body.Children.Add(touch);
                touch.Children.Add(new TextBlock { Text = "Touch: ", VerticalAlignment = VerticalAlignment.Center, Width = 70 });
                var drags = new (string, string)[] { ("", "(none)"), ("drag_left", "Drag ←"), ("drag_right", "Drag →"), ("drag_up", "Drag ↑"), ("drag_down", "Drag ↓") };
                var touchPick = new ComboBox { ItemsSource = drags.Select(d => d.Item2).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(drags, d => d.Item1 == input.Touch)), Width = 200, HorizontalAlignment = HorizontalAlignment.Left,
                    ToolTip = "On a touch screen, dragging a finger this way presses the input, from wherever the finger first lands. ctx.input.axis(name) gives how far, 0 to 1. Tapping a control still works the control." };
                touchPick.SelectionChanged += (_, _) => input.Touch = drags[touchPick.SelectedIndex].Item1; touch.Children.Add(touchPick);
                // Which controller. Two players need one each, or both pads drive both characters.
                var padRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) }; body.Children.Add(padRow);
                padRow.Children.Add(new TextBlock { Text = "Pad: ", VerticalAlignment = VerticalAlignment.Center, Width = 70 });
                var pads = new (int, string)[] { (-1, "Any controller"), (0, "Controller 1"), (1, "Controller 2"), (2, "Controller 3"), (3, "Controller 4") };
                var padPick = new ComboBox { ItemsSource = pads.Select(x => x.Item2).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(pads, x => x.Item1 == input.Pad)), Width = 200, HorizontalAlignment = HorizontalAlignment.Left,
                    ToolTip = "Which gamepad presses this input. Any controller is the default; pick one per player for local two-player, so the pads stop driving each other." };
                padPick.SelectionChanged += (_, _) => input.Pad = pads[padPick.SelectedIndex].Item1; padRow.Children.Add(padPick);
            }
            var addInput = new Button { Content = "+ New input", HorizontalAlignment = HorizontalAlignment.Left };
            addInput.Click += (_, _) => { int n = 1; while (inputs.Any(i => i.Name == "input" + n)) n++; inputs.Add(new GameInput { Name = "input" + n }); Fill(); };
            list.Children.Add(addInput);
        }
        var ok = new Button { Content = "Apply", IsDefault = true, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 3, 12, 3) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(12, 3, 12, 3) };
        ok.Click += (_, _) => Guard(() =>
        {
            Keyboard.ClearFocus();
            var names = new HashSet<string>();
            foreach (var i in inputs) if (!Validation.Variable(i.Name) || !names.Add(i.Name)) throw new InvalidOperationException($"\"{i.Name}\": input names must be unique and use letters, digits and _.");
            Change(); project.Manifest.Inputs = inputs;
            foreach (var e in project.Screens.SelectMany(s => s.Elements).Where(e => e.Input.Length > 0 && !names.Contains(e.Input))) e.Input = ""; // removed inputs
            window.Close(); RefreshAll(); RefreshInspector();
        });
        cancel.Click += (_, _) => window.Close();
        bottom.Children.Add(ok); bottom.Children.Add(cancel);
        Fill(); window.ShowDialog();
    }
    // Same names as the Key event (and the runtimes' KeyNames).
    static string? KeyNameFor(Key key)
    {
        if (key >= Key.A && key <= Key.Z) return ((char)('a' + (key - Key.A))).ToString();
        if (key >= Key.D0 && key <= Key.D9) return ((char)('0' + (key - Key.D0))).ToString();
        if (key >= Key.NumPad0 && key <= Key.NumPad9) return ((char)('0' + (key - Key.NumPad0))).ToString();
        if (key >= Key.F1 && key <= Key.F12) return "f" + (key - Key.F1 + 1);
        return key switch { Key.Space => "space", Key.Enter => "enter", Key.Tab => "tab", Key.Back => "backspace", Key.Left => "left", Key.Right => "right", Key.Up => "up", Key.Down => "down", _ => null };
    }
}
