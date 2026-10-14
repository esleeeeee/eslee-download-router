using System.Globalization;

namespace DownloadRouter.Core.Localization;

/// <summary>Language the user picked in Settings. <see cref="System"/> follows the Windows display language.</summary>
public enum AppLanguage
{
    System,
    Korean,
    English,
}

/// <summary>
/// Selects Korean or English display text at run time from a single build. Each process
/// calls <see cref="SetLanguage"/> once at startup with the saved preference; a change in
/// Settings applies after the app restarts. Every user-facing string is written with
/// <see cref="Pick"/> so both languages always live side by side.
/// </summary>
public static class AppText
{
    public static string LanguageCode { get; private set; } = ResolveCode(AppLanguage.System);

    public static bool IsEnglish => LanguageCode == "en";

    public static void SetLanguage(AppLanguage language)
        => LanguageCode = ResolveCode(language);

    /// <summary>System resolves to Korean only when the Windows display language is Korean.</summary>
    public static string ResolveCode(AppLanguage language, CultureInfo? uiCulture = null)
        => language switch
        {
            AppLanguage.Korean => "ko",
            AppLanguage.English => "en",
            _ => string.Equals(
                    (uiCulture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName,
                    "ko",
                    StringComparison.OrdinalIgnoreCase)
                ? "ko"
                : "en",
        };

    public static AppLanguage ParseLanguage(string? value)
        => Enum.TryParse<AppLanguage>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : AppLanguage.System;

    public static string Pick(string korean, string english)
        => IsEnglish ? english : korean;
}
