using System.IO.Compression;
using System.Text;
using Wysicraft.Core;
using Wysicraft.Models;
namespace Wysicraft.Packaging;

public static class Distribution
{
    public const string RuntimeVersion=RuntimeInfo.Version;
    public static byte[] ProjectJar(Project source, bool clientOnly=false)
    {
        var project=Json.Clone(source);
        if(project.Manifest.Id.Length<2) throw new InvalidDataException("Project JAR mod IDs need at least two characters.");
        if(project.Manifest.Id is "minecraft" or "neoforge" or "wysicraft") throw new InvalidDataException("Choose your own project ID before exporting a mod JAR.");
        Dictionary<string,byte[]> packFiles;
        if(UsesKube(project)) {
            var bundle=KubeExport.Files(project);
            using var input=new MemoryStream(bundle["wysicraft/"+project.Manifest.Id+".wysicraft"]);
            using var zip=new ZipArchive(input,ZipArchiveMode.Read);
            packFiles=zip.Entries.ToDictionary(e=>e.FullName,e=>{ using var s=e.Open(); using var m=new MemoryStream(); s.CopyTo(m); return m.ToArray(); });
        } else {
            var errors=Validation.Check(project); if(errors.Count>0) throw new InvalidDataException(string.Join("\n",errors));
            packFiles=ProjectStore.Files(project,true);
        }
        // Client distribution never includes server script source or trusted command definitions.
        if(clientOnly) {
            foreach(var key in packFiles.Keys.Where(k=>k.StartsWith("scripts/server/")).ToList()) packFiles.Remove(key);
            foreach(var key in packFiles.Keys.Where(k=>k.StartsWith("ui/")).ToList()) {
                var screen=Json.Read<UiDefinition>(Encoding.UTF8.GetString(packFiles[key]));
                foreach(var ev in screen.Events.Values.Concat(screen.Elements.SelectMany(e=>e.Events.Values))) ev.Server=new();
                packFiles[key]=Encoding.UTF8.GetBytes(Json.Write(screen));
            }
            var manifest=Json.Read<Manifest>(Encoding.UTF8.GetString(packFiles["manifest.json"])); manifest.Dependencies.Remove("kubejs");
            packFiles["manifest.json"]=Encoding.UTF8.GetBytes(Json.Write(manifest));
        }
        string id=project.Manifest.Id;
        // JSON string literals also provide correct escaping for TOML basic strings.
        string metadata=$"modLoader=\"lowcodefml\"\nloaderVersion=\"[4,)\"\nlicense=\"All Rights Reserved\"\n[[mods]]\nmodId=\"{id}\"\nversion={Json.Write(project.Manifest.Version)}\ndisplayName={Json.Write(project.Manifest.Name)}\n[[dependencies.{id}]]\nmodId=\"wysicraft\"\ntype=\"required\"\nversionRange=\"[{RuntimeVersion},)\"\nordering=\"AFTER\"\nside=\"BOTH\"\n[[dependencies.{id}]]\nmodId=\"minecraft\"\ntype=\"required\"\nversionRange=\"[1.21.1]\"\nordering=\"NONE\"\nside=\"BOTH\"\n";
        var files=new Dictionary<string,byte[]> { ["META-INF/neoforge.mods.toml"]=Encoding.UTF8.GetBytes(metadata),["wysicraft/"+id+".wysicraft"]=Zip(packFiles) };
        foreach(var file in packFiles.Where(f=>f.Key.StartsWith("assets/"))) files[file.Key]=file.Value;
        // Project sounds: Minecraft finds them through the mod's sounds.json (one entry per .ogg file).
        foreach(var group in packFiles.Keys.Where(k=>SoundAssets.IsSound(k) && k.EndsWith(".ogg",StringComparison.OrdinalIgnoreCase)).GroupBy(k=>k.Split('/')[1])) {
            var events=group.ToDictionary(path=>SoundAssets.Resource(path).Split(':',2)[1],path=>new { sounds=new[]{ SoundAssets.Resource(path) } });
            files[$"assets/{group.Key}/sounds.json"]=Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(events));
        }
        return Zip(files);
    }
    public static bool UsesKube(Project p) => p.Screens.Where(s=>!s.IsComponent).SelectMany(s=>s.Events.Values.Concat(s.Elements.SelectMany(e=>e.Events.Values))).Any(e=>e.Server.Script.Length>0 && e.Server.ScriptEngine=="kubejs");
    public static byte[] BundledJar(Project project, string runtimeJar, bool clientOnly = false)
    {
        using var runtime = ZipFile.OpenRead(runtimeJar);
        using var metadataReader = new StreamReader((runtime.GetEntry("META-INF/neoforge.mods.toml") ?? throw new InvalidDataException("Invalid runtime JAR")).Open());
        if (!metadataReader.ReadToEnd().Contains("version=\"" + RuntimeVersion + "\"")) throw new InvalidDataException("Bundled export needs runtime " + RuntimeVersion);
        using var source = new ZipArchive(new MemoryStream(ProjectJar(project,clientOnly)));
        var files = source.Entries.ToDictionary(e=>e.FullName,e=>{using var s=e.Open();using var m=new MemoryStream();s.CopyTo(m);return m.ToArray();});
        string path = "META-INF/jarjar/wysicraft-" + RuntimeVersion + ".jar";
        files[path] = File.ReadAllBytes(runtimeJar);
        files["META-INF/jarjar/metadata.json"] = Encoding.UTF8.GetBytes(Json.Write(new { jars = new[] { new { identifier = new { group="com.wysicraft.runtime",artifact="wysicraft" },version=new { range="["+RuntimeVersion+",2.0.0)",artifactVersion=RuntimeVersion },path,isObfuscated=false } } }));
        if (!clientOnly && UsesKube(project)) {
            foreach(var file in KubeExport.Files(project).Where(f=>f.Key.StartsWith("kubejs/"))) files["wysicraft_kube/"+file.Key[7..]]=file.Value;
            string id=project.Manifest.Id;
            files["META-INF/neoforge.mods.toml"] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(files["META-INF/neoforge.mods.toml"])+$"[[dependencies.{id}]]\nmodId=\"kubejs\"\ntype=\"required\"\nversionRange=\"[2101.7.2,)\"\nordering=\"AFTER\"\nside=\"BOTH\"\n");
        }
        return Zip(files);
    }
    public static void Export(Project project,string destination,string runtimeJar)
    {
        if(!File.Exists(runtimeJar)) throw new InvalidDataException("Runtime JAR is missing. Keep Runtime beside Designer.");
        using(var archive=ZipFile.OpenRead(runtimeJar)) {
            var metadata=archive.GetEntry("META-INF/neoforge.mods.toml") ?? throw new InvalidDataException("Invalid runtime JAR");
            using var reader=new StreamReader(metadata.Open()); if(!reader.ReadToEnd().Contains("version=\""+RuntimeVersion+"\"")) throw new InvalidDataException("Installation export requires runtime "+RuntimeVersion);
        }
        string id=project.Manifest.Id;
        var files=new Dictionary<string,byte[]> {
            ["server/mods/"+id+".jar"]=BundledJar(project,runtimeJar),
            ["client/mods/"+id+".jar"]=BundledJar(project,runtimeJar,true)
        };
        files["INSTALL.txt"]=Encoding.UTF8.GetBytes($"{project.Manifest.Name} — Minecraft 1.21.1 / NeoForge 21.1.250+\nInstall server/mods on the server and client/mods on clients. Single player uses server/mods.\nThe Arcadia Studio runtime is bundled inside each project JAR. No desktop app or separate runtime installation is needed.\nRemove older copies of this project and restart Minecraft. Open /{id}.open; close /{id}.close.\n"+(UsesKube(project)?"Server behavior requires matching KubeJS 2101 and Rhino. Scripts load directly from the server project JAR.\n":"Standard scripts need no other scripting mod.\n"));        Write(destination,Zip(files));
    }
    public static void Write(string path,byte[] bytes) { string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp"; try { File.WriteAllBytes(temp,bytes); File.Move(temp,path,true); } finally { if(File.Exists(temp)) File.Delete(temp); } }
    static byte[] Zip(Dictionary<string,byte[]> files) { using var buffer=new MemoryStream(); using(var zip=new ZipArchive(buffer,ZipArchiveMode.Create,true)) foreach(var file in files) { using var output=zip.CreateEntry(file.Key,CompressionLevel.Optimal).Open(); output.Write(file.Value); } return buffer.ToArray(); }
}

