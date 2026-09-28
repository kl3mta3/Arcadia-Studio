namespace Wysicraft.Core.Audio;

/// <summary>A retro sound effect, in the style of the classic sfxr generator but with readable units: a wave, a pitch that
/// can slide, wobble and jump (arpeggio), a volume envelope with an optional punch, pulse width sweep, repeats and simple
/// low-pass/high-pass filters. Saved as JSON beside the sound (name.ogg.sfx) so it can be opened and changed again.</summary>
public sealed class SoundEffect
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "sound";
    public string Wave { get; set; } = "square"; // square, triangle, saw, sine, noise
    /// <summary>Start pitch in Hz, the lowest it may slide to (0 = no limit), slide in octaves per second and how
    /// fast the slide itself changes (octaves per second²).</summary>
    public double Frequency { get; set; } = 440;
    public double MinFrequency { get; set; }
    public double Slide { get; set; }
    public double DeltaSlide { get; set; }
    public double VibratoDepth { get; set; }   // semitones
    public double VibratoSpeed { get; set; } = 10; // Hz
    /// <summary>After ArpeggioTime seconds the pitch jumps by ArpeggioSemitones (a coin's "ding-DING").</summary>
    public double ArpeggioSemitones { get; set; }
    public double ArpeggioTime { get; set; }
    public double Duty { get; set; } = 0.5;
    public double DutySweep { get; set; } // per second
    public double Attack { get; set; }
    public double Sustain { get; set; } = 0.1;
    /// <summary>Extra loudness at the start of the sustain that fades through it (0–1).</summary>
    public double Punch { get; set; }
    public double Decay { get; set; } = 0.2;
    /// <summary>Restart the pitch (and arpeggio) every RepeatTime seconds; 0 = off.</summary>
    public double RepeatTime { get; set; }
    /// <summary>Low-pass cutoff 0–1 (1 = open) and how it moves per second; high-pass cutoff 0–1 (0 = off).</summary>
    public double LowPass { get; set; } = 1;
    public double LowPassSweep { get; set; }
    public double HighPass { get; set; }
    public int Crush { get; set; }
    public double Volume { get; set; } = 0.7;

    public double Seconds => Attack + Sustain + Decay;
    public SoundEffect Copy() => (SoundEffect)MemberwiseClone();
    public void Check()
    {
        if (!new[] { "square", "triangle", "saw", "sine", "noise" }.Contains(Wave)) throw new InvalidDataException("Sound effect wave must be square, triangle, saw, sine or noise.");
        double[] all = [Frequency, MinFrequency, Slide, DeltaSlide, VibratoDepth, VibratoSpeed, ArpeggioSemitones, ArpeggioTime, Duty, DutySweep, Attack, Sustain, Punch, Decay, RepeatTime, LowPass, LowPassSweep, HighPass, Volume];
        if (all.Any(v => !double.IsFinite(v))) throw new InvalidDataException("Sound effect settings must be numbers.");
        if (Frequency is < 20 or > 8000 || MinFrequency is < 0 or > 8000) throw new InvalidDataException("Frequency is 20–8000 Hz.");
        if (Attack is < 0 or > 3 || Sustain is < 0 or > 5 || Decay is < 0 or > 5 || Seconds < 0.01) throw new InvalidDataException("Attack 0–3 s, sustain 0–5 s, decay 0–5 s (at least 0.01 s in all).");
        if (Duty is < 0.05 or > 0.95 || Punch is < 0 or > 1 || LowPass is < 0 or > 1 || HighPass is < 0 or > 1 || Volume is < 0 or > 1 || Crush is < 0 or > 8) throw new InvalidDataException("Duty 0.05–0.95; punch, filters and volume 0–1; crush 0 or 2–8 bits.");
    }

    /// <summary>Renders the effect (mono).</summary>
    public float[] Render(int sampleRate = SongRenderer.SampleRate)
    {
        Check();
        int n = (int)Math.Ceiling(Seconds * sampleRate); var output = new float[n];
        double phase = 0, lp = 0, hpPrev = 0, hpOut = 0, noiseClock = 0, noiseOut = 1; int lfsr = 1;
        double slide = Slide, freqOct = Math.Log2(Frequency), cutoff = LowPass, duty = Duty, restart = 0;
        bool arpDone = false; double dt = 1.0 / sampleRate;
        for (int i = 0; i < n; i++)
        {
            double t = i * dt, local = t - restart;
            if (RepeatTime > 0 && local >= RepeatTime) { restart = t; local = 0; freqOct = Math.Log2(Frequency); slide = Slide; arpDone = false; }
            // Pitch
            slide += DeltaSlide * dt; freqOct += slide * dt;
            double f = Math.Pow(2, freqOct);
            if (MinFrequency > 0 && f < MinFrequency) f = MinFrequency;
            if (!arpDone && ArpeggioTime > 0 && local >= ArpeggioTime) { freqOct += ArpeggioSemitones / 12; arpDone = true; f = Math.Pow(2, freqOct); }
            if (VibratoDepth > 0) f *= Math.Pow(2, VibratoDepth / 12 * Math.Sin(2 * Math.PI * VibratoSpeed * t));
            f = Math.Clamp(f, 10, sampleRate / 2.0);
            duty = Math.Clamp(Duty + DutySweep * t, 0.05, 0.95);
            phase = (phase + f * dt) % 1;
            double s = Wave switch
            {
                "square" => phase < duty ? 0.6 : -0.6,
                "triangle" => (4 * Math.Abs(phase - 0.5) - 1) * 0.8,
                "saw" => (2 * phase - 1) * 0.6,
                "sine" => Math.Sin(2 * Math.PI * phase) * 0.8,
                _ => Noise(f * 16, dt, ref noiseClock, ref lfsr, ref noiseOut) * 0.6
            };
            // Envelope
            double env;
            if (t < Attack) env = t / Math.Max(Attack, 1e-4);
            else if (t < Attack + Sustain) env = 1 + Punch * (1 - (t - Attack) / Math.Max(Sustain, 1e-4));
            else env = Math.Max(0, 1 - (t - Attack - Sustain) / Math.Max(Decay, 1e-4));
            s *= env;
            // Filters: one-pole low-pass (cutoff 1 = open) and high-pass.
            cutoff = Math.Clamp(LowPass + LowPassSweep * t, 0.001, 1);
            if (cutoff < 1) { double a = Math.Pow(cutoff, 2); lp += (s - lp) * a; s = lp; } else lp = s;
            if (HighPass > 0) { double a = 1 - HighPass * 0.9; hpOut = a * (hpOut + s - hpPrev); hpPrev = s; s = hpOut; }
            if (Crush >= 2) { double q = Math.Pow(2, Crush - 1); s = Math.Round(s * q) / q; }
            output[i] = (float)Math.Clamp(s * Volume, -1, 1);
        }
        return output;
    }
    static double Noise(double clock, double dt, ref double phase, ref int lfsr, ref double output)
    {
        phase += clock * dt;
        while (phase >= 1) { phase -= 1; int bit = (lfsr ^ (lfsr >> 1)) & 1; lfsr = (lfsr >> 1) | (bit << 14); output = (lfsr & 1) == 1 ? 1 : -1; }
        return output;
    }

    // ---- Presets, random and mutate ----
    public static readonly string[] Presets = ["coin", "jump", "laser", "explosion", "powerup", "hurt", "blip", "random"];
    public static SoundEffect Preset(string preset, Random? random = null)
    {
        var r = random ?? new Random(); double R(double a, double b) => a + r.NextDouble() * (b - a);
        string Pick(params string[] w) => w[r.Next(w.Length)];
        var s = new SoundEffect { Name = preset };
        switch (preset)
        {
            case "coin":
                s.Wave = Pick("square", "saw"); s.Frequency = R(800, 1400); s.Sustain = R(0.03, 0.08); s.Punch = R(0.3, 0.6); s.Decay = R(0.15, 0.35);
                s.ArpeggioSemitones = new[] { 5, 7, 12 }[r.Next(3)]; s.ArpeggioTime = R(0.04, 0.09); s.Duty = R(0.25, 0.5); break;
            case "jump":
                s.Wave = "square"; s.Duty = R(0.2, 0.5); s.Frequency = R(250, 500); s.Slide = R(2.5, 5); s.Sustain = R(0.06, 0.14); s.Decay = R(0.08, 0.2); s.LowPass = R(0.6, 1); break;
            case "laser":
                s.Wave = Pick("square", "saw", "sine"); s.Frequency = R(900, 2200); s.MinFrequency = R(80, 250); s.Slide = -R(6, 14); s.Sustain = R(0.05, 0.15); s.Decay = R(0.05, 0.2);
                s.Duty = R(0.1, 0.5); s.DutySweep = R(-0.5, 0.5); if (r.Next(2) == 0) s.HighPass = R(0.1, 0.4); break;
            case "explosion":
                s.Wave = "noise"; s.Frequency = R(60, 300); s.Slide = -R(0.3, 2); s.Sustain = R(0.1, 0.3); s.Punch = R(0.3, 0.7); s.Decay = R(0.4, 0.9);
                if (r.Next(2) == 0) { s.VibratoDepth = R(0.5, 3); s.VibratoSpeed = R(8, 25); } s.LowPass = R(0.5, 1); s.LowPassSweep = -R(0, 0.6); break;
            case "powerup":
                s.Wave = Pick("square", "triangle", "saw"); s.Frequency = R(250, 500); s.Slide = R(1, 3); s.Sustain = R(0.2, 0.35); s.Decay = R(0.1, 0.3);
                if (r.Next(2) == 0) s.RepeatTime = R(0.06, 0.12); else { s.VibratoDepth = R(0.2, 0.8); s.VibratoSpeed = R(12, 20); } s.Duty = R(0.3, 0.5); break;
            case "hurt":
                s.Wave = Pick("square", "saw", "noise"); s.Frequency = R(200, 600); s.Slide = -R(2, 5); s.Sustain = R(0.02, 0.08); s.Punch = R(0.2, 0.5); s.Decay = R(0.1, 0.25); s.HighPass = r.Next(2) == 0 ? R(0, 0.3) : 0; break;
            case "blip":
                s.Wave = Pick("square", "sine", "triangle"); s.Frequency = R(500, 1500); s.Sustain = R(0.02, 0.06); s.Decay = R(0.02, 0.08); s.Duty = R(0.2, 0.5); break;
            default:
                s.Wave = Pick("square", "triangle", "saw", "sine", "noise"); s.Frequency = Math.Pow(2, R(6.5, 11)); s.Slide = R(-6, 6); s.DeltaSlide = R(-10, 10) * (r.Next(3) == 0 ? 1 : 0);
                s.Attack = r.Next(3) == 0 ? R(0, 0.15) : 0; s.Sustain = R(0.03, 0.35); s.Punch = R(0, 0.6); s.Decay = R(0.05, 0.5); s.Duty = R(0.1, 0.9); s.DutySweep = R(-1, 1) * (r.Next(2));
                if (r.Next(3) == 0) { s.VibratoDepth = R(0.2, 2); s.VibratoSpeed = R(4, 25); }
                if (r.Next(3) == 0) { s.ArpeggioSemitones = r.Next(-12, 13); s.ArpeggioTime = R(0.03, 0.2); }
                if (r.Next(4) == 0) s.RepeatTime = R(0.05, 0.3);
                s.LowPass = r.Next(2) == 0 ? 1 : R(0.3, 1); s.HighPass = r.Next(3) == 0 ? R(0, 0.4) : 0; s.Name = "random"; break;
        }
        s.MinFrequency = Math.Min(s.MinFrequency, s.Frequency);
        return s;
    }
    /// <summary>A close variation: every setting nudged a little.</summary>
    public SoundEffect Mutate(Random? random = null)
    {
        var r = random ?? new Random(); var m = Copy();
        double N(double v, double spread, double lo, double hi) => Math.Clamp(v + (r.NextDouble() * 2 - 1) * spread, lo, hi);
        m.Frequency = Math.Clamp(Frequency * Math.Pow(2, (r.NextDouble() * 2 - 1) * 0.25), 20, 8000); m.Slide = N(Slide, 0.6, -40, 40); m.DeltaSlide = N(DeltaSlide, 0.6, -60, 60);
        m.Duty = N(Duty, 0.08, 0.05, 0.95); m.Sustain = N(Sustain, 0.03, 0, 5); m.Decay = N(Decay, 0.05, 0, 5); m.Punch = N(Punch, 0.08, 0, 1);
        m.VibratoDepth = N(VibratoDepth, VibratoDepth > 0 ? 0.2 : 0, 0, 12); m.LowPass = N(LowPass, LowPass < 1 ? 0.08 : 0, 0, 1); m.ArpeggioTime = N(ArpeggioTime, ArpeggioTime > 0 ? 0.02 : 0, 0, 3);
        if (m.Seconds < 0.01) m.Decay = 0.05;
        return m;
    }
}
