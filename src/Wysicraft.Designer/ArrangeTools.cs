using System.Windows.Controls;
using Wysicraft.Core;
using Wysicraft.Models;

namespace Wysicraft.Designer;
public partial class MainWindow
{
    bool keepArrangeGroups=true;
    static readonly (string Label,ArrangeOperation Operation)[] ArrangeChoices = [
        ("Align left edges",ArrangeOperation.Left),("Align horizontal centers",ArrangeOperation.HorizontalCenter),("Align right edges",ArrangeOperation.Right),
        ("Align top edges",ArrangeOperation.Top),("Align vertical centers",ArrangeOperation.VerticalCenter),("Align bottom edges",ArrangeOperation.Bottom),
        ("Equal horizontal gaps",ArrangeOperation.HorizontalGaps),("Equal vertical gaps",ArrangeOperation.VerticalGaps),
        ("Center selection horizontally on screen",ArrangeOperation.CanvasHorizontalCenter),("Center selection vertically on screen",ArrangeOperation.CanvasVerticalCenter)
    ];
    MenuItem ArrangeMenu(System.Action? selectTarget=null) {
        var menu=new MenuItem {Header="Align"};
        foreach(var (label,operation) in ArrangeChoices) {
            if(operation is ArrangeOperation.HorizontalGaps or ArrangeOperation.CanvasHorizontalCenter)menu.Items.Add(new Separator());
            var entry=new MenuItem {Header=label,Tag=operation};
            entry.Click+=(_,_)=>Guard(()=>{selectTarget?.Invoke();ArrangeSelection(operation);});menu.Items.Add(entry);
        }
        menu.Items.Add(new Separator());
        var groups=new MenuItem {Header="Keep layer groups together",IsCheckable=true};groups.Click+=(_,_)=>keepArrangeGroups=groups.IsChecked;menu.Items.Add(groups);
        menu.SubmenuOpened+=(_,_)=>{
            selectTarget?.Invoke();groups.IsChecked=keepArrangeGroups;
            foreach(var entry in menu.Items.OfType<MenuItem>()) {
                if(entry.Tag is not ArrangeOperation operation)continue;ToolTipService.SetShowOnDisabled(entry,true);
                try {var plan=Arrangement.Plan(ui,selected,operation,keepArrangeGroups);entry.IsEnabled=plan.Positions.Count>0;entry.ToolTip="Align to selection bounds; full groups and containers move together. Exact spacing ignores grid snapping.";}
                catch(InvalidOperationException ex){entry.IsEnabled=false;entry.ToolTip=ex.Message;}
            }
        };
        return menu;
    }
    void ArrangeSelection(ArrangeOperation operation) {
        var plan=Arrangement.Plan(ui,selected,operation,keepArrangeGroups);
        if(plan.Positions.Count==0)return;
        Change();Arrangement.Apply(ui,plan);Draw();RefreshInspector();
        Log(ArrangeChoices.First(c=>c.Operation==operation).Label+" — "+plan.Units+" objects");
    }
    internal void VerifyArrangement(string output) {
        project=new Project();ui=project.Screens[0];history.Clear();selected.Clear();
        ui.Elements=[new(){Id="a",Bounds=new(){X=10,Y=10,Width=20,Height=20}},new(){Id="b",Bounds=new(){X=70,Y=25,Width=30,Height=20}},new(){Id="c",Bounds=new(){X=150,Y=50,Width=40,Height=20}}];
        selected.UnionWith(new[]{"a","b","c"});
        ArrangeSelection(ArrangeOperation.Top);if(ui.Elements.Any(e=>e.Bounds.Y!=10))throw new Exception("Alignment failed");
        history.Undo();if(ui.Elements[1].Bounds.Y!=25 || ui.Elements[2].Bounds.Y!=50)throw new Exception("Alignment undo failed");
        history.Redo();if(ui.Elements.Any(e=>e.Bounds.Y!=10))throw new Exception("Alignment redo failed");
        ArrangeSelection(ArrangeOperation.HorizontalGaps);if(ui.Elements[1].Bounds.X!=75)throw new Exception("Equal gap spacing failed");
        history.Undo();if(ui.Elements[1].Bounds.X!=70)throw new Exception("Distribution undo failed");
        dirty=false;System.IO.File.WriteAllText(output,"PASS: arrange/distribute editor integration, single-step undo and redo");
    }
}

