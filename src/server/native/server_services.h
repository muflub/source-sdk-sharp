// The DLL tier's subsystems: built in DLLInit once the engine is bound, each handed
// what it calls, and gone in DLLShutdown with the tier.

#pragma once

#include "server_engine_interfaces.h"
#include "server_entity_context.h"
#include "managed_side.h"
#include "shell_managed_areas.h"
#include "overlay_library.h"
#include "shell_debugoverlays.h"
#include "shell_entity_lifecycle.h"
#include "shell_frame.h"
#include "shell_gameclients.h"
#include "shell_gameents.h"
#include "shell_level.h"
#include "precache_model.h"
#include "world_queries.h"
#include "tempent_registry.h"
#include "server_entity_resolver.h"
#include "server_damage.h"
#include "server_console_commands.h"
#include "server_models.h"
#include "server_network.h"
#include "server_entities.h"
#include "server_movement.h"
#include "server_collision.h"
#include "server_player_queries.h"
#include "server_transform.h"
#include "server_hierarchy.h"
#include "server_physics.h"
#include "shell_usermessage_send.h"
#include "game_events.h"
#include "server_diagnostics.h"
#include "shell_boundary_probe.h"

class CServerDllServices
{
public:
	CServerDllServices( const CServerEngineInterfaces &engine, CGlobalVars *pGlobals, const CManagedHolders &managed,
						CManagedSide &managedSide, CShellTransmitSet &transmitSet, CServerPlayersDll &players,
						unsigned short &nWorldSerial, int32_t &nInvisibleRangeReach, const CSourceSharpSlotTable &slots,
						CSourceSharpEntitySlots &entitySlots, CServerNetTables &netTables )
		: m_ManagedSide( managedSide )
		, m_Precache( engine.m_pEngine, engine.m_pModelInfo, managed.m_Areas )
		, m_EntityContext( engine.m_pEngine, pGlobals, engine.m_pPartition, engine.m_pModelInfo, engine.m_pStaticProps,
						   engine.m_pTrace, managed, managedSide.Loaded(), managedSide.Generation(), players,
						   nWorldSerial, m_Precache, netTables )
		, m_Resolver( slots, m_EntityContext, nInvisibleRangeReach )
		, m_Physics( pGlobals, engine.m_Factories, managed.m_Areas, players, m_EntityContext, m_Resolver )
		, m_Damage( m_Resolver, engine.m_pRandom )
		, m_ConsoleCommands( m_Resolver, engine.m_pEngine )
		, m_Models( m_Resolver )
		, m_Network( m_Resolver, entitySlots, netTables )
		, m_Entities( m_Resolver )
		, m_Movement( m_Resolver )
		, m_Collision( m_Resolver, pGlobals )
		, m_PlayerQueries( m_Resolver )
		, m_Transform( m_Resolver )
		, m_Hierarchy( m_Resolver )
		, m_EntityLifecycle( m_EntityContext )
		, m_UserMessages( engine.m_pEngine, pGlobals )
		, m_Events( engine.m_pEvents, managed.m_Areas.m_pEvents, true )
		, m_Diagnostics( players )
		, m_OverlayLibrary( engine.m_pOverlay )
		, m_DebugOverlays( m_OverlayLibrary )
		, m_WorldQueries( engine.m_pModelInfo, nInvisibleRangeReach, m_Physics.SurfacePropsRef() )
		, m_Frame( pGlobals, engine.m_pGameStats, engine.m_pMdlCache, m_DebugOverlays, m_EntityContext, m_WorldQueries )
		, m_GameClients( engine.m_pEngine, pGlobals, engine.m_pMdlCache, managed.m_Areas, managedSide.Loaded(),
						 m_EntityLifecycle, m_BoundaryProbes, m_EntityContext )
		, m_GameEnts( engine.m_pEngine, engine.m_pMdlCache, managed.m_Areas, managedSide.Loaded(), transmitSet,
					  m_BoundaryProbes )
		, m_Level( engine.m_pEngine, engine.m_pBaseFileSystem, engine.m_pMdlCache, pGlobals, m_EntityContext,
				   m_EntityLifecycle.Factories(), m_Precache, m_Physics )
	{
		// The entities reach the level's physics through their context.
		m_EntityContext.m_pPhysics = &m_Physics;
	}

	CServerDllServices( const CServerDllServices & ) = delete;
	CServerDllServices &operator=( const CServerDllServices & ) = delete;

	// The managed runtime's loader, the game DLL's; native command bodies reach cs_sv_reload's through it.
	CManagedSide &m_ManagedSide;

	CServerModelPrecache m_Precache;
	CServerEntityContext m_EntityContext;
	CServerEntityResolver m_Resolver;
	CServerPhysics m_Physics;
	CServerDamage m_Damage;
	CServerConsoleCommands m_ConsoleCommands;
	CServerModels m_Models;
	CServerNetwork m_Network;
	CServerEntities m_Entities;
	CServerMovement m_Movement;
	CServerCollision m_Collision;
	CServerPlayerQueries m_PlayerQueries;
	CServerTransform m_Transform;
	CServerHierarchy m_Hierarchy;
	CServerEntityLifecycle m_EntityLifecycle;
	CServerUserMessages m_UserMessages;
	CGameEvents m_Events;
	CServerDiagnostics m_Diagnostics;
	COverlayLibrary m_OverlayLibrary;
	CServerDebugOverlays m_DebugOverlays;
	CServerWorldQueries m_WorldQueries;

	// Built before the engine walks the server class list, which the tier outlives.
	CServerTempEnts m_TempEnts;
	CServerFrame m_Frame;

	// The engine's calls into the clients and ents objects below, counted.
	CBoundaryProbe m_BoundaryProbes;
	CServerGameClients m_GameClients;
	CServerGameEnts m_GameEnts;
	CServerLevel m_Level;
};
