using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Wysicraft.Core.Audio;
using Wysicraft.Models;
namespace Wysicraft.Designer;

/// <summary>Sound effect maker: retro game sounds from presets (coin, jump, laser, explosion, power-up, hurt, blip or
/// random), then tweaked with sliders and mutated. Saved into the project as a sound, with its settings kept beside it
/// (name.ogg.sfx) so it can be opened and changed again.</summary>
sealed class SoundEffectMaker : Window
{
    public sealed record SaveRequest(SoundEffect Effect, string Format, bool AsNew, string? Path, float[] Samples);
    readonly Func<SaveRequest, string> save;
    readonly AudioOut audio = new();
    SoundEffect fx; string? path; bool dirty, loading;
    readonly List<string> undo = [];
    readonly StackPanel knobs = new();
    readonly Canvas wave = new() { Height = 110, Background = new SolidColorBrush(Color.FromRgb(28, 31, 38)), ClipToBounds = true };
    readonly TextBox nameBox = new() { Width = 140 };
    readonly ComboBox formatBox = new() { ItemsSource = AudioFiles.Formats, Width = 60 };
    readonly CheckBox autoPlay = new() { Content = "Play on every change", IsChecked = true, Margin = new Thickness(0, 8, 0, 0) };
    readonly TextBlock status = new() { Margin = new Thickness(8, 4, 8, 4) };
    readonly DispatcherTimer settle = new() { Interval = TimeSpan.FromMilliseconds(180) };

    public SoundEffectMaker(Window owner, SoundEffect effect, string? assetPath, Func<SaveRequest, string> save)
    {
        Owner = owner; fx = effect; path = assetPath; this.save = save;
        SetResourceReference(StyleProperty, typeof(Window));
        Width = 980; Height = 760; MinWidth = 820; MinHeight = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        formatBox.SelectedItem = assetPath != null && assetPath.EndsWith(".wav") ? "wav" : "ogg";
        var root = new DockPanel { Margin = new Thickness(8) }; Content = root;
        var left = new StackPanel { Width = 170, Margin = new Thickness(0, 0, 10, 0) }; DockPanel.SetDock(left, Dock.Left); root.Children.Add(left);
        left.Children.Add(new TextBlock { Text = "Start from", Foreground = Brushes.LightSkyBlue, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        foreach (var preset in SoundEffect.Presets)
        {
            var b = new Button { Content = preset switch { "powerup" => "Power-up", _ => char.ToUpper(preset[0]) + preset[1..] }, Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(6, 3, 6, 3), ToolTip = "A new random " + preset + " sound (click again for another)" };
            b.Click += (_, _) => Guard(() => { Push(); var name = fx.Name; fx = SoundEffect.Preset(preset); if (path != null) fx.Name = name; Changed(true); });
            left.Children.Add(b);
        }
        var mutate = new Button { Content = "Mutate", Margin = new Thickness(0, 10, 0, 4), Padding = new Thickness(6, 3, 6, 3), ToolTip = "A close variation of this sound" };
        mutate.Click += (_, _) => Guard(() => { Push(); fx = fx.Mutate(); Changed(true); }); left.Children.Add(mutate);
        var play = new Button { Content = "▶ Play (Space)", Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(6, 3, 6, 3) }; play.Click += (_, _) => Guard(Play); left.Children.Add(play);
        var undoButton = new Button { Content = "Undo (Ctrl+Z)", Padding = new Thickness(6, 3, 6, 3) }; undoButton.Click += (_, _) => Undo(); left.Children.Add(undoButton);
        left.Children.Add(autoPlay);

        var bottom = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom);
        bottom.Children.Add(new TextBlock { Text = "Name", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) }); bottom.Children.Add(nameBox);
        bottom.Children.Add(new TextBlock { Text = "Format", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0) }); bottom.Children.Add(formatBox);
        var saveButton = new Button { Content = "Save", Margin = new Thickness(10, 0, 4, 0), Padding = new Thickness(10, 2, 10, 2) }; saveButton.Click += (_, _) => Guard(() => Save(false));
        var saveNew = new Button { Content = "Save as new", Padding = new Thickness(10, 2, 10, 2) }; saveNew.Click += (_, _) => Guard(() => Save(true));
        var export = new Button { Content = "Export audio…", Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2), ToolTip = "Save it as an MP3, WAV or Ogg file anywhere" };
        export.Click += (_, _) => Guard(() =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "MP3|*.mp3|WAV|*.wav|Ogg Vorbis|*.ogg", FileName = PixelEditor.SafeName(nameBox.Text) + ".wav" }; if (dialog.ShowDialog(this) != true) return;
            AudioExport.Save(dialog.FileName, [fx.Render(SongRenderer.SampleRate)], SongRenderer.SampleRate); status.Text = "Exported " + System.IO.Path.GetFileName(dialog.FileName) + ".";
        });
        bottom.Children.Add(saveButton); bottom.Children.Add(saveNew); bottom.Children.Add(export); bottom.Children.Add(status);
        root.Children.Add(bottom);
        var centre = new DockPanel(); root.Children.Add(centre);
        DockPanel.SetDock(wave, Dock.Top); centre.Children.Add(wave);
        centre.Children.Add(new ScrollViewer { Content = knobs, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 8, 0, 0) });
        nameBox.LostFocus += (_, _) => { var n = PixelEditor.SafeName(nameBox.Text); if (n != fx.Name) { fx.Name = n; dirty = true; UpdateTitle(); } };
        settle.Tick += (_, _) => { settle.Stop(); if (autoPlay.IsChecked == true) Guard(Play); };
        wave.SizeChanged += (_, _) => DrawWave();
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.FocusedElement is TextBox) return;
            if (e.Key == Key.Space) { Guard(Play); e.Handled = true; }
            else if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { Undo(); e.Handled = true; }
        };
        Closing += (_, e) =>
        {
            if (!dirty) return;
            var answer = MessageBox.Show(this, "Save your sound to the project before closing?", "Sound effect maker", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) e.Cancel = true;
            else if (answer == MessageBoxResult.Yes) { try { Save(false); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Sound effect maker"); e.Cancel = true; } }
        };
        Closed += (_, _) => audio.Dispose();
        Build(); UpdateTitle();
    }
    void Push() { undo.Add(Json.Write(fx)); if (undo.Count > 100) undo.RemoveAt(0); }
    void Undo() { if (undo.Count == 0) return; fx = Json.Read<SoundEffect>(undo[^1]); undo.RemoveAt(undo.Count - 1); Changed(true); }
    void Changed(bool rebuild)
    {
        dirty = true;
        if (rebuild) Build(); else DrawWave();
        UpdateTitle(); settle.Stop(); settle.Start();
    }
    void Play() { fx.Check(); audio.StopAll(); audio.Play(fx.Render(AudioOut.Rate)); }
    /// <summary>For an assistant showing the person a sound it made: play it, and whether they've changed anything since.</summary>
    internal void PlayNow() => Play();
    internal bool Untouched => !dirty;
    void Build()
    {
        loading = true; knobs.Children.Clear(); nameBox.Text = fx.Name;
        var waveBox = new ComboBox { ItemsSource = new[] { "square", "triangle", "saw", "sine", "noise" }, SelectedItem = fx.Wave, Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
        waveBox.SelectionChanged += (_, _) => { if (loading) return; Push(); fx.Wave = (string)waveBox.SelectedItem; Changed(true); };
        knobs.Children.Add(Row("Wave", waveBox));
        Section("Volume shape");
        Knob("Attack", 0, 1, fx.Attack, v => fx.Attack = v, Seconds);
        Knob("Sustain", 0, 1.5, fx.Sustain, v => fx.Sustain = v, Seconds);
        Knob("Punch", 0, 1, fx.Punch, v => fx.Punch = v, Percent);
        Knob("Decay", 0, 2, fx.Decay, v => fx.Decay = v, Seconds);
        Section("Pitch");
        Knob("Start pitch", 40, 3000, fx.Frequency, v => fx.Frequency = v, v => $"{v:0} Hz", log: true);
        Knob("Lowest pitch", 0, 2000, fx.MinFrequency, v => fx.MinFrequency = v, v => v < 1 ? "none" : $"{v:0} Hz");
        Knob("Slide", -20, 20, fx.Slide, v => fx.Slide = v, v => $"{v:+0.0;-0.0;0} oct/s");
        Knob("Slide change", -40, 40, fx.DeltaSlide, v => fx.DeltaSlide = v, v => $"{v:+0.0;-0.0;0}");
        Knob("Vibrato depth", 0, 4, fx.VibratoDepth, v => fx.VibratoDepth = v, v => $"{v:0.00} st");
        Knob("Vibrato speed", 0, 40, fx.VibratoSpeed, v => fx.VibratoSpeed = v, v => $"{v:0.0} Hz");
        Knob("Jump by", -24, 24, fx.ArpeggioSemitones, v => fx.ArpeggioSemitones = Math.Round(v), v => $"{Math.Round(v):+0;-0;0} st", 1);
        Knob("Jump after", 0, 1, fx.ArpeggioTime, v => fx.ArpeggioTime = v, v => v < 0.001 ? "off" : Seconds(v));
        Knob("Repeat every", 0, 1, fx.RepeatTime, v => fx.RepeatTime = v, v => v < 0.001 ? "off" : Seconds(v));
        if (fx.Wave == "square") { Section("Pulse"); Knob("Pulse width", 0.05, 0.95, fx.Duty, v => fx.Duty = v, Percent); Knob("Width sweep", -2, 2, fx.DutySweep, v => fx.DutySweep = v, v => $"{v:+0.00;-0.00;0}/s"); }
        Section("Tone");
        Knob("Low-pass", 0, 1, fx.LowPass, v => fx.LowPass = v, v => v >= 0.999 ? "open" : Percent(v));
        Knob("Low-pass sweep", -2, 2, fx.LowPassSweep, v => fx.LowPassSweep = v, v => $"{v:+0.00;-0.00;0}/s");
        Knob("High-pass", 0, 1, fx.HighPass, v => fx.HighPass = v, v => v < 0.001 ? "off" : Percent(v));
        Knob("Bit crush", 0, 8, fx.Crush, v => fx.Crush = (int)Math.Round(v) == 1 ? 0 : (int)Math.Round(v), v => Math.Round(v) <= 1 ? "off" : $"{Math.Round(v)} bits", 1);
        Knob("Volume", 0, 1, fx.Volume, v => fx.Volume = v, Percent);
        loading = false; DrawWave();
    }
    static string Seconds(double v) => v < 1 ? $"{v * 1000:0} ms" : $"{v:0.00} s";
    static string Percent(double v) => $"{v * 100:0}%";
    void Section(string text) => knobs.Children.Add(new TextBlock { Text = text, Foreground = Brushes.LightSkyBlue, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
    static UIElement Row(string label, UIElement control)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        var l = new TextBlock { Text = label, Width = 120, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(l, Dock.Left); row.Children.Add(l); row.Children.Add(control); return row;
    }
    void Knob(string label, double min, double max, double value, Action<double> set, Func<double, string> show, double tick = 0, bool log = false)
    {
        // Log sliders (for pitch) move evenly through octaves.
        double To(double v) => log ? Math.Log(Math.Max(v, min)) : v; double From(double s) => log ? Math.Exp(s) : s;
        var text = new TextBlock { Text = show(value), Width = 90, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider { Minimum = To(min), Maximum = To(max), Value = Math.Clamp(To(value), To(min), To(max)), IsSnapToTickEnabled = tick > 0, TickFrequency = tick > 0 ? tick : (To(max) - To(min)) / 300, VerticalAlignment = VerticalAlignment.Center };
        slider.PreviewMouseLeftButtonDown += (_, _) => Push();
        slider.ValueChanged += (_, _) => { if (loading) return; set(From(slider.Value)); text.Text = show(From(slider.Value)); Changed(false); };
        var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
        var l = new TextBlock { Text = label, Width = 120, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(l, Dock.Left); row.Children.Add(l);
        DockPanel.SetDock(text, Dock.Right); row.Children.Add(text); row.Children.Add(slider);
        knobs.Children.Add(row);
    }
    void DrawWave()
    {
        wave.Children.Clear();
        float[] samples; try { samples = fx.Render(AudioOut.Rate); } catch (InvalidDataException ex) { wave.Children.Add(new TextBlock { Text = ex.Message, Foreground = Brushes.OrangeRed, Margin = new Thickness(6) }); return; }
        double w = wave.ActualWidth > 0 ? wave.ActualWidth : 700, h = wave.Height; int per = Math.Max(1, samples.Length / (int)Math.Max(1, w));
        var line = new Polyline { Stroke = Brushes.LightSkyBlue, StrokeThickness = 1 };
        for (int x = 0; x < w && x * per < samples.Length; x++)
        {
            float lo = 0, hi = 0; for (int i = x * per; i < Math.Min(samples.Length, (x + 1) * per); i++) { lo = Math.Min(lo, samples[i]); hi = Math.Max(hi, samples[i]); }
            line.Points.Add(new Point(x, h / 2 - hi * h / 2)); line.Points.Add(new Point(x, h / 2 - lo * h / 2));
        }
        wave.Children.Add(line);
        wave.Children.Add(new TextBlock { Text = $"{fx.Seconds:0.00} s", Foreground = Brushes.Gray, FontSize = 10, Margin = new Thickness(4, 2, 0, 0) });
    }
    void Save(bool asNew)
    {
        fx.Name = PixelEditor.SafeName(nameBox.Text); fx.Check();
        path = save(new SaveRequest(fx.Copy(), (string)formatBox.SelectedItem, asNew, path, fx.Render(SongRenderer.SampleRate)));
        dirty = false; UpdateTitle(); status.Text = "Saved to Assets as " + System.IO.Path.GetFileName(path) + ".";
    }
    void UpdateTitle() => Title = "Sound effect maker — " + (path != null ? System.IO.Path.GetFileName(path) : fx.Name + " (not saved yet)") + (dirty ? " •" : "");
    void Guard(Action action) { try { action(); } catch (OperationCanceledException) { } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Sound effect maker", MessageBoxButton.OK, MessageBoxImage.Warning); } }
}
