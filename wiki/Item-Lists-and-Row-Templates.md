# Item lists and row templates

An **Item List** shows a scrollable list of items, for example a shop, an inventory view or a reward catalog. Each row can have up to two buttons.

## Filling a list

A list's **Value** is JSON: an array of rows with `item`, `count` and `name`.

```json
[{"item":"minecraft:diamond","count":3,"name":"Diamond"},
 {"item":"minecraft:apple","count":12,"name":"Apple"}]
```

Usually a script fills it:

```javascript
function refresh(ctx) {
    ctx.ui.setItems('catalog', [{item:'minecraft:diamond', count:3, name:'Diamond'}]);
}
```

In a server script, `ctx.player.getInventory()` returns the player's inventory in this format, and the built-in server action `player_inventory` fills a list with it. Lists hold up to **128 rows** and **4096 characters** of JSON; page through bigger data sets on the server.

## Standard rows

Without a template, each row shows the item icon, name, count and optionally the item ID.

| Property | Meaning |
| --- | --- |
| **RowHeight** | 24–128 GUI pixels. |
| **PrimaryLabel / SecondaryLabel** | Text for the two row buttons. Leave one blank to hide that button. |
| **ShowItemId** | Show the `namespace:id` under the name. |

## Row events

| Event | When |
| --- | --- |
| `item_click` | A row is clicked. |
| `item_primary` | A row's primary button is clicked. |
| `item_secondary` | A row's secondary button is clicked. |

The event's value (`ctx.value` in scripts) is the **row index**, starting at 0.

> **Security:** on the server, look the index up in **your own** data. Never trust an item ID or amount sent from the client. The server already checks that the index is inside the list it sent.

## Custom row templates

To design your own row layout:

1. Select the Item List. In Properties, find **Reusable row template**.
2. Click **New row template**, or pick an existing template screen and click **Edit template**.
3. Design the row on the normal canvas. Make it the list's width (leave room for the scrollbar) and **RowHeight** tall. Positions are relative to the row's top left.
4. Use panels, labels, images, item icons, progress bars and buttons. Panels can nest.
5. Bind values with `${row.item}`, `${row.name}`, `${row.count}` and `${row.index}` in Text, Item, Value or Tooltip.
6. For each button, choose **Row button** in Properties: `item_primary`, `item_secondary` or `item_click`. Put the actions or scripts on that event of the **Item List** itself, not on the template button.

Several lists can share one template. Anchors work in templates: the template's width is the design width, and rows adapt to the real list width. New templates stretch the background and name, and anchor the action button right.

Templates are for display and row buttons. They can't contain text boxes, lists inside rows, or their own scripts; use normal panels for interactive forms. Up to 64 controls per template. Clear the template choice to go back to standard rows.

Exports include the current template layout, so players need nothing extra. Hiding or disabling a row button (or its panel) also blocks its action on the server.
