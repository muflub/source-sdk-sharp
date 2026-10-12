using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// What one physics object answers when the diagnostics ask: the public
/// <c>IPhysicsObject</c> reads native's SS-PHYSWORLD report made
/// (plan_physics_bones_native_zero 2.3).
/// </summary>
public readonly record struct PhysicsObjectReadout(
    string? Name, bool Static, bool CollisionEnabled, bool Moveable, bool Asleep, uint Contents,
    nint Collide, float Mass, float SphereRadius, Vector Position, Vector Angles, Vector Velocity,
    ushort GameFlags);

/// <summary>
/// The environment and object reads managed's physics diagnostics make, each a
/// direct call through <see cref="PhysicsEnvironmentVtbl"/> or
/// <see cref="PhysicsObjectVtbl"/> (plan_physics_bones_native_zero 2.3: the
/// SS-PHYSWORLD report, physics_budget and air_density left native). Every
/// call on 0 is a no-op answering zero.
/// </summary>
public interface IVPhysicsInspect
{
    /// <summary><c>IPhysicsEnvironment::GetObjectList</c>, copied.</summary>
    nint[] Objects(nint environment);

    /// <summary><c>GetActiveObjectCount</c> and <c>GetActiveObjects</c>, copied.</summary>
    nint[] ActiveObjects(nint environment);

    /// <summary>The object's public reads, in one record.</summary>
    PhysicsObjectReadout Read(nint physicsObject);

    /// <summary><c>IPhysicsObject::GetPosition</c>'s position alone.</summary>
    Vector Position(nint physicsObject);

    /// <summary><c>IPhysicsObject::GetMaterialIndex</c>.</summary>
    int MaterialIndex(nint physicsObject);

    /// <summary><c>IPhysicsEnvironment::Simulate</c>.</summary>
    void Simulate(nint environment, float deltaTime);

    /// <summary><c>IPhysicsEnvironment::GetAirDensity</c>.</summary>
    float AirDensity(nint environment);

    /// <summary><c>IPhysicsEnvironment::SetAirDensity</c>.</summary>
    void SetAirDensity(nint environment, float density);
}

/// <summary>The real vphysics. Holds nothing: every pointer is handed in.</summary>
public sealed unsafe class VPhysicsInspect : IVPhysicsInspect
{
    private const int MaxName = 256;

    private static PhysicsEnvironmentVtbl* Env(nint environment) => *(PhysicsEnvironmentVtbl**)environment;

    private static PhysicsObjectVtbl* Obj(nint physicsObject) => *(PhysicsObjectVtbl**)physicsObject;

    public nint[] Objects(nint environment)
    {
        if (environment == 0)
            return [];

        int count = 0;
        void** list = (void**)Env(environment)->GetObjectList((void*)environment, &count);
        if (list == null || count <= 0)
            return [];

        var copy = new nint[count];
        for (int i = 0; i < count; i++)
            copy[i] = (nint)list[i];
        return copy;
    }

    public nint[] ActiveObjects(nint environment)
    {
        if (environment == 0)
            return [];

        var vtbl = Env(environment);
        int count = vtbl->GetActiveObjectCount((void*)environment);
        if (count <= 0)
            return [];

        var list = new nint[count];
        fixed (nint* p = list)
            vtbl->GetActiveObjects((void*)environment, p);
        return list;
    }

    public PhysicsObjectReadout Read(nint o)
    {
        if (o == 0)
            return default;

        var vtbl = Obj(o);
        Vector position, angles, velocity;
        vtbl->GetPosition((void*)o, &position, &angles);
        vtbl->GetVelocity((void*)o, &velocity, null);

        return new PhysicsObjectReadout(
            Vtable.TextOf(vtbl->GetName((void*)o), MaxName),
            vtbl->IsStatic((void*)o) != 0,
            vtbl->IsCollisionEnabled((void*)o) != 0,
            vtbl->IsMoveable((void*)o) != 0,
            vtbl->IsAsleep((void*)o) != 0,
            vtbl->GetContents((void*)o),
            (nint)vtbl->GetCollide((void*)o),
            vtbl->GetMass((void*)o),
            vtbl->GetSphereRadius((void*)o),
            position, angles, velocity,
            vtbl->GetGameFlags((void*)o));
    }

    public Vector Position(nint o)
    {
        if (o == 0)
            return Vector.Zero;

        Vector at;
        Obj(o)->GetPosition((void*)o, &at, null);
        return at;
    }

    public int MaterialIndex(nint o) => o == 0 ? 0 : Obj(o)->GetMaterialIndex((void*)o);

    public void Simulate(nint environment, float deltaTime)
    {
        if (environment != 0)
            Env(environment)->Simulate((void*)environment, deltaTime);
    }

    public float AirDensity(nint environment)
        => environment == 0 ? 0f : Env(environment)->GetAirDensity((void*)environment);

    public void SetAirDensity(nint environment, float density)
    {
        if (environment != 0)
            Env(environment)->SetAirDensity((void*)environment, density);
    }
}
