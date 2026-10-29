//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through
// The client leaf system's storage in client.so. The leaf system itself is managed, behind
// libss_fwd_client's forwarder object; client.so keeps its storage, the global Valve code
// reads it by and the name the engine asks the factory for (ClientLeafSystem002).

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "client_entity.h"
#include "shell_clientsystem.h"

#include "detailobjectsystem.h"

#include "particlemgr.h"
#include "vphysics_interface.h"
#include "ss_fwd.h"
#include "fwd_leaf_system.h"
#include "shell_managed_areas_client.h"

DEFINE_FIXEDSIZE_ALLOCATOR(CClientRenderablesList, 1, CUtlMemoryPool::GROW_SLOW);

// A renderable's world box from its own render bounds, origin and angles. Nothing asks an
// entity: the parallel hierarchy keeps a child's origin current when its parent moves.
void DefaultRenderBoundsWorldspace(IClientRenderable* pRenderable, Vector& absMins, Vector& absMaxs)
{
	Vector mins, maxs;
	pRenderable->GetRenderBounds(mins, maxs);
	const Vector& origin = pRenderable->GetRenderOrigin();
	const QAngle& angles = pRenderable->GetRenderAngles();
	if (angles == vec3_angle)
	{
		absMins = mins + origin;
		absMaxs = maxs + origin;
		return;
	}
	matrix3x4_t toWorld;
	AngleMatrix(angles, origin, toWorld);
	TransformAABB(toWorld, mins, maxs, absMins, absMaxs);
}

namespace
{
// Allocates here because the forwarder library has no heap.
void AppendRenderable(void* pVector, IClientRenderable* pRenderable)
{
	static_cast<CUtlVector<IClientRenderable*>*>(pVector)->AddToTail(pRenderable);
}

IDetailObjectSystem* DetailSystem()
{
	return DetailObjectSystem();
}

alignas(16) unsigned char s_Storage[kSSLeafSystemBytes];
SSLeafSystemEnv s_Env;

// Built through the forwarder library's HAND table LeafSystem, slot 1 (Construct).
IClientLeafSystem* BuildLeafSystem()
{
	s_Env.ppTarget = &ShellClientDll_Managed().m_Areas.m_pLeafSystemClient;
	s_Env.ppEngine = &engine;
	s_Env.ppModelInfo = &modelinfo;
	s_Env.ppRender = &render;
	s_Env.ppGlobals = &gpGlobals;
	s_Env.pfnDetailObjectSystem = DetailSystem;
	s_Env.pfnAppendRenderable = AppendRenderable;

	const SSForwarderTable* pTable = SSFwdClient_Table("SSFwd_LeafSystem");
	if (!pTable || pTable->nSlots < 2)
		return NULL;
	typedef void* (*Construct_t)(void*, const SSLeafSystemEnv*);
	return static_cast<IClientLeafSystem*>(reinterpret_cast<Construct_t>(pTable->ppSlots[1])(s_Storage, &s_Env));
}

void* CreateLeafSystem()
{
	return static_cast<IClientLeafSystemEngine*>(g_pClientLeafSystem);
}

InterfaceReg s_LeafSystemReg(CreateLeafSystem, CLIENTLEAFSYSTEM_INTERFACE_VERSION);

// kClientSystems' per-frame walks use dynamic_cast, and libss_fwd_client is built without
// RTTI, so this RTTI adapter is the row. It forwards the three stages stock had bodies for.
class CLeafSystemGameSystem final : public CClientSystemPerFrame
{
public:
	char const* Name() override { return "CClientLeafSystem"; }

	void LevelInitPreEntity() override
	{
		if (g_pClientLeafSystem)
			g_pClientLeafSystem->LevelInitPreEntity();
	}

	void LevelShutdownPostEntity() override
	{
		if (g_pClientLeafSystem)
			g_pClientLeafSystem->LevelShutdownPostEntity();
	}

	void PreRender() override
	{
		if (g_pClientLeafSystem)
			g_pClientLeafSystem->PreRender();
	}
};

CLeafSystemGameSystem s_GameSystem;
}

IClientLeafSystem* g_pClientLeafSystem = BuildLeafSystem();

CClientSystem* ClientLeafSystemGameSystem()
{
	return &s_GameSystem;
}
