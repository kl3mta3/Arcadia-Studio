# KubeJS integration

If your modpack uses **KubeJS**, server events can run KubeJS scripts with full access to KubeJS's APIs, beyond what Standard Server scripts allow. Requirements: Minecraft 1.21.1, NeoForge, **KubeJS 2101** and **Rhino** on the server.

## Writing a KubeJS handler

1. Select a control, open **Events**, choose the event and the **Server** side.
2. Click **New Script** and choose **KubeJS example**, or set the script engine to `kubejs`.
3. Write the function and click **Save & Assign**.
4. Click **Export for KubeJS**, then test in Minecraft. Preview doesn't run KubeJS scripts.

```js
function on_click(event) {
    event.message('Hello from the Minecraft server');
    event.ui.setText('status', 'Updated from KubeJS');
    event.runCommand('portal'); // uses the clicking player's permissions
}
```

Put your button logic **inside the function**. Code outside it runs when KubeJS loads or reloads scripts, not on click.

## What `event` gives you

| Member | Is |
| --- | --- |
| `event.player` | The Minecraft `ServerPlayer` |
| `event.server` | The `MinecraftServer` |
| `event.value` | The event's input |
| `event.message(text)` | Chat message to the player |
| `event.runCommand(cmd)` | Runs a command with the **player's** permissions |
| `event.state.get(name)` / `.set(name, value)` | Screen variables |
| `event.ui.…` | `setItem`, `setItems`, `showPlayerInventory`, `setText`, `setValue`, `setVisible`, `setEnabled`, `setVariable`, `getVariable`, `open(screenId)`, `close()` |

`event.server.runCommandSilent(...)` runs with **server** authority. That's KubeJS's own API, so check permissions yourself before using it.

UI calls update controls on the player's open screen. They can't create new controls.

## Exporting

**Export for KubeJS** produces a project JAR that bundles your KubeJS handlers; Arcadia Studio hands them to KubeJS as it loads scripts, so no files are copied into `kubejs/`. The modpack still needs KubeJS and Rhino installed.

If you're moving from the old loose-file export, delete the old generated Arcadia Studio scripts from the `kubejs` folders first to avoid duplicate registrations.

## Opening screens from your own KubeJS scripts

Arcadia Studio creates a helper for each project (new or changed helpers need a restart):

```js
global.my_project.open(event.player);
global.my_project.close(event.player);
```

Or use the API directly, inside a server event:

```js
const Arcadia Studio = Java.loadClass('com.wysicraft.runtime.api.WysicraftApi');
Wysicraft.openProject(event.player, 'my_project');   // opens the Main screen
Wysicraft.openUi(event.player, 'shop');               // a specific screen
Wysicraft.setText(event.player, 'status', 'Hello');
```

More methods are listed on [[Addon API|Addon-API]].

## Mixing script kinds

KubeJS Server scripts, Standard Server scripts, Standard Client scripts and built-in actions can all live in one project. KubeJS **Client** scripts aren't supported.

## Trust

Installed KubeJS scripts are trusted server code with all of KubeJS's power. They don't have the limits that Standard scripts have. Review them like any other server script.
