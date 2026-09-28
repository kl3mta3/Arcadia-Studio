using Wysicraft.Models;
namespace Wysicraft.Core;

/// <summary>Which typeface a control draws its text in.
///
/// Minecraft draws its own fonts and can only use the three it ships with, so those stay the default. A web or
/// desktop app is an ordinary canvas: it can use any of the families every browser already has, or a font file
/// brought into the project. Both of those are advanced (web and desktop) and are reported for Minecraft the way
/// every other advanced tool is.</summary>
public static class Fonts
{
    /// <summary>The fonts Minecraft itself has.</summary>
    public static readonly string[] Minecraft = ["minecraft:default", "minecraft:uniform", "minecraft:alt"];

    /// <summary>Families every browser and desktop already has, so a project can look like something other than
    /// Minecraft without importing anything. The value after "web:" is the CSS family the runtime asks for.</summary>
    public static readonly (string Id, string Label, string Css)[] Builtin =
    [
        ("web:sans", "Sans (system UI)", "system-ui, \"Segoe UI\", Roboto, \"Helvetica Neue\", Arial, sans-serif"),
        ("web:serif", "Serif", "Georgia, \"Times New Roman\", serif"),
        ("web:mono", "Monospace", "\"Cascadia Mono\", Consolas, \"DejaVu Sans Mono\", monospace"),
        ("web:rounded", "Rounded", "\"Segoe UI Variable\", \"Nunito\", \"Trebuchet MS\", system-ui, sans-serif"),
        ("web:condensed", "Condensed", "\"Arial Narrow\", \"Roboto Condensed\", \"Segoe UI\", sans-serif"),
        ("web:display", "Display", "Impact, \"Haettenschweiler\", \"Arial Black\", sans-serif"),
        ("web:handwriting", "Handwriting", "\"Segoe Script\", \"Comic Sans MS\", cursive"),
    ];

    public const string Folder = "fonts/";
    public static readonly string[] Extensions = [".ttf", ".otf", ".woff2", ".woff"];
    /// <summary>A font file is small; anything this big is a mistake, and it has to travel inside an export.</summary>
    public const int MaxBytes = 8 * 1024 * 1024;

    public static bool IsMinecraft(string font) => Minecraft.Contains(font);
    public static bool IsBuiltin(string font) => Builtin.Any(f => f.Id == font);
    /// <summary>A font file in this project, rather than one of the named families.</summary>
    public static bool IsProjectFont(string font) => font.Contains(':') && font[(font.IndexOf(':') + 1)..].StartsWith(Folder);

    /// <summary>Where a project font's file lives: assets/&lt;id&gt;/fonts/&lt;name&gt;.</summary>
    public static string Path(string projectId, string fileName) => $"assets/{projectId}/{Folder}{fileName}";
    /// <summary>That path back as the resource ID a control stores.</summary>
    public static string Resource(string projectId, string assetPath) =>
        assetPath.StartsWith($"assets/{projectId}/") ? projectId + ":" + assetPath[$"assets/{projectId}/".Length..] : assetPath;

    /// <summary>Every font file in the project, as resource IDs.</summary>
    public static IEnumerable<string> InProject(Project project) =>
        project.Assets.Keys
            .Where(k => k.StartsWith($"assets/{project.Manifest.Id}/{Folder}") && Extensions.Any(x => k.EndsWith(x, StringComparison.OrdinalIgnoreCase)))
            .Select(k => Resource(project.Manifest.Id, k))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase);

    /// <summary>The asset path a resource ID points at, or null.</summary>
    public static string? FileOf(Project project, string font)
    {
        if (!IsProjectFont(font)) return null;
        int colon = font.IndexOf(':');
        string path = $"assets/{font[..colon]}/{font[(colon + 1)..]}";
        return project.Assets.ContainsKey(path) ? path : null;
    }

    /// <summary>The CSS font-family for a built-in, or null.</summary>
    public static string? CssFor(string font) => Builtin.FirstOrDefault(f => f.Id == font).Css;

    /// <summary>A stable CSS family name for a project font, used by both the export's @font-face and the runtime.</summary>
    public static string FamilyName(string font)
    {
        var name = font.Replace(':', '_').Replace('/', '_').Replace('.', '_');
        return "wys_" + new string([.. name.Where(c => char.IsLetterOrDigit(c) || c == '_')]);
    }

    /// <summary>What is wrong with a control's font, or null. A font that is not one of Minecraft's, not one of the
    /// built-in families and not a file in the project is a typo, and would silently fall back at runtime.</summary>
    public static string? Problem(Project project, string font)
    {
        if (font.Length == 0 || IsMinecraft(font) || IsBuiltin(font)) return null;
        if (!IsProjectFont(font)) return $"Unknown font \"{font}\": use one of Minecraft's, a built-in like web:sans, or a font imported into the project";
        if (FileOf(project, font) == null) return $"Missing font file for \"{font}\"";
        return null;
    }
}
