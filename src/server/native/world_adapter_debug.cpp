//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — world_adapter.cpp's diagnostic console commands ===//
// SS_REASON c  static-init registration
//
// DEBUG BUILDS ONLY (user ruling 2026-10-01: debug commands live in debug files
// that CMake compiles only for a debug build -- server_sourcesharp_debug_sources).
// SourceSharp.Server's NativeCommands declares the matching rows under #if DEBUG.
//
//=============================================================================//

#include "ss_util.h"            // ss::UTIL_TraceLine, UTIL_ImpactTrace
#include "ss_shareddefs.h"      // ss::kDmgBullet
#include "ss_world.h"           // CWorld, GetWorldEntity

// cs_sv_trace_subject: the three arms, the two hierarchies, and what the
// movement loop would call the ground -- all printed off ONE real trace, which
// is the only way the plan's "shown, not asserted" gate can be met. slot_table.h
// is its slot-table arm (plan_entity.md 3.3): the resolver that turns a trace
// result into a shell without casting it.
#include "slot_table.h"
#include "anyentity.h"
#include "movement_ground.h"
#include "world_subject.h"
#include "shell_standalone_player.h"
#include "native_command_body.h"
#include "trace_hit.h"   // ss::TraceHit: the IHandleEntity the engine stored in trace_t::m_pEnt

extern CGlobalVars *gpGlobals;

//-----------------------------------------------------------------------------
// The world's networked state, ON DEMAND.
//
// CWorld::Spawn prints it once per level load, which is the evidence that the
// map's keys landed. This is the other question: what does the world hold NOW.
// They are different questions the moment anything writes a key at runtime --
// `ent_fire worldspawn AddOutput "detailmaterial x"` reaches CWorld::KeyValue
// exactly as the map parse does -- and the load-time line cannot answer it.
//
// The client's half of this needs no command: C_World::OnDataChanged prints
// whenever the ten values CHANGE, which is once at the create and then only
// when something moved them. That asymmetry is not an oversight -- a console
// command delivered through engine->ServerCommand reaches a SERVER concommand
// and not a client one (docs/gamedll-ledger.md Phase A6 row 1, measured three
// ways in one process), so a cs_cl_ command here would be a gate nobody could
// run headlessly.
//-----------------------------------------------------------------------------
SS_NATIVE_COMMAND( cs_sv_world )
{
	CWorld *pWorld = GetWorldEntity();

	if ( pWorld == NULL )
	{
		Msg( "[SourceSharp] no world entity -- is a map loaded?\n" );
		return;
	}

	pWorld->ReportNetState();
}

//-----------------------------------------------------------------------------
// WHICH OF THE THREE HIERARCHIES A TRACE CAME BACK WITH -- shown, not asserted.
//
// plan_cworld_trace.md's T3 gate is that "a trace against level geometry still
// answers the world", DEMONSTRATED rather than claimed, and that the failure
// mode is not a crash: it is bullets and props passing through walls, which a
// green test suite cannot see and a screenshot barely can.
//
// So this prints, for one real trace, every answer the game has about what it
// hit -- the raw handle, the trace's own DidHitWorld, the third arm, both
// hierarchy arms, all three arms of AnyEntity_t, and what the movement loop
// would call the ground. Whatever changes, the disagreement between two columns
// is visible in one line.
//
// TRACED STRAIGHT DOWN, AND THAT IS THE POINT. The downward trace from a
// standing player is the exact query MovementGroundFromTrace serves and the
// exact one that decides whether the game has floors; a forward trace merely
// looks convincing. Both are printed, because a wall and a floor are different
// pieces of level geometry and stopping on one is not evidence about the other.
//
// A READ. Traces and prints, changes nothing, safe at sv_cheats 0.
//-----------------------------------------------------------------------------
static void ReportOneTraceSubject( const char *pszWhat, const Vector &vecStart,
                                   const Vector &vecEnd,
                                   const IHandleEntity *pIgnore )
{
	// THE IGNORE ARGUMENT IS NOT DECORATION. The first run of this command
	// passed NULL and every one of its five traces came back at fraction 0.000
	// having hit "ss_player" -- the trace starts inside the player's own hull,
	// so without it the command measures the player and never reaches a wall.
	// It looked like a working answer, which is the only reason it is worth a
	// comment.
	trace_t tr;
	ss::UTIL_TraceLine( vecStart, vecEnd, MASK_SOLID, pIgnore, COLLISION_GROUP_NONE, &tr );

	const AnyEntity_t ent = AnyEntityOf( ss::TraceHit( tr ) );

	Msg( "[SourceSharp] %-16s fraction %.3f  m_pEnt %p\n", pszWhat, tr.fraction,
	     (void *)tr.m_pEnt );

	Msg( "[SourceSharp]     trace says     DidHitWorld %-5s  DidHitNonWorldEntity %s\n",
	     tr.DidHitWorld() ? "YES" : "no",
	     tr.DidHitNonWorldEntity() ? "YES" : "no" );

	Msg( "[SourceSharp]     third arm      SourceSharp_IsTheWorld %s\n",
	     SourceSharp_IsTheWorld( ss::TraceHit( tr ) ) ? "YES" : "no" );

	// TraceGameEntity IS GONE FROM THIS LINE, and what took its place is the
	// point of the print rather than a tidier spelling of it.
	//
	// The removed arm was util_shared.h's C-style cast of tr.m_pEnt to the
	// server-entity interface --
	// section 3.16's cast family 3, which asserts a type over whatever the
	// engine put in the trace and cannot tell "not one of ours" from "one of
	// ours". SourceSharp_ShellFromHandleEntity asks the object for its handle
	// through a virtual it really has and indexes the slot table, so a static
	// prop, a detail model or a recycled entry all answer NULL instead of
	// answering with a pointer.
	//
	// Printed BESIDE the SDK arm on purpose: this command exists because the
	// two arms coming apart is the failure it hunts, and the slot table is now
	// one of the arms. A NULL here beside a non-NULL TraceEntity is an SDK
	// entity; NULL in both with a non-NULL m_pEnt above is something the game
	// does not own; and a non-NULL here at all is only possible once step 1e
	// binds the table, which is worth being able to see.
	Msg( "[SourceSharp]     two arms       TraceGameEntity %p   ShellFromHandleEntity %p\n",
	     (void *)ss::TraceGameEntity( tr ),
	     (void *)SourceSharp_ShellFromHandleEntity( ss::TraceHit( tr ) ) );

	Msg( "[SourceSharp]     AnyEntity_t    pBase %p  pGame %p  pWorld %p  "
	     "valid %s  \"%s\"\n",
	     (void *)ent.pBase, (void *)ent.pGame, (void *)ent.pWorld,
	     ent.IsValid() ? "YES" : "NO", ent.GetClassname() );

	// The one that decides whether the game has floors. Printed beside the
	// arms rather than derived from them, because the whole failure this
	// command exists to catch is those two coming apart.
	Msg( "[SourceSharp]     ground         MovementGroundFromTrace %p  (%s)\n",
	     (void *)MovementGroundFromTrace( tr ),
	     MovementGroundFromTrace( tr ) ? "something to stand on"
	                                   : "NOTHING TO STAND ON" );

	// AND WHETHER A BULLET HOLE WOULD LAND ON IT. UTIL_ImpactTrace is what
	// every shot in the game reaches for a decal and its flecks, and its
	// failure mode is a silent return -- walls that stop taking bullet holes,
	// which reads as a renderer bug rather than as a resolver one. So the
	// impact is actually RUN here and the world's own counter is read either
	// side of it: a number that goes up is the proof, and a number that does
	// not is the defect.
	CWorld *pWorld = GetWorldEntity();
	const int nBefore = pWorld ? pWorld->ImpactCount() : -1;

	UTIL_ImpactTrace( &tr, ss::kDmgBullet, NULL );

	const int nAfter = pWorld ? pWorld->ImpactCount() : -1;

	Msg( "[SourceSharp]     decal          UTIL_ImpactTrace: world impacts %d -> %d  (%s)\n",
	     nBefore, nAfter,
	     ( nAfter > nBefore ) ? "the world took the hit"
	                          : ( tr.DidHitWorld() ? "DID NOT REACH THE WORLD"
	                                               : "not a world hit" ) );
}

SS_NATIVE_COMMAND( cs_sv_trace_subject )
{
	CWorld *pWorld = GetWorldEntity();

	Msg( "[SourceSharp] the world is %p (GetWorldEntity), %p as an IHandleEntity\n",
	     (void *)pWorld,
	     (void *)static_cast< IHandleEntity * >( pWorld ) );

	// From every connected player, which is where a trace against level
	// geometry actually happens in a game.
	int nTraced = 0;
	for ( int i = 1; i <= gpGlobals->maxClients; i++ )
	{
		CStandalonePlayer *pPlayer = CStandalonePlayer::FromIndex( i );
		if ( !pPlayer )
			continue;

		const Vector vecEye = pPlayer->EyePosition();

		Msg( "[SourceSharp] player slot %d at (%.0f %.0f %.0f):\n",
		     i, vecEye.x, vecEye.y, vecEye.z );

		const IHandleEntity *pSelf = pPlayer;

		ReportOneTraceSubject( "straight down", vecEye,
		                       vecEye + Vector( 0, 0, -4096 ), pSelf );
		ReportOneTraceSubject( "+X", vecEye, vecEye + Vector( 4096, 0, 0 ), pSelf );
		ReportOneTraceSubject( "-X", vecEye, vecEye + Vector( -4096, 0, 0 ), pSelf );
		ReportOneTraceSubject( "+Y", vecEye, vecEye + Vector( 0, 4096, 0 ), pSelf );
		ReportOneTraceSubject( "-Y", vecEye, vecEye + Vector( 0, -4096, 0 ), pSelf );
		nTraced++;
	}

	if ( nTraced == 0 )
	{
		// No player, so no eye to trace from. The world's own bounds still
		// name somewhere inside the map -- a trace with no subject at all
		// would be a command that always answers and never measures.
		Vector vecMins, vecMaxs;
		if ( pWorld )
			pWorld->GetWorldBounds( vecMins, vecMaxs );

		const Vector vecMid = ( vecMins + vecMaxs ) * 0.5f;
		Msg( "[SourceSharp] no players; tracing from the middle of the world "
		     "bounds (%.0f %.0f %.0f)\n", vecMid.x, vecMid.y, vecMid.z );

		ReportOneTraceSubject( "straight down", vecMid,
		                       Vector( vecMid.x, vecMid.y, vecMins.z - 64 ), NULL );
	}
}
