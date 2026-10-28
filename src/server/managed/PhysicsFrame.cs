//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server.Interop;

namespace SourceSharp.Server;

/// <summary>The engine's clock as the frame reads it.</summary>
public interface IPhysicsFrameClock
{
    /// <summary><c>gpGlobals-&gt;frametime</c>: 0 when the game is paused.</summary>
    float FrameTime { get; }

    /// <summary><c>gpGlobals-&gt;interval_per_tick</c>.</summary>
    float TickInterval { get; }

    /// <summary>Whether the server simulates two ticks per frame.</summary>
    bool AlternateTicks { get; }

    /// <summary>The wall clock, in seconds (stock's engine-&gt;Time(), for phys_speeds).</summary>
    double Seconds { get; }
}

/// <summary>The engine's clock over its frame area and globals; the wall clock is the process's.</summary>
public sealed class EnginePhysicsFrameClock(IEngineFrame frame, IEngineEnvironment environment) : IPhysicsFrameClock
{
    public float FrameTime => frame.GetFrameTime();

    public float TickInterval => environment.Globals.IntervalPerTick;

    public bool AlternateTicks => frame.IsSimulatingOnAlternateTicks();

    public double Seconds => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
}

/// <summary>
/// The server's physics frame, managed (plan_physics_bones_native_zero 2.3;
/// native's CPhysicsHook::FrameUpdatePostEntityThink and PhysFrame at
/// bc21e4d46, stock's physics.cpp). CPhysicsHook keeps its row in the server
/// systems and forwards here.
///
/// <para>
/// What it decides: whether to simulate (an environment, and the level not
/// paused between its pre- and post-entity init), how long a step is
/// (interval_per_tick, or 0 while paused; twice on alternate ticks; an
/// interrupt thrown away, a hitch held to 100 ms, phys_timescale between the
/// two clamps, the clock reset when the timescale changes), and what the step
/// tells the game: each active object's owner that it moved (UPDATE, for the
/// entity's own object only), each player's awake hull's stress (STRESS) and
/// its VPhysicsShadowUpdate (SHADOW_UPDATE). The events go to the router's
/// queue, delivered at the managed frame's flush in the same server frame, as
/// stock's VPhysicsUpdate runs inside PhysFrame.
/// </para>
/// </summary>
public sealed class PhysicsFrame : IGameLifecycle
{
    private readonly IVPhysicsFrameCalls _v;
    private readonly IPhysicsFrameNative _native;
    private readonly IPhysicsFrameClock _clock;
    private readonly PlayerShadowBodies? _bodies;
    private readonly PlayerShadowStress? _stress;
    private readonly Func<IEnumerable<EntityHandle>> _players;
    private readonly Action<PhysicsEvent> _post;
    private readonly Func<float> _timescale;
    private readonly Func<bool> _speeds;
    private readonly Action<string> _msg;
    private readonly Action _ensureCallbacks;

    private bool _paused;
    private float _lastTimescale = 1f;
    private float _averageSimTime;
    private int _lastObjectCount;
    private nint[] _active = new nint[256];
    private uint[] _owners = new uint[256];
    private float[] _velocities = new float[256 * 3];
    private readonly List<EntityHandle> _playerOrder = new();
    private readonly List<EntityHandle> _awake = new();

    /// <param name="players">The connected players; walked in entry-index order, as stock walks 1..maxClients.</param>
    /// <param name="post">The router's queue.</param>
    /// <param name="timescale"><c>phys_timescale</c>.</param>
    /// <param name="speeds"><c>phys_speeds</c>.</param>
    /// <param name="msg">The console.</param>
    /// <param name="ensureCallbacks">PhysicsCallbacks.EnsureInstalled: managed's tables on the environment before it steps.</param>
    public PhysicsFrame(IVPhysicsFrameCalls v, IPhysicsFrameNative native, IPhysicsFrameClock clock,
                        PlayerShadowBodies? bodies, PlayerShadowStress? stress,
                        Func<IEnumerable<EntityHandle>> players, Action<PhysicsEvent> post,
                        Func<float> timescale, Func<bool> speeds, Action<string> msg, Action ensureCallbacks)
    {
        _v = v;
        _native = native;
        _clock = clock;
        _bodies = bodies;
        _stress = stress;
        _players = players;
        _post = post;
        _timescale = timescale;
        _speeds = speeds;
        _msg = msg;
        _ensureCallbacks = ensureCallbacks;
    }

    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.PhysicsFrame;

    /// <summary>Frames that stepped the solver, since the module started.</summary>
    public long Steps { get; private set; }

    /// <summary>Called after every step with its environment (cs_sv_physics_watch's reading).</summary>
    public Action<nint>? AfterStep { get; set; }

    /// <summary>
    /// The whole frame's wall time, one call a frame that stepped, its units
    /// the steps (two on alternate ticks): the solve, the active-object walk,
    /// the owners' update crossing and the player walk (cs_sv_physics_cost).
    /// </summary>
    public CostMeter FrameCost { get; } = new();

    /// <summary>The solve alone (IPhysicsEnvironment::Simulate), one call a step.</summary>
    public CostMeter SimulateCost { get; } = new();

    /// <summary>
    /// The frame less its solves, one call a frame that stepped: what managed
    /// adds around vphysics (the decisions, the walks and their crossings).
    /// </summary>
    public CostMeter ManagedCost { get; } = new();

    // The solves' ticks inside the current frame, for ManagedCost.
    private long _frameSolveTicks;

    /// <summary>Whether the level is between its pre- and post-entity init (CPhysicsHook's m_bPaused).</summary>
    public bool Paused => _paused;

    void IGameLifecycle.OnLevelInitPreEntity(string mapName)
    {
        _paused = true;
        _averageSimTime = 0f;
    }

    void IGameLifecycle.OnLevelInitPostEntity(string mapName) => _paused = false;

    /// <summary>
    /// CPhysicsHook::FrameUpdatePostEntityThink: one step a frame, two on
    /// alternate ticks. Not frametime: that can be 30 or 15 ms by the alternate
    /// tick; and 0 while the game is paused (Tracker 24846: no vphysics then).
    /// </summary>
    public void Frame()
    {
        long start = CostMeter.Start();
        long steps = Steps;
        _frameSolveTicks = 0;
        float interval = _clock.FrameTime > 0f ? _clock.TickInterval : 0f;

        if (_clock.AlternateTicks)
            Step(interval);

        Step(interval);

        if (Steps != steps)
        {
            long spent = CostMeter.Start() - start;
            int stepped = (int)(Steps - steps);
            FrameCost.Record(spent, stepped);
            ManagedCost.Record(spent - _frameSolveTicks, stepped);
        }
    }

    /// <summary>
    /// Stock's step's clamp: an interrupt or a clock change (a delta past a
    /// second, or negative) is thrown away, a hitch is held to 100 ms, and the
    /// timescale is applied between the two clamps so it cannot buy more solve
    /// time than the ceiling allows.
    /// </summary>
    public static float ClampStep(float deltaTime, float timescale)
    {
        if (deltaTime > 1.0f || deltaTime < 0.0f)
            return 0.0f;
        if (deltaTime > 0.1f)
            deltaTime = 0.1f;

        deltaTime *= timescale;
        if (deltaTime > 0.100f)
            deltaTime = 0.100f;

        return deltaTime;
    }

    private void Step(float deltaTime)
    {
        nint env = _native.Environment();
        if (env == 0 || _paused)
            return;

        // phys_timescale: a change resets the simulation clock (stock's change
        // callback did it at once; this, before the next simulate either way).
        float scale = _timescale();
        if (scale != _lastTimescale)
        {
            _lastTimescale = scale;
            _v.ResetSimulationClock(env);
        }

        float incoming = deltaTime;
        deltaTime = ClampStep(deltaTime, scale);
        if (incoming > 1.0f || incoming < 0.0f)
            _msg("Reset physics clock\n");

        bool profile = _speeds();
        double started = profile ? _clock.Seconds : 0d;

        // Managed's callback tables, on this environment, before it steps:
        // a hot reload or a new level's environment gets them here at the latest.
        _ensureCallbacks();

#if DEBUG
        _v.DebugCheckContacts(env);
#endif

        long solve = CostMeter.Start();
        _v.Simulate(env, deltaTime);
        long solved = CostMeter.Start() - solve;
        SimulateCost.Record(solved, 1);
        _frameSolveTicks += solved;
        Steps++;

        // The per-tick transform update, for every owner the solver moved (every
        // active object, as stock's PhysFrame: one frozen this step is listed
        // asleep once, and that is where its final pose appears); and to the
        // entity, for its own object, stock's VPhysicsUpdate override (a
        // player's movement shadow is on this list too and is not its update).
        int active = _v.ActiveObjects(env, ref _active);
        if (active > 0)
        {
            if (_owners.Length < active)
                _owners = new uint[_active.Length];
            if (_velocities.Length < active * 3)
                _velocities = new float[_active.Length * 3];

            // WHAT A RIDER'S GROUND MOVES AT: an entity the solver moves moves
            // at its object's velocity (stock's CBaseEntity::GetVelocity reads
            // the object for a simulated entity). Held on the entity, which the
            // movement's per-trace ground read answers from without asking.
            for (int i = 0; i < active; i++)
            {
                Vector v = _v.Velocity(_active[i]);
                _velocities[i * 3 + 0] = v.X;
                _velocities[i * 3 + 1] = v.Y;
                _velocities[i * 3 + 2] = v.Z;
            }

            int owned = _native.UpdateOwners(_active.AsSpan(0, active), _velocities.AsSpan(0, active * 3), _owners);
            for (int i = 0; i < owned; i++)
                _post(new PhysicsEvent { KindRaw = (uint)PhysicsEventKind.Update, Entity0 = _owners[i], Entity1 = PhysicsEvent.NoEntity });
        }

        // The players: CBasePlayer::VPhysicsUpdate's stress for each awake
        // hull, then each one's VPhysicsShadowUpdate (stock's shadow-entity
        // walk), in player order.
        PlayerWalk(env);

        AfterStep?.Invoke(env);

        if (profile)
        {
            float simRealTime = (float)(_clock.Seconds - started);
            if (simRealTime < 0)
                simRealTime = 0;
            _averageSimTime *= 0.8f;
            _averageSimTime += simRealTime * 0.2f;
            if (_lastObjectCount != 0 || active != 0)
            {
                _msg(string.Create(CultureInfo.InvariantCulture,
                    $"Physics: {active,3} objects, {simRealTime * 1000,4:F1}ms / AVG: {_averageSimTime * 1000,4:F1}ms\n"));
            }

            _lastObjectCount = active;
        }
    }

    private void PlayerWalk(nint env)
    {
        if (_bodies is null)
            return;

        _playerOrder.Clear();
        _playerOrder.AddRange(_players());
        _playerOrder.Sort((a, b) => a.EntryIndex.CompareTo(b.EntryIndex));

        _awake.Clear();
        foreach (EntityHandle player in _playerOrder)
        {
            nint shadow = _bodies.AwakeShadow(player, out PlayerShadowFacts facts);
            if (shadow == 0)
                continue;

            _awake.Add(player);
            if (_stress is null)
                continue;

            (float received, float mass, uint flags) = _stress.Measure(env, shadow, in facts);
            _post(new PhysicsEvent
            {
                KindRaw = (uint)PhysicsEventKind.Stress,
                Entity0 = player.Value,
                Entity1 = PhysicsEvent.NoEntity,
                Speed = received,
                Energy = mass,
                Flags = flags,
            });
        }

        foreach (EntityHandle player in _awake)
            _post(new PhysicsEvent { KindRaw = (uint)PhysicsEventKind.ShadowUpdate, Entity0 = player.Value, Entity1 = PhysicsEvent.NoEntity });
    }
}
