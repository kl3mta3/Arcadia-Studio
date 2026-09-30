using Wysicraft.Models;
namespace Wysicraft.Core;

/// <summary>Ready-made bundles of settings you add to a control — what Unity calls components.
///
/// They are called behaviours here because "component" already means a reusable screen in this app, and two meanings
/// for one word in the same editor is worse than an unfamiliar name.
///
/// A behaviour is not a new kind of object: it only sets the properties that were always there (body, collider,
/// bounce, friction, trigger, tags) and, where it needs one, writes an ordinary script you can open and edit. There
/// is nothing to un-learn later and nothing hidden — after adding one, everything it did is visible in the Inspector
/// and in Scripts, which is the point. Adding the same behaviour twice is harmless: each one sets the same things.
///
/// In the editor they show as component cards. The physics ones are views over the body fields, so they are "on" a
/// control whenever the fields say so; the ones that write a script are listed in the control's Behaviours so the
/// card knows to show that script's tunables and can take the whole thing off again.</summary>
public static class Behaviours
{
    public sealed record Behaviour(string Id, string Name, string Group, string What, string Adds);
    /// <summary>What a script behaviour writes and wires: the script file's suffix, the function in it, and the
    /// event that runs it (on the screen, or on the control itself).</summary>
    public sealed record ScriptWiring(string Suffix, string Function, string Event, bool OnScreen);
    public static ScriptWiring? ScriptOf(string id) => id switch
    {
        // Movement scripts run from the tick event of the control they move, so several can tick side by side.
        // (They used to take the screen's one tick event, and a second one replaced the first: see MoveOldTicks.)
        "character_controller" => new("controller", "tick", "tick", false),
        "topdown_mover" => new("mover", "tick", "tick", false),
        "follower" => new("follower", "tick", "tick", false),
        "pickup" => new("pickup", "taken", "trigger_enter", false),
        _ => null
    };
    public static string ScriptPath(Element e, string id) => $"scripts/client/{e.Id}_{(ScriptOf(id) ?? throw new InvalidDataException("Not a script behaviour: " + id)).Suffix}.js";
    /// <summary>The script as it is first written, so the editor can tell an untouched one from an edited one.</summary>
    public static string ScriptSource(Element e, string id) => id switch
    {
        "character_controller" => CharacterController(e.Id),
        "topdown_mover" => TopDownMover(e.Id),
        "follower" => Follower(e.Id),
        "pickup" => Pickup(e.Id),
        _ => throw new InvalidDataException("Not a script behaviour: " + id)
    };

    public static readonly Behaviour[] All =
    [
        new("rigidbody", "Rigidbody", "Physics", "Falls with the screen's gravity, collides with static bodies and bounces. The everyday moving object.", "body = dynamic, a box collider, a little friction"),
        new("static_body", "Static body", "Physics", "A wall, a floor or a platform: never moves, and everything collides with it.", "body = static, a box collider"),
        new("kinematic_body", "Kinematic body", "Physics", "Moved by scripts or animations rather than by physics, and pushes dynamic bodies out of the way. Moving platforms, doors, a script-driven enemy.", "body = kinematic, a box collider"),
        new("bouncy", "Bouncy", "Physics", "Keeps most of its speed when it hits something.", "bounce = 0.8, friction = 0"),
        new("slippery", "Slippery", "Physics", "Ice: nothing slows down along it.", "friction = 0"),
        new("trigger_zone", "Trigger zone", "Physics", "Passes through everything and reports what entered instead — a checkpoint, a door sensor, a damage zone.", "body = static, trigger = true, no fill"),
        new("character_controller", "Character controller", "Movement", "A platformer character: left and right from your inputs, a jump that only works on the ground, and it stops when it lets go.", "a dynamic body, the left/right/jump inputs, screen gravity, and a script you can edit"),
        new("topdown_mover", "Top-down mover", "Movement", "Eight-way movement with no gravity, for a top-down game.", "a kinematic body, the four direction inputs, and a script you can edit"),
        new("follower", "Follower", "Movement", "Moves steadily towards the nearest control with a given tag. An enemy, a homing shot, a pet.", "a kinematic body, a target tag, and a script you can edit"),
        new("pickup", "Pickup", "Gameplay", "Disappears when something touches it and adds to a screen variable — coins, hearts, ammo.", "a trigger, the \"pickup\" tag, a score variable, and a script you can edit"),
    ];

    /// <summary>The behaviours worth offering for a control. Nothing is offered that the control could not use.</summary>
    public static IEnumerable<Behaviour> For(Element e)
    {
        foreach (var b in All)
        {
            // Sounds, emitters and cameras are not things in the world, so none of this applies to them.
            if (e.Type is "sound" or "particles" or "camera") yield break;
            // A collider is already a physics body; offering to make it one again would only confuse.
            if (e.Type == "collider" && b.Id is "rigidbody" or "character_controller" or "topdown_mover") continue;
            yield return b;
        }
    }

    /// <summary>Applies a behaviour, returning a line per thing it changed so the editor can say what it did.
    /// Scripts it writes are ordinary project scripts, named after the control so two of them never collide.</summary>
    public static List<string> Apply(Project project, UiDefinition ui, Element e, string id)
    {
        var did = new List<string>();
        var behaviour = All.FirstOrDefault(b => b.Id == id) ?? throw new InvalidDataException("Unknown behaviour: " + id);

        void Body(string body) { if (e.Body != body) { e.Body = body; did.Add("physics body = " + body); } }
        void Box() { if (e.Collider != "box" && e.Collider != "circle" && e.Collider != "polygon") { e.Collider = "box"; did.Add("collider = box"); } }
        void Number(string what, double from, double to, Action<double> set) { if (Math.Abs(from - to) > 0.0001) { set(to); did.Add($"{what} = {to:0.##}"); } }
        void Tag(string tag) { if (!e.Tags.Contains(tag) && e.Tags.Count < Registry.MaxTags) { e.Tags.Add(tag); did.Add("tag \"" + tag + "\""); } }
        void Variable(string name, string value) { if (!ui.Variables.ContainsKey(name)) { ui.Variables[name] = value; did.Add($"screen variable {name} = {value}"); } }
        void Input(string name, string[] keys, string[] buttons, string touch = "")
        {
            if (project.Manifest.Inputs.Any(i => i.Name == name)) return;
            project.Manifest.Inputs.Add(new GameInput { Name = name, Keys = [.. keys], Buttons = [.. buttons], Touch = touch });
            did.Add("input \"" + name + "\"");
        }
        void Script(string suffix, string source, string function, string eventName, bool onScreen)
        {
            string path = $"scripts/client/{e.Id}_{suffix}.js";
            // A script that is already there is left alone: the numbers in it may have been tuned.
            if (!project.Scripts.ContainsKey(path)) { project.Scripts[path] = source; did.Add($"script {path}, on the {(onScreen ? "screen's" : "control's")} {eventName} event"); }
            var events = onScreen ? ui.Events : e.Events;
            if (!events.TryGetValue(eventName, out var handler)) events[eventName] = handler = new UiEvent();
            handler.Client.Script = path; handler.Client.Function = function;
            if (!e.Behaviours.Contains(id)) { e.Behaviours.Add(id); did.Add("component " + behaviour.Name); }
        }
        void Gravity(double pull) { if (ui.Gravity == 0) { ui.Gravity = pull; did.Add($"screen gravity = {pull:0}"); } }
        void Ticking() { if (ui.TickInterval is 0 or > 33) { ui.TickInterval = 16; did.Add("screen tick interval = 16 ms"); } }

        switch (id)
        {
            case "rigidbody": Body("dynamic"); Box(); Number("friction", e.Friction, 0.2, v => e.Friction = v); break;
            case "static_body": Body("static"); Box(); break;
            case "kinematic_body": Body("kinematic"); Box(); break;
            case "bouncy": Number("bounce", e.Bounce, 0.8, v => e.Bounce = v); Number("friction", e.Friction, 0, v => e.Friction = v); break;
            case "slippery": Number("friction", e.Friction, 0, v => e.Friction = v); break;
            case "trigger_zone":
                if (e.Body.Length == 0) Body("static");
                if (!e.Trigger) { e.Trigger = true; did.Add("trigger = true"); }
                if (e.FillEnabled) { e.FillEnabled = false; did.Add("no fill (a zone is invisible in the game)"); }
                break;

            case "character_controller":
                Body("dynamic"); Box(); Gravity(900); Ticking();
                Number("friction", e.Friction, 0.2, v => e.Friction = v);
                Number("bounce", e.Bounce, 0, v => e.Bounce = v);
                Input("left", ["a", "left"], ["dpad_left"], "drag_left");
                Input("right", ["d", "right"], ["dpad_right"], "drag_right");
                Input("jump", ["space", "w", "up"], ["a"], "drag_up");
                Script("controller", CharacterController(e.Id), "tick", "tick", onScreen: false);
                break;

            case "topdown_mover":
                Body("kinematic"); Box(); Ticking();
                Input("left", ["a", "left"], ["dpad_left"], "drag_left");
                Input("right", ["d", "right"], ["dpad_right"], "drag_right");
                Input("up", ["w", "up"], ["dpad_up"], "drag_up");
                Input("down", ["s", "down"], ["dpad_down"], "drag_down");
                Script("mover", TopDownMover(e.Id), "tick", "tick", onScreen: false);
                break;

            case "follower":
                Body("kinematic"); Box(); Ticking();
                Script("follower", Follower(e.Id), "tick", "tick", onScreen: false);
                break;

            case "pickup":
                if (e.Body.Length == 0) Body("static");
                if (!e.Trigger) { e.Trigger = true; did.Add("trigger = true"); }
                Tag("pickup");
                Variable("score", "0");
                Script("pickup", Pickup(e.Id), "taken", "trigger_enter", onScreen: false);
                break;
        }
        if (did.Count == 0) did.Add("nothing — " + behaviour.Name + " was already set up on this control");
        return did;
    }

    /// <summary>Takes a script behaviour off a control: the event stops running its script, the script itself is
    /// deleted if it is still exactly what was written (an edited one is kept, and said so), and the control no
    /// longer lists it. Inputs, tags, variables and the physics body stay, because other things may use them; the
    /// physics cards are removed by changing the body fields instead.</summary>
    public static List<string> Remove(Project project, UiDefinition ui, Element e, string id)
    {
        var did = new List<string>();
        var wiring = ScriptOf(id);
        if (wiring != null)
        {
            string path = ScriptPath(e, id);
            // Wherever it is wired: the control's event, or (in a project from before MoveOldTicks) the screen's.
            foreach (var (events, whose) in new[] { (e.Events, "control's"), (ui.Events, "screen's") })
                if (events.TryGetValue(wiring.Event, out var handler) && handler.Client.Script == path)
                {
                    handler.Client.Script = ""; handler.Client.Function = "";
                    if (handler.Client.Actions.Count == 0 && handler.Server.Script.Length == 0 && handler.Server.Actions.Count == 0) events.Remove(wiring.Event);
                    did.Add($"the {whose} {wiring.Event} event no longer runs it");
                }
            if (project.Scripts.TryGetValue(path, out var source))
            {
                if (source == ScriptSource(e, id) || (id == "pickup" && source == OldPickup(e.Id))) { project.Scripts.Remove(path); did.Add("deleted " + path); }
                else did.Add("kept " + path + " because it was edited — delete it in Scripts if it is not wanted");
            }
        }
        if (e.Behaviours.Remove(id)) did.Add("removed the component");
        return did;
    }

    /// <summary>Brings a project made before movement scripts moved to their control's tick up to date, and takes the
    /// old Pickup script's debug message out: a screen tick still running a movement component's script moves to that
    /// control's own tick (unless the control already has one), and a Pickup script still exactly as first written is
    /// rewritten without the "Picked up by" popup. Edited scripts are left alone. Returns what it changed.</summary>
    public static List<string> MoveOldTicks(Project project)
    {
        var did = new List<string>();
        foreach (var ui in project.Screens)
            foreach (var e in ui.Elements)
                foreach (var id in e.Behaviours)
                {
                    var wiring = ScriptOf(id); if (wiring == null) continue;
                    string path = ScriptPath(e, id);
                    if (!wiring.OnScreen && ui.Events.TryGetValue(wiring.Event, out var old) && old.Client.Script == path && !e.Events.ContainsKey(wiring.Event))
                    {
                        e.Events[wiring.Event] = new UiEvent { Client = new() { Script = path, Function = old.Client.Function.Length > 0 ? old.Client.Function : wiring.Function } };
                        old.Client.Script = ""; old.Client.Function = "";
                        if (old.Client.Actions.Count == 0 && old.Server.Script.Length == 0 && old.Server.Actions.Count == 0) ui.Events.Remove(wiring.Event);
                        did.Add($"{ui.Id} › {e.Id}: {path} now runs from the control's {wiring.Event} event");
                    }
                    if (id == "pickup" && project.Scripts.TryGetValue(path, out var source) && source == OldPickup(e.Id))
                    {
                        project.Scripts[path] = Pickup(e.Id);
                        did.Add($"{path}: the \"Picked up by\" message is gone");
                    }
                }
        return did;
    }

    /// <summary>The numbers a script asks you to tune: every "var NAME = value;" line above its first function.
    /// Value is the text as written (a string keeps its quotes); Comment is what followed it on the line.</summary>
    public sealed record Tunable(string Name, string Value, string Comment, int Line);
    static readonly System.Text.RegularExpressions.Regex TunableLine = new(@"^(\s*)var\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.+?);\s*(//.*)?$");
    public static List<Tunable> Tunables(string source)
    {
        var found = new List<Tunable>();
        var lines = source.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^\s*function\b")) break;
            var m = TunableLine.Match(line);
            if (m.Success) found.Add(new(m.Groups[2].Value, m.Groups[3].Value.Trim(), m.Groups[4].Value.Trim(), i));
        }
        return found;
    }
    /// <summary>A tunable's value the way a person reads it: a string without its quotes, anything else as is.</summary>
    public static string TunableText(Tunable t) => IsQuoted(t.Value) ? t.Value[1..^1] : t.Value;
    static bool IsQuoted(string v) => v.Length >= 2 && v[0] is '\'' or '"' && v[^1] == v[0];
    /// <summary>The same script with one tunable's value replaced in place, keeping its indent and comment. A
    /// value that was a string stays one; a number has to stay a number.</summary>
    public static string WithTunable(string source, string name, string value)
    {
        var tunable = Tunables(source).FirstOrDefault(t => t.Name == name) ?? throw new InvalidDataException("No tunable named " + name);
        string written = value.Trim();
        if (IsQuoted(tunable.Value)) { char quote = tunable.Value[0]; if (!(IsQuoted(written) && written[0] == quote)) written = quote + written.Replace(quote.ToString(), "\\" + quote) + quote; }
        else if (!double.TryParse(written, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _) && written is not ("true" or "false"))
            throw new InvalidDataException(name + " is a number here; \"" + value + "\" is not one.");
        var lines = source.Split('\n');
        bool cr = lines[tunable.Line].EndsWith('\r');
        var m = TunableLine.Match(lines[tunable.Line].TrimEnd('\r'));
        string comment = m.Groups[4].Value.Length > 0 ? "  " + m.Groups[4].Value : "";
        lines[tunable.Line] = $"{m.Groups[1].Value}var {name} = {written};{comment}" + (cr ? "\r" : "");
        return string.Join('\n', lines);
    }

    // The scripts below are written to be read and changed: the numbers a person will want to tune are named
    // constants at the top, and everything else uses the same API the manual documents.
    static string CharacterController(string id) => $$"""
        // Character controller for "{{id}}", added by Advanced → Add behaviour.
        // Tune these three numbers; everything below them is ordinary script you can edit or delete.
        var SPEED = 140;        // sideways speed, pixels a second
        var JUMP = 330;         // how hard the jump pushes upward
        var GROUND_GRIP = 0.8;  // 0 keeps all speed when you let go, 1 stops instantly

        function tick(ctx) {
            var me = ctx.ui.getElement('{{id}}');
            var push = (ctx.input.isDown('right') ? 1 : 0) - (ctx.input.isDown('left') ? 1 : 0);
            var vx = push !== 0 ? push * SPEED : me.vx * (1 - GROUND_GRIP);
            var vy = me.vy;

            // Jump only from the ground: "touching something" plus "not already moving upward" is enough here,
            // and it costs nothing compared with a separate ground sensor.
            var onGround = ctx.physics.touching('{{id}}').length > 0 && Math.abs(me.vy) < 40;
            if (onGround && ctx.input.isDown('jump')) vy = -JUMP;

            ctx.ui.setVelocity('{{id}}', vx, vy);
        }
        """;

    static string TopDownMover(string id) => $$"""
        // Top-down movement for "{{id}}", added by Advanced → Add behaviour.
        var SPEED = 120;   // pixels a second

        function tick(ctx) {
            var x = (ctx.input.isDown('right') ? 1 : 0) - (ctx.input.isDown('left') ? 1 : 0);
            var y = (ctx.input.isDown('down') ? 1 : 0) - (ctx.input.isDown('up') ? 1 : 0);
            // Diagonals are scaled so moving corner-ways is not faster than straight.
            if (x !== 0 && y !== 0) { x *= 0.7071; y *= 0.7071; }
            ctx.ui.setVelocity('{{id}}', x * SPEED, y * SPEED);
        }
        """;

    static string Follower(string id) => $$"""
        // "{{id}}" moves towards the nearest control carrying TARGET_TAG. Tag something with it in the Inspector.
        var TARGET_TAG = 'player';
        var SPEED = 60;     // pixels a second
        var STOP_AT = 4;    // how close it gets before it stops

        function tick(ctx) {
            var me = ctx.ui.getElement('{{id}}');
            var best = null, bestDistance = Infinity;
            var targets = ctx.ui.findByTag(TARGET_TAG);
            for (var i = 0; i < targets.length; i++) {
                var them = ctx.ui.getElement(targets[i]);
                var d = Math.hypot(them.x - me.x, them.y - me.y);
                if (d < bestDistance) { bestDistance = d; best = them; }
            }
            if (!best || bestDistance <= STOP_AT) { ctx.ui.setVelocity('{{id}}', 0, 0); return; }
            ctx.ui.setVelocity('{{id}}', (best.x - me.x) / bestDistance * SPEED, (best.y - me.y) / bestDistance * SPEED);
        }
        """;

    static string Pickup(string id) => $$"""
        // "{{id}}" is taken when something enters it. Its trigger_enter event runs this.
        var WORTH = 1;

        function taken(ctx) {
            // ctx.value is the ID of whatever walked into it, so a pickup can ignore anything but the player.
            ctx.ui.setVisible('{{id}}', false);
            ctx.state.set('score', Number(ctx.state.get('score') || 0) + WORTH);
        }
        """;

    // The Pickup script as it was first written, with a debug message players saw. Kept to recognise it untouched.
    static string OldPickup(string id) => $$"""
        // "{{id}}" is taken when something enters it. Its trigger_enter event runs this.
        var WORTH = 1;

        function taken(ctx) {
            // ctx.value is the ID of whatever walked into it, so a pickup can ignore anything but the player.
            ctx.ui.setVisible('{{id}}', false);
            ctx.state.set('score', Number(ctx.state.get('score') || 0) + WORTH);
            ctx.message('Picked up by ' + ctx.value);
        }
        """;
}
