using DownloadRouter.Core.Paths;

namespace DownloadRouter.Core.Tests;

public sealed class StorageRootHealthTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("dr-storage-health");

    [Fact]
    public async Task AnExistingWritableFolderIsAvailableAndTheProbeLeavesNothingBehind()
    {
        Assert.Equal(StorageRootStatus.Available, await StorageRootHealth.CheckAsync(directory.FullName, probeWrite: false, Timeout));
        Assert.Equal(StorageRootStatus.Available, await StorageRootHealth.CheckAsync(directory.FullName, probeWrite: true, Timeout));
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.FullName));
    }

    [Fact]
    public async Task AMissingFolderOnAnExistingDriveIsMissing()
    {
        var missing = Path.Combine(directory.FullName, "not-there", "deeper");

        Assert.Equal(StorageRootStatus.Missing, await StorageRootHealth.CheckAsync(missing, probeWrite: true, Timeout));
        Assert.False(Directory.Exists(missing));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative\\folder")]
    [InlineData("C:relative")]
    public async Task APathThatIsNotAbsoluteIsInvalid(string path)
    {
        Assert.Equal(StorageRootStatus.Invalid, await StorageRootHealth.CheckAsync(path, probeWrite: false, Timeout));
    }

    [Fact]
    public async Task AFileInsteadOfAFolderIsInvalid()
    {
        var file = Path.Combine(directory.FullName, "file.txt");
        await File.WriteAllTextAsync(file, "x");

        Assert.Equal(StorageRootStatus.Invalid, await StorageRootHealth.CheckAsync(file, probeWrite: true, Timeout));
    }

    [Fact]
    public async Task AnUnmappedDriveIsUnreachable()
    {
        var unused = Enumerable.Range('D', 'Z' - 'D' + 1)
            .Select(static letter => $"{(char)letter}:\\")
            .FirstOrDefault(static root => !Directory.Exists(root));
        if (unused is null)
        {
            return;
        }

        Assert.Equal(
            StorageRootStatus.Unreachable,
            await StorageRootHealth.CheckAsync(Path.Combine(unused, "Routed"), probeWrite: false, Timeout));
    }

    [Fact]
    public async Task AStuckCheckTimesOutAsUnreachableWithoutWaitingForIt()
    {
        using var release = new ManualResetEventSlim(false);
        var started = DateTime.UtcNow;

        var status = await StorageRootHealth.CheckAsync(
            () =>
            {
                release.Wait(TimeSpan.FromSeconds(30));
                return StorageRootStatus.Available;
            },
            TimeSpan.FromMilliseconds(100),
            CancellationToken.None);

        Assert.Equal(StorageRootStatus.Unreachable, status);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
        release.Set();
    }

    [Fact]
    public async Task CancellationIsObserved()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => StorageRootHealth.CheckAsync(directory.FullName, probeWrite: false, Timeout, cancellation.Token));
    }

    [Fact]
    public void EveryStatusHasADescriptionThatNamesThePath()
    {
        foreach (var status in Enum.GetValues<StorageRootStatus>())
        {
            var description = StorageRootHealth.Describe(status, "D:\\Routed");
            Assert.Contains("D:\\Routed", description, StringComparison.Ordinal);
        }

        Assert.Contains("권한", StorageRootHealth.Describe(StorageRootStatus.AccessDenied, "D:\\Routed"), StringComparison.Ordinal);
        Assert.Contains("연결", StorageRootHealth.Describe(StorageRootStatus.Unreachable, "D:\\Routed"), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (directory.Exists)
        {
            directory.Delete(recursive: true);
        }
    }
}
