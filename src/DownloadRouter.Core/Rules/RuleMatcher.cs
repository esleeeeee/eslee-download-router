using System.Globalization;
using DownloadRouter.Core.Models;

namespace DownloadRouter.Core.Rules;

public sealed class RuleMatcher
{
    private static readonly IdnMapping Idn = new();

    /// <summary>
    /// Picks the most specific enabled rule whose address matches. When the file name is
    /// known, rules whose file-type filter rejects it are skipped. When it is not known yet,
    /// filtered rules still compete and the result is marked tentative, because the winner
    /// can change once the browser reports the real name.
    /// </summary>
    public RuleMatchResult? Match(IEnumerable<DownloadRule> rules, DownloadMetadata metadata)
        => Match(rules, metadata, previousRuleId: null, previousMatchedUrl: null);

    /// <summary>
    /// Matches again once a tentative job's file name is known. A job stores only sanitized
    /// addresses, so the rule that matched the full addresses at creation keeps counting as an
    /// address match even when its sanitized address no longer matches it.
    /// </summary>
    public RuleMatchResult? Rematch(
        IEnumerable<DownloadRule> rules,
        DownloadMetadata metadata,
        Guid previousRuleId,
        string? previousMatchedUrl)
        => Match(rules, metadata, previousRuleId, previousMatchedUrl);

    private static RuleMatchResult? Match(
        IEnumerable<DownloadRule> rules,
        DownloadMetadata metadata,
        Guid? previousRuleId,
        string? previousMatchedUrl)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(metadata);

        var fileNameKnown = FileTypeFilter.IsFileNameKnown(metadata.OriginalFileName);
        var enabled = rules.Where(static rule => rule.IsEnabled).ToList();
        var candidates = enabled
            .SelectMany(rule => CandidateUrls(rule, metadata)
                .Select(candidate => TryMatch(rule, candidate.Url)
                    ? new Candidate(rule, candidate.Url, candidate.Source, Specificity(rule))
                    : null))
            .Where(static candidate => candidate is not null)
            .Cast<Candidate>()
            .ToList();
        if (previousRuleId is { } previousId
            && candidates.All(candidate => candidate.Rule.Id != previousId)
            && enabled.FirstOrDefault(rule => rule.Id == previousId) is { } previous)
        {
            candidates.Add(new Candidate(previous, previousMatchedUrl ?? string.Empty, "PreviousMatch", Specificity(previous)));
        }

        var isTentative = !fileNameKnown
            && candidates.Any(static candidate => candidate.Rule.FileTypeMode != FileTypeFilterMode.AllFiles);

        return candidates
            .Where(candidate => !fileNameKnown || FileTypeFilter.Matches(
                candidate.Rule.FileTypeMode,
                candidate.Rule.FileExtensions,
                metadata.OriginalFileName))
            .OrderByDescending(static candidate => candidate.Specificity.TypeRank)
            .ThenByDescending(static candidate => candidate.Specificity.ValueLength)
            .ThenByDescending(static candidate => candidate.Rule.Priority)
            .ThenBy(static candidate => candidate.Rule.ListOrder)
            .ThenBy(static candidate => candidate.Rule.CreatedAt)
            .ThenBy(static candidate => candidate.Rule.Id)
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
