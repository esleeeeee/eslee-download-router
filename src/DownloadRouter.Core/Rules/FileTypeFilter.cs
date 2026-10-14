using DownloadRouter.Core.Jobs;
using DownloadRouter.Core.Localization;
using DownloadRouter.Core.Models;

namespace DownloadRouter.Core.Rules;

/// <summary>
/// Parses and applies a rule's file-type filter. Extensions are stored as
/// <c>".pdf;.zip;.tar.gz"</c>: lowercase, dot-prefixed, de-duplicated, in input order.
/// </summary>
public static class FileTypeFilter
{
    public const int MaximumEntries = 30;
    public const int MaximumEntryLength = 16;

    private static readonly char[] Separators = [',', ';', ' ', '\t', '\r', '\n'];

    /// <summary>
    /// Normalizes user input such as <c>"pdf, .ZIP; jpg tar.gz"</c> to
    /// <c>".pdf;.zip;.jpg;.tar.gz"</c>. Empty input yields an empty string.
    /// </summary>
    /// <exception cref="ArgumentException">An entry is malformed or there are too many entries.</exception>
    public static string Normalize(string? input)
        => string.Join(';', Parse(input));

    /// <summary>The normalized entries of <paramref name="input"/>, each with a leading dot.</summary>
    public static IReadOnlyList<string> Parse(string? input)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(input))
        {
            return result;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in input.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var entry = raw.ToLowerInvariant();
            var body = entry.StartsWith('.') ? entry[1..] : entry;
            if (!IsValidBody(body))
            {
                throw new ArgumentException(AppText.Pick(
                    $"파일 형식 '{Shorten(raw)}'을(를) 사용할 수 없습니다. 영문 소문자, 숫자, + _ - 와 점으로 된 16자 이하 확장자를 입력하세요. 예: pdf, zip, tar.gz",
                    $"The file type '{Shorten(raw)}' is not valid. Enter extensions of up to 16 characters made of letters, digits, + _ - and dots, for example pdf, zip, tar.gz."));
            }

            if (seen.Add("." + body))
            {
                result.Add("." + body);
            }
        }

        if (result.Count > MaximumEntries)
        {
            throw new ArgumentException(AppText.Pick(
                $"파일 형식은 최대 {MaximumEntries}개까지 입력할 수 있습니다.",
                $"Enter at most {MaximumEntries} file types."));
        }

        return result;
    }

    /// <summary>
    /// Whether a rule with this filter accepts <paramref name="fileName"/>. An extension matches
    /// when the name ends with it, ignoring case, so multi-part entries such as <c>.tar.gz</c> work.
    /// </summary>
    public static bool Matches(FileTypeFilterMode mode, string? extensions, string? fileName)
    {
        if (mode == FileTypeFilterMode.AllFiles)
        {
            return true;
        }

        var name = fileName?.Trim() ?? string.Empty;
        var listed = ParseLenient(extensions)
            .Any(extension => name.Length > extension.Length
                && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
        return mode switch
        {
            FileTypeFilterMode.OnlyListed => listed,
            FileTypeFilterMode.ExceptListed => !listed,
            // An unknown mode from a newer build never narrows the rule.
            _ => true,
        };
    }

    /// <summary>
    /// False for an empty name and for browser placeholders such as <c>*.crdownload</c>,
    /// which say nothing about the final file type.
    /// </summary>
    public static bool IsFileNameKnown(string? fileName)
        => DownloadPresentation.TrustedFileName(fileName) is not null;

    private static IEnumerable<string> ParseLenient(string? extensions)
    {
        if (string.IsNullOrWhiteSpace(extensions))
        {
            yield break;
        }

        foreach (var raw in extensions.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var body = raw.TrimStart('.');
            if (body.Length > 0)
            {
                yield return "." + body;
            }
        }
    }

    private static bool IsValidBody(string body)
    {
        if (body.Length is < 1 or > MaximumEntryLength)
        {
            return false;
        }

        foreach (var segment in body.Split('.'))
        {
            if (segment.Length == 0 || !segment.All(static character =>
                    character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '+' or '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static string Shorten(string value)
        => value.Length <= 24 ? value : value[..24] + "…";
}
