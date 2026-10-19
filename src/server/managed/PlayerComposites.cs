using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// One slice of a player, owned by the code that made it (tf2 stage 0.7 part B,
/// rulings D9/D10): the generic per-player composite that every per-player
/// feature (TF2's class and respawn state, Descent's abilities) hangs itself
/// on, instead of each inventing its own player registry.
///
/// <para>
/// A component is NOT an entity: the player is the entity, and this is one
/// mod's per-player state bolted beside it. The three methods are the whole
/// contract — <see cref="Attach"/> when the player is in the game,
/// <see cref="Tick"/> every frame in the composite's fixed order, and
/// <see cref="Detach"/> when they leave. Detach runs the whole sequence in
/// REVERSE so a component may still read its neighbours while tearing down.
/// </para>
/// </summary>
public interface IPlayerComponent
{
    /// <summary>
    /// Position within its own source's block: sources first order by
    /// registering assembly name (ordinal), and this orders inside a source,
    /// so the tick sequence is fixed and readable — see
    /// <see cref="PlayerComposites"/>.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// The player is in the game: called once per player, before the first
    /// <see cref="Tick"/>. The handle is the player's own; it is valid during
    /// the attach and while the player stays, and a component must not hold
    /// onto it past <see cref="Detach"/>.
    /// </summary>
    void Attach(EntityHandle player);

    /// <summary>
    /// Every server frame, in the fixed order, while attached. A component
    /// that throws is named once and skipped from then on: a broken slice
    /// must not stop its neighbours' frames, and must not spam the log at
    /// 60 Hz either.
    /// </summary>
    void Tick(float now);

    /// <summary>
    /// The player left, or the level ended; runs exactly once per attach, in
    /// reverse order, and after attach is attempted even if the attach threw.
    /// </summary>
    void Detach();
}

/// <summary>
/// A mod's claim on the per-player composite: asked once per player, it
/// creates that player's components. A CHAIN (<see cref="IHooks{T}"/>): every
/// source every mod registers is asked — TF2 registers one (Stage 2.3),
/// Descent later registers its own into the SAME composite — and the
/// components from all of them tick as one ordered set. A source that throws
/// is named and contributes nothing for that player: a broken mod gets no
/// components, not a broken game.
/// </summary>
public interface IPlayerComponentSource
{
    IEnumerable<IPlayerComponent> Create(EntityHandle player);
}

/// <summary>
/// The generic per-player composite (plan 0.7 part B, rulings D9/D10): the
/// one place a player's per-player components live, so two mods can bolt
/// state onto the same player without knowing about each other and the tick
/// order is still a single fixed sequence.
///
/// <para>
/// <b>The order, stated once:</b> components sort by registering assembly
/// name (ordinal), then by their source's own <see cref="IPlayerComponent.Order"/>
/// inside that block. Not the container's resolution order — that is the
/// input nobody decided, which is why the sequence is spelled out here and
/// pinned by facts that plant two sources in two assemblies.
/// </para>
///
/// <para>
/// <see cref="Get{T}(EntityHandle)"/> answers across sources by INTERFACE
/// (Descent's component reads TF2's <c>ITfClassComponent</c> without either
/// mod referencing the other): the composite is the one object that has seen
/// both, and it answers in the tick order, so "the first component that
/// answers to T" is deterministic.
/// </para>
///
/// <para>
/// <b>Lifetime:</b> components are created when the engine says the client is
/// active — the entity exists and its loadout is in — and detach on every
/// leave AND at level shutdown. The second matters: a level change is not a
/// departure (the client stays) but it is still the end of THAT player's
/// components; per-player state that crossed a reload would be the
/// stale-handle-and-old-score bug every other save-tier participant records.
/// </para>
/// </summary>
public sealed class PlayerComposites(ILog log,
                                     IHooks<IPlayerComponentSource> sources,
                                     IEnginePlayers players,
                                     IGameClock clock)
    : IGameLifecycle, IFrameLifecycle, IClientLifecycle
{
    private sealed record Slot(EntityHandle Player, List<IPlayerComponent> Components);

    // Keyed by the engine's player INDEX, not the handle: the leave event
    // names the index, and resolving a departed entity's handle to unkey the
    // room would ask the engine about a thing that has just gone away.
    private readonly Dictionary<int, Slot> _byPlayer = new();

    /// <summary>Test seam: this player's components, in the dispatch order.</summary>
    public IReadOnlyList<IPlayerComponent>? Of(int entIndex)
        => _byPlayer.TryGetValue(entIndex, out var s) ? s.Components : null;

    //---------------------------------------------------------------------
    // The build: every source, ordered, once per player.
    //---------------------------------------------------------------------

    private List<IPlayerComponent> Build(EntityHandle player)
    {
        var all = new List<IPlayerComponent>();

        foreach (var source in sources.All)
        {
            try
            {
                all.AddRange(source.Create(player));
            }
            catch (Exception e)
            {
                // A broken mod's source contributes nothing; the other mods'
                // components still arrive — the chain's rule for a throw.
                log.Warning($"[SourceSharp/Composites] source {source.GetType().FullName} " +
                            $"threw for {player}: {e}\n");
            }
        }

        // Sources first by registering assembly name, then Order inside a
        // source: the sequence is what the class summary says, not what the
        // container happened to resolve first.
        return all
            .OrderBy(c => c.GetType().Assembly.GetName().Name, StringComparer.Ordinal)
            .ThenBy(c => c.Order)
            .ToList();
    }

    private void AttachPlayer(int entIndex)
    {
        if (_byPlayer.ContainsKey(entIndex))
            return;   // already in: a doubled active event must not attach twice

        var player = players.GetHandle(entIndex);
        var components = Build(player);

        // Attempted attach means detach is OWED, whether or not the attach
        // survived: a half-attached slice never detached leaks its state into
        // the next player to wear this index.
        _byPlayer[entIndex] = new Slot(player, components);

        foreach (var c in components)
        {
            try
            {
                c.Attach(player);
            }
            catch (Exception e)
            {
                log.Warning($"[SourceSharp/Composites] component {c.GetType().FullName} " +
                            $"attach threw for {player}: {e}\n");
            }
        }
    }

    private void DetachPlayer(int entIndex)
    {
        if (!_byPlayer.Remove(entIndex, out var slot))
            return;

        foreach (var c in Enumerable.Reverse(slot.Components))
        {
            try
            {
                c.Detach();
            }
            catch (Exception e)
            {
                // One slice's teardown failure must not skip the ones before
                // it in the reverse walk — that is the next loop iteration's
                // problem, not this room's.
                log.Warning($"[SourceSharp/Composites] component {c.GetType().FullName} " +
                            $"detach threw for {slot.Player}: {e}\n");
            }
        }
    }

    //---------------------------------------------------------------------
    // The frame.
    //---------------------------------------------------------------------

    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.PlayerComponents;

    void IGameLifecycle.OnLevelShutdown()
    {
        // Not departures — the clients stay — but the end of THESE components:
        // what crosses a level change is exactly the state a component has no
        // way to re-derive (the plan's save-tier rule).
        foreach (var entIndex in _byPlayer.Keys.ToArray())
            DetachPlayer(entIndex);
    }

    int IFrameLifecycle.FrameOrder => (int)FrameStage.PlayerComponents;

    void IFrameLifecycle.OnThink(float frameTime)
    {
        float now = clock.Now;   // the components' clock is the game's, not the delta

        // No snapshot: detach is a lifecycle event and never a Tick side
        // effect, so the dictionary cannot shrink under this walk. A component
        // that wants out is SKIPPED, in place — the slot stays so the order
        // and the detach accounting do not move.
        foreach (var slot in _byPlayer.Values)
        {
            for (int i = 0; i < slot.Components.Count; i++)
            {
                var c = slot.Components[i];
                if (c is Skipped)
                    continue;

                try
                {
                    c.Tick(now);
                }
                catch (Exception e)
                {
                    // Named once, then quiet: a frame is not where one broken
                    // slice shouts sixty times a second, and a dead slice
                    // must not starve its neighbours.
                    log.Warning($"[SourceSharp/Composites] component {c.GetType().FullName} " +
                                $"tick threw: {e}\n");
                    slot.Components[i] = new Skipped(c);
                }
            }
        }
    }

    /// <summary>The skipped record: keeps the slot and the order, asks nothing, still detaches.</summary>
    private sealed class Skipped(IPlayerComponent was) : IPlayerComponent
    {
        public int Order => was.Order;
        public void Attach(EntityHandle player) { }
        public void Tick(float now) { }

        // Teardown is owed even to a skipped slice: it was attached, and the
        // detach that never ran is the leak this room's design refuses.
        public void Detach() => was.Detach();

        public override string ToString() => was.ToString() ?? "";
    }

    //---------------------------------------------------------------------
    // The lifecycle: attach on client-active, detach on leave.
    //---------------------------------------------------------------------

    int IClientLifecycle.ClientLifecycleOrder => (int)LifecycleStage.PlayerComponents;

    // The two core client participants take their positions from
    // LifecycleStage as well (Arsenal = 600); the composite sits with the
    // players it composes. Unique among client participants is enforced by
    // ClientLifecycleDriver's own StageUniqueness check.
    void IClientLifecycle.OnClientActive(int entIndex) => AttachPlayer(entIndex);

    void IClientLifecycle.OnClientDisconnected(int entIndex) => DetachPlayer(entIndex);

    /// <summary>
    /// The first of <paramref name="player"/>'s components that answers to
    /// <typeparamref name="T"/>, in the tick order — how one mod's component
    /// finds another's by interface without either referencing the other.
    /// Null is "no such player, or no such slice", and both arms are the
    /// caller's to answer (a component reading another that did not register
    /// is a mod's normal first frame, not an error).
    /// </summary>
    public T? Get<T>(EntityHandle player) where T : class, IPlayerComponent
    {
        foreach (var slot in _byPlayer.Values)
        {
            if (slot.Player != player)
                continue;

            foreach (var c in slot.Components)
                if (c is T match)
                    return match;

            return null;
        }

        return null;
    }
}
