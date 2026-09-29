# Editing on the canvas

## Selecting

| To select… | Do this |
| --- | --- |
| One control | Click it. |
| Several | Shift-click or Ctrl-click each one. |
| Everything | **Ctrl+A** (Edit → Select all). Locked controls are skipped; while a group is isolated, only that group's members are selected. |
| Everything in an area | Drag a box starting on empty space. Any control the box **touches**, even partly, is selected. Hold Shift or Ctrl to add to the current selection. |
| Across the whole screen | Start the box on the **dark area around the screen** and drag across. |
| A control hidden behind others | Click its row in [[Layers|Layers-and-Groups]]. |
| One member of a group | Alt-click it, or isolate the group (double-click). See [[Layers and groups|Layers-and-Groups]]. |

Clicking a grouped control selects the whole group. Click empty space, or press Escape while isolated, to clear things.

## Moving

- **Drag** a selected control. Everything selected moves together, and so do the contents of any selected panel.
- **Arrow keys** nudge the selection 1 GUI pixel; **Shift+arrow** nudges 10. A run of nudges on the same selection is a single Undo step.
- **Snap to grid** (View menu) rounds positions to the grid size from Project settings. The grid itself can be shown or hidden with **Show grid**.
- For exact positions, type X and Y in Properties.

## Resizing

A selected control shows **eight handles**, on the corners and edge midpoints.

- Drag a **right or bottom** handle to grow or shrink from that side.
- Drag a **left or top** handle to move that edge; the opposite edge stays in place.
- When you resize a **panel**, the controls inside it move and stretch according to their anchors, exactly as they will in Minecraft. Hold **Ctrl** while dragging to resize the panel alone. See [[Anchors and responsive layouts|Anchors-and-Responsive-Layouts]].
- Or type Width and Height in Properties.

## Copy, paste, duplicate, delete

| Action | Shortcut |
| --- | --- |
| Copy | Ctrl+C |
| Paste | Ctrl+V |
| Cut | Ctrl+X |
| Duplicate | Ctrl+D (or the Duplicate button in Layers) |
| Delete | Delete |

New controls and copies get simple numbered IDs: `button1`, `button2`, `label1`, and a copy of `play` becomes `play1`. Links between the copied controls (for example a button's action that targets a copied label) are updated to point at the copies. Copying a panel includes everything inside it, and groups are preserved.

## Undo and redo

**Ctrl+Z** and **Ctrl+Y**. Arcadia Studio keeps the last 200 steps. A drag, a resize, a batch of nudges, or a whole command such as Attach or Arrange counts as one step. Text fields have their own undo while you're typing in them.

## Zoom

| Zoom | How |
| --- | --- |
| Toward the pointer | Ctrl + mouse wheel |
| In / out | Ctrl + plus / Ctrl + minus |
| Actual size | Ctrl+0 |
| Fit the whole screen in the window | Ctrl+9 |

Zoom changes only the view, never the project. The toolbar and status bar show the current zoom.

## Lock

Lock a control you don't want to bump by accident, such as a background panel:

- Click the **lock** in its Layers row, press **Ctrl+L**, or right-click → **Lock**.
- A locked control stays visible but can't be clicked, dragged, box-selected or nudged on the canvas. Clicks pass through to whatever is underneath.
- You can still select it in Layers and edit it in Properties. If its unlocked parent panel moves, it moves along.
- Unlock it the same way. The lock only affects the editor; it has no effect in Minecraft.

## Right-click menu

Right-clicking a control (on the canvas or in Layers) offers: Isolate group, Select, Group selected, Ungroup, Duplicate, Rename…, Hide/Show, Lock/Unlock, Delete, then **Arrange** (Bring to front, Bring forward, Send backward, Send to back), **Align** (see [[Align and distribute|Align-and-Distribute]]), **Attach to panel** and **Detach from panel**. Images and sprites also have **Edit in pixel editor…** (or **Draw in pixel editor…** when they have no picture yet), and sprites **Sprite sheet editor…**. Items show their shortcuts.
