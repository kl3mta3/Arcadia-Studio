using System.Text.Json;
namespace Wysicraft.Models;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, PropertyNameCaseInsensitive = false };
    // Copies are never read by a person, so they skip the indentation that files are written with.
    static readonly JsonSerializerOptions Compact = new(Options) { WriteIndented = false };
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, Options) ?? throw new InvalidDataException("Empty JSON");
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Compact), Compact) ?? throw new InvalidDataException("Empty JSON");
    // Deep copy of everything except asset bytes, which are never modified in place and so can be shared.
    // Avoids base64-encoding every texture on each undo checkpoint, autosave and staging copy.
    public static Project CloneProject(Project project) {
        var assets = project.Assets; project.Assets = [];
        Project copy;
        try { copy = Clone(project); } finally { project.Assets = assets; }
        copy.Assets = new Dictionary<string, byte[]>(assets);
        return copy;
    }
}
public sealed class Manifest
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "untitled";
    public string Name { get; set; } = "Untitled";
    public string Author { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string RuntimeVersion { get; set; } = RuntimeInfo.Version;
    public string DefaultUi { get; set; } = "main";
    public List<string> Ui { get; set; } = ["main"];
    public List<string> Dependencies { get; set; } = [];
    public int GridSize { get; set; } = 8;
    public bool Snap { get; set; } = true;
    // Where the project is meant to run: "minecraft", "web" (web and desktop apps) or "both". Minecraft limits and
    // web-only (advanced) tools are checked against this.
    public string Target { get; set; } = "both";
    // Advanced (web and desktop): named inputs, each pressed by keys and/or gamepad buttons.
    public List<GameInput> Inputs { get; set; } = [];
    // Advanced (web and desktop): what the 16 collision layers are called, in order. Blank entries show as their
    // number. Only naming — the behaviour is the same either way.
    public List<string> CollisionLayers { get; set; } = [];
    // Which layers collide with which, for the whole project: sixteen bit masks, one per layer, as Unity's matrix.
    // Empty means every layer meets every layer, which is how it behaved before. A control can narrow it further with
    // its own collidesWith, but never widen it.
    public List<int> CollisionMatrix { get; set; } = [];
    // Advanced (web and desktop): named particle effects, played by a Particles control or ui.emit(id).
    public List<ParticleEffect> Particles { get; set; } = [];
    // Advanced (web only): named GLSL fragment shaders, run over a screen’s batched layer. A screen picks one by ID.
    public List<ShaderEffect> Shaders { get; set; } = [];
}
/// <summary>A named input such as "jump": any of its keys (Key event names) or gamepad buttons presses it.
/// Axis optionally links a stick direction ("left_x-" = left stick pushed left, "right_y+" = right stick down) for ctx.input.axis(name), 0–1.</summary>
public sealed class GameInput
{
    public string Name { get; set; } = "";
    public List<string> Keys { get; set; } = [];
    public List<string> Buttons { get; set; } = [];
    public string Axis { get; set; } = "";
    /// <summary>Which gamepad presses this input: -1 for any (the default), or 0-3 for one pad, so two players on
    /// two controllers do not drive each other. Keys and touch are unaffected.</summary>
    public int Pad { get; set; } = -1;
    /// <summary>Touch and pen: one of Registry.TouchActions. A drag_* input is pressed by dragging a finger that way
    /// anywhere the controls are not, from wherever the finger first lands, and reports how far as 0-1.</summary>
    public string Touch { get; set; } = "";
}
/// <summary>Advanced (web and desktop): a keyframe animation on a screen.</summary>
public sealed class ScreenAnimation
{
    public string Id { get; set; } = "animation";
    public int Duration { get; set; } = 1000;
    public bool Loop { get; set; }
    public bool Autoplay { get; set; }
    public List<AnimationTrack> Tracks { get; set; } = [];
}
/// <summary>A particle effect: a burst or a stream of short-lived specks, with the shape of the spray, how they move
/// and how they fade. Named effects live on the manifest (like inputs) so several screens can use the same one, and a
/// Particles control plays one. The runtime's simulation in wysicraft-web.js is the same arithmetic, in the same order;
/// this one exists so the editor can show the effect without a browser. Keep the two in step.</summary>
/// <summary>One colour on a particle's ramp: At is where in its life this colour is reached, 0 (born) to 1 (dead).</summary>
public sealed class ParticleStop
{
    public double At { get; set; }
    public string Color { get; set; } = "#FFFFFF";
}
public sealed class ParticleEffect
{
    public string Id { get; set; } = "sparks";
    /// <summary>"burst" fires Count particles at once; "stream" emits Count per second while it plays.</summary>
    public string Emission { get; set; } = "burst";
    public int Count { get; set; } = 24;
    /// <summary>Seconds a stream keeps emitting; 0 means until it is stopped. Bursts ignore it.</summary>
    public double Duration { get; set; }
    public double Life { get; set; } = 0.6;
    /// <summary>How much the life of each particle varies, 0–1 (0.5 = give or take half).</summary>
    public double LifeVariance { get; set; } = 0.3;
    /// <summary>Degrees clockwise from east; Spread is the full width of the cone (360 = every direction).</summary>
    public double Direction { get; set; }
    public double Spread { get; set; } = 360;
    public double Speed { get; set; } = 120;
    public double SpeedVariance { get; set; } = 0.4;
    /// <summary>Pixels per second², positive is down the screen. Drag is the fraction of speed lost per second.</summary>
    public double Gravity { get; set; }
    public double Drag { get; set; } = 1.2;
    public double SizeStart { get; set; } = 3;
    public double SizeEnd { get; set; } = 0;
    public double SizeVariance { get; set; } = 0.3;
    /// <summary>The colour ramp across a particle's life: stops at 0 to 1, in any order. Two or more stops.
    /// When it is empty the older ColorStart/ColorEnd pair is used instead, so effects saved before ramps still work.</summary>
    public List<ParticleStop> Colors { get; set; } = [];
    public string ColorStart { get; set; } = "#F2C240";
    public string ColorEnd { get; set; } = "#C2413A";
    public double OpacityStart { get; set; } = 1;
    public double OpacityEnd { get; set; }
    /// <summary>Degrees per second; only squares and textures show it.</summary>
    public double Spin { get; set; }
    /// <summary>"square", "circle", "line" (drawn along its travel) or "texture".</summary>
    public string Shape { get; set; } = "circle";
    public string Texture { get; set; } = "";
    /// <summary>"normal", or "add" for fire, sparks and anything that should glow where it piles up.</summary>
    public string Blend { get; set; } = "normal";
    /// <summary>How far from the middle particles start, in pixels: 0 is a point, more is a ring or a patch.</summary>
    public double Radius { get; set; }
}
/// <summary>Animates one property ("x", "y", "width", "height", "opacity") of one element through keyframes.</summary>
public sealed class AnimationTrack
{
    public string Target { get; set; } = "";
    public string Property { get; set; } = "x";
    public List<Keyframe> Keys { get; set; } = [];
}
public sealed class Keyframe
{
    public int Time { get; set; }
    public double Value { get; set; }
    // linear, ease_in, ease_out, ease_in_out or step
    public string Ease { get; set; } = "linear";
}
/// <summary>A collider point. Curve: this point is a handle that bends the edge between its neighbours (a smooth curve).</summary>
public sealed class Vertex { public double X { get; set; } public double Y { get; set; } public bool Curve { get; set; } }
public sealed class Project
{
    public Manifest Manifest { get; set; } = new();
    public List<UiDefinition> Screens { get; set; } = [new()];
    public Dictionary<string, string> Scripts { get; set; } = [];
    public Dictionary<string, byte[]> Assets { get; set; } = [];
}
public sealed class UiDefinition
{
    public bool IsComponent { get; set; }
    public List<ComponentInstance> ComponentInstances { get; set; } = [];
    // Milliseconds between the screen's Tick events while it is open; 0 turns the timer off.
    public int TickInterval { get; set; }
    // Milliseconds between Key events while a key is held down; 0 = a held key sends one event.
    public int KeyRepeat { get; set; } = 150;
    public bool Responsive { get; set; }
    public Dictionary<string,string> GroupParents { get; set; } = [];
    public bool ShowFrame { get; set; }
    public bool DimBackground { get; set; }
    public bool FitToScreen { get; set; } = true;
    // Web & desktop and Minecraft: nothing outside the screen's own area is drawn or clicked (games scrolling things in from off screen).
    public bool ClipToScreen { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "main";
    public string Title { get; set; } = "New screen";
    public Size Size { get; set; } = new();
    public Dictionary<string, string> Variables { get; set; } = [];
    public Dictionary<string, UiEvent> Events { get; set; } = [];
    public List<Element> Elements { get; set; } = [];
    // Advanced (web and desktop): downward pull on moving physics bodies, in GUI pixels per second squared.
    public double Gravity { get; set; }
    // Advanced (web and desktop): keyframe animations, played with ui.animate(id) or on open when Autoplay is set.
    public List<ScreenAnimation> Animations { get; set; } = [];
    // Advanced (web only): a manifest shader ID, run over this screen’s batched layer.
    public string Shader { get; set; } = "";
    // Advanced (web and desktop): sprite state machines. The runtime picks which clip a sprite plays, so idle → run
    // → jump → land needs no script at all.
    public List<StateGraph> StateGraphs { get; set; } = [];
}
/// <summary>A state machine over one sprite's clips: which clip is playing, and what moves it to another. The runtime
/// advances it every frame, so the ordinary cases — idle to run to jump to land — need no script. Unity's Animator and
/// Godot's AnimationTree are the same idea; this one is data, like the keyframe animations beside it.</summary>
/// <summary>A GLSL ES 3.00 fragment shader, run over what the WebGL2 batch layer drew for a screen. It samples the
/// scene from u_scene and may read u_resolution, u_time and any "uniform float u_name" a script sets with
/// ui.setShaderValue. Web only: there is no batch layer in Minecraft, and one that will not compile is dropped at
/// runtime rather than taking the screen with it.</summary>
public sealed class ShaderEffect
{
    public string Id { get; set; } = "shader";
    public string Source { get; set; } = "";
}
public sealed class StateGraph
{
    public string Id { get; set; } = "graph";
    /// <summary>The sprite control this drives.</summary>
    public string Target { get; set; } = "";
    /// <summary>The state it begins in. Empty means the first one.</summary>
    public string Start { get; set; } = "";
    public List<AnimationState> States { get; set; } = [];
}
public sealed class AnimationState
{
    public string Name { get; set; } = "state";
    /// <summary>A clip on the target sprite. The clip itself says whether it loops.</summary>
    public string Clip { get; set; } = "";
    public List<StateTransition> Transitions { get; set; } = [];
}
/// <summary>One way out of a state. When is an ordinary condition over the screen's variables, plus clipDone (the
/// clip has finished), clipStep (how many frames in) and input:name for an input being held. The highest Priority
/// that is true wins; an empty When is always true. At most one transition is taken per frame.</summary>
public sealed class StateTransition
{
    public string To { get; set; } = "";
    public string When { get; set; } = "";
    public int Priority { get; set; }
}
public sealed class ComponentInstance
{
    public string Root { get; set; } = "";
    public string Source { get; set; } = "";
    public Size SourceSize { get; set; } = new();
    public Dictionary<string,string> Ids { get; set; } = [];
    public Dictionary<string,Element> Baseline { get; set; } = [];
}
public sealed class Size { public int Width { get; set; } = 320; public int Height { get; set; } = 200; }
public sealed class Bounds { public double X { get; set; } public double Y { get; set; } public double Width { get; set; } = 100; public double Height { get; set; } = 20; }
public sealed class Element
{
    public string HorizontalAnchor { get; set; } = "left";
    public string VerticalAnchor { get; set; } = "top";
    public double MinWidth { get; set; } = 1;
    public double MinHeight { get; set; } = 1;
    public double RowTemplateWidth { get; set; }
    public string RowTemplate { get; set; } = "";
    public string RowAction { get; set; } = "";
    public List<Element> RowElements { get; set; } = [];
    public int RowHeight { get; set; } = 30;
    public string PrimaryLabel { get; set; } = "";
    public string SecondaryLabel { get; set; } = "";
    public bool ShowItemId { get; set; } = true;
    public string Id { get; set; } = "element";
    public string Name { get; set; } = "";
    public string LayerGroup { get; set; } = "";
    public string Type { get; set; } = "button";
    public Bounds Bounds { get; set; } = new();
    // Editor-only: a locked control can't be clicked, dragged or nudged on the canvas. Minecraft ignores it.
    public bool Locked { get; set; }
    public bool Visible { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public string Tooltip { get; set; } = "";
    public string Text { get; set; } = "Button";
    public string Foreground { get; set; } = "#FFFFFF";
    public string Background { get; set; } = "#40464F";
    public bool FillEnabled { get; set; } = true;
    public string BorderColor { get; set; } = "#697382";
    public double BorderWidth { get; set; }
    public double Opacity { get; set; } = 1;
    public double FontScale { get; set; } = 1;
    public string Font { get; set; } = "minecraft:default";
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool TextShadow { get; set; }
    public string ShadowColor { get; set; } = "#000000";
    public double ShadowOpacity { get; set; } = 0.75;
    public double ShadowOffsetX { get; set; } = 1;
    public double ShadowOffsetY { get; set; } = 1;
    public double ShadowBlur { get; set; }
    public double CornerRadius { get; set; }
    public string Alignment { get; set; } = "left";
    public string Texture { get; set; } = "";
    public string Item { get; set; } = "minecraft:stone";
    public string Value { get; set; } = "0";
    public double Minimum { get; set; }
    public double Maximum { get; set; } = 100;
    public List<string> Options { get; set; } = ["Option 1", "Option 2"];
    public string VisibleIf { get; set; } = "";
    public string EnabledIf { get; set; } = "";
    public string Parent { get; set; } = "";
    public int TextureX { get; set; }
    public int TextureY { get; set; }
    public int TextureWidth { get; set; } = 256;
    public int TextureHeight { get; set; } = 256;
    public Dictionary<string, UiEvent> Events { get; set; } = [];
    // Sprite: frame size in the sheet (frames are numbered left to right, top to bottom) and named clips, written
    // "idle: 0; run: 1-6 @12; jump: 7,8,9 @8 once". Value is the clip playing.
    public int FrameWidth { get; set; } = 16;
    public int FrameHeight { get; set; } = 16;
    public string Clips { get; set; } = "";
    // Shape: rectangle, ellipse, triangle, diamond, hexagon or star. Stretch it to make ovals and rectangles.
    public string Shape { get; set; } = "rectangle";
    // Advanced (web and desktop) physics: "" (none), "static" (walls), "dynamic" (moves and falls) or "kinematic"
    // (moved by scripts and animations, pushes dynamic bodies).
    public string Body { get; set; } = "";
    // box, circle or polygon (ColliderPoints, relative to the control's top-left corner).
    public string Collider { get; set; } = "box";
    public List<Vertex> ColliderPoints { get; set; } = [];
    public double Bounce { get; set; }
    public double Friction { get; set; } = 0.2;
    // A trigger notices overlaps (trigger_enter / trigger_stay / trigger_exit) but never pushes or gets pushed:
    // pickups, checkpoints and zones.
    public bool Trigger { get; set; }
    // Advanced (web and desktop): pressing this control also presses the named input (its own events still fire).
    public string Input { get; set; } = "";
    // Sound control (invisible; listed in Layers): what it plays and how. Autoplay starts it Delay ms after the
    // screen opens; otherwise set its value to "play" (or "stop") from an action or script.
    public string Sound { get; set; } = "";
    public bool Autoplay { get; set; } = true;
    public int Delay { get; set; }
    public double Volume { get; set; } = 1;
    public int Repeat { get; set; } = 1;
    public bool Loop { get; set; }
    // Tilemap control (advanced): a grid of tiles cut from one sheet. Tiles is a run-length list of indices into
    // that sheet, "-1" for an empty cell, written as "index" or "index*count" separated by spaces. Solid lists the
    // indices that stop a body, as numbers and ranges: "1,3,5-9".
    public int TileWidth { get; set; } = 16;
    public int TileHeight { get; set; } = 16;
    public int Columns { get; set; } = 20;
    public int Rows { get; set; } = 12;
    public string Tiles { get; set; } = "";
    public string Solid { get; set; } = "";
    // Names a script can look controls up by: ctx.ui.findByTag("enemy"). A control may carry several. Tags are data,
    // so they travel everywhere; the lookup itself is web and desktop.
    public List<string> Tags { get; set; } = [];
    // Advanced (web and desktop): the script components attached to this control (Core/Behaviours ids). The physics
    // ones are not listed here because they are the body/collider fields themselves.
    public List<string> Behaviours { get; set; } = [];
    // Advanced (web and desktop): which collision layer this body is on (0-15), and which layers it tests against as
    // a bit mask (-1, the default, is every layer). A hitbox on its own layer never reports hitting another hitbox.
    public int Layer { get; set; }
    public int CollidesWith { get; set; } = -1;
    // Advanced (web and desktop): drawn only on a device with a touch screen, so on-screen controls stay off a desktop.
    public bool TouchOnly { get; set; }
    // Particles control (advanced; invisible except for what it emits): the named effect on the manifest it plays.
    // Autoplay starts it when the screen opens; a script fires one with ui.emit(id) and stops a stream with ui.stopEmit(id).
    public string Effect { get; set; } = "";
}
public sealed class UiEvent { public EventHandler Client { get; set; } = new(); public EventHandler Server { get; set; } = new(); }
public sealed class EventHandler
{
    public int PermissionLevel { get; set; }
    public int CooldownTicks { get; set; } = 4;
    public string ScriptEngine { get; set; } = "standard";
    public List<VisualAction> Actions { get; set; } = [];
    public string Script { get; set; } = "";
    public string Function { get; set; } = "";
}
public sealed class VisualAction
{
    public string Type { get; set; } = "set_text";
    public string Target { get; set; } = "";
    public string Value { get; set; } = "";
}

