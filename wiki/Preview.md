# Preview

**Preview** (toolbar or F5) opens your current screen in a separate window where everything is clickable. It's the fastest way to test.

Preview runs your project on the same engine as [[web page, Windows and Electron exports|Web-and-Desktop-Apps]], so what works here works there, and it follows the Minecraft runtime closely. Minecraft textures and item icons come from your own installed game, just for Preview (they are never exported). Colliders are outlined so you can see your physics walls.

## What works

- Buttons, text boxes, checkboxes, sliders, dropdowns and item lists respond, with hover and pressed feedback.
- **Client actions and Client scripts run for real.**
- **Server actions and scripts are simulated:** they appear in the console as `SIMULATED SERVER…`. No commands run and no Minecraft world is touched.
- Every click is logged in the console, even when nothing is assigned, so you can tell a click reached the control.

## Window parts

| Part | Use |
| --- | --- |
| **Reset preview** | Restores the screen's starting values. |
| **Clear console** | Empties the console. |
| **Mute** | Turns the preview's sound off (the game itself is unchanged). Remembered for the next preview. |
| **● Record** | Records the game to a video, only the game's own area as players see it, with its sound. Press **■ Stop** to finish; videos go to Videos\Arcadia Studio. See [[Recording your game|Speech-and-Video#recording-your-game]]. |
| **Layout size (GUI pixels)** + **Apply size** | Lays the screen out at another size, to test [[anchors|Anchors-and-Responsive-Layouts]]. Only works when **Responsive layout** is on for the screen. Doesn't change your design. **Reset size** lays it out for the window again. |
| **Fit game to window** | On (the default): the game is drawn scaled to fill the Preview window, squeezed down when the window is smaller than the game and enlarged when it's bigger, and it follows as you resize the window or fold the console away. Only Preview's zoom changes: the window keeps its size and the game keeps its own size and layout. A responsive screen already lays itself out for the window, so it's only zoomed after **Apply size**. Preview also opens no bigger than your screen. |
| **Console** | Clicks, actions, script output (`console.log`) and errors with file and line. **Minimize ▾** on its bar folds the console and JavaScript panels away to give the game the room; while they're folded the bar counts new lines (and says when there are errors). **Restore ▴** brings them back. Remembered for the next preview. |
| **JavaScript scratchpad** + **Run JavaScript** | Try script code instantly against the previewed screen. |
| **Profiler** | Shows where each frame's time goes, over the game. See [Profiler](#profiler). |

Try this in the scratchpad:

```javascript
console.log('Hello from the preview!');
ui.setText('status', 'It works!');
```

## Profiler

Tick **Profiler** to see where the time goes, over the game. The figures are averages over the last quarter second:

| Line | What it is |
| --- | --- |
| **FRAME**, **fps**, **worst** | Time from one frame to the next, and the longest in that quarter second. 16.7 ms is 60 frames a second. |
| **engine** | The engine's own work each frame, split into **logic** (inputs, state graphs, animations), **objects** (spawned copies moving and keeping apart), **physics** and **particles**. |
| **draw** | Drawing the screen, and how many batches went to the graphics card (`canvas` when there's no WebGL). |
| **script** | One script run from start to finish (sending the screen, running, applying its changes), how many run a second, the slowest, and how many changes each makes. Scripts run apart from drawing, so a slow one delays its own changes, not the frame. |
| **controls**, **spawned**, **bodies**, **pairs**, **particles** | What's on screen: all controls, spawned copies, shown controls with a body, the pairs of bodies physics tested in its last step, and live particles. |

Under the figures there's a bar for each recent frame: green within a 60 fps frame, yellow over it, red over 33 ms.

The profiler is never shown in exported apps. It stays on through **Reset preview**. An AI assistant can tick it too (MCP `preview_control`, action `profile`), and while it's on, Preview results include its figures as `profile`. In a web export you can still turn it on from the browser's developer console with `Wysicraft.app.setProfiler(true)`. `Wysicraft.app.profile()` gives the same figures as data.

## How close is it to Minecraft?

Preview is a close approximation, not Minecraft itself:

- Layout, positions, anchors, colors, images and behavior match.
- Fonts are approximated, so exact glyph shapes differ slightly.
- Item icons are texture previews or placeholders; Minecraft draws the real models.
- Soft text shadows differ slightly.
- KubeJS scripts don't run; test them in Minecraft.

For the real thing, use the [[Minecraft test|Testing-in-Minecraft]].

## Safety

Preview scripts run in a separate, restricted process with tight time and memory limits. They can't reach your files, the network or Minecraft.
