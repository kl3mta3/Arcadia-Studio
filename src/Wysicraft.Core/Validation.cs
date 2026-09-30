using System.Text.RegularExpressions;
using Wysicraft.Models;
namespace Wysicraft.Core;

/// <summary>MinecraftOnly: fine for web and desktop apps, but blocks Minecraft exports.
/// Advice: blocks nothing anywhere — a note about something that will cost the player, not a mistake.</summary>
public record Issue(string Ui, string Element, string Message, bool MinecraftOnly = false, bool Advice = false)
{
    public override string ToString() => (Advice ? "[Advice] " : MinecraftOnly ? "[Minecraft only] " : "") + $"{Ui}/{Element}: {Message}";
}
public static partial class Validation
{
    public static bool Id(string s) => s != null && Regex.IsMatch(s, "^[a-z][a-z0-9_]{0,63}$");
    public static bool Variable(string s) => s != null && Regex.IsMatch(s, "^[a-zA-Z_][a-zA-Z0-9_]{0,63}$");
    public static bool Resource(string s) => Regex.IsMatch(s, "^[a-z0-9_.-]+:[a-z0-9_./-]+$") && !s.Contains("..");
    public static bool Version(string s) => Regex.IsMatch(s, @"^\d+\.\d+\.\d+$") && System.Version.TryParse(s, out _);
    public static string SafePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 240 || path.Contains('\\') || path.Contains(':') || path.StartsWith('/') || path.Split('/').Any(s => s is "" or "." or ".." || s.EndsWith('.') || s.EndsWith(' ') || s.Any(char.IsControl))) throw new InvalidDataException($"Unsafe path: {path}");
        return path;
    }
    /// <summary>Problems that block every export and Preview.</summary>
    public static List<Issue> Errors(Project project) => Check(project).Where(i => !i.MinecraftOnly).ToList();
    /// <summary>Things worth saying that block nothing: kept apart from Check so no export path can trip over them.
    /// The editor shows these alongside the real problems, marked as advice.</summary>
    public static List<Issue> Advice(Project project)
    {
        List<Issue> notes = [];
        // A script naming a picture or sound by an old project ID (after Project settings → Id changed): the file is
        // now under the new ID, so the script would find nothing.
        foreach (var (path, source) in Limits.UsedScripts(project))
            foreach (var old in OldIdMentions(project, source))
                notes.Add(new("manifest", "", $"{path} names \"{old.Old}\", but that file is now \"{old.Now}\" (the project's ID changed). Change the script to the new name.", Advice: true));
        if (project.Manifest.Target == "minecraft") return notes; // Minecraft has its own, much lower, hard limits
        if (DownloadSize.Advice(project) is Issue size) notes.Add(size);
        // Kept script state runs a script's top level once: code there that works the screen stops happening per event.
        if (project.Manifest.Target == "web" && project.Manifest.KeepScriptState)
            foreach (var (path, source) in Limits.UsedScripts(project))
                if (ScriptShape.TopLevelCtxLine(source) is int line and > 0)
                    notes.Add(new("manifest", "", $"{path} line {line} uses ctx or ui outside a function. Scripts keep their variables between events in this project, so that line runs once, on the first event, not on every event. Move it into the event's function, or turn off \"Scripts keep their variables between events\" in Project settings.", Advice: true));
        foreach (var ui in project.Screens.Where(s => s.Elements.Count > Limits.HeavyScreen))
        {
            // By what actually draws text, not by whether Text is set: a panel carries a default Text it never shows.
            int text = ui.Elements.Count(e => e.Type is "label" or "button" or "textbox" or "checkbox" or "slider" or "dropdown" or "progress" or "item_list");
            // The measured cost: about 0.6 ms per thousand plain controls, about 1.6 ms per thousand with text.
            double cost = (ui.Elements.Count - text) * 0.00063 + text * 0.00160;
            notes.Add(new(ui.Id, "", $"{ui.Elements.Count} controls: drawing this screen costs about {cost:0.0} ms a frame, {Math.Round(100 * cost / 16.7)}% of a 60 fps frame. Controls with text cost roughly 2.6x a plain one.", Advice: true));
        }
        return notes;
    }
    static readonly Regex QuotedResource = new("['\"`]([a-z0-9_.-]+):([a-z0-9_./-]+)['\"`]");
    /// <summary>Quoted "namespace:path" names in a script that aren't the project's (nor Minecraft's) but would be a
    /// picture or sound of the project under its own ID.</summary>
    public static IEnumerable<(string Old, string Now)> OldIdMentions(Project project, string source)
    {
        string id = project.Manifest.Id; var seen = new HashSet<string>();
        foreach (Match m in QuotedResource.Matches(source))
        {
            string ns = m.Groups[1].Value, rest = m.Groups[2].Value;
            if (ns == id || ns == "minecraft" || !seen.Add(ns + ":" + rest)) continue;
            string now = id + ":" + rest;
            if (SoundAssets.Find(project, now) != null || (TextureAssets.PathOf(project, now) is string file && project.Assets.ContainsKey(file))) yield return (ns + ":" + rest, now);
        }
    }
    /// <summary>Everything, including what only blocks Minecraft exports. Minecraft exports use this.</summary>
    public static List<Issue> Check(Project project)
    {
        List<Issue> errors = []; void Add(string ui, string id, string msg) => errors.Add(new(ui, id, msg));
        var limits = Limits.Web; // hard caps; Minecraft's limits are checked below by target
        var m = project.Manifest;
        if (!Id(m.Id)) Add("manifest", "", "Invalid pack ID");
        if (m.SchemaVersion != 1) Add("manifest", "", "Unsupported schema version");
        if (!Version(m.Version) || !Version(m.RuntimeVersion)) Add("manifest", "", "Versions must be major.minor.patch");
        else if (System.Version.Parse(m.RuntimeVersion) > System.Version.Parse(RuntimeInfo.Version)) Add("manifest", "", "Minimum runtime exceeds " + RuntimeInfo.Version);
        if (!project.Screens.Any(s => s.Id == m.DefaultUi && !s.IsComponent)) Add("manifest", "", "Default UI does not exist");
        if (m.Target is not ("minecraft" or "web" or "both")) Add("manifest", "", "Made for must be minecraft, web or both");
        if (m.GameVariables.Count > 256) Add("manifest", "", "A game has at most 256 game variables");
        foreach (var (name, value) in m.GameVariables)
        {
            if (!Variable(name)) Add("manifest", "", "Invalid game variable name: " + name);
            if (value.Length > 4096) Add("manifest", "", $"Game variable {name}'s starting value is over 4096 characters");
        }
        foreach (var name in m.SavedVariables.Where(n => !m.GameVariables.ContainsKey(n))) Add("manifest", "", $"Saved between visits names {name}, which isn't a game variable");
        if (project.Screens.Count > limits.Screens) Add("manifest", "", $"At most {limits.Screens} screens");
        CheckInputs(); CheckParticles(); CheckShaders();
        HashSet<string> uis = []; var templates=project.Screens.SelectMany(s=>s.Elements).Where(e=>e.RowTemplate.Length>0).Select(e=>e.RowTemplate).ToHashSet();
        foreach (var ui in project.Screens)
        {
            if (!Id(ui.Id) || !uis.Add(ui.Id)) Add(ui.Id, "", "Invalid or duplicate UI ID");
            if (ui.SchemaVersion != 1) Add(ui.Id, "", "Unsupported schema version");
            if (ui.Size.Width < 16 || ui.Size.Height < 16 || ui.Size.Width > limits.ScreenSize || ui.Size.Height > limits.ScreenSize || ui.Elements.Count > limits.Elements) Add(ui.Id, "", $"Screens are 16–{limits.ScreenSize} pixels each way with at most {limits.Elements} controls");
            if (ui.TickInterval != 0 && (ui.TickInterval < limits.MinTick || ui.TickInterval > 60000)) Add(ui.Id, "", $"Tick interval must be 0 (off) or {limits.MinTick}–60000 milliseconds (Minecraft: 50 or more)");
            CheckAdvancedScreen(ui);
            if (ui.KeyRepeat != 0 && ui.KeyRepeat is < 50 or > 2000) Add(ui.Id, "", "Key repeat must be 0 (held keys don't repeat) or 50–2000 milliseconds");
            try { foreach(var group in ui.GroupParents.Keys.Concat(ui.Elements.Select(e=>e.LayerGroup))) { foreach(var part in LayerGroups.Path(ui,group))if(part.Length>64 || part.Any(char.IsControl))throw new InvalidDataException("Invalid group name"); } } catch(Exception ex) { Add(ui.Id,"",ex.Message); }
            if(ui.IsComponent && ui.Events.Count>0)Add(ui.Id,"","Component sources use control events, not screen open/close events");
            if(ui.IsComponent && ui.ComponentInstances.Count>0)Add(ui.Id,"","Linked components cannot be nested");
            var instanceRoots=new HashSet<string>();
            foreach(var instance in ui.ComponentInstances) {
                if(!instanceRoots.Add(instance.Root) || !ui.Elements.Any(e=>e.Id==instance.Root && e.Type=="panel"))Add(ui.Id,instance.Root,"Invalid or missing component root; detach the link or undo deletion");
                if(instance.Ids.Count>512 || instance.Ids.Values.Distinct().Count()!=instance.Ids.Count || instance.Ids.Values.Any(id=>!Id(id)||id==instance.Root))Add(ui.Id,instance.Root,"Invalid component ID mapping");
                if(!project.Screens.Any(s=>s.Id==instance.Source && s.IsComponent))Add(ui.Id,instance.Root,"Missing component source");
            }
            HashSet<string> ids = [];
            foreach (var e in ui.Elements)
            {
                if (!Id(e.Id) || !ids.Add(e.Id)) Add(ui.Id, e.Id, "Invalid or duplicate element ID");
                if (!Registry.Controls.ContainsKey(e.Type)) Add(ui.Id, e.Id, "Unknown control type: " + e.Type);
                foreach (var b in e.Behaviours.Where(b => Behaviours.All.All(x => x.Id != b))) Add(ui.Id, e.Id, "Unknown component: " + b);
                if (!double.IsFinite(e.Bounds.X + e.Bounds.Y + e.Bounds.Width + e.Bounds.Height) || e.Bounds.Width < 1 || e.Bounds.Height < 1 || e.Bounds.Width > limits.ScreenSize || e.Bounds.Height > limits.ScreenSize) Add(ui.Id, e.Id, "Invalid bounds");
                if(e.HorizontalAnchor is not ("left" or "center" or "right" or "stretch") || e.VerticalAnchor is not ("top" or "center" or "bottom" or "stretch") || !double.IsFinite(e.MinWidth+e.MinHeight+e.RowTemplateWidth) || e.MinWidth < 1 || e.MinWidth > limits.ScreenSize || e.MinHeight < 1 || e.MinHeight > limits.ScreenSize || e.RowTemplateWidth < 0 || e.RowTemplateWidth > limits.ScreenSize) Add(ui.Id,e.Id,"Invalid anchors or minimum size");
                if (e.Opacity < 0 || e.Opacity > 1 || e.FontScale <= 0 || e.FontScale > 8 || e.Maximum <= e.Minimum) Add(ui.Id, e.Id, "Invalid appearance or value range");
                if (!Resource(e.Font) || !double.IsFinite(e.CornerRadius) || e.CornerRadius < 0 || e.CornerRadius > 128) Add(ui.Id, e.Id, "Invalid font resource or corner radius (0–128)");
                else if (Fonts.Problem(project, e.Font) is string fontProblem) Add(ui.Id, e.Id, fontProblem);
                if (!double.IsFinite(e.BorderWidth + e.ShadowOpacity + e.ShadowOffsetX + e.ShadowOffsetY + e.ShadowBlur) || e.BorderWidth is < 0 or > 32 || e.ShadowOpacity is < 0 or > 1 || Math.Abs(e.ShadowOffsetX) > 64 || Math.Abs(e.ShadowOffsetY) > 64 || e.ShadowBlur is < 0 or > 16) Add(ui.Id, e.Id, "Invalid border/shadow settings");
                foreach (var color in new[] { e.Foreground, e.Background, e.BorderColor, e.ShadowColor }) if (!Regex.IsMatch(color, "^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$")) Add(ui.Id, e.Id, "Color must be #RRGGBB or #AARRGGBB");
                try { ContainerTree.Ancestors(ui,e).ToArray(); } catch(Exception ex) { Add(ui.Id,e.Id,ex.Message); }
                foreach (string condition in new[] { e.VisibleIf, e.EnabledIf }) try { Expressions.Evaluate(condition, ui.Variables); } catch (FormatException ex) { Add(ui.Id, e.Id, ex.Message); }
                if (e.Texture != "") { if (!Resource(e.Texture)) Add(ui.Id, e.Id, "Invalid texture resource"); else if (e.Texture.StartsWith(m.Id + ":") && !TextureAssets.TryGet(project, e.Texture, out _)) Add(ui.Id, e.Id, "Missing texture asset"); }
                if (e.Type == "item" && !(templates.Contains(ui.Id) && e.Item=="${row.item}") && !Resource(e.Item)) Add(ui.Id, e.Id, "Invalid item identifier");
                if(e.RowHeight<24 || e.RowHeight>128 || e.PrimaryLabel.Length>24 || e.SecondaryLabel.Length>24) Add(ui.Id,e.Id,"Row height must be 24–128; button labels at most 24 characters");
                if(e.RowTemplate.Length>0 && project.Screens.Any(s=>s.Id==e.RowTemplate && s.IsComponent))Add(ui.Id,e.Id,"Use a row template screen rather than a component source");
                if(e.Type=="item_list") try { ItemRows.Parse(e.Value); } catch(Exception ex) { Add(ui.Id,e.Id,ex.Message); }
                if(e.Type=="item_list" && (e.RowTemplate.Length>0 || e.RowElements.Count>0)) try { RowTemplates.Check(RowTemplates.Resolve(project,e)); } catch(Exception ex) { Add(ui.Id,e.Id,ex.Message); }
                if (e.Type.StartsWith("lb_")) Add(ui.Id, e.Id, "Leaderboard widgets go on leaderboard pages (Advanced → Create leaderboard), not on the game's screens");
                if (e.Type == "slots")
                {
                    if (!Registry.SlotKinds.Contains(e.SlotKind)) Add(ui.Id, e.Id, "Item slots hold player, storage, crafting or result");
                    if (e.Columns is < 1 or > 9 || e.Rows is < 1 or > 6) Add(ui.Id, e.Id, "Item slots are 1–9 columns by 1–6 rows");
                    else if (e.SlotKind == "player" && (e.SlotStart < 0 || e.SlotStart + e.Columns * e.Rows > 36)) Add(ui.Id, e.Id, "Player slots are 0–35 (0–8 is the hotbar): start + columns × rows can't pass 36");
                    else if (e.SlotKind == "crafting" && (e.Columns > 3 || e.Rows > 3)) Add(ui.Id, e.Id, "A crafting grid is at most 3 × 3");
                    else if (e.SlotKind == "result" && (e.Columns != 1 || e.Rows != 1)) Add(ui.Id, e.Id, "A crafting result is one slot");
                    if (ContainerTree.Ancestors(ui, e).Any(p => p.Type == "scroll_panel")) Add(ui.Id, e.Id, "Item slots can't be inside a scroll panel");
                }
                CheckEvents(ui, e.Id, e.Events, Registry.Controls.GetValueOrDefault(e.Type)?.Events ?? []);
                CheckNewControls(ui, e);
            }
            var slotControls = ui.Elements.Where(e => e.Type == "slots").ToList();
            if (slotControls.Count(e => e.SlotKind == "crafting") > 1 || slotControls.Count(e => e.SlotKind == "result") > 1) Add(ui.Id, "", "A screen has at most one crafting grid and one crafting result");
            if (slotControls.Any(e => e.SlotKind == "result") && !slotControls.Any(e => e.SlotKind == "crafting")) Add(ui.Id, "", "A crafting result needs a crafting grid on the same screen");
            if (slotControls.Count > 0 && ui.Responsive) Add(ui.Id, "", "Screens with item slots use a fixed layout: turn off Responsive layout");
            CheckEvents(ui, "", ui.Events, [.. Registry.ScreenEvents, .. Registry.AdvancedScreenEvents]);
            // Tick and Key fire on the player's screen only; nothing is sent to the server for them.
            foreach (var clientOnly in new[] { "tick", "key" })
                if (ui.Events.TryGetValue(clientOnly, out var ce) && (ce.Server.Actions.Count > 0 || ce.Server.Script != "")) Add(ui.Id, "", $"The {clientOnly} event runs only on the player's screen: use Client actions or a client script");
        }
        foreach (var path in project.Scripts.Keys.Concat(project.Assets.Keys)) try { SafePath(path); } catch (Exception ex) { Add("files", "", ex.Message); }
        // Minecraft can't run advanced tools or go past its own limits: an error when the project is made for
        // Minecraft, a Minecraft-only problem when it's made for both, and fine when it's web and desktop only.
        if (m.Target != "web")
            foreach (var use in Compatibility.MinecraftProblems(project))
                errors.Add(new(use.Screen.Length > 0 ? use.Screen : "manifest", use.Element, "Not in Minecraft: " + use.What, MinecraftOnly: m.Target == "both"));
        return errors;

        void CheckInputs()
        {
            var names = new HashSet<string>();
            foreach (var input in m.Inputs)
            {
                if (!Variable(input.Name) || !names.Add(input.Name)) Add("inputs", input.Name, "Input names must be unique, using letters, digits and _");
                if (input.Keys.Count + input.Buttons.Count > 16 || input.Keys.Any(k => !KeyNames.Contains(k)) || input.Buttons.Any(b => !Registry.GamepadButtons.Contains(b))) Add("inputs", input.Name, "Unknown key or gamepad button");
                if (input.Axis.Length > 0 && !Registry.GamepadAxes.Contains(input.Axis)) Add("inputs", input.Name, "Unknown gamepad stick axis");
                if (input.Touch.Length > 0 && !Registry.TouchActions.Contains(input.Touch)) Add("inputs", input.Name, "Unknown touch action");
                if (input.Pad is < -1 or > 3) Add("inputs", input.Name, "Gamepad is -1 (any) or 0-3");
            }
            if (m.Inputs.Count > 128) Add("inputs", "", "At most 128 inputs");
        }
        void CheckParticles()
        {
            var names = new HashSet<string>();
            foreach (var effect in m.Particles)
            {
                if (!names.Add(effect.Id)) Add("particles", effect.Id, "Particle effect names must be unique");
                try { effect.Check(); } catch (InvalidDataException ex) { Add("particles", effect.Id, ex.Message); }
                if (effect.Shape == "texture" && effect.Texture.Length > 0 && !TextureAssets.TryGet(project, effect.Texture, out _)) Add("particles", effect.Id, "Missing texture: " + effect.Texture);
            }
            foreach (var name in m.CollisionLayers) if (name.Length > 24) Add("particles", "", "Collision layer names are at most 24 characters");
            if (m.CollisionLayers.Count > 16) Add("particles", "", "There are 16 collision layers");
            if (m.Particles.Count > Particles.MaxPerProject) Add("particles", "", $"At most {Particles.MaxPerProject} particle effects");
        }
        void CheckAdvancedScreen(UiDefinition ui)
        {
            if (!double.IsFinite(ui.Gravity) || Math.Abs(ui.Gravity) > 100000) Add(ui.Id, "", "Gravity must be between -100000 and 100000");
            var ids = new HashSet<string>();
            foreach (var a in ui.Animations)
            {
                if (!Id(a.Id) || !ids.Add(a.Id)) Add(ui.Id, "", "Animation IDs must be unique lowercase IDs: " + a.Id);
                if (a.Duration is < 1 or > 600000 || a.Tracks.Count > 256) Add(ui.Id, "", $"Animation {a.Id}: length 1–600000 ms, at most 256 tracks");
                foreach (var t in a.Tracks)
                {
                    if (!ui.Elements.Any(e => e.Id == t.Target)) Add(ui.Id, "", $"Animation {a.Id}: missing control {t.Target}");
                    if (t.Property is not ("x" or "y" or "width" or "height" or "opacity")) Add(ui.Id, "", $"Animation {a.Id}: can't animate {t.Property}");
                    if (t.Keys.Count > 1024 || t.Keys.Any(k => k.Time < 0 || k.Time > a.Duration || !double.IsFinite(k.Value) || k.Ease is not ("linear" or "ease_in" or "ease_out" or "ease_in_out" or "step"))) Add(ui.Id, "", $"Animation {a.Id}: keyframes must be inside the animation, with a known easing");
                }
            }
            if (ui.Animations.Count > 128) Add(ui.Id, "", "At most 128 animations per screen");
            CheckStateGraphs(ui);
            if (ui.Shader.Length > 0 && !m.Shaders.Any(s => s.Id == ui.Shader)) Add(ui.Id, "", $"No shader called \"{ui.Shader}\"");
        }
        // Shaders are checked for shape, not compiled: only a GPU can say whether GLSL links, and the runtime already
        // drops one that won't rather than losing the screen. This catches the mistakes that are obvious without one.
        void CheckShaders()
        {
            if (m.Shaders.Count > 32) Add("manifest", "", "At most 32 shaders");
            var shaderIds = new HashSet<string>();
            foreach (var shader in m.Shaders)
            {
                if (!Id(shader.Id) || !shaderIds.Add(shader.Id)) Add("shaders", shader.Id, "Shader IDs must be unique lowercase IDs");
                if (shader.Source.Length > 64 * 1024) Add("shaders", shader.Id, "A shader is at most 64 KiB");
                else if (shader.Source.Trim().Length > 0)
                {
                    if (!shader.Source.Contains("void main")) Add("shaders", shader.Id, "A fragment shader needs a void main()");
                    if (!shader.Source.TrimStart().StartsWith("#version 300 es")) Add("shaders", shader.Id, "Start the shader with \"#version 300 es\" (WebGL2 uses GLSL ES 3.00)");
                }
            }
        }
        void CheckStateGraphs(UiDefinition ui)
        {
            if (ui.StateGraphs.Count > 64) Add(ui.Id, "", "At most 64 state graphs per screen");
            var graphIds = new HashSet<string>();
            foreach (var g in ui.StateGraphs)
            {
                if (!Id(g.Id) || !graphIds.Add(g.Id)) Add(ui.Id, "", "State graph IDs must be unique lowercase IDs: " + g.Id);
                var target = ui.Elements.FirstOrDefault(e => e.Id == g.Target);
                if (target == null) Add(ui.Id, "", $"State graph {g.Id}: missing control {g.Target}");
                else if (target.Type != "sprite") Add(ui.Id, target.Id, $"State graph {g.Id} drives {g.Target}, which is a {target.Type}, not a sprite");
                if (g.States.Count is 0 or > 64) Add(ui.Id, "", $"State graph {g.Id}: 1–64 states");
                // The clips the target actually has, so a graph can't name one that was renamed away.
                var clips = new HashSet<string>();
                if (target != null) try { foreach (var c in SpriteClips.Parse(target.Clips)) clips.Add(c.Key); } catch (FormatException) { }
                var names = new HashSet<string>();
                foreach (var s in g.States)
                {
                    if (!Variable(s.Name) || !names.Add(s.Name)) Add(ui.Id, "", $"State graph {g.Id}: state names must be unique: " + s.Name);
                    if (s.Clip.Length > 0 && clips.Count > 0 && !clips.Contains(s.Clip)) Add(ui.Id, "", $"State graph {g.Id}: state {s.Name} plays clip \"{s.Clip}\", which {g.Target} does not have");
                    if (s.Transitions.Count > 16) Add(ui.Id, "", $"State graph {g.Id}: at most 16 ways out of state {s.Name}");
                    foreach (var t in s.Transitions)
                    {
                        if (t.When.Length > 512) Add(ui.Id, "", $"State graph {g.Id}: a condition is at most 512 characters");
                        if (t.Priority is < -1000 or > 1000) Add(ui.Id, "", $"State graph {g.Id}: priority is -1000 to 1000");
                    }
                }
                if (g.Start.Length > 0 && !names.Contains(g.Start)) Add(ui.Id, "", $"State graph {g.Id}: it starts in \"{g.Start}\", which is not one of its states");
                foreach (var s in g.States) foreach (var t in s.Transitions)
                    if (!names.Contains(t.To)) Add(ui.Id, "", $"State graph {g.Id}: state {s.Name} goes to \"{t.To}\", which is not one of its states");
            }
        }
        void CheckNewControls(UiDefinition ui, Element e)
        {
            if (e.Type == "sprite")
            {
                if (e.FrameWidth is < 1 or > 4096 || e.FrameHeight is < 1 or > 4096) Add(ui.Id, e.Id, "Sprite frames are 1–4096 pixels");
                try { SpriteClips.Parse(e.Clips); } catch (FormatException ex) { Add(ui.Id, e.Id, ex.Message); }
            }
            if (e.Type == "shape" && !Shapes.Names.Contains(e.Shape)) Add(ui.Id, e.Id, "Unknown shape: " + e.Shape);
            if (e.Type == "sound")
            {
                if (e.Sound.Length > 0 && !Resource(e.Sound)) Add(ui.Id, e.Id, "Sound must be a sound ID like " + m.Id + ":click or minecraft:ui.button.click");
                if (e.Delay is < 0 or > 3600000 || !double.IsFinite(e.Volume) || e.Volume is < 0 or > 1 || e.Repeat is < 1 or > 1000) Add(ui.Id, e.Id, "Sound: delay 0–3600000 ms, volume 0–1, play 1–1000 times");
            }
            if (e.Body is not ("" or "static" or "dynamic" or "kinematic")) Add(ui.Id, e.Id, "Body must be static, dynamic or kinematic");
            if (e.Collider is not ("box" or "circle" or "polygon")) Add(ui.Id, e.Id, "Collider must be box, circle or polygon");
            if (e.Collider == "polygon" && (e.Body.Length > 0 || e.Type == "collider") && (e.ColliderPoints.Count is < 3 or > 256 || e.ColliderPoints.Any(p => !double.IsFinite(p.X + p.Y) || Math.Abs(p.X) > limits.ScreenSize || Math.Abs(p.Y) > limits.ScreenSize)))
                Add(ui.Id, e.Id, "A polygon collider needs 3–256 points; draw it in the Collider editor");
            else if (e.Collider == "polygon" && (e.Body.Length > 0 || e.Type == "collider") && Colliders.Problem(e.ColliderPoints) is string outline) Add(ui.Id, e.Id, "The collider outline " + outline);
            if (!double.IsFinite(e.Bounce + e.Friction) || e.Bounce is < 0 or > 1 || e.Friction is < 0 or > 1) Add(ui.Id, e.Id, "Bounce and friction are 0–1");
            if (e.Input.Length > 0 && !m.Inputs.Any(i => i.Name == e.Input)) Add(ui.Id, e.Id, "Unknown input: " + e.Input);
            if (e.Layer is < 0 or > 15) Add(ui.Id, e.Id, "Collision layer is 0-15");
            if (e.Tags.Count > Registry.MaxTags) Add(ui.Id, e.Id, $"At most {Registry.MaxTags} tags");
            foreach (var tag in e.Tags) if (!Registry.Tag(tag)) Add(ui.Id, e.Id, "Tags use lowercase letters, digits, _ and -, up to 24 characters: " + tag);
            if (e.Type == "tilemap") foreach (var problem in Tilemaps.Problems(e)) Add(ui.Id, e.Id, problem);
            if (e.Type == "particles" && !m.Particles.Any(p => p.Id == e.Effect)) Add(ui.Id, e.Id, e.Effect.Length == 0 ? "Particles needs an effect: make one in the Particle maker" : "Unknown particle effect: " + e.Effect);
        }

        void CheckEvents(UiDefinition ui, string id, Dictionary<string, UiEvent> events, string[] allowed)
        {
            foreach (var (name, ev) in events)
            {
                if (!allowed.Contains(name)) Add(ui.Id, id, "Invalid event: " + name);
                foreach (bool server in new[] { false, true })
                {
                    var h = server ? ev.Server : ev.Client; string side = server ? "server" : "client";
                    if (h.ScriptEngine is not ("standard" or "kubejs") || (!server && h.ScriptEngine == "kubejs")) Add(ui.Id, id, "KubeJS scripts must use a Server event");
                    if(h.PermissionLevel<0 || h.PermissionLevel>4 || h.CooldownTicks<0 || h.CooldownTicks>1200) Add(ui.Id,id,"Permission must be 0–4 and cooldown 0–1200 ticks");
                    if (h.Actions.Count > 64) Add(ui.Id, id, "Too many actions");
                    foreach (var a in h.Actions)
                    {
                        if (!(server ? Registry.ServerActions : Registry.ClientActions).Contains(a.Type)) Add(ui.Id, id, "Unknown " + side + " action: " + a.Type);
                        if (new[] { "set_text", "set_visible", "set_enabled", "set_value", "change_texture" }.Contains(a.Type) && !ui.Elements.Any(e => e.Id == a.Target)) Add(ui.Id, id, "Missing action target: " + a.Target);
                        if (a.Type == "open_ui" && !project.Screens.Any(s => s.Id == a.Value && !s.IsComponent)) Add(ui.Id, id, "Missing destination UI: " + a.Value);
                        if (a.Type is "set_variable" or "toggle_variable" && !Variable(a.Target)) Add(ui.Id, id, "Invalid variable name");
                        if(a.Type=="player_inventory" && !ui.Elements.Any(e=>e.Id==a.Target && e.Type=="item_list")) Add(ui.Id,id,"Player inventory action requires an Item List target");
                        if (a.Value.Length > 4096 || a.Target.Length > 256) Add(ui.Id, id, "Action exceeds string limit");
                    }
                    if (h.Script != "" || h.Function != "")
                    {
                        if (!h.Script.StartsWith("scripts/" + side + "/") || !project.Scripts.TryGetValue(h.Script, out var source)) Add(ui.Id, id, "Missing or wrong-side script: " + h.Script);
                        else if (Limits.SizeOf(source) > Limits.For(project).ScriptBytes) Add(ui.Id, id, $"Script exceeds {Limits.For(project).ScriptBytes / 1024} KiB");
                        if (!Regex.IsMatch(h.Function, "^[a-zA-Z_][a-zA-Z0-9_]*$")) Add(ui.Id, id, "Invalid script function");
                    }
                }
            }
        }
    }
}



