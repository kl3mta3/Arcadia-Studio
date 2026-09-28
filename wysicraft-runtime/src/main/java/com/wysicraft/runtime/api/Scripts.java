package com.wysicraft.runtime.api;

import com.wysicraft.runtime.model.Models.Handler;
import com.wysicraft.runtime.pack.PackRepository.Loaded;
import java.nio.charset.StandardCharsets;
import java.util.Map;
import java.util.function.Consumer;

/** Client and server scripts use the bundled engine through a restricted context. */
public final class Scripts {
    private Scripts() {}
    public enum Side { CLIENT, SERVER }
    public interface Context {
        String getVariable(String name);
        void setVariable(String name, String value);
        void message(String text);
        default String elementId() { return ""; }
        default String value() { return ""; }
        // True when a screen Key event comes from a key being held down (auto-repeat) rather than a fresh press.
        default boolean repeat() { return false; }
        default String text(String id) { return ""; }
        default String query(String operation, String argument) { throw new UnsupportedOperationException(operation); }
        default void action(String type, String target, String value) { throw new UnsupportedOperationException(type); }
    }
    public interface Provider { void execute(Side side, String source, String function, Context context) throws Exception; }
    private static Provider provider;
    public static void register(Provider implementation) { if (provider != null) throw new IllegalStateException("Script provider already registered"); provider = implementation; }
    public static void execute(Side side, Loaded pack, Handler handler, Context context, Consumer<String> errors) {
        if (handler.script.isEmpty()) return;
        try {
            String path = com.wysicraft.runtime.pack.PackRepository.safePath(handler.script);
            if (!path.startsWith("scripts/" + side.name().toLowerCase(java.util.Locale.ROOT) + "/")) throw new SecurityException("Wrong script side");
            byte[] source = pack.files().get(path); if (source == null || source.length > ClientJavaScript.MAX_SCRIPT_BYTES) throw new IllegalArgumentException("Missing/oversized script");
            Provider executor = provider != null ? provider : ClientJavaScript.INSTANCE;
            executor.execute(side, new String(source, StandardCharsets.UTF_8), handler.function, context);
        } catch (Exception | LinkageError ex) { errors.accept(handler.script + ": " + ex.getMessage()); }
    }
}
