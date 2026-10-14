using DownloadRouter.App.Controls;
using DownloadRouter.Core.Jobs;
using DownloadRouter.Core.Localization;
using DownloadRouter.Core.Models;

namespace DownloadRouter.App.Services;

/// <summary>How a download job is described on screen: status sentence, color, icon, and source.</summary>
internal static class JobText
{
    public static string DescribeStatus(DownloadJob job)
        => job.ErrorCode switch
        {
            "file.source-cleanup-pending" => AppText.Pick("복사 확인 완료 · 원본 정리 다시 시도 대기", "Copied and verified · original cleanup pending"),
            "file.source-changed-after-copy" => AppText.Pick("복사 뒤 원본이 바뀜 · 두 파일 확인 필요", "Original changed after copy · check both files"),
            "file.staging-changed" => AppText.Pick("임시 파일이 바뀜 · 직접 확인 필요", "Temporary file changed · check manually"),
            _ => job.BrowserState switch
            {
                BrowserTransferState.Cancelled => AppText.Pick("브라우저에서 취소함", "Cancelled in the browser"),
                BrowserTransferState.Interrupted => AppText.Pick("브라우저 또는 네트워크 오류로 중단", "Stopped by a browser or network error"),
                BrowserTransferState.InProgress when job.RoutingState == RoutingState.WaitingForSelection
                    => AppText.Pick("다운로드 중 · 저장 위치 선택 대기", "Downloading · waiting for a folder"),
                BrowserTransferState.Complete when job.RoutingState == RoutingState.WaitingForSelection
                    => AppText.Pick("다운로드 완료 · 저장 위치 선택 대기", "Downloaded · waiting for a folder"),
                BrowserTransferState.InProgress when job.RoutingState == RoutingState.SelectionReady
                    => AppText.Pick("저장 위치 선택 완료 · 다운로드 중", "Folder chosen · downloading"),
                _ => job.RoutingState switch
                {
                    RoutingState.Moving => AppText.Pick("파일 옮기는 중", "Moving file"),
                    RoutingState.Completed => AppText.Pick("옮김 완료", "Moved"),
                    RoutingState.Skipped => AppText.Pick("옮기지 않음", "Not moved"),
                    RoutingState.RetryPending => AppText.Pick("옮기기 다시 시도 대기", "Move retry pending"),
                    RoutingState.Failed => AppText.Pick("옮기기 실패", "Move failed"),
                    RoutingState.NotRequired when job.BrowserState == BrowserTransferState.InProgress
                        => AppText.Pick("다운로드 중", "Downloading"),
                    RoutingState.NotRequired => AppText.Pick("다운로드 완료", "Downloaded"),
                    _ => AppText.Pick("상태 확인 중", "Checking"),
                },
            },
        };

    /// <summary>Short label for list rows.</summary>
    public static string ShortStatus(DownloadJob job)
        => job.BrowserState switch
        {
            BrowserTransferState.Cancelled => AppText.Pick("취소", "Cancelled"),
            BrowserTransferState.Interrupted => AppText.Pick("중단", "Stopped"),
            _ when job.IsSelectionPending => AppText.Pick("위치 선택 대기", "Needs folder"),
            _ => job.RoutingState switch
            {
                RoutingState.Moving => AppText.Pick("옮기는 중", "Moving"),
                RoutingState.Completed => AppText.Pick("옮김", "Moved"),
                RoutingState.Skipped => AppText.Pick("옮기지 않음", "Not moved"),
                RoutingState.RetryPending => AppText.Pick("재시도 대기", "Retry pending"),
                RoutingState.Failed => AppText.Pick("실패", "Failed"),
                _ when job.BrowserState == BrowserTransferState.InProgress => AppText.Pick("다운로드 중", "Downloading"),
                _ => AppText.Pick("다운로드 완료", "Downloaded"),
            },
        };

    public static BadgeKind Kind(DownloadJob job)
        => job.BrowserState == BrowserTransferState.Interrupted || job.RoutingState is RoutingState.Failed or RoutingState.RetryPending
            ? BadgeKind.Critical
            : job.IsSelectionPending
                ? BadgeKind.Caution
                : job.RoutingState == RoutingState.Completed
                    ? BadgeKind.Success
                    : job.BrowserState == BrowserTransferState.InProgress || job.RoutingState == RoutingState.Moving
                        ? BadgeKind.Accent
                        : BadgeKind.Neutral;

    public static string Glyph(DownloadJob job)
        => job.BrowserState switch
        {
            BrowserTransferState.Cancelled => "",
            BrowserTransferState.Interrupted => "",
            _ when job.IsSelectionPending => "",
            _ => job.RoutingState switch
            {
                RoutingState.Completed => "",
                RoutingState.Failed or RoutingState.RetryPending => "",
                RoutingState.Skipped => "",
                RoutingState.Moving => "",
                _ => "",
            },
        };

    public static string SourceHost(DownloadJob job)
        => Uri.TryCreate(job.SanitizedSource, UriKind.Absolute, out var source)
            ? source.Host
            : AppText.Pick("출처 확인 불가", "Unknown source");

    public static string RelativeTime(DateTimeOffset value)
    {
        var local = value.ToLocalTime();
        var today = DateTimeOffset.Now.Date;
        return local.Date == today
            ? local.ToString("HH:mm")
            : local.Date == today.AddDays(-1)
                ? AppText.Pick("어제 ", "Yesterday ") + local.ToString("HH:mm")
                : local.ToString(AppText.IsEnglish ? "MMM d HH:mm" : "M월 d일 HH:mm", AppText.IsEnglish
                    ? System.Globalization.CultureInfo.GetCultureInfo("en-US")
                    : System.Globalization.CultureInfo.GetCultureInfo("ko-KR"));
    }

    public static bool CanRetry(DownloadJob job)
        => job.BrowserState == BrowserTransferState.Complete
            && job.RoutingState is RoutingState.Failed or RoutingState.RetryPending
            && !string.IsNullOrEmpty(job.OriginalPath);

    /// <summary>Where the file is now: the moved location, or the browser's original location.</summary>
    public static string? CurrentFilePath(DownloadJob job)
        => job.RoutingState == RoutingState.Completed && !string.IsNullOrEmpty(job.FinalPath)
            ? job.FinalPath
            : job.BrowserState == BrowserTransferState.Complete ? job.OriginalPath : null;

    public static string DisplayFileName(DownloadJob job)
        => DownloadPresentation.DisplayFileName(job);
}
