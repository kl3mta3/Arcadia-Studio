package com.wysicraft.runtime;

import com.wysicraft.runtime.model.SpritesAndShapes;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class SpritesAndShapesTests {
    @Test void clipsParseLikeTheEditor() {
        var clips = SpritesAndShapes.clips("idle: 0; run: 1-6 @12; jump: 7,8,9 @8 once; back: 3-1");
        assertEquals(4, clips.size()); assertEquals("idle", clips.keySet().iterator().next());
        var run = clips.get("run"); assertEquals(6, run.frames().length); assertEquals(12, run.fps());
        assertEquals(1, SpritesAndShapes.frameAt(run, 0)); assertEquals(2, SpritesAndShapes.frameAt(run, 90)); assertEquals(1, SpritesAndShapes.frameAt(run, 500));
        var jump = clips.get("jump"); assertFalse(jump.loop()); assertEquals(9, SpritesAndShapes.frameAt(jump, 10_000));
        assertArrayEquals(new int[]{3, 2, 1}, clips.get("back").frames());
        assertTrue(SpritesAndShapes.clips("nonsense; also bad").isEmpty());
    }
    @Test void shapesFillInsideTheirOutline() {
        var rect = SpritesAndShapes.rowSpans(SpritesAndShapes.outline("rectangle"), 20, 10, 5);
        assertEquals(1, rect.size()); assertArrayEquals(new int[]{0, 20}, rect.getFirst());
        var diamondTop = SpritesAndShapes.rowSpans(SpritesAndShapes.outline("diamond"), 20, 20, 0);
        var diamondMiddle = SpritesAndShapes.rowSpans(SpritesAndShapes.outline("diamond"), 20, 20, 10);
        assertTrue(diamondTop.getFirst()[1] - diamondTop.getFirst()[0] < diamondMiddle.getFirst()[1] - diamondMiddle.getFirst()[0]);
        // A star's arms make two separate spans on some rows.
        boolean twoSpans = false; for (int row = 0; row < 40; row++) if (SpritesAndShapes.rowSpans(SpritesAndShapes.outline("star"), 40, 40, row).size() > 1) twoSpans = true;
        assertTrue(twoSpans);
    }
}
