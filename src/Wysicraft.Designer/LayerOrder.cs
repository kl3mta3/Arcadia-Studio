using Wysicraft.Core;
using Wysicraft.Models;
namespace Wysicraft.Designer;

// Bring to front / Send to back, and renaming an element's ID from Layers, the canvas menu or F2.
public partial class MainWindow
{
    void BringToFront() => MoveLayersToEnd(front: true);
    void SendToBack() => MoveLayersToEnd(front: false);

    // Moves the selection (with its attached children) in front of or behind everything else in the same
    // layer group, or the whole screen when the selection is ungrouped. Group membership never changes.
    void MoveLayersToEnd(bool front)
    {
        if (selected.Count == 0) return;
        var moving = ContainerTree.Moving(ui, selected).Select(e => e.Id).ToHashSet();
        var order = ui.Elements.AsEnumerable().Reverse().ToList(); // front-most first
        var moved = order.Where(e => moving.Contains(e.Id)).ToList();
        if (moved.Count == 0) return;
        var groups = moved.Select(e => e.LayerGroup).Distinct().ToList();
        string scope = groups.Count == 1 ? groups[0] : isolatedGroup;
        var rest = order.Where(e => !moving.Contains(e.Id)).ToList();
        int first = rest.FindIndex(e => LayerGroups.Contains(ui, scope, e.LayerGroup)), last = rest.FindLastIndex(e => LayerGroups.Contains(ui, scope, e.LayerGroup));
        int index = scope.Length == 0 ? (front ? 0 : rest.Count) : first < 0 ? 0 : front ? first : last + 1;
        rest.InsertRange(Math.Clamp(index, 0, rest.Count), moved);
        if (rest.Select(e => e.Id).SequenceEqual(order.Select(e => e.Id))) return;
        Change(); SetLayerOrder(rest); ContainerTree.KeepChildrenInFront(ui);
        Draw(); RefreshInspector();
    }

    void RenameSelected()
    {
        var element = ui.Elements.FirstOrDefault(e => selected.Contains(e.Id));
        if (element == null || selected.Count != 1) throw new InvalidOperationException("Select one element to rename.");
        RenameElement(element);
    }

    void RenameElement(Element element)
    {
        string? id = Prompt("Rename", "Element ID (lowercase letters, digits and _)", element.Id);
        if (id == null || id == element.Id) return;
        id = id.Trim();
        if (!Validation.Id(id)) throw new InvalidOperationException("IDs use lowercase letters, digits and _, starting with a letter.");
        if (ui.Elements.Any(e => e.Id == id)) throw new InvalidOperationException($"\"{id}\" is already used on this screen.");
        Change(); string oldId = element.Id;
        ReportScriptMentions(oldId, ElementIds.Rename(project, ui, oldId, id));
        selected.Remove(oldId); selected.Add(id);
        Draw(); RefreshInspector();
    }

    static bool InsideButton(System.Windows.DependencyObject? node)
    {
        for (; node != null; node = node is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(node) : System.Windows.LogicalTreeHelper.GetParent(node))
            if (node is System.Windows.Controls.Primitives.ButtonBase) return true;
        return false;
    }

    void ReportScriptMentions(string oldId, List<string> scripts)
    {
        if (scripts.Count > 0) Log($"Renamed {oldId}. Actions and attachments were updated, but these scripts still mention \"{oldId}\": {string.Join(", ", scripts)}");
    }
}
