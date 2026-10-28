//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON b  an interface resolved by name at load
// The client's game systems as one named table, in the order they run. Stage
// passes walk the rows forwards; the shutdown stages walk them backwards. The
// order is stock's observed runtime order: the systems CHLClient::Init added by
// hand first, then the auto-registered ones in reverse construction order.

#include "datacache/imdlcache.h"
#include "detailobjectsystem.h"
#include "clienteffectprecachesystem.h"
#include "tier0/commonmacros.h"
#include "tier0/dbg.h"
#include "tier1/strtools.h"
#include "cdll_client_int.h"
#include "cdll_int.h"
#include "shell_clientsystem.h"
#include "igamesystem.h"

// Stock's IGameSystem keys its vtable on this destructor. client.so keeps no
// system list of its own, so the body is empty.
IGameSystem::~IGameSystem() {}

// The accessors. Declared here because this table is their only other reader.
CClientSystem *GameStringSystem();
CClientSystem *ClientSystem_SoundEmitter();
CClientSystem *ClientSystem_ManagedViewport();
CClientSystem *ClientSoundscapeSystem();

CClientSystem *ClientSystem_StandaloneClient();
CClientSystem *ClientSystem_ManagedGlowOverlay();
CClientSystem *ClientSystem_DecalEmitter();
CClientSystem *ClientSystem_DirtySpatialPartition();
CClientSystem *ClientSystem_BulletWhizTimer();
CClientSystem *ClientSystem_ManagedPixelVisibility();
CClientSystem *ClientSystem_Sprite();
CClientSystem *ClientSystem_WorldText();
CClientSystem *ClientSystem_Rope();
CClientSystem *ClientSystem_ManagedParticles();
CClientSystem *ClientSystem_StandaloneInterp();
CClientSystem *ClientSystem_SourceSharpManaged();
CClientSystem *ClientSystem_Physics();

// Stock's accessors answer a more specific type; these adapters keep the
// table's column uniform.
class IClientLeafSystem;
class IDetailObjectSystem;
class CClientEffectPrecacheSystem;

IClientLeafSystem *ClientLeafSystem();
IDetailObjectSystem *DetailObjectSystem();
CClientEffectPrecacheSystem *ClientEffectPrecacheSystem();

// The leaf system is libss_fwd_client's object, built without RTTI, so its row
// is an adapter the per-frame walks can dynamic_cast.
CClientSystem *ClientLeafSystemGameSystem();
static CClientSystem *ClientLeafGameSystem()          { return ClientLeafSystemGameSystem(); }
static CClientSystem *DetailObjectGameSystem()
{
	static CGameSystemRow< IDetailObjectSystem > s_Row( DetailObjectSystem() );
	return &s_Row;
}
// The shadow manager's row is shell_shadows_client.cpp's adapter over the managed manager.
CClientSystem *ClientShadowMgrGameSystem();
static CClientSystem *ClientShadowGameSystem()        { return ClientShadowMgrGameSystem(); }
static CClientSystem *ClientEffectPrecacheGameSystem()
{
	static CGameSystemRow< CClientEffectPrecacheSystem > s_Row( ClientEffectPrecacheSystem() );
	return &s_Row;
}

// The map's base name, or "" when no level is loaded.
const char *SourceSharp_ClientMapName( char *pszOut, int nOutBytes )
{
	if ( pszOut == NULL || nOutBytes <= 0 )
		return "";

	pszOut[ 0 ] = 0;

	const char *pszLevel = engine ? engine->GetLevelName() : NULL;
	if ( pszLevel == NULL || pszLevel[ 0 ] == 0 )
		return pszOut;

	V_FileBase( pszLevel, pszOut, nOutBytes );
	return pszOut;
}

// One `// RULING | <row> | <verdict> | <file> | <symbol>` line per table row;
// client_flip_ruling_test parses them against the table.
//
// RULING | CGameStringPool | STAYS | sourcesharp/game/shared/gamestringpool.cpp | GameStringSystem
// RULING | CSoundEmitterSystem | STAYS | sourcesharp/game/client/shell_soundemitter_client.cpp | ClientSystem_SoundEmitter
// RULING | CClientLeafSystem | STAYS | sourcesharp/game/client/shell_leaf_system_client.cpp | ClientLeafSystemGameSystem
// RULING | CDetailObjectSystem | STAYS | sourcesharp/game/client/shell_detail_props.cpp | DetailObjectSystem
// RULING | CViewportClientSystem | STAYS | sourcesharp/game/client/shell_managed_clientsystems_client.cpp | ClientSystem_ManagedViewport
// RULING | CClientEffectPrecacheSystem | STAYS | sourcesharp/game/client/shell_effects_list_client.cpp | ClientEffectPrecacheSystem
// RULING | CClientShadowMgr | STAYS | sourcesharp/game/client/shell_shadows_client.cpp | ClientShadowMgrGameSystem
// RULING | C_SoundscapeSystem | STAYS | sourcesharp/game/client/shell_soundscape_audio_client.cpp | ClientSoundscapeSystem
// RULING | CSourceSharpStandaloneClientSystem | STAYS | sourcesharp/game/client/shell_standalone_client.cpp | ClientSystem_StandaloneClient
// RULING | CGlowOverlaySystem | STAYS | sourcesharp/game/client/shell_managed_clientsystems_client.cpp | ClientSystem_ManagedGlowOverlay
// RULING | CDecalEmitterSystem | STAYS | sourcesharp/game/client/shell_decals_client.cpp | ClientSystem_DecalEmitter
// RULING | CDirtySpatialPartitionEntityList | STAYS | sourcesharp/game/shared/collisionproperty.cpp | ClientSystem_DirtySpatialPartition
// RULING | CBulletWhizTimer | STAYS | sourcesharp/game/client/shell_effects_list_client.cpp | ClientSystem_BulletWhizTimer
// RULING | CPixelVisibilitySystem | STAYS | sourcesharp/game/client/shell_managed_clientsystems_client.cpp | ClientSystem_ManagedPixelVisibility
// RULING | CSourceSharpSpriteSystem | STAYS | sourcesharp/game/client/shell_sprite_render.cpp | ClientSystem_Sprite
// RULING | CSourceSharpWorldTextSystem | STAYS | sourcesharp/game/client/shell_worldtext_render.cpp | ClientSystem_WorldText
// RULING | CSourceSharpRopeSystem | STAYS | sourcesharp/game/client/shell_rope_render.cpp | ClientSystem_Rope
// RULING | CSourceSharpManagedParticlesSystem | STAYS | sourcesharp/game/client/shell_managed_particles.cpp | ClientSystem_ManagedParticles
// RULING | CSourceSharpStandaloneInterpSystem | STAYS | sourcesharp/game/client/shell_standalone_client.cpp | ClientSystem_StandaloneInterp
// RULING | SourceSharp | STAYS | sourcesharp/game/shared/sourcesharp_gamesystem.cpp | ClientSystem_SourceSharpManaged
// RULING | CPhysicsSystem | STAYS | sourcesharp/game/client/shell_physics_client.cpp | ClientSystem_Physics
//
// Not rows: CToolFrameworkClient, CBasePresence, CBaseGameStats_Driver and
// CRagdollLRURetirement left with their files.

struct ClientSystemRow_t
{
	const char *m_pszName;
	CClientSystem *( *m_pfnSystem )();
};

static const ClientSystemRow_t kClientSystems[] =
{
	// Stock's hand-added systems, in CHLClient::Init's order.
	{ "CGameStringPool",                    GameStringSystem },
	{ "CSoundEmitterSystem",                ClientSystem_SoundEmitter },

	// "Client Leaf System has to be initialized first, since DetailObjectSystem
	// uses it" (stock): it must precede DetailObjectSystem.
	{ "CClientLeafSystem",                  ClientLeafGameSystem },
	{ "CDetailObjectSystem",                DetailObjectGameSystem },

	{ "CViewportClientSystem",              ClientSystem_ManagedViewport },
	{ "CClientEffectPrecacheSystem",        ClientEffectPrecacheGameSystem },
	{ "CClientShadowMgr",                   ClientShadowGameSystem },

	{ "C_SoundscapeSystem",                 ClientSoundscapeSystem },

	// The auto-registered systems, in stock's runtime order.
	{ "CSourceSharpStandaloneClientSystem", ClientSystem_StandaloneClient },
	{ "CGlowOverlaySystem",                 ClientSystem_ManagedGlowOverlay },
	{ "CDecalEmitterSystem",                ClientSystem_DecalEmitter },
	{ "CDirtySpatialPartitionEntityList",   ClientSystem_DirtySpatialPartition },
	{ "CBulletWhizTimer",                   ClientSystem_BulletWhizTimer },
	{ "CPixelVisibilitySystem",             ClientSystem_ManagedPixelVisibility },
	{ "CSourceSharpSpriteSystem",           ClientSystem_Sprite },
	{ "CSourceSharpWorldTextSystem",        ClientSystem_WorldText },
	{ "CSourceSharpRopeSystem",             ClientSystem_Rope },
	{ "CSourceSharpManagedParticlesSystem", ClientSystem_ManagedParticles },
	{ "CSourceSharpStandaloneInterpSystem", ClientSystem_StandaloneInterp },

	// The managed frame. The CLR is already up from CHLClient::Init.
	{ "SourceSharp",                        ClientSystem_SourceSharpManaged },

	{ "CPhysicsSystem",                     ClientSystem_Physics },
};

static const int kClientSystemCount = ARRAYSIZE( kClientSystems );

typedef void ( CClientSystem::*ClientSystemStage_t )();
typedef void ( CClientSystemPerFrame::*ClientSystemFrameStage_t )();

static void WalkForward( ClientSystemStage_t fn )
{
	for ( int i = 0; i < kClientSystemCount; i++ )
	{
		MDLCACHE_CRITICAL_SECTION();

		CClientSystem *pSys = kClientSystems[ i ].m_pfnSystem();
		( pSys->*fn )();
	}
}

static void WalkBackward( ClientSystemStage_t fn )
{
	for ( int i = kClientSystemCount; --i >= 0; )
	{
		MDLCACHE_CRITICAL_SECTION();

		CClientSystem *pSys = kClientSystems[ i ].m_pfnSystem();
		( pSys->*fn )();
	}
}

// The per-frame walk: the same rows, filtered by dynamic_cast to the ones with
// a per-frame half, as stock's second list was.
static void WalkForwardPerFrame( ClientSystemFrameStage_t fn )
{
	for ( int i = 0; i < kClientSystemCount; i++ )
	{
		CClientSystemPerFrame *pSys =
			dynamic_cast< CClientSystemPerFrame * >( kClientSystems[ i ].m_pfnSystem() );

		if ( pSys == NULL )
			continue;

		MDLCACHE_CRITICAL_SECTION();
		( pSys->*fn )();
	}
}

// Init in the table's order, stopping on the first refusal, as stock did.
bool SourceSharp_ClientSystemsInit()
{
	for ( int i = 0; i < kClientSystemCount; i++ )
	{
		MDLCACHE_CRITICAL_SECTION();

		CClientSystem *pSys = kClientSystems[ i ].m_pfnSystem();

		if ( !pSys->Init() )
		{
			Warning( "[SourceSharp] client system '%s' refused to init; the client "
			         "cannot come up without it\n", kClientSystems[ i ].m_pszName );
			return false;
		}
	}

	return true;
}

void SourceSharp_ClientSystemsPostInit()
{
	WalkForward( &CClientSystem::PostInit );
}

void SourceSharp_ClientSystemsShutdown()
{
	WalkBackward( &CClientSystem::Shutdown );
}

void SourceSharp_ClientSystemsLevelInitPreEntity()
{
	WalkForward( &CClientSystem::LevelInitPreEntity );
}

void SourceSharp_ClientSystemsLevelInitPostEntity()
{
	WalkForward( &CClientSystem::LevelInitPostEntity );
}

void SourceSharp_ClientSystemsLevelShutdownPreEntity()
{
	WalkBackward( &CClientSystem::LevelShutdownPreEntity );
}

void SourceSharp_ClientSystemsLevelShutdownPostEntity()
{
	WalkBackward( &CClientSystem::LevelShutdownPostEntity );
}

void SourceSharp_ClientSystemsPreRender()
{
	WalkForwardPerFrame( &CClientSystemPerFrame::PreRender );
}

// Update takes an argument, so it does not go through WalkForwardPerFrame.
void SourceSharp_ClientSystemsUpdate( float frametime )
{
	for ( int i = 0; i < kClientSystemCount; i++ )
	{
		CClientSystemPerFrame *pSys =
			dynamic_cast< CClientSystemPerFrame * >( kClientSystems[ i ].m_pfnSystem() );

		if ( pSys == NULL )
			continue;

		MDLCACHE_CRITICAL_SECTION();
		pSys->Update( frametime );
	}
}
