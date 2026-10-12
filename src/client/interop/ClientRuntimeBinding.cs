using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Interop;

/// <summary>
/// plan_client_vtables CD: the shell client DLL's calls into the mod, as the
/// managed-built <c>IManagedRuntimeClient</c> (imanaged_runtime_client.h). The
/// slots are generated from docs/managed-interfaces.tsv; this contract is held
/// to their layout by ManagedVtableGenerator, as IServerRuntime is.
/// </summary>
[ManagedVtable(typeof(ManagedRuntimeClientVtbl))]
public unsafe interface IClientRuntime
{
    void Frame(float frameTime);
    void ClientStage(int stage);
    void LevelInit(byte* pMapName);
    void LevelShutdown();
    void ClientInvalidateMdlCache();
    int ParticlesNeedFullReload(int previousMapParticleFiles);
    int MapMaterialReload(byte* pNextBsp, byte* pPreviousBsp, byte* pOut, int outSize);
}

/// <summary>
/// What the client runtime's slots reach, in safe types: SourceSharp.Client
/// implements it (ClientModule) and stays compiled without /unsafe.
/// </summary>
public interface IClientRuntimeHost
{
    void Frame(float frameTime);
    void OnStage(int stage);
    void LevelInit(string mapName);
    void LevelShutdown();

    /// <summary>The engine flushed its model cache (IBaseClientDLL::InvalidateMdlCache).</summary>
    void InvalidateMdlCache();

    /// <summary>
    /// Whether this level's LevelInitPreEntity must re-read every particle
    /// definition file, given how many map-specific particle files the previous
    /// level loaded (-1: there was no previous level). plan_map_load P8.
    /// </summary>
    bool ParticlesNeedFullReload(int previousMapParticleFiles);

    /// <summary>
    /// The materials a listen server's spawn reloads when <paramref name="nextBsp"/>
    /// is mounted after <paramref name="previousBsp"/> (files on disk; empty: no
    /// previous map): newline-joined, possibly empty, or null for the stock full
    /// reload. plan_map_load P1.
    /// </summary>
    string? MapMaterialReloadSet(string nextBsp, string previousBsp);
}

/// <summary>
/// The bodies of the bootstrap's thunks for the same ManagedApi slots
/// (Entry.ClientFrame, ClientStage, ClientLevelInit, ClientLevelShutdown),
/// moved at CE1. The generated thunk has already made the client the active
/// side and catches what a body throws. Frame and ClientStage stay quiet (seven
/// stages a frame); the level edges log as the thunks' trace did. The level
/// shutdown clears the composition's mirror and the client engine's own model
/// caches (plan_abi_hotreload D8), all three handed in by the composition.
/// </summary>
public sealed unsafe class ClientRuntimeBinding(Crossings crossings, ILog log, IClientRuntimeHost host, StandaloneMirror mirror,
                                                ModelDescriptors models, BreakModels breaks) : IClientRuntime
{
    public void Frame(float frameTime)
    {
        crossings.Count(CrossingOp.ClientFrame);
        host.Frame(frameTime);
    }

    public void ClientStage(int stage)
    {
        crossings.Count(CrossingOp.ClientStage);
        host.OnStage(stage);
    }

    public void LevelInit(byte* pMapName)
    {
        crossings.Count(CrossingOp.ClientLevelInit);
        log.Msg("[SourceSharp] > managed ClientLevelInit\n");
        host.LevelInit(Marshal.PtrToStringUTF8((nint)pMapName) ?? string.Empty);
    }

    public void LevelShutdown()
    {
        crossings.Count(CrossingOp.ClientLevelShutdown);
        log.Msg("[SourceSharp] > managed ClientLevelShutdown\n");
        host.LevelShutdown();
        mirror.Clear();
        models.Clear();
        breaks.Clear();
    }

    /// <summary>
    /// IBaseClientDLL::InvalidateMdlCache, the Entry.cs thunk
    /// ClientInvalidateMdlCache's body, moved at CE9a: the client's mirror of
    /// the server runtime's OnInvalidateMdlCache.
    /// </summary>
    public void ClientInvalidateMdlCache()
    {
        crossings.Count(CrossingOp.ClientInvalidateMdlCache);
        host.InvalidateMdlCache();
    }

    /// <summary>
    /// The shell client's LevelInitPreEntity asking whether to re-read the
    /// particle definitions (1) or only re-precache them (0). plan_map_load P8.
    /// </summary>
    public int ParticlesNeedFullReload(int previousMapParticleFiles)
    {
        crossings.Count(CrossingOp.ClientParticlesNeedFullReload);
        return host.ParticlesNeedFullReload(previousMapParticleFiles) ? 1 : 0;
    }

    /// <summary>
    /// The shell client's ReloadMaterials hook asking what a spawn reloads: the
    /// bytes of newline-joined names written (NUL-terminated, possibly 0), or
    /// -1 for the full reload -- also the answer to anything that fails or does
    /// not fit, so a failure costs time and never an override. plan_map_load P1.
    /// </summary>
    public int MapMaterialReload(byte* pNextBsp, byte* pPreviousBsp, byte* pOut, int outSize)
    {
        crossings.Count(CrossingOp.ClientMapMaterialReload);
        if (pNextBsp == null || pOut == null || outSize <= 0)
            return -1;
        string? set = host.MapMaterialReloadSet(Marshal.PtrToStringUTF8((nint)pNextBsp) ?? string.Empty,
                                                pPreviousBsp == null ? string.Empty : Marshal.PtrToStringUTF8((nint)pPreviousBsp) ?? string.Empty);
        if (set is null)
            return -1;
        int needed = System.Text.Encoding.UTF8.GetByteCount(set);
        if (needed >= outSize)
            return -1;
        var into = new Span<byte>(pOut, outSize);
        int written = System.Text.Encoding.UTF8.GetBytes(set, into);
        into[written] = 0;
        return written;
    }
}

/// <summary>
/// Builds each of the client's managed-built objects in a side's registry. The
/// generated <c>Create</c> methods are internal to this assembly; this is the
/// safe door SourceSharp.Client registers its bindings through. Stateless.
/// </summary>
public static class ClientInteropAreas
{
    public static ManagedVtable Runtime(ManagedVtables vtables, ClientRuntimeBinding binding)
        => IClientRuntime__ManagedVtable.Create(vtables, binding);
}
