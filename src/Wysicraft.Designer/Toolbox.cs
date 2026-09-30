using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wysicraft.Core;
using Wysicraft.Models;
using Validation = Wysicraft.Core.Validation;
namespace Wysicraft.Designer;

// The Toolbox: standard controls (they work everywhere, Minecraft included), shape stamps, and a collapsible
// Advanced section for web and desktop tools. Projects made for Minecraft only don't show Advanced at all.
public partial class MainWindow
{
    static readonly (string Tag, string Icon, string Label)[] ShapeStamps =
        [("shape:rectangle", "rectangle", "Rectangle"), ("shape:ellipse", "ellipse", "Circle / oval"), ("shape:triangle", "triangle", "Triangle"),
         ("shape:diamond", "diamond", "Diamond"), ("shape:hexagon", "hexagon", "Hexagon"), ("shape:star", "star", "Star")];
    static readonly (string Tag, string Icon, string Label, string Tip)[] SlotStamps =
        [("slots:crafting_table", "crafting_table", "Crafting table", "A 3 × 3 crafting grid, its result and the player's inventory and hotbar below, laid out like Minecraft's crafting table. Items move for real and recipes work."),
         ("slots:player", "slots", "Player inventory", "The player's 27 inventory slots (9 × 3). Items move for real."),
         ("slots:hotbar", "slots", "Hotbar", "The player's 9 hotbar slots."),
         ("slots:storage", "slots", "Storage slots", "Temporary slots (9 × 3). Whatever is left in them goes back to the player when the screen closes."),
         ("slots:crafting", "slots", "Crafting grid", "A 3 × 3 crafting grid using the game's recipes. Add a Crafting result beside it. Left-over items go back to the player."),
         ("slots:result", "slots", "Crafting result", "The crafting grid's output slot.")];
    static readonly (string Tag, string Icon, string Label, string Tip)[] AdvancedTools =
        [("collider:box", "collider", "Box collider", "An invisible wall or floor for physics. Draw it over your art."),
         ("collider:circle", "collider_circle", "Circle collider", "An invisible round collision area for physics."),
         ("collider:polygon", "collider_polygon", "Polygon collider", "An invisible collision area of any outline. Double-click it (or Advanced → Collider editor) to draw its points over a trace image."),
         ("tilemap", "tilemap", "Tilemap", "A grid painted from one tile sheet: floors, walls, whole levels. Only the tiles on screen cost anything, so the map can be far bigger than the view. Double-click it (or Advanced → Tile painter) to paint, and set Solid to say which tiles stop a body."),
         ("particles", "particles", "Particles", "Throws a named particle effect from its middle: sparks, smoke, rain, an explosion. The control itself is invisible. Make effects in Advanced → Particle maker."),
         ("camera", "camera", "Camera", "The view: only what is inside the camera is shown, scaled to fill the game (a smaller camera zooms in). Move or resize it from scripts (ui.setPosition, ui.setSize) or animations to scroll and zoom. Grab it by its edge or in Layers.")];

    string toolboxTarget = "";
    MenuItem? advancedMenu;
    void RebuildToolbox()
    {
        Toolbox.Items.Clear();
        ListBoxItem Item(string tag, string icon, string label, string? tip = null, double indent = 0) =>
            new() { Content = Icons.WithText(Icons.Has(icon) ? icon : "button", label), Tag = tag, ToolTip = tip, Padding = new Thickness(8 + indent, 5, 8, 5) };
        ListBoxItem Header(string text, bool? open = null, string? tip = null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (open != null) row.Children.Add(new TextBlock { Text = open == true ? "▾ " : "▸ ", Foreground = Brushes.LightGray });
            row.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Foreground = open == null ? Brushes.Gray : new SolidColorBrush(Color.FromRgb(0x91, 0xCF, 0xFF)) });
            return new ListBoxItem { Content = row, Focusable = open != null, IsHitTestVisible = open != null, Padding = new Thickness(6, 8, 6, 3), ToolTip = tip, Cursor = open != null ? System.Windows.Input.Cursors.Hand : null };
        }
        // A leaderboard page has its own toolbox: page pieces and the widgets that read the arcade's board.
        if (ui.IsLeaderboard) { LeaderboardToolbox((tag, icon, label, tip, indent) => Item(tag, icon, label, tip, indent), (text, open, tip) => Header(text, open, tip)); return; }
        foreach (var spec in Registry.Controls.Values.Where(s => !s.Advanced && s.Type is not ("shape" or "slots"))) Toolbox.Items.Add(Item(spec.Type, spec.Type, spec.DisplayName));
        Toolbox.Items.Add(Header("Shapes"));
        foreach (var (tag, icon, label) in ShapeStamps) Toolbox.Items.Add(Item(tag, icon, label, "A shape with a color or texture fill. Stretch it into ovals and rectangles.", 6));
        if (project.Manifest.Target != "web")
        {
            Toolbox.Items.Add(Header("Item slots · Minecraft"));
            foreach (var (tag, icon, label, tip) in SlotStamps) Toolbox.Items.Add(Item(tag, icon, label, tip + " Minecraft only.", 6));
        }
        if (advancedMenu != null) advancedMenu.Visibility = project.Manifest.Target == "minecraft" ? Visibility.Collapsed : Visibility.Visible;
        if (project.Manifest.Target == "minecraft") return;
        bool open = Prefs().AdvancedOpen;
        var header = Header("Advanced · web & desktop", open, "Tools for web page, Windows and Electron apps. Projects using them can't be exported to Minecraft.");
        header.PreviewMouseLeftButtonUp += (_, e) => { e.Handled = true; ToggleAdvancedToolbox(); };
        Toolbox.Items.Add(header);
        if (!open) return;
        foreach (var (tag, icon, label, tip) in AdvancedTools) Toolbox.Items.Add(Item(tag, icon, label, tip + " Web and desktop only.", 6));
        var more = new TextBlock { Text = "Inputs, Animations and screen Gravity: Advanced menu and Screen settings.", TextWrapping = TextWrapping.Wrap, Opacity = 0.6, FontSize = 11, Margin = new Thickness(14, 4, 6, 6), MaxWidth = 220 };
        Toolbox.Items.Add(new ListBoxItem { Content = more, Focusable = false, IsHitTestVisible = false });
    }

    void ToggleAdvancedToolbox()
    {
        var prefs = Prefs();
        if (!prefs.AdvancedOpen && !prefs.AdvancedNoteSeen)
        {
            var window = new Window { Owner = this, Title = "Advanced tools", SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
            var panel = new StackPanel { Margin = new Thickness(16), MaxWidth = 440 }; window.Content = panel;
            panel.Children.Add(new TextBlock { Text = "These tools work in web page, Windows app and Electron exports only — not inside Minecraft.", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            panel.Children.Add(new TextBlock { Text = "Physics, colliders, animations and gamepad inputs run on the player's own screen. A project that uses them can't be exported to Minecraft; Arcadia Studio lists what's in the way (and where) if you try.", TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 0, 0, 10) });
            var dontShow = new CheckBox { Content = "Don't show this again", Margin = new Thickness(0, 0, 0, 12) }; panel.Children.Add(dontShow);
            var ok = new Button { Content = "Show advanced tools", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(10, 3, 10, 3) };
            ok.Click += (_, _) => window.Close(); panel.Children.Add(ok);
            window.ShowDialog();
            if (dontShow.IsChecked == true) prefs.AdvancedNoteSeen = true;
        }
        prefs.AdvancedOpen = !prefs.AdvancedOpen; SavePrefs(); RebuildToolbox();
    }

    /// <summary>An item slots control's size from its grid: 18 GUI pixels a slot (a result is one big 26 × 26 slot).</summary>
    internal static void FitSlots(Element e)
    {
        if (e.SlotKind == "result") { e.Bounds.Width = 26; e.Bounds.Height = 26; return; }
        e.Bounds.Width = Math.Clamp(e.Columns, 1, 9) * Registry.SlotCell; e.Bounds.Height = Math.Clamp(e.Rows, 1, 6) * Registry.SlotCell;
    }
    void ApplyControlPreset(Element element, string preset)
    {
        switch (element.Type)
        {
            case "slots":
            {
                var (kind, columns, rows, start, id) = preset switch
                {
                    "hotbar" => ("player", 9, 1, 0, "hotbar"),
                    "storage" => ("storage", 9, 3, 0, "storage"),
                    "crafting" => ("crafting", 3, 3, 0, "crafting_grid"),
                    "result" => ("result", 1, 1, 0, "crafting_result"),
                    _ => ("player", 9, 3, 9, "inventory")
                };
                element.SlotKind = kind; element.Columns = columns; element.Rows = rows; element.SlotStart = start;
                element.Id = Unique(id); element.Text = ""; element.FillEnabled = false; element.BorderWidth = 0;
                FitSlots(element);
                break;
            }
            case "shape":
                if (Shapes.Names.Contains(preset)) element.Shape = preset;
                element.Id = Unique(element.Shape == "ellipse" ? "circle" : element.Shape);
                element.Text = ""; element.Bounds.Width = 40; element.Bounds.Height = 40; element.Background = "#5B8DEF"; element.BorderWidth = 0;
                break;
            case "sprite":
                element.Text = ""; element.Value = ""; element.Bounds.Width = 32; element.Bounds.Height = 32;
                break;
            case "sound":
                element.Text = ""; element.Value = ""; element.FillEnabled = false; element.Bounds.Width = 16; element.Bounds.Height = 16; element.Bounds.X = 0; element.Bounds.Y = 0;
                break;
            case "particles":
                element.Id = Unique("particles"); element.Text = ""; element.Value = ""; element.FillEnabled = false; element.BorderWidth = 0;
                element.Bounds.Width = 16; element.Bounds.Height = 16; element.Autoplay = false;
                element.Effect = project.Manifest.Particles.FirstOrDefault()?.Id ?? "";
                break;
            case "camera":
                // Starts as the whole screen: the same view as without a camera, ready to be moved or resized.
                element.Id = Unique("camera"); element.Text = ""; element.FillEnabled = false; element.BorderWidth = 0;
                element.Bounds.X = 0; element.Bounds.Y = 0; element.Bounds.Width = ui.Size.Width; element.Bounds.Height = ui.Size.Height;
                break;
            case "tilemap":
                // Sized to its own grid, and no fill: the empty cells have to show what is behind them.
                element.Id = Unique("tilemap"); element.Text = ""; element.FillEnabled = false; element.BorderWidth = 0;
                element.Bounds.X = 0; element.Bounds.Y = 0;
                element.Columns = Math.Max(1, (int)Math.Min(64, Math.Ceiling(ui.Size.Width / 16.0)));
                element.Rows = Math.Max(1, (int)Math.Min(64, Math.Ceiling(ui.Size.Height / 16.0)));
                Tilemaps.Fit(element);
                break;
            case "collider":
                element.Collider = preset is "circle" or "polygon" ? preset : "box"; element.Body = "static";
                element.Id = Unique(element.Collider == "box" ? "wall" : element.Collider == "circle" ? "bumper" : "outline");
                element.Text = ""; element.FillEnabled = false; element.BorderWidth = 0;
                element.Bounds.Width = element.Collider == "box" ? 120 : 40; element.Bounds.Height = element.Collider == "box" ? 12 : 40;
                if (element.Collider == "polygon") element.ColliderPoints = [new() { X = 0, Y = 40 }, new() { X = 20, Y = 0 }, new() { X = 40, Y = 40 }];
                break;
        }
    }
}
public partial class MainWindow
{
    internal void VerifyAdvancedEditor(string output)
    {
        project = new Project(); project.Manifest.Id = "advsmoke"; ui = project.Screens[0]; history.Clear(); selected.Clear(); toolboxTarget = ""; RefreshAll();
        var tags = Toolbox.Items.OfType<ListBoxItem>().Select(i => i.Tag as string).Where(t => t != null).ToList();
        if (!tags.Contains("shape:star") || !tags.Contains("sprite") || !tags.Contains("sound")) throw new Exception("Toolbox is missing new controls: " + string.Join(",", tags));
        var prefs = Prefs(); bool wasOpen = prefs.AdvancedOpen; prefs.AdvancedOpen = true; RebuildToolbox();
        if (!Toolbox.Items.OfType<ListBoxItem>().Any(i => i.Tag as string == "collider:polygon")) throw new Exception("Advanced toolbox missing");
        foreach (var tag in new[] { "shape:star", "shape:ellipse", "sprite", "collider:polygon", "collider:box", "sound", "camera", "particles" }) { AddControl(tag, 20, 20); Draw(); RefreshInspector(); }
        VerifyParticles();
        engineCaptureTo = System.IO.Path.GetDirectoryName(output);
        VerifyEngineEditors();
        VerifyComponentCards();
        // A new camera frames the whole screen, draws no fill, and only its edge is clickable on the canvas.
        var camera = ui.Elements.Single(e => e.Type == "camera");
        if (camera.Id != "camera1" || camera.FillEnabled || camera.Bounds.X != 0 || camera.Bounds.Width != ui.Size.Width || camera.Bounds.Height != ui.Size.Height) throw new Exception("Camera preset not applied");
        if (Surface.Children.OfType<Border>().Single(b => Equals(b.Tag, "camera1")).Background != null) throw new Exception("Camera would block clicks on the controls under it");
        if (!ui.Elements.Any(e => e.Id == "star1" && e.Shape == "star") || !ui.Elements.Any(e => e.Type == "collider" && e.Collider == "polygon" && e.ColliderPoints.Count == 3) || !ui.Elements.Any(e => e.Type == "sound"))
            throw new Exception("Presets not applied: " + string.Join(",", ui.Elements.Select(e => e.Id)));
        if (Validation.Errors(project).Count > 0) throw new Exception("New controls invalid: " + string.Join("; ", Validation.Errors(project)));
        // The Minecraft check lists the collider and blocks Minecraft exports; web exports are fine.
        if (MinecraftExportAllowed("Smoke test")) throw new Exception("Minecraft export not blocked by colliders");
        foreach (Window w in OwnedWindows.Cast<Window>().ToList()) if (w.Title == "Not ready for Minecraft") w.Close();
        project.Manifest.Target = "minecraft"; RefreshAll();
        if (Toolbox.Items.OfType<ListBoxItem>().Any(i => i.Tag as string == "collider:box") || advancedMenu?.Visibility == Visibility.Visible) throw new Exception("Advanced tools shown for a Minecraft-only project");
        prefs.AdvancedOpen = wasOpen; dirty = false;
        System.IO.File.WriteAllText(output, "PASS: toolbox sections, new control presets (camera, particles and tilemap) and properties, particle effects and maker, the Tile painter, State graphs and Shaders windows, the raised element cap and its advice, collapsible Properties sections that remember, component cards (Collider, Rigidbody, script components with editable tunables, removal), advanced hidden for Minecraft projects, Minecraft export check");
    }
}
