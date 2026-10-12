using System;
using System.Collections.Generic;
using System.Linq;

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// Finds every <see cref="IGameLifecycle"/> the container knows about and calls
/// them in sequence.
///
/// <para>
/// <b>Resolved once, not per event.</b> The set of registered services does not
/// change after startup, and re-resolving on every map change would make the
/// order depend on what had happened to be built by then — a lazily registered
/// participant would join the sequence the first time something else asked for
/// it, which is a different order on the second map than the first.
/// </para>
///
/// <para>
/// <b>One participant's failure does not eat the rest.</b> A throwing
/// <c>OnLevelInit</c> used to be a throwing <c>LevelInit</c>, and the services
/// after it in the hand-written list simply did not get reset — with the map
/// loading anyway and the damage showing up later as stale handles. Each call
/// is isolated and reported by name.
/// </para>
/// </summary>
public sealed class LifecycleDriver
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IReadOnlyList<IGameLifecycle> _participants;
    private readonly ServiceContainer _services;

    // How OnRestore re-checks handles: this side's engine, from the container
    // the participants came from (plan_abi_hotreload D8). Built on first use,
    // since a driver whose participants restore nothing never needs it.
    private SavedHandles? _handles;

    private SavedHandles Handles
        => _handles ??= new SavedHandles(_services.Require<IEngineHost>(), _services.Require<IEngineEntities>());

    public LifecycleDriver(ILog log, ServiceContainer services)
    {

        _log = log;
        _services = services;
        var found = services.ResolveAll<IGameLifecycle>();

        // TIES ARE IMPOSSIBLE, NOT RESOLVED. This used to sort with "equal
        // orders keep registration order", which is a stable sort honestly
        // implemented against the one input nobody decided -- see
        // StageUniqueness for the argument. Checked BEFORE the sort, so the
        // message can name the two participants rather than two positions.
        StageUniqueness.Require(found, p => p.LifecycleOrder, "LifecycleOrder", "LifecycleStage");

        _participants = found.OrderBy(p => p.LifecycleOrder).ToArray();
    }

    /// <summary>What was found, in the order they will be called.</summary>
    public IReadOnlyList<IGameLifecycle> Participants => _participants;

    /// <summary>
    /// Stock's <c>PostInit</c>. No participant in this build — see
    /// <see cref="IGameLifecycle.OnPostInit"/> for why the empty pass is here.
    /// </summary>
    /// <summary>The level is up and clients can connect (<see cref="IGameLifecycle.OnLevelReady"/>).</summary>
    public void LevelReady(string mapName) => Dispatch("OnLevelReady", p => p.OnLevelReady(mapName));

    public void PostInit() => Dispatch("OnPostInit", p => p.OnPostInit());

    /// <summary>
    /// The map is loading and its entities do NOT exist yet. Stock's
    /// <c>LevelInitPreEntity</c>.
    /// </summary>
    public void LevelInitPreEntity(string mapName) =>
        Dispatch("OnLevelInitPreEntity", p => p.OnLevelInitPreEntity(mapName));

    /// <summary>
    /// The map has loaded and its entities exist. Stock's
    /// <c>LevelInitPostEntity</c>, and the moment every participant written
    /// before the split already runs at.
    /// </summary>
    public void LevelInitPostEntity(string mapName) =>
        Dispatch("OnLevelInitPostEntity", p => p.OnLevelInitPostEntity(mapName));

    /// <summary>
    /// The PostEntity half, under the name the module already calls.
    ///
    /// <para>
    /// It dispatches <c>OnLevelInitPostEntity</c>, whose interface default
    /// forwards to <c>OnLevelInit</c> — so a participant written before the
    /// split is reached through one extra virtual call, at the same moment as
    /// before and in the same position. That equivalence is the entire safety
    /// claim of the split, so it is a test rather than this comment.
    /// </para>
    /// </summary>
    public void LevelInit(string mapName) => LevelInitPostEntity(mapName);

    /// <summary>The map is going away and its entities are still alive.</summary>
    public void LevelShutdownPreEntity() =>
        Dispatch("OnLevelShutdownPreEntity", p => p.OnLevelShutdownPreEntity());

    /// <summary>The map has gone and its entities with it.</summary>
    public void LevelShutdownPostEntity() =>
        Dispatch("OnLevelShutdownPostEntity", p => p.OnLevelShutdownPostEntity());

    /// <summary>The PostEntity half, under the name the module already calls.</summary>
    public void LevelShutdown() => LevelShutdownPostEntity();

    public void Shutdown() => Dispatch("OnShutdown", p => p.OnShutdown());

    //-------------------------------------------------------------------------
    // THE SAVE STAGES -- the escape hatch for state that is not an entity's.
    //
    // GameLifecycle.cs used to record that OnSave and OnRestore were
    // deliberately absent because "their only possible caller is code that will
    // not be written". That caller is these two methods; the reversal and its
    // reason are written down at the interface.
    //
    // FOUND THE SAME WAY EVERY OTHER STAGE IS, which is the whole argument for
    // putting them here rather than inventing a registration of their own: the
    // participants are already resolved, already ordered, and a thrower is
    // already isolated from the ones after it. A separate discovery mechanism
    // for the same set of objects is a second list, and a second list drifts.
    //
    // ONE RECORD PER PARTICIPANT, KEYED BY TYPE NAME. Not by position: the
    // participants are FOUND rather than listed, so a positional slot would be
    // silently wrong the first time somebody registered one in a different
    // order -- and "silently wrong" here means one service reading another
    // service's state.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Every participant's state, in a form that outlives this assembly.
    ///
    /// <para>
    /// A participant that does not implement <see cref="ISaveCustom"/> is
    /// skipped, and one that wrote nothing is dropped — so the count this
    /// returns is "how many had something to carry", which is the number the
    /// reload report shows and the number a gate can assert on.
    /// </para>
    /// </summary>
    public Dictionary<string, ReloadRecord> SaveState()
    {
        var records = new Dictionary<string, ReloadRecord>(StringComparer.Ordinal);

        foreach (var participant in _participants)
        {
            if (participant is not ISaveCustom custom)
                continue;

            string owner = participant.GetType().FullName ?? participant.GetType().Name;
            var record = new ReloadRecord(owner);

            try
            {
                custom.OnSave(new SaveRecordAccess(record, owner, _log));
            }
            catch (System.Exception e)
            {
                // Named, and then carried on -- Dispatch's rule exactly. A
                // service whose OnSave throws loses ITS state; the ones after
                // it have not done anything wrong.
                _log.Warning($"[SourceSharp/Server] {participant.GetType().Name}.OnSave threw: " +
                            $"{e.Message}; its state is not carried across\n");
                continue;
            }

            if (!record.IsEmpty)
                records[owner] = record;
        }

        return records;
    }

    /// <summary>
    /// Puts every participant's state back. Answers how many were reached.
    ///
    /// <para>
    /// A participant with no record is still called, with an EMPTY one. That is
    /// deliberate and it is the difference between "restore what was carried"
    /// and "tell every participant a reload happened": a service that has to
    /// know it is coming up after a reload rather than after a fresh start
    /// learns it here, and one that only wants its values reads them with
    /// fallbacks and cannot tell the difference.
    /// </para>
    /// </summary>
    public int RestoreState(IReadOnlyDictionary<string, ReloadRecord>? records)
    {
        int reached = 0;

        foreach (var participant in _participants)
        {
            if (participant is not ISaveCustom custom)
                continue;

            string owner = participant.GetType().FullName ?? participant.GetType().Name;

            ReloadRecord record =
                records is not null && records.TryGetValue(owner, out var found)
                    ? found
                    : new ReloadRecord(owner);

            try
            {
                custom.OnRestore(new SaveRecordAccess(record, owner, _log, Handles));
                reached++;
            }
            catch (System.Exception e)
            {
                _log.Warning($"[SourceSharp/Server] {participant.GetType().Name}.OnRestore " +
                            $"threw: {e.Message}; its state is not put back\n");
            }
        }

        return reached;
    }

    /// <summary>
    /// How many participants have state to carry at all. Zero is a real answer
    /// and is what the reload report shows before anything implements the
    /// interface — see <see cref="ReloadStore"/> on why a readout that cannot
    /// read zero is not a readout.
    /// </summary>
    public int SaveParticipantCount
    {
        get
        {
            int n = 0;
            foreach (var p in _participants)
            {
                if (p is ISaveCustom)
                    n++;
            }
            return n;
        }
    }

    private void Dispatch(string what, System.Action<IGameLifecycle> call)
    {
        foreach (var p in _participants)
        {
            try
            {
                call(p);
            }
            catch (System.Exception e)
            {
                // Named, and then carried on. The participant that threw is the
                // one with the bug; the ones after it in the sequence have not
                // done anything wrong and still need their event.
                _log.Warning($"[SourceSharp/Server] {p.GetType().Name}.{what} threw: {e.Message}\n");
            }
        }
    }
}
