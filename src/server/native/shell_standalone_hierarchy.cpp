//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON g  leaf math over mdl/bsp data

// The server's hitbox trace thunk, and the native relays into managed parenting.
// Each relay is guarded by two engine-block words managed writes: ChildCount
// (something hangs off this entity) and ParentHandle (something carries it). An
// entity in an unparented map pays one load and a branch per position write.
// A CBaseEntity child of a standalone parent is refused, since the client could
// not rebuild that link.

#include "ss_util.h"
#include "shell_standalone.h"

#include "standalone_bone_trace.h"
#include "ss_string.h"

#include "imanaged_areas.h"
#include "server_hierarchy.h"
#include "server_entity_resolver.h"
#include "server_network.h"
#include "server_physics.h"
#include "server_entity_context.h"
#include "shell_managed_areas.h"

// TestHitboxes returning true means "I have hitboxes and tested them";
// tr.DidHit() is the hit, as in stock's CBaseAnimating::TestHitboxes. False
// leaves the trace untouched and the engine falls back to the box.
static uint32_t HandleOf(const CStandaloneEntity* pEntity);

bool CStandaloneEntity::TestHitboxes(const Ray_t& ray, unsigned int fContentsMask, trace_t& tr)
{
	IManagedBones* pBones = Context().m_Managed.m_Areas.m_pBones;

	if (!pBones)
		return false;

	float flRay[SS_BONE_RAY_FLOATS];

	for (int i = 0; i < 3; i++)
	{
		flRay[SS_BONE_RAY_START_X + i] = ray.m_Start[i];
		flRay[SS_BONE_RAY_DELTA_X + i] = ray.m_Delta[i];
		flRay[SS_BONE_RAY_START_OFFSET_X + i] = ray.m_StartOffset[i];
		flRay[SS_BONE_RAY_EXTENTS_X + i] = ray.m_Extents[i];
	}

	flRay[SS_BONE_RAY_IS_RAY] = ray.m_IsRay ? 1.0f : 0.0f;
	flRay[SS_BONE_RAY_IS_SWEPT] = ray.m_IsSwept ? 1.0f : 0.0f;

	float flTrace[SS_BONE_TRACE_FLOATS];
	int32_t nTrace[SS_BONE_TRACE_INTS];

	for (int i = 0; i < 3; i++)
	{
		flTrace[SS_BONE_TRACE_START_X + i] = tr.startpos[i];
		flTrace[SS_BONE_TRACE_END_X + i] = tr.endpos[i];
		flTrace[SS_BONE_TRACE_PLANE_NORMAL_X + i] = tr.plane.normal[i];
	}

	flTrace[SS_BONE_TRACE_PLANE_DIST] = tr.plane.dist;
	flTrace[SS_BONE_TRACE_FRACTION] = tr.fraction;

	nTrace[SS_BONE_TRACE_PLANE_TYPE] = tr.plane.type;
	nTrace[SS_BONE_TRACE_CONTENTS] = tr.contents;
	nTrace[SS_BONE_TRACE_ALL_SOLID] = tr.allsolid ? 1 : 0;
	nTrace[SS_BONE_TRACE_START_SOLID] = tr.startsolid ? 1 : 0;
	nTrace[SS_BONE_TRACE_HIT_GROUP] = tr.hitgroup;
	nTrace[SS_BONE_TRACE_HIT_BOX] = tr.hitbox;
	nTrace[SS_BONE_TRACE_PHYSICS_BONE] = tr.physicsbone;
	nTrace[SS_BONE_TRACE_SURFACE_FLAGS] = tr.surface.flags;
	nTrace[SS_BONE_TRACE_SURFACE_PROPS] = tr.surface.surfaceProps;
	nTrace[SS_BONE_TRACE_SURFACE_IS_STUDIO] = 0;

	if (!pBones->TestHitboxes(HandleOf(this), &Block(), flRay, (int32_t)fContentsMask, flTrace, nTrace))
		return false;

	for (int i = 0; i < 3; i++)
	{
		tr.startpos[i] = flTrace[SS_BONE_TRACE_START_X + i];
		tr.endpos[i] = flTrace[SS_BONE_TRACE_END_X + i];
		tr.plane.normal[i] = flTrace[SS_BONE_TRACE_PLANE_NORMAL_X + i];
	}

	tr.plane.dist = flTrace[SS_BONE_TRACE_PLANE_DIST];
	tr.fraction = flTrace[SS_BONE_TRACE_FRACTION];
	tr.plane.type = (byte)nTrace[SS_BONE_TRACE_PLANE_TYPE];
	tr.contents = nTrace[SS_BONE_TRACE_CONTENTS];
	tr.allsolid = nTrace[SS_BONE_TRACE_ALL_SOLID] != 0;
	tr.startsolid = nTrace[SS_BONE_TRACE_START_SOLID] != 0;
	tr.hitgroup = nTrace[SS_BONE_TRACE_HIT_GROUP];
	tr.hitbox = nTrace[SS_BONE_TRACE_HIT_BOX];
	tr.physicsbone = (short)nTrace[SS_BONE_TRACE_PHYSICS_BONE];
	tr.surface.flags = (unsigned short)nTrace[SS_BONE_TRACE_SURFACE_FLAGS];
	tr.surface.surfaceProps = (unsigned short)nTrace[SS_BONE_TRACE_SURFACE_PROPS];

	if (nTrace[SS_BONE_TRACE_SURFACE_IS_STUDIO])
		tr.surface.name = "**studio**";

	return true;
}

// The entity's managed side, or NULL before it has loaded: then nothing is parented.
static IManagedHierarchy* HierarchyApi(const CStandaloneEntity* pEntity)
{
	return pEntity ? pEntity->Context().m_Managed.m_Areas.m_pHierarchy : NULL;
}

static IManagedHierarchy* HierarchyApi(const CServerEntityResolver& entities)
{
	return entities.Context().m_Managed.m_Areas.m_pHierarchy;
}

// The two guard words, which only managed writes.
static bool CarriesAnything(const CStandaloneEntity* pEntity)
{
	return pEntity != NULL && pEntity->Block().Head.ChildCount != 0;
}

static bool IsCarried(const CStandaloneEntity* pEntity)
{
	return pEntity != NULL && pEntity->Block().Head.ParentHandle != (uint32_t)INVALID_EHANDLE_INDEX;
}

static uint32_t HandleOf(const CStandaloneEntity* pEntity)
{
	if (!pEntity)
		return INVALID_EHANDLE_INDEX;

	const CBaseHandle& handle = pEntity->GetRefEHandle();

	return handle.IsValid() ? (uint32_t)handle.ToInt() : (uint32_t)INVALID_EHANDLE_INDEX;
}

// A parent has moved, so its children move with it. Reached on every position
// write, hence the guard.
void StandaloneHierarchy_ParentMoved(CStandaloneEntity* pParent)
{
	if (!CarriesAnything(pParent))
		return;

	IManagedHierarchy* pApi = HierarchyApi(pParent);

	if (pApi)
		pApi->HierarchyParentMoved(HandleOf(pParent));
}

// A child moved itself, so its offset from its parent changed. From the public
// setters, never from the composition.
void StandaloneHierarchy_ChildMoved(CStandaloneEntity* pChild)
{
	if (!IsCarried(pChild))
		return;

	IManagedHierarchy* pApi = HierarchyApi(pChild);

	if (pApi)
		pApi->HierarchyChildMoved(HandleOf(pChild));
}

// Whether something carries this entity, with no crossing. Resolved, so a parent
// that has died before the managed sweep answers "nobody".
bool StandaloneHierarchy_IsParented(const CStandaloneEntity* pChild)
{
	if (!IsCarried(pChild))
		return false;

	return pChild->Context().Resolver().Resolve(pChild->Block().Head.ParentHandle).IsValid();
}

// An entity with a parent lives in its parent's frame, and code that moves it
// must integrate in that frame or it jitters. Both convert in place and return
// false, untouched, for an entity with no parent.

// Vector and QAngle cross as float triples, not structs by value.
static bool HierarchyConvert(IManagedHierarchy* pApi, const CStandaloneEntity* pChild, Vector* pOrigin, QAngle* pAngles,
							 bool bToWorld)
{
	if (!pApi || !IsCarried(pChild))
		return false;

	float flOrigin[3];
	float flAngles[3];

	if (pOrigin)
	{
		flOrigin[0] = pOrigin->x;
		flOrigin[1] = pOrigin->y;
		flOrigin[2] = pOrigin->z;
	}

	if (pAngles)
	{
		flAngles[0] = pAngles->x;
		flAngles[1] = pAngles->y;
		flAngles[2] = pAngles->z;
	}

	const uint32_t hChild = HandleOf(pChild);
	float* pOriginOut = pOrigin ? flOrigin : NULL;
	float* pAnglesOut = pAngles ? flAngles : NULL;

	if (!(bToWorld ? pApi->HierarchyToWorld(hChild, pOriginOut, pAnglesOut)
				   : pApi->HierarchyToLocal(hChild, pOriginOut, pAnglesOut)))
		return false;

	if (pOrigin)
		pOrigin->Init(flOrigin[0], flOrigin[1], flOrigin[2]);

	if (pAngles)
		pAngles->Init(flAngles[0], flAngles[1], flAngles[2]);

	return true;
}

bool StandaloneHierarchy_WorldToLocal(const CStandaloneEntity* pChild, Vector* pOrigin, QAngle* pAngles)
{
	IManagedHierarchy* pApi = HierarchyApi(pChild);

	return HierarchyConvert(pApi, pChild, pOrigin, pAngles, false);
}

// A new offset, recorded and applied. Not guarded on ParentHandle: managed places
// an entity whose parent has gone absolutely, so a movement can outlive the parent.
void StandaloneHierarchy_SetLocal(CStandaloneEntity* pChild, const Vector& vecOrigin, const QAngle& angles)
{
	if (!pChild)
		return;

	IManagedHierarchy* pApi = HierarchyApi(pChild);

	if (!pApi)
		return;

	const float flOrigin[3] = {vecOrigin.x, vecOrigin.y, vecOrigin.z};
	const float flAngles[3] = {angles.x, angles.y, angles.z};

	pApi->HierarchySetLocal(HandleOf(pChild), flOrigin, flAngles);
}

void StandaloneHierarchy_SetParentName(CStandaloneEntity* pChild, const char* pParentName)
{
	IManagedHierarchy* pApi = HierarchyApi(pChild);

	if (pChild && pApi)
		pApi->HierarchySetParentName(HandleOf(pChild), pParentName ? pParentName : "");
}

void StandaloneHierarchy_SetParentAttachmentName(CStandaloneEntity* pChild, const char* pAttachmentName)
{
	IManagedHierarchy* pApi = HierarchyApi(pChild);

	if (pChild && pApi)
	{
		pApi->HierarchySetParentAttachmentName(HandleOf(pChild), pAttachmentName ? pAttachmentName : "");
	}
}

// Which hierarchy holds the entity, and whether it can pose, as one set of bits.
// A recycled slot answers "nobody".
int32_t CServerHierarchy::EntityParentProbe(uint32_t hEntity)
{
	const SourceSharpEntity_t ent = m_Entities.Resolve(hEntity);

	if (!ent.IsValid())
		return 0;

	int32_t nBits = ent.pStandalone ? SOURCESHARP_PARENT_PROBE_STANDALONE : SOURCESHARP_PARENT_PROBE_BASE;

	// A standalone entity can always be asked for an attachment.
	if (ent.pStandalone != NULL)
		nBits |= SOURCESHARP_PARENT_PROBE_CAN_POSE;

	return nBits;
}

// Where a one-based attachment is. Managed answers standalone entities first;
// what reaches here has no skeleton, so it gets its own transform and 0.
int32_t CServerHierarchy::EntityGetAttachmentFrame(uint32_t hEntity, int32_t nAttachment, float* pOutOriginXYZ,
												   float* pOutAnglesPYR)
{
	const SourceSharpEntity_t ent = m_Entities.Resolve(hEntity);

	Vector vecOrigin(0, 0, 0);
	QAngle angAngles(0, 0, 0);

	(void)nAttachment;

	if (ent.IsValid())
	{
		MatrixAngles(ent.EntityToWorld(), angAngles, vecOrigin);
	}

	if (pOutOriginXYZ)
	{
		pOutOriginXYZ[0] = vecOrigin.x;
		pOutOriginXYZ[1] = vecOrigin.y;
		pOutOriginXYZ[2] = vecOrigin.z;
	}

	if (pOutAnglesPYR)
	{
		pOutAnglesPYR[0] = angAngles.x;
		pOutAnglesPYR[1] = angAngles.y;
		pOutAnglesPYR[2] = angAngles.z;
	}

	return 0;
}

// The engine block with its own length, always sizeof( EngineBlock_t ). Not the
// networked payload's length, which can be shorter. NULL for anything not on this
// hierarchy, including worldspawn.
void* CServerNetwork::EntityGetEngineBlock(uint32_t hEntity, int32_t* pOutSize)
{
	if (pOutSize)
		*pOutSize = 0;

	CStandaloneEntity* pEntity = m_Entities.Standalone(hEntity);

	if (!pEntity)
		return NULL;

	if (pOutSize)
		*pOutSize = (int32_t)sizeof(EngineBlock_t);

	return &pEntity->Block();
}

// Destroys a parented child's physics object. Stock would shadow it, which this
// hierarchy does not support, and a static object left behind would stay at the
// map position as an invisible hull. Not recreated on unparenting: only the
// entity knows what kind of object it wanted.
void CServerPhysics::EntityDestroyPhysics(uint32_t hEntity)
{
	if (CStandaloneEntity* pStandalone = m_Resolver.Standalone(hEntity))
		pStandalone->DestroyPhysics();
}

int32_t CServerHierarchy::EntitySetParent(uint32_t hChild, uint32_t hParent, int32_t nParentAttachment)
{
	CStandaloneEntity* pChild = m_Entities.Standalone(hChild);

	// A CBaseEntity child is refused aloud, since C# cannot tell which kind it holds.
	if (!pChild)
	{
		const SourceSharpEntity_t child = m_Entities.Resolve(hChild);

		if (child.pBase)
		{
			Warning("[SourceSharp] cannot parent '%s': it is a CBaseEntity, and an SDK "
					"child of a managed parent is not supported — see the header of "
					"shell_standalone_hierarchy.cpp\n",
					child.Classname());
		}

		return 0;
	}

	// INVALID_EHANDLE_INDEX means unparent; zero is a real handle (the world).
	IManagedHierarchy* pApi = HierarchyApi(m_Entities);

	if (!pApi)
		return 0;

	// A parent that does not resolve is a refusal, not an unparenting.
	return pApi->HierarchySetParent(hChild, hParent, nParentAttachment);
}

// Who carries this entity, from the block, with no crossing.
uint32_t CServerHierarchy::EntityGetParent(uint32_t hChild)
{
	CStandaloneEntity* pChild = m_Entities.Standalone(hChild);

	if (!IsCarried(pChild))
		return INVALID_EHANDLE_INDEX;

	const uint32_t hParent = pChild->Block().Head.ParentHandle;

	// Resolved, so a parent that has gone answers nothing.
	return m_Entities.Resolve(hParent).IsValid() ? hParent : (uint32_t)INVALID_EHANDLE_INDEX;
}

// The attachment on the parent, which the block does not carry.
int32_t CServerHierarchy::EntityGetParentAttachment(uint32_t hChild)
{
	IManagedHierarchy* pApi = HierarchyApi(m_Entities);

	if (!pApi)
		return 0;

	return pApi->HierarchyGetParentAttachment(hChild);
}

void CServerHierarchy::EntitySetLocalTransform(uint32_t hChild, const float* pOrigin, const float* pAngles)
{
	CStandaloneEntity* pChild = m_Entities.Standalone(hChild);

	if (!pChild)
		return;

	// Each half defaults to what the child already has.
	Vector vecLocal = pChild->GetAbsOrigin();
	QAngle angLocal = pChild->GetAbsAngles();

	StandaloneHierarchy_WorldToLocal(pChild, &vecLocal, &angLocal);

	if (pOrigin)
		vecLocal.Init(pOrigin[0], pOrigin[1], pOrigin[2]);

	if (pAngles)
		angLocal.Init(pAngles[0], pAngles[1], pAngles[2]);

	StandaloneHierarchy_SetLocal(pChild, vecLocal, angLocal);
}

void CServerHierarchy::EntityGetLocalTransform(uint32_t hChild, float* pOutOrigin, float* pOutAngles)
{
	CStandaloneEntity* pChild = m_Entities.Standalone(hChild);

	if (!pChild)
		return;

	// An unparented entity's local frame is its world frame.
	Vector vecLocal = pChild->GetAbsOrigin();
	QAngle angLocal = pChild->GetAbsAngles();

	StandaloneHierarchy_WorldToLocal(pChild, &vecLocal, &angLocal);

	if (pOutOrigin)
	{
		pOutOrigin[0] = vecLocal.x;
		pOutOrigin[1] = vecLocal.y;
		pOutOrigin[2] = vecLocal.z;
	}

	if (pOutAngles)
	{
		pOutAngles[0] = angLocal.x;
		pOutAngles[1] = angLocal.y;
		pOutAngles[2] = angLocal.z;
	}
}
