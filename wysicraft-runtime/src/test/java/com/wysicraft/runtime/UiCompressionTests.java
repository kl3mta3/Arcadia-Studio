package com.wysicraft.runtime;

import java.lang.StringBuilder;
import com.wysicraft.runtime.network.UiCompression;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class UiCompressionTests {
    @Test void bigScreensTravelCompressed() {
        // 600 image controls: far past the old 200,000-character limit as plain JSON.
        var sb = new StringBuilder("{\"id\":\"big\",\"elements\":[");
        for (int i = 0; i < 600; i++) sb.append(i == 0 ? "" : ",").append("{\"id\":\"tile").append(i).append("\",\"type\":\"image\",\"texture\":\"minecraft:textures/block/acacia_planks.png\",\"foreground\":\"#FFFFFF\",\"background\":\"#40464F\",\"borderColor\":\"#697382\",\"font\":\"minecraft:default\",\"horizontalAnchor\":\"left\",\"verticalAnchor\":\"top\",\"options\":[\"Option 1\",\"Option 2\"],\"bounds\":{\"x\":").append(i % 20 * 16).append(",\"y\":").append(i / 20 * 16).append(",\"width\":16,\"height\":16},\"events\":{},\"rowElements\":[],\"text\":\"Button\",\"value\":\"0\",\"item\":\"minecraft:stone\",\"visibleIf\":\"\",\"enabledIf\":\"\",\"parent\":\"\",\"tooltip\":\"\",\"shadowColor\":\"#000000\",\"alignment\":\"left\"}");
        String json = sb.append("]}").toString();
        assertTrue(json.length() > 200_000, "fixture should exceed the old limit: " + json.length());
        byte[] packed = UiCompression.pack(json);
        assertTrue(packed.length < json.length() / 10, "compressed to " + packed.length);
        assertEquals(json, UiCompression.unpack(packed));
    }
    @Test void limitsAndCorruptData() {
        assertThrows(IllegalArgumentException.class, () -> UiCompression.pack("x".repeat(UiCompression.MAX_JSON_BYTES + 1)));
        assertThrows(IllegalArgumentException.class, () -> UiCompression.unpack(new byte[]{1,2,3,4}));
        assertThrows(IllegalArgumentException.class, () -> UiCompression.unpack(UiCompression.pack("[" + " ".repeat(10) + "]").length > 0 ? new byte[UiCompression.MAX_PACKED + 1] : new byte[0]));
    }
}
