using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;
using Wysicraft.Packaging;
namespace Wysicraft.Designer;

// Updates from GitHub Releases (kl3mta3/arcadia-studio): a check on startup (at most once a day, and never a popup when
// offline) or from Help → Check for updates…, then a popup that asks first. An installed copy updates with the
// release's installer; a portable copy with its ZIP, which the new version copies over this folder once this copy has
// closed. Nothing is downloaded or run without the person's click, and nothing is used unless its SHA-256 matches.
public partial class MainWindow
{
    internal const string InstallerAppId = "{2EDF00FA-8E24-489C-BCF0-C35C37A88CB1}";
    // Self-checks point these at a fake GitHub and catch the launch instead of running an installer.
    internal static string UpdateApi = "https://api.github.com/";
    internal static bool? InstalledForTest;
    internal static Action<string, string>? LaunchForTest;
    bool closingForUpdate;
    internal bool UpdateHandedOver => closingForUpdate;

    /// <summary>The folder holding Designer, Runtime and Docs.</summary>
    static string ReleaseRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
    /// <summary>Whether this copy is the one the installer put in place (else it's a portable copy).</summary>
    static bool IsInstalledCopy()
    {
        if (InstalledForTest is bool test) return test;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + InstallerAppId + "_is1");
            if (key?.GetValue("InstallLocation") is not string location || location.Length == 0) return false;
            return ReleaseRoot.TrimEnd('\\').Equals(Path.GetFullPath(location).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { return false; }
    }

    internal void CheckForUpdatesOnStartup()
    {
        // Downloads from an earlier update aren't needed once it's done (unless this copy is running from one).
        try { string updates = Wysicraft.Core.AppFolders.Path("Updates"); if (Directory.Exists(updates) && !ReleaseRoot.StartsWith(Path.GetFullPath(updates), StringComparison.OrdinalIgnoreCase)) Directory.Delete(updates, true); } catch (Exception) { }
        var prefs = Prefs();
        if (!prefs.AutoCheckUpdates) return;
        if (DateTime.TryParse(prefs.LastUpdateCheck, null, System.Globalization.DateTimeStyles.RoundtripKind, out var last) && DateTime.UtcNow - last < TimeSpan.FromHours(20)) return;
        _ = CheckForUpdatesAsync(quiet: true);
    }
    void CheckForUpdatesNow() => _ = CheckForUpdatesAsync(quiet: false);
    void ToggleAutoUpdates() { Prefs().AutoCheckUpdates = !Prefs().AutoCheckUpdates; SavePrefs(); Log(Prefs().AutoCheckUpdates ? "Arcadia Studio will check for updates when it starts (at most once a day)." : "Arcadia Studio won't check for updates by itself. Help → Check for updates… still does."); }

    internal async Task<UpdateInfo?> CheckForUpdatesAsync(bool quiet)
    {
        UpdateInfo? info;
        try
        {
            info = await new UpdateCheck(WysicraftVersion, null, UpdateApi).LatestAsync();
            Prefs().LastUpdateCheck = DateTime.UtcNow.ToString("o"); SavePrefs();
        }
        catch (Exception ex) when (ex is HttpRequestException or UpdateException or TaskCanceledException or System.Text.Json.JsonException)
        {
            if (!quiet) MessageBox.Show(this, "Couldn't check for updates: " + ex.Message, "Arcadia Studio");
            return null;
        }
        if (info == null) { if (!quiet) MessageBox.Show(this, "Arcadia Studio " + WysicraftVersion + " is the newest version.", "Arcadia Studio"); return null; }
        if (quiet && Prefs().SkippedUpdate == info.Tag) return info;
        new UpdateWindow(this, info, IsInstalledCopy()).Show();
        return info;
    }

    /// <summary>The popup: what's new, and Update now / Later / Skip this version.</summary>
    internal sealed class UpdateWindow : Window
    {
        readonly MainWindow editor; readonly UpdateInfo info; readonly bool installed;
        readonly ProgressBar progress = new() { Height = 6, Minimum = 0, Maximum = 1, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
        readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(0, 6, 0, 0) };
        internal readonly Button Update = new() { Content = "Update now", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 4, 14, 4) };
        internal readonly Button Later = new() { Content = "Later", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 4, 14, 4) };
        internal readonly Button Skip = new() { Content = "Skip this version", Padding = new Thickness(14, 4, 14, 4) };
        internal string Status => status.Text;
        bool busy;
        public UpdateWindow(MainWindow editor, UpdateInfo info, bool installed)
        {
            this.editor = editor; this.info = info; this.installed = installed; Owner = editor;
            SetResourceReference(StyleProperty, typeof(Window));
            Title = "Update available"; Width = 560; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var panel = new StackPanel { Margin = new Thickness(18) }; Content = panel;
            panel.Children.Add(new TextBlock { Text = "Arcadia Studio " + info.Version.ToString(3) + " is available", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            panel.Children.Add(new TextBlock { Text = "You have " + WysicraftVersion + ".", Foreground = Brushes.LightGray, Margin = new Thickness(0, 2, 0, 10) });
            if (info.Notes.Trim().Length > 0)
                panel.Children.Add(new TextBox { Text = info.Notes.Trim(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 220, MinHeight = 60 });
            var page = new TextBlock { Margin = new Thickness(0, 6, 0, 0) }; var link = new Hyperlink(new Run("Release page")); link.Click += (_, _) => OpenInBrowser(info.Page); page.Inlines.Add(link); panel.Children.Add(page);
            var asset = installed ? info.Installer : info.Zip;
            string what = installed ? "the installer" : "the portable ZIP";
            panel.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, Margin = new Thickness(0, 12, 0, 0),
                Text = asset == null ? "This release has no " + (installed ? "installer" : "ZIP") + ", so it can't update this copy by itself. The release page has the files."
                    : "Update now downloads " + what + (asset.Size > 0 ? $" ({asset.Size / 1048576.0:0} MB)" : "") + " from GitHub and checks it. Then Arcadia Studio closes, "
                      + (installed ? "the installer updates it" : "the new version replaces this copy's files in " + ReleaseRoot) + ", and it opens again. Your projects and settings aren't touched."
            });
            panel.Children.Add(progress); panel.Children.Add(status);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            buttons.Children.Add(Update); buttons.Children.Add(Later); buttons.Children.Add(Skip); panel.Children.Add(buttons);
            Update.IsEnabled = asset != null;
            Later.Click += (_, _) => Close();
            Skip.Click += (_, _) => { editor.Prefs().SkippedUpdate = info.Tag; editor.SavePrefs(); editor.Log("Skipped Arcadia Studio " + info.Version.ToString(3) + "; you'll hear about the next version."); Close(); };
            Update.Click += async (_, _) => await UpdateNow();
            Closing += (_, e) => { if (busy) e.Cancel = true; };
        }

        internal async Task UpdateNow()
        {
            var asset = installed ? info.Installer : info.Zip;
            if (asset == null || busy) return;
            // Unsaved work first, the same question as closing.
            editor.SaveScriptText();
            if (editor.dirty)
            {
                var answer = editor.AskForTest?.Invoke("Save changes before updating?") ?? MessageBox.Show(this, "Save changes before updating?", "Arcadia Studio", MessageBoxButton.YesNoCancel);
                if (answer == MessageBoxResult.Cancel) return;
                if (answer == MessageBoxResult.Yes) { editor.Save(saveAs: false); if (editor.dirty) return; }
            }
            busy = true; Update.IsEnabled = Later.IsEnabled = Skip.IsEnabled = false; progress.Visibility = Visibility.Visible;
            try
            {
                string folder = Wysicraft.Core.AppFolders.Path("Updates", info.Version.ToString(3));
                string file = Path.Combine(folder, asset.Name);
                status.Text = "Downloading " + asset.Name + "…";
                await new UpdateCheck(WysicraftVersion, null, UpdateApi).DownloadAsync(asset, file, new Progress<double>(v => { progress.Value = v; status.Text = $"Downloading {asset.Name}… {v:P0}"; }));
                status.Text = "Checked. Closing Arcadia Studio to update…";
                if (installed) Launch(file, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /UPDATE=1");
                else
                {
                    string release = await Task.Run(() => UpdateInstall.Unzip(file, Path.Combine(folder, "release")));
                    Launch(Path.Combine(release, "Designer", "ArcadiaStudio.exe"), "--apply-update \"" + ReleaseRoot + "\" " + Environment.ProcessId);
                }
                editor.Log("Updating to Arcadia Studio " + info.Version.ToString(3) + ".");
                busy = false; editor.closingForUpdate = true;
                if (LaunchForTest == null) Application.Current.Shutdown();
                else Close();
            }
            catch (Exception ex) when (ex is HttpRequestException or UpdateException or IOException or InvalidDataException or TaskCanceledException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                busy = false; Update.IsEnabled = Later.IsEnabled = Skip.IsEnabled = true; progress.Visibility = Visibility.Collapsed;
                status.Text = "Not updated: " + ex.Message;
                editor.Log("Update not installed: " + ex.Message);
            }
        }
        static void Launch(string file, string arguments)
        {
            if (LaunchForTest != null) { LaunchForTest(file, arguments); return; }
            Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(file)! });
        }
    }
    // Self-checks answer the save question through this.
    internal Func<string, MessageBoxResult>? AskForTest;
}
