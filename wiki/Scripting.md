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

Web and desktop only: `ctx.client.playSound(id, volume)` plays one sound effect at a volume from 0 to 1, and `ui.setVolume(id, volume)` sets a Sound control's volume, at once if it is playing. Together they make a volume menu: keep the player's levels in a screen variable and pass them along. Minecraft ignores the volume and has no `setVolume`, so check `if (ctx.ui.setVolume)` in a project that also targets Minecraft.

## Spawned objects (web & desktop)

A game with crowds (enemies, bullets, pickups) doesn't need a control for each one. Make one control as a
**template**, usually hidden, and spawn copies of it while the game runs. The engine draws each copy, moves it,
collides it and removes it.

```js
function wave(ctx) {
  for (var i = 0; i < 20; i++) {
    // A copy of 'bat' at x, y (its top-left, like setPosition), chasing the player at 60 px a second.
    ctx.ui.spawn('bat', 40 * i, 0, { seek: 'player', speed: 60 });
  }
}
function batTouched(ctx) {        // the template's trigger_enter event, run by each copy
  if (ctx.value === 'player') ctx.ui.despawn(ctx.elementId);   // ctx.elementId is the copy's own ID
}
```

| Method | Does |
| --- | --- |
| `spawn(template, x, y, options)` | Adds a copy and returns its ID (`bat~12`). Options: `vx`, `vy` (a velocity), `seek` (a control's ID) with `speed` (px/s), `path` (a tilemap to find the way over), `separate` (px to keep from other copies), `life` (seconds, then it's removed), `clip` (a sprite clip), `texture`. `template` can also be a [component](#spawning-components) |
| `despawn(id)` | Removes a copy (design controls can only be hidden) |
| `seek(id, target, speed, { path })` | Keep moving toward another control, over a tilemap if `path` names one; `seek(id, '')` stops |
| `separate(id, px)` | Keep a copy at least `px` from other copies, centre to centre. Given a template, every live copy and every later one |
| `instancesOf(template)` | The IDs of a template's live copies, oldest first |

What a copy gets:

- **Everything the template has.** Its size, picture, sprite clips, body and collider, trigger setting, tags and
  events. Its events run with the copy's ID as `ctx.elementId`.
- **Its place in the draw order.** Copies draw right after their template, so put the template where you want the
  crowd to appear (under the HUD, over the floor).
- **Movement.** A copy that isn't a dynamic body moves by its velocity every frame. A dynamic body is moved by the
  physics instead, with gravity and collisions. `setVelocity`, `setPosition` and `getElement` work on copies like
  any control.
- **When it exists.** A copy exists once the script run that spawned it ends. Later calls in that same run can move
  or despawn it, and the next run sees it with `getElement` and `instancesOf`.

Up to 5,000 copies can be alive on a screen at once; more are skipped with a warning. The engine measures 1,000
seeking copies at under 1 ms a frame to move and about 2 ms to draw, and physics with 1,000 overlapping trigger copies
at about 4 ms. Minecraft has no spawned objects.

### Keeping a crowd apart

Seekers without a spacing all end up on the same spot. Give them one and they spread around their target instead, the
way a swarm surrounds the player:

```js
ctx.ui.separate('bat', 12);                          // every bat, now and later, keeps 12 px from the others
ctx.ui.spawn('slime', x, y, { seek: 'player', speed: 40, separate: 16 });   // or per copy
```

Spacing is only between copies (design controls and walls aren't pushed), and it isn't a collision: copies can still
overlap for a moment when a crowd presses in, then ease apart. 1,000 copies seeking and keeping apart cost about 2 ms
a frame.

### Following a map

A seeker heads straight for its target. With `path` it finds its way over a tilemap instead, round solid tiles and
through gaps, and never cuts a solid corner:

```js
ctx.ui.spawn('ghoul', x, y, { seek: 'player', speed: 60, path: 'dungeon' });
ctx.ui.seek(id, 'exit', 80, { path: 'dungeon' });   // an existing seeker onto the map
```

All the seekers following one map share one route map toward their target, rebuilt only when the target moves to
another tile or a tile changes, so a crowd costs little more than one seeker (about 5 ms to rebuild on the largest
map, 256 × 256 tiles). If there's no way through, a seeker goes straight.

For a route of your own, `ctx.physics.findPath(map, x1, y1, x2, y2)` gives the tile centres to walk through, the last
one being the goal's tile, or `null` if either end is off the map or on a solid tile or there's no way through.
Across a 256 × 256 maze it takes about 4 ms.

### Raycasts and line of sight

```js
var hit = ctx.physics.raycast(x1, y1, x2, y2, { ignore: 'player' });
// null, or { id, x, y, distance, normal: { x, y } }: the first thing the line meets, where, and which side it hit
if (ctx.physics.canSee('guard', 'player')) alarm(ctx);   // nothing solid between the two centres
```

A ray stops at controls with a body (round colliders on their circle, polygons on their box) and at solid tiles.
Triggers and controls without a body don't stop it. Options: `triggers: true` to stop at triggers too, `ignore` (a
control's ID), `tag` (only controls with that tag). 200 rays in one script run, with 300 copies on screen, take
about 6 ms.

### Spawning components

A [[component|Reusable-Components]] can be spawned like a template: everything in it, as one group.

```js
var id = ctx.ui.spawn('enemy_card', 40, 60, { seek: 'player', speed: 30 });   // 'enemy_card~3'
ctx.ui.setText(id + '_name', 'Slime');   // its controls are the root's ID + '_' + their ID in the component
ctx.ui.despawn(id);                      // removes the whole group
```

- The group gets a new, see-through **root** the size of the component, at x, y. Moving, seeking, spacing, `life` and
  `despawn` all go through the root, and its controls come with it. Despawning one control inside the group isn't
  allowed; despawn the root.
- Each control keeps its look, body and events. Actions aimed at another control of the component (Set Text, Show,
  Change Texture…) are aimed at this group's copy, as when you place a component in the editor.
- The component's variables are added to the screen's state if it doesn't have them yet.
- Groups draw on top of everything, one after another, or right after the control named by `after`
  (`{ after: 'floor' }`).
- At most 256 controls in a component you spawn. 500 groups of 4 controls spawn in about 40 ms and seek at about
  0.5 ms a frame.

Components travel with web and desktop exports for this (with the scripts only they use); Minecraft packs still
leave them out.

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
