//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"

#include "client_entity.h"

#include "cdll_bounded_cvars.h"
#include "iviewrender_beams.h"
#include "dlight.h"
#include "iefx.h"

#include "model_types.h"

#include "shell_standalone_client.h"
#include "standalone_renderable_handle.h"

#include "slot_table.h"
#include "shell_cliententitylist.h"
#include "shell_surface_render.h"
#include "standalone_nettable.h"
#include "said_once_per_id.h"
#include "shell_effect_ledger.h"
#include "shell_shadows_client.h"

#include "imanaged_areas_client.h"

#include "shell_beam_render.h"
#include "world_aligned_bounds.h"

static CUtlVector<C_StandaloneEntity *> g_StandaloneClients;

// Every viewmodel-pass entity rides the eye: the viewmodel and its bone-merged attachments.
static CUtlVector<C_StandaloneEntity *> s_pViewModels;

static C_StandaloneEntity *s_pLocalPlayerShell = NULL;

C_StandaloneEntity *SourceSharp_LocalPlayerShell()
{
	return s_pLocalPlayerShell;
}

C_StandaloneEntity::C_StandaloneEntity( int nEntNum, int nSerialNum, ClientClass *pClass, int iTier )
	: m_nEntIndex( nEntNum )
	, m_bHasManaged( false )
	, m_bDrawReported( false )
	, m_ModelInstance( MODEL_INSTANCE_INVALID )
	, m_hRenderHandle( INVALID_CLIENT_RENDER_HANDLE )
	,

	// The think list's invalid handle (its unsigned short InvalidIndex).
	m_hThink( (ClientThinkHandle_t)(uintp)0xFFFF )
	,

	// Before the entity joins the list at the end of this constructor.
	m_pClientClass( pClass )
	,

	// Clamped: it indexes the tier table.
	m_iTier( ( iTier >= 0 && iTier < SourceSharp_TierCount() ) ? iTier : 0 )
	, m_bPlayerShell( false )
	, m_nPredictedHealth( -1 )
	, m_bLocalPlayer( false )
	, m_pSurface( NULL )
	, m_flModelScale( 1.0f )
	, m_nOverlayModelIndex( -1 )
	, m_bIgnoreZ( false )
	, m_nPublishedModelIndex( -1 )
	, m_nPublishedEffects( -1 )
	, m_nAppliedPolicy( 0 )
	, m_flSeqDuration( 0.0f )
	, m_bSeqLooping( false )
	,

	m_nTypeId( 0 )
	, m_nSchemaHash( 0 )
	, m_nPayloadWords( 0 )
	, m_nResolvedTypeId( 0xFFFFFFFFu )
	,

	// Never a real type id, so the first EnsureManagedHalf always asks.
	m_nManagedTriedTypeId( 0xFFFFFFFFu )
{
	// operator new hands back zeroed memory, so the block's non-zero defaults are
	// written here. The handle matters most: a zeroed handle is index 0, the world,
	// and the engine resolves handles on a trace path with no null test.
	EngineBlockHead_t &head = Block().Head;

	head.Handle = INVALID_EHANDLE_INDEX;
	head.ParentHandle = INVALID_EHANDLE_INDEX;
	head.OwnerHandle = INVALID_EHANDLE_INDEX;

	// A zero-sized box would be partitioned into nothing and culled as a point.
	head.Mins.Init( -16, -16, -16 );
	head.Maxs.Init( 16, 16, 16 );

	SetIdentityMatrix( head.WorldTransform );

	EngineBlockClientTail_t &tail = Block().Client;

	// A zeroed colour draws black.
	tail.RenderColorR = 255;
	tail.RenderColorG = 255;
	tail.RenderColorB = 255;
	tail.RenderColorA = 255;

	tail.FxBlend = 255;

	tail.PlaybackRate = 1.0f;

	tail.ShadowChoice = STANDALONE_SHADOW_CHOICE_AUTO;

	// -1 is no model; zero is mod_bad.
	tail.ModelType = -1;

	BlockHandle().Init( nEntNum, nSerialNum );
	m_szClassname[0] = 0;

	// Without this binding the engine never unpacks into the entity.
	SourceSharp_ClientEntityBind( this, nEntNum, nSerialNum );

	g_StandaloneClients.AddToTail( this );
}

C_StandaloneEntity::~C_StandaloneEntity()
{
	// The eye pin must not chase a freed entity.
	s_pViewModels.FindAndRemove( this );

	if ( s_pLocalPlayerShell == this )
		s_pLocalPlayerShell = NULL;

	delete m_pSurface;
	m_pSurface = NULL;

	RemoveFromLeafSystem();

	// As C_BaseEntity::Term does. `beams` is NULL between ShutdownBeams and the
	// next InitBeams.
	if ( beams )
	{
		SourceSharp_KillDeadBeams( GetRefEHandle() );
	}

	if ( m_ModelInstance != MODEL_INSTANCE_INVALID )
	{
		modelrender->DestroyInstance( m_ModelInstance );
		m_ModelInstance = MODEL_INSTANCE_INVALID;
	}

	IManagedStandaloneClient *pStandalone = ManagedStandaloneClient();

	if ( m_bHasManaged && pStandalone )
		pStandalone->OnStandaloneRemoved( (uint32_t)BlockHandle().ToInt() );

	// The entry is nameable only until the stock list's RemoveEntity invalidates it.
	SourceSharp_ClientEntityUnbind( this );

	g_StandaloneClients.FindAndFastRemove( this );
}

// Called from the type id's recv proxy during the decode, and from
// PostDataUpdate as a fallback: the client cannot know the class at create time.
bool C_StandaloneEntity::EnsureManagedHalf()
{
	if ( m_szClassname[0] == 0 || m_nTypeId != m_nResolvedTypeId )
	{
		const SourceSharpNetType_t *pType = SourceSharp_NetTypes().ById( m_nTypeId );

		m_nResolvedTypeId = m_nTypeId;
		Q_strncpy( m_szClassname, pType ? pType->szClass : "", sizeof( m_szClassname ) );

		if ( !pType && m_nTypeId != 0 )
		{
			// A server-only class: normal, and its shell still draws by model index.
			static CSaidOncePerId s_SaidUnknown;

			if ( s_SaidUnknown.FirstTime( m_nTypeId ) )
			{
				Msg( "[SourceSharp] standalone %d: type id 0x%08x is not published on this "
					 "client; it has no managed half here (fine for a server-only class; "
					 "said once per type)\n",
					 m_nEntIndex, m_nTypeId );
			}
		}
	}

	// Latched on the type id: most entities have no managed half, and asking every
	// update would cost a crossing each.
	if ( m_bHasManaged || m_szClassname[0] == 0 || m_nTypeId == m_nManagedTriedTypeId )
		return m_bHasManaged;

	IManagedStandaloneClient *pStandalone = ManagedStandaloneClient();

	// Not latched: with no managed runtime yet this is not an answer about the entity.
	if ( !pStandalone )
		return false;

	m_nManagedTriedTypeId = m_nTypeId;

	m_bHasManaged = pStandalone->OnStandaloneCreate( (uint32_t)BlockHandle().ToInt(), m_szClassname ) != 0;

	return m_bHasManaged;
}

// The engine calls this right after unpacking; OnDataChanged reaches only
// entities that registered for it, which this one does not.
void C_StandaloneEntity::PostDataUpdate( DataUpdateType_t updateType )
{
	NoteNetworkedPosition();

	OnPositionChanged();

	IManagedStandaloneClient *pStandalone = ManagedStandaloneClient();

	// The recv proxy may already have done this; a late-published type pairs here.
	const bool bHadManaged = m_bHasManaged;

	EnsureManagedHalf();

	const bool bJustMade = m_bHasManaged && !bHadManaged;

	if ( updateType == DATA_UPDATE_CREATED )
	{
		if ( m_bPlayerShell && engine->GetLocalPlayer() == entindex() )
		{
			m_bLocalPlayer = true;
			s_pLocalPlayerShell = this;

			Msg( "[SourceSharp] standalone player: local player is entity %d\n", entindex() );
		}

		AddToLeafSystem();
	}

	// Before the spawn callback, which may ask about dormancy.
	if ( updateType == DATA_UPDATE_CREATED || bJustMade )
		PushDormancy();

	if ( bJustMade && pStandalone )
		pStandalone->OnStandaloneSpawn( (uint32_t)BlockHandle().ToInt() );

	// Asked once: SourceSharpSchema_Accept counts refused updates.
	const bool bSchemaOk = BlobBytes() > 0 && SourceSharpSchema_Accept( m_nTypeId, m_nSchemaHash, m_nEntIndex, NULL );

	// Only with an accepted layout, so a class never decodes bytes it did not declare.
	if ( m_bHasManaged && pStandalone && bSchemaOk )
	{
		pStandalone->OnStandaloneDataChanged( (uint32_t)BlockHandle().ToInt(), DataBlob(), BlobBytes(),
											  (int32_t)updateType );
	}

	// The model index is networked straight into the block, so this is where a new
	// model is noticed: publish its facts and bump the policy serial. Guarded by the
	// index so an entity networked every tick does not re-decide every tick.
	if ( Block().Head.ModelIndex != m_nPublishedModelIndex )
		PublishModelFacts();

	// EF_NODRAW lands in the block the same way, with nothing here running.
	if ( Block().Head.Effects != m_nPublishedEffects )
	{
		m_nPublishedEffects = Block().Head.Effects;
		BumpPolicyInputs();
	}
}

void C_StandaloneEntity::NotifyShouldTransmit( ShouldTransmitState_t state )
{
	switch ( state )
	{
		case SHOULDTRANSMIT_END:
			SetDormant( true );
			break;

		case SHOULDTRANSMIT_START:
			SetDormant( false );
			break;

		default:
			break;
	}
}

// Dormancy is a policy input: managed code decides from it whether the entity
// draws, and PreRender's apply sweep moves the renderable, in the same frame.
void C_StandaloneEntity::SetDormant( bool bDormant )
{
	if ( ( Block().Client.Dormant != 0 ) == bDormant )
		return;

	Block().Client.Dormant = bDormant ? 1 : 0;
	BumpPolicyInputs();

	PushDormancy();
}

// Only an entity with a managed half enters the mirror: handles are recycled,
// and an entry nothing clears would be inherited.
void C_StandaloneEntity::PushDormancy()
{
	if ( !m_bHasManaged )
		return;

	IManagedStandaloneClient *pStandalone = ManagedStandaloneClient();

	if ( pStandalone )
		pStandalone->OnStandaloneDormancyChanged( (uint32_t)BlockHandle().ToInt(), Block().Client.Dormant );
}

// The packet's server time, not curtime, which drifts against the server's tick.
void C_StandaloneEntity::NoteNetworkedPosition()
{
	Block().Client.SimulationTime = engine->GetLastTimeStamp();
}

void C_StandaloneEntity::PostInterpolate( float flCurrentTime )
{
	Block().Client.Cycle = SequenceCycle( flCurrentTime );

	OnPositionChanged();
}

void C_StandaloneEntity::PublishRenderPose()
{
	// A body the solver owns publishes nothing: the physics step writes its pose into
	// the tail, and the networked origin would snap it back every update.
	if ( IsRagdoll() )
		return;

	Block().Client.RenderOrigin = Block().Head.Origin;
	Block().Client.RenderAngles = Block().Head.Angles;

	Block().Client.RenderTransform = Block().Head.WorldTransform;
}

void C_StandaloneEntity::OnPositionChanged()
{
	// Runs every frame, so the shadow work is guarded on an exact pose comparison,
	// as stock's UpdateShadow compares origins.
	matrix3x4_t matPrevPose;
	MatrixCopy( Block().Head.WorldTransform, matPrevPose );

	AngleMatrix( Block().Head.Angles, Block().Head.Origin, Block().Head.WorldTransform );

	const bool bPoseMoved = ( memcmp( &matPrevPose, &Block().Head.WorldTransform, sizeof( matrix3x4_t ) ) != 0 );

	PublishRenderPose();

	ShellLeaf_Moved( ClientLeafSystem(), this );

	// The re-projection is unconditional (the manager compares origins itself, and
	// a ragdoll moves without bPoseMoved); the texture mark only when the pose moved.
	ShellShadows_MarkDirty( BlockHandle(), bPoseMoved );
}

void C_StandaloneEntity::AddToLeafSystem()
{
	// The asserts pin managed ClientRenderGroup to the engine's enum.
	COMPILE_TIME_ASSERT( (int)RENDER_GROUP_OPAQUE_ENTITY == 7 );
	COMPILE_TIME_ASSERT( (int)RENDER_GROUP_TRANSLUCENT_ENTITY == 8 );
	COMPILE_TIME_ASSERT( (int)RENDER_GROUP_TWOPASS == 9 );
	COMPILE_TIME_ASSERT( (int)RENDER_GROUP_VIEW_MODEL_OPAQUE == 10 );
	COMPILE_TIME_ASSERT( (int)RENDER_GROUP_OPAQUE_BRUSH == 12 );

	const RenderGroup_t group = (RenderGroup_t)Block().Client.RenderGroup;

	if ( IsInLeafSystem() )
	{
		// Re-set: a model arriving later can change what kind of thing this is to draw.
		ClientLeafSystem()->SetRenderGroup( RenderHandle(), group );
		ClientLeafSystem()->RenderableChanged( RenderHandle() );
	}
	else
	{
		ShellLeaf_Add( ClientLeafSystem(), this, group );
	}

	RefreshShadow();
}

void C_StandaloneEntity::UpdateVisibility()
{
	const bool bWanted = ShouldDraw();
	const bool bPresent = RenderHandle() != INVALID_CLIENT_RENDER_HANDLE;

	if ( bWanted == bPresent )
		return;

	if ( bWanted )
		AddToLeafSystem();
	else
		RemoveFromLeafSystem();
}

void C_StandaloneEntity::RemoveFromLeafSystem()
{
	// The shadow goes first, as in C_BaseEntity::RemoveFromLeafSystem, but above the
	// early return: here the destructor is the last chance to hand the manager its
	// handle back.
	DestroyShadow();

	if ( !IsInLeafSystem() )
		return;

	ShellLeaf_Remove( ClientLeafSystem(), this );
	RenderHandle() = INVALID_CLIENT_RENDER_HANDLE;
}

const model_t *C_StandaloneEntity::GetModel() const
{
	return Block().Head.ModelIndex > 0 ? modelinfo->GetModel( Block().Head.ModelIndex ) : NULL;
}

static_assert( !std::is_base_of<IGameEntity, C_StandaloneEntity>::value,
			   "C_StandaloneEntity derives from IGameEntity again" );

bool C_StandaloneEntity::ShouldDraw()
{
	// Whether the camera sees the owner is solved during view setup just before
	// this is asked, so it is read live.
	const EngineBlockClientTail_t &tail = Block().Client;

	if ( tail.Drawn == 0 || tail.Dormant != 0 )
		return false;

	switch ( tail.OwnerSeenRule )
	{
		case STANDALONE_OWNER_SEEN_HIDDEN_WHILE:
			return !SourceSharp_ThirdPersonSeesOwner();

		case STANDALONE_OWNER_SEEN_DRAWN_ONLY_WHILE:
			return SourceSharp_ThirdPersonSeesOwner();

		default:
			return true;
	}
}

// The origin is written only while driving; managed code leaves a driven entity
// out of interpolation that frame.
void C_StandaloneEntity::PredictionDrive( const SourceSharpClientPlayerView &view )
{
	m_vecViewOffset.Init( view.vecViewOffset[0], view.vecViewOffset[1], view.vecViewOffset[2] );
	m_angPunch.Init( view.angPunch[0], view.angPunch[1], view.angPunch[2] );
	m_angPunchVel.Init( view.angPunchVel[0], view.angPunchVel[1], view.angPunchVel[2] );
	m_nPredictedHealth = view.nHealth;

	const bool bDriving = view.bDriving != 0;

	if ( !bDriving )
		return;

	Block().Head.Origin.Init( view.vecOrigin[0], view.vecOrigin[1], view.vecOrigin[2] );

	OnPositionChanged();
}

void C_StandaloneEntity::GetRenderBounds( Vector &mins, Vector &maxs )
{
	// The two endpoints, or a beam vanishes when its emitter leaves the view.
	if ( Block().Client.CustomDraw )
	{
		mins = Block().Client.RenderMins;
		maxs = Block().Client.RenderMaxs;
		return;
	}

	// A ragdoll's limbs leave the model's box; the physics step writes a generous cube.
	if ( IsRagdoll() )
	{
		mins = Block().Client.RenderMins;
		maxs = Block().Client.RenderMaxs;
		return;
	}

	if ( const model_t *pModel = GetModel() )
	{
		modelinfo->GetModelRenderBounds( pModel, mins, maxs );

		// Custom-draw bounds above are already in world units and are not scaled.
		mins *= m_flModelScale;
		maxs *= m_flModelScale;
		return;
	}

	mins = Block().Head.Mins;
	maxs = Block().Head.Maxs;
}

void C_StandaloneEntity::CreateModelInstance()
{
	if ( m_ModelInstance == MODEL_INSTANCE_INVALID )
		m_ModelInstance = modelrender->CreateInstance( this );
}

void C_StandaloneEntity::PublishModelFacts()
{
	m_nPublishedModelIndex = Block().Head.ModelIndex;

	// -1, not zero: zero is mod_bad.
	const model_t *pModel = GetModel();
	Block().Client.ModelType = pModel ? (int32_t)modelinfo->GetModelType( pModel ) : -1;

	BumpPolicyInputs();
}

// The asserts keep managed ShadowCast equal to ShadowType_t.
ShadowType_t C_StandaloneEntity::ShadowCastType()
{
	COMPILE_TIME_ASSERT( (int)SHADOWS_NONE == 0 );
	COMPILE_TIME_ASSERT( (int)SHADOWS_SIMPLE == 1 );
	COMPILE_TIME_ASSERT( (int)SHADOWS_RENDER_TO_TEXTURE == 2 );
	COMPILE_TIME_ASSERT( (int)SHADOWS_RENDER_TO_TEXTURE_DYNAMIC == 3 );

	return (ShadowType_t)Block().Client.ShadowType;
}

void C_StandaloneEntity::RefreshShadow()
{
	ShellShadows_Refresh( BlockHandle(), (int)ShadowCastType(), Block().Client.ShadowFlags );
}

void C_StandaloneEntity::DestroyShadow()
{
	// Earlier queued entries go too: the renderable may be freed before the hand-over.
	ShellShadows_Destroy( BlockHandle() );
}

// Whether a projected texture lands on this entity. The render policy writes a
// shadow's and a flashlight's answers above the shadow flags (1 << 29, 1 << 30);
// the flashlight's is tested first when the mask holds both, as stock does.
bool C_StandaloneEntity::ShouldReceiveProjectedTextures( int flags )
{
	COMPILE_TIME_ASSERT( (int)SHADOW_FLAGS_FLASHLIGHT == ( 1 << 0 ) );
	COMPILE_TIME_ASSERT( (int)SHADOW_FLAGS_SHADOW == ( 1 << 1 ) );
	const int kReceivesShadows = 1 << 29;
	const int kReceivesFlashlight = 1 << 30;
	const int nAnswer = ( flags & SHADOW_FLAGS_FLASHLIGHT ) ? kReceivesFlashlight : kReceivesShadows;
	return ( Block().Client.ShadowFlags & nAnswer ) != 0;
}

// With no managed side, every bone slot is the entity's own transform.

#include "standalone_bone_trace.h"
#include "standalone_bone_batch.h"

bool C_StandaloneEntity::TestHitboxes( const Ray_t &ray, unsigned int fContentsMask, trace_t &tr )
{
	IManagedBonesClient *pBones = ManagedBonesClient();

	if ( !pBones )
		return false;

	float flRay[SS_BONE_RAY_FLOATS];

	for ( int i = 0; i < 3; i++ )
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

	for ( int i = 0; i < 3; i++ )
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

	if ( !pBones->TestHitboxes( GetRefEHandle().ToInt(), &Block(), flRay, (int32_t)fContentsMask, flTrace, nTrace,
								gpGlobals->curtime, gpGlobals->framecount ) )
		return false;

	for ( int i = 0; i < 3; i++ )
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

	if ( nTrace[SS_BONE_TRACE_SURFACE_IS_STUDIO] )
		tr.surface.name = "**studio**";

	return true;
}

// The frame's skeletons in one crossing: the first SetupBones ask of a frame
// fetches managed code's batch, and later asks it holds a matching pose for (same
// entity, frame, clock and inputs, mask inside the posed one) are copied out.
namespace
{
struct BoneBatchState_t
{
	int nFrame;
	const SourceSharpBoneBatch_t *pBatch;
};

BoneBatchState_t g_BoneBatch = { -1, NULL };

const SourceSharpBoneBatch_t *FrameBoneBatch( IManagedBonesClient *pBones )
{
	if ( g_BoneBatch.nFrame != gpGlobals->framecount )
	{
		g_BoneBatch.nFrame = gpGlobals->framecount;
		g_BoneBatch.pBatch = static_cast<const SourceSharpBoneBatch_t *>(
			pBones->BoneBatch( gpGlobals->framecount, gpGlobals->curtime ) );
	}

	return g_BoneBatch.pBatch;
}
} // namespace

bool C_StandaloneEntity::SetupBones( matrix3x4_t *pBoneToWorldOut, int nMaxBones, int boneMask, float currentTime )
{
	IManagedBonesClient *pBones = ManagedBonesClient();

	if ( !pBones )
	{
		for ( int i = 0; pBoneToWorldOut && i < nMaxBones; i++ )
			MatrixCopy( Block().Head.WorldTransform, pBoneToWorldOut[i] );

		return pBoneToWorldOut != NULL;
	}

	const SourceSharpBoneBatchEntry_t *pPosed = SourceSharp_BoneBatchEntry(
		FrameBoneBatch( pBones ), (uint32_t)GetRefEHandle().ToInt(), boneMask, currentTime, gpGlobals->framecount,
		Block().Head.ModelIndex, Block().Client.ViewModel, Block().Head.Origin.Base(), Block().Head.Angles.Base(),
		Block().Client.RenderOrigin.Base(), Block().Client.RenderAngles.Base() );

	if ( pPosed )
	{
		const_cast<SourceSharpBoneBatchEntry_t *>( pPosed )->nAnswered++;

		if ( !pBoneToWorldOut )
			return pPosed->nBones > 0;

		const float *pMatrices = g_BoneBatch.pBatch->pMatrices + pPosed->nFirstMatrix * SS_BONE_BATCH_MATRIX_FLOATS;
		const int nCopy = MIN( pPosed->nBones, nMaxBones );

		if ( nCopy > 0 )
			memcpy( pBoneToWorldOut[0].Base(), pMatrices, sizeof( matrix3x4_t ) * nCopy );

		for ( int i = nCopy; i < nMaxBones; i++ )
			MatrixCopy( Block().Head.WorldTransform, pBoneToWorldOut[i] );

		return true;
	}

	float *pOut = ( pBoneToWorldOut && nMaxBones > 0 ) ? pBoneToWorldOut[0].Base() : NULL;
	const int32_t nOut = pOut ? nMaxBones : 0;
	int32_t nAnswer;

	nAnswer = pBones->SetupBones( GetRefEHandle().ToInt(), &Block(), pOut, nOut, boneMask, currentTime,
								  gpGlobals->framecount );

	// A refusal leaves the array untouched, so it gets the entity's own transform.
	if ( !nAnswer && pBoneToWorldOut )
	{
		for ( int i = 0; i < nMaxBones; i++ )
			MatrixCopy( Block().Head.WorldTransform, pBoneToWorldOut[i] );

		return true;
	}

	return nAnswer != 0;
}

int C_StandaloneEntity::LookupAttachment( const char *pAttachmentName )
{
	IManagedBonesClient *pBones = ManagedBonesClient();

	if ( !pAttachmentName || !pBones )
		return -1;

	return pBones->LookupAttachment( GetRefEHandle().ToInt(), &Block(), pAttachmentName );
}

bool C_StandaloneEntity::GetAttachment( int number, matrix3x4_t &matrix )
{
	IManagedBonesClient *pBones = ManagedBonesClient();

	if ( !pBones )
		return false;

	return pBones->GetAttachment( GetRefEHandle().ToInt(), &Block(), number, matrix.Base(), gpGlobals->curtime,
								  gpGlobals->framecount ) != 0;
}

bool C_StandaloneEntity::GetAttachment( int number, Vector &origin, QAngle &angles )
{
	matrix3x4_t attachmentToWorld;

	if ( !GetAttachment( number, attachmentToWorld ) )
	{
		origin = Block().Head.Origin;
		angles = Block().Head.Angles;

		return false;
	}

	MatrixAngles( attachmentToWorld, angles );
	MatrixPosition( attachmentToWorld, origin );

	return true;
}

// A view's custom draws in one crossing: the first to reach DrawModel asks
// managed code for all of them, and each then draws its prepared plan. A sprite
// whose plan was made under another blend falls back to its own crossing.
namespace
{
struct DrawBatchEntry_t
{
	C_StandaloneEntity *pEntity;
	uint32_t hHandle;
	float flBlend;
	int32_t nResult;
};

CUtlVector<DrawBatchEntry_t> g_DrawBatch;
bool g_bDrawBatchAsked = false;
bool g_bDrawBatchPreparing = false;

int FindInDrawBatch( const C_StandaloneEntity *pEntity )
{
	for ( int i = 0; i < g_DrawBatch.Count(); ++i )
		if ( g_DrawBatch[i].pEntity == pEntity )
			return i;
	return -1;
}

C_StandaloneEntity *StandaloneFromRenderable( IClientRenderable *pRenderable )
{
	IClientUnknown *pUnknown = pRenderable ? pRenderable->GetIClientUnknown() : NULL;
	uint32_t nWord = 0;
	if ( !RenderableEntityHandleWord( pUnknown, nWord ) )
		return NULL;
	C_StandaloneEntity *pEntity = SourceSharp_StandaloneFromHandle( nWord );
	return ( pEntity && static_cast<IClientRenderable *>( pEntity ) == pRenderable ) ? pEntity : NULL;
}
} // namespace

void SourceSharp_StandaloneDrawBatchCapture( const CClientRenderablesList &list )
{
	g_DrawBatch.RemoveAll();
	g_bDrawBatchAsked = false;
	SourceSharp_SpriteStoredClear();

	for ( int g = 0; g < RENDER_GROUP_COUNT; ++g )
	{
		const bool bTranslucent = ( g == RENDER_GROUP_TRANSLUCENT_ENTITY );
		for ( int i = 0; i < list.m_RenderGroupCounts[g]; ++i )
		{
			C_StandaloneEntity *pEntity = StandaloneFromRenderable( list.m_RenderGroups[g][i].m_pRenderable );
			if ( !pEntity || !pEntity->Block().Client.CustomDraw )
				continue;
			const float flBlend = bTranslucent ? (float)( pEntity->GetFxBlend() / 255.0f ) : 1.0f;
			// DrawTranslucentRenderable draws nothing at a blend of zero, so
			// nothing is prepared for it.
			if ( flBlend <= 0.0f )
				continue;
			DrawBatchEntry_t entry = { pEntity, (uint32_t)pEntity->BlockHandle().ToInt(), flBlend, -1 };
			g_DrawBatch.AddToTail( entry );
		}
	}
}

float SourceSharp_StandaloneDrawBatchBlend( IClientEntity *pEntity, float flFallback )
{
	if ( !g_bDrawBatchPreparing )
		return flFallback;
	for ( int i = 0; i < g_DrawBatch.Count(); ++i )
		if ( static_cast<IClientEntity *>( g_DrawBatch[i].pEntity ) == pEntity )
			return g_DrawBatch[i].flBlend;
	return flFallback;
}

static void AskDrawBatch()
{
	g_bDrawBatchAsked = true;
	IManagedStandaloneClient *pStandalone = ManagedStandaloneClient();
	const int n = g_DrawBatch.Count();
	if ( !pStandalone || n == 0 )
		return;

	CUtlVector<uint32_t> handles;
	CUtlVector<int32_t> results;
	handles.SetCount( n );
	results.SetCount( n );
	for ( int i = 0; i < n; ++i )
	{
		handles[i] = g_DrawBatch[i].hHandle;
		results[i] = -1;
	}
	g_bDrawBatchPreparing = true;
	g_SourceSharpEffects.nDrawBatches++;
	pStandalone->OnStandaloneDrawBatch( handles.Base(), n, results.Base() );
	g_bDrawBatchPreparing = false;
	for ( int i = 0; i < n; ++i )
		g_DrawBatch[i].nResult = results[i];
}

int C_StandaloneEntity::DrawModel( int flags )
{
	// Before the model check: a beam has no model.
	if ( Block().Client.CustomDraw )
	{
		const int nBatch = FindInDrawBatch( this );
		if ( nBatch >= 0 )
		{
			if ( !g_bDrawBatchAsked )
				AskDrawBatch();
			const DrawBatchEntry_t &entry = g_DrawBatch[nBatch];
			if ( entry.nResult >= 0 )
			{
				const int nStored =
					SourceSharp_SpriteExecuteStored( static_cast<IClientEntity *>( this ), render->GetBlend() );
				if ( nStored >= 0 )
				{
					g_SourceSharpEffects.nDrawBatchedSprites++;
					return nStored;
				}
				if ( nStored == -2 )
					return entry.nResult;
				// A stored sprite under another blend: draw it the long way.
			}
			g_SourceSharpEffects.nDrawBatchFallbacks++;
		}

		IManagedStandaloneClient *pStandalone = ManagedStandaloneClient();

		return pStandalone ? pStandalone->OnStandaloneDraw( (uint32_t)BlockHandle().ToInt() ) : 0;
	}

	const model_t *pModel = GetModel();

	if ( !pModel )
		return 0;

	const int nModelType = modelinfo->GetModelType( pModel );

	if ( nModelType != mod_studio && nModelType != mod_brush )
	{
		if ( !m_bDrawReported )
		{
			m_bDrawReported = true;
			Warning( "[SourceSharp] standalone(client): '%s' is neither a studio "
					 "nor a brush model; nothing will draw it\n",
					 modelinfo->GetModelName( pModel ) );
		}

		return 0;
	}

	if ( !m_bDrawReported )
	{
		m_bDrawReported = true;
		DevMsg( "[SourceSharp] standalone(client): '%s' drawing '%s' at (%.0f %.0f %.0f) "
				"angles (%.1f %.1f %.1f)\n",
				m_szClassname, modelinfo->GetModelName( pModel ), Block().Head.Origin.x, Block().Head.Origin.y,
				Block().Head.Origin.z, Block().Head.Angles.x, Block().Head.Angles.y, Block().Head.Angles.z );
	}

	if ( nModelType == mod_brush )
	{
		ERenderDepthMode depthMode = DEPTH_MODE_NORMAL;

		if ( flags & STUDIO_SSAODEPTHTEXTURE )
			depthMode = DEPTH_MODE_SSA0;
		else if ( flags & STUDIO_SHADOWDEPTHTEXTURE )
			depthMode = DEPTH_MODE_SHADOW;

		if ( depthMode != DEPTH_MODE_NORMAL )
		{
			render->DrawBrushModelShadowDepth( this, (model_t *)pModel, Block().Head.Origin, Block().Head.Angles,
											   depthMode );
		}
		else
		{
			// A brush with opaque and translucent faces drawn at once sorts wrongly.
			DrawBrushModelMode_t mode = DBM_DRAW_ALL;

			if ( flags & STUDIO_TWOPASS )
			{
				mode = ( flags & STUDIO_TRANSPARENCY ) ? DBM_DRAW_TRANSLUCENT_ONLY : DBM_DRAW_OPAQUE_ONLY;
			}

			// While installed, the engine hands each brush surface to the renderer.
			if ( m_pSurface )
				m_pSurface->BeginDraw();

			// The fade is applied here: the world renderer reads the last blend it was
			// handed rather than asking the renderable.
			if ( Block().Client.FadeStyle != 0 )
				render->SetBlend( Block().Client.FxBlend / 255.0f );

			render->DrawBrushModelEx( this, (model_t *)pModel, Block().Head.Origin, Block().Head.Angles, mode );

			// Uninstalled unconditionally: it is process-global, and left set it would get
			// every brush in the world.
			if ( m_pSurface )
				m_pSurface->EndDraw();

			// Drawn by one entity so the two sort together (a fake window).
			if ( m_nOverlayModelIndex > 0 )
			{
				model_t *pOverlay = (model_t *)modelinfo->GetModel( m_nOverlayModelIndex );

				if ( pOverlay && modelinfo->GetModelType( pOverlay ) == mod_brush )
				{
					render->SetBlend( 1.0f );
					render->DrawBrushModelEx( this, pOverlay, Block().Head.Origin, Block().Head.Angles, mode );
				}
			}

			// Put back: the blend is renderer-global.
			if ( Block().Client.FadeStyle != 0 || m_nOverlayModelIndex > 0 )
				render->SetBlend( 1.0f );
		}

		return 1;
	}

	const int nExtras = DrawExtras( flags, false );
	if ( nExtras >= 0 )
		return nExtras;

	// As C_BaseAnimating::DrawModel: the instance holds lighting state and decals.
	CreateModelInstance();

	ModelRenderInfo_t info;
	info.origin = GetRenderOrigin();
	info.angles = GetRenderAngles();
	info.pRenderable = this;
	info.pModel = pModel;
	// The viewmodel is drawn and lit at its render pose; without the explicit
	// lighting origin the engine lit it at the owner's feet and it drew black.
	const bool bViewModel = Block().Client.ViewModel != 0;
	info.pModelToWorld = bViewModel ? &Block().Client.RenderTransform : &Block().Head.WorldTransform;
	info.instance = GetModelInstance();
	if ( bViewModel )
		info.pLightingOrigin = &Block().Client.RenderOrigin;
	info.entity_index = entindex();
	info.skin = GetSkin();
	info.body = GetBody();
	info.hitboxset = Block().Client.HitboxSet;
	info.flags = flags;

	const int nDrawn = modelrender->DrawModelEx( info );
	DrawExtras( flags, true );
	return nDrawn;
}

// What managed code draws beside the model, where stock's weapons drew theirs:
// before it, a two-pass entity's translucent pass (answering what DrawModel
// returns, or -1 to draw the model); after it, the local viewmodel's extras.
int C_StandaloneEntity::DrawExtras( int flags, bool bAfterModel )
{
	const bool bAsk = bAfterModel ? ( Block().Client.ViewModel != 0 && ( flags & STUDIO_RENDER ) )
								  : ( ( flags & STUDIO_TRANSPARENCY ) && ( flags & STUDIO_TWOPASS ) );
	IManagedStandaloneClient *pStandalone = bAsk ? ManagedStandaloneClient() : NULL;
	return pStandalone ? pStandalone->OnStandaloneDrawExtras( (uint32_t)BlockHandle().ToInt(), flags ) : -1;
}

// Turned with the entity unless world-aligned (CCollisionProperty::WorldSpaceAABB).
void C_StandaloneEntity::WorldSpaceTriggerBounds( Vector *pMins, Vector *pMaxs ) const
{
	Vector vecMins, vecMaxs;
	SourceSharp_CollisionBoundsToWorld( Block().Head.SolidType, Block().Head.SolidFlags, Block().Head.Mins,
										Block().Head.Maxs, Block().Head.Origin, Block().Head.Angles, &vecMins,
										&vecMaxs );
	if ( pMins )
		*pMins = vecMins;
	if ( pMaxs )
		*pMaxs = vecMaxs;
}

// The render box placed and turned as CCollisionProperty::CollisionAABBToWorldAABB turns
// a box, so a turned entity's centre is where stock's WorldSpaceCenter puts it.
void C_StandaloneEntity::WorldSpaceSurroundingBounds( Vector *pMins, Vector *pMaxs )
{
	Vector vecLocalMins, vecLocalMaxs;
	GetRenderBounds( vecLocalMins, vecLocalMaxs );

	// GetRenderOrigin(): a ragdoll's networked origin stays where it was thrown from.
	Vector vecMins, vecMaxs;
	SourceSharp_CollisionBoundsToWorld( Block().Head.SolidType, Block().Head.SolidFlags, vecLocalMins, vecLocalMaxs,
										GetRenderOrigin(), GetRenderAngles(), &vecMins, &vecMaxs );
	if ( pMins )
		*pMins = vecMins;
	if ( pMaxs )
		*pMaxs = vecMaxs;
}

// The ClientClass name pairs with the ServerClass name.

// Runs during RecvTable_Decode, before the payload words, so the managed half
// exists before its first data update. Store first: EnsureManagedHalf reads it.
static void RecvProxy_StandaloneTypeId( const CRecvProxyData *pData, void *pStruct, void *pOut )
{
	*( (uint32 *)pOut ) = (uint32)pData->m_Value.m_Int;

	SourceSharp_ClientShellFromRecvStruct( pStruct )->EnsureManagedHalf();
}

static void BuildStandaloneRecvProps( RecvProp *pOut, int &nOut )
{
	C_StandaloneEntity *pNull = NULL;

	int nOffsets[STANDALONE_NET_FIELD_COUNT];
	nOffsets[STANDALONE_NET_FIELD_ORIGIN] = C_StandaloneEntity::BlockBase() + SS_CLIENT_BLOCK_HEAD_OFFSET( Origin );
	nOffsets[STANDALONE_NET_FIELD_ANGLES] = C_StandaloneEntity::BlockBase() + SS_CLIENT_BLOCK_HEAD_OFFSET( Angles );
	nOffsets[STANDALONE_NET_FIELD_MODELINDEX] =
		C_StandaloneEntity::BlockBase() + SS_CLIENT_BLOCK_HEAD_OFFSET( ModelIndex );
	nOffsets[STANDALONE_NET_FIELD_EFFECTS] = C_StandaloneEntity::BlockBase() + SS_CLIENT_BLOCK_HEAD_OFFSET( Effects );
	// The discontinuity counter a teleport bumps; managed interpolation reads it and
	// drops the entity's history when it changes.
	nOffsets[STANDALONE_NET_FIELD_INTERPFRAME] =
		C_StandaloneEntity::BlockBase() + SS_CLIENT_BLOCK_HEAD_OFFSET( InterpolationFrame );
	nOffsets[STANDALONE_NET_FIELD_TYPEID] = (int)( (intp)&pNull->m_nTypeId );
	nOffsets[STANDALONE_NET_FIELD_SCHEMAHASH] = (int)( (intp)&pNull->m_nSchemaHash );
	nOffsets[STANDALONE_NET_FIELD_PAYLOADWORDS] = (int)( (intp)&pNull->m_nPayloadWords );
	nOffsets[STANDALONE_NET_FIELD_SOLIDTYPE] =
		C_StandaloneEntity::BlockBase() + SS_CLIENT_BLOCK_HEAD_OFFSET( SolidType );
	nOffsets[STANDALONE_NET_FIELD_SOLIDFLAGS] =
		C_StandaloneEntity::BlockBase() + SS_CLIENT_BLOCK_HEAD_OFFSET( SolidFlags );
	nOffsets[STANDALONE_NET_FIELD_SKIN] = C_StandaloneEntity::BlockBase() + (int)offsetof( EngineBlock_t, Client ) +
		(int)offsetof( EngineBlockClientTail_t, Skin );
	nOffsets[STANDALONE_NET_FIELD_BODY] = C_StandaloneEntity::BlockBase() + (int)offsetof( EngineBlock_t, Client ) +
		(int)offsetof( EngineBlockClientTail_t, Body );

	int nCount = 0;
	const StandaloneNetField_t *pFields = StandaloneNetFields( &nCount );

	for ( int i = 0; i < nCount; i++ )
	{
		switch ( pFields[i].kind )
		{
			case STANDALONE_NET_VECTOR:
				pOut[nOut++] = RecvPropVector( pFields[i].pName, nOffsets[i], 12 );
				break;

			case STANDALONE_NET_INT:
				pOut[nOut++] = ( i == STANDALONE_NET_FIELD_TYPEID )
					? RecvPropInt( pFields[i].pName, nOffsets[i], 4, 0, RecvProxy_StandaloneTypeId )
					: RecvPropInt( pFields[i].pName, nOffsets[i], 4 );
				break;

			case STANDALONE_NET_STRING:
				pOut[nOut++] = RecvPropString( pFields[i].pName, nOffsets[i], pFields[i].nBits );
				break;
		}
	}
}

int SourceSharp_StandaloneBlobOffset()
{
	return (int)ALIGN_VALUE( sizeof( C_StandaloneEntity ), 16 );
}

// BlockHandle() overlays the block's Handle; a second CBaseHandle member would
// read ClassnameIndex as its serial and no handle would resolve.
static_assert( sizeof( CBaseHandle ) == sizeof( uint32_t ),
			   "CBaseHandle is no longer one uint32; EngineBlockHead_t::Handle cannot overlay it" );

// Not offsetof(): the class is not standard-layout.
int C_StandaloneEntity::BlockBase()
{
	C_StandaloneEntity *pNull = NULL;

	return (int)( (intp)&pNull->m_EngineBlock );
}

void *C_StandaloneEntity::operator new( size_t nBytes, int nBlobBytes )
{
	Assert( (int)nBytes <= SourceSharp_StandaloneBlobOffset() );

	// Floored at the smallest tier: a shorter blob would let the engine decode past
	// the allocation. Tier 0 suffices here because the block is a member, not in
	// the blob as on the server.
	if ( nBlobBytes < SourceSharp_TierBytes( 0 ) )
		nBlobBytes = SourceSharp_TierBytes( 0 );

	const int nTotal = SourceSharp_StandaloneBlobOffset() + nBlobBytes;

	// Zeroed: the recv props write only changed words.
	void *pMem = malloc( nTotal );
	if ( pMem )
		V_memset( pMem, 0, nTotal );

	return pMem;
}

void C_StandaloneEntity::operator delete( void *pMem )
{
	free( pMem );
}

void C_StandaloneEntity::operator delete( void *pMem, int )
{
	free( pMem );
}

unsigned char *C_StandaloneEntity::DataBlob()
{
	return (unsigned char *)this + SourceSharp_StandaloneBlobOffset();
}

int C_StandaloneEntity::BlobBytes() const
{
	return SourceSharp_TierBytes( m_iTier );
}

CSourceSharpSurfaceRenderer *C_StandaloneEntity::SurfaceRenderer( bool bCreate )
{
	if ( !m_pSurface && bCreate )
		m_pSurface = new CSourceSharpSurfaceRenderer;

	return m_pSurface;
}

// One ClientClass per tier; only the create function knows an entity's tier.
static RecvProp *g_pTierProps[SOURCESHARP_TIER_MAX] = { NULL };
static RecvTable *g_pTierTables[SOURCESHARP_TIER_MAX] = { NULL };
static ClientClass *g_pTierClasses[SOURCESHARP_TIER_MAX] = { NULL };
static bool g_bTiersRegistered = false;

static IClientNetworkable *CreateStandaloneTier( int iTier, int entnum, int serialNum )
{
	C_StandaloneEntity *pEntity =
		new ( SourceSharp_TierBytes( iTier ) ) C_StandaloneEntity( entnum, serialNum, g_pTierClasses[iTier], iTier );

	// An entity in 1..maxclients is a player: the only fact the create function has,
	// since the classname arrives with the first update.
	pEntity->SetPlayerShell( entnum >= 1 && entnum <= gpGlobals->maxClients );

	return pEntity;
}

static IClientNetworkable *CreateStandaloneTier0( int e, int s )
{
	return CreateStandaloneTier( 0, e, s );
}

static IClientNetworkable *CreateStandaloneTier1( int e, int s )
{
	return CreateStandaloneTier( 1, e, s );
}

static IClientNetworkable *CreateStandaloneTier2( int e, int s )
{
	return CreateStandaloneTier( 2, e, s );
}

static IClientNetworkable *CreateStandaloneTier3( int e, int s )
{
	return CreateStandaloneTier( 3, e, s );
}

static IClientNetworkable *CreateStandaloneTier4( int e, int s )
{
	return CreateStandaloneTier( 4, e, s );
}

static IClientNetworkable *CreateStandaloneTier5( int e, int s )
{
	return CreateStandaloneTier( 5, e, s );
}

static IClientNetworkable *CreateStandaloneTier6( int e, int s )
{
	return CreateStandaloneTier( 6, e, s );
}

static void RegisterStandaloneTiers()
{
	if ( g_bTiersRegistered )
		return;

	g_bTiersRegistered = true;

	// One per tier, sized by the tier bound, so a missing function does not compile.
	static CreateClientClassFn s_Create[SOURCESHARP_TIER_MAX] = {
		CreateStandaloneTier0, CreateStandaloneTier1, CreateStandaloneTier2, CreateStandaloneTier3,
		CreateStandaloneTier4, CreateStandaloneTier5, CreateStandaloneTier6,
	};

	int nShared = 0;
	StandaloneNetFields( &nShared );

	const int nBlobBase = SourceSharp_StandaloneBlobOffset();

	for ( int iTier = 0; iTier < SourceSharp_TierCount(); iTier++ )
	{
		const int nWords = SourceSharp_TierWords( iTier );
		const char *pClassName = SourceSharp_TierClassName( SS_SHELL_STANDALONE, iTier );
		const char *pTableName = SourceSharp_TierTableName( SS_SHELL_STANDALONE, iTier );

		if ( nWords < 0 || !SourceSharpPayload_OffsetsFit( nBlobBase, nWords ) )
		{
			Warning( "[SourceSharp] standalone tier '%s': %d word(s) will not fit at blob "
					 "base %d; NOT registered\n",
					 pTableName, nWords, nBlobBase );
			continue;
		}

		g_pTierProps[iTier] = new RecvProp[nShared + nWords];

		int nBuilt = 0;
		BuildStandaloneRecvProps( g_pTierProps[iTier], nBuilt );

		if ( SourceSharpPayload_BuildRecvProps( g_pTierProps[iTier] + nBuilt, nWords, nBlobBase ) != nWords )
		{
			Warning( "[SourceSharp] standalone tier '%s': payload build refused %d word(s); "
					 "not registered\n",
					 pTableName, nWords );
			delete[] g_pTierProps[iTier];
			g_pTierProps[iTier] = NULL;
			continue;
		}

		nBuilt += nWords;

		g_pTierTables[iTier] = new RecvTable( g_pTierProps[iTier], nBuilt, pTableName );
		g_pTierClasses[iTier] = new ClientClass( pClassName, s_Create[iTier], NULL, g_pTierTables[iTier] );

		// The listen server's local transfer checks both ends, so a wrong recv prop is
		// still the slow list however right the send prop is.
		int nSlow = 0;

		for ( int i = 0; i < nWords; i++ )
		{
			if ( !SourceSharpPayload_IsFastRecvProxy( g_pTierTables[iTier]->GetProp( nShared + i ) ) )
				nSlow++;
		}

		Msg( "[SourceSharp] standalone(client) tier %d '%s' -> '%s': %d real + %d payload "
			 "word(s) = %d prop(s), %d payload byte(s) at blob base %d\n",
			 iTier, pClassName, pTableName, nShared, nWords, nBuilt, SourceSharp_Tiers( NULL )[iTier].nBytes,
			 nBlobBase );

		if ( nSlow > 0 )
		{
			Warning( "[SourceSharp] standalone tier '%s': %d of %d payload word(s) did NOT "
					 "resolve to m_Int32ToInt32 and would be copied slowly\n",
					 pTableName, nSlow, nWords );
		}
	}
}

// Every tier is registered, so a registered empty tier is normal.
bool SourceSharp_StandaloneTierIsInUse( int iTier )
{
	RegisterStandaloneTiers();

	if ( iTier < 0 || iTier >= SourceSharp_TierCount() || g_pTierClasses[iTier] == NULL )
		return false;

	const CSourceSharpTypeTable &types = SourceSharp_NetTypes();

	for ( int i = 0; i < types.Count(); i++ )
	{
		const SourceSharpNetType_t *pType = types.At( i );

		if ( pType && pType->nShell == SS_SHELL_STANDALONE && pType->iTier == iTier )
			return true;
	}

	return false;
}

// The hierarchy's shared blob size, declared by C# before any class. Must mirror
// the server's: the word count and prop names must agree, though the offsets
// differ per side.
int32_t SourceSharp_NetHierarchyDeclare( int32_t nBlobBytes )
{
	RegisterStandaloneTiers();

	const int iTier = SourceSharp_TierForSize( nBlobBytes );

	if ( iTier < 0 )
	{
		Warning( "[SourceSharp] standalone(client): %d declared blob byte(s) exceeds the "
				 "largest tier; the hierarchy has NO table it can use\n",
				 nBlobBytes );
		return -1;
	}

	const int nWords = SourceSharp_TierWords( iTier );

	Msg( "[SourceSharp] standalone(client): the greediest declaration is %d byte(s) -> tier "
		 "%d '%s' (%d bytes, %d payload word(s)); every OTHER class is sized to its own "
		 "tier, blob base %d\n",
		 nBlobBytes, iTier, SourceSharp_TierTableName( SS_SHELL_STANDALONE, iTier ), SourceSharp_TierBytes( iTier ),
		 nWords, SourceSharp_StandaloneBlobOffset() );

	return nWords;
}

ClientClass *C_StandaloneEntity::GetClientClass()
{
	return m_pClientClass;
}

// The engine collects the class list once, at DLL init.
#include "shell_clientsystem.h"

class CSourceSharpStandaloneClientSystem : public CClientSystem
{
public:
	CSourceSharpStandaloneClientSystem()
		: CClientSystem( "CSourceSharpStandaloneClientSystem" )
	{
	}

	virtual bool Init() OVERRIDE
	{
		// Unconditional, as on the server; the two must agree.
		RegisterStandaloneTiers();
		return true;
	}
};

static CSourceSharpStandaloneClientSystem g_StandaloneClientSystem;

CClientSystem *ClientSystem_StandaloneClient()
{
	return &g_StandaloneClientSystem;
}

// Rebuilt each call; valid until the next.
static CUtlVector<void *> g_StandaloneInterpBlocks;

void **SourceSharp_ClientInterpBlocks( int32_t *pOutCount, float *pOutInterpAmount )
{
	if ( pOutCount )
		*pOutCount = 0;

	if ( pOutInterpAmount )
		*pOutInterpAmount = GetClientInterpAmount();

	g_StandaloneInterpBlocks.RemoveAll();

	for ( int i = 0; i < g_StandaloneClients.Count(); i++ )
		g_StandaloneInterpBlocks.AddToTail( &g_StandaloneClients[i]->Block() );

	if ( g_StandaloneInterpBlocks.Count() == 0 )
		return NULL;

	if ( pOutCount )
		*pOutCount = g_StandaloneInterpBlocks.Count();

	return g_StandaloneInterpBlocks.Base();
}

void SourceSharp_ClientInterpAdmitted( const uint32_t *pHandles, int32_t nCount )
{
	for ( int i = 0; i < g_StandaloneClients.Count(); i++ )
		g_StandaloneClients[i]->SetInterpAdmitted( false );

	for ( int32_t i = 0; pHandles && i < nCount; i++ )
	{
		C_StandaloneEntity *pEntity = SourceSharp_StandaloneFromHandle( pHandles[i] );

		if ( pEntity && (uint32_t)pEntity->BlockHandle().ToInt() == pHandles[i] )
			pEntity->SetInterpAdmitted( true );
	}
}

// Unfiltered: the viewmodel, which interpolation skips, needs deciding most.
static CUtlVector<void *> g_StandalonePolicyBlocks;

void **SourceSharp_ClientPolicyBlocks( int32_t *pOutCount )
{
	if ( pOutCount )
		*pOutCount = 0;

	g_StandalonePolicyBlocks.RemoveAll();

	for ( int i = 0; i < g_StandaloneClients.Count(); i++ )
		g_StandalonePolicyBlocks.AddToTail( &g_StandaloneClients[i]->Block() );

	if ( g_StandalonePolicyBlocks.Count() == 0 )
		return NULL;

	if ( pOutCount )
		*pOutCount = g_StandalonePolicyBlocks.Count();

	return g_StandalonePolicyBlocks.Base();
}

// Hands the managed policy's decision to the leaf system and the shadow manager.
// Edge-triggered: managed code bumps Client.PolicyOutputs only when it writes
// new choices. Compared for equality, since a recycled block carries old values.
void C_StandaloneEntity::ApplyRenderPolicy()
{
	if ( Block().Client.PolicyOutputs == m_nAppliedPolicy )
		return;

	m_nAppliedPolicy = Block().Client.PolicyOutputs;

	const bool bWasIn = IsInLeafSystem();

	UpdateVisibility();

	if ( !IsInLeafSystem() || !bWasIn )
		return;

	AddToLeafSystem();
}

static void StandaloneClients_ApplyRenderPolicy()
{
	for ( int i = 0; i < g_StandaloneClients.Count(); i++ )
		g_StandaloneClients[i]->ApplyRenderPolicy();
}

static void StandaloneClients_PostInterpolate()
{
	const float flCurrentTime = gpGlobals->curtime;

	for ( int i = 0; i < g_StandaloneClients.Count(); i++ )
	{
		if ( g_StandaloneClients[i]->IsInterpAdmitted() )
			g_StandaloneClients[i]->PostInterpolate( flCurrentTime );
	}
}

class CSourceSharpStandaloneInterpSystem : public CClientSystemPerFrame
{
public:
	CSourceSharpStandaloneInterpSystem()
		: CClientSystemPerFrame( "CSourceSharpStandaloneInterpSystem" )
	{
	}

	virtual void PreRender() OVERRIDE
	{
		// Policy first, pose second: the policy inserts a newly drawn entity into the
		// leaf system, and PostInterpolate's move notification does nothing for an
		// entity not yet in it.
		StandaloneClients_ApplyRenderPolicy();

		StandaloneClients_PostInterpolate();
	}

	// Emptied at level shutdown: it holds block pointers into shells the shutdown deletes.
	virtual void LevelShutdownPostEntity() OVERRIDE
	{
		g_StandaloneInterpBlocks.Purge();
		g_StandalonePolicyBlocks.Purge();
	}
};

static CSourceSharpStandaloneInterpSystem g_StandaloneInterpSystem;

CClientSystem *ClientSystem_StandaloneInterp()
{
	return &g_StandaloneInterpSystem;
}

void SourceSharp_StandaloneSetCustomDraw( uint32_t hEntity, const float *pMinsXYZ, const float *pMaxsXYZ )
{
	if ( !pMinsXYZ || !pMaxsXYZ )
		return;

	const int nEntry = (int)( hEntity & ENT_ENTRY_MASK );

	C_StandaloneEntity *pEntity = SourceSharp_ClientShellFromIndex( nEntry );

	if ( !pEntity )
		return;

	pEntity->SetCustomDraw( Vector( pMinsXYZ[0], pMinsXYZ[1], pMinsXYZ[2] ),
							Vector( pMaxsXYZ[0], pMaxsXYZ[1], pMaxsXYZ[2] ) );
}

// CL_AllocDlight reuses the slot for a known key, so the same key moves a light
// rather than adding one.
void SourceSharp_DynamicLightUpdate( const SourceSharpDynamicLight *pLight )
{
	if ( !pLight || !effects )
		return;

	dlight_t *dl = pLight->IsElight ? effects->CL_AllocElight( pLight->Key ) : effects->CL_AllocDlight( pLight->Key );

	if ( !dl )
		return;

	dl->origin.Init( pLight->OriginXYZ[0], pLight->OriginXYZ[1], pLight->OriginXYZ[2] );
	dl->m_Direction.Init( pLight->DirectionXYZ[0], pLight->DirectionXYZ[1], pLight->DirectionXYZ[2] );

	dl->radius = pLight->Radius;

	dl->color.r = (unsigned char)clamp( pLight->Red, 0, 255 );
	dl->color.g = (unsigned char)clamp( pLight->Green, 0, 255 );
	dl->color.b = (unsigned char)clamp( pLight->Blue, 0, 255 );
	dl->color.exponent = (signed char)clamp( pLight->Exponent, -127, 127 );

	dl->style = (unsigned char)clamp( pLight->Style, 0, 63 );
	dl->flags = (unsigned char)pLight->Flags;
	dl->m_InnerAngle = pLight->InnerAngle;
	dl->m_OuterAngle = pLight->OuterAngle;
	dl->minlight = 0;

	dl->die = gpGlobals->curtime + pLight->LiveFor;

	// Refreshed lights pass zero, which a fresh slot already holds.
	dl->decay = pLight->Decay;
}

// Both pools are asked: this side does not track which one a key went into.
void SourceSharp_DynamicLightRelease( int32_t nKey )
{
	if ( !effects )
		return;

	if ( dlight_t *dl = effects->CL_AllocDlight( nKey ) )
		dl->die = gpGlobals->curtime;

	if ( dlight_t *el = effects->CL_AllocElight( nKey ) )
		el->die = gpGlobals->curtime;
}

bool C_StandaloneEntity::IsRagdoll() const
{
	return m_bRagdollPose;
}

void C_StandaloneEntity::SetRagdollPose( bool bRagdoll )
{
	m_bRagdollPose = bRagdoll;

	if ( bRagdoll )
		AddToLeafSystem();
}

// Re-leafed: the leaf system places a renderable by its bounds.
void C_StandaloneEntity::SetModelScale( float flScale )
{
	if ( flScale <= 0.0f )
	{
		Warning( "[SourceSharp] model scale %f refused on entity %d: a scale of zero "
				 "or less is a model drawn as nothing and picked by nothing\n",
				 flScale, entindex() );
		return;
	}

	if ( flScale == m_flModelScale )
		return;

	m_flModelScale = flScale;

	AddToLeafSystem();
}

void C_StandaloneEntity::SetFade( int nStyle, float flStartDist, float flEndDist, float flMinAlpha )
{
	EngineBlockClientTail_t &tail = Block().Client;

	tail.FadeStyle = nStyle;
	tail.FadeStartDistance = flStartDist;
	tail.FadeEndDistance = flEndDist;
	tail.FadeMinAlpha = flMinAlpha;

	BumpPolicyInputs();
}

C_StandaloneEntity *SourceSharp_StandaloneFromHandle( uint32_t hEntity )
{
	const int nEntry = (int)( hEntity & ENT_ENTRY_MASK );

	return SourceSharp_ClientShellFromIndex( nEntry );
}

static C_StandaloneEntity *ResolveStandaloneClient( uint32_t hEntity )
{
	return SourceSharp_StandaloneFromHandle( hEntity );
}

int32_t SourceSharp_ClientEntityGetIndex( uint32_t hEntity )
{
	if ( C_StandaloneEntity *pEntity = ResolveStandaloneClient( hEntity ) )
		return pEntity->entindex();

	return -1;
}

void SourceSharp_StandaloneSetFade( uint32_t hEntity, int32_t nStyle, float flStartDist, float flEndDist,
									float flMinAlpha )
{
	if ( C_StandaloneEntity *pEntity = ResolveStandaloneClient( hEntity ) )
		pEntity->SetFade( nStyle, flStartDist, flEndDist, flMinAlpha );
}

void SourceSharp_StandaloneSetOverlayModel( uint32_t hEntity, int32_t nModelIndex )
{
	if ( C_StandaloneEntity *pEntity = ResolveStandaloneClient( hEntity ) )
		pEntity->SetOverlayModel( nModelIndex );
}

void SourceSharp_StandaloneSetRagdoll( uint32_t hEntity, int32_t bRagdoll )
{
	if ( C_StandaloneEntity *pEntity = ResolveStandaloneClient( hEntity ) )
		pEntity->SetRagdollPose( bRagdoll != 0 );
}

// A handle naming no shell gets a zero box and is not counted.
int32_t SourceSharp_StandaloneSurroundingBounds( const uint32_t *pHandles, int32_t nCount, float *pOutMinsMaxs )
{
	if ( !pHandles || !pOutMinsMaxs || nCount <= 0 )
		return 0;

	int32_t nFound = 0;

	for ( int32_t i = 0; i < nCount; i++ )
	{
		float *pOut = pOutMinsMaxs + 6 * i;
		Vector vecMins( 0, 0, 0 ), vecMaxs( 0, 0, 0 );

		if ( C_StandaloneEntity *pEntity = ResolveStandaloneClient( pHandles[i] ) )
		{
			pEntity->WorldSpaceSurroundingBounds( &vecMins, &vecMaxs );
			nFound++;
		}

		pOut[0] = vecMins.x;
		pOut[1] = vecMins.y;
		pOut[2] = vecMins.z;
		pOut[3] = vecMaxs.x;
		pOut[4] = vecMaxs.y;
		pOut[5] = vecMaxs.z;
	}

	return nFound;
}

void SourceSharp_EntitySetSkin( uint32_t hEntity, int32_t nSkin )
{
	if ( C_StandaloneEntity *pEntity = ResolveStandaloneClient( hEntity ) )
		pEntity->SetSkin( (int)nSkin );
}

void SourceSharp_StandaloneSetModelScale( uint32_t hEntity, float flScale )
{
	if ( C_StandaloneEntity *pEntity = ResolveStandaloneClient( hEntity ) )
		pEntity->SetModelScale( flScale );
}

void SourceSharp_ClientEntityGetModelName( uint32_t hEntity, char *pOut, int32_t nOutSize )
{
	if ( !pOut || nOutSize <= 0 )
		return;

	pOut[0] = '\0';

	C_StandaloneEntity *pEntity = ResolveStandaloneClient( hEntity );

	if ( !pEntity )
		return;

	if ( const model_t *pModel = pEntity->GetModel() )
		Q_strncpy( pOut, modelinfo->GetModelName( pModel ), nOutSize );
}

// A handle that resolves to nothing answers 0: absent is not dormant.
int32_t SourceSharp_StandaloneIsDormant( uint32_t hEntity )
{
	C_StandaloneEntity *pEntity = ResolveStandaloneClient( hEntity );

	return ( pEntity && pEntity->IsDormant() ) ? 1 : 0;
}

int SourceSharp_StandaloneClientCount()
{
	return g_StandaloneClients.Count();
}

C_StandaloneEntity *SourceSharp_StandaloneClientAt( int nIndex )
{
	if ( nIndex < 0 || nIndex >= g_StandaloneClients.Count() )
		return NULL;

	return g_StandaloneClients[nIndex];
}

// Without this the shells survive the level change holding leaf-system and
// shadow handles into subsystems the shutdown reset.
int SourceSharp_StandaloneClientsLevelShutdown()
{
	const int nDestroyed = g_StandaloneClients.Count();

	// From the end: the destructor's FindAndFastRemove moves the tail into the freed
	// slot, which a forward walk would skip.
	for ( int i = g_StandaloneClients.Count() - 1; i >= 0; i-- )
	{
		if ( C_StandaloneEntity *pShell = g_StandaloneClients[i] )
			pShell->Release();
	}

	g_StandaloneClients.RemoveAll();

	// The per-frame block arrays hold pointers into the freed shells.
	g_StandaloneInterpBlocks.RemoveAll();
	g_StandalonePolicyBlocks.RemoveAll();

	return nDestroyed;
}

int32_t SourceSharp_StandaloneClientsInvalidateBoneCaches()
{
	return 0;
}

void SourceSharp_ActivateOccluder( int32_t nOccluderIndex, int32_t bActive )
{
	if ( nOccluderIndex < 0 )
		return;

	engine->ActivateOccluder( nOccluderIndex, bActive != 0 );
}

void C_StandaloneEntity::SetCustomDraw( const Vector &mins, const Vector &maxs )
{
	Block().Client.CustomDraw = 1;
	Block().Client.RenderMins = mins;
	Block().Client.RenderMaxs = maxs;

	BumpPolicyInputs();
}

void C_StandaloneEntity::SetBlending( bool bTranslucent, bool bIgnoreZ, int nAlpha )
{
	Block().Client.Translucent = bTranslucent ? 1 : 0;
	m_bIgnoreZ = bIgnoreZ;
	Block().Client.RenderColorA = (uint8_t)clamp( nAlpha, 0, 255 );

	BumpPolicyInputs();
}

void SourceSharp_StandaloneSetBlending( uint32_t hEntity, int32_t bTranslucent, int32_t bIgnoreZ, int32_t nAlpha )
{
	if ( C_StandaloneEntity *pEntity = SourceSharp_StandaloneFromHandle( hEntity ) )
		pEntity->SetBlending( bTranslucent != 0, bIgnoreZ != 0, nAlpha );
}

int32_t SourceSharp_StandaloneShadowCast( uint32_t hEntity, int32_t *pOutChoice )
{
	if ( pOutChoice )
		*pOutChoice = STANDALONE_SHADOW_CHOICE_AUTO;

	C_StandaloneEntity *pEntity = SourceSharp_StandaloneFromHandle( hEntity );

	if ( !pEntity )
		return -1;

	if ( pOutChoice )
		*pOutChoice = pEntity->ShadowCastChoice();

	return (int32_t)pEntity->ShadowCastType();
}

void SourceSharp_ClientEntityGetOrigin( uint32_t hEntity, float *pOutXYZ )
{
	if ( !pOutXYZ )
		return;

	pOutXYZ[0] = pOutXYZ[1] = pOutXYZ[2] = 0.0f;

	if ( C_StandaloneEntity *pEntity = SourceSharp_StandaloneFromHandle( hEntity ) )
	{
		const Vector &vecOrigin = pEntity->GetAbsOrigin();

		pOutXYZ[0] = vecOrigin.x;
		pOutXYZ[1] = vecOrigin.y;
		pOutXYZ[2] = vecOrigin.z;
	}
}

void SourceSharp_ClientEntityGetAngles( uint32_t hEntity, float *pOutPYR )
{
	if ( !pOutPYR )
		return;

	pOutPYR[0] = pOutPYR[1] = pOutPYR[2] = 0.0f;

	if ( C_StandaloneEntity *pEntity = SourceSharp_StandaloneFromHandle( hEntity ) )
	{
		const QAngle &ang = pEntity->GetAbsAngles();

		pOutPYR[0] = ang.x;
		pOutPYR[1] = ang.y;
		pOutPYR[2] = ang.z;
	}
}

void C_StandaloneEntity::SetViewModelPass( bool bEnable )
{
	if ( ( Block().Client.ViewModel != 0 ) == bEnable )
		return;

	Block().Client.ViewModel = bEnable ? 1 : 0;

	if ( bEnable )
	{
		Block().Client.CustomDraw = 0;
		if ( s_pViewModels.Find( this ) == s_pViewModels.InvalidIndex() )
			s_pViewModels.AddToTail( this );
	}
	else
	{
		s_pViewModels.FindAndRemove( this );
	}

	BumpPolicyInputs();
}

float C_StandaloneEntity::SequenceCycle( float flNow ) const
{
	if ( m_flSeqDuration <= 0.0f )
		return 0.0f;

	float flCycle = ( flNow - Block().Client.AnimTime ) * Block().Client.PlaybackRate / m_flSeqDuration;

	if ( flCycle < 0.0f )
		return 0.0f;

	if ( m_bSeqLooping )
		return flCycle - (int)flCycle;

	// A finished one-shot holds its last frame, as stock clamps the cycle.
	return MIN( flCycle, 0.999f );
}

void C_StandaloneEntity::MoveToEye( const Vector &vecEye, const QAngle &angEye )
{
	// The tail alone, so the collision origin stays put.
	Block().Client.RenderOrigin = vecEye;
	Block().Client.RenderAngles = angEye;

	AngleMatrix( Block().Client.RenderAngles, Block().Client.RenderOrigin, Block().Client.RenderTransform );
}

void SourceSharp_StandaloneViewModelToEye( const Vector &vecEye, const QAngle &angEye )
{
	for ( int i = 0; i < s_pViewModels.Count(); i++ )
		s_pViewModels[i]->MoveToEye( vecEye, angEye );
}

void SourceSharp_StandaloneSetViewModel( uint32_t hEntity, int32_t bEnable )
{
	if ( C_StandaloneEntity *pEntity = SourceSharp_StandaloneFromHandle( hEntity ) )
		pEntity->SetViewModelPass( bEnable != 0 );
}

void C_StandaloneEntity::StoreSequence( int nSequence, float flPlaybackRate, float flAnimTime, float flDuration,
										bool bLooping )
{
	Block().Client.Sequence = nSequence;
	Block().Client.PlaybackRate = flPlaybackRate;
	Block().Client.AnimTime = flAnimTime;
	m_flSeqDuration = flDuration;
	m_bSeqLooping = bLooping;
}

void SourceSharp_StandaloneStoreSequence( uint32_t hEntity, int32_t nSequence, float flPlaybackRate, float flAnimTime,
										  float flDuration, int32_t bLooping )
{
	if ( C_StandaloneEntity *pEntity = SourceSharp_StandaloneFromHandle( hEntity ) )
		pEntity->StoreSequence( nSequence, flPlaybackRate, flAnimTime, flDuration, bLooping != 0 );
}

void *SourceSharp_StandaloneEngineBlock( uint32_t hEntity, int32_t *pOutBytes )
{
	if ( pOutBytes )
		*pOutBytes = 0;

	C_StandaloneEntity *pEntity = SourceSharp_StandaloneFromHandle( hEntity );

	if ( !pEntity )
		return NULL;

	if ( pOutBytes )
		*pOutBytes = (int32_t)sizeof( EngineBlock_t );

	return &pEntity->Block();
}

COMPILE_TIME_ASSERT( STANDALONE_NOINTERP_PARITY_BITS == NOINTERP_PARITY_MAX_BITS );

// Compared as ints: an unnamed enum against SolidFlags_t is a -Wenum-compare
// error under gcc.
COMPILE_TIME_ASSERT( (int)STANDALONE_SOLID_FLAGS_MAX_BITS == (int)FSOLID_MAX_BITS );
