using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wysicraft.Core;
using Wysicraft.Models;
namespace Wysicraft.Designer;

// Tile painter (web and desktop): paint a tilemap's grid from its tile sheet.
// Pick a tile on the left, then paint with the left button and erase with the right. Fill floods a region of the
// same tile, and Pick takes the tile under the cursor. A tile marked solid stops a physics body.
//
// The map is drawn into one bitmap and single cells are written into it as they change, rather than the whole map
// being rebuilt on every stroke: a 256x256 map is 65,536 tiles and anything per-tile is too slow to paint with.
public partial class MainWindow
{
    enum TileTool { Paint, Fill, Erase, Pick }

    void ShowTilemapEditor(Element element)
    {
        if (element.Type != "tilemap") throw new InvalidOperationException("Select a Tilemap control first.");
        if (!TryTexture(element.Texture, out var png)) throw new InvalidOperationException("Give the tilemap a tile sheet texture first — pick one in the Inspector, or drag one in from Assets.");
        var sheet = DecodeTexture(png);
        int tileW = Math.Clamp(element.TileWidth, 1, sheet.PixelWidth), tileH = Math.Clamp(element.TileHeight, 1, sheet.PixelHeight);
        int across = Math.Max(1, sheet.PixelWidth / tileW), down = Math.Max(1, sheet.PixelHeight / tileH), tileCount = across * down;
        int columns = Math.Clamp(element.Columns, 1, Tilemaps.MaxColumns), rows = Math.Clamp(element.Rows, 1, Tilemaps.MaxRows);
        int[] grid = Tilemaps.Read(element.Tiles, columns, rows);
        var solid = Tilemaps.Solid(element.Solid);

        // The sheet's pixels once, so scaled tiles can be built without going through WPF for each one.
        var sheetPixels = new int[sheet.PixelWidth * sheet.PixelHeight];
        new FormatConvertedBitmap(sheet, PixelFormats.Bgra32, null, 0).CopyPixels(sheetPixels, sheet.PixelWidth * 4, 0);

        var window = new Window { Owner = this, Title = "Tile painter — " + element.Id, Width = 1000, Height = 700, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel(); window.Content = root;
        var side = new StackPanel { Width = 250, Margin = new Thickness(8) }; DockPanel.SetDock(side, Dock.Right); root.Children.Add(side);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 4, 8, 8), Opacity = 0.8 }; DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var surface = new Image { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12) };
        RenderOptions.SetBitmapScalingMode(surface, BitmapScalingMode.NearestNeighbor);
        var scroller = new ScrollViewer { Content = surface, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = new SolidColorBrush(Color.FromRgb(24, 27, 33)) };
        root.Children.Add(scroller);

        int selected = 0, cell = 0;
        var tool = TileTool.Paint;
        WriteableBitmap? picture = null;
        var blocks = new Dictionary<int, int[]>();   // tile index (-1 for empty) -> its pixels at the current cell size
        const int Empty = -1;

        // One tile scaled to the cell size, nearest-neighbour, with a grid line baked into its right and bottom edge
        // so painting a cell repaints its own gridline and nothing else has to be redrawn.
        int[] Block(int index)
        {
            if (blocks.TryGetValue(index, out var made)) return made;
            var block = new int[cell * cell];
            unchecked
            {
                int line = (int)0xFF20242B, empty = (int)0xFF1B1E24;
                if (index < 0 || index >= tileCount) Array.Fill(block, empty);
                else
                {
                    int sx = index % across * tileW, sy = index / across * tileH;
                    for (int y = 0; y < cell; y++)
                    {
                        int row = (sy + y * tileH / cell) * sheet.PixelWidth;
                        for (int x = 0; x < cell; x++) block[y * cell + x] = sheetPixels[row + sx + x * tileW / cell];
                    }
                }
                if (cell >= 6) { for (int i = 0; i < cell; i++) { block[i * cell + cell - 1] = line; block[(cell - 1) * cell + i] = line; } }
            }
            return blocks[index] = block;
        }
        void PaintCell(int c, int r)
        {
            if (picture == null || c < 0 || r < 0 || c >= columns || r >= rows) return;
            picture.WritePixels(new Int32Rect(c * cell, r * cell, cell, cell), Block(grid[r * columns + c]), cell * 4, 0);
        }
        void Rebuild(int size)
        {
            // The whole map at once would be a 4096x4096 bitmap for a full-size map, so the cell size is capped to
            // keep it under 2048 a side. The slider then zooms within what is left.
            cell = Math.Clamp(size, 2, 64);
            cell = Math.Max(2, Math.Min(cell, Math.Min(2048 / columns, 2048 / rows)));
            blocks.Clear();
            picture = new WriteableBitmap(columns * cell, rows * cell, 96, 96, PixelFormats.Bgra32, null);
            surface.Source = picture; surface.Width = picture.PixelWidth; surface.Height = picture.PixelHeight;
            for (int r = 0; r < rows; r++) for (int c = 0; c < columns; c++) PaintCell(c, r);
        }

        // ---- The tile palette ----
        var palette = new WrapPanel();
        var chips = new List<Border>();
        void RefreshPalette()
        {
            for (int i = 0; i < chips.Count; i++)
            {
                chips[i].BorderBrush = i == selected ? Brushes.DodgerBlue : Brushes.Transparent;
                chips[i].Background = solid.Contains(i) ? new SolidColorBrush(Color.FromRgb(120, 40, 40)) : Brushes.Transparent;
            }
        }
        for (int i = 0; i < Math.Min(tileCount, 1024); i++)
        {
            int index = i;
            var crop = new CroppedBitmap(sheet, new Int32Rect(index % across * tileW, index / across * tileH, tileW, tileH)); crop.Freeze();
            var picture2 = new Image { Source = crop, Width = 28, Height = 28, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(picture2, BitmapScalingMode.NearestNeighbor);
            var chip = new Border { Child = picture2, BorderThickness = new Thickness(2), Margin = new Thickness(1), Padding = new Thickness(2), ToolTip = "Tile " + index + (solid.Contains(index) ? " — solid" : ""), Cursor = Cursors.Hand };
            chip.MouseLeftButtonDown += (_, _) => Guard(() => { selected = index; tool = TileTool.Paint; RefreshPalette(); status.Text = "Tile " + index + " selected."; });
            chips.Add(chip); palette.Children.Add(chip);
        }
        side.Children.Add(new TextBlock { Text = "Tiles", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        side.Children.Add(new ScrollViewer { Content = palette, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var markSolid = new Button { Content = "Selected tile ⇄ solid", Margin = new Thickness(0, 6, 0, 0), ToolTip = "A solid tile stops a physics body. Solid tiles are marked red in this list." };
        markSolid.Click += (_, _) => Guard(() =>
        {
            if (!solid.Remove(selected)) solid.Add(selected);
            RefreshPalette(); status.Text = "Tile " + selected + (solid.Contains(selected) ? " is solid." : " is scenery.");
        });
        side.Children.Add(markSolid);

        // ---- Tools ----
        side.Children.Add(new TextBlock { Text = "Tool", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 4) });
        var tools = new StackPanel();
        foreach (var (which, label, tip) in new[]
        {
            (TileTool.Paint, "Paint", "Left button paints the selected tile, right button erases."),
            (TileTool.Fill, "Fill", "Floods the run of matching tiles under the cursor with the selected one."),
            (TileTool.Erase, "Erase", "Clears cells back to empty."),
            (TileTool.Pick, "Pick", "Takes the tile under the cursor as the selected one."),
        })
        {
            var radio = new RadioButton { Content = label, IsChecked = which == TileTool.Paint, Margin = new Thickness(0, 2, 0, 2), ToolTip = tip };
            radio.Checked += (_, _) => { tool = which; status.Text = tip; };
            tools.Children.Add(radio);
        }
        side.Children.Add(tools);

        side.Children.Add(new TextBlock { Text = "Zoom", Margin = new Thickness(0, 12, 0, 0) });
        var zoom = new Slider { Minimum = 4, Maximum = 48, Value = 16, IsSnapToTickEnabled = true, TickFrequency = 2 };
        zoom.ValueChanged += (_, _) => Guard(() => Rebuild((int)zoom.Value));
        side.Children.Add(zoom);

        // ---- Grid size ----
        side.Children.Add(new TextBlock { Text = "Grid", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 4) });
        TextBox Number(string label, int value, string tip)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), ToolTip = tip };
            row.Children.Add(new TextBlock { Text = label, Width = 80, VerticalAlignment = VerticalAlignment.Center });
            var box = new TextBox { Text = value.ToString() }; row.Children.Add(box); side.Children.Add(row); return box;
        }
        var columnsBox = Number("Columns", columns, "How many tiles across. Cells outside a smaller grid are dropped.");
        var rowsBox = Number("Rows", rows, "How many tiles down.");
        var resize = new Button { Content = "Resize grid", Margin = new Thickness(0, 4, 0, 0) };
        resize.Click += (_, _) => Guard(() =>
        {
            if (!int.TryParse(columnsBox.Text, out int wantColumns) || !int.TryParse(rowsBox.Text, out int wantRows)) throw new InvalidOperationException("Columns and rows are whole numbers.");
            wantColumns = Math.Clamp(wantColumns, 1, Tilemaps.MaxColumns); wantRows = Math.Clamp(wantRows, 1, Tilemaps.MaxRows);
            if (wantColumns * wantRows > Tilemaps.MaxCells) throw new InvalidOperationException($"A tilemap holds at most {Tilemaps.MaxCells} tiles; {wantColumns}×{wantRows} is {wantColumns * wantRows}.");
            // Kept by position, so growing a map keeps what is already painted where it was.
            var moved = new int[wantColumns * wantRows]; Array.Fill(moved, Empty);
            for (int r = 0; r < Math.Min(rows, wantRows); r++) for (int c = 0; c < Math.Min(columns, wantColumns); c++) moved[r * wantColumns + c] = grid[r * columns + c];
            grid = moved; columns = wantColumns; rows = wantRows;
            columnsBox.Text = columns.ToString(); rowsBox.Text = rows.ToString();
            Rebuild((int)zoom.Value); status.Text = $"Grid is now {columns} × {rows}.";
        });
        side.Children.Add(resize);
        var clear = new Button { Content = "Clear the map", Margin = new Thickness(0, 4, 0, 0) };
        clear.Click += (_, _) => Guard(() => { Array.Fill(grid, Empty); Rebuild((int)zoom.Value); status.Text = "Map cleared."; });
        side.Children.Add(clear);
        var fillAll = new Button { Content = "Fill the map with the selected tile", Margin = new Thickness(0, 4, 0, 0) };
        fillAll.Click += (_, _) => Guard(() => { Array.Fill(grid, selected); Rebuild((int)zoom.Value); status.Text = "Map filled with tile " + selected + "."; });
        side.Children.Add(fillAll);

        // ---- Painting ----
        void Flood(int c, int r, int to)
        {
            int from = grid[r * columns + c]; if (from == to) return;
            var stack = new Stack<(int C, int R)>(); stack.Push((c, r));
            while (stack.Count > 0)
            {
                var (x, y) = stack.Pop();
                if (x < 0 || y < 0 || x >= columns || y >= rows || grid[y * columns + x] != from) continue;
                grid[y * columns + x] = to; PaintCell(x, y);
                stack.Push((x + 1, y)); stack.Push((x - 1, y)); stack.Push((x, y + 1)); stack.Push((x, y - 1));
            }
        }
        void Apply(System.Windows.Point p, bool erasing)
        {
            int c = (int)(p.X / cell), r = (int)(p.Y / cell);
            if (c < 0 || r < 0 || c >= columns || r >= rows) return;
            int want = erasing || tool == TileTool.Erase ? Empty : selected;
            switch (erasing ? TileTool.Paint : tool)
            {
                case TileTool.Pick:
                    selected = Math.Max(0, grid[r * columns + c]); RefreshPalette();
                    status.Text = "Tile " + selected + " selected."; return;
                case TileTool.Fill: Flood(c, r, want); return;
                default:
                    if (grid[r * columns + c] == want) return;
                    grid[r * columns + c] = want; PaintCell(c, r); return;
            }
        }
        surface.MouseLeftButtonDown += (_, e) => Guard(() => { surface.CaptureMouse(); Apply(e.GetPosition(surface), false); });
        surface.MouseRightButtonDown += (_, e) => Guard(() => { surface.CaptureMouse(); Apply(e.GetPosition(surface), true); });
        surface.MouseMove += (_, e) => Guard(() =>
        {
            var p = e.GetPosition(surface);
            int c = (int)(p.X / cell), r = (int)(p.Y / cell);
            if (c >= 0 && r >= 0 && c < columns && r < rows) status.Text = $"{c}, {r}  ·  tile {grid[r * columns + c]}";
            if (e.LeftButton == MouseButtonState.Pressed) Apply(p, false);
            else if (e.RightButton == MouseButtonState.Pressed) Apply(p, true);
        });
        surface.MouseLeftButtonUp += (_, _) => surface.ReleaseMouseCapture();
        surface.MouseRightButtonUp += (_, _) => surface.ReleaseMouseCapture();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        side.Children.Add(buttons);
        var apply = new Button { Content = "Apply", IsDefault = true, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 3, 12, 3) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(12, 3, 12, 3) };
        apply.Click += (_, _) => Guard(() =>
        {
            if (!ui.Elements.Contains(element)) throw new InvalidOperationException("The control was removed.");
            Change();
            element.Columns = columns; element.Rows = rows;
            element.Tiles = Tilemaps.Write(grid);
            element.Solid = Ranges(solid);
            Tilemaps.Fit(element);
            window.Close(); Draw(); RefreshInspector();
        });
        cancel.Click += (_, _) => window.Close();
        buttons.Children.Add(apply); buttons.Children.Add(cancel);

        RefreshPalette(); Rebuild(16);
        status.Text = "Left button paints, right button erases. Pick a tile on the right.";
        window.ShowDialog();
    }

    /// <summary>A set of tile numbers back to the "1,3,5-9" form, so a long run of solid tiles stays short.</summary>
    static string Ranges(IEnumerable<int> numbers)
    {
        var sorted = numbers.Distinct().OrderBy(n => n).ToList();
        var parts = new List<string>();
        for (int i = 0; i < sorted.Count;)
        {
            int j = i; while (j + 1 < sorted.Count && sorted[j + 1] == sorted[j] + 1) j++;
            parts.Add(j > i + 1 ? sorted[i] + "-" + sorted[j] : string.Join(',', sorted.Skip(i).Take(j - i + 1)));
            i = j + 1;
        }
        return string.Join(',', parts);
    }
}
