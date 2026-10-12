using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The client's game events, managed-built (plan_client_vtables CE3): the body
/// of the Entry.cs thunk ClientOnGameEvent, moved, as the server's EventsBinding
/// took its game events (plan_engineapi_vtables E12). The generated thunk has
/// already made the client the active side and catches what a body throws.
///
/// The event handle is the live native IGameEvent, valid only for this call.
/// The name cache is this binding's, so it unloads with the side. User messages
/// left for UserMessagesClientBinding (plan_side_isolation §2.2a).
/// </summary>
public sealed unsafe class EventsClientBinding(Crossings crossings, ISideModule module) : IManagedEventsClient
{
    private readonly Utf8NameCache _names = new();

    public void OnGameEvent(byte* a0, void* a1)
    {
        crossings.Count(CrossingOp.ClientOnGameEvent);
        module.DispatchGameEvent(_names.Get(a0), (IntPtr)a1);
    }
}
