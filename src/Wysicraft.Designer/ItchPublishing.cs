using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ModelContextProtocol.Server;
using Wysicraft.Models;
using Wysicraft.Packaging;
using Path = System.IO.Path;
namespace Wysicraft.Designer;

// File → Publish to itch.io: builds go up with butler, itch.io's own uploader, which ships with Arcadia Studio
// (Butler/butler.exe). Signing in runs butler's browser sign-in; the key is then kept DPAPI-protected in preferences and
// handed to butler only while it runs (BUTLER_API_KEY), never left in a file. The game's page itself (title, price,
// pictures) is made and edited on itch.io. An assistant can set up and check over MCP; publishing is the person's click.
public partial class MainWindow
{
    // Self-checks swap in a fake butler (this program with --fake-butler) and a fake itch.io API.
    internal static string? ButlerOverride, ItchApiOverride;
    static readonly HttpClient ItchHttp = new() { Timeout = TimeSpan.FromSeconds(30) };
    ItchApi NewItchApi() => new(ItchHttp, ItchApiOverride ?? "https://api.itch.io");

    /// <summary>The bundled butler: Butler/butler.exe beside the install (artifacts/butler in a development checkout).</summary>
    internal static string? ButlerExe()
    {
        if (ButlerOverride != null) return ButlerOverride;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            foreach (var relative in new[] { "Butler", "artifacts/butler" })
            { string path = Path.Combine(directory.FullName, relative, "butler.exe"); if (File.Exists(path)) return path; }
        return null;
    }

    string? ItchKey()
    {
        string stored = Prefs().ItchKey;
        if (stored.Length == 0) return null;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), null, DataProtectionScope.CurrentUser)); }
        catch (Exception) { Prefs().ItchKey = ""; SavePrefs(); Log("The saved itch.io sign-in couldn't be read, so it was removed. Sign in again to publish."); return null; }
    }
    void StoreItchKey(string key) { Prefs().ItchKey = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), null, DataProtectionScope.CurrentUser)); SavePrefs(); }
    void ForgetItchKey() { if (Prefs().ItchKey.Length == 0) return; Prefs().ItchKey = ""; SavePrefs(); }

    internal sealed record ButlerRun(int Exit, List<Itch.ButlerMessage> Messages, string Errors)
    {
        public string Problem => Itch.Explain(Messages.LastOrDefault(m => m.Type == "error")?.Text is { Length: > 0 } e ? e : Errors.Trim().Length > 0 ? Errors : "butler stopped (" + Exit + ").");
    }
    /// <summary>Runs butler with --json, reporting each message as it comes (on the window's thread). The key, when
    /// given, goes in the environment for this run only.</summary>
    internal async Task<ButlerRun> RunButler(IReadOnlyList<string> args, string? key, Action<Itch.ButlerMessage>? each, CancellationToken cancel)
    {
        string exe = ButlerExe() ?? throw new InvalidOperationException("butler, itch.io's uploader, is missing from this Arcadia Studio install. Reinstalling Arcadia Studio puts it back.");
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
        if (exe.EndsWith("ArcadiaStudio.exe", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add("--fake-butler");
        foreach (var a in args) start.ArgumentList.Add(a);
        start.Environment.Remove("BUTLER_API_KEY");
        if (key != null) start.Environment["BUTLER_API_KEY"] = key;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("butler didn't start.");
        var messages = new List<Itch.ButlerMessage>(); var errors = new StringBuilder();
        var err = Task.Run(async () => { string? line; while ((line = await process.StandardError.ReadLineAsync()) != null) lock (errors) errors.AppendLine(line); });
        using var stop = cancel.Register(() => { try { process.Kill(true); } catch { } });
        string? text;
        while ((text = await process.StandardOutput.ReadLineAsync(cancel)) != null)
        {
            if (Itch.ReadLine(text) is not { } message) continue;
            messages.Add(message);
            if (each != null) await Dispatcher.InvokeAsync(() => each(message));
        }
        await process.WaitForExitAsync(cancel); await err;
        return new(process.ExitCode, messages, errors.ToString());
    }

    ItchDialog? itchDialog;
    void PublishToItch()
    {
        SaveScriptText();
        if (itchDialog != null) { itchDialog.Activate(); return; }
        itchDialog = new ItchDialog(this); itchDialog.Closed += (_, _) => itchDialog = null; itchDialog.Show();
    }

    /// <summary>After an itch.io publish, "Yes" to Arcadia: Publish to Arcadia opens with what carries over filled in
    /// (only where it's still empty), for the person to finish, add a cover and, if they like, a leaderboard.</summary>
    internal void ContinueOnArcadia(string title, string description, string version)
    {
        var s = project.Publishing; bool changed = false;
        void Fill(Func<string> get, Action<string> set, string value) { if (get().Trim().Length == 0 && value.Trim().Length > 0) { if (!changed) Change(); changed = true; set(value.Trim()); } }
        Fill(() => s.Title, v => s.Title = v, title.Length > 0 ? title : project.Manifest.Name);
        Fill(() => s.Description, v => s.Description = v.Length > 600 ? v[..600] : v, description);
        Fill(() => s.Version, v => s.Version = v, version);
        PublishToArcadia();
        arcadiaDialog?.Note("Filled in from your itch.io game. Check the details, add a cover, and set up a leaderboard (and a leaderboard page) if you'd like one.");
    }

    /// <summary>The Publish to itch.io window: account, game, what to upload, check and publish.</summary>
    internal sealed partial class ItchDialog : Window
    {
        readonly MainWindow editor;
        string? key;
        ItchApi.User? user;
        List<ItchApi.Game>? games;
        bool busy; CancellationTokenSource? work;
        readonly TextBlock account = Wrap(), game = Wrap(), status = Wrap();
        readonly Button signIn = new() { Content = "Sign in to itch.io" }, apiKey = new() { Content = "Use an API key…" }, setup = new() { Content = "Set up…" },
            openPage = new() { Content = "Open game page" }, editPage = new() { Content = "Edit on itch.io" }, browserPage = new() { Content = "Open itch.io's Edit game page" },
            check = new() { Content = "Check" }, publish = new() { Content = "Publish", FontWeight = FontWeights.SemiBold };
        readonly CheckBox web = new() { Content = "Web version (plays in the browser)" }, windows = new() { Content = "Windows app" },
            hidden = new() { Content = "Start a new channel hidden" }, ifChanged = new() { Content = "Skip an upload that changes nothing" }, browserDone = new() { Content = "I've done this" };
        readonly TextBox version = new();
        readonly StackPanel browserStep = new();
        readonly ListBox findings = new() { MinHeight = 60, MaxHeight = 200 };
        readonly ProgressBar progress = new() { Height = 6, Minimum = 0, Maximum = 1, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 4) };
        // Self-checks answer the window's questions through this instead of a message box.
        internal Func<string, bool>? Confirm;
        bool Ask(string question) => Confirm?.Invoke(question) ?? MessageBox.Show(this, question, "Publish to itch.io", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
        ItchSettings S => editor.project.Publishing.Itch;

        public ItchDialog(MainWindow editor)
        {
            this.editor = editor; Owner = editor; SetResourceReference(StyleProperty, typeof(Window));
            Title = "Publish to itch.io"; Width = 640; Height = 780; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var root = new DockPanel { Margin = new Thickness(12) }; Content = root;
            var bottom = new StackPanel { Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
            bottom.Children.Add(Heading("Check")); bottom.Children.Add(findings); bottom.Children.Add(progress); bottom.Children.Add(status);
            var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) }; bottom.Children.Add(buttons);
            foreach (var b in new[] { check, publish, openPage }) { b.Margin = new Thickness(0, 0, 8, 0); b.Padding = new Thickness(12, 3, 12, 3); buttons.Children.Add(b); }
            var form = new StackPanel(); root.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            form.Children.Add(Heading("Account"));
            form.Children.Add(account);
            var accountButtons = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
            foreach (var b in new[] { signIn, apiKey }) { b.Margin = new Thickness(0, 0, 8, 0); b.Padding = new Thickness(10, 2, 10, 2); accountButtons.Children.Add(b); }
            form.Children.Add(accountButtons);
            form.Children.Add(Wrap(Brushes.Gray, "Sign in opens itch.io in your browser to approve Arcadia Studio's uploader (butler). No password is typed here. Your games' pages, prices and pictures are edited on itch.io."));

            form.Children.Add(Heading("Game"));
            form.Children.Add(game);
            var gameButtons = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
            foreach (var b in new[] { setup, editPage }) { b.Margin = new Thickness(0, 0, 8, 0); b.Padding = new Thickness(10, 2, 10, 2); gameButtons.Children.Add(b); }
            form.Children.Add(gameButtons);

            form.Children.Add(Heading("Upload"));
            var s = S;
            web.IsChecked = s.Web; windows.IsChecked = s.Windows; hidden.IsChecked = s.Hidden; ifChanged.IsChecked = s.IfChanged; browserDone.IsChecked = s.BrowserStepDone;
            foreach (var c in new[] { web, windows }) { c.Margin = new Thickness(0, 3, 0, 3); form.Children.Add(c); }
            form.Children.Add(Wrap(Brushes.Gray, "Either or both. Each goes to its own channel (Set up…). Leaderboards and leaderboard pages are Arcadia's: on itch.io the game plays without them."));
            version.Text = s.LastVersion.Length > 0 ? ArcadiaPackage.NextVersion(s.LastVersion) : editor.project.Publishing.Version.Length > 0 ? editor.project.Publishing.Version : editor.project.Manifest.Version;
            form.Children.Add(Row("Version", version, "Shown to players and in the itch.io app. Raise it each time you publish."));
            foreach (var c in new[] { hidden, ifChanged }) { c.Margin = new Thickness(0, 3, 0, 3); form.Children.Add(c); }

            // The once-only browser step for a web game.
            browserStep.Children.Add(Heading("Playing in the browser (once)"));
            browserStep.Children.Add(Wrap(Brushes.White, "itch.io only lets its own page set this: after the first web upload, on the Edit game page set Kind of project to HTML, tick \"This file will be played in the browser\" beside the web upload, and Save. Later uploads keep it."));
            var stepRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) }; browserPage.Margin = new Thickness(0, 0, 12, 0); browserPage.Padding = new Thickness(10, 2, 10, 2);
            stepRow.Children.Add(browserPage); stepRow.Children.Add(browserDone); browserStep.Children.Add(stepRow);
            form.Children.Add(browserStep);

            signIn.Click += async (_, _) => await Guard(key == null ? SignIn : SignOut);
            apiKey.Click += async (_, _) => await Guard(UseApiKey);
            setup.Click += async (_, _) => await Guard(Setup);
            editPage.Click += (_, _) => OpenInBrowser(Itch.EditPageOf(S));
            openPage.Click += (_, _) => OpenInBrowser(Itch.PageOf(S.Target));
            browserPage.Click += (_, _) => OpenInBrowser(Itch.EditPageOf(S));
            check.Click += async (_, _) => await Guard(Check);
            publish.Click += async (_, _) => await Guard(Publish);
            foreach (var c in new[] { web, windows, hidden, ifChanged, browserDone }) c.Click += (_, _) => { Apply(); ShowGame(); };
            Closing += (_, e) => { if (busy) { e.Cancel = true; status.Text = "Wait for the upload to finish before closing."; return; } Apply(); };
            Loaded += async (_, _) => await Guard(Refresh);
        }

        // ---- Layout helpers ----
        static TextBlock Wrap(Brush? brush = null, string text = "") => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = brush ?? Brushes.White, Margin = new Thickness(0, 2, 0, 2) };
        static TextBlock Heading(string text) => new() { Text = text.ToUpperInvariant(), Foreground = Brushes.LightSkyBlue, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) };
        static Grid Row(string name, UIElement input, string help)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(new TextBlock { Text = name, Foreground = Brushes.White, Margin = new Thickness(0, 4, 8, 0) });
            var right = new StackPanel(); Grid.SetColumn(right, 1); right.Children.Add(input);
            if (help.Length > 0) right.Children.Add(new TextBlock { Text = help, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
            grid.Children.Add(right); return grid;
        }

        /// <summary>The window's choices back into the project (one Undo step) when anything changed.</summary>
        void Apply()
        {
            var was = S; var now = Json.Clone(was);
            now.Web = web.IsChecked == true; now.Windows = windows.IsChecked == true; now.Hidden = hidden.IsChecked == true; now.IfChanged = ifChanged.IsChecked == true; now.BrowserStepDone = browserDone.IsChecked == true;
            if (Json.Write(now) == Json.Write(was)) return;
            editor.Change(); editor.project.Publishing.Itch = now;
        }
        void Set(Action<ItchSettings> change) { editor.Change(); var now = Json.Clone(S); change(now); editor.project.Publishing.Itch = now; }

        async Task Guard(Func<Task> action)
        {
            if (busy) return;
            busy = true; SetEnabled(false); work = new CancellationTokenSource();
            try { await action(); }
            catch (ItchException ex) { status.Text = ex.Message; editor.Log("itch.io: " + ex.Message); }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or HttpRequestException or TaskCanceledException or UnauthorizedAccessException or Win32Exception or FileNotFoundException)
            { status.Text = ex is TaskCanceledException ? "Stopped." : ex.Message; editor.Log("itch.io: " + status.Text); }
            finally { busy = false; work = null; progress.Visibility = Visibility.Collapsed; SetEnabled(true); }
        }
        void SetEnabled(bool on)
        {
            foreach (var b in new UIElement[] { signIn, apiKey, setup, check, web, windows, hidden, ifChanged }) b.IsEnabled = on;
            publish.IsEnabled = on && key != null && Itch.Target(S.Target) != null;
            editPage.IsEnabled = openPage.IsEnabled = Itch.Target(S.Target) != null;
            apiKey.Visibility = key == null ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---- Account ----
        async Task Refresh()
        {
            key = editor.ItchKey(); user = null; games = null;
            signIn.Content = key == null ? "Sign in to itch.io" : "Sign out";
            if (editor.ButlerExeOrNull() == null) status.Text = "butler, itch.io's uploader, is missing from this Arcadia Studio install. Reinstalling Arcadia Studio puts it back.";
            if (key == null) { account.Text = "Not signed in to itch.io."; ShowGame(); return; }
            account.Text = "Checking your itch.io account…";
            try { user = await editor.NewItchApi().ProfileAsync(key, work!.Token); account.Text = "Signed in to itch.io as " + (user.DisplayName.Length > 0 ? user.DisplayName + " (" + user.Username + ")" : user.Username) + "."; }
            catch (ItchException ex) when (ex.Scope) { account.Text = "Signed in to itch.io. (This sign-in can upload; itch.io doesn't let it read your account name.)"; }
            catch (ItchException ex) when (ex.Invalid) { editor.ForgetItchKey(); key = null; account.Text = "The itch.io sign-in isn't valid anymore (" + ex.Message + "). Sign in again."; signIn.Content = "Sign in to itch.io"; }
            ShowGame();
        }
        void ShowGame()
        {
            var s = S; game.Inlines.Clear();
            if (Itch.Target(s.Target) is not string target) { game.Inlines.Add("Not set up: choose which itch.io game the uploads go to."); }
            else
            {
                game.Inlines.Add("Uploads go to "); game.Inlines.Add(new Bold(new Run(s.GameTitle.Length > 0 ? s.GameTitle : target)));
                game.Inlines.Add(" (" + target + ")");
                var channels = new List<string>(); if (s.Web) channels.Add("web → " + s.WebChannel); if (s.Windows) channels.Add("Windows → " + s.WindowsChannel);
                game.Inlines.Add(channels.Count > 0 ? ", channels " + string.Join(", ", channels) + "." : ".");
                if (s.LastVersion.Length > 0) game.Inlines.Add(" Last published: " + s.LastVersion + ".");
            }
            browserStep.Visibility = s.Web ? Visibility.Visible : Visibility.Collapsed;
            SetEnabled(!busy);
        }
        async Task SignIn()
        {
            string folder = Wysicraft.Core.AppFolders.Path("ItchSignIn", Guid.NewGuid().ToString("N")), identity = Path.Combine(folder, "key");
            Directory.CreateDirectory(folder);
            try
            {
                status.Text = "Starting the itch.io sign-in…";
                // Five minutes to approve in the browser.
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(work!.Token); limit.CancelAfter(TimeSpan.FromMinutes(5));
                var run = await editor.RunButler(["--json", "login", "-i", identity], null, m =>
                {
                    if (m.Type == "login" && m.Uri is string uri && Uri.TryCreate(uri, UriKind.Absolute, out var address) && address.Host.EndsWith("itch.io", StringComparison.OrdinalIgnoreCase))
                    { OpenInBrowser(uri); status.Text = "Approve Arcadia Studio's uploader on itch.io in your browser. This window waits."; }
                }, limit.Token);
                if (run.Exit != 0 || !File.Exists(identity)) { status.Text = "Not signed in: " + run.Problem; return; }
                string found = File.ReadAllText(identity).Trim();
                if (found.Length == 0) { status.Text = "Not signed in: itch.io didn't give a key."; return; }
                editor.StoreItchKey(found);
                editor.Log("Signed in to itch.io for publishing.");
                status.Text = "Signed in.";
            }
            finally { try { Directory.Delete(folder, true); } catch { } }
            await Refresh();
        }
        async Task SignOut()
        {
            if (!Ask("Sign out of itch.io on this computer? (To stop the key working everywhere, remove it on itch.io's API keys page too.)")) return;
            editor.ForgetItchKey(); editor.Log("Signed out of itch.io."); status.Text = "Signed out.";
            await Refresh();
        }
        /// <summary>An API key from itch.io's settings page, for when the browser sign-in can't work here.</summary>
        async Task UseApiKey()
        {
            var ask = new Window { Owner = this, Title = "Use an itch.io API key", SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
            ask.SetResourceReference(StyleProperty, typeof(Window));
            var panel = new StackPanel { Margin = new Thickness(14), Width = 420 }; ask.Content = panel;
            panel.Children.Add(Wrap(Brushes.White, "On itch.io, open Settings → API keys, generate a key and copy it here. It's kept encrypted for your Windows account."));
            var open = new Button { Content = "Open itch.io's API keys page", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 8), Padding = new Thickness(10, 2, 10, 2) };
            open.Click += (_, _) => OpenInBrowser(Itch.ApiKeysPage); panel.Children.Add(open);
            var box = new PasswordBox(); panel.Children.Add(box);
            var ok = new Button { Content = "Use this key", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(12, 2, 12, 2) };
            ok.Click += (_, _) => ask.DialogResult = true; panel.Children.Add(ok);
            if (ask.ShowDialog() != true || box.Password.Trim().Length == 0) return;
            string candidate = box.Password.Trim();
            var who = await editor.NewItchApi().ProfileAsync(candidate, work!.Token);
            editor.StoreItchKey(candidate); editor.Log("Signed in to itch.io as " + who.Username + " with an API key.");
            await Refresh();
        }

        // ---- Set up: which game, which channels ----
        async Task Setup()
        {
            if (key != null && games == null)
                try { games = await editor.NewItchApi().GamesAsync(key, work!.Token); }
                catch (ItchException ex) when (ex.Scope) { games = []; }
            var s = S;
            var ask = new Window { Owner = this, Title = "Set up itch.io publishing", Width = 520, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
            ask.SetResourceReference(StyleProperty, typeof(Window));
            var panel = new StackPanel { Margin = new Thickness(14) }; ask.Content = panel;
            var list = new ListBox { Height = 150, DisplayMemberPath = "Title" };
            var address = new TextBox { Text = s.Target };
            var mine = (games ?? []).Where(g => g.Target != null).ToList();
            if (mine.Count > 0)
            {
                panel.Children.Add(Wrap(Brushes.White, "Your itch.io games:"));
                list.ItemsSource = mine; list.SelectedItem = mine.FirstOrDefault(g => g.Target == s.Target || g.Id == s.GameId);
                list.SelectionChanged += (_, _) => { if (list.SelectedItem is ItchApi.Game g) address.Text = g.Target!; };
                panel.Children.Add(list);
                panel.Children.Add(Wrap(Brushes.Gray, "Or type another game's address:"));
            }
            else panel.Children.Add(Wrap(Brushes.White, key == null ? "Sign in to pick from your games, or type the game's itch.io address:" : "The game's itch.io address (for example https://you.itch.io/my-game, or you/my-game):"));
            panel.Children.Add(address);
            var create = new Button { Content = "Create a new game on itch.io", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(10, 2, 10, 2) };
            create.Click += (_, _) => OpenInBrowser(Itch.NewGamePage); panel.Children.Add(create);
            panel.Children.Add(Wrap(Brushes.Gray, "itch.io games are made on itch.io (Kind of project: HTML for a browser game). Make it, then come back and choose it here."));
            var webChannel = new TextBox { Text = s.WebChannel }; var winChannel = new TextBox { Text = s.WindowsChannel };
            panel.Children.Add(Row("Web channel", webChannel, "Where the web version goes, for example html5."));
            panel.Children.Add(Row("Windows channel", winChannel, "Where the Windows app goes: a name with win or windows in it."));
            var note = Wrap(new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x7A))); panel.Children.Add(note);
            var ok = new Button { Content = "Use this", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(12, 2, 12, 2) };
            ok.Click += (_, _) =>
            {
                if (Itch.Target(address.Text) == null) { note.Text = "That isn't an itch.io game address: https://you.itch.io/my-game or you/my-game."; return; }
                if (!System.Text.RegularExpressions.Regex.IsMatch(webChannel.Text.Trim(), "^[a-z0-9][a-z0-9-]*$") || !System.Text.RegularExpressions.Regex.IsMatch(winChannel.Text.Trim(), "^[a-z0-9][a-z0-9-]*$")) { note.Text = "Channels are lowercase letters, digits and dashes."; return; }
                ask.DialogResult = true;
            };
            panel.Children.Add(ok);
            if (ask.ShowDialog() != true) return;
            string target = Itch.Target(address.Text)!;
            var picked = mine.FirstOrDefault(g => g.Target == target);
            Set(n =>
            {
                if (n.Target != target) { n.LastVersion = ""; n.BrowserStepDone = false; }
                n.Target = target; n.GameId = picked?.Id ?? (n.Target == s.Target ? s.GameId : 0); n.GameTitle = picked?.Title ?? (n.Target == s.Target ? s.GameTitle : "");
                n.GameUrl = picked?.Url ?? Itch.PageOf(target); n.WebChannel = webChannel.Text.Trim(); n.WindowsChannel = winChannel.Text.Trim();
            });
            browserDone.IsChecked = S.BrowserStepDone;
            status.Text = "Uploads go to " + target + "."; ShowGame();
        }

        // ---- Check and publish ----
        /// <summary>The builds, made from a copy of the project taken here (on the window's thread) and built in the background.</summary>
        Task<List<Itch.Build>> Builds()
        {
            editor.SaveScriptText(); Apply();
            var project = Json.CloneProject(editor.project); var s = Json.Clone(S); string? host = s.Windows ? editor.AppHostExe() : null;
            return Task.Run(() => Itch.Builds(project, s, host));
        }
        string BuildFolder(Itch.Build build) => Wysicraft.Core.AppFolders.Path("ItchBuilds", editor.project.Manifest.Id + "-" + build.Kind);
        void ShowFindings(IEnumerable<ArcadiaFinding> found, IEnumerable<string> notes)
        {
            findings.Items.Clear();
            foreach (var f in found.OrderBy(f => f.Level switch { "block" => 0, "warn" => 1, _ => 2 }))
            {
                var (brush, tag) = f.Level switch { "block" => ((Brush)new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x7A)), "Can't publish"), "warn" => (new SolidColorBrush(Color.FromRgb(0x7F, 0xB8, 0xF0)), "Warning"), _ => (Brushes.Gray, "Info") };
                var text = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 560 };
                text.Inlines.Add(new Run(tag + "  ") { Foreground = brush, FontWeight = FontWeights.SemiBold }); text.Inlines.Add(new Run(f.Message) { Foreground = Brushes.White });
                findings.Items.Add(new ListBoxItem { Content = text, Tag = f });
            }
            foreach (var n in notes) findings.Items.Add(new ListBoxItem { Content = new TextBlock { Text = n, TextWrapping = TextWrapping.Wrap, MaxWidth = 560, Foreground = Brushes.LightGray, FontFamily = new FontFamily("Consolas"), FontSize = 11 } });
        }
        async Task Check()
        {
            var local = Itch.Check(S, version.Text);
            if (local.Any(f => f.Level == "block")) { ShowFindings(local, []); status.Text = "Fix the problems marked red, then check again."; return; }
            status.Text = "Making the builds…";
            var builds = await Builds();
            var notes = new List<string>();
            foreach (var build in builds) notes.Add($"{build.Kind}: {build.Files.Count} files, {build.Files.Sum(f => (long)f.Value.Length) / 1048576.0:0.0} MB → {S.Target}:{build.Channel}");
            if (key == null) { ShowFindings(local, notes); status.Text = "Checked here. Sign in to itch.io to see what an upload would change."; return; }
            foreach (var build in builds)
            {
                string folder = BuildFolder(build); await Task.Run(() => Itch.WriteBuild(build, folder));
                status.Text = "Asking itch.io what the " + build.Kind + " upload would change…";
                var run = await editor.RunButler(Itch.PushArguments(folder, S, build.Channel, version.Text, preview: true), key, null, work!.Token);
                if (run.Exit != 0) { local.Add(new("block", "butler", $"{build.Kind}: {run.Problem}")); continue; }
                var summary = run.Messages.LastOrDefault(m => m.Text.Contains("Comparison", StringComparison.OrdinalIgnoreCase) || m.Text.Contains("new,", StringComparison.OrdinalIgnoreCase))?.Text ?? run.Messages.LastOrDefault(m => m.Type is "log" or "text" && m.Text.Length > 0)?.Text ?? "ready";
                notes.Add($"{build.Kind} → {build.Channel}: {summary}");
            }
            ShowFindings(local, notes);
            status.Text = local.Any(f => f.Level == "block") ? "itch.io wouldn't take this: see the red lines." : "All clear: Publish sends " + string.Join(" and ", builds.Select(b => b.Kind == "web" ? "the web version" : "the Windows app")) + ".";
        }
        async Task Publish()
        {
            if (key == null) { status.Text = "Sign in to itch.io first."; return; }
            var local = Itch.Check(S, version.Text);
            if (local.Any(f => f.Level == "block")) { ShowFindings(local, []); status.Text = "Fix the problems marked red before publishing."; return; }
            status.Text = "Making the builds…";
            var builds = await Builds();
            var done = new List<string>(); string v = version.Text.Trim();
            foreach (var build in builds)
            {
                string folder = BuildFolder(build); await Task.Run(() => Itch.WriteBuild(build, folder));
                progress.Value = 0; progress.Visibility = Visibility.Visible;
                string what = build.Kind == "web" ? "the web version" : "the Windows app";
                status.Text = "Uploading " + what + "…";
                var run = await editor.RunButler(Itch.PushArguments(folder, S, build.Channel, v, preview: false), key, m =>
                {
                    if (m.Progress is double p) { progress.Value = Math.Clamp(p, 0, 1); status.Text = $"Uploading {what}: {p:P0}"; }
                }, work!.Token);
                if (run.Exit != 0)
                {
                    status.Text = $"{(done.Count > 0 ? string.Join(" and ", done) + " went up, but " : "")}{what} didn't: {run.Problem}";
                    editor.Log("itch.io: " + status.Text);
                    if (done.Count > 0) Set(n => n.LastVersion = v);
                    return;
                }
                done.Add(what);
            }
            Set(n => n.LastVersion = v);
            string page = Itch.PageOf(S.Target);
            status.Text = $"Published {v} to itch.io: {string.Join(" and ", done)}. itch.io takes a moment to process the files." + (S.Web && !S.BrowserStepDone ? " One more step for the web version: see Playing in the browser." : "");
            editor.Log("itch.io: published " + v + " (" + string.Join(", ", done) + ") to " + S.Target + ".");
            ShowFindings([], builds.Select(b => $"{b.Kind} → {S.Target}:{b.Channel} {v}"));
            version.Text = ArcadiaPackage.NextVersion(v);
            ShowGame();
            await Task.Yield();
            OfferArcadia(v);
        }
        /// <summary>After an itch.io publish: the game isn't on Arcadia yet, so offer it (once a "no" is given, not again).</summary>
        void OfferArcadia(string v)
        {
            var s = S;
            if (s.NoArcadiaNudge || editor.RememberedGameId(new Uri(editor.ArcadeUrlForNudge).Host) != null) return;
            string name = s.GameTitle.Length > 0 ? s.GameTitle : editor.project.Publishing.Title.Length > 0 ? editor.project.Publishing.Title : editor.project.Manifest.Name;
            if (!Ask("Arcadia is a community-driven arcade with built-in leaderboards. Would you like to publish " + name + " there too?\n\nYes opens Publish to Arcadia with what we can fill in from itch.io, for you to finish."))
            { Set(n => n.NoArcadiaNudge = true); return; }
            var picked = games?.FirstOrDefault(g => g.Target == s.Target);
            editor.ContinueOnArcadia(name, picked?.ShortText ?? "", v);
        }
        internal void CloseForTest() { busy = false; Close(); }
    }

    internal string? ButlerExeOrNull() => ButlerExe();
    internal string ArcadeUrlForNudge => ArcadeUrl;

    // ---- MCP: set up and check only; publishing is the person's own click ----
    internal Task<string> McpItch(string action, string expected, string settings, CancellationToken cancellationToken) => Dispatcher.InvokeAsync(async () =>
    {
        try
        {
            if (mcpHost == null) throw new InvalidOperationException("MCP server is stopped.");
            string? itchKey = ItchKey();
            if (action == "status")
                return Json.Write(new { signedIn = itchKey != null, butler = ButlerExe() != null, settings = project.Publishing.Itch, gamePage = Itch.Target(project.Publishing.Itch.Target) is string t ? Itch.PageOf(t) : null, note = "Publishing itself is done by the person, in File → Publish to itch.io." });
            if (action is not ("prepare" or "check")) throw new InvalidOperationException("action is status, prepare or check.");
            if (settings.Trim().Length > 0)
            {
                CheckRevision(expected);
                var node = System.Text.Json.Nodes.JsonNode.Parse(settings)!.AsObject(); var s = Json.Clone(project.Publishing.Itch);
                if (node.ContainsKey("target")) { var t = Itch.Target((string?)node["target"] ?? "") ?? throw new InvalidDataException("target is an itch.io game: https://user.itch.io/game or user/game."); if (t != s.Target) { s.LastVersion = ""; s.BrowserStepDone = false; s.GameId = 0; s.GameTitle = ""; } s.Target = t; s.GameUrl = Itch.PageOf(t); }
                if (node.ContainsKey("web")) s.Web = (bool)node["web"]!; if (node.ContainsKey("windows")) s.Windows = (bool)node["windows"]!;
                if (node.ContainsKey("webChannel")) s.WebChannel = ((string?)node["webChannel"] ?? "").Trim(); if (node.ContainsKey("windowsChannel")) s.WindowsChannel = ((string?)node["windowsChannel"] ?? "").Trim();
                if (node.ContainsKey("hidden")) s.Hidden = (bool)node["hidden"]!; if (node.ContainsKey("ifChanged")) s.IfChanged = (bool)node["ifChanged"]!;
                Change(); project.Publishing.Itch = s; RefreshAll(); Log("MCP updated the Publish to itch.io settings.");
            }
            var snapshot = Json.CloneProject(project); var settingsNow = snapshot.Publishing.Itch;
            string v = settingsNow.LastVersion.Length > 0 ? ArcadiaPackage.NextVersion(settingsNow.LastVersion) : snapshot.Publishing.Version.Length > 0 ? snapshot.Publishing.Version : snapshot.Manifest.Version;
            var local = Itch.Check(settingsNow, v);
            var results = new List<object>();
            if (!local.Any(f => f.Level == "block"))
            {
                var builds = await Task.Run(() => Itch.Builds(snapshot, settingsNow, settingsNow.Windows ? AppHostExe() : null), cancellationToken);
                foreach (var build in builds)
                {
                    string folder = Wysicraft.Core.AppFolders.Path("McpExports", Guid.NewGuid().ToString("N"), "itch-" + build.Kind);
                    await Task.Run(() => Itch.WriteBuild(build, folder), cancellationToken);
                    string? preview = null;
                    if (action == "check")
                    {
                        if (itchKey == null) throw new InvalidOperationException("This computer isn't signed in to itch.io. The person signs in from File → Publish to itch.io.");
                        var run = await RunButler(Itch.PushArguments(folder, settingsNow, build.Channel, v, preview: true), itchKey, null, cancellationToken);
                        preview = run.Exit == 0 ? string.Join("\n", run.Messages.Where(m => m.Text.Length > 0).Select(m => m.Text).TakeLast(12)) : "butler: " + run.Problem;
                    }
                    results.Add(new { kind = build.Kind, channel = build.Channel, folder, files = build.Files.Count, bytes = build.Files.Sum(f => (long)f.Value.Length), preview });
                }
            }
            return Json.Write(new { target = settingsNow.Target, version = v, local, blocked = local.Any(f => f.Level == "block"), builds = results, revision = Revision(), note = "Nothing was uploaded. The person publishes from File → Publish to itch.io." });
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw new ModelContextProtocol.McpException(ex.Message); }
    }, System.Windows.Threading.DispatcherPriority.Normal, cancellationToken).Task.Unwrap();
}

public sealed partial class DesignerMcpTools
{
    [McpServerTool(Name = "itch_publish"), Description("Set up and check publishing to itch.io (with butler, itch.io's uploader, bundled with Arcadia Studio). This never uploads: the person publishes from File → Publish to itch.io, and signs in there. action: status (sign-in, settings; read-only), prepare (build the uploads under LocalAppData/Arcadia Studio/McpExports and run the local checks), check (prepare, then butler push-preview: what each upload would change; needs the person signed in). settings (optional JSON, saved as one Undo step): {target: \"https://user.itch.io/game\" or \"user/game\" (the game must already exist on itch.io; its page, price and pictures are edited on itch.io), web: bool (the browser version), windows: bool (the Windows app), webChannel (e.g. html5), windowsChannel (needs win/windows in the name), hidden: bool, ifChanged: bool}. A browser game needs a one-time step on itch.io's Edit game page after its first upload: Kind of project = HTML and 'played in the browser' ticked. expectedRevision is needed when settings are given.")]
    public Task<string> ItchPublish(string action, string expectedRevision = "", string settings = "", CancellationToken cancellationToken = default) => editor.McpItch(action, expectedRevision, settings, cancellationToken);
}
