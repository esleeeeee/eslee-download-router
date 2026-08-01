using DownloadRouter.Core.Models;

namespace DownloadRouter.Core.Rules;

/// <summary>
/// Maps internal rule enums to the wording shown in the app.
/// Enum member names are never rendered to the user.
/// </summary>
public sealed record RuleChoice<T>(T Value, string Label, string Description, string Example)
    where T : struct, Enum;

public static class RulePresentation
{
    public const RuleMatchType DefaultMatchType = RuleMatchType.DomainAndSubdomains;
    public const RuleMatchTarget DefaultMatchTarget = RuleMatchTarget.InitiatingPage;
    public const StorageMode DefaultStorageMode = StorageMode.Automatic;

    public static IReadOnlyList<RuleChoice<RuleMatchType>> MatchTypes { get; } =
    [
        new(
            RuleMatchType.DomainAndSubdomains,
            "이 도메인과 하위 도메인 모두",
            "입력한 도메인과 그 아래의 모든 하위 도메인에 적용합니다.",
            "naver.com 을 입력하면 naver.com, mail.naver.com 등에 적용됩니다."),
        new(
            RuleMatchType.ExactHost,
            "입력한 호스트만",
            "입력한 주소와 정확히 같은 호스트에만 적용합니다.",
            "download.example.com 을 입력하면 download.example.com 에만 적용됩니다."),
        new(
            RuleMatchType.UrlContains,
            "주소에 특정 문구가 포함될 때",
            "전체 주소 안에 입력한 문구가 들어 있으면 적용합니다.",
            "/board/ 를 입력하면 주소에 /board/ 가 포함된 다운로드에 적용됩니다."),
    ];

    public static IReadOnlyList<RuleChoice<RuleMatchTarget>> MatchTargets { get; } =
    [
        new(
            RuleMatchTarget.InitiatingPage,
            "다운로드를 시작한 웹페이지",
            "파일을 내려받기 시작한 페이지 주소를 기준으로 판단합니다. 권장 설정입니다.",
            "게시판에서 첨부파일을 받을 때 게시판 주소로 판단합니다."),
        new(
            RuleMatchTarget.FileUrl,
            "실제 파일 주소",
            "브라우저가 파일을 내려받은 주소를 기준으로 판단합니다.",
            "파일이 별도의 저장소 주소에서 전달될 때 사용합니다."),
        new(
            RuleMatchTarget.Either,
            "둘 중 하나",
            "웹페이지 주소나 파일 주소 가운데 하나만 맞아도 적용합니다.",
            "어느 쪽 주소가 맞을지 확실하지 않을 때 사용합니다."),
    ];

    public static IReadOnlyList<RuleChoice<StorageMode>> StorageModes { get; } =
    [
        new(
            StorageMode.Automatic,
            "정한 폴더로 자동 이동",
            "다운로드가 끝나면 묻지 않고 기준 폴더로 옮깁니다.",
            "같은 사이트의 파일을 항상 한곳에 모을 때 사용합니다."),
        new(
            StorageMode.SelectSubfolder,
            "다운로드마다 하위 폴더 선택",
            "다운로드마다 기준 폴더 아래에서 저장할 하위 폴더를 직접 고릅니다.",
            "받을 때마다 분류가 달라지는 자료에 사용합니다."),
    ];

    public static string MatchTypeLabel(RuleMatchType value)
        => Find(MatchTypes, value).Label;

    public static string MatchTargetLabel(RuleMatchTarget value)
        => Find(MatchTargets, value).Label;

    public static string StorageModeLabel(StorageMode value)
        => Find(StorageModes, value).Label;

    public static string MatchScopeSummary(RuleMatchType matchType, string matchValue)
    {
        var trimmed = string.IsNullOrWhiteSpace(matchValue) ? "(주소 미입력)" : matchValue.Trim();
        return matchType switch
        {
            RuleMatchType.DomainAndSubdomains => $"{trimmed}와 모든 하위 도메인",
            RuleMatchType.ExactHost => $"{trimmed} 호스트만",
            RuleMatchType.UrlContains => $"주소에 {trimmed}가 포함된 다운로드",
            _ => trimmed,
        };
    }

    /// <summary>
    /// One plain-language sentence describing what the rule will do.
    /// </summary>
    public static string Summarize(
        RuleMatchType matchType,
        string matchValue,
        RuleMatchTarget matchTarget,
        StorageMode storageMode,
        string storageRootDisplay)
    {
        var scope = MatchScopeSummary(matchType, matchValue);
        var target = matchTarget switch
        {
            RuleMatchTarget.InitiatingPage => "에서 시작한 다운로드를",
            RuleMatchTarget.FileUrl => "의 파일 주소로 받은 다운로드를",
            _ => "의 웹페이지 주소나 파일 주소로 받은 다운로드를",
        };
        var root = string.IsNullOrWhiteSpace(storageRootDisplay) ? "(기준 폴더 미지정)" : storageRootDisplay.Trim();
        var destination = storageMode == StorageMode.Automatic
            ? $"{root} 로 자동 이동합니다."
            : $"{root} 아래에서 매번 선택한 하위 폴더로 이동합니다.";
        return $"{scope}{target} {destination}";
    }

    private static RuleChoice<T> Find<T>(IReadOnlyList<RuleChoice<T>> choices, T value)
        where T : struct, Enum
        => choices.FirstOrDefault(choice => EqualityComparer<T>.Default.Equals(choice.Value, value))
            ?? choices[0];
}
