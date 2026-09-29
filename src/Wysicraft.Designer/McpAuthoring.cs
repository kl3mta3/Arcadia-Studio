using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ModelContextProtocol.Server;
using Wysicraft.Core;
using Wysicraft.Models;
using Wysicraft.Packaging;

namespace Wysicraft.Designer;
public partial class MainWindow
{
    PreviewSession? activePreview;
    string previewRevision="";
    internal Task<string> McpWork(string operation,string expected="",string path="",string screen="",string element="",string eventName="click",string value="",CancellationToken cancellationToken=default) => Dispatcher.InvokeAsync(async ()=> {
        try {
            if(mcpHost==null) throw new InvalidOperationException("MCP server is stopped.");
            if(operation is not ("test_stop" or "preview_close" or "templates")) CheckRevision(expected);
            switch(operation) {
                case "save_as":
                    if(!Path.IsPathFullyQualified(path) || !path.EndsWith(".wysicraftproj",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Provide an absolute .wysicraftproj path in an existing directory.");
                    if(File.Exists(path) && !string.Equals(path,folder,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Destination exists. Choose a new filename.");
                    ProjectStore.SaveProject(project,path);folder=path;dirty=false;ClearRecovery();Log("MCP saved "+path);return Json.Write(new{path,revision=Revision()});
                case "import_asset":
                    if(!Path.IsPathFullyQualified(path) || !File.Exists(path)) throw new InvalidDataException("Provide an absolute path to an existing PNG, sound (.ogg/.mp3/.wav/.m4a/.aac) or .png.mcmeta file.");
                    if(new FileInfo(path).Length>ProjectStore.MaxEntry) throw new InvalidDataException("Files are at most 32 MiB.");
                    if(SoundAssets.Extensions.Any(x=>path.EndsWith(x,StringComparison.OrdinalIgnoreCase))) {
                        string soundName=System.Text.RegularExpressions.Regex.Replace(Path.GetFileNameWithoutExtension(path).ToLowerInvariant(),"[^a-z0-9_-]","_")+Path.GetExtension(path).ToLowerInvariant();
                        string soundPath=SoundAssets.Path(project.Manifest.Id,soundName),soundId=SoundAssets.Resource(soundPath);
                        if(SoundAssets.Find(project,soundId)!=null) throw new InvalidDataException("A sound called "+soundId+" already exists. Import a uniquely named file.");
                        Change();project.Assets[soundPath]=File.ReadAllBytes(path);RefreshAll();RefreshAssetBrowser();
                        return Json.Write(new{sound=soundId,minecraft=soundPath.EndsWith(".ogg"),note=soundPath.EndsWith(".ogg")?"Plays everywhere.":"Web and desktop only: Minecraft plays .ogg sounds.",revision=Revision()});
                    }
                    if(path.EndsWith(".png.mcmeta",StringComparison.OrdinalIgnoreCase)) {
                        var metaText=File.ReadAllText(path);System.Text.Json.JsonDocument.Parse(metaText).Dispose();
                        string png=Path.GetFileName(path[..^7]).ToLowerInvariant();var image=project.Assets.Keys.FirstOrDefault(k=>k.EndsWith("/"+png)) ?? throw new InvalidDataException("Import "+png+" first; the animation file joins the image with the same name.");
                        Change();project.Assets[image+".mcmeta"]=System.Text.Encoding.UTF8.GetBytes(metaText);RefreshAll();RefreshAssetBrowser();
                        return Json.Write(new{animated=image,revision=Revision()});
                    }
                    if(!path.EndsWith(".png",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Provide a PNG, a sound (.ogg/.mp3/.wav/.m4a/.aac) or a .png.mcmeta file.");
                    var bytes=File.ReadAllBytes(path);ProjectStore.TextureSize(bytes);
                    string name=System.Text.RegularExpressions.Regex.Replace(Path.GetFileNameWithoutExtension(path).ToLowerInvariant(),"[^a-z0-9_-]","_")+".png";
                    string resource=TextureAssets.Resource(project.Manifest.Id,name), asset=TextureAssets.Path(project.Manifest.Id,name);
                    if(project.Assets.ContainsKey(asset)) throw new InvalidDataException("Asset already exists. Import a uniquely named PNG.");
                    Change();project.Assets[asset]=bytes;RefreshAll();return Json.Write(new{resource,revision=Revision()});
                case "export_electron":
                {
                    var platforms=(value.Length>0?value:"darwin-arm64,darwin-x64,linux-x64").Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
                    if(platforms.Any(p=>!DesktopExport.ElectronPlatforms.Contains(p))) throw new InvalidDataException("Platforms are "+string.Join(", ",DesktopExport.ElectronPlatforms));
                    var electronErrors=Wysicraft.Core.Validation.Errors(project);if(electronErrors.Count>0)throw new InvalidDataException(string.Join("\n",electronErrors));
                    var snapshot=Json.CloneProject(project);
                    string outputFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Wysicraft","McpExports",project.Manifest.Id+"-electron-"+Guid.NewGuid().ToString("N")[..8]);
                    string version=await DesktopExport.LatestElectronAsync(ExportHttp,cancellationToken);var made=new List<string>();
                    foreach(var platform in platforms){var zip=await DesktopExport.ElectronZipAsync(ExportHttp,version,platform,null,cancellationToken);made.Add(await Task.Run(()=>DesktopExport.ElectronApp(snapshot,zip,platform,outputFolder),cancellationToken));}
                    Log("MCP exported Electron "+version+" apps to "+outputFolder);LogSizeWarning("electron");return Json.Write(new{electron=version,files=made,sizeWarning=DownloadSize.Warning(snapshot,"electron")});
                }
                case "preview_open":
                    if(activePreview!=null) await activePreview.CloseAsync();
                    if(screen.Length==0) screen=ui.Id;
                    if(!project.Screens.Any(s=>s.Id==screen)) throw new InvalidDataException("Screen not found");
                    var errors=Wysicraft.Core.Validation.Errors(project);if(errors.Count>0)throw new InvalidDataException(string.Join("\n",errors));
                    activePreview=new PreviewSession(this,Json.CloneProject(project),screen);previewRevision=Revision();activePreview.Window.Show();
                    return await activePreview.SnapshotAsync();
                case "preview_close":
                    if(activePreview!=null) await activePreview.CloseAsync();activePreview=null;return Json.Write(new{closed=true});
                case "preview_event": case "preview_capture": case "preview_script": case "preview_state": case "preview_wait": case "preview_profile":
                    if(activePreview==null || !activePreview.Window.IsVisible)throw new InvalidOperationException("Call preview_open first.");
                    if(previewRevision!=Revision())throw new InvalidOperationException("Project changed. Reopen Preview to use the latest project.");
                    if(operation=="preview_event") { await activePreview.RunMcpEvent(element,eventName,value);return await activePreview.SnapshotAsync(); }
                    if(operation=="preview_script") { await activePreview.RunScriptAsync(value);return await activePreview.SnapshotAsync(); }
                    if(operation=="preview_state") return await activePreview.SnapshotAsync();
                    if(operation=="preview_profile") { await activePreview.SetProfilerAsync(value.Trim().ToLowerInvariant() is not ("off" or "false" or "0"));return await activePreview.SnapshotAsync(); }
                    // Lets animations, physics and timers run for a while (value = milliseconds, up to 10000), then reports the state.
                    if(operation=="preview_wait") { await Task.Delay(int.TryParse(value,out int ms)?Math.Clamp(ms,0,10000):500);return await activePreview.SnapshotAsync(); }
                    string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Wysicraft","McpCaptures");Directory.CreateDirectory(root);
                    string capture=Path.Combine(root,Guid.NewGuid().ToString("N")+".png");await activePreview.CaptureCanvas(capture);return Json.Write(new{path=capture});
                case "templates":
                    return Json.Write(new { templates=ScriptTemplate.All.Select(t=>new{ id=t.Title,title=t.Title,server=t.Server,engine=t.Engine,global=t.Global,source=t.Source("on_event",project.Manifest.Id).Replace("__PROJECT__",project.Manifest.Id) }),instructions="Use apply_template with an id, screen, eventName, and optional element. Constants in source are editable placeholders. Global registration uses KubeJS and exports because its script is assigned to an event." });
                case "template_apply":
                    var template=ScriptTemplate.All.SingleOrDefault(t=>t.Title==path) ?? throw new InvalidDataException("Template not found; call get_templates.");
                    var screenDefinition=project.Screens.SingleOrDefault(s=>s.Id==screen) ?? throw new InvalidDataException("Screen not found");
                    string function=value.Length==0?"on_"+eventName:value; CheckFunction(function);
                    string prefix=template.Server?"scripts/server/":"scripts/client/";
                    string stem=screen+"_"+(element.Length==0?"screen":element)+"_"+eventName;
                    string scriptPath=prefix+stem+".js";int n=2;while(project.Scripts.ContainsKey(scriptPath))scriptPath=prefix+stem+"_"+(n++)+".js";
                    var handler=new {script=scriptPath,function,scriptEngine=template.Engine};
                    var edits=new List<ProjectEdit> {
                        new(){Kind="put_script",Key=scriptPath,Source=template.Source(function,project.Manifest.Id).Replace("__PROJECT__",project.Manifest.Id)},
                        new(){Kind="set_event",Screen=screen,Element=element,Key=eventName,Data=System.Text.Json.JsonSerializer.SerializeToElement(template.Server?(object)new{server=handler}:new{client=handler})}
                    };
                    string result=await McpInvoke("apply_edits",expected,edits,cancellationToken:cancellationToken);
                    return Json.Write(new{script=scriptPath,function,engine=template.Engine,result=System.Text.Json.JsonSerializer.Deserialize<object>(result)});
                case "project_new": case "project_open":
                    if(dirty) throw new InvalidOperationException("Save the current project before replacing it.");
                    if(activePreview!=null) throw new InvalidOperationException("Close Preview before replacing the project.");
                    Project replacement;
                    if(operation=="project_open") {
                        if(!Path.IsPathFullyQualified(path))throw new InvalidDataException("Provide an absolute project path.");
                        replacement=ProjectStore.Load(path);
                    } else {
                        if(!Wysicraft.Core.Validation.Id(value))throw new InvalidDataException("Provide a lowercase project ID.");
                        replacement=new();replacement.Manifest.Id=value;replacement.Manifest.Name=value;
                    }
                    if(replacement.Screens.Count==0)throw new InvalidDataException("Project needs a screen.");
                    project=replacement;ui=project.Screens.FirstOrDefault(s=>s.Id==project.Manifest.DefaultUi)??project.Screens[0];
                    folder=operation=="project_open" && path.EndsWith(".wysicraftproj",StringComparison.OrdinalIgnoreCase)?path:null;
                    editingScript=null;selected.Clear();history.Clear();dirty=operation=="project_new";RefreshAll();return await McpInvoke("get_project",cancellationToken:cancellationToken);
                case "test_start": case "test_export": case "test_apply": case "test_stop": case "test_open": case "test_close": case "test_command":
                    if(operation=="test_stop" && minecraftTest==null)return Json.Write(new{running=false});
                    if(minecraftTest==null)TestMinecraft();
                    await minecraftTest!.McpControl(operation,path,value);return minecraftTest.McpStatus();
                default: throw new InvalidOperationException("Unknown operation");
            }
        } catch(Exception ex) when(ex is not OperationCanceledException) { throw new ModelContextProtocol.McpException(ex.Message); }
    },System.Windows.Threading.DispatcherPriority.Normal,cancellationToken).Task.Unwrap();
}

public sealed partial class DesignerMcpTools
{
    [McpServerTool(Name="save_project_as"),Description("Save the live project as a single editable .wysicraftproj at an absolute path. Requires an existing parent directory; refuses to overwrite a different existing file.")]
    public Task<string> SaveAs(string expectedRevision,string path,CancellationToken cancellationToken)=>editor.McpWork("save_as",expectedRevision,path,cancellationToken:cancellationToken);
    [McpServerTool(Name="import_asset"),Description("Import a local file (absolute path, at most 32 MiB): a PNG becomes a texture (returns its resource ID); a sound (.ogg, .mp3, .wav, .m4a, .aac) becomes a sound ID for play_sound actions and Sound controls (Minecraft only plays .ogg); a name.png.mcmeta animates the already-imported name.png (Minecraft animation format). One Undo step; refuses duplicate names.")]
    public Task<string> ImportAsset(string expectedRevision,string path,CancellationToken cancellationToken)=>editor.McpWork("import_asset",expectedRevision,path,cancellationToken:cancellationToken);
    [McpServerTool(Name="preview_control"),Description("Operate Preview (the same web runtime as HTML, Windows and Electron exports). action: open (optional screen), close, event (element, eventName, value; omit element for screen events such as key, tick, input_pressed with value=input name), script (value = JavaScript run as a client script, e.g. ui.animate('intro') or ui.setVelocity('ball',0,-300)), state (screen, variables and live element bounds, e.g. after physics), wait (value = milliseconds up to 10000 to let timers, animations and physics run, then state), capture (returns a local PNG path), profile (value on/off: the Profiler box; while on, results include profile = {fps, frameMs, worstFrameMs, engineMs, logicMs, objectsMs, physicsMs, particlesMs, drawMs, scriptMs, scriptsPerSecond, controls, spawned, bodies, pairs, particles, ...}, averaged over the last quarter second). Every result except capture includes state and console logs. Server operations are simulated. close ignores revision and field-edit locks.")]
    public Task<string> PreviewControl(string expectedRevision,string action,string screen="",string element="",string eventName="click",string value="",CancellationToken cancellationToken=default)=>editor.McpWork("preview_"+action,expectedRevision,screen:screen,element:element,eventName:eventName,value:value,cancellationToken:cancellationToken);
    [McpServerTool(Name="export_electron_apps"),Description("Export Electron desktop apps into a new folder under LocalAppData/Wysicraft/McpExports. platforms: comma list of win32-x64, darwin-arm64, darwin-x64, linux-x64 (default: both macOS and Linux; for Windows prefer export_project windows_app, which is under 1 MB instead of ~100 MB). Downloads Electron from its official GitHub releases the first time (about 100 MB per platform, checksum-verified, cached). Returns the files.")]
    public Task<string> ExportElectron(string expectedRevision,string platforms="",CancellationToken cancellationToken=default)=>editor.McpWork("export_electron",expectedRevision,value:platforms,cancellationToken:cancellationToken);
    [McpServerTool(Name="minecraft_test_control"),Description("Control Minecraft: action is start (editor), export (absolute JAR/ZIP path), apply, open, close, command (command text), or stop. Read get_test_status until ready and to obtain command completion/errors. Uses configured Java/instance. Stop ignores revision and field-edit locks. Launch may download dependencies.")]
    public Task<string> MinecraftControl(string expectedRevision,string action,string path="",string command="",CancellationToken cancellationToken=default)=>editor.McpWork("test_"+action,expectedRevision,path:path,value:command,cancellationToken:cancellationToken);
    [McpServerTool(Name="get_templates",ReadOnly=true),Description("List editable script and global-command templates, source code, engine, and assignment guidance.")]
    public Task<string> Templates(CancellationToken cancellationToken)=>editor.McpWork("templates",cancellationToken:cancellationToken);
    [McpServerTool(Name="apply_template"),Description("Create a uniquely named script from a template id returned by get_templates and assign it to an event in one Undo step. Existing scripts are preserved; assignment on that side is replaced. Edit its placeholder constants using put_script.")]
    public Task<string> ApplyTemplate(string expectedRevision,string templateId,string screen,string eventName,string element="",string function="",CancellationToken cancellationToken=default)=>editor.McpWork("template_apply",expectedRevision,path:templateId,screen:screen,element:element,eventName:eventName,value:function,cancellationToken:cancellationToken);
    [McpServerTool(Name="project_control"),Description("Create or open a project. action is new (projectId required) or open (absolute path required). Refuses to replace unsaved changes or an active preview; save/close first.")]
    public Task<string> ProjectControl(string expectedRevision,string action,string path="",string projectId="",CancellationToken cancellationToken=default)=>editor.McpWork("project_"+action,expectedRevision,path:path,value:projectId,cancellationToken:cancellationToken);
}

