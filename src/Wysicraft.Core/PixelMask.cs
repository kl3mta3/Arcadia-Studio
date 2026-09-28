namespace Wysicraft.Core;

/// <summary>A selection in the pixel editor: which pixels are chosen, any shape. Masks never change once made;
/// every operation returns a new one (so undo can keep them as they are).</summary>
public sealed class PixelMask
{
    public int Width { get; }
    public int Height { get; }
    readonly bool[] bits;
    (int X, int Y, int W, int H)? bounds;

    public PixelMask(int width, int height, bool[]? bits = null)
    {
        if (width < 1 || height < 1 || width > PixelArt.MaxSheetSize || height > PixelArt.MaxSheetSize) throw new ArgumentOutOfRangeException(nameof(width));
        if (bits != null && bits.Length != width * height) throw new ArgumentException("Mask size mismatch", nameof(bits));
        Width = width; Height = height; this.bits = bits ?? new bool[width * height];
    }
    public bool this[int x, int y] => x >= 0 && y >= 0 && x < Width && y < Height && bits[y * Width + x];
    public int Count => bits.Count(b => b);
    public bool IsEmpty => !Array.Exists(bits, b => b);

    /// <summary>The smallest box around the chosen pixels ((0,0,0,0) when nothing is chosen).</summary>
    public (int X, int Y, int W, int H) Bounds => bounds ??= Measure();
    (int, int, int, int) Measure()
    {
        int x0 = Width, y0 = Height, x1 = -1, y1 = -1;
        for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++) if (bits[y * Width + x]) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
        return x1 < 0 ? (0, 0, 0, 0) : (x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    public static PixelMask Full(int width, int height) => new(width, height, Enumerable.Repeat(true, width * height).ToArray());
    /// <summary>A box, cut to the canvas.</summary>
    public static PixelMask Box(int width, int height, int x, int y, int w, int h)
    {
        var mask = new bool[width * height];
        for (int row = Math.Max(0, y); row < Math.Min(height, y + h); row++) for (int col = Math.Max(0, x); col < Math.Min(width, x + w); col++) mask[row * width + col] = true;
        return new(width, height, mask);
    }
    PixelMask Combine(PixelMask other, Func<bool, bool, bool> rule)
    {
        var result = new bool[bits.Length];
        for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++) result[y * Width + x] = rule(bits[y * Width + x], other[x, y]);
        return new(Width, Height, result);
    }
    public PixelMask Union(PixelMask other) => Combine(other, (a, b) => a || b);
    public PixelMask Subtract(PixelMask other) => Combine(other, (a, b) => a && !b);
    /// <summary>The chosen pixels inside a box, as a mask the size of that box.</summary>
    public PixelMask Crop(int x, int y, int w, int h)
    {
        var result = new bool[w * h];
        for (int row = 0; row < h; row++) for (int col = 0; col < w; col++) result[row * w + col] = this[x + col, y + row];
        return new(w, h, result);
    }
    /// <summary>A small mask put down at (x, y) on a canvas-sized one; parts past the edge are cut off.</summary>
    public static PixelMask Place(PixelMask piece, int x, int y, int width, int height)
    {
        var result = new bool[width * height];
        for (int row = 0; row < piece.Height; row++) for (int col = 0; col < piece.Width; col++)
        {
            int cx = x + col, cy = y + row;
            if (piece.bits[row * piece.Width + col] && cx >= 0 && cy >= 0 && cx < width && cy < height) result[cy * width + cx] = true;
        }
        return new(width, height, result);
    }
    public PixelMask FlipHorizontal() { var r = new bool[bits.Length]; for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++) r[y * Width + x] = bits[y * Width + Width - 1 - x]; return new(Width, Height, r); }
    public PixelMask FlipVertical() { var r = new bool[bits.Length]; for (int y = 0; y < Height; y++) Array.Copy(bits, (Height - 1 - y) * Width, r, y * Width, Width); return new(Width, Height, r); }

    /// <summary>The edges between chosen and unchosen pixels, as straight lines in pixel units (neighboring edges on
    /// the same line are joined), for drawing the dashed selection outline.</summary>
    public List<(int X1, int Y1, int X2, int Y2)> Outline()
    {
        var lines = new List<(int, int, int, int)>();
        for (int y = 0; y <= Height; y++)
        {
            int start = -1;
            for (int x = 0; x <= Width; x++)
            {
                bool edge = x < Width && this[x, y - 1] != this[x, y];
                if (edge && start < 0) start = x; else if (!edge && start >= 0) { lines.Add((start, y, x, y)); start = -1; }
            }
        }
        for (int x = 0; x <= Width; x++)
        {
            int start = -1;
            for (int y = 0; y <= Height; y++)
            {
                bool edge = y < Height && this[x - 1, y] != this[x, y];
                if (edge && start < 0) start = y; else if (!edge && start >= 0) { lines.Add((x, start, x, y)); start = -1; }
            }
        }
        return lines;
    }
}

public static class PixelWand
{
    /// <summary>Magic wand: the pixels of the clicked color, either only those touching it (4-way) or every one in the
    /// picture.</summary>
    /// <param name="tolerance">0 matches the color exactly; up to 255, a pixel also matches when none of its channels
    /// (alpha, red, green, blue) differs from the clicked one by more than this.</param>
    /// <param name="diagonals">With touchingOnly, pixels that only meet at a corner count as touching too (so one click takes a whole diagonal line).</param>
    public static PixelMask Select(PixelImage image, int x, int y, bool touchingOnly, int tolerance = 0, bool diagonals = false)
    {
        var mask = new bool[image.Width * image.Height];
        if (!image.Contains(x, y)) return new(image.Width, image.Height, mask);
        uint target = image.Get(x, y); tolerance = Math.Clamp(tolerance, 0, 255);
        bool Same(uint c) => c == target || (c >> 24 == 0 && target >> 24 == 0) || (tolerance > 0 && Near(c, target, tolerance)); // every fully transparent pixel counts as the same
        if (!touchingOnly) { for (int i = 0; i < mask.Length; i++) mask[i] = Same(image.Pixels[i]); return new(image.Width, image.Height, mask); }
        var stack = new Stack<(int, int)>(); stack.Push((x, y));
        while (stack.Count > 0)
        {
            var (px, py) = stack.Pop();
            if (!image.Contains(px, py)) continue;
            int i = py * image.Width + px; if (mask[i] || !Same(image.Pixels[i])) continue;
            mask[i] = true;
            stack.Push((px + 1, py)); stack.Push((px - 1, py)); stack.Push((px, py + 1)); stack.Push((px, py - 1));
            if (diagonals) { stack.Push((px + 1, py + 1)); stack.Push((px - 1, py - 1)); stack.Push((px + 1, py - 1)); stack.Push((px - 1, py + 1)); }
        }
        return new(image.Width, image.Height, mask);
    }
    static bool Near(uint a, uint b, int tolerance)
    {
        for (int shift = 0; shift < 32; shift += 8) if (Math.Abs((int)(a >> shift & 0xFF) - (int)(b >> shift & 0xFF)) > tolerance) return false;
        return true;
    }
}
