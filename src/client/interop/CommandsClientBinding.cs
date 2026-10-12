using System.Runtime.InteropServices;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The client's console commands and convar changes, managed-built
/// (plan_client_vtables CE2): the bodies of the Entry.cs thunks ClientConCommand
/// and ClientConVarChanged, moved, as the server's CommandsBinding took its own
/// (plan_engineapi_vtables E15). The generated thunk has already made the client
/// the active side and catches what a body throws; both slots are voids.
/// </summary>
public sealed unsafe class CommandsClientBinding(Crossings crossings, ISideModule module, IConVarHost convars) : IManagedCommandsClient
{
    private static string Utf8(byte* p)
        => p == null ? string.Empty : (Marshal.PtrToStringUTF8((nint)p) ?? string.Empty);

    public void ConCommandDispatch(int a0, byte* a1)
    {
        crossings.Count(CrossingOp.ClientConCommand);
        module.ConCommandDispatch(a0, Utf8(a1));
    }

    public void ConVarChanged(int a0, byte* a1, byte* a2)
    {
        crossings.Count(CrossingOp.ClientConVarChanged);
        convars.OnConVarChanged(a0, Utf8(a1), Utf8(a2));
    }
}
