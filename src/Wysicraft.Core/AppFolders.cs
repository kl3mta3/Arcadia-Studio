namespace Wysicraft.Core;

/// <summary>Where Arcadia Studio keeps its own files: %LOCALAPPDATA%\Arcadia Studio. Before the rename it was
/// %LOCALAPPDATA%\Wysicraft; on first launch the settings are copied across (never moved: the old folder is left as it
/// was), and large downloads made there (sound fonts, the instrument splitter, Electron, the Minecraft test install)
/// are used where they are rather than copied or downloaded again.</summary>
public static class AppFolders
{
    public const string Name = "Arcadia Studio";
    static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static string Root => System.IO.Path.Combine(LocalAppData, Name);
    public static string Legacy => System.IO.Path.Combine(LocalAppData, "Wysicraft");

    /// <summary>A path inside the Arcadia Studio folder.</summary>
    public static string Path(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);

    /// <summary>A download that may already exist from before the rename: the Arcadia Studio copy if there is one, else
    /// the old one if that exists, else where a new one goes.</summary>
    public static string Existing(params string[] parts) => ExistingIn(Root, Legacy, parts);
    public static string ExistingIn(string root, string legacy, string[] parts)
    {
        string now = System.IO.Path.Combine([root, .. parts]);
        if (File.Exists(now) || Directory.Exists(now)) return now;
        string old = System.IO.Path.Combine([legacy, .. parts]);
        return File.Exists(old) || Directory.Exists(old) ? old : now;
    }

    // What a first launch copies from the old folder: settings and work in progress, all small.
    static readonly string[] SettingsFiles = ["preferences.json", "keybindings.json", "workspace-layout-2.xml", "workspace-layout.xml", "pixel-palette.json", "mcp.json", "minecraft-test.json"];
    static readonly string[] SettingsFolders = ["Recovery", "Asset files"];

    /// <summary>Copies the settings from before the rename, once: only when the Arcadia Studio folder doesn't exist yet.
    /// Returns what was copied (empty when there was nothing to do).</summary>
    public static List<string> CarryOverSettings() => CarryOverSettings(Legacy, Root);
    public static List<string> CarryOverSettings(string legacy, string root)
    {
        var copied = new List<string>();
        if (Directory.Exists(root) || !Directory.Exists(legacy)) return copied;
        Directory.CreateDirectory(root);
        foreach (var name in SettingsFiles)
        {
            string from = System.IO.Path.Combine(legacy, name);
            try { if (File.Exists(from)) { File.Copy(from, System.IO.Path.Combine(root, name)); copied.Add(name); } } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        foreach (var name in SettingsFolders)
        {
            string from = System.IO.Path.Combine(legacy, name);
            if (!Directory.Exists(from)) continue;
            try { CopyFolder(from, System.IO.Path.Combine(root, name)); copied.Add(name); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return copied;
    }
    static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from)) File.Copy(file, System.IO.Path.Combine(to, System.IO.Path.GetFileName(file)), false);
        foreach (var folder in Directory.EnumerateDirectories(from, "*", new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint }))
            CopyFolder(folder, System.IO.Path.Combine(to, System.IO.Path.GetFileName(folder)));
    }
}
