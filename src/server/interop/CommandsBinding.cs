using System.Runtime.InteropServices;

using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The server's console commands, convar changes and client commands,
/// managed-built (plan_engineapi_vtables E15). These are the Entry.cs thunks'
/// bodies, moved. A faulting client command answers NotHandled, which is 0, so
/// the generated catch's answer is right for all three slots.
/// </summary>
public sealed unsafe class CommandsBinding(Crossings crossings, ISideModule module, IConVarHost convars, IServerModule game) : IManagedCommands
{
    private static string Utf8(byte* p)
        => p == null ? string.Empty : (Marshal.PtrToStringUTF8((nint)p) ?? string.Empty);

    private void Enter(CrossingOp op)
    {
        crossings.Count(op);
    }

    public void ConCommandDispatch(int a0, byte* a1)
    {
        Enter(CrossingOp.ServerConCommand);
        module.ConCommandDispatch(a0, Utf8(a1));
    }

    public void ConVarChanged(int a0, byte* a1, byte* a2)
    {
        Enter(CrossingOp.ServerConVarChanged);
        convars.OnConVarChanged(a0, Utf8(a1), Utf8(a2));
    }

    public int OnClientCommand(uint a0, byte* a1)
    {
        Enter(CrossingOp.ServerOnClientCommand);
        return (int)game.OnClientCommand(new EntityHandle(a0), Utf8(a1));
    }
}
