using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The server's game events, managed-built (plan_engineapi_vtables E12): the
/// listener in shell_gameevents.cpp forwards each subscribed event here. The
/// Entry.cs thunk's body, moved. The handle is the live native IGameEvent,
/// valid only for this call. The name cache is this binding's, so it unloads
/// with the side.
/// </summary>
public sealed unsafe class EventsBinding(Crossings crossings, ISideModule module) : IManagedEvents
{
    private readonly Utf8NameCache _names = new();

    public void OnGameEvent(byte* a0, void* a1)
    {
        crossings.Count(CrossingOp.ServerOnGameEvent);
        module.DispatchGameEvent(_names.Get(a0), (IntPtr)a1);
    }
}
