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
/// <summary>The fake arcade taking its time: how many more looks at an upload answer "processing", how many get no
/// answer at all (503), and an upload held back until it is let go (to cancel one on its way).</summary>
internal sealed class SlowArcade { public int Processing, Unanswered; public TaskCompletionSource? Hold; }

public partial class MainWindow
{
    internal async Task VerifyPublishingAsync(string output)
    {
        var calls = new List<string>(); int polls = 0, statusPolls = 0, newGames = 0; bool revoked = false, deleted = false, titleTaken = false; byte[] lastUpload = [];
        // Details edited on the website: the check reports a clash, uploads say whose details were used, the game
        // sends Arcadia's details, and its pictures can be downloaded. One screenshot is one the project already has.
        bool clash = false;
        // The leaderboard turned off on the website: the arcade's details then say "scores": false.
        bool boardOff = false;
        // The arcade failing to store the next upload (its submission ends "failed"), and the arcade being down.
        bool failNext = false, arcadeDown = false;
        // The arcade taking its time over an upload (see SlowArcade).
        var slow = new SlowArcade();
        static bool UploadHasBoard(byte[] zip)
        {
            using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(zip));
            using var reader = new StreamReader(archive.GetEntry("game.json")!.Open());
            return JsonNode.Parse(reader.ReadToEnd())!["scores"] is JsonObject;
        }
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
            ["scores"] = boardOff ? JsonValue.Create(false) : new JsonObject { ["label"] = "Dots", ["format"] = "points", ["order"] = "desc", ["min"] = 0, ["max"] = 99999, ["stats"] = new JsonArray(), ["watch"] = new JsonObject { ["score"] = new JsonObject { ["variable"] = "dots" }, ["trigger"] = new JsonArray(new JsonObject { ["variable"] = "over", ["equals"] = "true" }), ["round"] = "floor" } }
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
                    if (arcadeDown) { await Send(503, "{\"error\":\"The arcade is restarting.\",\"code\":\"unavailable\"}"); return; }
                    if (deleted && http.Request.Query["game"] == "c-smoke") { await Send(404, "{\"error\":\"You deleted this game on Arcadia.\",\"code\":\"deleted\"}"); return; }
                    if (clash && http.Request.Query["game"].Count > 0)
                    {
                        // "scores" is listed only when the upload and the website disagree about having a leaderboard.
                        var changed = boardOff ? new JsonArray() : new JsonArray("title", "description", "screenshots");
                        if (UploadHasBoard(body.ToArray()) == boardOff) changed.Add("scores");
                        var details = new JsonObject { ["conflict"] = changed.Count > 0, ["changed"] = changed, ["editedAt"] = 1790812345678, ["editedBy"] = "creator", ["arcade"] = ArcadeBlock(http.Request.Host.Value!, boardOff ? "Goblin Pac Remix" : "Goblin Pac Deluxe") };
                        await Send(200, new JsonObject { ["ok"] = true, ["wouldHold"] = false, ["findings"] = new JsonArray(), ["details"] = details }.ToJsonString()); return;
                    }
                    await Send(200, "{\"ok\":true,\"wouldHold\":true,\"findings\":[{\"level\":\"hold\",\"code\":\"new-runtime\",\"message\":\"A new Arcadia Studio runtime build\",\"file\":\"wysicraft/wysicraft-web.js\"}]}"); return;
                case "POST /api/publish/games":
                    // A different game of theirs already has this title.
                    if (slow.Hold is { } heldNew) { await heldNew.Task; if (http.RequestAborted.IsCancellationRequested) return; }
                    if (titleTaken) { titleTaken = false; await Send(409, "{\"error\":\"You already have a game called Smoke.\",\"code\":\"title-taken\",\"gameId\":\"c-smoke2\"}"); return; }
                    lastUpload = body.ToArray();
                    string newId = ++newGames > 1 ? "c-smoke" + newGames : "c-smoke";
                    await Send(202, "{\"game\":{\"id\":\"" + newId + "\",\"title\":\"Smoke\",\"url\":\"GAMEURL\"},\"submission\":" + submission("processing") + "}"); return;
                case var update when update.StartsWith("POST /api/publish/games/"):
                    // Deleted on the arcade: updates are refused with their own code.
                    string id = path[(path.LastIndexOf('/') + 1)..];
                    if (slow.Hold is { } heldUpdate) { await heldUpdate.Task; if (http.RequestAborted.IsCancellationRequested) return; }
                    if (deleted && id == "c-smoke") { await Send(404, "{\"error\":\"You deleted this game on Arcadia.\",\"code\":\"deleted\"}"); return; }
                    lastUpload = body.ToArray();
                    string used = clash && http.Request.Query["overwrite"] != "true" ? ",\"details\":{\"used\":\"arcade\",\"kept\":[" + (boardOff ? "" : "\"title\",\"description\",\"screenshots\",") + "\"scores\"]}" : ",\"details\":{\"used\":\"app\"}";
                    await Send(202, "{\"game\":{\"id\":\"" + id + "\",\"title\":\"Smoke\",\"url\":\"GAMEURL\"},\"submission\":" + submission("processing") + used + "}"); return;
                case var one when one.StartsWith("GET /api/publish/games/") && !one.EndsWith("/download"):
                    await Send(200, new JsonObject { ["id"] = path[(path.LastIndexOf('/') + 1)..], ["title"] = "Goblin Pac Remix", ["status"] = "live", ["url"] = "GAMEURL", ["submissions"] = new JsonArray(), ["arcade"] = ArcadeBlock(http.Request.Host.Value!, "Goblin Pac Remix"), ["editedAt"] = 1790812345678, ["editedBy"] = "mod" }.ToJsonString()); return;
                case "GET /media/cover.png": case "GET /media/shot.png": case "GET /media/keep.png":
                    http.Response.ContentType = "image/png"; await http.Response.Body.WriteAsync(path.EndsWith("cover.png") ? mediaCover : mediaShot); return;
                case "GET /api/publish/submissions/7":
                    if (slow.Unanswered > 0) { slow.Unanswered--; await Send(503, "{\"error\":\"The arcade is restarting.\",\"code\":\"unavailable\"}"); return; }
                    if (slow.Processing > 0) { slow.Processing--; await Send(200, submission("processing")); return; }
                    if (failNext) { failNext = false; await Send(200, submission("failed").Replace("\"message\":null", "\"message\":\"Something went wrong on the arcade's side (GitHub POST /repos/x/y/git/trees failed (422): Sorry, your request timed out.). It doesn't count against your limits; please upload again.\"")); return; }
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
            dialog.PromptPictures = output;
            string result = await dialog.SelfTest(calls, () => deleted = true, () => titleTaken = true, () => lastUpload, () => clash = true, on => boardOff = !on, () => failNext = true, down => arcadeDown = down, slow, mediaCover, mediaShot);
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
        /// <summary>Where pictures of the details prompt go (the result's path; ".remove.png" and ".turnon.png" are added).</summary>
        internal string PromptPictures = "";
        internal void ShowGallery() { UpdateLayout(); shots.BringIntoView(); }
        async Task Settle() { for (int i = 0; i < 400 && busy; i++) await Task.Delay(25); await Task.Delay(50); }
        internal async Task<string> SelfTest(List<string> calls, Action deleteOnArcade, Action takeTitle, Func<byte[]> lastUpload, Action editOnWebsite, Action<bool> boardOnWebsite, Action failNextUpload, Action<bool> arcadeIsDown, SlowArcade slow, byte[] mediaCover, byte[] mediaShot)
        {
            void Expect(bool ok, string what) { if (!ok) throw new Exception("Publish window: " + what + "\nStatus: " + status.Text + "\nAccount: " + account.Text + "\nCalls:\n" + string.Join("\n", calls)); }
            await Settle();
            Expect(key == null && account.Text.StartsWith("Not linked") && (string)link.Content == "Link to Arcadia…" && !publish.IsEnabled, "starts unlinked, with Publish off");
            Expect(loadDetails.Visibility == Visibility.Collapsed && open.Visibility == Visibility.Collapsed, "with nothing uploaded yet there is no Load details from Arcadia and no Open in browser");

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

            // The arcade is down while checking: a pop-up, with Try again because that can pass.
            var problems = new List<(string Text, bool CanRetry)>();
            arcadeIsDown(true); ProblemForTest = (text, canRetry) => { problems.Add((text, canRetry)); return false; };
            await Guard(Check, popUp: true);
            Expect(problems.Count == 1 && problems[0].CanRetry && problems[0].Text.Contains("restarting") && status.Text.Contains("restarting"), "a Check the arcade can't answer shows a pop-up that offers Try again: " + (problems.Count > 0 ? problems[0].Text : "none"));
            // Try again runs the same check; once the arcade is back it goes through with no second pop-up.
            problems.Clear(); ProblemForTest = (text, canRetry) => { problems.Add((text, canRetry)); arcadeIsDown(false); return true; };
            arcadeIsDown(true); await Guard(Check, popUp: true);
            Expect(problems.Count == 1 && status.Text.Contains("moderator") && findings.Items.Count > 0, "Try again runs the check again, and it goes through: " + status.Text);

            // The arcade fails to store the very first upload: a pop-up with what it said and Try again. Nothing is
            // counted as sent (no version used, no Open in browser, no details on Arcadia to load); the game's ID is kept.
            // This one goes through the real pop-up: its buttons are read, a picture is taken, and it is closed.
            failNextUpload(); ProblemForTest = null; string popUpButtons = ""; LastProblem = null;
            ProblemWindowForTest = w => { popUpButtons = string.Join(" | ", ((StackPanel)w.Content).Children.OfType<StackPanel>().SelectMany(b => b.Children.OfType<Button>()).Select(b => (string)b.Content)); MainWindow.CaptureWindow(w, PromptPictures + ".problem.png"); w.Close(); };
            await Guard(Publish, popUp: true);
            ProblemWindowForTest = null;
            Expect(popUpButtons == "Try again | Close" && LastProblem != null && LastProblem.StartsWith("The upload didn't go through") && LastProblem.Contains("GitHub POST") && LastProblem.Contains("didn't use up"), "a failed upload shows a pop-up with what the arcade said, Try again and Close: " + popUpButtons + " / " + LastProblem);
            Expect(status.Text.StartsWith("Not published") && !status.Text.Contains("Something went wrong on the arcade's side. Something"), "the status line says it once: " + status.Text);
            Expect(open.Visibility == Visibility.Collapsed && loadDetails.Visibility == Visibility.Collapsed, "after a failed upload there is no Open in browser and no Load details from Arcadia");
            Expect(version.Text == "1.0.0" && editor.RememberedGameId(client.Host) == "c-smoke" && editor.SavedGame(client.Host)!.LastVersion == "", "the version isn't used up, and the game's ID is kept for the next try: " + version.Text + " / " + editor.SavedGame(client.Host)!.LastVersion);
            // Try again from the pop-up: the same version is sent again, to the same game, and goes live.
            problems.Clear(); failNextUpload(); ProblemForTest = (text, canRetry) => { problems.Add((text, canRetry)); return true; };
            await Guard(Publish, popUp: true);
            Expect(problems.Count == 1 && calls.Count(c => c.StartsWith("POST /api/publish/games/c-smoke [key]")) == 2, "Try again uploads again without another click: " + problems.Count + " pop-ups, " + calls.Count(c => c.StartsWith("POST /api/publish/games/c-smoke [key]")) + " uploads to the game");
            ProblemForTest = null;
            Expect(loadDetails.Visibility == Visibility.Visible, "once it has gone through, Load details from Arcadia is offered");

            // Publish: processing, then live; the game ID is kept in the project.
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
                Expect(sentGame.AsObject().ContainsKey("scores") && sentGame["scores"] is JsonValue noBoard && noBoard.TryGetValue<bool>(out bool keeps) && !keeps && sentGame["leaderboardPage"] == null, "Keep a leaderboard off is sent as \"scores\": false (not left out), with no leaderboard page");
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
            // The prompt itself, as a person sees it (closed as Cancel), with the leaderboard line in it.
            string PromptText(string picture)
            {
                string text = ""; var choose = ChooseDetails; ChooseDetails = null;
                DetailsPromptForTest = w => { text = string.Join("\n", ((StackPanel)w.Content).Children.OfType<TextBlock>().Select(t => t.Text)) + "\n" + string.Join(" | ", ((StackPanel)w.Content).Children.OfType<StackPanel>().SelectMany(b => b.Children.OfType<Button>()).Select(b => (string)b.Content)); MainWindow.SaveWindowPicture(w, PromptPictures + picture); };
                try { Expect(AskDetails(seen!) == null, "the prompt closed as Cancel"); } finally { DetailsPromptForTest = null; ChooseDetails = choose; }
                return text;
            }
            string removePrompt = PromptText(".remove.png");
            Expect(removePrompt.Contains("The arcade has a leaderboard set up on the website. Remove it? Use mine removes it. Keep Arcadia's keeps it, and ticks Keep a leaderboard here.") && removePrompt.EndsWith("Keep Arcadia's | Use mine | Cancel"), "the existing keep/overwrite prompt shows the leaderboard question: " + removePrompt);
            Expect(leaderboard.IsChecked == false && seen!.Arcade is { Board: not null, NoLeaderboard: false } && LeaderboardQuestion == "The arcade has a leaderboard set up on the website. Remove it?", "the arcade has a board and the project says none: the prompt asks whether to remove it: " + LeaderboardQuestion);
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
            Expect(leaderboard.IsChecked == true && scoresPanel.Visibility == Visibility.Visible, "and Keep a leaderboard is ticked, since the website's board was kept");
            ChooseDetails = null;
            await Guard(LoadDetails);
            Expect(title.Text == "Goblin Pac Remix" && editor.project.Publishing.Title == "Goblin Pac Remix" && status.Text.StartsWith("Loaded the details from Arcadia"), "Load details from Arcadia takes what's on Arcadia now");

            // The other way round: the leaderboard was turned off on the website, and this project keeps one.
            JsonNode SentGame() { using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(lastUpload())); using var reader = new StreamReader(zip.GetEntry("game.json")!.Open()); return JsonNode.Parse(reader.ReadToEnd())!; }
            boardOnWebsite(false); seen = null; LeaderboardQuestion = null; uploadsBefore = calls.Count(c => c.StartsWith("POST /api/publish/games/"));
            Expect(leaderboard.IsChecked == true, "(the project keeps a leaderboard at this point)");
            ChooseDetails = d => { seen = d; return null; };
            await Guard(Publish);
            Expect(seen != null && seen.Changed.SequenceEqual(["scores"]) && seen.Arcade is { Board: null, NoLeaderboard: true }, "a leaderboard turned off on the website is a clash over \"scores\", with Arcadia's value false: " + status.Text);
            Expect(LeaderboardQuestion == "The leaderboard was turned off on the website. Turn it back on?" && calls.Count(c => c.StartsWith("POST /api/publish/games/")) == uploadsBefore, "the prompt asks whether to turn it back on, and Cancel uploads nothing: " + LeaderboardQuestion);
            string turnOnPrompt = PromptText(".turnon.png");
            Expect(turnOnPrompt.Contains("The leaderboard was turned off on the website. Turn it back on? Use mine turns it back on. Keep Arcadia's leaves it off, and unticks Keep a leaderboard here."), "and the prompt shows the question the other way round: " + turnOnPrompt);
            ChooseDetails = _ => true;
            await Guard(Publish);
            Expect(calls.Last(c => c.StartsWith("POST /api/publish/games/")).Contains("?overwrite=true") && SentGame()["scores"] is JsonObject && leaderboard.IsChecked == true, "Use mine sends the board with overwrite=true, turning it back on: " + status.Text);
            ChooseDetails = _ => false;
            await Guard(Publish);
            Expect(calls.Last(c => c.StartsWith("POST /api/publish/games/")).Contains("?overwrite=false") && leaderboard.IsChecked == false && scoresPanel.Visibility == Visibility.Collapsed && !editor.project.Publishing.Leaderboard && status.Text.Contains("Kept Arcadia's leaderboard"), "Keep Arcadia's leaves it off and unticks Keep a leaderboard: " + status.Text);
            Expect(editor.project.Publishing.Scores.Label == "Dots" && editor.project.Publishing.Scores.Score.Variable == "dots", "the project's leaderboard setup is kept for turning it on again later");
            // Both sides now agree there's none: nothing is asked, and the upload says so on purpose.
            bool askedAgain = false; ChooseDetails = _ => { askedAgain = true; return null; };
            await Guard(Publish);
            Expect(!askedAgain && SentGame()["scores"] is JsonValue stillOff && stillOff.TryGetValue<bool>(out bool stillKeeps) && !stillKeeps && SentGame()["leaderboardPage"] == null, "once both agree there's no leaderboard nothing is asked, and the upload carries \"scores\": false: " + status.Text);
            ChooseDetails = null;
            // Load details from Arcadia follows the website both ways.
            leaderboard.IsChecked = true;
            await Guard(LoadDetails);
            Expect(leaderboard.IsChecked == false && !editor.project.Publishing.Leaderboard, "Load details from Arcadia unticks Keep a leaderboard when it was turned off on the website");
            boardOnWebsite(true);
            await Guard(LoadDetails);
            Expect(leaderboard.IsChecked == true && editor.project.Publishing.Leaderboard && title.Text == "Goblin Pac Remix", "and ticks it again when the website has a board");
            // Settings changed outside the open window (an MCP arcadia_publish, Undo): the window must take them, never
            // write its older form back over them. This is what emptied a publishing setup made while the window was open.
            var outside = Json.Clone(editor.project.Publishing); outside.KeepImages(editor.project.Publishing);
            outside.Title = "Set from MCP"; outside.Description = "Written while the window was open."; outside.Genre = ["Roguelike"];
            editor.project.Publishing = outside;
            await Guard(Check);
            Expect(editor.project.Publishing.Title == "Set from MCP" && editor.project.Publishing.Description == "Written while the window was open." && editor.project.Publishing.Genre.SequenceEqual(["Roguelike"]), "a stale window doesn't write its form over settings changed elsewhere: " + editor.project.Publishing.Title);
            Expect(title.Text == "Set from MCP" && description.Text == "Written while the window was open." && genres[0].Text == "Roguelike", "and shows them instead");
            editor.project.Publishing.Description = "Changed in place.";
            OnActivated(EventArgs.Empty);
            Expect(description.Text == "Changed in place.", "a change made in place (the itch.io hand-over does that) shows when the window is used again");
            bool refused = false; int before = calls.Count;
            try { await client.MediaAsync(key!, "https://elsewhere.example/steal.png"); } catch (ArcadiaException ex) when (ex.Code == "media") { refused = true; }
            Expect(refused && calls.Count == before, "a picture address outside the arcade is never fetched, so the key goes nowhere else");

            // ---- Waiting for the arcade to store an upload ----
            // The wait goes by the number of files. The window says what is happening, Cancel becomes Stop waiting,
            // and a look the arcade doesn't answer neither ends the wait nor offers to upload again.
            var hadChoose = ChooseDetails; ChooseDetails = _ => true; problems.Clear(); ProblemForTest = (text, canRetry) => { problems.Add((text, canRetry)); return false; };
            Expect(CanPublish(out string cannot), "the game can be published for the waiting checks: " + cannot);
            int Uploads() => calls.Count(c => c.StartsWith("POST /api/publish/games"));
            async Task<bool> Until(Func<bool> test, int ms = 8000) { for (int i = 0; i < ms / 10 && !test(); i++) await Task.Delay(10); return test(); }
            PollEvery = TimeSpan.FromMilliseconds(40);
            int counted = -1; StoreWait = n => { counted = n; return ArcadiaPackage.StoreWait(n); };
            slow.Processing = 8; uploadsBefore = Uploads();
            var run = Guard(Publish, popUp: true);
            Expect(await Until(() => onArcade), "the upload reaches the arcade: " + status.Text);
            Expect(cancel.Visibility == Visibility.Visible && (string)cancel.Content == "Stop waiting" && progress.IsIndeterminate && progress.Visibility == Visibility.Visible, "while the arcade stores an upload there is a moving bar, and Cancel is Stop waiting");
            Expect(status.Text.StartsWith("Storing on Arcadia…") && status.Text.Contains(" file") && status.Text.Contains("minutes") && status.Text.Contains("close this window"), "the status says what is happening: " + status.Text);
            Expect(await Until(() => status.Text.Contains("so far")), "and how long it has been: " + status.Text);
            await run;
            Expect(status.Text.Contains("is live") && cancel.Visibility == Visibility.Collapsed && progress.Visibility == Visibility.Collapsed && !progress.IsIndeterminate && Uploads() == uploadsBefore + 1, "it ends live once the arcade is done: " + status.Text);
            using (var sentZip = new System.IO.Compression.ZipArchive(new MemoryStream(lastUpload())))
                Expect(counted > 0 && counted == sentZip.Entries.Count(e => !e.FullName.EndsWith('/')), "the wait is worked out from the files in the upload: " + counted);
            slow.Unanswered = 3; slow.Processing = 2; uploadsBefore = Uploads();
            await Guard(Publish, popUp: true);
            Expect(status.Text.Contains("is live") && problems.Count == 0 && Uploads() == uploadsBefore + 1, "looks the arcade doesn't answer don't end the wait, and nothing is uploaded twice: " + status.Text + " / " + problems.Count + " pop-ups");
            // The wait running out: still being stored (another upload may be ahead of it), or the arcade out of reach.
            StoreWait = _ => TimeSpan.FromMilliseconds(300); slow.Processing = 1000000;
            await Guard(Publish, popUp: true);
            Expect(status.Text.StartsWith("The arcade is still storing it") && open.Visibility == Visibility.Visible && problems.Count == 0, "when the wait runs out the window says the arcade is still storing it: " + status.Text);
            slow.Processing = 0; slow.Unanswered = 1000000;
            await Guard(Publish, popUp: true);
            Expect(status.Text.StartsWith("The upload reached the arcade, but") && problems.Count == 0, "or that the arcade couldn't be reached since, without offering to upload again: " + status.Text);
            slow.Unanswered = 0;
            // Stop waiting stops the waiting, not the upload.
            StoreWait = ArcadiaPackage.StoreWait; slow.Processing = 1000000; uploadsBefore = Uploads();
            run = Guard(Publish, popUp: true);
            Expect(await Until(() => onArcade), "the upload reaches the arcade again: " + status.Text);
            CancelWork(); await run;
            Expect(status.Text.StartsWith("Stopped waiting") && Uploads() == uploadsBefore + 1 && open.Visibility == Visibility.Visible, "Stop waiting stops the waiting, and the upload stays with the arcade: " + status.Text);
            // Closing the window while it works hides it; the work carries on, and the window comes back with the result.
            slow.Processing = 30;
            run = Guard(Publish, popUp: true);
            Expect(await Until(() => onArcade), "the upload reaches the arcade a third time: " + status.Text);
            Close();
            Expect(!IsVisible && busy, "closing the window while the arcade stores an upload hides it, and the work carries on");
            await run;
            Expect(IsVisible && status.Text.Contains("is live"), "and it comes back with how it ended: " + status.Text);
            // Cancel while the game is still on its way: nothing is published and no version is used.
            slow.Processing = 0; var hold = new TaskCompletionSource(); slow.Hold = hold;
            string versionWas = version.Text, lastWas = editor.SavedGame(client.Host)?.LastVersion ?? "";
            run = Guard(Publish, popUp: true);
            Expect(await Until(() => UploadInFlight), "a game on its way to the arcade is known to be: " + status.Text);
            Expect(cancel.Visibility == Visibility.Visible && (string)cancel.Content == "Cancel", "while a game is being sent there is a Cancel button");
            CancelWork(); await run;
            slow.Hold = null; hold.SetResult();
            Expect(status.Text.StartsWith("Cancelled") && version.Text == versionWas && (editor.SavedGame(client.Host)?.LastVersion ?? "") == lastWas && problems.Count == 0 && cancel.Visibility == Visibility.Collapsed && !UploadInFlight,
                "Cancel stops an upload that hasn't reached the arcade: nothing is published and no version is used: " + status.Text);
            ChooseDetails = hadChoose; ProblemForTest = null; PollEvery = TimeSpan.FromSeconds(2.5); StoreWait = ArcadiaPackage.StoreWait;

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
            return "PASS: Publish to Arcadia window against a fake arcade: link (pending → approved), encrypted key, account and review mode, cover fitting, screenshots (reorder, order sent), YouTube-only video links (flagged as typed, refused before sending), mobile, Check, Publish (processing → live, game ID kept, next version), update to the same game (check?game=), a game deleted on the arcade (Check and Publish; No / Yes → new game at 1.0.0 with its details kept), a title already taken, revoked key, an arcade at a new address (link and game carried over; forgetting clears both), details edited on the website (checked before an update; Cancel, Use mine = overwrite=true, Keep Arcadia's = overwrite=false and copied into the project with its pictures, an unchanged picture not downloaded again), problems in a pop-up (the arcade down during Check, a failed upload: Try again, no version used, no Open in browser or Load details until it has gone through), the leaderboard clash both ways (a board on the website and none here: \"Remove it?\"; turned off on the website and one here: \"Turn it back on?\"; Use mine, Keep Arcadia's ticking or unticking Keep a leaderboard), Keep a leaderboard off sent as \"scores\": false, Load details from Arcadia (unticks Keep a leaderboard when it's off on the website), pictures only from the arcade.";
        }
    }
}
