const POOLS = {"food":[{"name":"Apple","id":"minecraft:apple"},{"name":"Bread","id":"minecraft:bread"},{"name":"Carrot","id":"minecraft:carrot"},{"name":"Baked potato","id":"minecraft:baked_potato"},{"name":"Steak","id":"minecraft:cooked_beef"},{"name":"Chicken","id":"minecraft:cooked_chicken"},{"name":"Porkchop","id":"minecraft:cooked_porkchop"},{"name":"Salmon","id":"minecraft:cooked_salmon"},{"name":"Cookie","id":"minecraft:cookie"},{"name":"Pumpkin pie","id":"minecraft:pumpkin_pie"},{"name":"Melon slice","id":"minecraft:melon_slice"},{"name":"Sweet berries","id":"minecraft:sweet_berries"},{"name":"Golden apple","id":"minecraft:golden_apple"},{"name":"Golden carrot","id":"minecraft:golden_carrot"},{"name":"Honey bottle","id":"minecraft:honey_bottle"},{"name":"Beetroot","id":"minecraft:beetroot"}],"gear":[{"name":"Iron sword","id":"minecraft:iron_sword"},{"name":"Diamond sword","id":"minecraft:diamond_sword"},{"name":"Bow","id":"minecraft:bow"},{"name":"Crossbow","id":"minecraft:crossbow"},{"name":"Iron pickaxe","id":"minecraft:iron_pickaxe"},{"name":"Diamond pick","id":"minecraft:diamond_pickaxe"},{"name":"Iron axe","id":"minecraft:iron_axe"},{"name":"Diamond axe","id":"minecraft:diamond_axe"},{"name":"Iron helmet","id":"minecraft:iron_helmet"},{"name":"Diamond helm","id":"minecraft:diamond_helmet"},{"name":"Iron armor","id":"minecraft:iron_chestplate"},{"name":"Diamond armor","id":"minecraft:diamond_chestplate"},{"name":"Iron boots","id":"minecraft:iron_boots"},{"name":"Diamond boots","id":"minecraft:diamond_boots"},{"name":"Shield","id":"minecraft:shield"},{"name":"Fishing rod","id":"minecraft:fishing_rod"}],"blocks":[{"name":"Oak planks","id":"minecraft:oak_planks"},{"name":"Spruce planks","id":"minecraft:spruce_planks"},{"name":"Birch planks","id":"minecraft:birch_planks"},{"name":"Cherry planks","id":"minecraft:cherry_planks"},{"name":"Stone","id":"minecraft:stone"},{"name":"Cobblestone","id":"minecraft:cobblestone"},{"name":"Stone bricks","id":"minecraft:stone_bricks"},{"name":"Deepslate","id":"minecraft:deepslate"},{"name":"Glass","id":"minecraft:glass"},{"name":"Bricks","id":"minecraft:bricks"},{"name":"Quartz block","id":"minecraft:quartz_block"},{"name":"Terracotta","id":"minecraft:terracotta"},{"name":"Moss block","id":"minecraft:moss_block"},{"name":"Grass block","id":"minecraft:grass_block"},{"name":"Sand","id":"minecraft:sand"},{"name":"Obsidian","id":"minecraft:obsidian"}],"loot":[{"name":"Diamond","id":"minecraft:diamond"},{"name":"Emerald","id":"minecraft:emerald"},{"name":"Amethyst","id":"minecraft:amethyst_shard"},{"name":"Lapis lazuli","id":"minecraft:lapis_lazuli"},{"name":"Iron ingot","id":"minecraft:iron_ingot"},{"name":"Gold ingot","id":"minecraft:gold_ingot"},{"name":"Copper ingot","id":"minecraft:copper_ingot"},{"name":"Netherite scrap","id":"minecraft:netherite_scrap"},{"name":"Ender pearl","id":"minecraft:ender_pearl"},{"name":"Blaze rod","id":"minecraft:blaze_rod"},{"name":"Prismarine","id":"minecraft:prismarine_shard"},{"name":"Slimeball","id":"minecraft:slime_ball"},{"name":"Redstone","id":"minecraft:redstone"},{"name":"Glowstone dust","id":"minecraft:glowstone_dust"},{"name":"Quartz","id":"minecraft:quartz"},{"name":"Gold nugget","id":"minecraft:gold_nugget"}]};
// Decorative demo only. Requires Wysicraft's setItem-enabled runtime.
function refresh(ctx) {
    const category = ctx.state.get('category') || 'food';
    const remaining = POOLS[category].slice();
    const previous = JSON.parse(ctx.state.get('last_choices') || '[]');
    const choices = [];
    let total = 0;
    for (let slot = 0; slot < 12; slot++) {
        const candidates = remaining.filter(item => item.id !== previous[slot]);
        const item = candidates[Math.floor(Math.random() * candidates.length)];
        remaining.splice(remaining.indexOf(item), 1);
        choices.push(item.id);
        let amount = category === 'gear' ? 1 : 1 + Math.floor(Math.random() * (category === 'loot' ? 16 : 64));
        if (item.id === 'minecraft:honey_bottle') amount = Math.min(amount, 16);
        total += amount;
        ctx.ui.setItem('icon_' + slot, item.id);
        ctx.ui.setText('name_' + slot, item.name);
        ctx.ui.setText('amount_' + slot, String(amount));
    }
    const roll = Number(ctx.state.get('roll') || '0') + 1;
    ctx.state.set('roll', String(roll));
    ctx.state.set('last_choices', JSON.stringify(choices));
    ctx.ui.setText('summary', '12 stacks  /  ' + total + ' items');
    ctx.ui.setText('edition', 'ROLL ' + String(roll).padStart(2, '0'));
    console.log('Fieldkit: refreshed ' + category + ', ' + total + ' decorative items.');
}
