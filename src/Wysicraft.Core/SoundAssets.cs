using Wysicraft.Models;
namespace Wysicraft.Core;

/// <summary>Project sounds live at assets/&lt;project id&gt;/sounds/&lt;name&gt;.&lt;ext&gt; and play as "&lt;project id&gt;:&lt;name&gt;"
/// (the play_sound action and ctx.client.playSound). Minecraft only plays .ogg (Vorbis); web and desktop apps
/// also play .mp3, .wav and .m4a/.aac.</summary>
public static class SoundAssets
{
    public static readonly string[] Extensions = [".ogg", ".mp3", ".wav", ".m4a", ".aac"];
    public static bool IsSound(string path) => path.Contains("/sounds/") && Extensions.Any(x => path.EndsWith(x, StringComparison.OrdinalIgnoreCase));
    /// <summary>What the Music maker (name.ogg.song) and Sound effect maker (name.ogg.sfx) keep beside a sound so it can be
    /// opened and changed again. Editor-only: never exported.</summary>
    public const string SongSuffix = ".song", EffectSuffix = ".sfx";
    public static bool IsSidecar(string path) => (path.EndsWith(SongSuffix, StringComparison.Ordinal) || path.EndsWith(EffectSuffix, StringComparison.Ordinal)) && IsSound(path[..path.LastIndexOf('.')]);
    public static void RemoveWithSidecars(Project p, string path) { p.Assets.Remove(path); p.Assets.Remove(path + SongSuffix); p.Assets.Remove(path + EffectSuffix); }
    public static string Path(string projectId, string fileName) => "assets/" + projectId + "/sounds/" + fileName;
    /// <summary>"assets/ns/sounds/ui/pop.ogg" → "ns:ui/pop".</summary>
    public static string Resource(string path)
    {
        var parts = path.Split('/', 4); // assets, ns, sounds, rest
        string rest = parts[3]; int dot = rest.LastIndexOf('.');
        return parts[1] + ":" + (dot > 0 ? rest[..dot] : rest);
    }
    public static IEnumerable<string> All(Project p) => p.Assets.Keys.Where(IsSound).OrderBy(k => k, StringComparer.Ordinal);
    /// <summary>The project file for a sound resource, if the project has one.</summary>
    public static string? Find(Project p, string resource) => All(p).FirstOrDefault(path => Resource(path) == resource);
    /// <summary>Every sound named by play_sound actions.</summary>
    public static IEnumerable<(string Screen, string Element, string Sound)> Uses(Project p)
    {
        foreach (var s in p.Screens)
        {
            foreach (var (element, events) in new[] { ("", s.Events) }.Concat(s.Elements.Select(e => (e.Id, e.Events))))
                foreach (var ev in events.Values)
                    foreach (var a in ev.Client.Actions.Concat(ev.Server.Actions))
                        if (a.Type == "play_sound" && a.Value.Length > 0) yield return (s.Id, element, a.Value);
            // Sound controls
            foreach (var e in s.Elements.Where(e => e.Type == "sound" && e.Sound.Length > 0)) yield return (s.Id, e.Id, e.Sound);
        }
    }
}
