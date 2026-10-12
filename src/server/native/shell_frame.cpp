//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — IShimFrame, and the native frame steps behind it ===//
// SS_REASON a  a vtable the engine calls through
//
// plan_shim.md §5 3a. ONE FILE, where there were two: shell_framesteps.cpp held
// nineteen free functions and shell_frameshim.cpp held a class whose nineteen
// methods each called one of them by name. The delegation layer bought a
// signature check the compiler was already giving us on the vtable itself, and
// cost nineteen forward declarations, nineteen one-line bodies and a second
// file header. The bodies live in the methods now.
//
// The rule the steps obey is unchanged, and it is plan_server.md Part 4's:
//
//     Native exposes one irreducible step per slot. C# owns the order, the
//     conditions and the early returns. A STEP IS NEVER A DECISION.
//
// So there is no SourceSharp_GameFrame() here. Each method is one thing
// CServerGameDLL::GameFrame (gameinterface.cpp:1319-1418) or ::PreClientUpdate
// (:1424-1442) does, and the ORDER of them is in ServerGameDll.cs.
//
//=============================================================================
// WHERE THE MANAGED FRAME ACTUALLY RUNS, because it is NOT a line in that order
// and plan_gamedll.md Part 4's sketch says it is.
//
// Part 4 writes the frame as
//
//     Entities.CleanupDeleteList();
//     _frames.PreThink(frameTime);          <-- inline
//     Physics.RunThinkFunctions(simulating);
//     _frames.Think(frameTime);             <-- inline
//     ...
//
// MEASURED, that is wrong, and it is wrong in a way that changes behaviour
// rather than only reading order. The three managed points are game-system
// stages in stock: FramePreThink is CSourceSharpSystem::FrameUpdatePreEntityThink
// and Frame is its FrameUpdatePostEntityThink, both of which run INSIDE
// IGameSystem's per-frame passes at that object's position in the list. Hoisting
// them out and calling them around the passes moves the managed frame
//
//   * ahead of CSoundEnt in the pre pass, and
//   * ahead of CRagdollLRURetirement and CEntityListSystem in the post pass.
//
// None of those three is a no-op. So the two passes below are the steps, the
// managed frame is a ROW of kServerSystems[] inside them, and what C# owns is
// the order AROUND the passes -- which is exactly what stock's GameFrame owns.
//
// The THIRD managed point is different and stays where it is: FramePostThink is
// reached from CSourceSharpGameRules::EndGameFrame, which is the EndGameFrame
// method below, because that is where stock reaches it from too.
//
//=============================================================================
// WHAT IS NOT HERE
//
//   `if ( g_InRestore ) return;` -- GameFrame's first line. Nothing in a
//   multiplayer build sets g_InRestore; it is the save cluster's flag and this
//   build's engine answers "Can't save multiplayer games" to everything that
//   could set it. plan_gamedll.md Part 11c #9. Dead code that reads as live.
//
//   GameStartFrame() -- this build's is shell_server.cpp:94, a verbosity-gated
//   SOURCESHARP_STUB(). A step whose whole body is a stub is a crossing that
//   buys nothing.
//
//   TheNavMesh->Update() and TheNextBots().Update() -- USE_NAV_MESH and NEXT_BOT
//   are defined nowhere in this build. Grepped, not assumed.
//
//   UpdateQueryCache and g_pServerBenchmark->UpdateBenchmark -- both already
//   `#ifndef SOURCESHARP` in stock, gone with querycache.cpp and
//   serverbenchmark_base.cpp.
//
//   trace_report -- a debug readout behind a convar, and one that reads THREE
//   engine trace counters and prints them. It is a diagnostic with a condition
//   in it, so by Part 4's rule it is not a step; nothing has asked for it since
//   the port began and it is left out rather than carried inert.
//
//   g_bUseNetworkVars = s_UseNetworkVars.GetBool() -- `#ifdef _DEBUG` profiling
//   support for a manual-mode switch this port does not use.
//
//   DrawAllDebugOverlays IS carried, because it is what makes every
//   NDebugOverlay call in managed code visible, and it is NOT behind _DEBUG in
//   stock -- stock's own comment says "allow this in release for now".
//
//=============================================================================
// FOUR RETIRED SLOTS, KEPT AT THEIR INDICES.
//
// PhysicsRunThinkFunctions, ServiceEventQueue, FireNetworkPropertyEvents and
// DeliverToSdkEntities do nothing and can do nothing: each one's subject left
// the vpc. They are marked retired in ishim_frame.h and in the generated
// managed layout, their managed callers are gone, and the full argument for
// each is a row in docs/abi-retirements.md. THE SLOTS THEMSELVES STAY: a vtable
// slot removed from the middle renumbers every slot after it, managed binds by
// index, and the span check does not catch a reorder. They go together, in one
// deliberate pass, at a SourceSharpFrame002 version string.
//
// The refusal objects that used to warn from these four bodies are gone with
// the bodies. They were reach counters for an argument nothing can now reach:
// with no managed caller the slot is not entered at all, so a counter on it
// measures the binding rather than the claim.
//
//=============================================================================
// THE ONE FILE-SCOPE OBJECT IN THIS PORT'S C++, AND IT IS ARGUED RATHER THAN
// SLIPPED IN.
//
// CLAUDE.md's standing rule is no file-scope statics or globals: state goes in
// a class as fields. tools/statics-audit and tools/shell-globals-audit both
// gate it, and this file adds one symbol to each of their baselines.
//
// What it adds is not state. CShimFrame has NO FIELDS -- the object exists
// solely so that there is an address whose first word is a vtable pointer,
// which is what a bound call needs and the only thing it needs. This is exactly
// the shape the engine's own EXPOSE_SINGLE_INTERFACE_GLOBALVAR has
// (interface.h registers one file-scope static per version string), and the
// whole design of this phase is that our factory answers the same way the
// engine's does.
//
// The alternatives were weighed and are worse. A function-local static emits
// the same object PLUS a thread-safe-init guard variable -- two symbols where
// this is one -- and hides the object from the source-level audit while leaving
// it in the emitted one, which is the sort of half-visibility a rule with a
// gate exists to prevent. An anonymous namespace would drop the word `static`
// and change nothing else, which is a dodge by spelling. Putting the file
// somewhere shell-globals-audit does not sweep would be a dodge by filename.
//
// So it is spelled plainly, baselined deliberately, and said out loud here.
//
//-----------------------------------------------------------------------------
// THE ENTRY COUNT COMES FROM THE COMPILER, NOT FROM COUNTING.
//
// `VTableHook::MemberOf( &IShimFrame::<the last member> ).slot + 1` reads the
// slot out of a pointer-to-member-function, which under the Itanium ABI carries
// it directly. So the number is whatever the compiler decided when it compiled
// ishim_frame.h, and "count the virtual keywords" never happens. That matters
// most where it is least visible -- this interface has no virtual destructor,
// and one appearing would take the first TWO slots and move everything.
//
// Managed holds that number against sizeof(ShimFrameVtbl)/sizeof(void*) and
// requires managed <= native. Why that relation and not equality is argued in
// ishim_frame.h: appending to a C++ class moves nothing, so a rebuilt
// server.so that gained shim methods still satisfies it and every existing
// binding is still right, while a stale-newer managed assembly expecting more
// than native has is refused by name.
//
//=============================================================================//

#include "eiface.h"		// IUploadGameStats, CGlobalVars
#include "igamerules.h"
#include "ishim_frame.h"
#include "shell_server.h"
#include "shell_standalone_player.h"
#include "vtable_hook.h"


// The parallel hierarchy's per-frame drive, declared where it is used rather
// than given a header whose only reader would be this file -- the same shape
// shell_standalone.cpp itself uses for it. Defined in shell_standalone_base.cpp.
void StandaloneEntities_Update();
void StandaloneEntities_Finish();

// Declared exactly the way stock's GameFrame declares them -- as function-local
// externs, because none of the three has a header (gameinterface.cpp:1347-1349).

// gameinterface.cpp:1057, outside the class. shell_level.cpp names it too;
// it is the rollback-independent server clock.
extern float g_flServerCurTime;

// client.cpp / player.cpp's, reached by stock's GameFrame without a header of
// its own either.
void UpdateAllClientData( void );

// gameinterface.cpp:155. Stock's global, acquired by
// SourceSharp_AcquireEngineInterfaces on the shell path.
extern IUploadGameStats *gamestatsuploader;

// The engine's globals (connected in shell_engineboundary.cpp).
extern CGlobalVars *gpGlobals;

// entity_shared_statics.h:121, defined at baseentity_shared.cpp:2763.
bool EntitySim_IsSimulatingOnAlternateTicks( void );

// gameinterface.cpp:468. NOT behind _DEBUG -- stock's own comment beside the
// call is "allow this in release for now".
void DrawAllDebugOverlays( void );

namespace
{

class CShimFrame : public IShimFrame
{
public:
	// NO FIELDS, AND NONE IS COMING. The moment one appears, this object stops
	// being a vtable with an address and becomes state at file scope, which is
	// the thing the rule above is about.

	//-------------------------------------------------------------------------
	// STEP 1 -- gameinterface.cpp:1326. Whether this server runs simulation on
	// every other tick.
	//
	// A QUESTION, not a decision: the step answers it and C# does the
	// branching, which is the only way the alternate-tick arm can be read as
	// the four lines it is rather than as a native early return.
	//-------------------------------------------------------------------------
	virtual int32_t IsSimulatingOnAlternateTicks() OVERRIDE
	{
		return EntitySim_IsSimulatingOnAlternateTicks() ? 1 : 0;
	}

	//-------------------------------------------------------------------------
	// STEP 2 -- gpGlobals->tickcount, for the `tickcount & 1` test above.
	//
	// A step rather than a Globals read, and that is measured: IEngineEnvironment
	// is registered from its parameterless constructor, so Globals.IsAvailable
	// answers FALSE on this path and every property on it throws. Wiring the
	// environment through is Phase 5's unfinished business and not this
	// phase's.
	//-------------------------------------------------------------------------
	virtual int32_t TickCount() OVERRIDE
	{
		return gpGlobals->tickcount;
	}

	//-------------------------------------------------------------------------
	// STEPS 3 and 4 -- gpGlobals->frametime, read and written.
	//
	// A PAIR, because stock's body is a pair: it doubles frametime for an
	// alternate tick, saves the old value, runs the frame and puts the old
	// value back. Two steps rather than a ScaleFrameTime and a RestoreFrameTime,
	// so that the arithmetic -- the `* 2.0f` -- is in C# where the
	// transliteration can be read against stock line for line.
	//-------------------------------------------------------------------------
	virtual float GetFrameTime() OVERRIDE
	{
		return gpGlobals->frametime;
	}

	virtual void SetFrameTime( float flFrameTime ) OVERRIDE
	{
		gpGlobals->frametime = flFrameTime;
	}

	//-------------------------------------------------------------------------
	// STEP 5 -- gameinterface.cpp:1339. The clock CBaseProjectile's
	// collide-with-teammates think reads, which is not affected by rollback.
	//-------------------------------------------------------------------------
	virtual void SetServerCurTime() OVERRIDE
	{
		g_flServerCurTime = gpGlobals->curtime;
	}

	//-------------------------------------------------------------------------
	// STEPS 6 and 7 -- gameinterface.cpp:1355 and :1379. The named list's two
	// per-frame passes, forwards.
	//
	// THE MANAGED FRAME IS INSIDE THESE. See the header comment:
	// SourceSharpManaged is a row of kServerSystems[] at the position
	// CSourceSharpSystem held, so FramePreThink runs in the first and Frame in
	// the second, each with the same native neighbours before and after it that
	// stock gives them.
	//-------------------------------------------------------------------------
	virtual void ServerSystemsPreThink() OVERRIDE
	{
		SourceSharp_ServerSystemsFramePreThink();
	}

	//-------------------------------------------------------------------------
	// RETIRED SLOT -- stock's step 8, every entity's think function.
	//
	// Physics_RunThinkFunctions is physics_main.cpp's and that file left the
	// vpc; the SimThink list it walks is defined only there and in
	// entitylist.cpp, both out; and under the entity alias there is no native
	// entity object that could be on the list. The thinks are managed:
	// ThinkScheduler drains through EntityDirectory.RunFrame, driven from
	// StandaloneHierarchy's per-frame hook on the NEXT step,
	// ServerSystemsPostThink (plan_entity.md §3.4, step 1g). The one non-think
	// effect, the deferred-delete cleanup, already ran a step earlier from
	// managed code.
	//
	// The slot keeps its index. See the retired-slot note in the header.
	//-------------------------------------------------------------------------
	virtual void PhysicsRunThinkFunctions( int32_t bSimulating ) OVERRIDE
	{
		(void)bSimulating;
	}

	virtual void ServerSystemsPostThink() OVERRIDE
	{
		SourceSharp_ServerSystemsFramePostThink();
	}

	//-------------------------------------------------------------------------
	// RETIRED SLOT -- stock's step 9, "service event queue, firing off any
	// actions whos time has come".
	//
	// The native queue is defined in cbase.cpp, which left the vpc, and so did
	// every producer that posted into it -- the ent_fire console command in
	// baseentity.cpp and the output-firing path in entityoutput.cpp, neither TU
	// in the armed build. No armed server TU adds an event, so there is nothing
	// to service. The entity I/O queue armed is the managed EntityIoQueue and
	// C# services it at stock's own position in GameFrame.
	//-------------------------------------------------------------------------
	virtual void ServiceEventQueue() OVERRIDE
	{
	}

	//-------------------------------------------------------------------------
	// STEP 10 -- gameinterface.cpp:1389. Stock's own comment is "FIXME: Should
	// this only occur on the final tick?", carried across unanswered: this is a
	// transliteration, and answering an open question in it would be a change
	// disguised as a port.
	//-------------------------------------------------------------------------
	virtual void UpdateAllClientData() OVERRIDE
	{
		::UpdateAllClientData();
	}

	//-------------------------------------------------------------------------
	// STEP 11 -- gameinterface.cpp:1391-1394. The rules' end of frame, which is
	// where the THIRD managed frame point lives (CSourceSharpGameRules::
	// EndGameFrame -> FramePostThink) along with the late entity-slot drain.
	//
	// The null test is stock's and stays native, because it is not a policy: it
	// is the fact that g_pGameRules does not exist between levels. A C# caller
	// would need a second step to ask, and the answer would be stale by the
	// time it acted on it.
	//-------------------------------------------------------------------------
	virtual void EndGameFrame() OVERRIDE
	{
		if ( g_pGameRules )
			g_pGameRules->EndGameFrame();
	}

	//-------------------------------------------------------------------------
	// RETIRED SLOT -- stock's step 12, "Any entities that detect network state
	// changes on a timer do it here."
	//
	// NO ENTITY DOES, AND NONE COULD, and the body has been empty since two
	// lanes met in it: plan_shim.md Phase 1d deleted g_NetworkPropertyEventMgr
	// with game/server/timedeventmgr.cpp -- CServerNetworkProperty::
	// SetUpdateInterval was the only thing that could register an entity with
	// that manager and it had no caller in any compiled TU -- and 1c-sv deleted
	// ShellGameDll_RecordStep with the step-order ring. Each side of that merge
	// had removed the other's only statement.
	//
	// THIS ONE IS THE LANE'S OWN FINDING: plan_shim.md §5 3a names THREE frame
	// slots to retire and this is a fourth. An empty body with no work left in
	// it is the same claim the other three make.
	//-------------------------------------------------------------------------
	virtual void FireNetworkPropertyEvents() OVERRIDE
	{
	}

	//-------------------------------------------------------------------------
	// STEP 13 -- gameinterface.cpp:1366. The stats uploader's connection.
	//-------------------------------------------------------------------------
	virtual void GamestatsUpdateConnection() OVERRIDE
	{
		gamestatsuploader->UpdateConnection();
	}

	//-------------------------------------------------------------------------
	// STEPS 14 and 15 -- PreClientUpdate, gameinterface.cpp:1436 and :1440.
	//
	// `if ( !simulating ) return;` is stock's first line and it is a CONDITION,
	// so it stays in C#. It is also already in the shell today, which is where
	// it came from -- see CShellGameDll::PreClientUpdate.
	//-------------------------------------------------------------------------
	virtual void DrawAllDebugOverlays() OVERRIDE
	{
		::DrawAllDebugOverlays();
	}

	virtual void ServerSystemsPreClientUpdate() OVERRIDE
	{
		SourceSharp_ServerSystemsPreClientUpdate();
	}

	//-------------------------------------------------------------------------
	// THE PARALLEL HIERARCHY'S THREE POST-THINK STEPS -- plan_gamedll_after.md
	// Phase A3, the fifth "C" system.
	//
	// These were one game system stage: CSourceSharpStandaloneSystem's
	// FrameUpdatePostEntityThink, a row of kServerSystems[] immediately before
	// the SourceSharpManaged row. They are steps now, driven by the
	// StandaloneHierarchy participant at FrameStage.Standalone -- which runs
	// FIRST at OnThink, i.e. at the very start of the managed row's body, which
	// is where index 11 sat with nothing between it and index 12.
	//
	// SPLIT BECAUSE THE ORDER BETWEEN THEM WAS A COMMENT. The row ran the
	// player commands and then the entity thinks, and said why in C++:
	// "Players first, so an entity's think this tick sees where the players
	// moved this tick". That is a decision, and IShimFrame's rule is that a
	// step is never one -- so the calls are separate steps and the decision is
	// in C#, beside every other frame ordering this build makes.
	//
	// THE WORK ITSELF IS NATIVE AND STAYS NATIVE. RunAllQueuedCommands replays
	// CUserCmds and saves and restores gpGlobals->curtime around them;
	// StandaloneEntities_Update walks a CUtlVector of native shells and updates
	// touch links, movement and flight; StandaloneEntities_Finish composes the
	// attachment-parented children and drains the deferred deletes. This is the
	// floor the managed hierarchy stands on, not logic with a managed half.
	//
	// UPDATE AND FINISH ARE TWO AND NOT ONE, because the DUE THINKS run between
	// them and they are managed now (plan_entity.md §3.4, step 1g). The order
	// around the thinks -- touch links before, hierarchy compose and deletes
	// after -- is what the split exists to keep, and shell_standalone_base.cpp
	// says why each half is on the side it is on.
	//-------------------------------------------------------------------------
	virtual void StandalonePlayerCommands() OVERRIDE
	{
		CStandalonePlayer::RunAllQueuedCommands();
	}

	virtual void StandaloneUpdate() OVERRIDE
	{
		StandaloneEntities_Update();
	}

	virtual void StandaloneFinish() OVERRIDE
	{
		StandaloneEntities_Finish();
	}

	//-------------------------------------------------------------------------
	// RETIRED SLOT -- the SDK hierarchy's half of one entity I/O delivery.
	//
	// This leaf existed to reach four stock classes the managed EntityDirectory
	// does not hold: the dissolver, the two ragdoll kinds and the physics
	// solver. Every one of their defining translation units is absent from the
	// armed build, so the hierarchy it walked has no members at all, and the
	// pair of passes it called left with cbase.cpp and have no other armed
	// caller. The managed directory is the whole population now, and it already
	// warns once per distinct target and input that nothing took it.
	//-------------------------------------------------------------------------
	virtual int32_t DeliverToSdkEntities( const char *pTarget, const char *pInput,
	                                      const char *pValue ) OVERRIDE
	{
		(void)pTarget; (void)pInput; (void)pValue;
		return 0;
	}
};

} // namespace

// The one object. See the header comment for why it is spelled this way and
// what was rejected.
static CShimFrame g_ShimFrame;

//-----------------------------------------------------------------------------
// The internalFactory's row for this area: the pointer and the compiler's own
// count for the table behind it.
//
// **THE NAME BELOW MUST BE THE LAST DECLARATION IN IShimFrame.** The
// append-only rule is what makes naming the last one sufficient; a method added
// after it and NOT named here reports a count one short, and managed then
// refuses the bind -- correctly and loudly, once per frame, from inside
// GameFrame.
//
// THAT IS NOT HYPOTHETICAL. The sub-ioqueue lane appended
// DeliverToSdkEntities, left this line naming StandaloneFinish, and shipped a
// game whose every server frame threw
//
//     native says "SourceSharpFrame001" has 18 vtable entries and
//     ShimFrameVtbl declares 19
//
// -- with `make check` GREEN, both slot-count facts green, and the whole
// managed frame dead. A live run is what found it. So it is now also held by a
// test: VtableAcquireCount.ShimFrameAcquireNamesTheLastMember reads this file
// and the committed layout and requires the two to name the same member.
//-----------------------------------------------------------------------------
void *ShimFrame_Acquire( int32_t *pnEntries )
{
	if ( pnEntries )
		*pnEntries = VTableHook::MemberOf( &IShimFrame::DeliverToSdkEntities ).slot + 1;

	return static_cast< IShimFrame * >( &g_ShimFrame );
}
