using System.Runtime.InteropServices;
using System.Text;

namespace SourceSharp.Abi;

//=============================================================================
// THE SECOND AREA TO LEAVE THE FLAT TABLE — plan_native_api.md Phase 6, wave 2.
//
// Twenty-two one-line forwards into the god-class become twenty-two calls
// through one vtable, on the recipe EngineFrame proved. The bodies in
// shell_level.cpp and shell_propdata.cpp do not move a line; what changes
// is that managed reaches them through a slot instead of through three
// hand-mirrored declarations each.
//
// WHY THIS ONE IS SECOND, and it is the plan's own choice: every method here
// runs on EVERY MAP LOAD, so `make live-gate`'s two loads of dm_lockdown
// exercise the whole interface — the init half and the shutdown half, in order
// — four times over. A level-scoped mistake lives in the reload, and the reload
// is what this area IS.
//
//-----------------------------------------------------------------------------
// TWO MEMBERS MARSHAL AND THE OTHER TWENTY DO NOT, and both marshallings are
// the ones the god-class was doing. They are here rather than in a helper
// because the interface takes a `const char *` and a pair of arrays, and the
// shape of turning a managed span and a managed list into those is the whole
// of what the wrapper ever was.
//
// LAZY BIND, CREATE RATHER THAN TRYCREATE — the same two decisions EngineFrame
// argues at length. The container builds every area on both sides;
// SourceSharpLevel001 is served only by the server, because the client has no
// level lifecycle to have steps of.
//=============================================================================

/// <summary>
/// <see cref="IEngineLevel"/> over the shim layer's own <c>IShimLevel</c>,
/// called through its forwarders (<c>SSFwd_ShimLevel</c>,
/// plan_forwarders_everywhere.md).
/// </summary>
internal sealed unsafe class EngineLevel : IEngineLevel, ILevelFiles
{
    /// <summary>SourceSharp's own factory name for the area.</summary>
    public const string InterfaceVersion = "SourceSharpLevel001";

    private readonly NativeInterfaceFactory _factory;

    private void* _self;
    private ShimLevelFwd* _vtbl;

    /// <summary>
    /// The one the container builds: over the real ABI slot, acquired through
    /// its own side's factory (plan_abi_hotreload D8), never the process-wide one.
    /// </summary>
    public EngineLevel(EngineSide side) : this((side ?? throw new ArgumentNullException(nameof(side))).Interfaces)
    {
    }

    /// <summary>
    /// The same, over a named factory — the seam that lets a refused bind be
    /// driven at the call site rather than one layer under it.
    /// </summary>
    internal EngineLevel(NativeInterfaceFactory factory) => _factory = factory;

    private ShimLevelFwd* Vtbl
    {
        get
        {
            if (_vtbl == null)
                Bind();

            return _vtbl;
        }
    }

    private void Bind()
    {
        if (!Forwarders.TryAcquire(
                _factory, SourceSharpFactoryType.Internal, InterfaceVersion,
                out void* self, out ShimLevelFwd* table, out string why))
            throw InterfaceAcquisitionException.FromReport(
                SourceSharpFactoryType.Internal, InterfaceVersion, why);

        _self = self;
        _vtbl = table;
    }

    /// <inheritdoc/>
    public void ResetServerTime() => Vtbl->ResetServerTime(_self);

    /// <inheritdoc/>
    public void ParseParticleManifest() => Vtbl->ParseParticleManifest(_self);

    /// <inheritdoc/>
    public void ParseParticleManifestForMap(string mapName)
    {
        byte[] bytes = Vtable.Utf8(mapName);

        fixed (byte* p = bytes)
            Vtbl->ParseParticleManifestForMap(_self, p);
    }

    /// <inheritdoc/>
    public bool SpawnWorld(IReadOnlyList<KeyValuePair<string, string>> keys)
    {
        // Each key and value as its own NUL-terminated UTF-8 copy, pinned for
        // the one call: native reads them during it and keeps none of them.
        int n = keys.Count;
        var bytes = new byte[n * 2][];
        for (int i = 0; i < n; i++)
        {
            bytes[i * 2] = Vtable.Utf8(keys[i].Key);
            bytes[i * 2 + 1] = Vtable.Utf8(keys[i].Value);
        }

        var handles = new GCHandle[n * 2];
        try
        {
            var pKeys = stackalloc nint[Math.Max(n, 1)];
            var pValues = stackalloc nint[Math.Max(n, 1)];
            for (int i = 0; i < n * 2; i++)
                handles[i] = GCHandle.Alloc(bytes[i], GCHandleType.Pinned);
            for (int i = 0; i < n; i++)
            {
                pKeys[i] = handles[i * 2].AddrOfPinnedObject();
                pValues[i] = handles[i * 2 + 1].AddrOfPinnedObject();
            }

            return Vtbl->SpawnWorld(_self, (void*)pKeys, (void*)pValues, n) != 0;
        }
        finally
        {
            foreach (var h in handles)
            {
                if (h.IsAllocated)
                    h.Free();
            }
        }
    }

    /// <inheritdoc/>
    public void PrecachePointTemplates(IReadOnlyList<TemplateSpawn> templates)
    {
        // THE CALL IS MADE EVEN FOR A COUNT OF ZERO. The step is the record of
        // having reached this point in the load, not of having had work to do,
        // and tools/gate5 records the level-load sequence: a step that stopped
        // happening on an empty map would be a reordering nothing else in this
        // tree can see. The god-class made this call unconditionally too.
        int count = templates.Count;
        int bytes = 0;

        for (int i = 0; i < count; i++)
            bytes += templates[i].MapData.Length + 1;

        byte[] blobs = new byte[Math.Max(bytes, 1)];
        int[] sizes = new int[Math.Max(count, 1)];

        int at = 0;

        for (int i = 0; i < count; i++)
        {
            byte[] data = templates[i].MapData;

            Array.Copy(data, 0, blobs, at, data.Length);
            at += data.Length;
            blobs[at++] = 0;
            sizes[i] = templates[i].StringSize;
        }

        fixed (byte* pBlobs = blobs)
        fixed (int* pSizes = sizes)
            Vtbl->PrecachePointTemplates(_self, pBlobs, pSizes, count);
    }

    /// <inheritdoc/>
    public void SetLowViolence(string mapName)
    {
        if (mapName.Length == 0)
            return;

        byte[] bytes = Vtable.Utf8(mapName);

        fixed (byte* p = bytes)
            Vtbl->SetLowViolence(_self, p);
    }

    /// <inheritdoc/>
    public void LoadMessageOfTheDay() => Vtbl->LoadMessageOfTheDay(_self);

    /// <inheritdoc/>
    public int ResetDeleteList() => Vtbl->ResetDeleteList(_self);

    /// <inheritdoc/>
    public void ActivateAllEntities() => Vtbl->ActivateAllEntities(_self);

    /// <inheritdoc/>
    public void SetPrecacheAllowed(bool allowed)
        => Vtbl->SetPrecacheAllowed(_self, allowed ? 1 : 0);

    /// <inheritdoc/>
    public bool IsPrecacheAllowed() => Vtbl->IsPrecacheAllowed(_self) != 0;

    /// <inheritdoc/>
    public void ClearSteamAPIContext() => Vtbl->ClearSteamAPIContext(_self);

    /// <inheritdoc/>
    public void ShutdownStandaloneEntities() => Vtbl->ShutdownStandaloneEntities(_self);

    /// <inheritdoc/>
    public void ShutdownSoundEnt() => Vtbl->ShutdownSoundEnt(_self);

    /// <inheritdoc/>
    public void ClearEntityList() => Vtbl->ClearEntityList(_self);

    /// <inheritdoc/>
    public void UncacheAllParticleSystems() => Vtbl->UncacheAllParticleSystems(_self);

    /// <inheritdoc/>
    public void RecreateParticleDictionary() => Vtbl->RecreateParticleDictionary(_self);

    /// <inheritdoc/>
    public void PropDataLoadFile() => Vtbl->PropDataLoadFile(_self);

    /// <inheritdoc/>
    public void PropDataClearFile() => Vtbl->PropDataClearFile(_self);

    /// <inheritdoc/>
    public bool LoadSaveGameState(string mapName, bool createPlayers)
    {
        // AN EMPTY NAME IS NOT PASSED DOWN, for the same reason ParseAllEntities
        // refuses an empty lump: the engine builds a path out of this string and
        // an empty one names the save directory itself.
        if (mapName.Length == 0)
            return false;

        byte[] bytes = Vtable.Utf8(mapName);

        fixed (byte* p = bytes)
            return Vtbl->LoadSaveGameState(_self, p, createPlayers ? 1 : 0) != 0;
    }

    /// <inheritdoc/>
    public void SetLoadType(MapLoadType how) => Vtbl->SetLoadType(_self, (int)how);

    /// <inheritdoc/>
    public bool PhysicsInit() => Vtbl->PhysicsInit(_self) != 0;

    /// <inheritdoc/>
    public void PhysicsLevelInitPreEntity() => Vtbl->PhysicsLevelInitPreEntity(_self);

    /// <inheritdoc/>
    public void PhysicsLevelInitPostEntity() => Vtbl->PhysicsLevelInitPostEntity(_self);

    /// <inheritdoc/>
    public void PhysicsLevelShutdownPreEntity() => Vtbl->PhysicsLevelShutdownPreEntity(_self);

    /// <inheritdoc/>
    public void PhysicsLevelShutdownPostEntity() => Vtbl->PhysicsLevelShutdownPostEntity(_self);

    /// <inheritdoc/>
    public void StringPoolLevelShutdownPostEntity() => Vtbl->StringPoolLevelShutdownPostEntity(_self);

    /// <inheritdoc/>
    public void ResetEntitySlots() => Vtbl->ResetEntitySlots(_self);

    /// <inheritdoc/>
    public byte[]? ReadFileOnPath(string path, string pathId)
    {
        if (path.Length == 0 || pathId.Length == 0)
            return null;

        byte[] name = Vtable.Utf8(path);
        byte[] id = Vtable.Utf8(pathId);

        fixed (byte* pn = name)
        fixed (byte* pi = id)
        {
            int size = Vtbl->FileSizeOnPath(_self, pn, pi);
            if (size < 0)
                return null;

            var buf = new byte[size];
            if (size == 0)
                return buf;

            fixed (byte* pb = buf)
            {
                int read = Vtbl->ReadFileOnPath(_self, pn, pi, pb, size);
                return read == size ? buf : null;
            }
        }
    }
}
