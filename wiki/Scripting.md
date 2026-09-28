# Scripting with JavaScript

When built-in actions aren't enough, attach a JavaScript function to an event. No scripting mod is needed: Wysicraft includes its own JavaScript engine in Minecraft and in the editor.

| Kind | Runs where | Use for |
| --- | --- | --- |
| **Standard Client** | The player's game | Instant screen updates, local logic, sounds, messages |
| **Standard Server** | The Minecraft server | Player data, commands, updates that must be trusted |
| **KubeJS Server** | The server, via KubeJS | Full KubeJS/Minecraft APIs. See [[KubeJS integration|KubeJS-Integration]]. |

Scripts live in your project under `scripts/client/…` or `scripts/server/…`. The folder decides the side.

## The easy way: New Script

1. Select a control, open **Events**, and pick the event (for example `click`) and side (Client or Server).
2. Click **New Script**. A file and function are created for you.
3. Write the function body and click **Save & Assign**. The script is now attached to that event.
4. **Test Event** fires that event in Preview. **Test Function** in the script editor tests your unsaved draft.

**Edit Script** reopens the attached script, and **Detach** removes it from the event (the file stays in your project). To reuse an existing file, expand **Use an existing script…** and choose it with **Attach existing script**.

You can also write scripts in the **Scripts** tab (bottom panel) with **New script**, **Import script** and **Save script**. **Help → Script API and snippets**, or **Ctrl+Space** in the editor, inserts ready-made API calls.

## Templates

The script dropdowns include ready-made **[Template]** scripts. Pick one, edit the constants at the top (such as `ITEM_ID`, `COUNT`, `LIST_ID` or `COMMAND`), and **Save & Assign**. Existing scripts are never overwritten.

| Template | Does |
| --- | --- |
| Give item — Standard Server / KubeJS Server | Grants an item (the Standard version uses `/give`, so it needs permission) |
| Run server command | Runs an existing command as the player |
| Populate inventory list | Fills an Item List |
| Open project | Opens another project's Main screen |
| Teleport — Standard Server | Teleports the player |
| Message player — Standard Server | Sends a chat message |
| Change text | Updates a label |
| Close screen | Closes the screen |
| Global reward / Open UI / command alias — KubeJS | Registers a server-wide command such as `/<project>.reward` |

## Script basics

Your function receives `ctx`:

```javascript
function engineStart(ctx) {
    console.log('Clicked', ctx.elementId);
    ctx.ui.setText('status', 'Starting engine...');
    const visits = Number(ctx.state.get('visits') || '0') + 1;
    ctx.state.set('visits', String(visits));
}
```

- `ctx.elementId`: which control fired the event.
- `ctx.value`: the event's input (text box text, slider value, row index, the key name for a screen `key` event, and so on).
- Scripts run separately from the page with the same **2-second limit** as in Minecraft, so a script stuck in a loop is stopped instead of freezing Preview or the app.
- Sprites: `ui.play(id, 'run')` switches clip. Sound controls: `ui.setValue(id, 'play')` or `'stop'`.
- Web and desktop ([[advanced tools|Advanced-Tools]]): `ui.animate(id)`, `ui.stopAnimation(id)`, `ui.setVelocity(id, vx, vy)`, `ui.setPosition(id, x, y)`, `ctx.input.isDown(name)`, `ctx.input.axis(name)`, `ctx.physics.touching(id)` (IDs it touches or overlaps), `ctx.physics.isTouching(a, b)`, and `ctx.ui.getElement(id)` with `x`, `y`, `width`, `height`, `vx`, `vy`.
- `ctx.repeat`: `true` when a screen `key` event comes from a key being held down, `false` for a fresh press. Use it to ignore held keys for one-shot actions such as jump or drop.
- `ctx.state.get(name)` / `ctx.state.set(name, value)`: screen variables (strings). Use them to remember things; JavaScript globals are reset on every call.
- `console.log/warn/error`: messages for Preview's console and the game log.

### UI methods (`ctx.ui`, also available as `ui`)

| Method | Does |
| --- | --- |
| `setText(id, text)` | Change text |
| `setValue(id, value)` | Change a value |
| `setVisible(id, bool)` / `setEnabled(id, bool)` | Show/hide, enable/disable |
| `setItem(id, 'minecraft:diamond')` | Change an Item Icon (ID must be `namespace:path`) |
| `setItems(id, rows)` | Fill an Item List with `[{item, count, name}]` |
| `getVariable(name)` / `setVariable(name, value)` | Screen variables |
| `getElement(id)` | `{id, text, setText(), setItem()}` |
| `close()` | Close the screen |
| `changeTexture(id, resource)` | Client only: swap an image |
| `open(screenId)` | **Server only:** open a screen (this project, or `project:screen`) |

Client scripts also have `ctx.client.playSound(id)` and `ctx.client.sendMessage(text)`.

## Server scripts

```javascript
function refresh(ctx) {
    ctx.ui.setItems('inventory', ctx.player.getInventory());
    ctx.ui.setText('status', 'Hello ' + ctx.player.getName());
}
function portal(ctx) {
    if (!ctx.player.hasPermission(2)) { ctx.message('Operators only.'); return; }
    ctx.server.runCommand('portal');
}
```

| API | Returns / does |
| --- | --- |
| `ctx.player.getName()`, `getUuid()` | Text |
| `ctx.player.getPosition()` | `{x, y, z, dimension}` |
| `ctx.player.getInventory()` | Main-inventory items as `[{item, count, name}]` |
| `ctx.player.hasPermission(level)` | `true`/`false` for levels 0–4 |
| `ctx.server.runCommand(cmd)` | Runs a command **with the player's own permissions**, always |
| `ctx.message(text)` / `ctx.server.sendMessage(text)` | Chat message to the player |

These return plain values, never Minecraft or Java objects. UI changes from server scripts are sent to the player's open screen.

> Server scripts can build commands from player input, so `runCommand` always uses the player's permissions, even if the server enables `runCommandsAsServer` for built-in command actions.

## Preview vs Minecraft

- **Preview** runs Client scripts for real. Server scripts run in simulation: commands and server UI calls are logged, not applied. The simulated player is "Preview player" at the origin with an empty inventory and no permissions.
- KubeJS scripts only run in Minecraft; Test Event says so.
- Errors show the script file and line where available.

## Limits

| | In Minecraft | In Preview |
| --- | --- | --- |
| Script size | 256 KiB | 1 MiB (web & desktop projects) |
| Statements per call | 100,000 | 20,000 |
| Time per call | 2 seconds | 300 ms |
| UI operations per call | 128 | 128 |

Server scripts also share a **per-player time budget** (about 100 ms per second, with a 250 ms burst). If one player triggers scripts too fast, their server scripts are skipped until it refills, and the server log notes it.

Scripts can't access Java classes, files, the network, processes or the host system. They run inside Minecraft without a separate memory cap, so **only install packs you trust**; see [[Security and permissions|Security-and-Permissions]].

## What gets exported

Only scripts attached to an event are exported. Your project file keeps every script, including unfinished ones. Each script file is self-contained: `import` between files isn't supported, but helper functions in the same file work.
