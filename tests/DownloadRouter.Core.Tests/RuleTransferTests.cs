using System.Text.Json;
using System.Text.Json.Nodes;
using DownloadRouter.Core.Models;
using DownloadRouter.Core.Rules;

namespace DownloadRouter.Core.Tests;

public sealed class RuleTransferTests
{
    private static readonly DateTimeOffset Then = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-14T03:00:00Z");

    [Fact]
    public void ExportWritesOnlyPortableFieldsWithEnumNames()
    {
        var rule = Rule("Docs", "example.com", "{Downloads}\\Docs") with
        {
            FileTypeMode = FileTypeFilterMode.OnlyListed,
            FileExtensions = ".pdf;.zip",
            Priority = 5,
            IsEnabled = false,
        };

        var json = RuleTransfer.Export([rule], Now);
        var document = JsonNode.Parse(json)!.AsObject();

        Assert.Equal("eslee-download-router.rules", document["format"]!.GetValue<string>());
        Assert.Equal(1, document["version"]!.GetValue<int>());
        Assert.Equal(Now, document["exportedAt"]!.GetValue<DateTimeOffset>());
        var exported = Assert.Single(document["rules"]!.AsArray())!.AsObject();
        Assert.Equal(
            ["name", "isEnabled", "matchType", "matchValue", "matchTarget", "storageRoot", "storageMode", "fileTypeMode", "fileExtensions", "priority"],
            exported.Select(static property => property.Key).ToArray());
        Assert.Equal("DomainAndSubdomains", exported["matchType"]!.GetValue<string>());
        Assert.Equal("InitiatingPage", exported["matchTarget"]!.GetValue<string>());
        Assert.Equal("Automatic", exported["storageMode"]!.GetValue<string>());
        Assert.Equal("OnlyListed", exported["fileTypeMode"]!.GetValue<string>());
        Assert.False(exported["isEnabled"]!.GetValue<bool>());
        Assert.DoesNotContain(rule.Id.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("createdAt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("listOrder", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ARoundTripCreatesNewRulesAfterTheExistingOnes()
    {
        var existing = new[] { Rule("Kept", "kept.example", "{Downloads}\\Kept") with { ListOrder = 7 } };
        var exported = new[]
        {
            Rule("Docs", "example.com", "{Downloads}\\Docs") with { FileTypeMode = FileTypeFilterMode.ExceptListed, FileExtensions = ".exe" },
            Rule("Board", "/board/", "{Downloads}\\Board") with { MatchType = RuleMatchType.UrlContains, StorageMode = StorageMode.SelectSubfolder },
        };

        var result = RuleTransfer.Import(RuleTransfer.Export(exported, Now), existing, Now);

        Assert.Empty(result.Errors);
        Assert.Empty(result.SkippedDuplicates);
        Assert.Equal(2, result.NewRules.Count);
        Assert.All(result.NewRules, rule =>
        {
            Assert.DoesNotContain(rule.Id, exported.Select(static source => source.Id));
            Assert.NotEqual(Guid.Empty, rule.Id);
            Assert.Equal(Now, rule.CreatedAt);
            Assert.Equal(Now, rule.UpdatedAt);
        });
        Assert.Equal([8, 9], result.NewRules.Select(static rule => rule.ListOrder));
        Assert.Equal(FileTypeFilterMode.ExceptListed, result.NewRules[0].FileTypeMode);
        Assert.Equal(".exe", result.NewRules[0].FileExtensions);
        Assert.Equal(StorageMode.SelectSubfolder, result.NewRules[1].StorageMode);
        Assert.Equal(RuleMatchType.UrlContains, result.NewRules[1].MatchType);
    }

    [Fact]
    public void DuplicatesOfExistingRulesOrEarlierEntriesAreSkipped()
    {
        var existing = new[] { Rule("Original", "Example.COM", "D:\\Routed") };
        var json = Envelope(
            Entry("Same target different name", "example.com.", "d:\\routed\\"),
            Entry("New", "other.example", "D:\\Routed"),
            Entry("New again", "OTHER.example", "D:\\ROUTED"),
            Entry("Different filter", "example.com", "D:\\Routed", fileTypeMode: "OnlyListed", fileExtensions: "pdf"),
            Entry("Same filter", "example.com", "D:\\Routed", fileTypeMode: "OnlyListed", fileExtensions: ".PDF"));

        var result = RuleTransfer.Import(json, existing, Now);

        Assert.Empty(result.Errors);
        Assert.Equal(["New", "Different filter"], result.NewRules.Select(static rule => rule.Name));
        Assert.Equal(["Same target different name", "New again", "Same filter"], result.SkippedDuplicates);
        Assert.Equal(".pdf", result.NewRules[1].FileExtensions);
    }

    [Fact]
    public void EachBadEntryReportsOneErrorAndTheRestStillImport()
    {
        var json = Envelope(
            Entry("Good", "good.example", "D:\\Routed"),
            Entry(" ", "blank.example", "D:\\Routed"),
            Entry("Bad type", "bad.example", "D:\\Routed", matchType: "Regex"),
            Entry("Numeric", "num.example", "D:\\Routed", matchType: "7"),
            Entry("Filter without types", "f.example", "D:\\Routed", fileTypeMode: "OnlyListed"),
            Entry("Bad types", "t.example", "D:\\Routed", fileTypeMode: "OnlyListed", fileExtensions: "exe!"),
            Entry("Token", "token.example", "{Nowhere}\\x"),
            "42",
            """{"name":"Missing value","matchType":"ExactHost","storageRoot":"D:\\x","storageMode":"Automatic"}""");

        var result = RuleTransfer.Import(json, [], Now);

        Assert.Equal(["Good"], result.NewRules.Select(static rule => rule.Name));
        Assert.Equal(8, result.Errors.Count);
        Assert.StartsWith("2번째 규칙:", result.Errors[0], StringComparison.Ordinal);
        Assert.StartsWith("9번째 규칙:", result.Errors[7], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"format":"something-else","version":1,"rules":[]}""")]
    [InlineData("""{"format":"eslee-download-router.rules","version":2,"rules":[]}""")]
    [InlineData("""{"format":"eslee-download-router.rules","version":"1","rules":[]}""")]
    [InlineData("""{"format":"eslee-download-router.rules","version":1,"rules":{}}""")]
    [InlineData("""{"format":"eslee-download-router.rules","version":1}""")]
    public void AFileThatIsNotASupportedExportIsRejectedWhole(string json)
    {
        Assert.Throws<ArgumentException>(() => RuleTransfer.Import(json, [], Now));
    }

    [Fact]
    public void OversizedFilesAndTooManyRulesAreRejected()
    {
        var tooMany = Envelope(Enumerable.Range(0, RuleTransfer.MaximumRules + 1)
            .Select(index => Entry($"r{index}", $"r{index}.example", "D:\\Routed"))
            .ToArray());
        var atLimit = Envelope(Enumerable.Range(0, RuleTransfer.MaximumRules)
            .Select(index => Entry($"r{index}", $"r{index}.example", "D:\\Routed"))
            .ToArray());
        var oversized = "{\"format\":\"eslee-download-router.rules\",\"version\":1,\"rules\":[],\"padding\":\""
            + new string('x', RuleTransfer.MaximumBytes) + "\"}";

        Assert.Throws<ArgumentException>(() => RuleTransfer.Import(tooMany, [], Now));
        Assert.Equal(RuleTransfer.MaximumRules, RuleTransfer.Import(atLimit, [], Now).NewRules.Count);
        Assert.Throws<ArgumentException>(() => RuleTransfer.Import(oversized, [], Now));
    }

    private static DownloadRule Rule(string name, string matchValue, string storageRoot)
        => new(Guid.NewGuid(), name, true, RuleMatchType.DomainAndSubdomains, matchValue,
            RuleMatchTarget.InitiatingPage, storageRoot, StorageMode.Automatic, 0, 0, Then, Then);

    private static string Envelope(params string[] entries)
        => $$"""{"format":"eslee-download-router.rules","version":1,"exportedAt":"2026-10-01T00:00:00Z","rules":[{{string.Join(',', entries)}}]}""";

    private static string Entry(
        string name,
        string matchValue,
        string storageRoot,
        string matchType = "DomainAndSubdomains",
        string fileTypeMode = "AllFiles",
        string fileExtensions = "")
        => JsonSerializer.Serialize(new
        {
            name,
            isEnabled = true,
            matchType,
            matchValue,
            matchTarget = "InitiatingPage",
            storageRoot,
            storageMode = "Automatic",
            fileTypeMode,
            fileExtensions,
            priority = 0,
        });
}
