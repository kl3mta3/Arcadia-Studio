using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Wysicraft.Designer;

// Set up, from the Ask Assistant row on the MCP panel. Everything that needs words lives here, so the row itself
// can stay four buttons: which tool, whether it is installed, how to install it, signing in, the manual command,
// and the server address the config will point at.
public partial class MainWindow
{
    void ShowAssistantSetup()
    {
        var prefs = Prefs();
        var window = new Window { Owner = this, Title = "Set up an assistant", Width = 640, Height = 600, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new StackPanel { Margin = new Thickness(16) };
        window.Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Opacity = 0.85, Margin = new Thickness(0, 0, 0, 10),
            Text = "An assistant is a command-line AI tool that can be pointed at this project's MCP server. It runs on your own account with that tool — Arcadia Studio never holds a key. Once set up, Start opens it in a terminal with the project attached, and Ask Agent can hand it work directly."
        });

        // ---- Which tool ----
        var choices = new List<(string Id, string Name)> { ("", "(none)") };
        choices.AddRange(Assistants.Known.Select(t => (t.Id, t.Name)));
        choices.Add((Assistants.Custom, "Custom command…"));
        var toolRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        toolRow.Children.Add(new TextBlock { Text = "Assistant", Width = 110, VerticalAlignment = VerticalAlignment.Center });
        var tool = new ComboBox { ItemsSource = choices.Select(c => c.Name).ToList(), SelectedIndex = Math.Max(0, choices.FindIndex(c => c.Id == prefs.AssistantTool)) };
        toolRow.Children.Add(tool); root.Children.Add(toolRow);

        var custom = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        TextBox Line(string label, string value, string tip)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), ToolTip = tip };
            row.Children.Add(new TextBlock { Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Center });
            var box = new TextBox { Text = value }; row.Children.Add(box); custom.Children.Add(row); return box;
        }
        var customCommand = Line("Command", prefs.AssistantCustomCommand, "The executable, or its name if it is on PATH.");
        var customArgs = Line("Ask arguments", prefs.AssistantCustomArguments, "Arguments for a headless run. {prompt} becomes the request, {mcpConfig} the path to a config file for this server, {tools} the tool names it may use.");
        var customChat = Line("Chat arguments", prefs.AssistantCustomChatArguments, "Arguments to start it interactively with {mcpConfig} attached.");
        custom.Children.Add(new TextBlock { Text = "A custom tool has to accept an MCP server config and, for Ask Agent, run once with a prompt and exit. Its own documentation says whether it can.", TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11 });
        root.Children.Add(custom);

        // ---- Installed / signed in ----
        var state = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
        root.Children.Add(state);
        var install = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        root.Children.Add(install);
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var check = new Button { Content = "Check again", Margin = new Thickness(0, 0, 8, 0) };
        var signIn = new Button { Content = "Sign in", Margin = new Thickness(0, 0, 8, 0), ToolTip = "Opens a terminal running the tool's own sign-in. Your browser does the rest, on your account." };
        var copyCommand = new Button { Content = "Copy start command", Margin = new Thickness(0, 0, 8, 0), ToolTip = "The PowerShell line that starts the tool with Arcadia Studio attached, for running it yourself." };
        var test = new Button { Content = "Test", Margin = new Thickness(0, 0, 8, 0), ToolTip = "Sends a real prompt through the whole path — the tool, your sign-in, this server — and shows what came back." };
        actions.Children.Add(check); actions.Children.Add(signIn); actions.Children.Add(copyCommand); actions.Children.Add(test);
        root.Children.Add(actions);

        // ---- The server address the config points at ----
        root.Children.Add(new TextBlock { Text = "Server address", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
        root.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 11, Margin = new Thickness(0, 0, 0, 6),
            Text = "The port stays the same between launches and the token is kept, so a config given to an assistant once keeps working. The token is stored protected to this Windows account. Regenerate it if it was ever shared somewhere it shouldn't have been."
        });
        var portRow = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        portRow.Children.Add(new TextBlock { Text = "Port", Width = 110, VerticalAlignment = VerticalAlignment.Center });
        var port = new TextBox { Text = prefs.McpPort.ToString(), Width = 90, HorizontalAlignment = HorizontalAlignment.Left }; portRow.Children.Add(port); root.Children.Add(portRow);
        var tokenRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        var regenerate = new Button { Content = "Regenerate token", Margin = new Thickness(0, 0, 8, 0) };
        var copyConfig = new Button { Content = "Copy MCP config", ToolTip = "The same connection config the MCP panel shows.", IsEnabled = McpRunning };
        tokenRow.Children.Add(regenerate); tokenRow.Children.Add(copyConfig); root.Children.Add(tokenRow);

        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var done = new Button { Content = "Done", IsDefault = true, Padding = new Thickness(14, 3, 14, 3) };
        test.IsEnabled = false;
        bottom.Children.Add(done); root.Children.Add(bottom);

        string ChosenId() => choices[Math.Max(0, tool.SelectedIndex)].Id;
        void Save()
        {
            prefs.AssistantTool = ChosenId();
            prefs.AssistantCustomCommand = customCommand.Text.Trim();
            prefs.AssistantCustomArguments = customArgs.Text.Trim();
            prefs.AssistantCustomChatArguments = customChat.Text.Trim();
            if (int.TryParse(port.Text, out int p) && p is >= 1024 and <= 65535) prefs.McpPort = p;
            SavePrefs();
        }
        async Task Refresh()
        {
            Save();
            custom.Visibility = ChosenId() == Assistants.Custom ? Visibility.Visible : Visibility.Collapsed;
            install.Children.Clear();
            var known = Assistants.Find(ChosenId());
            if (ChosenId().Length == 0) { state.Text = "Choose an assistant."; signIn.IsEnabled = copyCommand.IsEnabled = false; return; }
            state.Text = "Checking…";
            var found = await CheckAssistantAsync();
            if (found.Path == null)
            {
                state.Text = (known?.Name ?? "That command") + " is not installed, or not on PATH.";
                state.Foreground = Brushes.Goldenrod;
                if (known != null)
                {
                    install.Children.Add(new TextBlock { Text = "Install it: paste this into PowerShell, then come back and press Check again.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 2) });
                    var line = new DockPanel();
                    var copy = new Button { Content = "Copy", Margin = new Thickness(6, 0, 0, 0) }; DockPanel.SetDock(copy, Dock.Right);
                    copy.Click += (_, _) => Clipboard.SetText(known.InstallCommand);
                    line.Children.Add(copy);
                    line.Children.Add(new TextBox { Text = known.InstallCommand, IsReadOnly = true, FontFamily = new FontFamily("Consolas") });
                    install.Children.Add(line);
                    install.Children.Add(new TextBlock { Text = known.InstallNote + "  Needs: " + known.Requires, TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11, Margin = new Thickness(0, 4, 0, 0) });
                }
                signIn.IsEnabled = copyCommand.IsEnabled = false;
                return;
            }
            string version = found.Version.Length > 0 ? " · " + found.Version : "";
            switch (found.SignedIn)
            {
                case "yes": state.Text = "Installed" + version + " · signed in. Ready."; state.Foreground = Brushes.LightGreen; break;
                case "no": state.Text = "Installed" + version + " · not signed in. Press Sign in; it uses your own account with this tool."; state.Foreground = Brushes.Goldenrod; break;
                default: state.Text = "Installed" + version + (known == null ? ". Whether it is signed in is up to the tool." : " · sign-in state unknown."); state.Foreground = Brushes.LightGray; break;
            }
            signIn.IsEnabled = known != null && found.SignedIn != "yes";
            copyCommand.IsEnabled = true;
            test.IsEnabled = found.SignedIn != "no" && McpRunning;
            RefreshAssistantRow?.Invoke();
        }
        tool.SelectionChanged += async (_, _) => { try { await Refresh(); } catch (Exception ex) { state.Text = ex.Message; } };
        check.Click += async (_, _) => { try { await Refresh(); } catch (Exception ex) { state.Text = ex.Message; } };
        signIn.Click += (_, _) => Guard(() => { Save(); SignInAssistant(); state.Text = "A terminal opened for sign-in. Finish there, then press Check again."; });
        copyCommand.Click += (_, _) => Guard(() =>
        {
            Save();
            if (!McpRunning) throw new InvalidOperationException("Start MCP first, so the command points at a running server.");
            SaveMcpConfigFile();
            Clipboard.SetText(AssistantChatCommand());
            state.Text = "Copied. Paste it into PowerShell; it starts the assistant with this project attached.";
        });
        regenerate.Click += async (_, _) => { try { Save(); await RegenerateMcpToken(); copyConfig.IsEnabled = McpRunning; state.Text = "Token regenerated. Anything holding the old config needs the new one."; } catch (Exception ex) { state.Text = ex.Message; } };
        copyConfig.Click += (_, _) => Guard(() => { Clipboard.SetText(McpConfigJson()); state.Text = "Config copied."; });
        test.Click += async (_, _) =>
        {
            Save();
            if (!McpRunning) { state.Text = "Start MCP first: the test connects the assistant back through it."; return; }
            test.IsEnabled = false;
            state.Foreground = Brushes.Goldenrod; state.Text = "Starting the assistant…";
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var run = await RunAgentAsync("Call get_project, then reply with exactly: WYSICRAFT_TEST_OK followed by the project's id.", line => Dispatcher.Invoke(() => state.Text = line));
            test.IsEnabled = true;
            if (run.Ok && run.Text.Contains("WYSICRAFT_TEST_OK")) { state.Foreground = Brushes.LightGreen; state.Text = $"Works — it reached this server and answered in {clock.Elapsed.TotalSeconds:0}s: {run.Text.Trim()}"; }
            else if (run.Ok) { state.Foreground = Brushes.Goldenrod; state.Text = "It answered, but not as asked, so check it can reach this server: " + run.Text.Trim(); }
            else { state.Foreground = Brushes.IndianRed; state.Text = "Did not work: " + run.Trouble; }
        };
        done.Click += (_, _) => { Save(); RefreshAssistantRow?.Invoke(); window.Close(); };
        window.Closed += (_, _) => { Save(); RefreshAssistantRow?.Invoke(); };
        _ = Refresh();
        window.ShowDialog();
    }

    /// <summary>The saved config an assistant can be pointed at by hand: %LOCALAPPDATA%\Wysicraft\mcp.json. Same
    /// posture as the config files assistants keep for themselves, and only useful while the server is running.</summary>
    internal string SaveMcpConfigFile()
    {
        string file = Wysicraft.Core.AppFolders.Path("mcp.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, McpConfigJson());
        return file;
    }

    internal Action? RefreshAssistantRow;
}
