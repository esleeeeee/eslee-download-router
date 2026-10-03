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

    [Fact]
    public async Task SameFolderIsNoOpButExplicitRenameStillWorks()
    {
        var source = Path.Combine(root, "original.txt");
        await File.WriteAllTextAsync(source, "original");
        var service = new FileMoveService(new PathBoundaryValidator(), NullLogger<FileMoveService>.Instance);
        var unchanged = await service.MoveAsync(source, root, null, true);
        Assert.True(unchanged.Success); Assert.Equal(source, unchanged.DestinationPath);
        Assert.Single(Directory.GetFiles(root));
        var renamed = await service.MoveAsync(source, root, null, true, selectedFileName: "renamed.txt");
        Assert.True(renamed.Success); Assert.Equal("original", await File.ReadAllTextAsync(renamed.DestinationPath!));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public async Task PublishedCopySurvivesCleanupFailureAndRestartWithoutDuplicate()
    {
        var source = Path.Combine(root, "source.txt"); await File.WriteAllTextAsync(source, "verified bytes");
        var destination = Directory.CreateDirectory(Path.Combine(root, "destination")).FullName;
        var failing = new FileMoveService(new PathBoundaryValidator(), NullLogger<FileMoveService>.Instance,
            new FileMoveOperations { SameVolume = (_, _) => false, DeleteSource = _ => throw new IOException("injected deletion failure") });
        var pending = await failing.MoveAsync(source, destination, null, true);
        Assert.False(pending.Success); Assert.True(pending.CanRetry);
        Assert.Equal("file.source-cleanup-pending", pending.ErrorCode);
        Assert.Equal("verified bytes", await File.ReadAllTextAsync(pending.DestinationPath!)); Assert.True(File.Exists(source));
        var journal = Directory.GetFiles(destination, ".eslee-move-*.json").Single();
        using var state = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(journal));
        var staged = state.RootElement.GetProperty("Temporary").GetString()!;
        await File.WriteAllTextAsync(staged, "verified bytes"); // interrupted duplicate staging cleanup
        var restarted = new FileMoveService(new PathBoundaryValidator(), NullLogger<FileMoveService>.Instance);
        var completed = await restarted.MoveAsync(source, destination, null, true);
        Assert.True(completed.Success); Assert.Equal(pending.DestinationPath, completed.DestinationPath);
        Assert.False(File.Exists(source)); Assert.Single(Directory.GetFiles(destination));
    }

    [Fact]
    public async Task ChangedSourceAfterPublicationRequiresInspectionAndPreservesBothFiles()
    {
        var source = Path.Combine(root, "source.txt"); await File.WriteAllTextAsync(source, "original");
        var destination = Directory.CreateDirectory(Path.Combine(root, "destination")).FullName;
        var service = new FileMoveService(new PathBoundaryValidator(), NullLogger<FileMoveService>.Instance,
            new FileMoveOperations { SameVolume = (_, _) => false, DeleteSource = _ => throw new IOException("locked") });
        var pending = await service.MoveAsync(source, destination, null, true);
        await File.WriteAllTextAsync(source, "new user edit");
        var resumed = await service.MoveAsync(source, destination, null, true);
        Assert.False(resumed.Success); Assert.False(resumed.CanRetry);
        Assert.Equal("file.source-changed-after-copy", resumed.ErrorCode);
        Assert.Equal(pending.DestinationPath, resumed.DestinationPath);
        Assert.Equal("original", await File.ReadAllTextAsync(resumed.DestinationPath!));
        Assert.Equal("new user edit", await File.ReadAllTextAsync(source));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
