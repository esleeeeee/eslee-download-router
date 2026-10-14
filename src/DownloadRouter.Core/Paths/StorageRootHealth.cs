using DownloadRouter.Core.Localization;

namespace DownloadRouter.Core.Paths;

public enum StorageRootStatus
{
    /// <summary>The folder exists, can be listed, and (when probed) accepts a new file.</summary>
    Available,

    /// <summary>The drive or share is there but the folder is not.</summary>
    Missing,

    /// <summary>The folder exists but this user may not read or write it.</summary>
    AccessDenied,

    /// <summary>The drive or share did not answer in time or reported an I/O error.</summary>
    Unreachable,

    /// <summary>The path itself is not a usable absolute folder path.</summary>
    Invalid,
}

/// <summary>
/// Checks whether a rule's base folder can be used right now. The check runs on the thread
/// pool and is abandoned after a timeout, so a disconnected network share can never freeze
/// the caller; the abandoned check finishes on its own in the background.
/// </summary>
public static class StorageRootHealth
{
    public static Task<StorageRootStatus> CheckAsync(
        string resolvedPath,
        bool probeWrite,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        => CheckAsync(() => Check(resolvedPath, probeWrite), timeout, cancellationToken);

    internal static async Task<StorageRootStatus> CheckAsync(
        Func<StorageRootStatus> check,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(check);
        cancellationToken.ThrowIfCancellationRequested();
        var work = Task.Run(check, CancellationToken.None);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delay = Task.Delay(timeout < TimeSpan.Zero ? TimeSpan.Zero : timeout, timeoutSource.Token);
        var finished = await Task.WhenAny(work, delay).ConfigureAwait(false);
        if (finished == work)
        {
            await timeoutSource.CancelAsync().ConfigureAwait(false);
            return await work.ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return StorageRootStatus.Unreachable;
    }

    /// <summary>One sentence for the user describing <paramref name="status"/>.</summary>
    public static string Describe(StorageRootStatus status, string path)
        => status switch
        {
            StorageRootStatus.Available => AppText.Pick(
                $"이 폴더에 파일을 저장할 수 있습니다. ({path})",
                $"Files can be saved to this folder. ({path})"),
            StorageRootStatus.Missing => AppText.Pick(
                $"폴더가 없습니다. 폴더 선택 버튼으로 기존 폴더를 지정하세요. ({path})",
                $"The folder does not exist. Use the folder picker to choose an existing folder. ({path})"),
            StorageRootStatus.AccessDenied => AppText.Pick(
                $"이 폴더에 저장할 권한이 없습니다. 다른 폴더를 선택하세요. ({path})",
                $"You do not have permission to save to this folder. Choose another folder. ({path})"),
            StorageRootStatus.Unreachable => AppText.Pick(
                $"폴더에 연결할 수 없습니다. 외장 드라이브나 네트워크 연결을 확인하세요. ({path})",
                $"The folder cannot be reached. Check the external drive or network connection. ({path})"),
            _ => AppText.Pick(
                $"올바른 폴더 경로가 아닙니다. 드라이브부터 시작하는 전체 경로를 입력하세요. ({path})",
                $"This is not a valid folder path. Enter a full path that starts with a drive. ({path})"),
        };

    private static StorageRootStatus Check(string resolvedPath, bool probeWrite)
    {
        if (string.IsNullOrWhiteSpace(resolvedPath) || !Path.IsPathFullyQualified(resolvedPath))
        {
            return StorageRootStatus.Invalid;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(resolvedPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return StorageRootStatus.Invalid;
        }

        if (fullPath.AsSpan().IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return StorageRootStatus.Invalid;
        }

        try
        {
            if (!Directory.Exists(fullPath))
            {
                if (File.Exists(fullPath))
                {
                    return StorageRootStatus.Invalid;
                }

                // A missing drive letter or share is a connection problem, not a missing folder.
                var root = Path.GetPathRoot(fullPath);
                return string.IsNullOrEmpty(root) || Directory.Exists(root)
                    ? StorageRootStatus.Missing
                    : StorageRootStatus.Unreachable;
            }

            _ = Directory.EnumerateFileSystemEntries(fullPath).Take(1).ToArray();
            if (probeWrite)
            {
                var probe = Path.Combine(fullPath, $".eslee-write-test-{Guid.NewGuid():N}.tmp");
                using (new FileStream(
                           probe,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           1,
                           FileOptions.DeleteOnClose))
                {
                }
            }

            return StorageRootStatus.Available;
        }
        catch (UnauthorizedAccessException)
        {
            return StorageRootStatus.AccessDenied;
        }
        catch (DirectoryNotFoundException)
        {
            return StorageRootStatus.Missing;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return StorageRootStatus.Invalid;
        }
        catch (IOException)
        {
            return StorageRootStatus.Unreachable;
        }
    }
}
