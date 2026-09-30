package com.wysicraft.runtime;

import com.wysicraft.runtime.api.*;
import com.wysicraft.runtime.model.Models;
import com.wysicraft.runtime.model.Models.*;
import com.wysicraft.runtime.model.Expressions;
import com.wysicraft.runtime.pack.PackRepository;
import com.wysicraft.runtime.network.Payloads;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.network.chat.Component;
import net.neoforged.neoforge.network.PacketDistributor;
import java.util.*;

public final class ServerRuntime {
    public final PackRepository packs = new PackRepository();
    private final Map<UUID,Session> sessions = new HashMap<>();
    private final Map<UUID,Map<String,Long>> cooldowns = new HashMap<>();
    private final ScriptBudget scriptBudget = new ScriptBudget();
    private int depth;
    private static final class Session {
        final String ui, token = UUID.randomUUID().toString(); final Map<String,String> state; final Ui definition; long tick; int count;
        Session(Ui ui) { this.ui = ui.id; definition = ui.copy(); state = new HashMap<>(ui.variables); }
    }
    public void forget(UUID player) { sessions.remove(player); cooldowns.remove(player); scriptBudget.forget(player); }
    /** Whether this player's open screen is still the one with this session token (item slot containers check it). */
    public boolean hasSession(ServerPlayer player, String token) { Session s = sessions.get(player.getUUID()); return s != null && s.token.equals(token); }
    /** Whether a control on the player's open screen is shown (or usable), by the same rules events are checked with. */
    private boolean shown(ServerPlayer player, String token, String id, boolean usable) {
        Session s = sessions.get(player.getUUID()); if (s == null || !s.token.equals(token)) return false;
        Element e = s.definition.element(id); if (e == null) return false;
        java.util.List<Element> chain = new java.util.ArrayList<>(com.wysicraft.runtime.model.ContainerTree.ancestors(s.definition, e)); chain.add(e);
        for (Element c : chain) {
            if (!c.visible || !Expressions.evaluate(c.visibleIf, s.state)) return false;
            if (usable && (!c.enabled || !Expressions.evaluate(c.enabledIf, s.state))) return false;
        }
        return true;
    }
    public void openRelative(ServerPlayer player, String id) {
        Session current=sessions.get(player.getUUID());
        if (!id.contains(":") && current!=null) id=current.ui.substring(0,current.ui.indexOf(':')+1)+id;
        open(player,id);
    }
    public void update(ServerPlayer player, String type, String target, String value) {
        WysicraftApi.requireServerThread(player);
        Session session = sessions.get(player.getUUID());
        if (session == null) throw new IllegalStateException("Player has no open Wysicraft screen");
        if (value.length() > 4096 || target.length() > 64) throw new IllegalArgumentException("UI update exceeds limits");
        if (type.equals("set_variable")) {
            if (!PackRepository.variable(target)) throw new IllegalArgumentException("Invalid variable");
            session.state.put(target,value);
        } else {
            Element element = session.definition.element(target);
            if (element == null) throw new IllegalArgumentException("Unknown element: " + target);
            switch (type) {
                case "set_text" -> element.text = value;
                case "set_value" -> { if(element.type.equals("item_list")) com.wysicraft.runtime.model.ItemRows.parse(value); element.value = value; }
                case "set_item" -> {
                    var item=net.minecraft.resources.ResourceLocation.tryParse(value);
                    if (!element.type.equals("item") || item==null || !net.minecraft.core.registries.BuiltInRegistries.ITEM.containsKey(item)) throw new IllegalArgumentException("Invalid item target or ID: "+target+" = "+value);
                    element.item=value;
                }
                case "set_visible" -> element.visible = Boolean.parseBoolean(value);
                case "set_enabled" -> element.enabled = Boolean.parseBoolean(value);
                default -> throw new IllegalArgumentException("Unsupported UI update");
            }
        }
        PacketDistributor.sendToPlayer(player,new Payloads.UpdateUi(session.token,type,target,value));
    }
    public void reload(net.minecraft.server.MinecraftServer server) {
        for (ServerPlayer player : server.getPlayerList().getPlayers()) { close(player); player.sendSystemMessage(Component.literal("Wysicraft packs reloaded; open interfaces were closed.")); }
        loadPacks();
        ProjectCommands.register(server.getCommands().getDispatcher());
        for (ServerPlayer player : server.getPlayerList().getPlayers()) server.getCommands().sendCommands(player);
    }
    public void loadPacks() {
        packs.reload(Wysicraft.packRoot(), (message,error) -> { if (error == null) Wysicraft.LOG.info(message); else Wysicraft.LOG.warn("{}: {}",message,error.toString()); }, dependency -> net.neoforged.fml.ModList.get().isLoaded(dependency), com.wysicraft.runtime.pack.EmbeddedPacks.paths());
    }
    public void closeProject(ServerPlayer player, String projectId) {
        Session session = sessions.get(player.getUUID());
        if (session == null) return;
        var pack = packs.pack(session.ui);
        if (pack != null && pack.manifest().id.equals(projectId)) close(player);
    }
    public void open(ServerPlayer player, String id) {
        if (depth >= 16) { Wysicraft.LOG.warn("UI action recursion limit"); return; }
        Ui ui = packs.get(id); if (ui == null) { player.sendSystemMessage(Component.literal("Unknown Wysicraft UI: " + id)); return; }
        depth++;
        try {
            // Replace the client screen directly: a CloseUi here briefly grabs and
            // recenters the mouse before OpenUi releases it again.
            close(player,false); Session session = new Session(ui); sessions.put(player.getUUID(),session);
            // Only presentation/client actions leave the server. Server code and commands stay on the host.
            Ui client = ui.copy(); client.events.values().forEach(e -> e.server = new Handler()); client.elements.forEach(e -> e.events.values().forEach(v -> v.server = new Handler()));
            byte[] data = com.wysicraft.runtime.network.UiCompression.pack(Models.JSON.toJson(client));
            PacketDistributor.sendToPlayer(player,new Payloads.OpenUi(session.token,data));
            // Item slots: the screen opens as a real container so the server moves the items (see WysicraftMenu).
            if (com.wysicraft.runtime.menu.WysicraftMenu.hasSlots(session.definition)) {
                String token = session.token;
                com.wysicraft.runtime.menu.WysicraftMenu.open(player, token, session.definition, control -> shown(player, token, control, false), control -> shown(player, token, control, true));
            }
            Event ev = ui.events.get("open"); if (ev != null) { mirror(session,ev.client); execute(player,session,ev.server); if (sessions.get(player.getUUID()) == session) navigate(player,ev.client); }
        } catch (Exception ex) { sessions.remove(player.getUUID()); Wysicraft.LOG.warn("Open UI {} failed: {}",id,ex.toString()); }
        finally { depth--; }
    }
    public void close(ServerPlayer player) {
        close(player,true);
    }
    private void close(ServerPlayer player, boolean notifyClient) {
        Session s = sessions.remove(player.getUUID()); if (s == null) return;
        if (notifyClient) PacketDistributor.sendToPlayer(player,new Payloads.CloseUi(s.token));
        // Its item slots close with it; anything left in its storage or crafting grid goes back to the player.
        if (player.containerMenu instanceof com.wysicraft.runtime.menu.WysicraftMenu menu && menu.token.equals(s.token)) player.closeContainer();
        Ui ui = packs.get(s.ui); if (ui != null && ui.events.containsKey("close") && depth < 16) { depth++; try { execute(player,s,ui.events.get("close").server); } finally { depth--; } }
    }
    public void event(ServerPlayer player, Payloads.UiEvent packet) {
        Session s = sessions.get(player.getUUID());
        if (s == null || !s.ui.equals(packet.ui()) || !s.token.equals(packet.session())) return;
        long tick = player.serverLevel().getGameTime(); if (s.tick != tick) { s.tick = tick; s.count = 0; } if (++s.count > 20) return;
        if (packet.element().isEmpty() && packet.event().equals("close")) { close(player); return; }
        if (!PackRepository.id(packet.element()) || !packet.event().matches("[a-z_]{1,32}") || packet.value().length() > 1024) return;
        Ui ui = s.definition;
        Element element = ui.element(packet.element());
        if (element == null || !element.visible || !element.enabled || !Expressions.evaluate(element.visibleIf,s.state) || !Expressions.evaluate(element.enabledIf,s.state)) return;
        for (Element parent : com.wysicraft.runtime.model.ContainerTree.ancestors(ui,element))
        if (parent != null && (!parent.visible || !parent.enabled || !Expressions.evaluate(parent.visibleIf,s.state) || !Expressions.evaluate(parent.enabledIf,s.state))) return;
        Event ev = element.events.get(packet.event()); if (ev == null || !PackRepository.events(element.type).contains(packet.event())) return;
        if (!validValue(element,packet.event(),packet.value())) return;
        if(!element.rowElements.isEmpty() && Set.of("item_primary","item_secondary").contains(packet.event()) && !com.wysicraft.runtime.model.RowTemplates.hasAction(element,packet.event(),s.state))return;
        if (!player.hasPermissions(ev.server.permissionLevel)) return;
        if (ev.server.cooldownTicks > 0 && !Set.of("text_changed","value_changed").contains(packet.event())) {
            var limits=cooldowns.computeIfAbsent(player.getUUID(),key -> new HashMap<>());
            limits.values().removeIf(until -> until<=tick);
            String key=s.ui+"/"+element.id+"/"+packet.event();
            if(limits.containsKey(key) || limits.size()>=1024) return;
            limits.put(key,tick+ev.server.cooldownTicks);
        }
        // Event input is state data only; it is never interpolated into a command.
        s.state.put("event_value",packet.value()); s.state.put("event_location",element.id+"/"+packet.event());
        try {
            mirror(s,ev.client);
            execute(player,s,ev.server);
            if (sessions.get(player.getUUID()) == s) navigate(player,ev.client);
        } catch (Exception ex) { Wysicraft.LOG.warn("Event {}/{}/{} failed: {}",s.ui,element.id,packet.event(),ex.toString()); }
    }
    private void navigate(ServerPlayer player, Handler handler) {
        for (Action a : handler.actions) { if (a.type.equals("open_ui")) { open(player,a.value); break; } if (a.type.equals("close_ui")) { close(player); break; } }
    }
    private void mirror(Session session, Handler handler) {
        for (Action a : handler.actions) {
            Element target = session.definition.element(a.target); String value = Expressions.bind(a.value,session.state);
            switch (a.type) {
                case "set_variable" -> session.state.put(a.target,value);
                case "toggle_variable" -> session.state.put(a.target,Boolean.toString(!Boolean.parseBoolean(session.state.get(a.target))));
                case "set_visible" -> { if (target != null) target.visible = Boolean.parseBoolean(value); }
                case "set_enabled" -> { if (target != null) target.enabled = Boolean.parseBoolean(value); }
                default -> { }
            }
        }
    }
    public static boolean validValue(Element e, String event, String value) {
        if(event.equals("item_click") || event.equals("item_primary") || event.equals("item_secondary")) return com.wysicraft.runtime.model.ItemRows.validEvent(e,event,value);
        if (event.equals("checked") || event.equals("unchecked")) return value.equals(event.equals("checked") ? "true" : "false");
        if (event.equals("text_changed") || event.equals("submit")) return value.length() <= 1024 && value.chars().noneMatch(c -> c == 0 || c == '\n' || c == '\r');
        if (event.equals("value_changed")) try { double v = Double.parseDouble(value); if (!Double.isFinite(v)) return false; return e.type.equals("dropdown") ? v == Math.rint(v) && v >= 0 && v < e.options.size() : v >= e.minimum && v <= e.maximum; } catch (NumberFormatException ex) { return false; }
        return value.isEmpty();
    }
    private void execute(ServerPlayer player, Session session, Handler handler) {
        if (!player.hasPermissions(handler.permissionLevel)) throw new SecurityException("This event requires permission level " + handler.permissionLevel);
        for (Action action : handler.actions) {
            switch (action.type) {
                case "command" -> {
                    if (action.value.contains("\n") || action.value.contains("\r") || action.value.length() > 2048) throw new SecurityException("Invalid command");
                    var source = Wysicraft.RUN_AS_SERVER.get() ? player.getServer().createCommandSourceStack() : player.createCommandSourceStack();
                    player.getServer().getCommands().performPrefixedCommand(source,action.value);
                }
                case "message" -> player.sendSystemMessage(Component.literal(Expressions.bind(action.value,session.state)));
                case "player_inventory" -> WysicraftApi.showPlayerInventory(player,action.target);
                case "set_variable" -> session.state.put(action.target,Expressions.bind(action.value,session.state));
                case "toggle_variable" -> session.state.put(action.target,Boolean.toString(!Boolean.parseBoolean(session.state.get(action.target))));
                case "open_ui" -> { open(player,action.value); return; }
                case "close_ui" -> { close(player); return; }
                case "server_function" -> { var fn = WysicraftApi.SERVER_FUNCTIONS.get(action.target); if (fn == null) throw new IllegalArgumentException("Unknown server function " + action.target); fn.accept(new WysicraftApi.ServerContext(player,session.state),action.value); }
                default -> { var fn = WysicraftApi.SERVER_ACTIONS.get(action.type); if (fn == null) throw new IllegalArgumentException("Unknown server action"); fn.accept(new WysicraftApi.ServerContext(player,session.state),action); }
            }
        }
        if (handler.script.isEmpty()) return;
        long tick = player.getServer().getTickCount();
        if (!scriptBudget.tryStart(player.getUUID(),tick)) {
            if (scriptBudget.shouldWarn(player.getUUID(),tick)) Wysicraft.LOG.warn("Server scripts for {} are throttled: too much script time in a short period ({})",player.getGameProfile().getName(),session.ui);
            return;
        }
        long started = System.nanoTime();
        try {
        Scripts.execute(Scripts.Side.SERVER,packs.pack(session.ui),handler,new Scripts.Context() {
            public String getVariable(String name) { return session.state.getOrDefault(name,""); }
            public void setVariable(String name,String value) { if (!PackRepository.variable(name) || value.length() > 4096) throw new IllegalArgumentException("Invalid variable"); session.state.put(name,value); }
            public void message(String text) { player.sendSystemMessage(Component.literal(text)); }
            public String value() { return session.state.getOrDefault("event_value",""); }
            public String text(String id) { var e=session.definition.element(id); return e==null?"":e.text; }
            public String query(String operation,String argument) {
                return switch(operation) {
                    case "player_name" -> player.getGameProfile().getName();
                    case "player_uuid" -> player.getUUID().toString();
                    case "player_position" -> Models.JSON.toJson(Map.of("x",player.getX(),"y",player.getY(),"z",player.getZ(),"dimension",player.level().dimension().location().toString()));
                    case "player_inventory" -> WysicraftApi.inventoryJson(player);
                    case "slot_items" -> WysicraftApi.slotsJson(player,argument);
                    case "slot_take" -> { var p=argument.trim().split("\\s+"); if(p.length<2) throw new IllegalArgumentException("takeFromSlot needs an element, an item and a count"); yield Integer.toString(WysicraftApi.takeFromSlots(player,p[0],p[1],p.length>2?Integer.parseInt(p[2]):1)); }
                    case "player_permission" -> { int level=Integer.parseInt(argument); if(level<0 || level>4) throw new IllegalArgumentException("Permission level must be 0–4"); yield Boolean.toString(player.hasPermissions(level)); }
                    default -> throw new IllegalArgumentException(operation);
                };
            }
            public void action(String type,String target,String value) {
                if(type.startsWith("console_")) { Wysicraft.LOG.info("[Server JS] {}",value); return; }
                switch(type) {
                    case "message" -> message(value);
                    case "command" -> { try { WysicraftApi.runCommand(player,value); } catch(Exception ex) { throw new IllegalArgumentException(ex.getMessage(),ex); } }
                    case "open_ui" -> openRelative(player,value);
                    case "close_ui" -> close(player);
                    case "set_text", "set_value", "set_visible", "set_enabled", "set_variable", "set_item" -> update(player,type,target,value);
                    default -> throw new IllegalArgumentException("Unsupported server script action: "+type);
                }
            }
        }, message -> Wysicraft.LOG.warn("Script: {} / {}: {}",session.ui,session.state.getOrDefault("event_location","screen event"),message));
        } finally { scriptBudget.charge(player.getUUID(),System.nanoTime()-started); }
    }
}



