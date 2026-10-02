namespace Wysicraft.Core;

/// <summary>Soft drawing for the pixel editor: a round brush whose edge fades out, and smoothing for jagged edges.
/// The hard tools (PixelArt) put down exact colors; these mix with what is there, for art that should not look
/// pixelated. The editor and the pixel_art MCP tool both draw through here.
///
/// A stroke is drawn through a coverage map, one byte a pixel (0 = untouched, 255 = fully painted). Each dab only
/// ever raises the coverage, so a stroke that crosses itself or lingers in one place doesn't build up: the result is
/// always the picture as it was before the stroke with the color laid over it by the coverage.</summary>
public static class PixelSoft
{
    public const int MaxSize = 64;

    /// <summary>One round dab. <paramref name="size"/> is the diameter in pixels; <paramref name="hardness"/> is how
    /// much of the radius is solid: 1 is a round brush with only its outline anti-aliased, 0 fades all the way from
    /// the centre. The centre is the pixel's own for odd sizes and its lower-right corner for even ones, like the
    /// square brush.</summary>
    public static void Dab(byte[] coverage, int width, int height, int x, int y, int size, double hardness)
    {
        size = Math.Clamp(size, 1, MaxSize); hardness = double.IsFinite(hardness) ? Math.Clamp(hardness, 0, 1) : 0.5;
        double outer = size / 2.0, cx = x + (size % 2 == 0 ? 1.0 : 0.5), cy = y + (size % 2 == 0 ? 1.0 : 0.5);
        // The fade runs over this many pixels inside the edge: never less than one, so even a hard brush is anti-aliased.
        double feather = Math.Max(1, (outer + 0.5) * (1 - hardness));
        int reach = (int)Math.Ceiling(outer + 0.5);
        for (int py = (int)Math.Floor(cy) - reach; py <= (int)Math.Floor(cy) + reach; py++)
        {
            if (py < 0 || py >= height) continue;
            for (int px = (int)Math.Floor(cx) - reach; px <= (int)Math.Floor(cx) + reach; px++)
            {
                if (px < 0 || px >= width) continue;
                double dx = px + 0.5 - cx, dy = py + 0.5 - cy, t = Math.Clamp((outer + 0.5 - Math.Sqrt(dx * dx + dy * dy)) / feather, 0, 1);
                if (t <= 0) continue;
                byte value = (byte)Math.Round(255 * t * t * (3 - 2 * t)); // eased, so the fade has no visible ring
                int i = py * width + px; if (value > coverage[i]) coverage[i] = value;
            }
        }
    }

    /// <summary>Dabs along a straight line, both ends included.</summary>
    public static void Line(byte[] coverage, int width, int height, int x0, int y0, int x1, int y1, int size, double hardness)
    {
        foreach (var (x, y) in PixelArt.LinePoints(x0, y0, x1, y1)) Dab(coverage, width, height, x, y, size, hardness);
    }

    /// <summary>The part of the picture a line of dabs can have touched, kept inside the picture.</summary>
    public static (int Left, int Top, int Right, int Bottom) Reach(int width, int height, int x0, int y0, int x1, int y1, int size)
    {
        int pad = Math.Clamp(size, 1, MaxSize) / 2 + 2;
        return (Math.Max(0, Math.Min(x0, x1) - pad), Math.Max(0, Math.Min(y0, y1) - pad), Math.Min(width - 1, Math.Max(x0, x1) + pad), Math.Min(height - 1, Math.Max(y0, y1) + pad));
    }

    /// <summary>One pixel with a color laid over it by a coverage. A transparent color erases instead: the pixel
    /// keeps its color and loses that much of its alpha.</summary>
    public static uint Mix(uint under, uint color, byte coverage)
    {
        if (coverage == 0) return under;
        if (color >> 24 != 0) return PixelArt.Over(color, under, coverage / 255.0);
        uint alpha = (uint)Math.Round((under >> 24) * (255 - coverage) / 255.0);
        return alpha == 0 ? 0 : alpha << 24 | (under & 0xFFFFFF);
    }

    /// <summary>Paints <paramref name="target"/> inside a box: the picture before the stroke with the color laid over
    /// it by the coverage.</summary>
    public static void Paint(PixelImage basis, PixelImage target, byte[] coverage, uint color, int left, int top, int right, int bottom)
    {
        int w = basis.Width;
        for (int y = Math.Max(0, top); y <= Math.Min(basis.Height - 1, bottom); y++)
            for (int x = Math.Max(0, left); x <= Math.Min(w - 1, right); x++)
            {
                int i = y * w + x; target.Pixels[i] = Mix(basis.Pixels[i], color, coverage[i]);
            }
    }

    /// <summary>A pixel averaged with its eight neighbours (the middle counts four times, the sides twice, the
    /// corners once). Colors are weighed by how solid they are, so a see-through neighbour lends its transparency and
    /// not a dark or bright fringe. Past the picture's edge, the edge pixel is repeated. A flat area comes out as it
    /// went in, so only edges change.</summary>
    public static uint Smoothed(PixelImage image, int x, int y)
    {
        long a = 0, r = 0, g = 0, b = 0;
        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
        {
            uint p = image.Pixels[Math.Clamp(y + dy, 0, image.Height - 1) * image.Width + Math.Clamp(x + dx, 0, image.Width - 1)];
            int weight = (dx == 0 ? 2 : 1) * (dy == 0 ? 2 : 1); long pa = (p >> 24) * weight;
            a += pa; r += ((p >> 16) & 0xFF) * pa; g += ((p >> 8) & 0xFF) * pa; b += (p & 0xFF) * pa;
        }
        if (a == 0) return 0;
        uint alpha = (uint)((a + 8) / 16);
        return alpha == 0 ? 0 : alpha << 24 | (uint)((r + a / 2) / a) << 16 | (uint)((g + a / 2) / a) << 8 | (uint)((b + a / 2) / a);
    }

    /// <summary>Two pixels mixed by a coverage (0 = all <paramref name="from"/>, 255 = all <paramref name="to"/>), weighing color by alpha.</summary>
    static uint Blend(uint from, uint to, byte coverage)
    {
        if (coverage == 0 || from == to) return from; if (coverage == 255) return to;
        long fa = from >> 24, ta = to >> 24, wf = fa * (255 - coverage), wt = ta * coverage, total = wf + wt;
        if (total == 0) return 0;
        uint Channel(int shift) => (uint)((((from >> shift) & 0xFF) * wf + ((to >> shift) & 0xFF) * wt + total / 2) / total);
        uint alpha = (uint)((total + 127) / 255);
        return alpha == 0 ? 0 : alpha << 24 | Channel(16) << 16 | Channel(8) << 8 | Channel(0);
    }

    /// <summary>Smooths <paramref name="target"/> inside a box: each pixel moves toward its smoothed value from the
    /// picture before the stroke, by the coverage (everywhere in the box when there is none).</summary>
    public static void Smooth(PixelImage basis, PixelImage target, byte[]? coverage, int left, int top, int right, int bottom)
    {
        int w = basis.Width;
        for (int y = Math.Max(0, top); y <= Math.Min(basis.Height - 1, bottom); y++)
            for (int x = Math.Max(0, left); x <= Math.Min(w - 1, right); x++)
            {
                int i = y * w + x; byte amount = coverage == null ? (byte)255 : coverage[i];
                if (amount != 0) target.Pixels[i] = Blend(basis.Pixels[i], Smoothed(basis, x, y), amount);
            }
    }

    /// <summary>Smooths a whole picture, or the part inside a selection, a number of times over. Each pass softens
    /// edges by about a pixel more; flat areas are left as they are.</summary>
    public static PixelImage SmoothEdges(PixelImage image, PixelMask? inside = null, int passes = 1)
    {
        var result = image;
        for (int pass = 0; pass < Math.Clamp(passes, 1, 8); pass++)
        {
            var next = result.Clone();
            for (int y = 0; y < image.Height; y++) for (int x = 0; x < image.Width; x++)
                if (inside == null || inside[x, y]) next.Pixels[y * image.Width + x] = Smoothed(result, x, y);
            result = next;
        }
        return result;
    }
}
