# Anchors and responsive layouts

Minecraft windows come in all sizes, and players change **GUI Scale**. Anchors decide how your controls react when the space around them changes size.

## The key idea

**An anchor doesn't move a control to an edge. It keeps the distance you drew between the control and that edge.**

If you draw a close button 10 px from a panel's right edge and anchor it **right**, it stays 10 px from the right edge however wide the panel gets. To pin something into a corner, place it in the corner first, then anchor it.

## Anchor options

Set these under **Layout** in Properties.

| Horizontal | Vertical | Behavior |
| --- | --- | --- |
| **left** (default) | **top** (default) | Keeps its distance from the left / top edge. |
| **right** | **bottom** | Keeps its distance from the right / bottom edge. |
| **center** | **center** | Keeps its offset from the center. |
| **stretch** | **stretch** | Keeps both margins and changes size. **Minimum width / height** stop it shrinking too far. |

Anchors are measured from the control's **parent panel**, or from the screen for controls that aren't inside a panel. See [[Panels and parenting|Panels-and-Parenting]].

## Seeing anchors in the editor

- **Orange guides:** with one control selected, solid orange lines run from it to each edge it's pinned to, and a dashed line marks the center line for **center**.
- **Live resizing:** drag a panel's resize handles and its children move and stretch by their anchors as you drag. Hold **Ctrl** to resize the panel alone.
- **Typing sizes:** changing a panel's Width or Height in Properties, or the screen's Width or Height in **Screen settings**, reflows the controls the same way. Each resize is one Undo step.

The editor uses the same layout rules as Minecraft, so what you see while resizing is what players get.

## Making a screen responsive

1. Click **Screen settings** and tick **Responsive layout**.
2. The screen's Width and Height become its **design size**. In Minecraft, the screen is laid out at the available GUI size (minus a small margin for the frame), and every control moves by its anchor.
3. Try it without launching the game: in [[Preview|Preview]], type a width and height under **Layout size (GUI pixels)** and click **Apply size**.

Without Responsive layout, a screen keeps its fixed size. **Fit to viewport** then scales it down if the window is too small.

## A good starting recipe

| Control | Anchors |
| --- | --- |
| Background panel | stretch / stretch |
| Title label | stretch / top |
| Close button | right / top |
| Main list or content area | stretch / stretch |
| Footer buttons | left or right / bottom |

## Limits

- Anchors don't wrap controls onto new rows and don't switch layouts at breakpoints.
- Minimum sizes can make content overflow on very small windows.
- Item List row templates support anchors too: the template's width is the design width, and rows adapt to the list's real width. See [[Item lists and row templates|Item-Lists-and-Row-Templates]].
- Existing fixed screens are unchanged until you turn on Responsive layout.
