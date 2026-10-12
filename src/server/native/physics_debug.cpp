//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — physics.cpp's diagnostic console commands ===========//
// SS_REASON c  static-init registration
//
// DEBUG BUILDS ONLY (user ruling 2026-10-01: debug commands live in debug files
// that CMake compiles only for a debug build -- server_sourcesharp_debug_sources).
// SourceSharp.Server's NativeCommands declares the matching rows under #if DEBUG.
//
// Six commands, moved out of physics.cpp with every helper only they used:
// three of lane 3f-shadow's SS-PHYSWORLD instruments (ss_physworld_recheck,
// _bounds, _object) and three
// stock debug commands (physics_highlight_active, physics_report_active,
// physics_select). The event ring's report, cs_sv_physics_shell, went with the
// ring (2.3). ss_physworld_report, physics_budget and air_density are
// managed's (plan_physics_bones_native_zero 2.3: PhysWorldReport,
// PhysicsBudget, AirDensityCommand), and so is the report's per-pid file.
// SOURCESHARP_PHYSICS_SHELL_ENGINE is NOT defined here: everything these use
// from physics_shell.h is above that guard.
// docs/physics-move-scope.md records the move.
//
//=============================================================================//

#include "ss_physics.h"
#include "ss_physics_shared.h"    // physenv, physcollision
#include "physicsowner.h"
#include "physics_bridge_refusal.h"
#include "standalone_relays.h"     // StandaloneEntity_PhysicsObject
#include "ss_util.h"
#include "ss_shareddefs.h"        // ss::kMaxPhysicsObjectList
#include "coordsize.h"
#include "bspfile.h"               // MAX_MAP_DISPINFO
#include "engine/ivmodelinfo.h"
#include "utlvector.h"
#include "physics_shell.h"         // the engine-free half only
// Which objects ss_physworld_object reports on, and why the obvious spelling of
// "all of them" is a trap. Header-only so physworld_match_rule_test can compile
// the rule without an engine.
#include "physworld_match_rule.h"
#include "native_command_body.h"
#include "ss_string.h"

extern IVModelInfo *modelinfo;

//-----------------------------------------------------------------------------
// physics.cpp's two entity-to-object helpers, which stay there (static, and
// pinned by cpp_alias_t1_test as that file's answer to igameentity.h's
// "VPhysicsGetObject never crosses"). MarkVPhysicsDebug below asks the same
// relay the same way (PhysForceEntityToSleep went back to physics.cpp for the
// penetrate ledger, physics stock parity H); the block above
// them in physics.cpp says why a list of one is the whole list.
//-----------------------------------------------------------------------------
static inline IPhysicsObject *EntityPhysicsObject( IGameEntity *pEntity )
{
	return pEntity ? StandaloneEntity_PhysicsObject( pEntity ) : NULL;
}

//-----------------------------------------------------------------------------
// The SS-PHYSWORLD sink: the console and a per-pid file, so parallel probes
// each read their own. The report itself (ss_physworld_report) is managed's
// PhysWorldReport since plan_physics_bones_native_zero 2.3, writing the same
// file; these three commands still write through this.
//-----------------------------------------------------------------------------
static void PhysWorldReportEmit( const char *pText )
{
	Msg( "%s", pText );

	char szPath[ 256 ];
	ss::Format( szPath, sizeof( szPath ), "/tmp/ss-physworld-%d.log", (int)getpid() );

	FILE *fp = fopen( szPath, "a" );
	if ( !fp )
		return;
	fputs( pText, fp );
	fclose( fp );
}

//-----------------------------------------------------------------------------
// THE EXPERIMENT THE REPORT CANNOT DO BY READING.
//
// IPhysicsCollisionSolver::ShouldCollide is not asked per step: vphysics asks
// it when a pair comes into proximity and CACHES the answer until something
// calls IPhysicsObject::RecheckCollisionFilter. So a pair that was refused ONCE
// -- at a moment one side's entity had not finished being set up, say -- stays
// refused for the object's whole life, while every later reading of the rule
// table says the two should collide. The report cannot tell those apart, and
// this can: if an object that is falling through the world starts landing the
// moment its filter is re-asked, the rule was never the problem and the CACHE
// was.
//
// DEBT with the rest of this block, and the same removal owner.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// "IF VPHYSICS ASKED RIGHT NOW, WOULD THIS OBJECT COLLIDE WITH THE WORLD?"
//
// The one question the parallel hierarchy cannot ask for itself: the rule table
// and the world's own side both live behind SOURCESHARP_PHYSICS_SHELL_ENGINE,
// which only this file defines. shell_standalone_base.cpp calls it at the one
// instant that matters -- the moment a prop's physics object enters the solver,
// before the entity has been made solid -- because ivp CACHES this answer per
// pair and never asks again.
//
// -1 when there is nothing to compare against. DEBT with the rest of the block.
//-----------------------------------------------------------------------------
SS_NATIVE_COMMAND( ss_physworld_recheck )
{
	if ( !physenv )
	{
		Msg( "[SS-PHYSWORLD] no environment\n" );
		return;
	}

	int nCount = 0;
	const IPhysicsObject **ppList = physenv->GetObjectList( &nCount );
	int nAsked = 0;

	for ( int i = 0; ppList && i < nCount; i++ )
	{
		IPhysicsObject *pObject = const_cast< IPhysicsObject * >( ppList[ i ] );
		if ( !pObject || pObject->IsStatic() )
			continue;
		pObject->RecheckCollisionFilter();
		nAsked++;
	}

	char szLine[ 128 ];
	ss::Format( szLine, sizeof( szLine ),
	            "[SS-PHYSWORLD-RECHECK] re-asked the filter for %d moveable object(s)\n",
	            nAsked );
	PhysWorldReportEmit( szLine );
}

//-----------------------------------------------------------------------------
// IS ANYTHING IN THE ENVIRONMENT SOMEWHERE A MAP CANNOT BE?
//
// Public API only -- GetObjectList, GetPosition, GetName -- so it has nothing
// to go stale against when vphysics changes. It is here rather than in
// physics_shell.h because it needs `physenv`. A NaN or infinite float becomes
// 0x80000000 when x86 converts it to a 32-bit integer, so one object at a
// non-finite position is enough to make vphysics' broadphase enclose a point
// about 2^31 cells away; this walk names the object.
//
// MAX_COORD_INTEGER is 16384; a Source map cannot legally hold anything past
// it, so twice that is a wide, unambiguous margin.
//-----------------------------------------------------------------------------
static void SourceSharpReportObjectsOutOfBounds()
{
	const float flLimit = 2.0f * MAX_COORD_INTEGER;

	int nCount = 0;
	const IPhysicsObject **ppList = physenv->GetObjectList( &nCount );

	int nBad = 0;
	int nPrinted = 0;
	char szLine[ 512 ];

	// THE DISPLACEMENT TERRAIN'S COLLIDES, which CollideGetAABB must never see:
	// vphysics segfaults on a virtual mesh there (every power <= 3 displacement
	// map, at level init). Collected from the same
	// source PhysCreateVirtualTerrain builds them from, and matched by pointer
	// rather than by the "vdisp_%04d" name -- see SourceSharpPointerSetContains.
	const void *pVirtualTerrain[ MAX_MAP_DISPINFO ];
	int32_t nVirtualTerrain = 0;
	for ( int i = 0; i < MAX_MAP_DISPINFO; i++ )
	{
		const CPhysCollide *pTerrain = modelinfo->GetCollideForVirtualTerrain( i );
		if ( pTerrain )
			pVirtualTerrain[ nVirtualTerrain++ ] = pTerrain;
	}
	SourceSharpSortPointerSet( pVirtualTerrain, nVirtualTerrain );

	for ( int i = 0; ppList && i < nCount; i++ )
	{
		IPhysicsObject *pObject = const_cast< IPhysicsObject * >( ppList[ i ] );
		if ( !pObject )
			continue;

		Vector vecPos;
		QAngle angAngles;
		pObject->GetPosition( &vecPos, &angAngles );

		// physics_shell.h's test rather than IsFinite and a float compare, and
		// for its reason: the release build is -ffast-math, under which the
		// compiler may reason as if NaN cannot occur. A report that cannot see
		// the value it exists to find is worse than no report.
		const bool bFinite = SourceSharpIsFinitePhysicsVector( vecPos.Base() )
			&& SourceSharpIsFinitePhysicsVector( angAngles.Base() );
		// The magnitude of an IEEE-754 single orders monotonically as an
		// unsigned integer, so masking the sign bit off and comparing against
		// the limit's own bits is the same test as fabsf() < flLimit -- without
		// a float comparison for -ffast-math to reason about.
		const uint32_t nLimitBits = SourceSharpFloatBits( flLimit );
		const bool bInBounds =
			   ( SourceSharpFloatBits( vecPos.x ) & 0x7FFFFFFFu ) < nLimitBits
			&& ( SourceSharpFloatBits( vecPos.y ) & 0x7FFFFFFFu ) < nLimitBits
			&& ( SourceSharpFloatBits( vecPos.z ) & 0x7FFFFFFFu ) < nLimitBits;

		// AND ITS SIZE, not only its position. An object with a non-finite or
		// absurd hull is as bad for the broadphase as an object in the wrong
		// place -- and GetPosition cannot see it.
		//
		// Except a virtual mesh, whose hull vphysics cannot answer without
		// crashing: that is static world terrain, and it counts as sane.
		bool bSaneHull = true;
		Vector vecMins( 0, 0, 0 ), vecMaxs( 0, 0, 0 );
		const CPhysCollide *pCollide = pObject->GetCollide();
		if ( pCollide && !SourceSharpPointerSetContains( pVirtualTerrain, nVirtualTerrain, pCollide ) )
		{
			physcollision->CollideGetAABB( &vecMins, &vecMaxs, pCollide, vec3_origin, vec3_angle );
			bSaneHull = SourceSharpIsFinitePhysicsVector( vecMins.Base() )
				&& SourceSharpIsFinitePhysicsVector( vecMaxs.Base() );
		}

		if ( bFinite && bInBounds && bSaneHull )
			continue;

		nBad++;
		if ( nPrinted < 8 )
		{
			nPrinted++;
			const char *pName = pObject->GetName();
			ss::Format( szLine, sizeof( szLine ),
			            "[SS-PHYSWORLD-OUTOFBOUNDS] index=%d name='%s' pos=(%f %f %f) "
			            "ang=(%f %f %f) hull=(%f %f %f)..(%f %f %f) "
			            "finite=%d inBounds=%d saneHull=%d static=%d asleep=%d\n",
			            i, pName ? pName : "(null)",
			            vecPos.x, vecPos.y, vecPos.z,
			            angAngles.x, angAngles.y, angAngles.z,
			            vecMins.x, vecMins.y, vecMins.z, vecMaxs.x, vecMaxs.y, vecMaxs.z,
			            bFinite ? 1 : 0, bInBounds ? 1 : 0, bSaneHull ? 1 : 0,
			            pObject->IsStatic() ? 1 : 0,
			            pObject->IsAsleep() ? 1 : 0 );
			PhysWorldReportEmit( szLine );
		}
	}

	ss::Format( szLine, sizeof( szLine ),
	            "[SS-PHYSWORLD-BOUNDS] objects=%d outOfBounds=%d limit=%f\n",
	            nCount, nBad, flLimit );
	PhysWorldReportEmit( szLine );

	// What the guard caught is managed's line now (ss_physworld_nan,
	// PhysWorldNan): the guard moved with the player shadow.
}


SS_NATIVE_COMMAND( ss_physworld_bounds )
{
	if ( !physenv )
	{
		PhysWorldReportEmit( "[SS-PHYSWORLD-BOUNDS] usable=0 why=no-environment\n" );
		return;
	}
	SourceSharpReportObjectsOutOfBounds();
}

//-----------------------------------------------------------------------------
// WHERE IS THE PLAYER'S SHADOW, RIGHT NOW?
//
// The `push` live-gate step is bimodal and the two modes part on how far the
// PLAYER walks, not on how hard the crate is pushed: over 57 holds the walk was
// either 18-83 units or 199-286 and never once in between, and a hold two and a
// half times as long moved the crate no further. That is a deadlock, and the
// question it raises is one only vphysics can answer -- when the player stops
// dead against the crate, is their SHADOW stopped too, or is it out in front of
// them with no way to bring them along?
//
// Stock's answer to that second case is CBasePlayer::VPhysicsShadowUpdate
// (player.cpp:8347), which moves the PLAYER to where the shadow got to --
// `SetAbsOrigin( newPosition )` at :8505. This port does not carry it
// (shell_standalone_player.cpp records the omission as a stated divergence:
// it needs PhysAddShadow and g_pShadowEntities), so if the shadow IS ahead
// there is nothing to close the gap.
//
// Public API only -- GetObjectList, GetName, GetPosition, GetVelocity,
// IsAsleep -- so it has nothing to go stale against.
//
// DEBT, with the same removal owner as the two blocks above: it is an
// instrument for the `push` deadlock and not something the build needs.
//-----------------------------------------------------------------------------
SS_NATIVE_COMMAND( ss_physworld_object )
{
	char szLine[ 512 ];

	if ( args.ArgC() < 2 )
	{
		PhysWorldReportEmit( "[SS-PHYSWORLD-OBJECT] usage: ss_physworld_object <name substring>\n" );
		return;
	}

	if ( !physenv )
	{
		PhysWorldReportEmit( "[SS-PHYSWORLD-OBJECT] no environment\n" );
		return;
	}

	const char *pszWanted = args.Arg( 1 );

	// "*" IS THE WHOLE ENVIRONMENT. The rule, and the reason the obvious
	// spelling of it does not work, are in physworld_match_rule.h -- which is a
	// header rather than two lines here because a gtest can compile it and the
	// empty-needle trap is worth pinning rather than commenting.
	int nCount = 0;
	const IPhysicsObject **ppList = physenv->GetObjectList( &nCount );
	int nFound = 0;

	for ( int i = 0; ppList && i < nCount; i++ )
	{
		IPhysicsObject *pObject = const_cast< IPhysicsObject * >( ppList[ i ] );

		if ( !pObject )
			continue;

		const char *pszName = pObject->GetName();

		if ( !PhysWorldObjectMatches( pszName, pszWanted ) )
			continue;

		if ( !pszName )
			pszName = "";

		Vector vecPos, vecVel;
		QAngle angAngles;
		pObject->GetPosition( &vecPos, &angAngles );
		pObject->GetVelocity( &vecVel, NULL );

		nFound++;
		// THROUGH THE REPORT SINK AND NOT A BARE Msg, for PhysWorldReportEmit's
		// own reason and for one more found here: a Msg from this file reaches
		// game/mod_sharp/console.log -- ONE file for every instance in the game
		// directory -- and does NOT reach the launch's own stdout, so a probe
		// tailing its game's log sees nothing at all and reads it as a command
		// that did not run. It cost an hour. The per-pid file is the sink a
		// parallel probe can actually read.
		ss::Format( szLine, sizeof( szLine ),
		            "[SS-PHYSWORLD-OBJECT] name='%s' pos=(%f %f %f) vel=(%f %f %f) "
		            "asleep=%d moveable=%d mass=%f gameFlags=%04x\n",
		            pszName, vecPos.x, vecPos.y, vecPos.z,
		            vecVel.x, vecVel.y, vecVel.z,
		            pObject->IsAsleep() ? 1 : 0, pObject->IsMoveable() ? 1 : 0,
		            pObject->GetMass(),
		            (unsigned int)pObject->GetGameFlags() );
		PhysWorldReportEmit( szLine );
	}

	ss::Format( szLine, sizeof( szLine ),
	            "[SS-PHYSWORLD-OBJECT] matched=%d of %d for '%s'\n",
	            nFound, nCount, pszWanted );
	PhysWorldReportEmit( szLine );
}


// THE CALLBACK IS HANDED THE OWNER, NOT THE SDK ENTITY, and that is a widening
// rather than a retype for its own sake. Both console commands below walk the
// ACTIVE OBJECT LIST — everything the solver is currently moving — and then
// dropped every entity outside the SDK on the floor, because the walk filtered
// on `PhysicsOwnerOf( ... ).pBase` and a managed prop answers NULL there. So
// `physics_report_active` listed a subset of the active objects and called it
// all of them, which is the shape of wrongness this hierarchy's contract warns
// about: a diagnostic that quietly under-reports is worse than one that says it
// cannot answer.
//
// PhysicsOwner_t is the type that closes it. It already answers both questions
// these two callbacks ask — GetClassname and GetDebugName — for either kind,
// which is what it was written for.
typedef void (*EntityCallbackFunction) ( const PhysicsOwner_t &owner );

void IterateActivePhysicsEntities( EntityCallbackFunction func )
{
	int activeCount = physenv->GetActiveObjectCount();
	IPhysicsObject **pActiveList = NULL;
	if ( activeCount )
	{
		pActiveList = (IPhysicsObject **)stackalloc( sizeof(IPhysicsObject *)*activeCount );
		physenv->GetActiveObjects( pActiveList );
		for ( int i = 0; i < activeCount; i++ )
		{
			// Asked rather than cast: the object may belong to an entity that is
			// not a CBaseEntity, and IsValid() is the question that covers both.
			PhysicsOwner_t owner = PhysicsOwnerOf( pActiveList[i] );
			if ( owner.IsValid() )
			{
				func( owner );
			}
		}
	}
}


static void CallbackHighlight( const PhysicsOwner_t &owner )
{
	// The one callback that does NOT widen. m_debugOverlays is the SDK's own
	// bit field end to end — client.cpp's ent_text family writes it,
	// CBaseEntity::DrawDebugGeometryOverlays reads it, ndebugoverlay.cpp clears
	// it — and igameentity.h names it among the members that never cross for
	// exactly that reason. There is no parallel bit to set, so this stays the
	// SDK's and says so.
	(void)owner;
}

static void CallbackReport( const PhysicsOwner_t &owner )
{
	// The SDK arm keeps stock's exact answer, model name fallback and all,
	// rather than being folded into GetDebugName() — that method falls back to
	// the CLASSNAME where this falls back to the MODEL name, and quietly
	// changing what a shipped console command prints is not this shard's to do.

	// The other hierarchy, through the two questions this type already answers.
	// GetDebugName is the mapper's name or the classname when it has none,
	// which is the same "name it by whatever it has" the SDK arm reaches for by
	// a different fallback; there is no model name on this boundary to prefer.
	Msg( "%s - %s\n", owner.GetClassname(), owner.GetDebugName() );
}

SS_NATIVE_COMMAND( physics_highlight_active )
{
	if ( !UTIL_IsCommandIssuedByServerAdmin() )
		return;

	IterateActivePhysicsEntities( CallbackHighlight );
}

SS_NATIVE_COMMAND( physics_report_active )
{
	if ( !UTIL_IsCommandIssuedByServerAdmin() )
		return;

	IterateActivePhysicsEntities( CallbackReport );
}


// ARMED THIS COMMAND HAS NO SUBJECT, and the tree already says so one line at
// its call site: `physics_debug_entity`, the only caller, is `#ifndef
// SOURCESHARP` and "the spelling is registered in shell_standalone_player.cpp".
// The port's answer for this family of console commands is to re-register them
// on the managed side, not to widen the C++ ones, and that answer has already
// been taken for this one.
//
// The reason it has no subject is PhysicsCommand, which is the only route in.
// It resolves its argument with TraceEntity( tr ) or gEntList.FindEntityGeneric,
// and BOTH answer NULL for an entity outside the SDK — TraceEntity is one of the
// thirteen live instances of that NULL-subject class. So armed nothing can be
// handed to this function at all.
//
// NOT WIDENED to the parallel hierarchy here. Doing that means giving
// PhysicsCommand a resolver that can find one, which is the shell's job and the
// shell has already done it.

// traverses the graph of attachments (currently supports springs & constraints) starting at an entity
// Then turns on debug info for each link in the graph (springs/constraints are links)
//
// ARMED THIS HAS NO SUBJECT AND NO GRAPH TO WALK, for two independent reasons
// and either alone is enough.
//
// The subject: `physics_constraints` is the only caller and it goes through
// PhysicsCommand, which resolves its argument with TraceEntity( tr ) or
// gEntList.FindEntityGeneric — both answer NULL for an entity outside the SDK,
// and TraceEntity is one of the thirteen live instances of that NULL-subject
// class. Nothing can be handed to this function.
//
// The graph: it is built by walking gEntList from FirstEnt() to NextEnt(), the
// SDK entity list, and asking each member for its constraint and spring
// attachments. Constraints and springs are phys_constraint entities, which are
// CBaseEntity's; there is no parallel-hierarchy constraint for the walk to find.
//
// The last line is the third reason — m_debugOverlays, which igameentity.h
// names among the members that never cross.

static void MarkVPhysicsDebug( IGameEntity *pEntity )
{
	if ( pEntity )
	{
		IPhysicsObject *pPhysics = EntityPhysicsObject( pEntity );
		if ( pPhysics )
		{
			unsigned short callbacks = pPhysics->GetCallbackFlags();
			callbacks ^= CALLBACK_MARKED_FOR_TEST;
			pPhysics->SetCallbackFlags( callbacks );
		}
	}
}

void PhysicsCommand( const CCommand &args, void (*func)( IGameEntity *pEntity ) )
{
	if ( args.ArgC() < 2 )
	{
	}
	else
	{
		//=====================================================================//
		// f-physics: A DECLARED, PRINTED REFUSAL (§7). THE BY-NAME WALK OVER
		// THE SDK ENTITY LIST IS REMOVED AND IT IS SAID PLAINLY.
		//
		// `gEntList` and `CGlobalEntityList::FindEntityGeneric` are
		// game/server/entitylist.cpp's, inside the removal set. They are ALSO
		// the entity-list family (plan_entity.md §3.3), which the slot table and
		// IClientEntityList replace and which is another lane's -- 19 objects
		// reference gEntList and 2 reference FindEntityGeneric, so this is not a
		// physics question and physics.cpp must not answer it with a local
		// mechanism of its own. What this file owes is to stop being one of
		// those referencing objects.
		//
		// THE WALK ALREADY FINDS NOTHING FOR THE HIERARCHY THAT MATTERS.
		// FindEntityGeneric answers out of the SDK entity list, and this build's
		// entities are on the other one; `physics_debug_entity` and the
		// constraint dump below already record the same judgement for the same
		// reason. The first branch of this function -- the cursor trace from a
		// CBasePlayer -- has been `#ifndef SOURCESHARP` since 13c because there
		// is no CBasePlayer to trace from, so BOTH ways of naming a subject are
		// now gone and the function is honest about it rather than half-working.
		//
		// WHAT WOULD HAVE TO EXIST: a by-name lookup over the slot table. The
		// debug-api already resolves entities by name for the web UI, so the answer
		// exists in this build -- it is on the other side of the boundary, and
		// wiring a console command to it belongs with whoever owns the list
		// replacement.
		//=====================================================================//
		static CPhysicsBridgeRefusal s_RefusePhysicsCommandByName(
			"naming a physics console command's subject entity by map name",
			"The walk is FindEntityGeneric on the global SDK entity list, which answers out of "
			"the "
			"list in entitylist.cpp -- inside the removal set, and the entity-list family "
			"(§3.3) that the slot table replaces, which is another lane's: 19 objects "
			"reference that global. This build's entities are on the other hierarchy, so the "
			"walk already finds none of them; and the function's other way of naming a "
			"subject, a cursor trace from a CBasePlayer, has been compiled out since 13c "
			"because there is no CBasePlayer. To change this answer the slot table needs "
			"a by-name lookup -- the debug-api already has one for the web UI." );

		if ( s_RefusePhysicsCommandByName.Reached() )
		{
			Warning( SOURCESHARP_PHYSICS_REFUSAL_FMT,
			         s_RefusePhysicsCommandByName.What(),
			         s_RefusePhysicsCommandByName.Refusal() );
		}

		// `func` is the per-entity body the caller wanted run. There is no
		// subject to run it on, so it is named here and not called -- a `(void)`
		// rather than a loop that cannot iterate, because a `while ( false )`
		// reads as live machinery to the next person to open this file.
		(void)func;
	}
}

// Goes with DebugConstraints itself, and for the reasons stated above it: armed
// there is no subject PhysicsCommand can resolve and no constraint graph to
// walk. Registering a command whose body cannot do anything would be a worse
// answer than not registering it -- the same judgement `physics_debug_entity`
// below already records, and the same place it points at for the replacement.


SS_NATIVE_COMMAND( physics_select )
{
	if ( !UTIL_IsCommandIssuedByServerAdmin() )
		return;

	PhysicsCommand( args, MarkVPhysicsDebug );
}

// physics_budget is managed's (PhysicsBudget, plan_physics_bones_native_zero
// 2.3), with the PhysForceEntityToSleep it was the only caller of.
