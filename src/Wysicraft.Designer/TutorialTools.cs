using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AvalonDock.Layout;
using ModelContextProtocol.Server;
using Wysicraft.Core;
using Wysicraft.Models;
namespace Wysicraft.Designer;

// Tutorial tools an AI assistant uses to teach the editor: a highlight that points at a part of the editor by name,
// notices in the corner, and a step-by-step tutorial window (Back / Show me / Next, steps that tick themselves off,
// references, Save as HTML). Tutorials are about the editor, not a game, so they live in the app's Tutorials folder
// and never in a project.

/// <summary>A condition that ticks a step off by itself, checked against the open project.</summary>
public sealed class TutorialCheck
{
    /// <summary>element (a control exists), event (a control's or screen's event does something), screen, script,
    /// property (a control's property equals a value), input, variable (a screen or game variable), leaderboard, asset.</summary>
    public string Kind { get; set; } = "";
    public string Screen { get; set; } = "";
    public string Element { get; set; } = "";
    public string Event { get; set; } = "";
    public string Name { get; set; } = "";
    [JsonPropertyName("equals")] public string? EqualsValue { get; set; }
}
public sealed class TutorialStep
{
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    /// <summary>What Show me points at (the same names as tutorial highlight); empty for none.</summary>
    public string Target { get; set; } = "";
    public string Caption { get; set; } = "";
    /// <summary>An absolute path to a picture, texture:&lt;resource&gt; (a project picture, e.g. from pixel_art), or
    /// capture:&lt;target&gt; (the editor around that target, taken when the tutorial opens).</summary>
    public string Image { get; set; } = "";
    /// <summary>The picture itself, kept so a saved tutorial opens with it.</summary>
    public string ImageData { get; set; } = "";
    public TutorialCheck? Check { get; set; }
    /// <summary>Spoken aloud when the step is shown (when Read aloud is on); empty for a silent step.</summary>
    public string Narration { get; set; } = "";
}
public sealed class TutorialReference { public string Title { get; set; } = ""; public string Text { get; set; } = ""; public string Link { get; set; } = ""; }
public sealed class Tutorial
{
    public string Title { get; set; } = "";
    public List<TutorialStep> Steps { get; set; } = [];
    public List<TutorialReference> References { get; set; } = [];
    /// <summary>The voice the narration is read in (empty: the assistant's voice).</summary>
    public string Voice { get; set; } = "";
}

public partial class MainWindow
{
    // ---- Finding a part of the editor by name ----
    internal sealed record TutorialTarget(FrameworkElement Element, string Where, Action? Reveal);
    static string Plain(object? header) => (header as string ?? (header as TextBlock)?.Text ?? "").Replace("_", "").Trim();
    string PanelTitle(string id) => Workspace.Layout.Descendents().OfType<LayoutContent>().FirstOrDefault(p => p.ContentId == id)?.Title ?? id;
    /// <summary>Where a target is, in words, without finding it on screen (for a saved tutorial).</summary>
    internal string TutorialWhere(string target)
    {
        var (kind, name) = SplitTarget(target);
        return kind switch
        {
            "menu" when MenuItemFor(name) is MenuItem item => MenuPath(item),
            "menu" or "toolbar" when commands.Any(c => c.Id == name) => Cmd(name).Name,
            "panel" => "the " + PanelTitle(name) + " panel",
            "toolbox" => "the " + (Registry.Controls.TryGetValue(name, out var spec) ? spec.DisplayName : name) + " entry in the Toolbox",
            "element" => "the control " + name + " on the canvas",
            "property" => "the " + name + " field in Properties",
            _ => "“" + name + "”"
        };
    }
    static (string Kind, string Name) SplitTarget(string target)
    {
        target = target.Trim(); int colon = target.IndexOf(':');
        string kind = colon > 0 ? target[..colon].Trim().ToLowerInvariant() : "text";
        if (target.Equals("screens", StringComparison.OrdinalIgnoreCase)) return ("screens", "");
        return kind is "menu" or "toolbar" or "panel" or "toolbox" or "element" or "property" or "text" or "pixel" or "card" ? (kind, colon > 0 ? target[(colon + 1)..].Trim() : target) : ("text", target);
    }
    // The menu bar's item for a command (a command can also sit in a context menu).
    MenuItem? MenuItemFor(string id) => commandMenuItems.TryGetValue(id, out var items) ? items.FirstOrDefault(i => Menus.Items.Contains(TopMenu(i))) ?? items.FirstOrDefault() : null;
    static MenuItem TopMenu(MenuItem item) { var at = item; while (LogicalTreeHelper.GetParent(at) is MenuItem parent) at = parent; return at; }
    static string MenuPath(MenuItem item)
    {
        var parts = new List<string>(); for (DependencyObject? at = item; at is MenuItem m; at = LogicalTreeHelper.GetParent(at)) parts.Insert(0, Plain(m.Header));
        return string.Join(" → ", parts);
    }
    /// <summary>A part of the editor by name: menu:&lt;command&gt;, toolbar:&lt;command&gt;, panel:&lt;id&gt;, toolbox:&lt;type&gt;,
    /// element:&lt;id&gt;, property:&lt;label&gt;, or text:&lt;what it says&gt; (also any name without a prefix).</summary>
    internal TutorialTarget? FindTutorialTarget(string target)
    {
        var (kind, name) = SplitTarget(target);
        switch (kind)
        {
            case "menu":
                if (MenuItemFor(name) is not MenuItem item) return null;
                return new(item, MenuPath(item), () =>
                {
                    var chain = new List<MenuItem>(); for (DependencyObject? at = LogicalTreeHelper.GetParent(item); at is MenuItem m; at = LogicalTreeHelper.GetParent(at)) chain.Insert(0, m);
                    // Menus opened to show the item are closed again when the highlight goes (ClearHighlight).
                    foreach (var m in chain) { if (!m.IsSubmenuOpen) openedMenus.Add(m); m.IsSubmenuOpen = true; m.UpdateLayout(); }
                });
            case "toolbar":
                var button = toolbarButtons.FirstOrDefault(b => b.Command == name).Button;
                return button == null ? null : new(button, "the " + Cmd(name).Name.Replace("…", "") + " button on the toolbar", null);
            case "panel":
                if (!dockContents.TryGetValue(name, out var content) || content is not FrameworkElement panel) return null;
                return new(panel, "the " + PanelTitle(name) + " panel", () => ShowDock(name));
            case "toolbox":
                var entry = Toolbox.Items.OfType<ListBoxItem>().FirstOrDefault(i => string.Equals(i.Tag as string, name, StringComparison.OrdinalIgnoreCase));
                return entry == null ? null : new(entry, TutorialWhere(target), () => { ShowDock("toolbox"); Toolbox.ScrollIntoView(entry); });
            case "element":
                FrameworkElement? Box() => Surface.Children.OfType<Border>().FirstOrDefault(b => b.Tag as string == name);
                var box = Box();
                if (box == null)
                {
                    // On another screen: Show me goes to that screen first.
                    var other = project.Screens.FirstOrDefault(s => s.Elements.Any(e => e.Id == name));
                    if (other == null) return null;
                    ui = other; RefreshAll(); box = Box(); if (box == null) return null;
                }
                return new(box, "the control " + name + " on the canvas", () => box.BringIntoView());
            case "screens":
                return new(Screens, "the screens list at the top of the canvas (pick a screen there to switch to it)", null);
            case "pixel":
                // A part of the open pixel editor: its tools, layers, frames strip, the frames' Duplicate button or the Preview box.
                var pixelEditor = Application.Current.Windows.OfType<PixelEditor>().LastOrDefault(w => w.IsVisible);
                if (pixelEditor == null || !pixelEditor.Parts.TryGetValue(name, out var part)) return null;
                return new(part, "the pixel editor's " + (name.ToLowerInvariant() switch { "tools" => "drawing tools", "layers" => "layers", "frames" => "frames", "duplicate" => "Duplicate frame button", "preview" => "Preview", _ => name }), () => pixelEditor.Activate());
            case "card":
                // A component card of the selected control (Collider, Rigidbody, Top-down mover, Pickup…), by its component id.
                ShowDock("properties"); UpdateLayout();
                var card = InspectorItems().SelectMany(Descend).OfType<Border>().FirstOrDefault(b => string.Equals(b.Tag as string, "card:" + name, StringComparison.OrdinalIgnoreCase));
                if (card == null) return null;
                return new(card, "the " + (Behaviours.All.FirstOrDefault(b => b.Id.Equals(name, StringComparison.OrdinalIgnoreCase))?.Name ?? name) + " card in Properties", () => ShowDock("properties"));
            case "property":
                if (!dockContents.TryGetValue("properties", out var props) || props is not DependencyObject propsRoot) return null;
                ShowDock("properties"); UpdateLayout(); // its rows only exist on screen once the panel is showing
                // Fields in a folded section aren't on screen yet, but they're found (the highlight opens the section).
                var label = Descendants(propsRoot).OfType<TextBlock>().FirstOrDefault(t => string.Equals(t.Text.Trim().TrimEnd(':'), name, StringComparison.OrdinalIgnoreCase))
                    ?? InspectorItems().SelectMany(Descend).OfType<TextBlock>().FirstOrDefault(t => string.Equals(t.Text.Trim().TrimEnd(':'), name, StringComparison.OrdinalIgnoreCase));
                if (label == null) return null;
                var row = (label.Parent as FrameworkElement) ?? label;
                return new(row, "the " + name + " field in Properties", () => ShowDock("properties"));
            default:
                foreach (var window in Application.Current.Windows.OfType<Window>().Where(w => w.IsVisible))
                {
                    var all = Descendants(window).OfType<FrameworkElement>().Where(f => f.IsVisible).ToList();
                    string TextOf(FrameworkElement f) => f switch { Button b => b.Content as string ?? Descendants(b).OfType<TextBlock>().Select(t => t.Text).FirstOrDefault(t => t.Trim().Length > 0) ?? "", HeaderedItemsControl h => Plain(h.Header), ContentControl c when c.Content is string s => s, TextBlock t => t.Text, _ => "" };
                    var match = all.FirstOrDefault(f => f is Button or MenuItem or CheckBox && TextOf(f).Replace("_", "").Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                        ?? all.FirstOrDefault(f => TextOf(f).Replace("_", "").Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                        ?? all.FirstOrDefault(f => f is Button or MenuItem or CheckBox && TextOf(f).Contains(name, StringComparison.OrdinalIgnoreCase));
                    if (match != null) return new(match, "“" + name + "”", null);
                }
                return null;
        }
    }
    /// <summary>What can be pointed at right now, for an assistant choosing a target.</summary>
    internal object TutorialTargets() => new
    {
        menu = commandMenuItems.Keys.OrderBy(k => k, StringComparer.Ordinal).Where(k => MenuItemFor(k) != null).Select(k => new { target = "menu:" + k, where = MenuPath(MenuItemFor(k)!) }),
        toolbar = toolbarButtons.Select(b => "toolbar:" + b.Command),
        panel = dockContents.Keys.Select(k => new { target = "panel:" + k, title = PanelTitle(k) }),
        toolbox = Toolbox.Items.OfType<ListBoxItem>().Select(i => i.Tag as string).Where(t => !string.IsNullOrEmpty(t)).Select(t => "toolbox:" + t),
        element = ui.Elements.Select(e => "element:" + e.Id),
        property = dockContents.TryGetValue("properties", out var p) && p is DependencyObject root ? Descendants(root).OfType<TextBlock>().Select(t => t.Text.Trim().TrimEnd(':')).Where(t => t.Length is > 1 and < 40 && !t.Contains('.')).Distinct().Select(t => "property:" + t) : [],
        text = "text:<words on a button, menu, tab or label anywhere in the editor>",
        screens = "screens (the screens list, where you switch screens)",
        card = InspectorItems().SelectMany(Descend).OfType<Border>().Select(b => b.Tag as string).Where(t => t != null && t.StartsWith("card:")).ToArray(),
        pixel = Application.Current.Windows.OfType<PixelEditor>().Any(w => w.IsVisible) ? PixelEditor.PartNames.Select(n => "pixel:" + n).ToArray() : [],
        screen = ui.Id
    };
    /// <summary>Where an element is on the screen, in device pixels: the same space for every window and monitor, so two
    /// of these can be compared whatever each monitor's scaling is.</summary>
    static Rect? ScreenRect(FrameworkElement element)
    {
        if (!element.IsVisible || element.ActualWidth < 1 || element.ActualHeight < 1 || PresentationSource.FromVisual(element) == null) return null;
        try { return new Rect(element.PointToScreen(new Point(0, 0)), element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight))); }
        catch (InvalidOperationException) { return null; }
    }

    // ---- Highlight ----
    // Drawn in the adorner layer of the window the target is in, so it's always on the right monitor, at the right size
    // for that monitor's scaling, and moves with the window. (A separate overlay window couldn't be placed reliably across
    // monitors with different scaling.)
    readonly List<MenuItem> openedMenus = new();
    HighlightAdorner? highlight; DispatcherTimer? highlightTimer; FrameworkElement? highlightTarget; string highlightSpec = "", highlightCaption = ""; DateTime highlightUntil; bool highlightEscapeHooked;
    /// <summary>Points at a part of the editor: a pulsing outline and a caption, which clicks go straight through. It
    /// clears when the person clicks what it points at, presses Escape, after seconds, or with the next highlight.</summary>
    internal string Highlight(string target, string caption, double seconds)
    {
        ClearHighlight();
        var found = FindTutorialTarget(target) ?? throw new InvalidOperationException("Nothing in the editor matches " + target + ". tutorial action targets lists what can be pointed at.");
        found.Reveal?.Invoke(); UpdateLayout();
        ScrollToTarget(found.Element);
        highlightSpec = target; highlightCaption = caption.Trim().Length > 0 ? caption.Trim() : found.Where;
        highlightUntil = DateTime.UtcNow.AddSeconds(Math.Clamp(seconds <= 0 ? 30 : seconds, 2, 600));
        if (!highlightEscapeHooked) { highlightEscapeHooked = true; PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && highlight != null) ClearHighlight(); }; }
        Attach(found.Element);
        highlightTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, (_, _) => FollowHighlight(), Dispatcher);
        highlightTimer.Start();
        KeepTutorialClear();
        return found.Where;
    }
    /// <summary>Brings what's pointed at into view: any folded section it's inside is opened, and the panel it's in is
    /// scrolled to it (a card at the bottom of Properties is no use to point at if it can't be seen).</summary>
    void ScrollToTarget(FrameworkElement element)
    {
        if (element is MenuItem) return;
        for (DependencyObject? at = element; at != null; at = (at is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(at) : null) ?? LogicalTreeHelper.GetParent(at))
            if (at is Expander { IsExpanded: false } section) section.IsExpanded = true;
        UpdateLayout();
        element.BringIntoView();
        UpdateLayout();
    }
    void Attach(FrameworkElement element)
    {
        Detach();
        // A menu item sits in a popup without an adorner layer: the menu it opened from is outlined instead, with the
        // item's path as the caption, while the menu stays open showing the item.
        var at = element;
        if (AdornerLayer.GetAdornerLayer(at) == null && at is MenuItem item) { var top = item; while (LogicalTreeHelper.GetParent(top) is MenuItem parent) top = parent; at = top; }
        var layer = AdornerLayer.GetAdornerLayer(at); if (layer == null) return;
        highlightTarget = element; highlight = new HighlightAdorner(at, highlightCaption); layer.Add(highlight);
        element.PreviewMouseDown += HighlightClicked;
    }
    void Detach()
    {
        if (highlightTarget != null) highlightTarget.PreviewMouseDown -= HighlightClicked;
        if (highlight != null) AdornerLayer.GetAdornerLayer(highlight.AdornedElement)?.Remove(highlight);
        highlight = null; highlightTarget = null;
    }
    void HighlightClicked(object sender, MouseButtonEventArgs e) => Dispatcher.BeginInvoke(ClearHighlight);
    void FollowHighlight()
    {
        if (DateTime.UtcNow > highlightUntil) { ClearHighlight(); return; }
        // A redrawn canvas control or a reopened menu is a new element: find it again by name.
        if (highlightTarget == null || !highlightTarget.IsVisible || PresentationSource.FromVisual(highlightTarget) == null)
            if (FindTutorialTarget(highlightSpec) is { } again && again.Element != highlightTarget) Attach(again.Element);
        highlight?.InvalidateVisual();
    }
    internal bool HighlightShowing => highlight != null && highlight.AdornedElement.IsVisible;
    /// <summary>What the highlight outlines, on the screen (device pixels).</summary>
    internal Rect? HighlightRect => highlight != null && highlight.AdornedElement is FrameworkElement at && ScreenRect(at) is Rect r ? Rect.Inflate(r, 4, 4) : null;
    internal void ClearHighlight()
    {
        highlightTimer?.Stop(); highlightTimer = null; Detach();
        for (int i = openedMenus.Count - 1; i >= 0; i--) openedMenus[i].IsSubmenuOpen = false;
        openedMenus.Clear();
    }
    /// <summary>The tutorial window never hides what it points at: if it covers the highlight, it moves to the other side
    /// of the editor (and down, if that isn't enough).</summary>
    void KeepTutorialClear()
    {
        if (tutorialWindow is not { IsVisible: true } w || highlight?.AdornedElement is not FrameworkElement at || Window.GetWindow(at) == w) return;
        w.UpdateLayout();
        if (w.Content is not FrameworkElement body || ScreenRect(body) is not Rect box || ScreenRect(at) is not Rect target || Content is not FrameworkElement editorRoot || ScreenRect(editorRoot) is not Rect editor) return;
        var outer = Rect.Inflate(box, 16, 40); // the window's frame and title bar round its content
        if (!outer.IntersectsWith(target)) return;
        double scale = VisualTreeHelper.GetDpi(w).DpiScaleX;
        bool targetOnRight = target.X + target.Width / 2 > editor.X + editor.Width / 2;
        double wantX = targetOnRight ? editor.Left + 24 : editor.Right - box.Width - 24;
        w.Left += (wantX - box.Left) / scale;
        w.UpdateLayout();
        if (ScreenRect(body) is Rect moved && Rect.Inflate(moved, 16, 40).IntersectsWith(target))
            w.Top += ((target.Bottom + 60 < editor.Bottom - moved.Height ? target.Bottom + 60 : editor.Top + 40) - moved.Top) / scale;
    }

    // ---- Notices ----
    // Drawn in the editor window's own adorner layer, bottom right, for the same reason as the highlight.
    NoticeAdorner? noticeLayer; DateTime lastNotice = DateTime.MinValue;
    /// <summary>A notice in the corner of the editor: it never takes the keyboard or blocks anything, goes by itself,
    /// and is written to Output too. At most one every two seconds and three at a time, so an assistant can't flood it.</summary>
    internal void Notify(string title, string text, string level, double seconds)
    {
        if (DateTime.UtcNow - lastNotice < TimeSpan.FromSeconds(2)) throw new InvalidOperationException("One notice every two seconds at most: wait a moment.");
        lastNotice = DateTime.UtcNow;
        title = title.Trim().Length > 0 ? title.Trim() : "Assistant"; text = text.Trim();
        if (text.Length == 0) throw new InvalidOperationException("A notice needs text.");
        if (text.Length > 600) text = text[..600] + "…";
        Log("Assistant notice: " + title + " — " + text);
        if (noticeLayer == null)
        {
            if (Content is not UIElement root || AdornerLayer.GetAdornerLayer(root) is not { } layer) throw new InvalidOperationException("The editor isn't ready for notices yet.");
            noticeLayer = new NoticeAdorner(root); layer.Add(noticeLayer);
        }
        var card = noticeLayer.Add(title, text, level);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(seconds <= 0 ? 12 : seconds, 3, 120)) };
        timer.Tick += (_, _) => { timer.Stop(); noticeLayer?.Remove(card); }; timer.Start();
    }
    internal int NoticeCount => noticeLayer?.Count ?? 0;
    internal void ClearNotices() => noticeLayer?.Clear();

    // ---- The tutorial window ----
    TutorialWindow? tutorialWindow;
    internal TutorialWindow? OpenTutorialWindow => tutorialWindow;
    internal TutorialWindow OpenTutorial(Tutorial tutorial)
    {
        if (tutorial.Steps.Count == 0) throw new InvalidOperationException("A tutorial needs at least one step.");
        if (tutorial.Steps.Count > 60) throw new InvalidOperationException("A tutorial has at most 60 steps.");
        foreach (var step in tutorial.Steps) if (step.ImageData.Length == 0 && step.Image.Trim().Length > 0) step.ImageData = Convert.ToBase64String(TutorialPicture(step.Image.Trim()));
        tutorialWindow?.Close();
        tutorialWindow = new TutorialWindow(this, tutorial) { Owner = this };
        tutorialWindow.Closed += (_, _) => { if (tutorialWindow is { IsVisible: false }) tutorialWindow = null; };
        tutorialWindow.Show();
        // Kept with the app, so it can be opened again later.
        string folder = AppFolders.Path("Tutorials"); Directory.CreateDirectory(folder);
        string slug = System.Text.RegularExpressions.Regex.Replace(tutorial.Title.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        File.WriteAllText(Path.Combine(folder, (slug.Length > 0 ? slug : "tutorial") + ".json"), Json.Write(tutorial));
        return tutorialWindow;
    }
    /// <summary>A step's picture as PNG/JPEG bytes: a file, a project picture, or the editor around a target.</summary>
    byte[] TutorialPicture(string spec)
    {
        if (spec.StartsWith("texture:", StringComparison.OrdinalIgnoreCase))
            return TextureAssets.TryGet(project, spec[8..].Trim(), out var bytes) ? bytes : throw new InvalidOperationException("No project picture " + spec[8..].Trim() + ".");
        if (spec.StartsWith("capture:", StringComparison.OrdinalIgnoreCase))
        {
            var found = FindTutorialTarget(spec[8..].Trim()) ?? throw new InvalidOperationException("Nothing in the editor matches " + spec[8..].Trim() + " to capture.");
            found.Reveal?.Invoke(); UpdateLayout();
            var window = Window.GetWindow(found.Element) ?? this; window.UpdateLayout();
            if (window.Content is not FrameworkElement root) throw new InvalidOperationException("That part of the editor can't be captured.");
            var dpi = VisualTreeHelper.GetDpi(root);
            var whole = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            whole.Render(root);
            var at = found.Element.TransformToAncestor(root).TransformBounds(new Rect(0, 0, found.Element.ActualWidth, found.Element.ActualHeight));
            at.Inflate(60, 40); at.Intersect(new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            var crop = new CroppedBitmap(whole, new Int32Rect((int)(at.X * dpi.DpiScaleX), (int)(at.Y * dpi.DpiScaleY), Math.Max(1, (int)(at.Width * dpi.DpiScaleX)), Math.Max(1, (int)(at.Height * dpi.DpiScaleY))));
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(crop)); using var stream = new MemoryStream(); png.Save(stream); return stream.ToArray();
        }
        if (!Path.IsPathFullyQualified(spec) || !File.Exists(spec)) throw new InvalidOperationException("A step's image is an absolute path to a PNG or JPEG, texture:<resource> or capture:<target>.");
        var file = File.ReadAllBytes(spec);
        if (file.Length > 8 * 1024 * 1024) throw new InvalidOperationException(Path.GetFileName(spec) + " is over 8 MB.");
        return file;
    }
    /// <summary>Whether a step's check holds for the open project, and the check in words.</summary>
    internal (bool Done, string What) TutorialCheckState(TutorialCheck c)
    {
        var screens = project.Screens.Where(s => c.Screen.Length == 0 || s.Id == c.Screen).ToList();
        string on = c.Screen.Length > 0 ? " on the " + c.Screen + " screen" : "";
        Element? Control() => screens.SelectMany(s => s.Elements).FirstOrDefault(e => e.Id == c.Element);
        static bool Does(UiEvent? ev) => ev != null && (ev.Client.Actions.Count > 0 || ev.Client.Script.Length > 0 || ev.Server.Actions.Count > 0 || ev.Server.Script.Length > 0);
        switch (c.Kind.Trim().ToLowerInvariant())
        {
            case "element": return (Control() != null, "a control called " + c.Element + on);
            case "event":
                if (c.Element.Length == 0) return (screens.Any(s => Does(s.Events.GetValueOrDefault(c.Event))), "the screen's " + c.Event + " event does something" + on);
                return (Does(Control()?.Events.GetValueOrDefault(c.Event)), c.Element + "'s " + c.Event + " event does something" + on);
            case "screen": return (project.Screens.Any(s => s.Id == (c.Screen.Length > 0 ? c.Screen : c.Name)), "a screen called " + (c.Screen.Length > 0 ? c.Screen : c.Name));
            case "script": return (project.Scripts.ContainsKey(c.Name), "the script " + c.Name);
            case "property":
                var element = Control(); if (element == null) return (false, c.Element + "'s " + c.Name + " is " + c.EqualsValue);
                var node = JsonNode.Parse(Json.Write(element))!.AsObject(); string key = node.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, c.Name, StringComparison.OrdinalIgnoreCase)) ?? c.Name;
                string value = node[key] is JsonValue v ? (v.TryGetValue<string>(out var s) ? s : v.ToJsonString()) : node[key]?.ToJsonString() ?? "";
                return (c.EqualsValue == null ? value.Length > 0 : string.Equals(value, c.EqualsValue, StringComparison.OrdinalIgnoreCase), c.Element + "'s " + c.Name + (c.EqualsValue == null ? " is set" : " is " + c.EqualsValue));
            case "input": return (project.Manifest.Inputs.Any(i => i.Name == c.Name), "an input called " + c.Name);
            case "variable": return (screens.Any(s => s.Variables.ContainsKey(c.Name)) || (c.Screen.Length == 0 && project.Manifest.GameVariables.ContainsKey(c.Name)), "a variable called " + c.Name + on);
            case "leaderboard": return (project.Leaderboards.Any(b => b.Id == c.Name), "a leaderboard page called " + c.Name);
            case "asset": return (TextureAssets.TryGet(project, c.Name, out _) || SoundAssets.Find(project, c.Name) != null || project.Assets.ContainsKey(c.Name), "the picture or sound " + c.Name);
            default: throw new InvalidOperationException("A check's kind is element, event, screen, script, property, input, variable, leaderboard or asset.");
        }
    }
    /// <summary>A tutorial as one HTML file, pictures and all, that opens anywhere.</summary>
    internal string TutorialHtml(Tutorial t)
    {
        static string H(string s) => System.Net.WebUtility.HtmlEncode(s);
        static string Paragraphs(string text) => string.Join("", text.Replace("\r", "").Split("\n\n").Select(p => "<p>" + H(p).Replace("\n", "<br>") + "</p>"));
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>").Append(H(t.Title)).Append("</title><style>");
        html.Append("body{margin:0;background:#15181D;color:#D9E6F1;font:15px/1.6 'Segoe UI',system-ui,sans-serif}main{max-width:760px;margin:0 auto;padding:24px 16px 48px}h1{font-size:26px;margin:0 0 4px}");
        html.Append(".sub{color:#8E9BA8;margin:0 0 24px}section{background:#1E232A;border:1px solid #2C333C;border-radius:8px;padding:16px 18px;margin:0 0 14px}h2{font-size:18px;margin:0 0 6px}.n{color:#5FB3F0;margin-right:8px}");
        html.Append(".where{color:#8E9BA8;font-size:13px;margin:0 0 8px}.done{color:#7FD08A;font-size:13px}img{max-width:100%;border-radius:6px;border:1px solid #2C333C;margin-top:8px}a{color:#5FB3F0}h3{margin:28px 0 8px}");
        html.Append("</style></head><body><main><h1>").Append(H(t.Title)).Append("</h1><p class=\"sub\">").Append(t.Steps.Count).Append(" steps · Arcadia Studio</p>");
        for (int i = 0; i < t.Steps.Count; i++)
        {
            var s = t.Steps[i];
            html.Append("<section><h2><span class=\"n\">").Append(i + 1).Append("</span>").Append(H(s.Title.Length > 0 ? s.Title : "Step " + (i + 1))).Append("</h2>");
            if (s.Target.Length > 0) html.Append("<p class=\"where\">Where: ").Append(H(TutorialWhere(s.Target))).Append("</p>");
            html.Append(Paragraphs(s.Text));
            if (s.Check != null) { var (_, what) = TutorialCheckState(s.Check); html.Append("<p class=\"done\">Done when: ").Append(H(what)).Append("</p>"); }
            if (s.ImageData.Length > 0) { var bytes = Convert.FromBase64String(s.ImageData); string type = bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 ? "jpeg" : "png"; html.Append("<img alt=\"\" src=\"data:image/").Append(type).Append(";base64,").Append(s.ImageData).Append("\">"); }
            html.Append("</section>");
        }
        if (t.References.Count > 0)
        {
            html.Append("<h3>References</h3>");
            foreach (var r in t.References)
            {
                html.Append("<section><h2>").Append(H(r.Title)).Append("</h2>").Append(Paragraphs(r.Text));
                if (Uri.TryCreate(r.Link, UriKind.Absolute, out var link) && link.Scheme is "http" or "https") html.Append("<p><a href=\"").Append(H(link.ToString())).Append("\">").Append(H(link.ToString())).Append("</a></p>");
                html.Append("</section>");
            }
        }
        return html.Append("</main></body></html>").ToString();
    }

    // ---- MCP ----
    internal Task<string> McpTutorial(string action, string target, string caption, string title, string text, string level, double seconds, string tutorial, int step, string references, string path, string voice, CancellationToken cancellationToken)
    {
        // Opening a window may put up a dialog that waits for the person, so it doesn't wait for the command to return.
        if (action.Trim().Equals("open_window", StringComparison.OrdinalIgnoreCase)) return McpOpenWindow(target, cancellationToken);
        // Saying something aloud waits for it to be heard, off the UI thread.
        if (action.Trim().Equals("say", StringComparison.OrdinalIgnoreCase)) return McpSpeech("say", text, voice, 0, true, false, "", "", "", "", "", "", 0, "", cancellationToken);
        return McpTutorialOnUi(action, target, caption, title, text, level, seconds, tutorial, step, references, path, cancellationToken);
    }
    // Every command that opens a window, a dialog or a tool of the app's own, with what it needs first. Commands that act
    // at once (Save, Delete, Exit…) and ones that open a Windows file picker aren't here: nothing in this list changes
    // the project by itself.
    static readonly (string Id, string Needs)[] WindowCommands =
    [
        ("file.recover", "an unsaved draft to recover"), ("file.export", ""), ("file.publish", ""), ("file.publishItch", ""),
        ("view.shortcuts", ""),
        ("project.preview", ""), ("project.test", "a project that can run in Minecraft"), ("project.screen", ""), ("project.settings", ""),
        ("project.pixelEditor", ""), ("project.musicMaker", ""), ("project.soundEffects", ""), ("project.spriteSheet", "a Sprite control selected"), ("project.mcp", ""),
        ("advanced.inputs", ""), ("advanced.inputCreator", ""), ("advanced.animations", ""), ("advanced.stateGraphs", ""), ("advanced.shaders", ""),
        ("advanced.particleMaker", ""), ("advanced.particles", ""), ("advanced.collider", "a collider, or a control with a physics body, selected"),
        ("advanced.tilemap", "a Tilemap control selected"), ("advanced.layers", ""), ("advanced.leaderboard.open", "a leaderboard page in the project"),
        ("advanced.createAudio", ""), ("advanced.createText", ""), ("advanced.speechSettings", ""),
        ("help.manual", ""), ("help.scriptApi", ""), ("help.updates", ""), ("help.about", ""),
    ];
    static string CommandTitle(string name) => name.Replace("…", "").Trim();
    List<string> OpenWindowTitles() => Application.Current.Windows.OfType<Window>().Where(w => w.IsVisible && w != this && w.Title.Length > 0).Select(w => w.Title)
        .Concat(CaptureTargets.Dialogs().Select(d => d.Title.Length > 0 ? d.Title + " (a message)" : "(a message)")).ToList();
    object WindowList() => new
    {
        canOpen = WindowCommands.Where(c => commands.Any(x => x.Id == c.Id)).Select(c => new { target = "window:" + c.Id, name = CommandTitle(Cmd(c.Id).Name), menu = Cmd(c.Id).Category, needs = c.Needs.Length > 0 ? c.Needs : null }),
        panels = dockContents.Keys.Select(k => new { target = "panel:" + k, title = PanelTitle(k) }),
        open = OpenWindowTitles(),
        note = "open_window target = window:<command> (or its name, e.g. Music maker) or panel:<id>; close_window target = words in a window's title, or all. Point inside an open window with highlight text:<words on a button or label>."
    };

    async Task<string> McpOpenWindow(string target, CancellationToken cancellationToken)
    {
        try
        {
            string opened = ""; string? problem = null; List<string> before = [];
            var running = Dispatcher.InvokeAsync(() =>
            {
                if (mcpHost == null) throw new InvalidOperationException("MCP server is stopped.");
                before = OpenWindowTitles();
                string wanted = target.Trim();
                if (wanted.StartsWith("panel:", StringComparison.OrdinalIgnoreCase))
                {
                    string panel = wanted["panel:".Length..].Trim();
                    if (!dockContents.ContainsKey(panel)) throw new InvalidOperationException("There's no panel " + panel + ". tutorial action windows lists them.");
                    ShowDock(panel); opened = "the " + PanelTitle(panel) + " panel"; return;
                }
                if (wanted.StartsWith("window:", StringComparison.OrdinalIgnoreCase)) wanted = wanted["window:".Length..].Trim();
                var known = WindowCommands.Where(c => commands.Any(x => x.Id == c.Id)).ToList();
                var match = known.Where(c => c.Id.Equals(wanted, StringComparison.OrdinalIgnoreCase)).Concat(known.Where(c => CommandTitle(Cmd(c.Id).Name).Equals(wanted, StringComparison.OrdinalIgnoreCase)))
                    .Concat(known.Where(c => wanted.Length > 2 && CommandTitle(Cmd(c.Id).Name).Contains(wanted, StringComparison.OrdinalIgnoreCase))).FirstOrDefault();
                if (match.Id == null) throw new InvalidOperationException("Nothing to open is called " + target + ". tutorial action windows lists every window and panel that can be opened.");
                opened = CommandTitle(Cmd(match.Id).Name);
                // These two would only put up a "nothing here" message for the person to dismiss.
                if (match.Id == "file.recover" && !Wysicraft.Packaging.RecoveryStore.Available(RecoveryRoot).Any()) { problem = "There are no unsaved drafts to recover."; return; }
                if (match.Id == "advanced.leaderboard.open" && project.Leaderboards.Count == 0) { problem = "This project has no leaderboard pages yet (make one with new_leaderboard or Advanced → Create leaderboard)."; return; }
                try { Cmd(match.Id).Run(); }
                catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException) { problem = ex.Message + (match.Needs.Length > 0 ? " (it needs " + match.Needs + ")" : ""); }
            }).Task;
            // A dialog that waits for the person keeps the command from returning: that still counts as opened.
            bool returned = await Task.WhenAny(running, Task.Delay(1500, cancellationToken)) == running;
            if (returned) await running;
            if (problem != null) throw new InvalidOperationException(problem);
            var now = await Dispatcher.InvokeAsync(OpenWindowTitles);
            return Json.Write(new
            {
                opened, appeared = now.Except(before).ToList(), open = now, waitsForThePerson = !returned ? true : (bool?)null,
                note = opened.StartsWith("MCP server", StringComparison.Ordinal) ? "This panel shows the access token: close it before recording the screen." : "Point at things in it with highlight text:<words on a button or label>; close it with close_window."
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException) { throw new ModelContextProtocol.McpException(ex.Message); }
    }

    Task<string> McpTutorialOnUi(string action, string target, string caption, string title, string text, string level, double seconds, string tutorial, int step, string references, string path, CancellationToken cancellationToken) => Dispatcher.InvokeAsync(() =>
    {
        if (mcpHost == null) throw new InvalidOperationException("MCP server is stopped.");
        switch (action.Trim().ToLowerInvariant())
        {
            case "targets": return Json.Write(TutorialTargets());
            case "highlight": return Json.Write(new { highlighted = Highlight(target, caption, seconds), note = "It clears when the person clicks it, presses Escape, after the given seconds (default 30), or with the next highlight." });
            case "clear": ClearHighlight(); return Json.Write(new { cleared = true });
            case "notify": Notify(title, text, level, seconds); return Json.Write(new { shown = true, onScreen = NoticeCount });
            case "open":
                var t = Json.Read<Tutorial>(tutorial.Trim().Length > 0 ? tutorial : throw new InvalidOperationException("Give the tutorial as JSON: {title, steps:[{title, text, target, caption, image, check}], references:[{title, text, link}]}."));
                foreach (var s in t.Steps) { if (s.Target.Length > 0 && FindTutorialTarget(s.Target) == null && SplitTarget(s.Target).Kind != "element") throw new InvalidOperationException("Step \"" + s.Title + "\": nothing in the editor matches " + s.Target + ". tutorial action targets lists what can be pointed at."); if (s.Check != null) TutorialCheckState(s.Check); }
                var window = OpenTutorial(t); return Json.Write(window.State());
            case "step": { var w = tutorialWindow ?? throw new InvalidOperationException("No tutorial is open (action open)."); w.GoTo(step - 1); return Json.Write(w.State()); }
            case "reference":
            {
                var w = tutorialWindow ?? throw new InvalidOperationException("No tutorial is open (action open).");
                var node = JsonNode.Parse(references.Trim().Length > 0 ? references : throw new InvalidOperationException("Give references as JSON: {title, text, link} or a list of them."));
                var list = node is JsonArray ? Json.Read<List<TutorialReference>>(references) : [Json.Read<TutorialReference>(references)];
                w.AddReferences(list); return Json.Write(w.State());
            }
            case "select":
            {
                // A script opened in the Scripts panel, ready to point at.
                if (target.StartsWith("script:", StringComparison.Ordinal))
                {
                    string scriptPath = target["script:".Length..];
                    if (!project.Scripts.ContainsKey(scriptPath)) throw new InvalidOperationException("There's no script " + scriptPath + ". get_project lists them.");
                    RefreshScripts(scriptPath); ShowDock("scripts");
                    return Json.Write(new { opened = scriptPath, note = "It's open in the Scripts panel: point at it with panel:scripts." });
                }
                // A screen with nothing selected: Properties and Events show the screen's own settings and events.
                if (target.StartsWith("screen:", StringComparison.Ordinal))
                {
                    var shown = project.Screens.FirstOrDefault(s => s.Id == target["screen:".Length..]) ?? throw new InvalidOperationException("There's no screen " + target["screen:".Length..] + ".");
                    if (shown != ui) ShowScreen(shown);
                    selected.Clear(); RefreshAll(); ShowDock("events");
                    return Json.Write(new { screen = shown.Id, note = "Properties and Events show the screen's own settings and events now." });
                }
                // Selecting a control (on its screen) so Properties shows it: its fields and component cards can then be pointed at.
                string id = target.StartsWith("element:", StringComparison.Ordinal) ? target["element:".Length..] : target;
                var screen = (ui.Elements.Any(e => e.Id == id) ? ui : null) ?? project.Screens.FirstOrDefault(s => s.Elements.Any(e => e.Id == id))
                    ?? throw new InvalidOperationException("There's no control called " + id + ". get_project lists them.");
                if (screen != ui) ShowScreen(screen);
                selected.Clear(); selected.Add(id); RefreshAll(); ShowDock("properties");
                return Json.Write(new { selected = id, screen = screen.Id, note = "Properties shows it now: point at its fields with property:<label>, e.g. property:Sound on a Pickup card." });
            }
            case "windows": return Json.Write(WindowList());
            case "close_window":
            {
                // By words in its title, or all of them. Closing is asked of each window in its own turn, so one with
                // unsaved work can ask the person first without holding this up.
                string words = target.StartsWith("window:", StringComparison.OrdinalIgnoreCase) ? target["window:".Length..].Trim() : target.Trim();
                if (words.Length == 0) throw new InvalidOperationException("Give target: words in the window's title (tutorial action windows lists what's open), or all.");
                bool everything = words.Equals("all", StringComparison.OrdinalIgnoreCase);
                var closing = Application.Current.Windows.OfType<Window>().Where(w => w.IsVisible && w != this && (everything ? w != tutorialWindow : w.Title.Contains(words, StringComparison.OrdinalIgnoreCase))).ToList();
                // Message boxes too (About, a notice): they're Windows' own dialogs, closed as their ✕ would.
                var messages = CaptureTargets.Dialogs().Where(d => everything || d.Title.Contains(words, StringComparison.OrdinalIgnoreCase) || words.Equals("message", StringComparison.OrdinalIgnoreCase)).ToList();
                if (closing.Count == 0 && messages.Count == 0) throw new InvalidOperationException("No open window has \"" + words + "\" in its title. Open: " + string.Join("; ", OpenWindowTitles()));
                foreach (var d in messages) CaptureTargets.CloseDialog(d.Handle);
                foreach (var w in closing) Dispatcher.BeginInvoke(() => { try { if (w.IsVisible) w.Close(); } catch (InvalidOperationException) { } });
                return Json.Write(new { closing = closing.Select(w => w.Title).Concat(messages.Select(d => d.Title + " (a message)")), note = "A window with unsaved work asks the person before it closes; a question with only Yes and No waits for them too." });
            }
            case "close_editors":
            {
                // The pixel editor and makers an assistant left open while it talked about them; ones with unsaved changes stay.
                var closed = new List<string>();
                foreach (var w in Application.Current.Windows.OfType<Window>().Where(w => w.IsVisible).ToList())
                {
                    if (w is PixelEditor p && !p.TestDirty) { p.TestDiscard(); closed.Add("pixel editor"); }
                    else if (w is SoundEffectMaker s && s.Untouched) { s.Close(); closed.Add("sound effect maker"); }
                    else if (w is ParticleMaker m && m.Untouched) { m.Close(); closed.Add("particle maker"); }
                }
                return Json.Write(new { closed });
            }
            case "status": return Json.Write(tutorialWindow?.State() ?? (object)new { open = false });
            case "save":
            {
                var w = tutorialWindow ?? throw new InvalidOperationException("No tutorial is open (action open).");
                if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(Path.GetDirectoryName(path))) throw new InvalidOperationException("path is an absolute .html path in a folder that exists.");
                File.WriteAllText(path, TutorialHtml(w.Tutorial)); return Json.Write(new { saved = path });
            }
            case "close": tutorialWindow?.Close(); tutorialWindow = null; ClearHighlight(); return Json.Write(new { closed = true });
            default: throw new InvalidOperationException("action is targets, highlight, clear, notify, open, step, reference, say, select, windows, open_window, close_window, close_editors, status, save or close.");
        }
    }).Task;
}

public sealed partial class DesignerMcpTools
{
    [McpServerTool(Name = "tutorial"), Description("Teach the person the editor, inside the app. action: targets (what can be pointed at now, with the exact names: call it before guessing one); highlight (target, caption, seconds, default 30: a pulsing outline and caption that clicks pass through; returns at once; target is menu:<command>, toolbar:<command>, panel:<id>, toolbox:<type>, element:<id>, property:<label>, text:<words>, card:<component>, screens or, while a pixel editor is open, pixel:tools|layers|frames|duplicate|preview); clear; notify (title, text, level info|success|warn, seconds: a small notice that blocks nothing); open (tutorial = JSON {title, voice, steps:[{title, text, narration, target, caption, image, check}], references}: a step-by-step window that never locks the app; a check ticks a step off by itself, e.g. {kind:'element', element:'play'} (a control called play exists) or {kind:'event', element:'play', event:'click'}); step (step = 1-based number); reference (references = JSON {title, text, link} or a list); say (text spoken aloud, optional voice; returns when it has been heard); select (target = element:<id>, screen:<id> or script:<path>); windows (what can be opened, and what is open); open_window (target = window:<command or name> or panel:<id>; it never saves, exports or publishes by itself); close_window (target = words in the title, or all); close_editors; status; save (path = absolute .html); close. Tutorials are kept in the app's Tutorials folder, never in the project. Every check kind, the image forms and the limits: guide(topic:\"tutorial\").")]
    public Task<string> Tutorial(string action, string target = "", string caption = "", string title = "", string text = "", string level = "info", double seconds = 0, string tutorial = "", int step = 0, string references = "", string path = "", string voice = "", CancellationToken cancellationToken = default) =>
        editor.McpTutorial(action, target, caption, title, text, level, seconds, tutorial, step, references, path, voice, cancellationToken);
}

/// <summary>The highlight: a pulsing outline round the adorned element and a caption below it (or above, when there's no
/// room below in its window). It takes no clicks: they go to what's underneath.</summary>
sealed class HighlightAdorner : Adorner
{
    static readonly Color Accent = Color.FromRgb(0xF4, 0xC7, 0x44);
    readonly string caption;
    public HighlightAdorner(UIElement adorned, string caption) : base(adorned)
    {
        this.caption = caption; IsHitTestVisible = false;
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.45, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
    }
    protected override void OnRender(DrawingContext dc)
    {
        var size = AdornedElement.RenderSize; var ring = new Rect(-4, -4, size.Width + 8, size.Height + 8);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x26, Accent.R, Accent.G, Accent.B)), new Pen(new SolidColorBrush(Accent), 3), ring, 6, 6);
        if (caption.Length == 0) return;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var text = new FormattedText(caption, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 13, Brushes.White, dpi) { MaxTextWidth = 320 };
        double w = text.Width + 20, h = text.Height + 12;
        // Room in the window: the adorner layer covers it, so its size says where the caption fits.
        var layer = AdornerLayer.GetAdornerLayer(AdornedElement) as FrameworkElement;
        var origin = layer != null ? AdornedElement.TranslatePoint(new Point(0, 0), layer) : new Point();
        double roomBelow = layer != null ? layer.ActualHeight - (origin.Y + ring.Bottom) : double.MaxValue;
        double y = roomBelow > h + 12 ? ring.Bottom + 8 : ring.Top - 8 - h;
        double x = ring.Left;
        if (layer != null) x = Math.Clamp(x, -origin.X + 4, Math.Max(-origin.X + 4, layer.ActualWidth - origin.X - w - 4));
        var bubble = new Rect(x, y, w, h);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x1E, 0x23, 0x2A)), new Pen(new SolidColorBrush(Accent), 1.5), bubble, 6, 6);
        dc.DrawText(text, new Point(x + 10, y + 6));
    }
}

/// <summary>Notices, stacked in the bottom-right corner of the editor. Only the cards take clicks (their ✕); the rest
/// of the editor works as usual.</summary>
sealed class NoticeAdorner : Adorner
{
    readonly StackPanel stack = new() { Width = 340, VerticalAlignment = VerticalAlignment.Bottom };
    readonly VisualCollection children;
    public NoticeAdorner(UIElement adorned) : base(adorned) { children = new VisualCollection(this) { stack }; }
    internal int Count => stack.Children.Count;
    internal Border Add(string title, string text, string level)
    {
        while (stack.Children.Count >= 3) stack.Children.RemoveAt(0);
        var colour = level.Trim().ToLowerInvariant() switch { "warn" or "warning" => Color.FromRgb(0xF4, 0xC7, 0x44), "success" => Color.FromRgb(0x7F, 0xD0, 0x8A), _ => Color.FromRgb(0x5F, 0xB3, 0xF0) };
        var close = new Button { Content = "✕", Width = 22, Height = 22, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Top, Cursor = Cursors.Hand, ToolTip = "Close", Focusable = false };
        var head = new DockPanel(); DockPanel.SetDock(close, Dock.Right); head.Children.Add(close);
        head.Children.Add(new TextBlock { Text = title, Foreground = new SolidColorBrush(colour), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var body = new StackPanel(); body.Children.Add(head); body.Children.Add(new TextBlock { Text = text, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        var card = new Border { Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x23, 0x2A)), BorderBrush = new SolidColorBrush(colour), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 8, 10), Margin = new Thickness(0, 8, 0, 0), Child = body,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.5 } };
        close.Click += (_, _) => Remove(card);
        stack.Children.Add(card); InvalidateMeasure(); InvalidateArrange();
        return card;
    }
    internal void Remove(Border card) { stack.Children.Remove(card); InvalidateArrange(); }
    internal void Clear() { stack.Children.Clear(); InvalidateArrange(); }
    protected override int VisualChildrenCount => children.Count;
    protected override Visual GetVisualChild(int index) => children[index];
    protected override System.Windows.Size MeasureOverride(System.Windows.Size constraint) { stack.Measure(new System.Windows.Size(340, double.PositiveInfinity)); return AdornedElement.RenderSize; }
    protected override System.Windows.Size ArrangeOverride(System.Windows.Size finalSize)
    {
        var size = AdornedElement.RenderSize; double h = stack.DesiredSize.Height;
        stack.Arrange(new Rect(Math.Max(0, size.Width - 340 - 20), Math.Max(0, size.Height - h - 36), 340, h));
        return finalSize;
    }
}

/// <summary>The step-by-step tutorial window: it never locks the editor, can stay on top of everything, and ticks
/// steps off as the person does them.</summary>
sealed class TutorialWindow : Window
{
    readonly MainWindow editor;
    internal Tutorial Tutorial { get; }
    int index;
    readonly TextBlock heading = new() { FontSize = 12, Foreground = Brushes.LightGray }, stepTitle = new() { FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap },
        body = new() { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new Thickness(0, 6, 0, 0) }, state = new() { FontSize = 12, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
    readonly Image picture = new() { MaxHeight = 240, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
    readonly Button back = new() { Content = "◀ Back", Padding = new Thickness(10, 3, 10, 3) }, showMe = new() { Content = "Show me", Padding = new Thickness(12, 3, 12, 3), FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 8, 0) },
        next = new() { Content = "Next ▶", Padding = new Thickness(10, 3, 10, 3) }, save = new() { Content = "Save…", Padding = new Thickness(10, 2, 10, 2) },
        referencesButton = new() { Padding = new Thickness(8, 1, 8, 1), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0xB3, 0xF0)), Cursor = Cursors.Hand };
    readonly CheckBox onTop = new() { Content = "Always on top", Foreground = Brushes.LightGray, FontSize = 12 }, autoNext = new() { Content = "Move on when done", Foreground = Brushes.LightGray, FontSize = 12, IsChecked = true },
        aloud = new() { Content = "🔊 Read aloud", Foreground = Brushes.LightGray, FontSize = 12, ToolTip = "Speak each step as it's shown." };
    readonly StackPanel referenceList = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
    readonly HashSet<int> done = new(); readonly HashSet<int> advanced = new();
    readonly DispatcherTimer checker;
    public TutorialWindow(MainWindow editor, Tutorial tutorial)
    {
        this.editor = editor; Tutorial = tutorial;
        SetResourceReference(StyleProperty, typeof(Window));
        Title = tutorial.Title.Length > 0 ? tutorial.Title : "Tutorial"; Width = 420; Height = 560; MinWidth = 300; MinHeight = 320; ShowActivated = true; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual; Left = editor.Left + Math.Max(0, editor.ActualWidth - 460); Top = editor.Top + 80;
        var root = new DockPanel { Margin = new Thickness(14) };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(onTop, Dock.Right); top.Children.Add(onTop);
        DockPanel.SetDock(aloud, Dock.Right); top.Children.Add(aloud); top.Children.Add(heading);
        aloud.Visibility = tutorial.Steps.Any(s => s.Narration.Trim().Length > 0) && TextToSpeech.Available ? Visibility.Visible : Visibility.Collapsed;
        aloud.IsChecked = editor.ReadTutorialsAloud;
        aloud.Click += (_, _) => { editor.ReadTutorialsAloud = aloud.IsChecked == true; if (aloud.IsChecked == true) Narrate(); else TextToSpeech.Shared.Stop(); };
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var nav = new DockPanel { Margin = new Thickness(0, 10, 0, 0) }; DockPanel.SetDock(nav, Dock.Bottom);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center }; buttons.Children.Add(back); buttons.Children.Add(showMe); buttons.Children.Add(next);
        var extras = new DockPanel { Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(save, Dock.Right); extras.Children.Add(save); DockPanel.SetDock(referencesButton, Dock.Right); extras.Children.Add(referencesButton); extras.Children.Add(autoNext);
        var bottom = new StackPanel(); bottom.Children.Add(buttons); bottom.Children.Add(extras); bottom.Children.Add(referenceList); nav.Children.Add(bottom);
        root.Children.Add(nav);
        var content = new StackPanel(); content.Children.Add(stepTitle); content.Children.Add(state); content.Children.Add(body); content.Children.Add(picture);
        root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        onTop.Checked += (_, _) => Topmost = true; onTop.Unchecked += (_, _) => Topmost = false;
        back.Click += (_, _) => GoTo(index - 1); next.Click += (_, _) => GoTo(index + 1);
        showMe.Click += (_, _) => ShowMe();
        save.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = (Title.Length > 0 ? Title : "Tutorial") + ".html", Filter = "Web page|*.html", DefaultExt = ".html" };
            if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, editor.TutorialHtml(Tutorial));
        };
        referencesButton.Click += (_, _) => referenceList.Visibility = referenceList.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        checker = new DispatcherTimer(TimeSpan.FromMilliseconds(700), DispatcherPriority.Background, (_, _) => Check(), Dispatcher);
        Closed += (_, _) => { checker.Stop(); editor.ClearHighlight(); if (aloud.Visibility == Visibility.Visible) TextToSpeech.Shared.Stop(); };
        ShowReferences(); GoTo(0); checker.Start();
    }
    internal int Index => index;
    internal void GoTo(int to)
    {
        index = Math.Clamp(to, 0, Tutorial.Steps.Count - 1);
        var s = Tutorial.Steps[index];
        heading.Text = $"Step {index + 1} of {Tutorial.Steps.Count}";
        stepTitle.Text = s.Title.Length > 0 ? s.Title : "Step " + (index + 1);
        body.Text = s.Text;
        showMe.IsEnabled = s.Target.Length > 0; showMe.ToolTip = s.Target.Length > 0 ? "Points at " + editor.TutorialWhere(s.Target) : "Nothing to point at in this step.";
        back.IsEnabled = index > 0; next.Content = index == Tutorial.Steps.Count - 1 ? "Finish" : "Next ▶";
        picture.Source = null; picture.Visibility = Visibility.Collapsed;
        if (s.ImageData.Length > 0)
            try { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.StreamSource = new MemoryStream(Convert.FromBase64String(s.ImageData)); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.EndInit(); picture.Source = bitmap; picture.Visibility = Visibility.Visible; }
            catch (Exception ex) when (ex is NotSupportedException or FormatException or IOException) { }
        editor.ClearHighlight(); Check(); Narrate();
    }
    /// <summary>Reads the step's narration aloud (interrupting the last one), when Read aloud is on.</summary>
    void Narrate()
    {
        var s = Tutorial.Steps[index];
        if (aloud.Visibility != Visibility.Visible || aloud.IsChecked != true) return;
        if (s.Narration.Trim().Length == 0) { TextToSpeech.Shared.Stop(); return; }
        _ = TextToSpeech.Shared.SayAsync(s.Narration, Tutorial.Voice.Length > 0 ? Tutorial.Voice : editor.AssistantVoice, editor.AssistantSpeed).ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted);
    }
    internal void ShowMe()
    {
        var s = Tutorial.Steps[index]; if (s.Target.Length == 0) return;
        try { editor.Highlight(s.Target, s.Caption.Length > 0 ? s.Caption : stepTitle.Text, 30); }
        catch (InvalidOperationException ex) { state.Text = ex.Message; state.Foreground = Brushes.Orange; }
    }
    void Check()
    {
        var s = Tutorial.Steps[index];
        for (int i = 0; i < Tutorial.Steps.Count; i++)
            if (Tutorial.Steps[i].Check is { } c) { try { if (editor.TutorialCheckState(c).Done) done.Add(i); else done.Remove(i); } catch (InvalidOperationException) { } }
        if (s.Check == null) { state.Text = ""; return; }
        var (isDone, what) = editor.TutorialCheckState(s.Check);
        state.Text = isDone ? "✔ Done: " + what : "○ To do: " + what;
        state.Foreground = isDone ? new SolidColorBrush(Color.FromRgb(0x7F, 0xD0, 0x8A)) : Brushes.LightGray;
        // Once per step: done moves on (after a moment to see the tick), unless the person turned that off.
        if (isDone && autoNext.IsChecked == true && index < Tutorial.Steps.Count - 1 && advanced.Add(index))
        {
            int from = index; var wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            wait.Tick += (_, _) => { wait.Stop(); if (index == from) GoTo(from + 1); }; wait.Start();
        }
    }
    internal void AddReferences(IEnumerable<TutorialReference> list) { Tutorial.References.AddRange(list.Where(r => r.Title.Trim().Length > 0 || r.Text.Trim().Length > 0)); ShowReferences(); }
    void ShowReferences()
    {
        referenceList.Children.Clear();
        referencesButton.Content = "References (" + Tutorial.References.Count + ")"; referencesButton.Visibility = Tutorial.References.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var r in Tutorial.References)
        {
            var box = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            box.Children.Add(new TextBlock { Text = r.Title, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (r.Text.Length > 0) box.Children.Add(new TextBlock { Text = r.Text, Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap });
            if (Uri.TryCreate(r.Link, UriKind.Absolute, out var link) && link.Scheme is "http" or "https")
            {
                var open = new TextBlock(); var hyper = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(link.ToString()));
                hyper.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link.ToString()) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { } };
                open.Inlines.Add(hyper); box.Children.Add(open);
            }
            referenceList.Children.Add(box);
        }
    }
    internal object State() => new
    {
        open = IsVisible, title = Tutorial.Title, step = index + 1, steps = Tutorial.Steps.Count,
        done = Tutorial.Steps.Select((s, i) => new { step = i + 1, s.Title, check = s.Check == null ? null : (bool?)done.Contains(i) }),
        references = Tutorial.References.Count
    };
}
