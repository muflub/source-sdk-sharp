using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// vphysics' <c>IPhysicsConstraintEvent</c>, in Valve's layout and written by
/// managed (plan_physics_bones_native_zero 2.2). One slot, asked from inside
/// the step when a breakable constraint gives way.
/// </summary>
[ManagedVtable(typeof(PhysicsConstraintEventVtbl))]
public unsafe interface IPhysicsConstraintEventSink
{
    void ConstraintBroken(void* pConstraint);
}

/// <summary>
/// The sink: counted, then handed to the server's constraint table, which
/// knows the constraint by its pointer and tells the router after the step.
/// </summary>
public sealed unsafe class PhysicsConstraintEventBinding(Crossings crossings, Action<nint> broken)
    : IPhysicsConstraintEventSink
{
    // TAKEN OVER (2.2): CPhysConstraintEvents (shell_constraints.cpp).
    public void ConstraintBroken(void* pConstraint)
    {
        crossings.Count(CrossingOp.ServerPhysicsConstraintBroken);
        broken((nint)pConstraint);
    }

    /// <summary>The table vphysics is handed, built over this sink.</summary>
    public static ManagedVtable Create(ManagedVtables vtables, IPhysicsConstraintEventSink target)
        => IPhysicsConstraintEventSink__ManagedVtable.Create(vtables, target);
}
