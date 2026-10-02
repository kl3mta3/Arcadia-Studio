namespace Wysicraft.Core.Audio;

/// <summary>Listening tools for turning recordings into songs: short-time spectra, splitting a stereo mix into what's in
/// the middle (usually the singer, bass and drums) and what's at the sides (usually guitars and keys), following the
/// beat through the whole song (tempo drift included), finding bar lines, and naming the chord on every beat.</summary>
public static class SongAnalysis
{
    public const int Rate = 22050;

    // ---- Spectra ----
    public static void Fft(double[] re, double[] im, bool inverse = false)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++) { int bit = n >> 1; for (; (j & bit) != 0; bit >>= 1) j ^= bit; j ^= bit; if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); } }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = (inverse ? 2 : -2) * Math.PI / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int j = 0; j < len / 2; j++)
                {
                    int a = i + j, b = i + j + len / 2;
                    double vr = re[b] * cr - im[b] * ci, vi = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - vr; im[b] = im[a] - vi; re[a] += vr; im[a] += vi;
                    double nr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = nr;
                }
            }
        }
        if (inverse) for (int i = 0; i < n; i++) { re[i] /= n; im[i] /= n; }
    }
    static double[] Hann(int n) => Enumerable.Range(0, n).Select(i => 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n)).ToArray();

    /// <summary>Magnitude spectra (frames × bins) of a signal at 22050 Hz.</summary>
    public static float[][] Magnitudes(float[] x, int size, int hop)
    {
        int frames = Math.Max(0, (x.Length - size) / hop + 1); var w = Hann(size); var result = new float[frames][];
        var re = new double[size]; var im = new double[size];
        for (int f = 0; f < frames; f++)
        {
            for (int i = 0; i < size; i++) { re[i] = x[f * hop + i] * w[i]; im[i] = 0; }
            Fft(re, im); var mag = new float[size / 2 + 1];
            for (int k = 0; k <= size / 2; k++) mag[k] = (float)Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
            result[f] = mag;
        }
        return result;
    }

    /// <summary>Splits a stereo recording into its centre (what's the same in both speakers: usually the voice, bass,
    /// kick and snare) and sides (what's panned: usually doubled guitars, keys, room), bin by bin.</summary>
    public static (float[] Centre, float[] Sides, double Width) CentreAndSides(float[] left, float[] right)
    {
        const int size = 2048, hop = size / 4; var w = Hann(size);
        int n = left.Length, frames = Math.Max(1, (n - size) / hop + 1);
        var centre = new double[n + size]; var sides = new double[n + size]; var norm = new double[n + size];
        var lr = new double[size]; var li = new double[size]; var rr = new double[size]; var ri = new double[size];
        var cr = new double[size]; var ci = new double[size]; var sr = new double[size]; var si = new double[size];
        double sideEnergy = 0, totalEnergy = 0;
        for (int f = 0; f < frames; f++)
        {
            int at = f * hop;
            for (int i = 0; i < size; i++) { double wl = at + i < n ? left[at + i] * w[i] : 0, wr = at + i < n ? right[at + i] * w[i] : 0; lr[i] = wl; li[i] = 0; rr[i] = wr; ri[i] = 0; }
            Fft(lr, li); Fft(rr, ri);
            for (int k = 0; k < size; k++)
            {
                // Mid (L+R) holds everything; side (L-R) holds only what is panned. Taking about as much from the mid as the
                // side shows is panned there (spectral subtraction) leaves what's in the middle, without cutting a voice
                // out wherever a guitar plays the same pitch.
                double mr = (lr[k] + rr[k]) / 2, mi = (li[k] + ri[k]) / 2, dr = (lr[k] - rr[k]) / 2, di = (li[k] - ri[k]) / 2;
                double mid = Math.Sqrt(mr * mr + mi * mi), side = Math.Sqrt(dr * dr + di * di);
                double keep = mid > 1e-9 ? Math.Clamp(1 - 1.3 * side / mid, 0, 1) : 0;
                cr[k] = mr * keep; ci[k] = mi * keep;
                sr[k] = dr + mr * (1 - keep) * 0.5; si[k] = di + mi * (1 - keep) * 0.5;
                if (k <= size / 2) { double c = cr[k] * cr[k] + ci[k] * ci[k], s = sr[k] * sr[k] + si[k] * si[k]; sideEnergy += s; totalEnergy += c + s; }
            }
            Fft(cr, ci, true); Fft(sr, si, true);
            for (int i = 0; i < size; i++) { centre[at + i] += cr[i] * w[i]; sides[at + i] += sr[i] * w[i]; norm[at + i] += w[i] * w[i]; }
        }
        var c1 = new float[n]; var s1 = new float[n];
        for (int i = 0; i < n; i++) { double d = norm[i] > 1e-6 ? norm[i] : 1; c1[i] = (float)(centre[i] / d); s1[i] = (float)(sides[i] / d); }
        return (c1, s1, totalEnergy > 0 ? sideEnergy / totalEnergy : 0);
    }

    /// <summary>Keeps only lo–hi Hz (for the melody: the singing range, without kick, bass and cymbal hiss).</summary>
    public static float[] BandPass(float[] x, double lo, double hi)
    {
        const int size = 2048, hop = size / 4; var w = Hann(size); int n = x.Length, frames = Math.Max(1, (n - size) / hop + 1);
        var outp = new double[n + size]; var norm = new double[n + size]; var re = new double[size]; var im = new double[size];
        int k0 = (int)(lo * size / Rate), k1 = (int)(hi * size / Rate);
        for (int f = 0; f < frames; f++)
        {
            int at = f * hop; for (int i = 0; i < size; i++) { re[i] = at + i < n ? x[at + i] * w[i] : 0; im[i] = 0; }
            Fft(re, im);
            for (int k = 0; k < size; k++) { int bin = k <= size / 2 ? k : size - k; double g = bin < k0 ? Math.Pow(bin / (double)Math.Max(1, k0), 4) : bin > k1 ? Math.Pow(k1 / (double)bin, 4) : 1; re[k] *= g; im[k] *= g; }
            Fft(re, im, true);
            for (int i = 0; i < size; i++) { outp[at + i] += re[i] * w[i]; norm[at + i] += w[i] * w[i]; }
        }
        var r = new float[n]; for (int i = 0; i < n; i++) r[i] = (float)(outp[i] / (norm[i] > 1e-6 ? norm[i] : 1)); return r;
    }

    // ---- Beats ----
    /// <summary>How strongly new sounds start in each frame (log spectral flux), 86 frames per second.</summary>
    public static double[] OnsetEnvelope(float[] x, out double frameSeconds)
    {
        const int size = 1024, hop = 256; frameSeconds = hop / (double)Rate;
        var mags = Magnitudes(x, size, hop); var env = new double[mags.Length];
        for (int f = 1; f < mags.Length; f++)
        {
            double sum = 0;
            for (int k = 1; k < mags[f].Length; k++)
            {
                double d = Math.Log(1 + 100 * mags[f][k]) - Math.Log(1 + 100 * mags[f - 1][k]);
                if (d > 0) sum += d * (k < 12 ? 2.5 : 1); // the low end (kick, bass) counts extra
            }
            env[f] = sum;
        }
        // Remove the slowly changing level so only the hits stand out.
        var outp = new double[env.Length]; int w = 16;
        for (int i = 0; i < env.Length; i++) { double m = 0; int c = 0; for (int j = Math.Max(0, i - w); j < Math.Min(env.Length, i + w + 1); j++) { m += env[j]; c++; } outp[i] = Math.Max(0, env[i] - m / c); }
        double sd = Math.Sqrt(outp.Select(v => v * v).DefaultIfEmpty(0).Average()); if (sd > 0) for (int i = 0; i < outp.Length; i++) outp[i] /= sd;
        return outp;
    }

    /// <summary>The tempo (BPM) that best explains the rhythm: autocorrelation of the onsets with its multiples, gently
    /// preferring the range most songs are counted in.</summary>
    public static double Tempo(double[] env, double frameSeconds, double min = 60, double max = 210)
    {
        int n = env.Length; if (n < 100) return 120;
        double AC(double lag) { int l0 = (int)lag; double frac = lag - l0, s = 0; for (int i = l0 + 1; i < n; i++) s += env[i] * (env[i - l0] * (1 - frac) + env[i - l0 - 1] * frac); return s / (n - l0); }
        double best = 120, bestScore = double.MinValue;
        for (double bpm = min; bpm <= max; bpm += 0.25)
        {
            double lag = 60 / bpm / frameSeconds;
            double score = AC(lag) + 0.5 * AC(2 * lag) + 0.33 * AC(4 * lag) + 0.25 * AC(lag / 2);
            score *= Math.Exp(-0.5 * Math.Pow(Math.Log2(bpm / 130) / 1.1, 2));
            if (score > bestScore) { bestScore = score; best = bpm; }
        }
        // Counted the way people usually count: a double past 160 stays in half time (punk at 191 reads as 96).
        if (best * 2 <= 160 && FasterPulse(env, frameSeconds, best)) best *= 2;
        return best;
    }

    /// <summary>Is the song really moving twice as fast as this tempo? Follow the beat and look halfway between the
    /// beats: with a punk snare on every other beat the in-between hits are nearly as strong as the beats, or the
    /// faster pulse is the stronger repetition in its own right.</summary>
    public static bool FasterPulse(double[] env, double frameSeconds, double bpm)
    {
        int n = env.Length;
        double AC(double lag) { int l0 = (int)lag; double frac = lag - l0, s = 0; for (int i = l0 + 1; i < n; i++) s += env[i] * (env[i - l0] * (1 - frac) + env[i - l0 - 1] * frac); return s / (n - l0); }
        var beats = TrackBeats(env, frameSeconds, bpm);
        double on = 0, mid = 0; int count = 0;
        double Peak(double seconds) { int c = (int)Math.Round(seconds / frameSeconds), r = Math.Max(1, (int)(0.03 / frameSeconds)); double m = 0; for (int i = Math.Max(0, c - r); i <= Math.Min(n - 1, c + r); i++) m = Math.Max(m, env[i]); return m; }
        for (int i = 0; i + 1 < beats.Count; i++) { on += Peak(beats[i]); mid += Peak((beats[i] + beats[i + 1]) / 2); count++; }
        double lag = 60 / bpm / frameSeconds;
        return count > 8 && (mid >= 0.8 * on || mid >= 0.5 * on && AC(lag / 2) >= AC(lag));
    }

    /// <summary>Beat times (seconds) by dynamic programming (Ellis 2007): beats land on strong onsets while keeping
    /// close to the tempo, so a band that speeds up or slows down is still followed.</summary>
    public static List<double> TrackBeats(double[] env, double frameSeconds, double bpm, double tightness = 120)
    {
        int n = env.Length; double period = 60 / bpm / frameSeconds;
        var score = new double[n]; var back = new int[n];
        for (int t = 0; t < n; t++)
        {
            int from = Math.Max(0, t - (int)Math.Round(2 * period)), to = t - (int)Math.Round(period / 2);
            double best = 0; int arg = -1;
            for (int p = from; p <= to; p++)
            {
                double gap = Math.Log((t - p) / period), s = score[p] - tightness * gap * gap;
                if (arg < 0 || s > best) { best = s; arg = p; }
            }
            score[t] = env[t] + (arg >= 0 ? Math.Max(0, best) : 0); back[t] = arg >= 0 && best > 0 ? arg : -1;
        }
        // End on the best beat in the last period, then follow the links back.
        int end = n - 1; double top = double.MinValue;
        for (int t = Math.Max(0, n - (int)period - 1); t < n; t++) if (score[t] > top) { top = score[t]; end = t; }
        var beats = new List<double>();
        for (int t = end; t >= 0; t = back[t]) { beats.Add(t * frameSeconds); if (back[t] < 0) break; }
        beats.Reverse();
        return beats;
    }

    /// <summary>Beat positions (in beats, fractional) for any moment, from tracked beat times; before the first and after
    /// the last beat it continues at the average tempo.</summary>
    public sealed class BeatMap
    {
        readonly double[] times; public readonly double Period; public readonly int Offset;
        public BeatMap(List<double> beats, int offset)
        {
            times = beats.ToArray(); Offset = offset;
            Period = times.Length > 1 ? (times[^1] - times[0]) / (times.Length - 1) : 0.5;
        }
        public double Bpm => 60 / Period;
        public int Count => times.Length;
        /// <summary>Song beat (bar lines on multiples of the beats per bar) for a time in seconds.</summary>
        public double BeatAt(double seconds)
        {
            if (times.Length == 0) return seconds / Period + Offset;
            if (seconds <= times[0]) return (seconds - times[0]) / Period + Offset;
            if (seconds >= times[^1]) return times.Length - 1 + (seconds - times[^1]) / Period + Offset;
            int lo = 0, hi = times.Length - 1; while (hi - lo > 1) { int mid = (lo + hi) / 2; if (times[mid] <= seconds) lo = mid; else hi = mid; }
            return lo + (seconds - times[lo]) / (times[hi] - times[lo]) + Offset;
        }
    }

    // ---- Chords ----
    public static readonly string[] Qualities = ["", "m", "5"];
    public sealed record Chord(int Root, int Quality) { public string Name => Root < 0 ? "N" : MusicTheory.Keys[Root] + Qualities[Quality]; public int[] Tones => Quality switch { 0 => [0, 4, 7], 1 => [0, 3, 7], _ => [0, 7] }; }

    /// <summary>A 12-note chroma (how much of each note name sounds) for each beat, from 80 Hz to 1.6 kHz. Each frame's
    /// spectrum is loudness-compressed and flattened (its smooth outline removed) so the notes stand out and the kick,
    /// bass rumble and cymbal wash don't swamp them.</summary>
    public static double[][] BeatChroma(float[] x, BeatMap map, int beats)
    {
        const int size = 4096, hop = 1024; var mags = Magnitudes(x, size, hop);
        int lo = (int)(80.0 * size / Rate), hi = (int)(1600.0 * size / Rate);
        var pitchOfBin = new double[hi + 1]; for (int k = lo; k <= hi; k++) pitchOfBin[k] = 12 * Math.Log2(k * (double)Rate / size / 440) + 69;
        var chroma = new double[beats][]; for (int b = 0; b < beats; b++) chroma[b] = new double[12];
        var log = new double[hi + 1];
        for (int f = 0; f < mags.Length; f++)
        {
            double t = (f * hop + size / 2) / (double)Rate; int b = (int)Math.Floor(map.BeatAt(t)); if (b < 0 || b >= beats) continue;
            for (int k = lo; k <= hi; k++) log[k] = Math.Log(1 + 100 * mags[f][k]);
            for (int k = lo; k <= hi; k++)
            {
                // Flatten: compare each bin with the average around it (a third of an octave), keep only what stands above.
                int span = Math.Max(2, k / 16); double avg = 0; int c = 0;
                for (int j = Math.Max(lo, k - span); j <= Math.Min(hi, k + span); j++) { avg += log[j]; c++; }
                double v = log[k] - avg / c; if (v <= 0) continue;
                // Share between the two nearest note names by how close the bin is to each.
                double p = pitchOfBin[k]; int n0 = (int)Math.Floor(p); double frac = p - n0;
                chroma[b][((n0 % 12) + 12) % 12] += v * (1 - frac); chroma[b][(((n0 + 1) % 12) + 12) % 12] += v * frac;
            }
        }
        for (int b = 0; b < beats; b++) { double norm = Math.Sqrt(chroma[b].Sum(v => v * v)); if (norm > 0) for (int i = 0; i < 12; i++) chroma[b][i] /= norm; }
        return chroma;
    }
    /// <summary>The chord on each beat: major, minor or power chord templates, smoothed so chords don't flicker (Viterbi).
    /// The bass line (pitch-class weight per beat, 0 to 1) backs up the root: in a band the bass nearly always plays it.</summary>
    public static Chord[] RecogniseChords(double[][] chroma, double stay = 0.85, double[][]? bass = null)
    {
        var chords = new List<Chord>(); for (int r = 0; r < 12; r++) for (int q = 0; q < 3; q++) chords.Add(new Chord(r, q));
        int states = chords.Count, beats = chroma.Length; if (beats == 0) return [];
        var templates = chords.Select(c => { var t = new double[12]; foreach (var i in c.Tones) t[(c.Root + i) % 12] = i == 0 ? 1.2 : 1; double norm = Math.Sqrt(t.Sum(v => v * v)); return t.Select(v => v / norm).ToArray(); }).ToArray();
        double Emit(int b, int s) { double dot = 0; for (int i = 0; i < 12; i++) dot += chroma[b][i] * templates[s][i]; return 20 * dot + (bass != null && b < bass.Length ? 12 * bass[b][chords[s].Root] : 0); }
        double logStay = Math.Log(stay), logMove = Math.Log((1 - stay) / (states - 1));
        var score = new double[states]; var next = new double[states]; var back = new int[beats, states];
        for (int s = 0; s < states; s++) score[s] = Emit(0, s);
        for (int b = 1; b < beats; b++)
        {
            int bestPrev = 0; for (int s = 1; s < states; s++) if (score[s] > score[bestPrev]) bestPrev = s;
            for (int s = 0; s < states; s++)
            {
                double fromSame = score[s] + logStay, fromBest = score[bestPrev] + logMove;
                if (fromSame >= fromBest || bestPrev == s) { next[s] = fromSame; back[b, s] = s; } else { next[s] = fromBest; back[b, s] = bestPrev; }
                next[s] += Emit(b, s);
            }
            (score, next) = (next, score);
        }
        var path = new Chord[beats]; int cur = 0; for (int s = 1; s < states; s++) if (score[s] > score[cur]) cur = s;
        for (int b = beats - 1; b >= 0; b--) { path[b] = chords[cur]; cur = back[b, cur]; }
        // Silence or noise (no clear chord) stays empty.
        for (int b = 0; b < beats; b++) if (chroma[b].Sum() < 1e-6) path[b] = new Chord(-1, 0);
        return path;
    }

    /// <summary>Which of the beats starts the bar: bars usually begin where chords change and the kick/bass hits.</summary>
    public static int DownbeatPhase(Chord[] chords, double[] lowHits, int beatsPerBar = 4)
    {
        var score = new double[beatsPerBar];
        for (int b = 1; b < chords.Length; b++) if (chords[b] != chords[b - 1]) score[b % beatsPerBar] += 2;
        for (int b = 0; b < lowHits.Length; b++) score[b % beatsPerBar] += lowHits[b];
        int best = 0; for (int p = 1; p < beatsPerBar; p++) if (score[p] > score[best]) best = p;
        return best;
    }
}
