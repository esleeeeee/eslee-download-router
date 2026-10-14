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

    [Fact]
    public void AKnownNameThatAFilterIncludesSelectsTheFilteredRule()
    {
        var general = Rule(RuleMatchType.DomainAndSubdomains, "example.com");
        var documents = Rule(RuleMatchType.ExactHost, "files.example.com") with
        {
            FileTypeMode = FileTypeFilterMode.OnlyListed,
            FileExtensions = ".pdf;.docx",
        };

        var result = matcher.Match([general, documents], Metadata("https://files.example.com/page") with { OriginalFileName = "Report.PDF" });

        Assert.NotNull(result);
        Assert.Equal(documents.Id, result.Rule.Id);
        Assert.False(result.IsTentative);
    }

    [Fact]
    public void AKnownNameThatAFilterExcludesFallsBackToTheNextRuleOrNothing()
    {
        var general = Rule(RuleMatchType.DomainAndSubdomains, "example.com");
        var noInstallers = Rule(RuleMatchType.ExactHost, "files.example.com") with
        {
            FileTypeMode = FileTypeFilterMode.ExceptListed,
            FileExtensions = ".exe;.msi",
        };
        var metadata = Metadata("https://files.example.com/page") with { OriginalFileName = "setup.exe" };

        var fallback = matcher.Match([general, noInstallers], metadata);
        var none = matcher.Match([noInstallers], metadata);

        Assert.NotNull(fallback);
        Assert.Equal(general.Id, fallback.Rule.Id);
        Assert.False(fallback.IsTentative);
        Assert.Null(none);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Unconfirmed 1.crdownload")]
    public void AnUnknownNameLetsFilteredRulesCompeteAndMarksTheResultTentative(string fileName)
    {
        var general = Rule(RuleMatchType.DomainAndSubdomains, "example.com");
        var documents = Rule(RuleMatchType.ExactHost, "files.example.com") with
        {
            FileTypeMode = FileTypeFilterMode.OnlyListed,
            FileExtensions = ".pdf",
        };

        var result = matcher.Match([general, documents], Metadata("https://files.example.com/page") with { OriginalFileName = fileName });

        Assert.NotNull(result);
        Assert.Equal(documents.Id, result.Rule.Id);
        Assert.True(result.IsTentative);
    }

    [Fact]
    public void AnUnknownNameIsTentativeEvenWhenTheFilteredRuleDoesNotWin()
    {
        var exact = Rule(RuleMatchType.ExactHost, "files.example.com");
        var filteredParent = Rule(RuleMatchType.DomainAndSubdomains, "example.com") with
        {
            FileTypeMode = FileTypeFilterMode.ExceptListed,
            FileExtensions = ".exe",
        };
        var disabledFiltered = Rule(RuleMatchType.UrlContains, "files") with
        {
            IsEnabled = false,
            FileTypeMode = FileTypeFilterMode.OnlyListed,
            FileExtensions = ".pdf",
        };
        var metadata = Metadata("https://files.example.com/page") with { OriginalFileName = "" };

        var withFilter = matcher.Match([exact, filteredParent], metadata);
        var onlyDisabledFilter = matcher.Match([exact, disabledFiltered], metadata);

        Assert.Equal(exact.Id, withFilter!.Rule.Id);
        Assert.True(withFilter.IsTentative);
        Assert.False(onlyDisabledFilter!.IsTentative);
    }

    [Fact]
    public void AnUnknownNameWithoutAnyFilteredRuleIsNotTentative()
    {
        var rule = Rule(RuleMatchType.DomainAndSubdomains, "example.com");

        var result = matcher.Match([rule], Metadata("https://example.com/page") with { OriginalFileName = "" });

        Assert.False(result!.IsTentative);
    }

    [Fact]
    public void RematchKeepsThePreviousRuleWhenOnlyItsFullAddressMatched()
    {
        // Created from a query-string match that the stored, sanitized address no longer has.
        var queryRule = Rule(RuleMatchType.UrlContains, "board=7") with
        {
            FileTypeMode = FileTypeFilterMode.OnlyListed,
            FileExtensions = ".pdf",
        };
        var metadata = Metadata("https://example.com/view") with { OriginalFileName = "notes.pdf" };

        Assert.Null(matcher.Match([queryRule], metadata));
        var rematch = matcher.Rematch([queryRule], metadata, queryRule.Id, "https://example.com/view");
        Assert.Equal(queryRule.Id, rematch!.Rule.Id);
        Assert.Null(matcher.Rematch([queryRule], metadata with { OriginalFileName = "setup.exe" }, queryRule.Id, null));
        Assert.Null(matcher.Rematch([queryRule with { IsEnabled = false }], metadata, queryRule.Id, null));
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
