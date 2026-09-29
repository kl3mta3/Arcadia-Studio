# Properties and appearance

Select a control to edit it in **Properties**. Changes apply as you type valid values; an invalid value turns the field's border red and isn't applied. With several controls selected, Properties shows the first one.

Every section (Identity, Layout, Appearance and so on) folds: click its heading to close or open it. Arcadia Studio remembers which ones you closed, so a long panel stays the way you left it. **Ask Agent** stays at the top whatever is folded.

## Identity

| Field | Meaning |
| --- | --- |
| **Id** | Unique on the screen: lowercase letters, numbers and underscores, starting with a letter. Actions, scripts and conditions refer to controls by Id. New controls get numbered IDs like `button1`. Rename with F2, a double-click in Layers, or here: panels' children and every action that targets the control are updated for you; scripts are not, and the Output panel lists any script that still uses the old ID. |
| **Name** | Optional human-friendly label, shown as the Layers tooltip. |

## Layout

| Field | Meaning |
| --- | --- |
| **X**, **Y**, **Width**, **Height** | Position and size in Minecraft GUI pixels, measured from the screen's top left. Changing X or Y of a panel moves its contents too. |
| **Parent panel** | The panel or scroll panel this control sits inside. See [[Panels and parenting|Panels-and-Parenting]]. |
| **Horizontal / Vertical anchor** | How the control reacts when its parent or the screen changes size. Orange guide lines on the canvas show it. See [[Anchors and responsive layouts|Anchors-and-Responsive-Layouts]]. |
| **Minimum width / height** | Stretched controls never shrink below this. |

## Appearance

**Colors.** Click a color swatch to open the color wheel, with brightness and opacity sliders, or type `#RRGGBB` or `#AARRGGBB` (with alpha). The preview updates as you type.

| Setting | Meaning |
| --- | --- |
| **Text color** | Color of the text (for a Progress Bar, the bar). |
| **Fill enabled / Fill color** | The control's background. Turn fill off for text with no box behind it. |
| **Border color / Width (px)** | Outline, 0–32 px. 0 hides it. |
| **Corners → Radius** | Rounds the fill, border and skin, up to half the control's smaller side. |
| **Opacity** | 0 (invisible) to 1 (solid). |
| **Color swatches** | One-click fill colors under Fill color (they also remove any skin). |
| **Skin image → Choose PNG…** | Uses an image instead of the fill color. Works on buttons, panels, labels and other boxed controls; choosing one turns **Fill enabled** on, because the skin is drawn as the fill. **Use color only** removes it. |
| **Texture** (Image, Texture Region) | Which image to show. |

**Text.**

| Setting | Meaning |
| --- | --- |
| **Typeface** | Pick from the list. Minecraft has three: `minecraft:default` (the default), `minecraft:uniform` and `minecraft:alt`. Web and desktop apps can also use a built-in family or your own font — see **Typefaces** below. |
| **Size scale** | 1 is normal, up to 8. |
| **Bold, Italic, Underline** | Styles. |
| **Alignment** | Left, center or right: how the text sits inside the control. |
| **Text shadow** | On/off, with **Shadow color**, opacity, **Offset X / Y (px)** and **Blur (px)**. Minecraft approximates blur, so soft shadows look slightly different in-game. |

The editor approximates Minecraft's font rendering; exact glyph shapes are only seen in the game.

### Images

- PNG images up to **8192 × 8192** pixels and **32 MiB** each (256 MiB per project).
- Choosing a skin imports the image into your project automatically. Manage all images in the [[Assets panel|Assets-and-Items]].
- Resource-pack images work too: set Texture to an ID such as `minecraft:textures/gui/...png`.
- Resource packs can override your project's images, because Minecraft checks resource packs first.

### Typefaces (web & desktop)

Minecraft draws its own fonts, so a Minecraft screen can only use its three. Web pages and desktop apps can use more:

| Built-in family | Looks like |
| --- | --- |
| `web:sans` | The system's interface font (Segoe UI on Windows) |
| `web:serif` | Georgia or Times |
| `web:mono` | Cascadia Mono or Consolas |
| `web:rounded` | A rounded sans such as Nunito or Trebuchet |
| `web:condensed` | Arial Narrow or Roboto Condensed |
| `web:display` | Impact or Arial Black |
| `web:handwriting` | Segoe Script or Comic Sans |

These need no import: they use fonts every computer already has.

For anything else, click **Import a font…** under Typeface and choose a `.ttf`, `.otf`, `.woff2` or `.woff` file (up to 8 MiB). It's copied into the project, travels inside every web and desktop export, and the text is drawn in the real font. Make sure its licence lets you ship it.

A misspelled font name is a **validation error** rather than a silent fallback, and **Validate** lists every non-Minecraft font as something Minecraft can't run.

## Behavior

| Field | Meaning |
| --- | --- |
| **Visible** | Whether the control is shown when the screen opens. (Same as the eye in Layers.) Hidden controls appear faded in the editor. |
| **Enabled** | Whether it responds to clicks and input. |
| **Tooltip** | Text shown on hover. Supports `${variable}`. |
| **VisibleIf / EnabledIf** | Conditions that show or enable the control based on variables. Empty means always. |

Visibility and enabled state apply to everything inside a panel: hiding a panel hides its contents.

### Conditions

Conditions compare screen variables:

```text
fuel > 0
mode == 'flying' AND NOT locked
(level >= 5 OR vip) && !banned
```

- Values are variable names, numbers, `true`/`false`, or quoted strings (`'text'` or `"text"`).
- Comparisons: `==`, `!=`, `>`, `<`, `>=`, `<=`. Numbers compare as numbers.
- Combine with `AND`/`&&`, `OR`/`||`, `NOT`/`!` and parentheses.
- A bare variable is true when it's `true` or a non-zero number.
- No arithmetic, function calls or JavaScript. Up to 1024 characters.

The server checks conditions too: a hidden or disabled button can't be triggered by a modified client.

## Screen settings

With nothing selected, Properties shows the screen's settings (ID, title, size, Main screen, variables, display options). See [[Projects and screens|Projects-and-Screens#screen-settings]].
