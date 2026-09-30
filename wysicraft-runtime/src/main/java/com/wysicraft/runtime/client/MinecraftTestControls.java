package com.wysicraft.runtime.client;

import com.mojang.blaze3d.platform.InputConstants;
import net.minecraft.client.*;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;
import net.neoforged.bus.api.IEventBus;
import net.neoforged.neoforge.common.NeoForge;
import net.neoforged.neoforge.client.event.*;
import net.neoforged.neoforge.client.settings.KeyConflictContext;
import org.lwjgl.glfw.GLFW;

/** Installed only by the opt-in local Minecraft test harness. */
public final class MinecraftTestControls {
    private static final String CATEGORY = "key.categories.wysicraft_test";
    private static final KeyMapping OPEN = key("open",GLFW.GLFW_KEY_F6);
    private static final KeyMapping CLOSE = key("close",GLFW.GLFW_KEY_F7);
    private static final KeyMapping CONTROLS = key("controls",GLFW.GLFW_KEY_F8);
    private static boolean toolbar = false;
    private static KeyMapping key(String name,int code) { return new KeyMapping("key.wysicraft_test."+name,KeyConflictContext.UNIVERSAL,InputConstants.Type.KEYSYM,code,CATEGORY); }
    public static void install(IEventBus bus) {
        bus.addListener((RegisterKeyMappingsEvent e) -> { e.register(OPEN); e.register(CLOSE); e.register(CONTROLS); });
        NeoForge.EVENT_BUS.addListener(MinecraftTestControls::tick);
        NeoForge.EVENT_BUS.addListener(MinecraftTestControls::keyPressed);
        NeoForge.EVENT_BUS.addListener(MinecraftTestControls::render);
        NeoForge.EVENT_BUS.addListener(MinecraftTestControls::click);
    }
    private static boolean available() { var mc=Minecraft.getInstance(); return mc.player!=null && mc.getSingleplayerServer()!=null && !MinecraftTestHarness.project().isEmpty(); }
    private static void tick(ClientTickEvent.Post e) { consumeKeys(); }
    static void consumeKeys() {
        var mc=Minecraft.getInstance();
        // GUI keys are handled before the screen; always drain stale clicks.
        boolean open=false,close=false,controls=false;
        while(OPEN.consumeClick()) open=true;
        while(CLOSE.consumeClick()) close=true;
        while(CONTROLS.consumeClick()) controls=true;
        if(mc.screen!=null || !available()) return;
        if(open) command(true); else if(close) command(false); else if(controls) mc.setScreen(new ControlScreen());
    }
    private static void keyPressed(ScreenEvent.KeyPressed.Pre e) {
        if(!available() || e.getScreen() instanceof net.minecraft.client.gui.screens.options.controls.KeyBindsScreen) return;
        var key=InputConstants.getKey(e.getKeyCode(),e.getScanCode());
        if(OPEN.isActiveAndMatches(key)) { e.setCanceled(true); command(true); }
        else if(CLOSE.isActiveAndMatches(key)) { e.setCanceled(true); command(false); }
        else if(CONTROLS.isActiveAndMatches(key)) { e.setCanceled(true); if(e.getScreen() instanceof ControlScreen) e.getScreen().onClose(); else if(DynamicScreen.of(e.getScreen()) != null) toolbar=!toolbar; else Minecraft.getInstance().setScreen(new ControlScreen()); }
    }
    static void command(boolean open) {
        if(!available()) return;
        toolbar = false;
        var mc=Minecraft.getInstance();
        if(mc.isPaused()) mc.setScreen(null);
        // Use the same registered command as the desktop button, as the player.
        mc.player.connection.sendCommand(MinecraftTestHarness.project()+(open?".open":".close"));
    }
    private static void render(ScreenEvent.Render.Post e) {
        if(!(DynamicScreen.of(e.getScreen()) != null) || !available() || !toolbar) return;
        var g=e.getGuiGraphics(); var font=Minecraft.getInstance().font;
        g.pose().pushPose(); g.pose().translate(0,0,500);
        for(int i=0;i<2;i++) {
            int x=5+i*66;
            boolean hover=e.getMouseX()>=x && e.getMouseX()<x+62 && e.getMouseY()>=5 && e.getMouseY()<25;
            g.fill(x,5,x+62,25,hover?0xFF526981:0xFF293848);
            g.drawCenteredString(font,i==0?"Reset":".close",x+31,11,0xFFFFFFFF);
        }
        g.pose().popPose();
    }
    private static void click(ScreenEvent.MouseButtonPressed.Pre e) {
        if(!(DynamicScreen.of(e.getScreen()) != null) || !available() || !toolbar || e.getButton()!=0 || e.getMouseY()<5 || e.getMouseY()>=25) return;
        if(e.getMouseX()>=5 && e.getMouseX()<67) { e.setCanceled(true); command(true); }
        else if(e.getMouseX()>=71 && e.getMouseX()<133) { e.setCanceled(true); command(false); }
    }
    static final class ControlScreen extends Screen {
        ControlScreen() { super(Component.literal("Wysicraft test controls")); }
        @Override public boolean isPauseScreen() { return false; }
        @Override protected void init() {
            addRenderableWidget(Button.builder(Component.literal(".open"),b->command(true)).bounds(width/2-105,height/2-10,100,20).build());
            addRenderableWidget(Button.builder(Component.literal(".close"),b->command(false)).bounds(width/2+5,height/2-10,100,20).build());
            addRenderableWidget(Button.builder(Component.literal("Return to world"),b->onClose()).bounds(width/2-70,height/2+20,140,20).build());
        }
        @Override public void render(GuiGraphics g,int mx,int my,float partial) {
            g.fill(0,0,width,height,0xD018202A);
            g.drawCenteredString(font,title,width/2,height/2-65,0xFFFFFFFF);
            g.drawCenteredString(font,MinecraftTestHarness.project(),width/2,height/2-48,0xFF80DFFF);
            g.drawCenteredString(font,"Open: "+OPEN.getTranslatedKeyMessage().getString()+"   Close: "+CLOSE.getTranslatedKeyMessage().getString(),width/2,height/2+55,0xFFFFFFFF);
            super.render(g,mx,my,partial);
        }
    }
}
