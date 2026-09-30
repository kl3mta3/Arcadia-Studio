using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Wysicraft.Models;
namespace Wysicraft.Packaging;

/// <summary>One problem or fact about an Arcadia upload, from the local check or the arcade. Level: block (refused,
/// must be fixed), hold (a moderator must look), warn (worth fixing) or info.</summary>
public sealed class ArcadiaFinding
{
    public string Level { get; set; } = "info";
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public string? File { get; set; }
    public int? Line { get; set; }
    public string? Snippet { get; set; }
    public ArcadiaFinding() { }
    public ArcadiaFinding(string level, string code, string message, string? file = null) { Level = level; Code = code; Message = message; File = file; }
}

/// <summary>A game packed for Arcadia (docs/ARCADIA.md): the ordinary folder web export, plus game.json, the cover and
/// any screenshots, in a zip. Check applies the arcade's upload rules locally, so most mistakes show before
/// anything is sent.</summary>
public static class ArcadiaPackage
{
    public const string DefaultArcade = "https://arcadia.arcadiastudio.games";
    public const int MaxFiles = 1000, MaxFolders = 10, DefaultMaxUploadMb = 50;
    public const long MaxFileBytes = 25L * 1024 * 1024, MaxScreenshotBytes = 2L * 1024 * 1024;
    public const int ScreenshotWidth = 1280, ScreenshotHeight = 800, MaxScreenshots = 8, MaxVideos = 3;
    public static readonly string[] Genres = ["Action", "Arcade", "Puzzle", "Platformer", "Runner", "Shooter", "Strategy", "Survival", "Roguelike", "Racing", "Sports", "Casual", "Classic"];
    static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".js", ".mjs", ".css", ".json", ".txt", ".md", ".csv", ".xml", ".svg", ".atlas", ".fnt", ".tmx", ".tsj", ".tmj", ".glsl", ".frag", ".vert",
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".avif", ".ico", ".bmp", ".mp3", ".ogg", ".oga", ".wav", ".m4a", ".aac", ".flac", ".mid", ".midi", ".mp4", ".webm", ".ogv",
        ".woff", ".woff2", ".ttf", ".otf", ".glb", ".gltf", ".bin", ".wasm"
    };
    // Each part of a path: starts with a letter, digit or _, then only letters, digits, _ . - ( ) and spaces.
    static readonly Regex PathPart = new(@"^[A-Za-z0-9_][A-Za-z0-9_.\-() ]*$");
    static readonly Regex StatKey = new(@"^[a-z0-9_]+$");
    static readonly Regex StateSet = new(@"ctx\.state\.set\(\s*['""]([A-Za-z_][A-Za-z0-9_]*)['""]");
    // YouTube links the arcade takes: youtube.com/watch?v=…, youtu.be/… and youtube.com/shorts/… (11-character video IDs).
    static readonly Regex YouTube = new(@"^https?://(?:(?:www\.|m\.)?youtube\.com/(?:watch\?(?:[^\s#]*&)?v=[A-Za-z0-9_-]{11}(?:[&#][^\s]*)?|shorts/[A-Za-z0-9_-]{11}(?:[/?#][^\s]*)?)|youtu\.be/[A-Za-z0-9_-]{11}(?:[/?#][^\s]*)?)$", RegexOptions.IgnoreCase);
    /// <summary>Whether a link is a YouTube video the arcade will take (a watch, youtu.be or shorts link).</summary>
    public static bool IsYouTube(string link) => YouTube.IsMatch(link.Trim());
    /// <summary>The cover's name in the package: cover.png, cover.jpg or cover.webp.</summary>
    public static string CoverName(PublishSettings s) => "cover." + (s.CoverType.Length > 0 ? s.CoverType : "png");
    /// <summary>The leaderboard page an upload carries: one of the project's leaderboards (Board), a page imported as it is
    /// (Html), or neither (the arcade's standard board). "" means the project's first leaderboard, if it has one.</summary>
    public static (UiDefinition? Board, byte[]? Html, bool Chosen) LeaderboardPageOf(Project project, PublishSettings s)
    {
        string choice = s.LeaderboardPage;
        if (choice == "standard") return (null, null, false);
        if (choice == "file") return (null, s.LeaderboardHtml.Length > 0 ? s.LeaderboardHtml : null, true);
        if (choice.StartsWith("board:", StringComparison.Ordinal)) return (project.Leaderboards.FirstOrDefault(b => b.Id == choice[6..]), null, true);
        var first = project.Leaderboards.FirstOrDefault();
        return (first, null, first != null);
    }
    // Anything a page would load from another website: the arcade runs pages with no network.
    static readonly Regex Outside = new(@"(?:\b(?:src|href|action)\s*=\s*[""']?\s*|url\(\s*[""']?\s*|@import\s+[""']?)(?:https?:)?//", RegexOptions.IgnoreCase);
    /// <summary>The screenshots' names in the package, in gallery order: screenshots/1.png, screenshots/2.jpg …</summary>
    public static List<string> ScreenshotNames(PublishSettings s) => s.Screenshots.Select((x, i) => "screenshots/" + (i + 1) + "." + (x.Type.Length > 0 ? x.Type : "png")).ToList();
    // Already-compressed formats are stored rather than deflated again.
    static readonly HashSet<string> Stored = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".avif", ".mp3", ".ogg", ".oga", ".m4a", ".aac", ".flac", ".mp4", ".webm", ".ogv", ".woff", ".woff2" };

    /// <summary>SHA-256 (lowercase hex) of the web runtime exactly as exports write it. Arcadia trusts official builds
    /// by this hash; each release has a new one.</summary>
    public static string RuntimeSha256 { get; } = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(WebExport.RuntimeScript))).ToLowerInvariant();

    /// <summary>The arcade's shape for the main screen: "16:9", "4:3", or width / height as a number.</summary>
    public static string AspectRatio(Project project)
    {
        var screen = project.Screens.FirstOrDefault(s => s.Id == project.Manifest.DefaultUi) ?? project.Screens.FirstOrDefault(s => !s.IsComponent);
        if (screen == null || screen.Size.Width <= 0 || screen.Size.Height <= 0) return "16:9";
        double ratio = (double)screen.Size.Width / screen.Size.Height;
        if (Math.Abs(ratio - 16.0 / 9) < 0.005) return "16:9";
        if (Math.Abs(ratio - 4.0 / 3) < 0.005) return "4:3";
        return Math.Round(ratio, 4).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The version after the last one published: the last number goes up by one.</summary>
    public static string NextVersion(string last)
    {
        if (string.IsNullOrWhiteSpace(last)) return "1.0.0";
        var match = Regex.Match(last, @"^(.*?)(\d+)(\D*)$");
        if (!match.Success) return last + ".1";
        return match.Groups[1].Value + (long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture) + match.Groups[3].Value;
    }

    /// <summary>Names a score can be read from: every screen variable, and every ctx.state name the scripts set.</summary>
    public static List<string> StateNames(Project project)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var screen in project.Screens) foreach (var name in screen.Variables.Keys) names.Add(name);
        foreach (var code in project.Scripts.Values) foreach (Match m in StateSet.Matches(code)) names.Add(m.Groups[1].Value);
        return names.ToList();
    }

    /// <summary>The package's game.json.</summary>
    public static string GameJson(Project project, PublishSettings s, string wysicraftVersion)
    {
        var game = new JsonObject
        {
            ["title"] = s.Title.Trim(),
            ["description"] = s.Description.Trim(),
            ["genre"] = new JsonArray(s.Genre.Where(g => g.Trim().Length > 0).Select(g => (JsonNode)JsonValue.Create(g.Trim())!).ToArray()),
            ["version"] = s.Version.Trim(),
            ["entry"] = "index.html",
            ["cover"] = CoverName(s),
            ["controls"] = s.Controls.Trim()
        };
        if (s.Screenshots.Count > 0) game["screenshots"] = new JsonArray(ScreenshotNames(s).Select(n => (JsonNode)JsonValue.Create(n)!).ToArray());
        var videos = s.Videos.Select(v => v.Trim()).Where(v => v.Length > 0).ToList();
        if (videos.Count > 0) game["videos"] = new JsonArray(videos.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());
        // Only when it's true: left out, the arcade tells phone players the game may need a keyboard.
        if (s.Mobile) game["mobile"] = true;
        var page = LeaderboardPageOf(project, s);
        if (page.Board != null || page.Html != null) game["leaderboardPage"] = Leaderboards.PageName;
        string aspect = s.AspectRatio.Trim().Length > 0 ? s.AspectRatio.Trim() : AspectRatio(project);
        game["aspectRatio"] = double.TryParse(aspect, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? JsonValue.Create(number) : JsonValue.Create(aspect);
        game["wysicraft"] = new JsonObject { ["version"] = wysicraftVersion, ["projectId"] = project.Manifest.Id };
        if (s.Leaderboard)
        {
            var c = s.Scores;
            JsonObject Watch(string variable, string path) { var w = new JsonObject { ["variable"] = variable.Trim() }; if (path.Trim().Length > 0) w["path"] = path.Trim(); return w; }
            var scores = new JsonObject
            {
                ["label"] = c.Label.Trim().Length > 0 ? c.Label.Trim() : "Score",
                ["format"] = c.Format, ["order"] = c.Order, ["aggregate"] = c.Aggregate,
                ["min"] = c.Min, ["minSeconds"] = c.MinSeconds
            };
            if (c.Max is double max) scores["max"] = max;
            scores["stats"] = new JsonArray(c.Stats.Select(st =>
            {
                var stat = new JsonObject { ["key"] = st.Key.Trim(), ["label"] = st.Label.Trim(), ["format"] = st.Format, ["aggregate"] = st.Aggregate };
                if (!st.Check) stat["check"] = false;
                return (JsonNode)stat;
            }).ToArray());
            var triggers = new JsonArray();
            foreach (var t in c.Triggers) { var w = Watch(t.Variable, t.Path); if (t.EqualsValue != null) w["equals"] = t.EqualsValue; triggers.Add(w); }
            scores["watch"] = new JsonObject
            {
                ["score"] = Watch(c.Score.Variable, c.Score.Path),
                ["trigger"] = triggers,
                ["stats"] = new JsonArray(c.Stats.Where(st => st.Variable.Trim().Length > 0).Select(st => { var w = Watch(st.Variable, st.Path); w["key"] = st.Key.Trim(); w["label"] = st.Label.Trim(); return (JsonNode)w; }).ToArray()),
                ["round"] = c.Round
            };
            game["scores"] = scores;
        }
        return game.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Every file in the package: the folder web export (never the single-file one: Arcadia recognises the
    /// official runtime only as its own file), game.json, the cover and the screenshots. Videos are links only.</summary>
    public static Dictionary<string, byte[]> Files(Project project, PublishSettings s, string wysicraftVersion)
    {
        var files = WebExport.Files(project);
        files["game.json"] = Encoding.UTF8.GetBytes(GameJson(project, s, wysicraftVersion));
        if (s.Cover.Length > 0) files[CoverName(s)] = s.Cover;
        var names = ScreenshotNames(s);
        for (int i = 0; i < names.Count; i++) if (s.Screenshots[i].Bytes.Length > 0) files[names[i]] = s.Screenshots[i].Bytes;
        // The leaderboard page: made from the project's leaderboard now, or the imported page as it is.
        var page = LeaderboardPageOf(project, s);
        if (page.Board != null) foreach (var (path, bytes) in Leaderboards.Page(project, page.Board)) files[path] = bytes;
        else if (page.Html != null) files[Leaderboards.PageName] = page.Html;
        return files;
    }

    /// <summary>The zip Arcadia takes: forward-slash paths, stored or deflated entries, game.json first.</summary>
    public static byte[] Zip(Dictionary<string, byte[]> files)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true, Encoding.UTF8))
            foreach (var (name, bytes) in files.OrderBy(f => f.Key == "game.json" ? 0 : 1).ThenBy(f => f.Key, StringComparer.Ordinal))
            {
                var entry = archive.CreateEntry(name, Stored.Contains(Path.GetExtension(name)) ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                using var stream = entry.Open(); stream.Write(bytes);
            }
        return output.ToArray();
    }

    /// <summary>What a picture really is, from its first bytes: png, jpg, webp, or null.</summary>
    public static string? ImageType(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return "jpg";
        if (bytes.Length >= 12 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP") return "webp";
        return null;
    }

    /// <summary>A picture's size in pixels, for PNG, JPEG and WebP, or null if it can't be read.</summary>
    public static (int Width, int Height)? ImageSize(byte[] b)
    {
        try
        {
            switch (ImageType(b))
            {
                case "png": return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(16)), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(20)));
                case "jpg":
                    for (int i = 2; i + 9 < b.Length;)
                    {
                        if (b[i] != 0xFF) { i++; continue; }
                        byte marker = b[i + 1];
                        if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC) return ((b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6]);
                        if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7)) { i += 2; continue; }
                        i += 2 + ((b[i + 2] << 8) | b[i + 3]);
                    }
                    return null;
                case "webp":
                    string chunk = Encoding.ASCII.GetString(b, 12, 4);
                    if (chunk == "VP8X") return (1 + (b[24] | b[25] << 8 | b[26] << 16), 1 + (b[27] | b[28] << 8 | b[29] << 16));
                    if (chunk == "VP8 ") return ((b[26] | b[27] << 8) & 0x3FFF, (b[28] | b[29] << 8) & 0x3FFF);
                    if (chunk == "VP8L") { int v = b[21] | b[22] << 8 | b[23] << 16 | b[24] << 24; return (1 + (v & 0x3FFF), 1 + ((v >> 14) & 0x3FFF)); }
                    return null;
            }
        }
        catch (ArgumentOutOfRangeException) { }
        catch (IndexOutOfRangeException) { }
        return null;
    }

    /// <summary>The arcade's upload rules, applied here first: the details, the leaderboard, the cover, screenshots and
    /// video links, and every file's path, type and size. Anything at level block would be refused.</summary>
    public static List<ArcadiaFinding> Check(Project project, PublishSettings s, Dictionary<string, byte[]> files, int maxUploadMb = DefaultMaxUploadMb)
    {
        var found = new List<ArcadiaFinding>();
        void Add(string level, string code, string message, string? file = null) => found.Add(new(level, code, message, file));

        // The details.
        string title = s.Title.Trim();
        if (title.Length == 0) Add("block", "title", "Give the game a title.");
        else if (title.Length > 60) Add("block", "title", $"The title is {title.Length} characters; at most 60.");
        if (s.Description.Trim().Length == 0) Add("warn", "description", "Add a description: it's what players read before they play.");
        else if (s.Description.Trim().Length > 600) Add("block", "description", $"The description is {s.Description.Trim().Length} characters; at most 600.");
        else if (Regex.IsMatch(s.Description, @"<[A-Za-z/!]")) Add("warn", "description", "The description is shown as plain text, so HTML tags appear as typed.");
        var genres = s.Genre.Where(g => g.Trim().Length > 0).ToList();
        if (genres.Count == 0) Add("warn", "genre", "Pick at least one genre, so players can find the game by it.");
        if (genres.Count > 3) Add("block", "genre", "At most 3 genres.");
        foreach (var g in genres.Where(g => g.Trim().Length > 24)) Add("block", "genre", $"The genre \"{g.Trim()}\" is over 24 characters.");
        if (s.Version.Trim().Length == 0) Add("warn", "version", "Give this upload a version (for example 1.0.1), and raise it every time you publish.");
        else if (s.Version.Trim().Length > 32) Add("block", "version", "The version is at most 32 characters.");
        if (s.Controls.Trim().Length == 0) Add("warn", "controls", "Say how to play (keys, mouse or touch): it's shown beside the game.");
        else if (s.Controls.Trim().Length > 200) Add("block", "controls", $"The controls text is {s.Controls.Trim().Length} characters; at most 200.");
        string aspect = s.AspectRatio.Trim();
        if (aspect.Length > 0 && aspect is not ("16:9" or "4:3") && !(double.TryParse(aspect, NumberStyles.Float, CultureInfo.InvariantCulture, out double r) && r > 0.2 && r < 5))
            Add("block", "aspect-ratio", "The aspect ratio is 16:9, 4:3 or width ÷ height as a number (for example 1.6).");

        // The leaderboard.
        if (s.Leaderboard)
        {
            var c = s.Scores; var names = StateNames(project).ToHashSet(StringComparer.Ordinal);
            if (c.Score.Variable.Trim().Length == 0) Add("block", "scores", "Choose where the score comes from.");
            else if (!names.Contains(c.Score.Variable.Trim())) Add("warn", "scores", $"The score's variable \"{c.Score.Variable.Trim()}\" isn't a screen variable or a ctx.state name in this project.");
            if (c.Triggers.Count == 0) Add("block", "scores", "Say when a run ends, so the score can be sent.");
            if (c.Triggers.Count > 6) Add("block", "scores", "At most 6 \"run ends when\" conditions.");
            foreach (var t in c.Triggers)
            {
                if (t.Variable.Trim().Length == 0) Add("block", "scores", "A \"run ends when\" condition has no variable.");
                else if (!names.Contains(t.Variable.Trim())) Add("warn", "scores", $"The run-end variable \"{t.Variable.Trim()}\" isn't a screen variable or a ctx.state name in this project.");
            }
            if (c.Max == null) Add("warn", "scores", "Set the highest score that's really possible: anything above it is refused as a cheat. Without it, any score counts.");
            else if (c.Max < c.Min) Add("block", "scores", "The highest score is below the lowest.");
            if (c.MinSeconds < 0) Add("block", "scores", "The shortest run can't be negative.");
            if (c.Stats.Count > 8) Add("block", "scores", "At most 8 extra columns.");
            foreach (var st in c.Stats)
            {
                if (!StatKey.IsMatch(st.Key.Trim())) Add("block", "scores", $"The column key \"{st.Key}\" may only use lowercase letters, digits and _.");
                if (st.Variable.Trim().Length > 0 && !names.Contains(st.Variable.Trim())) Add("warn", "scores", $"The column \"{st.Key}\" reads \"{st.Variable.Trim()}\", which isn't a screen variable or a ctx.state name in this project.");
            }
            if (c.Stats.GroupBy(st => st.Key.Trim()).Any(g => g.Count() > 1)) Add("block", "scores", "Two extra columns have the same key.");
        }

        // The pictures: the cover (required) and the screenshots (optional, up to 8), each checked the same way.
        void Picture(string code, string what, string name, byte[] bytes)
        {
            string? type = ImageType(bytes);
            if (type == null || !name.EndsWith("." + type, StringComparison.Ordinal)) Add("block", code, $"{what} isn't really a PNG, JPEG or WebP picture.", name);
            if (bytes.Length > MaxScreenshotBytes) Add("block", code, $"{what} is {bytes.Length / 1048576.0:0.0} MB; at most 2 MB.", name);
            if (ImageSize(bytes) is var (w, h) && (w != ScreenshotWidth || h != ScreenshotHeight)) Add("warn", code, $"{what} is {w} × {h}. Arcadia shows it at 1280 × 800 (16:10), so it may be cropped or blurry.", name);
        }
        string coverName = CoverName(s);
        if (!files.TryGetValue(coverName, out var cover)) Add("block", "cover", "Add a cover: capture one from Preview or choose a picture.");
        else Picture("cover", "The cover", coverName, cover);
        var shotNames = ScreenshotNames(s);
        if (shotNames.Count > MaxScreenshots) Add("block", "screenshots", $"{shotNames.Count} screenshots; at most {MaxScreenshots}.");
        for (int i = 0; i < shotNames.Count; i++)
        {
            if (!files.TryGetValue(shotNames[i], out var shot)) Add("block", "screenshots", $"Screenshot {i + 1} has no picture.", shotNames[i]);
            else Picture("screenshots", $"Screenshot {i + 1}", shotNames[i], shot);
        }
        if (shotNames.Count is > 0 and < 3) Add("info", "screenshots", "Games with 3 or more screenshots show a fuller gallery on their page.");
        else if (shotNames.Count == 0) Add("info", "screenshots", "No screenshots: add a few (3 or more is best) to show the game on its page.");

        // The video links: YouTube only, and never files.
        var links = s.Videos.Select(v => v.Trim()).Where(v => v.Length > 0).ToList();
        if (links.Count > MaxVideos) Add("block", "videos", $"{links.Count} video links; at most {MaxVideos}.");
        foreach (var link in links.Where(l => !IsYouTube(l))) Add("block", "videos", $"\"{link}\" isn't a YouTube video link (youtube.com/watch?v=…, youtu.be/… or youtube.com/shorts/…).");

        // The leaderboard page.
        var board = LeaderboardPageOf(project, s);
        if (board.Chosen && board.Board == null && board.Html == null)
            Add("block", "leaderboard-page", s.LeaderboardPage == "file" ? "The imported leaderboard page is missing: import it again or choose another." : $"The leaderboard page \"{s.LeaderboardPage[6..]}\" isn't in the project anymore: choose another.");
        if ((board.Board != null || board.Html != null) && !s.Leaderboard) Add("warn", "leaderboard-page", "There's a leaderboard page but Keep a leaderboard is off, so the page would have nothing to show.");
        if (board.Board != null) foreach (var problem in Leaderboards.Problems(project, board.Board)) Add("warn", "leaderboard-page", "Leaderboard page: " + problem);
        if (files.TryGetValue(Leaderboards.PageName, out var pageBytes) && Outside.IsMatch(Encoding.UTF8.GetString(pageBytes)))
            Add("block", "leaderboard-page", "The leaderboard page loads something from another website. Pages on the arcade can only use files in the game.", Leaderboards.PageName);

        // The files.
        if (!files.ContainsKey("game.json")) Add("block", "game-json", "game.json is missing.");
        if (!files.ContainsKey("index.html")) Add("block", "entry", "index.html is missing.");
        if (!files.TryGetValue("wysicraft/wysicraft-web.js", out var runtime)) Add("block", "runtime", "The Arcadia Studio runtime is missing from the game.");
        else if (Convert.ToHexString(SHA256.HashData(runtime)).ToLowerInvariant() != RuntimeSha256) Add("hold", "runtime", "The game's Arcadia Studio runtime has been changed, so a moderator would have to look at it.");
        if (files.Count > MaxFiles) Add("block", "files", $"{files.Count} files; at most {MaxFiles}.");
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, bytes) in files)
        {
            var parts = path.Split('/');
            if (path.StartsWith('/') || path.Contains('\\') || parts.Any(p => p is "" or "." or "..")) { Add("block", "path", "Not a plain relative path.", path); continue; }
            if (parts.Length - 1 > MaxFolders) Add("block", "path", $"More than {MaxFolders} folders deep.", path);
            if (parts.Any(p => p.StartsWith('.'))) Add("block", "path", "Hidden files and folders aren't allowed.", path);
            else if (parts.Any(p => !PathPart.IsMatch(p))) Add("block", "path", "File and folder names may only use letters, digits, spaces and _ . - ( ), starting with a letter, digit or _.", path);
            if (!Allowed.Contains(Path.GetExtension(path))) Add("block", "file-type", $"{Path.GetExtension(path)} files can't be uploaded.", path);
            if (Path.GetExtension(path).Equals(".wasm", StringComparison.OrdinalIgnoreCase)) Add("hold", "wasm", "WebAssembly is always looked at by a moderator.", path);
            if (bytes.LongLength > MaxFileBytes) Add("block", "file-size", $"{bytes.LongLength / 1048576.0:0.0} MB; each file is at most 25 MB.", path);
            if (seen.TryGetValue(path, out var other)) Add("block", "path", $"Differs from {other} only in upper/lower case.", path); else seen[path] = path;
        }
        long total = files.Sum(f => f.Value.LongLength);
        if (total > (long)maxUploadMb * 1024 * 1024) Add("block", "size", $"The game is {total / 1048576.0:0.0} MB unzipped; this arcade takes at most {maxUploadMb} MB.");
        Add("info", "files", $"{files.Count} files, {total / 1048576.0:0.0} MB unzipped.");
        return found;
    }
}
