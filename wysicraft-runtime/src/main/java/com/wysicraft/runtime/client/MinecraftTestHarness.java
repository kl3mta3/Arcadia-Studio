package com.wysicraft.runtime.client;

import com.wysicraft.runtime.*;
import com.wysicraft.runtime.model.Models;
import com.wysicraft.runtime.pack.PackRepository;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.TitleScreen;
import net.minecraft.core.registries.Registries;
import net.minecraft.world.Difficulty;
import net.minecraft.world.level.*;
import net.minecraft.world.level.levelgen.WorldOptions;
import net.minecraft.world.level.levelgen.presets.WorldPresets;
import net.neoforged.neoforge.common.NeoForge;
import net.neoforged.neoforge.client.event.ClientTickEvent;
import java.nio.file.*;
import java.util.*;
import java.util.concurrent.atomic.AtomicBoolean;

/** Local file control channel, opt-in development client only. Never used on multiplayer servers. */
public final class MinecraftTestHarness {
    private static final String WORLD = "Wysicraft Test";
    private static final boolean LOCAL_SERVER = "127.0.0.1:25579".equals(System.getProperty("wysicraft.localTestServer"));
    private static boolean started, catalogWritten;
    private static long nextPoll;
    private static final AtomicBoolean pending = new AtomicBoolean();
    private static String lastRequest = "", lastResult = "", lastError = "";
    private record Session(String token, String project) {}
    private record Request(String token, String id, String kind, String command) {}
    private static volatile String activeUi = "";
    private static volatile Map<String,String> texts = Map.of();
    private static volatile Map<String,String> values = Map.of();
    private record State(String token, String project, boolean ready, List<String> commands, Map<String,String> usage, String requestId, String result, String error, String activeUi, Map<String,String> texts, Map<String,String> values) {}
    public static void install(net.neoforged.bus.api.IEventBus bus) { NeoForge.EVENT_BUS.addListener(MinecraftTestHarness::tick); MinecraftTestControls.install(bus); }
    static String project() {
        try { var session = read(root().resolve("session.json"),Session.class); return session != null && PackRepository.id(session.project) ? session.project : ""; }
        catch (Exception ex) { return ""; }
    }
    private static Path root() { return Minecraft.getInstance().gameDirectory.toPath().resolve(".wysicraft-test"); }
    private static <T> T read(Path path, Class<T> type) throws Exception {
        if (Files.size(path) > 16384) throw new IllegalArgumentException("Test control file exceeds limit");
        return Models.JSON.fromJson(Files.readString(path),type);
    }
    private static void state(Session session, boolean ready, List<String> commands, Map<String,String> usage) {
        try {
            Path temp = root().resolve("state.tmp");
            Files.writeString(temp,Models.JSON.toJson(new State(session.token,session.project,ready,commands,usage,lastRequest,lastResult,lastError,activeUi,texts,values)));
            Files.move(temp,root().resolve("state.json"),StandardCopyOption.REPLACE_EXISTING);
        } catch (Exception ex) { Wysicraft.LOG.debug("Test state: {}",ex.toString()); }
    }
    private static void tick(ClientTickEvent.Post event) {
        var mc = Minecraft.getInstance();
        mc.options.pauseOnLostFocus = false;
        activeUi = DynamicScreen.of(mc.screen) instanceof DynamicScreen screen ? screen.ui.id : "";
        if (DynamicScreen.of(mc.screen) instanceof DynamicScreen screen) {
            var snapshot = new LinkedHashMap<String,String>();
            var data = new LinkedHashMap<String,String>();
            for (var element : screen.ui.elements) { snapshot.put(element.id,element.text); data.put(element.id,element.type.equals("item")?element.item:element.value); }
            texts = Map.copyOf(snapshot);
            values=Map.copyOf(data);
        } else { texts = Map.of();values=Map.of(); }
        // A paused integrated server cannot complete its queued status task.
        // Let an incoming desktop request unpause it before checking pending.
        if (pending.get()) {
            if (mc.isPaused() && Files.exists(root().resolve("request.json"))) mc.setScreen(null);
            return;
        }
        if (System.currentTimeMillis() < nextPoll) return;
        nextPoll = System.currentTimeMillis()+500;
        Session session;
        try { session = read(root().resolve("session.json"),Session.class); }
        catch (Exception ex) { return; }
        if (session == null || session.token == null || !session.token.matches("[a-f0-9-]{36}") || !PackRepository.id(session.project)) return;
        if (!started && mc.screen instanceof TitleScreen) {
            started = true;
            try {
                if (LOCAL_SERVER) {
                    var address=net.minecraft.client.multiplayer.resolver.ServerAddress.parseString("127.0.0.1:25579");
                    var data=new net.minecraft.client.multiplayer.ServerData("Wysicraft local release check","127.0.0.1:25579",net.minecraft.client.multiplayer.ServerData.Type.OTHER);
                    net.minecraft.client.gui.screens.ConnectScreen.startConnecting(new TitleScreen(),mc,address,data,false,null);
                } else if (Files.isRegularFile(mc.gameDirectory.toPath().resolve("saves").resolve(WORLD).resolve("level.dat"))) {
                    mc.createWorldOpenFlows().openWorld(WORLD,() -> mc.setScreen(new TitleScreen()));
                } else {
                    var rules = new GameRules();
                    rules.getRule(GameRules.RULE_DOMOBSPAWNING).set(false,null);
                    rules.getRule(GameRules.RULE_DAYLIGHT).set(false,null);
                    rules.getRule(GameRules.RULE_WEATHER_CYCLE).set(false,null);
                    rules.getRule(GameRules.RULE_SPAWN_CHUNK_RADIUS).set(0,null);
                    var settings = new LevelSettings(WORLD,GameType.CREATIVE,false,Difficulty.PEACEFUL,true,rules,WorldDataConfiguration.DEFAULT);
                    mc.createWorldOpenFlows().createFreshLevel(WORLD,settings,new WorldOptions(42L,false,false), access -> access.registryOrThrow(Registries.WORLD_PRESET).getHolderOrThrow(WorldPresets.FLAT).value().createWorldDimensions(), new TitleScreen());
                }
            } catch (Exception ex) { lastError = "Test world: " + ex.getMessage(); }
        }
        Request request = null;
        try {
            Path path = root().resolve("request.json");
            if (Files.exists(path)) { request = read(path,Request.class); Files.delete(path); }
        } catch (Exception ex) { lastError = ex.getMessage(); }
        if (request != null && (!session.token.equals(request.token) || request.id == null || request.id.equals(lastRequest))) request = null;
        if (request != null && "stop".equals(request.kind)) { mc.stop(); return; }
        if(request!=null && "capture".equals(request.kind)) {
            lastRequest=request.id;
            net.minecraft.client.Screenshot.grab(mc.gameDirectory,"wysicraft-test.png",mc.getMainRenderTarget(),message -> lastResult="Screenshot saved: screenshots/wysicraft-test.png");
            request=null;
        }
        if (request != null && "key".equals(request.kind) && mc.getSingleplayerServer()!=null) {
            int key=Integer.parseInt(request.command);
            if(mc.screen==null) {
                net.minecraft.client.KeyMapping.click(com.mojang.blaze3d.platform.InputConstants.Type.KEYSYM.getOrCreate(key));
                // Deliver synthetic input before the unfocused window clears keys.
                MinecraftTestControls.consumeKeys();
            }
            else {
                var keyEvent=new net.neoforged.neoforge.client.event.ScreenEvent.KeyPressed.Pre(mc.screen,key,0,0);
                NeoForge.EVENT_BUS.post(keyEvent);
                if(!keyEvent.isCanceled()) mc.screen.keyPressed(key,0,0);
            }
            lastRequest=request.id; lastResult="Test key " + key + " on " + (mc.screen==null?"world":mc.screen.getClass().getSimpleName()); request=null;
        }
        if (request != null && "click".equals(request.kind) && (mc.getSingleplayerServer() != null || LOCAL_SERVER)) {
            lastRequest = request.id;
            if (DynamicScreen.of(mc.screen) instanceof DynamicScreen screen && screen.ui.element(request.command) != null) {
                var element = screen.ui.element(request.command);
                screen.testClick(element);
                lastResult = "Clicked " + request.command;
            } else lastError = "No active UI element: " + request.command;
            request = null;
        }
        if(request!=null && "row_click".equals(request.kind) && DynamicScreen.of(mc.screen) instanceof DynamicScreen screen) {
            String[] parts=request.command.split(":");
            try { var element=screen.ui.element(parts[0]); if(element==null || !element.type.equals("item_list")) throw new IllegalArgumentException("Expected Item List"); screen.testRowClick(element,Integer.parseInt(parts[2]),parts[1].equals("secondary")); lastResult="Clicked row "+request.command; } catch(Exception ex) {lastError=ex.getMessage();}
            lastRequest=request.id; request=null;
        }
        if(request!=null && "type".equals(request.kind) && DynamicScreen.of(mc.screen) instanceof DynamicScreen screen) {
            String[] parts=request.command.split(":",2); var element=screen.ui.element(parts[0]);
            if(element!=null && element.type.equals("textbox") && parts.length==2) {screen.testClick(element); for(char c:parts[1].toCharArray()) screen.charTyped(c,0);}
            lastRequest=request.id; request=null;
        }
        if (request != null && mc.isPaused()) mc.setScreen(null);
        var server = mc.getSingleplayerServer();
        if(LOCAL_SERVER && server==null && mc.player!=null) {
            var commands=List.of(session.project+".open",session.project+".close");
            if(request!=null) {
                lastRequest=request.id;lastError="";
                if("command".equals(request.kind) && commands.contains(request.command)) {mc.player.connection.sendCommand(request.command);lastResult="Sent /"+request.command;}
                else lastError="Local release test only accepts project open/close commands";
            }
            state(session,true,commands,Map.of());return;
        }
        if (server == null || mc.player == null) { state(session,false,List.of(),Map.of()); return; }
        if(!catalogWritten)try {
            var items=new ArrayList<Map<String,String>>();
            for(var item:net.minecraft.core.registries.BuiltInRegistries.ITEM)if(item!=net.minecraft.world.item.Items.AIR)items.add(Map.of("id",net.minecraft.core.registries.BuiltInRegistries.ITEM.getKey(item).toString(),"name",item.getDescription().getString()));
            Files.writeString(root().resolve("items.tmp"),Models.JSON.toJson(items));Files.move(root().resolve("items.tmp"),root().resolve("items.json"),StandardCopyOption.REPLACE_EXISTING);catalogWritten=true;
        }catch(Exception ex){Wysicraft.LOG.debug("Item catalog: {}",ex.toString());}
        UUID playerId = mc.player.getUUID();
        Request input = request;
        pending.set(true);
        server.execute(() -> {
            try {
                var player = server.getPlayerList().getPlayer(playerId);
                if (player == null) { state(session,false,List.of(),Map.of()); return; }
                var source = player.createCommandSourceStack();
                var dispatcher = server.getCommands().getDispatcher();
                var commands = ProjectCommands.available(dispatcher,source,session.project);
                if (input != null) {
                    lastRequest = input.id; lastResult = ""; lastError = "";
                    if ("reload".equals(input.kind)) {
                        server.reloadResources(server.getPackRepository().getSelectedIds()).whenComplete((unused,error) -> server.execute(() -> {
                            if (error != null) lastError = error.toString();
                            else { Wysicraft.SERVER.reload(server); lastResult = "KubeJS and Wysicraft reloaded. Use .open to test."; }
                        }));
                        lastResult = "Reload requested";
                    } else if ("command".equals(input.kind)) {
                        String command = input.command == null ? "" : input.command.strip();
                        if (command.length() > 2048 || command.contains("\n") || command.contains("\r") || !commands.contains(command.split("\\s+",2)[0])) throw new IllegalArgumentException("Choose a registered command belonging to this project");
                        int result = dispatcher.execute(command,source);
                        lastResult = "/" + command + " completed (result " + result + ")";
                        Wysicraft.LOG.info("Wysicraft TEST: {}",lastResult);
                    }
                }
                var usage = new LinkedHashMap<String,String>();
                for (String command : commands) {
                    var node = dispatcher.getRoot().getChild(command);
                    usage.put(command,String.join(" | ",dispatcher.getSmartUsage(node,source).values()));
                }
                state(session,true,commands,usage);
            } catch (Exception ex) { lastError = ex.getMessage(); state(session,true,List.of(),Map.of()); Wysicraft.LOG.warn("Wysicraft TEST: {}",ex.toString()); }
            finally { pending.set(false); }
        });
    }
}
