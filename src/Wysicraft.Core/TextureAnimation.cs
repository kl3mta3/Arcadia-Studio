using System.Text.Json;
namespace Wysicraft.Core;

/// <summary>A Minecraft-style animated texture: frames stacked in the PNG (e.g. prismarine) and timed by a
/// <c>.png.mcmeta</c> file. Frame times are game ticks (50 ms). Same rules as the runtime's TextureAnimation.java;
/// interpolation is not supported, frames switch.</summary>
public sealed class TextureAnimation
{
    public sealed record Frame(int Index, int Ticks);
    public int FrameWidth { get; private init; }
    public int FrameHeight { get; private init; }
    public int Columns { get; private init; }
    public int Rows { get; private init; }
    public IReadOnlyList<Frame> Frames { get; private init; } = [];
    public int TotalTicks => Frames.Sum(f => f.Ticks);

    /// <summary>The .png.mcmeta for frames laid out left to right, then top to bottom (as the pixel editor saves them),
    /// each shown for <paramref name="frametime"/> ticks (50 ms). Only the first <paramref name="frames"/> cells play.</summary>
    public static string Write(int frameWidth, int frameHeight, int frames, int cells, int frametime)
    {
        var animation = new Dictionary<string, object> { ["frametime"] = Math.Max(1, frametime), ["width"] = frameWidth, ["height"] = frameHeight };
        if (cells != frames) animation["frames"] = Enumerable.Range(0, frames).ToArray();
        return JsonSerializer.Serialize(new Dictionary<string, object> { ["animation"] = animation });
    }
    /// <summary>Frames per second as whole game ticks per frame (at least one tick).</summary>
    public static int TicksFor(double fps) => Math.Max(1, (int)Math.Round(20 / Math.Clamp(fps, 0.1, 20)));

    /// <summary>Null when the metadata has no "animation" section, or it doesn't fit the image.</summary>
    public static TextureAnimation? Parse(string? mcmeta, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(mcmeta) || width < 1 || height < 1) return null;
        try
        {
            using var doc = JsonDocument.Parse(mcmeta);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("animation", out var animation) || animation.ValueKind != JsonValueKind.Object) return null;
            int Int(string name, int fallback) => animation.TryGetProperty(name, out var v) && v.TryGetInt32(out int n) ? n : fallback;
            int fw = Int("width", -1), fh = Int("height", -1);
            // Minecraft's defaults: square frames the size of the image's shorter side.
            if (fw < 1 && fh < 1) fw = fh = Math.Min(width, height); else if (fw < 1) fw = fh; else if (fh < 1) fh = fw;
            if (fw > width || fh > height) return null;
            int columns = width / fw, rows = height / fh, count = columns * rows, frametime = Math.Max(1, Int("frametime", 1));
            var frames = new List<Frame>();
            if (animation.TryGetProperty("frames", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in list.EnumerateArray())
                {
                    int index, ticks = frametime;
                    if (entry.ValueKind == JsonValueKind.Object) { index = entry.GetProperty("index").GetInt32(); if (entry.TryGetProperty("time", out var t)) ticks = Math.Max(1, t.GetInt32()); }
                    else index = entry.GetInt32();
                    if (index >= 0 && index < count) frames.Add(new(index, ticks));
                    if (frames.Count >= 1024) break;
                }
            }
            else for (int i = 0; i < Math.Min(count, 1024); i++) frames.Add(new(i, frametime));
            return frames.Count == 0 ? null : new TextureAnimation { FrameWidth = fw, FrameHeight = fh, Columns = columns, Rows = rows, Frames = frames };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or KeyNotFoundException) { return null; }
    }

    /// <summary>The frame index (position in the image) showing at this time.</summary>
    public int FrameAt(long milliseconds)
    {
        long tick = ((milliseconds / 50) % TotalTicks + TotalTicks) % TotalTicks;
        foreach (var frame in Frames) { if (tick < frame.Ticks) return frame.Index; tick -= frame.Ticks; }
        return Frames[^1].Index;
    }
    public int Column(int index) => index % Columns;
    public int Row(int index) => index / Columns;
}
