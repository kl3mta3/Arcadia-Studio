using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wysicraft.Core;
namespace Wysicraft.Designer;

// Before anything goes to Minecraft: list what Minecraft can't run (web and desktop tools, sizes past its limits),
// where each one is, and let a click jump there. Shows 50 at first; more than that means this is really a
// web/desktop project.
public partial class MainWindow
{
    const int CompatibilityShown = 50;

    /// <summary>True when the project can go to Minecraft. Otherwise shows what's in the way and returns false.</summary>
    bool MinecraftExportAllowed(string action)
    {
        var uses = Compatibility.MinecraftProblems(project);
        if (uses.Count == 0) return true;
        var window = new Window { Owner = this, Title = "Not ready for Minecraft", Width = 620, Height = 520, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        var layout = new DockPanel { Margin = new Thickness(16) }; window.Content = layout;
        var intro = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        intro.Text = uses.Count > CompatibilityShown
            ? $"{action} isn't possible: this project uses {uses.Count} things Minecraft can't run. With more than {CompatibilityShown}, it looks like a web and desktop project: set Project settings → Made for to \"Web & desktop\" and export it as a web page, Windows app or Electron app instead."
            : $"{action} isn't possible: this project uses {uses.Count} thing{(uses.Count == 1 ? "" : "s")} Minecraft can't run. Click one to go to it. They all work in web page, Windows app and Electron exports.";
        DockPanel.SetDock(intro, Dock.Top); layout.Children.Add(intro);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) }; DockPanel.SetDock(buttons, Dock.Bottom); layout.Children.Add(buttons);
        var list = new ListBox(); layout.Children.Add(list);
        void Fill(bool all)
        {
            list.Items.Clear();
            foreach (var use in all ? uses : uses.Take(CompatibilityShown)) list.Items.Add(new ListBoxItem { Content = Compatibility.Describe(use), Tag = use, Cursor = Cursors.Hand });
            if (!all && uses.Count > CompatibilityShown) list.Items.Add(new ListBoxItem { Content = $"…and {uses.Count - CompatibilityShown} more", IsEnabled = false, Foreground = Brushes.Gray });
        }
        Fill(false);
        list.MouseDoubleClick += (_, _) => { }; // single click is enough
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is ListBoxItem { Tag: Compatibility.Use use }) GoTo(use.Screen, use.Element); };
        if (uses.Count > CompatibilityShown) { var all = new Button { Content = $"Show all {uses.Count}", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 3, 10, 3) }; all.Click += (_, _) => { Fill(true); all.IsEnabled = false; }; buttons.Children.Add(all); }
        var close = new Button { Content = "Close", IsCancel = true, IsDefault = true, Padding = new Thickness(10, 3, 10, 3) }; close.Click += (_, _) => window.Close(); buttons.Children.Add(close);
        window.Show(); // not modal, so you can fix things while the list stays open
        return false;
    }
    void GoTo(string screen, string element)
    {
        if (screen.Length > 0 && project.Screens.FirstOrDefault(s => s.Id == screen) is { } target && target != ui) ShowScreen(target);
        if (element.Length > 0 && ui.Elements.Any(e => e.Id == element)) { selected.Clear(); selected.Add(element); Draw(); RefreshInspector(); RefreshLayers(); }
        else if (screen.Length > 0) ShowScreenSettings();
        else Settings();
    }
}
