using System.IO;
using System.Text.Json;
namespace Wysicraft.Designer;

/// <summary>ArcadiaStudio.exe --fake-butler …: answers like butler --json for the self-checks, so they never reach
/// itch.io. Every call is written to FAKE_BUTLER_LOG (its arguments, whether the key came through, and a push's files).
/// login: writes "fake-itch-key" to the -i file after announcing a sign-in address. push / push-preview: need that key.</summary>
static class FakeButler
{
    public const string Key = "fake-itch-key";
    static void Say(object message) { Console.Out.WriteLine(JsonSerializer.Serialize(message)); Console.Out.Flush(); }
    public static int Run(string[] args)
    {
        var list = args.Where(a => a != "--json").ToList();
        string? identity = null;
        int i = list.IndexOf("-i"); if (i >= 0 && i + 1 < list.Count) { identity = list[i + 1]; list.RemoveRange(i, 2); }
        string command = list.FirstOrDefault() ?? "";
        string key = Environment.GetEnvironmentVariable("BUTLER_API_KEY") ?? "";
        var files = new List<string>();
        if (command is "push" or "push-preview" && list.Count > 1 && Directory.Exists(list[1]))
            files = Directory.GetFiles(list[1], "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(list[1], f).Replace('\\', '/')).OrderBy(f => f).ToList();
        if (Environment.GetEnvironmentVariable("FAKE_BUTLER_LOG") is string log && log.Length > 0)
            File.AppendAllText(log, JsonSerializer.Serialize(new { command, args = list, keyGiven = key == Key, anyKey = key.Length > 0, files }) + "\n");
        switch (command)
        {
            case "login":
                if (identity == null) { Say(new { type = "error", message = "no -i" }); return 1; }
                Say(new { type = "login", uri = "https://itch.io/user/oauth?client_id=butler&scope=wharf&response_type=token&redirect_uri=http%3A%2F%2F127.0.0.1%3A9%2Foauth%2Fcallback" });
                Thread.Sleep(300);
                Directory.CreateDirectory(Path.GetDirectoryName(identity)!); File.WriteAllText(identity, Key);
                Say(new { type = "log", level = "info", message = "Authenticated successfully!" });
                Say(new { type = "result", value = new { status = "success" } });
                return 0;
            case "push": case "push-preview":
                if (key != Key) { Say(new { type = "error", message = "invalid api key" }); Console.Error.WriteLine("bailing out: invalid api key"); return 1; }
                if (list.Count < 3 || !list[2].StartsWith("smoke/game:")) { Say(new { type = "error", message = "game not found: " + (list.Count > 2 ? list[2] : "") }); return 1; }
                if (command == "push-preview") { Say(new { type = "log", level = "info", message = $"✓ Comparison vs build 1: {files.Count} new, 0 modified, 0 deleted, 0 unchanged" }); return 0; }
                foreach (var p in new[] { 0.25, 0.6, 1.0 }) { Say(new { type = "progress", progress = p, eta = 1, bps = 1000 }); Thread.Sleep(50); }
                Say(new { type = "log", level = "info", message = "Build is now processing, should be up in a bit." });
                return 0;
            case "version": Console.Out.WriteLine("v0.0.0-fake"); return 0;
            default: Say(new { type = "error", message = "fake butler: unknown command " + command }); return 1;
        }
    }
}
