using System.IO;
using System.Text.Json;
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
// sample players, and the Publish window's choice. The loaded project file is never written.
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

            // The Publish window picks the project's page by itself, and offers Create and Import.
            var dialog = new ArcadiaDialog(this, new ArcadiaClient("http://127.0.0.1:9", WysicraftVersion));
            try
            {
                dialog.Show();
                var (choices, chosen, collected) = dialog.BoardPageState();
                Expect(choices.SequenceEqual(new[] { "standard", "board:hall_of_fame", "board:hall_of_fame1", "board:hall_of_fame2" }) && chosen == "board:hall_of_fame" && collected == "", "the Publish window offers the standard board and every page, and picks the first by itself: " + string.Join(",", choices) + " / " + chosen);
                Expect(dialog.HasBoardButtons(), "with Create leaderboard and Import leaderboard beside it");
            }
            finally { dialog.CloseForTest(); }

            File.WriteAllText(output, "PASS: leaderboard editor: create a page (own toolbox and bar), every widget added, drawn and in the inspector, what can't go where, Undo on the page, rename, save and open, .lb import, exported page import, a page from elsewhere, preview with sample players, the Publish window's page choice");
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
            dirty = false;
        }
        _ = gameScreen;
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
    }
}
