using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using Wysicraft.Core;
using Wysicraft.Models;
using Wysicraft.Packaging;
using Path = System.IO.Path;
using Registry = Wysicraft.Core.Registry;
using Validation = Wysicraft.Core.Validation;
namespace Wysicraft.Designer;

// Leaderboard pages (Advanced → Create leaderboard): designed on the same canvas as screens, with layers, groups, undo
// and the rest, but kept in Project.Leaderboards (never part of the game) and saved as .lb. The toolbox offers labels,
// pictures, panels and shapes, and widgets that read the arcade's board. The canvas shows them with sample players;
// Preview and Export make the real page (Leaderboards.Page), which Arcadia fills with the game's scores.
public partial class MainWindow
{
    static readonly (string Tag, string Icon, string Label, string Tip)[] BoardStamps =
    [
        ("lb:top10", "lb_table", "Top 10 panel", "The top 10: rank, name and score. Change the rows, the text and the background."),
        ("lb:top100", "lb_table", "Top 100 (scrolling)", "The top 100 in a list that scrolls."),
        ("lb:podium", "lb_podium", "Top 3 podium", "The top 3 on tiered steps: 2nd, 1st and 3rd."),
        ("lb:first", "lb_rank", "First place", "A box for the player in 1st place."),
        ("lb:second", "lb_rank", "Second place", "A box for the player in 2nd place."),
        ("lb:third", "lb_rank", "Third place", "A box for the player in 3rd place."),
        ("lb:crown", "lb_icon", "1st place crown", "A gold crown to put by 1st place. Pick another icon, or your own picture."),
        ("lb:medal2", "lb_icon", "2nd place medal", "A silver medal to put by 2nd place."),
        ("lb:medal3", "lb_icon", "3rd place medal", "A bronze medal to put by 3rd place."),
        ("lb:range", "lb_range", "Range panel", "Choose which ranks to show (from and to), or find a player by name. It scrolls."),
        ("lb:slider", "lb_slider", "Top 5 slider", "The top 5 as cards that slide along by themselves."),
        ("lb:me", "lb_me", "Your rank", "The person looking: their rank, and the players just above and below."),
        ("lb:periods", "lb_periods", "Period tabs", "All time, This week, Today: switches every list on the page."),
        ("lb:title", "label", "Board title", "A label showing the game's title ({title})."),
        ("lb:players", "label", "Player count", "A label showing how many players are on the board ({players}).")
    ];
    const string TemplateHelp = "Text: {rank} {name} {score} {runs} {playerNo} {when} {stat.level} (any stat's key) and {medal} (the rank's icon). A | splits a row into columns; \\n starts a new line on the podium, rank boxes and slider cards. Any label can use {title}, {label}, {players} and {period}.";

    void LeaderboardToolbox(Func<string, string, string, string?, double, ListBoxItem> item, Func<string, bool?, string?, ListBoxItem> header)
    {
        Toolbox.Items.Add(header("Page", null, null));
        Toolbox.Items.Add(item("label", "label", "Label", "Text. Can show {title}, {label}, {players} and {period}.", 0));
        Toolbox.Items.Add(item("image", "image", "Image", "A picture from the project.", 0));
        Toolbox.Items.Add(item("panel", "panel", "Panel", "A colour or picture behind other things.", 0));
        Toolbox.Items.Add(item("shape:rectangle", "shape_rectangle", "Shape", "A rectangle, oval, triangle, diamond, hexagon or star.", 0));
        Toolbox.Items.Add(header("Leaderboard", null, null));
        foreach (var (tag, icon, label, tip) in BoardStamps) Toolbox.Items.Add(item(tag, icon, label, tip, 0));
        var note = new TextBlock { Text = "Click an empty spot for the page's settings, Preview (F5), Export page and Save as .lb.", TextWrapping = TextWrapping.Wrap, Opacity = 0.6, FontSize = 11, Margin = new Thickness(8, 6, 6, 6), MaxWidth = 220 };
        Toolbox.Items.Add(new ListBoxItem { Content = note, Focusable = false, IsHitTestVisible = false });
    }
    /// <summary>Which toolbox is showing: the project's target, and whether a leaderboard page is open.</summary>
    string ToolboxKey() => project.Manifest.Target + (ui.IsLeaderboard ? "|leaderboard" : "");

    /// <summary>A leaderboard widget from the toolbox, set up for what it's for. One Undo step.</summary>
    void AddBoardWidget(string preset, double x, double y)
    {
        if (!ui.IsLeaderboard) throw new InvalidOperationException("Leaderboard widgets go on leaderboard pages: Advanced → Create leaderboard.");
        Change();
        Element W(string id, string type, double w, double h, Action<Element> set)
        {
            var e = new Element { Id = Unique(id), Type = type, Text = "", Bounds = new() { X = x, Y = y, Width = w, Height = h }, Font = "web:sans", Foreground = "#E6EDF3", Background = "#1D2127", FillEnabled = true, BorderWidth = 0, CornerRadius = 6, FontScale = 1.2, Board = new() };
            set(e); e.LayerGroup = isolatedGroup; ui.Elements.Add(e); return e;
        }
        void List(Element e, int count, int rowHeight) { e.Text = "{medal}{rank}|{name}|{score}"; e.Board!.Count = count; e.Board.RowHeight = rowHeight; e.Board.Header = "#|Player|{label}"; e.Board.Icon = "medal"; e.Board.AltColor = "#0AFFFFFF"; }
        Element added = preset switch
        {
            "top10" => W("top_10", "lb_table", 290, 264, e => List(e, 10, 22)),
            "top100" => W("top_100", "lb_table", 300, 300, e => List(e, 100, 20)),
            "podium" => W("podium", "lb_podium", 290, 180, e => e.Text = "{name}\\n{score}"),
            "first" or "second" or "third" => W(preset + "_place", "lb_rank", 180, 48, e => { e.Board!.Rank = preset == "first" ? 1 : preset == "second" ? 2 : 3; e.Text = "#{rank} {name}\\n{score}"; e.Alignment = "center"; e.Foreground = Leaderboards.Tiers[e.Board.Rank]; }),
            "crown" or "medal2" or "medal3" => W(preset == "crown" ? "crown" : "medal", "lb_icon", 32, 32, e => { e.FillEnabled = false; e.CornerRadius = 0; e.Board!.Rank = preset == "crown" ? 1 : preset == "medal2" ? 2 : 3; e.Board.Icon = preset == "crown" ? "crown" : "medal"; }),
            "range" => W("range", "lb_range", 300, 240, e => { e.Text = "{rank}|{name}|{score}"; e.Minimum = 1; e.Maximum = 20; e.Board!.Search = true; e.Board.RowHeight = 20; e.Board.AltColor = "#0AFFFFFF"; }),
            "slider" => W("top_5_slider", "lb_slider", 420, 90, e => { e.Text = "#{rank}\\n{name}\\n{score}"; e.Board!.Count = 5; e.Board.Visible = 3; e.Board.Icon = "medal"; e.Alignment = "center"; }),
            "me" => W("your_rank", "lb_me", 290, 80, e => { e.Text = "{rank}|{name}|{score}"; e.Board!.Around = 1; e.Board.RowHeight = 18; e.Board.Header = "Your rank"; e.Board.Empty = "Sign in and play to get on the board."; }),
            "periods" => W("periods", "lb_periods", 240, 20, e => { e.FillEnabled = false; e.CornerRadius = 0; e.Alignment = "center"; }),
            "title" => W("title", "label", 400, 30, e => { e.Board = null; e.Text = "{title}"; e.FontScale = 2.4; e.Bold = true; e.Alignment = "center"; e.Foreground = "#F4C744"; e.FillEnabled = false; e.CornerRadius = 0; }),
            "players" => W("players", "label", 300, 20, e => { e.Board = null; e.Text = "{players} players on the board"; e.Alignment = "center"; e.Foreground = "#8B95A5"; e.FillEnabled = false; e.CornerRadius = 0; e.FontScale = 1.1; }),
            _ => throw new InvalidOperationException("Unknown leaderboard widget: " + preset)
        };
        selected.Clear(); selected.Add(added.Id); Draw(); RefreshInspector();
    }

    // ---- The canvas: widgets drawn with sample players ----
    static readonly string[] SampleNames = ["Ada", "Bex", "Cato", "Dune", "Echo", "Fenn", "Gale", "Hux", "Iris", "Jax", "Kit", "Lark", "Moss", "Nova", "Orin", "Pip", "Quill", "Rook", "Sol", "Tamsin"];
    static string SampleName(int rank) => rank == 42 ? "You" : SampleNames[(rank - 1) % SampleNames.Length] + (rank > SampleNames.Length ? " " + ((rank - 1) / SampleNames.Length + 1) : "");
    static long SampleScore(int rank) => (long)Math.Round(250000 / (1 + (rank - 1) * 0.35));
    string BoardText(string template, int rank)
    {
        string title = project.Publishing.Title.Length > 0 ? project.Publishing.Title : project.Manifest.Name;
        return System.Text.RegularExpressions.Regex.Replace(template.Replace("\\n", "\n"), @"\{([a-zA-Z.]+)\}", m => m.Groups[1].Value switch
        {
            "title" => title, "label" => project.Publishing.Scores.Label.Length > 0 ? project.Publishing.Scores.Label : "Score", "players" => "137", "period" => "All time", "medal" => "",
            "rank" => rank > 0 ? rank.ToString(CultureInfo.InvariantCulture) : "", "name" => rank > 0 ? SampleName(rank) : "", "score" => rank > 0 ? SampleScore(rank).ToString("N0", CultureInfo.InvariantCulture) : "",
            "runs" => rank > 0 ? (3 + rank * 7 % 40).ToString(CultureInfo.InvariantCulture) : "", "playerNo" => rank > 0 ? (1000 + rank * 7).ToString(CultureInfo.InvariantCulture) : "",
            "when" => rank > 0 ? DateTime.Today.ToShortDateString() : "",
            var k when k.StartsWith("stat.") => rank > 0 ? Math.Max(1, 30 - rank / 5).ToString(CultureInfo.InvariantCulture) : "",
            _ => m.Value
        });
    }
    static Brush Tier(int rank) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(Leaderboards.Tiers.GetValueOrDefault(rank, "#C9D1D9"))!);
    static FrameworkElement BoardIcon(string kind, Brush fill, double size) => new Viewbox
    {
        Width = size, Height = size, Stretch = Stretch.Uniform,
        Child = new Canvas { Width = 24, Height = 24, Children = { new System.Windows.Shapes.Path { Data = Geometry.Parse("F1 " + Leaderboards.IconPaths.GetValueOrDefault(kind, Leaderboards.IconPaths["crown"])), Fill = fill } } }
    };

    FrameworkElement RenderBoardWidget(Element e)
    {
        var b = e.Board ??= new BoardWidget();
        double w = Math.Max(1, e.Bounds.Width) * Zoom, h = Math.Max(1, e.Bounds.Height) * Zoom, size = Math.Clamp(9 * Zoom * e.FontScale, 6, 144);
        var family = FamilyFor(e.Font); var ink = Brush(e.Foreground);
        TextBlock T(string text, TextAlignment align = TextAlignment.Left) => new() { Text = text, FontFamily = family, FontSize = size, Foreground = ink, FontWeight = e.Bold ? FontWeights.Bold : FontWeights.Normal, FontStyle = e.Italic ? FontStyles.Italic : FontStyles.Normal, TextAlignment = align, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var align = e.Alignment == "center" ? TextAlignment.Center : e.Alignment == "right" ? TextAlignment.Right : TextAlignment.Left;
        StackPanel Lines(string template, int rank) { var p = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; foreach (var line in BoardText(template, rank).Split('\n')) p.Children.Add(T(line, align)); return p; }
        // A list row: "|" cells become columns (the first and last hug their text, the middle ones share the rest).
        FrameworkElement Row(string template, int rank, double height, Brush? back)
        {
            var grid = new Grid { Height = height, Background = back ?? Brushes.Transparent, Margin = new Thickness(0) };
            var cells = template.Split('|');
            for (int i = 0; i < cells.Length; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = cells.Length > 1 && (i == 0 || i == cells.Length - 1) ? GridLength.Auto : new GridLength(1, GridUnitType.Star), MinWidth = i == 0 && cells.Length > 2 ? size * 2.4 : 0 });
                var cell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(i == 0 ? size * 0.5 : size * 0.25, 0, i == cells.Length - 1 ? size * 0.5 : size * 0.25, 0), HorizontalAlignment = cells.Length > 1 && i == cells.Length - 1 ? HorizontalAlignment.Right : HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
                if (rank is >= 1 and <= 3 && cells[i].Contains("{medal}")) { var icon = BoardIcon(b.Icon, Tier(rank), height * 0.7); icon.Margin = new Thickness(0, 0, size * 0.3, 0); cell.Children.Add(icon); }
                cell.Children.Add(T(BoardText(cells[i], rank)));
                Grid.SetColumn(cell, i); grid.Children.Add(cell);
            }
            return grid;
        }
        var root = new Grid { Width = w, Height = h, ClipToBounds = true };
        Brush? Tint(string c) => c.Length > 0 && ColorPicker.TryColor(c, out var parsed) ? new SolidColorBrush(parsed) : null;
        switch (e.Type)
        {
            case "lb_table": case "lb_me": case "lb_range":
            {
                var list = new StackPanel(); double rh = Math.Max(4, b.RowHeight) * Zoom;
                if (b.Header.Length > 0) { var header = Row(b.Header, 0, rh, null); header.Opacity = 0.7; list.Children.Add(header); }
                if (e.Type == "lb_range")
                {
                    var controls = new WrapPanel { Margin = new Thickness(size * 0.5, size * 0.3, size * 0.5, size * 0.3) };
                    Border Box(string text, double width) => new() { Width = width, BorderBrush = new SolidColorBrush(Colors.White) { Opacity = 0.3 }, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Margin = new Thickness(size * 0.3, 0, size * 0.3, 0), Child = T(text) };
                    controls.Children.Add(T("From")); controls.Children.Add(Box(((int)Math.Max(1, e.Minimum)).ToString(CultureInfo.InvariantCulture), size * 3));
                    controls.Children.Add(T("to")); controls.Children.Add(Box(((int)Math.Max(1, e.Maximum)).ToString(CultureInfo.InvariantCulture), size * 3));
                    if (b.Search) { controls.Children.Add(T("Player")); controls.Children.Add(Box("", size * 7)); }
                    list.Children.Add(controls);
                }
                int first = e.Type == "lb_range" ? (int)Math.Max(1, e.Minimum) : e.Type == "lb_me" ? 42 - Math.Clamp(b.Around, 0, 10) : 1;
                int count = e.Type == "lb_table" ? Math.Clamp(b.Count, 1, 100) : e.Type == "lb_me" ? Math.Clamp(b.Around, 0, 10) * 2 + 1 : Math.Clamp((int)Math.Max(1, e.Maximum) - first + 1, 1, 100);
                for (int i = 0; i < count && i < 60; i++)
                {
                    int rank = first + i;
                    var back = rank == 42 && e.Type == "lb_me" ? Tint(b.HighlightColor) : i % 2 == 1 ? Tint(b.AltColor) : null;
                    list.Children.Add(Row(e.Text.Length > 0 ? e.Text : "{rank}|{name}|{score}", rank, rh, back));
                }
                root.Children.Add(list);
                if (e.Type != "lb_me" && count * rh > h) root.Children.Add(new System.Windows.Shapes.Rectangle { Width = Math.Max(2, Zoom * 1.5), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, rh, 2, 2), Fill = new SolidColorBrush(Colors.White) { Opacity = 0.25 }, RadiusX = 2, RadiusY = 2, Height = Math.Max(size, h * h / (count * rh)), VerticalAlignment = VerticalAlignment.Top });
                break;
            }
            case "lb_podium":
            {
                var steps = new UniformGrid3Columns(); root.Children.Add(steps);
                foreach (int rank in new[] { 2, 1, 3 })
                {
                    var step = new DockPanel { LastChildFill = false, Margin = new Thickness(w * 0.02, 0, w * 0.02, 0) };
                    var block = new Border { Height = h * (rank == 1 ? 0.62 : rank == 2 ? 0.46 : 0.34), Background = Tier(rank), CornerRadius = new CornerRadius(4 * Zoom, 4 * Zoom, 0, 0), Child = new TextBlock { Text = rank.ToString(CultureInfo.InvariantCulture), FontFamily = family, FontSize = size * 1.6, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x18, 0x1D)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, size * 0.2, 0, 0) } };
                    DockPanel.SetDock(block, Dock.Bottom); step.Children.Add(block);
                    var who = new StackPanel { Margin = new Thickness(0, 0, 0, size * 0.3) };
                    foreach (var line in BoardText(e.Text.Length > 0 ? e.Text : "{name}\\n{score}", rank).Split('\n')) who.Children.Add(T(line, TextAlignment.Center));
                    DockPanel.SetDock(who, Dock.Bottom); step.Children.Add(who);
                    steps.Children.Add(step);
                }
                break;
            }
            case "lb_rank":
                root.Children.Add(Lines(e.Text.Length > 0 ? e.Text : "#{rank} {name}\\n{score}", Math.Clamp(b.Rank, 1, 3)));
                break;
            case "lb_icon":
                if (e.Texture.Length > 0 && TryTexture(e.Texture, out var png)) root.Children.Add(new Image { Source = DecodeTexture(png), Stretch = Stretch.Uniform });
                else root.Children.Add(BoardIcon(b.Icon, Tier(Math.Clamp(b.Rank, 1, 3)), Math.Min(w, h)));
                break;
            case "lb_slider":
            {
                int per = Math.Clamp(b.Visible, 1, 5), cards = Math.Clamp(b.Count, 1, 100);
                var track = new UniformGrid3Columns(per); root.Children.Add(track);
                for (int i = 1; i <= Math.Min(per, cards); i++)
                {
                    var card = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    if (i <= 3) { var icon = BoardIcon(b.Icon, Tier(i), size * 1.6); icon.HorizontalAlignment = HorizontalAlignment.Center; card.Children.Add(icon); }
                    foreach (var line in BoardText(e.Text.Length > 0 ? e.Text : "#{rank}\\n{name}\\n{score}", i).Split('\n')) card.Children.Add(T(line, TextAlignment.Center));
                    track.Children.Add(card);
                }
                if (cards > per) root.Children.Add(new TextBlock { Text = "▸", Foreground = ink, Opacity = 0.6, FontSize = size * 1.4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 0) });
                break;
            }
            case "lb_periods":
            {
                var names = b.PeriodLabels.Split('|'); string[] keys = ["all", "week", "day"];
                var shown = b.Periods.Split(',').Select(p => p.Trim()).Where(p => keys.Contains(p)).ToList();
                var tabs = new UniformGrid3Columns(Math.Max(1, shown.Count)); root.Children.Add(tabs);
                foreach (var (p, i) in shown.Select((p, i) => (p, i)))
                    tabs.Children.Add(new Border { Margin = new Thickness(i == 0 ? 0 : size * 0.15, 0, 0, 0), CornerRadius = new CornerRadius(3 * Zoom), Background = new SolidColorBrush(Colors.White) { Opacity = i == 0 ? 0.3 : 0.08 }, Child = T(names.ElementAtOrDefault(Array.IndexOf(keys, p))?.Trim() is { Length: > 0 } n ? n : p, TextAlignment.Center) });
                break;
            }
        }
        return root;
    }
    /// <summary>Equal columns, side by side.</summary>
    sealed class UniformGrid3Columns : System.Windows.Controls.Primitives.UniformGrid { public UniformGrid3Columns(int columns = 3) { Columns = columns; Rows = 1; } }

    // ---- The inspector ----
    /// <summary>A control on a leaderboard page: where it is, how it looks, and (for a widget) what it shows. Game
    /// things (events, anchors, conditions) don't apply to a page.</summary>
    void BuildLeaderboardElementInspector(Element element)
    {
        Heading(Properties, "Identity");
        foreach (var name in new[] { "Id", "Name" }) Field(Properties, name, element, name);
        Heading(Properties, "Layout");
        foreach (var name in new[] { "X", "Y", "Width", "Height" }) Field(Properties, name, element.Bounds, name);
        BuildParentField(element);
        BuildAppearance(element);
        Field(Properties, "Visible", element, "Visible");
        if (element.Type.StartsWith("lb_") && Registry.Controls.TryGetValue(element.Type, out var spec))
        {
            Heading(Properties, "Leaderboard");
            foreach (var property in spec.Properties) if (!SpecialField(element, property)) Field(Properties, property, element, property);
            if (element.Type is not ("lb_icon" or "lb_periods")) Properties.Children.Add(new TextBlock { Text = TemplateHelp, TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11, Margin = new Thickness(4, 4, 4, 6) });
        }
        else if (element.Type == "label") Properties.Children.Add(new TextBlock { Text = "A label can show {title} (the game), {label} (the score's name), {players} (how many are on the board) and {period}.", TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11, Margin = new Thickness(4, 4, 4, 6) });
    }
    /// <summary>A widget setting ("Board.Count" and the like) in the inspector.</summary>
    bool BoardField(Element e, string name)
    {
        var b = e.Board ??= new BoardWidget();
        (string Label, string Tip) Describe() => name switch
        {
            "Count" => (e.Type == "lb_slider" ? "Cards" : "Rows", e.Type == "lb_slider" ? "How many of the top players get a card (1–100)." : "How many of the top players are listed (1–100). More rows than fit scroll."),
            "RowHeight" => ("Row height", "Each row's height in page pixels."),
            "Header" => ("Header row", "A line above the list, with the same | columns, for example #|Player|{label}. Empty: no header."),
            "Empty" => ("When empty", "Shown when there's nothing to list yet (or the viewer isn't signed in or has no score)."),
            "Around" => ("Around you", "Players shown above and below the viewer (0–10)."),
            "Seconds" => ("Slide every (s)", "How often the slider moves on to the next card. 0: only when the player scrolls it."),
            "Visible" => ("Cards in view", "How many cards show at once (1–5)."),
            "Periods" => ("Periods", "Which tabs to show, in order: all, week, day (separated by commas)."),
            "PeriodLabels" => ("Tab names", "The names for all time, this week and today, separated by |."),
            _ => (name, "")
        };
        var (label, tip) = Describe();
        switch (name)
        {
            case "Rank":
                Choice(Properties, "Rank", b.Rank.ToString(CultureInfo.InvariantCulture), [("1", "1st"), ("2", "2nd"), ("3", "3rd")], v => b.Rank = int.Parse(v, CultureInfo.InvariantCulture), e.Type == "lb_icon" ? "The icon's colour: gold, silver or bronze." : "Which place this box shows.");
                return true;
            case "Icon":
                Choice(Properties, "Icon", b.Icon, Leaderboards.Icons.Select(i => (i, char.ToUpperInvariant(i[0]) + i[1..])).ToList(), v => b.Icon = v, e.Type == "lb_icon" ? "The icon's shape. A picture chosen under Appearance replaces it." : "The icon {medal} puts by the top 3.");
                return true;
            case "Search":
                var box = new CheckBox { Content = "Find a player box", IsChecked = b.Search, Margin = new Thickness(4), ToolTip = "A box for a name or player number: shows them and 5 either side." };
                box.Click += (_, _) => { Change(); b.Search = box.IsChecked == true; Draw(); };
                Properties.Children.Add(box); return true;
            case "AltColor": ColorField(Properties, "Every other row", b, "AltColor"); return true;
            case "HighlightColor": ColorField(Properties, "Your row", b, "HighlightColor"); return true;
        }
        var property = typeof(BoardWidget).GetProperty(name);
        if (property == null) return false;
        CommitField(label, Convert.ToString(property.GetValue(b), CultureInfo.InvariantCulture) ?? "", v =>
        {
            object value = property.PropertyType == typeof(int) ? (object)int.Parse(v.Trim(), CultureInfo.InvariantCulture)
                : property.PropertyType == typeof(double) ? double.Parse(v.Trim(), CultureInfo.InvariantCulture) : v;
            value = name switch
            {
                "Count" => Math.Clamp((int)value, 1, 100), "RowHeight" => Math.Clamp((int)value, 6, 400), "Around" => Math.Clamp((int)value, 0, 10),
                "Visible" => Math.Clamp((int)value, 1, 5), "Seconds" => Math.Clamp((double)value, 0, 120), _ => value
            };
            Change(); property.SetValue(b, value); Draw(); RefreshInspector();
        }, tip);
        return true;
    }

    /// <summary>The page itself (nothing selected): its ID and title, its size, and what to do with it.</summary>
    void BuildLeaderboardSettings()
    {
        Heading(Properties, "Leaderboard page");
        CommitField("Leaderboard ID", ui.Id, RenameLeaderboard, "Lowercase letters, numbers and underscores. Also its file name: leaderboards/<id>.lb in the project.");
        Field(Properties, "Title", ui, "Title");
        Field(Properties, "Width", ui.Size, "Width");
        Field(Properties, "Height", ui.Size, "Height");
        Properties.Children.Add(new TextBlock { Text = "On the arcade the page is scaled to fit the player's window, keeping this shape. The Title is the browser tab's name; {title} on a label is the game's title.", TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11, Margin = new Thickness(4) });
        var s = project.Publishing;
        var page = ArcadiaPackage.LeaderboardPageOf(project, s);
        string use = ReferenceEquals(page.Board, ui) ? "This is the page Publish to Arcadia uses." : page.Board != null ? "Publish to Arcadia uses " + page.Board.Id + ". Choose this one in the Publish window." : page.Html != null ? "Publish to Arcadia uses an imported page. Choose this one in the Publish window." : "Publish to Arcadia uses the arcade's standard board. Choose this one in the Publish window.";
        Properties.Children.Add(new TextBlock { Text = use + (s.Leaderboard ? "" : " Turn on Keep a leaderboard there, or the page has nothing to show."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 6, 4, 6) });
        foreach (var problem in Leaderboards.Problems(project, ui)) Properties.Children.Add(new TextBlock { Text = "⚠ " + problem, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0xC7, 0x44)), Margin = new Thickness(4, 0, 4, 4) });
        var buttons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        Button B(string text, string tip, Action act) { var button = new Button { Content = text, ToolTip = tip, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(8, 2, 8, 2) }; button.Click += (_, _) => Guard(act); buttons.Children.Add(button); return button; }
        B("Preview", "Open the page with sample players (F5).", PreviewLeaderboard);
        B("Export page…", "Write leaderboard.html and its pictures into a folder.", ExportLeaderboardPage);
        B("Save as .lb…", "Save this leaderboard as a file of its own, with its pictures, to use in another project.", SaveLeaderboardFile);
        if (!creatorMode) B("Delete", "Remove this leaderboard from the project (Undo brings it back).", DeleteLeaderboard);
        Properties.Children.Add(buttons);
    }

    // ---- Creating, opening, importing, exporting ----
    /// <summary>The screen or leaderboard the editor shows, found again in the project (after Undo, or an assistant's edit).</summary>
    UiDefinition ResolveUi(UiDefinition old) =>
        // The leaderboard creator only ever shows its own page (found by its place in the list, in case it was renamed).
        creatorMode && creatorBoardIndex < project.Leaderboards.Count ? project.Leaderboards[creatorBoardIndex] :
        (old.IsLeaderboard ? project.Leaderboards.FirstOrDefault(b => b.Id == old.Id) : project.Screens.FirstOrDefault(s => s.Id == old.Id))
        ?? project.Screens.FirstOrDefault(s => s.Id == componentReturnScreen && !s.IsComponent) ?? project.Screens.FirstOrDefault(s => !s.IsComponent) ?? project.Screens[0];

    /// <param name="open">Show it on the canvas (false when the leaderboard creator is about to open on it).</param>
    internal UiDefinition CreateLeaderboard(bool open = true)
    {
        SaveScriptText(); Change();
        string id = project.Leaderboards.Any(b => b.Id == "leaderboard") ? ElementIds.Next("leaderboard", project.Leaderboards.Select(b => b.Id)) : "leaderboard";
        var board = Leaderboards.Starter(id);
        string game = project.Publishing.Title.Length > 0 ? project.Publishing.Title : project.Manifest.Name;
        board.Title = (game.Length > 0 ? game + " " : "") + "leaderboard";
        project.Leaderboards.Add(board);
        if (open) OpenLeaderboard(board); else RefreshAll();
        Log("Created the leaderboard page " + id + ". Change anything on it; Preview (F5) shows it with sample players. Publish to Arcadia uses it for the game's leaderboard.");
        return board;
    }
    internal void OpenLeaderboard(UiDefinition board)
    {
        if (!ui.IsComponent && !ui.IsLeaderboard) componentReturnScreen = ui.Id;
        ShowScreen(board);
    }
    /// <summary>Advanced → Open leaderboard: the only one straight away, otherwise a choice.</summary>
    void ChooseLeaderboard()
    {
        if (project.Leaderboards.Count == 0)
        {
            if (MessageBox.Show(this, "This project has no leaderboard pages yet. Create one?", "Leaderboards", MessageBoxButton.YesNo) == MessageBoxResult.Yes) CreateLeaderboard();
            return;
        }
        if (project.Leaderboards.Count == 1) { OpenLeaderboard(project.Leaderboards[0]); return; }
        var window = new Window { Title = "Open leaderboard", Owner = this, Width = 360, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        window.SetResourceReference(StyleProperty, typeof(Window));
        var panel = new StackPanel { Margin = new Thickness(14) }; window.Content = panel;
        var list = new ListBox { Height = 160, DisplayMemberPath = "Id", ItemsSource = project.Leaderboards.ToList(), SelectedIndex = 0 };
        panel.Children.Add(list);
        var open = new Button { Content = "Open", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(14, 2, 14, 2) };
        open.Click += (_, _) => window.DialogResult = true; list.MouseDoubleClick += (_, _) => window.DialogResult = true; panel.Children.Add(open);
        if (window.ShowDialog() == true && list.SelectedItem is UiDefinition board) OpenLeaderboard(board);
    }
    /// <summary>Import a leaderboard: a .lb file, or a page. A page this app exported opens as an editable leaderboard;
    /// any other page can only be used as it is, for publishing. Returns the Publish window's choice for it, or null.</summary>
    internal string? ImportLeaderboard(bool open = true)
    {
        var dialog = new OpenFileDialog { Title = "Import a leaderboard", Filter = "Leaderboards (*.lb, *.html)|*.lb;*.html;*.htm|All files|*.*" };
        if (dialog.ShowDialog(this) != true) return null;
        return ImportLeaderboardFile(dialog.FileName, open, confirm: q => MessageBox.Show(this, q, "Import leaderboard", MessageBoxButton.YesNo) == MessageBoxResult.Yes);
    }
    internal string? ImportLeaderboardFile(string file, bool open, Func<string, bool> confirm)
    {
        var bytes = File.ReadAllBytes(file);
        Leaderboards.LbFile? found;
        if (file.EndsWith(Leaderboards.Extension, StringComparison.OrdinalIgnoreCase)) found = Leaderboards.Read(bytes);
        else
        {
            string folder = Path.GetFullPath(Path.GetDirectoryName(file)!);
            // Pictures and fonts are read from beside the page, and only from inside its folder.
            found = Leaderboards.FromPage(System.Text.Encoding.UTF8.GetString(bytes), relative =>
            {
                string path = Path.GetFullPath(Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar)));
                return path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? File.ReadAllBytes(path) : null;
            });
            if (found == null)
            {
                if (!confirm(Path.GetFileName(file) + " wasn't made in Arcadia Studio, so it can't be opened in the editor.\n\nUse it as this game's leaderboard page, as it is, when publishing to Arcadia?")) return null;
                Change(); project.Publishing.LeaderboardHtml = bytes; project.Publishing.LeaderboardPage = "file";
                Log("Imported " + Path.GetFileName(file) + " as the game's leaderboard page (used as it is). Choose it in Publish to Arcadia.");
                RefreshInspector();
                return "file";
            }
        }
        SaveScriptText(); Change();
        var board = Leaderboards.Import(project, found);
        Log("Imported the leaderboard " + board.Id + " from " + Path.GetFileName(file) + (found.Images.Count + found.Fonts.Count > 0 ? $", with {found.Images.Count + found.Fonts.Count} picture(s) and font(s)" : "") + ".");
        if (open) OpenLeaderboard(board); else RefreshAll();
        return "board:" + board.Id;
    }
    void ExportLeaderboardPage()
    {
        if (!ui.IsLeaderboard) return;
        var dialog = new OpenFolderDialog { Title = "Export the leaderboard page to a folder" };
        if (dialog.ShowDialog(this) != true) return;
        // Only this page's own files are written: nothing else in the folder is touched.
        foreach (var (path, bytes) in Leaderboards.Page(project, ui))
        {
            string target = Path.Combine(dialog.FolderName, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllBytes(target, bytes);
        }
        Log("Exported " + ui.Id + " to " + Path.Combine(dialog.FolderName, Leaderboards.PageName) + ". Opened as a file it shows sample players; on Arcadia, the game's board.");
    }
    void SaveLeaderboardFile()
    {
        if (!ui.IsLeaderboard) return;
        var dialog = new SaveFileDialog { Title = "Save the leaderboard", FileName = ui.Id + Leaderboards.Extension, Filter = "Leaderboard (*.lb)|*.lb", DefaultExt = Leaderboards.Extension };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllBytes(dialog.FileName, Leaderboards.Standalone(project, ui));
        Log("Saved " + Path.GetFileName(dialog.FileName) + " with its pictures. Import it into any project from Advanced → Import leaderboard.");
    }
    void DeleteLeaderboard()
    {
        if (!ui.IsLeaderboard) return;
        if (MessageBox.Show(this, "Delete the leaderboard page " + ui.Id + "? Undo brings it back.", "Delete leaderboard", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        Change(); var id = ui.Id;
        project.Leaderboards.RemoveAll(b => b.Id == id);
        if (project.Publishing.LeaderboardPage == "board:" + id) project.Publishing.LeaderboardPage = "";
        ui = project.Screens.FirstOrDefault(s => s.Id == componentReturnScreen && !s.IsComponent) ?? project.Screens.First(s => !s.IsComponent);
        selected.Clear(); RefreshAll();
    }
    void RenameLeaderboard(string id)
    {
        id = id.Trim();
        if (id == ui.Id) return;
        if (!Validation.Id(id)) throw new InvalidOperationException("IDs use lowercase letters, digits and _, starting with a letter.");
        if (project.Leaderboards.Any(b => b.Id == id)) throw new InvalidOperationException("Another leaderboard is already called " + id + ".");
        Change(); string old = ui.Id; ui.Id = id;
        if (project.Publishing.LeaderboardPage == "board:" + old) project.Publishing.LeaderboardPage = "board:" + id;
        RefreshSourceContext(); RefreshInspector();
    }

    // ---- The bar over the canvas while a page is open ----
    StackPanel? boardBar;
    void RefreshBoardBar()
    {
        if (boardBar == null)
        {
            boardBar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            void B(string text, string tip, Action act) { var button = new Button { Content = text, ToolTip = tip, Margin = new Thickness(6, 2, 0, 2), Padding = new Thickness(8, 1, 8, 1) }; button.Click += (_, _) => Guard(act); boardBar.Children.Add(button); }
            B("Preview", "The page with sample players (F5).", PreviewLeaderboard);
            B("Export page…", "Write leaderboard.html and its pictures into a folder.", ExportLeaderboardPage);
            B("Save as .lb…", "Save this leaderboard as a file of its own.", SaveLeaderboardFile);
            int back = ComponentSourceBar.Children.Count - 1;
            ComponentSourceBar.Children.Insert(Math.Max(0, back), boardBar);
        }
        boardBar.Visibility = ui.IsLeaderboard ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- Preview: the real page, with sample players ----
    LeaderboardPreview? boardPreview;
    void PreviewLeaderboard()
    {
        if (!ui.IsLeaderboard) return;
        var files = Leaderboards.Page(project, ui);
        if (boardPreview == null || !boardPreview.IsLoaded) { boardPreview = new LeaderboardPreview(this); boardPreview.Closed += (_, _) => boardPreview = null; boardPreview.Show(); }
        else boardPreview.Activate();
        _ = boardPreview.ShowPage(files, ui.Size.Width, ui.Size.Height);
    }
    internal LeaderboardPreview? LeaderboardPreviewWindow => boardPreview;
    internal sealed class LeaderboardPreview : Window
    {
        const string Host = "leaderboard.preview";
        readonly MainWindow editor;
        readonly WebView2 view = new();
        readonly string folder = Path.Combine(Path.GetTempPath(), "Arcadia Studio", "Leaderboard", Guid.NewGuid().ToString("N"));
        bool ready;
        internal TaskCompletionSource<bool> Loaded2 { get; private set; } = new();
        public LeaderboardPreview(MainWindow editor)
        {
            this.editor = editor; Owner = editor; SetResourceReference(StyleProperty, typeof(Window));
            Title = "Leaderboard preview"; Width = 980; Height = 640; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var layout = new DockPanel(); Content = layout;
            var bar = new DockPanel { Margin = new Thickness(8, 6, 8, 6) }; DockPanel.SetDock(bar, Dock.Top); layout.Children.Add(bar);
            var reload = new Button { Content = "Reload", Padding = new Thickness(10, 2, 10, 2), ToolTip = "Show the page as it is in the editor now." };
            reload.Click += (_, _) => editor.Guard(editor.PreviewLeaderboard);
            DockPanel.SetDock(reload, Dock.Right); bar.Children.Add(reload);
            bar.Children.Add(new TextBlock { Text = "Sample players. On Arcadia the page shows the game's real board, for whoever is looking.", Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            view.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x0E, 0x10, 0x14);
            layout.Children.Add(view);
            Closed += (_, _) => { view.Dispose(); try { Directory.Delete(folder, true); } catch { } };
        }
        internal async Task ShowPage(Dictionary<string, byte[]> files, int width, int height)
        {
            try
            {
                Loaded2 = new();
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                foreach (var (name, bytes) in files) { var path = Path.Combine(folder, name.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); }
                if (!ready)
                {
                    await view.EnsureCoreWebView2Async(await PreviewEnvironment());
                    var web = view.CoreWebView2;
                    web.Settings.AreDefaultContextMenusEnabled = false; web.Settings.IsStatusBarEnabled = false; web.Settings.AreBrowserAcceleratorKeysEnabled = false;
                    web.SetVirtualHostNameToFolderMapping(Host, folder, CoreWebView2HostResourceAccessKind.DenyCors);
                    web.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith($"https://{Host}/")) e.Cancel = true; };
                    web.NewWindowRequested += (_, e) => e.Handled = true;
                    web.NavigationCompleted += (_, e) => Loaded2.TrySetResult(e.IsSuccess);
                    ready = true;
                }
                view.CoreWebView2.Navigate($"https://{Host}/{Leaderboards.PageName}?v={Guid.NewGuid():N}");
            }
            catch (WebView2RuntimeNotFoundException) { editor.Log("Leaderboard preview needs the Microsoft Edge WebView2 Runtime, which comes with Windows 10 and 11."); Loaded2.TrySetResult(false); }
        }
        internal async Task<string> Script(string code) => view.CoreWebView2 == null ? "null" : await view.CoreWebView2.ExecuteScriptAsync(code);
    }
}
