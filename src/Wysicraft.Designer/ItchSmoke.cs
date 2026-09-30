using System.IO;
using System.Net;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Wysicraft.Models;
using Wysicraft.Packaging;
using Path = System.IO.Path;
namespace Wysicraft.Designer;

// --smoke-itch: the Publish to itch.io window with a fake butler (this program, --fake-butler) and a fake itch.io API
// on this computer. Sign in (the key kept encrypted, the sign-in file gone), the account and games, the local checks,
// Check (push-preview), Publish (web and Windows, with the key only in butler's environment), the version, the Arcadia
// question (Yes fills in Publish to Arcadia; No isn't asked again), a sign-in that stopped working, and signing out.
// Nothing reaches itch.io, and the preferences it touches are put back.
public partial class MainWindow
{
    internal async Task VerifyItchAsync(string output)
    {
        var calls = new List<string>();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ApplicationName = typeof(MainWindow).Assembly.FullName, ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0));
        var fake = builder.Build();
        fake.Use(async (HttpContext http, Func<Task> next) =>
        {
            string path = http.Request.Path.Value ?? "", auth = http.Request.Headers.Authorization.ToString();
            calls.Add(path + (auth == "Bearer " + FakeButler.Key ? " [key]" : auth.Length > 0 ? " [other key]" : ""));
            async Task Send(int status, string json) { http.Response.StatusCode = status; http.Response.ContentType = "application/json"; await http.Response.WriteAsync(json); }
            if (auth == "Bearer upload-only-key") { await Send(403, "{\"errors\":[\"api key does not permit `" + (path == "/profile" ? "profile:me" : "profile:games") + "`\"]}"); return; }
            if (auth != "Bearer " + FakeButler.Key) { await Send(401, "{\"errors\":[\"invalid key\"]}"); return; }
            if (path == "/profile") { await Send(200, "{\"user\":{\"id\":7,\"username\":\"smoke\",\"display_name\":\"Smoke Studio\",\"url\":\"https://smoke.itch.io\"}}"); return; }
            if (path == "/profile/games") { await Send(200, "{\"games\":[{\"id\":42,\"title\":\"Smoke Game\",\"url\":\"https://smoke.itch.io/game\",\"short_text\":\"A game for the smoke test.\",\"cover_url\":\"\",\"classification\":\"game\",\"type\":\"html\",\"published\":true}]}"); return; }
            await Send(404, "{\"errors\":[\"not found\"]}");
        });
        await fake.StartAsync();
        string address = fake.Services.GetService(typeof(IServer)) is IServer server ? server.Features.Get<IServerAddressesFeature>()!.Addresses.First() : throw new Exception("No fake itch.io address");
        string log = Path.Combine(Path.GetTempPath(), "fake-butler-" + Guid.NewGuid().ToString("N") + ".log");
        var prefs = Prefs(); string hadKey = prefs.ItchKey; var hadPublishing = Json.Clone(project.Publishing); hadPublishing.KeepImages(project.Publishing);
        ButlerOverride = Environment.ProcessPath; ItchApiOverride = address; NoBrowser = true;
        Environment.SetEnvironmentVariable("FAKE_BUTLER_LOG", log);
        AppHostOverride = FindAppHostForSmoke();
        ItchDialog? dialog = null;
        try
        {
            prefs.ItchKey = ""; project.Publishing = new PublishSettings();
            dialog = new ItchDialog(this); dialog.Show();
            string result = await dialog.SelfTest(calls, log, () => File.Exists(log) ? File.ReadAllLines(log).Select(l => JsonNode.Parse(l)!).ToList() : []);
            dialog.UpdateLayout();
            var shot = new System.Windows.Media.Imaging.RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); shot.Render(dialog);
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(shot));
            using (var file = File.Create(output + ".png")) png.Save(file);
            File.WriteAllText(output, result);
        }
        finally
        {
            dialog?.CloseForTest(); arcadiaDialog?.Close();
            ButlerOverride = null; ItchApiOverride = null; NoBrowser = false; AppHostOverride = null;
            Environment.SetEnvironmentVariable("FAKE_BUTLER_LOG", null);
            prefs.ItchKey = hadKey; SavePrefs(); project.Publishing = hadPublishing; dirty = false;
            try { File.Delete(log); } catch { }
            await fake.StopAsync(); await fake.DisposeAsync();
        }
    }
    static string? FindAppHostForSmoke()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                foreach (var relative in new[] { "Runtime", "artifacts/apphost" })
                { string p = Path.Combine(d.FullName, relative, "WysicraftAppHost.exe"); if (File.Exists(p)) return p; }
        return null;
    }

    internal sealed partial class ItchDialog
    {
        async Task Settle() { for (int i = 0; i < 400 && busy; i++) await Task.Delay(25); await Task.Delay(50); }
        internal async Task<string> SelfTest(List<string> calls, string logPath, Func<List<JsonNode>> log)
        {
            void Expect(bool ok, string what) { if (!ok) throw new Exception("Publish to itch.io: " + what + "\nStatus: " + status.Text + "\nAccount: " + account.Text + "\nCalls: " + string.Join(", ", calls) + "\nbutler: " + string.Join("\n", log().Select(n => n.ToJsonString()))); }
            await Settle();
            Expect(key == null && account.Text.StartsWith("Not signed in") && !publish.IsEnabled, "starts signed out, with Publish off");
            Expect(game.Text.StartsWith("Not set up"), "and no game chosen");

            // Signing in: butler's browser sign-in, the key kept encrypted and the file butler wrote gone.
            await Guard(SignIn);
            Expect(editor.ItchKey() == FakeButler.Key && !File.ReadAllText(PreferencesPath).Contains(FakeButler.Key), "the key is kept, encrypted only");
            Expect(!Directory.Exists(Wysicraft.Core.AppFolders.Path("ItchSignIn")) || Directory.GetFileSystemEntries(Wysicraft.Core.AppFolders.Path("ItchSignIn")).Length == 0, "butler's sign-in file is removed");
            Expect(log().Any(c => (string?)c["command"] == "login" && !(bool)c["anyKey"]!), "sign-in ran butler login, with no key given to it");
            Expect(account.Text.Contains("Smoke Studio") && (string)signIn.Content == "Sign out" && apiKey.Visibility == Visibility.Collapsed, "the account shows who is signed in");

            // Set up (as the Set up window would): the game and its channels.
            games = await editor.NewItchApi().GamesAsync(key!, work?.Token ?? default);
            Expect(games.Count == 1 && games[0].Target == "smoke/game", "your games come from itch.io, with their addresses");
            Set(n => { n.Target = "smoke/game"; n.GameId = 42; n.GameTitle = "Smoke Game"; n.GameUrl = "https://smoke.itch.io/game"; });
            ShowGame();
            Expect(game.Text.Contains("Smoke Game") && game.Text.Contains("smoke/game") && publish.IsEnabled, "the game is shown, and Publish turns on");

            // The local checks come first: nothing is asked of butler while something is wrong.
            web.IsChecked = true; windows.IsChecked = true; Apply(); Set(n => n.WindowsChannel = "html5");
            int before = log().Count;
            await Guard(Check);
            Expect(findings.Items.OfType<ListBoxItem>().Any(i => i.Tag is ArcadiaFinding { Level: "block", Code: "channel" }) && log().Count == before, "two uploads on one channel are refused before butler is asked");
            Set(n => n.WindowsChannel = "windows");

            // Check: butler's preview of each upload, with the key in its environment only.
            version.Text = "1.0.0";
            await Guard(Check);
            var previews = log().Where(c => (string?)c["command"] == "push-preview").ToList();
            Expect(previews.Count == 2 && previews.All(c => (bool)c["keyGiven"]!) && status.Text.StartsWith("All clear"), "Check previews both uploads with the key");
            Expect(findings.Items.OfType<ListBoxItem>().Any(i => i.Content is TextBlock t && t.Text.Contains("Comparison")), "and shows what each would change");

            // Publish: both uploads, their files, the version; then the Arcadia question (Yes fills in Publish to Arcadia).
            string asked = ""; Confirm = q => { asked = q; return true; };
            await Guard(Publish);
            var pushes = log().Where(c => (string?)c["command"] == "push").ToList();
            Expect(pushes.Count == 2 && pushes.All(c => (bool)c["keyGiven"]!), "Publish sends both uploads, with the key");
            JsonNode Push(string channel) => pushes.First(c => c["args"]!.AsArray().Any(a => (string?)a == "smoke/game:" + channel));
            var webFiles = Push("html5")["files"]!.AsArray().Select(f => (string)f!).ToList();
            var winFiles = Push("windows")["files"]!.AsArray().Select(f => (string)f!).ToList();
            Expect(webFiles.Contains("index.html") && webFiles.Contains("wysicraft/wysicraft-web.js"), "the web upload is the web export, index.html at the top: " + string.Join(",", webFiles.Take(6)));
            Expect(winFiles.Any(f => f.EndsWith(".exe") && !f.Contains('/')) && winFiles.Contains("app.ini") && winFiles.Contains("app/index.html"), "the Windows upload is the Windows app, its program at the top: " + string.Join(",", winFiles.Take(6)));
            var args = Push("html5")["args"]!.AsArray().Select(a => (string)a!).ToList();
            Expect(args.Contains("--userversion=1.0.0") && args.Contains("--if-changed") && !args.Contains("--hidden"), "with the version and the upload options: " + string.Join(" ", args));
            Expect(S.LastVersion == "1.0.0" && version.Text == "1.0.1" && status.Text.StartsWith("Published 1.0.0"), "the version is kept and the next one offered");
            Expect(asked.Contains("Arcadia is a community-driven arcade with built-in leaderboards") && asked.Contains("Smoke Game"), "after publishing, Arcadia is offered: " + asked);
            var arcadia = editor.arcadiaDialog;
            Expect(arcadia != null && editor.project.Publishing.Title == "Smoke Game" && editor.project.Publishing.Description == "A game for the smoke test." && editor.project.Publishing.Version == "1.0.0", "Yes opens Publish to Arcadia with the title, description and version filled in");
            arcadia!.Close();

            // No: not asked again for this game.
            asked = ""; Confirm = q => { asked = q; return false; };
            version.Text = "1.0.1"; await Guard(Publish);
            Expect(asked.Length > 0 && S.NoArcadiaNudge, "No is remembered");
            asked = ""; version.Text = "1.0.2"; await Guard(Publish);
            Expect(asked.Length == 0 && S.LastVersion == "1.0.2", "and Arcadia isn't offered again");

            // A sign-in that may upload but not read the account (what butler's sign-in gives, as itch.io answered it):
            // kept, and Publish stays on.
            editor.StoreItchKey("upload-only-key"); await Guard(Refresh);
            Expect(key == "upload-only-key" && editor.ItchKey() == "upload-only-key" && account.Text.StartsWith("Signed in to itch.io.") && publish.IsEnabled, "an upload-only sign-in is kept and Publish stays on: " + account.Text);
            games = null; await Guard(async () => { if (key != null && games == null) try { games = await editor.NewItchApi().GamesAsync(key, work!.Token); } catch (ItchException ex) when (ex.Scope) { games = []; } });
            Expect(games != null && games.Count == 0 && key != null, "and Set up falls back to typing the game's address");

            // A sign-in that stopped working is dropped, with a clear message; publishing doesn't try.
            editor.StoreItchKey("revoked-key"); await Guard(Refresh);
            Expect(key == null && editor.ItchKey() == null && account.Text.Contains("isn't valid anymore") && !publish.IsEnabled, "a revoked key is forgotten and Sign in comes back");

            // Signing out.
            editor.StoreItchKey(FakeButler.Key); await Guard(Refresh);
            Confirm = _ => true; await Guard(SignOut);
            Expect(editor.ItchKey() == null && account.Text.StartsWith("Not signed in"), "Sign out forgets the key");
            return "PASS: Publish to itch.io with a fake butler and a fake itch.io: sign in (butler login, key encrypted, sign-in file removed), account and games, local checks before butler, Check (push-preview, key in the environment only), Publish (web export and Windows app, version, options), the Arcadia offer after publishing (Yes fills in Publish to Arcadia, No isn't asked again), a revoked key, sign out";
        }
    }
}
