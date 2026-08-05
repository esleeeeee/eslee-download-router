using System.Diagnostics;
using DownloadRouter.Core.Models;

namespace DownloadRouter.App;

public sealed record BrowserInfo(
    BrowserKind Kind,
    string DisplayName,
    string ManagementUrl,
    bool IsOfficial,
    string? ExecutablePath)
{
    public bool IsInstalled => ExecutablePath is not null;
}

public static class BrowserCatalog
{
    // Display name, management URL, and support level come from the shared catalog so the
    // browsers page, the information screen, and the installer registration cannot drift.
    private static readonly IReadOnlyDictionary<BrowserKind, string[]> ExecutableCandidates =
        new Dictionary<BrowserKind, string[]>
        {
            [BrowserKind.Whale] =
            [
                Local("Naver", "Naver Whale", "Application", "whale.exe"),
                ProgramFiles("Naver", "Naver Whale", "Application", "whale.exe"),
            ],
            [BrowserKind.Edge] =
            [
                ProgramFilesX86("Microsoft", "Edge", "Application", "msedge.exe"),
                ProgramFiles("Microsoft", "Edge", "Application", "msedge.exe"),
            ],
            [BrowserKind.Chrome] =
            [
                ProgramFiles("Google", "Chrome", "Application", "chrome.exe"),
                ProgramFilesX86("Google", "Chrome", "Application", "chrome.exe"),
                Local("Google", "Chrome", "Application", "chrome.exe"),
            ],
            [BrowserKind.Brave] =
            [
                ProgramFiles("BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
                Local("BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
            ],
            [BrowserKind.Vivaldi] =
            [
                Local("Vivaldi", "Application", "vivaldi.exe"),
                ProgramFiles("Vivaldi", "Application", "vivaldi.exe"),
            ],
            [BrowserKind.Opera] =
            [
                Local("Programs", "Opera", "opera.exe"),
                Local("Programs", "Opera GX", "opera.exe"),
            ],
        };

    public static IReadOnlyList<BrowserInfo> Detect()
        => BrowserSupportCatalog.All
            .Select(static browser => new BrowserInfo(
                browser.Kind,
                browser.DisplayName,
                browser.ExtensionManagementUrl,
                browser.Level == BrowserSupportLevel.Official,
                ExecutableCandidates.TryGetValue(browser.Kind, out var candidates)
                    ? candidates.FirstOrDefault(File.Exists)
                    : null))
            .ToArray();

    public static void OpenManagementPage(BrowserInfo browser)
    {
        if (browser.ExecutablePath is null)
        {
            throw new FileNotFoundException($"{browser.DisplayName} 실행 파일을 찾지 못했습니다.");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = browser.ExecutablePath,
            ArgumentList = { browser.ManagementUrl },
            UseShellExecute = false,
        });
    }

    private static string Local(params string[] parts)
        => Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), .. parts]);

    private static string ProgramFiles(params string[] parts)
        => Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), .. parts]);

    private static string ProgramFilesX86(params string[] parts)
        => Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), .. parts]);
}
