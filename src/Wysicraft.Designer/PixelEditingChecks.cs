using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Wysicraft.Core;
using Wysicraft.Models;
using Wysicraft.Packaging;
namespace Wysicraft.Designer;

public partial class MainWindow
{
    static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) { yield return child; foreach (var d in LogicalDescendants(child)) yield return d; }
    }
    static void CaptureWindow(Window window, string file)
    {
        window.UpdateLayout(); var content = (FrameworkElement)window.Content;
        // The window's own background sits behind its content, so paint it first to see the real colors.
        var visual = new DrawingVisual(); var area = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
        using (var dc = visual.RenderOpen()) { dc.DrawRectangle(window.Background ?? SystemColors.WindowBrush, null, area); dc.DrawRectangle(new VisualBrush(content), null, area); }
        var bitmap = new RenderTargetBitmap((int)area.Width, (int)area.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(file); encoder.Save(stream);
    }
    static bool SamePixels(PixelImage a, PixelImage b) => a.Width == b.Width && a.Height == b.Height && a.Pixels.Zip(b.Pixels).All(p => p.First == p.Second || (p.First >> 24 == 0 && p.Second >> 24 == 0));

    // Draws, layers, selects, moves, saves and places pixel art through the pixel editor, then makes a clip in the
    // sprite sheet editor.
    internal void VerifyPixelEditing(string output)
    {
        project = new Project(); project.Manifest.Id = "pix"; ui = project.Screens[0]; history.Clear(); selected.Clear(); RefreshAll();
        static void Ok(bool pass, string what) { if (!pass) throw new Exception(what); }
        const uint Red = 0xFFFF0000, Blue = 0xFF0000FF, Green = 0xFF00FF00, Glass = 0x802040FF, None = 0;
        IDataObject? userClipboard = null; try { userClipboard = Clipboard.GetDataObject(); } catch { }

        var blank = new PixelDocument(8, 8); blank.Layers.Add(blank.NewLayer("Layer 1"));
        var editor = new PixelEditor(this, blank, null, "hero", r => SavePixelArt(r, null), AddPixelArtToScreen);
        editor.Show();
        Ok(editor.TestCanvasTakesMouse(), "The canvas doesn't receive the mouse, so clicking would draw nothing");
        PixelImage F() => editor.TestShown(editor.TestCurrent);
        PixelImage C(string layer, int frame = 0) => editor.TestCell(layer, frame);
        PixelLayer L(string layer) => editor.TestDoc.All().First(l => l.Name == layer);

        // Drawing on one layer.
        editor.TestTool("Pencil"); editor.TestColors(Red, None); editor.TestStroke(false, (0, 0), (7, 0));
        Ok(Enumerable.Range(0, 8).All(x => F().Get(x, 0) == Red), "Pencil stroke didn't draw a full line");
        editor.TestTool("Fill"); editor.TestColors(Glass, None); editor.TestStroke(false, (3, 3));
        Ok(F().Get(3, 3) == Glass && F().Get(7, 7) == Glass && F().Get(0, 0) == Red, "Fill with a see-through color");
        editor.TestTool("Eraser"); editor.TestStroke(false, (0, 0));
        Ok(F().Get(0, 0) == None && F().Get(1, 0) == Red, "Eraser");
        editor.TestUndo(); Ok(F().Get(0, 0) == Red, "Undo"); editor.TestRedo(); Ok(F().Get(0, 0) == None, "Redo");
        editor.TestTool("Pencil"); editor.TestMirror(true); editor.TestColors(Green, None); editor.TestStroke(false, (1, 5));
        Ok(F().Get(1, 5) == Green && F().Get(6, 5) == Green, "Mirror drawing");
        editor.TestMirror(false); editor.TestStroke(true, (1, 5));
        Ok(F().Get(1, 5) == None && F().Get(6, 5) == Green, "Right click paints the right-hand (transparent) color");
        editor.TestTool("Rectangle"); editor.TestColors(Blue, None); editor.TestStroke(false, (1, 2), (4, 4), (5, 6));
        Ok(F().Get(1, 2) == Blue && F().Get(5, 6) == Blue && F().Get(5, 2) == Blue && F().Get(3, 4) == Glass && F().Get(4, 4) == Glass, "Rectangle outline, or its drag preview left marks");

        // Layers, opacity, hiding, nested groups, group opacity, undo, merge.
        editor.TestNewLayer(); Ok(editor.TestLayer == "Layer 2", "New layer should be selected");
        editor.TestTool("Pencil"); editor.TestColors(Green, None); editor.TestStroke(false, (3, 3));
        Ok(C("Layer 2").Get(3, 3) == Green && C("Layer 1").Get(3, 3) == Glass && F().Get(3, 3) == Green, "Drawing on a new layer");
        editor.TestOpacity(50); uint half = PixelArt.Over(Green, Glass, 0.5);
        Ok(F().Get(3, 3) == half && half != Green && half != Glass, "Layer opacity");
        editor.TestToggleVisible("Layer 2"); Ok(F().Get(3, 3) == Glass, "Hidden layers shouldn't show");
        editor.TestStroke(false, (4, 3)); Ok(editor.TestNotice.Contains("hidden") && C("Layer 2").Get(4, 3) == None, "Drawing on a hidden layer should be refused");
        editor.TestToggleVisible("Layer 2");
        editor.TestNewGroup(); Ok(editor.TestLayer == "Group 1", "New group");
        editor.TestNewGroup(); Ok(editor.TestDoc.ParentOf(L("Group 2"))?.Name == "Group 1", "A group made with a group selected goes inside it");
        editor.TestPickLayer("Layer 2"); editor.TestIntoGroup(); Ok(editor.TestDoc.ParentOf(L("Layer 2"))?.Name == "Group 1", "Into group");
        editor.TestIntoGroup(); Ok(editor.TestDoc.ParentOf(L("Layer 2"))?.Name == "Group 2" && editor.TestDoc.Depth(L("Layer 2")) == 2, "Into a nested group");
        Ok(F().Get(3, 3) == half, "Grouping changed how the picture looks");
        editor.TestPickLayer("Group 1"); editor.TestOpacity(50);
        Ok(F().Get(3, 3) == PixelArt.Over(PixelArt.Over(Green, None, 0.5), Glass, 0.5), "Group opacity");
        editor.TestOpacity(100); Ok(editor.TestLayerRows == 4, "Layers panel rows: " + editor.TestLayerRows);
        editor.TestUndo(); Ok(L("Group 1").Opacity == 0.5, "Undo group opacity"); editor.TestRedo(); Ok(L("Group 1").Opacity == 1, "Redo group opacity");
        editor.TestPickLayer("Layer 2"); editor.TestOutOfGroup(); Ok(editor.TestDoc.ParentOf(L("Layer 2"))?.Name == "Group 1", "Out of group");
        int steps = editor.TestUndoCount; bool refused = false; try { editor.TestMergeDown(); } catch (InvalidOperationException) { refused = true; }
        Ok(refused && editor.TestUndoCount == steps, "Merging onto a group should be refused without an undo step");
        editor.TestOutOfGroup(); editor.TestPickLayer("Group 1"); editor.TestDeleteLayer();
        Ok(editor.TestDoc.All().Select(l => l.Name).SequenceEqual(["Layer 1", "Layer 2"]), "Deleting a group");
        editor.TestPickLayer("Layer 2"); editor.TestMergeDown();
        Ok(editor.TestDoc.All().Count() == 1 && C("Layer 1").Get(3, 3) == half && editor.TestLayer == "Layer 1", "Merge down");

        // Select, move, nudge, undo, copy and paste onto another layer, moving whole layers and groups.
        editor.TestDeselect(); editor.TestClear(); Ok(C("Layer 1").IsEmpty, "Clear the layer");
        editor.TestTool("Rectangle"); editor.TestFilled(true); editor.TestColors(Red, None); editor.TestStroke(false, (1, 1), (2, 2));
        editor.TestTool("Select"); editor.TestStroke(false, (1, 1), (2, 2));
        editor.TestTool("Move"); editor.TestStroke(false, (1, 1), (4, 5));
        var shownNow = editor.TestDisplayed;
        Ok(editor.TestFloating && shownNow.Get(4, 5) == Red && shownNow.Get(5, 6) == Red && shownNow.Get(1, 1) == None && C("Layer 1").Get(4, 5) == None, "Moving a selection (shown on the canvas, not put down yet)");
        editor.TestCommit(); Ok(!editor.TestFloating && C("Layer 1").Get(4, 5) == Red && C("Layer 1").Get(1, 1) == None, "Putting a moved selection down");
        editor.TestNudge(1, 0); editor.TestDeselect();
        Ok(C("Layer 1").Get(5, 5) == Red && C("Layer 1").Get(6, 6) == Red && C("Layer 1").Get(4, 5) == None, "Nudging a selection with the arrow keys");
        editor.TestUndo(); Ok(C("Layer 1").Get(4, 5) == Red && C("Layer 1").Get(6, 6) == None, "Undo a nudge");
        editor.TestUndo(); Ok(C("Layer 1").Get(1, 1) == Red && C("Layer 1").Get(4, 5) == None, "Undo a move");
        editor.TestTool("Select"); editor.TestStroke(false, (1, 1), (2, 2)); editor.TestCopy(false);
        editor.TestNewLayer(); editor.TestPaste(); Ok(editor.TestFloating && editor.TestLayer == "Layer 2", "Paste makes a movable selection on the current layer");
        editor.TestNudge(4, 0); editor.TestCommit();
        Ok(C("Layer 2").Get(5, 1) == Red && C("Layer 2").Get(6, 2) == Red && C("Layer 2").Get(1, 1) == None && C("Layer 1").Get(1, 1) == Red, "Copy and paste onto another layer");
        editor.TestDeselect(); editor.TestTool("Move"); editor.TestStroke(false, (0, 0), (0, 3));
        Ok(C("Layer 2").Get(5, 4) == Red && C("Layer 2").Get(5, 1) == None && C("Layer 1").Get(1, 1) == Red, "Moving a whole layer");
        editor.TestNewGroup(); editor.TestPickLayer("Layer 2"); editor.TestIntoGroup(); editor.TestPickLayer("Group 1");
        editor.TestTool("Move"); editor.TestStroke(false, (0, 0), (1, 0));
        Ok(C("Layer 2").Get(6, 4) == Red && C("Layer 2").Get(5, 4) == None, "Moving a group moves the layers in it");

        // Blending: see-through colors mix instead of replacing, and going over a pixel twice in one stroke doesn't stack.
        editor.TestPickLayer("Layer 1"); editor.TestBlend(true); editor.TestTool("Pencil"); editor.TestColors(Glass, None);
        editor.TestStroke(false, (1, 1), (2, 1), (1, 1));
        uint mixed = PixelArt.Over(Glass, Red);
        Ok(C("Layer 1").Get(1, 1) == mixed && C("Layer 1").Get(2, 1) == mixed && mixed != Glass, "Blended pencil");
        editor.TestBlend(false); editor.TestStroke(false, (2, 2)); Ok(C("Layer 1").Get(2, 2) == Glass, "Without blending, colors replace");

        // Magic wand: odd shapes, add and take away, moving only the shape (not the rest of its box), drawing inside it.
        editor.TestNewLayer(); Ok(editor.TestLayer == "Layer 3", "New layer for the wand");
        editor.TestTool("Pencil"); editor.TestColors(Red, None);
        editor.TestStroke(false, (1, 1), (2, 1)); editor.TestStroke(false, (1, 2), (2, 2)); editor.TestStroke(false, (3, 1)); editor.TestStroke(false, (6, 6));
        editor.TestColors(Blue, None); editor.TestStroke(false, (3, 2));
        editor.TestColors(0xFFF00000, None); editor.TestStroke(false, (4, 1)); // nearly red, touching the red shape
        editor.TestTool("Wand"); editor.TestTouching(true); editor.TestSelectMode(null); editor.TestStroke(false, (1, 1));
        var picked = editor.TestSelection!; Ok(picked.Count == 5 && !picked[6, 6] && !picked[3, 2], "Wand should pick the touching red shape only: " + picked.Count);
        editor.TestTolerance(10); editor.TestStroke(false, (1, 1)); Ok(editor.TestSelection!.Count == 6 && editor.TestSelection[4, 1], "Wand tolerance should take the nearly-red pixel too");
        editor.TestTolerance(0); editor.TestStroke(false, (1, 1)); Ok(editor.TestSelection!.Count == 5, "Wand with no tolerance");
        editor.TestDeselect();
        editor.TestTool("Pencil"); editor.TestColors(Red, None); editor.TestStroke(false, (4, 4)); editor.TestStroke(false, (5, 5)); editor.TestTool("Wand");
        editor.TestStroke(false, (4, 4)); Ok(editor.TestSelection!.Count == 1, "Without diagonals, corner-touching pixels are separate");
        editor.TestDiagonals(true); editor.TestStroke(false, (4, 4)); Ok(editor.TestSelection!.Count == 3 && editor.TestSelection[6, 6], "Diagonals: one click takes the whole diagonal run"); editor.TestDiagonals(false);
        editor.TestTool("Pencil"); editor.TestColors(None, None); editor.TestStroke(false, (4, 4)); editor.TestStroke(false, (5, 5)); editor.TestTool("Wand");
        editor.TestTouching(false); editor.TestStroke(false, (1, 1)); Ok(editor.TestSelection!.Count == 6, "Wand, every red pixel"); editor.TestTouching(true);
        editor.TestSelectMode("Subtract"); editor.TestStroke(false, (6, 6)); Ok(editor.TestSelection!.Count == 5 && !editor.TestSelection[6, 6], "Ctrl+click takes away");
        editor.TestSelectMode("Add"); editor.TestStroke(false, (6, 6)); Ok(editor.TestSelection!.Count == 6, "Shift+click adds");
        editor.TestTool("Select"); editor.TestStroke(false, (5, 5), (7, 7)); Ok(editor.TestSelection!.Count == 14, "Shift+drag adds a box: " + editor.TestSelection!.Count);
        editor.TestSelectMode(null); editor.TestTool("Wand"); editor.TestStroke(false, (1, 1)); Ok(editor.TestSelection!.Count == 5, "A plain click starts a new selection");
        editor.TestTool("Move"); editor.TestStroke(false, (1, 1), (1, 4)); editor.TestCommit();
        Ok(C("Layer 3").Get(1, 4) == Red && C("Layer 3").Get(3, 4) == Red && C("Layer 3").Get(2, 5) == Red && C("Layer 3").Get(1, 1) == None, "Moving a wand selection");
        Ok(C("Layer 3").Get(3, 2) == Blue && C("Layer 3").Get(3, 5) == None, "Only the selected shape moves, not the rest of its box");
        editor.TestTool("Pencil"); editor.TestColors(Green, None); editor.TestStroke(false, (0, 4), (4, 4));
        Ok(C("Layer 3").Get(1, 4) == Green && C("Layer 3").Get(3, 4) == Green && C("Layer 3").Get(0, 4) == None && C("Layer 3").Get(4, 4) == None, "Drawing stays inside a wand selection");
        editor.TestClear(); Ok(C("Layer 3").Get(1, 4) == None && C("Layer 3").Get(2, 5) == None && C("Layer 3").Get(3, 2) == Blue && C("Layer 3").Get(6, 6) == Red, "Clearing a wand selection");
        CaptureWindow(editor, output + ".wand.png");
        editor.TestDeselect(); editor.TestDeleteLayer(); editor.TestPickLayer("Layer 1");

        // Frames apply to every layer.
        editor.TestDuplicateFrame(); editor.TestFlip();
        Ok(SamePixels(C("Layer 1", 1), PixelArt.FlipHorizontal(C("Layer 1", 0))) && SamePixels(C("Layer 2", 1), C("Layer 2", 0)), "Duplicating a frame, then flipping one layer of it");
        editor.TestAddFrame(); Ok(editor.TestDoc.FrameCount == 3 && editor.TestCurrent == 2 && editor.TestDoc.DrawingLayers().All(l => l.Cells[2].IsEmpty), "Adding a frame");
        editor.TestTool("Ellipse"); editor.TestFilled(true); editor.TestColors(Red, None); editor.TestStroke(false, (0, 0), (7, 7));
        Ok(F().Get(4, 4) == Red && F().Get(0, 0) == None && F().Get(0, 4) == Red, "Filled ellipse");
        CaptureWindow(editor, output + ".pixel.png");

        // Saving: a transparent sprite sheet plus the layers beside it (never exported).
        editor.TestSave("hero");
        string path = TextureAssets.Path("pix", "hero.png"), layersPath = path + TextureAssets.LayersSuffix;
        Ok(editor.TestAssetPath == path && project.Assets.ContainsKey(path) && project.Assets.ContainsKey(layersPath) && !editor.TestDirty, "Saving to Assets (with layers)");
        var sheet = DecodePixels(project.Assets[path]);
        Ok(sheet.Width == 24 && sheet.Height == 8, $"Three 8×8 frames should save as a 24×8 sheet, got {sheet.Width}×{sheet.Height}");
        for (int i = 0; i < 3; i++) Ok(SamePixels(PixelArt.Crop(sheet, i * 8, 0, 8, 8), editor.TestShown(i)), $"Frame {i} in the PNG doesn't match the picture");
        history.Undo(); Ok(!project.Assets.ContainsKey(path) && !project.Assets.ContainsKey(layersPath), "Saving pixel art isn't undoable"); history.Redo(); Ok(project.Assets.ContainsKey(layersPath), "Redo of the save");
        editor.TestTool("Pencil"); editor.TestColors(Green, None); editor.TestStroke(false, (7, 7)); editor.TestSave("ignored");
        Ok(project.Assets.Keys.Count(k => k.Contains("/hero") && k.EndsWith(".png")) == 1 && DecodePixels(project.Assets[path]).Get(16 + 7, 7) == Green && !project.Assets.Keys.Any(k => k.Contains("ignored")), "Saving again should update the same image");
        var reopened = PixelDocument.Load(project.Assets[layersPath]);
        Ok(reopened.FrameCount == 3 && reopened.All().Select(l => l.Name).SequenceEqual(["Layer 1", "Group 1", "Layer 2"]) && reopened.Produces(DecodePixels(project.Assets[path])), "Saved layers don't reopen as they were");
        var altered = DecodePixels(project.Assets[path]); altered.Pixels[0] = 0xFF123456;
        Ok(!reopened.Produces(altered), "Layers should count as out of date once the PNG changes");
        Ok(!ProjectStore.Files(project, true).Keys.Any(TextureAssets.IsEditorOnly) && ProjectStore.Files(project, false).ContainsKey(layersPath), "Layers must be saved with the project but never exported");

        // The saved sheet remembers its frames: Assets knows it's a sprite sheet, shows its first frame and adds it as a playing Sprite.
        Ok(SheetFrames(path) == (8, 8, 3) && PixelDocument.ReadFrames(project.Assets[layersPath]) == (8, 8, 3), "The sheet should remember its 8×8 frames");
        Ok(AssetThumbnail(path, project.Assets[path]) is BitmapSource thumb && thumb.PixelWidth == 8 && thumb.PixelHeight == 8, "Assets should show the first frame");
        CreateBrowserElement(false, 0, 0, path);
        var fromAssets = ui.Elements.Single(e => selected.Contains(e.Id));
        Ok(fromAssets.Type == "sprite" && fromAssets.FrameWidth == 8 && fromAssets.Clips == "play: 0-2 @8" && fromAssets.Value == "play", "Adding a sprite sheet from Assets should make a playing Sprite, got " + fromAssets.Type);
        history.Undo();
        editor.TestAddToScreen();
        var sprite = ui.Elements.Single(e => e.Type == "sprite");
        Ok(sprite.Texture == "pix:textures/gui/image/hero.png" && sprite.FrameWidth == 8 && sprite.FrameHeight == 8 && sprite.Clips == "play: 0-2 @8" && sprite.Value == "play" && sprite.Bounds.Width == 32, $"Add to screen made {sprite.Texture} {sprite.FrameWidth}×{sprite.FrameHeight} '{sprite.Clips}' {sprite.Bounds.Width}");
        Ok(Wysicraft.Core.Validation.Errors(project).Count == 0, "The placed sprite doesn't validate: " + string.Join("; ", Wysicraft.Core.Validation.Errors(project)));
        editor.Close();
        try { if (userClipboard != null) Clipboard.SetDataObject(userClipboard, true); } catch { }

        // Sprite sheet editor: add frames 2 and 0 to "play", right-click frame 2 to take its last use back out, set 12/s and once.
        ShowSpriteSheetEditor(sprite, window =>
        {
            Rectangle Cell(int frame) => LogicalDescendants(window).OfType<Rectangle>().First(r => r.Tag is int i && i == frame);
            void Click(int frame, bool right = false) => Cell(frame).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, right ? MouseButton.Right : MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
            Ok(LogicalDescendants(window).OfType<Rectangle>().Count(r => r.Tag is int) == 3, "The sheet editor should show 3 frames");
            window.UpdateLayout(); var probe = Cell(1); var middle = probe.TranslatePoint(new Point(probe.ActualWidth / 2, probe.ActualHeight / 2), window);
            Ok(window.InputHitTest(middle) == probe, "Sheet frames don't receive clicks");
            Click(2); Click(0); Click(2, right: true);
            var fps = LogicalDescendants(window).OfType<TextBlock>().First(t => t.Text == "Speed ").Parent is Panel row ? row.Children.OfType<TextBox>().First() : throw new Exception("Speed box missing");
            fps.Text = "12"; fps.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, fps, null) { RoutedEvent = UIElement.LostKeyboardFocusEvent });
            var once = LogicalDescendants(window).OfType<CheckBox>().First(c => (c.Content as string)?.StartsWith("Play once") == true);
            once.IsChecked = true; once.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            CaptureWindow(window, output + ".sheet.png");
            LogicalDescendants(window).OfType<Button>().First(b => Equals(b.Content, "Apply")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        });
        Ok(sprite.Clips == "play: 0-2,0 @12 once", "The sheet editor wrote '" + sprite.Clips + "'");
        history.Undo(); Ok(ui.Elements.Single(e => e.Type == "sprite").Clips == "play: 0-2 @8", "Applying the sheet editor isn't one undo step");

        // Right-click menu: drawing order and alignment in their own submenus; sprites open in the pixel and sprite sheet editors.
        var spriteMenu = ElementMenu(ui.Elements.Single(e => e.Type == "sprite"));
        MenuItem? Sub(string header) => spriteMenu.Items.OfType<MenuItem>().FirstOrDefault(m => Equals(m.Header, header));
        Ok(Sub("Arrange")?.Items.OfType<MenuItem>().Select(m => (string)m.Header).SequenceEqual(["Bring to front", "Bring forward", "Send backward", "Send to back"]) == true, "Arrange submenu should hold the front/back order");
        Ok(Sub("Align")?.Items.OfType<MenuItem>().Any(m => Equals(m.Header, "Align left edges")) == true && Sub("Bring to front") == null, "Align submenu, and no order items left at the top");
        Ok(Sub("Edit in pixel editor…") != null && Sub("Sprite sheet editor…") != null, "Sprites should open in the pixel editor and sprite sheet editor from the menu");

        // Frames: several picked at once, standard and mirrored copies, moving, deleting, and the onion skin on by default.
        var strip = new PixelDocument(2, 1, 4); strip.Layers.Add(strip.NewLayer("Layer 1"));
        uint[] ids = [0xFF000001, 0xFF000002, 0xFF000003, 0xFF000004]; // A B C D
        for (int i = 0; i < 4; i++) strip.Layers[0].Cells[i].Set(0, 0, ids[i]);
        var frames = new PixelEditor(this, strip, null, "strip", r => SavePixelArt(r, null), null); frames.Show();
        string Order() => string.Concat(Enumerable.Range(0, frames.TestDoc.FrameCount).Select(i => (char)('A' + (int)(frames.TestShown(i).Get(0, 0) & 0xFF) - 1)));
        frames.TestFrame(1); Ok(frames.TestOnion, "Onion skin should show the previous frame by default"); frames.TestFrame(0); Ok(!frames.TestOnion, "No onion skin on the first frame");
        frames.TestPickFrames(1, 2); Ok(frames.TestPicked.SequenceEqual([1, 2]) && frames.TestCurrent == 2, "Picking several frames");
        frames.TestDuplicateFrames(true); Ok(Order() == "ABCCBD", "Mirrored duplicate: " + Order());
        frames.TestUndo(); Ok(Order() == "ABCD", "Undo a mirrored duplicate: " + Order());
        frames.TestPickFrames(0, 1); frames.TestDuplicateFrames(false); Ok(Order() == "ABABCD", "Standard duplicate: " + Order()); frames.TestUndo();
        frames.TestPickFrames(1, 2); frames.TestMoveFrames(1); Ok(Order() == "ADBC" && frames.TestPicked.SequenceEqual([2, 3]) && frames.TestCurrent == 3, "Moving frames right: " + Order());
        frames.TestMoveFrames(-1); Ok(Order() == "ABCD" && frames.TestPicked.SequenceEqual([1, 2]), "Moving frames left: " + Order());
        frames.TestMoveFrames(-1); frames.TestMoveFrames(-1); Ok(Order() == "BCAD", "Moving stops at the start: " + Order());
        frames.TestPickFrames(0, 1, 2, 3); refused = false; try { frames.TestDeleteFrames(); } catch (InvalidOperationException) { refused = true; } Ok(refused && Order() == "BCAD", "Deleting every frame must be refused");
        frames.TestPickFrames(1, 2); frames.TestDeleteFrames(); Ok(Order() == "BD" && frames.TestCurrent == 1, "Deleting picked frames: " + Order());
        CaptureWindow(frames, output + ".frames.png");
        // Plays on its own: an animation file beside the PNG with the frame size and speed; not treated as a sprite sheet.
        frames.TestAnimate(true, 10); frames.TestSave("spinner");
        string spinner = TextureAssets.Path("pix", "spinner.png"), spinnerSize = "";
        Ok(project.Assets.TryGetValue(spinner + ".mcmeta", out var spinnerMeta), "Plays on its own should save an animation file");
        var sheetSize = ProjectStore.TextureSize(project.Assets[spinner]); var played = TextureAnimation.Parse(System.Text.Encoding.UTF8.GetString(spinnerMeta!), sheetSize.Width, sheetSize.Height);
        Ok(played != null && played.FrameWidth == 2 && played.FrameHeight == 1 && played.Frames.Count == 2 && played.Frames[0].Ticks == 2 && SheetFrames(spinner) == null, "Animation file: " + System.Text.Encoding.UTF8.GetString(spinnerMeta!) + spinnerSize);
        frames.TestAnimate(false, 10); frames.TestSave("spinner"); Ok(!project.Assets.ContainsKey(spinner + ".mcmeta") && SheetFrames(spinner) == (2, 1, 2), "Turning it off makes it a sprite sheet again");
        frames.TestDiscard();

        dirty = false;
        File.WriteAllText(output, "PASS: menus (Arrange and Align submenus, open sprites in the editors), frames (multi-pick, standard and mirrored duplicate, move left/right, delete, onion by default), pencil, fill with see-through colors, eraser, undo/redo, mirror, right-click color, rectangle preview; layers (new, opacity, hide, refuse hidden, nested groups, group opacity, undo, out of group, refused merge onto a group, delete group, merge down); select, move, put down, nudge, undo, copy/paste to another layer, move a layer and a group; blended and replacing colors; frames across layers, flip, filled ellipse; save as a transparent 24×8 sheet with layers kept (undoable, re-save updates, reopens, out-of-date detection, never exported); add to screen as a sprite; sprite sheet editor clicks/right-click/speed/once and single-step apply");
    }
}
