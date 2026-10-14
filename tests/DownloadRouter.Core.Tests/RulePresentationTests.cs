using DownloadRouter.Core.Models;
using DownloadRouter.Core.Rules;

namespace DownloadRouter.Core.Tests;

public sealed class RulePresentationTests
{
    [Fact]
    public void EveryRuleEnumValueHasAUserFacingLabelDescriptionAndExample()
    {
        Assert.Equal(Enum.GetValues<RuleMatchType>().Length, RulePresentation.MatchTypes.Count);
        Assert.Equal(Enum.GetValues<RuleMatchTarget>().Length, RulePresentation.MatchTargets.Count);
        Assert.Equal(Enum.GetValues<StorageMode>().Length, RulePresentation.StorageModes.Count);
        Assert.Equal(Enum.GetValues<FileTypeFilterMode>().Length, RulePresentation.FileTypeModes.Count);

        foreach (var value in Enum.GetValues<FileTypeFilterMode>())
        {
            Assert.Single(RulePresentation.FileTypeModes, choice => choice.Value == value);
        }

        foreach (var value in Enum.GetValues<RuleMatchType>())
        {
            Assert.Single(RulePresentation.MatchTypes, choice => choice.Value == value);
        }

        foreach (var value in Enum.GetValues<RuleMatchTarget>())
        {
            Assert.Single(RulePresentation.MatchTargets, choice => choice.Value == value);
        }

        foreach (var value in Enum.GetValues<StorageMode>())
        {
            Assert.Single(RulePresentation.StorageModes, choice => choice.Value == value);
        }

        var all = RulePresentation.MatchTypes.Select(choice => (choice.Label, choice.Description, choice.Example))
            .Concat(RulePresentation.MatchTargets.Select(choice => (choice.Label, choice.Description, choice.Example)))
            .Concat(RulePresentation.StorageModes.Select(choice => (choice.Label, choice.Description, choice.Example)))
            .Concat(RulePresentation.FileTypeModes.Select(choice => (choice.Label, choice.Description, choice.Example)));
        foreach (var (label, description, example) in all)
        {
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.False(string.IsNullOrWhiteSpace(description));
            Assert.False(string.IsNullOrWhiteSpace(example));
        }
    }

    [Fact]
    public void UserFacingTextNeverExposesInternalEnumNames()
    {
        var reserved = new[]
        {
            "DomainAndSubdomains", "ExactHost", "UrlContains",
            "InitiatingPage", "FileUrl", "Either",
            "Automatic", "SelectSubfolder",
            "AllFiles", "OnlyListed", "ExceptListed",
        };

        var text = string.Join(
            '\n',
            RulePresentation.MatchTypes.Select(choice => $"{choice.Label}{choice.Description}{choice.Example}")
                .Concat(RulePresentation.MatchTargets.Select(choice => $"{choice.Label}{choice.Description}{choice.Example}"))
                .Concat(RulePresentation.StorageModes.Select(choice => $"{choice.Label}{choice.Description}{choice.Example}"))
                .Concat(RulePresentation.FileTypeModes.Select(choice => $"{choice.Label}{choice.Description}{choice.Example}"))
                .Append(RulePresentation.Summarize(
                    RuleMatchType.ExactHost,
                    "files.example.com",
                    RuleMatchTarget.FileUrl,
                    StorageMode.Automatic,
                    "D:\\Routed",
                    FileTypeFilterMode.ExceptListed,
                    ".exe"))
                .Append(RulePresentation.Summarize(
                    RuleMatchType.DomainAndSubdomains,
                    "kio.ac",
                    RuleMatchTarget.InitiatingPage,
                    StorageMode.SelectSubfolder,
                    "D:\\Routed")));

        foreach (var name in reserved)
        {
            Assert.DoesNotContain(name, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SummaryDescribesScopeTargetAndDestinationInPlainKorean()
    {
        var summary = RulePresentation.Summarize(
            RuleMatchType.DomainAndSubdomains,
            "kio.ac",
            RuleMatchTarget.InitiatingPage,
            StorageMode.SelectSubfolder,
            "D:\\Routed");

        Assert.Contains("kio.ac와 모든 하위 도메인", summary, StringComparison.Ordinal);
        Assert.Contains("에서 시작한 다운로드를", summary, StringComparison.Ordinal);
        Assert.Contains("D:\\Routed", summary, StringComparison.Ordinal);
        Assert.Contains("매번 선택한 하위 폴더로 이동합니다.", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void AutomaticSummaryDescribesADirectMove()
    {
        var summary = RulePresentation.Summarize(
            RuleMatchType.ExactHost,
            "download.example.com",
            RuleMatchTarget.FileUrl,
            StorageMode.Automatic,
            "D:\\Routed");

        Assert.Contains("download.example.com 호스트만", summary, StringComparison.Ordinal);
        Assert.Contains("의 파일 주소로 받은 다운로드를", summary, StringComparison.Ordinal);
        Assert.Contains("자동 이동합니다.", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingInputIsDescribedInsteadOfRenderingAnEmptySentence()
    {
        var summary = RulePresentation.Summarize(
            RuleMatchType.UrlContains,
            "   ",
            RuleMatchTarget.Either,
            StorageMode.Automatic,
            "   ");

        Assert.Contains("(주소 미입력)", summary, StringComparison.Ordinal);
        Assert.Contains("(기준 폴더 미지정)", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void FileTypeClauseNarrowsTheSummaryAndTheOldOverloadIsUnchanged()
    {
        var only = RulePresentation.Summarize(
            RuleMatchType.DomainAndSubdomains,
            "kio.ac",
            RuleMatchTarget.InitiatingPage,
            StorageMode.Automatic,
            "D:\\Routed",
            FileTypeFilterMode.OnlyListed,
            ".pdf;.zip");
        var except = RulePresentation.Summarize(
            RuleMatchType.DomainAndSubdomains,
            "kio.ac",
            RuleMatchTarget.InitiatingPage,
            StorageMode.Automatic,
            "D:\\Routed",
            FileTypeFilterMode.ExceptListed,
            "exe");
        var all = RulePresentation.Summarize(
            RuleMatchType.DomainAndSubdomains,
            "kio.ac",
            RuleMatchTarget.InitiatingPage,
            StorageMode.Automatic,
            "D:\\Routed",
            FileTypeFilterMode.AllFiles,
            ".pdf");

        Assert.Contains("에서 시작한 다운로드 가운데 .pdf, .zip 파일만 D:\\Routed 로 자동 이동합니다.", only, StringComparison.Ordinal);
        Assert.Contains(".exe 파일을 제외한 나머지를", except, StringComparison.Ordinal);
        Assert.Equal(
            RulePresentation.Summarize(
                RuleMatchType.DomainAndSubdomains, "kio.ac", RuleMatchTarget.InitiatingPage, StorageMode.Automatic, "D:\\Routed"),
            all);
        Assert.Contains("에서 시작한 다운로드를 D:\\Routed 로 자동 이동합니다.", all, StringComparison.Ordinal);
        Assert.Equal("지정한 형식만", RulePresentation.FileTypeModeLabel(FileTypeFilterMode.OnlyListed));
        Assert.Equal("지정한 형식만: .pdf, .zip", RulePresentation.FileTypeModeLabel(FileTypeFilterMode.OnlyListed, ".pdf;.zip"));
        Assert.Equal("모든 파일", RulePresentation.FileTypeModeLabel(FileTypeFilterMode.AllFiles, ".pdf"));
        Assert.Contains("(형식 미입력)", RulePresentation.FileTypeModeLabel(FileTypeFilterMode.ExceptListed, " "), StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultsMatchTheRecommendedRuleShape()
    {
        Assert.Equal(RuleMatchType.DomainAndSubdomains, RulePresentation.DefaultMatchType);
        Assert.Equal(RuleMatchTarget.InitiatingPage, RulePresentation.DefaultMatchTarget);
        Assert.Equal(StorageMode.Automatic, RulePresentation.DefaultStorageMode);
        Assert.Equal(FileTypeFilterMode.AllFiles, RulePresentation.DefaultFileTypeMode);
        Assert.Equal("다운로드를 시작한 웹페이지", RulePresentation.MatchTargetLabel(RulePresentation.DefaultMatchTarget));
    }
}
