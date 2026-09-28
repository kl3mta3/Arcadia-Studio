package com.wysicraft.runtime.network;

import net.minecraft.network.RegistryFriendlyByteBuf;
import net.minecraft.network.codec.StreamCodec;
import net.minecraft.network.protocol.common.custom.CustomPacketPayload;
import net.minecraft.resources.ResourceLocation;

public final class Payloads {
    private Payloads() {}
    public record UpdateUi(String session, String action, String target, String value) implements CustomPacketPayload {
        public static final Type<UpdateUi> TYPE = new Type<>(ResourceLocation.fromNamespaceAndPath("wysicraft","update"));
        public static final StreamCodec<RegistryFriendlyByteBuf,UpdateUi> CODEC = StreamCodec.of((buf,p) -> { buf.writeUtf(p.session,36); buf.writeUtf(p.action,32); buf.writeUtf(p.target,64); buf.writeUtf(p.value,4096); }, buf -> new UpdateUi(buf.readUtf(36),buf.readUtf(32),buf.readUtf(64),buf.readUtf(4096)));
        public Type<? extends CustomPacketPayload> type() { return TYPE; }
    }
    public record UiEvent(String ui, String session, String element, String event, String value) implements CustomPacketPayload {
        public static final Type<UiEvent> TYPE = new Type<>(ResourceLocation.fromNamespaceAndPath("wysicraft","event"));
        public static final StreamCodec<RegistryFriendlyByteBuf,UiEvent> CODEC = StreamCodec.of((buf,p) -> { buf.writeUtf(p.ui,129); buf.writeUtf(p.session,36); buf.writeUtf(p.element,64); buf.writeUtf(p.event,32); buf.writeUtf(p.value,1024); }, buf -> new UiEvent(buf.readUtf(129),buf.readUtf(36),buf.readUtf(64),buf.readUtf(32),buf.readUtf(1024)));
        public Type<? extends CustomPacketPayload> type() { return TYPE; }
    }
    // The screen JSON travels compressed (see UiCompression).
    public record OpenUi(String session, byte[] data) implements CustomPacketPayload {
        public static final Type<OpenUi> TYPE = new Type<>(ResourceLocation.fromNamespaceAndPath("wysicraft","open"));
        public static final StreamCodec<RegistryFriendlyByteBuf,OpenUi> CODEC = StreamCodec.of((buf,p) -> { buf.writeUtf(p.session,36); buf.writeByteArray(p.data); }, buf -> new OpenUi(buf.readUtf(36),buf.readByteArray(UiCompression.MAX_PACKED)));
        public Type<? extends CustomPacketPayload> type() { return TYPE; }
    }
    public record CloseUi(String session) implements CustomPacketPayload {
        public static final Type<CloseUi> TYPE = new Type<>(ResourceLocation.fromNamespaceAndPath("wysicraft","close"));
        public static final StreamCodec<RegistryFriendlyByteBuf,CloseUi> CODEC = StreamCodec.of((buf,p) -> buf.writeUtf(p.session,36),buf -> new CloseUi(buf.readUtf(36)));
        public Type<? extends CustomPacketPayload> type() { return TYPE; }
    }
}
