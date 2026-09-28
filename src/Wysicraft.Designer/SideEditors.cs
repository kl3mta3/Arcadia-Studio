using System.IO;
using System.Windows;
namespace Wysicraft.Designer;

// The pixel editor, music maker and sound effect maker open beside the main window rather than in front of it: each is
// its own window (with its own taskbar button), so the project, the other editors and MCP all keep working while one
// is open. They close (each asking about unsaved work) before the project is replaced or Wysicraft closes.
public partial class MainWindow
{
    readonly List<Window> sideEditors = [];

    /// <summary>Shows an editor as its own window, centred over the main window.</summary>
    void OpenBeside(Window editor)
    {
        editor.Owner = null; editor.ShowInTaskbar = true; editor.WindowStartupLocation = WindowStartupLocation.Manual;
        if (Icon != null) editor.Icon = Icon;
        var area = SystemParameters.WorkArea;
        var main = WindowState == WindowState.Maximized ? area : new Rect(Left, Top, ActualWidth, ActualHeight);
        double w = Math.Min(editor.Width, area.Width), h = Math.Min(editor.Height, area.Height);
        editor.Width = w; editor.Height = h;
        editor.Left = Math.Clamp(main.Left + (main.Width - w) / 2, area.Left, Math.Max(area.Left, area.Right - w));
        editor.Top = Math.Clamp(main.Top + (main.Height - h) / 2, area.Top, Math.Max(area.Top, area.Bottom - h));
        sideEditors.Add(editor); editor.Closed += (_, _) => sideEditors.Remove(editor);
        editor.Show();
    }

    internal int SideEditorCount => sideEditors.Count;

    /// <summary>Closes the open editors; each asks about unsaved work. False if one was kept open.</summary>
    bool CloseSideEditors()
    {
        foreach (var editor in sideEditors.ToList()) { if (editor.WindowState == WindowState.Minimized) editor.WindowState = WindowState.Normal; editor.Activate(); editor.Close(); }
        return sideEditors.Count == 0;
    }

    /// <summary>What an editor last read or wrote of its file, to notice when it's changed some other way meanwhile
    /// (in the main window, another editor or over MCP) before saving over it.</summary>
    sealed class EditorFile(string? path, byte[]? bytes)
    {
        public string? Path = path; public byte[]? Bytes = bytes;
    }
    EditorFile Watch(string? path) => new(path, path != null && project.Assets.TryGetValue(path, out var b) ? b : null);

    /// <summary>Before an editor saves to target: fine if it's new or unchanged since the editor saw it; otherwise ask.
    /// Throws OperationCanceledException (which the editors ignore) when the user keeps the other version.</summary>
    void CheckOverwrite(EditorFile file, string? target, Window editor)
    {
        if (target == null || !project.Assets.TryGetValue(target, out var now)) return;
        if (file.Path == target && file.Bytes != null && now.AsSpan().SequenceEqual(file.Bytes)) return;
        if (file.Path != target) return;
        if (!MusicMaker.NoPrompts && MessageBox.Show(editor, $"{Path.GetFileName(target)} was changed outside this editor since you opened it. Save over those changes?", editor.Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            throw new OperationCanceledException("Not saved: the version changed outside this editor was kept.");
    }
    void Saw(EditorFile file, string path) { file.Path = path; file.Bytes = project.Assets.TryGetValue(path, out var b) ? b : null; }
}
