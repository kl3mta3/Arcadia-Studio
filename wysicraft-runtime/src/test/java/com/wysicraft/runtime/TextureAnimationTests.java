package com.wysicraft.runtime;

import com.wysicraft.runtime.model.TextureAnimation;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class TextureAnimationTests {
    @Test void prismarineStyleStrip() {
        // Prismarine: 16x352 strip (22 frames), listed frames with frametime 300.
        var a = TextureAnimation.parse("{\"animation\":{\"frametime\":300,\"frames\":[0,1,2]}}", 16, 352);
        assertEquals(16, a.frameWidth); assertEquals(22, a.rows); assertEquals(1, a.columns);
        assertEquals(0, a.frameAt(0)); assertEquals(1, a.frameAt(300 * 50)); assertEquals(2, a.frameAt(600 * 50)); assertEquals(0, a.frameAt(900 * 50));
    }
    @Test void defaultsAndPerFrameTimes() {
        var a = TextureAnimation.parse("{\"animation\":{}}", 16, 64);
        assertEquals(4, a.frames.size()); assertEquals(3, a.frameAt(3 * 50));
        var b = TextureAnimation.parse("{\"animation\":{\"frames\":[{\"index\":1,\"time\":4},0]}}", 16, 32);
        assertEquals(1, b.frameAt(150)); assertEquals(0, b.frameAt(200));
        var grid = TextureAnimation.parse("{\"animation\":{\"width\":8,\"height\":8}}", 16, 16);
        assertEquals(4, grid.frames.size()); assertEquals(1, grid.column(3)); assertEquals(1, grid.row(3));
    }
    @Test void notAnimated() {
        assertNull(TextureAnimation.parse(null, 16, 16));
        assertNull(TextureAnimation.parse("{\"texture\":{\"blur\":true}}", 16, 16));
        assertNull(TextureAnimation.parse("not json", 16, 16));
        assertNull(TextureAnimation.parse("{\"animation\":{\"width\":32}}", 16, 16));
    }
}
