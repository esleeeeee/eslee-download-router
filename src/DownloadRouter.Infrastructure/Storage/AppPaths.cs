using DownloadRouter.Core.Startup;

namespace DownloadRouter.Infrastructure.Storage;

public sealed class AppPaths
{
    private AppPaths(string rootDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
        DataDirectory = Path.Combine(RootDirectory, "data");
        LogsDirectory = Path.Combine(RootDirectory, "logs");
        DiagnosticsDirectory = Path.Combine(RootDirectory, "diagnostics");
        DatabasePath = Path.Combine(DataDirectory, "download-router.db");
        ConfigPath = Path.Combine(RootDirectory, "config.local.json");
    }

    public string RootDirectory { get; }

    public string DataDirectory { get; }

    public string LogsDirectory { get; }

    public string DiagnosticsDirectory { get; }

    public string DatabasePath { get; }

    public string ConfigPath { get; }

    /// <summary>Paths for the data root of <paramref name="instance"/>.</summary>
    public static AppPaths For(AppInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return new AppPaths(instance.DataRoot);
    }

    /// <summary>
    /// Paths for the instance selected by <c>DOWNLOAD_ROUTER_DATA_DIR</c>, or the default
    /// data folder when it is unset or not fully qualified.
    /// </summary>
    public static AppPaths CreateDefault()
        => For(AppInstance.Resolve(
            [],
            Environment.GetEnvironmentVariable(AppInstance.DataDirectoryEnvironmentVariable)));

    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(DiagnosticsDirectory);
    }
}
