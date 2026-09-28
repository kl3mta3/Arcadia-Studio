using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Wysicraft.Core;
using Wysicraft.Models;
namespace Wysicraft.Packaging;

/// <summary>Exports a project as a web app: index.html, the web runtime (wysicraft-web.js), the screens and scripts
/// (wysicraft/project.js) and the images. Runs in any browser, and is what the Windows and Electron apps wrap.</summary>
public static class WebExport
{
    public static string RuntimeScript { get; } = ReadRuntime();
    static string ReadRuntime()
    {
        using var stream = typeof(WebExport).Assembly.GetManifestResourceStream("wysicraft-web.js") ?? throw new InvalidOperationException("Web runtime missing from this build");
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }

    /// <summary>What won't work the same outside Minecraft. Shown before exporting; none of it stops the export.</summary>
    public static List<string> Warnings(Project project)
    {
        var warnings = new List<string>(); var screens = project.Screens.Where(s => !s.IsComponent).ToList();
        IEnumerable<Element> All(IEnumerable<Element> elements) => elements.SelectMany(e => new[] { e }.Concat(All(e.RowElements)));
        var elements = screens.SelectMany(s => All(s.Elements)).ToList();
        var handlers = screens.SelectMany(s => s.Events.Values.Concat(s.Elements.SelectMany(e => e.Events.Values))).ToList();
        var minecraftTextures = elements.Select(e => e.Texture).Concat(handlers.SelectMany(h => h.Client.Actions.Concat(h.Server.Actions)).Where(a => a.Type == "change_texture").Select(a => a.Value))
            .Where(t => t.Length > 0 && !t.StartsWith(project.Manifest.Id + ":") && !TextureAssets.TryGet(project, t, out _)).Distinct().ToList();
        if (minecraftTextures.Count > 0) warnings.Add($"{minecraftTextures.Count} Minecraft texture(s) can't be included (they belong to Mojang), so they show nothing: {string.Join(", ", minecraftTextures.Take(4))}{(minecraftTextures.Count > 4 ? ", …" : "")}. Import your own PNGs instead.");
        if (elements.Any(e => e.Type is "item" or "item_list")) warnings.Add("Item icons show a placeholder. To show real icons, add images named like the item under textures/item (for example assets/<namespace>/textures/item/diamond.png).");
        var server = handlers.SelectMany(h => h.Server.Actions).Where(a => a.Type is "command" or "server_function" or "player_inventory").Select(a => a.Type).Distinct().ToList();
        if (server.Count > 0) warnings.Add($"Server actions ({string.Join(", ", server)}) have no Minecraft to run in: they are passed to the hooks in host.js (onCommand, onServerFunction), where your own code can handle them.");
        if (handlers.Any(h => h.Server.Script.Length > 0 && h.Server.ScriptEngine == "kubejs")) warnings.Add("KubeJS scripts don't run outside Minecraft and are skipped.");
        if (handlers.Any(h => h.Client.Actions.Concat(h.Server.Actions).Any(a => a.Type == "play_sound"))) warnings.Add("Sounds are Minecraft's: play_sound is passed to onSound in host.js.");
        if (elements.Any(e => e.Font is "minecraft:default" or "minecraft:uniform")) warnings.Add("Minecraft's font isn't included; text uses the system font, so it may be slightly wider or narrower.");
        return warnings;
    }

    /// <summary>ExtraAssets: more files by pack path (Preview adds the player's own Minecraft textures; never used for exports).
    /// HostScript replaces the generated host.js. Screen is the screen to start on.</summary>
    public sealed record Options(bool SingleFile = false, string Background = "#15181D", bool Desktop = false, IReadOnlyDictionary<string, byte[]>? ExtraAssets = null, string? HostScript = null, string? Screen = null);

    /// <summary>All files of the export, by relative path. host.js is included: callers that re-export into the same
    /// folder keep the author's edited copy (see <see cref="ExportFolder"/>).</summary>
    public static Dictionary<string, byte[]> Files(Project project, Options? options = null)
    {
        options ??= new();
        var errors = Validation.Errors(project); if (errors.Count != 0) throw new InvalidDataException(string.Join("\n", errors));
        var pack = ProjectStore.Files(project, true);
        var manifest = project.Manifest;
        var screens = new Dictionary<string, JsonElement>(); var scripts = new Dictionary<string, string>();
        var assets = new Dictionary<string, string>(); var animations = new Dictionary<string, string>(); var sounds = new Dictionary<string, string>(); var files = new Dictionary<string, byte[]>();
        var fonts = new Dictionary<string, string>();
        var all = new Dictionary<string, byte[]>(pack); if (options.ExtraAssets != null) foreach (var (path, bytes) in options.ExtraAssets) all.TryAdd(path, bytes);
        foreach (var (path, bytes) in all)
        {
            if (path.StartsWith("ui/")) screens[path[3..^5]] = JsonDocument.Parse(bytes).RootElement.Clone();
            else if (path.StartsWith("scripts/")) scripts[path] = Encoding.UTF8.GetString(bytes);
            else if (path.StartsWith("assets/") && path.EndsWith(".png.mcmeta")) animations[path[..^7]] = Encoding.UTF8.GetString(bytes);
            else if (SoundAssets.IsSound(path))
            {
                string mime = Path.GetExtension(path).ToLowerInvariant() switch { ".mp3" => "audio/mpeg", ".wav" => "audio/wav", ".m4a" or ".aac" => "audio/mp4", _ => "audio/ogg" };
                if (options.SingleFile) sounds[SoundAssets.Resource(path)] = $"data:{mime};base64," + Convert.ToBase64String(bytes);
                else { sounds[SoundAssets.Resource(path)] = string.Join("/", path.Split('/').Select(Uri.EscapeDataString)); files[path] = bytes; }
            }
            else if (Fonts.Extensions.Any(x => path.EndsWith(x, StringComparison.OrdinalIgnoreCase)) && path.Contains("/" + Fonts.Folder))
            {
                // Fonts travel like sounds: inline in a single-file export, beside the page otherwise. The runtime
                // registers each one with the FontFace API under the name Fonts.FamilyName gives it.
                string mime = Path.GetExtension(path).ToLowerInvariant() switch { ".woff2" => "font/woff2", ".woff" => "font/woff", ".otf" => "font/otf", _ => "font/ttf" };
                string resource = Fonts.Resource(manifest.Id, path);
                if (options.SingleFile) fonts[resource] = $"data:{mime};base64," + Convert.ToBase64String(bytes);
                else { fonts[resource] = string.Join("/", path.Split('/').Select(Uri.EscapeDataString)); files[path] = bytes; }
            }
            else if (path.StartsWith("assets/") && path.EndsWith(".png"))
            {
                if (options.SingleFile) assets[path] = "data:image/png;base64," + Convert.ToBase64String(bytes);
                else { assets[path] = string.Join("/", path.Split('/').Select(Uri.EscapeDataString)); files[path] = bytes; }
            }
        }
        // Inputs use the same camelCase names as the screens; limits follow what the project is made for.
        var inputs = JsonDocument.Parse(Json.Write(manifest.Inputs)).RootElement.Clone();
        var particles = JsonDocument.Parse(Json.Write(manifest.Particles)).RootElement.Clone();
        var collisionMatrix = JsonDocument.Parse(Json.Write(manifest.CollisionMatrix)).RootElement.Clone();
        var shaders = manifest.Shaders.ToDictionary(s => s.Id, s => s.Source);
        var limits = new { scriptOps = manifest.Target == "web" ? Limits.Web.ScriptOps : Limits.Minecraft.ScriptOps, tickMin = manifest.Target == "minecraft" ? Limits.Minecraft.MinTick : Limits.Web.MinTick };
        var data = new { id = manifest.Id, name = manifest.Name, version = manifest.Version, main = manifest.DefaultUi, target = manifest.Target, screens, scripts, assets, animations, sounds, inputs, particles, shaders, fonts, collisionMatrix, limits };
        // JSON inside a <script>: escape "</" so a string can never end the script block.
        string projectJs = "window.WYSICRAFT_PROJECT = " + JsonSerializer.Serialize(data).Replace("</", "<\\/") + ";\n";
        string hostJs = options.HostScript ?? HostTemplate(options.Desktop);
        string title = WebUtility.HtmlEncode(manifest.Name.Length > 0 ? manifest.Name : manifest.Id);
        string background = Regex.IsMatch(options.Background, "^#[0-9a-fA-F]{6}$") ? options.Background : "#15181D";
        string head = $"<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n<title>{title}</title>\n<style>html,body{{margin:0;height:100%;overflow:hidden;background:{background}}}#app{{position:fixed;inset:0}}.wysicraft-canvas{{display:block;outline:none}}</style>\n</head>\n<body>\n<div id=\"app\"></div>\n";
        string startOptions = JsonSerializer.Serialize(new { screen = options.Screen }).Replace("</", "<\\/");
        string start = "<script>Wysicraft.start(document.getElementById('app'), null, null, " + startOptions + ");</script>\n</body>\n</html>\n";
        if (options.SingleFile)
        {
            string Inline(string code) => "<script>\n" + code.Replace("</script", "<\\/script") + "\n</script>\n";
            files["index.html"] = Encoding.UTF8.GetBytes(head + Inline(projectJs) + Inline(hostJs) + Inline(RuntimeScript) + start);
        }
        else
        {
            files["index.html"] = Encoding.UTF8.GetBytes(head + "<script src=\"wysicraft/project.js\"></script>\n<script src=\"host.js\"></script>\n<script src=\"wysicraft/wysicraft-web.js\"></script>\n" + start);
            files["wysicraft/project.js"] = Encoding.UTF8.GetBytes(projectJs);
            files["wysicraft/wysicraft-web.js"] = Encoding.UTF8.GetBytes(RuntimeScript);
            files["host.js"] = Encoding.UTF8.GetBytes(hostJs);
        }
        return files;
    }

    /// <summary>Writes the export into a folder. An existing host.js is kept, so your hooks survive re-exporting.</summary>
    public static void ExportFolder(Project project, string folder, Options? options = null)
    {
        var files = Files(project, options);
        Directory.CreateDirectory(folder);
        // Replace what earlier exports wrote, never anything else in the folder.
        foreach (var old in new[] { "wysicraft", "assets" }) { var path = Path.Combine(folder, old); if (Directory.Exists(path)) Directory.Delete(path, true); }
        foreach (var (name, bytes) in files)
        {
            var path = Path.GetFullPath(Path.Combine(folder, name));
            if (!path.StartsWith(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid export path " + name);
            if (name == "host.js" && File.Exists(path)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); Distribution.Write(path, bytes);
        }
    }
    public static void ExportSingleFile(Project project, string path, Options? options = null) =>
        Distribution.Write(path, Files(project, (options ?? new()) with { SingleFile = true })["index.html"]);

    /// <summary>Suggested window size for desktop apps: the design at a whole GUI scale that fits a typical screen.</summary>
    public static (int Width, int Height) WindowSize(Project project)
    {
        var main = project.Screens.FirstOrDefault(s => s.Id == project.Manifest.DefaultUi && !s.IsComponent) ?? project.Screens.First(s => !s.IsComponent);
        int w = main.Size.Width + 12, h = main.Size.Height + (main.ShowFrame ? 32 : 12);
        int scale = Math.Clamp((int)Math.Floor(Math.Min(1500.0 / w, 900.0 / h)), 1, 4);
        return (Math.Max(320, w * scale), Math.Max(240, h * scale));
    }

    static string HostTemplate(bool desktop) => """
// host.js: connect your Wysicraft screens to your own code. Edit freely; re-exporting keeps this file.
// Every hook is optional. "info" has { screen, element, event, value, state, app }, and "app" (also
// window.Wysicraft.app once started) can change the UI: open(id), close(), setText(id, text),
// setValue(id, value), setVisible(id, bool), setEnabled(id, bool), getVariable(name), setVariable(name, value),
// message(text) and fire(elementId, eventName, value).
window.wysicraftHost = {
  // A Server "command" action, or ctx.server.runCommand(...) in a server script.
  onCommand(command, info) { console.log('Command:', command, info); },
  // A Server "server_function" action.
  onServerFunction(name, value, info) { console.log('Server function:', name, value, info); },
  // Any other server action Minecraft would handle (for example player_inventory).
  onServerAction(action, info) { console.log('Server action:', action, info); },
  // "play_sound" actions. Return nothing; play your own audio here if you like.
  onSound(sound) { },
  // Messages from "message" actions and ctx.message(). Return false to hide the built-in message line.
  onMessage(text) { },
""" + (desktop ? """
  // A close_ui action or ctx.ui.close(): closes the app window.
  onClose(screen) { window.close(); },
""" : """
  // A close_ui action or ctx.ui.close(). Without this hook the page shows "Screen closed. Click to open it again."
  // onClose(screen) { },
""") + """
  // Escape closes the screen, as in Minecraft.
  closeOnEscape: false,
  // Player details that server scripts see through ctx.player.
  player: { name: 'Player', uuid: '00000000-0000-0000-0000-000000000000', position: { x: 0, y: 0, z: 0, dimension: 'app' }, inventory: [], permission: 0 },
  // Force a GUI scale (1, 2, 3…) instead of the largest that fits the window.
  guiScale: 0,
  // Text font (Minecraft's font isn't included).
  fontFamily: '"Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif'
};
""";
}
