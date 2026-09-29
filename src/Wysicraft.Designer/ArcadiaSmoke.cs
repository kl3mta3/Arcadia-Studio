using System.IO;
using System.Net;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Logging;
using Wysicraft.Packaging;
namespace Wysicraft.Designer;

// --smoke-publish: the Publish to Arcadia window against a fake arcade on this computer. Linking (without a browser),
// the account, Check, Publish (processing → live, the game ID kept), publishing again to the same game, and a revoked
// key. Nothing reaches a real arcade, and the preferences it touches are put back.
public partial class MainWindow
{
    internal async Task VerifyPublishingAsync(string output)
    {
        var calls = new List<string>(); int polls = 0, statusPolls = 0, newGames = 0; bool revoked = false, deleted = false, titleTaken = false;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ApplicationName = typeof(MainWindow).Assembly.FullName, ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => { o.Listen(IPAddress.Loopback, 0); o.Limits.MaxRequestBodySize = 64 * 1024 * 1024; });
        var fake = builder.Build();
        const string key = "arcadia_pk_smoke";
        string submission(string status) => "{\"id\":7,\"gameId\":\"c-smoke\",\"number\":1,\"version\":\"1.0.0\",\"status\":\"" + status + "\",\"message\":null,\"findings\":[],\"createdAt\":1790592271305,\"gameUrl\":\"GAMEURL\"}";
        fake.Use(async (HttpContext http, Func<Task> next) =>
        {
            string path = http.Request.Path.Value ?? "", method = http.Request.Method, auth = http.Request.Headers.Authorization.ToString();
            using var body = new MemoryStream(); await http.Request.Body.CopyToAsync(body);
            calls.Add(method + " " + path + http.Request.QueryString.Value + (auth.Length > 0 ? " [key]" : "") + " " + http.Request.ContentType);
            string gameUrl = "http://" + http.Request.Host.Value + "/#/play/c-smoke";
            async Task Send(int status, string json) { http.Response.StatusCode = status; http.Response.ContentType = "application/json"; await http.Response.WriteAsync(json.Replace("GAMEURL", gameUrl)); }
            if (path == "/api/publish/link") { await Send(200, "{\"deviceCode\":\"dev-smoke\",\"userCode\":\"SMOK-2345\",\"verificationUri\":\"x\",\"verificationUriComplete\":\"x\",\"interval\":1,\"expiresIn\":60}"); return; }
            if (path == "/api/publish/link/token") { await Send(polls++ == 0 ? 400 : 200, polls == 1 ? "{\"error\":\"authorization_pending\"}" : "{\"key\":\"" + key + "\",\"creator\":{\"name\":\"Smoke Studio\",\"slug\":\"smoke\",\"url\":\"u\",\"status\":\"active\"}}"); return; }
            if (auth != "Bearer " + key || revoked) { await Send(401, "{\"error\":\"This device was unlinked on the website.\",\"code\":\"bad-key\"}"); return; }
            switch (method + " " + path)
            {
                case "GET /api/publish/me":
                    await Send(200, "{\"creator\":{\"name\":\"Smoke Studio\",\"slug\":\"smoke\",\"url\":\"u\",\"status\":\"active\",\"trusted\":false,\"autoPublish\":false},\"publishing\":{\"enabled\":true,\"maxUploadMb\":50},\"limits\":{\"newGame\":{\"available\":1,\"max\":1,\"nextAt\":null}},\"games\":[{\"id\":\"c-smoke\",\"title\":\"Smoke\",\"status\":\"live\",\"url\":\"u\",\"limits\":{\"available\":3,\"max\":3}}]}"); return;
                case "POST /api/publish/check":
                    if (deleted && http.Request.Query["game"] == "c-smoke") { await Send(404, "{\"error\":\"You deleted this game on Arcadia.\",\"code\":\"deleted\"}"); return; }
                    await Send(200, "{\"ok\":true,\"wouldHold\":true,\"findings\":[{\"level\":\"hold\",\"code\":\"new-runtime\",\"message\":\"A new Arcadia Studio runtime build\",\"file\":\"wysicraft/wysicraft-web.js\"}]}"); return;
                case "POST /api/publish/games":
                    // A different game of theirs already has this title.
                    if (titleTaken) { titleTaken = false; await Send(409, "{\"error\":\"You already have a game called Smoke.\",\"code\":\"title-taken\",\"gameId\":\"c-smoke2\"}"); return; }
                    string newId = ++newGames > 1 ? "c-smoke" + newGames : "c-smoke";
                    await Send(202, "{\"game\":{\"id\":\"" + newId + "\",\"title\":\"Smoke\",\"url\":\"GAMEURL\"},\"submission\":" + submission("processing") + "}"); return;
                case var update when update.StartsWith("POST /api/publish/games/"):
                    // Deleted on the arcade: updates are refused with their own code.
                    string id = path[(path.LastIndexOf('/') + 1)..];
                    if (deleted && id == "c-smoke") { await Send(404, "{\"error\":\"You deleted this game on Arcadia.\",\"code\":\"deleted\"}"); return; }
                    await Send(202, "{\"game\":{\"id\":\"" + id + "\",\"title\":\"Smoke\",\"url\":\"GAMEURL\"},\"submission\":" + submission("processing") + "}"); return;
                case "GET /api/publish/submissions/7":
                    await Send(200, submission(statusPolls++ == 0 ? "processing" : "live")); return;
            }
            await Send(404, "{\"error\":\"Not here.\",\"code\":\"not-found\"}");
        });
        await fake.StartAsync();
        string address = fake.Services.GetService(typeof(IServer)) is IServer server ? server.Features.Get<IServerAddressesFeature>()!.Addresses.First() : throw new Exception("No fake arcade address");
        var client = new ArcadiaClient(address, WysicraftVersion);
        var prefs = Prefs(); bool hadKey = prefs.ArcadiaKeys.ContainsKey(client.Host), hadGame = prefs.ArcadiaGames.ContainsKey(client.Host + "|" + project.Manifest.Id);
        NoBrowser = true;
        ArcadiaDialog? dialog = null;
        try
        {
            dialog = new ArcadiaDialog(this, client); dialog.Show();
            string result = await dialog.SelfTest(calls, () => deleted = true, () => titleTaken = true);
            // A picture of the window as it ended, beside the result, for a look at the layout.
            dialog.UpdateLayout();
            var shot = new System.Windows.Media.Imaging.RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); shot.Render(dialog);
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(shot));
            using (var file = File.Create(output + ".png")) png.Save(file);
            File.WriteAllText(output, result);
        }
        finally
        {
            NoBrowser = false; dialog?.CloseForTest(); dirty = false;
            if (!hadKey) prefs.ArcadiaKeys.Remove(client.Host);
            if (!hadGame) prefs.ArcadiaGames.Remove(client.Host + "|" + project.Manifest.Id);
            SavePrefs();
            await fake.StopAsync(); await fake.DisposeAsync();
        }
        _ = revoked;
    }

    sealed partial class ArcadiaDialog
    {
        internal void CloseForTest() { busy = false; Close(); }
        async Task Settle() { for (int i = 0; i < 400 && busy; i++) await Task.Delay(25); await Task.Delay(50); }
        internal async Task<string> SelfTest(List<string> calls, Action deleteOnArcade, Action takeTitle)
        {
            void Expect(bool ok, string what) { if (!ok) throw new Exception("Publish window: " + what + "\nStatus: " + status.Text + "\nAccount: " + account.Text + "\nCalls:\n" + string.Join("\n", calls)); }
            await Settle();
            Expect(key == null && account.Text.StartsWith("Not linked") && (string)link.Content == "Link to Arcadia…" && !publish.IsEnabled, "starts unlinked, with Publish off");

            // Linking: the link window polls until approved, and the key is stored for this arcade.
            var start = await client.StartLinkAsync(ArcadiaDeviceName());
            var linkWindow = new LinkWindow(this, client, start);
            bool? linked = linkWindow.ShowDialog();
            Expect(linked == true && linkWindow.Key == "arcadia_pk_smoke" && linkWindow.Creator?.Name == "Smoke Studio", "the link window waits through 'pending' and returns the key");
            editor.StoreArcadiaKey(client.Host, linkWindow.Key);
            Expect(editor.ArcadiaKey(client.Host) == "arcadia_pk_smoke" && !File.ReadAllText(PreferencesPath).Contains("arcadia_pk_smoke"), "the key is stored, and only encrypted");
            await Guard(Refresh);
            Expect(account.Text.Contains("Smoke Studio") && mode.Text.Contains("moderator") && publish.IsEnabled, "the account shows who, that uploads are reviewed, and Publish turns on");

            // The form, a screenshot, a leaderboard.
            title.Text = "Smoke"; description.Text = "A smoke test."; genres[0].Text = "Arcade"; controls.Text = "Click"; version.Text = "1.0.0";
            var tiny = new System.Windows.Media.Imaging.WriteableBitmap(64, 40, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(tiny)); using (var stream = new MemoryStream()) { encoder.Save(stream); (screenshot, screenshotType) = FitScreenshot(stream.ToArray()); }
            ShowPicture();
            Expect(ArcadiaPackage.ImageSize(screenshot) == (1280, 800) && screenshotType == "png", "a small picture is fitted to 1280 × 800");
            leaderboard.IsChecked = false;

            // Check: local rules, then the arcade's dry run.
            await Guard(Check);
            Expect(calls.Any(c => c.StartsWith("POST /api/publish/check [key] application/zip")) && status.Text.Contains("moderator") && findings.Items.Count > 0, "Check sends the zip and shows the arcade's findings");

            // Publish: processing, then live; the game ID is kept in the project.
            await Guard(Publish);
            Expect(status.Text.Contains("is live") && open.Visibility == Visibility.Visible && openUrl.Contains("/#/play/c-smoke"), "Publish ends on live, with Open in browser");
            Expect(editor.project.Publishing.Arcades.TryGetValue(client.Host, out var game) && game.GameId == "c-smoke" && game.LastVersion == "1.0.0" && editor.RememberedGameId(client.Host) == "c-smoke", "the game ID and version are kept");
            Expect(version.Text == "1.0.1", "the next version is offered");
            Expect(calls.Count(c => c.StartsWith("POST /api/publish/games ")) == 1, "the first upload is a new game");

            // Publishing again updates the same game.
            await Guard(Publish);
            Expect(calls.Any(c => c.StartsWith("POST /api/publish/games/c-smoke [key]")), "publishing again goes to the same game");

            await Guard(Check);
            Expect(calls.Any(c => c.StartsWith("POST /api/publish/games/c-smoke [key]")) && calls.Last(c => c.Contains("/check")).Contains("/check?game=c-smoke"), "checking an update names the game (check?game=)");

            // Deleted on the arcade (Check or Publish): asked first. No changes nothing. Yes forgets the old ID and starts
            // the version again at 1.0.0, keeping the title, description, screenshot and leaderboard.
            deleteOnArcade(); string asked = ""; var keptShot = screenshot;
            Confirm = q => { asked = q; return false; };
            await Guard(Check);
            Expect(asked.StartsWith("You deleted Smoke on Arcadia") && asked.Contains("version starts again at 1.0.0") && editor.RememberedGameId(client.Host) == "c-smoke" && status.Text.StartsWith("Nothing was published"), "Check on a deleted game asks, and No changes nothing");
            asked = ""; await Guard(Publish);
            Expect(asked.StartsWith("You deleted Smoke on Arcadia") && editor.RememberedGameId(client.Host) == "c-smoke" && calls.Count(c => c.StartsWith("POST /api/publish/games ")) == 1, "Publish on a deleted game asks, and No uploads nothing");
            Confirm = q => { asked = q; return true; };
            await Guard(Publish);
            var saved = editor.project.Publishing;
            Expect(calls.Count(c => c.StartsWith("POST /api/publish/games ")) == 2 && editor.RememberedGameId(client.Host) == "c-smoke2" && saved.Arcades[client.Host].LastVersion == "1.0.0" && status.Text.Contains("is live"), "Yes publishes it as a new game at 1.0.0 and keeps the new ID");
            Expect(saved.Title == "Smoke" && saved.Description == "A smoke test." && saved.Controls == "Click" && saved.Genre.SequenceEqual(["Arcade"]) && ReferenceEquals(saved.Screenshot, keptShot) && version.Text == "1.0.1", "the details and screenshot are kept");

            // A project that lost its ID, whose title is already one of their games: offered to update that game.
            editor.ForgetGame(client.Host); takeTitle();
            await Guard(Publish);
            Expect(asked.StartsWith("You already have a game called Smoke") && editor.RememberedGameId(client.Host) == "c-smoke2" && calls.Any(c => c.StartsWith("POST /api/publish/games/c-smoke2 [key]")), "title-taken: Yes updates the existing game of that title");
            Confirm = null;

            // A revoked key: forgotten, and the window says to link again.
            editor.StoreArcadiaKey(client.Host, "arcadia_pk_revoked"); await Guard(Refresh);
            Expect(key == null && status.Text.Contains("isn't linked to Arcadia anymore") && editor.ArcadiaKey(client.Host) == null, "a revoked key is forgotten and the window offers to link again");
            Expect(calls.All(c => !c.Contains("arcadia_pk")), "the key never appears in an address");
            return "PASS: Publish to Arcadia window against a fake arcade: link (pending → approved), encrypted key, account and review mode, screenshot fitting, Check, Publish (processing → live, game ID kept, next version), update to the same game (check?game=), a game deleted on the arcade (Check and Publish; No / Yes → new game at 1.0.0 with its details kept), a title already taken, revoked key.";
        }
    }
}
