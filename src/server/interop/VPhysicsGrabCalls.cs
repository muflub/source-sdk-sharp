using System.Runtime.InteropServices;
using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// <c>hlshadowcontrol_params_t</c> (vphysics_interface.h), 48 bytes: what
/// <c>IPhysicsObject::ComputeShadowControl</c> steers an object towards. The
/// offsets are held by <c>VPhysicsJointParamsLayoutTests</c> and, against the
/// header, by vtable_index_agreement_test's <c>ShadowControlLayout</c>.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 48)]
public struct ShadowControlParams
{
    [FieldOffset(0)] public Vector TargetPosition;
    [FieldOffset(12)] public QAngle TargetRotation;
    [FieldOffset(24)] public float MaxAngular;
    [FieldOffset(28)] public float MaxDampAngular;
    [FieldOffset(32)] public float MaxSpeed;
    [FieldOffset(36)] public float MaxDampSpeed;
    [FieldOffset(40)] public float DampFactor;
    [FieldOffset(44)] public float TeleportDistance;
}

/// <summary>
/// One contact of a friction snapshot, as the grab controller reads it: the
/// other object, whether it can move, its mass, and the contact's surface
/// normal (pointing away from the snapshot's own object).
/// </summary>
public readonly record struct GrabContact(nint Other, bool OtherMoveable, float OtherMass, Vector Normal);

/// <summary>
/// The <c>IPhysicsObject</c> calls the physcannon's grab controller makes
/// (<c>CGrabController</c>, weapon_physcannon.cpp), through the vphysics
/// forwarder tables. A call is the method and nothing else; what to do with
/// the answers is the managed controller's.
/// </summary>
public interface IVPhysicsGrabCalls
{
    /// <summary><c>GetPosition( &amp;pos, &amp;angles )</c>.</summary>
    (Vector Position, QAngle Angles) Position(nint physicsObject);

    /// <summary><c>GetShadowPosition( &amp;pos, NULL )</c>: where the shadow last put it.</summary>
    Vector ShadowPosition(nint physicsObject);

    float Mass(nint physicsObject);

    void SetMass(nint physicsObject, float mass);

    /// <summary><c>GetDamping( NULL, &amp;rot )</c>.</summary>
    float RotDamping(nint physicsObject);

    /// <summary><c>SetDamping( NULL, &amp;rot )</c>.</summary>
    void SetRotDamping(nint physicsObject, float rotDamping);

    void EnableDrag(nint physicsObject, bool enable);

    void Wake(nint physicsObject);

    bool IsMoveable(nint physicsObject);

    /// <summary><c>IsHinged()</c>: a world hinge's object, which no player may pick up.</summary>
    bool IsHinged(nint physicsObject);

    ushort GameFlags(nint physicsObject);

    void SetGameFlags(nint physicsObject, ushort flags);

    int MaterialIndex(nint physicsObject);

    (Vector Linear, Vector Angular) Velocity(nint physicsObject);

    void SetVelocity(nint physicsObject, Vector linear, Vector angular);

    void AddVelocity(nint physicsObject, Vector linear, Vector angular);

    /// <summary><c>SetVelocityInstantaneous( &amp;linear, NULL )</c>.</summary>
    void SetVelocityInstantaneous(nint physicsObject, Vector linear);

    void ApplyForceCenter(nint physicsObject, Vector force);

    void ApplyForceOffset(nint physicsObject, Vector force, Vector worldPoint);

    /// <summary><c>ComputeShadowControl( params, secondsToArrival, dt )</c>: the new seconds to arrival.</summary>
    float ComputeShadowControl(nint physicsObject, in ShadowControlParams p, float secondsToArrival, float dt);

    /// <summary><c>GetContactPoint( NULL, NULL )</c>: whether it touches anything.</summary>
    bool HasContact(nint physicsObject);

    /// <summary>
    /// The object's friction snapshot, contact by contact, into
    /// <paramref name="into"/>: how many were written. The snapshot is made
    /// and destroyed inside the call.
    /// </summary>
    int Contacts(nint physicsObject, Span<GrabContact> into);

    /// <summary>
    /// Every contact's stored strain cleared and its friction recomputed
    /// (the loop of <c>PhysForceClearVelocity</c>).
    /// </summary>
    void ClearContactFriction(nint physicsObject);
}

/// <summary>The real vphysics, by the forwarder tables. Holds nothing.</summary>
public sealed unsafe class VPhysicsGrabCalls : IVPhysicsGrabCalls
{
    private readonly VPhysicsTables _vphysics;

    /// <summary>Over <paramref name="vphysics"/>, its side's vphysics forwarder tables.</summary>
    public VPhysicsGrabCalls(VPhysicsTables vphysics)
        => _vphysics = vphysics ?? throw new ArgumentNullException(nameof(vphysics));

    private PhysicsObjectFwd* Obj => _vphysics.PhysicsObject;

    public (Vector Position, QAngle Angles) Position(nint o)
    {
        Vector position;
        QAngle angles;
        Obj->GetPosition((void*)o, &position, &angles);
        return (position, angles);
    }

    public Vector ShadowPosition(nint o)
    {
        Vector position;
        Obj->GetShadowPosition((void*)o, &position, null);
        return position;
    }

    public float Mass(nint o) => Obj->GetMass((void*)o);

    public void SetMass(nint o, float mass) => Obj->SetMass((void*)o, mass);

    public float RotDamping(nint o)
    {
        float rot = 0f;
        Obj->GetDamping((void*)o, null, &rot);
        return rot;
    }

    public void SetRotDamping(nint o, float rotDamping) => Obj->SetDamping((void*)o, null, &rotDamping);

    public void EnableDrag(nint o, bool enable) => Obj->EnableDrag((void*)o, enable ? (byte)1 : (byte)0);

    public void Wake(nint o) => Obj->Wake((void*)o);

    public bool IsMoveable(nint o) => Obj->IsMoveable((void*)o) != 0;

    public bool IsHinged(nint o) => Obj->IsHinged((void*)o) != 0;

    public ushort GameFlags(nint o) => Obj->GetGameFlags((void*)o);

    public void SetGameFlags(nint o, ushort flags) => Obj->SetGameFlags((void*)o, flags);

    public int MaterialIndex(nint o) => Obj->GetMaterialIndex((void*)o);

    public (Vector Linear, Vector Angular) Velocity(nint o)
    {
        Vector linear, angular;
        Obj->GetVelocity((void*)o, &linear, &angular);
        return (linear, angular);
    }

    public void SetVelocity(nint o, Vector linear, Vector angular) => Obj->SetVelocity((void*)o, &linear, &angular);

    public void AddVelocity(nint o, Vector linear, Vector angular) => Obj->AddVelocity((void*)o, &linear, &angular);

    public void SetVelocityInstantaneous(nint o, Vector linear) => Obj->SetVelocityInstantaneous((void*)o, &linear, null);

    public void ApplyForceCenter(nint o, Vector force) => Obj->ApplyForceCenter((void*)o, &force);

    public void ApplyForceOffset(nint o, Vector force, Vector worldPoint)
        => Obj->ApplyForceOffset((void*)o, &force, &worldPoint);

    public float ComputeShadowControl(nint o, in ShadowControlParams p, float secondsToArrival, float dt)
    {
        fixed (ShadowControlParams* block = &p)
            return Obj->ComputeShadowControl((void*)o, block, secondsToArrival, dt);
    }

    public bool HasContact(nint o) => Obj->GetContactPoint((void*)o, null, null) != 0;

    public int Contacts(nint o, Span<GrabContact> into)
    {
        if (o == 0)
            return 0;

        void* snapshot = Obj->CreateFrictionSnapshot((void*)o);
        if (snapshot == null)
            return 0;

        var friction = _vphysics.PhysicsFrictionSnapshot;
        int count = 0;

        while (count < into.Length && friction->IsValid(snapshot) != 0)
        {
            void* other = friction->GetObject(snapshot, 1);
            Vector normal;
            friction->GetSurfaceNormal(snapshot, &normal);

            into[count++] = new GrabContact((nint)other,
                                                other != null && Obj->IsMoveable(other) != 0,
                                                other != null ? Obj->GetMass(other) : 0f,
                                                normal);
            friction->NextFrictionData(snapshot);
        }

        Obj->DestroyFrictionSnapshot((void*)o, snapshot);
        return count;
    }

    public void ClearContactFriction(nint o)
    {
        if (o == 0)
            return;

        void* snapshot = Obj->CreateFrictionSnapshot((void*)o);
        if (snapshot == null)
            return;

        var friction = _vphysics.PhysicsFrictionSnapshot;

        while (friction->IsValid(snapshot) != 0)
        {
            friction->ClearFrictionForce(snapshot);
            friction->RecomputeFriction(snapshot);
            friction->NextFrictionData(snapshot);
        }

        Obj->DestroyFrictionSnapshot((void*)o, snapshot);
    }
}
