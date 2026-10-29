using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server.Interop;

/// <summary>
/// Model precache components and the late-precache check, managed-built
/// (plan_engineapi_vtables E3): the Entry.cs thunks' bodies, moved.
/// </summary>
public sealed unsafe class ResourcesBinding(Crossings crossings, IServerModule game) : IManagedResources
{
    private void Enter(CrossingOp op)
    {
        crossings.Count(op);
    }

    private static string Utf8(byte* p)
        => p == null ? string.Empty : (Marshal.PtrToStringUTF8((nint)p) ?? string.Empty);

    public void ModelPrecacheComponents(byte* a0)
    {
        Enter(CrossingOp.ServerModelPrecacheComponents);
        if (a0 != null)
            game.Level.ModelPrecacheComponents(Utf8(a0));
    }

    /// <summary>
    /// Precache_Model's "warn on out of order precache", decided here
    /// (plan_server_native_v2 T4e): native hands over the name and its
    /// precache window's flag.
    /// </summary>
    public void ModelPrecacheCheck(byte* a0, byte a1)
    {
        Enter(CrossingOp.ServerModelPrecacheCheck);
        if (a0 != null)
            game.Level.ModelPrecacheCheck(Utf8(a0), a1 != 0);
    }
}
