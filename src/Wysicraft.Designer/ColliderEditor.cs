using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using Wysicraft.Core;
using Wysicraft.Models;
namespace Wysicraft.Designer;

// Collider editor (web and desktop): draw a polygon collider's outline over a trace image.
// Click to add a point after the selected one, drag points to move them, right-click to delete, C to turn the
// selected point into a curve handle (and back). The trace image can be moved, scaled and faded, then frozen.
public partial class MainWindow
{
    void ShowColliderEditor(Element element)
    {
        var points = element.ColliderPoints.Select(p => new Vertex { X = p.X, Y = p.Y, Curve = p.Curve }).ToList();
        if (points.Count < 3) points = DefaultPolygon(element);
        double w = Math.Max(1, element.Bounds.Width), h = Math.Max(1, element.Bounds.Height);
        const double margin = 40;
        double scale = Math.Clamp(Math.Min(560 / w, 420 / h), 0.5, 16);
        var window = new Window { Owner = this, Title = "Collider editor — " + element.Id, Width = Math.Max(760, w * scale + 2 * margin + 260), Height = Math.Max(560, h * scale + 2 * margin + 90), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel(); window.Content = root;
        var side = new StackPanel { Width = 240, Margin = new Thickness(8) }; DockPanel.SetDock(side, Dock.Right); root.Children.Add(side);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 4, 8, 8) }; DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var canvas = new Canvas { Width = w * scale + 2 * margin, Height = h * scale + 2 * margin, Background = new SolidColorBrush(Color.FromRgb(36, 40, 48)), ClipToBounds = true, Focusable = true };
        root.Children.Add(new ScrollViewer { Content = canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        System.Windows.Point ToScreen(double x, double y) => new(margin + x * scale, margin + y * scale);
        (double X, double Y) ToLocal(System.Windows.Point p) => (Math.Round((p.X - margin) / scale, 1), Math.Round((p.Y - margin) / scale, 1));

        // Trace image: under everything, movable and scalable until frozen.
        var trace = new Image { Opacity = 0.5, Stretch = Stretch.Fill, IsHitTestVisible = false }; RenderOptions.SetBitmapScalingMode(trace, BitmapScalingMode.NearestNeighbor);
        double traceX = 0, traceY = 0, traceScale = 1; bool frozen = false;
        if (TryTexture(element.Texture, out var own)) trace.Source = DecodeTexture(own);
        canvas.Children.Add(trace);
        var bounds = new Rectangle { Width = w * scale, Height = h * scale, Stroke = Brushes.Gray, StrokeDashArray = [3, 3], IsHitTestVisible = false }; Canvas.SetLeft(bounds, margin); Canvas.SetTop(bounds, margin); canvas.Children.Add(bounds);
        var shape = new Path { Fill = new SolidColorBrush(Color.FromArgb(60, 20, 200, 255)), Stroke = new SolidColorBrush(Color.FromRgb(20, 200, 255)), StrokeThickness = 2, IsHitTestVisible = false }; canvas.Children.Add(shape);
        var guides = new Path { Stroke = Brushes.Orange, StrokeThickness = 1, StrokeDashArray = [2, 2], IsHitTestVisible = false }; canvas.Children.Add(guides);
        var handles = new List<FrameworkElement>(); int selectedPoint = points.Count - 1, dragging = -1; bool movingTrace = false; System.Windows.Point traceGrab = default;

        void PlaceTrace()
        {
            if (trace.Source is not BitmapSource b) return;
            trace.Width = b.PixelWidth * traceScale * scale; trace.Height = b.PixelHeight * traceScale * scale;
            Canvas.SetLeft(trace, margin + traceX * scale); Canvas.SetTop(trace, margin + traceY * scale);
        }
        void Redraw()
        {
            foreach (var hnd in handles) canvas.Children.Remove(hnd); handles.Clear();
            var outline = Colliders.Flatten(points).Select(p => ToScreen(p.X, p.Y)).ToList();
            shape.Data = outline.Count >= 3 ? Polygon(outline) : null;
            var guide = new GeometryGroup();
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i]; var at = ToScreen(p.X, p.Y);
                if (p.Curve) { var prev = points[(i - 1 + points.Count) % points.Count]; var next = points[(i + 1) % points.Count]; guide.Children.Add(new LineGeometry(ToScreen(prev.X, prev.Y), at)); guide.Children.Add(new LineGeometry(at, ToScreen(next.X, next.Y))); }
                FrameworkElement handle = p.Curve ? new Ellipse { Width = 10, Height = 10 } : new Rectangle { Width = 10, Height = 10 };
                ((Shape)handle).Fill = i == selectedPoint ? Brushes.White : p.Curve ? Brushes.Orange : new SolidColorBrush(Color.FromRgb(20, 200, 255));
                ((Shape)handle).Stroke = Brushes.Black; handle.Cursor = Cursors.SizeAll; handle.Tag = i; handle.ToolTip = $"Point {i + 1}: {p.X:0.#}, {p.Y:0.#}{(p.Curve ? " (curve handle)" : "")}";
                Canvas.SetLeft(handle, at.X - 5); Canvas.SetTop(handle, at.Y - 5); canvas.Children.Add(handle); handles.Add(handle);
            }
            guides.Data = guide;
            string? problem = Colliders.Problem(points);
            status.Text = problem == null ? $"✓ Closed shape with {points.Count(p => !p.Curve)} corners{(points.Any(p => p.Curve) ? $" and {points.Count(p => p.Curve)} curve handle(s)" : "")}. Click to add a point after the white one; drag to move; right-click to delete; C makes a curve handle."
                : "✗ The outline " + problem + ". Fix it before closing, or the project won't validate.";
            status.Foreground = problem == null ? Brushes.LightGreen : Brushes.IndianRed;
        }
        int HandleAt(object source) => source is FrameworkElement { Tag: int index } ? index : -1;
        canvas.MouseLeftButtonDown += (_, e) =>
        {
            canvas.Focus(); var pos = e.GetPosition(canvas); int hit = HandleAt(e.OriginalSource);
            if (hit >= 0) { selectedPoint = dragging = hit; canvas.CaptureMouse(); Redraw(); return; }
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && !frozen && trace.Source != null) { movingTrace = true; traceGrab = pos; canvas.CaptureMouse(); return; }
            var (x, y) = ToLocal(pos); points.Insert(selectedPoint + 1, new Vertex { X = x, Y = y }); selectedPoint++; Redraw();
        };
        canvas.MouseMove += (_, e) =>
        {
            var pos = e.GetPosition(canvas);
            if (dragging >= 0) { var (x, y) = ToLocal(pos); points[dragging].X = x; points[dragging].Y = y; Redraw(); }
            else if (movingTrace) { traceX += (pos.X - traceGrab.X) / scale; traceY += (pos.Y - traceGrab.Y) / scale; traceGrab = pos; PlaceTrace(); }
        };
        canvas.MouseLeftButtonUp += (_, _) => { dragging = -1; movingTrace = false; canvas.ReleaseMouseCapture(); };
        canvas.MouseRightButtonDown += (_, e) => { int hit = HandleAt(e.OriginalSource); if (hit >= 0 && points.Count > 3) { points.RemoveAt(hit); selectedPoint = Math.Min(selectedPoint, points.Count - 1); Redraw(); } };
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.C && selectedPoint >= 0 && selectedPoint < points.Count) { points[selectedPoint].Curve = !points[selectedPoint].Curve; Redraw(); e.Handled = true; }
            if (e.Key == Key.Delete && selectedPoint >= 0 && points.Count > 3) { points.RemoveAt(selectedPoint); selectedPoint = Math.Min(selectedPoint, points.Count - 1); Redraw(); e.Handled = true; }
        };

        // Side panel: trace image and shape tools.
        side.Children.Add(new TextBlock { Text = "Trace image", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var pickAsset = new ComboBox { ToolTip = "An image from this project to trace over" };
        var assetPaths = project.Assets.Keys.Where(k => k.EndsWith(".png")).OrderBy(k => k).ToList();
        pickAsset.ItemsSource = new[] { "(choose a project image)" }.Concat(assetPaths.Select(System.IO.Path.GetFileName)).ToList(); pickAsset.SelectedIndex = 0;
        pickAsset.SelectionChanged += (_, _) => { if (pickAsset.SelectedIndex > 0) { trace.Source = DecodeTexture(project.Assets[assetPaths[pickAsset.SelectedIndex - 1]]); PlaceTrace(); } };
        side.Children.Add(pickAsset);
        var file = new Button { Content = "Open image file…", Margin = new Thickness(0, 4, 0, 0) };
        file.Click += (_, _) => Guard(() => { var d = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif" }; if (d.ShowDialog(window) == true) { var b = new BitmapImage(); b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad; b.UriSource = new Uri(d.FileName); b.EndInit(); b.Freeze(); trace.Source = b; PlaceTrace(); } });
        side.Children.Add(file);
        side.Children.Add(new TextBlock { Text = "Size", Margin = new Thickness(0, 8, 0, 0) });
        var size = new Slider { Minimum = 0.1, Maximum = 8, Value = 1, ToolTip = "Trace image scale" }; size.ValueChanged += (_, _) => { if (!frozen) { traceScale = size.Value; PlaceTrace(); } }; side.Children.Add(size);
        side.Children.Add(new TextBlock { Text = "Fade", Margin = new Thickness(0, 4, 0, 0) });
        var fade = new Slider { Minimum = 0.05, Maximum = 1, Value = 0.5 }; fade.ValueChanged += (_, _) => trace.Opacity = fade.Value; side.Children.Add(fade);
        side.Children.Add(new TextBlock { Text = "Alt+drag moves the image.", Opacity = 0.65, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
        var freeze = new CheckBox { Content = "Freeze image", Margin = new Thickness(0, 6, 0, 0), ToolTip = "Stop the image moving or scaling while you trace" };
        freeze.Checked += (_, _) => { frozen = true; size.IsEnabled = false; }; freeze.Unchecked += (_, _) => { frozen = false; size.IsEnabled = true; }; side.Children.Add(freeze);
        side.Children.Add(new TextBlock { Text = "Shape", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 4) });
        var curve = new Button { Content = "Selected point ⇄ curve handle (C)" }; curve.Click += (_, _) => { if (selectedPoint >= 0) { points[selectedPoint].Curve = !points[selectedPoint].Curve; Redraw(); } }; side.Children.Add(curve);
        var box = new Button { Content = "Reset to the control's box", Margin = new Thickness(0, 4, 0, 0) };
        box.Click += (_, _) => { points = [new() { X = 0, Y = 0 }, new() { X = w, Y = 0 }, new() { X = w, Y = h }, new() { X = 0, Y = h }]; selectedPoint = 3; Redraw(); }; side.Children.Add(box);
        var circle = new Button { Content = "Reset to an oval", Margin = new Thickness(0, 4, 0, 0) };
        circle.Click += (_, _) => { points = Enumerable.Range(0, 16).Select(i => new Vertex { X = Math.Round(w / 2 + w / 2 * Math.Cos(i * Math.PI / 8), 1), Y = Math.Round(h / 2 + h / 2 * Math.Sin(i * Math.PI / 8), 1) }).ToList(); selectedPoint = 15; Redraw(); }; side.Children.Add(circle);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) }; side.Children.Add(buttons);
        var apply = new Button { Content = "Apply", IsDefault = true, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 3, 12, 3) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(12, 3, 12, 3) };
        apply.Click += (_, _) => Guard(() =>
        {
            if (Colliders.Problem(points) is string problem) throw new InvalidOperationException("The outline " + problem + ".");
            if (!ui.Elements.Contains(element)) throw new InvalidOperationException("The control was removed.");
            Change(); element.Collider = "polygon"; element.ColliderPoints = points; window.Close(); Draw(); RefreshInspector();
        });
        cancel.Click += (_, _) => window.Close();
        buttons.Children.Add(apply); buttons.Children.Add(cancel);
        PlaceTrace(); Redraw(); window.ShowDialog();
    }
}
