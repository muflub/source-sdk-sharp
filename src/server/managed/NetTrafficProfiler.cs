using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// Turns the payload traffic view on. Off by default, because it costs a blob
/// copy and a compare per entity per frame and nothing else in the game needs
/// the answer.
///
/// <para>
/// The name matches the one plan_nettable_generic.md Part 13 gives it, so that
/// the plan and the console agree about what to type.
/// </para>
/// </summary>
[ConVar("cs_sv_nettable_profile", "0",
        Help = "Sample managed entity payloads each frame and attribute the delta to C# fields")]
public sealed class NetTableProfileConVar : BoolConVar
{
}

/// <summary>
/// The per-frame half of the traffic view — the part that has to touch the game.
///
/// <para>
/// ENTIRELY MANAGED, and that is a correction to Part 11. The plan puts the
/// snapshot and diff in native behind <c>cs_sv_nettable_profile</c>, which was
/// the right shape when the blob was only reachable from C++; it is not any
/// more. C# already has every entity's blob through
/// <see cref="IEngineNetworking.GetEntityBlob"/> and every class's field list through
/// <see cref="NetSchema"/>, so the whole view is managed and the C++ diff of
/// this step is empty. That is the direction the project's own rule points, and
/// the plan simply predates the accessor.
/// </para>
///
/// <para>
/// SAMPLED AT FRAME END, after the thinks that write and before the snapshot
/// that sends, which is the same window the dirty-word drain uses. A sample
/// taken mid-frame would attribute a write to the frame after it.
/// </para>
///
/// <para>
/// A SERVICE, registered in the server module's container and cleared by its
/// own level-shutdown step. It was a static class holding the real engine, the
/// sampler and the "was on" latch for the process, on the reasoning that a
/// profiler has no owner and no lifetime. It has both: the module that samples
/// through it, and the level whose entity indices its snapshots are keyed by.
/// </para>
/// </summary>
public sealed class NetTrafficProfiler : INetTrafficProfiler, IGameLifecycle
{
    private readonly IEngineEntities _entities;
    private readonly IEngineNetworking _net;

    /// <summary>The engine areas a sample reads: the entity walk and each entity's blob.</summary>
    public NetTrafficProfiler(IEngineEntities entities, IEngineNetworking net)
    {
        _entities = entities;
        _net = net;
    }

    /// <summary>
    /// Payload snapshots are keyed by entity INDEX, and every index is reused
    /// by the next map. Keeping them would diff a new entity against an
    /// unrelated old one and attribute the difference to whichever class
    /// happened to land on that index. No ordering against any other
    /// participant: nothing reads the sampler during a level change.
    /// </summary>
    void IGameLifecycle.OnLevelShutdown() => Clear();

    /// <summary>The engine's own matcher for "every entity", on both walks.</summary>
    private const string Everything = "*";

    private readonly NetTrafficSampler _sampler = new();

    private bool _wasOn;

    /// <summary>Whether sampling ran on the last frame, for the route to report.</summary>
    public bool IsSampling { get; private set; }

    /// <summary>What has been measured, for the route and the CLI.</summary>
    public IReadOnlyList<NetTrafficClass> Report(int topFields = 8)
        => _sampler.Report(topFields);

    /// <summary>Seconds the current measurement has been running.</summary>
    public double Elapsed => _sampler.Elapsed;

    /// <summary>How many entities are being tracked.</summary>
    public int Tracked => _sampler.Tracked;

    /// <summary>Clears the totals, keeping the images. See NetTrafficSampler.Reset.</summary>
    public void Reset() => _sampler.Reset();

    /// <summary>Drops everything. Called at level change, where every index is reused.</summary>
    public void Clear()
    {
        _sampler.Clear();
        IsSampling = false;
        _wasOn = false;
    }

    /// <summary>
    /// One frame's sample.
    ///
    /// <para>
    /// Returns immediately when the convar is off, which is the common case and
    /// has to cost nothing measurable — this is called from
    /// <c>ServerModule.Frame</c>, which is documented as allocation-free.
    /// </para>
    ///
    /// <para>
    /// Turning it on RESETS rather than resuming: the images are stale by
    /// however long it was off, so the first frame after would charge every
    /// entity everything that changed while nobody was counting, and that
    /// arrives as one enormous spike attributed to whatever happened to move.
    /// </para>
    /// </summary>
    /// <param name="enabled">The convar's current value, read by the caller.</param>
    /// <param name="layoutOf">
    /// A class's declared fields, or null for one this side has no schema for.
    /// Passed in rather than looked up here so the schema stays the caller's
    /// business and this file has one job.
    /// </param>
    public void Frame(bool enabled, Func<string, IReadOnlyList<NetSchemaField>?> layoutOf)
    {
        if (!enabled)
        {
            IsSampling = false;
            _wasOn = false;
            return;
        }

        if (!_wasOn)
        {
            // Just switched on. Drop the stale images so the first frame
            // establishes a baseline and charges nothing.
            _sampler.Clear();
            _wasOn = true;
        }

        IsSampling = true;

        foreach (var entity in _entities.FindAllByClassname(Everything))
        {
            // The module's networking area: in the game, the engine itself. A
            // test may hand it a fake to drive the sampling logic, which is a
            // test of the logic and not a measurement.
            ReadOnlySpan<byte> blob = _net.GetEntityBlob(entity);

            if (blob.IsEmpty)
                continue;

            string className = _entities.GetClassName(entity);

            if (string.IsNullOrEmpty(className))
                continue;

            _sampler.Observe(className, _entities.GetIndex(entity), blob, layoutOf(className));
        }
    }
}

/// <summary>
/// The server's payload traffic view: sampled once a frame while
/// <c>cs_sv_nettable_profile</c> is on, reported to the dev API.
/// </summary>
public interface INetTrafficProfiler
{
    /// <summary>Whether sampling ran on the last frame.</summary>
    bool IsSampling { get; }

    /// <summary>Seconds of sampling behind the report.</summary>
    double Elapsed { get; }

    /// <summary>How many entities have a snapshot.</summary>
    int Tracked { get; }

    /// <summary>The traffic attributed so far, by class, with each class's busiest fields.</summary>
    IReadOnlyList<NetTrafficClass> Report(int topFields = 8);

    /// <summary>Zeroes the counts and keeps the snapshots.</summary>
    void Reset();

    /// <summary>Drops everything, snapshots included.</summary>
    void Clear();

    /// <summary>One frame's sample, when <paramref name="enabled"/>.</summary>
    void Frame(bool enabled, Func<string, IReadOnlyList<NetSchemaField>?> layoutOf);
}
