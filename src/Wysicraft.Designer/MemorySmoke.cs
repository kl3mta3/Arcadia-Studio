using System.Diagnostics;
using System.IO;
using System.Text;
using Wysicraft.Models;
namespace Wysicraft.Designer;

// --smoke-memory <project>: a measurement of where the editor's memory goes with a real project open, not a pass/fail
// check. It reports the managed heap and the process's memory after loading, after drawing the canvas and the panels,
// and as Undo steps pile up (each edit keeps a way back), with what one step costs in memory and time, and what Undo
// and clearing the history give back. The project file is only read.
public partial class MainWindow
{
    internal async Task MeasureMemory(string output)
    {
        var report = new StringBuilder();
        static long Heap() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); return GC.GetTotalMemory(true); }
        static string MB(long bytes) => (bytes / 1048576.0).ToString("0.0") + " MB";
        void Save() => File.WriteAllText(output, report.ToString());
        void Line(string what, long heap) { using var me = Process.GetCurrentProcess(); report.AppendLine($"{what,-58} managed heap {MB(heap),10}   process {MB(me.PrivateMemorySize64),10}"); Save(); }

        int controls = project.Screens.Sum(s => s.Elements.Count), scriptBytes = project.Scripts.Values.Sum(s => Encoding.UTF8.GetByteCount(s));
        long assetBytes = project.Assets.Values.Sum(a => (long)a.Length);
        report.AppendLine($"{project.Manifest.Name}: {project.Screens.Count} screen(s), {controls} controls, {project.Scripts.Count} script(s) of {scriptBytes / 1024} KB, {project.Assets.Count} assets of {MB(assetBytes)}");
        long loaded = Heap(); Line("project loaded", loaded);
        RefreshAll(); Draw(); UpdateLayout(); RefreshAssetBrowser();
        long drawn = Heap(); Line("canvas, layers, properties and assets drawn", drawn);

        // Undo steps: a small real edit each time (a control moved by a pixel), as dragging or an assistant's edit does.
        history.Clear(); long before = Heap();
        var mover = ui.Elements.First(e => e.Type != "sound");
        var watch = Stopwatch.StartNew();
        const int first = 20;
        for (int i = 0; i < first; i++) { Change(); mover.Bounds.X += 1; }
        watch.Stop();
        long afterFirst = Heap(); Line($"after {first} edits", afterFirst);
        long perStep = (afterFirst - before) / first;
        report.AppendLine($"   one Undo step keeps {MB(perStep)} and takes {watch.Elapsed.TotalMilliseconds / first:0.0} ms to record"); Save();
        for (int i = first; i < 200; i++) { Change(); mover.Bounds.X += 1; }
        long full = Heap(); Line("after 200 edits (the most Undo keeps)", full);
        report.AppendLine($"   Undo history holds {MB(full - before)} for {history.UndoCount} steps");
        for (int i = 0; i < 100; i++) { Change(); mover.Bounds.X += 1; }
        long more = Heap(); Line("after 300 edits (older steps dropped)", more);

        watch.Restart(); history.Undo(); watch.Stop();
        report.AppendLine($"   one Undo takes {watch.Elapsed.TotalMilliseconds:0.0} ms" + (project.Screens.SelectMany(s => s.Elements).Any() ? "" : " (and lost the project!)")); Save();
        watch.Restart(); history.Redo(); watch.Stop();
        report.AppendLine($"   one Redo takes {watch.Elapsed.TotalMilliseconds:0.0} ms"); Save();
        // The project must come back exactly: undo everything that was done here and compare.
        string was = Json.Write(project.Screens.Select(s => s.Elements.Count).ToList());
        watch.Restart(); for (int i = 0; i < 20; i++) history.Undo(); watch.Stop();
        report.AppendLine($"   20 more Undos took {watch.Elapsed.TotalMilliseconds / 20:0} ms each; the control moved in each edit is back at x = {ui.Elements.First(e => e.Id == mover.Id).Bounds.X:0} from {mover.Bounds.X:0} (20 pixels, one per edit); screens intact: {Json.Write(project.Screens.Select(s => s.Elements.Count).ToList()) == was}"); Save();

        // The window gets to go idle (layout, drawing and anything queued finish and let go of what they held) before
        // the last readings, as it does between a person's actions.
        async Task Idle() { for (int i = 0; i < 3; i++) await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); }
        await Idle(); Line("after those Undos, once the window is idle", Heap());
        history.Clear(); await Idle(); long cleared = Heap(); Line("history cleared", cleared);
        // Where a refresh spends its time (Undo, Redo and opening a project all do one).
        void Time(string what, Action run) { var w = Stopwatch.StartNew(); run(); w.Stop(); report.AppendLine($"   {what,-34} {w.Elapsed.TotalMilliseconds,7:0} ms"); Save(); }
        report.AppendLine("one refresh, by part:");
        Time("scripts list", () => RefreshScripts());
        Time("asset browser", RefreshAssetBrowser);
        Time("components", RefreshComponents);
        Time("canvas (Draw)", Draw);
        Time("  of which Layers", RefreshLayers);
        Time("properties", RefreshInspector);
        Time("layout pass", UpdateLayout);
        Time("unpack a step", () => Json.Unpack(Json.Pack(project)));
        // Ten more refreshes, idle between them: memory should not climb.
        long refreshBefore = Heap(); for (int i = 0; i < 10; i++) { RefreshAll(); await Idle(); }
        long refreshAfter = Heap(); Line("after 10 more refreshes, idle between", refreshAfter);
        report.AppendLine($"   each refresh left {MB((refreshAfter - refreshBefore) / 10)} behind"); Save();
        dirty = false;
        File.WriteAllText(output, report.ToString());
    }
}
