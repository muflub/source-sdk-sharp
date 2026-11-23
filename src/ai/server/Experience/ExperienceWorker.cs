using System.Diagnostics;
using System.Security.Cryptography;

namespace SourceSharp.Ai.Server.Experience;

/// <summary>
/// What the experience worker last said of itself, for the game's thread to
/// read: the key it is on, the file, whether it fell back to memory and why,
/// why the store is not attached yet while it retries a busy file
/// (<see cref="IExperienceStore.Waiting"/>), and the rows the store held at
/// its last load or flush.
/// </summary>
public sealed record ExperienceStatus(ExperienceKey Key, string Path, bool MemoryOnly, string? Refusal, string? Waiting, ExperienceCounts Counts, long LastFlushStamp)
{
    /// <summary>Seconds since the last flush that wrote; -1 for none yet.</summary>
    public double SecondsSinceFlush => LastFlushStamp == 0 ? -1d : Stopwatch.GetElapsedTime(LastFlushStamp).TotalSeconds;
}

/// <summary>A jump a player showed as the worker judged it (lane G6a): what, by which class, from and to which bins, its seconds in the air and health, and the verdict.</summary>
public readonly record struct ShownJumpSeen(ShownJumpKind Kind, int Class, WorldBin From, WorldBin To, double Seconds, int Health, ShownJumpVerdict Verdict);

/// <summary>
/// The off-thread half of the experience feed (plan_bots_teams_tf2 Part G,
/// G.3): one worker thread of its own (an <see cref="AiWorkerPool"/> of one,
/// named <c>ai-experience</c>) on which every accumulation and every store
/// call runs, in the order it was posted. The game's thread only posts: it
/// never accumulates, never opens, reads or writes a store, and never waits,
/// but for <see cref="Close"/> at a level's end.
///
/// <para>Its own thread and not the brains' pool, for three reasons: a store
/// call can wait the busy timeout for another process's write, which would
/// hold a brain's worker; <c>ai_sync 1</c> runs the brains inline and stops
/// their pool, and the rule holds then too; and a pool discards what is
/// queued when it stops, where a level's last flush must run.</para>
///
/// <para>A file that is refused (or no database assembly in the mod's list)
/// leaves the server on a <see cref="MemoryExperienceStore"/>: it learns for
/// the life of the process and says why in <see cref="ExperienceStatus.Refusal"/>.</para>
///
/// <para>A file another process held locked as it was opened leaves the store
/// <see cref="IExperienceStore.Waiting"/> (G.11's ruling: retry, then go on):
/// the brains read the empty snapshot the store answers, and what a flush
/// would write is kept, summed into one delta (as a merge the file refused
/// for a lock is), so it grows with the map's rows, not with time. Before each
/// job the worker looks whether the store attached; when it has, the snapshot
/// is read again from the file and the next flush writes what was kept. A
/// level that ends first drops it, as a refused merge's is dropped.</para>
/// </summary>
public sealed class ExperienceWorker(IExperienceStores? stores) : IDisposable
{
    /// <summary>Posts waiting past this are dropped and counted: a worker stuck on a file must not grow without bound.</summary>
    public const int QueueLimit = 4096;

    /// <summary>How long a level's end waits for its last flush before the worker is stopped regardless.</summary>
    public const int CloseWaitMs = 5000;

    private readonly AiWorkerPool _pool = new("ai-experience");

    // The worker thread's own: nothing below is touched from another thread while the pool runs.
    private readonly ExperienceAccumulator _accumulator = new();
    private IExperienceStore? _store;
    private ExperienceDelta? _kept;
    private ExperienceKey _key;
    private string _path = "";
    private string? _refusal;
    private bool _waiting;
    private double _halfLife = ExperienceDecay.DefaultHalfLife;
    private long _flushStamp;
    private readonly ShownJumpLedger _ledger = new();
    private readonly List<(LinkKey Key, LinkStat Stat)> _jumpRows = new();

    // Handed between the threads.
    private volatile MapExperience? _loaded;
    private volatile ExperienceStatus? _status;
    private int _owner = -1;
    private long _frames, _events, _flushes, _rowsMerged, _mergeFailures, _loads, _dropped, _onOwner, _attaches;
    private readonly long[] _verdicts = new long[(int)ShownJumpVerdict.NoWay + 1];
    private readonly Queue<ShownJumpSeen> _seen = new();

    /// <summary>Whether the worker thread runs.</summary>
    public bool Running => _pool.Running;

    public long Frames => Interlocked.Read(ref _frames);

    public long Events => Interlocked.Read(ref _events);

    /// <summary>Flushes that wrote rows.</summary>
    public long Flushes => Interlocked.Read(ref _flushes);

    public long RowsMerged => Interlocked.Read(ref _rowsMerged);

    /// <summary>Merges the store answered false to (a file held locked too long, or one that went bad).</summary>
    public long MergeFailures => Interlocked.Read(ref _mergeFailures);

    /// <summary>Snapshots read from the store.</summary>
    public long Loads => Interlocked.Read(ref _loads);

    /// <summary>Stores that waited on a busy file and then attached.</summary>
    public long Attaches => Interlocked.Read(ref _attaches);

    /// <summary>Posts dropped: the worker was stopped, or <see cref="QueueLimit"/> were waiting.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Work that ran on the thread that started the worker (the game's): always 0, and <c>/ai/counters</c> shows it.</summary>
    public long RanOnOwner => Interlocked.Read(ref _onOwner);

    /// <summary>Work that threw; the worker goes on.</summary>
    public long Faults => _pool.Faults;

    public string? LastFault => _pool.LastFault;

    /// <summary>What the worker last said of itself; null before it opened anything.</summary>
    public ExperienceStatus? Status => _status;

    /// <summary>Starts the worker thread; the caller's thread is the one work must never run on.</summary>
    public void Start()
    {
        _owner = Environment.CurrentManagedThreadId;
        _pool.Start(1);
    }

    /// <summary>The snapshot a load finished with since the last call, or null: the game's thread takes it and puts it where the brains read.</summary>
    public MapExperience? TakeLoaded() => Interlocked.Exchange(ref _loaded, null);

    /// <summary>
    /// Works on <paramref name="map"/> in <paramref name="mode"/> from here on:
    /// what was gathered under the last key is flushed to it first, the store
    /// at <paramref name="path"/> is opened if it is not the one open, and the
    /// key's snapshot is loaded. The map's checksum is
    /// <paramref name="checksum"/>, or, without one, the head of
    /// <paramref name="bsp"/>'s SHA-256, worked out here on the worker.
    /// </summary>
    public bool Open(string path, string map, string mode, string? checksum, byte[]? bsp, double halfLife)
        => Post(() =>
        {
            FlushNow(_halfLife);
            // What the last key's file would not take is not this key's.
            _kept = null;
            _key = new ExperienceKey(map, checksum ?? (bsp is null ? "" : Convert.ToHexStringLower(SHA256.HashData(bsp))[..16]), mode);
            _halfLife = halfLife;
            // Another key's exemplars are not this key's (lane G6a): the load below seeds this one's.
            _ledger.Clear();
            if (_store is null || _path != path)
            {
                _store?.Dispose();
                _path = path;
                _refusal = null;
                _store = stores?.Open(path);
                if (_store is null)
                {
                    _refusal = "this mod lists no experience database (SourceSharp.Ai.Store)";
                    _store = new MemoryExperienceStore();
                }

                // A file refused as it was opened is closed before anything is asked of it.
                FellBack();
                _waiting = _store.Waiting is not null;
            }

            Reload();
        });

    /// <summary>A sampled frame of every player; <paramref name="actors"/> is the worker's from here on.</summary>
    public bool Frame(ActorSample[] actors, double seconds)
        => Post(() =>
        {
            _accumulator.Frame(actors, seconds);
            Interlocked.Increment(ref _frames);
        });

    public bool Damage(CombatSample victim, CombatSample? attacker, double amount)
        => Post(() =>
        {
            _accumulator.Damage(in victim, attacker, amount);
            Interlocked.Increment(ref _events);
        });

    public bool Kill(CombatSample victim, CombatSample? killer, int weaponClass)
        => Post(() =>
        {
            _accumulator.Kill(in victim, killer, weaponClass);
            Interlocked.Increment(ref _events);
        });

    /// <summary>A try of a link (lane G5e): its row and what it added.</summary>
    public bool Link(LinkKey key, LinkStat stat)
        => Post(() =>
        {
            _accumulator.Link(key, stat);
            Interlocked.Increment(ref _events);
        });

    /// <summary>
    /// A jump a player showed (lane G6a), made as <paramref name="cls"/>: judged here, off the game's thread, by
    /// <paramref name="judge"/> against the level's graph and <paramref name="links"/> (null for none), and when the level
    /// has no way for it, its count row and (the first time) its exemplar row are added (<see cref="ShownJumpLedger"/>).
    /// <paramref name="judge"/> is the worker's alone from here on.
    /// </summary>
    public bool ShownJump(ShownJumpRecipe jump, int cls, ShownJumpJudge judge, ExperienceLinkKeys? links, double tickSeconds)
        => Post(() =>
        {
            ShownJumpVerdict verdict = judge.Judge(jump, cls, links);
            Interlocked.Increment(ref _verdicts[(int)verdict]);
            Interlocked.Increment(ref _events);
            Seen(new ShownJumpSeen(jump.Kind, cls, jump.From, jump.To, jump.AirCommands * tickSeconds, jump.HealthCost, verdict));
            if (!ShownJumpJudge.Learned(verdict))
                return;
            _jumpRows.Clear();
            _ledger.Rows(jump, cls, tickSeconds, _jumpRows);
            foreach ((LinkKey key, LinkStat stat) in _jumpRows)
                _accumulator.Link(key, stat);
        });

    /// <summary>The jumps players showed that were judged <paramref name="verdict"/> since the worker was made.</summary>
    public long ShownJumps(ShownJumpVerdict verdict) => Interlocked.Read(ref _verdicts[(int)verdict]);

    /// <summary>The last <see cref="SeenKept"/> jumps judged, oldest first: what each was and what the judge made of it.</summary>
    public IReadOnlyList<ShownJumpSeen> RecentShownJumps()
    {
        lock (_seen)
            return [.. _seen];
    }

    /// <summary>The jumps judged that <see cref="RecentShownJumps"/> keeps.</summary>
    public const int SeenKept = 16;

    private void Seen(ShownJumpSeen seen)
    {
        lock (_seen)
        {
            _seen.Enqueue(seen);
            while (_seen.Count > SeenKept)
                _seen.Dequeue();
        }
    }

    /// <summary>A place held, or set up (lane G4b): its row's context, kind, place and yaw, and what it added.</summary>
    public bool Spot(ExperienceContext context, int kind, AiVector at, float yaw, SpotStat stat)
        => Post(() =>
        {
            _accumulator.Spot(context, kind, at, yaw, stat);
            Interlocked.Increment(ref _events);
        });

    /// <summary>A choice's result (lane G4b): its row and what it added.</summary>
    public bool Outcome(OutcomeKey key, OutcomeStat stat)
        => Post(() =>
        {
            _accumulator.Outcome(key, stat);
            Interlocked.Increment(ref _events);
        });

    /// <summary>A round ended: its row is added and everything gathered is written.</summary>
    public bool RoundEnd(int winningRole, double halfLife)
        => Post(() =>
        {
            _accumulator.RoundEnd(winningRole);
            FlushNow(halfLife);
        });

    /// <summary>A round began: the round's tallies start over and the snapshot is read again, the one time it is.</summary>
    public bool RoundStart(double halfLife)
        => Post(() =>
        {
            _accumulator.RoundStart();
            _halfLife = halfLife;
            Reload();
        });

    /// <summary>Writes what was gathered since the last flush.</summary>
    public bool Flush(double halfLife) => Post(() => FlushNow(halfLife));

    /// <summary>Has the worker look whether a waiting store attached (every job looks first): the game's thread posts it while the store waits, so a level with nothing to gather notices too.</summary>
    public bool CheckAttach() => Post(static () => { });

    /// <summary>
    /// Waits until everything posted before this call has run, at most
    /// <paramref name="waitMs"/>: for the facts and for a debugging read. The
    /// game never calls it.
    /// </summary>
    public bool Settle(int waitMs = CloseWaitMs)
    {
        using var ran = new ManualResetEventSlim();
        return _pool.Post(new Job(this, ran.Set)) && ran.Wait(waitMs);
    }

    /// <summary>
    /// A level's end: the last flush, the store closed, the thread stopped and
    /// joined. The one place the game's thread waits for the worker, at most
    /// <paramref name="waitMs"/> for the flush to start and then for the store
    /// call in flight.
    /// </summary>
    public void Close(double halfLife, int waitMs = CloseWaitMs)
    {
        if (!_pool.Running)
            return;
        var closed = new ManualResetEventSlim();
        bool posted = _pool.Post(new Job(this, () =>
        {
            FlushNow(halfLife);
            Shut();
            closed.Set();
        }));
        if (posted)
            closed.Wait(waitMs);
        _pool.Stop();
        closed.Dispose();
        // Stopped before the closing work ran (a file held locked for longer than the wait): no worker is left to close the store.
        Shut();
    }

    public void Dispose()
    {
        Close(_halfLife);
        _pool.Dispose();
    }

    private void Shut()
    {
        _store?.Dispose();
        _store = null;
        _waiting = false;
        _path = "";
        _key = default;
        _kept = null;
    }

    private bool Post(Action work)
    {
        if (_pool.Queued < QueueLimit && _pool.Post(new Job(this, work)))
            return true;
        Interlocked.Increment(ref _dropped);
        return false;
    }

    private void Reload()
    {
        if (_store is null || !_key.IsValid)
            return;
        MapExperience loaded = _store.Load(_key, _halfLife);
        _ledger.Seed(loaded);
        _loaded = loaded;
        Interlocked.Increment(ref _loads);
        FellBack();
        Say();
    }

    private void FlushNow(double halfLife)
    {
        _halfLife = halfLife;
        ExperienceDelta delta = _accumulator.Take();
        if (_store is null || !_key.IsValid)
            return;
        if (_kept is { } kept)
        {
            kept.Add(delta);
            delta = kept;
            _kept = null;
        }

        if (delta.IsEmpty)
            return;
        if (_store.Waiting is not null)
        {
            // Not attached yet: kept for the flush after it attaches, and no merge failure.
            _kept = delta;
            Say();
            return;
        }

        if (_store.Merge(_key, delta, halfLife))
        {
            Wrote(delta);
        }
        else
        {
            Interlocked.Increment(ref _mergeFailures);
            // A file gone bad: on in memory, with what could not be written. A file held locked: again with the next flush.
            if (FellBack() && _store.Merge(_key, delta, halfLife))
                Wrote(delta);
            else
                _kept = delta;
        }

        Say();
    }

    private void Wrote(ExperienceDelta delta)
    {
        Interlocked.Increment(ref _flushes);
        Interlocked.Add(ref _rowsMerged, delta.Rows);
        _flushStamp = Stopwatch.GetTimestamp();
    }

    /// <summary>Whether the store has just refused itself, and the worker went on in memory.</summary>
    private bool FellBack()
    {
        if (_store?.Refusal is not { } why)
            return false;
        _store.Dispose();
        _store = new MemoryExperienceStore();
        _refusal = why;
        return true;
    }

    /// <summary>Whether the store that waited on a busy file has attached (or been refused) since the last job: its snapshot is read again from the file.</summary>
    private void NoticeAttach()
    {
        if (!_waiting || _store is null || _store.Waiting is not null)
            return;
        _waiting = false;
        // A retry that met a real refusal (a newer build wrote the file meanwhile) falls back to memory, as an open's does.
        if (!FellBack())
            Interlocked.Increment(ref _attaches);
        Reload();
    }

    private void Say()
        => _status = new ExperienceStatus(_key, _path, _store is MemoryExperienceStore, _refusal, _store?.Waiting, _store?.Counts(_key) ?? default, _flushStamp);

    private void OnWorker()
    {
        if (Environment.CurrentManagedThreadId == _owner)
            Interlocked.Increment(ref _onOwner);
    }

    private sealed class Job(ExperienceWorker owner, Action work) : IAiWork
    {
        public void Run()
        {
            owner.OnWorker();
            owner.NoticeAttach();
            work();
        }
    }
}
