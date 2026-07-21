using DownloadRouter.Core.Models;
using DownloadRouter.Core.Rules;

namespace DownloadRouter.Core.Tests;

public sealed class RuleMatcherTests
{
    private readonly RuleMatcher matcher = new();

    [Theory]
    [InlineData("https://naver.com/file", true)]
    [InlineData("https://blog.naver.com/file", true)]
    [InlineData("https://m.blog.naver.com/file", true)]
    [InlineData("https://notnaver.com/file", false)]
    [InlineData("https://naver.com.example.org/file", false)]
    public void DomainRuleMatchesOnlyRootAndLabelBoundedSubdomains(string url, bool expected)
    {
        var rule = Rule(RuleMatchType.DomainAndSubdomains, "naver.com");
        Assert.Equal(expected, matcher.IsMatch(rule, url));
    }

    [Fact]
    public void ExactHostWinsOverParentDomainRegardlessOfParentPriority()
    {
        var parent = Rule(RuleMatchType.DomainAndSubdomains, "naver.com", priority: 999);
        var exact = Rule(RuleMatchType.ExactHost, "blog.naver.com", priority: 0);
        var metadata = Metadata("https://blog.naver.com/post");

        var result = matcher.Match([parent, exact], metadata);

        Assert.NotNull(result);
        Assert.Equal(exact.Id, result.Rule.Id);
    }

    [Fact]
    public void LongerDomainWinsWithinDomainRules()
    {
        var parent = Rule(RuleMatchType.DomainAndSubdomains, "naver.com");
        var child = Rule(RuleMatchType.DomainAndSubdomains, "blog.naver.com");

        var result = matcher.Match([parent, child], Metadata("https://blog.naver.com/post"));

        Assert.NotNull(result);
        Assert.Equal(child.Id, result.Rule.Id);
    }

    [Fact]
    public void InitiatingPageFallsBackToReferrerButNotCdnFileUrl()
    {
        var rule = Rule(RuleMatchType.DomainAndSubdomains, "portal.example", target: RuleMatchTarget.InitiatingPage);
        var metadata = Metadata(null) with
        {
            ReferrerUrl = "https://portal.example/course",
            InitialUrl = "https://cdn.example/file",
        };

        var result = matcher.Match([rule], metadata);

        Assert.NotNull(result);
        Assert.Equal(nameof(DownloadMetadata.ReferrerUrl), result.SourceField);
    }

    [Fact]
    public void InternationalizedDomainIsNormalizedToPunycode()
    {
        var rule = Rule(RuleMatchType.DomainAndSubdomains, "예시.테스트");
        Assert.True(matcher.IsMatch(rule, "https://xn--vv4b11d.xn--9t4b11yi5a/file"));
    }

    private static DownloadRule Rule(
        RuleMatchType type,
        string value,
        int priority = 0,
        RuleMatchTarget target = RuleMatchTarget.InitiatingPage)
    {
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        return new DownloadRule(Guid.NewGuid(), value, true, type, value, target, "{Downloads}", StorageMode.Automatic, priority, 0, now, now);
    }

    private static DownloadMetadata Metadata(string? initiatingPage)
        => new(BrowserKind.Edge, "1", "file.pdf", null, initiatingPage, null, null, null, DateTimeOffset.UtcNow);
}
