using DownloadRouter.Core.Models;
using DownloadRouter.Core.Paths;

namespace DownloadRouter.App.Services;

/// <summary>
/// Checks every enabled rule's base folder in the background (deleted folder, unplugged or
/// offline drive) so the rules list and the home page can flag "needs attention" without
/// touching the disk on the UI thread. A slow network path times out instead of blocking.
/// </summary>
public sealed class RootHealthMonitor
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RecheckInterval = TimeSpan.FromSeconds(60);

    private readonly Dictionary<Guid, StorageRootStatus> statuses = [];
    private string? lastRulesKey;
    private DateTimeOffset lastCheck = DateTimeOffset.MinValue;
    private bool running;

    /// <summary>Raised on the UI thread after a check finished with a different result.</summary>
    public event EventHandler? Changed;

    public IReadOnlyDictionary<Guid, StorageRootStatus> Statuses => statuses;

    public int AttentionCount => statuses.Values.Count(static status => status != StorageRootStatus.Available);

    public bool NeedsAttention(Guid ruleId)
        => statuses.TryGetValue(ruleId, out var status) && status != StorageRootStatus.Available;

    public StorageRootStatus? StatusOf(Guid ruleId)
        => statuses.TryGetValue(ruleId, out var status) ? status : null;

    /// <summary>Starts a check when the rules changed or the last check is older than a minute.</summary>
    public void Observe(IReadOnlyList<DownloadRule> rules, bool force = false)
    {
        var enabled = rules.Where(static rule => rule.IsEnabled).ToArray();
        var key = string.Join('|', enabled.Select(static rule => rule.Id.ToString("N") + rule.StorageRoot));
        var due = force
            || !string.Equals(key, lastRulesKey, StringComparison.Ordinal)
            || DateTimeOffset.Now - lastCheck > RecheckInterval;
        if (!due || running)
        {
            return;
        }

        lastRulesKey = key;
        lastCheck = DateTimeOffset.Now;
        _ = CheckAsync(enabled);
    }

    private async Task CheckAsync(IReadOnlyList<DownloadRule> rules)
    {
        running = true;
        try
        {
            var results = await Task.WhenAll(rules.Select(async rule =>
            {
                string resolved;
                try
                {
                    resolved = AppServices.PathResolver.Resolve(rule.StorageRoot);
                }
                catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
                {
                    return (rule.Id, StorageRootStatus.Invalid);
                }

                var status = await StorageRootHealth.CheckAsync(resolved, probeWrite: false, CheckTimeout, CancellationToken.None)
                    .ConfigureAwait(false);
                return (rule.Id, status);
            })).ConfigureAwait(true);

            var changed = results.Length != statuses.Count
                || results.Any(result => !statuses.TryGetValue(result.Item1, out var previous) || previous != result.Item2);
            statuses.Clear();
            foreach (var (id, status) in results)
            {
                statuses[id] = status;
            }

            if (changed)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            running = false;
        }
    }
}
