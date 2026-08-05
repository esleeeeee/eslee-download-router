using DownloadRouter.Core.Models;
using DownloadRouter.Core.Paths;

namespace DownloadRouter.Core.Tests;

public sealed class UserGuidanceTests
{
    // ---------------------------------------------------------------------
    // Extension folder shown on the browser connection screen
    // ---------------------------------------------------------------------

    [Fact]
    public void AnInstalledBuildPointsAtItsOwnExtensionFolder()
    {
        var appDirectory = Path.Combine(@"X:\", "Programs", "eslee", "DownloadRouter");
        var expected = Path.Combine(appDirectory, "extension");

        var location = ExtensionFolderLocator.Locate(
            appDirectory,
            directoryExists: path => string.Equals(path, expected, StringComparison.OrdinalIgnoreCase),
            fileExists: static _ => false);

        Assert.Equal(ExtensionFolderSource.Installed, location.Source);
        Assert.Equal(Path.GetFullPath(expected), location.Path);
        Assert.True(location.Exists);
    }

    [Fact]
    public void AnInstalledBuildNeverFallsBackToTheDevelopmentBuildOutput()
    {
        var repositoryRoot = Path.Combine(@"X:\", "src", "eslee-download-router");
        var appDirectory = Path.Combine(repositoryRoot, "artifacts", "publish", "app");
        var installed = Path.Combine(appDirectory, "extension");
        var development = Path.Combine(repositoryRoot, "src", "DownloadRouter.Extension", "dist");

        var location = ExtensionFolderLocator.Locate(
            appDirectory,
            directoryExists: path =>
                string.Equals(path, installed, StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, development, StringComparison.OrdinalIgnoreCase),
            fileExists: path => string.Equals(
                path,
                Path.Combine(repositoryRoot, "DownloadRouter.slnx"),
                StringComparison.OrdinalIgnoreCase));

        // Both folders exist here; the one shipped with the app must win.
        Assert.Equal(ExtensionFolderSource.Installed, location.Source);
        Assert.Equal(Path.GetFullPath(installed), location.Path);
    }

    [Fact]
    public void ADevelopmentRunPointsAtTheBuildOutputInsideTheClone()
    {
        var repositoryRoot = Path.Combine(@"X:\", "src", "eslee-download-router");
        var appDirectory = Path.Combine(repositoryRoot, "src", "DownloadRouter.App", "bin", "Debug", "net10.0");
        var development = Path.Combine(repositoryRoot, "src", "DownloadRouter.Extension", "dist");

        var location = ExtensionFolderLocator.Locate(
            appDirectory,
            directoryExists: path => string.Equals(path, development, StringComparison.OrdinalIgnoreCase),
            fileExists: path => string.Equals(
                path,
                Path.Combine(repositoryRoot, "DownloadRouter.slnx"),
                StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ExtensionFolderSource.DevelopmentBuild, location.Source);
        Assert.Equal(Path.GetFullPath(development), location.Path);
    }

    [Fact]
    public void AMissingExtensionFolderIsReportedInsteadOfGuessingAPath()
    {
        var location = ExtensionFolderLocator.Locate(
            Path.Combine(@"X:\", "Programs", "eslee", "DownloadRouter"),
            directoryExists: static _ => false,
            fileExists: static _ => false);

        Assert.Equal(ExtensionFolderSource.NotFound, location.Source);
        Assert.Null(location.Path);
        Assert.False(location.Exists);
        Assert.Contains("찾지 못했습니다", ExtensionFolderLocator.DescribeForUser(location), StringComparison.Ordinal);
    }

    [Fact]
    public void TheGuidanceTextAlwaysContainsTheFolderItFound()
    {
        var installed = new ExtensionFolderLocation(
            ExtensionFolderSource.Installed,
            Path.Combine(@"X:\", "Programs", "eslee", "DownloadRouter", "extension"));
        var development = new ExtensionFolderLocation(
            ExtensionFolderSource.DevelopmentBuild,
            Path.Combine(@"X:\", "src", "eslee-download-router", "src", "DownloadRouter.Extension", "dist"));

        Assert.Contains(installed.Path!, ExtensionFolderLocator.DescribeForUser(installed), StringComparison.Ordinal);
        Assert.Contains(development.Path!, ExtensionFolderLocator.DescribeForUser(development), StringComparison.Ordinal);
        // A development run must say so instead of looking like an installed layout.
        Assert.Contains("개발 빌드", ExtensionFolderLocator.DescribeForUser(development), StringComparison.Ordinal);
        Assert.DoesNotContain("개발 빌드", ExtensionFolderLocator.DescribeForUser(installed), StringComparison.Ordinal);
    }

    [Fact]
    public void TheLocatorNeverHardCodesAUserSpecificPath()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "DownloadRouter.Core", "Paths", "ExtensionFolderLocator.cs"));

        Assert.DoesNotContain(@"C:\Users", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LOCALAPPDATA", source, StringComparison.OrdinalIgnoreCase);
        // The search always starts from the running executable's directory.
        Assert.Contains("baseDirectory", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBrowserScreenAsksTheLocatorInsteadOfPrintingAFixedPath()
    {
        var shell = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "DownloadRouter.App", "MainWindow.xaml.cs"));

        Assert.Contains("ExtensionFolderLocator.Locate(AppContext.BaseDirectory)", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("src/DownloadRouter.Extension/dist", shell, StringComparison.Ordinal);
        Assert.DoesNotContain(@"src\DownloadRouter.Extension\dist", shell, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------
    // Browser support wording
    // ---------------------------------------------------------------------

    [Fact]
    public void TheSupportCatalogMatchesTheBrowsersTheInstallerRegisters()
    {
        var script = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "scripts", "register-native-host.ps1"));

        foreach (var browser in BrowserSupportCatalog.All)
        {
            Assert.Contains($"{browser.Kind} = 'HKCU:", script, StringComparison.Ordinal);
        }

        // Every registered browser must also be listed for the user.
        var registered = System.Text.RegularExpressions.Regex
            .Matches(script, @"^\s*(?<name>\w+) = 'HKCU:", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            BrowserSupportCatalog.All.Select(static browser => browser.Kind.ToString()).OrderBy(static name => name),
            registered.OrderBy(static name => name));
    }

    [Fact]
    public void EveryListedBrowserHasAManagementUrlAndASupportLevel()
    {
        Assert.NotEmpty(BrowserSupportCatalog.Official);
        Assert.NotEmpty(BrowserSupportCatalog.Compatible);

        foreach (var browser in BrowserSupportCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(browser.DisplayName));
            Assert.EndsWith("://extensions", browser.ExtensionManagementUrl, StringComparison.Ordinal);
            Assert.NotEqual(BrowserKind.Unknown, browser.Kind);
        }

        Assert.Equal(
            BrowserSupportCatalog.All.Count,
            BrowserSupportCatalog.All.Select(static browser => browser.Kind).Distinct().Count());
    }

    [Fact]
    public void TheInformationScreenUsesTheSharedSupportSentence()
    {
        var shell = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "DownloadRouter.App", "MainWindow.xaml.cs"));

        Assert.Contains("BrowserSupportCatalog.SupportSummary", shell, StringComparison.Ordinal);
        // The previously hard-coded sentence must be gone.
        Assert.DoesNotContain("Whale / Edge / Chrome 공식 지원", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSupportSentenceOnlyClaimsBrowsersThatAreListed()
    {
        var summary = BrowserSupportCatalog.SupportSummary;

        foreach (var browser in BrowserSupportCatalog.All)
        {
            Assert.Contains(browser.DisplayName, summary, StringComparison.Ordinal);
        }

        Assert.Contains("Firefox 제외", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Safari", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReadmeAgreesWithTheSupportCatalog()
    {
        var readme = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "README.md"));

        foreach (var browser in BrowserSupportCatalog.Official)
        {
            Assert.Contains(browser.DisplayName, readme, StringComparison.Ordinal);
        }

        foreach (var browser in BrowserSupportCatalog.Compatible)
        {
            Assert.Contains(browser.DisplayName, readme, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------------
    // Logging disclosure
    // ---------------------------------------------------------------------

    [Fact]
    public void SecurityDocumentDescribesEveryLogFileTheProductWrites()
    {
        var security = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "SECURITY.md"));

        foreach (var logFile in new[]
        {
            "download-router.jsonl",
            "app-unhandled.log",
            "app-startup.log",
            "window-activation.log",
        })
        {
            Assert.Contains(logFile, security, StringComparison.Ordinal);
        }

        // The previous wording claimed paths were always scrubbed everywhere.
        Assert.DoesNotContain("JSONL 로그에서 URL과 경로 민감값 정제", security, StringComparison.Ordinal);
    }

    [Fact]
    public void SecurityDocumentAdmitsWhatTheRedactionDoesNotCover()
    {
        var security = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "SECURITY.md"));

        // File names survive redaction, and one log file is not redacted at all.
        Assert.Contains("파일 이름", security, StringComparison.Ordinal);
        Assert.Contains("치환이 적용되지 않습니다", security, StringComparison.Ordinal);
        Assert.Contains("File move completed for", security, StringComparison.Ordinal);
        Assert.Contains("[PATH_REDACTED]", security, StringComparison.Ordinal);
        Assert.Contains("[URL_REDACTED]", security, StringComparison.Ordinal);
    }

    [Fact]
    public void SecurityDocumentQuotesTheRedactionMarkersTheLoggerActuallyWrites()
    {
        var root = FindRepositoryRoot();
        var security = File.ReadAllText(Path.Combine(root, "SECURITY.md"));
        var logger = File.ReadAllText(Path.Combine(
            root, "src", "DownloadRouter.Infrastructure", "Logging", "JsonLineFileLogger.cs"));
        var fileMove = File.ReadAllText(Path.Combine(
            root, "src", "DownloadRouter.Infrastructure", "Files", "FileMoveService.cs"));

        foreach (var marker in new[] { "[URL_REDACTED]", "[PATH_REDACTED]" })
        {
            Assert.Contains(marker, logger, StringComparison.Ordinal);
            Assert.Contains(marker, security, StringComparison.Ordinal);
        }

        // The documented example must be a log template that really exists.
        Assert.Contains("File move completed for {SourceFileName}", fileMove, StringComparison.Ordinal);

        // The jsonl line carries the exception type only, which is what the document promises.
        Assert.Contains("exception = exception?.GetType().Name", logger, StringComparison.Ordinal);
        Assert.Contains("형식 이름만", security, StringComparison.Ordinal);
    }

    [Fact]
    public void SecurityDocumentAndReadmeGiveTheSameLogPromise()
    {
        var root = FindRepositoryRoot();
        var security = File.ReadAllText(Path.Combine(root, "SECURITY.md"));
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));

        // Both must warn before attaching a log, and neither may promise a scrubbed log.
        Assert.Contains("파일 이름", readme, StringComparison.Ordinal);
        Assert.Contains("app-unhandled.log", readme, StringComparison.Ordinal);
        foreach (var document in new[] { security, readme })
        {
            Assert.DoesNotContain("로그에 개인정보가 포함되지 않습니다", document, StringComparison.Ordinal);
            Assert.DoesNotContain("로그에는 개인정보가 남지 않습니다", document, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheFixedExtensionIdIsDeclaredOnceAndReused()
    {
        var root = FindRepositoryRoot();
        var nativeHost = File.ReadAllText(Path.Combine(root, "src", "DownloadRouter.NativeHost", "Program.cs"));
        var shell = File.ReadAllText(Path.Combine(root, "src", "DownloadRouter.App", "MainWindow.xaml.cs"));

        Assert.Equal("gilicenlclaemgiijcjjejilikbooggj", ProtocolConstants.ExtensionId);
        Assert.Contains("ProtocolConstants.ExtensionId", nativeHost, StringComparison.Ordinal);
        Assert.Contains("ProtocolConstants.ExtensionId", shell, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DownloadRouter.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
