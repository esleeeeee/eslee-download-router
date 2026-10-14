using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DownloadRouter.Core.Localization;
using DownloadRouter.Core.Models;
using DownloadRouter.Core.Paths;

namespace DownloadRouter.Core.Rules;

/// <param name="NewRules">Rules to store: fresh IDs, disabled, created now, ordered after the existing rules.</param>
/// <param name="SkippedDuplicates">Names of entries that repeat an existing rule or an earlier entry.</param>
/// <param name="Errors">One localized message per entry that could not be imported.</param>
public sealed record RuleImportResult(
    IReadOnlyList<DownloadRule> NewRules,
    IReadOnlyList<string> SkippedDuplicates,
    IReadOnlyList<string> Errors);

/// <summary>
/// Exports rules to a portable JSON file and imports them back. The file carries only what
/// a rule does, never IDs or timestamps, so importing always creates new rules and never
/// changes or removes an existing one. Every imported entry goes through the same
/// validation as <c>rules.upsert</c>.
/// </summary>
public static class RuleTransfer
{
    public const string Format = "eslee-download-router.rules";
    public const int Version = 1;
    public const int MaximumBytes = 1024 * 1024;
    public const int MaximumRules = 500;

    private static readonly JsonSerializerOptions ExportOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Export(IEnumerable<DownloadRule> rules, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var document = new ExportDocument(
            Format,
            Version,
            now.ToUniversalTime(),
            rules.Select(static rule => new ExportedRule(
                rule.Name,
                rule.IsEnabled,
                rule.MatchType,
                rule.MatchValue,
                rule.MatchTarget,
                rule.StorageRoot,
                rule.StorageMode,
                rule.FileTypeMode,
                rule.FileExtensions ?? string.Empty,
                rule.Priority)).ToArray());
        return JsonSerializer.Serialize(document, ExportOptions);
    }

    /// <summary>
    /// Reads an exported file. Entries that fail validation are reported in
    /// <see cref="RuleImportResult.Errors"/> and the rest are still imported.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The file is too large, is not JSON, is not a rules export of a supported version, or
    /// holds more than <see cref="MaximumRules"/> rules. Nothing is imported in that case.
    /// </exception>
    public static RuleImportResult Import(
        string json,
        IReadOnlyList<DownloadRule> existing,
        DateTimeOffset now,
        PathTokenResolver? pathTokenResolver = null)
    {
        ArgumentNullException.ThrowIfNull(existing);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException(NotAnExport());
        }

        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes)
        {
            throw new ArgumentException(AppText.Pick(
                "규칙 파일이 너무 큽니다. 1MB 이하의 파일만 가져올 수 있습니다.",
                "The rules file is too large. Only files up to 1 MB can be imported."));
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        }
        catch (JsonException)
        {
            throw new ArgumentException(NotAnExport());
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryGetProperty(root, "format", out var format)
                || format.ValueKind != JsonValueKind.String
                || !string.Equals(format.GetString(), Format, StringComparison.Ordinal))
            {
                throw new ArgumentException(NotAnExport());
            }

            if (!TryGetProperty(root, "version", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var versionNumber)
                || versionNumber != Version)
            {
                throw new ArgumentException(AppText.Pick(
                    "이 버전에서 읽을 수 없는 규칙 파일 형식입니다.",
                    "This rules file version is not supported."));
            }

            if (!TryGetProperty(root, "rules", out var entries) || entries.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException(NotAnExport());
            }

            if (entries.GetArrayLength() > MaximumRules)
            {
                throw new ArgumentException(AppText.Pick(
                    $"한 번에 가져올 수 있는 규칙은 최대 {MaximumRules}개입니다.",
                    $"At most {MaximumRules} rules can be imported at once."));
            }

            return ImportEntries(entries, existing, now, pathTokenResolver ?? new PathTokenResolver(new WindowsKnownPathProvider()));
        }
    }

    private static RuleImportResult ImportEntries(
        JsonElement entries,
        IReadOnlyList<DownloadRule> existing,
        DateTimeOffset now,
        PathTokenResolver pathTokenResolver)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in existing)
        {
            seen.Add(DuplicateKey(rule));
        }

        var nextOrder = existing.Count == 0 ? 0 : existing.Max(static rule => rule.ListOrder) + 1;
        var created = new List<DownloadRule>();
        var duplicates = new List<string>();
        var errors = new List<string>();
        var position = 0;
        foreach (var entry in entries.EnumerateArray())
        {
            position++;
            DownloadRule candidate;
            try
            {
                candidate = RuleValidation.Normalize(ReadEntry(entry, now), pathTokenResolver, forImport: true);
            }
            catch (ArgumentException exception)
            {
                errors.Add(AppText.Pick($"{position}번째 규칙: {exception.Message}", $"Rule {position}: {exception.Message}"));
                continue;
            }

            if (!seen.Add(DuplicateKey(candidate)))
            {
                duplicates.Add(candidate.Name);
                continue;
            }

            created.Add(candidate with { ListOrder = nextOrder++, IsEnabled = false });
        }

        return new RuleImportResult(created, duplicates, errors);
    }

    private static DownloadRule ReadEntry(JsonElement entry, DateTimeOffset now)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(AppText.Pick("규칙 항목의 형식이 올바르지 않습니다.", "The rule entry is not an object."));
        }

        return new DownloadRule(
            Guid.NewGuid(),
            RequiredString(entry, "name"),
            OptionalBoolean(entry, "isEnabled", true),
            RequiredEnum<RuleMatchType>(entry, "matchType"),
            RequiredString(entry, "matchValue"),
            OptionalEnum(entry, "matchTarget", RuleMatchTarget.InitiatingPage),
            RequiredString(entry, "storageRoot"),
            RequiredEnum<StorageMode>(entry, "storageMode"),
            OptionalInt(entry, "priority", 0),
            0,
            now,
            now,
            OptionalEnum(entry, "fileTypeMode", FileTypeFilterMode.AllFiles),
            OptionalString(entry, "fileExtensions") ?? string.Empty);
    }

    /// <summary>
    /// Two rules are duplicates when they would match and route the same downloads the same
    /// way. Names, priority and the enabled switch do not count.
    /// </summary>
    private static string DuplicateKey(DownloadRule rule)
    {
        var matchValue = rule.MatchType == RuleMatchType.UrlContains
            ? rule.MatchValue.Trim().ToUpperInvariant()
            : RuleMatcher.NormalizeHost(rule.MatchValue);
        string extensions;
        try
        {
            extensions = FileTypeFilter.Normalize(rule.FileExtensions);
        }
        catch (ArgumentException)
        {
            extensions = rule.FileExtensions ?? string.Empty;
        }

        var storageRoot = rule.StorageRoot.Trim().TrimEnd('\\', '/').ToUpperInvariant();
        return string.Join(
            '\u001f',
            rule.MatchType.ToString(),
            matchValue,
            rule.MatchTarget.ToString(),
            storageRoot,
            rule.StorageMode.ToString(),
            rule.FileTypeMode.ToString(),
            extensions);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string RequiredString(JsonElement entry, string name)
        => OptionalString(entry, name) is { } value && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw MissingField(name);

    private static string? OptionalString(JsonElement entry, string name)
    {
        if (!TryGetProperty(entry, name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : throw InvalidField(name);
    }

    private static bool OptionalBoolean(JsonElement entry, string name, bool fallback)
    {
        if (!TryGetProperty(entry, name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw InvalidField(name),
        };
    }

    private static int OptionalInt(JsonElement entry, string name, int fallback)
    {
        if (!TryGetProperty(entry, name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return fallback;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : throw InvalidField(name);
    }

    private static T RequiredEnum<T>(JsonElement entry, string name)
        where T : struct, Enum
        => TryGetProperty(entry, name, out var value) && value.ValueKind != JsonValueKind.Null
            ? ParseEnum<T>(value, name)
            : throw MissingField(name);

    private static T OptionalEnum<T>(JsonElement entry, string name, T fallback)
        where T : struct, Enum
        => TryGetProperty(entry, name, out var value) && value.ValueKind != JsonValueKind.Null
            ? ParseEnum<T>(value, name)
            : fallback;

    private static T ParseEnum<T>(JsonElement value, string name)
        where T : struct, Enum
    {
        // Only member names are accepted, so a number can never select an undefined value.
        if (value.ValueKind == JsonValueKind.String
            && value.GetString() is { } text
            && !text.Any(char.IsDigit)
            && Enum.TryParse<T>(text, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw InvalidField(name);
    }

    private static ArgumentException MissingField(string name)
        => new(AppText.Pick($"'{name}' 항목이 없습니다.", $"The '{name}' field is missing."));

    private static ArgumentException InvalidField(string name)
        => new(AppText.Pick($"'{name}' 항목의 값이 올바르지 않습니다.", $"The '{name}' field has an invalid value."));

    private static string NotAnExport()
        => AppText.Pick(
            "eslee Download Router에서 내보낸 규칙 파일이 아닙니다.",
            "This is not a rules file exported from eslee Download Router.");

    private sealed record ExportDocument(
        string Format,
        int Version,
        DateTimeOffset ExportedAt,
        IReadOnlyList<ExportedRule> Rules);

    private sealed record ExportedRule(
        string Name,
        bool IsEnabled,
        RuleMatchType MatchType,
        string MatchValue,
        RuleMatchTarget MatchTarget,
        string StorageRoot,
        StorageMode StorageMode,
        FileTypeFilterMode FileTypeMode,
        string FileExtensions,
        int Priority);
}
