using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using Wysicraft.Core;
using Wysicraft.Models;
using Wysicraft.Packaging;

namespace Wysicraft.Designer;

public partial class MainWindow
{
    internal async Task VerifyMcpAsync(string output,bool connectionOnly=false)
    {
        using var client=new HttpClient { Timeout=TimeSpan.FromSeconds(20) };
        int requestId=0;
        async Task<JsonNode> Rpc(string method,object args)
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,mcpUrl);
            request.Headers.Add("Authorization","Bearer "+mcpToken);
            request.Headers.Add("Accept","application/json, text/event-stream");
            request.Headers.Add("MCP-Protocol-Version","2025-11-25");
            if (mcpSessionId != null) request.Headers.TryAddWithoutValidation("Mcp-Session-Id", mcpSessionId);
            request.Content=new StringContent(Json.Write(new { jsonrpc="2.0",id=++requestId,method,@params=args }),Encoding.UTF8,"application/json");
            using var response=await client.SendAsync(request);
            if (response.Headers.TryGetValues("Mcp-Session-Id", out var given)) mcpSessionId = given.FirstOrDefault();
            response.EnsureSuccessStatusCode();
            var body=await response.Content.ReadAsStringAsync();
            if(body.StartsWith("event:") || body.StartsWith("data:")) body=body.Split('\n').First(l=>l.StartsWith("data: "))[6..];
            var result=JsonNode.Parse(body)!;
            if(result["error"]!=null) throw new InvalidOperationException(result.ToJsonString());
            return result["result"]!;
        }
async Task<string> CallText(string name,object args) { var r=await Rpc("tools/call",new { name,arguments=args }); if(r["isError"]?.GetValue<bool>() ?? false) throw new InvalidOperationException(r.ToJsonString()); return r["content"]![0]!["text"]!.GetValue<string>(); }
        async Task<JsonNode> Call(string name,object args,bool fail=false)
        {
            var result=await Rpc("tools/call",new { name,arguments=args });
            if((result["isError"]?.GetValue<bool>() ?? false)!=fail) throw new InvalidOperationException(result.ToJsonString());
            return fail ? result : JsonNode.Parse(result["content"]![0]!["text"]!.GetValue<string>())!;
        }
        try
        {
            // Old exports are cleared: a check's package after a day, an export after two weeks; anything newer stays.
            string exports=Path.Combine(Path.GetTempPath(),"mcp-exports-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(exports);
            try
            {
                void Make(string name,bool folder,double daysOld)
                {
                    string full=Path.Combine(exports,name);
                    if(folder) { Directory.CreateDirectory(full); File.WriteAllBytes(Path.Combine(full,"game.zip"),new byte[1000]); Directory.SetLastWriteTimeUtc(full,DateTime.UtcNow.AddDays(-daysOld)); }
                    else { File.WriteAllBytes(full,new byte[1000]); File.SetLastWriteTimeUtc(full,DateTime.UtcNow.AddDays(-daysOld)); }
                }
                string oldCheck=Guid.NewGuid().ToString("N"),newCheck=Guid.NewGuid().ToString("N");
                Make(oldCheck,true,2); Make(newCheck,true,0.5); Make("game-old.html",false,15); Make("game-new.html",false,13); Make("game-electron-0a1b2c3d",true,20); Make("game-electron-4e5f6a7b",true,3);
                var pruned=PruneMcpExports(exports,DateTime.UtcNow);
                var left=Directory.EnumerateFileSystemEntries(exports).Select(Path.GetFileName).OrderBy(n=>n,StringComparer.Ordinal).ToList();
                if(pruned.Removed!=3 || pruned.Bytes!=3000 || !left.SequenceEqual(new[] { newCheck,"game-electron-4e5f6a7b","game-new.html" }.OrderBy(n=>n,StringComparer.Ordinal)))
                    throw new Exception("Old MCP exports were not cleared as expected: removed "+pruned.Removed+", left "+string.Join(", ",left));
                if(PruneMcpExports(Path.Combine(exports,"missing"),DateTime.UtcNow).Removed!=0) throw new Exception("A missing exports folder was not handled.");
            }
            finally { try { Directory.Delete(exports,true); } catch { } }
            await StartMcp();
            using(var denied=await client.GetAsync(mcpUrl))
                if(denied.StatusCode!=HttpStatusCode.Unauthorized) throw new Exception("Unauthenticated access was not rejected.");
            using(var hostile=new HttpRequestMessage(HttpMethod.Get,mcpUrl))
            {
                hostile.Headers.Add("Authorization","Bearer "+mcpToken); hostile.Headers.Add("Origin","https://example.com");
                using var denied=await client.SendAsync(hostile);
                if(denied.StatusCode!=HttpStatusCode.Forbidden) throw new Exception("Cross-origin access was not rejected.");
            }
            var handshake=await Rpc("initialize",new { protocolVersion="2025-11-25",capabilities=new {},clientInfo=new { name="Arcadia Studio smoke",version="1" } });
            // An assistant meeting this server is told how to use it before it calls anything.
            string greeting=handshake["instructions"]?.GetValue<string>() ?? "";
            if(!greeting.Contains("get_project") || !greeting.Contains("guide")) throw new Exception("Server instructions missing.");
            // The server says it's Arcadia Studio, and so does the config the app hands out (it was "wysicraft").
            if(handshake["serverInfo"]?["name"]?.GetValue<string>()!="arcadia-studio" || handshake["serverInfo"]?["title"]?.GetValue<string>()!="Arcadia Studio" || greeting.Contains("named wysicraft")) throw new Exception("The MCP server doesn't announce itself as Arcadia Studio: "+handshake["serverInfo"]);
            var handedOut=JsonNode.Parse(McpConfigJson())!["mcpServers"]!.AsObject();
            if(handedOut.Count!=1 || !handedOut.ContainsKey("arcadia-studio")) throw new Exception("The MCP config isn't keyed arcadia-studio: "+string.Join(",",handedOut.Select(p=>p.Key)));
            var listing=await Rpc("tools/list",new {});
            if(listing["tools"]!.AsArray().Count!=34) throw new Exception("Tools missing: "+listing["tools"]!.AsArray().Count);
            if(connectionOnly) { if(!mcpClients.ContainsKey("Arcadia Studio smoke") || mcpRequests<2) throw new Exception("Client visibility missing"); File.WriteAllText(output,"PASS: authentication, origin checks, MCP discovery and client visibility."); return; }
            // The tutorial tools through MCP: what can be pointed at, a highlight, clearing it.
            var targets=await Call("tutorial",new{action="targets"});
            if(!targets.ToJsonString().Contains("menu:file.publish")||!targets.ToJsonString().Contains("panel:layers"))throw new Exception("tutorial targets is missing menu commands or panels.");
            var pointed=await Call("tutorial",new{action="highlight",target="panel:layers",caption="Your layers",seconds=5});
            if(!pointed["highlighted"]!.GetValue<string>().Contains("Layers")||(IsVisible&&!HighlightShowing))throw new Exception("tutorial highlight didn't point at Layers.");
            await Call("tutorial",new{action="clear"});if(HighlightShowing)throw new Exception("tutorial clear left the highlight.");
            await Call("tutorial",new{action="highlight",target="panel:not_a_panel"},fail:true);
            string someControl=project.Screens.SelectMany(s=>s.Elements).First().Id;
            var picked=await Call("tutorial",new{action="select",target="element:"+someControl});
            if(picked["selected"]!.GetValue<string>()!=someControl||!selected.Contains(someControl))throw new Exception("tutorial select didn't select "+someControl);
            await Call("tutorial",new{action="select",target="element:no_such_control"},fail:true);
            // Every window, dialog and panel the app has can be opened and closed again through MCP.
            var canOpen=await Call("tutorial",new{action="windows"});
            var openable=canOpen["canOpen"]!.AsArray().Select(c=>c!["target"]!.GetValue<string>()).ToList();
            if(openable.Count<30||canOpen["panels"]!.AsArray().Count<8)throw new Exception("tutorial windows lists too little: "+openable.Count+" windows, "+canOpen["panels"]!.AsArray().Count+" panels");
            var windowProblems=new List<string>();
            // Left out here only because of what they do when opened in a test: the manual opens in the web browser, updates and
            // the publish windows go online, Minecraft test and Recover depend on this computer, and three need a selection.
            var skipped=new[]{"help.manual","help.updates","file.publish","file.publishItch","project.test","file.recover","project.spriteSheet","advanced.collider","advanced.tilemap","advanced.leaderboard.open"};
            foreach(var opening in openable.Where(t=>!skipped.Contains(t["window:".Length..])))
            {
                try
                {
                    var shown=await Call("tutorial",new{action="open_window",target=opening});
                    // Screen settings shows in the Properties panel rather than a window of its own.
                    if(opening=="window:project.screen")continue;
                    if(shown["open"]!.AsArray().Count==0)throw new Exception("nothing opened");
                    await Call("tutorial",new{action="close_window",target="all"});
                    for(int i=0;i<40&&(await Call("tutorial",new{action="windows"}))["open"]!.AsArray().Count>0;i++)await Task.Delay(100);
                    var left=(await Call("tutorial",new{action="windows"}))["open"]!.AsArray();
                    if(left.Count>0)throw new Exception("still open after close_window all: "+left.ToJsonString());
                }
                catch(Exception ex){windowProblems.Add(opening+": "+ex.Message);}
            }
            if(windowProblems.Count>0)throw new Exception("open_window / close_window: "+string.Join(" | ",windowProblems));
            var byName=await Call("tutorial",new{action="open_window",target="Music maker"});
            if(!byName["opened"]!.GetValue<string>().Contains("Music maker"))throw new Exception("open_window by name: "+byName);
            await Call("tutorial",new{action="close_window",target="Music"});
            var shownPanel=await Call("tutorial",new{action="open_window",target="panel:layers"});
            if(!shownPanel["opened"]!.GetValue<string>().Contains("Layers"))throw new Exception("open_window panel: "+shownPanel);
            await Call("tutorial",new{action="open_window",target="window:advanced.tilemap"},fail:true);
            await Call("tutorial",new{action="open_window",target="window:not_a_window"},fail:true);
            await Call("tutorial",new{action="close_window",target="no such window title"},fail:true);
            // Speech and video through MCP: the voices, a voiced line added to the project, reading it back, recording status.
            var speech=await Call("speech",new{action="status"});
            if(speech["textToSpeech"]!.GetValue<bool>())
            {
                var voices=await Call("speech",new{action="voices"});if(voices["voices"]!.AsArray().Count<50)throw new Exception("speech voices lists too few voices.");
                await Call("speech",new{action="say",text="hi",voice="zz_nobody"},fail:true);
                string speechBefore=(await Call("get_project",new{}))["revision"]!.GetValue<string>();
                var line=await Call("speech",new{action="add_sound",expectedRevision=speechBefore,lines="[{\"name\":\"guard_hello\",\"text\":\"Halt! Who goes there?\",\"voice\":\"am_michael*0.7+bm_george*0.3\"}]"});
                string id=line["sounds"]![0]!["sound"]!.GetValue<string>();if(!id.EndsWith(":guard_hello"))throw new Exception("speech add_sound didn’t add guard_hello: "+id);
                var heard=await Call("speech",new{action="transcribe",sound=id});
                if(!heard["text"]!.GetValue<string>().Contains("who goes there",StringComparison.OrdinalIgnoreCase))throw new Exception("speech transcribe heard: "+heard["text"]);
                await Call("undo",new{expectedRevision=line["revision"]!.GetValue<string>()});
            }
            var video=await Call("video",new{action="status"});if(video["monitors"]!.AsArray().Count<1)throw new Exception("video status lists no monitors.");
            // A particle effect through MCP, burst from a script with no Particles control on the screen.
            string burstScreen=(await Call("get_project",new{}))["activeScreen"]!.GetValue<string>();
            var made=await Call("particles",new{expectedRevision=(await Call("get_project",new{}))["revision"]!.GetValue<string>(),effect="mcp_sparkle",preset="sparkle",settings=new{count=20}});
            if(made["effect"]!.GetValue<string>()!="mcp_sparkle"||made["settings"]!["count"]!.GetValue<int>()!=20)throw new Exception("particles didn't make the effect: "+made);
            await Call("particles",new{expectedRevision=made["revision"]!.GetValue<string>(),effect="Bad Name"},fail:true);
            var spot=await Call("apply_edits",new{expectedRevision=made["revision"]!.GetValue<string>(),edits=new object[]{new{kind="upsert_element",screen=burstScreen,element="mcp_spot",data=new{type="button",text="Spot"}}}});
            string spotRevision=spot["revision"]!.GetValue<string>();
            await Call("preview_control",new{expectedRevision=spotRevision,action="open",screen=burstScreen});
            await Call("preview_control",new{expectedRevision=spotRevision,action="profile",value="on"});
            await Call("preview_control",new{expectedRevision=spotRevision,action="script",value="ui.burst('mcp_sparkle', 'mcp_spot')"});
            var burst=await Call("preview_control",new{expectedRevision=spotRevision,action="wait",value="300"});
            if((burst["profile"]?["particles"]?.GetValue<int>()??0)<=0)throw new Exception("ui.burst made no particles: "+burst["profile"]);
            await Call("preview_control",new{expectedRevision=spotRevision,action="close"});
            await Call("undo",new{expectedRevision=spotRevision});
            // Pixel art drawn live in the pixel editor: the same picture as without live, a new frame begun as a duplicate.
            string liveRevision=(await Call("undo",new{expectedRevision=(await Call("get_project",new{}))["revision"]!.GetValue<string>()}))["revision"]!.GetValue<string>();
            var live=await Call("pixel_art",new{expectedRevision=liveRevision,newName="mcp_live",width=4,height=4,live=true,delayMs=10,commands=new object[]{
                new{op="grid",x=0,y=0,rows=new[]{"ab..","ba..","....","..aa"},palette=new{a="#FF0000",b="#0000FF"}},
                new{op="add_frame",copyOf=0},
                new{op="pixels",frame=1,points=new[]{new[]{3,0}},color="#00FF00"}}});
            if(live["drawnLive"]?["pixels"]?.GetValue<int>()!=7||live["sheet"]!["width"]!.GetValue<int>()!=8||SideEditorCount!=0)throw new Exception("pixel_art live didn't draw each pixel once and save: "+live["drawnLive"]+" sheet "+live["sheet"]+" editors open "+SideEditorCount);
            await Call("undo",new{expectedRevision=live["revision"]!.GetValue<string>()});
            // Kept open while it's talked about: its parts can be pointed at, then close_editors closes it.
            string keptRevision=(await Call("get_project",new{}))["revision"]!.GetValue<string>();
            var kept=await Call("pixel_art",new{expectedRevision=keptRevision,newName="mcp_kept",width=2,height=2,live=true,delayMs=10,keepOpen=true,commands=new object[]{new{op="grid",x=0,y=0,rows=new[]{"ab"},palette=new{a="#FF0000",b="#0000FF"}},new{op="add_frame",copyOf=0}}});
            if(SideEditorCount!=1)throw new Exception("pixel_art keepOpen closed the editor");
            foreach(var part in new[]{"pixel:duplicate","pixel:preview","pixel:frames","pixel:tools"})
            {
                var shown=await Call("tutorial",new{action="highlight",target=part,caption="here",seconds=3});
                if(!shown["highlighted"]!.GetValue<string>().Contains("pixel editor"))throw new Exception(part+" didn't point into the pixel editor: "+shown);
            }
            var screensList=await Call("tutorial",new{action="highlight",target="screens",caption="Screens",seconds=3});
            if(!screensList["highlighted"]!.GetValue<string>().Contains("screens list"))throw new Exception("screens didn't point at the screens list: "+screensList);
            await Call("tutorial",new{action="clear"});
            var closedEditors=await Call("tutorial",new{action="close_editors"});
            if(SideEditorCount!=0||!closedEditors.ToJsonString().Contains("pixel editor"))throw new Exception("close_editors didn't close the pixel editor: "+closedEditors);
            await Call("undo",new{expectedRevision=kept["revision"]!.GetValue<string>()});
            if(await Call("tutorial",new{action="status"}) is var none && none["open"]!.GetValue<bool>())throw new Exception("tutorial status says one is open.");
            string index=await CallText("guide",new {});
            if(!index.Contains("apply_edits") || !McpGuideTopicNames.All(index.Contains)) throw new Exception("Guide index missing topics.");
            foreach(string topic in McpGuideTopicNames) { string page=await CallText("guide",new { topic }); if(page.Length<400) throw new Exception("Guide topic "+topic+" is empty."); }
            // get_project in part: one screen in full and the rest in brief, scripts by name or not at all.
            var whole=await Call("get_project",new {});
            string firstScreen=project.Screens[0].Id;
            var onlyScreen=await Call("get_project",new { screen=firstScreen,scripts="none" });
            var partScreens=onlyScreen["project"]!["screens"]!.AsArray();
            if(partScreens.Count!=1 || partScreens[0]!["id"]!.GetValue<string>()!=firstScreen || partScreens[0]!["elements"]!.AsArray().Count!=project.Screens[0].Elements.Count) throw new Exception("get_project screen did not return that screen in full.");
            if(onlyScreen["project"]!["scripts"]!.AsObject().Count!=0 || onlyScreen["partial"]!["otherScripts"]!.AsArray().Count!=project.Scripts.Count || onlyScreen["partial"]!["otherScreens"]!.AsArray().Count!=project.Screens.Count-1) throw new Exception("get_project did not list what it left out: "+onlyScreen["partial"]);
            if(onlyScreen["revision"]!.GetValue<string>()!=whole["revision"]!.GetValue<string>() || whole["partial"]!=null || whole["project"]!["scripts"]!.AsObject().Count!=project.Scripts.Count) throw new Exception("get_project's revision or its whole-project answer changed.");
            if(project.Scripts.Count>0)
            {
                string onePath=project.Scripts.Keys.First();
                var oneScript=await Call("get_project",new { scripts=onePath });
                if(oneScript["project"]!["scripts"]!.AsObject().Count!=1 || oneScript["project"]!["scripts"]![onePath]!.GetValue<string>()!=project.Scripts[onePath] || oneScript["project"]!["screens"]!.AsArray().Count!=project.Screens.Count) throw new Exception("get_project scripts=<path> did not return just that script.");
            }
            if(!(await Call("get_project",new { screen="no_such_screen" },true)).ToJsonString().Contains("There is no screen")) throw new Exception("get_project accepted a screen that isn't there.");
            File.WriteAllText(output+".sizes.txt",$"get_project {whole.ToJsonString().Length:N0} chars whole, {onlyScreen.ToJsonString().Length:N0} for one screen without scripts");
            var before=await Call("get_project",new {});
            string revision=before["revision"]!.GetValue<string>();
            string screen=ui.Id;
            var applied=await Call("apply_edits",new { expectedRevision=revision,edits=new object[] {
                new { kind="upsert_element",screen,element="mcp_smoke",data=new { type="button",text="MCP test" } },
                new { kind="put_script",key="scripts/client/mcp_smoke.js",source="function click(ctx) { console.log('MCP'); }" },
                new { kind="set_event",screen,element="mcp_smoke",key="click",data=new { client=new { script="scripts/client/mcp_smoke.js",function="click" } } }
            } });
            if(!ui.Elements.Any(e=>e.Id=="mcp_smoke" && e.Events["click"].Client.Function=="click")) throw new Exception("Live editor did not update.");
            string changed=applied["revision"]!.GetValue<string>();
            var stale=await Call("apply_edits",new { expectedRevision=revision,edits=new[]{new { kind="delete_element",screen,element="mcp_smoke" }} },fail:true);
            if(!stale.ToJsonString().Contains("Project changed")) throw new Exception("Revision recovery instructions are missing.");
            await Call("apply_edits",new { expectedRevision=changed,edits=new object[] {
                new { kind="upsert_element",screen,element="mcp_smoke",data=new { text="Should roll back" } },
                new { kind="upsert_element",screen,element="mcp_smoke",data=new { nonexistentProperty=true } }
            } },fail:true);
            if(Revision()!=changed || ui.Elements.Single(e=>e.Id=="mcp_smoke").Text!="MCP test") throw new Exception("Invalid batch was not atomic.");
            var undone=await Call("undo",new { expectedRevision=changed });
            if(undone["revision"]!.GetValue<string>()!=revision) throw new Exception("Undo did not restore project.");
            await Call("apply_edits",new{expectedRevision=revision,edits=new[]{new{kind="put_script",key="scripts/client/broken.js",source="function broken( {"}}},fail:true);
            if(Revision()!=revision)throw new Exception("Malformed script changed the project");
            var templates=await Call("get_templates",new{});
            foreach(var item in templates["templates"]!.AsArray()) Jint.Engine.PrepareScript(item!["source"]!.GetValue<string>());
            var templated=await Call("apply_template",new{expectedRevision=revision,templateId="[Template] Global reward command — KubeJS",screen,eventName="open"});
            string templateRevision=templated["result"]!["revision"]!.GetValue<string>();
            await Call("project_control",new{expectedRevision=templateRevision,action="new",projectId="do_not_replace_dirty"},fail:true);
            await Call("undo",new{expectedRevision=templateRevision});
            var checkbox=await Call("apply_edits",new{expectedRevision=revision,edits=new[]{new{kind="upsert_element",screen,element="mcp_checkbox",data=new{type="checkbox",value="false",events=new{ @checked=new{client=new{actions=Array.Empty<object>()}}}}}}});
            string checkboxRevision=checkbox["revision"]!.GetValue<string>();
            await Call("preview_control",new{expectedRevision=checkboxRevision,action="open",screen});
            var clicked=await Call("preview_control",new{expectedRevision=checkboxRevision,action="event",element="mcp_checkbox",eventName="checked",value="true"});
            if(clicked["elements"]!.AsArray().Single(e=>e!["id"]!.GetValue<string>()=="mcp_checkbox")!["value"]!.GetValue<string>()!="true")throw new Exception("Preview event did not update value");
            await Call("preview_control",new{expectedRevision="stale",action="close"});
            await Call("undo",new{expectedRevision=checkboxRevision});
            // Test events keep working after an open_ui switches screens, even onto a screen whose tick keeps the game busy.
            var rooms=await Call("apply_edits",new{expectedRevision=revision,edits=new object[]{
                new{kind="upsert_screen",screen="mcp_room",data=new{size=new{width=256,height=224},tickInterval=16,variables=new{hit="no",ticks="0"},events=new{tick=new{client=new{script="scripts/client/mcp_tick.js",function="tick"}}}}},
                new{kind="put_script",key="scripts/client/mcp_tick.js",source="function tick(ctx) { var end = Date.now() + 12; while (Date.now() < end) Math.sqrt(1); ctx.state.set('ticks', String(Number(ctx.state.get('ticks')) + 1)); }"},
                new{kind="upsert_element",screen="mcp_room",element="mcp_hit",data=new{type="button",text="Hit",events=new{click=new{client=new{actions=new[]{new{type="set_variable",target="hit",value="yes"}}}}}}},
                new{kind="upsert_element",screen,element="mcp_door",data=new{type="button",text="Door",events=new{click=new{client=new{actions=new[]{new{type="open_ui",value="mcp_room"}}}}}}}
            }});
            string roomsRevision=rooms["revision"]!.GetValue<string>();
            await Call("preview_control",new{expectedRevision=roomsRevision,action="open",screen});
            var inRoom=await Call("preview_control",new{expectedRevision=roomsRevision,action="event",element="mcp_door",eventName="click"});
            if(inRoom["screen"]!.GetValue<string>()!="mcp_room")throw new Exception("open_ui from a test click did not switch screens: "+inRoom["screen"]);
            var clickTime=System.Diagnostics.Stopwatch.StartNew();
            var hit=await Call("preview_control",new{expectedRevision=roomsRevision,action="event",element="mcp_hit",eventName="click"});
            if(clickTime.ElapsedMilliseconds>3000)throw new Exception("A test click on a busy screen took "+clickTime.ElapsedMilliseconds+" ms");
            if(hit["variables"]!["hit"]!.GetValue<string>()!="yes")throw new Exception("A test click after open_ui did not reach the new screen: "+hit["variables"]);
            await Call("preview_control",new{expectedRevision=roomsRevision,action="close"});
            await Call("undo",new{expectedRevision=roomsRevision});
            // Playing like a person: holding an input, tapping a button by where it is, and capturing just the game.
            var play=await Call("apply_edits",new{expectedRevision=revision,edits=new object[]{
                new{kind="upsert_input",key="mcp_right",data=new{keys=new[]{"d"}}},
                new{kind="upsert_screen",screen="mcp_play",data=new{size=new{width=320,height=200},tickInterval=16,variables=new{held="no",tapped="no"},events=new{tick=new{client=new{script="scripts/client/mcp_held.js",function="tick"}}}}},
                new{kind="put_script",key="scripts/client/mcp_held.js",source="function tick(ctx) { if (ctx.input.isDown('mcp_right')) ctx.state.set('held', 'yes'); }"},
                new{kind="upsert_element",screen="mcp_play",element="mcp_tapme",data=new{type="button",text="Tap",bounds=new{x=200,y=120,width=80,height=30},events=new{click=new{client=new{actions=new[]{new{type="set_variable",target="tapped",value="yes"}}}}}}}
            }});
            string playRevision=play["revision"]!.GetValue<string>();
            await Call("preview_control",new{expectedRevision=playRevision,action="open",screen="mcp_play"});
            var idle=await Call("preview_control",new{expectedRevision=playRevision,action="wait",value="200"});
            if(idle["variables"]!["held"]!.GetValue<string>()!="no")throw new Exception("The held-input test started pressed.");
            var held=await Call("preview_control",new{expectedRevision=playRevision,action="input",element="mcp_right",value="300"});
            if(held["variables"]!["held"]!.GetValue<string>()!="yes")throw new Exception("preview_control input didn't hold the input: "+held["variables"]);
            await Call("preview_control",new{expectedRevision=playRevision,action="input",element="nope",value="100"},fail:true);
            var tapped=await Call("preview_control",new{expectedRevision=playRevision,action="tap",value="240,135"});
            if(tapped["variables"]!["tapped"]!.GetValue<string>()!="yes")throw new Exception("preview_control tap didn't click the button under it: "+tapped["variables"]);
            string gameShot=(await Call("preview_control",new{expectedRevision=playRevision,action="capture",value="game"}))["path"]!.GetValue<string>();
            var shotFrame=System.Windows.Media.Imaging.BitmapDecoder.Create(new Uri(gameShot),System.Windows.Media.Imaging.BitmapCreateOptions.None,System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
            if(Math.Abs((double)shotFrame.PixelWidth/shotFrame.PixelHeight-320.0/200)>0.02)throw new Exception("capture game isn't the game's own area: "+shotFrame.PixelWidth+"x"+shotFrame.PixelHeight);
            await Call("preview_control",new{expectedRevision=playRevision,action="close"});
            // arcadia_publish: a leaderboard page imported from a file, and a picture given by path fitted to 1280 × 800.
            string pageFile=Path.Combine(Path.GetTempPath(),"mcp-board-"+Guid.NewGuid().ToString("N")+".html"), smallPng=Path.Combine(Path.GetTempPath(),"mcp-cover-"+Guid.NewGuid().ToString("N")+".png");
            try
            {
                File.WriteAllText(pageFile,"<html><head><link href=\"https://fonts.googleapis.com/css2?family=Bungee\" rel=\"stylesheet\"></head><body>Scores</body></html>");
                var smallBitmap=new System.Windows.Media.Imaging.WriteableBitmap(64,40,96,96,System.Windows.Media.PixelFormats.Bgra32,null);
                var smallEncoder=new System.Windows.Media.Imaging.PngBitmapEncoder();smallEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(smallBitmap));
                using(var smallFile=File.Create(smallPng))smallEncoder.Save(smallFile);
                await Call("arcadia_publish",new{action="prepare",expectedRevision=playRevision,leaderboardFile=pageFile,cover=smallPng});
                if(project.Publishing.LeaderboardPage!="file" || System.Text.Encoding.UTF8.GetString(project.Publishing.LeaderboardHtml)!=File.ReadAllText(pageFile))throw new Exception("arcadia_publish leaderboardFile didn't import the page as the game's leaderboard page.");
                if(Wysicraft.Packaging.ArcadiaPackage.ImageSize(project.Publishing.Cover)!=(1280,800))throw new Exception("A cover given by path wasn't fitted to 1280 × 800: "+Wysicraft.Packaging.ArcadiaPackage.ImageSize(project.Publishing.Cover));
            }
            finally { File.Delete(pageFile); File.Delete(smallPng); }
            await Call("undo",new{expectedRevision=Revision()});await Call("undo",new{expectedRevision=Revision()});await Call("undo",new{expectedRevision=Revision()});
            await Call("get_test_status",new {});
            await Call("get_schema",new {});
            await Call("validate_project",new {});
            string savePath=Path.GetFullPath(output)+"."+Guid.NewGuid().ToString("N")+".wysicraftproj";
            await Call("save_project_as",new {expectedRevision=revision,path=savePath});
            if(!File.Exists(savePath))throw new Exception("MCP Save As failed");
            // Use the approved PNG to exercise import through the actual endpoint.
            string branding=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../assets/branding/wysicraft-wc-icon.png"));
            if(!File.Exists(branding))branding=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../Branding/wysicraft-wc-icon.png"));
            if(File.Exists(branding)) {
                var imported=await Call("import_asset",new{expectedRevision=revision,path=branding});
                string asset=project.Assets.Keys.Single(k=>k.EndsWith("wysicraft-wc-icon.png"));
                var deleted=await Call("apply_edits",new{expectedRevision=imported["revision"]!.GetValue<string>(),edits=new[]{new{kind="delete_asset",key=asset}}});
                await Call("undo",new{expectedRevision=deleted["revision"]!.GetValue<string>()});
                await Call("undo",new{expectedRevision=imported["revision"]!.GetValue<string>()});
            }
            await Call("preview_control",new{expectedRevision=revision,action="open",screen});
            var capture=await Call("preview_control",new{expectedRevision=revision,action="capture"});
            if(!File.Exists(capture["path"]!.GetValue<string>()))throw new Exception("Preview capture failed");
            await Call("preview_control",new{expectedRevision=revision,action="close"});
            await Call("minecraft_test_control",new{expectedRevision=revision,action="export",path=Path.GetFullPath(output)+".missing.jar"},fail:true);
            await Call("minecraft_test_control",new{expectedRevision="stale",action="stop"});
            var exported=await Call("export_project",new{expectedRevision=revision,format="kubejs"});
            string jarPath=exported["path"]!.GetValue<string>();
            if(!jarPath.EndsWith(".jar"))throw new Exception("KubeJS export must be a JAR");
            using(var jar=System.IO.Compression.ZipFile.OpenRead(jarPath))if(jar.GetEntry("META-INF/jarjar/metadata.json")==null)throw new Exception("Export is not bundled");
            await Call("save_project",new{expectedRevision=revision});
            var fresh=await Call("project_control",new{expectedRevision=revision,action="new",projectId="mcp_new"});
            string freshRevision=fresh["revision"]!.GetValue<string>();
            // New projects save as .arcadia; the older .wysicraftproj (savePath) still opens below.
            await Call("save_project_as",new{expectedRevision=freshRevision,path=savePath+".new.arcadia"});
            if(!File.Exists(savePath+".new.arcadia"))throw new Exception("save_project_as did not write the .arcadia file");
            var opened=await Call("project_control",new{expectedRevision=freshRevision,action="open",path=savePath});
            if(opened["revision"]!.GetValue<string>()!=revision)throw new Exception("Open project did not restore saved project");
            // New features through MCP: Made for, sounds, sprites/shapes/Sound controls, inputs, animations, physics,
            // polygon colliders with curves, rename/reorder, empty groups, the Minecraft check, web export and preview testing.
            var schema=await Call("get_schema",new {});
            foreach(var kind in new[]{"upsert_input","upsert_animation","rename_element","reorder_element","add_group","add_component","remove_component"}) if(!schema["editKinds"]!.AsArray().Any(k=>k!.GetValue<string>()==kind))throw new Exception("Schema lacks "+kind);
            string rev=revision;
            async Task Edit(params object[] batch){ var r=await Call("apply_edits",new{expectedRevision=rev,edits=batch}); rev=r["revision"]!.GetValue<string>(); }
            string soundFile=Path.Combine(Path.GetTempPath(),"mcp_pop_"+Guid.NewGuid().ToString("N")[..6]+".ogg"); File.WriteAllBytes(soundFile,[1,2,3]);
            var sound=await Call("import_asset",new{expectedRevision=rev,path=soundFile}); File.Delete(soundFile);
            string soundId=sound["sound"]!.GetValue<string>(); rev=sound["revision"]!.GetValue<string>();
            await Edit(new{kind="set_project",data=new{target="both"}},
                new{kind="upsert_input",key="jump",data=new{keys=new[]{"space"},buttons=new[]{"a"},axis="left_y-"}},
                new{kind="upsert_element",screen,element="mcp_star",data=new{type="shape",shape="star",text="",bounds=new{x=10,y=10,width=30,height=30},input="jump"}},
                new{kind="upsert_element",screen,element="mcp_sound",data=new{type="sound",sound=soundId,delay=0,volume=0.5,text=""}},
                new{kind="upsert_element",screen,element="mcp_floor",data=new{type="collider",body="static",collider="polygon",colliderPoints=new object[]{new{x=0,y=20},new{x=100,y=20},new{x=50,y=0,curve=true}},bounds=new{x=0,y=180,width=100,height=20},text="",fillEnabled=false}},
                new{kind="upsert_element",screen,element="mcp_ball",data=new{type="shape",shape="ellipse",body="dynamic",collider="circle",bounds=new{x=40,y=100,width=10,height=10},text=""}},
                new{kind="upsert_animation",screen,key="mcp_slide",data=new{duration=500,autoplay=true,tracks=new[]{new{target="mcp_star",property="x",keys=new[]{new{time=0,value=10.0,ease="linear"},new{time=500,value=60.0,ease="ease_out"}}}}}},
                new{kind="upsert_screen",screen,data=new{gravity=500}},
                new{kind="add_group",screen,key="MCP group"},
                new{kind="rename_element",screen,element="mcp_star",key="mcp_star2"},
                new{kind="reorder_element",screen,element="mcp_ball",key="back"});
            var target=project.Screens.First(s=>s.Id==screen);
            if(project.Manifest.Inputs.Count!=1 || target.Animations.Single().Tracks[0].Target!="mcp_star2" || target.Elements[0].Id!="mcp_ball" || !target.GroupParents.ContainsKey("MCP group") || target.Elements.Single(e=>e.Id=="mcp_star2").Input!="jump")
                throw new Exception("New MCP edit kinds did not apply");
            var checkedProject=await Call("validate_project",new {});
            if(!checkedProject["errors"]!.AsArray().Any(e=>e!["minecraftOnly"]!.GetValue<bool>()) || checkedProject["minecraftUses"]!.AsArray().Count<4)throw new Exception("Minecraft-only uses not reported");
            await Call("export_project",new{expectedRevision=rev,format="jar"},fail:true); // physics and inputs don't go to Minecraft
            var webFile=await Call("export_project",new{expectedRevision=rev,format="web_file"});
            if(!File.ReadAllText(webFile["path"]!.GetValue<string>()).Contains("mcp_star2"))throw new Exception("Web export lacks the new controls");
            await Call("preview_control",new{expectedRevision=rev,action="open",screen});
            var settled=await Call("preview_control",new{expectedRevision=rev,action="wait",value="1500"});
            double BallY(JsonNode state)=>state["elements"]!.AsArray().First(e=>(string?)e!["id"]=="mcp_ball")!["bounds"]!["y"]!.GetValue<double>();
            double StarX(JsonNode state)=>state["elements"]!.AsArray().First(e=>(string?)e!["id"]=="mcp_star2")!["bounds"]!["x"]!.GetValue<double>();
            if(BallY(settled)<150 || BallY(settled)>185 || Math.Abs(StarX(settled)-60)>0.5)throw new Exception($"Physics/animation did not run in preview: ball y {BallY(settled)}, star x {StarX(settled)}");
            var kicked=await Call("preview_control",new{expectedRevision=rev,action="script",value="ui.setPosition('mcp_ball', 40, 20); ui.setVelocity('mcp_ball', 0, 0);"});
            if(BallY(kicked)>60)throw new Exception("Preview script did not move the body: y "+BallY(kicked));
            // Arcadia over MCP: status and a prepared package (with a screenshot captured from Preview), never an upload.
            var arcadia=await Call("arcadia_publish",new{action="status"});
            if(arcadia["runtimeSha256"]!.GetValue<string>()!=ArcadiaPackage.RuntimeSha256 || arcadia["scoreSources"]==null)throw new Exception("Arcadia status incomplete: "+arcadia.ToJsonString());
            await Call("arcadia_publish",new{action="publish"},fail:true);
            var prepared=await Call("arcadia_publish",new{action="prepare",expectedRevision=rev,settings="{\"title\":\"MCP smoke\",\"genre\":[\"Arcade\"],\"version\":\"1.0.0\",\"controls\":\"Click\",\"mobile\":true,\"videos\":[\"https://youtu.be/aBcDeFgHiJk\"]}",cover="capture",screenshots="[\"capture\",\"capture\"]"});
            rev=prepared["revision"]!.GetValue<string>();
            string packagePath=prepared["package"]!.GetValue<string>();
            using(var package=System.IO.Compression.ZipFile.OpenRead(packagePath))
                if(prepared["blocked"]!.GetValue<bool>() || package.Entries[0].FullName!="game.json" || package.GetEntry("cover.png")==null || package.GetEntry("screenshots/2.png")==null || package.GetEntry("wysicraft/wysicraft-web.js")==null)
                    throw new Exception("Arcadia package wrong: "+prepared.ToJsonString());
            if(project.Publishing.Title!="MCP smoke" || ArcadiaPackage.ImageSize(project.Publishing.Cover)!=(1280,800) || project.Publishing.Screenshots.Count!=2 || !project.Publishing.Mobile || project.Publishing.Videos.Count!=1)throw new Exception("Arcadia settings, cover or screenshots not kept in the project");
            try { Directory.Delete(Path.GetDirectoryName(packagePath)!,true); } catch { }
            // The same settings as an object, which is what the tool's schema now describes (the call above sent them the
            // old way, as a string of JSON, and still worked). Only the fields given change; an empty list clears the gallery.
            var typed=await Call("arcadia_publish",new{action="prepare",expectedRevision=rev,settings=new{description="Typed over MCP.",leaderboard=true,scores=new{label="Points",max=5000,score=new{variable="score",path=""},triggers=new[]{new{variable="mode",path="",equals="over"}}}},screenshots=Array.Empty<string>()});
            rev=typed["revision"]!.GetValue<string>();
            try { Directory.Delete(Path.GetDirectoryName(typed["package"]!.GetValue<string>())!,true); } catch { }
            var typedNow=project.Publishing;
            if(typedNow.Title!="MCP smoke" || typedNow.Description!="Typed over MCP." || !typedNow.Mobile || !typedNow.Leaderboard || typedNow.Scores.Label!="Points" || typedNow.Scores.Max!=5000 || typedNow.Scores.Score.Variable!="score" || typedNow.Scores.Triggers is not [{ Variable:"mode",EqualsValue:"over" }] || typedNow.Screenshots.Count!=0)
                throw new Exception("Typed arcadia_publish settings were not applied as given: "+Json.Write(typedNow));
            // The schema names each field and its type, and a value of the wrong type is refused before anything changes.
            JsonNode Schema(string tool)=>listing["tools"]!.AsArray().First(t=>t!["name"]!.GetValue<string>()==tool)!["inputSchema"]!["properties"]!;
            string settingsSchema=Schema("arcadia_publish")["settings"]!.ToJsonString();
            if(!settingsSchema.Contains("\"title\"") || !settingsSchema.Contains("\"mobile\"") || !settingsSchema.Contains("boolean") || !settingsSchema.Contains("\"triggers\"") || !Schema("arcadia_publish")["screenshots"]!.ToJsonString().Contains("array"))
                throw new Exception("arcadia_publish's schema doesn't describe its settings: "+settingsSchema);
            if(!Schema("itch_publish")["settings"]!.ToJsonString().Contains("\"webChannel\"") || !Schema("answer_request")["payload"]!.ToJsonString().Contains("\"inputName\""))
                throw new Exception("itch_publish or answer_request doesn't describe its input.");
            bool refusedType=false;
            try { var answer=await Rpc("tools/call",new{name="arcadia_publish",arguments=new{action="prepare",expectedRevision=rev,settings=new{mobile="yes"}}}); refusedType=answer["isError"]?.GetValue<bool>() ?? false; }
            catch(InvalidOperationException) { refusedType=true; }
            if(!refusedType || !project.Publishing.Mobile || Revision()!=rev) throw new Exception("A setting of the wrong type was not refused.");
            File.AppendAllText(output+".sizes.txt","\narcadia_publish settings schema: "+settingsSchema.Length+" chars");
            // itch.io over MCP: status, and a prepared web upload for a game given by its page address; never an upload.
            // Soft drawing over MCP: a hard-edged disc, a soft brush stroke across it, then its edges smoothed.
            var softArt=await Call("pixel_art",new{expectedRevision=rev,newName="mcp_soft",width=32,height=32,commands=new object[]{
                new{op="ellipse",x=4,y=4,x2=27,y2=27,color="#FFFFFF",filled=true},
                new{op="brush",points=new[]{new[]{8,16},new[]{24,16}},color="#FF0000",size=7,hardness=0.4},
                new{op="smooth"}}});
            rev=softArt["revision"]!.GetValue<string>();
            var softPng=project.Assets.First(a=>a.Key.EndsWith("mcp_soft.png")).Value; var softImage=DecodePixels(softPng);
            if(softImage.Get(16,16)!=0xFFFF0000u || softImage.Pixels.Count(p=>p>>24 is >0 and <255)<20 || softImage.Pixels.Distinct().Count()<30) throw new Exception("pixel_art brush and smooth did not draw soft art: "+softImage.Pixels.Distinct().Count()+" colors");
            await Call("pixel_art",new{expectedRevision=rev,newName="mcp_soft_bad",width=8,height=8,commands=new object[]{new{op="brush",color="#FFFFFF",hardness=3}}},fail:true);
            var itch=await Call("itch_publish",new{action="status"});
            if(itch["settings"]==null || itch["note"]==null)throw new Exception("itch.io status incomplete: "+itch.ToJsonString());
            await Call("itch_publish",new{action="publish"},fail:true);
            await Call("itch_publish",new{action="prepare",expectedRevision=rev,settings="{\"target\":\"not a game\"}"},fail:true);
            var itchPrepared=await Call("itch_publish",new{action="prepare",expectedRevision=rev,settings="{\"target\":\"https://smoke.itch.io/mcp-game\",\"web\":true,\"windows\":false,\"webChannel\":\"html5\"}"});
            rev=itchPrepared["revision"]!.GetValue<string>();
            var itchBuild=itchPrepared["builds"]!.AsArray().Single()!;
            string itchFolder=itchBuild["folder"]!.GetValue<string>();
            if(itchPrepared["blocked"]!.GetValue<bool>() || project.Publishing.Itch.Target!="smoke/mcp-game" || itchBuild["channel"]!.GetValue<string>()!="html5" || !File.Exists(Path.Combine(itchFolder,"index.html")))
                throw new Exception("itch.io prepare wrong: "+itchPrepared.ToJsonString());
            try { Directory.Delete(Path.GetDirectoryName(itchFolder)!,true); } catch { }
            await Call("preview_control",new{expectedRevision=rev,action="close"});
            await Edit(new{kind="delete_input",key="jump"},new{kind="delete_animation",screen,key="mcp_slide"});
            // Components through MCP: a pickup on the star writes and wires its script, and removing it cleans up.
            await Edit(new{kind="add_component",screen,element="mcp_star2",key="pickup"});
            var withPickup=await Call("get_project",new{});
            if(!withPickup.ToJsonString().Contains("mcp_star2_pickup.js")||!withPickup.ToJsonString().Contains("\"behaviours\":[\"pickup\"]"))throw new Exception("add_component did not write the pickup script or record the component");
            await Edit(new{kind="remove_component",screen,element="mcp_star2",key="pickup"});
            if((await Call("get_project",new{})).ToJsonString().Contains("mcp_star2_pickup.js"))throw new Exception("remove_component kept an untouched script");
            try{ await Edit(new{kind="add_component",screen,element="mcp_star2",key="jetpack"}); throw new Exception("An unknown component was accepted"); }catch(Exception ex) when(!ex.Message.Contains("unknown component was accepted")){ }
            if(project.Manifest.Inputs.Count!=0 || project.Screens.First(s=>s.Id==screen).Elements.Any(e=>e.Input.Length>0))throw new Exception("delete_input left references behind");
            // Pixel art and sprite sheets: draw a two-frame picture with a second layer, read it back, edit it, then animate a sprite with it.
            var red=new Dictionary<string,string>{["R"]="#FF0000"}; var green=new Dictionary<string,string>{["G"]="#00FF00"};
            var art=await Call("pixel_art",new{expectedRevision=rev,newName="mcp_hero",width=4,height=4,frames=2,commands=new object[]{
                new{op="grid",rows=new[]{"RR..","RR.."},palette=red},
                new{op="add_layer",name="eyes"},
                new{op="pixels",layer="eyes",points=new[]{new[]{1,1}},color="#0000FF"},
                new{op="grid",layer="Layer 1",frame=1,rows=new[]{"..GG","..GG"},palette=green}}});
            rev=art["revision"]!.GetValue<string>(); string artTexture=art["texture"]!.GetValue<string>();
            var frame0=art["frame0"]!["rows"]!.AsArray().Select(r=>r!.GetValue<string>()).ToArray();
            if(artTexture!=project.Manifest.Id+":textures/gui/image/mcp_hero.png" || art["sheet"]!["width"]!.GetValue<int>()!=8 || frame0[0]!="AA.." || frame0[1]!="AB.." || !File.Exists(art["preview"]!.GetValue<string>()))
                throw new Exception("pixel_art didn't draw as asked: "+art.ToJsonString());
            if(!project.Assets.ContainsKey(TextureAssets.Path(project.Manifest.Id,"mcp_hero.png")+TextureAssets.LayersSuffix))throw new Exception("pixel_art didn't keep its layers");
            var looked=await Call("read_pixel_art",new{image="mcp_hero",frame=1});
            if(looked["grid"]!["rows"]![0]!.GetValue<string>()!="..AA" || looked["layersFrom"]!.GetValue<string>()!="saved layers" || looked["picture"]!["layers"]!.AsArray().Count!=2)throw new Exception("read_pixel_art: "+looked.ToJsonString());
            var hidden=await Call("pixel_art",new{expectedRevision=rev,image=artTexture,commands=new object[]{new{op="set_layer",layer="eyes",visible=false},new{op="fill",layer="Layer 1",x=3,y=3,color="#80FFFFFF"}}});
            rev=hidden["revision"]!.GetValue<string>();
            var hiddenRows=hidden["frame0"]!["rows"]!.AsArray().Select(r=>r!.GetValue<string>()).ToArray();
            if(hiddenRows[1]!="AABB" || hidden["startedFrom"]!.GetValue<string>()!="saved layers")throw new Exception("pixel_art editing an image: "+hidden.ToJsonString());
            await Call("pixel_art",new{expectedRevision=rev,image="mcp_hero",commands=new object[]{new{op="line",color="blue-ish"}}},fail:true);
            await Call("pixel_art",new{expectedRevision=rev,image="mcp_hero",commands=new object[]{new{op="pixels",layer="nope",points=new[]{new[]{0,0}},color="#FF0000"}}},fail:true);
            await Edit(new{kind="upsert_element",screen,element="mcp_sprite",data=new{type="sprite",text="",bounds=new{x=0,y=0,width=16,height=16}}});
            var animated=await Call("sprite_sheet",new{expectedRevision=rev,screen,element="mcp_sprite",texture=artTexture,frameWidth=4,frameHeight=4,
                clips=new object[]{new{name="walk",frames=new[]{0,1},fps=6},new{name="pose",frames=new[]{1},once=true}},playing="walk"});
            rev=animated["revision"]!.GetValue<string>();
            var sprite=project.Screens.First(s=>s.Id==screen).Elements.Single(e=>e.Id=="mcp_sprite");
            if(sprite.Clips!="walk: 0,1 @6; pose: 1 @8 once" || sprite.Value!="walk" || sprite.Texture!=artTexture || animated["sprite"]!["frameCount"]!.GetValue<int>()!=2)throw new Exception("sprite_sheet: "+animated.ToJsonString());
            await Call("sprite_sheet",new{expectedRevision=rev,screen,element="mcp_sprite",clips=new object[]{new{name="far",frames=new[]{5}}}},fail:true);
            await Call("sprite_sheet",new{expectedRevision=rev,screen,element="mcp_star2",clips=new object[]{new{name="x",frames=new[]{0}}}},fail:true);
            var readSprite=await Call("read_sprite_sheet",new{screen,element="mcp_sprite"});
            if(readSprite["frameCount"]!.GetValue<int>()!=2 || readSprite["playing"]!.GetValue<string>()!="walk" || readSprite["parsedClips"]!.AsArray().Count!=2)throw new Exception("read_sprite_sheet: "+readSprite.ToJsonString());
            var undoneSprite=await Call("undo",new{expectedRevision=rev}); rev=undoneSprite["revision"]!.GetValue<string>();
            if(project.Screens.First(s=>s.Id==screen).Elements.Single(e=>e.Id=="mcp_sprite").Clips.Length!=0)throw new Exception("sprite_sheet isn't one Undo step");
            if(ProjectStore.Files(project,true).Keys.Any(TextureAssets.IsEditorOnly))throw new Exception("Layers from pixel_art leaked into exports");
            await StopMcp();
            try { using var stopped=await client.GetAsync(mcpUrl); throw new Exception("Server still accepts connections after stopping."); }
            catch(HttpRequestException) { }
            File.WriteAllText(output,"PASS: 34 MCP tools, tutorial (targets, highlight, clear, status), speech (status, voices, mix, add_sound, transcribe), video (status), particles (+ ui.burst in Preview), pixel_art live, connect instructions, guide topics, itch_publish (status, prepare), pixel_art (new, layers, frames, grid, fill, edit with saved layers, errors), read_pixel_art, sprite_sheet (clips, checks, one Undo), read_sprite_sheet, authentication, new edit kinds (inputs, animations, rename, reorder, groups, add/remove component), sounds, sprites/shapes/colliders/physics, Minecraft-only validation and export blocking, web export, Arcadia prepare (settings, Preview screenshot, package; no upload), preview script/wait/state, atomic edits/undo, syntax rejection, template discovery/assignment, asset deletion, preview value changes, stale-revision close/stop, launch failure reporting, bundled KubeJS export, project new/open/save, capture, and shutdown. No Minecraft launch needed.");
        }
        finally { await StopMcp(); dirty=false; }
    }
}
