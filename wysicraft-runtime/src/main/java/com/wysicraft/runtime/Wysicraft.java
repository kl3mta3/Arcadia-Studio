package com.wysicraft.runtime;

import com.mojang.brigadier.arguments.StringArgumentType;
import com.mojang.logging.LogUtils;
import com.wysicraft.runtime.network.Payloads;
import net.minecraft.commands.Commands;
import net.minecraft.commands.SharedSuggestionProvider;
import net.minecraft.commands.arguments.EntityArgument;
import net.minecraft.network.chat.Component;
import net.minecraft.server.level.ServerPlayer;
import net.neoforged.bus.api.IEventBus;
import net.neoforged.fml.common.Mod;
import net.neoforged.fml.ModContainer;
import net.neoforged.fml.config.ModConfig;
import net.neoforged.fml.loading.FMLPaths;
import net.neoforged.neoforge.common.NeoForge;
import net.neoforged.neoforge.common.ModConfigSpec;
import net.neoforged.neoforge.event.RegisterCommandsEvent;
import net.neoforged.neoforge.event.server.ServerStartedEvent;
import net.neoforged.neoforge.event.entity.player.PlayerEvent;
import net.neoforged.neoforge.network.event.RegisterPayloadHandlersEvent;
import org.slf4j.Logger;
import java.nio.file.Path;

@Mod("wysicraft")
public final class Wysicraft {
    public static final Logger LOG = LogUtils.getLogger();
    public static final ServerRuntime SERVER = new ServerRuntime();
    public static final ModConfigSpec.BooleanValue RUN_AS_SERVER;
    static final ModConfigSpec CONFIG;
    static { var builder = new ModConfigSpec.Builder(); RUN_AS_SERVER = builder.comment("Dangerous: execute trusted pack commands as the server. Default false preserves player permissions.").define("runCommandsAsServer",false); CONFIG = builder.build(); }
    public Wysicraft(IEventBus bus, ModContainer container) {
        container.registerConfig(ModConfig.Type.SERVER,CONFIG);
        com.wysicraft.runtime.menu.WysicraftMenu.MENUS.register(bus);
        if (net.neoforged.fml.loading.FMLEnvironment.dist == net.neoforged.api.distmarker.Dist.CLIENT) com.wysicraft.runtime.client.SlotScreen.register(bus);
        bus.addListener(this::payloads);
        NeoForge.EVENT_BUS.addListener(this::commands);
        NeoForge.EVENT_BUS.addListener((ServerStartedEvent event) -> SERVER.reload(event.getServer()));
        NeoForge.EVENT_BUS.addListener((PlayerEvent.PlayerLoggedOutEvent event) -> SERVER.forget(event.getEntity().getUUID()));
        if (Boolean.getBoolean("wysicraft.testHarness") && net.neoforged.fml.loading.FMLEnvironment.dist == net.neoforged.api.distmarker.Dist.CLIENT)
            com.wysicraft.runtime.client.MinecraftTestHarness.install(bus);
    }
    public static Path packRoot() { return FMLPaths.GAMEDIR.get().resolve("wysicraft"); }
    private void payloads(RegisterPayloadHandlersEvent event) {
        var registrar = event.registrar("3");
        registrar.playToServer(Payloads.UiEvent.TYPE,Payloads.UiEvent.CODEC,(p,ctx) -> { if (ctx.player() instanceof ServerPlayer player) SERVER.event(player,p); });
        registrar.playToClient(Payloads.OpenUi.TYPE,Payloads.OpenUi.CODEC,(p,ctx) -> com.wysicraft.runtime.client.ClientRuntime.open(p));
        registrar.playToClient(Payloads.CloseUi.TYPE,Payloads.CloseUi.CODEC,(p,ctx) -> com.wysicraft.runtime.client.ClientRuntime.close(p));
        registrar.playToClient(Payloads.UpdateUi.TYPE,Payloads.UpdateUi.CODEC,(p,ctx) -> com.wysicraft.runtime.client.ClientRuntime.update(p));
    }
    private void commands(RegisterCommandsEvent event) {
        var root = Commands.literal("wysicraft")
            .then(Commands.literal("list").executes(ctx -> { ctx.getSource().sendSuccess(() -> Component.literal("Wysicraft UIs: " + String.join(", ",SERVER.packs.all().keySet())),false); return SERVER.packs.all().size(); }))
            .then(Commands.literal("reload").requires(source -> source.hasPermission(2)).executes(ctx -> { SERVER.reload(ctx.getSource().getServer()); return 1; }))
            .then(Commands.literal("open").then(Commands.argument("ui",StringArgumentType.word()).suggests((ctx,builder) -> SharedSuggestionProvider.suggest(SERVER.packs.all().keySet(),builder))
                .executes(ctx -> { SERVER.open(ctx.getSource().getPlayerOrException(),StringArgumentType.getString(ctx,"ui")); return 1; })
                .then(Commands.argument("player",EntityArgument.player()).requires(source -> source.hasPermission(2)).executes(ctx -> { SERVER.open(EntityArgument.getPlayer(ctx,"player"),StringArgumentType.getString(ctx,"ui")); return 1; }))));
        var node = event.getDispatcher().register(root); event.getDispatcher().register(Commands.literal("wui").redirect(node));
        SERVER.loadPacks();
        ProjectCommands.register(event.getDispatcher());
    }
}
