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
| **Layout size (GUI pixels)** + **Apply size** | Lays the screen out at another size, to test [[anchors|Anchors-and-Responsive-Layouts]]. Only works when **Responsive layout** is on for the screen. Doesn't change your design. |
| **Console** | Clicks, actions, script output (`console.log`) and errors with file and line. |
| **JavaScript scratchpad** + **Run JavaScript** | Try script code instantly against the previewed screen. |

Try this in the scratchpad:

```javascript
console.log('Hello from the preview!');
ui.setText('status', 'It works!');
```

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
