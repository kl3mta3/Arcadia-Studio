using System.IO.Compression;
using System.Text;
using Wysicraft.Models;
using Wysicraft.Core;
namespace Wysicraft.Packaging;

public static class ProjectStore
{
    /// <summary>Arcadia Studio projects are .arcadia files. Projects saved before the rename (.wysicraftproj) still open
    /// and save; the editor offers to save them as .arcadia.</summary>
    public const string Extension = ".arcadia", LegacyExtension = ".wysicraftproj";
    public static bool IsProjectFile(string path) => path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) || path.EndsWith(LegacyExtension, StringComparison.OrdinalIgnoreCase);
    public static void SaveProject(Project project, string path)
    {
        if (!IsProjectFile(path)) throw new InvalidDataException("Use a .arcadia project file.");
        // Saving preserves unassigned scripts and unfinished work; export validates separately.
        var files = Files(Json.CloneProject(project), false);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            using (var output = File.Create(temporary))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
                foreach (var file in files) { using var stream = archive.CreateEntry(file.Key, Level(file.Key)).Open(); stream.Write(file.Value); }
            File.Move(temporary, path, true);
        } finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    // PNGs are already compressed; recompressing them only costs time.
    static CompressionLevel Level(string name) => name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
    public const int MaxEntry = 32 * 1024 * 1024, MaxPack = 256 * 1024 * 1024, MaxTextureDimension = 8192;
    public static (int Width, int Height) TextureSize(byte[] bytes)
    {
        if (bytes.Length > MaxEntry) throw new InvalidDataException("Texture exceeds 32 MiB");
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new InvalidDataException("Expected a PNG image");
        int width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
        int height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        if (width < 1 || height < 1 || width > MaxTextureDimension || height > MaxTextureDimension) throw new InvalidDataException("Maximum texture dimensions are 8192 × 8192");
        return (width, height);
    }
    public static void Save(Project project, string folder)
    {
        var files = Files(project, false); Directory.CreateDirectory(folder);
        foreach (var (name, bytes) in files) { var path = Resolve(folder, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path + ".tmp", bytes); File.Move(path + ".tmp", path, true); }
    }
    /// <summary>A texture ID in the form a pack uses for it (textures/gui/... paths collapse to the short form).</summary>
    public static string CanonicalResource(Project p, string resource) {
        if (!resource.StartsWith(p.Manifest.Id + ":") || !TextureAssets.TryGet(p, resource, out _)) return resource;
        string path = resource.Split(':', 2)[1];
        if (!path.StartsWith("textures/")) return TextureAssets.Resource(p.Manifest.Id, path);
        if (path.StartsWith("textures/gui/") && !path[13..].Contains('/')) return TextureAssets.Resource(p.Manifest.Id, path[13..]);
        return resource;
    }
    /// <summary>One screen as a pack or web export writes it: a copy with canonical texture IDs and row templates
    /// filled in. Component screens go through this too when a web export carries them for runtime spawning.</summary>
    public static UiDefinition PackScreen(Project p, UiDefinition original, bool pack)
    {
        string CanonicalResource(string resource) => ProjectStore.CanonicalResource(p, resource);
        var ui = Json.Clone(original); if(pack)ui.ComponentInstances.Clear();
        if (!Validation.Id(ui.Id)) throw new InvalidDataException("Invalid UI ID");
        foreach (var element in ui.Elements) { element.Texture = CanonicalResource(element.Texture); if(pack && element.RowTemplate.Length>0) {element.RowTemplateWidth=p.Screens.First(s=>s.Id==element.RowTemplate).Size.Width;element.RowElements=Json.Clone(RowTemplates.Resolve(p,element));foreach(var rowElement in element.RowElements)rowElement.Texture=CanonicalResource(rowElement.Texture);} }
            foreach (var ev in ui.Events.Values.Concat(ui.Elements.SelectMany(e => e.Events.Values)))
                foreach (var action in ev.Client.Actions.Concat(ev.Server.Actions))
                    if (action.Type == "change_texture") action.Value = CanonicalResource(action.Value);
        return ui;
    }
    public static Dictionary<string, byte[]> Files(Project p, bool pack)
    {
        if(pack) { p=Json.Clone(p); p.Manifest.RuntimeVersion=Distribution.RuntimeVersion; }
        p.Manifest.Ui = p.Screens.Where(s=>!pack || !s.IsComponent).Select(s => s.Id).ToList();
        Dictionary<string, byte[]> files = new() { [pack ? "manifest.json" : "project.json"] = Encoding.UTF8.GetBytes(Json.Write(p.Manifest)) };
        foreach (var original in p.Screens.Where(s=>!pack || !s.IsComponent)) {
            var ui = PackScreen(p, original, pack);
            files.Add("ui/" + ui.Id + ".json", Encoding.UTF8.GetBytes(Json.Write(ui)));
        }
        var usedScripts = p.Screens.Where(s=>!s.IsComponent).SelectMany(s => s.Events.Values.Concat(s.Elements.SelectMany(e => e.Events.Values)))
            .SelectMany(e => new[] { e.Client.Script, e.Server.Script }).Where(s => s.Length > 0).ToHashSet(StringComparer.Ordinal);
        int scriptBytes = Limits.For(p).ScriptBytes;
        foreach (var (path, code) in p.Scripts) { if (pack && !usedScripts.Contains(path)) continue; if (!path.StartsWith("scripts/") || !path.EndsWith(".js") || Limits.SizeOf(code) > scriptBytes) throw new InvalidDataException("Invalid script"); files.Add(Validation.SafePath(path), Encoding.UTF8.GetBytes(code)); }
        foreach (var (path, data) in TextureAssets.CanonicalAssets(p)) { if (pack && TextureAssets.IsEditorOnly(path)) continue; if (data.Length > MaxEntry) throw new InvalidDataException("Invalid asset"); files.Add(path, data); }
        // Publishing settings stay with the project file only: never in a pack or an export.
        if (!pack && !p.Publishing.IsEmpty)
        {
            files["publishing.json"] = Encoding.UTF8.GetBytes(Json.Write(p.Publishing));
            if (p.Publishing.Screenshot.Length > 0 && p.Publishing.ScreenshotType is "png" or "jpg" or "webp") files["publishing/screenshot." + p.Publishing.ScreenshotType] = p.Publishing.Screenshot;
        }
        if (files.Sum(f => (long)f.Value.Length) > MaxPack) throw new InvalidDataException("Pack exceeds 256 MiB");
        return files;
    }
    public static void Export(Project project, string destination)
    {
        if (project.Screens.Where(s=>!s.IsComponent).SelectMany(s => s.Events.Values.Concat(s.Elements.SelectMany(e => e.Events.Values))).Any(e => e.Server.Script.Length > 0 && e.Server.ScriptEngine == "kubejs")) throw new InvalidDataException("This project has KubeJS scripts. Use Export for KubeJS.");
        var errors = Validation.Check(project); if (errors.Count != 0) throw new InvalidDataException(string.Join("\n", errors));
        Compatibility.RequireMinecraftScripts(project);
        var files = Files(project, true);
        string temporary = destination + ".tmp";
        try {
            using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create)) foreach (var (name, bytes) in files) { using var stream = archive.CreateEntry(name, Level(name)).Open(); stream.Write(bytes); }
            File.Move(temporary, destination, true);
        } finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static Project Load(string path)
    {
        Dictionary<string, byte[]> files = []; long total = 0;
        void Add(string name, Stream stream) { Validation.SafePath(name); using var data = new MemoryStream(); var buffer = new byte[8192]; int n; while ((n = stream.Read(buffer)) > 0) { total += n; if (data.Length + n > MaxEntry || total > MaxPack) throw new InvalidDataException("Pack size limit"); data.Write(buffer, 0, n); } if (!files.TryAdd(name, data.ToArray())) throw new InvalidDataException("Duplicate archive path"); }
        if (File.Exists(path))
        {
            if (new FileInfo(path).Length > MaxPack) throw new InvalidDataException("Pack size limit");
            using var zip = ZipFile.OpenRead(path); if (zip.Entries.Count > 2048) throw new InvalidDataException("Too many files");
            foreach (var entry in zip.Entries) { if (entry.FullName.EndsWith('/')) { Validation.SafePath(entry.FullName.TrimEnd('/')); continue; } using var stream = entry.Open(); Add(entry.FullName, stream); }
        }
        else
        {
            foreach (string file in Directory.EnumerateFiles(path, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })) { if (files.Count >= 2048) throw new InvalidDataException("Too many files"); using var stream = File.OpenRead(file); Add(Path.GetRelativePath(path, file).Replace('\\', '/'), stream); }
        }
        string Text(string name) => files.TryGetValue(name, out var bytes) ? Encoding.UTF8.GetString(bytes) : throw new InvalidDataException("Missing " + name);
        var manifest = Json.Read<Manifest>(Text(files.ContainsKey("manifest.json") ? "manifest.json" : "project.json"));
        if (manifest.SchemaVersion != 1) throw new InvalidDataException("Unsupported schema version");
        Project project = new() { Manifest = manifest, Screens = [] };
        foreach (string id in manifest.Ui) { if (!Validation.Id(id)) throw new InvalidDataException("Invalid UI ID"); var ui = Json.Read<UiDefinition>(Text("ui/" + id + ".json")); if (ui.Id != id) throw new InvalidDataException("UI file ID mismatch"); project.Screens.Add(ui); }
        foreach (var (name, bytes) in files) { if (name.StartsWith("scripts/")) project.Scripts.Add(name, Encoding.UTF8.GetString(bytes)); if (name.StartsWith("assets/")) project.Assets.Add(name, bytes); }
        // Old source folders may retain legacy files after a save; the canonical copy wins.
        foreach (var legacy in project.Assets.Keys.Where(p => p.StartsWith("assets/textures/")).ToArray())
            if (project.Assets.ContainsKey(TextureAssets.Path(manifest.Id, legacy[16..]))) project.Assets.Remove(legacy);
        string flatPrefix = "assets/" + manifest.Id + "/textures/gui/";
        foreach (var flat in project.Assets.Keys.Where(p => p.StartsWith(flatPrefix) && !p[flatPrefix.Length..].Contains('/')).ToArray())
            if (project.Assets.ContainsKey(TextureAssets.Path(manifest.Id, flat[flatPrefix.Length..]))) project.Assets.Remove(flat);
        project.Assets = TextureAssets.CanonicalAssets(project);
        if (files.TryGetValue("publishing.json", out var publishing))
        {
            project.Publishing = Json.Read<PublishSettings>(Encoding.UTF8.GetString(publishing));
            if (files.TryGetValue("publishing/screenshot." + project.Publishing.ScreenshotType, out var shot)) project.Publishing.Screenshot = shot;
        }
        if (files.ContainsKey("manifest.json")) { var errors = Validation.Check(project); if (errors.Count != 0) throw new InvalidDataException(string.Join("\n", errors)); }
        return project;
    }
    static string Resolve(string root, string relative)
    {
        Validation.SafePath(relative); string full = Path.GetFullPath(Path.Combine(root, relative));
        string basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Path escapes project");
        string? cursor = full; while (cursor != null && cursor.Length >= basePath.Length) { if ((File.Exists(cursor) || Directory.Exists(cursor)) && File.GetAttributes(cursor).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Symlinks are not allowed"); cursor = Path.GetDirectoryName(cursor); }
        return full;
    }
}


