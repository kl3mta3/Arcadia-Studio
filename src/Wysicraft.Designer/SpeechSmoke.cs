using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Wysicraft.Core;
using Wysicraft.Core.Audio;
using Wysicraft.Models;
namespace Wysicraft.Designer;

// --smoke-speech: speech and recording in the real editor. Kokoro speaks on its own thread and the first sentence is
// ready quickly; voices mix; speed changes the length; speech saves as Ogg, WAV and MP3 and is added to the project as a
// sound; Whisper (on its own thread) writes back what was said; a label and a text box take the text; Preview's game is
// recorded with the app's own sound, cropped to exactly the game; a recording is narrated, trimmed, made into a GIF.
// Speech is played quietly while recording, so the test is barely audible.
public partial class MainWindow
{
    internal async Task VerifySpeechAsync(string output)
    {
        void Expect(bool ok, string what) { if (!ok) throw new Exception("Speech: " + what); }
        var results = new List<string>();
        string temp = Path.Combine(Path.GetTempPath(), "Arcadia Studio", "speech-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        Show(); await Task.Delay(400);
        var tts = TextToSpeech.Shared; double volume = tts.Volume;
        try
        {
            Expect(TextToSpeech.Available && SpeechToText.Available && FFmpeg.Exe() != null, "the models, voices and FFmpeg are here (run scripts/Get-MediaTools.ps1)");
            var voices = TextToSpeech.Voices();
            Expect(voices.Count >= 50 && voices[0].Language == "English" && voices.Any(v => v.Id == "af_heart"), "the voices are listed, English first: " + voices.Count);
            Expect(TextToSpeech.Normal("af_heart*3+am_michael") == "af_heart*0.75+am_michael*0.25", "a mix is written the same way every time: " + TextToSpeech.Normal("af_heart*3+am_michael"));
            bool refused = false; try { TextToSpeech.ParseVoice("zz_nobody"); } catch (InvalidDataException) { refused = true; }
            Expect(refused, "an unknown voice is refused");
            Expect(TextToSpeech.Pieces("One. Two, three!\n\nFour?").Count == 3, "text is cut into sentences");
            Expect(TextToSpeech.Pieces("I’ll be there. I’ve got “it”.")[0].Text == "I'll be there." && TextToSpeech.Pieces("I’ve got it.")[0].Text == "I've got it.", "curly apostrophes are read as apostrophes (not spelled out)");
            Expect(TextToSpeech.Advice("Press Play to start.").Count == 0 && TextToSpeech.Advice("See https://itch.io **now** with WASD & 50% off 🎮").Count >= 5, "text that may be misread gets advice, plain text none");

            // The model loads once; then the first sentence is ready quickly, made on the speech thread.
            var clock = Stopwatch.StartNew(); await tts.WarmAsync(); results.Add($"model loaded in {clock.ElapsedMilliseconds} ms");
            string? thread = null; long first = -1; var pieces = new List<float>();
            clock.Restart();
            await tts.Make("Arcadia Studio makes games. Press Preview to try yours, then publish it for your friends.", "af_heart", 1, piece => { if (first < 0) { first = clock.ElapsedMilliseconds; thread = Thread.CurrentThread.Name; } pieces.AddRange(piece); }, default);
            double seconds = pieces.Count / (double)TextToSpeech.Rate;
            Expect(thread == "Kokoro speech", "speech is made on its own thread, not " + thread);
            Expect(first is > 0 and < 1500, "the first sentence is ready in under 1.5 s: " + first + " ms");
            Expect(seconds is > 3 and < 12, "the speech is about the right length: " + seconds);
            results.Add($"first sentence in {first} ms, {seconds:0.0} s of speech in {clock.ElapsedMilliseconds} ms");
            var speech = pieces.ToArray();
            var faster = await tts.RenderAsync("Arcadia Studio makes games.", "af_heart", 1.6);
            var slower = await tts.RenderAsync("Arcadia Studio makes games.", "af_heart", 0.8);
            Expect(faster.Length < slower.Length * 0.7, "speed changes how long it takes to say");
            var mixed = await tts.RenderAsync("A mixed voice.", "af_heart*0.5+bm_george*0.5", 1);
            Expect(mixed.Length > TextToSpeech.Rate / 2, "a mix of two voices speaks");
            var french = await tts.RenderAsync("Bonjour tout le monde.", "ff_siwis", 1);
            Expect(french.Length > TextToSpeech.Rate / 2, "a French voice speaks French");

            // Files and the project.
            foreach (var ext in new[] { ".ogg", ".wav", ".mp3" })
            {
                string file = Path.Combine(temp, "line" + ext); SaveSpeech(file, speech);
                Expect(new FileInfo(file).Length > 10_000, "speech saves as " + ext);
            }
            string saved = SaveSoundAsset("npc_greeting", "ogg", true, null, [ToProjectRate(speech)]);
            Expect(project.Assets.ContainsKey(saved) && SoundAssets.Resource(saved).EndsWith(":npc_greeting"), "speech is added to the project as a sound");

            // Whisper writes back what was said, on its own thread.
            var decoded = await FFmpeg.DecodeAsync(Path.Combine(temp, "line.ogg"), SpeechToText.Rate);
            clock.Restart();
            var segments = await SpeechToText.Shared.TranscribeAsync(decoded, "en");
            string heard = SpeechToText.Join(segments);
            Expect(heard.Contains("Arcadia Studio", StringComparison.OrdinalIgnoreCase) && heard.Contains("Preview", StringComparison.OrdinalIgnoreCase), "Whisper heard it: " + heard);
            Expect(SpeechToText.Srt(segments).Contains(" --> "), "subtitles have times");
            results.Add($"Whisper wrote {decoded.Length / (double)SpeechToText.Rate:0.0} s in {clock.ElapsedMilliseconds} ms: “{heard}”");
            var label = AddTextToScreen("label", heard); var box = AddTextToScreen("textbox", heard);
            Expect(label.Text == heard && box.Value == heard && ui.Elements.Contains(label) && ui.Elements.Contains(box), "the text goes on the screen as a label and a text box");

            // Speaking aloud (quietly) can be stopped at once.
            tts.Volume = 0.15;
            var saying = tts.SayAsync("This sentence is long enough that it is still being spoken when it gets stopped part of the way through.", "af_heart", 1);
            await Task.Delay(900); Expect(tts.Speaking, "speaking shows as speaking");
            clock.Restart(); tts.Stop(); await saying;
            Expect(!tts.Speaking && clock.ElapsedMilliseconds < 500, "stop ends speech at once");

            // Recording Preview's game, with the app's own sound.
            var preview = new PreviewSession(this, Json.CloneProject(project), ui.Id);
            preview.Window.Show();
            try
            {
                await Task.Delay(2500);
                var source = await preview.GameSourceAsync();
                Picture(preview.Window, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "speech-preview.png"));
                string video = Path.Combine(temp, "game.mp4");
                await StartRecordingAsync(source, video, 30, true, preview);
                await tts.SayAsync("Recording the game now.", "af_heart", 1);
                await Task.Delay(500);
                var (file, length, bytes, soundProblem) = await StopRecordingAsync();
                Expect(soundProblem == null, "the app's sound records: " + soundProblem);
                Expect(length is > 1 and < 10 && bytes > 5_000, $"the recording was saved: {length:0.0} s, {bytes} bytes");
                // Exactly the game: the same size as Preview's own capture of it, and the same picture.
                var still = await preview.CaptureScreenAsync();
                var expected = BitmapFrame.Create(new MemoryStream(still));
                string frame = Path.Combine(temp, "frame.png");
                var r = await FFmpeg.RunAsync(["-y", "-sseof", "-0.3", "-i", video, "-frames:v", "1", frame]);
                Expect(r.ExitCode == 0 && File.Exists(frame), "a frame comes out of the recording");
                var got = BitmapFrame.Create(new MemoryStream(File.ReadAllBytes(frame)));
                Expect(Math.Abs(got.PixelWidth - expected.PixelWidth) <= 2 && Math.Abs(got.PixelHeight - expected.PixelHeight) <= 2, $"the recording is the game's size: {got.PixelWidth}×{got.PixelHeight}, the game is {expected.PixelWidth}×{expected.PixelHeight}");
                double difference = Difference(got, expected);
                Expect(difference < 18, "the recording shows the game, not what's around it (average difference " + difference.ToString("0.0") + ")");
                results.Add($"recorded {length:0.0} s of the game at {got.PixelWidth}×{got.PixelHeight} (difference from Preview's own picture {difference:0.0}/255)");
                var sound = await FFmpeg.DecodeAsync(video, SpeechToText.Rate);
                float peak = sound.Length == 0 ? 0 : sound.Max(Math.Abs);
                Expect(peak > 0.005, "the narration is in the recording's sound: peak " + peak);
                string recorded = SpeechToText.Join(await SpeechToText.Shared.TranscribeAsync(sound, "en"));
                results.Add($"the recording's sound says “{recorded}”");
                Expect(recorded.Contains("record", StringComparison.OrdinalIgnoreCase), "the narration can be heard in the recording: " + recorded);

                // The game's own sound is in the recording too. It plays from WebView2's processes, which Windows doesn't
                // count as part of this program for sound capture, so they're captured beside it and mixed in.
                preview.SetMuted(false);
                string gameSound = Path.Combine(temp, "game-sound.mp4");
                await StartRecordingAsync(source, gameSound, 30, true, preview);
                await Task.Delay(1800);
                await preview.RunScriptAsync("ctx.client.playSound('" + SoundAssets.Resource(saved) + "', 0.2)");
                await Task.Delay((int)(seconds * 1000) + 1200);
                await StopRecordingAsync();
                var played = await FFmpeg.DecodeAsync(gameSound, SpeechToText.Rate);
                string gameHeard = SpeechToText.Join(await SpeechToText.Shared.TranscribeAsync(played, "en"));
                Expect(played.Length > 0 && played.Max(Math.Abs) > 0.01 && gameHeard.Contains("Arcadia", StringComparison.OrdinalIgnoreCase), "the game's own sound is recorded (peak " + (played.Length > 0 ? played.Max(Math.Abs) : 0).ToString("0.000") + "): " + gameHeard);
                results.Add($"the game's own sound is in the recording: “{gameHeard}”");

                // Narrating a recording afterwards, cutting it and making a GIF.
                string narrated = Path.Combine(temp, "narrated.mp4");
                await McpVideo("narrate", "", narrated, 0, true, video, 0, 0, 0, "", "", 0, "[{\"at\":0.2,\"text\":\"Here is the game.\"},{\"at\":1.5,\"text\":\"Have fun!\",\"voice\":\"bm_george\"}]", 0.3, "", default);
                var narratedText = SpeechToText.Join(await SpeechToText.Shared.TranscribeAsync(await FFmpeg.DecodeAsync(narrated, SpeechToText.Rate), "en"));
                Expect(narratedText.Contains("game", StringComparison.OrdinalIgnoreCase), "narration is mixed into a video: " + narratedText);
                await McpVideo("gif", "", Path.Combine(temp, "clip.gif"), 10, true, video, 0, 1, 240, "", "", 0, "", 0, "", default);
                Expect(new FileInfo(Path.Combine(temp, "clip.gif")).Length > 1000, "a GIF is made from a recording");
                await McpVideo("trim", "", Path.Combine(temp, "cut.mp4"), 0, true, video, 0.2, 1.2, 0, "", "", 0, "", 0, "", default);
                var cut = await FFmpeg.RunAsync(["-i", Path.Combine(temp, "cut.mp4")]);
                Expect(MediaRecorder.FileDuration(cut.Log) is double d && d is > 0.7 and < 1.5, "a recording is trimmed");
            }
            finally { if (recording != null) await StopRecordingAsync(); await preview.CloseAsync(); }

            // A still screen: Windows sends no new frames, so FFmpeg may not hear the stop. The video must still be whole.
            await Task.Delay(500);
            string stillVideo = Path.Combine(temp, "still.mp4");
            await StartRecordingAsync(new MediaRecorder.Source(new System.Windows.Interop.WindowInteropHelper(this).Handle, IntPtr.Zero, 0, 0, 0, 0, "the editor"), stillVideo, 30, false);
            await Task.Delay(2500);
            clock.Restart();
            var stillResult = await StopRecordingAsync();
            var stillInfo = await FFmpeg.RunAsync(["-i", stillVideo]);
            Expect(clock.Elapsed.TotalSeconds < 15 && MediaRecorder.FileDuration(stillInfo.Log) is double stillSeconds && stillSeconds > 1 && !File.Exists(stillVideo + ".recording.mkv"), $"a recording of a still screen stops and opens ({clock.Elapsed.TotalSeconds:0.0} s to stop): " + FFmpeg.LastLine(stillInfo.Log));
            results.Add($"a still-screen recording stopped in {clock.Elapsed.TotalSeconds:0.0} s and plays ({stillResult.Seconds:0.0} s)");

            // Narration spoken after the screen has been still for a while is in the recording (it used to be lost).
            string quietVideo = Path.Combine(temp, "still-sound.mp4");
            await StartRecordingAsync(new MediaRecorder.Source(new System.Windows.Interop.WindowInteropHelper(this).Handle, IntPtr.Zero, 0, 0, 0, 0, "the editor"), quietVideo, 30, true);
            await Task.Delay(4000);
            tts.Volume = 0.15;
            await tts.SayAsync("This line is spoken while nothing on the screen moves.", "af_heart", 1);
            await Task.Delay(600);
            await StopRecordingAsync();
            string stillHeard = SpeechToText.Join(await SpeechToText.Shared.TranscribeAsync(await FFmpeg.DecodeAsync(quietVideo, SpeechToText.Rate), "en"));
            Expect(stillHeard.Contains("nothing on the screen moves", StringComparison.OrdinalIgnoreCase), "narration during a still screen is recorded: " + stillHeard);
            results.Add($"narration after 4 s of a still screen is recorded: “{stillHeard}”");
            // Pictures of the windows beside the result, for a look at the layout.
            string folder = Path.GetDirectoryName(Path.GetFullPath(output))!;
            ShowSpeechSettings(); ShowCreateAudio("Welcome, traveller! The castle gates open at dawn, so rest here tonight."); ShowCreateText();
            await Task.Delay(900);
            foreach (var w in Application.Current.Windows.OfType<Window>().Where(w => w.Title is "Speech settings" or "Create audio from text" or "Create text from audio").ToList())
            { Picture(w, Path.Combine(folder, "speech-" + w.Title.Replace(' ', '-') + ".png")); w.Close(); }
            File.WriteAllText(output, "PASS: speech and recording.\n" + string.Join("\n", results));
        }
        finally
        {
            tts.Stop(); tts.Volume = volume; dirty = false;
            try { Directory.Delete(temp, true); } catch { }
        }
    }

    static void Picture(Window w, string file) { w.UpdateLayout(); if (w.ActualWidth < 1) return; var bmp = new RenderTargetBitmap((int)w.ActualWidth, (int)w.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bmp.Render(w); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp)); using var stream = File.Create(file); png.Save(stream); }
    /// <summary>The average difference per channel (0-255) between two pictures of about the same size.</summary>
    static double Difference(BitmapSource a, BitmapSource b)
    {
        static byte[] Pixels(BitmapSource s, int w, int h)
        {
            var scaled = new TransformedBitmap(new FormatConvertedBitmap(s, System.Windows.Media.PixelFormats.Bgra32, null, 0), new System.Windows.Media.ScaleTransform(w / (double)s.PixelWidth, h / (double)s.PixelHeight));
            var pixels = new byte[w * h * 4]; scaled.CopyPixels(pixels, w * 4, 0); return pixels;
        }
        int width = Math.Min(a.PixelWidth, b.PixelWidth) / 4, height = Math.Min(a.PixelHeight, b.PixelHeight) / 4;
        var pa = Pixels(a, width, height); var pb = Pixels(b, width, height);
        long total = 0, count = 0;
        for (int i = 0; i < pa.Length; i += 4) for (int c = 0; c < 3; c++) { total += Math.Abs(pa[i + c] - pb[i + c]); count++; }
        return total / (double)count;
    }
}
