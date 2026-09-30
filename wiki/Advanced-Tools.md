# Advanced tools (web and desktop)

Arcadia Studio can make more than Minecraft screens. The advanced tools add **physics and components**, **tilemaps**, **keyframe animation**, **state graphs**, **shaders**, **particles** and **gamepad inputs** for [[web page, Windows and Electron apps|Web-and-Desktop-Apps]]. They don't work inside Minecraft.

## Made for

**Project settings → Made for** says where a project will run:

| Made for | What you get |
| --- | --- |
| **Minecraft and web & desktop** (new projects) | Everything is available. Anything Minecraft can't run is listed when you export to Minecraft. |
| **Minecraft only** | The Advanced toolbox and menu are hidden, and Minecraft's limits are errors. |
| **Web & desktop only** | Advanced tools and bigger limits, with no Minecraft warnings. |

Web and desktop projects can go bigger than Minecraft allows:

| | Minecraft | Web & desktop |
| --- | --- | --- |
| Screen size | 4096 pixels each way | 16384 |
| Controls per screen | 512 | 16,000 |
| Screens | 128 | 1000 |
| Shortest Tick interval | 50 ms | 16 ms (one frame) |
| UI changes per script call | 128 | 100,000 |
| Script variables between events | Start over every event | Kept, with **Scripts keep their variables between events** (Project settings) |
| Saved games (`ctx.save`) | None | 512 KB per game |

## Making a game: which parts to use

Before writing your own engine in a script, check whether a part already does the job:

| You need | Use | Where |
| --- | --- | --- |
| Enemies, bullets, coins, anything made while playing | **Spawned objects**: `ui.spawn(template, x, y, …)`; each copy runs the template's events as itself | [[Spawned objects|Scripting#spawned-objects-web--desktop]] |
| Things chasing the player or walking a route | `seek`, `separate` and paths over a tilemap | [[Following a map|Scripting#following-a-map]] |
| Walls, floors, hitting and overlapping | **Physics** bodies and colliders, solid tiles on a tilemap, collide and trigger events | [Physics](#physics), [Tilemaps](#tilemaps) |
| Line of sight, "what's in front of me" | `ctx.physics.raycast` and `canSee` | [[Raycasts|Scripting#raycasts-and-line-of-sight]] |
| A world bigger than the screen | A **Camera**; attach the score and buttons to it | [Camera](#camera) |
| Idle, run, jump, land | **State graphs** on a sprite | [State graphs](#state-graphs) |
| Movement, pickups, top-down or platform controls | **Components** such as Character controller, Top-down mover, Follower, Pickup | [Components](#components) |
| Rooms or levels | One screen each; keep progress in script variables or `ctx.save`, since screen variables start over when a screen opens | [[Where to keep state|Scripting#where-to-keep-state]] |
| Progress that survives closing the game | `ctx.save` | [[Saved games|Scripting#saved-games-web--desktop]] |

## The Minecraft check

Arcadia Studio always works out, from the project itself, what Minecraft can't run:
- advanced tools
- sounds that aren't `.ogg`
- anything past Minecraft's limits

Nothing is stored, so the list is always current. **Validate** shows these as `[Minecraft only]`.

Past 4,000 controls on one screen, **Validate** also gives **advice** — it blocks nothing — saying roughly what drawing that screen costs a frame. Plain controls cost about 0.6 ms per thousand; controls with text cost about 2.6 times that. For thousands of moving things, use particles or a tilemap rather than thousands of controls.

When you **export to Minecraft** or **Test in Minecraft** and something is in the way, a list opens with each thing and where it is. Click a line to go straight to it. It shows 50 lines at first; with more than that, the project is really a web and desktop project, and **Show all** lists the rest. Web page, Windows app and Electron exports are never blocked by this list.

## The Advanced toolbox

At the bottom of the **Toolbox**, click **▸ Advanced · web & desktop** to open it (a short note explains it the first time). It holds the **Box**, **Circle** and **Polygon collider** (invisible walls, floors and bumpers for physics), the **Camera**, **Particles** and the **Tilemap**.

The **Advanced** menu has **Inputs**, **Input creator**, **Animations**, **State graphs**, **Shaders**, **Particle maker**, **Particles**, the **Collider editor**, the **Tile painter** and **Collision layers**.

Advanced items show in Properties under **Advanced · web & desktop**.

## Camera

A **Camera** is the screen's view. Only what's inside the first visible camera is shown, scaled to fill the game, so a smaller camera zooms in. Nothing outside it is drawn or clickable. On the canvas it's an orange dashed frame labelled with its zoom; grab it by its edge or in Layers, and clicks inside it reach the controls underneath.

- **Scroll and zoom** from scripts with `ui.setPosition('camera1', x, y)` and `ui.setSize('camera1', w, h)`, or animate its x, y, width and height.
- **Attach** controls to the camera (select them with it, then right-click → Attach) to keep a score, lives or a menu in view while it moves. They zoom with it.
- **Switch views** by showing one camera and hiding another.

Without a camera, **Clip to screen** (Screen settings) hides whatever sits outside the screen's own area, for games that scroll things in from off screen.

## Components

Under **Advanced · web & desktop** in Properties, every control has a **+ Add component** button, the way Unity does it. Type to filter the list, pick one, and it appears as a **card** with its settings and a **✕** to take it off again. Cards fold with the arrow beside their name, and stay folded until you open them.

| Component | What it does | Settings on the card |
| --- | --- | --- |
| **Collider** | The shape the physics knows about. On its own it is a wall, floor or platform (a static body). | Shape (box, circle or polygon with **Edit collider points…**), **Trigger**, collision **Layer** and **Collides with** |
| **Rigidbody** | Makes the control move: it falls with the screen's gravity, collides and bounces. | Body (**Dynamic** or **Kinematic**), **Bounce**, **Friction**, the **Bouncy** and **Slippery** presets, and the screen's gravity with a link to **Screen settings** |
| **Character controller** | A platformer character driven by your `left`, `right` and `jump` inputs (created if missing), with a jump that only works on the ground. | `SPEED`, `JUMP`, `GROUND_GRIP` |
| **Top-down mover** | Eight-way movement with no gravity. It stops against walls, colliders and solid tiles. | `SPEED` |
| **Follower** | Moves steadily towards the nearest control with a tag — an enemy, a homing shot, a pet. | `TARGET_TAG`, `SPEED`, `STOP_AT` |
| **Pickup** | Disappears when something touches it and adds to a screen variable. | `WORTH` |
| Presets: **Kinematic body**, **Trigger zone**, **Bouncy**, **Slippery** | One-click setups that set the fields above. | — |

A few things worth knowing:

- **Nothing is hidden.** A card only sets the fields it shows and, for the movement and gameplay ones, writes an ordinary script into **Scripts** named after the control (`scripts/client/player_controller.js`). **Open script** on the card takes you to it, and you can change anything in it.
- **The numbers on a card are the script's.** They are the `var SPEED = 140;` lines at the top of the script. Editing one on the card writes it back into the script in place, comment and all; editing the script changes the card. Keep those lines in that form if you want them on the card.
- **✕ cleans up after itself.** Removing a Rigidbody leaves the control a plain static Collider; removing the Collider clears the body. Removing a script component stops the event running its script and deletes the script — unless you edited it, in which case it is kept and the log tells you. Inputs, tags and screen variables stay, because other things may use them.
- **Each movement component ticks on its own control.** Character controller, Top-down mover and Follower run their script from the `tick` event of the control they move, so a player and several enemies all move at once, and the screen's own `tick` stays free for your game loop. (Projects from before this are moved over when they open.)
- **Kinematic bodies with a velocity move and stop at walls.** `ui.setVelocity` on a kinematic body moves it, and it stops against static bodies, colliders and solid tiles. One moved by `ui.setPosition` or an animation goes exactly where it's put.
- **Gravity is the screen's.** It is set once in **Screen settings** and pulls on every dynamic body on the screen; the Rigidbody card shows the value and links there.
- **Sprites, images and panels all take the same cards.** The **collider** control in the Advanced toolbox is still there for standalone walls: it is its own Collider card, without a ✕.
- An AI assistant can add and remove the same components through MCP (`add_component` / `remove_component`).

## Physics

Physics is "arcade" style: things fall, collide, bounce and slide, but don't rotate.

1. In **Screen settings**, set **Gravity** (pixels per second², for example `600`).
2. Add a **Rigidbody** component to the controls that move. Pick the Collider's shape (box, circle or polygon), and the Rigidbody's **Bounce** (0–1) and **Friction** (0–1).
3. Make walls and floors with colliders from the Advanced toolbox, or add a **Collider** component to any control (a static body).
4. **Kinematic** bodies are moved by scripts or animations, and push dynamic bodies out of the way.

Moving a panel moves everything attached inside it.

- **Events:** a body's `collide` event runs when it starts touching another body, `collide_stay` every 250 ms while it keeps touching, and `collide_end` when it stops. The value is the other control's ID (`${event_value}` in actions, `ctx.value` in scripts).
- **Scripts:** `ui.setVelocity(id, vx, vy)` and `ui.setPosition(id, x, y)` move bodies. `ctx.ui.getElement(id)` tells you `x`, `y`, `width`, `height`, `vx` and `vy`. `ctx.physics.touching(id)` lists the IDs it is touching or overlapping, and `ctx.physics.isTouching(a, b)` checks one pair.

Colliders are invisible in apps. Preview outlines them so you can see your walls.

### Triggers

Tick **Trigger** on the Collider card (or add the **Trigger zone** preset) to make it pass through things and report it instead. Use it for pickups, checkpoints, goal lines and danger zones.

| Event | Runs when |
| --- | --- |
| `trigger_enter` | Something starts overlapping the trigger |
| `trigger_stay` | Every 250 ms while it stays inside (the first one 250 ms after it enters) |
| `trigger_exit` | It leaves |

Both controls get the event, each with the other's ID as the value, so you can put `trigger_enter` on the zone (value: who came in) or on the player (value: which zone).

- Whatever enters needs a physics body. For a control your scripts move, use **Kinematic**.
- A trigger pair needs something that moves (dynamic or kinematic), so zones that sit still never report each other.
- Stay events are skipped while another event is still running, like Tick, so a long overlap never builds up a backlog.

## Collider editor

Select a polygon collider (or a body with a polygon collider) and double-click it, or use **Advanced → Collider editor**.

- **Points:** click to add a point after the white (selected) one, drag to move, right-click or Delete to remove.
- **Curves:** press **C** (or the button) to turn the selected point into a **curve handle**. The edge between its neighbours then bends smoothly.
- **Trace image:** pick a project image or open any image file. **Alt+drag** moves it, **Size** scales it and **Fade** sets its transparency. **Freeze image** stops it moving while you trace.
- **Checking:** the outline must be a closed shape whose edges don't cross. The status line turns green when it is. Outlines that go inward (like an L shape) are fine.

## Animations

**Advanced → Animations** (also in Screen settings) makes keyframe animations for the current screen:

1. **+ New animation**, then give it an ID, a length in milliseconds, and optionally **Loop** and **Autoplay** (starts when the screen opens).
2. **+ Track** picks a control and a property: `x`, `y`, `width`, `height` or `opacity`.
3. Add keyframes: a time, a value and an easing (linear, ease in, ease out, ease in-out or step). **Add keyframe from canvas** copies the control's current value.

The timeline slider previews the animation on the canvas. Everything snaps back when the window closes. **Preview** plays it for real.

- **Scripts:** `ui.animate('id')` starts an animation and `ui.stopAnimation('id')` stops it.
- **Events:** the screen's `animation_end` event runs when a non-looping animation finishes, with its ID as the value.

## Inputs

**Advanced → Inputs** defines named inputs such as `jump` or `left`. Each can be pressed by keys, gamepad buttons and a stick direction.

- **Presets:** Keyboard, Xbox, PlayStation and Generic gamepad add the usual inputs (left, right, up, down, jump, action, pause…).
- **Gamepads:** Xbox, PlayStation and generic pads use the same standard button layout; only the names differ. Choose how buttons are labelled with **Show gamepad buttons as**.
- **Screen events:** the screen's `input_pressed` and `input_released` events run with the input's name as the value.
- **Scripts:** `ctx.input.isDown('jump')` and `ctx.input.axis('left')` (0–1, how far the stick is pushed that way). For tap-to-move, `ctx.input.pointer()` gives `x`, `y` (screen coordinates), `down` (a press on the floor is held) and `presses` (how many there have been), from a finger or the mouse. Presses on buttons and other controls don't count; sprites, images, labels and panels are scenery.
- **On-screen controls:** give a button or shape a **Presses input** in Properties to make an on-screen control. Pressing it presses the input *as well as* running its own click, hover, enter and exit events.

Clicks, hover and the Key event work exactly as before; inputs only add to them.

## Input creator

**Advanced → Input creator (with the assistant)** writes inputs for you. Say what a button should do in plain words and an AI assistant writes the script; you check it before anything reaches the project.

1. Choose a **Device**: Keyboard, Gamepad (generic), Xbox controller or PlayStation controller.
2. Click **+ Input** for each thing you want. On each row pick a **Button** — only buttons no input already uses are listed, named the way that controller labels them (Cross and Circle on PlayStation, A and B on Xbox) — optionally a **Name** for the input, an **Action** to lean on (or let the assistant choose), and describe what it should do. Name your controls and variables: the assistant can see the open project.
3. **Create** sends the row to the assistant. The status line shows what it's doing.
4. **Review** shows exactly what came back: the input, the script and where it will be wired.
5. **Test** applies it to a *copy* of the project and validates it, so nothing changes yet.
6. **Save** adds the input, the script and the screen's `input_pressed` wiring as **one Undo step**.

**Create all**, **Review all**, **Save all to project** and **Delete all** do the same for every row; the deletes ask first. Everything except Create works without an assistant.

Create needs MCP running. With **Ask Agent** set up (see [[AI assistants|MCP-and-AI-Assistants]]) it runs your own AI command-line tool straight away; without it, the request waits until an assistant connected the ordinary way picks it up.

## Tilemaps

A **Tilemap** (Toolbox → Advanced) draws a grid of tiles from one tile sheet: a floor, walls, a whole level. It's how platformers and top-down worlds are built.

1. Drag a **Tilemap** onto the canvas.
2. Set its **Texture** to a tile sheet — pick one of your images from the list, **Import…** a PNG, or click **Make a tile sheet…** for a ready-made one.
3. Set **TileWidth** and **TileHeight** to the size of one tile in the sheet. Tiles are numbered from 0, left to right, then top to bottom.
4. Set **Columns** and **Rows** for the size of the map. The control's size always follows them.
5. Double-click the tilemap (or **Advanced → Tile painter for the selection…**) and paint.

**Ready-made sheets.** **Make a tile sheet…** draws a 16 px sheet into your project, already set up with the right tile size and **Solid** tiles. Tile 0 is always empty, so a new map starts blank. Each one is an ordinary image you can open in the Pixel editor.

| Sheet | Tiles |
| --- | --- |
| **Dungeon** | Stone floor and walls, rubble, a door, a torch, moss and water |
| **Cave** | Rock, dirt and ore, with lava that isn't solid so a body falls into it |
| **Grass and earth** | Grass, earth, stone, sand, a path and water |
| **Ice and snow** | Snow, ice, frozen stone and a crack that isn't solid |
| **Brick and metal** | Brick, metal plate, grating, pipes and a hazard stripe |
| **Plain colours** | Flat colours for blocking a level out before any art exists |

**Solid** lists the tile numbers that stop a physics body, as `1,3,5-9`. The field tells you how many tiles currently count as solid; leave it empty and the map is scenery that bodies pass straight through.

### Tile painter

The map sits in the middle, zoomable, with the sheet's tiles listed on the right. Solid tiles are marked red in that list.

| Tool | What it does |
| --- | --- |
| **Paint** | Left button paints the selected tile, right button erases. |
| **Fill** | Floods the run of matching tiles under the cursor with the selected one. |
| **Erase** | Clears cells back to empty. |
| **Pick** | Takes the tile under the cursor as the selected one. |

- **Selected tile ⇄ solid** marks the selected tile solid or not.
- **Fill the map with the selected tile** and **Clear the map** do the whole grid at once.
- Under **Grid**, type new Columns and Rows and click **Resize grid**. What's already painted stays where it was.
- **Zoom** scales the view.
- **Apply** puts the map back on the canvas as one Undo step; **Cancel** leaves it as it was.

### How big a map can be

Only the tiles inside the view are drawn, so a 256 × 256 map costs the same as a 20 × 12 one, and a map can be far bigger than the screen — pair it with a **Camera**. Solid tiles aren't separate controls: each moving body is only checked against the few tiles it overlaps. A map holds up to 512 columns, 512 rows and 65,536 cells.

- **Events:** a body that lands on the map gets a `collide` event with the tilemap's ID.
- **Scripts:** `ctx.ui.getTile(id, column, row)` (−1 for an empty cell or off the grid), `ctx.ui.setTile(id, column, row, tile)`, `ctx.ui.fillTiles(id, column, row, width, height, tile)`, `ctx.ui.tileAt(id, x, y)` → `{column, row, tile}` and `ctx.ui.tileSize(id)`. See [[Scripting|Scripting]].

## State graphs

A **state graph** decides which clip a sprite plays, so idle → run → jump → land needs **no script at all**. It's what Unity calls an Animator.

1. Give a **Sprite** some clips first (its **Clips** field, or the Sprite sheet editor): for example `idle: 0; run: 1-6 @12; jump: 7,8 @8 once`.
2. Open **Advanced → State graphs…** and click **+ New graph**. Give it an **ID** and choose the **Sprite** it drives.
3. **+ State** for each state, and pick the clip it **plays**.
4. On each state, **+ Way out of** adds a transition: the state it goes **→** to, **when** (a condition), and a **priority**.
5. **Starts in** picks the state the screen opens in. **Apply**, then **Preview** (F5) to watch it run.

Conditions are the same ones used everywhere else in Arcadia Studio, over the screen's variables, plus three extras:

| Condition | True when |
| --- | --- |
| `clipDone` | The current clip has finished (a clip marked `once`) |
| `clipStep` | How many frames into the clip it is — `clipStep >= 3` |
| `input:name` | That input is held — `input:jump`, `!input:move` |

They combine like any condition: `clipDone && hp > 0`. An empty condition is always true. The **highest priority** true transition wins, and **only one transition happens per frame**, so a chain of true conditions can't skip ahead. Entering a state restarts its clip.

- **Events:** the sprite gets `state_changed` with the new state's name.
- **Scripts:** `ctx.ui.getElement('hero').state` reads the current state; `ctx.ui.setState('graph_id', 'hurt')` forces one.
- **Limits:** 64 graphs a screen, 64 states each, 16 ways out of a state.

## Shaders (web)

A **shader** is an effect run over the whole scene: a vignette, CRT scanlines, a colour drain when the player is hurt.

1. Open **Advanced → Shaders…**. Use **+ Add a stock shader ▾** for a ready-made one — **Pass through**, **Vignette**, **Scanlines (CRT)**, **Chromatic split**, **Colour drain**, **Heat wobble** or **Bloomish glow** — or **+ New shader** to write your own. **Apply**.
2. In **Screen settings**, pick it in **Shader (web)**.

Shaders are GLSL ES 3.00 fragment shaders and must start with `#version 300 es`. They receive `u_scene` (what was drawn), `u_resolution` and `u_time` (seconds), and any `uniform float u_name` you declare can be set from a script with `ui.setShaderValue('u_name', value)` — the stock ones say which in their first line. See [[Scripting|Scripting]] for an example.

A shader that won't compile is reported in the log and dropped: you lose the effect, never the screen. Shaders run in web and desktop exports wherever WebGL2 is available; Minecraft can't run them, and Validate says so.

**What a shader runs over.** Where the browser has WebGL2, tilemap tiles, particles and plain sprites and images are drawn in a fast batch — one draw call per picture instead of one per control — and the shader runs over that. Everything else still draws as before, in the same order. Without WebGL2 everything falls back to the ordinary drawing, minus the shader. The batch is also why a screen can have up to 50,000 particles alive instead of 2,000.
