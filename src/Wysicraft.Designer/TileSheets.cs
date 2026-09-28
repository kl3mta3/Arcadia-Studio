using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wysicraft.Core;
using Wysicraft.Models;
namespace Wysicraft.Designer;

// Ready-made tile sheets, drawn into the project rather than shipped as files. A tilemap is useless without a sheet,
// and asking someone to go and draw sixteen 16×16 tiles before they can try the control is the wrong first step —
// so this makes a usable one in a click, and the Pixel editor can take it from there.
//
// Each sheet is one row of 16px tiles, numbered left to right the way the control reads them: tile 0 is always empty
// so a map starts blank, then solid, then the decorative ones.
public partial class MainWindow
{
    sealed record TileSheet(string Id, string Name, string Solid, string What, (string Name, string[] Colors)[] Tiles);

    // Colours are listed back-to-front: first is the base, then the shading passes drawn over it.
    static readonly TileSheet[] StockSheets =
    [
        new("dungeon", "Dungeon", "1-3", "Stone floor and walls, a door and a torch — the set the crawler samples use.",
        [
            ("empty", []), ("floor", ["#3E3A36", "#4A443E", "#2E2B28"]), ("wall", ["#5A5048", "#6B6058", "#3E3730"]),
            ("rubble", ["#46413B", "#6B6058", "#2E2B28"]), ("door", ["#4A3524", "#6B4A30", "#2A1D14"]),
            ("torch", ["#3E3A36", "#C8873A", "#F2C46A"]), ("moss", ["#3E3A36", "#4C6B3A", "#6B8C4A"]), ("water", ["#1E3A52", "#2B5273", "#3E6B94"])
        ]),
        new("cave", "Cave", "1-2", "Rock, dirt and ore, with lava that isn't solid so a body falls into it.",
        [
            ("empty", []), ("rock", ["#4A4540", "#5C564F", "#332F2B"]), ("dirt", ["#52402E", "#6B543E", "#3A2C1F"]),
            ("gravel", ["#4A4540", "#7A736B", "#332F2B"]), ("gold ore", ["#4A4540", "#C9A227", "#332F2B"]),
            ("gem ore", ["#4A4540", "#3FB6C4", "#332F2B"]), ("lava", ["#8C2B12", "#D4541C", "#F2A03C"]), ("crystal", ["#4A4540", "#8C5FC4", "#C49AF2"])
        ]),
        new("grass", "Grass and earth", "1-2", "An outdoor set: grass, earth, stone, sand, a path and water.",
        [
            ("empty", []), ("grass", ["#4C7A32", "#5E9440", "#3A5F26"]), ("earth", ["#6B4E33", "#82613F", "#4E3925"]),
            ("stone", ["#6E6E6E", "#858585", "#525252"]), ("sand", ["#C9B274", "#DCC88C", "#A8925A"]),
            ("path", ["#8A7455", "#A08A68", "#6B5A42"]), ("water", ["#2B5A94", "#3E75B2", "#1E406B"]), ("flowers", ["#4C7A32", "#D45A6B", "#F2D45A"])
        ]),
        new("ice", "Ice and snow", "1-2", "A cold set: snow, ice, frozen stone and a crack that isn't solid.",
        [
            ("empty", []), ("snow", ["#D8E4EC", "#EEF5FA", "#B4C6D4"]), ("ice", ["#8CC2DC", "#B2DCEE", "#5E98BA"]),
            ("frozen stone", ["#5E6B75", "#76848F", "#44505A"]), ("icicle", ["#8CC2DC", "#EEF5FA", "#5E98BA"]),
            ("crack", ["#8CC2DC", "#2B4457", "#5E98BA"]), ("frost", ["#D8E4EC", "#8CC2DC", "#FFFFFF"]), ("deep water", ["#1E3A52", "#2B5273", "#14283A"])
        ]),
        new("brick", "Brick and metal", "1-4", "A built set: brick, metal plate, grating, pipes and a hazard stripe.",
        [
            ("empty", []), ("brick", ["#7A3E30", "#96513E", "#5A2A20"]), ("plate", ["#5A6068", "#737A84", "#3E434A"]),
            ("rivet plate", ["#5A6068", "#8C949E", "#3E434A"]), ("grating", ["#3E434A", "#6E757E", "#23262B"]),
            ("pipe", ["#5A6068", "#9AA2AC", "#3E434A"]), ("hazard", ["#C9A227", "#1E1E1E", "#E4BC3E"]), ("glass", ["#3E4A52", "#6E8C9A", "#2A333A"])
        ]),
        new("palette", "Plain colours", "1-8", "Flat colours to block a level out with before any art exists.",
        [
            ("empty", []), ("white", ["#E8E8E8"]), ("grey", ["#8A8A8A"]), ("black", ["#2A2A2A"]), ("red", ["#C4453A"]),
            ("orange", ["#D4822B"]), ("green", ["#4C8C3A"]), ("blue", ["#3A6BC4"]), ("purple", ["#7A4CC4"])
        ]),
    ];

    /// <summary>Draws one stock sheet as a PNG: one row of 16px tiles, tile 0 transparent.</summary>
    static byte[] DrawSheet(TileSheet sheet)
    {
        const int tile = 16;
        var visual = new DrawingVisual();
        var noise = new Random(sheet.Id.Sum(c => c));
        using (var dc = visual.RenderOpen())
            for (int i = 0; i < sheet.Tiles.Length; i++)
            {
                var (_, colors) = sheet.Tiles[i];
                if (colors.Length == 0) continue;   // tile 0 stays transparent, so an unpainted cell shows through
                double x = i * tile;
                dc.DrawRectangle(new SolidColorBrush(Hex(colors[0])), null, new Rect(x, 0, tile, tile));
                if (colors.Length == 1) continue;
                // A light pass and a dark pass of scattered pixels: enough that tiles read as different materials
                // at 16px without pretending to be finished art.
                var light = new SolidColorBrush(Hex(colors[1]));
                var dark = new SolidColorBrush(Hex(colors.Length > 2 ? colors[2] : colors[1]));
                for (int n = 0; n < 26; n++)
                {
                    int px = noise.Next(tile), py = noise.Next(tile), size = noise.Next(1, 3);
                    dc.DrawRectangle(n % 2 == 0 ? light : dark, null, new Rect(x + px, py, Math.Min(size, tile - px), Math.Min(size, tile - py)));
                }
                // A highlight along the top edge and a shadow along the bottom, so tiles stack readably.
                dc.DrawRectangle(light, null, new Rect(x, 0, tile, 1));
                dc.DrawRectangle(dark, null, new Rect(x, tile - 1, tile, 1));
            }
        var bitmap = new RenderTargetBitmap(sheet.Tiles.Length * tile, tile, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
    static Color Hex(string hex) => ColorPicker.TryColor(hex, out var c) ? c : Colors.Magenta;

    /// <summary>Pick a ready-made sheet, draw it into the project, and point the tilemap at it.</summary>
    void ShowTileSheetMaker(Element map)
    {
        var window = new Window { Owner = this, Title = "Tile sheets", Width = 520, Height = 520, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new Thickness(12) }; window.Content = root;
        root.Children.Add(new TextBlock
        {
            Text = "A ready-made sheet of 16×16 tiles, drawn into the project. Tile 0 is empty so a map starts blank. "
                 + "Choose one to get going, then open it in the Pixel editor and make it yours.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 0, 0, 10)
        });
        DockPanel.SetDock(root.Children[0], Dock.Top);
        var list = new StackPanel();
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        foreach (var sheet in StockSheets)
        {
            var png = DrawSheet(sheet);
            var preview = new Image { Source = DecodeTexture(png), Height = 32, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.NearestNeighbor);
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = sheet.Name, FontWeight = FontWeights.SemiBold });
            body.Children.Add(new TextBlock { Text = sheet.What, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11 });
            body.Children.Add(preview);
            body.Children.Add(new TextBlock { Text = "Tiles: " + string.Join(", ", sheet.Tiles.Select((t, i) => i + " " + t.Name)) + "   ·   solid: " + sheet.Solid, Opacity = 0.55, FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            var use = new Button { Content = "Use this sheet", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
            use.Click += (_, _) => Guard(() =>
            {
                string path = TextureAssets.Path(project.Manifest.Id, "tiles_" + sheet.Id + ".png");
                Change();
                project.Assets[path] = png;
                map.Texture = project.Manifest.Id + ":" + path[("assets/" + project.Manifest.Id + "/").Length..];
                map.TileWidth = 16; map.TileHeight = 16;
                // Only set Solid if it hasn't been chosen already, so re-picking a sheet doesn't undo that work.
                if (map.Solid.Trim().Length == 0) map.Solid = sheet.Solid;
                Tilemaps.Fit(map);
                RefreshAssetBrowser(); Draw(); RefreshInspector();
                Log($"Added the {sheet.Name} tile sheet and pointed {map.Id} at it. Open the Tile painter to draw with it.");
                window.Close();
            });
            body.Children.Add(use);
            list.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(69, 75, 86)), BorderThickness = new Thickness(1), Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 8), Child = body });
        }
        window.ShowDialog();
    }
}
