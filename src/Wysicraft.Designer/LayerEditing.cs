using Wysicraft.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wysicraft.Models;

namespace Wysicraft.Designer;
public partial class MainWindow
{
    const string GroupPrefix="@group:";
    readonly HashSet<string> collapsedGroups=new();
    Point layerDragStart;
    string? layerDragRow;
    string? pendingLayerClick;
    bool layersDragging;
    ListBoxItem? layerDropMarker;
    DateTime nextLayerScroll;
    void SetLayerOrder(List<Element> front) => ui.Elements=LayerGroups.Ordered(ui,front).AsEnumerable().Reverse().ToList();
    IEnumerable<string> LayerRowIds(string key) => key.StartsWith(GroupPrefix)
        ? ui.Elements.Where(e=>LayerGroups.Contains(ui,key[GroupPrefix.Length..],e.LayerGroup)).Select(e=>e.Id)
        : new[]{key};
    string UniqueLayerGroup(string name) { string result=name; int number=2; while(ui.GroupParents.ContainsKey(result) || ui.Elements.Any(e=>e.LayerGroup==result)) result=name+" "+number++; return result; }
    void GroupSelected() {
        if(selected.Count<2) {Log("Select at least two layers to group.");return;}
        var name=Prompt("Group layers","Group name",UniqueLayerGroup("Group"));
        if(name!=null) SetLayerGroup(name);
    }
    void SetLayerGroup(string name) {
        name=name.Trim(); if(name.Length is <1 or >64 || name.Any(char.IsControl)) throw new InvalidOperationException("Use a group name of 1–64 characters.");
        if(ui.GroupParents.ContainsKey(name) || ui.Elements.Any(e=>LayerGroups.Path(ui,e.LayerGroup).Contains(name))) throw new InvalidOperationException("That group already exists. Drag items into it instead.");
        Change(); var front=ui.Elements.AsEnumerable().Reverse().ToList(); var moved=front.Where(e=>selected.Contains(e.Id)).ToList();
        int index=front.FindIndex(e=>selected.Contains(e.Id)); if(index<0)return;
        var roots=SelectedGroupRoots(selected);
        front.RemoveAll(e=>selected.Contains(e.Id)); foreach(var e in moved) if(!roots.Any(g=>LayerGroups.Contains(ui,g,e.LayerGroup)))e.LayerGroup=name;
        foreach(var group in roots)ui.GroupParents[group]=name;ui.GroupParents.TryAdd(name,"");
        front.InsertRange(Math.Min(index,front.Count),moved); SetLayerOrder(front); collapsedGroups.Remove(name);
        Draw(); RefreshInspector();
    }
    List<string> SelectedGroupRoots(IEnumerable<string> selection) {
        var ids=selection.ToHashSet();var groups=ui.Elements.SelectMany(e=>LayerGroups.Path(ui,e.LayerGroup)).Distinct().Where(g=>ui.Elements.Where(e=>LayerGroups.Contains(ui,g,e.LayerGroup)).All(e=>ids.Contains(e.Id))).ToHashSet();
        return groups.Where(g=>!LayerGroups.Path(ui,g).Skip(1).Any(groups.Contains)).ToList();
    }
    void NewLayerGroup() {
        int n=1;while(ui.GroupParents.ContainsKey("Group "+n)||ui.Elements.Any(e=>e.LayerGroup=="Group "+n))n++;
        var name=Prompt("New group","Group name","Group "+n);if(name==null)return;name=name.Trim();
        if(name.Length==0||name.Length>64||name.Any(char.IsControl))throw new InvalidOperationException("Group names are 1–64 characters.");
        if(ui.GroupParents.ContainsKey(name)||ui.Elements.Any(e=>e.LayerGroup==name))throw new InvalidOperationException("There is already a group called "+name+".");
        Change();ui.GroupParents[name]=isolatedGroup;RefreshLayers();Log("Made the empty group "+name+". Drag layers onto it to put them inside.");
    }
    void RenameLayerGroup(string group,string name) {
        name=name.Trim();if(name==group)return;if(name.Length is <1 or >64 || name.Any(char.IsControl) || ui.GroupParents.ContainsKey(name) || ui.Elements.Any(e=>e.LayerGroup==name))throw new InvalidOperationException("Choose a unique group name (1–64 characters)");
        Change();string parent=ui.GroupParents.GetValueOrDefault(group,"");ui.GroupParents.Remove(group);ui.GroupParents[name]=parent;
        foreach(var key in ui.GroupParents.Keys.ToArray())if(ui.GroupParents[key]==group)ui.GroupParents[key]=name;
        foreach(var e in ui.Elements)if(e.LayerGroup==group)e.LayerGroup=name;Draw();RefreshInspector();
    }
    void UngroupSelected() {
        var roots=SelectedGroupRoots(selected);if(roots.Count==0)roots=ui.Elements.Where(e=>selected.Contains(e.Id) && e.LayerGroup.Length>0).Select(e=>e.LayerGroup).Distinct().ToList();
        if(roots.Count==0)return;UngroupLayers(roots);
    }
    void UngroupLayers(IEnumerable<string> roots) {Change();foreach(var group in roots) {string parent=ui.GroupParents.GetValueOrDefault(group,"");foreach(var e in ui.Elements.Where(e=>e.LayerGroup==group))e.LayerGroup=parent;foreach(var key in ui.GroupParents.Keys.ToArray())if(ui.GroupParents[key]==group)ui.GroupParents[key]=parent;ui.GroupParents.Remove(group);}Draw();RefreshInspector();
    }
    void RefreshLayerRows() {
        syncingLayers=true;
        try {
            var keys=new List<string>(); var seen=new HashSet<string>(); var panelDepth=new Dictionary<string,int>();
            foreach(var (e,depth) in LayerTreeOrder(LayerGroups.Ordered(ui,ui.Elements.AsEnumerable().Reverse().ToList()))) {
                panelDepth[e.Id]=depth;
                var path=LayerGroups.Path(ui,e.LayerGroup).Reverse().ToArray();bool hidden=false;
                foreach(var group in path) {if(seen.Add(group))keys.Add(GroupPrefix+group);if(collapsedGroups.Contains(group)){hidden=true;break;}}
                if(!hidden)keys.Add(e.Id);
            }
            // Empty groups (made with New group) show at the top, ready for things to be dragged in.
            foreach(var group in ui.GroupParents.Keys.Where(g=>!seen.Contains(g) && (isolatedGroup.Length==0 || LayerGroups.Contains(ui,isolatedGroup,g))).OrderBy(g=>g).Reverse())keys.Insert(0,GroupPrefix+group);
            if(!Layers.Items.Cast<ListBoxItem>().Select(i=>(string)i.Tag).SequenceEqual(keys)) {
                Layers.Items.Clear(); foreach(string key in keys) Layers.Items.Add(new ListBoxItem {Tag=key,Padding=new Thickness(6,4,2,4)});
            }
            foreach(ListBoxItem item in Layers.Items) {
                string key=(string)item.Tag;
                bool available=isolatedGroup.Length==0 || (key.StartsWith(GroupPrefix)?LayerGroups.Contains(ui,isolatedGroup,key[GroupPrefix.Length..]):InIsolation(ui.Elements.First(e=>e.Id==key)));
                item.IsEnabled=available;item.Opacity=available?1:.3;
                if(key.StartsWith(GroupPrefix)) {
                    string group=key[GroupPrefix.Length..]; var ids=LayerRowIds(key).ToList();
                    var members=ui.Elements.Where(x=>ids.Contains(x.Id)).ToList();
                    var row=new DockPanel {LastChildFill=true};
                    AddLayerToggles(row,members);
                    var toggle=new Button {Content=collapsedGroups.Contains(group)?"▸":"▾",Padding=new Thickness(2,0,2,0),Margin=new Thickness(0),ToolTip="Expand / collapse group",Style=(Style)FindResource("IconButton")};
                    toggle.Click+=(_,args)=> {if(!collapsedGroups.Add(group))collapsedGroups.Remove(group);RefreshLayers();args.Handled=true;};
                    var label=new StackPanel {Orientation=Orientation.Horizontal};
                    label.Children.Add(toggle);label.Children.Add(Icons.Get("folder"));label.Children.Add(new TextBlock {Text=" "+group+" ("+ids.Count+")",FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});
                    row.Children.Add(label);item.HorizontalContentAlignment=HorizontalAlignment.Stretch;
                    label.Margin=new Thickness((LayerGroups.Path(ui,group).Count()-1)*12,0,0,0); item.Content=row; item.IsSelected=false; label.Opacity=ids.All(selected.Contains)?1:0.85; item.ToolTip="Drag to reorder the group. Right-click for group actions.";
                    var menu=new ContextMenu();
                    var isolate=new MenuItem {Header="Isolate group"};isolate.Click+=(_,_)=>IsolateGroup(group);menu.Items.Add(isolate);
                    void Add(string title,Action action) {var entry=new MenuItem {Header=title};entry.Click+=(_,_)=>Guard(()=>{selected.Clear();selected.UnionWith(ids);action();});menu.Items.Add(entry);}
                    Add("Rename group",()=>{var name=Prompt("Rename group","Group name",group);if(name!=null)RenameLayerGroup(group,name);});
                    Add("Move group to top level",()=>{Change();ui.GroupParents[group]="";SetLayerOrder(ui.Elements.AsEnumerable().Reverse().ToList());Draw();RefreshInspector();});
                    Add("Ungroup",()=>UngroupLayers(new[]{group}));Add("Duplicate",Duplicate);Add("Delete group and elements",Delete); item.ContextMenu=menu;
                } else {
                    var e=ui.Elements.First(e=>e.Id==key); int depth=panelDepth.GetValueOrDefault(e.Id); bool elsewhere=e.Parent.Length>0 && depth==0;
                    var row=new DockPanel {LastChildFill=true};AddLayerToggles(row,new List<Element>{e});
                    var label=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(LayerGroups.Path(ui,e.LayerGroup).Count()*12+depth*14,0,0,0)};
                    if(depth>0)label.Children.Add(new TextBlock {Text="↳ ",Foreground=Brushes.Gray,VerticalAlignment=VerticalAlignment.Center});
                    label.Children.Add(Icons.Get(Icons.Has(e.Type)?e.Type:"button"));
                    label.Children.Add(new TextBlock {Text=" "+e.Id,VerticalAlignment=VerticalAlignment.Center});
                    label.Children.Add(new TextBlock {Text="  "+e.Type+(elsewhere?"  (in "+e.Parent+")":""),Foreground=Brushes.Gray,VerticalAlignment=VerticalAlignment.Center});
                    if(!e.Visible)label.Opacity=.5;
                    row.Children.Add(label);item.HorizontalContentAlignment=HorizontalAlignment.Stretch;item.Content=row;
                    item.ToolTip=e.Name.Length>0?e.Name:e.Text; item.IsSelected=selected.Contains(e.Id); item.ContextMenu=ElementMenu(e);
                }
            }
        } finally {syncingLayers=false;}
    }
    void InitializeLayerDragging() {
        Layers.AllowDrop=true;
        Layers.MouseDoubleClick+=(_,e)=>{var row=ItemsControl.ContainerFromElement(Layers,e.OriginalSource as DependencyObject) as ListBoxItem;if(row?.Tag is string key && key.StartsWith(GroupPrefix)){IsolateGroup(key[GroupPrefix.Length..]);e.Handled=true;}else if(row?.Tag is string id && ui.Elements.FirstOrDefault(x=>x.Id==id) is Element element && !InsideButton(e.OriginalSource as DependencyObject)){e.Handled=true;Guard(()=>RenameElement(element));}};
        Layers.PreviewMouseLeftButtonDown+=(_,e)=> {
            pendingLayerClick=null;
            layerDragStart=e.GetPosition(Layers); var row=ItemsControl.ContainerFromElement(Layers,e.OriginalSource as DependencyObject) as ListBoxItem;
            layerDragRow=row?.Tag as string;
            for(var node=e.OriginalSource as DependencyObject;node!=null && node!=row;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))
                if(node is System.Windows.Controls.Primitives.ButtonBase){layerDragRow=null;return;}
            if(row?.IsSelected==true && Keyboard.Modifiers==ModifierKeys.None) {pendingLayerClick=layerDragRow;e.Handled=true;}
        };
        Layers.PreviewMouseLeftButtonUp+=(_,e)=> {
            var key=pendingLayerClick;pendingLayerClick=null;layerDragRow=null;
            if(key==null || layersDragging || Keyboard.Modifiers!=ModifierKeys.None)return;
            var row=ItemsControl.ContainerFromElement(Layers,e.OriginalSource as DependencyObject) as ListBoxItem;
            if(row?.Tag as string!=key)return;
            selected.Clear();selected.UnionWith(LayerRowIds(key));Draw();RefreshInspector();e.Handled=true;
        };
        Layers.PreviewMouseMove+=(_,e)=> {
            if(e.LeftButton!=MouseButtonState.Pressed || layerDragRow==null || layersDragging)return;
            var delta=e.GetPosition(Layers)-layerDragStart;
            if(Math.Abs(delta.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y)<SystemParameters.MinimumVerticalDragDistance)return;
            var ids=LayerRowIds(layerDragRow).ToArray(); if(!ids.All(selected.Contains)){selected.Clear();selected.UnionWith(ids);}
            pendingLayerClick=null;layersDragging=true;
            layerDragIds=selected.ToArray();ResetLayerHover();layerHoverTimer.Start();
            try {DragDrop.DoDragDrop(Layers,new DataObject("Wysicraft.LayerOrder",layerDragIds),DragDropEffects.Move);}
            finally {layersDragging=false;layerDragRow=null;layerHoverTimer.Stop();ClearLayerDropMarker();ResetLayerHover();Draw();RefreshInspector();}
        };
        Layers.DragOver+=(_,e)=> {
            e.Effects=e.Data.GetDataPresent("Wysicraft.LayerOrder")?DragDropEffects.Move:DragDropEffects.None;e.Handled=true;
            ClearLayerDropMarker(); if(e.Effects==DragDropEffects.None)return;
            if(DateTime.UtcNow>=nextLayerScroll) {
                var queue=new Queue<DependencyObject>();queue.Enqueue(Layers);
                while(queue.Count>0) {var node=queue.Dequeue();if(node is ScrollViewer scroll) {double y=e.GetPosition(Layers).Y;if(y<24)scroll.LineUp();else if(y>Layers.ActualHeight-24)scroll.LineDown();break;}for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)queue.Enqueue(VisualTreeHelper.GetChild(node,i));}
                nextLayerScroll=DateTime.UtcNow.AddMilliseconds(80);
            }
            layerDropMarker=ItemsControl.ContainerFromElement(Layers,e.OriginalSource as DependencyObject) as ListBoxItem;
            UpdateLayerHover(layerDropMarker?.Tag as string,DateTime.UtcNow);
            ShowLayerDropMarker(layerDropMarker!=null && e.GetPosition(layerDropMarker).Y>=layerDropMarker.ActualHeight/2);
        };
        Layers.DragLeave+=(_,_)=>ClearLayerDropMarker();
        Layers.Drop+=(_,e)=> {
            if(e.Data.GetData("Wysicraft.LayerOrder") is not string[] ids)return;
            var row=ItemsControl.ContainerFromElement(Layers,e.OriginalSource as DependencyObject) as ListBoxItem;
            string? key=row?.Tag as string; bool after=row!=null && e.GetPosition(row).Y>=row.ActualHeight/2;
            Guard(()=>CompleteLayerDrop(ids,key,after));ClearLayerDropMarker();e.Handled=true;
        };
        // Drag events stop while the pointer rests, so a timer notices when the half-second hover is reached.
        layerHoverTimer.Tick+=(_,_)=>{ if(!layersDragging || layerParentTarget!=null)return; UpdateLayerHover(layerHoverKey,DateTime.UtcNow); if(layerParentTarget!=null)ShowLayerDropMarker(false); };
        var buttons=(Panel)LayerDuplicate.Parent; foreach(var (label,action) in new (string,Action)[]{("Group",GroupSelected),("Ungroup",UngroupSelected)}) {var button=new Button {Content=Icons.Get(label.ToLowerInvariant()),ToolTip=label};button.Click+=(_,_)=>Guard(action);buttons.Children.Add(button);}
        if(buttons.Parent is DockPanel host) { int index=host.Children.IndexOf(buttons); var wrap=new WrapPanel(); while(buttons.Children.Count>0) {var child=buttons.Children[0];buttons.Children.RemoveAt(0);wrap.Children.Add(child);} host.Children.Remove(buttons);DockPanel.SetDock(wrap,Dock.Bottom);host.Children.Insert(index,wrap); }
    }
    void ClearLayerDropMarker() { if(layerDropMarker!=null) {layerDropMarker.ClearValue(Control.BorderThicknessProperty);layerDropMarker.ClearValue(Control.BorderBrushProperty);layerDropMarker.ClearValue(Control.BackgroundProperty);layerDropMarker=null;} }
    void ReorderLayerDrop(string[] ids,string? target,bool after) {
        if(isolatedGroup.Length>0 && (ids.Any(id=>!InIsolation(ui.Elements.First(e=>e.Id==id))) || target!=null && LayerRowIds(target).Any(id=>!InIsolation(ui.Elements.First(e=>e.Id==id)))))throw new InvalidOperationException("Exit isolation to move layers outside this group.");
        // A panel carries its children, so the subtree keeps its order and stays in front of the panel.
        var moving=ContainerTree.Moving(ui,ids).Select(e=>e.Id).ToHashSet(); if(target!=null && LayerRowIds(target).All(moving.Contains))return;
        var front=ui.Elements.AsEnumerable().Reverse().ToList(); var moved=front.Where(e=>moving.Contains(e.Id)).ToList(); if(moved.Count==0)return;
        bool wholeGroup=moved.Any(e=>e.LayerGroup.Length>0 && ui.Elements.Where(x=>x.LayerGroup==e.LayerGroup).All(x=>moving.Contains(x.Id)));
        string destination=target==null?isolatedGroup:target.StartsWith(GroupPrefix)?target[GroupPrefix.Length..]:ui.Elements.First(e=>e.Id==target).LayerGroup;
        var roots=SelectedGroupRoots(moving);
        if(target?.StartsWith(GroupPrefix)==true && roots.Any(g=>LayerGroups.Contains(ui,g,destination)))throw new InvalidOperationException("Cannot move a group into itself or a descendant");
        Change(); if(target==null || target.StartsWith(GroupPrefix))foreach(var group in roots)ui.GroupParents[group]=destination;
        front.RemoveAll(e=>moving.Contains(e.Id));
        int index=front.Count;
        if(target!=null) {
            if(target.StartsWith(GroupPrefix) || wholeGroup && destination.Length>0) {int first=front.FindIndex(e=>LayerGroups.Contains(ui,destination,e.LayerGroup));int last=front.FindLastIndex(e=>LayerGroups.Contains(ui,destination,e.LayerGroup));index=after?last+1:first;}
            else {index=front.FindIndex(e=>e.Id==target)+(after?1:0);}
        }
        foreach(var e in moved)if(!roots.Any(g=>LayerGroups.Contains(ui,g,e.LayerGroup)))e.LayerGroup=destination;
        front.InsertRange(Math.Clamp(index,0,front.Count),moved);SetLayerOrder(front);ContainerTree.KeepChildrenInFront(ui); selected.Clear();selected.UnionWith(ids);
        Draw();RefreshInspector();
    }
    internal void VerifyLayerEditing(string output) {
        project=new Project();ui=project.Screens[0];ui.Elements=[new Element {Id="one"},new Element {Id="two"},new Element {Id="three"}];history.Clear();selected.Clear();selected.UnionWith(new[]{"one","two"});
        SetLayerGroup("Controls");
        if(ui.Elements.Count(e=>e.LayerGroup=="Controls")!=2)throw new Exception("Grouping failed");
        ReorderLayerDrop(new[]{"one","two"},"three",false);
        if(ui.Elements.Last().Id!="two" || ui.Elements.First().Id!="three")throw new Exception("Group ordering failed");
        SelectCanvasElement("one",ModifierKeys.None);if(selected.Count!=2)throw new Exception("Canvas did not select group");
        SelectCanvasElement("one",ModifierKeys.Alt);if(selected.Count!=1)throw new Exception("Alt selection did not isolate item");
        ReorderLayerDrop(new[]{"three"},GroupPrefix+"Controls",true);
        if(ui.Elements.First(e=>e.Id=="three").LayerGroup!="Controls")throw new Exception("Drop into group failed");
        history.Undo();if(ui.Elements.First(e=>e.Id=="three").LayerGroup!="")throw new Exception("Undo failed");
        selected.Clear();selected.UnionWith(new[]{"one","two"});Duplicate();
        if(ui.Elements.Where(e=>selected.Contains(e.Id)).Any(e=>e.LayerGroup=="Controls" || e.LayerGroup.Length==0))throw new Exception("Duplicate did not isolate copied group");
        Wysicraft.Packaging.ProjectStore.SaveProject(project,output+".wysicraftproj");
        if(Wysicraft.Packaging.ProjectStore.Load(output+".wysicraftproj").Screens[0].Elements.Count(e=>e.LayerGroup.Length>0)!=4)throw new Exception("Groups were not saved");
        UngroupSelected();if(ui.Elements.Where(e=>selected.Contains(e.Id)).Any(e=>e.LayerGroup.Length>0))throw new Exception("Ungroup failed");
        // Bring to front / Send to back stay inside the selection's group and keep groups intact.
        ui.Elements=[new Element {Id="a"},new Element {Id="b",LayerGroup="G"},new Element {Id="c",LayerGroup="G"},new Element {Id="d"}];ui.GroupParents=new();history.Clear();
        string Order()=>string.Join(",",ui.Elements.Select(e=>e.Id));
        selected.Clear();selected.Add("a");BringToFront();if(Order()!="b,c,d,a")throw new Exception("Bring to front: "+Order());
        SendToBack();if(Order()!="a,b,c,d")throw new Exception("Send to back: "+Order());
        selected.Clear();selected.Add("b");BringToFront();if(Order()!="a,c,b,d" || ui.Elements.First(e=>e.Id=="b").LayerGroup!="G")throw new Exception("Front within group: "+Order());
        SendToBack();if(Order()!="a,b,c,d")throw new Exception("Back within group: "+Order());
        history.Undo();if(Order()!="a,c,b,d")throw new Exception("Undo order: "+Order());
        selected.Clear();selected.Add("d");Duplicate();if(!ui.Elements.Any(e=>e.Id=="d1"))throw new Exception("Duplicate ID not numbered: "+Order());
        VerifyLockChildren();
        dirty=false;System.IO.File.WriteAllText(output,"PASS: grouping, order, front/back, canvas/Alt selection, drop into group, undo, independent duplication, numbered IDs, locking items inside panels, save/load and ungroup.");
    }
}


public partial class MainWindow
{
    // Eye (visible when the screen opens) and lock (editor-only) buttons on the right of a Layers row.
    // For a group row they apply to every member.
    void AddLayerToggles(DockPanel row,List<Element> targets) {
        bool allHidden=targets.All(t=>!t.Visible),allLocked=targets.All(t=>t.Locked);
        Button Toggle(string icon,double opacity,string tip,Action apply) {
            var button=new Button {Content=Icons.Get(icon,size:13),Opacity=opacity,ToolTip=tip,Style=(Style)FindResource("IconButton")};
            button.Click+=(_,args)=>{Guard(()=>{Change();apply();Draw();RefreshInspector();});args.Handled=true;};
            DockPanel.SetDock(button,Dock.Right);row.Children.Add(button);return button;
        }
        Toggle(allLocked?"lock":"unlock",allLocked?1:.35,allLocked?"Locked: can't be clicked or moved on the canvas. Click to unlock.":"Click to lock (it can't then be clicked or moved on the canvas).",
            ()=>SetLocked(targets,!allLocked));
        Toggle(allHidden?"eye-off":"eye",allHidden?1:.6,allHidden?"Starts hidden when the screen opens. Click to show it.":"Shown when the screen opens. Click to start it hidden.",
            ()=>{foreach(var t in targets)t.Visible=allHidden;});
    }
}
public partial class MainWindow
{
    // Hover-to-parent while dragging in Layers: resting on a panel row for half a second lights it up,
    // and releasing then puts the dragged items inside that panel instead of reordering.
    const double ParentHoverSeconds=0.5;
    static readonly Brush ParentDropBrush=new SolidColorBrush(Color.FromArgb(90,14,99,156));
    readonly System.Windows.Threading.DispatcherTimer layerHoverTimer=new(){Interval=TimeSpan.FromMilliseconds(100)};
    string[] layerDragIds=[];
    string? layerHoverKey,layerParentTarget;
    DateTime layerHoverSince;
    void ResetLayerHover(){layerHoverKey=null;layerParentTarget=null;}
    void UpdateLayerHover(string? key,DateTime now) {
        if(key!=layerHoverKey){layerHoverKey=key;layerHoverSince=now;layerParentTarget=null;return;}
        if(layerParentTarget==null && key!=null && (now-layerHoverSince).TotalSeconds>=ParentHoverSeconds && CanDropInside(layerDragIds,key))layerParentTarget=key;
    }
    // Only panels and scroll panels, never one of the dragged items or something inside them.
    bool CanDropInside(string[] ids,string key) {
        if(key.StartsWith(GroupPrefix) || ui.Elements.FirstOrDefault(e=>e.Id==key) is not Element panel || !ContainerTree.IsContainer(panel) || !InIsolation(panel))return false;
        var moving=ContainerTree.Moving(ui,ids).Select(e=>e.Id).ToHashSet();
        return !moving.Contains(key) && ids.Any(id=>id!=key);
    }
    void ShowLayerDropMarker(bool after) {
        var row=layerDropMarker ?? Layers.Items.Cast<ListBoxItem>().FirstOrDefault(i=>Equals(i.Tag,layerHoverKey));
        if(row==null)return;layerDropMarker=row;
        if(layerParentTarget!=null && Equals(row.Tag,layerParentTarget)) {
            row.Background=ParentDropBrush;row.BorderBrush=Brushes.DeepSkyBlue;row.BorderThickness=new Thickness(1);
            int count=layerDragIds.Count(id=>id!=layerParentTarget);
            Status.Text=$"Release to put {count} item(s) inside '{layerParentTarget}'";
        } else {
            row.BorderBrush=Brushes.DeepSkyBlue;row.BorderThickness=after?new Thickness(0,0,0,2):new Thickness(0,2,0,0);
            Status.Text=StatusLine();
        }
    }
    void CompleteLayerDrop(string[] ids,string? key,bool after) {
        if(layerParentTarget!=null && key==layerParentTarget) {
            string target=layerParentTarget;ResetLayerHover();
            if(isolatedGroup.Length>0 && ids.Any(id=>!InIsolation(ui.Elements.First(e=>e.Id==id))))throw new InvalidOperationException("Exit isolation to move layers outside this group.");
            AttachTo(ids,target,keepSelection:true);return;
        }
        ReorderLayerDrop(ids,key,after);
    }
}
