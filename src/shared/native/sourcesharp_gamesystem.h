#pragma once

#ifdef CLIENT_DLL

//=============================================================================
// THE CLIENT'S HALF, AND IT IS THE SERVER'S SHAPE WITH THE SERVER'S NAMES.
//
// This arm used to be four lines: an `IClientRuntimeAdapter` with no methods
// and a `LoadManagedRuntime()` that was declared and never defined anywhere in
// the tree. The client DID reach managed code -- CShellClientDLL::Init called
// SourceSharp_ClientEarlyInit(), which called a file-static game system's
// Init(), which called SourceSharp_LoadManagedSide() -- but it reached it by a
// route with no interface, no named owner and no partner for the release. The
// server's DLLInit says `m_pManagedRuntime = LoadManagedRuntime( this )` and
// its DLLShutdown says `UnloadManagedRuntime( m_pManagedRuntime )`; the client
// said neither, and the asymmetry was the point of the divergence rather than
// an accident of it.
//
// Both names below are the server's, spelled the same way, in the same order,
// with the same split of responsibility:
//
//   IManagedRuntimeEnvironment  what the SHELL provides. The shell owns Init,
//                               so the shell owns the factory and the globals,
//                               and the loader asks IT rather than fetching
//                               its own. CShellClientDLL implements it exactly
//                               as CShellGameDll does.
//
//   IClientRuntimeAdapter       what the LOADER hands back: the adapter, one
//                               forward per method and no cached table. The
//                               mirror of IManagedRuntimeServer.
//
// THE ONE DIVERGENCE IN THIS FILE, STATED. On the server the environment's
// GetGlobalVars() returns `CGlobalVars &`; here it returns `CGlobalVarsBase &`,
// because that is the type IBaseClientDLL::Init is handed and the client's own
// `gpGlobals` is declared as. It is the base of the server's type, not a
// different one, and the client genuinely does not have the derived fields.
//
//=============================================================================
// OWNERSHIP -- WHO BRINGS THE CLR UP, AND WHO IS ALLOWED TO PUT IT DOWN.
//
// A LISTEN SERVER LOADS BOTH DLLs INTO ONE PROCESS, so both of these arms run
// at once and each calls SourceSharp_Acquire(). That is correct and it is not a
// double initialisation:
//
//   * SourceSharp_Acquire/Release live in libsourcesharp.so, ONE shared object
//     for the process, and hold ONE refcount over ONE CoreCLR
//     (sourcesharp/host/sourcesharp.cpp -- "runtime already running, refcount
//     now 2 (listen server)"). Whichever side is second joins the CLR the
//     first side started; whichever side releases first leaves it up for the
//     other.  Verified by `nm`: SourceSharp_Acquire is UNDEFINED in both
//     client.so and server.so, so there is no second copy of the counter.
//
//   * SourceSharp_LoadManagedSide/UnloadManagedSide are compiled into BOTH
//     .so files and each gets its own g_SourceSharpInitGate. That is what
//     makes them per-side rather than per-process, and each calls its own
//     side's host exports (SourceSharp_LoadServer/LoadClient, ...).
//
// So the rule this arm states, and the rule the server arm has always had:
//
//   ONE LOAD AND ONE RELEASE PER DLL. LoadManagedRuntime is called exactly once
//   per DLL, from that DLL's Init, and UnloadManagedRuntime exactly once from
//   its Shutdown. Neither side counts the other, neither side waits for the
//   other, and the refcount in the host is the only place the two meet.
//
// The gate makes a second call a no-op, so this is a rule about WHO rather than
// about what would crash -- but the release is not idempotent in the same way,
// and a side that released a runtime it had not loaded would decrement a count
// it did not own. That is why the client's release is now a NAMED PARTNER of a
// named load instead of a Shutdown() on a game-system row nobody owns.
//=============================================================================

class CGlobalVarsBase;

class IManagedRuntimeEnvironment
{
protected:
    virtual ~IManagedRuntimeEnvironment() {};

public:
    virtual void *GetEngineInterface(const char *pName, int *pReturnCode) = 0;
    virtual CGlobalVarsBase &GetGlobalVars() = 0;
};

//-----------------------------------------------------------------------------
// THE ADAPTER -- the mirror of IManagedRuntimeServer, and it has FOUR methods
// rather than fifty because the client calls four things.
//
// Those four are not a subset chosen for convenience: they are exactly the
// managed slots client.so reads, measured rather than assumed. `Frame`,
// `LevelInit` and `LevelShutdown` are read by CSourceSharpSystem in
// sourcesharp_gamesystem.cpp and by nothing else on the client, and `Shutdown`
// is read by SourceSharp_UnloadManagedSide. Every other slot the client uses is
// reached from managed code calling IN, not from native calling out.
//
// OnSystemStop IS THE LOADER'S PARTNER AND NOT A LIFECYCLE SLOT, exactly as it
// is on the server: it invokes the managed `Shutdown` slot and then
// UnloadSide + Release. The server's comment on the same method applies here
// word for word, including "the CLR is refcounted across client.so and
// server.so, and this is the client's release".
//
// There is NO OnSystemStart. The server's calls the `ServerSystemStart` slot;
// the client has no `ClientSystemStart` in the ABI and this lane does not
// request one, because there is nothing for it to do -- the client's start IS
// LoadManagedRuntime, and inventing a slot so the two sides have the same
// method count would be symmetry of shape over symmetry of behaviour. STATED
// DIVERGENCE, and the reason is written here rather than left to be noticed.
//-----------------------------------------------------------------------------
// plan_client_vtables CD: renamed from IManagedRuntimeClient, which is now the
// MANAGED-BUILT runtime (imanaged_runtime_client.h); this native adapter goes
// at CE1, as the server's did after D5.
class IClientRuntimeAdapter
{
protected:
    virtual ~IClientRuntimeAdapter() {};

public:
    virtual void OnSystemStop() = 0;

    virtual void OnLevelInit( const char *pMapName ) = 0;
    virtual void OnLevelShutdown() = 0;

    virtual void OnFrame( float flFrameTime ) = 0;
};

IClientRuntimeAdapter *LoadManagedRuntime( IManagedRuntimeEnvironment *env );

// The partner. Null-safe, so a Shutdown after a failed Init needs no second
// guard -- the server's UnloadManagedRuntime says the same and for the same
// reason. It cannot be `delete`d at the call site: IClientRuntimeAdapter's
// destructor is protected, deliberately, which is the interface saying the
// caller does not own the object, so the concrete type stays inside
// managed_runtime_client.cpp.
void UnloadManagedRuntime( IClientRuntimeAdapter *pRuntime );

// Handing the adapter to the game-system row that drives the three per-level
// and per-frame forwards.
//
// The row is CSourceSharpSystem in sourcesharp_gamesystem.cpp, which is where
// it has to stay: kClientSystems[] walks it in a position a live census
// measured, and that ordering is not this change's to move. So the shell loads
// the runtime and then hands the row the pointer, which is the same
// relationship CShellGameDll has to kServerSystems[] -- the table row does no
// load of its own precisely because the shell already did it.
//
// The row clears its own copy in its Shutdown(), which the kClientSystems[]
// walk reaches BEFORE CShellClientDLL::Shutdown releases the adapter -- so the
// row cannot forward through a freed object, and it cannot do so because of an
// ordering nobody wrote down.
void SourceSharp_SetClientRuntime( IClientRuntimeAdapter *pRuntime );

#else

#include "sourcesharp.h"   // SourceSharpMapLoad, the map loads' flat argument

class CGlobalVars;

class IManagedRuntimeEnvironment
{
protected:
    virtual ~IManagedRuntimeEnvironment() {};

public:
    virtual void *GetEngineInterface(const char *pName, int *pReturnCode) = 0;
    virtual CGlobalVars &GetGlobalVars() = 0;

    // TODOS #15 AND #16, CLOSED BY SUBTRACTION: THE MEANS ALREADY EXISTS AND IT
    // IS NOT A METHOD ON THIS INTERFACE. Both asked for an "add" here; both are
    // refused, and the refusal is written down because deleting the comments
    // would take the question with them (docs/gamedll-ledger.md, Phase 9).
    //
    // A SERVER CLASS ADDS ITSELF, AND ITS ORDER IS THE WIRE FORMAT. A
    // ServerClass links itself into g_pServerClassHead from its own constructor
    // and does so ALPHABETICALLY (public/server_class.h:35-51); CClientClass
    // does exactly the same into g_pClientClassHead. The engine assigns class
    // ids by walking the server's list in order, and in the ordinary case where
    // it does not send send-tables the client rebuilds those ids by walking ITS
    // list in order. The two agree only because both sort by one rule. An Add()
    // on this interface would be a SECOND registration route into a list whose
    // order is load-bearing, and the first thing anyone would use it for -- put
    // the managed classes at the front -- renumbers every class on one side
    // only, silently, with every entity then decoding as a different class.
    //
    // And the need it names is already met by the route that exists: the shell
    // prints "server classes: N from managed, M from g_pServerClassHead" at
    // DLLInit, and today that reads 50 from managed against 7 from stock. C# is
    // already the majority registrar with no method here.
    //
    // SEND PROXIES ARE A TABLE COMPARED BY IDENTITY, not a list to append to.
    // CStandardSendProxies holds the function pointers the ENGINE matches
    // against to recognise the standard prop encodings, so what matters is the
    // pointers inside it. CShellGameDll::SeedNetworkingPointers copies the
    // struct by value into a member it owns, which is what lets
    // g_StandardSendProxies be deleted the day sourcesharp's own dt_send.cpp is
    // the only thing that fills it -- again with nothing added here.
};

class MapLoadInfo
{
public:
    const char *m_szName = {};
    const char *m_szEntities = {};
    const char *m_szOldName = {};
    const char *m_szLandMark = {};
};

// IMPLEMENTED BY MANAGED (plan_engineapi_vtables D5): ManagedVtableGenerator
// writes this table and native calls it, so the chain kind's rules hold here --
// NO VIRTUAL DESTRUCTOR (lifetime is Release, the last slot), and every slot
// blittable: the map loads take the ABI's flat SourceSharpMapLoad, not the
// MapLoadInfo class by value. The stop is not a slot: it unloads the managed
// side, which no managed method can do to itself (DLLShutdown calls
// SourceSharp_UnloadManagedSide directly).
//
// THE CLASS IS GENERATED (plan_managed_vtable_reload.md L0c): tools/managed-areas
// writes IManagedRuntimeServer into imanaged_runtime.h from
// docs/managed-interfaces.tsv, slot for slot what was declared here (the
// layouts tools/vtable-layout walks from it are identical). Its methods, in
// order: the lifetime (system start, game start/post-start/stop), the four map
// loads and the unload, server activate, the frame and pre-client update,
// think, the string tables, the tick interval, the five listing answers, the
// cvar-query result, hibernation, the status lines, the mdl cache, the Steam
// game server's two edges, the pre-entity pair, the world's keyvalues and the
// game description -- then Release.
#include "imanaged_runtime.h"

IManagedRuntimeServer *LoadManagedRuntime(IManagedRuntimeEnvironment *env);

// THIS LOAD'S RUNTIME. Managed builds it and every load (and cs_sv_reload)
// acquires it again through the managed root; it is released before the side
// unloads. So the shell keeps no pointer to it and asks here at each call:
// a pointer kept across a reload would name the previous build's object.
// NULL outside a load. Held by the shim root (shell_internalfactory.cpp).
IManagedRuntimeServer *ManagedRuntimeServer();

// Unloads the managed side: the runtime and the root are released first.
// DLLShutdown's stop, which is not a runtime slot (sourcesharp_gamesystem.cpp).
void SourceSharp_UnloadManagedSide( void );

// THE ONE SERVICE POINT FOR A HOT RELOAD, and the shell's GameFrame is where it
// is called from (plan_gamedll.md Part 3b point 1).
//
// cs_sv_reload unloads the managed assemblies and loads them again with the
// game running. Between the unload and the successful load the managed table is
// memset and SourceSharp_GetManagedApi() answers NULL, and the shell is the one
// caller in the tree that cannot degrade to stock -- CServerGameDLL is guarded
// out from Phase 4, so there is nothing behind it. So the dead window is made
// unobservable rather than tested for: the request is deferred to the TOP of a
// frame, and inside that call no other engine->shell entry can arrive.
//
// Declared here rather than reached through a second header because Part 2's
// rule for the shell is that it includes ONE header and knows nothing about
// g_ManagedApi or the ABI version. This is that header.
void SourceSharp_ServiceReloadRequest(void);

// Deletes the C#-declared ConCommands and ConVars and their names. Only after
// the engine has unregistered this DLL's commands (ConVar_Unregister in
// DLLShutdown): until then its ICvar points at them.
void SourceSharp_ReleaseConsoleObjects(void);

#endif
