namespace Wysicraft.Core.Audio;

/// <summary>Standard MIDI files (.mid) to and from songs. Each MIDI track and channel becomes a layer; General MIDI
/// instruments map to the nearest 8-bit preset, and channel 10 becomes the drum kit. Notes snap to the song's grid.</summary>
public static class Midi
{
    // General MIDI program families (8 programs each) → preset names.
    static readonly string[] Families =
    [
        "Piano", "Music box", "Organ", "Guitar", "Triangle bass", "Strings", "Strings", "Saw brass",
        "Pulse 25%", "Sine flute", "Square lead", "Soft pad", "Pulse 12%", "Pluck", "Bell", "Noise"
    ];
    static readonly Dictionary<string, int> Programs = new()
    {
        ["Pulse 25%"] = 0, ["Bell"] = 8, ["Square lead"] = 80, ["Pluck"] = 24, ["Triangle bass"] = 33, ["Soft pad"] = 88, ["Saw brass"] = 61, ["Sine flute"] = 73,
        ["Pulse 12%"] = 81, ["Noise"] = 122, ["Chip chord"] = 80, ["Minor chord"] = 80, ["Laser bass"] = 38,
        ["Piano"] = 0, ["Organ"] = 16, ["Guitar"] = 24, ["Harp"] = 46, ["Strings"] = 48, ["Choir"] = 52, ["Music box"] = 10
    };
    // Bard track names, as FFXIV bard players (Bard Music Player, MidiBard) name them: the in-game instrument, sometimes
    // with an octave shift ("Lute+1"). Each maps to the nearest preset; the percussion ones to a drum of the kit.
    // Longer names come first so "ElectricGuitarClean" isn't taken for "Guitar".
    static readonly (string Bard, string Preset, int Drum)[] BardNames =
    [
        ("ElectricGuitarOverdriven", "Guitar", 0), ("ElectricGuitarPowerChords", "Guitar", 0), ("ElectricGuitarClean", "Guitar", 0), ("ElectricGuitarMuted", "Pluck", 0),
        ("ElectricGuitarSpecial", "Guitar", 0), ("ElectricGuitar", "Guitar", 0), ("SnareDrum", "Drum kit", 38), ("BassDrum", "Drum kit", 36), ("DoubleBass", "Triangle bass", 0),
        ("Saxophone", "Saw brass", 0), ("Trombone", "Saw brass", 0), ("Clarinet", "Pulse 25%", 0), ("Panpipes", "Sine flute", 0), ("Timpani", "Triangle bass", 0),
        ("Trumpet", "Saw brass", 0), ("Cymbal", "Drum kit", 49), ("Fiddle", "Pluck", 0), ("Violin", "Strings", 0), ("Guitar", "Guitar", 0), ("Bongo", "Drum kit", 45),
        ("Flute", "Sine flute", 0), ("Piano", "Piano", 0), ("Cello", "Strings", 0), ("Viola", "Strings", 0), ("Horn", "Saw brass", 0), ("Harp", "Harp", 0),
        ("Lute", "Pluck", 0), ("Oboe", "Pulse 12%", 0), ("Fife", "Square lead", 0), ("Tuba", "Triangle bass", 0),
    ];
    // Bard instruments as General MIDI programs, for SoundFont playback.
    static readonly (string Bard, int Program)[] BardPrograms =
    [
        ("ElectricGuitarOverdriven", 29), ("ElectricGuitarPowerChords", 30), ("ElectricGuitarClean", 27), ("ElectricGuitarMuted", 28), ("ElectricGuitarSpecial", 31),
        ("ElectricGuitar", 29), ("DoubleBass", 43), ("Saxophone", 65), ("Trombone", 57), ("Clarinet", 71), ("Panpipes", 75), ("Timpani", 47), ("Trumpet", 56),
        ("Fiddle", 45), ("Violin", 40), ("Guitar", 24), ("Flute", 73), ("Piano", 0), ("Cello", 42), ("Viola", 41), ("Horn", 60), ("Harp", 46), ("Lute", 24),
        ("Oboe", 68), ("Fife", 72), ("Tuba", 58),
    ];
    static int? BardProgram(string name)
    {
        var letters = new string(name.Where(char.IsLetter).ToArray());
        if (letters.StartsWith("Program", StringComparison.OrdinalIgnoreCase)) letters = letters[7..];
        foreach (var (bard, program) in BardPrograms) if (letters.StartsWith(bard, StringComparison.OrdinalIgnoreCase)) return program;
        return null;
    }
    /// <summary>A bard instrument track name ("Harp", "Lute+1", "Program:ElectricGuitarClean"): its preset, the
    /// octaves to move its notes by, and for a percussion instrument the drum it plays (0 for pitched ones).</summary>
    public static bool BardTrack(string name, out Instrument instrument, out int octaves, out int drum)
    {
        instrument = Instruments.Get("Square lead"); octaves = 0; drum = 0;
        var letters = new string(name.Where(char.IsLetter).ToArray());
        if (letters.StartsWith("Program", StringComparison.OrdinalIgnoreCase)) letters = letters[7..];
        foreach (var (bard, preset, kit) in BardNames)
        {
            if (!letters.StartsWith(bard, StringComparison.OrdinalIgnoreCase)) continue;
            var shift = System.Text.RegularExpressions.Regex.Match(name, @"([+-])\s*([1-4])");
            octaves = shift.Success ? (shift.Groups[1].Value == "-" ? -1 : 1) * (shift.Groups[2].Value[0] - '0') : 0;
            instrument = Instruments.Get(preset); drum = kit;
            return true;
        }
        return false;
    }
    /// <summary>The General MIDI program nearest a chip preset (for SoundFont playback and MIDI export).</summary>
    public static int ProgramFor(string preset) => Programs.TryGetValue(preset, out var p) ? p : 80;
    public static Instrument ForProgram(int program) => Instruments.Get(program switch { 46 => "Harp", >= 52 and <= 54 => "Choir", _ => Families[Math.Clamp(program, 0, 127) / 8] });

    public static Song Read(byte[] data, int stepsPerBeat = 4, string name = "song")
    {
        int pos = 0;
        string Tag() { var s = System.Text.Encoding.ASCII.GetString(data, pos, 4); pos += 4; return s; }
        int U32() { int v = data[pos] << 24 | data[pos + 1] << 16 | data[pos + 2] << 8 | data[pos + 3]; pos += 4; return v; }
        int U16() { int v = data[pos] << 8 | data[pos + 1]; pos += 2; return v; }
        if (data.Length < 14 || Tag() != "MThd") throw new InvalidDataException("Not a MIDI file.");
        int headerLength = U32(); int format = U16(), trackCount = U16(), division = U16(); pos = 8 + headerLength;
        if ((division & 0x8000) != 0) throw new InvalidDataException("MIDI files timed in SMPTE frames aren't supported.");
        if (format > 1) throw new InvalidDataException("Only MIDI format 0 and 1 files can be imported.");
        var song = new Song { Name = name, StepsPerBeat = stepsPerBeat }; bool tempoSet = false, meterSet = false, keySet = false;
        // (track, channel) → layer notes
        var layers = new Dictionary<(int, int), MidiLayer>();
        MidiLayer Layer(int track, int channel) { if (!layers.TryGetValue((track, channel), out var l)) layers[(track, channel)] = l = new MidiLayer(); return l; }
        var trackNames = new Dictionary<int, string>();
        for (int t = 0; t < trackCount && pos + 8 <= data.Length; t++)
        {
            if (Tag() != "MTrk") throw new InvalidDataException("A MIDI track is damaged.");
            int length = U32(), end = Math.Min(data.Length, pos + length); long tick = 0; int status = 0;
            var programs = new int[16]; var open = new Dictionary<(int, int), (long, int)>();
            while (pos < end)
            {
                tick += Vlq(data, ref pos);
                int b = data[pos];
                if (b >= 0x80) { status = b; pos++; } else if (status == 0) throw new InvalidDataException("A MIDI track is damaged.");
                if (status == 0xFF)
                {
                    int type = data[pos++]; int len = (int)Vlq(data, ref pos);
                    if (type == 0x51 && len == 3 && !tempoSet) { int us = data[pos] << 16 | data[pos + 1] << 8 | data[pos + 2]; song.Bpm = Math.Round(Math.Clamp(60_000_000.0 / Math.Max(1, us), 20, 400), 1); tempoSet = true; }
                    else if (type == 0x58 && len >= 2 && !meterSet) { song.Numerator = Math.Clamp((int)data[pos], 1, 16); song.Denominator = 1 << Math.Clamp((int)data[pos + 1], 1, 4); meterSet = true; }
                    else if (type == 0x59 && len >= 2 && !keySet) { (song.Key, song.Scale) = KeyFrom((sbyte)data[pos], data[pos + 1] == 1); keySet = true; }
                    else if (type == 0x03 && len > 0) trackNames[t] = System.Text.Encoding.UTF8.GetString(data, pos, len).Trim();
                    pos += len; status = 0; continue;
                }
                if (status is 0xF0 or 0xF7) { int len = (int)Vlq(data, ref pos); pos += len; status = 0; continue; }
                int kind = status & 0xF0, channel = status & 0x0F;
                int d1 = data[pos++], d2 = kind is 0xC0 or 0xD0 ? 0 : data[pos++];
                if (kind == 0xC0) programs[channel] = d1;
                else if (kind == 0x90 && d2 > 0) { open[(channel, d1)] = (tick, d2); Layer(t, channel).Program = programs[channel]; }
                else if (kind == 0x80 || kind == 0x90)
                    if (open.Remove((channel, d1), out var started)) Layer(t, channel).Notes.Add((started.Item1, tick, d1, started.Item2));
            }
            pos = end;
        }
        double ticksPerStep = division / (double)stepsPerBeat;
        foreach (var ((track, channel), layer) in layers.OrderBy(p => p.Key.Item1).ThenBy(p => p.Key.Item2))
        {
            if (layer.Notes.Count == 0) continue;
            if (song.Tracks.Count >= 32) break;
            string title = trackNames.TryGetValue(track, out var tn) && tn.Length > 0 ? tn : "";
            int octaves = 0, drum = 0;
            var instrument = channel == 9 ? Instruments.Get("Drum kit") : BardTrack(title, out var bardSound, out octaves, out drum) ? bardSound : ForProgram(layer.Program);
            // The file's own instrument, for SoundFont playback (bard tracks by their name).
            if (channel == 9 || drum > 0) { instrument.Program = channel == 9 ? layer.Program : 0; instrument.Bank = 128; }
            else instrument.Program = BardProgram(title) ?? layer.Program;
            if (title.Length == 0) title = channel == 9 ? "Drums" : instrument.Name;
            var t = new Track { Name = title, Instrument = instrument, Volume = channel == 9 ? 0.7 : 0.6 };
            foreach (var (start, end, pitch, velocity) in layer.Notes)
            {
                int step = (int)Math.Round(start / ticksPerStep), len = Math.Max(1, (int)Math.Round((end - start) / ticksPerStep));
                t.Notes.Add(new Note { Step = step, Length = len, Pitch = drum > 0 ? drum : Math.Clamp(pitch + 12 * octaves, 0, 127), Velocity = Math.Round(velocity / 127.0, 2) });
            }
            t.Notes = t.Notes.GroupBy(n => (n.Step, n.Pitch)).Select(g => g.OrderByDescending(n => n.Length).First()).OrderBy(n => n.Step).ToList();
            song.Tracks.Add(t);
        }
        if (song.Tracks.Count == 0) throw new InvalidDataException("The MIDI file has no notes.");
        song.Bars = Math.Min(512, song.BarsNeeded());
        return song;
    }
    /// <summary>Re-voices a song for chip sound: drums stay on the kit, the highest part becomes the square-wave lead,
    /// low parts the triangle bass, the rest pulse waves. With <paramref name="split"/>, a part that plays melody and
    /// chords at once (a bard's solo harp or piano arrangement) is first split into its melody (the notes on top of the
    /// chords), its chords and its bass (the bottom notes, when low).</summary>
    public static void Chipify(Song song, bool split)
    {
        var parts = new List<Track>();
        foreach (var t in song.Tracks)
        {
            if (t.Instrument.Wave == "drums" || !split || !Polyphonic(t)) { parts.Add(t); continue; }
            var groups = t.Notes.GroupBy(n => n.Step).OrderBy(g => g.Key).Select(g => g.OrderByDescending(n => n.Pitch).ToList()).ToList();
            // The top of the chords around each step: the second-highest note of every chord of three or more.
            var tops = groups.Where(g => g.Count >= 3).Select(g => (Step: g[0].Step, Pitch: g[1].Pitch)).ToList();
            // Where no chords are near (a riff of single notes), the part's usual chord height decides instead.
            int window = song.StepsPerBar * 2, usual = tops.Count > 0 ? tops.Select(c => c.Pitch).OrderBy(p => p).ElementAt(tops.Count / 2) : -1;
            int Ceiling(int step) { int top = -1; foreach (var c in tops) if (Math.Abs(c.Step - step) <= window && c.Pitch > top) top = c.Pitch; return top < 0 ? usual : top; }
            var lead = new List<Note>(); var chords = new List<Note>(); var bass = new List<Note>();
            foreach (var g in groups)
            {
                int from = 0;
                if (g[0].Pitch > Ceiling(g[0].Step)) { lead.Add(g[0]); from = 1; }
                var rest = g.Skip(from).ToList();
                if (rest.Count >= 2 && rest[^1].Pitch <= 52) { bass.Add(rest[^1]); rest.RemoveAt(rest.Count - 1); }
                chords.AddRange(rest);
            }
            void Part(string role, List<Note> notes) { if (notes.Count > 0) parts.Add(new Track { Name = $"{t.Name} {role}", Instrument = t.Instrument.Copy(), Notes = notes, Volume = t.Volume, Pan = t.Pan }); }
            Part("melody", lead); Part("chords", chords); Part("bass", bass);
        }
        // Roles by pitch: the highest-sounding part leads.
        var melodic = parts.Where(p => p.Instrument.Wave != "drums" && p.Notes.Count > 0).ToList();
        double Average(Track p) => p.Notes.Average(n => n.Pitch);
        var leadPart = melodic.Where(p => !p.Name.EndsWith(" chords") && !p.Name.EndsWith(" bass")).OrderByDescending(Average).FirstOrDefault();
        int pulse = 0;
        foreach (var p in melodic)
        {
            string preset = p == leadPart ? "Square lead" : p.Name.EndsWith(" bass") || Average(p) < 50 ? "Triangle bass" : pulse++ % 2 == 0 ? "Pulse 25%" : "Pulse 12%";
            p.Instrument = Instruments.Get(preset);
            p.Volume = preset == "Square lead" ? 0.7 : preset == "Triangle bass" ? 0.85 : Polyphonic(p) ? 0.4 : 0.55;
        }
        song.Tracks = parts.Take(32).ToList();
    }
    // Plays chords: on average more than one and a half notes start together.
    static bool Polyphonic(Track t) => t.Notes.Count > 0 && t.Notes.Count / (double)t.Notes.Select(n => n.Step).Distinct().Count() > 1.5;

    sealed class MidiLayer { public int Program; public readonly List<(long Start, long End, int Pitch, int Velocity)> Notes = []; }
    static long Vlq(byte[] d, ref int pos) { long v = 0; for (int i = 0; i < 4; i++) { byte b = d[pos++]; v = v << 7 | (uint)(b & 0x7F); if ((b & 0x80) == 0) break; } return v; }
    static readonly string[] MajorBySharps = ["Cb", "Gb", "Db", "Ab", "Eb", "Bb", "F", "C", "G", "D", "A", "E", "B", "F#", "C#"];
    static (string, string) KeyFrom(int sharps, bool minor)
    {
        int root = MusicTheory.Parse(MajorBySharps[Math.Clamp(sharps + 7, 0, 14)] + "4") % 12;
        if (minor) root = (root + 9) % 12;
        return (MusicTheory.Keys[root], minor ? "minor" : "major");
    }

    /// <summary>A format 1 MIDI file: a tempo track, then one track per layer (drums on channel 10).</summary>
    public static byte[] Write(Song song)
    {
        const int division = 480; int ticksPerStep = division / Math.Max(1, song.StepsPerBeat);
        var tracks = new List<byte[]>();
        var meta = new List<byte>();
        int us = (int)Math.Round(60_000_000 / song.Bpm);
        meta.AddRange([0, 0xFF, 0x51, 3, (byte)(us >> 16), (byte)(us >> 8), (byte)us]);
        meta.AddRange([0, 0xFF, 0x58, 4, (byte)song.Numerator, (byte)Math.Log2(song.Denominator), 24, 8]);
        meta.AddRange([0, 0xFF, 0x2F, 0]); tracks.Add(meta.ToArray());
        int channel = 0;
        foreach (var t in song.Tracks)
        {
            bool drums = t.Instrument.Wave == "drums"; int ch = drums ? 9 : channel++ % 16; if (!drums && ch == 9) ch = channel++ % 16;
            var events = new List<(long Tick, int Order, byte[] Bytes)>();
            var name = System.Text.Encoding.UTF8.GetBytes(t.Name); var head = new List<byte> { 0xFF, 0x03 }; head.AddRange(Vlq(name.Length)); head.AddRange(name);
            events.Add((0, 0, head.ToArray()));
            if (!drums) events.Add((0, 1, [(byte)(0xC0 | ch), (byte)(t.Instrument.Program >= 0 ? t.Instrument.Program : ProgramFor(t.Instrument.Name))]));
            foreach (var n in t.Notes)
            {
                int pitch = Math.Clamp(n.Pitch + (drums ? 0 : t.Transpose + t.Instrument.Octave * 12), 0, 127), vel = Math.Clamp((int)Math.Round(n.Velocity * 127), 1, 127);
                events.Add(((long)n.Step * ticksPerStep, 3, [(byte)(0x90 | ch), (byte)pitch, (byte)vel]));
                events.Add(((long)(n.Step + n.Length) * ticksPerStep, 2, [(byte)(0x80 | ch), (byte)pitch, 0]));
            }
            var body = new List<byte>(); long last = 0;
            foreach (var e in events.OrderBy(e => e.Tick).ThenBy(e => e.Order)) { body.AddRange(Vlq(e.Tick - last)); body.AddRange(e.Bytes); last = e.Tick; }
            body.AddRange([0, 0xFF, 0x2F, 0]); tracks.Add(body.ToArray());
        }
        var file = new List<byte>(); file.AddRange("MThd"u8.ToArray()); file.AddRange(BE(6)); file.AddRange([0, 1, (byte)(tracks.Count >> 8), (byte)tracks.Count, division >> 8, division & 0xFF]);
        foreach (var t in tracks) { file.AddRange("MTrk"u8.ToArray()); file.AddRange(BE(t.Length)); file.AddRange(t); }
        return file.ToArray();
    }
    static byte[] BE(int v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];
    static byte[] Vlq(long v)
    {
        var bytes = new List<byte> { (byte)(v & 0x7F) }; v >>= 7;
        while (v > 0) { bytes.Insert(0, (byte)(v & 0x7F | 0x80)); v >>= 7; }
        return bytes.ToArray();
    }
}
