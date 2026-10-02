using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using Wysicraft.Core;
using Wysicraft.Models;
using Validation = Wysicraft.Core.Validation;
namespace Wysicraft.Designer;

// The Input creator's round trip, over the real MCP server rather than a stand-in: queue a request the way the
// window does, read it back through pending_requests, answer it through answer_request, then apply the answer and
// check the project still validates. This is the loop the feature rests on, so it is worth testing as one piece.
public partial class MainWindow
{
    internal async Task VerifyInputCreatorAsync(string output)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        int requestId = 0;
        async Task<JsonNode> Rpc(string method, object args)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, mcpUrl);
            request.Headers.Add("Authorization", "Bearer " + mcpToken);
            request.Headers.Add("Accept", "application/json, text/event-stream");
            request.Headers.Add("MCP-Protocol-Version", "2025-11-25");
            if (mcpSessionId != null) request.Headers.TryAddWithoutValidation("Mcp-Session-Id", mcpSessionId);
            request.Content = new StringContent(Json.Write(new { jsonrpc = "2.0", id = ++requestId, method, @params = args }), Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request);
            if (response.Headers.TryGetValues("Mcp-Session-Id", out var given)) mcpSessionId = given.FirstOrDefault();
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            if (body.StartsWith("event:") || body.StartsWith("data:")) body = body.Split('\n').First(l => l.StartsWith("data: "))[6..];
            var parsed = JsonNode.Parse(body)!;
            if (parsed["error"] != null) throw new InvalidOperationException(parsed.ToJsonString());
            return parsed["result"]!;
        }
        async Task<(bool Failed, string Text)> Call(string name, object args)
        {
            var result = await Rpc("tools/call", new { name, arguments = args });
            return (result["isError"]?.GetValue<bool>() ?? false, result["content"]![0]!["text"]!.GetValue<string>());
        }

        await StartMcp();
        // A client that says it can do sampling and elicitation: the panel has to notice, because whether the app
        // can ever ask the assistant something of its own accord depends on exactly this.
        await Rpc("initialize", new
        {
            protocolVersion = "2025-11-25",
            capabilities = new { sampling = new { }, elicitation = new { }, roots = new { } },
            clientInfo = new { name = "Arcadia Studio input smoke", version = "1" }
        });
        if (!mcpCapabilities.TryGetValue("Arcadia Studio input smoke", out var offered) || !offered.Contains("sampling") || !offered.Contains("elicitation"))
            throw new Exception("Client capabilities were not recorded: " + (offered ?? "(nothing)"));

        // A project with something worth talking about, so pending_requests has real context to hand over.
        project = new Project(); project.Manifest.Id = "inputsmoke"; ui = project.Screens[0];
        history.Clear(); selected.Clear();
        ui.Variables["paused"] = "false";
        ui.Elements.Add(new Element { Id = "menu", Type = "panel", Visible = false, Bounds = new() { X = 40, Y = 30, Width = 200, Height = 120 } });
        RefreshAll();

        // Only free buttons are offered, and one already bound never is.
        project.Manifest.Inputs.Add(new GameInput { Name = "jump", Keys = ["space"] });
        var free = FreeBindings("keyboard");
        if (free.Contains("space")) throw new Exception("A key already bound was offered as free");
        if (!free.Contains("p")) throw new Exception("A free key was missing from the list");
        if (FreeBindings("xbox").Contains("space")) throw new Exception("Keyboard keys leaked into the gamepad list");
        if (BindingLabel("playstation", "a") != "Cross (a)" || BindingLabel("xbox", "a") != "A") throw new Exception("Device button labels are wrong");

        // The Ask Agent window: it needs MCP, which is up now. The typing box has to actually get the room --
        // a DockPanel hands its space to the last child, so one line added in the wrong order turns the box into a
        // narrow strip down the side, which is exactly what happened the first time.
        ui.Elements.Add(new Element { Id = "art1", Type = "image", Bounds = new() { X = 4, Y = 4, Width = 32, Height = 32 } });
        foreach (var kind in new[] { "help" })
        {
            ShowAskAgent(ui.Elements.First(e => e.Id == "art1"), kind);
            var asked = OwnedWindows.Cast<Window>().FirstOrDefault(w => w.Title.StartsWith(kind == "art" ? "Draw it" : "Ask Agent"))
                ?? throw new Exception("The Ask Agent window did not open for " + kind);
            asked.UpdateLayout();
            var typing = LogicalDescendants(asked).OfType<TextBox>().FirstOrDefault(t => !t.IsReadOnly) ?? throw new Exception("Ask Agent has no box to type in");
            if (typing.ActualWidth < asked.ActualWidth / 2)
                throw new Exception($"The Ask Agent box is {typing.ActualWidth:0} wide in a {asked.ActualWidth:0} window: it is not getting the space");
            if (typing.ActualHeight < 60) throw new Exception($"The Ask Agent box is only {typing.ActualHeight:0} tall");
            try { CaptureWindow(asked, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(output)!, "AskAgent_" + kind + ".png")); } catch (Exception) { }
            asked.Close();
        }

        // The assistant: chosen in memory only, so the check never writes a choice into the person's preferences.
        // Whether Claude Code is installed and signed in on this machine cannot be required of a check, so both
        // outcomes are accepted; what matters is that every path reports rather than throws.
        var prefs = Prefs(); string hadTool = prefs.AssistantTool;
        try
        {
            prefs.AssistantTool = "claude";
            var found = await CheckAssistantAsync();
            if (found.Path == null) Log("Assistant: Claude Code is not installed here, so that path is untested on this machine.");
            else
            {
                if (found.Version.Length == 0) throw new Exception("An installed assistant should report a version");
                if (found.SignedIn is not ("yes" or "no" or "unknown")) throw new Exception("Sign-in state must be yes, no or unknown: " + found.SignedIn);
                if (found.SignedIn == "no" && AssistantReady()) throw new Exception("A signed-out assistant must not count as ready");
                if (found.SignedIn == "yes" && !CanRunAgent) throw new Exception("A signed-in, installed assistant with MCP up should be runnable");
                if (AssistantChatCommand().Length == 0 || !AssistantChatCommand().Contains("--mcp-config")) throw new Exception("The start command should attach the MCP config");
                if (CanRunAgent)
                {
                    var run = await RunAgentAsync("Reply with exactly: WYSICRAFT_RUNNER_OK");
                    if (run.Ok && !run.Text.Contains("WYSICRAFT_RUNNER_OK")) throw new Exception("The assistant answered something unexpected: " + run.Text);
                    if (!run.Ok && run.Trouble.Length == 0) throw new Exception("A failed run must say why");
                    Log("Assistant run: " + (run.Ok ? "answered" : run.Trouble));
                }
            }
            // The stable address: the token survives a restart and the config file says the same thing twice.
            string before = mcpToken;
            if (!File.Exists(SaveMcpConfigFile())) throw new Exception("The MCP config file was not written");
            if (!File.ReadAllText(SaveMcpConfigFile()).Contains(mcpToken)) throw new Exception("The config file does not carry the current token");
            if (Prefs().McpTokenProtected.Length == 0) throw new Exception("The token was not persisted");
            // The preferred port is used whenever it is free, which is what makes a saved config keep working.
            if (PortIsFree(Prefs().McpPort) && !mcpUrl.Contains(":" + Prefs().McpPort + "/")) throw new Exception("The server did not use its preferred port: " + mcpUrl);
            if (LoadOrMakeToken() != before) throw new Exception("Reading the persisted token back gave a different value");
        }
        finally { prefs.AssistantTool = hadTool; }

        // Queue one the way the window does.
        agentRequests.Clear();
        var ask = new AgentRequest { Device = "keyboard", Binding = "p", Want = "pause the game and show the menu panel", Status = "waiting" };
        agentRequests.Add(ask);

        var (failed, text) = await Call("pending_requests", new { });
        if (failed) throw new Exception("pending_requests failed: " + text);
        foreach (var expected in new[] { ask.Id, "pause the game", "\"menu\"", "paused", "inputsmoke" })
            if (!text.Contains(expected)) throw new Exception($"pending_requests left out {expected}: " + text[..Math.Min(400, text.Length)]);

        // A bad answer has to be refused rather than half-applied.
        const string good = "function pause(ctx) {\n    if (ctx.value !== 'pause') return;\n    var paused = ctx.state.get('paused') !== 'true';\n    ctx.state.set('paused', paused ? 'true' : 'false');\n    ctx.ui.setVisible('menu', paused);\n}\n";
        foreach (var (payload, why) in new[]
        {
            (Json.Write(new { id = "nope", inputName = "pause", script = good, function = "pause" }), "an unknown request id"),
            (Json.Write(new { id = ask.Id, inputName = "Not A Name", script = good, function = "pause" }), "an invalid input name"),
            (Json.Write(new { id = ask.Id, inputName = "pause", script = "function pause(ctx) { if (", function = "pause" }), "a script that does not parse"),
            (Json.Write(new { id = ask.Id, inputName = "pause", script = good, function = "missing" }), "a function the script does not have"),
            (Json.Write(new { id = ask.Id, inputName = "pause", script = good, function = "" }), "a script with no function named"),
        })
        {
            var (bad, message) = await Call("answer_request", new { payload });
            if (!bad) throw new Exception("answer_request accepted " + why);
            if (ask.Status != "waiting") throw new Exception("A refused answer changed the request: " + ask.Status);
        }

        var (wrong, reply) = await Call("answer_request", new { payload = Json.Write(new { id = ask.Id, inputName = "pause", script = good, function = "pause", notes = "Toggles the paused variable and shows the menu." }) });
        if (wrong) throw new Exception("answer_request refused a good answer: " + reply);
        if (ask.Status != "answered" || ask.Name != "pause" || !ask.Script.Contains("setVisible")) throw new Exception("The answer was not held on the request");
        if (project.Manifest.Inputs.Any(i => i.Name == "pause")) throw new Exception("answer_request changed the project; it is meant to stay a draft");

        // Test runs against a copy, so it can say whether saving would work without doing it.
        var trial = Json.Clone(project);
        string problem = ApplyRequest(trial, trial.Screens.First(s => s.Id == ui.Id), ask);
        if (problem.Length > 0) throw new Exception("Applying the answer to a copy failed: " + problem);
        if (Validation.Errors(trial).Count > 0) throw new Exception("The copy did not validate: " + string.Join("; ", Validation.Errors(trial)));
        if (project.Manifest.Inputs.Any(i => i.Name == "pause")) throw new Exception("Testing changed the real project");

        // Saving for real: the input, the script and the screen event all land, as one undo step.
        Change();
        problem = ApplyRequest(project, ui, ask);
        if (problem.Length > 0) throw new Exception("Saving failed: " + problem);
        var saved = project.Manifest.Inputs.FirstOrDefault(i => i.Name == "pause") ?? throw new Exception("The input was not added");
        if (!saved.Keys.Contains("p")) throw new Exception("The input did not get its key");
        if (!project.Scripts.ContainsKey("scripts/client/pause_input.js")) throw new Exception("The script was not added");
        if (ui.Events["input_pressed"].Client.Function != "pause") throw new Exception("The screen event was not wired");
        if (Validation.Errors(project).Count > 0) throw new Exception("The project did not validate after saving: " + string.Join("; ", Validation.Errors(project)));
        history.Undo();
        if (project.Manifest.Inputs.Any(i => i.Name == "pause")) throw new Exception("Saving was not one undo step");

        // A gamepad request binds a button rather than a key.
        var pad = new AgentRequest { Device = "xbox", Binding = "y", Name = "special", Want = "use the special move", Status = "answered", Script = good, Function = "pause" };
        var padTrial = Json.Clone(project);
        if (ApplyRequest(padTrial, padTrial.Screens.First(s => s.Id == ui.Id), pad).Length > 0) throw new Exception("A gamepad request would not apply");
        var padInput = padTrial.Manifest.Inputs.First(i => i.Name == "special");
        if (padInput.Buttons.Count != 1 || padInput.Keys.Count != 0) throw new Exception("A gamepad request should bind a button, not a key");

        File.WriteAllText(output, "PASS: client capability reporting, free-binding lists per device, pending_requests hands over the project, answer_request refuses bad answers and holds good ones as drafts, test runs on a copy, save adds input + script + event as one undo step, gamepad requests bind buttons.");
    }
}
