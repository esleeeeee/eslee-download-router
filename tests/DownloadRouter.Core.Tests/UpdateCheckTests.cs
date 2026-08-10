using DownloadRouter.Core.Settings;
using DownloadRouter.Core.Updates;

namespace DownloadRouter.Core.Tests;

public sealed class UpdateCheckTests
{
    // ---------------------------------------------------------------------
    // Release payload parsing
    // ---------------------------------------------------------------------

    [Fact]
    public void AnOfficialReleasePayloadYieldsVersionAndPage()
    {
        var latest = UpdateCheckPolicy.TryParseLatestRelease("""
            {
              "tag_name": "v1.2.0",
              "html_url": "https://github.com/esleeeeee/eslee-download-router/releases/tag/v1.2.0",
              "draft": false,
              "prerelease": false
            }
            """);

        Assert.NotNull(latest);
        Assert.Equal(new Version(1, 2, 0), latest.Version);
        Assert.Equal("v1.2.0", latest.TagName);
        Assert.Equal(
            "https://github.com/esleeeeee/eslee-download-router/releases/tag/v1.2.0",
            latest.ReleaseUrl);
    }

    [Fact]
    public void ATagWithoutTheLeadingVIsStillAVersion()
    {
        var latest = UpdateCheckPolicy.TryParseLatestRelease("""{"tag_name": "1.2.3"}""");

        Assert.NotNull(latest);
        Assert.Equal(new Version(1, 2, 3), latest.Version);
    }

    [Theory]
    [InlineData("""{"tag_name": "v1.2.0", "draft": true}""")]
    [InlineData("""{"tag_name": "v1.2.0", "prerelease": true}""")]
    public void DraftAndPrereleaseEntriesAreNeverOffered(string json)
        => Assert.Null(UpdateCheckPolicy.TryParseLatestRelease(json));

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"tag_name": null}""")]
    [InlineData("""{"tag_name": "nightly-build"}""")]
    [InlineData("""{"tag_name": "v1.2"}""")]
    public void AnythingThatIsNotAThreePartReleaseIsIgnored(string json)
        => Assert.Null(UpdateCheckPolicy.TryParseLatestRelease(json));

    [Theory]
    [InlineData("https://evil.example/download.exe")]
    [InlineData("https://github.com/attacker/eslee-download-router-lookalike/releases/tag/v9.9.9")]
    [InlineData("https://trusted-text@github.com/esleeeeee/eslee-download-router/releases")]
    [InlineData("http://github.com/esleeeeee/eslee-download-router/releases")]
    public void AReleaseUrlOutsideThisRepositoryFallsBackToTheReleasesPage(string url)
    {
        var latest = UpdateCheckPolicy.TryParseLatestRelease(
            $$"""{"tag_name": "v1.2.0", "html_url": "{{url}}"}""");

        Assert.NotNull(latest);
        Assert.Equal(UpdateCheckPolicy.ReleasesPageUrl, latest.ReleaseUrl);
    }

    // ---------------------------------------------------------------------
    // URL normalisation guards every launch, including values read back from
    // the user-editable settings file
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("banana")]
    [InlineData("github.com/esleeeeee/eslee-download-router/releases")]
    [InlineData("ms-settings:windowsupdate")]
    [InlineData("file:///C:/anything.exe")]
    [InlineData("https://github.com/someone-else/other-repo")]
    public void AnUntrustedOrMalformedStoredUrlNeverReachesTheLauncher(string? candidate)
        => Assert.Equal(UpdateCheckPolicy.ReleasesPageUrl, UpdateCheckPolicy.NormalizeReleaseUrl(candidate));

    [Theory]
    [InlineData("https://github.com/esleeeeee/eslee-download-router/releases/tag/v1.2.0")]
    [InlineData("https://github.com/esleeeeee/eslee-download-router")]
    public void TheProjectsOwnReleasePagesPassNormalisationUnchanged(string url)
        => Assert.Equal(url, UpdateCheckPolicy.NormalizeReleaseUrl(url));

    [Fact]
    public void TheFallbackUrlItselfPassesNormalisation()
        => Assert.Equal(
            UpdateCheckPolicy.ReleasesPageUrl,
            UpdateCheckPolicy.NormalizeReleaseUrl(UpdateCheckPolicy.ReleasesPageUrl));

    [Fact]
    public void TheReleaseButtonAndTheCacheOnlyUseNormalisedUrls()
    {
        var updates = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "DownloadRouter.App", "MainWindow.Updates.cs"));

        // Every read of the persisted URL must go through the normaliser, and the raw
        // value must never reach a Uri constructor or the cached-state record directly.
        Assert.Contains(
            "UpdateCheckPolicy.NormalizeReleaseUrl(preferences.LastKnownReleaseUrl)",
            updates,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "new Uri(preferences.LastKnownReleaseUrl",
            updates,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "preferences.LastKnownReleaseUrl ??",
            updates,
            StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------
    // Version comparison
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("1.1.3", "v1.1.4", UpdateStatus.UpdateAvailable)]
    [InlineData("1.1.4", "v1.1.4", UpdateStatus.UpToDate)]
    [InlineData("1.2.0", "v1.1.4", UpdateStatus.UpToDate)]
    public void TheStatusComparesTheRunningBuildAgainstThePublishedRelease(
        string current,
        string latestTag,
        UpdateStatus expected)
    {
        var latest = UpdateCheckPolicy.TryParseLatestRelease($$"""{"tag_name": "{{latestTag}}"}""");

        Assert.Equal(expected, UpdateCheckPolicy.Evaluate(current, latest));
    }

    [Fact]
    public void WithoutAComparableVersionTheStatusIsUnknown()
    {
        var latest = UpdateCheckPolicy.TryParseLatestRelease("""{"tag_name": "v1.2.0"}""");

        Assert.Equal(UpdateStatus.Unknown, UpdateCheckPolicy.Evaluate(null, latest));
        Assert.Equal(UpdateStatus.Unknown, UpdateCheckPolicy.Evaluate("dev", latest));
        Assert.Equal(UpdateStatus.Unknown, UpdateCheckPolicy.Evaluate("1.2.0", null));
    }

    // ---------------------------------------------------------------------
    // Automatic-check throttle
    // ---------------------------------------------------------------------

    [Fact]
    public void TheFirstLaunchChecksAndLaterLaunchesWaitADay()
    {
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.FromHours(9));

        Assert.True(UpdateCheckPolicy.IsAutomaticCheckDue(null, now));
        Assert.True(UpdateCheckPolicy.IsAutomaticCheckDue("not a timestamp", now));
        Assert.False(UpdateCheckPolicy.IsAutomaticCheckDue(now.AddHours(-1).ToString("O"), now));
        Assert.False(UpdateCheckPolicy.IsAutomaticCheckDue(now.AddHours(-23).ToString("O"), now));
        Assert.True(UpdateCheckPolicy.IsAutomaticCheckDue(now.AddHours(-25).ToString("O"), now));
        // A clock that moved backwards must not block checks forever.
        Assert.True(UpdateCheckPolicy.IsAutomaticCheckDue(now.AddHours(6).ToString("O"), now));
    }

    // ---------------------------------------------------------------------
    // User-facing wording
    // ---------------------------------------------------------------------

    [Fact]
    public void TheUserSeesBothVersionsWhenAnUpdateExists()
    {
        var latest = UpdateCheckPolicy.TryParseLatestRelease("""{"tag_name": "v1.2.0"}""");
        var message = UpdateCheckPolicy.DescribeForUser(UpdateStatus.UpdateAvailable, "1.1.4", latest);

        Assert.Contains("1.2.0", message, StringComparison.Ordinal);
        Assert.Contains("1.1.4", message, StringComparison.Ordinal);

        Assert.Contains(
            "1.1.4",
            UpdateCheckPolicy.DescribeForUser(UpdateStatus.UpToDate, "1.1.4", null),
            StringComparison.Ordinal);
        Assert.Contains(
            "확인하지 못했습니다",
            UpdateCheckPolicy.DescribeForUser(UpdateStatus.Unknown, "1.1.4", null),
            StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------
    // Persistence of the check state
    // ---------------------------------------------------------------------

    [Fact]
    public void TheCheckStateSurvivesARestartAndKeepsForeignSettings()
    {
        var directory = Directory.CreateTempSubdirectory("dr-update-prefs");
        try
        {
            var path = Path.Combine(directory.FullName, "config.local.json");
            File.WriteAllText(path, """{"theme":"Dark","futureSetting":42}""");
            var store = new AppPreferencesStore(path);

            var loaded = store.Load();
            store.Save(loaded with
            {
                LastUpdateCheckAt = "2026-08-10T12:00:00+09:00",
                LastKnownLatestVersion = "1.2.0",
                LastKnownReleaseUrl = UpdateCheckPolicy.ReleasesPageUrl,
            });

            var reloaded = new AppPreferencesStore(path).Load();
            Assert.Equal("2026-08-10T12:00:00+09:00", reloaded.LastUpdateCheckAt);
            Assert.Equal("1.2.0", reloaded.LastKnownLatestVersion);
            Assert.Equal(UpdateCheckPolicy.ReleasesPageUrl, reloaded.LastKnownReleaseUrl);
            Assert.Equal("Dark", reloaded.Theme);
            Assert.Contains("futureSetting", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void OneWrongTypedValueDoesNotResetTheOtherSettings()
    {
        var directory = Directory.CreateTempSubdirectory("dr-update-prefs-mixed");
        try
        {
            var path = Path.Combine(directory.FullName, "config.local.json");
            // lastUpdateCheckAt has the wrong JSON type; theme and closeBehavior are valid
            // and must survive. The update check writes this file automatically, so a bad
            // value must never cost the user their own settings.
            File.WriteAllText(path, """{"theme":"Dark","closeBehavior":1,"lastUpdateCheckAt":123}""");

            var loaded = new AppPreferencesStore(path).Load();

            Assert.Equal("Dark", loaded.Theme);
            Assert.Equal(Core.Models.WindowCloseBehavior.ExitApplication, loaded.CloseBehavior);
            Assert.Null(loaded.LastUpdateCheckAt);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void AFreshInstallHasNoCheckStateAndNothingIsWrittenForIt()
    {
        var directory = Directory.CreateTempSubdirectory("dr-update-prefs-fresh");
        try
        {
            var path = Path.Combine(directory.FullName, "config.local.json");
            var store = new AppPreferencesStore(path);

            Assert.Null(store.Load().LastUpdateCheckAt);
            store.Save(store.Load());
            Assert.DoesNotContain("lastUpdateCheckAt", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    // ---------------------------------------------------------------------
    // Source and document guards
    // ---------------------------------------------------------------------

    [Fact]
    public void TheAboutScreenShowsTheUpdateCardNextToTheCurrentVersion()
    {
        var root = FindRepositoryRoot();
        var about = File.ReadAllText(Path.Combine(root, "src", "DownloadRouter.App", "MainWindow.xaml.cs"));
        var updates = File.ReadAllText(Path.Combine(root, "src", "DownloadRouter.App", "MainWindow.Updates.cs"));

        Assert.Contains("AddCard(\"현재 버전\", version.DisplayVersion)", about, StringComparison.Ordinal);
        Assert.Contains("AddUpdateCard(version)", about, StringComparison.Ordinal);
        Assert.Contains("지금 확인", updates, StringComparison.Ordinal);
        Assert.Contains("Release 페이지 열기", updates, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGitHubEndpointIsDeclaredOnceInTheCorePolicy()
    {
        var root = FindRepositoryRoot();
        var appSources = Directory
            .EnumerateFiles(Path.Combine(root, "src", "DownloadRouter.App"), "*.cs", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText);

        Assert.StartsWith("https://api.github.com/", UpdateCheckPolicy.LatestReleaseApiUrl, StringComparison.Ordinal);
        foreach (var source in appSources)
        {
            Assert.DoesNotContain("api.github.com", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheNetworklessComponentsStayNetworkless()
    {
        var root = FindRepositoryRoot();
        foreach (var project in new[] { "DownloadRouter.Agent", "DownloadRouter.NativeHost", "DownloadRouter.Infrastructure" })
        {
            var sources = Directory.EnumerateFiles(
                Path.Combine(root, "src", project), "*.cs", SearchOption.AllDirectories)
                .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
            foreach (var file in sources)
            {
                Assert.DoesNotContain("HttpClient", File.ReadAllText(file), StringComparison.Ordinal);
            }
        }

        // The browser extension must stay networkless too; it only talks to the Native
        // Host over the messaging APIs.
        var extensionSources = Directory.EnumerateFiles(
            Path.Combine(root, "src", "DownloadRouter.Extension", "src"), "*.ts", SearchOption.AllDirectories);
        Assert.NotEmpty(extensionSources);
        foreach (var file in extensionSources)
        {
            var source = File.ReadAllText(file);
            foreach (var primitive in new[] { "fetch(", "XMLHttpRequest", "WebSocket", "sendBeacon", "EventSource" })
            {
                Assert.DoesNotContain(primitive, source, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void ThePrivacyDocumentsDiscloseTheUpdateCheck()
    {
        var root = FindRepositoryRoot();
        var security = File.ReadAllText(Path.Combine(root, "SECURITY.md"));
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));

        Assert.Contains("업데이트 확인", security, StringComparison.Ordinal);
        Assert.Contains("api.github.com", security, StringComparison.Ordinal);
        // The old absolute no-network claims are gone.
        Assert.DoesNotContain("서버 전송, 텔레메트리, 광고 SDK, 전체 방문 기록 권한 없음", security, StringComparison.Ordinal);
        Assert.DoesNotContain("외부 서버로 전송하지 않습니다", readme, StringComparison.Ordinal);
        Assert.Contains("공개 배포 정보를 읽기만", readme, StringComparison.Ordinal);
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
