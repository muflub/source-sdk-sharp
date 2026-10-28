//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
#ifdef CLIENT_DLL
#include <stdio.h>
#include <stdlib.h>
#include "tier0/platform.h"
#include "tier0/dbg.h"
#include "tier1/strtools.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "const.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#endif
#include "shell_gamerules.h"
#include "shell_diagnostics.h"
#include "sourcesharp.h"
#ifdef CLIENT_DLL
#include "ammodef.h"
#include "takedamageinfo.h"
#include "gamestringpool.h"
#include "blast_blocking.h"
#include "networkstringtable_clientdll.h"
#else
#include "networkstringtabledefs.h"
#include "edict.h"
extern INetworkStringTableContainer *networkstringtable;
extern CGlobalVars *gpGlobals;
#endif

#ifndef CLIENT_DLL
uint32_t SourceSharp_HandleFromEntity( IGameEntity *pEntity );
#endif

#ifndef CLIENT_DLL
#include "shell_server.h"
#include "shell_standalone_class.h"

#include "ss_shareddefs.h"
#include "ss_takedamageinfo.h"
#include "standalone_relays.h"

// shareddefs.h's damage bits by their stock names, for the mask table below.
#define DMG_ACID ss::kDmgAcid
#define DMG_BLAST ss::kDmgBlast
#define DMG_BURN ss::kDmgBurn
#define DMG_CLUB ss::kDmgClub
#define DMG_CRUSH ss::kDmgCrush
#define DMG_DROWN ss::kDmgDrown
#define DMG_DROWNRECOVER ss::kDmgDrownrecover
#define DMG_FALL ss::kDmgFall
#define DMG_NERVEGAS ss::kDmgNervegas
#define DMG_PARALYZE ss::kDmgParalyze
#define DMG_PHYSGUN ss::kDmgPhysgun
#define DMG_PLASMA ss::kDmgPlasma
#define DMG_POISON ss::kDmgPoison
#define DMG_PREVENT_PHYSICS_FORCE ss::kDmgPreventPhysicsForce
#define DMG_RADIATION ss::kDmgRadiation
#define DMG_SHOCK ss::kDmgShock
#define DMG_SLOWBURN ss::kDmgSlowburn
#define DMG_SONIC ss::kDmgSonic
#include "imanaged_areas.h"

// Stock's value.
#define ROBUST_RADIUS_PROBE_DIST 16.0f
#endif

#ifndef CLIENT_DLL

// Set and cleared by CSourceSharpGameRules' constructor and destructor.
IGameRules *g_pGameRules = NULL;

#endif // !CLIENT_DLL

// The hulls before managed registers its own: stock's values, so reads during level
// init see a real hull. Static, so the client's copy is a second static, not a duplicate.
static RulesViewVectors_t g_DefaultViewVectors( Vector( 0, 0, 64 ), // VEC_VIEW
												Vector( -16, -16, 0 ), // VEC_HULL_MIN
												Vector( 16, 16, 72 ), // VEC_HULL_MAX
												Vector( -16, -16, 0 ), // VEC_DUCK_HULL_MIN
												Vector( 16, 16, 36 ), // VEC_DUCK_HULL_MAX
												Vector( 0, 0, 28 ), // VEC_DUCK_VIEW
												Vector( -10, -10, -10 ), // VEC_OBS_HULL_MIN
												Vector( 10, 10, 10 ), // VEC_OBS_HULL_MAX
												Vector( 0, 0, 14 ) // VEC_DEAD_VIEWHEIGHT
);

static Vector SourceSharpVec3( const float *p )
{
	return Vector( p[0], p[1], p[2] );
}

// A standing hull with no volume would give every player no collision box, so it is
// refused and stock's hulls kept. The other eight vectors may legally be zero.
static bool SourceSharpHullIsDegenerate( const SourceSharpMovementParams *p )
{
	return p->vecHullMax[2] <= p->vecHullMin[2] || p->vecHullMax[0] <= p->vecHullMin[0] ||
		p->vecHullMax[1] <= p->vecHullMin[1];
}

void SourceSharp_PublishViewVectors( const SourceSharpMovementParams *pParams )
{
	if ( !pParams )
		return;

	if ( SourceSharpHullIsDegenerate( pParams ) )
	{
		Warning( "[SourceSharp] movement params carry a standing hull with no "
				 "volume (min %.1f %.1f %.1f, max %.1f %.1f %.1f). KEEPING the "
				 "stock hull: publishing that one is a player with no collision "
				 "box, which looks like a broken map rather than a missing "
				 "number. Set Movement.Current's HullMin/HullMax in "
				 "SourceSharp.Shared.\n",
				 pParams->vecHullMin[0], pParams->vecHullMin[1], pParams->vecHullMin[2], pParams->vecHullMax[0],
				 pParams->vecHullMax[1], pParams->vecHullMax[2] );
		return;
	}

	g_DefaultViewVectors.m_vView = SourceSharpVec3( pParams->vecView );
	g_DefaultViewVectors.m_vHullMin = SourceSharpVec3( pParams->vecHullMin );
	g_DefaultViewVectors.m_vHullMax = SourceSharpVec3( pParams->vecHullMax );
	g_DefaultViewVectors.m_vDuckHullMin = SourceSharpVec3( pParams->vecDuckHullMin );
	g_DefaultViewVectors.m_vDuckHullMax = SourceSharpVec3( pParams->vecDuckHullMax );
	g_DefaultViewVectors.m_vDuckView = SourceSharpVec3( pParams->vecDuckView );
	g_DefaultViewVectors.m_vObsHullMin = SourceSharpVec3( pParams->vecObsHullMin );
	g_DefaultViewVectors.m_vObsHullMax = SourceSharpVec3( pParams->vecObsHullMax );
	g_DefaultViewVectors.m_vDeadViewHeight = SourceSharpVec3( pParams->vecDeadViewHeight );
}

// The server writes the rules classname into this table to tell the client to build
// its own rules object; the client constructs its one class whatever the string.
#define GAMERULES_STRINGTABLE_NAME "GameRulesCreation"

INetworkStringTable *g_StringTableGameRules = NULL;

#ifdef CLIENT_DLL

// The server has made its rules object; make ours.
//-----------------------------------------------------------------------------
static void OnGameRulesCreationStringChanged( void *object, INetworkStringTable *stringTable, int stringNumber,
											  const char *newString, void const *newData )
{
	delete g_pGameRules;
	g_pGameRules = NULL;

	// The constructor assigns g_pGameRules, exactly as it does on the server.
	new CSourceSharpGameRules();

	if ( !g_pGameRules )
		Error( "OnGameRulesCreationStringChanged: game rules object not created" );
}

void InstallStringTableCallback_GameRules()
{
	if ( g_StringTableGameRules )
		return;

	g_StringTableGameRules = networkstringtable->FindTable( GAMERULES_STRINGTABLE_NAME );

	if ( g_StringTableGameRules )
		g_StringTableGameRules->SetStringChangedCallback( NULL, OnGameRulesCreationStringChanged );
}

#else

void CreateNetworkStringTables_GameRules()
{
	g_StringTableGameRules = networkstringtable->CreateStringTable( GAMERULES_STRINGTABLE_NAME, 1 );
}

void SourceSharp_CreateGameRules()
{
	delete g_pGameRules;
	g_pGameRules = NULL;

	// The constructor assigns g_pGameRules.
	new CSourceSharpGameRules();

	if ( !g_pGameRules )
		Error( "SourceSharp_CreateGameRules: game rules object not created" );

	// Tell the client to build its own; without it, g_pGameRules is NULL there.
	Assert( g_StringTableGameRules );
	if ( g_StringTableGameRules )
	{
		const char *pszName = "CSourceSharpGameRules";
		g_StringTableGameRules->AddString( true, "classname", (int)strlen( pszName ) + 1, pszName );
	}

	g_pGameRules->CreateCustomNetworkStringTables();
}

#endif // CLIENT_DLL

#ifdef CLIENT_DLL
BEGIN_NETWORK_TABLE_NOBASE( CSourceSharpGameRules, DT_SourceSharpGameRules )
END_NETWORK_TABLE()
#endif

CSourceSharpGameRules::CSourceSharpGameRules()
{
	SOURCESHARP_TRACE();

	// Two rules objects would leave the first leaked and unreachable.
	Assert( !g_pGameRules );
	g_pGameRules = this;
}

CSourceSharpGameRules::~CSourceSharpGameRules()
{
	SOURCESHARP_TRACE();

	Assert( g_pGameRules == this );
	g_pGameRules = NULL;
}

// Stock's values as the defaults: damage paths can run before managed registers, and
// a zeroed table would make nothing time-based and everything take physics force.
static SourceSharpDamageRules s_DamageRules = {
	// nTimeBased
	DMG_PARALYZE | DMG_NERVEGAS | DMG_POISON | DMG_RADIATION | DMG_DROWNRECOVER | DMG_ACID | DMG_SLOWBURN,

	// nNoPhysicsForce, which includes the time-based set as stock's does.
	DMG_FALL | DMG_BURN | DMG_PLASMA | DMG_DROWN | DMG_PARALYZE | DMG_NERVEGAS | DMG_POISON | DMG_RADIATION |
		DMG_DROWNRECOVER | DMG_ACID | DMG_SLOWBURN | DMG_CRUSH | DMG_PHYSGUN | DMG_PREVENT_PHYSICS_FORCE,

	// nShouldGibCorpse
	DMG_CRUSH | DMG_FALL | DMG_BLAST | DMG_SONIC | DMG_CLUB,

	// nShouldNotBleed
	DMG_POISON | DMG_ACID,

	// nShowOnHud
	DMG_POISON | DMG_ACID | DMG_DROWN | DMG_BURN | DMG_SLOWBURN | DMG_NERVEGAS | DMG_RADIATION | DMG_SHOCK,
};

// C# states them once at startup.
void SourceSharp_RegisterDamageRules( const SourceSharpDamageRules *pRules )
{
	if ( !pRules )
		return;

	s_DamageRules = *pRules;
}

#ifndef CLIENT_DLL
// Defaults for paths that run before C# registers.
static SourceSharpGameRulesPolicy s_Policy = {
	true, // bMultiplayer
};

// C# states them once at startup.
void SourceSharp_RegisterGameRulesPolicy( const SourceSharpGameRulesPolicy *pPolicy )
{
	if ( !pPolicy )
		return;

	s_Policy = *pPolicy;
}
#endif // !CLIENT_DLL

int CSourceSharpGameRules::Damage_GetTimeBased()
{
	return s_DamageRules.nTimeBased;
}

int CSourceSharpGameRules::Damage_GetNoPhysicsForce()
{
	return s_DamageRules.nNoPhysicsForce;
}

bool CSourceSharpGameRules::IsMultiplayer()
{
#ifdef CLIENT_DLL
	// The policy table is registered on the server; the client is multiplayer too.
	return true;
#else
	return s_Policy.bMultiplayer != 0;
#endif
}

#ifndef CLIENT_DLL

// Connection admission. C# decides; with no managed side every connection is allowed.
bool CSourceSharpGameRules::ClientConnected( edict_t *pEntity, const char *pszName, const char *pszAddress,
											 char *reject, int maxrejectlen )
{
	SOURCESHARP_TRACE_MSG( pszName );

	IManagedClients *pApi = ManagedClients();
	if ( pApi )
	{
		if ( reject && maxrejectlen > 0 )
			reject[0] = '\0';

		if ( pApi->OnClientConnect( pszName ? pszName : "", pszAddress ? pszAddress : "", reject, maxrejectlen ) == 0 )
		{
			Msg( "[SourceSharp] C# refused '%s': %s\n", pszName ? pszName : "?",
				 ( reject && reject[0] ) ? reject : "(no reason given)" );
			return false;
		}
	}

	return true;
}

// Empty: managed's GameFrame runs the frame's last managed point and the late drain.
void CSourceSharpGameRules::EndGameFrame() {}

#endif // !CLIENT_DLL

const RulesViewVectors_t *CSourceSharpGameRules::GetViewVectors() const
{
	return &g_DefaultViewVectors;
}

// Which collision groups collide: a 20x20 bit matrix C# registers, asked once per
// traced entity. The defaults are stock's ShouldCollide answers.
static SourceSharpCollisionRules s_CollisionRules = { {
	0x000FABFFu, // COLLISION_GROUP_NONE
	0x00020001u, // COLLISION_GROUP_DEBRIS
	0x00000001u, // COLLISION_GROUP_DEBRIS_TRIGGER
	0x000EAAC1u, // COLLISION_GROUP_INTERACTIVE_DEBRIS
	0x000EABE1u, // COLLISION_GROUP_INTERACTIVE
	0x000C23F1u, // COLLISION_GROUP_PLAYER
	0x000EABB9u, // COLLISION_GROUP_BREAKABLE_GLASS
	0x000EB3F9u, // COLLISION_GROUP_VEHICLE
	0x000CABF1u, // COLLISION_GROUP_PLAYER_MOVEMENT
	0x000EE3F9u, // COLLISION_GROUP_NPC
	0x00000000u, // COLLISION_GROUP_IN_VEHICLE
	0x000E8959u, // COLLISION_GROUP_WEAPON
	0x00000080u, // COLLISION_GROUP_VEHICLE_CLIP
	0x000E83F9u, // COLLISION_GROUP_PROJECTILE
	0x000E8200u, // COLLISION_GROUP_DOOR_BLOCKER
	0x000EEBD9u, // COLLISION_GROUP_PASSABLE_DOOR
	0x00000001u, // COLLISION_GROUP_DISSOLVING
	0x000EEADBu, // COLLISION_GROUP_PUSHAWAY
	0x000EEBF9u, // COLLISION_GROUP_NPC_ACTOR
	0x000EEBF9u, // COLLISION_GROUP_NPC_SCRIPTED
} };

// C# states them once at startup.
void SourceSharp_RegisterCollisionRules( const SourceSharpCollisionRules *pRules )
{
	if ( !pRules )
		return;

	s_CollisionRules = *pRules;
}

bool CSourceSharpGameRules::ShouldCollide( int collisionGroup0, int collisionGroup1 )
{
	// Stock answers true for groups it did not name; out of range is not indexable.
	if ( collisionGroup0 < 0 || collisionGroup0 >= LAST_SHARED_COLLISION_GROUP || collisionGroup1 < 0 ||
		 collisionGroup1 >= LAST_SHARED_COLLISION_GROUP )
		return true;

	// The matrix is symmetric, so the group order does not matter.
	return ( s_CollisionRules.nRows[collisionGroup0] & ( 1u << collisionGroup1 ) ) != 0;
}
