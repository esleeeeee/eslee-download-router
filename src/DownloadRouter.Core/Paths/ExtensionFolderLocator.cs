namespace DownloadRouter.Core.Paths;

public enum ExtensionFolderSource
{
    /// <summary>The folder shipped next to the installed app.</summary>
    Installed,

    /// <summary>The build output inside a source clone, used while developing.</summary>
    DevelopmentBuild,

    /// <summary>No extension folder could be found on this machine.</summary>
    NotFound,
}

public sealed record ExtensionFolderLocation(ExtensionFolderSource Source, string? Path)
{
    public bool Exists => Source != ExtensionFolderSource.NotFound && Path is not null;
}

/// <summary>
/// Finds the unpacked extension folder the user must load in the browser.
///
/// The installer copies the extension next to the app, so an installed build always uses
/// its own folder. A source clone has no such folder, so the build output inside the
/// repository is used instead. Nothing is hard-coded to a machine-specific path: the
/// search always starts from the running executable.
/// </summary>
public static class ExtensionFolderLocator
{
    public const string InstalledFolderName = "extension";

    private static readonly string[] DevelopmentRelativePath =
        ["src", "DownloadRouter.Extension", "dist"];

    private const string RepositoryMarker = "DownloadRouter.slnx";

    public static ExtensionFolderLocation Locate(string baseDirectory)
        => Locate(baseDirectory, Directory.Exists, File.Exists);

    /// <summary>
    /// Overload used by tests so the search can run against a temporary layout.
    /// </summary>
    public static ExtensionFolderLocation Locate(
        string baseDirectory,
        Func<string, bool> directoryExists,
        Func<string, bool> fileExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentNullException.ThrowIfNull(directoryExists);
        ArgumentNullException.ThrowIfNull(fileExists);

        var installed = Path.Combine(baseDirectory, InstalledFolderName);
        if (directoryExists(installed))
        {
            return new ExtensionFolderLocation(ExtensionFolderSource.Installed, Path.GetFullPath(installed));
        }

        var repositoryRoot = FindRepositoryRoot(baseDirectory, fileExists);
        if (repositoryRoot is not null)
        {
            var development = Path.Combine([repositoryRoot, .. DevelopmentRelativePath]);
            if (directoryExists(development))
            {
                return new ExtensionFolderLocation(
                    ExtensionFolderSource.DevelopmentBuild,
                    Path.GetFullPath(development));
            }
        }

        return new ExtensionFolderLocation(ExtensionFolderSource.NotFound, null);
    }

    /// <summary>One line telling the user which folder to load, or why none is available.</summary>
    public static string DescribeForUser(ExtensionFolderLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return location.Source switch
        {
            ExtensionFolderSource.Installed =>
                $"브라우저에서 이 폴더를 선택하세요: {location.Path}",
            ExtensionFolderSource.DevelopmentBuild =>
                $"개발 빌드로 실행 중입니다. 브라우저에서 이 폴더를 선택하세요: {location.Path}",
            _ =>
                "확장 폴더를 찾지 못했습니다. 설치 프로그램으로 다시 설치한 뒤 이 화면을 다시 열어 주세요.",
        };
    }

    private static string? FindRepositoryRoot(string baseDirectory, Func<string, bool> fileExists)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(baseDirectory));
        while (directory is not null)
        {
            if (fileExists(Path.Combine(directory.FullName, RepositoryMarker)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
