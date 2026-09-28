using System.ComponentModel;
namespace Wysicraft.Core;

/// <summary>One drawing or layer command for the pixel_art MCP tool. Which fields matter depends on <see cref="Op"/>.</summary>
public sealed class PixelCommand
{
    [Description("pixels, grid, line, rect, ellipse, fill, clear, flip, shift, add_layer, add_group, set_layer, move_layer, merge_down, delete_layer, add_frame, delete_frame, move_frame, resize")]
    public string Op { get; set; } = "";
    [Description("Layer or group name to act on. Empty = the top drawing layer.")]
    public string Layer { get; set; } = "";
    [Description("Frame number (0-based).")]
    public int Frame { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int X2 { get; set; }
    public int Y2 { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Dx { get; set; }
    public int Dy { get; set; }
    [Description("#RRGGBB, #AARRGGBB (see-through) or transparent.")]
    public string Color { get; set; } = "";
    public bool Filled { get; set; }
    [Description("Brush size 1-8 for line and rect outlines.")]
    public int Size { get; set; } = 1;
    [Description("0-255: fill also takes colors this close to the clicked one.")]
    public int Tolerance { get; set; }
    [Description("Mix see-through colors with what's there instead of replacing it.")]
    public bool Blend { get; set; }
    [Description("pixels: [[x,y],...] painted with color.")]
    public List<int[]> Points { get; set; } = [];
    [Description("grid: rows of characters painted from (x,y); each character is looked up in palette. Characters not in the palette are left as they are.")]
    public List<string> Rows { get; set; } = [];
    [Description("grid: one-character keys to colors (#RRGGBB, #AARRGGBB or transparent).")]
    public Dictionary<string, string> Palette { get; set; } = [];
    [Description("add_layer / add_group: the new name. set_layer: a new name.")]
    public string Name { get; set; } = "";
    [Description("add_layer / add_group: the group to put it in (empty = top level).")]
    public string Parent { get; set; } = "";
    [Description("flip: horizontal or vertical. move_layer: up, down, into (the group next to it) or out (of its group).")]
    public string Direction { get; set; } = "";
    [Description("set_layer: 0-1.")]
    public double? Opacity { get; set; }
    public bool? Visible { get; set; }
    public bool? Locked { get; set; }
    [Description("add_frame / move_frame: position to put the frame at (default: the end).")]
    public int? At { get; set; }
    [Description("add_frame: copy this frame instead of adding an empty one.")]
    public int? CopyOf { get; set; }
}

/// <summary>Runs pixel_art commands on a layered document, the same drawing the pixel editor does.</summary>
public static class PixelCommands
{
    public const int MaxCommands = 512;

    public static void Apply(PixelDocument doc, IReadOnlyList<PixelCommand> commands)
    {
        if (commands.Count > MaxCommands) throw new InvalidDataException($"At most {MaxCommands} commands at once.");
        for (int i = 0; i < commands.Count; i++)
        {
            var c = commands[i];
            try { Run(doc, c); }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException) { throw new InvalidDataException($"Command {i + 1} ({c.Op}): {ex.Message}"); }
        }
    }

    public static uint ParseColor(string text)
    {
        var t = text.Trim();
        if (t.Equals("transparent", StringComparison.OrdinalIgnoreCase) || t.Equals("none", StringComparison.OrdinalIgnoreCase)) return 0;
        return PixelArt.TryParseColor(t, out var c) ? c : throw new InvalidDataException($"\"{text}\" isn't a color. Use #RRGGBB, #AARRGGBB or transparent.");
    }

    static PixelLayer Find(PixelDocument doc, string name) =>
        name.Length == 0 ? doc.DrawingLayers().Last() : doc.All().FirstOrDefault(l => l.Name == name) ?? throw new InvalidDataException($"There's no layer or group called \"{name}\".");
    static PixelLayer Drawable(PixelDocument doc, string name)
    {
        var layer = Find(doc, name);
        if (layer.IsGroup) throw new InvalidDataException($"\"{layer.Name}\" is a group. Draw on a layer inside it.");
        if (doc.LockedHere(layer)) throw new InvalidDataException($"\"{layer.Name}\" is locked.");
        return layer;
    }
    static int CheckFrame(PixelDocument doc, int frame) => frame >= 0 && frame < doc.FrameCount ? frame : throw new InvalidDataException($"Frame {frame} doesn't exist (0–{doc.FrameCount - 1}).");
    // The picture being changed: replaced by a copy first, like the editor does.
    static PixelImage Target(PixelDocument doc, PixelCommand c) { var layer = Drawable(doc, c.Layer); int f = CheckFrame(doc, c.Frame); return layer.Cells[f] = layer.Cells[f].Clone(); }
    // Draws with a painter; with blending, onto a clear sheet first, then laid over what's there.
    static void Draw(PixelDocument doc, PixelCommand c, Action<PixelImage> paint)
    {
        var cell = Target(doc, c);
        if (!c.Blend) { paint(cell); return; }
        var stroke = new PixelImage(cell.Width, cell.Height); paint(stroke);
        for (int i = 0; i < cell.Pixels.Length; i++) if (stroke.Pixels[i] >> 24 != 0) cell.Pixels[i] = PixelArt.Over(stroke.Pixels[i], cell.Pixels[i]);
    }
    static string Unique(PixelDocument doc, string name)
    {
        name = name.Trim(); if (name.Length == 0) throw new InvalidDataException("Give it a name.");
        if (name.Length > 64) throw new InvalidDataException("Names are at most 64 characters.");
        if (doc.All().Any(l => l.Name == name)) throw new InvalidDataException($"There's already a layer or group called \"{name}\".");
        return name;
    }
    static void Place(PixelDocument doc, PixelLayer item, string parent)
    {
        if (parent.Length == 0) { doc.Insert(item, null); return; }
        var group = Find(doc, parent); if (!group.IsGroup) throw new InvalidDataException($"\"{parent}\" isn't a group.");
        if (doc.Depth(group) + 1 + PixelDocument.Nesting(item) > PixelDocument.MaxDepth) throw new InvalidDataException($"Groups nest at most {PixelDocument.MaxDepth} deep.");
        if (doc.All().Count() + item.Self().Count() > PixelDocument.MaxLayers) throw new InvalidDataException($"At most {PixelDocument.MaxLayers} layers and groups.");
        group.Children!.Add(item);
    }

    static void Run(PixelDocument doc, PixelCommand c)
    {
        switch (c.Op)
        {
            case "pixels":
            {
                uint color = ParseColor(c.Color);
                if (c.Points.Count == 0) throw new InvalidDataException("Give points as [[x,y],...].");
                if (c.Points.Count > 65536) throw new InvalidDataException("At most 65536 points.");
                Draw(doc, c, img => { foreach (var p in c.Points) { if (p.Length != 2) throw new InvalidDataException("Each point is [x,y]."); img.Set(p[0], p[1], color); } });
                break;
            }
            case "grid":
            {
                if (c.Rows.Count == 0) throw new InvalidDataException("Give rows of characters.");
                if (c.Rows.Count > PixelArt.MaxSize || c.Rows.Any(r => r.Length > PixelArt.MaxSize)) throw new InvalidDataException($"Grids are at most {PixelArt.MaxSize}×{PixelArt.MaxSize}.");
                var colors = new Dictionary<char, uint>();
                foreach (var (key, value) in c.Palette) { if (key.Length != 1) throw new InvalidDataException($"Palette keys are single characters (\"{key}\")."); colors[key[0]] = ParseColor(value); }
                if (colors.Count == 0) throw new InvalidDataException("Give a palette, e.g. {\"R\":\"#FF0000\",\"_\":\"transparent\"}.");
                Draw(doc, c, img => { for (int row = 0; row < c.Rows.Count; row++) for (int col = 0; col < c.Rows[row].Length; col++) if (colors.TryGetValue(c.Rows[row][col], out var color)) img.Set(c.X + col, c.Y + row, color); });
                break;
            }
            case "line": { uint color = ParseColor(c.Color); Draw(doc, c, img => PixelArt.Line(img, c.X, c.Y, c.X2, c.Y2, color, Math.Clamp(c.Size, 1, 8))); break; }
            case "rect": { uint color = ParseColor(c.Color); Draw(doc, c, img => PixelArt.Rectangle(img, c.X, c.Y, c.X2, c.Y2, color, c.Filled, Math.Clamp(c.Size, 1, 8))); break; }
            case "ellipse": { uint color = ParseColor(c.Color); Draw(doc, c, img => PixelArt.Ellipse(img, c.X, c.Y, c.X2, c.Y2, color, c.Filled)); break; }
            case "fill":
            {
                uint color = ParseColor(c.Color); var cell = Target(doc, c);
                if (!cell.Contains(c.X, c.Y)) throw new InvalidDataException($"({c.X}, {c.Y}) is outside the {doc.Width}×{doc.Height} picture.");
                if (!c.Blend) { PixelArt.Fill(cell, c.X, c.Y, color, Math.Clamp(c.Tolerance, 0, 255)); break; }
                var area = PixelWand.Select(cell, c.X, c.Y, true, Math.Clamp(c.Tolerance, 0, 255));
                for (int y = 0; y < cell.Height; y++) for (int x = 0; x < cell.Width; x++) if (area[x, y]) cell.Pixels[y * cell.Width + x] = PixelArt.Over(color, cell.Pixels[y * cell.Width + x]);
                break;
            }
            case "clear":
            {
                var cell = Target(doc, c);
                if (c.Width > 0 && c.Height > 0) PixelArt.Clear(cell, c.X, c.Y, c.Width, c.Height); else Array.Clear(cell.Pixels);
                break;
            }
            case "flip":
            {
                var layer = Drawable(doc, c.Layer); int f = CheckFrame(doc, c.Frame);
                layer.Cells[f] = c.Direction switch { "horizontal" => PixelArt.FlipHorizontal(layer.Cells[f]), "vertical" => PixelArt.FlipVertical(layer.Cells[f]), _ => throw new InvalidDataException("direction is horizontal or vertical.") };
                break;
            }
            case "shift":
            {
                var layer = Find(doc, c.Layer); int f = CheckFrame(doc, c.Frame);
                foreach (var l in layer.Self().Where(l => !l.IsGroup))
                {
                    if (doc.LockedHere(l)) throw new InvalidDataException($"\"{l.Name}\" is locked.");
                    l.Cells[f] = PixelArt.Shift(l.Cells[f], c.Dx, c.Dy);
                }
                break;
            }
            case "add_layer": Place(doc, doc.NewLayer(Unique(doc, c.Name)), c.Parent); break;
            case "add_group": Place(doc, PixelDocument.NewGroup(Unique(doc, c.Name)), c.Parent); break;
            case "set_layer":
            {
                var layer = Find(doc, c.Layer);
                if (c.Name.Length > 0 && c.Name != layer.Name) layer.Name = Unique(doc, c.Name);
                if (c.Opacity is double o) { if (!double.IsFinite(o) || o is < 0 or > 1) throw new InvalidDataException("opacity is 0–1."); layer.Opacity = o; }
                if (c.Visible is bool v) layer.Visible = v;
                if (c.Locked is bool k) layer.Locked = k;
                break;
            }
            case "move_layer":
            {
                var layer = Find(doc, c.Layer);
                bool moved = c.Direction switch { "up" => doc.Move(layer, 1), "down" => doc.Move(layer, -1), "into" => doc.IntoGroup(layer), "out" => doc.OutOfGroup(layer), _ => throw new InvalidDataException("direction is up, down, into or out.") };
                if (!moved) throw new InvalidDataException(c.Direction switch { "into" => "There's no group next to it to move into.", "out" => "It isn't in a group.", _ => "It's already at the " + (c.Direction == "up" ? "top." : "bottom.") });
                break;
            }
            case "merge_down": doc.MergeDown(Find(doc, c.Layer)); break;
            case "delete_layer": doc.Remove(Find(doc, c.Layer)); break;
            case "add_frame":
            {
                int at = c.At ?? doc.FrameCount; if (at < 0 || at > doc.FrameCount) throw new InvalidDataException($"at is 0–{doc.FrameCount}.");
                doc.InsertFrame(at, c.CopyOf is int copy ? CheckFrame(doc, copy) : -1);
                break;
            }
            case "delete_frame": if (doc.FrameCount <= 1) throw new InvalidDataException("A picture needs at least one frame."); doc.RemoveFrame(CheckFrame(doc, c.Frame)); break;
            case "move_frame":
            {
                int from = CheckFrame(doc, c.Frame), to = CheckFrame(doc, c.At ?? doc.FrameCount - 1);
                for (int f = from; f != to; f += Math.Sign(to - from)) doc.SwapFrames(f, f + Math.Sign(to - from));
                break;
            }
            case "resize": doc.Resize(c.Width, c.Height); break;
            default: throw new InvalidDataException("Unknown op. Use pixels, grid, line, rect, ellipse, fill, clear, flip, shift, add_layer, add_group, set_layer, move_layer, merge_down, delete_layer, add_frame, delete_frame, move_frame or resize.");
        }
    }

    /// <summary>A picture as text for an assistant to read: one character per pixel ('.' = transparent) and the colors
    /// they stand for. Null when it's too big or has too many colors to write this way.</summary>
    public static (List<string> Rows, Dictionary<string, string> Palette)? Describe(PixelImage image, int maxSize = 64)
    {
        if (image.Width > maxSize || image.Height > maxSize) return null;
        const string symbols = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789#@$%&*+=?";
        var keys = new Dictionary<uint, char>(); var palette = new Dictionary<string, string>(); var rows = new List<string>();
        for (int y = 0; y < image.Height; y++)
        {
            var row = new char[image.Width];
            for (int x = 0; x < image.Width; x++)
            {
                uint c = image.Get(x, y);
                if (c >> 24 == 0) { row[x] = '.'; continue; }
                if (!keys.TryGetValue(c, out var key)) { if (keys.Count == symbols.Length) return null; key = symbols[keys.Count]; keys[c] = key; palette[key.ToString()] = PixelArt.ToHex(c); }
                row[x] = key;
            }
            rows.Add(new string(row));
        }
        return (rows, palette);
    }
}
