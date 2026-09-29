using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Wysicraft.Core;
using Wysicraft.Models;
using Wysicraft.Packaging;
using Validation = Wysicraft.Core.Validation;

namespace Wysicraft.Designer;

public partial class MainWindow
{
    MinecraftTestWindow? minecraftTest;
    void TestMinecraft()
    {
        if (minecraftTest != null) { minecraftTest.Activate(); return; }
        SaveScriptText();
        if (!MinecraftExportAllowed("Testing in Minecraft")) return;
        minecraftTest = new MinecraftTestWindow(this, () => { SaveScriptText(); return Json.CloneProject(project); }, RuntimeJar);
        minecraftTest.Closed += (_, _) => minecraftTest = null;
        minecraftTest.Show();
    }
}

internal sealed class MinecraftTestWindow : Window
{
    readonly Func<Project> capture;
    readonly Func<string> runtimeJar;
    readonly TextBox diagnostics = new() { IsReadOnly=true,AcceptsReturn=true,Height=90,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Foreground=Brushes.Salmon };
    readonly TextBox instance = new(), javaHome = new(), arguments = new() { MinWidth = 170, ToolTip = "Arguments for the selected command, without the command name" };
    readonly TextBox log = new() { IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.Wrap };
    readonly ComboBox globals = new() { MinWidth = 190 };
    readonly CheckBox skipScripts = new() { Content = "Test layout without unsupported scripts (test copy only)", Foreground = Brushes.White, Margin = new Thickness(5), ToolTip = "Skip unsupported script engines. Standard Client and Server JS, built-in actions and KubeJS Server scripts still run. Your project is unchanged." };
    readonly TextBlock usage = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(5) };
    readonly Button start, testJar, apply, open, close, run, stop, reset;
    readonly TextBlock sourceLabel = new() { Text = "Source: editor project", Margin = new Thickness(5), TextWrapping = TextWrapping.Wrap };
    string? activeJar;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    readonly string template;
    Process? process;
    string activeRoot = "", projectId = "", token = "", previousResult = "";
    bool busy, stopping, forcedStop;
    static string SettingsFile => Wysicraft.Core.AppFolders.Path("minecraft-test.json");
    sealed class Settings { public string Instance { get; set; } = ""; public string JavaHome { get; set; } = ""; }
    Dictionary<string,string> usages = [];
    long logPosition;
    sealed class TestState {
        public string Token { get; set; } = "";
        public bool Ready { get; set; }
        public List<string> Commands { get; set; } = [];
        public Dictionary<string,string> Usage { get; set; } = [];
        public string Result { get; set; } = "";
        public string Error { get; set; } = "";
    }
    public MinecraftTestWindow(Window owner, Func<Project> capture, Func<string> runtimeJar)
    {
        Owner = owner; this.capture = capture; this.runtimeJar=runtimeJar; template = FindTemplate();
        Title = "Arcadia Studio • Minecraft 1.21.1 / NeoForge test"; Width = 1050; Height = 650;
        Background = new SolidColorBrush(Color.FromRgb(29,32,37)); Foreground = Brushes.White;
        var layout = new DockPanel { Margin = new Thickness(10) }; Content = layout;
        var top = new StackPanel(); DockPanel.SetDock(top,Dock.Top); layout.Children.Add(top);
        var errors=new StackPanel(); DockPanel.SetDock(errors,Dock.Bottom); errors.Children.Add(new TextBlock {Text="SCRIPT / RUNTIME ERRORS",Foreground=Brushes.Salmon}); errors.Children.Add(diagnostics); layout.Children.Add(errors);
        top.Children.Add(new TextBlock { Text = "In Minecraft: F6 opens, F7 closes, F8 shows test controls / toggles the toolbar. Keys can be changed in Minecraft Controls.", Margin = new Thickness(4) });
        instance.Text = Wysicraft.Core.AppFolders.Existing("MinecraftTest","1.21.1");
        javaHome.Text = FindJava();
        try { if (File.Exists(SettingsFile)) { var settings = Json.Read<Settings>(File.ReadAllText(SettingsFile)); instance.Text = settings.Instance; if (IsJava21(settings.JavaHome)) javaHome.Text = settings.JavaHome; } } catch (Exception) { }
        AddFolder(top,"Test instance",instance); AddFolder(top,"Java 21 home",javaHome);
        var controls = new WrapPanel(); top.Children.Add(controls);
        Button Add(string title, System.Action action) { var button = new Button { Content = title }; button.Click += async (_, _) => { try { action(); } catch (Exception ex) { Print(ex.Message); await Task.CompletedTask; } }; controls.Children.Add(button); return button; }
        start = Add("Start editor test", async () => await Launch());
        testJar = Add("Test export…", async () => {
            var dialog = new OpenFileDialog { Filter = "Arcadia Studio exports (*.zip;*.jar)|*.zip;*.jar|Installation ZIP (*.zip)|*.zip|Project JAR (*.jar)|*.jar", Title = "Select Installation ZIP or exported project JAR" };
            if (dialog.ShowDialog(this) == true) await Launch(dialog.FileName);
        });
        apply = Add("Apply changes", () => { if (busy) return; ApplyEditor(); });
        open = Add(".open", () => Send("command",projectId+".open"));
        close = Add(".close", () => Send("command",projectId+".close"));
        controls.Children.Add(new TextBlock { Text = "Globals", Margin = new Thickness(10,6,4,4) }); controls.Children.Add(globals); controls.Children.Add(arguments);
        run = Add("Run", () => { if (globals.SelectedItem is string name) Send("command",name + (arguments.Text.Length == 0 ? "" : " " + arguments.Text)); });
        stop = Add("Stop", async () => await Stop()); reset = Add("Reset world", ResetWorld);
        globals.SelectionChanged += (_, _) => usage.Text = globals.SelectedItem is string name ? name + " " + usages.GetValueOrDefault(name) : "";
        top.Children.Add(sourceLabel); top.Children.Add(skipScripts); top.Children.Add(usage); layout.Children.Add(log);
        timer.Tick += (_, _) => Poll(); timer.Start(); SetReady(false);
        Closing += async (_, e) => { if (Running) { e.Cancel = true; await Stop(); if (!Running) Close(); } else timer.Stop(); };
        Print("One reusable test instance; .open is never run automatically. First launch downloads Minecraft, NeoForge and KubeJS. Game files remain outside Git.");
        Print("Additional globals appear when their command starts with project_id. or is associated using WysicraftApi.registerProjectCommand(projectId, command).");
    }
    bool Running { get { try { return process != null && !process.HasExited; } catch (InvalidOperationException) { return false; } } }
    internal async Task McpControl(string operation, string path = "", string command = "") {
        switch(operation) {
            case "test_start": case "test_export":
                if(Running || busy) throw new InvalidOperationException("Stop the running test before starting another.");
                if(operation=="test_export" && !Path.IsPathFullyQualified(path)) throw new InvalidDataException("Provide an absolute exported JAR or installation ZIP path.");
                await Launch(operation=="test_export"?path:null,true); break;
            case "test_stop": await Stop(); break;
            case "test_apply": if(busy || !Running || !open.IsEnabled) throw new InvalidOperationException("Start the test and wait until ready first."); ApplyEditor();break;
            case "test_open": case "test_close": case "test_command":
                if(!Running || !open.IsEnabled) throw new InvalidOperationException("Wait for the test to become ready.");
                string text=operation=="test_open"?projectId+".open":operation=="test_close"?projectId+".close":command.Trim().TrimStart('/');
                if(string.IsNullOrWhiteSpace(text) || text.Length>2048 || text.Any(char.IsControl)) throw new InvalidDataException("Provide one command without newlines, at most 2048 characters.");
                Send("command",text); break;
            default:throw new InvalidOperationException("Unknown test operation");
        }
    }
    internal string McpStatus() {
        object? state = null;
        if (activeRoot.Length > 0) try {
            string path=Inside(activeRoot,"run/.wysicraft-test/state.json");
            if(File.Exists(path)) { var data=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject(); if(data["token"]?.GetValue<string>()==token) { data.Remove("token"); state=data; } }
        } catch(Exception ex) when(ex is IOException or System.Text.Json.JsonException) { }
        return Json.Write(new { running=Running,projectId,source=activeJar ?? "editor",state,logs=log.Text.Length>12000?log.Text[^12000..]:log.Text });
    }
    static string FindTemplate() {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            foreach (string child in new[] { "TestEnvironment", "wysicraft-runtime" }) {
                string path = Path.Combine(dir.FullName,child);
                if (File.Exists(Path.Combine(path,"gradlew.bat")) && Directory.Exists(Path.Combine(path,"src","main"))) return path;
            }
        return "";
    }
    static string FindJava() {
        string? home = Environment.GetEnvironmentVariable("JAVA_HOME"); if (IsJava21(home)) return home!;
        string jdks = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".jdks");
        return Directory.Exists(jdks) ? Directory.GetDirectories(jdks).FirstOrDefault(IsJava21) ?? "" : "";
    }
    static bool IsJava21(string? home) {
        try {
            if (string.IsNullOrWhiteSpace(home) || !File.Exists(Path.Combine(home,"bin","java.exe"))) return false;
            string release = Path.Combine(home,"release");
            if (!File.Exists(release)) return false;
            string version = File.ReadLines(release).FirstOrDefault(line => line.StartsWith("JAVA_VERSION="))?.Split('=',2)[1].Trim().Trim('"') ?? "";
            return version == "21" || version.StartsWith("21.") || version.StartsWith("21+") || version.StartsWith("21-");
        } catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; }
    }
    void AddFolder(Panel panel,string label,TextBox box) {
        var row = new DockPanel(); row.Children.Add(new TextBlock { Text = label, Width = 110, Margin = new Thickness(4) });
        var browse = new Button { Content = "Browse" }; DockPanel.SetDock(browse,Dock.Right); row.Children.Add(browse); row.Children.Add(box); panel.Children.Add(row);
        browse.Click += (_, _) => { if (Running) return; var dialog = new OpenFolderDialog(); if (dialog.ShowDialog(this) == true) box.Text = dialog.FolderName; };
    }
    void Print(string message) { if (log.Text.Length > 150000) log.Text = log.Text[^75000..]; log.AppendText(message+"\n"); log.ScrollToEnd(); foreach(string line in message.Split('\n')) if(line.Contains("ERROR",StringComparison.OrdinalIgnoreCase) || line.Contains("failed:") || line.Contains("Script:") || line.Contains("Exception:")) { if(diagnostics.Text.Length>12000) diagnostics.Text=diagnostics.Text[^6000..]; diagnostics.AppendText(line+"\n"); diagnostics.ScrollToEnd(); } }
    void SetReady(bool ready) { open.IsEnabled = close.IsEnabled = run.IsEnabled = ready && !busy; apply.IsEnabled = ready && !busy && activeJar == null; start.IsEnabled = testJar.IsEnabled = !Running && !busy; skipScripts.IsEnabled = !Running && !busy; stop.IsEnabled = Running; reset.IsEnabled = !Running && !busy; instance.IsEnabled = javaHome.IsEnabled = !Running && !busy; }
    void ApplyEditor() {
        if (activeJar != null) throw new InvalidOperationException("Exported JAR tests cannot apply editor changes. Stop, export again, and select the new JAR.");
        Deploy(capture()); Send("reload");
    }
    static string Inside(string root,string relative) {
        Validation.SafePath(relative.Replace('\\','/'));
        string full = Path.GetFullPath(Path.Combine(root,relative));
        if (!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("Path escapes test instance");
        for (string? p = full; p != null && p.Length >= root.Length; p = Path.GetDirectoryName(p))
            if ((Directory.Exists(p) || File.Exists(p)) && File.GetAttributes(p).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Test instance cannot contain symlinks/junctions");
        return full;
    }
    static void CopyTree(string source,string destination) {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source,"*",new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })) {
            string to = Inside(destination,Path.GetRelativePath(source,file)); Directory.CreateDirectory(Path.GetDirectoryName(to)!); File.Copy(file,to,true);
        }
    }
    void Setup() {
        if (template.Length == 0) throw new IOException("TestEnvironment is missing. Keep it beside Designer in the complete download.");
        activeRoot = Path.GetFullPath(instance.Text);
        if (activeRoot.StartsWith(Path.GetFullPath(template),StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose a separate test instance folder.");
        string marker = Inside(activeRoot,"wysicraft-test-instance.txt");
        if (Directory.Exists(activeRoot) && Directory.EnumerateFileSystemEntries(activeRoot).Any() && !File.Exists(marker)) throw new IOException("Choose an empty folder; this folder is not an Arcadia Studio-owned test instance.");
        Directory.CreateDirectory(activeRoot); File.WriteAllText(marker,"Arcadia Studio Minecraft 1.21.1 test instance\n");
        foreach (string file in new[] { "gradlew", "gradlew.bat", "build.gradle", "settings.gradle", "gradle.properties" })
            if (File.Exists(Path.Combine(template,file))) File.Copy(Path.Combine(template,file),Inside(activeRoot,file),true);
        CopyTree(Path.Combine(template,"gradle"),Inside(activeRoot,"gradle")); CopyTree(Path.Combine(template,"src","main"),Inside(activeRoot,"src/main"));
        Directory.CreateDirectory(Inside(activeRoot,"run/.wysicraft-test"));
        string options = Inside(activeRoot,"run/options.txt");
        if (!File.Exists(options)) File.WriteAllText(options,"renderDistance:2\nsimulationDistance:5\npauseOnLostFocus:false\nonboardAccessibility:false\n");
    }
    void Deploy(Project project) {
        if (Running && projectId != project.Manifest.Id) throw new InvalidOperationException("Stop before switching projects. This avoids retaining another project's scripts in the running server.");
        project = Json.CloneProject(project);
        var unsupported = project.Screens.SelectMany(s => s.Events.Select(e => (Location: s.Id + "." + e.Key, Event: e.Value))
            .Concat(s.Elements.SelectMany(element => element.Events.Select(e => (Location: s.Id + "." + element.Id + "." + e.Key, Event: e.Value)))))
            .SelectMany(e => new[] { (e.Location, Side: "Client", Handler: e.Event.Client), (e.Location, Side: "Server", Handler: e.Event.Server) })
            .Where(e => e.Handler.Script.Length > 0 && (e.Side == "Server" ? e.Handler.ScriptEngine is not ("kubejs" or "standard") : e.Handler.ScriptEngine != "standard")).ToList();
        if (unsupported.Count > 0) {
            string details = string.Join("\n",unsupported.Select(e => $"  {e.Location} ({e.Side}): {e.Handler.Script}"));
            if (skipScripts.IsChecked != true) throw new InvalidOperationException("These assigned scripts cannot run in Minecraft yet:\n" + details + "\nChoose 'Test layout without unsupported scripts' to test visuals and built-in actions, or replace these assignments with supported actions/KubeJS Server scripts.");
            Print("LAYOUT TEST: skipping these script assignments in the test copy only:\n" + details);
            foreach (var entry in unsupported) { entry.Handler.Script = ""; entry.Handler.Function = ""; entry.Handler.ScriptEngine = "standard"; }
        }
        Dictionary<string,byte[]> files;
        bool kube = project.Screens.SelectMany(s => s.Events.Values.Concat(s.Elements.SelectMany(e => e.Events.Values))).Any(e => e.Server.ScriptEngine == "kubejs" && e.Server.Script.Length > 0);
        if (kube) files = KubeExport.Files(project).Where(f => f.Key.StartsWith("wysicraft/") || f.Key.StartsWith("kubejs/")).ToDictionary(f => f.Key, f => f.Value);
        else {
            var temp = Inside(activeRoot,"test-export.wysicraft"); ProjectStore.Export(project,temp); files = new() { ["wysicraft/"+project.Manifest.Id+".wysicraft"] = File.ReadAllBytes(temp) }; File.Delete(temp);
        }
        DeployFiles(files); projectId = project.Manifest.Id;
        Print("Deployed " + projectId + (kube ? " with KubeJS Server scripts." : " with built-in actions and Standard scripts."));
    }
    void DeployFiles(Dictionary<string,byte[]> files) {
        string runRoot = Inside(activeRoot,"run"); string ownership = Inside(activeRoot,"deployed-files.json");
        var previous = File.Exists(ownership) ? Json.Read<List<string>>(File.ReadAllText(ownership)) : [];
        foreach (string relative in files.Keys) {
            string path = Inside(runRoot,relative);
            if (File.Exists(path) && !previous.Contains(relative)) throw new IOException("A file not managed by this test already exists: " + path + ". Move it or choose another test instance.");
        }
        if (Running) foreach (var file in files.Where(f => f.Key.StartsWith("kubejs/startup_scripts/"))) {
            string path = Inside(runRoot,file.Key);
            if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(file.Value)) throw new InvalidOperationException("Stop and restart to install changed KubeJS startup helpers.");
        }
        foreach (var (relative,data) in files) { string path = Inside(runRoot,relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path+".tmp",data); File.Move(path+".tmp",path,true); }
        foreach (string relative in previous.Except(files.Keys)) { string path = Inside(runRoot,relative); if (File.Exists(path)) File.Delete(path); }
        File.WriteAllText(ownership,Json.Write(files.Keys.ToList()));
    }
    async Task Launch(string? jarPath = null, bool reportFailure = false) {
        if (Running || busy) return; busy = true; SetReady(false);
        try {
            if (!IsJava21(javaHome.Text)) throw new IOException("Minecraft 1.21.1 requires Java 21. Select a Java 21 home folder (not Java 17 or 26) containing bin/java.exe and its release file.");
            var jar = jarPath == null ? null : JarTestPackage.Read(jarPath);
            Setup();
            if (jar == null) Deploy(capture());
            else { DeployFiles(jar.Files); projectId = jar.ProjectId; Print("Installed project JAR from export: " + jarPath); if (jar.Files.Count > 1) Print("Installed the project's exported KubeJS companion scripts."); }
            activeJar = jarPath;
            // Editor and exports use the identical built runtime; never compile a second copy.
            string runtimeTarget=Inside(activeRoot,"run/mods/wysicraft-editor-runtime.jar");
            if(jar!=null && HasBundledRuntime(jar)) { if(File.Exists(runtimeTarget)) File.Delete(runtimeTarget); }
            else { Directory.CreateDirectory(Path.GetDirectoryName(runtimeTarget)!); File.Copy(runtimeJar(),runtimeTarget,true); }
            sourceLabel.Text = jarPath == null ? "Source: editor project" : "Source: export — " + jarPath;
            string runtimeSource=jar!=null && HasBundledRuntime(jar)?jarPath!:runtimeJar();
            sourceLabel.Text += " • Runtime " + (jar!=null && HasBundledRuntime(jar)?"embedded in export":Distribution.RuntimeVersion) + " • Build " + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(runtimeSource)))[..12];
            diagnostics.Clear();
            if (jarPath != null) Print("Editor content and the skip-scripts option are not used. F6 / .open opens this JAR's Main screen. Stop and select a new export to retest. Use the server JAR for full behavior; client-only exports omit server handlers.");
            token = Guid.NewGuid().ToString(); previousResult = ""; logPosition = 0; forcedStop = false;
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!); File.WriteAllText(SettingsFile,Json.Write(new Settings { Instance = activeRoot, JavaHome = javaHome.Text }));
            string control = Inside(activeRoot,"run/.wysicraft-test");
            foreach (string old in new[] { "request.json", "state.json" }) { string path = Path.Combine(control,old); if (File.Exists(path)) File.Delete(path); }
            File.WriteAllText(Path.Combine(control,"session.json"),Json.Write(new { token, project = projectId }));
            var info = new ProcessStartInfo(Path.Combine(javaHome.Text,"bin","java.exe")) { WorkingDirectory = activeRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.Environment["JAVA_HOME"] = javaHome.Text;
            foreach (string arg in new[] { "-classpath", Path.Combine(activeRoot,"gradle","wrapper","gradle-wrapper.jar"), "org.gradle.wrapper.GradleWrapperMain", "--no-daemon", "-PwysicraftTest", "runTestClient" }) info.ArgumentList.Add(arg);
            info.ArgumentList.Insert(info.ArgumentList.Count-1,"-PwysicraftExportTest");
            process = new Process { StartInfo = info, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) Dispatcher.InvokeAsync(() => Print(e.Data)); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) Dispatcher.InvokeAsync(() => Print(e.Data)); };
            process.Exited += (_, _) => Dispatcher.InvokeAsync(() => { Print("Minecraft test process exited."); SetReady(false); SaveBaseline(); });
            process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine(); Print("Launching real Minecraft. First setup may take several minutes.");
            await Task.CompletedTask;
        } catch (Exception ex) { Print("Launch failed: "+ex.Message); if(reportFailure) throw; }
        finally { busy = false; SetReady(false); }
    }
    void Send(string kind,string command = "") {
        if (!Running) throw new InvalidOperationException("Start the test instance first.");
        string target = Inside(activeRoot,"run/.wysicraft-test/request.json");
        if (File.Exists(target)) throw new InvalidOperationException("Wait for the previous request to be picked up.");
        File.WriteAllText(target+".tmp",Json.Write(new { token, id = Guid.NewGuid().ToString(), kind, command })); File.Move(target+".tmp",target,true);
        Print(kind == "command" ? "→ /"+command : "→ "+kind);
    }
    static bool HasBundledRuntime(JarTestPackage jar) {
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(jar.Files["mods/wysicraft-test-project.jar"]));
        return archive.GetEntry("META-INF/jarjar/metadata.json") != null;
    }
    void Poll() {
        if (!Running || activeRoot.Length == 0) return;
        try {
            string path = Inside(activeRoot,"run/.wysicraft-test/state.json");
            if (File.Exists(path)) {
                var state = Json.Read<TestState>(File.ReadAllText(path)); if (state.Token != token) return;
                SetReady(state.Ready); usages = state.Usage;
                var commands = state.Commands.Where(c => c != projectId+".open" && c != projectId+".close").ToList();
                if (!commands.SequenceEqual(globals.Items.Cast<string>())) { string? pick = globals.SelectedItem as string; globals.ItemsSource = commands; globals.SelectedItem = commands.Contains(pick ?? "") ? pick : commands.FirstOrDefault(); }
                string result = state.Error.Length > 0 ? "ERROR: "+state.Error : state.Result;
                if (result.Length > 0 && result != previousResult) { Print(result); previousResult = result; }
            }
            // Capture game messages even when Gradle routes them only to latest.log.
            string gameLog = Inside(activeRoot,"run/logs/latest.log");
            if (File.Exists(gameLog)) {
                using var stream = new FileStream(gameLog,FileMode.Open,FileAccess.Read,FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length < logPosition) logPosition = 0;
                stream.Seek(logPosition,SeekOrigin.Begin); using var reader = new StreamReader(stream);
                string text = reader.ReadToEnd(); logPosition = stream.Position;
                if (text.Length > 0) Print(text.Length > 20000 ? text[^20000..] : text);
            }
        } catch (IOException) { } catch (Exception ex) { Print("Test status: "+ex.Message); }
    }
    async Task Stop() {
        if (!Running || stopping) return; stopping = true;
        try { try { Send("stop"); } catch (Exception) { } var exited = process!.WaitForExitAsync(); if (await Task.WhenAny(exited,Task.Delay(15000)) != exited) { forcedStop = true; process.Kill(entireProcessTree:true); await process.WaitForExitAsync(); } }
        catch (Exception ex) { Print(ex.Message); }
        finally { stopping = false; SetReady(false); SaveBaseline(); }
    }
    void SaveBaseline() {
        if (Running || forcedStop) return;
        try { string world = Inside(activeRoot,"run/saves/Wysicraft Test"); string baseline = Inside(activeRoot,"baseline"); if (!stopping && File.Exists(Path.Combine(world,"level.dat")) && !Directory.Exists(baseline)) { CopyTree(world,baseline); Print("Saved the reusable test-world baseline."); } }
        catch (Exception ex) { Print("Baseline: "+ex.Message); }
    }
    void ResetWorld() {
        if (Running) return;
        activeRoot = Path.GetFullPath(instance.Text);
        if (!File.Exists(Inside(activeRoot,"wysicraft-test-instance.txt"))) throw new IOException("Set up this test instance first.");
        string world = Inside(activeRoot,"run/saves/Wysicraft Test");
        if (Directory.Exists(world)) Directory.Move(world,Inside(activeRoot,"world-backup-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")));
        string baseline = Inside(activeRoot,"baseline"); if (Directory.Exists(baseline)) CopyTree(baseline,world);
        Print("Test world reset. Previous world retained as a backup; downloaded game files are reused.");
    }
}
