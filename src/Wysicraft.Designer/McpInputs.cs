using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wysicraft.Models;
namespace Wysicraft.Designer;

// The shapes of the settings an assistant gives the publish tools and answer_request. They were one string of JSON
// each, which a tool's schema can only describe as "a string"; as classes, the schema names every field and its type,
// so a client can see them and a wrong one is refused before the call runs. Every field is optional: one left out
// isn't changed. A call written the old way (the JSON as a string) still works: see McpTypedInputs.Accept.

/// <summary>arcadia_publish settings: only the fields given are changed.</summary>
public sealed class ArcadiaSettingsInput
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    [Description("Up to 3.")] public List<string>? Genre { get; set; }
    [Description("Like 1.0.0; raise it every publish.")] public string? Version { get; set; }
    [Description("How to play, up to 200 characters.")] public string? Controls { get; set; }
    [Description("Like 16:10; empty takes it from the screen.")] public string? AspectRatio { get; set; }
    [Description("Plays on phones and tablets: touch controls, and fits a small screen.")] public bool? Mobile { get; set; }
    [Description("Up to 3 YouTube links: youtube.com/watch?v=, youtu.be/ or youtube.com/shorts/.")] public List<string>? Videos { get; set; }
    [Description("Keep a leaderboard. false publishes the game with none, on purpose.")] public bool? Leaderboard { get; set; }
    [Description("The leaderboard, given whole. format: points|number|time (a time score is in milliseconds); order: desc|asc; aggregate: best|sum; round: floor|none. score and each stat read a screen variable or ctx.state name (path = a field inside one that holds JSON). The run has ended when any trigger holds; a trigger without equals means \"is true\". A stat's check:false is for one that doesn't grow over time.")]
    public PublishScores? Scores { get; set; }
    [Description("\"\" (the project's first leaderboard page, or the standard board), \"standard\", \"board:<id>\" or \"file\" (a page the person imported).")] public string? LeaderboardPage { get; set; }
}

/// <summary>itch_publish settings: only the fields given are changed.</summary>
public sealed class ItchSettingsInput
{
    [Description("The itch.io game: https://user.itch.io/game or user/game.")] public string? Target { get; set; }
    [Description("Upload the web version.")] public bool? Web { get; set; }
    [Description("Upload the Windows app.")] public bool? Windows { get; set; }
    public string? WebChannel { get; set; }
    public string? WindowsChannel { get; set; }
    [Description("A new channel starts hidden.")] public bool? Hidden { get; set; }
    [Description("Skip a push that changes nothing.")] public bool? IfChanged { get; set; }
}

/// <summary>answer_request: the answer to one Input creator request.</summary>
public sealed class AnswerRequestInput
{
    [Description("The request's id, from pending_requests.")] public string Id { get; set; } = "";
    [Description("A new lowercase name for the input: letters, digits and _.")] public string? InputName { get; set; }
    [Description("Client JavaScript for the screen's input_pressed event, which gets the input name as ctx.value.")] public string? Script { get; set; }
    [Description("The function in the script to call.")] public string? Function { get; set; }
    [Description("A sentence on what it does.")] public string? Notes { get; set; }
}

static class McpTypedInputs
{
    static readonly JsonSerializerOptions Given = new(Json.Options) { WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    /// <summary>The fields that were given, as the JSON the tools read ("" when there were none).</summary>
    public static string ToJson<T>(T? input) where T : class
    {
        if (input == null) return "";
        string text = JsonSerializer.Serialize(input, Given);
        return text == "{}" ? "" : text;
    }
    /// <summary>A list as JSON ("" when it wasn't given; an empty list is still "[]", which clears).</summary>
    public static string List(List<string>? items) => items == null ? "" : JsonSerializer.Serialize(items, Given);

    // Arguments that used to be a string of JSON, by tool. A call that still sends one has it read here, so nothing
    // written before the tools had typed inputs stops working.
    static readonly Dictionary<string, string[]> OnceStrings = new()
    {
        ["arcadia_publish"] = ["settings", "screenshots"],
        ["itch_publish"] = ["settings"],
        ["answer_request"] = ["payload"],
    };
    /// <summary>Turns a JSON string given for a typed argument into the object or list it holds. Returns the arguments
    /// to use: the same ones when nothing needed changing.</summary>
    public static IDictionary<string, JsonElement>? Accept(string tool, IDictionary<string, JsonElement>? arguments)
    {
        if (arguments == null || !OnceStrings.TryGetValue(tool, out var names)) return arguments;
        Dictionary<string, JsonElement>? changed = null;
        foreach (string name in names)
        {
            if (!arguments.TryGetValue(name, out var value) || value.ValueKind != JsonValueKind.String) continue;
            string text = value.GetString()!.Trim();
            changed ??= new Dictionary<string, JsonElement>(arguments);
            // An empty string meant "not given".
            if (text.Length == 0) { changed.Remove(name); continue; }
            try { using var parsed = JsonDocument.Parse(text); if (parsed.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array) changed[name] = parsed.RootElement.Clone(); }
            catch (JsonException) { } // not JSON: left as it is, and refused with the usual message
        }
        return changed ?? arguments;
    }
}
