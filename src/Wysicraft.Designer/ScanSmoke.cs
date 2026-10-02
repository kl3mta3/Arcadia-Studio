using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Wysicraft.Models;
using Wysicraft.Packaging;
namespace Wysicraft.Designer;

// --smoke-scan (run on samples/Blockfall): Scan in Publish to Arcadia. The scan of the game as it would be uploaded, the
// scan window with Arcadia's choices picked, Use these filling the leaderboard fields, Cancel changing nothing, extra
// columns added once, the settings saved when the window closes, and a game with nothing to find. It also writes the
// scan's input and answer beside the result, so the same input can be run through the arcade's file in Node and compared.
public partial class MainWindow
{
    internal async Task VerifyScanAsync(string output)
    {
        // What the scan was given and what it answered, for comparing with the arcade's own run of the same file.
        var files = ArcadiaPackage.Files(Json.CloneProject(project), project.Publishing, WysicraftVersion);
        var texts = files.Where(f => (f.Key.EndsWith(".html") || f.Key.EndsWith(".js")) && !f.Key.EndsWith("wysicraft-web.js")).ToDictionary(f => f.Key, f => Encoding.UTF8.GetString(f.Value));
        File.WriteAllText(output + ".files.json", JsonSerializer.Serialize(texts));
        File.WriteAllText(output + ".scan.json", ScoringScan.RunJson(files));
        File.WriteAllText(output + ".scoringscan.js", ScoringScan.Script);

        var dialog = new ArcadiaDialog(this, new ArcadiaClient("http://127.0.0.1:9", WysicraftVersion));
        try
        {
            dialog.Show();
            File.WriteAllText(output, await dialog.ScanSelfTest(output));
        }
        finally { dialog.CloseForTest(); dirty = false; }
    }

    sealed partial class ArcadiaDialog
    {
        internal async Task<string> ScanSelfTest(string output)
        {
            void Expect(bool ok, string what) { if (!ok) throw new Exception("Scan: " + what + "\nStatus: " + status.Text); }
            async Task Press() { scan.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); await Settle(); }
            List<PublishTrigger> Conditions() => triggers.Children.OfType<DockPanel>().Select(r => ((Func<PublishTrigger>)r.Tag)()).ToList();
            List<PublishStat> Columns() => stats.Children.OfType<StackPanel>().Select(r => ((Func<PublishStat>)r.Tag)()).ToList();
            await Settle();
            Expect(leaderboard.IsChecked == false && scan.IsEnabled && scan.IsVisible && ReferenceEquals(VisualTreeHelperParent(scan), VisualTreeHelperParent(leaderboard)), "the Scan button sits beside Keep a leaderboard, usable before it's ticked");
            scan.BringIntoView(); await Task.Delay(100); MainWindow.SaveWindowPicture(this, output + ".section.png");

            // Cancel: the scan runs, and nothing in the form changes.
            ScoringScanResult? seen = null; string before = Json.Write(Collect());
            ChooseScan = r => { seen = r; return null; };
            await Press();
            Expect(seen != null && seen.Score.Count > 0 && seen.Triggers.Count > 0, "the scan finds the game's score and the end of a run");
            Expect(seen!.Score[0] is { Variable: "score", Path: "", Recommended: true } && seen.Triggers[0] is { Variable: "mode", Value: "over", Recommended: true } && seen.Score[0].Evidence.Count > 0 && seen.Score[0].Evidence[0].Code.Length > 0,
                "Blockfall: the score is score, a run ends when mode equals over, each with the line it was found on: " + seen.Score[0].Source + " / " + Condition(seen.Triggers[0]));
            Expect(seen.Stats.Any(c => c.Variable == "lines") && seen.Stats.Any(c => c.Variable == "level") && seen.Stats.All(c => c.Key.Length > 0 && c.Label.Length > 0), "with lines and level as possible columns: " + string.Join(", ", seen.Stats.Select(c => c.Source)));
            Expect(Json.Write(Collect()) == before && status.Text == "Nothing was changed." && leaderboard.IsChecked == false, "Cancel changes nothing");

            // The window itself: Arcadia's choices are already picked, and Use these fills the fields.
            ChooseScan = null; string shownText = ""; int windows = 0;
            static IEnumerable<T> All<T>(DependencyObject node) where T : DependencyObject
            {
                for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
                {
                    var child = System.Windows.Media.VisualTreeHelper.GetChild(node, i);
                    if (child is T hit) yield return hit;
                    foreach (var deeper in All<T>(child)) yield return deeper;
                }
            }
            string press = "Use these";
            ScanWindowForTest = w =>
            {
                windows++; w.UpdateLayout();
                shownText = string.Join("\n", All<TextBlock>(w).Select(t => t.Text));
                var picked = All<RadioButton>(w).Where(r => r.IsChecked == true).ToList(); var ticked = All<CheckBox>(w).Where(c => c.IsChecked == true).ToList();
                shownText += "\nPICKED " + picked.Count + " TICKED " + ticked.Count;
                MainWindow.SaveWindowPicture(w, output + (press == "Close" ? ".nothing.png" : ".window.png"));
                All<Button>(w).First(b => b.Content as string == press).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            };
            await Press();
            Expect(windows == 1 && shownText.Contains("SCORE COMES FROM") && shownText.Contains("RUN ENDS WHEN") && shownText.Contains("EXTRA COLUMNS") && shownText.Contains("mode equals over") && shownText.Contains("Leave it as it is") && shownText.Contains(" line "), "the scan window lists the score, the end of a run and extra columns, with where each was found:\n" + shownText);
            Expect(shownText.Contains("PICKED 1 TICKED " + seen.Triggers.Count(t => t.Recommended)), "with Arcadia's own choices picked, and no extra columns ticked: " + shownText[shownText.LastIndexOf('\n')..]);
            Expect(shownText.Contains(seen.Confident ? "Arcadia is sure" : "Arcadia wouldn't be sure"), "and it says whether Arcadia would be sure by itself");
            var conditions = Conditions();
            Expect(leaderboard.IsChecked == true && scoresPanel.Visibility == Visibility.Visible && scoreFrom.Text == "score" && scorePath.Text == "", "Use these ticks Keep a leaderboard and fills Score comes from: " + scoreFrom.Text);
            Expect(conditions.Count == seen.Triggers.Count(t => t.Recommended) && conditions[0] is { Variable: "mode", Path: "", EqualsValue: "over" }, "and Run ends when: " + string.Join(" | ", conditions.Select(c => c.Variable + "=" + c.EqualsValue)));
            Expect(Columns().Count == 0 && status.Text.StartsWith("From the scan: the score comes from score; a run ends when mode equals over"), "columns are left alone unless ticked, and the status says what was filled in: " + status.Text);

            // Extra columns: ticked ones are added, once.
            ChooseScan = r => new ScanChoice(null, [], r.Stats);
            await Press();
            var columns = Columns();
            Expect(columns.Count == seen.Stats.Count && columns.Any(c => c is { Key: "lines", Variable: "lines", Label: "Lines", Aggregate: "max", Check: true }) && scoreFrom.Text == "score" && Conditions().Count == conditions.Count, "ticked columns are added with their key and name; the score and conditions chosen before stay");
            await Press();
            Expect(Columns().Count == columns.Count && status.Text == "Nothing was changed.", "a column already there isn't added again");

            // A dotted source: the field goes in its own box.
            ChooseScan = _ => new ScanChoice(new ScanCandidate("g", "score", null, "", "Points", 5, true, []), [new ScanCandidate("g", "dead", null, "", "", 4, true, [])], []);
            label.Text = "Score"; await Press();
            Expect(scoreFrom.Text == "g" && scorePath.Text == "score" && label.Text == "Points" && Conditions() is [{ Variable: "g", Path: "dead", EqualsValue: null }], "a field inside a JSON variable fills the variable and field boxes, \"is true\" for a flag, and names a column still called Score");
            ChooseScan = r => new ScanChoice(r.Score[0], r.Triggers.Where(t => t.Recommended).ToList(), []);
            label.Text = "My points"; await Press();
            Expect(label.Text == "My points" && scoreFrom.Text == "score", "a column name of your own is kept");

            // Saved with the form, and what the game then sends.
            Apply();
            var saved = editor.project.Publishing;
            Expect(saved.Leaderboard && saved.Scores.Score is { Variable: "score", Path: "" } && saved.Scores.Triggers[0] is { Variable: "mode", EqualsValue: "over" } && saved.Scores.Stats.Count == columns.Count, "the choices are saved in the project with the rest of the form");
            var sent = System.Text.Json.Nodes.JsonNode.Parse(Encoding.UTF8.GetString(ArcadiaPackage.Files(editor.project, saved, WysicraftVersion)["game.json"]))!["scores"]!["watch"]!;
            Expect((string?)sent["score"]!["variable"] == "score" && (string?)sent["trigger"]![0]!["variable"] == "mode" && (string?)sent["trigger"]![0]!["equals"] == "over" && sent["stats"]!.AsArray().Count == columns.Count, "and the upload's scores.watch carries them: " + sent.ToJsonString());
            var warnings = ArcadiaPackage.Check(editor.project, saved, ArcadiaPackage.Files(editor.project, saved, WysicraftVersion), ArcadiaPackage.DefaultMaxUploadMb).Where(f => f.Message.Contains("isn't a screen variable")).Select(f => f.Message).ToList();
            Expect(warnings.Count == 0, "Check doesn't then say a scanned variable isn't in the project: " + string.Join(" | ", warnings));

            // A game with nothing to find: the window says so, and nothing changes.
            foreach (var key in editor.project.Scripts.Keys.ToList()) editor.project.Scripts[key] = "// nothing here";
            editor.ScriptEditor.Text = "// nothing here"; // the open script is saved into the project before a scan
            foreach (var screen in editor.project.Screens) screen.Variables.Clear();
            shown = Fingerprint(editor.project.Publishing);
            ChooseScan = null; press = "Close"; windows = 0; shownText = ""; before = Json.Write(Collect());
            await Press();
            Expect(windows == 1 && LastScan is { Empty: true } && shownText.Contains("didn't find anything that looks like a score") && Json.Write(Collect()) == before && status.Text.StartsWith("The scan found nothing"), "a game with nothing to find says so and changes nothing: " + shownText);
            ScanWindowForTest = null;
            return "PASS: Scan in Publish to Arcadia (Arcadia's own scan file run on the upload): finds Blockfall's score, end of run and columns with the lines they're on; the window with Arcadia's choices picked; Use these fills Score comes from and Run ends when and ticks Keep a leaderboard; Cancel changes nothing; extra columns added once; a field inside a JSON variable; a column name kept; saved with the form and sent as scores.watch; no \"not in this project\" warning for a scanned name; a game with nothing to find. confident=" + seen.Confident;
        }
        static DependencyObject? VisualTreeHelperParent(DependencyObject child) => System.Windows.Media.VisualTreeHelper.GetParent(child);
    }
}
