using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wysicraft.Core;
using Wysicraft.Models;
namespace Wysicraft.Designer;

// Menus, toolbar and the small screen/layer buttons, all built from the command list in Shortcuts.cs.
public partial class MainWindow
{
    TextBlock zoomLabel=null!;
    readonly List<(Button Button,string Command,string Tip)> toolbarButtons=new();
    static readonly Brush AccentBrush=new SolidColorBrush(Color.FromRgb(0x0E,0x63,0x9C));
    static readonly Brush RunningBrush=new SolidColorBrush(Color.FromRgb(0x2E,0x7D,0x32));

    void BuildMenus() {
        RegisterCommands();
        MenuItem Top(string header,params object[] entries) {
            var menu=new MenuItem {Header=header};
            foreach(var entry in entries)menu.Items.Add(entry switch {string id when id=="-"=>new Separator(),string id=>CommandItem(id),_=>entry});
            TrackChecks(menu);Menus.Items.Add(menu);return menu;
        }
        Top("_File","file.new","file.open","file.recover","-","file.save","file.saveAs","file.looseCopy","-","file.export","file.exportKube","file.publish","-","file.exit");
        Top("_Edit","edit.undo","edit.redo","-","edit.cut","edit.copy","edit.paste","edit.duplicate","edit.delete","edit.selectAll","-",
            "edit.group","edit.ungroup","edit.newGroup","edit.isolate","-","edit.attach","edit.detach","edit.toggleLock","-","edit.rename","-",OrderMenu(),ArrangeMenu());
        MenuItem OrderMenu(){var order=new MenuItem {Header="Arrange"};foreach(var id in new[]{"edit.bringToFront","edit.bringForward","edit.sendBackward","edit.sendToBack"})order.Items.Add(CommandItem(id));return order;}
        var panels=new MenuItem {Header="_Panels"};
        foreach(var (title,id) in new[]{("Toolbox","toolbox"),("Assets","assets"),("Components","components"),("Minecraft items","items"),("Layers","layers"),("Properties","properties"),("Events","events"),("Scripts","scripts"),("Output","output")}) {
            var item=new MenuItem {Header=title};item.Click+=(_,_)=>Guard(()=>ShowDock(id));panels.Items.Add(item);
        }
        Top("_View",panels,"view.resetLayout","-","view.zoomIn","view.zoomOut","view.zoomActual","view.zoomFit","-","view.grid","view.snap","-","view.shortcuts","view.askLockChildren");
        advancedMenu=Top("_Advanced","advanced.inputs","advanced.inputCreator","advanced.animations","advanced.stateGraphs","advanced.shaders","advanced.particleMaker","advanced.particles","advanced.collider","advanced.tilemap","advanced.layers","-","advanced.toolbox");
        Top("_Project","project.preview","project.test","project.validate","-","project.screen","project.settings","-","project.importTexture","project.pixelEditor","project.spriteSheet","-","project.musicMaker","project.soundEffects","project.importAudio","-","project.mcp");
        Top("_Help","help.manual","help.scriptApi","view.shortcuts","-","help.about");

        // Toolbar: every action stays visible; the AI (MCP) button is highlighted so it's easy to find.
        void Tool(string icon,string text,string command,string tip) {
            var button=new Button {Content=Icons.WithText(icon,text)};button.Click+=(_,_)=>Guard(Cmd(command).Run);
            toolbarButtons.Add((button,command,tip));Toolbar.Children.Add(button);
        }
        void Divider()=>Toolbar.Children.Add(new Border {Width=1,Height=20,Margin=new Thickness(6,0,6,0),Background=new SolidColorBrush(Color.FromRgb(0x45,0x4B,0x56)),VerticalAlignment=VerticalAlignment.Center});
        Tool("preview","Preview","project.preview","Try the screen with working buttons and scripts.");
        Tool("test","Minecraft test","project.test","Launch Minecraft with this project installed.");
        Divider();
        Tool("validate","Validate","project.validate","Check the project for errors before exporting.");
        Tool("export","Export…","file.export","Export a project JAR or installation ZIP.");
        Tool("kubejs","Export for KubeJS","file.exportKube","Export for servers that run KubeJS scripts.");
        Divider();
        AddMcpButton();
        Divider();
        zoomLabel=new TextBlock {VerticalAlignment=VerticalAlignment.Center,Foreground=Brushes.LightGray,Margin=new Thickness(4,0,0,0)};Toolbar.Children.Add(zoomLabel);
        UpdateZoomLabel();RefreshToolbarTooltips();
        InitializeScreenBar();
    }
    void RefreshToolbarTooltips() {
        foreach(var (button,command,tip) in toolbarButtons) {string g=DisplayGesture(GestureFor(command));button.ToolTip=g.Length>0?$"{tip} ({g})":tip;}
    }
    void UpdateZoomLabel() {
        if(zoomLabel==null)return;
        // The canvas draws 2 screen pixels per Minecraft GUI pixel at 100%.
        zoomLabel.Text=$"Zoom {viewScale:P0}  •  1 GUI pixel = {Zoom*viewScale:0.##} screen pixels";
    }
    void SetMcpButton(bool running) {
        mcpButton.Content=Icons.WithText("mcp",running?"MCP running – connect":"Start MCP server",Brushes.White);
        mcpButton.Background=running?RunningBrush:AccentBrush;mcpButton.FontWeight=FontWeights.SemiBold;
        mcpButton.ToolTip=running?"MCP is running. Click for the connection details to give your AI assistant.":"Let an AI assistant (Claude, ChatGPT and others) read and edit this project.";
    }
    void InitializeScreenBar() {
        AddScreen.Content=Icons.WithText("add","New screen");AddScreen.ToolTip="Add a screen to this project.";
        ScreenSettings.Content=Icons.WithText("settings","Screen settings");ScreenSettings.ToolTip="Show every setting for this screen in Properties, and its open/close events in Events.";
        DeleteScreenButton.Content=Icons.WithText("delete","Delete screen");
        LayerTop.Content=Icons.Get("top");LayerBottom.Content=Icons.Get("bottom");LayerUp.Content=Icons.Get("up");LayerDown.Content=Icons.Get("down");LayerDuplicate.Content=Icons.Get("duplicate");LayerNewGroup.Content=Icons.WithText("add","Group");
        LayerTop.ToolTip="Bring to front";LayerBottom.ToolTip="Send to back";LayerUp.ToolTip="Bring forward";LayerDown.ToolTip="Send backward";LayerDuplicate.ToolTip="Duplicate";
    }
    void ShowScreenSettings() {
        selected.Clear();dragBounds=null;Surface.ReleaseMouseCapture();Draw();RefreshInspector();ShowDock("properties");
    }
    void ShowAbout() {
        string version=typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        MessageBox.Show(this,$"Arcadia Studio {version}\nVisual UI and 2D game editor for the web, desktop and Minecraft 1.21.1 / NeoForge\nBundled Minecraft runtime {RuntimeInfo.Version}\nClient and server JavaScript use the bundled engine.","About Arcadia Studio");
    }
    // The release ships the manual as Docs\Wysicraft-Manual.html beside the Designer folder; development builds fall back to the online wiki.
    internal static string ManualPath => System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "Docs", "Arcadia-Studio-Manual.html"));
    const string OnlineManual = "https://github.com/kl3mta3/WYSICRAFT/wiki";
    void OpenManual() {
        string target = System.IO.File.Exists(ManualPath) ? ManualPath : OnlineManual;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
    }
    void IsolateSelectedGroup() {
        var element=ui.Elements.FirstOrDefault(e=>selected.Contains(e.Id)) ?? throw new InvalidOperationException("Select something in a group first.");
        string group=CanvasGroup(element);if(group.Length==0)throw new InvalidOperationException(element.Id+" isn't in a group.");
        IsolateGroup(group);
    }
    // Locked controls stay visible but can't be clicked, dragged, box-selected or nudged on the canvas.
    void ToggleSelectionLock() {
        var items=ui.Elements.Where(e=>selected.Contains(e.Id)).ToList();if(items.Count==0)return;
        bool lockThem=items.Any(e=>!e.Locked);Change();SetLocked(items,lockThem);
        if(lockThem)selected.Clear();Draw();RefreshInspector();Log(lockThem?$"Locked {items.Count} item(s). Unlock them from Layers.":$"Unlocked {items.Count} item(s).");
    }
}
public partial class MainWindow
{
    internal void VerifyChrome(string output) {
        project=new Project();project.Manifest.Snap=false;ui=project.Screens[0];history.Clear();selected.Clear();
        ui.Elements=[new(){Id="a",Type="button",Bounds=new(){X=10,Y=10,Width=40,Height=20},Events=new(){["click"]=new(){Client=new(){Actions=[new(){Type="open_ui",Value="main"}]}}}},
                     new(){Id="b",Type="label",Bounds=new(){X=80,Y=10,Width=40,Height=20}}];
        RefreshAll();Element E(string id)=>ui.Elements.Single(x=>x.Id==id);
        // Gesture names: numpad and aliased keys collapse to one form, and display text is readable.
        if(Normalize("Ctrl+Add")!="Ctrl+OemPlus" || Normalize("Ctrl+NumPad0")!="Ctrl+D0" || Normalize("Shift+Ctrl+Oem6")!="Ctrl+Shift+OemCloseBrackets" || DisplayGesture("Ctrl+OemCloseBrackets")!="Ctrl+]")throw new Exception("Gesture normalization failed");
        // Menus show the current shortcut; rebinding updates them and the key runs the command.
        var duplicateItem=commandMenuItems["edit.duplicate"][0];if(duplicateItem.InputGestureText!="Ctrl+D")throw new Exception("Menu shortcut hint missing: "+duplicateItem.InputGestureText);
        var saved=shortcutOverrides;shortcutOverrides=new(){["edit.duplicate"]="F9"};RefreshShortcutHints();
        if(duplicateItem.InputGestureText!="F9")throw new Exception("Rebinding did not update the menu");
        selected.Add("b");Surface.Focus();UpdateLayout();var source=PresentationSource.FromVisual(this)!;
        Surface.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,source,0,System.Windows.Input.Key.F9){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent});
        if(ui.Elements.Count!=3)throw new Exception("Rebound shortcut did not run (focus: "+System.Windows.Input.Keyboard.FocusedElement+", modifiers: "+System.Windows.Input.Keyboard.Modifiers+")");
        shortcutOverrides=saved;RefreshShortcutHints();history.Undo();
        // Screen ID rename updates links and the Main screen; variables are validated.
        RenameScreen("cockpit");if(ui.Id!="cockpit" || project.Manifest.DefaultUi!="cockpit" || E("a").Events["click"].Client.Actions[0].Value!="cockpit")throw new Exception("Screen rename did not update references");
        bool refused=false;try{RenameScreen("Bad Id");}catch(InvalidOperationException){refused=true;}if(!refused)throw new Exception("Invalid screen ID accepted");
        SetScreenVariables("fuel=10;mode=idle");if(ui.Variables["mode"]!="idle")throw new Exception("Variables not applied");
        // Locked items can't be hit, box-selected or nudged; unlocking restores them.
        selected.Clear();selected.Add("a");ToggleSelectionLock();Draw();
        if(!E("a").Locked || Surface.Children.OfType<Border>().Single(x=>Equals(x.Tag,"a")).IsHitTestVisible)throw new Exception("Lock did not make the item click-through");
        BeginMarquee(new Point(0,0),System.Windows.Input.ModifierKeys.None);UpdateMarquee(new Point(300,100));EndCanvasGesture();
        if(selected.Contains("a") || !selected.Contains("b"))throw new Exception("Box selection picked a locked item");
        // Select all skips locked items.
        selected.Clear();SelectAll();if(!selected.SetEquals(new[]{"b"}))throw new Exception("Select all included a locked item or missed one: "+string.Join(",",selected));
        // A press on the dark area around the canvas starts a selection box; clicks on the canvas itself don't go through this path.
        EndCanvasGesture();selected.Clear();UpdateLayout();
        CanvasScroll.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,System.Windows.Input.MouseButton.Left){RoutedEvent=System.Windows.Input.Mouse.PreviewMouseDownEvent});
        if(marqueeStart==null)throw new Exception("Pressing outside the canvas did not start a selection box");
        marqueeStart=new Point(-40,-40);UpdateMarquee(new Point(2000,2000));EndCanvasGesture();
        if(!selected.SetEquals(new[]{"b"}))throw new Exception("Sweeping from outside the canvas did not select everything unlocked: "+string.Join(",",selected));
        Surface.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,System.Windows.Input.MouseButton.Left){RoutedEvent=System.Windows.Input.Mouse.PreviewMouseDownEvent,Source=Surface});
        if(marqueeStart!=null)throw new Exception("A press on the canvas was treated as an outside-canvas press");EndCanvasGesture();
        // Zoom label follows the view zoom.
        SetViewScale(2);if(!zoomLabel.Text.StartsWith("Zoom 200%") || !zoomLabel.Text.Contains("= 4 screen"))throw new Exception("Zoom label is stale: "+zoomLabel.Text);ZoomActual();
        // Screen settings button shows the merged settings.
        ShowScreenSettings();if(!InspectorItems().OfType<DockPanel>().Any(d=>d.Children.OfType<TextBlock>().Any(t=>t.Text=="Screen ID")))throw new Exception("Screen settings not shown in Properties");
        ShowShortcutSettings(output+".shortcuts.png");
        dirty=false;System.IO.File.WriteAllText(output,"PASS: gesture normalization, menu shortcut hints, rebinding and key dispatch, screen rename references, variables, lock click-through and marquee skip, select all, box selection from outside the canvas, live zoom label, merged screen settings");Application.Current.Shutdown();
    }
}
