using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
namespace Wysicraft.Designer;

/// <summary>Records the sound one process and its children make (Windows' process loopback, Windows 10 2004 and later):
/// Arcadia Studio itself, so the Preview's game sound (WebView2 runs as child processes) and the narration are in a
/// recording and nothing else the computer is playing is. Silence is written while nothing plays, so the sound stays
/// in step with the video. Runs on its own thread; Stop (or Dispose) ends it.</summary>
sealed class ProcessLoopback : IDisposable
{
    public static readonly WaveFormat Format = new(48000, 16, 2);
    readonly Thread thread;
    readonly Action<byte[], int> write;
    volatile bool stopping;
    readonly int processId;
    AudioClient? client;
    readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string? Problem { get; private set; }

    ProcessLoopback(int processId, Action<byte[], int> write)
    {
        this.processId = processId; this.write = write;
        // Its own thread, in the multithreaded COM apartment: activation and capture both happen there (from WPF's
        // single-threaded UI thread the activated audio client can't be used).
        thread = new Thread(Run) { IsBackground = true, Name = "Process loopback", Priority = ThreadPriority.AboveNormal };
        thread.SetApartmentState(ApartmentState.MTA);
    }

    /// <summary>Starts capturing; write gets 16-bit 48 kHz stereo PCM, continuously (silence included).</summary>
    public static ProcessLoopback Start(int processId, Action<byte[], int> write)
    {
        var loopback = new ProcessLoopback(processId, write);
        loopback.thread.Start();
        if (!loopback.started.Task.Wait(6000)) { loopback.Stop(); throw new TimeoutException("Windows didn't start the sound recording."); }
        if (loopback.started.Task.IsFaulted) throw loopback.started.Task.Exception!.InnerException!;
        return loopback;
    }

    void Run()
    {
        try { client = Activate(processId); }
        catch (Exception ex) { started.TrySetException(ex); return; }
        try
        {
            using var ready = new AutoResetEvent(false);
            const long bufferTicks = 2_000_000; // 200 ms
            client.Initialize(AudioClientShareMode.Shared, AudioClientStreamFlags.Loopback | AudioClientStreamFlags.EventCallback | (AudioClientStreamFlags)0x80000000 /* AUTOCONVERTPCM */ | (AudioClientStreamFlags)0x08000000 /* SRC_DEFAULT_QUALITY */, bufferTicks, 0, Format, Guid.Empty);
            client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle());
            var capture = client.AudioCaptureClient;
            client.Start();
            started.TrySetResult();
            var clock = Stopwatch.StartNew();
            long written = 0; // frames
            int frameBytes = Format.BlockAlign;
            var silence = new byte[Format.AverageBytesPerSecond / 10];
            var buffer = new byte[Format.AverageBytesPerSecond];
            while (!stopping)
            {
                ready.WaitOne(20);
                int packet;
                while ((packet = capture.GetNextPacketSize()) > 0)
                {
                    var data = capture.GetBuffer(out int frames, out var flags);
                    int bytes = frames * frameBytes;
                    if (buffer.Length < bytes) buffer = new byte[bytes];
                    if ((flags & AudioClientBufferFlags.Silent) != 0) Array.Clear(buffer, 0, bytes);
                    else Marshal.Copy(data, buffer, 0, bytes);
                    capture.ReleaseBuffer(frames);
                    write(buffer, bytes); written += frames;
                }
                // Nothing plays → no packets: fill the gap with silence, keeping 40 ms behind the clock for packets in flight.
                long due = (long)(clock.Elapsed.TotalSeconds * Format.SampleRate) - Format.SampleRate / 25;
                while (written < due)
                {
                    int frames = (int)Math.Min(due - written, silence.Length / frameBytes);
                    write(silence, frames * frameBytes); written += frames;
                }
            }
            client.Stop();
        }
        catch (Exception ex) { Problem = ex.Message; started.TrySetException(ex); }
    }

    public void Stop() { stopping = true; if (thread.IsAlive) thread.Join(2000); }
    public void Dispose() { Stop(); client?.Dispose(); }

    // ---- WebView2's browser processes ----
    // The Preview's game plays its sound from WebView2's own processes. Windows doesn't count those as part of this
    // program's tree for loopback, although they are its children, so each WebView2 browser process this program started
    // is captured as a tree of its own (its audio process is inside that one) and mixed in.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ProcessEntry { public int Size, Usage, ProcessId; public IntPtr HeapId; public int ModuleId, Threads, ParentProcessId, Priority, Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile; }
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    /// <summary>The WebView2 browser processes started by a process (its direct msedgewebview2.exe children).</summary>
    public static List<int> WebViewBrowsers(int parentProcessId)
    {
        var found = new List<int>();
        IntPtr snapshot = CreateToolhelp32Snapshot(2 /* TH32CS_SNAPPROCESS */, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) return found;
        try
        {
            var entry = new ProcessEntry { Size = Marshal.SizeOf<ProcessEntry>() };
            for (bool more = Process32FirstW(snapshot, ref entry); more; more = Process32NextW(snapshot, ref entry))
                if (entry.ParentProcessId == parentProcessId && entry.ExeFile.Equals("msedgewebview2.exe", StringComparison.OrdinalIgnoreCase)) found.Add(entry.ProcessId);
        }
        finally { CloseHandle(snapshot); }
        return found;
    }

    // ---- Activation: ActivateAudioInterfaceAsync on the process loopback virtual device ----
    const string VirtualDevice = "VAD\\Process_Loopback";
    [StructLayout(LayoutKind.Sequential)] struct ActivationParams { public int ActivationType; public int TargetProcessId; public int ProcessLoopbackMode; }
    [StructLayout(LayoutKind.Sequential)] struct Blob { public int Size; public IntPtr Data; }
    [StructLayout(LayoutKind.Explicit)] struct PropVariant { [FieldOffset(0)] public short Type; [FieldOffset(8)] public Blob Blob; }

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IActivateAudioInterfacesCompletionHandler { void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation); }
    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IActivateAudioInterfaceAsyncOperation { void GetActivateResult(out int result, [MarshalAs(UnmanagedType.IUnknown)] out object activated); }
    [ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAgileObject { }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    static extern void ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string path, [MarshalAs(UnmanagedType.LPStruct)] Guid iid, IntPtr parameters, IActivateAudioInterfacesCompletionHandler handler, out IActivateAudioInterfaceAsyncOperation operation);

    sealed class Completion : IActivateAudioInterfacesCompletionHandler, IAgileObject
    {
        public readonly ManualResetEventSlim Done = new();
        public object? Client; public int Result;
        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
        {
            try { operation.GetActivateResult(out Result, out var activated); Client = activated; }
            catch (Exception ex) { Result = ex.HResult; }
            Done.Set();
        }
    }

    static AudioClient Activate(int processId)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) throw new PlatformNotSupportedException("Recording an app's own sound needs Windows 10 version 2004 or later.");
        var parameters = new ActivationParams { ActivationType = 1 /* PROCESS_LOOPBACK */, TargetProcessId = processId, ProcessLoopbackMode = 0 /* INCLUDE_TARGET_PROCESS_TREE */ };
        IntPtr paramsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<ActivationParams>()), variantPtr = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariant>());
        try
        {
            Marshal.StructureToPtr(parameters, paramsPtr, false);
            Marshal.StructureToPtr(new PropVariant { Type = 65 /* VT_BLOB */, Blob = new Blob { Size = Marshal.SizeOf<ActivationParams>(), Data = paramsPtr } }, variantPtr, false);
            var completion = new Completion();
            ActivateAudioInterfaceAsync(VirtualDevice, typeof(IAudioClient).GUID, variantPtr, completion, out _);
            if (!completion.Done.Wait(5000)) throw new TimeoutException("Windows didn't start the sound recording.");
            if (completion.Result != 0 || completion.Client is not IAudioClient audio) throw new InvalidOperationException($"Windows refused to record the app's sound (0x{completion.Result:X8}).");
            return new AudioClient(audio);
        }
        finally { Marshal.FreeHGlobal(paramsPtr); Marshal.FreeHGlobal(variantPtr); }
    }
}
