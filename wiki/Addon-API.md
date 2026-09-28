# Addon API

For mod developers and KubeJS authors who want to open Wysicraft screens from their own code or add new server actions. You compile against the Wysicraft runtime (1.6.0) for Minecraft 1.21.1 / NeoForge. All calls must happen on the Minecraft **server thread**.

## Opening and updating screens

Class: `com.wysicraft.runtime.api.WysicraftApi`

| Method | Does |
| --- | --- |
| `openProject(player, projectId)` | Opens the project's Main screen |
| `closeProject(player, projectId)` | Closes that project's screen if it's open |
| `openUi(player, screenId)` | Opens a screen. Use `project:screen`; a bare ID resolves in the player's current project, or must be unique. |
| `closeUi(player)` | Closes the player's Wysicraft screen |
| `setText(player, id, text)` | Updates a control on the player's open screen |
| `setValue`, `setVisible`, `setEnabled` | Same, for value, visibility, enabled state |
| `setItem(player, id, itemId)` | Changes an Item Icon |
| `setItems(player, id, json)` | Fills an Item List with `[{item,count,name}]` JSON (128 rows / 4096 chars max) |
| `showPlayerInventory(player, id)` | Fills an Item List with the player's inventory |
| `inventoryJson(player)` | The player's main inventory as JSON |
| `setVariable(player, name, value)` | Sets a screen variable |
| `message(player, text)` | Sends a chat message |
| `runCommand(player, command)` | Runs a command **with the player's permissions** |
| `registerProjectCommand(projectId, command)` | Links an existing command to a project (shows in the test's Globals list) |

Updates only apply to the player's currently open screen; if nothing is open they fail.

```java
WysicraftApi.openUi(serverPlayer, "airship_controls:cockpit");
WysicraftApi.setText(serverPlayer, "status", "Docked");
```

From KubeJS:

```js
const Wysicraft = Java.loadClass('com.wysicraft.runtime.api.WysicraftApi');
Wysicraft.openProject(event.player, 'airship_controls');
```

## Server functions

Register a function once during mod initialization, then call it from a project with the built-in `server_function` action:

```java
WysicraftApi.registerServerFunction("engine_start", (ctx, argument) -> {
    if (!ctx.player().hasPermissions(2)) return;
    ctx.player().sendSystemMessage(Component.literal("Engine integration invoked"));
});
```

In the project: a Server action with Type `server_function`, Target `engine_start`, and an optional Value passed as `argument`.

`ctx` is a `ServerContext` with the player and the screen's session variables. You're responsible for permission checks and validating the argument.

## Custom server actions

`WysicraftApi.registerServerAction(id, (context, action) -> …)` adds a new server action type, which the pack validator then accepts. Never treat client-supplied text as commands, code or file paths.

## Client-side extensions

Client only (keep these out of dedicated-server code):

- `DynamicScreen.registerAction(id, (screen, action) -> …)` for custom client actions.
- `ElementRenderers.register(type, renderer)` for custom control rendering. A custom control also needs registering in the editor (`Wysicraft.Core.Registry.Register`) to appear in the Toolbox.

## Scripting providers

`Scripts.register(provider)` replaces the JavaScript engine. A custom provider must enforce its own limits and isolation.

## Commands for integrations

`/wysicraft open <screen> <player>` (permission level 2) and `/<project>.open <player>` work from command blocks and datapacks. For a block that opens a screen, call `openUi` from your block's server-side interaction after checking range and access.

## Variables

Screen variables live only while a screen is open. Store anything persistent (per-player or server-wide) in your own mod or KubeJS data.
