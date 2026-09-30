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
using Wysicraft.Models;
using Wysicraft.Packaging;
namespace Wysicraft.Designer;

// --smoke-publish: the Publish to Arcadia window against a fake arcade on this computer. Linking (without a browser),
// the account, Check, Publish (processing → live, the game ID kept), publishing again to the same game, and a revoked
// key. Nothing reaches a real arcade, and the preferences it touches are put back.
public partial class MainWindow
{
    internal async Task VerifyPublishingAsync(string output)
    {
        var calls = new List<string>(); int polls = 0, statusPolls = 0, newGames = 0; bool revoked = false, deleted = false, titleTaken = false; byte[] lastUpload = [];
        // Details edited on the website: the check reports a clash, uploads say whose details were used, the game
        // sends Arcadia's details, and its pictures can be downloaded. One screenshot is one the project already has.
        bool clash = false;
        static byte[] Png(int width, int height, byte shade)
        {
            var bitmap = new System.Windows.Media.Imaging.WriteableBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
            var pixels = new byte[width * height * 4]; for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = pixels[i + 1] = pixels[i + 2] = shade; pixels[i + 3] = 255; }
            bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
        }
        byte[] mediaCover = Png(1280, 800, 40), mediaShot = Png(1280, 800, 90);
        JsonObject ArcadeBlock(string host, string title) => new()
        {
            ["title"] = title, ["description"] = "Eat every dot and dodge the goblins.", ["genre"] = new JsonArray("Arcade", "Maze"), ["controls"] = "Arrows or WASD to move.",
            ["mobile"] = false, ["videos"] = new JsonArray("https://youtu.be/dQw4w9WgXcQ"),
            ["cover"] = new JsonObject { ["url"] = "http://" + host + "/media/cover.png", ["sha"] = ArcadiaDetailsMerge.GitSha(mediaCover) },
            ["screenshots"] = new JsonArray(
                new JsonObject { ["url"] = "http://" + host + "/media/keep.png", ["sha"] = ArcadiaDetailsMerge.GitSha(project.Publishing.Screenshots[0].Bytes) },
                new JsonObject { ["url"] = "http://" + host + "/media/shot.png", ["sha"] = ArcadiaDetailsMerge.GitSha(mediaShot) }),
            ["scores"] = new JsonObject { ["label"] = "Dots", ["format"] = "points", ["order"] = "desc", ["min"] = 0, ["max"] = 99999, ["stats"] = new JsonArray(), ["watch"] = new JsonObject { ["score"] = new JsonObject { ["variable"] = "dots" }, ["trigger"] = new JsonArray(new JsonObject { ["variable"] = "over", ["equals"] = "true" }), ["round"] = "floor" } }
        };
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
                    if (clash && http.Request.Query["game"].Count > 0)
                    {
                        var details = new JsonObject { ["conflict"] = true, ["changed"] = new JsonArray("title", "description", "screenshots", "scores"), ["editedAt"] = 1790812345678, ["editedBy"] = "creator", ["arcade"] = ArcadeBlock(http.Request.Host.Value!, "Goblin Pac Deluxe") };
                        await Send(200, new JsonObject { ["ok"] = true, ["wouldHold"] = false, ["findings"] = new JsonArray(), ["details"] = details }.ToJsonString()); return;
                    }
                    await Send(200, "{\"ok\":true,\"wouldHold\":true,\"findings\":[{\"level\":\"hold\",\"code\":\"new-runtime\",\"message\":\"A new Arcadia Studio runtime build\",\"file\":\"wysicraft/wysicraft-web.js\"}]}"); return;
                case "POST /api/publish/games":
                    // A different game of theirs already has this title.
                    if (titleTaken) { titleTaken = false; await Send(409, "{\"error\":\"You already have a game called Smoke.\",\"code\":\"title-taken\",\"gameId\":\"c-smoke2\"}"); return; }
                    lastUpload = body.ToArray();
                    string newId = ++newGames > 1 ? "c-smoke" + newGames : "c-smoke";
                    await Send(202, "{\"game\":{\"id\":\"" + newId + "\",\"title\":\"Smoke\",\"url\":\"GAMEURL\"},\"submission\":" + submission("processing") + "}"); return;
                case var update when update.StartsWith("POST /api/publish/games/"):
                    // Deleted on the arcade: updates are refused with their own code.
                    string id = path[(path.LastIndexOf('/') + 1)..];
                    if (deleted && id == "c-smoke") { await Send(404, "{\"error\":\"You deleted this game on Arcadia.\",\"code\":\"deleted\"}"); return; }
                    string used = clash && http.Request.Query["overwrite"] != "true" ? ",\"details\":{\"used\":\"arcade\",\"kept\":[\"title\",\"description\",\"screenshots\",\"scores\"]}" : ",\"details\":{\"used\":\"app\"}";
                    await Send(202, "{\"game\":{\"id\":\"" + id + "\",\"title\":\"Smoke\",\"url\":\"GAMEURL\"},\"submission\":" + submission("processing") + used + "}"); return;
                case var one when one.StartsWith("GET /api/publish/games/") && !one.EndsWith("/download"):
                    await Send(200, new JsonObject { ["id"] = path[(path.LastIndexOf('/') + 1)..], ["title"] = "Goblin Pac Remix", ["status"] = "live", ["url"] = "GAMEURL", ["submissions"] = new JsonArray(), ["arcade"] = ArcadeBlock(http.Request.Host.Value!, "Goblin Pac Remix"), ["editedAt"] = 1790812345678, ["editedBy"] = "mod" }.ToJsonString()); return;
                case "GET /media/cover.png": case "GET /media/shot.png": case "GET /media/keep.png":
                    http.Response.ContentType = "image/png"; await http.Response.Body.WriteAsync(path.EndsWith("cover.png") ? mediaCover : mediaShot); return;
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
            string result = await dialog.SelfTest(calls, () => deleted = true, () => titleTaken = true, () => lastUpload, () => clash = true, mediaCover, mediaShot);
            // A picture of the window as it ended, scrolled to the screenshots and videos, beside the result, for a look at the layout.
            dialog.ShowGallery(); dialog.UpdateLayout();
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
        internal void ShowGallery() { UpdateLayout(); shots.BringIntoView(); }
        async Task Settle() { for (int i = 0; i < 400 && busy; i++) await Task.Delay(25); await Task.Delay(50); }
        internal async Task<string> SelfTest(List<string> calls, Action deleteOnArcade, Action takeTitle, Func<byte[]> lastUpload, Action editOnWebsite, byte[] mediaCover, byte[] mediaShot)
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

            // The form, a cover, screenshots, videos, phones, no leaderboard.
            title.Text = "Smoke"; description.Text = "A smoke test."; genres[0].Text = "Arcade"; controls.Text = "Click"; version.Text = "1.0.0";
            static byte[] Picture(int width, int height)
            {
                var bitmap = new System.Windows.Media.Imaging.WriteableBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
            }
            (cover, coverType) = FitScreenshot(Picture(64, 40));
            ShowPicture();
            Expect(ArcadiaPackage.ImageSize(cover) == (1280, 800) && coverType == "png", "a small picture is fitted to 1280 × 800 for the cover");
            // Two screenshots told apart by size; the second is moved first with its ◀ button, as a person would.
            byte[] first = Picture(1280, 800), second = Picture(640, 400);
            gallery.Add(new PublishImage { Type = "png", Bytes = first }); gallery.Add(new PublishImage { Type = "png", Bytes = second }); ShowShots();
            Button Arrow(int card, string text) => ((StackPanel)((DockPanel)((StackPanel)shots.Children[card]).Children[1]).Children[0]).Children.OfType<Button>().First(b => (string)b.Content == text);
            Expect(shots.Children.Count == 2 && !Arrow(0, "◀").IsEnabled && !Arrow(1, "▶").IsEnabled && shotsInfo.Text == "2 of 8.", "the screenshots show as two cards, with the ends' arrows off");
            Arrow(1, "◀").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Expect(ReferenceEquals(gallery[0].Bytes, second) && ReferenceEquals(gallery[1].Bytes, first), "◀ moves a screenshot earlier");
            mobile.IsChecked = true;
            videos[0].Text = "https://youtu.be/aBcDeFgHiJk"; videos[1].Text = "https://vimeo.com/12345";
            Expect(videoNote.Visibility == Visibility.Visible && videoNote.Text.StartsWith("Link 2 isn't"), "a link that isn't YouTube is flagged as it's typed");
            leaderboard.IsChecked = false;
            await Guard(Check);
            Expect(!calls.Any(c => c.Contains("/api/publish/check")) && findings.Items.OfType<ListBoxItem>().Any(i => i.Tag is ArcadiaFinding { Level: "block", Code: "videos" }), "Check refuses a link that isn't YouTube before anything is sent");
            videos[1].Text = "https://www.youtube.com/shorts/aBcDeFgHiJk";
            Expect(videoNote.Visibility == Visibility.Collapsed, "and the note goes once it's a YouTube link");

            // Check: local rules, then the arcade's dry run.
            await Guard(Check);
            Expect(calls.Any(c => c.StartsWith("POST /api/publish/check [key] application/zip")) && status.Text.Contains("moderator") && findings.Items.Count > 0, "Check sends the zip and shows the arcade's findings");

            // Publish: processing, then live; the game ID is kept in the project.
            await Guard(Publish);
            Expect(status.Text.Contains("is live") && open.Visibility == Visibility.Visible && openUrl.Contains("/#/play/c-smoke"), "Publish ends on live, with Open in browser");
            Expect(editor.project.Publishing.Arcades.TryGetValue(client.Host, out var game) && game.GameId == "c-smoke" && game.LastVersion == "1.0.0" && editor.RememberedGameId(client.Host) == "c-smoke", "the game ID and version are kept");
            Expect(version.Text == "1.0.1", "the next version is offered");
            Expect(calls.Count(c => c.StartsWith("POST /api/publish/games ")) == 1, "the first upload is a new game");
            using (var sent = new System.IO.Compression.ZipArchive(new MemoryStream(lastUpload())))
            {
                var sentGame = JsonNode.Parse(new StreamReader(sent.GetEntry("game.json")!.Open()).ReadToEnd())!;
                var shotsSent = sentGame["screenshots"]!.AsArray().Select(n => (string)n!).ToList();
                byte[] Entry(string name) { using var e = sent.GetEntry(name)!.Open(); using var m = new MemoryStream(); e.CopyTo(m); return m.ToArray(); }
                Expect((string?)sentGame["cover"] == "cover.png" && sentGame["screenshot"] == null && sent.GetEntry("cover.png") != null, "the upload has the cover as cover.png");
                Expect(shotsSent.SequenceEqual(["screenshots/1.png", "screenshots/2.png"]) && ArcadiaPackage.ImageSize(Entry("screenshots/1.png")) == (640, 400) && ArcadiaPackage.ImageSize(Entry("screenshots/2.png")) == (1280, 800), "the screenshots go in the gallery order chosen");
                Expect(sentGame["videos"]!.AsArray().Select(n => (string)n!).SequenceEqual(["https://youtu.be/aBcDeFgHiJk", "https://www.youtube.com/shorts/aBcDeFgHiJk"]) && !sent.Entries.Any(e => e.FullName.EndsWith(".mp4")), "the video links are listed, and no video file is sent");
                Expect(sentGame["mobile"]?.GetValue<bool>() == true, "mobile: true when Plays on phones and tablets is ticked");
            }
            Expect(editor.project.Publishing.Screenshots.Count == 2 && editor.project.Publishing.Videos.Count == 2 && editor.project.Publishing.Mobile, "the screenshots, videos and phone setting are kept in the project");

            // Publishing again updates the same game.
            await Guard(Publish);
            Expect(calls.Any(c => c.StartsWith("POST /api/publish/games/c-smoke [key]")), "publishing again goes to the same game");

            await Guard(Check);
            Expect(calls.Any(c => c.StartsWith("POST /api/publish/games/c-smoke [key]")) && calls.Last(c => c.Contains("/check")).Contains("/check?game=c-smoke"), "checking an update names the game (check?game=)");

            // Deleted on the arcade (Check or Publish): asked first. No changes nothing. Yes forgets the old ID and starts
            // the version again at 1.0.0, keeping the title, description, cover, screenshots, videos and leaderboard.
            deleteOnArcade(); string asked = ""; var keptShot = cover;
            Confirm = q => { asked = q; return false; };
            await Guard(Check);
            Expect(asked.StartsWith("You deleted Smoke on Arcadia") && asked.Contains("version starts again at 1.0.0") && editor.RememberedGameId(client.Host) == "c-smoke" && status.Text.StartsWith("Nothing was published"), "Check on a deleted game asks, and No changes nothing");
            asked = ""; await Guard(Publish);
            Expect(asked.StartsWith("You deleted Smoke on Arcadia") && editor.RememberedGameId(client.Host) == "c-smoke" && calls.Count(c => c.StartsWith("POST /api/publish/games ")) == 1, "Publish on a deleted game asks, and No uploads nothing");
            Confirm = q => { asked = q; return true; };
            await Guard(Publish);
            var saved = editor.project.Publishing;
            Expect(calls.Count(c => c.StartsWith("POST /api/publish/games ")) == 2 && editor.RememberedGameId(client.Host) == "c-smoke2" && saved.Arcades[client.Host].LastVersion == "1.0.0" && status.Text.Contains("is live"), "Yes publishes it as a new game at 1.0.0 and keeps the new ID");
            Expect(saved.Title == "Smoke" && saved.Description == "A smoke test." && saved.Controls == "Click" && saved.Genre.SequenceEqual(["Arcade"]) && ReferenceEquals(saved.Cover, keptShot) && saved.Screenshots.Count == 2 && saved.Videos.Count == 2 && version.Text == "1.0.1", "the details, cover, screenshots and videos are kept");

            // A project that lost its ID, whose title is already one of their games: offered to update that game.
            editor.ForgetGame(client.Host); takeTitle();
            await Guard(Publish);
            Expect(asked.StartsWith("You already have a game called Smoke") && editor.RememberedGameId(client.Host) == "c-smoke2" && calls.Any(c => c.StartsWith("POST /api/publish/games/c-smoke2 [key]")), "title-taken: Yes updates the existing game of that title");
            Confirm = null;

            // Details edited on the website since the last publish: the update is checked first, and the person chooses.
            editOnWebsite(); ArcadiaDetails? seen = null; int uploadsBefore = calls.Count(c => c.StartsWith("POST /api/publish/games/"));
            var projectShot = editor.project.Publishing.Screenshots[0].Bytes;
            ChooseDetails = d => { seen = d; return null; };
            await Guard(Publish);
            Expect(seen != null && seen.Changed.SequenceEqual(["title", "description", "screenshots", "scores"]) && seen.EditedBy == "creator" && seen.Arcade?.Title == "Goblin Pac Deluxe", "a clash is found before uploading, with what changed and Arcadia's values");
            Expect(status.Text.StartsWith("Nothing was published") && calls.Count(c => c.StartsWith("POST /api/publish/games/")) == uploadsBefore, "Cancel uploads nothing");
            ChooseDetails = _ => true;
            await Guard(Publish);
            Expect(calls.Last(c => c.StartsWith("POST /api/publish/games/")).Contains("?overwrite=true") && editor.project.Publishing.Title == "Smoke", "Use mine uploads with overwrite=true and leaves the project's details alone");
            ChooseDetails = _ => false;
            await Guard(Publish);
            var took = editor.project.Publishing;
            Expect(calls.Last(c => c.StartsWith("POST /api/publish/games/")).Contains("?overwrite=false"), "Keep Arcadia's uploads with overwrite=false");
            Expect(took.Title == "Goblin Pac Deluxe" && took.Description == "Eat every dot and dodge the goblins." && took.Genre.SequenceEqual(["Arcade", "Maze"]) && took.Controls == "Arrows or WASD to move." && !took.Mobile && took.Videos.SequenceEqual(["https://youtu.be/dQw4w9WgXcQ"]), "and copies Arcadia's details into the project");
            Expect(took.Cover.SequenceEqual(mediaCover) && took.Screenshots.Count == 2 && ReferenceEquals(took.Screenshots[0].Bytes, projectShot) && took.Screenshots[1].Bytes.SequenceEqual(mediaShot), "with Arcadia's cover and screenshots, reusing the one the project already had");
            Expect(!calls.Any(c => c.StartsWith("GET /media/keep.png")) && calls.Any(c => c.StartsWith("GET /media/cover.png [key]")), "a picture the project already has isn't downloaded again");
            Expect(took.Leaderboard && took.Scores.Label == "Dots" && took.Scores.Score.Variable == "dots" && took.Scores.Triggers.Count == 1 && took.Scores.Triggers[0].EqualsValue == "true" && took.Scores.Max == 99999, "and Arcadia's leaderboard setup");
            Expect(title.Text == "Goblin Pac Deluxe" && gallery.Count == 2 && status.Text.Contains("Kept Arcadia's title, description, screenshots, leaderboard"), "the form shows them, and the status says what was kept: " + status.Text);
            ChooseDetails = null;
            await Guard(LoadDetails);
            Expect(title.Text == "Goblin Pac Remix" && editor.project.Publishing.Title == "Goblin Pac Remix" && status.Text.StartsWith("Loaded the details from Arcadia"), "Load details from Arcadia takes what's on Arcadia now");
            bool refused = false; int before = calls.Count;
            try { await client.MediaAsync(key!, "https://elsewhere.example/steal.png"); } catch (ArcadiaException ex) when (ex.Code == "media") { refused = true; }
            Expect(refused && calls.Count == before, "a picture address outside the arcade is never fetched, so the key goes nowhere else");

            // A revoked key: forgotten, and the window says to link again.
            editor.StoreArcadiaKey(client.Host, "arcadia_pk_revoked"); await Guard(Refresh);
            Expect(key == null && status.Text.Contains("isn't linked to Arcadia anymore") && editor.ArcadiaKey(client.Host) == null, "a revoked key is forgotten and the window offers to link again");
            Expect(calls.All(c => !c.Contains("arcadia_pk")), "the key never appears in an address");
            // The arcade at a new address: the old address's link and game carry over; forgetting clears both.
            FormerHosts[client.Host] = "old.arcade.test";
            try
            {
                editor.StoreArcadiaKey("old.arcade.test", "arcadia_pk_smoke"); editor.ForgetGame(client.Host);
                editor.project.Publishing.Arcades["old.arcade.test"] = new Wysicraft.Models.ArcadeGame { GameId = "c-old", LastVersion = "2.0.0" };
                Expect(editor.ArcadiaKey(client.Host) == "arcadia_pk_smoke" && editor.Prefs().ArcadiaKeys.ContainsKey(client.Host), "the link saved under the old address carries over to the new one");
                Expect(editor.RememberedGameId(client.Host) == "c-old" && editor.SavedGame(client.Host)?.LastVersion == "2.0.0", "a game saved under the old address is the same game at the new one");
                editor.ForgetGame(client.Host);
                Expect(editor.RememberedGameId(client.Host) == null && !editor.project.Publishing.Arcades.ContainsKey("old.arcade.test"), "forgetting the game clears both addresses");
                editor.ForgetArcadiaKey(client.Host);
                Expect(editor.ArcadiaKey(client.Host) == null && !editor.Prefs().ArcadiaKeys.ContainsKey("old.arcade.test"), "unlinking clears both addresses, so an old key can't come back");
            }
            finally { FormerHosts.Remove(client.Host); }
            return "PASS: Publish to Arcadia window against a fake arcade: link (pending → approved), encrypted key, account and review mode, cover fitting, screenshots (reorder, order sent), YouTube-only video links (flagged as typed, refused before sending), mobile, Check, Publish (processing → live, game ID kept, next version), update to the same game (check?game=), a game deleted on the arcade (Check and Publish; No / Yes → new game at 1.0.0 with its details kept), a title already taken, revoked key, an arcade at a new address (link and game carried over; forgetting clears both), details edited on the website (checked before an update; Cancel, Use mine = overwrite=true, Keep Arcadia's = overwrite=false and copied into the project with its pictures, an unchanged picture not downloaded again), Load details from Arcadia, pictures only from the arcade.";
        }
    }
}
