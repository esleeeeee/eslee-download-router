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

    public static AppPaths CreateDefault()
    {
        var overridePath = Environment.GetEnvironmentVariable("DOWNLOAD_ROUTER_DATA_DIR");
        var root = !string.IsNullOrWhiteSpace(overridePath) && Path.IsPathFullyQualified(overridePath)
            ? overridePath
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "eslee",
                "DownloadRouter");

        return new AppPaths(root);
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(DiagnosticsDirectory);
    }
}
