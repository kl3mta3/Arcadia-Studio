package com.wysicraft.runtime.model;

/** Names sent to a screen's Key event for a GLFW key code: letters and digits as themselves ("a", "7"),
 * plus "space", "enter", "tab", "backspace", "left", "right", "up", "down" and "f1".."f12".
 * Escape is never forwarded, so it always closes the screen. Codes are GLFW's, kept literal so this class needs no LWJGL. */
public final class KeyNames {
    private KeyNames() {}
    public static String name(int key) {
        if (key >= 65 && key <= 90) return String.valueOf((char) ('a' + key - 65));   // A..Z
        if (key >= 48 && key <= 57) return String.valueOf((char) ('0' + key - 48));   // 0..9
        if (key >= 320 && key <= 329) return String.valueOf((char) ('0' + key - 320)); // numpad 0..9
        if (key >= 290 && key <= 301) return "f" + (key - 289);                         // F1..F12
        return switch (key) {
            case 32 -> "space";
            case 257, 335 -> "enter";   // Enter, numpad Enter
            case 258 -> "tab";
            case 259 -> "backspace";
            case 262 -> "right";
            case 263 -> "left";
            case 264 -> "down";
            case 265 -> "up";
            default -> null;
        };
    }
}
