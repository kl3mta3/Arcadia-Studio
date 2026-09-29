# Assets and Minecraft items

Two panels in the top-left tab group handle pictures: **Assets** for your own images and **Items** for Minecraft item icons. Both are also under **View → Panels**.

## Assets (your images)

**Assets** shows thumbnails and resource paths for every PNG in your project, with a search box.

| Button | Does |
| --- | --- |
| **Import PNGs** | Adds one or more PNG files. A clashing filename gets a suffix; nothing is overwritten. You can also **drag PNG files from Windows Explorer** onto the Assets panel, or straight onto the canvas to import them and place them as Image controls. (Also **Project → Import texture…**.) |
| **New pixel art** / **Edit pixels** | Draws a new picture, or edits the selected image, in the [[pixel editor|Pixel-Art-and-Sprites]]. |
| **Assign selected** | Uses the selected image as the skin or texture of the selected controls. |
| **Add image** | Creates an Image control showing it. |
| **Replace** | Swaps in another file but keeps the name, so everything using it updates. Works for images (a PNG) and sounds (any sound format; a different one is fine, as sounds play by name). A sound made in the Music maker or Sound effect maker loses those settings, since they described the old sound. |
| **Rename** | Right-click → **Rename…**, press **F2**, or click the name of the selected asset once more and wait a moment. Only the name changes, never the extension. Controls, change-texture and play-sound actions, Sound controls and particle effects that use it follow, and so do its animation, pixel-editor layers and maker settings. Scripts aren't rewritten (they can build names as they run); the Output panel lists any that mention the old name. Names use lowercase letters, numbers, `_` and `-`. |
| **Find uses** | Lists controls, events and scripts that mention the image. |
| **Delete** | Removes the image. Refused while it's still known to be used. |

You can also **drag an image** onto the canvas to create an Image control there. All of these can be undone.

Scripts that build image paths from pieces can't be detected by **Find uses**. Check your scripts before deleting an image that looks unused.

**Where images live:** `assets/<project_id>/textures/gui/<control_type>/<name>.png`, referenced as `<project_id>:textures/gui/<control_type>/<name>.png`. Arcadia Studio manages these paths for you, and they update automatically if you change the project Id. Minecraft resource packs can override them.

**Limits:** PNG only, up to 8192 × 8192 pixels and 32 MiB each, 256 MiB per project. The editor may show a smaller preview of large images; exports keep the originals.

### Sounds

**Import sounds** (or drag audio files onto Assets) adds `.ogg`, `.mp3`, `.wav` and `.m4a`/`.aac` files. Each gets an ID such as `myproject:click`, which **play_sound** actions and **Sound** controls use. **Minecraft only plays `.ogg`**: other formats work in web and desktop apps, and a Minecraft export lists where they're used. Project `.ogg` sounds go into the Minecraft JAR with a `sounds.json` automatically.

An animation file (`name.png.mcmeta`) dropped on Assets by itself joins the project image with the same name.

### Animated textures

Arcadia Studio plays animated textures the way Minecraft does: the frames are stacked in one PNG, and a `.png.mcmeta` file beside it sets the timing (for example `{"animation":{"frametime":2}}`; frame times are game ticks of 50 ms, and `frames` can list an order and per-frame `time`).

- **Minecraft's own** animated textures work straight away: set an Image (or a skin) to `minecraft:textures/block/prismarine.png`, `minecraft:textures/block/sea_lantern.png`, `minecraft:textures/block/magma.png` and so on.
- **Made in the pixel editor:** tick **Plays on its own** before saving. See [[Pixel art and sprites|Pixel-Art-and-Sprites]].
- **Your own:** keep `name.png.mcmeta` in the same folder as `name.png`, then import the PNG (Import PNGs or drag and drop). The animation file comes along automatically, and Assets marks the image **animated**.

Images and skins show one frame at a time, animated on the canvas, in Preview and in the game. Frame blending (`interpolate`) isn't supported; frames switch.

## Items (Minecraft item icons)

**Items** searches Minecraft items by name or ID (`namespace:item_id`).

- **Add item** creates an Item Icon control, or drag an item onto the canvas.
- **Assign selected** changes the item of the selected Item Icon controls.
- Item Icon controls also have a **Browse Minecraft items** button in Properties.
- You can always type an ID straight into the control's **Item** field.

### Where the list comes from

- Arcadia Studio reads your installed **Minecraft 1.21.1 client** automatically when it can find it (the standard launcher, or Arcadia Studio's own test instance).
- **Load Minecraft / mod JAR** adds items from another client or mod JAR.
- For the exact list of registered items, **including modded items**, start a [[Minecraft test|Testing-in-Minecraft]] once, then click **Read test items**. It also reads textures from the test instance's mods folder.

Minecraft's files stay on your computer; they're never packed into your project or exports.

### About the thumbnails

Thumbnails are texture previews, not 3D renders. Blocks show one face, animated textures show their first frame, and some custom-rendered items have no thumbnail. In Minecraft, the real model, tint and animation are drawn. An item with no thumbnail still works if its mod is installed.
