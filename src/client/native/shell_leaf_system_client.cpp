//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the client leaf system's object in client.so ========//
// SS_REASON a  a vtable the engine calls through
//
// C7 cluster 12 L2 (docs/plans/c7_leaf_system.md). The leaf system is
// managed's, and the object the engine and client.so's C++ call is
// libss_fwd_client's forwarder class (src/fwd/native/fwd_leaf_system.cpp).
// What client.so keeps is that object's storage, the addresses it reaches
// client.so through (the forwarder library has no globals of ours), the
// global Valve code reads it by (g_pClientLeafSystem, ClientLeafSystem()), and
// the name the engine asks the factory for (ClientLeafSystem002).
//
// clientleafsystem.cpp LEFT client.so with L2 (docs/client_evictions.md),
// and two of its definitions other client code still links against are
// here: CClientRenderablesList's fixed-size allocator (the view executor
// allocates the lists), and DefaultRenderBoundsWorldspace, the box a native
// renderable (a detail model, a standalone entity) answers
// GetRenderBoundsWorldspace with.
//=============================================================================//
#include "cbase.h"
#include "mathlib/mathlib.h"
#include "clientleafsystem.h"
#include "detailobjectsystem.h"
#include "tier1/interface.h"
#include "ss_fwd.h"
#include "fwd_leaf_system.h"
#include "shell_managed_areas_client.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

DEFINE_FIXEDSIZE_ALLOCATOR( CClientRenderablesList, 1, CUtlMemoryPool::GROW_SLOW );

// A renderable's world box from its own: its render bounds about its origin,
// and turned by its angles when it has any, the turned box's axis-aligned
// bounds. Nothing here asks an entity, a followed parent included: the
// parallel hierarchy composes a child's frame when its parent moves
// (shell_standalone_hierarchy.cpp), so the renderable's own origin is current.
void DefaultRenderBoundsWorldspace( IClientRenderable *pRenderable, Vector &absMins, Vector &absMaxs )
{
	Vector mins, maxs;
	pRenderable->GetRenderBounds( mins, maxs );
	const Vector &origin = pRenderable->GetRenderOrigin();
	const QAngle &angles = pRenderable->GetRenderAngles();
	if ( angles == vec3_angle )
	{
		absMins = mins + origin;
		absMaxs = maxs + origin;
		return;
	}
	matrix3x4_t toWorld;
	AngleMatrix( angles, origin, toWorld );
	TransformAABB( toWorld, mins, maxs, absMins, absMaxs );
}

namespace
{
	// CUtlVector::AddToTail, which allocates: the forwarder library has no heap.
	void AppendRenderable( void *pVector, IClientRenderable *pRenderable )
	{
		static_cast< CUtlVector< IClientRenderable * > * >( pVector )->AddToTail( pRenderable );
	}

	IDetailObjectSystem *DetailSystem()
	{
		return DetailObjectSystem();
	}

	alignas( 16 ) unsigned char s_Storage[ kSSLeafSystemBytes ];
	SSLeafSystemEnv s_Env;

	// Built through the forwarder library's own table (tools/engine-forwarders'
	// HAND row LeafSystem, whose slot 1 is Construct; its slots only append).
	IClientLeafSystem *BuildLeafSystem()
	{
		s_Env.ppTarget = &ShellClientDll_Managed().m_Areas.m_pLeafSystemClient;
		s_Env.ppEngine = &engine;
		s_Env.ppModelInfo = &modelinfo;
		s_Env.ppRender = &render;
		s_Env.ppGlobals = &gpGlobals;
		s_Env.pfnDetailObjectSystem = DetailSystem;
		s_Env.pfnAppendRenderable = AppendRenderable;

		const SSForwarderTable *pTable = SSFwdClient_Table( "SSFwd_LeafSystem" );
		if ( !pTable || pTable->nSlots < 2 )
			return NULL;
		typedef void *( *Construct_t )( void *, const SSLeafSystemEnv * );
		return static_cast< IClientLeafSystem * >( reinterpret_cast< Construct_t >( pTable->ppSlots[ 1 ] )( s_Storage, &s_Env ) );
	}

	void *CreateLeafSystem()
	{
		return static_cast< IClientLeafSystemEngine * >( g_pClientLeafSystem );
	}

	InterfaceReg s_LeafSystemReg( CreateLeafSystem, CLIENTLEAFSYSTEM_INTERFACE_VERSION );

	// THE GAME-SYSTEM ROW. kClientSystems' per-frame walks find a system's
	// per-frame half with dynamic_cast, which reads the object's type_info;
	// libss_fwd_client is built without RTTI (it links no C++ runtime), so its
	// object answers no dynamic_cast. The row is this adapter, built here with
	// RTTI: it hands the leaf system the three stages stock's CClientLeafSystem
	// had bodies for, and the rest are CClientSystemPerFrame's empty ones, as
	// stock's were.
	class CLeafSystemGameSystem final : public CClientSystemPerFrame
	{
	public:
		char const *Name() override { return "CClientLeafSystem"; }

		void LevelInitPreEntity() override
		{
			if ( g_pClientLeafSystem )
				g_pClientLeafSystem->LevelInitPreEntity();
		}

		void LevelShutdownPostEntity() override
		{
			if ( g_pClientLeafSystem )
				g_pClientLeafSystem->LevelShutdownPostEntity();
		}

		void PreRender() override
		{
			if ( g_pClientLeafSystem )
				g_pClientLeafSystem->PreRender();
		}
	};

	CLeafSystemGameSystem s_GameSystem;
}

// What Valve code reads the leaf system by (clientleafsystem.h's
// ClientLeafSystem()): libss_fwd_client's object, built at load.
IClientLeafSystem *g_pClientLeafSystem = BuildLeafSystem();

// shell_clientsystems.cpp's CClientLeafSystem row.
CClientSystem *ClientLeafSystemGameSystem()
{
	return &s_GameSystem;
}
