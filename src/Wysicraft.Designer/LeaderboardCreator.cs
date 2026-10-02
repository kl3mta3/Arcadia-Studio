using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wysicraft.Models;
namespace Wysicraft.Designer;

// The leaderboard creator: the editor in a window of its own, for one leaderboard page. Publish to Arcadia opens it for
// "Create leaderboard…" and "Edit", so the page is designed beside the publish window instead of on the main canvas
// behind it. It works on a copy of the project (its own Undo, nothing shared while it's open); closing it puts the page,
// and any pictures or fonts added for it, back into the project as one Undo step.
//
// It is the same editor (canvas, Toolbox with the leaderboard widgets, Layers, Properties, Preview with sample players),
// without what belongs to the whole project: no File, Project or Advanced menus, no screens list, no MCP, no recovery
// drafts, and commands outside editing and viewing are refused.
public partial class MainWindow
{
    readonly bool creatorMode;
    int creatorBoardIndex;

    static bool CreatorAllows(string id) => id.StartsWith("edit.", StringComparison.Ordinal) || id.StartsWith("view.", StringComparison.Ordinal)
        || id is "project.preview" or "project.screen" or "project.importTexture" or "project.pixelEditor" or "help.manual";
    /// <summary>A command as the leaderboard creator runs it: editing, viewing, Preview and pictures; nothing else.</summary>
    Action CreatorCommand(string id, Action run) => !creatorMode || CreatorAllows(id) ? run
        : () => throw new InvalidOperationException("That isn't part of the leaderboard creator. Close this window to get back to your project.");

    /// <summary>Opens the leaderboard creator on a page and waits for it to be closed. Returns the page's ID afterwards
    /// (it can be renamed in the creator).</summary>
    internal string OpenLeaderboardCreator(UiDefinition board, Window owner)
    {
        string id = board.Id;
        var creator = StartLeaderboardCreator(board, owner);
        creator.ShowDialog();
        return FinishLeaderboardCreator(creator, id);
    }

    /// <summary>The creator window, ready to show, on a copy of the project.</summary>
    internal MainWindow StartLeaderboardCreator(UiDefinition board, Window owner)
    {
        SaveScriptText();
        int index = project.Leaderboards.IndexOf(board);
        if (index < 0) throw new InvalidOperationException("That leaderboard page isn't in the project any more.");
        var creator = new MainWindow(true) { Owner = owner, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, WindowState = WindowState.Normal };
        // Over the editor, a little inside it, so it's plainly a window of its own.
        var over = WindowState == WindowState.Maximized || ActualWidth < 200 ? SystemParameters.WorkArea : new Rect(Left, Top, ActualWidth, ActualHeight);
        creator.Width = Math.Max(960, over.Width - 60); creator.Height = Math.Max(620, over.Height - 60);
        creator.Left = over.Left + (over.Width - creator.Width) / 2; creator.Top = over.Top + (over.Height - creator.Height) / 2;
        creator.LoadCreator(Json.CloneProject(project), index);
        return creator;
    }
    void LoadCreator(Project copy, int index)
    {
        project = copy; creatorBoardIndex = index;
        ShowScreen(project.Leaderboards[index]);
        RefreshAll();
        dirty = false;
        // Only what a leaderboard page needs: no project menus, screens list, MCP or other toolbar buttons.
        foreach (var menu in Menus.Items.OfType<MenuItem>()) if (menu.Header as string is not ("_Edit" or "_View")) menu.Visibility = Visibility.Collapsed;
        foreach (UIElement child in Toolbar.Children) child.Visibility = Visibility.Collapsed;
        foreach (var (button, command, _) in toolbarButtons) if (command == "project.preview") button.Visibility = Visibility.Visible;
        if (zoomLabel != null) zoomLabel.Visibility = Visibility.Visible;
        var done = new Button { Content = "✔  Done", Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(8, 0, 0, 0), Background = AccentBrush, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, ToolTip = "Close the leaderboard creator. The page goes back into your project." };
        done.Click += (_, _) => Close();
        Toolbar.Children.Insert(1, done);
        Screens.Visibility = AddScreen.Visibility = DeleteScreenButton.Visibility = Visibility.Collapsed;
        foreach (var back in ComponentSourceBar.Children.OfType<Button>().Where(b => b.Content as string == "Back to screen")) back.Visibility = Visibility.Collapsed;
        Log("Leaderboard creator: design the page here. Preview (F5) shows it with sample players. Done, or closing this window, puts it back into your project.");
    }
    /// <summary>Whether the creator changed anything (for the project it came from).</summary>
    internal bool CreatorChanged => dirty;

    /// <summary>After the creator closed: its page replaces the project's (one Undo step), with any pictures and fonts
    /// added for it. Nothing happens when nothing was changed. Returns the page's ID.</summary>
    internal string FinishLeaderboardCreator(MainWindow creator, string originalId)
    {
        var edited = creator.creatorBoardIndex < creator.project.Leaderboards.Count ? creator.project.Leaderboards[creator.creatorBoardIndex] : null;
        if (edited == null || !creator.CreatorChanged) return originalId;
        var page = Json.Clone(edited);
        // A new name that another page already has is not taken.
        if (page.Id != originalId && project.Leaderboards.Any(b => b.Id == page.Id)) page.Id = originalId;
        bool showing = ui.IsLeaderboard && ui.Id == originalId;
        Change();
        int at = project.Leaderboards.FindIndex(b => b.Id == originalId);
        if (at >= 0) project.Leaderboards[at] = page; else project.Leaderboards.Add(page);
        // Pictures and fonts brought in while designing it. (Ones removed there stay: other screens may use them.)
        int added = 0;
        foreach (var (path, bytes) in creator.project.Assets)
            if (!project.Assets.TryGetValue(path, out var had) || !ReferenceEquals(had, bytes)) { project.Assets[path] = bytes; added++; }
        if (page.Id != originalId && project.Publishing.LeaderboardPage == "board:" + originalId) project.Publishing.LeaderboardPage = "board:" + page.Id;
        if (showing) ui = page;
        RefreshAll(); RefreshAssetBrowser();
        Log($"Leaderboard page {page.Id} updated from the leaderboard creator{(added > 0 ? $" ({added} picture or font file{(added == 1 ? "" : "s")} added)" : "")}. Undo takes it back.");
        return page.Id;
    }

    /// <summary>Takes the imported leaderboard page (the one used as it is) out of the project: the game uses the arcade's
    /// standard board again.</summary>
    internal void RemoveImportedLeaderboard()
    {
        if (project.Publishing.LeaderboardHtml.Length == 0) return;
        Change();
        project.Publishing.LeaderboardHtml = [];
        if (project.Publishing.LeaderboardPage == "file") project.Publishing.LeaderboardPage = "standard";
        RefreshInspector();
        Log("Removed the imported leaderboard page. The game uses the arcade's standard board from the next publish.");
    }
}
