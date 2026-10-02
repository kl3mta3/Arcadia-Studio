using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wysicraft.Models;
namespace Wysicraft.Designer;

// Every menu command has an ID, a default shortcut and an optional user override saved per Windows user.
// Gestures are stored as "Ctrl+Shift+G" using WPF Key names; numpad variants match their main-keyboard keys.
public partial class MainWindow
{
    sealed record Command(string Id,string Category,string Name,string DefaultGesture,Action Run,bool WorksWhileTyping=false,Func<bool>? Checked=null);
    readonly List<Command> commands=new();
    Dictionary<string,string> shortcutOverrides=new();
    readonly Dictionary<string,List<MenuItem>> commandMenuItems=new();
    static string ShortcutsPath => Wysicraft.Core.AppFolders.Path("keybindings.json");

    void RegisterCommands() {
        void Add(string id,string category,string name,string gesture,Action run,bool typing=false,Func<bool>? isChecked=null)=>commands.Add(new(id,category,name,gesture,CreatorCommand(id,run),typing,isChecked));
        Add("file.new","File","New project","Ctrl+N",NewProject,true);
        Add("file.open","File","Open project or pack…","Ctrl+O",OpenProject,true);
        Add("file.recover","File","Recover unsaved project…","",RecoverProject);
        Add("file.save","File","Save","Ctrl+S",()=>Save(saveAs:false),true);
        Add("file.saveAs","File","Save as…","Ctrl+Shift+S",()=>Save(saveAs:true),true);
        Add("file.looseCopy","File","Save a copy as loose files…","",SaveLooseCopy);
        Add("file.export","File","Export…","Ctrl+E",Export);
        Add("file.exportKube","File","Export for KubeJS…","",ExportKube);
        Add("file.publish","File","Publish to Arcadia…","",PublishToArcadia);
        Add("file.publishItch","File","Publish to itch.io…","",PublishToItch);
        Add("file.exit","File","Exit","",Close);
        Add("edit.undo","Edit","Undo","Ctrl+Z",history.Undo);
        Add("edit.redo","Edit","Redo","Ctrl+Y",history.Redo);
        Add("edit.cut","Edit","Cut","Ctrl+X",()=>{Copy();Delete();});
        Add("edit.copy","Edit","Copy","Ctrl+C",Copy);
        Add("edit.paste","Edit","Paste","Ctrl+V",Paste);
        Add("edit.duplicate","Edit","Duplicate","Ctrl+D",Duplicate);
        Add("edit.delete","Edit","Delete","Delete",Delete);
        Add("edit.selectAll","Edit","Select all","Ctrl+A",SelectAll);
        Add("edit.group","Edit","Group","Ctrl+G",GroupSelected);
        Add("edit.ungroup","Edit","Ungroup","Ctrl+Shift+G",UngroupSelected);
        Add("edit.newGroup","Edit","New empty group","",NewLayerGroup);
        Add("edit.attach","Edit","Attach to panel","Ctrl+Shift+A",()=>AttachSelection(null));
        Add("edit.detach","Edit","Detach from panel","Ctrl+Shift+D",DetachSelection);
        Add("edit.isolate","Edit","Isolate group","",IsolateSelectedGroup);
        Add("edit.bringForward","Edit","Bring forward","Ctrl+OemCloseBrackets",()=>MoveLayers(1));
        Add("edit.sendBackward","Edit","Send backward","Ctrl+OemOpenBrackets",()=>MoveLayers(-1));
        Add("edit.bringToFront","Edit","Bring to front","Ctrl+Shift+OemCloseBrackets",BringToFront);
        Add("edit.sendToBack","Edit","Send to back","Ctrl+Shift+OemOpenBrackets",SendToBack);
        Add("edit.rename","Edit","Rename…","F2",RenameSelected);
        Add("edit.toggleLock","Edit","Lock / unlock selection","Ctrl+L",ToggleSelectionLock);
        Add("view.zoomIn","View","Zoom in","Ctrl+OemPlus",ZoomIn);
        Add("view.zoomOut","View","Zoom out","Ctrl+OemMinus",ZoomOut);
        Add("view.zoomActual","View","Actual size","Ctrl+D0",ZoomActual);
        Add("view.zoomFit","View","Fit screen in window","Ctrl+D9",ZoomFit);
        Add("view.grid","View","Show grid","",()=>{grid=!grid;Draw();},isChecked:()=>grid);
        Add("view.snap","View","Snap to grid","",()=>{Change();project.Manifest.Snap=!project.Manifest.Snap;Draw();},isChecked:()=>project.Manifest.Snap);
        Add("view.resetLayout","View","Reset panel layout","",ResetDockLayout);
        Add("view.shortcuts","View","Keyboard shortcuts…","",ShowShortcutSettings);
        Add("view.askLockChildren","View","Ask again before locking items inside panels","",ResetDontAskAgain);
        Add("project.preview","Project","Preview","F5",Preview,true);
        Add("project.test","Project","Test in Minecraft","Ctrl+F5",TestMinecraft,true);
        Add("project.validate","Project","Validate","F7",Validate,true);
        Add("project.screen","Project","Screen settings","",ShowScreenSettings);
        Add("project.settings","Project","Project settings…","",Settings);
        Add("project.importTexture","Project","Import texture…","",ImportTexture);
        Add("project.pixelEditor","Project","Pixel editor…","",()=>PixelEditorForAsset(true));
        Add("project.musicMaker","Project","Music maker…","",()=>OpenMusicMaker());
        Add("project.soundEffects","Project","Sound effect maker…","",()=>OpenSoundEffectMaker());
        Add("project.importAudio","Project","Import audio as 8-bit…","",()=>ImportAudioAs8Bit());
        Add("project.spriteSheet","Project","Sprite sheet editor for the selection…","",()=>{var e=ui.Elements.FirstOrDefault(x=>selected.Contains(x.Id));if(e==null||e.Type!="sprite")throw new InvalidOperationException("Select a Sprite control first.");ShowSpriteSheetEditor(e);});
        Add("project.mcp","Project","MCP server (AI assistants)…","",ShowMcpPanel);
        Add("advanced.inputs","Advanced","Inputs (keys and gamepads)…","",ShowInputsWindow);
        Add("advanced.animations","Advanced","Animations…","",ShowAnimationsWindow);
        Add("advanced.layers","Advanced","Collision layers…","",ShowCollisionLayers);
        Add("advanced.stateGraphs","Advanced","State graphs…","",ShowStateGraphsWindow);
        Add("advanced.shaders","Advanced","Shaders…","",ShowShadersWindow);
        Add("advanced.leaderboard.new","Advanced","Create leaderboard","",()=>CreateLeaderboard());
        Add("advanced.leaderboard.open","Advanced","Open leaderboard…","",ChooseLeaderboard);
        Add("advanced.leaderboard.import","Advanced","Import leaderboard…","",()=>ImportLeaderboard());
        Add("advanced.inputCreator","Advanced","Input creator (with the assistant)…","",ShowInputCreator);
        Add("advanced.particles","Advanced","Particles…","",ShowParticlesWindow);
        Add("advanced.particleMaker","Advanced","Particle maker…","",()=>OpenParticleMaker());
        Add("advanced.collider","Advanced","Collider editor for the selection…","",()=>{var e=ui.Elements.FirstOrDefault(x=>selected.Contains(x.Id));if(e==null||!(e.Type=="collider"||e.Body.Length>0))throw new InvalidOperationException("Select a collider, or a control with a physics body, first.");if(e.Collider!="polygon"){Change();e.Collider="polygon";if(e.ColliderPoints.Count<3)e.ColliderPoints=DefaultPolygon(e);}ShowColliderEditor(e);});
        Add("advanced.tilemap","Advanced","Tile painter for the selection…","",()=>{var e=ui.Elements.FirstOrDefault(x=>selected.Contains(x.Id));if(e==null||e.Type!="tilemap")throw new InvalidOperationException("Select a Tilemap control first.");ShowTilemapEditor(e);});
        Add("advanced.createAudio","Advanced","Create audio from text…","",()=>ShowCreateAudio());
        Add("advanced.createText","Advanced","Create text from audio…","",ShowCreateText);
        Add("advanced.speechSettings","Advanced","Speech settings…","",ShowSpeechSettings);
        Add("advanced.toolbox","Advanced","Show / hide the Advanced toolbox","",ToggleAdvancedToolbox);
        Add("help.manual","Help","User manual","F1",OpenManual,true);
        Add("help.scriptApi","Help","Script API and snippets","",ShowScriptApi);
        Add("help.updates","Help","Check for updates…","",CheckForUpdatesNow);
        Add("help.autoUpdates","Help","Check for updates automatically","",ToggleAutoUpdates,isChecked:()=>Prefs().AutoCheckUpdates);
        Add("help.about","Help","About Arcadia Studio","",ShowAbout);
        LoadShortcuts();
    }
    Command Cmd(string id)=>commands.First(c=>c.Id==id);
    string GestureFor(string id)=>shortcutOverrides.TryGetValue(id,out var g)?g:Cmd(id).DefaultGesture;

    // A menu item bound to a command: runs it and shows its current shortcut.
    MenuItem CommandItem(string id,string? header=null) {
        var command=Cmd(id);
        var item=new MenuItem {Header=header ?? command.Name,InputGestureText=DisplayGesture(GestureFor(id)),IsCheckable=command.Checked!=null};
        if(command.Checked!=null)item.Loaded+=(_,_)=>item.IsChecked=command.Checked();
        item.Click+=(_,_)=>Guard(command.Run);
        if(!commandMenuItems.TryGetValue(id,out var list))commandMenuItems[id]=list=new();
        list.Add(item);return item;
    }
    // Refreshes checkmarks of toggle commands each time a menu opens.
    void TrackChecks(MenuItem menu)=>menu.SubmenuOpened+=(_,_)=>{foreach(var (id,items) in commandMenuItems)if(Cmd(id).Checked is Func<bool> check)foreach(var item in items)item.IsChecked=check();};
    void RefreshShortcutHints() {
        foreach(var (id,items) in commandMenuItems)foreach(var item in items)item.InputGestureText=DisplayGesture(GestureFor(id));
        RefreshToolbarTooltips();
    }

    // Runs the command bound to this key press. Commands that aren't marked WorksWhileTyping leave text fields alone.
    bool TryRunShortcut(KeyEventArgs e) {
        string? gesture=GestureFromEvent(e);if(gesture==null)return false;
        var command=commands.FirstOrDefault(c=>Normalize(GestureFor(c.Id))==gesture);if(command==null)return false;
        // Lists with their own keyboard use (Assets, Items) count as typing, so Delete there never deletes canvas items.
        bool typing=e.OriginalSource is TextBox or ComboBox || Keyboard.FocusedElement is TextBox || assetList.IsKeyboardFocusWithin || itemList.IsKeyboardFocusWithin;
        if(typing && !command.WorksWhileTyping)return false;
        Guard(command.Run);return true;
    }
    static string? GestureFromEvent(KeyEventArgs e) {
        var key=e.Key==Key.System?e.SystemKey:e.Key;
        if(key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None or Key.ImeProcessed)return null;
        return Normalize(Compose(Keyboard.Modifiers,key));
    }
    static string Compose(ModifierKeys modifiers,Key key) {
        var parts=new List<string>();
        if(modifiers.HasFlag(ModifierKeys.Control))parts.Add("Ctrl");
        if(modifiers.HasFlag(ModifierKeys.Shift))parts.Add("Shift");
        if(modifiers.HasFlag(ModifierKeys.Alt))parts.Add("Alt");
        parts.Add(key.ToString());return string.Join("+",parts);
    }
    // Canonical form: modifiers in Ctrl+Shift+Alt order and one fixed name per key. WPF gives some keys two names
    // (Oem6/OemCloseBrackets, Return/Enter), and numpad keys behave like their main-keyboard equivalents.
    static string Normalize(string gesture) {
        if(gesture.Length==0)return "";
        var parts=gesture.Split('+');var modifiers=ModifierKeys.None;
        foreach(var part in parts[..^1])modifiers|=part switch {"Ctrl"=>ModifierKeys.Control,"Shift"=>ModifierKeys.Shift,"Alt"=>ModifierKeys.Alt,_=>ModifierKeys.None};
        if(!Enum.TryParse<Key>(parts[^1],out var key))return gesture;
        var mods=new List<string>();
        if(modifiers.HasFlag(ModifierKeys.Control))mods.Add("Ctrl");if(modifiers.HasFlag(ModifierKeys.Shift))mods.Add("Shift");if(modifiers.HasFlag(ModifierKeys.Alt))mods.Add("Alt");
        mods.Add(CanonicalKey(key));return string.Join("+",mods);
    }
    static string CanonicalKey(Key key) {
        if(key>=Key.NumPad0 && key<=Key.NumPad9)return "D"+(key-Key.NumPad0);
        return key switch {
            Key.Add=>"OemPlus",Key.Subtract=>"OemMinus",Key.Oem4=>"OemOpenBrackets",Key.Oem6=>"OemCloseBrackets",Key.Oem7=>"OemQuotes",
            Key.Oem1=>"OemSemicolon",Key.Oem2=>"OemQuestion",Key.Oem3=>"OemTilde",Key.Oem5=>"OemPipe",Key.Return=>"Return",Key.Next=>"Next",Key.Prior=>"Prior",
            Key.Capital=>"CapsLock",_=>key.ToString()};
    }
    static string DisplayGesture(string gesture) {
        if(gesture.Length==0)return "";
        var parts=gesture.Split('+').ToList();
        parts[^1]=parts[^1] switch {
            "OemPlus"=>"+","OemMinus"=>"-","OemOpenBrackets"=>"[","OemCloseBrackets"=>"]","OemQuotes"=>"'","OemComma"=>",","OemPeriod"=>".",
            "OemQuestion"=>"/","OemSemicolon"=>";","OemPipe"=>"\\","OemTilde"=>"`","Return"=>"Enter","Back"=>"Backspace","Next"=>"Page Down","Prior"=>"Page Up",
            var k when k.Length==2 && k[0]=='D' && char.IsDigit(k[1])=>k[1].ToString(),var k=>k};
        return string.Join("+",parts);
    }
    // Keys the canvas already uses: arrows nudge (Shift for 10 px), Escape leaves isolation, Tab moves focus.
    static bool Reserved(string gesture) {
        var parts=gesture.Split('+');string key=parts[^1];bool onlyShift=parts.Length==1 || parts.Length==2 && parts[0]=="Shift";
        return key is "Escape" or "Tab" || onlyShift && key is "Left" or "Right" or "Up" or "Down";
    }

    void LoadShortcuts() {
        try {
            if(!File.Exists(ShortcutsPath))return;
            var saved=Json.Read<Dictionary<string,string>>(File.ReadAllText(ShortcutsPath));
            shortcutOverrides=saved.Where(p=>commands.Any(c=>c.Id==p.Key)).ToDictionary(p=>p.Key,p=>Normalize(p.Value));
        } catch(Exception ex) {shortcutOverrides=new();Log("Keyboard shortcuts could not be loaded; using defaults. "+ex.Message);}
    }
    void SaveShortcuts() {
        Directory.CreateDirectory(Path.GetDirectoryName(ShortcutsPath)!);
        File.WriteAllText(ShortcutsPath+".tmp",Json.Write(shortcutOverrides));File.Move(ShortcutsPath+".tmp",ShortcutsPath,true);
    }

    void ShowShortcutSettings()=>ShowShortcutSettings(null);
    // capture: render the window to a PNG and close it (used to check the layout without a person clicking).
    void ShowShortcutSettings(string? capture) {
        var working=commands.ToDictionary(c=>c.Id,c=>Normalize(GestureFor(c.Id)));
        var window=new Window {Title="Keyboard shortcuts",Owner=this,Width=640,Height=600,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var layout=new DockPanel {Margin=new Thickness(12)};window.Content=layout;
        var top=new DockPanel {Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(top,Dock.Top);layout.Children.Add(top);
        top.Children.Add(new TextBlock {Text="Search",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)});
        var search=new TextBox();top.Children.Add(search);
        var help=new TextBlock {Text="Click a shortcut box, then press the new key combination. Arrow keys, Escape and Tab are kept for the canvas. Changes apply when you click Save.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8),Foreground=System.Windows.Media.Brushes.LightGray};
        DockPanel.SetDock(help,Dock.Top);layout.Children.Add(help);
        var status=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0),Foreground=System.Windows.Media.Brushes.Khaki};
        var buttons=new DockPanel {Margin=new Thickness(0,8,0,0)};DockPanel.SetDock(buttons,Dock.Bottom);layout.Children.Add(buttons);
        DockPanel.SetDock(status,Dock.Bottom);layout.Children.Add(status);
        var resetAll=new Button {Content="Reset all to defaults"};buttons.Children.Add(resetAll);
        var right=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};buttons.Children.Add(right);
        var save=new Button {Content="Save",IsDefault=true,MinWidth=80};var cancel=new Button {Content="Cancel",IsCancel=true,MinWidth=80};right.Children.Add(save);right.Children.Add(cancel);
        var rows=new StackPanel();layout.Children.Add(new ScrollViewer {Content=rows,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var boxes=new Dictionary<string,TextBox>();
        void Show(string id)=>boxes[id].Text=working[id].Length==0?"(none)":DisplayGesture(working[id]);
        void Build() {
            rows.Children.Clear();boxes.Clear();string filter=search.Text.Trim();
            foreach(var category in commands.Select(c=>c.Category).Distinct()) {
                var matches=commands.Where(c=>c.Category==category && (filter.Length==0 || c.Name.Contains(filter,StringComparison.OrdinalIgnoreCase) || DisplayGesture(working[c.Id]).Contains(filter,StringComparison.OrdinalIgnoreCase))).ToList();
                if(matches.Count==0)continue;
                rows.Children.Add(new TextBlock {Text=category.ToUpperInvariant(),FontWeight=FontWeights.SemiBold,Foreground=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4F,0xC1,0xFF)),Margin=new Thickness(0,10,0,4)});
                foreach(var command in matches) {
                    var row=new DockPanel {Margin=new Thickness(0,1,0,1)};
                    var reset=new Button {Content="Reset",ToolTip="Default: "+(command.DefaultGesture.Length==0?"none":DisplayGesture(command.DefaultGesture))};DockPanel.SetDock(reset,Dock.Right);
                    var clear=new Button {Content="Clear"};DockPanel.SetDock(clear,Dock.Right);
                    var box=new TextBox {Width=170,IsReadOnly=true,IsReadOnlyCaretVisible=false,Cursor=Cursors.Hand,ToolTip="Click, then press keys"};DockPanel.SetDock(box,Dock.Right);
                    row.Children.Add(reset);row.Children.Add(clear);row.Children.Add(box);
                    row.Children.Add(new TextBlock {Text=command.Name,VerticalAlignment=VerticalAlignment.Center});
                    rows.Children.Add(row);boxes[command.Id]=box;Show(command.Id);
                    string id=command.Id;
                    box.GotKeyboardFocus+=(_,_)=>{box.Text="Press keys…";status.Text="";};
                    box.LostKeyboardFocus+=(_,_)=>Show(id);
                    box.PreviewKeyDown+=(_,e)=>{
                        e.Handled=true;string? gesture=GestureFromEvent(e);if(gesture==null)return;
                        if(gesture=="Escape"){Keyboard.ClearFocus();return;}
                        if(Reserved(gesture)){status.Text=DisplayGesture(gesture)+" is kept for the canvas. Choose another combination.";return;}
                        var other=commands.FirstOrDefault(c=>c.Id!=id && working[c.Id]==gesture);
                        if(other!=null && MessageBox.Show(window,$"{DisplayGesture(gesture)} is already used by \"{other.Name}\".\n\nUse it for \"{command.Name}\" instead? \"{other.Name}\" will have no shortcut.","Shortcut in use",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes){Keyboard.ClearFocus();return;}
                        if(other!=null){working[other.Id]="";if(boxes.ContainsKey(other.Id))Show(other.Id);}
                        working[id]=gesture;status.Text=$"{command.Name}: {DisplayGesture(gesture)}";Keyboard.ClearFocus();
                    };
                    clear.Click+=(_,_)=>{working[id]="";Show(id);};
                    reset.Click+=(_,_)=>{
                        string gesture=Normalize(command.DefaultGesture);
                        var other=gesture.Length>0?commands.FirstOrDefault(c=>c.Id!=id && working[c.Id]==gesture):null;
                        if(other!=null){working[other.Id]="";if(boxes.ContainsKey(other.Id))Show(other.Id);status.Text=$"\"{other.Name}\" no longer has a shortcut.";}
                        working[id]=gesture;Show(id);
                    };
                }
            }
        }
        search.TextChanged+=(_,_)=>Build();Build();
        resetAll.Click+=(_,_)=>{foreach(var c in commands)working[c.Id]=Normalize(c.DefaultGesture);Build();status.Text="All shortcuts reset to defaults. Click Save to keep this.";};
        save.Click+=(_,_)=>Guard(()=>{
            shortcutOverrides=commands.Where(c=>working[c.Id]!=Normalize(c.DefaultGesture)).ToDictionary(c=>c.Id,c=>working[c.Id]);
            SaveShortcuts();RefreshShortcutHints();window.DialogResult=true;Log("Keyboard shortcuts saved.");
        });
        if(capture!=null) {
            window.Show();window.UpdateLayout();FlushUi();
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(window);
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using(var file=File.Create(capture))encoder.Save(file);
            window.Close();return;
        }
        window.ShowDialog();
    }
}
