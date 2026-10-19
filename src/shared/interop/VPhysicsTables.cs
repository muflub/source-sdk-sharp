namespace SourceSharp.Abi;

//=============================================================================
// VPHYSICS' OBJECTS, CALLED THROUGH THEIR FORWARDERS (plan_engine_forwarders.md).
//
// A vphysics object -- an IPhysicsObject, an environment, a controller, a
// constraint -- reaches managed as a pointer and nothing else. Managed used to
// read each object's vptr and index the table it found there, which meant
// knowing the C++ ABI the engine was built with. Now each family of object has
// a forwarder table in each side's forwarder library (SharedForwarders.g.cs), and a
// call is `tables.PhysicsObject->GetMass(obj)`: the object is handed to the
// forwarder, which calls the method through the object's own vtable as the
// compiler laid it out. Nothing here reads a vptr.
//
// The tables are constant data in the library, the same for every object of a
// family and for both sides, so one set serves a whole side. They are bound
// together on first use rather than at construction: a service built before
// its side's shims are attached binds when it is first called, after them.
//=============================================================================

/// <summary>
/// The forwarder tables a vphysics service calls objects through: one per
/// object family, bound together from one side's interface factory on first
/// use, or handed in whole by a fact.
/// </summary>
public sealed unsafe class VPhysicsTables
{
    /// <summary>Every table, in one place.</summary>
    internal struct Set
    {
        public PhysicsEnvironmentFwd* PhysicsEnvironment;
        public PhysicsObjectFwd* PhysicsObject;
        public PhysicsShadowControllerFwd* PhysicsShadowController;
        public PhysicsMotionControllerFwd* PhysicsMotionController;
        public PhysicsPlayerControllerFwd* PhysicsPlayerController;
        public PhysicsFrictionSnapshotFwd* PhysicsFrictionSnapshot;
        public PhysicsFluidControllerFwd* PhysicsFluidController;
        public PhysicsConstraintFwd* PhysicsConstraint;
        public PhysicsConstraintGroupFwd* PhysicsConstraintGroup;
        public PhysicsSpringFwd* PhysicsSpring;
        public PhysicsCollisionSetFwd* PhysicsCollisionSet;
        public PhysicsCollisionDataFwd* PhysicsCollisionData;
        public VPhysicsKeyParserFwd* VPhysicsKeyParser;
    }

    private readonly NativeInterfaceFactory? _factory;
    private Set _set;
    private bool _bound;

    /// <summary>The tables one side's interface factory serves, bound on first use.</summary>
    internal VPhysicsTables(NativeInterfaceFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>Tables a fact built, already bound.</summary>
    internal VPhysicsTables(in Set set)
    {
        _set = set;
        _bound = true;
    }

    internal PhysicsEnvironmentFwd* PhysicsEnvironment => Bound().PhysicsEnvironment;

    internal PhysicsObjectFwd* PhysicsObject => Bound().PhysicsObject;

    internal PhysicsShadowControllerFwd* PhysicsShadowController => Bound().PhysicsShadowController;

    internal PhysicsMotionControllerFwd* PhysicsMotionController => Bound().PhysicsMotionController;

    internal PhysicsPlayerControllerFwd* PhysicsPlayerController => Bound().PhysicsPlayerController;

    internal PhysicsFrictionSnapshotFwd* PhysicsFrictionSnapshot => Bound().PhysicsFrictionSnapshot;

    internal PhysicsFluidControllerFwd* PhysicsFluidController => Bound().PhysicsFluidController;

    internal PhysicsConstraintFwd* PhysicsConstraint => Bound().PhysicsConstraint;

    internal PhysicsConstraintGroupFwd* PhysicsConstraintGroup => Bound().PhysicsConstraintGroup;

    internal PhysicsSpringFwd* PhysicsSpring => Bound().PhysicsSpring;

    internal PhysicsCollisionSetFwd* PhysicsCollisionSet => Bound().PhysicsCollisionSet;

    internal PhysicsCollisionDataFwd* PhysicsCollisionData => Bound().PhysicsCollisionData;

    internal VPhysicsKeyParserFwd* VPhysicsKeyParser => Bound().VPhysicsKeyParser;


    private ref readonly Set Bound()
    {
        if (!_bound)
            Bind();

        return ref _set;
    }

    // A side whose factory does not serve the tables cannot call a vphysics
    // object at all, so the first call says which table and why, as an
    // unserved engine interface does.
    private void Bind()
    {
        if (!TryBind(_factory!, out Set set, out string table, out string why))
            throw InterfaceAcquisitionException.FromReport(SourceSharpFactoryType.Internal, table, why);

        _set = set;
        _bound = true;
    }

    /// <summary>Every table from <paramref name="factory"/>, or the first that is not served.</summary>
    internal static bool TryBind(NativeInterfaceFactory factory, out Set set, out string table, out string why)
    {
        set = default;
        table = string.Empty;

        return Bind(factory, out set.PhysicsEnvironment, ref table, out why)
            && Bind(factory, out set.PhysicsObject, ref table, out why)
            && Bind(factory, out set.PhysicsShadowController, ref table, out why)
            && Bind(factory, out set.PhysicsMotionController, ref table, out why)
            && Bind(factory, out set.PhysicsPlayerController, ref table, out why)
            && Bind(factory, out set.PhysicsFrictionSnapshot, ref table, out why)
            && Bind(factory, out set.PhysicsFluidController, ref table, out why)
            && Bind(factory, out set.PhysicsConstraint, ref table, out why)
            && Bind(factory, out set.PhysicsConstraintGroup, ref table, out why)
            && Bind(factory, out set.PhysicsSpring, ref table, out why)
            && Bind(factory, out set.PhysicsCollisionSet, ref table, out why)
            && Bind(factory, out set.PhysicsCollisionData, ref table, out why)
            && Bind(factory, out set.VPhysicsKeyParser, ref table, out why);
    }

    private static bool Bind<TFwd>(NativeInterfaceFactory factory, out TFwd* fwd, ref string table, out string why)
        where TFwd : unmanaged, IForwarderTable
    {
        table = TFwd.TableName;
        return Forwarders.TryAcquire(factory, out fwd, out why);
    }
}
