namespace Wysicraft.Core.Audio;

/// <summary>An 8-bit instrument: a wave shape (square with a pulse width, stepped triangle, saw, sine, noise or a drum
/// kit), a volume envelope (attack, decay, sustain level, release) and effects: vibrato, a pitch slide at the start of
/// each note, a fast arpeggio (chiptune chords), bit crushing and an echo.</summary>
public sealed class Instrument
{
    public static readonly string[] Waves = ["square", "triangle", "saw", "sine", "noise", "drums"];
    public string Name { get; set; } = "Square lead";
    public string Wave { get; set; } = "square";
    /// <summary>Square wave pulse width, 0.05–0.95 (0.5 hollow, 0.25 and 0.125 thinner and nasal).</summary>
    public double Duty { get; set; } = 0.5;
    public double Attack { get; set; } = 0.005;
    public double Decay { get; set; } = 0.1;
    /// <summary>Level held after the decay, 0–1.</summary>
    public double Sustain { get; set; } = 0.7;
    public double Release { get; set; } = 0.08;
    /// <summary>Vibrato depth in semitones, its speed (Hz) and how long a note plays before it starts (s).</summary>
    public double Vibrato { get; set; }
    public double VibratoRate { get; set; } = 6;
    public double VibratoDelay { get; set; } = 0.15;
    /// <summary>Pitch slide in semitones (negative drops) over SlideTime seconds from the start of each note.</summary>
    public double Slide { get; set; }
    public double SlideTime { get; set; } = 0.1;
    /// <summary>Semitone offsets cycled quickly while a note plays, like "0,4,7" for a major chord. Empty = off.</summary>
    public string Arpeggio { get; set; } = "";
    public double ArpeggioRate { get; set; } = 30;
    /// <summary>Bit depth for a crunchier sound (2–8); 0 = off.</summary>
    public int Crush { get; set; }
    /// <summary>Echo delay in seconds (0 = off) and how much of each echo comes back (0–0.9).</summary>
    public double Echo { get; set; }
    public double EchoFeedback { get; set; } = 0.35;
    public double Volume { get; set; } = 0.8;
    /// <summary>Octaves added to every note (-3 to 3).</summary>
    public int Octave { get; set; }
    /// <summary>The General MIDI program (0–127) this layer plays when the song uses a SoundFont; -1 = the nearest to
    /// this chip instrument. Bank 128 is a drum kit.</summary>
    public int Program { get; set; } = -1;
    public int Bank { get; set; }

    public Instrument Copy() => (Instrument)MemberwiseClone();
    public int[] ArpeggioSteps()
    {
        if (string.IsNullOrWhiteSpace(Arpeggio)) return [];
        return Arpeggio.Split(',', ' ', ';').Where(s => s.Trim().Length > 0).Select(s => int.Parse(s.Trim())).ToArray();
    }
    public void Check()
    {
        if (!Waves.Contains(Wave)) throw new InvalidDataException("Instrument wave must be one of " + string.Join(", ", Waves) + ".");
        double[] all = [Duty, Attack, Decay, Sustain, Release, Vibrato, VibratoRate, VibratoDelay, Slide, SlideTime, ArpeggioRate, Echo, EchoFeedback, Volume];
        if (all.Any(v => !double.IsFinite(v))) throw new InvalidDataException("Instrument settings must be numbers.");
        if (Duty is < 0.05 or > 0.95 || Sustain is < 0 or > 1 || Volume is < 0 or > 1 || EchoFeedback is < 0 or > 0.9 || Echo is < 0 or > 1) throw new InvalidDataException("Instrument: duty 0.05–0.95, sustain, volume 0–1, echo 0–1 s, echo feedback 0–0.9.");
        if (Attack is < 0 or > 5 || Decay is < 0 or > 5 || Release is < 0 or > 5 || SlideTime is < 0.001 or > 5 || Math.Abs(Slide) > 48 || Vibrato is < 0 or > 12 || VibratoRate is < 0 or > 40 || ArpeggioRate is < 1 or > 120) throw new InvalidDataException("Instrument: attack, decay, release 0–5 s; slide ±48 semitones; vibrato 0–12 semitones at 0–40 Hz; arpeggio 1–120 Hz.");
        if (Program is < -1 or > 127 || Bank is < 0 or > 128) throw new InvalidDataException("Instrument: program -1 to 127, bank 0 to 128.");
        if (Crush is < 0 or > 8 || Octave is < -3 or > 3) throw new InvalidDataException("Instrument: crush 0 or 2–8 bits; octave -3 to 3.");
        try { if (ArpeggioSteps().Any(s => Math.Abs(s) > 36) || ArpeggioSteps().Length > 8) throw new FormatException(); }
        catch (FormatException) { throw new InvalidDataException("Arpeggio is up to 8 semitone offsets like \"0,4,7\"."); }
    }
}

public static class Instruments
{
    static Instrument I(string name, string wave, double duty = 0.5, double a = 0.005, double d = 0.1, double s = 0.7, double r = 0.08, double vib = 0, double slide = 0, double slideTime = 0.1, string arp = "", int crush = 0, double echo = 0, double volume = 0.8, int octave = 0)
        => new() { Name = name, Wave = wave, Duty = duty, Attack = a, Decay = d, Sustain = s, Release = r, Vibrato = vib, Slide = slide, SlideTime = slideTime, Arpeggio = arp, Crush = crush, Echo = echo, Volume = volume, Octave = octave };
    /// <summary>Ready-made 8-bit instruments; each layer starts from one and can be tweaked.</summary>
    public static readonly Instrument[] Presets =
    [
        I("Square lead", "square", vib: 0.15),
        I("Pulse 25%", "square", duty: 0.25, d: 0.15, s: 0.6, r: 0.1, vib: 0.12),
        I("Pulse 12%", "square", duty: 0.125, d: 0.15, s: 0.6, r: 0.1),
        I("Triangle bass", "triangle", a: 0.004, d: 0.05, s: 0.9, r: 0.05, volume: 0.95),
        I("Saw brass", "saw", a: 0.02, d: 0.2, s: 0.6, r: 0.1, volume: 0.55),
        I("Sine flute", "sine", a: 0.04, d: 0.1, s: 0.8, r: 0.15, vib: 0.2, volume: 0.85),
        I("Pluck", "square", duty: 0.25, a: 0.002, d: 0.25, s: 0, r: 0.05),
        I("Bell", "square", a: 0.002, d: 0.6, s: 0, r: 0.3, crush: 6, echo: 0.18),
        I("Chip chord", "square", d: 0.2, s: 0.6, arp: "0,4,7", volume: 0.6),
        I("Minor chord", "square", duty: 0.25, d: 0.2, s: 0.6, arp: "0,3,7", volume: 0.6),
        I("Laser bass", "saw", a: 0.002, d: 0.2, s: 0.5, slide: -12, slideTime: 0.08, volume: 0.6),
        I("Soft pad", "triangle", a: 0.25, d: 0.3, s: 0.8, r: 0.5, vib: 0.1, echo: 0.25),
        // 8-bit takes on common instruments (MIDI files map onto these).
        I("Piano", "square", duty: 0.25, a: 0.002, d: 0.5, s: 0.35, r: 0.2, volume: 0.7),
        I("Organ", "square", a: 0.01, d: 0.05, s: 1, r: 0.05, vib: 0.05, volume: 0.55),
        I("Guitar", "saw", a: 0.002, d: 0.35, s: 0.2, r: 0.1, volume: 0.55),
        I("Harp", "triangle", a: 0.002, d: 0.6, s: 0.1, r: 0.4, echo: 0.15, volume: 0.9),
        I("Strings", "saw", a: 0.12, d: 0.2, s: 0.8, r: 0.3, vib: 0.12, volume: 0.45),
        I("Choir", "triangle", a: 0.15, d: 0.2, s: 0.9, r: 0.4, vib: 0.2, volume: 0.8),
        I("Music box", "square", duty: 0.125, a: 0.002, d: 0.4, s: 0, r: 0.3, echo: 0.2, volume: 0.6),
        I("Noise", "noise", a: 0.002, d: 0.2, s: 0.2, r: 0.1, volume: 0.5),
        I("Drum kit", "drums", volume: 0.9),
    ];
    public static Instrument Get(string name) => (Presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? Presets[0]).Copy();
    /// <summary>Drum sounds for the Drum kit (General MIDI note numbers).</summary>
    public static readonly (int Pitch, string Name)[] Drums =
        [(36, "Kick"), (38, "Snare"), (39, "Clap"), (42, "Closed hat"), (46, "Open hat"), (41, "Low tom"), (45, "Mid tom"), (48, "High tom"), (49, "Crash"), (51, "Ride"), (37, "Rim")];
    public static string DrumName(int pitch) => Drums.FirstOrDefault(d => d.Pitch == pitch).Name ?? MusicTheory.Name(pitch);
}

/// <summary>One sounding note. Songs render voices ahead of time; the Music maker's keyboard plays them live (Release
/// when the key comes up). Next() gives one mono sample in -1..1 (before the instrument's echo).</summary>
public sealed class Voice
{
    readonly Instrument ins; readonly int rate; readonly double basePitch, velocity, hold; readonly int[] arp;
    double t, phase, releaseAt = double.PositiveInfinity, releaseLevel, level, noisePhase, noiseOut = 1; int lfsr = 1;
    public bool Done { get; private set; }
    public Voice(Instrument instrument, double pitch, double velocity, int sampleRate, double holdSeconds = double.PositiveInfinity)
    {
        ins = instrument; rate = sampleRate; basePitch = pitch + instrument.Octave * 12; this.velocity = Math.Clamp(velocity, 0, 1); hold = holdSeconds; arp = instrument.ArpeggioSteps();
        if (ins.Wave == "drums") drum = DrumFor((int)Math.Round(pitch));
    }
    public void Release() { if (double.IsPositiveInfinity(releaseAt)) { releaseAt = t; releaseLevel = level; } }
    public float Next()
    {
        if (Done) return 0;
        double dt = 1.0 / rate;
        if (t >= hold) Release();
        double s = ins.Wave == "drums" ? DrumSample() : ToneSample(dt);
        t += dt;
        if (ins.Crush >= 2) { double q = Math.Pow(2, ins.Crush - 1); s = Math.Round(s * q) / q; }
        return (float)(s * level * velocity * ins.Volume);
    }
    double Envelope()
    {
        double a = Math.Max(ins.Attack, 0.001), d = Math.Max(ins.Decay, 0.001);
        double held = t < a ? t / a : t < a + d ? 1 - (1 - ins.Sustain) * (t - a) / d : ins.Sustain;
        if (t < releaseAt) return held;
        double r = Math.Max(ins.Release, 0.002), k = 1 - (t - releaseAt) / r;
        if (k <= 0 || (held <= 0.0005 && ins.Sustain == 0 && t > a + d)) { Done = true; return 0; }
        return releaseLevel * k;
    }
    double ToneSample(double dt)
    {
        level = Envelope(); if (Done) return 0;
        if (ins.Sustain == 0 && t > ins.Attack + ins.Decay + 0.002 && t >= releaseAt) { Done = true; return 0; }
        double pitch = basePitch + ins.Slide * Math.Min(1, t / Math.Max(0.001, ins.SlideTime));
        if (ins.Vibrato > 0 && t > ins.VibratoDelay) pitch += ins.Vibrato * Math.Sin(2 * Math.PI * ins.VibratoRate * (t - ins.VibratoDelay));
        if (arp.Length > 0) pitch += arp[(int)(t * ins.ArpeggioRate) % arp.Length];
        double f = MusicTheory.Frequency(pitch);
        phase = (phase + f * dt) % 1;
        switch (ins.Wave)
        {
            case "square": return phase < ins.Duty ? 0.6 : -0.6;
            case "triangle": { double tri = 4 * Math.Abs(phase - 0.5) - 1; return Math.Round(tri * 7.5) / 7.5 * 0.8; } // 16 steps, like the NES
            case "saw": return (2 * phase - 1) * 0.6;
            case "sine": return Math.Sin(2 * Math.PI * phase) * 0.8;
            default: return Noise(f * 8, dt) * 0.6;
        }
    }
    // 15-bit shift register noise (like the NES noise channel), clocked faster for higher notes.
    double Noise(double clock, double dt)
    {
        noisePhase += clock * dt;
        while (noisePhase >= 1) { noisePhase -= 1; int bit = (lfsr ^ (lfsr >> 1)) & 1; lfsr = (lfsr >> 1) | (bit << 14); noiseOut = (lfsr & 1) == 1 ? 1 : -1; }
        return noiseOut;
    }

    // ---- Drum kit ----
    readonly (string Kind, double Tone, double Length)? drum;
    static (string, double, double) DrumFor(int pitch) => pitch switch
    {
        35 or 36 => ("kick", 150, 0.28), 37 => ("rim", 900, 0.05), 38 or 40 => ("snare", 190, 0.18), 39 => ("clap", 0, 0.16),
        42 or 44 => ("hat", 0, 0.05), 46 => ("hat", 0, 0.3), 49 or 52 or 55 or 57 => ("crash", 0, 1.1), 51 or 53 or 59 => ("ride", 0, 0.6),
        41 or 43 => ("tom", 110, 0.3), 45 or 47 => ("tom", 150, 0.28), 48 or 50 => ("tom", 200, 0.25),
        _ => ("noise", MusicTheory.Frequency(pitch), 0.15)
    };
    double DrumSample()
    {
        var (kind, tone, length) = drum!.Value;
        double dt = 1.0 / rate, k = t / length;
        if (k >= 1) { Done = true; level = 0; return 0; }
        level = 1;
        double env = Math.Pow(1 - k, 2.2);
        switch (kind)
        {
            case "kick": { double f = 45 + (tone - 45) * Math.Exp(-t * 28); phase = (phase + f * dt) % 1; return Math.Sin(2 * Math.PI * phase) * env * 1.1; }
            case "tom": { double f = tone * (0.7 + 0.3 * Math.Exp(-t * 12)); phase = (phase + f * dt) % 1; return (4 * Math.Abs(phase - 0.5) - 1) * env * 0.9; }
            case "snare": { phase = (phase + tone * dt) % 1; double body = Math.Sin(2 * Math.PI * phase) * Math.Exp(-t * 40); return (Noise(9000, dt) * 0.55 + body * 0.5) * env; }
            case "clap": { double bursts = t < 0.03 ? (((int)(t * 300)) % 2 == 0 ? 1 : 0.3) : 1; return Noise(7000, dt) * env * bursts * 0.7; }
            case "hat": return Noise(22000, dt) * env * 0.35;
            case "crash": return Noise(16000, dt) * env * 0.4;
            case "ride": { phase = (phase + 3100 * dt) % 1; return (Noise(18000, dt) * 0.25 + (phase < 0.5 ? 0.12 : -0.12)) * env; }
            case "rim": { phase = (phase + tone * dt) % 1; return (phase < 0.5 ? 0.6 : -0.6) * env; }
            default: return Noise(tone * 8, dt) * env * 0.5;
        }
    }
}

/// <summary>Mixes a song to stereo samples: every note of every audible layer (mute and solo respected) through its
/// instrument, with the layer's volume, pan, transpose and echo; then the song volume and a limiter.</summary>
public static class SongRenderer
{
    public const int SampleRate = 32000;
    public static (float[] Left, float[] Right) Render(Song song, int sampleRate = SampleRate, int fromStep = 0, int toStep = -1, Func<Track, bool>? include = null)
    {
        double stepSec = song.StepSeconds; if (toStep < 0) toStep = song.TotalSteps;
        int length = (int)Math.Ceiling((toStep - fromStep) * stepSec * sampleRate);
        const double tailSeconds = 3; int tail = (int)(tailSeconds * sampleRate);
        var left = new float[length + tail]; var right = new float[length + tail];
        bool anySolo = song.Tracks.Any(t => t.Solo);
        var font = SoundFonts.Load(song.SoundFont);
        foreach (var track in font != null ? [] : song.Tracks)
        {
            if (include != null ? !include(track) : track.Mute || anySolo && !track.Solo) continue;
            var buffer = new float[left.Length];
            foreach (var note in track.Notes)
            {
                if (note.Step >= toStep || note.Step + note.Length <= fromStep) continue;
                int start = (int)Math.Round((note.Step - fromStep) * stepSec * sampleRate);
                var voice = new Voice(track.Instrument, note.Pitch + track.Transpose + track.Fine / 100, note.Velocity, sampleRate, note.Length * stepSec);
                // A note that began before the range still plays its remaining part.
                for (int skip = Math.Max(0, -start); skip > 0 && !voice.Done; skip--) voice.Next();
                for (int i = Math.Max(0, start); i < buffer.Length && !voice.Done; i++) buffer[i] += voice.Next();
            }
            ApplyEcho(buffer, track.Instrument, sampleRate);
            double pan = Math.Clamp(track.Pan, -1, 1), angle = (pan + 1) * Math.PI / 4, vol = track.Volume * song.Volume;
            float gl = (float)(Math.Cos(angle) * Math.Sqrt(2) * vol), gr = (float)(Math.Sin(angle) * Math.Sqrt(2) * vol);
            for (int i = 0; i < buffer.Length; i++) { left[i] += buffer[i] * gl; right[i] += buffer[i] * gr; }
        }
        // Played with a SoundFont: each layer by its instrument from the font (stereo, as the font places it).
        foreach (var track in font == null ? [] : song.Tracks)
        {
            if (include != null ? !include(track) : track.Mute || anySolo && !track.Solo) continue;
            var (l, r) = SoundFonts.RenderTrack(font!, track, sampleRate, fromStep, toStep, stepSec, left.Length);
            double pan = Math.Clamp(track.Pan, -1, 1), vol = track.Volume * song.Volume * FontGain;
            float gl = (float)(vol * Math.Min(1, 1 - pan)), gr = (float)(vol * Math.Min(1, 1 + pan));
            for (int i = 0; i < left.Length; i++) { left[i] += l[i] * gl; right[i] += r[i] * gr; }
        }
        // Sound after the end: a looping song folds it back onto the start, otherwise it's kept until it goes quiet.
        int end = length;
        if (song.Loop && fromStep == 0 && toStep == song.TotalSteps)
        {
            for (int i = length; i < left.Length; i++) { left[i - length] += left[i]; right[i - length] += right[i]; }
        }
        else
        {
            end = left.Length; while (end > length && Math.Abs(left[end - 1]) < 1e-4 && Math.Abs(right[end - 1]) < 1e-4) end--;
        }
        Array.Resize(ref left, end); Array.Resize(ref right, end);
        Limit(left, right);
        return (left, right);
    }
    /// <summary>SoundFont layers are quieter than the chip waves; this brings them to about the same loudness.</summary>
    public const float FontGain = 5f;
    static void ApplyEcho(float[] buffer, Instrument ins, int sampleRate)
    {
        if (ins.Echo <= 0) return;
        int delay = Math.Max(1, (int)(ins.Echo * sampleRate)); float fb = (float)Math.Clamp(ins.EchoFeedback, 0, 0.9);
        for (int i = delay; i < buffer.Length; i++) buffer[i] += buffer[i - delay] * fb;
    }
    /// <summary>Keeps peaks under full scale: scales the whole mix down if it would clip.</summary>
    public static void Limit(float[] left, float[] right)
    {
        float peak = 0; for (int i = 0; i < left.Length; i++) peak = Math.Max(peak, Math.Max(Math.Abs(left[i]), Math.Abs(right[i])));
        if (peak <= 0.97f) return; float g = 0.97f / peak;
        for (int i = 0; i < left.Length; i++) { left[i] *= g; right[i] *= g; }
    }
    /// <summary>A short click for the metronome (accented on the first beat of a bar).</summary>
    public static float[] Click(bool accent, int sampleRate)
    {
        int n = sampleRate / 25; var s = new float[n]; double f = accent ? 1760 : 1175;
        for (int i = 0; i < n; i++) s[i] = (float)(Math.Sin(2 * Math.PI * f * i / sampleRate) * Math.Pow(1 - (double)i / n, 3) * 0.45);
        return s;
    }
}
