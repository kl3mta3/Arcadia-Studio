# Arcadia Studio

*Formerly Wysicraft. Projects saved before the rename (`.wysicraftproj`) still open; new projects are `.arcadia` files.*

**Latest:** Properties sections fold and remember; controls take **components** Unity-style (a searchable **+ Add component**, cards with a ✕ for Collider, Rigidbody, Character controller, Top-down mover, Follower and Pickup, with the script's tunable numbers editable on the card); and the MCP panel gains **Ask Assistant (Requires CLI)** — the editor can ask your own command-line AI (Claude Code, Codex, Gemini CLI, Qwen Code, Kimi Code, opencode, Copilot CLI or a custom command, on your subscription, no API key) to do things from Ask Agent, Draw it… and the Input creator. The MCP port and token now stay the same across restarts. See [Advanced tools](wiki/Advanced-Tools.md) and [MCP and AI assistants](wiki/MCP-and-AI-Assistants.md).

**1.2:** Project JARs now bundle the runtime and assigned Standard/KubeJS scripts. No separate Arcadia Studio installation is needed in-game; KubeJS projects still require KubeJS/Rhino. Script dropdowns include editable `[Template]` entries and global command examples. See [1.2 release notes](docs/RELEASE-1.2.md).

**Version 1.1:** single-file .wysicraftproj saving, project-scoped screens, Standard Server scripts, Item Lists, installation ZIP/JAR exports and expanded MCP tools. See [1.1 workflow notes](docs/RELEASE-1.1.md).

A Windows visual GUI designer and portable NeoForge runtime for Minecraft Java Edition **1.21.1**. Create screens, connect client and server actions, export a `.wysicraft` ZIP, and load it without generating Java screen classes or restarting Minecraft.

Arcadia Studio is intended for modpack authors and addon developers building menus, dashboards and control panels. Create and Create Aeronautics can be integrated through their documented commands or addon actions; neither mod is required.

![Actual WPF designer displaying the Airship sample](docs/designer.png)

## Build and launch

The designer includes a local MCP server. Click **Start MCP server** in the toolbar and copy its connection configuration into a local MCP client to inspect and edit the open project. The same panel's **Ask Assistant** row lets the editor send work to your own AI CLI. See [MCP setup and tools](docs/MCP.md).

Requirements: Windows, .NET SDK 8 or newer, Java **21**, and internet access for the first Gradle dependency download.

From the repository root:

```powershell
dotnet build
dotnet run --project src/Wysicraft.Designer
dotnet run --project tests/Wysicraft.Tests
```


Build the runtime with `JAVA_HOME` pointing to your Java 21 installation:

```powershell
cd wysicraft-runtime
.\gradlew.bat build
```

On Unix, use `./gradlew build`. The runtime JAR is `wysicraft-runtime/build/libs/wysicraft-1.1.0.jar`. The desktop executable is `src/Wysicraft.Designer/bin/Debug/net8.0-windows/ArcadiaStudio.exe`; keep its adjacent files when distributing. For a publish directory:

```powershell
dotnet publish src/Wysicraft.Designer -c Release -r win-x64 --self-contained false -o artifacts/designer
```

## Getting started

1. Launch Designer. Choose **File → New Project**, enter `Airship Controls`, and set pack ID `airship_controls` in Project Settings.
2. Click **Screen settings** and change **Screen ID** from `main` to `cockpit` in Properties. The Main screen and links follow the rename.
3. Drag a **Button** from the toolbox onto the canvas. Set its text to `START ENGINE`.
4. Add a **Label** with ID `status`.
5. Select the button, open **Events**, choose `click` and **Client**, and add a `set_text` action with Target `status` and Value `Starting...`.
6. Choose **Server**, add `command`, and enter `say Engine start requested` in Value / Command.
7. **Preview** the UI. Client actions execute locally; server actions appear as `SIMULATED SERVER` output.
8. Save the project to a folder, then **Export .wysicraft**.

Alternatively, open `samples/AirshipControls/project.json`. It includes START/STOP ENGINE, status and altitude labels, navigation and close controls. The ready-to-install archive is `samples/airship_controls.wysicraft`.

The canvas supports dragging, **Shift-click or Ctrl-click multi-selection**, resize handles on every corner and edge, grid snapping, arrow movement, Shift+arrow movement, Delete, clipboard operations and undo/redo. **Ctrl+C / Ctrl+V** copy/paste the full selection, preserving styles and remapping references between copied elements. **Ctrl+X** cuts the selection. Shift-click in Layers selects a range. The inspector updates the canvas while valid values are typed. Options are separated with `|`; local variables use `name=value;name=value` in Screen / Variables.

### Styling and layers

- In **Appearance**, click the color swatch button to open an HSV wheel with brightness/opacity sliders. Both the inspector and wheel accept `#RRGGBB` or `#AARRGGBB`; typing a valid value updates the selector and preview immediately.
- **Choose PNG…** assigns and packages a skin image. Buttons and panels display it in the canvas and preview; **Use color only** clears the image. Buttons/panels and runtime dropdowns support rounded skins using the **Radius** slider.
- Images support up to **32 MiB** and **8192 × 8192 pixels**, with **256 MiB per pack**. Desktop previews cache a reduced-resolution image when needed; exports preserve the original image bytes.
- Font controls include Minecraft font resource ID, size scale, bold, italic, underline, shadow and alignment. The WPF preview approximates Minecraft glyph shapes; custom fonts must exist in the Minecraft resource pack.
- **Layers** lists the current screen front-to-back. Select hidden/overlapping elements there, move them forward/backward, or right-click to show/hide, duplicate, or delete. Canvas right-click menus also support duplication. Duplicating a selection assigns unique IDs and remaps references between the copied elements.

Install the updated runtime JAR alongside the new designer when using these styling fields. Older packs keep their defaults; older runtimes do not render the new font/radius fields.

### Interactive preview and JavaScript

Preview has its own console: every click is logged, even when no action is assigned. Buttons have hover/pressed feedback, and decorative images/panels cannot swallow input. Text/slider input keeps focus while bound labels update. Use **Reset preview** to restore the saved initial values.

Try this in the preview's JavaScript scratchpad and click **Run JavaScript**:

```javascript
console.log("Hello from the preview!");
ui.setText("status", "It works!");
```

For an event, create `scripts/client/cockpit.js`, write the following, then assign that file and function `engineStart` to the button's `click` Client event:

```javascript
function engineStart(ctx) {
  console.log("Clicked", ctx.elementId);
  ctx.ui.setText("status", "Starting engine...");
}
```

The easiest workflow is now **select an element → Events → New Script**. A function and unique file are generated automatically. Edit its body and click **Save & Assign**; both file and function are attached to that event. For projects with an existing folder this also saves to disk; new unsaved projects keep the script in memory until File → Save. **Edit Script** reopens it. **Test Event** automatically fires the selected event in preview, and **Test Function** in the editor tests your unsaved draft. Existing scripts can still be selected under the collapsed **Use an existing script…** section.

The preview runs actual JavaScript using Jint in a separate constrained process. It exposes no CLR objects, filesystem, network, Minecraft objects or real commands. Server operations are logged as simulated. Minecraft uses its own bundled GraalJS engine for Standard Client scripts; KubeJS Server scripts use the KubeJS bridge. See [SCRIPTING](docs/SCRIPTING.md).

## Install and open in Minecraft

1. Install NeoForge **21.1.250** for Minecraft **1.21.1**.
2. Put `wysicraft-1.1.0.jar` in the instance's `mods/` directory. Multiplayer requires the mod on both client and server.
3. Put the exported pack in `<game directory>/wysicraft/`. For a dedicated server, this is `<server directory>/wysicraft/`. Install the same pack on clients when using custom textures or external client scripts. UI definitions themselves are sent by the server.
4. Start the game/server, then run:

```text
/wysicraft reload
/wysicraft list
/wysicraft open cockpit
```

Reload requires permission level 2. The sample `say` commands also require the player's normal command permissions: use a cheats-enabled single-player world or an authorized operator to verify them. **Arcadia Studio does not elevate the player.**

For command blocks or operators: `/wysicraft open cockpit <player>`. `/wui` is an alias. Unpacked development projects can be placed in `wysicraft/dev/<project>/`. Reload closes active screens and rescans packs.

## Client and server actions

Client actions update text, visibility, enabled state, values, textures and local variables; play sounds; show messages; and request UI transitions. Variables support `${name}` text substitution. Conditions use a small parser with comparisons, AND, OR, NOT and parentheses.

Server actions run trusted commands, send messages, manage session variables, open/close screens and call registered addon functions. The client sends only a UI ID, element ID, event name, session token and bounded input value. Commands and server scripts are removed from the UI definition sent to clients. The server verifies the active session and resolves the action from its own pack.

`runCommandsAsServer=false` is the default in the generated server configuration. Enabling it is an explicit operator decision that gives trusted pack commands server authority. Client input is never substituted into commands.

For Create/Create Aeronautics, replace sample `say` actions with commands confirmed for your installed version. The sample commands are ordinary Minecraft examples, not purported Aeronautics APIs.

## Repository

| Directory | Responsibility |
| --- | --- |
| `src/Wysicraft.Models` | Shared C# JSON contract |
| `src/Wysicraft.Core` | Registries, validation, conditions, history |
| `src/Wysicraft.Packaging` | Source folders and safe ZIP import/export |
| `src/Wysicraft.Designer` | WPF desktop application |
| `wysicraft-runtime` | Java 21 / NeoForge mod, renderer and networking |
| `tests` | Focused C# integration checks |
| `samples` | Editable Airship Controls and exported pack |
| `docs` | Format, security, scripting and addon documentation |

See [FORMAT](docs/FORMAT.md), [SCRIPTING](docs/SCRIPTING.md), [RUNTIME_API](docs/RUNTIME_API.md), [SECURITY](docs/SECURITY.md) and [DEVELOPMENT](docs/DEVELOPMENT.md).

## MVP boundaries

- JavaScript files and event references are editable, validated and exported, and run in the designer preview. **The Minecraft runtime bundles a client JavaScript engine.** Standard Server scripts still need a provider; KubeJS Server scripts use the KubeJS bridge.
- Runtime text boxes offer end-of-text editing, backspace, paste and submit. Dropdowns cycle through options when clicked. They are functional basic controls, not full desktop widgets.
- Panels use absolute canvas coordinates and one-level parent relationships. Runtime scroll panels clip and scroll assigned children. Designer preview approximates panels, resource-pack images and item icons; it does not reproduce Minecraft rendering.
- Property/event panes have a fixed docking layout with splitters. Script editing is plain text with line numbers, without syntax highlighting or debugger.
- Local UI and per-open server session state are available. Persistent player/global state, live telemetry bindings and a demonstration block are future work.
- Builds and focused format/security checks are automated. A real Minecraft world click-through must still be performed in your installed mod environment; build success does not establish that interactive acceptance result.

Recommended next work: sandboxed JavaScript with enforceable CPU/memory limits, richer text/dropdown widgets, persistent variable scopes, automatic asset synchronization and Create addon integrations.

Appearance controls include full-bound label backgrounds, optional fills, border color/width, rounded corners, and text shadow color, opacity, horizontal/vertical offset, and blur. Border and shadow colors use the same color wheel and editable hex fields. Minecraft approximates soft shadow blur with nine samples.

### Project open and close commands

In **Screen settings** (the screen rename dialog), check **Main screen** and save. This replaces the previous main screen. Renaming a screen also updates built-in `open_ui` action references. Each loaded project's lowercase **Id** automatically supplies two commands:

- `/airship_controls.open` opens that project's Main screen for the executing player.
- `/airship_controls.close` closes that project's current interface for the executing player.
- `/airship_controls.open <player>` and `/airship_controls.close <player>` target a player from a server/block script or console (permission level 2).

Use `/wysicraft reload` after replacing or adding packs. Existing aliases resolve the latest Main screen; newly loaded projects get aliases immediately. Removed project aliases reject calls until the command tree is rebuilt or the server restarts. Conflicting command names are logged and never replace another mod's command. Screen IDs still need to be unique across installed packs.

Export includes only scripts assigned to screen or element events, on either side. Project saves keep all scripts for authoring. Helper functions inside an assigned script remain included; cross-file imports/modules are not currently supported. Standard Client scripts use the bundled GraalJS engine. Built-in actions and server command actions also work independently.

KubeJS Server scripts now have a separate **Export for KubeJS** installation ZIP and a public project/UI API. See [KubeJS setup and first Minecraft test](docs/KUBEJS.md). The included KubeBridge demo exercises a server callback, a player command, and a label update sent back to the client.

GUI image paths are automatic: `assets/<project_id>/textures/gui/<element_type>/<image_name>.png`. Assigned skins use the control type; generic image imports use `image`. Existing projects remain readable, and saves/exports upgrade older asset layouts. Minecraft resource packs can override the canonical paths.

**Minecraft test** launches a reusable real Minecraft 1.21.1 / NeoForge / KubeJS development instance, with `.open`, `.close`, a Globals dropdown, and game logs. See [setup and controls](docs/MINECRAFT_TEST.md). The game opens in its own window and does not automatically open your project UI.
