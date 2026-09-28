using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using Wysicraft.Core;
using Wysicraft.Models;
using Validation = Wysicraft.Core.Validation;
using Fonts = Wysicraft.Core.Fonts;
namespace Wysicraft.Designer;

// The editor side of the engine upgrade — the Tilemap control on the canvas, the Tile painter, the State graphs
// window and the Shaders window. Run as part of --smoke-advanced, because a WPF window only fails when it is built.
public partial class MainWindow
{
    /// <summary>Opens a modal window and closes it as soon as it has finished laying out, so a smoke test can build
    /// one without a person to click Cancel. Anything wrong in its construction throws out of here.</summary>
    internal string? engineCaptureTo;
    void OpenAndClose(Action open, string titleStartsWith)
    {
        Window? Find() => OwnedWindows.Cast<Window>().FirstOrDefault(w => w.Title.StartsWith(titleStartsWith));
        Window? seen = null;
        // A modal window never returns from ShowDialog until something closes it, so the close is queued first and
        // runs inside the dialog's own message pump. A window shown with Show() returns straight away and is closed
        // here instead; whichever it is, one of the two finds it.
        Dispatcher.InvokeAsync(() =>
        {
            seen ??= Find();
            if (seen != null && engineCaptureTo != null)
                try { CaptureWindow(seen, System.IO.Path.Combine(engineCaptureTo, titleStartsWith.Replace(' ', '_') + ".png")); } catch (Exception) { }
            seen?.Close();
        }, DispatcherPriority.ApplicationIdle);
        open();
        seen ??= Find();
        if (seen != null && engineCaptureTo != null && seen.IsVisible)
            try { CaptureWindow(seen, System.IO.Path.Combine(engineCaptureTo, titleStartsWith.Replace(' ', '_') + ".png")); } catch (Exception) { }
        if (seen == null) throw new Exception("No window titled \"" + titleStartsWith + "…\" opened");
        seen.Close();
    }

    /// <summary>The dropdown template replaces the system one, so the parts WPF requires have to be there: a
    /// missing PART_EditableTextBox breaks every editable ComboBox in the editor, silently and only at runtime.</summary>
    void VerifyComboTemplate()
    {
        foreach (bool editable in new[] { false, true })
        {
            var box = new ComboBox { IsEditable = editable, ItemsSource = new[] { "one", "two", "three" }, SelectedIndex = 0 };
            var host = new Window { Owner = this, Width = 200, Height = 80, ShowInTaskbar = false, Content = box };
            try
            {
                host.Show(); host.UpdateLayout(); box.ApplyTemplate();
                if (box.Template.FindName("PART_Popup", box) is not System.Windows.Controls.Primitives.Popup popup)
                    throw new Exception("The dropdown template has no PART_Popup");
                if (editable && box.Template.FindName("PART_EditableTextBox", box) is not TextBox)
                    throw new Exception("The dropdown template has no PART_EditableTextBox, so typing into one would not work");
                // The popup's IsOpen follows IsDropDownOpen through a template binding, which settles on a later
                // dispatcher pass rather than inside UpdateLayout — checking it straight away made this flaky.
                box.IsDropDownOpen = true; host.UpdateLayout();
                Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
                if (!popup.IsOpen) throw new Exception("The dropdown did not open");
                if (box.ItemContainerGenerator.ContainerFromIndex(0) is not ComboBoxItem item) throw new Exception("The dropdown made no items");
                item.ApplyTemplate();
                if (item.Foreground is not SolidColorBrush ink || ink.Color.R < 0x80) throw new Exception("Dropdown items are dark on a dark popup");
                box.IsDropDownOpen = false;
            }
            finally { host.Close(); }
        }
    }

    void VerifyEngineEditors()
    {
        VerifyComboTemplate();
        // ---- The Tilemap control ----
        AddControl("tilemap", 0, 0); Draw(); RefreshInspector();
        var map = ui.Elements.Single(e => e.Type == "tilemap");
        if (map.FillEnabled) throw new Exception("A new tilemap should not draw a fill: its empty cells have to show what is behind them");
        if (map.Columns < 1 || map.Rows < 1) throw new Exception("A new tilemap has no grid");
        if (map.Bounds.Width != map.Columns * map.TileWidth || map.Bounds.Height != map.Rows * map.TileHeight)
            throw new Exception($"A tilemap's box should match its grid, got {map.Bounds.Width}x{map.Bounds.Height} for {map.Columns}x{map.Rows} tiles of {map.TileWidth}x{map.TileHeight}");
        // Changing the grid moves the box with it, wherever the change came from.
        map.Columns = 7; map.Rows = 5; Draw();
        if (map.Bounds.Width != 7 * map.TileWidth || map.Bounds.Height != 5 * map.TileHeight) throw new Exception("Drawing did not re-fit the tilemap's box to its grid");

        // Without a sheet it draws a placeholder rather than throwing, and the painter says what is missing.
        if (RenderNewControl(map) is not Grid) throw new Exception("A tilemap with no sheet did not render");
        bool refused = false;
        try { ShowTilemapEditor(map); } catch (InvalidOperationException) { refused = true; }
        if (!refused) throw new Exception("The Tile painter should ask for a tile sheet before it opens");

        // With a sheet it paints its tiles, and the painter builds.
        byte[] sheet = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAACAAAAAQCAYAAAB3AH1ZAAAAHUlEQVRIx2NgGAWjYBSMglEwCkbBKBgFo2AUUAcAAAgAAAHrY2CtAAAAAElFTkSuQmCC");
        project.Assets["assets/textures/tiles.png"] = sheet;
        map.Texture = project.Manifest.Id + ":tiles.png";
        map.TileWidth = 16; map.TileHeight = 16; map.Columns = 4; map.Rows = 3; map.Tiles = "0 1 0 1 -1*4 1*4"; map.Solid = "1";
        Tilemaps.Fit(map); Draw(); RefreshInspector();
        if (RenderNewControl(map) is not Grid painted || painted.Children.OfType<Image>().FirstOrDefault()?.Source == null)
            throw new Exception("A tilemap with a sheet did not paint its tiles onto the canvas");
        if (Validation.Errors(project).Count > 0) throw new Exception("Tilemap invalid: " + string.Join("; ", Validation.Errors(project)));
        selected.Clear(); selected.Add(map.Id);
        OpenAndClose(() => ShowTilemapEditor(map), "Tile painter");

        // ---- State graphs ----
        var sprite = ui.Elements.First(e => e.Type == "sprite");
        sprite.Clips = "idle: 0; run: 0-1 @8";
        sprite.Texture = map.Texture; sprite.FrameWidth = 16; sprite.FrameHeight = 16;
        ui.StateGraphs.Add(new StateGraph
        {
            Id = "smoke_states", Target = sprite.Id, Start = "idle",
            States =
            [
                new() { Name = "idle", Clip = "idle", Transitions = [new() { To = "run", When = "input:move" }] },
                new() { Name = "run", Clip = "run", Transitions = [new() { To = "idle", When = "!input:move" }] }
            ]
        });
        if (Validation.Errors(project).Count > 0) throw new Exception("State graph invalid: " + string.Join("; ", Validation.Errors(project)));
        OpenAndClose(ShowStateGraphsWindow, "State graphs");
        // A graph pointing at a control that is not a sprite has to be reported rather than exported.
        ui.StateGraphs[0].Target = map.Id;
        if (!Validation.Errors(project).Any(i => i.Message.Contains("not a sprite"))) throw new Exception("A state graph on a tilemap should be an error");
        ui.StateGraphs[0].Target = sprite.Id;

        // ---- Shaders ----
        project.Manifest.Shaders.Add(new ShaderEffect { Id = "smoke_shader", Source = ShaderHeader + "\nvoid main() { outColor = texture(u_scene, v_uv); }\n" });
        ui.Shader = "smoke_shader";
        if (Validation.Errors(project).Count > 0) throw new Exception("Shader invalid: " + string.Join("; ", Validation.Errors(project)));
        OpenAndClose(ShowShadersWindow, "Shaders");
        // Every preset has to be the shape the runtime and Validate both expect.
        foreach (var (name, body) in ShaderPresets)
        {
            project.Manifest.Shaders[0].Source = ShaderHeader + "\n" + body;
            if (Validation.Errors(project).Count > 0) throw new Exception($"Shader preset \"{name}\" is invalid: " + string.Join("; ", Validation.Errors(project)));
        }
        // The screen picker lists the shader, and Screen settings builds with it chosen.
        selected.Clear(); RefreshInspector();

        // ---- The menus ----
        foreach (var id in new[] { "advanced.tilemap", "advanced.stateGraphs", "advanced.shaders" })
            if (!commands.Any(c => c.Id == id)) throw new Exception("Missing command " + id);
        if (!Toolbox.Items.OfType<ListBoxItem>().Any(i => i.Tag as string == "tilemap")) throw new Exception("Advanced toolbox is missing the Tilemap");

        // ---- The element cap and its advice ----
        var heavy = new Project { Manifest = { Id = "heavy", Target = "web" } };
        for (int i = 0; i < Limits.HeavyScreen + 1; i++) heavy.Screens[0].Elements.Add(new Element { Id = "e" + i, Type = "panel" });
        if (Validation.Errors(heavy).Count > 0) throw new Exception($"{Limits.HeavyScreen + 1} controls should be allowed now");
        if (!Validation.Advice(heavy).Any(i => i.Advice)) throw new Exception("A heavy screen should get advice");

        // ---- Stock tile sheets ----
        // Every sheet has to draw, land in the project as a real PNG, and leave the tilemap valid.
        foreach (var stock in StockSheets)
        {
            var bytes = DrawSheet(stock);
            var decoded = DecodeTexture(bytes);
            if (decoded.PixelWidth != stock.Tiles.Length * 16 || decoded.PixelHeight != 16)
                throw new Exception($"Tile sheet \"{stock.Id}\" is {decoded.PixelWidth}x{decoded.PixelHeight}, expected {stock.Tiles.Length * 16}x16");
            if (Tilemaps.Solid(stock.Solid).Count == 0) throw new Exception($"Tile sheet \"{stock.Id}\" marks nothing solid");
            if (Tilemaps.Solid(stock.Solid).Any(t => t >= stock.Tiles.Length)) throw new Exception($"Tile sheet \"{stock.Id}\" marks a tile it does not have solid");
            project.Assets[TextureAssets.Path(project.Manifest.Id, "tiles_" + stock.Id + ".png")] = bytes;
        }
        map.Texture = project.Manifest.Id + ":tiles_dungeon.png";
        map.Solid = StockSheets[0].Solid; Tilemaps.Fit(map); Draw();
        if (Validation.Errors(project).Count > 0) throw new Exception("A stock tile sheet left the project invalid: " + string.Join("; ", Validation.Errors(project)));
        if (RenderNewControl(map) is not Grid stocked || stocked.Children.OfType<Image>().FirstOrDefault()?.Source == null)
            throw new Exception("A tilemap on a stock sheet did not paint");

        // ---- Typefaces ----
        var text = ui.Elements.FirstOrDefault(e => e.Type is "label" or "button");
        if (text == null) { AddControl("label", 0, 0); text = ui.Elements.Last(e => e.Type == "label"); }
        foreach (var choice in FontChoices())
        {
            text.Font = choice;
            if (Validation.Errors(project).Count > 0) throw new Exception($"Font \"{choice}\" is not valid: " + string.Join("; ", Validation.Errors(project)));
            if (FamilyFor(choice) == null) throw new Exception($"No WPF family for \"{choice}\"");
        }
        if (!FontChoices().Contains("web:sans")) throw new Exception("A web project should offer the built-in families");
        // A font file in the project shows up as a choice and draws without throwing.
        project.Assets[Fonts.Path(project.Manifest.Id, "smoke.ttf")] = [0, 1, 2, 3];
        if (!FontChoices().Contains(Fonts.Resource(project.Manifest.Id, Fonts.Path(project.Manifest.Id, "smoke.ttf"))))
            throw new Exception("An imported font is missing from the Typeface list");
        text.Font = Fonts.Resource(project.Manifest.Id, Fonts.Path(project.Manifest.Id, "smoke.ttf"));
        if (FamilyFor(text.Font) == null) throw new Exception("A font WPF cannot read should fall back, not return null");
        Draw();   // an unreadable font file must not take the canvas down with it
        project.Assets.Remove(Fonts.Path(project.Manifest.Id, "smoke.ttf"));
        text.Font = "minecraft:default";
        // Minecraft-only projects are offered Minecraft's fonts and nothing else.
        var was = project.Manifest.Target; project.Manifest.Target = "minecraft";
        if (FontChoices().Count != Fonts.Minecraft.Length) throw new Exception("A Minecraft project should be offered only Minecraft's fonts");
        project.Manifest.Target = was;

        // ---- Behaviours ----
        // Applying each one through the editor, the way the menu does, and checking the canvas survives it.
        foreach (var behaviour in Behaviours.All)
        {
            AddControl("panel", 0, 0);
            var actor = ui.Elements.Last();
            var did = Behaviours.Apply(project, ui, actor, behaviour.Id);
            if (did.Count == 0) throw new Exception(behaviour.Name + " did nothing");
            Draw(); selected.Clear(); selected.Add(actor.Id); RefreshInspector();
            if (Validation.Errors(project).Count > 0) throw new Exception(behaviour.Name + " left the project invalid: " + string.Join("; ", Validation.Errors(project)));
        }
        // The menu offers them, grouped, for an ordinary control.
        var plain = ui.Elements.First(e => e.Type == "panel");
        if (!Behaviours.For(plain).Any(b => b.Id == "character_controller")) throw new Exception("A panel should be offered the character controller");

        ui.StateGraphs.Clear(); ui.Shader = ""; project.Manifest.Shaders.Clear();
    }
}
