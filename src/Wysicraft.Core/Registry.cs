using Wysicraft.Models;
namespace Wysicraft.Core;

// Advanced controls work in web and desktop apps only (not in Minecraft).
public record ControlSpec(string Type, string DisplayName, string[] Properties, string[] Events, bool Advanced = false);
public static class Registry
{
    public static readonly Dictionary<string, ControlSpec> Controls = new();
    public static readonly HashSet<string> ClientActions = ["set_text", "set_visible", "set_enabled", "set_value", "open_ui", "close_ui", "play_sound", "set_variable", "toggle_variable", "message", "change_texture"];
    public static readonly HashSet<string> ServerActions = ["command", "message", "set_variable", "toggle_variable", "open_ui", "close_ui", "server_function", "player_inventory"];
    static Registry()
    {
        Register("button", "Button", [], ["click"]);
        Register("label", "Label", [], []);
        Register("image", "Image", ["Texture"], []);
        Register("textbox", "Text Box", ["Value"], ["text_changed", "submit"]);
        Register("checkbox", "Checkbox", ["Value"], ["checked", "unchecked"]);
        Register("slider", "Slider", ["Value", "Minimum", "Maximum"], ["value_changed"]);
        Register("progress", "Progress Bar", ["Value", "Minimum", "Maximum"], []);
        Register("dropdown", "Dropdown", ["Value", "Options"], ["value_changed"]);
        Register("panel", "Panel", [], []);
        Register("scroll_panel", "Scroll Panel", [], []);
        Register("item", "Item Icon", ["Item"], []);
        Register("item_list", "Item List", ["Value", "RowHeight", "PrimaryLabel", "SecondaryLabel", "ShowItemId"], ["item_click", "item_primary", "item_secondary"]);
        Register("texture_region", "Texture Region", ["Texture", "TextureX", "TextureY", "TextureWidth", "TextureHeight"], []);
        Register("sprite", "Sprite", ["Texture", "FrameWidth", "FrameHeight", "Clips", "Value"], []);
        Register("shape", "Shape", ["Shape"], ["click"]);
        Register("sound", "Sound", ["Sound", "Autoplay", "Delay", "Volume", "Repeat", "Loop"], []);
        Register("tilemap", "Tilemap", ["Texture", "TileWidth", "TileHeight", "Columns", "Rows", "Tiles", "Solid"], [], advanced: true);
        Register("particles", "Particles", ["Effect", "Autoplay"], [], advanced: true);
        Register("collider", "Collider", ["Collider"], [], advanced: true);
        // The view: only what is inside the first visible camera is shown, scaled to fill the screen. Draws nothing.
        Register("camera", "Camera", [], [], advanced: true);
    }
    public static void Register(string type, string name, string[] properties, string[] events, bool advanced = false) => Controls.Add(type, new(type, name, properties, [.. events, "hover", "mouse_enter", "mouse_leave", .. AdvancedElementEvents], advanced));
    // Web and desktop only: physics collisions and inputs.
    public static readonly string[] AdvancedElementEvents = ["collide", "collide_stay", "collide_end", "trigger_enter", "trigger_stay", "trigger_exit", "state_changed"];
    public static readonly string[] ScreenEvents = ["open", "close", "tick", "key"];
    public static readonly string[] AdvancedScreenEvents = ["input_pressed", "input_released", "animation_end"];
    // Standard gamepad buttons (the same layout for Xbox, PlayStation and generic pads; only the labels differ).
    public static readonly string[] GamepadButtons = ["a", "b", "x", "y", "lb", "rb", "lt", "rt", "back", "start", "ls", "rs", "dpad_up", "dpad_down", "dpad_left", "dpad_right", "home"];
    /// <summary>A tag is lowercase letters, digits, underscore or dash, up to 24 characters; a control carries at most 8.</summary>
    public const int MaxTags = 8;
    public static bool Tag(string value) => value.Length is > 0 and <= 24 && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' || c is '_' or '-');
    // Touch: what a finger does to press an input. A drag reports its distance as a strength, like a stick.
    public static readonly string[] TouchActions = ["drag_left", "drag_right", "drag_up", "drag_down"];
    // Stick directions: "left_x-" is the left stick pushed left, "left_y+" pushed down, and so on.
    public static readonly string[] GamepadAxes = ["left_x-", "left_x+", "left_y-", "left_y+", "right_x-", "right_x+", "right_y-", "right_y+"];
}
public sealed class History<T>(Func<T> capture, Action<T> restore, Func<T,T>? clone = null, int limit = 200)
{
    // Oldest entries are dropped past `limit` so long sessions don't grow without bound.
    readonly LinkedList<T> undo = new(), redo = new();
    readonly Func<T,T> copy = clone ?? Json.Clone;
    public int UndoCount => undo.Count;
    public void Checkpoint() { Push(undo, copy(capture())); redo.Clear(); }
    public void Undo() { if (undo.Count == 0) return; Push(redo, copy(capture())); restore(Pop(undo)); }
    public void Redo() { if (redo.Count == 0) return; Push(undo, copy(capture())); restore(Pop(redo)); }
    public void Clear() { undo.Clear(); redo.Clear(); }
    void Push(LinkedList<T> stack, T value) { stack.AddLast(value); while (stack.Count > Math.Max(1, limit)) stack.RemoveFirst(); }
    static T Pop(LinkedList<T> stack) { var value = stack.Last!.Value; stack.RemoveLast(); return value; }
}
