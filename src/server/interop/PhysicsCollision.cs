namespace SourceSharp.Abi;

/// <summary>
/// vphysics' <c>IPhysicsCollision</c>, the members the managed player shadow
/// and the physics diagnostics call (plan_physics_bones_native_zero 2.5),
/// through <see cref="PhysicsCollisionVtbl"/>.
/// </summary>
public interface IVPhysicsCollision
{
    /// <summary><c>BBoxToCollide( mins, maxs )</c>: a box collide, or 0.</summary>
    nint BoxCollide(Vector mins, Vector maxs);

    /// <summary><c>CollideGetAABB</c> of a collide at the origin, unrotated.</summary>
    (Vector Mins, Vector Maxs) Bounds(nint collide);
}

public sealed unsafe class PhysicsCollision : IVPhysicsCollision
{
    public const string InterfaceVersion = "VPhysicsCollision007";

    private readonly void* _self;
    private readonly PhysicsCollisionVtbl* _vtbl;

    private PhysicsCollision(void* self, void** table)
    {
        _self = self;
        _vtbl = (PhysicsCollisionVtbl*)table;
    }

    /// <summary>
    /// From the physics module's factory, through <paramref name="engine"/>'s
    /// own side; null, with the reason, when it does not answer.
    /// </summary>
    public static IVPhysicsCollision? Acquire(ModuleEngine engine, out string why)
        => Vtable.TryAcquire<PhysicsCollisionVtbl>(
               (engine ?? throw new ArgumentNullException(nameof(engine))).Api.Interfaces,
               SourceSharpFactoryType.Physics, InterfaceVersion, out void* self, out void** table, out why)
            ? new PhysicsCollision(self, table)
            : null;

    public nint BoxCollide(Vector mins, Vector maxs)
        => (nint)_vtbl->BBoxToCollide(_self, &mins, &maxs);

    public (Vector Mins, Vector Maxs) Bounds(nint collide)
    {
        if (collide == 0)
            return (Vector.Zero, Vector.Zero);

        Vector mins, maxs;
        Vector origin = Vector.Zero, angles = Vector.Zero;
        _vtbl->CollideGetAABB(_self, &mins, &maxs, (void*)collide, &origin, &angles);
        return (mins, maxs);
    }
}
