using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Wysicraft.Packaging;
namespace Wysicraft.Designer;

// File → Export: Minecraft formats, plus the project as a web page, a Windows app or Electron apps.
public partial class MainWindow
{
    static readonly HttpClient ExportHttp = new() { Timeout = TimeSpan.FromMinutes(20) };
    sealed record ExportFormat(string Key, string Label, string Help);
    static readonly ExportFormat[] ExportFormats =
    [
        new("installation", "Minecraft: Installation ZIP — bundled client/server JARs", "For Minecraft 1.21.1 / NeoForge. Separate client and server project JARs with the runtime and assigned scripts bundled, plus instructions."),
        new("jar", "Minecraft: Project JAR — runtime + assigned scripts bundled", "For Minecraft 1.21.1 / NeoForge. One JAR for the mods folder. JAR changes require a game restart."),
        new("standard", "Minecraft: Portable .wysicraft pack", "The project's screens, scripts and images in one file, for servers that already have the Arcadia Studio runtime."),
        new("kubejs_files", "Minecraft: KubeJS loose files (advanced)", "Loose files for a KubeJS setup. KubeJS projects still require KubeJS/Rhino."),
        new("web_folder", "Web page — folder (index.html, host.js, images)", "Runs in any browser: open index.html, or put the folder on a website. Edit host.js to connect server actions to your own code; re-exporting keeps your host.js."),
        new("web_file", "Web page — one self-contained HTML file", "Everything in a single .html file (images included), easy to send or attach. Open it in any browser."),
        new("windows_app", "Windows app (.zip) — runs on Windows 10/11", "A small program (under 1 MB plus your images) that shows the project in its own window using Microsoft Edge WebView2, which comes with Windows. Unzip and run the .exe."),
        new("electron", "Electron apps — Windows, macOS and Linux", "Full desktop apps built on Electron (about 100 MB each). Electron is downloaded from its official GitHub releases the first time, checked against its published checksums, and kept for next time. Unsigned: on macOS, run  xattr -cr YourApp.app  once (or sign it on a Mac); on Linux, extract the .tar.gz and run the program."),
    ];
    // Self-checks run from a build folder outside the install point this at the app host.
    internal static string? AppHostOverride;
    string AppHostExe()
    {
        if (AppHostOverride != null) return AppHostOverride;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            foreach (var relative in new[] { "Runtime", "artifacts/apphost" })
            { string path = Path.Combine(directory.FullName, relative, "WysicraftAppHost.exe"); if (File.Exists(path)) return path; }
        throw new InvalidDataException("The Windows app host is missing. Keep Runtime beside Designer.");
    }

    // A size warning goes in the log too, so it is still there after the dialog closes.
    void LogSizeWarning(string format) { if (Wysicraft.Core.DownloadSize.Warning(project, format) is string size) Log("Size: " + size); }

    void Export()
    {
        SaveScriptText();
        var window = new Window { Owner = this, Title = "Export project", Width = 620, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(16) }; window.Content = panel;
        var formats = new ComboBox { ItemsSource = ExportFormats.Select(f => f.Label).ToArray(), SelectedIndex = project.Manifest.Target == "web" ? 4 : 1 }; panel.Children.Add(formats);
        var help = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 12, 4, 8) }; panel.Children.Add(help);
        var platforms = new WrapPanel { Margin = new Thickness(4, 0, 4, 8) }; panel.Children.Add(platforms);
        var platformBoxes = new Dictionary<string, CheckBox>();
        foreach (var (key, label) in new[] { ("win32-x64", "Windows x64"), ("darwin-arm64", "macOS Apple silicon"), ("darwin-x64", "macOS Intel"), ("linux-x64", "Linux x64") })
        {
            // Windows is off by default: the Windows app format does the same job at a fraction of the size.
            bool windows = key == "win32-x64";
            var box = new CheckBox { Content = label, IsChecked = !windows, Margin = new Thickness(0, 0, 16, 4) };
            if (windows) { box.ToolTip = "For Windows we suggest the \"Windows app\" format instead: it uses WebView2, which comes with Windows 10 and 11, so it's under 1 MB plus your images instead of about 100 MB. Tick this only if you need the Windows build to match your Electron Mac/Linux builds."; ToolTipService.SetShowOnDisabled(box, true); }
            platformBoxes[key] = box; platforms.Children.Add(box);
        }
        var warnings = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 4, 8), Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0xC7, 0x44)) }; panel.Children.Add(warnings);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 4, 8), Foreground = Brushes.LightGray }; panel.Children.Add(status);
        var button = new Button { Content = "Export…" }; panel.Children.Add(button);
        var webWarnings = Wysicraft.Packaging.WebExport.Warnings(project);
        void Refresh()
        {
            var f = ExportFormats[formats.SelectedIndex];
            // How big this format comes out, and a warning past what players should have to download.
            long? bytes = Wysicraft.Core.DownloadSize.Estimate(project, f.Key);
            help.Text = f.Help + (bytes is long b ? "\nAbout " + Wysicraft.Core.DownloadSize.Mb(b) + (f.Key == "electron" ? " per app." : ".") : "");
            bool web = f.Key is "web_folder" or "web_file" or "windows_app" or "electron";
            platforms.Visibility = f.Key == "electron" ? Visibility.Visible : Visibility.Collapsed;
            var notes = new List<string>(webWarnings);
            if (Wysicraft.Core.DownloadSize.Warning(project, f.Key) is string size) notes.Insert(0, size);
            warnings.Text = web && notes.Count > 0 ? "Outside Minecraft:\n• " + string.Join("\n• ", notes) : "";
            warnings.Visibility = warnings.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        formats.SelectionChanged += (_, _) => Refresh(); Refresh();
        bool busy = false;
        window.Closing += (_, e) => { if (busy) e.Cancel = true; };
        button.Click += async (_, _) =>
        {
            if (busy) return;
            var f = ExportFormats[formats.SelectedIndex];
            try
            {
                switch (f.Key)
                {
                    case "web_folder":
                    {
                        string? folder = NameAFolder(window, "Name a folder for the web page (index.html goes inside)", project.Manifest.Id + "-web");
                        if (folder == null) return;
                        Wysicraft.Packaging.WebExport.ExportFolder(project, folder); Log("Exported web page to " + folder); LogSizeWarning(f.Key); window.Close(); return;
                    }
                    case "web_file":
                    {
                        var dialog = new SaveFileDialog { Filter = "Web page|*.html", DefaultExt = ".html", FileName = project.Manifest.Id + ".html" };
                        if (dialog.ShowDialog(window) != true) return;
                        Wysicraft.Packaging.WebExport.ExportSingleFile(project, dialog.FileName); Log("Exported " + dialog.FileName); LogSizeWarning(f.Key); window.Close(); return;
                    }
                    case "windows_app":
                    {
                        var dialog = new SaveFileDialog { Filter = "Windows app|*.zip", DefaultExt = ".zip", FileName = project.Manifest.Id + "-windows.zip" };
                        if (dialog.ShowDialog(window) != true) return;
                        DesktopExport.WindowsApp(project, dialog.FileName, AppHostExe()); Log("Exported Windows app " + dialog.FileName); LogSizeWarning(f.Key); window.Close(); return;
                    }
                    case "electron":
                    {
                        var chosen = platformBoxes.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList();
                        if (chosen.Count == 0) throw new InvalidOperationException("Choose at least one platform.");
                        string? chosenFolder = NameAFolder(window, "Name a folder for the Electron apps", project.Manifest.Id + "-apps");
                        if (chosenFolder == null) return;
                        var snapshot = Wysicraft.Models.Json.CloneProject(project); string output = chosenFolder;
                        busy = true; button.IsEnabled = formats.IsEnabled = false;
                        var progress = new Progress<string>(text => status.Text = text);
                        string version = await DesktopExport.LatestElectronAsync(ExportHttp);
                        var made = new List<string>();
                        foreach (var platform in chosen)
                        {
                            string zip = await DesktopExport.ElectronZipAsync(ExportHttp, version, platform, progress);
                            ((IProgress<string>)progress).Report($"Building the {platform} app…");
                            made.Add(await Task.Run(() => DesktopExport.ElectronApp(snapshot, zip, platform, output)));
                        }
                        busy = false; Log($"Exported Electron {version} apps: " + string.Join(", ", made.Select(Path.GetFileName))); LogSizeWarning(f.Key); window.Close(); return;
                    }
                    default:
                    {
                        if (!MinecraftExportAllowed("Exporting for Minecraft")) return;
                        string ext = f.Key == "jar" ? "jar" : f.Key == "standard" ? "wysicraft" : "zip";
                        var dialog = new SaveFileDialog { Filter = $"Export file|*.{ext}", DefaultExt = "." + ext, FileName = project.Manifest.Id + "." + ext };
                        if (dialog.ShowDialog(window) != true) return;
                        ExportArtifact(f.Key, dialog.FileName); Log("Exported " + dialog.FileName); window.Close(); return;
                    }
                }
            }
            catch (Exception ex) { status.Text = ex.Message; Log("Export failed: " + ex.Message); }
            finally { busy = false; button.IsEnabled = formats.IsEnabled = true; }
        };
        window.ShowDialog();
    }
}

public partial class MainWindow
{
    /// <summary>Asks for a folder by name rather than by picking an existing one, and makes it. A folder picker can
    /// only return somewhere that is already there, which is why naming a new one used to mean a trip to Explorer.</summary>
    internal static string? NameAFolder(Window owner, string title, string suggested)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = title, FileName = suggested, AddExtension = false,
            Filter = "Folder|*.", OverwritePrompt = false, CheckPathExists = true
        };
        if (dialog.ShowDialog(owner) != true) return null;
        string folder = dialog.FileName.TrimEnd('.', ' ');
        if (File.Exists(folder)) throw new InvalidOperationException($"{System.IO.Path.GetFileName(folder)} is a file, not a folder. Give the folder a different name.");
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Writes the project out as ordinary files and folders — screens, scripts and images, all openable —
    /// rather than the single .wysicraftproj, which is a zip and shows nothing in Explorer.</summary>
    void SaveLooseCopy()
    {
        string? folder = NameAFolder(this, "Name a folder for the project's files", PixelEditor.SafeName(project.Manifest.Id) + "-files");
        if (folder == null) return;
        ProjectStore.Save(project, folder);
        int files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Length;
        Log($"Wrote {files} files to {folder}. This is a copy to look at: editing it does not change the project.");
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true }); } catch { }
    }
}
