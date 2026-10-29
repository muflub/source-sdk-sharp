//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
// The standalone entity hierarchy's shell: what the engine demands. Decisions are
// managed code's, reached through the OnManaged* virtuals subclasses implement.

#include <cstdlib>

#include "ss_util.h"
#include "server_state.h"
#include "server_entity_context.h"
#include "standalone_list.h"

#include "standalone_collision_rules.h"

#include "slot_table.h"
#include "standalone_model_bounds.h"
#include "shell_standalone_class.h"
#include "standalone_names.h"
#include "shell_model_name.h"
#include "generic_nettable.h"

#include "ss_physics_shared.h"
#include "physicsowner.h"

#include "slot_table.h"

#include "ss_world.h"
#include "world_subject.h"
#include "engine/IEngineSound.h"

#include "managed_convar_ref.h"
#include "precache_model.h"
#include "server_physics.h"

#include "ss_string.h"
#include "ss_cpp20.h"
#include "world_aligned_bounds.h"

#include "imanaged_areas.h"
#include "ss_takedamageinfo.h"
#include "ss_entityhandle.h"
#include "shell_entity_lifecycle.h"
#include "server_collision.h"
#include "server_entity_resolver.h"
#include "edict_change.h"

// A slot outliving its entity would let C# write into freed memory, so the shell
// that frees the memory closes the slot.
void SourceSharp_EntitySlotClosed(CSourceSharpEntitySlots& slots, uint32_t hEntity);

// This map's standalone shells (CServerLevelState::m_Shells), or NULL outside a level.
static CServerEntityShells* ShellsOf(const CServerEntityContext& context)
{
	return context.m_pLevel ? &context.m_pLevel->m_Shells : NULL;
}

static CServerOverlaps* OverlapsOf(const CServerEntityContext& context)
{
	return context.m_pLevel ? &context.m_pLevel->m_Overlaps : NULL;
}

// Every shell joins the level's list, which deletes it at the level's end.
static void TrackShell(CStandaloneEntity* pEnt)
{
	CServerEntityShells* pShells = ShellsOf(pEnt->Context());
	AssertMsg(pShells, "a shell constructed outside a level would outlive it");

	if (pShells)
		pShells->m_Live.push_back(pEnt);
}

CStandaloneList StandaloneEntities(const CServerEntityContext& context)
{
	const CServerEntityShells* pShells = ShellsOf(context);
	return pShells ? CStandaloneList(pShells->m_Live.data(), pShells->m_Live.size()) : CStandaloneList();
}

IGameEntity* SourceSharpEntity_t::GameEntity() const
{
	if (pBase)
		return pBase;
	return pStandalone;
}

const Vector& SourceSharpEntity_t::Origin() const
{
	// The world is at the origin; a bare vec3_origin below means nothing resolved.
	if (pWorld)
		return pWorld->GetCollideable()->GetCollisionOrigin();

	return pStandalone ? pStandalone->GetAbsOrigin() : vec3_origin;
}

void SourceSharpEntity_t::Bounds(Vector* pOutMins, Vector* pOutMaxs) const
{
	if (pStandalone)
	{
		if (pOutMins)
			*pOutMins = pStandalone->OBBMins();
		if (pOutMaxs)
			*pOutMaxs = pStandalone->OBBMaxs();
		return;
	}

	if (!pBase)
	{
		if (pOutMins)
			*pOutMins = vec3_origin;
		if (pOutMaxs)
			*pOutMaxs = vec3_origin;
		return;
	}
}

Vector SourceSharpEntity_t::Center() const
{
	Vector vecMins, vecMaxs;
	Bounds(&vecMins, &vecMaxs);

	return Origin() + (vecMins + vecMaxs) * 0.5f;
}

int SourceSharpEntity_t::EntityIndex() const
{
	if (pWorld)
		return 0;

	if (!pStandalone)
		return 0;

	// -1 (server-only) has no engine slot; 0 is the world's index.
	const int nIndex = pStandalone->EntIndex();
	return nIndex > 0 ? nIndex : 0;
}

const matrix3x4_t& SourceSharpEntity_t::EntityToWorld() const
{
	// On this arm CollisionToWorldTransform is the entity frame: the transform is
	// built from origin and angles unconditionally, with no rotation-dropping branch.
	if (pStandalone)
		return pStandalone->CollisionToWorldTransform();

	// Nothing resolved: identity, so a transformed point stays in world space.
	static const matrix3x4_t s_matIdentity(1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f);
	return s_matIdentity;
}

const char* SourceSharpEntity_t::Classname() const
{
	if (pWorld)
		return pWorld->GetNetworkable()->GetClassName();

	return pStandalone ? pStandalone->GetClassname() : "";
}

IPhysicsObject* SourceSharpEntity_t::Physics() const
{
	return pStandalone ? pStandalone->GetPhysics() : NULL;
}

IHandleEntity* SourceSharpEntity_t::ToHandleEntity() const
{
	return pStandalone;
}

uint32_t SourceSharp_HandleFromAnyEntity(IHandleEntity* pHandleEntity)
{
	if (!pHandleEntity)
		return INVALID_EHANDLE_INDEX;

	return (uint32_t)pHandleEntity->GetRefEHandle().ToInt();
}

// The next entity of a classname after pStartAfter; gEntList's walk skips these.
CStandaloneEntity* StandaloneEntities_FindByClassname(const CServerEntityContext& context,
													  CStandaloneEntity* pStartAfter, const char* pClassName)
{
	if (!pClassName || !pClassName[0])
		return NULL;

	const CStandaloneList live = StandaloneEntities(context);
	size_t nStart = 0;

	if (pStartAfter)
	{
		CStandaloneEntity* const* it = std::find(live.begin(), live.end(), pStartAfter);

		// A removed start entity ends the iteration; restarting would never terminate.
		if (it == live.end())
			return NULL;

		nStart = (it - live.begin()) + 1;
	}

	auto it = std::find_if(live.begin() + nStart, live.end(), [pClassName](CStandaloneEntity* pEnt)
						   { return StandaloneNameMatches(pClassName, pEnt->GetClassname()); });

	return (it != live.end()) ? *it : NULL;
}

// Allocated as the engine's PvAllocEntPrivateData allocates an entity (calloc):
// zeroed memory, which SDK members without constructors rely on.
void* CStandaloneEntity::operator new(size_t nBytes, int nBlobBytes)
{
	// The blob is sized by this class's tier. nBytes is ignored: the props were
	// built against the constant blob offset, which nBytes must not exceed.
	Assert((int)nBytes <= SourceSharp_StandaloneBlobOffset());

	// Never below the smallest tier that holds the engine block.
	if (nBlobBytes < SourceSharp_StandaloneMinBlobBytes())
		nBlobBytes = SourceSharp_StandaloneMinBlobBytes();

	return calloc(1, (size_t)(SourceSharp_StandaloneBlobOffset() + nBlobBytes));
}

unsigned char* CStandaloneEntity::DataBlob()
{
	return (unsigned char*)this + SourceSharp_StandaloneBlobOffset();
}

// The engine's change accessor for the edict and the change list the context holds.
static void MarkEdictChanged(const CServerEntityContext& context, edict_t* pEdict, int nOffset)
{
	SourceSharp_MarkEdictChanged(context.m_pEngine->GetChangeAccessor(pEdict), *context.m_pChangeInfo, pEdict, nOffset);
}

void CStandaloneEntity::NetworkStateChanged()
{
	if (m_pEdict)
		MarkEdictChanged(Context(), m_pEdict, -1);
}

// Marks only the block offset that changed. StateChanged() would set
// FL_FULL_EDICT_CHANGED and copy every prop; an offset copies one. The offset is
// the field's own (a Vector is one SendProp), since the engine matches offsets
// exactly and holds at most MAX_CHANGE_OFFSETS (19) of them.
void CStandaloneEntity::MarkBlockChanged(int nBlockOffset)
{
	Assert(nBlockOffset >= 0 && nBlockOffset < (int)sizeof(EngineBlock_t));

	if (!m_pEdict)
		return;

	MarkEdictChanged(Context(), m_pEdict, BlockBase() + nBlockOffset);
}

void CStandaloneEntity::operator delete(void* pMem)
{
	free(pMem);
}

// Pairs with the sized new; reached only if a constructor throws.
void CStandaloneEntity::operator delete(void* pMem, int)
{
	free(pMem);
}

int CStandaloneEntity::BlobBytes() const
{
	return SourceSharp_TierBytes(m_iTier);
}

// BlockHandle and BlockOwner overlay a CBaseHandle on a uint32 in the block.
static_assert(sizeof(CBaseHandle) == sizeof(uint32_t),
			  "CBaseHandle is no longer one uint32; EngineBlockHead_t::Handle cannot overlay it");

int CStandaloneEntity::BlockBase()
{
	return SourceSharp_StandaloneBlobOffset();
}

// The smallest tier that holds the engine block, which the constructor writes.
// Searched, since rows are added to the shared tier table.
int SourceSharp_StandaloneMinTier()
{
	const int nCount = SourceSharp_TierCount();

	for (int i = 0; i < nCount; i++)
	{
		if (SourceSharp_TierBytes(i) >= (int)sizeof(EngineBlock_t))
			return i;
	}

	Warning("[SourceSharp] standalone: NO TIER holds the %d-byte engine block; "
			"the largest is %d bytes\n",
			(int)sizeof(EngineBlock_t), SourceSharp_TierBytes(nCount - 1));

	return nCount - 1;
}

int SourceSharp_StandaloneMinBlobBytes()
{
	return SourceSharp_TierBytes(SourceSharp_StandaloneMinTier());
}

#if defined(_MSC_VER)
// 'this' in the initialiser list is deliberate. tier1/convar.h re-enables C4355.
#pragma warning(disable : 4355)
#endif
CStandaloneEntity::CStandaloneEntity(CServerEntityContext& context)
	// First: members initialise in declaration order, and the header declares it first.
	: m_pEngineBlock(reinterpret_cast<EngineBlock_t*>(reinterpret_cast<unsigned char*>(this) + BlockBase()))
	, m_pContext(&context)
	, m_nSpawnFlags(0)
	, m_iHealth(0)
	, m_nTriggerBloat(0)
	, m_bUniformTriggerBloat(false)
	, m_vecSpawnOrigin(vec3_origin)
	, m_angSpawnRotation(0, 0, 0)
	, m_pPhysics(NULL)
	, m_vecVelocity(vec3_origin)
	, m_vecMoveVelocity(vec3_origin)
	, m_flGravityScale(0.0f)
	, m_nBouncesLeft(0)
	, m_angVelocity(0, 0, 0)
	, m_pClass(NULL)
	, m_pServerClass(NULL)
	,

	// Never -1: readers index the tier table with it.
	m_iTier(SourceSharp_StandaloneMinTier())
	, m_pEdict(NULL)
	, m_bOwnsEdict(true)
	, m_Partition(PARTITION_INVALID_HANDLE)
{
	// The block's non-zero defaults; PvAllocEntPrivateData zeroes the rest. The
	// handles above all: a zeroed handle is index 0, the world.
	EngineBlockHead_t& head = m_pEngineBlock->Head;

	head.Handle = INVALID_EHANDLE_INDEX;
	head.ParentHandle = INVALID_EHANDLE_INDEX;
	head.OwnerHandle = INVALID_EHANDLE_INDEX;
	head.ClassnameIndex = 0;
	head.ModelIndex = 0;
	head.CollisionModelIndex = 0;
	head.Origin = vec3_origin;
	head.Angles = QAngle(0, 0, 0);
	head.Mins.Init(-16, -16, -16);
	head.Maxs.Init(16, 16, 16);

	head.SolidType = SOLID_NONE;
	head.SolidFlags = FSOLID_NOT_SOLID;
	head.CollisionGroup = COLLISION_GROUP_NONE;

	head.MoveType = 0;
	head.Flags = 0;
	head.Effects = 0;
	head.WaterLevel = 0;

	// Managed owns this word from here on; native only asks whether it is zero.
	head.ChildCount = 0;

	SetIdentityMatrix(head.WorldTransform);

	// Zeroed whole: PVSInfo_t holds a pointer BuildEntityClusterList writes
	// through, and a player's shell is rebuilt in place on reconnect.
	memset(BlockPVSInfo(), 0, sizeof(PVSInfo_t));
	m_szName[0] = 0;
	m_szModel[0] = 0;
	m_szClassname[0] = 0;
	m_nTypeId = 0;
	m_nSchemaHash = 0;
	m_nPayloadWords = 0;

	// A playback rate of one: zero would freeze every animation on its first frame.
	Block().Client.Sequence = 0;
	Block().Client.PlaybackRate = 1.0f;
	Block().Client.AnimTime = 0.0f;

	// One, "as the artist built it": zero reads as a model with no extent.
	m_flModelScale = 1.0f;
}

CStandaloneEntity::~CStandaloneEntity()
{
	// First, before the handle is torn down: closing the slot turns a managed
	// state's next access into a named exception rather than a write into freed
	// memory, and after Term() the entry index can no longer be named.
	SourceSharp_EntitySlotClosed(Context().m_EntitySlots, (uint32_t)BlockHandle().ToInt());

	// The slot table too, for the same reason. SetRefEHandle unbinds only for an
	// entity on the list; a dangling binding would make the next Bind on this
	// entry refuse.
	if (BlockHandle().IsValid())
		SourceSharp_SlotUnbind(Context().m_SlotTable, BlockHandle().GetEntryIndex());

	if (CServerEntityShells* pShells = ShellsOf(*m_pContext))
	{
		ss::erase(pShells->m_Live, this);
		ss::erase(pShells->m_PendingDeletes, this);
	}

	// The solver object is native and owned here; a level change deletes shells
	// that were never marked, so it must go with the shell.
	DestroyPhysics();

	if (BlockHandle().IsValid())
	{
		BlockHandle().Term();
	}

	if (m_Partition != PARTITION_INVALID_HANDLE)
	{
		m_pContext->m_pPartition->DestroyHandle(m_Partition);
		m_Partition = PARTITION_INVALID_HANDLE;
	}

	// The engine allocated the cluster list; without this every entity leaks it.
	m_pContext->m_pEngine->CleanUpEntityClusterList(BlockPVSInfo());

	if (m_pEdict)
	{
		// SetEdict( NULL ) before RemoveEdict: the next CreateEdict that recycles
		// the slot hands its m_pUnk to FreeContainingEntity, which dereferences it.
		m_pEdict->SetEdict(NULL, false);
		m_pEdict->m_pNetworkable = NULL;

		// Only an edict this entity created. A player's edict is the engine's.
		if (m_bOwnsEdict)
			m_pContext->m_pEngine->RemoveEdict(m_pEdict);

		m_pEdict = NULL;
	}
}

// The player's edict, in transit from ClientPutInServer to PostConstruct (the DLL tier's).
void SourceSharp_SetPendingPlayerEdict(CServerPlayersDll& players, edict_t* pEdict)
{
	edict_t*& pPending = players.m_pPendingEdict;

	if (pPending && pEdict)
	{
		Warning("[SourceSharp] standalone player: pending edict overwritten before it was claimed\n");
	}

	pPending = pEdict;
}

edict_t* SourceSharp_ClaimPendingPlayerEdict(CServerPlayersDll& players)
{
	edict_t*& pPending = players.m_pPendingEdict;
	edict_t* pEdict = pPending;
	pPending = NULL;
	return pEdict;
}

void CStandaloneEntity::SetClassname(const char* pName)
{
	ss::StrCopy(m_szClassname, pName, sizeof(m_szClassname));

	// The only place the name is set, so the id, a pure function of it, is set
	// here too; a wrong id pairs the entity with another class's managed half.
	m_nTypeId = SourceSharp_HashClassName(m_szClassname);
}

void CStandaloneEntity::SetReportedClassname(const char* pName)
{
	ss::StrCopy(m_szClassname, pName, sizeof(m_szClassname));
}

uint32_t CStandaloneEntity::ManagedHandle() const
{
	const CBaseHandle& h = GetRefEHandle();
	return h.IsValid() ? h.ToInt() : 0;
}

const model_t* CStandaloneEntity::GetCollisionModel()
{
	return Block().Head.CollisionModelIndex > 0 ? m_pContext->m_pModelInfo->GetModel(Block().Head.CollisionModelIndex)
												: NULL;
}

int CStandaloneEntity::EntIndex() const
{
	return m_pEdict ? m_pContext->m_pEngine->IndexOfEdict(m_pEdict) : -1;
}

// Applied now rather than at Spawn: managed code sets a model from OnSpawn,
// which runs at the end of Spawn.
void CStandaloneEntity::SetModelName(const char* pModel)
{
	if (!pModel || !pModel[0])
		return;

	ss::StrCopy(m_szModel, pModel, sizeof(m_szModel));

	// A brush model ("*23") is already in the BSP, so it is looked up, not precached.
	Block().Head.ModelIndex = SourceSharp_IsBrushModelName(m_szModel)
		? m_pContext->m_pModelInfo->GetModelIndex(m_szModel)
		: m_pContext->m_Precache.Model(m_szModel);

	if (Block().Head.ModelIndex <= 0)
	{
		Warning("[SourceSharp] %s: no model '%s'\n", GetClassname(), m_szModel);
		return;
	}

	// Kept separately: hiding the model must not stop it being a shape.
	Block().Head.CollisionModelIndex = Block().Head.ModelIndex;

	// A brush is not centred on its origin, so it takes its bounds from the model.
	// A player keeps its movement hull (standalone_model_bounds.h).
	if (const model_t* pModelPtr = m_pContext->m_pModelInfo->GetModel(Block().Head.ModelIndex))
	{
		Vector vecMins, vecMaxs;
		m_pContext->m_pModelInfo->GetModelBounds(pModelPtr, vecMins, vecMaxs);

		if (StandaloneModel_AdoptsBounds(IsPlayer(), vecMins != vecMaxs))
			SetCollisionBounds(vecMins, vecMaxs);
	}

	// The model index is networked and nothing else on this path marks the edict:
	// mark the drawn index and the collision index beside it.
	MarkBlockChanged(SS_BLOCK_HEAD_OFFSET(ModelIndex));
	MarkBlockChanged(SS_BLOCK_HEAD_OFFSET(CollisionModelIndex));
}

// Visibility is EF_NODRAW, as stock's, not a zero model index: a hidden brush
// keeps its shape, since client effects ask it whether points are inside it.
void CStandaloneEntity::SetVisible(bool bVisible)
{
	if (!m_szModel[0])
		return;

	// A lookup, not a precache: the model was precached at spawn, and a precache
	// after level load warns.
	const int nIndex = m_pContext->m_pModelInfo->GetModelIndex(m_szModel);

	if (nIndex != Block().Head.ModelIndex)
	{
		Block().Head.ModelIndex = nIndex;
		MarkBlockChanged(SS_BLOCK_HEAD_OFFSET(ModelIndex));
	}

	const int nEffects = bVisible ? (Block().Head.Effects & ~EF_NODRAW) : (Block().Head.Effects | EF_NODRAW);

	if (nEffects != Block().Head.Effects)
	{
		Block().Head.Effects = nEffects;
		MarkBlockChanged(SS_BLOCK_HEAD_OFFSET(Effects));
	}
}

void CStandaloneEntity::SetAbsTransform(const Vector& vecOrigin, const QAngle& angles)
{
	const Vector vecPrevOrigin = Block().Head.Origin;

	Block().Head.Origin = vecOrigin;
	Block().Head.Angles = angles;

	OnPositionChanged(vecPrevOrigin);
}

// Everything that follows a move once the block has been written. Managed code
// that writes the block itself reaches this through ENTITY_OP_POSITION_CHANGED;
// vecPrevOrigin is the one thing the block no longer holds.
void CStandaloneEntity::OnPositionChanged(const Vector& vecPrevOrigin, bool bTouchTriggers)
{
	AngleMatrix(Block().Head.Angles, Block().Head.Origin, Block().Head.WorldTransform);

	if (m_Partition != PARTITION_INVALID_HANDLE)
		MovePartitionElement();

	// Only the two fields that moved. The world transform is derived and the
	// client rebuilds its own, so marking it would spend twelve of the edict's
	// nineteen change offsets.
	MarkBlockChanged(SS_BLOCK_HEAD_OFFSET(Origin));
	MarkBlockChanged(SS_BLOCK_HEAD_OFFSET(Angles));

	// Children move now, so by the time anyone sees this position they have theirs.
	StandaloneHierarchy_ParentMoved(this);

	// Last, so the sweep is from where the entity now is. Immediate, as stock's:
	// a trigger a frame late is one a fast prop has already left.
	if (bTouchTriggers)
		TouchTriggers(vecPrevOrigin);
}

// The public setters: the world has been told where this is, so a parented
// entity's offset from its parent is measured again, as CBaseEntity::SetAbsOrigin does.
void CStandaloneEntity::SetAbsOrigin(const Vector& vecOrigin)
{
	SetAbsTransform(vecOrigin, Block().Head.Angles);
	StandaloneHierarchy_ChildMoved(this);
}

void CStandaloneEntity::SetAbsAngles(const QAngle& angles)
{
	SetAbsTransform(Block().Head.Origin, angles);
	StandaloneHierarchy_ChildMoved(this);
}

// TeleportEntity's UTIL_SetOrigin without bFireTriggers: no sweep from the old origin.
void CStandaloneEntity::TeleportTo(const Vector& vecOrigin, const QAngle* pAngles)
{
	Block().Head.Origin = vecOrigin;
	if (pAngles)
		Block().Head.Angles = *pAngles;

	OnPositionChanged(vecOrigin, false);
	StandaloneHierarchy_ChildMoved(this);
}

void CStandaloneEntity::SetCollisionBounds(const Vector& vecMins, const Vector& vecMaxs)
{
	Block().Head.Mins = vecMins;
	Block().Head.Maxs = vecMaxs;

	MarkBlockChanged(SS_BLOCK_HEAD_OFFSET(Mins));
	MarkBlockChanged(SS_BLOCK_HEAD_OFFSET(Maxs));

	if (m_Partition != PARTITION_INVALID_HANDLE)
		MovePartitionElement();
}

// Declared in C#. Unregistered reads as 1, stock's behaviour; 0 skips telling
// vphysics when solidity or the collision group changes.
static CManagedConVarRef sourcesharp_collision_rules_recheck("sourcesharp_collision_rules_recheck");

static bool CollisionRulesRecheck()
{
	return !sourcesharp_collision_rules_recheck.IsValid() || sourcesharp_collision_rules_recheck.GetBool();
}

// Stock's order sets solidity before the physics object exists, so the recheck
// below is a no-op at spawn.
void CStandaloneEntity::SetSolidity(SolidType_t nSolidType, int nSolidFlags)
{
	Block().Head.SolidType = nSolidType;
	Block().Head.SolidFlags = nSolidFlags;

	MarkBlockChanged(SS_BLOCK_HEAD_OFFSET(SolidType));
	MarkBlockChanged(SS_BLOCK_HEAD_OFFSET(SolidFlags));

	UpdatePartition();

	// And tell vphysics, which caches whether two objects may touch. Stock's
	// CBaseEntity::SetSolid calls CollisionRulesChanged for the same reason.
	SourceSharpCollisionRulesChanged(m_pPhysics, CollisionRulesRecheck());
}

void CStandaloneEntity::SetCollisionGroup(int nGroup)
{
	Block().Head.CollisionGroup = nGroup;
	MarkBlockChanged(SS_BLOCK_HEAD_OFFSET(CollisionGroup));

	SourceSharpCollisionRulesChanged(m_pPhysics, CollisionRulesRecheck());
}

void CStandaloneEntity::SetModelIndex(int index)
{
	Block().Head.ModelIndex = index;
	MarkBlockChanged(SS_BLOCK_HEAD_OFFSET(ModelIndex));
}

// Which spatial partition lists it belongs to, by CCollisionProperty's rule; the
// engine reads these lists. NON_STATIC: solid or trigger. SOLID: what traces hit.
// TRIGGER: what SolidMoved reports overlaps with.
void CStandaloneEntity::UpdatePartition()
{
	const bool bIsSolid = (Block().Head.SolidType != SOLID_NONE) || (Block().Head.SolidFlags & FSOLID_TRIGGER) != 0;

	if (!bIsSolid)
	{
		if (m_Partition != PARTITION_INVALID_HANDLE)
		{
			m_pContext->m_pPartition->DestroyHandle(m_Partition);
			m_Partition = PARTITION_INVALID_HANDLE;
		}

		return;
	}

	if (m_Partition == PARTITION_INVALID_HANDLE)
	{
		m_Partition = m_pContext->m_pPartition->CreateHandle(static_cast<IServerEntity*>(this));
	}

	m_pContext->m_pPartition->Remove(PARTITION_ENGINE_NON_STATIC_EDICTS | PARTITION_ENGINE_SOLID_EDICTS |
										 PARTITION_ENGINE_TRIGGER_EDICTS,
									 m_Partition);

	SpatialPartitionListMask_t mask = PARTITION_ENGINE_NON_STATIC_EDICTS;

	if (!(Block().Head.SolidFlags & FSOLID_NOT_SOLID))
		mask |= PARTITION_ENGINE_SOLID_EDICTS;

	if (Block().Head.SolidFlags & FSOLID_TRIGGER)
		mask |= PARTITION_ENGINE_TRIGGER_EDICTS;

	m_pContext->m_pPartition->Insert(mask, m_Partition);
	MovePartitionElement();
}

// The frame's overlap report: native knows who overlaps whom (the partition) and
// what the solver holds open; managed diffs the pairs against its touch links.
// The partition walk is the engine's own TriggerMoved, so "is this inside that"
// has one answer.

// Said once per level: the list only fills when managed never reads it.
static void WarnOverlapsFull(bool bFirstOverflow)
{
	if (bFirstOverflow)
		Warning("[SourceSharp/Touch] the overlap list reached %d pairs without being "
				"read. Managed code is not calling EntityOverlapReport, so no touch, "
				"trigger or pickup will fire.\n",
				CServerOverlaps::kMaxPairs);
}

// Drops everything: on a level change every handle names a departing entity.
void StandaloneEntities_ClearOverlaps(const CServerEntityContext& context)
{
	if (CServerOverlaps* pOverlaps = OverlapsOf(context))
		pOverlaps->Clear();
}

// The engine says these two overlap (MarkEntitiesAsTouching), reported when
// either side moves. Recorded for the frame's report; managed decides what it means.
void CStandaloneEntity::Touch(const Toucher_t& toucher)
{
	if (!toucher.IsValid())
		return;

	if (CServerOverlaps* pOverlaps = OverlapsOf(*m_pContext))
		WarnOverlapsFull(
			pOverlaps->Append(pOverlaps->m_Pending, GetRefEHandle().ToInt(), toucher.GetRefEHandle().ToInt()));
}

// The solver says something entered one of our physics triggers. Said once, so
// the pair is held and reported every frame until EndTouch.
void CStandaloneEntity::StartTouch(const Toucher_t& toucher)
{
	if (!toucher.IsValid())
		return;

	if (CServerOverlaps* pOverlaps = OverlapsOf(*m_pContext))
		WarnOverlapsFull(
			pOverlaps->Append(pOverlaps->m_SolverHeld, GetRefEHandle().ToInt(), toucher.GetRefEHandle().ToInt()));
}

// The solver says it left. Whether the pair still overlaps is the engine's
// report and the managed diff's business.
void CStandaloneEntity::EndTouch(const Toucher_t& toucher)
{
	if (!toucher.IsValid())
		return;

	if (CServerOverlaps* pOverlaps = OverlapsOf(*m_pContext))
		pOverlaps->Forget(pOverlaps->m_SolverHeld, GetRefEHandle().ToInt(), toucher.GetRefEHandle().ToInt());
}

// Builds the frame's overlap pairs once, from the engine's TriggerMoved walk over
// every standalone trigger, the eager reports and the solver's held pairs, and
// copies up to nMaxPairs. Returns the total, which may exceed nMaxPairs; asking
// again with a bigger buffer returns the same list.
int32_t CServerCollision::EntityOverlapReport(uint32_t* pOutPairs, int32_t nMaxPairs)
{
	// NULL outside a level: a report then has nowhere to go.
	CServerOverlaps* pOverlaps = OverlapsOf(m_Entities.Context());

	if (!pOverlaps)
		return 0;

	std::vector<uint32_t>& report = pOverlaps->m_Report;

	if (m_pGlobals->framecount != pOverlaps->m_nReportFrame)
	{
		pOverlaps->m_nReportFrame = m_pGlobals->framecount;
		report.clear();

		WarnOverlapsFull(pOverlaps->DrainPending(report));

		for (CStandaloneEntity* pEnt : StandaloneEntities(m_Entities.Context()))
		{
			if (!(pEnt->GetSolidFlags() & FSOLID_TRIGGER))
				continue;

			pEnt->TouchTriggers(pEnt->GetAbsOrigin());
		}

		// The walk reports through MarkEntitiesAsTouching, into m_Pending.
		WarnOverlapsFull(pOverlaps->DrainPending(report));

		const std::vector<uint32_t>& held = pOverlaps->m_SolverHeld;
		for (size_t i = 0; i + 1 < held.size(); i += 2)
			WarnOverlapsFull(pOverlaps->Append(report, held[i], held[i + 1]));
	}

	const int32_t nPairs = (int32_t)report.size() / 2;

	if (pOutPairs && nMaxPairs > 0)
	{
		const int32_t nWrite = (nPairs < nMaxPairs) ? nPairs : nMaxPairs;

		for (int32_t i = 0; i < nWrite * 2; i++)
			pOutPairs[i] = report[i];
	}

	return nPairs;
}

Toucher_t CStandaloneEntity::AsToucher()
{
	Toucher_t toucher = {static_cast<IHandleEntity*>(static_cast<IServerEntity*>(this)), NULL, this};

	return toucher;
}

// Which triggers this is standing in, asked of the engine. Stock's
// CBaseEntity::PhysicsTouchTriggers, including that a moving trigger is not
// checked against other triggers, and the bail-out for a SOLID_BSP with no brush.
void CStandaloneEntity::TouchTriggers(const Vector& vecPrevOrigin)
{
	// The engine indexes both calls by edict, so a server-only entity is skipped;
	// so is one not yet in the partition (between construction and Spawn).
	if (!m_pEdict || m_Partition == PARTITION_INVALID_HANDLE)
		return;

	const bool bIsTrigger = (Block().Head.SolidFlags & FSOLID_TRIGGER) != 0;
	const bool bIsSolid = (Block().Head.SolidType != SOLID_NONE) && !(Block().Head.SolidFlags & FSOLID_NOT_SOLID);

	if (Block().Head.SolidType == SOLID_BSP && Block().Head.CollisionModelIndex <= 0)
		return;

	if (bIsTrigger)
	{
		m_pContext->m_pEngine->TriggerMoved(m_pEdict, true);
		return;
	}

	if (bIsSolid)
		m_pContext->m_pEngine->SolidMoved(m_pEdict, this, &vecPrevOrigin, true);
}

// The volume the engine tests trigger overlap against; a point here would make
// every trigger overlap nothing.
void CStandaloneEntity::WorldSpaceTriggerBounds(Vector* pMins, Vector* pMaxs) const
{
	WorldCollisionBounds(pMins, pMaxs);
	SourceSharp_TriggerBounds(Block().Head.SolidFlags, m_nTriggerBloat, m_bUniformTriggerBloat, pMins, pMaxs);
}

void CStandaloneEntity::WorldSpaceSurroundingBounds(Vector* pMins, Vector* pMaxs)
{
	WorldCollisionBounds(pMins, pMaxs);
	SourceSharp_SurroundingBounds(Block().Head.SolidFlags, m_nTriggerBloat, m_bUniformTriggerBloat, pMins, pMaxs);
}

void CStandaloneEntity::SetTriggerBloat(int nBloat, bool bUniform)
{
	m_nTriggerBloat = nBloat;
	m_bUniformTriggerBloat = bUniform;
	UpdatePartition();
}

// The box the world sees: turned with the entity unless it is world-aligned
// (CCollisionProperty::CollisionAABBToWorldAABB).
void CStandaloneEntity::WorldCollisionBounds(Vector* pMins, Vector* pMaxs) const
{
	Vector vecMins, vecMaxs;
	SourceSharp_CollisionBoundsToWorld(Block().Head.SolidType, Block().Head.SolidFlags, Block().Head.Mins,
									   Block().Head.Maxs, Block().Head.Origin, Block().Head.Angles, &vecMins, &vecMaxs);
	if (pMins)
		*pMins = vecMins;
	if (pMaxs)
		*pMaxs = vecMaxs;
}

void CStandaloneEntity::MovePartitionElement()
{
	// The surrounding box, as CCollisionProperty::UpdatePartition files it.
	Vector vecMins, vecMaxs;
	WorldCollisionBounds(&vecMins, &vecMaxs);
	SourceSharp_SurroundingBounds(Block().Head.SolidFlags, m_nTriggerBloat, m_bUniformTriggerBloat, &vecMins, &vecMaxs);
	m_pContext->m_pPartition->ElementMoved(m_Partition, vecMins, vecMaxs);
}

// Stock's SetRefEHandle mirrors the handle's serial into the edict, which tells
// the engine the slot changed occupants (standalone_netserial.h).
void CStandaloneEntity::SetRefEHandle(const CBaseHandle& handle)
{
	// The ten bits restated in the engine-free header, pinned to stock's.
	COMPILE_TIME_ASSERT(STANDALONE_NETSERIAL_BITS == NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS);

	// The slot table adopts the handle the entity list assigned; SlotBindAt
	// mirrors the serial. Unbind first: this runs again with INVALID_EHANDLE on
	// removal, and BindAt refuses a bound entry. `this` is the concrete
	// CStandaloneEntity *, since the table static_casts it back.
	if (BlockHandle().IsValid())
		SourceSharp_SlotUnbind(Context().m_SlotTable, BlockHandle().GetEntryIndex());

	BlockHandle() = handle;

	MarkBlockChanged(SS_BLOCK_HEAD_OFFSET(Handle));

	if (handle.IsValid())
	{
		SourceSharp_SlotBindAt(Context().m_SlotTable, handle, this, m_pEdict);
	}
	else
	{
		StandaloneNetSerial_Mirror(m_pEdict, (unsigned long)handle.GetSerialNumber());
	}
}

// Bound to its class and given a handle and an edict before the map hands it a
// key: the managed instance is keyed by the handle and created on the first
// keyvalue, and AddNetworkableEntity is indexed by the edict.
void CStandaloneEntity::PostConstruct(const StandaloneClassDesc_t* pClass)
{
	m_pClass = pClass;

	// The tier the factory sized the allocation from; looking it up again could
	// disagree after a managed reload. Never below the block's tier.
	if (pClass)
		m_iTier = (pClass->iTier >= 0) ? pClass->iTier : SourceSharp_StandaloneMinTier();

	// A blob shorter than the class's published layout means the entity was
	// created before its type was published, and managed state would overrun it.
	// Warned once per class. Named from the desc: SetClassname runs further down.
	const char* pName = (pClass && pClass->pClassName) ? pClass->pClassName : "";
	const SourceSharpNetType_t* pType = m_pContext->m_NetTables.m_Types.ByName(pName);

	if (pType && pType->nLayoutBytes > BlobBytes())
	{
		CServerLevelState* pLevel = m_pContext->m_pLevel;

		if (pLevel && !pLevel->m_bWarnedShortBlob)
		{
			pLevel->m_bWarnedShortBlob = true;
			Warning("[SourceSharp] standalone '%s': allocated a %d-byte blob (tier %d) for a "
					"class whose published layout is %d bytes. Its managed state would write "
					"past the end of the allocation. This is a startup ORDERING fault -- the "
					"entity was created before its type was published.\n",
					pName, BlobBytes(), m_iTier, pType->nLayoutBytes);
		}
	}

	if (pClass && pClass->pClassName)
		SetClassname(pClass->pClassName);

	// Set before the map's keys, which may change it; Spawn applies the result.
	if (pClass)
	{
		Block().Head.SolidType = pClass->nSolidType;
		Block().Head.SolidFlags = pClass->nSolidFlags;
	}

	// Server-only classes are shell-less, so no route builds one of these; the
	// engine-invisible handle range belongs to the managed EntityDirectory. Said
	// loudly, and the entity holds no handle.
	if (pClass && pClass->bServerOnly)
	{
		Warning("[SourceSharp] standalone '%s' is a SERVER-ONLY class with a shell. "
				"This build has no route that creates one (step 2e made every "
				"server-only class shell-less), so there is no engine-invisible "
				"entry to bind it at: the engine-invisible half of the handle "
				"range belongs to the managed EntityDirectory. The entity is "
				"constructed and holds NO handle -- nothing will resolve it.\n",
				pClass->pClassName ? pClass->pClassName : "?");

		TrackShell(this);
		return;
	}

	// A player binds to the edict the engine allocated for the client; everything
	// else creates its own. Claimed unconditionally so a pending edict never leaks.
	m_pEdict = SourceSharp_ClaimPendingPlayerEdict(m_pContext->m_Players);
	m_bOwnsEdict = (m_pEdict == NULL);

	if (!m_pEdict)
		m_pEdict = m_pContext->m_pEngine->CreateEdict();

	if (!m_pEdict)
	{
		Warning("[SourceSharp] standalone: no free edict for '%s'\n", GetClassname());
		return;
	}

	m_pEdict->SetEdict(this, true);
	m_pEdict->m_EdictIndex = m_pContext->m_pEngine->IndexOfEdict(m_pEdict);

	// SetEdict stores only m_pUnk. The edict's separate networkable pointer is
	// read unguarded by GetNetworkable, AreaNum and GetClassName, as
	// CBaseEntity::PostConstructor sets it.
	m_pEdict->m_pNetworkable = this;

	// Into the slot table, which makes a handle to it resolve. The entry is the
	// edict index. SlotIssue, not SlotBind: SetRefEHandle is the single binder.
	const int nEntry = m_pContext->m_pEngine->IndexOfEdict(m_pEdict);
	const CBaseHandle hIssued = SourceSharp_SlotIssue(Context().m_SlotTable, nEntry);

	// An occupied entry is a bug (stock only asserted). Said loudly: an entity
	// without a handle exists but cannot be named or found.
	if (!hIssued.IsValid())
	{
		Warning("[SourceSharp] standalone '%s': entry %d could not issue a "
				"handle -- it is out of range or ALREADY BOUND to another "
				"shell. This entity will exist and resolve to nothing: no "
				"handle to it resolves and no find-by-name reaches it. "
				"cs_sv_slot_table's refusal counter is the other half of this.\n",
				(pClass && pClass->pClassName) ? pClass->pClassName : GetClassname(), nEntry);
	}

	SetRefEHandle(hIssued);

	TrackShell(this);
}

void CStandaloneEntity::Spawn()
{
	// Remembered before anything can move it: movement is relative to where
	// the mapper drew the entity.
	m_vecSpawnOrigin = Block().Head.Origin;
	m_angSpawnRotation = Block().Head.Angles;

	AngleMatrix(Block().Head.Angles, Block().Head.Origin, Block().Head.WorldTransform);

	if (m_szModel[0])
	{
		char szModel[sizeof(m_szModel)];
		ss::StrCopy(szModel, m_szModel, sizeof(szModel));
		SetModelName(szModel);
	}

	if (m_pEdict)
	{
		// The engine only transmits what it has been told changed.
		MarkEdictChanged(Context(), m_pEdict, -1);

		// And CheckTransmit decides from DONTSEND|ALWAYS|PVSCHECK|FULLCHECK
		// alone, none of which SetEdict sets. CBaseEntity sets this through
		// SetTransmitState; this entity says so itself.
		m_pEdict->ClearTransmitState();
		m_pEdict->m_fStateFlags |= FL_EDICT_PVSCHECK;

		// Which visibility clusters it is in; CServerNetworkProperty does this
		// for stock entities through RecomputePVSInformation.
		m_pContext->m_pEngine->BuildEntityClusterList(m_pEdict, BlockPVSInfo());
	}

	UpdatePartition();

	OnManagedSpawn();
}

// The map's keys. Both the shell and managed code see every key: the shell's
// m_iHealth and a managed class's health field both want the map's number.
bool CStandaloneEntity::KeyValue(const char* szKeyName, const char* szValue)
{
	// Two statements, not `||`: the offer to managed code is unconditional,
	// including for keys the shell claims.
	const bool bShell = KeyValueShellOnly(szKeyName, szValue);
	const bool bManaged = OnManagedKeyValue(szKeyName, szValue);

	return bShell || bManaged;
}

// The shell's own arm, without the offer to managed code; the managed map
// spawner has already delivered every key.
bool CStandaloneEntity::KeyValueShellOnly(const char* szKeyName, const char* szValue)
{
	bool bShell = false;

	if (!ss::StrICmp(szKeyName, "origin"))
	{
		// Recorded, not applied: Spawn() places it once every key is in.
		float x = 0.0f, y = 0.0f, z = 0.0f;
		if (sscanf(szValue, "%f %f %f", &x, &y, &z) == 3)
			Block().Head.Origin.Init(x, y, z);

		bShell = true;
	}
	else if (!ss::StrICmp(szKeyName, "angles"))
	{
		float p = 0.0f, y = 0.0f, r = 0.0f;
		if (sscanf(szValue, "%f %f %f", &p, &y, &r) == 3)
			Block().Head.Angles.Init(p, y, r);

		bShell = true;
	}
	else if (!ss::StrICmp(szKeyName, "targetname"))
	{
		ss::StrCopy(m_szName, szValue, sizeof(m_szName));
		bShell = true;
	}
	else if (!ss::StrICmp(szKeyName, "parentname"))
	{
		// Every entity can be parented, so the shell claims it. Recorded, not
		// resolved: the parent may not exist yet and neither end has its position
		// until Spawn. StandaloneHierarchy_ResolveParentNames finishes it.
		StandaloneHierarchy_SetParentName(this, szValue);
		bShell = true;
	}
	else if (!ss::StrICmp(szKeyName, "parentattachment"))
	{
		// Recorded for the same reasons; it may also arrive before parentname.
		StandaloneHierarchy_SetParentAttachmentName(this, szValue);
		bShell = true;
	}
	else if (!ss::StrICmp(szKeyName, "spawnflags"))
	{
		m_nSpawnFlags = atoi(szValue);
		bShell = true;
	}
	else if (!ss::StrICmp(szKeyName, "health"))
	{
		m_iHealth = atoi(szValue);
		bShell = true;
	}
	else if (!ss::StrICmp(szKeyName, "model"))
	{
		ss::StrCopy(m_szModel, szValue, sizeof(m_szModel));
		bShell = true;
	}

	return bShell;
}

int CStandaloneEntity::TakeDamage(const ss::CTakeDamageInfo& info)
{
	// Managed code answers how much landed; zero is how it refuses damage.
	const int nTaken = OnManagedDamage(info);

	if (nTaken <= 0)
		return 0;

	m_iHealth -= nTaken;

	if (m_iHealth <= 0)
		OnManagedKilled(info.GetAttacker(Context().Resolver()));

	return nTaken;
}

// The physics object is made by managed code and held here, because the engine
// and the shell read it every frame. Its teardown stays with the hold.
IPhysicsEnvironment* CStandaloneEntity::PhysicsEnvironment() const
{
	return m_pContext->m_pPhysics ? m_pContext->m_pPhysics->Environment() : NULL;
}

void CStandaloneEntity::DestroyPhysics()
{
	if (!m_pPhysics)
		return;

	// Clear the owner first: DestroyObject is deferred while the solver steps, so
	// the object may still reach a callback after the entity is gone.
	m_pPhysics->SetGameData(NULL);

	if (IPhysicsEnvironment* pEnv = PhysicsEnvironment())
		pEnv->DestroyObject(m_pPhysics);

	m_pPhysics = NULL;

	m_vecMoveVelocity = m_vecVelocity;
}

void CStandaloneEntity::VPhysicsUpdate(IPhysicsObject* pObject)
{
	// CBaseEntity::VPhysicsUpdate's switch: only a MOVETYPE_VPHYSICS entity takes its
	// object's pose. A mover's shadow follows the entity, never the other way.
	if (pObject == m_pPhysics && Block().Head.MoveType == MOVETYPE_VPHYSICS)
		UpdatePhysics();
}

void CStandaloneEntity::UpdatePhysics()
{
	if (!m_pPhysics)
		return;

	Vector vecOrigin;
	QAngle angRotation;
	m_pPhysics->GetPosition(&vecOrigin, &angRotation);

	// Origin and angles in one write, so the transform matches the angles.
	SetAbsTransform(vecOrigin, angRotation);
}

void CStandaloneEntity::SetVelocity(const Vector& vecVelocity)
{
	m_vecVelocity = vecVelocity;

	// And the velocity a rider's ground reads, this tick, as stock's Teleport writes both.
	m_vecMoveVelocity = vecVelocity;
}

void CStandaloneEntity::SetOwnerHandle(uint32_t hOwner)
{
	BlockOwner() = (hOwner == INVALID_EHANDLE_INDEX) ? CBaseHandle() : CBaseHandle::UnsafeFromIndex((int)hOwner);

	MarkBlockChanged(SS_BLOCK_HEAD_OFFSET(OwnerHandle));
}

// What EntityGetAngularVelocity reads back for an entity the solver does not move.
void CStandaloneEntity::SetAngularVelocity(const QAngle& angPerSecond)
{
	m_angVelocity = angPerSecond;
}

// Deferred, always: deleting inside the entity's own damage handler or think
// frees it while callers still hold it. Managed code's EntityDirectory takes the
// mark and frees at the end of the frame; this list is the fall-back for a shell
// the directory never bound.
void CStandaloneEntity::Remove()
{
	const CBaseHandle& handle = GetRefEHandle();

	if (handle.IsValid())
	{
		IManagedEntities* pApi = m_pContext->m_Managed.m_Areas.m_pEntities;

		if (pApi && pApi->EntityRemoveRequested((uint32_t)handle.ToInt()) != 0)
			return;
	}

	CServerEntityShells* pShells = ShellsOf(*m_pContext);
	AssertMsg(pShells, "a shell removed outside a level");

	if (pShells &&
		std::find(pShells->m_PendingDeletes.begin(), pShells->m_PendingDeletes.end(), this) ==
			pShells->m_PendingDeletes.end())
		pShells->m_PendingDeletes.push_back(this);
}

// Frees the shells behind a frame's removals, one crossing per frame. A handle
// that no longer resolves is skipped: a parent freed earlier in the batch takes
// its children, and the serial check keeps a recycled entry's new occupant safe.
void CServerEntityLifecycle::DestroyShell(const uint32_t* pHandles, int32_t nCount)
{
	if (!pHandles || nCount <= 0)
		return;

	for (int32_t i = 0; i < nCount; i++)
	{
		const SourceSharpEntity_t ent = m_Entities.Resolver().Resolve(pHandles[i]);

		if (ent.pStandalone)
			delete ent.pStandalone;
	}
}

void StandaloneEntities_Finish(const CServerEntityContext& context)
{
	// From the tail: a destructor removes itself from this list, and removing a
	// parent can append its children.
	CServerEntityShells* pShells = ShellsOf(context);

	if (!pShells)
		return;

	while (!pShells->m_PendingDeletes.empty())
		delete pShells->m_PendingDeletes.back();
}

// Removes every standalone entity at level change; gEntList cannot see them, so
// they would otherwise survive into the next map.
void StandaloneEntities_LevelShutdown(const CServerEntityContext& context)
{
	// From the end: the destructor removes the entity from the list being walked.
	// Managed code has already emptied the parent links.
	if (CServerEntityShells* pShells = ShellsOf(context))
	{
		for (int i = (int)pShells->m_Live.size() - 1; i >= 0; i--)
			delete pShells->m_Live[i];

		pShells->m_Live.clear();
		pShells->m_PendingDeletes.clear();
	}

	// Every handle in it names a destroyed entity, and a recycled entry would
	// resolve to its new tenant.
	StandaloneEntities_ClearOverlaps(context);
}
