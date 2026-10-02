using Wysicraft.Core;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Wysicraft.Models;
using Wysicraft.Packaging;
using Element = Wysicraft.Models.Element;
namespace Wysicraft.Designer;

public partial class MainWindow
{
    bool syncingLayers;
    void VerifyAppearanceTools()
    {
        var original = Json.CloneProject(project);
        try
        {
            var button = ui.Elements.First(e => e.Type == "button");
            var label = ui.Elements.First(e => e.Type == "label");
            label.Background = "#123456"; label.BorderColor = "#ABCDEF"; label.BorderWidth = 2; label.TextShadow = true; label.ShadowOffsetX = 3; label.ShadowBlur = 4;
            if (RenderControl(label, true, null) is not Border frame || frame.Background is not SolidColorBrush fill || fill.Color != Color.FromRgb(0x12, 0x34, 0x56) || frame.BorderThickness.Left != 2 * Zoom || frame.Child is not TextBlock labelText || labelText.Effect is not DropShadowEffect shadow || shadow.BlurRadius != 4 * Zoom) throw new InvalidOperationException("Label fill, border or shadow failed");
            label.FillEnabled = false;
            if (FrameBrush(label) != Brushes.Transparent) throw new InvalidOperationException("Disabled fill was not transparent");
            label.FillEnabled = true;
            SelectCanvasElement(button.Id, System.Windows.Input.ModifierKeys.None);
            SelectCanvasElement(label.Id, System.Windows.Input.ModifierKeys.Shift);
            if (selected.Count != 2 || !selected.Contains(button.Id) || !selected.Contains(label.Id)) throw new InvalidOperationException("Shift multi-selection failed");
            button.Events["click"].Client.Actions.Add(new VisualAction { Type = "set_text", Target = label.Id, Value = "Copied link" });
            var copied = Json.Write(ui.Elements.Where(e => selected.Contains(e.Id)).ToList());
            InsertCopies(Json.Read<List<Element>>(copied));
            var pasted = ui.Elements.Where(e => selected.Contains(e.Id)).ToList();
            if (pasted.Count != 2 || pasted.First(e => e.Type == "button").Events["click"].Client.Actions.Last().Target != pasted.First(e => e.Type == "label").Id) throw new InvalidOperationException("Multi-element paste did not remap action targets");
            selected.Clear(); selected.Add(button.Id); Draw();
            if (!Layers.SelectedItems.Cast<ListBoxItem>().Any(item => Equals(item.Tag, button.Id))) throw new InvalidOperationException("Layers selection not synchronized");
            int count = ui.Elements.Count; Duplicate();
            if (ui.Elements.Count != count + 1 || ui.Elements.Select(e => e.Id).Distinct().Count() != ui.Elements.Count) throw new InvalidOperationException("Duplicate failed");
            button.CornerRadius = 7; button.Bold = true; button.Italic = true; button.Font = "minecraft:uniform";
            var pixels = new byte[1300 * 1300 * 4]; new Random(42).NextBytes(pixels);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1300, 1300, 96, 96, PixelFormats.Bgra32, null, pixels, 1300 * 4)));
            using var data = new MemoryStream(); png.Save(data); project.Assets["assets/textures/smoke.png"] = data.ToArray(); button.Texture = project.Manifest.Id + ":smoke.png";
            if (data.Length <= 4 * 1024 * 1024) throw new InvalidOperationException("Large-image fixture is too small");
            string packPath = Path.Combine(Path.GetTempPath(), "wysicraft-image-" + Guid.NewGuid() + ".wysicraft");
            try { ProjectStore.Export(project, packPath); if (!ProjectStore.Load(packPath).Assets[Wysicraft.Core.TextureAssets.Path(project.Manifest.Id, "smoke.png")].SequenceEqual(data.ToArray())) throw new InvalidOperationException("Large image was not preserved"); }
            finally { if (File.Exists(packPath)) File.Delete(packPath); }
            if (SkinBrush(button) is not ImageBrush || RenderControl(button, true, null) is not Button view || view.Content is not TextBlock text || text.FontWeight != FontWeights.Bold) throw new InvalidOperationException("Skin/font preview failed");
            if (!ColorPicker.TryColor("#8044AAEE", out var color) || ColorPicker.ToHex(color) != "#8044AAEE") throw new InvalidOperationException("Color hex conversion failed");
        }
        finally { project = original; ui = project.Screens.First(s => s.Id == ui.Id); selected.Clear(); dirty = false; history.Clear(); Draw(); RefreshInspector(); }
    }
    void ColorField(StackPanel panel, string label, object obj, string name)
    {
        var property = obj.GetType().GetProperty(name)!;
        var row = new DockPanel(); row.Children.Add(new TextBlock { Text = label, Width = 90, Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Center });
        string current = (string)property.GetValue(obj)!;
        var hex = new TextBox { Text = current, MinWidth = 85, ToolTip = "Type #RRGGBB or #AARRGGBB" };
        var selector = new Button { Content = "◉", Width = 32, Padding = new Thickness(2), Background = Brush(current), ToolTip = "Open color wheel" };
        DockPanel.SetDock(selector, Dock.Right); row.Children.Add(selector); row.Children.Add(hex); panel.Children.Add(row);
        bool transaction = false, updating = false;
        void Apply(string color)
        {
            if (color == current) return;
            if (!transaction) { Change(); transaction = true; }
            property.SetValue(obj, color); current = color; selector.Background = Brush(color); Draw();
        }
        hex.TextChanged += (_, _) => { if (updating) return; if (ColorPicker.TryColor(hex.Text, out var color)) { hex.BorderBrush = Brushes.Gray; Apply(ColorPicker.ToHex(color)); } else hex.BorderBrush = Brushes.IndianRed; };
        hex.LostKeyboardFocus += (_, _) => { updating = true; hex.Text = current; updating = false; transaction = false; };
        selector.Click += (_, _) =>
        {
            transaction = false;
            var picker = new ColorPicker(this, current, color => { Apply(color); updating = true; hex.Text = color; updating = false; }); picker.ShowDialog(); transaction = false;
        };
    }
    void InitializeLayers()
    {
        Layers.SelectionChanged += (_, _) =>
        {
            if (syncingLayers) return;
            selected.Clear(); foreach (ListBoxItem item in Layers.SelectedItems) foreach(string id in LayerRowIds((string)item.Tag)) if(ui.Elements.Any(e=>e.Id==id&&InIsolation(e)))selected.Add(id);
            Draw(); RefreshInspector();
        };
        InitializeLayerDragging();
        LayerUp.Click += (_, _) => MoveLayers(1);
        LayerDown.Click += (_, _) => MoveLayers(-1);
        LayerTop.Click += (_, _) => Guard(BringToFront);
        LayerBottom.Click += (_, _) => Guard(SendToBack);
        LayerDuplicate.Click += (_, _) => Duplicate();
        LayerNewGroup.Click += (_, _) => Guard(NewLayerGroup);
    }
    void RefreshLayers() => RefreshLayerRows();
    ContextMenu ElementMenu(Element element)
    {
        var menu = new ContextMenu(); FillElementMenu(menu, element); return menu;
    }
    void FillElementMenu(ContextMenu menu, Element element)
    {
        menu.Items.Clear();
        var group=CanvasGroup(element);
        if(group.Length>0){var isolate=new MenuItem {Header="Isolate group"};isolate.Click+=(_,_)=>IsolateGroup(group);menu.Items.Add(isolate);}
        if(isolatedGroup.Length>0){var exit=new MenuItem {Header="Exit isolation"};exit.Click+=(_,_)=>ExitIsolation();menu.Items.Add(exit);}
        // Select the right-clicked element when executing an action. Keeping its
        // visual alive while the menu opens avoids losing the WPF placement target.
        void SelectTarget() { if (!selected.Contains(element.Id)) { selected.Clear(); selected.Add(element.Id); } }
        MenuItem Item(string label, System.Action action)
        {
            var item = new MenuItem { Header = label, InputGestureText = ContextShortcut(label) };
            item.Click += (_, _) => Guard(() => { SelectTarget(); action(); }); return item;
        }
        foreach (var (label, action) in new (string, System.Action)[] {
            ("Select", () => { selected.Clear(); selected.Add(element.Id); Draw(); RefreshInspector(); }),
            ("Group selected", GroupSelected), ("Ungroup", UngroupSelected), ("Duplicate", Duplicate), ("Rename…", () => RenameElement(element)),
            (element.Visible ? "Hide" : "Show", () => { Change(); element.Visible = !element.Visible; Draw(); RefreshInspector(); }),
            (element.Locked ? "Unlock" : "Lock", () => { Change(); SetLocked([element], !element.Locked); Draw(); RefreshInspector(); }),
            ("Delete", Delete) })
            menu.Items.Add(Item(label, action));
        // Drawing order and lining things up each get their own submenu.
        var order = new MenuItem { Header = "Arrange" };
        foreach (var (label, action) in new (string, System.Action)[] { ("Bring to front", BringToFront), ("Bring forward", () => MoveLayers(1)), ("Send backward", () => MoveLayers(-1)), ("Send to back", SendToBack) })
            order.Items.Add(Item(label, action));
        menu.Items.Add(new Separator()); menu.Items.Add(order); menu.Items.Add(ArrangeMenu(SelectTarget));
        // Pictures: open this control's image in the pixel editor, and a sprite's clips in the sprite sheet editor.
        if (element.Type is "image" or "sprite" or "texture_region" || element.Texture.Length > 0)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Item(element.Texture.Length > 0 ? "Edit in pixel editor…" : "Draw in pixel editor…", () => PixelEditorForElement(element)));
            if (element.Type == "sprite") menu.Items.Add(Item("Sprite sheet editor…", () => ShowSpriteSheetEditor(element)));
            if (element.Type == "sprite" && AdvancedAllowed) menu.Items.Add(Item("State graphs…", ShowStateGraphsWindow));
            if (element.Type == "tilemap") menu.Items.Add(Item("Tile painter…", () => ShowTilemapEditor(element)));
        }
        AddPanelMenuItems(menu, element);
    }
    void MoveLayers(int direction)
    {
        if(selected.Count==0)return;
        var front=ui.Elements.AsEnumerable().Reverse().ToList();
        int index=direction>0?front.FindIndex(e=>selected.Contains(e.Id))-1:front.FindLastIndex(e=>selected.Contains(e.Id))+1;
        if(index<0 || index>=front.Count)return;
        ReorderLayerDrop(selected.ToArray(),front[index].Id,direction<0);
    }
    void Duplicate()
    {
        InsertCopies(ContainerTree.Moving(ui,selected).ToList());
    }
    void InsertCopies(List<Element> originals,Dictionary<string,string>? sourceGroups=null)
    {
        if (originals.Count == 0) return;
        Change(); var clones = originals.Select(Json.Clone).ToList(); var ids = new Dictionary<string, string>();
        var sourceUi=new UiDefinition {GroupParents=sourceGroups ?? new(ui.GroupParents)};
        var groups=clones.SelectMany(e=>LayerGroups.Path(sourceUi,e.LayerGroup)).Distinct().ToDictionary(g=>g,g=>UniqueLayerGroup(g+" copy"));
        foreach(var (oldGroup,newGroup) in groups)ui.GroupParents[newGroup]=groups.GetValueOrDefault(sourceUi.GroupParents.GetValueOrDefault(oldGroup,""),"");
        foreach(var clone in clones) if(groups.TryGetValue(clone.LayerGroup,out var group)) clone.LayerGroup=group;
        foreach (var clone in clones) { string old = clone.Id; clone.Id = Unique(old); ids.Add(old, clone.Id); ui.Elements.Add(clone); }
        foreach (var clone in clones)
        {
            clone.Bounds.X += Math.Max(1, project.Manifest.GridSize); clone.Bounds.Y += Math.Max(1, project.Manifest.GridSize);
            if (ids.TryGetValue(clone.Parent, out var parent)) clone.Parent = parent;
            else if(!ui.Elements.Any(e=>e.Id==clone.Parent))clone.Parent="";
            foreach (var ev in clone.Events.Values) foreach (var handler in new[] { ev.Client, ev.Server }) foreach (var action in handler.Actions)
                if (action.Type is "set_text" or "set_visible" or "set_enabled" or "set_value" or "change_texture" && ids.TryGetValue(action.Target, out var target)) action.Target = target;
        }
        selected.Clear(); foreach (var clone in clones) selected.Add(clone.Id);
        Draw(); RefreshInspector();
    }
    void BuildAppearance(Element e)
    {
        Heading(Properties, "Appearance"); Field(Properties, "Text", e, "Text");
        Field(Properties, "Fill enabled", e, "FillEnabled");
        Field(Properties, "Text color", e, "Foreground"); Field(Properties, "Fill color", e, "Background");
        var colors = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
        foreach (string color in new[] { "#40464F", "#176B91", "#2D8156", "#A84646", "#BE842B", "#FFFFFF", "#000000" })
        {
            var swatch = new Button { Width = 27, Height = 22, Background = Brush(color), ToolTip = "Fill " + color, Padding = new Thickness(0) };
            swatch.Click += (_, _) => { Change(); e.Background = color; e.Texture = ""; Draw(); RefreshInspector(); }; colors.Children.Add(swatch);
        }
        Properties.Children.Add(colors);
        Field(Properties, "Skin image", e, "Texture");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var browse = new Button { Content = "Choose PNG…" }; browse.Click += (_, _) => Guard(() => AssignSkin(e));
        var clear = new Button { Content = "Use color only" }; clear.Click += (_, _) => { Change(); e.Texture = ""; Draw(); RefreshInspector(); };
        var assets=new Button {Content="Assets"};assets.Click+=(_,_)=>{RefreshAssetBrowser();ShowDock("assets");};buttons.Children.Add(browse); buttons.Children.Add(assets); buttons.Children.Add(clear);
        var pixels = new Button { Content = e.Texture.Length > 0 ? "Edit pixels…" : "Draw…", ToolTip = e.Texture.Length > 0 ? "Edit this image in the pixel editor" : "Draw a new image in the pixel editor" };
        pixels.Click += (_, _) => Guard(() => PixelEditorForElement(e)); buttons.Children.Add(pixels); Properties.Children.Add(buttons);
        Properties.Children.Add(new TextBlock { Text = "An assigned image replaces the fill. Transparent pixels reveal the canvas.", Margin = new Thickness(4), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray });
        Field(Properties, "Opacity", e, "Opacity");
        // Web & desktop: the control turned and resized about its centre. A panel carries what's attached inside it.
        if (AdvancedAllowed && !Untransformed(e))
        {
            Field(Properties, "Rotation (°)", e, "Rotation"); Field(Properties, "Scale", e, "Scale");
            Properties.Children.Add(new TextBlock { Text = "Turned clockwise and resized about the centre (web & desktop). A panel takes everything attached inside it along. Physics bodies and colliders stay as they are.", Margin = new Thickness(4), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray });
        }
        Heading(Properties, "Corners");
        var radiusLabel = new TextBlock { Text = $"Radius: {e.CornerRadius:0} px", Margin = new Thickness(4) };
        var radius = new Slider { Minimum = 0, Maximum = 128, Value = e.CornerRadius, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(4), ToolTip = "Corner radius in Minecraft pixels; clamped to half the control size." };
        bool changing = false;
        radius.ValueChanged += (_, _) => { if (!changing) { Change(); changing = true; } e.CornerRadius = radius.Value; radiusLabel.Text = $"Radius: {e.CornerRadius:0} px"; Draw(); };
        radius.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => changing = false));
        radius.LostKeyboardFocus += (_, _) => changing = false;
        Properties.Children.Add(radiusLabel); Properties.Children.Add(radius);
        Heading(Properties, "Border");
        Field(Properties, "Border color", e, "BorderColor");
        NumericSlider(Properties, "Width (px)", e.BorderWidth, 0, 32, value => e.BorderWidth = value);
        Heading(Properties, "Font");
        // Both of these used to be bare dropdowns with nothing naming them: "Typeface" sat under the Font heading and
        // "Alignment" sat under Underline, looking like it belonged to it.
        var fontRow = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        fontRow.Children.Add(new TextBlock { Text = "Typeface", Width = FieldLabelWidth, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) });
        var font = new ComboBox { IsEditable = true, ItemsSource = FontChoices(), Text = e.Font };
        void SetFont(string? name) { if (name == e.Font || name == null || !Wysicraft.Core.Validation.Resource(name)) return; Change(); e.Font = name; Draw(); RefreshInspector(); }
        font.SelectionChanged += (_, _) => SetFont(font.SelectedItem as string);
        font.LostKeyboardFocus += (_, _) => SetFont(font.Text);
        fontRow.Children.Add(font); Properties.Children.Add(fontRow);
        if (AdvancedAllowed)
        {
            var addFont = new Button { Content = "Import a font…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 2, 0, 2), ToolTip = "Bring a .ttf, .otf or .woff2 into the project and use it here. Web and desktop only — Minecraft draws its own fonts." };
            addFont.Click += (_, _) => Guard(() => { if (ImportFontFile() is string added) { Change(); e.Font = added; Draw(); RefreshInspector(); } });
            Properties.Children.Add(addFont);
        }
        Field(Properties, "Size scale", e, "FontScale");
        foreach (string property in new[] { "Bold", "Italic", "Underline" }) Field(Properties, property, e, property);
        // Web & desktop: several lines instead of one cut off at the control's width.
        if (AdvancedAllowed && e.Type is "label" or "button") Field(Properties, "Wrap text", e, "Wrap");
        var alignRow = new DockPanel { Margin = new Thickness(0, 2, 0, 2), ToolTip = "How the text sits inside the control." };
        alignRow.Children.Add(new TextBlock { Text = "Alignment", Width = FieldLabelWidth, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) });
        var align = new ComboBox { ItemsSource = new[] { "left", "center", "right" }, SelectedItem = e.Alignment };
        align.SelectionChanged += (_, _) => { if (align.SelectedItem is string value && value != e.Alignment) { Change(); e.Alignment = value; Draw(); } };
        alignRow.Children.Add(align); Properties.Children.Add(alignRow);
        Properties.Children.Add(new TextBlock { Text = FontNote(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4), Foreground = Brushes.LightGray });
        Heading(Properties, "Text shadow");
        Field(Properties, "Enabled", e, "TextShadow"); Field(Properties, "Shadow color", e, "ShadowColor");
        NumericSlider(Properties, "Opacity", e.ShadowOpacity, 0, 1, value => e.ShadowOpacity = value, .05);
        NumericSlider(Properties, "Offset X (px)", e.ShadowOffsetX, -64, 64, value => e.ShadowOffsetX = value);
        NumericSlider(Properties, "Offset Y (px)", e.ShadowOffsetY, -64, 64, value => e.ShadowOffsetY = value);
        NumericSlider(Properties, "Blur (px)", e.ShadowBlur, 0, 16, value => e.ShadowBlur = value);
    }
    void NumericSlider(StackPanel panel, string label, double initial, double min, double max, System.Action<double> update, double step = 1)
    {
        var text = new TextBlock { Text = $"{label}: {initial:0.##}", Margin = new Thickness(4) };
        var slider = new Slider { Minimum = min, Maximum = max, Value = initial, TickFrequency = step, IsSnapToTickEnabled = true, Margin = new Thickness(4) }; bool changing = false;
        slider.ValueChanged += (_, _) => { if (!changing) { Change(); changing = true; } update(slider.Value); text.Text = $"{label}: {slider.Value:0.##}"; Draw(); };
        slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => changing = false)); slider.LostKeyboardFocus += (_, _) => changing = false;
        panel.Children.Add(text); panel.Children.Add(slider);
    }
    void AssignSkin(Element element)
    {
        var dialog = new OpenFileDialog { Filter = "PNG skin|*.png", Title = "Choose control skin" }; if (dialog.ShowDialog() != true) return;
        var bytes = File.ReadAllBytes(dialog.FileName); ProjectStore.TextureSize(bytes);
        DecodeTexture(bytes);
        string stem = System.Text.RegularExpressions.Regex.Replace(Path.GetFileNameWithoutExtension(dialog.FileName).ToLowerInvariant(), "[^a-z0-9_-]", "_");
        string name = stem + ".png"; int suffix = 1;
        while (Wysicraft.Core.TextureAssets.TryGet(project, Wysicraft.Core.TextureAssets.Resource(project.Manifest.Id, name, element.Type), out var existing) && !existing.SequenceEqual(bytes)) name = stem + "_" + suffix++ + ".png";
        Change(); project.Assets[Wysicraft.Core.TextureAssets.Path(project.Manifest.Id, name, element.Type)] = bytes; element.Texture = Wysicraft.Core.TextureAssets.Resource(project.Manifest.Id, name, element.Type); if (element.Type is not ("image" or "texture_region")) element.FillEnabled = true; RefreshAssetBrowser(); Draw(); RefreshInspector();
    }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[], BitmapImage> TexturePreviews = new();
    static BitmapImage DecodeTexture(byte[] bytes) => TexturePreviews.GetValue(bytes, DecodePreview);
    static BitmapImage DecodePreview(byte[] bytes)
    {
        var size = ProjectStore.TextureSize(bytes);
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        // Keep full-resolution source bytes in the pack, but bound desktop preview memory.
        if (Math.Max(size.Width, size.Height) > 2048) { if (size.Width >= size.Height) image.DecodePixelWidth = 2048; else image.DecodePixelHeight = 2048; }
        image.StreamSource = new MemoryStream(bytes); image.EndInit(); image.Freeze(); return image;
    }
    Brush SkinBrush(Element e)
    {
        if (!e.FillEnabled) return Brushes.Transparent;
        if (e.Texture.Length == 0) return Brush(e.Background);
        if (TryTexture(e.Texture, out var bytes))
            try { return TextureBrush(e.Texture, bytes); } catch { return Brushes.Magenta; }
        return Brush(e.Background);
    }
    Brush FrameBrush(Element e) => e.Type is "shape" or "collider" or "camera" ? Brushes.Transparent : e.Type is "image" or "texture_region" or "sprite" ? e.FillEnabled ? Brush(e.Background) : Brushes.Transparent : SkinBrush(e);
    Border DecorateControl(FrameworkElement content, Element e)
    {
        if (content is Control control) { control.Background = Brushes.Transparent; control.BorderThickness = new Thickness(0); }
        if (content is TextBlock text) { text.Background = Brushes.Transparent; text.VerticalAlignment = VerticalAlignment.Center; }
        bool drawsOwn = e.Type is "shape" or "collider" or "camera"; // shapes draw their own fill and outline
        var frame = new Border { Tag = "appearance", Background = drawsOwn ? Brushes.Transparent : FrameBrush(e), BorderBrush = Brush(e.BorderColor), BorderThickness = new Thickness(drawsOwn ? 0 : Math.Clamp(e.BorderWidth, 0, 32) * Zoom), CornerRadius = new CornerRadius(Math.Clamp(e.CornerRadius, 0, 128) * Zoom), Child = content };
        if (content is System.Windows.Controls.Image or Grid) content.SizeChanged += (_, _) => { double r = Math.Max(0, e.CornerRadius - e.BorderWidth) * Zoom; content.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, content.ActualWidth), Math.Max(0, content.ActualHeight)), r, r); };
        return frame;
    }
    ControlTemplate SkinTemplate(Element e)
    {
        var border = new FrameworkElementFactory(typeof(Border), "Skin");
        border.SetValue(Border.BackgroundProperty, SkinBrush(e));
        border.SetValue(Border.BorderBrushProperty, Brush(e.BorderColor));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(Math.Clamp(e.BorderWidth, 0, 32) * Zoom));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(Math.Min(e.CornerRadius, Math.Min(e.Bounds.Width, e.Bounds.Height) / 2) * Zoom));
        border.SetValue(Border.PaddingProperty, new Thickness(4, 0, 4, 0));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(UIElement.OpacityProperty, .9, "Skin")); template.Triggers.Add(hover);
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true }; pressed.Setters.Add(new Setter(UIElement.OpacityProperty, .65, "Skin")); template.Triggers.Add(pressed);
        return template;
    }
    TextBlock StyledText(Element e)
    {
        var text = new TextBlock { Text = e.Wrap ? WrappedText(e.Text) : e.Text, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = e.Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap }; ApplyFont(text, e); return text;
    }
    /// <summary>Wrapped text takes a typed \n as a line break, as the runtime does.</summary>
    static string WrappedText(string text) => text.Replace("\\n", "\n");
    /// <summary>Kinds the runtime never turns or scales: they aren't one rectangle (or aren't drawn at all).</summary>
    static bool Untransformed(Element e) => e.Type is "tilemap" or "particles" or "camera" or "sound" or "collider";
    static bool Turned(Element e) => (e.Rotation != 0 || e.Scale != 1) && !Untransformed(e);
    /// <summary>One control's turn about its own centre, written in the coordinates of the control being drawn
    /// (relativeTo): what the canvas needs to show a control inside a turned panel where the game draws it.</summary>
    static Transform TurnOf(Element turnedOne, Element relativeTo)
    {
        double cx = (turnedOne.Bounds.X + turnedOne.Bounds.Width / 2 - relativeTo.Bounds.X) * 2, cy = (turnedOne.Bounds.Y + turnedOne.Bounds.Height / 2 - relativeTo.Bounds.Y) * 2;
        var turn = new TransformGroup();
        if (turnedOne.Scale != 1) turn.Children.Add(new ScaleTransform(turnedOne.Scale, turnedOne.Scale, cx, cy));
        if (turnedOne.Rotation != 0) turn.Children.Add(new RotateTransform(turnedOne.Rotation, cx, cy));
        return turn;
    }
    /// <summary>The canvas transform for a control, as the runtime draws it: its own turn first, then each turned panel
    /// it is attached inside, from the nearest outwards. Null when nothing is turned. own is the control's own part.</summary>
    Transform? CanvasTransform(Element e, out Transform? own)
    {
        own = Turned(e) ? TurnOf(e, e) : null;
        var all = new TransformGroup(); if (own != null) all.Children.Add(own);
        foreach (var panel in ContainerTree.Ancestors(ui, e)) if (Turned(panel)) all.Children.Add(TurnOf(panel, e));
        return all.Children.Count == 0 ? null : all;
    }
    void ApplyFont(FrameworkElement widget, Element e)
    {
        var family = FamilyFor(e.Font);
        double size = Math.Clamp(9 * Zoom * e.FontScale, 6, 144);
        if (widget is Control control) { control.FontFamily = family; control.FontSize = size; control.FontWeight = e.Bold ? FontWeights.Bold : FontWeights.Normal; control.FontStyle = e.Italic ? FontStyles.Italic : FontStyles.Normal; control.Foreground = Brush(e.Foreground); }
        if (widget is TextBlock text)
        {
            text.FontFamily = family; text.FontSize = size; text.FontWeight = e.Bold ? FontWeights.Bold : FontWeights.Normal; text.FontStyle = e.Italic ? FontStyles.Italic : FontStyles.Normal;
            text.Foreground = Brush(e.Foreground); text.TextAlignment = e.Alignment == "center" ? TextAlignment.Center : e.Alignment == "right" ? TextAlignment.Right : TextAlignment.Left;
            if (e.Underline) text.TextDecorations = TextDecorations.Underline;
        }
            if (e.TextShadow && widget is TextBlock or TextBox or CheckBox or ComboBox)
            {
                var color = ColorPicker.TryColor(e.ShadowColor, out var parsed) ? parsed : Colors.Black;
                double distance = Math.Sqrt(e.ShadowOffsetX * e.ShadowOffsetX + e.ShadowOffsetY * e.ShadowOffsetY);
                widget.Effect = new DropShadowEffect { Color = Color.FromRgb(color.R, color.G, color.B), Opacity = Math.Clamp(e.ShadowOpacity, 0, 1) * color.A / 255d, BlurRadius = Math.Clamp(e.ShadowBlur, 0, 16) * Zoom, ShadowDepth = distance * Zoom, Direction = (360 - Math.Atan2(e.ShadowOffsetY, e.ShadowOffsetX) * 180 / Math.PI) % 360 };
        }
    }
}



public partial class MainWindow
{
    // Shortcut text for right-click menu entries that match a command.
    string ContextShortcut(string label) => label switch {
        "Group selected"=>DisplayGesture(GestureFor("edit.group")),"Ungroup"=>DisplayGesture(GestureFor("edit.ungroup")),
        "Duplicate"=>DisplayGesture(GestureFor("edit.duplicate")),"Delete"=>DisplayGesture(GestureFor("edit.delete")),
        "Rename…"=>DisplayGesture(GestureFor("edit.rename")),"Bring to front"=>DisplayGesture(GestureFor("edit.bringToFront")),"Send to back"=>DisplayGesture(GestureFor("edit.sendToBack")),"Bring forward"=>DisplayGesture(GestureFor("edit.bringForward")),"Send backward"=>DisplayGesture(GestureFor("edit.sendBackward")),
        "Lock" or "Unlock"=>DisplayGesture(GestureFor("edit.toggleLock")),_=>""};
}
