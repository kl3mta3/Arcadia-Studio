using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Wysicraft.Core;
using Wysicraft.Models;

namespace Wysicraft.Packaging;

public static class KubeExport
{
    public static Dictionary<string, byte[]> Files(Project source)
    {
        var errors = Validation.Check(source);
        if (errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors));
        var project = Json.Clone(source);
        var registrations = new StringBuilder();
        var handlers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var screen in project.Screens.Where(s=>!s.IsComponent))
        foreach (var ev in screen.Events.Values.Concat(screen.Elements.SelectMany(e => e.Events.Values)))
        {
            if (ev.Client.Script.Length > 0 && ev.Client.ScriptEngine != "standard") throw new InvalidDataException("Client scripts must use the Standard engine: " + ev.Client.Script);
            var handler = ev.Server;
            if (handler.Script.Length == 0) continue;
            if (handler.ScriptEngine == "standard") continue;
            string identity = handler.Script + "\n" + handler.Function;
            if (!handlers.TryGetValue(identity, out string? key))
            {
                key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
                handlers.Add(identity, key);
                registrations.AppendLine("(() => {");
                int sourceStart=6+registrations.ToString().Count(c=>c=='\n');
                int sourceLength=project.Scripts[handler.Script].Count(c=>c=='\n')+1;
                registrations.AppendLine(project.Scripts[handler.Script]);
                registrations.AppendLine($"API.registerKubeHandler(projectId, {Json.Write(key)}, (context, value) => {{");
                registrations.AppendLine("  const player = context.player();");
                registrations.AppendLine("  const ui = {setItem: (id, item) => API.setItem(player, id, String(item)), setItems: (id, items) => API.setItems(player, id, JSON.stringify(items)), showPlayerInventory: id => API.showPlayerInventory(player, id), setText: (id, text) => API.setText(player, id, String(text)), setValue: (id, value) => API.setValue(player, id, String(value)), setVisible: (id, visible) => API.setVisible(player, id, !!visible), setEnabled: (id, enabled) => API.setEnabled(player, id, !!enabled), setVariable: (name, value) => API.setVariable(player, name, String(value)), getVariable: name => context.state().get(String(name)), open: id => API.openUi(player, String(id).includes(':') ? String(id) : projectId + ':' + id), close: () => API.closeProject(player, projectId)};");
                registrations.AppendLine("  const event = {player: player, server: player.getServer(), value: String(context.state().getOrDefault('event_value', '')), ui: ui, runCommand: command => API.runCommand(player, String(command)), message: text => API.message(player, String(text)), state: {get: ui.getVariable, set: ui.setVariable}};");
                registrations.AppendLine($"  try {{ {handler.Function}(event); }} catch (error) {{ const line=Number(error.lineNumber || 0); const location=line>={sourceStart} && line<{sourceStart+sourceLength} ? ' line '+(line-{sourceStart}+1) : ''; throw new Error({Json.Write("Arcadia Studio script " + handler.Script + " :: " + handler.Function)} + location + ': ' + error); }}");
                registrations.AppendLine("});\n})();");
            }
            handler.Actions.Add(new VisualAction { Type = "server_function", Target = project.Manifest.Id + ":kube:" + key });
            if (handler.Actions.Count > 64) throw new InvalidDataException("KubeJS handler needs one action slot; reduce existing actions to 63.");
            handler.Script = ""; handler.Function = ""; handler.ScriptEngine = "standard";
        }
        if (!project.Manifest.Dependencies.Contains("kubejs")) project.Manifest.Dependencies.Add("kubejs");
        string id = project.Manifest.Id;
        string code = "// Generated Arcadia Studio server handlers. Install on the server; /reload after replacement.\n(() => {\nconst API = Java.loadClass('com.wysicraft.runtime.api.WysicraftApi');\nconst projectId = " + Json.Write(id) + ";\nAPI.clearKubeHandlers(projectId);\n" + registrations + "\nconsole.info('Arcadia Studio: registered " + handlers.Count + " handlers for ' + projectId);\n})();\n";
        Compatibility.RequireMinecraftScripts(project);
        using var pack = new MemoryStream();
        using (var zip = new ZipArchive(pack, ZipArchiveMode.Create, true))
            foreach (var file in ProjectStore.Files(project, true)) { using var stream = zip.CreateEntry(file.Key).Open(); stream.Write(file.Value); }
        return new Dictionary<string, byte[]>
        {
            ["wysicraft/" + id + ".wysicraft"] = pack.ToArray(),
            ["kubejs/server_scripts/wysicraft/" + id + ".js"] = Encoding.UTF8.GetBytes(code),
            ["kubejs/startup_scripts/wysicraft/" + id + ".js"] = Encoding.UTF8.GetBytes("// Project helper: KubeJS globals must be defined at startup.\n(() => { const API = Java.loadClass('com.wysicraft.runtime.api.WysicraftApi'); const id = " + Json.Write(id) + "; global[id] = {open: player => API.openProject(player, id), close: player => API.closeProject(player, id)}; })();\n"),
            ["INSTALL.txt"] = Encoding.UTF8.GetBytes($"Arcadia Studio KubeJS export for {id}\nMinecraft 1.21.1 / NeoForge, KubeJS 2101 and its dependencies.\nInstall the updated Arcadia Studio runtime JAR on server and clients.\nMerge kubejs/ and wysicraft/ from this ZIP into the server directory.\nCopy the wysicraft/ pack to clients for images; do not distribute server scripts to clients.\nRestart for first installation or changed startup helpers. After replacing server handlers/packs run /reload, then /wysicraft reload.\nOpen /{id}.open, or /{id}.open <player> from server commands.\nFrom a KubeJS SERVER event: global.{id}.open(event.player). Close works the same way.\nOnly assigned KubeJS server scripts are exported. Built-in client actions remain active.\nTest scripts in Minecraft; the desktop preview cannot execute KubeJS Minecraft APIs.\nExisting standalone scripts are not automatically translated.\n")
        };
    }
    public static void Export(Project project, string destination)
    {
        var files = Files(project);
        string temporary = destination + ".tmp";
        try {
            using (var output = File.Create(temporary))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
                foreach (var file in files) { using var stream = zip.CreateEntry(file.Key, CompressionLevel.Optimal).Open(); stream.Write(file.Value); }
            File.Move(temporary, destination, true);
        } finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
