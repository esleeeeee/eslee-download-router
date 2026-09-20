using System.Collections.Concurrent;
using System.Security.Cryptography;
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
    ILogger<FileMoveService> logger)
{
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
            if (!File.Exists(sourceFull))
            {
                return Failure("file.source-missing", "The downloaded file no longer exists.", false);
            }

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

            if (!await WaitForStableFileAsync(sourceFull, cancellationToken).ConfigureAwait(false))
            {
                return Failure("file.locked-or-changing", "The file is still changing or locked by another process.", true);
            }

            var safeFileName = selectedFileName is null
                ? ValidateFileName(Path.GetFileName(sourceFull))
                : SelectionDestination.ValidateFileName(selectedFileName);
            var gate = DestinationLocks.GetOrAdd(destinationDirectory, static _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var destination = FindAvailableDestination(destinationDirectory, safeFileName);
                boundaryValidator.EnsureWithin(storageRoot, destination);

                if (sourceFull.Equals(destination, StringComparison.OrdinalIgnoreCase))
                {
                    return new FileMoveResult(true, destination, null, null, false);
                }

                if (SameVolume(sourceFull, destination))
                {
                    File.Move(sourceFull, destination, overwrite: false);
                }
                else
                {
                    await CopyAcrossVolumesAsync(sourceFull, destination, cancellationToken).ConfigureAwait(false);
                }

                logger.LogInformation(
                    "File move completed for {SourceFileName}",
                    Path.GetFileName(sourceFull));
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

    private static async Task CopyAcrossVolumesAsync(
        string source,
        string destination,
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

            File.Move(temporary, destination, overwrite: false);
            File.Delete(source);
        }
        catch
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            throw;
        }
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
