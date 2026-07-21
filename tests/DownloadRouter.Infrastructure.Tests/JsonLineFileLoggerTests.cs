using DownloadRouter.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace DownloadRouter.Infrastructure.Tests;

public sealed class JsonLineFileLoggerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "download-router-logger-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void RedactsUrlsPathsTokensAndExceptionMessages()
    {
        using var provider = new JsonLineFileLoggerProvider(root);
        var logger = provider.CreateLogger("privacy-test");
        logger.LogError(
            new IOException("private exception C:\\Users\\person\\Downloads\\secret.txt"),
            "Native failure at {Url} for {Path} with token={Token}",
            "https://example.test/file?token=secret",
            "C:\\Users\\person\\Downloads\\secret.txt",
            "secret");

        var line = File.ReadAllText(Path.Combine(root, "download-router.jsonl"));

        Assert.DoesNotContain("example.test", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\Users", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private exception", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token=secret", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[URL_REDACTED]", line, StringComparison.Ordinal);
        Assert.Contains("[PATH_REDACTED]", line, StringComparison.Ordinal);
        Assert.Contains("IOException", line, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
