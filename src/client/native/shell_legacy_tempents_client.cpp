//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp -- the legacy tempents, as a thunk ===================//
// SS_REASON a  a vtable the engine calls through
//
// c_te_legacytempents.cpp left client.so (plan_client_novalve C2, lane c2-fx,
// cluster 4). What it decided is C#'s, each part replayed against what the
// stock TU did, recorded through its probe copy (tempents-*-stock.tsv):
//
//   C_LocalTempEntity's render choices   TempEntRender (TempEntRenderTests)
//   the breakmodel lighting groups        TempEntBreakGroups (same)
//   the muzzle flashes                    MuzzleFlashes (MuzzleFlashTests)
//   PlaySound                             TempEntSounds (TempEntSoundTests)
//   AttachTentToPlayer                    TempEntAttachments (TempEntAttachTests)
//   EjectBrass's gate and model, and the level's model list   ClientTempEntDecisions
//
// The pool, the lifetimes, the frame and the spawn helpers moved before this
// (ClientTempEntPool, ClientTempEntFrame, TempEntSpawns). What is left here is
// what the engine and the leaf system hold a pointer to: the one CTempEnts the
// `tempents` global names (ITempEnts' vtable, every slot of which carries a
// call to managed or has no caller in this mod), and C_LocalTempEntity's
// renderable members, which read the tempent's own state and carry out the
// answer managed gave. Nothing here decides.
//
// THE ITempEnts SLOTS WITH NO CALLER. A runtime survey of the stock TU (every
// function's entry counted over a session, lane c2-fx) and a search of the SDK
// found nothing that calls BloodSprite, RicochetSprite, BreakModel, Bubbles,
// BubbleTrail, Sprite_Explode, FizzEffect, DefaultSprite, Sprite_Smoke,
// TempSprite, KillAttachedTents, Sprite_Spray or SpawnTempModel through this
// object: the managed temp entities spawn through TempEntSpawns and the pool,
// and Sprite_Explode, Sprite_Smoke, Sprite_Trail, RicochetSprite, RocketFlare
// and DefaultSprite have no caller anywhere in the SDK. Each says so once if
// that changes, rather than doing what stock did natively.
//=============================================================================//

#include "cbase.h"
#include "tempent.h"
#include "c_te_legacytempents.h"
#include "clienteffectprecachesystem.h"
#include "engine/ivmodelinfo.h"
#include "model_types.h"
#include "view.h"
#include "datacache/imdlcache.h"
#include "clientleafsystem.h"
#include "sourcesharp.h"
#include "imanaged_areas_client.h"
#include "shell_renderable.h"
#include "studio.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

// The pool's seam (shell_te_pool_client.cpp).
void SourceSharpTePool_Update( float flFrameTime );
void SourceSharpTePool_Clear( void );
C_LocalTempEntity *SourceSharpTePool_Find( int nID, int nSubID );
int SourceSharpTePool_SlotOf( const C_LocalTempEntity *pTemp );
C_LocalTempEntity *SourceSharpTePool_AtSlot( int nSlot );

// The materials the muzzle flashes draw with, precached at a level's start.
CLIENTEFFECT_REGISTER_BEGIN( PrecacheEffectMuzzleFlash )
CLIENTEFFECT_MATERIAL( "effects/muzzleflash1" )
CLIENTEFFECT_MATERIAL( "effects/muzzleflash2" )
CLIENTEFFECT_MATERIAL( "effects/muzzleflash3" )
CLIENTEFFECT_MATERIAL( "effects/muzzleflash4" )
CLIENTEFFECT_MATERIAL( "effects/muzzleflash1_noz" )
CLIENTEFFECT_MATERIAL( "effects/muzzleflash2_noz" )
CLIENTEFFECT_MATERIAL( "effects/muzzleflash3_noz" )
CLIENTEFFECT_MATERIAL( "effects/muzzleflash4_noz" )
CLIENTEFFECT_MATERIAL( "effects/combinemuzzle1" )
CLIENTEFFECT_MATERIAL( "effects/combinemuzzle2" )
CLIENTEFFECT_MATERIAL( "effects/combinemuzzle1_noz" )
CLIENTEFFECT_MATERIAL( "effects/combinemuzzle2_noz" )
CLIENTEFFECT_MATERIAL( "effects/strider_muzzle" )
CLIENTEFFECT_REGISTER_END()

static CTempEnts g_TempEnts;
ITempEnts *tempents = ( ITempEnts * )&g_TempEnts;

static void PutVector( float *p, const Vector &v ) { p[0] = v.x; p[1] = v.y; p[2] = v.z; }

//=============================================================================//
// C_LocalTempEntity: the renderable.
//=============================================================================//
C_LocalTempEntity::C_LocalTempEntity()
{
#ifdef _DEBUG
	tentOffset.Init();
	m_vecTempEntVelocity.Init();
	m_vecTempEntAngVelocity.Init();
	m_vecNormal.Init();
#endif
	m_vecTempEntAcceleration.Init();
	m_pfnDrawHelper = 0;
	m_pszImpactEffect = NULL;

	// The block arrives zeroed (the pool's AllocZero); these are the values a
	// zero is wrong for. m_nFXComputeFrame: zero is a real frame number.
	index             = -1;
	m_pModel          = NULL;
	m_nRenderMode     = kRenderNormal;
	m_nRenderFX       = kRenderFxNone;
	m_clrRender.r = m_clrRender.g = m_clrRender.b = m_clrRender.a = 255;
	m_nRenderFXBlend  = 255;
	m_nFXComputeFrame = -1;
	m_flGravity       = 0.0f;
}

// SetTempEntOwner( IGameEntity * ) is not defined here any more (C8): no
// object on the link line calls it; the pool sets the owner by handle
// (SetTempEntOwnerHandle), so game_entity_relays.h has no site in this file.

// The tempent's place and model, reset for recycled memory; every other field
// is the managed spawn record's, written whole straight after this.
void C_LocalTempEntity::Prepare( const model_t *pmodel, float time )
{
	index = -1;
	SetAbsOrigin( vec3_origin );
	SetAbsAngles( vec3_angle );
	SetModelPointer( pmodel );
}

void C_LocalTempEntity::SetVelocity( const Vector &vecVelocity )
{
	m_vecTempEntVelocity = vecVelocity;
}

void C_LocalTempEntity::SetAcceleration( const Vector &vecVelocity )
{
	m_vecTempEntAcceleration = vecVelocity;
}

// The vtable keeps these two; the lifetime and the frame are the managed
// pool's (ClientTempEntPool, ClientTempEntFrame), and nothing calls them.
bool C_LocalTempEntity::IsActive( void )
{
	return true;
}

bool C_LocalTempEntity::Frame( float frametime, int framenumber )
{
	return true;
}

// The breakmodel groups are the managed pool's (TempEntBreakGroups): a slot
// leaves its group when the pool frees it.
void C_LocalTempEntity::OnRemoveTempEntity()
{
}

bool C_LocalTempEntity::IsTransparent( void )
{
	IManagedFxClient *pFx = ManagedFxClient();
	const int bTranslucent = modelinfo->IsTranslucent( GetModel() ) ? 1 : 0;
	return pFx ? pFx->TempEntIsTransparent( bTranslucent, m_nRenderMode ) != 0 : bTranslucent != 0;
}

void C_LocalTempEntity::GetRenderBounds( Vector &theMins, Vector &theMaxs )
{
	if ( !GetModel() )
	{
		theMins = vec3_origin;
		theMaxs = vec3_origin;
		return;
	}

	modelinfo->GetModelRenderBounds( GetModel(), theMins, theMaxs );
}

void C_LocalTempEntity::GetColorModulation( float *color )
{
	color[0] = m_clrRender.r / 255.0f;
	color[1] = m_clrRender.g / 255.0f;
	color[2] = m_clrRender.b / 255.0f;
}

RenderGroup_t C_LocalTempEntity::GetRenderGroup()
{
	IManagedFxClient *pFx = ManagedFxClient();
	if ( !pFx )
		return RENDER_GROUP_OPAQUE_ENTITY;

	// The blend this frame, through GetFxBlend's frame protection bypassed, as
	// stock reads it: this is asked from the leaf-system add, before anything
	// has computed a blend this frame.
	int nBlend = 255;
	if ( m_nRenderMode != kRenderNone )
	{
		const int nTempComputeFrame = m_nFXComputeFrame;
		m_nFXComputeFrame = gpGlobals->framecount;
		nBlend = GetFxBlend();
		m_nFXComputeFrame = nTempComputeFrame;
	}

	const int bTranslucent = modelinfo->IsTranslucent( GetModel() ) ? 1 : 0;
	const int bTwoPass = modelinfo->IsTranslucentTwoPass( GetModel() ) ? 1 : 0;
	return (RenderGroup_t)pFx->TempEntRenderGroup( m_nRenderMode, nBlend, bTranslucent, bTwoPass );
}

void C_LocalTempEntity::ComputeFxBlend( void )
{
	// Don't recompute if we've already computed this frame
	if ( m_nFXComputeFrame == gpGlobals->framecount )
		return;

	IManagedFxClient *pFx = ManagedFxClient();
	if ( !pFx )
		return;

	float in[ 14 ];
	in[0] = (float)m_nRenderFX;
	in[1] = (float)m_clrRender.a;
	in[2] = (float)m_nRenderMode;
	in[3] = (float)index;
	in[4] = gpGlobals->curtime;
	PutVector( in + 5, GetAbsOrigin() );
	PutVector( in + 8, CurrentViewOrigin() );
	PutVector( in + 11, CurrentViewForward() );

	int32_t out[ 2 ] = { 255, m_clrRender.a };
	pFx->TempEntBlend( in, out );

	SetRenderColorA( (byte)out[1] );
	m_nRenderFXBlend = out[0];
	m_nFXComputeFrame = gpGlobals->framecount;

	// The render group follows the blend, as stock's GetRenderHandle() update did.
	if ( IsInLeafSystem() )
		ClientLeafSystem()->SetRenderGroup( RenderHandle(), GetRenderGroup() );
}

int C_LocalTempEntity::DrawStudioModel( int modelFlags )
{

	if ( !GetModel() || modelinfo->GetModelType( GetModel() ) != mod_studio )
		return 0;

	MDLCACHE_CRITICAL_SECTION();
	if ( !modelinfo->GetStudiomodel( GetModel() ) )
		return 0;

	if ( m_pfnDrawHelper )
		return ( *m_pfnDrawHelper )( this, modelFlags );

	return modelrender->DrawModel( modelFlags, this, MODEL_INSTANCE_INVALID, index, GetModel(),
	                               GetAbsOrigin(), GetAbsAngles(), m_nSkin, m_nBody, m_nHitboxSet );
}

int C_LocalTempEntity::DrawModel( int modelFlags )
{
	if ( !GetModel() || GetRenderMode() == kRenderNone )
		return 0;

	// The occlusion fade (FTENT_BEOCCLUDED): managed's answer for the alpha.
	if ( this->flags & FTENT_BEOCCLUDED )
	{
		if ( IManagedFxClient *pFx = ManagedFxClient() )
		{
			float in[ 9 ];
			PutVector( in, m_vecNormal );
			PutVector( in + 3, GetAbsOrigin() );
			PutVector( in + 6, MainViewOrigin() );
			SetRenderColorA( (byte)pFx->TempEntOccludedAlpha( flags, m_clrRender.a, tempent_renderamt, in ) );
		}
	}

	switch ( modelinfo->GetModelType( GetModel() ) )
	{
	case mod_sprite:
		// NULL entity: a de-entified tempent has no IClientEntity to bind the
		// material proxies to, and NULL is the answer they treat as "not an entity".
		return DrawSprite( NULL, GetModel(), GetAbsOrigin(), GetAbsAngles(), m_flFrame, NULL, m_nSkin,
		                   GetRenderMode(), m_nRenderFX, m_clrRender.a, m_clrRender.r, m_clrRender.g,
		                   m_clrRender.b, m_flSpriteScale );
	case mod_studio:
		return DrawStudioModel( modelFlags );
	default:
		return 0;
	}
}

//=============================================================================//
// CShellRenderable::SetupBones -- see shell_renderable.h. The engine's studio
// draw asks this before it skins the model; every slot it may read is written.
//
// THE POSE IS MANAGED (plan physics_bones_native_zero 1.2, client half): a
// thunk into IManagedBonesClient::PoseModel, which builds stock's tempent pose
// -- sequence 0, cycle 0, pose parameters it never set (zero), the client's
// IK rule -- and writes the render transform into every slot the build did
// not. With no managed side, or a model that is not studio, every slot is the
// render transform here.
//=============================================================================//
bool CShellRenderable::SetupBones( matrix3x4_t *pBoneToWorldOut, int nMaxBones, int boneMask,
                                   float currentTime )
{
	if ( !pBoneToWorldOut || nMaxBones <= 0 )
		return true;

	const matrix3x4_t &matRender = RenderableToWorldTransform();
	const model_t *pModel = GetModel();
	IManagedBonesClient *pBones = ManagedBonesClient();

	if ( pBones && pModel && modelinfo->GetModelType( pModel ) == mod_studio )
	{
		const Vector &vecOrigin = GetRenderOrigin();
		const QAngle &angAngles = GetRenderAngles();
		const float flPlace[ 6 ] = { vecOrigin.x, vecOrigin.y, vecOrigin.z,
		                             angAngles.x, angAngles.y, angAngles.z };

		if ( pBones->PoseModel( modelinfo->GetModelIndex( modelinfo->GetModelName( pModel ) ), flPlace,
		                        matRender.Base(), pBoneToWorldOut[0].Base(), nMaxBones, boneMask,
		                        currentTime, gpGlobals->framecount ) )
			return true;
	}

	for ( int i = 0; i < nMaxBones; i++ )
		MatrixCopy( matRender, pBoneToWorldOut[ i ] );

	return true;
}

//=============================================================================//
// The breakmodel draw: the lighting origin is the group head's, as managed
// answers it (TempEntBreakGroups).
//=============================================================================//
static int BreakModelDrawHelper( C_LocalTempEntity *entity, int flags )
{
	ModelRenderInfo_t sInfo;
	sInfo.flags = flags;
	sInfo.pRenderable = entity;
	sInfo.instance = MODEL_INSTANCE_INVALID;
	sInfo.entity_index = entity->index;
	sInfo.pModel = entity->GetModel();
	sInfo.origin = entity->GetRenderOrigin();
	sInfo.angles = entity->GetRenderAngles();
	sInfo.skin = entity->m_nSkin;
	sInfo.body = entity->m_nBody;
	sInfo.hitboxset = entity->m_nHitboxSet;

	if ( IManagedFxClient *pFx = ManagedFxClient() )
	{
		const int nHead = pFx->TempEntLightingOrigin( SourceSharpTePool_SlotOf( entity ) );
		if ( C_LocalTempEntity *pHead = SourceSharpTePool_AtSlot( nHead ) )
			sInfo.pLightingOrigin = &pHead->GetAbsOrigin();
	}

	return modelrender->DrawModelEx( sInfo );
}

// The draw path a breakmodel takes; its group is the managed pool's.
void SourceSharpTePool_JoinBreakGroup( C_LocalTempEntity *pTemp, bool bSlave )
{
	if ( pTemp )
		pTemp->SetDrawHelper( BreakModelDrawHelper );
}

//=============================================================================//
// CTempEnts: ITempEnts' vtable.
//=============================================================================//
CTempEnts::CTempEnts( void ) :
	m_TempEntsPool( ( MAX_TEMP_ENTITIES / 20 ), CUtlMemoryPool::GROW_SLOW )
{
}

CTempEnts::~CTempEnts( void )
{
	m_TempEntsPool.Clear();
	m_TempEnts.RemoveAll();
}

void CTempEnts::Init( void )
{
	Clear();
}

void CTempEnts::Shutdown()
{
	LevelShutdown();
}

void CTempEnts::LevelInit()
{
	if ( IManagedFxClient *pFx = ManagedFxClient() )
		pFx->TempEntsLevelInit();
}

void CTempEnts::LevelShutdown()
{
	Clear();
}

void CTempEnts::Update( void )
{

	if ( !engine->IsInGame() )
		return;

	SourceSharpTePool_Update( gpGlobals->frametime );
}

void CTempEnts::Clear( void )
{
	SourceSharpTePool_Clear();
}

C_LocalTempEntity *CTempEnts::FindTempEntByID( int nID, int nSubID )
{
	return SourceSharpTePool_Find( nID, nSubID );
}

C_LocalTempEntity *CTempEnts::TempEntAlloc()
{
	return m_TempEntsPool.AllocZero();
}

int CTempEnts::AddVisibleTempEntity( C_LocalTempEntity *pEntity )
{
	if ( !pEntity->GetModel() )
		return 0;

	pEntity->index = -1;
	if ( pEntity->m_RenderGroup == RENDER_GROUP_OTHER )
		pEntity->AddToLeafSystem();
	else
		pEntity->AddToLeafSystem( pEntity->m_RenderGroup );
	return 1;
}

void CTempEnts::MuzzleFlash( int type, ClientEntityHandle_t hEntity, int attachmentIndex, bool firstPerson )
{
	if ( IManagedFxClient *pFx = ManagedFxClient() )
		pFx->TempEntMuzzleFlash( type, (uint32_t)hEntity.ToInt(), attachmentIndex, firstPerson ? 1 : 0, 0 );
}

void CTempEnts::MuzzleFlash( const Vector &pos1, const QAngle &angles, int type, ClientEntityHandle_t hEntity, bool firstPerson )
{
	if ( IManagedFxClient *pFx = ManagedFxClient() )
		pFx->TempEntMuzzleFlash( type, (uint32_t)hEntity.ToInt(), 1, firstPerson ? 1 : 0, 1 );
}

void CTempEnts::EjectBrass( const Vector &pos1, const QAngle &angles, const QAngle &gunAngles, int type )
{
	IManagedFxClient *pFx = ManagedFxClient();
	if ( !pFx )
		return;
	float in[ 9 ];
	PutVector( in, pos1 );
	in[3] = angles.x; in[4] = angles.y; in[5] = angles.z;
	in[6] = gunAngles.x; in[7] = gunAngles.y; in[8] = gunAngles.z;
	pFx->TempEntEjectBrass( in, type );
}

void CTempEnts::AttachTentToPlayer( int client, int modelIndex, float zoffset, float life )
{
	if ( IManagedFxClient *pFx = ManagedFxClient() )
		pFx->TempEntAttach( client, modelIndex, zoffset, life, gpGlobals->maxClients );
}

// The slots nothing in this mod calls (see the banner). Said once each.
//
// WHERE EACH ONE'S BEHAVIOUR IS, so a stub is never the only copy:
//   BloodSprite, BreakModel, Bubbles, BubbleTrail, FizzEffect, TempSprite,
//   Sprite_Spray, ClientProjectile   the managed te players (ClientTe*) call
//                                    TempEntSpawns' port of each
//   KillAttachedTents                ClientTeKillPlayerAttachments -> ClientTempEntPool
//   PlaySound                        ClientTempEntFrame -> TempEntSounds
//   RicochetSprite, DefaultSprite, Sprite_Explode, Sprite_Smoke, Sprite_Trail,
//   RocketFlare, SpawnTempModel      TempEntSpawns / ClientTempEntDecisions, held to
//                                    stock by TempEntNoCallerArmsOracleTests;
//                                    SpawnTempModel's caller, BoltImpact, is
//                                    CrossbowBoltEffect
//   HL1EjectBrass, CSEjectBrass      a no-op in stock hl2mp too (the model is set only
//                                    under HL1_CLIENT_DLL / CSTRIKE_DLL / SDK_DLL)
//   PhysicsProp (both)               refused with C_PhysPropClientside
//                                    (ClientTePhysicsProp; clientside_prop_evicted_test)
#define SS_TEMPENTS_NO_CALLER( name ) \
	do { static bool s_bSaid = false; if ( !s_bSaid ) { s_bSaid = true; \
		DevWarning( "[SourceSharp] CTempEnts::" name " was called; nothing in this mod calls it, and the " \
		            "managed temp entities spawn through TempEntSpawns. Nothing drawn.\n" ); } } while ( 0 )

void CTempEnts::BloodSprite( const Vector &, int, int, int, int, int, int, float ) { SS_TEMPENTS_NO_CALLER( "BloodSprite" ); }
void CTempEnts::RicochetSprite( const Vector &, model_t *, float, float ) { SS_TEMPENTS_NO_CALLER( "RicochetSprite" ); }
void CTempEnts::BreakModel( const Vector &, const QAngle &, const Vector &, const Vector &, float, float, int, int, char ) { SS_TEMPENTS_NO_CALLER( "BreakModel" ); }
void CTempEnts::Bubbles( const Vector &, const Vector &, float, int, int, float ) { SS_TEMPENTS_NO_CALLER( "Bubbles" ); }
void CTempEnts::BubbleTrail( const Vector &, const Vector &, float, int, int, float ) { SS_TEMPENTS_NO_CALLER( "BubbleTrail" ); }
void CTempEnts::Sprite_Explode( C_LocalTempEntity *, float, int ) { SS_TEMPENTS_NO_CALLER( "Sprite_Explode" ); }
void CTempEnts::FizzEffect( IClientRenderable *, int, int, int ) { SS_TEMPENTS_NO_CALLER( "FizzEffect" ); }
C_LocalTempEntity *CTempEnts::DefaultSprite( const Vector &, int, float ) { SS_TEMPENTS_NO_CALLER( "DefaultSprite" ); return NULL; }
void CTempEnts::Sprite_Smoke( C_LocalTempEntity *, float ) { SS_TEMPENTS_NO_CALLER( "Sprite_Smoke" ); }
C_LocalTempEntity *CTempEnts::TempSprite( const Vector &, const Vector &, float, int, int, int, float, float, int, const Vector & ) { SS_TEMPENTS_NO_CALLER( "TempSprite" ); return NULL; }
void CTempEnts::KillAttachedTents( int ) { SS_TEMPENTS_NO_CALLER( "KillAttachedTents" ); }
void CTempEnts::Sprite_Spray( const Vector &, const Vector &, int, int, int, int ) { SS_TEMPENTS_NO_CALLER( "Sprite_Spray" ); }
void CTempEnts::PlaySound( C_LocalTempEntity *, float ) { SS_TEMPENTS_NO_CALLER( "PlaySound" ); }
C_LocalTempEntity *CTempEnts::SpawnTempModel( const model_t *, const Vector &, const QAngle &, const Vector &, float, int ) { SS_TEMPENTS_NO_CALLER( "SpawnTempModel" ); return NULL; }
void CTempEnts::Sprite_Trail( const Vector &, const Vector &, int, int, float, float, float, int, float ) { SS_TEMPENTS_NO_CALLER( "Sprite_Trail" ); }
void CTempEnts::RocketFlare( const Vector & ) { SS_TEMPENTS_NO_CALLER( "RocketFlare" ); }
void CTempEnts::HL1EjectBrass( const Vector &, const QAngle &, const Vector &, int ) { SS_TEMPENTS_NO_CALLER( "HL1EjectBrass" ); }
void CTempEnts::CSEjectBrass( const Vector &, const QAngle &, int, int, CBasePlayer * ) { SS_TEMPENTS_NO_CALLER( "CSEjectBrass" ); }
void CTempEnts::PhysicsProp( int, int, const Vector &, const QAngle &, const Vector &, int, int ) { SS_TEMPENTS_NO_CALLER( "PhysicsProp" ); }
C_LocalTempEntity *CTempEnts::ClientProjectile( const Vector &, const Vector &, const Vector &, int, int, IGameEntity *, const char *, const char * ) { SS_TEMPENTS_NO_CALLER( "ClientProjectile" ); return NULL; }
C_PhysPropClientside *CTempEnts::PhysicsProp( const model_t *, int, const Vector &, const QAngle &, const Vector &, int, int, int ) { SS_TEMPENTS_NO_CALLER( "PhysicsProp(model)" ); return NULL; }
