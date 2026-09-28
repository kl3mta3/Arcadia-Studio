using Wysicraft.Models;
namespace Wysicraft.Core;

/// <summary>A tilemap's data: the grid of tile indices and which of them stop a body.
///
/// The grid is stored run-length encoded, because a map is mostly repetition — "-1*240 5 5 5 12*3" is a screen of
/// nothing, three of tile 5 and three of tile 12. A plain list of indices for a 100x100 map is 30 KB of JSON; the same
/// map encoded this way is usually a few hundred bytes. The runtime in wysicraft-web.js reads the same format.</summary>
public static class Tilemaps
{
    public const int MaxColumns = 512, MaxRows = 512, MaxCells = 65536;
    public const int Empty = -1;

    /// <summary>The grid as a flat array of length columns * rows. Cells past the end of the data are empty.</summary>
    public static int[] Read(string tiles, int columns, int rows)
    {
        var grid = new int[Math.Max(0, columns * rows)];
        Array.Fill(grid, Empty);
        int at = 0;
        foreach (var run in (tiles ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int star = run.IndexOf('*');
            string number = star < 0 ? run : run[..star];
            if (!int.TryParse(number, out int index)) throw new InvalidDataException($"Tile run \"{run}\" is not a number.");
            int count = 1;
            if (star >= 0 && !int.TryParse(run[(star + 1)..], out count)) throw new InvalidDataException($"Tile run \"{run}\" has no count.");
            if (count < 1) throw new InvalidDataException($"Tile run \"{run}\" repeats fewer than once.");
            for (int i = 0; i < count && at < grid.Length; i++) grid[at++] = index;
        }
        return grid;
    }

    /// <summary>Back to the run-length form, trimming the empty tail so an untouched map costs nothing.</summary>
    public static string Write(int[] grid)
    {
        int last = grid.Length - 1;
        while (last >= 0 && grid[last] == Empty) last--;
        var parts = new List<string>();
        for (int i = 0; i <= last;)
        {
            int value = grid[i], run = 1;
            while (i + run <= last && grid[i + run] == value) run++;
            parts.Add(run > 1 ? value + "*" + run : value.ToString());
            i += run;
        }
        return string.Join(' ', parts);
    }

    /// <summary>Which tile indices stop a body, from "1,3,5-9". Empty means nothing is solid.</summary>
    public static HashSet<int> Solid(string solid)
    {
        var set = new HashSet<int>();
        foreach (var part in (solid ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var span = part.Trim();
            int dash = span.IndexOf('-', 1);
            if (dash < 0) { if (int.TryParse(span, out int one)) set.Add(one); else throw new InvalidDataException($"\"{span}\" is not a tile number."); continue; }
            if (!int.TryParse(span[..dash], out int from) || !int.TryParse(span[(dash + 1)..], out int to)) throw new InvalidDataException($"\"{span}\" is not a tile range.");
            if (to < from) (from, to) = (to, from);
            if (to - from > MaxCells) throw new InvalidDataException("That tile range is too wide.");
            for (int i = from; i <= to; i++) set.Add(i);
        }
        return set;
    }

    /// <summary>Sizes a tilemap's box to its grid. The runtime draws the grid, not the box, so the two are kept the
    /// same and a map can't be dragged into a size that doesn't match what it shows.</summary>
    public static void Fit(Element e)
    {
        if (e.Type != "tilemap") return;
        e.Bounds.Width = Math.Max(1, e.Columns) * Math.Max(1, e.TileWidth);
        e.Bounds.Height = Math.Max(1, e.Rows) * Math.Max(1, e.TileHeight);
    }

    /// <summary>Everything wrong with a tilemap control, or nothing.</summary>
    public static IEnumerable<string> Problems(Element e)
    {
        if (e.TileWidth is < 1 or > 512 || e.TileHeight is < 1 or > 512) yield return "Tiles are 1–512 pixels each way";
        if (e.Columns is < 1 or > MaxColumns || e.Rows is < 1 or > MaxRows) yield return $"A tilemap is 1–{MaxColumns} columns and 1–{MaxRows} rows";
        else if (e.Columns * e.Rows > MaxCells) yield return $"A tilemap holds at most {MaxCells} tiles ({e.Columns}×{e.Rows} is {e.Columns * e.Rows})";
        var problem = "";
        try { Read(e.Tiles, e.Columns, e.Rows); Solid(e.Solid); } catch (InvalidDataException ex) { problem = ex.Message; }
        if (problem.Length > 0) yield return problem;
        if (e.Texture.Length == 0) yield return "A tilemap needs a tile sheet as its texture";
    }
}
