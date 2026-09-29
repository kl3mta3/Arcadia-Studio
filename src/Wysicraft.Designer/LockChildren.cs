using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Wysicraft.Core;
using Wysicraft.Models;
namespace Wysicraft.Designer;

// Locking or unlocking a panel can do the same to everything attached inside it. Wysicraft asks each time,
// unless "Don't ask me again" was ticked; the answer is then remembered for this Windows user.
// Children can still be locked or unlocked on their own afterwards.
public partial class MainWindow
{
    static string PreferencesPath => Wysicraft.Core.AppFolders.Path("preferences.json");
    sealed class Preferences
    {
        public string LockChildren { get; set; } = "ask"; // ask, always, never
        public bool AdvancedOpen { get; set; }
        public bool AdvancedNoteSeen { get; set; }
        // The MCP server keeps the same address between launches, so a config given to an assistant once keeps
        // working. The token is DPAPI-protected: only this Windows account can read it back.
        public int McpPort { get; set; } = 4730;
        public string McpTokenProtected { get; set; } = "";
        // The assistant CLI the editor may start: one of the known tools, or a custom command line with {prompt}
        // and {mcpConfig} filled in. Empty means none is set up.
        public string AssistantTool { get; set; } = "";
        public string AssistantCustomCommand { get; set; } = "";
        public string AssistantCustomArguments { get; set; } = "";
        public string AssistantCustomChatArguments { get; set; } = "";
        public bool AssistantAutoConnect { get; set; }
        // Properties sections that were collapsed, by heading text. Anything not listed is open.
        public List<string> CollapsedSections { get; set; } = [];
        // Publishing to Arcadia: the arcade's address (empty: the default one), the key for each arcade host
        // (DPAPI-protected, like the MCP token), and each project's game there ("host|projectId" → game ID) as a backup
        // for a project that wasn't saved after its first publish.
        public string ArcadeUrl { get; set; } = "";
        public Dictionary<string, string> ArcadiaKeys { get; set; } = [];
        public Dictionary<string, string> ArcadiaGames { get; set; } = [];
    }
    Preferences? preferences;
    internal string? lockChildrenAnswer; // tests set "always"/"never" so no dialog appears
    Preferences Prefs()
    {
        if (preferences != null) return preferences;
        try { preferences = File.Exists(PreferencesPath) ? JsonSerializer.Deserialize<Preferences>(File.ReadAllText(PreferencesPath)) : null; } catch { preferences = null; }
        return preferences ??= new();
    }
    void SavePrefs()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!); File.WriteAllText(PreferencesPath + ".tmp", JsonSerializer.Serialize(Prefs())); File.Move(PreferencesPath + ".tmp", PreferencesPath, true); }
        catch (Exception ex) { Log("Preferences could not be saved: " + ex.Message); }
    }
    void ResetDontAskAgain() { Prefs().LockChildren = "ask"; SavePrefs(); Log("Arcadia Studio will ask again before locking or unlocking items inside panels."); }

    /// <summary>Locks or unlocks the targets, and (after asking) everything attached inside them that doesn't match yet.</summary>
    void SetLocked(IReadOnlyCollection<Element> targets, bool locking)
    {
        var ids = targets.Select(t => t.Id).ToHashSet();
        var inside = ContainerTree.Moving(ui, ids).Where(e => !ids.Contains(e.Id) && e.Locked != locking).ToList();
        foreach (var t in targets) t.Locked = locking;
        if (inside.Count > 0 && AskApplyToChildren(targets, inside.Count, locking)) foreach (var e in inside) e.Locked = locking;
        if (locking) selected.ExceptWith(targets.Concat(inside.Where(e => e.Locked)).Select(e => e.Id));
    }

    bool AskApplyToChildren(IReadOnlyCollection<Element> targets, int count, bool locking)
    {
        string answer = lockChildrenAnswer ?? Prefs().LockChildren;
        if (answer == "always") return true;
        if (answer == "never") return false;
        string verb = locking ? "lock" : "unlock", what = targets.Count == 1 ? targets.First().Id : $"these {targets.Count} items";
        var window = new Window { Owner = this, Title = locking ? "Lock items inside?" : "Unlock items inside?", SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        var panel = new StackPanel { Margin = new Thickness(16), MaxWidth = 420 }; window.Content = panel;
        panel.Children.Add(new TextBlock { Text = $"Also {verb} the {count} item{(count == 1 ? "" : "s")} inside {what}?", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(new TextBlock { Text = "You can still lock or unlock them one by one afterwards.", TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 10) });
        var remember = new CheckBox { Content = "Don't ask me again", Margin = new Thickness(0, 0, 0, 12) }; panel.Children.Add(remember);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(buttons);
        bool result = false;
        var yes = new Button { Content = $"Yes, {verb} them too", IsDefault = true, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 3, 10, 3) };
        var no = new Button { Content = "No, just this", IsCancel = true, Padding = new Thickness(10, 3, 10, 3) };
        yes.Click += (_, _) => { result = true; window.Close(); }; no.Click += (_, _) => window.Close();
        buttons.Children.Add(yes); buttons.Children.Add(no);
        window.ShowDialog();
        if (remember.IsChecked == true) { Prefs().LockChildren = result ? "always" : "never"; SavePrefs(); Log("Remembered: " + (result ? "items inside panels follow their panel's lock." : "items inside panels keep their own lock.") + " View → Ask again before locking items inside panels resets this."); }
        return result;
    }

    internal void VerifyLockChildren()
    {
        project = new Project(); ui = project.Screens[0]; history.Clear(); selected.Clear();
        ui.Elements = [new Element { Id = "panel1", Type = "panel" }, new Element { Id = "label1", Type = "label", Parent = "panel1" }, new Element { Id = "inner1", Type = "panel", Parent = "panel1" }, new Element { Id = "deep1", Type = "label", Parent = "inner1" }, new Element { Id = "other1" }];
        Element E(string id) => ui.Elements.Single(e => e.Id == id);
        lockChildrenAnswer = "always"; SetLocked([E("panel1")], true);
        if (!E("label1").Locked || !E("deep1").Locked || E("other1").Locked) throw new Exception("Locking a panel did not lock everything inside it");
        E("label1").Locked = false; // children can still be changed on their own
        SetLocked([E("panel1")], false);
        if (E("panel1").Locked || E("inner1").Locked || E("deep1").Locked) throw new Exception("Unlocking a panel did not unlock everything inside it");
        lockChildrenAnswer = "never"; SetLocked([E("panel1")], true);
        if (!E("panel1").Locked || E("label1").Locked) throw new Exception("\"No\" still changed the items inside");
        lockChildrenAnswer = null;
    }
}
