using Wysicraft.Models;
namespace Wysicraft.Core;
public static class ContainerTree
{
    public static IEnumerable<Element> Ancestors(UiDefinition screen,Element child) {
        var seen=new HashSet<string>{child.Id}; string id=child.Parent;
        while(id.Length>0) {
            if(!seen.Add(id) || seen.Count>33)throw new InvalidDataException("Container cycle or nesting exceeds 32 levels");
            var parent=screen.Elements.FirstOrDefault(e=>e.Id==id);
            if(parent==null || !IsContainer(parent))throw new InvalidDataException("Parent must identify a panel, scroll panel or camera");
            yield return parent;id=parent.Parent;
        }
    }
    public static IEnumerable<Element> Moving(UiDefinition screen,IEnumerable<string> selected) {
        var ids=selected.ToHashSet();return screen.Elements.Where(e=>ids.Contains(e.Id) || Ancestors(screen,e).Any(p=>ids.Contains(p.Id)));
    }
    // A camera holds controls that stay in view when it moves (score, lives, menus), like a screen-fixed layer.
    public static bool IsContainer(Element e) => e.Type is "panel" or "scroll_panel" or "camera";
    public static bool IsDescendant(UiDefinition screen,Element candidate,string ancestorId) => Ancestors(screen,candidate).Any(p=>p.Id==ancestorId);
    // Panels an element may be placed inside: never itself or anything inside it (that would be a cycle).
    public static IEnumerable<Element> ValidParents(UiDefinition screen,Element child) =>
        screen.Elements.Where(p=>IsContainer(p) && p.Id!=child.Id && !IsDescendant(screen,p,child.Id));

    // Sets or clears (parent "") an element's container. Bounds are absolute, so nothing moves on screen.
    // A new child is drawn directly in front of its panel so the panel background can't hide it.
    public static void SetParent(UiDefinition screen,string id,string parent) {
        var child=screen.Elements.Single(e=>e.Id==id);
        if(parent.Length>0) {
            var panel=screen.Elements.FirstOrDefault(e=>e.Id==parent) ?? throw new InvalidOperationException("No element named "+parent);
            if(!IsContainer(panel))throw new InvalidOperationException(parent+" is not a panel, scroll panel or camera");
            if(panel.Id==child.Id || IsDescendant(screen,panel,child.Id))throw new InvalidOperationException("Can't put "+child.Id+" inside its own child "+parent);
        }
        string previous=child.Parent;child.Parent=parent;
        try {Ancestors(screen,child).ToArray();} catch {child.Parent=previous;throw;}
        if(parent.Length>0 && previous!=parent) {
            screen.Elements.Remove(child);
            screen.Elements.Insert(screen.Elements.FindIndex(e=>e.Id==parent)+1,child);
        }
        KeepChildrenInFront(screen);
    }
    // Attaches the selection to `panelId`. Items whose parent is also being attached keep that parent,
    // so a selected panel keeps its own children. Returns the IDs that extend past the panel (and will be clipped).
    public static List<string> Attach(UiDefinition screen,IEnumerable<string> ids,string panelId) {
        var chosen=ids.Where(id=>id!=panelId).ToHashSet();
        var panel=screen.Elements.Single(e=>e.Id==panelId);
        var roots=screen.Elements.Where(e=>chosen.Contains(e.Id) && !Ancestors(screen,e).Any(p=>chosen.Contains(p.Id))).ToList();
        if(roots.Count==0)throw new InvalidOperationException("Select at least one item besides the panel.");
        foreach(var e in roots) {
            if(IsDescendant(screen,panel,e.Id))throw new InvalidOperationException("Can't put "+e.Id+" inside its own child "+panelId);
            if(e.LayerGroup!=panel.LayerGroup)e.LayerGroup=panel.LayerGroup;
        }
        // Insert in the items' existing relative order, directly in front of the panel.
        foreach(var e in roots.AsEnumerable().Reverse())SetParent(screen,e.Id,panelId);
        var area=panel.Bounds;
        return roots.Where(e=>e.Bounds.X<area.X || e.Bounds.Y<area.Y || e.Bounds.X+e.Bounds.Width>area.X+area.Width || e.Bounds.Y+e.Bounds.Height>area.Y+area.Height).Select(e=>e.Id).ToList();
    }
    // Moves each item out one level: into its parent's parent, or onto the screen.
    public static int Detach(UiDefinition screen,IEnumerable<string> ids) {
        int count=0;
        foreach(var e in screen.Elements.Where(e=>ids.Contains(e.Id) && e.Parent.Length>0).ToList()) {
            e.Parent=screen.Elements.FirstOrDefault(p=>p.Id==e.Parent)?.Parent ?? "";count++;
        }
        return count;
    }
    // Runtime and editor draw in list order, so every child must come after (in front of) its panel.
    public static void KeepChildrenInFront(UiDefinition screen) {
        for(int pass=0,limit=screen.Elements.Count*screen.Elements.Count+1;pass<limit;pass++) {
            bool moved=false;
            for(int i=0;i<screen.Elements.Count;i++) {
                var child=screen.Elements[i];if(child.Parent.Length==0)continue;
                int parent=screen.Elements.FindIndex(e=>e.Id==child.Parent);
                if(parent>i){screen.Elements.RemoveAt(i);screen.Elements.Insert(parent,child);moved=true;break;}
            }
            if(!moved)return;
        }
    }
    public static double Top(UiDefinition screen,Element e,IReadOnlyDictionary<string,double> scroll) => e.Bounds.Y-Ancestors(screen,e).Where(p=>p.Type=="scroll_panel").Sum(p=>scroll.GetValueOrDefault(p.Id));
}
