using DownloadRouter.Core.Paths;
using DownloadRouter.Infrastructure.Files;
using Microsoft.Extensions.Logging.Abstractions;

namespace DownloadRouter.Infrastructure.Tests;

public sealed class FileMoveServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "download-router-file-tests", Guid.NewGuid().ToString("N"));

    public FileMoveServiceTests()
    {
        Directory.CreateDirectory(root);
    }

    [Fact]
    public async Task MovesCompletedFileAndPreservesExistingNameWithSuffix()
    {
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(root, "destination")).FullName;
        await File.WriteAllTextAsync(Path.Combine(destination, "report.pdf"), "existing", CancellationToken.None);
        var source = Path.Combine(sourceDirectory, "report.pdf");
        await File.WriteAllTextAsync(source, "new file", CancellationToken.None);
        var service = new FileMoveService(new PathBoundaryValidator(), NullLogger<FileMoveService>.Instance);

        var result = await service.MoveAsync(source, destination, null, true, CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(Path.Combine(destination, "report (1).pdf"), result.DestinationPath);
        Assert.False(File.Exists(source));
        Assert.Equal("new file", await File.ReadAllTextAsync(result.DestinationPath!, CancellationToken.None));
    }

    [Fact]
    public async Task DoesNotMoveBeforeBrowserReportsCompletion()
    {
        var destination = Directory.CreateDirectory(Path.Combine(root, "destination")).FullName;
        var source = Path.Combine(root, "download.bin");
        await File.WriteAllTextAsync(source, "data", CancellationToken.None);
        var service = new FileMoveService(new PathBoundaryValidator(), NullLogger<FileMoveService>.Instance);

        var result = await service.MoveAsync(source, destination, null, false, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("file.not-complete", result.ErrorCode);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task RejectsTemporaryChromeDownloadExtension()
    {
        var destination = Directory.CreateDirectory(Path.Combine(root, "destination")).FullName;
        var source = Path.Combine(root, "download.crdownload");
        await File.WriteAllTextAsync(source, "data", CancellationToken.None);
        var service = new FileMoveService(new PathBoundaryValidator(), NullLogger<FileMoveService>.Instance);

        var result = await service.MoveAsync(source, destination, null, true, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("file.temporary-extension", result.ErrorCode);
        Assert.True(File.Exists(source));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
