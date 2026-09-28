package com.wysicraft.runtime.model;

import java.util.*;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/** Sprite clips and shape outlines, matching the editor (SpritesAndShapes.cs) and the web runtime. */
public final class SpritesAndShapes {
    private SpritesAndShapes() {}

    public record Clip(int[] frames, double fps, boolean loop) {}
    private static final Pattern CLIP = Pattern.compile("^([A-Za-z_][A-Za-z0-9_]{0,63})\\s*:\\s*([0-9\\s,-]+?)\\s*(?:@\\s*([0-9]{1,3}(?:\\.[0-9]+)?))?\\s*(once)?$");

    /** "idle: 0; run: 1-6 @12; jump: 7,8,9 @8 once" in order; unreadable parts are skipped. */
    public static LinkedHashMap<String,Clip> clips(String text) {
        var clips = new LinkedHashMap<String,Clip>();
        if (text == null || text.isBlank() || text.length() > 4096) return clips;
        for (String part : text.split(";")) {
            Matcher m = CLIP.matcher(part.trim()); if (!m.matches()) continue;
            var frames = new ArrayList<Integer>();
            for (String piece : m.group(2).split(",")) {
                piece = piece.trim(); if (piece.isEmpty()) continue;
                String[] range = piece.split("-"); int a = Integer.parseInt(range[0].trim()), b = range.length > 1 ? Integer.parseInt(range[1].trim()) : a;
                for (int f = a; a <= b ? f <= b : f >= b; f += a <= b ? 1 : -1) { frames.add(f); if (frames.size() > 1024) break; }
            }
            if (frames.isEmpty()) continue;
            double fps = m.group(3) != null ? Math.clamp(Double.parseDouble(m.group(3)), 0.1, 120) : 8;
            clips.putIfAbsent(m.group(1), new Clip(frames.stream().mapToInt(Integer::intValue).toArray(), fps, m.group(4) == null));
        }
        return clips;
    }
    /** The frame showing, milliseconds after the clip started. */
    public static int frameAt(Clip clip, long elapsedMs) {
        int step = (int)Math.floor(Math.max(0, elapsedMs) * clip.fps() / 1000.0);
        return clip.frames()[clip.loop() ? step % clip.frames().length : Math.min(step, clip.frames().length - 1)];
    }

    /** Shape outline on a unit square, like Shapes.cs. */
    public static double[][] outline(String shape) {
        switch (shape) {
            case "ellipse": { var p = new double[48][]; for (int i = 0; i < 48; i++) p[i] = new double[]{0.5 + 0.5 * Math.cos(i * Math.PI / 24), 0.5 + 0.5 * Math.sin(i * Math.PI / 24)}; return p; }
            case "triangle": return new double[][]{{0.5,0},{1,1},{0,1}};
            case "diamond": return new double[][]{{0.5,0},{1,0.5},{0.5,1},{0,0.5}};
            case "hexagon": return new double[][]{{0.25,0},{0.75,0},{1,0.5},{0.75,1},{0.25,1},{0,0.5}};
            case "star": { var p = new double[10][]; for (int i = 0; i < 10; i++) { double r = i % 2 == 0 ? 0.5 : 0.2, a = -Math.PI / 2 + i * Math.PI / 5; p[i] = new double[]{0.5 + r * Math.cos(a), 0.5 + r * Math.sin(a)}; } return p; }
            default: return new double[][]{{0,0},{1,0},{1,1},{0,1}};
        }
    }
    /** Filled spans [from, to) of one pixel row (through its middle) of the outline stretched to w × h. */
    public static List<int[]> rowSpans(double[][] unit, int w, int h, int row) {
        double y = row + 0.5; var xs = new ArrayList<Double>();
        for (int i = 0; i < unit.length; i++) {
            double[] a = unit[i], b = unit[(i + 1) % unit.length];
            double ay = a[1] * h, by = b[1] * h;
            if ((ay <= y && by > y) || (by <= y && ay > y)) xs.add(a[0] * w + (y - ay) / (by - ay) * (b[0] * w - a[0] * w));
        }
        Collections.sort(xs); var spans = new ArrayList<int[]>();
        for (int i = 0; i + 1 < xs.size(); i += 2) { int from = (int)Math.round(xs.get(i)), to = (int)Math.round(xs.get(i + 1)); if (to > from) spans.add(new int[]{from, to}); }
        return spans;
    }
}
