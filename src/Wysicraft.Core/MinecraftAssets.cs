using System.IO.Compression;
using System.Text.Json;
namespace Wysicraft.Core;

// Reads the user's installed assets. Minecraft files are never copied into projects.
public sealed class MinecraftAssets
{
    readonly Dictionary<string,JsonElement> models=[];
    readonly Dictionary<string,(string Jar,string Entry)> textures=[];
    readonly Dictionary<string,string> names=[];
    readonly Dictionary<string,(string Jar,string Entry)> animations=[];
    public IEnumerable<(string Id,string Name)> Items {
        get {
            var variants=new HashSet<string>();
            foreach(var model in models.Values)if(model.TryGetProperty("overrides",out var overrides))foreach(var v in overrides.EnumerateArray())if(v.TryGetProperty("model",out var m))variants.Add(Resource(m.GetString()!));
            return models.Keys.Where(k=>k.Contains(":item/") && !variants.Contains(k) && !k.Contains("/template_") && !k.EndsWith("/generated") && !k.EndsWith("_in_hand"))
                .Select(k=>k.Replace(":item/",":" )).Select(id=>(id,Name(id))).OrderBy(e=>e.Item2).ToArray();
        }
    }
    /// <summary>Every texture the loaded game jar has, as resource IDs. Used to offer real Minecraft
    /// textures alongside the project’s own images; empty until a jar has been loaded.</summary>
    public IEnumerable<string> TextureIds => textures.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase);
    public string Name(string id) {var key=id.Replace(':','.');return names.GetValueOrDefault("item."+key)??names.GetValueOrDefault("block."+key)??System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Split(':').Last().Replace('_',' '));}
    static string Resource(string id)=>id.Contains(':')?id:"minecraft:"+id;
    public void LoadJar(string path) {
        using var zip=ZipFile.OpenRead(path);
        var incoming=new Dictionary<string,JsonElement>();var images=new Dictionary<string,(string,string)>();var metas=new Dictionary<string,(string,string)>();var labels=new Dictionary<string,string>();
        foreach(var entry in zip.Entries) {
            var parts=entry.FullName.Split('/',3);if(parts.Length!=3 || parts[0]!="assets")continue;
            if(parts[2].StartsWith("models/") && parts[2].EndsWith(".json") && entry.Length<1024*1024) {
                using var stream=entry.Open();using var doc=JsonDocument.Parse(stream);incoming[parts[1]+":"+parts[2][7..^5]]=doc.RootElement.Clone();
            } else if(parts[2].StartsWith("textures/") && parts[2].EndsWith(".png") && entry.Length<=32*1024*1024)images[parts[1]+":"+parts[2][9..^4]]=(path,entry.FullName);
            else if(parts[2].StartsWith("textures/") && parts[2].EndsWith(".png.mcmeta") && entry.Length<=64*1024)metas[parts[1]+":"+parts[2]]=(path,entry.FullName);
            else if(parts[2]=="lang/en_us.json" && entry.Length<8*1024*1024) {using var stream=entry.Open();using var doc=JsonDocument.Parse(stream);foreach(var pair in doc.RootElement.EnumerateObject())if(pair.Value.ValueKind==JsonValueKind.String)labels[pair.Name]=pair.Value.GetString()!;}
        }
        if(incoming.Count==0)throw new InvalidDataException("No Minecraft item/block models found in this JAR.");
        foreach(var pair in incoming)models[pair.Key]=pair.Value;foreach(var pair in images)textures[pair.Key]=pair.Value;foreach(var pair in labels)names[pair.Key]=pair.Value;foreach(var pair in metas)animations[pair.Key]=pair.Value;
    }
    // Model chain for an item: its merged texture variables and every model ID it inherits from.
    (Dictionary<string,string> Values,HashSet<string> Chain)? Resolve(string item) {
        var split=item.Split(':',2);if(split.Length!=2)return null;
        var values=new Dictionary<string,string>();var seen=new HashSet<string>();
        void Read(string id) {
            if(seen.Count>=32 || !seen.Add(id) || !models.TryGetValue(id,out var model))return;
            if(model.TryGetProperty("parent",out var parent))Read(Resource(parent.GetString()!));
            if(model.TryGetProperty("textures",out var map))foreach(var p in map.EnumerateObject())values[p.Name]=p.Value.GetString()!;
        }
        Read(split[0]+":item/"+split[1]);
        return (values,seen);
    }
    byte[]? Load(Dictionary<string,string> values,params string[] keys) {
        string? texture=keys.Select(k=>values.GetValueOrDefault(k)).FirstOrDefault(v=>v!=null);
        for(int i=0;texture?.StartsWith('#')==true && i<32;i++)texture=values.GetValueOrDefault(texture[1..]);
        if(texture==null || texture.StartsWith('#') || !textures.TryGetValue(Resource(texture),out var location))return null;
        using var zip=ZipFile.OpenRead(location.Jar);using var input=zip.GetEntry(location.Entry)!.Open();using var output=new MemoryStream();input.CopyTo(output);return output.ToArray();
    }
    /// <summary>A texture file by resource, e.g. "minecraft:textures/block/bricks.png". Null when not in the loaded JARs.</summary>
    public byte[]? TextureFile(string resource) {
        var split=resource.Split(':',2);if(split.Length!=2 || !split[1].StartsWith("textures/") || !split[1].EndsWith(".png"))return null;
        return Load(new(){["t"]=split[0]+":"+split[1][9..^4]},"t");
    }
    /// <summary>The .png.mcmeta beside a texture, e.g. for "minecraft:textures/block/prismarine.png". Null when there is none.</summary>
    public string? TextureMeta(string resource) {
        if(!animations.TryGetValue(resource+".mcmeta",out var location))return null;
        using var zip=ZipFile.OpenRead(location.Jar);using var reader=new StreamReader(zip.GetEntry(location.Entry)!.Open());return reader.ReadToEnd();
    }
    public byte[]? Texture(string item) {
        var resolved=Resolve(item);return resolved==null?null:Load(resolved.Value.Values,"layer0","all","side","front","top","particle");
    }
    /// <summary>For full-cube blocks (bricks, logs, furnaces…), the top, left and right faces Minecraft shows in its
    /// inventory icon. Null for flat items and non-cube blocks, which use <see cref="Texture"/>.</summary>
    public (byte[] Top,byte[] Left,byte[] Right)? CubeFaces(string item) {
        var resolved=Resolve(item);if(resolved==null)return null;var (values,chain)=resolved.Value;
        if(values.ContainsKey("layer0") || !chain.Contains("minecraft:block/cube"))return null;
        var top=Load(values,"up","top","end","all","particle");var left=Load(values,"north","front","side","all","particle");var right=Load(values,"east","side","all","particle");
        return top==null||left==null||right==null?null:(top,left,right);
    }
}
