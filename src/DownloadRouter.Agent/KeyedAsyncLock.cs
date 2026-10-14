namespace DownloadRouter.Agent;

/// <summary>
/// An async mutual-exclusion lock per key. Entries are reference counted and removed as soon
/// as nobody holds or waits for them, so the table stays as small as the work in flight.
/// The lock is not reentrant: a holder must not acquire the same key again.
/// </summary>
public sealed class KeyedAsyncLock<TKey>
    where TKey : notnull
{
    private readonly Dictionary<TKey, Entry> entries;
    private readonly Lock gate = new();

    public KeyedAsyncLock(IEqualityComparer<TKey>? comparer = null)
    {
        entries = new Dictionary<TKey, Entry>(comparer);
    }

    /// <summary>Keys that are currently held or awaited. Exposed for diagnostics and tests.</summary>
    public int ActiveKeyCount
    {
        get
        {
            lock (gate)
            {
                return entries.Count;
            }
        }
    }

    public async Task<IDisposable> AcquireAsync(TKey key, CancellationToken cancellationToken = default)
    {
        Entry entry;
        lock (gate)
        {
            if (!entries.TryGetValue(key, out var existing))
            {
                existing = new Entry();
                entries.Add(key, existing);
            }

            entry = existing;
            entry.References++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Release(key, entry, holdsSemaphore: false);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    /// <summary>
    /// Acquires every distinct key in a fixed sort order, so two callers locking overlapping
    /// sets can never deadlock. Disposing the result releases them all.
    /// </summary>
    public async Task<IDisposable> AcquireManyAsync(
        IEnumerable<TKey> keys,
        IComparer<TKey>? comparer = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var ordered = keys.Distinct().Order(comparer ?? Comparer<TKey>.Default).ToList();
        var held = new List<IDisposable>(ordered.Count);
        try
        {
            foreach (var key in ordered)
            {
                held.Add(await AcquireAsync(key, cancellationToken).ConfigureAwait(false));
            }
        }
        catch
        {
            ReleaseAll(held);
            throw;
        }

        return new CompositeReleaser(held);
    }

    private void Release(TKey key, Entry entry, bool holdsSemaphore)
    {
        if (holdsSemaphore)
        {
            entry.Semaphore.Release();
        }

        lock (gate)
        {
            entry.References--;
            if (entry.References == 0)
            {
                entries.Remove(key);
            }
        }
    }

    private static void ReleaseAll(List<IDisposable> held)
    {
        for (var index = held.Count - 1; index >= 0; index--)
        {
            held[index].Dispose();
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int References { get; set; }
    }

    private sealed class Releaser(KeyedAsyncLock<TKey> owner, TKey key, Entry entry) : IDisposable
    {
        private int released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                owner.Release(key, entry, holdsSemaphore: true);
            }
        }
    }

    private sealed class CompositeReleaser(List<IDisposable> held) : IDisposable
    {
        private int released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                ReleaseAll(held);
            }
        }
    }
}
