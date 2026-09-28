using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wysicraft.Core;
using Wysicraft.Models;

namespace Wysicraft.Designer;
public partial class MainWindow
{
    void BuildRowTemplateFields(Element element)
    {
        if(element.Type=="button" && project.Screens.Any(s=>s.Elements.Any(e=>e.RowTemplate==ui.Id))) {
            Heading(Properties,"Row button");
            var actions=new ComboBox {ItemsSource=RowTemplates.Actions,SelectedItem=element.RowAction,Margin=new Thickness(4)};
            actions.SelectionChanged+=(_,_)=>{Change();element.RowAction=(string)actions.SelectedItem;};Properties.Children.Add(actions);
            Properties.Children.Add(new TextBlock {Text="Calls this event on the owning Item List. Its script receives the zero-based row index as value.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4)});
        }
        if(element.Type!="item_list")return;
        Heading(Properties,"Reusable row template");
        var pick=new ComboBox {ItemsSource=new[]{""}.Concat(project.Screens.Where(s=>s!=ui && !s.IsComponent).Select(s=>s.Id)).ToArray(),SelectedItem=element.RowTemplate,Margin=new Thickness(4)};
        pick.SelectionChanged+=(_,_)=>{Change();element.RowTemplate=pick.SelectedItem as string ?? "";Draw();};Properties.Children.Add(pick);
        var tools=new StackPanel {Orientation=Orientation.Horizontal};Properties.Children.Add(tools);
        var create=new Button {Content="New row template"};var edit=new Button {Content="Edit template"};tools.Children.Add(create);tools.Children.Add(edit);
        create.Click+=(_,_)=>Guard(()=>{
            string? id=Prompt("New row template","Template screen ID","item_row");if(id==null)return;
            if(!Wysicraft.Core.Validation.Id(id) || project.Screens.Any(s=>s.Id==id))throw new InvalidOperationException("Choose a unique lowercase ID");
            Change();element.RowTemplate=id;element.RowHeight=48;
            int width=Math.Max(180,(int)element.Bounds.Width);
            var template=new UiDefinition {Id=id,Title="Item row template",Size=new(){Width=width,Height=48},Elements=[
                new Element {Id="row_panel",Type="panel",HorizontalAnchor="stretch",Bounds=new(){Width=width,Height=46},Background="#1B2E36",CornerRadius=3},
                new Element {Id="icon",Type="item",Parent="row_panel",Item="${row.item}",FillEnabled=false,Bounds=new(){X=4,Y=8,Width=24,Height=24}},
                new Element {Id="name",Type="label",HorizontalAnchor="stretch",Parent="row_panel",Text="${row.name}",FillEnabled=false,Bounds=new(){X=32,Y=2,Width=width-36,Height=20}},
                new Element {Id="amount",Type="label",Parent="row_panel",Text="×${row.count}",FillEnabled=false,Bounds=new(){X=32,Y=24,Width=60,Height=18}},
                new Element {Id="primary",Type="button",HorizontalAnchor="right",Parent="row_panel",Text="Add 1",RowAction="item_primary",Bounds=new(){X=width-80,Y=24,Width=76,Height=18},Background="#426649"}
            ]};project.Screens.Add(template);ui=template;selected.Clear();RefreshAll();
        });
        edit.Click+=(_,_)=>Guard(()=>{ui=project.Screens.FirstOrDefault(s=>s.Id==element.RowTemplate) ?? throw new InvalidOperationException("Select a template first");selected.Clear();RefreshAll();});
        Properties.Children.Add(new TextBlock {Text="Empty uses the standard row. Design templates at the list's width and RowHeight. Bind text or item IDs with ${row.name}, ${row.item}, ${row.count}, ${row.index}. Templates support nested panels and display controls; configure row button actions in Properties.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4)});
    }
    Canvas RenderTemplateRow(Element list,ItemRow row,int index,Action<string>? fire,IReadOnlyDictionary<string,string>? state)
    {
        state ??= ui.Variables;var elements=Json.Clone(RowTemplates.Resolve(project,list));
        var original=project.Screens.FirstOrDefault(s=>s.Id==list.RowTemplate);
        var template=new UiDefinition {Elements=elements,Size=new(){Width=original?.Size.Width ?? (int)(list.RowTemplateWidth>0?list.RowTemplateWidth:list.Bounds.Width),Height=list.RowHeight}};
        var bounds=ResponsiveLayout.Resolve(template,list.Bounds.Width,list.RowHeight);foreach(var child in elements)child.Bounds=bounds[child.Id];
        var canvas=new Canvas {Width=list.Bounds.Width*Zoom,Height=list.RowHeight*Zoom,ClipToBounds=true,Background=Brushes.Transparent};
        foreach(var source in elements) {
            var ancestors=ContainerTree.Ancestors(template,source).ToArray();if(!source.Visible || !Expressions.Evaluate(source.VisibleIf,state) || ancestors.Any(p=>!p.Visible || !Expressions.Evaluate(p.VisibleIf,state)))continue;
            var bound=RowTemplates.Bind(source,row,index);bound.Text=Expressions.Bind(bound.Text,state);
            var control=RenderControl(bound,true,_=>{if(source.RowAction.Length>0){list.Text=index.ToString();fire?.Invoke(source.RowAction);}});
            control.Width=source.Bounds.Width*Zoom;control.Height=source.Bounds.Height*Zoom;
            control.IsEnabled=source.Enabled && Expressions.Evaluate(source.EnabledIf,state) && ancestors.All(p=>p.Enabled && Expressions.Evaluate(p.EnabledIf,state));
            control.IsHitTestVisible=source.Type=="button";
            var rect=new Rect(source.Bounds.X*Zoom,source.Bounds.Y*Zoom,control.Width,control.Height);
            foreach(var p in ancestors)rect.Intersect(new Rect(p.Bounds.X*Zoom,p.Bounds.Y*Zoom,p.Bounds.Width*Zoom,p.Bounds.Height*Zoom));
            control.Clip=new RectangleGeometry(rect.IsEmpty?new Rect():new Rect(rect.X-source.Bounds.X*Zoom,rect.Y-source.Bounds.Y*Zoom,rect.Width,rect.Height));
            Canvas.SetLeft(control,source.Bounds.X*Zoom);Canvas.SetTop(control,source.Bounds.Y*Zoom);canvas.Children.Add(control);
        }
        return canvas;
    }
    internal async Task VerifyNesting(string output)
    {
        VerifyLayerEditing(output+".layers.txt");
        // Its own starting point: the layer checks above end on a different set of items with no groups left.
        project=new Project();ui=project.Screens[0];
        ui.Elements=[new Element {Id="one",LayerGroup="Controls"},new Element {Id="two",LayerGroup="Controls"}];
        ui.GroupParents=new() {["Controls"]=""};
        history.Clear();selected.Clear();RefreshAll();
        selected.Clear();selected.UnionWith(ui.Elements.Where(e=>e.LayerGroup=="Controls").Select(e=>e.Id));SetLayerGroup("Outer");
        if(ui.GroupParents.GetValueOrDefault("Controls")!="Outer")throw new Exception("Nested group creation failed");
        Duplicate();var copiedGroups=ui.Elements.Where(e=>selected.Contains(e.Id)).Select(e=>e.LayerGroup).Distinct().ToArray();
        if(copiedGroups.Any(g=>LayerGroups.Path(ui,g).Count()!=2 || LayerGroups.Root(ui,g)=="Outer"))throw new Exception("Nested duplication failed");
        // A list with a row template of its own, so the check doesn't depend on which project was opened.
        project=new Project();ui=project.Screens[0];
        project.Screens.Add(new UiDefinition {Id="item_row",Title="Item row template",Size=new(){Width=180,Height=48},Elements=[
            new Element {Id="row_panel",Type="panel",Bounds=new(){Width=180,Height=46}},
            new Element {Id="name",Type="label",Parent="row_panel",Text="${row.name}",Bounds=new(){X=4,Y=2,Width=120,Height=20}},
            new Element {Id="primary",Type="button",Parent="row_panel",Text="Add 1",RowAction="item_primary",Bounds=new(){X=100,Y=24,Width=70,Height=18}}]});
        project.Manifest.Ui=["main","item_row"];
        ui.Elements=[new Element {Id="list1",Type="item_list",RowTemplate="item_row",RowHeight=48,Bounds=new(){X=8,Y=8,Width=180,Height=96},
            Value="[{\"item\":\"minecraft:stone\",\"name\":\"Stone\",\"count\":3},{\"item\":\"minecraft:dirt\",\"name\":\"Dirt\",\"count\":5}]"}];
        history.Clear();selected.Clear();RefreshAll();
        var list=ui.Elements.First(e=>e.Type=="item_list");string fired="";
        var row=RenderTemplateRow(list,ItemRows.Parse(list.Value)[1],1,e=>fired=e,ui.Variables);
        var button=row.Children.OfType<Button>().Single();button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(fired!="item_primary" || list.Text!="1")throw new Exception("Row button did not route index to owning list");
        var preview=new PreviewSession(this,Json.CloneProject(project),ui.Id);preview.Window.Show();await preview.WaitReady();
        await preview.CaptureCanvas(output+".png");await preview.CloseAsync();
        dirty=false;System.IO.File.WriteAllText(output,"PASS: nested group editing/duplication, row button/index and preview rendering");
    }
}


