package com.wysicraft.runtime.network;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.zip.Deflater;
import java.util.zip.DeflaterOutputStream;
import java.util.zip.InflaterInputStream;

/** Screens travel compressed: their JSON repeats the same property names and defaults for every control,
 * so it shrinks 10-20x. Both limits are checked on the server (clear error) and again on the client (safety). */
public final class UiCompression {
    private UiCompression() {}
    /** Largest compressed screen sent to a player (well under Minecraft's 1 MiB custom-payload limit). */
    public static final int MAX_PACKED = 900_000;
    /** Largest screen JSON a client will unpack. */
    public static final int MAX_JSON_BYTES = 8_000_000;

    public static byte[] pack(String json) {
        byte[] raw = json.getBytes(StandardCharsets.UTF_8);
        if (raw.length > MAX_JSON_BYTES) throw new IllegalArgumentException("UI exceeds network size limit (" + raw.length / 1000 + " kB of JSON; the limit is " + MAX_JSON_BYTES / 1000 + " kB)");
        var out = new ByteArrayOutputStream(Math.max(64, raw.length / 8));
        try (var deflate = new DeflaterOutputStream(out, new Deflater(Deflater.BEST_COMPRESSION))) { deflate.write(raw); }
        catch (IOException ex) { throw new IllegalStateException(ex); }
        byte[] packed = out.toByteArray();
        if (packed.length > MAX_PACKED) throw new IllegalArgumentException("UI exceeds network size limit (" + packed.length / 1000 + " kB compressed; the limit is " + MAX_PACKED / 1000 + " kB)");
        return packed;
    }

    public static String unpack(byte[] packed) {
        if (packed.length > MAX_PACKED) throw new IllegalArgumentException("UI exceeds network size limit");
        try (var inflate = new InflaterInputStream(new ByteArrayInputStream(packed))) {
            byte[] raw = inflate.readNBytes(MAX_JSON_BYTES + 1);
            if (raw.length > MAX_JSON_BYTES) throw new IllegalArgumentException("UI exceeds network size limit");
            return new String(raw, StandardCharsets.UTF_8);
        } catch (IOException ex) { throw new IllegalArgumentException("Corrupt UI data", ex); }
    }
}
