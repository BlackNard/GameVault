namespace GameVault.Core;

/// <summary>
/// The games waiting for their automatic information lookup. A game the owner just added is <b>urgent</b> and goes to the front, so it is never
/// stuck behind (or dropped because of) a long background pass over the whole library. A game is queued once: asking again for a game that is already
/// waiting does nothing, unless the new request is urgent, which moves it to the front.
/// </summary>
public sealed class AutoLookupQueue
{
    public readonly record struct Job(Guid Id, string Name, bool OnlyNeverLookedUp, bool Urgent);

    private readonly LinkedList<Job> jobs = new();
    private readonly object gate = new();

    public int Count { get { lock (gate) return jobs.Count; } }

    public void Enqueue(IEnumerable<Job> items)
    {
        lock (gate)
        {
            var urgent = new List<Job>();
            foreach (var job in items)
            {
                var existing = FindNode(job.Id);
                if (existing is not null)
                {
                    if (!job.Urgent) continue;
                    jobs.Remove(existing);
                }
                if (job.Urgent) urgent.Add(job); else jobs.AddLast(job);
            }
            // Urgent games keep the order they were given, ahead of everything already waiting.
            for (var i = urgent.Count - 1; i >= 0; i--) jobs.AddFirst(urgent[i]);
        }
    }

    public bool TryDequeue(out Job job)
    {
        lock (gate)
        {
            if (jobs.First is null) { job = default; return false; }
            job = jobs.First.Value;
            jobs.RemoveFirst();
            return true;
        }
    }

    public void Clear() { lock (gate) jobs.Clear(); }

    private LinkedListNode<Job>? FindNode(Guid id)
    {
        for (var node = jobs.First; node is not null; node = node.Next) if (node.Value.Id == id) return node;
        return null;
    }
}
