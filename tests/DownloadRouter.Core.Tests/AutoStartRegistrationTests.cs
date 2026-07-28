using DownloadRouter.Core.Startup;
using Microsoft.Win32;

namespace DownloadRouter.Core.Tests;

public sealed class AutoStartRegistrationTests
{
    [Fact]
    public void RegistrationIsIdempotentAndUsesBackgroundArgument()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var keyPath = $@"Software\eslee\DownloadRouter.Tests\{Guid.NewGuid():N}";
        const string valueName = "AutoStartTest";
        var registration = new AutoStartRegistration(keyPath, valueName);
        var executable = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Download Router App.exe"));
        try
        {
            registration.SetEnabled(true, executable);
            registration.SetEnabled(true, executable);

            Assert.True(registration.IsEnabled(executable));
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            Assert.Equal($"\"{executable}\" --background", key!.GetValue(valueName));

            registration.SetEnabled(false, executable);
            Assert.False(registration.IsEnabled(executable));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
        }
    }
}
