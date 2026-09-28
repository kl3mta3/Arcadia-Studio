using System.Globalization;
using System.Text.RegularExpressions;
namespace Wysicraft.Core;

public static partial class Validation
{
    /// <summary>Key names used by the Key event and inputs (same as the runtimes' KeyNames).</summary>
    public static readonly HashSet<string> KeyNames = [.. Enumerable.Range('a', 26).Select(c => ((char)c).ToString()), .. Enumerable.Range(0, 10).Select(d => d.ToString()),
        "space", "enter", "escape", "tab", "backspace", "left", "right", "up", "down", .. Enumerable.Range(1, 12).Select(n => "f" + n)];
}

/// <summary>Sprite clips, written "idle: 0; run: 1-6 @12; jump: 7,8,9 @8 once". Frames are numbered left to right,
/// top to bottom in the sheet; @ is frames per second (default 8); "once" stops on the last frame.</summary>
public static partial class SpriteClips
{
    public sealed record Clip(string Name, int[] Frames, double Fps, bool Loop);
    public static Dictionary<string, Clip> Parse(string text)
    {
        var clips = new Dictionary<string, Clip>();
        if (string.IsNullOrWhiteSpace(text)) return clips;
        if (text.Length > 4096) throw new FormatException("Clips are at most 4096 characters");
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var m = ClipPattern().Match(part);
            if (!m.Success) throw new FormatException($"Clip \"{part}\": write it like  run: 1-6 @12");
            var frames = new List<int>();
            foreach (var piece in m.Groups["frames"].Value.Split(',', StringSplitOptions.TrimEntries))
            {
                var range = piece.Split('-', StringSplitOptions.TrimEntries);
                int a = int.Parse(range[0], CultureInfo.InvariantCulture), b = range.Length > 1 ? int.Parse(range[1], CultureInfo.InvariantCulture) : a;
                for (int f = a; a <= b ? f <= b : f >= b; f += a <= b ? 1 : -1) { frames.Add(f); if (frames.Count > 1024) throw new FormatException("A clip has at most 1024 frames"); }
            }
            double fps = m.Groups["fps"].Success ? double.Parse(m.Groups["fps"].Value, CultureInfo.InvariantCulture) : 8;
            if (fps is <= 0 or > 120) throw new FormatException($"Clip {m.Groups["name"].Value}: 0.1–120 frames per second");
            if (!clips.TryAdd(m.Groups["name"].Value, new(m.Groups["name"].Value, [.. frames], fps, !m.Groups["once"].Success))) throw new FormatException("Two clips are named " + m.Groups["name"].Value);
        }
        return clips;
    }
    /// <summary>Clips as text, e.g. "idle: 0 @8; run: 1-6 @12; jump: 7,8,9 @8 once". Three or more frames in a row
    /// (up or down) become a range.</summary>
    public static string Format(IEnumerable<Clip> clips) =>
        string.Join("; ", clips.Select(c => $"{c.Name}: {FormatFrames(c.Frames)} @{c.Fps.ToString("0.##", CultureInfo.InvariantCulture)}{(c.Loop ? "" : " once")}"));
    public static string FormatFrames(IReadOnlyList<int> frames)
    {
        var parts = new List<string>();
        for (int i = 0; i < frames.Count;)
        {
            int j = i;
            if (i + 1 < frames.Count && Math.Abs(frames[i + 1] - frames[i]) == 1) { int step = frames[i + 1] - frames[i]; while (j + 1 < frames.Count && frames[j + 1] - frames[j] == step) j++; }
            if (j - i >= 2) { parts.Add(frames[i].ToString(CultureInfo.InvariantCulture) + "-" + frames[j].ToString(CultureInfo.InvariantCulture)); i = j + 1; }
            else { parts.Add(frames[i].ToString(CultureInfo.InvariantCulture)); i++; }
        }
        return string.Join(",", parts);
    }
    /// <summary>The frame showing, milliseconds after the clip started.</summary>
    public static int FrameAt(Clip clip, double elapsedMs)
    {
        int step = (int)Math.Floor(Math.Max(0, elapsedMs) * clip.Fps / 1000);
        return clip.Frames[clip.Loop ? step % clip.Frames.Length : Math.Min(step, clip.Frames.Length - 1)];
    }
    [GeneratedRegex(@"^(?<name>[A-Za-z_][A-Za-z0-9_]{0,63})\s*:\s*(?<frames>\d{1,5}(\s*-\s*\d{1,5})?(\s*,\s*\d{1,5}(\s*-\s*\d{1,5})?)*)\s*(@\s*(?<fps>\d{1,3}(\.\d+)?))?\s*(?<once>once)?$")]
    private static partial Regex ClipPattern();
}

/// <summary>Shape outlines on a unit square (0–1 each way), stretched to the control's size. Every renderer fills the
/// same outline, so shapes look alike in the editor, Minecraft and web apps.</summary>
public static class Shapes
{
    public static readonly string[] Names = ["rectangle", "ellipse", "triangle", "diamond", "hexagon", "star"];
    public static (double X, double Y)[] Outline(string shape)
    {
        switch (shape)
        {
            case "ellipse": return [.. Enumerable.Range(0, 48).Select(i => (0.5 + 0.5 * Math.Cos(i * Math.PI / 24), 0.5 + 0.5 * Math.Sin(i * Math.PI / 24)))];
            case "triangle": return [(0.5, 0), (1, 1), (0, 1)];
            case "diamond": return [(0.5, 0), (1, 0.5), (0.5, 1), (0, 0.5)];
            case "hexagon": return [(0.25, 0), (0.75, 0), (1, 0.5), (0.75, 1), (0.25, 1), (0, 0.5)];
            case "star": return [.. Enumerable.Range(0, 10).Select(i => { double r = i % 2 == 0 ? 0.5 : 0.2, a = -Math.PI / 2 + i * Math.PI / 5; return (0.5 + r * Math.Cos(a), 0.5 + r * Math.Sin(a)); })];
            default: return [(0, 0), (1, 0), (1, 1), (0, 1)];
        }
    }
}
