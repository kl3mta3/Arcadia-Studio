using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Wysicraft.Core;
using Wysicraft.Models;
using Wysicraft.Packaging;
using Validation = Wysicraft.Core.Validation;
namespace Wysicraft.Designer;

// Project sounds: import (.ogg, .mp3, .wav, .m4a/.aac), pick them for play_sound actions and Sound controls.
// Minecraft only plays .ogg; anything else is flagged by the Minecraft check with where it's used.
public partial class MainWindow
{
    const string SoundFilter = "Sounds|*.ogg;*.mp3;*.wav;*.m4a;*.aac|Ogg Vorbis (works in Minecraft)|*.ogg";
    static bool IsSoundFile(string path) => SoundAssets.Extensions.Any(x => path.EndsWith(x, StringComparison.OrdinalIgnoreCase)) && File.Exists(path);

    /// <summary>Imports sound files into the project and returns their sound IDs (project id:name).</summary>
    List<string> ImportSoundFiles(IEnumerable<string> paths)
    {
        var files = paths.Select(p => (Path: p, Bytes: File.ReadAllBytes(p))).ToArray(); var added = new List<string>();
        if (files.Length == 0) return added;
        if (files.Any(f => f.Bytes.Length > ProjectStore.MaxEntry)) throw new InvalidOperationException("Sounds are at most 32 MB each.");
        Change();
        foreach (var file in files)
        {
            string ext = Path.GetExtension(file.Path).ToLowerInvariant(), stem = System.Text.RegularExpressions.Regex.Replace(Path.GetFileNameWithoutExtension(file.Path).ToLowerInvariant(), "[^a-z0-9_-]", "_");
            string path = SoundAssets.Path(project.Manifest.Id, stem + ext); int n = 1;
            while (project.Assets.TryGetValue(path, out var existing) && !existing.SequenceEqual(file.Bytes) || SoundAssets.All(project).Any(p => p != path && SoundAssets.Resource(p) == SoundAssets.Resource(path)))
                path = SoundAssets.Path(project.Manifest.Id, stem + "_" + n++ + ext);
            project.Assets[path] = file.Bytes; added.Add(SoundAssets.Resource(path));
            if (ext != ".ogg" && project.Manifest.Target != "web") Log($"{Path.GetFileName(file.Path)}: Minecraft only plays .ogg sounds. It works in web and desktop exports; Minecraft exports will list where it's used.");
        }
        RefreshAssetBrowser(); Log(added.Count == 1 ? "Imported sound " + added[0] : $"Imported {added.Count} sounds.");
        return added;
    }
    void ImportSounds() { var d = new OpenFileDialog { Filter = SoundFilter, Multiselect = true }; if (d.ShowDialog() == true) ImportSoundFiles(d.FileNames); }

    /// <summary>A sound chooser: the project's sounds, "Import…", or any sound ID typed in (Minecraft sounds too).</summary>
    void SoundPicker(Panel panel, string label, string current, Action<string> apply, string? tip = null)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), ToolTip = tip ?? "A project sound, or a Minecraft sound ID such as minecraft:ui.button.click (Minecraft sounds only play in Minecraft)." };
        row.Children.Add(new TextBlock { Text = label, Width = FieldLabelWidth, VerticalAlignment = VerticalAlignment.Center });
        var import = new Button { Content = "Import…", Margin = new Thickness(4, 0, 0, 0) }; DockPanel.SetDock(import, Dock.Right); row.Children.Add(import);
        var sounds = SoundAssets.All(project).ToList();
        var box = new ComboBox { IsEditable = true, ItemsSource = sounds.Select(p => SoundAssets.Resource(p) + "  (" + Path.GetExtension(p).TrimStart('.') + ")").ToList(), Text = current };
        void Commit(string value)
        {
            int paren = value.IndexOf("  (", StringComparison.Ordinal); if (paren > 0) value = value[..paren];
            value = value.Trim(); if (value == current) return;
            if (value.Length > 0 && !Validation.Resource(value)) { Log("Sound IDs look like " + project.Manifest.Id + ":click or minecraft:ui.button.click"); box.Text = current; return; }
            Change(); apply(value); current = value; if (value.Length > 0 && SoundAssets.Find(project, value) is string file && !file.EndsWith(".ogg") && project.Manifest.Target != "web") Log($"{value} is {Path.GetExtension(file)}: fine for web and desktop, but Minecraft only plays .ogg.");
        }
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is string s) Commit(s); };
        box.LostKeyboardFocus += (_, _) => Commit(box.Text);
        import.Click += (_, _) => Guard(() => { var d = new OpenFileDialog { Filter = SoundFilter }; if (d.ShowDialog() != true) return; var added = ImportSoundFiles([d.FileName]); if (added.Count > 0) { apply(added[0]); current = added[0]; RefreshInspector(); } });
        row.Children.Add(box); panel.Children.Add(row);
    }
}
