//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Server;

/// <summary>
/// Stock's penetration events (game/server/physics.cpp: CCollisionEvent's
/// ShouldSolvePenetration, UpdatePenetrateEvents and m_penetrateEvents): the
/// live ledger, kept by managed's IPhysicsCollisionSolver
/// (<see cref="PhysicsSolver"/>, plan_physics_bones_native_zero 2.1), which
/// decides the give-up mid-step.
///
/// <para>
/// One event per pair of entities, keyed in either order. A pair stuck for
/// the give-up time is told not to solve once and put to sleep on the next
/// update, unless exempt (a player or shadow pair); debris stuck in a
/// non-static object for over half a second goes to the entity solver; a
/// pair quiet for the quiet time is dropped, a slept one re-enabled first.
/// </para>
/// </summary>
public sealed class PhysicsPenetrationLedger
{
    public enum State
    {
        Enabled = 0,
        TryDisable,
        Disabled,
        TryEntitySolver,
    }

    /// <summary>ShouldSolvePenetration's answer, and whether this was the pair's first report.</summary>
    public readonly record struct Ask(bool Solve, bool Added);

    /// <summary>A pair, lower key first.</summary>
    public readonly record struct Pair(uint Key0, uint Key1);

    /// <summary>
    /// What one update decided: pairs to put to sleep, pairs to hand to the
    /// entity solver, and pairs dropped (clear the penetrating flag on both).
    /// One instance, refilled by each <see cref="Advance"/>: the frame that
    /// asks allocates nothing.
    /// </summary>
    public sealed record Update(List<Pair> Sleep, List<Pair> Solve, List<Pair> Removed);

    private readonly Update _update = new([], [], []);

    private struct Event
    {
        public uint Key0, Key1;
        public float Start, Stamp;
        public State State;
    }

    private readonly List<Event> _events = new();
    private readonly float _giveUp;
    private readonly float _quiet;

    public PhysicsPenetrationLedger(float giveUpSeconds = 3f, float quietSeconds = 1f)
    {
        _giveUp = giveUpSeconds;
        _quiet = quietSeconds;
    }

    public int Count => _events.Count;

    public void Clear() => _events.Clear();

    /// <summary>
    /// ShouldSolvePenetration's bookkeeping for one report of a pair: the
    /// first report adds it; past the give-up time the clock restarts and, unless
    /// exempt, the pair moves to TryDisable and is told not to solve this once;
    /// debris stuck past half a second moves to TryEntitySolver.
    /// </summary>
    public Ask Report(uint key0, uint key1, float now, bool exempt, bool debrisStuck = false)
    {
        if (key0 > key1)
            (key0, key1) = (key1, key0);

        bool added = false;
        int i = IndexOf(key0, key1);
        if (i < 0)
        {
            _events.Add(new Event { Key0 = key0, Key1 = key1, Start = now, Stamp = now, State = State.Enabled });
            i = _events.Count - 1;
            added = true;
        }

        Event e = _events[i];
        e.Stamp = now;
        bool solve = true;

        if (debrisStuck && now - e.Start > 0.5f)
            e.State = State.TryEntitySolver;

        if (now - e.Start > _giveUp)
        {
            e.Start = now;
            if (!exempt)
            {
                e.State = State.TryDisable;
                solve = false;
            }
        }

        _events[i] = e;
        return new Ask(solve, added);
    }

    /// <summary>
    /// UpdatePenetrateEvents, once per frame: TryDisable pairs are slept (and
    /// kept), TryEntitySolver pairs handed to the solver (and dropped), quiet
    /// pairs dropped (a Disabled one re-enabled first). A pair whose entity is
    /// gone is dropped.
    /// </summary>
    public Update Advance(float now, Func<uint, uint, bool> bothAlive)
    {
        var update = _update;
        update.Sleep.Clear();
        update.Solve.Clear();
        update.Removed.Clear();

        for (int i = _events.Count; i-- > 0;)
        {
            Event e = _events[i];
            var pair = new Pair(e.Key0, e.Key1);

            if (e.State == State.TryDisable)
            {
                if (bothAlive(e.Key0, e.Key1))
                {
                    update.Sleep.Add(pair);
                    e.State = State.Disabled;
                    _events[i] = e;
                    continue;
                }
            }
            else if (e.State == State.TryEntitySolver)
            {
                if (bothAlive(e.Key0, e.Key1))
                    update.Solve.Add(pair);
            }
            else if (now - e.Stamp > _quiet)
            {
                if (e.State == State.Disabled && bothAlive(e.Key0, e.Key1))
                {
                    e.State = State.Enabled;
                    _events[i] = e;
                    continue;
                }
            }
            else
            {
                continue;   // recent: keep
            }

            update.Removed.Add(pair);
            _events[i] = _events[^1];
            _events.RemoveAt(_events.Count - 1);
        }

        return update;
    }

    /// <summary>
    /// Every key paired with this one, last event first: stock's backward walk
    /// (physics.cpp:821-834).
    /// </summary>
    public IEnumerable<uint> Partners(uint key)
    {
        for (int i = _events.Count; i-- > 0;)
        {
            Event e = _events[i];
            if (e.Key0 == key)
                yield return e.Key1;
            else if (e.Key1 == key)
                yield return e.Key0;
        }
    }

    private int IndexOf(uint key0, uint key1)
    {
        for (int i = 0; i < _events.Count; i++)
        {
            if (_events[i].Key0 == key0 && _events[i].Key1 == key1)
                return i;
        }

        return -1;
    }
}
