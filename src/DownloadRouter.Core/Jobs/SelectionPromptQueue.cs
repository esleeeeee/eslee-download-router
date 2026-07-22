namespace DownloadRouter.Core.Jobs;

public sealed class SelectionPromptQueue
{
    private readonly Queue<Guid> queue = new();
    private readonly HashSet<Guid> queued = [];

    public int Count => queue.Count;

    public bool Enqueue(Guid jobId)
    {
        if (jobId == Guid.Empty || !queued.Add(jobId))
        {
            return false;
        }

        queue.Enqueue(jobId);
        return true;
    }

    public bool EnqueueFirst(Guid jobId)
    {
        if (jobId == Guid.Empty || !queued.Add(jobId))
        {
            return false;
        }

        var remaining = queue.ToArray();
        queue.Clear();
        queue.Enqueue(jobId);
        foreach (var id in remaining)
        {
            queue.Enqueue(id);
        }

        return true;
    }

    public bool TryDequeue(out Guid jobId)
    {
        if (!queue.TryDequeue(out jobId))
        {
            return false;
        }

        queued.Remove(jobId);
        return true;
    }

    public bool Remove(Guid jobId)
    {
        if (!queued.Remove(jobId))
        {
            return false;
        }

        var remaining = queue.Where(id => id != jobId).ToArray();
        queue.Clear();
        foreach (var id in remaining)
        {
            queue.Enqueue(id);
        }

        return true;
    }
}
