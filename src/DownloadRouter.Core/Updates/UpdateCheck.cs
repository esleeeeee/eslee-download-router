using System.Text.Json;

namespace DownloadRouter.Core.Updates;

public enum UpdateStatus
{
    /// <summary>The published release is newer than the running build.</summary>
    UpdateAvailable,

    /// <summary>The running build is the published release or newer (a development build).</summary>
    UpToDate,

    /// <summary>No comparison was possible.</summary>
    Unknown,
}

public sealed record LatestReleaseInfo(Version Version, string TagName, string ReleaseUrl);

/// <summary>
/// Version-check logic against the project's own GitHub releases.
///
/// Only public release metadata is read; nothing about the user or their downloads is
/// sent. The network call itself lives in the app so this logic stays testable, and a
/// failed or skipped check must never reach the download pipeline — the check is
/// informational only.
/// </summary>
public static class UpdateCheckPolicy
{
    /// <summary>GitHub already excludes drafts and prereleases from this endpoint.</summary>
    public const string LatestReleaseApiUrl =
        "https://api.github.com/repos/esleeeeee/eslee-download-router/releases/latest";

    /// <summary>Shown when no specific release URL is known yet.</summary>
    public const string ReleasesPageUrl =
        "https://github.com/esleeeeee/eslee-download-router/releases/latest";

    /// <summary>An automatic check runs at most once per day; manual checks are unrestricted.</summary>
    public static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(24);

    public static bool IsAutomaticCheckDue(string? lastCheckedAtIso, DateTimeOffset now)
        => !DateTimeOffset.TryParse(lastCheckedAtIso, out var lastCheckedAt)
            || now - lastCheckedAt >= AutomaticCheckInterval
            || lastCheckedAt > now;

    /// <summary>
    /// Reads the fields this feature needs from a releases/latest response body. Draft and
    /// prerelease entries are rejected even though the endpoint should never return them.
    /// </summary>
    public static LatestReleaseInfo? TryParseLatestRelease(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                || (root.TryGetProperty("prerelease", out var prerelease) && prerelease.ValueKind == JsonValueKind.True)
                || !root.TryGetProperty("tag_name", out var tagElement)
                || tagElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var tagName = tagElement.GetString()!;
            if (TryParseVersion(tagName) is not Version version)
            {
                return null;
            }

            var releaseUrl = root.TryGetProperty("html_url", out var urlElement)
                && urlElement.ValueKind == JsonValueKind.String
                    ? NormalizeReleaseUrl(urlElement.GetString())
                    : ReleasesPageUrl;
            return new LatestReleaseInfo(version, tagName, releaseUrl);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static UpdateStatus Evaluate(string? currentSemanticVersion, LatestReleaseInfo? latest)
    {
        if (latest is null || TryParseVersion(currentSemanticVersion) is not Version current)
        {
            return UpdateStatus.Unknown;
        }

        return latest.Version > current ? UpdateStatus.UpdateAvailable : UpdateStatus.UpToDate;
    }

    /// <summary>
    /// Returns the URL only when it points inside this project's own GitHub repository
    /// (https, github.com, no userinfo); everything else falls back to the releases page.
    /// This runs both on the network response and again on the value read back from the
    /// user-editable settings file, so nothing the app launches can be steered elsewhere.
    /// </summary>
    public static string NormalizeReleaseUrl(string? candidate)
        => Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            && uri.UserInfo.Length == 0
            && (uri.AbsolutePath.StartsWith("/esleeeeee/eslee-download-router/", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.AbsolutePath, "/esleeeeee/eslee-download-router", StringComparison.OrdinalIgnoreCase))
                ? uri.ToString()
                : ReleasesPageUrl;

    /// <summary>Accepts "1.2.3" or "v1.2.3"; anything else is not a product version.</summary>
    public static Version? TryParseVersion(string? value)
    {
        var trimmed = value?.Trim().TrimStart('v', 'V');
        return Version.TryParse(trimmed, out var version) && version.Build >= 0
            ? version
            : null;
    }

    public static string DescribeForUser(UpdateStatus status, string currentSemanticVersion, LatestReleaseInfo? latest)
        => status switch
        {
            UpdateStatus.UpdateAvailable when latest is not null =>
                $"새 버전 {ToDisplayVersion(latest.Version)}이(가) 있습니다. 현재 버전은 {currentSemanticVersion}입니다.",
            UpdateStatus.UpToDate =>
                $"최신 버전입니다. ({currentSemanticVersion})",
            _ =>
                "최신 버전을 확인하지 못했습니다. 네트워크 연결을 확인한 뒤 다시 시도해 주세요.",
        };

    public static string ToDisplayVersion(Version version)
        => version.ToString(3);
}
