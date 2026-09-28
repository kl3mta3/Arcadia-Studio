using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Wysicraft.Core.Audio;
using Track = Wysicraft.Core.Audio.Track;
using Wysicraft.Models;
namespace Wysicraft.Designer;

/// <summary>One layer as sheet music: a staff with clef, key and time signature, notes with stems and flags, rests in
/// the gaps. Pick a note value in the toolbox and click the staff to place it; the same notes as the piano roll, so a
/// layer played in on the keyboard can be tidied up here and the other way round.</summary>
sealed class SheetMusicEditor : Window
{
    readonly Song song; readonly AudioOut audio; readonly int layer;
    internal List<Note> notes; internal Instrument instrument;
    internal double quarters = 1; internal bool dotted, restMode; internal int accidental; // 0 = as the key says, 2 = natural, 1 = sharp, -1 = flat
    internal string clef;
    internal readonly HashSet<Note> selected = [];
    readonly StaffView staff; readonly TextBlock status = new() { Margin = new Thickness(8, 4, 8, 4) };
    readonly List<string> undo = [];
    BufferPlayer? player; readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromMilliseconds(30) };
    internal double playStep = -1;
    bool accepted;
    internal Song Song => song;
    internal int ValueSteps => Math.Max(1, (int)Math.Round(quarters * song.StepsPerBeat * (dotted ? 1.5 : 1)));

    internal static Window Open(Window owner, Song song, int layer, AudioOut audio) { var editor = new SheetMusicEditor(owner, song, layer, audio); editor.Show(); return editor; }
    public static (List<Note> Notes, Instrument Instrument)? Edit(Window owner, Song song, int layer, AudioOut audio)
    {
        var editor = new SheetMusicEditor(owner, song, layer, audio);
        editor.ShowDialog();
        return editor.accepted ? (editor.notes, editor.instrument) : null;
    }
    SheetMusicEditor(Window owner, Song song, int layer, AudioOut audio)
    {
        Owner = owner; this.song = song; this.audio = audio; this.layer = layer;
        var track = song.Tracks[layer];
        notes = track.Notes.Select(n => n.Copy()).ToList(); instrument = track.Instrument.Copy();
        clef = notes.Count > 0 && notes.Average(n => n.Pitch) < 57 ? "bass" : "treble";
        SetResourceReference(StyleProperty, typeof(Window));
        Title = "Sheet music — " + track.Name; Width = 1200; Height = 520; MinWidth = 800; MinHeight = 400; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        staff = new StaffView(this);
        var root = new DockPanel(); Content = root;
        var bar = BuildToolbar(); DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8) };
        var ok = new Button { Content = "Use these notes", IsDefault = false, Padding = new Thickness(10, 2, 10, 2) }; ok.Click += (_, _) => { accepted = true; Close(); };
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2) }; cancel.Click += (_, _) => Close();
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        var bottom = new DockPanel(); DockPanel.SetDock(bottom, Dock.Bottom); bottom.Children.Add(Docked(buttons, Dock.Right)); bottom.Children.Add(status); root.Children.Add(bottom);
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Visible, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = staff, Background = new SolidColorBrush(Color.FromRgb(246, 243, 234)) };
        root.Children.Add(scroll);
        clock.Tick += (_, _) => { if (player == null) return; playStep = (player.Seconds - AudioOut.Latency / 1000.0) / song.StepSeconds; staff.InvalidateVisual(); if (player.Finished) StopPlay(); };
        Closed += (_, _) => StopPlay();
        PreviewKeyDown += Keys;
        staff.Refresh(); UpdateStatus();
    }
    static T Docked<T>(T e, Dock d) where T : UIElement { DockPanel.SetDock(e, d); return e; }
    UIElement BuildToolbar()
    {
        var panel = new WrapPanel { Margin = new Thickness(6) };
        var group = new List<ToggleButton>();
        foreach (var (symbol, name, q) in MusicTheory.Values)
        {
            if (q * song.StepsPerBeat < 1 - 1e-9) continue;
            var b = new ToggleButton { Content = Glyph(q), ToolTip = name + " note", FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 18, Width = 38, Height = 34, Margin = new Thickness(0, 0, 3, 0), IsChecked = q == 1 };
            b.Click += (_, _) => { quarters = q; foreach (var o in group) o.IsChecked = o == b; UpdateStatus(); };
            group.Add(b); panel.Children.Add(b);
        }
        var dot = new ToggleButton { Content = "•  dotted", Margin = new Thickness(6, 0, 3, 0), Padding = new Thickness(6, 0, 6, 0), ToolTip = "Half as long again" }; dot.Click += (_, _) => { dotted = dot.IsChecked == true; UpdateStatus(); }; panel.Children.Add(dot);
        var rest = new ToggleButton { Content = "𝄽 rest", FontFamily = new FontFamily("Segoe UI Symbol"), Margin = new Thickness(3, 0, 10, 0), Padding = new Thickness(6, 0, 6, 0), ToolTip = "Click to insert a rest there (later notes move along)" };
        rest.Click += (_, _) => { restMode = rest.IsChecked == true; UpdateStatus(); }; panel.Children.Add(rest);
        var accs = new List<ToggleButton>();
        foreach (var (text, value, tip) in new[] { ("♮", 2, "Natural"), ("♯", 1, "Sharp"), ("♭", -1, "Flat") })
        {
            var b = new ToggleButton { Content = text, Width = 30, FontSize = 16, Margin = new Thickness(0, 0, 3, 0), ToolTip = tip + " (otherwise notes follow the key signature)" };
            b.Click += (_, _) => { accidental = b.IsChecked == true ? value : 0; foreach (var o in accs) if (o != b) o.IsChecked = false; UpdateStatus(); };
            accs.Add(b); panel.Children.Add(b);
        }
        panel.Children.Add(new TextBlock { Text = "Clef", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) });
        var clefBox = new ComboBox { ItemsSource = new[] { "treble", "bass" }, SelectedItem = clef, Width = 80 }; clefBox.SelectionChanged += (_, _) => { clef = (string)clefBox.SelectedItem; staff.Refresh(); }; panel.Children.Add(clefBox);
        panel.Children.Add(new TextBlock { Text = "Instrument", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) });
        var ins = new ComboBox { ItemsSource = Instruments.Presets.Select(p => p.Name).Append("(custom)").ToArray(), Width = 130 };
        ins.SelectedItem = Instruments.Presets.Any(p => p.Name == instrument.Name) ? instrument.Name : "(custom)";
        ins.SelectionChanged += (_, _) => { if (ins.SelectedItem is string n && n != "(custom)") { instrument = Instruments.Get(n); Hear(60); } };
        panel.Children.Add(ins);
        var edit = new Button { Content = "Edit…", Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(6, 0, 6, 0) }; edit.Click += (_, _) => { if (InstrumentEditor.Edit(this, instrument, audio) is Instrument changed) { instrument = changed; ins.SelectedItem = "(custom)"; } }; panel.Children.Add(edit);
        var play = new Button { Content = "▶ Play", Margin = new Thickness(12, 0, 3, 0), Padding = new Thickness(8, 0, 8, 0) }; play.Click += (_, _) => Play(false); panel.Children.Add(play);
        var all = new Button { Content = "▶ With other layers", Margin = new Thickness(0, 0, 3, 0), Padding = new Thickness(8, 0, 8, 0) }; all.Click += (_, _) => Play(true); panel.Children.Add(all);
        var stop = new Button { Content = "■", Padding = new Thickness(8, 0, 8, 0) }; stop.Click += (_, _) => StopPlay(); panel.Children.Add(stop);
        var undoButton = new Button { Content = "Undo", Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(8, 0, 8, 0) }; undoButton.Click += (_, _) => Undo(); panel.Children.Add(undoButton);
        return panel;
    }
    internal static string Glyph(double q) => q switch { 4 => "𝅝", 2 => "𝅗𝅥", 1 => "𝅘𝅥", 0.5 => "𝅘𝅥𝅮", 0.25 => "𝅘𝅥𝅯", _ => "𝅘𝅥𝅰" };
    internal void Push() { undo.Add(Json.Write(notes)); if (undo.Count > 200) undo.RemoveAt(0); }
    void Undo() { if (undo.Count == 0) return; notes = Json.Read<List<Note>>(undo[^1]); undo.RemoveAt(undo.Count - 1); selected.Clear(); staff.Refresh(); UpdateStatus(); }
    void Keys(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;
        if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { Undo(); e.Handled = true; return; }
        if (e.Key is Key.Delete or Key.Back && selected.Count > 0) { Push(); notes.RemoveAll(selected.Contains); selected.Clear(); staff.Refresh(); UpdateStatus(); e.Handled = true; return; }
        if (e.Key is Key.Up or Key.Down && selected.Count > 0)
        {
            Push(); int d = (e.Key == Key.Up ? 1 : -1) * (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 12 : 1);
            foreach (var n in selected) n.Pitch = Math.Clamp(n.Pitch + d, 0, 127);
            if (selected.Count == 1) Hear(selected.First().Pitch); staff.Refresh(); e.Handled = true; return;
        }
        if (e.Key == Key.Space) { if (player != null) StopPlay(); else Play(false); e.Handled = true; }
    }
    internal void Hear(int pitch)
    {
        var n = audio.NoteOn(instrument, pitch, 0.9);
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) }; t.Tick += (_, _) => { n.Release(); t.Stop(); }; t.Start();
    }
    void Play(bool others)
    {
        StopPlay();
        // This layer as it is being edited (its volume, pan and pitch kept), alone or with the rest of the song.
        var copy = Json.Clone(song); var mine = copy.Tracks[layer]; mine.Notes = notes.Select(n => n.Copy()).ToList(); mine.Instrument = instrument.Copy(); mine.Mute = false;
        if (others) { foreach (var t in copy.Tracks) t.Solo = false; } else copy.Tracks = [mine];
        copy.Bars = Math.Max(copy.Bars, copy.BarsNeeded());
        var (l, r) = SongRenderer.Render(copy); player = audio.Play(l, r); clock.Start();
    }
    void StopPlay() { player?.Stop(); player = null; clock.Stop(); playStep = -1; staff.InvalidateVisual(); }
    internal void UpdateStatus()
    {
        var value = MusicTheory.Values.First(v => v.Quarters == quarters).Name;
        status.Text = (restMode ? $"Click to insert a {(dotted ? "dotted " : "")}{value} rest" : $"Click the staff to place a {(dotted ? "dotted " : "")}{value} note") +
            (accidental switch { 1 => " (sharp)", -1 => " (flat)", 2 => " (natural)", _ => "" }) + $"  •  {notes.Count} notes  •  click a note to select it, ↑/↓ move it a semitone (Shift: octave), Delete or right-click removes, Ctrl+Z undo, Space plays";
    }
}

/// <summary>Draws the staff and handles clicks. Time runs left to right at a steady rate (proportional spacing).</summary>
sealed class StaffView : FrameworkElement
{
    readonly SheetMusicEditor e;
    const double LineGap = 10, Top = 70, Left = 110;
    double StepW => Math.Max(9, 34.0 / e.Song.StepsPerBeat * 2);
    static readonly Typeface Music = new(new FontFamily("Segoe UI Symbol"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), Text = new("Segoe UI");
    static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(30, 30, 36)), Chosen = new SolidColorBrush(Color.FromRgb(20, 110, 220)), Ghost = new SolidColorBrush(Color.FromArgb(90, 20, 110, 220));
    (int Step, int Pitch)? hover;
    public StaffView(SheetMusicEditor editor) { e = editor; Focusable = true; }
    public void Refresh()
    {
        int steps = Math.Max(e.Song.TotalSteps, e.notes.Select(n => n.Step + n.Length).DefaultIfEmpty(0).Max());
        int bars = (int)Math.Ceiling(steps / (double)e.Song.StepsPerBar) + 1;
        Width = Left + bars * e.Song.StepsPerBar * StepW + 60; Height = Top + LineGap * 4 + 110;
        InvalidateVisual();
    }
    bool Flats => MusicTheory.UsesFlats(e.Song.Key, e.Song.Scale);
    // Diatonic position: C0 = 0, D0 = 1 … ; the bottom staff line is E4 (treble) or G2 (bass).
    int BottomLine => e.clef == "bass" ? 2 * 7 + 4 : 4 * 7 + 2;
    static readonly int[] SharpLetter = [0, 0, 1, 1, 2, 3, 3, 4, 4, 5, 5, 6], FlatLetter = [0, 1, 1, 2, 2, 3, 4, 4, 5, 5, 6, 6];
    static readonly int[] Natural = [0, 2, 4, 5, 7, 9, 11];
    (int Position, int Accidental) Spell(int pitch)
    {
        int pc = ((pitch % 12) + 12) % 12, octave = pitch / 12 - 1, letter = (Flats ? FlatLetter : SharpLetter)[pc];
        int accidental = pc - Natural[letter]; // -1, 0 or +1
        return (octave * 7 + letter, accidental);
    }
    double Y(int position) => Top + LineGap * 4 - (position - BottomLine) * LineGap / 2;
    int PositionAt(double y) => BottomLine + (int)Math.Round((Top + LineGap * 4 - y) / (LineGap / 2));
    // The key signature: which letters (0 = C … 6 = B) are sharp or flat.
    int[] KeyAccidentals()
    {
        var acc = new int[7]; int root = MusicTheory.KeyIndex(e.Song.Key); if (e.Song.Scale.Contains("minor") || e.Song.Scale == "blues") root = (root + 3) % 12;
        int[] sharpsOrder = [3, 0, 4, 1, 5, 2, 6], flatsOrder = [6, 2, 5, 1, 4, 0, 3];
        int count = root switch { 7 => 1, 2 => 2, 9 => 3, 4 => 4, 11 => 5, 6 => 6, 1 => 7, _ => 0 }, flats = root switch { 5 => 1, 10 => 2, 3 => 3, 8 => 4, 1 => 5, 6 => 6, _ => 0 };
        if (Flats) for (int i = 0; i < flats; i++) acc[flatsOrder[i]] = -1; else for (int i = 0; i < count; i++) acc[sharpsOrder[i]] = 1;
        return acc;
    }
    int PitchAt(int position)
    {
        int octave = Math.DivRem(position, 7, out int letter); if (letter < 0) { letter += 7; octave--; }
        int acc = e.accidental switch { 2 => 0, 1 => 1, -1 => -1, _ => KeyAccidentals()[letter] };
        return Math.Clamp((octave + 1) * 12 + Natural[letter] + acc, 0, 127);
    }
    int StepAt(double x) => (int)Math.Floor((x - Left) / StepW);
    protected override void OnRender(DrawingContext dc)
    {
        var song = e.Song; double width = ActualWidth;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, ActualHeight));
        var line = new Pen(Ink, 1);
        for (int i = 0; i < 5; i++) dc.DrawLine(line, new Point(10, Top + i * LineGap), new Point(width - 20, Top + i * LineGap));
        // Clef, key signature, time signature.
        Draw(dc, e.clef == "bass" ? "𝄢" : "𝄞", Music, e.clef == "bass" ? 38 : 50, new Point(14, e.clef == "bass" ? Top - 14 : Top - 26));
        var acc = KeyAccidentals(); double kx = 50; int[] sharpsOrder = [3, 0, 4, 1, 5, 2, 6], flatsOrder = [6, 2, 5, 1, 4, 0, 3];
        foreach (int letter in Flats ? flatsOrder : sharpsOrder)
        {
            if (acc[letter] == 0) continue;
            int basePos = e.clef == "bass" ? 3 * 7 + letter : 4 * 7 + letter; // an octave that sits on the staff
            if (basePos > BottomLine + 8) basePos -= 7; if (basePos < BottomLine + 1) basePos += 7;
            Draw(dc, acc[letter] > 0 ? "♯" : "♭", Text, 16, new Point(kx, Y(basePos) - 12)); kx += 9;
        }
        Draw(dc, song.Numerator.ToString(), Text, 20, new Point(kx + 6, Top - 4), true); Draw(dc, song.Denominator.ToString(), Text, 20, new Point(kx + 6, Top + LineGap * 2 - 4), true);
        // Bar lines.
        int bars = (int)((width - Left) / (song.StepsPerBar * StepW));
        for (int b = 0; b <= bars; b++) { double x = Left + b * song.StepsPerBar * StepW - 6; dc.DrawLine(line, new Point(x, Top), new Point(x, Top + LineGap * 4)); if (b < bars) Draw(dc, (b + 1).ToString(), Text, 10, new Point(x + 3, Top - 22)); }
        // Rests in the gaps (whole beats where possible).
        var covered = new bool[bars * song.StepsPerBar + 1];
        foreach (var n in e.notes) for (int s = n.Step; s < n.Step + n.Length && s < covered.Length; s++) covered[s] = true;
        int perBeat = Math.Max(1, song.StepsPerBar / song.Numerator);
        for (int s = 0; s < covered.Length - 1;)
        {
            if (covered[s]) { s++; continue; }
            int len = 1; while (s + len < covered.Length - 1 && !covered[s + len] && len < song.StepsPerBar - s % song.StepsPerBar && !(len >= perBeat && (s + len) % perBeat == 0 && s % perBeat != 0)) len++;
            double q = len / (double)song.StepsPerBeat, value = MusicTheory.Values.Select(v => v.Quarters).Where(v => v <= q + 1e-9).DefaultIfEmpty(0.125).Max();
            int used = Math.Max(1, (int)Math.Round(value * song.StepsPerBeat));
            if (s * StepW + Left < width) Draw(dc, value switch { 4 => "𝄻", 2 => "𝄼", 1 => "𝄽", 0.5 => "𝄾", 0.25 => "𝄿", _ => "𝅀" }, Music, 30, new Point(Left + s * StepW, Top - 6), false, Brushes.Gray);
            s += used;
        }
        // Notes: heads, ledger lines, accidentals, stems and flags; chords share a stem.
        foreach (var chord in e.notes.GroupBy(n => (n.Step, n.Length)))
        {
            double x = Left + chord.Key.Step * StepW;
            double q = chord.Key.Length / (double)song.StepsPerBeat;
            var (value, dot) = ValueFor(q);
            var heads = chord.Select(n => (Note: n, Spell: Spell(n.Pitch))).OrderBy(h => h.Spell.Position).ToList();
            bool up = heads.Average(h => h.Spell.Position) < BottomLine + 4;
            foreach (var (n, (pos, accidental)) in heads)
            {
                double y = Y(pos); var brush = e.selected.Contains(n) ? Chosen : Ink;
                for (int ledger = BottomLine - 2; ledger >= pos; ledger -= 2) dc.DrawLine(line, new Point(x - 4, Y(ledger)), new Point(x + 14, Y(ledger)));
                for (int ledger = BottomLine + 10; ledger <= pos; ledger += 2) dc.DrawLine(line, new Point(x - 4, Y(ledger)), new Point(x + 14, Y(ledger)));
                int keyAcc = KeyAccidentals()[((pos % 7) + 7) % 7];
                if (accidental != keyAcc) Draw(dc, accidental > 0 ? "♯" : accidental < 0 ? "♭" : "♮", Text, 15, new Point(x - 13, y - 11), false, brush);
                var head = new EllipseGeometry(new Point(x + 5, y), 5.6, 4.2) { Transform = new RotateTransform(-20, x + 5, y) };
                dc.DrawGeometry(value >= 2 ? Brushes.White : brush, new Pen(brush, 1.4), head);
                if (dot) dc.DrawEllipse(brush, null, new Point(x + 15, y - 2), 1.8, 1.8);
                // The note's real length, when it doesn't match a written value exactly.
                double written = value * (dot ? 1.5 : 1);
                if (Math.Abs(written - q) > 1e-6) dc.DrawRectangle(Ghost, null, new Rect(x, y - 1.5, chord.Key.Length * StepW, 3));
            }
            if (value < 4)
            {
                var brush = chord.Any(e.selected.Contains) ? Chosen : Ink; var pen = new Pen(brush, 1.3);
                double yLow = Y(heads[0].Spell.Position), yHigh = Y(heads[^1].Spell.Position);
                double sx = up ? x + 10.5 : x - 0.2, sy = up ? yHigh - 32 : yLow + 32;
                dc.DrawLine(pen, new Point(sx, up ? yLow : yHigh), new Point(sx, sy));
                int flags = value switch { 0.5 => 1, 0.25 => 2, 0.125 => 3, _ => 0 };
                for (int f = 0; f < flags; f++) { double fy = sy + (up ? f * 6 : -f * 6); dc.DrawLine(pen, new Point(sx, fy), new Point(sx + 8, fy + (up ? 10 : -10))); }
            }
        }
        // Hover ghost and the play cursor.
        if (hover is var (hs, hp) && !e.restMode) { var (pos, _) = Spell(hp); dc.DrawEllipse(Ghost, null, new Point(Left + hs * StepW + 5, Y(pos)), 5.6, 4.2); }
        if (hover is var (rs, _) && e.restMode) dc.DrawRectangle(Ghost, null, new Rect(Left + rs * StepW, Top, e.ValueSteps * StepW, LineGap * 4));
        if (e.playStep >= 0) { double px = Left + e.playStep * StepW; dc.DrawLine(new Pen(Brushes.OrangeRed, 2), new Point(px, Top - 30), new Point(px, Top + LineGap * 4 + 30)); }
    }
    static (double Value, bool Dot) ValueFor(double q)
    {
        foreach (var v in MusicTheory.Values) { if (Math.Abs(v.Quarters - q) < 1e-6) return (v.Quarters, false); if (Math.Abs(v.Quarters * 1.5 - q) < 1e-6) return (v.Quarters, true); }
        return (MusicTheory.Values.Select(v => v.Quarters).Where(v => v <= q).DefaultIfEmpty(0.125).Max(), false);
    }
    static void Draw(DrawingContext dc, string text, Typeface face, double size, Point at, bool bold = false, Brush? brush = null)
        => dc.DrawText(new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush ?? Ink, 1.0) { }, at);
    Note? NoteAt(Point p)
    {
        foreach (var n in e.notes)
        {
            double x = Left + n.Step * StepW + 5, y = Y(Spell(n.Pitch).Position);
            if (Math.Abs(p.X - x) <= 7 && Math.Abs(p.Y - y) <= 5) return n;
        }
        return null;
    }
    int Snap(int step) { int grid = Math.Max(1, Math.Min(e.ValueSteps, e.Song.StepsPerBeat)); return Math.Max(0, step / grid * grid); }
    protected override void OnMouseMove(MouseEventArgs args)
    {
        var p = args.GetPosition(this); int step = StepAt(p.X);
        hover = step >= 0 ? (Snap(step), PitchAt(PositionAt(p.Y))) : null; InvalidateVisual();
    }
    protected override void OnMouseLeave(MouseEventArgs args) { hover = null; InvalidateVisual(); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs args)
    {
        Focus(); var p = args.GetPosition(this); int step = StepAt(p.X); if (step < 0) return;
        var hit = NoteAt(p);
        if (hit != null && !e.restMode)
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) e.selected.Clear();
            if (!e.selected.Remove(hit)) e.selected.Add(hit); e.Hear(hit.Pitch); InvalidateVisual(); e.UpdateStatus(); return;
        }
        e.Push(); step = Snap(step);
        if (e.restMode) { foreach (var n in e.notes.Where(n => n.Step >= step)) n.Step += e.ValueSteps; }
        else
        {
            int pitch = PitchAt(PositionAt(p.Y));
            var note = new Note { Step = step, Length = e.ValueSteps, Pitch = pitch, Velocity = 0.9 };
            e.notes.RemoveAll(n => n.Step == step && n.Pitch == pitch); e.notes.Add(note); e.selected.Clear(); e.selected.Add(note); e.Hear(pitch);
        }
        Refresh(); e.UpdateStatus();
    }
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs args)
    {
        var hit = NoteAt(args.GetPosition(this)); if (hit == null) return;
        e.Push(); e.notes.Remove(hit); e.selected.Remove(hit); Refresh(); e.UpdateStatus(); args.Handled = true;
    }
}
