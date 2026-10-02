using System.Windows;
namespace Wysicraft.Designer;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // The self-checks' stand-in for butler (itch.io's uploader): answers on the console and exits.
        if (e.Args.Length > 0 && e.Args[0] == "--fake-butler") { Shutdown(FakeButler.Run(e.Args[1..])); return; }
        if (e.Args.Length == 3 && e.Args[0] == "--apply-update") { ApplyPortableUpdate(e.Args[1], e.Args[2]); return; }
        // First launch after the rename: settings come across from the Wysicraft folder (which is left as it was).
        var carried = Wysicraft.Core.AppFolders.CarryOverSettings();
        var icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/ArcadiaStudio.ico"));
        icon.Freeze();
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => { if (sender is Window window && window.Icon == null) window.Icon = icon; }));
        var window = new MainWindow(); MainWindow = window;
        if (carried.Count > 0) window.Log("Welcome to Arcadia Studio. Your settings came across from the Wysicraft folder (" + string.Join(", ", carried) + "); that folder was left as it was.");
        DispatcherUnhandledException+=(_,args)=>{window.CaptureCrash(args.Exception);MessageBox.Show("Arcadia Studio encountered an unexpected error and will close. Any recovery draft is available from File → Recover unsaved project on the next launch.\n\n"+args.Exception.Message,"Wysicraft");args.Handled=true;Shutdown(1);};
        if (e.Args.Length == 3 && e.Args[0] is "--smoke-speech" or "--smoke-tutorial" or "--smoke-itch" or "--smoke-leaderboard" or "--smoke-sound" or "--smoke-preview" or "--smoke-events" or "--smoke-mcp" or "--smoke-connection" or "--smoke-docking" or "--smoke-layers" or "--smoke-nesting" or "--smoke-recovery" or "--smoke-isolation" or "--smoke-componentselection" or "--smoke-components" or "--smoke-assets" or "--smoke-arrange" or "--smoke-anchors" or "--smoke-attach" or "--smoke-chrome" or "--smoke-advanced" or "--smoke-pixels" or "--smoke-inputs" or "--smoke-publish" or "--smoke-update" or "--smoke-slots" or "--smoke-scan" or "--smoke-art")
        {
            window.LoadForSmoke(e.Args[1]); window.Show();
            if (e.Args[0] is "--smoke-mcp" or "--smoke-connection") window.Hide();
            window.Dispatcher.InvokeAsync(async () =>
            {
                try { if(e.Args[0]=="--smoke-speech") await window.VerifySpeechAsync(e.Args[2]); else if(e.Args[0]=="--smoke-tutorial") await window.VerifyTutorialAsync(e.Args[2]); else if(e.Args[0]=="--smoke-itch") await window.VerifyItchAsync(e.Args[2]); else if(e.Args[0]=="--smoke-scan") await window.VerifyScanAsync(e.Args[2]); else if(e.Args[0]=="--smoke-art") await window.VerifyArtAsync(e.Args[2]); else if(e.Args[0]=="--smoke-leaderboard") await window.VerifyLeaderboardEditorAsync(e.Args[2]); else if(e.Args[0]=="--smoke-slots") window.VerifyCraftingTable(e.Args[2]); else if(e.Args[0]=="--smoke-publish") await window.VerifyPublishingAsync(e.Args[2]); else if(e.Args[0]=="--smoke-update") { await window.VerifyUpdatesAsync(e.Args[2]); if (window.UpdateHandedOver) return; } else if(e.Args[0]=="--smoke-sound") window.VerifySoundMakers(e.Args[2]); else if(e.Args[0]=="--smoke-pixels") window.VerifyPixelEditing(e.Args[2]); else if(e.Args[0]=="--smoke-advanced") window.VerifyAdvancedEditor(e.Args[2]); else if(e.Args[0]=="--smoke-inputs") await window.VerifyInputCreatorAsync(e.Args[2]); else if(e.Args[0]=="--smoke-chrome") window.VerifyChrome(e.Args[2]); else if(e.Args[0]=="--smoke-attach") window.VerifyPanelParenting(e.Args[2]); else if(e.Args[0]=="--smoke-anchors") window.VerifyLiveAnchors(e.Args[2]); else if(e.Args[0]=="--smoke-isolation") window.VerifyIsolation(e.Args[2]); else if(e.Args[0]=="--smoke-componentselection") window.VerifyComponentSelection(e.Args[2]); else if(e.Args[0]=="--smoke-components") window.VerifyComponents(e.Args[2]); else if(e.Args[0]=="--smoke-assets") window.VerifyAssetBrowser(e.Args[2]); else if(e.Args[0]=="--smoke-arrange") window.VerifyArrangement(e.Args[2]); else if(e.Args[0]=="--smoke-recovery") window.VerifyRecovery(e.Args[2]); else if(e.Args[0]=="--smoke-nesting") await window.VerifyNesting(e.Args[2]); else if(e.Args[0]=="--smoke-layers") window.VerifyLayerEditing(e.Args[2]); else if(e.Args[0]=="--smoke-docking") window.VerifyDocking(e.Args[2]); else if (e.Args[0] is "--smoke-mcp" or "--smoke-connection") await window.VerifyMcpAsync(e.Args[2],e.Args[0]=="--smoke-connection"); else if (e.Args[0] == "--smoke-events") await window.VerifyEventScriptsAsync(e.Args[2]); else await window.VerifyPreviewAsync(e.Args[2]); window.Close(); }
                catch (Exception ex) { System.IO.File.WriteAllText(e.Args[2] + ".error.txt", ex.ToString()); Shutdown(1); }
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        else if (e.Args.Length == 3 && e.Args[0] == "--smoke")
        {
            window.LoadForSmoke(e.Args[1]);
            window.Show();
            window.Dispatcher.InvokeAsync(() => { window.Capture(e.Args[2]); window.Close(); }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        else { window.Show(); window.Dispatcher.InvokeAsync(window.CheckForUpdatesOnStartup, System.Windows.Threading.DispatcherPriority.ApplicationIdle); if(e.Args.Length==1 && System.IO.File.Exists(e.Args[0])) {try {window.OpenProjectPath(e.Args[0]);}catch(Exception ex){MessageBox.Show(window,ex.Message,"Could not open project");}} }
    }

    /// <summary>A portable update: this is the new version, unzipped elsewhere. Once the old copy (pid) has closed, it
    /// copies itself over the old folder and opens it there. It refuses anything that isn't an Arcadia Studio folder.</summary>
    void ApplyPortableUpdate(string target, string pidText)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        string source = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, ".."));
        target = System.IO.Path.GetFullPath(target);
        Task.Run(() =>
        {
            string? problem = null;
            try
            {
                if (!System.IO.File.Exists(System.IO.Path.Combine(target, "Designer", "ArcadiaStudio.exe")) && !System.IO.File.Exists(System.IO.Path.Combine(target, "Designer", "Wysicraft.Designer.exe")))
                    throw new System.IO.IOException(target + " isn't an Arcadia Studio folder.");
                if (string.Equals(source.TrimEnd('\\'), target.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new System.IO.IOException("The update is already in place.");
                if (int.TryParse(pidText, out int pid) && pid > 0)
                    try { using var old = System.Diagnostics.Process.GetProcessById(pid); old.WaitForExit(60000); } catch (ArgumentException) { }
                // Files can stay locked for a moment after the old copy closes.
                for (int attempt = 0; ; attempt++)
                {
                    try { Wysicraft.Packaging.UpdateInstall.CopyRelease(source, target); break; }
                    catch (System.IO.IOException) when (attempt < 15) { Thread.Sleep(1000); }
                }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.IO.Path.Combine(target, "Designer", "ArcadiaStudio.exe")) { UseShellExecute = true, WorkingDirectory = System.IO.Path.Combine(target, "Designer") });
            }
            catch (Exception ex) { problem = ex.Message; }
            Dispatcher.Invoke(() =>
            {
                if (problem != null) MessageBox.Show("Arcadia Studio couldn't finish updating: " + problem + "\n\nThe new version is in " + source + ". You can copy its folders over " + target + " yourself.", "Arcadia Studio");
                Shutdown();
            });
        });
    }
}
