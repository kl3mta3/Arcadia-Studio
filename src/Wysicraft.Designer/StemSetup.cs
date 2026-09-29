using System.Windows;
using System.Windows.Controls;
using Wysicraft.Core.Audio;
namespace Wysicraft.Designer;

/// <summary>The instrument splitter (Demucs) for song conversion: offers its one-time download the first time a song is
/// imported, and splits songs (keeping the last split, so converting the same song again with other options is quick).</summary>
static class StemSetup
{
    static bool offered;
    static (string Key, StemSplitter.Stems Stems)? last;

    /// <summary>Asks once per session to download the splitter, if it isn't there yet. True when it's ready to use.</summary>
    public static async Task<bool> Offer(Window owner)
    {
        if (StemSplitter.Available) return true;
        if (offered) return false;
        offered = true;
        return await ShowDownload(owner);
    }

    /// <summary>The download window: explains, downloads with progress, and can be cancelled.</summary>
    public static Task<bool> ShowDownload(Window owner)
    {
        var window = new Window { Owner = owner, Title = "Split instruments — one-time download", SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SetResourceReference(FrameworkElement.StyleProperty, typeof(Window));
        var panel = new StackPanel { Margin = new Thickness(16), Width = 420 }; window.Content = panel;
        panel.Children.Add(new TextBlock { Text = "Songs convert much better split into parts first", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6), Text = "Before converting, Arcadia Studio can split a song into vocals, bass, drums and the other instruments with Demucs (by Meta, MIT licence). The singing then becomes the melody, the bass guitar the bass line and the guitars the chords." });
        panel.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 10), Text = $"This needs a one-time download of {StemSplitter.ModelBytes / 1_000_000.0:0} MB from Hugging Face, kept in your app data folder so the installer stays small. Songs can still be converted without it." });
        var bar = new ProgressBar { Height = 14, Maximum = 1, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 4) }; panel.Children.Add(bar);
        var status = new TextBlock { Opacity = 0.75, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 6) }; panel.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) }; panel.Children.Add(buttons);
        var download = new Button { Content = "Download", IsDefault = true, Padding = new Thickness(12, 3, 12, 3) };
        var later = new Button { Content = "Not now", IsCancel = true, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(12, 3, 12, 3) };
        buttons.Children.Add(download); buttons.Children.Add(later);
        CancellationTokenSource? cancel = null; bool done = false;
        later.Click += (_, _) => { if (cancel != null) cancel.Cancel(); else window.Close(); };
        window.Closing += (_, e) => { if (cancel != null && !done) { cancel.Cancel(); } };
        download.Click += async (_, _) =>
        {
            download.IsEnabled = false; later.Content = "Cancel"; bar.Visibility = status.Visibility = Visibility.Visible;
            cancel = new CancellationTokenSource();
            var progress = new Progress<double>(p => { bar.Value = p; status.Text = $"Downloading… {p * StemSplitter.ModelBytes / 1_000_000:0} of {StemSplitter.ModelBytes / 1_000_000.0:0} MB"; });
            try { await StemSplitter.Download(progress, cancel.Token); done = true; window.Close(); }
            catch (OperationCanceledException) { window.Close(); }
            catch (Exception ex) { status.Text = "The download failed: " + ex.Message; download.IsEnabled = true; download.Content = "Try again"; later.Content = "Not now"; cancel = null; }
        };
        window.ShowDialog();
        return Task.FromResult(StemSplitter.Available);
    }

    /// <summary>The song split into parts (reusing the last split of the same audio).</summary>
    public static async Task<StemSplitter.Stems> Split(float[][] channels, int rate, string name, Action<string> notice)
    {
        string key = $"{name}|{rate}|{channels.Length}|{channels[0].Length}";
        if (last is { } cached && cached.Key == key) return cached.Stems;
        var owner = Application.Current.Dispatcher;
        var stems = await Task.Run(() => StemSplitter.Separate(channels, rate, p => owner.BeginInvoke(() => notice($"Splitting {name} into vocals, bass, drums and other… {p * 100:0}%"))));
        last = (key, stems);
        return stems;
    }
}
