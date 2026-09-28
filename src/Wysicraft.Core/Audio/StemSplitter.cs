using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
namespace Wysicraft.Core.Audio;

/// <summary>Splits a recorded song into drums, bass, vocals and everything else (guitars, keys) with Meta's Demucs v4
/// (htdemucs, MIT licence), so each part can be turned into notes on its own. The model is 166 MB, so it isn't in the
/// installer: it's downloaded once, when first needed, into the user's app data folder.</summary>
public static class StemSplitter
{
    public const string ModelUrl = "https://huggingface.co/StemSplitio/htdemucs-onnx/resolve/main/htdemucs_fp16weights.onnx";
    public const long ModelBytes = 165_612_636;
    public const int Rate = 44100;
    const int Segment = 343_980; // 7.8 s, the length the model was exported for

    /// <summary>The four parts of a song, each mono at <see cref="SongAnalysis.Rate"/>.</summary>
    public sealed record Stems(float[] Drums, float[] Bass, float[] Other, float[] Vocals);

    public static string ModelPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wysicraft", "Models", "htdemucs_fp16weights.onnx");
    public static bool Available => File.Exists(ModelPath) && new FileInfo(ModelPath).Length == ModelBytes;

    /// <summary>Downloads the model (reporting 0 to 1). A partial download is thrown away, so nothing half-written is
    /// ever used.</summary>
    public static async Task Download(IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
        string part = ModelPath + ".part";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Wysicraft");
            using var response = await http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, cancel);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? ModelBytes, got = 0;
            await using (var from = await response.Content.ReadAsStreamAsync(cancel))
            await using (var to = File.Create(part))
            {
                var buffer = new byte[1 << 16]; int read;
                while ((read = await from.ReadAsync(buffer, cancel)) > 0)
                {
                    await to.WriteAsync(buffer.AsMemory(0, read), cancel); got += read;
                    progress?.Report(Math.Min(1, got / (double)total));
                }
            }
            if (new FileInfo(part).Length != ModelBytes) throw new InvalidDataException("The download was incomplete; try again.");
            File.Move(part, ModelPath, true);
        }
        finally { try { if (File.Exists(part)) File.Delete(part); } catch { } }
    }

    static InferenceSession? session;
    static readonly object sessionLock = new();
    static InferenceSession Session()
    {
        lock (sessionLock)
        {
            if (session != null) return session;
            if (!Available) throw new InvalidOperationException("The instrument splitter (Demucs) hasn't been downloaded.");
            var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
            return session = new InferenceSession(ModelPath, options);
        }
    }

    /// <summary>Splits the song (mono or stereo, any sample rate). Runs the model on 7.8 s pieces that overlap by a
    /// quarter and cross-fade, as the reference implementation does. Progress is reported 0 to 1.</summary>
    public static Stems Separate(float[][] channels, int sampleRate, Action<double>? progress = null)
    {
        var left = AudioTools.Resample(channels[0], sampleRate, Rate);
        var right = channels.Length > 1 ? AudioTools.Resample(channels[1], sampleRate, Rate) : left;
        int total = left.Length, overlap = Segment / 4, stride = Segment - overlap, pieces = Math.Max(1, (total + stride - 1) / stride);
        var window = new float[Segment];
        for (int i = 0; i < Segment; i++) window[i] = 1;
        for (int i = 0; i < overlap; i++) { float f = i / (float)(overlap - 1); window[i] = f; window[Segment - 1 - i] = f; }
        // Summed as mono per stem (half the memory of stereo; the notes are found from mono anyway).
        var output = new float[4][]; for (int s = 0; s < 4; s++) output[s] = new float[total];
        var weight = new float[total];
        var model = Session();
        for (int p = 0; p < pieces; p++)
        {
            int start = p * stride, length = Math.Min(Segment, total - start);
            if (length <= 0) break;
            var input = new DenseTensor<float>([1, 2, Segment]);
            var buffer = input.Buffer.Span;
            left.AsSpan(start, length).CopyTo(buffer[..length]);
            right.AsSpan(start, length).CopyTo(buffer.Slice(Segment, length));
            using var results = model.Run([NamedOnnxValue.CreateFromTensor("mix", input)]);
            var stems = results.First().AsTensor<float>() as DenseTensor<float> ?? throw new InvalidDataException("The instrument splitter returned an unexpected result.");
            var o = stems.Buffer.Span; // (1, 4, 2, Segment)
            for (int s = 0; s < 4; s++)
            {
                int l0 = (s * 2) * Segment, r0 = (s * 2 + 1) * Segment; var dest = output[s];
                for (int i = 0; i < length; i++) dest[start + i] += (o[l0 + i] + o[r0 + i]) * 0.5f * window[i];
            }
            for (int i = 0; i < length; i++) weight[start + i] += window[i];
            progress?.Invoke((p + 1) / (double)pieces);
        }
        for (int s = 0; s < 4; s++) { var d = output[s]; for (int i = 0; i < total; i++) d[i] /= Math.Max(weight[i], 1e-8f); }
        return new Stems(Down(output[0]), Down(output[1]), Down(output[2]), Down(output[3]));
        static float[] Down(float[] x) => AudioTools.Resample(x, Rate, SongAnalysis.Rate);
    }
}
