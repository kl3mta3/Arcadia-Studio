using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Wysicraft.Core;
using Wysicraft.Models;
namespace Wysicraft.Designer;

// Live anchors: resizing a container or the screen in the editor moves its children the same way the runtime does.
public partial class MainWindow
{
    UiDefinition? resizeBase;
    static readonly Brush AnchorBrush=new SolidColorBrush(Color.FromRgb(255,170,60));
    // Repositions the handles of whatever is currently drawn, so a move drag doesn't have to rebuild the canvas.
    readonly List<Action> handlePlacers=[];
    void MoveCanvasOverlays() {
        foreach(var place in handlePlacers)place();
        foreach(var line in Surface.Children.OfType<Line>().ToList())Surface.Children.Remove(line);
        DrawAnchorMarkers();
    }

    // Eight handles (corners and edge midpoints). Dragging a left/top handle moves that edge and keeps the opposite one fixed.
    void AddResizeHandles(Element e) {
        var handles=new List<(System.Windows.Controls.Primitives.Thumb Thumb,int X,int Y)>();
        void Place() {foreach(var (t,hx,hy) in handles){Canvas.SetLeft(t,(e.Bounds.X+e.Bounds.Width*(hx+1)/2)*Zoom-5);Canvas.SetTop(t,(e.Bounds.Y+e.Bounds.Height*(hy+1)/2)*Zoom-5);}}
        handlePlacers.Add(Place);
        foreach(int hy in new[]{-1,0,1})foreach(int hx in new[]{-1,0,1}) {
            if(hx==0 && hy==0)continue;
            var thumb=new System.Windows.Controls.Primitives.Thumb {Width=9,Height=9,Background=Brushes.DeepSkyBlue,
                Cursor=hx==0?Cursors.SizeNS:hy==0?Cursors.SizeWE:hx==hy?Cursors.SizeNWSE:Cursors.SizeNESW,ToolTip="Resize • Ctrl: leave children in place"};
            Panel.SetZIndex(thumb,1000);Surface.Children.Add(thumb);handles.Add((thumb,hx,hy));
            thumb.DragStarted+=(_,_)=>{Change();resizeBase=Json.Clone(ui);};
            thumb.DragDelta+=(_,args)=>{
                double dx=args.HorizontalChange/Zoom,dy=args.VerticalChange/Zoom;
                if(hx==1)e.Bounds.Width=Math.Max(8,e.Bounds.Width+dx);
                if(hx==-1){double right=e.Bounds.X+e.Bounds.Width;e.Bounds.Width=Math.Max(8,e.Bounds.Width-dx);e.Bounds.X=right-e.Bounds.Width;}
                if(hy==1)e.Bounds.Height=Math.Max(8,e.Bounds.Height+dy);
                if(hy==-1){double bottom=e.Bounds.Y+e.Bounds.Height;e.Bounds.Height=Math.Max(8,e.Bounds.Height-dy);e.Bounds.Y=bottom-e.Bounds.Height;}
                ReflowResizedChildren(e);Place();
            };
            thumb.DragCompleted+=(_,_)=>{
                if(hx==1)e.Bounds.Width=Math.Max(8,Snap(e.Bounds.Width));
                if(hx==-1){double right=e.Bounds.X+e.Bounds.Width;e.Bounds.X=Snap(e.Bounds.X);e.Bounds.Width=Math.Max(8,right-e.Bounds.X);}
                if(hy==1)e.Bounds.Height=Math.Max(8,Snap(e.Bounds.Height));
                if(hy==-1){double bottom=e.Bounds.Y+e.Bounds.Height;e.Bounds.Y=Snap(e.Bounds.Y);e.Bounds.Height=Math.Max(8,bottom-e.Bounds.Y);}
                ReflowResizedChildren(e);resizeBase=null;Draw();RefreshInspector();
            };
        }
        Place();
    }

    // Canvas resize handle. Ctrl resizes the element alone, leaving its children where they are.
    void ReflowResizedChildren(Element e) {
        var changed=new List<string>{e.Id};
        if(resizeBase!=null) {
            var bounds=Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
                ? resizeBase.Elements.Where(d=>ContainerTree.Ancestors(resizeBase,d).Any(a=>a.Id==e.Id)).ToDictionary(d=>d.Id,d=>d.Bounds)
                : ResponsiveLayout.Reflow(resizeBase,e.Id,e.Bounds);
            foreach(var (id,b) in bounds) {var child=ui.Elements.FirstOrDefault(x=>x.Id==id);if(child==null)continue;child.Bounds=Json.Clone(b);changed.Add(id);}
        }
        UpdateCanvasBounds(changed);
    }

    // Width/Height typed in Properties, for the screen or a container. Always computed from the state before the edit so partial input doesn't compound.
    void ReflowAfterSizeEdit(UiDefinition before,object target,string name) {
        if(name is not ("Width" or "Height"))return;
        if(target is Wysicraft.Models.Size) {
            var bounds=ResponsiveLayout.Resolve(before,ui.Size.Width,ui.Size.Height);
            foreach(var e in ui.Elements)if(bounds.TryGetValue(e.Id,out var b))e.Bounds=Json.Clone(b);
        } else if(target is Bounds edited && ui.Elements.FirstOrDefault(e=>e.Bounds==edited) is Element owner) {
            foreach(var (id,b) in ResponsiveLayout.Reflow(before,owner.Id,owner.Bounds))
                if(ui.Elements.FirstOrDefault(x=>x.Id==id) is Element child)child.Bounds=Json.Clone(b);
        }
    }

    // Moves existing canvas borders without rebuilding the canvas, which would cancel an in-progress Thumb drag.
    void UpdateCanvasBounds(ICollection<string> ids) {
        var affected=new HashSet<string>(ids);
        var byId=new Dictionary<string,Element>(ui.Elements.Count);
        foreach(var e in ui.Elements)byId[e.Id]=e;
        foreach(var e in ui.Elements)if(ContainerTree.Ancestors(ui,e).Any(a=>affected.Contains(a.Id)))affected.Add(e.Id);
        foreach(var border in Surface.Children.OfType<Border>()) {
            if(border.Tag is not string id || !affected.Contains(id) || !byId.TryGetValue(id,out var e))continue;
            Canvas.SetLeft(border,e.Bounds.X*Zoom);Canvas.SetTop(border,e.Bounds.Y*Zoom);border.Width=e.Bounds.Width*Zoom;border.Height=e.Bounds.Height*Zoom;
            var rect=new Rect(e.Bounds.X*Zoom,e.Bounds.Y*Zoom,e.Bounds.Width*Zoom,e.Bounds.Height*Zoom);
            foreach(var a in ContainerTree.Ancestors(ui,e))rect.Intersect(new Rect(a.Bounds.X*Zoom,a.Bounds.Y*Zoom,a.Bounds.Width*Zoom,a.Bounds.Height*Zoom));
            border.Clip=new RectangleGeometry(rect.IsEmpty?default:new Rect(rect.X-e.Bounds.X*Zoom,rect.Y-e.Bounds.Y*Zoom,rect.Width,rect.Height));
        }
    }

    // Orange guides on a single selected control: a line to each parent edge it is pinned to, or the parent's center line.
    void DrawAnchorMarkers() {
        if(selected.Count!=1 || ui.Elements.FirstOrDefault(x=>selected.Contains(x.Id)) is not Element e || !InIsolation(e))return;
        var parent=ui.Elements.FirstOrDefault(p=>p.Id==e.Parent);
        var p=parent==null?new Rect(0,0,ui.Size.Width*Zoom,ui.Size.Height*Zoom):new Rect(parent.Bounds.X*Zoom,parent.Bounds.Y*Zoom,parent.Bounds.Width*Zoom,parent.Bounds.Height*Zoom);
        var r=new Rect(e.Bounds.X*Zoom,e.Bounds.Y*Zoom,e.Bounds.Width*Zoom,e.Bounds.Height*Zoom);
        double cx=r.X+r.Width/2,cy=r.Y+r.Height/2;
        void Guide(double x1,double y1,double x2,double y2,bool dashed) {
            var line=new Line {X1=x1,Y1=y1,X2=x2,Y2=y2,Stroke=AnchorBrush,StrokeThickness=1.5,IsHitTestVisible=false,StrokeDashArray=dashed?[3,2]:null};
            Panel.SetZIndex(line,999);Surface.Children.Add(line);
        }
        switch(e.HorizontalAnchor) {
            case "left":Guide(p.Left,cy,r.Left,cy,false);break;
            case "right":Guide(r.Right,cy,p.Right,cy,false);break;
            case "stretch":Guide(p.Left,cy,r.Left,cy,false);Guide(r.Right,cy,p.Right,cy,false);break;
            case "center":double mx=p.X+p.Width/2;Guide(mx,r.Top-6,mx,r.Bottom+6,true);break;
        }
        switch(e.VerticalAnchor) {
            case "top":Guide(cx,p.Top,cx,r.Top,false);break;
            case "bottom":Guide(cx,r.Bottom,cx,p.Bottom,false);break;
            case "stretch":Guide(cx,p.Top,cx,r.Top,false);Guide(cx,r.Bottom,cx,p.Bottom,false);break;
            case "center":double my=p.Y+p.Height/2;Guide(r.Left-6,my,r.Right+6,my,true);break;
        }
    }

    internal void VerifyLiveAnchors(string output) {
        project=new Project();project.Manifest.Snap=false;ui=project.Screens[0];ui.Size=new(){Width=320,Height=200};
        ui.Elements=[
            new(){Id="panel",Type="panel",Bounds=new(){X=10,Y=10,Width=200,Height=100}},
            new(){Id="close",Type="button",Parent="panel",HorizontalAnchor="right",Bounds=new(){X=180,Y=15,Width=20,Height=20}},
            new(){Id="bar",Type="progress",Parent="panel",HorizontalAnchor="stretch",VerticalAnchor="bottom",Bounds=new(){X=20,Y=90,Width=180,Height=10}},
            new(){Id="corner",Type="label",HorizontalAnchor="right",VerticalAnchor="bottom",Bounds=new(){X=290,Y=180,Width=20,Height=10}}];
        history.Clear();selected.Clear();selected.Add("panel");RefreshAll();
        Element E(string id)=>ui.Elements.Single(x=>x.Id==id);
        // Canvas resize handle, simulated: widen the panel 40 and heighten it 20.
        Change();resizeBase=Json.Clone(ui);E("panel").Bounds.Width=240;E("panel").Bounds.Height=120;ReflowResizedChildren(E("panel"));resizeBase=null;Draw();
        if(E("close").Bounds.X!=220 || E("bar").Bounds.Width!=220 || E("bar").Bounds.Y!=110 || E("corner").Bounds.X!=290)throw new Exception("Panel resize did not follow anchors");
        if(Canvas.GetLeft(Surface.Children.OfType<Border>().Single(b=>Equals(b.Tag,"close")))!=440)throw new Exception("Canvas did not redraw reflowed child");
        history.Undo();if(E("close").Bounds.X!=180 || E("bar").Bounds.Width!=180)throw new Exception("Resize reflow is not one undo step");history.Redo();
        // Typed screen size, with a transient value that must not compound.
        var before=Json.Clone(ui);ui.Size.Width=40;ReflowAfterSizeEdit(before,ui.Size,"Width");ui.Size.Width=420;ReflowAfterSizeEdit(before,ui.Size,"Width");
        if(E("corner").Bounds.X!=390 || E("close").Bounds.X!=220)throw new Exception("Screen resize did not follow anchors");
        // Typed panel width.
        before=Json.Clone(ui);E("panel").Bounds.Width=200;ReflowAfterSizeEdit(before,E("panel").Bounds,"Width");
        if(E("close").Bounds.X!=180 || E("bar").Bounds.Width!=180)throw new Exception("Typed panel width did not follow anchors");
        selected.Clear();selected.Add("close");Draw();if(!Surface.Children.OfType<Line>().Any())throw new Exception("Anchor guides missing");
        // Left-edge handle: drag it 20 GUI px left. The right edge stays put, and the right-anchored child stays in its corner.
        selected.Clear();selected.Add("panel");Draw();
        var thumbs=Surface.Children.OfType<System.Windows.Controls.Primitives.Thumb>().ToList();
        if(thumbs.Count!=8)throw new Exception("Expected eight resize handles, found "+thumbs.Count);
        var left=thumbs.Single(t=>t.Cursor==Cursors.SizeWE && Canvas.GetLeft(t)<E("panel").Bounds.X*Zoom);
        left.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0,0){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragStartedEvent});
        left.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(-20*Zoom,0){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragDeltaEvent});
        left.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0,0,false){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragCompletedEvent});
        if(E("panel").Bounds.X!=-10 || E("panel").Bounds.Width!=220 || E("close").Bounds.X!=180)throw new Exception($"Left-edge resize failed: x={E("panel").Bounds.X} w={E("panel").Bounds.Width} close={E("close").Bounds.X}");
        // Zoom is view-only: canvas coordinates don't change.
        ZoomActual();SetViewScale(2);if(viewTransform.ScaleX!=2 || Surface.Width!=ui.Size.Width*Zoom)throw new Exception("Zoom did not scale the view only");
        ZoomIn();ZoomIn();ZoomIn();ZoomIn();if(viewScale!=MaxViewScale)throw new Exception("Zoom is not clamped");
        ZoomFit();if(viewScale<=MinViewScale-0.001 || viewScale>MaxViewScale)throw new Exception("Fit zoom out of range");ZoomActual();
        // Five arrow-key nudges on one selection are one undo step.
        selected.Clear();selected.Add("corner");Draw();Surface.Focus();UpdateLayout();
        double startX=E("corner").Bounds.X;int stepsBefore=history.UndoCount;
        var source=PresentationSource.FromVisual(this)!;
        for(int i=0;i<5;i++)Surface.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,0,Key.Right){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        if(E("corner").Bounds.X!=startX+5)throw new Exception("Nudge did not move the selection");
        if(history.UndoCount!=stepsBefore+1)throw new Exception($"Nudges created {history.UndoCount-stepsBefore} undo steps");
        history.Undo();if(E("corner").Bounds.X!=startX)throw new Exception("One undo did not revert the nudge run");
        dirty=false;System.IO.File.WriteAllText(output,"PASS: panel resize reflow and canvas update, single undo step, typed screen and panel sizes, anchor guides, eight handles with left-edge resize, view-only zoom, merged nudges");Application.Current.Shutdown();
    }
}
