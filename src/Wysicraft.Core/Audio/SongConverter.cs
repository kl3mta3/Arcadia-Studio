using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
namespace Wysicraft.Core.Audio;

/// <summary>Turns a recorded song into an 8-bit song (a chiptune cover): every note, chords included, is found with
/// Spotify's Basic Pitch model (Apache-2.0, bundled), drums are found from bursts of low, middle and high sound, the
/// tempo and key are estimated, and the notes are split into a lead line, chords, bass and drums, each played by an
/// 8-bit instrument. The words of a song can't be played by a chip, so a singer becomes the lead melody.</summary>
public static class SongConverter
{
    /// <summary>Style: "arrange" (chords, bass, melody, drums, the way songs are arranged for chip or bard instruments) or
    /// "notes" (every note the detector heard). ChordRhythm: eighths, quarters or held. ChordVoicing: auto (power chords
    /// where no third is heard), power or triads.</summary>
    public sealed record Options(double Bpm = 0, int StepsPerBeat = 4, bool Lead = true, bool Chords = true, bool Bass = true, bool Drums = true,
        double Sensitivity = 0.5, bool RemoveVocals = false, int MaxChordNotes = 3, string Style = "arrange", string ChordRhythm = "eighths", string ChordVoicing = "auto",
        string SungNotes = "syllables");
    public sealed record Found(double Start, double End, int Pitch, double Amplitude);

    // ---- Notes: Basic Pitch ----
    const int ModelRate = 22050, Hop = 256, WindowSamples = 43844, WindowFrames = 172, Overlap = 30, OverlapSamples = Overlap * Hop;
    static readonly double FramesPerSecond = ModelRate / (double)Hop;
    static InferenceSession? session;
    static readonly object sessionLock = new();
    static InferenceSession Session()
    {
        lock (sessionLock)
        {
            if (session != null) return session;
            using var stream = typeof(SongConverter).Assembly.GetManifestResourceStream("basic-pitch.onnx") ?? throw new InvalidOperationException("The note detector is missing from this build.");
            using var ms = new MemoryStream(); stream.CopyTo(ms);
            return session = new InferenceSession(ms.ToArray());
        }
    }

    /// <summary>Every note in the audio (mono, any sample rate), in seconds.</summary>
    public static List<Found> DetectNotes(float[] mono, int sampleRate, double sensitivity = 0.5, Action<double>? progress = null)
    {
        var (noteFrames, onsetFrames, n) = Posteriors(mono, sampleRate, progress);
        // Thresholds from Basic Pitch's defaults, loosened or tightened by the sensitivity.
        double onsetThresh = Math.Max(0.15, 0.5 + (0.5 - sensitivity) * 0.4), frameThresh = Math.Max(0.1, 0.3 + (0.5 - sensitivity) * 0.2);
        var events = ToNotes(noteFrames, onsetFrames, n, onsetThresh, frameThresh, minFrames: 11, energyTol: 11);
        return events.Select(e => new Found(FrameTime(e.Start), FrameTime(e.End), e.Pitch, e.Amplitude)).OrderBy(f => f.Start).ToList();
    }
    // Frame to seconds, with Basic Pitch's small per-window correction.
    static readonly double WindowOffset = Hop / (double)ModelRate * (WindowFrames - WindowSamples / (double)Hop) + 0.0018;
    static double FrameTime(int frame) => frame / FramesPerSecond - WindowOffset * Math.Floor(frame / (double)WindowFrames);

    /// <summary>The model's raw output: how likely each of the 88 piano keys is sounding, and starting, in every frame
    /// (86 frames a second).</summary>
    static (float[,] Note, float[,] Onset, int Frames) Posteriors(float[] mono, int sampleRate, Action<double>? progress)
    {
        var audio = AudioTools.Resample(mono, sampleRate, ModelRate);
        int originalLength = audio.Length, hopSamples = WindowSamples - OverlapSamples;
        // Padding at the start, then windows that overlap by 30 frames; half the overlap is cut from each side of each result.
        var padded = new float[OverlapSamples / 2 + audio.Length + WindowSamples];
        Array.Copy(audio, 0, padded, OverlapSamples / 2, audio.Length);
        int windows = Math.Max(1, (int)Math.Ceiling((OverlapSamples / 2 + audio.Length) / (double)hopSamples));
        int keep = WindowFrames - Overlap, total = (int)Math.Floor(originalLength * FramesPerSecond / ModelRate);
        var noteFrames = new float[windows * keep, 88]; var onsetFrames = new float[windows * keep, 88];
        var model = Session(); string input = model.InputMetadata.Keys.First();
        const int batch = 8;
        for (int w0 = 0; w0 < windows; w0 += batch)
        {
            int count = Math.Min(batch, windows - w0);
            var tensor = new DenseTensor<float>([count, WindowSamples, 1]);
            for (int b = 0; b < count; b++) { int start = (w0 + b) * hopSamples; for (int i = 0; i < WindowSamples; i++) tensor[b, i, 0] = start + i < padded.Length ? padded[start + i] : 0; }
            using var results = model.Run([NamedOnnxValue.CreateFromTensor(input, tensor)]);
            var note = results.First(r => r.Name == "StatefulPartitionedCall:1").AsTensor<float>();
            var onset = results.First(r => r.Name == "StatefulPartitionedCall:2").AsTensor<float>();
            for (int b = 0; b < count; b++)
                for (int f = 0; f < keep; f++)
                    for (int k = 0; k < 88; k++) { int row = (w0 + b) * keep + f; noteFrames[row, k] = note[b, f + Overlap / 2, k]; onsetFrames[row, k] = onset[b, f + Overlap / 2, k]; }
            progress?.Invoke(Math.Min(1, (w0 + count) / (double)windows));
        }
        return (noteFrames, onsetFrames, Math.Min(total, windows * keep));
    }

    /// <summary>The sung line of an isolated voice: the strongest pitch in singing range, frame by frame, smoothed so
    /// vibrato and slides don't flicker between keys, then cut into notes wherever the pitch changes or a new syllable
    /// starts (the voice dips for a consonant and swells again). Only where the voice is really there: what leaks into
    /// a vocal part from the band is too quiet to count. Unlike <see cref="DetectNotes"/> this never gives two notes at once.</summary>
    /// <param name="bySyllable">One note per sung syllable (true), or a new note wherever the pitch changes or the voice dips (false).</param>
    public static List<Found> VoiceLine(float[] voice, int sampleRate, double sensitivity = 0.5, Action<double>? progress = null, bool bySyllable = true)
    {
        var (note, onset, n) = Posteriors(voice, sampleRate, progress);
        const int lowKey = 43 - 21, highKey = 86 - 21; // G2 to D6
        double on = Math.Max(0.08, 0.25 + (0.5 - sensitivity) * 0.2), syllable = Math.Max(0.2, 0.45 + (0.5 - sensitivity) * 0.3);
        // Loudness per frame (the model's frames are 256 samples apart at 22050 Hz).
        var audio = AudioTools.Resample(voice, sampleRate, ModelRate);
        var level = new double[n];
        for (int t = 0; t < n; t++)
        {
            int c = t * Hop; double s = 0; int count = 0;
            for (int i = Math.Max(0, c - Hop); i < Math.Min(audio.Length, c + Hop); i++) { s += audio[i] * audio[i]; count++; }
            level[t] = count > 0 ? Math.Sqrt(s / count) : 0;
        }
        var sorted = level.Where(v => v > 0).OrderBy(v => v).ToArray();
        double loud = sorted.Length > 0 ? sorted[(int)(sorted.Length * 0.9)] : 0, gate = loud * Math.Max(0.03, 0.14 - (sensitivity - 0.5) * 0.12);
        // The sung pitch through time (Viterbi): each frame's likely keys, with a cost for every jump, so the line only
        // leaps where the voice clearly moves (not to an overtone's shadow for a few frames). State 0 is "not singing".
        int keys = highKey - lowKey + 1, states = keys + 1;
        const double jumpCost = 0.25, switchCost = 0.9, voiceCost = 2.0;
        var smooth = new int[n]; var score = new double[states]; var next = new double[states]; var back = new byte[n, states];
        double Emit(int t, int s) => s == 0 ? Math.Log(on) : level[t] < gate ? -30 : Math.Log(Math.Max(note[t, lowKey + s - 1], 1e-4));
        for (int s0 = 0; s0 < states; s0++) score[s0] = Emit(0, s0);
        for (int t = 1; t < n; t++)
        {
            // Best voiced state to come from, for the jump costs (one pass each way keeps this linear in keys).
            for (int s1 = 0; s1 < states; s1++)
            {
                double best = score[s1]; int from = s1;
                if (s1 == 0) { for (int p = 1; p < states; p++) if (score[p] - voiceCost > best) { best = score[p] - voiceCost; from = p; } }
                else
                {
                    if (score[0] - voiceCost > best) { best = score[0] - voiceCost; from = 0; }
                    for (int p = 1; p < states; p++) { if (p == s1) continue; double v = score[p] - switchCost - jumpCost * Math.Abs(p - s1); if (v > best) { best = v; from = p; } }
                }
                next[s1] = best + Emit(t, s1); back[t, s1] = (byte)from;
            }
            (score, next) = (next, score);
        }
        int cur = 0; for (int s1 = 1; s1 < states; s1++) if (score[s1] > score[cur]) cur = s1;
        for (int t = n - 1; t >= 0; t--) { smooth[t] = cur == 0 ? -1 : lowKey + cur - 1; cur = back[t, cur]; }
        // Gaps of up to 3 frames inside a held pitch are breaths or consonants, not a new note.
        for (int t = 1; t < n; t++)
        {
            if (smooth[t] >= 0 || smooth[t - 1] < 0) continue;
            int e = t; while (e < n && e < t + 4 && smooth[e] < 0) e++;
            if (e < n && e < t + 4 && smooth[e] == smooth[t - 1]) for (int j = t; j < e; j++) smooth[j] = smooth[t - 1];
        }
        // A new syllable on the same pitch: the voice dips (to under 60% of the loudest just before and after) and swells.
        bool Syllable(int from, int at)
        {
            if (at - from < 6 || at + 1 >= n || level[at] > level[at - 1] || level[at] > level[at + 1]) return false;
            double before = 0, after = 0;
            for (int j = Math.Max(from, at - 12); j < at; j++) before = Math.Max(before, level[j]);
            for (int j = at + 1; j < Math.Min(n, at + 12); j++) after = Math.Max(after, level[j]);
            bool dip = level[at] < 0.6 * Math.Min(before, after);
            bool struck = onset[at, smooth[at]] >= syllable && onset[at, smooth[at]] >= onset[at - 1, smooth[at]] && onset[at, smooth[at]] >= onset[at + 1, smooth[at]];
            return dip || struck;
        }
        var found = new List<Found>(); int minFrames = (int)Math.Ceiling(0.04 * FramesPerSecond);
        for (int t = 0; t < n;)
        {
            if (smooth[t] < 0) { t++; continue; }
            int k = smooth[t], end = t + 1;
            while (end < n && smooth[end] == k && !Syllable(t, end)) end++;
            if (end - t >= minFrames)
            {
                double amp = 0; for (int j = t; j < end; j++) amp += note[j, k];
                found.Add(new Found(FrameTime(t), FrameTime(end), k + 21, amp / (end - t)));
            }
            t = end;
        }
        // How far the voice must drop between two vowels to count them as two syllables (less with more sensitivity).
        if (bySyllable) found = BySyllable(Math.Max(1, 3.5 - (sensitivity - 0.5) * 2));
        // One note per sung syllable: each syllable's vowel is a peak in loudness; the note runs from the quiet point
        // before it to the one after, and takes the pitch the vowel holds (not the scoop into it). A syllable that
        // moves to another pitch and holds it (a slur) becomes two notes.
        List<Found> BySyllable(double dipDb)
        {
            var db = new double[n];
            for (int t = 0; t < n; t++) { double m = 0; int c = 0; for (int j = Math.Max(0, t - 1); j <= Math.Min(n - 1, t + 1); j++) { m += level[j]; c++; } db[t] = 20 * Math.Log10(m / c + 1e-9); }
            var result = new List<Found>();
            int slur = (int)Math.Ceiling(0.12 * FramesPerSecond), shortest = (int)Math.Ceiling(0.05 * FramesPerSecond);
            for (int t = 0; t < n;)
            {
                if (smooth[t] < 0) { t++; continue; }
                int end = t; while (end < n && smooth[end] >= 0) end++; // a sung stretch
                // Vowel peaks: the loudest point within 45 ms, standing clear of the dip since the last one.
                var peaks = new List<int>();
                for (int p = t; p < end; p++)
                {
                    bool top = true; for (int j = Math.Max(t, p - 4); j <= Math.Min(end - 1, p + 4); j++) if (db[j] > db[p]) { top = false; break; }
                    if (!top) continue;
                    if (peaks.Count > 0)
                    {
                        int last = peaks[^1]; double dip = double.MaxValue; for (int j = last; j <= p; j++) dip = Math.Min(dip, db[j]);
                        if (Math.Min(db[last], db[p]) - dip < dipDb) { if (db[p] > db[last]) peaks[^1] = p; continue; }
                    }
                    peaks.Add(p);
                }
                if (peaks.Count == 0) peaks.Add(t);
                // Syllable edges: the quietest point between neighbouring vowels.
                var edges = new List<int> { t };
                for (int i = 1; i < peaks.Count; i++) { int at = peaks[i - 1]; for (int j = peaks[i - 1]; j <= peaks[i]; j++) if (db[j] < db[at]) at = j; edges.Add(at); }
                edges.Add(end);
                for (int i = 0; i + 1 < edges.Count; i++)
                {
                    int a = edges[i], b = edges[i + 1]; if (b - a < shortest) continue;
                    // Pitch runs inside the syllable; a run held long enough after the first is a slur to a new note.
                    var runs = new List<(int From, int To, int Key)>();
                    for (int j = a; j < b;) { int k0 = smooth[j], e = j; while (e < b && smooth[e] == k0) e++; runs.Add((j, e, k0)); j = e; }
                    var parts = new List<(int From, int To)>(); int from = a;
                    // The syllable's main pitch: the one held longest while loud (the vowel), ignoring the scoop.
                    for (int r = 1; r < runs.Count; r++) if (runs[r].To - runs[r].From >= slur && runs[r - 1].Key != runs[r].Key && runs.Take(r).Any(x => x.To - x.From >= slur / 2)) { parts.Add((from, runs[r].From)); from = runs[r].From; }
                    parts.Add((from, b));
                    foreach (var (pa, pb) in parts)
                    {
                        if (pb - pa < shortest) continue;
                        var weight = new Dictionary<int, double>();
                        for (int j = pa + (pb - pa) / 5; j < pb; j++) weight[smooth[j]] = weight.GetValueOrDefault(smooth[j]) + Math.Pow(10, db[j] / 20);
                        int key = weight.Count > 0 ? weight.MaxBy(w => w.Value).Key : smooth[pa];
                        double amp = 0; for (int j = pa; j < pb; j++) amp += note[j, key];
                        result.Add(new Found(FrameTime(pa), FrameTime(pb), key + 21, amp / (pb - pa)));
                    }
                }
                t = end;
            }
            return result;
        }
        // A short stretch (one note, or a few repeats of it) that leaps more than a fifth away from the notes on both
        // sides, on the same side, is an octave slip.
        for (int i = 1; i + 1 < found.Count; i++)
        {
            int j = i; while (j + 1 < found.Count && found[j + 1].Pitch == found[i].Pitch) j++;
            if (j + 1 >= found.Count) break;
            var (a, c) = (found[i - 1], found[j + 1]);
            if (found[j].End - found[i].Start > 0.45 || c.Start - a.End > 1.2) { i = j; continue; }
            int p = found[i].Pitch;
            while (p - Math.Max(a.Pitch, c.Pitch) > 7 && p - 12 >= Math.Min(a.Pitch, c.Pitch) - 7) p -= 12;
            while (Math.Min(a.Pitch, c.Pitch) - p > 7 && p + 12 <= Math.Max(a.Pitch, c.Pitch) + 7) p += 12;
            if (p != found[i].Pitch) for (int k = i; k <= j; k++) found[k] = found[k] with { Pitch = p };
            i = j;
        }
        return found;
    }

    // Basic Pitch's note creation (output_to_notes_polyphonic with inferred onsets and the "melodia trick").
    static List<(int Start, int End, int Pitch, double Amplitude)> ToNotes(float[,] frames, float[,] onsets, int n, double onsetThresh, double frameThresh, int minFrames, int energyTol)
    {
        const int keys = 88, midiOffset = 21;
        // Inferred onsets: where a note's energy jumps, as strong as the model's own onsets.
        var inferred = new float[n, keys]; double maxOnset = 0, maxDiff = 0;
        for (int t = 0; t < n; t++) for (int k = 0; k < keys; k++)
        {
            maxOnset = Math.Max(maxOnset, onsets[t, k]);
            if (t < 2) continue;
            double d = Math.Min(frames[t, k] - frames[t - 1, k], frames[t, k] - frames[t - 2, k]);
            if (d > 0) { inferred[t, k] = (float)d; maxDiff = Math.Max(maxDiff, d); }
        }
        var onset = new float[n, keys];
        for (int t = 0; t < n; t++) for (int k = 0; k < keys; k++) onset[t, k] = Math.Max(onsets[t, k], maxDiff > 0 ? (float)(inferred[t, k] * maxOnset / maxDiff) : 0);
        // Peaks in time above the onset threshold, latest first.
        var starts = new List<(int T, int K)>();
        for (int t = 1; t < n - 1; t++) for (int k = 0; k < keys; k++)
            if (onset[t, k] >= onsetThresh && onset[t, k] > onset[t - 1, k] && onset[t, k] > onset[t + 1, k]) starts.Add((t, k));
        starts.Sort((a, b) => b.T != a.T ? b.T.CompareTo(a.T) : b.K.CompareTo(a.K));
        var remaining = new float[n, keys]; Array.Copy(frames, remaining, n * keys);
        var notes = new List<(int, int, int, double)>();
        void Clear(int from, int to, int k) { for (int t = from; t < to; t++) { remaining[t, k] = 0; if (k < keys - 1) remaining[t, k + 1] = 0; if (k > 0) remaining[t, k - 1] = 0; } }
        double Mean(int from, int to, int k) { double s = 0; for (int t = from; t < to; t++) s += frames[t, k]; return to > from ? s / (to - from) : 0; }
        foreach (var (start, k) in starts)
        {
            if (start >= n - 1) continue;
            int i = start + 1, miss = 0;
            while (i < n - 1 && miss < energyTol) { miss = remaining[i, k] < frameThresh ? miss + 1 : 0; i++; }
            i -= miss;
            if (i - start <= minFrames) continue;
            Clear(start, i, k); notes.Add((start, i, k + midiOffset, Mean(start, i, k)));
        }
        // Melodia trick: strong stretches that had no clear start still become notes, strongest first.
        var cells = new List<(float E, int T, int K)>();
        for (int t = 0; t < n; t++) for (int k = 0; k < keys; k++) if (remaining[t, k] > frameThresh) cells.Add((remaining[t, k], t, k));
        cells.Sort((a, b) => b.E.CompareTo(a.E));
        foreach (var (_, mid, k) in cells)
        {
            if (remaining[mid, k] <= frameThresh) continue;
            remaining[mid, k] = 0;
            int i = mid + 1, miss = 0;
            while (i < n - 1 && miss < energyTol) { miss = remaining[i, k] < frameThresh ? miss + 1 : 0; remaining[i, k] = 0; if (k < keys - 1) remaining[i, k + 1] = 0; if (k > 0) remaining[i, k - 1] = 0; i++; }
            int end = i - 1 - miss;
            i = mid - 1; miss = 0;
            while (i > 0 && miss < energyTol) { miss = remaining[i, k] < frameThresh ? miss + 1 : 0; remaining[i, k] = 0; if (k < keys - 1) remaining[i, k + 1] = 0; if (k > 0) remaining[i, k - 1] = 0; i--; }
            int begin = i + 1 + miss;
            if (end - begin <= minFrames) continue;
            notes.Add((begin, end, k + midiOffset, Mean(begin, end, k)));
        }
        return notes;
    }

    // ---- Spectrum helpers ----
    static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++) { int bit = n >> 1; for (; (j & bit) != 0; bit >>= 1) j ^= bit; j ^= bit; if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); } }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int j = 0; j < len / 2; j++)
                {
                    double ur = re[i + j], ui = im[i + j], vr = re[i + j + len / 2] * cr - im[i + j + len / 2] * ci, vi = re[i + j + len / 2] * ci + im[i + j + len / 2] * cr;
                    re[i + j] = ur + vr; im[i + j] = ui + vi; re[i + j + len / 2] = ur - vr; im[i + j + len / 2] = ui - vi;
                    double nr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = nr;
                }
            }
        }
    }
    /// <summary>Energy in frequency bands per frame (1024-sample frames, 256 apart, at 22050 Hz).</summary>
    static double[][] BandEnergy(float[] audio, (double Lo, double Hi)[] bands, out double frameSeconds)
    {
        const int size = 1024, hop = 256; frameSeconds = hop / (double)ModelRate;
        int frames = Math.Max(0, (audio.Length - size) / hop); var result = bands.Select(_ => new double[frames]).ToArray();
        var window = Enumerable.Range(0, size).Select(i => 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / size)).ToArray();
        var re = new double[size]; var im = new double[size];
        for (int f = 0; f < frames; f++)
        {
            for (int i = 0; i < size; i++) { re[i] = audio[f * hop + i] * window[i]; im[i] = 0; }
            Fft(re, im);
            for (int b = 0; b < bands.Length; b++)
            {
                int lo = (int)(bands[b].Lo * size / ModelRate), hi = Math.Min(size / 2, (int)(bands[b].Hi * size / ModelRate)); double e = 0;
                for (int k = Math.Max(1, lo); k <= hi; k++) e += re[k] * re[k] + im[k] * im[k];
                result[b][f] = e;
            }
        }
        return result;
    }
    // Rises in loudness (log energy), smoothed a little.
    static double[] Flux(double[] energy)
    {
        var flux = new double[energy.Length];
        for (int i = 1; i < energy.Length; i++) flux[i] = Math.Max(0, Math.Log(energy[i] + 1e-6) - Math.Log(energy[i - 1] + 1e-6));
        return flux;
    }
    // Peaks that stand out from their surroundings, at least minGap frames apart.
    static List<int> Peaks(double[] f, double sensitivity, int minGap)
    {
        var peaks = new List<int>(); int w = 20; double k = Math.Max(0.3, 1.2 + (0.5 - sensitivity) * 1.2);
        double globalMean = f.DefaultIfEmpty(0).Average(), globalStd = Math.Sqrt(f.DefaultIfEmpty(0).Select(v => (v - globalMean) * (v - globalMean)).Average());
        for (int i = 1; i < f.Length - 1; i++)
        {
            if (f[i] <= f[i - 1] || f[i] < f[i + 1]) continue;
            int a = Math.Max(0, i - w), b = Math.Min(f.Length, i + w + 1); double mean = 0; for (int j = a; j < b; j++) mean += f[j]; mean /= b - a;
            if (f[i] < mean + k * globalStd * 0.5 || f[i] < globalMean + 0.5 * globalStd) continue;
            if (peaks.Count > 0 && i - peaks[^1] < minGap) { if (f[i] > f[peaks[^1]]) peaks[^1] = i; continue; }
            peaks.Add(i);
        }
        return peaks;
    }

    /// <summary>Kick (36), snare (38) and hi-hat (42) hits, in seconds.</summary>
    public static List<(double Time, int Drum, double Strength)> DetectDrums(float[] mono, int sampleRate, double sensitivity = 0.5)
    {
        var audio = AudioTools.Resample(mono, sampleRate, ModelRate);
        var bands = BandEnergy(audio, [(35, 130), (1500, 5000), (7000, 11000)], out double sec);
        var hits = new List<(double, int, double)>();
        int[] drums = [36, 38, 42]; int[] gaps = [(int)(0.09 / sec), (int)(0.09 / sec), (int)(0.06 / sec)];
        for (int b = 0; b < 3; b++)
        {
            var flux = Flux(bands[b]); double max = flux.DefaultIfEmpty(0).Max();
            foreach (var p in Peaks(flux, sensitivity, gaps[b])) hits.Add((p * sec, drums[b], max > 0 ? flux[p] / max : 0));
        }
        return hits;
    }

    /// <summary>Drum hits from a drums-only part (a Demucs stem): every hit is found once, from the whole kit's rise in
    /// loudness, then told apart by its sound: a low thump is a kick, a mid-range body with a crack is a snare, highs
    /// alone are a hi-hat or cymbal. (Read band by band, a snare's crack would also count as a hi-hat.)</summary>
    public static List<(double Time, int Drum, double Strength)> DetectKit(float[] drums, int sampleRate, double sensitivity = 0.5)
    {
        var audio = AudioTools.Resample(drums, sampleRate, ModelRate);
        var bands = BandEnergy(audio, [(40, 120), (150, 350), (1500, 4000), (7000, 11000)], out double sec);
        var flux = bands.Select(Flux).ToArray();
        int frames = flux[0].Length;
        var all = new double[frames]; for (int f = 0; f < frames; f++) for (int b = 0; b < 4; b++) all[f] += flux[b][f];
        var onsets = Peaks(all, sensitivity, (int)(0.05 / sec));
        // Each band's rise at each hit (the most within 2 frames), scaled by that band's typical strong hit.
        var rise = onsets.Select(p => Enumerable.Range(0, 4).Select(b => { double m = 0; for (int f = Math.Max(0, p - 2); f <= Math.Min(frames - 1, p + 2); f++) m = Math.Max(m, flux[b][f]); return m; }).ToArray()).ToList();
        var typical = Enumerable.Range(0, 4).Select(b => { var v = rise.Select(r => r[b]).OrderBy(x => x).ToList(); return v.Count > 0 ? Math.Max(1e-6, v[(int)(v.Count * 0.9)]) : 1; }).ToArray();
        var hits = new List<(double, int, double)>();
        for (int i = 0; i < onsets.Count; i++)
        {
            double kick = rise[i][0] / typical[0], body = rise[i][1] / typical[1], crack = rise[i][2] / typical[2], high = rise[i][3] / typical[3];
            double t = onsets[i] * sec;
            bool isKick = kick >= 0.5;
            // A snare's body isn't just the kick's own thump carrying into the mid range.
            bool isSnare = crack >= 0.45 && body >= 0.35 && body - 0.5 * kick >= 0.15 && crack >= 0.7 * high;
            bool isHat = !isSnare && high >= 0.4;
            if (isKick) hits.Add((t, 36, Math.Min(1, kick)));
            if (isSnare) hits.Add((t, 38, Math.Min(1, (crack + body) / 2)));
            else if (isHat) hits.Add((t, 42, Math.Min(1, high)));
        }
        return hits;
    }

    /// <summary>The drummer's pattern rather than every hit heard: hits are gathered per bar on eighth-note slots, and
    /// each bar plays the slots its neighbourhood (a few bars either side) hits most. Cymbal washes, flams and guitar
    /// noise in the drum bands drop out, leaving a steady kick, snare and hi-hat groove.</summary>
    /// <param name="perBeat">Slots per beat: 2 (eighth notes), or 4 when the beat is slow, so the hi-hat can keep time between hits.</param>
    static List<Note> Groove(List<(double Time, int Drum, double Strength)> hits, SongAnalysis.BeatMap map, int spb, int totalBeats, int perBeat = 2)
    {
        int slots = 4 * perBeat, half = Math.Max(1, spb / perBeat), bars = (totalBeats + 3) / 4;
        int[] kinds = [36, 38, 42]; var grid = new double[kinds.Length, bars, slots];
        foreach (var h in hits)
        {
            int slot = (int)Math.Round(map.BeatAt(h.Time) * perBeat), k = Array.IndexOf(kinds, h.Drum);
            if (slot < 0 || k < 0 || slot / slots >= bars) continue;
            grid[k, slot / slots, slot % slots] = Math.Max(grid[k, slot / slots, slot % slots], h.Strength);
        }
        var list = new List<Note>();
        for (int bar = 0; bar < bars; bar++)
        {
            double activity = 0; for (int k = 0; k < kinds.Length; k++) for (int s = 0; s < slots; s++) activity += grid[k, bar, s];
            if (activity < 0.6) continue; // the drums aren't playing here
            var share = new double[kinds.Length, slots];
            for (int k = 0; k < kinds.Length; k++)
            {
                var avg = new double[slots];
                for (int b = Math.Max(0, bar - 4); b <= Math.Min(bars - 1, bar + 3); b++) for (int s = 0; s < slots; s++) avg[s] += grid[k, b, s];
                double top = avg.Max(); if (top > 0) for (int s = 0; s < slots; s++) share[k, s] = avg[s] / top;
            }
            for (int s = 0; s < slots; s++)
            {
                // Kick or snare on a slot, not both: whichever is the more typical of its drum there.
                bool kick = share[0, s] >= 0.6, snare = share[1, s] >= 0.6;
                if (kick && snare) { if (share[0, s] >= share[1, s]) snare = false; else kick = false; }
                int step = bar * 4 * spb + s * half; double vel = s % (2 * perBeat) == 0 ? 0.9 : s % perBeat == 0 ? 0.8 : 0.65;
                if (kick) list.Add(new Note { Step = step, Length = 1, Pitch = 36, Velocity = vel });
                if (snare) list.Add(new Note { Step = step, Length = 1, Pitch = 38, Velocity = vel });
                if (!snare && share[2, s] >= 0.3) // one noise channel: a hat never sounds with the snare
                list.Add(new Note { Step = step, Length = 1, Pitch = 42, Velocity = vel * 0.8 });
            }
        }
        return list;
    }

    /// <summary>The key that fits the notes best (Krumhansl–Schmuckler profiles).</summary>
    public static (string Key, string Scale) EstimateKey(IEnumerable<Note> notes)
    {
        double[] major = [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88], minor = [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17];
        var hist = new double[12]; foreach (var n in notes) hist[((n.Pitch % 12) + 12) % 12] += n.Length;
        if (hist.Sum() == 0) return ("C", "major");
        double Corr(double[] p, int root) { double mx = hist.Average(), my = p.Average(), sxy = 0, sx = 0, sy = 0; for (int i = 0; i < 12; i++) { double x = hist[(i + root) % 12] - mx, y = p[i] - my; sxy += x * y; sx += x * x; sy += y * y; } return sxy / Math.Sqrt(sx * sy + 1e-12); }
        var best = Enumerable.Range(0, 12).SelectMany(r => new[] { (r, "major", Corr(major, r)), (r, "minor", Corr(minor, r)) }).MaxBy(x => x.Item3);
        return (MusicTheory.Keys[best.r], best.Item2);
    }

    /// <summary>The whole conversion: audio in, a song of 8-bit layers out.
    /// Arrangement style (the default) plays the song the way people arrange it for chip or bard instruments: the chord
    /// on every beat as steady power chords or triads, the bass on the roots, the vocal melody, and the drums.
    /// "Every note" style keeps everything the note detector heard instead.</summary>
    /// <param name="stems">The song already split into drums, bass, other and vocals (<see cref="StemSplitter"/>): each
    /// part is then found from its own stem, so the singing becomes the melody, the bass guitar the bass line, and the
    /// guitars the chords, far more reliably than from the whole mix.</param>
    public static Song Convert(float[][] channels, int sampleRate, string name, Options o, Action<string>? progress = null, StemSplitter.Stems? stems = null)
    {
        var left = AudioTools.Resample(channels[0], sampleRate, SongAnalysis.Rate);
        var right = channels.Length > 1 ? AudioTools.Resample(channels[1], sampleRate, SongAnalysis.Rate) : left;
        var mono = new float[left.Length]; for (int i = 0; i < mono.Length; i++) mono[i] = (left[i] + right[i]) * 0.5f;
        int rate = SongAnalysis.Rate, spb = o.StepsPerBeat;
        // The sensitivity slider (0 to 1) reaches further than the detectors' own scale: its middle catches quiet notes
        // and every sung syllable, which is what makes a song recognisable; its top goes further still.
        double sens = Math.Clamp(o.Sensitivity, 0, 1) + 0.5;

        // With stems, the beat and drums come from the drum part (plus the bass, which locks to the kick).
        float[] beatSource = mono, drumSource = mono;
        if (stems != null && AudioTools.Peak(stems.Drums) > 0.02)
        {
            drumSource = stems.Drums; beatSource = new float[Math.Min(stems.Drums.Length, stems.Bass.Length)];
            for (int i = 0; i < beatSource.Length; i++) beatSource[i] = stems.Drums[i] + 0.5f * stems.Bass[i];
        }
        progress?.Invoke("Following the beat…");
        var env = SongAnalysis.OnsetEnvelope(beatSource, out double sec);
        double bpm = o.Bpm > 0 ? o.Bpm : SongAnalysis.Tempo(env, sec);
        var beats = SongAnalysis.TrackBeats(env, sec, bpm);
        progress?.Invoke("Listening for drums…");
        var hits = drumSource != mono ? DetectKit(drumSource, rate, sens) : DetectDrums(drumSource, rate, sens); // also used to find the beats the bars start on
        if (o.Bpm <= 0 && bpm * 2 > 160 && bpm * 2 <= 230 && SongAnalysis.FasterPulse(env, sec, bpm))
        {
            // Counted in half time: the fast pulse is the steadier one to follow, then every other beat is kept, the
            // ones the kick drum lands on (following the slow beat directly can lock onto the snare instead).
            var fast = SongAnalysis.TrackBeats(env, sec, bpm * 2); var fastMap = new SongAnalysis.BeatMap(fast, 0);
            var kick = new double[2];
            foreach (var h in hits.Where(h => h.Drum == 36)) { int b = (int)Math.Round(fastMap.BeatAt(h.Time)); if (b >= 0) kick[b % 2] += h.Strength; }
            int kept = kick[1] > kick[0] ? 1 : 0;
            beats = fast.Where((_, i) => i % 2 == kept).ToList();
        }
        if (beats.Count < 8) throw new InvalidOperationException("The recording is too short or too quiet to follow its beat.");

        // Voice in the middle, guitars at the sides (when the mix is really stereo).
        float[] melodySource = mono, harmonySource = mono;
        if (stems != null) { melodySource = o.RemoveVocals ? stems.Other : stems.Vocals; harmonySource = stems.Other; }
        else if (channels.Length > 1)
        {
            progress?.Invoke("Separating the voice from the band…");
            var (centre, sides, width) = SongAnalysis.CentreAndSides(left, right);
            if (width > 0.06) { melodySource = centre; harmonySource = sides; }
            if (o.RemoveVocals) melodySource = sides;
        }

        progress?.Invoke("Naming the chords…");
        var rough = new SongAnalysis.BeatMap(beats, 0);
        var chroma = SongAnalysis.BeatChroma(harmonySource, rough, beats.Count);
        progress?.Invoke("Following the bass line…");
        var bassWeight = new double[beats.Count][];
        for (int b = 0; b < beats.Count; b++) bassWeight[b] = new double[12];
        var bassFound = DetectNotes(stems?.Bass ?? mono, rate, sens, p => progress?.Invoke($"Following the bass line… {p * 100:0}%")).Where(f => f.Pitch < (stems != null ? 60 : 52)).ToList();
        foreach (var f in bassFound)
        {
            int from = Math.Max(0, (int)Math.Floor(rough.BeatAt(f.Start))), to = Math.Min(beats.Count - 1, (int)Math.Floor(rough.BeatAt(f.End)));
            for (int b = from; b <= to; b++)
            {
                double start = beats[b], end = b + 1 < beats.Count ? beats[b + 1] : start + 60 / bpm;
                double overlap = Math.Min(end, f.End) - Math.Max(start, f.Start);
                if (overlap > 0) bassWeight[b][f.Pitch % 12] += overlap / (end - start) * f.Amplitude * (f.Pitch < 45 ? 1.5 : 1); // the lowest notes count most
            }
        }
        foreach (var v in bassWeight) { double top = v.Max(); if (top > 0) for (int i = 0; i < 12; i++) v[i] /= top; }
        var chords = SongAnalysis.RecogniseChords(chroma, bass: bassWeight);
        var low = new double[beats.Count];
        foreach (var h in hits.Where(h => h.Drum == 36)) { int b = (int)Math.Round(rough.BeatAt(h.Time)); if (b >= 0 && b < low.Length) low[b] += h.Strength; }
        int phase = SongAnalysis.DownbeatPhase(chords, low), offset = (4 - phase) % 4;
        var map = new SongAnalysis.BeatMap(beats, offset);
        // A grid fine enough for quick sung syllables: steps of at most a tenth of a second (a song counted in half time
        // gets 32nd-note steps). Only when the tempo is found here, so the notes can still go into a song's own grid.
        if (o.Bpm <= 0) while (spb < 8 && 60 / map.Bpm / spb > 0.1) spb *= 2;
        int Step(double seconds) => Math.Max(0, (int)Math.Round(map.BeatAt(seconds) * spb));
        SongAnalysis.Chord ChordAt(int songBeat) { int b = songBeat - offset; return b >= 0 && b < chords.Length ? chords[b] : new SongAnalysis.Chord(-1, 0); }
        int totalBeats = beats.Count + offset;

        var song = new Song { Name = name, Bpm = Math.Round(map.Bpm), StepsPerBeat = spb, Numerator = 4, Denominator = 4 };
        // Key: from the chords when there are any (worked out now, so chord shapes can follow it).
        var keyNotes = chords.Where(c => c.Root >= 0).SelectMany(c => c.Tones.Select(t => new Note { Pitch = 60 + (c.Root + t) % 12, Length = 1 })).ToList();
        if (keyNotes.Count > 0) (song.Key, song.Scale) = EstimateKey(keyNotes);
        void Add(string layer, string instrument, List<Note> list, double volume, double pan = 0) { if (list.Count > 0) song.Tracks.Add(new Track { Name = layer, Instrument = Instruments.Get(instrument), Notes = list.OrderBy(n => n.Step).ThenBy(n => n.Pitch).ToList(), Volume = volume, Pan = pan }); }

        // Melody: the strongest single line in singing range, from the voice channel.
        List<Note> Melody()
        {
            progress?.Invoke("Listening for the melody…");
            if (stems != null && !o.RemoveVocals)
            {
                // The singer, alone: one pitch at a time, a note per syllable.
                var sung = VoiceLine(stems.Vocals, rate, sens, f => progress?.Invoke($"Listening for the melody… {f * 100:0}%"), o.SungNotes != "pitch");
                if (sung.Count == 0) return [];
                // Octave slips (a breathy note heard an octave off) fold back towards the singer's usual range.
                var pitches = sung.Select(f => f.Pitch).OrderBy(p => p).ToList(); int middle = pitches[pitches.Count / 2];
                var sungNotes = sung.Select(f =>
                {
                    int p = f.Pitch; while (p > middle + 14) p -= 12; while (p < middle - 14) p += 12;
                    return new Note { Step = Step(f.Start), Length = Math.Max(1, Step(f.End) - Step(f.Start)), Pitch = p, Velocity = Math.Round(Math.Clamp(0.55 + f.Amplitude * 0.5, 0.55, 1), 2) };
                });
                var sungLine = Mono(sungNotes.GroupBy(n => n.Step).Select(g => g.MaxBy(n => n.Length)!), lowest: false);
                for (int i = 0; i < sungLine.Count; i++)
                {
                    // A note sung between two keys of the scale (a bent or slid note) takes the nearer one, leaning
                    // towards the note before it.
                    var nt = sungLine[i];
                    if (keyNotes.Count > 0 && !MusicTheory.InScale(nt.Pitch, song.Key, song.Scale))
                    {
                        int before = i > 0 ? sungLine[i - 1].Pitch : nt.Pitch;
                        nt.Pitch = before < nt.Pitch && MusicTheory.InScale(nt.Pitch - 1, song.Key, song.Scale) || !MusicTheory.InScale(nt.Pitch + 1, song.Key, song.Scale) ? nt.Pitch - 1 : nt.Pitch + 1;
                    }
                    // Sung legato: a note holds on to the next one when the gap is shorter than an eighth note.
                    if (i + 1 < sungLine.Count) { int gap = sungLine[i + 1].Step - (nt.Step + nt.Length); if (gap > 0 && gap <= Math.Max(1, spb / 2)) nt.Length += gap; }
                }
                return sungLine;
            }
            var voice = SongAnalysis.BandPass(melodySource, 180, 4000);
            var found = DetectNotes(voice, rate, sens + 0.2, f => progress?.Invoke($"Listening for the melody… {f * 100:0}%"))
                .Where(f => f.Pitch is >= 52 and <= 88 && f.End - f.Start >= 0.08).OrderBy(f => f.End).ToList();
            // Best set of notes that don't overlap (weighted interval scheduling): loud, long notes win.
            int n = found.Count; var best = new double[n + 1]; var take = new bool[n + 1]; var prev = new int[n];
            for (int j = 0; j < n; j++) { int p = j - 1; while (p >= 0 && found[p].End > found[j].Start + 0.02) p--; prev[j] = p; }
            for (int j = 1; j <= n; j++)
            {
                var f = found[j - 1]; double with = f.Amplitude * Math.Sqrt(f.End - f.Start) + best[prev[j - 1] + 1];
                if (with > best[j - 1]) { best[j] = with; take[j] = true; } else best[j] = best[j - 1];
            }
            var chosen = new List<Found>(); for (int j = n; j > 0;) { if (take[j]) { chosen.Add(found[j - 1]); j = prev[j - 1] + 1; } else j--; }
            var line = chosen.Select(f => new Note { Step = Step(f.Start), Length = Math.Max(1, Step(f.End) - Step(f.Start)), Pitch = f.Pitch, Velocity = Math.Round(Math.Clamp(0.5 + f.Amplitude, 0.5, 1), 2) }).ToList();
            return Mono(line, lowest: false);
        }

        if (o.Style == "notes")
        {
            // Everything the note detector heard, on the tracked beat grid.
            progress?.Invoke("Listening for notes…");
            var found = DetectNotes(stems?.Other ?? mono, rate, sens, f => progress?.Invoke($"Listening for notes… {f * 100:0}%"));
            if (stems != null) found.AddRange(bassFound);
            var notes = found.Select(f => new Note { Step = Step(f.Start), Length = Math.Max(1, Step(f.End) - Step(f.Start)), Pitch = f.Pitch, Velocity = Math.Round(Math.Clamp(0.4 + f.Amplitude, 0.3, 1), 2) }).ToList();
            var low2 = notes.Where(n => n.Pitch < 60).OrderBy(n => n.Step).ToList(); var bassLine = low2.Where(n => !low2.Any(m => m != n && m.Pitch < n.Pitch && m.Step <= n.Step && m.Step + m.Length > n.Step)).ToList();
            var bass = Mono(bassLine, lowest: true);
            var used = new HashSet<Note>(bass); var rest = new List<Note>();
            foreach (var n in notes.Where(n => !used.Contains(n)).OrderByDescending(n => n.Velocity)) if (rest.Count(c => c.Step < n.Step + n.Length && n.Step < c.Step + c.Length) < o.MaxChordNotes + 1) rest.Add(n);
            if (o.Lead) Add("Lead", "Square lead", Melody(), 0.7, 0.1);
            if (o.Chords) Add("Notes", "Pulse 25%", rest, 0.4, -0.2);
            if (o.Bass) Add("Bass", "Triangle bass", bass, 0.9);
        }
        else
        {
            // Chords, played the arranger's way.
            if (o.Chords)
            {
                var list = new List<Note>(); int half = Math.Max(1, spb / 2);
                for (int b = 0; b < totalBeats; b++)
                {
                    var c = ChordAt(b); if (c.Root < 0) continue;
                    // Automatic: a power chord when the chord's third isn't in the key (a rock band's D in C major), a triad otherwise.
                    bool power = o.ChordVoicing == "power" || o.ChordVoicing != "triads" && (c.Quality == 2 || keyNotes.Count > 0 && !MusicTheory.InScale(c.Root + (c.Quality == 1 ? 3 : 4), song.Key, song.Scale));
                    int root = 53 + ((c.Root - 53 % 12 + 12) % 12); // the chord's root between F3 and E4
                    int[] voice = power ? [root, root + 7, root + 12] : c.Quality == 1 ? [root, root + 3, root + 7] : [root, root + 4, root + 7];
                    int start = b * spb;
                    if (o.ChordRhythm == "held")
                    {
                        if (b > 0 && ChordAt(b - 1) == c) continue; // continues the chord already held
                        int end = b + 1; while (end < totalBeats && ChordAt(end) == c) end++;
                        foreach (var p in voice) list.Add(new Note { Step = start, Length = (end - b) * spb, Pitch = p, Velocity = 0.75 });
                    }
                    else if (o.ChordRhythm == "quarters") foreach (var p in voice) list.Add(new Note { Step = start, Length = spb, Pitch = p, Velocity = 0.75 });
                    else for (int e = 0; e < 2; e++) foreach (var p in voice) list.Add(new Note { Step = start + e * half, Length = half, Pitch = p, Velocity = e == 0 ? 0.8 : 0.65 });
                }
                Add("Chords", "Pulse 25%", list, 0.36, -0.2);
            }
            if (o.Bass && stems != null && bassFound.Count > 0)
            {
                // The bass guitar's own notes, from its stem, played as a bass player would: on a steady pulse.
                Add("Bass", "Triangle bass", BassPulse(bassFound, map, spb, map.Bpm < 120 && spb >= 4 ? 4 : 2), 0.85);
            }
            else if (o.Bass)
            {
                var list = new List<Note>(); int half = Math.Max(1, spb / 2);
                for (int b = 0; b < totalBeats; b++)
                {
                    var c = ChordAt(b); if (c.Root < 0) continue;
                    int root = 36 + c.Root; // C2 to B2
                    if (o.ChordRhythm == "held") { if (b > 0 && ChordAt(b - 1) == c) continue; int end = b + 1; while (end < totalBeats && ChordAt(end) == c) end++; list.Add(new Note { Step = b * spb, Length = (end - b) * spb, Pitch = root, Velocity = 0.85 }); }
                    else for (int e = 0; e < 2; e++) list.Add(new Note { Step = b * spb + e * half, Length = half, Pitch = root, Velocity = e == 0 ? 0.9 : 0.75 });
                }
                Add("Bass", "Triangle bass", list, 0.85);
            }
            if (o.Lead) Add("Melody", "Square lead", Melody(), 0.72, 0.1);
        }
        if (o.Drums)
        {
            var list = o.Style == "notes"
                ? hits.Select(h => new Note { Step = Step(h.Time), Length = 1, Pitch = h.Drum, Velocity = Math.Round(Math.Clamp(0.5 + h.Strength * 0.5, 0.5, 1), 2) }).GroupBy(n => (n.Step, n.Pitch)).Select(g => g.First()).ToList()
                : Groove(hits, map, spb, totalBeats);
            Add("Drums", "Drum kit", list, 0.6);
        }
        if (song.Tracks.Count == 0) throw new InvalidOperationException("No notes, chords or drums were found. Try a higher sensitivity.");
        // Key: from the chords when there are any, otherwise from the notes.
        if (keyNotes.Count == 0) (song.Key, song.Scale) = EstimateKey(song.Tracks.SelectMany(t => t.Notes));
        song.Bars = Math.Min(512, song.BarsNeeded());
        if (song.Seconds > 600) throw new InvalidOperationException("The song is longer than 10 minutes; use a shorter piece.");
        return song;
    }
    /// <summary>A bass line on a steady pulse: each slot (an eighth note, or a sixteenth when the beat is slow) plays the
    /// bass note heard most in it, kept in one octave near the notes around it (the detector often hears a bass note an
    /// octave off). A slot starts a new note where the bass is plucked again, and holds the note on where it rings.</summary>
    static List<Note> BassPulse(List<Found> found, SongAnalysis.BeatMap map, int spb, int perBeat)
    {
        int stepsPerSlot = Math.Max(1, spb / perBeat);
        double Slot(double seconds) => map.BeatAt(seconds) * perBeat;
        int last = found.Count == 0 ? 0 : (int)Math.Ceiling(found.Max(f => Slot(f.End)));
        var weight = new Dictionary<int, double>[last + 1]; var plucked = new bool[last + 1];
        foreach (var f in found)
        {
            double a = Slot(f.Start), b = Slot(f.End); if (b <= 0) continue;
            int first = Math.Max(0, (int)Math.Floor(a)), end = Math.Min(last, (int)Math.Ceiling(b) - 1);
            for (int slot = first; slot <= end; slot++)
            {
                double overlap = Math.Min(b, slot + 1) - Math.Max(a, slot); if (overlap <= 0) continue;
                (weight[slot] ??= [])[f.Pitch] = weight[slot].GetValueOrDefault(f.Pitch) + overlap * f.Amplitude;
            }
            int onset = (int)Math.Round(a); if (onset >= 0 && onset <= last) plucked[onset] = true;
        }
        var notes = new List<Note>(); int previous = -1; Note? current = null;
        for (int slot = 0; slot <= last; slot++)
        {
            var w = weight[slot];
            if (w == null || w.Values.Sum() < 0.3 * (w.Count > 0 ? w.Values.Max() : 1) || w.Values.Sum() < 0.05) { current = null; continue; }
            // The strongest pitch class, in the octave nearest the line so far (between E1 and G3).
            var byClass = w.GroupBy(p => p.Key % 12).Select(g => (Class: g.Key, Weight: g.Sum(p => p.Value), Low: g.Min(p => p.Key))).MaxBy(g => g.Weight);
            int pitch = byClass.Low;
            if (previous >= 0) { while (pitch - previous > 6) pitch -= 12; while (previous - pitch > 6) pitch += 12; }
            while (pitch < 28) pitch += 12; while (pitch > 55) pitch -= 12;
            double velocity = Math.Round(Math.Clamp(0.6 + Math.Min(0.4, byClass.Weight * 0.5), 0.6, 1), 2);
            if (current != null && current.Pitch == pitch && !plucked[slot]) current.Length += stepsPerSlot;
            else { current = new Note { Step = slot * stepsPerSlot, Length = stepsPerSlot, Pitch = pitch, Velocity = velocity }; notes.Add(current); }
            previous = pitch;
        }
        return notes;
    }

    // One note at a time: overlapping notes are cut where the next one starts (keeping the lowest or highest when two start together).
    static List<Note> Mono(IEnumerable<Note> source, bool lowest)
    {
        var list = source.GroupBy(n => n.Step).Select(g => lowest ? g.MinBy(n => n.Pitch)! : g.MaxBy(n => n.Pitch)!).OrderBy(n => n.Step).ToList();
        for (int i = 0; i + 1 < list.Count; i++) if (list[i].Step + list[i].Length > list[i + 1].Step) list[i].Length = Math.Max(1, list[i + 1].Step - list[i].Step);
        return list;
    }
}
