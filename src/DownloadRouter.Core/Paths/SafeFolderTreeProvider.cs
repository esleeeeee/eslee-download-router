namespace DownloadRouter.Core.Paths;

public sealed record FolderTreeEntry(
    string Name,
    string FullPath,
    string RelativePath,
    bool IsAccessible,
    string? ErrorMessage = null);

public sealed record FolderTreeChildren(
    IReadOnlyList<FolderTreeEntry> Entries,
    string? ErrorMessage = null);

public sealed class SafeFolderTreeProvider(PathBoundaryValidator boundaryValidator)
{
    public Task<FolderTreeChildren> GetChildrenAsync(
        string storageRoot,
        string parentPath,
        CancellationToken cancellationToken = default)
        => Task.Run(() => GetChildren(storageRoot, parentPath, cancellationToken), cancellationToken);

    private FolderTreeChildren GetChildren(
        string storageRoot,
        string parentPath,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(storageRoot);
        var parent = Path.GetFullPath(parentPath);
        boundaryValidator.EnsureWithin(root, parent);
        if (!Directory.Exists(parent))
        {
            return new FolderTreeChildren([], "폴더가 존재하지 않습니다.");
        }

        try
        {
            var result = new List<FolderTreeEntry>();
            foreach (var child in Directory.EnumerateDirectories(parent)
                         .OrderBy(static path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var fullPath = Path.GetFullPath(child);
                    boundaryValidator.EnsureWithin(root, fullPath);
                    if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    result.Add(new FolderTreeEntry(
                        Path.GetFileName(fullPath),
                        fullPath,
                        Path.GetRelativePath(root, fullPath),
                        true));
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
                {
                    result.Add(new FolderTreeEntry(
                        Path.GetFileName(child),
                        child,
                        string.Empty,
                        false,
                        "이 폴더에 접근할 수 없습니다."));
                }
            }

            return new FolderTreeChildren(result);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return new FolderTreeChildren([], "이 폴더의 하위 항목을 읽을 수 없습니다.");
        }
    }
}
