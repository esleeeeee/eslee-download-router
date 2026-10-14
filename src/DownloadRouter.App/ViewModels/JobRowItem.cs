using DownloadRouter.App.Controls;
using DownloadRouter.App.Services;
using DownloadRouter.Core.Models;
using Microsoft.UI.Xaml;
using Windows.UI.Text;

namespace DownloadRouter.App.ViewModels;

/// <summary>One download in a list. Updated in place so selection and scroll position survive refreshes.</summary>
public sealed class JobRowItem : ObservableObject
{
    private DownloadJob job;
    private string ruleName = string.Empty;

    public JobRowItem(DownloadJob job, string ruleName)
    {
        this.job = job;
        this.ruleName = ruleName;
    }

    public Guid Id => job.Id;

    public DownloadJob Job => job;

    public string FileName => JobText.DisplayFileName(job);

    public string Subtitle => $"{JobText.SourceHost(job)} · {JobText.RelativeTime(job.CreatedAt)}";

    public string RuleName => ruleName;

    public string StatusText => JobText.ShortStatus(job);

    public BadgeKind Kind => JobText.Kind(job);

    public string Glyph => JobText.Glyph(job);

    public TextDecorations FileNameDecorations
        => job.BrowserState == BrowserTransferState.Cancelled ? TextDecorations.Strikethrough : TextDecorations.None;

    public double RowOpacity => job.BrowserState == BrowserTransferState.Cancelled ? 0.6 : 1;

    public Visibility RuleVisibility => string.IsNullOrEmpty(ruleName) ? Visibility.Collapsed : Visibility.Visible;

    public void Update(DownloadJob updated, string updatedRuleName)
    {
        job = updated;
        ruleName = updatedRuleName;
        Raise(string.Empty);
    }

    /// <summary>Screen readers announce rows by this text.</summary>
    public override string ToString() => $"{FileName}, {StatusText}, {Subtitle}";
}
