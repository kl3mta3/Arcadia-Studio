using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Processing;
using Microsoft.ML.OnnxRuntime;
using NAudio.Wave;
using Whisper.net;
using Wysicraft.Core;
namespace Wysicraft.Designer;

/// <summary>Where the speech models are. The standard ones come with the app (Speech/ beside the install, artifacts/speech in
/// a development checkout): Kokoro (fp16), its voices and Whisper tiny. Larger ones are installed on request into
/// %LOCALAPPDATA%\Arcadia Studio\Speech, each checked by its SHA-256.</summary>
static class SpeechFiles
{
    public static string? Bundled()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            foreach (var relative in new[] { "Speech", "artifacts/speech" })
            { string path = Path.Combine(directory.FullName, relative); if (File.Exists(Path.Combine(path, StandardVoiceModel))) return path; }
        return null;
    }
    public static string Installed => AppFolders.Path("Speech");
    public const string StandardVoiceModel = "kokoro-fp16.onnx", FullVoiceModel = "kokoro.onnx";

    public sealed record Download(string Id, string Title, string File, long Bytes, string Url, string Sha256, string About);
    /// <summary>The higher-precision Kokoro model: the same voices, very slightly cleaner, twice the size and the same speed.</summary>
    public static readonly Download FullVoice = new("kokoro-full", "Kokoro full precision", FullVoiceModel, 325_508_342,
        "https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/download/v2.0.0/kokoro.onnx", "0cfd5e79aab70a3d8c1a57dc639835110ddb32c9f5ff4fdd1f4db202ea43bb05", "Same voices, full 32-bit precision");
    /// <summary>Whisper models, smallest first. Tiny comes with the app.</summary>
    public static readonly Download[] Whisper =
    [
        new("tiny", "Tiny", "ggml-tiny.bin", 77_691_713, "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin", "be07e048e1e599ad46341c8d2a135645097a538221678b7acdd1b1919c6e1b21", "Comes with Arcadia Studio · fastest"),
        new("base", "Base", "ggml-base.bin", 147_951_465, "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin", "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe", "Better with names and accents · about 2× slower"),
        new("small", "Small", "ggml-small.bin", 487_601_967, "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin", "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b", "Much better in noise and other languages · about 6× slower"),
        new("medium", "Medium", "ggml-medium.bin", 1_533_763_059, "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin", "6c14d5adee5f86394037b4e4e8b59f1673b6cee10e3cf0b11bbdbee79c156208", "Very accurate · slow without a fast processor"),
        new("large-v3-turbo", "Large v3 Turbo", "ggml-large-v3-turbo.bin", 1_624_555_275, "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo.bin", "1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69", "The most accurate · slowest"),
    ];

    /// <summary>A model's file if it's here (bundled or installed), else null.</summary>
    public static string? Find(string file)
    {
        foreach (var root in new[] { Bundled(), Installed })
        {
            if (root == null) continue;
            foreach (var path in new[] { Path.Combine(root, file), Path.Combine(root, "whisper", file) }) if (File.Exists(path)) return path;
        }
        return null;
    }
    public static bool IsBundled(Download d) => Bundled() is string root && (File.Exists(Path.Combine(root, d.File)) || File.Exists(Path.Combine(root, "whisper", d.File)));
    public static bool Has(Download d) => Find(d.File) != null;
    public static string? VoicesFolder() => Bundled() is string root && Directory.Exists(Path.Combine(root, "voices")) ? Path.Combine(root, "voices") : null;

    static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    /// <summary>Downloads a model into the Speech folder, checking its SHA-256; a partial or wrong download is removed.</summary>
    public static async Task InstallAsync(Download d, IProgress<double>? progress, CancellationToken cancel)
    {
        string folder = d.File.StartsWith("ggml-") ? Path.Combine(Installed, "whisper") : Installed;
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, d.File), part = target + ".part";
        try
        {
            using (var response = await Http.GetAsync(d.Url, HttpCompletionOption.ResponseHeadersRead, cancel))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? d.Bytes;
                await using var input = await response.Content.ReadAsStreamAsync(cancel);
                await using var output = File.Create(part);
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[1 << 20]; long done = 0; int n;
                while ((n = await input.ReadAsync(buffer, cancel)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, n), cancel); sha.AppendData(buffer, 0, n);
                    done += n; progress?.Report(done / (double)total);
                }
                if (Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant() != d.Sha256) throw new InvalidDataException(d.Title + " didn't download correctly (its checksum doesn't match). Try again.");
            }
            File.Move(part, target, true);
        }
        finally { try { if (File.Exists(part)) File.Delete(part); } catch { } }
    }
    public static void Remove(Download d)
    {
        if (IsBundled(d)) throw new InvalidOperationException(d.Title + " comes with Arcadia Studio and can't be removed.");
        foreach (var path in new[] { Path.Combine(Installed, d.File), Path.Combine(Installed, "whisper", d.File) }) if (File.Exists(path)) File.Delete(path);
    }
}

/// <summary>A Kokoro voice for the app: its ID (af_heart), a readable name, language and gender.</summary>
sealed record VoiceInfo(string Id, string Name, string Language, string Gender, string Accent);

/// <summary>Text to speech with Kokoro (82M, ONNX, all in C#: KokoroSharp with MisakiSharp for pronunciation; no Python and
/// no espeak). Everything runs on one dedicated thread: text is cut into sentences, and each is spoken as soon as it's
/// ready, so speech starts in a fraction of a second while the rest is still being made. Playback is NAudio's own
/// audio thread. Nothing here touches the UI thread.</summary>
sealed class TextToSpeech : IDisposable
{
    public const int Rate = 24000;
    public static TextToSpeech Shared { get; } = new();

    readonly BlockingCollection<Action> jobs = new();
    readonly Thread worker;
    KokoroModel? model; string loaded = "";
    bool voicesLoaded;
    public string? Problem { get; private set; }

    TextToSpeech()
    {
        worker = new Thread(() => { foreach (var job in jobs.GetConsumingEnumerable()) { try { job(); } catch (Exception) { } } })
        { IsBackground = true, Name = "Kokoro speech", Priority = ThreadPriority.AboveNormal };
        worker.Start();
    }

    /// <summary>Whether the model and voices that come with the app are here.</summary>
    public static bool Available => SpeechFiles.Bundled() != null && SpeechFiles.VoicesFolder() != null;
    public static bool UseFullModel { get; set; }

    Task<T> Run<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        jobs.Add(() => { try { done.SetResult(work()); } catch (Exception ex) { done.SetException(ex); } });
        return done.Task;
    }

    /// <summary>Loads the model ahead of time (about two seconds, once) so the first words come quickly.</summary>
    public Task WarmAsync() => Run(() => { Ready(); return 0; });

    void Ready()
    {
        if (!Available) throw new InvalidOperationException("The voice model isn't installed with this copy of Arcadia Studio (it comes in its Speech folder). Reinstall Arcadia Studio to get it back.");
        if (!voicesLoaded) { KokoroVoiceManager.LoadVoicesFromPath(SpeechFiles.VoicesFolder()!); voicesLoaded = true; }
        string path = (UseFullModel ? SpeechFiles.Find(SpeechFiles.FullVoiceModel) : null) ?? Path.Combine(SpeechFiles.Bundled()!, SpeechFiles.StandardVoiceModel);
        if (model != null && loaded == path) return;
        model?.Dispose(); model = null;
        var options = new SessionOptions { LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR, IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 2, 8) };
        model = new KokoroModel(path, options); loaded = path;
        // Pronunciation tables load on first use; do it now too, so the first sentence isn't the slow one.
        Tokenizer.Tokenize("Ready.", "en-us");
    }

    // ---- Voices ----
    static readonly Dictionary<char, (string Language, string Accent)> Languages = new()
    {
        ['a'] = ("English", "American"), ['b'] = ("English", "British"), ['e'] = ("Spanish", ""), ['f'] = ("French", ""), ['h'] = ("Hindi", ""),
        ['i'] = ("Italian", ""), ['j'] = ("Japanese", ""), ['p'] = ("Portuguese", "Brazilian"), ['z'] = ("Chinese", "Mandarin"),
    };
    /// <summary>Every voice that came with the app, English first.</summary>
    public static List<VoiceInfo> Voices()
    {
        var folder = SpeechFiles.VoicesFolder();
        if (folder == null) return [];
        return Directory.GetFiles(folder, "*.npy").Select(Path.GetFileNameWithoutExtension).Where(n => n!.Length > 3 && n[2] == '_' && Languages.ContainsKey(n[0])).Select(n => Describe(n!))
            .OrderBy(v => v.Language != "English").ThenBy(v => v.Language).ThenBy(v => v.Accent).ThenBy(v => v.Gender).ThenBy(v => v.Name).ToList();
    }
    public static VoiceInfo Describe(string id)
    {
        var (language, accent) = Languages.TryGetValue(id[0], out var l) ? l : ("English", "American");
        string gender = id.Length > 1 && id[1] == 'm' ? "Male" : "Female";
        string name = id.Length > 3 ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id[3..].Replace('_', ' ')) : id;
        return new VoiceInfo(id, name, language, gender, accent);
    }
    public const string DefaultVoice = "af_heart";

    /// <summary>A voice spec: one ID (af_heart), or a mix of voices with weights ("af_heart*0.6+am_michael*0.4"; weights
    /// default to equal). The first voice sets the language.</summary>
    public static List<(string Id, float Weight)> ParseVoice(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) spec = DefaultVoice;
        var known = Voices().Select(v => v.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var parts = new List<(string, float)>();
        foreach (var raw in spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bits = raw.Split('*', 2, StringSplitOptions.TrimEntries);
            string id = bits[0].ToLowerInvariant();
            if (!known.Contains(id)) throw new InvalidDataException($"There's no voice \"{bits[0]}\". Voices look like af_heart, am_michael or bf_emma (speech action voices lists them all).");
            float weight = 1;
            if (bits.Length > 1 && (!float.TryParse(bits[1], NumberStyles.Float, CultureInfo.InvariantCulture, out weight) || weight <= 0 || weight > 10)) throw new InvalidDataException($"\"{raw}\": a voice's weight is a number above 0, like af_heart*0.7.");
            parts.Add((id, weight));
        }
        if (parts.Count == 0) parts.Add((DefaultVoice, 1));
        if (parts.Count > 4) throw new InvalidDataException("Mix up to 4 voices.");
        float total = parts.Sum(p => p.Item2);
        return parts.Select(p => (p.Item1, p.Item2 / total)).ToList();
    }
    /// <summary>The spec written the same way every time (weights to two places, dropped for a single voice).</summary>
    public static string Normal(string spec)
    {
        var parts = ParseVoice(spec);
        return parts.Count == 1 ? parts[0].Id : string.Join("+", parts.Select(p => p.Id + "*" + p.Weight.ToString("0.##", CultureInfo.InvariantCulture)));
    }
    readonly Dictionary<string, KokoroVoice> mixes = new();
    KokoroVoice Voice(string spec)
    {
        string key = Normal(spec);
        if (mixes.TryGetValue(key, out var v)) return v;
        var parts = ParseVoice(spec);
        v = parts.Count == 1 ? KokoroVoiceManager.GetVoice(parts[0].Id) : KokoroVoiceManager.Mix(parts.Select(p => (KokoroVoiceManager.GetVoice(p.Id), p.Weight)).ToArray());
        if (mixes.Count > 32) mixes.Clear();
        return mixes[key] = v;
    }

    // ---- Making speech ----
    static readonly Regex Sentences = new(@"(?<=[.!?…。！？])\s+|(?<=\n)\s*", RegexOptions.Compiled);

    /// <summary>Typographic apostrophes and quotes made plain, so "I’ll" is read as I'll (the pronunciation tables only know
    /// the plain apostrophe; with a curly one the "ll" is spelled out letter by letter). Text from word processors and AI
    /// assistants is full of them.</summary>
    internal static string Plain(string text) => text
        .Replace('’', '\'').Replace('‘', '\'').Replace('ʼ', '\'').Replace('‛', '\'').Replace('′', '\'').Replace('＇', '\'')
        .Replace('“', '"').Replace('”', '"').Replace('„', '"').Replace('‟', '"').Replace('″', '"')
        .Replace("…", "...");

    /// <summary>What in the text may not be read the way it's meant, with how to write it instead (empty when it reads well).</summary>
    internal static List<string> Advice(string text)
    {
        var advice = new List<string>();
        void Add(bool found, string note) { if (found && !advice.Contains(note)) advice.Add(note); }
        Add(Regex.IsMatch(text, @"https?://|www\.|\b[\w-]+\.(com|io|net|org|dev|gg)\b", RegexOptions.IgnoreCase), "Links and web addresses are read character by character: write them as said, e.g. \"itch dot io\".");
        Add(Regex.IsMatch(text, @"[*_#`~|<>\[\]{}]"), "Markdown and symbols (* _ # ` | < > [ ] { }) are read out or skipped: write plain sentences.");
        Add(Regex.IsMatch(text, @"\p{Cs}|[☀-➿]"), "Emoji are skipped or read oddly: leave them out.");
        Add(Regex.IsMatch(text, @"\b[A-Z]{2,5}\b(?<!\bOK\b)"), "All-capital abbreviations may be spelled out or misread: write them as said, e.g. \"W, A, S, D\", \"M C P\" or \"N P C\".");
        Add(Regex.IsMatch(text, @"[@&%$€£+=/\\]"), "Symbols such as @ & % $ + = / are read as words or skipped: write them out (\"and\", \"percent\", \"plus\").");
        Add(Regex.IsMatch(text, @"\d+[:.,]\d+|\d{5,}"), "Times, decimals and long numbers may be read digit by digit: write them as said, e.g. \"two thirty\" or \"one point five\".");
        return advice;
    }

    /// <summary>The text in the pieces it's spoken in, each with the pause after it in seconds (at normal speed).</summary>
    internal static List<(string Text, double Pause)> Pieces(string text)
    {
        text = Plain(text);
        var pieces = new List<(string, double)>();
        foreach (var paragraph in Regex.Split(text.Replace("\r", ""), @"\n\s*\n"))
        {
            var sentences = Sentences.Split(paragraph).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            for (int i = 0; i < sentences.Count; i++)
            {
                // Long sentences are cut at commas so no piece runs past what the model takes at once (and the first
                // words come sooner).
                var chunks = Split(sentences[i], 220);
                for (int c = 0; c < chunks.Count; c++) pieces.Add((chunks[c], c < chunks.Count - 1 ? 0.12 : i < sentences.Count - 1 ? 0.3 : 0.55));
            }
        }
        if (pieces.Count > 0) pieces[^1] = (pieces[^1].Item1, 0);
        return pieces;
    }
    static List<string> Split(string sentence, int max)
    {
        if (sentence.Length <= max) return [sentence];
        var result = new List<string>(); var current = new StringBuilder();
        foreach (var part in Regex.Split(sentence, @"(?<=[,;:—])\s+"))
        {
            if (current.Length > 0 && current.Length + part.Length + 1 > max) { result.Add(current.ToString()); current.Clear(); }
            if (part.Length > max)
                foreach (var word in part.Split(' ')) { if (current.Length + word.Length + 1 > max && current.Length > 0) { result.Add(current.ToString()); current.Clear(); } current.Append(current.Length > 0 ? " " : "").Append(word); }
            else current.Append(current.Length > 0 ? " " : "").Append(part);
        }
        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }

    /// <summary>Makes speech piece by piece on the speech thread, handing each piece (24 kHz mono, pause included) to onPiece
    /// as soon as it's ready. Stops early when cancelled.</summary>
    public Task Make(string text, string voice, double speed, Action<float[]> onPiece, CancellationToken cancel) => Run(() =>
    {
        Ready();
        var v = Voice(voice);
        string language = KokoroLangCodeHelper.GetLangCode(v);
        float s = (float)Math.Clamp(speed, 0.5, 2.0);
        foreach (var (piece, pause) in Pieces(text))
        {
            cancel.ThrowIfCancellationRequested();
            var tokens = Tokenizer.Tokenize(piece, language);
            if (tokens.Length == 0) continue;
            if (tokens.Length > KokoroModel.maxTokens) tokens = tokens[..KokoroModel.maxTokens];
            var samples = Trim(model!.Infer(tokens, v.Features, s));
            int gap = (int)(pause / s * Rate);
            var withPause = new float[samples.Length + gap];
            samples.CopyTo(withPause, 0);
            onPiece(withPause);
        }
        return 0;
    });

    /// <summary>The whole text as one sound (24 kHz mono).</summary>
    public async Task<float[]> RenderAsync(string text, string voice, double speed, CancellationToken cancel = default)
    {
        var all = new List<float>();
        await Make(text, voice, speed, piece => all.AddRange(piece), cancel);
        if (all.Count == 0) throw new InvalidDataException("There's nothing to say: the text has no words.");
        return all.ToArray();
    }

    // The model leaves a little silence before and after each piece; keep 40 ms of it.
    static float[] Trim(float[] samples)
    {
        const float quiet = 0.003f; int keep = Rate / 25;
        int start = 0, end = samples.Length - 1;
        while (start < samples.Length && Math.Abs(samples[start]) < quiet) start++;
        while (end > start && Math.Abs(samples[end]) < quiet) end--;
        start = Math.Max(0, start - keep); end = Math.Min(samples.Length - 1, end + keep);
        return end > start ? samples[start..(end + 1)] : [];
    }

    // ---- Speaking aloud ----
    readonly object speaking = new();
    WaveOutEvent? output; BufferedWaveProvider? buffer; CancellationTokenSource? current;
    long spokenId;
    float volume = 1;
    public string NowSaying { get; private set; } = "";
    public bool Speaking => NowSaying.Length > 0;
    public event Action<string>? Started, Finished;

    public double Volume { get => volume; set { volume = (float)Math.Clamp(value, 0, 1); lock (speaking) if (output != null) output.Volume = volume; } }

    /// <summary>Speaks the text aloud, starting as soon as the first sentence is ready. Interrupts anything being said
    /// (queue: false) or waits its turn (queue: true). The task ends when the last word has been heard.</summary>
    public async Task SayAsync(string text, string voice, double speed, bool queue = false, CancellationToken cancel = default)
    {
        if (!queue) Stop();
        CancellationTokenSource mine;
        long id;
        lock (speaking)
        {
            if (output == null)
            {
                buffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1)) { BufferDuration = TimeSpan.FromMinutes(20), DiscardOnBufferOverflow = true, ReadFully = true };
                output = new WaveOutEvent { DesiredLatency = 120, NumberOfBuffers = 3, Volume = volume };
                output.Init(buffer);
            }
            // The speakers are only open while something is being said (an open output keeps a playback thread running).
            if (output.PlaybackState != PlaybackState.Playing) output.Play();
            mine = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            current = mine; id = ++spokenId;
        }
        NowSaying = text; Started?.Invoke(text);
        try
        {
            var making = Make(text, voice, speed, piece =>
            {
                var bytes = new byte[piece.Length * 4]; Buffer.BlockCopy(piece, 0, bytes, 0, bytes.Length);
                lock (speaking) if (!mine.IsCancellationRequested) buffer!.AddSamples(bytes, 0, bytes.Length);
            }, mine.Token);
            // Stopping doesn't wait for the sentence being made to finish: it's dropped when it's done.
            var stopped = new TaskCompletionSource();
            using (mine.Token.Register(() => stopped.TrySetResult()))
                if (await Task.WhenAny(making, stopped.Task) != making) { _ = making.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted); throw new OperationCanceledException(); }
            await making;
            // Wait for the speakers to catch up.
            while (!mine.IsCancellationRequested && (buffer?.BufferedBytes ?? 0) > 0) await Task.Delay(30);
            await Task.Delay(mine.IsCancellationRequested ? 0 : 120);
        }
        catch (OperationCanceledException) { }
        finally
        {
            lock (speaking) if (spokenId == id) { NowSaying = ""; current = null; buffer?.ClearBuffer(); output?.Stop(); }
            Finished?.Invoke(text);
        }
    }
    /// <summary>Stops speaking at once (what's queued is dropped).</summary>
    public void Stop()
    {
        lock (speaking) { current?.Cancel(); buffer?.ClearBuffer(); NowSaying = ""; }
    }

    public void Dispose()
    {
        Stop();
        lock (speaking) { output?.Dispose(); output = null; }
        jobs.CompleteAdding();
    }
}

/// <summary>Speech to text with Whisper (whisper.cpp through Whisper.net), on one dedicated thread. The model chosen in
/// Speech settings is loaded on first use and kept.</summary>
sealed class SpeechToText : IDisposable
{
    public const int Rate = 16000;
    public static SpeechToText Shared { get; } = new();
    readonly BlockingCollection<Action> jobs = new();
    readonly Thread worker;
    WhisperFactory? factory; string loaded = "";
    /// <summary>Words to listen out for: the app's own names, so "Arcadia Studio" isn't heard as "our Katie a studio".</summary>
    public const string Vocabulary = "Arcadia Studio, Toolbox, Preview, Properties, MCP, Claude, sprite, tilemap.";

    SpeechToText()
    {
        worker = new Thread(() => { foreach (var job in jobs.GetConsumingEnumerable()) { try { job(); } catch (Exception) { } } })
        { IsBackground = true, Name = "Whisper speech recognition", Priority = ThreadPriority.AboveNormal };
        worker.Start();
    }

    public static string Model { get; set; } = "tiny";
    public static bool Available => SpeechFiles.Find(SpeechFiles.Whisper[0].File) != null;

    public sealed record Segment(TimeSpan Start, TimeSpan End, string Text);

    /// <summary>Transcribes 16 kHz mono sound. language: "auto" or a code like en, es, fr. prompt: extra words to expect.</summary>
    public Task<List<Segment>> TranscribeAsync(float[] samples, string language = "auto", string prompt = "", CancellationToken cancel = default)
    {
        var done = new TaskCompletionSource<List<Segment>>(TaskCreationOptions.RunContinuationsAsynchronously);
        jobs.Add(() =>
        {
            try
            {
                cancel.ThrowIfCancellationRequested();
                var model = SpeechFiles.Whisper.FirstOrDefault(m => m.Id == Model && SpeechFiles.Has(m)) ?? SpeechFiles.Whisper[0];
                string path = SpeechFiles.Find(model.File) ?? throw new InvalidOperationException("The speech recognition model isn't installed with this copy of Arcadia Studio (it comes in its Speech folder). Reinstall Arcadia Studio to get it back.");
                if (factory == null || loaded != path) { factory?.Dispose(); factory = WhisperFactory.FromPath(path); loaded = path; }
                var segments = new List<Segment>();
                var builder = factory.CreateBuilder().WithThreads(Math.Clamp(Environment.ProcessorCount / 2, 2, 8)).WithPrompt((Vocabulary + " " + prompt).Trim())
                    .WithSegmentEventHandler(s => segments.Add(new Segment(s.Start, s.End, s.Text.Trim())));
                builder = language is "" or "auto" ? builder.WithLanguageDetection() : builder.WithLanguage(language);
                using var processor = builder.Build();
                if (samples.Length < Rate / 2) { var padded = new float[Rate]; samples.CopyTo(padded, 0); samples = padded; }
                processor.Process(samples);
                done.SetResult(segments.Where(s => s.Text.Length > 0 && !IsNoise(s.Text)).ToList());
            }
            catch (Exception ex) { done.SetException(ex); }
        });
        return done.Task;
    }
    // Whisper writes these for silence and noise.
    static bool IsNoise(string text) => Regex.IsMatch(text, @"^[\[\(][^\]\)]*[\]\)]$") || text is "." or "…";

    public static string Join(IEnumerable<Segment> segments) => Regex.Replace(string.Join(" ", segments.Select(s => s.Text)), @"\s+", " ").Trim();

    /// <summary>Subtitles in SRT form.</summary>
    public static string Srt(IReadOnlyList<Segment> segments)
    {
        var text = new StringBuilder();
        static string T(TimeSpan t) => t.ToString(@"hh\:mm\:ss\,fff", CultureInfo.InvariantCulture);
        for (int i = 0; i < segments.Count; i++) text.Append(i + 1).Append('\n').Append(T(segments[i].Start)).Append(" --> ").Append(T(segments[i].End)).Append('\n').Append(segments[i].Text).Append("\n\n");
        return text.ToString();
    }

    public void Dispose() { jobs.CompleteAdding(); factory?.Dispose(); }
}

/// <summary>The microphone, for speaking a request: 16 kHz mono, on NAudio's own recording thread. With stopOnSilence it
/// ends by itself about a second and a half after you stop talking.</summary>
sealed class Microphone : IDisposable
{
    readonly WaveInEvent input;
    readonly List<float> samples = new();
    readonly TaskCompletionSource<float[]> done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly double maxSeconds;
    bool heard; int quietSamples;
    /// <summary>The loudness of the latest sound, 0-1, for a level meter (raised on the recording thread).</summary>
    public event Action<double>? Level;
    public Task<float[]> Recording => done.Task;

    public static List<string> Devices() => Enumerable.Range(0, WaveInEvent.DeviceCount).Select(i => WaveInEvent.GetCapabilities(i).ProductName).ToList();

    public Microphone(int device = -1, bool stopOnSilence = true, double maxSeconds = 60)
    {
        if (WaveInEvent.DeviceCount == 0) throw new InvalidOperationException("There's no microphone. Plug one in, or check Windows sound settings (Privacy → Microphone lets desktop apps use it).");
        this.maxSeconds = maxSeconds;
        input = new WaveInEvent { DeviceNumber = device >= 0 && device < WaveInEvent.DeviceCount ? device : -1, WaveFormat = new WaveFormat(SpeechToText.Rate, 16, 1), BufferMilliseconds = 50 };
        input.DataAvailable += (_, e) =>
        {
            double sum = 0; int count = e.BytesRecorded / 2;
            lock (samples)
                for (int i = 0; i < count; i++) { float s = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f; samples.Add(s); sum += s * s; }
            double rms = Math.Sqrt(sum / Math.Max(1, count));
            Level?.Invoke(Math.Min(1, rms * 8));
            if (rms > 0.02) { heard = true; quietSamples = 0; } else quietSamples += count;
            if (stopOnSilence && heard && quietSamples > SpeechToText.Rate * 1.5) Stop();
            if (samples.Count > SpeechToText.Rate * this.maxSeconds) Stop();
        };
        input.RecordingStopped += (_, e) =>
        {
            float[] all; lock (samples) all = samples.ToArray();
            if (e.Exception != null) done.TrySetException(new InvalidOperationException("The microphone stopped: " + e.Exception.Message));
            else done.TrySetResult(all);
        };
        input.StartRecording();
    }
    public bool HeardSpeech => heard;
    public void Stop() { try { input.StopRecording(); } catch { } }
    public void Dispose() { Stop(); input.Dispose(); }
}
