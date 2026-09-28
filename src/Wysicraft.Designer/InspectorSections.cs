using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Wysicraft.Designer;

// The Properties panel folded into sections. Each heading becomes a collapsible section and the panel remembers
// which ones were closed, so a long inspector can be kept to the parts being worked on.
public partial class MainWindow
{
    /// <summary>Marks a heading TextBlock so the fold pass can find it; the text is the section's name.</summary>
    sealed record SectionHeading(string Text);

    /// <summary>Everything in Properties, with sections opened up: what a test walks instead of Properties.Children,
    /// because after folding the fields sit inside Expanders rather than directly in the panel.</summary>
    internal IEnumerable<UIElement> InspectorItems()
    {
        foreach (UIElement child in Properties.Children)
        {
            if (child is Expander { Content: StackPanel body })
            {
                if (body.Tag is UIElement heading) yield return heading;
                foreach (UIElement item in body.Children) yield return item;
            }
            else yield return child;
        }
    }

    /// <summary>Folds the flat list the builders produced into one Expander per heading. Whatever sits before the
    /// first heading (Ask Agent) stays where it is. A section that was closed last time opens closed again.</summary>
    void FoldSections()
    {
        var items = Properties.Children.Cast<UIElement>().ToList();
        if (!items.Any(i => i is TextBlock { Tag: SectionHeading })) return;
        Properties.Children.Clear();
        var collapsed = Prefs().CollapsedSections;
        Expander? open = null; StackPanel? body = null;
        foreach (var item in items)
        {
            if (item is TextBlock { Tag: SectionHeading heading } title)
            {
                body = new StackPanel { Margin = new Thickness(0, 0, 0, 2), Tag = title };
                title.Margin = new Thickness(0);
                open = new Expander { Header = title, Content = body, IsExpanded = !collapsed.Contains(heading.Text), Margin = new Thickness(0, 4, 0, 0) };
                open.Expanded += (_, _) => { if (collapsed.Remove(heading.Text)) SavePrefs(); };
                open.Collapsed += (_, _) => { if (!collapsed.Contains(heading.Text)) { collapsed.Add(heading.Text); SavePrefs(); } };
                Properties.Children.Add(open);
            }
            else if (body != null) body.Children.Add(item);
            else Properties.Children.Add(item);
        }
    }

    /// <summary>Opens a section by name, for a link that points at one ("Screen settings…").</summary>
    void OpenSection(string text)
    {
        foreach (var expander in Properties.Children.OfType<Expander>())
            if (expander.Header is TextBlock { Tag: SectionHeading h } && h.Text == text) { expander.IsExpanded = true; expander.BringIntoView(); }
    }

    /// <summary>A card is a small bordered box with a title row and a body: what an attached component looks like.
    /// Its body folds like a section and is remembered under the card's own key.</summary>
    StackPanel Card(Panel into, string key, string title, string? tip, Action? remove, string? removeTip = null)
    {
        var border = new Border { Background = Brush("#2A2E36"), BorderBrush = Brush("#3A4250"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 4, 0, 4), Padding = new Thickness(6, 4, 6, 6), Tag = "card:" + key };
        var layout = new StackPanel(); border.Child = layout;
        var head = new DockPanel { ToolTip = tip };
        var collapsed = Prefs().CollapsedSections; string prefKey = "card:" + key;
        var toggle = new Button { Style = (Style)FindResource("IconButton"), Content = collapsed.Contains(prefKey) ? "▸" : "▾", Width = 20, ToolTip = "Show or hide this component's settings" };
        DockPanel.SetDock(toggle, Dock.Left); head.Children.Add(toggle);
        if (remove != null)
        {
            var x = new Button { Style = (Style)FindResource("IconButton"), Content = "✕", Width = 22, ToolTip = removeTip ?? "Remove this component", Foreground = Brush("#C88A8A") };
            x.Click += (_, _) => Guard(remove);
            DockPanel.SetDock(x, Dock.Right); head.Children.Add(x);
        }
        head.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.LightSkyBlue });
        layout.Children.Add(head);
        var body = new StackPanel { Margin = new Thickness(0, 4, 0, 0), Visibility = collapsed.Contains(prefKey) ? Visibility.Collapsed : Visibility.Visible };
        toggle.Click += (_, _) =>
        {
            bool hide = body.Visibility == Visibility.Visible;
            body.Visibility = hide ? Visibility.Collapsed : Visibility.Visible; toggle.Content = hide ? "▸" : "▾";
            if (hide) { if (!collapsed.Contains(prefKey)) collapsed.Add(prefKey); } else collapsed.Remove(prefKey);
            SavePrefs();
        };
        layout.Children.Add(body); into.Children.Add(border);
        return body;
    }
}
