# Pixel art and sprite sheets

Wysicraft has two built-in editors for pictures:

- the **Pixel editor**, for drawing pixel art (one picture or an animation of several frames), saved into your project as a PNG with transparency, and
- the **Sprite sheet editor**, for turning a sheet of frames into named clips (idle, run, jump…) for a **Sprite** control.

Both work for Minecraft, web and desktop projects: the result is an ordinary PNG and the same clip text you could type by hand.

## Pixel editor

Open it from any of these:

- **Project → Pixel editor…** or **New pixel art** in the Assets panel: a new picture.
- **Edit pixels** in the Assets panel: the selected image.
- **Right-click** an image or sprite on the canvas or in Layers: **Edit in pixel editor…** (sprites open one frame at a time).
- **Draw…** (or **Edit pixels…**) under a control's skin image in Properties: draws or edits that control's picture and assigns it.
- **Edit pixels…** or **Draw new…** in the Sprite sheet editor.

### Drawing

| Tool | Key | What it does |
| --- | --- | --- |
| Pencil | B | Draws pixels. Brush size 1–8 (`[` and `]`). |
| Eraser | E | Makes pixels transparent. |
| Fill | G | Fills the touching area of the same color (with **Tolerance**, colors close to it too). |
| Line | L | Drag to draw a straight line. |
| Rectangle | R | Drag to draw a rectangle. Shift keeps it square. |
| Ellipse | O | Drag to draw an ellipse. Shift keeps it a circle. |
| Select | S | Drag a box to select part of the layer. Drawing then only changes the inside. Click once to deselect. |
| Magic wand | W | Click to select an area of one color. With **Wand: touching only** ticked it picks the area touching where you click; untick it to pick every pixel of that color. **Diagonals count as touching** also takes pixels that only meet at a corner, so one click takes a whole diagonal line. **Tolerance** also takes colors close to it. |
| Move | V | Drag to move the selection, or with nothing selected, the whole layer (or every layer in a selected group). |
| Pick color | I | Click to take a color from the picture (or Alt+click with any tool). |

- **Left click** paints the left color and **right click** the right one. The right color starts out transparent, so right click erases. Press **X** to swap them.
- Click a color square to choose any color. Colors can be see-through: type `#AARRGGBB` (for example `#80FF0000` is half-transparent red). Normally a color replaces what's underneath. Tick **Blend colors** to mix see-through colors with what's already there instead (going over a pixel twice in one stroke doesn't darken it further).
- The **palette** holds your favorite colors: left click a swatch for the left color, right click for the right one, **Add left color** to keep one, Ctrl+click to remove one. Your palette is remembered.
- **Mirror (M)** copies everything you draw onto the other half, mirrored left to right.
- **Filled shapes** makes rectangles and ellipses solid.
- **Tolerance** (0–100%) is for the magic wand and fill. At 0% they take exactly the color you click. Raise it to also take nearby shades, for example a gradient or soft edges.
- The mouse wheel zooms (Shift+wheel scrolls). The checkerboard shows transparent areas.
- **Undo / Redo:** Ctrl+Z and Ctrl+Y.

### Layers and groups

The **Layers** panel lists layers from the front of the picture (top) to the back. Each frame has its own picture on every layer.

- **+ Layer** adds a layer above the selected one (inside it, if a group is selected). **+ Group** adds a group, and groups can hold other groups.
- **▲ ▼** move a layer in front of or behind its neighbors. **Into group** moves it into the group next to it, and **Out of group** moves it back out.
- The **eye** hides a layer or group. Hidden layers aren't saved into the PNG. The **lock** stops a layer being drawn on or moved.
- **Opacity** fades a layer or a whole group.
- **Merge down** combines a layer with the layer under it. **Duplicate**, **Delete** and **Rename…** (or double-click) do what they say.

You draw on the selected layer. With a group selected, drawing tools ask you to pick a layer, and **Move** shifts everything in the group.

### Selecting and moving

- Drag with **Select** (S) to select a box, or click with the **Magic wand** (W) to select an area of one color. **Ctrl+A** selects everything, and **Esc** or **Ctrl+D** deselects.
- Hold **Shift** while selecting to add to the selection, or **Ctrl** to take away from it. Selections can be any shape. A dashed outline shows exactly what's selected, and only those pixels move, get copied or change when you draw.
- The wand looks at the selected layer's own pixels (for a group, at what you see).
- Drag with **Move** (V), or press the arrow keys (Shift for 10 pixels), to move the selected pixels. They float above the layer until you put them down with **Enter**, deselect, or pick another tool.
- **Ctrl+C**, **Ctrl+X** and **Ctrl+V** copy, cut and paste. Pasting goes onto the selected layer, ready to move. You can also paste pictures copied from other programs.
- **Delete** erases the selection. **Flip ↔ / ↕** and **Clear** work on the selection, or on the whole layer when nothing is selected.

### Frames and animation

The strip at the bottom holds the frames. Click a frame to show it (comma and period step through them). **Shift+click** or **Ctrl+click** picks several.

- **Add** puts an empty frame after the selected ones. **Duplicate** copies the selected frames right after them, handy for small changes between frames. **Delete** removes them.
- **Move left** and **Move right** move the selected frames one place.
- **Right-click** the strip for the same actions, plus **Duplicate → Mirrored (plays back)**. It adds the selected frames again in reverse order, so an animation plays forward and then back: frames 1 2 3 4 become 1 2 3 4 4 3 2 1.

- **Onion skin** (on by default) shows the previous frame faintly underneath, so you can line up the next pose.
- **Play** in the Preview box plays the frames at the speed you set.
- **Canvas size…** resizes every frame and layer (the top-left corner stays put).
- **Split into frames…** cuts a single picture into frames, for sprite sheets you drew or downloaded elsewhere.

### Saving

- **Save to project** (Ctrl+S) puts the picture in your project's images. Several frames are saved as one **sprite sheet**: frames side by side, wrapping onto a new row past 2048 pixels, numbered left to right then top to bottom, the way Sprite controls count them. Saving again updates the same image, and every control using it updates too. Your layers are kept with the project (never in exports), so **Edit pixels** opens them again as you left them. If the PNG has been changed another way since, for example with Replace, it opens as one layer.
- **Pictures with several frames remember them.** The frame size and count are saved with the picture, so **Edit pixels** reopens it already cut into frames. In **Assets** it shows its first frame and "sprite sheet, N frames of W×H". **Add image**, dragging it onto the screen or double-clicking it makes a **Sprite** that plays every frame, and assigning it to a Sprite sets the frame size.
- **Plays on its own** (under Preview) saves the frames as an **animated image** instead. It loops by itself at the Preview speed wherever it's used, including Image controls, skins, Preview, web and desktop apps, and Minecraft. It works like Minecraft's own animated textures, so there's no Sprite or clips to set up. Leave it off when you want clips you control from events and scripts.
- **Save as new…** keeps the old image and saves a copy under a new name.
- **Export PNG…** saves the PNG on your computer.
- **Add to screen** saves, then places it on the current screen: one frame (or an animated image) becomes an **Image** control, and several frames a **Sprite** that plays them all (a clip called `play`). Small pictures are enlarged in whole steps so the pixels stay square.

Saving counts as one step you can undo in the main editor. Pictures (and each frame) can be up to 512 × 512 pixels.

## With an AI assistant

An assistant connected through [[MCP|MCP-and-AI-Assistants]] can draw pixel art (`pixel_art`), look at images (`read_pixel_art`) and set up sprite clips (`sprite_sheet`). It saves the same PNG and layers as the pixel editor, so you can open its work in **Edit pixels** and carry on. Each change is one step you can undo. While the pixel editor or sprite sheet editor is open, the assistant's changes wait until you close it.

## Sprite sheet editor

Select a **Sprite** control and double-click it on the canvas, or click **Sprite sheet editor…** in its Properties (also **Project → Sprite sheet editor for the selection…**).

1. **Choose image…** picks the sheet from your project's images (or imports a PNG). **Draw new…** draws one in the pixel editor, and **Edit pixels…** opens this sheet there, one frame at a time.
2. Set the **frame width and height**, or type how many **columns and rows** the sheet has and the frame size is worked out for you. Each frame is numbered on the sheet.
3. **New clip** adds a clip. Then **click frames on the sheet** in the order they should play. Shift+click adds every frame from the last one up to the one you click, and right-click takes a frame back out. Each frame in the clip shows its step number (`#1`, `#2`…).
4. Under **Frames in …**, click a frame to select it, then move it with ◀ ▶ or **Remove** it (Delete).
5. Set the **speed** in frames per second, and tick **Play once** for clips that should stop on their last frame (a jump or an explosion) instead of looping.
6. The **Preview** plays the selected clip. **Plays first** chooses the clip showing when the screen opens.
7. **Apply** writes it all back to the Sprite control in one undo step.

The editor writes the same clip text you could type in Properties, for example `idle: 0 @8; run: 1-6 @12; jump: 9-7 @8 once`. Scripts switch clips with `ui.play('hero', 'run')`.
