using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Wysicraft.Packaging;

/// <summary>An error from the arcade. Message is the arcade's own text, shown to the person as it is; Code and
/// Status say what kind (bad-key, not-creator, removed, limit, invalid…).</summary>
public sealed class ArcadiaException(int status, string code, string message, List<ArcadiaFinding>? findings = null, long? retryAt = null, string? gameId = null) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public List<ArcadiaFinding> Findings { get; } = findings ?? [];
    public long? RetryAt { get; } = retryAt;
    /// <summary>409 title-taken: the account's other game that already has this title.</summary>
    public string? GameId { get; } = gameId;
    /// <summary>The saved game can't be updated: deleted by its creator, removed by a moderator, or not this account's
    /// (any 404). The person decides whether to publish the project as a new game.</summary>
    public bool GameGone => Code is "deleted" or "removed" or "not-found" || Status == 404;
}

public sealed class ArcadiaLinkStart
{
    public string DeviceCode { get; set; } = "";
    public string UserCode { get; set; } = "";
    public string VerificationUri { get; set; } = "";
    public string VerificationUriComplete { get; set; } = "";
    public int Interval { get; set; } = 5;
    public int ExpiresIn { get; set; } = 900;
}
public enum ArcadiaLinkState { Pending, SlowDown, Denied, Expired, Approved }
public sealed record ArcadiaLinkPoll(ArcadiaLinkState State, int Interval = 0, string Key = "", ArcadiaCreator? Creator = null);
public sealed class ArcadiaCreator
{
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Url { get; set; } = "";
    public string Status { get; set; } = "";
    public bool Trusted { get; set; }
    public bool AutoPublish { get; set; }
}
public sealed class ArcadiaLimit
{
    public int Available { get; set; }
    public int Max { get; set; }
    public long? NextAt { get; set; }
    public bool Unlimited { get; set; }
    public bool CanUpload => Unlimited || Available > 0;
}
public sealed class ArcadiaMe
{
    public ArcadiaCreator Creator { get; set; } = new();
    public ArcadiaDevice? Device { get; set; }
    public ArcadiaPublishing Publishing { get; set; } = new();
    public ArcadiaLimits Limits { get; set; } = new();
    public List<ArcadiaGame> Games { get; set; } = [];
}
public sealed class ArcadiaDevice { public long Id { get; set; } public string Name { get; set; } = ""; }
public sealed class ArcadiaPublishing { public bool Enabled { get; set; } = true; public string? Reason { get; set; } public int MaxUploadMb { get; set; } = ArcadiaPackage.DefaultMaxUploadMb; }
public sealed class ArcadiaLimits { public ArcadiaLimit NewGame { get; set; } = new(); }
public sealed class ArcadiaGame
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public string Url { get; set; } = "";
    public ArcadiaLive? Live { get; set; }
    public ArcadiaSubmission? Latest { get; set; }
    public ArcadiaLimit Limits { get; set; } = new();
    public List<ArcadiaSubmission> Submissions { get; set; } = [];
    /// <summary>The game's details as they are on Arcadia now, and when and by whom (creator, mod) they were last
    /// edited on the website. Some arcades send these inside Details instead; ArcadeDetails reads either.</summary>
    public ArcadiaGameDetails? Arcade { get; set; }
    public long? EditedAt { get; set; }
    public string? EditedBy { get; set; }
    public ArcadiaDetails? Details { get; set; }
    public ArcadiaGameDetails? ArcadeDetails => Arcade ?? Details?.Arcade;
}
public sealed class ArcadiaLive { public string Version { get; set; } = ""; public int Number { get; set; } }
public sealed class ArcadiaSubmission
{
    public long Id { get; set; }
    public string GameId { get; set; } = "";
    public int Number { get; set; }
    public string Version { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Message { get; set; }
    public List<ArcadiaFinding> Findings { get; set; } = [];
    public long CreatedAt { get; set; }
    public long? ReviewedAt { get; set; }
    public string GameUrl { get; set; } = "";
    public bool Downloadable { get; set; }
}
public sealed class ArcadiaUpload
{
    public ArcadiaGameRef Game { get; set; } = new();
    public ArcadiaSubmission Submission { get; set; } = new();
    /// <summary>Whose details were used: "app", or "arcade" with the fields it Kept.</summary>
    public ArcadiaDetails? Details { get; set; }
}
public sealed class ArcadiaGameRef { public string Id { get; set; } = ""; public string Title { get; set; } = ""; public string Url { get; set; } = ""; }
public sealed class ArcadiaCheck
{
    public bool Ok { get; set; }
    public bool WouldHold { get; set; }
    public bool Refused { get; set; }
    public List<ArcadiaFinding> Findings { get; set; } = [];
    /// <summary>On an update: whether the details clash with ones edited on Arcadia since the last publish.</summary>
    public ArcadiaDetails? Details { get; set; }
}

/// <summary>Talks to an Arcadia server's publishing API (docs/ARCADIA.md). Always HTTPS, except to this computer
/// (localhost) for testing. The key goes only in the Authorization header: never in a URL, a log or an error.</summary>
public sealed class ArcadiaClient
{
    static readonly HttpClient Http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { Timeout = Timeout.InfiniteTimeSpan };
    // Lenient: a number sent as text, a decimal where a whole number was expected, or null where a value was, is read
    // rather than refusing the whole answer. Real arcades don't always match the spec's examples to the letter.
    internal static readonly JsonSerializerOptions Read = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, Converters = { new LenientInt(), new LenientLong(), new LenientDouble(), new LenientBool(), new LenientString() } };
    static readonly TimeSpan Short = TimeSpan.FromSeconds(30), Long = TimeSpan.FromMinutes(2);
    readonly HttpClient http;
    readonly string userAgent;
    public Uri Arcade { get; }
    /// <summary>The arcade's host (with a port when it isn't the usual one): the key and game IDs are kept per host.</summary>
    public string Host => Arcade.IsDefaultPort ? Arcade.Host : Arcade.Host + ":" + Arcade.Port;

    public ArcadiaClient(string arcadeUrl, string wysicraftVersion, HttpClient? client = null)
    {
        Arcade = Normalize(arcadeUrl);
        http = client ?? Http; userAgent = "Wysicraft/" + wysicraftVersion;
    }

    /// <summary>An arcade address as the client uses it: https (or http to this computer), no path, query or login.</summary>
    public static Uri Normalize(string arcadeUrl)
    {
        string text = arcadeUrl.Trim().TrimEnd('/');
        if (!text.Contains("://")) text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0) throw new ArgumentException("That isn't an arcade address. It looks like " + ArcadiaPackage.DefaultArcade + ".");
        bool local = uri.IsLoopback;
        if (uri.Scheme != Uri.UriSchemeHttps && !(local && uri.Scheme == Uri.UriSchemeHttp)) throw new ArgumentException("Arcade addresses start with https://.");
        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
    }

    // ---- Linking (once per device) ----
    public async Task<ArcadiaLinkStart> StartLinkAsync(string device, CancellationToken ct = default)
    {
        string name = device.Length > 60 ? device[..60] : device;
        var body = await SendAsync(HttpMethod.Post, "api/publish/link", null, Json(new JsonObject { ["device"] = name }), Short, null, ct);
        return Parse<ArcadiaLinkStart>(body);
    }

    /// <summary>One poll of a link code. Pending and SlowDown mean ask again (after Interval seconds for SlowDown).</summary>
    public async Task<ArcadiaLinkPoll> PollLinkAsync(string deviceCode, CancellationToken ct = default)
    {
        using var request = Request(HttpMethod.Post, "api/publish/link/token", null, Json(new JsonObject { ["deviceCode"] = deviceCode }));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(Short);
        using var response = await http.SendAsync(request, timeout.Token);
        string text = await response.Content.ReadAsStringAsync(timeout.Token);
        JsonNode? node = null; try { node = JsonNode.Parse(text); } catch (JsonException) { }
        if (response.IsSuccessStatusCode)
            return new(ArcadiaLinkState.Approved, Key: (string?)node?["key"] ?? "", Creator: node?["creator"]?.Deserialize<ArcadiaCreator>(Read));
        if ((int)response.StatusCode == 400)
            switch ((string?)node?["error"])
            {
                case "authorization_pending": return new(ArcadiaLinkState.Pending);
                case "slow_down": return new(ArcadiaLinkState.SlowDown, Interval: (int?)node?["interval"] ?? 10);
                case "access_denied": return new(ArcadiaLinkState.Denied);
                case "expired_token": return new(ArcadiaLinkState.Expired);
            }
        throw Error(response, node);
    }

    /// <summary>Ends this device's link on the arcade (the stored key stops working).</summary>
    public Task UnlinkAsync(string key, CancellationToken ct = default) => SendAsync(HttpMethod.Delete, "api/publish/key", key, null, Short, null, ct);

    // ---- The account ----
    public async Task<ArcadiaMe> MeAsync(string key, CancellationToken ct = default)
    {
        var me = Parse<ArcadiaMe>(await SendAsync(HttpMethod.Get, "api/publish/me", key, null, Short, null, ct));
        // Sections the arcade left out (or sent as null) read as empty rather than failing later.
        me.Creator ??= new(); me.Publishing ??= new(); me.Limits ??= new(); me.Limits.NewGame ??= new(); me.Games ??= [];
        foreach (var game in me.Games) { game.Limits ??= new(); game.Submissions ??= []; }
        return me;
    }

    // ---- Uploads ----
    /// <summary>A dry run: what the arcade would say about this package, as a new game or (with gameId) as a new version
    /// of that game. Nothing is stored and no upload is used up. A refusal (422) comes back as Refused with its findings
    /// rather than as an error.</summary>
    public async Task<ArcadiaCheck> CheckAsync(string key, byte[] zip, string? gameId = null, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        string path = "api/publish/check" + (string.IsNullOrEmpty(gameId) ? "" : "?game=" + Uri.EscapeDataString(gameId));
        try { return Parse<ArcadiaCheck>(await SendAsync(HttpMethod.Post, path, key, Zip(zip, progress), Long, null, ct)); }
        catch (ArcadiaException ex) when (ex.Status == 422) { return new ArcadiaCheck { Ok = false, Refused = true, Findings = ex.Findings }; }
    }

    /// <summary>Uploads a package: a new game when gameId is empty, otherwise a new version of that game. overwrite
    /// matters only when the check found a clash with details edited on Arcadia: true sends this package's details in
    /// their place, false (or null) keeps Arcadia's. The game's files are updated either way.</summary>
    public async Task<ArcadiaUpload> UploadAsync(string key, string? gameId, byte[] zip, IProgress<double>? progress = null, CancellationToken ct = default, bool? overwrite = null)
    {
        string path = string.IsNullOrEmpty(gameId) ? "api/publish/games" : "api/publish/games/" + Uri.EscapeDataString(gameId) + (overwrite is bool o ? "?overwrite=" + (o ? "true" : "false") : "");
        return Parse<ArcadiaUpload>(await SendAsync(HttpMethod.Post, path, key, Zip(zip, progress), Long, null, ct));
    }

    public async Task<ArcadiaSubmission> SubmissionAsync(string key, long id, CancellationToken ct = default) =>
        Parse<ArcadiaSubmission>(await SendAsync(HttpMethod.Get, "api/publish/submissions/" + id, key, null, Short, null, ct));

    /// <summary>A game with its last 20 uploads, newest first.</summary>
    public async Task<ArcadiaGame> GameAsync(string key, string gameId, CancellationToken ct = default) =>
        Parse<ArcadiaGame>(await SendAsync(HttpMethod.Get, "api/publish/games/" + Uri.EscapeDataString(gameId), key, null, Short, null, ct));

    /// <summary>A published version as the zip the arcade stored (the live one, or a given upload), and its file name.</summary>
    public async Task<(byte[] Zip, string FileName)> DownloadAsync(string key, string gameId, long? submission = null, CancellationToken ct = default)
    {
        string path = "api/publish/games/" + Uri.EscapeDataString(gameId) + "/download" + (submission is long s ? "?submission=" + s : "");
        string? name = null;
        var bytes = await SendBytesAsync(path, key, r => name = r.Content.Headers.ContentDisposition?.FileNameStar ?? r.Content.Headers.ContentDisposition?.FileName?.Trim('"'), ct);
        return (bytes, string.IsNullOrWhiteSpace(name) ? gameId + ".zip" : Path.GetFileName(name));
    }

    /// <summary>A picture of a game on this arcade (a cover or screenshot URL from its details). Only this arcade's own
    /// address is fetched, so the key never goes anywhere else.</summary>
    public async Task<byte[]> MediaAsync(string key, string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Arcade.Scheme || !string.Equals(uri.Host, Arcade.Host, StringComparison.OrdinalIgnoreCase) || uri.Port != Arcade.Port)
            throw new ArcadiaException(0, "media", "A picture on Arcadia has an address outside the arcade, so it wasn't downloaded: " + url);
        using var request = Request(HttpMethod.Get, uri.PathAndQuery.TrimStart('/'), key, null);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(Long);
        using var response = await http.SendAsync(request, timeout.Token);
        if (!response.IsSuccessStatusCode) throw new ArcadiaException((int)response.StatusCode, "media", "Arcadia didn't send a picture (" + (int)response.StatusCode + "): " + url);
        var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token);
        if (bytes.Length > 8 * 1024 * 1024) throw new ArcadiaException(0, "media", "A picture on Arcadia is larger than expected: " + url);
        return bytes;
    }

    // ---- Plumbing ----
    HttpRequestMessage Request(HttpMethod method, string path, string? key, HttpContent? content)
    {
        var request = new HttpRequestMessage(method, new Uri(Arcade, path)) { Content = content };
        request.Headers.UserAgent.ParseAdd(userAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (key != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
    }
    async Task<string> SendAsync(HttpMethod method, string path, string? key, HttpContent? content, TimeSpan limit, Action<HttpResponseMessage>? seen, CancellationToken ct)
    {
        using var request = Request(method, path, key, content);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(limit);
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, timeout.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ArcadiaException(0, "timeout", "The arcade didn't answer in time. Check your connection and try again."); }
        catch (HttpRequestException ex) { throw new ArcadiaException(0, "offline", "Couldn't reach " + Host + ": " + ex.Message); }
        using (response)
        {
            seen?.Invoke(response);
            string text = await response.Content.ReadAsStringAsync(timeout.Token);
            if (response.IsSuccessStatusCode) return text;
            JsonNode? node = null; try { node = JsonNode.Parse(text); } catch (JsonException) { }
            throw Error(response, node);
        }
    }
    async Task<byte[]> SendBytesAsync(string path, string key, Action<HttpResponseMessage> seen, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Get, path, key, null);
        request.Headers.Accept.Clear(); request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/zip"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(Long);
        using var response = await http.SendAsync(request, timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            JsonNode? node = null; try { node = JsonNode.Parse(await response.Content.ReadAsStringAsync(timeout.Token)); } catch (JsonException) { }
            throw Error(response, node);
        }
        seen(response);
        return await response.Content.ReadAsByteArrayAsync(timeout.Token);
    }
    ArcadiaException Error(HttpResponseMessage response, JsonNode? node)
    {
        int status = (int)response.StatusCode;
        string code = (string?)node?["code"] ?? "";
        string? message = (string?)node?["error"];
        if (string.IsNullOrWhiteSpace(message))
            message = status switch
            {
                413 => "The game is too large for this arcade.",
                415 => "The arcade didn't accept the upload's format.",
                401 => "Arcadia Studio isn't linked to Arcadia anymore.",
                _ => $"The arcade answered {status} {response.ReasonPhrase}."
            };
        List<ArcadiaFinding>? findings = null;
        try { findings = node?["findings"]?.Deserialize<List<ArcadiaFinding>>(Read); } catch (JsonException) { }
        long? retryAt = null; try { retryAt = (long?)node?["retryAt"]; } catch (Exception) { }
        string? gameId = null; try { gameId = node?["gameId"]?.ToString(); } catch (Exception) { }
        return new ArcadiaException(status, code, message, findings, retryAt, gameId);
    }
    static StringContent Json(JsonObject body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");
    static HttpContent Zip(byte[] zip, IProgress<double>? progress)
    {
        var content = new ProgressContent(zip, progress);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        return content;
    }
    static T Parse<T>(string text) where T : new()
    {
        if (string.IsNullOrWhiteSpace(text)) return new T();
        try { return JsonSerializer.Deserialize<T>(text, Read) ?? new T(); }
        catch (JsonException ex) { throw new ArcadiaException(0, "bad-answer", "The arcade sent an answer Arcadia Studio doesn't understand" + (string.IsNullOrEmpty(ex.Path) ? "." : " (at " + ex.Path + ").")); }
    }

    // ---- Lenient readers ----
    sealed class LenientInt : System.Text.Json.Serialization.JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) => (int)Math.Clamp(Math.Floor(LenientDouble.Value(ref r)), int.MinValue, int.MaxValue);
        public override void Write(Utf8JsonWriter w, int v, JsonSerializerOptions o) => w.WriteNumberValue(v);
    }
    sealed class LenientLong : System.Text.Json.Serialization.JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
        {
            if (r.TokenType == JsonTokenType.Number && r.TryGetInt64(out long whole)) return whole;
            if (r.TokenType == JsonTokenType.String && long.TryParse(r.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long text)) return text;
            double v = LenientDouble.Value(ref r); return v >= long.MaxValue ? long.MaxValue : v <= long.MinValue ? long.MinValue : (long)Math.Floor(v);
        }
        public override void Write(Utf8JsonWriter w, long v, JsonSerializerOptions o) => w.WriteNumberValue(v);
    }
    sealed class LenientDouble : System.Text.Json.Serialization.JsonConverter<double>
    {
        internal static double Value(ref Utf8JsonReader r)
        {
            switch (r.TokenType)
            {
                case JsonTokenType.Number: return r.GetDouble();
                case JsonTokenType.String: return double.TryParse(r.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
                case JsonTokenType.True: return 1;
                case JsonTokenType.StartObject: case JsonTokenType.StartArray: r.Skip(); return 0;
                default: return 0;
            }
        }
        public override double Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) => Value(ref r);
        public override void Write(Utf8JsonWriter w, double v, JsonSerializerOptions o) => w.WriteNumberValue(v);
    }
    sealed class LenientBool : System.Text.Json.Serialization.JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) => r.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.Number => r.GetDouble() != 0,
            JsonTokenType.String => r.GetString() is "true" or "1" or "yes" or "True",
            JsonTokenType.StartObject or JsonTokenType.StartArray => Skip(ref r),
            _ => false
        };
        static bool Skip(ref Utf8JsonReader r) { r.Skip(); return false; }
        public override void Write(Utf8JsonWriter w, bool v, JsonSerializerOptions o) => w.WriteBooleanValue(v);
    }
    sealed class LenientString : System.Text.Json.Serialization.JsonConverter<string>
    {
        public override bool HandleNull => false;
        public override string? Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) => r.TokenType switch
        {
            JsonTokenType.String => r.GetString(),
            JsonTokenType.Number => Encoding.UTF8.GetString(r.HasValueSequence ? r.ValueSequence.ToArray() : r.ValueSpan.ToArray()),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.StartObject or JsonTokenType.StartArray => JsonDocument.ParseValue(ref r).RootElement.GetRawText(),
            _ => null
        };
        public override void Write(Utf8JsonWriter w, string v, JsonSerializerOptions o) => w.WriteStringValue(v);
    }

    /// <summary>The zip, sent in pieces so the dialog can show how much has gone.</summary>
    sealed class ProgressContent(byte[] bytes, IProgress<double>? progress) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            const int piece = 64 * 1024;
            for (int sent = 0; sent < bytes.Length; sent += piece)
            {
                await stream.WriteAsync(bytes.AsMemory(sent, Math.Min(piece, bytes.Length - sent)));
                progress?.Report(Math.Min(1.0, (double)(sent + piece) / Math.Max(1, bytes.Length)));
            }
            if (bytes.Length == 0) progress?.Report(1);
        }
        protected override bool TryComputeLength(out long length) { length = bytes.Length; return true; }
    }
}
