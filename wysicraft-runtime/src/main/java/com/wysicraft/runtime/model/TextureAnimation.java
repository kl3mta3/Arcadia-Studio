package com.wysicraft.runtime.model;

import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import java.util.ArrayList;
import java.util.List;

/** A Minecraft-style animated texture: frames stacked in the PNG (e.g. prismarine) and timed by a
 * {@code .png.mcmeta} file: {"animation":{"frametime":2,"frames":[0,1,{"index":2,"time":4}],"width":16,"height":16}}.
 * Frame times are game ticks (50 ms). Interpolation is not supported; frames switch. */
public final class TextureAnimation {
    public record Frame(int index, int ticks) {}
    public final int frameWidth, frameHeight, columns, rows;
    public final List<Frame> frames;
    private final int totalTicks;

    private TextureAnimation(int frameWidth, int frameHeight, int columns, int rows, List<Frame> frames) {
        this.frameWidth = frameWidth; this.frameHeight = frameHeight; this.columns = columns; this.rows = rows; this.frames = frames;
        this.totalTicks = frames.stream().mapToInt(Frame::ticks).sum();
    }

    /** Null when the metadata has no "animation" section, or it doesn't fit the image. */
    public static TextureAnimation parse(String mcmeta, int width, int height) {
        if (mcmeta == null || mcmeta.isBlank() || width < 1 || height < 1) return null;
        JsonObject root; try { root = JsonParser.parseString(mcmeta).getAsJsonObject(); } catch (Exception ex) { return null; }
        if (!root.has("animation") || !root.get("animation").isJsonObject()) return null;
        JsonObject animation = root.getAsJsonObject("animation");
        int fw = animation.has("width") ? animation.get("width").getAsInt() : -1, fh = animation.has("height") ? animation.get("height").getAsInt() : -1;
        // Minecraft's defaults: square frames the size of the image's shorter side.
        if (fw < 1 && fh < 1) { fw = fh = Math.min(width, height); } else if (fw < 1) fw = fh; else if (fh < 1) fh = fw;
        if (fw > width || fh > height) return null;
        int columns = width / fw, rows = height / fh, count = columns * rows;
        if (count < 1) return null;
        int frametime = Math.max(1, animation.has("frametime") ? animation.get("frametime").getAsInt() : 1);
        var frames = new ArrayList<Frame>();
        if (animation.has("frames") && animation.get("frames").isJsonArray()) {
            for (JsonElement entry : animation.getAsJsonArray("frames")) {
                int index, ticks = frametime;
                if (entry.isJsonObject()) { var o = entry.getAsJsonObject(); index = o.get("index").getAsInt(); if (o.has("time")) ticks = Math.max(1, o.get("time").getAsInt()); }
                else index = entry.getAsInt();
                if (index >= 0 && index < count) frames.add(new Frame(index, ticks));
                if (frames.size() >= 1024) break;
            }
        } else for (int i = 0; i < Math.min(count, 1024); i++) frames.add(new Frame(i, frametime));
        return frames.isEmpty() ? null : new TextureAnimation(fw, fh, columns, rows, List.copyOf(frames));
    }

    /** The frame index (position in the image) showing at this time. */
    public int frameAt(long millis) {
        long tick = Math.floorMod(millis / 50, (long) totalTicks);
        for (Frame frame : frames) { if (tick < frame.ticks()) return frame.index(); tick -= frame.ticks(); }
        return frames.getLast().index();
    }
    public int column(int index) { return index % columns; }
    public int row(int index) { return index / columns; }
}
