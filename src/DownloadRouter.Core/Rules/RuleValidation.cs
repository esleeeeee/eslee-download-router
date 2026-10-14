using DownloadRouter.Core.Localization;
using DownloadRouter.Core.Models;
using DownloadRouter.Core.Paths;

namespace DownloadRouter.Core.Rules;

/// <summary>
/// The single validation path for a rule that is about to be stored, shared by
/// <c>rules.upsert</c> and rule import so both accept exactly the same rules.
/// </summary>
public static class RuleValidation
{
    public const int MaximumNameLength = 200;
    public const int MaximumMatchValueLength = 2048;
    public const int MaximumStorageRootLength = 32767;

    /// <summary>
    /// Validates <paramref name="rule"/> and returns it with its file types normalized.
    /// </summary>
    /// <exception cref="ArgumentException">The rule cannot be stored.</exception>
    public static DownloadRule Normalize(DownloadRule rule, PathTokenResolver pathTokenResolver)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(pathTokenResolver);

        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            throw new ArgumentException(AppText.Pick("규칙 이름을 입력하세요.", "Enter a rule name."));
        }

        if (string.IsNullOrWhiteSpace(rule.MatchValue))
        {
            throw new ArgumentException(AppText.Pick("적용할 사이트 주소를 입력하세요.", "Enter the site address the rule applies to."));
        }

        if (string.IsNullOrWhiteSpace(rule.StorageRoot))
        {
            throw new ArgumentException(AppText.Pick("기준 폴더를 지정하세요.", "Choose a base folder."));
        }

        if (rule.Name.Length > MaximumNameLength
            || rule.MatchValue.Length > MaximumMatchValueLength
            || rule.StorageRoot.Length > MaximumStorageRootLength)
        {
            throw new ArgumentException(AppText.Pick(
                "규칙 항목이 허용된 길이를 넘었습니다.",
                "A rule field exceeds its maximum allowed length."));
        }

        if (!Enum.IsDefined(rule.MatchType)
            || !Enum.IsDefined(rule.MatchTarget)
            || !Enum.IsDefined(rule.StorageMode)
            || !Enum.IsDefined(rule.FileTypeMode))
        {
            throw new ArgumentException(AppText.Pick(
                "규칙 설정 값이 올바르지 않습니다.",
                "A rule setting has an unsupported value."));
        }

        _ = pathTokenResolver.Resolve(rule.StorageRoot);
        var extensions = FileTypeFilter.Normalize(rule.FileExtensions);
        if (rule.FileTypeMode != FileTypeFilterMode.AllFiles && extensions.Length == 0)
        {
            throw new ArgumentException(AppText.Pick(
                "파일 형식을 제한하려면 확장자를 하나 이상 입력하세요.",
                "Enter at least one file type to limit this rule by file type."));
        }

        return rule with { FileExtensions = extensions };
    }
}
