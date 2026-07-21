namespace DownloadRouter.Core.Paths;

public interface IKnownPathProvider
{
    IReadOnlyDictionary<string, string> GetKnownPaths();
}

public sealed class WindowsKnownPathProvider : IKnownPathProvider
{
    public IReadOnlyDictionary<string, string> GetKnownPaths()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{UserProfile}"] = userProfile,
            ["{Downloads}"] = Path.Combine(userProfile, "Downloads"),
            ["{Documents}"] = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            ["{Pictures}"] = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            ["{Videos}"] = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            ["{Music}"] = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            ["{Desktop}"] = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            ["{LocalAppData}"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        };
    }
}

public sealed class PathTokenResolver(IKnownPathProvider knownPathProvider)
{
    public string Resolve(string tokenizedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenizedPath);

        var knownPaths = knownPathProvider.GetKnownPaths();
        foreach (var pair in knownPaths.OrderByDescending(static pair => pair.Key.Length))
        {
            if (tokenizedPath.Equals(pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(pair.Value);
            }

            if (tokenizedPath.StartsWith(pair.Key + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || tokenizedPath.StartsWith(pair.Key + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                var suffix = tokenizedPath[(pair.Key.Length + 1)..];
                return Path.GetFullPath(Path.Combine(pair.Value, suffix));
            }
        }

        if (tokenizedPath.Contains('{', StringComparison.Ordinal)
            || tokenizedPath.Contains('}', StringComparison.Ordinal))
        {
            throw new ArgumentException("Unknown or misplaced path token.", nameof(tokenizedPath));
        }

        return Path.GetFullPath(tokenizedPath);
    }

    public string Tokenize(string absolutePath)
    {
        var fullPath = Path.GetFullPath(absolutePath).TrimEnd(Path.DirectorySeparatorChar);
        foreach (var pair in knownPathProvider.GetKnownPaths()
                     .Where(static pair => !string.IsNullOrWhiteSpace(pair.Value))
                     .OrderByDescending(static pair => pair.Value.Length))
        {
            var known = Path.GetFullPath(pair.Value).TrimEnd(Path.DirectorySeparatorChar);
            if (fullPath.Equals(known, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Key;
            }

            if (fullPath.StartsWith(known + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Key + fullPath[known.Length..];
            }
        }

        return fullPath;
    }
}

public sealed class PathBoundaryValidator
{
    public string ValidateRelativeFolder(string root, string relativeFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        relativeFolder ??= string.Empty;

        if (Path.IsPathRooted(relativeFolder)
            || relativeFolder.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(static part => part == ".."))
        {
            throw new UnauthorizedAccessException("The selected folder must remain under the configured root.");
        }

        var rootFull = NormalizeDirectory(root);
        var targetFull = NormalizeDirectory(Path.Combine(rootFull, relativeFolder));
        EnsureWithin(rootFull, targetFull);
        EnsureNoReparsePointEscape(rootFull, targetFull);
        return targetFull;
    }

    public void EnsureWithin(string root, string target)
    {
        var rootFull = NormalizeDirectory(root);
        var targetFull = NormalizeDirectory(target);
        if (!targetFull.Equals(rootFull, StringComparison.OrdinalIgnoreCase)
            && !targetFull.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Resolved path escapes the configured root.");
        }
    }

    private static void EnsureNoReparsePointEscape(string root, string target)
    {
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Configured root does not exist: {root}");
        }

        var relative = Path.GetRelativePath(root, target);
        var current = root;
        foreach (var part in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (!Directory.Exists(current))
            {
                break;
            }

            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new UnauthorizedAccessException("Reparse points are not allowed inside a selection root.");
            }
        }
    }

    private static string NormalizeDirectory(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
