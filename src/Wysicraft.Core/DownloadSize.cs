using System.Text;
using Wysicraft.Models;
namespace Wysicraft.Core;

/// <summary>How big an export will be, and a warning when it is bigger than players should have to download. Web
/// games are held to 100 MB, since every player fetches them over their own connection; desktop apps, downloaded once,
/// to 500 MB. Advice only: nothing is ever refused for its size here.</summary>
public static class DownloadSize
{
    public const long WebBudget = 100L * 1024 * 1024, DesktopBudget = 500L * 1024 * 1024;
    /// <summary>What Electron itself adds to each desktop app it builds.</summary>
    public const long ElectronBytes = 100L * 1024 * 1024;
    /// <summary>A slow but ordinary connection, for saying how long the wait is.</summary>
    const double SlowBitsPerSecond = 10_000_000;

    /// <summary>The files an export carries: pictures, sounds, fonts and scripts, without the editor's own sidecars.</summary>
    public static long ContentBytes(Project p) =>
        p.Assets.Where(a => !TextureAssets.IsEditorOnly(a.Key)).Sum(a => (long)a.Value.Length)
        + p.Scripts.Sum(s => (long)Encoding.UTF8.GetByteCount(s.Value));

    /// <summary>About how big this format comes out. A single HTML file carries every file as base64 text, a third
    /// bigger; Electron adds itself to every app. Minecraft formats and unknown ones return null.</summary>
    public static long? Estimate(Project p, string format) => format switch
    {
        "web_folder" or "windows_app" => ContentBytes(p),
        "web_file" => ContentBytes(p) * 4 / 3,
        "electron" => ContentBytes(p) + ElectronBytes,
        _ => null
    };

    public static bool IsDesktop(string format) => format is "windows_app" or "electron";

    /// <summary>A plain sentence when the format is over its budget, else null.</summary>
    public static string? Warning(Project p, string format)
    {
        if (Estimate(p, format) is not long bytes) return null;
        long budget = IsDesktop(format) ? DesktopBudget : WebBudget;
        if (bytes <= budget) return null;
        var biggest = p.Assets.Where(a => !TextureAssets.IsEditorOnly(a.Key)).OrderByDescending(a => a.Value.Length).Take(3)
            .Select(a => System.IO.Path.GetFileName(a.Key) + " (" + Mb(a.Value.Length) + ")");
        string wait = Math.Round(bytes * 8 / SlowBitsPerSecond / 60, 1) + " minutes";
        return IsDesktop(format)
            ? $"This app comes to about {Mb(bytes)}, over the {Mb(budget)} a desktop download should stay under. At 10 Mbit/s it takes about {wait} to download. The biggest files: {string.Join(", ", biggest)}."
            : $"This web game comes to about {Mb(bytes)}, over the {Mb(budget)} a web game should stay under: every player downloads it over their own connection, and at 10 Mbit/s that is about {wait}. The biggest files: {string.Join(", ", biggest)}. Shorter or quieter sounds and smaller pictures help most; a desktop app can go to {Mb(DesktopBudget)}.";
    }

    /// <summary>For the validation list: a project too big for the web gets a note, whatever it is exported as.</summary>
    public static Issue? Advice(Project p)
    {
        if (p.Manifest.Target == "minecraft") return null;
        long bytes = ContentBytes(p);
        if (bytes <= WebBudget) return null;
        return new Issue("manifest", "", $"About {Mb(bytes)} of pictures, sounds and scripts: over the {Mb(WebBudget)} a web game should stay under (desktop apps can go to {Mb(DesktopBudget)}).", Advice: true);
    }

    public static string Mb(long bytes) => bytes >= 10L * 1024 * 1024 ? Math.Round(bytes / 1048576.0) + " MB" : Math.Round(bytes / 1048576.0, 1) + " MB";
}
