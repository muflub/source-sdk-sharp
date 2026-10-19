using System.Runtime.InteropServices;
using SourceSharp.Abi;

namespace SourceSharp.Tests;

/// <summary>
/// libss_fwd's <c>SSFwd_PhysicsCallbacks</c> faked in managed memory
/// (plan_forwarders_everywhere.md phase 5): each Construct writes what the real
/// library's object holds, a marker naming the kind where the vptr goes and the
/// target after it, into the memory it is handed, and answers that memory, so a
/// fact sees where the object was built and over what.
/// </summary>
internal sealed unsafe class FakePhysicsCallbacks : IDisposable
{
    /// <summary>The marker each kind writes at word 0 of its object.</summary>
    public const nint ObjectEvent = 0xC1, CollisionEvent = 0xC2, CollisionSolver = 0xC3,
                      ConstraintEvent = 0xC4, MotionEvent = 0xC5, KeyHandler = 0xC6;

    private readonly PhysicsCallbacksFwd* _table;

    public FakePhysicsCallbacks(int sizeOf = 16)
    {
        _table = (PhysicsCallbacksFwd*)NativeMemory.AllocZeroed((nuint)sizeof(PhysicsCallbacksFwd));
        _table->SizeOf = sizeOf <= 16 ? &SizeOf16 : &SizeOf24;
        _table->ConstructObjectEvent = &BuildObjectEvent;
        _table->ConstructCollisionEvent = &BuildCollisionEvent;
        _table->ConstructCollisionSolver = &BuildCollisionSolver;
        _table->ConstructConstraintEvent = &BuildConstraintEvent;
        _table->ConstructMotionEvent = &BuildMotionEvent;
        _table->ConstructKeyHandler = &BuildKeyHandler;
        Objects = new PhysicsCallbackObjects(_table);
    }

    /// <summary>The builder over the fake table.</summary>
    public PhysicsCallbackObjects Objects { get; }

    /// <summary>The kind marker and the target an object was built with.</summary>
    public static (nint Kind, nint Target) Built(nint built) => (((nint*)built)[0], ((nint*)built)[1]);

    public void Dispose() => NativeMemory.Free(_table);

    [UnmanagedCallersOnly] private static int SizeOf16() => 16;
    [UnmanagedCallersOnly] private static int SizeOf24() => 24;

    private static void* Build(void* memory, void* target, nint kind)
    {
        if (memory == null || target == null)
            return null;
        ((nint*)memory)[0] = kind;
        ((nint*)memory)[1] = (nint)target;
        return memory;
    }

    [UnmanagedCallersOnly] private static void* BuildObjectEvent(void* m, void* t) => Build(m, t, ObjectEvent);
    [UnmanagedCallersOnly] private static void* BuildCollisionEvent(void* m, void* t) => Build(m, t, CollisionEvent);
    [UnmanagedCallersOnly] private static void* BuildCollisionSolver(void* m, void* t) => Build(m, t, CollisionSolver);
    [UnmanagedCallersOnly] private static void* BuildConstraintEvent(void* m, void* t) => Build(m, t, ConstraintEvent);
    [UnmanagedCallersOnly] private static void* BuildMotionEvent(void* m, void* t) => Build(m, t, MotionEvent);
    [UnmanagedCallersOnly] private static void* BuildKeyHandler(void* m, void* t) => Build(m, t, KeyHandler);
}
