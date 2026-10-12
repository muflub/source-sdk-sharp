using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server.Interop;

/// <summary>
/// Decals, prop-data chunk models and model precache components, managed-built
/// (plan_engineapi_vtables E3): the Entry.cs thunks' bodies, moved. Each answer
/// that failed to -1 (<see cref="NameBuffer.NoAnswer"/>) keeps that answer: the
/// generated thunk's own catch would answer 0, which is a decal index and a
/// written length rather than "no answer".
/// </summary>
public sealed unsafe class ResourcesBinding(Crossings crossings, ILog log, IServerModule game) : IManagedResources
{
    private void Enter(CrossingOp op)
    {
        crossings.Count(op);
    }

    private static string Utf8(byte* p)
        => p == null ? string.Empty : (Marshal.PtrToStringUTF8((nint)p) ?? string.Empty);

    public int ServerDecalIndexForName(byte* a0)
    {
        Enter(CrossingOp.ServerDecalIndexForName);
        try { return game.Decals.IndexForName(Utf8(a0)); }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedResources.ServerDecalIndexForName", e); return -1; }
    }

    public int ServerDecalNameForIndex(int a0, byte* a1, int a2)
    {
        Enter(CrossingOp.ServerDecalNameForIndex);
        try
        {
            if (a1 == null || a2 <= 0) return NameBuffer.NoAnswer;
            return NameBuffer.Write(game.Decals.NameForIndex(a0), new Span<byte>(a1, a2));
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedResources.ServerDecalNameForIndex", e); return NameBuffer.NoAnswer; }
    }

    public int ServerDecalTranslateForGameMaterial(byte* a0, int a1, byte* a2, int a3)
    {
        Enter(CrossingOp.ServerDecalTranslateForGameMaterial);
        try
        {
            if (a2 == null || a3 <= 0 || a0 == null) return NameBuffer.NoAnswer;
            return NameBuffer.Write(game.Decals.TranslateForGameMaterial(Utf8(a0), a1), new Span<byte>(a2, a3));
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedResources.ServerDecalTranslateForGameMaterial", e);
            return NameBuffer.NoAnswer;
        }
    }

    public int ServerPropDataRandomChunkModel(byte* a0, int a1, byte* a2, int a3)
    {
        Enter(CrossingOp.ServerPropDataRandomChunkModel);
        try
        {
            if (a2 == null || a3 <= 0 || a0 == null) return NameBuffer.NoAnswer;
            return NameBuffer.Write(game.PropData.RandomChunkModel(Utf8(a0), a1), new Span<byte>(a2, a3));
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedResources.ServerPropDataRandomChunkModel", e);
            return NameBuffer.NoAnswer;
        }
    }

    public void ModelPrecacheComponents(byte* a0)
    {
        Enter(CrossingOp.ServerModelPrecacheComponents);
        if (a0 != null)
            game.Level.ModelPrecacheComponents(Utf8(a0));
    }
}
