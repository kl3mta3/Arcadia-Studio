using Wysicraft.Models;
namespace Wysicraft.Core;

/// <summary>Polygon collider outlines: curve handles become short straight segments, and the outline must be a
/// closed shape whose edges don't cross (the same rules the web runtime uses).</summary>
public static class Colliders
{
    /// <summary>The outline as straight edges. A point marked Curve bends the edge between its neighbours
    /// (a quadratic curve through 8 segments).</summary>
    public static List<(double X, double Y)> Flatten(IReadOnlyList<Vertex> points)
    {
        var result = new List<(double, double)>(); int n = points.Count;
        if (n == 0) return result;
        for (int i = 0; i < n; i++)
        {
            var p = points[i]; if (p.Curve) continue;
            result.Add((p.X, p.Y));
            var next = points[(i + 1) % n];
            if (next.Curve)
            {
                // Curve handles in a row share the same end point: the next ordinary point after them.
                var end = Enumerable.Range(1, n).Select(k => points[(i + k) % n]).FirstOrDefault(v => !v.Curve) ?? p;
                for (int s = 1; s < 8; s++) { double t = s / 8.0, u = 1 - t; result.Add((u * u * p.X + 2 * u * t * next.X + t * t * end.X, u * u * p.Y + 2 * u * t * next.Y + t * t * end.Y)); }
            }
        }
        return result;
    }

    /// <summary>Why an outline can't be used, or null when it's a proper closed shape.</summary>
    public static string? Problem(IReadOnlyList<Vertex> points)
    {
        if (points.Count(p => !p.Curve) < 2 || Flatten(points).Count < 3) return "needs at least 3 points (or 2 corners and a curve handle)";
        var outline = Flatten(points);
        for (int i = 0; i < outline.Count; i++)
            for (int j = i + 1; j < outline.Count; j++)
            {
                if (j == i + 1 || (i == 0 && j == outline.Count - 1)) continue; // neighbouring edges share a corner
                if (Cross(outline[i], outline[(i + 1) % outline.Count], outline[j], outline[(j + 1) % outline.Count])) return "has edges that cross each other";
            }
        double area = 0; for (int i = 0; i < outline.Count; i++) { var a = outline[i]; var b = outline[(i + 1) % outline.Count]; area += a.X * b.Y - b.X * a.Y; }
        if (Math.Abs(area) < 1) return "has no area (its points are in a line)";
        return null;
    }
    static bool Cross((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, (double X, double Y) d)
    {
        static double Side((double X, double Y) p, (double X, double Y) q, (double X, double Y) r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
        double d1 = Side(c, d, a), d2 = Side(c, d, b), d3 = Side(a, b, c), d4 = Side(a, b, d);
        return (d1 > 0) != (d2 > 0) && (d3 > 0) != (d4 > 0) && d1 != 0 && d2 != 0 && d3 != 0 && d4 != 0;
    }
}
