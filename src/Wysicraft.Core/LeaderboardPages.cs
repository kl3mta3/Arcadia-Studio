using Wysicraft.Models;
namespace Wysicraft.Core;

/// <summary>What a leaderboard page (Project.Leaderboards) may hold, and a new page to start from. The page itself is
/// made by Wysicraft.Packaging.Leaderboards.</summary>
public static class LeaderboardPages
{
    public static readonly string[] Widgets = ["lb_table", "lb_podium", "lb_rank", "lb_icon", "lb_range", "lb_slider", "lb_me", "lb_periods"];
    public static readonly HashSet<string> Types = ["label", "image", "panel", "shape", .. Widgets];

    /// <summary>A new leaderboard page: a title, period tabs, the top 3 on a podium, the top 10, the viewer's rank and
    /// the number of players. Everything can be moved, restyled or removed.</summary>
    public static UiDefinition Starter(string id)
    {
        Element E(string eid, string type, double x, double y, double w, double h, Action<Element> set)
        {
            var e = new Element { Id = eid, Type = type, Text = "", Bounds = new() { X = x, Y = y, Width = w, Height = h }, Background = "#1D2127", FillEnabled = false, BorderWidth = 0, Foreground = "#E6EDF3", Font = "web:sans", Alignment = "left" };
            if (type.StartsWith("lb_")) e.Board = new();
            set(e); return e;
        }
        return new UiDefinition
        {
            Id = id, Title = "Leaderboard", IsLeaderboard = true, Size = new() { Width = 640, Height = 400 },
            Elements =
            [
                E("background", "panel", 0, 0, 640, 400, e => { e.FillEnabled = true; e.Background = "#15181D"; }),
                E("title", "label", 20, 10, 600, 30, e => { e.Text = "{title}"; e.FontScale = 2.4; e.Bold = true; e.Alignment = "center"; e.Foreground = "#F4C744"; }),
                E("periods", "lb_periods", 200, 46, 240, 20, e => { e.FontScale = 1.2; e.Alignment = "center"; }),
                E("podium", "lb_podium", 20, 76, 290, 180, e => { e.Text = "{name}\\n{score}"; e.FontScale = 1.2; e.FillEnabled = true; e.CornerRadius = 6; }),
                E("top_10", "lb_table", 330, 76, 290, 264, e => { e.Text = "{medal}{rank}|{name}|{score}"; e.FontScale = 1.2; e.FillEnabled = true; e.CornerRadius = 6; e.Board!.Count = 10; e.Board.RowHeight = 22; e.Board.Header = "#|Player|{label}"; e.Board.Icon = "medal"; e.Board.AltColor = "#0AFFFFFF"; }),
                E("your_rank", "lb_me", 20, 266, 290, 74, e => { e.Text = "{rank}|{name}|{score}"; e.FontScale = 1.1; e.FillEnabled = true; e.CornerRadius = 6; e.Board!.Around = 1; e.Board.RowHeight = 17; e.Board.Header = "Your rank"; e.Board.Empty = "Sign in and play to get on the board."; }),
                E("players", "label", 20, 352, 600, 20, e => { e.Text = "{players} players on the board"; e.Alignment = "center"; e.Foreground = "#8B95A5"; e.FontScale = 1.1; })
            ]
        };
    }
}
