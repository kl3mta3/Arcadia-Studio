using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Wysicraft.Core;
using Wysicraft.Core.Audio;
using Wysicraft.Models;
using Wysicraft.Packaging;
namespace Wysicraft.Designer;

// Music maker, Sound effect maker and 8-bit audio import hosting: saving sounds into Assets (with what made them kept
// beside them), reading audio files, and the sound entries of the Assets right-click menu.
public partial class MainWindow
{
    AudioOut? assetAudio;
    AudioOut AssetAudio => assetAudio ??= new AudioOut();

    /// <summary>Saves a sound into the project. Saving over an existing sound keeps its sound ID (and changes the file's
    /// extension if the format changed); "as new" picks an unused name.</summary>
    string SaveSoundAsset(string name, string format, bool asNew, string? path, float[][] channels, string? sidecarSuffix = null, byte[]? sidecar = null, bool loop = false)
    {
        name = PixelEditor.SafeName(name);
        var bytes = AudioFiles.Encode(format, channels, SongRenderer.SampleRate, loop);
        if (bytes.Length > ProjectStore.MaxEntry) throw new InvalidOperationException("The sound is too long to keep in the project (32 MB at most). Make it shorter or use Ogg.");
        string target;
        if (!asNew && path != null && project.Assets.ContainsKey(path)) target = path[..(path.LastIndexOf('.') + 1)] + format;
        else
        {
            target = SoundAssets.Path(project.Manifest.Id, name + "." + format); int n = 1;
            while (SoundAssets.Find(project, SoundAssets.Resource(target)) != null) target = SoundAssets.Path(project.Manifest.Id, name + "_" + n++ + "." + format);
        }
        Change();
        if (path != null && path != target && !asNew) SoundAssets.RemoveWithSidecars(project, path);
        SoundAssets.RemoveWithSidecars(project, target);
        project.Assets[target] = bytes; if (sidecarSuffix != null && sidecar != null) project.Assets[target + sidecarSuffix] = sidecar;
        RefreshAssetBrowser(); RefreshInspector();
        double seconds = channels[0].Length / (double)SongRenderer.SampleRate;
        Log($"Saved {Path.GetFileName(target)} ({seconds:0.0} s, {bytes.Length / 1024} KB) to Assets. Its sound ID is {SoundAssets.Resource(target)}.");
        return target;
    }
    string SaveSong(MusicMaker.SaveRequest r) => SaveSoundAsset(r.Song.Name, r.Format, r.AsNew, r.Path, [r.Left, r.Right], SoundAssets.SongSuffix, Encoding.UTF8.GetBytes(Json.Write(r.Song)), r.Song.Loop);
    string SaveEffect(SoundEffectMaker.SaveRequest r) => SaveSoundAsset(r.Effect.Name, r.Format, r.AsNew, r.Path, [r.Samples], SoundAssets.EffectSuffix, Encoding.UTF8.GetBytes(Json.Write(r.Effect)));

    internal void OpenMusicMaker(string? path = null, Song? start = null, (float[][] Channels, int Rate, string Name)? transcribe = null)
    {
        var song = start ?? new Song { Name = "song", Bars = 4 };
        if (path != null && project.Assets.TryGetValue(path + SoundAssets.SongSuffix, out var saved)) song = Json.Read<Song>(Encoding.UTF8.GetString(saved));
        string? opened = path != null && project.Assets.ContainsKey(path + SoundAssets.SongSuffix) ? path : null;
        var file = Watch(opened); MusicMaker? maker = null;
        maker = new MusicMaker(this, song, opened, r => { CheckOverwrite(file, r.AsNew ? null : r.Path, maker!); var saved = SaveSong(r); Saw(file, saved); return saved; }, PickAudio);
        if (transcribe != null) maker.Loaded += (_, _) => maker.ImportAudio(transcribe);
        OpenBeside(maker);
    }
    internal SoundEffectMaker OpenSoundEffectMaker(string? path = null)
    {
        var fx = path != null && project.Assets.TryGetValue(path + SoundAssets.EffectSuffix, out var saved) ? Json.Read<SoundEffect>(Encoding.UTF8.GetString(saved)) : SoundEffect.Preset("coin");
        string? opened = path != null && project.Assets.ContainsKey(path + SoundAssets.EffectSuffix) ? path : null;
        var file = Watch(opened); SoundEffectMaker? maker = null;
        maker = new SoundEffectMaker(this, fx, opened, r => { CheckOverwrite(file, r.AsNew ? null : r.Path, maker!); var saved = SaveEffect(r); Saw(file, saved); return saved; });
        OpenBeside(maker);
        return maker;
    }

    // ---- Reading audio ----
    const double MaxImportSeconds = 600;
    /// <summary>Asks for an audio file and reads it as mono samples.</summary>
    (float[][] Channels, int Rate, string Name)? PickAudio()
    {
        var dialog = new OpenFileDialog { Filter = "Audio|*.wav;*.mp3;*.ogg;*.m4a;*.aac;*.wma;*.flac|All files|*.*", Title = "Choose a recording" };
        if (dialog.ShowDialog(this) != true) return null;
        var (channels, rate) = DecodeAudio(File.ReadAllBytes(dialog.FileName), Path.GetExtension(dialog.FileName));
        return (channels, rate, Path.GetFileNameWithoutExtension(dialog.FileName));
    }
    /// <summary>WAV and Ogg are read directly; MP3, M4A/AAC, WMA and FLAC through Windows.</summary>
    internal static (float[][] Channels, int Rate) DecodeAudio(byte[] data, string extension)
    {
        extension = extension.ToLowerInvariant();
        (float[][] Channels, int Rate) result;
        if (extension == ".wav") result = AudioFiles.ReadWav(data);
        else if (extension == ".ogg")
        {
            using var reader = new NVorbis.VorbisReader(new MemoryStream(data), true);
            result = ReadAll(reader.Channels, reader.SampleRate, buffer => reader.ReadSamples(buffer, 0, buffer.Length));
        }
        else
        {
            string temp = Path.Combine(Path.GetTempPath(), "wysicraft-audio-" + Guid.NewGuid().ToString("N") + extension);
            File.WriteAllBytes(temp, data);
            try
            {
                using var reader = new NAudio.Wave.MediaFoundationReader(temp);
                var samples = NAudio.Wave.WaveExtensionMethods.ToSampleProvider(reader);
                result = ReadAll(samples.WaveFormat.Channels, samples.WaveFormat.SampleRate, buffer => samples.Read(buffer, 0, buffer.Length));
            }
            catch (Exception ex) when (ex is not InvalidOperationException) { throw new InvalidOperationException("Windows couldn't read this audio file (" + ex.Message + "). Try a WAV, MP3 or Ogg file."); }
            finally { try { File.Delete(temp); } catch { } }
        }
        if (result.Channels[0].Length == 0) throw new InvalidOperationException("The audio file is empty.");
        return result;
        static (float[][], int) ReadAll(int channels, int rate, Func<float[], int> read)
        {
            var all = new List<float>(); var buffer = new float[channels * 4096]; long max = (long)(MaxImportSeconds * rate * channels);
            for (int got; (got = read(buffer)) > 0 && all.Count < max;) for (int i = 0; i < got; i++) all.Add(buffer[i]);
            int frames = all.Count / channels; var result = new float[channels][];
            for (int c = 0; c < channels; c++) { result[c] = new float[frames]; for (int i = 0; i < frames; i++) result[c][i] = all[i * channels + c]; }
            return (result, rate);
        }
    }
    (float[][] Channels, int Rate) DecodeSoundAsset(string path) => DecodeAudio(project.Assets[path], Path.GetExtension(path));

    // ---- Import audio as 8-bit ----
    void ImportAudioAs8Bit() => CrunchDialog(PickAudio());
    /// <summary>Two ways to make a recording retro: turn it into an 8-bit song (notes played by chip instruments, like a
    /// chiptune cover) or crunch the recording itself, like an old console playing a sample.</summary>
    void CrunchDialog((float[][] Channels, int Rate, string Name)? source)
    {
        if (source is not var (channels, rate, name)) return;
        var window = new Window { Owner = this, Title = "Import audio as 8-bit — " + name, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SetResourceReference(StyleProperty, typeof(Window));
        var panel = new StackPanel { Margin = new Thickness(14), Width = 450 }; window.Content = panel;
        panel.Children.Add(new TextBlock { Text = $"{name}: {channels[0].Length / (double)rate:0.0} s. Two ways to make it 8-bit:", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(new TextBlock { Text = "1. Turn it into an 8-bit song", FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Its notes, chords, bass and drums are found and played on square, triangle and noise instruments, like a chiptune cover. It opens in the Music maker to tidy up, change instruments or save.", TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 2, 0, 4) });
        var song = new Button { Content = "Turn into an 8-bit song…", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 2, 10, 2) };
        song.Click += (_, _) => { window.Close(); OpenMusicMaker(start: new Song { Name = PixelEditor.SafeName(name), Bars = 4, Tracks = [] }, transcribe: source); };
        panel.Children.Add(song);
        panel.Children.Add(new TextBlock { Text = "2. Crunch the recording", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 0) });
        panel.Children.Add(new TextBlock { Text = "Keeps the sound itself (voices too) but makes it lo-fi. For the real old-console sound go low: 4–8 kHz and 2–5 bits.", TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 2, 0, 4) });
        int[] rateValues = [4000, 6000, 8000, 11025, 16000, 22050]; int[] bitValues = [2, 3, 4, 5, 6, 8];
        var rates = new ComboBox { ItemsSource = rateValues.Select(r => r + " Hz").ToArray(), SelectedIndex = 2 };
        var bits = new ComboBox { ItemsSource = bitValues.Select(b => b + " bits").ToArray(), SelectedIndex = 2, Margin = new Thickness(6, 0, 0, 0) };
        var presets = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var (label, r, b, tip) in new[] { ("NES", 8000, 5, "Like the NES sample channel"), ("Game Boy", 8000, 4, "Like the Game Boy's wave channel"), ("Atari", 4000, 3, "Rough and buzzy, like the Atari 2600"), ("Arcade", 11025, 6, "Like an 80s arcade board") })
        {
            var p = new Button { Content = label, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(8, 1, 8, 1), ToolTip = tip + $" ({r} Hz, {b} bits)" };
            p.Click += (_, _) => { rates.SelectedIndex = Array.IndexOf(rateValues, r); bits.SelectedIndex = Array.IndexOf(bitValues, b); };
            presets.Children.Add(p);
        }
        panel.Children.Add(presets);
        var settings = new StackPanel { Orientation = Orientation.Horizontal }; settings.Children.Add(rates); settings.Children.Add(bits); panel.Children.Add(settings);
        var vocals = new CheckBox { Content = "Remove centre vocals", Margin = new Thickness(0, 6, 0, 0), IsEnabled = channels.Length > 1, ToolTip = "Takes out what is mixed in the middle, where singers usually are (on many studio mixes). The bass and kick drum are usually in the middle too, so they get quieter." };
        panel.Children.Add(vocals);
        var nameBox = new TextBox { Text = PixelEditor.SafeName(name + "_8bit"), Margin = new Thickness(0, 6, 0, 0) }; panel.Children.Add(nameBox);
        var format = new ComboBox { ItemsSource = AudioFiles.Formats, SelectedItem = "ogg", Margin = new Thickness(0, 4, 0, 0) }; panel.Children.Add(format);
        float[] Source() { if (vocals.IsChecked == true && channels.Length > 1) { var side = new float[channels[0].Length]; for (int i = 0; i < side.Length; i++) side[i] = (channels[0][i] - channels[1][i]) * 0.7f; return side; } return AudioTools.Mono(channels); }
        float[] Crunched() => AudioTools.Crunch(Source(), rate, rateValues[rates.SelectedIndex], bitValues[bits.SelectedIndex]);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) }; panel.Children.Add(row);
        var hear = new Button { Content = "▶ Hear it", Padding = new Thickness(10, 2, 10, 2) };
        hear.Click += (_, _) => Guard(() => { AssetAudio.StopAll(); AssetAudio.Play(AudioTools.Hold(Crunched(), rateValues[rates.SelectedIndex], AudioOut.Rate)); });
        var stop = new Button { Content = "■", Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) }; stop.Click += (_, _) => AssetAudio.StopAll();
        var saveIt = new Button { Content = "Save to Assets", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2) };
        saveIt.Click += (_, _) => Guard(() =>
        {
            // Stored at the app's usual rate, stepped (not smoothed) so the crunch survives.
            var samples = AudioTools.Hold(Crunched(), rateValues[rates.SelectedIndex], SongRenderer.SampleRate);
            SaveSoundAsset(nameBox.Text, (string)format.SelectedItem, true, null, [samples]); window.Close();
        });
        row.Children.Add(hear); row.Children.Add(stop); row.Children.Add(saveIt);
        window.Closed += (_, _) => AssetAudio.StopAll();
        window.ShowDialog();
    }

    // ---- Assets right-click menu: files ----
    /// <summary>Assets live inside the project file, so a copy is written to %LOCALAPPDATA%\Wysicraft\Asset files\(project)
    /// (refreshed each time) and Explorer opens with it selected.</summary>
    void ShowAssetInExplorer(string path)
    {
        string folder = Wysicraft.Core.AppFolders.Path("Asset files", project.Manifest.Id);
        string file = Path.Combine(folder, Path.GetFileName(path));
        Directory.CreateDirectory(folder); File.WriteAllBytes(file, project.Assets[path]);
        System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + file + "\"");
        Log("Saved a copy of " + Path.GetFileName(path) + " to " + folder + ". Edit it, then use Replace in Assets to put it back into the project.");
    }
    void SaveAssetCopy(string path)
    {
        string ext = Path.GetExtension(path);
        var dialog = new SaveFileDialog { FileName = Path.GetFileName(path), Filter = (ext.TrimStart('.').ToUpperInvariant()) + " file|*" + ext + "|All files|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllBytes(dialog.FileName, project.Assets[path]); Log("Saved a copy of " + Path.GetFileName(path) + " as " + dialog.FileName + ".");
    }

    // ---- Assets right-click menu: sounds ----
    void AddSoundMenuItems(ContextMenu menu, string path)
    {
        MenuItem Item(string header, Action run, string tip = "") { var i = new MenuItem { Header = header, ToolTip = tip.Length > 0 ? tip : null }; i.Click += (_, _) => Guard(run); menu.Items.Add(i); return i; }
        Item("▶ Play", () => { AssetAudio.StopAll(); var (ch, rate) = DecodeSoundAsset(path); var l = AudioTools.Resample(ch[0], rate, AudioOut.Rate); var r = ch.Length > 1 ? AudioTools.Resample(ch[1], rate, AudioOut.Rate) : l; AssetAudio.Play(l, r); });
        Item("■ Stop", () => AssetAudio.StopAll());
        menu.Items.Add(new Separator());
        if (project.Assets.ContainsKey(path + SoundAssets.SongSuffix)) Item("Open in music maker", () => OpenMusicMaker(path), "Change the song's notes, layers and instruments");
        if (project.Assets.ContainsKey(path + SoundAssets.EffectSuffix)) Item("Open in sound effect maker", () => OpenSoundEffectMaker(path));
        if (!project.Assets.ContainsKey(path + SoundAssets.SongSuffix))
            Item("Turn into an 8-bit song…", () => { var (ch, rate) = DecodeSoundAsset(path); OpenMusicMaker(start: new Song { Name = PixelEditor.SafeName(Path.GetFileNameWithoutExtension(path) + "_8bit"), Bars = 4, Tracks = [] }, transcribe: (ch, rate, Path.GetFileNameWithoutExtension(path))); }, "Find its notes, chords, bass and drums and play them on chip instruments (a chiptune cover), in the Music maker");
        Item("Crunch to lo-fi…", () => { var (ch, rate) = DecodeSoundAsset(path); CrunchDialog((ch, rate, Path.GetFileNameWithoutExtension(path))); }, "Keep the recording but make it sound like an old console sample");
        menu.Items.Add(new Separator());
        Item("Add to screen", () => { AddControl("sound", 0, 0); var sound = ui.Elements.First(e => selected.Contains(e.Id)); sound.Sound = SoundAssets.Resource(path); RefreshInspector(); RefreshLayers(); }, "Adds a Sound control playing it to the current screen (it has no picture; find it in Layers)");
        Item("Use in the selected Sound control", AssignBrowserAsset, "Sets the selected Sound control's sound (or adds a new Sound control)");
    }
}

public partial class MainWindow
{
    /// <summary>Opens the Music maker, Sheet music editor and Sound effect maker, draws them, adds and plays a note, and
    /// closes them again: they build and lay out without errors.</summary>
    internal void VerifySoundMakers(string output)
    {
        MusicMaker.NoPrompts = true;
        var song = new Song { Name = "smoke", Tracks = [new Wysicraft.Core.Audio.Track { Name = "Melody", Instrument = Instruments.Get("Square lead"), Notes = MusicText.Parse("C4 q E4 q G4 q C5 q", 4) }, new Wysicraft.Core.Audio.Track { Name = "Drums", Instrument = Instruments.Get("Drum kit"), Notes = MusicText.Parse("C2 q F#2 q D2 q F#2 q", 4) }] };
        var maker = new MusicMaker(this, song, null, SaveSong, () => null); maker.Show(); maker.UpdateLayout();
        maker.AddNote(8, 67, 4); maker.Relayout(); maker.UpdateLayout();
        if (song.Tracks[0].Notes.Count != 5) throw new Exception("Music maker didn't add the note");
        var sheet = SheetMusicEditor.Open(maker, song, 0, maker.audio); sheet.UpdateLayout(); sheet.Close();
        var drums = SheetMusicEditor.Open(maker, song, 1, maker.audio); drums.UpdateLayout(); drums.Close();
        maker.Close();
        var fx = new SoundEffectMaker(this, SoundEffect.Preset("laser"), null, SaveEffect); fx.Show(); fx.UpdateLayout(); fx.Close();
        // The same song with the bundled SoundFont: layers list the font's instruments, and it renders.
        if (!SoundFonts.Installed().Contains("GeneralUser GS")) throw new Exception("The bundled SoundFont is missing");
        song.SoundFont = "GeneralUser GS";
        var fontMaker = new MusicMaker(this, song, null, SaveSong, () => null); fontMaker.Show(); fontMaker.UpdateLayout();
        var lists = new List<IEnumerable<string>>(); var queue = new Queue<System.Windows.DependencyObject>([fontMaker]);
        while (queue.Count > 0) { var node = queue.Dequeue(); if (node is System.Windows.Controls.ComboBox { ItemsSource: IEnumerable<string> items }) lists.Add(items); for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++) queue.Enqueue(System.Windows.Media.VisualTreeHelper.GetChild(node, i)); }
        if (!lists.Any(i => i.Any(n => n.StartsWith("1. "))) || !lists.Any(i => i.Any(n => n.StartsWith("Drums: ")))) throw new Exception("Layers don't list the SoundFont's instruments");
        fontMaker.Close();
        var (fl, _) = SongRenderer.Render(song);
        if (AudioTools.Peak(fl) < 0.01) throw new Exception("The SoundFont song rendered silent");
        // Beside, not in front: opening one returns straight away (a dialog would block here) and the editor and the
        // main window are both usable, so several can be open at once while the project keeps working.
        OpenMusicMaker(); OpenSoundEffectMaker();
        if (SideEditorCount != 2) throw new Exception("The makers didn't open beside the main window");
        if (!IsEnabled || sideEditors.Any(w => !w.IsEnabled)) throw new Exception("A window was left disabled");
        if (!CloseSideEditors() || SideEditorCount != 0) throw new Exception("The makers didn't close with the project");
        File.WriteAllText(output, "PASS: music maker (layers, keyboard, piano roll, adding a note), sheet music editor (melody and drums), sound effect maker open and draw, SoundFont layers list and render, makers open beside the main window and close with the project");
    }
}
