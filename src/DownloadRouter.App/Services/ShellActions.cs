using System.Diagnostics;

namespace DownloadRouter.App.Services;

/// <summary>Opens files and folders through Explorer. Paths are checked first so a moved file gives a clear message.</summary>
internal static class ShellActions
{
    /// <summary>Files Windows runs as programs; opening them asks the user first.</summary>
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".msix", ".appx", ".bat", ".cmd", ".com", ".scr", ".pif", ".ps1", ".vbs", ".vbe",
        ".js", ".jse", ".wsf", ".wsh", ".hta", ".lnk", ".reg", ".cpl", ".jar",
    };

    public static bool IsExecutable(string path)
        => ExecutableExtensions.Contains(Path.GetExtension(path));

    public static Task<bool> FileExistsAsync(string? path)
        => string.IsNullOrWhiteSpace(path)
            ? Task.FromResult(false)
            : Task.Run(() => File.Exists(path));

    public static Task<bool> DirectoryExistsAsync(string? path)
        => string.IsNullOrWhiteSpace(path)
            ? Task.FromResult(false)
            : Task.Run(() => Directory.Exists(path));

    public static void OpenFile(string path)
        => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    public static void ShowInFolder(string path)
    {
        var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        startInfo.ArgumentList.Add("/select,");
        startInfo.ArgumentList.Add(path);
        Process.Start(startInfo)?.Dispose();
    }

    public static void OpenFolder(string path)
    {
        var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        startInfo.ArgumentList.Add(path);
        Process.Start(startInfo)?.Dispose();
    }

    public static void OpenUrl(string url)
        => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
}
