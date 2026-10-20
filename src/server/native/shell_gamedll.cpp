//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
//
// P5 lane R: on the R5 include set. cbase.h and gameinterface.h are gone:
// nothing here used them (every function compiled identical without them);
// every name it uses comes from the public/ and server.so headers below.
#include "tier_connect.h"

#include "eiface.h"

// Never compiled before this phase, so these were never checked: the headers
// are tier1/tier1.h, tier2/tier2.h, tier3/tier3.h -- gameinterface.cpp:79,89
// includes them that way. Part 0's "the file has never been compiled", found
// the moment a $File line named it.
#include "tier1/tier1.h"
#include "tier2/tier2.h"
#include "tier3/tier3.h"

#include "icvar.h"
#include "tier0/icommandline.h"
#include "server_class.h"
#include "dt_send.h"

#include "sourcesharp_gamesystem.h"
#include "shell_managed_areas.h"     // CManagedHolders: the game DLL object owns them (reload L2)
#include "shell_server.h"
#include "shell_savecomment.h"
#include "shell_steporder.h"
#include "shell_gamedll_internal.h"
#include "shell_factory.h"
#include "ss_string.h"
#include "server_state.h"     // m_pDll / m_pLevel (plan_server_native_v2 T6)
#include "shell_shim_root_server.h"  // m_ShimRoot, the bootstrap object managed is handed (T6)

//-----------------------------------------------------------------------------
// THE DELEGATE IS GONE -- plan_gamedll.md T4, closed at Phase 7a.
//
// It existed because Phase 4 flips the exposed interface before any body exists
// and Phases 5, 6 and 7 fill it in: without a way to hand the whole job back to
// stock's CServerGameDLL there was no end-to-end gate for three phases, in a
// project whose recorded lesson is that a server which boots and does nothing
// looks exactly like one that works. It did that job: Phase 7 ended with the
// two paths producing IDENTICAL worlds on dm_lockdown -- 473 entities each,
// 61 classes, zero differing -- which is the strongest statement the A/B could
// make and the statement it was built to make.
//
// PHASE 7a IS WHERE IT HAD TO GO, and not by preference. The delegate path is
// stock's CServerGameDLL::DLLInit, and stock's DLLInit is what called
// IGameSystem::InitAllSystems(). This phase removes that call
// (gameinterface.cpp) and takes igamesystem.cpp off the server's link line, so
// a delegating server would start with NOT ONE game system initialised:
// physcollision NULL -- the exact dereference that segfaulted Phase 6 in
// BuildPropList -- no sound emitter, no string pool, no managed frame. Keeping
// the switch would have kept a mode that crashes on the first physics prop, and
// a broken A/B reference is worse than none because it is still quoted.
//
// So `-shellgamedll` no longer selects anything. The shell IS the game DLL, on
// every path, and there is no second answer for a reader to wonder about.
//-----------------------------------------------------------------------------

// public/server_class.h and public/dt_send.h. The two the engine follows into
// a C++ ABI, and the two Part 5 seeds into members at DLLInit rather than
// answering from directly.
extern ServerClass *g_pServerClassHead;
extern CStandardSendProxies g_StandardSendProxies;

// util.h:190, defined at gameinterface.cpp:257 -- outside the class, so it
// survives Phase 7a. Named here rather than by including util.h, which would
// pull cbase.h into a file that deliberately does not have it.
extern CGlobalVars *gpGlobals;

// precache_model.cpp:108. "Due to dependencies, these are not autogamesystems"
// -- stock's comment, and stock's placement: the init is the first thing after
// the systems walk and the shutdown the first thing before it.


class CShellGameDll : public IServerGameDLL, public IConCommandBaseAccessor, public IManagedRuntimeEnvironment
{
public:
    virtual ~CShellGameDll()
    {
        // plan_managed_vtable_reload.md L2: the managed side's final unload
        // releases everything native holds from managed before the module
        // goes; a holder still set here outlived the module it came from.
        AssertMsg( ManagedHolders_Empty( m_Managed ),
                   "the game DLL object is destroyed holding a managed-built object" );
    }

    bool DLLInit(CreateInterfaceFn engineFactory,
                 CreateInterfaceFn physicsFactory,
                 CreateInterfaceFn fileSystemFactory,
                 CGlobalVars *pGlobals) override
    {
        // THE DLL TIER FIRST (plan_globals.md §2, plan_server_native_v2 T6):
        // everything DLLInit and the managed load reach that is DLL state lives
        // in it, so it exists before any of them runs. DLLShutdown ends it last.
        m_pDll.Begin();

        m_pEngineFactory = engineFactory;
        m_pPhysicsFactory = physicsFactory;
        m_pFileSystemFactory = fileSystemFactory;
        m_pGlobals = pGlobals;

        // NO SWITCH IS READ HERE ANY MORE -- Phase 7a deleted the delegate, so
        // there is nothing for -shellgamedll to select between. The banner is
        // kept and reworded rather than dropped: it is the one line that says
        // WHICH game DLL answered, and the live logs of Phases 4 through 7 are
        // read by grepping for it.
        Msg( "[SourceSharp] shell game DLL: answering from the shell "
             "-- there is no delegate any more (Phase 7a)\n" );

        SourceSharp_ConnectTier1(engineFactory);
        // tier2 and tier3 are off the link (plan_server_novalve.md P3). tier3's
        // one lookup for this DLL, mdlcache, is tier_connect.cpp's; tier2's,
        // g_pFullFileSystem, went with the model sounds cache that read it.
        SourceSharp_ConnectTier3(engineFactory);

        m_pEngine = (IVEngineServer *)engineFactory(INTERFACEVERSION_VENGINESERVER, nullptr);
        m_pCVar = (ICvar *)engineFactory(CVAR_INTERFACE_VERSION, nullptr);

        if (m_pEngine == nullptr || m_pCVar == nullptr)
        {
            return false;
        }

        // EVERY OTHER ENGINE INTERFACE, and until Phase 6 the shell acquired
        // NONE of them. That is not one of Part 9's eight TODOs -- all eight of
        // those are game systems -- and no other part of the plan assigns them
        // either, which is how twenty null pointers survived Phases 4 and 5
        // without anything failing: nothing had loaded a map yet.
        //
        // networkstringtable is the loudest of them. Part 8's very first line
        // is networkstringtable->CreateStringTable, so with -shellgamedll and
        // without this call the first map load is a null dereference before a
        // single entity is considered. filesystem, modelinfo, partition,
        // enginetrace and soundemitterbase are the same story one step later.
        //
        // See shell_engineboundary.cpp. It is stock's own straight-line block
        // and it adds no state and makes no decision.
        if ( !SourceSharp_AcquireEngineInterfaces( engineFactory, physicsFactory,
                                                   fileSystemFactory ) )
        {
            return false;
        }

        // gpGlobals, here rather than only inside LoadManagedRuntime. Every
        // native step LevelInit is made of reads it -- the first one IS
        // gpGlobals->curtime -- and they run whether or not managed code came
        // up. LoadManagedRuntime seeds it too, from the same pointer, and that
        // is a second assignment of one value rather than a second source for
        // it: the engine owns the memory and writes through it every frame.
        gpGlobals = pGlobals;

        // THE GLOBAL, NOT A MEMBER, and this line is the difference between a
        // server that networks and a null dereference on the first change.
        //
        // g_pSharedChangeInfo is defined at gameinterface.cpp:273 -- outside
        // the class, so SOURCESHARP_SHELL_GAMEDLL's guard does not remove it --
        // and stock ASSIGNS it inside CServerGameDLL::DLLInit. Its reader is
        // CBaseEdict::StateChanged (public/edict.h:295), an INLINE on the
        // entity-networking change-tracking path that dereferences it
        // UNCONDITIONALLY:
        //
        //     if ( accessor->GetChangeInfoSerialNumber() ==
        //          g_pSharedChangeInfo->m_iSerialNumber )
        //
        // Every networked variable write reaches that. So with the engine
        // calling this DLLInit instead of stock's, the global stays NULL and
        // the first networked state change is a crash -- one that no link
        // error and no registration count can see, because it is a runtime
        // dereference on a path nothing offline exercises.
        //
        // The shell assigns it ITSELF rather than relying on the delegate
        // above having done so. In delegate mode stock's DLLInit assigns it as
        // a side effect, but that is a crutch Phase 7a removes with the
        // delegate; this line is what still stands afterwards.
        //
        // The class's old m_pSharedChangeInfo member was the wrong subject: it
        // captured the same value into private storage nothing ever read, so
        // wiring it would have changed nothing and deleting it would have
        // looked like the question was answered. It is deleted, and this is
        // the answer. plan_gamedll.md Part 5's last paragraph, corrected.
        g_pSharedChangeInfo = m_pEngine->GetSharedEdictChangeInfo();

        ConVar_Register(FCVAR_GAMEDLL, this);

        // The runtime is managed-built (plan_engineapi_vtables D5) and held by
        // the shim root, not here: every load and reload acquires it afresh,
        // so the shell asks ManagedRuntimeServer() at each call.
        if (LoadManagedRuntime(this) == nullptr)
        {
            return false;
        }

        // TODO #1, CLOSED. The game systems' Init is the FIRST thing
        // OnSystemStart does, in managed's order (ServerSystems.Init,
        // plan_server_native_v2 T4e): CPhysicsHook's Init through
        // IShimLevel::PhysicsInit, then the managed sound emitter system's,
        // then the mod's own start -- which is entitled to find every game
        // system up, physenv's interfaces among them. kServerSystems[] in
        // shell_serversystems.cpp held that order until then.
        //
        // The particle system manager is not among them: it is not a game
        // system, and it is inited in SourceSharp_AcquireEngineInterfaces
        // above, where stock inits it.
        if ( !ManagedRuntimeServer()->OnSystemStart() )
        {
            return false;
        }

        // AFTER OnSystemStart AND NOT BEFORE IT. Both of the things seeded
        // below are filled by managed startup: the standalone tier
        // ServerClasses come from C# type declarations
        // (shell_standalone.cpp's RegisterStandaloneTiers, reached from
        // SourceSharp_NetHierarchyDeclare) and the temp-entity ones from C#
        // [TempEntity] declarations. Seeding first would take a snapshot of a
        // list C# had not finished writing.
        SeedNetworkingPointers();

        // ONCE. GetTickInterval is `const` and the engine asks it exactly once,
        // so this is the only moment it can be taken without a `mutable`
        // member -- and taking it here rather than lazily is also what makes
        // "the tick interval does not move mid-run" a property of the code
        // rather than of nobody having written the setter yet.
        m_flTickInterval = ManagedRuntimeServer()->OnTickInterval();

        return true;
    }

    //-------------------------------------------------------------------------
    // PART 5 -- THE TWO POINTERS THE ENGINE FOLLOWS INTO A C++ ABI.
    //
    // GetAllServerClasses and GetStandardSendProxies cannot cross the managed
    // boundary (Part 2), so the shell answers both from MEMBERS seeded here.
    // Answering `return g_pServerClassHead;` and `return &g_StandardSendProxies;`
    // reads as the tidy one-line version and is the move that never gets to the
    // end state: g_pServerClassHead is a linked list assembled by static
    // constructors at DLL load, which is the thing this port is removing, and a
    // shell that returns it has adopted it. With a member, the day the last SDK
    // IMPLEMENT_SERVERCLASS is evicted is the day this function changes and
    // GetAllServerClasses does not.
    //
    // THE PLAN SAYS "MANAGED REGISTRATIONS FIRST, THEN THE SURVIVING CHAIN
    // APPENDED", AND THAT ORDER WOULD BREAK NETWORKING. Not a style objection --
    // a wire one. A ServerClass links itself into g_pServerClassHead
    // ALPHABETICALLY (public/server_class.h:35-51) and CClientClass does the
    // same into g_pClientClassHead; the engine assigns class IDs by walking the
    // server's list in order and, in the ordinary case where it does not send
    // send-tables, the client rebuilds those IDs by walking ITS list in order.
    // The two agree only because both are sorted by the same rule. Re-linking
    // the server's list so that the managed classes lead would give the same
    // objects different IDs on the two sides, silently, and every entity would
    // decode as a different class.
    //
    // So the member is seeded with the chain AS IT STANDS -- one order, the one
    // both sides sort to -- and what this phase adds is the METER. The two
    // populations are counted separately and printed, which is what Part 5
    // actually wants the seeding for: "how much of the server class list is
    // still stock?" becomes a number at startup rather than an estimate, and
    // when the second one reaches zero the append goes and the member is seeded
    // purely from C#.
    //-------------------------------------------------------------------------
    void SeedNetworkingPointers()
    {
        int nManaged = 0;
        int nStock = 0;

        for ( ServerClass *pClass = g_pServerClassHead; pClass != nullptr; pClass = pClass->m_pNext )
        {
            if ( SourceSharp_IsManagedServerClass( pClass ) )
                nManaged++;
            else
                nStock++;
        }

        m_pServerClasses = g_pServerClassHead;

        // THE METER, and the reason it is printed rather than asserted: this
        // plan does not evict the surviving SDK registrations -- that is the
        // nettable lane's remaining work -- so the second number is still large
        // and will be for some time. Printing it makes it a number going down
        // instead of a global nobody is measuring, and makes a regression that
        // re-adds twenty visible rather than inferred.
        Msg( "[SourceSharp] server classes: %d from managed, %d from g_pServerClassHead\n",
             nManaged, nStock );

        // BY VALUE, and the copy is transparent. CStandardSendProxies is a
        // table of function pointers the ENGINE compares against to recognise
        // the standard prop encodings, so it is the pointers INSIDE the struct
        // whose identity matters, not the struct's address. Copying it lets
        // g_StandardSendProxies become deletable the day sourcesharp's own
        // dt_send.cpp is the only thing that fills it, without this method
        // changing.
        m_SendProxies = g_StandardSendProxies;
        m_pSendProxies = &m_SendProxies;
    }

    void DLLShutdown() override
    {
        // THE TALLY IS GONE, AND THE CHANNEL THE GATE USES IS NOT.
        //
        // There were two readouts of a per-method refusal table: this one, and
        // the cs_sv_refusals console command. Neither had a reader -- no tool,
        // no managed caller, no config in this tree ever ran cs_sv_refusals,
        // and the table's own two readouts were the only thing that read the
        // table.
        //
        // THE CHANNEL THAT CANNOT BE STARVED, and the one the live gate was
        // actually answered with, is the WARNING. Every refusal emits one
        // unconditionally, so `grep -c REFUSED console.log` is the count --
        // and it needs no frame, no level and no clean exit, which this
        // readout did. Measured rather than assumed: a listen server quit with
        // `quit` produced `UnloadSide(client)`, no server-side teardown, and
        // no line from here at all.

        // GUARDED, and this is not defensive programming. DLLInit returns false
        // on four paths -- no engine, no cvar, no runtime, and a managed
        // ServerSystemStart that says no -- and THE ENGINE CALLS DLLShutdown
        // AFTER A FAILED DLLInit. Three of those four leave this member null,
        // so an unconditional forward turns "the managed runtime would not
        // load" into a null dereference, which is the one outcome that hides
        // the diagnostic saying what was actually missing. plan_gamedll.md
        // Part 10b.
        //
        // The stop is not a runtime slot (D5): it unloads the managed side,
        // which releases the runtime and the root before the context goes.
        if (ManagedRuntimeServer() != nullptr)
        {
            SourceSharp_UnloadManagedSide();
        }

        // STOCK'S IGameSystem::ShutdownAllSystems() (gameinterface.cpp:876) HAS
        // NOTHING TO WALK HERE: neither native system (CGameStringPool,
        // CPhysicsHook) overrides Shutdown, and the managed row's was empty.
        // The walk went with kServerSystems[] (plan_server_native_v2 T4e);
        // ServerSystemsTests holds that no row declares the stage.


        // AND THE REST OF DLLInit, UNWOUND IN REVERSE. It connected all three
        // tiers and registered its ConVars and disconnected none of them: on a
        // listen server that is a leak across map loads, and with the CLR
        // refcounted across client.so and server.so an unbalanced teardown is
        // also how the runtime outlives the side that owned it.
        //
        // The order is stock's, backwards (gameinterface.cpp:879-882): tier3,
        // tier2, the ConVars, tier1. ConVar_Unregister sits INSIDE the tier
        // pair rather than after it because it needs g_pCVar, which is what
        // DisconnectTier1Libraries takes away.
        SourceSharp_DisconnectTier3();
        ConVar_Unregister();
        // The engine holds none of our commands or convars now: delete the
        // C#-declared ones (plan_server_novalve.md 3.6, live at unload).
        SourceSharp_ReleaseConsoleObjects();
        SourceSharp_DisconnectTier1();

        m_pEngine = nullptr;
        m_pCVar = nullptr;

        // THE TIERS LAST, level then DLL: nothing above may still reach them,
        // and a level the engine never shut down is ended here rather than
        // leaked. The engine calls this after a FAILED DLLInit too, where the
        // DLL tier exists (DLLInit's first line) and ends the same way.
        m_pLevel.End();
        m_pDll.End();
        // g_pSharedChangeInfo is deliberately NOT cleared, because stock does
        // not clear it either -- its DLLShutdown unwinds the tiers and nothing
        // else. It points at engine-owned storage that outlives this DLL, and
        // a NULL there is a crash in an inline on the networking path rather
        // than a caught error, so leaving the stale-but-valid pointer is
        // strictly safer than symmetry.
    }

    // AN UNCONDITIONAL REFUSAL THAT MUST NOT BECOME AN ABSENCE.
    //
    // plan_shim.md step 5c drops HLTV and Replay, and this is one of the four
    // vtable slots the step deliberately KEPT. IServerGameDLL is an interface
    // the engine holds by pointer and calls BY VTABLE POSITION; removing this
    // method shifts every slot declared after it, and the engine's call to one
    // of those then enters the wrong body with the wrong arguments. There is no
    // link error and no log line -- the symptom is an unrelated method
    // misbehaving, which is the worst way for a deletion to be wrong.
    //
    // So Replay is refused here, once, by returning false: the engine asks
    // whether the game DLL wants a replay system, and the answer is no. The
    // factory is not called and nothing is stored.
    bool ReplayInit(CreateInterfaceFn fnReplayFactory) override
    {
        (void)fnReplayFactory;
        return false;
    }

    bool GameInit() override
    {

        return ManagedRuntimeServer()->OnGameStart();
    }

    void PostInit() override
    {

        // Stock's whole body is IGameSystem::PostInitAllSystems()
        // (gameinterface.cpp:849-852). Neither native system overrides
        // PostInit, so the native half of that pass went with kServerSystems[]
        // (plan_server_native_v2 T4e); the managed participants' PostInit is
        // OnGamePostStart's.
        ManagedRuntimeServer()->OnGamePostStart();
    }

    void GameShutdown() override
    {

        ManagedRuntimeServer()->OnGameStop();
    }

    bool LevelInit(char const *pMapName,
                   char const *pMapEntities, char const *pOldLevel,
                   const char *pLandmarkName, bool loadGame, bool background) override
    {

        // TODO #2 IS CLOSED, and this is where its verdict lives so that the
        // comment's deletion does not take the question with it.
        //
        // ParseParticleEffects and ParseParticleEffectsMap are now TWO of the
        // nine native steps LevelInit is made of -- SourceSharp_LevelParse-
        // ParticleManifest and ...ForMap in shell_level.cpp -- and their
        // ORDER, along with the other seven, is ServerGameDll.SpawnTheMap's in
        // C#. They stayed two steps rather than becoming one because they are
        // two files with two lifetimes and two calls in stock; one step would
        // have put the sequence in C++, which is what Part 4's rule forbids.
        // docs/gamedll-ledger.md, Phase 6.

        // The ABI's flat struct, which the managed-built runtime takes by pointer
        // (plan_engineapi_vtables D5: a class by value cannot cross a vtable
        // managed writes). Field-assigned rather than positional: its members
        // are four interchangeable `const char *`.
        // THE LEVEL TIER (T6): a new object for every map, before the map's
        // load reaches anything that keeps per-map state.
        m_pLevel.Begin();

        SourceSharpMapLoad load = {};
        load.pMapName = pMapName;
        load.pMapEntities = pMapEntities;
        load.pOldLevel = pOldLevel;
        load.pLandmarkName = pLandmarkName;

        // THE ARM IS MANAGED'S (plan_server_native_v2 T4e). Stock picks one of
        // four from loadGame, pOldLevel and background and writes
        // gpGlobals->eLoadType first; MapLoadArms makes that choice and the
        // write is IShimLevel::SetLoadType. pOldLevel's null-ness reaches it
        // through the struct, so a transition is still "loadGame with an old
        // level" exactly as stock tests the pointer.
        return ManagedRuntimeServer()->OnMapLoad(&load, loadGame, background);
    }

    void LevelShutdown() override
    {

        // ONE FORWARD, and the whole of stock's body is on the other side of
        // it. CServerGameDLL::LevelShutdown is nine calls whose ORDER is the
        // only thing in it, so the order is ServerGameDll.MapUnload's, in C#,
        // over nine native steps -- three of which are the named list's
        // shutdown passes, walked backwards.
        ManagedRuntimeServer()->OnMapUnload();

        // ...and gone once the map's teardown has run: the next LevelInit
        // cannot see this map's per-map state, because it no longer exists.
        m_pLevel.End();
    }

    void ServerActivate(edict_t *pEdictList, int edictCount, int clientMax) override
    {

        // THE THREE ARGUMENTS ARE NOT FORWARDED, AND STOCK DOES NOT READ THEM
        // EITHER. CServerGameDLL::ServerActivate takes pEdictList, edictCount
        // and clientMax and its body mentions none of the three: it walks
        // gEntList, which is the game DLL's own list, not the engine's edict
        // array. Marshalling an edict array across the managed boundary to be
        // ignored on the other side would be a crossing that carries a lie.
        //
        // The order is ServerGameDll.ServerActivate's: reset the delete list,
        // the activate walk, the named list's LevelInitPostEntity pass -- which
        // is where the MANAGED level init happens, as a row -- then close the
        // precache window and clamp the think limit.
        ManagedRuntimeServer()->OnServerActivate();
    }

    void Think(bool finalTick) override
    {
        // THE ONE SERVICE POINT FOR A QUEUED cs_sv_reload, and this is Think's
        // whole job. plan_gamedll.md Part 3b point 1 puts it at the top of
        // GameFrame; eiface.h says why that is the wrong one of the two.
        //
        //   GameFrame  (eiface.h:503) "The server should run physics/think on
        //              all edicts" -- a level is loaded, or it is not called.
        //   Think      (eiface.h:560) "Called once per frame EVEN WHEN NO LEVEL
        //              IS LOADED."
        //
        // So with the service point in GameFrame, a cs_sv_reload typed at the
        // main menu or between maps is never serviced: the request sits in
        // g_bReloadRequested and then fires on the first frame of the next map,
        // which is worse than not reloading because it happens at a moment
        // nobody asked for.
        //
        // The torn-frame objection does not apply, and it is the only reason to
        // have preferred GameFrame. The frame's three managed points --
        // FramePreThink, Think, FramePostThink -- all run INSIDE GameFrame, and
        // Think is a separate engine->shell entry that is never nested in it.
        // So a reload serviced here is atomic with respect to the frame: all
        // three either ran against the outgoing assembly or will run against
        // the incoming one, never a mixture. Both sites satisfy Part 3b's "no
        // other engine->shell entry can interleave"; only this one also covers
        // the no-level case.
        //
        // No runtime guard: this does not call the runtime, and a reload cannot
        // have been requested by a runtime that never loaded.
        //
        // A reload that FAILS does not return from this call -- see the Error()
        // in SourceSharp_ReloadManagedSide. That is what entitles every adapter
        // forward on this class to run without testing the table.
        //
        // finalTick is ignored, as stock's own Think ignores it
        // (gameinterface.cpp:1460-1481). Part 7 measured that body as the
        // autosave-dangerous commit and nothing else -- the save cluster, which
        // this build cannot reach -- so none of it is carried. That finding is
        // about not carrying stock's body across; it does not argue the method
        // must stay unused, and this is the one thing on the shell that wants a
        // per-frame call which does not depend on a level being loaded.
        //
        // NOT BEHIND THE SWITCH, and it is the single deliberate difference
        // between delegate mode and the game before Phase 4. Three reasons,
        // and the first is what makes it safe:
        //
        //   1. It is IDEMPOTENT. SourceSharp_ServiceReloadRequest returns
        //      immediately unless g_bReloadRequested is set, and clears it
        //      before doing anything, so the gamesystem's own service point in
        //      FrameUpdatePostEntityThink -- which is what runs in delegate
        //      mode -- and this one cannot both act on one request.
        //   2. The managed side is up in BOTH modes (CSourceSharpSystem::Init
        //      loads it in delegate mode, LoadManagedRuntime in shell mode) and
        //      cs_sv_reload exists in both, so the reload is not one of the
        //      answers the switch is selecting between.
        //   3. It is the only way the no-level case is reachable at all before
        //      Phase 7. In delegate mode the sole service point is inside
        //      GameFrame, so a cs_sv_reload typed at the main menu sits queued
        //      until the first frame of the next map.
        SourceSharp_ServiceReloadRequest();

        // HOUSEKEEPING, once per frame, level or no level. Today its whole job
        // is the dev API's command pump.
        //
        // WHY IT IS HERE AND NOT IN GameFrame. The pump runs from the managed
        // frame, the managed frame runs from CSourceSharpSystem, and nothing
        // drives CSourceSharpSystem with -shellgamedll until Phase 7 -- so
        // every ./debug-api command in shell mode answered "timed out; is the game
        // running frames?", INCLUDING the entity count that is Phase 6's own
        // gate. docs/gamedll-ledger.md asked for exactly this at the end of
        // Phase 4, and it also makes the dev API usable at the main menu, which
        // it has never been.
        //
        // Not behind a null check on the same argument the reload service above
        // makes: a Think can only be reached through an engine that picked this
        // object, which means DLLInit returned true, which means the runtime
        // loaded.
        ManagedRuntimeServer()->OnThink();
    }

    void GameFrame(bool simulating) override
    {
        // The reload service point is NOT here -- it is Think(). See there.

        ManagedRuntimeServer()->OnGameFrame(simulating);
    }

    void PreClientUpdate(bool simulating) override
    {

    	if ( !simulating )
	    	return;

        ManagedRuntimeServer()->OnPreClientUpdate();
    }

    float GetTickInterval() const override
    {

        // READ ONCE, AT DLLInit, and returned from a member -- which is what
        // lets this stay `const` without a `mutable` anywhere. Stock lets
        // -tickrate move it and the engine asks exactly once; a tick interval
        // that changed mid-run would silently redefine every duration already
        // computed against the old one.
        //
        // The managed side needs no native help to know it: -tickrate is
        // on the process' own command line, which C# reads (ILocalCommandLine), so
        // ServerGameDll computes the answer in its constructor and this is a
        // read of a decision already made.
        return m_flTickInterval;
    }

    ServerClass *GetAllServerClasses() override
    {

        return m_pServerClasses;
    }

    const char *GetGameDescription() override
    {

        // THE ROUTE THAT WAS ALREADY THERE, and not a sixth new ABI slot.
        // SourceSharp_GetGameDescription() (shell_server.h) has reached
        // IServerListing.GameDescription since before this phase, through the
        // GetGameDescription slot, and owns the buffer the engine reads after
        // the call. A second route to one answer is two things to keep in
        // agreement. Part 6 names this trap itself.
        return SourceSharp_GetGameDescription();
    }

    void CreateNetworkStringTables() override
    {

        // NATIVE FIRST, THEN MANAGED, and the order is the contract rather
        // than a preference. The seven tables below are the ones the ENGINE
        // and the rest of this binary already depend on -- shell_te_fwd_s4.cpp
        // reads g_pStringTableEffectDispatch (the message of the day wrote
        // g_pStringTableInfoPanel until 2026-09-29) -- so anything a mod does in the managed
        // slot may reasonably assume they exist, and nothing native may
        // assume a mod did anything. plan_gamedll.md Part 8.
        SourceSharp_CreateNetworkStringTables();

        // Where MOD-DECLARED tables go, and as of plan_gamedll_after.md Phase
        // A8 they actually go there: the six StringTable* slots at the tail of
        // SourceSharpEngineApi are how a mod creates, fills and reads a table
        // of its own, and ServerGameDll.CreateNetworkStringTables uses them.
        //
        // AFTER THE LINE ABOVE, ALWAYS, and this is now load-bearing rather
        // than merely tidy. The engine allows a table to be CREATED in this
        // window and nowhere else, so a mod has to be given its turn here --
        // and a mod given AddString can put a string in any table it can name.
        // Run it before the precaches above and "debug/debugempty" stops being
        // index 0 of Materials, which is the answer EVERY FAILED MATERIAL
        // LOOKUP in the binary gets, silently. stringtable_index0_test.cpp
        // replays this whole method for exactly that reason.
        ManagedRuntimeServer()->CreateNetworkStringTables();
    }

    //-------------------------------------------------------------------------
    // THE SAVE CLUSTER. ELEVEN OF THE FIFTEEN NOW ANSWER -- stage 1 of the
    // server save shim, docs/managed-saverestore.md.
    //
    // WHAT THE REFUSALS SAID, AND HOW MUCH OF IT SURVIVED. Part 7 refused all
    // fifteen, on an argument that read: no console command in this build can
    // reach a save. docs/plan-audit.md D16 measured that and it was wrong about
    // WHY -- the engine's gate is `sv.IsMultiplayer()`, this mod's own
    // CServerGameClients::GetPlayerLimits (shell_server.cpp:192) answers
    // `minplayers = 1`, and at `-maxplayers 1` the whole cluster is live:
    //
    //     # ./source# -maxplayers 1 +map dm_lockdown
    //     ] save acb1test
    //     [SourceSharp] REFUSED: IServerGameDLL::GetSaveComment ... Refusal #1
    //     [SourceSharp] REFUSED: IServerGameDLL::SaveInit ... Refusal #2
    //
    // The refusals were doing exactly the job Part 7 built them for and they
    // were right to be loud. What they were loud ABOUT is the thing that has
    // changed: the second reason, that a save here could capture nothing
    // because `SaveInit` saw NumberOfEntities=473 against a NextEnt walk of 0.
    //
    // THAT REASON IS NOW A SCOPE REDUCTION RATHER THAN A BLOCKER -- §2d.
    // The entity table is the
    // entity-pointer namespace for FIELD_EHANDLE / FIELD_CLASSPTR /
    // FIELD_EDICT inside a DATAMAP; this port's entities are not saved through
    // a datamap at all, and every engine loop over the table degenerates at
    // `tableCount == 0`. So stage 1 writes NO game entity data, leaves the
    // table empty, and that is a complete answer to the engine rather than an
    // unfinished one. The managed entity half is a separate lane.
    //
    // WHAT THE ELEVEN DO. SaveInit, SaveWriteFields, SaveReadFields, Save and
    // Restore are each one forward into IManagedSaveRestore
    // (plan_server_native_v2 T4c): the managed SaveGameSession
    // (src/server/interop/SaveGameSession.cs) sizes, allocates and places the
    // block, encodes the ENGINE's own structs and the managed world's one body
    // block, and narrates each call. PreSave, WriteSaveHeaders,
    // ReadRestoreHeaders, PreSaveGameLoaded and the global-state pair each
    // have an argued reason to move no bytes, written at the method.
    //
    // WHAT STILL REFUSES, and each is a method whose work genuinely is not
    // here: GetSaveComment (which still fills its out-parameter),
    // CreateEntityTransitionList and BuildAdjacentMapList (level TRANSITIONS,
    // which a plain save/load does not involve and which need the entity table
    // this stage does not build). LevelInit's transition arm refuses in
    // managed, where its arm is chosen (MapLoadArms, plan_server_native_v2 T4e).
    //
    // SO WHY KEEP WARNING AND COUNTING AT ALL. plan_server.md Part 5's rule,
    // which Part 7 adopts and which nothing here overturns: a stub that is
    // never called and a stub that is called constantly look identical from
    // outside. The three that remain carry the instrument for that reason, and
    // the eleven that answer replaced it with a trace rather than dropping it.
    //-------------------------------------------------------------------------
    CSaveRestoreData *SaveInit(int size) override
    {
        // THE ENGINE PASSES 0 -- docs/managed-saverestore.md §4a; the size,
        // the token table and the time stamp are the managed session's.
        IManagedSaveRestore *pArea = ManagedSaveRestore();
        CSaveRestoreData *pSaveData = pArea
            ? static_cast< CSaveRestoreData * >( pArea->SaveInit( size, m_pGlobals ? m_pGlobals->curtime : 0.0f ) )
            : nullptr;

        if ( pSaveData == nullptr )
        {
            // The session has already named WHICH allocation failed (or there
            // is no managed side). This line says what happens next: the
            // engine abandons the save on a NULL, on both of its save paths,
            // and prints nothing at all -- docs/managed-saverestore.md §4a.
            Warning( "[SourceSharp] SaveInit is answering NULL. The engine will abandon the "
                     "save at this call site and print NOTHING -- no file and no error is "
                     "what that looks like from outside\n" );
            return nullptr;
        }

        // Stock's ::SaveInit publishes it here too (saverestore.cpp:3146):
        // gpGlobals->pSaveData is how anything holding a CGlobalVars reaches
        // the save in progress.
        if ( m_pGlobals )
            m_pGlobals->pSaveData = pSaveData;

        return pSaveData;
    }

    // THE ENGINE'S OWN STRUCTS, ENCODED BY THE GAME. This is the part of the
    // contract §2a does NOT cover: the engine does not parse the game's bytes,
    // but it hands the game its own save header and its own light styles as
    // typedescription_t arrays and expects them back on load. Three field types
    // across all 37 of them, measured rather than assumed --
    // game/shared/save_field_codec.h carries the measurement and the argument
    // for why a FOURTH must refuse rather than be skipped.
    //
    // pMap IS UNUSED, and stock's body does not read it either: CSave::Write-
    // Fields takes pRootMap only to walk FTYPEDESC_OVERRIDE chains, which the
    // engine's own flat header maps do not have.
    //
    // sizeof( typedescription_t ) is passed rather than known in C#: the
    // struct holds a member-function pointer, whose width is the C++ ABI's.
    void SaveWriteFields(CSaveRestoreData *pSave, const char *pname, void *pBaseData, datamap_t *pMap, typedescription_t *pFields, int fieldCount) override
    {
        if ( IManagedSaveRestore *pArea = ManagedSaveRestore() )
            pArea->SaveWriteFields( pSave, pname, pBaseData, pFields, (int32_t)sizeof( typedescription_t ), fieldCount );
    }

    void SaveReadFields(CSaveRestoreData *pSave, const char *pname, void *pBaseData, datamap_t *pMap, typedescription_t *pFields, int fieldCount) override
    {
        if ( IManagedSaveRestore *pArea = ManagedSaveRestore() )
            pArea->SaveReadFields( pSave, pname, pBaseData, pFields, (int32_t)sizeof( typedescription_t ), fieldCount );
    }

    //-------------------------------------------------------------------------
    // THE GLOBAL STATE PAIR, AND THE DECISION IS TO WRITE NOTHING AND SAY SO.
    //
    // Stock forwards these to ::SaveGlobalState / ::RestoreGlobalState in
    // game/server/globalstate.cpp, whose bodies are `CSave saveHelper( pSave );
    // gGlobalState.Save( saveHelper )` -- and BOTH ARE ALREADY EMPTY IN THIS
    // BUILD, behind `#ifndef SOURCESHARP` since plan_shared.md Phase 2, because
    // CSave and CRestore left the build with game/shared/saverestore.cpp. The
    // file says so at its own guard, and names this shell's refusal as the only
    // caller either one had.
    //
    // THREE REASONS NOT TO RE-ENABLE THEM HERE, in increasing order of weight:
    //
    //   1. There is nothing to encode. gGlobalState's list is filled by
    //      env_global and by trigger_changelevel's global-entity transfer.
    //      trigger_changelevel is evicted from this build and this lane does no
    //      level transitions, so on a plain save the list is empty and both
    //      functions would write a zero-length CUtlVector.
    //   2. It is not a three-type block. The datadesc is
    //      DEFINE_UTLVECTOR( m_list, FIELD_EMBEDDED ) over globalentity_t, whose
    //      own fields are two DEFINE_CUSTOM_FIELDs through g_GlobalSymbolDataOps
    //      -- an ISaveRestoreOps callback, a CUtlSymbol table, and FIELD_EMBEDDED
    //      recursion. save_field_codec.h encodes an int, a fixed-width char array
    //      and a float, and REFUSES everything else on purpose. Wiring this in
    //      would be the first caller to hit that refusal, for an empty list.
    //   3. Re-enabling the guard would put game/shared/saverestore.cpp back on
    //      the server's link line -- CUtlVectorDataOps<>'s virtual Save/Restore
    //      reach CSave::WriteAll and DoWriteAll -- which is 5,000 lines of stock
    //      that Phase 2 removed and that this lane's whole argument is about not
    //      needing.
    //
    // So: no bytes, no guard change, no link edge, and a trace line so that
    // "SaveGlobalState wrote nothing" is a finding rather than an absence. This
    // ALSO answers §6 item 3 -- whether the pair is on the plain-save path at
    // all -- because the trace fires whether or not anything is written.
    //
    // WHAT WOULD CHANGE THE ANSWER: a level transition, or an env_global that a
    // managed entity reads across a load. Either one makes this a real encoder,
    // and it would be a managed one over SavedEntity rather than a datamap.
    //-------------------------------------------------------------------------
    void SaveGlobalState(CSaveRestoreData *pSave) override
    {
        Msg( "[SourceSharp][save] SaveGlobalState  -- nothing written; gGlobalState is empty in "
             "this build and globalstate.cpp's body is #ifndef SOURCESHARP\n" );
    }

    void RestoreGlobalState(CSaveRestoreData *pSave) override
    {
        Msg( "[SourceSharp][save] RestoreGlobalState  -- nothing read; symmetric with "
             "SaveGlobalState\n" );
    }

    // Stock's PreSave is `g_pGameSaveRestoreBlockSet->PreSave( s )`, whose one
    // load-bearing member is CEntitySaveRestoreBlockHandler -- the NextEnt walk
    // that fills the entity table. That walk is what D16 measured returning 0
    // against NumberOfEntities=473: every entity in this build is on the
    // parallel hierarchy and outside the list stock's saver walks. §2d is why
    // that is now a scope reduction rather than the blocker it read as -- the
    // table is the entity-pointer namespace for a DATAMAP, this port's entities
    // are not saved through one, and every engine loop over the table
    // degenerates at tableCount == 0.
    void PreSave(CSaveRestoreData *pSave) override
    {
        Msg( "[SourceSharp][save] PreSave  -- no entity table is built; tableCount stays 0\n" );
    }

    // The BODIES half of the file, which stage 1 has none of: game entity data
    // is SourceSharp.Abi's SavedEntityCodec and is another lane's half. Writing
    // nothing here is correct rather than incomplete -- see the file's stage-1
    // note -- and the cursor not moving across this call is the thing worth
    // recording, because §4a's second constraint is that the engine takes the
    // header and body spans by pointer subtraction ACROSS this boundary.
    void Save(CSaveRestoreData *pSave) override
    {
        // THE ONE BODY BLOCK, and it is the whole managed world: the session
        // sizes it through the module, fills it and frames it as SSENTITY. A
        // refusal is loud there rather than here.
        if ( IManagedSaveRestore *pArea = ManagedSaveRestore() )
            pArea->SaveWriteBody( pSave );
    }

    // THE ONE REFUSAL THAT STILL HAS TO WRITE SOMETHING -- docs/plan-audit.md
    // D16. Fourteen of the fifteen return void or a value this shell chooses,
    // so refusing by doing nothing is a complete answer. This one is handed a
    // buffer to FILL (`char comment[80]` on the engine's stack), and a refusal
    // that returns without touching it leaves the engine calling uninitialised
    // stack the save's title. Refusing the work is a decision; leaving an
    // out-parameter undefined is a contract broken quietly.
    //
    // IT STILL REFUSES AND IT STILL COUNTS. What changed is only that the
    // buffer is defined afterwards, in stock's own spelling minus stock's HL2
    // chapter-title table -- see shell_savecomment.h.
    //
    // COUNTED FIRST, then filled, for the reason every other refusal counts
    // before it forwards: the finding has to survive the line under it.
    void GetSaveComment(char *comment, int maxlength, float flMinutes, float flSeconds, bool bNoTime = false) override
    {

        ShellGameDll_Unreachable( "IServerGameDLL::GetSaveComment" );

        ShellGameDll_FormatSaveComment( comment, maxlength,
                                        STRING( gpGlobals->mapname ),
                                        flMinutes, flSeconds, bNoTime );
    }

    // The last thing on the write path, and the cursor position it reports IS
    // the size of the game's contribution to the .sav -- everything before it
    // came out of SaveWriteFields, which is to say out of the ENGINE's own
    // structs.
    void WriteSaveHeaders(CSaveRestoreData *pSave) override
    {
        // STILL NO HEADER BLOCKS, and that is a decision rather than an
        // omission. Stock's header region is the block SET's directory -- a
        // name and a size per block so that Restore can find one it does not
        // read in order. There is exactly ONE body block here
        // (SOURCESHARP_SAVE_ENTITY_BLOCK) and Restore reads it at the cursor,
        // so a directory would be a second statement of one fact. The cursor
        // reported is the game's whole contribution to this .sav.
        Msg( "[SourceSharp][save] WriteSaveHeaders  -- 0 header blocks\n" );
    }

    void ReadRestoreHeaders(CSaveRestoreData *pSave) override
    {
        // AND THIS IS WHERE THE LOAD BEGINS TO READ THE GAME'S BUFFER.
        // The engine's load-game path calls it FIRST, before the 'Save Header'
        // and the light styles are read and before Restore -- the trace order
        // in the console shows exactly that. Nothing is read
        // here because WriteSaveHeaders writes nothing; the trace is what says
        // the load reached this far, which for the whole of stage 1 it never
        // did.
        Msg( "[SourceSharp][save] ReadRestoreHeaders  -- 0 header blocks expected\n" );
    }

    // createPlayers IS FORWARDED NOWHERE, and that is the honest answer rather
    // than a dropped argument. Stock passes it to
    // g_pGameSaveRestoreBlockSet->Restore, where the only handler that reads it
    // is the entity block's -- it decides whether the restore should re-create
    // player entities from the table or adopt the ones already spawned. With no
    // entity block and an empty table there is nothing for it to select between.
    void Restore(CSaveRestoreData *pSave, bool createPlayers) override
    {
        // THE OTHER HALF OF Save's ONE BLOCK. The world's NATIVE entities have
        // already been respawned from the map lump by this point -- see
        // ServerGameDll.MapLoadSaveGame, which spawns the map and only then
        // asks the engine to load the game state -- so what comes back here is
        // the MANAGED half laid over entities that exist.
        if ( IManagedSaveRestore *pArea = ManagedSaveRestore() )
            pArea->SaveReadBody( pSave );
    }

    // THE ONE MEMBER OF THE SAVE CLUSTER THAT MUST NOT WARN, and getting this
    // wrong makes the game unusable rather than subtly wrong.
    //
    // `false` IS STILL THE RIGHT ANSWER NOW THAT THE CLUSTER ANSWERS, and the
    // reason is sharper than the old one rather than the same one repeated.
    // Stock's body is `return g_InRestore`, a flag raised by
    // BeginRestoreEntities and lowered by EndRestoreEntities -- the ENTITY
    // restore, spanning more than one engine call -- and its documented purpose
    // is to stop sv.tickcount advancing while that is in flight, so the NPCs do
    // not all fast-forward their think times to the same tick. Stage 1 restores
    // no entities and defers nothing to a later frame: its Restore completes
    // inside the engine's own call, so there is no window in which a frame
    // could observe a true. When the managed entity restore lands, this becomes
    // a real flag, and it will be one because that lane raises it.
    //
    // AND THIS IS NOT AN UNREACHABLE METHOD -- GameFrame reads it as
    // its FIRST gate (gameinterface.cpp:1300), so the engine asks it on every
    // single frame of every map. A ShellGameDll_Unreachable here is sixty-six
    // console lines a second and a counter in the hundreds of thousands: a
    // console nobody can read, a frame rate nobody can play, and a refusal
    // reading that says nothing because it drowns the fifteen findings the
    // counter exists to carry.
    //
    // So it answers, with no warning and no count, and this comment is the
    // record that the omission is a decision. RefusalSites.IsRestoringDoesNot-
    // WarnAndDoesNotCount pins it, because "someone tidied the odd one out to
    // match its neighbours" is exactly the edit that looks like an improvement.
    bool IsRestoring() override
    {

        return false;
    }

    int CreateEntityTransitionList(CSaveRestoreData *pSave, int nBytes) override
    {

        ShellGameDll_Unreachable( "IServerGameDLL::CreateEntityTransitionList" );

        return 0;
    }

    void BuildAdjacentMapList() override
    {

        ShellGameDll_Unreachable( "IServerGameDLL::BuildAdjacentMapList" );
    }

    //-------------------------------------------------------------------------
    // What the engine asks when it wants to describe a message it is carrying:
    // a name and a size for an engine id. The registry is C#'s
    // (plan_side_isolation §2.2a), so this is one forward to it; false outside
    // a managed load, as for an id the registry does not hold.
    //-------------------------------------------------------------------------
    bool GetUserMessageInfo(int msg_type, char *name, int maxnamelength, int &size) override
    {
        IManagedUserMessages *pMessages = ManagedUserMessages();
        return pMessages && pMessages->GetUserMessageInfo(msg_type, name, maxnamelength, &size);
    }

    CStandardSendProxies *GetStandardSendProxies() override
    {

        return m_pSendProxies;
    }

    // Stock's whole body is `gamestats->Event_PreSaveGameLoaded( ... )`, and
    // the gamestats fan-in left this build entirely (gamestats_evicted_test.cpp
    // pins that it did and that the server row is where a managed one would
    // reattach). So there is no work to forward -- what there IS is the FIRST
    // call of the load sequence, which docs/managed-saverestore.md §6 item 5
    // lists as undetermined ordering, so it narrates itself like the rest.
    void PreSaveGameLoaded(char const *pSaveName, bool bCurrentlyInGame) override
    {
        Msg( "[SourceSharp][save] PreSaveGameLoaded( '%s', currentlyInGame=%d )\n",
             pSaveName ? pSaveName : "<null>", bCurrentlyInGame ? 1 : 0 );
    }

    bool ShouldHideServer() override
    {

        // TODO #7, CLOSED: IServerListing.ShouldHideServer, and there is
        // nothing else to say about it -- which is why the verdict is written
        // here anyway. A one-line forward is exactly the shape whose answer
        // ends up living only in the table, and the table is not what the next
        // reader of this file has open.
        return ManagedRuntimeServer()->OnShouldHideServer();
    }

    //-------------------------------------------------------------------------
    // PART 11a2's FIRST GAP, AND IT IS PORTED NOW -- plan_gamedll_after.md
    // Phase A2. It is not one of the sixteen and never carried a TODO, which is
    // why nothing in plan_gamedll.md was ever going to catch it; the verdict
    // stays written here so the next reader does not have to find the ledger.
    //
    // THE VERDICT IS: IMPLEMENTED. It forwards to managed, and managed calls
    // back the one native step -- Entities.InvalidateMdlCacheAll, which is
    // stock's gEntList walk (gameinterface.cpp:2049-2061) in
    // shell_mdlcache.cpp. Until A2 the body was empty, so an mdlcache flush
    // left every server entity holding the CStudioHdr it had built over the OLD
    // studio data, read back by bone setup, hitboxes, sequences and
    // attachments; the client half of the same interface has always been
    // implemented (cdll_client_int.cpp:1617), so the gap was one-sided and the
    // symptom was a model reload that appears not to take, with no error.
    //
    // WHY THE WALK STAYED NATIVE, against Part 11a2's design. Part 11a2 asks
    // for an IMdlCacheEvents interface walked over MANAGED entities. That is an
    // ADDITION and not a replacement, and its own caveat says so: the state
    // being freed is CBaseAnimating::m_pStudioHdr -- a native member, freed by
    // a native virtual -- and no managed walk reaches it. A managed-only
    // version would leave this loss exactly where it was while reading as a
    // fix. WHAT IS STILL NOT BUILT, said plainly: nothing implements
    // IMdlCacheEvents, so a managed entity holding its own model-derived cache
    // has no hook here and gets one the day the first such entity exists.
    // Adding an interface with no implementor now would be a second walk to
    // keep in agreement with this one, for nobody.
    //
    // NO REFUSAL AND NO COUNTER, unchanged and for the original reason: Part
    // 7's counter is for methods argued to be UNREACHABLE, and this one is
    // reached whenever an operator flushes the cache.
    //-------------------------------------------------------------------------
    void InvalidateMdlCache() override
    {
        ManagedRuntimeServer()->OnInvalidateMdlCache();
    }

    //-------------------------------------------------------------------------
    // PART 10, FIRST LOOSE END. The engine's answer to a StartQueryCvarValue
    // this build never issues.
    //
    // A FORWARD WITH A COUNTER, NOT A FEATURE. Stock's body is empty. Nothing
    // in this tree calls IVEngineServer::StartQueryCvarValue, so nothing can
    // arrive here -- and "nothing can arrive here" is exactly the claim Part 7's
    // counter exists to turn into a measurement. The slot is real, so a mod
    // that starts asking clients about their cvars gets its answer in C#
    // instead of finding an empty native body; the count says whether that day
    // has arrived.
    //
    // THE EDICT DOES NOT CROSS. It is a pointer into a table only the engine
    // owns, so it becomes the entity index -- what the wire carries and what
    // OnClientPutInServer already uses. The engine passes NULL for a query
    // whose client left before it finished, and that becomes -1 rather than 0,
    // because 0 is worldspawn and a real index.
    //-------------------------------------------------------------------------
    void OnQueryCvarValueFinished(QueryCvarCookie_t iCookie, edict_t *pPlayerEntity, EQueryCvarValueStatus eStatus, const char *pCvarName, const char *pCvarValue) override
    {

        ShellGameDll_Unreachable( "IServerGameDLL::OnQueryCvarValueFinished" );

        const int nEntIndex = ( pPlayerEntity != nullptr && m_pEngine != nullptr )
                            ? m_pEngine->IndexOfEdict( pPlayerEntity )
                            : -1;

        ManagedRuntimeServer()->OnQueryCvarValueFinished( (int)iCookie, nEntIndex, (int)eStatus,
                                                     pCvarName, pCvarValue );
    }

    //-------------------------------------------------------------------------
    // PART 11a2's OTHER TWO GAPS, AND THEY ARE ONE SUBJECT -- IMPLEMENTED at
    // plan_gamedll_after.md Phase A2. Neither ever carried a TODO either, so
    // neither is in the sixteen-row table; both keep their verdict here for the
    // same reason as InvalidateMdlCache above.
    //
    // THE VERDICT IS: IMPLEMENTED. Both were empty, and the loss was larger
    // than the two bodies: stock Init()s the game-server context in DLLInit
    // too (gameinterface.cpp:676) and stock's DLLInit has been unreachable
    // since Phase 7a deleted the delegate -- while the shell nevertheless
    // CLEARS the context at every level shutdown. So the context was
    // clear-only, every pointer in it permanently NULL, and everything
    // downstream -- listing, auth, stats -- was absent rather than broken.
    // Nothing crashes, which is what let it survive a green suite. The one
    // consumer in the built server is util_shared.cpp:2049 GetSteamUtils, read
    // by GetUniverse, read by UTIL_PlayerByCommandArg's two SteamID arms.
    //
    // (Part 11a2 says "the game-server context is never Init'd in DLLInit at
    // all". That is FALSE in this tree -- gameinterface.cpp:675 and :676 Init
    // both contexts and :922/:923 Clear both -- and its citations :648/:873 are
    // stale. Measured at A2, recorded rather than quietly worked around.)
    //
    // NO REFUSAL AND NO COUNTER, unchanged: these arrive on any server Steam
    // activates, so they are reachable by design and a warning here would be
    // noise on every startup saying the opposite of what is true.
    //-------------------------------------------------------------------------
    void GameServerSteamAPIActivated() override
    {
        ManagedRuntimeServer()->OnSteamGameServerActivated();
    }

    // PART 11a2's THIRD GAP, IMPLEMENTED at Phase A2, and the block above
    // covers both halves -- but it is written again here because the check that
    // reads it walks back over the contiguous comment run and nothing further.
    // That is not pedantry: a window wide enough to reach the previous method's
    // comment is a window that passes for its NEIGHBOUR's verdict, which is
    // what mutation G2 caught it doing.
    //
    // THE VERDICT IS: IMPLEMENTED. The Steam game-server context is cleared on
    // the way down, through the one body shell_steamcontext.cpp holds.
    //
    // THE ORDER INVERTS BETWEEN THE PAIR AND THAT IS THE WHOLE DESIGN. On the
    // way up native re-acquires and THEN managed hears about it; here managed
    // runs FIRST and clears the context itself as its last act, because a
    // handler that ran after the clear would have nothing to read. Stock makes
    // the same argument with a whole game-system stage of its own --
    // LevelShutdownPreClearSteamAPIContext exists for no other reason. Not
    // counted, for the same reason as its partner: reachable by design.
    void GameServerSteamAPIShutdown() override
    {
        ManagedRuntimeServer()->OnSteamGameServerShutdown();
    }

    //-------------------------------------------------------------------------
    // PART 10, SECOND LOOSE END, and the TODO this replaces asked the right
    // question and guessed the wrong answer.
    //
    // "IS THIS THE INVERSE OF OnServerActivate?" NO, and the two are not a pair
    // in any sense. Stock stores a flag (gameinterface.cpp:2004-2018) that a
    // DEDICATED server reads while it idles with nobody connected;
    // ServerActivate is a per-map event that fires once the entities exist.
    // They do not alternate, they are not each other's opposite, and on a
    // listen server this one never arrives at all -- which is why it is counted
    // as well as forwarded.
    //-------------------------------------------------------------------------
    void SetServerHibernation(bool bHibernating) override
    {

        ShellGameDll_Unreachable( "IServerGameDLL::SetServerHibernation" );

        ManagedRuntimeServer()->OnHibernation( bHibernating );
    }

    IServerGCLobby *GetServerGCLobby() override
    {

        return nullptr;
    }

    const char *GetServerBrowserMapOverride() override
    {

        // NULL AND EMPTY ARE DIFFERENT ANSWERS HERE. Null means "the engine
        // should show the real map name"; an empty string means "show
        // nothing". Managed says which by returning -1 for no answer against 0
        // for an empty one, and collapsing the two would make an override of ""
        // silently show the map name.
        return Answer( m_szBrowserMapOverride, sizeof( m_szBrowserMapOverride ),
                       ManagedRuntimeServer()->OnBrowserMapOverride(
                           m_szBrowserMapOverride, sizeof( m_szBrowserMapOverride ) ) );
    }

    const char *GetServerBrowserGameData() override
    {

        // TODO #11, CLOSED: IServerListing.BrowserGameData, through the shell's
        // own buffer. The null-versus-empty argument above is this method's
        // too -- Answer() is the one place either convention is applied, so
        // there is one rule and not two.
        return Answer( m_szBrowserGameData, sizeof( m_szBrowserGameData ),
                       ManagedRuntimeServer()->OnBrowserGameData(
                           m_szBrowserGameData, sizeof( m_szBrowserGameData ) ) );
    }

    //-------------------------------------------------------------------------
    // PART 10, THIRD LOOSE END, and the answer to "what is this used for".
    //
    // It is the engine's `status` console command asking the mod to print its
    // own lines. Stock forwards to g_pGameRules->Status( print ).
    //
    // PART 4'S RULE INVERTED, and forced rather than chosen. Part 4's rule is
    // that the shell takes the native steps and managed decides the order; here
    // the native step is `print`, and `print` is a C VARIADIC function pointer
    // -- `void (*)( const char *fmt, ... )`. A variadic calling convention is
    // not something this boundary can carry: managed code cannot call one, and
    // a delegate that pretended to would have to agree with the C ABI's
    // register/stack split per platform. So MANAGED RETURNS LINES AND THE SHELL
    // PRINTS THEM, which costs nothing, because everything a mod wants to say
    // here is already text.
    //
    // The crossing is the browser strings': the shell owns the buffer, managed
    // fills it and returns the byte count, -1 means "nothing to say". Lines are
    // separated by '\n' and split HERE, so managed code does not have to know
    // that `print` takes one line at a time -- and an empty line is dropped
    // rather than printed, so a trailing separator is harmless.
    //
    // NOT COUNTED. `status` is a command an operator types on a live server, so
    // unlike the other two loose ends this one IS reachable and a warning would
    // be noise on a working feature.
    //-------------------------------------------------------------------------
    void Status(void (*print)(const char *fmt, ...)) override
    {

        if ( print == nullptr )
            return;

        const int nWritten = ManagedRuntimeServer()->OnStatusLines( m_szStatusLines,
                                                               sizeof( m_szStatusLines ) );

        if ( nWritten < 0 || nWritten >= (int)sizeof( m_szStatusLines ) )
            return;

        m_szStatusLines[ nWritten ] = '\0';

        // Split in place. The buffer is the shell's and is refilled on every
        // call, so writing terminators into it costs nothing and saves a second
        // copy of every line.
        char *pLine = m_szStatusLines;
        while ( pLine != nullptr && *pLine != '\0' )
        {
            char *pNext = strchr( pLine, '\n' );
            if ( pNext != nullptr )
                *pNext++ = '\0';

            if ( *pLine != '\0' )
                print( "%s\n", pLine );

            pLine = pNext;
        }
    }

    void PrepareLevelResources(/* in/out */ char *pszMapName, size_t nMapNameSize,
                               /* in/out */ char *pszMapFile, size_t nMapFileSize) override
    {
    }

    ePrepareLevelResourcesResult AsyncPrepareLevelResources(/* in/out */ char *pszMapName, size_t nMapNameSize,
                                                            /* in/out */ char *pszMapFile, size_t nMapFileSize,
                                                            float *flProgress = NULL) override
    {

        if (flProgress)
        {
            *flProgress = 1.f;
        }

        return IServerGameDLL::ePrepareLevelResources_Prepared;
    }

    eCanProvideLevelResult CanProvideLevel(/* in/out */ char *pMapName, int nMapNameMax) override
    {

        return IServerGameDLL::eCanProvideLevel_CannotProvide;
    }

    bool IsManualMapChangeOkay(const char **pszReason) override
    {

        // CLEARED BEFORE THE CALL, and that is what makes one crossing enough.
        // Managed writes the reason only when it refuses, so without this the
        // allow path would leave the PREVIOUS refusal's text in the buffer --
        // and the engine tests the pointer, not the verdict, in at least one
        // place. Clearing here means "a reason was written" is answerable from
        // the buffer, so the verdict can be the return value and there is no
        // second out-parameter and no second ask.
        m_szMapChangeReason[ 0 ] = '\0';

        const bool bOkay = ManagedRuntimeServer()->OnIsManualMapChangeOkay(
            m_szMapChangeReason, sizeof( m_szMapChangeReason ) );

        if ( pszReason != nullptr )
        {
            // NULL rather than "" when there is nothing to say, for the same
            // reason as the browser overrides: the engine tests the pointer.
            *pszReason = ( !bOkay && m_szMapChangeReason[ 0 ] != '\0' )
                       ? m_szMapChangeReason : nullptr;
        }

        return bOkay;
    }

    bool GetWorkshopMap(uint32 uIndex, WorkshopMapDesc_t *pDesc) override
    {

        return false;
    }

    bool RegisterConCommandBase(ConCommandBase *pCommand) override
    {
        // Stock's CGameDLL_ConVarAccessor (game.cpp:60-98), transliterated.
        // All three steps are load-bearing: without the command-line apply a
        // build ignores `+sv_cheats 1`, and without the replicated-default
        // capture a debug game DLL takes a shared ConVar's value from the
        // client. The managed half of both -- which declaration a command-line
        // value may set, and what its declared default is -- is Phase 3's
        // ConVarRegistry; the shell keeps stock's behaviour until then.

        // Remember the "unlinked" default value for replicated cvars.
        const bool bReplicated = pCommand->IsFlagSet(FCVAR_REPLICATED);
        const char *pDefault = nullptr;
        if (bReplicated && !pCommand->IsCommand())
        {
            pDefault = ((ConVar *)pCommand)->GetDefault();
        }

        // Link to engine's list instead
        m_pCVar->RegisterConCommand(pCommand);

        // Apply any command-line values.
        const char *pValue = m_pCVar->GetCommandLineValue(pCommand->GetName());
        if (pValue)
        {
            if (!pCommand->IsCommand())
            {
                ((ConVar *)pCommand)->SetValue(pValue);
            }
        }
        else if (bReplicated && pDefault && !pCommand->IsCommand())
        {
            // Not overridden at the command line, so a replicated ConVar takes
            // the server's value rather than the client's.
            ((ConVar *)pCommand)->SetValue(pDefault);
        }

        return true;
    }

    // -- IManagedRuntimeEnvironment ------------------------------------------
    // The inversion Part 0a #1 exists for: the shell owns DLLInit, so the shell
    // owns the factory, and everything below asks it rather than fetching its
    // own. Both members are already assigned in DLLInit; they were simply never
    // exposed.

    void *GetEngineInterface(const char *pName, int *pReturnCode) override
    {
        return m_pEngineFactory(pName, pReturnCode);
    }

    // Engine-owned memory at a fixed address, alive for the whole process and
    // changing every frame. Managed code reads fields through the pointer --
    // a snapshot taken at load would be one frame stale everywhere.
    CGlobalVars &GetGlobalVars() override
    {
        return *m_pGlobals;
    }

public:
    // GATE 5's TRANSMIT-SET RECORDER. Public for the ConCommand's accessor
    // (ShellGameDll_TransmitSet, read by shell_gamedll_debug.cpp) and for
    // the three free functions at the bottom of this file, which are the seam
    // shell_gameents.cpp records through -- see shell_steporder.h for why that
    // file must not be able to see this class. Non-const because recording
    // writes.
    CShellTransmitSet &TransmitSet() { return m_TransmitSet; }

    // Everything native holds from managed (plan_managed_vtable_reload.md L2):
    // the root, the runtime and the generated areas, reached through
    // ShellGameDll_Managed() at the bottom of this file.
    CManagedHolders &Managed() { return m_Managed; }

    CServerDllState *Dll() const { return m_pDll.Get(); }
    CServerLevelState *Level() const { return m_pLevel.Get(); }
    CShimRootServer &ShimRoot() { return m_ShimRoot; }

private:
    // PART 7'S SPELLING, KEPT, now that there is no counter behind it.
    //
    // The plan writes ShellGameDll_Unreachable as a free function, and a free
    // function needed its counter somewhere -- which under this project's
    // standing rule ("no file-scope statics or globals in SourceSharp's C++")
    // it could not have, so the count was a FIELD and this was its forwarder.
    // The table is gone; the WARNING is what Part 7 actually needed and is
    // what the gate reads. This stays a member rather than becoming a free
    // function only because every call site already reads exactly as Part 7
    // writes it, and the name is the greppable thing.
    void ShellGameDll_Unreachable( const char *pMethod )
    {
        // Warning() and not Msg(): a refusal firing means Part 7 was wrong
        // about that method, and the whole point of writing the arguments down
        // was so that being wrong about one of them would be loud rather than
        // silent.
        Warning( "[SourceSharp] REFUSED: %s was called. plan_gamedll.md Part 7 "
                 "argues this method is unreachable in this build, so it is a "
                 "no-op -- if you are reading this, that argument was wrong for "
                 "this method. `grep -c REFUSED` over console.log is the tally.\n",
                 ( pMethod != nullptr ) ? pMethod : "(unnamed)" );
    }

    // The one place -1 becomes a null pointer. Written once because all three
    // string answers share the convention and a second copy of "-1 means no
    // answer" is a second place for it to become "-1 means empty".
    static const char *Answer( const char *pBuffer, int nSize, int nWritten )
    {
        if ( nWritten < 0 || nWritten >= nSize )
            return nullptr;

        return pBuffer;
    }

    CreateInterfaceFn m_pEngineFactory = {};
    CreateInterfaceFn m_pPhysicsFactory = {};
    CreateInterfaceFn m_pFileSystemFactory = {};
    CGlobalVars *m_pGlobals = {};

    ServerClass *m_pServerClasses = {};

    // BY VALUE, with the pointer beside it. m_SendProxies is the storage the
    // engine is handed the address of; m_pSendProxies is what
    // GetStandardSendProxies returns, so the day the copy stops coming from
    // g_StandardSendProxies neither the getter nor its caller changes.
    CStandardSendProxies m_SendProxies = {};
    CStandardSendProxies *m_pSendProxies = {};

    // PART 6. The tick interval is read once at DLLInit and the three strings
    // are the buffers managed writes into -- the engine reads all three after
    // the call returns, so the storage has to be the shell's and has to outlive
    // it. 128 bytes each, matching SourceSharp_GetGameDescription's own buffer;
    // ListingBuffer truncates on a rune boundary rather than mid-sequence.
    float m_flTickInterval = DEFAULT_TICK_INTERVAL;
    char m_szBrowserMapOverride[ 128 ] = {};
    char m_szBrowserGameData[ 128 ] = {};
    char m_szMapChangeReason[ 128 ] = {};

    // PART 10. Status is a whole block rather than one answer, so it gets more
    // room than the browser strings: managed joins its lines with '\n' and the
    // shell splits them back apart in place.
    char m_szStatusLines[ 1024 ] = {};

    // GATE 5's RECORDER, and a field for exactly the reason the counter above
    // is one. CheckTransmit is a free function in another translation unit; the
    // rule says its state may not be a global, so it is a field of the object
    // the engine holds exactly one of and the call sites reach it through the
    // three free functions this file defines at the bottom. See
    // shell_steporder.h.
    CShellTransmitSet m_TransmitSet;

    CManagedHolders m_Managed = {};

    // T4's SWITCH AND ITS DELEGATE ARE GONE -- Phase 7a. See the note at the
    // top of this file for why they could not survive this phase.
    //
    // m_pSharedChangeInfo IS GONE too, and for a different reason. It captured
    // engine->GetSharedEdictChangeInfo() into private storage nothing ever
    // read; the load-bearing destination is the SDK's own g_pSharedChangeInfo,
    // which DLLInit assigns. See the comment there.

    IVEngineServer *m_pEngine = {};
    ICvar *m_pCVar = {};

    // THE TWO HEAP TIERS (server_state.h): DLLInit..DLLShutdown and
    // LevelInit..LevelShutdown. The root is the one static; these are not.
    CStateTier< CServerDllState > m_pDll;
    CStateTier< CServerLevelState > m_pLevel;

    // THE SHIM ROOT, by value (plan_server_native_v2 T6): the one native
    // object LoadSideV2 hands managed, beside SSFwd_ShimRoot, and every other
    // area is acquired from it. By value rather than in m_pDll because it is
    // stateless beyond its areas and managed may hold it for the DLL's life.
    CShimRootServer m_ShimRoot;
};

namespace
{
    CShellGameDll s_gameShellDll;
}

//=============================================================================
// GATE 5 -- THE TRANSMIT SET. plan_server.md's Gate 5, item C2.
//
// THE SEAM FIRST. shell_steporder.h declares these three and leaves them to
// whoever links it, exactly as shell_gamedll_refusals.h does with its warning:
// the shell defines them here against the singleton's field, and the gtest
// defines them against its own recorder. That is what lets shell_gameents.cpp
// record without being able to see CShellGameDll -- see the header for why a
// step that can see the game-DLL object is a step one edit away from being a
// decision.
//
// THESE ARE NOT #ifdef'd OUT OF A RELEASE BUILD, and that is deliberate. An
// instrument that exists only in a debug build measures a build nobody plays,
// and the release build is the one the probe reads. The cost disarmed is one
// predictable branch per CheckTransmit.
//
// THE DUMP THAT READS THEM IS DEBUG-ONLY since the 2026-10-01 ruling that
// diagnostic commands live in debug files: cs_sv_transmitset is in
// shell_gamedll_debug.cpp, so a probe that asks for it needs a debug build.
//=============================================================================
CManagedHolders &ShellGameDll_Managed()
{
    return s_gameShellDll.Managed();
}

CServerDllState *ShellGameDll_Dll()
{
    return s_gameShellDll.Dll();
}

CServerLevelState *ShellGameDll_Level()
{
    return s_gameShellDll.Level();
}

CShimRootServer &ShellGameDll_ShimRoot()
{
    return s_gameShellDll.ShimRoot();
}

bool ShellGameDll_TransmitCaptureBegin( int nRecipientEdict )
{
    return s_gameShellDll.TransmitSet().BeginCapture( nRecipientEdict );
}

void ShellGameDll_TransmitCaptureMark( int iEdict )
{
    s_gameShellDll.TransmitSet().Mark( iEdict );
}

void ShellGameDll_TransmitCaptureSkyboxArea( int nArea )
{
    s_gameShellDll.TransmitSet().SetSkyboxArea( nArea );
}

// The recorder itself, for cs_sv_transmitset -- the dump, a diagnostic that
// lives in shell_gamedll_debug.cpp, which only a debug build compiles.
// Declared in shell_gamedll_internal.h; the singleton stays this file's.
CShellTransmitSet &ShellGameDll_TransmitSet()
{
    return s_gameShellDll.TransmitSet();
}

//-----------------------------------------------------------------------------
// THE SWITCH (Phase 4). The engine now finds THIS object when it asks for
// "ServerGameDLL012", and stock's identical registration at
// gameinterface.cpp:630 is behind #ifndef SOURCESHARP_SHELL_GAMEDLL so there
// is still exactly one.
//
// That count is the whole gate and it is not decoration: two InterfaceReg
// entries for one version string do NOT collide at link time -- both link, and
// which one CreateInterface hands the engine is decided by
// static-initialisation order across translation units. A silent,
// nondeterministic wrong answer is worse than a link error, which is why this
// line sat commented out from Phase 1 to Phase 3 while the bodies were built.
//
// ONE LINE AND NOT SEVEN. Part 1's end state has this class grow four more
// bases and answer all five game-DLL interfaces from seven EXPOSE_ lines. That
// is deferred, and deliberately.
//
// THE HAZARD THIS PARAGRAPH USED TO NAME IS FIXED, and the correction is worth
// keeping: it said ServerGameTags001 had FOUR InterfaceReg entries, because its
// EXPOSE_SINGLE_INTERFACE sat in gameinterface.h and four translation units
// included it. Measured on the built server.so at plan_gamedll_after.md Phase
// A9 it was SIX, not four -- two more TUs had come to include that header --
// and step 2 moved the EXPOSE_ into sourcesharp/game/server/shell_gametags.cpp,
// where it is ONE.
//
// So the remaining hazard is the plain one: a SECOND registration of a version
// string something already answers. Duplicates do not collide at link time,
// they are resolved by static-initialisation order across translation units,
// which is a silent nondeterministic wrong answer rather than an error.
//
// So the gate this phase holds is per-interface DISTINCT REGISTERING
// IMPLEMENTATIONS, which `nm` can see and `strings | grep -c` cannot:
//
//   nm -C -a game/mod_sharp/bin/linux64/server.so \
//     | grep -oE '__g_Create[A-Za-z0-9_]+_reg'
//
// See shell_gamedll_registration_test.cpp, which holds that arrangement.
//-----------------------------------------------------------------------------
// Answered by the factory (shell_factory.cpp), which asks for it by this row.
static void *SourceSharp_GetServerGameDll() { return (IServerGameDLL *)&s_gameShellDll; }
const SourceSharpExposed kExposeServerGameDll = { INTERFACEVERSION_SERVERGAMEDLL, SourceSharp_GetServerGameDll };

// Stock's, carried here from gameinterface.cpp:412 at plan_gamedll_after.md
// Phase A9 step 3 when that file's CServerGameDLL was deleted. It belongs
// beside whichever class answers the version string, and that is this one.
//
// When bumping the version to this interface, check that our assumption is
// still valid and expose the older version in the same way.
COMPILE_TIME_ASSERT( INTERFACEVERSION_SERVERGAMEDLL_INT == 12 );