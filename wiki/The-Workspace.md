# The workspace

![The Arcadia Studio editor](images/editor-overview.png)

## Layout

| Area | What it's for |
| --- | --- |
| **Menu bar** | Every command, with its keyboard shortcut shown on the right. |
| **Toolbar** | Preview, Minecraft test, Validate, Export…, Export for KubeJS, **Start MCP server** (highlighted in blue), and the current zoom. |
| **Toolbox / Assets / Components / Items** (top left, tabs) | Controls to drag onto the canvas, your project's images, reusable components, and Minecraft item icons. |
| **Layers** (bottom left) | Every control on the screen, front to back, with groups, visibility and lock. |
| **Designer** (center) | The canvas. The screen dropdown and **New screen**, **Delete screen** and **Screen settings** sit above it. |
| **Properties / Events** (right, tabs) | Settings for the selected control, or for the screen when nothing is selected, and what happens on clicks and other events. |
| **Output / Scripts** (bottom, tabs) | Messages, validation results and the script editor. |
| **Status bar** | Screen ID, size, selection count, grid, snap and zoom. |

## Moving panels

Every panel can be rearranged:

- **Drag a panel's title or tab** to move it. Drop it on one of the docking guides to dock it beside another panel or into its tab group.
- **Drag away from the guides** to float it as its own window (useful on a second monitor).
- **Pin button:** auto-hides the panel at the window edge until you hover over it.
- **Close button:** hides the panel. Bring it back from **View → Panels**.
- **View → Reset panel layout** restores the default arrangement. It never changes your project or script text.

Your arrangement is saved when you close Arcadia Studio and restored next time. It's a per-user preference, separate from projects.

## Toolbar

| Button | Shortcut | Does |
| --- | --- | --- |
| **Preview** | F5 | Opens an interactive desktop preview. See [[Preview|Preview]]. |
| **Minecraft test** | Ctrl+F5 | Launches real Minecraft with your project. See [[Testing in Minecraft|Testing-in-Minecraft]]. |
| **Validate** | F7 | Checks the project for errors; results appear in Output. |
| **Export…** | Ctrl+E | Creates a mod JAR or other format. See [[Exporting and installing|Exporting-and-Installing]]. |
| **Export for KubeJS** | – | Export for projects with KubeJS server scripts. |
| **Start MCP server** | – | Lets an AI assistant work on your project. See [[AI assistants (MCP)|MCP-and-AI-Assistants]]. |

The text on the right shows the zoom, for example `Zoom 100% • 1 GUI pixel = 2 screen pixels`. Minecraft measures screens in **GUI pixels**; the canvas shows each GUI pixel as 2 screen pixels at 100%.

## Menus

- **File:** New project, Open project or pack…, Recover unsaved project…, Save, Save as…, Export…, Export for KubeJS…, [[Publish to Arcadia…|Publishing-to-Arcadia]], [[Publish to itch.io…|Publishing-to-itch.io]], Exit.
- **Edit:** Undo, Redo, Cut, Copy, Paste, Duplicate, Delete, Select all, Group, Ungroup, Isolate group, Attach to panel, Detach from panel, Lock / unlock selection, Rename…, Bring to front, Bring forward, Send backward, Send to back, and the **Arrange** submenu.
- **View:** Panels, Reset panel layout, zoom commands, Show grid, Snap to grid, Keyboard shortcuts….
- **Project:** Preview, Test in Minecraft, Validate, Screen settings, Project settings…, Import texture…, MCP server (AI assistants)….
- **Advanced** (web and desktop projects): Inputs (keys and gamepads)…, Input creator…, Animations…, State graphs…, Shaders…, Particle maker…, Particles…, Collider editor and Tile painter for the selection, Collision layers…, **Create leaderboard**, **Open leaderboard…** and **Import leaderboard…** ([[Leaderboard pages|Leaderboard-Pages]]), **Create audio from text…**, **Create text from audio…** and **Speech settings…** ([[Speech and video|Speech-and-Video]]), and Show / hide the Advanced toolbox.
- **Help:** User manual (F1, opens the offline manual installed with Arcadia Studio), Script API and snippets, Keyboard shortcuts…, About Arcadia Studio.

All of these can be given your own shortcuts. See [[Keyboard shortcuts|Keyboard-Shortcuts]].

## Properties panel

With a control selected, Properties shows its sections: **Identity**, **Layout** (position, size, parent panel, anchors), component options if it's a linked component, **Appearance** and **Behavior**. With nothing selected, it shows the **Screen** settings. See [[Properties and appearance|Properties-and-Appearance]] and [[Projects and screens|Projects-and-Screens]].

Text fields update the canvas as you type valid values. An invalid value turns the field's border red and isn't applied.

## Output panel

Output lists what Arcadia Studio did: saves, exports, validation errors, attach and detach reports, and errors. Validation opens this panel automatically. The last message also appears in the status bar.
