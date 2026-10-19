using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// vphysics' <c>IMotionEvent</c>'s managed side (plan_physics_bones_native_zero
/// 2.2/2.4): libss_fwd builds the object vphysics calls
/// (PhysicsCallbackObjects), whose override calls this table. ONE table for every motion
/// controller: vphysics hands Simulate the controller it is asking for
/// (<c>m_handler-&gt;Simulate( this, ... )</c>),
/// which is the key the controller's state is found by.
/// </summary>
[ManagedVtable(typeof(ManagedMotionEventTable))]
public unsafe interface IMotionEventSink
{
    int Simulate(void* pController, void* pObject, float deltaTime, void* linear, void* angular);
}

/// <summary>What the motion slot asks of the server, synchronously, mid-step.</summary>
public interface IMotionAnswers
{
    /// <summary>The controller's answer for one object: an IMotionEvent::simresult_e.</summary>
    int Simulate(nint controller, nint physicsObject, float deltaTime, out Vector linear, out Vector angular);
}

/// <summary>The sink: counted, answered by the server, the answer written where vphysics reads it.</summary>
public sealed unsafe class MotionEventBinding(Crossings crossings, IMotionAnswers answers) : IMotionEventSink
{
    // TAKEN OVER (2.4): CSourceSharpMotionEvent's four Simulates (shell_motioncontrollers.cpp).
    public int Simulate(void* pController, void* pObject, float deltaTime, void* linear, void* angular)
    {
        crossings.Count(CrossingOp.ServerPhysicsMotionSimulate);
        int result = answers.Simulate((nint)pController, (nint)pObject, deltaTime, out Vector l, out Vector a);
        *(Vector*)linear = l;
        *(Vector*)angular = a;
        return result;
    }

    /// <summary>The table vphysics is handed, built over this sink.</summary>
    public static ManagedVtable Create(ManagedVtables vtables, IMotionEventSink target)
        => IMotionEventSink__ManagedVtable.Create(vtables, target);
}
