using DownloadRouter.Core.Models;
using DownloadRouter.Core.Paths;
using DownloadRouter.Core.Rules;

namespace DownloadRouter.Core.Tests;

public sealed class FileTypeFilterTests
{
    [Fact]
    public void NormalizeProducesLowercaseDotPrefixedDeduplicatedEntriesInInputOrder()
    {
        Assert.Equal(".pdf;.zip;.jpg;.tar.gz", FileTypeFilter.Normalize("pdf, .ZIP; jpg tar.gz"));
        Assert.Equal(".pdf;.zip", FileTypeFilter.Normalize(" .pdf;;PDF ,zip\t.Zip "));
        Assert.Equal(".c++;.my_ext;.a-b", FileTypeFilter.Normalize("c++ my_ext a-b"));
        Assert.Equal(string.Empty, FileTypeFilter.Normalize("  , ; "));
        Assert.Equal(string.Empty, FileTypeFilter.Normalize(null));
    }

    [Theory]
    [InlineData("pdf exe!")]
    [InlineData("..pdf")]
    [InlineData("tar..gz")]
    [InlineData("pdf.")]
    [InlineData(".")]
    [InlineData("한글")]
    [InlineData("file/name")]
    [InlineData("abcdefghijklmnopq")]
    public void InvalidEntriesAreRejected(string input)
    {
        Assert.Throws<ArgumentException>(() => FileTypeFilter.Normalize(input));
    }

    [Fact]
    public void SixteenCharactersAndThirtyEntriesAreTheLimits()
    {
        Assert.Equal(".abcdefghijklmnop", FileTypeFilter.Normalize("abcdefghijklmnop"));
        var thirty = string.Join(' ', Enumerable.Range(0, 30).Select(index => $"e{index}"));
        Assert.Equal(30, FileTypeFilter.Parse(thirty).Count);
        Assert.Throws<ArgumentException>(() => FileTypeFilter.Normalize(thirty + " e30"));
        // Duplicates do not count toward the limit.
        Assert.Equal(30, FileTypeFilter.Parse(thirty + " E0 .e1").Count);
    }

    [Theory]
    [InlineData(FileTypeFilterMode.AllFiles, "", "anything.exe", true)]
    [InlineData(FileTypeFilterMode.OnlyListed, ".pdf;.zip", "Report.PDF", true)]
    [InlineData(FileTypeFilterMode.OnlyListed, ".pdf;.zip", "setup.exe", false)]
    [InlineData(FileTypeFilterMode.OnlyListed, ".pdf", "file.notpdf", false)]
    [InlineData(FileTypeFilterMode.OnlyListed, ".tar.gz", "archive.tar.gz", true)]
    [InlineData(FileTypeFilterMode.OnlyListed, ".tar.gz", "archive.gz", false)]
    [InlineData(FileTypeFilterMode.OnlyListed, ".gz", "archive.tar.gz", true)]
    [InlineData(FileTypeFilterMode.ExceptListed, ".exe;.msi", "setup.exe", false)]
    [InlineData(FileTypeFilterMode.ExceptListed, ".exe;.msi", "photo.jpg", true)]
    [InlineData(FileTypeFilterMode.OnlyListed, "", "photo.jpg", false)]
    [InlineData(FileTypeFilterMode.ExceptListed, "", "photo.jpg", true)]
    public void MatchesUsesACaseInsensitiveSuffix(FileTypeFilterMode mode, string extensions, string fileName, bool expected)
    {
        Assert.Equal(expected, FileTypeFilter.Matches(mode, extensions, fileName));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("download", false)]
    [InlineData("Unconfirmed 123.crdownload", false)]
    [InlineData("file.partial", false)]
    [InlineData("report.pdf", true)]
    [InlineData("C:\\Downloads\\report.pdf", true)]
    public void OnlyARealNameCountsAsKnown(string? fileName, bool expected)
    {
        Assert.Equal(expected, FileTypeFilter.IsFileNameKnown(fileName));
    }

    [Fact]
    public void ValidationRequiresExtensionsForAFilteredRuleAndStoresTheNormalizedForm()
    {
        var resolver = new PathTokenResolver(new WindowsKnownPathProvider());
        var now = DateTimeOffset.UtcNow;
        var rule = new DownloadRule(
            Guid.NewGuid(), "docs", true, RuleMatchType.DomainAndSubdomains, "example.com",
            RuleMatchTarget.InitiatingPage, "{Downloads}\\docs", StorageMode.Automatic, 0, 0, now, now,
            FileTypeFilterMode.OnlyListed, "PDF, docx");

        Assert.Equal(".pdf;.docx", RuleValidation.Normalize(rule, resolver).FileExtensions);
        Assert.Throws<ArgumentException>(() => RuleValidation.Normalize(rule with { FileExtensions = " " }, resolver));
        Assert.Throws<ArgumentException>(() => RuleValidation.Normalize(
            rule with { FileTypeMode = FileTypeFilterMode.ExceptListed, FileExtensions = "" }, resolver));
        Assert.Throws<ArgumentException>(() => RuleValidation.Normalize(rule with { FileTypeMode = (FileTypeFilterMode)9 }, resolver));
        Assert.Throws<ArgumentException>(() => RuleValidation.Normalize(rule with { FileExtensions = "bad!" }, resolver));
        Assert.Equal(string.Empty, RuleValidation.Normalize(
            rule with { FileTypeMode = FileTypeFilterMode.AllFiles, FileExtensions = "" }, resolver).FileExtensions);
        Assert.Throws<ArgumentException>(() => RuleValidation.Normalize(rule with { Name = " " }, resolver));
        Assert.Throws<ArgumentException>(() => RuleValidation.Normalize(rule with { StorageRoot = "{Unknown}\\x" }, resolver));
    }

    [Fact]
    public void ExistingPositionalConstructorsStillDefaultToNoFilter()
    {
        var now = DateTimeOffset.UtcNow;
        var rule = new DownloadRule(
            Guid.NewGuid(), "legacy", true, RuleMatchType.ExactHost, "example.com",
            RuleMatchTarget.FileUrl, "D:\\x", StorageMode.Automatic, 0, 0, now, now);

        Assert.Equal(FileTypeFilterMode.AllFiles, rule.FileTypeMode);
        Assert.Equal(string.Empty, rule.FileExtensions);
    }
}
