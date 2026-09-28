package com.wysicraft.runtime;
import com.wysicraft.runtime.api.ClientJavaScript;
import com.wysicraft.runtime.api.Scripts;
import org.junit.jupiter.api.Test;
import java.nio.charset.StandardCharsets;
import java.util.*;
import static org.junit.jupiter.api.Assertions.*;

/** Events share one engine so a script is parsed once rather than on every event. What they must not share is state,
 *  and the size limit has to be measured the way the pack measures it. */
class ScriptEngineSharingTests {
    static class Host implements Scripts.Context {
        final Map<String,String> values = new HashMap<>();
        public String getVariable(String n){ return ""; }
        public void setVariable(String n,String v){ values.put(n,v); }
        public void message(String t){}
        public void action(String type,String target,String value){ values.put(target,value); }
    }
    @Test void eventsDoNotShareGlobals() throws Exception {
        String source = "globalThis.seen = (globalThis.seen || 0) + 1; function go(ctx){ ctx.ui.setText('count', String(globalThis.seen)); }";
        for (int i = 0; i < 3; i++) {
            var host = new Host();
            ClientJavaScript.INSTANCE.execute(Scripts.Side.CLIENT, source, "go", host);
            assertEquals("1", host.values.get("count"), "each event starts from a clean global scope");
        }
    }
    @Test void repeatedEventsKeepGivingTheSameAnswer() throws Exception {
        String source = "var total = 0; function go(ctx){ for (var i = 0; i < 5; i++) total += i; ctx.ui.setText('t', String(total)); }";
        for (int i = 0; i < 3; i++) {
            var host = new Host();
            ClientJavaScript.INSTANCE.execute(Scripts.Side.CLIENT, source, "go", host);
            assertEquals("10", host.values.get("t"), "a cached source must not carry values between events");
        }
    }
    @Test void sizeLimitIsMeasuredInUtf8Bytes() {
        // Comfortably inside the limit: accepted.
        var ok = new StringBuilder("function go(ctx){ctx.ui.setText('a','x');}\n");
        while (ok.length() < ClientJavaScript.MAX_SCRIPT_BYTES - 4096) ok.append("// padding padding padding padding\n");
        assertDoesNotThrow(() -> ClientJavaScript.INSTANCE.execute(Scripts.Side.CLIENT, ok.toString(), "go", new Host()));
        // Over the limit in plain ASCII: refused.
        var over = new StringBuilder(ok);
        while (over.toString().getBytes(StandardCharsets.UTF_8).length <= ClientJavaScript.MAX_SCRIPT_BYTES) over.append("// padding padding padding padding\n");
        assertThrows(IllegalArgumentException.class, () -> ClientJavaScript.INSTANCE.execute(Scripts.Side.CLIENT, over.toString(), "go", new Host()));
        // Inside the limit counted in chars, over it counted in UTF-8 bytes: refused, because the pack counts bytes.
        var wide = new StringBuilder("function go(ctx){}\n// ");
        wide.append("—".repeat(ClientJavaScript.MAX_SCRIPT_BYTES / 2));
        assertTrue(wide.length() < ClientJavaScript.MAX_SCRIPT_BYTES, "under the limit measured in UTF-16 chars");
        assertTrue(wide.toString().getBytes(StandardCharsets.UTF_8).length > ClientJavaScript.MAX_SCRIPT_BYTES, "over it measured in UTF-8 bytes");
        assertThrows(IllegalArgumentException.class, () -> ClientJavaScript.INSTANCE.execute(Scripts.Side.CLIENT, wide.toString(), "go", new Host()));
    }
}
