using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// vphysics' <c>objectparams_t</c> (public/vphysics_interface.h), in its own
/// layout: what <c>IPhysicsEnvironment::CreatePolyObject</c> reads. Explicit
/// offsets; vtable_index_agreement_test's ObjectParamsLayout holds them to the
/// header and VPhysicsObjectParamsLayoutTests holds them here.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = SizeInBytes)]
public struct VPhysicsObjectParams
{
    public const int SizeInBytes = 64;

    [FieldOffset(0)] public nint MassCenterOverride;
    [FieldOffset(8)] public float Mass;
    [FieldOffset(12)] public float Inertia;
    [FieldOffset(16)] public float Damping;
    [FieldOffset(20)] public float RotDamping;
    [FieldOffset(24)] public float RotInertiaLimit;

    /// <summary>A C string vphysics KEEPS (it does not copy it): it must outlive the object.</summary>
    [FieldOffset(32)] public nint Name;

    [FieldOffset(40)] public nint GameData;
    [FieldOffset(48)] public float Volume;
    [FieldOffset(52)] public float DragCoefficient;
    [FieldOffset(56)] public byte EnableCollisions;
}

/// <summary>
/// Every vphysics call the managed player shadow makes (plan_physics_bones_native_zero
/// 2.5), each a direct call through a generated vtable --
/// <see cref="PhysicsEnvironmentFwd"/>, <see cref="PhysicsObjectFwd"/>,
/// <see cref="PhysicsPlayerControllerFwd"/> -- or through the collision and
/// surface tables. A 0 pointer is a no-op answering zero, as stock's NULL
/// checks are.
/// </summary>
public interface IVPhysicsShadowCalls
{
    /// <summary><c>IPhysicsCollision::BBoxToCollide</c>, or 0 with no collision table.</summary>
    nint BoxCollide(Vector mins, Vector maxs);

    /// <summary><c>IPhysicsSurfaceProps::GetSurfaceIndex</c>.</summary>
    int SurfaceIndex(string name);

    /// <summary><c>IPhysicsEnvironment::CreatePolyObject</c> at <paramref name="origin"/>, unrotated.</summary>
    nint CreatePolyObject(nint environment, nint collide, int surfaceProps, Vector origin, in VPhysicsObjectParams p);

    /// <summary><c>IPhysicsEnvironment::DestroyObject</c>.</summary>
    void DestroyObject(nint environment, nint physicsObject);

    /// <summary><c>IPhysicsEnvironment::CreatePlayerController</c>.</summary>
    nint CreatePlayerController(nint environment, nint physicsObject);

    /// <summary><c>IPhysicsEnvironment::DestroyPlayerController</c>.</summary>
    void DestroyPlayerController(nint environment, nint controller);

    /// <summary><c>IPhysicsObject::SetCallbackFlags</c>.</summary>
    void SetCallbackFlags(nint physicsObject, ushort flags);

    /// <summary><c>IPhysicsObject::SetPosition( origin, vec3_angle, teleport )</c>.</summary>
    void SetPosition(nint physicsObject, Vector origin, bool teleport);

    /// <summary><c>IPhysicsObject::SetVelocity</c>; a null angular is NULL.</summary>
    void SetVelocity(nint physicsObject, Vector linear, Vector? angular);

    /// <summary><c>IPhysicsObject::EnableCollisions</c>.</summary>
    void EnableCollisions(nint physicsObject, bool enable);

    /// <summary><c>IPhysicsObject::GetPosition</c>'s position.</summary>
    Vector Position(nint physicsObject);

    /// <summary><c>IPhysicsObject::GetMass</c>.</summary>
    float Mass(nint physicsObject);

    /// <summary><c>IPhysicsObject::IsMoveable</c>.</summary>
    bool IsMoveable(nint physicsObject);

    /// <summary><c>IPhysicsObject::IsAsleep</c>.</summary>
    bool IsAsleep(nint physicsObject);

    /// <summary><c>IPhysicsPlayerController::Update</c>.</summary>
    void Update(nint controller, Vector position, Vector velocity, float secondsToArrival, bool onGround, nint ground);

    /// <summary><c>IPhysicsPlayerController::SetObject</c>.</summary>
    void SetObject(nint controller, nint physicsObject);

    /// <summary><c>IPhysicsPlayerController::StepUp</c>.</summary>
    void StepUp(nint controller, float height);

    /// <summary><c>IPhysicsPlayerController::Jump</c>.</summary>
    void Jump(nint controller);

    /// <summary><c>IPhysicsPlayerController::SetPushMassLimit</c> and <c>SetPushSpeedLimit</c>.</summary>
    void SetPushLimits(nint controller, float maxPushMass, float maxPushSpeed);

    /// <summary><c>IPhysicsPlayerController::GetShadowPosition</c>: whether the solver placed it this tick, and where.</summary>
    (bool Updated, Vector Position) ShadowPosition(nint controller);

    /// <summary><c>IPhysicsPlayerController::GetShadowVelocity</c>.</summary>
    Vector ShadowVelocity(nint controller);

    /// <summary><c>IPhysicsPlayerController::WasFrozen</c>.</summary>
    bool WasFrozen(nint controller);

    /// <summary><c>IPhysicsPlayerController::IsInContact</c>.</summary>
    bool IsInContact(nint controller);
}

/// <summary>The real vphysics. Holds only the two factory tables.</summary>
public sealed unsafe class VPhysicsShadowCalls(IVPhysicsCollision? collision, IFwdPhysicsSurfaceProps surfaces,
                                               VPhysicsTables vphysics)
    : IVPhysicsShadowCalls
{
    private readonly VPhysicsTables _vphysics = vphysics ?? throw new ArgumentNullException(nameof(vphysics));

    private PhysicsEnvironmentFwd* Env(nint e) => _vphysics.PhysicsEnvironment;

    private PhysicsObjectFwd* Obj(nint o) => _vphysics.PhysicsObject;

    private PhysicsPlayerControllerFwd* Ctl(nint c) => _vphysics.PhysicsPlayerController;

    public nint BoxCollide(Vector mins, Vector maxs) => collision?.BoxCollide(mins, maxs) ?? 0;

    public int SurfaceIndex(string name) => surfaces.SurfaceIndex(name);

    public nint CreatePolyObject(nint environment, nint collide, int surfaceProps, Vector origin, in VPhysicsObjectParams p)
    {
        if (environment == 0 || collide == 0)
            return 0;

        Vector angles = Vector.Zero;
        VPhysicsObjectParams copy = p;
        return (nint)Env(environment)->CreatePolyObject((void*)environment, (void*)collide, surfaceProps, &origin,
                                                        &angles, &copy);
    }

    public void DestroyObject(nint environment, nint physicsObject)
    {
        if (environment != 0 && physicsObject != 0)
            Env(environment)->DestroyObject((void*)environment, (void*)physicsObject);
    }

    public nint CreatePlayerController(nint environment, nint physicsObject)
        => environment == 0 || physicsObject == 0
            ? 0
            : (nint)Env(environment)->CreatePlayerController((void*)environment, (void*)physicsObject);

    public void DestroyPlayerController(nint environment, nint controller)
    {
        if (environment != 0 && controller != 0)
            Env(environment)->DestroyPlayerController((void*)environment, (void*)controller);
    }

    public void SetCallbackFlags(nint o, ushort flags)
    {
        if (o != 0)
            Obj(o)->SetCallbackFlags((void*)o, flags);
    }

    public void SetPosition(nint o, Vector origin, bool teleport)
    {
        if (o == 0)
            return;

        Vector angles = Vector.Zero;
        Obj(o)->SetPosition((void*)o, &origin, &angles, teleport ? (byte)1 : (byte)0);
    }

    public void SetVelocity(nint o, Vector linear, Vector? angular)
    {
        if (o == 0)
            return;

        if (angular is Vector spin)
            Obj(o)->SetVelocity((void*)o, &linear, &spin);
        else
            Obj(o)->SetVelocity((void*)o, &linear, null);
    }

    public void EnableCollisions(nint o, bool enable)
    {
        if (o != 0)
            Obj(o)->EnableCollisions((void*)o, enable ? (byte)1 : (byte)0);
    }

    public Vector Position(nint o)
    {
        if (o == 0)
            return Vector.Zero;

        Vector at;
        Obj(o)->GetPosition((void*)o, &at, null);
        return at;
    }

    public float Mass(nint o) => o == 0 ? 0f : Obj(o)->GetMass((void*)o);

    public bool IsMoveable(nint o) => o != 0 && Obj(o)->IsMoveable((void*)o) != 0;

    public bool IsAsleep(nint o) => o != 0 && Obj(o)->IsAsleep((void*)o) != 0;

    public void Update(nint c, Vector position, Vector velocity, float secondsToArrival, bool onGround, nint ground)
    {
        if (c != 0)
            Ctl(c)->Update((void*)c, &position, &velocity, secondsToArrival, onGround ? (byte)1 : (byte)0, (void*)ground);
    }

    public void SetObject(nint c, nint o)
    {
        if (c != 0)
            Ctl(c)->SetObject((void*)c, (void*)o);
    }

    public void StepUp(nint c, float height)
    {
        if (c != 0)
            Ctl(c)->StepUp((void*)c, height);
    }

    public void Jump(nint c)
    {
        if (c != 0)
            Ctl(c)->Jump((void*)c);
    }

    public void SetPushLimits(nint c, float maxPushMass, float maxPushSpeed)
    {
        if (c == 0)
            return;

        Ctl(c)->SetPushMassLimit((void*)c, maxPushMass);
        Ctl(c)->SetPushSpeedLimit((void*)c, maxPushSpeed);
    }

    public (bool Updated, Vector Position) ShadowPosition(nint c)
    {
        if (c == 0)
            return (false, Vector.Zero);

        Vector at;
        int placed = Ctl(c)->GetShadowPosition((void*)c, &at, null);
        return (placed > 0, at);
    }

    public Vector ShadowVelocity(nint c)
    {
        if (c == 0)
            return Vector.Zero;

        Vector velocity;
        Ctl(c)->GetShadowVelocity((void*)c, &velocity);
        return velocity;
    }

    public bool WasFrozen(nint c) => c != 0 && Ctl(c)->WasFrozen((void*)c) != 0;

    public bool IsInContact(nint c) => c != 0 && Ctl(c)->IsInContact((void*)c) != 0;
}
