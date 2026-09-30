package com.wysicraft.runtime.pack;

import com.wysicraft.runtime.model.Models.*;
import com.wysicraft.runtime.model.Models;
import com.wysicraft.runtime.model.Expressions;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;
import java.util.function.BiConsumer;
import java.util.zip.*;

public final class PackRepository {
    public static final int MAX_FILE = 32 * 1024 * 1024, MAX_PACK = 256 * 1024 * 1024;
    public static final Set<String> CONTROLS = new HashSet<>(List.of("button","label","image","textbox","checkbox","slider","progress","dropdown","panel","scroll_panel","item","item_list","texture_region","sprite","shape","sound","slots"));
    /** What an item slots control holds: part of the player's inventory, temporary storage, a crafting grid, or its result. */
    public static final Set<String> SLOT_KINDS = Set.of("player","storage","crafting","result");
    public static final Set<String> CLIENT_ACTIONS = new HashSet<>(List.of("set_text","set_visible","set_enabled","set_value","open_ui","close_ui","play_sound","set_variable","toggle_variable","message","change_texture"));
    public static final Set<String> SERVER_ACTIONS = new HashSet<>(List.of("command","message","set_variable","toggle_variable","open_ui","close_ui","server_function","player_inventory"));
    /** This runtime's version, stamped from build.gradle at build time; packs may require at most this version. */
    public static final String RUNTIME_VERSION = runtimeVersion();
    private static String runtimeVersion() {
        try (var in = PackRepository.class.getResourceAsStream("/wysicraft-runtime.properties")) {
            var properties = new java.util.Properties(); if (in != null) properties.load(in);
            String version = properties.getProperty("version", "");
            if (!version.matches("\\d+\\.\\d+\\.\\d+")) throw new IllegalStateException("Missing or unexpanded runtime version: " + version);
            return version;
        } catch (IOException ex) { throw new java.io.UncheckedIOException(ex); }
    }
    public record Loaded(Manifest manifest, Map<String,Ui> screens, Map<String,byte[]> files) {}
    private final Map<String,Loaded> byUi = new LinkedHashMap<>();
    public Map<String,Loaded> all() { return Collections.unmodifiableMap(byUi); }
    public String resolve(String id) {
        if (byUi.containsKey(id)) return id;
        if (id == null || id.contains(":")) return null;
        var matches = byUi.keySet().stream().filter(key -> key.endsWith(":" + id)).toList();
        return matches.size() == 1 ? matches.getFirst() : null;
    }
    public Ui get(String id) {
        String key = resolve(id); if (key == null) return null;
        Loaded p = byUi.get(key); Ui ui = p.screens.get(key.substring(key.indexOf(':')+1)).copy(); ui.id=key;
        var events = new ArrayList<>(ui.events.values()); ui.elements.forEach(e -> events.addAll(e.events.values()));
        for (var event : events) for (var handler : List.of(event.client,event.server))
            for (var action : handler.actions) if(action.type.equals("open_ui") && !action.value.contains(":")) action.value=p.manifest.id+":"+action.value;
        return ui;
    }
    public Loaded pack(String id) { return byUi.get(resolve(id)); }
    public static boolean screenId(String value) { if(value==null) return false; String[] parts=value.split(":",-1); return parts.length==2 && id(parts[0]) && id(parts[1]); }
    public static byte[] textureFile(Loaded pack, String resource) {
        if (!resource(resource)) return null;
        String[] parts = resource.split(":",2);
        byte[] direct = pack.files().get("assets/" + parts[0] + "/" + parts[1]);
        if (direct != null || !parts[0].equals(pack.manifest().id)) return direct;
        String name = parts[1].startsWith("textures/gui/") ? parts[1].substring(13) : parts[1];
        if (name.startsWith("image/")) name = name.substring(6);
        byte[] canonical = pack.files().get("assets/" + parts[0] + "/textures/gui/image/" + name);
        if (canonical == null) canonical = pack.files().get("assets/" + parts[0] + "/textures/gui/" + name);
        return canonical != null ? canonical : pack.files().get("assets/textures/" + name);
    }
    public void reload(Path root, BiConsumer<String,Exception> log) {
        reload(root, log, dependency -> true);
    }
    public void reload(Path root, BiConsumer<String,Exception> log, java.util.function.Predicate<String> availableMod) {
        reload(root,log,availableMod,List.of());
    }
    public void reload(Path root, BiConsumer<String,Exception> log, java.util.function.Predicate<String> availableMod, List<Path> embedded) {
        byUi.clear(); Set<String> packIds = new HashSet<>();
        try {
            Files.createDirectories(root); Path real = root.toRealPath(); List<Path> candidates = new ArrayList<>();
            try (var paths = Files.list(root)) { paths.filter(p -> p.toString().endsWith(".wysicraft")).sorted().forEach(candidates::add); }
            Path dev = root.resolve("dev"); if (Files.isDirectory(dev) && !Files.isSymbolicLink(dev)) try (var paths = Files.list(dev)) { paths.filter(Files::isDirectory).sorted().forEach(candidates::add); }
            candidates.addAll(embedded);
            for (Path path : candidates) try {
                if ((!embedded.contains(path) && !path.toRealPath().startsWith(real)) || Files.isSymbolicLink(path)) throw new IOException("Pack escapes root");
                Loaded pack = load(path);
                for (String dependency : pack.manifest.dependencies) require(availableMod.test(dependency), "Missing required mod: " + dependency);
                if (packIds.contains(pack.manifest.id)) throw new IOException("Duplicate pack ID: " + pack.manifest.id);
                packIds.add(pack.manifest.id); pack.screens.keySet().forEach(id -> byUi.put(pack.manifest.id+":"+id, pack)); log.accept("Loaded " + path, null);
            } catch (Exception ex) { log.accept("Rejected " + path, ex); }
        } catch (Exception ex) { log.accept("Cannot scan " + root, ex); }
    }
    public static boolean id(String s) { return s != null && s.matches("[a-z][a-z0-9_]{0,63}"); }
    public static boolean variable(String s) { return s != null && s.matches("[a-zA-Z_][a-zA-Z0-9_]{0,63}"); }
    public static boolean resource(String s) { return s != null && s.matches("[a-z0-9_.-]+:[a-z0-9_./-]+") && !s.contains(".."); }
    public static String safePath(String path) {
        if (path == null || path.isEmpty() || path.length() > 240 || path.contains("\\") || path.contains(":") || path.startsWith("/")) throw new IllegalArgumentException("Unsafe path: " + path);
        for (String part : path.split("/", -1)) if (part.isEmpty() || part.equals(".") || part.equals("..") || part.endsWith(".") || part.endsWith(" ") || part.chars().anyMatch(c -> c < 32)) throw new IllegalArgumentException("Unsafe path: " + path);
        return path;
    }
    public static int compareVersion(String a, String b) {
        if (!a.matches("\\d+\\.\\d+\\.\\d+") || !b.matches("\\d+\\.\\d+\\.\\d+")) throw new IllegalArgumentException("Invalid version");
        String[] x = a.split("\\."), y = b.split("\\."); for (int i = 0; i < 3; i++) { int c = Integer.compare(Integer.parseInt(x[i]), Integer.parseInt(y[i])); if (c != 0) return c; } return 0;
    }
    public static Loaded load(Path path) throws IOException {
        Map<String,byte[]> files = new LinkedHashMap<>(); long[] total = {0};
        if (Files.isDirectory(path)) {
            Path real = path.toRealPath(); try (var walk = Files.walk(path)) { for (Path p : walk.toList()) { if (Files.isSymbolicLink(p) || !p.toRealPath().startsWith(real)) throw new IOException("Symlinks not allowed"); if (Files.isRegularFile(p)) try (var in = Files.newInputStream(p)) { add(files, path.relativize(p).toString().replace('\\','/'), in, total); } } }
        } else {
            if (Files.size(path) > MAX_PACK) throw new IOException("Pack exceeds 256 MiB");
            try(var zip=new ZipInputStream(Files.newInputStream(path))) { ZipEntry entry; int count=0; while((entry=zip.getNextEntry())!=null) { if(++count>2048) throw new IOException("Too many files"); if(entry.isDirectory()) safePath(entry.getName().replaceAll("/+$","")); else add(files,entry.getName(),zip,total); zip.closeEntry(); } }
        }
        Manifest manifest = Models.JSON.fromJson(text(files, files.containsKey("manifest.json") ? "manifest.json" : "project.json"), Manifest.class);
        require(manifest != null && manifest.schemaVersion == 1, "Unsupported manifest schema");
        require(id(manifest.id), "Invalid pack ID"); compareVersion(manifest.version,"1.0.0"); require(compareVersion(manifest.runtimeVersion,RUNTIME_VERSION) <= 0, "Pack requires runtime " + manifest.runtimeVersion + "; this is " + RUNTIME_VERSION);
        require(manifest.ui != null && !manifest.ui.isEmpty() && manifest.ui.size() <= 128, "Invalid UI list");
        Map<String,Ui> screens = new LinkedHashMap<>();
        for (String id : manifest.ui) { require(id(id), "Invalid UI ID"); Ui ui = Models.JSON.fromJson(text(files, "ui/" + id + ".json"), Ui.class); require(ui != null && id.equals(ui.id), "UI ID mismatch"); require(screens.putIfAbsent(id, ui) == null, "Duplicate UI ID"); }
        require(screens.containsKey(manifest.defaultUi), "Missing default UI");
        for (Ui ui : screens.values()) validate(ui, manifest, screens, files);
        return new Loaded(manifest, screens, files);
    }
    private static void add(Map<String,byte[]> files, String name, InputStream in, long[] total) throws IOException {
        safePath(name); if (files.size() >= 2048) throw new IOException("Too many files");
        ByteArrayOutputStream out = new ByteArrayOutputStream(); byte[] buffer = new byte[8192]; int n;
        while ((n = in.read(buffer)) != -1) { total[0] += n; if (out.size() + n > MAX_FILE || total[0] > MAX_PACK) throw new IOException("Pack size limit"); out.write(buffer,0,n); }
        require(files.putIfAbsent(name, out.toByteArray()) == null, "Duplicate ZIP path");
    }
    public static String text(Map<String,byte[]> files, String path) { byte[] bytes = files.get(path); require(bytes != null, "Missing " + path); return new String(bytes, StandardCharsets.UTF_8); }
    public static void require(boolean value, String message) { if (!value) throw new IllegalArgumentException(message); }
    public static Set<String> events(String type) {
        Set<String> result = new HashSet<>(List.of("hover", "mouse_enter", "mouse_leave"));
        switch (type) { case "button" -> result.add("click"); case "item_list" -> result.addAll(List.of("item_click","item_primary","item_secondary")); case "textbox" -> result.addAll(List.of("text_changed","submit")); case "checkbox" -> result.addAll(List.of("checked","unchecked")); case "slider", "dropdown" -> result.add("value_changed"); case "shape" -> result.add("click"); }
        return result;
    }
    private static void validate(Ui ui, Manifest manifest, Map<String,Ui> screens, Map<String,byte[]> files) {
        validate(ui,manifest,screens,files,screens.values().stream().flatMap(s->s.elements.stream()).anyMatch(e->e.rowTemplate.equals(ui.id)));
    }
    private static void validate(Ui ui, Manifest manifest, Map<String,Ui> screens, Map<String,byte[]> files,boolean template) {
        require(ui.schemaVersion == 1, ui.id + ": unsupported schema"); require(ui.size.width >= 16 && ui.size.width <= 4096 && ui.size.height >= 16 && ui.size.height <= 4096, "Invalid canvas"); require(ui.elements.size() <= 512, "Too many elements");
        Set<String> ids = new HashSet<>();
        for (Element e : ui.elements) {
            String location = ui.id + "/" + e.id + ": ";
            require(id(e.id) && ids.add(e.id), location + "invalid or duplicate element ID"); require(CONTROLS.contains(e.type), location + "unknown element " + e.type);
            require(Double.isFinite(e.bounds.x + e.bounds.y + e.bounds.width + e.bounds.height) && e.bounds.width >= 1 && e.bounds.height >= 1 && e.bounds.width <= 4096 && e.bounds.height <= 4096, location + "invalid bounds");
            require(e.opacity >= 0 && e.opacity <= 1 && e.fontScale > 0 && e.fontScale <= 8 && e.maximum > e.minimum, location + "invalid appearance or range");
            require(resource(e.font) && Double.isFinite(e.cornerRadius) && e.cornerRadius >= 0 && e.cornerRadius <= 128, location + "invalid font or corner radius");
            require(Double.isFinite(e.borderWidth + e.shadowOpacity + e.shadowOffsetX + e.shadowOffsetY + e.shadowBlur) && e.borderWidth >= 0 && e.borderWidth <= 32 && e.shadowOpacity >= 0 && e.shadowOpacity <= 1 && Math.abs(e.shadowOffsetX) <= 64 && Math.abs(e.shadowOffsetY) <= 64 && e.shadowBlur >= 0 && e.shadowBlur <= 16, location + "invalid border/shadow settings");
            require(e.borderColor.matches("#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?") && e.shadowColor.matches("#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?"), location + "invalid border/shadow color");
            require(e.foreground.matches("#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?") && e.background.matches("#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?"), location + "invalid color");
            Expressions.evaluate(e.visibleIf, ui.variables); Expressions.evaluate(e.enabledIf, ui.variables);
            com.wysicraft.runtime.model.ContainerTree.ancestors(ui,e);
            if (!e.texture.isEmpty()) { require(resource(e.texture), location + "invalid resource"); if (e.texture.startsWith(manifest.id + ":")) require(textureFile(new Loaded(manifest,Map.of(),files),e.texture) != null, location + "missing texture"); }
            require(Set.of("left","center","right","stretch").contains(e.horizontalAnchor) && Set.of("top","center","bottom","stretch").contains(e.verticalAnchor) && Double.isFinite(e.minWidth+e.minHeight+e.rowTemplateWidth) && e.minWidth>=1 && e.minWidth<=4096 && e.minHeight>=1 && e.minHeight<=4096 && e.rowTemplateWidth>=0 && e.rowTemplateWidth<=4096,location+"invalid anchors or minimum size");
            require(e.rowHeight>=24 && e.rowHeight<=128 && e.primaryLabel.length()<=24 && e.secondaryLabel.length()<=24, location+"invalid row template");
            if (e.type.equals("item")) require(resource(e.item) || template && e.item.equals("${row.item}"), location + "invalid item"); if(e.type.equals("item_list")) com.wysicraft.runtime.model.ItemRows.parse(e.value);
            if(!e.rowElements.isEmpty()) { require(e.type.equals("item_list"),"Row template requires Item List"); com.wysicraft.runtime.model.RowTemplates.check(e); var row=com.wysicraft.runtime.model.RowTemplates.layout(e); validate(row,manifest,screens,files,true); }
            if (e.type.equals("slots")) {
                require(SLOT_KINDS.contains(e.slotKind), location + "item slots hold player, storage, crafting or result");
                require(e.columns >= 1 && e.rows >= 1 && e.columns <= 9 && e.rows <= 6, location + "item slots are 1-9 columns by 1-6 rows");
                if (e.slotKind.equals("player")) require(e.slotStart >= 0 && e.slotStart + e.columns * e.rows <= 36, location + "player slots are 0-35 (0-8 the hotbar)");
                if (e.slotKind.equals("crafting")) require(e.columns <= 3 && e.rows <= 3, location + "a crafting grid is at most 3 × 3");
                if (e.slotKind.equals("result")) require(e.columns == 1 && e.rows == 1, location + "a crafting result is one slot");
                require(com.wysicraft.runtime.model.ContainerTree.ancestors(ui, e).stream().noneMatch(p -> p.type.equals("scroll_panel")), location + "item slots can't be inside a scroll panel");
            }
            validateEvents(e.events, events(e.type), location, ui, screens, files);
        }
        long crafting = ui.elements.stream().filter(e -> e.type.equals("slots") && e.slotKind.equals("crafting")).count(), results = ui.elements.stream().filter(e -> e.type.equals("slots") && e.slotKind.equals("result")).count();
        require(crafting <= 1 && results <= 1, ui.id + ": at most one crafting grid and one result per screen");
        require(results == 0 || crafting == 1, ui.id + ": a crafting result needs a crafting grid");
        if (ui.elements.stream().anyMatch(e -> e.type.equals("slots"))) require(!ui.responsive, ui.id + ": screens with item slots use a fixed layout (turn off Responsive layout)");
        validateEvents(ui.events, Set.of("open","close","tick","key"), ui.id + ": ", ui, screens, files);
        require(ui.tickInterval == 0 || (ui.tickInterval >= 50 && ui.tickInterval <= 60000), ui.id + ": tick interval must be 0 or 50-60000 ms");
        require(ui.keyRepeat == 0 || (ui.keyRepeat >= 50 && ui.keyRepeat <= 2000), ui.id + ": key repeat must be 0 or 50-2000 ms");
        // Tick and Key are client-only screen events: the server never receives them, so server handlers would never run.
        for (String clientOnly : List.of("tick","key")) { Event ev = ui.events.get(clientOnly); if (ev != null) require(ev.server.actions.isEmpty() && ev.server.script.isEmpty(), ui.id + ": " + clientOnly + " event runs on the client only"); }
    }
    private static void validateEvents(Map<String,Event> events, Set<String> allowed, String location, Ui ui, Map<String,Ui> screens, Map<String,byte[]> files) {
        for (var entry : events.entrySet()) {
            require(allowed.contains(entry.getKey()), location + "unknown event " + entry.getKey());
            for (boolean server : new boolean[]{false,true}) {
                Handler h = server ? entry.getValue().server : entry.getValue().client;
                require(h.permissionLevel>=0 && h.permissionLevel<=4 && h.cooldownTicks>=0 && h.cooldownTicks<=1200, location+"invalid permission/cooldown");
                require(h.actions.size() <= 64, location + "too many actions");
                for (Action a : h.actions) {
                    require((server ? SERVER_ACTIONS : CLIENT_ACTIONS).contains(a.type), location + "unknown action " + a.type);
                    require(a.value.length() <= 4096 && a.target.length() <= 256, location + "action string too long");
                    if (Set.of("set_text","set_visible","set_enabled","set_value","change_texture").contains(a.type)) require(ui.element(a.target) != null, location + "missing target " + a.target);
                    if(a.type.equals("player_inventory")) require(ui.element(a.target)!=null && ui.element(a.target).type.equals("item_list"),location+"Player inventory requires Item List target");
                    if (a.type.equals("open_ui")) require(screens.containsKey(a.value), location + "missing UI " + a.value);
                    if (a.type.equals("set_variable") || a.type.equals("toggle_variable")) require(variable(a.target), location + "invalid variable");
                }
                if (!h.script.isEmpty() || !h.function.isEmpty()) { safePath(h.script); require(h.script.startsWith("scripts/" + (server ? "server/" : "client/")) && files.containsKey(h.script), location + "missing/wrong-side script"); require(files.get(h.script).length <= com.wysicraft.runtime.api.ClientJavaScript.MAX_SCRIPT_BYTES, location + "script exceeds " + com.wysicraft.runtime.api.ClientJavaScript.MAX_SCRIPT_BYTES / 1024 + " KiB"); require(h.function.matches("[a-zA-Z_][a-zA-Z0-9_]*"), location + "invalid function"); }
            }
        }
    }
}


