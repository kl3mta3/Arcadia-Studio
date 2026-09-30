using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Wysicraft.Models;

namespace Wysicraft.Core;

public sealed class ProjectEdit
{
    public string Kind { get; set; } = "";
    public string Screen { get; set; } = "";
    public string Element { get; set; } = "";
    public string Key { get; set; } = "";
    public string Source { get; set; } = "";
    public JsonElement Data { get; set; }
    /// <summary>Upserts merge into what is already there by default, which is usually what an edit wants and is
    /// occasionally a trap: fields left out keep whatever they had. replace:true builds the object from defaults plus
    /// the data instead, so what is sent is exactly what results.</summary>
    public bool Replace { get; set; }
    /// <summary>upsert_element, for a control that did not exist: where to put it in the draw order. "front" (the
    /// default, and how it always behaved) draws it over everything; "back" draws it under; a number is an index.
    /// Later controls draw on top, so a control added last covers what is already there.</summary>
    public string At { get; set; } = "";
}

public static class ProjectEdits
{
    static readonly JsonSerializerOptions Strict = new(Json.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    // After the project ID changes, moves images, sounds and fonts from assets/<old>/ to assets/<new>/ and updates every
    // <old>:... reference so the game keeps them: textures, fonts, Sound controls, change_texture and play_sound
    // actions, particle textures, on screens, components and leaderboard pages. Scripts are text and aren't rewritten;
    // the ones naming the old ID are returned (and Validate points them out) so the person can change them.
    public static List<string> MoveAssetNamespace(Project project,string oldId) {
        string newId=project.Manifest.Id;if(oldId==newId)return [];
        string Rename(string value)=>value.StartsWith(oldId+":")?newId+value[oldId.Length..]:value;
        project.Assets=project.Assets.ToDictionary(p=>p.Key.StartsWith("assets/"+oldId+"/")?"assets/"+newId+"/"+p.Key[(8+oldId.Length)..]:p.Key,p=>p.Value);
        foreach(var s in project.Screens.Concat(project.Leaderboards)) {
            foreach(var e in s.Elements) { e.Texture=Rename(e.Texture); e.Font=Rename(e.Font); e.Sound=Rename(e.Sound); }
            foreach(var ev in s.Events.Values.Concat(s.Elements.SelectMany(e=>e.Events.Values))) foreach(var h in new[]{ev.Client,ev.Server}) foreach(var a in h.Actions) if(a.Type is "change_texture" or "play_sound") a.Value=Rename(a.Value);
        }
        foreach(var fx in project.Manifest.Particles) fx.Texture=Rename(fx.Texture);
        var mention=new System.Text.RegularExpressions.Regex("['\"`]"+System.Text.RegularExpressions.Regex.Escape(oldId)+":[a-z0-9_./-]+['\"`]");
        return project.Scripts.Where(p=>mention.IsMatch(p.Value)).Select(p=>p.Key).OrderBy(k=>k,StringComparer.Ordinal).ToList();
    }
    public static Project Apply(Project original, IReadOnlyList<ProjectEdit> edits)
    {
        if (edits.Count is < 1 or > 128) throw new InvalidDataException("Use 1–128 edits per batch.");
        // CloneProject, not a plain JSON copy: that leaves out the publishing cover, screenshots and imported leaderboard
        // page (kept as bytes, not JSON), so every MCP edit used to drop them.
        var project = Json.CloneProject(original);
        foreach (var edit in edits)
        {
            // Element edits reach leaderboard pages too, by their ID (a screen of the same ID comes first).
            UiDefinition Screen() => project.Screens.SingleOrDefault(s => s.Id == edit.Screen) ?? project.Leaderboards.SingleOrDefault(b => b.Id == edit.Screen) ?? throw new InvalidDataException("Screen or leaderboard not found: " + edit.Screen);
            switch (edit.Kind)
            {
                case "add_component_template": ComponentStarters.Add(project,edit.Key);break;
                case "create_component": Components.Capture(project,Screen(),edit.Data.GetProperty("ids").Deserialize<string[]>(Json.Options)!,edit.Key);break;
                case "place_component": Components.Place(project,Screen(),edit.Key,edit.Data.GetProperty("x").GetDouble(),edit.Data.GetProperty("y").GetDouble());break;
                case "update_component":
                    var componentScreen=Screen();Components.Update(project,componentScreen,componentScreen.ComponentInstances.Single(i=>i.Root==edit.Element),!edit.Data.TryGetProperty("reset",out var reset)||!reset.GetBoolean());break;
                case "detach_component": Components.Detach(Screen(),edit.Element);break;
                case "arrange":
                    var layout=Screen();
                    var ids=edit.Data.GetProperty("ids").Deserialize<string[]>(Json.Options) ?? throw new InvalidDataException("Arrange requires selected element IDs");
                    if(ids.Any(id=>!layout.Elements.Any(e=>e.Id==id)))throw new InvalidDataException("Arrange selection contains a missing element");
                    if(!Enum.TryParse<ArrangeOperation>(edit.Data.GetProperty("operation").GetString(),true,out var operation) || !Enum.IsDefined(operation))throw new InvalidDataException("Unknown arrange operation");
                    Arrangement.Apply(layout,Arrangement.Plan(layout,ids,operation,!edit.Data.TryGetProperty("keepGroups",out var keepGroups) || keepGroups.GetBoolean()));break;
                case "set_project":
                    string oldId=project.Manifest.Id;
                    project.Manifest=Patch(project.Manifest,edit.Data);
                    if(oldId!=project.Manifest.Id)MoveAssetNamespace(project,oldId);
                    break;
                case "upsert_screen":
                    var old = project.Screens.SingleOrDefault(s => s.Id == edit.Screen);
                    var screen = Patch(old ?? new UiDefinition { Id = edit.Screen },edit.Data,edit.Replace,new UiDefinition { Id = edit.Screen });
                    if (screen.Id != edit.Screen) throw new InvalidDataException("Screen ID must match screen; renaming IDs is not supported here.");
                    if (old == null) project.Screens.Add(screen); else project.Screens[project.Screens.IndexOf(old)] = screen;
                    break;
                case "delete_screen": project.Screens.Remove(project.Screens.SingleOrDefault(s => s.Id == edit.Screen) ?? throw new InvalidDataException("Screen not found: " + edit.Screen)); break;
                case "new_leaderboard":
                {
                    if (!Validation.Id(edit.Key)) throw new InvalidDataException("A leaderboard ID uses lowercase letters, digits and _, starting with a letter.");
                    if (project.Leaderboards.Any(b => b.Id == edit.Key) || project.Screens.Any(s => s.Id == edit.Key)) throw new InvalidDataException(edit.Key + " is already a screen or leaderboard.");
                    bool starter = edit.Data.ValueKind != JsonValueKind.Object || !edit.Data.TryGetProperty("starter", out var s) || s.GetBoolean();
                    var board = starter ? LeaderboardPages.Starter(edit.Key) : new UiDefinition { Id = edit.Key, IsLeaderboard = true, Size = new() { Width = 640, Height = 400 }, Elements = [] };
                    if (edit.Data.ValueKind == JsonValueKind.Object && edit.Data.TryGetProperty("title", out var title)) board.Title = title.GetString() ?? "";
                    project.Leaderboards.Add(board);
                    break;
                }
                case "upsert_leaderboard":
                {
                    var was = project.Leaderboards.SingleOrDefault(b => b.Id == edit.Screen) ?? throw new InvalidDataException("Leaderboard not found: " + edit.Screen + " (make one with new_leaderboard)");
                    var board = Patch(was, edit.Data, edit.Replace, new UiDefinition { Id = edit.Screen, IsLeaderboard = true });
                    if (board.Id != edit.Screen) throw new InvalidDataException("Leaderboard ID must match screen; renaming isn't supported here.");
                    board.IsLeaderboard = true; board.IsComponent = false;
                    project.Leaderboards[project.Leaderboards.IndexOf(was)] = board;
                    break;
                }
                case "delete_leaderboard":
                    if (project.Leaderboards.RemoveAll(b => b.Id == edit.Key) == 0) throw new InvalidDataException("Leaderboard not found: " + edit.Key);
                    if (project.Publishing.LeaderboardPage == "board:" + edit.Key) project.Publishing.LeaderboardPage = "";
                    break;
                case "upsert_element":
                    var target = Screen(); var before = target.Elements.SingleOrDefault(e => e.Id == edit.Element);
                    var element = Patch(before ?? new Element { Id = edit.Element },edit.Data,edit.Replace,new Element { Id = edit.Element });
                    if (element.Id != edit.Element) throw new InvalidDataException("Element ID must match element.");
                    // Like the toolbox: pictures draw their own image (a fill would show through transparent pixels),
                    // and sounds, colliders and cameras have no picture at all, so new ones start without a fill unless asked.
                    if (before == null && element.Type is "image" or "texture_region" or "item" or "sprite" or "sound" or "collider" or "camera" or "tilemap"
                        && !(edit.Data.ValueKind == JsonValueKind.Object && edit.Data.TryGetProperty("fillEnabled", out _))) element.FillEnabled = false;
                    Tilemaps.Fit(element); // a tilemap's box always matches its grid, whatever was sent for it
                    if (before != null) target.Elements[target.Elements.IndexOf(before)] = element;
                    else if (edit.At.Length == 0 || edit.At == "front") target.Elements.Add(element);
                    else if (edit.At == "back") target.Elements.Insert(0, element);
                    else if (int.TryParse(edit.At, out int where)) target.Elements.Insert(Math.Clamp(where, 0, target.Elements.Count), element);
                    else throw new InvalidDataException("at is front, back or an index.");
                    break;
                case "delete_element":
                    var owner = Screen();
                    if (owner.Elements.RemoveAll(e => e.Id == edit.Element) == 0) throw new InvalidDataException("Element not found: " + edit.Element);
                    foreach (var child in owner.Elements.Where(e => e.Parent == edit.Element)) child.Parent = "";
                    break;
                case "put_script":
                    Validation.SafePath(edit.Key);
                    int scriptBytes = Limits.For(project).ScriptBytes;
                    if (!(edit.Key.StartsWith("scripts/client/") || edit.Key.StartsWith("scripts/server/")) || !edit.Key.EndsWith(".js") || Limits.SizeOf(edit.Source)>scriptBytes)
                        throw new InvalidDataException($"Use scripts/client/name.js or scripts/server/name.js, at most {scriptBytes / 1024} KiB.");
                    project.Scripts[edit.Key] = edit.Source; break;
                case "delete_script":
                    if (!project.Scripts.Remove(edit.Key)) throw new InvalidDataException("Script not found: " + edit.Key);
                    break;
                case "delete_asset":
                    if (!project.Assets.Remove(edit.Key)) throw new InvalidDataException("Asset not found: " + edit.Key);
                    project.Assets.Remove(edit.Key + ".mcmeta"); project.Assets.Remove(edit.Key + TextureAssets.LayersSuffix); // files that only belong to that image or sound
                    project.Assets.Remove(edit.Key + SoundAssets.SongSuffix); project.Assets.Remove(edit.Key + SoundAssets.EffectSuffix);
                    break;
                case "set_event":
                    var events = edit.Element.Length == 0 ? Screen().Events : Screen().Elements.Single(e => e.Id == edit.Element).Events;
                    events[edit.Key] = Patch(events.GetValueOrDefault(edit.Key) ?? new UiEvent(),edit.Data); break;
                case "set_main": project.Manifest.DefaultUi = Screen().Id; break;
                // Components (web & desktop), the way the editor's "+ Add component" does it: key is a Behaviours ID, or
                // "collider" (a static body) and "rigidbody" for the two physics cards. Removing a physics card changes
                // the body fields; removing a script component unwires and (if untouched) deletes its script.
                case "add_component":
                {
                    var subject = Screen().Elements.SingleOrDefault(e => e.Id == edit.Element) ?? throw new InvalidDataException("Element not found: " + edit.Element);
                    string id = edit.Key == "collider" ? "static_body" : edit.Key;
                    if (!Behaviours.For(subject).Any(b => b.Id == id)) throw new InvalidDataException($"Component {edit.Key} is not one this control can take. Components: collider, rigidbody, " + string.Join(", ", Behaviours.All.Select(b => b.Id)));
                    Behaviours.Apply(project, Screen(), subject, id);
                    break;
                }
                case "remove_component":
                {
                    var subject = Screen().Elements.SingleOrDefault(e => e.Id == edit.Element) ?? throw new InvalidDataException("Element not found: " + edit.Element);
                    if (edit.Key == "collider") { if (subject.Type == "collider") throw new InvalidDataException("A collider control is its collider; delete the control instead."); subject.Body = ""; subject.Trigger = false; }
                    else if (edit.Key == "rigidbody") subject.Body = subject.Body.Length > 0 ? "static" : "";
                    else if (Behaviours.ScriptOf(edit.Key) != null) Behaviours.Remove(project, Screen(), subject, edit.Key);
                    else throw new InvalidDataException("Not a removable component: " + edit.Key + " (collider, rigidbody, or a script component such as character_controller)");
                    break;
                }
                // Web & desktop inputs, by name: data patches {keys, buttons, axis}.
                case "upsert_input":
                {
                    var inputs = project.Manifest.Inputs; var oldInput = inputs.FirstOrDefault(i => i.Name == edit.Key);
                    var input = Patch(oldInput ?? new GameInput { Name = edit.Key }, edit.Data, edit.Replace, new GameInput { Name = edit.Key });
                    if (input.Name != edit.Key) throw new InvalidDataException("Input name must match key.");
                    if (oldInput == null) inputs.Add(input); else inputs[inputs.IndexOf(oldInput)] = input;
                    break;
                }
                case "delete_input":
                    if (project.Manifest.Inputs.RemoveAll(i => i.Name == edit.Key) == 0) throw new InvalidDataException("Input not found: " + edit.Key);
                    foreach (var e in project.Screens.SelectMany(s => s.Elements).Where(e => e.Input == edit.Key)) e.Input = "";
                    break;
                // Web & desktop particle effects on the manifest, by ID. data patches the settings; key "preset:sparks"
                // style values are not used here, the caller sends the numbers it wants.
                case "upsert_particles":
                {
                    var effects = project.Manifest.Particles; var oldEffect = effects.FirstOrDefault(f => f.Id == edit.Key);
                    var effect = Patch(oldEffect ?? Particles.Preset("sparks"), edit.Data, edit.Replace, Particles.Preset("sparks"));
                    effect.Id = edit.Key; effect.Check();
                    if (oldEffect == null && effects.Count >= Particles.MaxPerProject) throw new InvalidDataException($"At most {Particles.MaxPerProject} particle effects.");
                    if (oldEffect == null) effects.Add(effect); else effects[effects.IndexOf(oldEffect)] = effect;
                    break;
                }
                case "delete_particles":
                    if (project.Manifest.Particles.RemoveAll(f => f.Id == edit.Key) == 0) throw new InvalidDataException("Particle effect not found: " + edit.Key);
                    foreach (var e in project.Screens.SelectMany(s => s.Elements).Where(e => e.Effect == edit.Key)) e.Effect = "";
                    break;
                // Web & desktop keyframe animations on a screen, by ID: data patches {duration, loop, autoplay, tracks}.
                case "upsert_animation":
                {
                    var animations = Screen().Animations; var oldAnimation = animations.FirstOrDefault(a => a.Id == edit.Key);
                    var animation = Patch(oldAnimation ?? new ScreenAnimation { Id = edit.Key }, edit.Data, edit.Replace, new ScreenAnimation { Id = edit.Key });
                    if (animation.Id != edit.Key) throw new InvalidDataException("Animation ID must match key.");
                    if (oldAnimation == null) animations.Add(animation); else animations[animations.IndexOf(oldAnimation)] = animation;
                    break;
                }
                case "delete_animation":
                    if (Screen().Animations.RemoveAll(a => a.Id == edit.Key) == 0) throw new InvalidDataException("Animation not found: " + edit.Key);
                    break;
                case "upsert_state_graph":
                {
                    var graphs = Screen().StateGraphs; var oldGraph = graphs.FirstOrDefault(g => g.Id == edit.Key);
                    var graph = Patch(oldGraph ?? new StateGraph { Id = edit.Key }, edit.Data, edit.Replace, new StateGraph { Id = edit.Key });
                    if (graph.Id != edit.Key) throw new InvalidDataException("State graph ID must match key.");
                    if (oldGraph == null) graphs.Add(graph); else graphs[graphs.IndexOf(oldGraph)] = graph;
                    break;
                }
                case "upsert_shader":
                {
                    var shaders = project.Manifest.Shaders; var oldShader = shaders.FirstOrDefault(s => s.Id == edit.Key);
                    var shader = new ShaderEffect { Id = edit.Key, Source = edit.Source.Length > 0 ? edit.Source : oldShader?.Source ?? "" };
                    if (oldShader == null) shaders.Add(shader); else shaders[shaders.IndexOf(oldShader)] = shader;
                    break;
                }
                case "delete_shader":
                    if (project.Manifest.Shaders.RemoveAll(s => s.Id == edit.Key) == 0) throw new InvalidDataException("Shader not found: " + edit.Key);
                    break;
                case "delete_state_graph":
                    if (Screen().StateGraphs.RemoveAll(g => g.Id == edit.Key) == 0) throw new InvalidDataException("State graph not found: " + edit.Key);
                    break;
                // Renames an element and updates its children, action targets and animation tracks (scripts are not changed).
                case "rename_element":
                {
                    var renamed = Screen(); ElementIds.Rename(project, renamed, edit.Element, edit.Key);
                    foreach (var track in renamed.Animations.SelectMany(a => a.Tracks).Where(t => t.Target == edit.Element)) track.Target = edit.Key;
                    foreach (var graph in renamed.StateGraphs.Where(g => g.Target == edit.Element)) graph.Target = edit.Key;
                    break;
                }
                // Layer order: key is front, back, forward or backward (front draws on top).
                case "reorder_element":
                {
                    var list = Screen().Elements; var moving = list.SingleOrDefault(e => e.Id == edit.Element) ?? throw new InvalidDataException("Element not found: " + edit.Element);
                    int index = list.IndexOf(moving); list.RemoveAt(index);
                    int place = edit.Key switch { "front" => list.Count, "back" => 0, "forward" => Math.Min(list.Count, index + 1), "backward" => Math.Max(0, index - 1), _ => throw new InvalidDataException("Use front, back, forward or backward") };
                    list.Insert(place, moving); ContainerTree.KeepChildrenInFront(Screen());
                    break;
                }
                // An empty layer group (key = name), ready for elements' layerGroup to point at it.
                case "add_group":
                    if (edit.Key.Length is < 1 or > 64 || edit.Key.Any(char.IsControl)) throw new InvalidDataException("Group names are 1–64 characters.");
                    Screen().GroupParents.TryAdd(edit.Key, edit.Data.ValueKind == JsonValueKind.Object && edit.Data.TryGetProperty("parent", out var parent) ? parent.GetString() ?? "" : "");
                    break;
                default: throw new InvalidDataException("Unknown edit kind: " + edit.Kind);
            }
        }
        project.Manifest.Ui = project.Screens.Select(s => s.Id).ToList();
        // A leaderboard page holds page pieces and leaderboard widgets only, with IDs of its own.
        foreach (var board in project.Leaderboards)
        {
            var odd = board.Elements.FirstOrDefault(e => !LeaderboardPages.Types.Contains(e.Type));
            if (odd != null) throw new InvalidDataException($"{board.Id}.{odd.Id}: a leaderboard page holds labels, images, panels, shapes and leaderboard widgets ({string.Join(", ", LeaderboardPages.Widgets)}), not a {odd.Type}.");
            var twice = board.Elements.GroupBy(e => e.Id).FirstOrDefault(g => g.Count() > 1);
            if (twice != null) throw new InvalidDataException($"{board.Id}: two controls are called {twice.Key}.");
            foreach (var e in board.Elements.Where(e => e.Type.StartsWith("lb_"))) e.Board ??= new BoardWidget();
        }
        var errors = Validation.Errors(project);
        if (errors.Count > 0) throw new InvalidDataException(string.Join("\n",errors));
        return project;
    }
    static T Patch<T>(T original,JsonElement changes,bool replace = false,T? blank = default)
    {
        if (changes.ValueKind != JsonValueKind.Object) throw new InvalidDataException("data must be a JSON object.");
        // replace starts from a blank object, so anything the edit leaves out goes back to its default instead of
        // keeping what happened to be there.
        var node = JsonNode.Parse(Json.Write(replace && blank != null ? blank : original))!.AsObject();
        Merge(node,JsonNode.Parse(changes.GetRawText())!.AsObject());
        return JsonSerializer.Deserialize<T>(node.ToJsonString(),Strict) ?? throw new InvalidDataException("Empty edit.");
    }
    static void Merge(JsonObject target,JsonObject changes)
    {
        foreach (var pair in changes)
            if (pair.Value is JsonObject child && target[pair.Key] is JsonObject existing) Merge(existing,child);
            else target[pair.Key] = pair.Value?.DeepClone();
    }
}

