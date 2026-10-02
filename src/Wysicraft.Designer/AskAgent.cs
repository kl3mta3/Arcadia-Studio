using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wysicraft.Models;
namespace Wysicraft.Designer;

// "Ask Agent", at the top of the Inspector: ask the assistant about the selected control, or have it draw the
// picture for an image or sprite. The ask goes on the same queue the Input creator uses — pending_requests hands it
// over along with the control, the project and the art already in it, and the assistant does the work with the
// ordinary tools (apply_edits, pixel_art) and replies.
//
// It needs MCP running, so the button says so rather than being a dead control with no explanation.
public partial class MainWindow
{
    /// <summary>The Ask Agent button that sits above everything else in the Inspector.</summary>
    void AddAskAgent(Panel panel, Element? element)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var ask = new Button
        {
            Content = "Ask Agent",
            Padding = new Thickness(10, 2, 10, 2),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsEnabled = McpRunning,
            ToolTip = McpRunning
                ? (element == null ? "Ask the assistant about this screen." : $"Ask the assistant about \"{element.Id}\" — how to do something with it, or to change it for you.")
                : "Start MCP on the main window first: the assistant is what answers."
        };
        DockPanel.SetDock(ask, Dock.Right);
        ask.Click += (_, _) => Guard(() => ShowAskAgent(element, "help"));
        row.Children.Add(ask);

        row.Children.Add(new TextBlock());   // eats the remaining width so the buttons stay right-aligned
        panel.Children.Add(row);
    }

    void ShowAskAgent(Element? element, string kind)
    {
        if (!McpRunning) throw new InvalidOperationException("Start MCP on the main window first — the assistant is what answers.");
        bool art = kind == "art";
        string about = element?.Id ?? ui.Id;
        var window = new Window
        {
            Owner = this, Title = (art ? "Draw it · " : "Ask Agent · ") + about,
            Width = 560, Height = 380, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var root = new DockPanel { Margin = new Thickness(12) }; window.Content = root;
        var head = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
            Text = art
                ? $"Describe the picture you want for \"{about}\". The assistant can see the images already in this project, so it can match their size and style — say so if you want it to."
                : element == null
                    ? $"Ask about the screen \"{about}\". The assistant can see the whole project and can make the change itself."
                    : $"Ask about \"{about}\" — a {element.Type}. The assistant can see the whole project and can make the change itself."
        };
        DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        // The reply can be long, so it lives in a read-only box that scrolls and can be copied from; the one-line
        // status above it says what is happening while the assistant works.
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Opacity = 0.85 };
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var reply = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 220, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed, Foreground = Brushes.LightGreen };
        DockPanel.SetDock(reply, Dock.Bottom); root.Children.Add(reply);

        var examples = new TextBlock
        {
            Opacity = 0.55, FontSize = 10, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap,
            Text = art
                ? "For example: \"a 16×16 brass key, side on, same palette as the other tiles\" or \"a 32×32 four-frame walk cycle for a small robot\"."
                : "For example: \"make this fade in when the screen opens\", \"why doesn't this move when I press left?\", \"give it a health bar above it\"."
        };
        DockPanel.SetDock(examples, Dock.Bottom); root.Children.Add(examples);
        // The typing box goes in last on purpose: a DockPanel gives the remaining space to its final child, and
        // anything added after it would dock this to the left as a narrow strip instead.
        var want = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 90 };
        root.Children.Add(want);

        var send = new Button { Content = art ? "Ask it to draw this" : "Ask", IsDefault = true, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 3, 12, 3) };
        var close = new Button { Content = "Close", IsCancel = true, Padding = new Thickness(12, 3, 12, 3) };
        // Speak instead of typing: listens until you pause, writes what it heard into the box and asks.
        var mic = new Button
        {
            Content = "🎤 Speak", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 3, 12, 3),
            IsEnabled = SpeechToText.Available,
            ToolTip = SpeechToText.Available ? "Say what you want; it's asked when you stop talking. Click again to stop listening." : "Speech recognition isn't installed with this copy of Arcadia Studio."
        };
        var level = new ProgressBar { Width = 60, Height = 6, Maximum = 1, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        buttons.Children.Add(level); buttons.Children.Add(mic); buttons.Children.Add(send); buttons.Children.Add(close);
        Microphone? listening = null; bool asked = false;
        mic.Click += async (_, _) =>
        {
            if (listening != null) { listening.Stop(); return; }
            asked = false;
            try
            {
                listening = new Microphone(Prefs().Microphone);
                mic.Content = "■ Stop"; level.Visibility = Visibility.Visible; send.IsEnabled = false;
                status.Text = "Listening… say what you want, then pause."; status.Foreground = Brushes.Goldenrod;
                string heard = await ListenAsync(v => Dispatcher.BeginInvoke(() => level.Value = v), listening);
                if (heard.Length == 0) { status.Text = "Nothing was heard. Check the microphone in Advanced → Speech settings."; status.Foreground = Brushes.IndianRed; return; }
                want.Text = (want.Text.Trim() + " " + heard).Trim();
                send.IsEnabled = true; asked = true;
                send.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }
            catch (Exception ex) { status.Text = ex.Message; status.Foreground = Brushes.IndianRed; }
            finally { listening = null; mic.Content = "🎤 Speak"; level.Visibility = Visibility.Collapsed; if (!asked) send.IsEnabled = true; asked = false; }
        };
        window.Closed += (_, _) => listening?.Stop();

        AgentRequest? sent = null;
        void Show()
        {
            if (sent == null) return;
            if (sent.Status == "waiting")
            {
                status.Text = "Queued: ask your assistant in chat to check Arcadia Studio and the reply appears here.";
                status.Foreground = Brushes.Goldenrod;
            }
            else if (sent.Problem.Length > 0 && sent.Reply.Length == 0)
            {
                status.Text = sent.Problem + "  The request is still queued, so asking in chat still works.";
                status.Foreground = Brushes.IndianRed;
                send.IsEnabled = true;
            }
            else if (sent.Reply.Length > 0 || sent.Notes.Length > 0)
            {
                reply.Text = sent.Reply.Length > 0 ? sent.Reply : sent.Notes;
                reply.Visibility = Visibility.Visible;
                status.Text = "Done. Each ask is its own run: the assistant starts, does the work, answers and exits.";
                status.Foreground = Brushes.LightGreen;
                send.Content = art ? "Ask for another" : "Ask again";
                send.IsEnabled = true;
            }
        }
        Action watch = () => Dispatcher.Invoke(() => { Show(); RefreshAll(); });
        agentRequestWatchers.Add(watch);
        window.Closed += (_, _) => agentRequestWatchers.Remove(watch);

        send.Click += (_, _) => Guard(async () =>
        {
            if (want.Text.Trim().Length == 0) throw new InvalidOperationException(art ? "Describe the picture first." : "Type your question first.");
            sent = new AgentRequest { Kind = kind, Element = element?.Id ?? "", Want = want.Text.Trim(), Status = "waiting" };
            agentRequests.Add(sent);
            send.IsEnabled = false;
            Show();
            Log($"Ask Agent: {about} — {sent.Want}");
            // Two ways down: starting the assistant that's set up (its CLI, which can use the editor's tools), or, with
            // none set up, leaving the ask on the queue for a connected assistant to pick up on its next turn.
            if (sent.Status == "waiting" && CanRunAgent)
            {
                reply.Visibility = Visibility.Collapsed;
                status.Text = "Starting the assistant…";
                status.Foreground = Brushes.Goldenrod;
                var run = await RunAgentAsync(AgentPrompt(sent), line => Dispatcher.Invoke(() => status.Text = line));
                if (run.Ok) { sent.Reply = run.Text.Length > 0 ? run.Text : "Done."; sent.Status = "done"; RefreshAll(); }
                else { sent.Problem = run.Trouble; Log("Ask Agent: " + run.Trouble); }
            }
            Show();
        });
        close.Click += (_, _) => window.Close();
        window.Show();
    }
}
