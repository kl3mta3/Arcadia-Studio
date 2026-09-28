using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wysicraft.Core;
using Wysicraft.Models;
namespace Wysicraft.Designer;

// Properties for sprites, shapes and the advanced (web and desktop) settings: physics and inputs.
public partial class MainWindow
{
    bool AdvancedAllowed => project.Manifest.Target != "minecraft";

    void Choice(Panel panel, string label, string current, IReadOnlyList<(string Value, string Text)> options, Action<string> apply, string? tip = null)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), ToolTip = tip };
        row.Children.Add(new TextBlock { Text = label, Width = FieldLabelWidth, VerticalAlignment = VerticalAlignment.Center });
        var box = new ComboBox { ItemsSource = options.Select(o => o.Text).ToArray(), SelectedIndex = Math.Max(0, options.ToList().FindIndex(o => o.Value == current)) };
        box.SelectionChanged += (_, _) => Guard(() => { var value = options[box.SelectedIndex].Value; if (value == current) return; Change(); apply(value); current = value; Draw(); RefreshInspector(); });
        row.Children.Add(box); panel.Children.Add(row);
    }
    static (string, string)[] Pairs(params string[] values) => values.Select(v => (v, char.ToUpperInvariant(v[0]) + v[1..].Replace('_', ' '))).ToArray();

    /// <summary>A picture from the project, chosen from a list rather than typed. Shaped like SoundPicker: the
    /// project's own images in a dropdown, and an Import button for one that isn't in the project yet.</summary>
    void TexturePicker(Panel panel, string label, string current, Action<string> apply, string? tip = null)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), ToolTip = tip ?? "A picture from this project. Import brings a new PNG in and uses it here." };
        row.Children.Add(new TextBlock { Text = label, Width = FieldLabelWidth, VerticalAlignment = VerticalAlignment.Center });
        var import = new Button { Content = "Import…", Margin = new Thickness(4, 0, 0, 0), ToolTip = "Bring a PNG into the project and use it here." };
        DockPanel.SetDock(import, Dock.Right); row.Children.Add(import);
        var pictures = ProjectPictures();
        // A Minecraft or both project can point at the game's own textures, so those are offered after the
        // project's own — but only the ones the loaded game jar actually has, so nothing here is a dead reference.
        if (project.Manifest.Target != "web") pictures.AddRange(MinecraftTextureIds().Take(4000));
        var box = new ComboBox { IsEditable = true, ItemsSource = pictures, Text = current, IsEnabled = true };
        if (pictures.Count == 0) box.ToolTip = "No pictures in this project yet. Import… brings one in, or draw one in the Pixel editor.";
        void Commit(string value)
        {
            value = (value ?? "").Trim(); if (value == current) return;
            if (value.Length > 0 && !pictures.Contains(value) && !Wysicraft.Core.Validation.Resource(value))
            { Log("Pick a picture from the list, or type one as " + project.Manifest.Id + ":name.png"); box.Text = current; return; }
            Change(); apply(value); current = value; Draw(); RefreshInspector();
        }
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is string s) Commit(s); };
        box.LostKeyboardFocus += (_, _) => Commit(box.Text);
        import.Click += (_, _) => Guard(() => { if (ImportOnePicture() is string picked) { Change(); apply(picked); current = picked; Draw(); RefreshInspector(); } });
        row.Children.Add(box); panel.Children.Add(row);
    }

    /// <summary>Minecraft’s own textures, for a project that can use them. Empty until a game jar is loaded.</summary>
    List<string> MinecraftTextureIds() { try { return [.. minecraftAssets.TextureIds]; } catch { return []; } }

    /// <summary>A control-specific property with a better editor than a text box, or false to use the plain field.</summary>
    bool SpecialField(Element e, string name)
    {
        switch (name)
        {
            case "Texture":
            {
                TexturePicker(Properties, "Texture", e.Texture, v => e.Texture = v, e.Type switch
                {
                    "tilemap" => "The tile sheet this map is painted from, cut into TileWidth × TileHeight tiles. Import brings a PNG in; Make a tile sheet… draws a ready-made one.",
                    "sprite" => "The sprite sheet, cut into FrameWidth × FrameHeight frames.",
                    _ => "A picture from this project. Import brings a new PNG in and uses it here."
                });
                if (e.Type == "tilemap")
                {
                    var stock = new Button { Content = "Make a tile sheet…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 2, 0, 4), ToolTip = "Draws a ready-made sheet — dungeon, cave, grass, ice, brick or a plain palette — into the project and uses it here." };
                    stock.Click += (_, _) => Guard(() => ShowTileSheetMaker(e));
                    Properties.Children.Add(stock);
                }
                return true;
            }
            case "Sound":
                SoundPicker(Properties, "Sound", e.Sound, v => e.Sound = v);
                Properties.Children.Add(new TextBlock { Text = "Autoplay starts it Delay ms after the screen opens (0 = straight away). Otherwise an action or script sets its value to play or stop.", TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11 });
                return true;
            case "Effect":
            {
                var effects = new List<(string, string)> { ("", "(choose an effect)") };
                effects.AddRange(project.Manifest.Particles.Select(f => (f.Id, f.Id + "  ·  " + f.Emission)));
                Choice(Properties, "Effect", e.Effect, effects, v => e.Effect = v);
                var make = new Button { Content = project.Manifest.Particles.Count == 0 ? "Particle maker…" : "Edit in Particle maker…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 2, 0, 4) };
                make.Click += (_, _) => Guard(() => OpenParticleMaker(e.Effect.Length > 0 ? e.Effect : null));
                Properties.Children.Add(make);
                Properties.Children.Add(new TextBlock { Text = "The control is invisible: what you see is what it throws, from its middle. Autoplay fires it when the screen opens; otherwise a Set value action (play, stop or clear) or ui.emit('" + e.Id + "') from a script.", TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11 });
                return true;
            }
            case "Tiles":
            {
                int painted;
                try { painted = Tilemaps.Read(e.Tiles, Math.Max(1, e.Columns), Math.Max(1, e.Rows)).Count(t => t >= 0); } catch { painted = -1; }
                Properties.Children.Add(new TextBlock { Text = painted < 0 ? "Tiles: the data can't be read" : $"{painted} of {Math.Max(1, e.Columns) * Math.Max(1, e.Rows)} cells painted", Margin = new Thickness(0, 4, 0, 2), Opacity = 0.8 });
                var paint = new Button { Content = "Paint tiles…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 0, 0, 4) };
                paint.Click += (_, _) => Guard(() => ShowTilemapEditor(e));
                Properties.Children.Add(paint);
                Properties.Children.Add(new TextBlock { Text = "The map is drawn from the tile sheet in Texture, cut into TileWidth × TileHeight tiles numbered left to right. Only the tiles on screen cost anything to draw, so a map can be far larger than the view.", TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11 });
                return true;
            }
            case "Solid" when e.Type == "tilemap":
            {
                // Without this the box beside "Solid" is an empty field with nothing saying what belongs in it.
                CommitField("Solid", e.Solid, v => { Change(); e.Solid = v; Draw(); RefreshInspector(); },
                    "Which tile numbers stop a physics body, as 1,3,5-9. Empty means the whole map is scenery. The Tile painter marks solid tiles in red.");
                int count = -1;
                try { count = Tilemaps.Solid(e.Solid).Count; } catch (InvalidDataException) { }
                Properties.Children.Add(new TextBlock
                {
                    Text = count < 0 ? "That is not a list of tile numbers — use 1,3,5-9."
                        : count == 0 ? "Nothing is solid: bodies pass straight through the map. Type tile numbers here, or mark them in the Tile painter."
                        : count + (count == 1 ? " tile stops a body." : " tiles stop a body."),
                    TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11, Margin = new Thickness(4, 0, 0, 4)
                });
                return true;
            }
            case "Shape":
                Choice(Properties, "Shape", e.Shape, Pairs(Shapes.Names), v => e.Shape = v, "Stretch the control to make ovals and rectangles.");
                return true;
            case "Layer":
                LayerFields(Properties, e);
                return true;
            case "Collider":
                // Shown on the Collider card under Advanced, with the trigger and layer settings beside it.
                return AdvancedAllowed;
            case "Value" when e.Type == "sprite":
            {
                Dictionary<string, SpriteClips.Clip> clips; try { clips = SpriteClips.Parse(e.Clips); } catch { clips = []; }
                var options = new List<(string, string)> { ("", "(first clip)") }; options.AddRange(clips.Keys.Select(k => (k, k)));
                Choice(Properties, "Playing", e.Value, options, v => e.Value = v, "The clip shown when the screen opens. Scripts switch clips with ui.play(id, 'run') or set_value.");
                return true;
            }
            case "Clips":
                Field(Properties, "Clips", e, "Clips");
                Properties.Children.Add(new TextBlock { Text = "Frames count left to right, top to bottom. Example:  idle: 0;  run: 1-6 @12;  jump: 7,8,9 @8 once", TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });
                var sheetEditor = new Button { Content = "Sprite sheet editor…", ToolTip = "Make clips by clicking frames on the sheet (or double-click the sprite on the canvas)", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 0, 0, 4) };
                sheetEditor.Click += (_, _) => Guard(() => ShowSpriteSheetEditor(e)); Properties.Children.Add(sheetEditor);
                return true;
        }
        return false;
    }
    static List<Vertex> DefaultPolygon(Element e) => [new() { X = 0, Y = e.Bounds.Height }, new() { X = e.Bounds.Width / 2, Y = 0 }, new() { X = e.Bounds.Width, Y = e.Bounds.Height }];

    void BuildAdvancedFields(Element e)
    {
        if (!AdvancedAllowed) return;
        Heading(Properties, "Advanced · web & desktop");
        if (e.Type == "camera")
        {
            double zoom = Math.Min(ui.Size.Width / Math.Max(1, e.Bounds.Width), ui.Size.Height / Math.Max(1, e.Bounds.Height));
            Properties.Children.Add(new TextBlock { Text = $"Only what is inside this camera is shown, scaled to fill the game (zoom ×{zoom:0.##}). Nothing outside it is drawn or clickable. Make it smaller to zoom in. Scripts move it with ui.setPosition('{e.Id}', x, y) and zoom with ui.setSize('{e.Id}', w, h), and animations can move and resize it. If a screen has several cameras, the first visible one is used, so a script can switch views with ui.setVisible.", TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 11, Margin = new Thickness(4, 0, 4, 6) });
            return;
        }
        // Components: the Unity "add a component" move. Collider and Rigidbody are cards over the physics fields
        // that were always there; the script ones are recorded on the control and show their script's tunables.
        BuildComponentCards(e);
        var inputs = new List<(string, string)> { ("", "(none)") }; inputs.AddRange(project.Manifest.Inputs.Select(i => (i.Name, i.Name)));
        Choice(Properties, "Presses input", e.Input, inputs, v => e.Input = v, "Pressing this control also presses the input, for on-screen controls. Its own click and hover events still fire.");
        var link = new Button { Content = "Inputs…", HorizontalAlignment = HorizontalAlignment.Left }; link.Click += (_, _) => Guard(ShowInputsWindow); Properties.Children.Add(link);
    }

    /// <summary>Event names to offer: advanced ones only for projects that can use them.</summary>
    string[] EventNames(string[] names, bool screen) =>
        [.. names.Where(n => AdvancedAllowed || !Registry.AdvancedElementEvents.Contains(n)), .. (screen && AdvancedAllowed ? Registry.AdvancedScreenEvents : [])];
    static bool ClientOnlyEvent(string name) => name is "tick" or "key" or "input_pressed" or "input_released" or "animation_end" or "collide" or "collide_stay" or "collide_end" or "trigger_enter" or "trigger_stay" or "trigger_exit";
    static string? AdvancedEventHelp(string name) => name switch
    {
        "input_pressed" => "Web & desktop: runs when one of your inputs (Advanced → Inputs) is pressed. The script's value is the input's name.",
        "input_released" => "Web & desktop: runs when an input is let go. The script's value is the input's name.",
        "animation_end" => "Web & desktop: runs when an animation that doesn't loop finishes. The script's value is the animation's ID.",
        "collide" => "Web & desktop: runs when this physics body starts touching another. The script's value is the other control's ID.",
        "collide_stay" => "Web & desktop: runs every 250 ms while this physics body keeps touching another. The script's value is the other control's ID.",
        "collide_end" => "Web & desktop: runs when this physics body stops touching another. The script's value is the other control's ID.",
        "trigger_enter" => "Web & desktop: runs when something enters a trigger (or this control enters one). The script's value is the other control's ID.",
        "trigger_stay" => "Web & desktop: runs every 250 ms while it stays inside the trigger. The script's value is the other control's ID.",
        "trigger_exit" => "Web & desktop: runs when it leaves the trigger. The script's value is the other control's ID.",
        _ => null
    };
}
