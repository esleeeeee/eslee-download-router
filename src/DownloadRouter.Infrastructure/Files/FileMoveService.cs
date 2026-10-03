using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DownloadRouter.Core.Paths;
using Microsoft.Extensions.Logging;

namespace DownloadRouter.Infrastructure.Files;

public sealed record FileMoveResult(
    bool Success,
    string? DestinationPath,
    string? ErrorCode,
    string? ErrorMessage,
    bool CanRetry);

public sealed class FileMoveService(
    PathBoundaryValidator boundaryValidator,
    ILogger<FileMoveService> logger,
    FileMoveOperations? operations = null)
{
    private sealed record Publication(string Source, string Destination, string Temporary, string Hash);
    private readonly FileMoveOperations _operations = operations ?? new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DestinationLocks = new(StringComparer.OrdinalIgnoreCase);

    public async Task<FileMoveResult> MoveAsync(
        string sourcePath,
        string storageRoot,
        string? selectedRelativeFolder,
        bool browserReportedComplete,
        CancellationToken cancellationToken = default,
        string? selectedFileName = null,
        bool validateSelectedRoot = false)
    {
        try
        {
            if (!browserReportedComplete)
            {
                return Failure("file.not-complete", "The browser has not reported this download as complete.", true);
            }

            var sourceFull = Path.GetFullPath(sourcePath);
            if (IsTemporaryDownloadFile(sourceFull))
            {
                return Failure("file.temporary-extension", "The browser is still using a temporary download extension.", true);
            }

            if (validateSelectedRoot)
                SelectionDestination.ValidateFolder(storageRoot);
            var destinationDirectory = boundaryValidator.ValidateRelativeFolder(storageRoot, selectedRelativeFolder ?? string.Empty);
            if (!Directory.Exists(destinationDirectory))
            {
                return Failure("destination.missing", "The destination folder does not exist.", false);
            }

            var safeFileName = selectedFileName is null
                ? ValidateFileName(Path.GetFileName(sourceFull))
                : SelectionDestination.ValidateFileName(selectedFileName);
            var gate = DestinationLocks.GetOrAdd(destinationDirectory, static _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var requested = Path.Combine(destinationDirectory, safeFileName);
                boundaryValidator.EnsureWithin(storageRoot, requested);
                var journal = Path.Combine(destinationDirectory, ".eslee-move-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceFull.ToUpperInvariant() + "|" + requested.ToUpperInvariant()))) + ".json");
                if (File.Exists(journal))
                {
                    var pending = JsonSerializer.Deserialize<Publication>(await File.ReadAllTextAsync(journal, cancellationToken)) ?? throw new IOException("Invalid move recovery record.");
                    if (!string.Equals(pending.Source, sourceFull, StringComparison.OrdinalIgnoreCase) || Path.GetDirectoryName(pending.Destination) != destinationDirectory || Path.GetDirectoryName(pending.Temporary) != destinationDirectory || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(pending.Temporary), @"^\.eslee-[0-9a-f]{32}\.partial$") || pending.Temporary == pending.Destination)
                        throw new IOException("Move recovery path mismatch.");
                    boundaryValidator.EnsureWithin(storageRoot, pending.Destination);
                    return await FinishPublicationAsync(pending, journal, cancellationToken);
                }
                if (!File.Exists(sourceFull)) return Failure("file.source-missing", "The downloaded file no longer exists.", false);
                if (sourceFull.Equals(requested, StringComparison.OrdinalIgnoreCase))
                    return new FileMoveResult(true, sourceFull, null, null, false);
                if (!await WaitForStableFileAsync(sourceFull, cancellationToken).ConfigureAwait(false))
                    return Failure("file.locked-or-changing", "The file is still changing or locked by another process.", true);
                var destination = FindAvailableDestination(destinationDirectory, safeFileName);
                boundaryValidator.EnsureWithin(storageRoot, destination);

                if (sourceFull.Equals(destination, StringComparison.OrdinalIgnoreCase))
                {
                    return new FileMoveResult(true, destination, null, null, false);
                }

                if (_operations.SameVolume(sourceFull, destination))
                {
                    File.Move(sourceFull, destination, overwrite: false);
                }
                else
                {
                    return await CopyAcrossVolumesAsync(sourceFull, destination, journal, cancellationToken).ConfigureAwait(false);
                }

                logger.LogInformation("File move completed");
                return new FileMoveResult(true, destination, null, null, false);
            }
            finally
            {
                gate.Release();
            }
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(exception, "File move was rejected by a path or permission boundary");
            return Failure("security.path-or-permission", exception.Message, false);
        }
        catch (PathTooLongException exception)
        {
            return Failure("path.too-long", exception.Message, false);
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "File move failed with an I/O error");
            return Failure("file.io", exception.Message, true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "File move failed unexpectedly");
            return Failure("file.unexpected", exception.Message, false);
        }
    }

    private static async Task<bool> WaitForStableFileAsync(string path, CancellationToken cancellationToken)
    {
        const int requiredStableSamples = 3;
        const int maximumSamples = 60;
        long? previousLength = null;
        var stableSamples = 0;

        for (var sample = 0; sample < maximumSamples; sample++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(path);
                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 1,
                    FileOptions.Asynchronous);

                stableSamples = previousLength == info.Length ? stableSamples + 1 : 0;
                previousLength = info.Length;
                if (stableSamples >= requiredStableSamples)
                {
                    return true;
                }
            }
            catch (IOException)
            {
                stableSamples = 0;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private async Task<FileMoveResult> CopyAcrossVolumesAsync(
        string source,
        string destination,
        string journal,
        CancellationToken cancellationToken)
    {
        var destinationDirectory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("Destination directory could not be resolved.");
        var temporary = Path.Combine(destinationDirectory, $".eslee-{Guid.NewGuid():N}.partial");

        try
        {
            await using (var input = new FileStream(
                             source,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             1024 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             1024 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            var sourceLength = new FileInfo(source).Length;
            var copiedLength = new FileInfo(temporary).Length;
            if (sourceLength != copiedLength)
            {
                throw new IOException("Copied file size does not match the source file size.");
            }

            var sourceHash = await ComputeSha256Async(source, cancellationToken).ConfigureAwait(false);
            var copiedHash = await ComputeSha256Async(temporary, cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(sourceHash, copiedHash))
            {
                throw new IOException("Copied file hash does not match the source file hash.");
            }

            var publication = new Publication(source, destination, temporary, Convert.ToHexString(sourceHash));
            using (var state = new FileStream(journal + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(state, publication);
                state.Flush(true);
            }
            File.Move(journal + ".tmp", journal, true);
            return await FinishPublicationAsync(publication, journal, cancellationToken);
        }
        catch
        {
            if (!File.Exists(journal) && File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            throw;
        }
    }

    private async Task<FileMoveResult> FinishPublicationAsync(Publication pending, string journal, CancellationToken cancellationToken)
    {
        if (!File.Exists(pending.Destination))
        {
            if (!File.Exists(pending.Temporary) || Convert.ToHexString(await ComputeSha256Async(pending.Temporary, cancellationToken)) != pending.Hash)
                throw new IOException("The staged copy failed recovery verification.");
            File.Move(pending.Temporary, pending.Destination, false);
        }
        if (Convert.ToHexString(await ComputeSha256Async(pending.Destination, cancellationToken)) != pending.Hash)
            throw new IOException("The published destination has changed; source retained.");
        try
        {
            if (File.Exists(pending.Source))
            {
                if (Convert.ToHexString(await ComputeSha256Async(pending.Source, cancellationToken)) != pending.Hash)
                    return new FileMoveResult(false, pending.Destination, "file.source-changed-after-copy", "The verified destination exists, but the source changed. Both files were kept; inspect them before retrying cleanup.", false);
                _operations.DeleteSource(pending.Source);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FileMoveResult(false, pending.Destination, "file.source-cleanup-pending", ex.Message, true);
        }
        if (File.Exists(pending.Temporary))
        {
            if (Convert.ToHexString(await ComputeSha256Async(pending.Temporary, cancellationToken)) != pending.Hash)
                return new FileMoveResult(false, pending.Destination, "file.staging-changed", "The staging file changed; it was retained for inspection.", false);
            File.Delete(pending.Temporary);
        }
        File.Delete(journal);
        return new FileMoveResult(true, pending.Destination, null, null, false);
    }

    private static async Task<byte[]> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static string FindAvailableDestination(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate) && !Directory.Exists(candidate))
        {
            return candidate;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var suffix = 1; suffix < int.MaxValue; suffix++)
        {
            candidate = Path.Combine(directory, $"{stem} ({suffix}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("No available destination file name could be allocated.");
    }

    private static string ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new IOException("The downloaded file name is invalid on Windows.");
        }

        var stem = Path.GetFileNameWithoutExtension(fileName).TrimEnd(' ', '.');
        var reserved = stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || (stem.Length == 4
                && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                    || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && stem[3] is >= '1' and <= '9');
        if (reserved)
        {
            throw new IOException("The downloaded file name uses a reserved Windows device name.");
        }

        return fileName;
    }

    private static bool IsTemporaryDownloadFile(string path)
        => path.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".partial", StringComparison.OrdinalIgnoreCase);

    private static bool SameVolume(string first, string second)
        => string.Equals(Path.GetPathRoot(first), Path.GetPathRoot(second), StringComparison.OrdinalIgnoreCase);

    private static FileMoveResult Failure(string code, string message, bool canRetry)
        => new(false, null, code, message, canRetry);
}

/// <summary>Filesystem boundary hooks allow cross-volume failure tests using temporary folders.</summary>
public sealed class FileMoveOperations
{
    public Func<string, string, bool> SameVolume { get; init; } = (first, second) =>
        string.Equals(Path.GetPathRoot(first), Path.GetPathRoot(second), StringComparison.OrdinalIgnoreCase);
    public Action<string> DeleteSource { get; init; } = File.Delete;
}
