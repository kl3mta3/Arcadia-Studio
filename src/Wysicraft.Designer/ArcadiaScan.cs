using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wysicraft.Models;
using Wysicraft.Packaging;
namespace Wysicraft.Designer;

// Scan, in Publish to Arcadia's leaderboard section: runs Arcadia's own scan (ScoringScan.cs) on the game as it would be
// uploaded, shows what it found (where the score comes from, when a run ends, extra columns) with the lines it found
// them on, and fills the leaderboard fields with what the person chooses. Nothing changes until they press Use these.
public partial class MainWindow
{
    /// <summary>What was chosen in the scan window. Score null leaves "Score comes from" alone; no triggers leaves the
    /// conditions alone; stats are added to the columns already there.</summary>
    sealed record ScanChoice(ScanCandidate? Score, IReadOnlyList<ScanCandidate> Triggers, IReadOnlyList<ScanCandidate> Stats);

    sealed partial class ArcadiaDialog
    {
        readonly Button scan = new() { Content = "Scan…", Padding = new Thickness(10, 1, 10, 1), Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Reads your game the way Arcadia does when it's uploaded, and suggests where the score comes from, when a run ends and extra columns. You choose which to use." };
        /// <summary>Self-checks choose from the scan through this instead of the window (null cancels).</summary>
        internal Func<ScoringScanResult, ScanChoice?>? ChooseScan;
        /// <summary>Self-checks look at the scan window as it's shown, and press its buttons.</summary>
        internal Action<Window>? ScanWindowForTest;
        internal ScoringScanResult? LastScan;

        async Task Scan()
        {
            editor.SaveScriptText(); Apply();
            var s = Collect(); var project = Json.CloneProject(editor.project); string version = WysicraftVersion;
            status.Text = "Scanning the game the way Arcadia does…";
            var found = await Task.Run(() => ScoringScan.Run(ArcadiaPackage.Files(project, s, version)));
            LastScan = found;
            editor.Log($"Leaderboard scan: {found.Score.Count} possible score{(found.Score.Count == 1 ? "" : "s")}, {found.Triggers.Count} end-of-run signal{(found.Triggers.Count == 1 ? "" : "s")}, {found.Stats.Count} possible column{(found.Stats.Count == 1 ? "" : "s")}.");
            var choice = ChooseScan != null ? ChooseScan(found) : AskScan(found);
            if (choice == null) { status.Text = found.Empty ? "The scan found nothing that looks like a score. Fill the leaderboard fields in yourself." : "Nothing was changed."; return; }
            UseScan(choice);
        }

        static string Condition(ScanCandidate c) => c.Value == null ? c.Source + " is true" : c.Source + " equals " + c.Value;

        /// <summary>The scan window: what was found, best first, with Arcadia's own choices already picked.</summary>
        ScanChoice? AskScan(ScoringScanResult found)
        {
            var window = new Window { Owner = this, Title = "Scan for the score", Width = 580, SizeToContent = SizeToContent.Height, MaxHeight = 660, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false };
            window.SetResourceReference(StyleProperty, typeof(Window));
            var outer = new DockPanel { Margin = new Thickness(16) }; window.Content = outer;
            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            DockPanel.SetDock(bar, Dock.Bottom); outer.Children.Add(bar);
            var panel = new StackPanel();
            outer.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel });
            ScanChoice? answer = null;
            void Close(string text, Func<ScanChoice?> value, bool isDefault = false, bool isCancel = false)
            {
                var b = new Button { Content = text, IsDefault = isDefault, IsCancel = isCancel, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(8, 0, 0, 0) };
                b.Click += (_, _) => { answer = value(); window.Close(); }; bar.Children.Add(b);
            }
            if (ScanWindowForTest != null) window.ContentRendered += (_, _) => ScanWindowForTest(window);

            if (found.Empty)
            {
                panel.Children.Add(Wrap(Brushes.White, "The scan didn't find anything that looks like a score or the end of a run."));
                panel.Children.Add(Wrap(Brushes.LightGray, "It looks for a variable named like score, points, coins or distance that goes up, and for a run ending, such as a variable set to 'over' or a flag like dead or gameOver set to true. Screen variables and ctx.state names are read; a variable inside a script alone can't be seen by Arcadia.\n\nYou can still fill the leaderboard fields in yourself."));
                Close("Close", () => null, isDefault: true, isCancel: true);
                window.ShowDialog();
                return null;
            }

            panel.Children.Add(Wrap(Brushes.White, "This is the scan Arcadia runs on a game when it's uploaded. Choose what to use: it goes into the leaderboard fields, where you can still change it."));
            if (found.UsesSdk) panel.Children.Add(Wrap(Brushes.Khaki, "This game sends its own scores with Arcadia.submitScore, so Arcadia doesn't need to watch its variables."));
            else if (found.Confident) panel.Children.Add(Wrap(Brushes.LightGreen, "Arcadia is sure about the choices already picked: it would set these up by itself."));
            else panel.Children.Add(Wrap(Brushes.Khaki, "Arcadia wouldn't be sure by itself, so look the choices over before using them."));

            // One found thing: what it is, and the line (or two) it was found on.
            static StackPanel Found(string text, ScanCandidate c)
            {
                var item = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };
                item.Children.Add(new TextBlock { Text = text, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
                foreach (var e in c.Evidence.Take(2)) item.Children.Add(new TextBlock { Text = e.Text, Foreground = Brushes.Gray, FontFamily = new FontFamily("Consolas"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = e.Text });
                return item;
            }

            panel.Children.Add(Heading("Score comes from"));
            var scores = new List<(RadioButton Button, ScanCandidate? Candidate)>();
            foreach (var c in found.Score)
            {
                var radio = new RadioButton { GroupName = "scanScore", Content = Found(c.Source + (c.Label.Length > 0 ? "   (" + c.Label + ")" : ""), c), IsChecked = scores.Count == 0, Margin = new Thickness(0, 3, 0, 3) };
                scores.Add((radio, c)); panel.Children.Add(radio);
            }
            if (found.Score.Count == 0) panel.Children.Add(Wrap(Brushes.LightGray, "Nothing that looks like a score was found."));
            else
            {
                string now = scoreFrom.Text.Trim().Length > 0 ? " (" + scoreFrom.Text.Trim() + (scorePath.Text.Trim().Length > 0 ? "." + scorePath.Text.Trim() : "") + ")" : "";
                var keep = new RadioButton { GroupName = "scanScore", Content = new TextBlock { Text = "Leave it as it is" + now, Foreground = Brushes.LightGray }, Margin = new Thickness(0, 3, 0, 3) };
                scores.Add((keep, null)); panel.Children.Add(keep);
            }

            panel.Children.Add(Heading("Run ends when"));
            var ends = new List<(CheckBox Box, ScanCandidate Candidate)>();
            foreach (var c in found.Triggers)
            {
                var box = new CheckBox { Content = Found(Condition(c), c), IsChecked = c.Recommended, Margin = new Thickness(0, 3, 0, 3) };
                ends.Add((box, c)); panel.Children.Add(box);
            }
            panel.Children.Add(Wrap(Brushes.LightGray, found.Triggers.Count == 0 ? "Nothing that looks like the end of a run was found." : "The run has ended when any ticked one holds. They replace the conditions in the form; tick none to leave those alone."));

            var columns = new List<(CheckBox Box, ScanCandidate Candidate)>();
            if (found.Stats.Count > 0)
            {
                panel.Children.Add(Heading("Extra columns"));
                foreach (var c in found.Stats)
                {
                    var box = new CheckBox { Content = Found(c.Source + "   (" + c.Label + ")", c), Margin = new Thickness(0, 3, 0, 3) };
                    columns.Add((box, c)); panel.Children.Add(box);
                }
                panel.Children.Add(Wrap(Brushes.LightGray, "Other figures worth showing beside the score. Ticked ones are added to the columns in the form."));
            }

            Close("Use these", () => new ScanChoice(scores.FirstOrDefault(x => x.Button.IsChecked == true).Candidate,
                ends.Where(x => x.Box.IsChecked == true).Select(x => x.Candidate).Take(6).ToList(),
                columns.Where(x => x.Box.IsChecked == true).Select(x => x.Candidate).ToList()), isDefault: true);
            Close("Cancel", () => null, isCancel: true);
            window.ShowDialog();
            return answer;
        }

        /// <summary>Puts what was chosen into the leaderboard fields. It's saved with the rest of the form.</summary>
        void UseScan(ScanChoice choice)
        {
            var said = new List<string>();
            leaderboard.IsChecked = true; scoresPanel.Visibility = Visibility.Visible;
            if (choice.Score is { } score)
            {
                scoreFrom.Text = score.Variable; scorePath.Text = score.Path;
                // The column takes the score's name while it still has none of its own.
                if (score.Label.Length > 0 && label.Text.Trim() is "" or "Score") label.Text = score.Label;
                said.Add("the score comes from " + score.Source);
            }
            if (choice.Triggers.Count > 0)
            {
                triggers.Children.Clear();
                foreach (var t in choice.Triggers.Take(6)) AddTrigger(new PublishTrigger { Variable = t.Variable, Path = t.Path, EqualsValue = t.Value });
                said.Add("a run ends when " + string.Join(" or ", choice.Triggers.Take(6).Select(Condition)));
            }
            int added = 0;
            var have = stats.Children.OfType<StackPanel>().Select(r => ((Func<PublishStat>)r.Tag)()).ToList();
            foreach (var c in choice.Stats)
            {
                if (stats.Children.Count >= 8 || have.Any(h => (h.Variable == c.Variable && h.Path == c.Path) || (c.Key.Length > 0 && h.Key == c.Key))) continue;
                var stat = new PublishStat { Key = c.Key, Label = c.Label, Variable = c.Variable, Path = c.Path };
                AddStat(stat); have.Add(stat); added++;
            }
            if (added > 0) said.Add(added + " extra column" + (added == 1 ? "" : "s") + " added");
            status.Text = said.Count == 0 ? "Nothing was changed." : "From the scan: " + string.Join("; ", said) + "." + (max.Text.Trim().Length == 0 ? " Set the highest score that's really possible, then Check." : " Check when you're ready.");
        }
    }
}
