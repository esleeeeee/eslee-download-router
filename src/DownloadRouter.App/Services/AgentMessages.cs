using DownloadRouter.Core.Localization;
using DownloadRouter.Core.Models;

namespace DownloadRouter.App.Services;

/// <summary>
/// Turns agent failures into text in the app's language. Known error codes get a fixed
/// sentence; anything else falls back to the caller's sentence plus the agent's detail.
/// </summary>
internal static class AgentMessages
{
    public static string Describe(AgentResponse response, string fallback)
    {
        var known = response.ErrorCode switch
        {
            "database.unavailable" => AppText.Pick(
                "규칙 데이터베이스를 잠시 사용할 수 없습니다. 잠시 후 다시 시도해 주세요.",
                "The rule database is briefly unavailable. Try again in a moment."),
            "job.not-found" => AppText.Pick("다운로드 기록을 찾을 수 없습니다. 이미 삭제되었을 수 있습니다.", "The download record was not found. It may have been deleted."),
            "file.source-missing" => AppText.Pick("원래 다운로드 파일 위치를 알 수 없습니다.", "The original download location is unknown."),
            "download.not-complete" => AppText.Pick("다운로드가 끝난 파일만 다시 시도할 수 있습니다.", "Only finished downloads can be retried."),
            "security.path-boundary" => AppText.Pick("허용된 기준 폴더 밖으로는 옮길 수 없습니다.", "Files cannot be moved outside the allowed base folder."),
            "protocol.command-not-allowed" => AppText.Pick(
                "백그라운드 서비스가 이 기능을 지원하지 않습니다. 앱을 다시 시작해 주세요.",
                "The background service does not support this action. Restart the app."),
            _ => null,
        };
        if (known is not null)
        {
            return known;
        }

        return string.IsNullOrWhiteSpace(response.Message)
            ? fallback
            : $"{fallback}\n\n{response.Message}";
    }

    public static string Unreachable(Exception exception)
        => AppText.Pick(
            "백그라운드 서비스(Agent)에 연결하지 못했습니다. 잠시 후 다시 시도해 주세요.",
            "Could not reach the background service (agent). Try again in a moment.")
            + $"\n\n{exception.Message}";
}
