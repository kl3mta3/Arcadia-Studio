package com.wysicraft.runtime.client;

import com.wysicraft.runtime.model.Models.Element;
import com.wysicraft.runtime.pack.PackRepository;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.resources.ResourceLocation;
import net.minecraft.world.item.ItemStack;
import java.util.*;

public final class ElementRenderers {
    private ElementRenderers() {}
    @FunctionalInterface public interface Renderer { void draw(DynamicScreen screen, GuiGraphics graphics, Element element, int x, int y, int w, int h, boolean hover); }
    private static final Map<String,Renderer> REGISTRY = new HashMap<>();
    public static void register(String type, Renderer renderer) { if (REGISTRY.putIfAbsent(type,renderer) != null) throw new IllegalArgumentException("Duplicate renderer"); PackRepository.CONTROLS.add(type); }
    public static Renderer get(String type) { return REGISTRY.get(type); }
    // Item models draw raised in depth, so controls drawn after them (higher layers) would be hidden behind the icon.
    // Finishing the item and clearing depth keeps Wysicraft's layer order: later controls always draw on top.
    static void layeredItem(GuiGraphics g, ItemStack stack, int x, int y) {
        g.renderItem(stack,x,y); g.flush();
        com.mojang.blaze3d.systems.RenderSystem.clear(org.lwjgl.opengl.GL11.GL_DEPTH_BUFFER_BIT, net.minecraft.client.Minecraft.ON_OSX);
    }
    static {
        Renderer panel = (s,g,e,x,y,w,h,hover) -> { };
        register("panel",panel); register("scroll_panel",panel);
        register("label",(s,g,e,x,y,w,h,hover) -> s.text(g,e,x,y,w,h,s.bind(e.text)));
        // Item slots: Minecraft's sunken slot look; the items themselves are the container's (SlotScreen).
        register("slots",(s,g,e,x,y,w,h,hover) -> {
            if (e.slotKind.equals("result")) { slot(g,x+(w-26)/2,y+(h-26)/2,26); return; }
            for (int row = 0; row < e.rows; row++) for (int column = 0; column < e.columns; column++) slot(g,x+column*18,y+row*18,18);
        });
        register("button",(s,g,e,x,y,w,h,hover) -> { if (hover) roundedFill(g,x,y,w,h,e.cornerRadius,0x22FFFFFF); s.text(g,e,x,y,w,h,s.bind(e.text)); });
        register("textbox",(s,g,e,x,y,w,h,hover) -> { if (s.focused == e) g.renderOutline(x,y,w,h,0xFF91CFFF); s.text(g,e,x+3,y,w-6,h,e.value + (s.focused == e && (System.currentTimeMillis()/500)%2 == 0 ? "_" : "")); });
        register("checkbox",(s,g,e,x,y,w,h,hover) -> { int size = Math.min(16,h); g.renderOutline(x,y,size,size,0xFFB0B8C0); if (Boolean.parseBoolean(e.value)) g.fill(x+3,y+3,x+size-3,y+size-3,0xFF69C8EE); s.text(g,e,x+size+4,y,w-size-4,h,s.bind(e.text)); });
        Renderer bar = (s,g,e,x,y,w,h,hover) -> { double value; try { value = Double.parseDouble(e.value); } catch (Exception ex) { value = e.minimum; } int fill = (int)(w*Math.clamp((value-e.minimum)/(e.maximum-e.minimum),0,1)); g.fill(x,y,x+fill,y+h,0xFF318DB5); s.text(g,e,x,y,w,h,e.value); };
        register("slider",bar); register("progress",bar);
        register("dropdown",(s,g,e,x,y,w,h,hover) -> { int index; try { index = Integer.parseInt(e.value); } catch (Exception ex) { index = 0; } String text = e.options.isEmpty() ? "(empty)" : e.options.get(Math.clamp(index,0,e.options.size()-1)); s.text(g,e,x+3,y,w-14,h,text); g.drawString(s.font(),"v",x+w-10,y+(h-8)/2,0xFFFFFFFF); });
        register("item",(s,g,e,x,y,w,h,hover) -> {
            float scale=Math.min(w,h)/16f;
            g.pose().pushPose(); g.pose().translate(x+(w-16*scale)/2,y+(h-16*scale)/2,0); g.pose().scale(scale,scale,1);
            layeredItem(g,new ItemStack(BuiltInRegistries.ITEM.get(ResourceLocation.parse(e.item))),0,0); g.pose().popPose();
        });
        register("item_list",(s,g,e,x,y,w,h,hover)-> {
            var rows=s.rows(e);
            s.clip(g,x,y,w,h);
            int rh=Math.clamp(e.rowHeight,24,128), buttons=(e.primaryLabel.isEmpty()?0:1)+(e.secondaryLabel.isEmpty()?0:1);
            int bw=Math.max(32,Math.min(62,w/5)), right=w-6-buttons*bw;
            try { for(int i=Math.max(0,s.listScroll(e.id)/rh);i<rows.size();i++) {
                int top=y+i*rh-s.listScroll(e.id); if(top>=y+h) break;
                var row=rows.get(i); if(!e.rowElements.isEmpty()) { RowTemplateRenderer.draw(s,g,e,row,i,x,top,w,rh);continue; } g.fill(x,top,x+w,top+rh-1,(i%2==0)?0x332C4E58:0x222C4E58);
                layeredItem(g,new ItemStack(BuiltInRegistries.ITEM.get(ResourceLocation.parse(row.item()))),x+4,top+4);
                var line=s.font().plainSubstrByWidth(row.name(),Math.max(1,right-30));
                g.drawString(s.font(),line,x+26,top+4,0xFFE3ECEB,false);
                if(e.showItemId) { g.pose().pushPose(); g.pose().translate(x+26,top+16,0); g.pose().scale(.65f,.65f,1); g.drawString(s.font(),s.font().plainSubstrByWidth(row.item(),Math.max(1,(int)((right-30)/.65))),0,0,0xFF8199A4,false); g.pose().popPose(); }
                int bx=x+w-6-buttons*bw;
                for(String label:new String[]{e.primaryLabel,e.secondaryLabel}) if(!label.isEmpty()) {
                    roundedFill(g,bx,top+3,bw-4,rh-6,3,0xFF325546);
                    g.drawString(s.font(),s.font().plainSubstrByWidth(label,bw-8),bx+3,top+(rh-8)/2,0xFFC8F1AE,false); bx+=bw;
                }
                if(buttons==0) { String count="x"+row.count(); g.drawString(s.font(),count,x+w-s.font().width(count)-4,top+8,0xFF9CE7CD,false); }
            } } finally { g.disableScissor(); }
            if(rows.size()*rh>h) { int thumb=Math.max(8,h*h/(rows.size()*rh)); int offset=s.listScroll(e.id)*(h-thumb)/Math.max(1,rows.size()*rh-h); g.fill(x+w-3,y+offset,x+w,y+offset+thumb,0xFF8199A4); }
        });
        Renderer image = (s,g,e,x,y,w,h,hover) -> { if (e.texture.isEmpty()) return; var info = ClientRuntime.textureInfo(e.texture); var texture = info.location(); g.setColor(1,1,1,(float)e.opacity); if (e.type.equals("texture_region")) g.blit(texture,x,y,w,h,e.textureX,e.textureY,(int)e.bounds.width,(int)e.bounds.height,e.textureWidth,e.textureHeight); else { var f = Frame.of(info,w,h); g.blit(texture,x,y,f.u,f.v,w,h,f.width,f.height); } g.setColor(1,1,1,1); };
        register("image",image); register("texture_region",image);
        // Sprites: the clip in value plays from when it was chosen; frames count left to right, top to bottom.
        register("sprite",(s,g,e,x,y,w,h,hover) -> {
            if (e.texture.isEmpty()) return; var info = ClientRuntime.textureInfo(e.texture); if (info.width() < 1) return;
            var clips = com.wysicraft.runtime.model.SpritesAndShapes.clips(e.clips); var clip = clips.get(e.value); if (clip == null && e.value.isEmpty() && !clips.isEmpty()) clip = clips.values().iterator().next();
            int fw = Math.max(1, e.frameWidth), fh = Math.max(1, e.frameHeight), columns = Math.max(1, info.width() / fw);
            int frame = clip == null ? 0 : com.wysicraft.runtime.model.SpritesAndShapes.frameAt(clip, s.spriteElapsed(e));
            g.setColor(1,1,1,(float)e.opacity); g.blit(info.location(),x,y,w,h,(frame % columns) * fw,(frame / columns) * fh,fw,fh,info.width(),info.height()); g.setColor(1,1,1,1);
        });
        // Shapes: the outline is filled row by row (colour or texture), then its edge drawn.
        register("shape",(s,g,e,x,y,w,h,hover) -> {
            var unit = com.wysicraft.runtime.model.SpritesAndShapes.outline(e.shape);
            if (e.fillEnabled) {
                var info = e.texture.isEmpty() ? null : ClientRuntime.textureInfo(e.texture); int fill = color(e.background,e.opacity);
                var f = info == null ? null : Frame.of(info,w,h); if (info != null) g.setColor(1,1,1,(float)e.opacity);
                for (int row = 0; row < h; row++) for (int[] span : com.wysicraft.runtime.model.SpritesAndShapes.rowSpans(unit,w,h,row)) {
                    if (info == null) g.fill(x+span[0],y+row,x+span[1],y+row+1,fill); else g.blit(info.location(),x+span[0],y+row,f.u()+span[0],f.v()+row,span[1]-span[0],1,f.width(),f.height());
                }
                if (info != null) g.setColor(1,1,1,1);
            }
            if (hover && e.events.containsKey("click")) for (int row = 0; row < h; row++) for (int[] span : com.wysicraft.runtime.model.SpritesAndShapes.rowSpans(unit,w,h,row)) g.fill(x+span[0],y+row,x+span[1],y+row+1,0x22FFFFFF);
            int t = (int)Math.ceil(e.borderWidth);
            if (t > 0) { int c = color(e.borderColor,e.opacity); for (int i = 0; i < unit.length; i++) { double[] a = unit[i], b = unit[(i + 1) % unit.length]; double ax = a[0]*w, ay = a[1]*h, bx = b[0]*w, by = b[1]*h; int steps = (int)Math.ceil(Math.hypot(bx-ax,by-ay)*2)+1; for (int k = 0; k <= steps; k++) { double px = ax+(bx-ax)*k/steps, py = ay+(by-ay)*k/steps; int cx = x+(int)Math.round(px-t/2.0), cy = y+(int)Math.round(py-t/2.0); g.fill(cx,cy,cx+t,cy+t,c); } } }
            if (!e.text.isEmpty()) s.text(g,e,x,y,w,h,s.bind(e.text));
        });
        register("sound",(s,g,e,x,y,w,h,hover) -> { }); // plays, never drawn
    }
    static int inset(int row, int height, double radius) {
        double distance = Math.max(0, radius - Math.min(row + .5, height - row - .5));
        return (int)Math.ceil(radius - Math.sqrt(Math.max(0, radius * radius - distance * distance)));
    }
    public static void roundedFill(GuiGraphics g,int x,int y,int w,int h,double radius,int color) {
        double r = Math.clamp(radius,0,Math.min(w,h)/2.0);
        if (r == 0) { g.fill(x,y,x+w,y+h,color); return; }
        for (int row = 0; row < h; row++) { int edge = inset(row,h,r); g.fill(x+edge,y+row,x+w-edge,y+row+1,color); }
    }
    static void slot(GuiGraphics g,int x,int y,int size) {
        g.fill(x,y,x+size,y+size,0xFF8B8B8B);
        g.fill(x,y,x+size-1,y+1,0xFF373737); g.fill(x,y,x+1,y+size-1,0xFF373737);
        g.fill(x+1,y+size-1,x+size,y+size,0xFFFFFFFF); g.fill(x+size-1,y+1,x+size,y+size,0xFFFFFFFF);
    }
    public static void skin(GuiGraphics g,Element e,int x,int y,int w,int h) {
        if (!e.fillEnabled || e.type.equals("shape") || e.type.equals("sound")) return; // shapes fill their own outline
        if (e.texture.isEmpty() || e.type.equals("image") || e.type.equals("texture_region") || e.type.equals("sprite")) { roundedFill(g,x,y,w,h,e.cornerRadius,color(e.background,e.opacity)); return; }
        var info = ClientRuntime.textureInfo(e.texture); var texture = info.location(); double radius = Math.clamp(e.cornerRadius,0,Math.min(w,h)/2.0);
        var f = Frame.of(info,w,h);
        g.setColor(1,1,1,(float)e.opacity);
        try {
            if (radius == 0) g.blit(texture,x,y,f.u,f.v,w,h,f.width,f.height);
            else for (int row = 0; row < h; row++) {
                int edge = inset(row,h,radius);
                // Draw UV-aligned strips, preserving any parent scissor rectangle.
                g.blit(texture,x+edge,y+row,f.u+edge,f.v+row,w-2*edge,1,f.width,f.height);
            }
        } finally { g.setColor(1,1,1,1); }
    }
    // Which part of a texture to draw into a w×h box. A still image fills the box; an animated one is treated as
    // its frame grid scaled so one frame is w×h, and (u,v) picks the frame showing now.
    record Frame(int u,int v,int width,int height) {
        static Frame of(ClientRuntime.TextureInfo info,int w,int h) {
            var a = info.animation(); if (a == null) return new Frame(0,0,w,h);
            int index = a.frameAt(System.currentTimeMillis());
            return new Frame(a.column(index)*w,a.row(index)*h,w*a.columns,h*a.rows);
        }
    }
    public static void border(GuiGraphics g,Element e,int x,int y,int w,int h) {
        if (e.type.equals("shape") || e.type.equals("sound")) return;
        int thickness = Math.min((int)Math.ceil(e.borderWidth),Math.min(w,h)/2);
        if (thickness <= 0) return;
        double radius = Math.clamp(e.cornerRadius,0,Math.min(w,h)/2.0);
        double innerRadius = Math.max(0,radius-thickness);
        int color = color(e.borderColor,e.opacity);
        for (int row=0; row<h; row++) {
            int outer = inset(row,h,radius);
            if (row<thickness || row>=h-thickness) g.fill(x+outer,y+row,x+w-outer,y+row+1,color);
            else {
                int inner = thickness+inset(row-thickness,h-2*thickness,innerRadius);
                g.fill(x+outer,y+row,x+inner,y+row+1,color);
                g.fill(x+w-inner,y+row,x+w-outer,y+row+1,color);
            }
        }
    }
    public static int color(String value, double opacity) { try { long c = Long.parseLong(value.substring(1),16); int alpha = value.length() == 9 ? (int)(c >>> 24) : 255; return ((int)(alpha*opacity) << 24) | ((int)c & 0xFFFFFF); } catch (Exception ex) { return 0xFFFF00FF; } }
}

