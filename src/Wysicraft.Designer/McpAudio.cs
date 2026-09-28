using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using Wysicraft.Core;
using Wysicraft.Core.Audio;
using Wysicraft.Models;
namespace Wysicraft.Designer;

/// <summary>A song for compose_music. Leave a field out to keep it (when editing a song) or use the default.</summary>
public sealed class SongSpec
{
    [Description("Tempo, 20-400 beats (quarter notes) per minute. Default 120.")] public double? Bpm { get; set; }
    [Description("Like \"4/4\", \"3/4\", \"6/8\". Default 4/4.")] public string? TimeSignature { get; set; }
    [Description("Grid steps per quarter note: 2, 4 (sixteenths, default) or 8.")] public int? StepsPerBeat { get; set; }
    [Description("Key for sheet music and highlighting: C, C#, D … B. Default C.")] public string? Key { get; set; }
    [Description("major, minor, harmonic minor, major pentatonic, minor pentatonic, blues, dorian, mixolydian or chromatic.")] public string? Scale { get; set; }
    [Description("Length in bars; 0 or left out = just enough for the notes.")] public int? Bars { get; set; }
    [Description("Master volume 0-1 (default 0.8).")] public double? Volume { get; set; }
    [Description("Save so it loops seamlessly (notes ringing past the end continue at the start). For background music.")] public bool? Loop { get; set; }
    [Description("The layers. Given: they replace the song's layers (or are added, with addTracks). Left out: the layers stay.")] public List<TrackSpec>? Tracks { get; set; }
}
public sealed class TrackSpec
{
    public string? Name { get; set; }
    [Description("A preset: Square lead, Pulse 25%, Pulse 12%, Triangle bass, Saw brass, Sine flute, Pluck, Bell, Chip chord, Minor chord, Laser bass, Soft pad, Piano, Organ, Guitar, Harp, Strings, Choir, Music box, Noise, Drum kit.")] public string? Instrument { get; set; }
    [Description("Changes to the instrument: wave (square|triangle|saw|sine|noise|drums), duty, attack, decay, sustain, release, vibrato, vibratoRate, vibratoDelay, slide, slideTime, arpeggio (\"0,4,7\"), arpeggioRate, crush, echo, echoFeedback, volume, octave.")] public JsonElement? InstrumentSettings { get; set; }
    [Description("0-1.")] public double? Volume { get; set; }
    [Description("-1 (left) to 1 (right).")] public double? Pan { get; set; }
    [Description("Semitones added to every note.")] public int? Transpose { get; set; }
    public bool? Mute { get; set; }
    [Description("Notes as text: pitch then length, e.g. \"C4 q E4 e G4 e | C5 h r q C4+E4+G4 w\". Lengths w h q e s t (dot for dotted, e.g. q.) or steps like 3s; r = rest; A+B = chord; | bar lines are ignored; [n] jumps to step n; pitch@0.6 = velocity. For the Drum kit use C2 kick, D2 snare, D#2 clap, F#2 closed hat, A#2 open hat, C#3 crash, D#3 ride, F2/A2/C3 toms.")] public string? Notes { get; set; }
    [Description("Or notes as a list, added after any text notes.")] public List<NoteSpec>? NoteList { get; set; }
}
public sealed class NoteSpec
{
    [Description("Start, in grid steps from the beginning (0 = first beat).")] public int Step { get; set; }
    [Description("Length in grid steps (default 1).")] public int Length { get; set; } = 1;
    [Description("Note name like C4, F#3, Bb5 or a MIDI number (60 = middle C).")] public string Pitch { get; set; } = "C4";
    [Description("0-1 (default 1).")] public double Velocity { get; set; } = 1;
}

// Music and sound effects for AI assistants: the same songs, instruments and effects as the Music maker and Sound effect
// maker, saved the same way (a sound plus what made it, so the makers can open it again).
public partial class MainWindow
{
    // A sound named by asset path, sound ID (ns:name) or file name. Null when there isn't one.
    string? SoundAssetPath(string sound)
    {
        if (sound.Length == 0) return null;
        if (project.Assets.ContainsKey(sound) && SoundAssets.IsSound(sound)) return sound;
        if (sound.Contains(':')) return SoundAssets.Find(project, sound);
        var matches = SoundAssets.All(project).Where(k => Path.GetFileName(k) == sound || Path.GetFileNameWithoutExtension(k) == sound).ToList();
        if (matches.Count > 1) throw new InvalidDataException($"Several sounds are called {sound}: {string.Join(", ", matches)}. Use the sound ID.");
        return matches.FirstOrDefault();
    }
    static T Patch<T>(T original, JsonElement? changes)
    {
        if (changes is not { ValueKind: JsonValueKind.Object } patch) return original;
        var node = JsonSerializer.SerializeToNode(original, Json.Options)!.AsObject();
        foreach (var p in patch.EnumerateObject())
        {
            string key = char.ToLowerInvariant(p.Name[0]) + p.Name[1..];
            if (!node.ContainsKey(key)) throw new InvalidDataException($"Unknown setting \"{p.Name}\". Known: {string.Join(", ", node.Select(n => n.Key))}.");
            node[key] = JsonNode.Parse(p.Value.GetRawText());
        }
        try { return node.Deserialize<T>(Json.Options)!; } catch (JsonException ex) { throw new InvalidDataException("A setting has the wrong kind of value: " + ex.Message); }
    }
    static int ParsePitch(string pitch)
    {
        if (int.TryParse(pitch, out var midi)) return midi is >= 0 and <= 127 ? midi : throw new InvalidDataException("MIDI notes are 0-127.");
        var drum = Instruments.Drums.FirstOrDefault(d => d.Name.Replace(" ", "").Equals(pitch.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
        if (drum.Name != null) return drum.Pitch;
        try { return MusicTheory.Parse(pitch); } catch (FormatException ex) { throw new InvalidDataException(ex.Message); }
    }
    object SongInfo(Song song, string? path) => new
    {
        sound = path != null ? SoundAssets.Resource(path) : null, path, song.Name, song.Bpm, timeSignature = song.Numerator + "/" + song.Denominator, song.StepsPerBeat, song.Key, song.Scale, song.Bars, song.Loop,
        seconds = Math.Round(song.Seconds, 2), stepsPerBar = song.StepsPerBar,
        tracks = song.Tracks.Select(t => new
        {
            t.Name, instrument = t.Instrument.Name, t.Volume, t.Pan, t.Transpose, t.Mute, noteCount = t.Notes.Count,
            notes = MusicText.Write(t.Notes, song.StepsPerBeat, song.StepsPerBar, MusicTheory.UsesFlats(song.Key, song.Scale))
        })
    };

    internal Task<string> McpComposeMusic(string expected, SongSpec? spec, string sound, string name, string format, bool addTracks, CancellationToken cancellationToken) => Dispatcher.InvokeAsync(() =>
    {
        try
        {
            if (mcpHost == null) throw new InvalidOperationException("MCP server is stopped.");
            CheckRevision(expected);
            string? path = SoundAssetPath(sound);
            if (sound.Length > 0 && path == null) throw new InvalidDataException($"There's no sound \"{sound}\" in the project.");
            Song song;
            if (path != null)
            {
                if (project.Assets.TryGetValue(path + SoundAssets.SongSuffix, out var saved)) song = Json.Read<Song>(Encoding.UTF8.GetString(saved));
                // A recording (or a sound from elsewhere) can be replaced by a new song that keeps its sound ID.
                else if (spec?.Tracks is { Count: > 0 }) song = new Song { Name = PixelEditor.SafeName(Path.GetFileNameWithoutExtension(path)) };
                else throw new InvalidDataException($"{Path.GetFileName(path)} wasn't made in the Music maker, so it has no notes to change. Give tracks to replace it with a new song (keeping its sound ID), or leave sound out to write a new one.");
            }
            else
            {
                if (name.Length == 0) throw new InvalidDataException("Give name for the new song (it becomes the sound ID, e.g. " + project.Manifest.Id + ":theme).");
                if (spec?.Tracks is not { Count: > 0 }) throw new InvalidDataException("A new song needs tracks, each with an instrument and notes.");
                song = new Song();
            }
            if (name.Length > 0) song.Name = PixelEditor.SafeName(name);
            spec ??= new SongSpec();
            int oldSteps = song.StepsPerBeat;
            if (spec.StepsPerBeat is int spb && spb != oldSteps)
            {
                if (spb is not (2 or 4 or 8)) throw new InvalidDataException("stepsPerBeat is 2, 4 or 8.");
                double k = spb / (double)oldSteps; foreach (var n in song.Tracks.SelectMany(t => t.Notes)) { n.Step = (int)Math.Round(n.Step * k); n.Length = Math.Max(1, (int)Math.Round(n.Length * k)); }
                song.StepsPerBeat = spb;
            }
            if (spec.Bpm is double bpm) song.Bpm = bpm;
            if (spec.TimeSignature is string ts)
            {
                var parts = ts.Split('/'); if (parts.Length != 2 || !int.TryParse(parts[0], out var num) || !int.TryParse(parts[1], out var den)) throw new InvalidDataException("timeSignature is like \"4/4\" or \"6/8\".");
                song.Numerator = num; song.Denominator = den;
            }
            if (spec.Key is string key) song.Key = key;
            if (spec.Scale is string scale) song.Scale = scale;
            if (spec.Volume is double volume) song.Volume = volume;
            if (spec.Loop is bool loop) song.Loop = loop;
            if (spec.Tracks != null)
            {
                var tracks = spec.Tracks.Select((t, i) =>
                {
                    var ins = Patch(Instruments.Get(t.Instrument ?? "Square lead"), t.InstrumentSettings);
                    if (t.Instrument != null && !Instruments.Presets.Any(p => p.Name.Equals(t.Instrument, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException($"Unknown instrument \"{t.Instrument}\". Presets: {string.Join(", ", Instruments.Presets.Select(p => p.Name))}.");
                    var track = new Wysicraft.Core.Audio.Track { Name = t.Name ?? ins.Name, Instrument = ins, Volume = t.Volume ?? 0.8, Pan = t.Pan ?? 0, Transpose = t.Transpose ?? 0, Mute = t.Mute ?? false };
                    try { if (t.Notes != null) track.Notes.AddRange(MusicText.Parse(t.Notes, song.StepsPerBeat)); }
                    catch (FormatException ex) { throw new InvalidDataException($"Track {i + 1} ({track.Name}): {ex.Message}"); }
                    foreach (var n in t.NoteList ?? []) track.Notes.Add(new Note { Step = n.Step, Length = n.Length, Pitch = ParsePitch(n.Pitch), Velocity = n.Velocity });
                    return track;
                }).ToList();
                if (addTracks) song.Tracks.AddRange(tracks); else song.Tracks = tracks;
            }
            song.Bars = spec.Bars is int bars && bars > 0 ? bars : Math.Min(512, song.BarsNeeded());
            song.Check();
            var (l, r) = SongRenderer.Render(song);
            if (AudioTools.Peak(l) + AudioTools.Peak(r) < 1e-4) throw new InvalidDataException("The song is silent: give tracks some notes.");
            string fmt = format.Length > 0 ? format : path != null ? Path.GetExtension(path).TrimStart('.') : "ogg";
            if (!AudioFiles.Formats.Contains(fmt)) throw new InvalidDataException("format is ogg (default; plays everywhere, Minecraft too) or wav.");
            string savedPath = SaveSoundAsset(song.Name, fmt, path == null, path, [l, r], SoundAssets.SongSuffix, Encoding.UTF8.GetBytes(Json.Write(song)), song.Loop);
            return Json.Write(new { revision = Revision(), song = SongInfo(song, savedPath), note = "Play it with a Sound control (sound = the sound ID, loop for music) or the play_sound action / ctx.client.playSound. The Music maker can open and change it." });
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException) { throw new ModelContextProtocol.McpException(ex.Message); }
    }, System.Windows.Threading.DispatcherPriority.Normal, cancellationToken).Task;

    internal Task<string> McpSoundEffect(string expected, string preset, JsonElement? settings, string sound, string name, string format, bool mutate, CancellationToken cancellationToken) => Dispatcher.InvokeAsync(() =>
    {
        try
        {
            if (mcpHost == null) throw new InvalidOperationException("MCP server is stopped.");
            CheckRevision(expected);
            string? path = SoundAssetPath(sound);
            if (sound.Length > 0 && path == null) throw new InvalidDataException($"There's no sound \"{sound}\" in the project.");
            SoundEffect fx;
            if (preset.Length > 0)
            {
                if (!SoundEffect.Presets.Contains(preset)) throw new InvalidDataException("preset is one of " + string.Join(", ", SoundEffect.Presets) + ".");
                fx = SoundEffect.Preset(preset);
            }
            else if (path != null)
            {
                if (!project.Assets.TryGetValue(path + SoundAssets.EffectSuffix, out var saved)) throw new InvalidDataException($"{Path.GetFileName(path)} wasn't made in the Sound effect maker. Give a preset to replace it, or leave sound out for a new one.");
                fx = Json.Read<SoundEffect>(Encoding.UTF8.GetString(saved));
            }
            else fx = SoundEffect.Preset("blip");
            fx = Patch(fx, settings);
            if (mutate) fx = fx.Mutate();
            if (name.Length > 0) fx.Name = PixelEditor.SafeName(name);
            else if (path == null) throw new InvalidDataException("Give name for the new sound (it becomes the sound ID, e.g. " + project.Manifest.Id + ":coin).");
            fx.Check();
            string fmt = format.Length > 0 ? format : path != null ? Path.GetExtension(path).TrimStart('.') : "ogg";
            if (!AudioFiles.Formats.Contains(fmt)) throw new InvalidDataException("format is ogg (default) or wav.");
            string savedPath = SaveSoundAsset(fx.Name, fmt, path == null, path, [fx.Render(SongRenderer.SampleRate)], SoundAssets.EffectSuffix, Encoding.UTF8.GetBytes(Json.Write(fx)));
            return Json.Write(new { revision = Revision(), sound = SoundAssets.Resource(savedPath), path = savedPath, seconds = Math.Round(fx.Seconds, 3), settings = fx, note = "Play it with ctx.client.playSound('" + SoundAssets.Resource(savedPath) + "') or a play_sound action. Pass sound and settings (or mutate) to adjust it." });
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException) { throw new ModelContextProtocol.McpException(ex.Message); }
    }, System.Windows.Threading.DispatcherPriority.Normal, cancellationToken).Task;

    internal async Task<string> McpSongFromAudio(string expected, string file, string sound, string name, double bpm, bool lead, bool chords, bool bass, bool drums, bool removeVocals, double sensitivity, string format, string style, string chordRhythm, string chordVoicing, CancellationToken cancellationToken)
    {
        // Read the recording on the UI thread (project state), convert it in the background, save on the UI thread.
        var (channels, rate, title) = await Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (mcpHost == null) throw new InvalidOperationException("MCP server is stopped.");
                CheckRevision(expected);
                if (file.Length > 0)
                {
                    if (!Path.IsPathFullyQualified(file) || !File.Exists(file)) throw new InvalidDataException("file must be the full path of an audio file on this computer.");
                    var (c, r) = DecodeAudio(File.ReadAllBytes(file), Path.GetExtension(file)); return (c, r, Path.GetFileNameWithoutExtension(file));
                }
                string at = SoundAssetPath(sound) ?? throw new InvalidDataException("Give file (a recording on this computer) or sound (a project sound).");
                var (ch, rt) = DecodeSoundAsset(at); return (ch, rt, Path.GetFileNameWithoutExtension(at));
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException) { throw new ModelContextProtocol.McpException(ex.Message); }
        }, System.Windows.Threading.DispatcherPriority.Normal, cancellationToken).Task;
        string songName = PixelEditor.SafeName(name.Length > 0 ? name : title + "_8bit");
        Song song;
        try
        {
            // Split into vocals, bass, drums and other first when the splitter has been downloaded (in the Music maker).
            var stems = StemSplitter.Available ? await Task.Run(() => StemSplitter.Separate(channels, rate), cancellationToken) : null;
            song = await Task.Run(() => SongConverter.Convert(channels, rate, songName, new SongConverter.Options(bpm, 4, lead, chords, bass, drums, Math.Clamp(sensitivity, 0, 1), removeVocals, Style: style == "notes" ? "notes" : "arrange", ChordRhythm: chordRhythm is "quarters" or "held" ? chordRhythm : "eighths", ChordVoicing: chordVoicing is "power" or "triads" ? chordVoicing : "auto"), null, stems), cancellationToken);
        }
        catch (InvalidOperationException ex) { throw new ModelContextProtocol.McpException(ex.Message); }
        return await Dispatcher.InvokeAsync(() =>
        {
            try
            {
                CheckRevision(expected);
                var (l, r) = SongRenderer.Render(song);
                string fmt = format.Length > 0 ? format : "ogg"; if (!AudioFiles.Formats.Contains(fmt)) throw new InvalidDataException("format is ogg (default) or wav.");
                string saved = SaveSoundAsset(song.Name, fmt, true, null, [l, r], SoundAssets.SongSuffix, Encoding.UTF8.GetBytes(Json.Write(song)), song.Loop);
                return Json.Write(new { revision = Revision(), song = SongInfo(song, saved), note = "An 8-bit cover: check it with read_music and tidy stray notes with compose_music (or open it in the Music maker)." });
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException) { throw new ModelContextProtocol.McpException(ex.Message); }
        }, System.Windows.Threading.DispatcherPriority.Normal, cancellationToken).Task;
    }

    internal Task<string> McpReadMusic(string sound, CancellationToken cancellationToken) => Dispatcher.InvokeAsync(() =>
    {
        try
        {
            string path = SoundAssetPath(sound) ?? throw new InvalidDataException($"There's no sound \"{sound}\" in the project.");
            if (project.Assets.TryGetValue(path + SoundAssets.SongSuffix, out var song)) return Json.Write(new { kind = "song", song = SongInfo(Json.Read<Song>(Encoding.UTF8.GetString(song)), path) });
            if (project.Assets.TryGetValue(path + SoundAssets.EffectSuffix, out var fx)) return Json.Write(new { kind = "sound effect", sound = SoundAssets.Resource(path), path, settings = Json.Read<SoundEffect>(Encoding.UTF8.GetString(fx)) });
            var (channels, rate) = DecodeSoundAsset(path);
            return Json.Write(new { kind = "recording", sound = SoundAssets.Resource(path), path, seconds = Math.Round(channels[0].Length / (double)rate, 2), channels = channels.Length, sampleRate = rate, note = "An imported recording: it has no notes. The Music maker can turn its melody into notes (right-click it in Assets)." });
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException) { throw new ModelContextProtocol.McpException(ex.Message); }
    }, System.Windows.Threading.DispatcherPriority.Normal, cancellationToken).Task;
}

public sealed partial class DesignerMcpTools
{
    [McpServerTool(Name = "compose_music"), Description("Write or change 8-bit music, like the Music maker, and save it as a project sound (Ogg by default; plays in Minecraft, web and desktop). New song: name + song {bpm, timeSignature, key, scale, tracks:[{name, instrument (preset), instrumentSettings, volume, pan, transpose, notes (text) or noteList}]}. Notes text: \"C4 q E4 e G4 e | C5 h r q C4+E4+G4 w\" (w h q e s t lengths, . dotted, r rest, + chord). Change a song: sound = its ID, and only the fields to change (tracks replace the layers, or addTracks adds them). A sound not made in the Music maker is replaced by the new song, keeping its ID. Set loop:true for background music. Returns the sound ID and every layer as text.")]
    public Task<string> ComposeMusic(string expectedRevision, SongSpec? song = null, string sound = "", string name = "", string format = "", bool addTracks = false, CancellationToken cancellationToken = default)
        => editor.McpComposeMusic(expectedRevision, song, sound, name, format, addTracks, cancellationToken);
    [McpServerTool(Name = "sound_effect"), Description("Make or change a retro sound effect, like the Sound effect maker, saved as a project sound. preset: coin, jump, laser, explosion, powerup, hurt, blip or random (a new random take each call). settings patch any of: wave (square|triangle|saw|sine|noise), frequency (Hz), minFrequency, slide (octaves/s), deltaSlide, vibratoDepth (semitones), vibratoSpeed, arpeggioSemitones, arpeggioTime (s), duty, dutySweep, attack, sustain, punch, decay (s), repeatTime, lowPass, lowPassSweep, highPass, crush, volume. sound = an existing effect to change; mutate:true for a close variation.")]
    public Task<string> MakeSoundEffect(string expectedRevision, string preset = "", JsonElement? settings = null, string sound = "", string name = "", string format = "", bool mutate = false, CancellationToken cancellationToken = default)
        => editor.McpSoundEffect(expectedRevision, preset, settings, sound, name, format, mutate, cancellationToken);
    [McpServerTool(Name = "song_from_audio"), Description("Turn a recording into an 8-bit song (a chiptune cover), like the Music maker's Song from audio: the beat is tracked through the song, the chord on every beat is named, the singer becomes the melody and the drums come from their hits. When the Demucs instrument splitter has been downloaded (the Music maker offers it on first import), the song is split into vocals, bass, drums and other first, which is much more accurate (a minute or two). style arrange (default: Chords, Bass, Melody, Drums layers; chordRhythm eighths|quarters|held, chordVoicing auto|power|triads) or notes (every note heard, for piano or solo pieces). file = full path of an audio file on this computer (MP3, WAV, Ogg, M4A…) or sound = a project sound. bpm 0 = detect. removeVocals takes out centre-mixed singing first (then the lead is the instruments). Saved as a new project sound with its song, so compose_music and the Music maker can change it.")]
    public Task<string> SongFromAudio(string expectedRevision, string file = "", string sound = "", string name = "", double bpm = 0, bool lead = true, bool chords = true, bool bass = true, bool drums = true, bool removeVocals = false, double sensitivity = 0.5, string format = "", string style = "arrange", string chordRhythm = "eighths", string chordVoicing = "auto", CancellationToken cancellationToken = default)
        => editor.McpSongFromAudio(expectedRevision, file, sound, name, bpm, lead, chords, bass, drums, removeVocals, sensitivity, format, style, chordRhythm, chordVoicing, cancellationToken);
    [McpServerTool(Name = "read_music", ReadOnly = true), Description("Read a project sound: a song's tempo, time signature, key and every layer's instrument and notes (as compose_music text), a sound effect's settings, or a recording's length.")]
    public Task<string> ReadMusic(string sound, CancellationToken cancellationToken = default) => editor.McpReadMusic(sound, cancellationToken);
}
