# Events and actions

An **event** is something that happens, such as a click, typing or the screen opening. You attach **actions** (built-in steps) and optionally a **script** to it. Select a control and open the **Events** tab, or click **Screen settings** for the screen's own events.

## Which events exist

| Control | Events |
| --- | --- |
| Button | `click` |
| Text Box | `text_changed` (each edit), `submit` (Enter) |
| Checkbox | `checked`, `unchecked` |
| Slider, Dropdown | `value_changed` |
| Item List | `item_click`, `item_primary`, `item_secondary` (value = row index) |
| Every control | `hover`, `mouse_enter`, `mouse_leave` (client only, at most 4 per second) |
| Screen | `open`, `close`, `tick`, `key` (tick and key are client only) |

## Timers and keys

Two screen events make games, clocks and animations possible without any hover tricks:

- **`tick`** runs on the player's screen every few milliseconds while the screen is open. Set how often with **Tick interval (ms)** in Screen settings (50–60000; 0 turns it off). If a run is still going when the next one is due, the next one waits.
- **`key`** runs on the player's screen for each key pressed while no text box is being typed in. A script receives the key's name as `ctx.value`: `a`–`z`, `0`–`9` (number row and keypad), `space`, `enter`, `tab`, `backspace`, `left`, `right`, `up`, `down`, `f1`–`f12`. Escape is never sent; it always closes the screen. Holding a key repeats it every **Key repeat (ms)** (Screen settings, 150 by default; 0 means a held key sends one event), and a script can tell a held key from a fresh press with `ctx.repeat`. Presses never pile up: a key arriving while the previous event is still running is dropped, so holding a key can't queue a backlog of moves.

Both work in Preview too. They can only use Client actions or a client script, because they fire far too often for the server. For server work, have the script show a button the player clicks, or use a control event. Export the project again after updating Arcadia Studio so the game gets the runtime the editor bundles.

## Sounds on events

Choose **play_sound** as an action and a sound picker appears. Pick one of the project's sounds, click **Import…** to add a file right there, or type a Minecraft sound ID such as `minecraft:ui.button.click`. It plays whenever the event fires. Minecraft sounds only play in Minecraft.

## Web and desktop events

With [[advanced tools|Advanced-Tools]] a screen also has `input_pressed`, `input_released` and `animation_end`, and physics bodies have `collide`, `collide_stay` and `collide_end`, plus `trigger_enter`, `trigger_stay` and `trigger_exit` for [[triggers|Advanced-Tools]]. Stay events repeat every 250 ms. They run on the player's screen only.

## Client and Server

Every event has two sides:

- **Client:** "Runs locally for this screen." Instant visual changes on the player's own screen.
- **Server:** "Trusted server actions. Preview simulates these." Runs on the Minecraft server: commands, messages, navigation, server scripts.

When an event fires, client actions run first on the player's screen, then the server is told which control and event fired, and the server runs its own actions from **its own copy** of your project. A modified client can't invent server actions or commands.

## Adding actions

In the Events panel, pick the event, then under Client or Server click **+ Add action**, choose a **Type**, and fill in **Target** and **Value**. **Remove** deletes an action. Actions run top to bottom.

### Client actions

| Type | Target | Value | Does |
| --- | --- | --- | --- |
| `set_text` | control Id | new text | Changes a control's text. `${var}` works. |
| `set_visible` | control Id | `true` / `false` | Shows or hides a control. |
| `set_enabled` | control Id | `true` / `false` | Enables or disables a control. |
| `set_value` | control Id | new value | Sets a slider, checkbox, text box, progress bar or dropdown value. |
| `change_texture` | control Id | image resource | Swaps a control's image. |
| `set_variable` | variable name | value | Sets a screen variable. |
| `toggle_variable` | variable name | – | Flips a true/false variable. |
| `play_sound` | – | sound ID | Plays a sound, for example `minecraft:ui.button.click`. |
| `message` | – | text | Shows a message to the player. |
| `open_ui` | – | screen ID | Asks to switch to another screen. |
| `close_ui` | – | – | Closes the screen. |

### Server actions

| Type | Target | Value | Does |
| --- | --- | --- | --- |
| `command` | – | command | Runs a Minecraft command **as the player, with the player's permissions**. The command is used exactly as written; player input is never inserted into it. |
| `message` | – | text | Sends the player a chat message. `${var}` works. |
| `set_variable` / `toggle_variable` | variable | value | Changes the server's copy of a variable. |
| `open_ui` | – | screen ID | Opens another screen of this project. (To open another project's screen, use a server script: `ctx.ui.open("project:screen")`.) |
| `close_ui` | – | – | Closes the screen. |
| `player_inventory` | Item List Id | – | Fills an Item List with the player's inventory. |
| `server_function` | function ID | argument | Calls a function registered by a mod or KubeJS. See [[Addon API|Addon-API]]. |

## Permission level and cooldown (Server side)

Each Server handler has:

- **Permission level:** `0 — Everyone`, `1 — Moderator`, `2 — Operator`, `3 — Administrator`, `4 — Owner`. Players below it can't trigger the server side of this event.
- **Minimum interval (ticks):** a per-player cooldown, 0–1200 ticks (20 ticks = 1 second). The default is 4. Reopening the screen doesn't reset it. Text and slider changes aren't delayed by it, so the final value always arrives.

Both are enforced by the server.

## Scripts on events

Instead of (or as well as) actions, an event can run a JavaScript function. In the Events panel use **New Script**, write the function, then **Save & Assign**. See [[Scripting|Scripting]] for the full workflow and API.

## Variables

- Each screen has string variables, with starting values set in [[Screen settings|Projects-and-Screens#screen-settings]].
- Show them in text with `${name}`, and use them in [[conditions|Properties-and-Appearance#conditions]].
- Variables reset every time the screen opens; they aren't saved between sessions.
- Client and server each keep their own copy. The server mirrors trusted client changes (visibility, enabled state, variables) so it can check what's allowed. Server-side changes reach the client through script UI calls such as `ctx.ui.setVariable`.
- The player's input is available on the server as `event_value` (and `ctx.value` in scripts). It can be shown in messages but is never put into commands.

## Navigation between screens

Use `open_ui` with a screen ID from the same project; validation checks that the screen exists. A server script can also open another project's screen with `ctx.ui.open("project_id:screen_id")`. Navigation from the client side is a request; the server decides.
