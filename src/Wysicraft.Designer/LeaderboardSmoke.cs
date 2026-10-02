using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wysicraft.Models;
using Wysicraft.Packaging;
using Path = System.IO.Path;
using Validation = Wysicraft.Core.Validation;
namespace Wysicraft.Designer;

// --smoke-leaderboard: the leaderboard editor in the real window. Creating a page, every toolbox widget, the inspector,
// what can't go where, Undo, renaming, saving and opening, .lb files and pages, a page from elsewhere, the preview with
// sample players, the leaderboard creator (a window of its own on a copy of the project), and the Publish window's
// choice with Create, Edit and Remove. The loaded project file is never written.
public partial class MainWindow
{
    internal async Task VerifyLeaderboardEditorAsync(string output)
    {
        void Expect(bool ok, string what) { if (!ok) throw new Exception("Leaderboard editor: " + what); }
        string temp = Path.Combine(Path.GetTempPath(), "lb-smoke-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        int screens = project.Screens.Count; var gameScreen = ui;
        try
        {
            // A new page opens on the canvas with its own toolbox, and the game's screens are untouched.
            var board = CreateLeaderboard();
            Expect(ui == board && ui.IsLeaderboard && project.Leaderboards.Count == 1 && project.Screens.Count == screens, "Create leaderboard opens a new page, apart from the screens");
            Expect(ComponentSourceBar.Visibility == Visibility.Visible && ComponentSourceLabel.Text.Contains("leaderboard") && boardBar?.Visibility == Visibility.Visible && Screens.SelectedItem == null, "the bar says a leaderboard is open, with its buttons");
            var tags = Toolbox.Items.OfType<ListBoxItem>().Select(i => i.Tag as string).Where(t => t != null).ToList();
            Expect(BoardStamps.All(s => tags.Contains(s.Tag)) && tags.Contains("label") && !tags.Contains("button") && !tags.Contains("slots:crafting_table"), "the toolbox has page pieces and every widget, and nothing from the game: " + string.Join(",", tags));
            Expect(board.Elements.Count == 7 && Validation.Errors(project).Count == 0, "the starter page is complete and the project still validates");
            selected.Clear(); SaveCanvasPicture(output + ".starter.png");

            // Every widget from the toolbox: added, drawn and shown in the inspector.
            int before = board.Elements.Count;
            foreach (var (tag, _, label, _) in BoardStamps)
            {
                AddControl(tag, 20, 20);
                var added = ui.Elements[^1];
                Expect(selected.Contains(added.Id) && Leaderboards.Types.Contains(added.Type), label + " is added and selected");
                Draw(); RefreshInspector();
                var headings = InspectorText();
                Expect(headings.Contains("LAYOUT") && (!added.Type.StartsWith("lb_") || headings.Contains("LEADERBOARD")) && !headings.Contains("BEHAVIOR"), label + ": the inspector shows its settings, and no game settings: " + string.Join("|", headings.Take(12)));
            }
            Expect(board.Elements.Count == before + BoardStamps.Length, "one control per widget");
            foreach (var (tag, _, _, _) in new[] { ("button", "", "", ""), ("slots:crafting_table", "", "", ""), ("tilemap", "", "", "") })
            {
                bool refused = false; try { AddControl(tag, 0, 0); } catch (InvalidOperationException) { refused = true; }
                Expect(refused, tag + " can't go on a leaderboard page");
            }
            // Undo stays on the page.
            int count = board.Elements.Count; history.Undo();
            Expect(ui.IsLeaderboard && ui.Id == board.Id && ui.Elements.Count == count - 1, "Undo takes back the last widget and stays on the page");
            history.Redo(); board = ui;

            // Page settings: nothing selected.
            selected.Clear(); RefreshInspector();
            Expect(InspectorText().Contains("LEADERBOARD PAGE"), "with nothing selected, the page's settings");
            RenameLeaderboard("hall_of_fame");
            Expect(ui.Id == "hall_of_fame" && ComponentSourceLabel.Text.Contains("hall_of_fame"), "the page can be renamed");

            // Back on a game screen, widgets are refused and the game's toolbox returns.
            BackToScreen(this, new RoutedEventArgs());
            Expect(!ui.IsLeaderboard && ComponentSourceBar.Visibility == Visibility.Collapsed && Toolbox.Items.OfType<ListBoxItem>().Any(i => (i.Tag as string) == "button"), "Back to screen returns to the game's screens and toolbox");
            bool widgetRefused = false; try { AddControl("lb:top10", 0, 0); } catch (InvalidOperationException) { widgetRefused = true; }
            Expect(widgetRefused, "widgets can't go on a game screen");
            OpenLeaderboard(project.Leaderboards[0]); board = ui;

            // Saved in the project file, and opened again.
            string saved = Path.Combine(temp, "game.arcadia");
            ProjectStore.SaveProject(project, saved);
            var reopened = ProjectStore.Load(saved);
            Expect(reopened.Leaderboards.Count == 1 && reopened.Leaderboards[0].Id == "hall_of_fame" && reopened.Leaderboards[0].Elements.Count == board.Elements.Count && reopened.Screens.Count == screens, "the page is saved in the project and opens again");

            // A .lb of its own, imported back: a second page with an ID of its own.
            string lb = Path.Combine(temp, "hall.lb"); File.WriteAllBytes(lb, Leaderboards.Standalone(project, board));
            string? choice = ImportLeaderboardFile(lb, open: false, confirm: _ => throw new Exception("a .lb isn't asked about"));
            Expect(choice == "board:hall_of_fame1" && project.Leaderboards.Count == 2, "a .lb imports as another page: " + choice);
            // An exported page opens again for editing; a page from elsewhere is used as it is.
            foreach (var (path, bytes) in Leaderboards.Page(project, board)) { var full = Path.Combine(temp, "page", path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllBytes(full, bytes); }
            choice = ImportLeaderboardFile(Path.Combine(temp, "page", "leaderboard.html"), open: false, confirm: _ => throw new Exception("a page made here isn't asked about"));
            Expect(choice == "board:hall_of_fame2" && project.Leaderboards.Count == 3 && project.Leaderboards[2].Elements.Count == board.Elements.Count, "an exported page imports as an editable page");
            string foreign = Path.Combine(temp, "mine.html"); File.WriteAllText(foreign, "<html><body>my own board</body></html>");
            string asked = ""; choice = ImportLeaderboardFile(foreign, open: false, confirm: q => { asked = q; return true; });
            Expect(choice == "file" && asked.Contains("wasn't made in Arcadia Studio") && project.Publishing.LeaderboardPage == "file" && project.Publishing.LeaderboardHtml.Length > 0, "a page from elsewhere is used as it is, after asking");
            project.Publishing.LeaderboardPage = ""; project.Publishing.LeaderboardHtml = [];
            OpenLeaderboard(project.Leaderboards[0]);

            // The canvas as it looks, for a look at the drawing.
            SaveCanvasPicture(output + ".png");

            // Preview: the real page, filled with sample players.
            PreviewLeaderboard();
            var preview = boardPreview ?? throw new Exception("no preview window");
            Expect(await preview.Loaded2.Task.WaitAsync(TimeSpan.FromSeconds(20)), "the preview loads the page");
            string state = "";
            for (int i = 0; i < 100; i++)
            {
                state = JsonSerializer.Deserialize<string>(await preview.Script("JSON.stringify({ sample: !!document.querySelector('.sample'), rows: document.querySelectorAll('.lb_table .list .row').length, podium: document.querySelectorAll('.lb_podium .who div').length })")) ?? "";
                if (state.Contains("\"sample\":true") && !state.Contains("\"rows\":0")) break;
                await Task.Delay(100);
            }
            Expect(state.Contains("\"sample\":true") && !state.Contains("\"rows\":0") && !state.Contains("\"podium\":0"), "the preview shows the page with sample players: " + state);
            preview.Close();

            // The leaderboard creator: the page in a window of its own, on a copy of the project. The project isn't touched
            // until it closes; then the page comes back as one Undo step.
            BackToScreen(this, new RoutedEventArgs());
            var page = project.Leaderboards[0]; int had = page.Elements.Count, steps = history.UndoCount;
            var creator = StartLeaderboardCreator(page, this);
            try
            {
                creator.Show();
                Expect(creator.Title.StartsWith("Leaderboard creator") && creator.ui.IsLeaderboard && creator.ui.Id == "hall_of_fame" && !ReferenceEquals(creator.ui, page) && !ReferenceEquals(creator.project, project), "the leaderboard creator is a window of its own, on a copy of the page: " + creator.Title);
                var menus = creator.Menus.Items.OfType<MenuItem>().Where(m => m.Visibility == Visibility.Visible).Select(m => m.Header as string ?? "").ToList();
                var creatorTags = creator.Toolbox.Items.OfType<ListBoxItem>().Select(i => i.Tag as string).ToList();
                Expect(menus.SequenceEqual(new[] { "_Edit", "_View" }) && creator.Screens.Visibility == Visibility.Collapsed && BoardStamps.All(s => creatorTags.Contains(s.Tag)), "with only the Edit and View menus, no screens list, and the leaderboard toolbox: " + string.Join(",", menus));
                bool refusedSave = false; try { creator.Cmd("file.save").Run(); } catch (InvalidOperationException) { refusedSave = true; }
                bool refusedNew = false; try { creator.Cmd("file.new").Run(); } catch (InvalidOperationException) { refusedNew = true; }
                Expect(refusedSave && refusedNew, "saving and other project commands are refused there");
                creator.AddControl(BoardStamps[0].Tag, 24, 24);
                creator.RenameLeaderboard("champions");
                Expect(creator.ui.Id == "champions" && creator.ui.Elements.Count == had + 1 && creator.Title.Contains("Leaderboard creator"), "the page is edited there like any page");
                SaveWindowPicture(creator, output + ".creator.png");
                Expect(page.Elements.Count == had && project.Leaderboards[0].Id == "hall_of_fame" && history.UndoCount == steps, "and the project isn't touched while the creator is open");
            }
            finally { creator.Close(); }
            string back = FinishLeaderboardCreator(creator, "hall_of_fame");
            Expect(back == "champions" && project.Leaderboards.Count == 3 && project.Leaderboards[0].Id == "champions" && project.Leaderboards[0].Elements.Count == had + 1 && history.UndoCount == steps + 1 && !ui.IsLeaderboard, "closing it puts the page back into the project as one Undo step: " + back);
            history.Undo();
            Expect(project.Leaderboards[0].Id == "hall_of_fame" && project.Leaderboards[0].Elements.Count == had, "and Undo takes the creator's changes back");
            steps = history.UndoCount;
            var untouched = StartLeaderboardCreator(project.Leaderboards[0], this); untouched.Show(); untouched.Close();
            Expect(FinishLeaderboardCreator(untouched, "hall_of_fame") == "hall_of_fame" && history.UndoCount == steps, "a creator closed with nothing changed leaves the project alone");

            // The Publish window picks the project's page by itself, and offers Create, Import, Edit and Remove.
            var dialog = new ArcadiaDialog(this, new ArcadiaClient("http://127.0.0.1:9", WysicraftVersion));
            try
            {
                dialog.Show();
                var (choices, chosen, collected) = dialog.BoardPageState();
                Expect(choices.SequenceEqual(new[] { "standard", "board:hall_of_fame", "board:hall_of_fame1", "board:hall_of_fame2" }) && chosen == "board:hall_of_fame" && collected == "", "the Publish window offers the standard board and every page, and picks the first by itself: " + string.Join(",", choices) + " / " + chosen);
                Expect(dialog.HasBoardButtons(), "with Create leaderboard and Import leaderboard beside it");
                static JsonObject Game(Dictionary<string, byte[]> files) => JsonNode.Parse(Encoding.UTF8.GetString(files["game.json"]))!.AsObject();
                var sent = dialog.FilesForTest(keepLeaderboard: true);
                Expect(sent.ContainsKey("leaderboard.html") && Game(sent).ContainsKey("leaderboardPage"), "a chosen page is sent with the game");

                // Remove: on only while a page is set. A page made here stays in the project; the upload goes without it.
                Expect(dialog.BoardButton("Edit").IsEnabled && dialog.BoardButton("Remove").IsEnabled, "Edit and Remove are on while a page of your own is chosen");
                dialog.Click("Remove");
                var after = dialog.BoardPageState();
                Expect(after.Chosen == "standard" && after.Collected == "standard" && !dialog.BoardButton("Remove").IsEnabled && !dialog.BoardButton("Edit").IsEnabled && project.Leaderboards.Count == 3, "Remove goes back to the standard board, turns itself off, and keeps the page in the project: " + after.Chosen + " / " + after.Collected);
                sent = dialog.FilesForTest(keepLeaderboard: true);
                Expect(!sent.ContainsKey("leaderboard.html") && !sent.Keys.Any(k => k.StartsWith("leaderboard/")) && !Game(sent).ContainsKey("leaderboardPage") && Game(sent)["scores"] is JsonObject, "and the next upload leaves leaderboardPage and the page's file out, keeping the board itself");

                // Create and Edit open the leaderboard creator; closing it comes back here with the page chosen.
                int opened = 0;
                dialog.CreatorForTest = b => { opened++; var c = StartLeaderboardCreator(b, dialog); c.Show(); c.AddControl(BoardStamps[0].Tag, 30, 30); c.Close(); return FinishLeaderboardCreator(c, b.Id); };
                dialog.Click("Create leaderboard…");
                after = dialog.BoardPageState();
                Expect(opened == 1 && project.Leaderboards.Count == 4 && after.Chosen == "board:" + project.Leaderboards[3].Id && project.Leaderboards[3].Elements.Count == 8 && !ui.IsLeaderboard, "Create leaderboard opens the creator on a new page and comes back with it chosen, the main canvas left where it was: " + after.Chosen);
                Expect(dialog.BoardButton("Edit").IsEnabled && dialog.BoardButton("Remove").IsEnabled, "with Edit and Remove on again");
                dialog.BoardButton("Remove").BringIntoView(); await Task.Delay(100); SaveWindowPicture(dialog, output + ".publish.png");
                dialog.Click("Edit");
                Expect(opened == 2 && project.Leaderboards[3].Elements.Count == 9 && dialog.BoardPageState().Chosen == after.Chosen, "Edit opens the creator on the chosen page");
                // With Keep a leaderboard off, nothing about a page is sent, and the game says so: "scores": false.
                sent = dialog.FilesForTest(keepLeaderboard: false);
                Expect(!sent.ContainsKey("leaderboard.html") && !Game(sent).ContainsKey("leaderboardPage") && Game(sent)["scores"] is JsonValue none && none.TryGetValue<bool>(out bool keeps) && !keeps, "Keep a leaderboard off sends \"scores\": false and no page");

                // An imported page: Remove asks, then takes it out of the project.
                project.Publishing.LeaderboardHtml = Encoding.UTF8.GetBytes("<html><body>my own board</body></html>"); project.Publishing.LeaderboardPage = "file"; dialog.Reload();
                Expect(dialog.BoardPageState().Chosen == "file" && dialog.BoardButton("Remove").IsEnabled && !dialog.BoardButton("Edit").IsEnabled, "an imported page can be removed, not edited");
                string removeAsked = ""; dialog.ConfirmForTest = q => { removeAsked = q; return false; };
                dialog.Click("Remove");
                Expect(removeAsked.StartsWith("Remove the imported leaderboard page") && project.Publishing.LeaderboardHtml.Length > 0 && dialog.BoardPageState().Chosen == "file", "Remove asks first, and No keeps the imported page");
                dialog.ConfirmForTest = _ => true; dialog.Click("Remove");
                after = dialog.BoardPageState();
                Expect(project.Publishing.LeaderboardHtml.Length == 0 && project.Publishing.LeaderboardPage == "standard" && after.Chosen == "standard" && !after.Choices.Contains("file") && !dialog.BoardButton("Remove").IsEnabled, "Yes takes the imported page out and goes back to the standard board");
                history.Undo();
                Expect(project.Publishing.LeaderboardHtml.Length > 0, "and Undo brings the imported page back");
                project.Publishing.LeaderboardPage = ""; project.Publishing.LeaderboardHtml = [];
            }
            finally { dialog.CloseForTest(); }

            File.WriteAllText(output, "PASS: leaderboard editor: create a page (own toolbox and bar), every widget added, drawn and in the inspector, what can't go where, Undo on the page, rename, save and open, .lb import, exported page import, a page from elsewhere, preview with sample players, the leaderboard creator (own window on a copy, project commands refused, one Undo step on close, nothing when unchanged), the Publish window's page choice, Create and Edit through the creator, Remove (a designed page kept, an imported page taken out after asking, the upload without leaderboardPage), Keep a leaderboard off as \"scores\": false");
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
            dirty = false;
        }
        _ = gameScreen;
    }
    internal static void SaveWindowPicture(Window window, string path)
    {
        window.UpdateLayout();
        if (window.Content is not FrameworkElement content) return;
        var picture = new RenderTargetBitmap(Math.Max(1, (int)content.ActualWidth), Math.Max(1, (int)content.ActualHeight), 96, 96, PixelFormats.Pbgra32); picture.Render(content);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(picture)); using var file = File.Create(path); png.Save(file);
    }
    void SaveCanvasPicture(string path)
    {
        Draw(); UpdateLayout();
        var picture = new RenderTargetBitmap(Math.Max(1, (int)Surface.ActualWidth), Math.Max(1, (int)Surface.ActualHeight), 96, 96, PixelFormats.Pbgra32); picture.Render(Surface);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(picture)); using var file = File.Create(path); png.Save(file);
    }
    /// <summary>Every heading and label in the inspector, for checking what it shows.</summary>
    List<string> InspectorText()
    {
        var found = new List<string>();
        // The logical tree: folded sections aren't drawn yet, but their controls are there.
        void Walk(object node)
        {
            if (node is TextBlock t && t.Text.Length > 0) found.Add(t.Text);
            if (node is ContentControl { Content: string content }) found.Add(content);
            if (node is HeaderedContentControl { Header: string header }) found.Add(header);
            if (node is DependencyObject d) foreach (var child in LogicalTreeHelper.GetChildren(d)) Walk(child);
        }
        foreach (var child in Properties.Children.OfType<DependencyObject>()) Walk(child);
        return found.Select(s => s.Trim().ToUpperInvariant()).Distinct().ToList();
    }

    sealed partial class ArcadiaDialog
    {
        internal (List<string> Choices, string Chosen, string Collected) BoardPageState() =>
            (boardPage.Items.OfType<ComboBoxItem>().Select(i => (string)i.Tag).ToList(), Chosen(boardPage), Collect().LeaderboardPage);
        internal bool HasBoardButtons()
        {
            var buttons = new List<string>();
            void Walk(DependencyObject node) { if (node is Button { Content: string text }) buttons.Add(text); for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Walk(VisualTreeHelper.GetChild(node, i)); }
            UpdateLayout(); Walk(this);
            return buttons.Contains("Create leaderboard…") && buttons.Contains("Import leaderboard…");
        }
        internal Button BoardButton(string text)
        {
            Button? found = null;
            void Walk(DependencyObject node) { if (node is Button { Content: string t } b && t == text) found ??= b; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Walk(VisualTreeHelper.GetChild(node, i)); }
            UpdateLayout(); Walk(this);
            return found ?? throw new Exception("The Publish window has no " + text + " button");
        }
        internal void Click(string text) => BoardButton(text).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        /// <summary>The files an upload would hold now, with Keep a leaderboard set.</summary>
        internal Dictionary<string, byte[]> FilesForTest(bool keepLeaderboard)
        {
            var s = Collect(); s.Leaderboard = keepLeaderboard;
            return ArcadiaPackage.Files(editor.project, s, WysicraftVersion);
        }
    }
}
