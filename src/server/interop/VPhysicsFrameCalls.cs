namespace SourceSharp.Abi;

/// <summary>
/// The vphysics calls managed's physics frame makes (plan_physics_bones_native_zero
/// 2.3: PhysFrame and the player's stress left physics.cpp), each a direct call
/// through <see cref="PhysicsEnvironmentFwd"/>, <see cref="PhysicsObjectFwd"/>,
/// <see cref="PhysicsShadowControllerFwd"/> or
/// <see cref="PhysicsPlayerControllerFwd"/>. A 0 pointer answers zero.
/// </summary>
public interface IVPhysicsFrameCalls
{
    /// <summary><c>IPhysicsEnvironment::Simulate</c>.</summary>
    void Simulate(nint environment, float deltaTime);

    /// <summary><c>IPhysicsEnvironment::ResetSimulationClock</c>.</summary>
    void ResetSimulationClock(nint environment);

    /// <summary><c>IPhysicsEnvironment::DebugCheckContacts</c> (a debug build's check).</summary>
    void DebugCheckContacts(nint environment);

    /// <summary>
    /// <c>GetActiveObjectCount</c> and <c>GetActiveObjects</c> into
    /// <paramref name="buffer"/>, grown when too small and otherwise reused, so
    /// a frame allocates nothing. The count.
    /// </summary>
    int ActiveObjects(nint environment, ref nint[] buffer);

    /// <summary><c>IPhysicsEnvironment::GetGravity</c>.</summary>
    Vector Gravity(nint environment);

    /// <summary><c>IPhysicsEnvironment::GetSimulationTimestep</c>.</summary>
    float Timestep(nint environment);

    bool IsAsleep(nint physicsObject);

    /// <summary><c>IPhysicsObject::GetVelocity</c>, the linear half.</summary>
    Vector Velocity(nint physicsObject);

    bool IsMoveable(nint physicsObject);
    bool IsStatic(nint physicsObject);
    float Mass(nint physicsObject);
    ushort GameFlags(nint physicsObject);
    ushort CallbackFlags(nint physicsObject);
    nint GameData(nint physicsObject);

    /// <summary>
    /// <c>GetShadowController</c>, and when there is one whether it is
    /// physically controlled and its last impulse.
    /// </summary>
    (bool Has, bool PhysicallyControlled, Vector LastImpulse) ShadowController(nint physicsObject);

    /// <summary><c>IPhysicsPlayerController::GetLastImpulse</c>.</summary>
    Vector PlayerControllerLastImpulse(nint controller);

    /// <summary>The object's friction snapshot, walked (as <see cref="Server.Interop.IVPhysicsSolverObjects.Contacts"/>).</summary>
    int Contacts(nint physicsObject, Span<Server.Interop.SnapshotContact> into);
}

/// <summary>The real vphysics. Holds nothing.</summary>
public sealed unsafe class VPhysicsFrameCalls : IVPhysicsFrameCalls
{
    private readonly VPhysicsTables _vphysics;

    /// <summary>Over <paramref name="vphysics"/>, its side's vphysics forwarder tables.</summary>
    public VPhysicsFrameCalls(VPhysicsTables vphysics)
    {
        _vphysics = vphysics ?? throw new ArgumentNullException(nameof(vphysics));
        _objects = new Server.Interop.VPhysicsSolverObjects(vphysics);
    }

    private readonly Server.Interop.VPhysicsSolverObjects _objects;

    private PhysicsEnvironmentFwd* Env(nint e) => _vphysics.PhysicsEnvironment;

    private PhysicsObjectFwd* Obj(nint o) => _vphysics.PhysicsObject;

    public void Simulate(nint environment, float deltaTime)
    {
        if (environment != 0)
            Env(environment)->Simulate((void*)environment, deltaTime);
    }

    public void ResetSimulationClock(nint environment)
    {
        if (environment != 0)
            Env(environment)->ResetSimulationClock((void*)environment);
    }

    public void DebugCheckContacts(nint environment)
    {
        if (environment != 0)
            Env(environment)->DebugCheckContacts((void*)environment);
    }

    public int ActiveObjects(nint environment, ref nint[] buffer)
    {
        if (environment == 0)
            return 0;

        var vtbl = Env(environment);
        int count = vtbl->GetActiveObjectCount((void*)environment);
        if (count <= 0)
            return 0;

        if (buffer.Length < count)
            buffer = new nint[Math.Max(count, buffer.Length * 2)];

        fixed (nint* p = buffer)
            vtbl->GetActiveObjects((void*)environment, p);
        return count;
    }

    public Vector Gravity(nint environment)
    {
        if (environment == 0)
            return Vector.Zero;

        Vector g;
        Env(environment)->GetGravity((void*)environment, &g);
        return g;
    }

    public float Timestep(nint environment)
        => environment == 0 ? 0f : Env(environment)->GetSimulationTimestep((void*)environment);

    public bool IsAsleep(nint o) => o != 0 && Obj(o)->IsAsleep((void*)o) != 0;

    public bool IsMoveable(nint o) => o != 0 && Obj(o)->IsMoveable((void*)o) != 0;

    public Vector Velocity(nint o)
    {
        Vector velocity = default;
        if (o != 0)
            Obj(o)->GetVelocity((void*)o, &velocity, null);
        return velocity;
    }

    public bool IsStatic(nint o) => o != 0 && Obj(o)->IsStatic((void*)o) != 0;

    public float Mass(nint o) => o == 0 ? 0f : Obj(o)->GetMass((void*)o);

    public ushort GameFlags(nint o) => o == 0 ? (ushort)0 : Obj(o)->GetGameFlags((void*)o);

    public ushort CallbackFlags(nint o) => o == 0 ? (ushort)0 : Obj(o)->GetCallbackFlags((void*)o);

    public nint GameData(nint o) => o == 0 ? 0 : (nint)Obj(o)->GetGameData((void*)o);

    public (bool Has, bool PhysicallyControlled, Vector LastImpulse) ShadowController(nint o)
    {
        if (o == 0)
            return (false, false, Vector.Zero);

        void* shadow = Obj(o)->GetShadowController((void*)o);
        if (shadow == null)
            return (false, false, Vector.Zero);

        var vtbl = _vphysics.PhysicsShadowController;
        Vector impulse;
        vtbl->GetLastImpulse(shadow, &impulse);
        return (true, vtbl->IsPhysicallyControlled(shadow) != 0, impulse);
    }

    public Vector PlayerControllerLastImpulse(nint controller)
    {
        if (controller == 0)
            return Vector.Zero;

        Vector impulse;
        (_vphysics.PhysicsPlayerController)->GetLastImpulse((void*)controller, &impulse);
        return impulse;
    }

    public int Contacts(nint o, Span<Server.Interop.SnapshotContact> into) => _objects.Contacts(o, into);
}
