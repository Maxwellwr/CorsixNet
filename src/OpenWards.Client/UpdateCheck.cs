// Looking for a newer version, ported from CorsixTH CorsixTH/Lua/app.lua (App:checkForUpdates: once a launch, not for
// development versions, the semantic version comparison, the trusted download address). CorsixTH reads an update table
// from its website; here the newest release of the project on GitHub is asked for.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenWards.Client;

/// <summary>A newer version found: its number, what changed and where to get it.</summary>
internal sealed record UpdateInfo(string CurrentVersion, string NewVersion, string Description, string DownloadUrl);

/// <summary><c>App:checkForUpdates</c> against the project's GitHub releases.</summary>
internal static partial class UpdateCheck
{
    /// <summary>The newest release of the project (GitHub's API).</summary>
    public const string LatestReleaseUrl = "https://api.github.com/repos/Maxwellwr/OpenWards/releases/latest";

    /// <summary>
    /// Whether the project publishes releases on GitHub yet. Until the first one, GitHub answers 404 to every launch's
    /// request, so the check is not made; set this to true once a release is out.
    /// </summary>
    public static readonly bool ReleasesPublished;

    /// <summary>Where a download page may be (<c>trusted_prefixes</c>).</summary>
    private static readonly string[] TrustedPrefixes = ["https://github.com/"];

    /// <summary>This build's version (the assembly's informational version, without the commit after a <c>+</c>), or null.</summary>
    public static string? CurrentVersion()
    {
        var version = typeof(UpdateCheck).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return version?.Split('+')[0];
    }

    /// <summary>
    /// Asks GitHub for the newest release and returns it when it is newer than <paramref name="currentVersion"/>; null
    /// for a development version (as CorsixTH, which does not check "dev" versions), when this one is the newest, when
    /// the download is not on a trusted address, or when GitHub cannot be reached.
    /// </summary>
    public static async Task<UpdateInfo?> FindAsync(string? currentVersion, CancellationToken cancellation = default)
    {
        if (currentVersion is null || IsDevelopment(currentVersion))
        {
            return null;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OpenWards", currentVersion));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            var json = await client.GetStringAsync(new Uri(LatestReleaseUrl), cancellation).ConfigureAwait(false);
            return FromRelease(currentVersion, json);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            Console.Error.WriteLine("Couldn't check for updates: " + e.Message);
            return null;
        }
    }

    /// <summary>The update a release (GitHub's JSON) offers over <paramref name="currentVersion"/>, if any.</summary>
    public static UpdateInfo? FromRelease(string currentVersion, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True
            || root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True
            || !root.TryGetProperty("tag_name", out var tag) || tag.GetString() is not { } tagName
            || ParseVersion(tagName) is not { } newest)
        {
            return null;
        }

        if (IsCurrentNewest(currentVersion, (newest.Major, newest.Minor, newest.Revision)))
        {
            return null;
        }

        var url = root.TryGetProperty("html_url", out var page) ? page.GetString() ?? string.Empty : string.Empty;
        if (!TrustedPrefixes.Any(prefix => url.StartsWith(prefix, StringComparison.Ordinal)))
        {
            Console.Error.WriteLine("Update download url is not on the trusted domains list (" + url + ")");
            return null;
        }

        var body = root.TryGetProperty("body", out var text) ? text.GetString() ?? string.Empty : string.Empty;
        var name = string.Create(CultureInfo.InvariantCulture, $"{newest.Major}.{newest.Minor}.{newest.Revision}");
        return new UpdateInfo(currentVersion, name, body.Trim(), url);
    }

    /// <summary>Whether a version is a development one, which is never checked.</summary>
    public static bool IsDevelopment(string version) => version.Contains("dev", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <c>compare_versions</c>: whether <paramref name="current"/> is at least the newest release. A current version with
    /// a patch after its number (beta, rc) is older than a release of the same number.
    /// </summary>
    public static bool IsCurrentNewest(string current, (int Major, int Minor, int Revision) newest)
    {
        if (ParseVersion(current) is not { } mine)
        {
            return true;
        }

        if (mine.Major != newest.Major)
        {
            return mine.Major > newest.Major;
        }

        if (mine.Minor != newest.Minor)
        {
            return mine.Minor > newest.Minor;
        }

        return mine.Patch.Length > 0 ? mine.Revision > newest.Revision : mine.Revision >= newest.Revision;
    }

    /// <summary>"v1.2.3", "1.2", "1.2.3-beta" or "1.2.3 RC1": the numbers, and whatever follows them.</summary>
    public static (int Major, int Minor, int Revision, string Patch)? ParseVersion(string text)
    {
        var match = VersionPattern().Match(text.Trim());
        if (!match.Success)
        {
            return null;
        }

        var revision = match.Groups[3].Success ? int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
        return (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), revision, match.Groups[4].Value.Trim(' ', '-', '.'));
    }

    [GeneratedRegex(@"^[vV]?(\d+)\.(\d+)(?:\.(\d+))?(.*)$")]
    private static partial Regex VersionPattern();
}
