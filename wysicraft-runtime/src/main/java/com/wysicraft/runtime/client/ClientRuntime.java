package com.wysicraft.runtime.client;

import com.wysicraft.runtime.Wysicraft;
import com.wysicraft.runtime.model.Models;
import com.wysicraft.runtime.network.Payloads;
import com.wysicraft.runtime.pack.PackRepository;
import com.mojang.blaze3d.platform.NativeImage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.texture.DynamicTexture;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.network.chat.Component;
import java.io.ByteArrayInputStream;
import java.util.*;

public final class ClientRuntime {
    private ClientRuntime() {}
    public static final PackRepository PACKS = new PackRepository();
    private static final Map<String,ResourceLocation> TEXTURES = new HashMap<>();
    public static void open(Payloads.OpenUi packet) {
        try {
            var mc = Minecraft.getInstance();
            PACKS.reload(Wysicraft.packRoot(),(message,error) -> { if (error != null) Wysicraft.LOG.warn("{}: {}",message,error.toString()); }, dependency -> true, com.wysicraft.runtime.pack.EmbeddedPacks.paths());
            for (var location : TEXTURES.values()) mc.getTextureManager().release(location); TEXTURES.clear(); INFO.clear();
            var ui = Models.JSON.fromJson(com.wysicraft.runtime.network.UiCompression.unpack(packet.data()),Models.Ui.class);
            if (ui == null || !PackRepository.screenId(ui.id) || ui.elements.size() > 512 || ui.schemaVersion != 1) throw new IllegalArgumentException("Invalid UI payload");
            if (mc.screen instanceof DynamicScreen old) old.remoteClosing = true;
            mc.setScreen(new DynamicScreen(ui,packet.session()));
        } catch (Exception ex) { Wysicraft.LOG.warn("Unable to open UI: {}",ex.toString()); if (Minecraft.getInstance().player != null) Minecraft.getInstance().player.sendSystemMessage(Component.literal("Wysicraft: " + ex.getMessage())); }
    }
    public static void close(Payloads.CloseUi packet) { var mc = Minecraft.getInstance(); if (mc.screen instanceof DynamicScreen screen && screen.session.equals(packet.session())) { screen.remoteClosing = true; mc.setScreen(null); } }
    public static void update(Payloads.UpdateUi packet) {
        var mc = Minecraft.getInstance();
        if (!(mc.screen instanceof DynamicScreen screen) || !screen.session.equals(packet.session())) return;
        if (!Set.of("set_text","set_value","set_visible","set_enabled","set_variable","set_item").contains(packet.action())) return;
        var action = new Models.Action(); action.type = packet.action(); action.target = packet.target(); action.value = packet.value();
        screen.execute(action);
    }
    // A texture plus its animation, when a .png.mcmeta sits next to it (Minecraft, resource packs or the project).
    public record TextureInfo(ResourceLocation location, com.wysicraft.runtime.model.TextureAnimation animation, int width, int height) {}
    private static final Map<String,TextureInfo> INFO = new HashMap<>();
    public static TextureInfo textureInfo(String resource) {
        var cached = INFO.get(resource); if (cached != null) return cached;
        var location = texture(resource); com.wysicraft.runtime.model.TextureAnimation animation = null; int width = 0, height = 0;
        try {
            byte[] png = null; String meta = null; var manager = Minecraft.getInstance().getResourceManager();
            var found = manager.getResource(location);
            if (found.isPresent()) {
                try (var in = found.get().open()) { png = in.readNBytes(24); }
                var metaFile = manager.getResource(location.withPath(location.getPath() + ".mcmeta"));
                if (metaFile.isPresent()) try (var in = metaFile.get().open()) { meta = new String(in.readNBytes(65536), java.nio.charset.StandardCharsets.UTF_8); }
            } else for (var pack : new HashSet<>(PACKS.all().values())) {
                byte[] data = PackRepository.textureFile(pack,resource); if (data == null) continue;
                png = data; byte[] metaData = PackRepository.textureFile(pack,resource + ".mcmeta");
                if (metaData != null) meta = new String(metaData, java.nio.charset.StandardCharsets.UTF_8); break;
            }
            if (png != null && png.length >= 24) { var header = java.nio.ByteBuffer.wrap(png); width = header.getInt(16); height = header.getInt(20); if (meta != null) animation = com.wysicraft.runtime.model.TextureAnimation.parse(meta, width, height); }
        } catch (Exception ex) { Wysicraft.LOG.warn("Texture animation {}: {}",resource,ex.toString()); }
        var info = new TextureInfo(location, animation, width, height); INFO.put(resource, info); return info;
    }
    public static ResourceLocation texture(String resource) {
        var location = ResourceLocation.parse(resource);
        // Resource packs and mod JARs can override a project's canonical texture.
        String path = location.getPath();
        if (!path.startsWith("textures/")) path = "textures/gui/image/" + path;
        else if (path.startsWith("textures/gui/") && !path.substring(13).contains("/")) path = "textures/gui/image/" + path.substring(13);
        var canonical = ResourceLocation.fromNamespaceAndPath(location.getNamespace(), path);
        if (Minecraft.getInstance().getResourceManager().getResource(canonical).isPresent()) return canonical;
        if (TEXTURES.containsKey(resource)) return TEXTURES.get(resource);
        for (var pack : new HashSet<>(PACKS.all().values())) {
            byte[] data = PackRepository.textureFile(pack,resource);
            if (data == null) continue;
            try {
                // Check PNG dimensions before allocating a decoded pixel buffer.
                if (data.length < 24 || data[0] != (byte)137 || data[1] != 80 || data[2] != 78 || data[3] != 71) throw new IllegalArgumentException("Expected PNG");
                var header = java.nio.ByteBuffer.wrap(data); int w = header.getInt(16), h = header.getInt(20);
                if (w < 1 || h < 1 || w > 8192 || h > 8192) throw new IllegalArgumentException("Texture dimensions exceed 8192");
                var image = NativeImage.read(new ByteArrayInputStream(data));
                var registered = Minecraft.getInstance().getTextureManager().register("wysicraft/" + location.getNamespace() + "/" + location.getPath(),new DynamicTexture(image));
                TEXTURES.put(resource,registered); return registered;
            } catch (Exception ex) { Wysicraft.LOG.warn("Texture {}: {}",resource,ex.toString()); return ResourceLocation.withDefaultNamespace("missingno"); }
        }
        return location;
    }
}
