# Projects and screens

## Project files

A project is one file with the extension **`.arcadia`**. It holds all your screens, scripts (including ones you haven't assigned yet) and images.

- **File → New project** (Ctrl+N) starts a blank project. You're asked to save unsaved changes first.
- **File → Open project or pack…** (Ctrl+O) opens a `.arcadia` project. It also opens projects from before Arcadia Studio was renamed (`.wysicraftproj`), a `project.json` folder, or an exported `.wysicraft` pack. The first time you save a `.wysicraftproj`, Arcadia Studio offers to save it as `.arcadia` beside it (the old file is kept); a `project.json` folder or a pack is always saved as a new `.arcadia` rather than overwriting the old files.
- **File → Save** (Ctrl+S) and **Save as…** (Ctrl+Shift+S). Saving is atomic: if something goes wrong while writing, your previous file stays intact.
- Double-clicking a `.arcadia` (or older `.wysicraftproj`) opens it if you ticked file registration in the installer.

Project files contain your **server script source**. Only share them with people who should see that code. The files you give players are exports; see [[Exporting and installing|Exporting-and-Installing]].

## Project settings

**Project → Project settings…**

| Setting | Meaning |
| --- | --- |
| **Name** | Display name. |
| **Id** | Lowercase ID used for commands (`/<id>.open`, `/<id>.close`), the JAR name and image paths. Changing it moves your images to the new name automatically. |
| **Author**, **Version** | Metadata. Version is `major.minor.patch`. |
| **RuntimeVersion** | Oldest Arcadia Studio runtime your project needs. Exports set this for you. |
| **GridSize**, **Snap** | Canvas grid spacing and whether moves snap to it. |
| **Smooth pictures** | Web & desktop: pictures are scaled smoothly, for illustrated art, instead of with hard pixel edges. Leave it off for pixel art. |
| **Dependencies** | Mod IDs that must be installed (for example `kubejs`). Minecraft refuses to load the project without them. |

## Recovering unsaved work

Every 30 seconds, changed work is saved to a separate recovery draft in `%LOCALAPPDATA%\Arcadia Studio\Recovery`, including unsaved text in the script editor. Autosave never overwrites your real project file, and it runs in the background so the editor doesn't pause.

After a crash or power loss, choose **File → Recover unsaved project…**, pick the draft, then **Save as** to keep it. Drafts from other Arcadia Studio windows that are still open aren't listed. Saving normally, or choosing not to save, removes the draft.

## Screens

A project can have up to 128 screens. The dropdown above the canvas switches between them.

- **New screen** adds one. IDs are lowercase letters, numbers and underscores, and unique within the project.
- **Delete screen** removes the current screen after asking which screen should replace it in links and as the Main screen. It can be undone. You must keep at least one screen. A screen used as an Item List row template must be reassigned first. Screen IDs written inside your JavaScript must be updated by hand.
- **Screen settings** clears the selection and shows everything about the current screen in Properties, with its Open/Close events in Events.

Component sources (see [[Reusable components|Reusable-Components]]) aren't listed in the screen dropdown. You edit them from the Components panel.

### Screen settings

| Setting | Meaning |
| --- | --- |
| **Screen ID** | The screen's ID. Changing it updates built-in links (`open_ui` actions), the Main screen, row templates and components. IDs written inside scripts are **not** changed. Press Enter or click away to apply. |
| **Title** | Title shown in the screen frame, if the frame is on. |
| **Width**, **Height** | Design size in GUI pixels (16–4096). Controls anchored right, bottom, center or stretch move as you change it. |
| **Main screen** | The screen `/<project id>.open` opens. Ticking it on one screen removes it from the previous one. |
| **Variables** | Starting values for screen variables, written `name=value;name=value`. Scripts, conditions and `${name}` text use them. |
| **Responsive layout** | In Minecraft, controls move by their anchors when the game window is a different size. See [[Anchors and responsive layouts|Anchors-and-Responsive-Layouts]]. |
| **Show frame / title** | Draws Minecraft's opaque screen frame and title behind your controls. Off by default. |
| **Dim game behind UI** | Darkens the world behind the screen. Off by default. |
| **Fit to viewport** | For non-responsive screens, scales the whole screen down if it's larger than the game window. In a web game it also grows it to fill the page (a whole-number scale when that wastes under 12%, otherwise exactly). Minecraft screens only shrink. |
| **Tick interval (ms)** | How often the screen's `tick` event runs while it's open: 50–60000 milliseconds, or 0 for off. See [[Timers and keys|Events-and-Actions#timers-and-keys]]. |
| **Gravity** (web & desktop) | Pull on dynamic physics bodies, in pixels per second². See [[Advanced tools|Advanced-Tools]]. |
| **Key repeat (ms)** | How often a held key sends the `key` event again: 50–2000 milliseconds, or 0 so a held key sends it only once. Default 150. |

**Transparent screens:** turn off both frame and dimming, and add a **Panel** wherever you want a background. The canvas's grey backdrop is only a workspace aid; it isn't exported.

## How screens open in Minecraft

Each project automatically gets two commands:

- `/<project id>.open` opens the project's Main screen for you.
- `/<project id>.close` closes the project's screen for you.
- Add a player name (`/<id>.open Steve`) to target another player. That needs permission level 2, so it works from command blocks and the server console.

Other ways in:

- `/wysicraft open <screen>` or `/wysicraft open <project>:<screen>` opens a specific screen. `/wui` is a short alias.
- An `open_ui` action (on any event) or `ui.open()` in a script navigates between screens. Client scripts can use `ui.open()` in web & desktop projects; in Minecraft only server scripts can. See [[Events and actions|Events-and-Actions]].
- **Game variables** (Project settings) carry from screen to screen, for a score a Game over screen shows; saved ones come back next visit. See [[Where to keep state|Scripting#where-to-keep-state]].
- Mods and KubeJS can open screens; see [[Addon API|Addon-API]].

Two projects can both have a screen called `main`. Internally, screens are addressed as `project_id:screen_id`.
