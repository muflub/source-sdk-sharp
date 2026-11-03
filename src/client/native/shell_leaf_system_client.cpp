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
#include "shell_detail_props.h"
#include "shell_leaf_system_client.h"
#include "shell_client_engine_interfaces.h"

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
}

IDetailObjectSystem* CLeafSystemClient::DetailSystem()
{
	return &m_DetailProps;
}

// Built through the forwarder library's HAND table LeafSystem, slot 1 (Construct).
CLeafSystemClient::CLeafSystemClient(const CManagedAreasClient& areas, const CClientEngineInterfaces& engines,
									 CShellDetailProps& detailProps)
	: m_DetailProps(detailProps)
{
	m_Env.ppTarget = &areas.m_pLeafSystemClient;
	m_Env.ppEngine = &engines.m_pEngine;
	m_Env.ppModelInfo = &engines.m_pModelInfo;
	m_Env.ppRender = &engines.m_pRender;
	m_Env.ppGlobals = &engines.m_pGlobals;
	m_Env.pfnDetailObjectSystem = m_DetailSystem.Get();
	m_Env.pfnAppendRenderable = AppendRenderable;

	const SSForwarderTable* pTable = SSFwdClient_Table("SSFwd_LeafSystem");
	if (pTable && pTable->nSlots >= 2)
	{
		typedef void* (*Construct_t)(void*, const SSLeafSystemEnv*);
		m_pSystem =
			static_cast<IClientLeafSystem*>(reinterpret_cast<Construct_t>(pTable->ppSlots[1])(m_Storage, &m_Env));
	}
}

void CLeafSystemClient::CSystemRow::LevelInitPreEntity()
{
	if (IClientLeafSystem* pSystem = m_Leaf.System())
		pSystem->LevelInitPreEntity();
}

void CLeafSystemClient::CSystemRow::LevelShutdownPostEntity()
{
	if (IClientLeafSystem* pSystem = m_Leaf.System())
		pSystem->LevelShutdownPostEntity();
}

void CLeafSystemClient::CSystemRow::PreRender()
{
	if (IClientLeafSystem* pSystem = m_Leaf.System())
		pSystem->PreRender();
}
