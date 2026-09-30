using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Wysicraft.Core;
using Wysicraft.Models;
namespace Wysicraft.Packaging;

/// <summary>Leaderboard pages for Arcadia. A leaderboard is designed on the canvas (Project.Leaderboards), saved as a
/// .lb file (JSON: the board, and in a file of its own its pictures and fonts too), and turned into leaderboard.html
/// (plus leaderboard/… pictures) only when the game is packed for Arcadia or the page is exported. The page reads the
/// arcade's board with Arcadia.board and shows sample data anywhere else. The design travels inside the page, so an
/// exported page can be opened in the editor again.</summary>
public static class Leaderboards
{
    public const string Extension = ".lb", Format = "arcadia-leaderboard", PageName = "leaderboard.html", Folder = "leaderboard/";
    const string BlockId = "arcadia-studio-leaderboard";
    /// <summary>The widgets a leaderboard page can hold, besides labels, pictures, panels and shapes.</summary>
    public static readonly string[] Widgets = LeaderboardPages.Widgets;
    /// <summary>Every control type a leaderboard page draws.</summary>
    public static readonly HashSet<string> Types = LeaderboardPages.Types;
    public static readonly string[] Icons = ["crown", "medal", "trophy", "star"];
    /// <summary>The rank icons' outlines (24 × 24 SVG path data), shared with the page script and the editor.</summary>
    public static readonly Dictionary<string, string> IconPaths = new()
    {
        ["crown"] = "M3 8 L7.5 12 L12 5 L16.5 12 L21 8 L19 18 L5 18 Z M5 19.5 H19 V21 H5 Z",
        ["medal"] = "M7 2 H10.5 L12 6 L13.5 2 H17 L14.2 8.4 A7 7 0 1 1 9.8 8.4 Z M12 11 A4.6 4.6 0 1 0 12.01 11 Z",
        ["trophy"] = "M6 3 H18 V5 H21 V8 A4 4 0 0 1 17.4 12 A6 6 0 0 1 13 15.8 V18 H16 V21 H8 V18 H11 V15.8 A6 6 0 0 1 6.6 12 A4 4 0 0 1 3 8 V5 H6 Z M5 7 V8 A2 2 0 0 0 6.2 9.8 A10 10 0 0 1 6 7 Z M19 7 H18 A10 10 0 0 1 17.8 9.8 A2 2 0 0 0 19 8 Z",
        ["star"] = "M12 2 L14.9 8.6 L22 9.3 L16.6 14 L18.2 21 L12 17.3 L5.8 21 L7.4 14 L2 9.3 L9.1 8.6 Z"
    };
    public static readonly Dictionary<int, string> Tiers = new() { [1] = "#F4C744", [2] = "#C9D1D9", [3] = "#D08A4E" };

    /// <summary>The page script (src/Wysicraft.Web/leaderboard-page.js).</summary>
    public static string PageScript { get; } = ReadScript();
    static string ReadScript()
    {
        using var stream = typeof(Leaderboards).Assembly.GetManifestResourceStream("leaderboard-page.js") ?? throw new InvalidOperationException("The leaderboard page script is missing from this build.");
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }

    // ---- .lb files ----
    /// <summary>A .lb file: the board, and (for a file of its own) the pictures and fonts it uses, by resource ID.</summary>
    public static byte[] Write(UiDefinition board, Dictionary<string, byte[]>? images = null, Dictionary<string, byte[]>? fonts = null)
    {
        var node = new JsonObject { ["format"] = Format, ["version"] = 1, ["board"] = JsonNode.Parse(Json.Write(board)) };
        if (images is { Count: > 0 }) node["images"] = new JsonObject(images.Select(i => KeyValuePair.Create(i.Key, (JsonNode?)JsonValue.Create(Convert.ToBase64String(i.Value)))));
        if (fonts is { Count: > 0 }) node["fonts"] = new JsonObject(fonts.Select(i => KeyValuePair.Create(i.Key, (JsonNode?)JsonValue.Create(Convert.ToBase64String(i.Value)))));
        return Encoding.UTF8.GetBytes(node.ToJsonString(Json.Options));
    }
    public sealed record LbFile(UiDefinition Board, Dictionary<string, byte[]> Images, Dictionary<string, byte[]> Fonts);
    public static LbFile Read(byte[] bytes)
    {
        JsonObject node;
        try { node = JsonNode.Parse(bytes)!.AsObject(); } catch (Exception ex) when (ex is JsonException or InvalidOperationException) { throw new InvalidDataException("That isn't a leaderboard file (.lb)."); }
        if ((string?)node["format"] != Format || node["board"] is not JsonObject boardNode) throw new InvalidDataException("That isn't a leaderboard file (.lb).");
        var board = Json.Read<UiDefinition>(boardNode.ToJsonString());
        board.IsLeaderboard = true; board.IsComponent = false;
        Dictionary<string, byte[]> Blobs(string key) => node[key] is JsonObject o ? o.Where(p => p.Value != null).ToDictionary(p => p.Key, p => Convert.FromBase64String((string)p.Value!)) : [];
        return new(board, Blobs("images"), Blobs("fonts"));
    }
    /// <summary>A .lb file of its own, carrying every picture and font the board uses.</summary>
    public static byte[] Standalone(Project project, UiDefinition board)
    {
        var (images, fonts) = Files(project, board);
        return Write(board, images, fonts);
    }
    /// <summary>The project's pictures and fonts a board uses, by resource ID (Minecraft's own pictures can't travel).</summary>
    public static (Dictionary<string, byte[]> Images, Dictionary<string, byte[]> Fonts) Files(Project project, UiDefinition board)
    {
        var images = new Dictionary<string, byte[]>(); var fonts = new Dictionary<string, byte[]>();
        foreach (var e in board.Elements)
        {
            if (e.Texture.Length > 0 && !images.ContainsKey(e.Texture) && TextureAssets.TryGet(project, e.Texture, out var png)) images[e.Texture] = png;
            if (Fonts.FileOf(project, e.Font) is string path && !fonts.ContainsKey(e.Font)) fonts[e.Font] = project.Assets[path];
        }
        return (images, fonts);
    }

    /// <summary>Adds a board from elsewhere (a .lb file or an exported page) to the project: its pictures and fonts become
    /// the project's (a name already taken by a different file gets a number), and it gets an ID of its own.</summary>
    public static UiDefinition Import(Project project, LbFile file)
    {
        var board = file.Board; string id = project.Manifest.Id;
        board.IsLeaderboard = true; board.IsComponent = false;
        string wanted = Slug(board.Id.Length > 0 ? board.Id : "leaderboard");
        board.Id = project.Leaderboards.Any(b => b.Id == wanted) || !Validation.Id(wanted) ? ElementIds.Next(wanted, project.Leaderboards.Select(b => b.Id)) : wanted;
        string Place(string folder, string name, byte[] bytes, Func<string, string> pathOf)
        {
            string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name), candidate = name;
            for (int n = 2; project.Assets.TryGetValue(pathOf(candidate), out var there) && !there.AsSpan().SequenceEqual(bytes); n++) candidate = stem + "_" + n + ext;
            project.Assets[pathOf(candidate)] = bytes; return candidate;
        }
        var textures = new Dictionary<string, string>(); var fontIds = new Dictionary<string, string>();
        foreach (var e in board.Elements)
        {
            if (e.Texture.Length > 0 && file.Images.TryGetValue(e.Texture, out var png))
            {
                if (!textures.TryGetValue(e.Texture, out var resource))
                {
                    string name = Place("image", SafeName(e.Texture.Split('/')[^1], ".png"), png, n => TextureAssets.Path(id, n, "image"));
                    resource = textures[e.Texture] = TextureAssets.Resource(id, name, "image");
                }
                e.Texture = resource;
            }
            if (file.Fonts.TryGetValue(e.Font, out var font))
            {
                if (!fontIds.TryGetValue(e.Font, out var resource))
                {
                    string name = Place("font", SafeName(e.Font.Split('/')[^1], ".ttf"), font, n => Fonts.Path(id, n));
                    resource = fontIds[e.Font] = Fonts.Resource(id, Fonts.Path(id, name));
                }
                e.Font = resource;
            }
        }
        project.Leaderboards.Add(board);
        return board;
    }
    static string Slug(string text) { var s = Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9_]+", "_").Trim('_'); return s.Length == 0 ? "leaderboard" : s; }
    /// <summary>A file name the project and the arcade both take: lowercase letters, digits, _ and -, and its extension.</summary>
    static string SafeName(string name, string fallbackExtension)
    {
        string ext = Path.GetExtension(name).ToLowerInvariant(), stem = Path.GetFileNameWithoutExtension(name).ToLowerInvariant();
        if (!Regex.IsMatch(ext, "^\\.[a-z0-9]+$")) ext = fallbackExtension;
        stem = Regex.Replace(stem, "[^a-z0-9_-]+", "_").Trim('_', '-');
        return (stem.Length == 0 ? "file" : stem) + ext;
    }

    // ---- The page ----
    /// <summary>leaderboard.html and its pictures and fonts (under leaderboard/), for Arcadia or an export folder.</summary>
    public static Dictionary<string, byte[]> Page(Project project, UiDefinition board)
    {
        var files = new Dictionary<string, byte[]>(); var urls = new JsonObject(); var fontUrls = new JsonObject();
        var (images, fonts) = Files(project, board);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string Name(string folder, string resource, string fallbackExtension)
        {
            string name = SafeName(resource.Split('/')[^1], fallbackExtension), stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name), candidate = folder + name;
            for (int n = 2; !used.Add(candidate); n++) candidate = folder + stem + "_" + n + ext;
            return candidate;
        }
        foreach (var (resource, png) in images) { string path = Name(Folder, resource, ".png"); files[path] = png; urls[resource] = path; }
        foreach (var (resource, font) in fonts) { string path = Name(Folder + "fonts/", resource, ".ttf"); files[path] = font; fontUrls[resource] = path; }
        var data = new JsonObject { ["format"] = Format, ["version"] = 1, ["board"] = JsonNode.Parse(Json.Write(board)), ["images"] = urls, ["fonts"] = fontUrls };
        // The design sits in a JSON block: "<" is escaped so no text in it can close the script element.
        string json = data.ToJsonString(new JsonSerializerOptions { WriteIndented = false }).Replace("<", "\\u003c");
        string title = System.Net.WebUtility.HtmlEncode(board.Title.Length > 0 ? board.Title : "Leaderboard");
        var html = new StringBuilder();
        html.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        html.Append("<title>").Append(title).Append("</title>\n<style>\n").Append(Css).Append("</style>\n");
        html.Append("<script type=\"application/json\" id=\"").Append(BlockId).Append("\">").Append(json).Append("</script>\n</head>\n<body>\n<div id=\"stage\"></div>\n");
        // The arcade's leaderboard API. Outside the arcade it isn't there, and the page shows sample data instead.
        html.Append("<script src=\"/sdk/arcadia.js\"></script>\n<script>\n").Append(PageScript).Append("\n</script>\n</body>\n</html>\n");
        files[PageName] = Encoding.UTF8.GetBytes(html.ToString());
        return files;
    }
    const string Css = """
        html,body{margin:0;height:100%;overflow:hidden;background:#0E1014}
        #stage{position:absolute;left:0;top:0;transform-origin:0 0;overflow:hidden}
        .el{position:absolute;box-sizing:border-box;overflow:hidden;line-height:1.25}
        .label,.lb_rank{display:flex;align-items:center}
        .text{width:100%;white-space:pre-wrap}
        .lb_table,.lb_me,.lb_range{display:flex;flex-direction:column}
        .list{flex:1;min-height:0;overflow-y:auto;scrollbar-width:thin;scrollbar-color:rgba(255,255,255,.25) transparent}
        .header{flex:0 0 auto;opacity:.7}
        .row{display:flex;align-items:center;gap:.5em;padding:0 .5em;box-sizing:border-box}
        .row.you{font-weight:700}
        .cell{white-space:nowrap;overflow:hidden;text-overflow:ellipsis;display:flex;align-items:center;gap:.3em}
        .cell.first{flex:0 0 auto;min-width:2.4em}
        .cell.grow{flex:1 1 auto;min-width:0}
        .cell.last{flex:0 0 auto;margin-left:auto;justify-content:flex-end}
        .controls{flex:0 0 auto;display:flex;flex-wrap:wrap;gap:.4em;align-items:center;padding:.35em .5em}
        .controls input{font:inherit;color:inherit;width:4em;background:rgba(0,0,0,.35);border:1px solid rgba(255,255,255,.25);border-radius:3px;padding:1px 4px}
        .controls input[type=text]{width:9em}
        .empty{padding:.5em;opacity:.7}
        .lb_podium{display:flex;align-items:flex-end;gap:4%;padding:0 4%}
        .step{flex:1;height:100%;display:flex;flex-direction:column;justify-content:flex-end;text-align:center;min-width:0}
        .who{padding-bottom:.3em;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
        .block{border-radius:4px 4px 0 0;display:flex;justify-content:center;color:#15181D;font-weight:700;font-size:1.6em;padding-top:.15em;box-sizing:border-box}
        .lb_icon svg{display:block;width:100%;height:100%}
        .lb_periods{display:flex;gap:.3em}
        .tab{flex:1;border:0;border-radius:3px;background:rgba(255,255,255,.08);cursor:pointer;padding:0}
        .tab.on{background:rgba(255,255,255,.3)}
        .track{display:flex;height:100%;overflow-x:auto;scroll-snap-type:x mandatory;scrollbar-width:none}
        .track::-webkit-scrollbar{display:none}
        .card{flex:0 0 auto;scroll-snap-align:start;display:flex;flex-direction:column;align-items:center;justify-content:center;text-align:center;box-sizing:border-box;padding:.3em;gap:.15em;min-width:0}
        .card div{max-width:100%;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
        .shape-ellipse,.shape-circle{border-radius:50%}
        .shape-triangle{clip-path:polygon(50% 0,100% 100%,0 100%)}
        .shape-diamond{clip-path:polygon(50% 0,100% 50%,50% 100%,0 50%)}
        .shape-hexagon{clip-path:polygon(25% 0,75% 0,100% 50%,75% 100%,25% 100%,0 50%)}
        .shape-star{clip-path:polygon(50% 0,61% 35%,98% 35%,68% 57%,79% 91%,50% 70%,21% 91%,32% 57%,2% 35%,39% 35%)}
        .sample{position:fixed;right:8px;bottom:6px;font:600 11px system-ui,sans-serif;letter-spacing:.08em;color:#F4C744;background:rgba(0,0,0,.6);padding:3px 8px;border-radius:3px;pointer-events:none}
        body.pixel .el{image-rendering:pixelated}

        """;

    /// <summary>The design inside a page this app exported, with the pictures and fonts it names (read beside the page),
    /// or null for any other page.</summary>
    public static LbFile? FromPage(string html, Func<string, byte[]?> sibling)
    {
        var match = Regex.Match(html, "<script type=\"application/json\" id=\"" + BlockId + "\">(.*?)</script>", RegexOptions.Singleline);
        if (!match.Success) return null;
        JsonObject node;
        try { node = JsonNode.Parse(match.Groups[1].Value)!.AsObject(); } catch (JsonException) { return null; }
        if ((string?)node["format"] != Format || node["board"] is not JsonObject boardNode) return null;
        var board = Json.Read<UiDefinition>(boardNode.ToJsonString());
        Dictionary<string, byte[]> Read(string key)
        {
            var found = new Dictionary<string, byte[]>();
            if (node[key] is JsonObject o) foreach (var (resource, url) in o) if ((string?)url is string path && !path.Contains("..") && sibling(path) is byte[] bytes) found[resource] = bytes;
            return found;
        }
        return new(board, Read("images"), Read("fonts"));
    }

    /// <summary>What would go wrong with a board on the arcade: pictures that can't travel with it, or a page that would
    /// be empty.</summary>
    public static List<string> Problems(Project project, UiDefinition board)
    {
        var problems = new List<string>();
        foreach (var e in board.Elements)
        {
            if (!Types.Contains(e.Type)) problems.Add($"{e.Id}: a {e.Type} can't go on a leaderboard page.");
            if (e.Texture.Length > 0 && !TextureAssets.TryGet(project, e.Texture, out _)) problems.Add($"{e.Id}: the picture {e.Texture} isn't in the project, so the page can't show it.");
        }
        if (!board.Elements.Any(e => e.Type.StartsWith("lb_") && e.Type is not "lb_icon" and not "lb_periods")) problems.Add("The page has no list, podium or rank box, so it shows no scores.");
        return problems;
    }

    /// <summary>A new leaderboard page (see LeaderboardPages.Starter).</summary>
    public static UiDefinition Starter(string id) => LeaderboardPages.Starter(id);
}
