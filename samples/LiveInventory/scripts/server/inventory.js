// Runs in Wysicraft's bundled SERVER engine. Data comes from the clicking player.
// This example never grants, removes, or transfers items.
function refresh(ctx) {
    const rows = ctx.player.getInventory();
    ctx.ui.setItems('inventory', rows);
    ctx.state.set('inventory_snapshot', JSON.stringify(rows));
    ctx.ui.setText('summary', rows.length + (rows.length === 1 ? ' occupied slot / ' : ' occupied slots / ') + ctx.player.getName());
}
function selected(ctx) {
    const rows = JSON.parse(ctx.state.get('inventory_snapshot') || '[]');
    const row = rows[Number(ctx.value)];
    if (row) ctx.message(row.name + ' x' + row.count + ' (display only)');
}
