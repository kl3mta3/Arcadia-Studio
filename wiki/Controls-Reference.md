# Controls reference

Drag a control from the **Toolbox** onto the canvas, or double-click it to add it at the top left. Every control has an **Id** (lowercase, unique on the screen), a position and size in GUI pixels, and the shared appearance and behavior settings described in [[Properties and appearance|Properties-and-Appearance]].

| Control | What it is | Main properties | Events |
| --- | --- | --- | --- |
| **Button** | A clickable button. | Text, colors or a PNG skin, corner radius | `click` |
| **Label** | Text. Can show variables with `${name}`. | Text, font, alignment, optional background fill | – |
| **Image** | A PNG from your project or a Minecraft resource. Transparent parts of the PNG stay see-through; new Image controls have **Fill enabled** off, turn it on only if you want a colored box behind the picture. | Texture | – |
| **Text Box** | A single-line text field. | Value (the starting text) | `text_changed`, `submit` (Enter) |
| **Checkbox** | An on/off box. | Value (`true`/`false`), Text | `checked`, `unchecked` |
| **Slider** | Drag to pick a number. | Value, Minimum, Maximum | `value_changed` |
| **Progress Bar** | A bar showing a value in a range. | Value, Minimum, Maximum, Foreground (bar color) | – |
| **Dropdown** | Pick one option from a list. In Minecraft, clicking cycles through the options. | Options (separated by `\|`), Value (zero-based index) | `value_changed` |
| **Panel** | A rectangle that can hold other controls. Use it for backgrounds and sections. | Background, border, radius, PNG skin | – |
| **Scroll Panel** | A panel whose contents scroll when they're taller than it. | Same as Panel | – |
| **Item Icon** | A Minecraft item's icon, including modded items. | Item (`namespace:item_id`) | – |
| **Sprite** | A sprite sheet: frames of the same size in one image, played as named clips. Double-click it for the [[Sprite sheet editor|Pixel-Art-and-Sprites]]. | Texture, FrameWidth, FrameHeight, Clips (`idle: 0; run: 1-6 @12; jump: 7,8,9 @8 once`), Playing | – |
| **Shape** | Rectangle, circle/oval, triangle, diamond, hexagon or star, filled with a colour or texture. Stretch it for ovals and rectangles. Shape stamps are in the Toolbox under **Shapes**. | Shape, fill, border | `click` |
| **Sound** | Plays a sound. It has no picture: it only shows in Layers. **Autoplay** starts it **Delay** ms after the screen opens (0 = straight away); otherwise set its value to `play` or `stop`. **Volume** 0–1, **Repeat** (play × times) and **Loop**. | Sound, Autoplay, Delay, Volume, Repeat, Loop | – |
| **Tilemap** (web & desktop) | A grid of tiles from one tile sheet — a floor, walls, a level. Double-click it for the Tile painter. See [[Advanced tools|Advanced-Tools]]. | Texture (the sheet), TileWidth/TileHeight, Columns/Rows, Solid | `collide` (on the body that hits it) |
| **Collider** (web & desktop) | An invisible physics wall or area. See [[Advanced tools|Advanced-Tools]]. | Collider, points, trigger | `collide`, `collide_stay`, `collide_end`, `trigger_enter`, `trigger_stay`, `trigger_exit` |
| **Item List** | A scrollable list of items with optional row buttons. | Value (JSON rows), RowHeight, PrimaryLabel, SecondaryLabel, ShowItemId, Reusable row template | `item_click`, `item_primary`, `item_secondary` |
| **Item Slots** (Minecraft) | Real inventory slots: players drag, shift-click and split stacks as in any Minecraft inventory, and the server moves the items. Stamps are in the Toolbox under **Item slots · Minecraft**. | Holds (player inventory, storage, crafting grid, crafting result), Columns, Rows, First slot | – |
| **Texture Region** | Part of a larger image (sprite-sheet style). | Texture, TextureX/Y (where the region starts), TextureWidth/Height (full image size) | – |

Every control also has three hover events: `hover`, `mouse_enter` and `mouse_leave`. They run client actions only, at most four times a second.

## Notes on specific controls

**Button and Panel skins:** choose a PNG in Appearance to replace the solid color. Transparency in the PNG is kept, and **Radius** rounds the corners. See [[Assets and items|Assets-and-Items]].

**Label backgrounds:** labels can have a fill that covers their whole box, or no fill for plain text on whatever is behind.

**Text Box** in Minecraft supports typing at the end of the text, Backspace, paste and Enter to submit. It isn't a full desktop text editor.

**Scroll Panel:** set other controls' **Parent panel** to the scroll panel (see [[Panels and parenting|Panels-and-Parenting]]). Contents below the bottom edge scroll into view with the mouse wheel. Scroll panels can be nested; the wheel scrolls the innermost one first.

**Item Icon** shows an item exactly as it looks in an inventory: flat items as their sprite, and blocks as a small 3D cube (the editor draws full blocks like bricks or logs as that cube too; other blocks show their texture as a preview). The icon is scaled to fit the control. Use it for inventories, shops, rewards and recipes, **not as a background or wall texture**: for a brick wall, add an **Image** (or a Panel skin) with a Minecraft texture such as `minecraft:textures/block/bricks.png`. See [[Assets and items|Assets-and-Items]] for the item picker.

**Item Slots** (Minecraft only) turn the screen into a real inventory screen, so items can be dragged, shift-clicked, split and crafted exactly as in the game. The server moves every item, so nothing can be duplicated from the client.

- **Crafting table** (Toolbox stamp): a 3 × 3 crafting grid, its result, and the player's inventory and hotbar below, laid out like Minecraft's crafting table. Recipes are the game's own, including modded ones.
- **Holds** decides what the slots are. **Player inventory** shows part of the player's own inventory from **First slot** (0–8 is the hotbar, 9–35 the rest; the usual layout is 9 × 3 from 9 and a 9 × 1 hotbar from 0). **Storage** is temporary. **Crafting grid** is up to 3 × 3, and **Crafting result** is its output slot.
- When the screen closes, anything left in storage or the crafting grid goes back to the player (or drops at their feet if their inventory is full).
- Each slot is 18 GUI pixels; the control sizes itself from Columns × Rows. A screen has at most one crafting grid and one result. Slot screens use a fixed layout, not Responsive layout, and slots can't go inside a scroll panel.
- Hiding or disabling a slots control (visibility rules) hides or locks its slots too.
- Web and desktop apps leave item slots out.

**Item List** has its own page: [[Item lists and row templates|Item-Lists-and-Row-Templates]].

## Values and binding

- `${name}` in Text, Tooltip and similar fields is replaced with the screen variable `name`. For example, a label with text `Altitude: ${altitude}` shows the current `altitude` value.
- A Checkbox's Value is `true` or `false`. A Slider's and Progress Bar's are numbers. A Dropdown's is the selected option's index, starting at 0.
- Scripts and actions change values at runtime; see [[Events and actions|Events-and-Actions]].
