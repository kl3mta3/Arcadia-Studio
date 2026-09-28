using System.IO.Compression;
using System.Runtime.InteropServices;
using Wysicraft.Models;
namespace Wysicraft.Core;

/// <summary>A layer or a group of layers in the pixel editor. A layer has one picture (cell) per frame; a group holds
/// other layers and groups and has no pictures of its own.</summary>
public sealed class PixelLayer
{
    public string Name { get; set; } = "Layer";
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public double Opacity { get; set; } = 1;
    public List<PixelLayer>? Children { get; set; }
    public List<PixelImage> Cells { get; set; } = [];
    public bool IsGroup => Children != null;
    /// <summary>A copy of the tree that shares the pictures. Painting replaces a cell with a fresh copy first, so a
    /// structural copy is all undo needs.</summary>
    public PixelLayer CloneStructure() => new() { Name = Name, Visible = Visible, Locked = Locked, Opacity = Opacity, Children = Children?.Select(c => c.CloneStructure()).ToList(), Cells = [.. Cells] };
    public PixelLayer CloneDeep() => new() { Name = Name, Visible = Visible, Locked = Locked, Opacity = Opacity, Children = Children?.Select(c => c.CloneDeep()).ToList(), Cells = Cells.Select(c => c.Clone()).ToList() };
    /// <summary>This layer and everything inside it.</summary>
    public IEnumerable<PixelLayer> Self() { yield return this; foreach (var c in Children ?? []) foreach (var d in c.Self()) yield return d; }
}

/// <summary>Layered, animated pixel art. Layers are listed bottom to top; every drawing layer has one cell per frame.
/// Saving flattens it into a PNG; the layers themselves are kept beside it as an editor-only file.</summary>
public sealed class PixelDocument
{
    public const int MaxLayers = 128, MaxDepth = 16;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int FrameCount { get; private set; }
    public List<PixelLayer> Layers { get; private set; } = [];

    public PixelDocument(int width, int height, int frames = 1)
    {
        if (!PixelArt.Fits(width, height, frames, out string why)) throw new ArgumentOutOfRangeException(nameof(width), why);
        Width = width; Height = height; FrameCount = frames;
    }
    /// <summary>One layer holding these frames.</summary>
    public static PixelDocument FromFrames(IReadOnlyList<PixelImage> frames, string name = "Layer 1")
    {
        var doc = new PixelDocument(frames[0].Width, frames[0].Height, frames.Count);
        if (frames.Any(f => f.Width != doc.Width || f.Height != doc.Height)) throw new ArgumentException("Frames must all be the same size");
        doc.Layers.Add(new PixelLayer { Name = name, Cells = [.. frames] }); return doc;
    }
    public PixelLayer NewLayer(string name) => new() { Name = name, Cells = Enumerable.Range(0, FrameCount).Select(_ => new PixelImage(Width, Height)).ToList() };
    public static PixelLayer NewGroup(string name) => new() { Name = name, Children = [] };

    /// <summary>Structural copy for undo (cells shared).</summary>
    public PixelDocument Snapshot() => new(Width, Height, FrameCount) { Layers = Layers.Select(l => l.CloneStructure()).ToList() };
    public IEnumerable<PixelLayer> All() => Layers.SelectMany(l => l.Self());
    public IEnumerable<PixelLayer> DrawingLayers() => All().Where(l => !l.IsGroup);
    /// <summary>The list a layer sits in (the top level or its group's children).</summary>
    public List<PixelLayer> SiblingsOf(PixelLayer layer) => ParentOf(layer)?.Children ?? Layers;
    public PixelLayer? ParentOf(PixelLayer layer) => All().FirstOrDefault(g => g.Children?.Contains(layer) == true);
    /// <summary>How many group levels a layer holds inside it (0 for a layer or an empty group).</summary>
    public static int Nesting(PixelLayer layer) => layer.Children is { Count: > 0 } c ? 1 + c.Max(Nesting) : 0;
    public int Depth(PixelLayer layer) { int d = 0; for (var p = ParentOf(layer); p != null; p = ParentOf(p)) d++; return d; }
    /// <summary>Visible here and in every group around it.</summary>
    public bool ShownOnCanvas(PixelLayer layer) { for (PixelLayer? l = layer; l != null; l = ParentOf(l)) if (!l.Visible) return false; return true; }
    public bool LockedHere(PixelLayer layer) { for (PixelLayer? l = layer; l != null; l = ParentOf(l)) if (l.Locked) return true; return false; }

    /// <summary>Rows for a layers panel: top of the picture first, with how deeply each is nested.</summary>
    public List<(PixelLayer Layer, int Depth)> Rows(Func<PixelLayer, bool>? expanded = null)
    {
        var rows = new List<(PixelLayer, int)>();
        void Walk(List<PixelLayer> list, int depth) { for (int i = list.Count - 1; i >= 0; i--) { rows.Add((list[i], depth)); if (list[i].IsGroup && (expanded?.Invoke(list[i]) ?? true)) Walk(list[i].Children!, depth + 1); } }
        Walk(Layers, 0); return rows;
    }

    // ---- Pictures ----
    /// <summary>What a frame looks like: visible layers stacked with their opacity (groups are stacked first, then
    /// laid down with the group's opacity).</summary>
    /// <param name="swapLayer">Optionally shows <paramref name="swapImage"/> in place of this layer's picture (the editor
    /// uses it to show a selection being moved before it's put down).</param>
    public PixelImage Compose(int frame, PixelLayer? swapLayer = null, PixelImage? swapImage = null)
    {
        var result = new PixelImage(Width, Height);
        void Stack(List<PixelLayer> list, PixelImage target)
        {
            foreach (var layer in list)
            {
                if (!layer.Visible || layer.Opacity <= 0) continue;
                if (layer.IsGroup) { var inner = new PixelImage(Width, Height); Stack(layer.Children!, inner); PixelArt.Blit(inner, target, layer.Opacity); }
                else PixelArt.Blit(layer == swapLayer && swapImage != null ? swapImage : layer.Cells[frame], target, layer.Opacity);
            }
        }
        Stack(Layers, result); return result;
    }
    public List<PixelImage> ComposeAll() => Enumerable.Range(0, FrameCount).Select(f => Compose(f)).ToList();

    // ---- Frames (every layer at once) ----
    public void InsertFrame(int at, int copyOf = -1)
    {
        if (!PixelArt.Fits(Width, Height, FrameCount + 1, out string why)) throw new InvalidOperationException(why);
        foreach (var layer in DrawingLayers()) layer.Cells.Insert(at, copyOf >= 0 ? layer.Cells[copyOf].Clone() : new PixelImage(Width, Height));
        FrameCount++;
    }
    public void RemoveFrame(int at)
    {
        if (FrameCount <= 1) return;
        foreach (var layer in DrawingLayers()) layer.Cells.RemoveAt(at);
        FrameCount--;
    }
    public void SwapFrames(int a, int b) { foreach (var layer in DrawingLayers()) (layer.Cells[a], layer.Cells[b]) = (layer.Cells[b], layer.Cells[a]); }
    public void Resize(int width, int height)
    {
        if (!PixelArt.Fits(width, height, FrameCount, out string why)) throw new ArgumentOutOfRangeException(nameof(width), why);
        foreach (var layer in DrawingLayers()) for (int f = 0; f < FrameCount; f++) layer.Cells[f] = PixelArt.Resize(layer.Cells[f], width, height);
        Width = width; Height = height;
    }
    /// <summary>Cuts a one-frame document into frames of the given size, in every layer.</summary>
    public void SplitIntoFrames(int frameWidth, int frameHeight)
    {
        if (FrameCount != 1) throw new InvalidOperationException("Splitting works on a single picture. This one already has frames.");
        if (Width % frameWidth != 0 || Height % frameHeight != 0) throw new InvalidOperationException($"{Width}×{Height} doesn't divide evenly into {frameWidth}×{frameHeight} frames.");
        int count = DrawingLayers().Select(l => PixelArt.Unpack(l.Cells[0], frameWidth, frameHeight).Count).DefaultIfEmpty(1).Max();
        foreach (var layer in DrawingLayers()) layer.Cells = PixelArt.Unpack(layer.Cells[0], frameWidth, frameHeight, count);
        Width = frameWidth; Height = frameHeight; FrameCount = count;
    }

    // ---- Layer tree ----
    void CheckRoom(int adding) { if (All().Count() + adding > MaxLayers) throw new InvalidOperationException($"At most {MaxLayers} layers and groups."); }
    /// <summary>Puts a new layer or group directly above <paramref name="above"/> (in its list), or at the top.</summary>
    public void Insert(PixelLayer item, PixelLayer? above)
    {
        CheckRoom(item.Self().Count());
        if (above == null) { Layers.Add(item); return; }
        var list = SiblingsOf(above); list.Insert(list.IndexOf(above) + 1, item);
        if (Depth(item) + Nesting(item) > MaxDepth) { list.Remove(item); throw new InvalidOperationException($"Groups nest at most {MaxDepth} deep."); }
    }
    public void Remove(PixelLayer layer)
    {
        if (!DrawingLayers().Except(layer.Self()).Any()) throw new InvalidOperationException("A picture needs at least one layer to draw on.");
        SiblingsOf(layer).Remove(layer);
    }
    public PixelLayer Duplicate(PixelLayer layer) { var copy = layer.CloneDeep(); copy.Name = layer.Name + " copy"; Insert(copy, layer); return copy; }
    /// <summary>Moves a layer up (+1, towards the top of the picture) or down among its siblings.</summary>
    public bool Move(PixelLayer layer, int direction)
    {
        var list = SiblingsOf(layer); int i = list.IndexOf(layer), j = i + Math.Sign(direction);
        if (j < 0 || j >= list.Count) return false;
        (list[i], list[j]) = (list[j], list[i]); return true;
    }
    /// <summary>Moves a layer into the group next to it: the group just under it on the panel (it becomes that
    /// group's top item), or failing that the group just above it (it becomes its bottom item).</summary>
    public bool IntoGroup(PixelLayer layer)
    {
        var list = SiblingsOf(layer); int i = list.IndexOf(layer);
        PixelLayer? below = i > 0 ? list[i - 1] : null, above = i + 1 < list.Count ? list[i + 1] : null;
        var target = below?.IsGroup == true ? below : above?.IsGroup == true ? above : null;
        if (target == null) return false;
        if (Depth(target) + 1 + Nesting(layer) > MaxDepth) throw new InvalidOperationException($"Groups nest at most {MaxDepth} deep.");
        list.Remove(layer);
        if (target == below) target.Children!.Add(layer); else target.Children!.Insert(0, layer);
        return true;
    }
    /// <summary>Moves a layer out of its group, to just above the group.</summary>
    public bool OutOfGroup(PixelLayer layer)
    {
        var parent = ParentOf(layer); if (parent == null) return false;
        parent.Children!.Remove(layer); var list = SiblingsOf(parent); list.Insert(list.IndexOf(parent) + 1, layer); return true;
    }
    /// <summary>Stacks a layer onto the layer just under it (both must be drawing layers) and removes it.</summary>
    public PixelLayer MergeDown(PixelLayer layer)
    {
        var list = SiblingsOf(layer); int i = list.IndexOf(layer);
        if (layer.IsGroup || i == 0 || list[i - 1].IsGroup) throw new InvalidOperationException("Merge down needs a layer with another layer (not a group) right under it.");
        var below = list[i - 1];
        for (int f = 0; f < FrameCount; f++) { var merged = below.Cells[f].Clone(); if (layer.Visible) PixelArt.Blit(layer.Cells[f], merged, layer.Opacity); below.Cells[f] = merged; }
        list.RemoveAt(i); return below;
    }

    // ---- Saved layers (editor-only file beside the PNG) ----
    sealed class LayerData { public string Name { get; set; } = ""; public bool Visible { get; set; } = true; public bool Locked { get; set; } public double Opacity { get; set; } = 1; public bool Group { get; set; } public List<LayerData>? Children { get; set; } public List<string>? Cells { get; set; } }
    sealed class FileData { public string Format { get; set; } = ""; public int Version { get; set; } public int Width { get; set; } public int Height { get; set; } public int Frames { get; set; } public List<LayerData> Layers { get; set; } = []; }
    public const string FileFormat = "wysicraft-pixel-layers";
    static readonly System.Text.Json.JsonSerializerOptions Compact = new(Json.Options) { WriteIndented = false };

    public byte[] Save()
    {
        LayerData Write(PixelLayer l) => new() { Name = l.Name, Visible = l.Visible, Locked = l.Locked, Opacity = l.Opacity, Group = l.IsGroup, Children = l.Children?.Select(Write).ToList(), Cells = l.IsGroup ? null : l.Cells.Select(Pack).ToList() };
        var data = new FileData { Format = FileFormat, Version = 1, Width = Width, Height = Height, Frames = FrameCount, Layers = Layers.Select(Write).ToList() };
        return System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(data, Compact));
    }
    /// <summary>Just the frame size and count from a saved layers file (without reading the pictures), or null.</summary>
    public static (int FrameWidth, int FrameHeight, int Frames)? ReadFrames(byte[] bytes)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(bytes); var root = doc.RootElement;
            if (root.GetProperty("format").GetString() != FileFormat) return null;
            int w = root.GetProperty("width").GetInt32(), h = root.GetProperty("height").GetInt32(), n = root.GetProperty("frames").GetInt32();
            return w >= 1 && h >= 1 && n >= 1 ? (w, h, n) : null;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }
    public static PixelDocument Load(byte[] bytes)
    {
        if (bytes.Length > 32 * 1024 * 1024) throw new InvalidDataException("Layer file too large");
        var data = System.Text.Json.JsonSerializer.Deserialize<FileData>(bytes, Json.Options) ?? throw new InvalidDataException("Empty layer file");
        if (data.Format != FileFormat || data.Version != 1) throw new InvalidDataException("Not a Wysicraft layer file");
        var doc = new PixelDocument(data.Width, data.Height, data.Frames); int count = 0;
        PixelLayer Read(LayerData d, int depth)
        {
            if (++count > MaxLayers || depth > MaxDepth) throw new InvalidDataException("Too many layers");
            var layer = new PixelLayer { Name = d.Name.Length > 64 ? d.Name[..64] : d.Name, Visible = d.Visible, Locked = d.Locked, Opacity = double.IsFinite(d.Opacity) ? Math.Clamp(d.Opacity, 0, 1) : 1 };
            if (d.Group) layer.Children = (d.Children ?? []).Select(c => Read(c, depth + 1)).ToList();
            else
            {
                if (d.Cells == null || d.Cells.Count != data.Frames) throw new InvalidDataException("A layer is missing frames");
                layer.Cells = d.Cells.Select(c => Unpack(c, data.Width, data.Height)).ToList();
            }
            return layer;
        }
        doc.Layers = data.Layers.Select(l => Read(l, 0)).ToList();
        if (!doc.DrawingLayers().Any()) throw new InvalidDataException("No layers to draw on");
        return doc;
    }
    static string Pack(PixelImage image)
    {
        using var output = new MemoryStream();
        using (var z = new ZLibStream(output, CompressionLevel.Optimal, true)) z.Write(MemoryMarshal.AsBytes(image.Pixels.AsSpan()));
        return Convert.ToBase64String(output.ToArray());
    }
    static PixelImage Unpack(string text, int width, int height)
    {
        var image = new PixelImage(width, height); var target = MemoryMarshal.AsBytes(image.Pixels.AsSpan());
        using var z = new ZLibStream(new MemoryStream(Convert.FromBase64String(text)), CompressionMode.Decompress);
        int read = 0; while (read < target.Length) { int n = z.Read(target[read..]); if (n == 0) break; read += n; }
        if (read != target.Length || z.ReadByte() != -1) throw new InvalidDataException("A layer picture is the wrong size");
        return image;
    }
    /// <summary>True when these layers still produce <paramref name="sheet"/>; if the PNG was changed some other
    /// way (replaced, edited elsewhere) the layers are out of date and the PNG is opened flat instead.</summary>
    public bool Produces(PixelImage sheet)
    {
        var packed = PixelArt.Pack(ComposeAll());
        if (packed.Width != sheet.Width || packed.Height != sheet.Height) return false;
        for (int i = 0; i < packed.Pixels.Length; i++)
        {
            uint a = packed.Pixels[i], b = sheet.Pixels[i];
            if (a != b && !(a >> 24 == 0 && b >> 24 == 0)) return false;
        }
        return true;
    }
}
