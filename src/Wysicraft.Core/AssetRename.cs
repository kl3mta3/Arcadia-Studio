using System.Text.RegularExpressions;
using Wysicraft.Models;
namespace Wysicraft.Core;

/// <summary>Renames a project image or sound: only the name, never the folder or the extension. Everything that
/// points at it by its resource ID is updated — control textures (row templates and tilemap sheets included),
/// change_texture actions, particle textures, Sound controls and play_sound actions — and the files that belong
/// to it move with it (animation .mcmeta, pixel editor .layers, Music maker .song, Sound effect maker .sfx).
/// Scripts are text and can build names at run time, so they are not rewritten; the ones that mention the old
/// name are returned so the editor can say where to look.</summary>
public static class AssetRename
{
    public sealed record Result(string NewPath, int References, List<string> ScriptsMentioningOldName);

    static readonly Regex NamePattern = new("^[a-z0-9_-]{1,64}$");
    public static bool ValidName(string stem) => NamePattern.IsMatch(stem);

    /// <summary>The part a person edits: "assets/dcc/sounds/hit.ogg" → "hit".</summary>
    public static string Stem(string path) => System.IO.Path.GetFileNameWithoutExtension(path);
    public static string Extension(string path) => System.IO.Path.GetExtension(path);
    public static string WithStem(string path, string stem) => path[..(path.LastIndexOf('/') + 1)] + stem + Extension(path);

    public static Result Rename(Project project, string oldPath, string newStem)
    {
        newStem = (newStem ?? "").Trim();
        if (!project.Assets.ContainsKey(oldPath)) throw new InvalidOperationException("No asset " + oldPath);
        bool sound = SoundAssets.IsSound(oldPath), image = oldPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        if (!sound && !image) throw new InvalidOperationException("Only images and sounds can be renamed.");
        if (!ValidName(newStem)) throw new InvalidOperationException("Names use lowercase letters, numbers, _ and -, up to 64 characters.");
        string newPath = WithStem(oldPath, newStem);
        if (newPath == oldPath) return new(oldPath, 0, []);
        if (project.Assets.ContainsKey(newPath)) throw new InvalidOperationException("There is already an asset called " + System.IO.Path.GetFileName(newPath) + ".");
        // A sound plays by its name without the extension, so name.ogg and name.mp3 would be the same sound.
        if (sound && SoundAssets.All(project).Any(p => p != oldPath && SoundAssets.Resource(p) == SoundAssets.Resource(newPath)))
            throw new InvalidOperationException("There is already a sound called " + newStem + ".");

        int references = 0;
        IEnumerable<Element> All(IEnumerable<Element> elements) => elements.SelectMany(e => new[] { e }.Concat(All(e.RowElements)));
        IEnumerable<VisualAction> Actions(Dictionary<string, UiEvent> events) => events.Values.SelectMany(v => v.Client.Actions.Concat(v.Server.Actions));
        if (image)
        {
            // References are matched the way the runtimes resolve them, so every spelling that reached this file follows it.
            string newResource = ResourceOf(project, newPath);
            bool Points(string value) => value.Length > 0 && TextureAssets.PathOf(project, value) == oldPath;
            foreach (var screen in project.Screens)
            {
                foreach (var e in All(screen.Elements))
                {
                    if (Points(e.Texture)) { e.Texture = newResource; references++; }
                    foreach (var a in Actions(e.Events)) if (a.Type == "change_texture" && Points(a.Value)) { a.Value = newResource; references++; }
                }
                foreach (var a in Actions(screen.Events)) if (a.Type == "change_texture" && Points(a.Value)) { a.Value = newResource; references++; }
            }
            foreach (var fx in project.Manifest.Particles) if (Points(fx.Texture)) { fx.Texture = newResource; references++; }
        }
        else
        {
            string oldId = SoundAssets.Resource(oldPath), newId = SoundAssets.Resource(newPath);
            foreach (var screen in project.Screens)
            {
                foreach (var e in All(screen.Elements))
                {
                    if (e.Type == "sound" && e.Sound == oldId) { e.Sound = newId; references++; }
                    foreach (var a in Actions(e.Events)) if (a.Type == "play_sound" && a.Value == oldId) { a.Value = newId; references++; }
                }
                foreach (var a in Actions(screen.Events)) if (a.Type == "play_sound" && a.Value == oldId) { a.Value = newId; references++; }
            }
        }

        // The file and what belongs to it.
        foreach (var suffix in new[] { "" }.Concat(image ? [".mcmeta", TextureAssets.LayersSuffix] : [SoundAssets.SongSuffix, SoundAssets.EffectSuffix]))
            if (project.Assets.Remove(oldPath + suffix, out var bytes)) project.Assets[newPath + suffix] = bytes;

        string oldStem = Stem(oldPath);
        var mention = new Regex("(?<![A-Za-z0-9_])" + Regex.Escape(oldStem) + "(?![A-Za-z0-9_])");
        var scripts = project.Scripts.Where(s => mention.IsMatch(s.Value)).Select(s => s.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
        return new(newPath, references, scripts);
    }

    /// <summary>The resource ID for a project image path: "assets/ns/textures/gui/image/a.png" → "ns:textures/gui/image/a.png".</summary>
    public static string ResourceOf(Project project, string path)
    {
        if (path.StartsWith("assets/textures/", StringComparison.Ordinal)) return TextureAssets.Resource(project.Manifest.Id, path[16..]);
        var parts = path.Split('/', 3);
        return parts[1] + ":" + parts[2];
    }
}
