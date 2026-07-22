using Microsoft.Win32;
using System.Runtime.Versioning;

namespace DownloadRouter.Core.Startup;

[SupportedOSPlatform("windows")]
public sealed class AutoStartRegistration(
    string subKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run",
    string valueName = "eslee Download Router")
{
    public bool IsEnabled(string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKeyPath, writable: false);
        var current = key?.GetValue(valueName) as string;
        return string.Equals(current, BuildCommand(executablePath), StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled, string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (!Path.IsPathFullyQualified(executablePath))
        {
            throw new ArgumentException("The startup executable path must be absolute.", nameof(executablePath));
        }

        using var key = Registry.CurrentUser.CreateSubKey(subKeyPath, writable: true)
            ?? throw new InvalidOperationException("The current-user startup registry key could not be opened.");
        if (enabled)
        {
            key.SetValue(valueName, BuildCommand(executablePath), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }

    public static string BuildCommand(string executablePath)
        => $"\"{Path.GetFullPath(executablePath)}\" --background";
}
