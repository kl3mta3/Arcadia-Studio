using System.IO.Compression;
using Wysicraft.Core;
using Wysicraft.Models;

namespace Wysicraft.Packaging;

public sealed record JarTestPackage(string ProjectId, Dictionary<string, byte[]> Files)
{
    public static JarTestPackage Read(string path)
    {
        if (new FileInfo(path).Length > ProjectStore.MaxPack) throw new InvalidDataException("Export exceeds 256 MiB.");
        byte[] bytes = File.ReadAllBytes(path);
        if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)) {
            using var installation = new ZipArchive(new MemoryStream(bytes));
            var projects = installation.Entries.Where(e => e.FullName.StartsWith("server/mods/") && e.FullName.EndsWith(".jar") && !e.FullName.StartsWith("server/mods/wysicraft-")).ToList();
            if (projects.Count != 1) throw new InvalidDataException("Select an Installation ZIP from Export → Installation ZIP. A KubeJS-files ZIP does not contain a project JAR.");
            return ReadJar(ReadEntry(projects[0], ProjectStore.MaxPack), relative => {
                var companion = installation.GetEntry("server/" + relative) ?? throw new InvalidDataException("Installation ZIP is missing " + relative);
                return ReadEntry(companion, ProjectStore.MaxEntry);
            });
        }
        return ReadJar(bytes, relative => {
            string root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(path)))!;
            string companion = Path.Combine(root, relative);
            if (!File.Exists(companion)) throw new InvalidDataException("This JAR needs its exported KubeJS scripts. Select the Installation ZIP directly, or keep server/kubejs beside server/mods.");
            if (new FileInfo(companion).Length > ProjectStore.MaxEntry) throw new InvalidDataException("KubeJS companion is too large.");
            return File.ReadAllBytes(companion);
        });
    }
    static byte[] ReadEntry(ZipArchiveEntry entry, int limit) {
        if (entry.Length > limit) throw new InvalidDataException("Export entry is too large: " + entry.FullName);
        using var source = entry.Open(); using var output = new MemoryStream(); source.CopyTo(output); return output.ToArray();
    }
    static JarTestPackage ReadJar(byte[] bytes, Func<string, byte[]> readCompanion)
    {
        using var jar = new ZipArchive(new MemoryStream(bytes));
        if (jar.GetEntry("META-INF/neoforge.mods.toml") == null) throw new InvalidDataException("Select an exported NeoForge project JAR, not the runtime JAR or a project file.");
        var packs = jar.Entries.Where(e => e.FullName.StartsWith("wysicraft/") && e.FullName.EndsWith(".wysicraft")).ToList();
        if (packs.Count != 1 || packs[0].Length > ProjectStore.MaxPack) throw new InvalidDataException("Select an Arcadia Studio project JAR containing exactly one project.");
        using var packBytes = new MemoryStream();
        using (var source = packs[0].Open()) source.CopyTo(packBytes);
        packBytes.Position = 0;
        using var pack = new ZipArchive(packBytes);
        var entry = pack.GetEntry("manifest.json") ?? throw new InvalidDataException("The embedded project manifest is missing.");
        if (entry.Length > 65536) throw new InvalidDataException("Project manifest is too large.");
        using var reader = new StreamReader(entry.Open());
        var manifest = Json.Read<Manifest>(reader.ReadToEnd());
        if (!Validation.Id(manifest.Id) || packs[0].FullName != "wysicraft/" + manifest.Id + ".wysicraft") throw new InvalidDataException("Invalid project ID in the JAR.");
        if (!Version.TryParse(manifest.RuntimeVersion, out var version) || version > Version.Parse(Distribution.RuntimeVersion)) throw new InvalidDataException("Update Arcadia Studio to test this project's required runtime: " + manifest.RuntimeVersion);
        var files = new Dictionary<string, byte[]> { ["mods/wysicraft-test-project.jar"] = bytes };
        if (manifest.Dependencies.Contains("kubejs")) {
            foreach (string kind in new[] { "server_scripts", "startup_scripts" }) {
                string relative = "kubejs/" + kind + "/wysicraft/" + manifest.Id + ".js";
                if (jar.GetEntry("wysicraft_kube/" + relative[7..]) != null) continue;
                files[relative] = readCompanion(relative);
            }
        }
        return new(manifest.Id, files);
    }
}
