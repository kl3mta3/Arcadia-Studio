namespace Wysicraft.Core.Audio;

/// <summary>Turns a recording's melody into notes (like the bard tools that play songs from files): the loudest pitch at
/// each moment is found with the YIN method, steady stretches become notes, and a new note starts when the pitch changes
/// or the sound is struck again. Works best on one clear melody (a whistle, a voice, a solo instrument); chords and
/// full mixes come out as the strongest line only.</summary>
public static class Transcriber
{
    public sealed record Options(double Bpm = 120, int StepsPerBeat = 4, double MinPitchHz = 60, double MaxPitchHz = 1600, double Sensitivity = 0.5, int MinSteps = 1);

    public static List<Note> Notes(float[] mono, int sampleRate, Options o)
    {
        // Work at 16 kHz: plenty for melodies and much faster.
        const int rate = 16000; var x = AudioTools.Resample(mono, sampleRate, rate);
        const int frame = 1024, hop = 256; int frames = Math.Max(0, (x.Length - frame) / hop);
        var pitch = new double[frames]; var loud = new double[frames];
        int minLag = (int)(rate / o.MaxPitchHz), maxLag = Math.Min(frame / 2 - 1, (int)(rate / o.MinPitchHz));
        double threshold = 0.1 + 0.15 * (1 - Math.Clamp(o.Sensitivity, 0, 1));
        var d = new double[maxLag + 2];
        for (int f = 0; f < frames; f++)
        {
            int start = f * hop; double energy = 0;
            for (int i = 0; i < frame; i++) energy += x[start + i] * x[start + i];
            loud[f] = Math.Sqrt(energy / frame);
            // YIN difference function and its cumulative mean normalisation.
            for (int tau = 1; tau <= maxLag + 1; tau++)
            {
                double sum = 0; for (int i = 0; i < frame / 2; i++) { double diff = x[start + i] - x[start + i + tau]; sum += diff * diff; }
                d[tau] = sum;
            }
            double running = 0; d[0] = 1;
            for (int tau = 1; tau <= maxLag + 1; tau++) { running += d[tau]; d[tau] = running > 0 ? d[tau] * tau / running : 1; }
            int best = -1;
            for (int tau = Math.Max(2, minLag); tau <= maxLag; tau++)
                if (d[tau] < threshold) { while (tau + 1 <= maxLag && d[tau + 1] < d[tau]) tau++; best = tau; break; }
            if (best < 0) { pitch[f] = double.NaN; continue; }
            // Parabolic interpolation for a finer period.
            double a = d[best - 1], b = d[best], c = d[best + 1], shift = (a - 2 * b + c) == 0 ? 0 : 0.5 * (a - c) / (a - 2 * b + c);
            double hz = rate / (best + shift);
            pitch[f] = 69 + 12 * Math.Log2(hz / 440);
        }
        double maxLoud = loud.DefaultIfEmpty(0).Max(), gate = maxLoud * (0.04 + 0.12 * (1 - Math.Clamp(o.Sensitivity, 0, 1)));
        // Smooth: a median of 5 frames removes octave blips.
        var smooth = new double[frames];
        for (int f = 0; f < frames; f++)
        {
            if (loud[f] < gate || double.IsNaN(pitch[f])) { smooth[f] = double.NaN; continue; }
            var window = new List<double>(); for (int k = -2; k <= 2; k++) if (f + k >= 0 && f + k < frames && !double.IsNaN(pitch[f + k]) && loud[f + k] >= gate) window.Add(pitch[f + k]);
            window.Sort(); smooth[f] = window[window.Count / 2];
        }
        // Segment into notes.
        double stepSeconds = 60.0 / o.Bpm / o.StepsPerBeat, frameSeconds = (double)hop / rate;
        var notes = new List<Note>(); int begin = -1; int current = 0; double peak = 0;
        void Close(int endFrame)
        {
            if (begin < 0) return;
            int step = (int)Math.Round(begin * frameSeconds / stepSeconds), endStep = (int)Math.Round(endFrame * frameSeconds / stepSeconds);
            if (endStep - step >= o.MinSteps) notes.Add(new Note { Step = step, Length = Math.Max(1, endStep - step), Pitch = Math.Clamp(current, 0, 127), Velocity = Math.Round(Math.Clamp(0.35 + 0.65 * peak / Math.Max(1e-9, maxLoud), 0.2, 1), 2) });
            begin = -1;
        }
        for (int f = 0; f < frames; f++)
        {
            if (double.IsNaN(smooth[f])) { Close(f); continue; }
            int p = (int)Math.Round(smooth[f]);
            bool struck = f > 0 && loud[f] > loud[f - 1] * 1.8 && loud[f] > gate * 2; // re-articulated
            if (begin < 0) { begin = f; current = p; peak = loud[f]; }
            else if (p != current || struck) { Close(f); begin = f; current = p; peak = loud[f]; }
            else peak = Math.Max(peak, loud[f]);
        }
        Close(frames);
        // Neighbouring notes on the same pitch that now touch after snapping to the grid stay separate; overlaps are trimmed.
        for (int i = 0; i + 1 < notes.Count; i++) if (notes[i].Step + notes[i].Length > notes[i + 1].Step) notes[i].Length = Math.Max(1, notes[i + 1].Step - notes[i].Step);
        return notes.GroupBy(n => n.Step).Select(g => g.OrderByDescending(n => n.Length).First()).OrderBy(n => n.Step).ToList();
    }
}
