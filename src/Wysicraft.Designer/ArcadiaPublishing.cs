using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ModelContextProtocol.Server;
using Wysicraft.Models;
using Wysicraft.Packaging;
namespace Wysicraft.Designer;

// File → Publish to Arcadia (docs/ARCADIA.md): link this computer to a player's Arcadia account once, then pack the
// project (the folder web export, game.json, the cover and any screenshots), check it and upload it. The key is DPAPI-protected in
// preferences and only ever sent in the Authorization header. An AI assistant can prepare and check a package over MCP,
// but publishing is always the person's own click.
public partial class MainWindow
{
    internal static string WysicraftVersion => (typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "").Split('+')[0];
    string ArcadeUrl => Prefs().ArcadeUrl.Length > 0 ? Prefs().ArcadeUrl : ArcadiaPackage.DefaultArcade;
    ArcadiaClient NewArcadiaClient() => new(ArcadeUrl, WysicraftVersion);
    // The arcade lists linked computers by this name.
    static string ArcadiaDeviceName() { string name = Environment.MachineName + " (Arcadia Studio)"; return name.Length > 60 ? name[..60] : name; }

    /// <summary>An arcade that moved to a new address is the same server: its link and game IDs saved under the old
    /// address count for the new one (the link is copied across the first time it's needed; the new address wins after).</summary>
    internal static readonly Dictionary<string, string> FormerHosts = new(StringComparer.OrdinalIgnoreCase) { ["arcadia.arcadiastudio.games"] = "arcadia.lastweeksproject.com" };
    static IEnumerable<string> HostAndFormer(string host) { yield return host; if (FormerHosts.TryGetValue(host, out var old)) yield return old; }

    string? ArcadiaKey(string host)
    {
        if ((!Prefs().ArcadiaKeys.TryGetValue(host, out var stored) || stored.Length == 0) && FormerHosts.TryGetValue(host, out var former)
            && Prefs().ArcadiaKeys.TryGetValue(former, out var carried) && carried.Length > 0)
        {
            stored = Prefs().ArcadiaKeys[host] = carried; SavePrefs();
            Log("Your Arcadia link carried over from " + former + " to its new address, " + host + ".");
        }
        if (stored == null || stored.Length == 0) return null;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), null, DataProtectionScope.CurrentUser)); }
        catch (Exception) { Prefs().ArcadiaKeys.Remove(host); SavePrefs(); Log("The saved Arcadia link for " + host + " couldn't be read, so it was removed. Link again to publish."); return null; }
    }
    void StoreArcadiaKey(string host, string key) { Prefs().ArcadiaKeys[host] = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser)); SavePrefs(); }
    // Both addresses, so a revoked or unlinked key can't come back from the old one.
    void ForgetArcadiaKey(string host) { bool removed = false; foreach (var h in HostAndFormer(host).ToList()) removed |= Prefs().ArcadiaKeys.Remove(h); if (removed) SavePrefs(); }

    /// <summary>The game this project became on an arcade: from the project, or from preferences when the project
    /// wasn't saved after its first publish (publishing it again must not make a second game).</summary>
    string? RememberedGameId(string host)
    {
        foreach (var h in HostAndFormer(host))
        {
            if (project.Publishing.Arcades.TryGetValue(h, out var game) && game.GameId.Length > 0) return game.GameId;
            if (Prefs().ArcadiaGames.TryGetValue(h + "|" + project.Manifest.Id, out var id) && id.Length > 0) return id;
        }
        return null;
    }
    /// <summary>What the project remembers about its game on an arcade (or under the arcade's old address).</summary>
    ArcadeGame? SavedGame(string host)
    {
        foreach (var h in HostAndFormer(host)) if (project.Publishing.Arcades.TryGetValue(h, out var game) && game.GameId.Length > 0) return game;
        return null;
    }
    void RememberGame(string host, string gameId, string version)
    {
        Change();
        project.Publishing.Arcades[host] = new ArcadeGame { GameId = gameId, LastVersion = version };
        Prefs().ArcadiaGames[host + "|" + project.Manifest.Id] = gameId; SavePrefs();
    }
    // Both addresses, so "Publish as a new game" can't be undone by the old address's entry.
    void ForgetGame(string host)
    {
        Change(); bool removed = false;
        foreach (var h in HostAndFormer(host).ToList()) { project.Publishing.Arcades.Remove(h); removed |= Prefs().ArcadiaGames.Remove(h + "|" + project.Manifest.Id); }
        if (removed) SavePrefs();
    }
    // Self-checks run against a fake arcade and must not open pages in the person's browser.
    internal static bool NoBrowser;
    static void OpenInBrowser(string url)
    {
        if (NoBrowser) return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return;
        try { Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true }); } catch { }
    }
    /// <summary>An address on the arcade itself; anything the server names elsewhere is ignored in favour of it.</summary>
    static string OnArcade(ArcadiaClient client, string url, string fallbackPath)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && string.Equals(uri.Host, client.Arcade.Host, StringComparison.OrdinalIgnoreCase) && uri.Scheme == client.Arcade.Scheme) return uri.ToString();
        return new Uri(client.Arcade, fallbackPath).ToString();
    }
    static string When(long? ms) => ms is long t ? DateTimeOffset.FromUnixTimeMilliseconds(t).ToLocalTime().ToString("ddd d MMM, HH:mm", CultureInfo.CurrentCulture) : "soon";

    /// <summary>A cover or screenshot the arcade's size (1280 × 800): the middle of the picture at 16:10, scaled. Pixel art is
    /// enlarged with hard edges; a larger picture is shrunk smoothly. PNG, or JPEG when a PNG would pass 2 MB.</summary>
    static (byte[] Bytes, string Type) FitScreenshot(byte[] picture)
    {
        var source = BitmapDecoder.Create(new MemoryStream(picture), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        int w = source.PixelWidth, h = source.PixelHeight, cw = w, ch = h;
        double target = (double)ArcadiaPackage.ScreenshotWidth / ArcadiaPackage.ScreenshotHeight;
        if ((double)w / h > target) cw = Math.Max(1, (int)Math.Round(h * target)); else ch = Math.Max(1, (int)Math.Round(w / target));
        var crop = new CroppedBitmap(source, new Int32Rect((w - cw) / 2, (h - ch) / 2, cw, ch));
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, cw < ArcadiaPackage.ScreenshotWidth ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen()) dc.DrawImage(crop, new Rect(0, 0, ArcadiaPackage.ScreenshotWidth, ArcadiaPackage.ScreenshotHeight));
        var bitmap = new RenderTargetBitmap(ArcadiaPackage.ScreenshotWidth, ArcadiaPackage.ScreenshotHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        byte[] Encode(BitmapEncoder encoder) { encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray(); }
        var png = Encode(new PngBitmapEncoder());
        return png.Length <= ArcadiaPackage.MaxScreenshotBytes ? (png, "png") : (Encode(new JpegBitmapEncoder { QualityLevel = 90 }), "jpg");
    }

    ArcadiaDialog? arcadiaDialog;
    void PublishToArcadia()
    {
        SaveScriptText();
        if (arcadiaDialog != null) { arcadiaDialog.Activate(); return; }
        arcadiaDialog = new ArcadiaDialog(this); arcadiaDialog.Closed += (_, _) => arcadiaDialog = null; arcadiaDialog.Show();
    }

    /// <summary>The Publish to Arcadia window: account, game details, cover, screenshots and videos, leaderboard, check
    /// and publish.</summary>
    sealed partial class ArcadiaDialog : Window
    {
        readonly MainWindow editor;
        ArcadiaClient client;
        string? key;
        ArcadiaMe? me;
        bool busy;
        CancellationTokenSource? work;
        byte[] cover; string coverType;
        readonly List<PublishImage> gallery;
        // Account
        readonly TextBlock account = Wrap(), mode = Wrap(Brushes.LightGray), limit = Wrap(Brushes.LightGray);
        readonly Button link = new() { Content = "Link to Arcadia…" }, arcade = new() { Content = "Arcade address…" }, profile = new() { Content = "Set up a creator profile", Visibility = Visibility.Collapsed };
        // Game
        readonly TextBox title = new(), description = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 64, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, version = new(), controls = new();
        readonly ComboBox[] genres = [Editable(ArcadiaPackage.Genres), Editable(ArcadiaPackage.Genres), Editable(ArcadiaPackage.Genres)];
        readonly ComboBox aspect;
        readonly CheckBox mobile = new() { Content = "Plays on phones and tablets" };
        // Cover
        readonly Image picture = new() { Width = 256, Height = 160, Stretch = Stretch.Uniform };
        readonly TextBlock pictureInfo = Wrap(Brushes.LightGray);
        // Screenshots and videos
        readonly WrapPanel shots = new();
        readonly TextBlock shotsInfo = Wrap(Brushes.LightGray), videoNote = Wrap(new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x7A)));
        readonly Button shotCapture = new() { Content = "Capture from Preview", Margin = new Thickness(0, 0, 8, 0) }, shotChoose = new() { Content = "Choose files…" };
        readonly TextBox[] videos = [new(), new(), new()];
        // Leaderboard
        readonly CheckBox leaderboard = new() { Content = "Keep a leaderboard for this game" };
        readonly ComboBox boardPage = new() { MinWidth = 260 };
        readonly StackPanel scoresPanel = new();
        readonly TextBox label = new(), min = new(), max = new(), minSeconds = new(), scorePath = new();
        readonly ComboBox format = Choice(("points", "Points"), ("number", "Number"), ("time", "Time (milliseconds)")), order = Choice(("desc", "Higher is better"), ("asc", "Lower is better")),
            aggregate = Choice(("best", "Each player's best run"), ("sum", "Total of all their runs")), scoreFrom;
        readonly CheckBox whole = new() { Content = "Whole numbers (round down)", IsChecked = true };
        readonly StackPanel triggers = new(), stats = new();
        readonly List<string> names;
        // Check and publish
        readonly ListBox findings = new() { MinHeight = 60, MaxHeight = 180 };
        readonly TextBlock status = Wrap();
        /// <summary>A line for the person in the status area (another window handing over, for example).</summary>
        internal void Note(string text) => status.Text = text;
        readonly ProgressBar progress = new() { Height = 6, Minimum = 0, Maximum = 1, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 4) };
        readonly Button check = new() { Content = "Check" }, publish = new() { Content = "Publish", FontWeight = FontWeights.SemiBold }, history = new() { Content = "Publishing history…" }, open = new() { Content = "Open in browser", Visibility = Visibility.Collapsed };
        string openUrl = "";
        // Self-checks answer the window's yes/no questions through this instead of a message box.
        internal Func<string, bool>? Confirm;
        bool Ask(string question) => Confirm?.Invoke(question) ?? MessageBox.Show(this, question, "Publish to Arcadia", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
        // Self-checks answer the "details were changed on Arcadia" question through this: false keeps Arcadia's, true
        // uses the project's, null cancels.
        internal Func<ArcadiaDetails, bool?>? ChooseDetails;
        readonly Button loadDetails = new() { Content = "Load details from Arcadia", ToolTip = "Replace this form's title, description, genres, controls, videos, cover, screenshots and leaderboard with what's on Arcadia now (for example after editing them on the website)." };
        /// <summary>What to ask when the saved game can't be updated: deleted by you, removed by a moderator, or not
        /// one of this account's games on this arcade (any other 404).</summary>
        static string CantUpdate(ArcadiaException ex, string name)
        {
            string game = name.Length > 0 ? name : "this game";
            string why = ex.Code switch
            {
                "deleted" => "You deleted " + game + " on Arcadia, so it can't be updated.",
                "removed" => "A moderator removed " + game + " from Arcadia, so it can't be updated." + (ex.Message.Length > 0 ? " " + ex.Message : ""),
                _ => game + " isn't one of your games on this arcade anymore, so it can't be updated."
            };
            return why + "\n\nPublish it as a new game? Its title, description, cover, screenshots, videos and leaderboard stay as they are, and its version starts again at 1.0.0.";
        }

        public ArcadiaDialog(MainWindow editor, ArcadiaClient? testClient = null)
        {
            this.editor = editor; Owner = editor; client = testClient ?? editor.NewArcadiaClient();
            // The app theme styles Window by exact type, which a subclass doesn't match.
            SetResourceReference(StyleProperty, typeof(Window));
            Title = "Publish to Arcadia"; Width = 720; Height = 860; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            names = ArcadiaPackage.StateNames(editor.project);
            scoreFrom = Editable(names);
            string fromScreen = ArcadiaPackage.AspectRatio(editor.project);
            aspect = Editable(["From the screen (" + fromScreen + ")", "16:9", "4:3"]);
            var s = editor.project.Publishing;
            cover = s.Cover; coverType = s.CoverType;
            gallery = s.Screenshots.Select(x => new PublishImage { Type = x.Type, Bytes = x.Bytes }).ToList();

            var root = new DockPanel { Margin = new Thickness(12) }; Content = root;
            // The bottom bar: findings, status and the two buttons stay in view while the form scrolls.
            var bottom = new StackPanel { Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
            bottom.Children.Add(Heading("Check")); bottom.Children.Add(findings); bottom.Children.Add(progress); bottom.Children.Add(status);
            var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) }; bottom.Children.Add(buttons);
            foreach (var b in new[] { check, publish, open, history, loadDetails }) { b.Margin = new Thickness(0, 0, 8, 0); b.Padding = new Thickness(12, 3, 12, 3); buttons.Children.Add(b); }
            var form = new StackPanel(); root.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            // Account
            form.Children.Add(Heading("Account"));
            form.Children.Add(account); form.Children.Add(mode); form.Children.Add(limit);
            var accountButtons = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
            foreach (var b in new[] { link, profile, arcade }) { b.Margin = new Thickness(0, 0, 8, 0); accountButtons.Children.Add(b); }
            form.Children.Add(accountButtons);
            var terms = Wrap(Brushes.LightGray); terms.Inlines.Add("Your game stays yours and you're responsible for it. Publishing follows the ");
            var termsLink = new Hyperlink(new Run("terms for publishing games")); termsLink.Click += (_, _) => OpenInBrowser(new Uri(client.Arcade, "#/legal?s=creators").ToString());
            terms.Inlines.Add(termsLink); terms.Inlines.Add(". By publishing you confirm you have the right to publish everything in the game."); form.Children.Add(terms);

            // Game
            form.Children.Add(Heading("Game"));
            title.Text = s.Title.Length > 0 ? s.Title : editor.project.Manifest.Name;
            description.Text = s.Description; controls.Text = s.Controls;
            for (int i = 0; i < 3; i++) genres[i].Text = i < s.Genre.Count ? s.Genre[i] : "";
            aspect.Text = s.AspectRatio.Length > 0 ? s.AspectRatio : (string)aspect.Items[0]!;
            form.Children.Add(Row("Title", title, "1 to 60 characters. Shown everywhere on the arcade."));
            form.Children.Add(Row("Description", description, "Up to 600 characters of plain text."));
            var genreRow = new UniformGrid3(genres); form.Children.Add(Row("Genre", genreRow, "Up to 3. The first is the label on the game's card."));
            form.Children.Add(Row("Version", version, "Raise it every time you publish."));
            form.Children.Add(Row("Controls", controls, "How to play, for example \"WASD to move · Space to dash\". Up to 200 characters."));
            form.Children.Add(Row("Aspect ratio", aspect, "The arcade letterboxes the game to this shape."));
            mobile.IsChecked = s.Mobile;
            form.Children.Add(Row("", mobile, "Touch controls (on-screen buttons or tapping) and it fits a small screen. The arcade shows a phone icon and lists it under Mobile friendly; otherwise phone players are told it may need a keyboard."));

            // Cover
            form.Children.Add(Heading("Cover"));
            var shotRow = new StackPanel { Orientation = Orientation.Horizontal };
            shotRow.Children.Add(new Border { Child = picture, BorderBrush = new SolidColorBrush(Color.FromRgb(0x45, 0x4B, 0x56)), BorderThickness = new Thickness(1), Background = new SolidColorBrush(Color.FromRgb(0x15, 0x18, 0x1D)) });
            var shotButtons = new StackPanel { Margin = new Thickness(12, 0, 0, 0), Width = 300 };
            var capture = new Button { Content = "Capture from Preview", Margin = new Thickness(0, 0, 0, 6) }; var choose = new Button { Content = "Choose file…", Margin = new Thickness(0, 0, 0, 6) };
            shotButtons.Children.Add(capture); shotButtons.Children.Add(choose); shotButtons.Children.Add(pictureInfo);
            shotRow.Children.Add(shotButtons); form.Children.Add(shotRow);
            form.Children.Add(Wrap(Brushes.LightGray, "The game's card in the arcade and the big picture on its page: 1280 × 800, real gameplay, no borders. PNG, JPEG or WebP up to 2 MB. Captures are cropped to 16:10 from the middle."));

            // Screenshots
            form.Children.Add(Heading("Screenshots"));
            form.Children.Add(Wrap(Brushes.LightGray, "The gallery on the game's page, in this order. Up to 8, same size and rules as the cover. 3 or more is best."));
            form.Children.Add(shots);
            var shotBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            shotBar.Children.Add(shotCapture); shotBar.Children.Add(shotChoose); form.Children.Add(shotBar); form.Children.Add(shotsInfo);

            // Videos
            form.Children.Add(Heading("Videos"));
            var videoList = new StackPanel();
            for (int i = 0; i < videos.Length; i++) { videos[i].Text = i < s.Videos.Count ? s.Videos[i] : ""; videos[i].Margin = new Thickness(0, 0, 0, 4); videos[i].TextChanged += (_, _) => ShowVideoNote(); videoList.Children.Add(videos[i]); }
            videoList.Children.Add(videoNote);
            form.Children.Add(Row("YouTube links", videoList, "Up to 3, shown on the game's page before the screenshots: youtube.com/watch?v=…, youtu.be/… or youtube.com/shorts/…. Only the link is sent, never a video file."));

            // Leaderboard
            form.Children.Add(Heading("Leaderboard"));
            // Scan sits beside the tick box: it suggests the score, the end of a run and extra columns, and ticks the box.
            leaderboard.IsChecked = s.Leaderboard; leaderboard.VerticalAlignment = VerticalAlignment.Center;
            var boardRow = new StackPanel { Orientation = Orientation.Horizontal }; boardRow.Children.Add(leaderboard); boardRow.Children.Add(scan);
            form.Children.Add(boardRow);
            // The leaderboard page players see: the arcade's standard board, one made here, or a page imported as it is.
            var pageRow = new StackPanel();
            var pageButtons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            Button PageButton(string text, string tip) { var b = new Button { Content = text, ToolTip = tip, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 1, 8, 1) }; pageButtons.Children.Add(b); return b; }
            var createPage = PageButton("Create leaderboard…", "Design a leaderboard page in the editor: lists, a podium, rank boxes and more, filled from the game's board.");
            var importPage = PageButton("Import leaderboard…", "A .lb file, or a page. A page made in Arcadia Studio can be edited again; any other page is used as it is.");
            var editPage = PageButton("Edit", "Open the chosen leaderboard page in the leaderboard creator.");
            var removePage = PageButton("Remove", "Stop using a page of your own: the game goes back to the arcade's standard board from the next publish. A page you designed stays in the project; an imported page is taken out.");
            pageRow.Children.Add(boardPage); pageRow.Children.Add(pageButtons);
            form.Children.Add(Row("Leaderboard page", pageRow, "What players see when they open the game's leaderboard. Needs Keep a leaderboard."));
            FillBoardPages(s.LeaderboardPage);
            // Create and Edit open the leaderboard creator, a window of its own; closing it comes back here with the page chosen.
            createPage.Click += (_, _) => { try { Apply(); OpenCreator(editor.CreateLeaderboard(open: false)); } catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException) { status.Text = ex.Message; } };
            importPage.Click += (_, _) => { try { if (editor.ImportLeaderboard(open: false) is string choice) FillBoardPages(choice); } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { status.Text = ex.Message; } };
            editPage.Click += (_, _) => { try { if (Chosen(boardPage) is string c && c.StartsWith("board:") && editor.project.Leaderboards.FirstOrDefault(b => b.Id == c[6..]) is UiDefinition board) { Apply(); OpenCreator(board); } } catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException) { status.Text = ex.Message; } };
            removePage.Click += (_, _) => RemoveBoardPage();
            // Edit needs a page made here; Remove needs a page of any kind to be set.
            void PageButtons() { string chosen = Chosen(boardPage); editPage.IsEnabled = chosen.StartsWith("board:"); removePage.IsEnabled = chosen.StartsWith("board:") || chosen == "file"; }
            boardPage.SelectionChanged += (_, _) => PageButtons();
            PageButtons();
            // Pages made or changed in the editor meanwhile appear when the window is used again.
            Activated += (_, _) => { if (busy) return; if (Fingerprint(editor.project.Publishing) != shown) Reload(); else FillBoardPages(null); };
            form.Children.Add(scoresPanel);
            var c = s.Scores;
            label.Text = c.Label; Select(format, c.Format); Select(order, c.Order); Select(aggregate, c.Aggregate);
            min.Text = c.Min.ToString(CultureInfo.InvariantCulture); max.Text = c.Max?.ToString(CultureInfo.InvariantCulture) ?? ""; minSeconds.Text = c.MinSeconds.ToString(CultureInfo.InvariantCulture);
            scoreFrom.Text = c.Score.Variable; scorePath.Text = c.Score.Path; whole.IsChecked = c.Round != "none";
            scoresPanel.Children.Add(Row("Column name", label, "For example Score, Distance or Time."));
            scoresPanel.Children.Add(Row("Format", format, "")); scoresPanel.Children.Add(Row("Better scores", order, "")); scoresPanel.Children.Add(Row("Ranks", aggregate, ""));
            scoresPanel.Children.Add(Row("Lowest score", min, "")); scoresPanel.Children.Add(Row("Highest score", max, "The highest score that's really possible. Anything above it is refused as a cheat."));
            scoresPanel.Children.Add(Row("Shortest run", minSeconds, "Seconds. A shorter run isn't counted."));
            var source = new DockPanel(); var pathBox = Labelled("field", scorePath, 150); DockPanel.SetDock(pathBox, Dock.Right); source.Children.Add(pathBox); source.Children.Add(scoreFrom);
            scoresPanel.Children.Add(Row("Score comes from", source, "A screen variable or ctx.state name. If it holds JSON (like ctx.state.set('g', JSON.stringify(g))), the field is the part to read, for example score."));
            scoresPanel.Children.Add(Row("", whole, ""));
            scoresPanel.Children.Add(Wrap(Brushes.White, "Run ends when (any of these):")); scoresPanel.Children.Add(triggers);
            var addTrigger = new Button { Content = "Add a condition", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 6) }; scoresPanel.Children.Add(addTrigger);
            scoresPanel.Children.Add(Wrap(Brushes.LightGray, "A score is sent each time this turns true, so it must be false while playing and false again when a new run starts."));
            scoresPanel.Children.Add(Wrap(Brushes.White, "Extra columns:")); scoresPanel.Children.Add(stats);
            var addStat = new Button { Content = "Add a column", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 6) }; scoresPanel.Children.Add(addStat);
            foreach (var t in c.Triggers) AddTrigger(t);
            foreach (var st in c.Stats) AddStat(st);
            if (c.Triggers.Count == 0) AddTrigger(new PublishTrigger());
            addTrigger.Click += (_, _) => { if (triggers.Children.Count < 6) AddTrigger(new PublishTrigger()); };
            addStat.Click += (_, _) => { if (stats.Children.Count < 8) AddStat(new PublishStat()); };
            void ShowScores() => scoresPanel.Visibility = leaderboard.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            leaderboard.Click += (_, _) => ShowScores(); ShowScores();

            ShowPicture(); ShowShots(); ShowVideoNote();
            shotCapture.Click += async (_, _) => await Guard(CaptureScreenshot);
            shotChoose.Click += (_, _) => ChooseScreenshots();
            link.Click += async (_, _) => await Guard(key == null ? Link : Unlink);
            profile.Click += (_, _) => OpenInBrowser(new Uri(client.Arcade, "#/me").ToString());
            arcade.Click += async (_, _) => await Guard(ChangeArcade);
            capture.Click += async (_, _) => await Guard(Capture);
            choose.Click += (_, _) => Choose();
            check.Click += async (_, _) => await Guard(Check);
            publish.Click += async (_, _) => await Guard(Publish);
            open.Click += (_, _) => OpenInBrowser(openUrl);
            history.Click += async (_, _) => await Guard(History);
            loadDetails.Click += async (_, _) => await Guard(LoadDetails);
            scan.Click += async (_, _) => await Guard(Scan);
            findings.MouseDoubleClick += (_, _) => OpenFinding();
            Closing += (_, e) => { if (busy) { e.Cancel = true; status.Text = "Wait for the upload to finish before closing."; return; } Apply(); };
            shown = Fingerprint(editor.project.Publishing);
            Loaded += async (_, _) => await Guard(Refresh);
        }

        // ---- Layout helpers ----
        static TextBlock Wrap(Brush? brush = null, string text = "") => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = brush ?? Brushes.White, Margin = new Thickness(0, 2, 0, 2) };
        static TextBlock Heading(string text) => new() { Text = text.ToUpperInvariant(), Foreground = Brushes.LightSkyBlue, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) };
        static Grid Row(string name, UIElement input, string help)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(new TextBlock { Text = name, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 8, 0) });
            var right = new StackPanel(); Grid.SetColumn(right, 1); right.Children.Add(input);
            if (help.Length > 0) right.Children.Add(new TextBlock { Text = help, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
            grid.Children.Add(right); return grid;
        }
        static StackPanel Labelled(string name, FrameworkElement input, double width)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0) };
            panel.Children.Add(new TextBlock { Text = name, Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            input.Width = width; panel.Children.Add(input); return panel;
        }
        static ComboBox Editable(IEnumerable<string> items) => new() { IsEditable = true, ItemsSource = items.ToList() };
        static ComboBox Choice(params (string Value, string Text)[] items)
        {
            var box = new ComboBox(); foreach (var (value, text) in items) box.Items.Add(new ComboBoxItem { Content = text, Tag = value });
            box.SelectedIndex = 0; return box;
        }
        /// <summary>The leaderboard page choices, keeping (or setting) the chosen one. "" (never chosen) picks the project's
        /// first leaderboard, or the standard board when it has none.</summary>
        void FillBoardPages(string? choose)
        {
            var project = editor.project;
            string current = choose ?? (boardPage.SelectedItem is ComboBoxItem { Tag: string tag } ? tag : project.Publishing.LeaderboardPage);
            boardPage.Items.Clear();
            boardPage.Items.Add(new ComboBoxItem { Content = "The arcade's standard board", Tag = "standard" });
            foreach (var b in project.Leaderboards) boardPage.Items.Add(new ComboBoxItem { Content = "My page: " + (b.Title.Length > 0 ? b.Title + " (" + b.Id + ")" : b.Id), Tag = "board:" + b.Id });
            if (project.Publishing.LeaderboardHtml.Length > 0) boardPage.Items.Add(new ComboBoxItem { Content = $"Imported page ({project.Publishing.LeaderboardHtml.Length / 1024.0:0} KB, used as it is)", Tag = "file" });
            if (current == "") current = AutoBoardPage();
            Select(boardPage, current); if (boardPage.SelectedItem == null) boardPage.SelectedIndex = 0;
        }
        /// <summary>The leaderboard creator on a page; when it closes, the page is the one chosen here.</summary>
        internal Func<UiDefinition, string>? CreatorForTest;
        void OpenCreator(UiDefinition board)
        {
            string id = CreatorForTest != null ? CreatorForTest(board) : editor.OpenLeaderboardCreator(board, this);
            FillBoardPages("board:" + id);
            shown = Fingerprint(editor.project.Publishing);
            Activate();
            status.Text = "Leaderboard page: " + id + ". Publishing sends it as the game's leaderboard.";
        }
        /// <summary>Remove: the game stops using a page of its own. The dropdown goes back to the arcade's standard board,
        /// so the next publish leaves leaderboardPage and the page's file out (the arcade then deletes the old page). A page
        /// designed here stays in the project; an imported one is taken out of it.</summary>
        internal Func<string, bool>? ConfirmForTest;
        internal void RemoveBoardPage()
        {
            string chosen = Chosen(boardPage);
            if (chosen == "file")
            {
                const string question = "Remove the imported leaderboard page from this project?\n\nThe game will use the arcade's standard board. You can import the page again later.";
                if (!(ConfirmForTest != null ? ConfirmForTest(question) : MessageBox.Show(this, question, "Leaderboard page", MessageBoxButton.YesNo) == MessageBoxResult.Yes)) return;
                Apply(); editor.RemoveImportedLeaderboard(); shown = Fingerprint(editor.project.Publishing);
                FillBoardPages("standard");
                status.Text = "The imported page was removed. The game uses the arcade's standard board from the next publish.";
            }
            else if (chosen.StartsWith("board:"))
            {
                FillBoardPages("standard");
                status.Text = "The game uses the arcade's standard board from the next publish. Your page " + chosen[6..] + " is still in the project: choose it again here any time.";
            }
        }
        string AutoBoardPage() => editor.project.Leaderboards.Count > 0 ? "board:" + editor.project.Leaderboards[0].Id : "standard";
        static void Select(ComboBox box, string value) { foreach (ComboBoxItem item in box.Items) if ((string)item.Tag == value) { box.SelectedItem = item; return; } }
        static string Chosen(ComboBox box) => (string?)((ComboBoxItem?)box.SelectedItem)?.Tag ?? "";
        sealed class UniformGrid3 : System.Windows.Controls.Primitives.UniformGrid
        {
            public UniformGrid3(ComboBox[] boxes) { Columns = boxes.Length; foreach (var b in boxes) { b.Margin = new Thickness(0, 0, 6, 0); Children.Add(b); } }
        }

        void AddTrigger(PublishTrigger t)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var variable = Editable(names); variable.Text = t.Variable;
            var path = new TextBox { Text = t.Path }; var how = Choice(("true", "is true"), ("equals", "equals")); var value = new TextBox { Text = t.EqualsValue ?? "" };
            Select(how, t.EqualsValue != null ? "equals" : "true");
            var remove = new Button { Content = "Remove", Margin = new Thickness(6, 0, 0, 0) }; remove.Click += (_, _) => triggers.Children.Remove(row);
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(Labelled("field", path, 110)); right.Children.Add(Labelled("", how, 80)); right.Children.Add(Labelled("", value, 90)); right.Children.Add(remove);
            void Show() => value.Visibility = Chosen(how) == "equals" ? Visibility.Visible : Visibility.Hidden;
            how.SelectionChanged += (_, _) => Show(); Show();
            DockPanel.SetDock(right, Dock.Right); row.Children.Add(right); row.Children.Add(variable);
            row.Tag = (Func<PublishTrigger>)(() => new PublishTrigger { Variable = variable.Text.Trim(), Path = path.Text.Trim(), EqualsValue = Chosen(how) == "equals" ? value.Text : null });
            triggers.Children.Add(row);
        }
        void AddStat(PublishStat st)
        {
            var outer = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };
            var row = new DockPanel();
            var steady = new CheckBox { Content = "Doesn't grow over time", IsChecked = !st.Check, Margin = new Thickness(6, 3, 0, 0),
                ToolTip = "For a column like accuracy % or a character number. Arcadia compares every run's pace with play time; this column is left out of that." };
            var keyBox = new TextBox { Text = st.Key }; var labelBox = new TextBox { Text = st.Label }; var variable = Editable(names); variable.Text = st.Variable; var path = new TextBox { Text = st.Path };
            var how = Choice(("max", "highest"), ("min", "lowest"), ("sum", "total")); Select(how, st.Aggregate);
            var remove = new Button { Content = "Remove", Margin = new Thickness(6, 0, 0, 0) }; remove.Click += (_, _) => stats.Children.Remove(outer);
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(Labelled("key", keyBox, 70)); left.Children.Add(Labelled("name", labelBox, 90));
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(Labelled("field", path, 80)); right.Children.Add(Labelled("keep", how, 80)); right.Children.Add(remove);
            DockPanel.SetDock(left, Dock.Left); DockPanel.SetDock(right, Dock.Right); row.Children.Add(left); row.Children.Add(right); row.Children.Add(Labelled("from", variable, 110));
            outer.Tag = (Func<PublishStat>)(() => new PublishStat { Key = keyBox.Text.Trim(), Label = labelBox.Text.Trim(), Variable = variable.Text.Trim(), Path = path.Text.Trim(), Aggregate = Chosen(how), Format = "number", Check = steady.IsChecked != true });
            outer.Children.Add(row); outer.Children.Add(steady);
            stats.Children.Add(outer);
        }

        // ---- What the form says ----
        static double Number(string text, double fallback) => double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
        PublishSettings Collect()
        {
            var s = Json.Clone(editor.project.Publishing);
            s.Title = title.Text.Trim(); s.Description = description.Text.Trim(); s.Version = version.Text.Trim(); s.Controls = controls.Text.Trim();
            s.Genre = genres.Select(g => g.Text.Trim()).Where(g => g.Length > 0).ToList();
            s.AspectRatio = aspect.Text.StartsWith("From the screen") ? "" : aspect.Text.Trim();
            s.Mobile = mobile.IsChecked == true;
            // Left at "" while it's still the automatic choice, so a leaderboard made later is picked up by itself.
            string page = Chosen(boardPage);
            s.LeaderboardPage = editor.project.Publishing.LeaderboardPage == "" && page == AutoBoardPage() ? "" : page;
            s.LeaderboardHtml = editor.project.Publishing.LeaderboardHtml;
            s.Cover = cover; s.CoverType = coverType;
            s.Screenshots = gallery.Select(x => new PublishImage { Type = x.Type, Bytes = x.Bytes }).ToList();
            s.Videos = videos.Select(v => v.Text.Trim()).Where(v => v.Length > 0).ToList();
            s.Leaderboard = leaderboard.IsChecked == true;
            s.Scores = new PublishScores
            {
                Label = label.Text.Trim(), Format = Chosen(format), Order = Chosen(order), Aggregate = Chosen(aggregate),
                Min = Number(min.Text, 0), Max = max.Text.Trim().Length == 0 ? null : Number(max.Text, 0), MinSeconds = Number(minSeconds.Text, 3),
                Score = new PublishWatch { Variable = scoreFrom.Text.Trim(), Path = scorePath.Text.Trim() },
                Triggers = triggers.Children.OfType<DockPanel>().Select(r => ((Func<PublishTrigger>)r.Tag)()).ToList(),
                Stats = stats.Children.OfType<StackPanel>().Select(r => ((Func<PublishStat>)r.Tag)()).ToList(),
                Round = whole.IsChecked == true ? "floor" : "none"
            };
            return s;
        }
        /// <summary>The form goes back into the project (one Undo step) when anything in it changed. If the project's
        /// settings changed since the form last showed them (MCP, Undo, the itch.io hand-over), the form takes them
        /// instead: a stale form never writes over newer settings.</summary>
        void Apply()
        {
            if (Fingerprint(editor.project.Publishing) != shown) { Reload(); return; }
            var s = Collect();
            var was = editor.project.Publishing;
            if (Json.Write(s) == Json.Write(was) && ReferenceEquals(s.Cover, was.Cover) && s.Screenshots.Select(x => x.Bytes).SequenceEqual(was.Screenshots.Select(x => x.Bytes), ReferenceEqualityComparer.Instance) && ReferenceEquals(s.LeaderboardHtml, was.LeaderboardHtml)) return;
            editor.Change(); editor.project.Publishing = s; shown = Fingerprint(s);
        }
        // Which settings the form is showing: their text and which pictures (by reference; picture bytes are never
        // changed in place, only replaced).
        string shown = "";
        // The saved game IDs (Arcades) and itch.io settings are left out: the window updates those itself while publishing.
        static string Fingerprint(PublishSettings p)
        {
            var form = Json.Clone(p); form.Arcades = []; form.Itch = new();
            static int Id(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
            return Json.Write(form) + "|" + Id(p.Cover) + "|" + string.Join(",", p.Screenshots.Select(x => Id(x.Bytes))) + "|" + Id(p.LeaderboardHtml);
        }
        /// <summary>The whole form from the project's settings, as when the window opens.</summary>
        internal void Reload()
        {
            var s = editor.project.Publishing;
            FillDetails(s);
            if (s.Title.Length == 0) title.Text = editor.project.Manifest.Name;
            version.Text = s.Version.Length > 0 ? s.Version : version.Text;
            aspect.Text = s.AspectRatio.Length > 0 ? s.AspectRatio : (string)aspect.Items[0]!;
            FillBoardPages(s.LeaderboardPage);
            shown = Fingerprint(s);
        }

        // ---- Account ----
        async Task Guard(Func<Task> action)
        {
            if (busy) return;
            busy = true; SetEnabled(false); work = new CancellationTokenSource();
            try { await action(); }
            catch (ArcadiaException ex) { await Failed(ex); }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or ArgumentException or IOException or NotSupportedException or FileFormatException) { status.Text = ex.Message; editor.Log("Arcadia: " + ex.Message); }
            finally { busy = false; work = null; progress.Visibility = Visibility.Collapsed; SetEnabled(true); }
        }
        void SetEnabled(bool on)
        {
            foreach (var b in new[] { link, arcade, check, history, scan }) b.IsEnabled = on;
            publish.IsEnabled = on && CanPublish(out _);
            history.IsEnabled = on && key != null && editor.RememberedGameId(client.Host) != null;
            foreach (var line in new[] { mode, limit }) line.Visibility = line.Text.Length > 0 || line.Inlines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        async Task Refresh()
        {
            key = editor.ArcadiaKey(client.Host); me = null; profile.Visibility = Visibility.Collapsed;
            link.Content = key == null ? "Link to Arcadia…" : "Unlink";
            string last = editor.SavedGame(client.Host)?.LastVersion ?? "";
            if (version.Text.Length == 0) version.Text = last.Length > 0 ? ArcadiaPackage.NextVersion(last) : editor.project.Publishing.Version.Length > 0 ? editor.project.Publishing.Version : editor.project.Manifest.Version;
            if (key == null) { account.Text = "Not linked to " + client.Host + ". Link this computer to your Arcadia account to publish (no password is typed here)."; mode.Text = limit.Text = ""; return; }
            account.Text = "Checking your account on " + client.Host + "…";
            try { me = await client.MeAsync(key, work?.Token ?? default); }
            catch (ArcadiaException ex) when (ex.Code == "not-creator") { account.Text = ex.Message; profile.Visibility = Visibility.Visible; mode.Text = "Set up a creator profile on Arcadia (Profile → Make games for Arcadia), then come back."; return; }
            ShowAccount();
        }
        void ShowAccount()
        {
            if (me == null) return;
            account.Inlines.Clear(); account.Inlines.Add("Publishing to " + client.Host + " as "); account.Inlines.Add(new Bold(new Run(me.Creator.Name)));
            if (me.Creator.Status == "pending") account.Inlines.Add(" (waiting for a moderator to approve your creator profile)");
            mode.Text = !me.Publishing.Enabled ? "Uploads are closed on this arcade" + (string.IsNullOrWhiteSpace(me.Publishing.Reason) ? "." : ": " + me.Publishing.Reason)
                : me.Creator.AutoPublish ? "Clean uploads go live straight away." : "Every upload waits for a moderator to review it before it goes live.";
            string? gameId = editor.RememberedGameId(client.Host);
            var game = gameId == null ? null : me.Games.FirstOrDefault(x => x.Id == gameId);
            var l = game?.Limits ?? (gameId == null ? me.Limits.NewGame : null);
            string what = gameId == null ? "This will be a new game." : game == null ? "Game " + gameId + " isn't in your account on this arcade anymore (deleted?). Publishing will ask whether to make it a new game." : "This updates " + game.Title + (game.Live != null ? " (live: " + game.Live.Version + ")" : "") + ".";
            limit.Text = what + (l == null ? "" : l.Unlimited ? " No upload limits." : l.Available > 0 ? $" {l.Available} of {l.Max} uploads available." : " Next upload available " + When(l.NextAt) + ".");
        }
        bool CanPublish(out string why)
        {
            why = "";
            if (key == null) { why = "Link to Arcadia first."; return false; }
            if (me == null) { why = "Your account couldn't be read."; return false; }
            if (!me.Publishing.Enabled) { why = "Uploads are closed on this arcade."; return false; }
            string? gameId = editor.RememberedGameId(client.Host);
            var l = gameId == null ? me.Limits.NewGame : me.Games.FirstOrDefault(x => x.Id == gameId)?.Limits;
            if (l != null && !l.CanUpload) { why = "Next upload available " + When(l.NextAt) + "."; return false; }
            return true;
        }
        async Task Link()
        {
            var start = await client.StartLinkAsync(ArcadiaDeviceName(), work!.Token);
            var dialog = new LinkWindow(this, client, start);
            bool? linked = dialog.ShowDialog();
            if (linked == true && dialog.Key.Length > 0)
            {
                editor.StoreArcadiaKey(client.Host, dialog.Key);
                status.Text = "Linked as " + (dialog.Creator?.Name ?? "your account") + ".";
                editor.Log("Linked Arcadia Studio to Arcadia (" + client.Host + ") as " + (dialog.Creator?.Name ?? "your account") + ".");
                await Refresh();
            }
            else status.Text = dialog.Outcome;
        }
        async Task Unlink()
        {
            if (!Ask("Unlink this computer from your Arcadia account on " + client.Host + "?")) return;
            try { await client.UnlinkAsync(key!, work!.Token); } catch (ArcadiaException ex) when (ex.Status == 401) { }
            editor.ForgetArcadiaKey(client.Host); status.Text = "Unlinked from " + client.Host + "."; editor.Log("Unlinked Arcadia Studio from Arcadia (" + client.Host + ").");
            await Refresh();
        }
        async Task ChangeArcade()
        {
            var input = new TextBox { Text = client.Arcade.ToString().TrimEnd('/'), Width = 380 };
            var ask = new Window { Owner = this, Title = "Arcade address", SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
            var panel = new StackPanel { Margin = new Thickness(14) }; ask.Content = panel;
            panel.Children.Add(Wrap(Brushes.White, "The Arcadia server to publish to (https). Each arcade has its own link and its own game IDs."));
            panel.Children.Add(input);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "Use this arcade", IsDefault = true, Margin = new Thickness(0, 0, 8, 0) }; var reset = new Button { Content = "Default", Margin = new Thickness(0, 0, 8, 0) }; var cancel = new Button { Content = "Cancel", IsCancel = true };
            reset.Click += (_, _) => input.Text = ArcadiaPackage.DefaultArcade; ok.Click += (_, _) => ask.DialogResult = true;
            row.Children.Add(ok); row.Children.Add(reset); row.Children.Add(cancel); panel.Children.Add(row);
            if (ask.ShowDialog() != true) return;
            var uri = ArcadiaClient.Normalize(input.Text);
            editor.Prefs().ArcadeUrl = uri.ToString().TrimEnd('/') == ArcadiaPackage.DefaultArcade ? "" : uri.ToString().TrimEnd('/'); editor.SavePrefs();
            client = editor.NewArcadiaClient(); version.Text = ""; await Refresh();
        }
        async Task Failed(ArcadiaException ex)
        {
            status.Text = ex.Message;
            editor.Log("Arcadia: " + ex.Message);
            switch (ex.Code)
            {
                case "bad-key":
                    editor.ForgetArcadiaKey(client.Host); key = null; me = null;
                    status.Text = "Arcadia Studio isn't linked to Arcadia anymore. Link again?";
                    account.Text = "Not linked to " + client.Host + "."; link.Content = "Link to Arcadia…"; mode.Text = limit.Text = "";
                    break;
                case "not-creator": profile.Visibility = Visibility.Visible; break;
                case "limit": if (ex.RetryAt != null) status.Text = ex.Message + " Next upload available " + When(ex.RetryAt) + "."; break;
                case "slow-down": status.Text = ex.Message + " Wait a few minutes, then try again."; break;
                case "deleted": status.Text = "You deleted this game on Arcadia. Click Publish to make it a new game."; break;
            }
            if (ex.Findings.Count > 0) ShowFindings([], ex.Findings);
            await Task.CompletedTask;
        }

        // ---- Cover, screenshots and videos ----
        /// <summary>A small copy of a picture for showing in the window, or null if it can't be read.</summary>
        static BitmapImage? Thumbnail(byte[] bytes, int width)
        {
            try { var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = width; image.StreamSource = new MemoryStream(bytes); image.EndInit(); image.Freeze(); return image; }
            catch (Exception) { return null; }
        }
        static string Describe(byte[] bytes, string type) => (ArcadiaPackage.ImageSize(bytes) is var (w, h) ? $"{w} × {h}, " : "") + $"{bytes.Length / 1024.0:0} KB {type.ToUpperInvariant()}";
        void ShowPicture()
        {
            if (cover.Length == 0) { picture.Source = null; pictureInfo.Text = "No cover yet."; return; }
            picture.Source = Thumbnail(cover, 512);
            pictureInfo.Text = picture.Source == null ? "This picture can't be shown here; the check will say what's wrong with it." : Describe(cover, coverType);
        }
        void ShowShots()
        {
            shots.Children.Clear();
            var frame = new SolidColorBrush(Color.FromRgb(0x45, 0x4B, 0x56)); var back = new SolidColorBrush(Color.FromRgb(0x15, 0x18, 0x1D));
            for (int i = 0; i < gallery.Count; i++)
            {
                int at = i; var shot = gallery[i];
                var card = new StackPanel { Margin = new Thickness(0, 0, 10, 10), Width = 160 };
                var thumb = Thumbnail(shot.Bytes, 320);
                card.Children.Add(new Border { BorderBrush = frame, BorderThickness = new Thickness(1), Background = back, Height = 100, ToolTip = Describe(shot.Bytes, shot.Type),
                    Child = thumb != null ? new Image { Source = thumb, Stretch = Stretch.Uniform } : new TextBlock { Text = "Can't show this picture", Foreground = Brushes.Gray, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
                var bar = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
                Button Small(string text, string tip, bool enabled, Action act)
                {
                    var b = new Button { Content = text, ToolTip = tip, IsEnabled = enabled, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(4, 0, 0, 0) };
                    b.Click += (_, _) => { act(); ShowShots(); }; return b;
                }
                var buttons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(buttons, Dock.Right);
                buttons.Children.Add(Small("◀", "Move earlier", at > 0, () => { gallery.RemoveAt(at); gallery.Insert(at - 1, shot); }));
                buttons.Children.Add(Small("▶", "Move later", at < gallery.Count - 1, () => { gallery.RemoveAt(at); gallery.Insert(at + 1, shot); }));
                buttons.Children.Add(Small("Remove", "Take this screenshot out", true, () => gallery.RemoveAt(at)));
                bar.Children.Add(buttons);
                bar.Children.Add(new TextBlock { Text = (at + 1).ToString(CultureInfo.InvariantCulture), Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center });
                card.Children.Add(bar); shots.Children.Add(card);
            }
            shotsInfo.Text = gallery.Count == 0 ? "No screenshots yet." : $"{gallery.Count} of {ArcadiaPackage.MaxScreenshots}.";
            shotCapture.IsEnabled = shotChoose.IsEnabled = gallery.Count < ArcadiaPackage.MaxScreenshots;
        }
        void ShowVideoNote()
        {
            var bad = videos.Select((v, i) => (Text: v.Text.Trim(), Number: i + 1)).Where(v => v.Text.Length > 0 && !ArcadiaPackage.IsYouTube(v.Text)).Select(v => v.Number).ToList();
            videoNote.Text = bad.Count == 0 ? "" : (bad.Count == 1 ? "Link " + bad[0] + " isn't" : "Links " + string.Join(" and ", bad) + " aren't") + " a YouTube video link. Check and Publish refuse it.";
            videoNote.Visibility = bad.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            var brush = new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x7A));
            foreach (var v in videos) { bool wrong = v.Text.Trim().Length > 0 && !ArcadiaPackage.IsYouTube(v.Text); if (wrong) v.BorderBrush = brush; else v.ClearValue(Control.BorderBrushProperty); }
        }
        /// <summary>The Preview screen fitted to 1280 × 800, or null when Preview wasn't open (it opens, for next time).</summary>
        async Task<(byte[] Bytes, string Type)?> CapturePreview()
        {
            if (editor.activePreview == null || !editor.activePreview.IsOpen)
            {
                editor.Preview();
                status.Text = "Preview is open. Play to a good moment, then click Capture from Preview again.";
                return null;
            }
            return FitScreenshot(await editor.activePreview.CaptureScreenAsync());
        }
        async Task Capture()
        {
            if (await CapturePreview() is not { } got) return;
            (cover, coverType) = got; ShowPicture();
            status.Text = "Captured the Preview screen as the cover.";
        }
        async Task CaptureScreenshot()
        {
            if (gallery.Count >= ArcadiaPackage.MaxScreenshots) return;
            if (await CapturePreview() is not { } got) return;
            gallery.Add(new PublishImage { Type = got.Type, Bytes = got.Bytes }); ShowShots();
            status.Text = $"Captured the Preview screen as screenshot {gallery.Count}.";
        }
        /// <summary>A chosen picture ready to use: its bytes and type, fitted to the arcade's 1280 × 800 (the middle at 16:10,
        /// as captures are) unless it already is that size. WebP is kept as it is (it can't be decoded here). Null (and a
        /// note in problems) when it isn't really a PNG, JPEG or WebP.</summary>
        internal static (byte[] Bytes, string Type)? ReadPicture(string file, List<string> problems, List<string> fitted)
        {
            var bytes = File.ReadAllBytes(file);
            string? type = ArcadiaPackage.ImageType(bytes);
            if (type == null) { problems.Add(Path.GetFileName(file) + " isn't really a PNG, JPEG or WebP picture."); return null; }
            if (type != "webp" && (ArcadiaPackage.ImageSize(bytes) != (ArcadiaPackage.ScreenshotWidth, ArcadiaPackage.ScreenshotHeight) || bytes.LongLength > ArcadiaPackage.MaxScreenshotBytes)) { (bytes, type) = FitScreenshot(bytes); fitted.Add(Path.GetFileName(file)); }
            return (bytes, type);
        }
        static string Fitted(List<string> names) => names.Count == 0 ? "" : " " + string.Join(", ", names) + (names.Count == 1 ? " was" : " were") + " fitted to 1280 × 800 (the middle at 16:10).";
        void Choose()
        {
            var dialog = new OpenFileDialog { Title = "Choose a cover", Filter = "Pictures (PNG, JPEG, WebP)|*.png;*.jpg;*.jpeg;*.webp" };
            if (dialog.ShowDialog(this) != true) return;
            List<string> problems = [], fitted = [];
            if (ReadPicture(dialog.FileName, problems, fitted) is not { } got) { status.Text = problems[0]; return; }
            (cover, coverType) = got; ShowPicture();
            status.Text = ("Cover chosen." + Fitted(fitted)).Trim();
        }
        void ChooseScreenshots()
        {
            int room = ArcadiaPackage.MaxScreenshots - gallery.Count; if (room <= 0) return;
            var dialog = new OpenFileDialog { Title = "Choose screenshots", Filter = "Pictures (PNG, JPEG, WebP)|*.png;*.jpg;*.jpeg;*.webp", Multiselect = true };
            if (dialog.ShowDialog(this) != true) return;
            List<string> problems = [], fitted = []; int added = 0;
            foreach (var file in dialog.FileNames.Take(room))
                if (ReadPicture(file, problems, fitted) is { } got) { gallery.Add(new PublishImage { Type = got.Type, Bytes = got.Bytes }); added++; }
            ShowShots();
            string left = dialog.FileNames.Length > room ? $" {dialog.FileNames.Length - room} more didn't fit: at most {ArcadiaPackage.MaxScreenshots}." : "";
            status.Text = $"Added {added} screenshot{(added == 1 ? "" : "s")}." + Fitted(fitted) + left + (problems.Count > 0 ? " " + string.Join(" ", problems) : "");
        }

        // ---- Check and publish ----
        async Task<(PublishSettings Settings, byte[] Zip, List<ArcadiaFinding> Local)> Build()
        {
            editor.SaveScriptText(); Apply();
            var s = Collect(); var project = Json.CloneProject(editor.project); string version = WysicraftVersion;
            status.Text = "Packing the game…";
            return await Task.Run(() =>
            {
                var files = ArcadiaPackage.Files(project, s, version);
                var local = ArcadiaPackage.Check(project, s, files, me?.Publishing.MaxUploadMb ?? ArcadiaPackage.DefaultMaxUploadMb);
                return (s, local.Any(f => f.Level == "block") ? Array.Empty<byte>() : ArcadiaPackage.Zip(files), local);
            });
        }
        IProgress<double> Sending(string what)
        {
            progress.Value = 0; progress.Visibility = Visibility.Visible;
            return new Progress<double>(v => { progress.Value = v; status.Text = v < 1 ? $"{what} {v:P0}" : what + " sent; waiting for the arcade…"; });
        }
        /// <summary>The saved game can't be updated. Asks first (it may have been deleted on purpose); on Yes the old ID is
        /// forgotten and the version starts again at 1.0.0, while everything else in the window stays as it is.</summary>
        bool OfferNewGame(ArcadiaException ex, string gameId)
        {
            if (!Ask(CantUpdate(ex, title.Text.Trim())))
            {
                status.Text = "Nothing was published. " + (ex.Code == "deleted" ? "You deleted this game on Arcadia: publish again and choose Yes to make it a new game." : ex.Message);
                return false;
            }
            editor.Log("Arcadia: game " + gameId + (ex.Code == "deleted" ? " was deleted on the arcade" : ex.Code == "removed" ? " was removed by a moderator" : " isn't this account's") + "; publishing it as a new game.");
            editor.ForgetGame(client.Host); version.Text = "1.0.0";
            ShowAccount();
            return true;
        }
        /// <summary>409 title-taken: another of the account's games already has this title. When this project has no saved
        /// game (it lost its ID), that game may well be this project: offer to update it. Otherwise the title must change.</summary>
        bool OfferExistingGame(ArcadiaException ex)
        {
            string name = title.Text.Trim();
            if (string.IsNullOrEmpty(ex.GameId) || editor.RememberedGameId(client.Host) != null)
            { status.Text = ex.Message + " Give this game a different title."; title.Focus(); return false; }
            if (!Ask("You already have a game called " + name + " on Arcadia.\n\nIs it this project? Yes updates that game. No lets you give this one a different title."))
            { status.Text = "Nothing was published. Give this game a different title, or choose Yes to update your existing " + name + "."; title.Focus(); return false; }
            string live = me?.Games.FirstOrDefault(g => g.Id == ex.GameId)?.Live?.Version ?? "";
            editor.RememberGame(client.Host, ex.GameId, live);
            if (live.Length > 0) version.Text = ArcadiaPackage.NextVersion(live);
            ShowAccount();
            return true;
        }
        async Task Check()
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var (_, zip, local) = await Build();
                if (zip.Length == 0) { ShowFindings(local, []); status.Text = "Fix the problems marked red, then check again."; return; }
                if (key == null) { ShowFindings(local, []); status.Text = "Checked here. Link to Arcadia to have the arcade check it too."; return; }
                string? gameId = editor.RememberedGameId(client.Host);
                ArcadiaCheck result;
                try { result = await client.CheckAsync(key, zip, gameId, Sending("Sending for a check:"), work!.Token); }
                catch (ArcadiaException ex) when (ex.GameGone && gameId != null) { ShowFindings(local, []); if (!OfferNewGame(ex, gameId)) return; continue; }
                catch (ArcadiaException ex) when (ex.Code == "title-taken") { ShowFindings(local, []); if (!OfferExistingGame(ex)) return; continue; }
                ShowFindings(local, result.Findings);
                status.Text = result.Refused ? "The arcade would refuse this upload: fix the problems marked red."
                    : result.WouldHold ? "The arcade would accept it, but a moderator would have to look at it first (amber)." : "All clear: the arcade would accept this upload" + (gameId == null ? " as a new game." : " as a new version.");
                return;
            }
        }
        async Task Publish()
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (!CanPublish(out string why)) { status.Text = why; return; }
                var (s, zip, local) = await Build();
                if (zip.Length == 0) { ShowFindings(local, []); status.Text = "Fix the problems marked red before publishing."; return; }
                string? gameId = editor.RememberedGameId(client.Host);
                // An update is checked first: the game's details may have been edited on Arcadia since the last publish,
                // and the person chooses whose to keep. Arcadia's are kept unless they say otherwise.
                bool? overwrite = null; ArcadiaGameDetails? keepArcade = null;
                if (gameId != null)
                {
                    ArcadiaCheck pre;
                    try { pre = await client.CheckAsync(key!, zip, gameId, Sending("Checking with Arcadia:"), work!.Token); }
                    catch (ArcadiaException ex) when (ex.GameGone) { ShowFindings(local, []); if (!OfferNewGame(ex, gameId)) return; continue; }
                    catch (ArcadiaException ex) when (ex.Code == "title-taken") { ShowFindings(local, []); if (!OfferExistingGame(ex)) return; continue; }
                    if (pre.Refused) { ShowFindings(local, pre.Findings); status.Text = "The arcade would refuse this upload: fix the problems marked red."; return; }
                    if (pre.Details is { Conflict: true } clash)
                    {
                        LeaderboardQuestion = ArcadiaDetailsMerge.LeaderboardQuestion(clash, leaderboard.IsChecked == true);
                        bool? mine = ChooseDetails != null ? ChooseDetails(clash) : AskDetails(clash);
                        if (mine == null) { status.Text = "Nothing was published."; return; }
                        overwrite = mine; if (mine == false) keepArcade = clash.Arcade;
                    }
                }
                ArcadiaUpload upload;
                try { upload = await client.UploadAsync(key!, gameId, zip, Sending("Uploading:"), work!.Token, overwrite); }
                catch (ArcadiaException ex) when (ex.GameGone && gameId != null) { ShowFindings(local, []); if (!OfferNewGame(ex, gameId)) return; continue; }
                catch (ArcadiaException ex) when (ex.Code == "title-taken") { ShowFindings(local, []); if (!OfferExistingGame(ex)) return; continue; }
                // The game's permanent ID is kept at once, so publishing again updates this game rather than making another.
                editor.RememberGame(client.Host, upload.Game.Id, s.Version);
                editor.Log($"Uploaded {s.Title} {s.Version} to Arcadia ({client.Host}), game {upload.Game.Id}.");
                // Arcadia's details were kept: they come into the project too, so the next publish doesn't ask again.
                string kept = "";
                if (upload.Details is { Used: "arcade" } used && used.Kept.Count > 0) kept = " Kept Arcadia's " + ArcadiaDetailsMerge.Names(used.Kept).ToLowerInvariant() + ".";
                if (keepArcade != null)
                {
                    try { await TakeArcadeDetails(keepArcade); }
                    catch (Exception ex) when (ex is ArcadiaException or InvalidDataException or System.Net.Http.HttpRequestException or TaskCanceledException) { kept += " The details couldn't be copied into this project (" + ex.Message + "): use Load details from Arcadia."; }
                }
                var submission = upload.Submission;
                status.Text = "Publishing…";
                for (int i = 0; submission.Status == "processing" && i < 90; i++)
                {
                    await Task.Delay(2500, work!.Token);
                    submission = await client.SubmissionAsync(key!, submission.Id, work.Token);
                }
                ShowFindings(local, submission.Findings);
                // The arcade's own address for the game (it follows renames); never built here.
                openUrl = OnArcade(client, submission.GameUrl.Length > 0 ? submission.GameUrl : upload.Game.Url, "#/me");
                open.Visibility = Visibility.Visible;
                status.Text = submission.Status switch
                {
                    "live" => (keepArcade?.Title ?? s.Title) + " is live!" + kept,
                    "held" => "Waiting for review." + (string.IsNullOrWhiteSpace(submission.Message) ? "" : " " + submission.Message) + " You'll get an email when a moderator approves or declines it.",
                    "declined" => "Not published." + (string.IsNullOrWhiteSpace(submission.Message) ? "" : " " + submission.Message),
                    "failed" => "Something went wrong on the arcade's side." + (string.IsNullOrWhiteSpace(submission.Message) ? "" : " " + submission.Message) + " It didn't use up an upload: try again.",
                    "processing" => "The arcade is still storing it. Check Publishing history in a minute.",
                    _ => "Status: " + submission.Status + "."
                };
                editor.Log("Arcadia: " + status.Text);
                version.Text = ArcadiaPackage.NextVersion(s.Version);
                try { me = await client.MeAsync(key!, work!.Token); ShowAccount(); } catch (ArcadiaException) { }
                return;
            }
        }
        /// <summary>What the last details prompt asked about the leaderboard, when it was a clash over having one at all.</summary>
        internal string? LeaderboardQuestion;
        /// <summary>Tests look at the prompt as it's shown; it then closes as Cancel.</summary>
        internal Action<Window>? DetailsPromptForTest;
        /// <summary>Asks whose details to keep when they were changed on Arcadia since the last publish: true uses this
        /// project's, false keeps Arcadia's (the default), null publishes nothing.</summary>
        bool? AskDetails(ArcadiaDetails clash)
        {
            string who = clash.EditedBy == "mod" ? " by a moderator" : "";
            string when = clash.EditedAt is long at ? " on " + DateTimeOffset.FromUnixTimeMilliseconds(at).ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture) : "";
            string fields = clash.Changed.Count > 0 ? " (" + ArcadiaDetailsMerge.Names(clash.Changed) + ")" : "";
            var window = new Window { Owner = this, Title = "Details changed on Arcadia", Width = 480, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
            window.SetResourceReference(StyleProperty, typeof(Window));
            var panel = new StackPanel { Margin = new Thickness(16) }; window.Content = panel;
            panel.Children.Add(Wrap(Brushes.White, $"Your game's details were changed on Arcadia{who}{when}{fields}."));
            // A clash over having a leaderboard at all is spelled out: it adds or removes the game's board.
            if (ArcadiaDetailsMerge.LeaderboardQuestion(clash, leaderboard.IsChecked == true) is string question)
                panel.Children.Add(Wrap(Brushes.Khaki, question + (clash.Arcade?.Board != null
                    ? " Use mine removes it. Keep Arcadia's keeps it, and ticks Keep a leaderboard here."
                    : " Use mine turns it back on. Keep Arcadia's leaves it off, and unticks Keep a leaderboard here.")));
            panel.Children.Add(Wrap(Brushes.LightGray, "Keep Arcadia's, or replace them with the ones from Arcadia Studio? The game itself is updated either way. Keeping Arcadia's also copies them into this project, so you won't be asked again."));
            bool? answer = null;
            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) }; panel.Children.Add(bar);
            void Choice(string text, bool? value, bool isDefault = false, bool isCancel = false)
            {
                var b = new Button { Content = text, IsDefault = isDefault, IsCancel = isCancel, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(8, 0, 0, 0) };
                b.Click += (_, _) => { answer = value; window.Close(); }; bar.Children.Add(b);
            }
            Choice("Keep Arcadia's", false, isDefault: true); Choice("Use mine", true); Choice("Cancel", null, isCancel: true);
            if (DetailsPromptForTest != null) window.ContentRendered += (_, _) => { DetailsPromptForTest(window); window.Close(); };
            window.ShowDialog();
            return answer;
        }
        /// <summary>Load details from Arcadia: the form (and the project) take the game's details as they are on Arcadia now.</summary>
        async Task LoadDetails()
        {
            string? gameId = editor.RememberedGameId(client.Host);
            if (key == null || gameId == null) { status.Text = "Publish this game to Arcadia first: there are no details there to load yet."; return; }
            status.Text = "Loading the game's details from Arcadia…";
            var game = await client.GameAsync(key, gameId, work!.Token);
            if (game.ArcadeDetails is not ArcadiaGameDetails details) { status.Text = "Arcadia didn't send this game's details, so nothing changed."; return; }
            await TakeArcadeDetails(details);
            status.Text = "Loaded the details from Arcadia" + (game.EditedAt is long at ? " (last edited on the website " + DateTimeOffset.FromUnixTimeMilliseconds(at).ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture) + ")" : "") + ". Publishing now sends these.";
        }
        async Task TakeArcadeDetails(ArcadiaGameDetails details)
        {
            var merged = await ArcadiaDetailsMerge.Merge(Collect(), details, media => client.MediaAsync(key!, media.Url, work!.Token));
            FillDetails(merged); Apply();
            editor.Log("Arcadia: took the game's details from Arcadia into the project.");
        }
        /// <summary>Puts a game's details into the form: what Load details from Arcadia and Keep Arcadia's show.</summary>
        void FillDetails(PublishSettings s)
        {
            title.Text = s.Title; description.Text = s.Description; controls.Text = s.Controls;
            for (int i = 0; i < 3; i++) genres[i].Text = i < s.Genre.Count ? s.Genre[i] : "";
            mobile.IsChecked = s.Mobile;
            for (int i = 0; i < videos.Length; i++) videos[i].Text = i < s.Videos.Count ? s.Videos[i] : "";
            cover = s.Cover; coverType = s.CoverType;
            gallery.Clear(); gallery.AddRange(s.Screenshots.Select(x => new PublishImage { Type = x.Type, Bytes = x.Bytes }));
            leaderboard.IsChecked = s.Leaderboard; scoresPanel.Visibility = s.Leaderboard ? Visibility.Visible : Visibility.Collapsed;
            var c = s.Scores;
            label.Text = c.Label; Select(format, c.Format); Select(order, c.Order); Select(aggregate, c.Aggregate);
            min.Text = c.Min.ToString(CultureInfo.InvariantCulture); max.Text = c.Max?.ToString(CultureInfo.InvariantCulture) ?? ""; minSeconds.Text = c.MinSeconds.ToString(CultureInfo.InvariantCulture);
            scoreFrom.Text = c.Score.Variable; scorePath.Text = c.Score.Path; whole.IsChecked = c.Round != "none";
            triggers.Children.Clear(); stats.Children.Clear();
            foreach (var t in c.Triggers) AddTrigger(t);
            foreach (var st in c.Stats) AddStat(st);
            if (c.Triggers.Count == 0) AddTrigger(new PublishTrigger());
            ShowPicture(); ShowShots(); ShowVideoNote();
        }
        void ShowFindings(List<ArcadiaFinding> local, List<ArcadiaFinding> arcade)
        {
            findings.Items.Clear();
            void Add(ArcadiaFinding f, string from)
            {
                var (brush, tag) = f.Level switch
                {
                    "block" => (new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x7A)), "Refused"),
                    "hold" => (new SolidColorBrush(Color.FromRgb(0xF4, 0xC7, 0x44)), "Review"),
                    "warn" => (new SolidColorBrush(Color.FromRgb(0x7F, 0xB8, 0xF0)), "Warning"),
                    _ => ((SolidColorBrush)Brushes.Gray, "Info")
                };
                var text = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 640 };
                text.Inlines.Add(new Run(tag + "  ") { Foreground = brush, FontWeight = FontWeights.SemiBold });
                text.Inlines.Add(new Run(f.Message) { Foreground = Brushes.White });
                if (f.File != null) text.Inlines.Add(new Run("  " + f.File + (f.Line is int line ? ":" + line : "")) { Foreground = Brushes.LightGray });
                if (!string.IsNullOrWhiteSpace(f.Snippet)) text.Inlines.Add(new Run("\n" + f.Snippet) { Foreground = Brushes.Gray, FontFamily = new FontFamily("Consolas") });
                text.Inlines.Add(new Run("  (" + from + ")") { Foreground = Brushes.Gray, FontSize = 10 });
                findings.Items.Add(new ListBoxItem { Content = text, Tag = f });
            }
            int Rank(ArcadiaFinding f) => f.Level switch { "block" => 0, "hold" => 1, "warn" => 2, _ => 3 };
            foreach (var f in local.OrderBy(Rank)) Add(f, "here");
            foreach (var f in arcade.OrderBy(Rank)) Add(f, "arcade");
        }
        /// <summary>Double-clicking a finding in a project script opens that script.</summary>
        void OpenFinding()
        {
            if (findings.SelectedItem is not ListBoxItem { Tag: ArcadiaFinding f } || f.File == null) return;
            int at = f.File.IndexOf("script ", StringComparison.Ordinal); if (at < 0) return;
            string name = f.File[(at + 7)..].Trim();
            string? path = editor.project.Scripts.Keys.FirstOrDefault(k => k == name) ?? editor.project.Scripts.Keys.FirstOrDefault(k => k.EndsWith("/" + name, StringComparison.Ordinal));
            if (path == null) return;
            editor.SaveScriptText(); editor.RefreshScripts(path); editor.ShowDock("scripts"); editor.Activate();
        }
        async Task History()
        {
            string? gameId = editor.RememberedGameId(client.Host);
            if (key == null || gameId == null) return;
            var game = await client.GameAsync(key, gameId, work!.Token);
            var window = new Window { Owner = this, Title = "Publishing history: " + game.Title, Width = 640, Height = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new DockPanel { Margin = new Thickness(12) }; window.Content = panel;
            var list = new ListBox();
            foreach (var s in game.Submissions)
            {
                string line = $"#{s.Number}  {s.Version}  {s.Status}  {When(s.CreatedAt)}" + (string.IsNullOrWhiteSpace(s.Message) ? "" : "  · " + s.Message);
                list.Items.Add(new ListBoxItem { Content = new TextBlock { Text = line, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap }, Tag = s });
            }
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(bar, Dock.Bottom); panel.Children.Add(bar);
            var download = new Button { Content = "Download this version…", Margin = new Thickness(0, 0, 8, 0) }; var live = new Button { Content = "Download what's live…" };
            bar.Children.Add(download); bar.Children.Add(live);
            var note = new TextBlock { Foreground = Brushes.LightGray, Margin = new Thickness(12, 4, 0, 0) }; bar.Children.Add(note);
            panel.Children.Add(new TextBlock { Text = game.Live != null ? "Live: " + game.Live.Version + "   " + game.Url : "Not live yet.", Foreground = Brushes.LightGray, Margin = new Thickness(0, 0, 0, 6) });
            DockPanel.SetDock(panel.Children[^1], Dock.Top);
            panel.Children.Add(list);
            async Task Save(long? submission)
            {
                try
                {
                    var (zip, name) = await client.DownloadAsync(key!, gameId, submission);
                    var dialog = new SaveFileDialog { FileName = name, Filter = "Zip|*.zip", DefaultExt = ".zip" };
                    if (dialog.ShowDialog(window) == true) { File.WriteAllBytes(dialog.FileName, zip); note.Text = "Saved " + Path.GetFileName(dialog.FileName) + "."; }
                }
                catch (ArcadiaException ex) { note.Text = ex.Message; }
            }
            download.Click += async (_, _) => { if (list.SelectedItem is ListBoxItem { Tag: ArcadiaSubmission s }) await Save(s.Id); else note.Text = "Pick a version first."; };
            live.Click += async (_, _) => await Save(null);
            window.ShowDialog();
        }
    }

    /// <summary>Linking, like signing in to a TV app: the browser opens the arcade's approval page with the code, and
    /// this window waits for the approval. The device code is never shown or logged.</summary>
    sealed class LinkWindow : Window
    {
        readonly ArcadiaClient client; readonly ArcadiaLinkStart start; readonly CancellationTokenSource stop = new();
        public string Key { get; private set; } = "";
        public ArcadiaCreator? Creator { get; private set; }
        public string Outcome { get; private set; } = "Linking was cancelled.";
        readonly TextBlock state = new() { Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        public LinkWindow(Window owner, ArcadiaClient client, ArcadiaLinkStart start)
        {
            this.client = client; this.start = start; Owner = owner; SetResourceReference(StyleProperty, typeof(Window));
            Title = "Link to Arcadia"; Width = 440; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var panel = new StackPanel { Margin = new Thickness(16) }; Content = panel;
            panel.Children.Add(new TextBlock { Text = "Approve Arcadia Studio in your browser. Your code is", Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = start.UserCode, Foreground = Brushes.White, FontSize = 28, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 8, 0, 0) });
            panel.Children.Add(new TextBlock { Text = "If you don't make games on Arcadia yet, the page asks you to set up a creator profile first (Profile → Make games for Arcadia). This window keeps waiting.", Foreground = Brushes.Gray, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
            panel.Children.Add(state);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            var copy = new Button { Content = "Copy code", Margin = new Thickness(0, 0, 8, 0) }; var again = new Button { Content = "Open the page again", Margin = new Thickness(0, 0, 8, 0) }; var cancel = new Button { Content = "Cancel", IsCancel = true };
            copy.Click += (_, _) => { try { Clipboard.SetText(start.UserCode); state.Text = "Code copied."; } catch { } };
            again.Click += (_, _) => OpenPage();
            cancel.Click += (_, _) => { stop.Cancel(); DialogResult = false; };
            row.Children.Add(copy); row.Children.Add(again); row.Children.Add(cancel); panel.Children.Add(row);
            Closed += (_, _) => stop.Cancel();
            Loaded += async (_, _) => { OpenPage(); await Poll(); };
        }
        void OpenPage() => OpenInBrowser(OnArcade(client, start.VerificationUriComplete, "#/link?code=" + Uri.EscapeDataString(start.UserCode)));
        async Task Poll()
        {
            int interval = Math.Max(1, start.Interval);
            var until = DateTime.UtcNow.AddSeconds(Math.Max(30, start.ExpiresIn));
            state.Text = "Waiting for you to approve…";
            try
            {
                while (DateTime.UtcNow < until)
                {
                    await Task.Delay(TimeSpan.FromSeconds(interval), stop.Token);
                    var poll = await client.PollLinkAsync(start.DeviceCode, stop.Token);
                    switch (poll.State)
                    {
                        case ArcadiaLinkState.Pending: continue;
                        case ArcadiaLinkState.SlowDown: interval = Math.Max(interval + 1, poll.Interval); continue;
                        case ArcadiaLinkState.Approved: Key = poll.Key; Creator = poll.Creator; Outcome = "Linked."; DialogResult = true; return;
                        case ArcadiaLinkState.Denied: Outcome = "Linking was declined."; DialogResult = false; return;
                        case ArcadiaLinkState.Expired: Outcome = "The code expired. Click Link to Arcadia to start again."; DialogResult = false; return;
                    }
                }
                Outcome = "The code expired. Click Link to Arcadia to start again."; DialogResult = false;
            }
            catch (OperationCanceledException) { }
            catch (ArcadiaException ex) { Outcome = ex.Message; if (IsVisible) DialogResult = false; }
        }
    }

    // ---- MCP: prepare and check only; publishing is the person's own click ----
    internal Task<string> McpArcadia(string action, string expected, string settings, string cover, string screenshots, CancellationToken cancellationToken, string leaderboardFile = "") => Dispatcher.InvokeAsync(async () =>
    {
        try
        {
            if (mcpHost == null) throw new InvalidOperationException("MCP server is stopped.");
            var client = NewArcadiaClient(); string? arcadiaKey = ArcadiaKey(client.Host);
            if (action == "status")
            {
                ArcadiaMe? account = null; string? problem = null;
                if (arcadiaKey != null) try { account = await client.MeAsync(arcadiaKey, cancellationToken); } catch (ArcadiaException ex) { problem = ex.Message; if (ex.Code == "bad-key") ForgetArcadiaKey(client.Host); }
                return Json.Write(new
                {
                    arcade = client.Arcade.ToString(), linked = arcadiaKey != null && problem == null, problem,
                    creator = account?.Creator.Name, autoPublish = account?.Creator.AutoPublish, uploadsOpen = account?.Publishing.Enabled, maxUploadMb = account?.Publishing.MaxUploadMb,
                    gameId = RememberedGameId(client.Host), gameInAccount = account == null || RememberedGameId(client.Host) == null ? (bool?)null : account.Games.Any(g => g.Id == RememberedGameId(client.Host)), newGameLimit = account?.Limits.NewGame,
                    settings = project.Publishing, hasCover = project.Publishing.Cover.Length > 0, screenshots = project.Publishing.Screenshots.Count, scoreSources = ArcadiaPackage.StateNames(project),
                    runtimeSha256 = ArcadiaPackage.RuntimeSha256,
                    note = "Publishing itself is done by the person, in File → Publish to Arcadia."
                });
            }
            if (action is not ("prepare" or "check")) throw new InvalidOperationException("action is status, prepare or check.");
            // A leaderboard page from a file, as Import leaderboard… does it: a .lb or a page made in Arcadia Studio becomes an
            // editable page; any other page is used as it is. Either way it becomes the game's leaderboard page.
            if (leaderboardFile.Trim().Length > 0)
            {
                CheckRevision(expected);
                string file = leaderboardFile.Trim();
                if (!Path.IsPathFullyQualified(file) || !File.Exists(file) || !(file.EndsWith(Leaderboards.Extension, StringComparison.OrdinalIgnoreCase) || file.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("leaderboardFile is an absolute path to a .lb file or an .html page.");
                string choice = ImportLeaderboardFile(file, open: false, confirm: _ => true) ?? "";
                project.Publishing.LeaderboardPage = choice; arcadiaDialog?.Reload(); RefreshAll();
                Log("MCP imported " + Path.GetFileName(file) + " as the game's leaderboard page (" + choice + ").");
                expected = Revision(); // the import is this call's own change: settings below apply on top of it
            }
            if (settings.Trim().Length > 0 || cover.Trim().Length > 0 || screenshots.Trim().Length > 0)
            {
                CheckRevision(expected);
                var s = Json.CloneProject(project).Publishing;
                // "capture" takes the open Preview's screen; anything else is an absolute path to a picture.
                async Task<(byte[] Bytes, string Type)> Picture(string source, string what)
                {
                    if (source.Trim() == "capture")
                    {
                        if (activePreview == null || !activePreview.IsOpen) throw new InvalidOperationException("Open Preview first (preview_control open), then capture.");
                        return FitScreenshot(await activePreview.CaptureScreenAsync());
                    }
                    if (!Path.IsPathFullyQualified(source) || !File.Exists(source)) throw new InvalidDataException(what + " is \"capture\" or an absolute path to a PNG, JPEG or WebP.");
                    // Fitted to 1280 × 800 like a picture chosen in the window, unless it already is that size.
                    List<string> problems = [], fitted = [];
                    return ArcadiaDialog.ReadPicture(source, problems, fitted) ?? throw new InvalidDataException(problems[0]);
                }
                if (settings.Trim().Length > 0)
                {
                    var given = Json.Read<PublishSettings>(settings);
                    var node = System.Text.Json.Nodes.JsonNode.Parse(settings)!.AsObject();
                    if (node.ContainsKey("title")) s.Title = given.Title; if (node.ContainsKey("description")) s.Description = given.Description;
                    if (node.ContainsKey("genre")) s.Genre = given.Genre; if (node.ContainsKey("version")) s.Version = given.Version;
                    if (node.ContainsKey("controls")) s.Controls = given.Controls; if (node.ContainsKey("aspectRatio")) s.AspectRatio = given.AspectRatio;
                    if (node.ContainsKey("leaderboard")) s.Leaderboard = given.Leaderboard; if (node.ContainsKey("scores")) s.Scores = given.Scores;
                    if (node.ContainsKey("mobile")) s.Mobile = given.Mobile;
                    if (node.ContainsKey("leaderboardPage"))
                    {
                        string page = given.LeaderboardPage.Trim();
                        if (!(page is "" or "standard" || (page == "file" && s.LeaderboardHtml.Length > 0) || (page.StartsWith("board:") && project.Leaderboards.Any(b => b.Id == page[6..]))))
                            throw new InvalidDataException("leaderboardPage is \"\" (the first leaderboard), \"standard\", or \"board:<id>\" of one of: " + string.Join(", ", project.Leaderboards.Select(b => b.Id)));
                        s.LeaderboardPage = page;
                    }
                    if (node.ContainsKey("videos"))
                    {
                        var links = given.Videos.Select(v => v.Trim()).Where(v => v.Length > 0).ToList();
                        if (links.Count > ArcadiaPackage.MaxVideos) throw new InvalidDataException($"At most {ArcadiaPackage.MaxVideos} video links.");
                        var wrong = links.FirstOrDefault(l => !ArcadiaPackage.IsYouTube(l));
                        if (wrong != null) throw new InvalidDataException("\"" + wrong + "\" isn't a YouTube video link (youtube.com/watch?v=…, youtu.be/… or youtube.com/shorts/…).");
                        s.Videos = links;
                    }
                }
                if (cover.Trim().Length > 0) (s.Cover, s.CoverType) = await Picture(cover, "cover");
                if (screenshots.Trim().Length > 0)
                {
                    var sources = Json.Read<List<string>>(screenshots);
                    if (sources.Count > ArcadiaPackage.MaxScreenshots) throw new InvalidDataException($"At most {ArcadiaPackage.MaxScreenshots} screenshots.");
                    var list = new List<PublishImage>();
                    foreach (var source in sources) { var (bytes, type) = await Picture(source, "each screenshot"); list.Add(new PublishImage { Type = type, Bytes = bytes }); }
                    s.Screenshots = list;
                }
                Change(); project.Publishing = s; RefreshAll();
                // An open Publish window shows them at once (and so can't write its older form back over them).
                arcadiaDialog?.Reload();
                Log("MCP updated the Publish to Arcadia settings.");
            }
            var snapshot = Json.CloneProject(project); var settingsNow = snapshot.Publishing;
            ArcadiaMe? me = null; if (arcadiaKey != null && action == "check") me = await client.MeAsync(arcadiaKey, cancellationToken);
            var files = await Task.Run(() => ArcadiaPackage.Files(snapshot, settingsNow, WysicraftVersion), cancellationToken);
            var local = ArcadiaPackage.Check(snapshot, settingsNow, files, me?.Publishing.MaxUploadMb ?? ArcadiaPackage.DefaultMaxUploadMb);
            string root = Wysicraft.Core.AppFolders.Path("McpExports", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string zipPath = Path.Combine(root, PixelEditor.SafeName(project.Manifest.Id) + "-arcadia.zip");
            byte[] zip = ArcadiaPackage.Zip(files); File.WriteAllBytes(zipPath, zip);
            ArcadiaCheck? arcade = null;
            if (action == "check")
            {
                if (arcadiaKey == null) throw new InvalidOperationException("This computer isn't linked to Arcadia. The person links it in File → Publish to Arcadia.");
                if (local.Any(f => f.Level == "block")) arcade = null;
                else
                    try { arcade = await client.CheckAsync(arcadiaKey, zip, RememberedGameId(client.Host), null, cancellationToken); }
                    catch (ArcadiaException ex) when (ex.GameGone || ex.Code == "title-taken")
                    {
                        throw new ArcadiaException(ex.Status, ex.Code, ex.Message + (ex.Code == "title-taken" ? " Another of the person's games already has this title." : " The saved game can't be updated. The person decides in File → Publish to Arcadia whether to publish it as a new game (its details are kept; the version starts again at 1.0.0)."));
                    }
            }
            return Json.Write(new
            {
                package = zipPath, files = files.Count, bytes = zip.Length, gameJson = System.Text.Json.Nodes.JsonNode.Parse(Encoding.UTF8.GetString(files["game.json"])),
                local, blocked = local.Any(f => f.Level == "block"),
                arcade = arcade == null ? null : new { arcade.Ok, arcade.WouldHold, arcade.Refused, arcade.Findings },
                // Details edited on the website since the last publish: the person is asked in the Publish window whether to
                // keep Arcadia's (the default) or use the project's. An assistant only reports it; it never chooses.
                detailsChanged = arcade?.Details is { Conflict: true } clash ? new { fields = clash.Changed.Select(ArcadiaDetailsMerge.Name).ToList(), clash.EditedBy, clash.EditedAt, leaderboard = ArcadiaDetailsMerge.LeaderboardQuestion(clash, settingsNow.Leaderboard), note = "These were changed on Arcadia since the last publish. When the person publishes, they choose whether to keep Arcadia's (the default) or replace them with the project's; Load details from Arcadia copies Arcadia's into the project." } : null,
                revision = Revision(),
                note = "Nothing was published. The person publishes from File → Publish to Arcadia."
            });
        }
        catch (ArcadiaException ex) { throw new ModelContextProtocol.McpException(ex.Message); }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw new ModelContextProtocol.McpException(ex.Message); }
    }, System.Windows.Threading.DispatcherPriority.Normal, cancellationToken).Task.Unwrap();
}

public sealed partial class DesignerMcpTools
{
    [McpServerTool(Name = "arcadia_publish"), Description("Prepare a game for Arcadia (the web arcade) and check it. This never publishes: the person publishes from File → Publish to Arcadia. action: status (link, account, limits, current settings, score sources; read-only), prepare (build the package zip under LocalAppData/Arcadia Studio/McpExports and run the local upload rules), check (prepare, then the arcade's own dry run; needs this computer linked). settings (optional, saved to the project as one Undo step; only the fields given change): {title, description, genre, version, controls, aspectRatio, mobile, videos, leaderboard, scores, leaderboardPage}. cover (optional; required before publishing): \"capture\" takes it from the open Preview, or an absolute path to a PNG/JPEG/WebP. screenshots (optional): a list of up to 8, each \"capture\" or an absolute path ([] clears the gallery). screenshot is the older name for cover. leaderboardFile (optional): an absolute path to a .lb file or an .html leaderboard page to import. expectedRevision is needed when settings, cover or screenshots are given. Every settings field, the scores block and the leaderboard page choices: guide(topic:\"publish\").")]
    public Task<string> ArcadiaPublish(string action, string expectedRevision = "", ArcadiaSettingsInput? settings = null, string cover = "", List<string>? screenshots = null, string screenshot = "", string leaderboardFile = "", CancellationToken cancellationToken = default) => editor.McpArcadia(action, expectedRevision, McpTypedInputs.ToJson(settings), cover.Length > 0 ? cover : screenshot, McpTypedInputs.List(screenshots), cancellationToken, leaderboardFile);
}
