using Wysicraft.Models;
namespace Wysicraft.Core;

public static class ComponentStarters
{
    public record Starter(string Id,string Title,string Description);
    public static readonly Starter[] All=[
        new("window_header","Window header","Title, subtitle and working screen-close button."),
        new("tab_bar","Tab bar","Three tabs with a local active indicator. Add your screen/content actions."),
        new("item_card","Item card","Item icon, name, quantity and action button. Assign a real server action before granting items."),
        new("inventory_toolbar","Inventory toolbar","Category buttons, search field and refresh button. Connect these to your item data."),
        new("confirmation_dialog","Confirmation dialog","Message, confirm and cancel. Demo confirmation updates a label; cancel hides this dialog."),
        new("player_status","Player status","Player name, health and energy bars with sample values. Connect your player data."),
        new("pagination_bar","Pagination bar","Previous/next controls and page label. Connect paging events to your list."),
        new("notification_banner","Notification banner","Notice icon, message and working dismiss button."),
        new("touch_controls","Touch controls","On-screen buttons for a phone: a menu and a map button, plus two actions. They appear only on a touch device, and each presses a named input. Steering needs no buttons — give your direction inputs a touch action and a drag anywhere steers.")
    ];
    public static UiDefinition Add(Project project,string key) {
        var starter=All.SingleOrDefault(s=>s.Id==key)??throw new InvalidOperationException("Unknown starter component");
        var source=Build(key);string basis="component_"+key;source.Id=basis;int n=2;while(project.Screens.Any(s=>s.Id==source.Id))source.Id=basis+"_"+n++;
        source.Title=starter.Title;source.IsComponent=true;project.Screens.Add(source);
        // Controls that press an input need that input to exist, or the project no longer validates the moment the
        // starter is placed. Anything the project already has is left alone.
        foreach(var (name,keys,buttons) in StarterInputs(key))
            if(!project.Manifest.Inputs.Any(i=>i.Name==name))
                project.Manifest.Inputs.Add(new GameInput{Name=name,Keys=[..keys],Buttons=[..buttons]});
        return source;
    }
    /// <summary>The inputs a starter's controls press, so placing one leaves a project that still works.</summary>
    static (string Name,string[] Keys,string[] Buttons)[] StarterInputs(string key)=>key switch {
        "touch_controls"=>[("pause",["escape"],["start"]),("map",["m"],["back"]),("confirm",["enter","space"],["a"]),("action",["e"],["b"])],
        _=>[]
    };
    static UiDefinition Build(string key) {
        var ui=new UiDefinition();
        Element Add(string id,string type,int x,int y,int w,int h,string text="") {
            var e=new Element {Id=id,Type=type,Text=text,Name=text,Parent=id=="body"?"":"body",Bounds=new(){X=x,Y=y,Width=w,Height=h},Background="#22333E",Foreground="#EAF2F4",BorderColor="#425D69",CornerRadius=3,FontScale=1,FillEnabled=type is "panel" or "button" or "textbox" or "progress",BorderWidth=type is "panel" or "textbox"?1:0};
            if(type=="button")e.Alignment="center";ui.Elements.Add(e);return e;
        }
        void Body(int w,int h){ui.Size=new(){Width=w,Height=h};var e=Add("body","panel",0,0,w,h);e.HorizontalAnchor="stretch";e.VerticalAnchor="stretch";e.Background="#16242D";}
        Element Label(string id,string text,int x,int y,int w,int h=16)=>Add(id,"label",x,y,w,h,text);
        Element Button(string id,string text,int x,int y,int w,int h=24,bool accent=false){var e=Add(id,"button",x,y,w,h,text);if(accent){e.Background="#A8D77E";e.Foreground="#172A20";}return e;}
        void Click(Element e,params VisualAction[] actions)=>e.Events["click"]=new(){Client=new(){Actions=actions.ToList()}};
        VisualAction Text(string target,string value)=>new(){Type="set_text",Target=target,Value=value};
        VisualAction Visible(string target,bool value)=>new(){Type="set_visible",Target=target,Value=value?"true":"false"};
        switch(key) {
            case "window_header":
                Body(288,52);Label("title","YOUR WINDOW",12,9,220).Bold=true;Label("subtitle","A short description",12,29,220).Foreground="#9DB2BD";
                var close=Button("close","X",252,12,24);close.HorizontalAnchor="right";close.Tooltip="Closes the current screen.";Click(close,new VisualAction {Type="close_ui"});break;
            case "tab_bar":
                Body(288,54);
                for(int i=0;i<3;i++) {string id="tab_"+i;var tab=Button(id,new[]{"Overview","Items","Settings"}[i],8+i*92,8,88);var marker=Add("active_"+i,"panel",8+i*92,35,88,3);marker.Background="#A8D77E";marker.BorderWidth=0;marker.Visible=i==0;
                    Click(tab,Visible("active_0",i==0),Visible("active_1",i==1),Visible("active_2",i==2),Text("selection",tab.Text));tab.Tooltip="Add open_ui or content visibility actions to this click event.";
                }
                Label("selection","Overview",10,40,260,12).FontScale=.75;break;
            case "item_card":
                Body(240,88);var icon=Add("icon","item",12,12,32,32);icon.Item="minecraft:diamond";
                Label("name","Diamond",56,12,172).Bold=true;Label("quantity","Quantity: 1",56,32,170).Foreground="#9DB2BD";
                var add=Button("action","Add item",128,56,100,24,true);add.HorizontalAnchor="right";add.Tooltip="Placeholder: assign a trusted Server event to grant the chosen item.";Click(add,Text("status","Item requested"));Label("status","Ready",12,61,108).FontScale=.8;break;
            case "inventory_toolbar":
                Body(304,88);
                for(int i=0;i<3;i++){var tab=Button("category_"+i,new[]{"All","Gear","Food"}[i],8+i*64,8,60);Click(tab,Text("status",tab.Text+" selected"));tab.Tooltip="Connect this category to your inventory filtering.";}
                var refresh=Button("refresh","Refresh",208,8,88,24,true);refresh.HorizontalAnchor="right";Click(refresh,Text("status","Refresh requested"));refresh.Tooltip="Connect to your inventory refresh handler.";
                var search=Add("search","textbox",8,40,288,24);search.Value="";search.HorizontalAnchor="stretch";search.Tooltip="Search items: connect text_changed or submit to your filtering script.";
                Label("status","All items",10,69,284,12).FontScale=.8;break;
            case "confirmation_dialog":
                Body(272,128);Label("title","Are you sure?",16,14,240).Bold=true;Label("message","Describe the action here.",16,40,240);Label("status","Choose an option below.",16,62,240).Foreground="#9DB2BD";
                var cancel=Button("cancel","Cancel",16,92,112);cancel.VerticalAnchor="bottom";Click(cancel,Visible("body",false));var confirm=Button("confirm","Confirm",144,92,112,24,true);confirm.HorizontalAnchor="right";confirm.VerticalAnchor="bottom";confirm.Tooltip="Replace this demo response with your confirmed action.";Click(confirm,Text("status","Confirmed (demo)"));break;
            case "player_status":
                Body(240,104);Label("name","PLAYER NAME",12,10,216).Bold=true;Label("health_label","Health  20 / 20",12,34,216).FontScale=.8;
                var health=Add("health","progress",12,50,216,10);health.Value="100";health.Foreground="#A8D77E";health.HorizontalAnchor="stretch";health.Tooltip="Sample health. Update Value from player data.";
                Label("energy_label","Energy  75 / 100",12,68,216).FontScale=.8;var energy=Add("energy","progress",12,84,216,10);energy.Value="75";energy.Foreground="#73C7E5";energy.HorizontalAnchor="stretch";break;
            case "pagination_bar":
                Body(288,58);var previous=Button("previous","< Previous",8,8,88);var next=Button("next","Next >",192,8,88);next.HorizontalAnchor="right";Label("page","Page 1 of 3",104,13,80).Alignment="center";
                Label("status","",10,39,268,12).FontScale=.75;Click(previous,Text("status","Previous requested"));Click(next,Text("status","Next requested"));previous.Tooltip=next.Tooltip="Placeholder: connect to your list paging handler and update the page label.";break;
            case "touch_controls": {
                // Only the buttons are here. Steering is a drag anywhere the controls are not, which needs no control
                // at all — set a touch action on the direction inputs instead.
                Body(240,56);
                var pad=ui.Elements[0];pad.Background="#00000000";pad.BorderWidth=0;pad.TouchOnly=true;
                string[,] keys={{"t_menu","MENU","pause"},{"t_map","MAP","map"},{"t_a","A","confirm"},{"t_b","B","action"}};
                for(int i=0;i<4;i++){
                    var b=Button(keys[i,0],keys[i,1],8+i*58,10,52,36);
                    b.Input=keys[i,2];b.TouchOnly=true;b.CornerRadius=6;b.Background="#1E2A33C8";b.BorderColor="#6E8A9A";b.BorderWidth=1;
                    b.Tooltip="Presses the \""+keys[i,2]+"\" input. Rename it in Advanced → Inputs, or clear Presses input to use a click event instead.";
                }
                break; }
            case "notification_banner":
                Body(304,56);var notice=Add("icon","item",10,13,24,24);notice.Item="minecraft:paper";Label("title","NOTICE",44,8,212).Bold=true;Label("message","Your message goes here.",44,29,212).FontScale=.85;
                var dismiss=Button("dismiss","X",272,15,24);dismiss.HorizontalAnchor="right";Click(dismiss,Visible("body",false));break;
            default:throw new InvalidOperationException("Unknown starter component");
        }
        return ui;
    }
}
