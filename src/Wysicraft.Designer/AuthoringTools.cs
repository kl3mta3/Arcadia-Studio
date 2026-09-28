using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wysicraft.Core;
using Wysicraft.Models;
using Wysicraft.Packaging;
using Microsoft.Win32;

namespace Wysicraft.Designer;
public partial class MainWindow
{
    static readonly string[] ApiSnippets = [
        "ctx.ui.setText('label_id', 'Hello');", "ctx.ui.setItem('item_id', 'minecraft:diamond');",
        "ctx.ui.setItems('list_id', [{item:'minecraft:diamond',count:3,name:'Diamond'}]);",
        "ctx.ui.setVisible('element_id', true);", "ctx.ui.setEnabled('element_id', true);",
        "ctx.state.set('name', 'value');", "ctx.state.get('name');", "ctx.ui.close();",
        "ctx.server.runCommand('say Hello');", "ctx.ui.open('screen_id');",
        "ctx.player.getName();", "ctx.player.getInventory();", "ctx.player.getPosition();", "ctx.player.hasPermission(2);",
        "ctx.ui.setItems('list_id', ctx.player.getInventory());", "console.log('Debug message');",
        "ctx.ui.play('sprite_id', 'run'); // sprite clip", "ctx.ui.setValue('sound_id', 'play'); // Sound control: play or stop", "ctx.client.playSound('myproject:click');", "ctx.repeat // true while a key is held down",
        "ctx.ui.animate('animation_id'); // web & desktop", "ctx.ui.stopAnimation('animation_id'); // web & desktop", "ctx.ui.setVelocity('body_id', 0, -200); // web & desktop physics", "ctx.ui.setPosition('body_id', 40, 20); // web & desktop", "ctx.ui.setSize('camera1', 240, 135); // web & desktop: resize (a smaller camera zooms in)", "ctx.ui.changeTexture('image_id', 'myproject:textures/gui/image/other.png');",
        "ctx.input.isDown('jump'); // web & desktop inputs", "ctx.input.axis('left'); // 0-1, web & desktop", "ctx.ui.getElement('body_id').vx; // x, y, width, height, vx, vy (web & desktop)",
        "ctx.physics.touching('player'); // IDs it's touching or overlapping (web & desktop)", "ctx.physics.isTouching('player', 'goal'); // web & desktop"
    ];
    void ShowScriptApi() {
        var window=new Window { Owner=this,Title="Script API / insert snippet",Width=660,Height=440 };
        var panel=new DockPanel { Margin=new Thickness(12) }; window.Content=panel;
        var help=new TextBlock { Text="Standard scripts: ctx.ui and ctx.state work on both sides. ctx.player, server commands and ui.open require Server events. KubeJS uses event.ui/event.runCommand. Select a snippet and click Insert, or double-click. Ctrl+Space opens this list.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4) };
        DockPanel.SetDock(help,Dock.Top); panel.Children.Add(help);
        var insert=new Button { Content="Insert into script" }; DockPanel.SetDock(insert,Dock.Bottom);panel.Children.Add(insert);
        var list=new ListBox { ItemsSource=ApiSnippets,FontFamily=new FontFamily("Consolas"),SelectedIndex=0 }; panel.Children.Add(list);
        void Insert() { if(editingScript==null) { Log("Select or create a script first."); return; } ScriptEditor.SelectedText=list.SelectedItem+Environment.NewLine; window.Close();ScriptEditor.Focus(); }
        insert.Click+=(_,_)=>Insert(); list.MouseDoubleClick+=(_,_)=>Insert();window.Show();
    }
    internal static string ItemListStamp(string json,Wysicraft.Models.Element element,IReadOnlyDictionary<string,string>? state) => json+(element.RowTemplate.Length>0 || element.RowElements.Count>0 ? Json.Write(state ?? new Dictionary<string,string>()) : "");
    void FillItemList(ListBox list,string json, Wysicraft.Models.Element? element=null, Action<string>? fire=null, IReadOnlyDictionary<string,string>? state=null) {
        list.Tag=element==null?json:ItemListStamp(json,element,state);list.Items.Clear();
        try { int index=0; foreach(var row in ItemRows.Parse(json)) {
            int selectedIndex=index++; if(element!=null && (element.RowTemplate.Length>0 || element.RowElements.Count>0)) {list.Items.Add(new ListBoxItem {Content=RenderTemplateRow(element,row,selectedIndex,fire,state),Padding=new Thickness(0),Margin=new Thickness(0),Height=element.RowHeight*Zoom,HorizontalContentAlignment=HorizontalAlignment.Left});continue;} var panel=new DockPanel();
            if(element!=null) foreach(var (label,ev) in new[]{(element.SecondaryLabel,"item_secondary"),(element.PrimaryLabel,"item_primary")}) if(label.Length>0) {var button=new Button {Content=label,MinWidth=65}; DockPanel.SetDock(button,Dock.Right); panel.Children.Add(button);button.Click+=(_,args)=>{element.Text=selectedIndex.ToString();fire?.Invoke(ev);args.Handled=true;};}
            panel.Children.Add(new TextBlock {Text=$"◇ {row.Name}   ×{row.Count}"+(element?.ShowItemId==true?"\n"+row.Item:""),VerticalAlignment=VerticalAlignment.Center});
            list.Items.Add(new ListBoxItem { Content=panel,ToolTip=row.Item,Height=(element?.RowHeight ?? 30)*2,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(4) });
        } }
        catch(Exception ex) { list.Items.Add("Item List: "+ex.Message); }
    }
    string RuntimeJar() {
        for(var directory=new DirectoryInfo(AppContext.BaseDirectory);directory!=null;directory=directory.Parent) {
            foreach(var relative in new[]{"Runtime","wysicraft-runtime/build/libs"}) {
                string path=Path.Combine(directory.FullName,relative,"wysicraft-"+Distribution.RuntimeVersion+".jar"); if(File.Exists(path)) return path;
            }
        }
        throw new InvalidDataException("Matching runtime JAR missing. Keep Runtime beside Designer.");
    }
    void ExportArtifact(string format,string path) {
        switch(format) {
            case "installation": Distribution.Export(project,path,RuntimeJar()); break;
            case "kubejs": case "jar": Distribution.Write(path,Distribution.BundledJar(project,RuntimeJar()));break;
            case "kubejs_files": KubeExport.Export(project,path);break;
            case "standard": ProjectStore.Export(project,path);break;
            case "web_folder": Wysicraft.Packaging.WebExport.ExportFolder(project,path);break;
            case "web_file": Wysicraft.Packaging.WebExport.ExportSingleFile(project,path);break;
            case "windows_app": DesktopExport.WindowsApp(project,path,AppHostExe());break;
            default: throw new InvalidDataException("Unknown export format");
        }
    }
    // Export() lives in ExportDialog.cs.
}


