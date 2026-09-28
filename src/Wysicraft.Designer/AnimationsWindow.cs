using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wysicraft.Core;
using Wysicraft.Models;
using Validation = Wysicraft.Core.Validation;
namespace Wysicraft.Designer;

// Advanced → Animations (web and desktop): keyframe animations for the current screen. Each track moves one
// property (x, y, width, height or opacity) of one control through keyframes. The timeline slider previews the
// animation on the canvas; everything snaps back when the window closes. Scripts play them with ui.animate(id).
public partial class MainWindow
{
    static double Ease(string ease, double t) => ease switch
    {
        "ease_in" => t * t, "ease_out" => 1 - (1 - t) * (1 - t),
        "ease_in_out" => t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2, "step" => t < 1 ? 0 : 1, _ => t
    };
    /// <summary>A track's value at a time; the same rule as the web runtime (each key's easing shapes the way into it).</summary>
    internal static double? TrackValue(AnimationTrack track, double time)
    {
        var keys = track.Keys.OrderBy(k => k.Time).ToList(); if (keys.Count == 0) return null;
        if (time <= keys[0].Time) return keys[0].Value;
        for (int i = 1; i < keys.Count; i++)
            if (time <= keys[i].Time) { var a = keys[i - 1]; var b = keys[i]; double t = b.Time == a.Time ? 1 : (time - a.Time) / (b.Time - a.Time); return a.Value + (b.Value - a.Value) * Ease(b.Ease, t); }
        return keys[^1].Value;
    }
    static double PropertyOf(Element e, string property) => property switch { "x" => e.Bounds.X, "y" => e.Bounds.Y, "width" => e.Bounds.Width, "height" => e.Bounds.Height, _ => e.Opacity };
    static void SetProperty(Element e, string property, double v) { switch (property) { case "x": e.Bounds.X = v; break; case "y": e.Bounds.Y = v; break; case "width": e.Bounds.Width = Math.Max(1, v); break; case "height": e.Bounds.Height = Math.Max(1, v); break; default: e.Opacity = Math.Clamp(v, 0, 1); break; } }

    void ShowAnimationsWindow()
    {
        var screen = ui; var animations = Json.Clone(screen.Animations);
        var saved = screen.Elements.ToDictionary(e => e.Id, e => (e.Bounds.X, e.Bounds.Y, e.Bounds.Width, e.Bounds.Height, e.Opacity));
        void Restore() { foreach (var e in screen.Elements) if (saved.TryGetValue(e.Id, out var s)) { e.Bounds.X = s.X; e.Bounds.Y = s.Y; e.Bounds.Width = s.Width; e.Bounds.Height = s.Height; e.Opacity = s.Opacity; } Draw(); }
        var window = new Window { Owner = this, Title = "Animations · " + screen.Id + " · web & desktop", Width = 900, Height = 600, WindowStartupLocation = WindowStartupLocation.Manual, Left = Left + Math.Max(0, ActualWidth - 920), Top = Top + 80 };
        var root = new DockPanel { Margin = new Thickness(10) }; window.Content = root;
        var left = new DockPanel { Width = 200, Margin = new Thickness(0, 0, 10, 0) }; DockPanel.SetDock(left, Dock.Left); root.Children.Add(left);
        var addAnimation = new Button { Content = "+ New animation", Margin = new Thickness(0, 0, 0, 4) }; DockPanel.SetDock(addAnimation, Dock.Top); left.Children.Add(addAnimation);
        var removeAnimation = new Button { Content = "Delete animation", Margin = new Thickness(0, 4, 0, 0) }; DockPanel.SetDock(removeAnimation, Dock.Bottom); left.Children.Add(removeAnimation);
        var picker = new ListBox(); left.Children.Add(picker);
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var timeline = new DockPanel { Margin = new Thickness(0, 6, 0, 0) }; DockPanel.SetDock(timeline, Dock.Bottom); root.Children.Add(timeline);
        var timeLabel = new TextBlock { Width = 90, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(timeLabel, Dock.Left); timeline.Children.Add(timeLabel);
        var scrub = new Slider { Minimum = 0, Maximum = 1000, ToolTip = "Drag to preview on the canvas" }; timeline.Children.Add(scrub);
        var editor = new StackPanel(); root.Children.Add(new ScrollViewer { Content = editor, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        ScreenAnimation? current = null;
        string[] properties = ["x", "y", "width", "height", "opacity"], eases = ["linear", "ease_in", "ease_out", "ease_in_out", "step"];
        void Preview(double time)
        {
            if (current == null) return; Restore();
            foreach (var track in current.Tracks) if (screen.Elements.FirstOrDefault(e => e.Id == track.Target) is { } e && TrackValue(track, time) is double v) SetProperty(e, track.Property, v);
            Draw(); timeLabel.Text = $"{time:0} / {current.Duration} ms";
        }
        scrub.ValueChanged += (_, _) => Preview(scrub.Value);
        void Pick() { picker.ItemsSource = animations.Select(a => a.Id).ToList(); if (current != null) picker.SelectedItem = current.Id; }
        picker.SelectionChanged += (_, _) => { current = animations.FirstOrDefault(a => a.Id == picker.SelectedItem as string); Edit(); };
        addAnimation.Click += (_, _) => { int n = 1; while (animations.Any(a => a.Id == "animation" + n)) n++; current = new ScreenAnimation { Id = "animation" + n }; animations.Add(current); Pick(); Edit(); };
        removeAnimation.Click += (_, _) => { if (current == null) return; animations.Remove(current); current = animations.FirstOrDefault(); Restore(); Pick(); Edit(); };
        TextBox Number(double value, Action<double> set, double width = 60)
        {
            var box = new TextBox { Text = value.ToString(CultureInfo.InvariantCulture), Width = width, Margin = new Thickness(0, 0, 6, 0) };
            box.LostFocus += (_, _) => { if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v)) { set(v); Preview(scrub.Value); } else box.BorderBrush = Brushes.IndianRed; };
            return box;
        }
        void Edit()
        {
            editor.Children.Clear();
            if (current == null) { editor.Children.Add(new TextBlock { Text = "Make an animation with + New animation. Scripts start it with ui.animate('id'); set Autoplay to start it when the screen opens. It fires the screen's animation_end event when a non-looping animation finishes.", TextWrapping = TextWrapping.Wrap, Opacity = 0.8 }); return; }
            var a = current; scrub.Maximum = a.Duration;
            var head = new WrapPanel(); editor.Children.Add(head);
            head.Children.Add(new TextBlock { Text = "ID ", VerticalAlignment = VerticalAlignment.Center });
            var id = new TextBox { Text = a.Id, Width = 140, Margin = new Thickness(0, 0, 12, 0) }; id.LostFocus += (_, _) => { a.Id = id.Text.Trim(); Pick(); }; head.Children.Add(id);
            head.Children.Add(new TextBlock { Text = "Length (ms) ", VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(Number(a.Duration, v => { a.Duration = (int)Math.Clamp(v, 1, 600000); scrub.Maximum = a.Duration; }, 70));
            var loop = new CheckBox { Content = "Loop", IsChecked = a.Loop, Margin = new Thickness(6, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center }; loop.Click += (_, _) => a.Loop = loop.IsChecked == true; head.Children.Add(loop);
            var auto = new CheckBox { Content = "Autoplay when the screen opens", IsChecked = a.Autoplay, VerticalAlignment = VerticalAlignment.Center }; auto.Click += (_, _) => a.Autoplay = auto.IsChecked == true; head.Children.Add(auto);
            foreach (var track in a.Tracks.ToList())
            {
                var card = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(69, 75, 86)), BorderThickness = new Thickness(1), Padding = new Thickness(8), Margin = new Thickness(0, 8, 0, 0) };
                var body = new StackPanel(); card.Child = body; editor.Children.Add(card);
                var row = new WrapPanel(); body.Children.Add(row);
                var target = new ComboBox { ItemsSource = screen.Elements.Select(e => e.Id).ToList(), SelectedItem = track.Target, Width = 140, Margin = new Thickness(0, 0, 6, 0) }; target.SelectionChanged += (_, _) => { track.Target = target.SelectedItem as string ?? ""; Preview(scrub.Value); };
                var property = new ComboBox { ItemsSource = properties, SelectedItem = track.Property, Width = 90, Margin = new Thickness(0, 0, 6, 0) }; property.SelectionChanged += (_, _) => { track.Property = property.SelectedItem as string ?? "x"; Preview(scrub.Value); };
                var capture = new Button { Content = "Add keyframe from canvas", Margin = new Thickness(0, 0, 6, 0), ToolTip = "Adds a keyframe at the timeline position with the control's value on the canvas right now" };
                capture.Click += (_, _) => { if (!saved.ContainsKey(track.Target)) return; var s = saved[track.Target]; double v = track.Property switch { "x" => s.X, "y" => s.Y, "width" => s.Width, "height" => s.Height, _ => s.Opacity }; track.Keys.RemoveAll(k => k.Time == (int)scrub.Value); track.Keys.Add(new Keyframe { Time = (int)scrub.Value, Value = v }); track.Keys.Sort((p, q) => p.Time.CompareTo(q.Time)); Edit(); };
                var removeTrack = new Button { Content = "Remove track" }; removeTrack.Click += (_, _) => { a.Tracks.Remove(track); Restore(); Edit(); };
                row.Children.Add(target); row.Children.Add(property); row.Children.Add(capture); row.Children.Add(removeTrack);
                foreach (var key in track.Keys.ToList())
                {
                    var k = new WrapPanel { Margin = new Thickness(12, 4, 0, 0) }; body.Children.Add(k);
                    k.Children.Add(new TextBlock { Text = "at ", VerticalAlignment = VerticalAlignment.Center });
                    k.Children.Add(Number(key.Time, v => key.Time = (int)Math.Clamp(v, 0, a.Duration)));
                    k.Children.Add(new TextBlock { Text = "ms → ", VerticalAlignment = VerticalAlignment.Center });
                    k.Children.Add(Number(key.Value, v => key.Value = v));
                    var ease = new ComboBox { ItemsSource = eases, SelectedItem = key.Ease, Width = 110, Margin = new Thickness(0, 0, 6, 0), ToolTip = "How the value moves into this keyframe" }; ease.SelectionChanged += (_, _) => { key.Ease = ease.SelectedItem as string ?? "linear"; Preview(scrub.Value); }; k.Children.Add(ease);
                    var remove = new Button { Content = "✕" }; remove.Click += (_, _) => { track.Keys.Remove(key); Edit(); }; k.Children.Add(remove);
                }
            }
            var addTrack = new Button { Content = "+ Track (animate a control)", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            addTrack.Click += (_, _) => { var target = selected.FirstOrDefault(s => screen.Elements.Any(e => e.Id == s)) ?? screen.Elements.FirstOrDefault()?.Id ?? ""; a.Tracks.Add(new AnimationTrack { Target = target, Property = "x" }); Edit(); };
            editor.Children.Add(addTrack);
            Preview(scrub.Value);
        }
        var apply = new Button { Content = "Apply", IsDefault = true, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 3, 12, 3) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(12, 3, 12, 3) };
        apply.Click += (_, _) => Guard(() =>
        {
            System.Windows.Input.Keyboard.ClearFocus(); Restore();
            var ids = new HashSet<string>();
            foreach (var a in animations) if (!Validation.Id(a.Id) || !ids.Add(a.Id)) throw new InvalidOperationException($"\"{a.Id}\": animation IDs must be unique lowercase IDs.");
            Change(); screen.Animations = animations; window.Close(); RefreshInspector();
        });
        cancel.Click += (_, _) => window.Close();
        window.Closed += (_, _) => Restore();
        bottom.Children.Add(new TextBlock { Text = "Preview in Preview (F5) to see it play. ", Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        bottom.Children.Add(apply); bottom.Children.Add(cancel);
        current = animations.FirstOrDefault(); Pick(); Edit(); window.Show();
    }
}
