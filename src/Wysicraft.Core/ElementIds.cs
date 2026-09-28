using System.Text.RegularExpressions;
using Wysicraft.Models;
namespace Wysicraft.Core;

public static class ElementIds
{
    /// <summary>Next free readable ID for <paramref name="basis"/>: its trailing number (and any "_1_1" chain) is dropped,
    /// then the lowest free number is added, so "button" and "button_1_1" both give "button1", then "button2".</summary>
    public static string Next(string basis, IEnumerable<string> taken)
    {
        var used = taken.ToHashSet();
        string stem = Regex.Replace(basis, "(?:_?[0-9]+)+$", "");
        if (!Validation.Id(stem + "1")) stem = "element";
        stem = stem[..Math.Min(56, stem.Length)];
        int n = 1; while (used.Contains(stem + n)) n++;
        return stem + n;
    }

    /// <summary>Renames an element on its screen and updates everything in the project that names it:
    /// children's Parent, action targets on that screen, and component instance maps. IDs written inside
    /// scripts are not changed; the return value lists the scripts that mention the old ID.</summary>
    public static List<string> Rename(Project project, UiDefinition screen, string oldId, string newId)
    {
        if (oldId == newId) return [];
        if (!Validation.Id(newId)) throw new InvalidOperationException("IDs use lowercase letters, digits and _, starting with a letter.");
        if (screen.Elements.Any(e => e.Id == newId)) throw new InvalidOperationException($"\"{newId}\" is already used on this screen.");
        var element = screen.Elements.FirstOrDefault(e => e.Id == oldId) ?? throw new InvalidOperationException("Element not found.");
        if (screen.ComponentInstances.Any(i => i.Root == oldId || i.Ids.Values.Contains(oldId))) throw new InvalidOperationException("Detach the component before renaming its element IDs.");
        element.Id = newId;
        foreach (var child in screen.Elements.Where(c => c.Parent == oldId)) child.Parent = newId;
        IEnumerable<Element> All(IEnumerable<Element> elements) => elements.SelectMany(e => new[] { e }.Concat(All(e.RowElements)));
        var handlers = screen.Events.Values.Concat(All(screen.Elements).SelectMany(e => e.Events.Values)).SelectMany(ev => new[] { ev.Client, ev.Server });
        foreach (var action in handlers.SelectMany(h => h.Actions)) if (action.Target == oldId) action.Target = newId;
        var word = new Regex(@"\b" + Regex.Escape(oldId) + @"\b");
        return project.Scripts.Where(s => word.IsMatch(s.Value)).Select(s => s.Key).OrderBy(s => s).ToList();
    }
}
