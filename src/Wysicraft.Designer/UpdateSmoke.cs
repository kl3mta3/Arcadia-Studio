using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Windows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Wysicraft.Packaging;
namespace Wysicraft.Designer;

// --smoke-update: updates against a fake GitHub on this computer. The popup, Skip, a download that fails its checksum,
// and an installed copy's update (the installer launch is caught, not run). When a release ZIP is given (output + ".zip"),
// it then updates this portable copy for real: it closes, and the new version copies itself over this folder and
// opens it (the caller checks the result, since this process has ended by then).
public partial class MainWindow
{
    internal async Task VerifyUpdatesAsync(string output)
    {
        byte[] setup = System.Text.Encoding.UTF8.GetBytes("pretend installer");
        string zipPath = output + ".zip"; byte[]? zip = File.Exists(zipPath) ? File.ReadAllBytes(zipPath) : null;
        string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
        bool badDigest = false;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ApplicationName = typeof(MainWindow).Assembly.FullName, ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0));
        var fake = builder.Build();
        string address = "";
        fake.Use(async (HttpContext http, Func<Task> next) =>
        {
            string path = http.Request.Path.Value ?? "";
            if (path == "/repos/kl3mta3/arcadia-studio/releases/latest")
            {
                string Asset(string name, long size, string sha) => "{\"name\":\"" + name + "\",\"size\":" + size + ",\"browser_download_url\":\"" + address + "/dl/" + name + "\",\"digest\":\"sha256:" + sha + "\"}";
                var assets = new List<string> { Asset("ArcadiaStudio-9.9.9-Setup.exe", setup.Length, badDigest ? new string('0', 64) : Sha(setup)) };
                if (zip != null) assets.Add(Asset("ArcadiaStudio-9.9.9-win-x64.zip", zip.Length, Sha(zip)));
                http.Response.ContentType = "application/json";
                await http.Response.WriteAsync("{\"tag_name\":\"v9.9.9\",\"name\":\"Arcadia Studio 9.9.9\",\"body\":\"- A pretend release for the self-check.\",\"html_url\":\"https://github.com/kl3mta3/arcadia-studio/releases\",\"draft\":false,\"prerelease\":false,\"assets\":[" + string.Join(",", assets) + "]}");
                return;
            }
            if (path == "/dl/ArcadiaStudio-9.9.9-Setup.exe") { await http.Response.Body.WriteAsync(setup); return; }
            if (path == "/dl/ArcadiaStudio-9.9.9-win-x64.zip" && zip != null) { http.Response.ContentLength = zip.Length; await http.Response.Body.WriteAsync(zip); return; }
            http.Response.StatusCode = 404;
        });
        await fake.StartAsync();
        address = fake.Services.GetService(typeof(IServer)) is IServer server ? server.Features.Get<IServerAddressesFeature>()!.Addresses.First() : throw new Exception("No fake GitHub");
        var prefs = Prefs(); string hadSkip = prefs.SkippedUpdate, hadLast = prefs.LastUpdateCheck;
        string savedApi = UpdateApi; UpdateApi = address + "/";
        void Expect(bool ok, string what) { if (!ok) throw new Exception("Updates: " + what); }
        UpdateWindow? Popup() => Application.Current.Windows.OfType<UpdateWindow>().LastOrDefault();
        try
        {
            // The popup, and Skip this version.
            InstalledForTest = true;
            var info = await CheckForUpdatesAsync(quiet: false);
            var popup = Popup();
            Expect(info?.Version == new Version(9, 9, 9) && popup != null && popup.Update.IsEnabled, "a newer release shows the popup with Update now");
            popup!.Skip.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Expect(Prefs().SkippedUpdate == "v9.9.9" && Popup() == null, "Skip this version remembers it and closes");
            await CheckForUpdatesAsync(quiet: true);
            Expect(Popup() == null, "a skipped version isn't shown again by the startup check");
            Prefs().SkippedUpdate = "";

            // A download that doesn't match its checksum.
            badDigest = true; await CheckForUpdatesAsync(quiet: false); popup = Popup()!;
            string? launched = null; LaunchForTest = (file, args) => launched = file + " " + args;
            await popup.UpdateNow();
            Expect(launched == null && popup.Status.Contains("doesn't match its checksum") && popup.Update.IsEnabled, "a file that fails its checksum isn't run, and the popup says why");
            popup.Close(); badDigest = false;

            // An installed copy: unsaved work is asked about, then the checked installer is launched quietly.
            dirty = true; string? asked = null; AskForTest = q => { asked = q; return MessageBoxResult.No; };
            await CheckForUpdatesAsync(quiet: false); popup = Popup()!;
            await popup.UpdateNow();
            Expect(asked == "Save changes before updating?", "unsaved work is asked about first");
            Expect(launched != null && launched.Contains("ArcadiaStudio-9.9.9-Setup.exe") && launched.Contains("/UPDATE=1") && launched.Contains("/SILENT") && closingForUpdate, "the installer is launched quietly (and to reopen the app) after its checksum passed");
            string downloaded = launched!.Split(" /")[0];
            Expect(File.ReadAllBytes(downloaded).SequenceEqual(setup), "what's launched is exactly the checked download");
            closingForUpdate = false; dirty = false; AskForTest = null;

            // Portable copies get the ZIP, or say there isn't one.
            InstalledForTest = false;
            await CheckForUpdatesAsync(quiet: false); popup = Popup()!;
            Expect(popup.Update.IsEnabled == (zip != null), "a portable copy updates from the ZIP (and can't without one)");
            if (zip == null) { popup.Close(); File.WriteAllText(output, "PASS: update popup, skip, checksum refusal, installed update (installer launch caught)"); return; }

            // For real: this portable copy hands over to the new version and closes.
            File.WriteAllText(output, "PASS: update popup, skip, checksum refusal, installed update (installer launch caught); portable hand-over started");
            LaunchForTest = null;
            await popup.UpdateNow();
        }
        finally
        {
            UpdateApi = savedApi; InstalledForTest = null; LaunchForTest = null; AskForTest = null;
            prefs.SkippedUpdate = hadSkip; prefs.LastUpdateCheck = hadLast; SavePrefs();
            await fake.StopAsync(); await fake.DisposeAsync();
        }
    }
}
