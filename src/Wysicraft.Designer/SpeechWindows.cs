using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Wysicraft.Core.Audio;
using Wysicraft.Models;
namespace Wysicraft.Designer;

/// <summary>Choosing a voice: one voice, or a mix of two with a balance, and a speed. ▶ plays a sample. Used by Speech
/// settings, the first-time voice picker and Create audio from text.</summary>
sealed class VoicePicker : StackPanel
{
    readonly ComboBox first = new() { MinWidth = 260 }, second = new() { MinWidth = 260 };
    readonly Slider balance = new() { Minimum = 0.1, Maximum = 0.9, Value = 0.5, Width = 160, VerticalAlignment = VerticalAlignment.Center, ToolTip = "How much of each voice: left is more of the first, right more of the second." };
    readonly Slider speed = new() { Minimum = 0.5, Maximum = 2, Value = 1, Width = 160, VerticalAlignment = VerticalAlignment.Center, TickFrequency = 0.05, IsSnapToTickEnabled = true };
    readonly TextBlock speedText = new() { Width = 40, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
    readonly StackPanel mixRow;
    readonly Button play = new() { Content = "▶ Try it", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(8, 0, 0, 0) };
    public event Action? Changed;
    public Func<string> SampleText { get; set; } = () => "Hi! I'm here to help you make your game. Let's build something fun together.";

    public VoicePicker(string spec, double rate, bool showSpeed = true)
    {
        var voices = TextToSpeech.Voices();
        string Label(VoiceInfo v) => $"{v.Name}  ·  {v.Gender}  ·  {v.Language}{(v.Accent.Length > 0 ? " (" + v.Accent + ")" : "")}";
        foreach (var v in voices) first.Items.Add(new ComboBoxItem { Content = Label(v), Tag = v.Id });
        second.Items.Add(new ComboBoxItem { Content = "No mix (one voice)", Tag = "" });
        foreach (var v in voices) second.Items.Add(new ComboBoxItem { Content = Label(v), Tag = v.Id });
        List<(string Id, float Weight)> parts;
        try { parts = TextToSpeech.ParseVoice(spec); } catch (InvalidDataException) { parts = [(TextToSpeech.DefaultVoice, 1)]; }
        Select(first, parts[0].Id); Select(second, parts.Count > 1 ? parts[1].Id : "");
        if (parts.Count > 1) balance.Value = Math.Clamp(parts[1].Weight, 0.1, 0.9);
        speed.Value = Math.Clamp(rate, 0.5, 2);

        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        row1.Children.Add(new TextBlock { Text = "Voice", Width = 70, VerticalAlignment = VerticalAlignment.Center }); row1.Children.Add(first); row1.Children.Add(play);
        Children.Add(row1);
        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        row2.Children.Add(new TextBlock { Text = "Mix with", Width = 70, VerticalAlignment = VerticalAlignment.Center }); row2.Children.Add(second);
        Children.Add(row2);
        mixRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(70, 2, 0, 2) };
        mixRow.Children.Add(new TextBlock { Text = "first", Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }); mixRow.Children.Add(balance);
        mixRow.Children.Add(new TextBlock { Text = "second", Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        Children.Add(mixRow);
        var row3 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2), Visibility = showSpeed ? Visibility.Visible : Visibility.Collapsed };
        row3.Children.Add(new TextBlock { Text = "Speed", Width = 70, VerticalAlignment = VerticalAlignment.Center }); row3.Children.Add(speed); row3.Children.Add(speedText);
        Children.Add(row3);
        void Update() { mixRow.Visibility = (string)((ComboBoxItem)second.SelectedItem).Tag == "" ? Visibility.Collapsed : Visibility.Visible; speedText.Text = speed.Value.ToString("0.00", CultureInfo.InvariantCulture) + "×"; }
        Update();
        first.SelectionChanged += (_, _) => { Update(); Changed?.Invoke(); };
        second.SelectionChanged += (_, _) => { Update(); Changed?.Invoke(); };
        balance.ValueChanged += (_, _) => Changed?.Invoke();
        speed.ValueChanged += (_, _) => { Update(); Changed?.Invoke(); };
        play.Click += async (_, _) =>
        {
            if (TextToSpeech.Shared.Speaking) { TextToSpeech.Shared.Stop(); return; }
            play.Content = "■ Stop";
            try { await TextToSpeech.Shared.SayAsync(SampleText(), Spec, Speed); }
            catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "Speech"); }
            finally { play.Content = "▶ Try it"; }
        };
    }
    static void Select(ComboBox box, string id) => box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == id) ?? box.Items[0];
    public string Spec
    {
        get
        {
            string a = (string)((ComboBoxItem)first.SelectedItem).Tag, b = (string)((ComboBoxItem)second.SelectedItem).Tag;
            if (b == "" || b == a) return a;
            double w = Math.Round(balance.Value, 2);
            return TextToSpeech.Normal($"{a}*{(1 - w).ToString(CultureInfo.InvariantCulture)}+{b}*{w.ToString(CultureInfo.InvariantCulture)}");
        }
    }
    public double Speed => Math.Round(speed.Value, 2);
}

public partial class MainWindow
{
    /// <summary>The speech preferences, applied to the speech engines.</summary>
    Preferences SpeechPrefs()
    {
        var p = Prefs();
        TextToSpeech.UseFullModel = p.SpeechFullModel && SpeechFiles.Has(SpeechFiles.FullVoice);
        TextToSpeech.Shared.Volume = p.SpeechVolume;
        SpeechToText.Model = p.WhisperModel;
        return p;
    }
    /// <summary>The assistant's voice (the default one until a voice has been chosen).</summary>
    internal string AssistantVoice => SpeechPrefs().SpeechVoice is { Length: > 0 } v ? v : TextToSpeech.DefaultVoice;
    internal double AssistantSpeed => Math.Clamp(Prefs().SpeechSpeed, 0.5, 2);
    internal bool ReadTutorialsAloud { get => Prefs().ReadTutorialsAloud; set { Prefs().ReadTutorialsAloud = value; SavePrefs(); } }

    static Window SpeechWindow(Window owner, string title, double width, double height) => new()
    {
        Owner = owner, Title = title, Width = width, Height = height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false
    };
    static TextBlock SpeechHeading(string text) => new() { Text = text.ToUpperInvariant(), Foreground = Brushes.LightSkyBlue, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) };
    static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(0, 2, 0, 4) };

    void RequireSpeech()
    {
        if (!TextToSpeech.Available) throw new InvalidOperationException("The voice model isn't installed with this copy of Arcadia Studio (it comes in its Speech folder). Reinstall Arcadia Studio to get it back.");
    }

    /// <summary>The first time speech is used (Create audio from text, or starting MCP), a short choice of the assistant's
    /// voice. Returns false when there's no voice model to choose with.</summary>
    internal bool EnsureVoiceChosen()
    {
        if (Prefs().SpeechVoice.Length > 0) return true;
        if (!TextToSpeech.Available || DockSmoke) return false;
        _ = TextToSpeech.Shared.WarmAsync();
        var window = SpeechWindow(this, "Choose your assistant's voice", 560, 330);
        var root = new StackPanel { Margin = new Thickness(16) }; window.Content = root;
        root.Children.Add(new TextBlock { Text = "Your assistant can talk", FontSize = 16, FontWeight = FontWeights.SemiBold });
        root.Children.Add(Note("Arcadia Studio has a voice built in. Your AI assistant uses it to narrate tutorials and videos and to read lines aloud, and you can give characters in your game their own lines. Pick the voice it speaks with: press ▶ Try it to hear each one. You can change it any time in Advanced → Speech settings."));
        var picker = new VoicePicker(TextToSpeech.DefaultVoice, 1) { Margin = new Thickness(0, 8, 0, 0) };
        root.Children.Add(picker);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var use = new Button { Content = "Use this voice", IsDefault = true, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 8, 0) };
        var later = new Button { Content = "Decide later", IsCancel = true, Padding = new Thickness(12, 3, 12, 3) };
        buttons.Children.Add(use); buttons.Children.Add(later); root.Children.Add(buttons);
        use.Click += (_, _) => { Prefs().SpeechVoice = picker.Spec; Prefs().SpeechSpeed = picker.Speed; SavePrefs(); window.DialogResult = true; };
        window.Closed += (_, _) => TextToSpeech.Shared.Stop();
        window.ShowDialog();
        // "Decide later" uses the default voice and asks again next time.
        return true;
    }

    Window? speechSettings;
    internal void ShowSpeechSettings()
    {
        if (speechSettings != null) { speechSettings.Activate(); return; }
        var p = SpeechPrefs();
        var window = SpeechWindow(this, "Speech settings", 640, 720); speechSettings = window;
        window.Closed += (_, _) => { speechSettings = null; TextToSpeech.Shared.Stop(); };
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; window.Content = scroll;
        var root = new StackPanel { Margin = new Thickness(16) }; scroll.Content = root;

        // ---- Voice ----
        root.Children.Add(SpeechHeading("The assistant's voice (Kokoro)"));
        if (!TextToSpeech.Available) root.Children.Add(new TextBlock { Text = "The voice model isn't installed with this copy of Arcadia Studio. Reinstall Arcadia Studio to get it back.", Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap });
        else
        {
            root.Children.Add(Note("The voice your AI assistant narrates tutorials and videos with, and the starting voice in Create audio from text. Mix two voices to make a new one."));
            var picker = new VoicePicker(AssistantVoice, AssistantSpeed);
            picker.Changed += () => { Prefs().SpeechVoice = picker.Spec; Prefs().SpeechSpeed = picker.Speed; SavePrefs(); };
            root.Children.Add(picker);
            var volumeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            var volume = new Slider { Minimum = 0, Maximum = 1, Value = p.SpeechVolume, Width = 160, VerticalAlignment = VerticalAlignment.Center };
            volume.ValueChanged += (_, _) => { Prefs().SpeechVolume = Math.Round(volume.Value, 2); TextToSpeech.Shared.Volume = volume.Value; SavePrefs(); };
            volumeRow.Children.Add(new TextBlock { Text = "Volume", Width = 70, VerticalAlignment = VerticalAlignment.Center }); volumeRow.Children.Add(volume);
            root.Children.Add(volumeRow);
            var aloud = new CheckBox { Content = "Read tutorial steps aloud", IsChecked = p.ReadTutorialsAloud, ToolTip = "When an assistant's tutorial has narration, speak each step as it's shown." };
            aloud.Click += (_, _) => { Prefs().ReadTutorialsAloud = aloud.IsChecked == true; SavePrefs(); };
            root.Children.Add(aloud);

            root.Children.Add(new TextBlock { Text = "Voice model", Margin = new Thickness(0, 10, 0, 2), FontWeight = FontWeights.SemiBold });
            var standard = new RadioButton { Content = "Standard (comes with Arcadia Studio, 156 MB)", GroupName = "kokoro", IsChecked = !TextToSpeech.UseFullModel };
            var full = new RadioButton { Content = "Full precision (325 MB download): the same voices, very slightly cleaner, the same speed", GroupName = "kokoro", IsChecked = TextToSpeech.UseFullModel };
            root.Children.Add(standard); root.Children.Add(full);
            var fullRow = ModelRow(SpeechFiles.FullVoice, () => full.IsChecked == true, () => { });
            root.Children.Add(fullRow.Row);
            void Choose(bool wantFull)
            {
                if (wantFull && !SpeechFiles.Has(SpeechFiles.FullVoice)) { standard.IsChecked = true; MessageBox.Show(window, "Install the full-precision model first (the Install button below it).", "Speech"); return; }
                Prefs().SpeechFullModel = wantFull; SavePrefs(); SpeechPrefs();
            }
            standard.Checked += (_, _) => Choose(false); full.Checked += (_, _) => Choose(true);
        }

        // ---- Recognition ----
        root.Children.Add(SpeechHeading("Speech recognition (Whisper)"));
        root.Children.Add(Note("Turns speech into text: the microphone button in Ask Agent, Create text from audio, and an assistant's transcribing. Everything runs on this computer. Tiny comes with Arcadia Studio; the larger models hear more accurately but take longer."));
        var group = new List<RadioButton>();
        foreach (var model in SpeechFiles.Whisper)
        {
            var choose = new RadioButton { Content = $"{model.Title}  ({model.Bytes / 1_000_000:N0} MB) — {model.About}", GroupName = "whisper", IsChecked = SpeechToText.Model == model.Id, Tag = model };
            group.Add(choose);
            root.Children.Add(choose);
            if (!SpeechFiles.IsBundled(model)) root.Children.Add(ModelRow(model, () => choose.IsChecked == true, () => { }).Row);
            choose.Checked += (_, _) =>
            {
                if (!SpeechFiles.Has(model)) { group.First(g => ((SpeechFiles.Download)g.Tag).Id == SpeechToText.Model).IsChecked = true; MessageBox.Show(window, $"Install {model.Title} first (the Install button below it).", "Speech"); return; }
                Prefs().WhisperModel = model.Id; SavePrefs(); SpeechPrefs();
            };
        }
        var languageRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 2) };
        var language = LanguageBox(p.WhisperLanguage);
        language.SelectionChanged += (_, _) => { Prefs().WhisperLanguage = (string)((ComboBoxItem)language.SelectedItem).Tag; SavePrefs(); };
        languageRow.Children.Add(new TextBlock { Text = "Language", Width = 90, VerticalAlignment = VerticalAlignment.Center }); languageRow.Children.Add(language);
        root.Children.Add(languageRow);

        var micRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        var mic = new ComboBox { MinWidth = 260 };
        mic.Items.Add(new ComboBoxItem { Content = "Windows' default microphone", Tag = -1 });
        try { var devices = Microphone.Devices(); for (int i = 0; i < devices.Count; i++) mic.Items.Add(new ComboBoxItem { Content = devices[i], Tag = i }); } catch (Exception) { }
        mic.SelectedItem = mic.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == p.Microphone) ?? mic.Items[0];
        mic.SelectionChanged += (_, _) => { Prefs().Microphone = (int)((ComboBoxItem)mic.SelectedItem).Tag; SavePrefs(); };
        var test = new Button { Content = "🎤 Test", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Say something: it's written below when you stop talking." };
        micRow.Children.Add(new TextBlock { Text = "Microphone", Width = 90, VerticalAlignment = VerticalAlignment.Center }); micRow.Children.Add(mic); micRow.Children.Add(test);
        root.Children.Add(micRow);
        var heard = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(90, 4, 0, 0), Opacity = 0.85 };
        root.Children.Add(heard);
        test.Click += async (_, _) =>
        {
            test.IsEnabled = false;
            try { heard.Text = "Listening… say something, then stop talking."; var text = await ListenAsync(level => { }); heard.Text = text.Length > 0 ? "Heard: “" + text + "”" : "Nothing was heard. Check the microphone above, and Windows' microphone privacy settings."; }
            catch (Exception ex) { heard.Text = ex.Message; }
            finally { test.IsEnabled = true; }
        };
        window.Show();

        // A download row: Install (with progress and Cancel) or Remove.
        (FrameworkElement Row, Action Refresh) ModelRow(SpeechFiles.Download model, Func<bool> inUse, Action changed)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(26, 0, 0, 4) };
            var action = new Button { Padding = new Thickness(10, 1, 10, 1) };
            var bar = new ProgressBar { Width = 160, Height = 10, Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed, Maximum = 1 };
            var state = new TextBlock { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 };
            row.Children.Add(action); row.Children.Add(bar); row.Children.Add(state);
            CancellationTokenSource? downloading = null;
            void Refresh()
            {
                bool has = SpeechFiles.Has(model);
                action.Content = downloading != null ? "Cancel" : has ? "Remove" : $"Install ({model.Bytes / 1_000_000:N0} MB)";
                state.Text = downloading != null ? "" : has ? "Installed" : "";
            }
            action.Click += async (_, _) =>
            {
                if (downloading != null) { downloading.Cancel(); return; }
                if (SpeechFiles.Has(model))
                {
                    if (inUse()) { MessageBox.Show(window, "Choose another model first: this one is in use.", "Speech"); return; }
                    try { SpeechFiles.Remove(model); } catch (Exception ex) { MessageBox.Show(window, ex.Message, "Speech"); }
                    Refresh(); changed(); return;
                }
                downloading = new CancellationTokenSource(); bar.Visibility = Visibility.Visible; bar.Value = 0; Refresh();
                try { await SpeechFiles.InstallAsync(model, new Progress<double>(v => bar.Value = v), downloading.Token); Log($"Installed {model.Title} for speech."); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { MessageBox.Show(window, model.Title + " couldn't be installed: " + ex.Message, "Speech"); }
                finally { downloading = null; bar.Visibility = Visibility.Collapsed; Refresh(); changed(); }
            };
            Refresh();
            return (row, Refresh);
        }
    }

    static readonly (string Code, string Name)[] WhisperLanguages =
    [
        ("auto", "Detect it"), ("en", "English"), ("es", "Spanish"), ("fr", "French"), ("de", "German"), ("it", "Italian"), ("pt", "Portuguese"),
        ("nl", "Dutch"), ("pl", "Polish"), ("ru", "Russian"), ("uk", "Ukrainian"), ("tr", "Turkish"), ("ar", "Arabic"), ("hi", "Hindi"),
        ("ja", "Japanese"), ("ko", "Korean"), ("zh", "Chinese"), ("sv", "Swedish"), ("no", "Norwegian"), ("da", "Danish"), ("fi", "Finnish"),
    ];
    static ComboBox LanguageBox(string code)
    {
        var box = new ComboBox { MinWidth = 180 };
        foreach (var (c, n) in WhisperLanguages) box.Items.Add(new ComboBoxItem { Content = n, Tag = c });
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == code) ?? box.Items[0];
        return box;
    }

    /// <summary>Listens on the chosen microphone until a pause after speech (or stop), then transcribes it.</summary>
    internal async Task<string> ListenAsync(Action<double> level, Microphone? given = null, CancellationToken cancel = default)
    {
        var p = SpeechPrefs();
        using var mic = given ?? new Microphone(p.Microphone);
        mic.Level += level;
        using var stop = cancel.Register(mic.Stop);
        var samples = await mic.Recording;
        if (!mic.HeardSpeech || samples.Length < SpeechToText.Rate / 4) return "";
        var segments = await SpeechToText.Shared.TranscribeAsync(samples, p.WhisperLanguage, ProjectWords());
        return SpeechToText.Join(segments);
    }
    /// <summary>The project's own names (screens and controls), so Whisper expects them.</summary>
    string ProjectWords() => string.Join(", ", project.Screens.Take(10).Select(s => s.Id).Concat(ui.Elements.Take(30).Select(e => e.Id)).Where(n => n.Length > 2).Distinct().Select(n => n.Replace('_', ' ')));

    /// <summary>Mono 24 kHz speech at the project's sound rate.</summary>
    internal static float[] ToProjectRate(float[] speech)
    {
        var source = new RawSourceWaveStream(new MemoryStream(ToBytes(speech)), WaveFormat.CreateIeeeFloatWaveFormat(TextToSpeech.Rate, 1));
        var resampler = new WdlResamplingSampleProvider(source.ToSampleProvider(), SongRenderer.SampleRate);
        var output = new List<float>((int)((long)speech.Length * SongRenderer.SampleRate / TextToSpeech.Rate) + 1024);
        var buffer = new float[16384]; int n;
        while ((n = resampler.Read(buffer, 0, buffer.Length)) > 0) for (int i = 0; i < n; i++) output.Add(buffer[i]);
        return output.ToArray();
    }
    static byte[] ToBytes(float[] samples) { var bytes = new byte[samples.Length * 4]; Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length); return bytes; }

    /// <summary>Saves speech to a file: .ogg, .wav or .mp3.</summary>
    internal static void SaveSpeech(string file, float[] speech) => AudioExport.Save(file, [ToProjectRate(speech)], SongRenderer.SampleRate);

    // ---- Advanced → Create audio from text ----
    internal void ShowCreateAudio(string start = "")
    {
        RequireSpeech();
        EnsureVoiceChosen();
        _ = TextToSpeech.Shared.WarmAsync();
        var window = SpeechWindow(this, "Create audio from text", 720, 600);
        var root = new DockPanel { Margin = new Thickness(14) }; window.Content = root;
        var head = Note("Type or import text, choose a voice, then listen, save it as a sound file, or add it to the project as a sound (a line for a character, a narrator or a menu). Everything runs on this computer.");
        DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 6) }; DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var import = new Button { Content = "Import text file…", Padding = new Thickness(10, 2, 10, 2) };
        top.Children.Add(import);
        var picker = new VoicePicker(AssistantVoice, AssistantSpeed) { Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(picker, Dock.Bottom);
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(buttons, Dock.Bottom);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Opacity = 0.85 }; DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status); root.Children.Add(buttons); root.Children.Add(picker);
        var text = new TextBox { Text = start, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 13, Padding = new Thickness(4) };
        root.Children.Add(text);
        picker.SampleText = () => text.Text.Trim().Length > 0 ? string.Join(" ", TextToSpeech.Pieces(text.Text).Take(2).Select(p => p.Text)) : "This is how your text will sound.";

        Button B(string label, string tip) { var b = new Button { Content = label, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 8, 4), ToolTip = tip }; buttons.Children.Add(b); return b; }
        var play = B("▶ Listen", "Speak the text now (it starts after the first sentence is ready).");
        var save = B("Save as…", "Save as an Ogg, WAV or MP3 file.");
        var add = B("Add to project…", "Add it to the project's sounds, to play with a Sound control or the play_sound action.");
        string Text() => text.Text.Trim().Length > 0 ? text.Text : throw new InvalidOperationException("Type some text first, or import a text file.");
        import.Click += (_, _) => Guard(() =>
        {
            var open = new OpenFileDialog { Filter = "Text files (*.txt;*.md)|*.txt;*.md|All files|*.*" };
            if (open.ShowDialog(window) != true) return;
            var info = new FileInfo(open.FileName);
            if (info.Length > 1_000_000) throw new InvalidOperationException("That file is over 1 MB of text: split it into smaller parts.");
            text.Text = File.ReadAllText(open.FileName);
        });
        play.Click += async (_, _) =>
        {
            if (TextToSpeech.Shared.Speaking) { TextToSpeech.Shared.Stop(); return; }
            try { play.Content = "■ Stop"; status.Text = "Speaking…"; await TextToSpeech.Shared.SayAsync(Text(), picker.Spec, picker.Speed); status.Text = ""; }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { play.Content = "▶ Listen"; }
        };
        async Task<float[]?> Render()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            status.Text = "Making the sound…";
            save.IsEnabled = add.IsEnabled = false;
            try
            {
                var speech = await TextToSpeech.Shared.RenderAsync(Text(), picker.Spec, picker.Speed);
                status.Text = $"Made {speech.Length / (double)TextToSpeech.Rate:0.0} s of speech in {clock.Elapsed.TotalSeconds:0.0} s.";
                return speech;
            }
            catch (Exception ex) { status.Text = ex.Message; return null; }
            finally { save.IsEnabled = add.IsEnabled = true; }
        }
        save.Click += async (_, _) =>
        {
            var dialog = new SaveFileDialog { Filter = "Ogg Vorbis (*.ogg)|*.ogg|WAV (*.wav)|*.wav|MP3 (*.mp3)|*.mp3", FileName = SpeechName(text.Text) + ".ogg" };
            if (dialog.ShowDialog(window) != true) return;
            var speech = await Render(); if (speech == null) return;
            try { SaveSpeech(dialog.FileName, speech); status.Text += " Saved " + Path.GetFileName(dialog.FileName) + "."; }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        add.Click += async (_, _) =>
        {
            string? name = AskName(window, "Add to project", "The sound's name (it becomes its sound ID):", SpeechName(text.Text));
            if (name == null) return;
            var speech = await Render(); if (speech == null) return;
            try { string path = SaveSoundAsset(name, "ogg", true, null, [ToProjectRate(speech)]); status.Text += $" Added to the project as {Wysicraft.Core.SoundAssets.Resource(path)}."; }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        window.Closed += (_, _) => TextToSpeech.Shared.Stop();
        window.Show();
    }
    /// <summary>A sound name from the first few words.</summary>
    internal static string SpeechName(string text)
    {
        var words = System.Text.RegularExpressions.Regex.Matches(text.ToLowerInvariant(), "[a-z0-9]+").Select(m => m.Value).Take(4).ToList();
        return words.Count > 0 ? string.Join("_", words) : "speech";
    }
    static string? AskName(Window owner, string title, string question, string start)
    {
        var dialog = SpeechWindow(owner, title, 420, 170);
        var root = new StackPanel { Margin = new Thickness(14) }; dialog.Content = root;
        root.Children.Add(new TextBlock { Text = question, Margin = new Thickness(0, 0, 0, 6) });
        var box = new TextBox { Text = start }; root.Children.Add(box);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "Add", IsDefault = true, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(12, 3, 12, 3) };
        buttons.Children.Add(ok); buttons.Children.Add(cancel); root.Children.Add(buttons);
        ok.Click += (_, _) => dialog.DialogResult = box.Text.Trim().Length > 0;
        dialog.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return dialog.ShowDialog() == true ? box.Text.Trim() : null;
    }

    // ---- Advanced → Create text from audio ----
    internal void ShowCreateText()
    {
        if (!SpeechToText.Available) throw new InvalidOperationException("The speech recognition model isn't installed with this copy of Arcadia Studio (it comes in its Speech folder). Reinstall Arcadia Studio to get it back.");
        var p = SpeechPrefs();
        var window = SpeechWindow(this, "Create text from audio", 720, 560);
        var root = new DockPanel { Margin = new Thickness(14) }; window.Content = root;
        var head = Note("Choose a recording (any audio or video file) or speak into the microphone. The text appears below, where you can change it, copy it, save it or add it to the screen. Everything runs on this computer.");
        DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);
        var top = new WrapPanel { Margin = new Thickness(0, 4, 0, 6) }; DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var choose = new Button { Content = "Choose audio or video file…", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 8, 4) };
        var record = new Button { Content = "🎤 Record", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 8, 4), ToolTip = "Speak, then press Stop (or just stop talking)." };
        var language = LanguageBox(p.WhisperLanguage);
        var times = new CheckBox { Content = "Timestamps (subtitles)", VerticalAlignment = VerticalAlignment.Center, ToolTip = "Write the text as SRT subtitles, each line with its time." };
        top.Children.Add(choose); top.Children.Add(record); top.Children.Add(new TextBlock { Text = "Language", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) }); top.Children.Add(language); top.Children.Add(times);
        var level = new ProgressBar { Height = 4, Maximum = 1, Margin = new Thickness(0, 0, 0, 6), Visibility = Visibility.Collapsed }; DockPanel.SetDock(level, Dock.Top); root.Children.Add(level);
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(buttons, Dock.Bottom);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Opacity = 0.85 }; DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status); root.Children.Add(buttons);
        var text = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 13, Padding = new Thickness(4) };
        root.Children.Add(text);
        Button B(string label, string tip) { var b = new Button { Content = label, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 8, 4), ToolTip = tip }; buttons.Children.Add(b); return b; }
        var copy = B("Copy", "Copy the text.");
        var saveText = B("Save as…", "Save as a text file (.txt) or subtitles (.srt).");
        var label = B("Add to screen as label", "A Label on the open screen with this text.");
        var box = B("Add to screen as text box", "A Text Box on the open screen with this text in it.");
        List<SpeechToText.Segment> last = [];
        string Lang() => (string)((ComboBoxItem)language.SelectedItem).Tag;
        void Show(List<SpeechToText.Segment> segments, TimeSpan took, double seconds)
        {
            last = segments;
            text.Text = times.IsChecked == true ? SpeechToText.Srt(segments) : SpeechToText.Join(segments);
            status.Text = segments.Count == 0 ? "No speech was heard." : $"{seconds:0.0} s of sound written in {took.TotalSeconds:0.0} s.";
        }
        times.Click += (_, _) => { if (last.Count > 0) text.Text = times.IsChecked == true ? SpeechToText.Srt(last) : SpeechToText.Join(last); };
        choose.Click += async (_, _) =>
        {
            var open = new OpenFileDialog { Filter = "Audio and video|*.ogg;*.mp3;*.wav;*.m4a;*.aac;*.flac;*.opus;*.wma;*.mp4;*.mkv;*.webm;*.mov;*.avi|All files|*.*" };
            if (open.ShowDialog(window) != true) return;
            choose.IsEnabled = record.IsEnabled = false;
            try
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                status.Text = "Reading " + Path.GetFileName(open.FileName) + "…";
                var samples = await FFmpeg.DecodeAsync(open.FileName, SpeechToText.Rate);
                status.Text = $"Listening to {samples.Length / (double)SpeechToText.Rate:0.0} s of sound…";
                Show(await SpeechToText.Shared.TranscribeAsync(samples, Lang(), ProjectWords()), clock.Elapsed, samples.Length / (double)SpeechToText.Rate);
            }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { choose.IsEnabled = record.IsEnabled = true; }
        };
        Microphone? listening = null;
        record.Click += async (_, _) =>
        {
            if (listening != null) { listening.Stop(); return; }
            try
            {
                listening = new Microphone(Prefs().Microphone, stopOnSilence: false, maxSeconds: 600);
                record.Content = "■ Stop"; choose.IsEnabled = false; level.Visibility = Visibility.Visible; status.Text = "Recording… press Stop when you're done.";
                listening.Level += v => Dispatcher.BeginInvoke(() => level.Value = v);
                var samples = await listening.Recording;
                record.Content = "🎤 Record"; level.Visibility = Visibility.Collapsed;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                status.Text = "Writing it down…";
                Show(await SpeechToText.Shared.TranscribeAsync(samples, Lang(), ProjectWords()), clock.Elapsed, samples.Length / (double)SpeechToText.Rate);
            }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { listening?.Dispose(); listening = null; record.Content = "🎤 Record"; choose.IsEnabled = true; level.Visibility = Visibility.Collapsed; }
        };
        string Current() => text.Text.Trim().Length > 0 ? text.Text.Trim() : throw new InvalidOperationException("There's no text yet.");
        copy.Click += (_, _) => Guard(() => { Clipboard.SetText(Current()); status.Text = "Copied."; });
        saveText.Click += (_, _) => Guard(() =>
        {
            var dialog = new SaveFileDialog { Filter = times.IsChecked == true ? "Subtitles (*.srt)|*.srt|Text (*.txt)|*.txt" : "Text (*.txt)|*.txt|Subtitles (*.srt)|*.srt", FileName = "transcript" };
            if (dialog.ShowDialog(window) != true) return;
            File.WriteAllText(dialog.FileName, Path.GetExtension(dialog.FileName).Equals(".srt", StringComparison.OrdinalIgnoreCase) && last.Count > 0 ? SpeechToText.Srt(last) : Current());
            status.Text = "Saved " + Path.GetFileName(dialog.FileName) + ".";
        });
        label.Click += (_, _) => Guard(() => { var e = AddTextToScreen("label", Current()); status.Text = $"Added the label {e.Id} to {ui.Id}."; });
        box.Click += (_, _) => Guard(() => { var e = AddTextToScreen("textbox", Current()); status.Text = $"Added the text box {e.Id} to {ui.Id}."; });
        window.Closed += (_, _) => listening?.Stop();
        window.Show();
    }

    /// <summary>A Label or Text Box with this text on the open screen, sized to fit it, one Undo step.</summary>
    internal Element AddTextToScreen(string type, string text)
    {
        if (ui.IsLeaderboard && type != "label") throw new InvalidOperationException("A leaderboard page takes labels, not text boxes.");
        Change();
        int width = (int)Math.Min(ui.Size.Width - 16, Math.Max(160, Math.Min(text.Length * 6, 420)));
        int lines = Math.Max(1, (int)Math.Ceiling(text.Length * 6.0 / width) + text.Count(c => c == '\n'));
        var element = new Element
        {
            Type = type, Id = Unique(type == "label" ? "text" : "textbox"),
            Bounds = new Bounds { X = 8, Y = 8, Width = width, Height = (int)Math.Min(ui.Size.Height - 16, Math.Max(20, lines * 12 + 8)) },
        };
        if (type == "label") element.Text = text; else { element.Value = text; element.Text = ""; }
        ui.Elements.Add(element);
        selected.Clear(); selected.Add(element.Id);
        RefreshAll();
        return element;
    }
}
