using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Wysicraft.Models;
namespace Wysicraft.Packaging;

/// <summary>Publishing to itch.io with butler (itch.io's own uploader, bundled with Arcadia Studio). The game's page is
/// made and edited on itch.io; this sends the builds: the web version (played in the browser) and the Windows app, each
/// to its own channel. The checks here run before butler is asked to do anything.</summary>
public static class Itch
{
    public const string Web = "https://itch.io";
    static readonly Regex Slug = new("^[a-z0-9][a-z0-9_-]*$"), Channel = new("^[a-z0-9][a-z0-9-]*$");

    /// <summary>A game as butler names it ("user/game"), from that or the game's page address
    /// (https://user.itch.io/game), or null.</summary>
    public static string? Target(string input)
    {
        string text = input.Trim().TrimEnd('/');
        var page = Regex.Match(text, @"^(?:https?://)?([A-Za-z0-9_-]+)\.itch\.io/([A-Za-z0-9_-]+)(?:[/?#].*)?$");
        if (page.Success) text = page.Groups[1].Value + "/" + page.Groups[2].Value;
        var parts = text.ToLowerInvariant().Split('/');
        return parts.Length == 2 && Slug.IsMatch(parts[0]) && Slug.IsMatch(parts[1]) ? parts[0] + "/" + parts[1] : null;
    }
    /// <summary>The game's page on itch.io.</summary>
    public static string PageOf(string target) { var p = target.Split('/'); return p.Length == 2 ? $"https://{p[0]}.itch.io/{p[1]}" : Web; }
    /// <summary>itch.io's page for editing the game (title, description, price, "played in the browser"...).</summary>
    public static string EditPageOf(ItchSettings s) => s.GameId > 0 ? $"{Web}/game/edit/{s.GameId}" : PageOf(s.Target);
    public const string NewGamePage = Web + "/game/new", ApiKeysPage = Web + "/user/settings/api-keys";

    /// <summary>What's wrong before anything is sent. block: can't publish; warn: worth fixing; info.</summary>
    public static List<ArcadiaFinding> Check(ItchSettings s, string version)
    {
        var found = new List<ArcadiaFinding>();
        void Add(string level, string code, string message) => found.Add(new(level, code, message));
        if (Target(s.Target) == null) Add("block", "game", "Choose the itch.io game to upload to (Set up…).");
        if (!s.Web && !s.Windows) Add("block", "builds", "Choose what to upload: the web version, the Windows app, or both.");
        foreach (var (on, channel, name) in new[] { (s.Web, s.WebChannel, "web"), (s.Windows, s.WindowsChannel, "Windows") })
            if (on && !Channel.IsMatch(channel)) Add("block", "channel", $"The {name} channel is lowercase letters, digits and dashes, for example {(name == "web" ? "html5" : "windows")}.");
        if (s.Web && s.Windows && s.WebChannel == s.WindowsChannel) Add("block", "channel", "The web version and the Windows app need channels of their own.");
        if (s.Web && Regex.IsMatch(s.WebChannel, "win|linux|mac|osx|android")) Add("warn", "channel", $"itch.io tags a channel named \"{s.WebChannel}\" as a download for that system, not a browser game. html5 or web is safer.");
        if (s.Windows && !s.WindowsChannel.Contains("win")) Add("warn", "channel", $"itch.io marks a channel as a Windows download when its name has win or windows in it; \"{s.WindowsChannel}\" hasn't.");
        if (version.Trim().Length > 64) Add("block", "version", "The version is at most 64 characters.");
        if (version.Trim().Length == 0) Add("warn", "version", "Give this upload a version (for example 1.0.1): players and the itch.io app see it.");
        if (s.Web && !s.BrowserStepDone) Add("info", "browser", "Once, after the first web upload: on itch.io's Edit game page set Kind of project to HTML and tick \"This file will be played in the browser\" for the web upload.");
        return found;
    }

    /// <summary>One upload: the channel it goes to and the files (paths relative to the upload's folder).</summary>
    public sealed record Build(string Kind, string Channel, Dictionary<string, byte[]> Files);
    /// <summary>The builds to send: the folder web export (index.html at the top, as itch.io plays it in the browser)
    /// and the Windows app (its program at the top).</summary>
    public static List<Build> Builds(Project project, ItchSettings s, string? appHostExe)
    {
        var builds = new List<Build>();
        if (s.Web) builds.Add(new("web", s.WebChannel, WebExport.Files(project)));
        if (s.Windows) builds.Add(new("windows", s.WindowsChannel, DesktopExport.WindowsAppFiles(project, appHostExe ?? throw new FileNotFoundException("The Windows app host is missing from this Arcadia Studio install."))));
        return builds;
    }
    /// <summary>Writes a build into a folder of its own (made empty first), for butler to send.</summary>
    public static void WriteBuild(Build build, string folder)
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        foreach (var (path, bytes) in build.Files)
        {
            if (path.Contains("..") || Path.IsPathRooted(path)) throw new InvalidDataException("Bad file path in a build: " + path);
            string full = Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllBytes(full, bytes);
        }
    }

    /// <summary>butler's arguments for sending (or previewing) a build.</summary>
    public static List<string> PushArguments(string folder, ItchSettings s, string channel, string version, bool preview)
    {
        var args = new List<string> { "--json", preview ? "push-preview" : "push", folder, Target(s.Target) + ":" + channel };
        if (!preview)
        {
            if (version.Trim().Length > 0) args.Add("--userversion=" + version.Trim());
            if (s.Hidden) args.Add("--hidden");
            if (s.IfChanged) args.Add("--if-changed");
        }
        return args;
    }

    /// <summary>What went wrong, in a line: butler's errors carry a whole Go stack trace after the first line.</summary>
    public static string Explain(string message)
    {
        string first = message.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        if (first.StartsWith("bailing out:")) first = first[12..].Trim();
        if (first.Contains("invalid key", StringComparison.OrdinalIgnoreCase) || first.Contains("(401)") || first.Contains("(403)")) return "itch.io didn't accept the sign-in (" + first + "). Sign in to itch.io again.";
        if (Regex.IsMatch(first, @"not found|\(404\)|invalid game", RegexOptions.IgnoreCase)) return "itch.io doesn't know that game (" + first + "). Check Set up…, or create the game on itch.io first.";
        return first.Length > 0 ? first : "butler stopped without saying why.";
    }
    /// <summary>One line of butler's --json output.</summary>
    public sealed record ButlerMessage(string Type, string Text, double? Progress, string? Uri, JsonElement? Value);
    public static ButlerMessage? ReadLine(string line)
    {
        line = line.Trim();
        if (!line.StartsWith('{')) return line.Length == 0 ? null : new("text", line, null, null, null);
        try
        {
            using var doc = JsonDocument.Parse(line); var r = doc.RootElement;
            string type = r.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            string text = r.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            double? progress = r.TryGetProperty("progress", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : null;
            string? uri = r.TryGetProperty("uri", out var u) ? u.GetString() : null;
            JsonElement? value = r.TryGetProperty("value", out var v) ? v.Clone() : null;
            return new(type, text, progress, uri, value);
        }
        catch (JsonException) { return new("text", line, null, null, null); }
    }
}

/// <summary>itch.io's account API (api.itch.io): who is signed in and the games they can upload to.</summary>
public sealed class ItchApi(HttpClient http, string api = "https://api.itch.io")
{
    public sealed record User(long Id, string Username, string DisplayName, string Url);
    public sealed record Game(long Id, string Title, string Url, string ShortText, string CoverUrl, string Classification, string Type, bool Published)
    {
        public string? Target => Itch.Target(Url);
    }
    async Task<JsonElement> Get(string key, string path, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, api.TrimEnd('/') + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.UserAgent.ParseAdd("ArcadiaStudio");
        using var response = await http.SendAsync(request, cancel);
        string body = await response.Content.ReadAsStringAsync(cancel);
        JsonElement root;
        try { root = JsonDocument.Parse(body).RootElement.Clone(); }
        catch (JsonException) { throw new ItchException((int)response.StatusCode, "itch.io answered in a way Arcadia Studio doesn't understand (" + (int)response.StatusCode + ")."); }
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            throw new ItchException((int)response.StatusCode, string.Join(" ", errors.EnumerateArray().Select(e => e.GetString())));
        if (!response.IsSuccessStatusCode) throw new ItchException((int)response.StatusCode, "itch.io said " + (int)response.StatusCode + ".");
        return root;
    }
    static string S(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    public async Task<User> ProfileAsync(string key, CancellationToken cancel = default)
    {
        var u = (await Get(key, "/profile", cancel)).GetProperty("user");
        return new(u.TryGetProperty("id", out var id) ? id.GetInt64() : 0, S(u, "username"), S(u, "display_name"), S(u, "url"));
    }
    public async Task<List<Game>> GamesAsync(string key, CancellationToken cancel = default)
    {
        var root = await Get(key, "/profile/games", cancel);
        if (!root.TryGetProperty("games", out var games) || games.ValueKind != JsonValueKind.Array) return [];
        return games.EnumerateArray().Select(g => new Game(g.TryGetProperty("id", out var id) ? id.GetInt64() : 0, S(g, "title"), S(g, "url"), S(g, "short_text"), S(g, "cover_url"), S(g, "classification"), S(g, "type"),
            g.TryGetProperty("published", out var p) && p.ValueKind == JsonValueKind.True)).ToList();
    }
}
public sealed class ItchException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
    /// <summary>The key works but isn't allowed this (a key from butler's sign-in can upload, but itch.io answers
    /// "api key does not permit `profile:me`" when it asks who you are).</summary>
    public bool Scope => !Invalid && (Message.Contains("scope", StringComparison.OrdinalIgnoreCase) || Message.Contains("not permit", StringComparison.OrdinalIgnoreCase) || Status == 403);
    /// <summary>itch.io doesn't accept the key at all: signed out on the website, revoked, or mistyped.</summary>
    public bool Invalid => Status == 401 || Message.Contains("invalid key", StringComparison.OrdinalIgnoreCase) || Message.Contains("invalid api key", StringComparison.OrdinalIgnoreCase);
}
