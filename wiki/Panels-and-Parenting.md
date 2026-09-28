# Panels and parenting

A **Panel** or **Scroll Panel** can contain other controls. A control inside a panel (its *child*):

- moves when the panel moves, and is copied or deleted with it
- follows the panel's edges through its [[anchors|Anchors-and-Responsive-Layouts]], not the screen's
- is **clipped** to the panel: anything outside the panel's area isn't drawn
- is hidden or disabled when the panel is
- is always drawn in front of the panel

Only panels and scroll panels can be parents. Panels can be nested up to 32 levels deep.

## Putting controls inside a panel

There are three ways:

1. **Attach to panel.** Select the panel **together with** the controls, right-click, and choose **Attach to 'panel'** (Ctrl+Shift+A). If several panels are selected, right-click the one to attach to.
2. **Drag in Layers.** Drag the controls' rows over the panel's row and hold still for half a second until it lights up, then release. See [[Layers and groups|Layers-and-Groups#drag-to-put-inside-a-panel]].
3. **Parent panel dropdown.** Select the control and pick the panel under **Layout → Parent panel** in Properties. The list only offers panels the control can legally go inside.

Attaching moves the controls just in front of the panel in the layer order and into the panel's layer group. It's one Undo step. If any of them stick out past the panel's edges, the Output panel lists them, because those parts will be cut off.

Nothing moves on screen when you attach or detach: positions stay as they are.

## Taking controls out

- **Detach from panel** (right-click, or Ctrl+Shift+D) moves the control out one level, into its panel's parent or onto the screen.
- Or choose **(none – screen)** in the Parent panel dropdown.

## Rules

- A panel can't go inside itself or inside one of its own children. Those choices never appear.
- Controls keep absolute screen coordinates, so X and Y are always measured from the screen's top left, even inside panels.

## Scroll panels

Put controls inside a Scroll Panel the same way. In Minecraft, anything below its bottom edge can be scrolled into view with the mouse wheel. With nested scroll panels, the wheel scrolls the innermost one under the pointer first, then the outer one once the inner one reaches its end.

## In Layers

Children are listed indented under their panel with `↳`. If a child is in a different layer group from its panel, it's listed in its own group and marked `(in panel_id)`.
