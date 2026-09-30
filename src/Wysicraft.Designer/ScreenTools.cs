using System.Windows;
using System.Windows.Controls;
using Wysicraft.Core;
using Wysicraft.Models;
namespace Wysicraft.Designer;
public partial class MainWindow
{
    string? componentReturnScreen;
    void OpenComponentSource(string id){if(!ui.IsComponent)componentReturnScreen=ui.Id;ShowScreen(project.Screens.Single(s=>s.Id==id&&s.IsComponent));}
    void BackToScreen(object sender,RoutedEventArgs e)=>ShowScreen(project.Screens.FirstOrDefault(s=>s.Id==componentReturnScreen&&!s.IsComponent)??project.Screens.First(s=>!s.IsComponent));
    // Shared by the dropdown, component source editing and Back to screen so every path refreshes the same editor state.
    void ShowScreen(UiDefinition screen) {
        EndCanvasGesture();ui=screen;selected.Clear();
        refreshing=true;Screens.SelectedItem=ui.IsComponent||ui.IsLeaderboard?null:ui.Id;refreshing=false;
        RefreshSourceContext();RefreshComponents();Draw();RefreshInspector();
    }
    void RefreshSourceContext(){
        ComponentSourceBar.Visibility=ui.IsComponent||ui.IsLeaderboard?Visibility.Visible:Visibility.Collapsed;
        ComponentSourceLabel.Text=ui.IsLeaderboard?"Editing leaderboard page: "+ui.Id:"Editing component: "+ui.Title;
        DeleteScreenButton.IsEnabled=!ui.IsComponent&&!ui.IsLeaderboard&&project.Screens.Count(s=>!s.IsComponent)>1;
        RefreshBoardBar();
        // The toolbox follows what's open: a leaderboard page has its own.
        if(toolboxTarget!=ToolboxKey()){toolboxTarget=ToolboxKey();RebuildToolbox();}
    }
    void DeleteScreenClick(object sender,RoutedEventArgs e)=>Guard(DeleteCurrentScreen);
    void DeleteCurrentScreen() {
        if(ui.IsComponent)throw new InvalidOperationException("Use Delete source in the Components panel.");
        var remaining=project.Screens.Where(s=>!s.IsComponent&&s!=ui).Select(s=>s.Id).ToArray();if(remaining.Length==0)throw new InvalidOperationException("Keep at least one screen.");
        var window=new Window {Title="Delete screen",Owner=this,Width=430,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize};
        var panel=new StackPanel {Margin=new Thickness(16)};window.Content=panel;
        panel.Children.Add(new TextBlock {Text="Delete '"+ui.Id+"'? Choose the replacement for built-in screen links and, if needed, the Main screen. This can be undone. Literal screen IDs in scripts need updating separately.",TextWrapping=TextWrapping.Wrap});
        var replacement=new ComboBox {ItemsSource=remaining,SelectedItem=remaining.Contains(project.Manifest.DefaultUi)?project.Manifest.DefaultUi:remaining[0]};panel.Children.Add(replacement);
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(buttons);
        var cancel=new Button {Content="Cancel",IsCancel=true};buttons.Children.Add(cancel);var delete=new Button {Content="Delete screen"};buttons.Children.Add(delete);
        delete.Click+=(_,_)=>Guard(()=>{DeleteScreenTo((string)replacement.SelectedItem);window.DialogResult=true;});window.ShowDialog();
    }
    void DeleteScreenTo(string replacement){SaveScriptText();var candidate=Json.CloneProject(project);ScreenEdits.Delete(candidate,ui.Id,replacement);Change();project=candidate;ui=project.Screens.Single(s=>s.Id==replacement);selected.Clear();RefreshAll();}
}
public partial class MainWindow
{
    // Every screen setting in one place (Properties, with nothing selected).
    void BuildScreenSettings() {
        Heading(Properties,"Screen");
        CommitField("Screen ID",ui.Id,RenameScreen,"Lowercase letters, numbers and underscores. Built-in links, the Main screen, row templates and components are updated. IDs written inside scripts must be changed by hand.");
        Field(Properties,"Title",ui,"Title");
        Field(Properties,"Width",ui.Size,"Width");
        Field(Properties,"Height",ui.Size,"Height");
        if(!ui.IsComponent) {
            var main=new CheckBox {Content="Main screen (opened by /"+project.Manifest.Id+".open)",IsChecked=project.Manifest.DefaultUi==ui.Id};
            main.IsEnabled=main.IsChecked!=true;
            main.ToolTip=main.IsEnabled?"Make this the screen the project opens with. The previous Main screen stops being Main.":"This is the Main screen. To change it, open another screen and tick Main screen there.";
            main.Click+=(_,_)=>Guard(()=>{Change();project.Manifest.DefaultUi=ui.Id;RefreshInspector();});
            Properties.Children.Add(main);
        }
        CommitField("Variables",string.Join(";",ui.Variables.Select(v=>v.Key+"="+v.Value)),SetScreenVariables,"Screen variables as name=value;name=value. Scripts and conditions read them.");
        Heading(Properties,"Display");
        Field(Properties,"Responsive layout",ui,"Responsive");
        Field(Properties,"Show frame / title",ui,"ShowFrame");
        Field(Properties,"Dim game behind UI",ui,"DimBackground");
        Field(Properties,"Fit to viewport",ui,"FitToScreen");
        Field(Properties,"Clip to screen",ui,"ClipToScreen");
        Field(Properties,"Tick interval (ms)",ui,"TickInterval");
        Field(Properties,"Key repeat (ms)",ui,"KeyRepeat");
        if(AdvancedAllowed){Field(Properties,"Gravity (web & desktop)",ui,"Gravity");var anim=new Button{Content=$"Animations ({ui.Animations.Count})…",HorizontalAlignment=HorizontalAlignment.Left};anim.Click+=(_,_)=>Guard(ShowAnimationsWindow);Properties.Children.Add(anim);
            var states=new Button{Content=$"State graphs ({ui.StateGraphs.Count})…",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,4,0,0)};states.Click+=(_,_)=>Guard(ShowStateGraphsWindow);Properties.Children.Add(states);
            // The shader runs over what the WebGL2 batch drew (tilemap, particles, plain sprites); web only.
            var shaders=new List<(string,string)>{("","(no shader)")};shaders.AddRange(project.Manifest.Shaders.Select(s=>(s.Id,s.Id)));
            Choice(Properties,"Shader (web)",ui.Shader,shaders,v=>ui.Shader=v,"A fragment shader run over the batched layer: the tilemap, particles and plain sprites. Make them in Advanced → Shaders.");
            var edit=new Button{Content=project.Manifest.Shaders.Count==0?"Shaders…":$"Edit shaders ({project.Manifest.Shaders.Count})…",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,4,0,0)};edit.Click+=(_,_)=>Guard(ShowShadersWindow);Properties.Children.Add(edit);}
        Properties.Children.Add(new TextBlock {Text="Responsive layout moves controls by their anchors when the game window size differs. Fit to viewport scales a non-responsive screen to fit instead. Clip to screen hides anything outside the screen's own area, for games that scroll things in from off screen. Tick interval runs the screen's Tick event every so many milliseconds while it's open (0 = off, 50 or more), for timers, animations and games. Key repeat is how often a held key sends the Key event again (0 = only once per press); presses never pile up. For a transparent screen, turn off the frame and dimming, and add a Panel wherever you want a background.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4),Foreground=System.Windows.Media.Brushes.LightGray});
    }
    // A text field applied on Enter or when it loses focus (not on every keystroke), for values that need validation as a whole.
    void CommitField(string label,string value,Action<string> apply,string tip) {
        var dock=new DockPanel();dock.Children.Add(new TextBlock {Text=label,Width=FieldLabelWidth,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(4)});
        var box=new TextBox {Text=value,ToolTip=tip};dock.Children.Add(box);Properties.Children.Add(dock);
        bool applying=false;
        void Commit() {
            if(applying || box.Text==value)return;applying=true;
            try {apply(box.Text.Trim());value=box.Text;} catch(Exception ex) {Log(ex.Message);MessageBox.Show(this,ex.Message,"Arcadia Studio",MessageBoxButton.OK,MessageBoxImage.Warning);box.Text=value;}
            finally {applying=false;}
        }
        box.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Return){Commit();e.Handled=true;}};
        box.LostKeyboardFocus+=(_,_)=>Commit();
    }
    void RenameScreen(string id) {
        if(id==ui.Id)return;
        if(!Wysicraft.Core.Validation.Id(id) || project.Screens.Any(s=>s!=ui && s.Id==id))throw new InvalidOperationException("Use a unique screen ID: lowercase letters, numbers and underscores, starting with a letter.");
        Change();string old=ui.Id;
        if(project.Manifest.DefaultUi==old)project.Manifest.DefaultUi=id;
        foreach(var screen in project.Screens)
            foreach(var ev in screen.Events.Values.Concat(screen.Elements.SelectMany(e=>e.Events.Values)))
                foreach(var action in ev.Client.Actions.Concat(ev.Server.Actions))if(action.Type=="open_ui" && action.Value==old)action.Value=id;
        foreach(var e in project.Screens.SelectMany(s=>s.Elements))if(e.RowTemplate==old)e.RowTemplate=id;
        foreach(var instance in project.Screens.SelectMany(s=>s.ComponentInstances))if(instance.Source==old)instance.Source=id;
        if(componentReturnScreen==old)componentReturnScreen=id;
        ui.Id=id;RefreshAll();Log($"Renamed screen {old} to {id}.");
    }
    void SetScreenVariables(string text) {
        var variables=new Dictionary<string,string>();
        foreach(var pair in text.Split(';',StringSplitOptions.RemoveEmptyEntries)) {
            var parts=pair.Split('=',2);string name=parts[0].Trim();
            if(!Wysicraft.Core.Validation.Variable(name))throw new InvalidOperationException("Invalid variable name: "+name);
            variables[name]=parts.Length>1?parts[1]:"";
        }
        Change();ui.Variables=variables;Draw();
    }
}
