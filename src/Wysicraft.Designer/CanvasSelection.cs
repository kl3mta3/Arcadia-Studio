using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wysicraft.Core;
using Wysicraft.Models;
namespace Wysicraft.Designer;

public partial class MainWindow
{
    string isolatedGroup="";
    UiDefinition? isolatedScreen;
    bool dragChanged;
    Point? marqueeStart,marqueeEnd;
    HashSet<string> marqueeBase=[];
    string lastCanvasGroup="";
    long lastCanvasClick;
    Point lastCanvasPoint;
    bool InIsolation(Element e)=>isolatedGroup.Length==0 || LayerGroups.Contains(ui,isolatedGroup,e.LayerGroup);
    string CanvasGroup(Element e) {
        var path=LayerGroups.Path(ui,e.LayerGroup).Reverse().ToList();
        if(isolatedGroup.Length==0)return path.FirstOrDefault()??"";
        int index=path.IndexOf(isolatedGroup);return index>=0 && index+1<path.Count?path[index+1]:"";
    }
    void IsolateGroup(string group) {
        if(!ui.Elements.Any(e=>LayerGroups.Contains(ui,group,e.LayerGroup)))return;
        EndCanvasGesture();isolatedGroup=group;isolatedScreen=ui;selected.Clear();collapsedGroups.Remove(group);lastCanvasClick=0;Draw();RefreshInspector();Surface.Focus();
    }
    void ExitIsolationClick(object sender,RoutedEventArgs e)=>ExitIsolation();
    void ExitIsolation(){EndCanvasGesture();isolatedGroup="";isolatedScreen=null;selected.Clear();lastCanvasClick=0;Draw();RefreshInspector();}
    void SyncIsolation() {
        if(isolatedGroup.Length>0 && (!ReferenceEquals(isolatedScreen,ui)||!ui.Elements.Any(e=>LayerGroups.Contains(ui,isolatedGroup,e.LayerGroup)))){isolatedGroup="";isolatedScreen=null;selected.Clear();}
        IsolationBar.Visibility=isolatedGroup.Length>0?Visibility.Visible:Visibility.Collapsed;
        IsolationLabel.Text="Isolating: "+isolatedGroup+"  •  Esc to exit";
    }
    void CanvasElementDown(Element element,MouseButtonEventArgs args) {
        if(!InIsolation(element))return;
        var point=args.GetPosition(Surface);var group=CanvasGroup(element);long now=Environment.TickCount64;
        bool twice=args.ClickCount==2 || (group.Length>0 && lastCanvasGroup==group && now-lastCanvasClick<500 && (point-lastCanvasPoint).Length<5);
        lastCanvasGroup=group;lastCanvasClick=now;lastCanvasPoint=point;
        if(twice && group.Length>0 && Keyboard.Modifiers==ModifierKeys.None){IsolateGroup(group);args.Handled=true;return;}
        // Double-clicking a polygon collider opens its outline editor.
        // Double-clicking a sprite opens its sprite sheet editor.
        if(args.ClickCount==2 && group.Length==0 && element.Type=="tilemap" && Keyboard.Modifiers==ModifierKeys.None){selected.Clear();selected.Add(element.Id);args.Handled=true;Dispatcher.BeginInvoke(()=>Guard(()=>ShowTilemapEditor(element)));return;}
        if(args.ClickCount==2 && group.Length==0 && element.Type=="sprite" && Keyboard.Modifiers==ModifierKeys.None){selected.Clear();selected.Add(element.Id);args.Handled=true;Dispatcher.BeginInvoke(()=>Guard(()=>ShowSpriteSheetEditor(element)));return;}
        if(args.ClickCount==2 && group.Length==0 && element.Collider=="polygon" && (element.Type=="collider" || element.Body.Length>0) && Keyboard.Modifiers==ModifierKeys.None){selected.Clear();selected.Add(element.Id);args.Handled=true;Dispatcher.BeginInvoke(()=>Guard(()=>ShowColliderEditor(element)));return;}
        if(isolatedGroup.Length>0 && element.Type is "panel" or "scroll_panel" && ui.Elements.Any(e=>e.Parent==element.Id) && !selected.Contains(element.Id) && Keyboard.Modifiers==ModifierKeys.None){BeginMarquee(point,Keyboard.Modifiers);args.Handled=true;return;}
        PrepareElementDrag(element.Id,Keyboard.Modifiers,point);Surface.CaptureMouse();Surface.Focus();Draw();RefreshInspector();args.Handled=true;
    }
    void PrepareElementDrag(string id,ModifierKeys modifiers,Point point) {
        // A member selected explicitly in Layers stays selected when dragged on the canvas.
        if(modifiers!=ModifierKeys.None || !selected.Contains(id))SelectCanvasElement(id,modifiers);
        dragStart=point;dragChanged=false;
        dragBounds=ContainerTree.Moving(ui,selected.Where(id=>ui.Elements.FirstOrDefault(x=>x.Id==id) is Element s && !s.Locked)).Where(InIsolation).ToDictionary(e=>e.Id,e=>Json.Clone(e.Bounds));
    }
    void BeginMarquee(Point point,ModifierKeys modifiers) {
        dragBounds=null;marqueeStart=marqueeEnd=point;
        marqueeBase=(modifiers&(ModifierKeys.Control|ModifierKeys.Shift))!=0?new(selected):[];
        selected.Clear();selected.UnionWith(marqueeBase);Surface.CaptureMouse();Surface.Focus();Draw();RefreshInspector();
    }
    void UpdateMarquee(Point point) {
        if(marqueeStart is not Point start)return;marqueeEnd=point;var rectangle=new Rect(start,point);
        var before=new HashSet<string>(selected);
        selected.Clear();selected.UnionWith(marqueeBase);
        if(rectangle.Width>=SystemParameters.MinimumHorizontalDragDistance || rectangle.Height>=SystemParameters.MinimumVerticalDragDistance)
            foreach(var e in ui.Elements.Where(e=>e.Visible&&!e.Locked&&InIsolation(e))) {
                var bounds=new Rect(e.Bounds.X*Zoom,e.Bounds.Y*Zoom,e.Bounds.Width*Zoom,e.Bounds.Height*Zoom);
                foreach(var parent in ContainerTree.Ancestors(ui,e))bounds.Intersect(new Rect(parent.Bounds.X*Zoom,parent.Bounds.Y*Zoom,parent.Bounds.Width*Zoom,parent.Bounds.Height*Zoom));
                // Touch selection: any overlap counts, except a container the box lies wholly inside (e.g. an isolated panel's background it started on).
                if(bounds.IsEmpty || !rectangle.IntersectsWith(bounds) || bounds.Contains(rectangle))continue;
                var group=CanvasGroup(e);if(group.Length==0)selected.Add(e.Id);else selected.UnionWith(ui.Elements.Where(c=>InIsolation(c)&&!c.Locked&&LayerGroups.Contains(ui,group,c.LayerGroup)).Select(c=>c.Id));
            }
        // Dragging the box only resizes it until it actually picks something up or drops it.
        if(selected.SetEquals(before) && marqueeBox!=null){PlaceMarquee(marqueeBox,new Rect(start,point));return;}
        Draw();RefreshInspector();
    }
    System.Windows.Shapes.Rectangle? marqueeBox;
    static void PlaceMarquee(System.Windows.Shapes.Rectangle box,Rect rect) {
        box.Width=rect.Width;box.Height=rect.Height;Canvas.SetLeft(box,rect.X);Canvas.SetTop(box,rect.Y);
    }
    void DrawMarquee() {
        marqueeBox=null;
        if(marqueeStart is not Point start || marqueeEnd is not Point end)return;
        var box=new System.Windows.Shapes.Rectangle {Stroke=Brushes.DeepSkyBlue,StrokeThickness=1,Fill=new SolidColorBrush(Color.FromArgb(35,0,191,255)),IsHitTestVisible=false};
        PlaceMarquee(box,new Rect(start,end));Panel.SetZIndex(box,2000);Surface.Children.Add(box);marqueeBox=box;
    }
    void EndCanvasGesture() {dragBounds=null;dragChanged=false;marqueeStart=marqueeEnd=null;marqueeBase.Clear();if(Surface.IsMouseCaptured)Surface.ReleaseMouseCapture();}
    void CanvasBackgroundDown(MouseButtonEventArgs e) {
        if(e.Source!=Surface)return;
        if(e.ClickCount==2 && isolatedGroup.Length>0){ExitIsolation();e.Handled=true;return;}
        BeginMarquee(e.GetPosition(Surface),Keyboard.Modifiers);e.Handled=true;
    }
    internal void VerifyIsolation(string output) {
        project=new Project();project.Manifest.Snap=false;ui=project.Screens[0];ui.Elements=[
            new(){Id="a",LayerGroup="Group",Bounds=new(){X=10,Y=10,Width=20,Height=20}},
            new(){Id="b",LayerGroup="Group",Bounds=new(){X=50,Y=10,Width=20,Height=20}},
            new(){Id="outside",Bounds=new(){X=90,Y=10,Width=20,Height=20}}];
        history.Clear();selected.Clear();selected.Add("a");RefreshAll();
        PrepareElementDrag("a",ModifierKeys.None,new Point(0,0));UpdateElementDrag(new Point(20,0));EndCanvasGesture();
        if(ui.Elements[0].Bounds.X!=20 || ui.Elements[1].Bounds.X!=50)throw new Exception("Dragging an explicitly selected member moved its group");history.Undo();
        if(!ElementMenu(ui.Elements[0]).Items.OfType<MenuItem>().Any(m=>Equals(m.Header,"Isolate group")))throw new Exception("Isolation context menu missing");
        UpdateLayout();lastCanvasClick=0;
        CanvasElementDown(ui.Elements[0],new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.MouseDownEvent});
        // Synthetic clicks read the live cursor, and the first click can scroll the canvas; pin the previous point so the check doesn't depend on the physical mouse.
        EndCanvasGesture();UpdateLayout();lastCanvasPoint=Mouse.GetPosition(Surface);
        var clickDebug=$"group={lastCanvasGroup}, dt={Environment.TickCount64-lastCanvasClick}, point={Mouse.GetPosition(Surface)}, previous={lastCanvasPoint}, modifiers={Keyboard.Modifiers}";
        CanvasElementDown(ui.Elements[0],new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.MouseDownEvent});
        if(isolatedGroup!="Group")throw new Exception("Double-click did not isolate group: "+clickDebug);
        SelectCanvasElement("a",ModifierKeys.None);if(selected.Count!=1)throw new Exception("Isolation selected the whole group");
        Draw();if(Surface.Children.OfType<Border>().Single(b=>Equals(b.Tag,"outside")).IsHitTestVisible)throw new Exception("Outside objects aren't locked");
        // Canvas menus are filled when opened, not on every redraw: opening one twice must still list its actions once.
        var menuBorder=Surface.Children.OfType<Border>().First(b=>Equals(b.Tag,"a"));
        var canvasMenu=menuBorder.ContextMenu ?? throw new Exception("Canvas item has no context menu");
        if(canvasMenu.Items.Count!=0)throw new Exception("Canvas menu was built before it was opened");
        canvasMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));int opened=canvasMenu.Items.Count;
        canvasMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        if(opened==0 || canvasMenu.Items.Count!=opened)throw new Exception($"Canvas menu fill on open failed: {opened} then {canvasMenu.Items.Count}");
        BeginMarquee(new Point(0,0),ModifierKeys.None);UpdateMarquee(new Point(230,70));EndCanvasGesture();
        if(!selected.SetEquals(new[]{"a","b"}))throw new Exception("Isolation marquee selected outside objects");
        ExitIsolation();BeginMarquee(new Point(170,0),ModifierKeys.None);UpdateMarquee(new Point(230,70));EndCanvasGesture();
        if(!selected.SetEquals(new[]{"outside"}))throw new Exception("Marquee did not select an enclosed object");
        BeginMarquee(new Point(200,0),ModifierKeys.None);UpdateMarquee(new Point(260,30));EndCanvasGesture();
        if(!selected.SetEquals(new[]{"outside"}))throw new Exception("Marquee did not select a partly touched object");
        BeginMarquee(new Point(0,0),ModifierKeys.Shift);UpdateMarquee(new Point(65,70));EndCanvasGesture();
        if(selected.Count!=3)throw new Exception("Additive marquee did not preserve selection or expand groups");
        var source=ComponentStarters.Add(project,"window_header");RefreshAll();if(Screens.Items.Cast<string>().Contains(source.Id))throw new Exception("Component is in screen dropdown");
        OpenComponentSource(source.Id);if(ComponentSourceBar.Visibility!=Visibility.Visible)throw new Exception("Source editing context missing");BackToScreen(this,new RoutedEventArgs());
        OpenComponentSource(source.Id);Screens.SelectedItem="main";
        if(ui.IsComponent || ComponentSourceBar.Visibility!=Visibility.Collapsed ||InspectorItems().OfType<TextBlock>().Any(t=>t.Text.StartsWith("COMPONENT SOURCE")))throw new Exception("Leaving component source through the dropdown left stale source context");
        project.Screens.Add(new(){Id="second",Events=new(){["open"]=new(){Client=new(){Actions=[new(){Type="open_ui",Value="main"}]}}}});
        DeleteScreenTo("second");if(project.Manifest.DefaultUi!="second" || ui.Events["open"].Client.Actions[0].Value!="second")throw new Exception("Screen deletion did not redirect links/main");history.Undo();if(!project.Screens.Any(s=>s.Id=="main"))throw new Exception("Screen deletion undo failed");
        dirty=false;System.IO.File.WriteAllText(output,"PASS: individual member movement/undo, isolation locking, scoped/additive marquee, component-screen separation, dropdown exit from source editing, deletion redirects and undo");Application.Current.Shutdown();
    }
}
public partial class MainWindow
{
    // Everything the canvas lets you pick: unlocked controls, limited to the isolated group when isolating.
    void SelectAll() {
        EndCanvasGesture();selected.Clear();
        selected.UnionWith(ui.Elements.Where(e=>!e.Locked && InIsolation(e)).Select(e=>e.Id));
        Draw();RefreshInspector();Surface.Focus();
    }
    // Pressing on the dark area around the screen starts a selection box too, so a drag can begin outside the canvas
    // and sweep across all of it. Clicks on the canvas itself and on the scrollbars are left alone.
    void InitializeOutsideMarquee() {
        CanvasScroll.PreviewMouseLeftButtonDown+=(_,e)=>{
            for(var node=e.OriginalSource as DependencyObject;node!=null;node=node is Visual || node is System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))
                if(ReferenceEquals(node,Surface) || node is System.Windows.Controls.Primitives.ScrollBar)return;
            if(e.ClickCount==2 && isolatedGroup.Length>0){ExitIsolation();e.Handled=true;return;}
            BeginMarquee(e.GetPosition(Surface),Keyboard.Modifiers);e.Handled=true;
        };
    }
}
