using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Wysicraft.Models;
namespace Wysicraft.Packaging;

/// <summary>Desktop apps from the web export: a small Windows app (WebView2 host) and Electron apps for Windows,
/// macOS and Linux. Electron isn't compiled: we download its official prebuilt binaries once, verify them against
/// Electron's published checksums, and add the app files. That works from Windows for every platform.</summary>
public static class DesktopExport
{
    static string AppName(Project p) { string n = Regex.Replace(p.Manifest.Name.Length > 0 ? p.Manifest.Name : p.Manifest.Id, @"[<>:""/\\|?*\x00-\x1f]", "").Trim().TrimEnd('.'); return n.Length > 0 ? n : p.Manifest.Id; }
    static string Slug(Project p) => Regex.Replace(p.Manifest.Id.ToLowerInvariant(), "[^a-z0-9_-]", "_");

    // ---- Windows (WebView2) ----
    /// <summary>A ZIP with "Name/Name.exe" (the app host), "Name/app.ini" and "Name/app/…" (the web export).
    /// Runs on Windows 10/11 with nothing else to install.</summary>
    public static void WindowsApp(Project project, string zipPath, string appHostExe)
    {
        if (!File.Exists(appHostExe)) throw new FileNotFoundException("The Windows app host is missing from this Wysicraft install.", appHostExe);
        var web = WebExport.Files(project, new(Desktop: true)); string name = AppName(project); var (w, h) = WebExport.WindowSize(project);
        var ini = $"title={name.Replace("\n", " ")}\nid={Slug(project)}\nwidth={w}\nheight={h}\nbackground=#15181D\n";
        WriteZip(zipPath, zip => {
            Add(zip, $"{name}/{name}.exe", File.ReadAllBytes(appHostExe));
            Add(zip, $"{name}/app.ini", Encoding.UTF8.GetBytes(ini));
            // The host has the WebView2 SDK loader linked in; its license asks for the notice to travel with it.
            string notice = Path.Combine(Path.GetDirectoryName(appHostExe)!, "WysicraftAppHost-NOTICES.txt");
            if (File.Exists(notice)) Add(zip, $"{name}/THIRD-PARTY-NOTICES.txt", File.ReadAllBytes(notice));
            foreach (var (path, bytes) in web) Add(zip, $"{name}/app/{path}", bytes);
        });
    }

    // ---- Electron ----
    public static readonly string[] ElectronPlatforms = ["win32-x64", "darwin-arm64", "darwin-x64", "linux-x64"];
    public const string FallbackElectronVersion = "v37.2.0";
    public static string ElectronCache => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wysicraft", "Electron");

    /// <summary>Latest stable Electron release tag (e.g. "v38.1.0"), or the cached/fallback one when offline.</summary>
    public static async Task<string> LatestElectronAsync(HttpClient http, CancellationToken cancel = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/electron/electron/releases/latest");
            request.Headers.UserAgent.ParseAdd("Wysicraft"); request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, cancel); response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            if (Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+$")) return tag;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException) { }
        var cached = Directory.Exists(ElectronCache) ? Directory.GetDirectories(ElectronCache).Select(Path.GetFileName).Where(n => n != null && Regex.IsMatch(n, @"^v\d+\.\d+\.\d+$")).OrderByDescending(n => Version.Parse(n![1..])).FirstOrDefault() : null;
        return cached ?? FallbackElectronVersion;
    }

    /// <summary>Electron's official ZIP for a platform, from the cache or downloaded and checked against SHASUMS256.txt.</summary>
    public static async Task<string> ElectronZipAsync(HttpClient http, string version, string platform, IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        if (!ElectronPlatforms.Contains(platform)) throw new ArgumentException("Unknown platform " + platform);
        string file = $"electron-{version}-{platform}.zip", folder = Path.Combine(ElectronCache, version), path = Path.Combine(folder, file);
        if (File.Exists(path)) return path;
        Directory.CreateDirectory(folder);
        string baseUrl = $"https://github.com/electron/electron/releases/download/{version}/";
        progress?.Report("Checking Electron " + version + " checksums…");
        string sums = await http.GetStringAsync(baseUrl + "SHASUMS256.txt", cancel);
        var line = sums.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.EndsWith(" *" + file) || l.EndsWith("  " + file) || l.EndsWith(" " + file))
            ?? throw new InvalidDataException($"Electron {version} has no {file}.");
        string expected = line.Split(' ', 2)[0].ToLowerInvariant();
        progress?.Report($"Downloading {file} (about 100 MB, once)…");
        string temp = path + ".download";
        try
        {
            using (var response = await http.GetAsync(baseUrl + file, HttpCompletionOption.ResponseHeadersRead, cancel))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(cancel);
                await using var output = File.Create(temp);
                await input.CopyToAsync(output, cancel);
            }
            string actual;
            await using (var check = File.OpenRead(temp)) actual = Convert.ToHexString(await SHA256.HashDataAsync(check, cancel)).ToLowerInvariant();
            if (actual != expected) throw new InvalidDataException($"{file} failed its checksum check and was deleted. Try again.");
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return path;
    }

    /// <summary>Builds one Electron app from Electron's platform ZIP: Windows and macOS as .zip, Linux as .tar.gz
    /// (which keeps the program runnable). The ZIP is copied entry by entry, so macOS links and permissions survive.</summary>
    public static string ElectronApp(Project project, string electronZip, string platform, string outputFolder)
    {
        var web = WebExport.Files(project, new(Desktop: true)); string name = AppName(project), slug = Slug(project);
        var (w, h) = WebExport.WindowSize(project);
        var appFiles = new Dictionary<string, byte[]>
        {
            ["package.json"] = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { name = slug.Replace('_', '-'), productName = name, version = project.Manifest.Version, main = "main.js", @private = true }, new JsonSerializerOptions { WriteIndented = true })),
            ["main.js"] = Encoding.UTF8.GetBytes(ElectronMain(name, w, h)),
        };
        foreach (var (path, bytes) in web) appFiles["web/" + path] = bytes;
        Directory.CreateDirectory(outputFolder);
        bool mac = platform.StartsWith("darwin"), linux = platform.StartsWith("linux"), windows = platform.StartsWith("win32");
        string top = $"{name}-{platform}";
        // Where the app goes, and what the main program is renamed to.
        string resources = mac ? $"{name}.app/Contents/Resources/app/" : "resources/app/";
        string Rename(string entry)
        {
            if (mac && entry.StartsWith("Electron.app/")) return name + ".app/" + entry["Electron.app/".Length..];
            if (windows && entry == "electron.exe") return name + ".exe";
            if (linux && entry == "electron") return slug;
            return entry;
        }
        using var source = ZipFile.OpenRead(electronZip);
        if (linux)
        {
            string output = Path.Combine(outputFolder, top + ".tar.gz"), temp = output + ".tmp";
            try
            {
                using (var file = File.Create(temp)) using (var gz = new GZipStream(file, CompressionLevel.Optimal)) using (var tar = new TarWriter(gz, TarEntryFormat.Pax, leaveOpen: false))
                {
                    foreach (var entry in source.Entries)
                    {
                        string target = top + "/" + Rename(entry.FullName); int mode = (entry.ExternalAttributes >> 16) & 0xFFFF;
                        bool directory = entry.FullName.EndsWith('/');
                        if ((mode & 0xF000) == 0xA000) { using var reader = new StreamReader(entry.Open()); tar.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, target) { LinkName = reader.ReadToEnd(), Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute }); continue; }
                        var perms = (UnixFileMode)(mode & 0x1FF); if (perms == 0) perms = directory ? (UnixFileMode)0x1ED : (UnixFileMode)0x1A4;
                        if (Rename(entry.FullName) == slug) perms |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
                        var tarEntry = new PaxTarEntry(directory ? TarEntryType.Directory : TarEntryType.RegularFile, target) { Mode = perms };
                        MemoryStream? data = null; if (!directory) { data = new MemoryStream(); using var s = entry.Open(); s.CopyTo(data); data.Position = 0; tarEntry.DataStream = data; }
                        tar.WriteEntry(tarEntry); data?.Dispose();
                    }
                    foreach (var (path, bytes) in appFiles) tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, top + "/" + resources + path) { Mode = (UnixFileMode)0x1A4, DataStream = new MemoryStream(bytes) });
                }
                File.Move(temp, output, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return output;
        }
        string zipPath = Path.Combine(outputFolder, top + ".zip");
        WriteZip(zipPath, zip =>
        {
            foreach (var entry in source.Entries)
            {
                var copy = zip.CreateEntry((mac ? "" : top + "/") + Rename(entry.FullName), CompressionLevel.Optimal);
                copy.ExternalAttributes = entry.ExternalAttributes; copy.LastWriteTime = entry.LastWriteTime;
                if (!entry.FullName.EndsWith('/')) { using var input = entry.Open(); using var output = copy.Open(); input.CopyTo(output); }
            }
            foreach (var (path, bytes) in appFiles)
            {
                var e = zip.CreateEntry((mac ? "" : top + "/") + resources + path, CompressionLevel.Optimal);
                if (mac) e.ExternalAttributes = (0x81A4 << 16); // regular file, rw-r--r--
                using var s = e.Open(); s.Write(bytes);
            }
        });
        return zipPath;
    }

    static string ElectronMain(string name, int width, int height) => $$"""
// Electron entry point for a Wysicraft app: one window showing web/index.html. Web pages get no Node.js access.
const { app, BrowserWindow, Menu, shell } = require('electron');
const path = require('path');
app.setName({{JsonSerializer.Serialize(name)}});
// A game plays its sound from the start (a title theme) instead of waiting for a click, as an app should.
app.commandLine.appendSwitch('autoplay-policy', 'no-user-gesture-required');
if (process.platform !== 'darwin') Menu.setApplicationMenu(null);
function createWindow() {
  const win = new BrowserWindow({
    width: {{width}}, height: {{height}}, useContentSize: true, backgroundColor: '#15181D', autoHideMenuBar: true, show: false,
    title: {{JsonSerializer.Serialize(name)}},
    webPreferences: { contextIsolation: true, nodeIntegration: false, sandbox: true }
  });
  win.once('ready-to-show', () => win.show());
  // The app only shows its own pages; links to websites open in the normal browser.
  win.webContents.setWindowOpenHandler(({ url }) => { if (/^https?:/.test(url)) shell.openExternal(url); return { action: 'deny' }; });
  win.webContents.on('will-navigate', (event, url) => { if (!url.startsWith('file:')) event.preventDefault(); });
  win.loadFile(path.join(__dirname, 'web', 'index.html'));
}
app.whenReady().then(createWindow);
app.on('window-all-closed', () => app.quit());
""";

    static void WriteZip(string path, Action<ZipArchive> fill)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { using (var file = File.Create(temp)) using (var zip = new ZipArchive(file, ZipArchiveMode.Create)) fill(zip); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    static void Add(ZipArchive zip, string name, byte[] bytes) { using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open(); s.Write(bytes); }
}
