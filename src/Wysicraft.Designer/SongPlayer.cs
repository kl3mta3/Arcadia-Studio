using NAudio.Wave;
using Wysicraft.Core.Audio;
namespace Wysicraft.Designer;

/// <summary>Plays a song as it streams, rather than from a buffer rendered up front, so changes made while it plays
/// (mute, solo, volume, pan, instrument, echo, notes, tempo, loop, metronome) are heard straight away. The Music maker
/// hands it a fresh <see cref="Snapshot"/> whenever the song may have changed; notes already sounding finish with the
/// instrument they started with.</summary>
sealed class SongPlayer : ISampleProvider
{
    /// <summary>Key is the MIDI key a SoundFont plays for the note.</summary>
    public sealed record Event(int Step, int Track, double Pitch, double Velocity, int Length, int Key);
    public sealed class TrackMix
    {
        public required Instrument Instrument { get; init; }
        public float Left, Right; // 0 when muted or not soloed
        public (int Channel, int Bank, int Program) Patch; // with a SoundFont
    }
    /// <summary>Everything the audio thread reads: copied from the song on the UI thread, never changed afterwards.</summary>
    public sealed class Snapshot
    {
        public required Event[] Events { get; init; } // sorted by step
        public required TrackMix[] Tracks { get; init; }
        public double StepSeconds { get; init; }
        public int TotalSteps { get; init; }
        public int StepsPerBar { get; init; }
        public int StepsPerClick { get; init; }
        public bool Loop { get; init; }
        public bool Metronome { get; init; }
        /// <summary>The SoundFont the song plays with; null for the chip sounds.</summary>
        public MeltySynth.SoundFont? Font { get; init; }

        public static Snapshot Of(Song song, bool loop, bool metronome)
        {
            bool anySolo = song.Tracks.Any(t => t.Solo);
            var font = SoundFonts.Load(song.SoundFont);
            var tracks = new TrackMix[song.Tracks.Count]; var events = new List<Event>();
            for (int i = 0; i < song.Tracks.Count; i++)
            {
                var t = song.Tracks[i]; bool audible = !t.Mute && (!anySolo || t.Solo);
                double angle = (Math.Clamp(t.Pan, -1, 1) + 1) * Math.PI / 4, vol = audible ? t.Volume * song.Volume : 0;
                var patch = SoundFonts.Patch(t.Instrument);
                tracks[i] = font == null
                    ? new TrackMix { Instrument = t.Instrument, Left = (float)(Math.Cos(angle) * Math.Sqrt(2) * vol), Right = (float)(Math.Sin(angle) * Math.Sqrt(2) * vol) }
                    : new TrackMix { Instrument = t.Instrument, Patch = patch, Left = (float)(vol * SongRenderer.FontGain * Math.Min(1, 1 - t.Pan)), Right = (float)(vol * SongRenderer.FontGain * Math.Min(1, 1 + t.Pan)) };
                foreach (var n in t.Notes) events.Add(new Event(n.Step, i, n.Pitch + t.Transpose + t.Fine / 100, n.Velocity, n.Length, SoundFonts.Key(t, n, patch.Channel == 9)));
            }
            events.Sort((a, b) => a.Step.CompareTo(b.Step));
            return new Snapshot
            {
                Events = [.. events], Tracks = tracks, StepSeconds = song.StepSeconds, TotalSteps = song.TotalSteps, StepsPerBar = song.StepsPerBar,
                StepsPerClick = Math.Max(1, song.StepsPerBar / Math.Max(1, song.Numerator)), Loop = loop, Metronome = metronome, Font = font,
            };
        }
    }

    sealed class Sounding(Voice voice, int track) { public readonly Voice Voice = voice; public readonly int Track = track; }
    sealed class Echo { public float[] Line = []; public int At; }

    const int Rate = AudioOut.Rate;
    volatile Snapshot snapshot;
    readonly int loopFrom, countInEnd;
    readonly List<Sounding> voices = [];
    readonly List<(float[] Click, int At)> clicks = [];
    readonly float[] accent = SongRenderer.Click(true, Rate), beat = SongRenderer.Click(false, Rate);
    Echo?[] echoes = [];
    float[] mixL = [], mixR = [];
    double cursor; // song position in steps (fractional); below the start marker during a count-in
    int nextStep; // the next whole step whose notes haven't started
    bool ended; volatile bool stopped, finished;
    float gain = 1; // the limiter's gain
    long frames;
    // SoundFont playing: a synthesizer per layer (so each keeps its own instrument), and the notes to let go of.
    MeltySynth.SoundFont? synthFont;
    MeltySynth.Synthesizer?[] synths = [];
    (int Channel, int Bank, int Program)[] patches = [];
    readonly List<(int Step, int Track, int Channel, int Key)> offs = [];
    float[] fontL = [], fontR = [];
    int resumeAt;

    /// <param name="countInSteps">Steps of metronome before the song starts (a count-in for recording).</param>
    /// <param name="loopFrom">Where a loop goes back to (the start marker, when playing on from a pause).</param>
    public SongPlayer(Snapshot first, int fromStep, int countInSteps, int? loopFrom = null)
    {
        snapshot = first; this.loopFrom = loopFrom ?? fromStep; countInEnd = fromStep;
        cursor = fromStep - countInSteps; nextStep = (int)Math.Floor(cursor);
        // Notes that began before the start marker still play their remaining part.
        resumeAt = countInSteps == 0 ? fromStep : int.MinValue;
        if (countInSteps == 0 && first.Font == null) foreach (var e in first.Events)
        {
            if (e.Step >= fromStep || e.Step + e.Length <= fromStep || e.Track >= first.Tracks.Length) continue;
            var v = new Voice(first.Tracks[e.Track].Instrument, e.Pitch, e.Velocity, Rate, e.Length * first.StepSeconds);
            for (int skip = (int)((fromStep - e.Step) * first.StepSeconds * Rate); skip > 0 && !v.Done; skip--) v.Next();
            voices.Add(new Sounding(v, e.Track));
        }
    }

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2);
    double readStep; long readTime;
    /// <summary>The position (steps) of what's coming out of the speakers now: the last block handed over, moved on by
    /// the time since, less the output delay.</summary>
    public double Step
    {
        get
        {
            double seconds = (System.Diagnostics.Stopwatch.GetTimestamp() - Interlocked.Read(ref readTime)) / (double)System.Diagnostics.Stopwatch.Frequency;
            return Volatile.Read(ref readStep) + (Math.Min(seconds, 0.2) - AudioOut.Latency / 1000.0) / snapshot.StepSeconds;
        }
    }
    public double Seconds => Interlocked.Read(ref frames) / (double)Rate;
    public bool Finished => stopped || finished;
    public void Stop() => stopped = true;
    public void Update(Snapshot s) => snapshot = s;

    public int Read(float[] buffer, int offset, int count)
    {
        if (stopped) return 0;
        int n = count / 2; var s = snapshot;
        if (mixL.Length < n) { mixL = new float[n]; mixR = new float[n]; }
        if (echoes.Length != s.Tracks.Length) Array.Resize(ref echoes, s.Tracks.Length);
        Volatile.Write(ref readStep, cursor); Interlocked.Exchange(ref readTime, System.Diagnostics.Stopwatch.GetTimestamp());
        double perFrame = 1 / (s.StepSeconds * Rate);
        int done = 0;
        while (done < n)
        {
            // Start whatever falls on the next whole step, then play up to the one after.
            while (!ended && cursor >= nextStep) { StartStep(s, nextStep); nextStep++; }
            if (!ended && cursor >= s.TotalSteps)
            {
                ReleaseAll();
                if (s.Loop) { cursor = loopFrom; nextStep = loopFrom; continue; }
                ended = true;
            }
            int chunk = ended ? n - done : Math.Min(n - done, Math.Max(1, (int)Math.Ceiling((nextStep - cursor) / perFrame)));
            Render(s, done, chunk);
            cursor += chunk * perFrame; done += chunk;
        }
        for (int i = 0; i < n; i++)
        {
            float l = mixL[i], r = mixR[i], peak = Math.Max(Math.Abs(l), Math.Abs(r));
            if (peak * gain > 0.97f) gain = 0.97f / peak; else gain += (1 - gain) * 0.00005f;
            buffer[offset + 2 * i] = l * gain; buffer[offset + 2 * i + 1] = r * gain;
        }
        Interlocked.Add(ref frames, n);
        if (ended && voices.Count == 0 && clicks.Count == 0 && Silent(s) && synths.All(x => x == null || x.ActiveVoiceCount == 0)) { finished = true; }
        return n * 2;
    }

    void StartStep(Snapshot s, int step)
    {
        bool inSong = step >= countInEnd;
        if ((s.Metronome || !inSong) && ((step % s.StepsPerClick) + s.StepsPerClick) % s.StepsPerClick == 0)
            clicks.Add((((step % s.StepsPerBar) + s.StepsPerBar) % s.StepsPerBar == 0 ? accent : beat, 0));
        if (!inSong) return;
        var ev = s.Events;
        if (s.Font != null)
        {
            // Let go of the notes that end here, then start this step's (and, when starting mid-song, the ones already sounding).
            for (int i = offs.Count - 1; i >= 0; i--) if (offs[i].Step <= step) { var o = offs[i]; if (o.Track < synths.Length) synths[o.Track]?.NoteOff(o.Channel, o.Key); offs.RemoveAt(i); }
            bool resuming = step == resumeAt; resumeAt = int.MinValue;
            foreach (var e in ev)
            {
                if (e.Step > step) break;
                if (e.Step != step && !(resuming && e.Step + e.Length > step)) continue;
                if (e.Track >= s.Tracks.Length) continue;
                var synth = Synth(s, e.Track); var patch = s.Tracks[e.Track].Patch;
                synth.NoteOn(patch.Channel, e.Key, Math.Clamp((int)Math.Round(e.Velocity * 127), 1, 127));
                offs.Add((e.Step + e.Length, e.Track, patch.Channel, e.Key));
            }
            return;
        }
        // Binary search for the first note on this step.
        int lo = 0, hi = ev.Length;
        while (lo < hi) { int mid = (lo + hi) / 2; if (ev[mid].Step < step) lo = mid + 1; else hi = mid; }
        for (int i = lo; i < ev.Length && ev[i].Step == step; i++)
        {
            var e = ev[i]; if (e.Track >= s.Tracks.Length) continue;
            voices.Add(new Sounding(new Voice(s.Tracks[e.Track].Instrument, e.Pitch, e.Velocity, Rate, e.Length * s.StepSeconds), e.Track));
        }
    }

    /// <summary>The layer's synthesizer, made when first needed (again if the font changes), set to its instrument.</summary>
    MeltySynth.Synthesizer Synth(Snapshot s, int track)
    {
        if (synthFont != s.Font) { synths = []; patches = []; offs.Clear(); synthFont = s.Font; }
        if (synths.Length < s.Tracks.Length) { Array.Resize(ref synths, s.Tracks.Length); Array.Resize(ref patches, s.Tracks.Length); }
        bool fresh = synths[track] == null;
        var synth = synths[track] ??= SoundFonts.NewSynth(s.Font!, Rate);
        var patch = s.Tracks[track].Patch;
        if (fresh || patches[track] != patch) { SoundFonts.Select(synth, patch.Channel, patch.Bank, patch.Program); patches[track] = patch; }
        return synth;
    }
    void ReleaseAll()
    {
        foreach (var o in offs) if (o.Track < synths.Length) synths[o.Track]?.NoteOff(o.Channel, o.Key);
        offs.Clear();
    }

    float[] trackBuf = [];
    void Render(Snapshot s, int at, int length)
    {
        Array.Clear(mixL, at, length); Array.Clear(mixR, at, length);
        if (synthFont != null && synthFont != s.Font) { synths = []; patches = []; offs.Clear(); synthFont = null; }
        if (synths.Length > 0)
        {
            if (fontL.Length < length) { fontL = new float[Math.Max(length, 1024)]; fontR = new float[fontL.Length]; }
            for (int t = 0; t < synths.Length; t++)
            {
                var synth = synths[t]; if (synth == null) continue;
                synth.Render(fontL.AsSpan(0, length), fontR.AsSpan(0, length));
                float gl = t < s.Tracks.Length ? s.Tracks[t].Left : 0, gr = t < s.Tracks.Length ? s.Tracks[t].Right : 0;
                for (int i = 0; i < length; i++) { mixL[at + i] += fontL[i] * gl; mixR[at + i] += fontR[i] * gr; }
            }
        }
        if (trackBuf.Length < length) trackBuf = new float[Math.Max(length, 1024)];
        for (int t = 0; t < s.Tracks.Length; t++)
        {
            var mix = s.Tracks[t]; bool any = false;
            Array.Clear(trackBuf, 0, length);
            foreach (var v in voices)
            {
                if (v.Track != t) continue; any = true;
                for (int i = 0; i < length && !v.Voice.Done; i++) trackBuf[i] += v.Voice.Next();
            }
            // Echo per layer (the instrument's delay), kept running so its tail rings on after the notes.
            var ins = mix.Instrument; var echo = echoes[t];
            if (ins.Echo > 0)
            {
                int delay = Math.Max(1, (int)(ins.Echo * Rate));
                if (echo == null || echo.Line.Length != delay) echoes[t] = echo = new Echo { Line = new float[delay] };
                float fb = (float)Math.Clamp(ins.EchoFeedback, 0, 0.9);
                for (int i = 0; i < length; i++) { float y = trackBuf[i] + echo.Line[echo.At] * fb; echo.Line[echo.At] = y; echo.At = (echo.At + 1) % delay; trackBuf[i] = y; any = true; }
            }
            else echoes[t] = null;
            if (!any) continue;
            for (int i = 0; i < length; i++) { mixL[at + i] += trackBuf[i] * mix.Left; mixR[at + i] += trackBuf[i] * mix.Right; }
        }
        voices.RemoveAll(v => v.Voice.Done || v.Track >= s.Tracks.Length);
        for (int c = clicks.Count - 1; c >= 0; c--)
        {
            var (click, pos) = clicks[c]; int k = 0;
            for (; k < length && pos + k < click.Length; k++) { mixL[at + k] += click[pos + k]; mixR[at + k] += click[pos + k]; }
            if (pos + k >= click.Length) clicks.RemoveAt(c); else clicks[c] = (click, pos + k);
        }
    }

    /// <summary>True once every echo has died away.</summary>
    bool Silent(Snapshot s)
    {
        foreach (var e in echoes) if (e != null) foreach (var x in e.Line) if (Math.Abs(x) > 1e-4f) return false;
        return true;
    }
}
