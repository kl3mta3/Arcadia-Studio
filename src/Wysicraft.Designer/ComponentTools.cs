using System.Windows;
using System.Windows.Controls;
using Wysicraft.Core;
using Wysicraft.Models;
using Validation=Wysicraft.Core.Validation;
namespace Wysicraft.Designer;

public partial class MainWindow
{
    readonly ListBox componentList=new(){DisplayMemberPath="Title"};
    bool componentsReady;
    void InitializeComponents() {
        var tools=new WrapPanel();DockPanel.SetDock(tools,Dock.Top);ComponentsHost.Children.Add(tools);
        void Button(string name,System.Action action){var b=new Button{Content=name};b.Click+=(_,_)=>Guard(action);tools.Children.Add(b);}
        Button("Save selection",CaptureComponent);Button("Place instance",PlaceComponent);Button("Edit source",EditComponentSource);Button("Update all instances",UpdateAllComponents);Button("Delete source",DeleteComponentSource);
        var starters=new StackPanel();DockPanel.SetDock(starters,Dock.Top);ComponentsHost.Children.Add(starters);
        starters.Children.Add(new TextBlock {Text="Built-in starters",Margin=new Thickness(4)});
        var pick=new ComboBox {ItemsSource=ComponentStarters.All,DisplayMemberPath="Title",SelectedIndex=0};starters.Children.Add(pick);
        pick.ToolTip=ComponentStarters.All[0].Description;
        pick.SelectionChanged+=(_,_)=>pick.ToolTip=(pick.SelectedItem as ComponentStarters.Starter)?.Description??"";
        var addStarter=new Button {Content="Add to screen"};addStarter.Click+=(_,_)=>Guard(()=>{if(pick.SelectedItem is ComponentStarters.Starter starter)AddStarterToScreen(starter.Id);});starters.Children.Add(addStarter);
        var note=new TextBlock{Text="Select a source, then Place instance. Instance options are in Properties.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(6)};DockPanel.SetDock(note,Dock.Bottom);ComponentsHost.Children.Add(note);ComponentsHost.Children.Add(componentList);
        componentList.MouseDoubleClick+=(_,_)=>Guard(PlaceComponent);componentsReady=true;
    }
    void RefreshComponents() {if(!componentsReady)return;var id=(componentList.SelectedItem as UiDefinition)?.Id;componentList.ItemsSource=project.Screens.Where(s=>s.IsComponent).ToArray();componentList.SelectedItem=project.Screens.FirstOrDefault(s=>s.IsComponent&&s.Id==id);}
    string ChosenComponent()=>(componentList.SelectedItem as UiDefinition)?.Id??throw new InvalidOperationException("Choose a component source first.");
    void ComponentEdit(System.Action<Project,UiDefinition> action) {
        SaveScriptText();var candidate=Json.CloneProject(project);var screen=candidate.Screens.Single(s=>s.Id==ui.Id);action(candidate,screen);
        var errors=Validation.Errors(candidate);if(errors.Count>0)throw new InvalidOperationException(string.Join("\n",errors));
        Change();project=candidate;ui=screen;selected.IntersectWith(ui.Elements.Select(e=>e.Id));RefreshAll();
    }
    void CaptureComponent() {
        if(selected.Count==0)throw new InvalidOperationException("Select controls to save as a component.");
        var id=Prompt("Save reusable component","Component ID (lowercase letters, numbers, underscores)","component_badge");if(id==null)return;
        ComponentEdit((p,s)=>Components.Capture(p,s,selected,id));componentList.SelectedItem=project.Screens.Single(s=>s.Id==id);ShowDock("components");Log("Saved component source. Place instance creates a linked copy; the original selection is unchanged.");
    }
    void AddStarterToScreen(string key) {
        string source="",root="";
        ComponentEdit((p,s)=>{source=ComponentStarters.Add(p,key).Id;root=Components.Place(p,s,source,16,16).Root;});
        componentList.SelectedItem=project.Screens.Single(s=>s.Id==source);selected.Clear();selected.Add(root);Draw();RefreshInspector();
        Log("Added starter to screen. Its editable source is saved in Components.");
    }
    void PlaceComponent() {var id=ChosenComponent();string root="";ComponentEdit((p,s)=>root=Components.Place(p,s,id,16,16).Root);selected.Clear();selected.Add(root);Draw();RefreshInspector();}
    void EditComponentSource(){var id=ChosenComponent();OpenComponentSource(id);Log("Editing component source: "+id+". Update all instances when ready.");}
    void UpdateAllComponents(){var id=ChosenComponent();int count=0;ComponentEdit((p,_)=>{foreach(var s in p.Screens)foreach(var instance in s.ComponentInstances.Where(i=>i.Source==id)){Components.Update(p,s,instance);count++;}});Log($"Updated {count} instance(s); local property overrides preserved.");}
    void DeleteComponentSource(){var id=ChosenComponent();if(project.Screens.Any(s=>s.ComponentInstances.Any(i=>i.Source==id)))throw new InvalidOperationException("Detach or delete the linked instances before deleting their source.");if(ui.Id==id)ui=project.Screens.First(s=>!s.IsComponent);ComponentEdit((p,_)=>p.Screens.RemoveAll(s=>s.Id==id));}
    void BuildComponentFields(Element? element) {
        if(ui.IsComponent){Properties.Children.Add(new TextBlock{Text="COMPONENT SOURCE • Update instances from the Components panel after editing.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4)});return;}
        var instance=ui.ComponentInstances.FirstOrDefault(i=>i.Root==element?.Id || i.Ids.Values.Contains(element?.Id??""));if(instance==null)return;
        Heading(Properties,"Component: "+instance.Source);var tools=new WrapPanel();Properties.Children.Add(tools);
        void Button(string title,System.Action<Project,UiDefinition> action){var b=new Button{Content=title};b.Click+=(_,_)=>Guard(()=>ComponentEdit(action));tools.Children.Add(b);}
        Button("Update",(p,s)=>Components.Update(p,s,s.ComponentInstances.Single(i=>i.Root==instance.Root)));
        Button("Reset overrides",(p,s)=>Components.Update(p,s,s.ComponentInstances.Single(i=>i.Root==instance.Root),false));
        Button("Detach",(_,s)=>Components.Detach(s,instance.Root));
        var edit=new Button{Content="Edit source"};edit.Click+=(_,_)=>{OpenComponentSource(instance.Source);};tools.Children.Add(edit);
    }
    internal void VerifyComponents(string output) {
        project=new Project();ui=project.Screens[0];ui.Elements=[new(){Id="caption",Type="label",Text="Badge"}];history.Clear();selected.Clear();
        Components.Capture(project,ui,["caption"],"badge");RefreshAll();componentList.SelectedItem=project.Screens.Single(s=>s.Id=="badge");PlaceComponent();
        if(ui.ComponentInstances.Count!=1)throw new Exception("Component panel placement failed");
        history.Undo();if(ui.ComponentInstances.Count!=0)throw new Exception("Placement undo failed");history.Redo();if(ui.ComponentInstances.Count!=1)throw new Exception("Placement redo failed");
        var root=ui.ComponentInstances[0].Root;selected.Clear();selected.Add(root);RefreshInspector();
        if(!InspectorItems().OfType<WrapPanel>().Any())throw new Exception("Instance controls missing");
        componentList.SelectedItem=project.Screens.Single(s=>s.Id=="badge");project.Screens.Single(s=>s.Id=="badge").Elements[0].Text="Updated";UpdateAllComponents();
        if(ui.Elements.Single(e=>e.Id==ui.ComponentInstances[0].Ids["caption"]).Text!="Updated")throw new Exception("Update all failed");
        ShowDock("components");UpdateLayout();dirty=false;System.IO.File.WriteAllText(output,"PASS: component panel placement, undo/redo, instance Properties, update all and panel opening");Application.Current.Shutdown();
    }
    internal void VerifyComponentSelection(string output) {
        project=new Project();ui=project.Screens[0];selected.Clear();history.Clear();RefreshAll();
        AddStarterToScreen("window_header");
        if(ui.ComponentInstances.Count!=1 || project.Screens.Count!=2)throw new Exception("Starter was not added in one action");
        history.Undo();if(ui.ComponentInstances.Count!=0 || project.Screens.Count!=1)throw new Exception("Starter source and instance did not undo together");history.Redo();
        string member=ui.ComponentInstances[0].Ids["title"];
        void ClickSelectedRow() {
            Draw();UpdateLayout();var row=Layers.Items.Cast<ListBoxItem>().Single(r=>Equals(r.Tag,member));int count=selected.Count;
            row.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,System.Windows.Input.MouseButton.Left){RoutedEvent=System.Windows.Input.Mouse.PreviewMouseDownEvent});
            if(selected.Count!=count)throw new Exception("Mouse down lost the multi-selection needed for dragging");
            row.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,System.Windows.Input.MouseButton.Left){RoutedEvent=System.Windows.Input.Mouse.PreviewMouseUpEvent});
            if(selected.Count!=1 || !selected.Contains(member))throw new Exception("Layer click did not isolate the component member");
        }
        SelectCanvasElement(member,System.Windows.Input.ModifierKeys.None);ClickSelectedRow();
        selected.Clear();selected.UnionWith(ui.Elements.Select(e=>e.Id));UngroupSelected();ClickSelectedRow();
        selected.Clear();selected.UnionWith(ui.Elements.Select(e=>e.Id));SetLayerGroup("Manual group");ClickSelectedRow();
        dirty=false;System.IO.File.WriteAllText(output,"PASS: one-step starter and atomic undo/redo; component, ungrouped and manually grouped layer clicks select one member while mouse-down preserves drag selection");Application.Current.Shutdown();
    }
}
