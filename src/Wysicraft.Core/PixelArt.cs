namespace Wysicraft.Core;

/// <summary>An image for the pixel editor: 32-bit ARGB (0xAARRGGBB), row by row from the top-left. In memory on
/// Windows that is the same byte order as WPF's Bgra32, so it can be shown and saved without converting.</summary>
public sealed class PixelImage
{
    public int Width { get; }
    public int Height { get; }
    public uint[] Pixels { get; }
    public PixelImage(int width, int height, uint[]? pixels = null)
    {
        if (width < 1 || height < 1 || width > PixelArt.MaxSheetSize || height > PixelArt.MaxSheetSize) throw new ArgumentOutOfRangeException(nameof(width), $"Images are 1–{PixelArt.MaxSheetSize} pixels each way");
        if (pixels != null && pixels.Length != width * height) throw new ArgumentException("Pixel count doesn't match the size", nameof(pixels));
        Width = width; Height = height; Pixels = pixels ?? new uint[width * height];
    }
    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
    public uint Get(int x, int y) => Contains(x, y) ? Pixels[y * Width + x] : 0;
    public void Set(int x, int y, uint color) { if (Contains(x, y)) Pixels[y * Width + x] = color; }
    public PixelImage Clone() => new(Width, Height, (uint[])Pixels.Clone());
    public bool IsEmpty => Pixels.All(p => p >> 24 == 0);
}

/// <summary>Drawing for the pixel editor. Colors replace what's there (no blending), so a half-transparent color
/// paints exactly that color, and transparent (0) erases.</summary>
public static class PixelArt
{
    /// <summary>The largest canvas (one frame) the editor draws on. What actually costs memory is area × frames ×
    /// layers, not one edge, so the edge is generous and <see cref="MaxPixels"/> bounds the product: a single 2048 ×
    /// 2048 frame is 4 megapixels and fine, while 512 × 512 across 256 frames is 67 and is not.</summary>
    public const int MaxSize = 4096;
    /// <summary>Width × height × frames, across the whole document. Saving flattens the layers into one PNG, so this
    /// is a budget on what the editor holds while it works, not on what a project ships.</summary>
    public const long MaxPixels = 64L * 1024 * 1024;
    /// <summary>Whether a document of this shape fits the budget, and why not when it doesn't.</summary>
    public static bool Fits(int width, int height, int frames, out string why)
    {
        why = "";
        if (width < 1 || height < 1 || width > MaxSize || height > MaxSize) { why = $"Pixel art is 1–{MaxSize} pixels each way"; return false; }
        if (frames < 1 || frames > MaxFrames) { why = $"1–{MaxFrames} frames"; return false; }
        long total = (long)width * height * frames;
        if (total > MaxPixels) { why = $"{width}×{height} across {frames} frame(s) is {total / (1024 * 1024)} megapixels; the editor works up to {MaxPixels / (1024 * 1024)}. Use a smaller canvas or fewer frames."; return false; }
        return true;
    }
    /// <summary>Undo steps for a canvas of this size: a stroke copies the cell it paints, so a big canvas keeps
    /// fewer of them. 200 at 512 × 512 or smaller, never fewer than 20.</summary>
    public static int UndoSteps(int width, int height)
    {
        long area = (long)width * height;
        return (int)Math.Clamp(200L * 512 * 512 / Math.Max(1, area), 20, 200);
    }
    /// <summary>The largest saved image, e.g. a sprite sheet of many frames.</summary>
    public const int MaxSheetSize = 4096;
    public const int MaxFrames = 256;
    /// <summary>Sprite sheets wrap onto a new row past this width.</summary>
    public const int MaxSheetWidth = 2048;

    /// <summary>A square brush of <paramref name="size"/> pixels, centred on the point (odd sizes) or just up-left of it (even).</summary>
    public static void Plot(PixelImage image, int x, int y, uint color, int size = 1)
    {
        size = Math.Clamp(size, 1, 64); int start = -(size - 1) / 2;
        for (int dy = 0; dy < size; dy++) for (int dx = 0; dx < size; dx++) image.Set(x + start + dx, y + start + dy, color);
    }

    /// <summary>Points on a straight line, both ends included (Bresenham).</summary>
    public static IEnumerable<(int X, int Y)> LinePoints(int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1, dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, error = dx + dy;
        while (true)
        {
            yield return (x0, y0);
            if (x0 == x1 && y0 == y1) yield break;
            int e2 = 2 * error;
            if (e2 >= dy) { error += dy; x0 += sx; }
            if (e2 <= dx) { error += dx; y0 += sy; }
        }
    }
    public static void Line(PixelImage image, int x0, int y0, int x1, int y1, uint color, int size = 1)
    {
        foreach (var (x, y) in LinePoints(x0, y0, x1, y1)) Plot(image, x, y, color, size);
    }

    public static void Rectangle(PixelImage image, int x0, int y0, int x1, int y1, uint color, bool filled, int size = 1)
    {
        int left = Math.Min(x0, x1), right = Math.Max(x0, x1), top = Math.Min(y0, y1), bottom = Math.Max(y0, y1);
        if (filled) { for (int y = top; y <= bottom; y++) for (int x = left; x <= right; x++) image.Set(x, y, color); return; }
        Line(image, left, top, right, top, color, size); Line(image, left, bottom, right, bottom, color, size);
        Line(image, left, top, left, bottom, color, size); Line(image, right, top, right, bottom, color, size);
    }

    /// <summary>An ellipse filling the box between two corners. A pixel is inside when its centre is; the outline is
    /// the inside pixels that touch the outside, which keeps small circles round and symmetric.</summary>
    public static void Ellipse(PixelImage image, int x0, int y0, int x1, int y1, uint color, bool filled)
    {
        int left = Math.Min(x0, x1), right = Math.Max(x0, x1), top = Math.Min(y0, y1), bottom = Math.Max(y0, y1);
        double cx = (left + right + 1) / 2.0, cy = (top + bottom + 1) / 2.0, rx = (right - left + 1) / 2.0, ry = (bottom - top + 1) / 2.0;
        bool Inside(int x, int y)
        {
            if (x < left || x > right || y < top || y > bottom) return false;
            double nx = (x + 0.5 - cx) / rx, ny = (y + 0.5 - cy) / ry; return nx * nx + ny * ny <= 1.0001;
        }
        for (int y = top; y <= bottom; y++) for (int x = left; x <= right; x++)
        {
            if (!Inside(x, y)) continue;
            if (filled || !Inside(x - 1, y) || !Inside(x + 1, y) || !Inside(x, y - 1) || !Inside(x, y + 1)) image.Set(x, y, color);
        }
    }

    /// <summary>Paints the area of exactly matching color around a point (4-way). Returns how many pixels changed.</summary>
    /// <param name="tolerance">0 fills exactly one color; up to 255, colors whose channels are within that much of it too.</param>
    public static int Fill(PixelImage image, int x, int y, uint color, int tolerance = 0)
    {
        if (!image.Contains(x, y) || (tolerance == 0 && image.Get(x, y) == color)) return 0;
        var area = PixelWand.Select(image, x, y, true, tolerance); int changed = 0;
        for (int py = 0; py < image.Height; py++) for (int px = 0; px < image.Width; px++)
        {
            int i = py * image.Width + px;
            if (area[px, py] && image.Pixels[i] != color) { image.Pixels[i] = color; changed++; }
        }
        return changed;
    }

    public static PixelImage FlipHorizontal(PixelImage image)
    {
        var result = new PixelImage(image.Width, image.Height);
        for (int y = 0; y < image.Height; y++) for (int x = 0; x < image.Width; x++) result.Pixels[y * image.Width + x] = image.Pixels[y * image.Width + image.Width - 1 - x];
        return result;
    }
    public static PixelImage FlipVertical(PixelImage image)
    {
        var result = new PixelImage(image.Width, image.Height);
        for (int y = 0; y < image.Height; y++) Array.Copy(image.Pixels, (image.Height - 1 - y) * image.Width, result.Pixels, y * image.Width, image.Width);
        return result;
    }
    /// <summary>A new canvas size. The picture keeps its top-left corner; new space is transparent.</summary>
    public static PixelImage Resize(PixelImage image, int width, int height)
    {
        var result = new PixelImage(width, height);
        for (int y = 0; y < Math.Min(height, image.Height); y++) Array.Copy(image.Pixels, y * image.Width, result.Pixels, y * width, Math.Min(width, image.Width));
        return result;
    }

    /// <summary>How many columns a sheet of <paramref name="frames"/> frames uses: one row until it would pass
    /// <see cref="MaxSheetWidth"/>, then as many columns as fit.</summary>
    public static int SheetColumns(int frameWidth, int frames) => Math.Max(1, Math.Min(frames, MaxSheetWidth / Math.Max(1, frameWidth)));

    /// <summary>Lays frames out as a sprite sheet, numbered left to right then top to bottom (as Sprite controls count them).</summary>
    public static PixelImage Pack(IReadOnlyList<PixelImage> frames)
    {
        if (frames.Count == 0) throw new ArgumentException("No frames", nameof(frames));
        int w = frames[0].Width, h = frames[0].Height;
        if (frames.Any(f => f.Width != w || f.Height != h)) throw new ArgumentException("Frames must all be the same size", nameof(frames));
        int columns = SheetColumns(w, frames.Count), rows = (frames.Count + columns - 1) / columns;
        if (w * columns > MaxSheetSize || h * rows > MaxSheetSize) throw new InvalidOperationException($"The sprite sheet would be larger than {MaxSheetSize} pixels; use fewer or smaller frames.");
        var sheet = new PixelImage(w * columns, h * rows);
        for (int i = 0; i < frames.Count; i++)
        {
            int ox = i % columns * w, oy = i / columns * h;
            for (int y = 0; y < h; y++) Array.Copy(frames[i].Pixels, y * w, sheet.Pixels, (oy + y) * sheet.Width + ox, w);
        }
        return sheet;
    }

    /// <summary>Cuts a sheet into frames in reading order. Empty frames at the end (the unused cells of the last row)
    /// are dropped, but there is always at least one frame.</summary>
    public static List<PixelImage> Unpack(PixelImage sheet, int frameWidth, int frameHeight) => Unpack(sheet, frameWidth, frameHeight, null);
    /// <summary>With <paramref name="count"/>, exactly that many frames (empty ones kept or added), so every layer of a
    /// layered picture ends up with the same frames.</summary>
    public static List<PixelImage> Unpack(PixelImage sheet, int frameWidth, int frameHeight, int? count)
    {
        frameWidth = Math.Clamp(frameWidth, 1, Math.Min(MaxSize, sheet.Width)); frameHeight = Math.Clamp(frameHeight, 1, Math.Min(MaxSize, sheet.Height));
        int columns = sheet.Width / frameWidth, rows = sheet.Height / frameHeight;
        var frames = new List<PixelImage>();
        for (int r = 0; r < rows; r++) for (int c = 0; c < columns && frames.Count < MaxFrames; c++)
        {
            var frame = new PixelImage(frameWidth, frameHeight);
            for (int y = 0; y < frameHeight; y++) Array.Copy(sheet.Pixels, (r * frameHeight + y) * sheet.Width + c * frameWidth, frame.Pixels, y * frameWidth, frameWidth);
            frames.Add(frame);
        }
        if (count is int exact)
        {
            if (frames.Count > exact) frames.RemoveRange(exact, frames.Count - exact);
            while (frames.Count < exact) frames.Add(new PixelImage(frameWidth, frameHeight));
            return frames;
        }
        while (frames.Count > 1 && frames[^1].IsEmpty) frames.RemoveAt(frames.Count - 1);
        if (frames.Count == 0) frames.Add(new PixelImage(frameWidth, frameHeight));
        return frames;
    }

    /// <summary>Lays <paramref name="source"/> over <paramref name="destination"/> (standard alpha blending), faded by
    /// <paramref name="opacity"/>. Colors are straight (not premultiplied) ARGB.</summary>
    public static uint Over(uint source, uint destination, double opacity = 1)
    {
        int sa = opacity >= 1 ? (int)(source >> 24) : (int)Math.Round((source >> 24) * Math.Clamp(opacity, 0, 1));
        if (sa <= 0) return destination;
        int da = (int)(destination >> 24);
        if (sa == 255 || da == 0) return (uint)sa << 24 | (source & 0xFFFFFF);
        int keep = da * (255 - sa), outA255 = sa * 255 + keep; // alpha × 255
        uint Channel(int shift)
        {
            int s = (int)(source >> shift) & 0xFF, d = (int)(destination >> shift) & 0xFF;
            return (uint)Math.Clamp((s * sa * 255 + d * keep + outA255 / 2) / outA255, 0, 255);
        }
        uint outA = (uint)((outA255 + 127) / 255);
        return outA << 24 | Channel(16) << 16 | Channel(8) << 8 | Channel(0);
    }
    /// <summary>A copy of part of a picture (areas past the edge come out transparent).</summary>
    public static PixelImage Crop(PixelImage image, int x, int y, int width, int height)
    {
        var result = new PixelImage(width, height);
        for (int row = 0; row < height; row++) for (int col = 0; col < width; col++) result.Pixels[row * width + col] = image.Get(x + col, y + row);
        return result;
    }
    /// <summary>Puts a picture down at a position. Transparent pixels leave what's underneath; the others replace
    /// it, or are blended over it when <paramref name="blend"/> is on. Parts past the edge are cut off.</summary>
    public static void Paste(PixelImage source, PixelImage destination, int x, int y, bool blend = false)
    {
        for (int row = 0; row < source.Height; row++) for (int col = 0; col < source.Width; col++)
        {
            uint c = source.Pixels[row * source.Width + col]; if (c >> 24 == 0 || !destination.Contains(x + col, y + row)) continue;
            int i = (y + row) * destination.Width + x + col; destination.Pixels[i] = blend ? Over(c, destination.Pixels[i]) : c;
        }
    }
    public static void Clear(PixelImage image, int x, int y, int width, int height)
    {
        for (int row = y; row < y + height; row++) for (int col = x; col < x + width; col++) image.Set(col, row, 0);
    }
    /// <summary>The picture moved by (dx, dy); what moves past the edge is cut off.</summary>
    public static PixelImage Shift(PixelImage image, int dx, int dy)
    {
        var result = new PixelImage(image.Width, image.Height);
        for (int y = 0; y < image.Height; y++) for (int x = 0; x < image.Width; x++) result.Set(x + dx, y + dy, image.Pixels[y * image.Width + x]);
        return result;
    }
    /// <summary>Lays one picture over another of the same size.</summary>
    public static void Blit(PixelImage source, PixelImage destination, double opacity = 1)
    {
        var s = source.Pixels; var d = destination.Pixels;
        for (int i = 0; i < s.Length; i++) if (s[i] >> 24 != 0) d[i] = Over(s[i], d[i], opacity);
    }

    /// <summary>Parses #RRGGBB or #AARRGGBB (the # is optional).</summary>
    public static bool TryParseColor(string text, out uint color)
    {
        color = 0; text = text.Trim().TrimStart('#');
        if (text.Length is not (6 or 8) || !uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var value)) return false;
        color = text.Length == 6 ? 0xFF000000 | value : value; return true;
    }
    public static string ToHex(uint color) => color >> 24 == 0xFF ? $"#{color & 0xFFFFFF:X6}" : $"#{color:X8}";
}
