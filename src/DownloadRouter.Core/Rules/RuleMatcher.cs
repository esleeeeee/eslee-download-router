using System.Globalization;
using DownloadRouter.Core.Models;

namespace DownloadRouter.Core.Rules;

public sealed class RuleMatcher
{
    private static readonly IdnMapping Idn = new();

    /// <summary>
    /// Picks the most specific enabled rule whose address matches. When the file name is
    /// known, rules whose file-type filter rejects it are skipped. When it is not known yet,
    /// filtered rules still compete; only a filter ranked above the best all-files rule
    /// makes the result tentative, because the winner
    /// can change once the browser reports the real name.
    /// </summary>
    public RuleMatchResult? Match(IEnumerable<DownloadRule> rules, DownloadMetadata metadata)
        => Match(rules, metadata, previousRuleId: null, previousMatchedUrl: null, candidateRuleIds: null);

    /// <summary>
    /// Matches stored address candidates against current enabled rules and file types.
    /// Legacy jobs without candidates retain the previous-winner fallback. A job stores only sanitized
    /// addresses, so the rule that matched the full addresses at creation keeps counting as an
    /// address match even when its sanitized address no longer matches it.
    /// </summary>
    public RuleMatchResult? Rematch(
        IEnumerable<DownloadRule> rules,
        DownloadMetadata metadata,
        Guid previousRuleId,
        string? previousMatchedUrl,
        string? candidateRuleIds = null)
        => Match(rules, metadata, previousRuleId, previousMatchedUrl, candidateRuleIds);

    /// <summary>All enabled address matches before file-type filtering; never stores full URLs.</summary>
    public string GetCandidateRuleIds(IEnumerable<DownloadRule> rules, DownloadMetadata metadata)
        => string.Join(";", rules
            .Where(rule => rule.IsEnabled && CandidateUrls(rule, metadata).Any(url => TryMatch(rule, url.Url)))
            .Select(static rule => rule.Id).Distinct().Order().Select(static id => id.ToString("D")));

    private static RuleMatchResult? Match(
        IEnumerable<DownloadRule> rules,
        DownloadMetadata metadata,
        Guid? previousRuleId,
        string? previousMatchedUrl,
        string? candidateRuleIds)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(metadata);

        var fileNameKnown = FileTypeFilter.IsFileNameKnown(metadata.OriginalFileName);
        var enabled = rules.Where(static rule => rule.IsEnabled).ToList();
        var storedIds = candidateRuleIds?.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(static value => Guid.TryParse(value, out var id) ? id : Guid.Empty).ToHashSet();
        var candidates = storedIds is not null
            ? enabled.Where(rule => storedIds.Contains(rule.Id))
                .Select(rule => new Candidate(rule, previousMatchedUrl ?? string.Empty, "StoredCandidate", Specificity(rule)))
                .ToList()
            : enabled
            .SelectMany(rule => CandidateUrls(rule, metadata)
                .Select(candidate => TryMatch(rule, candidate.Url)
                    ? new Candidate(rule, candidate.Url, candidate.Source, Specificity(rule))
                    : null))
            .Where(static candidate => candidate is not null)
            .Cast<Candidate>()
            .ToList();
        if (storedIds is null && previousRuleId is { } previousId
            && candidates.All(candidate => candidate.Rule.Id != previousId)
            && enabled.FirstOrDefault(rule => rule.Id == previousId) is { } previous)
        {
            candidates.Add(new Candidate(previous, previousMatchedUrl ?? string.Empty, "PreviousMatch", Specificity(previous)));
        }

        var ordered = candidates
            .OrderByDescending(static candidate => candidate.Specificity.TypeRank)
            .ThenByDescending(static candidate => candidate.Specificity.ValueLength)
            .ThenByDescending(static candidate => candidate.Rule.Priority)
            .ThenBy(static candidate => candidate.Rule.ListOrder)
            .ThenBy(static candidate => candidate.Rule.CreatedAt)
            .ThenBy(static candidate => candidate.Rule.Id)
            .ToList();
        // Lower-ranked filters cannot displace an all-files winner once the name is known.
        var isTentative = !fileNameKnown && ordered
            .TakeWhile(static candidate => candidate.Rule.FileTypeMode != FileTypeFilterMode.AllFiles)
            .Any();

        return ordered
            .Where(candidate => !fileNameKnown || FileTypeFilter.Matches(
                candidate.Rule.FileTypeMode,
                candidate.Rule.FileExtensions,
                metadata.OriginalFileName))
            .Select(candidate => new RuleMatchResult(
                candidate.Rule,
                candidate.Url,
                candidate.Source,
                isTentative))
            .FirstOrDefault();
    }

    public bool IsMatch(DownloadRule rule, string url)
        => TryMatch(rule, url);

    public static string NormalizeHost(string value)
    {
        var trimmed = value.Trim().TrimEnd('.');
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            trimmed = uri.IdnHost;
        }

        try
        {
            return Idn.GetAscii(trimmed).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    private static IEnumerable<(string Url, string Source)> CandidateUrls(
        DownloadRule rule,
        DownloadMetadata metadata)
    {
        if (rule.MatchTarget is RuleMatchTarget.InitiatingPage or RuleMatchTarget.Either)
        {
            if (!string.IsNullOrWhiteSpace(metadata.InitiatingPageUrl))
            {
                yield return (metadata.InitiatingPageUrl, nameof(metadata.InitiatingPageUrl));
            }
            else if (!string.IsNullOrWhiteSpace(metadata.ReferrerUrl))
            {
                yield return (metadata.ReferrerUrl, nameof(metadata.ReferrerUrl));
            }
        }

        if (rule.MatchTarget is RuleMatchTarget.FileUrl or RuleMatchTarget.Either)
        {
            if (!string.IsNullOrWhiteSpace(metadata.InitialUrl))
            {
                yield return (metadata.InitialUrl, nameof(metadata.InitialUrl));
            }

            if (!string.IsNullOrWhiteSpace(metadata.FinalUrl)
                && !string.Equals(metadata.FinalUrl, metadata.InitialUrl, StringComparison.Ordinal))
            {
                yield return (metadata.FinalUrl, nameof(metadata.FinalUrl));
            }
        }
    }

    private static bool TryMatch(DownloadRule rule, string candidateUrl)
    {
        if (string.IsNullOrWhiteSpace(candidateUrl) || string.IsNullOrWhiteSpace(rule.MatchValue))
        {
            return false;
        }

        if (rule.MatchType == RuleMatchType.UrlContains)
        {
            return candidateUrl.Contains(rule.MatchValue.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        if (!Uri.TryCreate(candidateUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        var candidateHost = NormalizeHost(uri.IdnHost);
        var ruleHost = NormalizeHost(rule.MatchValue);
        if (candidateHost.Length == 0 || ruleHost.Length == 0)
        {
            return false;
        }

        return rule.MatchType switch
        {
            RuleMatchType.ExactHost => candidateHost.Equals(ruleHost, StringComparison.Ordinal),
            RuleMatchType.DomainAndSubdomains => candidateHost.Equals(ruleHost, StringComparison.Ordinal)
                || candidateHost.EndsWith('.' + ruleHost, StringComparison.Ordinal),
            _ => false,
        };
    }

    private static (int TypeRank, int ValueLength) Specificity(DownloadRule rule)
        => rule.MatchType switch
        {
            RuleMatchType.ExactHost => (3, NormalizeHost(rule.MatchValue).Length),
            RuleMatchType.DomainAndSubdomains => (2, NormalizeHost(rule.MatchValue).Length),
            RuleMatchType.UrlContains => (1, rule.MatchValue.Trim().Length),
            _ => (0, 0),
        };

    private sealed record Candidate(
        DownloadRule Rule,
        string Url,
        string Source,
        (int TypeRank, int ValueLength) Specificity);
}
