using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Wysicraft.Models;
namespace Wysicraft.Packaging;

/// <summary>A game's details as they are on Arcadia (game.json's shape), which may have been edited on the website
/// since Arcadia Studio last published: title, description, genres, controls, phones, videos, cover, screenshots and
/// the leaderboard setup.</summary>
public sealed class ArcadiaGameDetails
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public List<string>? Genre { get; set; }
    public string? Controls { get; set; }
    public bool? Mobile { get; set; }
    public List<string>? Videos { get; set; }
    public ArcadiaMedia? Cover { get; set; }
    public List<ArcadiaMedia>? Screenshots { get; set; }
    public JsonObject? Scores { get; set; }
}
/// <summary>A picture on Arcadia: where to download it, and its git blob hash (so an unchanged one isn't downloaded).</summary>
public sealed class ArcadiaMedia { public string Url { get; set; } = ""; public string Sha { get; set; } = ""; }

/// <summary>The details block of a check, an upload or a game. On a check: whether the upload's details clash with
/// details edited on Arcadia (Conflict, Changed, who edited them and when, and Arcadia's values). On an upload: whose
/// details were used ("app", or "arcade" with the Kept fields).</summary>
public sealed class ArcadiaDetails
{
    public bool Conflict { get; set; }
    public List<string> Changed { get; set; } = [];
    public long? EditedAt { get; set; }
    public string? EditedBy { get; set; }
    public ArcadiaGameDetails? Arcade { get; set; }
    public string? Used { get; set; }
    public List<string> Kept { get; set; } = [];
}

public static class ArcadiaDetailsMerge
{
    /// <summary>Field names as the arcade sends them, in the words the app shows.</summary>
    public static string Name(string field) => field switch
    {
        "title" => "Title", "description" => "Description", "genre" => "Genres", "controls" => "Controls", "mobile" => "Phones and tablets",
        "videos" => "Videos", "cover" => "Cover", "screenshots" => "Screenshots", "scores" => "Leaderboard", _ => field
    };
    public static string Names(IEnumerable<string> fields) => string.Join(", ", fields.Select(Name));

    /// <summary>git's blob hash of a file (sha1 of "blob {length}\0" and the bytes), as the arcade lists pictures.</summary>
    public static string GitSha(byte[] bytes)
    {
        var header = Encoding.ASCII.GetBytes("blob " + bytes.Length.ToString(CultureInfo.InvariantCulture) + "\0");
        var all = new byte[header.Length + bytes.Length]; header.CopyTo(all, 0); bytes.CopyTo(all, header.Length);
        return Convert.ToHexString(SHA1.HashData(all)).ToLowerInvariant();
    }

    /// <summary>The project's publishing settings with Arcadia's details in place of its own, so the next publish sends
    /// what's on Arcadia and isn't asked again. Only fields Arcadia sent are taken. Pictures whose hash matches one the
    /// project already has are reused; the rest are fetched (fetch is given the picture and returns its bytes).</summary>
    public static async Task<PublishSettings> Merge(PublishSettings current, ArcadiaGameDetails arcade, Func<ArcadiaMedia, Task<byte[]>> fetch)
    {
        var s = Json.Clone(current); s.KeepImages(current);
        if (arcade.Title != null) s.Title = arcade.Title;
        if (arcade.Description != null) s.Description = arcade.Description;
        if (arcade.Genre != null) s.Genre = arcade.Genre.Where(g => g.Trim().Length > 0).Take(3).ToList();
        if (arcade.Controls != null) s.Controls = arcade.Controls;
        if (arcade.Mobile is bool mobile) s.Mobile = mobile;
        if (arcade.Videos != null) s.Videos = arcade.Videos.Where(v => v.Trim().Length > 0).Take(3).ToList();

        // Pictures the project already has, by hash.
        var have = new Dictionary<string, (byte[] Bytes, string Type)>(StringComparer.OrdinalIgnoreCase);
        if (current.Cover.Length > 0) have[GitSha(current.Cover)] = (current.Cover, current.CoverType);
        foreach (var shot in current.Screenshots) if (shot.Bytes.Length > 0) have[GitSha(shot.Bytes)] = (shot.Bytes, shot.Type);
        async Task<(byte[] Bytes, string Type)> Picture(ArcadiaMedia media)
        {
            if (media.Sha.Length > 0 && have.TryGetValue(media.Sha, out var known)) return known;
            var bytes = await fetch(media);
            string type = ArcadiaPackage.ImageType(bytes) ?? throw new InvalidDataException("A picture from Arcadia isn't a PNG, JPEG or WebP: " + media.Url);
            return (bytes, type);
        }
        if (arcade.Cover != null) { var (bytes, type) = await Picture(arcade.Cover); s.Cover = bytes; s.CoverType = type; }
        if (arcade.Screenshots != null)
        {
            var shots = new List<PublishImage>();
            foreach (var media in arcade.Screenshots.Take(8)) { var (bytes, type) = await Picture(media); shots.Add(new PublishImage { Type = type, Bytes = bytes }); }
            s.Screenshots = shots;
        }
        if (arcade.Scores != null) { s.Leaderboard = true; s.Scores = ScoresFrom(arcade.Scores, current.Scores); }
        return s;
    }

    /// <summary>A leaderboard setup in game.json's shape (the one GameJson writes) as the project's settings. Anything
    /// missing keeps what the project had.</summary>
    public static PublishScores ScoresFrom(JsonObject o, PublishScores was)
    {
        string Text(JsonNode? n, string fallback) => n is JsonValue v && v.TryGetValue<string>(out var t) ? t : fallback;
        double? Num(JsonNode? n) => n is JsonValue v && (v.TryGetValue<double>(out var d) || (v.TryGetValue<string>(out var t) && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d))) ? d : null;
        PublishWatch Watch(JsonNode? n) => n is JsonObject w ? new PublishWatch { Variable = Text(w["variable"], ""), Path = Text(w["path"], "") } : new PublishWatch();
        var c = new PublishScores
        {
            Label = Text(o["label"], was.Label), Format = Text(o["format"], was.Format), Order = Text(o["order"], was.Order), Aggregate = Text(o["aggregate"], was.Aggregate),
            Min = Num(o["min"]) ?? was.Min, Max = o.ContainsKey("max") ? Num(o["max"]) : was.Max, MinSeconds = Num(o["minSeconds"]) ?? was.MinSeconds,
            Score = was.Score, Triggers = was.Triggers, Stats = was.Stats, Round = was.Round
        };
        var watch = o["watch"] as JsonObject;
        if (watch != null)
        {
            if (watch["score"] is JsonObject) c.Score = Watch(watch["score"]);
            if (watch["trigger"] is JsonArray triggers) c.Triggers = triggers.OfType<JsonObject>().Select(t => new PublishTrigger { Variable = Text(t["variable"], ""), Path = Text(t["path"], ""), EqualsValue = t["equals"]?.DeepClone() is JsonNode e ? Text(e, e.ToJsonString()) : null }).ToList();
            c.Round = Text(watch["round"], c.Round);
        }
        if (o["stats"] is JsonArray stats)
        {
            var watched = (watch?["stats"] as JsonArray)?.OfType<JsonObject>().ToDictionary(w => Text(w["key"], ""), w => w) ?? [];
            c.Stats = stats.OfType<JsonObject>().Select(st =>
            {
                string key = Text(st["key"], "");
                var w = watched.GetValueOrDefault(key);
                return new PublishStat { Key = key, Label = Text(st["label"], ""), Format = Text(st["format"], "number"), Aggregate = Text(st["aggregate"], "max"), Check = st["check"] is not JsonValue chk || !chk.TryGetValue<bool>(out var b) || b, Variable = w != null ? Text(w["variable"], "") : "", Path = w != null ? Text(w["path"], "") : "" };
            }).ToList();
        }
        return c;
    }
}
