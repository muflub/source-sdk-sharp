using SourceSharp.Abi;
using SourceSharp.Client.Interface;

namespace SourceSharp.Client;

/// <summary><see cref="IDebugClientLog"/> over the client composition's own console (P3).</summary>
public sealed class ClientDebugLog(ILog console) : IDebugClientLog
{
    private readonly ILog _console = console ?? throw new ArgumentNullException(nameof(console));

    public void Msg(string message) => _console.Msg(message);

    public void Warning(string message) => _console.Warning(message);
}

/// <summary>
/// <see cref="IDebugClientBuild"/> over the client's build stamps and the
/// crossing counters (P3). Both are process-wide statics in the Abi today
/// (Q4); this instance is the client composition's reading of them.
/// </summary>
public sealed class ClientDebugBuild(BuildInfo build, Abi.Crossings crossings, UserMessageRegistry? messages = null,
                                     GameEventRegistry? events = null) : IDebugClientBuild
{
    public IReadOnlyList<DebugCount> GameEventsReceived()
        => events is null ? [] : events.ReceivedCounts().Select(c => new DebugCount(c.Name, c.Count)).ToArray();

    public IReadOnlyList<DebugCount> UserMessagesReceived()
        => messages is null ? [] : messages.ReceivedCounts().Select(c => new DebugCount(c.Name, c.Count)).ToArray();

    public DebugBuildInfo Build()
        => new(Half(build.Native, build.NativeFreshness),
               Half(build.Managed, build.ManagedFreshness));

    public DebugCrossings Crossings()
        => new(crossings.Total, crossings.Resets, Abi.Crossings.OpCount,
               crossings.ServerFrames, crossings.ClientFrames,
               Counts(crossings.Active()), Counts(crossings.ActiveEntityOps()));

    public string ManagedStamp() => build.Managed.Stamp.Describe();

    /// <summary>One half, as the dev API has always rendered it.</summary>
    internal static DebugBuildHalf Half(BuildHalf half, BuildFreshness freshness)
        => new(half.Stamp.IsKnown, half.Stamp.Hash, half.Stamp.BuiltAtUnixMs, half.Stamp.Parts,
               half.Source.IsKnown ? half.Source.BranchName : "", half.Source.Commit, half.Source.Dirty,
               half.Source.Describe(), freshness.ToString().ToLowerInvariant(), half.Path);

    private static DebugCount[] Counts(List<(string Name, long Count)> counts)
        => counts.Select(c => new DebugCount(c.Name, c.Count)).ToArray();
}

/// <summary>The hooks one client composition serves its dev API (P3). Built by that composition, owned by it.</summary>
public sealed class DebugClientHooks(IDebugClientLog log, IDebugClientBuild build, IDebugClientPrediction prediction,
                                     IDebugClientFrame frame, IDebugClientEntities entities,
                                     IDebugClientNetTables netTables, IDebugClientReload reload,
                                     IDebugClientNetGraph netGraph, IDebugClientAbsences absences,
                                     IDebugClientUi ui, IDebugClientProxies proxies, IDebugClientViewCamera viewCamera,
                                     IDebugClientRender render,
                                     IDebugClientParticles particles,
                                     IDebugClientInput input)
    : IDebugClientHooks
{
    public IDebugClientRender Render { get; } = render ?? throw new ArgumentNullException(nameof(render));
    public IDebugClientParticles Particles { get; } = particles ?? throw new ArgumentNullException(nameof(particles));

    public IDebugClientProxies Proxies { get; } = proxies ?? throw new ArgumentNullException(nameof(proxies));
    public IDebugClientViewCamera ViewCamera { get; } = viewCamera ?? throw new ArgumentNullException(nameof(viewCamera));
    public IDebugClientInput Input { get; } = input ?? throw new ArgumentNullException(nameof(input));

    public IDebugClientNetGraph NetGraph { get; } = netGraph ?? throw new ArgumentNullException(nameof(netGraph));

    public IDebugClientAbsences Absences { get; } = absences ?? throw new ArgumentNullException(nameof(absences));

    public IDebugClientUi Ui { get; } = ui ?? throw new ArgumentNullException(nameof(ui));
    public IDebugClientReload Reload { get; } = reload ?? throw new ArgumentNullException(nameof(reload));
    public IDebugClientNetTables NetTables { get; } = netTables ?? throw new ArgumentNullException(nameof(netTables));
    public IDebugClientEntities Entities { get; } = entities ?? throw new ArgumentNullException(nameof(entities));
    public IDebugClientFrame Frame { get; } = frame ?? throw new ArgumentNullException(nameof(frame));

    public IDebugClientPrediction Prediction { get; } = prediction ?? throw new ArgumentNullException(nameof(prediction));

    public IDebugClientLog Log { get; } = log ?? throw new ArgumentNullException(nameof(log));

    public IDebugClientBuild Build { get; } = build ?? throw new ArgumentNullException(nameof(build));
}

/// <summary>
/// <see cref="IDebugClientPrediction"/> over the client's prediction ring (P3b).
/// The ring is the process-wide PredictionErrors.Ring today (Q4); the
/// composition hands it over, and a test hands over its own.
/// </summary>
public sealed class ClientDebugPrediction(PredictionErrorRing ring) : IDebugClientPrediction
{
    private readonly PredictionErrorRing _ring = ring ?? throw new ArgumentNullException(nameof(ring));

    public DebugPredictionWindow Since(long since, int max)
    {
        // The totals before the records: a record added in between is then
        // counted at worst once too few, never listed but uncounted.
        var (capacity, held, next, observed, mispredicted, toleratedOnly) =
            (_ring.Capacity, _ring.Count, _ring.NextSequence, _ring.Observed, _ring.Mispredicted, _ring.ToleratedOnly);
        var records = _ring.Since(since, max).Select(Record).ToArray();
        return new DebugPredictionWindow(capacity, held, next, observed, mispredicted, toleratedOnly, records);
    }

    public void Reset() => _ring.Reset();

    private static DebugPredictionRecord Record(PredictionErrorRecord r)
        => new(r.Sequence, r.Command, r.TypeId, r.ClassName, r.BlobBytes, r.StandingBytes, r.FirstDiffOffset,
               r.ToleratedFields, r.TimeUtc,
               r.Fields.Select(f => new DebugPredictionField(f.Name, f.Kind, f.Offset, f.Bytes, f.Tolerance, f.Tolerated,
                   f.DifferingBytes, f.Components.Select(c => new DebugPredictionComponent(c.Predicted, c.Server, c.Delta)).ToArray()))
                .ToArray());
}

/// <summary>
/// <see cref="IDebugClientFrame"/> over the module's frame counters, its frame
/// time window, the published view and the engine clock (plan_client_novalve
/// §4.4). Game thread: the dev API calls it from its pump.
/// </summary>
public sealed class ClientDebugFrame(IClientFrameStats stats, FrameTimeWindow window, ViewReadout view, IGameClock clock)
    : IDebugClientFrame
{
    public DebugClientFrame Read()
    {
        var (frames, min, avg, max) = window.Read();
        var stages = Enum.GetValues<ClientFrameStage>()
                         .Where(s => s != ClientFrameStage.Undefined)
                         .Select(s => new DebugCount(s.ToString(), stats.StageCount(s)))
                         .ToArray();

        return new DebugClientFrame(
            stats.FrameCount, stats.CommandCount, stats.CurrentStage.ToString(), stages,
            frames, min, avg, max,
            clock.Now, view.Known, new DebugClientVec3(view.Eye.X, view.Eye.Y, view.Eye.Z), view.Pitch, view.Yaw);
    }
}
