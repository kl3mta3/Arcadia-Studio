using System.Security.Cryptography;
using MeltySynth;
namespace Wysicraft.Core.Audio;

/// <summary>SoundFonts (.sf2 banks of recorded instruments) a song can be played with instead of the built-in chip
/// sounds. One comes with Wysicraft (GeneralUser GS); more can be downloaded from the catalog, or any .sf2 imported.
/// Downloaded and imported fonts are kept in the user's app data folder, shared by every project.</summary>
public static class SoundFonts
{
    public sealed record Entry(string Name, string Url, long Bytes, string Md5, string License, string Description);

    /// <summary>Fonts that can be downloaded (the first is the bundled one). Names are also the file names.</summary>
    public static readonly Entry[] Catalog =
    [
        new("GeneralUser GS", "https://github.com/mrbumpy409/GeneralUser-GS/raw/main/GeneralUser-GS.sf2", 32_319_396, "",
            "Free to use in anything, commercial included (S. Christian Collins).",
            "Comes with Wysicraft. A balanced, clean General MIDI and GS bank: 261 instruments and 13 drum kits, small and quick to load. A good all-rounder for game music."),
        new("FluidR3 GM", "https://archive.org/download/fluidr3-gm-gs/FluidR3_GM_GS.sf2", 151_001_312, "dc92de8c177bdcbdf1c45746e0f15370",
            "MIT licence (Frank Wen).",
            "The classic free General MIDI bank used by many Linux music programs and MuseScore. Full, realistic orchestral and band instruments with rich pianos and strings."),
        new("FatBoy", "https://archive.org/download/fat-boy-v-0.790/FatBoy-v0.790.sf2", 320_147_782, "ea4606e1f6fb1c8f4915c92daa9e2867",
            "Creative Commons Attribution-ShareAlike 3.0 (credit FatBoy in your project).",
            "A large, punchy General MIDI and GS bank tuned for classic DOS game music. Big drums and bright, characterful instruments; the largest download."),
        new("Arachno", "https://archive.org/download/arachno-sound-font-version-1.0/Arachno%20SoundFont%20-%20Version%201.0.sf2", 155_405_818, "4d4a2a0d6311c60d939d14b840982ea0",
            "Free for personal use; its author advises against commercial use, as it collects samples from many sources (Maxime Abbey).",
            "Made to bring old game MIDI music to life: famous sounds from Roland, Korg, Yamaha and E-mu synths, 128 instruments and 9 drum kits. Great for 90s game soundtracks."),
    ];

    /// <summary>Where the bundled font is (next to the program) and where downloaded and imported fonts are kept.</summary>
    public static string BundledFolder { get; set; } = Path.Combine(AppContext.BaseDirectory, "SoundFonts");
    public static string UserFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wysicraft", "SoundFonts");

    /// <summary>The fonts ready to use: catalog ones first, then imported ones, by name.</summary>
    public static List<string> Installed()
    {
        var names = new List<string>();
        foreach (var folder in new[] { BundledFolder, UserFolder })
            if (Directory.Exists(folder))
                foreach (var f in Directory.GetFiles(folder, "*.sf2")) { var n = Path.GetFileNameWithoutExtension(f); if (!names.Contains(n, StringComparer.OrdinalIgnoreCase)) names.Add(n); }
        return names.OrderBy(n => Array.FindIndex(Catalog, e => e.Name.Equals(n, StringComparison.OrdinalIgnoreCase)) is var i && i >= 0 ? i : 100).ThenBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }
    public static string? PathOf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (var folder in new[] { UserFolder, BundledFolder })
        {
            var path = Path.Combine(folder, name + ".sf2");
            if (File.Exists(path)) return path;
        }
        return null;
    }
    public static bool IsBundled(string name) => File.Exists(Path.Combine(BundledFolder, name + ".sf2"));
    public static bool IsCatalog(string name) => Catalog.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Downloads a catalog font (reporting 0 to 1), checking its size and checksum; a partial download is
    /// thrown away.</summary>
    public static async Task Download(Entry entry, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        Directory.CreateDirectory(UserFolder);
        string path = Path.Combine(UserFolder, entry.Name + ".sf2"), part = path + ".part";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(60) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Wysicraft");
            using var response = await http.GetAsync(entry.Url, HttpCompletionOption.ResponseHeadersRead, cancel);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? entry.Bytes, got = 0;
            using var md5 = MD5.Create();
            await using (var from = await response.Content.ReadAsStreamAsync(cancel))
            await using (var to = File.Create(part))
            {
                var buffer = new byte[1 << 16]; int read;
                while ((read = await from.ReadAsync(buffer, cancel)) > 0)
                {
                    await to.WriteAsync(buffer.AsMemory(0, read), cancel); md5.TransformBlock(buffer, 0, read, null, 0); got += read;
                    progress?.Report(Math.Min(1, got / (double)total));
                }
            }
            md5.TransformFinalBlock([], 0, 0);
            if (new FileInfo(part).Length != entry.Bytes || entry.Md5.Length > 0 && !Convert.ToHexString(md5.Hash!).Equals(entry.Md5, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The download was incomplete or damaged; try again.");
            File.Move(part, path, true);
        }
        finally { try { if (File.Exists(part)) File.Delete(part); } catch { } }
    }

    /// <summary>Copies any .sf2 file into the user's fonts (checking it opens as a SoundFont). Returns its name.</summary>
    public static string Import(string file)
    {
        try { using var stream = File.OpenRead(file); _ = new SoundFont(stream); }
        catch (Exception ex) { throw new InvalidDataException($"{Path.GetFileName(file)} isn't a SoundFont 2 (.sf2) file that can be read: {ex.Message}"); }
        Directory.CreateDirectory(UserFolder);
        string name = string.Concat(Path.GetFileNameWithoutExtension(file).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        if (name.Length == 0) name = "SoundFont";
        File.Copy(file, Path.Combine(UserFolder, name + ".sf2"), true);
        lock (cache) cache.Remove(name);
        return name;
    }
    /// <summary>Deletes a downloaded or imported font (the bundled one stays).</summary>
    public static void Remove(string name)
    {
        lock (cache) cache.Remove(name);
        var path = Path.Combine(UserFolder, name + ".sf2");
        if (File.Exists(path)) File.Delete(path);
    }

    static readonly Dictionary<string, SoundFont> cache = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The loaded font (kept in memory once loaded; only the two most recent stay). Null when it isn't installed.</summary>
    public static SoundFont? Load(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        lock (cache)
        {
            if (cache.TryGetValue(name, out var font)) return font;
            var path = PathOf(name); if (path == null) return null;
            font = new SoundFont(path);
            if (cache.Count >= 2) cache.Remove(cache.Keys.First());
            return cache[name] = font;
        }
    }

    /// <summary>A font's instruments: melodic ones (banks below 128) and drum kits (bank 128), in bank and program order.</summary>
    public static List<(int Bank, int Program, string Name)> Presets(SoundFont font, bool drums) =>
        font.Presets.Where(p => drums ? p.BankNumber >= 128 : p.BankNumber < 128).OrderBy(p => p.BankNumber).ThenBy(p => p.PatchNumber)
            .Select(p => (p.BankNumber, p.PatchNumber, p.Name.Trim())).ToList();

    /// <summary>A layer's instrument as MIDI: channel 10 for drums, the bank and the program (General MIDI numbers, so
    /// a song keeps its instruments when the font changes). Chip instruments that were never given one get the nearest.</summary>
    public static (int Channel, int Bank, int Program) Patch(Instrument ins)
    {
        if (ins.Program >= 0) return ins.Bank >= 128 ? (9, 128, ins.Program) : (0, ins.Bank, ins.Program);
        return ins.Wave == "drums" ? (9, 128, 0) : (0, 0, Midi.ProgramFor(ins.Name));
    }
    public static void Select(Synthesizer synth, int channel, int bank, int program)
    {
        if (channel != 9) synth.ProcessMidiMessage(channel, 0xB0, 0, bank);
        synth.ProcessMidiMessage(channel, 0xC0, program, 0);
    }
    public static Synthesizer NewSynth(SoundFont font, int sampleRate) => new(font, new SynthesizerSettings(sampleRate) { EnableReverbAndChorus = true, MaximumPolyphony = 96 });
    /// <summary>The key a note plays: its pitch moved by the layer's transpose and octave (drum notes pick a drum, so stay).</summary>
    public static int Key(Track t, Note n, bool drums) => Math.Clamp(drums ? n.Pitch : n.Pitch + t.Transpose + t.Instrument.Octave * 12, 0, 127);

    /// <summary>One layer played by the font (stereo), for the steps given, into buffers of the given length.</summary>
    public static (float[] Left, float[] Right) RenderTrack(SoundFont font, Track track, int sampleRate, int fromStep, int toStep, double stepSeconds, int length)
    {
        var left = new float[length]; var right = new float[length];
        var synth = NewSynth(font, sampleRate);
        var (channel, bank, program) = Patch(track.Instrument); bool drums = channel == 9;
        Select(synth, channel, bank, program);
        var events = new List<(int At, int Order, int Key, int Velocity)>();
        foreach (var n in track.Notes)
        {
            if (n.Step >= toStep || n.Step + n.Length <= fromStep) continue;
            int on = Math.Max(0, (int)Math.Round((n.Step - fromStep) * stepSeconds * sampleRate)), off = (int)Math.Round((n.Step + n.Length - fromStep) * stepSeconds * sampleRate);
            int key = Key(track, n, drums), velocity = Math.Clamp((int)Math.Round(n.Velocity * 127), 1, 127);
            events.Add((on, 1, key, velocity)); events.Add((Math.Min(off, length), 0, key, 0));
        }
        int at = 0;
        foreach (var e in events.OrderBy(e => e.At).ThenBy(e => e.Order))
        {
            int to = Math.Min(e.At, length);
            if (to > at) { synth.Render(left.AsSpan(at, to - at), right.AsSpan(at, to - at)); at = to; }
            if (e.Order == 1) synth.NoteOn(channel, e.Key, e.Velocity); else synth.NoteOff(channel, e.Key);
        }
        if (length > at) synth.Render(left.AsSpan(at), right.AsSpan(at));
        return (left, right);
    }
}
