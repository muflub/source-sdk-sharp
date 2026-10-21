// The real engine behind every area interface.
//
// One class per area, one line per member, and every line is the same line: the
// call the code used to make, made for it. There is deliberately no logic here
// at all — a binding that decided anything would be a second place for a rule
// to live, and the rule is already in the entity that asked.
//
// Engine itself is untouched and stays exactly what it was: the function-pointer
// table and the marshalling around it. This is not a replacement for it, and the
// two must not drift, which is why nothing here does more than forward.
//
// Each class is partial (plan_shim_areas_split phase 7). This file holds what
// both sides forward; a member only one side's engine serves is in that side's
// half (src/server/interop/EngineBinding.Server.cs,
// src/client/interop/EngineBinding.Client.cs), and the other half answers what
// the engine member's attach guard answered there, without calling it.
//
// internal rather than public, and every registration is by interface. Nothing
// outside this assembly should be able to name a binding: a class that reached
// for EngineEntities rather than IEngineEntities would compile, work, and be
// untestable, and it would be the one class nobody noticed had done it.

using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
using System.Reflection;

namespace SourceSharp.Abi;

/// <summary>The real engine, for <see cref="IEngineConsole"/>. Every member is one call.</summary>
internal sealed unsafe partial class EngineConsole : IEngineConsole, IConVarFlagReader
{
    // plan_engineapi_vtables B2, plan_abi_hotreload D8: the side this binding
    // answers for, always. Every container that builds one passes it: a side's
    // own, and that side's transitional defaults (EngineSide.Defaults).
    private readonly EngineSide _side;

    private EngineSide E => _side;

    private readonly IConsoleArea _consoleArea;

    public void ExecuteServerCommand(string command)
    {
        if (!string.IsNullOrEmpty(command))
            EngineBound.Server(E).ServerCommandNow(command);
    }

    /// <summary>
    /// One native convar's value by name. Null when the engine knows no such
    /// convar, and null for a command, which has no value to read.
    /// </summary>
    public string? FindConVarValue(string name)
    {
        const int bufSize = 260;
        byte* buf = stackalloc byte[bufSize];

        var nameBytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* pName = nameBytes)
        {
            if (_consoleArea.FindConVarValue(pName, buf, bufSize) == 0)
                return null;
        }

        return EngineSide.FromUtf8(buf);
    }

    /// <summary>
    /// The flags of the variable the engine holds under <paramref name="name"/>,
    /// or -1 for none (IShimConsole::FindConVarFlags, plan_server_native_v2 T4e).
    /// </summary>
    public int FindConVarFlags(string name)
    {
        if (name.Length == 0) return -1;

        var nameBytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* pName = nameBytes)
        {
            return _consoleArea.FindConVarFlags(pName);
        }
    }

    /// <summary>Reads the engine's current value. Prefer the cached ConVarBase accessors on hot paths.</summary>
    public string GetConVarValue(int id)
    {
        const int bufSize = 260;
        byte* buf = stackalloc byte[bufSize];
        _consoleArea.GetConVarValue(id, buf, bufSize);
        return EngineSide.FromUtf8(buf);
    }

    public void Msg(string message) => E.Msg(message);

    /// <summary>
    /// Registers a console command. Native owns the ConCommand object for the
    /// process lifetime; only the managed handler is rebound on reload, because
    /// UnregisterConCommands is all-or-nothing per DLL
    /// (src/tier1/convar.cpp:93). See plan.md Part 5, rule 2.
    /// </summary>
    /// <param name="flags">
    /// The FCVAR word, carried across and ORed with this DLL's own identifier
    /// flag natively — see the slot's comment in <c>sourcesharp.h</c>. A cheat
    /// command declared without <see cref="ConVarFlags.Cheat"/> is a command
    /// that runs with <c>sv_cheats 0</c>, which is a different command.
    /// </param>
    public int RegisterConCommand(string name, string help, ConVarFlags flags)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name + "\0");
        var helpBytes = Encoding.UTF8.GetBytes(help + "\0");
        fixed (byte* pName = nameBytes)
        fixed (byte* pHelp = helpBytes)
        {
            return _consoleArea.RegisterConCommand(pName, pHelp, unchecked((int)flags));
        }
    }

    /// <summary>
    /// Registers a ConVar. Native owns it for the process lifetime.
    ///
    /// The bounds are optional and are the ENGINE's clamp: a convar with a
    /// minimum clamps every write on the way in, whatever the source, so a
    /// change callback never sees a value outside them.
    /// </summary>
    public int RegisterConVar(string name, string defaultValue, string help, int flags, float? min = null, float? max = null)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name + "\0");
        var defBytes  = Encoding.UTF8.GetBytes(defaultValue + "\0");
        var helpBytes = Encoding.UTF8.GetBytes(help + "\0");
        fixed (byte* pName = nameBytes)
        fixed (byte* pDef = defBytes)
        fixed (byte* pHelp = helpBytes)
        {
            return _consoleArea.RegisterConVar(pName, pDef, pHelp, flags,
                                      min.HasValue ? 1 : 0, min ?? 0f,
                                      max.HasValue ? 1 : 0, max ?? 0f);
        }
    }

    public void SetConVarValue(int id, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        fixed (byte* p = bytes)
        {
            _consoleArea.SetConVarValue(id, p);
        }
    }

    /// <summary>
    /// Sets a convar the ENGINE owns, by name.
    ///
    /// <para>
    /// The partner of <see cref="FindConVarValue"/>, and deliberately not the
    /// same call as <see cref="SetConVarValue"/>: that one addresses a convar
    /// this mod registered and holds an id for, and a renderer convar declared
    /// in native code has no id here to hold.
    /// </para>
    ///
    /// <para>
    /// Goes through ConVarRef rather than the console, which is what makes it
    /// usable at all — a cheat-flagged convar refuses a console change without
    /// sv_cheats, while every stock entity that writes one writes it from code
    /// where no such check applies.
    /// </para>
    ///
    /// <para>
    /// A name that resolves to nothing is ignored. That is the ordinary case
    /// for a client convar named from the server, not an error.
    /// </para>
    /// </summary>
    public void SetConVarValueByName(string name, string value)
    {
        var nameBytes  = Encoding.UTF8.GetBytes(name + "\0");
        var valueBytes = Encoding.UTF8.GetBytes(value + "\0");

        fixed (byte* n = nameBytes)
        fixed (byte* v = valueBytes)
        {
            _consoleArea.SetNamedConVarValue(n, v);
        }
    }

    //-------------------------------------------------------------------------
    // The engine's own console registry.
    //
    // Nothing above this line can see it: RegisterConVar and friends deal only
    // in convars C# declared, addressed by an id native handed back. These read
    // the list the engine already had — thousands of entries belonging to the
    // engine and both game DLLs — and they are the raw crossings. What renders
    // them is NativeConsole, which is where the reason for each of them is.
    //
    // Game thread only, and a snapshot must be drained before the next is taken.
    //-------------------------------------------------------------------------

    /// <summary>Walks the engine's registry; returns how many entries it saw.</summary>
    public int Snapshot()
        => _consoleArea.ConsoleSnapshot();

    /// <summary>Copies one entry out of the last snapshot. False past the end.</summary>
    public bool SnapshotEntry(int index, out NativeConsoleEntry entry)
    {
        entry = default;

        ConsoleEntry raw;

        if (_consoleArea.ConsoleSnapshotEntry(index, &raw) == 0)
            return false;

        // Every buffer is fixed and NUL-terminated native-side, so each of these
        // reads to the terminator rather than to the end of its buffer.
        entry = new NativeConsoleEntry(
            EngineSide.FromUtf8(raw.Name),
            EngineSide.FromUtf8(raw.Value),
            EngineSide.FromUtf8(raw.Default),
            EngineSide.FromUtf8(raw.Help),
            raw.Flags,
            raw.IsCommand != 0,
            raw.HasMin != 0 ? raw.Min : null,
            raw.HasMax != 0 ? raw.Max : null);

        return true;
    }

    public void Warning(string message) => E.Warning(message);
}

/// <summary>The real engine, for <see cref="IEngineHost"/>. Every member is one call.</summary>
internal sealed unsafe partial class EngineHost : IEngineHost
{
    // plan_engineapi_vtables B2, plan_abi_hotreload D8: the side this binding
    // answers for, always. Every container that builds one passes it: a side's
    // own, and that side's transitional defaults (EngineSide.Defaults).
    private readonly EngineSide _side;

    private EngineSide E => _side;

    // The server load's Steam game server (P1 run 3: an instance the server
    // composition owns, not seven statics); a side that registers none, the
    // client, gets NoSteamGameServer's absent answers.
    private readonly ISteamGameServer _steam;

    // The side this binding answers for (plan_abi_hotreload D8): a binding is
    // one side's, so the side its caller runs on is that side.
    public Side ActiveSide => E.Side;
    public float CurrentTime => E.CurrentTime;
    public bool IsInitialized => E.IsInitialized;
    public string ManagedDirectory => E.ManagedDirectory;
    public float RandomFloat(float min, float max) => EngineBound.Random(E).RandomFloat(min, max);
    public int RandomInt(int min, int max) => EngineBound.Random(E).RandomInt(min, max);
    public bool IsDedicatedServer => EngineBound.Server(E).IsDedicatedServer();
    // plan_server_novalve.md P2.5: C#'s own, through the flat API; server.so no
    // longer links libsteam_api.so.
    public bool SteamGameServerReacquire() => _steam.Reacquire();
    public void SteamGameServerClear() => _steam.Clear();
    public void SteamGameServerRequestGameplayStats() => _steam.RequestGameplayStats();
    public bool SteamGameServerLoggedOn() => _steam.LoggedOn();
}

/// <summary>The real engine, for <see cref="IEnginePrecache"/>. Every member is one call.</summary>
internal sealed unsafe partial class EnginePrecache : IEnginePrecache
{
    // plan_engineapi_vtables B2, plan_abi_hotreload D8: the side this binding
    // answers for, always. Every container that builds one passes it: a side's
    // own, and that side's transitional defaults (EngineSide.Defaults).
    private readonly EngineSide _side;

    private EngineSide E => _side;
    public int ModelFrameCount(int modelIndex) => EngineBound.ModelInfo(E).FrameCountAt(modelIndex);
    public ModelType ModelTypeOf(int modelIndex) => EngineBound.ModelInfo(E).TypeAt(modelIndex);
    // Answered in C# (BreakModels), parsed once per model per level.
    public int BreakPieceCount(string model) => E.Breaks.Count(model);
    public bool BreakPieceAt(string model, int index, float defaultBurstScale, CollisionGroup defaultCollisionGroup, out string pieceModel, out BreakPiece piece)
        => E.Breaks.PieceAt(model, index, defaultBurstScale, defaultCollisionGroup, out pieceModel, out piece);
}

// AND SIX MEMBERS IN THIS FILE NOW CALL A BOUND ENGINE INTERFACE RATHER THAN
// THE GOD-CLASS -- plan_native_api.md Phase 3, the per-entry route beside the
// per-area one. They read `EngineBound.<Interface>(E).<Method>` instead of
// `Engine.<Slot>`, so a reader can tell which members have moved by looking at
// the right-hand side and nothing else. Their flat ABI fields are orphans and
// leave in the next removal batch.
//
// ENGINEFRAME IS NOT IN THIS FILE ANY MORE -- plan_native_api.md Phase 2, and
// it is the first area to leave it.
//
// It is in SourceSharp.Abi/EngineFrame.cs, and what moved is not only the
// class: it stopped being seventeen forwards into the god-class and became
// seventeen calls through one vtable, acquired by version string from
// SourceSharp's OWN factory. This file is the LIST OF AREAS THAT HAVE NOT
// MOVED YET; the plan's end state is that it has nothing left in it.
//
// A note is left here rather than a silent gap, because "which areas still
// forward into Engine.cs?" is a question a reader asks of this file, and an
// area that has gone answers it by being named as gone.

/// <summary>
/// The real engine, for <see cref="IEngineStringTables"/>. Every member is one
/// call.
///
/// <para>
/// The six ABI slots are id-addressed and stay that way — the shape change is
/// managed-side over the same native surface. This area is internal, so an id
/// only ever exists between here and <see cref="StringTableHandle"/>.
/// </para>
/// </summary>
internal sealed unsafe partial class EngineStringTables : IEngineStringTables
{
    // plan_engineapi_vtables B2, plan_abi_hotreload D8: the side this binding
    // answers for, always. Every container that builds one passes it: a side's
    // own, and that side's transitional defaults (EngineSide.Defaults).
    private readonly EngineSide _side;

    private EngineSide E => _side;

    public EngineStringTables(EngineSide side) => _side = side ?? throw new ArgumentNullException(nameof(side));

    public int Create(string name, int maxEntries) => Tables.Create(name, maxEntries);
    public int Find(string name) => Tables.Find(name);
    public int AddString(int tableId, string value) => Tables.AddString(tableId, value);
    public int FindStringIndex(int tableId, string value) => Tables.FindStringIndex(tableId, value);
    public string? GetString(int tableId, int stringIndex) => Tables.GetString(tableId, stringIndex);
    public int GetNumStrings(int tableId) => Tables.GetNumStrings(tableId);

    // The engine's container, through this side's own factory, bound on first
    // use (plan_server_native_v2 T3: the six slots and shell_stringtables.h's
    // CShellStringTables are this, over the forwarders).
    private ForwardedStringTables? _tables;
    private ForwardedStringTables Tables => _tables ??= ForwardedStringTables.Acquire(_side);
}

// ENGINELEVEL HAS GONE THE SAME WAY -- plan_native_api.md Phase 6, wave 2, and
// it is in SourceSharp.Abi/EngineLevel.cs. Twenty-two forwards became
// twenty-two calls through IShimLevel's vtable.

/// <summary>The real engine, for <see cref="IEngineFiles"/>. Every member is one call.</summary>
internal sealed unsafe partial class EngineFiles : IEngineFiles
{
    private readonly IEngineArea _engineBaseArea;

    // The side's filesystem, as its composition acquired and registered it,
    // never null. A fact hands a named one, so the -1 rule
    // below is driven at the site that owns it (a mutation arm that dropped
    // the exists question stayed GREEN against a fact written on the impl
    // pair, because that fact never reached this member).
    private readonly IFwdBaseFileSystem _files;

    public EngineFiles(IEngineArea engineBaseArea, IFwdBaseFileSystem files)
    {
        _engineBaseArea = engineBaseArea ?? throw new ArgumentNullException(nameof(engineBaseArea));
        _files = files ?? throw new ArgumentNullException(nameof(files));
    }

    private IFwdBaseFileSystem Files => _files;

    // -1 AND 0 ARE DIFFERENT ANSWERS AND THIS IS WHERE THAT IS DECIDED NOW.
    // IBaseFileSystem::Size answers 0 both for an empty file and for one that
    // is not there, which is precisely why the flat slot went through
    // Open/Size/Close. The pair does it without a handle.
    public int GameFileSize(string path)
        => Files.FileExists(path, GameSearchPath)
            ? (int)Files.FileSize(path, GameSearchPath)
            : -1;

    // THE `GAME` PATH ID, which is the one CPropData's and
    // CDecalEmitterSystem's own KeyValues::LoadFromFile calls used -- so a file
    // either of those could find is a file this can find. Not "MOD": the two
    // scripts this area exists for are under the SDK's hl2/scripts and are on
    // no mod-relative path at all.
    private const string GameSearchPath = "GAME";
    // Through VBaseFileSystem011's forwarders since the user's 2026-10-19 ruling
    // exposed file handles to managed: Open/Size/Read/Close in one call
    // (EngineBaseFileSystem.ReadAll), where four IShimEngine slots used to.
    public string? ReadGameFile(string path)
        => ReadGameFileBytes(path) is { } bytes ? System.Text.Encoding.UTF8.GetString(bytes) : null;
    public byte[]? ReadGameFileBytes(string path) => ReadFileOnPath(path, GameSearchPath);

    public byte[]? ReadFileOnPath(string path, string pathId)
        => string.IsNullOrEmpty(path) || string.IsNullOrEmpty(pathId) ? null : _files.ReadAll(path, pathId);
}

/// <summary>The real engine, for <see cref="IEngineGameEvents"/>. Every member is one call.</summary>
internal sealed unsafe partial class EngineGameEvents : IEngineGameEvents
{
    private readonly IEventsArea _eventsArea;
    public bool EventGetBool(IntPtr handle, string key)
    {
        var bytes = Encoding.UTF8.GetBytes(key + "\0");
        fixed (byte* p = bytes) { return _eventsArea.EventGetBool(handle, p) != 0; }
    }

    public float EventGetFloat(IntPtr handle, string key)
    {
        var bytes = Encoding.UTF8.GetBytes(key + "\0");
        fixed (byte* p = bytes) { return _eventsArea.EventGetFloat(handle, p); }
    }

    public int EventGetInt(IntPtr handle, string key)
    {
        var bytes = Encoding.UTF8.GetBytes(key + "\0");
        fixed (byte* p = bytes) { return _eventsArea.EventGetInt(handle, p); }
    }

    public string EventGetString(IntPtr handle, string key)
    {
        var bytes = Encoding.UTF8.GetBytes(key + "\0");
        fixed (byte* p = bytes) { return EngineSide.FromUtf8(_eventsArea.EventGetString(handle, p)); }
    }

    //-------------------------------------------------------------------------
    // Game events.
    //-------------------------------------------------------------------------

    public void ListenForGameEvent(string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes) { _eventsArea.ListenForGameEvent(p); }
    }

    // No engine answer since plan_server_native_v2 T2: the server creates events through IGameEventManager2's forwarders (ServerGameEvents); the client fires none.
    public IntPtr EventCreate(string name) => IntPtr.Zero;
    // No engine answer since plan_server_native_v2 T2: the server fires events through IGameEventManager2's forwarders (ServerGameEvents); the client fires none.
    public bool EventFire(IntPtr handle) => false;
}

/// <summary>The real engine, for <see cref="IEngineNetworking"/>. Every member is one call.</summary>
internal sealed unsafe partial class EngineNetworking : IEngineNetworking
{
    // plan_engineapi_vtables B2, plan_abi_hotreload D8: the side this binding
    // answers for, always. Every container that builds one passes it: a side's
    // own, and that side's transitional defaults (EngineSide.Defaults).
    private readonly EngineSide _side;

    private EngineSide E => _side;

    private readonly INetworkArea _networkArea;

    // Engine.FireScriptedOutput WAS HERE, AND IS DELETED WITH ITS SLOT --
    // plan_entity.md step 2a. Its one caller was Output.Fire, which now posts
    // straight into the directory's queue; there is nothing for a managed
    // wrapper around a deleted slot to wrap.

    /// <summary>
    /// Declares the parallel hierarchy's shared blob size, before any class on
    /// it is described. Returns the payload word count every class on the
    /// hierarchy will carry, -1 if native refused the size, and 0 when native
    /// does not supply the call.
    ///
    /// <para>
    /// The number is one for the whole hierarchy rather than one per class,
    /// because every entity on that hierarchy is allocated the same blob
    /// length — operator new is a static function with no idea which class the
    /// factory is about to make. Native cannot work it out early enough: it
    /// learns the size from the class descriptions as they arrive, so the first
    /// class described would get fewer payload words than the last.
    /// </para>
    ///
    /// <para>
    /// It also cannot be derived from the classes a side HOSTS. A state may be
    /// declared before one half of its entity has been ported, so the two sides
    /// publish different sets — and two sides deriving the number from what
    /// they publish derive two numbers. This is computed from every declaration
    /// in the shared assembly, which both sides read identically.
    /// </para>
    /// </summary>
    public int NetHierarchyDeclare(int blobBytes)
        => _networkArea.NetHierarchyDeclare(blobBytes);

    /// <summary>
    /// Declares worldspawn's blob size to the native shim, which builds its
    /// table around the number (plan_gamedll_after.md Part B2).
    ///
    /// <para>
    /// SEPARATE FROM <see cref="NetHierarchyDeclare"/> BECAUSE THE WORLD IS NOT
    /// ON THE HIERARCHY. That number sizes every entity on the parallel
    /// hierarchy and is therefore the max over all of them; the world's shim is
    /// a <c>CBaseEntity</c> that allocates its own blob against its own base, so
    /// its size is its own and folding it into that max would put three hundred
    /// bytes on a hundred-odd entities that have no use for them.
    /// </para>
    ///
    /// <para>
    /// Zero with no engine, which is the test harness: nothing is declared, and
    /// the caller reports that it published nothing rather than treating the
    /// absence of a game as a refusal.
    /// </para>
    /// </summary>
    public int WorldNetDeclare(int blobBytes)
        => _networkArea.WorldNetDeclare(blobBytes);

    /// <summary>
    /// Publishes one managed type and returns the tier native assigned it, or
    /// -1. See NetPublishType in sourcesharp.h.
    ///
    /// <para>
    /// This is the whole of what replaces the field-by-field description. What
    /// travels is a name, a layout fingerprint and a size — native has no
    /// opinion about what a field is any more.
    /// </para>
    ///
    /// <para>
    /// <paramref name="nativeTypeId"/> is the id NATIVE hashed from the name.
    /// The caller compares it against <see cref="NetSchema.TypeIdOf"/>, which
    /// turns "two implementations of FNV-1a in two languages" from something a
    /// test corpus covers into something every name in the build covers. -1
    /// with an id of 0 is what an absent engine answers, which is the test
    /// harness rather than a failure.
    /// </para>
    /// </summary>
    public int NetPublishType(string className, int shell, uint schemaHash, int layoutBytes, out uint nativeTypeId)
    {
        nativeTypeId = 0;

        var bytes = Encoding.UTF8.GetBytes(className + "\0");

        fixed (byte* p = bytes)
        fixed (uint* pId = &nativeTypeId)
        {
            return _networkArea.NetPublishType(p, shell, schemaHash, layoutBytes, pId);
        }
    }

    /// <summary>
    /// One tier as native registered it, or null past the end of the list. See
    /// NetTierInfo in sourcesharp.h — this is what <see cref="NetTiers"/> reads
    /// instead of projecting the boundary rule a second time.
    /// </summary>
    public NetTierInfo? NetTierAt(int shell, int tier)
    {
        byte* table = null;
        int bytes = 0, words = 0, props = 0, live = 0;

        if (_networkArea.NetTierInfo(shell, tier, &table, &bytes, &words, &props, &live) == 0)
            return null;

        return new NetTierInfo(tier, EngineSide.FromUtf8(table), bytes, words, props, live != 0);
    }
}

/// <summary>The real engine, for <see cref="IEngineScripted"/>. Every member is one call.</summary>
internal sealed unsafe partial class EngineScripted : IEngineScripted
{
    // plan_engineapi_vtables B2, plan_abi_hotreload D8: the side this binding
    // answers for, always. Every container that builds one passes it: a side's
    // own, and that side's transitional defaults (EngineSide.Defaults).
    private readonly EngineSide _side;

    private EngineSide E => _side;

    // No engine answer since plan_server_native_v2 T2: native map templates are gone (the world and templates arrive parsed); TemplateStore already treats this answer as nothing.
    public List<TemplatePlacement> TemplateCapture(string sourceName, bool removeSources, int limit = 64) => new List<TemplatePlacement>();
}

/// <summary>The real engine, for <see cref="IEngineEntities"/>. Every member is one call.</summary>
internal sealed unsafe partial class EngineEntities : IEngineEntities
{
    // plan_engineapi_vtables B2, plan_abi_hotreload D8: the side this binding
    // answers for, always. Every container that builds one passes it: a side's
    // own, and that side's transitional defaults (EngineSide.Defaults).
    private readonly EngineSide _side;

    private EngineSide E => _side;

    private readonly IEntitiesArea _entitiesArea;
    private readonly IModelsArea _modelsArea;

    private readonly StandaloneSkeletonRoute _skeletons = new();

    // ONE INSTANCE, HELD. plan_entity.md step 6a: the list it wraps is rebuilt
    // by Begin() and read by Block(), so the pair has to be the same object
    // across a pass -- a fresh one per property read would answer Block() out
    // of a list nothing had fetched.
    private readonly IInterpolationBlocks _interpolationBlocks;

    public IInterpolationBlocks InterpolationBlocks => _interpolationBlocks;

    // The same "one object across a pass" rule as the list above, and for the
    // same reason: Begin() fetches the array and Block() reads it.
    private readonly IRenderPolicyBlocks _renderPolicyBlocks;

    public IRenderPolicyBlocks RenderPolicyBlocks => _renderPolicyBlocks;
    // ONE INSTANCE for the same reason: BeginCommand and EndCommand are a
    // bracket, and a fresh object per property read would be closing a bracket
    // a different object opened.
    private readonly IClientReplayEngine _clientReplay;

    public IClientReplayEngine ClientReplay => _clientReplay;
    public int Count() => EngineBound.Server(E).EntityCount();
    // No engine answer since plan_server_native_v2 T2: the SDK entity list's map references went with the list; nothing native holds one to purge.
    public void PurgeMapRefs() { }
    /// <summary>
    /// BOTH DELETE LISTS -- plan_entity.md step 2c. Stock has one; this build has
    /// two, and they are drained at the same four points so that WHEN an entity
    /// stops existing does not depend on which hierarchy it was on.
    ///
    /// <para>
    /// The managed one first, so that anything a teardown asks of native code
    /// has already happened when gEntList drains its own.
    /// </para>
    ///
    /// <para>
    /// <b>The game frame's own removal pass is NOT this</b> — it is
    /// <c>EntityDirectory.FreeRemoved</c>, called from
    /// <c>StandaloneHierarchy.OnThink</c> where the order around the thinks and
    /// the parenting sweep is load-bearing. This is the parity for stock's four
    /// <c>CleanupDeleteList</c> sites, and on a frame where that pass has
    /// already run it finds nothing and costs no crossing.
    /// </para>
    /// </summary>
    public void CleanupDeleteList()
    {
        // The SDK entity list's own delete list went with the list
        // (plan_server_native_v2 T2 deleted its empty native step); the
        // managed removal queue is the whole of it.
        E.Removal.FreeNow();
    }
    public List<EntityHandle> FindAllByName(string name, int limit = 512) => E.FindEntitiesByName(name, limit);
    public EntityHandle FindByName(string name) => E.FindEntityByName(name);
    public bool FireInput(EntityHandle target, string input, string? value = null, EntityHandle activator = default) => E.EntityIo.FireInput(target, input, value, activator);
    public Vector GetAngles(EntityHandle h) => E.EntityGetAngles(h);
    // No engine answer since plan_server_native_v2 T2: armour left with CBasePlayer; Armour.cs is the managed owner.
    public int GetArmor(EntityHandle entity) => 0;

    /// <summary>
    /// The entity's edict index, or -1.
    /// </summary>
    /// <remarks>
    /// <b>-1 for a shell-less entity, and that is not a degradation</b> — step
    /// 2e. It is the same answer the shell gave: a class of kind
    /// <see cref="ScriptedEntityKind.Logical"/> was already server-only, so its
    /// <c>CStandaloneEntity</c> never claimed an edict and <c>entindex()</c>
    /// answered -1 for it. What changes is that there is no longer an object to
    /// ask.
    /// </remarks>
    public int GetIndex(EntityHandle h) => E.EntityGetIndex(h);

    public Vector GetOrigin(EntityHandle h) => E.EntityGetOrigin(h);

    public bool IsValid(EntityHandle h) => E.EntityIsValid(h);
    // No engine answer since plan_server_native_v2 T2: armour left with CBasePlayer; Armour.cs is the managed owner.
    public void SetArmor(EntityHandle entity, int armor) { }
    public bool GetAttachmentFrame(EntityHandle entity, int attachment, out Vector origin, out QAngle angles)
    {
        // A standalone entity's skeleton is managed; anything else (an entity
        // with no skeleton) is the native slot's own-transform answer.
        if (_skeletons.Skeletons is { } skeletons)
        {
            bool found = skeletons.GetAttachment(entity, attachment, out origin, out angles, out bool standalone);

            if (standalone)
                return found;
        }

        return EngineAttachmentFrame(entity, attachment, out origin, out angles);
    }

    /// <summary>The engine's own-transform answer for an entity with no managed skeleton: the server's slot.</summary>
    private partial bool EngineAttachmentFrame(EntityHandle entity, int attachment, out Vector origin, out QAngle angles);

    public int LookupAttachmentAny(EntityHandle entity, string attachmentName)
        // A managed skeleton answers the whole of it. Before the composition
        // fills the route (and on the client, which has no such route) the
        // answer is the engine's "no such attachment", 0, which is all the
        // native slot (IShimHierarchy's, unreached since the flip) answers.
        => _skeletons.Skeletons is { } skeletons
            ? skeletons.LookupAttachment(entity, attachmentName)
            : 0;
    // No engine answer since plan_server_native_v2 T2: no stock-hierarchy entity is ever registered on the server, so the SDK's find-by-name has no population; the managed directory's walk is the answer and every caller asks it first.
    public EntityHandle FindBaseByName(string name) => EntityHandle.None;

    // ---- plan_gamedll.md Part 4, Phase 6: LEVELINIT'S NINE NATIVE STEPS ----
    //
    // Nine wrappers and no tenth that runs them in order. The order is
    // ServerGameDll.MapLoadNewGame's, in C#, where it can be read and tested;
    // a Engine.LevelInit() here would be the same C++ sequence one language
    // further up.

    // // ---- plan_gamedll.md Part 4, Phase 7: SERVERACTIVATE'S AND
    // 	// ---- LEVELSHUTDOWN'S NATIVE STEPS.
    // 	//
    // 	// The same rule as LevelInit's nine above: one slot per irreducible step,
    // 	// C# owns the order and native owns no part of it. LevelShutdown is the
    // 	// clearest case in the whole plan -- stock's body is nine calls and the
    // 	// SEQUENCE is the only thing in it, so a single LevelShutdown slot would
    // 	// have been a crossing that carried nothing but the thing worth reading.
    // 	//
    // 	// SERVER ONLY, all twelve, and NULL on the client for the same reason the
    // 	// nine above are.
    // 	//
    // 	// APPENDED AT THE TAIL: every field above has an offset the managed side
    // 	// has already compiled against.

    // // ---- plan_gamedll.md Part 4, Phase 7: GAMEFRAME'S NATIVE STEPS.
    // 	//
    // 	// Fifteen slots and no GameFrame slot, on the same rule as LevelInit's
    // 	// nine and LevelShutdown's twelve. The two per-frame passes below are the
    // 	// named list's, and THE MANAGED FRAME RUNS INSIDE THEM rather than around
    // 	// them -- SourceSharpManaged is a row of kServerSystems[] at the position
    // 	// CSourceSharpSystem held, so FramePreThink and Frame keep the same native
    // 	// neighbours stock gives them. Hoisting them out would move the managed
    // 	// frame ahead of CSoundEnt, CRagdollLRURetirement and CEntityListSystem,
    // 	// none of which is a no-op. See shell_frame.cpp.
    // 	//
    // 	// SERVER ONLY, all fifteen.
    // 	//
    // 	// APPENDED AT THE TAIL: every field above has an offset the managed side
    // 	// has already compiled against.

    //-------------------------------------------------------------------------
    // MOD-DECLARED NETWORK STRING TABLES — plan_gamedll_after.md Phase A8.
    //
    // ADDITIVE. The seven shared tables (Materials, ParticleEffectNames,
    // EffectDispatch, VguiScreen, InfoPanel, Scenes, ServerMapCycle) are
    // created natively and 141 native references read the globals behind them;
    // nothing here replaces any of that. These six are how a MOD declares a
    // table of its OWN, which is what the CreateNetworkStringTables slot was
    // reserved for.
    //
    // CREATION ONLY WORKS INSIDE IServerModule's CreateNetworkStringTables.
    // The engine opens a window for it there and refuses outside it — Create
    // answers -1 and says so rather than the caller finding out from an index
    // that does not work. Find works at any time, which is what a hot reload
    // needs: the managed side forgets its ids, the engine does not forget its
    // tables.
    //
    // EVERY MISS IS -1. Never INVALID_STRING_INDEX (0xFFFF), which crosses as
    // 65535 and reads as success to every `if (i >= 0)`.
    //-------------------------------------------------------------------------

    // ---- plan_gamedll_after.md Phase A2 ------------------------------------

    // THE THREE STEAM GAME-SERVER SLOTS ARE RETIRED -- plan_server_novalve.md
    // P2.5. IEngineHost's Steam members are C#'s own (SteamGameServerFlat, the
    // flat API); server.so no longer links libsteam_api.so.

    /// <summary>
    /// Stock's InvalidateMdlCache walk: every live entity that owns a
    /// <c>CStudioHdr</c> drops it. Native because that cache is a native member
    /// of <c>CBaseAnimating</c> and no managed walk reaches it.
    ///
    /// <para>
    /// <b>BOTH SIDES, AND IT ANSWERS HOW MANY</b> (lane diag-visibility). The
    /// slot used to be bound on the server only, so this returned quietly on
    /// the client and the flush reached nothing there; and it used to answer
    /// nothing at all, which made an empty body and a working one identical
    /// from outside. Zero when there is no native side to ask, which is a true
    /// answer and not a degraded one: with no side bound there are no entities.
    /// </para>
    /// </summary>
    /// <returns>how many entities dropped a cache.</returns>
    public int InvalidateMdlCacheAll()
    {
        return _modelsArea.EntitiesInvalidateMdlCacheAll();
    }
}

/// <summary>The real engine, for <see cref="IEngineEntityNames"/>. Every member is one call.</summary>
internal sealed unsafe partial class EngineEntityNames : IEngineEntityNames
{
    private readonly EngineSide _side;

    private EngineSide E => _side;

    public EngineEntityNames(EngineSide side) => _side = side ?? throw new ArgumentNullException(nameof(side));

    public NameMatches Named(string name, int limit = 512)
        => name.Length == 0 || E.ShellLess.Mine is not { } side ? NameMatches.None : side.Named(name, limit);
}

/// <summary>The real engine, for <see cref="IEngineOverlaps"/>. Every member is one call.</summary>
internal sealed unsafe partial class EngineOverlaps : IEngineOverlaps
{
}

/// <summary>The real engine, for <see cref="IEngineEntityPhysics"/>. Every member is one call.</summary>
internal sealed unsafe partial class EngineEntityPhysics : IEngineEntityPhysics
{
    // plan_engineapi_vtables B2, plan_abi_hotreload D8: the side this binding
    // answers for, always. Every container that builds one passes it: a side's
    // own, and that side's transitional defaults (EngineSide.Defaults).
    private readonly EngineSide _side;

    private EngineSide E => _side;
    // No engine answer since plan_server_native_v2 T2: a per-entity friction on the movement path is not ported (func_friction, ss_gib ask).
    public void SetFriction(EntityHandle h, float friction) { }
}

/// <summary>
/// The real engine, for <see cref="IEngineEntityRender"/>. Every member is one
/// call — except the three plan_crossings.md Phases X2 and X3 turned round.
///
/// <para>
/// <see cref="IsDormant"/> and <see cref="SequenceState"/> were 51.000
/// crossings a client frame each (A11) — together 62 % of the client's whole
/// boundary — and both were polls of something managed either authors or is
/// already told about. They answer from <see cref="StandaloneMirror"/> now, and
/// <see cref="PlaySequence"/> is what feeds half of it. The slots are KEPT: the
/// mirror falls back to them, the divergence gate compares against them, and
/// the server side (where neither is wired) reaches them unchanged.
/// </para>
/// </summary>
internal sealed unsafe partial class EngineEntityRender : IEngineEntityRender
{
    // plan_engineapi_vtables B2, plan_abi_hotreload D8: the side this binding
    // answers for, always. Every container that builds one passes it: a side's
    // own, and that side's transitional defaults (EngineSide.Defaults).
    private readonly EngineSide _side;

    private EngineSide E => _side;

    private readonly IModelsArea _modelsArea;

    private readonly StandaloneMirror _mirror;

    private readonly StandaloneSkeletonRoute _skeletons;

    private IStandaloneSkeletons? Skeletons => _skeletons.Skeletons;

    /// <summary>The client's skeleton, once its composition has filled the route (plan physics_bones_native_zero 1.2).</summary>
    private IClientStandaloneSkeletons? Client => _skeletons.Skeletons as IClientStandaloneSkeletons;

    /// <summary>
    /// Whether this side's reads and writes go through the mirror at all.
    ///
    /// <para>
    /// <b>Both slots behind it are CLIENT-ONLY</b> — dormancy is a client
    /// concept, and the animation cycle is a clock the two sides legitimately
    /// disagree about, which is why <c>SideAuditTests</c> holds them in the
    /// client-only list. <c>StandaloneSetSequence</c> is wired on BOTH, so a
    /// server write reaching the mirror would stamp a record with the SERVER's
    /// curtime; and a server read answered from the mirror would get a number
    /// where the slot answers false. Both are plausible values rather than
    /// errors, which is the failure this gate exists to stop.
    /// </para>
    /// </summary>
    private bool Mirrored => Recording && _mirror.Rerouted;

    /// <summary>
    /// Whether a write is recorded. The side gate, and ONLY the side gate.
    ///
    /// <para>
    /// <b>Deliberately not <see cref="Mirrored"/>, and the difference was found
    /// by the divergence gate rather than by reasoning.</b> The write was
    /// briefly gated on the reroute as well, so switching the reroute off to
    /// take the census's "before" arm stopped feeding the mirror — and
    /// <c>cs_cl_mirror_check</c> reported the viewmodel's restart counter 18
    /// behind the shell's, growing by one per sequence change and never
    /// recovering. The reroute is a READING control; making it a writing one
    /// too turns a measurement into a corruption.
    /// </para>
    ///
    /// <para>
    /// It is also what makes the "before" arm honest in the other direction:
    /// the same work happens in both arms and only the branch in the two
    /// readers differs, so what the census measures is the reroute and nothing
    /// else. The write-side cost it leaves in the "before" arm is
    /// <c>StandaloneSequenceInfo</c> at 0.01 crossings a client frame,
    /// measured, against the 82.0 the arms differ by.
    /// </para>
    /// </summary>
    private bool Recording => E.Side == Side.Client;

    public string GetModelName(EntityHandle entity) => E.EntityGetModelName(entity);

    public string? GetModelKeyValueText(string modelName)
    {
        if (string.IsNullOrEmpty(modelName))
            return null;

        IFwdModelInfo info = EngineBound.ModelInfo(E);
        int index = info.ModelIndex(modelName);

        return index <= 0 ? null : info.KeyValueText(info.Model(index));
    }
    // No engine answer since plan_server_native_v2 T2: m_nTextureFrameIndex left with CBaseEntity; env_texturetoggle asks.
    public int GetTextureFrame(EntityHandle entity) => 0;
    //=========================================================================
    // THE MODEL'S OWN TABLE, ANSWERED FROM ONE CROSSING -- plan_shim.md 2d.
    //
    // These five used to be five ABI slots, each answering one scalar per call
    // about a file that cannot change while the level is loaded. They read a
    // cached <see cref="ModelDescriptor"/> now, and the crossing behind it is
    // one per MODEL per level.
    //
    // AND THE RULE IS ONE IMPLEMENTATION NOW, WHICH IS THE POINT.
    // LookupSequence's native bodies DISAGREED: the client's called
    // ::LookupSequence, which on a label miss falls through to an activity
    // lookup resolved by SelectWeightedSequence -- a weighted RANDOM pick --
    // while the server's refused that fallback and answered -1. The same ACT_
    // name therefore resolved to a random index on one side and to nothing on
    // the other, on a number that is PREDICTED state. One managed body is the
    // only shape that is the same on both sides by construction.
    //=========================================================================
    public int LookupSequence(EntityHandle entity, string name)
        => E.Models.For(entity).LookupSequence(name);
    public int LookupSequenceByActivity(EntityHandle entity, string activityName)
        => E.Models.For(entity).LookupSequenceByActivity(activityName);
    public float SequenceDuration(EntityHandle entity, int sequence)
        => E.Models.For(entity).SequenceDuration(sequence);
    public float SequenceGroundSpeed(EntityHandle entity, int sequence)
        => Client?.SequenceGroundSpeed(entity, sequence) ?? 0f;

    // THE SKELETON'S QUESTIONS (plan physics_bones_native_zero 1.2): both
    // sides' skeletons are managed and every member below asks the route.
    // The native slots they used to fall back to are retired; a composition
    // that registered no route (a fact's bare binding) gets the answers a
    // handle that names no skeleton got: 0, -1, false, empty.
    public int LookupAttachment(EntityHandle entity, string name)
        => Skeletons?.LookupAttachment(entity, name) ?? 0;

    public bool GetAttachment(EntityHandle entity, int attachment, out Vector origin, out Vector angles)
    {
        if (Skeletons is not { } skeletons)
        {
            origin = default;
            angles = default;
            return false;
        }

        bool found = skeletons.GetAttachment(entity, attachment, out origin, out QAngle frame, out _);
        angles = new Vector(frame.Pitch, frame.Yaw, frame.Roll);
        return found;
    }
    public int AttachmentCount(EntityHandle entity)
        => E.Models.For(entity).Attachments.Count;
    public string AttachmentName(EntityHandle entity, int attachment)
        => E.Models.For(entity).AttachmentName(attachment);

    /// <summary>
    /// How many bones the model has, and how many the BONE CACHE is holding.
    ///
    /// <para>
    /// THE TWO HALVES COME FROM DIFFERENT PLACES NOW, and they always
    /// described different things. The model's bone count is a fact of the
    /// file and comes out of the descriptor; the CACHED count is a fact of
    /// this entity at this instant -- zero until something has asked it to
    /// pose -- so it cannot be cached per model and still comes from the
    /// entity. Collapsing the two into one slot is what made them look like
    /// one question.
    /// </para>
    /// </summary>
    public int BoneCount(EntityHandle entity, out int cached)
    {
        cached = Skeletons?.CachedBoneCount(entity) ?? 0;

        return E.Models.For(entity).BoneCount;
    }
    public StandaloneBones? BoneMatrices(EntityHandle entity)
        => Skeletons?.BoneMatrices(entity);

    public bool IsPoseable(EntityHandle entity)
        => Skeletons?.IsPoseable(entity) ?? false;

    public string StudioName(EntityHandle entity)
        => Skeletons?.StudioName(entity) ?? string.Empty;
    public int PoseParameterCount(EntityHandle entity)
        => Client?.PoseParameterCount(entity) ?? 0;
    public int LookupPoseParameter(EntityHandle entity, string name)
        => Client?.LookupPoseParameter(entity, name) ?? -1;
    public string PoseParameterName(EntityHandle entity, int parameter)
        => Client?.PoseParameterName(entity, parameter) ?? string.Empty;
    public bool PoseParameterRange(EntityHandle entity, int parameter, out float start, out float end)
    {
        start = 0f;
        end = 0f;
        return Client is { } client && client.PoseParameterRange(entity, parameter, out start, out end);
    }
    public float SetPoseParameter(EntityHandle entity, int parameter, float value)
        => Client?.SetPoseParameter(entity, parameter, value) ?? 0f;
    public float GetPoseParameter(EntityHandle entity, int parameter)
        => Client?.GetPoseParameter(entity, parameter) ?? 0f;
    /// <summary>
    /// Whether the engine has stopped sending this entity — from the mirror the
    /// engine pushes into, not from a poll. plan_crossings.md Phase X3.
    /// </summary>
    public bool IsDormant(EntityHandle entity)
        => Mirrored ? _mirror.IsDormant(entity) : EngineIsDormant(entity);

    /// <summary>Whether the engine stopped sending this entity, asked of the client's slot.</summary>
    private partial bool EngineIsDormant(EntityHandle entity);
    public int SequenceEventCount(EntityHandle entity, int sequence)
        => E.Models.For(entity).EventsOf(sequence).Count;
    /// <summary>
    /// Where the animation has got to — computed from managed's own record,
    /// with the cycle arithmetic <c>C_StandaloneEntity::SequenceCycle</c> uses
    /// and the clock <see cref="EngineSide.CurrentTime"/> LOADS out of the engine's
    /// CGlobalVars (Phase X1), so this whole call is now zero crossings.
    ///
    /// <para>
    /// Answers true for a live entity and false for a handle the mirror does
    /// not know, which is what the slot answers for a handle that resolves to
    /// nothing — and what the entity's own creation and removal pushes make
    /// true of the mirror.
    /// </para>
    /// </summary>
    public bool SequenceState(EntityHandle entity, out int sequence, out float cycle, out float duration, out bool looping, out int starts)
    {
        if (!Mirrored)
        {
            if (Client is { } client)
                return client.SequenceState(entity, out sequence, out cycle, out duration, out looping, out starts);

            sequence = 0;
            cycle = 0f;
            duration = 0f;
            looping = false;
            starts = 0;
            return false;
        }

        SequenceRecord record = _mirror.Sequence(entity);

        sequence = record.Sequence;
        cycle = record.Cycle(E.CurrentTime);
        duration = record.Duration;
        looping = record.Looping;
        starts = record.Starts;

        return true;
    }
    public int ProceduralBones(EntityHandle entity, out int computed, out int jiggle, out int ikChains)
    {
        computed = 0;
        jiggle = 0;
        ikChains = 0;
        return Client?.ProceduralBones(entity, out computed, out jiggle, out ikChains) ?? 0;
    }
    public void SetAnimLayer(EntityHandle entity, int slot, int sequence, float weight, float playbackRate, int order, bool restart)
        => Client?.SetAnimLayer(entity, slot, sequence, weight, playbackRate, order, restart);
    public int AnimLayer(EntityHandle entity, int slot, out int sequence, out float weight, out int order, out float cycle)
    {
        sequence = -1;
        weight = 0f;
        order = 0;
        cycle = 0f;
        return Client?.AnimLayer(entity, slot, out sequence, out weight, out order, out cycle) ?? 0;
    }
    /// <summary>
    /// One event of one sequence. From the descriptor, so the E+1 crossings a
    /// single model check used to cost are one.
    /// </summary>
    public bool SequenceEvent(EntityHandle entity, int sequence, int index, out float cycle, out int eventId, out int type, out string name, out string options)
    {
        cycle = 0f;
        eventId = 0;
        type = 0;
        name = string.Empty;
        options = string.Empty;

        IReadOnlyList<ModelSequenceEvent> events = E.Models.For(entity).EventsOf(sequence);

        if (index < 0 || index >= events.Count)
            return false;

        ModelSequenceEvent e = events[index];

        cycle = e.Cycle;
        eventId = e.Id;
        type = e.Type;
        name = e.Name;
        options = e.Options;

        return true;
    }

    public void SetModelScale(EntityHandle entity, float scale)
    {
        // The decision is the skeleton's; what native keeps is the number its
        // player movement reads (CStandaloneEntity::GetModelScale), stored
        // only when the skeleton took it.
        if (Skeletons is not { } skeletons || skeletons.SetModelScale(entity, scale) == SkeletonScaleChange.Changed)
            StandaloneSetModelScale(entity, scale);
    }

    public void SetBoneMergeSource(EntityHandle entity, int sourceEntityIndex)
        => Client?.SetBoneMergeSource(entity, sourceEntityIndex);
    public void SetSequence(EntityHandle entity, int sequence)
    {
        // PlaySequence's fixed shape: from the start, at rate 1. A managed
        // skeleton answers the whole of it; with no skeleton composed yet there
        // is nothing to play the sequence on (IShimModelsServer's native slot
        // behind this only warns).
        if (Skeletons is { } skeletons && sequence >= 0)
            skeletons.SetSequence(entity, sequence, 1.0f, restart: true);
    }
    /// <summary>
    /// Play a sequence — and record it, because managed is its only author.
    ///
    /// <para>
    /// <b>The native write stays.</b> The shell's own render path reads
    /// <c>m_nSequence</c> to pick the studio sequence it draws, so the storage
    /// is C++'s regardless; what Phase X2 moves is the AUTHORITY. This is a
    /// write-through: one writer, two copies, fed from the same call.
    /// </para>
    ///
    /// <para>
    /// The read-back is the ONLY crossing this phase adds, it happens only when
    /// the sequence actually moved, and it fetches only what managed cannot
    /// know — the duration and loop flag out of the model's studio header. A11
    /// measured the write rate at 0.051 a frame against the 51.000-a-frame poll
    /// it replaces.
    /// </para>
    /// </summary>
    public void PlaySequence(EntityHandle entity, int sequence, float playbackRate, bool restart)
    {
        Skeletons?.SetSequence(entity, sequence, playbackRate, restart);

        if (!Recording)
            return;

        SequenceRecord before = _mirror.Sequence(entity);
        SequenceRecord after = _mirror.NoteSequence(entity, sequence, playbackRate, restart, E.CurrentTime);

        // Only a real change consults the model. The early-return path kept the
        // clock, so it kept the duration that clock is measured against too.
        if (after.Starts != before.Starts && Client is { } client
            && client.SequenceInfo(entity, out float duration, out bool looping))
            _mirror.NoteSequenceInfo(entity, duration, looping);
    }
    // No engine answer since plan_server_native_v2 T2: m_nTextureFrameIndex left with CBaseEntity; env_texturetoggle asks.
    public void SetTextureFrame(EntityHandle entity, int frame) { }
    // No engine answer since plan_server_native_v2 T2: the port is GetArea against the managed sky cameras (env_smokestack asks).
    public bool DetectInSkybox(EntityHandle entity) => false;

    /// <summary>
    /// How big this instance wears its model, as a multiplier on the size the
    /// artist built it at. Answered by BOTH sides.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client's copy is what the player sees. The server's is what its own
    /// skeleton is built with, so attachments on a scaled model land where the
    /// artist put them, and it is what the scaled-collision rule reads when it
    /// decides whether a trace may use the solver's scaled hull.
    /// </para>
    /// <para>
    /// Zero and below are REFUSED natively rather than clamped, and say so in
    /// the console: a scale of zero collapses every bone onto the entity
    /// origin, which is a thing that is still in the world and still solid to
    /// the entity list but draws as nothing and traces as nothing.
    /// </para>
    /// </remarks>
    private void StandaloneSetModelScale(EntityHandle entity, float scale)
    {
        _modelsArea.StandaloneSetModelScale(entity.Value, scale);
    }
}

/// <summary>The real engine, for <see cref="IEngineDamage"/>. Every member is one call.</summary>
internal sealed unsafe partial class EngineDamage : IEngineDamage
{
    // No engine answer since plan_server_native_v2 T2: CBaseCombatCharacter's server-ragdoll flag left with it (trigger_serverragdoll asks).
    public void SetForceServerRagdoll(EntityHandle entity, bool force) { }
    // No engine answer since plan_server_native_v2 T2: the take-damage mode left with CBaseEntity; every entity answers DAMAGE_YES (trigger_camera asks).
    public TakeDamageMode SetTakeDamageMode(EntityHandle entity, TakeDamageMode mode) => TakeDamageMode.Yes;
}

/// <summary>The real engine, for <see cref="IEngineCollisionRules"/>.</summary>
internal sealed unsafe partial class EngineCollisionRules : IEngineCollisionRules
{
    private readonly ICollisionArea _collisionArea;

    public EngineCollisionRules(ICollisionArea collisionArea)
    {
        _collisionArea = collisionArea ?? throw new ArgumentNullException(nameof(collisionArea));
    }

    /// <summary>
    /// The collision matrix. Registered from both sides; answers whether this
    /// side's native received it, so a side that cannot serve the slot says so
    /// instead of keeping native's defaults in silence (which the client did
    /// until the slot moved into the shared IShimCollision base).
    /// </summary>
    public bool RegisterCollisionRules(uint[] rows)
    {
        var blob = default(CollisionRulesBlob);
        blob.SetRows(rows);

        _collisionArea.RegisterCollisionRules(&blob);
        return true;
    }
}

/// <summary>The real engine, for <see cref="IEngineGameRulesPolicy"/>.</summary>
internal sealed unsafe partial class EngineGameRulesPolicy : IEngineGameRulesPolicy
{
}

/// <summary>The real engine, for <see cref="IEngineBrushMovement"/>. Every member is one call.</summary>
internal sealed unsafe partial class EngineBrushMovement : IEngineBrushMovement
{
    // plan_engineapi_vtables B2, plan_abi_hotreload D8: the side this binding
    // answers for, always. Every container that builds one passes it: a side's
    // own, and that side's transitional defaults (EngineSide.Defaults).
    private readonly EngineSide _side;

    private EngineSide E => _side;

    // No engine answer since plan_server_native_v2 T2: retired mover: EntityMotion implements this interface over the managed angular mover, so nothing reaches the engine binding's.
    public void MoveToAngles(EntityHandle entity, Vector destination, float speed) { }
    // No engine answer since plan_server_native_v2 T2: retired mover: EntityMotion implements this interface over the managed linear mover, so nothing reaches the engine binding's.
    public void MoveToPosition(EntityHandle entity, Vector destination, float speed) { }
    // No engine answer since plan_server_native_v2 T2: retired mover: EntityMotion implements this interface, so nothing reaches the engine binding's.
    public void StopMoving(EntityHandle entity) { }
}

/// <summary>The real engine, for <see cref="IEnginePlayers"/>. Every member is one call.</summary>
internal sealed unsafe partial class EnginePlayers : IEnginePlayers
{
    // plan_engineapi_vtables B2, plan_abi_hotreload D8: the side this binding
    // answers for, always. Every container that builds one passes it: a side's
    // own, and that side's transitional defaults (EngineSide.Defaults).
    private readonly EngineSide _side;

    private EngineSide E => _side;

    // No engine answer since plan_server_native_v2 T2: the engine's frag counter left with CBasePlayer; a managed score store is the port (game_score asks).
    public void AddScore(EntityHandle player, int points, bool allowNegative) { }

    // No engine answer since plan_server_native_v2 T2: redeploying is the Arsenal's to do and it does not yet (player_speedmod asks).
    public bool DeployLastWeapon(EntityHandle player) => false;
    // No engine answer since plan_server_native_v2 T2: only the Arsenal's players carry weapons, and ArsenalPlayers drops theirs.
    public bool DropActiveWeapon(EntityHandle player, Vector velocity) => false;
    // No engine answer since plan_server_native_v2 T2: a player outside the Arsenal holds no weapon the engine knows of; ArsenalPlayers answers its own.
    public string GetActiveWeapon(EntityHandle player) => string.Empty;
    // No engine answer since plan_server_native_v2 T2: the active weapon is the Arsenal's to name and it does not yet (trigger_camera, WeaponInventory ask).
    public EntityHandle GetActiveWeaponEntity(EntityHandle player) => EntityHandle.None;

    // No engine answer since plan_server_native_v2 T2: weapon slots are the Arsenal's to name and it does not yet (WeaponInventory asks).
    public EntityHandle GetWeaponEntity(EntityHandle player, int slot) => EntityHandle.None;

    // No engine answer since plan_server_native_v2 T2: the view model is the Arsenal's to name and it does not yet.
    public EntityHandle GetViewModelEntity(EntityHandle player) => EntityHandle.None;
    public int GetUserId(EntityHandle player) => E.EntityIsValid(player) ? EngineBound.Server(E).PlayerUserId(player.EntryIndex) : 0;
    // No engine answer since plan_server_native_v2 T2: ammo is the Arsenal's; ArsenalPlayers gives it to its players.
    public int GiveAmmo(EntityHandle player, int count, string ammoName) => 0;
    // No engine answer since plan_server_native_v2 T2: items are the Arsenal's; ArsenalPlayers gives them to its players.
    public bool GiveItem(EntityHandle player, string className) => false;
    // No engine answer since plan_server_native_v2 T2: holstering is the Arsenal's to do and it does not yet (player_speedmod asks; docs/todo.md).
    public bool HolsterActiveWeapon(EntityHandle player) => false;
    public bool BoxInPvs(EntityHandle player, Vector mins, Vector maxs) => E.EntityIsValid(player) && EngineBound.Server(E).BoxInPvs(EyePosition(player), mins, maxs);

    /// <summary>Where the player's eyes are, as the server's slot answers it.</summary>
    private partial Vector EyePosition(EntityHandle player);
    // No engine answer since plan_server_native_v2 T2: only the Arsenal's players carry items, and ArsenalPlayers strips theirs.
    public void RemoveAllItems(EntityHandle player, bool stripSuit) { }
    // No engine answer since plan_server_native_v2 T2: CBasePlayer::SetFOV left with CBasePlayer; env_zoom asks.
    public void SetFov(EntityHandle player, EntityHandle requester, int fov, float rate = 0f) { }
    public void SetViewEntity(EntityHandle player, EntityHandle view)
    {
        if (E.EntityIsValid(player))
            EngineBound.Server(E).SetView(player.EntryIndex, E.EntityIsValid(view) ? view.EntryIndex : -1);
    }

    // No engine answer since plan_server_native_v2 T2: there is no SDK player
    // on the server, so there are no SDK ears; the standalone arm above answers.
    public bool TrySdkPlayerEarPosition(int edictIndex, out Vector ears)
    {
        ears = default;
        return false;
    }
}

/// <summary>The real engine, for <see cref="IEnginePlayerMovement"/>. Every member is one call.</summary>
internal sealed unsafe partial class EnginePlayerMovement : IEnginePlayerMovement
{
    // plan_engineapi_vtables B2, plan_abi_hotreload D8: the side this binding
    // answers for, always. Every container that builds one passes it: a side's
    // own, and that side's transitional defaults (EngineSide.Defaults).
    private readonly EngineSide _side;

    private EngineSide E => _side;

    private readonly IMovementArea _movementArea;

    public void ClearMovementConstraint(EntityHandle player) => E.PlayerClearMovementConstraint(player);
    // No engine answer since plan_server_native_v2 T2: forcing buttons on the player's command path is not ported (trigger_playermovement, trigger_camera ask).
    public void ForceButtons(EntityHandle player, InputButtons buttons, bool forced) { }
    // No engine answer since plan_server_native_v2 T2: the player's held buttons are not read back (trigger_camera asks).
    public InputButtons GetButtons(EntityHandle player) => default;

    /// <summary>
    /// Hands the movement parameters to the native loop. Called once per side
    /// at startup — see <see cref="MovementParams"/> for why both.
    /// </summary>
    public void RegisterParams(MovementParams parameters)
    {
        _movementArea.RegisterMovementParams(&parameters);
    }

    // No engine answer since plan_server_native_v2 T2: auto-movement suppression is not ported (trigger_playermovement asks).
    public void SetAllowAutoMovement(EntityHandle player, bool allow) { }
    // No engine answer since plan_server_native_v2 T2: disabling buttons on the player's command path is not ported (player_speedmod asks).
    public void SetButtonsDisabled(EntityHandle player, InputButtons buttons, bool disabled) { }
    // No engine answer since plan_server_native_v2 T2: freezing a player is not ported (trigger_camera asks).
    public void SetFrozen(EntityHandle player, bool frozen) { }
    // No engine answer since plan_server_native_v2 T2: a lagged-movement factor on the player's predicted state is player_speedmod's ask and is not ported.
    public void SetLaggedMovement(EntityHandle player, float value) { }
    // No engine answer since plan_server_native_v2 T2: the movement constraint sphere is not ported (point_playermoveconstraint asks).
    public void SetMovementConstraint(EntityHandle player, Vector centre, float radius, float width, float speedFactor) { }
}

/// <summary>The real engine, for <see cref="IEngineWeapons"/>. Every member is one call.</summary>
internal sealed unsafe partial class EngineWeapons : IEngineWeapons
{
}

/// <summary>The real engine, for <see cref="IEngineDiagnostics"/>. Every member is one call.</summary>
internal sealed unsafe partial class EngineDiagnostics : IEngineDiagnostics
{
}

/// <summary>The real engine, for <see cref="IEngineHud"/>. Every member is one call.</summary>
internal sealed unsafe partial class EngineHud : IEngineHud
{
}

/// <summary>The real engine, for <see cref="IEngineWorldQuery"/>. Every member is one call.</summary>
internal sealed unsafe partial class EngineWorldQuery : IEngineWorldQuery
{
    // plan_engineapi_vtables B2, plan_abi_hotreload D8: the side this binding
    // answers for, always. Every container that builds one passes it: a side's
    // own, and that side's transitional defaults (EngineSide.Defaults).
    private readonly EngineSide _side;

    private EngineSide E => _side;

    public int GetArea(Vector origin) => EngineBound.Server(E).GetArea(origin);
    public Contents PointContents(Vector origin) => (Contents)EngineBound.Trace(E).PointContents(origin);
    // Through IPhysicsSurfaceProps' forwarders since 2026-10-19, where a slot made
    // the same call: the guard is its.
    public SurfaceMaterial SurfacePropGameMaterial(int surfaceProp)
        => surfaceProp < 0 ? 0 : (SurfaceMaterial)EngineBound.SurfaceProps(E).GameMaterial(surfaceProp);
}

/// <summary>The real engine, for <see cref="IEngineSound"/>. Every member is one call.</summary>
internal sealed unsafe partial class EngineSound : IEngineSound
{
    // plan_engineapi_vtables B2, plan_abi_hotreload D8: the side this binding
    // answers for, always. Every container that builds one passes it: a side's
    // own, and that side's transitional defaults (EngineSide.Defaults).
    private readonly EngineSide _side;

    private EngineSide E => _side;

    /// <summary>
    /// Emit one sound patch at the given envelope values — plan_gamedll_after.md
    /// Phase A3, second pair.
    ///
    /// <para>
    /// <paramref name="volume01"/> is the ENVELOPE's value and not what reaches
    /// the engine: the shim multiplies it by the volume the sound script chose,
    /// which it read once at create time. A patch's ramp is a MULTIPLIER on a
    /// script's number, and only one side of this boundary has ever seen that
    /// number.
    /// </para>
    /// </summary>
    public void SoundPatchEmit(int patch, float volume01, float pitch, int flags, float startTime, bool emitCaption)
    {
        SidePatchEmit(patch, volume01, pitch, flags, startTime, emitCaption);
    }

    /// <summary>
    /// Stop one sound patch, if it is playing. Idempotent, because the shim
    /// tracks whether it ever started — stopping a sound that never played is a
    /// warning from the engine and a real one.
    /// </summary>
    public void SoundPatchStop(int patch)
    {
        SidePatchStop(patch);
    }

    /// <summary>
    /// The two questions the frame pass asks of every live patch, in one
    /// crossing.
    /// </summary>
    /// <param name="entIndex">
    /// The entity the patch rides, or negative when it has gone — which is what
    /// makes the caller drop it.
    /// </param>
    /// <param name="filterActive">
    /// Whether anybody is still listening. A patch with no recipients is KEPT
    /// and not emitted, which is stock's behaviour and not the same thing as
    /// having lost its entity.
    /// </param>
    public void SoundPatchState(int patch, out int entIndex, out bool filterActive)
    {
        entIndex = -1;
        filterActive = false;

        SidePatchState(patch, out entIndex, out filterActive);
    }

    /// <summary>
    /// Free the native patch. The one direction <c>SoundDestroy</c> does not
    /// cover: a queued DESTROY command comes due inside the managed frame pass,
    /// so the decision is managed and the delete is not.
    /// </summary>
    public void SoundPatchFree(int patch)
    {
        SidePatchFree(patch);
    }

    // Through ISoundEmitterSystemBase's forwarders since 2026-10-19, where the
    // SoundScriptParameters slot made the same calls on both sides.
    public SoundScriptParams? SoundScriptParameters(string soundName)
        => !string.IsNullOrEmpty(soundName) ? EngineBound.SoundEmitter(E).ScriptParameters(soundName) : null;

    /// <summary>
    /// A sound patch on the entity, heard from where it stands
    /// (SoundCreate with a CPASAttenuationFilter): the handle the managed
    /// sound controller tracks it by, or 0. Played, ramped and destroyed
    /// through the controller like any other patch.
    /// </summary>
    public int SoundPatchCreate(EntityHandle entity, int channel, string soundName, int soundLevel)
    {
        if (soundName.Length == 0) return 0;

        return SidePatchCreate(entity, channel, soundName, soundLevel);
    }

    // Through IPhysicsSurfaceProps' forwarders since 2026-10-19, where the
    // SurfaceScrapeSound slot read the same two names.
    public string? SurfaceScrapeSound(int surfaceProps, bool smooth)
        => EngineBound.SurfaceProps(E).ScrapeSound(surfaceProps, smooth);

    // Each side's own patch arm (plan_server_native_v2 T4a): the server's
    // patches are the managed SoundEmitter's route, the client's its
    // IShimSoundscapeClient's slots (each binding half).
    private partial void SidePatchEmit(int patch, float volume01, float pitch, int flags, float startTime, bool emitCaption);
    private partial void SidePatchStop(int patch);
    private partial void SidePatchState(int patch, out int entIndex, out bool filterActive);
    private partial void SidePatchFree(int patch);
    private partial int SidePatchCreate(EntityHandle entity, int channel, string soundName, int soundLevel);
}

/// <summary>The real engine, for <see cref="IEngineEffects"/>. Every member is one call.</summary>
internal sealed unsafe partial class EngineEffects : IEngineEffects
{
    private readonly IMovementArea _movementArea;

    //-------------------------------------------------------------------------
    // TEMP ENTITIES (plan_tmp.md Part 3).
    //-------------------------------------------------------------------------

    /// <summary>
    /// No side registers a temp entity through here any more: the server plans
    /// its tables in C# (plan_server_native_v2 T4b) and so does the client
    /// (ClientTempEntTables over IShimTempEntBackendsClient's TempEntBuildClient).
    /// -1, "this side does not register through here".
    /// </summary>
    public int TempEntRegister(TeClass description) => -1;

    /// <summary>
    /// <c>GetCurrentGravity()</c>. A slot rather than a constant because
    /// <c>sv_gravity</c> is a convar: a tempent falling at 600 while the world
    /// falls at 800 looks almost right, which is the worst kind of wrong for
    /// something nobody watches closely.
    /// </summary>
    public float CurrentGravity()
        => _movementArea.CurrentGravity();
}

/// <summary>The real engine, for <see cref="IEngineVisibility"/>. Every member is one call.</summary>
internal sealed unsafe partial class EngineVisibility : IEngineVisibility
{
    // plan_engineapi_vtables B2, plan_abi_hotreload D8: the side this binding
    // answers for, always. Every container that builds one passes it: a side's
    // own, and that side's transitional defaults (EngineSide.Defaults).
    private readonly EngineSide _side;

    private EngineSide E => _side;
    public void SetLightStyle(int style, string pattern)
    {
        // The engine's 64 styles (MAX_LIGHTSTYLES); the slot's guard, moved here.
        if (style >= 0 && style < 64 && pattern is not null)
            EngineBound.Server(E).LightStyle(style, pattern);
    }

    // No engine answer since plan_server_native_v2 T2: there is no SDK player
    // on the server to have a skybox area; the standalone arm below answers.
    public bool TrySdkPlayerSkyboxArea(int edictIndex, out int area)
    {
        area = 0;
        return false;
    }
}

