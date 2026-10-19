namespace SourceSharp.Abi;

/// <summary>
/// The objects vphysics is handed as its callbacks (plan_forwarders_everywhere.md
/// phase 5). vphysics calls an <c>IPhysicsCollisionEvent</c> and the rest through
/// their own C++ vtables, so the side's forwarder library builds each
/// (<c>SSFwd_PhysicsCallbacks</c>, fwd_physics.cpp): a native object deriving the
/// Valve interface whose overrides call the managed-built block it is built
/// over, through the table managed wrote.
///
/// <para>
/// <b>The object lives in its block.</b> Each callback's table reserves words
/// after word 0 (<see cref="ImplementedByManagedAttribute.ReservedWords"/>),
/// which the registry zeroes once and never writes again, a reload included;
/// the object is built there, over the block. So vphysics' pointer stays good
/// across a managed reload, which patches the block in place, and the object
/// is freed with the block (<see cref="ManagedVtables.ReleaseCallerHeld"/>), when
/// native has let go of the side. Building it again over a patched block writes
/// the same two words.
/// </para>
/// </summary>
public sealed unsafe class PhysicsCallbackObjects
{
    private readonly PhysicsCallbacksFwd* _fwd;

    internal PhysicsCallbackObjects(PhysicsCallbacksFwd* fwd) => _fwd = fwd;

    /// <summary>The side's builder, from <paramref name="engine"/>'s own factory, or null with the reason.</summary>
    public static PhysicsCallbackObjects? Acquire(ModuleEngine engine, out string why)
        => Acquire((engine ?? throw new ArgumentNullException(nameof(engine))).Api.Interfaces, out why);

    /// <summary>The same, over a named factory.</summary>
    internal static PhysicsCallbackObjects? Acquire(NativeInterfaceFactory factory, out string why)
        => Forwarders.TryAcquire(factory, out PhysicsCallbacksFwd* table, out why)
            ? new PhysicsCallbackObjects(table)
            : null;

    /// <summary>The <c>IPhysicsObjectEvent</c> built over <paramref name="block"/>.</summary>
    public nint ObjectEvent(ManagedVtable block) => Build(block, _fwd->ConstructObjectEvent);

    /// <summary>The <c>IPhysicsCollisionEvent</c> built over <paramref name="block"/>.</summary>
    public nint CollisionEvent(ManagedVtable block) => Build(block, _fwd->ConstructCollisionEvent);

    /// <summary>The <c>IPhysicsCollisionSolver</c> built over <paramref name="block"/>.</summary>
    public nint CollisionSolver(ManagedVtable block) => Build(block, _fwd->ConstructCollisionSolver);

    /// <summary>The <c>IPhysicsConstraintEvent</c> built over <paramref name="block"/>.</summary>
    public nint ConstraintEvent(ManagedVtable block) => Build(block, _fwd->ConstructConstraintEvent);

    /// <summary>The <c>IMotionEvent</c> built over <paramref name="block"/>.</summary>
    public nint MotionEvent(ManagedVtable block) => Build(block, _fwd->ConstructMotionEvent);

    /// <summary>The <c>IVPhysicsKeyHandler</c> built over <paramref name="block"/>.</summary>
    public nint KeyHandler(ManagedVtable block) => Build(block, _fwd->ConstructKeyHandler);

    /// <summary>
    /// The object built in <paramref name="block"/>'s reserved words, the word
    /// after word 0, over the block itself. Refuses a block that reserves fewer
    /// bytes than the library builds in, naming both, rather than let the
    /// object overwrite the block's header.
    /// </summary>
    private nint Build(ManagedVtable block, delegate* unmanaged<void*, void*, void*> construct)
    {
        ArgumentNullException.ThrowIfNull(block);

        int needed = _fwd->SizeOf();
        int reserved = block.ReservedWords * sizeof(nint);
        if (needed > reserved)
            throw new InvalidOperationException(
                $"{block.Contract}: libss_fwd builds its vphysics callback in {needed} byte(s) and the block "
                + $"reserves {reserved}: the library and this assembly were built from different headers");

        void* built = construct((nint*)block.Pointer + 1, block.Pointer);
        if (built == null)
            throw new InvalidOperationException($"{block.Contract}: libss_fwd built no vphysics callback over the block");

        return (nint)built;
    }
}
