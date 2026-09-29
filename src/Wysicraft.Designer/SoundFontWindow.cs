using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Wysicraft.Core.Audio;
namespace Wysicraft.Designer;

/// <summary>The SoundFont library: each font in the catalog with what it sounds like, its size and licence, to download
/// (once, kept for every project) or remove; imported .sf2 files; and "Use" to play the current song with one.</summary>
static class SoundFontWindow
{
    /// <summary>Shows the library. Returns the font the user chose to use, if any.</summary>
    public static string? Show(Window owner, string? current)
    {
        string? chosen = null;
        var window = new Window { Owner = owner, Title = "SoundFonts", Width = 620, Height = 640, MinHeight = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.CanResize };
        window.SetResourceReference(FrameworkElement.StyleProperty, typeof(Window));
        var root = new DockPanel { Margin = new Thickness(14) }; window.Content = root;
        var intro = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), Text = "SoundFonts are banks of recorded instruments. Play a song with one instead of the chip sounds, and every layer picks from its instruments (pianos, guitars, strings, drum kits…). Downloaded and imported fonts are kept on this computer and shared by all your projects, so each is only downloaded once." };
        DockPanel.SetDock(intro, System.Windows.Controls.Dock.Top); root.Children.Add(intro);
        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) }; DockPanel.SetDock(bottom, System.Windows.Controls.Dock.Bottom); root.Children.Add(bottom);
        var import = new Button { Content = "Import custom soundfont (.sf2)…", Padding = new Thickness(10, 3, 10, 3), ToolTip = "Any SoundFont 2 file: it's copied into your fonts, so the original can be moved or deleted." };
        var folder = new Button { Content = "Open folder", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
        var close = new Button { Content = "Close", IsCancel = true, Padding = new Thickness(12, 3, 12, 3) };
        DockPanel.SetDock(close, System.Windows.Controls.Dock.Right); bottom.Children.Add(close);
        var left = new StackPanel { Orientation = Orientation.Horizontal }; left.Children.Add(import); left.Children.Add(folder); bottom.Children.Add(left);
        var list = new StackPanel(); root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var busy = new Dictionary<string, CancellationTokenSource>();
        close.Click += (_, _) => window.Close();
        window.Closing += (_, _) => { foreach (var c in busy.Values) c.Cancel(); };
        folder.Click += (_, _) => { Directory.CreateDirectory(SoundFonts.UserFolder); Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SoundFonts.UserFolder}\"") { UseShellExecute = true }); };
        import.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "SoundFont 2|*.sf2", Title = "Import a SoundFont" };
            if (dialog.ShowDialog(window) != true) return;
            try { var name = SoundFonts.Import(dialog.FileName); Fill(); MessageBox.Show(window, $"{name} is ready to use.", "SoundFonts"); }
            catch (Exception ex) { MessageBox.Show(window, ex.Message, "SoundFonts", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };

        void Fill()
        {
            list.Children.Clear();
            var installed = SoundFonts.Installed();
            foreach (var entry in SoundFonts.Catalog) list.Children.Add(Card(entry.Name, entry, installed.Contains(entry.Name, StringComparer.OrdinalIgnoreCase)));
            var own = installed.Where(n => !SoundFonts.IsCatalog(n)).ToList();
            if (own.Count > 0) list.Children.Add(new TextBlock { Text = "Imported", FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 10, 0, 4) });
            foreach (var name in own) list.Children.Add(Card(name, null, true));
        }
        UIElement Card(string name, SoundFonts.Entry? entry, bool ready)
        {
            var card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 8) };
            card.SetResourceReference(Border.BorderBrushProperty, SystemColors.ActiveBorderBrushKey);
            var stack = new StackPanel(); card.Child = stack;
            var head = new DockPanel();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(buttons, System.Windows.Controls.Dock.Right); head.Children.Add(buttons);
            string size = entry != null ? $"{entry.Bytes / 1_000_000.0:0} MB" : SoundFonts.PathOf(name) is { } p ? $"{new FileInfo(p).Length / 1_000_000.0:0} MB" : "";
            string state = SoundFonts.IsBundled(name) ? "included with Arcadia Studio" : ready ? (entry != null ? "downloaded" : "imported") : "not downloaded";
            var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            title.Inlines.Add(new System.Windows.Documents.Run(name) { FontWeight = FontWeights.SemiBold, FontSize = 14 });
            title.Inlines.Add(new System.Windows.Documents.Run($"   {size} · {state}{(string.Equals(name, current, StringComparison.OrdinalIgnoreCase) ? " · this song uses it" : "")}") { Foreground = Brushes.Gray });
            head.Children.Add(title); stack.Children.Add(head);
            if (entry != null)
            {
                stack.Children.Add(new TextBlock { Text = entry.Description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 2) });
                stack.Children.Add(new TextBlock { Text = "Licence: " + entry.License, TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.7 });
            }
            var bar = new ProgressBar { Height = 10, Maximum = 1, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed }; stack.Children.Add(bar);
            var status = new TextBlock { FontSize = 11, Opacity = 0.75, Visibility = Visibility.Collapsed }; stack.Children.Add(status);
            Button B(string text, string tip = "") { var b = new Button { Content = text, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0), ToolTip = tip.Length > 0 ? tip : null }; buttons.Children.Add(b); return b; }
            if (ready)
            {
                B("Use", "Play the song with this font").Click += (_, _) => { chosen = name; window.Close(); };
                if (!SoundFonts.IsBundled(name))
                    B("Remove", "Delete it from this computer (it can be downloaded or imported again)").Click += (_, _) =>
                    {
                        if (MessageBox.Show(window, $"Remove {name} from this computer?", "SoundFonts", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                        try { SoundFonts.Remove(name); Fill(); } catch (Exception ex) { MessageBox.Show(window, ex.Message, "SoundFonts"); }
                    };
            }
            else if (entry != null)
            {
                var download = B("Download");
                download.Click += async (_, _) =>
                {
                    if (busy.TryGetValue(name, out var running)) { running.Cancel(); return; }
                    var cancel = new CancellationTokenSource(); busy[name] = cancel;
                    download.Content = "Cancel"; bar.Visibility = status.Visibility = Visibility.Visible;
                    var progress = new Progress<double>(v => { bar.Value = v; status.Text = $"Downloading… {v * entry.Bytes / 1_000_000:0} of {entry.Bytes / 1_000_000.0:0} MB"; });
                    try { await SoundFonts.Download(entry, progress, cancel.Token); busy.Remove(name); if (window.IsVisible) Fill(); }
                    catch (OperationCanceledException) { busy.Remove(name); if (window.IsVisible) Fill(); }
                    catch (Exception ex) { busy.Remove(name); status.Text = "The download failed: " + ex.Message; download.Content = "Try again"; }
                };
            }
            return card;
        }
        Fill();
        window.ShowDialog();
        return chosen;
    }
}
