using System.Windows;
using System.Windows.Controls;
using Wysicraft.Core;
using Wysicraft.Models;
namespace Wysicraft.Designer;

// Attach/detach controls to panels, the Parent dropdown and the panel tree shown in Layers.
public partial class MainWindow
{
    // The panel a multi-selection would attach to: the only selected panel, or the right-clicked one when several are selected.
    Element? AttachTarget(Element? clicked) {
        var panels=ui.Elements.Where(e=>selected.Contains(e.Id) && ContainerTree.IsContainer(e)).ToList();
        if(panels.Count==1)return panels[0];
        return clicked!=null && panels.Contains(clicked)?clicked:null;
    }
    void AddPanelMenuItems(ContextMenu menu,Element element) {
        var attach=new MenuItem {InputGestureText=DisplayGesture(GestureFor("edit.attach"))};var detach=new MenuItem {Header="Detach from panel",InputGestureText=DisplayGesture(GestureFor("edit.detach"))};
        ToolTipService.SetShowOnDisabled(attach,true);ToolTipService.SetShowOnDisabled(detach,true);
        attach.Click+=(_,_)=>Guard(()=>AttachSelection(element));
        detach.Click+=(_,_)=>Guard(()=>{if(!selected.Contains(element.Id)){selected.Clear();selected.Add(element.Id);}DetachSelection();});
        menu.Items.Add(new Separator());menu.Items.Add(attach);menu.Items.Add(detach);
        // Selection can change after the menu is built, so work out the state when it opens.
        menu.Opened+=(_,_)=>{
            var target=AttachTarget(element);
            int others=selected.Count(id=>id!=target?.Id);
            attach.Header=target==null?"Attach to panel":$"Attach to '{target.Id}'";
            attach.IsEnabled=target!=null && others>0;
            attach.ToolTip=target==null
                ? "Select a panel together with the items to put inside it. With several panels selected, right-click the one to attach to."
                : others==0?"Also select the items to put inside this panel.":$"Puts {others} item(s) inside '{target.Id}'. They then follow its anchors and move with it.";
            var ids=selected.Contains(element.Id)?selected:new HashSet<string>{element.Id};
            detach.IsEnabled=ui.Elements.Any(e=>ids.Contains(e.Id) && e.Parent.Length>0);
            detach.ToolTip=detach.IsEnabled?"Moves the item out of its panel, keeping its position on screen.":"The selected items aren't inside a panel.";
        };
    }
    void AttachSelection(Element? clicked) {
        var target=AttachTarget(clicked) ?? throw new InvalidOperationException("Select a panel together with the items to put inside it.");
        AttachTo(selected.ToList(),target.Id,keepSelection:false);
    }
    // Shared by the right-click menu (then selects the panel) and dropping onto a panel in Layers (keeps the dragged items selected).
    void AttachTo(IEnumerable<string> ids,string panelId,bool keepSelection) {
        var list=ids.ToList();var candidate=Json.Clone(ui);var outside=ContainerTree.Attach(candidate,list,panelId);
        Change();ui.Elements=candidate.Elements;
        selected.Clear();if(keepSelection)selected.UnionWith(list.Where(id=>id!=panelId));else selected.Add(panelId);
        Draw();RefreshInspector();
        Log($"Attached to '{panelId}'."+(outside.Count>0?$" {outside.Count} item(s) extend past the panel and will be cut off at its edges: {string.Join(", ",outside)}":""));
    }
    void DetachSelection() {
        Change();int count=ContainerTree.Detach(ui,selected.ToList());Draw();RefreshInspector();
        Log(count==0?"Nothing to detach.":$"Detached {count} item(s) from their panel.");
    }
    // Parent dropdown: only panels this element can legally go inside.
    void BuildParentField(Element element) {
        const string none="(none – screen)";
        var dock=new DockPanel();
        dock.Children.Add(new TextBlock {Text="Parent panel",Width=FieldLabelWidth,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(4)});
        var choices=new List<string>{none};choices.AddRange(ContainerTree.ValidParents(ui,element).Select(p=>p.Id));
        var pick=new ComboBox {ItemsSource=choices,SelectedItem=element.Parent.Length==0?none:element.Parent,
            ToolTip="The panel this item sits inside. It follows that panel's anchors, moves with it and is clipped to it."};
        pick.SelectionChanged+=(_,_)=>Guard(()=>{
            string parent=pick.SelectedItem as string is string value && value!=none?value:"";
            if(parent==element.Parent)return;
            var candidate=Json.Clone(ui);ContainerTree.SetParent(candidate,element.Id,parent);
            Change();ui.Elements=candidate.Elements;Draw();RefreshInspector();
        });
        dock.Children.Add(pick);Properties.Children.Add(dock);
    }
    // Layers order: within a layer group, each panel's children are listed directly under it (front-most first).
    List<(Element Element,int Depth)> LayerTreeOrder(List<Element> ordered) {
        var output=new List<(Element,int)>();var placed=new HashSet<string>();
        bool Nested(Element e)=>e.Parent.Length>0 && ordered.FirstOrDefault(p=>p.Id==e.Parent) is Element p && p.LayerGroup==e.LayerGroup;
        void Emit(Element e,int depth) {
            if(!placed.Add(e.Id))return;output.Add((e,depth));
            foreach(var child in ordered.Where(c=>c.Parent==e.Id && c.LayerGroup==e.LayerGroup))Emit(child,depth+1);
        }
        foreach(var e in ordered)if(!Nested(e))Emit(e,0);
        foreach(var e in ordered)Emit(e,0); // anything left (defensive: malformed parents)
        return output;
    }

    internal void VerifyPanelParenting(string output) {
        project=new Project();ui=project.Screens[0];history.Clear();selected.Clear();
        ui.Elements=[
            new(){Id="label",Type="label",Bounds=new(){X=20,Y=20,Width=60,Height=20}},
            new(){Id="panel",Type="panel",HorizontalAnchor="stretch",Bounds=new(){X=10,Y=10,Width=200,Height=100}},
            new(){Id="button",Type="button",Bounds=new(){X=150,Y=90,Width=80,Height=20}},
            new(){Id="other",Type="panel",Bounds=new(){X=220,Y=10,Width=80,Height=80}}];
        RefreshAll();Element E(string id)=>ui.Elements.Single(x=>x.Id==id);int Z(string id)=>ui.Elements.FindIndex(x=>x.Id==id);
        // Attach from the canvas menu with the panel and two items selected.
        selected.UnionWith(new[]{"label","panel","button"});
        var menu=ElementMenu(E("label"));menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        var attach=menu.Items.OfType<MenuItem>().Single(m=>(m.Header as string ?? "").StartsWith("Attach"));
        if(!attach.IsEnabled || (string)attach.Header!="Attach to 'panel'")throw new Exception("Attach menu not offered for panel + items");
        attach.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        if(E("label").Parent!="panel" || E("button").Parent!="panel")throw new Exception("Attach did not set parents");
        if(Z("label")<Z("panel") || Z("button")<Z("panel"))throw new Exception("Attached items are behind the panel");
        if(Z("label")>Z("button"))throw new Exception("Attach changed the items' relative order");
        // Moving the panel carries its children; its stretch anchor reflows them on resize.
        history.Undo();if(E("label").Parent.Length>0 || Z("label")!=0)throw new Exception("Attach is not one undo step");history.Redo();
        // Layers shows children indented under their panel.
        RefreshLayers();var rows=Layers.Items.Cast<ListBoxItem>().Select(i=>(string)i.Tag).ToList();
        if(rows.IndexOf("button")!=rows.IndexOf("panel")+1 || rows.IndexOf("label")!=rows.IndexOf("panel")+2)throw new Exception("Layers did not nest children under panel: "+string.Join(",",rows));
        var labelRow=(DockPanel)Layers.Items.Cast<ListBoxItem>().Single(i=>Equals(i.Tag,"label")).Content;
        if(!labelRow.Children.OfType<StackPanel>().SelectMany(p=>p.Children.OfType<TextBlock>()).Any(t=>t.Text.Contains('↳')))throw new Exception("Nested row not marked");
        // Parent dropdown: only valid panels, and choosing one re-parents.
        selected.Clear();selected.Add("panel");RefreshInspector();
        var pick=InspectorItems().OfType<DockPanel>().Select(d=>d.Children.OfType<ComboBox>().FirstOrDefault()).First(c=>c?.ToolTip is string t && t.StartsWith("The panel this item"))!;
        var items=((IEnumerable<string>)pick.ItemsSource).ToList();
        if(items.Contains("panel") || !items.Contains("other") || items.Contains("label"))throw new Exception("Parent dropdown lists invalid choices: "+string.Join(",",items));
        pick.SelectedItem="other";if(E("panel").Parent!="other" || Z("panel")<Z("other") || Z("label")<Z("panel"))throw new Exception("Parent dropdown did not re-parent in front");
        // A panel can't go inside its own child.
        bool refused=false;try{ContainerTree.SetParent(ui,"other","panel");}catch(InvalidOperationException){refused=true;}if(!refused)throw new Exception("Cycle was allowed");
        // Dragging a panel row in Layers carries its children; children can't be dragged behind it.
        ReorderLayerDrop(new[]{"label"},"panel",true);if(Z("label")<Z("panel"))throw new Exception("Child was moved behind its panel");
        // Detach keeps screen position and moves one level out.
        selected.Clear();selected.Add("label");var before=E("label").Bounds.X;DetachSelection();
        if(E("label").Parent!="other" || E("label").Bounds.X!=before)throw new Exception("Detach did not move one level out");
        // Layers hover-to-parent: half a second on a panel row switches the drop to "inside"; other rows never do.
        history.Clear();selected.Clear();
        ui.Elements=[new(){Id="p",Type="panel",Bounds=new(){X=0,Y=0,Width=200,Height=100}},new(){Id="x",Type="label",Bounds=new(){X=10,Y=10,Width=40,Height=20}},new(){Id="y",Type="button",Bounds=new(){X=60,Y=10,Width=40,Height=20}}];
        RefreshAll();var t0=DateTime.UtcNow;
        layerDragIds=["x","y"];ResetLayerHover();UpdateLayerHover("p",t0);UpdateLayerHover("p",t0.AddMilliseconds(300));
        if(layerParentTarget!=null)throw new Exception("Parent mode started before half a second");
        UpdateLayerHover("p",t0.AddMilliseconds(600));if(layerParentTarget!="p")throw new Exception("Hovering on a panel did not switch to parent mode");
        UpdateLayerHover("x",t0.AddMilliseconds(700));if(layerParentTarget!=null)throw new Exception("Moving off the panel did not cancel parent mode");
        UpdateLayerHover("x",t0.AddMilliseconds(1500));if(layerParentTarget!=null)throw new Exception("A label was offered as a parent");
        layerDragIds=["p"];ResetLayerHover();UpdateLayerHover("p",t0);UpdateLayerHover("p",t0.AddSeconds(1));if(layerParentTarget!=null)throw new Exception("A dragged panel was offered as its own parent");
        layerDragIds=["x","y"];ResetLayerHover();UpdateLayerHover("p",t0);UpdateLayerHover("p",t0.AddSeconds(1));ShowLayerDropMarker(false);
        if(Layers.Items.Cast<ListBoxItem>().Single(i=>Equals(i.Tag,"p")).Background!=ParentDropBrush)throw new Exception("Panel row did not light up");
        ClearLayerDropMarker();CompleteLayerDrop(layerDragIds,"p",false);
        if(E("x").Parent!="p" || E("y").Parent!="p" || !selected.SetEquals(new[]{"x","y"}) || Z("x")<Z("p"))throw new Exception("Drop in parent mode did not attach");
        history.Undo();if(E("x").Parent.Length>0)throw new Exception("Drop attach is not one undo step");
        ResetLayerHover();CompleteLayerDrop(["x"],"p",true);if(E("x").Parent.Length>0)throw new Exception("A quick drop attached instead of reordering");
        dirty=false;System.IO.File.WriteAllText(output,"PASS: attach from menu (in front, order kept, one undo), Layers nesting, parent dropdown choices and re-parenting, cycle refusal, children stay in front, detach one level, Layers hover-to-parent (timing, cancel, invalid targets, highlight, attach, undo, quick drop reorders)");Application.Current.Shutdown();
    }
}
