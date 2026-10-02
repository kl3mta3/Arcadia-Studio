using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wysicraft.Core;
using Wysicraft.Models;
using Wysicraft.Packaging;
using Validation = Wysicraft.Core.Validation;
namespace Wysicraft.Designer;

// Advanced → Particles (web and desktop): named effects kept on the manifest, made in the Particle maker and played by
// a Particles control. One list for the project, so the same sparks can be thrown on any screen.
public partial class MainWindow
{
    /// <summary>Opens the maker on an existing effect, or on a preset when there is nothing to open.</summary>
    internal ParticleMaker OpenParticleMaker(string? effectId = null)
    {
        var existing = effectId != null ? project.Manifest.Particles.FirstOrDefault(p => p.Id == effectId) : null;
        var effect = existing != null ? Json.Clone(existing) : Particles.Preset("sparks");
        var maker = new ParticleMaker(this, effect, existing?.Id, SaveParticleEffect, ProjectPictures, ImportOnePicture);
        OpenBeside(maker);
        return maker;
    }

    /// <summary>A particle effect chooser: (none), the project's effects, the built-in templates (choosing one adds it to
    /// the project), Edit… (the Particle maker) and Import… (an exported effect, or effects from another project).</summary>
    void ParticlePicker(Panel panel, string label, string current, Action<string> apply, string? tip = null)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), ToolTip = tip };
        row.Children.Add(new TextBlock { Text = label, Width = FieldLabelWidth, VerticalAlignment = VerticalAlignment.Center });
        var import = new Button { Content = "Import…", Margin = new Thickness(4, 0, 0, 0), ToolTip = "An effect exported from the Particle maker (.particles.json), or effects from another Arcadia Studio project." };
        var edit = new Button { Content = "Edit…", Margin = new Thickness(4, 0, 0, 0), ToolTip = "Open the chosen effect in the Particle maker (or make a new one)." };
        // On their own line under the list: Properties is narrow, and beside it they left the list no room for a name.
        var below = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 3) };
        edit.Padding = import.Padding = new Thickness(8, 0, 8, 0); below.Children.Add(edit); below.Children.Add(import);
        const string None = "(none)", Template = "  (template)";
        var items = new List<string> { None };
        items.AddRange(project.Manifest.Particles.Select(p => p.Id));
        items.AddRange(Particles.Presets.Where(p => p != "random" && project.Manifest.Particles.All(x => x.Id != p)).Select(p => p + Template));
        var box = new ComboBox { ItemsSource = items, SelectedItem = current.Length == 0 ? None : items.Contains(current) ? current : null };
        if (box.SelectedItem == null && current.Length > 0) { items.Insert(1, current); box.ItemsSource = null; box.ItemsSource = items; box.SelectedItem = current; }
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is not string chosen) return;
            string value = chosen == None ? "" : chosen;
            if (value.EndsWith(Template, StringComparison.Ordinal))
            {
                // A template becomes an effect of the project's own, ready to change in the Particle maker.
                string preset = value[..^Template.Length];
                try { var fx = Particles.Preset(preset); fx.Id = preset; value = SaveParticleEffect(new ParticleMaker.SaveRequest(fx, true, null)); }
                catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException) { Log(ex.Message); return; }
            }
            if (value == current) return;
            Change(); apply(value); current = value;
        };
        edit.Click += (_, _) => Guard(() => OpenParticleMaker(current.Length > 0 ? current : null));
        import.Click += (_, _) => Guard(() => { var added = ImportParticleEffects(); if (added.Count > 0) { Change(); apply(added[0]); current = added[0]; } });
        row.Children.Add(box); panel.Children.Add(row); panel.Children.Add(below);
    }

    /// <summary>Imports particle effects: a .particles.json from the Particle maker's Export… (one effect or a list), or
    /// effects chosen from another project file. Returns their names in this project.</summary>
    internal List<string> ImportParticleEffects()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Particle effects or projects|*.json;*.arcadia;*.wysicraftproj|Particle effects (*.particles.json)|*.json|Arcadia Studio projects|*.arcadia;*.wysicraftproj", Title = "Import particle effects" };
        if (dialog.ShowDialog(this) != true) return [];
        List<ParticleEffect> found;
        string file = dialog.FileName;
        if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            string text = File.ReadAllText(file).TrimStart();
            try { found = text.StartsWith('[') ? Json.Read<List<ParticleEffect>>(text) : [Json.Read<ParticleEffect>(text)]; }
            catch (System.Text.Json.JsonException ex) { throw new InvalidDataException(Path.GetFileName(file) + " isn't a particle effect: " + ex.Message); }
        }
        else
        {
            var other = ProjectStore.Load(file);
            if (other.Manifest.Particles.Count == 0) throw new InvalidOperationException(Path.GetFileName(file) + " has no particle effects.");
            found = other.Manifest.Particles.Count == 1 ? other.Manifest.Particles : PickParticleEffects(other.Manifest.Particles, Path.GetFileName(file));
        }
        var added = new List<string>();
        foreach (var fx in found)
        {
            if (string.IsNullOrWhiteSpace(fx.Id)) fx.Id = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file));
            if (fx.Shape == "texture" && fx.Texture.Length > 0 && !ProjectPictures().Contains(fx.Texture))
            { Log($"{fx.Id} used the picture {fx.Texture}, which isn't in this project: it's drawn as circles until you choose a picture in the Particle maker."); fx.Shape = "circle"; }
            added.Add(SaveParticleEffect(new ParticleMaker.SaveRequest(fx, true, null)));
        }
        if (added.Count > 0) Log("Imported particle effect" + (added.Count > 1 ? "s " : " ") + string.Join(", ", added) + ".");
        return added;
    }
    List<ParticleEffect> PickParticleEffects(List<ParticleEffect> effects, string from)
    {
        var window = new Window { Owner = this, Title = "Import particle effects from " + from, Width = 380, Height = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        var root = new DockPanel { Margin = new Thickness(12) }; window.Content = root;
        var head = new TextBlock { Text = "Choose the effects to bring into this project:", Margin = new Thickness(0, 0, 0, 6) }; DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var ok = new Button { Content = "Import", IsDefault = true, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(12, 3, 12, 3) };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        var list = new ListBox { SelectionMode = SelectionMode.Multiple, ItemsSource = effects.Select(x => x.Id).ToList() };
        root.Children.Add(list);
        ok.Click += (_, _) => window.DialogResult = list.SelectedItems.Count > 0;
        return window.ShowDialog() == true ? effects.Where(x => list.SelectedItems.Contains(x.Id)).ToList() : [];
    }

    /// <summary>Puts the effect on the manifest and returns the name it ended up with. "Save as new" never replaces
    /// another effect: a name already taken gets a number, the way a new screen or asset does.</summary>
    string SaveParticleEffect(ParticleMaker.SaveRequest request)
    {
        var effect = request.Effect; effect.Check();
        var list = project.Manifest.Particles;
        var replacing = request.AsNew ? null : list.FirstOrDefault(p => p.Id == request.OriginalId);
        if (list.Any(p => p.Id == effect.Id && p != replacing))
        {
            string stem = effect.Id; int n = 2;
            while (list.Any(p => p.Id == stem + "_" + n && p != replacing)) n++;
            effect.Id = stem + "_" + n;
        }
        if (list.Count >= Particles.MaxPerProject && replacing == null) throw new InvalidDataException($"A project keeps at most {Particles.MaxPerProject} particle effects.");
        Change();
        if (replacing != null)
        {
            // Renaming an effect carries the controls that play it, so nothing silently stops working.
            if (replacing.Id != effect.Id)
                foreach (var e in project.Screens.SelectMany(s => s.Elements)) if (e.Type == "particles" && e.Effect == replacing.Id) e.Effect = effect.Id;
            list[list.IndexOf(replacing)] = effect;
        }
        else list.Add(effect);
        RefreshInspector(); Draw();
        Log((replacing != null ? "Saved particle effect " : "Added particle effect ") + effect.Id + ".");
        return effect.Id;
    }

    /// <summary>Every picture in the project, as the resource IDs a texture particle needs.</summary>
    List<string> ProjectPictures() =>
        [.. project.Assets.Keys.Where(k => k.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && !TextureAssets.IsEditorOnly(k))
            .Select(k => project.Manifest.Id + ":" + k[("assets/" + project.Manifest.Id + "/").Length..])
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Asks for a PNG, brings it into the project, and gives back its resource ID.</summary>
    string? ImportOnePicture()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "PNG image|*.png", Title = "Add a picture for this effect" };
        if (dialog.ShowDialog(this) != true) return null;
        var added = ImportImageFiles([dialog.FileName]);
        if (added.Count == 0) return null;
        // ImportImageFiles gives asset paths; the effect wants the resource ID.
        return project.Manifest.Id + ":" + added[0][("assets/" + project.Manifest.Id + "/").Length..];
    }

    /// <summary>Advanced → Particles: the project's effects, with what plays each one.</summary>
    void ShowParticlesWindow()
    {
        var window = new Window { Owner = this, Title = "Particles · web & desktop", Width = 620, Height = 480, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SetResourceReference(StyleProperty, typeof(Window));
        var root = new DockPanel { Margin = new Thickness(12) }; window.Content = root;
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Text = "An effect is a named burst or stream of particles. Make one in the Particle maker, then add a Particles control and choose it. Fire it with the control's Play on open, a Set value action (play, stop or clear), or ui.emit('id') from a script." });
        var list = new StackPanel(); root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Insert(1, bottom);
        void Fill()
        {
            list.Children.Clear();
            if (project.Manifest.Particles.Count == 0) list.Children.Add(new TextBlock { Text = "No effects yet. New effect… starts one from a preset.", Opacity = 0.7, Margin = new Thickness(0, 8, 0, 0) });
            foreach (var effect in project.Manifest.Particles.ToList())
            {
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                int used = project.Screens.SelectMany(s => s.Elements).Count(e => e.Type == "particles" && e.Effect == effect.Id);
                var swatch = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                swatch.Background = ColorPicker.TryColor(effect.ColorStart, out var c) ? new SolidColorBrush(c) : Brushes.Gray;
                DockPanel.SetDock(swatch, Dock.Left); row.Children.Add(swatch);
                var buttons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(buttons, Dock.Right);
                var edit = new Button { Content = "Edit", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(4, 0, 0, 0) };
                edit.Click += (_, _) => { window.Close(); OpenParticleMaker(effect.Id); };
                var remove = new Button { Content = "Delete", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(4, 0, 0, 0) };
                remove.Click += (_, _) =>
                {
                    if (used > 0 && MessageBox.Show(window, $"{used} control(s) play {effect.Id}. Delete it anyway?", "Particles", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
                    Change(); project.Manifest.Particles.Remove(effect); RefreshInspector(); Draw(); Fill();
                };
                buttons.Children.Add(edit); buttons.Children.Add(remove); row.Children.Add(buttons);
                row.Children.Add(new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Text = $"{effect.Id}   ·   {effect.Emission}, {effect.Count}{(effect.Emission == "stream" ? "/s" : "")}, {effect.Life:0.00} s   ·   " + (used == 0 ? "not used yet" : used + (used == 1 ? " control" : " controls"))
                });
                list.Children.Add(row);
            }
        }
        var add = new Button { Content = "New effect…", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
        add.Click += (_, _) => { window.Close(); OpenParticleMaker(); };
        var close = new Button { Content = "Close", Padding = new Thickness(10, 2, 10, 2) }; close.Click += (_, _) => window.Close();
        bottom.Children.Add(add); bottom.Children.Add(close);
        Fill(); window.ShowDialog();
    }
}

public partial class MainWindow
{
    /// <summary>Part of --smoke-advanced: an emitter with no effect is an error, saving from the maker fixes it,
    /// renaming carries the controls that play it, every preset survives its own validation, and the maker window
    /// builds and animates.</summary>
    void VerifyParticles()
    {
        var emitter = ui.Elements.Single(e => e.Type == "particles");
        if (emitter.Autoplay || emitter.FillEnabled) throw new Exception("A new emitter should not autoplay or draw a fill");
        if (!Validation.Errors(project).Any(i => i.Message.Contains("Particles needs an effect"))) throw new Exception("An emitter with no effect should be an error");

        // Every preset has to pass the same check the runtime relies on, at any roll of the dice.
        var random = new Random(7);
        foreach (var name in Particles.Presets)
            for (int i = 0; i < 25; i++)
            {
                var candidate = Particles.Preset(name, random);
                try { candidate.Check(); } catch (Exception ex) { throw new Exception($"Preset {name} is invalid: {ex.Message}"); }
                try { candidate.Mutate(random).Check(); } catch (Exception ex) { throw new Exception($"Mutated {name} is invalid: {ex.Message}"); }
            }

        // Saving through the maker's own callback is what the window does, so this covers that path too.
        var saved = SaveParticleEffect(new ParticleMaker.SaveRequest(Particles.Preset("explosion", random), false, null));
        emitter.Effect = saved; RefreshInspector();
        if (Validation.Errors(project).Count > 0) throw new Exception("Emitter still invalid: " + string.Join("; ", Validation.Errors(project)));

        // A second effect of the same name is numbered, never a silent overwrite.
        var second = SaveParticleEffect(new ParticleMaker.SaveRequest(Particles.Preset("explosion", random), true, null));
        if (second == saved || project.Manifest.Particles.Count != 2) throw new Exception("Save as new overwrote an effect: " + second);

        // Renaming carries the controls that play it.
        var renamed = Json.Clone(project.Manifest.Particles[0]); renamed.Id = "boom";
        SaveParticleEffect(new ParticleMaker.SaveRequest(renamed, false, saved));
        if (emitter.Effect != "boom") throw new Exception("Renaming an effect left its control pointing at " + emitter.Effect);

        // The simulation moves particles and retires them; the editor preview and the runtime share this arithmetic.
        var fx = project.Manifest.Particles[0]; var live = new List<Particles.Particle>();
        for (int i = 0; i < fx.Count; i++) live.Add(fx.Spawn(random, 100, 100));
        double moved = 0;
        for (int step = 0; step < 240; step++) { for (int i = live.Count - 1; i >= 0; i--) if (!fx.Step(live[i], 1 / 60.0)) live.RemoveAt(i); }
        foreach (var p in live) moved += Math.Abs(p.X - 100) + Math.Abs(p.Y - 100);
        if (live.Count != 0) throw new Exception($"{live.Count} particles outlived a 4-second run");
        var one = fx.Spawn(random, 100, 100); fx.Step(one, 0.1);
        if (Math.Abs(one.X - 100) + Math.Abs(one.Y - 100) < 0.001) throw new Exception("Particles are not moving");
        var (size, _, alpha) = fx.At(one);
        if (size <= 0 || alpha < 0 || alpha > 1) throw new Exception($"Bad appearance: size {size}, alpha {alpha}");

        // The maker window itself builds, lays out and paints a frame.
        var maker = new ParticleMaker(this, Particles.Preset("sparks", random), null, SaveParticleEffect, ProjectPictures, () => null);
        maker.Show(); maker.UpdateLayout(); maker.Close();

        project.Manifest.Particles.Clear(); emitter.Effect = ""; ui.Elements.Remove(emitter);
    }
}

public partial class MainWindow
{
    /// <summary>What collision layer n is called, or its number when it has no name.</summary>
    internal string LayerName(int n) =>
        n >= 0 && n < project.Manifest.CollisionLayers.Count && project.Manifest.CollisionLayers[n].Length > 0
            ? n + " · " + project.Manifest.CollisionLayers[n] : "Layer " + n;

    /// <summary>Advanced → Collision layers: names for the sixteen, so a mask reads as words rather than numbers.</summary>
    void ShowCollisionLayers()
    {
        var names = new List<string>(project.Manifest.CollisionLayers);
        while (names.Count < 16) names.Add("");
        var window = new Window { Owner = this, Title = "Collision layers · web & desktop", Width = 460, Height = 560, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SetResourceReference(StyleProperty, typeof(Window));
        var root = new DockPanel { Margin = new Thickness(12) }; window.Content = root;
        var note = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 0, 0, 8),
            Text = "Naming only: a body sits on one layer and watches the layers you tick on it. Both bodies have to be watching each other for a contact to count, so a hitbox on its own layer never reports hitting another hitbox. Leave a name blank to show it as a number." };
        DockPanel.SetDock(note, Dock.Top); root.Children.Add(note);
        var list = new StackPanel();
        var boxes = new List<TextBox>();
        for (int i = 0; i < 16; i++)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock { Text = i.ToString(), Width = 28, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
            var box = new TextBox { Text = names[i], MaxLength = 24 };
            boxes.Add(box); row.Children.Add(box); list.Children.Add(row);
        }
        // The matrix: one tick per pair of layers, and a pair is a pair, so ticking either half moves both.
        var matrix = new List<int>(project.Manifest.CollisionMatrix);
        while (matrix.Count < 16) matrix.Add(-1);
        var boxesByPair = new Dictionary<(int, int), CheckBox>();
        var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        for (int c = 0; c <= 16; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = c == 0 ? new GridLength(120) : new GridLength(20) });
        for (int r = 0; r <= 0; r++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) });
        for (int r = 0; r < 16; r++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
        for (int c = 0; c < 16; c++)
        {
            var head = new TextBlock { Text = c.ToString(), FontSize = 10, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center,
                LayoutTransform = new RotateTransform(-90) };
            Grid.SetColumn(head, c + 1); Grid.SetRow(head, 0); grid.Children.Add(head);
        }
        for (int r = 0; r < 16; r++)
        {
            int row = r;
            var label = new TextBlock { Text = LayerName(r), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(label, 0); Grid.SetRow(label, r + 1); grid.Children.Add(label);
            for (int c = 0; c <= r; c++)
            {
                int col = c;
                var box = new CheckBox { IsChecked = (matrix[row] & (1 << col)) != 0, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = LayerName(row) + " and " + LayerName(col) };
                void Set(bool on)
                {
                    if (on) { matrix[row] |= 1 << col; matrix[col] |= 1 << row; }
                    else { matrix[row] &= ~(1 << col); matrix[col] &= ~(1 << row); }
                    // The mirrored half of the pair follows along.
                    if (boxesByPair.TryGetValue((col, row), out var twin) && twin.IsChecked != on) twin.IsChecked = on;
                }
                box.Checked += (_, _) => Set(true); box.Unchecked += (_, _) => Set(false);
                boxesByPair[(row, col)] = box;
                Grid.SetColumn(box, c + 1); Grid.SetRow(box, r + 1); grid.Children.Add(box);
            }
        }
        var both = new StackPanel();
        both.Children.Add(list);
        both.Children.Add(new TextBlock { Text = "Which layers collide", Margin = new Thickness(0, 14, 0, 0), FontWeight = FontWeights.SemiBold, Foreground = Brushes.LightSkyBlue });
        both.Children.Add(new TextBlock { Text = "Unticked means those two layers pass straight through each other, everywhere in the project.", TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11 });
        both.Children.Add(grid);
        root.Children.Add(new ScrollViewer { Content = both, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Insert(1, bottom);
        var save = new Button { Content = "Save", Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(0, 0, 6, 0) };
        save.Click += (_, _) => Guard(() =>
        {
            Change();
            var kept = boxes.Select(b => b.Text.Trim()).ToList();
            while (kept.Count > 0 && kept[^1].Length == 0) kept.RemoveAt(kept.Count - 1);   // don't store trailing blanks
            project.Manifest.CollisionLayers = kept;
            // All-on is the same as having no matrix at all, so it is not stored.
            project.Manifest.CollisionMatrix = matrix.All(v => v == -1) ? [] : matrix;
            RefreshInspector(); window.Close(); Log("Collision layer names saved.");
        });
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(12, 2, 12, 2) }; cancel.Click += (_, _) => window.Close();
        bottom.Children.Add(save); bottom.Children.Add(cancel);
        window.ShowDialog();
    }
}
