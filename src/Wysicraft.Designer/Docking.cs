using System.IO;
using System.Windows;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;

namespace Wysicraft.Designer;

public partial class MainWindow
{
    readonly Dictionary<string,object> dockContents = new();
    string defaultDockLayout = "";
    bool DockSmoke => Environment.GetCommandLineArgs().Any(a=>a.StartsWith("--smoke"));
    // Versioned so the 1.2 default arrangement (shorter Output, wider left panels) applies once.
    static string DockLayoutPath => Wysicraft.Core.AppFolders.Path("workspace-layout-2.xml");

    void InitializeDocking()
    {
        if(!creatorMode) EventManager.RegisterClassHandler(typeof(AvalonDock.Controls.LayoutFloatingWindowControl),System.Windows.Input.Keyboard.PreviewKeyDownEvent,new System.Windows.Input.KeyEventHandler(Keys));
        foreach(var item in Workspace.Layout.Descendents().OfType<LayoutContent>()) dockContents[item.ContentId]=item.Content;
        using var writer=new StringWriter(); new XmlLayoutSerializer(Workspace).Serialize(writer); defaultDockLayout=writer.ToString();
        Loaded+=(_,_)=> {
            if(DockSmoke || !File.Exists(DockLayoutPath)) return;
            try { RestoreDockLayout(File.ReadAllText(DockLayoutPath)); }
            catch(Exception ex) { ResetDockLayout(); Log("Saved workspace layout could not be restored: "+ex.Message); }
        };
        Closed+=(_,_)=> {
            if(DockSmoke || creatorMode) return;
            try { Directory.CreateDirectory(Path.GetDirectoryName(DockLayoutPath)!); using var saved=new StringWriter(); new XmlLayoutSerializer(Workspace).Serialize(saved); File.WriteAllText(DockLayoutPath+".tmp",saved.ToString()); File.Move(DockLayoutPath+".tmp",DockLayoutPath,true); }
            catch(Exception ex) { System.Diagnostics.Debug.WriteLine("Workspace layout: "+ex.Message); }
        };
    }

    void RestoreDockLayout(string xml)
    {
        // Content instances hold unsaved edits and event subscriptions. Reuse them.
        var serializer=new XmlLayoutSerializer(Workspace);
        serializer.LayoutSerializationCallback+=(_,e)=> {
            if(dockContents.TryGetValue(e.Model.ContentId,out var content)) e.Content=content;
            else e.Cancel=true;
        };
        using var reader=new StringReader(xml); serializer.Deserialize(reader);
        // Add new panels to older layouts without discarding the user's arrangement.
        foreach(var (id,title) in new[]{("assets","Assets"),("items","Items"),("components","Components")})if(!Workspace.Layout.Descendents().OfType<LayoutContent>().Any(p=>p.ContentId==id)) {
            var pane=Workspace.Layout.Descendents().OfType<LayoutAnchorablePane>().First();
            pane.Children.Add(new LayoutAnchorable {ContentId=id,Title=title,Content=dockContents[id],CanClose=false});
        }
        foreach(var item in Workspace.Layout.Descendents().OfType<LayoutContent>()) {
            item.FloatingWidth=Math.Clamp(item.FloatingWidth>0?item.FloatingWidth:500,200,Math.Max(200,SystemParameters.VirtualScreenWidth));
            item.FloatingHeight=Math.Clamp(item.FloatingHeight>0?item.FloatingHeight:400,150,Math.Max(150,SystemParameters.VirtualScreenHeight));
            item.FloatingLeft=Math.Clamp(item.FloatingLeft,SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+Math.Max(0,SystemParameters.VirtualScreenWidth-item.FloatingWidth));
            item.FloatingTop=Math.Clamp(item.FloatingTop,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+Math.Max(0,SystemParameters.VirtualScreenHeight-item.FloatingHeight));
        }
    }

    void ShowDock(string id)
    {
        var item=Workspace.Layout.Descendents().OfType<LayoutAnchorable>().FirstOrDefault(p=>p.ContentId==id);
        if(item==null) { ResetDockLayout(); item=Workspace.Layout.Descendents().OfType<LayoutAnchorable>().First(p=>p.ContentId==id); }
        if(item.IsHidden) item.Show();
        if(item.IsAutoHidden) item.ToggleAutoHide();
        item.IsActive=true; item.IsSelected=true;
    }
    void ResetDockLayout() => RestoreDockLayout(defaultDockLayout);
    // AvalonDock opens floating windows asynchronously; tests wait for that before moving on or closing.
    void FlushUi()=>Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);

    internal void VerifyDocking(string output)
    {
        var original=ScriptEditor.Text;
        ScriptEditor.Text="// unsaved docking smoke";
        foreach(string id in new[]{"toolbox","layers","properties","events","output","scripts"}) {
            var pane=Workspace.Layout.Descendents().OfType<LayoutAnchorable>().First(p=>p.ContentId==id);
            pane.Float(); FlushUi(); UpdateLayout();
            if(!pane.IsFloating) throw new InvalidOperationException(id+" did not float");
            pane.Dock(); FlushUi(); pane.Hide(); ShowDock(id); FlushUi();
            if(pane.IsHidden) throw new InvalidOperationException(id+" did not reopen");
        }
        var scripts=Workspace.Layout.Descendents().OfType<LayoutAnchorable>().First(p=>p.ContentId=="scripts"); scripts.Float(); FlushUi();
        using var saved=new StringWriter(); new XmlLayoutSerializer(Workspace).Serialize(saved);
        ResetDockLayout(); FlushUi(); RestoreDockLayout(saved.ToString()); FlushUi(); UpdateLayout();
        if(ScriptEditor.Text!="// unsaved docking smoke" || Workspace.Layout.Descendents().OfType<LayoutContent>().Any(p=>!ReferenceEquals(p.Content,dockContents[p.ContentId]))) throw new InvalidOperationException("Restoring layout lost editor content");
        if(!Workspace.Layout.Descendents().OfType<LayoutAnchorable>().First(p=>p.ContentId=="scripts").IsFloating) throw new InvalidOperationException("Floating layout was not restored");
        ResetDockLayout(); FlushUi(); ScriptEditor.Text=original; dirty=false;
        UpdateLayout();
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(this);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap)); using(var file=File.Create(output+".png")) encoder.Save(file);
        File.WriteAllText(output,"PASS: six panels float/dock/hide/reopen; layout restore preserves floating state and editor contents; reset works.");
    }
}
