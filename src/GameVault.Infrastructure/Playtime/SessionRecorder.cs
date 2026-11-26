using GameVault.Core;
using GameVault.Infrastructure.Launching;
using GameVault.Infrastructure.Storage;
using System.Collections.Concurrent;

namespace GameVault.Infrastructure.Playtime;

public sealed class SessionRecorder
{
    private readonly AppDataStore store;
    private readonly IPlaytimeClock clock;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<Guid, ActiveSession> active = [];
    private readonly ConcurrentDictionary<Guid, SessionProgress> progress = new();
    private readonly object clockGate = new();
    private readonly Dictionary<Guid, (long Awake, DateTimeOffset Utc)> endpoints = [];
    public SessionRecorder(AppDataStore store, IPlaytimeClock clock) { this.store = store; this.clock = clock; }

    /// <returns>The new session's id.</returns>
    public async Task<Guid> BeginAsync(Guid gameId, string executable, IGameProcess process, string? groupName = null)
    {
        var now = clock.UtcNow;
        var awake = clock.AwakeTicks;
        var sessionId = Guid.NewGuid();
        await BeginCore(new(1, gameId, sessionId, now, now, 0, SessionState.Active,
            process.Identity.Id, process.Identity.CreatedUtcTicks, executable, groupName ?? process.RecoveryGroupName, 0), awake).ConfigureAwait(false);
        return sessionId;
    }

    public Task ResumeAsync(SessionJournal journal, IGameProcess process, string? groupName = null)
    {
        if (journal.State == SessionState.Completed) throw new ArgumentException("A completed session cannot be resumed.");
        return BeginCore(journal with {
        State = SessionState.Active, RecoveryCount = checked(journal.RecoveryCount + 1),
        ProcessId = process.Identity.Id, ProcessCreatedUtcTicks = process.Identity.CreatedUtcTicks,
        ProcessGroupName = groupName ?? process.RecoveryGroupName
        }, clock.AwakeTicks);
    }

    private async Task BeginCore(SessionJournal journal, long awakeBaseline)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (active.TryGetValue(journal.GameId, out var pending))
            {
                if (!pending.Closed) throw new InvalidOperationException("This game already has an active timing session.");
                await Persist(pending).ConfigureAwait(false);
                if (pending.Warning is not null) throw new IOException(pending.Warning);
                active.Remove(journal.GameId);
            }
            var game = await store.LoadProfileAsync(journal.GameId).ConfigureAwait(false);
            if (game.Statistics.State != DocumentState.Ready) throw new IOException("Recorded playtime needs recovery before a new session can be timed.");
            var documents = await ReadAsync(journal.GameId).ConfigureAwait(false);
            if (documents.Any(item => item.Document.IsReadOnly)) throw new IOException("A session journal needs attention. Existing data was preserved.");
            var statistics = game.Statistics.Value!;
            var total = statistics.Adjustments.Aggregate(statistics.Sessions.Aggregate(0L, (sum, s) => checked(sum + s.DurationSeconds)), (sum, a) => checked(sum + a.Seconds));
            var recorded = statistics.Sessions.SingleOrDefault(s => s.Id == journal.SessionId)?.DurationSeconds ?? 0;
            lock (clockGate) endpoints.Remove(journal.GameId);
            var session = new ActiveSession(journal, awakeBaseline, checked(total - recorded));
            active.Add(journal.GameId, session);
            await Persist(session).ConfigureAwait(false);
            Publish(session);
        }
        finally { gate.Release(); }
    }

    public async Task SampleAsync(Guid gameId, bool persist = false, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!active.TryGetValue(gameId, out var session) || session.Closed) return;
            Sample(session);
            if (persist || session.Journal.DurationTicks - session.LastAttemptTicks >= TimeSpan.FromSeconds(10).Ticks)
                await Persist(session).ConfigureAwait(false);
            Publish(session);
        }
        finally { gate.Release(); }
    }

    public async Task UpdateProcessAsync(Guid gameId, IGameProcess observed)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (active.TryGetValue(gameId, out var session) && !session.Closed)
                session.Journal = session.Journal with { ProcessId = observed.Identity.Id, ProcessCreatedUtcTicks = observed.Identity.CreatedUtcTicks };
        }
        finally { gate.Release(); }
    }

    public async Task EndAsync(Guid gameId, SessionState state, long? awakeEnd = null, DateTimeOffset? observedEnd = null)
    {
        if (state == SessionState.Active) throw new ArgumentException("An end state is required.");
        var end = CaptureEnd(gameId, awakeEnd, observedEnd);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!active.TryGetValue(gameId, out var session)) return;
            if (!session.Closed) { Sample(session, end.Awake, end.Utc); session.Journal = session.Journal with { State = state }; session.Closed = true; }
            await Persist(session).ConfigureAwait(false);
            Publish(session);
            if (session.Warning is null) active.Remove(gameId);
        }
        finally { gate.Release(); }
    }

    private void Sample(ActiveSession session, long? awake = null, DateTimeOffset? observed = null)
    {
        lock (clockGate)
        {
        var ticks = awake ?? clock.AwakeTicks;
        var utc = observed ?? clock.UtcNow;
        if (endpoints.TryGetValue(session.Journal.GameId, out var end) && ticks > end.Awake)
        { ticks = end.Awake; utc = end.Utc; }
        var elapsed = Math.Max(0, ticks - session.LastAwakeTicks);
        session.LastAwakeTicks = ticks;
        session.Journal = session.Journal with
        {
            DurationTicks = checked(session.Journal.DurationTicks + elapsed),
            ObservedAt = utc > session.Journal.ObservedAt ? utc : session.Journal.ObservedAt
        };
        }
    }

    private async Task Persist(ActiveSession session)
    {
        session.LastAttemptTicks = session.Journal.DurationTicks;
        try { await store.SaveSessionJournalAsync(session.Journal).ConfigureAwait(false); session.Warning = null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { session.Warning = "Playtime could not be saved. The last successful checkpoint is kept. " + error.Message; }
    }

    private void Publish(ActiveSession session) => progress[session.Journal.GameId] = new(session.Journal.GameId, session.Journal.SessionId,
        session.Journal.DurationTicks / TimeSpan.TicksPerSecond,
        checked(session.BaseSeconds + session.Journal.DurationTicks / TimeSpan.TicksPerSecond), session.Warning, !session.Closed);

    public SessionProgress? GetProgress(Guid gameId) => progress.GetValueOrDefault(gameId);
    internal (long Awake, DateTimeOffset Utc) CaptureEnd(Guid gameId, long? awake = null, DateTimeOffset? utc = null)
    {
        // This short lock never spans I/O: even a queued checkpoint must obey the exit boundary.
        lock (clockGate)
        {
            if (endpoints.TryGetValue(gameId, out var end)) return end;
            end = (awake ?? clock.AwakeTicks, utc ?? clock.UtcNow);
            endpoints.Add(gameId, end); return end;
        }
    }
    public Task<StoredSession[]> ReadAsync(Guid gameId) => store.LoadSessionJournalsAsync(gameId);

    public async Task FlushClosedAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var session in active.Values.Where(s => s.Closed).ToArray())
            {
                await Persist(session).ConfigureAwait(false); Publish(session);
                if (session.Warning is null) active.Remove(session.Journal.GameId);
            }
        }
        finally { gate.Release(); }
    }

    private sealed class ActiveSession(SessionJournal journal, long awakeTicks, long baseSeconds)
    {
        public SessionJournal Journal = journal;
        public long LastAwakeTicks = awakeTicks;
        public long LastAttemptTicks;
        public long BaseSeconds { get; } = baseSeconds;
        public bool Closed;
        public string? Warning;
    }
}
