using System.Text;
using System.Text.RegularExpressions;
namespace Wysicraft.Core.Audio;

/// <summary>A song for the Music maker: layers (tracks) of notes on a grid of steps, each played by an 8-bit instrument.
/// A step is 1/StepsPerBeat of a quarter note; a bar is Numerator × (4 / Denominator) quarter notes.
/// Saved as JSON beside the rendered sound (name.ogg.song), so it can be opened and changed again.</summary>
public sealed class Song
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "song";
    public double Bpm { get; set; } = 120;
    public int Numerator { get; set; } = 4;
    public int Denominator { get; set; } = 4;
    public int StepsPerBeat { get; set; } = 4;
    public string Key { get; set; } = "C";
    public string Scale { get; set; } = "major";
    public int Bars { get; set; } = 4;
    public double Volume { get; set; } = 0.8;
    /// <summary>Loops seamlessly: notes ringing past the end wrap round to the start.</summary>
    public bool Loop { get; set; }
    /// <summary>The SoundFont the song is played with (see <see cref="SoundFonts"/>); empty for the built-in chip sounds.</summary>
    public string SoundFont { get; set; } = "";
    public List<Track> Tracks { get; set; } = [];

    public int StepsPerBar => Math.Max(1, (int)Math.Round(Numerator * 4.0 / Denominator * StepsPerBeat));
    public int TotalSteps => Math.Max(1, Bars) * StepsPerBar;
    public double StepSeconds => 60.0 / Math.Clamp(Bpm, 20, 400) / Math.Max(1, StepsPerBeat);
    public double Seconds => TotalSteps * StepSeconds;
    /// <summary>The bar count that fits every note (at least one bar).</summary>
    public int BarsNeeded() => Math.Max(1, (int)Math.Ceiling(Tracks.SelectMany(t => t.Notes).Select(n => n.Step + n.Length).DefaultIfEmpty(0).Max() / (double)StepsPerBar));

    public void Check()
    {
        if (!double.IsFinite(Bpm) || Bpm is < 20 or > 400) throw new InvalidDataException("Tempo is 20–400 BPM.");
        if (Numerator is < 1 or > 16 || Denominator is not (2 or 4 or 8 or 16)) throw new InvalidDataException("Time signature: 1–16 beats of 2, 4, 8 or 16.");
        if (StepsPerBeat is < 1 or > 16) throw new InvalidDataException("Steps per beat is 1–16.");
        if (Bars is < 1 or > 512) throw new InvalidDataException("A song is 1–512 bars.");
        if (Tracks.Count > 32) throw new InvalidDataException("At most 32 layers.");
        if (!MusicTheory.Keys.Contains(Key)) throw new InvalidDataException("Key must be one of " + string.Join(", ", MusicTheory.Keys) + ".");
        if (!MusicTheory.Scales.ContainsKey(Scale)) throw new InvalidDataException("Scale must be one of " + string.Join(", ", MusicTheory.Scales.Keys) + ".");
        if (Seconds > 600) throw new InvalidDataException("A song can be at most 10 minutes long.");
        foreach (var t in Tracks)
        {
            if (t.Notes.Count > 20000) throw new InvalidDataException($"Layer {t.Name} has too many notes (20000 at most).");
            t.Instrument.Check();
            foreach (var n in t.Notes) if (n.Pitch is < 0 or > 127 || n.Step < 0 || n.Length < 1 || !double.IsFinite(n.Velocity)) throw new InvalidDataException($"Layer {t.Name} has a note outside the range (pitch 0–127, step ≥ 0, length ≥ 1).");
        }
    }
}

public sealed class Track
{
    public string Name { get; set; } = "Layer";
    public Instrument Instrument { get; set; } = Instruments.Get("Square lead");
    public double Volume { get; set; } = 0.8;
    /// <summary>-1 (left) to 1 (right).</summary>
    public double Pan { get; set; }
    /// <summary>Semitones added to every note (a pitch knob).</summary>
    public int Transpose { get; set; }
    /// <summary>Cents (hundredths of a semitone) for fine tuning.</summary>
    public double Fine { get; set; }
    public bool Mute { get; set; }
    public bool Solo { get; set; }
    public List<Note> Notes { get; set; } = [];
}

public sealed class Note
{
    public int Step { get; set; }
    public int Length { get; set; } = 1;
    /// <summary>MIDI note number: 60 is middle C (C4), 69 is A4 (440 Hz).</summary>
    public int Pitch { get; set; } = 60;
    public double Velocity { get; set; } = 1;
    public Note Copy() => new() { Step = Step, Length = Length, Pitch = Pitch, Velocity = Velocity };
}

public static class MusicTheory
{
    public static readonly string[] Keys = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    static readonly string[] SharpNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    static readonly string[] FlatNames = ["C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B"];
    public static readonly Dictionary<string, int[]> Scales = new()
    {
        ["major"] = [0, 2, 4, 5, 7, 9, 11], ["minor"] = [0, 2, 3, 5, 7, 8, 10], ["harmonic minor"] = [0, 2, 3, 5, 7, 8, 11],
        ["major pentatonic"] = [0, 2, 4, 7, 9], ["minor pentatonic"] = [0, 3, 5, 7, 10], ["blues"] = [0, 3, 5, 6, 7, 10],
        ["dorian"] = [0, 2, 3, 5, 7, 9, 10], ["mixolydian"] = [0, 2, 4, 5, 7, 9, 10], ["chromatic"] = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]
    };
    public static int KeyIndex(string key) => Math.Max(0, Array.IndexOf(Keys, key));
    public static bool InScale(int pitch, string key, string scale) => Scales.TryGetValue(scale, out var s) && s.Contains(((pitch - KeyIndex(key)) % 12 + 12) % 12);
    /// <summary>Keys whose signatures use flats (F, Bb, Eb, Ab, Db, Gb majors and their relative minors).</summary>
    public static bool UsesFlats(string key, string scale)
    {
        int root = KeyIndex(key); if (scale.Contains("minor") || scale == "blues") root = (root + 3) % 12; // relative major
        return root is 5 or 10 or 3 or 8 or 1 or 6;
    }
    public static string Name(int pitch, bool flats = false) => (flats ? FlatNames : SharpNames)[((pitch % 12) + 12) % 12] + (pitch / 12 - 1);
    public static double Frequency(double pitch) => 440 * Math.Pow(2, (pitch - 69) / 12);
    /// <summary>"C4", "F#3", "Bb5", "c#-1" → MIDI number.</summary>
    public static int Parse(string name)
    {
        var m = Regex.Match(name.Trim(), @"^([A-Ga-g])([#b♯♭]*)(-?\d+)$");
        if (!m.Success) throw new FormatException($"\"{name}\" isn't a note name like C4, F#3 or Bb5.");
        int pc = "C D EF G A B".IndexOf(char.ToUpperInvariant(m.Groups[1].Value[0]));
        foreach (char c in m.Groups[2].Value) pc += c is '#' or '♯' ? 1 : -1;
        int pitch = (int.Parse(m.Groups[3].Value) + 1) * 12 + pc;
        if (pitch is < 0 or > 127) throw new FormatException($"{name} is outside MIDI notes C-1 to G9.");
        return pitch;
    }
    /// <summary>Note values in quarter notes, for the text notation and the sheet music editor.</summary>
    public static readonly (string Symbol, string Name, double Quarters)[] Values =
        [("w", "whole", 4), ("h", "half", 2), ("q", "quarter", 1), ("e", "eighth", 0.5), ("s", "sixteenth", 0.25), ("t", "thirty-second", 0.125)];
}

/// <summary>A compact text form of a layer's notes, for AI assistants and copying: space-separated events, each a
/// pitch (C4, F#3, Bb5), a chord (C4+E4+G4) or a rest (r), then a length: w h q e s t (whole … thirty-second),
/// with "." for dotted, or a number of steps like 3s. "|" bar lines are allowed and ignored; [n] jumps to step n
/// (for notes that overlap); pitch@0.5 sets a note's velocity.
/// Example: "C4 q E4 e G4 e | C5 h r q C4+E4+G4 q".</summary>
public static class MusicText
{
    public static List<Note> Parse(string text, int stepsPerBeat, int startStep = 0)
    {
        var notes = new List<Note>(); int step = startStep;
        var tokens = text.Replace("|", " ").Replace(",", " ").Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < tokens.Length; i++)
        {
            string what = tokens[i];
            // [n] jumps to step n (for notes that overlap others; Write puts these in).
            var jump = Regex.Match(what, @"^\[(\d+)\]$"); if (jump.Success) { step = startStep + int.Parse(jump.Groups[1].Value); continue; }
            if (i + 1 >= tokens.Length) throw new FormatException($"\"{what}\" needs a length after it (w h q e s t, or a number of steps like 3s).");
            string len = tokens[++i]; int steps = Length(len, stepsPerBeat);
            double velocity = 1;
            int at = what.IndexOf('@'); if (at > 0) { velocity = Math.Clamp(double.Parse(what[(at + 1)..], System.Globalization.CultureInfo.InvariantCulture), 0, 1); what = what[..at]; }
            if (what is not ("r" or "R" or "rest"))
                foreach (var part in what.Split('+')) notes.Add(new Note { Step = step, Length = steps, Pitch = MusicTheory.Parse(part), Velocity = velocity });
            step += steps;
        }
        return notes;
    }
    public static int Length(string len, int stepsPerBeat)
    {
        var m = Regex.Match(len.Trim(), @"^(\d+)s$");
        if (m.Success) return Math.Max(1, int.Parse(m.Groups[1].Value));
        bool dotted = len.EndsWith('.'); string symbol = dotted ? len[..^1] : len;
        var value = MusicTheory.Values.FirstOrDefault(v => v.Symbol == symbol);
        if (value.Symbol == null) throw new FormatException($"\"{len}\" isn't a length: use w h q e s t (add . for dotted) or a number of steps like 3s.");
        double steps = value.Quarters * stepsPerBeat * (dotted ? 1.5 : 1);
        if (steps < 1 || Math.Abs(steps - Math.Round(steps)) > 1e-9) throw new FormatException($"A {value.Name} note{(dotted ? " (dotted)" : "")} doesn't fit the grid of {stepsPerBeat} steps per beat. Use more steps per beat, or give steps like 3s.");
        return (int)Math.Round(steps);
    }
    /// <summary>The notes back as text: chords where notes start and end together, rests between.</summary>
    public static string Write(IEnumerable<Note> source, int stepsPerBeat, int stepsPerBar, bool flats = false)
    {
        var sb = new StringBuilder(); int step = 0, bar = 0;
        foreach (var group in source.GroupBy(n => (n.Step, n.Length)).OrderBy(g => g.Key.Step).ThenBy(g => g.Key.Length))
        {
            if (group.Key.Step < step) { sb.Append($"[{group.Key.Step}] "); } // overlapping notes: say where this one starts
            else if (group.Key.Step > step) sb.Append("r ").Append(LengthText(group.Key.Step - step, stepsPerBeat)).Append(' ');
            int nowBar = group.Key.Step / Math.Max(1, stepsPerBar); if (nowBar > bar && sb.Length > 0) { sb.Append("| "); bar = nowBar; }
            sb.Append(string.Join("+", group.OrderBy(n => n.Pitch).Select(n => MusicTheory.Name(n.Pitch, flats)))).Append(' ').Append(LengthText(group.Key.Length, stepsPerBeat)).Append(' ');
            step = Math.Max(step, group.Key.Step + group.Key.Length);
        }
        return sb.ToString().Trim();
    }
    public static string LengthText(int steps, int stepsPerBeat)
    {
        foreach (var v in MusicTheory.Values)
        {
            if (Math.Abs(v.Quarters * stepsPerBeat - steps) < 1e-9) return v.Symbol;
            if (Math.Abs(v.Quarters * 1.5 * stepsPerBeat - steps) < 1e-9) return v.Symbol + ".";
        }
        return steps + "s";
    }
}
