using System.IO;
using Jint;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Wysicraft.Designer;

// The leaderboard scan: finds what holds a game's score, what means a run has ended, and what else is worth a column.
//
// It is Arcadia's own scan, not a copy of its rules: ScoringScan/scoringscan.js is the arcade server's file
// (server/lib/scoringscan.js), unchanged, run here in Jint on the files a publish would upload. So Scan in Publish to
// Arcadia suggests what the arcade itself would find. Copy the file again when it changes on the server.
sealed record ScanEvidence(string File, int Line, string Code)
{
    public string Text => File + (Line > 0 ? " line " + Line : "") + ": " + Code;
}
/// <summary>Something the scan found: a variable (and the field inside it, for a variable that holds JSON). Value is what
/// it equals when a run has ended (null: just "is true"); Key and Label are for a column.</summary>
sealed record ScanCandidate(string Variable, string Path, string? Value, string Key, string Label, double Points, bool Recommended, IReadOnlyList<ScanEvidence> Evidence)
{
    public string Source => Path.Length > 0 ? Variable + "." + Path : Variable;
}
sealed class ScoringScanResult
{
    /// <summary>Best first. The first score, and the triggers marked Recommended, are what Arcadia would use.</summary>
    public List<ScanCandidate> Score = [], Triggers = [], Stats = [];
    /// <summary>Arcadia would set the leaderboard up by itself from this: a clear score and a clear end of a run.</summary>
    public bool Confident;
    /// <summary>The game sends its own scores (Arcadia.submitScore), so nothing needs watching.</summary>
    public bool UsesSdk;
    public bool Empty => Score.Count == 0 && Triggers.Count == 0 && Stats.Count == 0;
}

static class ScoringScan
{
    // What the server does around the scan (lib/autoscore.js, analyzeGame): the entry page with its markup blanked out,
    // the local scripts it loads, and the project wherever it is (its own project.js, or inside a single-file page).
    const string Runner = """
        import { analyzeSources, confident, findWysicraftProject } from 'scoringscan';
        export function scan(entryPath, filesJson) {
          const files = JSON.parse(filesJson);
          const html = files[entryPath] || '';
          const texts = [{ file: entryPath, text: html.replace(/<(?!script)[^>]*>/g, (t) => ' '.repeat(t.length)) }];
          const srcs = [...html.matchAll(/<script[^>]*\ssrc=["']([^"']+)["']/gi)].map((m) => m[1]).filter((s) => !/^(https?:)?\/\//i.test(s) && !s.startsWith('/'));
          const base = entryPath.includes('/') ? entryPath.slice(0, entryPath.lastIndexOf('/') + 1) : '';
          let project = findWysicraftProject(html);
          for (const src of srcs.slice(0, 12)) {
            const text = files[(base + src.split(/[?#]/)[0]).replace(/(^|\/)\.\//g, '$1')];
            if (text == null || text.length > 5000000) continue;
            const found = findWysicraftProject(text);
            if (found) { project = found; continue; }
            if (/wysicraft-web\.js$/i.test(src)) continue;
            texts.push({ file: src, text });
          }
          const r = analyzeSources({ project, texts });
          return JSON.stringify({ usesSdk: r.usesSdk, confident: confident(r), suggestions: r.suggestions, recommended: r.recommended });
        }
        """;
    static readonly Lazy<string> Source = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("scoringscan.js") ?? throw new InvalidOperationException("The leaderboard scan is missing from this copy of Arcadia Studio.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    });
    /// <summary>The scan file as shipped, for checking it against the arcade's.</summary>
    internal static string Script => Source.Value;

    /// <summary>Scans the files of an upload (ArcadiaPackage.Files). Safe to call off the UI thread.</summary>
    public static ScoringScanResult Run(IReadOnlyDictionary<string, byte[]> files, string entry = "index.html") => Read(RunJson(files, entry));

    /// <summary>The scan's own answer, as JSON: { usesSdk, confident, suggestions, recommended }.</summary>
    internal static string RunJson(IReadOnlyDictionary<string, byte[]> files, string entry = "index.html")
    {
        // Pages and scripts only. The engine is left out (the server skips it too) unless it's where the project is.
        var texts = new Dictionary<string, string>();
        foreach (var (path, bytes) in files)
        {
            if (!path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) continue;
            string text = Encoding.UTF8.GetString(bytes);
            if (path.EndsWith("wysicraft-web.js", StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(text, @"window\.WYSICRAFT_PROJECT\s*=\s*")) continue;
            texts[path] = text;
        }
        try
        {
            var engine = new Jint.Engine(options => options.TimeoutInterval(TimeSpan.FromSeconds(30)).RegexTimeoutInterval(TimeSpan.FromSeconds(10)));
            engine.Modules.Add("scoringscan", Source.Value);
            engine.Modules.Add("run", Runner);
            var scan = engine.Modules.Import("run").Get("scan");
            return engine.Invoke(scan, entry, JsonSerializer.Serialize(texts)).AsString();
        }
        catch (Exception ex) when (ex is JintException or TimeoutException or RegexMatchTimeoutException or Acornima.ParseErrorException)
        {
            throw new InvalidOperationException("The scan couldn't read this game: " + ex.Message);
        }
    }

    static ScoringScanResult Read(string json)
    {
        var root = JsonNode.Parse(json)!;
        var result = new ScoringScanResult { UsesSdk = root["usesSdk"]?.GetValue<bool>() == true, Confident = root["confident"]?.GetValue<bool>() == true };
        static string Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out string? s) ? s : "";
        // What Arcadia would use: the first score, and these conditions (the strongest, plus any "won" flags).
        var recommended = new HashSet<string>();
        if (root["recommended"]?["trigger"]?["any"] is JsonArray any)
            foreach (var c in any) recommended.Add(Text(c?["variable"]) + "\n" + Text(c?["path"]) + "\n" + Text(c?["equals"]));
        List<ScanCandidate> List(string name, bool triggers)
        {
            var list = new List<ScanCandidate>();
            foreach (var item in root["suggestions"]?[name] as JsonArray ?? [])
            {
                // A project's variables only: a plain JavaScript global can't be chosen in the Publish window.
                if (item?["source"] is not JsonObject source || Text(source["kind"]) != "wysicraft" || Text(source["variable"]).Length == 0) continue;
                string variable = Text(source["variable"]), path = Text(source["path"]), equals = Text(item["equals"]);
                var evidence = (item["evidence"] as JsonArray ?? []).Where(e => e != null).Select(e => new ScanEvidence(Text(e!["file"]), e["line"] is JsonValue line && line.TryGetValue<int>(out int n) ? n : 0, Text(e["code"]))).ToList();
                list.Add(new ScanCandidate(variable, path, triggers && equals.Length > 0 ? equals : null, Text(item["key"]), Text(item["label"]),
                    item["score"] is JsonValue points && points.TryGetValue<double>(out double p) ? p : 0,
                    triggers ? recommended.Contains(variable + "\n" + path + "\n" + equals) : list.Count == 0 && name == "score", evidence));
            }
            return list;
        }
        result.Score = List("score", false); result.Triggers = List("trigger", true); result.Stats = List("stats", false);
        return result;
    }
}
