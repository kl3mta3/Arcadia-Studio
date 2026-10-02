using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Wysicraft.Core.Audio;
using Track = Wysicraft.Core.Audio.Track;
using Wysicraft.Models;
namespace Wysicraft.Designer;

/// <summary>Music maker: layered 8-bit songs. A keyboard to play and record, a piano roll to draw notes, a sheet music
/// editor per layer, instruments with knobs, MIDI import/export and turning recordings into notes. Saved into the
/// project as a sound (Ogg or WAV) with the song kept beside it (name.ogg.song) so it can be opened again.</summary>
sealed class MusicMaker : Window
{
    public sealed record SaveRequest(Song Song, string Format, bool AsNew, string? Path, float[] Left, float[] Right);
    public const int Low = 24, High = 108; // piano roll rows: C1 to C8
    internal static bool NoPrompts; // smoke tests close windows without being asked to save
    public const double RowH = 12;
    static readonly Color[] LayerColors = [Color.FromRgb(90, 170, 255), Color.FromRgb(255, 140, 90), Color.FromRgb(120, 220, 120), Color.FromRgb(230, 110, 200), Color.FromRgb(250, 210, 80), Color.FromRgb(140, 120, 255), Color.FromRgb(80, 220, 210), Color.FromRgb(240, 90, 110)];
    public static Color ColorOf(int layer) => LayerColors[layer % LayerColors.Length];
    static readonly Brush Heading = Brushes.LightSkyBlue;

    readonly Func<SaveRequest, string> save;
    readonly Func<(float[][] Channels, int Rate, string Name)?> pickAudio;
    internal Song song;
    string? path;
    internal int layerIndex;
    bool dirty;
    internal readonly AudioOut audio = new();
    readonly List<string> undo = [], redo = [];
    internal readonly HashSet<Note> selectedNotes = [];
    internal int noteLength = 4, startStep;
    internal double colW = 18, velocity = 0.9;
    int octave = 4;

    // Playback and recording
    SongPlayer? player; bool recording; int ticks;
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromMilliseconds(25) };
    readonly Dictionary<Key, (int Pitch, IHeldNote Note, double Start)> heldKeys = [];
    readonly Dictionary<int, IHeldNote> mouseNotes = [];
    readonly List<(int Pitch, double Start)> pendingMouse = [];

    // UI
    readonly RollView roll;
    readonly PianoStrip piano;
    readonly Canvas overlay = new() { IsHitTestVisible = false };
    readonly Line playhead = new() { Stroke = Brushes.OrangeRed, StrokeThickness = 2, Visibility = Visibility.Collapsed };
    readonly Line startMarker = new() { Stroke = Brushes.LimeGreen, StrokeThickness = 1, StrokeDashArray = [3, 2] };
    readonly ScrollViewer rollScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Visible, VerticalScrollBarVisibility = ScrollBarVisibility.Visible, Background = new SolidColorBrush(Color.FromRgb(30, 33, 40)) };
    readonly ScrollViewer keysScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Width = 52 };
    readonly ScrollViewer rulerScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Height = 20 };
    readonly Canvas keysCanvas = new(), rulerCanvas = new() { Background = new SolidColorBrush(Color.FromRgb(40, 44, 52)), Height = 20 };
    readonly StackPanel layersPanel = new();
    readonly TextBlock status = new() { Margin = new Thickness(8, 4, 8, 4) };
    readonly TextBox nameBox = new() { Width = 130 }, bpmBox = new() { Width = 48 }, barsBox = new() { Width = 40 };
    readonly ComboBox meterBox = new() { Width = 60 }, keyBox = new() { Width = 52 }, scaleBox = new() { Width = 120 }, gridBox = new() { Width = 110 }, lengthBox = new() { Width = 120 }, formatBox = new() { Width = 60 };
    readonly CheckBox loopBox = new() { Content = "Loop", VerticalAlignment = VerticalAlignment.Center }, clickBox = new() { Content = "Metronome", VerticalAlignment = VerticalAlignment.Center, IsChecked = false }, seamlessBox = new() { Content = "Seamless loop", VerticalAlignment = VerticalAlignment.Center, ToolTip = "Save so the sound loops without a gap: notes ringing past the end continue at the start (for music that repeats)." };
    readonly Slider velocitySlider = new() { Minimum = 0.1, Maximum = 1, Value = 0.9, Width = 80, VerticalAlignment = VerticalAlignment.Center };
    readonly Button playButton = new() { Content = "▶ Play" }, recordButton = new() { Content = "● Record" };
    bool syncing, emptyStarter;

    public MusicMaker(Window owner, Song song, string? assetPath, Func<SaveRequest, string> save, Func<(float[][] Channels, int Rate, string Name)?> pickAudio)
    {
        Owner = owner; this.song = song; path = assetPath; this.save = save; this.pickAudio = pickAudio;
        if (song.Tracks.Count == 0) song.Tracks.Add(new Track { Name = "Melody", Instrument = Instruments.Get("Square lead") });
        // A song started for a transcription gets its layer from the recording; the empty starter layer goes then.
        emptyStarter = song.Tracks.Count == 1 && song.Tracks[0].Notes.Count == 0;
        SetResourceReference(StyleProperty, typeof(Window));
        Width = 1320; Height = 860; MinWidth = 1000; MinHeight = 640; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        roll = new RollView(this); piano = new PianoStrip(this);
        var root = new DockPanel(); Content = root;
        root.Children.Add(Dock(BuildToolbar(), System.Windows.Controls.Dock.Top));
        root.Children.Add(Dock(BuildPiano(), System.Windows.Controls.Dock.Top));
        root.Children.Add(Dock(status, System.Windows.Controls.Dock.Bottom));
        root.Children.Add(Dock(BuildLayers(), System.Windows.Controls.Dock.Left));
        root.Children.Add(BuildRoll());
        clock.Tick += (_, _) => Tick();
        PreviewKeyDown += KeyDownHandler; PreviewKeyUp += KeyUpHandler;
        Closing += (_, e) => { if (!ConfirmDiscard("closing")) e.Cancel = true; };
        Closed += (_, _) => { clock.Stop(); audio.Dispose(); };
        Deactivated += (_, _) => ReleaseAllKeys();
        if (audio.Problem != null) Notice(audio.Problem);
        SyncControls(); RebuildLayers(); Relayout();
        Loaded += (_, _) => { rollScroll.ScrollToVerticalOffset((High - 76) * RowH); roll.Focus(); };
    }
    static T Dock<T>(T e, System.Windows.Controls.Dock d) where T : UIElement { DockPanel.SetDock(e, d); return e; }
    internal Track Layer => song.Tracks[Math.Clamp(layerIndex, 0, song.Tracks.Count - 1)];

    // ---------------- Toolbar ----------------
    UIElement BuildToolbar()
    {
        var panel = new StackPanel { Margin = new Thickness(6, 6, 6, 2) };
        var row1 = new WrapPanel(); var row2 = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(row1); panel.Children.Add(row2);
        Button B(Panel p, string text, Action run, string tip = "") { var b = new Button { Content = text, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(8, 2, 8, 2), ToolTip = tip.Length > 0 ? tip : null }; b.Click += (_, _) => Guard(run); p.Children.Add(b); return b; }
        void L(Panel p, string text) => p.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) });
        playButton.Click += (_, _) => Guard(PlayOrPause); playButton.Margin = new Thickness(0, 0, 4, 0); playButton.Padding = new Thickness(8, 2, 8, 2); playButton.ToolTip = "Play from the green marker, pause, and play on from where it paused (Space). Stop goes back to the marker; click the ruler to move it.";
        recordButton.Click += (_, _) => Guard(() => { if (recording) StopPlayback(); else Play(true); }); recordButton.Margin = new Thickness(0, 0, 4, 0); recordButton.Padding = new Thickness(8, 2, 8, 2); recordButton.ToolTip = "Record the selected layer: after a bar of count-in, play keys (on screen or your keyboard) along with the song (F9).";
        row1.Children.Add(playButton); B(row1, "■ Stop", StopPlayback, "Stop (Esc)"); row1.Children.Add(recordButton);
        row1.Children.Add(loopBox); loopBox.Margin = new Thickness(6, 0, 0, 0); row1.Children.Add(clickBox); clickBox.Margin = new Thickness(8, 0, 0, 0);
        L(row1, "Tempo"); row1.Children.Add(bpmBox); L(row1, "BPM");
        L(row1, "Time"); meterBox.ItemsSource = new[] { "2/4", "3/4", "4/4", "5/4", "6/8", "7/8", "9/8", "12/8" }; row1.Children.Add(meterBox);
        L(row1, "Key"); keyBox.ItemsSource = MusicTheory.Keys; row1.Children.Add(keyBox); scaleBox.ItemsSource = MusicTheory.Scales.Keys.ToArray(); row1.Children.Add(scaleBox);
        L(row1, "Bars"); row1.Children.Add(barsBox); B(row1, "Fit", () => Change(() => song.Bars = Math.Min(512, song.BarsNeeded())), "Just enough bars for every note");
        L(row1, "Grid"); gridBox.ItemsSource = new[] { "1/8 notes", "1/16 notes", "1/32 notes" }; row1.Children.Add(gridBox);
        L(row1, "New notes"); row1.Children.Add(lengthBox);
        L(row1, "Velocity"); row1.Children.Add(velocitySlider);
        B(row1, "−", () => Zoom(1 / 1.25), "Zoom out"); B(row1, "+", () => Zoom(1.25), "Zoom in");

        L(row2, "Sounds"); row2.Children.Add(fontBox); fontBox.ToolTip = "Play the song with the built-in chip sounds, or a SoundFont of recorded instruments. \"Get more soundfonts…\" downloads or imports others.";
        fontBox.SelectionChanged += (_, _) => { if (!syncing) Guard(ChooseFont); };
        L(row2, "Name"); row2.Children.Add(nameBox);
        L(row2, "Format"); formatBox.ItemsSource = AudioFiles.Formats; row2.Children.Add(formatBox); formatBox.ToolTip = "ogg: small, plays everywhere including Minecraft. wav: uncompressed, web and desktop apps.";
        row2.Children.Add(seamlessBox); seamlessBox.Margin = new Thickness(8, 0, 8, 0);
        B(row2, "Save", () => Save(false), "Save into the project's sounds (Ctrl+S)"); B(row2, "Save as new", () => Save(true));
        B(row2, "Sheet music…", OpenSheet, "Edit the selected layer as sheet music");
        B(row2, "Import MIDI…", ImportMidi); B(row2, "Export MIDI…", ExportMidi);
        B(row2, "Song from audio…", () => ImportAudio(), "Turn a recording (MP3, WAV, Ogg, M4A…) into 8-bit layers, or just its melody");
        B(row2, "Export audio…", ExportAudio, "Save the song as an MP3, WAV or Ogg file anywhere");
        B(row2, "Undo", Undo, "Ctrl+Z"); B(row2, "Redo", Redo, "Ctrl+Y");

        bpmBox.LostFocus += (_, _) => Guard(() => ApplyNumber(bpmBox, 20, 400, v => song.Bpm = v));
        barsBox.LostFocus += (_, _) => Guard(() => ApplyNumber(barsBox, 1, 512, v => song.Bars = (int)v));
        foreach (var box in new[] { bpmBox, barsBox }) box.KeyDown += (_, e) => { if (e.Key == Key.Enter) roll.Focus(); };
        meterBox.SelectionChanged += (_, _) => { if (syncing || meterBox.SelectedItem is not string m) return; var parts = m.Split('/'); Change(() => { song.Numerator = int.Parse(parts[0]); song.Denominator = int.Parse(parts[1]); }); };
        keyBox.SelectionChanged += (_, _) => { if (!syncing && keyBox.SelectedItem is string k) Change(() => song.Key = k); };
        scaleBox.SelectionChanged += (_, _) => { if (!syncing && scaleBox.SelectedItem is string s) Change(() => song.Scale = s); };
        gridBox.SelectionChanged += (_, _) => { if (!syncing) Guard(() => SetGrid(new[] { 2, 4, 8 }[gridBox.SelectedIndex])); };
        lengthBox.SelectionChanged += (_, _) => { if (!syncing && lengthBox.SelectedItem is ComboBoxItem { Tag: int steps }) noteLength = steps; };
        nameBox.LostFocus += (_, _) => { var n = PixelEditor.SafeName(nameBox.Text); if (n != song.Name) { song.Name = n; dirty = true; UpdateTitle(); } };
        velocitySlider.ValueChanged += (_, _) =>
        {
            velocity = Math.Round(velocitySlider.Value, 2);
            if (!syncing && selectedNotes.Count > 0) { foreach (var n in selectedNotes) n.Velocity = velocity; dirty = true; roll.InvalidateVisual(); }
        };
        velocitySlider.PreviewMouseLeftButtonDown += (_, _) => { if (selectedNotes.Count > 0) Push(); };
        seamlessBox.Click += (_, _) => { song.Loop = seamlessBox.IsChecked == true; dirty = true; UpdateTitle(); };
        return panel;
    }
    void ApplyNumber(TextBox box, double min, double max, Action<double> set)
    {
        if (!double.TryParse(box.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) || v < min || v > max) { SyncControls(); throw new InvalidOperationException($"Use a number from {min} to {max}."); }
        Change(() => set(v));
    }
    readonly ComboBox fontBox = new() { MinWidth = 150 };
    const string ChipSounds = "Chip (built-in)", MoreFonts = "Get more soundfonts…";
    /// <summary>The dropdown's fonts: the chip sounds, every installed font, the song's own if it isn't installed here, and
    /// the way to get more at the bottom.</summary>
    void FillFonts()
    {
        var items = new List<object> { ChipSounds };
        items.AddRange(SoundFonts.Installed());
        if (song.SoundFont.Length > 0 && !items.OfType<string>().Contains(song.SoundFont, StringComparer.OrdinalIgnoreCase)) items.Add(song.SoundFont + " (not installed)");
        items.Add(new Separator()); items.Add(MoreFonts);
        bool was = syncing; syncing = true;
        fontBox.ItemsSource = items;
        fontBox.SelectedItem = song.SoundFont.Length == 0 ? ChipSounds : items.OfType<string>().FirstOrDefault(i => i.Equals(song.SoundFont, StringComparison.OrdinalIgnoreCase) || i == song.SoundFont + " (not installed)");
        syncing = was;
    }
    async void ChooseFont()
    {
        if (fontBox.SelectedItem is not string pick) { FillFonts(); return; }
        if (pick == MoreFonts) { FillFonts(); if (SoundFontWindow.Show(this, song.SoundFont) is string chosen) await UseFont(chosen); else FillFonts(); return; }
        if (pick.EndsWith(" (not installed)")) return;
        await UseFont(pick == ChipSounds ? "" : pick);
    }
    /// <summary>Plays the song with a font (loading it first, which can take a moment for a big one), or the chip sounds.</summary>
    async Task UseFont(string name)
    {
        if (name.Length > 0 && SoundFonts.Load(name) == null)
        {
            Notice($"Loading {name}…"); IsEnabled = false;
            try { await Task.Run(() => SoundFonts.Load(name)); }
            catch (Exception ex) { MessageBox.Show(this, $"{name} couldn't be loaded: {ex.Message}", "Music maker", MessageBoxButton.OK, MessageBoxImage.Warning); FillFonts(); return; }
            finally { IsEnabled = true; }
        }
        if (name != song.SoundFont) Change(() => song.SoundFont = name, true);
        FillFonts();
        Notice(name.Length == 0 ? "Playing with the chip sounds." : $"Playing with {name}: pick each layer's instrument from its list.");
    }
    void SyncControls()
    {
        syncing = true;
        FillFonts();
        nameBox.Text = song.Name; bpmBox.Text = song.Bpm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); barsBox.Text = song.Bars.ToString();
        meterBox.SelectedItem = song.Numerator + "/" + song.Denominator; if (meterBox.SelectedItem == null) { var list = ((string[])meterBox.ItemsSource).ToList(); list.Add(song.Numerator + "/" + song.Denominator); meterBox.ItemsSource = list; meterBox.SelectedItem = song.Numerator + "/" + song.Denominator; }
        keyBox.SelectedItem = song.Key; scaleBox.SelectedItem = song.Scale;
        gridBox.SelectedIndex = song.StepsPerBeat switch { 2 => 0, 8 => 2, _ => 1 };
        formatBox.SelectedItem ??= path != null && path.EndsWith(".wav") ? "wav" : "ogg";
        seamlessBox.IsChecked = song.Loop;
        // Note lengths that fit the grid.
        var items = new List<ComboBoxItem>();
        foreach (var (symbol, name, quarters) in MusicTheory.Values)
            foreach (bool dotted in new[] { false, true })
            {
                double steps = quarters * song.StepsPerBeat * (dotted ? 1.5 : 1);
                if (steps >= 1 && Math.Abs(steps - Math.Round(steps)) < 1e-9 && steps <= 64) items.Add(new ComboBoxItem { Content = (dotted ? "dotted " : "") + name, Tag = (int)steps });
            }
        lengthBox.ItemsSource = items;
        lengthBox.SelectedItem = items.FirstOrDefault(i => (int)i.Tag == noteLength) ?? items.FirstOrDefault(i => (int)i.Tag == song.StepsPerBeat) ?? items[0];
        noteLength = (int)((ComboBoxItem)lengthBox.SelectedItem).Tag;
        syncing = false;
        UpdateTitle();
    }
    void SetGrid(int steps)
    {
        if (steps == song.StepsPerBeat) return;
        Push();
        double k = steps / (double)song.StepsPerBeat;
        foreach (var n in song.Tracks.SelectMany(t => t.Notes)) { n.Step = (int)Math.Round(n.Step * k); n.Length = Math.Max(1, (int)Math.Round(n.Length * k)); }
        startStep = (int)Math.Round(startStep * k); noteLength = Math.Max(1, (int)Math.Round(noteLength * k));
        song.StepsPerBeat = steps; colW = Math.Clamp(colW / k, 3, 80);
        dirty = true; SyncControls(); Relayout();
    }
    void Zoom(double factor) { colW = Math.Clamp(colW * factor, 3, 80); Relayout(); }

    // ---------------- Keyboard ----------------
    UIElement BuildPiano()
    {
        var box = new DockPanel { Margin = new Thickness(6, 2, 6, 4), Height = 76 };
        var side = new StackPanel { Width = 120, Margin = new Thickness(0, 0, 6, 0) };
        var octaveText = new TextBlock { Text = OctaveText(), Margin = new Thickness(0, 2, 0, 2) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var down = new Button { Content = "Oct −", Padding = new Thickness(6, 1, 6, 1) }; var up = new Button { Content = "Oct +", Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(4, 0, 0, 0) };
        down.Click += (_, _) => { octave = Math.Max(1, octave - 1); octaveText.Text = OctaveText(); piano.InvalidateVisual(); };
        up.Click += (_, _) => { octave = Math.Min(7, octave + 1); octaveText.Text = OctaveText(); piano.InvalidateVisual(); };
        buttons.Children.Add(down); buttons.Children.Add(up);
        side.Children.Add(new TextBlock { Text = "Keyboard", Foreground = Heading, FontWeight = FontWeights.SemiBold });
        side.Children.Add(octaveText); side.Children.Add(buttons);
        side.ToolTip = "Play with the mouse or your computer keyboard: Z S X D C V G B H N J M play one octave, Q 2 W 3 E R 5 T 6 Y 7 U the one above. PageUp/PageDown change octave.";
        box.Children.Add(Dock(side, System.Windows.Controls.Dock.Left)); box.Children.Add(piano);
        return box;
        string OctaveText() => $"Keys start at C{octave}";
    }
    internal int KeyboardOctave => octave;
    static readonly Key[] LowerRow = [Key.Z, Key.S, Key.X, Key.D, Key.C, Key.V, Key.G, Key.B, Key.H, Key.N, Key.J, Key.M, Key.OemComma, Key.L, Key.OemPeriod, Key.OemSemicolon, Key.OemQuestion];
    static readonly Key[] UpperRow = [Key.Q, Key.D2, Key.W, Key.D3, Key.E, Key.R, Key.D5, Key.T, Key.D6, Key.Y, Key.D7, Key.U, Key.I, Key.D9, Key.O, Key.D0, Key.P];
    int? PitchForKey(Key key)
    {
        int i = Array.IndexOf(LowerRow, key); if (i >= 0) return (octave + 1) * 12 + i;
        i = Array.IndexOf(UpperRow, key); if (i >= 0) return (octave + 2) * 12 + i;
        return null;
    }
    internal HashSet<int> Sounding => heldKeys.Values.Select(h => h.Pitch).Concat(mouseNotes.Keys).ToHashSet();
    void KeyDownHandler(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl)
        {
            switch (key)
            {
                case Key.Z: Guard(Undo); e.Handled = true; return;
                case Key.Y: Guard(Redo); e.Handled = true; return;
                case Key.S: Guard(() => Save(false)); e.Handled = true; return;
                case Key.A: selectedNotes.Clear(); selectedNotes.UnionWith(Layer.Notes); roll.InvalidateVisual(); e.Handled = true; return;
                case Key.D: Guard(DuplicateSelection); e.Handled = true; return;
                case Key.C: CopySelection(); e.Handled = true; return;
                case Key.V: Guard(Paste); e.Handled = true; return;
            }
            return;
        }
        switch (key)
        {
            case Key.Space: Guard(PlayOrPause); e.Handled = true; return;
            case Key.Escape: StopPlayback(); e.Handled = true; return;
            case Key.Delete: case Key.Back: if (selectedNotes.Count > 0) { Change(() => { Layer.Notes.RemoveAll(selectedNotes.Contains); selectedNotes.Clear(); }); } e.Handled = true; return;
            case Key.PageUp: octave = Math.Min(7, octave + 1); piano.InvalidateVisual(); e.Handled = true; return;
            case Key.PageDown: octave = Math.Max(1, octave - 1); piano.InvalidateVisual(); e.Handled = true; return;
            case Key.Left: case Key.Right: case Key.Up: case Key.Down:
                if (selectedNotes.Count > 0) { Nudge(key); e.Handled = true; return; }
                break;
        }
        if (key == Key.F9 || (key == Key.R && Keyboard.Modifiers == ModifierKeys.Shift)) { Guard(() => { if (recording) StopPlayback(); else Play(true); }); e.Handled = true; return; }
        if (PitchForKey(key) is int pitch)
        {
            e.Handled = true;
            if (heldKeys.ContainsKey(key) || e.IsRepeat) return;
            heldKeys[key] = (pitch, Sound(pitch), SongStep());
            piano.InvalidateVisual();
        }
    }
    void KeyUpHandler(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (!heldKeys.Remove(key, out var held)) return;
        held.Note.Release(); piano.InvalidateVisual();
        if (recording) Record(held.Pitch, held.Start, SongStep());
        e.Handled = true;
    }
    void ReleaseAllKeys()
    {
        foreach (var (_, held) in heldKeys) { held.Note.Release(); if (recording) Record(held.Pitch, held.Start, SongStep()); }
        heldKeys.Clear();
        foreach (var n in mouseNotes.Values) n.Release(); mouseNotes.Clear();
        piano.InvalidateVisual();
    }
    // The piano strip (mouse) and the roll's note previews go through here.
    internal void PianoDown(int pitch)
    {
        if (mouseNotes.ContainsKey(pitch)) return;
        mouseNotes[pitch] = Sound(pitch);
        if (recording) pendingMouse.Add((pitch, SongStep()));
        piano.InvalidateVisual();
    }
    internal void PianoUp(int pitch)
    {
        if (!mouseNotes.Remove(pitch, out var n)) return;
        n.Release();
        int i = pendingMouse.FindIndex(p => p.Pitch == pitch); if (i >= 0) { Record(pitch, pendingMouse[i].Start, SongStep()); pendingMouse.RemoveAt(i); }
        piano.InvalidateVisual();
    }
    /// <summary>A note on the selected layer's instrument, held until released: the chip sound, or the song's SoundFont.</summary>
    IHeldNote Sound(int pitch)
    {
        if (SoundFonts.Load(song.SoundFont) is { } font)
        {
            var patch = SoundFonts.Patch(Layer.Instrument);
            return audio.FontNote(font, patch, SoundFonts.Key(Layer, new Note { Pitch = pitch }, patch.Channel == 9), velocity * Math.Clamp(Layer.Volume / 0.8, 0.2, 1));
        }
        return audio.NoteOn(Layer.Instrument, pitch + Layer.Transpose + Layer.Fine / 100, velocity, Layer.Pan, Layer.Volume);
    }
    internal void Preview(int pitch, double seconds = 0.25)
    {
        var note = Sound(pitch);
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) }; t.Tick += (_, _) => { note.Release(); t.Stop(); }; t.Start();
    }

    // ---------------- Layers ----------------
    UIElement BuildLayers()
    {
        var outer = new DockPanel { Width = 330, Margin = new Thickness(6, 0, 4, 6) };
        var header = new StackPanel(); DockPanel.SetDock(header, System.Windows.Controls.Dock.Top); outer.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "Layers", Foreground = Heading, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) }; header.Children.Add(buttons);
        void B(string text, Action run, string tip) { var b = new Button { Content = text, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(6, 1, 6, 1), ToolTip = tip }; b.Click += (_, _) => Guard(run); buttons.Children.Add(b); }
        B("+ Add", AddLayer, "A new layer (track) with its own instrument");
        B("Duplicate", () => Change(() => { var copy = Json.Clone(Layer); copy.Name += " copy"; song.Tracks.Insert(layerIndex + 1, copy); layerIndex++; }, true), "Copy the selected layer");
        B("Delete", () => { if (song.Tracks.Count == 1) throw new InvalidOperationException("A song needs at least one layer."); Change(() => { song.Tracks.RemoveAt(layerIndex); layerIndex = Math.Max(0, layerIndex - 1); }, true); }, "Delete the selected layer");
        B("▲", () => { if (layerIndex > 0) Change(() => { (song.Tracks[layerIndex - 1], song.Tracks[layerIndex]) = (song.Tracks[layerIndex], song.Tracks[layerIndex - 1]); layerIndex--; }, true); }, "Move up");
        B("▼", () => { if (layerIndex < song.Tracks.Count - 1) Change(() => { (song.Tracks[layerIndex + 1], song.Tracks[layerIndex]) = (song.Tracks[layerIndex], song.Tracks[layerIndex + 1]); layerIndex++; }, true); }, "Move down");
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = layersPanel };
        outer.Children.Add(scroll);
        return outer;
    }
    void AddLayer()
    {
        if (song.Tracks.Count >= 32) throw new InvalidOperationException("A song can have 32 layers.");
        Change(() =>
        {
            string[] suggestions = ["Melody", "Bass", "Drums", "Harmony", "Arpeggio", "Pad"];
            string name = suggestions.FirstOrDefault(s => song.Tracks.All(t => t.Name != s)) ?? "Layer " + (song.Tracks.Count + 1);
            var ins = name switch { "Bass" => "Triangle bass", "Drums" => "Drum kit", "Harmony" => "Pulse 25%", "Arpeggio" => "Chip chord", "Pad" => "Soft pad", _ => "Square lead" };
            song.Tracks.Add(new Track { Name = name, Instrument = Instruments.Get(ins) }); layerIndex = song.Tracks.Count - 1;
        }, true);
    }
    readonly List<Border> cards = [];
    void RebuildLayers()
    {
        layersPanel.Children.Clear(); cards.Clear();
        for (int i = 0; i < song.Tracks.Count; i++) { var card = LayerCard(i); cards.Add(card); layersPanel.Children.Add(card); }
    }
    // Choosing a layer only restyles the cards, so a click on a knob of another layer still turns the knob.
    void SelectLayer(int index)
    {
        if (layerIndex == index) return;
        layerIndex = index; selectedNotes.Clear();
        for (int i = 0; i < cards.Count; i++) StyleCard(cards[i], i);
        Relayout();
    }
    void StyleCard(Border card, int index)
    {
        bool chosen = index == layerIndex;
        card.BorderThickness = new Thickness(chosen ? 2 : 1);
        card.BorderBrush = chosen ? new SolidColorBrush(ColorOf(index)) : new SolidColorBrush(Color.FromRgb(70, 76, 88));
        card.Background = new SolidColorBrush(chosen ? Color.FromRgb(44, 50, 62) : Color.FromRgb(36, 40, 48));
    }
    Border LayerCard(int index)
    {
        var t = song.Tracks[index];
        var card = new Border { Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(6), CornerRadius = new CornerRadius(4) }; StyleCard(card, index);
        card.PreviewMouseLeftButtonDown += (_, _) => SelectLayer(index);
        var stack = new StackPanel(); card.Child = stack;
        var top = new DockPanel();
        top.Children.Add(Dock(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(ColorOf(index)), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center }, System.Windows.Controls.Dock.Left));
        var solo = new ToggleButton { Content = "S", Width = 24, IsChecked = t.Solo, ToolTip = "Solo: only soloed layers play", Margin = new Thickness(4, 0, 0, 0) };
        var mute = new ToggleButton { Content = "M", Width = 24, IsChecked = t.Mute, ToolTip = "Mute", Margin = new Thickness(4, 0, 0, 0) };
        solo.Click += (_, _) => { t.Solo = solo.IsChecked == true; dirty = true; roll.InvalidateVisual(); };
        mute.Click += (_, _) => { t.Mute = mute.IsChecked == true; dirty = true; roll.InvalidateVisual(); };
        top.Children.Add(Dock(solo, System.Windows.Controls.Dock.Right)); top.Children.Add(Dock(mute, System.Windows.Controls.Dock.Right));
        var name = new TextBox { Text = t.Name }; name.LostFocus += (_, _) => { if (name.Text.Trim().Length > 0 && name.Text != t.Name) { Push(); t.Name = name.Text.Trim(); dirty = true; UpdateTitle(); } };
        top.Children.Add(name); stack.Children.Add(top);
        var insRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var edit = new Button { Content = "Edit…", Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(4, 0, 0, 0), ToolTip = "Wave, envelope, vibrato, slide, arpeggio, crush and echo" };
        edit.Click += (_, _) => Guard(() => { if (InstrumentEditor.Edit(this, t.Instrument, audio) is Instrument changed) Change(() => t.Instrument = changed, true); });
        insRow.Children.Add(Dock(edit, System.Windows.Controls.Dock.Right));
        var combo = new ComboBox();
        if (SoundFonts.Load(song.SoundFont) is { } font)
        {
            // The font's instruments, then its drum kits; each remembered by its General MIDI bank and program.
            edit.Visibility = Visibility.Collapsed;
            var patches = new Dictionary<string, (int Bank, int Program)>();
            foreach (var (bank, program, presetName) in SoundFonts.Presets(font, false)) patches.TryAdd(bank == 0 ? $"{program + 1}. {presetName}" : $"{program + 1}. {presetName} (bank {bank})", (bank, program));
            foreach (var (bank, program, presetName) in SoundFonts.Presets(font, true)) patches.TryAdd("Drums: " + presetName, (bank, program));
            var (_, nowBank, nowProgram) = SoundFonts.Patch(t.Instrument);
            combo.ItemsSource = patches.Keys.ToArray();
            combo.SelectedItem = patches.FirstOrDefault(p => p.Value == (nowBank, nowProgram)).Key ?? patches.FirstOrDefault(p => p.Value.Program == nowProgram && p.Value.Bank < 128 == nowBank < 128).Key;
            combo.ToolTip = $"{song.SoundFont} instrument";
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is not string n || !patches.TryGetValue(n, out var chosen) || chosen == (nowBank, nowProgram)) return;
                // The chip instrument follows along, so the layer still sounds right if the song goes back to chip sounds.
                Change(() => { var chip = chosen.Bank >= 128 ? Instruments.Get("Drum kit") : Midi.ForProgram(chosen.Program); chip.Program = chosen.Program; chip.Bank = chosen.Bank; chip.Octave = t.Instrument.Octave; t.Instrument = chip; }, true);
                Preview(chosen.Bank >= 128 ? 38 : 60);
            };
        }
        else
        {
            combo.ItemsSource = Instruments.Presets.Select(p => p.Name).Append("(custom)").ToArray();
            combo.SelectedItem = Instruments.Presets.Any(p => p.Name == t.Instrument.Name && Json.Write(p) == Json.Write(t.Instrument) || p.Name == t.Instrument.Name && t.Instrument.Program >= 0) ? t.Instrument.Name : "(custom)";
            combo.ToolTip = "Instrument: " + t.Instrument.Name;
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string n && n != "(custom)" && n != t.Instrument.Name) { Change(() => t.Instrument = Instruments.Get(n), true); Preview(60); } };
        }
        insRow.Children.Add(combo); stack.Children.Add(insRow);
        stack.Children.Add(Knob("Volume", 0, 1, t.Volume, v => t.Volume = v, v => $"{v * 100:0}%"));
        stack.Children.Add(Knob("Pan", -1, 1, t.Pan, v => t.Pan = v, v => Math.Abs(v) < 0.02 ? "centre" : v < 0 ? $"L {-v * 100:0}" : $"R {v * 100:0}"));
        stack.Children.Add(Knob("Pitch", -24, 24, t.Transpose, v => t.Transpose = (int)Math.Round(v), v => $"{Math.Round(v):+0;-0;0} st", 1));
        stack.Children.Add(Knob("Fine", -100, 100, t.Fine, v => t.Fine = Math.Round(v), v => $"{Math.Round(v):+0;-0;0} ¢"));
        stack.Children.Add(new TextBlock { Text = $"{t.Notes.Count} note{(t.Notes.Count == 1 ? "" : "s")}", Opacity = 0.6, FontSize = 11 });
        return card;
    }
    UIElement Knob(string label, double min, double max, double value, Action<double> set, Func<double, string> show, double tick = 0)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
        var text = new TextBlock { Text = show(value), Width = 58, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
        row.Children.Add(Dock(new TextBlock { Text = label, Width = 48, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 }, System.Windows.Controls.Dock.Left));
        row.Children.Add(Dock(text, System.Windows.Controls.Dock.Right));
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, IsSnapToTickEnabled = tick > 0, TickFrequency = tick > 0 ? tick : 0.01, VerticalAlignment = VerticalAlignment.Center };
        slider.PreviewMouseLeftButtonDown += (_, _) => Push();
        slider.ValueChanged += (_, _) => { set(slider.Value); text.Text = show(slider.Value); dirty = true; UpdateTitle(); };
        slider.MouseDoubleClick += (_, _) => slider.Value = min < 0 ? 0 : max; // back to centre / full
        row.Children.Add(slider);
        return row;
    }

    // ---------------- Piano roll ----------------
    UIElement BuildRoll()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 6, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition());
        rulerScroll.Content = rulerCanvas; Grid.SetColumn(rulerScroll, 1); grid.Children.Add(rulerScroll);
        keysScroll.Content = keysCanvas; Grid.SetRow(keysScroll, 1); grid.Children.Add(keysScroll);
        var layered = new Grid(); layered.Children.Add(roll); layered.Children.Add(overlay);
        overlay.Children.Add(startMarker); overlay.Children.Add(playhead);
        rollScroll.Content = layered; Grid.SetRow(rollScroll, 1); Grid.SetColumn(rollScroll, 1); grid.Children.Add(rollScroll);
        rollScroll.ScrollChanged += (_, e) => { keysScroll.ScrollToVerticalOffset(rollScroll.VerticalOffset); rulerScroll.ScrollToHorizontalOffset(rollScroll.HorizontalOffset); };
        rollScroll.PreviewMouseWheel += (_, e) => { if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return; Zoom(e.Delta > 0 ? 1.15 : 1 / 1.15); e.Handled = true; };
        rulerCanvas.MouseLeftButtonDown += (_, e) => { startStep = Math.Clamp((int)Math.Round(e.GetPosition(rulerCanvas).X / colW), 0, song.TotalSteps - 1); if (player == null && pausedAt != null) StopPlayback(); PlaceMarkers(); UpdateStatus(); };
        rulerCanvas.ToolTip = "Click to choose where playing and recording start";
        return grid;
    }
    internal void Relayout()
    {
        double w = song.TotalSteps * colW + 40, h = (High - Low + 1) * RowH;
        roll.Width = w; roll.Height = h; overlay.Width = w; overlay.Height = h; rulerCanvas.Width = w + 40; keysCanvas.Height = h;
        // Ruler: bar numbers.
        rulerCanvas.Children.Clear();
        for (int bar = 0; bar < song.Bars; bar++)
        {
            double x = bar * song.StepsPerBar * colW;
            rulerCanvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = 8, Y2 = 20, Stroke = Brushes.Gray });
            var label = new TextBlock { Text = (bar + 1).ToString(), FontSize = 10, Foreground = Brushes.LightGray }; Canvas.SetLeft(label, x + 3); Canvas.SetTop(label, 2); rulerCanvas.Children.Add(label);
        }
        // Key names (drum names for a drum layer).
        keysCanvas.Children.Clear(); bool drums = Layer.Instrument.Wave == "drums";
        for (int p = Low; p <= High; p++)
        {
            double y = (High - p) * RowH; bool black = new[] { 1, 3, 6, 8, 10 }.Contains(p % 12);
            keysCanvas.Children.Add(Positioned(new Rectangle { Width = 52, Height = RowH, Fill = new SolidColorBrush(black ? Color.FromRgb(34, 36, 42) : Color.FromRgb(200, 204, 212)) }, 0, y));
            string label = drums ? (Instruments.Drums.Any(d => d.Pitch == p) ? Instruments.DrumName(p) : "") : p % 12 == 0 ? MusicTheory.Name(p) : "";
            if (label.Length > 0) keysCanvas.Children.Add(Positioned(new TextBlock { Text = label, FontSize = 9, Foreground = black ? Brushes.White : Brushes.Black }, 2, y));
        }
        roll.InvalidateVisual(); PlaceMarkers(); UpdateStatus();
    }
    static UIElement Positioned(UIElement e, double x, double y) { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); return e; }
    void PlaceMarkers()
    {
        double h = (High - Low + 1) * RowH, x = startStep * colW;
        startMarker.X1 = startMarker.X2 = x; startMarker.Y1 = 0; startMarker.Y2 = h;
        playhead.Y1 = 0; playhead.Y2 = h;
    }

    // Note editing (called by RollView)
    internal void AddNote(int step, int pitch, int length)
    {
        Push();
        var n = new Note { Step = step, Pitch = pitch, Length = length, Velocity = velocity };
        Layer.Notes.Add(n); selectedNotes.Clear(); selectedNotes.Add(n);
        dirty = true; Preview(pitch); GrowToFit(); roll.InvalidateVisual(); UpdateStatus();
    }
    internal void Edited() { dirty = true; GrowToFit(); roll.InvalidateVisual(); UpdateStatus(); UpdateTitle(); }
    internal void Push()
    {
        undo.Add(Json.Write(song)); if (undo.Count > 200) undo.RemoveAt(0); redo.Clear();
    }
    void GrowToFit() { int need = song.BarsNeeded(); if (need > song.Bars && need <= 512) { song.Bars = need; barsBox.Text = song.Bars.ToString(); Relayout(); } }
    void Nudge(Key key)
    {
        Push();
        foreach (var n in selectedNotes)
            switch (key)
            {
                case Key.Left: n.Step = Math.Max(0, n.Step - 1); break;
                case Key.Right: n.Step++; break;
                case Key.Up: n.Pitch = Math.Min(127, n.Pitch + (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 12 : 1)); break;
                case Key.Down: n.Pitch = Math.Max(0, n.Pitch - (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 12 : 1)); break;
            }
        if (key is Key.Up or Key.Down && selectedNotes.Count == 1) Preview(selectedNotes.First().Pitch);
        Edited();
    }
    void DuplicateSelection()
    {
        if (selectedNotes.Count == 0) return;
        int from = selectedNotes.Min(n => n.Step), to = selectedNotes.Max(n => n.Step + n.Length), shift = to - from;
        Push(); var copies = selectedNotes.Select(n => { var c = n.Copy(); c.Step += shift; return c; }).ToList();
        Layer.Notes.AddRange(copies); selectedNotes.Clear(); selectedNotes.UnionWith(copies); Edited();
    }
    List<Note>? clipboard;
    void CopySelection() { if (selectedNotes.Count > 0) { int from = selectedNotes.Min(n => n.Step); clipboard = selectedNotes.Select(n => { var c = n.Copy(); c.Step -= from; return c; }).ToList(); Notice($"Copied {clipboard.Count} note(s). Ctrl+V pastes them at the green marker."); } }
    void Paste()
    {
        if (clipboard == null) return;
        Push(); var copies = clipboard.Select(n => { var c = n.Copy(); c.Step += startStep; return c; }).ToList();
        Layer.Notes.AddRange(copies); selectedNotes.Clear(); selectedNotes.UnionWith(copies); Edited();
    }

    // ---------------- Playback and recording ----------------
    /// <summary>Where the song is now (in steps, fractional) while playing; the start marker otherwise.</summary>
    double SongStep()
    {
        return player == null ? startStep : player.Step;
    }
    /// <summary>Where playback was paused (steps), so Play carries on from there; null when stopped.</summary>
    double? pausedAt;
    void PlayOrPause()
    {
        if (player != null && !recording) Pause();
        else if (player != null) StopPlayback();
        else Play(false);
    }
    void Pause()
    {
        double at = Math.Clamp(SongStep(), 0, Math.Max(0, song.TotalSteps - 1));
        StopPlayback();
        pausedAt = at; playButton.Content = "▶ Resume";
        playhead.X1 = playhead.X2 = at * colW; playhead.Visibility = Visibility.Visible;
    }
    void Play(bool record)
    {
        int from = !record && pausedAt is double paused ? (int)Math.Floor(paused) : startStep;
        StopPlayback();
        song.Check();
        if (record) { Push(); recording = true; recordButton.Content = "■ Stop recording"; recordButton.Foreground = Brushes.OrangeRed; }
        // Recording always gets one bar of clicks to count in, with or without the metronome.
        int countIn = record ? song.StepsPerBar : 0;
        player = audio.Add(new SongPlayer(Snapshot(record), from, countIn, loopFrom: startStep));
        playButton.Content = record ? "▶ Playing…" : "❚❚ Pause"; playhead.Visibility = Visibility.Visible; clock.Start();
        if (record) Notice("Recording after one bar of count-in… play keys along with the song.");
    }
    /// <summary>The song as it is right now, for the player: changes made while playing are picked up from this.</summary>
    SongPlayer.Snapshot Snapshot(bool record) => SongPlayer.Snapshot.Of(song, loopBox.IsChecked == true && !record, clickBox.IsChecked == true);
    void Tick()
    {
        if (player == null) return;
        double step = SongStep();
        double x = Math.Max(0, step) * colW; playhead.X1 = playhead.X2 = x;
        // Keep the playhead in view.
        if (x > rollScroll.HorizontalOffset + rollScroll.ViewportWidth - 60 || x < rollScroll.HorizontalOffset) rollScroll.ScrollToHorizontalOffset(Math.Max(0, x - 80));
        if (player.Finished) { StopPlayback(); return; }
        // Every 50 ms hand the player the song as it is now, so mute, solo, knobs, instruments and notes change live.
        if (++ticks % 2 == 0) player.Update(Snapshot(recording));
    }
    void StopPlayback()
    {
        if (recording)
        {
            double now = SongStep();
            foreach (var (_, held) in heldKeys) Record(held.Pitch, held.Start, now);
            foreach (var p in pendingMouse) Record(p.Pitch, p.Start, now);
            pendingMouse.Clear();
            recording = false; recordButton.Content = "● Record"; recordButton.ClearValue(ForegroundProperty);
            Notice("Recording stopped."); RebuildLayers();
        }
        player?.Stop(); player = null; clock.Stop(); pausedAt = null;
        playButton.Content = "▶ Play"; playhead.Visibility = Visibility.Collapsed;
    }
    /// <summary>A recorded key press becomes a note, snapped to the grid.</summary>
    void Record(int pitch, double startStepF, double endStepF)
    {
        if (!recording || startStepF < -0.5) return;
        int s = Math.Max(0, (int)Math.Round(startStepF)), e = Math.Max(s + 1, (int)Math.Round(endStepF));
        if (s >= song.TotalSteps) return;
        Layer.Notes.Add(new Note { Step = s, Length = e - s, Pitch = pitch, Velocity = velocity });
        dirty = true; roll.InvalidateVisual(); UpdateTitle();
    }

    // ---------------- Sheet music, MIDI, audio ----------------
    void OpenSheet()
    {
        StopPlayback();
        var result = SheetMusicEditor.Edit(this, song, layerIndex, audio);
        if (result != null) Change(() => { Layer.Notes = result.Value.Notes; Layer.Instrument = result.Value.Instrument; selectedNotes.Clear(); GrowToFit(); }, true);
    }
    async void ImportMidi()
    {
        var dialog = new OpenFileDialog { Filter = "MIDI files|*.mid;*.midi", Title = "Import MIDI" }; if (dialog.ShowDialog(this) != true) return;
        var imported = Midi.Read(System.IO.File.ReadAllBytes(dialog.FileName), song.StepsPerBeat, PixelEditor.SafeName(System.IO.Path.GetFileNameWithoutExtension(dialog.FileName)));
        // Options: the sound (matching instruments, as a bard MIDI names them, or chip voices), and replace or add.
        var window = new Window { Owner = this, Title = "Import MIDI — " + imported.Name, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SetResourceReference(StyleProperty, typeof(Window));
        var panel = new StackPanel { Margin = new Thickness(14), Width = 400 }; window.Content = panel;
        panel.Children.Add(new TextBlock { Text = $"{imported.Tracks.Count} layer(s): {string.Join(", ", imported.Tracks.Select(t => t.Name).Take(8))}{(imported.Tracks.Count > 8 ? "…" : "")}. {imported.Bpm:0.#} BPM, {imported.Numerator}/{imported.Denominator}, {imported.Bars} bars.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        var fonts = SoundFonts.Installed();
        var withFont = new RadioButton { Content = "Real instruments from a SoundFont (as the file names them):", IsChecked = fonts.Count > 0, IsEnabled = fonts.Count > 0 };
        var fontPick = new ComboBox { ItemsSource = fonts, SelectedItem = fonts.FirstOrDefault(f => f.Equals(song.SoundFont, StringComparison.OrdinalIgnoreCase)) ?? fonts.FirstOrDefault(), Margin = new Thickness(20, 4, 0, 6), IsEnabled = fonts.Count > 0 };
        panel.Children.Add(withFont); panel.Children.Add(fontPick);
        var matching = new RadioButton { Content = "Matching 8-bit instruments (Harp, Lute, Flute… as the file names them)", IsChecked = fonts.Count == 0 };
        var chip = new RadioButton { Content = "8-bit chip voices (square lead, pulse chords, triangle bass, drums)", Margin = new Thickness(0, 4, 0, 0) };
        var splitParts = new CheckBox { Content = "Split solo parts into melody, chords and bass", IsChecked = true, IsEnabled = false, Margin = new Thickness(20, 4, 0, 0), ToolTip = "A part that plays melody and chords at once (like a bard's harp arrangement) becomes a lead, a chord layer and a bass layer, each with its own chip voice." };
        chip.Checked += (_, _) => splitParts.IsEnabled = true; matching.Checked += (_, _) => splitParts.IsEnabled = false;
        panel.Children.Add(matching); panel.Children.Add(chip); panel.Children.Add(splitParts);
        bool hasNotes = song.Tracks.Any(t => t.Notes.Count > 0);
        var replaceIt = new RadioButton { Content = "Replace this song", IsChecked = true, Margin = new Thickness(0, 10, 0, 0) };
        var addIt = new RadioButton { Content = "Add its layers to this song (keeping this song's tempo)", Margin = new Thickness(0, 4, 0, 0) };
        if (hasNotes) { panel.Children.Add(replaceIt); panel.Children.Add(addIt); }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) }; panel.Children.Add(buttons);
        bool ok = false; var go = new Button { Content = "Import", IsDefault = true, Padding = new Thickness(10, 2, 10, 2) }; go.Click += (_, _) => { ok = true; window.Close(); };
        buttons.Children.Add(go); buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2) });
        window.ShowDialog(); if (!ok) return;
        if (chip.IsChecked == true) Midi.Chipify(imported, splitParts.IsChecked == true);
        imported.SoundFont = withFont.IsChecked == true && fontPick.SelectedItem is string useFont ? useFont : "";
        if (imported.SoundFont.Length > 0) await Task.Run(() => SoundFonts.Load(imported.SoundFont));
        bool replacing = !hasNotes || replaceIt.IsChecked == true;
        Change(() =>
        {
            if (replacing) { imported.Name = song.Name; song = imported; layerIndex = 0; }
            else { if (song.Tracks.Count == 1 && song.Tracks[0].Notes.Count == 0) song.Tracks.Clear(); foreach (var t in imported.Tracks) if (song.Tracks.Count < 32) song.Tracks.Add(t); layerIndex = song.Tracks.Count - 1; song.Bars = Math.Max(song.Bars, Math.Min(512, song.BarsNeeded())); }
        }, true);
        SyncControls(); Relayout();
    }
    void ExportAudio()
    {
        StopPlayback(); song.Check();
        var dialog = new SaveFileDialog { Filter = "MP3|*.mp3|WAV|*.wav|Ogg Vorbis|*.ogg", FileName = song.Name + ".mp3", Title = "Export the song as audio" }; if (dialog.ShowDialog(this) != true) return;
        var (l, r) = SongRenderer.Render(song);
        AudioExport.Save(dialog.FileName, [l, r], SongRenderer.SampleRate);
        Notice("Exported " + System.IO.Path.GetFileName(dialog.FileName) + ".");
    }
    void ExportMidi()
    {
        var dialog = new SaveFileDialog { Filter = "MIDI file|*.mid", FileName = song.Name + ".mid" }; if (dialog.ShowDialog(this) != true) return;
        System.IO.File.WriteAllBytes(dialog.FileName, Midi.Write(song)); Notice("Exported " + System.IO.Path.GetFileName(dialog.FileName) + ".");
    }
    /// <summary>A recording becomes notes: either the whole song as 8-bit layers (lead, chords, bass, drums, found by the
    /// note detector) or just its melody as one layer.</summary>
    internal async void ImportAudio((float[][] Channels, int Rate, string Name)? given = null)
    {
        StopPlayback();
        if ((given ?? pickAudio()) is not var (channels, rate, name)) return;
        await StemSetup.Offer(this); // the first time: offer the instrument splitter's one-time download
        bool hasNotes = song.Tracks.Any(t => t.Notes.Count > 0);
        var options = new Window { Owner = this, Title = "Song from audio — " + name, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        options.SetResourceReference(StyleProperty, typeof(Window));
        var panel = new StackPanel { Margin = new Thickness(14), Width = 430 }; options.Content = panel;
        panel.Children.Add(new TextBlock { Text = $"{name}: {channels[0].Length / (double)rate:0.0} s, {(channels.Length > 1 ? "stereo" : "mono")}.", Margin = new Thickness(0, 0, 0, 8) });
        var whole = new RadioButton { Content = "Whole song → 8-bit layers (lead, chords, bass and drums)", IsChecked = true, FontWeight = FontWeights.SemiBold };
        var melody = new RadioButton { Content = "Melody only (one line, for a voice, whistle or solo instrument)", Margin = new Thickness(0, 6, 0, 0) };
        // The mode labels in the window's own text colour (they otherwise came out dark on the dark window).
        foreach (var mode in new[] { whole, melody }) mode.SetBinding(ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = options });
        panel.Children.Add(whole);
        var wholeBox = new StackPanel { Margin = new Thickness(20, 4, 0, 0) }; panel.Children.Add(wholeBox);
        wholeBox.Children.Add(new TextBlock { Text = "The beat is followed through the song, the chord on every beat is named, the singer becomes the melody (a note for every sung syllable) and the drums come from their hits.", TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });
        var style = new ComboBox { ItemsSource = new[] { "Band arrangement: chords, bass, melody, drums (best for songs)", "Every note heard (for piano or solo pieces)" }, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 4) };
        wholeBox.Children.Add(style);
        var arrangeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var rhythm = new ComboBox { ItemsSource = new[] { "Chords on eighth notes", "Chords on quarter notes", "Chords held" }, SelectedIndex = 0, Width = 170 };
        var voicing = new ComboBox { ItemsSource = new[] { "Chord shapes: automatic", "Power chords", "Full chords (triads)" }, SelectedIndex = 0, Width = 170, Margin = new Thickness(6, 0, 0, 0) };
        arrangeRow.Children.Add(rhythm); arrangeRow.Children.Add(voicing); wholeBox.Children.Add(arrangeRow);
        style.SelectionChanged += (_, _) => arrangeRow.IsEnabled = style.SelectedIndex == 0;
        var parts = new WrapPanel(); wholeBox.Children.Add(parts);
        CheckBox Part(string text) { var c = new CheckBox { Content = text, IsChecked = true, Margin = new Thickness(0, 0, 12, 0) }; parts.Children.Add(c); return c; }
        var lead = Part("Lead"); var chords = Part("Chords"); var bass = Part("Bass"); var drums = Part("Drums");
        var split = new CheckBox { Content = "Split into vocals, bass, drums and other first (best)", IsChecked = StemSplitter.Available, IsEnabled = StemSplitter.Available, Margin = new Thickness(0, 6, 0, 0),
            ToolTip = StemSplitter.Available ? "Demucs separates the parts, then each is turned into notes on its own: the singing becomes the melody, the bass guitar the bass line. Takes a minute or two." : "Needs the one-time Demucs download." };
        var splitRow = new StackPanel { Orientation = Orientation.Horizontal }; splitRow.Children.Add(split);
        if (!StemSplitter.Available)
        {
            var get = new Button { Content = "Download…", Margin = new Thickness(8, 6, 0, 0), Padding = new Thickness(8, 0, 8, 0) };
            get.Click += async (_, _) => { if (await StemSetup.ShowDownload(this)) { split.IsEnabled = true; split.IsChecked = true; split.ToolTip = null; get.Visibility = Visibility.Collapsed; } };
            splitRow.Children.Add(get);
        }
        panel.Children.Insert(1, splitRow);
        var vocals = new CheckBox { Content = "Remove the vocals (the lead is then an instrument, not the singing)", Margin = new Thickness(0, 6, 0, 0), IsEnabled = channels.Length > 1, ToolTip = "Takes out what is mixed in the middle, where singers usually are. Works on many studio mixes; bass and drums still come from the full sound." };
        wholeBox.Children.Add(vocals);
        var tempoRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        tempoRow.Children.Add(new TextBlock { Text = "Tempo", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        var tempo = new TextBox { Width = 60, Text = hasNotes ? song.Bpm.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : "", ToolTip = "Leave empty to detect it" };
        var detect = new Button { Content = "Detect", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 0, 8, 0), ToolTip = "Work out the tempo now, so you can check it (halve or double it if it's counted differently)" };
        detect.Click += async (_, _) =>
        {
            detect.IsEnabled = false; detect.Content = "Listening…";
            try
            {
                var mono22 = AudioTools.Resample(AudioTools.Mono(channels), rate, SongAnalysis.Rate);
                double found = await Task.Run(() => { var env = SongAnalysis.OnsetEnvelope(mono22, out double sec); var b = SongAnalysis.TrackBeats(env, sec, SongAnalysis.Tempo(env, sec)); return b.Count > 1 ? 60 / ((b[^1] - b[0]) / (b.Count - 1)) : 120; });
                tempo.Text = Math.Round(found).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            finally { detect.IsEnabled = true; detect.Content = "Detect"; }
        };
        tempoRow.Children.Add(tempo); tempoRow.Children.Add(detect); tempoRow.Children.Add(new TextBlock { Text = "  BPM (empty = detect it)", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75 });
        wholeBox.Children.Add(tempoRow);
        var replace = new CheckBox { Content = "Replace this song (otherwise its layers are added)", IsChecked = !hasNotes, Margin = new Thickness(0, 6, 0, 0), Visibility = hasNotes ? Visibility.Visible : Visibility.Collapsed };
        wholeBox.Children.Add(replace);
        panel.Children.Add(melody);
        var melodyBox = new StackPanel { Margin = new Thickness(20, 4, 0, 0), IsEnabled = false }; panel.Children.Add(melodyBox);
        melodyBox.Children.Add(new TextBlock { Text = "The singer or lead line, taken from the middle of the mix and added as one layer at this song's tempo.", Opacity = 0.75, FontSize = 11, TextWrapping = TextWrapping.Wrap });
        var ins = new ComboBox { ItemsSource = Instruments.Presets.Select(p => p.Name).ToArray(), SelectedItem = "Square lead", Margin = new Thickness(0, 4, 0, 0) }; melodyBox.Children.Add(ins);
        var minLen = new ComboBox { ItemsSource = new[] { "Keep every note", "Skip notes shorter than 2 steps", "Skip notes shorter than 4 steps" }, SelectedIndex = 1, Margin = new Thickness(0, 4, 0, 0) }; melodyBox.Children.Add(minLen);
        whole.Checked += (_, _) => { wholeBox.IsEnabled = true; melodyBox.IsEnabled = false; }; melody.Checked += (_, _) => { wholeBox.IsEnabled = false; melodyBox.IsEnabled = true; };
        // Set apart from the two modes above: it applies to both.
        panel.Children.Add(new Separator { Margin = new Thickness(0, 14, 0, 10) });
        panel.Children.Add(new TextBlock { Text = "Sensitivity (both modes)", FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Higher catches quieter notes, and more stray ones.", Opacity = 0.75, FontSize = 11, Margin = new Thickness(0, 0, 0, 2) });
        var sensitivity = new Slider { Minimum = 0, Maximum = 1, Value = 0.5 }; panel.Children.Add(sensitivity);
        // Sung notes and the sounds, side by side.
        var choices = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        choices.ColumnDefinitions.Add(new ColumnDefinition()); choices.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); choices.ColumnDefinitions.Add(new ColumnDefinition());
        StackPanel Choice(string label, int column) { var box = new StackPanel(); box.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) }); Grid.SetColumn(box, column); choices.Children.Add(box); return box; }
        var sung = new ComboBox { ItemsSource = new[] { "One note per syllable", "A new note at every pitch change" }, SelectedIndex = 0, ToolTip = "How the singing becomes melody notes (with the instrument split): one note for each sung syllable, pitched on its vowel; or a note wherever the pitch moves or the voice dips." };
        Choice("Sung notes", 0).Children.Add(sung);
        var fontChoices = new List<string> { ChipSounds }; fontChoices.AddRange(SoundFonts.Installed());
        var sounds = new ComboBox { ItemsSource = fontChoices, SelectedItem = fontChoices.FirstOrDefault(f => f.Equals(song.SoundFont, StringComparison.OrdinalIgnoreCase)) ?? ChipSounds, ToolTip = "Play the result with the built-in chip sounds or a SoundFont (Get more soundfonts… in the Sounds dropdown adds others)." };
        Choice("Sounds", 2).Children.Add(sounds);
        panel.Children.Add(choices);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) }; panel.Children.Add(buttons);
        bool ok = false; var go = new Button { Content = "Convert", IsDefault = true, Padding = new Thickness(10, 2, 10, 2) }; go.Click += (_, _) => { ok = true; options.Close(); };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2) }; buttons.Children.Add(go); buttons.Children.Add(cancel);
        options.ShowDialog(); if (!ok) return;
        double bpm = 0;
        if (tempo.Text.Trim().Length > 0 && (!double.TryParse(tempo.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out bpm) || bpm is < 20 or > 400)) { MessageBox.Show(this, "Tempo is 20–400 BPM, or empty to detect it.", "Music maker"); return; }
        double sens = sensitivity.Value; bool wholeSong = whole.IsChecked == true, replacing = replace.IsChecked == true || !hasNotes;
        var convertOptions = new SongConverter.Options(bpm, song.StepsPerBeat, lead.IsChecked == true, chords.IsChecked == true, bass.IsChecked == true, drums.IsChecked == true, sens, vocals.IsChecked == true,
            Style: style.SelectedIndex == 0 ? "arrange" : "notes", ChordRhythm: new[] { "eighths", "quarters", "held" }[rhythm.SelectedIndex], ChordVoicing: new[] { "auto", "power", "triads" }[voicing.SelectedIndex],
            SungNotes: sung.SelectedIndex == 0 ? "syllables" : "pitch");
        string sungNotes = convertOptions.SungNotes, useFont = sounds.SelectedItem as string is { } f && f != ChipSounds ? f : "";
        if (useFont.Length > 0) await Task.Run(() => SoundFonts.Load(useFont));
        bool splitting = split.IsChecked == true && StemSplitter.Available;
        string instrument = (string)ins.SelectedItem; int minSteps = new[] { 1, 2, 4 }[minLen.SelectedIndex]; double songBpm = song.Bpm; int spb = song.StepsPerBeat;
        IsEnabled = false; Cursor = Cursors.Wait;
        try
        {
            var stems = splitting ? await StemSetup.Split(channels, rate, name, Notice) : null;
            if (wholeSong)
            {
                var converted = await Task.Run(() => SongConverter.Convert(channels, rate, song.Name, convertOptions, text => Dispatcher.BeginInvoke(() => Notice(text)), stems));
                Change(() =>
                {
                    if (replacing) { converted.Name = song.Name; song = converted; layerIndex = 0; song.SoundFont = useFont; }
                    else
                    {
                        song.SoundFont = useFont;
                        if (song.Tracks.Count == 1 && song.Tracks[0].Notes.Count == 0) song.Tracks.Clear();
                        // Onto this song's grid, if the conversion used a finer one.
                        double k = song.StepsPerBeat / (double)converted.StepsPerBeat;
                        if (k != 1) foreach (var t in converted.Tracks) foreach (var n in t.Notes) { int end = (int)Math.Round((n.Step + n.Length) * k); n.Step = (int)Math.Round(n.Step * k); n.Length = Math.Max(1, end - n.Step); }
                        foreach (var t in converted.Tracks) if (song.Tracks.Count < 32) song.Tracks.Add(t);
                        layerIndex = song.Tracks.Count - 1; song.Bars = Math.Max(song.Bars, Math.Min(512, song.BarsNeeded()));
                    }
                    emptyStarter = false;
                }, true);
                Notice($"Converted {name}: {converted.Bpm:0} BPM, {converted.Key} {converted.Scale}, " + string.Join(", ", converted.Tracks.Select(t => $"{t.Name} {t.Notes.Count} notes")) + ". Tidy any stray notes in the piano roll.");
            }
            else
            {
                var melodyOptions = new SongConverter.Options(songBpm, spb, Lead: true, Chords: false, Bass: false, Drums: false, Sensitivity: sens, SungNotes: sungNotes);
                var found = await Task.Run(() => SongConverter.Convert(channels, rate, "melody", melodyOptions, text => Dispatcher.BeginInvoke(() => Notice(text)), stems));
                var notes = (found.Tracks.FirstOrDefault()?.Notes ?? []).Where(n => n.Length >= minSteps).ToList();
                if (notes.Count == 0) throw new InvalidOperationException("No clear melody was found. Try a higher sensitivity, or a recording of a single instrument or voice.");
                Change(() => { song.SoundFont = useFont; if (emptyStarter && song.Tracks.Count == 1 && song.Tracks[0].Notes.Count == 0) song.Tracks.Clear(); emptyStarter = false; song.Tracks.Add(new Track { Name = "Melody from " + name, Instrument = Instruments.Get(instrument), Notes = notes }); layerIndex = song.Tracks.Count - 1; song.Bars = Math.Max(song.Bars, Math.Min(512, song.BarsNeeded())); }, true);
                Notice($"Wrote {notes.Count} notes from {name}.");
            }
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Music maker", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { IsEnabled = true; Cursor = null; }
    }

    // ---------------- Saving, undo ----------------
    void Save(bool asNew)
    {
        StopPlayback(); song.Name = PixelEditor.SafeName(nameBox.Text);
        song.Check();
        var (l, r) = SongRenderer.Render(song);
        if (l.Length == 0 || AudioTools.Peak(l) + AudioTools.Peak(r) < 1e-4) throw new InvalidOperationException("The song is silent: add some notes (or unmute a layer) first.");
        path = save(new SaveRequest(Json.Clone(song), (string)formatBox.SelectedItem, asNew, path, l, r));
        dirty = false; UpdateTitle(); Notice("Saved to Assets as " + System.IO.Path.GetFileName(path) + ".");
    }
    bool ConfirmDiscard(string doing)
    {
        StopPlayback();
        if (!dirty || NoPrompts) return true;
        var answer = MessageBox.Show(this, $"Save your song to the project before {doing}?", "Music maker", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel) return false;
        if (answer == MessageBoxResult.Yes) { try { Save(false); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Music maker"); return false; } }
        return true;
    }
    void Change(Action change, bool rebuildLayers = false)
    {
        Push(); change(); dirty = true;
        if (rebuildLayers) RebuildLayers();
        SyncControls(); Relayout();
    }
    void Undo() { if (undo.Count == 0) return; redo.Add(Json.Write(song)); Restore(undo[^1]); undo.RemoveAt(undo.Count - 1); }
    void Redo() { if (redo.Count == 0) return; undo.Add(Json.Write(song)); Restore(redo[^1]); redo.RemoveAt(redo.Count - 1); }
    void Restore(string json) { song = Json.Read<Song>(json); layerIndex = Math.Clamp(layerIndex, 0, song.Tracks.Count - 1); selectedNotes.Clear(); dirty = true; RebuildLayers(); SyncControls(); Relayout(); }

    void UpdateTitle() => Title = "Music maker — " + (path != null ? System.IO.Path.GetFileName(path) : song.Name + " (not saved yet)") + (dirty ? " •" : "") + $"  ({song.Tracks.Count} layer{(song.Tracks.Count == 1 ? "" : "s")}, {song.Bars} bars, {song.Seconds:0.0} s)";
    string notice = "";
    void Notice(string text) { notice = text; UpdateStatus(); }
    internal void UpdateStatus()
    {
        int bar = startStep / song.StepsPerBar + 1, beat = startStep % song.StepsPerBar / Math.Max(1, song.StepsPerBar / song.Numerator) + 1;
        status.Text = $"Layer: {Layer.Name} ({Layer.Instrument.Name})  •  start at bar {bar} beat {beat}  •  {selectedNotes.Count} selected  •  click to add a note, drag to move, drag its end to resize, right-click to delete, Shift+drag to select" + (notice.Length > 0 ? "  •  " + notice : "");
    }
    internal void Guard(Action action) { try { action(); } catch (OperationCanceledException) { } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Music maker", MessageBoxButton.OK, MessageBoxImage.Warning); } }
}

/// <summary>The piano roll: time across, pitch up; the selected layer's notes are solid, the others faint.</summary>
sealed class RollView : FrameworkElement
{
    readonly MusicMaker m;
    enum Drag { None, Move, Resize, Select, Paint }
    Drag drag; Point downAt; int downStep, downPitch; Note? grabbed; Dictionary<Note, (int Step, int Pitch, int Length)>? starts; int lastPreview = -1; Rect band;
    public RollView(MusicMaker maker) { m = maker; Focusable = true; FocusVisualStyle = null; ClipToBounds = true; }
    static readonly Brush White = new SolidColorBrush(Color.FromRgb(46, 50, 60)), Black = new SolidColorBrush(Color.FromRgb(38, 41, 50)), InKey = new SolidColorBrush(Color.FromArgb(22, 120, 200, 255));
    static readonly Pen Bar = new(new SolidColorBrush(Color.FromRgb(120, 128, 145)), 1), Beat = new(new SolidColorBrush(Color.FromRgb(78, 84, 98)), 1), Step = new(new SolidColorBrush(Color.FromRgb(56, 61, 72)), 1), Octave = new(new SolidColorBrush(Color.FromRgb(90, 96, 110)), 1);
    int PitchAt(double y) => Math.Clamp(MusicMaker.High - (int)Math.Floor(y / MusicMaker.RowH), MusicMaker.Low, MusicMaker.High);
    int StepAt(double x) => Math.Max(0, (int)Math.Floor(x / m.colW));
    Rect NoteRect(Note n) => new(n.Step * m.colW, (MusicMaker.High - n.Pitch) * MusicMaker.RowH, Math.Max(3, n.Length * m.colW - 1), MusicMaker.RowH - 1);
    protected override void OnRender(DrawingContext dc)
    {
        var song = m.song; double w = ActualWidth, rowH = MusicMaker.RowH; int steps = song.TotalSteps;
        dc.DrawRectangle(White, null, new Rect(0, 0, w, ActualHeight));
        for (int p = MusicMaker.Low; p <= MusicMaker.High; p++)
        {
            double y = (MusicMaker.High - p) * rowH;
            if (new[] { 1, 3, 6, 8, 10 }.Contains(p % 12)) dc.DrawRectangle(Black, null, new Rect(0, y, steps * m.colW, rowH));
            if (song.Scale != "chromatic" && MusicTheory.InScale(p, song.Key, song.Scale)) dc.DrawRectangle(InKey, null, new Rect(0, y, steps * m.colW, rowH));
            if (p % 12 == 0) dc.DrawLine(Octave, new Point(0, y + rowH), new Point(steps * m.colW, y + rowH));
        }
        int perBeat = Math.Max(1, song.StepsPerBar / song.Numerator);
        for (int s = 0; s <= steps; s++)
        {
            double x = s * m.colW + 0.5;
            var pen = s % song.StepsPerBar == 0 ? Bar : s % perBeat == 0 ? Beat : m.colW >= 8 ? Step : null;
            if (pen != null) dc.DrawLine(pen, new Point(x, 0), new Point(x, ActualHeight));
        }
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(160, 20, 22, 28)), null, new Rect(steps * m.colW, 0, Math.Max(0, w - steps * m.colW), ActualHeight));
        // Other layers faintly, then the selected one.
        for (int i = 0; i < song.Tracks.Count; i++)
        {
            if (i == m.layerIndex) continue;
            var c = MusicMaker.ColorOf(i); var brush = new SolidColorBrush(Color.FromArgb(song.Tracks[i].Mute ? (byte)25 : (byte)60, c.R, c.G, c.B));
            foreach (var n in song.Tracks[i].Notes) dc.DrawRectangle(brush, null, NoteRect(n));
        }
        var color = MusicMaker.ColorOf(m.layerIndex); var outline = new Pen(new SolidColorBrush(Color.FromRgb((byte)(color.R / 2), (byte)(color.G / 2), (byte)(color.B / 2))), 1); var chosen = new Pen(Brushes.White, 2);
        foreach (var n in m.Layer.Notes)
        {
            byte a = (byte)(110 + 145 * Math.Clamp(n.Velocity, 0, 1));
            var r = NoteRect(n); dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(a, color.R, color.G, color.B)), m.selectedNotes.Contains(n) ? chosen : outline, r);
            if (r.Width > 26 && m.Layer.Instrument.Wave != "drums") { var t = new FormattedText(MusicTheory.Name(n.Pitch), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 8.5, Brushes.Black, 1); dc.DrawText(t, new Point(r.X + 2, r.Y)); }
        }
        if (drag == Drag.Select) dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 80, 170, 255)), new Pen(Brushes.DeepSkyBlue, 1), band);
    }
    Note? NoteAt(Point p) => m.Layer.Notes.LastOrDefault(n => NoteRect(n).Contains(p));
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus(); var p = e.GetPosition(this); downAt = p; downStep = StepAt(p.X); downPitch = PitchAt(p.Y);
        if (downStep >= m.song.TotalSteps) return;
        var hit = NoteAt(p);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && hit == null) { drag = Drag.Select; band = new Rect(p, p); CaptureMouse(); return; }
        if (hit != null)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { if (!m.selectedNotes.Remove(hit)) m.selectedNotes.Add(hit); InvalidateVisual(); m.UpdateStatus(); return; }
            if (!m.selectedNotes.Contains(hit)) { m.selectedNotes.Clear(); m.selectedNotes.Add(hit); }
            m.Push(); grabbed = hit; drag = p.X > NoteRect(hit).Right - 6 ? Drag.Resize : Drag.Move;
            starts = m.selectedNotes.ToDictionary(n => n, n => (n.Step, n.Pitch, n.Length)); lastPreview = hit.Pitch; m.Preview(hit.Pitch, 0.15);
            CaptureMouse(); InvalidateVisual(); m.UpdateStatus(); return;
        }
        // Empty: a new note of the chosen length; dragging right makes it longer.
        m.AddNote(downStep, downPitch, m.noteLength); grabbed = m.Layer.Notes[^1]; drag = Drag.Paint; CaptureMouse();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (drag == Drag.None) { var hit = NoteAt(p); Cursor = hit != null && p.X > NoteRect(hit).Right - 6 ? Cursors.SizeWE : hit != null ? Cursors.SizeAll : Cursors.Arrow; return; }
        int ds = StepAt(p.X) - downStep, dp = PitchAt(p.Y) - downPitch;
        switch (drag)
        {
            case Drag.Move:
                foreach (var (n, s) in starts!) { n.Step = Math.Max(0, s.Step + ds); n.Pitch = Math.Clamp(s.Pitch + dp, 0, 127); }
                if (grabbed!.Pitch != lastPreview) { lastPreview = grabbed.Pitch; m.Preview(grabbed.Pitch, 0.12); }
                break;
            case Drag.Resize:
                foreach (var (n, s) in starts!) n.Length = Math.Max(1, s.Length + ds);
                break;
            case Drag.Paint:
                grabbed!.Length = Math.Max(m.noteLength, StepAt(p.X) - grabbed.Step + 1);
                break;
            case Drag.Select:
                band = new Rect(downAt, p);
                m.selectedNotes.Clear(); foreach (var n in m.Layer.Notes) if (band.IntersectsWith(NoteRect(n))) m.selectedNotes.Add(n);
                break;
        }
        InvalidateVisual();
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (drag == Drag.None) return;
        drag = Drag.None; grabbed = null; starts = null; ReleaseMouseCapture(); m.Edited();
    }
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        Focus(); var hit = NoteAt(e.GetPosition(this)); if (hit == null) return;
        m.Push(); m.Layer.Notes.Remove(hit); m.selectedNotes.Remove(hit); m.Edited(); e.Handled = true;
    }
}

/// <summary>An on-screen piano, five octaves from the keyboard octave down one. Scale notes have a dot.</summary>
sealed class PianoStrip : FrameworkElement
{
    readonly MusicMaker m; int? down;
    public PianoStrip(MusicMaker maker) { m = maker; ClipToBounds = true; }
    int Lowest => (m.KeyboardOctave) * 12; // C one octave below the keyboard's
    const int Octaves = 5;
    static bool IsBlack(int p) => new[] { 1, 3, 6, 8, 10 }.Contains(((p % 12) + 12) % 12);
    List<(int Pitch, Rect Rect, bool Black)> Keys()
    {
        var keys = new List<(int, Rect, bool)>(); int whites = Octaves * 7 + 1; double ww = ActualWidth / whites, h = ActualHeight; int wi = 0;
        for (int p = Lowest; p <= Lowest + Octaves * 12; p++) if (!IsBlack(p)) keys.Add((p, new Rect(wi++ * ww, 0, ww, h), false));
        wi = 0;
        for (int p = Lowest; p <= Lowest + Octaves * 12; p++) { if (!IsBlack(p)) { wi++; continue; } keys.Add((p, new Rect(wi * ww - ww * 0.32, 0, ww * 0.64, h * 0.62), true)); }
        return keys;
    }
    protected override void OnRender(DrawingContext dc)
    {
        var sounding = m.Sounding; var song = m.song;
        foreach (var (p, r, black) in Keys())
        {
            bool on = sounding.Contains(p);
            var fill = on ? new SolidColorBrush(MusicMaker.ColorOf(m.layerIndex)) : black ? Brushes.Black : Brushes.WhiteSmoke;
            dc.DrawRectangle(fill, new Pen(Brushes.Gray, 1), r);
            if (MusicTheory.InScale(p, song.Key, song.Scale) && song.Scale != "chromatic") dc.DrawEllipse(black ? Brushes.LightSkyBlue : Brushes.SteelBlue, null, new Point(r.X + r.Width / 2, r.Bottom - 7), 2.5, 2.5);
            if (!black && p % 12 == 0) dc.DrawText(new FormattedText("C" + (p / 12 - 1), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9, Brushes.DimGray, 1), new Point(r.X + 2, r.Bottom - 22));
        }
    }
    int? PitchAt(Point pt)
    {
        var keys = Keys();
        foreach (var k in keys.Where(k => k.Black)) if (k.Rect.Contains(pt)) return k.Pitch;
        foreach (var k in keys.Where(k => !k.Black)) if (k.Rect.Contains(pt)) return k.Pitch;
        return null;
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { if (PitchAt(e.GetPosition(this)) is int p) { down = p; m.PianoDown(p); CaptureMouse(); } }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (down == null || e.LeftButton != MouseButtonState.Pressed) return;
        if (PitchAt(e.GetPosition(this)) is int p && p != down) { m.PianoUp(down.Value); down = p; m.PianoDown(p); }
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { if (down is int p) m.PianoUp(p); down = null; ReleaseMouseCapture(); }
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo) { base.OnRenderSizeChanged(sizeInfo); InvalidateVisual(); }
}
