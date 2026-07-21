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
    public static IReadOnlyList<BrowserInfo> Detect()
        =>
        [
            Create(BrowserKind.Whale, "네이버 웨일", "whale://extensions", true,
                Local("Naver", "Naver Whale", "Application", "whale.exe"),
                ProgramFiles("Naver", "Naver Whale", "Application", "whale.exe")),
            Create(BrowserKind.Edge, "Microsoft Edge", "edge://extensions", true,
                ProgramFilesX86("Microsoft", "Edge", "Application", "msedge.exe"),
                ProgramFiles("Microsoft", "Edge", "Application", "msedge.exe")),
            Create(BrowserKind.Chrome, "Google Chrome", "chrome://extensions", true,
                ProgramFiles("Google", "Chrome", "Application", "chrome.exe"),
                ProgramFilesX86("Google", "Chrome", "Application", "chrome.exe"),
                Local("Google", "Chrome", "Application", "chrome.exe")),
            Create(BrowserKind.Brave, "Brave", "brave://extensions", false,
                ProgramFiles("BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
                Local("BraveSoftware", "Brave-Browser", "Application", "brave.exe")),
            Create(BrowserKind.Vivaldi, "Vivaldi", "vivaldi://extensions", false,
                Local("Vivaldi", "Application", "vivaldi.exe"),
                ProgramFiles("Vivaldi", "Application", "vivaldi.exe")),
            Create(BrowserKind.Opera, "Opera", "opera://extensions", false,
                Local("Programs", "Opera", "opera.exe"),
                Local("Programs", "Opera GX", "opera.exe")),
        ];

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

    private static BrowserInfo Create(
        BrowserKind kind,
        string displayName,
        string managementUrl,
        bool official,
        params string[] candidates)
        => new(kind, displayName, managementUrl, official, candidates.FirstOrDefault(File.Exists));

    private static string Local(params string[] parts)
        => Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), .. parts]);

    private static string ProgramFiles(params string[] parts)
        => Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), .. parts]);

    private static string ProgramFilesX86(params string[] parts)
        => Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), .. parts]);
}
