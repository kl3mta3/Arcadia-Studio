using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wysicraft.Models;
namespace Wysicraft.Designer;

// --smoke-tutorial: the tutorial tools in the real editor. Every kind of target is found and outlined, the outline
// clears on Escape, on a click on what it points at and with the next highlight; notices show and are rate-limited;
// and a tutorial window ticks steps off as the project changes, moves on by itself, points at a step's target, lists
// references and saves as one HTML file.
public partial class MainWindow
{
    internal async Task VerifyTutorialAsync(string output)
    {
        void Expect(bool ok, string what) { if (!ok) throw new Exception("Tutorial tools: " + what); }
        // Pictures of the windows as they are, beside the result, for a look at the layout.
        static void Picture(Window w, string file) { w.UpdateLayout(); if (w.ActualWidth < 1) return; var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)w.ActualWidth, (int)w.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bmp.Render(w); var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp)); using var stream = File.Create(file); png.Save(stream); }
        async Task Settle(int ms = 250) { await Task.Delay(ms); UpdateLayout(); }
        Show(); Activate(); await Settle(600);
        var results = new List<string>();
        try
        {
            // Targets of every kind: found, revealed, outlined where they are.
            string first = ui.Elements.First(e => e.Type != "sound").Id;
            foreach (var target in new[] { "menu:file.publish", "toolbar:project.preview", "panel:layers", "toolbox:button", "element:" + first, "text:Preview" })
            {
                string where = Highlight(target, "", 10); await Settle(400);
                var found = FindTutorialTarget(target)!;
                // A menu item sits in a popup: the menu it opened from is outlined, with the item's path as the caption.
                Expect(HighlightShowing && (target.StartsWith("menu:") || (HighlightRect is Rect ring && ScreenRect(found.Element) is Rect at && ring.Contains(at.TopLeft) && ring.Contains(at.BottomRight))), target + " is outlined where it is (" + where + ")");
                results.Add(target + " → " + where);
                ClearHighlight(); await Settle(150);
                Expect(!Menus.Items.OfType<MenuItem>().Any(m => m.IsSubmenuOpen), "clearing a highlight closes any menu it opened (" + target + ")");
            }
            Expect(FindTutorialTarget("menu:file.publish")!.Where.Contains("Publish"), "a menu command is described by its menu path");
            // A Properties field, by its label, with a control selected so Properties has fields.
            selected.Clear(); selected.Add(first); RefreshAll(); await Settle();
            string property = ((IEnumerable<string>)((dynamic)TutorialTargets()).property).First().Split(':', 2)[1];
            Highlight("property:" + property, "", 10); await Settle(400);
            Expect(HighlightShowing, "a Properties field is found by its label: " + property);
            // Escape clears it; so does clicking what it points at; so does the next highlight.
            RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this)!, 0, Key.Escape) { RoutedEvent = PreviewKeyDownEvent });
            await Settle(); Expect(!HighlightShowing, "Escape clears the highlight");
            Highlight("toolbar:project.validate", "", 10); await Settle(300);
            FindTutorialTarget("toolbar:project.validate")!.Element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = PreviewMouseDownEvent });
            await Settle(); Expect(!HighlightShowing, "clicking what it points at clears it");
            Highlight("panel:layers", "", 10); Highlight("panel:assets", "", 10); await Settle(300);
            Expect(HighlightShowing && FindTutorialTarget("panel:assets") is { } assets && ScreenRect(assets.Element) is Rect ar && HighlightRect is Rect hr && hr.Contains(ar.TopLeft), "the next highlight replaces it");
            ClearHighlight();
            // A component card low down in Properties, and a field on it, are scrolled into view when they're pointed at
            // (and the section they're in is opened if it was folded).
            var gem = new Element { Id = "tut_gem", Type = "panel", Bounds = new Bounds { X = 4, Y = 4, Width = 16, Height = 16 } };
            ui.Elements.Add(gem); Wysicraft.Core.Behaviours.Apply(project, ui, gem, "pickup"); selected.Clear(); selected.Add(gem.Id); RefreshAll(); await Settle(400);
            foreach (var section in Properties.Children.OfType<Expander>()) section.IsExpanded = false;
            await Settle(200);
            var propertiesPanel = FindTutorialTarget("panel:properties")!.Element;
            foreach (var target in new[] { "card:pickup", "property:Particles", "property:Sound" })
            {
                string where = Highlight(target, "", 10); await Settle(500);
                var found = FindTutorialTarget(target)!;
                Expect(HighlightShowing && ScreenRect(found.Element) is Rect at && ScreenRect(propertiesPanel) is Rect panel && at.Top >= panel.Top - 2 && at.Top < panel.Bottom - 12, target + " is scrolled into view in Properties (" + where + "): " + ScreenRect(found.Element) + " in " + ScreenRect(propertiesPanel));
                results.Add(target + " → " + where + " (in view)");
                ClearHighlight(); await Settle(150);
            }
            Wysicraft.Core.Behaviours.Remove(project, ui, gem, "pickup"); ui.Elements.Remove(gem); selected.Clear(); RefreshAll(); await Settle();
            bool unknown = false; try { Highlight("panel:nothing_here", "", 5); } catch (InvalidOperationException ex) { unknown = ex.Message.Contains("targets"); }
            Expect(unknown, "an unknown target is refused, pointing at the targets list");

            // Notices: shown without taking the keyboard, rate-limited, three at most.
            Notify("Heads up", "Your cover is 64 × 40; Arcadia shows 1280 × 800.", "warn", 30); await Settle();
            Expect(NoticeCount == 1 && IsActive, "a notice shows and the editor keeps the keyboard");
            bool limited = false; try { Notify("Again", "Too soon.", "info", 30); } catch (InvalidOperationException) { limited = true; }
            Expect(limited && NoticeCount == 1, "a second notice within two seconds is refused");
            for (int i = 0; i < 3; i++) { await Task.Delay(2100); Notify("Notice " + i, "Text " + i, "success", 30); }
            await Settle(); Expect(NoticeCount == 3, "at most three notices at once: " + NoticeCount);

            // A tutorial: steps tick off as the project changes and move on by themselves.
            string picture = Path.Combine(Path.GetTempPath(), "tutorial-" + Guid.NewGuid().ToString("N") + ".html");
            var tutorial = new Tutorial
            {
                Title = "Your first button",
                Steps =
                [
                    new() { Title = "Add a button", Text = "Drag a Button from the Toolbox onto the canvas.\n\nCall it tut_play.", Target = "toolbox:button", Image = "capture:toolbox:button", Check = new() { Kind = "element", Element = "tut_play" } },
                    new() { Title = "Make it do something", Text = "In Events, give its click an action.", Target = "panel:events", Check = new() { Kind = "event", Element = "tut_play", Event = "click" } },
                    new() { Title = "Try it", Text = "Click Preview and press your button.", Target = "toolbar:project.preview" }
                ],
                References = [new() { Title = "Events and actions", Text = "Every event a control has, and what actions do.", Link = "https://example.com/events" }]
            };
            var window = OpenTutorial(tutorial); await Settle(800);
            window.ShowMe(); await Settle(500); Picture(window, output + ".window.png");
            Picture(this, output + ".editor.png"); // the highlight and the notices are drawn in the editor window itself
            ClearHighlight();
            Expect(window.IsVisible && window.Index == 0 && !window.Topmost && IsEnabled, "the tutorial opens at step 1 and doesn't lock the editor");
            Expect(tutorial.Steps[0].ImageData.Length > 100, "a step's picture can be the editor around a target");
            Change(); ui.Elements.Add(new Element { Id = "tut_play", Type = "button", Text = "Play", Bounds = new() { X = 10, Y = 10, Width = 60, Height = 20 } }); RefreshAll();
            await Settle(2200);
            Expect(window.Index == 1, "a done step moves on by itself: step " + (window.Index + 1));
            ui.Elements.First(e => e.Id == "tut_play").Events["click"] = new UiEvent { Client = new() { Actions = [new VisualAction { Type = "message", Value = "Hi" }] } };
            await Settle(2200);
            var state = System.Text.Json.Nodes.JsonNode.Parse(Json.Write(window.State()))!;
            Expect(state["done"]![0]!["check"]!.GetValue<bool>() && state["done"]![1]!["check"]!.GetValue<bool>() && window.Index == 2, "both checks tick off, and it moves on to step 3: " + state.ToJsonString());
            window.ShowMe(); await Settle(400);
            Expect(HighlightShowing, "Show me points at the step's target");
            window.GoTo(0); await Settle(); Expect(!HighlightShowing && window.Index == 0, "Back/going to a step clears the highlight");
            window.AddReferences([new TutorialReference { Title = "Preview", Text = "What Preview can do." }]);
            Expect(tutorial.References.Count == 2, "references can be added while it's open");
            string html = TutorialHtml(tutorial);
            Expect(html.Contains("Your first button") && html.Contains("Make it do something") && html.Contains("data:image/png;base64,") && html.Contains("Where: ") && html.Contains("Done when: a control called tut_play") && html.Contains("https://example.com/events") && !html.Contains("<script"), "saving writes one HTML file: steps, where, checks, pictures and references, no scripts");
            Expect(File.Exists(Path.Combine(Wysicraft.Core.AppFolders.Path("Tutorials"), "your-first-button.json")), "the tutorial is kept in the app's Tutorials folder");
            window.Close(); await Settle();
            Expect(!HighlightShowing && OpenTutorialWindow == null, "closing it clears everything");
            results.Add("tutorial ok");
        }
        finally
        {
            ClearHighlight(); OpenTutorialWindow?.Close();
            ClearNotices();
            try { File.Delete(Path.Combine(Wysicraft.Core.AppFolders.Path("Tutorials"), "your-first-button.json")); } catch (IOException) { }
            dirty = false;
        }
        File.WriteAllText(output, "PASS: tutorial tools — highlight (menu path, toolbar, panel, toolbox, canvas control, text, Properties field; Escape, click and next highlight clear it; unknown targets refused), notices (keyboard kept, 2 s limit, 3 at once), tutorial window (editor picture, checks tick off and move on, Show me, references, HTML with pictures and no scripts, kept in Tutorials).\n" + string.Join("\n", results));
    }
}
