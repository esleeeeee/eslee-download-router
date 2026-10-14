using DownloadRouter.Core.Jobs;
using DownloadRouter.Core.Localization;
using DownloadRouter.Core.Models;
using DownloadRouter.Core.Paths;
using DownloadRouter.Core.Rules;
using DownloadRouter.Core.Updates;

namespace DownloadRouter.Core.Tests;

[Collection(AppTextLanguageCollection.Name)]
public sealed class EnglishTextTests : EnglishTextTestBase
{
    [Fact]
    public void RuleWordingSwitchesToEnglishWithoutExposingEnumNames()
    {
        Assert.True(AppText.IsEnglish);
        Assert.Equal("The page that started the download", RulePresentation.MatchTargetLabel(RuleMatchTarget.InitiatingPage));
        Assert.Equal("Only these file types", RulePresentation.FileTypeModeLabel(FileTypeFilterMode.OnlyListed));
        Assert.Equal("All except these file types: .exe, .msi", RulePresentation.FileTypeModeLabel(FileTypeFilterMode.ExceptListed, "exe;msi"));

        var choices = RulePresentation.MatchTypes.Select(choice => choice.Label + choice.Description + choice.Example)
            .Concat(RulePresentation.MatchTargets.Select(choice => choice.Label + choice.Description + choice.Example))
            .Concat(RulePresentation.StorageModes.Select(choice => choice.Label + choice.Description + choice.Example))
            .Concat(RulePresentation.FileTypeModes.Select(choice => choice.Label + choice.Description + choice.Example));
        foreach (var text in choices)
        {
            Assert.DoesNotMatch("[가-힣]", text);
        }
    }

    [Fact]
    public void RuleSummaryReadsAsOneEnglishSentence()
    {
        var selection = RulePresentation.Summarize(
            RuleMatchType.DomainAndSubdomains,
            "kio.ac",
            RuleMatchTarget.InitiatingPage,
            StorageMode.SelectSubfolder,
            "D:\\Routed");
        var filtered = RulePresentation.Summarize(
            RuleMatchType.ExactHost,
            "files.example.com",
            RuleMatchTarget.FileUrl,
            StorageMode.Automatic,
            "D:\\Routed",
            FileTypeFilterMode.OnlyListed,
            "pdf zip");
        var empty = RulePresentation.Summarize(RuleMatchType.UrlContains, " ", RuleMatchTarget.Either, StorageMode.Automatic, " ");

        Assert.Equal(
            "Downloads started from kio.ac and all its subdomains are moved to a subfolder of D:\\Routed that you choose each time.",
            selection);
        Assert.Equal(
            "Downloads whose file address matches the host files.example.com only, only .pdf, .zip files, are moved to D:\\Routed automatically.",
            filtered);
        Assert.Contains("(no address entered)", empty, StringComparison.Ordinal);
        Assert.Contains("(no base folder chosen)", empty, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendMessagesHaveEnglishText()
    {
        Assert.Contains("Reload eslee Download Router", DownloadRegistrationPolicy.ExtensionRefreshGuidance, StringComparison.Ordinal);
        Assert.Equal("Checking file name…", DownloadPresentation.PendingFileName);
        Assert.Contains("Firefox not supported", BrowserSupportCatalog.SupportSummary, StringComparison.Ordinal);
        Assert.Contains("NAVER Whale", BrowserSupportCatalog.SupportSummary, StringComparison.Ordinal);
        Assert.Equal("Officially supported", BrowserSupportCatalog.Official[0].LevelDescription);
        Assert.Equal(
            "You have the latest version. (2.0.0)",
            UpdateCheckPolicy.DescribeForUser(UpdateStatus.UpToDate, "2.0.0", null));
        Assert.StartsWith(
            "The extension folder was not found",
            ExtensionFolderLocator.DescribeForUser(new ExtensionFolderLocation(ExtensionFolderSource.NotFound, null)),
            StringComparison.Ordinal);
        Assert.Equal("Version 2.0.0", new ProductVersionInfo("p", "2.0.0", "2.0.0", null, false).DisplayVersion);

        var invalidName = Assert.Throws<ArgumentException>(() => SelectionDestination.ValidateFileName("bad|name"));
        Assert.StartsWith("Enter a valid file name", invalidName.Message, StringComparison.Ordinal);
        var invalidType = Assert.Throws<ArgumentException>(() => FileTypeFilter.Normalize("exe!"));
        Assert.Contains("is not valid", invalidType.Message, StringComparison.Ordinal);
        var invalidPause = Assert.Throws<ArgumentException>(() => RoutingPausePolicy.ToStoredValue(0, DateTimeOffset.UtcNow));
        Assert.Contains("between 1 and 1440 minutes", invalidPause.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StorageHealthAndRuleImportSpeakEnglish()
    {
        Assert.Equal(
            "The folder cannot be reached. Check the external drive or network connection. (D:\\Routed)",
            StorageRootHealth.Describe(StorageRootStatus.Unreachable, "D:\\Routed"));

        var json = """{"format":"eslee-download-router.rules","version":1,"rules":[{"name":"x","matchType":"Nope","matchValue":"a.example","storageRoot":"D:\\x","storageMode":"Automatic"}]}""";
        var result = RuleTransfer.Import(json, [], DateTimeOffset.UtcNow);
        Assert.Equal("Rule 1: The 'matchType' field has an invalid value.", Assert.Single(result.Errors));

        var notAnExport = Assert.Throws<ArgumentException>(() => RuleTransfer.Import("{}", [], DateTimeOffset.UtcNow));
        Assert.Equal("This is not a rules file exported from eslee Download Router.", notAnExport.Message);
    }

    [Fact]
    public void TheLanguageIsRestoredToKoreanAfterEachEnglishTest()
    {
        // Runs in the same non-parallel collection; the base class switched to English and
        // its Dispose switches back, which the next test in any collection relies on.
        Assert.True(AppText.IsEnglish);
        AppText.SetLanguage(AppLanguage.Korean);
        Assert.Equal("모든 파일", RulePresentation.FileTypeModeLabel(FileTypeFilterMode.AllFiles));
        AppText.SetLanguage(AppLanguage.English);
    }
}
