using DownloadRouter.Core.Models;
using DownloadRouter.Core.Updates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace DownloadRouter.App;

public sealed partial class MainWindow
{
    /// <summary>
    /// Re-evaluates due-ness every few hours while the tray app runs; the actual network
    /// request still happens at most once per <see cref="UpdateCheckPolicy.AutomaticCheckInterval"/>.
    /// </summary>
    private readonly DispatcherTimer updateCheckTimer = new() { Interval = TimeSpan.FromHours(6) };

    /// <summary>
    /// The in-flight check, shared so a manual click during the automatic startup check
    /// awaits the same result instead of reporting stale state. Touched only on the UI
    /// thread.
    /// </summary>
    private Task<UpdateCheckOutcome>? updateCheckTask;

    private void StartUpdateChecks()
    {
        updateCheckTimer.Tick += (_, _) => _ = RunAutomaticUpdateCheckAsync();
        updateCheckTimer.Start();
        Closed += (_, _) => updateCheckTimer.Stop();
        _ = RunAutomaticUpdateCheckAsync();
    }

    private async Task RunAutomaticUpdateCheckAsync()
    {
        if (!UpdateCheckPolicy.IsAutomaticCheckDue(preferences.LastUpdateCheckAt, DateTimeOffset.Now))
        {
            return;
        }

        await RunUpdateCheckAsync();
    }

    /// <summary>
    /// Runs one check and persists the outcome, or joins the check already in flight.
    /// Failures only mark the attempt time so an offline start does not retry on every
    /// launch; the known latest version is kept.
    /// </summary>
    private Task<UpdateCheckOutcome> RunUpdateCheckAsync()
    {
        if (updateCheckTask is { IsCompleted: false } running)
        {
            return running;
        }

        var task = RunUpdateCheckCoreAsync();
        updateCheckTask = task;
        return task;
    }

    private async Task<UpdateCheckOutcome> RunUpdateCheckCoreAsync()
    {
        var outcome = await UpdateCheckService.CheckAsync();
        preferences = outcome.Succeeded && outcome.Latest is not null
            ? preferences with
            {
                LastUpdateCheckAt = DateTimeOffset.Now.ToString("O"),
                LastKnownLatestVersion = UpdateCheckPolicy.ToDisplayVersion(outcome.Latest.Version),
                LastKnownReleaseUrl = outcome.Latest.ReleaseUrl,
            }
            : preferences with { LastUpdateCheckAt = DateTimeOffset.Now.ToString("O") };
        try
        {
            preferencesStore.Save(preferences);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        if (!outcome.Succeeded)
        {
            WriteAppDiagnostic("update-check failed; downloads are unaffected");
        }

        return outcome;
    }

    /// <summary>Cached state from the last completed check, shown without a new request.</summary>
    private (UpdateStatus Status, LatestReleaseInfo? Latest) ReadCachedUpdateState(string currentVersion)
    {
        if (UpdateCheckPolicy.TryParseVersion(preferences.LastKnownLatestVersion) is not Version known)
        {
            return (UpdateStatus.Unknown, null);
        }

        var latest = new LatestReleaseInfo(
            known,
            preferences.LastKnownLatestVersion!,
            UpdateCheckPolicy.NormalizeReleaseUrl(preferences.LastKnownReleaseUrl));
        return (UpdateCheckPolicy.Evaluate(currentVersion, latest), latest);
    }

    private void AddUpdateCard(ProductVersionInfo version)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = "업데이트",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });

        var statusText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var noteText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = themeManager.GetThemeBrush("DangerTextBrush"),
            Visibility = Visibility.Collapsed,
        };
        var lastCheckedText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
        };
        panel.Children.Add(statusText);
        panel.Children.Add(noteText);
        panel.Children.Add(lastCheckedText);

        // Always renders from the persisted state so the card, the cache, and what the
        // buttons act on cannot disagree; failureNote reports a just-failed attempt
        // without hiding a previously discovered update.
        void Render(string? failureNote = null)
        {
            var (status, latest) = ReadCachedUpdateState(version.SemanticVersion);
            statusText.Text = latest is null && preferences.LastUpdateCheckAt is null
                ? "아직 최신 버전을 확인하지 않았습니다."
                : UpdateCheckPolicy.DescribeForUser(status, version.SemanticVersion, latest);
            noteText.Text = failureNote ?? string.Empty;
            noteText.Visibility = failureNote is null ? Visibility.Collapsed : Visibility.Visible;
            lastCheckedText.Text =
                DateTimeOffset.TryParse(preferences.LastUpdateCheckAt, out var lastChecked)
                    ? $"마지막 확인 시도: {lastChecked.ToLocalTime():yyyy-MM-dd HH:mm}"
                    : "이 확인은 GitHub의 공개 배포 정보만 읽으며 개인 정보를 보내지 않습니다.";
        }

        Render();

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        var checkNow = new Button { Content = "지금 확인" };
        checkNow.Click += async (_, _) =>
        {
            checkNow.IsEnabled = false;
            statusText.Text = "확인하는 중...";
            try
            {
                var outcome = await RunUpdateCheckAsync();
                Render(outcome.Succeeded
                    ? null
                    : "최신 버전을 확인하지 못했습니다. 네트워크 연결을 확인한 뒤 다시 시도해 주세요.");
            }
            finally
            {
                checkNow.IsEnabled = true;
            }
        };
        actions.Children.Add(checkNow);

        var openRelease = new Button { Content = "Release 페이지 열기" };
        openRelease.Click += async (_, _) =>
            _ = await Launcher.LaunchUriAsync(new Uri(
                UpdateCheckPolicy.NormalizeReleaseUrl(preferences.LastKnownReleaseUrl)));
        actions.Children.Add(openRelease);
        panel.Children.Add(actions);

        ContentPanel.Children.Add(CreateCard(panel));
    }
}
