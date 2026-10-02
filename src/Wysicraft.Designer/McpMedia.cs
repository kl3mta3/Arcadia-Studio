using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Interop;
using System.Windows.Threading;
using ModelContextProtocol.Server;
using Wysicraft.Core;
using Wysicraft.Core.Audio;
using Wysicraft.Models;
namespace Wysicraft.Designer;

// Recording videos (Preview's Record button and the video tool) and the speech tool: an assistant's voice for narration
// and characters' lines, transcribing, and listening to the person. Speech and recording run on their own threads; the UI
// thread only starts and reports them.
public partial class MainWindow
{
    MediaRecorder? recording;
    PreviewSession? recordingPreview;
    DispatcherTimer? recordingClock;

    /// <summary>Where videos go: the folder chosen in preferences, else Videos\Arcadia Studio.</summary>
    internal string RecordingsFolder()
    {
        string folder = Prefs().RecordingFolder is { Length: > 0 } chosen ? chosen : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Arcadia Studio");
        Directory.CreateDirectory(folder);
        return folder;
    }
    string NewRecordingPath(string extension = ".mp4") => Path.Combine(RecordingsFolder(), PixelEditor.SafeName(project.Manifest.Id) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + extension);

    /// <summary>Preview's Record button: starts recording that Preview's game, or stops the recording.</summary>
    private async Task ToggleRecordingAsync(PreviewSession preview)
    {
        try
        {
            if (recording != null) { await StopRecordingAsync(); return; }
            await StartRecordingAsync(await preview.GameSourceAsync(), NewRecordingPath(), Prefs().RecordingFps, Prefs().RecordingSound, preview);
        }
        catch (Exception ex) { preview.Say("RECORD", ex.Message); Log("Recording: " + ex.Message); }
    }

    private async Task<MediaRecorder> StartRecordingAsync(MediaRecorder.Source source, string path, int fps, bool sound, PreviewSession? preview = null)
    {
        if (recording != null) throw new InvalidOperationException("A recording is already running (" + recording.Target + "). Stop it first.");
        StartHeartbeat();
        MediaRecorder.MoreSoundProcesses = () => { lock (PreviewSession.SoundProcesses) return PreviewSession.SoundProcesses.Distinct().ToList(); };
        MediaRecorder started;
        try { started = await MediaRecorder.StartAsync(source, path, fps, sound); }
        catch { StopHeartbeat(); throw; }
        recording = started; recordingPreview = preview ?? PreviewSession.Open.LastOrDefault(p => p.IsOpen);
        recordingClock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        recordingClock.Tick += (_, _) =>
        {
            if (recording == null) return;
            var t = DateTime.Now - recording.Started;
            recordingPreview?.ShowRecording($"■ Stop {(int)t.TotalMinutes}:{t.Seconds:00}");
            if (!recording.Running) _ = StopRecordingAsync();
        };
        recordingClock.Start();
        recordingPreview?.ShowRecording("■ Stop 0:00");
        Log($"Recording {source.Description}{(sound ? " with sound" : "")} to {path}.");
        return started;
    }

    internal async Task<(string Path, double Seconds, long Bytes, string? SoundProblem)> StopRecordingAsync()
    {
        var current = recording ?? throw new InvalidOperationException("Nothing is being recorded.");
        recording = null; recordingClock?.Stop(); recordingClock = null;
        try { return await FinishRecordingAsync(current); }
        finally { StopHeartbeat(); }
    }

    async Task<(string Path, double Seconds, long Bytes, string? SoundProblem)> FinishRecordingAsync(MediaRecorder current)
    {
        var preview = recordingPreview; recordingPreview = null;
        preview?.ShowRecording(null);
        try
        {
            double seconds = await current.StopAsync();
            long bytes = new FileInfo(current.File).Length;
            string message = $"Saved a {seconds:0.0} s video ({bytes / 1048576.0:0.0} MB): {current.File}";
            Log(message); if (preview?.IsOpen == true) preview.Say("RECORD", message);
            return (current.File, seconds, bytes, current.SoundProblem);
        }
        finally { await current.DisposeAsync(); }
    }

    // Windows' screen capture only hands over a picture when something on screen changes. A screen that stays still would
    // give FFmpeg nothing to write (a still recording saved no video at all) and makes it stop reading the sound while it
    // waits (lines of narration were lost that way). While recording, every Arcadia Studio window flickers one corner
    // pixel by a single shade ten times a second: invisible, but it keeps the pictures, and so the sound, flowing.
    readonly List<(System.Windows.Documents.AdornerLayer Layer, RecordingHeartbeat Beat)> heartbeats = [];
    DispatcherTimer? heartbeatTimer;
    void StartHeartbeat()
    {
        StopHeartbeat();
        foreach (var window in System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().Where(w => w.IsVisible))
        {
            if (window.Content is not System.Windows.UIElement content) continue;
            var layer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(content);
            if (layer == null) continue;
            var beat = new RecordingHeartbeat(content); layer.Add(beat); heartbeats.Add((layer, beat));
        }
        heartbeatTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Render, (_, _) => { foreach (var (_, beat) in heartbeats) beat.Tick(); }, Dispatcher);
        heartbeatTimer.Start();
    }
    void StopHeartbeat()
    {
        heartbeatTimer?.Stop(); heartbeatTimer = null;
        foreach (var (layer, beat) in heartbeats) layer.Remove(beat);
        heartbeats.Clear();
    }

    /// <summary>What a target names: game (Preview's game area), preview (the whole Preview window), app (this window),
    /// monitor:N (a whole monitor, 0 = the main one) or window:words (a window with those words in its title).</summary>
    async Task<MediaRecorder.Source> SourceFor(string target)
    {
        target = target.Trim();
        string kind = target.Split(':', 2)[0].ToLowerInvariant(), rest = target.Contains(':') ? target[(target.IndexOf(':') + 1)..].Trim() : "";
        switch (kind)
        {
            case "" or "game" or "preview":
                var preview = (activePreview?.IsOpen == true ? activePreview : null) ?? PreviewSession.Open.LastOrDefault(p => p.IsOpen)
                    ?? throw new InvalidOperationException("Open Preview first (preview_control open), then record the game.");
                return await preview.GameSourceAsync(kind == "preview");
            case "app":
                return new MediaRecorder.Source(new WindowInteropHelper(this).Handle, IntPtr.Zero, 0, 0, 0, 0, "the Arcadia Studio window");
            case "monitor":
                var monitors = CaptureTargets.Monitors();
                int index = rest.Length == 0 ? 0 : int.TryParse(rest, out int n) ? n : -1;
                if (index < 0 || index >= monitors.Count) throw new InvalidOperationException($"monitor is 0 to {monitors.Count - 1} (0 is the main one; video status lists them).");
                var m = monitors[index];
                return new MediaRecorder.Source(IntPtr.Zero, m.Handle, 0, 0, 0, 0, $"monitor {index} ({m.Width} × {m.Height})");
            case "window":
                if (rest.Length < 2) throw new InvalidOperationException("window:<words in its title>, e.g. window:Tutorial.");
                var found = CaptureTargets.Window(rest) ?? throw new InvalidOperationException($"No visible window has \"{rest}\" in its title.");
                return new MediaRecorder.Source(found.Handle, IntPtr.Zero, 0, 0, 0, 0, "the window " + found.Title);
            default: throw new InvalidOperationException("target is game, preview, app, monitor:N or window:<title words>.");
        }
    }

    static string FullPath(string path, string what)
    {
        if (path.Length == 0 || !Path.IsPathFullyQualified(path)) throw new InvalidOperationException(what + " must be a full path on this computer.");
        return Path.GetFullPath(path);
    }
    static string OutputPath(string path, string what, params string[] extensions)
    {
        string full = FullPath(path, what);
        if (!extensions.Contains(Path.GetExtension(full).ToLowerInvariant())) throw new InvalidOperationException(what + " ends in " + string.Join(", ", extensions) + ".");
        if (!Directory.Exists(Path.GetDirectoryName(full))) throw new InvalidOperationException("The folder for " + what + " doesn't exist.");
        return full;
    }

    // ---- MCP: video ----
    internal async Task<string> McpVideo(string action, string target, string path, int fps, bool sound, string input, double start, double end, int width, string text, string voice, double speed, string lines, double gameVolume, string args, CancellationToken cancel)
    {
        try
        {
            switch (action.Trim().ToLowerInvariant())
            {
                case "status":
                    return Json.Write(new
                    {
                        ffmpeg = FFmpeg.Exe() != null, recording = recording == null ? null : new { recording.File, recording.Target, seconds = Math.Round((DateTime.Now - recording.Started).TotalSeconds, 1), recording.WithSound },
                        folder = RecordingsFolder(), monitors = CaptureTargets.Monitors().Select(m => new { m.Index, m.Name, m.Width, m.Height, m.Primary }),
                        previewOpen = PreviewSession.Open.Any(p => p.IsOpen),
                    });
                case "record":
                    {
                        var source = await Dispatcher.InvokeAsync(() => SourceFor(target)).Task.Unwrap();
                        string file = path.Length > 0 ? OutputPath(path, "path", ".mp4", ".mkv", ".mov", ".webm") : NewRecordingPath();
                        var started = await Dispatcher.InvokeAsync(() => StartRecordingAsync(source, file, fps > 0 ? fps : Prefs().RecordingFps, sound)).Task.Unwrap();
                        return Json.Write(new { recording = true, file = started.File, target = started.Target, sound, note = "Set the scene with preview_control (input to walk, tap, script to place things, wait), narrate with speech say (wait:true so lines follow each other), then video stop." });
                    }
                case "stop":
                    {
                        var (file, seconds, bytes, soundProblem) = await Dispatcher.InvokeAsync(StopRecordingAsync).Task.Unwrap();
                        return Json.Write(new { file, seconds = Math.Round(seconds, 2), megabytes = Math.Round(bytes / 1048576.0, 2), soundProblem });
                    }
                case "frame":
                    {
                        string video = FullPath(input, "input");
                        string png = path.Length > 0 ? OutputPath(path, "path", ".png") : Path.ChangeExtension(video, null) + $"-{start:0.0}s.png";
                        var r = await FFmpeg.RunAsync(["-y", "-ss", start.ToString(CultureInfo.InvariantCulture), "-i", video, "-frames:v", "1", png], cancel);
                        if (r.ExitCode != 0 || !File.Exists(png)) throw new InvalidOperationException("No frame there: " + FFmpeg.LastLine(r.Log));
                        return Json.Write(new { file = png });
                    }
                case "trim":
                    {
                        string video = FullPath(input, "input");
                        if (end <= start) throw new InvalidOperationException("end (seconds) must be after start.");
                        string output = path.Length > 0 ? OutputPath(path, "path", ".mp4", ".mkv", ".mov", ".webm") : Path.ChangeExtension(video, null) + "-trimmed" + Path.GetExtension(video);
                        string h264 = await FFmpeg.H264Async();
                        var r = await FFmpeg.RunAsync(["-y", "-ss", start.ToString(CultureInfo.InvariantCulture), "-to", end.ToString(CultureInfo.InvariantCulture), "-i", video, "-c:v", h264, .. FFmpeg.QualityFor(h264), "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", output], cancel);
                        if (r.ExitCode != 0) throw new InvalidOperationException("Trimming failed: " + FFmpeg.LastLine(r.Log));
                        return Json.Write(new { file = output, seconds = Math.Round(end - start, 2) });
                    }
                case "gif":
                    {
                        string video = FullPath(input, "input");
                        string output = path.Length > 0 ? OutputPath(path, "path", ".gif") : Path.ChangeExtension(video, ".gif");
                        int w = width > 0 ? Math.Clamp(width, 64, 1280) : 480; int rate = fps > 0 ? Math.Clamp(fps, 5, 30) : 15;
                        var range = end > start ? new[] { "-ss", start.ToString(CultureInfo.InvariantCulture), "-to", end.ToString(CultureInfo.InvariantCulture) } : [];
                        var r = await FFmpeg.RunAsync(["-y", .. range, "-i", video, "-vf", $"fps={rate},scale={w}:-1:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4", output], cancel);
                        if (r.ExitCode != 0) throw new InvalidOperationException("Making the GIF failed: " + FFmpeg.LastLine(r.Log));
                        return Json.Write(new { file = output, megabytes = Math.Round(new FileInfo(output).Length / 1048576.0, 2) });
                    }
                case "narrate":
                    return await NarrateVideoAsync(input, path, text, voice, speed, lines, start, gameVolume, cancel);
                case "ffmpeg":
                    {
                        var list = JsonSerializer.Deserialize<List<string>>(args.Length > 0 ? args : "[]") ?? [];
                        if (list.Count == 0) throw new InvalidOperationException("args is a JSON array of ffmpeg arguments, e.g. [\"-i\",\"C:/in.mp4\",\"-vf\",\"scale=1280:-2\",\"C:/out.mp4\"]. Use full paths.");
                        var r = await FFmpeg.RunAsync(list, cancel, RecordingsFolder());
                        string log = r.Log.Length > 4000 ? r.Log[^4000..] : r.Log;
                        return Json.Write(new { exitCode = r.ExitCode, log });
                    }
                default: throw new InvalidOperationException("action is status, record, stop, frame, trim, gif, narrate or ffmpeg.");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or JsonException or TimeoutException or UnauthorizedAccessException) { throw new ModelContextProtocol.McpException(ex.Message); }
    }

    sealed class NarrationLine { public double At { get; set; } public string Text { get; set; } = ""; public string Voice { get; set; } = ""; public double Speed { get; set; } }

    /// <summary>Speaks lines over a video at given times and mixes them with its own sound (turned down to gameVolume).</summary>
    async Task<string> NarrateVideoAsync(string input, string path, string text, string voice, double speed, string lines, double start, double gameVolume, CancellationToken cancel)
    {
        string video = FullPath(input, "input");
        if (!File.Exists(video)) throw new InvalidOperationException("There's no video at input.");
        var said = lines.Length > 0 ? JsonSerializer.Deserialize<List<NarrationLine>>(lines, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [] : [];
        if (text.Trim().Length > 0) said.Insert(0, new NarrationLine { At = start, Text = text });
        said = said.Where(l => l.Text.Trim().Length > 0).OrderBy(l => l.At).ToList();
        if (said.Count == 0) throw new InvalidOperationException("Give text (spoken from start seconds) or lines: [{at: seconds, text, voice?, speed?}].");
        if (said.Count > 60) throw new InvalidOperationException("At most 60 lines at once.");
        string output = path.Length > 0 ? OutputPath(path, "path", ".mp4", ".mkv", ".mov") : Path.ChangeExtension(video, null) + "-narrated.mp4";
        string temp = Path.Combine(Path.GetTempPath(), "Arcadia Studio", "narration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string defaultVoice = voice.Length > 0 ? voice : AssistantVoice; double defaultSpeed = speed > 0 ? speed : AssistantSpeed;
            var arguments = new List<string> { "-y", "-i", video };
            var filters = new List<string>(); var labels = new List<string>();
            double lastEnd = 0;
            for (int i = 0; i < said.Count; i++)
            {
                var line = said[i];
                var samples = await TextToSpeech.Shared.RenderAsync(line.Text, line.Voice.Length > 0 ? line.Voice : defaultVoice, line.Speed > 0 ? line.Speed : defaultSpeed, cancel);
                string wav = Path.Combine(temp, i + ".wav");
                await File.WriteAllBytesAsync(wav, AudioFiles.Wav([samples], TextToSpeech.Rate), cancel);
                arguments.AddRange(["-i", wav]);
                int ms = (int)Math.Round(Math.Max(0, line.At) * 1000);
                filters.Add($"[{i + 1}:a]aresample=48000,adelay={ms}|{ms}[n{i}]");
                labels.Add($"[n{i}]");
                lastEnd = Math.Max(lastEnd, line.At + samples.Length / (double)TextToSpeech.Rate);
            }
            var probe = await FFmpeg.RunAsync(["-i", video], cancel);
            bool hasSound = probe.Log.Contains("Audio:", StringComparison.Ordinal);
            double duration = MediaRecorder.FileDuration(probe.Log) ?? 0;
            string narration = labels.Count == 1 ? labels[0] : string.Concat(labels) + $"amix=inputs={labels.Count}:normalize=0[nar]";
            if (labels.Count > 1) filters.Add(narration); string nar = labels.Count == 1 ? labels[0] : "[nar]";
            double level = gameVolume > 0 ? Math.Clamp(gameVolume, 0, 2) : 0.35;
            if (hasSound) filters.Add($"[0:a]aresample=48000,volume={level.ToString(CultureInfo.InvariantCulture)}[g];[g]{nar}amix=inputs=2:normalize=0:duration=first[a]");
            else filters.Add($"{nar}apad[a]");
            arguments.AddRange(["-filter_complex", string.Join(";", filters), "-map", "0:v", "-map", "[a]", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k"]);
            if (!hasSound) arguments.Add("-shortest");
            arguments.AddRange(["-movflags", "+faststart", output]);
            var r = await FFmpeg.RunAsync(arguments, cancel);
            if (r.ExitCode != 0) throw new InvalidOperationException("Mixing the narration failed: " + FFmpeg.LastLine(r.Log));
            return Json.Write(new { file = output, lines = said.Count, narrationEnds = Math.Round(lastEnd, 2), videoSeconds = Math.Round(duration, 2), note = lastEnd > duration + 0.1 && duration > 0 ? "The narration runs past the end of the video and is cut off there." : null });
        }
        finally { try { Directory.Delete(temp, true); } catch { } }
    }

    /// <summary>Notes on text that may not read well, or null when it's fine (so the result stays short).</summary>
    static List<string>? Advice(string text) { var a = TextToSpeech.Advice(text); return a.Count > 0 ? a : null; }

    // ---- MCP: speech ----
    sealed class SpokenLine { public string Name { get; set; } = ""; public string Text { get; set; } = ""; public string Voice { get; set; } = ""; public double Speed { get; set; } }

    internal async Task<string> McpSpeech(string action, string text, string voice, double speed, bool wait, bool queue, string path, string name, string lines, string file, string sound, string language, double seconds, string expected, CancellationToken cancel)
    {
        try
        {
            await Dispatcher.InvokeAsync(SpeechPrefs);
            string Voice() => voice.Length > 0 ? TextToSpeech.Normal(voice) : AssistantVoice;
            double Speed() => speed > 0 ? Math.Clamp(speed, 0.5, 2) : AssistantSpeed;
            switch (action.Trim().ToLowerInvariant())
            {
                case "status":
                    return Json.Write(new
                    {
                        textToSpeech = TextToSpeech.Available, speechToText = SpeechToText.Available, voice = AssistantVoice, speed = AssistantSpeed, voiceChosen = Prefs().SpeechVoice.Length > 0,
                        voiceModel = TextToSpeech.UseFullModel ? "full precision" : "standard", whisperModel = SpeechToText.Model, language = Prefs().WhisperLanguage,
                        speaking = TextToSpeech.Shared.Speaking, nowSaying = TextToSpeech.Shared.NowSaying,
                    });
                case "voices":
                    return Json.Write(new { voices = TextToSpeech.Voices(), current = AssistantVoice, mixing = "Mix voices with + and weights: \"af_heart*0.6+am_michael*0.4\" (up to 4). The first voice sets the language. a=American English, b=British English, e=Spanish, f=French, h=Hindi, i=Italian, j=Japanese, p=Brazilian Portuguese, z=Mandarin; the second letter is f (female) or m (male)." });
                case "say":
                    {
                        if (text.Trim().Length == 0) throw new InvalidDataException("Give text to say.");
                        if (text.Length > 20000) throw new InvalidDataException("That's a lot to say at once (20,000 characters at most): split it up.");
                        string v = Voice(); double s = Speed();
                        var speaking = TextToSpeech.Shared.SayAsync(text, v, s, queue, cancel);
                        if (!wait) { _ = speaking.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted); return Json.Write(new { speaking = true, voice = v, speed = s, textAdvice = Advice(text), note = "Speaking now; speech stop ends it." }); }
                        var clock = System.Diagnostics.Stopwatch.StartNew();
                        await speaking;
                        return Json.Write(new { said = true, voice = v, speed = s, seconds = Math.Round(clock.Elapsed.TotalSeconds, 2), textAdvice = Advice(text) });
                    }
                case "stop":
                    TextToSpeech.Shared.Stop();
                    return Json.Write(new { stopped = true });
                case "set_voice":
                    {
                        string v = TextToSpeech.Normal(voice.Length > 0 ? voice : throw new InvalidDataException("Give voice, e.g. af_heart or a mix like af_heart*0.6+am_michael*0.4."));
                        await Dispatcher.InvokeAsync(() => { Prefs().SpeechVoice = v; if (speed > 0) Prefs().SpeechSpeed = Math.Clamp(speed, 0.5, 2); SavePrefs(); });
                        return Json.Write(new { voice = v, speed = AssistantSpeed, note = "This is now the assistant's voice (the person can change it in Advanced → Speech settings)." });
                    }
                case "save":
                    {
                        if (text.Trim().Length == 0) throw new InvalidDataException("Give text to say.");
                        string output = OutputPath(path, "path", ".ogg", ".wav", ".mp3");
                        var speech = await TextToSpeech.Shared.RenderAsync(text, Voice(), Speed(), cancel);
                        SaveSpeech(output, speech);
                        return Json.Write(new { file = output, seconds = Math.Round(speech.Length / (double)TextToSpeech.Rate, 2), textAdvice = Advice(text) });
                    }
                case "add_sound":
                    {
                        var wanted = lines.Length > 0 ? JsonSerializer.Deserialize<List<SpokenLine>>(lines, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [] : [];
                        if (text.Trim().Length > 0) wanted.Insert(0, new SpokenLine { Name = name, Text = text });
                        wanted = wanted.Where(l => l.Text.Trim().Length > 0).ToList();
                        if (wanted.Count == 0) throw new InvalidDataException("Give text (and name), or lines: [{name, text, voice?, speed?}] for several characters' lines at once.");
                        if (wanted.Count > 40) throw new InvalidDataException("At most 40 lines at once.");
                        await Dispatcher.InvokeAsync(() => { if (mcpHost == null) throw new InvalidOperationException("MCP server is stopped."); CheckRevision(expected); });
                        var made = new List<(SpokenLine Line, float[] Samples, string Voice)>();
                        foreach (var line in wanted)
                        {
                            string v = line.Voice.Length > 0 ? TextToSpeech.Normal(line.Voice) : Voice();
                            made.Add((line, ToProjectRate(await TextToSpeech.Shared.RenderAsync(line.Text, v, line.Speed > 0 ? line.Speed : Speed(), cancel)), v));
                        }
                        var added = await Dispatcher.InvokeAsync(() =>
                        {
                            CheckRevision(expected);
                            return made.Select(m =>
                            {
                                string saved = SaveSoundAsset(m.Line.Name.Length > 0 ? m.Line.Name : SpeechName(m.Line.Text), "ogg", true, null, [m.Samples]);
                                return new { sound = SoundAssets.Resource(saved), path = saved, text = m.Line.Text, voice = m.Voice, seconds = Math.Round(m.Samples.Length / (double)SongRenderer.SampleRate, 2) };
                            }).ToList();
                        });
                        return Json.Write(new { revision = await Dispatcher.InvokeAsync(Revision), sounds = added, textAdvice = Advice(string.Join(" ", wanted.Select(l => l.Text))), note = "Play a line with ctx.client.playSound('<sound>') or a play_sound action (Ogg plays in Minecraft, web and desktop)." });
                    }
                case "transcribe":
                    {
                        float[] samples;
                        if (sound.Length > 0)
                        {
                            var (bytes, ext) = await Dispatcher.InvokeAsync(() => { string? p = SoundAssetPath(sound) ?? throw new InvalidDataException($"There's no sound \"{sound}\" in the project."); return (project.Assets[p], Path.GetExtension(p)); });
                            string temp = Path.Combine(Path.GetTempPath(), "Arcadia Studio", "transcribe-" + Guid.NewGuid().ToString("N") + ext);
                            Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
                            await File.WriteAllBytesAsync(temp, bytes, cancel);
                            try { samples = await FFmpeg.DecodeAsync(temp, SpeechToText.Rate, cancel); } finally { File.Delete(temp); }
                        }
                        else samples = await FFmpeg.DecodeAsync(FullPath(file, "file"), SpeechToText.Rate, cancel);
                        string words = await Dispatcher.InvokeAsync(ProjectWords);
                        var segments = await SpeechToText.Shared.TranscribeAsync(samples, language.Length > 0 ? language : Prefs().WhisperLanguage, words, cancel);
                        return Json.Write(new { text = SpeechToText.Join(segments), seconds = Math.Round(samples.Length / (double)SpeechToText.Rate, 2), segments = segments.Select(s => new { start = Math.Round(s.Start.TotalSeconds, 2), end = Math.Round(s.End.TotalSeconds, 2), s.Text }), srt = SpeechToText.Srt(segments) });
                    }
                case "listen":
                    {
                        double limit = seconds > 0 ? Math.Clamp(seconds, 2, 120) : 20;
                        await Dispatcher.InvokeAsync(() => Notify("🎤 Your assistant is listening", text.Trim().Length > 0 ? text.Trim() : "Say your answer, then pause.", "info", Math.Min(limit, 30)));
                        using var mic = new Microphone(Prefs().Microphone, stopOnSilence: true, maxSeconds: limit);
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(limit + 1));
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, timeout.Token);
                        string heard = await Dispatcher.InvokeAsync(() => ListenAsync(_ => { }, mic, linked.Token)).Task.Unwrap();
                        return Json.Write(new { heard, nothingHeard = heard.Length == 0 });
                    }
                default: throw new InvalidDataException("action is status, voices, say, stop, set_voice, save, add_sound, transcribe or listen.");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or JsonException or TimeoutException or UnauthorizedAccessException) { throw new ModelContextProtocol.McpException(ex.Message); }
    }
}

public sealed partial class DesignerMcpTools
{
    [McpServerTool(Name = "speech"), Description("The assistant's voice (Kokoro, on this computer) and speech recognition (Whisper). action: status; voices (every voice; mix them as \"af_heart*0.6+am_michael*0.4\"); say (text spoken aloud now; voice and speed 0.5-2 default to the assistant's own; wait:true (default) returns when it has been heard; queue:true waits for anything already being said instead of interrupting it); stop; set_voice (voice, speed: the assistant's voice from now on); save (text, voice, speed, path = absolute .ogg/.wav/.mp3); add_sound (a voiced line added to the project as an Ogg sound: text + name, or lines = JSON [{name, text, voice, speed}]; needs expectedRevision; returns sound IDs); transcribe (file = absolute path of any audio or video file, or sound = a project sound; language auto or en, es, fr…); listen (text = the question shown to the person; records their microphone until they pause, up to seconds). Write text the way it is said: plain sentences, no markdown, emoji, links or symbols; say, save and add_sound return textAdvice when something may be misread. How to write for the voice, with examples: guide(topic:\"speech\").")]
    public Task<string> Speech(string action, string text = "", string voice = "", double speed = 0, bool wait = true, bool queue = false, string path = "", string name = "", string lines = "", string file = "", string sound = "", string language = "", double seconds = 0, string expectedRevision = "", CancellationToken cancellationToken = default) =>
        editor.McpSpeech(action, text, voice, speed, wait, queue, path, name, lines, file, sound, language, seconds, expectedRevision, cancellationToken);

    [McpServerTool(Name = "video"), Description("Record and edit videos with the bundled FFmpeg. action: status (recording, the videos folder, monitors); record (target: game = only the game's area in the open Preview (default), preview, app, monitor:N or window:<title words>; sound (default true) records Arcadia Studio's own sound only: the game and your speech say narration; fps (default 30); path = absolute .mp4/.mkv/.mov/.webm, default Videos\\Arcadia Studio); stop (finishes the file: path, seconds, size); frame (input, start seconds, path .png); trim (input, start, end); gif (input, start, end, width, fps); narrate (input video; text spoken from start, or lines = JSON [{at: seconds, text, voice, speed}]; gameVolume, default 0.35); ffmpeg (args = JSON array of ffmpeg arguments with full paths). How to record a game while driving and narrating it: guide(topic:\"video\").")]
    public Task<string> Video(string action, string target = "game", string path = "", int fps = 0, bool sound = true, string input = "", double start = 0, double end = 0, int width = 0, string text = "", string voice = "", double speed = 0, string lines = "", double gameVolume = 0, string args = "", CancellationToken cancellationToken = default) =>
        editor.McpVideo(action, target, path, fps, sound, input, start, end, width, text, voice, speed, lines, gameVolume, args, cancellationToken);
}
