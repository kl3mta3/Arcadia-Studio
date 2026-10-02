using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Wysicraft.Core;
using Wysicraft.Models;
namespace Wysicraft.Designer;

// The assistant the editor can start for itself.
//
// Nothing here is tied to one AI. An assistant is a command-line tool that (a) can be told about an MCP server and
// (b) can be run with a prompt and no terminal. Any tool that does both can sit behind Ask Agent; the known ones are
// data in Assistants, a custom one is whatever command the person types. Each uses its own login — the person's own
// subscription — because the editor never talks to a model directly and never holds a key.
//
// Two ways to run one. A headless run is per request: Ask Agent starts it, it connects back over MCP, does the work
// with the tools, answers, and exits. A chat session is the interactive tool in its own terminal with Wysicraft
// attached, started and stopped from the MCP panel — "give your AI the config", as a button.
public sealed record AssistantTool(
    string Id, string Name, string[] Executables, string[] VersionArguments,
    string InstallCommand, string InstallNote, string[] LoginArguments, string[] StatusArguments, bool StatusByExitCode,
    string[] HeadlessArguments, string[] ChatArguments, string Requires,
    // How the tool is told about this server. A tool uses one or more of: an argument naming a config file
    // ({mcpConfig}), a file written into the run's working directory (CwdFile), environment variables (Environment),
    // or an entry merged into the tool's own config in the user's profile (HomeFile, when nothing else exists).
    string CwdFile, string CwdFileBody, Dictionary<string, string> Environment, string HomeFile, string HomeFileEntry);

public static class Assistants
{
    // Placeholders: {prompt}, {mcpConfig} (a JSON file in the mcpServers shape), {url}, {token}, {tools}, {toolList}.
    // Every value here is from the tool's own documentation or its --help; nothing is guessed. Sources are in
    // docs/engine-upgrade-plan.md.
    static readonly Dictionary<string, string> None = [];
    const string ServerShape = "{\"mcpServers\":{\"arcadia-studio\":{\"httpUrl\":\"{url}\",\"headers\":{\"Authorization\":\"Bearer {token}\"},\"trust\":true,\"includeTools\":[{toolList}]}}}";
    public static readonly AssistantTool[] Known =
    [
        new("claude", "Claude Code", ["claude.exe", "claude"], ["--version"],
            "irm https://claude.ai/install.ps1 | iex", "Or: winget install Anthropic.ClaudeCode. Git for Windows is optional.",
            ["auth", "login"], ["auth", "status"], false,
            ["-p", "{prompt}", "--mcp-config", "{mcpConfig}", "--output-format", "stream-json", "--verbose", "--permission-mode", "acceptEdits", "--allowedTools", "{tools}"],
            ["--mcp-config", "{mcpConfig}"],
            "A Pro, Max, Team or Enterprise plan; the free plan does not include Claude Code.",
            "", "", None, "", ""),
        new("codex", "Codex (OpenAI)", ["codex.cmd", "codex.exe", "codex"], ["--version"],
            "npm install -g @openai/codex", "Needs Node.js. Sign in with a ChatGPT account.",
            ["login"], ["login", "status"], true,
            ["exec", "--json", "--skip-git-repo-check", "-s", "workspace-write",
             "-c", "mcp_servers.arcadia-studio.url=\"{url}\"", "-c", "mcp_servers.arcadia-studio.bearer_token_env_var=\"ARCADIA_STUDIO_MCP_TOKEN\"", "{prompt}"],
            ["-c", "mcp_servers.arcadia-studio.url=\"{url}\"", "-c", "mcp_servers.arcadia-studio.bearer_token_env_var=\"ARCADIA_STUDIO_MCP_TOKEN\""],
            "A ChatGPT Plus, Pro, Team or Enterprise plan.",
            "", "", new() { ["ARCADIA_STUDIO_MCP_TOKEN"] = "{token}" }, "", ""),
        new("gemini", "Gemini CLI (Google)", ["gemini.cmd", "gemini.exe", "gemini"], ["--version"],
            "npm install -g @google/gemini-cli", "Needs Node.js. Sign in with a Google account the first time it runs.",
            [], [], false,
            ["-p", "{prompt}", "--output-format", "json"],
            [],
            "A Google account; the free tier is enough.",
            ".gemini/settings.json", ServerShape, None, "", ""),
        new("qwen", "Qwen Code (Alibaba)", ["qwen.cmd", "qwen.exe", "qwen"], ["--version"],
            "npm install -g @qwen-code/qwen-code@latest", "Needs Node.js. Run it once and use /auth to sign in.",
            [], [], false,
            ["-p", "{prompt}", "--output-format", "json"],
            [],
            "A Qwen account; there is a free tier.",
            ".qwen/settings.json", ServerShape, None, "", ""),
        new("kimi", "Kimi Code (Moonshot)", ["kimi.exe", "kimi.cmd", "kimi"], ["--version"],
            "irm https://code.kimi.com/kimi-code/install.ps1 | iex", "Or: npm install -g @moonshot-ai/kimi-code.",
            ["login"], [], false,
            ["-p", "{prompt}", "--output-format", "text", "--yolo"],
            [],
            "A Kimi account.",
            ".kimi-code/mcp.json", "{\"mcpServers\":{\"arcadia-studio\":{\"url\":\"{url}\",\"headers\":{\"Authorization\":\"Bearer {token}\"}}}}", None, "", ""),
        new("opencode", "opencode", ["opencode.cmd", "opencode.exe", "opencode"], ["--version"],
            "npm install -g opencode-ai", "Or: scoop install opencode, or choco install opencode.",
            ["auth", "login"], [], false,
            ["run", "{prompt}"],
            [],
            "An account with any provider it supports.",
            "", "", new() { ["OPENCODE_CONFIG"] = "{opencodeConfig}" }, "", ""),
        new("copilot", "GitHub Copilot CLI", ["copilot.cmd", "copilot.exe", "copilot"], ["--version"],
            "npm install -g @github/copilot", "Or: winget install GitHub.Copilot. Run it once and use /login. The server is added to ~/.copilot/mcp-config.json, beside anything already there.",
            [], [], false,
            ["-p", "{prompt}", "--output-format=json", "--allow-tool", "arcadia-studio"],
            [],
            "A GitHub Copilot subscription.",
            "", "", None, ".copilot/mcp-config.json", "{\"type\":\"http\",\"url\":\"{url}\",\"headers\":{\"Authorization\":\"Bearer {token}\"}}"),
    ];
    public const string Custom = "custom";

    public static AssistantTool? Find(string id) => Known.FirstOrDefault(t => t.Id == id);

    /// <summary>The tools the spawned assistant may use. Named rather than wildcarded so a run started by a button
    /// press cannot reach beyond this editor — no shell, no file writes, no network.</summary>
    public static readonly string[] EditorTools =
    [
        "get_project", "get_schema", "guide", "validate_project", "pending_requests", "answer_request",
        "apply_edits", "put_script", "pixel_art", "read_pixel_art", "sprite_sheet", "read_sprite_sheet",
        "sound_effect", "compose_music", "get_templates", "apply_template", "import_asset"
    ];
}

public partial class MainWindow
{
    internal sealed record AgentRun(bool Ok, string Text, string Trouble);
    internal sealed record AssistantCheck(string? Path, string Version, string SignedIn);   // SignedIn: yes, no, unknown

    /// <summary>Where a tool's executable is, or null.</summary>
    internal static string? FindExecutable(IEnumerable<string> names)
    {
        var folders = new List<string> { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin") };
        folders.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Where(f => f.Length > 0));
        foreach (var name in names)
            foreach (var folder in folders)
                try { var full = Path.Combine(folder, name); if (File.Exists(full)) return full; } catch (ArgumentException) { }
        return null;
    }

    /// <summary>The chosen assistant's executable, or null when none is chosen or it is not installed.</summary>
    internal string? AgentCommand()
    {
        var prefs = Prefs();
        if (prefs.AssistantTool == Assistants.Custom)
            return prefs.AssistantCustomCommand.Length > 0 && (File.Exists(prefs.AssistantCustomCommand) || FindExecutable([prefs.AssistantCustomCommand]) != null)
                ? (File.Exists(prefs.AssistantCustomCommand) ? prefs.AssistantCustomCommand : FindExecutable([prefs.AssistantCustomCommand])) : null;
        return Assistants.Find(prefs.AssistantTool) is { } tool ? FindExecutable(tool.Executables) : null;
    }

    /// <summary>Set up means chosen, installed and signed in (where the tool can say). Start and Ask Agent hang off this.</summary>
    internal bool AssistantReady() => AgentCommand() != null && (assistantSignedIn != "no");
    internal bool CanRunAgent => AssistantReady() && mcpUrl.Length > 0 && mcpToken.Length > 0;
    string assistantSignedIn = "unknown";

    /// <summary>Asks the tool itself whether it is installed and signed in. Runs the tool, so it is done off the UI
    /// thread and its answer is remembered until the next check.</summary>
    internal async Task<AssistantCheck> CheckAssistantAsync()
    {
        var prefs = Prefs();
        var tool = Assistants.Find(prefs.AssistantTool);
        var exe = AgentCommand();
        if (exe == null) { assistantSignedIn = "unknown"; return new(null, "", "unknown"); }
        string version = tool == null ? "" : (await Capture(exe, tool.VersionArguments, 15)).Trim().Split('\n').FirstOrDefault() ?? "";
        string signedIn = "unknown";
        if (tool != null && tool.StatusArguments.Length > 0 && tool.StatusByExitCode)
            signedIn = await ExitCode(exe, tool.StatusArguments, 20) == 0 ? "yes" : "no";
        else if (tool != null && tool.StatusArguments.Length > 0)
        {
            string status = await Capture(exe, tool.StatusArguments, 20);
            try
            {
                using var document = JsonDocument.Parse(status);
                if (document.RootElement.TryGetProperty("loggedIn", out var logged)) signedIn = logged.GetBoolean() ? "yes" : "no";
            }
            catch (JsonException) { signedIn = status.Contains("logged in", StringComparison.OrdinalIgnoreCase) && !status.Contains("not logged", StringComparison.OrdinalIgnoreCase) ? "yes" : "unknown"; }
        }
        assistantSignedIn = signedIn;
        return new(exe, version, signedIn);
    }

    static async Task<int> ExitCode(string exe, string[] arguments, int seconds)
    {
        try
        {
            var start = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in arguments) start.ArgumentList.Add(a);
            using var process = Process.Start(start); if (process == null) return -1;
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            try { await process.WaitForExitAsync(limit.Token); } catch (OperationCanceledException) { try { process.Kill(true); } catch (Exception) { } return -1; }
            return process.ExitCode;
        }
        catch (Exception) { return -1; }
    }
    static async Task<string> Capture(string exe, string[] arguments, int seconds)
    {
        try
        {
            var start = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in arguments) start.ArgumentList.Add(a);
            using var process = Process.Start(start); if (process == null) return "";
            var output = process.StandardOutput.ReadToEndAsync();
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            try { await process.WaitForExitAsync(limit.Token); } catch (OperationCanceledException) { try { process.Kill(true); } catch (Exception) { } }
            return await output;
        }
        catch (Exception) { return ""; }
    }

    /// <summary>Writes the config that points an assistant back at this server. A file rather than a command-line
    /// argument, because a command line is readable by anything else running as you and the token is in it.</summary>
    string WriteMcpConfig()
    {
        string file = Path.Combine(Path.GetTempPath(), "wysicraft-assistant-" + Guid.NewGuid().ToString("n")[..8] + ".json");
        File.WriteAllText(file, McpConfigJson());
        return file;
    }
    internal string McpConfigJson() => Json.Write(new
    {
        mcpServers = new Dictionary<string, object>
        {
            [McpServerKey] = new { type = "http", url = mcpUrl, headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + mcpToken } }
        }
    });

    /// <summary>The argument list for a run, with the placeholders filled in per argument so a prompt with spaces
    /// stays one argument.</summary>
    string Fill(string template, string prompt, string config) => template
        .Replace("{prompt}", prompt).Replace("{mcpConfig}", config).Replace("{url}", mcpUrl).Replace("{token}", mcpToken)
        .Replace("{tools}", string.Join(" ", Assistants.EditorTools.Select(t => "mcp__" + McpServerKey + "__" + t)))
        .Replace("{toolList}", string.Join(",", Assistants.EditorTools.Select(t => "\"" + t + "\"")));
    string[] AssistantArguments(bool headless, string prompt, string config)
    {
        var prefs = Prefs();
        string[] template;
        if (prefs.AssistantTool == Assistants.Custom)
            template = (headless ? prefs.AssistantCustomArguments : prefs.AssistantCustomChatArguments).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        else template = (Assistants.Find(prefs.AssistantTool) ?? throw new InvalidOperationException("No assistant is set up.")) is { } t ? (headless ? t.HeadlessArguments : t.ChatArguments) : [];
        return [.. template.Select(a => Fill(a, prompt, config))];
    }

    /// <summary>Everything a run needs on disk and in its environment for the chosen tool to find this server: a
    /// file in its working directory, environment variables, or an entry in its own config in the profile. Returns
    /// the working directory to run in; the caller deletes it afterwards.</summary>
    string PrepareRun(ProcessStartInfo start, string config)
    {
        var tool = Assistants.Find(Prefs().AssistantTool);
        string work = Path.Combine(Path.GetTempPath(), "wysicraft-run-" + Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(work);
        start.WorkingDirectory = work;
        if (tool == null) return work;
        if (tool.CwdFile.Length > 0)
        {
            string file = Path.Combine(work, tool.CwdFile);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, Fill(tool.CwdFileBody, "", config));
        }
        foreach (var (name, value) in tool.Environment)
        {
            string filled = Fill(value, "", config);
            if (value.Contains("{opencodeConfig}"))
            {
                // opencode reads one config file named by OPENCODE_CONFIG; this one carries only the server.
                string file = Path.Combine(work, "opencode.json");
                File.WriteAllText(file, Json.Write(new { mcp = new Dictionary<string, object> { [McpServerKey] = new { type = "remote", url = mcpUrl, enabled = true, headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + mcpToken } } } }));
                filled = file;
            }
            start.Environment[name] = filled;
        }
        if (tool.HomeFile.Length > 0) MergeHomeEntry(tool);
        return work;
    }
    /// <summary>For a tool that only reads its own config in the profile: the server entry goes in beside whatever
    /// is already there, under mcpServers.arcadia-studio, and nothing else in the file is touched, except the entry this
    /// app wrote under its old name (mcpServers.wysicraft, pointing at this computer), which is taken out so the editor
    /// isn't listed twice.</summary>
    /// <summary>The name assistants know the editor's MCP server by: the key in every config the app hands out, and so
    /// the prefix on its tools (mcp__arcadia-studio__get_project). It was "wysicraft" before the rename.</summary>
    internal const string McpServerKey = "arcadia-studio", OldMcpServerKey = "wysicraft";
    /// <summary>An entry this app wrote under the old name: its address is this computer's local server.</summary>
    static bool IsOurOldEntry(System.Text.Json.Nodes.JsonNode? entry)
    {
        string address = (entry?["url"] ?? entry?["httpUrl"])?.ToString() ?? "";
        return address.StartsWith("http://127.0.0.1:", StringComparison.Ordinal) && address.EndsWith("/mcp", StringComparison.Ordinal);
    }
    void MergeHomeEntry(AssistantTool tool)
    {
        string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), tool.HomeFile.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        System.Text.Json.Nodes.JsonObject root;
        try { root = File.Exists(file) ? System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file)) as System.Text.Json.Nodes.JsonObject ?? new() : new(); }
        catch (JsonException) { root = new(); }
        if (root["mcpServers"] is not System.Text.Json.Nodes.JsonObject servers) root["mcpServers"] = servers = new();
        servers[McpServerKey] = System.Text.Json.Nodes.JsonNode.Parse(Fill(tool.HomeFileEntry, "", ""));
        if (IsOurOldEntry(servers[OldMcpServerKey])) servers.Remove(OldMcpServerKey);
        File.WriteAllText(file, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>One headless run: the assistant connects back, does the work, answers, exits. Returns what it said,
    /// or why it could not — never throws, because a button press must not take the editor down.</summary>
    internal async Task<AgentRun> RunAgentAsync(string prompt, CancellationToken cancellationToken = default) => await RunAgentAsync(prompt, null, cancellationToken);
    /// <summary>The same, reporting what the assistant is doing as it goes: each tool it calls, and the time so
    /// far for tools that stream nothing. A run is one process per request — it starts, works, answers and
    /// exits, and the next request starts a fresh one; nothing stays running in between.</summary>
    internal async Task<AgentRun> RunAgentAsync(string prompt, Action<string>? progress, CancellationToken cancellationToken = default)
    {
        var exe = AgentCommand();
        if (exe == null) return new(false, "", "No assistant is set up. Open Set up under the MCP panel.");
        if (mcpUrl.Length == 0) return new(false, "", "Start the MCP server first: the assistant connects back through it.");
        string config = "", work = "";
        try
        {
            config = WriteMcpConfig();
            var start = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                UseShellExecute = false, CreateNoWindow = true,
                // The tools print UTF-8; read it as such or every dash and multiplication sign comes out mangled.
                StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8
            };
            work = PrepareRun(start, config);
            foreach (var argument in AssistantArguments(true, prompt, config)) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("The assistant would not start.");
            process.StandardInput.Close();
            var started = System.Diagnostics.Stopwatch.StartNew();
            var lines = new System.Text.StringBuilder();
            int calls = 0;
            // Read as it comes, so a tool that streams can say what it is doing and one that does not still shows time passing.
            var reading = Task.Run(async () =>
            {
                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync(cancellationToken)) != null)
                {
                    lines.AppendLine(line);
                    var m = System.Text.RegularExpressions.Regex.Match(line, "\"type\":\"tool_use\".{0,200}?\"name\":\"(?:mcp__(?:arcadia-studio|wysicraft)__)?([A-Za-z_]+)\"");
                    if (m.Success) { calls++; progress?.Invoke($"Using {m.Groups[1].Value}… ({calls} tool call{(calls == 1 ? "" : "s")}, {started.Elapsed.TotalSeconds:0}s)"); }
                }
            }, cancellationToken);
            var errors = process.StandardError.ReadToEndAsync(cancellationToken);
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(TimeSpan.FromMinutes(5));
            while (!process.HasExited)
            {
                try { await process.WaitForExitAsync(new CancellationTokenSource(2000).Token); }
                catch (OperationCanceledException) { }
                if (limit.IsCancellationRequested) { try { process.Kill(true); } catch (Exception) { } return new(false, "", "The assistant took longer than five minutes, so it was stopped."); }
                if (!process.HasExited && calls == 0) progress?.Invoke($"Working… ({started.Elapsed.TotalSeconds:0}s)");
            }
            await reading;
            return ReadRun(lines.ToString(), await errors);
        }
        catch (Exception ex) { return new(false, "", ex.Message); }
        finally
        {
            if (config.Length > 0) try { File.Delete(config); } catch (Exception) { }
            if (work.Length > 0) try { Directory.Delete(work, true); } catch (Exception) { }
        }
    }

    /// <summary>The tool's output as an answer or a reason. JSON with a result field is read; anything else is
    /// taken as the answer itself, since a custom tool may just print.</summary>
    static AgentRun ReadRun(string output, string errors)
    {
        output = output.Trim();
        if (output.Length == 0) return new(false, "", errors.Trim().Length > 0 ? errors.Trim() : "The assistant said nothing.");
        // JSON lines (Codex, Copilot): the last object that carries text is the answer.
        if (output.Contains('\n') && output.Split('\n').All(l => l.Trim().Length == 0 || l.TrimStart().StartsWith('{')))
        {
            string last = "";
            foreach (var line in output.Split('\n').Where(l => l.TrimStart().StartsWith('{')))
                try { using var d = JsonDocument.Parse(line); foreach (var key in new[] { "result", "response", "text", "content", "message" }) if (d.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && v.GetString()!.Length > 0) last = v.GetString()!; }
                catch (JsonException) { }
            if (last.Length > 0) return new(true, last, "");
        }
        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            string text = root.TryGetProperty("result", out var result) ? result.GetString() ?? ""
                        : root.TryGetProperty("response", out var response) ? response.GetString() ?? "" : output;
            bool failed = root.TryGetProperty("is_error", out var bad) && bad.ValueKind == JsonValueKind.True;
            if (!failed) return new(true, text, "");
            if (text.Contains("authenticate", StringComparison.OrdinalIgnoreCase) || text.Contains("401"))
                return new(false, "", "The assistant is not signed in. Use Sign in under Set up, then try again.");
            return new(false, "", text.Length > 0 ? text : "The assistant reported an error.");
        }
        catch (JsonException) { return new(true, output, ""); }
    }

    // ---- The chat session: the tool in its own terminal, with Wysicraft attached ----
    Process? assistantSession; string assistantSessionConfig = "", assistantSessionWork = "";
    internal bool AssistantSessionRunning => assistantSession != null && !assistantSession.HasExited;

    /// <summary>Opens a terminal running the assistant with this server attached. PowerShell is used directly so the
    /// process handle is the console that hosts the tool, and Stop can end it: Windows Terminal hands off to an
    /// existing window and returns at once, which leaves nothing to stop.</summary>
    internal void StartAssistantSession()
    {
        if (AssistantSessionRunning) return;
        var exe = AgentCommand() ?? throw new InvalidOperationException("No assistant is set up.");
        if (mcpUrl.Length == 0) throw new InvalidOperationException("Start the MCP server first.");
        assistantSessionConfig = WriteMcpConfig();
        var prepared = new ProcessStartInfo(exe);
        string work = PrepareRun(prepared, assistantSessionConfig);
        assistantSessionWork = work;
        // Environment for the tool travels through the PowerShell that hosts it.
        var wanted = Assistants.Find(Prefs().AssistantTool)?.Environment.Keys.ToHashSet() ?? [];
        string environment = string.Join(" ", prepared.Environment.Where(e => wanted.Contains(e.Key)).Select(e => "$env:" + e.Key + "=" + Quote(e.Value ?? "") + ";"));
        string command = environment + " & " + Quote(exe) + " " + string.Join(" ", AssistantArguments(false, "", assistantSessionConfig).Select(Quote));
        assistantSession = Process.Start(ConsoleWindow(command, work));
        Log("Assistant started in its own window, with this project's MCP server attached.");
    }
    /// <summary>PowerShell in a classic console window (conhost) that this editor owns. Opened the ordinary way, Windows
    /// 11 hands a new console to Windows Terminal, and the process the editor started can end in the hand-over (or
    /// Terminal opens its Settings instead): the session then looked stopped two seconds after Start. The console
    /// window lives exactly as long as the session, and Stop closes it with everything inside.</summary>
    static ProcessStartInfo ConsoleWindow(string command, string? workingDirectory)
    {
        string conhost = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "conhost.exe");
        var start = File.Exists(conhost) ? new ProcessStartInfo(conhost) : new ProcessStartInfo("powershell.exe");
        start.UseShellExecute = true;
        if (workingDirectory != null) start.WorkingDirectory = workingDirectory;
        if (File.Exists(conhost)) start.ArgumentList.Add("powershell.exe");
        foreach (var a in new[] { "-NoExit", "-ExecutionPolicy", "Bypass", "-Command", command }) start.ArgumentList.Add(a);
        return start;
    }
    internal void StopAssistantSession()
    {
        var session = assistantSession; assistantSession = null;
        if (session != null && !session.HasExited) { try { session.Kill(true); } catch (Exception) { } Log("Assistant session stopped."); }
        if (assistantSessionConfig.Length > 0) { try { File.Delete(assistantSessionConfig); } catch (Exception) { } assistantSessionConfig = ""; }
        if (assistantSessionWork.Length > 0) { try { Directory.Delete(assistantSessionWork, true); } catch (Exception) { } assistantSessionWork = ""; }
    }
    /// <summary>Runs the tool's sign-in in a terminal; the browser does the rest, on the person's own account.</summary>
    internal void SignInAssistant()
    {
        var exe = AgentCommand() ?? throw new InvalidOperationException("Install the assistant first.");
        var tool = Assistants.Find(Prefs().AssistantTool);
        if (tool == null) throw new InvalidOperationException("A custom tool signs in the way its own documentation says.");
        // No login command means the tool signs you in the first time it runs, so it is simply started.
        string command = "& " + Quote(exe) + (tool.LoginArguments.Length > 0 ? " " + string.Join(" ", tool.LoginArguments.Select(Quote)) : "");
        Process.Start(ConsoleWindow(command, null));
    }
    /// <summary>The command a person would paste to start their assistant with Arcadia Studio attached, for the manual route.</summary>
    internal string AssistantChatCommand()
    {
        var exe = AgentCommand() ?? "<assistant>";
        string config = Wysicraft.Core.AppFolders.Path("mcp.json");
        return "& " + Quote(exe) + " " + string.Join(" ", AssistantArguments(false, "", config).Select(Quote));
    }
    static string Quote(string s) => s.Contains(' ') || s.Contains('"') ? "'" + s.Replace("'", "''") + "'" : s;
}
