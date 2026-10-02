using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Wysicraft.Core;
using Wysicraft.Models;
namespace Wysicraft.Designer;

// Components on a control, Unity-style: an "+ Add component" list you can search, and one card per attached
// component with its settings and a ✕. Collider and Rigidbody are views over the physics fields that were always
// there (body, collider, trigger, bounce, friction, layer); the script ones are recorded on the control and show the
// numbers at the top of their script, editable in place.
public partial class MainWindow
{
    sealed record ComponentOffer(string Id, string Name, string Group, string What, Action Attach);

    /// <summary>What the Add list offers this control: everything it could use that is not already on it.</summary>
    List<ComponentOffer> ComponentOffers(Element e)
    {
        var offers = new List<ComponentOffer>();
        var allowed = Behaviours.For(e).Select(b => b.Id).ToHashSet();
        if (allowed.Count == 0) return offers;
        void Preset(string id, string name, string group, string what) => offers.Add(new(id, name, group, what, () => Attach(e, id, name)));
        bool hasCollider = e.Body.Length > 0 || e.Type == "collider", hasBody = e.Body is "dynamic" or "kinematic";
        if (!hasCollider && allowed.Contains("static_body"))
            offers.Add(new("collider", "Collider", "Physics", "A shape the physics knows about: a box, a circle or a polygon. On its own it is a wall or a floor; add a Rigidbody to make it move.", () => Attach(e, "static_body", "Collider")));
        if (!hasBody && allowed.Contains("rigidbody")) Preset("rigidbody", "Rigidbody", "Physics", Behaviours.All.First(b => b.Id == "rigidbody").What);
        foreach (var b in Behaviours.All.Where(b => Behaviours.ScriptOf(b.Id) != null && allowed.Contains(b.Id) && !e.Behaviours.Contains(b.Id)))
            Preset(b.Id, b.Name, b.Group, b.What);
        if (e.Body != "kinematic" && allowed.Contains("kinematic_body")) Preset("kinematic_body", "Kinematic body", "Presets", Behaviours.All.First(b => b.Id == "kinematic_body").What);
        if (!e.Trigger && allowed.Contains("trigger_zone")) Preset("trigger_zone", "Trigger zone", "Presets", Behaviours.All.First(b => b.Id == "trigger_zone").What);
        if (allowed.Contains("bouncy")) Preset("bouncy", "Bouncy", "Presets", Behaviours.All.First(b => b.Id == "bouncy").What);
        if (allowed.Contains("slippery")) Preset("slippery", "Slippery", "Presets", Behaviours.All.First(b => b.Id == "slippery").What);
        return offers;
    }

    void Attach(Element e, string behaviourId, string name)
    {
        Change();
        var did = Behaviours.Apply(project, ui, e, behaviourId);
        Draw(); RefreshAll();
        Log(name + " → " + string.Join("; ", did));
    }

    /// <summary>The Components part of Advanced: the Add button and a card per attached component.</summary>
    void BuildComponentCards(Element e)
    {
        var offers = ComponentOffers(e);
        var head = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        var add = new Button { Content = "+ Add component", HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Attach a collider, a rigidbody, a character controller and so on. Each one only sets the fields it shows and writes an ordinary script you can edit.", IsEnabled = offers.Count > 0 };
        add.Click += (_, _) => Guard(() => ShowAddComponent(add, e));
        head.Children.Add(add); Properties.Children.Add(head);

        if (e.Body.Length > 0 || e.Type == "collider") ColliderCard(e);
        if (e.Body is "dynamic" or "kinematic") RigidbodyCard(e);
        foreach (string id in e.Behaviours.ToList()) ScriptCard(e, id);
        if (e.Body.Length == 0 && e.Type != "collider" && e.Behaviours.Count == 0)
            Properties.Children.Add(new TextBlock { Text = "No components. Add a Collider to make it solid, a Rigidbody to make it move, or a Character controller to drive it from your inputs.", TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11, Margin = new Thickness(4, 0, 4, 4) });
    }

    /// <summary>The searchable list of components to attach, dropped under the Add button.</summary>
    void ShowAddComponent(Button under, Element e)
    {
        var offers = ComponentOffers(e);
        var popup = new Popup { PlacementTarget = under, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        var frame = new Border { Background = Brush("#25282E"), BorderBrush = Brush("#454B56"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6), Width = 320 };
        var layout = new StackPanel(); frame.Child = layout; popup.Child = frame;
        var search = new TextBox { ToolTip = "Type to filter", Margin = new Thickness(0, 0, 0, 4) };
        var list = new ListBox { MaxHeight = 280, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        layout.Children.Add(search); layout.Children.Add(list);
        void Fill()
        {
            list.Items.Clear();
            string q = search.Text.Trim();
            var shown = offers.Where(o => q.Length == 0 || o.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || o.What.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var group in shown.GroupBy(o => o.Group))
            {
                list.Items.Add(new ListBoxItem { Content = group.Key, IsEnabled = false, Opacity = 0.6, FontSize = 11, Padding = new Thickness(4, 4, 4, 1) });
                foreach (var offer in group)
                {
                    var text = new StackPanel();
                    text.Children.Add(new TextBlock { Text = offer.Name, FontWeight = FontWeights.SemiBold });
                    text.Children.Add(new TextBlock { Text = offer.What, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11 });
                    list.Items.Add(new ListBoxItem { Content = text, Tag = offer, Padding = new Thickness(12, 3, 4, 3) });
                }
            }
            if (shown.Count == 0) list.Items.Add(new ListBoxItem { Content = "Nothing matches.", IsEnabled = false, Opacity = 0.6 });
        }
        void Pick()
        {
            if (list.SelectedItem is ListBoxItem { Tag: ComponentOffer offer }) { popup.IsOpen = false; Guard(offer.Attach); }
        }
        search.TextChanged += (_, _) => Fill();
        search.PreviewKeyDown += (_, k) =>
        {
            if (k.Key == Key.Down) { var first = list.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.IsEnabled); if (first != null) { list.SelectedItem = first; first.Focus(); } k.Handled = true; }
            else if (k.Key == Key.Enter) { list.SelectedItem ??= list.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.IsEnabled); Pick(); k.Handled = true; }
            else if (k.Key == Key.Escape) { popup.IsOpen = false; k.Handled = true; }
        };
        list.PreviewMouseLeftButtonUp += (_, _) => Dispatcher.BeginInvoke(Pick);
        list.PreviewKeyDown += (_, k) => { if (k.Key == Key.Enter) { Pick(); k.Handled = true; } else if (k.Key == Key.Escape) { popup.IsOpen = false; k.Handled = true; } };
        Fill();
        popup.IsOpen = true;
        search.Focus();
    }

    void ColliderCard(Element e)
    {
        bool own = e.Type == "collider";
        var body = Card(Properties, "collider", "Collider",
            "The shape the physics uses for this control. A static body on its own: walls, floors, platforms. With a Rigidbody it moves.",
            own ? null : () => { Change(); e.Body = ""; e.Trigger = false; Draw(); RefreshInspector(); Log("Collider removed from " + e.Id + ": no physics body."); },
            "Remove: the physics forgets this control (and its Rigidbody, if it has one)");
        Choice(body, "Shape", e.Collider, Pairs("box", "circle", "polygon"), v => { e.Collider = v; if (v == "polygon" && e.ColliderPoints.Count < 3) e.ColliderPoints = DefaultPolygon(e); }, "Box and circle follow the control's size. Polygon has its own points.");
        if (e.Collider == "polygon") { var edit = new Button { Content = "Edit collider points…", HorizontalAlignment = HorizontalAlignment.Left }; edit.Click += (_, _) => Guard(() => ShowColliderEditor(e)); body.Children.Add(edit); }
        var row = new DockPanel();
        row.Children.Add(new TextBlock { Text = "Trigger", Width = FieldLabelWidth, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) });
        var trigger = new CheckBox { IsChecked = e.Trigger, VerticalAlignment = VerticalAlignment.Center, ToolTip = "A trigger passes through things and reports it instead: Trigger enter, stay (every 250 ms) and exit events. Whatever enters needs a physics body; use Kinematic for controls your scripts move." };
        trigger.Click += (_, _) => Guard(() => { Change(); e.Trigger = trigger.IsChecked == true; Draw(); RefreshInspector(); });
        row.Children.Add(trigger); body.Children.Add(row);
        LayerFields(body, e);
        if (own) body.Children.Add(new TextBlock { Text = "A collider control is its collider, so this card cannot be removed; delete the control instead.", TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11, Margin = new Thickness(4, 2, 4, 0) });
    }

    void RigidbodyCard(Element e)
    {
        var body = Card(Properties, "rigidbody", "Rigidbody",
            Behaviours.All.First(b => b.Id == "rigidbody").What,
            () => { Change(); e.Body = "static"; Draw(); RefreshInspector(); Log("Rigidbody removed from " + e.Id + ": it is a static collider again."); },
            "Remove: it stops moving and becomes a plain static collider");
        Choice(body, "Body", e.Body, Pairs("dynamic", "kinematic"), v => e.Body = v,
            "Dynamic: moves, falls with gravity and bounces. Kinematic: moved by scripts or animations, ignores gravity, and pushes dynamic bodies.");
        if (!e.Trigger) { Field(body, "Bounce", e, "Bounce"); Field(body, "Friction", e, "Friction"); }
        else body.Children.Add(new TextBlock { Text = "A trigger never pushes or gets pushed, so bounce and friction do not apply.", TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11, Margin = new Thickness(4, 0, 4, 2) });
        var presets = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 2) };
        foreach (var (id, name) in new[] { ("bouncy", "Bouncy"), ("slippery", "Slippery") })
        {
            var b = new Button { Content = name, ToolTip = Behaviours.All.First(x => x.Id == id).What + "\n\nSets: " + Behaviours.All.First(x => x.Id == id).Adds, Padding = new Thickness(8, 2, 8, 2) };
            b.Click += (_, _) => Guard(() => Attach(e, id, name)); presets.Children.Add(b);
        }
        body.Children.Add(presets);
        // Gravity is the screen's, not the body's: one pull for everything on the screen, as in Unity's physics settings.
        var gravity = new DockPanel { Margin = new Thickness(4, 2, 4, 0) };
        var open = new Button { Content = "Screen settings…", Padding = new Thickness(8, 2, 8, 2), ToolTip = "Gravity is set once per screen and shared by every dynamic body on it. This opens the screen's Display settings." };
        open.Click += (_, _) => Guard(() => { selected.Clear(); Draw(); RefreshInspector(); OpenSection("Display"); });
        DockPanel.SetDock(open, Dock.Right); gravity.Children.Add(open);
        gravity.Children.Add(new TextBlock { Text = e.Body == "kinematic" ? "Gravity: none — a kinematic body is moved by scripts and animations only." : $"Gravity: {ui.Gravity:0.##} (the screen's; every dynamic body on it falls the same way){(ui.Gravity == 0 ? " — 0 means nothing falls" : "")}", TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        body.Children.Add(gravity);
    }

    void ScriptCard(Element e, string id)
    {
        var known = Behaviours.All.FirstOrDefault(b => b.Id == id);
        var wiring = Behaviours.ScriptOf(id);
        if (known == null || wiring == null)
        {
            var unknown = Card(Properties, id, "Unknown component: " + id, "This project lists a component the editor does not know. Removing it only takes the name off the control.",
                () => { Change(); e.Behaviours.Remove(id); RefreshInspector(); Log("Removed unknown component " + id + " from " + e.Id); });
            return;
        }
        var body = Card(Properties, id, known.Name, known.What,
            () => { Change(); var did = Behaviours.Remove(project, ui, e, id); Draw(); RefreshAll(); Log(known.Name + " removed from " + e.Id + ": " + string.Join("; ", did)); },
            "Remove: the event stops running its script, and the script is deleted if it was never edited");
        string path = Behaviours.ScriptPath(e, id);
        if (!project.Scripts.TryGetValue(path, out var source))
        {
            body.Children.Add(new TextBlock { Text = "Its script " + path + " is missing — was the control renamed?", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Goldenrod, FontSize = 11, Margin = new Thickness(4, 0, 4, 2) });
            var again = new Button { Content = "Write it again", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 2, 8, 2) };
            again.Click += (_, _) => Guard(() => Attach(e, id, known.Name)); body.Children.Add(again);
            return;
        }
        var tunables = Behaviours.Tunables(source);
        if (tunables.Count == 0) body.Children.Add(new TextBlock { Text = "Nothing to tune: the script has no var lines above its first function.", TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11, Margin = new Thickness(4, 0, 4, 2) });
        foreach (var t in tunables)
        {
            // A Pickup's sound and particles get pickers of their own, below.
            if (id == "pickup" && t.Name is "SOUND" or "PARTICLES") continue;
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1), ToolTip = t.Comment.Length > 0 ? t.Comment.TrimStart('/', ' ') : "var " + t.Name + " at the top of " + path };
            row.Children.Add(new TextBlock { Text = t.Name, Width = FieldLabelWidth, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4), FontFamily = new FontFamily("Consolas") });
            var box = new TextBox { Text = Behaviours.TunableText(t), Tag = "tunable:" + t.Name };
            box.LostKeyboardFocus += (_, _) => Guard(() => CommitTunable(path, t.Name, box.Text));
            box.KeyDown += (_, k) => { if (k.Key == Key.Enter) { Guard(() => CommitTunable(path, t.Name, box.Text)); k.Handled = true; } };
            row.Children.Add(box); body.Children.Add(row);
        }
        if (id == "pickup") PickupChoices(body, e, path, tunables);
        var foot = new DockPanel { Margin = new Thickness(4, 2, 4, 0) };
        var openScript = new Button { Content = "Open script", Padding = new Thickness(8, 2, 8, 2), ToolTip = path };
        openScript.Click += (_, _) => Guard(() => { RefreshScripts(path); ShowDock("scripts"); });
        DockPanel.SetDock(openScript, Dock.Right); foot.Children.Add(openScript);
        foot.Children.Add(new TextBlock { Text = $"Runs {wiring.Function}() from the {(wiring.OnScreen ? "screen's" : "control's")} {wiring.Event} event.", Opacity = 0.65, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(foot);
    }

    /// <summary>The Pickup card's optional sound and particle burst: a project sound (or Import…) and a project effect
    /// or a built-in template (or Import…). Both are plain lines at the top of its script (SOUND, PARTICLES), so a
    /// script can still set them, and an untouched Pickup from before they existed is brought up to date when one is
    /// chosen.</summary>
    void PickupChoices(Panel body, Element e, string path, List<Behaviours.Tunable> tunables)
    {
        string Current(string name) => tunables.FirstOrDefault(t => t.Name == name) is { } t ? Behaviours.TunableText(t) : "";
        SoundPicker(body, "Sound", Current("SOUND"), v => Guard(() => SetPickupChoice(e, path, "SOUND", v)),
            "Played when it's picked up. Optional: choose a project sound, Import… one, or leave it empty for none.", buttonsBelow: true);
        ParticlePicker(body, "Particles", Current("PARTICLES"), v => Guard(() => SetPickupChoice(e, path, "PARTICLES", v)),
            "A burst of particles where it was, when it's picked up. Optional: choose an effect in the project or a template, Import… one, or (none).");
    }
    void SetPickupChoice(Element e, string path, string name, string value)
    {
        if (!project.Scripts.TryGetValue(path, out var source)) return;
        var current = Behaviours.UpgradePickup(source, e)
            ?? throw new InvalidOperationException($"{path} was changed by hand, so the card can't add this for you. In the script, add  var {name} = '{value}';  at the top, and in taken():  " + (name == "SOUND" ? "if (SOUND) ctx.client.playSound(SOUND);" : $"if (PARTICLES) ctx.ui.burst(PARTICLES, '{e.Id}');"));
        project.Scripts[path] = Behaviours.WithTunable(current, name, value);
        RefreshScripts();
        Log(value.Length > 0 ? $"{e.Id}: {(name == "SOUND" ? "plays " + value : "bursts " + value)} when it's picked up." : $"{e.Id}: no {(name == "SOUND" ? "sound" : "particles")} when it's picked up.");
        Dispatcher.BeginInvoke(RefreshInspector);
    }

    /// <summary>Writes one tunable back into its script, in place, and refreshes the Scripts panel if it is open there.</summary>
    void CommitTunable(string path, string name, string text)
    {
        if (!project.Scripts.TryGetValue(path, out var before)) return;
        var current = Behaviours.Tunables(before).FirstOrDefault(t => t.Name == name);
        if (current == null || Behaviours.TunableText(current) == text.Trim()) return;
        Change();
        project.Scripts[path] = Behaviours.WithTunable(project.Scripts[path], name, text);
        RefreshScripts();
        Log($"{name} = {text.Trim()} in {path}");
    }

    /// <summary>Collision layer and mask, on the Collider card.</summary>
    void LayerFields(StackPanel panel, Element e)
    {
        Choice(panel, "Layer", e.Layer.ToString(), Enumerable.Range(0, 16).Select(i => (i.ToString(), LayerName(i))).ToList(), v => e.Layer = int.Parse(v),
            "Which collision layer this body sits on. Rename the layers in Advanced → Collision layers.");
        // The mask, as one tick box per layer. Both bodies have to be watching each other for a contact to count.
        var all = new CheckBox { Content = "Collides with everything", IsChecked = e.CollidesWith == -1, Margin = new Thickness(4, 0, 0, 2) };
        var grid = new WrapPanel { Margin = new Thickness(12, 0, 0, 4), IsEnabled = e.CollidesWith != -1 };
        for (int i = 0; i < 16; i++)
        {
            int bit = 1 << i;
            var box = new CheckBox { Content = LayerName(i), Width = 150, IsChecked = (e.CollidesWith & bit) != 0, Margin = new Thickness(0, 1, 0, 1) };
            box.Checked += (_, _) => Guard(() => { Change(); e.CollidesWith |= bit; });
            box.Unchecked += (_, _) => Guard(() => { Change(); e.CollidesWith &= ~bit; });
            grid.Children.Add(box);
        }
        all.Checked += (_, _) => Guard(() => { Change(); e.CollidesWith = -1; RefreshInspector(); });
        all.Unchecked += (_, _) => Guard(() => { Change(); e.CollidesWith = 1 << e.Layer; RefreshInspector(); });
        panel.Children.Add(all); if (e.CollidesWith != -1) panel.Children.Add(grid);
    }

    /// <summary>Part of --smoke-advanced: sections fold and remember, cards appear for what is on a control,
    /// tunables write back into the script in place, and removing a component cleans up after itself.</summary>
    void VerifyComponentCards()
    {
        var actor = new Element { Id = "actor", Type = "panel", Bounds = new Bounds { X = 10, Y = 10, Width = 32, Height = 32 } };
        ui.Elements.Add(actor); selected.Clear(); selected.Add(actor.Id); RefreshInspector();
        // ---- Sections ----
        var sections = Properties.Children.OfType<Expander>().ToList();
        if (sections.Count < 5 || sections.All(s => (s.Header as TextBlock)?.Text != "LAYOUT")) throw new Exception("Properties not folded into sections: " + string.Join(",", sections.Select(s => (s.Header as TextBlock)?.Text)));
        if (Properties.Children.IndexOf(sections[0]) == 0) throw new Exception("Ask Agent should sit above the first section");
        var layout = sections.First(s => (s.Header as TextBlock)?.Text == "LAYOUT");
        layout.IsExpanded = false;
        if (!Prefs().CollapsedSections.Contains("Layout")) throw new Exception("Closing a section was not remembered");
        RefreshInspector();
        if (Properties.Children.OfType<Expander>().First(s => (s.Header as TextBlock)?.Text == "LAYOUT").IsExpanded) throw new Exception("A closed section reopened on refresh");
        Properties.Children.OfType<Expander>().First(s => (s.Header as TextBlock)?.Text == "LAYOUT").IsExpanded = true;
        if (Prefs().CollapsedSections.Contains("Layout")) throw new Exception("Reopening a section was not remembered");
        if (!InspectorItems().OfType<DockPanel>().Any(d => d.Children.OfType<TextBlock>().Any(t => t.Text == "Width"))) throw new Exception("InspectorItems does not see into sections");
        // ---- Nothing attached yet ----
        IEnumerable<Border> Cards() => InspectorItems().OfType<Border>().Where(b => b.Tag is string t && t.StartsWith("card:"));
        if (Cards().Any()) throw new Exception("A plain panel should have no component cards");
        var offers = ComponentOffers(actor);
        if (!offers.Any(o => o.Id == "collider") || !offers.Any(o => o.Id == "rigidbody") || !offers.Any(o => o.Id == "character_controller")) throw new Exception("Add list is missing components: " + string.Join(",", offers.Select(o => o.Id)));
        if (ComponentOffers(new Element { Id = "snd", Type = "sound" }).Count != 0) throw new Exception("A sound should be offered nothing");
        // ---- Character controller: three cards, tunables editable in place ----
        Attach(actor, "character_controller", "Character controller"); selected.Clear(); selected.Add(actor.Id); RefreshInspector();
        var tags = Cards().Select(c => (string)c.Tag).ToList();
        if (!tags.Contains("card:collider") || !tags.Contains("card:rigidbody") || !tags.Contains("card:character_controller")) throw new Exception("Cards missing after adding a character controller: " + string.Join(",", tags));
        if (ComponentOffers(actor).Any(o => o.Id is "character_controller" or "rigidbody" or "collider")) throw new Exception("Attached components are still offered");
        // The capture shows only the Advanced section, so the cards are in view; folding the rest also exercises remembering.
        if (engineCaptureTo != null) try
        {
            var keep = Prefs().CollapsedSections.ToList();
            Prefs().CollapsedSections = Properties.Children.OfType<Expander>().Select(x => ((SectionHeading)((TextBlock)x.Header).Tag!).Text).Where(t => !t.StartsWith("Advanced")).ToList();
            RefreshInspector(); UpdateLayout(); CaptureWindow(this, System.IO.Path.Combine(engineCaptureTo, "ComponentCards.png"));
            Prefs().CollapsedSections = keep; RefreshInspector();
        }
        catch (Exception) { }
        var speed = InspectorItems().SelectMany(Descend).OfType<TextBox>().FirstOrDefault(t => t.Tag as string == "tunable:SPEED") ?? throw new Exception("SPEED tunable not shown on the card");
        if (speed.Text != "140") throw new Exception("SPEED shows " + speed.Text);
        string path = Behaviours.ScriptPath(actor, "character_controller");
        CommitTunable(path, "SPEED", "200");
        if (!project.Scripts[path].Contains("var SPEED = 200;        // sideways speed") && !project.Scripts[path].Contains("var SPEED = 200;  // sideways speed")) throw new Exception("SPEED was not written back in place: " + project.Scripts[path].Split('\n')[3]);
        if (!project.Scripts[path].Contains("var JUMP = 330;")) throw new Exception("Editing one tunable disturbed another");
        try { CommitTunable(path, "JUMP", "high"); throw new Exception("A word was accepted for a number"); } catch (InvalidDataException) { }
        string follower = Behaviours.WithTunable(Behaviours.ScriptSource(actor, "follower"), "TARGET_TAG", "enemy");
        if (!follower.Contains("var TARGET_TAG = 'enemy';")) throw new Exception("A string tunable lost its quotes: " + follower.Split('\n')[1]);
        // ---- Remove: an edited script is kept, the event is unwired ----
        var did = Behaviours.Remove(project, ui, actor, "character_controller");
        if (actor.Behaviours.Count != 0 || !project.Scripts.ContainsKey(path) || ui.Events.ContainsKey("tick")) throw new Exception("Remove left things behind: " + string.Join("; ", did));
        // ---- Remove: an untouched script goes away ----
        Behaviours.Apply(project, ui, actor, "topdown_mover");
        string mover = Behaviours.ScriptPath(actor, "topdown_mover");
        Behaviours.Remove(project, ui, actor, "topdown_mover");
        if (project.Scripts.ContainsKey(mover) || ui.Events.ContainsKey("tick")) throw new Exception("An untouched script was kept on remove");
        // ---- Rigidbody off, collider stays; collider off, nothing left ----
        actor.Body = "static"; RefreshInspector();
        tags = Cards().Select(c => (string)c.Tag).ToList();
        if (tags.Contains("card:rigidbody") || !tags.Contains("card:collider")) throw new Exception("A static body should show only the Collider card: " + string.Join(",", tags));
        actor.Body = ""; actor.Trigger = false; RefreshInspector();
        if (Cards().Any()) throw new Exception("Cards remain with no body");
        // A collider control has its card with no ✕; a sprite gets the same cards as a panel.
        var wall = ui.Elements.First(e => e.Type == "collider"); selected.Clear(); selected.Add(wall.Id); RefreshInspector();
        var wallCard = Cards().FirstOrDefault(c => (string)c.Tag == "card:collider") ?? throw new Exception("A collider control has no Collider card");
        if (Descend(wallCard).OfType<Button>().Any(b => b.Content as string == "✕")) throw new Exception("A collider control offers to remove its own collider");
        var sprite = ui.Elements.First(e => e.Type == "sprite"); Behaviours.Apply(project, ui, sprite, "rigidbody"); selected.Clear(); selected.Add(sprite.Id); RefreshInspector();
        if (!Cards().Any(c => (string)c.Tag == "card:rigidbody") || !InspectorItems().SelectMany(Descend).OfType<Button>().Any(b => b.Content as string == "Screen settings…")) throw new Exception("A sprite's Rigidbody card is missing or has no gravity link");
        // Unknown component ids show as a card and fail validation.
        sprite.Behaviours.Add("jetpack"); RefreshInspector();
        if (!Cards().Any(c => (string)c.Tag == "card:jetpack") || !Wysicraft.Core.Validation.Errors(project).Any(m => m.Message.Contains("Unknown component"))) throw new Exception("Unknown component not surfaced");
        sprite.Behaviours.Remove("jetpack");
        // ---- Pickup: an optional sound and particle burst, chosen on its card (pickers, not text boxes) ----
        var gem = new Element { Id = "gem", Type = "panel", Bounds = new Bounds { X = 60, Y = 10, Width = 16, Height = 16 } };
        ui.Elements.Add(gem); Behaviours.Apply(project, ui, gem, "pickup"); selected.Clear(); selected.Add(gem.Id); RefreshInspector();
        string gemPath = Behaviours.ScriptPath(gem, "pickup");
        var pickupCard = Cards().FirstOrDefault(c => (string)c.Tag == "card:pickup") ?? throw new Exception("No Pickup card");
        if (Descend(pickupCard).OfType<TextBox>().Any(t => t.Tag as string is "tunable:SOUND" or "tunable:PARTICLES")) throw new Exception("The Pickup's sound and particles show as text boxes, not pickers");
        if (Descend(pickupCard).OfType<Button>().Count(b => b.Content as string == "Import…") != 2) throw new Exception("The Pickup card should have Import… for its sound and its particles");
        var particlesBox = Descend(pickupCard).OfType<ComboBox>().FirstOrDefault(c => c.ItemsSource is List<string> l && l.Contains("(none)")) ?? throw new Exception("No particle picker on the Pickup card");
        if (!((List<string>)particlesBox.ItemsSource).Contains("sparkle  (template)")) throw new Exception("The particle picker doesn't offer the templates: " + string.Join(",", (List<string>)particlesBox.ItemsSource));
        particlesBox.SelectedItem = "sparkle  (template)";
        if (!project.Manifest.Particles.Any(p => p.Id == "sparkle") || !project.Scripts[gemPath].Contains("var PARTICLES = 'sparkle';")) throw new Exception("Choosing a template didn't add it and set the Pickup's particles: " + project.Scripts[gemPath]);
        SetPickupChoice(gem, gemPath, "SOUND", project.Manifest.Id + ":gem_get");
        if (!project.Scripts[gemPath].Contains("var SOUND = '" + project.Manifest.Id + ":gem_get';")) throw new Exception("The Pickup's sound wasn't set");
        // A Pickup from before sounds and particles is brought up to date when one is chosen, its WORTH kept.
        project.Scripts[gemPath] = string.Join('\n', Behaviours.ScriptSource(gem, "pickup").Split('\n').Where(l => !l.Contains("SOUND") && !l.Contains("PARTICLES"))).Replace("var WORTH = 1;", "var WORTH = 3;");
        SetPickupChoice(gem, gemPath, "PARTICLES", "sparkle");
        if (!project.Scripts[gemPath].Contains("var WORTH = 3;") || !project.Scripts[gemPath].Contains("ctx.ui.burst(PARTICLES, 'gem')")) throw new Exception("An older Pickup wasn't brought up to date: " + project.Scripts[gemPath]);
        // One edited by hand is left alone, and says how to do it in code.
        project.Scripts[gemPath] = project.Scripts[gemPath].Replace("var SOUND", "var NOISE").Replace("if (SOUND) ctx.client.playSound(SOUND);", "");
        project.Scripts[gemPath] = project.Scripts[gemPath].Replace("var PARTICLES", "var PUFF");
        try { SetPickupChoice(gem, gemPath, "SOUND", "x:y"); throw new Exception("An edited Pickup was rewritten"); } catch (InvalidOperationException ex) when (ex.Message.Contains("playSound")) { }
        project.Scripts[gemPath] = Behaviours.ScriptSource(gem, "pickup");
        Behaviours.Remove(project, ui, gem, "pickup");
        if (project.Scripts.ContainsKey(gemPath)) throw new Exception("An untouched Pickup script was kept on remove");
        ui.Elements.Remove(gem); project.Manifest.Particles.RemoveAll(p => p.Id == "sparkle");
        ui.Elements.Remove(actor); selected.Clear(); RefreshInspector();
    }
    static IEnumerable<UIElement> Descend(UIElement root)
    {
        yield return root;
        IEnumerable<UIElement> children = root switch
        {
            Panel p => p.Children.Cast<UIElement>(),
            Border { Child: UIElement c } => [c],
            Expander { Content: UIElement c } => [c],
            ContentControl { Content: UIElement c } => [c],
            _ => []
        };
        foreach (var child in children) foreach (var item in Descend(child)) yield return item;
    }
}
