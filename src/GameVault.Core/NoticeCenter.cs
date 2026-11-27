namespace GameVault.Core;

public enum NoticeKind { Update, UnexpectedExit, BackupAttention, SaveFound }

/// <summary>One entry of the notifications list (the bell in the title bar). <see cref="Id"/> identifies the thing it is about, so posting it again replaces it.</summary>
public sealed record Notice(string Id, NoticeKind Kind, string Title, string? Detail, DateTimeOffset CreatedAt, bool IsProblem = false, string? ActionLabel = null);

/// <summary>
/// The list behind the bell: notices that stay until they are dismissed or resolved (unlike toasts, which vanish). Newest first. A notice that
/// has not been looked at counts towards the badge; opening the list marks everything as seen. Posting the same notice again keeps it seen,
/// unless its text changed. Used from the UI thread only.
/// </summary>
public sealed class NoticeCenter(int capacity = 50)
{
    private readonly List<Notice> items = [];
    private readonly HashSet<string> seen = [];

    public event EventHandler? Changed;

    /// <summary>Newest first.</summary>
    public IReadOnlyList<Notice> Items => [.. items.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => items.IndexOf(n))];

    public int Count => items.Count;
    public int UnseenCount => items.Count(n => !seen.Contains(n.Id));
    public bool IsSeen(string id) => seen.Contains(id);

    /// <summary>Adds the notice or replaces the one with the same id. A changed title or detail makes it unseen again.</summary>
    public void Post(Notice notice)
    {
        var index = items.FindIndex(n => n.Id == notice.Id);
        if (index >= 0)
        {
            var old = items[index];
            if (old with { CreatedAt = notice.CreatedAt } == notice) return;          // the same notice posted again (only the time differs): nothing changes
            if (old.Title != notice.Title || old.Detail != notice.Detail) seen.Remove(notice.Id);
            items[index] = notice with { CreatedAt = old.CreatedAt };
        }
        else
        {
            items.Add(notice);
            if (items.Count > capacity)
            {
                var oldest = items.OrderBy(n => n.CreatedAt).First();
                items.Remove(oldest);
                seen.Remove(oldest.Id);
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Remove(string id)
    {
        if (items.RemoveAll(n => n.Id == id) == 0) return false;
        seen.Remove(id);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Removes every notice of this kind whose id is not in <paramref name="keep"/> (the thing it was about has been resolved).</summary>
    public void RetainOnly(NoticeKind kind, IReadOnlySet<string> keep)
    {
        var gone = items.Where(n => n.Kind == kind && !keep.Contains(n.Id)).Select(n => n.Id).ToArray();
        foreach (var id in gone) { items.RemoveAll(n => n.Id == id); seen.Remove(id); }
        if (gone.Length > 0) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MarkAllSeen()
    {
        var before = UnseenCount;
        foreach (var notice in items) seen.Add(notice.Id);
        if (before > 0) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (items.Count == 0) return;
        items.Clear();
        seen.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
