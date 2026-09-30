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
            var listing=await Rpc("tools/list",new {});
            if(listing["tools"]!.AsArray().Count!=30) throw new Exception("Tools missing: "+listing["tools"]!.AsArray().Count);
            if(connectionOnly) { if(!mcpClients.ContainsKey("Arcadia Studio smoke") || mcpRequests<2) throw new Exception("Client visibility missing"); File.WriteAllText(output,"PASS: authentication, origin checks, MCP discovery and client visibility."); return; }
            string index=await CallText("guide",new {});
            if(!index.Contains("apply_edits") || !McpGuideTopicNames.All(index.Contains)) throw new Exception("Guide index missing topics.");
            foreach(string topic in McpGuideTopicNames) { string page=await CallText("guide",new { topic }); if(page.Length<400) throw new Exception("Guide topic "+topic+" is empty."); }
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
            // itch.io over MCP: status, and a prepared web upload for a game given by its page address; never an upload.
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
            File.WriteAllText(output,"PASS: 30 MCP tools, connect instructions, guide topics, itch_publish (status, prepare), pixel_art (new, layers, frames, grid, fill, edit with saved layers, errors), read_pixel_art, sprite_sheet (clips, checks, one Undo), read_sprite_sheet, authentication, new edit kinds (inputs, animations, rename, reorder, groups, add/remove component), sounds, sprites/shapes/colliders/physics, Minecraft-only validation and export blocking, web export, Arcadia prepare (settings, Preview screenshot, package; no upload), preview script/wait/state, atomic edits/undo, syntax rejection, template discovery/assignment, asset deletion, preview value changes, stale-revision close/stop, launch failure reporting, bundled KubeJS export, project new/open/save, capture, and shutdown. No Minecraft launch needed.");
        }
        finally { await StopMcp(); dirty=false; }
    }
}
