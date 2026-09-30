package com.wysicraft.runtime.api;

import org.graalvm.polyglot.*;
import org.graalvm.polyglot.proxy.ProxyExecutable;
import org.graalvm.polyglot.io.IOAccess;
import java.util.*;
import java.util.concurrent.*;

/** Per-event JS scopes with no Java/IO access and bounded statements/output/time.
 * In-process execution does not provide a separate heap quota; install trusted packs.
 *
 * Every event gets its own context, so nothing carries over between them. The contexts share one engine, which caches
 * parsed sources: the parse happens on the first event and is reused after, instead of re-parsing the whole script
 * twenty times a second under a tick handler. Sharing an engine shares compiled code and nothing else — globals,
 * bindings and resource limits all stay per-context, which ScriptEngineSharingTests holds to. */
public final class ClientJavaScript implements Scripts.Provider {
    /** UTF-8 bytes. Past this a single event costs a sizeable share of a 50 ms tick even with the parse cached, and
     *  Scripts/PackRepository refuse the pack before it gets that far. Raise all three together, or not at all. */
    public static final int MAX_SCRIPT_BYTES = 256 * 1024;
    public static final ClientJavaScript INSTANCE = new ClientJavaScript();
    private static final ScheduledExecutorService TIMER = Executors.newSingleThreadScheduledExecutor(r -> { var t = new Thread(r,"wysicraft-js-timeout"); t.setDaemon(true); return t; });
    private static final Engine ENGINE = Engine.newBuilder().option("engine.WarnInterpreterOnly","false").build();
    /** Parsed sources, keyed by the script text. Bounded so a pack that swaps scripts cannot grow it without end;
     *  the engine's own cache is what does the work, this only avoids rebuilding the Source wrapper. */
    private static final Map<String,Source> SOURCES = Collections.synchronizedMap(new LinkedHashMap<>(16,0.75f,true) {
        protected boolean removeEldestEntry(Map.Entry<String,Source> eldest) { return size() > 64; }
    });
    private record Output(String type,String target,String value) {}
    public void execute(Scripts.Side side,String source,String function,Scripts.Context host) {
        if (source.getBytes(java.nio.charset.StandardCharsets.UTF_8).length > MAX_SCRIPT_BYTES || !function.matches("[a-zA-Z_][a-zA-Z0-9_]*|")) throw new IllegalArgumentException("Invalid script/function");
        Source parsed = SOURCES.computeIfAbsent(source,text -> Source.newBuilder("js",text,"script.js").cached(true).buildLiteral());
        List<Output> output = new ArrayList<>();
        Map<String,String> variables = new HashMap<>();
        try (var js = Context.newBuilder("js").engine(ENGINE).allowHostAccess(HostAccess.NONE).allowHostClassLookup(name -> false)
                .allowIO(IOAccess.NONE).allowCreateThread(false).allowNativeAccess(false)
                .allowEnvironmentAccess(EnvironmentAccess.NONE)
                .resourceLimits(ResourceLimits.newBuilder().statementLimit(100000,null).build()).build()) {
            js.getBindings("js").putMember("__bridge",(ProxyExecutable)args -> {
                String op = str(args,0), target = str(args,1), value = str(args,2);
                if (target.length()>256 || value.length()>4096) throw new IllegalArgumentException("Script value exceeds limit");
                switch (op) {
                    case "get_variable": return variables.containsKey(target) ? variables.get(target) : host.getVariable(target);
                    case "get_text": return host.text(target);
                    case "element": return host.elementId();
                    case "value": return host.value();
                    case "repeat": return host.repeat() ? "true" : "false";
                    case "is_server": return side == Scripts.Side.SERVER ? "true" : "false";
                    case "slot_items": return host.query(op,target);   // both sides: what a slots control holds right now
                    case "player_name", "player_uuid", "player_position", "player_inventory", "player_permission", "slot_take":
                        if (side != Scripts.Side.SERVER) throw new IllegalArgumentException("Player data requires a Server script");
                        return host.query(op,target);
                }
                if (output.size() >= 128) throw new IllegalArgumentException("Script output exceeds 128 operations");
                if (op.equals("set_variable")) variables.put(target,value);
                output.add(new Output(op,target,value)); return null;
            });
            js.eval(BOOTSTRAP_SOURCE);
            var timeout = TIMER.schedule(()->js.close(true),2,TimeUnit.SECONDS);
            try {
                js.eval(parsed);
                if (!function.isEmpty()) {
                    var callback = js.getBindings("js").getMember(function);
                    if (callback == null || !callback.canExecute()) throw new IllegalArgumentException("Function not found: " + function);
                    callback.execute(js.getBindings("js").getMember("ctx"));
                }
            } finally { timeout.cancel(false); }
        } catch (PolyglotException ex) {
            if (ex.isCancelled() || ex.isResourceExhausted()) throw new IllegalStateException("Client script exceeded execution budget");
            throw new IllegalArgumentException(ex.getMessage(),ex);
        }
        for (var item : output) host.action(item.type,item.target,item.value);
    }
    private static String str(Value[] args,int index) { return index >= args.length || args[index].isNull() ? "" : args[index].asString(); }
    private static final String BOOTSTRAP = """
        delete globalThis.Java; delete globalThis.Packages; delete globalThis.java;
        (function(b) {
          function emit(t,id,v) { b(t,String(id === undefined ? '' : id),String(v === undefined ? '' : v)); }
          function unsupported() { throw new Error('Use a built-in Server event action for navigation or server commands'); }
          var isServer=b('is_server')==='true';
          function setItem(id, resource) {
            if (typeof resource !== 'string' || !/^[a-z0-9_.-]+:[a-z0-9/._-]+$/.test(resource)) throw new Error('setItem requires a namespaced item ID, e.g. minecraft:diamond');
            emit('set_item', id, resource);
          }
          var ui = Object.freeze({
            setText:(id,v)=>emit('set_text',id,v), setValue:(id,v)=>emit('set_value',id,v),
            setItem:setItem, play:(id,clip)=>emit('set_value',id,clip),
            setItems:(id,items)=>emit('set_value',id,JSON.stringify(items)),
            setVisible:(id,v)=>emit('set_visible',id,!!v), setEnabled:(id,v)=>emit('set_enabled',id,!!v),
            changeTexture:(id,v)=>emit('change_texture',id,v),
            getVariable:n=>b('get_variable',n), setVariable:(n,v)=>emit('set_variable',n,v),
            getElement:id=>Object.freeze({id:id,text:b('get_text',id),setText:v=>emit('set_text',id,v),setItem:v=>setItem(id,v)}),
            close:()=>emit('close_ui','',''), open:isServer?(id=>emit('open_ui','',id)):unsupported,
            getSlots:id=>JSON.parse(b('slot_items',String(id)))
          });
          this.ui=ui;
          this.ctx=Object.freeze({ui:ui,elementId:b('element'),value:b('value'),repeat:b('repeat')==='true',
            state:Object.freeze({get:ui.getVariable,set:ui.setVariable}),
            getVariable:ui.getVariable,setVariable:ui.setVariable,
            message:v=>emit('message','',v),
            client:Object.freeze({sendMessage:v=>emit('message','',v),playSound:v=>emit('play_sound','',v)}),
            player:isServer?Object.freeze({getName:()=>b('player_name'),getUuid:()=>b('player_uuid'),getPosition:()=>JSON.parse(b('player_position')),getInventory:()=>JSON.parse(b('player_inventory')),hasPermission:level=>b('player_permission',String(level))==='true',
              getSlots:id=>JSON.parse(b('slot_items',String(id))),
              takeFromSlot:(id,item,count)=>Number(b('slot_take',String(id)+' '+String(item)+' '+String(count===undefined?1:count)))}):undefined,
            server:Object.freeze({runCommand:isServer?(command=>emit('command','',command)):unsupported,sendMessage:isServer?(text=>emit('message','',text)):unsupported})
          });
          function logger(level) { return function() { var parts=[]; for(var i=0;i<arguments.length;i++) parts.push(typeof arguments[i]==='string'?arguments[i]:JSON.stringify(arguments[i])); emit('console_'+level,'',parts.join(' ')); }; }
          this.console=Object.freeze({log:logger('log'),warn:logger('warn'),error:logger('error')});
        })(__bridge);
        delete this.__bridge;
        """;
    // Declared after BOOTSTRAP so it reads in initialisation order rather than relying on constant folding.
    private static final Source BOOTSTRAP_SOURCE = Source.newBuilder("js",BOOTSTRAP,"wysicraft-bootstrap.js").cached(true).buildLiteral();
}
