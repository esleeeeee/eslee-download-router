using DownloadRouter.Core.Localization;
using DownloadRouter.Core.Models;

namespace DownloadRouter.App.ViewModels;

/// <summary>
/// Waiting files of one rule. Bulk folder choices stay inside one rule so files never mix
/// base folders.
/// </summary>
public sealed class PendingRuleGroup(DownloadRule rule, IReadOnlyList<DownloadJob> jobs, string resolvedRoot)
{
    public DownloadRule Rule { get; } = rule;

    public IReadOnlyList<DownloadJob> Jobs { get; } = jobs;

    public string Root { get; } = resolvedRoot;

    public string Title => AppText.Pick($"{Rule.Name} · 파일 {Jobs.Count}개", $"{Rule.Name} · {Jobs.Count} file(s)");

    public string RootText => AppText.Pick("기준 폴더: ", "Base folder: ") + Root;

    public override string ToString() => Title;
}
