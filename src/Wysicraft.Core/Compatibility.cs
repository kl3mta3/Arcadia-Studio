using Wysicraft.Models;
namespace Wysicraft.Core;

/// <summary>Limits that differ between Minecraft and web/desktop apps. Minecraft's come from the game and its server;
/// the web ones only keep the editor and the player's computer comfortable.</summary>
public sealed record Limits(int ScreenSize, int Elements, int Screens, int MinTick, int ScriptOps, int ScriptBytes)
{
    /// <summary>ScriptBytes mirrors the Minecraft runtime, which refuses a larger script in three places
    /// (PackRepository.validate, Scripts.execute and ClientJavaScript.MAX_SCRIPT_BYTES). Raising it here alone would
    /// only move the refusal to the player's game, so it moves when the runtime does. The runtime caches parsed
    /// sources, so 256 KiB now costs less per event than 64 KiB did before caching: measured at about 7 ms, against
    /// 21 ms for a 60 KiB script that was re-parsed every event. Requires runtime 1.7.0.</summary>
    public static readonly Limits Minecraft = new(4096, 512, 128, 50, 128, 256 * 1024);
    /// <summary>Nothing in the web runtime caps a script: it is compiled with `new Function` like any other source.
    /// A megabyte is a stop for a runaway generator, not a budget anyone should have to author against.
    ///
    /// Elements is measured, not chosen: drawing plain controls costs about 0.63 ms per thousand and stays linear to
    /// 16,000 (10.7 ms, two thirds of a 60 fps frame), then collapses to 37 ms at 24,000. Controls with text cost
    /// about 2.6x that. So 16,000 is the last count the renderer carries, and Validation.Advice says so from
    /// <see cref="HeavyScreen"/> upwards rather than refusing. tests/web/elements.html re-measures it.</summary>
    public static readonly Limits Web = new(16384, 16000, 1000, 16, 10000, 1024 * 1024);
    /// <summary>Where a screen starts costing a real share of a frame to draw: 4,000 controls is 2.4 ms of plain
    /// panels, or 6.2 ms once they have text on them. Advice, not an error.</summary>
    public const int HeavyScreen = 4000;
    public static Limits For(Project p) => p.Manifest.Target == "minecraft" ? Minecraft : Web;
    /// <summary>A script's size as every limit measures it: UTF-8 bytes, which is what both runtimes read.</summary>
    public static int SizeOf(string source) => System.Text.Encoding.UTF8.GetByteCount(source);
    /// <summary>The scripts a pack would actually carry: unused ones are left out, so they cannot break an export.</summary>
    public static IEnumerable<KeyValuePair<string, string>> UsedScripts(Project p)
    {
        var used = p.Screens.Where(s => !s.IsComponent).SelectMany(s => s.Events.Values.Concat(s.Elements.SelectMany(e => e.Events.Values)))
            .SelectMany(e => new[] { e.Client.Script, e.Server.Script }).Where(s => s.Length > 0).ToHashSet(StringComparer.Ordinal);
        return p.Scripts.Where(s => used.Contains(s.Key));
    }
}

/// <summary>Where a project uses something Minecraft can't run: advanced (web and desktop) tools, or sizes past
/// Minecraft's limits. Worked out from the project each time, so it can never go stale; it takes about a millisecond
/// even for the biggest projects.</summary>
public static class Compatibility
{
    public sealed record Use(string Screen, string Element, string What);

    public static List<Use> MinecraftProblems(Project p)
    {
        var uses = new List<Use>(); var m = Limits.Minecraft;
        void Add(string screen, string element, string what) => uses.Add(new(screen, element, what));
        foreach (var (screen, element, sound) in SoundAssets.Uses(p))
            if (SoundAssets.Find(p, sound) is string file && !file.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)) Add(screen, element, $"plays {System.IO.Path.GetFileName(file)} (Minecraft only plays .ogg sounds)");
        if (p.Manifest.Inputs.Count > 0) Add("", "", $"{p.Manifest.Inputs.Count} input(s) in the Inputs window");
        foreach (var (path, source) in Limits.UsedScripts(p))
            if (Limits.SizeOf(source) > m.ScriptBytes) Add("", "", $"script {path} is {Limits.SizeOf(source) / 1024} KiB (Minecraft allows {m.ScriptBytes / 1024} KiB)");
        if (p.Screens.Count(s => !s.IsComponent) > m.Screens) Add("", "", $"{p.Screens.Count} screens (Minecraft allows {m.Screens})");
        foreach (var ui in p.Screens)
        {
            if (ui.Size.Width > m.ScreenSize || ui.Size.Height > m.ScreenSize) Add(ui.Id, "", $"screen size {ui.Size.Width} × {ui.Size.Height} (Minecraft allows up to {m.ScreenSize})");
            if (ui.Elements.Count > m.Elements) Add(ui.Id, "", $"{ui.Elements.Count} controls (Minecraft allows {m.Elements})");
            if (ui.TickInterval != 0 && ui.TickInterval < m.MinTick) Add(ui.Id, "", $"tick interval {ui.TickInterval} ms (Minecraft's shortest is {m.MinTick} ms)");
            if (ui.Gravity != 0) Add(ui.Id, "", "physics gravity");
            foreach (var a in ui.Animations) Add(ui.Id, "", $"animation \"{a.Id}\"");
            foreach (var g in ui.StateGraphs) Add(ui.Id, g.Target, $"state graph \"{g.Id}\"");
            if (ui.Shader.Length > 0) Add(ui.Id, "", $"shader \"{ui.Shader}\"");
            foreach (var name in ui.Events.Keys.Where(Registry.AdvancedScreenEvents.Contains)) Add(ui.Id, "", $"{name} event");
            foreach (var e in ui.Elements)
            {
                if (Registry.Controls.TryGetValue(e.Type, out var spec) && spec.Advanced) Add(ui.Id, e.Id, spec.DisplayName.ToLowerInvariant() + " control");
                // Minecraft draws its own fonts and has only the three it ships with.
                if (e.Font.Length > 0 && !Fonts.IsMinecraft(e.Font))
                    Add(ui.Id, e.Id, Fonts.IsBuiltin(e.Font) ? $"font \"{e.Font}\" (Minecraft has only {string.Join(", ", Fonts.Minecraft)})" : $"imported font \"{e.Font}\"");
                if (e.Body.Length > 0) Add(ui.Id, e.Id, e.Body + " physics body");
                if (e.Input.Length > 0) Add(ui.Id, e.Id, $"presses input \"{e.Input}\"");
                if (e.Bounds.Width > m.ScreenSize || e.Bounds.Height > m.ScreenSize) Add(ui.Id, e.Id, $"size {e.Bounds.Width:0} × {e.Bounds.Height:0} (Minecraft allows up to {m.ScreenSize})");
                foreach (var name in e.Events.Keys.Where(Registry.AdvancedElementEvents.Contains)) Add(ui.Id, e.Id, $"{name} event");
            }
            // The server sends each screen to players compressed; one that is too big would fail to open in-game.
            if (!ui.IsComponent && ui.Elements.Count > 50)
            {
                var size = NetworkSize.Of(ui);
                if (size.Packed > NetworkSize.MaxPacked || size.Json > NetworkSize.MaxJsonBytes) Add(ui.Id, "", $"too large to send to players ({size.Packed / 1000} kB compressed, limit {NetworkSize.MaxPacked / 1000} kB)");
            }
        }
        return uses;
    }
    /// <summary>Stops a Minecraft export carrying a script the game's runtime would refuse, whatever the project's
    /// target says: the web export builds from the same pack, so the pack itself cannot decide this.</summary>
    public static void RequireMinecraftScripts(Project p)
    {
        foreach (var (path, source) in Limits.UsedScripts(p))
            if (Limits.SizeOf(source) > Limits.Minecraft.ScriptBytes)
                throw new System.IO.InvalidDataException($"{path} is {Limits.SizeOf(source) / 1024} KiB. Minecraft runs scripts up to {Limits.Minecraft.ScriptBytes / 1024} KiB; split it, or export for web and desktop.");
    }
    public static string Describe(Use u) => (u.Screen.Length == 0 ? "Project" : "Screen " + u.Screen) + (u.Element.Length > 0 ? " › " + u.Element : "") + ": " + u.What;
    public static string TargetName(string target) => target switch { "minecraft" => "Minecraft", "web" => "Web & desktop", _ => "Minecraft and web & desktop" };
}
