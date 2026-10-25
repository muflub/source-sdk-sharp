using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The vphysics calls the client's physics system makes for its level and its
/// step (plan physics_bones_native_zero, the client leftovers): what stock's
/// game/client/physics.cpp asked of <c>physics</c> and <c>physenv</c>. A 0
/// environment is a no-op, as stock's <c>if ( physenv )</c> tests are.
/// </summary>
public interface IClientPhysicsLevelCalls
{
    /// <summary><c>physics-&gt;CreateEnvironment()</c>; 0 when vphysics made none.</summary>
    nint CreateEnvironment();

    /// <summary><c>physics-&gt;DestroyEnvironment( env )</c>.</summary>
    void DestroyEnvironment(nint environment);

    /// <summary><c>physics-&gt;DestroyAllCollisionSets()</c>.</summary>
    void DestroyAllCollisionSets();

    /// <summary><c>IPhysicsEnvironment::SetGravity</c>.</summary>
    void SetGravity(nint environment, Vector gravity);

    /// <summary><c>IPhysicsEnvironment::SetSimulationTimestep</c>.</summary>
    void SetSimulationTimestep(nint environment, float timestep);

    /// <summary><c>IPhysicsEnvironment::SetCollisionEventHandler</c>.</summary>
    void SetCollisionEventHandler(nint environment, nint handler);

    /// <summary><c>IPhysicsEnvironment::SetCollisionSolver</c>.</summary>
    void SetCollisionSolver(nint environment, nint solver);

    /// <summary><c>IPhysicsEnvironment::SetQuickDelete</c>.</summary>
    void SetQuickDelete(nint environment, bool quickDelete);

    /// <summary><c>IPhysicsEnvironment::Simulate</c>.</summary>
    void Simulate(nint environment, float deltaTime);

    /// <summary><c>IPhysicsEnvironment::ResetSimulationClock</c>.</summary>
    void ResetSimulationClock(nint environment);
}

/// <summary>
/// The client's level environment, held for every consumer that asks for it by
/// <see cref="IClientPhysicsEngine"/> (the ragdolls, the world build). Written
/// only by the client's physics system, which makes and destroys it.
/// </summary>
public sealed class ClientPhysicsEnvironment : IClientPhysicsEngine
{
    /// <summary>The level's IPhysicsEnvironment, or 0 outside a level.</summary>
    public nint Current { get; set; }

    public nint Environment() => Current;
}

/// <summary>
/// The real vphysics, through libss_fwd_client's forwarders
/// (plan_engine_forwarders.md): <c>IPhysics</c> by its factory name, the
/// environment through the side's <see cref="VPhysicsTables"/>.
/// </summary>
public sealed unsafe class VPhysicsClientLevel : IClientPhysicsLevelCalls
{
    private readonly void* _physics;
    private readonly PhysicsFwd* _physicsFwd;
    private readonly VPhysicsTables _vphysics;

    private VPhysicsClientLevel(void* physics, PhysicsFwd* physicsFwd, VPhysicsTables vphysics)
    {
        _physics = physics;
        _physicsFwd = physicsFwd;
        _vphysics = vphysics;
    }

    /// <summary><c>IPhysics</c> from <paramref name="engine"/>'s own side; null with the reason when it does not answer.</summary>
    public static VPhysicsClientLevel? Acquire(ModuleEngine engine, out string why)
    {
        NativeInterfaceFactory factory = (engine ?? throw new ArgumentNullException(nameof(engine))).Interfaces;

        if (!Forwarders.TryAcquire<PhysicsFwd>(factory, SourceSharpFactoryType.Physics, "VPhysics031",
                                               out void* physics, out PhysicsFwd* physicsFwd, out why))
        {
            return null;
        }

        return new VPhysicsClientLevel(physics, physicsFwd, engine.VPhysics);
    }

    private PhysicsEnvironmentFwd* Env => _vphysics.PhysicsEnvironment;

    public nint CreateEnvironment() => (nint)_physicsFwd->CreateEnvironment(_physics);

    public void DestroyEnvironment(nint environment)
    {
        if (environment != 0)
            _physicsFwd->DestroyEnvironment(_physics, (void*)environment);
    }

    public void DestroyAllCollisionSets() => _physicsFwd->DestroyAllCollisionSets(_physics);

    public void SetGravity(nint environment, Vector gravity)
    {
        if (environment != 0)
            Env->SetGravity((void*)environment, &gravity);
    }

    public void SetSimulationTimestep(nint environment, float timestep)
    {
        if (environment != 0)
            Env->SetSimulationTimestep((void*)environment, timestep);
    }

    public void SetCollisionEventHandler(nint environment, nint handler)
    {
        if (environment != 0)
            Env->SetCollisionEventHandler((void*)environment, (void*)handler);
    }

    public void SetCollisionSolver(nint environment, nint solver)
    {
        if (environment != 0)
            Env->SetCollisionSolver((void*)environment, (void*)solver);
    }

    public void SetQuickDelete(nint environment, bool quickDelete)
    {
        if (environment != 0)
            Env->SetQuickDelete((void*)environment, quickDelete ? (byte)1 : (byte)0);
    }

    public void Simulate(nint environment, float deltaTime)
    {
        if (environment != 0)
            Env->Simulate((void*)environment, deltaTime);
    }

    public void ResetSimulationClock(nint environment)
    {
        if (environment != 0)
            Env->ResetSimulationClock((void*)environment);
    }
}
