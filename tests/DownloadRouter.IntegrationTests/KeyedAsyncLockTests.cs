using DownloadRouter.Agent;

namespace DownloadRouter.IntegrationTests;

public sealed class KeyedAsyncLockTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task TheSameKeyIsMutuallyExclusiveAndTheEntryIsRemovedWhenUnused()
    {
        var locks = new KeyedAsyncLock<Guid>();
        var key = Guid.NewGuid();

        var first = await locks.AcquireAsync(key);
        var second = locks.AcquireAsync(key);
        await Task.Delay(50);
        Assert.False(second.IsCompleted);
        Assert.Equal(1, locks.ActiveKeyCount);

        first.Dispose();
        var secondHandle = await second.WaitAsync(Timeout);
        Assert.Equal(1, locks.ActiveKeyCount);
        secondHandle.Dispose();
        secondHandle.Dispose();

        Assert.Equal(0, locks.ActiveKeyCount);
    }

    [Fact]
    public async Task DifferentKeysDoNotWaitForEachOther()
    {
        var locks = new KeyedAsyncLock<Guid>();
        using var first = await locks.AcquireAsync(Guid.NewGuid());
        using var second = await locks.AcquireAsync(Guid.NewGuid()).WaitAsync(Timeout);

        Assert.Equal(2, locks.ActiveKeyCount);
    }

    [Fact]
    public async Task ACancelledWaiterLeavesNoEntryBehind()
    {
        var locks = new KeyedAsyncLock<Guid>();
        var key = Guid.NewGuid();
        var held = await locks.AcquireAsync(key);
        using var cancellation = new CancellationTokenSource();

        var waiter = locks.AcquireAsync(key, cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        Assert.Equal(1, locks.ActiveKeyCount);

        held.Dispose();
        Assert.Equal(0, locks.ActiveKeyCount);
    }

    [Fact]
    public async Task OverlappingMultiKeyLocksInOppositeOrderNeverDeadlock()
    {
        var locks = new KeyedAsyncLock<Guid>();
        var keys = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        var reversed = keys.Reverse().ToArray();
        var counter = 0;

        var work = Enumerable.Range(0, 40).Select(index => Task.Run(async () =>
        {
            using var held = await locks.AcquireManyAsync(index % 2 == 0 ? keys : reversed);
            var observed = Interlocked.Increment(ref counter);
            Assert.Equal(1, observed);
            await Task.Yield();
            Interlocked.Decrement(ref counter);
        }));

        await Task.WhenAll(work).WaitAsync(Timeout);
        Assert.Equal(0, locks.ActiveKeyCount);
    }

    [Fact]
    public async Task ManyWritersOnOneKeyNeverOverlap()
    {
        var locks = new KeyedAsyncLock<string>(StringComparer.Ordinal);
        var inside = 0;
        var maximum = 0;

        await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(async () =>
        {
            using var held = await locks.AcquireAsync("job");
            var now = Interlocked.Increment(ref inside);
            InterlockedMax(ref maximum, now);
            await Task.Yield();
            Interlocked.Decrement(ref inside);
        }))).WaitAsync(Timeout);

        Assert.Equal(1, maximum);
        Assert.Equal(0, locks.ActiveKeyCount);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value
            && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
