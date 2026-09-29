using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Wysicraft.Packaging;

/// <summary>One file of a release: the installer (installed copies) or the ZIP (portable copies).</summary>
public sealed record UpdateAsset(string Name, string Url, long Size, string? Sha256, string? Sha256Url);

/// <summary>The newest release on GitHub, when it's newer than this copy.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string Name, string Notes, string Page, UpdateAsset? Installer, UpdateAsset? Zip);

/// <summary>Why a download was refused (wrong host, size or checksum); the file is deleted.</summary>
public sealed class UpdateException(string message) : Exception(message);

/// <summary>Checks GitHub Releases for a newer Arcadia Studio and downloads it safely: HTTPS only, GitHub hosts only,
/// and never used unless its SHA-256 matches (GitHub's own digest for the file, or a .sha256 file in the release).
/// The only thing sent is the app's name and version (GitHub requires a user agent).</summary>
public sealed class UpdateCheck
{
    public const string Repository = "kl3mta3/arcadia-studio";
    static readonly HttpClient Shared = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { Timeout = Timeout.InfiniteTimeSpan };
    readonly HttpClient http; readonly Uri api; readonly string userAgent; readonly bool allowLocal;
    public Version Current { get; }

    /// <param name="apiBase">Tests point this at a fake GitHub on this computer (then http and localhost are allowed).</param>
    public UpdateCheck(string currentVersion, HttpClient? client = null, string apiBase = "https://api.github.com/")
    {
        Current = ParseVersion(currentVersion) ?? new Version(0, 0);
        http = client ?? Shared; api = new Uri(apiBase.TrimEnd('/') + "/");
        allowLocal = api.IsLoopback;
        userAgent = "ArcadiaStudio/" + Current;
    }

    /// <summary>"v1.4.0", "1.4.0" or "V1.4" → 1.4.0; anything after the numbers ("-beta") makes it a pre-release: null.</summary>
    public static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        string t = tag.Trim(); if (t.StartsWith('v') || t.StartsWith('V')) t = t[1..];
        t = t.Split('+')[0];
        if (t.Contains('-')) return null;
        return Version.TryParse(t.Count(c => c == '.') == 0 ? t + ".0" : t, out var v) ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : null;
    }

    /// <summary>The newest published release if it's newer than this copy; null when up to date. Throws
    /// HttpRequestException / UpdateException when GitHub can't be asked (offline, rate limited…).</summary>
    public async Task<UpdateInfo?> LatestAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(api, "repos/" + Repository + "/releases/latest"));
        request.Headers.UserAgent.ParseAdd(userAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await http.SendAsync(request, timeout.Token);
        if ((int)response.StatusCode == 404) return null; // no releases yet
        if (!response.IsSuccessStatusCode) throw new UpdateException("GitHub answered " + (int)response.StatusCode + (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests ? " (too many checks from this network; try again later)." : "."));
        var release = JsonNode.Parse(await response.Content.ReadAsStringAsync(timeout.Token)) as JsonObject ?? throw new UpdateException("GitHub sent an answer Arcadia Studio doesn't understand.");
        if ((bool?)release["draft"] == true || (bool?)release["prerelease"] == true) return null;
        string tag = (string?)release["tag_name"] ?? "";
        var version = ParseVersion(tag);
        if (version == null || version <= Current) return null;
        var assets = (release["assets"] as JsonArray ?? []).OfType<JsonObject>().Select(a => (Name: (string?)a["name"] ?? "", Url: (string?)a["browser_download_url"] ?? "", Size: (long?)a["size"] ?? 0, Digest: (string?)a["digest"])).ToList();
        UpdateAsset? Pick(Func<string, bool> match)
        {
            var a = assets.FirstOrDefault(x => match(x.Name) && !x.Name.Contains("Installer-Test", StringComparison.OrdinalIgnoreCase));
            if (a.Name == null || a.Name.Length == 0) return null;
            string? sha = a.Digest is string d && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? d[7..].ToLowerInvariant() : null;
            string? shaUrl = assets.FirstOrDefault(x => x.Name.Equals(a.Name + ".sha256", StringComparison.OrdinalIgnoreCase)).Url;
            return new UpdateAsset(a.Name, a.Url, a.Size, sha, string.IsNullOrEmpty(shaUrl) ? null : shaUrl);
        }
        return new UpdateInfo(version, tag, (string?)release["name"] ?? tag, (string?)release["body"] ?? "", (string?)release["html_url"] ?? "https://github.com/" + Repository + "/releases/latest",
            Pick(n => n.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase)), Pick(n => n.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Only HTTPS addresses on GitHub (and, for tests, this computer).</summary>
    public bool Allowed(Uri uri)
    {
        if (allowLocal && uri.IsLoopback) return true;
        if (uri.Scheme != Uri.UriSchemeHttps) return false;
        string host = uri.Host.ToLowerInvariant();
        return host is "github.com" or "api.github.com" || host.EndsWith(".github.com") || host.EndsWith(".githubusercontent.com");
    }

    /// <summary>Downloads a release file to <paramref name="path"/> and checks it: the size GitHub gave, and its SHA-256
    /// (GitHub's digest, else the release's .sha256 file). No checksum, or a mismatch, and the file is deleted.</summary>
    public async Task DownloadAsync(UpdateAsset asset, string path, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        string expected = asset.Sha256 ?? (asset.Sha256Url != null ? await ReadChecksumAsync(asset.Sha256Url, asset.Name, ct) : "")
            ?? throw new UpdateException("This release has no checksum for " + asset.Name + ", so it can't be checked. Download it from the release page instead.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(expected, "^[0-9a-f]{64}$")) throw new UpdateException("This release has no usable checksum for " + asset.Name + ", so it can't be checked. Download it from the release page instead.");
        var uri = new Uri(asset.Url);
        if (!Allowed(uri)) throw new UpdateException("The update isn't on GitHub (" + uri.Host + "), so it wasn't downloaded.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string part = path + ".part";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri); request.Headers.UserAgent.ParseAdd(userAgent);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            // GitHub sends release files from its download hosts; wherever it ended up must still be one of them.
            if (response.RequestMessage?.RequestUri is Uri final && !Allowed(final)) throw new UpdateException("The download was sent somewhere other than GitHub (" + final.Host + "), so it was stopped.");
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? asset.Size;
            using (var input = await response.Content.ReadAsStreamAsync(ct))
            using (var output = File.Create(part))
            using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[128 * 1024]; long done = 0; int n;
                while ((n = await input.ReadAsync(buffer, ct)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, n), ct); sha.AppendData(buffer, 0, n); done += n;
                    if (total > 0) progress?.Report(Math.Min(1, (double)done / total));
                }
                if (asset.Size > 0 && done != asset.Size) throw new UpdateException("The download of " + asset.Name + " was incomplete.");
                string got = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
                if (got != expected) throw new UpdateException("The downloaded " + asset.Name + " doesn't match its checksum, so it was deleted and nothing was installed.");
            }
            File.Move(part, path, true);
            progress?.Report(1);
        }
        finally { if (File.Exists(part)) File.Delete(part); }
    }

    async Task<string?> ReadChecksumAsync(string url, string name, CancellationToken ct)
    {
        var uri = new Uri(url); if (!Allowed(uri)) return null;
        using var request = new HttpRequestMessage(HttpMethod.Get, uri); request.Headers.UserAgent.ParseAdd(userAgent);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        // "<hash>  <file name>" (sha256sum style) or just the hash.
        string text = (await response.Content.ReadAsStringAsync(ct)).Trim();
        string first = text.Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return first.ToLowerInvariant();
    }
}

/// <summary>Applying an update to a portable copy: the new release (unzipped elsewhere) copies itself over the old
/// folder once the old copy has closed. The app's own folders (Designer, Runtime, Docs) are replaced exactly, so no
/// file from the old version is left behind; anything else in the folder (your own files) is left alone.</summary>
public static class UpdateInstall
{
    static readonly string[] Replaced = ["Designer", "Runtime", "Docs"];
    static readonly string[] Merged = ["TestEnvironment"];

    /// <summary>The release folder inside an unzipped release (the folder holding Designer\ArcadiaStudio.exe).</summary>
    public static string? FindRelease(string unzipped)
    {
        foreach (var exe in Directory.EnumerateFiles(unzipped, "ArcadiaStudio.exe", SearchOption.AllDirectories))
            if (Path.GetFileName(Path.GetDirectoryName(exe)) == "Designer") return Path.GetDirectoryName(Path.GetDirectoryName(exe));
        return null;
    }

    /// <summary>Unzips a downloaded release ZIP into a fresh folder and returns its release folder.</summary>
    public static string Unzip(string zip, string folder)
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        Directory.CreateDirectory(folder);
        string root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        using (var archive = ZipFile.OpenRead(zip))
            foreach (var entry in archive.Entries)
            {
                string target = Path.GetFullPath(Path.Combine(folder, entry.FullName));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new UpdateException("The update ZIP has a file outside its own folder, so it wasn't used.");
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, true);
            }
        return FindRelease(folder) ?? throw new UpdateException("The update ZIP doesn't contain Arcadia Studio (Designer\\ArcadiaStudio.exe).");
    }

    /// <summary>Copies a release over a portable copy.</summary>
    public static void CopyRelease(string source, string target)
    {
        foreach (var name in Replaced)
        {
            string from = Path.Combine(source, name), to = Path.Combine(target, name);
            if (!Directory.Exists(from)) continue;
            Mirror(from, to);
        }
        foreach (var name in Merged)
        {
            string from = Path.Combine(source, name);
            if (Directory.Exists(from)) CopyOver(from, Path.Combine(target, name));
        }
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
    }
    static void CopyOver(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
        foreach (var dir in Directory.EnumerateDirectories(from)) CopyOver(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
    static void Mirror(string from, string to)
    {
        CopyOver(from, to);
        // Remove what the new version doesn't have, so an old library can never be loaded by mistake.
        foreach (var file in Directory.EnumerateFiles(to, "*", SearchOption.AllDirectories).ToList())
            if (!File.Exists(Path.Combine(from, Path.GetRelativePath(to, file)))) File.Delete(file);
        foreach (var dir in Directory.EnumerateDirectories(to, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).ToList())
            if (!Directory.Exists(Path.Combine(from, Path.GetRelativePath(to, dir))) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
    }
}
