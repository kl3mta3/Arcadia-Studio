# Layers and groups

The **Layers** panel lists every control on the current screen. The **top of the list is the front** of the screen: controls higher up are drawn on top.

Each row shows the control's type icon, Id and type. Controls inside a panel are indented under it with `↳`.

## Selecting in Layers

- Click a row to select that control, even if it's hidden behind others or outside its panel's visible area.
- Shift-click selects a range; Ctrl-click adds one row.
- Clicking a row inside a group selects just that member.

## Eye and lock

Each row has two buttons on the right:

- **Eye:** whether the control is shown when the screen opens (the same setting as **Visible**). Hidden rows are faded.
- **Lock:** stops the control being clicked, dragged, box-selected or nudged on the canvas. You can still select it here and edit it in Properties. The lock is only an editor aid and has no effect in Minecraft. Shortcut: Ctrl+L. Locking or unlocking a panel asks whether to do the same to everything attached inside it (tick **Don't ask me again** to keep your answer; **View → Ask again before locking items inside panels** brings the question back). You can still lock or unlock items inside one by one.

On a group row, both buttons apply to every member.

## Reordering

- **Drag rows** up or down. A blue line shows where they'll land. Selected rows move together, and dragging near the top or bottom edge scrolls the list.
- Or use the buttons at the bottom of Layers: **Bring to front**, **↑ Bring forward**, **↓ Send backward** and **Send to back** (Ctrl+Shift+], Ctrl+], Ctrl+[ and Ctrl+Shift+[). Inside a group, front and back mean the front or back of that group; the control stays in its group.
- **New group** (button at the bottom of Layers, or Edit → New empty group) makes an empty group, ready for layers to be dragged in.
- **Double-click** a control in Layers (or press **F2**, or right-click → **Rename…**) to change its ID.
- Dragging a panel's row carries everything inside it. Children always stay in front of their panel.

### Drag to put inside a panel

While dragging rows, **hold the pointer still over a panel's row for half a second**. The row lights up and the status bar says, for example, `Release to put 2 item(s) inside 'panel'`. Release to put the dragged items inside that panel. Keep moving instead and you're back to reordering. Only panels and scroll panels light up, and never one of the items you're dragging. See [[Panels and parenting|Panels-and-Parenting]].

## Groups

Groups are named folders for organizing the editor. They don't clip or change anything in Minecraft.

| Do this | How |
| --- | --- |
| Create a group | Select two or more controls, then **Group** (Ctrl+G), the Group button in Layers, or right-click → Group selected. |
| Ungroup | Select the group, then **Ungroup** (Ctrl+Shift+G). |
| Collapse / expand | Click the ▸ / ▾ on the group row. |
| Move a group in the order | Drag its header row. |
| Add a control to a group | Drag its row onto the group. |
| Nest groups | Group a selection of groups, or drag one group header onto another. Up to 32 levels. |
| Rename, duplicate, delete, move to top level | Right-click the group row. |

On the canvas, clicking a grouped control selects the **outermost** group. Copy, paste and duplicate keep the group structure.

## Isolating a group

To work inside a group without disturbing anything else:

1. **Double-click** a group's control on the canvas, double-click its Layers header, or right-click → **Isolate group** (also in the Edit menu).
2. Everything outside the group is dimmed and locked. A blue bar at the top of the canvas shows which group you're in.
3. Click, drag and box-select individual members freely. Nested groups can be isolated in turn.
4. Leave with **Exit isolation**, **Escape**, or a double-click on empty space. Switching screens also leaves isolation.

## Groups vs panels

| | Group | Panel |
| --- | --- | --- |
| Purpose | Organizing the editor | Part of the screen |
| Visible in Minecraft | No | Yes (background, border) |
| Clips its contents | No | Yes |
| Anchors follow it | No | Yes |
| Hiding hides its contents | – | Yes |

You can use both: a panel can be in a group, and a group's members can be inside panels.
