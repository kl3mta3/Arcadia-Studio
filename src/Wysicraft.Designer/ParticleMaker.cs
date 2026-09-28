using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Wysicraft.Core;
using Wysicraft.Models;
using Validation = Wysicraft.Core.Validation;
namespace Wysicraft.Designer;

/// <summary>Particle maker: sparks, explosions, smoke, rain and the rest, from a preset or built from nothing, then
/// tweaked with sliders and mutated. The preview runs the same simulation the runtime does, so what plays here is what
/// plays in the game. Effects are saved on the manifest by name, and a Particles control plays one.</summary>
sealed class ParticleMaker : Window
{
    public sealed record SaveRequest(ParticleEffect Effect, bool AsNew, string? OriginalId);
    readonly Func<SaveRequest, string> save;
    ParticleEffect fx; string? savedAs; bool dirty, loading;
    readonly List<string> undo = [];
    readonly StackPanel knobs = new() { Margin = new Thickness(0, 0, 20, 0) };
    readonly Canvas stage = new() { Height = 300, Background = new SolidColorBrush(Color.FromRgb(20, 17, 15)), ClipToBounds = true };
    readonly TextBox nameBox = new() { Width = 150 };
    readonly TextBlock status = new() { Margin = new Thickness(8, 4, 8, 4) };
    readonly TextBlock counter = new() { Foreground = Brushes.Gray, FontSize = 10, Margin = new Thickness(6, 2, 0, 0) };
    readonly CheckBox loopPreview = new() { Content = "Replay bursts", IsChecked = true, Margin = new Thickness(0, 8, 0, 0) };
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromMilliseconds(16) };
    readonly List<Particles.Particle> live = [];
    readonly List<Shape> shapes = [];
    readonly Random random = new();
    DateTime last = DateTime.UtcNow;
    double streamCarry, streamLeft, replayIn;

    readonly Func<List<string>> pickTextures;
    readonly Func<string?> addTexture;
    public ParticleMaker(Window owner, ParticleEffect effect, string? existingId, Func<SaveRequest, string> save, Func<List<string>> textures, Func<string?> import)
    {
        Owner = owner; fx = effect; savedAs = existingId; this.save = save; this.pickTextures = textures; this.addTexture = import;
        SetResourceReference(StyleProperty, typeof(Window));
        Width = 1000; Height = 820; MinWidth = 860; MinHeight = 620; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(8) }; Content = root;

        var left = new StackPanel { Width = 172, Margin = new Thickness(0, 0, 10, 0) }; DockPanel.SetDock(left, Dock.Left); root.Children.Add(left);
        left.Children.Add(Header("Start from"));
        foreach (var preset in Particles.Presets)
        {
            var b = new Button { Content = char.ToUpper(preset[0]) + preset[1..], Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(6, 3, 6, 3), ToolTip = "A new random " + preset + " effect (click again for another)" };
            b.Click += (_, _) => Guard(() => { Push(); var keep = fx.Id; fx = Particles.Preset(preset, random); if (savedAs != null) fx.Id = keep; Changed(true); });
            left.Children.Add(b);
        }
        var mutate = new Button { Content = "Mutate", Margin = new Thickness(0, 10, 0, 4), Padding = new Thickness(6, 3, 6, 3), ToolTip = "A close variation of this effect" };
        mutate.Click += (_, _) => Guard(() => { Push(); fx = fx.Mutate(random); Changed(true); }); left.Children.Add(mutate);
        var play = new Button { Content = "▶ Play (Space)", Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(6, 3, 6, 3) };
        play.Click += (_, _) => Guard(Fire); left.Children.Add(play);
        var undoButton = new Button { Content = "Undo (Ctrl+Z)", Padding = new Thickness(6, 3, 6, 3) }; undoButton.Click += (_, _) => Undo(); left.Children.Add(undoButton);
        left.Children.Add(loopPreview);
        left.Children.Add(new TextBlock { Text = "Click the preview to\nfire it from that spot.", Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap });

        var bottom = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom);
        bottom.Children.Add(new TextBlock { Text = "Name", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        bottom.Children.Add(nameBox);
        var saveButton = new Button { Content = "Save", Margin = new Thickness(10, 0, 4, 0), Padding = new Thickness(10, 2, 10, 2) };
        saveButton.Click += (_, _) => Guard(() => Save(false));
        var saveNew = new Button { Content = "Save as new", Padding = new Thickness(10, 2, 10, 2) }; saveNew.Click += (_, _) => Guard(() => Save(true));
        bottom.Children.Add(saveButton); bottom.Children.Add(saveNew); bottom.Children.Add(status);
        root.Children.Add(bottom);

        var centre = new DockPanel(); root.Children.Add(centre);
        var stageBox = new StackPanel(); DockPanel.SetDock(stageBox, Dock.Top);
        stageBox.Children.Add(stage); stageBox.Children.Add(counter); centre.Children.Add(stageBox);
        centre.Children.Add(new ScrollViewer { Content = knobs, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 8, 0, 0) });

        stage.MouseLeftButtonDown += (_, e) => { var at = e.GetPosition(stage); Guard(() => Fire(at.X, at.Y)); };
        nameBox.LostFocus += (_, _) => { var n = PixelEditor.SafeName(nameBox.Text).Replace('-', '_'); if (n != fx.Id && n.Length > 0) { fx.Id = n; dirty = true; UpdateTitle(); } };
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.FocusedElement is TextBox) return;
            if (e.Key == Key.Space) { Guard(Fire); e.Handled = true; }
            else if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { Undo(); e.Handled = true; }
        };
        Closing += (_, e) =>
        {
            if (!dirty) return;
            var answer = MessageBox.Show(this, "Save this effect to the project before closing?", "Particle maker", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) e.Cancel = true;
            else if (answer == MessageBoxResult.Yes) { try { Save(false); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Particle maker"); e.Cancel = true; } }
        };
        Closed += (_, _) => clock.Stop();
        clock.Tick += (_, _) => Frame();
        Loaded += (_, _) => { clock.Start(); Fire(); };
        Build(); UpdateTitle();
    }

    // ---- the preview, running the shared simulation ----
    void Fire() => Fire(stage.ActualWidth > 0 ? stage.ActualWidth / 2 : 400, stage.ActualHeight > 0 ? stage.ActualHeight / 2 : 150);
    void Fire(double x, double y)
    {
        originX = x; originY = y; replayIn = 0;
        try { fx.Check(); } catch (InvalidDataException) { return; }
        if (fx.Emission == "burst") { for (int i = 0; i < fx.Count && live.Count < Particles.MaxParticles; i++) live.Add(fx.Spawn(random, x, y)); }
        else { streamLeft = fx.Duration > 0 ? fx.Duration : double.PositiveInfinity; streamCarry = 0; }
    }
    double originX = 400, originY = 150;
    void Frame()
    {
        var now = DateTime.UtcNow; double dt = Math.Min(0.1, (now - last).TotalSeconds); last = now;
        bool valid = true; try { fx.Check(); } catch (InvalidDataException) { valid = false; }
        if (valid && fx.Emission == "stream" && streamLeft > 0)
        {
            streamLeft -= dt; streamCarry += fx.Count * dt;
            int n = (int)streamCarry; streamCarry -= n;
            for (int i = 0; i < n && live.Count < Particles.MaxParticles; i++) live.Add(fx.Spawn(random, originX, originY));
        }
        for (int i = live.Count - 1; i >= 0; i--) if (!fx.Step(live[i], dt)) live.RemoveAt(i);
        // A burst that has finished plays again, so a short effect can be watched without clicking each time.
        if (valid && loopPreview.IsChecked == true && live.Count == 0 && (fx.Emission == "burst" || streamLeft <= 0))
        { replayIn -= dt; if (replayIn <= 0) { replayIn = 0.6; Fire(originX, originY); } }
        Paint();
    }
    void Paint()
    {
        while (shapes.Count < live.Count)
        {
            Shape s = fx.Shape == "circle" ? new Ellipse() : new Rectangle();
            shapes.Add(s); stage.Children.Add(s);
        }
        // The shape kind can change under us, so rebuild the pool when it no longer matches.
        bool wantEllipse = fx.Shape == "circle";
        if (shapes.Count > 0 && shapes[0] is Ellipse != wantEllipse) { stage.Children.Clear(); shapes.Clear(); Paint(); return; }
        for (int i = 0; i < shapes.Count; i++)
        {
            if (i >= live.Count) { shapes[i].Visibility = Visibility.Collapsed; continue; }
            var p = live[i]; var (size, rgb, alpha) = fx.At(p);
            if (size <= 0.05 || alpha <= 0.004) { shapes[i].Visibility = Visibility.Collapsed; continue; }
            var s = shapes[i];
            s.Visibility = Visibility.Visible; s.Width = size; s.Height = fx.Shape == "line" ? Math.Max(1, size / 2) : size;
            s.Fill = new SolidColorBrush(Color.FromRgb((byte)rgb.R, (byte)rgb.G, (byte)rgb.B));
            s.Opacity = Math.Clamp(alpha, 0, 1);
            Canvas.SetLeft(s, p.X - size / 2); Canvas.SetTop(s, p.Y - s.Height / 2);
            s.RenderTransform = fx.Shape == "line" ? new RotateTransform(Math.Atan2(p.VY, p.VX) * 180 / Math.PI, size / 2, s.Height / 2)
                : p.Spin != 0 ? new RotateTransform(p.Angle * 180 / Math.PI, size / 2, size / 2) : null;
        }
        counter.Text = live.Count + " alive" + (fx.Blend == "add" ? "   ·   glow is shown in the game, not here" : "");
    }

    // ---- editing ----
    void Push() { undo.Add(Json.Write(fx)); if (undo.Count > 100) undo.RemoveAt(0); }
    void Undo() { if (undo.Count == 0) return; fx = Json.Read<ParticleEffect>(undo[^1]); undo.RemoveAt(undo.Count - 1); Changed(true); }
    void Changed(bool rebuild)
    {
        dirty = true;
        if (rebuild) { Build(); live.Clear(); Fire(originX, originY); }
        UpdateTitle();
    }
    void Build()
    {
        loading = true; knobs.Children.Clear(); nameBox.Text = fx.Id;
        var emission = new ComboBox { ItemsSource = Particles.Emissions, SelectedItem = fx.Emission, Width = 130, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "A burst throws them all at once; a stream keeps making them" };
        emission.SelectionChanged += (_, _) => { if (loading) return; Push(); fx.Emission = (string)emission.SelectedItem; Changed(true); };
        knobs.Children.Add(Row("Emission", emission));
        Knob(fx.Emission == "burst" ? "Particles" : "Per second", 1, fx.Emission == "burst" ? 400 : 300, fx.Count, v => fx.Count = (int)Math.Round(v), v => $"{Math.Round(v):0}", 1);
        if (fx.Emission == "stream") Knob("Runs for", 0, 20, fx.Duration, v => fx.Duration = v, v => v < 0.05 ? "until stopped" : Secs(v));
        Knob("Life", 0.02, 6, fx.Life, v => fx.Life = v, Secs);
        Knob("Life varies", 0, 1, fx.LifeVariance, v => fx.LifeVariance = v, Pct);

        Section("Direction");
        Knob("Aim", 0, 359, fx.Direction, v => fx.Direction = Math.Round(v), v => $"{Math.Round(v):0}°", 1);
        Knob("Spread", 0, 360, fx.Spread, v => fx.Spread = v, v => v >= 359.5 ? "all round" : $"{v:0}°");
        Knob("Start radius", 0, 200, fx.Radius, v => fx.Radius = v, v => v < 0.5 ? "a point" : $"{v:0} px");

        Section("Movement");
        Knob("Speed", 0, 800, fx.Speed, v => fx.Speed = v, v => $"{v:0} px/s");
        Knob("Speed varies", 0, 1, fx.SpeedVariance, v => fx.SpeedVariance = v, Pct);
        Knob("Gravity", -1000, 1500, fx.Gravity, v => fx.Gravity = v, v => Math.Abs(v) < 1 ? "none" : $"{v:0} px/s²");
        Knob("Drag", 0, 8, fx.Drag, v => fx.Drag = v, v => v < 0.05 ? "none" : $"{v:0.0}");
        Knob("Spin", -720, 720, fx.Spin, v => fx.Spin = v, v => Math.Abs(v) < 1 ? "none" : $"{v:0} °/s");

        Section("Look");
        var shape = new ComboBox { ItemsSource = Particles.Shapes, SelectedItem = fx.Shape, Width = 130, HorizontalAlignment = HorizontalAlignment.Left };
        shape.SelectionChanged += (_, _) => { if (loading) return; Push(); fx.Shape = (string)shape.SelectedItem; Changed(true); };
        knobs.Children.Add(Row("Shape", shape));
        if (fx.Shape == "texture") knobs.Children.Add(Row("Texture", TexturePicker()));
        var blend = new ComboBox { ItemsSource = Particles.Blends, SelectedItem = fx.Blend, Width = 130, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "\"add\" makes them glow where they pile up: fire, sparks, magic" };
        blend.SelectionChanged += (_, _) => { if (loading) return; Push(); fx.Blend = (string)blend.SelectedItem; Changed(false); };
        knobs.Children.Add(Row("Blend", blend));
        Knob("Size at birth", 0, 40, fx.SizeStart, v => fx.SizeStart = v, v => $"{v:0.0} px");
        Knob("Size at death", 0, 40, fx.SizeEnd, v => fx.SizeEnd = v, v => $"{v:0.0} px");
        Knob("Size varies", 0, 1, fx.SizeVariance, v => fx.SizeVariance = v, Pct);
        knobs.Children.Add(Row("Colour", Ramp()));
        knobs.Children.Add(new TextBlock { Text = "Click the bar to add a colour, drag a marker to move it, click one to change it, right-click to remove. Left is the moment it is thrown, right is the moment it dies.", TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11, Margin = new Thickness(134, 0, 0, 6) });
        Knob("Opacity at birth", 0, 1, fx.OpacityStart, v => fx.OpacityStart = v, Pct);
        Knob("Opacity at death", 0, 1, fx.OpacityEnd, v => fx.OpacityEnd = v, Pct);
        loading = false;
    }
    static string Secs(double v) => v < 1 ? $"{v * 1000:0} ms" : $"{v:0.00} s";
    static string Pct(double v) => $"{v * 100:0}%";
    void Section(string text) => knobs.Children.Add(Header(text, new Thickness(0, 10, 0, 2)));
    static TextBlock Header(string text, Thickness? margin = null) => new() { Text = text, Foreground = Brushes.LightSkyBlue, FontWeight = FontWeights.SemiBold, Margin = margin ?? new Thickness(0, 0, 0, 4) };
    static UIElement Row(string label, UIElement control)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        var l = new TextBlock { Text = label, Width = 130, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(l, Dock.Left); row.Children.Add(l); row.Children.Add(control); return row;
    }
    /// <summary>The colour ramp: a gradient bar with a marker per stop. Reads the effect's own stops, or the pair it
    /// was saved with before ramps existed, and writes stops back either way.</summary>
    UIElement Ramp()
    {
        const double W = 300, H = 22, MARK = 9;
        var host = new Canvas { Width = W, Height = H + MARK + 4, HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent, ClipToBounds = false };
        var bar = new Rectangle { Width = W, Height = H, Stroke = Brushes.Gray, StrokeThickness = 1, Cursor = Cursors.Cross };
        Canvas.SetLeft(bar, 0); Canvas.SetTop(bar, 0); host.Children.Add(bar);

        // The effect keeps its stops sorted; the markers are rebuilt from them each time anything changes.
        List<ParticleStop> Stops()
        {
            if (fx.Colors.Count == 0)
                fx.Colors = [new ParticleStop { At = 0, Color = fx.ColorStart }, new ParticleStop { At = 1, Color = fx.ColorEnd }];
            return fx.Colors;
        }
        Color Of(string hex) => ColorPicker.TryColor(hex, out var c) ? c : Colors.Black;
        void Paint()
        {
            var ordered = Stops().OrderBy(x => x.At).ToList();
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            foreach (var stop in ordered) brush.GradientStops.Add(new GradientStop(Of(stop.Color), Math.Clamp(stop.At, 0, 1)));
            bar.Fill = brush;
            foreach (var old in host.Children.OfType<Polygon>().ToList()) host.Children.Remove(old);
            foreach (var stop in ordered)
            {
                var stopRef = stop;
                var mark = new Polygon
                {
                    Points = [new Point(0, MARK), new Point(MARK / 2, 0), new Point(MARK, MARK)],
                    Fill = new SolidColorBrush(Of(stop.Color)), Stroke = Brushes.WhiteSmoke, StrokeThickness = 1,
                    Cursor = Cursors.SizeWE, ToolTip = $"{stop.At * 100:0}% through its life. Click to recolour, right-click to remove."
                };
                Canvas.SetLeft(mark, Math.Clamp(stop.At, 0, 1) * W - MARK / 2); Canvas.SetTop(mark, H + 2);
                bool dragged = false;
                mark.MouseLeftButtonDown += (_, e) => { dragged = false; Push(); mark.CaptureMouse(); e.Handled = true; };
                mark.MouseMove += (_, e) =>
                {
                    if (!mark.IsMouseCaptured) return;
                    dragged = true;
                    stopRef.At = Math.Clamp(e.GetPosition(host).X / W, 0, 1);
                    Paint(); Changed(false);
                };
                mark.MouseLeftButtonUp += (_, e) =>
                {
                    mark.ReleaseMouseCapture(); e.Handled = true;
                    if (dragged) return;
                    // A click that did not move it opens the picker for that stop.
                    var picker = new ColorPicker(this, stopRef.Color, hex =>
                    {
                        if (!ColorPicker.TryColor(hex, out var c)) return;
                        stopRef.Color = $"#{c.R:X2}{c.G:X2}{c.B:X2}"; Paint(); Changed(false);
                    });
                    picker.ShowDialog();
                };
                mark.MouseRightButtonDown += (_, e) =>
                {
                    e.Handled = true;
                    if (Stops().Count <= 2) { status.Text = "A ramp keeps at least two colours."; return; }
                    Push(); fx.Colors.Remove(stopRef); Paint(); Changed(false);
                };
                host.Children.Add(mark);
            }
        }
        // Clicking the bar adds a stop there, coloured as the ramp already is at that point.
        bar.MouseLeftButtonDown += (_, e) =>
        {
            if (Stops().Count >= Particles.MaxStops) { status.Text = $"A ramp holds at most {Particles.MaxStops} colours."; return; }
            double at = Math.Clamp(e.GetPosition(host).X / W, 0, 1);
            var (r, g, b) = fx.ColourAt(at);
            Push(); fx.Colors.Add(new ParticleStop { At = at, Color = $"#{r:X2}{g:X2}{b:X2}" });
            Paint(); Changed(false);
        };
        Paint();
        return host;
    }

    /// <summary>Picks one of the project's images for a texture particle. Typing a resource ID still works, but
    /// nobody should have to know one: the list is every picture the project already has.</summary>
    UIElement TexturePicker()
    {
        var row = new DockPanel { HorizontalAlignment = HorizontalAlignment.Left, Width = 330 };
        var add = new Button { Content = "Import…", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 1, 8, 1), ToolTip = "Bring a PNG into the project and use it here." };
        DockPanel.SetDock(add, Dock.Right); row.Children.Add(add);
        var box = new ComboBox { IsEditable = true, Text = fx.Texture,
            ToolTip = "A picture from this project. Small and roughly square works best — it is drawn at the particle's size." };
        var pictures = pickTextures();
        box.ItemsSource = pictures;
        void Commit(string value)
        {
            value = (value ?? "").Trim();
            if (value == fx.Texture) return;
            if (value.Length > 0 && !pictures.Contains(value) && !Validation.Resource(value))
            { status.Text = "Pick a picture from the list, or type one as projectid:textures/gui/image/name.png"; box.Text = fx.Texture; return; }
            Push(); fx.Texture = value; Changed(false);
        }
        box.SelectionChanged += (_, _) => { if (!loading && box.SelectedItem is string s) Commit(s); };
        box.LostKeyboardFocus += (_, _) => { if (!loading) Commit(box.Text); };
        add.Click += (_, _) => Guard(() =>
        {
            var picked = addTexture(); if (picked == null) return;
            pictures = pickTextures(); box.ItemsSource = pictures;
            Push(); fx.Texture = picked; box.Text = picked; Changed(false);
            status.Text = "Imported " + picked.Split('/').Last() + ".";
        });
        row.Children.Add(box);
        return row;
    }

    UIElement Swatch(string current, Action<string> set)
    {
        var box = new Border { Width = 40, Height = 18, CornerRadius = new CornerRadius(2), BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand };
        void Show(string hex) { box.Background = ColorPicker.TryColor(hex, out var c) ? new SolidColorBrush(c) : Brushes.Black; }
        Show(current);
        box.MouseLeftButtonDown += (_, _) =>
        {
            Push();
            var picker = new ColorPicker(this, current, hex =>
            {
                // The runtime's particle colours are solid; an alpha picked here would be ignored, so it is dropped.
                if (!ColorPicker.TryColor(hex, out var c)) return;
                current = $"#{c.R:X2}{c.G:X2}{c.B:X2}"; set(current); Show(current); Changed(false);
            });
            picker.ShowDialog();
        };
        return box;
    }
    void Knob(string label, double min, double max, double value, Action<double> set, Func<double, string> show, double tick = 0)
    {
        var text = new TextBlock { Text = show(value), Width = 96, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), IsSnapToTickEnabled = tick > 0, TickFrequency = tick > 0 ? tick : (max - min) / 300, VerticalAlignment = VerticalAlignment.Center };
        slider.PreviewMouseLeftButtonDown += (_, _) => Push();
        slider.ValueChanged += (_, _) => { if (loading) return; set(slider.Value); text.Text = show(slider.Value); Changed(false); };
        var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
        var l = new TextBlock { Text = label, Width = 130, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(l, Dock.Left); row.Children.Add(l);
        DockPanel.SetDock(text, Dock.Right); row.Children.Add(text); row.Children.Add(slider);
        knobs.Children.Add(row);
    }
    void Save(bool asNew)
    {
        var name = PixelEditor.SafeName(nameBox.Text).Replace('-', '_');
        if (name.Length == 0) throw new InvalidDataException("Give the effect a name.");
        fx.Id = name; fx.Check();
        savedAs = save(new SaveRequest(fx.Copy(), asNew, asNew ? null : savedAs));
        dirty = false; UpdateTitle(); status.Text = "Saved as " + savedAs + ". Add a Particles control and choose it.";
    }
    void UpdateTitle() => Title = "Particle maker — " + (savedAs ?? fx.Id + " (not saved yet)") + (dirty ? " •" : "");
    void Guard(Action action) { try { action(); } catch (OperationCanceledException) { } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Particle maker", MessageBoxButton.OK, MessageBoxImage.Warning); } }
}
