using DownloadRouter.Core.Models;

namespace DownloadRouter.Core.Jobs;

public enum DownloadRegistrationDecision
{
    /// <summary>A live transfer from a supported extension build. A new job may be created.</summary>
    Track,

    /// <summary>The extension build is unknown, so its download events cannot be trusted to be live.</summary>
    RejectUnsupportedExtension,

    /// <summary>The browser already finished this transfer, so it cannot be starting now.</summary>
    RejectAlreadyFinished,

    /// <summary>The transfer began before this session; it is a replayed history record.</summary>
    RejectStartedBeforeSession,
}

/// <summary>
/// Guards job creation against replayed browser history.
///
/// Chromium raises <c>downloads.onCreated</c> for every item in the download history when
/// the browser starts. A browser can also keep serving a cached older service worker after
/// an upgrade, so the agent must not assume the extension filters anything. Creation is
/// therefore fail-closed: an unrecognised extension build, a missing transfer state, or a
/// transfer that started before this session can never create a job.
///
/// Rejection never blocks the browser download and never touches existing jobs.
/// </summary>
public static class DownloadRegistrationPolicy
{
    /// <summary>
    /// A real <c>onCreated</c> notification reaches the agent within seconds. The allowance
    /// is generous enough to absorb agent startup and native messaging latency.
    /// </summary>
    public static readonly TimeSpan LiveDownloadWindow = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Extension builds whose download registration behaviour this agent understands.
    /// Keep in sync with <c>extensionBuild</c> in the extension's download-origin module.
    /// </summary>
    public static readonly IReadOnlySet<string> SupportedExtensionBuilds =
        new HashSet<string>(StringComparer.Ordinal) { "2026.08.02" };

    public static DownloadRegistrationDecision Classify(
        DownloadStartedPayload payload,
        DateTimeOffset now)
    {
        var build = payload.ExtensionBuild?.Trim();
        if (string.IsNullOrEmpty(build) || !SupportedExtensionBuilds.Contains(build))
        {
            return DownloadRegistrationDecision.RejectUnsupportedExtension;
        }

        var state = payload.State?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(state))
        {
            return DownloadRegistrationDecision.RejectUnsupportedExtension;
        }

        if (state is "complete" or "interrupted" or "cancelled")
        {
            return DownloadRegistrationDecision.RejectAlreadyFinished;
        }

        if (payload.StartedAt is DateTimeOffset startedAt
            && now - startedAt > LiveDownloadWindow)
        {
            return DownloadRegistrationDecision.RejectStartedBeforeSession;
        }

        return DownloadRegistrationDecision.Track;
    }

    public static string DescribeRejection(DownloadRegistrationDecision decision)
        => decision switch
        {
            DownloadRegistrationDecision.RejectUnsupportedExtension => "unsupported-extension-build",
            DownloadRegistrationDecision.RejectAlreadyFinished => "already-finished",
            DownloadRegistrationDecision.RejectStartedBeforeSession => "started-before-session",
            _ => "tracked",
        };

    /// <summary>
    /// User-facing guidance shown when the browser is still running an extension build
    /// this agent does not recognise.
    /// </summary>
    public const string ExtensionRefreshGuidance =
        "브라우저가 이전 버전의 확장을 계속 사용하고 있습니다. 확장 관리 화면에서 eslee Download Router를 새로 고친 뒤 브라우저를 다시 시작하세요. 그때까지 새 다운로드는 추적되지 않으며 브라우저 다운로드 자체는 그대로 동작합니다.";
}
