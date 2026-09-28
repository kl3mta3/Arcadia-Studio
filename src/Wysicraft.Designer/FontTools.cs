using System.IO;
using System.Windows.Media;
using Microsoft.Win32;
using Wysicraft.Core;
using Fonts = Wysicraft.Core.Fonts;
using WpfFonts = System.Windows.Media.Fonts;
namespace Wysicraft.Designer;

// Typefaces. Minecraft has only the three fonts it ships with, so those stay the default and the rest is advanced
// (web and desktop): the built-in families every browser already has, and font files brought into the project.
//
// WPF can only build a FontFamily from a file on disk, and project assets live in memory, so an imported font is
// written to a cache folder the first time it is drawn and the FontFamily is kept after that.
public partial class MainWindow
{
    static readonly Dictionary<string, FontFamily?> fontFamilies = [];

    /// <summary>What the Typeface dropdown offers: Minecraft's own, the built-in families, and this project's
    /// imported fonts. A Minecraft-only project sees just Minecraft's, because nothing else would work there.</summary>
    List<string> FontChoices()
    {
        var choices = new List<string>(Fonts.Minecraft);
        if (!AdvancedAllowed) return choices;
        choices.AddRange(Fonts.Builtin.Select(f => f.Id));
        choices.AddRange(Fonts.InProject(project));
        return choices;
    }

    string FontNote() => AdvancedAllowed
        ? "minecraft: fonts are the three the game ships with, and the only ones a Minecraft export can use. web: fonts are families every browser and desktop already has. Import a font… brings a .ttf, .otf or .woff2 into the project and ships it with web and desktop exports — the canvas draws it directly, so it looks the same in the game as it does here."
        : "This project is made for Minecraft, which draws its own fonts: only the three minecraft: ones are available. Change Made for to web or both to use other typefaces.";

    /// <summary>Asks for a font file, brings it into the project, and gives back its resource ID.</summary>
    string? ImportFontFile()
    {
        var dialog = new OpenFileDialog { Filter = "Font|*.ttf;*.otf;*.woff2;*.woff", Title = "Import a font" };
        if (dialog.ShowDialog(this) != true) return null;
        var bytes = File.ReadAllBytes(dialog.FileName);
        if (bytes.Length > Fonts.MaxBytes) throw new InvalidOperationException($"{Path.GetFileName(dialog.FileName)} is {bytes.Length / 1024 / 1024} MB; a font travels inside every export, so the limit is {Fonts.MaxBytes / 1024 / 1024} MB.");
        string extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        string stem = System.Text.RegularExpressions.Regex.Replace(Path.GetFileNameWithoutExtension(dialog.FileName).ToLowerInvariant(), "[^a-z0-9_-]", "_");
        string path = Fonts.Path(project.Manifest.Id, stem + extension);
        int n = 1;
        while (project.Assets.TryGetValue(path, out var existing) && !existing.SequenceEqual(bytes)) path = Fonts.Path(project.Manifest.Id, stem + "_" + n++ + extension);
        Change();
        project.Assets[path] = bytes;
        RefreshAssetBrowser();
        string resource = Fonts.Resource(project.Manifest.Id, path);
        Log("Imported " + Path.GetFileName(path) + ". It ships with web and desktop exports; Minecraft will keep using its own font.");
        return resource;
    }

    /// <summary>The WPF family for a control's font: an imported file if that is what it names, otherwise the closest
    /// stand-in for a built-in or Minecraft font. Cached, because building one writes the file out.</summary>
    FontFamily FamilyFor(string font)
    {
        if (Fonts.IsProjectFont(font))
        {
            if (fontFamilies.TryGetValue(font, out var cached)) return cached ?? new FontFamily("Segoe UI");
            FontFamily? built = null;
            try
            {
                if (Fonts.FileOf(project, font) is string asset)
                {
                    // WPF loads a font from a folder URI plus "#Family Name", so the bytes go to a cache file first.
                    var folder = Path.Combine(Path.GetTempPath(), "Wysicraft", "fonts");
                    Directory.CreateDirectory(folder);
                    var file = Path.Combine(folder, Fonts.FamilyName(font) + Path.GetExtension(asset));
                    var bytes = project.Assets[asset];
                    if (!File.Exists(file) || new FileInfo(file).Length != bytes.Length) File.WriteAllBytes(file, bytes);
                    var name = WpfFonts.GetFontFamilies(new Uri(folder + Path.DirectorySeparatorChar)).FirstOrDefault(f => f.Source.Contains(Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase));
                    built = name ?? WpfFonts.GetFontFamilies(new Uri(file)).FirstOrDefault();
                }
            }
            catch (Exception) { built = null; }   // a font WPF can't read still shows as text, in the fallback family
            fontFamilies[font] = built;
            return built ?? new FontFamily("Segoe UI");
        }
        return new FontFamily(font switch
        {
            "web:serif" => "Georgia, Times New Roman",
            "web:mono" => "Cascadia Mono, Consolas",
            "web:rounded" => "Segoe UI Variable, Nunito, Trebuchet MS",
            "web:condensed" => "Arial Narrow, Segoe UI",
            "web:display" => "Impact, Arial Black",
            "web:handwriting" => "Segoe Script, Comic Sans MS",
            "web:sans" => "Segoe UI",
            "minecraft:uniform" => "Segoe UI",
            _ => "Consolas"
        });
    }

    /// <summary>Forgets the cached families, so a re-imported font is picked up rather than the old one.</summary>
    static void ForgetFonts() => fontFamilies.Clear();
}
