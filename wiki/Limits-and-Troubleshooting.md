# Limits and troubleshooting

## Limits

These are Minecraft's limits. Projects made for web and desktop can go further; see [[Advanced tools|Advanced-Tools]].

| Thing | Limit |
| --- | --- |
| Screens per project | 128 (web & desktop: 1000) |
| Controls per screen | 512 (web & desktop: 16,000; Validate gives advice past 4,000) |
| Screen size sent to players | 900 kB compressed (screens compress 20–70×, so even 500 controls fit easily). **Validate** reports a screen that is too big. |
| Screen size | 16–4096 GUI pixels each way (web & desktop: 16384) |
| Sounds | `.ogg` in Minecraft; `.mp3`, `.wav`, `.m4a` also in web and desktop apps; 32 MiB each |
| Actions per event side | 64 |
| Particle effects | 64 per project, 2000 particles alive per screen (50,000 where the browser has WebGL2) (web & desktop) |
| Tilemaps | 512 columns, 512 rows and 65,536 cells each (web & desktop) |
| State graphs | 64 per screen, 64 states each, 16 ways out of a state (web & desktop) |
| Fonts | `.ttf`, `.otf`, `.woff2`, `.woff`, 8 MiB each (web & desktop) |
| Script size | 256 KiB each (web & desktop: 1 MiB; only attached scripts are exported) |
| Images | PNG, 8192 × 8192 px and 32 MiB each |
| Project / export size | 256 MiB total, 2048 files |
| Download size (web & desktop) | Advice, never a refusal: the Export dialog and **Validate** warn past 100 MB for a web game and 500 MB for a desktop app. See [[How big should a game be?|Web-and-Desktop-Apps#how-big-should-a-game-be]] |
| Item List data | 128 rows, 4096 characters of JSON |
| Row template | 64 controls |
| Text input sent to the server | 1024 characters |
| Panel / group nesting | 32 levels |
| Undo history | 200 steps |
| IDs | lowercase letters, numbers, `_`, starting with a letter, up to 64 characters |

## Troubleshooting

**My anchors don't seem to do anything.**
Anchors keep a control's *distance* from an edge; they don't move it there. They act when the parent or screen changes size: resize the panel on the canvas, change the screen's Width, or use **Apply size** in Preview. In Minecraft they only apply when **Responsive layout** is on in Screen settings. See [[Anchors and responsive layouts|Anchors-and-Responsive-Layouts]].

**A control is cut off or invisible.**
It's probably inside a panel and sticking out past the panel's edge (children are clipped), or its panel is hidden. Check **Parent panel** in Properties and the eye in Layers.

**I can't click or drag a control on the canvas.**
It may be **locked** (lock icon in Layers; press Ctrl+L to unlock), or you're isolating a different group (press Escape). Select it in Layers instead.

**Clicking selects a whole group when I want one control.**
Alt-click it, click its row in Layers, or double-click to isolate the group.

**Align commands are greyed out.**
Hover for the reason. You probably need more independent objects, or to turn off **Keep layer groups together**. See [[Align and distribute|Align-and-Distribute]].

**The Delete key did nothing.**
The canvas needs focus: click the canvas first. Delete is ignored while you're typing in a field or while the Assets or Items list has focus.

**The command button does nothing in Minecraft.**
Commands run with the player's own permissions. Test with cheats on or as an operator. Also check the handler's **Permission level** and cooldown in Events → Server.

**`/myproject.open` says unknown command.**
Check the project **Id** in Project settings, that the JAR is in `mods` on both server and client, and that you restarted after adding it. Remove any older copy of the same project.

**My screen changes don't show up in the game.**
JARs need a restart after being replaced. In the Minecraft test, use **Apply changes** and reopen the screen.

**Preview says my server action ran, but nothing happened.**
Preview only simulates server actions. Use the [[Minecraft test|Testing-in-Minecraft]].

**The Minecraft test won't start.**
It needs Java 21: choose the folder in the test window. The first run downloads several GB and can take several minutes. With the portable ZIP, keep `TestEnvironment` beside `Designer`.

**The export won't build.**
Click **Validate** and fix the errors listed in Output. Common causes: an action targeting a control that no longer exists, a missing destination screen, or an invalid ID.

**Server scripts stop running for a player.**
They've used up the per-player script time budget; it refills within seconds, and the server log mentions throttling. Make scripts lighter, or raise the event's cooldown.

**My AI assistant can't connect.**
The assistant must run on this computer and support MCP over HTTP. After restarting Wysicraft or the MCP server, copy the new configuration: the port and token change every time. See [[AI assistants (MCP)|MCP-and-AI-Assistants]].

**The editor crashed.**
Reopen it and use **File → Recover unsaved project…**. A crash log is saved in `%LOCALAPPDATA%\Wysicraft\Logs`.

**The panels are in a mess.**
**View → Reset panel layout**. It doesn't touch your project.

## Where Wysicraft keeps things

| What | Where |
| --- | --- |
| Recovery drafts | `%LOCALAPPDATA%\Wysicraft\Recovery` |
| Crash logs | `%LOCALAPPDATA%\Wysicraft\Logs` |
| Panel layout | `%LOCALAPPDATA%\Wysicraft\workspace-layout-2.xml` |
| Keyboard shortcuts | `%LOCALAPPDATA%\Wysicraft\keybindings.json` |
| Minecraft test instance | `%LOCALAPPDATA%\Wysicraft\MinecraftTest\1.21.1` |
| MCP exports | `%LOCALAPPDATA%\Wysicraft\McpExports` |
