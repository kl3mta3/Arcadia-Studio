using Wysicraft.Models;
namespace Wysicraft.Core;

public static class TextureAssets
{
    /// <summary>Pixel editor layers, kept beside a PNG (name.png.layers). Editor-only: never exported.</summary>
    public const string LayersSuffix = ".layers";
    public static bool IsEditorOnly(string path) => path.EndsWith(".png" + LayersSuffix, StringComparison.Ordinal) || SoundAssets.IsSidecar(path);
    public static string Path(string projectId, string name, string elementType = "image") => "assets/" + projectId + "/textures/gui/" + elementType + "/" + name;
    public static string Resource(string projectId, string name, string elementType = "image") => projectId + ":textures/gui/" + elementType + "/" + name;
    public static bool TryGet(Project project, string resource, out byte[] bytes)
    {
        bytes = [];
        return PathOf(project, resource) is string path && project.Assets.TryGetValue(path, out bytes!);
    }
    /// <summary>The project asset a texture resource points at (the same fallbacks the runtimes use), or null.</summary>
    public static string? PathOf(Project project, string resource)
    {
        if (!Validation.Resource(resource)) return null;
        var parts = resource.Split(':', 2);
        if (project.Assets.ContainsKey("assets/" + parts[0] + "/" + parts[1])) return "assets/" + parts[0] + "/" + parts[1];
        if (parts[0] != project.Manifest.Id) return null;
        string name = parts[1].StartsWith("textures/gui/") ? parts[1][13..] : parts[1];
        if (name.StartsWith("image/")) name = name[6..];
        foreach (var path in new[] { Path(parts[0], name), "assets/" + parts[0] + "/textures/gui/" + name, "assets/textures/" + name })
            if (project.Assets.ContainsKey(path)) return path;
        return null;
    }
    public static Dictionary<string, byte[]> CanonicalAssets(Project project)
    {
        var result = new Dictionary<string, byte[]>();
        foreach (var (path, bytes) in project.Assets) {
            string target = path.StartsWith("assets/textures/") ? Path(project.Manifest.Id, path[16..]) : path;
            string prefix = "assets/" + project.Manifest.Id + "/textures/gui/";
            if (target.StartsWith(prefix) && !target[prefix.Length..].Contains('/')) target = Path(project.Manifest.Id, target[prefix.Length..]);
            Validation.SafePath(target);
            if ((SoundAssets.IsSound(target) || SoundAssets.IsSidecar(target)) && System.Text.RegularExpressions.Regex.IsMatch(target, "^assets/[a-z0-9_.-]+/sounds/[a-z0-9_/.-]+$")) { result[target] = bytes; continue; }
            // Fonts live beside sounds: not textures, and carried through to web and desktop exports as they are.
            if (System.Text.RegularExpressions.Regex.IsMatch(target, "^assets/[a-z0-9_.-]+/fonts/[a-z0-9_-]+\\.(ttf|otf|woff2|woff)$")) { result[target] = bytes; continue; }
            if (!System.Text.RegularExpressions.Regex.IsMatch(target, @"^assets/[a-z0-9_.-]+/textures/.+\.png(\.mcmeta|\.layers)?$")) throw new InvalidDataException("Invalid texture path: " + target);
            if (result.TryGetValue(target, out var existing) && !existing.SequenceEqual(bytes)) throw new InvalidDataException("Conflicting texture paths: " + target);
            result[target] = bytes;
        }
        return result;
    }
}
