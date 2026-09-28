# Align and distribute

Select the controls, then use **Edit → Align** or **right-click → Align**. (Drawing order, such as Bring to front, is under **Arrange**.)

| Command | Does |
| --- | --- |
| **Align left / right / top / bottom edges** | Lines the selection up along one edge of the box around all selected controls. |
| **Align horizontal / vertical centers** | Lines up their centers. |
| **Equal horizontal / vertical gaps** | Spaces three or more controls evenly between the two outermost ones. The outer ones don't move; different sizes are accounted for. If there isn't room, spread the outer two further apart first. |
| **Center selection horizontally / vertically on screen** | Moves the whole selection to the screen's center line without changing its internal spacing. Works with one control too. |

Each command is one Undo step. Nothing is resized or reordered, and exact positions ignore grid snapping.

## Keep layer groups together

This option at the bottom of the Align menu is on by default. A fully selected group counts as **one object** and keeps its internal layout. Turn it off to align the group's members individually. You can also isolate the group and select its members.

A panel's children always move with it, whether or not they're selected.

## Why is a command greyed out?

Hover over it: the tooltip explains. Usually there aren't enough independent objects. For example, one selected group (with Keep layer groups together on) is a single object, so there's nothing to align it with. Aligning needs two objects and equal gaps needs three.

## Align vs anchors

Aligning moves controls **once**. Anchors decide how they move **later**, when a panel or the window changes size. Use Align to lay things out neatly, then anchors to keep them that way. See [[Anchors and responsive layouts|Anchors-and-Responsive-Layouts]].
