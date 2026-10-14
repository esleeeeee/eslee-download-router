using DownloadRouter.Core.Localization;

namespace DownloadRouter.Core.Models;

public enum BrowserSupportLevel
{
    /// <summary>Extension connection and real downloads verified.</summary>
    Official,

    /// <summary>Registration implemented, real downloads not verified yet.</summary>
    Compatible,
}

public sealed record BrowserSupport(
    BrowserKind Kind,
    string DisplayName,
    string ExtensionManagementUrl,
    BrowserSupportLevel Level)
{
    public string LevelDescription
        => Level == BrowserSupportLevel.Official
            ? AppText.Pick("공식 지원", "Officially supported")
            : AppText.Pick("호환 지원", "Compatible");
}

/// <summary>
/// The single list of browsers the product claims to support.
///
/// The installer registers the local messaging entry for exactly these browsers, so this
/// list, the registration script, and every user-visible support sentence stay in step.
/// A regression test compares it against the registration script. The lists are rebuilt
/// on each access so display names follow the current language.
/// </summary>
public static class BrowserSupportCatalog
{
    public static IReadOnlyList<BrowserSupport> All =>
    [
        new(BrowserKind.Whale, AppText.Pick("네이버 웨일", "NAVER Whale"), "whale://extensions", BrowserSupportLevel.Official),
        new(BrowserKind.Edge, "Microsoft Edge", "edge://extensions", BrowserSupportLevel.Official),
        new(BrowserKind.Chrome, "Google Chrome", "chrome://extensions", BrowserSupportLevel.Official),
        new(BrowserKind.Brave, "Brave", "brave://extensions", BrowserSupportLevel.Compatible),
        new(BrowserKind.Vivaldi, "Vivaldi", "vivaldi://extensions", BrowserSupportLevel.Compatible),
        new(BrowserKind.Opera, "Opera", "opera://extensions", BrowserSupportLevel.Compatible),
    ];

    public static IReadOnlyList<BrowserSupport> Official
        => All.Where(static browser => browser.Level == BrowserSupportLevel.Official).ToArray();

    public static IReadOnlyList<BrowserSupport> Compatible
        => All.Where(static browser => browser.Level == BrowserSupportLevel.Compatible).ToArray();

    /// <summary>Sentence shown on the app information screen.</summary>
    public static string SupportSummary
        => AppText.Pick(
            $"Windows 11 x64 · {Join(Official)} 공식 지원 · {Join(Compatible)} 호환 지원 · Firefox 제외",
            $"Windows 11 x64 · Officially supports {Join(Official)} · Compatible with {Join(Compatible)} · Firefox not supported");

    private static string Join(IReadOnlyList<BrowserSupport> browsers)
        => string.Join(" / ", browsers.Select(static browser => browser.DisplayName));
}
