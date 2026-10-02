using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
namespace Wysicraft.Designer;

/// <summary>The bundled FFmpeg (an LGPL build: FFmpeg/ffmpeg.exe beside the install, artifacts/ffmpeg/bin in a development
/// checkout): recording the screen, a window or the Preview's game; reading any audio or video file; converting and
/// cutting videos for an assistant. It always runs as its own process.</summary>
static class FFmpeg
{
    /// <summary>ffmpeg.exe, or null when it isn't there.</summary>
    public static string? Exe()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            foreach (var relative in new[] { "FFmpeg", "artifacts/ffmpeg/bin" })
            { string path = Path.Combine(directory.FullName, relative, "ffmpeg.exe"); if (File.Exists(path)) return path; }
        return null;
    }
    public static string Require() => Exe() ?? throw new InvalidOperationException("FFmpeg isn't installed with this copy of Arcadia Studio (it comes in the FFmpeg folder beside the app). Reinstall Arcadia Studio to get it back.");

    public sealed record Result(int ExitCode, string Log);

    /// <summary>Runs ffmpeg with these arguments (no shell) and returns its exit code and the end of its log.</summary>
    public static async Task<Result> RunAsync(IEnumerable<string> arguments, CancellationToken cancel = default, string? workingDirectory = null, TimeSpan? timeout = null)
    {
        var start = new ProcessStartInfo(Require()) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true, RedirectStandardInput = true, WorkingDirectory = workingDirectory ?? Path.GetTempPath() };
        start.ArgumentList.Add("-hide_banner"); start.ArgumentList.Add("-nostdin");
        foreach (var a in arguments) start.ArgumentList.Add(a);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("FFmpeg didn't start.");
        var log = new StringBuilder();
        var reading = Task.Run(async () => { string? line; while ((line = await process.StandardError.ReadLineAsync()) != null) lock (log) { log.AppendLine(line); if (log.Length > 64000) log.Remove(0, log.Length - 32000); } });
        _ = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        limit.CancelAfter(timeout ?? TimeSpan.FromMinutes(10));
        try { await process.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException) { try { process.Kill(true); } catch { } throw new TimeoutException(cancel.IsCancellationRequested ? "Stopped." : "FFmpeg took too long and was stopped."); }
        await reading;
        lock (log) return new Result(process.ExitCode, log.ToString());
    }

    /// <summary>Any audio or video file FFmpeg can read, as mono float samples at this rate.</summary>
    public static async Task<float[]> DecodeAsync(string file, int rate, CancellationToken cancel = default)
    {
        string raw = Path.Combine(Path.GetTempPath(), "Arcadia Studio", "decode-" + Guid.NewGuid().ToString("N") + ".f32");
        Directory.CreateDirectory(Path.GetDirectoryName(raw)!);
        try
        {
            var r = await RunAsync(["-y", "-i", file, "-vn", "-ac", "1", "-ar", rate.ToString(CultureInfo.InvariantCulture), "-f", "f32le", raw], cancel);
            if (r.ExitCode != 0 || !File.Exists(raw)) throw new InvalidDataException("That file couldn't be read as sound: " + LastLine(r.Log));
            var bytes = await File.ReadAllBytesAsync(raw, cancel);
            var samples = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 4);
            return samples;
        }
        finally { try { File.Delete(raw); } catch { } }
    }

    public static string LastLine(string log) => log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault(l => !l.StartsWith("frame=") && !l.StartsWith("size=")) ?? "no details";

    // The best H.264 encoder this computer has: the graphics card's, else Windows' own (every Windows 10/11 has it),
    // else OpenH264. Found once by encoding a few frames with each.
    static string? encoder;
    static readonly SemaphoreSlim probing = new(1, 1);
    public static async Task<string> H264Async()
    {
        if (encoder != null) return encoder;
        await probing.WaitAsync();
        try
        {
            if (encoder != null) return encoder;
            foreach (var candidate in new[] { "h264_nvenc", "h264_amf", "h264_qsv", "h264_mf", "libopenh264" })
            {
                var r = await RunAsync(["-f", "lavfi", "-i", "color=c=black:s=256x256:r=30", "-frames:v", "10", "-pix_fmt", "yuv420p", "-c:v", candidate, "-f", "null", "-"], timeout: TimeSpan.FromSeconds(20));
                if (r.ExitCode == 0) return encoder = candidate;
            }
            return encoder = "libopenh264";
        }
        finally { probing.Release(); }
    }
    /// <summary>Quality settings for an encoder (roughly the same look for each).</summary>
    public static string[] QualityFor(string encoder) => encoder switch
    {
        "h264_nvenc" => ["-preset", "p5", "-rc", "vbr", "-cq", "21", "-b:v", "0"],
        "h264_amf" => ["-quality", "quality", "-rc", "cqp", "-qp_i", "20", "-qp_p", "22"],
        "h264_qsv" => ["-global_quality", "22"],
        "h264_mf" => ["-rate_control", "quality", "-quality", "75", "-scenario", "display_remoting"],
        _ => ["-b:v", "8M"],
    };
}

/// <summary>One recording in progress: video from Windows' screen capture (a monitor, or one window even when other windows
/// cover it, cropped to a part of it), with the app's own sound when asked for. Stop finishes the file.</summary>
sealed class MediaRecorder : IAsyncDisposable
{
    public string File { get; }
    /// <summary>What FFmpeg writes while recording: Matroska, which stays playable even if FFmpeg has to be stopped (an MP4
    /// cut off before its end can't be opened). An MP4 or MOV is made from it when the recording stops.</summary>
    readonly string capture;
    public string Target { get; }
    public DateTime Started { get; } = DateTime.Now;
    public bool WithSound { get; }
    readonly Process process;
    readonly StringBuilder log = new();
    readonly NamedPipeServerStream? pipe;
    ProcessLoopback? loopback;
    // The sound goes from the capture thread into this queue, and a writer thread of its own feeds it to FFmpeg. FFmpeg
    // pauses its reading now and then (lining the sound up with the video); the capture thread must never wait for it,
    // or Windows' small capture buffer overflows and that stretch of sound is lost (and filled with silence).
    readonly System.Collections.Concurrent.BlockingCollection<byte[]> sound = new();
    Thread? soundWriter;
    // The app's own sound sets the pace; each WebView2 browser process (the Preview's game) is captured beside it and
    // mixed in, including ones that start while recording.
    readonly Dictionary<int, (ProcessLoopback Loopback, Queue<byte> Waiting)> webSound = [];
    System.Threading.Timer? webSoundWatcher;
    /// <summary>More processes whose sound belongs in a recording: the open Previews' browser processes.</summary>
    public static Func<List<int>>? MoreSoundProcesses;
    int watchingWebSound;
    void WatchWebSound()
    {
        if (Interlocked.Exchange(ref watchingWebSound, 1) == 1) return;
        try { FindWebSound(); } finally { watchingWebSound = 0; }
    }
    void FindWebSound()
    {
        var ids = ProcessLoopback.WebViewBrowsers(Environment.ProcessId);
        if (MoreSoundProcesses != null) ids = ids.Concat(MoreSoundProcesses()).Distinct().ToList();
        foreach (int id in ids)
        {
            lock (webSound) if (webSound.ContainsKey(id)) continue;
            try
            {
                var waiting = new Queue<byte>();
                var loop = ProcessLoopback.Start(id, (bytes, count) =>
                {
                    lock (waiting)
                    {
                        for (int i = 0; i < count; i++) waiting.Enqueue(bytes[i]);
                        // Never more than a second behind: older sound is dropped rather than drifting out of step.
                        int most = ProcessLoopback.Format.AverageBytesPerSecond;
                        while (waiting.Count > most) waiting.Dequeue();
                    }
                });
                lock (webSound) webSound[id] = (loop, waiting);
            }
            catch (Exception) { }
        }
    }
    // Adds what the WebView2 processes have played to a chunk of the app's own sound (16-bit samples, clipped).
    void MixWebSound(byte[] chunk)
    {
        List<Queue<byte>> sources;
        lock (webSound) sources = webSound.Values.Select(v => v.Waiting).ToList();
        foreach (var waiting in sources)
            lock (waiting)
            {
                int take = Math.Min(chunk.Length, waiting.Count) & ~1;
                for (int i = 0; i < take; i += 2)
                {
                    int theirs = (short)(waiting.Dequeue() | (waiting.Dequeue() << 8));
                    int mixed = Math.Clamp((short)(chunk[i] | (chunk[i + 1] << 8)) + theirs, short.MinValue, short.MaxValue);
                    chunk[i] = (byte)mixed; chunk[i + 1] = (byte)(mixed >> 8);
                }
            }
    }
    readonly Task exited;
    public string? SoundProblem => loopback?.Problem;
    public bool Running => !process.HasExited;

    /// <summary>The source: a window (hwnd) with an optional crop in its pixels, or a monitor (0 = the main one).</summary>
    public sealed record Source(IntPtr Window, IntPtr Monitor, int CropLeft, int CropTop, int CropRight, int CropBottom, string Description);

    MediaRecorder(string file, string capture, string target, bool sound, Process process, NamedPipeServerStream? pipe)
    {
        File = file; this.capture = capture; Target = target; WithSound = sound; this.process = process; this.pipe = pipe;
        exited = Task.Run(async () => { string? line; while ((line = await process.StandardError.ReadLineAsync()) != null) lock (log) { log.AppendLine(line); if (log.Length > 32000) log.Remove(0, 16000); } });
    }

    public static async Task<MediaRecorder> StartAsync(Source source, string file, int fps, bool sound)
    {
        string ffmpeg = FFmpeg.Require();
        fps = Math.Clamp(fps, 5, 60);
        string format = Path.GetExtension(file).ToLowerInvariant();
        if (format is not (".mp4" or ".mkv" or ".webm" or ".mov")) throw new InvalidOperationException("Record to .mp4 (default), .mkv, .mov or .webm. Make a GIF from a recording afterwards.");
        var capture = new StringBuilder("gfxcapture=");
        capture.Append(source.Window != IntPtr.Zero ? "hwnd=" + source.Window.ToInt64().ToString(CultureInfo.InvariantCulture) : source.Monitor != IntPtr.Zero ? "hmonitor=" + source.Monitor.ToInt64().ToString(CultureInfo.InvariantCulture) : "monitor_idx=0");
        capture.Append(CultureInfo.InvariantCulture, $":max_framerate={fps}:capture_cursor=0");
        if (source.CropLeft > 0) capture.Append(CultureInfo.InvariantCulture, $":crop_left={source.CropLeft}");
        if (source.CropTop > 0) capture.Append(CultureInfo.InvariantCulture, $":crop_top={source.CropTop}");
        if (source.CropRight > 0) capture.Append(CultureInfo.InvariantCulture, $":crop_right={source.CropRight}");
        if (source.CropBottom > 0) capture.Append(CultureInfo.InvariantCulture, $":crop_bottom={source.CropBottom}");
        // Even sizes (H.264 needs them), a steady frame rate (capture only sends frames when something changes).
        capture.Append(CultureInfo.InvariantCulture, $",hwdownload,format=bgra,crop=trunc(iw/2)*2:trunc(ih/2)*2:0:0,fps={fps},format=yuv420p");

        var start = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in new[] { "-hide_banner", "-y", "-thread_queue_size", "64", "-filter_complex", capture + "[v]" }) start.ArgumentList.Add(a);
        NamedPipeServerStream? pipe = null; string pipeName = "";
        if (sound)
        {
            pipeName = "arcadia-sound-" + Guid.NewGuid().ToString("N");
            pipe = new NamedPipeServerStream(pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 1 << 20);
            var f = ProcessLoopback.Format;
            foreach (var a in new[] { "-thread_queue_size", "4096", "-f", "s16le", "-ar", f.SampleRate.ToString(CultureInfo.InvariantCulture), "-ac", f.Channels.ToString(CultureInfo.InvariantCulture), "-i", @"\\.\pipe\" + pipeName }) start.ArgumentList.Add(a);
        }
        start.ArgumentList.Add("-map"); start.ArgumentList.Add("[v]");
        // The capture is a filter source, not an input, so the sound pipe is input 0.
        if (sound) { start.ArgumentList.Add("-map"); start.ArgumentList.Add("0:a"); }
        if (format == ".webm")
            foreach (var a in new[] { "-c:v", "libvpx-vp9", "-deadline", "realtime", "-cpu-used", "8", "-row-mt", "1", "-b:v", "0", "-crf", "32" }) start.ArgumentList.Add(a);
        else
        {
            string h264 = await FFmpeg.H264Async();
            start.ArgumentList.Add("-c:v"); start.ArgumentList.Add(h264);
            foreach (var a in FFmpeg.QualityFor(h264)) start.ArgumentList.Add(a);
            start.ArgumentList.Add("-g"); start.ArgumentList.Add((fps * 2).ToString(CultureInfo.InvariantCulture));
        }
        if (sound) foreach (var a in format == ".webm" ? new[] { "-c:a", "libopus", "-b:a", "160k" } : ["-c:a", "aac", "-b:a", "192k"]) start.ArgumentList.Add(a);
        string writing = format is ".mp4" or ".mov" ? file + ".recording.mkv" : file;
        start.ArgumentList.Add(writing);

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var process = Process.Start(start) ?? throw new InvalidOperationException("FFmpeg didn't start.");
        var recorder = new MediaRecorder(file, writing, source.Description, sound, process, pipe);
        _ = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        if (pipe != null)
        {
            // FFmpeg opens the pipe as it starts; the app's sound flows into it from then on.
            using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await pipe.WaitForConnectionAsync(wait.Token); }
            catch (OperationCanceledException) { await recorder.AbortAsync(); throw new InvalidOperationException("FFmpeg didn't start recording: " + FFmpeg.LastLine(recorder.Log)); }
            try
            {
                var queue = recorder.sound;
                recorder.soundWriter = new Thread(() =>
                {
                    try { foreach (var chunk in queue.GetConsumingEnumerable()) pipe.Write(chunk, 0, chunk.Length); }
                    catch (IOException) { } catch (ObjectDisposedException) { } catch (InvalidOperationException) { }
                }) { IsBackground = true, Name = "Recording sound writer" };
                recorder.soundWriter.Start();
                recorder.loopback = await Task.Run(() => ProcessLoopback.Start(Environment.ProcessId, (bytes, count) =>
                {
                    var chunk = new byte[count]; Buffer.BlockCopy(bytes, 0, chunk, 0, count);
                    recorder.MixWebSound(chunk);
                    try { queue.Add(chunk); } catch (InvalidOperationException) { }
                }));
                recorder.webSoundWatcher = new System.Threading.Timer(_ => { try { recorder.WatchWebSound(); } catch (Exception) { } }, null, 0, 700);
            }
            catch (Exception ex) { await recorder.AbortAsync(); throw new InvalidOperationException("The app's sound can't be recorded here (" + ex.Message + "). Record without sound instead."); }
        }
        await Task.Delay(400);
        if (process.HasExited) { await recorder.exited; throw new InvalidOperationException("Recording didn't start: " + FFmpeg.LastLine(recorder.Log)); }
        return recorder;
    }

    public string Log { get { lock (log) return log.ToString(); } }

    /// <summary>Finishes the recording (FFmpeg writes the end of the file) and returns its length in seconds.</summary>
    public async Task<double> StopAsync()
    {
        webSoundWatcher?.Dispose(); webSoundWatcher = null;
        loopback?.Stop();
        lock (webSound) { foreach (var (loop, _) in webSound.Values) loop.Dispose(); webSound.Clear(); }
        // What's still queued goes to FFmpeg before the pipe closes (a few seconds at most).
        sound.CompleteAdding();
        soundWriter?.Join(5000);
        try { pipe?.Flush(); pipe?.Dispose(); } catch { }
        if (!process.HasExited)
        {
            try { await process.StandardInput.WriteAsync("q"); await process.StandardInput.FlushAsync(); process.StandardInput.Close(); } catch (IOException) { }
            // Windows only hands over a new frame when something on screen changes, so on a still screen FFmpeg can sit
            // waiting for one and not see the q. The Matroska file is whole either way, so after a few seconds it's stopped.
            using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            try { await process.WaitForExitAsync(wait.Token); } catch (OperationCanceledException) { try { process.Kill(true); } catch { } }
        }
        await exited;
        loopback?.Dispose(); loopback = null;
        if (!System.IO.File.Exists(capture) || new FileInfo(capture).Length == 0) throw new InvalidOperationException("The recording wasn't saved: " + FFmpeg.LastLine(Log));
        double? seconds = Duration(Log);
        if (capture != File)
        {
            var r = await FFmpeg.RunAsync(["-y", "-i", capture, "-c", "copy", "-movflags", "+faststart", File]);
            if (r.ExitCode != 0 || !System.IO.File.Exists(File)) throw new InvalidOperationException("The recording was saved as " + capture + " but couldn't be made into " + Path.GetExtension(File) + ": " + FFmpeg.LastLine(r.Log));
            try { System.IO.File.Delete(capture); } catch (IOException) { }
            seconds = FileDuration(r.Log) ?? seconds;
        }
        return seconds ?? (DateTime.Now - Started).TotalSeconds;
    }
    async Task AbortAsync() { try { process.Kill(true); } catch { } try { await exited; } catch { } webSoundWatcher?.Dispose(); loopback?.Dispose(); lock (webSound) { foreach (var (loop, _) in webSound.Values) loop.Dispose(); webSound.Clear(); } sound.CompleteAdding(); pipe?.Dispose(); }
    public async ValueTask DisposeAsync() { if (!process.HasExited) await StopAsync(); process.Dispose(); }

    /// <summary>A file's length from FFmpeg's description of it (ffmpeg -i).</summary>
    internal static double? FileDuration(string log) => log.Contains("Duration: ", StringComparison.Ordinal) ? Duration("time=" + log[(log.IndexOf("Duration: ", StringComparison.Ordinal) + 10)..]) : null;
    /// <summary>The last "time=" FFmpeg reported.</summary>
    internal static double? Duration(string log)
    {
        int at = log.LastIndexOf("time=", StringComparison.Ordinal);
        if (at < 0) return null;
        var text = log[(at + 5)..].Split([' ', ',', '\r', '\n'], 2)[0];
        return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var t) ? t.TotalSeconds : null;
    }
}

/// <summary>One pixel in a window's top-left corner whose white is 1/255 or 2/255 opaque, switched on every tick while
/// recording, so the window always has a fresh picture for Windows' screen capture. It takes no clicks.</summary>
sealed class RecordingHeartbeat(System.Windows.UIElement adorned) : System.Windows.Documents.Adorner(adorned)
{
    static readonly System.Windows.Media.Brush[] Shades = [Frozen(1), Frozen(2)];
    static System.Windows.Media.Brush Frozen(byte alpha) { var b = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, 255, 255, 255)); b.Freeze(); return b; }
    int shade;
    public void Tick() { shade ^= 1; InvalidateVisual(); }
    protected override void OnRender(System.Windows.Media.DrawingContext dc) { IsHitTestVisible = false; dc.DrawRectangle(Shades[shade], null, new System.Windows.Rect(0, 0, 1, 1)); }
}

/// <summary>What can be recorded: the monitors, and top-level windows found by their title.</summary>
static class CaptureTargets
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    struct MonitorInfoEx { public int Size; public Rect Monitor, Work; public uint Flags; [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    delegate bool MonitorProc(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);
    delegate bool WindowProc(IntPtr window, IntPtr data);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorProc proc, IntPtr data);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumWindows(WindowProc proc, IntPtr data);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder text, int max);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out Rect rect);

    public sealed record Monitor(int Index, IntPtr Handle, string Name, int Width, int Height, int X, int Y, bool Primary);
    /// <summary>The monitors, the main one first.</summary>
    public static List<Monitor> Monitors()
    {
        var found = new List<Monitor>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (handle, _, _, _) =>
        {
            var info = new MonitorInfoEx { Size = System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfo(handle, ref info))
                found.Add(new Monitor(0, handle, info.Device.TrimStart('\\', '.'), info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top, info.Monitor.Left, info.Monitor.Top, (info.Flags & 1) != 0));
            return true;
        }, IntPtr.Zero);
        return found.OrderByDescending(m => m.Primary).ThenBy(m => m.X).ThenBy(m => m.Y).Select((m, i) => m with { Index = i }).ToList();
    }
    /// <summary>A visible window whose title has these words in it.</summary>
    public static (IntPtr Handle, string Title)? Window(string words)
    {
        (IntPtr, string)? best = null;
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || IsIconic(handle)) return true;
            var text = new StringBuilder(512); GetWindowText(handle, text, 512);
            string title = text.ToString();
            if (title.Contains(words, StringComparison.OrdinalIgnoreCase)) { best = (handle, title); return false; }
            return true;
        }, IntPtr.Zero);
        return best;
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder text, int max);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
    /// <summary>This program's open message boxes and other Windows dialogs (About, a question, a notice), by title.</summary>
    public static List<(IntPtr Handle, string Title)> Dialogs()
    {
        var found = new List<(IntPtr, string)>(); uint me = (uint)Environment.ProcessId;
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle)) return true;
            GetWindowThreadProcessId(handle, out uint owner); if (owner != me) return true;
            var name = new StringBuilder(64); GetClassName(handle, name, 64);
            if (name.ToString() != "#32770") return true;
            var text = new StringBuilder(256); GetWindowText(handle, text, 256);
            found.Add((handle, text.ToString()));
            return true;
        }, IntPtr.Zero);
        return found;
    }
    /// <summary>Asks a dialog to close (as its ✕ would).</summary>
    public static void CloseDialog(IntPtr handle) => PostMessage(handle, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
    /// <summary>A window's inside (without its frame and title bar), in pixels.</summary>
    public static (int Width, int Height) ClientSize(IntPtr window) => GetClientRect(window, out var r) ? (r.Right - r.Left, r.Bottom - r.Top) : (0, 0);
}
