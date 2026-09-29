using OggVorbisEncoder;
namespace Wysicraft.Core.Audio;

/// <summary>Audio files for the sound makers: 16-bit WAV (read and write) and Ogg Vorbis (write; the format Minecraft
/// plays, and the smallest for web and desktop apps).</summary>
public static class AudioFiles
{
    public static readonly string[] Formats = ["ogg", "wav"];
    public static byte[] Encode(string format, float[][] channels, int sampleRate, bool loop = false) => format == "wav" ? Wav(channels, sampleRate) : Ogg(channels, sampleRate, 0.5f, loop);

    public static byte[] Wav(float[][] channels, int sampleRate)
    {
        int count = channels.Length, frames = channels[0].Length, bytes = frames * count * 2;
        using var ms = new MemoryStream(44 + bytes); using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVE"u8); w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)count);
        w.Write(sampleRate); w.Write(sampleRate * count * 2); w.Write((short)(count * 2)); w.Write((short)16); w.Write("data"u8); w.Write(bytes);
        for (int i = 0; i < frames; i++) foreach (var c in channels) w.Write((short)Math.Round(Math.Clamp(c[i], -1f, 1f) * 32767));
        return ms.ToArray();
    }

    /// <summary>Reads PCM (8, 16, 24 or 32-bit) and 32-bit float WAV files.</summary>
    public static (float[][] Channels, int SampleRate) ReadWav(byte[] data)
    {
        using var r = new BinaryReader(new MemoryStream(data));
        if (data.Length < 12 || new string(r.ReadChars(4)) != "RIFF") throw new InvalidDataException("Not a WAV file.");
        r.ReadInt32(); if (new string(r.ReadChars(4)) != "WAVE") throw new InvalidDataException("Not a WAV file.");
        int format = 0, channels = 0, rate = 0, bits = 0; byte[]? pcm = null;
        while (r.BaseStream.Position + 8 <= data.Length)
        {
            string id = new(r.ReadChars(4)); int size = r.ReadInt32(); long next = r.BaseStream.Position + size + (size & 1);
            if (id == "fmt ") { format = r.ReadInt16(); channels = r.ReadInt16(); rate = r.ReadInt32(); r.ReadInt32(); r.ReadInt16(); bits = r.ReadInt16(); if (format == -2 && size >= 40) { r.ReadInt16(); r.ReadInt16(); r.ReadInt32(); format = r.ReadInt16(); } }
            else if (id == "data") pcm = r.ReadBytes((int)Math.Min(size, data.Length - r.BaseStream.Position));
            if (next > data.Length) break; r.BaseStream.Position = next;
        }
        if (pcm == null || channels < 1 || rate < 1000) throw new InvalidDataException("This WAV file has no readable audio.");
        if (!(format == 1 && bits is 8 or 16 or 24 or 32 || format == 3 && bits == 32)) throw new InvalidDataException("Only PCM and 32-bit float WAV files can be read.");
        int bytesPer = bits / 8, frames = pcm.Length / (bytesPer * channels); var result = new float[channels][];
        for (int c = 0; c < channels; c++) result[c] = new float[frames];
        for (int i = 0, o = 0; i < frames; i++)
            for (int c = 0; c < channels; c++, o += bytesPer)
                result[c][i] = format == 3 ? BitConverter.ToSingle(pcm, o) : bits switch
                {
                    8 => (pcm[o] - 128) / 128f,
                    16 => BitConverter.ToInt16(pcm, o) / 32768f,
                    24 => ((pcm[o] | pcm[o + 1] << 8 | pcm[o + 2] << 16) << 8 >> 8) / 8388608f,
                    _ => BitConverter.ToInt32(pcm, o) / 2147483648f
                };
        return (result, rate);
    }

    /// <summary>Ogg Vorbis at a variable bit rate (quality 0–1). The file decodes to exactly the samples given: the
    /// encoder drops the last block it is handed, so one block of lead-out is added for it to drop — silence, or for
    /// a loop the song's own start, so the samples before the join are encoded as they will be heard. A looping song
    /// that decoded short restarted mid-sound, which clicked and made the beat stumble at every repeat.</summary>
    public static byte[] Ogg(float[][] channels, int sampleRate, float quality = 0.5f, bool loop = false)
    {
        const int leadOut = 1024;
        int given = channels[0].Length;
        channels = channels.Select(c => { var padded = new float[given + leadOut]; Array.Copy(c, padded, given); if (loop && given > 0) for (int i = 0; i < leadOut; i++) padded[given + i] = c[i % given]; return padded; }).ToArray();
        var info = VorbisInfo.InitVariableBitRate(channels.Length, sampleRate, quality);
        var stream = new OggStream(new Random().Next());
        var comments = new Comments(); comments.AddTag("ENCODER", "Arcadia Studio");
        stream.PacketIn(HeaderPacketBuilder.BuildInfoPacket(info));
        stream.PacketIn(HeaderPacketBuilder.BuildCommentsPacket(comments));
        stream.PacketIn(HeaderPacketBuilder.BuildBooksPacket(info));
        using var output = new MemoryStream();
        void Flush(bool force) { while (stream.PageOut(out var page, force)) { output.Write(page.Header, 0, page.Header.Length); output.Write(page.Body, 0, page.Body.Length); } }
        Flush(true);
        var state = ProcessingState.Create(info); int frames = channels[0].Length; const int block = 1024;
        // Every block, then end-of-stream. (Stepping to frames exactly missed the end whenever the length was not a
        // whole number of blocks, and end-of-stream was never written.)
        for (int at = 0; at < frames; at += block)
        {
            state.WriteData(channels, Math.Min(block, frames - at), at);
            while (!stream.Finished && state.PacketOut(out var packet)) { stream.PacketIn(packet); Flush(false); }
        }
        state.WriteEndOfStream();
        while (!stream.Finished && state.PacketOut(out var last)) { stream.PacketIn(last); Flush(false); }
        Flush(true);
        return output.ToArray();
    }
}

public static class AudioTools
{
    public static float[] Mono(float[][] channels)
    {
        if (channels.Length == 1) return channels[0];
        var m = new float[channels[0].Length];
        for (int i = 0; i < m.Length; i++) { float s = 0; foreach (var c in channels) s += c[i]; m[i] = s / channels.Length; }
        return m;
    }
    /// <summary>Linear resampling (with a simple box filter when shrinking a lot, to keep it listenable).</summary>
    public static float[] Resample(float[] data, int from, int to)
    {
        if (from == to) return data;
        int n = (int)((long)data.Length * to / from); var r = new float[n]; double step = (double)from / to;
        int span = Math.Max(1, (int)Math.Floor(step));
        for (int i = 0; i < n; i++)
        {
            double pos = i * step; int a = (int)pos; double f = pos - a;
            if (span > 1) { float s = 0; int k = 0; for (; k < span && a + k < data.Length; k++) s += data[a + k]; r[i] = k > 0 ? s / k : 0; }
            else r[i] = (float)(a + 1 < data.Length ? data[a] * (1 - f) + data[a + 1] * f : a < data.Length ? data[a] : 0);
        }
        return r;
    }
    /// <summary>Turns any recording into a retro one: a low sample rate (the aliasing is part of the sound), fewer bits,
    /// and optionally squared off like a square-wave chip. Returns samples at targetRate.</summary>
    public static float[] Crunch(float[] mono, int sampleRate, int targetRate, int bits, bool normalize = true)
    {
        // Sample-and-hold decimation (no smoothing) for the crunchy character.
        int n = (int)((long)mono.Length * targetRate / sampleRate); var r = new float[n]; double step = (double)sampleRate / targetRate;
        for (int i = 0; i < n; i++) r[i] = mono[Math.Min(mono.Length - 1, (int)(i * step))];
        if (normalize) { float peak = 0; foreach (var s in r) peak = Math.Max(peak, Math.Abs(s)); if (peak > 0.0001f) for (int i = 0; i < n; i++) r[i] = r[i] / peak * 0.9f; }
        if (bits is >= 2 and < 16) { double q = Math.Pow(2, bits - 1); for (int i = 0; i < n; i++) r[i] = (float)(Math.Round(r[i] * q) / q); }
        return r;
    }
    /// <summary>Changes the sample rate by repeating each sample (no smoothing), so a crunched sound keeps its steps.</summary>
    public static float[] Hold(float[] data, int from, int to)
    {
        var r = new float[(int)((long)data.Length * to / from)]; double step = (double)from / to;
        for (int i = 0; i < r.Length; i++) r[i] = data[Math.Min(data.Length - 1, (int)(i * step))];
        return r;
    }
    public static float Peak(float[] data) { float p = 0; foreach (var s in data) p = Math.Max(p, Math.Abs(s)); return p; }
}
