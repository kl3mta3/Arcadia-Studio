using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Wysicraft.Core.Audio;
namespace Wysicraft.Designer;

/// <summary>Sound out of the speakers for the Music maker and Sound effect maker: one output stream with a mixer, so
/// rendered songs, effects and live keyboard notes all play at once with low delay.</summary>
sealed class AudioOut : IDisposable
{
    public const int Rate = SongRenderer.SampleRate;
    /// <summary>How far behind the speakers are (ms): recorded key presses are moved back by this much.</summary>
    public const int Latency = 70;
    readonly WaveOutEvent? output;
    readonly MixingSampleProvider mixer;
    public string? Problem { get; }

    public AudioOut()
    {
        mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2)) { ReadFully = true };
        try { output = new WaveOutEvent { DesiredLatency = Latency, NumberOfBuffers = 3 }; output.Init(mixer); output.Play(); }
        catch (Exception ex) { output = null; Problem = "No sound output: " + ex.Message; }
    }

    /// <summary>Plays stereo samples; the returned player reports its position and can be stopped.</summary>
    public BufferPlayer Play(float[] left, float[] right, double volume = 1)
    {
        var p = new BufferPlayer(left, right, (float)volume);
        if (output != null) mixer.AddMixerInput(p);
        return p;
    }
    public BufferPlayer Play(float[] mono, double volume = 1) => Play(mono, mono, volume);
    /// <summary>Adds any stream (such as a <see cref="SongPlayer"/>) to the output.</summary>
    public T Add<T>(T source) where T : ISampleProvider { if (output != null) mixer.AddMixerInput(source); return source; }
    /// <summary>A note that sounds until Release (like holding a key).</summary>
    public LiveNote NoteOn(Instrument instrument, double pitch, double velocity = 0.9, double pan = 0, double volume = 0.8)
    {
        var n = new LiveNote(new Voice(instrument, pitch, velocity, Rate), pan, volume);
        if (output != null) mixer.AddMixerInput(n);
        return n;
    }
    FontKeys? fontKeys;
    /// <summary>A key held on a SoundFont instrument (the keyboard and note previews when a song uses a font).</summary>
    public IHeldNote FontNote(MeltySynth.SoundFont font, (int Channel, int Bank, int Program) patch, int key, double velocity)
    {
        if (fontKeys == null || fontKeys.Font != font) { if (fontKeys != null) mixer.RemoveMixerInput(fontKeys); fontKeys = new FontKeys(font); if (output != null) mixer.AddMixerInput(fontKeys); }
        return fontKeys.Press(patch, key, Math.Clamp((int)Math.Round(velocity * 127), 1, 127));
    }
    public void StopAll() { mixer.RemoveAllMixerInputs(); fontKeys = null; }
    public void Dispose() { try { output?.Stop(); output?.Dispose(); } catch { } }
}

sealed class BufferPlayer(float[] left, float[] right, float volume) : ISampleProvider
{
    long position; volatile bool stopped;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(AudioOut.Rate, 2);
    /// <summary>Samples played so far (per channel).</summary>
    public long Position => Interlocked.Read(ref position);
    public double Seconds => Position / (double)AudioOut.Rate;
    public bool Finished => stopped || Position >= left.Length;
    public void Stop() => stopped = true;
    public int Read(float[] buffer, int offset, int count)
    {
        if (stopped) return 0;
        long at = Position; int frames = (int)Math.Min(count / 2, left.Length - at);
        for (int i = 0; i < frames; i++) { buffer[offset + 2 * i] = left[at + i] * volume; buffer[offset + 2 * i + 1] = right[at + i] * volume; }
        Interlocked.Add(ref position, frames);
        return frames * 2;
    }
}

interface IHeldNote { void Release(); }

/// <summary>Keys played live on a SoundFont: one synthesizer, melodic notes on channel 1 (switched to each note's
/// instrument as it starts) and drums on channel 10. Played from the UI thread, heard on the audio thread, so locked.</summary>
sealed class FontKeys(MeltySynth.SoundFont font) : ISampleProvider
{
    readonly MeltySynth.Synthesizer synth = SoundFonts.NewSynth(font, AudioOut.Rate);
    public MeltySynth.SoundFont Font { get; } = font;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(AudioOut.Rate, 2);
    float[] left = [], right = [];
    public IHeldNote Press((int Channel, int Bank, int Program) patch, int key, int velocity)
    {
        lock (synth) { SoundFonts.Select(synth, patch.Channel, patch.Bank, patch.Program); synth.NoteOn(patch.Channel, key, velocity); }
        return new Held(this, patch.Channel, key);
    }
    sealed class Held(FontKeys keys, int channel, int key) : IHeldNote { public void Release() { lock (keys.synth) keys.synth.NoteOff(channel, key); } }
    public int Read(float[] buffer, int offset, int count)
    {
        int frames = count / 2;
        if (left.Length < frames) { left = new float[frames]; right = new float[frames]; }
        lock (synth) synth.Render(left.AsSpan(0, frames), right.AsSpan(0, frames));
        for (int i = 0; i < frames; i++) { buffer[offset + 2 * i] = left[i] * SongRenderer.FontGain * 0.8f; buffer[offset + 2 * i + 1] = right[i] * SongRenderer.FontGain * 0.8f; }
        return count;
    }
}

sealed class LiveNote(Voice voice, double pan, double volume) : ISampleProvider, IHeldNote
{
    readonly float gl = (float)(Math.Cos((pan + 1) * Math.PI / 4) * Math.Sqrt(2) * volume), gr = (float)(Math.Sin((pan + 1) * Math.PI / 4) * Math.Sqrt(2) * volume);
    volatile bool release;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(AudioOut.Rate, 2);
    public void Release() => release = true;
    public int Read(float[] buffer, int offset, int count)
    {
        if (release) voice.Release();
        int frames = count / 2, i = 0;
        for (; i < frames && !voice.Done; i++) { float s = voice.Next(); buffer[offset + 2 * i] = s * gl; buffer[offset + 2 * i + 1] = s * gr; }
        return i * 2;
    }
}
