package com.wysicraft.runtime.menu;

import com.wysicraft.runtime.model.Models.Element;
import com.wysicraft.runtime.model.Models.Ui;
import net.minecraft.core.registries.Registries;
import net.minecraft.network.RegistryFriendlyByteBuf;
import net.minecraft.network.chat.Component;
import net.minecraft.network.protocol.game.ClientboundContainerSetSlotPacket;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.Container;
import net.minecraft.world.SimpleContainer;
import net.minecraft.world.SimpleMenuProvider;
import net.minecraft.world.entity.player.Inventory;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.inventory.*;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.crafting.RecipeType;
import net.neoforged.neoforge.common.extensions.IMenuTypeExtension;
import net.neoforged.neoforge.registries.DeferredHolder;
import net.neoforged.neoforge.registries.DeferredRegister;
import java.util.*;
import java.util.function.Predicate;

/**
 * A screen with item slots is a real Minecraft container: the server owns the slots and Minecraft's own container code
 * moves every item (clicks, drags, shift-clicks, splitting stacks), so nothing can be duplicated from the client.
 * Slot groups come from the screen's "slots" controls:
 *   player   – part of the player's own inventory (slotStart: 0-8 hotbar, 9-35 the rest);
 *   storage  – temporary slots; whatever is left in them goes back to the player when the screen closes;
 *   crafting – a crafting grid (up to 3 × 3) using the game's recipes; left-over items go back on close;
 *   result   – the crafting grid's output.
 */
public final class WysicraftMenu extends AbstractContainerMenu {
    public static final DeferredRegister<MenuType<?>> MENUS = DeferredRegister.create(Registries.MENU, "wysicraft");
    public static final DeferredHolder<MenuType<?>,MenuType<WysicraftMenu>> TYPE = MENUS.register("slots", () -> IMenuTypeExtension.create(WysicraftMenu::fromNetwork));
    public static final Set<String> KINDS = com.wysicraft.runtime.pack.PackRepository.SLOT_KINDS;
    public static final int CELL = 18;

    /** One slots control: its element, kind, place and size (screen coordinates), grid and first player slot. */
    public record Group(String element, String kind, int x, int y, int width, int height, int columns, int rows, int start) {}

    public final String token;
    public final List<Group> groups;
    private final Player player;
    /** Whether a slots control is shown / usable right now (the screen's visibility rules on each side). */
    public Predicate<String> visible = id -> true, enabled = id -> true;
    private final List<Container> returned = new ArrayList<>();
    private TransientCraftingContainer craft;
    private ResultContainer result;
    private int resultIndex = -1, playerFrom;

    WysicraftMenu(int id, Inventory inventory, String token, List<Group> groups) {
        super(TYPE.get(), id);
        this.token = token; this.groups = List.copyOf(groups); this.player = inventory.player;
        // Grids first, then the result (it reads the grid), then the player's slots as one run at the end.
        for (var g : groups) if (g.kind.equals("crafting")) { craft = new TransientCraftingContainer(this, g.columns, g.rows); returned.add(craft); grid(g, craft, 0); }
        for (var g : groups) if (g.kind.equals("storage")) { var box = new SimpleContainer(g.columns * g.rows); returned.add(box); grid(g, box, 0); }
        for (var g : groups) if (g.kind.equals("result") && craft != null && resultIndex < 0) {
            result = new ResultContainer(); resultIndex = slots.size();
            addSlot(new GroupResultSlot(player, craft, result, g.x + (g.width - 16) / 2, g.y + (g.height - 16) / 2, g.element));
        }
        playerFrom = slots.size();
        for (var g : groups) if (g.kind.equals("player")) grid(g, inventory, g.start);
    }
    private void grid(Group g, Container container, int first) {
        for (int row = 0; row < g.rows; row++) for (int column = 0; column < g.columns; column++)
            addSlot(new GroupSlot(container, first + row * g.columns + column, g.x + 1 + column * CELL, g.y + 1 + row * CELL, g.element));
    }

    private final class GroupSlot extends Slot {
        final String element;
        GroupSlot(Container container, int index, int x, int y, String element) { super(container, index, x, y); this.element = element; }
        @Override public boolean isActive() { return visible.test(element); }
        @Override public boolean mayPlace(ItemStack stack) { return isActive() && enabled.test(element) && super.mayPlace(stack); }
        @Override public boolean mayPickup(Player player) { return isActive() && enabled.test(element) && super.mayPickup(player); }
    }
    private final class GroupResultSlot extends ResultSlot {
        final String element;
        GroupResultSlot(Player player, CraftingContainer craft, Container result, int x, int y, String element) { super(player, craft, result, 0, x, y); this.element = element; }
        @Override public boolean isActive() { return visible.test(element); }
        @Override public boolean mayPickup(Player player) { return isActive() && enabled.test(element) && super.mayPickup(player); }
    }

    // ---- The crafting grid's result, worked out on the server like a crafting table ----
    @Override public void slotsChanged(Container container) {
        if (container == craft && resultIndex >= 0 && player instanceof ServerPlayer server) updateResult(server);
        super.slotsChanged(container);
    }
    private void updateResult(ServerPlayer server) {
        var level = server.serverLevel(); var input = craft.asCraftInput(); ItemStack made = ItemStack.EMPTY;
        var recipe = level.getServer().getRecipeManager().getRecipeFor(RecipeType.CRAFTING, input, level);
        if (recipe.isPresent() && result.setRecipeUsed(level, server, recipe.get())) {
            var stack = recipe.get().value().assemble(input, level.registryAccess());
            if (stack.isItemEnabled(level.enabledFeatures())) made = stack;
        }
        result.setItem(0, made);
        setRemoteSlot(resultIndex, made);
        server.connection.send(new ClientboundContainerSetSlotPacket(containerId, incrementStateId(), resultIndex, made));
    }

    // ---- Shift-click ----
    @Override public ItemStack quickMoveStack(Player player, int index) {
        if (index < 0 || index >= slots.size()) return ItemStack.EMPTY;
        Slot slot = slots.get(index);
        if (!slot.hasItem() || !slot.isActive() || !slot.mayPickup(player)) return ItemStack.EMPTY;
        ItemStack stack = slot.getItem(), before = stack.copy();
        List<Integer> targets = new ArrayList<>();
        if (index == resultIndex) { stack.getItem().onCraftedBy(stack, player.level(), player); targets = playerSlots(true); }
        else if (index >= playerFrom) {
            // From the player: into storage if there is any, otherwise between the hotbar and the rest of the inventory.
            for (int i = 0; i < playerFrom; i++) if (i != resultIndex && slots.get(i).container != craft && slots.get(i).isActive()) targets.add(i);
            if (targets.isEmpty()) { boolean hotbar = slot.getContainerSlot() < 9; for (int i = playerFrom; i < slots.size(); i++) if ((slots.get(i).getContainerSlot() < 9) != hotbar) targets.add(i); }
        } else targets = playerSlots(false);
        if (!moveTo(stack, targets)) return ItemStack.EMPTY;
        if (index == resultIndex) slot.onQuickCraft(stack, before);
        if (stack.isEmpty()) slot.setByPlayer(ItemStack.EMPTY); else slot.setChanged();
        if (stack.getCount() == before.getCount()) return ItemStack.EMPTY;
        slot.onTake(player, stack);
        if (index == resultIndex) player.drop(stack, false);
        return before;
    }
    private List<Integer> playerSlots(boolean backwards) {
        var list = new ArrayList<Integer>(); for (int i = playerFrom; i < slots.size(); i++) if (slots.get(i).isActive()) list.add(i);
        if (backwards) Collections.reverse(list); return list;
    }
    /** Like moveItemStackTo, over any set of slots: first onto matching stacks, then into empty slots. */
    private boolean moveTo(ItemStack stack, List<Integer> targets) {
        boolean moved = false;
        if (stack.isStackable()) for (int i : targets) {
            if (stack.isEmpty()) break;
            Slot slot = slots.get(i); ItemStack there = slot.getItem();
            if (there.isEmpty() || !ItemStack.isSameItemSameComponents(stack, there) || !slot.mayPlace(stack)) continue;
            int room = slot.getMaxStackSize(there) - there.getCount();
            if (room <= 0) continue;
            int n = Math.min(room, stack.getCount()); there.grow(n); stack.shrink(n); slot.setChanged(); moved = true;
        }
        for (int i : targets) {
            if (stack.isEmpty()) break;
            Slot slot = slots.get(i);
            if (slot.hasItem() || !slot.mayPlace(stack)) continue;
            slot.setByPlayer(stack.split(Math.min(stack.getCount(), slot.getMaxStackSize(stack)))); slot.setChanged(); moved = true;
        }
        return moved;
    }
    @Override public boolean canTakeItemForPickAll(ItemStack stack, Slot slot) { return slot.container != result && super.canTakeItemForPickAll(stack, slot); }

    // ---- Scripts: what a slots control holds, and taking items out of one (ui.getSlots / player.takeFromSlot) ----
    /** The stacks in one slots control's cells, in grid order (empty stacks included). */
    public List<ItemStack> stacksOf(String element) {
        var list = new ArrayList<ItemStack>();
        for (Slot slot : slots) if (element.equals(owner(slot))) list.add(slot.getItem());
        return list;
    }
    /** Takes up to count items matching the test out of one slots control (server side). Returns how many were taken. */
    public int takeFrom(String element, java.util.function.Predicate<ItemStack> matches, int count) {
        int taken = 0;
        for (Slot slot : slots) {
            if (taken >= count) break;
            if (!element.equals(owner(slot)) || !slot.hasItem()) continue;
            ItemStack stack = slot.getItem(); if (!matches.test(stack)) continue;
            int n = Math.min(count - taken, stack.getCount()); stack.shrink(n); taken += n;
            if (stack.isEmpty()) slot.set(ItemStack.EMPTY); else slot.setChanged();
        }
        return taken;
    }
    private static String owner(Slot slot) { return slot instanceof GroupSlot g ? g.element : slot instanceof GroupResultSlot r ? r.element : null; }

    // ---- Open and close ----
    @Override public boolean stillValid(Player player) { return !(player instanceof ServerPlayer server) || com.wysicraft.runtime.Wysicraft.SERVER.hasSession(server, token); }
    /** Items left in storage or the crafting grid go back to the player (or drop, if they can't take them). */
    @Override public void removed(Player player) {
        super.removed(player);
        if (!player.level().isClientSide) for (var container : returned) clearContainer(player, container);
    }

    public static boolean hasSlots(Ui ui) { return ui.elements.stream().anyMatch(e -> e.type.equals("slots")); }
    public static List<Group> groups(Ui ui) {
        var list = new ArrayList<Group>();
        for (Element e : ui.elements) if (e.type.equals("slots"))
            list.add(new Group(e.id, e.slotKind, (int) e.bounds.x, (int) e.bounds.y, (int) e.bounds.width, (int) e.bounds.height, e.columns, e.rows, e.slotStart));
        return list;
    }
    /** Opens the container for a session that has just been opened (the client already has the screen's design). */
    public static void open(ServerPlayer player, String token, Ui ui, Predicate<String> visible, Predicate<String> enabled) {
        var groups = groups(ui);
        player.openMenu(new SimpleMenuProvider((id, inventory, p) -> { var menu = new WysicraftMenu(id, inventory, token, groups); menu.visible = visible; menu.enabled = enabled; return menu; }, Component.literal(ui.title)),
            buf -> write(buf, token, groups));
    }
    static void write(RegistryFriendlyByteBuf buf, String token, List<Group> groups) {
        buf.writeUtf(token, 36); buf.writeVarInt(groups.size());
        for (var g : groups) { buf.writeUtf(g.element, 64); buf.writeUtf(g.kind, 16); buf.writeVarInt(g.x); buf.writeVarInt(g.y); buf.writeVarInt(g.width); buf.writeVarInt(g.height); buf.writeVarInt(g.columns); buf.writeVarInt(g.rows); buf.writeVarInt(g.start); }
    }
    static WysicraftMenu fromNetwork(int id, Inventory inventory, RegistryFriendlyByteBuf buf) {
        String token = buf.readUtf(36); int count = buf.readVarInt();
        if (count < 0 || count > 64) throw new IllegalArgumentException("Too many slot groups");
        var groups = new ArrayList<Group>();
        for (int i = 0; i < count; i++) {
            var g = new Group(buf.readUtf(64), buf.readUtf(16), buf.readVarInt(), buf.readVarInt(), buf.readVarInt(), buf.readVarInt(), buf.readVarInt(), buf.readVarInt(), buf.readVarInt());
            if (!KINDS.contains(g.kind) || g.columns < 1 || g.rows < 1 || g.columns * g.rows > 81 || g.start < 0 || (g.kind.equals("player") && g.start + g.columns * g.rows > 36)) throw new IllegalArgumentException("Invalid slot group");
            groups.add(g);
        }
        return new WysicraftMenu(id, inventory, token, groups);
    }
}
