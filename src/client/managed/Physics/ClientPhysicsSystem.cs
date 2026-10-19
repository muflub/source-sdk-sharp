//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Diagnostics;
using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Physics;

/// <summary>What stock's client physics step reads from the frame and the console.</summary>
public interface IClientPhysicsClock
{
    /// <summary><c>gpGlobals-&gt;frametime</c>.</summary>
    float FrameTime { get; }

    /// <summary><c>gpGlobals-&gt;interval_per_tick</c>.</summary>
    float IntervalPerTick { get; }

    /// <summary><c>GetCurrentGravity()</c>.</summary>
    float Gravity { get; }

    /// <summary><c>cl_phys_timescale</c>.</summary>
    float TimeScale { get; }
}

/// <summary>
/// The work stock's step does around the solver's, which the client's own
/// managed pieces hold: <c>CCollisionEvent::FrameUpdate</c>'s friction and
/// fluid halves, and <c>CPhysicsSystem</c>'s impact sound list.
/// </summary>
public interface IClientPhysicsStepTail
{
    /// <summary><c>UpdateFrictionSounds</c>: a scrape not kept alive stops.</summary>
    void UpdateFrictionSounds();

    /// <summary><c>UpdateTouchEvents</c>: the step's buffered contact touches, dispatched.</summary>
    void UpdateTouchEvents() { }

    /// <summary><c>UpdateFluidEvents</c>: a fluid entry older than its window is forgotten.</summary>
    void UpdateFluidEvents();

    /// <summary><c>physicssound::PlayImpactSounds( m_impactSounds )</c>.</summary>
    void PlayImpactSounds();

    /// <summary><c>m_impactSounds.RemoveAll()</c>.</summary>
    void RemoveAllImpactSounds();
}

/// <summary>
/// Stock's client <c>CPhysicsSystem</c> and <c>PhysicsLevelInit</c>
/// (game/client/physics.cpp), decided here: the level's environment made with
/// the game's gravity and the tick's step, managed's collision tables on it
/// before anything is made in it, the world and the static props, the
/// per-frame step, and the teardown in stock's order. Native's client system
/// row forwards the four level events, the step and the reset here
/// (IManagedPhysicsClient).
/// <para>
/// Three stock pieces have no subject on this client and are named rather
/// than dropped: the entity-collision pair hash (nothing on this client adds a
/// pair, so the solver never finds one: ClientCollisionSolver), the active
/// objects' <c>VPhysicsUpdate</c> (every client object's game data is a managed
/// handle word or nothing, so <c>PhysicsOwnerOf</c> found no SDK owner, and
/// C_StandaloneEntity's override was empty), and the touch buffer (both owners
/// of a touch are handle words, which stock's StartTouch/EndTouch return on
/// before buffering).
/// </para>
/// </summary>
public sealed class ClientPhysicsSystem(IClientPhysicsLevelCalls vphysics, IClientPhysicsClock clock,
                                        ClientPhysicsEnvironment environment, Func<IClientPhysicsWorld?> world,
                                        Func<IClientRagdollStep?> ragdolls, IClientPhysicsStepTail tail, ILog log)
    : IClientPhysicsFrame, IReloadParticipant
{
    /// <summary>Managed's IPhysicsCollisionEvent table, once built; 0 installs none.</summary>
    public nint CollisionEvents { get; set; }

    /// <summary>Managed's IPhysicsCollisionSolver table, once built; 0 installs none.</summary>
    public nint CollisionSolver { get; set; }

    /// <summary>The level's environment, or 0.</summary>
    public nint Environment => environment.Current;

    /// <summary>The level's world object (stock's <c>g_PhysWorldObject</c>), or 0.</summary>
    public nint World { get; private set; }

    /// <summary>Whether the level's environment carries this build's tables.</summary>
    public bool Installed => _installedOn != 0 && _installedOn == environment.Current;

    /// <summary>How many times the tables went on an environment.</summary>
    public long Installs { get; private set; }

    /// <summary>Steps simulated (an environment existed).</summary>
    public long Steps { get; private set; }

    private nint _installedOn;

    // The step's cost meter (cs_cl_physics): Stopwatch ticks over the whole
    // managed step, and over the part spent inside vphysics' Simulate.
    private long _stepTicks;
    private long _solverTicks;

    /// <summary>Mean wall time of a step with an environment, whole, in ns (0 before one).</summary>
    public double StepNs => Steps == 0 ? 0 : _stepTicks * 1e9 / Stopwatch.Frequency / Steps;

    /// <summary>Mean wall time of a step's vphysics Simulate call, in ns.</summary>
    public double SolverNs => Steps == 0 ? 0 : _solverTicks * 1e9 / Stopwatch.Frequency / Steps;

    /// <summary>Forget the meter (cs_cl_physics reset).</summary>
    public void ResetMeter()
    {
        _stepTicks = 0;
        _solverTicks = 0;
        Steps = 0;
    }

    /// <summary><c>CPhysicsSystem::LevelInitPreEntity</c>.</summary>
    public void LevelInitPreEntity()
    {
        tail.RemoveAllImpactSounds();
        world()?.PrecacheSurfaceSounds();
    }

    /// <summary><c>CPhysicsSystem::LevelInitPostEntity</c>: stock's <c>PhysicsLevelInit</c>.</summary>
    public void LevelInitPostEntity()
    {
        nint env = vphysics.CreateEnvironment();
        environment.Current = env;
        if (env == 0)
        {
            log.Warning("[SourceSharp/Client] physics: vphysics made no environment for the level\n");
            return;
        }

        vphysics.SetGravity(env, new Vector(0f, 0f, -clock.Gravity));
        vphysics.SetSimulationTimestep(env, clock.IntervalPerTick);
        Install(env);

        IClientPhysicsWorld? w = world();
        World = w?.CreateWorld() ?? 0;
        if (World == 0)
            log.Warning("[SourceSharp/Client] the level has no world physics object\n");
        w?.CreateStaticProps();
    }

    /// <summary><c>CPhysicsSystem::LevelShutdownPreEntity</c>.</summary>
    public void LevelShutdownPreEntity()
    {
        if (environment.Current != 0)
            vphysics.SetQuickDelete(environment.Current, true);
    }

    /// <summary><c>CPhysicsSystem::LevelShutdownPostEntity</c>: the bodies first, then the environment they live in.</summary>
    public void LevelShutdownPostEntity()
    {
        ragdolls()?.DestroyAll();

        if (environment.Current != 0)
            vphysics.DestroyEnvironment(environment.Current);

        vphysics.DestroyAllCollisionSets();
        environment.Current = 0;
        World = 0;
        _installedOn = 0;
    }

    /// <summary><c>PhysicsReset</c> (the HUD reset).</summary>
    public void Reset()
    {
        if (environment.Current != 0)
            vphysics.ResetSimulationClock(environment.Current);
    }

    /// <summary><c>CPhysicsSystem::PhysicsSimulate</c>, once a frame.</summary>
    public void Simulate()
    {
        nint env = environment.Current;
        if (env != 0)
        {
            long start = Stopwatch.GetTimestamp();

            // A reload adopts the environment with the old build's tables on
            // it: this build's go on before its first step.
            if (_installedOn != env)
                Install(env);

            long solver = Stopwatch.GetTimestamp();
            vphysics.Simulate(env, clock.FrameTime * clock.TimeScale);
            _solverTicks += Stopwatch.GetTimestamp() - solver;
            ragdolls()?.StepPoses();
            tail.UpdateFrictionSounds();
            tail.UpdateTouchEvents();
            tail.UpdateFluidEvents();
            tail.PlayImpactSounds();
            Steps++;
            _stepTicks += Stopwatch.GetTimestamp() - start;
            return;
        }

        tail.PlayImpactSounds();
    }

    private void Install(nint env)
    {
        if (CollisionEvents != 0)
            vphysics.SetCollisionEventHandler(env, CollisionEvents);
        if (CollisionSolver != 0)
            vphysics.SetCollisionSolver(env, CollisionSolver);
        _installedOn = env;
        Installs++;
    }

    // A reload keeps the level, and the environment and its world with it.
    public void Save(ReloadWriter writer)
    {
        Write(writer, environment.Current);
        Write(writer, World);
    }

    public void Restore(ReloadReader reader)
    {
        environment.Current = Read(reader);
        World = Read(reader);
        _installedOn = 0;
    }

    private static void Write(ReloadWriter w, nint pointer)
    {
        long value = pointer;
        w.Write(unchecked((int)value));
        w.Write(unchecked((int)(value >> 32)));
    }

    private static nint Read(ReloadReader r)
    {
        uint lo = unchecked((uint)r.ReadInt());
        long hi = r.ReadInt();
        return (nint)((hi << 32) | lo);
    }
}
