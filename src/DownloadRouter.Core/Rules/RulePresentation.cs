using DownloadRouter.Core.Localization;
using DownloadRouter.Core.Models;

namespace DownloadRouter.Core.Rules;

/// <summary>
/// Maps internal rule enums to the wording shown in the app.
/// Enum member names are never rendered to the user.
/// </summary>
public sealed record RuleChoice<T>(T Value, string Label, string Description, string Example)
    where T : struct, Enum;

/// <summary>
/// Rule wording in the current <see cref="AppText"/> language. The lists are rebuilt on each
/// access so they always follow the language chosen at startup.
/// </summary>
public static class RulePresentation
{
    public const RuleMatchType DefaultMatchType = RuleMatchType.DomainAndSubdomains;
    public const RuleMatchTarget DefaultMatchTarget = RuleMatchTarget.InitiatingPage;
    public const StorageMode DefaultStorageMode = StorageMode.Automatic;
    public const FileTypeFilterMode DefaultFileTypeMode = FileTypeFilterMode.AllFiles;

    public static IReadOnlyList<RuleChoice<RuleMatchType>> MatchTypes =>
    [
        new(
            RuleMatchType.DomainAndSubdomains,
            AppText.Pick("이 도메인과 하위 도메인 모두", "This domain and all its subdomains"),
            AppText.Pick(
                "입력한 도메인과 그 아래의 모든 하위 도메인에 적용합니다.",
                "Applies to the domain you enter and every subdomain under it."),
            AppText.Pick(
                "naver.com 을 입력하면 naver.com, mail.naver.com 등에 적용됩니다.",
                "Entering naver.com covers naver.com, mail.naver.com, and so on.")),
        new(
            RuleMatchType.ExactHost,
            AppText.Pick("입력한 호스트만", "Only this exact host"),
            AppText.Pick(
                "입력한 주소와 정확히 같은 호스트에만 적용합니다.",
                "Applies only to a host that is exactly the one you enter."),
            AppText.Pick(
                "download.example.com 을 입력하면 download.example.com 에만 적용됩니다.",
                "Entering download.example.com covers download.example.com only.")),
        new(
            RuleMatchType.UrlContains,
            AppText.Pick("주소에 특정 문구가 포함될 때", "When the address contains some text"),
            AppText.Pick(
                "전체 주소 안에 입력한 문구가 들어 있으면 적용합니다.",
                "Applies when the full address contains the text you enter."),
            AppText.Pick(
                "/board/ 를 입력하면 주소에 /board/ 가 포함된 다운로드에 적용됩니다.",
                "Entering /board/ covers downloads whose address contains /board/.")),
    ];

    public static IReadOnlyList<RuleChoice<RuleMatchTarget>> MatchTargets =>
    [
        new(
            RuleMatchTarget.InitiatingPage,
            AppText.Pick("다운로드를 시작한 웹페이지", "The page that started the download"),
            AppText.Pick(
                "파일을 내려받기 시작한 페이지 주소를 기준으로 판단합니다. 권장 설정입니다.",
                "Decides by the address of the page the download started from. Recommended."),
            AppText.Pick(
                "게시판에서 첨부파일을 받을 때 게시판 주소로 판단합니다.",
                "An attachment from a message board is matched by the board's address.")),
        new(
            RuleMatchTarget.FileUrl,
            AppText.Pick("실제 파일 주소", "The file's own address"),
            AppText.Pick(
                "브라우저가 파일을 내려받은 주소를 기준으로 판단합니다.",
                "Decides by the address the browser downloaded the file from."),
            AppText.Pick(
                "파일이 별도의 저장소 주소에서 전달될 때 사용합니다.",
                "Use it when files are served from a separate storage address.")),
        new(
            RuleMatchTarget.Either,
            AppText.Pick("둘 중 하나", "Either one"),
            AppText.Pick(
                "웹페이지 주소나 파일 주소 가운데 하나만 맞아도 적용합니다.",
                "Applies when either the page address or the file address matches."),
            AppText.Pick(
                "어느 쪽 주소가 맞을지 확실하지 않을 때 사용합니다.",
                "Use it when you are not sure which address will match.")),
    ];

    public static IReadOnlyList<RuleChoice<StorageMode>> StorageModes =>
    [
        new(
            StorageMode.Automatic,
            AppText.Pick("정한 폴더로 자동 이동", "Move to a set folder automatically"),
            AppText.Pick(
                "다운로드가 끝나면 묻지 않고 기준 폴더로 옮깁니다.",
                "When a download finishes, it is moved to the base folder without asking."),
            AppText.Pick(
                "같은 사이트의 파일을 항상 한곳에 모을 때 사용합니다.",
                "Use it to always collect files from the same site in one place.")),
        new(
            StorageMode.SelectSubfolder,
            AppText.Pick("다운로드마다 하위 폴더 선택", "Choose a subfolder for each download"),
            AppText.Pick(
                "다운로드마다 기준 폴더 아래에서 저장할 하위 폴더를 직접 고릅니다.",
                "For each download you pick a subfolder under the base folder."),
            AppText.Pick(
                "받을 때마다 분류가 달라지는 자료에 사용합니다.",
                "Use it for files you sort differently each time.")),
    ];

    public static IReadOnlyList<RuleChoice<FileTypeFilterMode>> FileTypeModes =>
    [
        new(
            FileTypeFilterMode.AllFiles,
            AppText.Pick("모든 파일", "All files"),
            AppText.Pick(
                "파일 형식과 관계없이 적용합니다.",
                "Applies to every file type."),
            AppText.Pick(
                "사이트에서 받는 모든 파일을 한곳에 모을 때 사용합니다.",
                "Use it to collect every file from the site in one place.")),
        new(
            FileTypeFilterMode.OnlyListed,
            AppText.Pick("지정한 형식만", "Only these file types"),
            AppText.Pick(
                "입력한 확장자로 끝나는 파일에만 적용합니다.",
                "Applies only to files that end with one of the extensions you enter."),
            AppText.Pick(
                "pdf, hwp 를 입력하면 문서 파일만 옮깁니다.",
                "Entering pdf, docx moves only those documents.")),
        new(
            FileTypeFilterMode.ExceptListed,
            AppText.Pick("지정한 형식 제외", "All except these file types"),
            AppText.Pick(
                "입력한 확장자로 끝나는 파일은 옮기지 않고 나머지에 적용합니다.",
                "Applies to every file except those that end with one of the extensions you enter."),
            AppText.Pick(
                "exe, msi 를 입력하면 설치 파일은 브라우저가 저장한 곳에 그대로 둡니다.",
                "Entering exe, msi leaves installers where the browser saved them.")),
    ];

    public static string MatchTypeLabel(RuleMatchType value)
        => Find(MatchTypes, value).Label;

    public static string MatchTargetLabel(RuleMatchTarget value)
        => Find(MatchTargets, value).Label;

    public static string StorageModeLabel(StorageMode value)
        => Find(StorageModes, value).Label;

    public static string FileTypeModeLabel(FileTypeFilterMode value)
        => Find(FileTypeModes, value).Label;

    /// <summary>
    /// The mode label with its extensions, for example "Only these file types: .pdf, .zip".
    /// </summary>
    public static string FileTypeModeLabel(FileTypeFilterMode value, string? fileExtensions)
        => value == FileTypeFilterMode.AllFiles
            ? FileTypeModeLabel(value)
            : $"{FileTypeModeLabel(value)}: {DisplayExtensions(fileExtensions)}";

    public static string MatchScopeSummary(RuleMatchType matchType, string matchValue)
    {
        var trimmed = string.IsNullOrWhiteSpace(matchValue)
            ? AppText.Pick("(주소 미입력)", "(no address entered)")
            : matchValue.Trim();
        return matchType switch
        {
            RuleMatchType.DomainAndSubdomains => AppText.Pick($"{trimmed}와 모든 하위 도메인", $"{trimmed} and all its subdomains"),
            RuleMatchType.ExactHost => AppText.Pick($"{trimmed} 호스트만", $"the host {trimmed} only"),
            RuleMatchType.UrlContains => AppText.Pick($"주소에 {trimmed}가 포함된 다운로드", $"addresses containing {trimmed}"),
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
        => Summarize(matchType, matchValue, matchTarget, storageMode, storageRootDisplay, FileTypeFilterMode.AllFiles, string.Empty);

    /// <summary>
    /// One plain-language sentence describing what the rule will do, including its file-type filter.
    /// </summary>
    public static string Summarize(
        RuleMatchType matchType,
        string matchValue,
        RuleMatchTarget matchTarget,
        StorageMode storageMode,
        string storageRootDisplay,
        FileTypeFilterMode fileTypeMode,
        string? fileExtensions)
    {
        var scope = MatchScopeSummary(matchType, matchValue);
        var root = string.IsNullOrWhiteSpace(storageRootDisplay)
            ? AppText.Pick("(기준 폴더 미지정)", "(no base folder chosen)")
            : storageRootDisplay.Trim();
        var extensions = DisplayExtensions(fileExtensions);
        return AppText.IsEnglish
            ? SummarizeEnglish(scope, matchTarget, storageMode, root, fileTypeMode, extensions)
            : SummarizeKorean(scope, matchTarget, storageMode, root, fileTypeMode, extensions);
    }

    private static string SummarizeKorean(
        string scope,
        RuleMatchTarget matchTarget,
        StorageMode storageMode,
        string root,
        FileTypeFilterMode fileTypeMode,
        string extensions)
    {
        var target = matchTarget switch
        {
            RuleMatchTarget.InitiatingPage => "에서 시작한 다운로드",
            RuleMatchTarget.FileUrl => "의 파일 주소로 받은 다운로드",
            _ => "의 웹페이지 주소나 파일 주소로 받은 다운로드",
        };
        var files = fileTypeMode switch
        {
            FileTypeFilterMode.OnlyListed => $" 가운데 {extensions} 파일만",
            FileTypeFilterMode.ExceptListed => $" 가운데 {extensions} 파일을 제외한 나머지를",
            _ => "를",
        };
        var destination = storageMode == StorageMode.Automatic
            ? $"{root} 로 자동 이동합니다."
            : $"{root} 아래에서 매번 선택한 하위 폴더로 이동합니다.";
        return $"{scope}{target}{files} {destination}";
    }

    private static string SummarizeEnglish(
        string scope,
        RuleMatchTarget matchTarget,
        StorageMode storageMode,
        string root,
        FileTypeFilterMode fileTypeMode,
        string extensions)
    {
        var target = matchTarget switch
        {
            RuleMatchTarget.InitiatingPage => $"Downloads started from {scope}",
            RuleMatchTarget.FileUrl => $"Downloads whose file address matches {scope}",
            _ => $"Downloads whose page or file address matches {scope}",
        };
        var files = fileTypeMode switch
        {
            FileTypeFilterMode.OnlyListed => $", only {extensions} files,",
            FileTypeFilterMode.ExceptListed => $", except {extensions} files,",
            _ => string.Empty,
        };
        var destination = storageMode == StorageMode.Automatic
            ? $"are moved to {root} automatically."
            : $"are moved to a subfolder of {root} that you choose each time.";
        return $"{target}{files} {destination}";
    }

    private static string DisplayExtensions(string? fileExtensions)
    {
        var entries = (fileExtensions ?? string.Empty)
            .Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static entry => "." + entry.TrimStart('.').ToLowerInvariant())
            .Where(static entry => entry.Length > 1)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return entries.Length == 0
            ? AppText.Pick("(형식 미입력)", "(no file types entered)")
            : string.Join(", ", entries);
    }

    private static RuleChoice<T> Find<T>(IReadOnlyList<RuleChoice<T>> choices, T value)
        where T : struct, Enum
        => choices.FirstOrDefault(choice => EqualityComparer<T>.Default.Equals(choice.Value, value))
            ?? choices[0];
}
