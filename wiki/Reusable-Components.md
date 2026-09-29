# Reusable components

A **component** is a layout you design once and place many times, such as a header, toolbar or card. Each placed copy (an *instance*) stays linked to its source, so you can update every copy at once.

Open the **Components** tab (top left, or **View → Panels → Components**).

## Built-in starters

Pick a starter and click **Add to screen**. Arcadia Studio saves an editable source copy and places an instance, in one Undo step.

| Starter | Contains |
| --- | --- |
| **Window header** | Title, subtitle and a working close (X) button |
| **Tab bar** | Three tabs (Overview, Items, Settings) that switch their own active indicator |
| **Item card** | Item icon, name, quantity and an **Add item** button |
| **Inventory toolbar** | Category buttons (All, Gear, Food), Refresh and a search box |
| **Confirmation dialog** | Title, message, Cancel (hides the dialog) and Confirm |
| **Player status** | Player name with health and energy bars (sample values) |
| **Pagination bar** | Previous / Next with a "Page 1 of 3" label |
| **Notification banner** | Icon, title, message and a dismiss (X) button |

They use a dark palette with green accents, normal editable controls and no external images. Buttons that need project-specific logic (search, grants, paging, confirm) show a harmless placeholder response; their tooltips say what to connect.

## Making your own

1. Select the controls to reuse. Selecting a panel includes everything inside it.
2. In Components, click **Save selection** and enter an ID such as `component_toolbar`. Your original controls are left as they are.
3. On any screen, select the source in the list and click **Place instance** (or double-click it). Each instance gets unique IDs and a transparent parent panel; move that panel to move the instance.

## Editing and updating

- **Edit source** opens the source layout in the normal editor. A green banner shows you're editing a component; **Back to screen** returns. Sources don't appear in the screen dropdown.
- When you're happy, click **Update all instances**. Updates are deliberate, so half-finished edits never reach your screens.

Select any control in an instance to see **Component** options in Properties:

| Button | Does |
| --- | --- |
| **Update** | Takes the source's latest changes, keeping your local changes to this instance. |
| **Reset overrides** | Restores the source's properties for this instance (its position stays). |
| **Detach** | Turns the instance into ordinary controls, no longer linked. |
| **Edit source** | Jumps to the source. |

Arcadia Studio notices local changes you made to an instance (text, colors, sizes, events) and keeps them when updating. Controls added to the source appear in instances; controls removed from the source are removed from instances.

## Good to know

- Built-in actions that target controls inside the component are remapped to each instance's IDs. **JavaScript isn't rewritten**: literal IDs inside scripts stay the same, so use the placed IDs in scripts, or use built-in actions for internal links.
- Screen variables keep their names and are shared by all instances on the same screen.
- Component sources can't be the Main screen, a navigation destination or an Item List row template, and can't contain other linked components yet.
- Renaming controls inside an instance requires detaching it first.
- Ordinary copy/duplicate makes independent controls; use **Place instance** for another linked copy.
- Exports contain the placed controls only. Sources and link data stay in your project file, and players need nothing extra. **Update instances before exporting** to include the latest source changes.
- Web and desktop exports also carry each component itself, so a script can [[spawn it while the game runs|Scripting]] (`ctx.ui.spawn('badge', x, y)`). A spawned copy comes from the component as it is, without any placed instance's overrides. Minecraft packs leave components out.
