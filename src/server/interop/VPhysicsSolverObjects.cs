using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// One entry of an object's friction snapshot, as the solver's freeze answer
/// reads it (stock's FindMaxContact walk): the object on the other side, and
/// how hard and where it presses.
/// </summary>
public readonly record struct SnapshotContact(nint Other, float NormalForce, Vector Point, Vector Normal);

/// <summary>
/// What managed's <c>IPhysicsCollisionSolver</c> asks of vphysics' own
/// <c>IPhysicsObject</c>s, through the generated <see cref="PhysicsObjectVtbl"/>
/// and <see cref="PhysicsFrictionSnapshotVtbl"/> (plan_physics_bones_native_zero
/// 2.1). Every call on 0 is a no-op answering zero, as stock's NULL checks are.
/// </summary>
public interface IVPhysicsSolverObjects
{
    /// <summary><c>GetGameData</c>: the owner word (see <see cref="PhysicsGameData"/>).</summary>
    nint GameData(nint physicsObject);

    /// <summary><c>GetGameFlags</c>.</summary>
    uint GameFlags(nint physicsObject);

    /// <summary><c>SetGameFlags</c>.</summary>
    void SetGameFlags(nint physicsObject, uint flags);

    /// <summary><c>GetContents</c>.</summary>
    uint Contents(nint physicsObject);

    /// <summary><c>IsMoveable</c>.</summary>
    bool IsMoveable(nint physicsObject);

    /// <summary><c>IsStatic</c>.</summary>
    bool IsStatic(nint physicsObject);

    /// <summary><c>IsTrigger</c>.</summary>
    bool IsTrigger(nint physicsObject);

    /// <summary><c>GetShadowController</c> is not NULL.</summary>
    bool HasShadowController(nint physicsObject);

    /// <summary><c>GetCallbackFlags</c> (CALLBACK_*).</summary>
    ushort CallbackFlags(nint physicsObject);

    /// <summary><c>GetMass</c>.</summary>
    float Mass(nint physicsObject);

    /// <summary><c>RecheckCollisionFilter</c>: vphysics asks ShouldCollide of its pairs again.</summary>
    void RecheckCollisionFilter(nint physicsObject);

    /// <summary><c>Sleep</c>.</summary>
    void Sleep(nint physicsObject);

    /// <summary>
    /// Stock's <c>PhysForceClearVelocity</c> (physics_shared.cpp): zero the
    /// velocity, then clear and recompute every contact's friction strain.
    /// </summary>
    void ClearVelocityAndStrain(nint physicsObject);

    /// <summary>
    /// The object's friction snapshot, walked: each contact's other object,
    /// normal force, point and surface normal, written into
    /// <paramref name="into"/>. The count of contacts the snapshot had (more
    /// than were written when <paramref name="into"/> is short).
    /// </summary>
    int Contacts(nint physicsObject, Span<SnapshotContact> into);
}

/// <summary>The real objects, by their generated vtables. Holds nothing.</summary>
public sealed unsafe class VPhysicsSolverObjects : IVPhysicsSolverObjects
{
    private static PhysicsObjectVtbl* Vt(nint o) => *(PhysicsObjectVtbl**)o;

    public nint GameData(nint o) => o == 0 ? 0 : (nint)Vt(o)->GetGameData((void*)o);

    public uint GameFlags(nint o) => o == 0 ? 0u : Vt(o)->GetGameFlags((void*)o);

    public void SetGameFlags(nint o, uint flags)
    {
        if (o != 0)
            Vt(o)->SetGameFlags((void*)o, (ushort)flags);
    }

    public uint Contents(nint o) => o == 0 ? 0u : Vt(o)->GetContents((void*)o);

    public bool IsMoveable(nint o) => o != 0 && Vt(o)->IsMoveable((void*)o) != 0;

    public bool IsStatic(nint o) => o != 0 && Vt(o)->IsStatic((void*)o) != 0;

    public bool IsTrigger(nint o) => o != 0 && Vt(o)->IsTrigger((void*)o) != 0;

    public bool HasShadowController(nint o) => o != 0 && Vt(o)->GetShadowController((void*)o) != null;

    public ushort CallbackFlags(nint o) => o == 0 ? (ushort)0 : Vt(o)->GetCallbackFlags((void*)o);

    public float Mass(nint o) => o == 0 ? 0f : Vt(o)->GetMass((void*)o);

    public void RecheckCollisionFilter(nint o)
    {
        if (o != 0)
            Vt(o)->RecheckCollisionFilter((void*)o);
    }

    public void Sleep(nint o)
    {
        if (o != 0)
            Vt(o)->Sleep((void*)o);
    }

    public void ClearVelocityAndStrain(nint o)
    {
        if (o == 0)
            return;

        var vtbl = Vt(o);
        void* snapshot = vtbl->CreateFrictionSnapshot((void*)o);

        Vector zero = Vector.Zero;
        Vector zeroSpin = Vector.Zero;
        vtbl->SetVelocity((void*)o, &zero, &zeroSpin);

        if (snapshot == null)
            return;

        var snap = *(PhysicsFrictionSnapshotVtbl**)snapshot;
        while (snap->IsValid(snapshot) != 0)
        {
            snap->ClearFrictionForce(snapshot);
            snap->RecomputeFriction(snapshot);
            snap->NextFrictionData(snapshot);
        }

        vtbl->DestroyFrictionSnapshot((void*)o, snapshot);
    }

    public int Contacts(nint o, Span<SnapshotContact> into)
    {
        if (o == 0)
            return 0;

        var vtbl = Vt(o);
        void* snapshot = vtbl->CreateFrictionSnapshot((void*)o);
        if (snapshot == null)
            return 0;

        var snap = *(PhysicsFrictionSnapshotVtbl**)snapshot;
        int count = 0;
        while (snap->IsValid(snapshot) != 0)
        {
            if (count < into.Length)
            {
                Vector point, normal;
                nint other = (nint)snap->GetObject(snapshot, 1);
                float force = snap->GetNormalForce(snapshot);
                snap->GetContactPoint(snapshot, &point);
                snap->GetSurfaceNormal(snapshot, &normal);
                into[count] = new SnapshotContact(other, force, point, normal);
            }

            count++;
            snap->NextFrictionData(snapshot);
        }

        vtbl->DestroyFrictionSnapshot((void*)o, snapshot);
        return count;
    }
}
