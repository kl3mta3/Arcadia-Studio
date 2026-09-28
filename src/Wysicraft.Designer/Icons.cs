using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Wysicraft.Designer;

// Small monochrome line icons drawn on a 16×16 grid, so the editor doesn't depend on icon fonts.
// Each icon is a stroked outline plus an optional filled part.
static class Icons
{
    static readonly Dictionary<string,(string Stroke,string Fill)> Shapes = new() {
        // Toolbar and commands
        ["preview"]=("","M4,2.5 L13,8 L4,13.5 Z"),
        ["test"]=("M8,1.5 L14,4.5 L14,11.5 L8,14.5 L2,11.5 L2,4.5 Z M2,4.5 L8,7.5 L14,4.5 M8,7.5 L8,14.5",""),
        ["validate"]=("M8,1.5 A6.5,6.5 0 1 1 7.99,1.5 Z M5,8.2 L7.2,10.4 L11,5.8",""),
        ["export"]=("M8,10 L8,1.5 M4.5,5 L8,1.5 L11.5,5 M2.5,9 L2.5,14 L13.5,14 L13.5,9",""),
        ["kubejs"]=("M6,2 C4,2 4,3 4,5 C4,7 3,8 2,8 C3,8 4,9 4,11 C4,13 4,14 6,14 M10,2 C12,2 12,3 12,5 C12,7 13,8 14,8 C13,8 12,9 12,11 C12,13 12,14 10,14",""),
        ["mcp"]=("","M8,1 L9.5,6.5 L15,8 L9.5,9.5 L8,15 L6.5,9.5 L1,8 L6.5,6.5 Z M13,1 L13.6,2.4 L15,3 L13.6,3.6 L13,5 L12.4,3.6 L11,3 L12.4,2.4 Z"),
        ["add"]=("M8,2.5 L8,13.5 M2.5,8 L13.5,8",""),
        ["delete"]=("M2.5,4 L13.5,4 M6,4 L6,2 L10,2 L10,4 M4,4 L4.8,14.5 L11.2,14.5 L12,4 M6.8,6.5 L6.8,12 M9.2,6.5 L9.2,12",""),
        ["settings"]=("M1.5,4 L14.5,4 M1.5,8 L14.5,8 M1.5,12 L14.5,12","M4,2.5 L6,2.5 L6,5.5 L4,5.5 Z M9,6.5 L11,6.5 L11,9.5 L9,9.5 Z M6,10.5 L8,10.5 L8,13.5 L6,13.5 Z"),
        ["up"]=("M8,13.5 L8,2.5 M3.5,7 L8,2.5 L12.5,7",""),
        ["down"]=("M8,2.5 L8,13.5 M3.5,9 L8,13.5 L12.5,9",""),
        ["top"]=("M3,2.5 L13,2.5 M8,14 L8,5 M4,9 L8,5 L12,9",""),
        ["bottom"]=("M3,13.5 L13,13.5 M8,2 L8,11 M4,7 L8,11 L12,7",""),
        ["duplicate"]=("M5.5,5.5 L14.5,5.5 L14.5,14.5 L5.5,14.5 Z M10.5,5.5 L10.5,1.5 L1.5,1.5 L1.5,10.5 L5.5,10.5",""),
        ["group"]=("M1.5,5 L1.5,1.5 L5,1.5 M11,1.5 L14.5,1.5 L14.5,5 M14.5,11 L14.5,14.5 L11,14.5 M5,14.5 L1.5,14.5 L1.5,11 M4.5,4.5 L9,4.5 L9,9 L4.5,9 Z M7,7 L11.5,7 L11.5,11.5 L7,11.5 Z",""),
        ["ungroup"]=("M1.5,1.5 L7.5,1.5 L7.5,7.5 L1.5,7.5 Z M8.5,8.5 L14.5,8.5 L14.5,14.5 L8.5,14.5 Z",""),
        ["eye"]=("M1,8 C3,4.2 5.5,3 8,3 C10.5,3 13,4.2 15,8 C13,11.8 10.5,13 8,13 C5.5,13 3,11.8 1,8 Z M8,5.8 A2.2,2.2 0 1 1 7.99,5.8 Z",""),
        ["eye-off"]=("M1,8 C3,4.2 5.5,3 8,3 C10.5,3 13,4.2 15,8 C13,11.8 10.5,13 8,13 C5.5,13 3,11.8 1,8 Z M2,2 L14,14",""),
        ["lock"]=("M3.5,7.5 L12.5,7.5 L12.5,14.5 L3.5,14.5 Z M5.5,7.5 L5.5,5 C5.5,1.8 10.5,1.8 10.5,5 L10.5,7.5",""),
        ["unlock"]=("M3.5,7.5 L12.5,7.5 L12.5,14.5 L3.5,14.5 Z M5.5,7.5 L5.5,5 C5.5,1.8 10.5,1.8 10.5,4.2",""),
        ["folder"]=("M1.5,3.5 L6,3.5 L7.5,5 L14.5,5 L14.5,13.5 L1.5,13.5 Z",""),
        ["keyboard"]=("M1.5,4 L14.5,4 L14.5,12 L1.5,12 Z M5,10 L11,10","M3.5,5.8 L5,5.8 L5,7.3 L3.5,7.3 Z M6.5,5.8 L8,5.8 L8,7.3 L6.5,7.3 Z M9.5,5.8 L11,5.8 L11,7.3 L9.5,7.3 Z M12.5,5.8 L13,5.8 L13,7.3 L12.5,7.3 Z"),
        // Control types
        ["button"]=("M1.5,4.5 L14.5,4.5 L14.5,11.5 L1.5,11.5 Z M5,8 L11,8",""),
        ["label"]=("M3,14 L8,2 L13,14 M5,10 L11,10",""),
        ["image"]=("M1.5,2.5 L14.5,2.5 L14.5,13.5 L1.5,13.5 Z M1.5,11 L6,7 L9,10 L11,8 L14.5,11","M10.5,4.5 A1.2,1.2 0 1 1 10.49,4.5 Z"),
        ["textbox"]=("M1.5,4.5 L14.5,4.5 L14.5,11.5 L1.5,11.5 Z M4.5,6 L4.5,10",""),
        ["checkbox"]=("M2.5,2.5 L13.5,2.5 L13.5,13.5 L2.5,13.5 Z M4.5,8 L7,10.5 L11.5,5.5",""),
        ["slider"]=("M1,8 L15,8","M4.5,4.5 L7.5,4.5 L7.5,11.5 L4.5,11.5 Z"),
        ["progress"]=("M1.5,5.5 L14.5,5.5 L14.5,10.5 L1.5,10.5 Z","M1.5,5.5 L9,5.5 L9,10.5 L1.5,10.5 Z"),
        ["dropdown"]=("M1.5,4.5 L14.5,4.5 L14.5,11.5 L1.5,11.5 Z M9.5,7 L11,8.5 L12.5,7",""),
        ["panel"]=("M1.5,1.5 L14.5,1.5 L14.5,14.5 L1.5,14.5 Z M1.5,4.5 L14.5,4.5",""),
        ["scroll_panel"]=("M1.5,1.5 L14.5,1.5 L14.5,14.5 L1.5,14.5 Z M11.5,1.5 L11.5,14.5","M12.2,3 L13.8,3 L13.8,7 L12.2,7 Z"),
        ["item"]=("M5,2 L11,2 L14,6 L8,14 L2,6 Z M2,6 L14,6 M5,2 L8,6 L11,2",""),
        ["item_list"]=("M1.5,3 L3,3 M5,3 L14.5,3 M1.5,8 L3,8 M5,8 L14.5,8 M1.5,13 L3,13 M5,13 L14.5,13",""),
        ["texture_region"]=("M1.5,1.5 L14.5,1.5 L14.5,14.5 L1.5,14.5 Z M6,1.5 L6,14.5 M10.5,1.5 L10.5,14.5 M1.5,6 L14.5,6 M1.5,10.5 L14.5,10.5",""),
        ["sprite"]=("M2.5,2.5 L13.5,2.5 L13.5,13.5 L2.5,13.5 Z M6.5,2.5 L6.5,13.5 M10,2.5 L10,13.5","M4.5,6 L4.5,10 M8.25,5 L8.25,11 M11.75,7 L11.75,9"),
        ["shape"]=("M8,1.5 L9.9,6 L14.5,6.3 L11,9.3 L12.1,14 L8,11.5 L3.9,14 L5,9.3 L1.5,6.3 L6.1,6 Z",""),
        ["rectangle"]=("M1.5,3.5 L14.5,3.5 L14.5,12.5 L1.5,12.5 Z",""),
        ["ellipse"]=("M1.5,8 A6.5,5 0 1 1 14.5,8 A6.5,5 0 1 1 1.5,8 Z",""),
        ["triangle"]=("M8,2 L14.5,14 L1.5,14 Z",""),
        ["diamond"]=("M8,1.5 L14.5,8 L8,14.5 L1.5,8 Z",""),
        ["hexagon"]=("M4.75,2 L11.25,2 L14.5,8 L11.25,14 L4.75,14 L1.5,8 Z",""),
        ["star"]=("M8,1.5 L9.9,6 L14.5,6.3 L11,9.3 L12.1,14 L8,11.5 L3.9,14 L5,9.3 L1.5,6.3 L6.1,6 Z",""),
        ["collider"]=("M2,2 L14,2 L14,14 L2,14 Z M2,6 L6,2 M2,10 L10,2 M2,14 L14,2 M6,14 L14,6 M10,14 L14,10",""),
        ["collider_circle"]=("M1.5,8 A6.5,6.5 0 1 1 14.5,8 A6.5,6.5 0 1 1 1.5,8 Z M4,12 L12,4",""),
        ["collider_polygon"]=("M2,12 L5,3 L12,2 L14.5,9 L8,14.5 Z M2,12 L12,2",""),
        ["tilemap"]=("M1.5,1.5 L14.5,1.5 L14.5,14.5 L1.5,14.5 Z M5.8,1.5 L5.8,14.5 M10.2,1.5 L10.2,14.5 M1.5,5.8 L14.5,5.8 M1.5,10.2 L14.5,10.2","M5.8,5.8 L10.2,5.8 L10.2,10.2 L5.8,10.2 Z"),
        ["particles"]=("M8,2.2 L9,5 L8,7.8 L7,5 Z M13.8,8 L11,9 L8.2,8 L11,7 Z M8,13.8 L7,11 L8,8.2 L9,11 Z M2.2,8 L5,7 L7.8,8 L5,9 Z","M12.4,3.6 A0.9,0.9 0 1 1 12.39,3.6 Z M3.6,12.4 A0.9,0.9 0 1 1 3.59,12.4 Z M12.4,12.4 A0.7,0.7 0 1 1 12.39,12.4 Z M3.6,3.6 A0.7,0.7 0 1 1 3.59,3.6 Z"),
        ["camera"]=("M1.5,5 L4.5,5 L6,3 L10,3 L11.5,5 L14.5,5 L14.5,13 L1.5,13 Z","M8,6.3 A2.7,2.7 0 1 1 7.99,6.3 Z"),
        ["advanced"]=("M8,1.5 L9.2,4.1 L12,3.6 L11.5,6.4 L14.1,7.6 L11.5,8.8 L12,11.6 L9.2,11.1 L8,13.7 L6.8,11.1 L4,11.6 L4.5,8.8 L1.9,7.6 L4.5,6.4 L4,3.6 L6.8,4.1 Z","M8,5.8 A1.8,1.8 0 1 1 7.99,5.8 Z"),
        ["input"]=("M1.5,5 L14.5,5 L14.5,12 L1.5,12 Z M4,8.5 L7,8.5 M5.5,7 L5.5,10 M10.5,7.5 A0.6,0.6 0 1 1 10.49,7.5 M12,9.5 A0.6,0.6 0 1 1 11.99,9.5",""),
        ["animation"]=("M1.5,3.5 L14.5,3.5 L14.5,12.5 L1.5,12.5 Z M1.5,9 L14.5,9","M4,9 L4,11 M8,9 L8,11 M12,9 L12,11"),
        // Pixel editor tools
        ["tool_pencil"]=("M2.5,13.5 L3.2,10.3 L10.8,2.7 L13.3,5.2 L5.7,12.8 Z M9.3,4.2 L11.8,6.7",""),
        ["tool_eraser"]=("M2.8,10.2 L8.6,4.4 L12.8,8.6 L8.4,13 L5.6,13 Z M5.7,7.3 L9.9,11.5 M8.4,13 L14,13",""),
        ["tool_fill"]=("M2.5,8.5 L7.5,3.5 L12.5,8.5 L7.5,13.5 Z M2.5,8.5 L12.5,8.5 M5.5,5.5 L3,3","M13.6,9.8 L14.9,12.3 A1.45,1.45 0 1 1 12.3,12.3 Z"),
        ["tool_line"]=("M2.5,13.5 L13.5,2.5","M1.3,12.3 L3.7,12.3 L3.7,14.7 L1.3,14.7 Z M12.3,1.3 L14.7,1.3 L14.7,3.7 L12.3,3.7 Z"),
        ["tool_select"]=("M2,2 L5,2 M7,2 L9,2 M11,2 L14,2 L14,5 M14,7 L14,9 M14,11 L14,14 L11,14 M9,14 L7,14 M5,14 L2,14 L2,11 M2,9 L2,7 M2,5 L2,2",""),
        ["tool_wand"]=("M2.5,13.5 L9.5,6.5 M11,1.5 L11,3.8 M13.8,2.2 L12.2,3.8 M14.5,5 L12.2,5 M8.2,2.2 L9.8,3.8",""),
        ["tool_move"]=("M8,1.5 L8,14.5 M1.5,8 L14.5,8 M6,3.5 L8,1.5 L10,3.5 M6,12.5 L8,14.5 L10,12.5 M3.5,6 L1.5,8 L3.5,10 M12.5,6 L14.5,8 L12.5,10",""),
        ["tool_picker"]=("M2.5,13.5 L3.3,10.7 L9.5,4.5 L11.5,6.5 L5.3,12.7 Z M9.5,4.5 L11,3 A1.9,1.9 0 0 1 13,5 L11.5,6.5",""),
        ["sound"]=("M2,6 L5,6 L9,2.5 L9,13.5 L5,10 L2,10 Z M11.5,5 A4,4 0 0 1 11.5,11 M13,3 A6.5,6.5 0 0 1 13,13",""),
    };
    public static bool Has(string name) => Shapes.ContainsKey(name);
    public static FrameworkElement Get(string name,Brush? brush=null,double size=14) {
        var ink=brush ?? new SolidColorBrush(Color.FromRgb(0xC9,0xD1,0xDC));
        var canvas=new Canvas {Width=16,Height=16};
        if(Shapes.TryGetValue(name,out var shape)) {
            if(shape.Fill.Length>0)canvas.Children.Add(new Path {Data=Geometry.Parse(shape.Fill),Fill=ink});
            if(shape.Stroke.Length>0)canvas.Children.Add(new Path {Data=Geometry.Parse(shape.Stroke),Stroke=ink,StrokeThickness=1.4,StrokeLineJoin=PenLineJoin.Round,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round});
        }
        return new Viewbox {Width=size,Height=size,Child=canvas,SnapsToDevicePixels=true,VerticalAlignment=VerticalAlignment.Center};
    }
    // Icon followed by a label, for buttons and list rows.
    public static StackPanel WithText(string icon,string text,Brush? brush=null) {
        var row=new StackPanel {Orientation=Orientation.Horizontal};
        row.Children.Add(Get(icon,brush));
        row.Children.Add(new TextBlock {Text=text,Margin=new Thickness(6,0,0,0),VerticalAlignment=VerticalAlignment.Center});
        return row;
    }
}
