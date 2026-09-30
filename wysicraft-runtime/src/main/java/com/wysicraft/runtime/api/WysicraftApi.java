package com.wysicraft.runtime.api;
import com.wysicraft.runtime.Wysicraft;
import com.wysicraft.runtime.model.Models.Action;
import com.wysicraft.runtime.pack.PackRepository;
import net.minecraft.server.level.ServerPlayer;
import java.util.*;
import java.util.function.BiConsumer;

public final class WysicraftApi {
    private WysicraftApi() {}
    public record ServerContext(ServerPlayer player, Map<String,String> state) {}
    public static final Map<String,BiConsumer<ServerContext,Action>> SERVER_ACTIONS = new HashMap<>();
    public static final Map<String,BiConsumer<ServerContext,String>> SERVER_FUNCTIONS = new java.util.concurrent.ConcurrentHashMap<>();
    public static void openUi(ServerPlayer player, String id) { requireServerThread(player); Wysicraft.SERVER.openRelative(player, id); }
    public static void closeUi(ServerPlayer player) { requireServerThread(player); Wysicraft.SERVER.close(player); }
    public static void openProject(ServerPlayer player, String projectId) {
        requireServerThread(player);
        var pack = Wysicraft.SERVER.packs.all().values().stream().filter(p -> p.manifest().id.equals(projectId)).findFirst().orElseThrow(() -> new IllegalArgumentException("Unknown project: " + projectId));
        openUi(player, projectId+":"+pack.manifest().defaultUi);
    }
    public static void closeProject(ServerPlayer player, String projectId) { requireServerThread(player); Wysicraft.SERVER.closeProject(player, projectId); }
    public static void registerProjectCommand(String projectId, String command) { com.wysicraft.runtime.ProjectCommands.associate(projectId,command); }
    // Always the player's own permissions, even with runCommandsAsServer: script-built commands can include
    // client-supplied text (ctx.value), so they must never gain server authority. Built-in command actions are fixed pack text.
    public static int runCommand(ServerPlayer player, String command) throws com.mojang.brigadier.exceptions.CommandSyntaxException {
        requireServerThread(player);
        if (command.length() > 2048 || command.contains("\n") || command.contains("\r")) throw new IllegalArgumentException("Invalid command");
        return player.getServer().getCommands().getDispatcher().execute(command.startsWith("/") ? command.substring(1) : command, player.createCommandSourceStack());
    }
    public static void message(ServerPlayer player, String text) { requireServerThread(player); player.sendSystemMessage(net.minecraft.network.chat.Component.literal(text)); }
    public static void setText(ServerPlayer player, String id, String text) { Wysicraft.SERVER.update(player,"set_text",id,text); }
    public static void setValue(ServerPlayer player, String id, String value) { Wysicraft.SERVER.update(player,"set_value",id,value); }
    public static void setItem(ServerPlayer player, String id, String item) { Wysicraft.SERVER.update(player,"set_item",id,item); }
    public static void setItems(ServerPlayer player, String id, String json) { com.wysicraft.runtime.model.ItemRows.parse(json); setValue(player,id,json); }
    public static String inventoryJson(ServerPlayer player) {
        requireServerThread(player);
        var rows=new ArrayList<com.wysicraft.runtime.model.ItemRows.Row>();
        for(var stack:player.getInventory().items) if(!stack.isEmpty()) {
            String name=stack.getHoverName().getString(); if(name.length()>32) name=name.substring(0,32);
            rows.add(new com.wysicraft.runtime.model.ItemRows.Row(net.minecraft.core.registries.BuiltInRegistries.ITEM.getKey(stack.getItem()).toString(),stack.getCount(),name));
        }
        String json=com.wysicraft.runtime.model.Models.JSON.toJson(rows);
        if(json.length()>4096) throw new IllegalArgumentException("Inventory display data exceeds 4096 characters; use a smaller page");
        return json;
    }
    public static void showPlayerInventory(ServerPlayer player,String id) { setItems(player,id,inventoryJson(player)); }
    /** [{item,count,name}] per cell of a slots control on the player's open screen (null for an empty cell); [] without such a screen or control. */
    public static String slotsJson(ServerPlayer player, String element) {
        requireServerThread(player);
        if (!(player.containerMenu instanceof com.wysicraft.runtime.menu.WysicraftMenu menu) || !Wysicraft.SERVER.hasSession(player, menu.token)) return "[]";
        var rows = new ArrayList<Object>();
        for (var stack : menu.stacksOf(element)) rows.add(stack.isEmpty() ? null : slotRow(stack));
        return com.wysicraft.runtime.model.Models.JSON.toJson(rows);
    }
    /** Removes up to count of an item from a slots control on the player's open screen; returns how many were removed. */
    public static int takeFromSlots(ServerPlayer player, String element, String item, int count) {
        requireServerThread(player);
        if (count < 1 || count > 3456) throw new IllegalArgumentException("Count must be 1-3456");
        if (!(player.containerMenu instanceof com.wysicraft.runtime.menu.WysicraftMenu menu) || !Wysicraft.SERVER.hasSession(player, menu.token)) return 0;
        var id = net.minecraft.resources.ResourceLocation.tryParse(item);
        if (id == null || !net.minecraft.core.registries.BuiltInRegistries.ITEM.containsKey(id)) throw new IllegalArgumentException("Unknown item: " + item);
        int taken = menu.takeFrom(element, stack -> net.minecraft.core.registries.BuiltInRegistries.ITEM.getKey(stack.getItem()).equals(id), count);
        if (taken > 0) menu.broadcastChanges();
        return taken;
    }
    private static com.wysicraft.runtime.model.ItemRows.Row slotRow(net.minecraft.world.item.ItemStack stack) {
        String name = stack.getHoverName().getString(); if (name.length() > 32) name = name.substring(0, 32);
        return new com.wysicraft.runtime.model.ItemRows.Row(net.minecraft.core.registries.BuiltInRegistries.ITEM.getKey(stack.getItem()).toString(), stack.getCount(), name);
    }
    public static void setVisible(ServerPlayer player, String id, boolean visible) { Wysicraft.SERVER.update(player,"set_visible",id,Boolean.toString(visible)); }
    public static void setEnabled(ServerPlayer player, String id, boolean enabled) { Wysicraft.SERVER.update(player,"set_enabled",id,Boolean.toString(enabled)); }
    public static void setVariable(ServerPlayer player, String name, String value) { Wysicraft.SERVER.update(player,"set_variable",name,value); }
    public static void requireServerThread(ServerPlayer player) { if (player.getServer() == null || !player.getServer().isSameThread()) throw new IllegalStateException("Call Wysicraft from a server event/thread"); }
    public static void registerKubeHandler(String projectId, String key, BiConsumer<ServerContext,String> handler) {
        if (!PackRepository.id(projectId) || !key.matches("[a-f0-9]{64}")) throw new IllegalArgumentException("Invalid KubeJS handler ID");
        SERVER_FUNCTIONS.put(projectId + ":kube:" + key, Objects.requireNonNull(handler));
    }
    public static void clearKubeHandlers(String projectId) {
        if (!PackRepository.id(projectId)) throw new IllegalArgumentException("Invalid project ID");
        SERVER_FUNCTIONS.keySet().removeIf(key -> key.startsWith(projectId + ":kube:"));
    }
    public static void registerServerAction(String id, BiConsumer<ServerContext,Action> handler) { if (SERVER_ACTIONS.putIfAbsent(id,handler) != null) throw new IllegalArgumentException("Duplicate action"); PackRepository.SERVER_ACTIONS.add(id); }
    public static void registerServerFunction(String id, BiConsumer<ServerContext,String> handler) { if (SERVER_FUNCTIONS.putIfAbsent(id,handler) != null) throw new IllegalArgumentException("Duplicate function"); }
}
