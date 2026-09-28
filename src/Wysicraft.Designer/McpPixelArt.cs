using System.ComponentModel;
using System.IO;
using ModelContextProtocol.Server;
using Wysicraft.Core;
using Wysicraft.Models;
using Wysicraft.Packaging;
namespace Wysicraft.Designer;

/// <summary>A sprite clip for the sprite_sheet MCP tool.</summary>
public sealed class SpriteClipSpec
{
    [Description("Letters, numbers and _, starting with a letter or _.")]
    public string Name { get; set; } = "";
    [Description("Frame numbers in playing order (0-based, left to right then top to bottom on the sheet).")]
    public List<int> Frames { get; set; } = [];
    [Description("Frames per second, 0.1-120.")]
    public double Fps { get; set; } = 8;
    [Description("Stop on the last frame instead of looping.")]
    public bool Once { get; set; }
}

// Pixel art and sprite sheets for AI assistants: the same drawing, layers, saving and clip rules as the pixel editor
// and sprite sheet editor, without their windows (MCP calls are one-off requests).
public partial class MainWindow
{
    int artEditorsOpen; // while the sprite sheet editor (or a pixel editor opened from it) is open, MCP edits wait (they'd be overwritten)

    // An image named by asset path, texture resource or file name. Null for textures that aren't in the project.
    string? ImageAssetPath(string image)
    {
        if (project.Assets.ContainsKey(image) && image.EndsWith(".png")) return image;
        if (image.Contains(':')) return TextureAssets.PathOf(project, image);
        string file = image.EndsWith(".png") ? image : image + ".png";
        var matches = project.Assets.Keys.Where(k => k.EndsWith("/" + file) && k.Contains("/textures/")).ToList();
        if (matches.Count > 1) throw new InvalidDataException($"Several images are called {file}: {string.Join(", ", matches)}. Use the full path.");
        return matches.FirstOrDefault();
    }
    // The picture to work on: the layers saved with it when they still match the PNG, otherwise the PNG itself
    // (cut into frames when a frame size is given). Also says which it used.
    (PixelDocument Doc, bool FromLayers) LoadPixelArt(string image, int frameWidth, int frameHeight, out string? path)
    {
        path = ImageAssetPath(image);
        byte[] png = path != null ? project.Assets[path] : TryTexture(image, out var found) ? found : throw new InvalidDataException($"There's no image \"{image}\" in the project (or in the loaded Minecraft textures).");
        var sheet = DecodePixels(png);
        if (path != null && project.Assets.TryGetValue(path + TextureAssets.LayersSuffix, out var layers))
            try { var saved = PixelDocument.Load(layers); if (saved.Produces(sheet)) return (saved, true); } catch (InvalidDataException) { }
        // An animated image (a .png.mcmeta beside it) knows its own frame size.
        if (frameWidth == 0 && path != null && project.Assets.TryGetValue(path + ".mcmeta", out var meta)
            && TextureAnimation.Parse(System.Text.Encoding.UTF8.GetString(meta), sheet.Width, sheet.Height) is { } animation && (animation.Columns > 1 || animation.Rows > 1))
            (frameWidth, frameHeight) = (animation.FrameWidth, animation.FrameHeight);
        if (frameWidth > 0 && frameHeight > 0)
        {
            if (sheet.Width % frameWidth != 0 || sheet.Height % frameHeight != 0) throw new InvalidDataException($"The {sheet.Width}×{sheet.Height} image doesn't divide into {frameWidth}×{frameHeight} frames.");
            if (frameWidth > PixelArt.MaxSize || frameHeight > PixelArt.MaxSize) throw new InvalidDataException($"Frames are at most {PixelArt.MaxSize}×{PixelArt.MaxSize}.");
            return (PixelDocument.FromFrames(PixelArt.Unpack(sheet, frameWidth, frameHeight)), false);
        }
        if (sheet.Width > PixelArt.MaxSize || sheet.Height > PixelArt.MaxSize) throw new InvalidDataException($"The image is {sheet.Width}×{sheet.Height}; give frameWidth and frameHeight to work on it as frames of at most {PixelArt.MaxSize}×{PixelArt.MaxSize}.");
        return (PixelDocument.FromFrames([sheet]), false);
    }
    // An enlarged copy (whole-number steps, so pixels stay square) an assistant can open to look at.
    static string PixelPreview(PixelImage image)
    {
        int scale = Math.Clamp(512 / Math.Max(image.Width, image.Height), 1, 32);
        var big = new PixelImage(Math.Min(PixelArt.MaxSheetSize, image.Width * scale), Math.Min(PixelArt.MaxSheetSize, image.Height * scale));
        for (int y = 0; y < big.Height; y++) for (int x = 0; x < big.Width; x++) big.Pixels[y * big.Width + x] = image.Get(x / scale, y / scale);
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wysicraft", "McpCaptures"); Directory.CreateDirectory(root);
        string file = Path.Combine(root, "pixels-" + Guid.NewGuid().ToString("N") + ".png"); File.WriteAllBytes(file, EncodePixels(big)); return file;
    }
    static object PixelArtInfo(PixelDocument doc) => new
    {
        width = doc.Width, height = doc.Height, frames = doc.FrameCount,
        layers = doc.Rows().Select(r => new { name = r.Layer.Name, group = r.Layer.IsGroup, depth = r.Depth, visible = r.Layer.Visible, locked = r.Layer.Locked, opacity = r.Layer.Opacity })
    };
    static object? PixelGrid(PixelImage image) => PixelCommands.Describe(image) is var (rows, palette) ? new { rows, palette, note = "One character per pixel; '.' is transparent." } : null;

    internal Task<string> McpPixelArt(string expected, List<PixelCommand> commands, string image, string newName, int width, int height, int frames, int frameWidth, int frameHeight, double animateFps, bool replace, CancellationToken cancellationToken) => Dispatcher.InvokeAsync(() =>
    {
        try
        {
            if (mcpHost == null) throw new InvalidOperationException("MCP server is stopped.");
            CheckRevision(expected);
            PixelDocument doc; string? path = null; string name; bool fromLayers = false;
            if (image.Length > 0)
            {
                (doc, fromLayers) = LoadPixelArt(image, frameWidth, frameHeight, out path);
                if (path == null && newName.Length == 0) throw new InvalidDataException("That's a Minecraft texture, not a project image. Give newName to save your own copy.");
                name = newName.Length > 0 ? newName : Path.GetFileNameWithoutExtension(path!);
            }
            else
            {
                if (width < 1 || height < 1) throw new InvalidDataException($"For a new image give width and height (1–{PixelArt.MaxSize}), and newName.");
                if (newName.Length == 0) throw new InvalidDataException("Give newName for the new image.");
                string taken = TextureAssets.Path(project.Manifest.Id, PixelEditor.SafeName(newName) + ".png");
                if (!replace && project.Assets.ContainsKey(taken))
                {
                    var had = ProjectStore.TextureSize(project.Assets[taken]);
                    throw new InvalidDataException($"There is already an image called {PixelEditor.SafeName(newName)} ({had.Width}x{had.Height}). " +
                        "Editing it keeps its size and frames, which is rarely what a new drawing wants: pass image to edit it on purpose, " +
                        "replace:true to draw over it at the new size, or choose another name.");
                }
                doc = new PixelDocument(width, height, Math.Clamp(frames, 1, PixelArt.MaxFrames)); doc.Layers.Add(doc.NewLayer("Layer 1")); name = newName;
            }
            PixelCommands.Apply(doc, commands ?? []);
            bool plain = doc.FrameCount == 1 && doc.Layers is [{ IsGroup: false, Visible: true, Locked: false, Opacity: 1 }];
            var sheet = PixelArt.Pack(doc.ComposeAll());
            string saved = SavePixelArt(new PixelEditor.SaveRequest(sheet, doc.Width, doc.Height, doc.FrameCount, PixelEditor.SafeName(name), path == null || newName.Length > 0, path, plain ? null : doc.Save(), animateFps > 0 && doc.FrameCount > 1 ? Math.Clamp(animateFps, 0.5, 20) : null), null);
            return Json.Write(new
            {
                revision = Revision(), image = saved, texture = AssetResource(saved), sheet = new { width = sheet.Width, height = sheet.Height }, frameWidth = doc.Width, frameHeight = doc.Height,
                picture = PixelArtInfo(doc), startedFrom = image.Length == 0 ? "new" : fromLayers ? "saved layers" : "the PNG (one layer)",
                preview = PixelPreview(sheet), frame0 = PixelGrid(doc.Compose(0)),
                note = doc.FrameCount > 1 ? (animateFps > 0 ? "Saved as an animated image: it plays by itself wherever it is used (e.g. an image control)." : "The frames are saved side by side as a sprite sheet; use sprite_sheet with frameWidth/frameHeight to play them (or pass animateFps to make an animated image that plays by itself).") : "Saved as one Undo step."
            });
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException) { throw new ModelContextProtocol.McpException(ex.Message); }
    }, System.Windows.Threading.DispatcherPriority.Normal, cancellationToken).Task;

    internal Task<string> McpReadPixelArt(string image, int frame, string layer, int frameWidth, int frameHeight, CancellationToken cancellationToken) => Dispatcher.InvokeAsync(() =>
    {
        try
        {
            if (mcpHost == null) throw new InvalidOperationException("MCP server is stopped.");
            var (doc, fromLayers) = LoadPixelArt(image, frameWidth, frameHeight, out var path);
            if (frame < 0 || frame >= doc.FrameCount) throw new InvalidDataException($"Frame {frame} doesn't exist (0–{doc.FrameCount - 1}).");
            PixelImage shown;
            if (layer.Length == 0) shown = doc.Compose(frame);
            else
            {
                var target = doc.All().FirstOrDefault(l => l.Name == layer) ?? throw new InvalidDataException($"There's no layer or group called \"{layer}\".");
                if (target.IsGroup) { var only = new PixelDocument(doc.Width, doc.Height, doc.FrameCount); only.Layers.Add(target); shown = only.Compose(frame); } else shown = target.Cells[frame];
            }
            return Json.Write(new
            {
                image = path ?? image, texture = path != null ? AssetResource(path) : image, inProject = path != null, picture = PixelArtInfo(doc),
                layersFrom = fromLayers ? "saved layers" : "the PNG (one layer)", frame, layer = layer.Length > 0 ? layer : "(all visible layers)",
                preview = PixelPreview(shown), grid = PixelGrid(shown) ?? (object)"Too large or too many colors to write as text; open the preview PNG."
            });
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException) { throw new ModelContextProtocol.McpException(ex.Message); }
    }, System.Windows.Threading.DispatcherPriority.Normal, cancellationToken).Task;

    // ---- Sprite sheets ----
    Element SpriteElement(string screen, string element)
    {
        var s = project.Screens.FirstOrDefault(x => x.Id == screen) ?? throw new InvalidDataException("No screen " + screen);
        var e = s.Elements.FirstOrDefault(x => x.Id == element) ?? throw new InvalidDataException($"No control {element} on {screen}");
        return e.Type == "sprite" ? e : throw new InvalidDataException($"{element} is a {e.Type}, not a sprite.");
    }
    object SheetInfo(string texture, int frameWidth, int frameHeight, string clips, string playing)
    {
        if (texture.Length == 0 || !TryTexture(texture, out var bytes)) return new { texture, found = false, frameWidth, frameHeight, clips, playing };
        var size = ProjectStore.TextureSize(bytes); int columns = size.Width / Math.Max(1, frameWidth), rows = size.Height / Math.Max(1, frameHeight);
        object parsed; try { parsed = SpriteClips.Parse(clips).Values.Select(c => new { name = c.Name, frames = c.Frames, fps = c.Fps, once = !c.Loop }); } catch (FormatException ex) { parsed = "Can't read the clips: " + ex.Message; }
        return new { texture, found = true, sheet = new { width = size.Width, height = size.Height }, frameWidth, frameHeight, columns, rows, frameCount = columns * rows, clips, parsedClips = parsed, playing };
    }
    internal Task<string> McpReadSpriteSheet(string screen, string element, CancellationToken cancellationToken) => Dispatcher.InvokeAsync(() =>
    {
        try
        {
            if (mcpHost == null) throw new InvalidOperationException("MCP server is stopped.");
            var e = SpriteElement(screen, element);
            return Json.Write(SheetInfo(e.Texture, e.FrameWidth, e.FrameHeight, e.Clips, e.Value));
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException) { throw new ModelContextProtocol.McpException(ex.Message); }
    }, System.Windows.Threading.DispatcherPriority.Normal, cancellationToken).Task;

    internal Task<string> McpSpriteSheet(string expected, string screen, string element, List<SpriteClipSpec>? clips, int frameWidth, int frameHeight, string texture, string? playing, CancellationToken cancellationToken) => Dispatcher.InvokeAsync(() =>
    {
        try
        {
            if (mcpHost == null) throw new InvalidOperationException("MCP server is stopped.");
            CheckRevision(expected);
            var e = SpriteElement(screen, element);
            string newTexture = e.Texture;
            if (texture.Length > 0) newTexture = ImageAssetPath(texture) is string p ? AssetResource(p) : TryTexture(texture, out _) ? texture : throw new InvalidDataException($"There's no image \"{texture}\".");
            int fw = frameWidth > 0 ? frameWidth : e.FrameWidth, fh = frameHeight > 0 ? frameHeight : e.FrameHeight;
            if (fw < 1 || fh < 1 || fw > 4096 || fh > 4096) throw new InvalidDataException("Frame width and height are 1–4096.");
            if (!TryTexture(newTexture, out var bytes)) throw new InvalidDataException("The sprite has no sheet yet. Give texture (an image made with pixel_art, or imported).");
            var size = ProjectStore.TextureSize(bytes); int count = size.Width / fw * (size.Height / fh);
            if (count == 0) throw new InvalidDataException($"{fw}×{fh} frames don't fit on the {size.Width}×{size.Height} sheet.");
            string text = e.Clips;
            if (clips != null)
            {
                foreach (var c in clips)
                {
                    if (!System.Text.RegularExpressions.Regex.IsMatch(c.Name, "^[A-Za-z_][A-Za-z0-9_]{0,63}$")) throw new InvalidDataException($"Clip name \"{c.Name}\": letters, numbers and _, starting with a letter.");
                    if (c.Frames.Count == 0) throw new InvalidDataException($"Clip {c.Name} has no frames.");
                    if (c.Frames.FirstOrDefault(f => f < 0 || f >= count) is var bad && c.Frames.Any(f => f < 0 || f >= count)) throw new InvalidDataException($"Clip {c.Name}: frame {bad} isn't on the sheet (it has frames 0–{count - 1}).");
                    if (!double.IsFinite(c.Fps) || c.Fps is < 0.1 or > 120) throw new InvalidDataException($"Clip {c.Name}: fps is 0.1–120.");
                }
                if (clips.GroupBy(c => c.Name).FirstOrDefault(g => g.Count() > 1) is { } twice) throw new InvalidDataException("Two clips are called " + twice.Key);
                text = SpriteClips.Format(clips.Select(c => new SpriteClips.Clip(c.Name, [.. c.Frames], Math.Round(c.Fps, 2), !c.Once)));
            }
            Dictionary<string, SpriteClips.Clip> parsed;
            try { parsed = SpriteClips.Parse(text); } catch (FormatException ex) { throw new InvalidDataException("The clips can't be read: " + ex.Message); }
            string value = playing ?? e.Value;
            if (playing != null && playing.Length > 0 && !parsed.ContainsKey(playing)) throw new InvalidDataException($"There's no clip called {playing}.");
            if (!parsed.ContainsKey(value)) value = ""; // "" plays the first clip
            Change(); e.Texture = newTexture; e.FrameWidth = fw; e.FrameHeight = fh; e.Clips = text; e.Value = value;
            Draw(); RefreshInspector(); Log($"MCP updated {element}'s sprite sheet and clips (one Undo).");
            return Json.Write(new { revision = Revision(), sprite = SheetInfo(newTexture, fw, fh, text, value) });
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException) { throw new ModelContextProtocol.McpException(ex.Message); }
    }, System.Windows.Threading.DispatcherPriority.Normal, cancellationToken).Task;
}

public sealed partial class DesignerMcpTools
{
    [McpServerTool(Name = "pixel_art"), Description("Draw pixel art (with transparency, layers, groups and frames) and save it as a project image, like the pixel editor. Edit an existing image (image = asset path, texture ID or file name; its saved layers are used when they still match) or make a new one (width, height, frames, newName). commands run in order; each has op plus fields: grid (rows of characters + palette of one-character keys to colors, drawn from x,y; best for drawing), pixels (points [[x,y]], color), line/rect/ellipse (x,y,x2,y2, color, filled, size), fill (x,y, color, tolerance 0-255), clear (optional x,y,width,height), flip (direction horizontal|vertical), shift (dx,dy; a group moves every layer in it), add_layer / add_group (name, parent group), set_layer (layer, name, opacity 0-1, visible, locked), move_layer (layer, direction up|down|into|out), merge_down, delete_layer, add_frame (at, copyOf), delete_frame (frame), move_frame (frame, at), resize (width, height). Drawing uses layer (name; empty = top layer) and frame (0-based); colors are #RRGGBB, #AARRGGBB or transparent; blend:true mixes see-through colors. Frames are saved side by side as a sprite sheet (for sprite_sheet), or with animateFps > 0 as an animated image that plays by itself in any image control. One Undo step. A newName that already exists is refused (it would otherwise be edited at its old size and frame count); pass replace:true to draw over it at the new size. Returns the texture ID, layers, an enlarged preview PNG path and frame 0 as a text grid.")]
    public Task<string> DrawPixelArt(string expectedRevision, List<PixelCommand> commands, string image = "", string newName = "", int width = 0, int height = 0, int frames = 1, int frameWidth = 0, int frameHeight = 0, double animateFps = 0, bool replace = false, CancellationToken cancellationToken = default)
        => editor.McpPixelArt(expectedRevision, commands, image, newName, width, height, frames, frameWidth, frameHeight, animateFps, replace, cancellationToken);
    [McpServerTool(Name = "read_pixel_art", ReadOnly = true), Description("Look at an image: its size, frames, layers and groups, one frame (all visible layers, or one layer or group) as a text grid (one character per pixel, '.' = transparent, with its palette) and an enlarged preview PNG path. image = asset path, texture ID (Minecraft textures too) or file name. For a sprite sheet without saved layers give frameWidth and frameHeight.")]
    public Task<string> ReadPixelArt(string image, int frame = 0, string layer = "", int frameWidth = 0, int frameHeight = 0, CancellationToken cancellationToken = default)
        => editor.McpReadPixelArt(image, frame, layer, frameWidth, frameHeight, cancellationToken);
    [McpServerTool(Name = "sprite_sheet"), Description("Set up a Sprite control's animation, like the sprite sheet editor: texture (optional: the sheet image), frameWidth/frameHeight (optional), clips (optional: replaces every clip; each has name, frames in order, fps, once) and playing (optional: the clip shown when the screen opens; empty = first clip). Checks every frame is on the sheet. One Undo step. Scripts switch clips with ui.play(id, clip).")]
    public Task<string> SpriteSheet(string expectedRevision, string screen, string element, List<SpriteClipSpec>? clips = null, int frameWidth = 0, int frameHeight = 0, string texture = "", string? playing = null, CancellationToken cancellationToken = default)
        => editor.McpSpriteSheet(expectedRevision, screen, element, clips, frameWidth, frameHeight, texture, playing, cancellationToken);
    [McpServerTool(Name = "read_sprite_sheet", ReadOnly = true), Description("Read a Sprite control's sheet: image size, frame size, columns, rows, how many frames it has, its clips and which one plays first.")]
    public Task<string> ReadSpriteSheet(string screen, string element, CancellationToken cancellationToken = default)
        => editor.McpReadSpriteSheet(screen, element, cancellationToken);
}
