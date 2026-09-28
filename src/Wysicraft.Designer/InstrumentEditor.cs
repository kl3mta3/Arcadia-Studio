using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Wysicraft.Core.Audio;
using Wysicraft.Models;
namespace Wysicraft.Designer;

/// <summary>Knobs for one layer's instrument: wave, envelope, vibrato, slide, arpeggio, bit crush and echo, with a
/// picture of the sound and buttons to hear it.</summary>
static class InstrumentEditor
{
    public static Instrument? Edit(Window owner, Instrument original, AudioOut audio)
    {
        var ins = original.Copy(); Instrument? result = null;
        var window = new Window { Owner = owner, Title = "Instrument — " + original.Name, Width = 560, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SetResourceReference(FrameworkElement.StyleProperty, typeof(Window));
        var root = new StackPanel { Margin = new Thickness(12) }; window.Content = root;
        var wavePicture = new Canvas { Height = 70, Background = new SolidColorBrush(Color.FromRgb(28, 31, 38)), ClipToBounds = true, Margin = new Thickness(0, 0, 0, 8) };
        var body = new StackPanel(); bool loading = false;
        void Redraw()
        {
            wavePicture.Children.Clear();
            double w = wavePicture.ActualWidth > 0 ? wavePicture.ActualWidth : 530, h = wavePicture.Height;
            var voice = new Voice(ins, 60, 1, AudioOut.Rate, 0.45); int total = (int)(AudioOut.Rate * (0.45 + Math.Min(ins.Release, 1) + 0.05)); var samples = new float[total];
            for (int i = 0; i < total && !voice.Done; i++) samples[i] = voice.Next();
            var line = new Polyline { Stroke = Brushes.LightSkyBlue, StrokeThickness = 1 };
            int perPixel = Math.Max(1, total / (int)w);
            for (int x = 0; x < w; x++)
            {
                float lo = 0, hi = 0; for (int i = x * perPixel; i < Math.Min(total, (x + 1) * perPixel); i++) { lo = Math.Min(lo, samples[i]); hi = Math.Max(hi, samples[i]); }
                line.Points.Add(new Point(x, h / 2 - hi * h / 2)); line.Points.Add(new Point(x, h / 2 - lo * h / 2));
            }
            wavePicture.Children.Add(line);
            wavePicture.Children.Add(new TextBlock { Text = "a held note, then its release", Foreground = Brushes.Gray, FontSize = 10, Margin = new Thickness(4, 2, 0, 0) });
        }
        void Build()
        {
            loading = true; body.Children.Clear();
            var nameBox = new TextBox { Text = ins.Name }; nameBox.TextChanged += (_, _) => ins.Name = nameBox.Text.Trim().Length > 0 ? nameBox.Text.Trim() : ins.Name;
            body.Children.Add(Row("Name", nameBox));
            var wave = new ComboBox { ItemsSource = Instrument.Waves, SelectedItem = ins.Wave };
            wave.SelectionChanged += (_, _) => { if (!loading) { ins.Wave = (string)wave.SelectedItem; Build(); } };
            body.Children.Add(Row("Wave", wave));
            if (ins.Wave == "drums")
                body.Children.Add(new TextBlock { Text = "The drum kit plays a drum for each note: " + string.Join(", ", Instruments.Drums.Select(d => d.Name + " " + MusicTheory.Name(d.Pitch))) + ". Other notes play a noise hit.", TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 4, 0, 4) });
            if (ins.Wave == "square") body.Children.Add(Slider("Pulse width", 0.05, 0.95, ins.Duty, v => ins.Duty = v, v => $"{v * 100:0}%"));
            if (ins.Wave != "drums")
            {
                body.Children.Add(Section("Envelope"));
                body.Children.Add(Slider("Attack", 0, 1, ins.Attack, v => ins.Attack = v, S));
                body.Children.Add(Slider("Decay", 0, 2, ins.Decay, v => ins.Decay = v, S));
                body.Children.Add(Slider("Sustain", 0, 1, ins.Sustain, v => ins.Sustain = v, v => $"{v * 100:0}%"));
                body.Children.Add(Slider("Release", 0, 2, ins.Release, v => ins.Release = v, S));
                body.Children.Add(Section("Pitch"));
                body.Children.Add(Slider("Vibrato", 0, 2, ins.Vibrato, v => ins.Vibrato = v, v => $"{v:0.00} st"));
                body.Children.Add(Slider("Vibrato speed", 0, 20, ins.VibratoRate, v => ins.VibratoRate = v, v => $"{v:0.0} Hz"));
                body.Children.Add(Slider("Vibrato delay", 0, 1, ins.VibratoDelay, v => ins.VibratoDelay = v, S));
                body.Children.Add(Slider("Slide", -24, 24, ins.Slide, v => ins.Slide = Math.Round(v, 1), v => $"{v:+0.0;-0.0;0} st"));
                body.Children.Add(Slider("Slide time", 0.005, 1, ins.SlideTime, v => ins.SlideTime = v, S));
                var arp = new TextBox { Text = ins.Arpeggio, ToolTip = "Semitone offsets played in turn very fast: 0,4,7 major chord, 0,3,7 minor, 0,12 octave. Empty = off." };
                arp.TextChanged += (_, _) => { var old = ins.Arpeggio; ins.Arpeggio = arp.Text; try { ins.Check(); arp.ClearValue(Control.BorderBrushProperty); Redraw(); } catch (InvalidDataException) { ins.Arpeggio = old; arp.BorderBrush = Brushes.OrangeRed; } };
                body.Children.Add(Row("Arpeggio", arp));
                body.Children.Add(Slider("Arpeggio speed", 1, 60, ins.ArpeggioRate, v => ins.ArpeggioRate = v, v => $"{v:0} Hz"));
                body.Children.Add(Slider("Octave", -3, 3, ins.Octave, v => ins.Octave = (int)Math.Round(v), v => $"{Math.Round(v):+0;-0;0}", 1));
            }
            body.Children.Add(Section("Colour"));
            body.Children.Add(Slider("Bit crush", 0, 8, ins.Crush, v => ins.Crush = (int)Math.Round(v) == 1 ? 0 : (int)Math.Round(v), v => (int)Math.Round(v) <= 1 ? "off" : $"{Math.Round(v)} bits", 1));
            body.Children.Add(Slider("Echo", 0, 0.6, ins.Echo, v => ins.Echo = v, v => v < 0.005 ? "off" : S(v)));
            body.Children.Add(Slider("Echo feedback", 0, 0.9, ins.EchoFeedback, v => ins.EchoFeedback = v, v => $"{v * 100:0}%"));
            body.Children.Add(Slider("Volume", 0, 1, ins.Volume, v => ins.Volume = v, v => $"{v * 100:0}%"));
            loading = false; Redraw();
        }
        static string S(double v) => v < 1 ? $"{v * 1000:0} ms" : $"{v:0.00} s";
        UIElement Section(string text) => new TextBlock { Text = text, Foreground = Brushes.LightSkyBlue, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) };
        UIElement Row(string label, UIElement control)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var l = new TextBlock { Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(l, Dock.Left); row.Children.Add(l); row.Children.Add(control); return row;
        }
        UIElement Slider(string label, double min, double max, double value, Action<double> set, Func<double, string> show, double tick = 0)
        {
            var text = new TextBlock { Text = show(value), Width = 70, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), IsSnapToTickEnabled = tick > 0, TickFrequency = tick > 0 ? tick : (max - min) / 200, VerticalAlignment = VerticalAlignment.Center };
            slider.ValueChanged += (_, _) => { if (loading) return; set(slider.Value); text.Text = show(slider.Value); Redraw(); };
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
            var l = new TextBlock { Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(l, Dock.Left); row.Children.Add(l);
            DockPanel.SetDock(text, Dock.Right); row.Children.Add(text); row.Children.Add(slider); return row;
        }
        var start = new ComboBox { ItemsSource = Instruments.Presets.Select(p => p.Name).ToArray(), ToolTip = "Replace every setting with a ready-made instrument" };
        start.SelectionChanged += (_, _) => { if (start.SelectedItem is string n) { ins = Instruments.Get(n); Build(); } };
        root.Children.Add(Row("Start from", start));
        root.Children.Add(wavePicture); root.Children.Add(body);
        var tests = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        void Test(string label, int[] pitches, double gap, double length)
        {
            var b = new Button { Content = label, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(8, 2, 8, 2) };
            b.Click += (_, _) =>
            {
                try { ins.Check(); } catch (InvalidDataException ex) { MessageBox.Show(window, ex.Message, "Instrument"); return; }
                var song = new Song { Bpm = 120, StepsPerBeat = 4, Bars = 2, Tracks = [new Track { Instrument = ins.Copy(), Volume = 0.9, Notes = pitches.Select((p, i) => new Note { Step = (int)(i * gap * 8), Length = (int)Math.Max(1, length * 8), Pitch = p }).ToList() }] };
                song.Bars = song.BarsNeeded(); var (l, r) = SongRenderer.Render(song); audio.Play(l, r);
            };
            tests.Children.Add(b);
        }
        bool drums = ins.Wave == "drums";
        Test("▶ Note", [60], 0, 0.5); Test("▶ Chord", [60, 64, 67], 0, 1); Test("▶ Scale", [60, 62, 64, 65, 67, 69, 71, 72], 0.25, 0.25); Test("▶ Drums", [36, 42, 38, 42, 36, 36, 38, 46], 0.25, 0.25);
        root.Children.Add(tests);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "Use this instrument", IsDefault = true, Padding = new Thickness(10, 2, 10, 2) };
        ok.Click += (_, _) => { try { ins.Check(); result = ins; window.Close(); } catch (InvalidDataException ex) { MessageBox.Show(window, ex.Message, "Instrument"); } };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2) };
        buttons.Children.Add(ok); buttons.Children.Add(cancel); root.Children.Add(buttons);
        wavePicture.SizeChanged += (_, _) => Redraw();
        Build();
        window.ShowDialog();
        if (result != null && Json.Write(result) != Json.Write(original) && Instruments.Presets.Any(p => p.Name == result.Name) && Json.Write(Instruments.Get(result.Name)) != Json.Write(result)) result.Name += " (edited)";
        return result;
    }
}
