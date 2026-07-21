using DownloadRouter.Core.Paths;
using DownloadRouter.Core.Privacy;

namespace DownloadRouter.Core.Tests;

public sealed class PathAndPrivacyTests : IDisposable
{
    private readonly string temporaryRoot = Path.Combine(Path.GetTempPath(), "download-router-core-tests", Guid.NewGuid().ToString("N"));

    public PathAndPrivacyTests()
    {
        Directory.CreateDirectory(temporaryRoot);
    }

    [Fact]
    public void ResolvesAndTokenizesKnownPathsWithoutMachineSpecificConstants()
    {
        var provider = new FakeKnownPathProvider(temporaryRoot);
        var resolver = new PathTokenResolver(provider);

        var resolved = resolver.Resolve("{Documents}\\eslee\\downloads");

        Assert.Equal(Path.Combine(temporaryRoot, "Documents", "eslee", "downloads"), resolved);
        Assert.Equal("{Documents}\\eslee\\downloads", resolver.Tokenize(resolved));
    }

    [Theory]
    [InlineData("..\\outside")]
    [InlineData("child\\..\\..\\outside")]
    public void RejectsRelativeTraversalOutsideRoot(string relative)
    {
        var validator = new PathBoundaryValidator();
        Assert.Throws<UnauthorizedAccessException>(() => validator.ValidateRelativeFolder(temporaryRoot, relative));
    }

    [Fact]
    public void AllowsARegularNestedDirectory()
    {
        var nested = Directory.CreateDirectory(Path.Combine(temporaryRoot, "a", "b")).FullName;
        var validator = new PathBoundaryValidator();
        Assert.Equal(nested, validator.ValidateRelativeFolder(temporaryRoot, "a\\b"));
    }

    [Fact]
    public void RemovesQueryFragmentAndCredentialsFromStoredUrl()
    {
        var sanitizer = new UrlSanitizer();
        var sanitized = sanitizer.Sanitize("https://user:password@example.test/download/report.pdf?token=secret#part");
        Assert.Equal("https://example.test/download/report.pdf", sanitized);
    }

    [Theory]
    [InlineData("blob:https://example.test/id")]
    [InlineData("data:text/plain,secret")]
    [InlineData("not a URL")]
    public void RejectsUnsupportedOrMalformedUrlForHistory(string value)
    {
        Assert.Null(new UrlSanitizer().Sanitize(value));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryRoot))
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private sealed class FakeKnownPathProvider(string root) : IKnownPathProvider
    {
        public IReadOnlyDictionary<string, string> GetKnownPaths()
            => new Dictionary<string, string>
            {
                ["{Documents}"] = Path.Combine(root, "Documents"),
                ["{UserProfile}"] = root,
            };
    }
}
