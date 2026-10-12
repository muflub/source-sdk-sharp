using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The client's per-frame presentation and input, managed-built
/// (plan_client_vtables CE4): the bodies of the Entry.cs thunks ClientOnHudPaint
/// and ClientOnCreateMove, moved. The generated thunk has already made the
/// client the active side and catches what a body throws; both slots are voids.
/// The command arrives as the generator's untyped pointer to the ABI's
/// SourceSharpUserCmd, which C# reads as UserCmd.
/// </summary>
public sealed unsafe class HudClientBinding(Crossings crossings, IClientModule client) : IManagedHudClient
{
    public void OnCreateMove(void* a0, float a1, int a2)
    {
        crossings.Count(CrossingOp.ClientOnCreateMove);
        if (a0 == null) return;
        client.OnCreateMove(ref *(UserCmd*)a0, a1, a2 != 0);
    }
}
