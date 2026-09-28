package com.wysicraft.runtime.model;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import java.util.*;

public final class Models {
    private Models() {}
    public static final Gson JSON = new GsonBuilder().disableHtmlEscaping().create();
    public static class Manifest {
        public int schemaVersion = 1;
        public String id = "", name = "", author = "", version = "1.0.0", runtimeVersion = "1.0.0", defaultUi = "";
        public List<String> ui = new ArrayList<>(), dependencies = new ArrayList<>();
    }
    public static class Ui {
        public int tickInterval = 0; // ms between Tick events while open; 0 = off
        public int keyRepeat = 150; // ms between Key events while a key is held; 0 = a held key sends one event
        public boolean responsive = false;
        public boolean showFrame = false, dimBackground = false, fitToScreen = true, clipToScreen = false;
        public int schemaVersion = 1;
        public String id = "", title = "";
        public Size size = new Size();
        public Map<String,String> variables = new LinkedHashMap<>();
        public Map<String,Event> events = new LinkedHashMap<>();
        public List<Element> elements = new ArrayList<>();
        public Element element(String id) { return elements.stream().filter(e -> e.id.equals(id)).findFirst().orElse(null); }
        public Ui copy() { return JSON.fromJson(JSON.toJson(this), Ui.class); }
    }
    public static class Size { public int width = 320, height = 200; }
    public static class Bounds { public double x, y, width = 100, height = 20; }
    public static class Element {
        public String rowTemplate = "", rowAction = "";
        public List<Element> rowElements = new ArrayList<>();
        public String horizontalAnchor="left", verticalAnchor="top";
        public double minWidth=1,minHeight=1,rowTemplateWidth;
        public int rowHeight = 30;
        public String primaryLabel = "", secondaryLabel = "";
        public boolean showItemId = true;
        public String id = "", name = "", type = "button", text = "", tooltip = "", foreground = "#FFFFFF", background = "#40464F", alignment = "left", texture = "", item = "minecraft:stone", value = "0", visibleIf = "", enabledIf = "", parent = "";
        public Bounds bounds = new Bounds();
        public boolean visible = true, enabled = true;
        public double opacity = 1, fontScale = 1, minimum = 0, maximum = 100;
        public String font = "minecraft:default";
        public boolean bold, italic, underline, textShadow;
        public boolean fillEnabled = true;
        public String borderColor = "#697382", shadowColor = "#000000";
        public double borderWidth, shadowOpacity = .75, shadowOffsetX = 1, shadowOffsetY = 1, shadowBlur;
        public double cornerRadius;
        public int textureX, textureY, textureWidth = 256, textureHeight = 256;
        // Sprite sheets (frame size + clips), shape outlines and Sound controls.
        public int frameWidth = 16, frameHeight = 16;
        public String clips = "", shape = "rectangle", sound = "";
        public boolean autoplay = true, loop;
        public int delay, repeat = 1;
        public double volume = 1;
        public List<String> options = new ArrayList<>();
        public Map<String,Event> events = new LinkedHashMap<>();
    }
    public static class Event { public Handler client = new Handler(), server = new Handler(); }
    public static class Handler { public int permissionLevel, cooldownTicks = 4; public List<Action> actions = new ArrayList<>(); public String script = "", function = ""; }
    public static class Action { public String type = "", target = "", value = ""; }
}


