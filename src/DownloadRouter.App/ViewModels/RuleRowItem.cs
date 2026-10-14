using DownloadRouter.App.Controls;
using DownloadRouter.App.Services;
using DownloadRouter.Core.Localization;
using DownloadRouter.Core.Models;
using DownloadRouter.Core.Rules;

namespace DownloadRouter.App.ViewModels;

/// <summary>One saved rule in the rules list.</summary>
public sealed class RuleRowItem : ObservableObject
{
    private DownloadRule rule;
    private bool needsAttention;

    public RuleRowItem(DownloadRule rule, bool needsAttention)
    {
        this.rule = rule;
        this.needsAttention = needsAttention;
    }

    public Guid Id => rule.Id;

    public DownloadRule Rule => rule;

    public string Name => rule.Name;

    public string Summary => RulePresentation.MatchScopeSummary(rule.MatchType, rule.MatchValue);

    public bool NeedsAttention => needsAttention;

    public string StatusText => !rule.IsEnabled
        ? AppText.Pick("꺼짐", "Off")
        : needsAttention ? AppText.Pick("확인 필요", "Check folder") : AppText.Pick("사용 중", "On");

    public BadgeKind StatusKind => !rule.IsEnabled
        ? BadgeKind.Neutral
        : needsAttention ? BadgeKind.Caution : BadgeKind.Success;

    public BadgeKind PlateKind => !rule.IsEnabled
        ? BadgeKind.Neutral
        : needsAttention ? BadgeKind.Caution : BadgeKind.Accent;

    public string Glyph => needsAttention && rule.IsEnabled
        ? ""
        : rule.StorageMode == StorageMode.SelectSubfolder ? "" : "";

    public string DestinationText => AppServices.ResolveForDisplay(rule.StorageRoot);

    public void Update(DownloadRule updated, bool attention)
    {
        rule = updated;
        needsAttention = attention;
        Raise(string.Empty);
    }

    public override string ToString() => $"{Name}, {StatusText}, {Summary}";
}

/// <summary>ComboBox option with a readable name for screen readers.</summary>
public sealed record Option<T>(T Value, string Label, string Description, string Example)
{
    public override string ToString() => Label;
}
