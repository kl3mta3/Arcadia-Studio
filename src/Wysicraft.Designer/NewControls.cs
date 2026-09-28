using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Wysicraft.Core;
using Wysicraft.Models;
namespace Wysicraft.Designer;

// Canvas drawing for sprites, shapes and colliders.
public partial class MainWindow
{
    FrameworkElement RenderNewControl(Element e)
    {
        double w = Math.Max(1, e.Bounds.Width) * Zoom, h = Math.Max(1, e.Bounds.Height) * Zoom;
        var grid = new Grid();
        switch (e.Type)
        {
            case "shape":
            {
                var path = new Path { Data = ShapeGeometry(e.Shape, w, h), Fill = SkinBrush(e), Stretch = Stretch.None, StrokeLineJoin = PenLineJoin.Round };
                if (e.BorderWidth > 0) { path.Stroke = Brush(e.BorderColor); path.StrokeThickness = Math.Clamp(e.BorderWidth, 0, 32) * Zoom; }
                grid.Children.Add(path);
                if (e.Text.Length > 0 && e.Text != "Shape") grid.Children.Add(new ContentPresenter { Content = StyledText(e), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                break;
            }
            case "sprite":
            {
                if (!TryTexture(e.Texture, out var png)) { grid.Children.Add(new TextBlock { Text = "▦ sprite", Foreground = Brushes.LightGray, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }); break; }
                var image = new Image { Stretch = Stretch.Fill }; RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                SetSpriteFrames(image, DecodeTexture(png), e); grid.Children.Add(image);
                break;
            }
            case "collider":
            {
                // Colliders are invisible in apps; on the canvas they show as a see-through hatched outline.
                var hatch = new DrawingBrush(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(70, 20, 200, 255)), null, new RectangleGeometry(new Rect(0, 0, 6, 6)))) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 8, 8), ViewportUnits = BrushMappingMode.Absolute };
                var outline = e.Collider switch
                {
                    "circle" => (Geometry)new EllipseGeometry(new Rect(0, 0, w, h)),
                    "polygon" when e.ColliderPoints.Count >= 3 => Polygon(e.ColliderPoints.Select(p => new System.Windows.Point(p.X * Zoom, p.Y * Zoom))),
                    _ => new RectangleGeometry(new Rect(0, 0, w, h))
                };
                grid.Children.Add(new Path { Data = outline, Fill = hatch, Stroke = new SolidColorBrush(Color.FromRgb(20, 200, 255)), StrokeThickness = 1.5, StrokeDashArray = [4, 2] });
                break;
            }
            case "particles":
            {
                // Emitters draw nothing in apps; on the canvas they show as a burst mark at the point they throw from,
                // so the spot can be seen and moved without guessing at an empty box.
                var ink = new SolidColorBrush(Color.FromRgb(120, 200, 255));
                var burst = new Path { Stroke = ink, StrokeThickness = 1.5, IsHitTestVisible = false, Data = Geometry.Parse("M8,0 L8,5 M8,11 L8,16 M0,8 L5,8 M11,8 L16,8 M2.5,2.5 L5.5,5.5 M10.5,10.5 L13.5,13.5 M13.5,2.5 L10.5,5.5 M5.5,10.5 L2.5,13.5"), Stretch = Stretch.Uniform, Margin = new Thickness(2) };
                grid.Children.Add(new Ellipse { Stroke = ink, StrokeThickness = 1, StrokeDashArray = [2, 2], Opacity = 0.7, IsHitTestVisible = false });
                grid.Children.Add(burst);
                if (e.Effect.Length > 0) grid.Children.Add(new TextBlock { Text = e.Effect, Foreground = ink, FontSize = 10, Margin = new Thickness(0, -13, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false });
                break;
            }
            case "tilemap":
            {
                var painted = TilemapImage(e);
                if (painted == null) { grid.Children.Add(new TextBlock { Text = "▦ tilemap", Foreground = Brushes.LightGray, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }); break; }
                var image = new Image { Source = painted, Stretch = Stretch.Fill }; RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                grid.Children.Add(image);
                break;
            }
            case "camera":
            {
                // Cameras draw nothing in apps; on the canvas they show as a dashed view frame with a zoom note.
                // Only the frame is solid, so clicks inside reach the controls underneath.
                var frame = new SolidColorBrush(Color.FromRgb(255, 176, 64));
                grid.Children.Add(new Rectangle { Stroke = frame, StrokeThickness = 2, StrokeDashArray = [5, 3], IsHitTestVisible = false });
                double zoom = Math.Min(ui.Size.Width / Math.Max(1, e.Bounds.Width), ui.Size.Height / Math.Max(1, e.Bounds.Height));
                grid.Children.Add(new TextBlock { Text = "📷 " + e.Id + (Math.Abs(zoom - 1) > 0.01 ? $"  ×{zoom:0.##}" : ""), Foreground = frame, FontSize = 11, Margin = new Thickness(4, 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false });
                break;
            }
        }
        return grid;
    }
    // A tilemap painted once into a single bitmap, rather than a control per tile: a full 256x256 map is 65,536 tiles,
    // and the canvas would not survive that as elements. The picture is kept until the map, its sheet or its size
    // changes, so dragging a map around costs nothing.
    readonly Dictionary<string, ImageSource?> tilemapPictures = [];
    ImageSource? TilemapImage(Element e)
    {
        string key = string.Join('\u0001', e.Texture, e.Tiles, e.Columns, e.Rows, e.TileWidth, e.TileHeight);
        if (tilemapPictures.TryGetValue(key, out var cached)) return cached;
        if (tilemapPictures.Count > 32) tilemapPictures.Clear();
        return tilemapPictures[key] = PaintTilemap(e);
    }
    ImageSource? PaintTilemap(Element e)
    {
        if (!TryTexture(e.Texture, out var png)) return null;
        var sheet = DecodeTexture(png);
        int tw = Math.Clamp(e.TileWidth, 1, sheet.PixelWidth), th = Math.Clamp(e.TileHeight, 1, sheet.PixelHeight);
        int across = Math.Max(1, sheet.PixelWidth / tw), down = Math.Max(1, sheet.PixelHeight / th);
        int columns = Math.Max(1, e.Columns), rows = Math.Max(1, e.Rows);
        // The picture is capped: a 256x256 map of 16px tiles would otherwise be a 4096x4096 bitmap, 67 MB of it.
        double scale = Math.Min(1, 2048.0 / Math.Max(columns * tw, rows * th));
        var grid = Tilemaps.Read(e.Tiles, columns, rows);
        var tiles = new Dictionary<int, CroppedBitmap>();
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            for (int r = 0; r < rows; r++) for (int c = 0; c < columns; c++)
            {
                int index = grid[r * columns + c];
                if (index < 0 || index >= across * down) continue;
                if (!tiles.TryGetValue(index, out var tile))
                {
                    tile = new CroppedBitmap(sheet, new Int32Rect(index % across * tw, index / across * th, tw, th));
                    tile.Freeze(); tiles[index] = tile;
                }
                dc.DrawImage(tile, new Rect(c * tw * scale, r * th * scale, tw * scale, th * scale));
            }
        var picture = new RenderTargetBitmap(Math.Max(1, (int)Math.Round(columns * tw * scale)), Math.Max(1, (int)Math.Round(rows * th * scale)), 96, 96, PixelFormats.Pbgra32);
        picture.Render(visual); picture.Freeze();
        return picture;
    }
    static Geometry Polygon(IEnumerable<System.Windows.Point> points)
    {
        var list = points.ToList(); var figure = new PathFigure { StartPoint = list[0], IsClosed = true, IsFilled = true };
        figure.Segments.Add(new PolyLineSegment(list.Skip(1), true)); var geometry = new PathGeometry([figure]); geometry.Freeze(); return geometry;
    }
    static Geometry ShapeGeometry(string shape, double w, double h) => Polygon(Shapes.Outline(shape).Select(p => new System.Windows.Point(p.X * w, p.Y * h)));

    // A sprite shows its current clip (Value), or its first clip, or frame 0; clips animate on the canvas too.
    void SetSpriteFrames(Image image, BitmapSource sheet, Element e)
    {
        int fw = Math.Clamp(e.FrameWidth, 1, sheet.PixelWidth), fh = Math.Clamp(e.FrameHeight, 1, sheet.PixelHeight), columns = Math.Max(1, sheet.PixelWidth / fw), count = columns * Math.Max(1, sheet.PixelHeight / fh);
        BitmapSource Frame(int index) { index = Math.Clamp(index, 0, count - 1); var crop = new CroppedBitmap(sheet, new Int32Rect(index % columns * fw, index / columns * fh, fw, fh)); crop.Freeze(); return crop; }
        Dictionary<string, SpriteClips.Clip> clips; try { clips = SpriteClips.Parse(e.Clips); } catch { clips = []; }
        var clip = clips.GetValueOrDefault(e.Value) ?? clips.Values.FirstOrDefault();
        if (clip == null) { image.Source = Frame(0); return; }
        image.Source = Frame(clip.Frames[0]);
        if (clip.Frames.Length < 2) return;
        var animation = new ObjectAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(clip.Frames.Length / clip.Fps), RepeatBehavior = clip.Loop ? RepeatBehavior.Forever : new RepeatBehavior(1), FillBehavior = FillBehavior.HoldEnd };
        for (int i = 0; i < clip.Frames.Length; i++) animation.KeyFrames.Add(new DiscreteObjectKeyFrame(Frame(clip.Frames[i]), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(i / clip.Fps))));
        image.BeginAnimation(Image.SourceProperty, animation);
    }
}
