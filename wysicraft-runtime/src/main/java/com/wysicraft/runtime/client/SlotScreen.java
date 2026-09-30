package com.wysicraft.runtime.client;

import com.wysicraft.runtime.menu.WysicraftMenu;
import com.wysicraft.runtime.model.Models;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.screens.inventory.AbstractContainerScreen;
import net.minecraft.network.chat.Component;
import net.minecraft.world.entity.player.Inventory;
import net.neoforged.bus.api.IEventBus;
import net.neoforged.neoforge.client.event.RegisterMenuScreensEvent;
import org.lwjgl.glfw.GLFW;

/**
 * A screen with item slots: Minecraft's own container screen (so dragging, shift-clicking and splitting stacks work
 * exactly as players expect, with the server moving the items), drawing the Arcadia Studio design underneath the slots.
 * Everything else on the screen (buttons, text boxes, scripts, events) is the ordinary {@link DynamicScreen}, hosted here.
 */
public final class SlotScreen extends AbstractContainerScreen<WysicraftMenu> {
    final DynamicScreen inner;

    public static void register(IEventBus bus) { bus.addListener((RegisterMenuScreensEvent event) -> event.register(WysicraftMenu.TYPE.get(), SlotScreen::new)); }

    public SlotScreen(WysicraftMenu menu, Inventory inventory, Component title) {
        super(menu, inventory, title);
        // The design arrived just before the container opened (ClientRuntime.open keeps it for this session).
        Models.Ui ui = ClientRuntime.takePending(menu.token);
        if (ui == null) { ui = new Models.Ui(); ui.id = "wysicraft:missing"; ui.title = title.getString(); }
        inner = new DynamicScreen(ui, menu.token); inner.host = this;
        menu.visible = id -> { var e = inner.ui.element(id); return e != null && inner.visible(e); };
        menu.enabled = id -> { var e = inner.ui.element(id); return e != null && inner.visible(e) && inner.enabled(e); };
        imageWidth = ui.size.width; imageHeight = ui.size.height;
    }

    @Override protected void init() {
        inner.init(minecraft, width, height);
        super.init();
        // Slots sit where their controls are: the container's corner is the design's corner.
        leftPos = inner.originX(); topPos = inner.originY();
    }
    @Override protected void containerTick() { inner.tick(); }
    @Override public boolean isPauseScreen() { return false; }

    // The design is the container's background; slots, held items and item tooltips draw over it.
    @Override public void renderBackground(GuiGraphics g, int mouseX, int mouseY, float partial) { renderBg(g, partial, mouseX, mouseY); }
    @Override protected void renderBg(GuiGraphics g, float partial, int mouseX, int mouseY) { inner.render(g, mouseX, mouseY, partial); }
    @Override protected void renderLabels(GuiGraphics g, int mouseX, int mouseY) { }
    @Override public void render(GuiGraphics g, int mouseX, int mouseY, float partial) { super.render(g, mouseX, mouseY, partial); renderTooltip(g, mouseX, mouseY); }

    private boolean overSlot(double mx, double my) {
        for (var slot : menu.slots) if (slot.isActive() && isHovering(slot.x, slot.y, 16, 16, mx, my)) return true;
        return false;
    }
    @Override public boolean mouseClicked(double mx, double my, int button) {
        // Slots (and anything carried) are Minecraft's; everything else is the design's buttons, boxes and lists.
        if (overSlot(mx, my) || !menu.getCarried().isEmpty()) return super.mouseClicked(mx, my, button);
        return inner.mouseClicked(mx, my, button);
    }
    @Override public boolean mouseDragged(double mx, double my, int button, double dx, double dy) {
        if (inner.dragging != null) return inner.mouseDragged(mx, my, button, dx, dy);
        return super.mouseDragged(mx, my, button, dx, dy);
    }
    @Override public boolean mouseReleased(double mx, double my, int button) { inner.mouseReleased(mx, my, button); return super.mouseReleased(mx, my, button); }
    @Override public boolean mouseScrolled(double mx, double my, double horizontal, double vertical) { return inner.mouseScrolled(mx, my, horizontal, vertical) || super.mouseScrolled(mx, my, horizontal, vertical); }
    @Override public boolean charTyped(char c, int modifiers) { return inner.charTyped(c, modifiers) || super.charTyped(c, modifiers); }
    @Override public boolean keyPressed(int key, int scan, int modifiers) {
        if (key == GLFW.GLFW_KEY_ESCAPE) { onClose(); return true; }
        if (inner.focused != null) { inner.keyPressed(key, scan, modifiers); return true; }   // typing in a text box
        if (minecraft.options.keyInventory.matches(key, scan)) { onClose(); return true; }     // E closes, as in any container
        inner.keyPressed(key, scan, modifiers);                                                  // the screen's Key event
        return super.keyPressed(key, scan, modifiers);                                           // hotbar keys, drop, pick
    }
    @Override public boolean keyReleased(int key, int scan, int modifiers) { inner.keyReleased(key, scan, modifiers); return super.keyReleased(key, scan, modifiers); }

    // Closing: the design's close event, then Minecraft closes the container (the server returns left-over items).
    @Override public void onClose() { inner.closing(); super.onClose(); }
    @Override public void removed() { inner.closing(); super.removed(); }
}
