package com.wysicraft.runtime.client;

import com.wysicraft.runtime.Wysicraft;
import com.wysicraft.runtime.api.Scripts;
import com.wysicraft.runtime.model.Models.*;
import com.wysicraft.runtime.model.Expressions;
import com.wysicraft.runtime.network.Payloads;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.client.resources.sounds.SimpleSoundInstance;
import net.neoforged.neoforge.network.PacketDistributor;
import org.lwjgl.glfw.GLFW;
import java.util.*;
import java.util.function.BiConsumer;

public final class DynamicScreen extends Screen {
    private final Ui design; public final Ui ui; public final String session; public boolean remoteClosing;
    public final Map<String,String> state;
    public static final Map<String,BiConsumer<DynamicScreen,Action>> ACTIONS = new HashMap<>();
    private final Map<String,Integer> scroll = new HashMap<>();
    private float viewScale=1;
    private record RowsCache(String json,java.util.List<com.wysicraft.runtime.model.ItemRows.Row> rows,Map<Integer,List<Element>> templates) {}
    private final Map<String,RowsCache> itemRows=new HashMap<>();
    java.util.List<com.wysicraft.runtime.model.ItemRows.Row> rows(Element element) {
        var cache=itemRows.get(element.id);
        if(cache==null || !cache.json.equals(element.value)) { cache=new RowsCache(element.value,com.wysicraft.runtime.model.ItemRows.parse(element.value),new HashMap<>());itemRows.put(element.id,cache);scroll.put(element.id,Math.clamp(listScroll(element.id),0,Math.max(0,cache.rows.size()*element.rowHeight-(int)element.bounds.height))); }
        return cache.rows;
    }
    List<Element> rowDisplay(Element list,int index) { rows(list); var cache=itemRows.get(list.id);return cache.templates.computeIfAbsent(index,i->list.rowElements.stream().map(e->com.wysicraft.runtime.model.RowTemplates.bind(e,cache.rows.get(i),i)).toList()); }
    int listScroll(String id) { return scroll.getOrDefault(id,0); }
    void testClick(Element element) { mouseClicked((x(element)+1)*viewScale,(y(element)+1)*viewScale,0); }
    void testRowClick(Element e,int index,boolean secondary) {
        if(!e.rowElements.isEmpty()) {
            var button=e.rowElements.stream().filter(v->v.rowAction.equals(secondary?"item_secondary":"item_primary")).findFirst().orElseThrow();
            mouseClicked((x(e)+button.bounds.x+button.bounds.width/2)*viewScale,(y(e)+index*e.rowHeight-listScroll(e.id)+button.bounds.y+button.bounds.height/2)*viewScale,0);return;
        }
        int bw=Math.max(32,Math.min(62,(int)e.bounds.width/5));
        mouseClicked((x(e)+e.bounds.width-6-bw*(secondary?0.5:1.5))*viewScale,(y(e)+index*e.rowHeight-listScroll(e.id)+8)*viewScale,0);
    }
    Element focused, hovered, dragging; private int originX, originY; private boolean opened, closed; private long lastHover, lastTick;
    /** Set when this screen has item slots: the container screen that shows it (and owns opening and closing). */
    SlotScreen host;
    int originX() { return originX; }
    int originY() { return originY; }
    /** The Arcadia Studio screen being shown, whether on its own or inside a container screen with item slots. */
    public static DynamicScreen of(net.minecraft.client.gui.screens.Screen screen) { return screen instanceof DynamicScreen d ? d : screen instanceof SlotScreen s ? s.inner : null; }
    /** A close_ui action: a screen with item slots closes its container too. */
    void requestClose() { if (host != null) host.onClose(); else onClose(); }
    public DynamicScreen(Ui ui, String session) { super(Component.literal(ui.title)); this.ui = ui; this.design=ui.copy(); this.session = session; state = new HashMap<>(ui.variables); }
    public Font font() { return font; }
    public String bind(String text) { return Expressions.bind(text,state); }
    public static void registerAction(String id, BiConsumer<DynamicScreen,Action> action) { if (ACTIONS.putIfAbsent(id,action) != null) throw new IllegalArgumentException("Duplicate action"); com.wysicraft.runtime.pack.PackRepository.CLIENT_ACTIONS.add(id); }
    @Override protected void init() {
        com.wysicraft.runtime.model.ResponsiveLayout.apply(ui,design,width-12,height-(ui.showFrame?32:12));itemRows.clear();
        for(var e:ui.elements) {
            int max=e.type.equals("item_list")?Math.max(0,rows(e).size()*e.rowHeight-(int)e.bounds.height):e.type.equals("scroll_panel")?Math.max(0,(int)(ui.elements.stream().filter(c->c.parent.equals(e.id)).mapToDouble(c->c.bounds.y+c.bounds.height).max().orElse(e.bounds.y+e.bounds.height)-e.bounds.y-e.bounds.height)):0;
            if(scroll.containsKey(e.id))scroll.put(e.id,Math.clamp(scroll.get(e.id),0,max));
        }
        viewScale=host!=null||ui.responsive?1f:ui.fitToScreen?Math.min(1f,Math.min(width/(ui.size.width+12f),height/(ui.size.height+(ui.showFrame?32f:12f)))):1f;
        originX=(int)((width/viewScale-ui.size.width)/2); originY=(int)((height/viewScale-ui.size.height+(ui.showFrame?14:0))/2);
        if (!opened) { opened = true; lastTick = openedAt = System.currentTimeMillis(); fire(null,"open",""); }
    }
    // Screen timer: fires the Tick event every tickInterval ms while the screen is open (checked each client tick, 50 ms).
    @Override public void tick() {
        super.tick();
        tickSounds();
        if (ui.tickInterval < 50 || !ui.events.containsKey("tick")) return;
        long now = System.currentTimeMillis();
        if (now - lastTick >= ui.tickInterval) { lastTick = now; fire(null,"tick",""); }
    }
    @Override public boolean isPauseScreen() { return false; }
    // Asked for several times per element per frame; a screen's parent chains only change when its element
    // list does, so they are worked out once and kept until then.
    private final Map<String,java.util.List<Element>> parentChains=new HashMap<>();
    private int parentChainCount=-1;
    java.util.List<Element> parents(Element e) {
        if(parentChainCount!=ui.elements.size()) { parentChains.clear(); parentChainCount=ui.elements.size(); }
        return parentChains.computeIfAbsent(e.id,id->com.wysicraft.runtime.model.ContainerTree.ancestors(ui,e));
    }
    boolean visible(Element e) { return e.visible && Expressions.evaluate(e.visibleIf,state) && parents(e).stream().allMatch(p->p.visible && Expressions.evaluate(p.visibleIf,state)); }
    boolean enabled(Element e) { return e.enabled && Expressions.evaluate(e.enabledIf,state) && parents(e).stream().allMatch(p->p.enabled && Expressions.evaluate(p.enabledIf,state)); }
    int x(Element e) { return originX + (int)e.bounds.x; }
    int y(Element e) { return originY + (int)e.bounds.y - parents(e).stream().filter(p->p.type.equals("scroll_panel")).mapToInt(p->scroll.getOrDefault(p.id,0)).sum(); }
    void clip(GuiGraphics g,int x,int y,int w,int h) {
        // GuiGraphics scissor coordinates do not use the pose transform.
        g.enableScissor((int)Math.floor(x*viewScale),(int)Math.floor(y*viewScale),
            (int)Math.ceil((x+w)*viewScale),(int)Math.ceil((y+h)*viewScale));
    }
    // Controls with no picture (sounds, and the web-only colliders and cameras) are never hovered or clicked.
    static boolean unseen(Element e) { return e.type.equals("sound") || e.type.equals("collider") || e.type.equals("camera"); }
    boolean inside(Element e,double mx,double my) { mx/=viewScale; my/=viewScale; if (ui.clipToScreen && (mx < originX || mx >= originX+ui.size.width || my < originY || my >= originY+ui.size.height)) return false; if (mx < x(e) || mx >= x(e)+e.bounds.width || my < y(e) || my >= y(e)+e.bounds.height) return false; for(var p:parents(e)) if(mx<x(p) || mx>=x(p)+p.bounds.width || my<y(p) || my>=y(p)+p.bounds.height)return false; return true; }
    @Override public void render(GuiGraphics g,int mx,int my,float partial) {
        if(ui.dimBackground) g.fill(0,0,width,height,0xB010141B);
        g.pose().pushPose(); g.pose().scale(viewScale,viewScale,1);
        if(ui.showFrame) { g.fill(originX-6,originY-20,originX+ui.size.width+6,originY+ui.size.height+6,0xFF242B34); g.drawString(font,title,originX,originY-14,0xFFD9E6F1); }
        Element over = null;
        if(ui.clipToScreen) clip(g,originX,originY,ui.size.width,ui.size.height); // nothing outside the screen's own area
        for (Element e : ui.elements) {
            if (!visible(e)) continue; boolean hover = !unseen(e) && inside(e,mx,my); if (hover && enabled(e)) over = e;
            var ancestors=parents(e); for(var parent:ancestors) clip(g,x(parent),y(parent),(int)parent.bounds.width,(int)parent.bounds.height);
            var renderer = ElementRenderers.get(e.type); if (renderer != null) try { ElementRenderers.skin(g,e,x(e),y(e),(int)e.bounds.width,(int)e.bounds.height); renderer.draw(this,g,e,x(e),y(e),(int)e.bounds.width,(int)e.bounds.height,hover && enabled(e)); ElementRenderers.border(g,e,x(e),y(e),(int)e.bounds.width,(int)e.bounds.height); } catch (Exception ex) { g.drawString(font,"Invalid " + e.type,x(e),y(e),0xFFFF7070); }
            if (!enabled(e)) ElementRenderers.roundedFill(g,x(e),y(e),(int)e.bounds.width,(int)e.bounds.height,e.cornerRadius,0x77000000);
            for(var parent:ancestors) g.disableScissor();
        }
        if(ui.clipToScreen) g.disableScissor();
        if (over != hovered) { if (hovered != null) fire(hovered,"mouse_leave",""); hovered = over; if (hovered != null) fire(hovered,"mouse_enter",""); }
        if (hovered != null && System.currentTimeMillis()-lastHover > 250) { lastHover = System.currentTimeMillis(); fire(hovered,"hover",""); }
        g.pose().popPose();
        if (over != null && !over.tooltip.isEmpty()) g.renderTooltip(font,Component.literal(bind(over.tooltip)),mx,my);
    }
    public void text(GuiGraphics g,Element e,int x,int y,int w,int h,String text) {
        float scale = (float)e.fontScale;
        var style = net.minecraft.network.chat.Style.EMPTY.withFont(ResourceLocation.parse(e.font)).withBold(e.bold).withItalic(e.italic).withUnderlined(e.underline);
        var lines = font.split(Component.literal(text).withStyle(style),Math.max(1,(int)(w/scale)-4));
        if (lines.isEmpty()) return;
        var clipped = lines.getFirst();
        int tw = (int)(font.width(clipped)*scale); int dx = e.alignment.equals("center") ? (w-tw)/2 : e.alignment.equals("right") ? w-tw-2 : 2;
        g.pose().pushPose(); g.pose().translate(x+dx,y+(h-8*scale)/2,0); g.pose().scale(scale,scale,1); if (e.textShadow && e.shadowOpacity > 0) {
            // Bounded nine-tap blur; offsets are GUI pixels independent of font scale.
            int extent = e.shadowBlur > 0 ? 1 : 0;
            for (int sy=-extent; sy<=extent; sy++) for (int sx=-extent; sx<=extent; sx++) {
                double weight = extent == 0 ? 1 : (sx == 0 ? 2 : 1)*(sy == 0 ? 2 : 1)/16.0;
                int shadow = ElementRenderers.color(e.shadowColor,e.opacity*e.shadowOpacity);
                double alpha = (shadow >>> 24)/255.0;
                int tapAlpha = (int)Math.round(255*(1-Math.pow(1-alpha,weight)));
                if (tapAlpha < 1) continue;
                g.pose().pushPose();
                g.pose().translate((e.shadowOffsetX+sx*e.shadowBlur/2)/scale,(e.shadowOffsetY+sy*e.shadowBlur/2)/scale,0);
                g.drawString(font,clipped,0,0,(tapAlpha << 24)|(shadow & 0xFFFFFF),false);
                g.pose().popPose();
            }
        }
        g.drawString(font,clipped,0,0,ElementRenderers.color(e.foreground,e.opacity),false); g.pose().popPose();
    }
    @Override public boolean mouseClicked(double mx,double my,int button) {
        if (button != 0) return super.mouseClicked(mx,my,button); focused = null;
        var reversed = new ArrayList<>(ui.elements); Collections.reverse(reversed);
        for (Element e : reversed) if (!unseen(e) && visible(e) && enabled(e) && inside(e,mx,my)) {
            switch (e.type) {
                case "item_list" -> { int index=(int)((my/viewScale-y(e)+listScroll(e.id))/Math.clamp(e.rowHeight,24,128)); if(index>=0 && index<rows(e).size()) { if(!e.rowElements.isEmpty()) {String action=RowTemplateRenderer.hit(this,e,mx/viewScale-x(e),(my/viewScale-y(e)+listScroll(e.id))%Math.clamp(e.rowHeight,24,128));if(!action.isEmpty())fire(e,action,Integer.toString(index));break;} int count=(e.primaryLabel.isEmpty()?0:1)+(e.secondaryLabel.isEmpty()?0:1); int bw=Math.max(32,Math.min(62,(int)e.bounds.width/5)); double local=mx/viewScale-x(e); String event="item_click"; if(!e.secondaryLabel.isEmpty() && local>=e.bounds.width-6-bw) event="item_secondary"; else if(!e.primaryLabel.isEmpty() && local>=e.bounds.width-6-count*bw) event="item_primary"; fire(e,event,Integer.toString(index)); } }
                case "button" -> fire(e,"click","");
                case "textbox" -> focused = e;
                case "checkbox" -> { e.value = Boolean.toString(!Boolean.parseBoolean(e.value)); fire(e,e.value.equals("true") ? "checked" : "unchecked",e.value); }
                case "slider" -> { dragging = e; slide(e,mx); }
                case "dropdown" -> { if (!e.options.isEmpty()) { int index; try { index = Integer.parseInt(e.value); } catch (Exception ex) { index = -1; } e.value = Integer.toString((index+1)%e.options.size()); fire(e,"value_changed",e.value); } }
            }
            return true;
        }
        return true;
    }
    private void slide(Element e,double mx) { e.value = String.format(Locale.ROOT,"%.2f",e.minimum + Math.clamp((mx/viewScale-x(e))/e.bounds.width,0,1)*(e.maximum-e.minimum)); fire(e,"value_changed",e.value); }
    @Override public boolean mouseDragged(double mx,double my,int button,double dx,double dy) { if (dragging != null) { slide(dragging,mx); return true; } return super.mouseDragged(mx,my,button,dx,dy); }
    @Override public boolean mouseReleased(double mx,double my,int button) { dragging = null; return super.mouseReleased(mx,my,button); }
    @Override public boolean mouseScrolled(double mx,double my,double horizontal,double vertical) { for(Element e:ui.elements) if(e.type.equals("item_list") && visible(e) && enabled(e) && inside(e,mx,my)) { int max=Math.max(0,rows(e).size()*Math.clamp(e.rowHeight,24,128)-(int)e.bounds.height); int next=Math.clamp(listScroll(e.id)-(int)(vertical*24),0,max);if(next==listScroll(e.id))continue;scroll.put(e.id,next); return true; } for (Element e : ui.elements.stream().sorted(java.util.Comparator.comparingInt((Element e)->parents(e).size()).reversed()).toList()) if (e.type.equals("scroll_panel") && visible(e) && enabled(e) && inside(e,mx,my)) { double bottom = ui.elements.stream().filter(c -> c.parent.equals(e.id)).mapToDouble(c -> c.bounds.y+c.bounds.height).max().orElse(e.bounds.y+e.bounds.height); int max = Math.max(0,(int)(bottom-e.bounds.y-e.bounds.height)); int next=Math.clamp(scroll.getOrDefault(e.id,0)-(int)(vertical*12),0,max); if(next==scroll.getOrDefault(e.id,0))continue;scroll.put(e.id,next); return true; } return false; }
    @Override public boolean charTyped(char c,int modifiers) { if (focused != null && c >= 32 && c != 127 && focused.value.length() < 1024) { focused.value += c; fire(focused,"text_changed",focused.value); return true; } return super.charTyped(c,modifiers); }
    @Override public boolean keyPressed(int key,int scan,int modifiers) {
        if (focused != null) { if (key == GLFW.GLFW_KEY_BACKSPACE) { if (!focused.value.isEmpty()) focused.value = focused.value.substring(0,focused.value.length()-1); fire(focused,"text_changed",focused.value); return true; } if (key == GLFW.GLFW_KEY_ENTER || key == GLFW.GLFW_KEY_KP_ENTER) { fire(focused,"submit",focused.value); return true; } if (Screen.isPaste(key)) { String value = minecraft.keyboardHandler.getClipboard().replaceAll("[\\p{Cntrl}]",""); focused.value = (focused.value+value).substring(0,Math.min(1024,focused.value.length()+value.length())); fire(focused,"text_changed",focused.value); return true; } }
        // Screen Key event: while no text box is being typed in, a fresh press fires at once. A held key repeats at most
        // every keyRepeat ms (never, when 0) and the script sees ctx.repeat. Nothing queues: extra repeats are dropped.
        // Escape isn't forwarded, so it still closes the screen.
        else if (ui.events.containsKey("key")) {
            String name = com.wysicraft.runtime.model.KeyNames.name(key);
            if (name != null) {
                boolean repeat = !heldKeys.add(key); long now = System.currentTimeMillis();
                if (!repeat || (ui.keyRepeat > 0 && now - lastKey >= ui.keyRepeat)) { lastKey = now; keyRepeating = repeat; try { fire(null,"key",name); } finally { keyRepeating = false; } }
                return true;
            }
        }
        return super.keyPressed(key,scan,modifiers);
    }
    private final Set<Integer> heldKeys = new HashSet<>(); private long lastKey; private boolean keyRepeating;
    @Override public boolean keyReleased(int key,int scan,int modifiers) { heldKeys.remove(key); return super.keyReleased(key,scan,modifiers); }
    @Override public void onClose() { closing(); super.onClose(); }
    @Override public void removed() { closing(); }
    /** The screen's close event and telling the server, once, however the screen goes away. */
    void closing() { stopSounds(); if (!closed) { closed = true; fire(null,"close",""); if (!remoteClosing) PacketDistributor.sendToServer(new Payloads.UiEvent(ui.id,session,"","close","")); } }
    void fire(Element element,String event,String value) {
        var events = element == null ? ui.events : element.events; Event ev = events.get(event); if (ev == null) return;
        if (element != null) PacketDistributor.sendToServer(new Payloads.UiEvent(ui.id,session,element.id,event,value));
        for (Action action : ev.client.actions) try { execute(action); } catch (Exception ex) { Wysicraft.LOG.warn("Client action {}: {}",action.type,ex.toString()); }
        var pack = ClientRuntime.PACKS.pack(ui.id); if (pack != null) Scripts.execute(Scripts.Side.CLIENT,pack,ev.client,new Scripts.Context() {
            public String getVariable(String name) { return state.getOrDefault(name,""); }
            public void setVariable(String name,String value) { state.put(name,value); }
            public void message(String text) { minecraft.player.sendSystemMessage(Component.literal(text)); }
            public String elementId() { return element == null ? "" : element.id; }
            public String value() { return value; }
            public boolean repeat() { return keyRepeating; }
            public String text(String id) { var e = ui.element(id); return e == null ? "" : e.text; }
            public String query(String operation,String argument) {
                if (!operation.equals("slot_items")) throw new IllegalArgumentException(operation);
                var rows = new ArrayList<Object>();
                if (host != null) for (var stack : host.getMenu().stacksOf(argument))
                    rows.add(stack.isEmpty() ? null : new com.wysicraft.runtime.model.ItemRows.Row(BuiltInRegistries.ITEM.getKey(stack.getItem()).toString(),stack.getCount(),stack.getHoverName().getString()));
                return com.wysicraft.runtime.model.Models.JSON.toJson(rows);
            }
            public void action(String type,String target,String value) {
                if (type.startsWith("console_")) { Wysicraft.LOG.info("[JS {}] {}",type.substring(8),value); return; }
                if (type.equals("set_variable")) { state.put(target,value); return; }
                // Script strings are literal; do not expand ${...} a second time.
                var e = ui.element(target);
                switch (type) {
                    case "set_text" -> { if (e != null) e.text = value; }
                    case "set_value" -> { if (e != null) { if(e.type.equals("item_list")) com.wysicraft.runtime.model.ItemRows.parse(value); e.value = value; soundValue(e,value); } }
                    case "set_item" -> {
                        if (e == null || !e.type.equals("item")) throw new IllegalArgumentException("setItem target must be an item element: " + target);
                        var resource = ResourceLocation.tryParse(value);
                        if (resource == null || !BuiltInRegistries.ITEM.containsKey(resource)) throw new IllegalArgumentException("Unknown item ID: " + value);
                        e.item = resource.toString();
                    }
                    case "set_visible" -> { if (e != null) e.visible = Boolean.parseBoolean(value); }
                    case "set_enabled" -> { if (e != null) e.enabled = Boolean.parseBoolean(value); }
                    case "change_texture" -> { if (e != null) e.texture = value; }
                    case "message" -> message(value);
                    case "close_ui" -> minecraft.execute(DynamicScreen.this::requestClose);
                    case "play_sound" -> playSound(value);
                    default -> throw new IllegalArgumentException("Unsupported client script action: " + type);
                }
            }
        },message -> Wysicraft.LOG.warn("Script: {}",message));
    }
    void execute(Action action) {
        Element target = ui.element(action.target); String value = bind(action.value);
        switch (action.type) {
            case "set_text" -> { if (target != null) target.text = value; }
            case "set_item" -> { if(target!=null && target.type.equals("item")) { var id=ResourceLocation.tryParse(value); if(id!=null && BuiltInRegistries.ITEM.containsKey(id)) target.item=value; } }
            case "set_visible" -> { if (target != null) target.visible = Boolean.parseBoolean(value); }
            case "set_enabled" -> { if (target != null) target.enabled = Boolean.parseBoolean(value); }
            case "set_value" -> { if (target != null) { target.value = value; soundValue(target,value); } }
            case "change_texture" -> { if (target != null) target.texture = value; }
            case "set_variable" -> state.put(action.target,value);
            case "toggle_variable" -> state.put(action.target,Boolean.toString(!Boolean.parseBoolean(state.get(action.target))));
            case "message" -> minecraft.player.sendSystemMessage(Component.literal(value));
            case "play_sound" -> playSound(value);
            case "close_ui" -> requestClose();
            case "open_ui" -> { /* Server authorizes navigation from the trusted event definition. */ }
            default -> { var handler = ACTIONS.get(action.type); if (handler == null) throw new IllegalArgumentException("Unknown client action"); handler.accept(this,action); }
        }
    }
    // ---- Sounds and sprites ----
    // Any sound ID Minecraft knows (a sounds.json in the game, a mod or the project JAR), including project .ogg files.
    void playSound(String id) {
        var location = ResourceLocation.tryParse(id); if (location == null) return;
        minecraft.getSoundManager().play(SimpleSoundInstance.forUI(net.minecraft.sounds.SoundEvent.createVariableRangeEvent(location),1));
    }
    private long openedAt;
    private record Playing(net.minecraft.client.resources.sounds.SoundInstance instance, int[] left) {}
    private final Map<String,Playing> sounds = new HashMap<>(); private final Set<String> started = new HashSet<>();
    // Sound controls: start Delay ms after opening (when Autoplay), play Repeat times, or loop; "play"/"stop" values control them.
    private void tickSounds() {
        long now = System.currentTimeMillis();
        for (var e : ui.elements) {
            if (!e.type.equals("sound") || e.sound.isEmpty()) continue;
            if (e.autoplay && !started.contains(e.id) && now - openedAt >= e.delay) startSound(e);
            var playing = sounds.get(e.id);
            if (playing != null && !e.loop && !minecraft.getSoundManager().isActive(playing.instance())) { if (--playing.left()[0] > 0) play(e,playing.left()); else sounds.remove(e.id); }
        }
    }
    void soundValue(Element e, String value) { if (!e.type.equals("sound")) return; if (value.equals("play")) startSound(e); else if (value.equals("stop")) stopSound(e); }
    private void startSound(Element e) { started.add(e.id); stopSound(e); play(e,new int[]{Math.max(1,e.repeat)}); }
    private void play(Element e, int[] left) {
        var location = ResourceLocation.tryParse(e.sound); if (location == null) return;
        var instance = new SimpleSoundInstance(location, net.minecraft.sounds.SoundSource.MASTER, (float)Math.clamp(e.volume,0,1), 1f,
            net.minecraft.client.resources.sounds.SoundInstance.createUnseededRandom(), e.loop, 0, net.minecraft.client.resources.sounds.SoundInstance.Attenuation.NONE, 0, 0, 0, true);
        minecraft.getSoundManager().play(instance); sounds.put(e.id, new Playing(instance, left));
    }
    private void stopSound(Element e) { var playing = sounds.remove(e.id); if (playing != null) minecraft.getSoundManager().stop(playing.instance()); }
    private void stopSounds() { for (var playing : sounds.values()) minecraft.getSoundManager().stop(playing.instance()); sounds.clear(); }
    private final Map<String,Object[]> spriteClock = new HashMap<>();
    /** Milliseconds since the sprite's current clip (its value) was chosen. */
    long spriteElapsed(Element e) {
        long now = System.currentTimeMillis(); var clock = spriteClock.get(e.id);
        if (clock == null || !clock[0].equals(e.value)) { clock = new Object[]{e.value, now}; spriteClock.put(e.id, clock); }
        return now - (long)clock[1];
    }

}




