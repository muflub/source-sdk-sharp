//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON a  a vtable the engine calls through
// The `tempents` object the engine and leaf system hold a pointer to, and
// C_LocalTempEntity's renderable members; managed code decides everything they
// carry out. ITempEnts slots nothing in this mod calls warn once if called.

#include <stdio.h>
#include <stdlib.h>
#include "tier0/platform.h"
#include "tier1/strtools.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "client_entity.h"
#include "tempent.h"
#include "c_te_legacytempents.h"
#include "clienteffectprecachesystem.h"
#include "engine/ivmodelinfo.h"
#include "model_types.h"
#include "view.h"
#include "datacache/imdlcache.h"
#include "clientleafsystem.h"
#include "engine/IClientLeafSystem.h"
#include "engine/ivmodelrender.h"
#include "mathlib/mathlib.h"
#include "mathlib/vector.h"
#include "tier0/basetypes.h"
#include "tier0/dbg.h"
#include "tier1/mempool.h"
#include "tier1/utllinkedlist.h"
#include "Sprite.h"
#include "basehandle.h"
#include "cdll_client_int.h"
#include "cdll_int.h"
#include "const.h"
#include "eiface.h"
#include "globalvars_base.h"
#include "icliententityinternal.h"
#include "iclientrenderable.h"
#include "particlemgr.h"
#include "shared_classnames.h"
#include "vphysics_interface.h"
#include "sourcesharp.h"
#include "imanaged_areas_client.h"
#include "shell_renderable.h"

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

	// The block arrives zeroed; these are the values zero is wrong for.
	// m_nFXComputeFrame: zero is a real frame number.
	index             = -1;
	m_pModel          = NULL;
	m_nRenderMode     = kRenderNormal;
	m_nRenderFX       = kRenderFxNone;
	m_clrRender.r = m_clrRender.g = m_clrRender.b = m_clrRender.a = 255;
	m_nRenderFXBlend  = 255;
	m_nFXComputeFrame = -1;
}

// Resets place and model for recycled memory; the managed spawn record writes
// every other field straight after.
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

// Kept for the vtable; the managed pool owns lifetime and frame.
bool C_LocalTempEntity::IsActive( void )
{
	return true;
}

bool C_LocalTempEntity::Frame( float frametime, int framenumber )
{
	return true;
}

// The managed pool removes a slot from its breakmodel group when it frees it.
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

	// The blend this frame, bypassing GetFxBlend's frame protection as stock does:
	// this is asked from the leaf-system add, before any blend is computed.
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

	// The occlusion fade (FTENT_BEOCCLUDED): managed code answers the alpha.
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
		// NULL entity: a tempent has no IClientEntity for the material proxies, and
		// they treat NULL as "not an entity".
		return DrawSprite( NULL, GetModel(), GetAbsOrigin(), GetAbsAngles(), m_flFrame, NULL, m_nSkin,
		                   GetRenderMode(), m_nRenderFX, m_clrRender.a, m_clrRender.r, m_clrRender.g,
		                   m_clrRender.b, m_flSpriteScale );
	case mod_studio:
		return DrawStudioModel( modelFlags );
	default:
		return 0;
	}
}

// The engine's studio draw asks this before it skins the model. Managed code
// builds stock's tempent pose (sequence 0, cycle 0, zero pose parameters) and
// writes the render transform into every slot the build did not; with no
// managed side, or a non-studio model, every slot is the render transform.
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

// The breakmodel draw, lit from the group head's origin as managed code answers it.
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

void SourceSharpTePool_JoinBreakGroup( C_LocalTempEntity *pTemp, bool bSlave )
{
	if ( pTemp )
		pTemp->SetDrawHelper( BreakModelDrawHelper );
}

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

// The slots nothing in this mod calls: each warns once.
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
