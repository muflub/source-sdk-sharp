//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON g  leaf math over mdl/bsp data
// The beam executor; managed code decides everything about a beam. This holds
// `beams` (forwarding to IManagedBeamsClient), the pool of leaf-system
// renderables managed code moves by index, the material system draw calls, and
// the reads managed code needs: the view, model frames, endpoint facts, box
// tests, the halo's sight test and the overlay's screen position.

#include <stdio.h>
#include <stdlib.h>
#include "tier0/platform.h"
#include "tier0/dbg.h"
#include "tier1/strtools.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "const.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "iviewrender_beams.h"
#include "enginesprite.h"
#include "materialsystem/imaterialsystem.h"
#include "materialsystem/imaterial.h"
#include "materialsystem/imaterialvar.h"
#include "materialsystem/imesh.h"
#include "engine/ivdebugoverlay.h"
#include "clientleafsystem.h"
#include "view.h"
#include "engine/ICollideable.h"
#include "engine/IClientLeafSystem.h"
#include "engine/ivmodelinfo.h"
#include "mathlib/mathlib.h"
#include "mathlib/vector.h"
#include "mathlib/vmatrix.h"
#include "particles/particles.h"
#include "tier1/refcount.h"
#include "tier1/utlvector.h"
#include "basehandle.h"
#include "cdll_client_int.h"
#include "cdll_int.h"
#include "client_entity.h"
#include "client_render_handle.h"
#include "const.h"
#include "eiface.h"
#include "globalvars_base.h"
#include "icliententitylist.h"
#include "iclientrenderable.h"
#include "iclientunknown.h"
#include "ihandleentity.h"
#include "texture_group_names.h"
#include "vphysics_interface.h"
#include "beam_strip_vertex.h"
#include "shell_cliententitylist.h"
#include "slot_table.h"
// c_env_fog_controller.h before the player shell: C_StandalonePlayer's
// CNetworkHandle< C_FogController > needs the class complete.
#include "shell_standalone_client.h"
#include "collideable_center.h"
#include "imanaged_areas_client.h"
#include "shell_beam_render.h"
#include "ss_command_line.h"

namespace
{

// The raw handle managed code holds, back into a CBaseHandle (0xFFFFFFFF is none).
CBaseHandle HandleFromRaw( uint32_t hRaw )
{
	if ( hRaw == 0xFFFFFFFFu )
		return CBaseHandle();
	return CBaseHandle( (int)( hRaw & ENT_ENTRY_MASK ), (int)( hRaw >> NUM_SERIAL_NUM_SHIFT_BITS ) );
}

uint32_t RawFromEntity( IClientEntity *pEnt )
{
	return pEnt ? (uint32_t)pEnt->GetRefEHandle().ToInt() : 0xFFFFFFFFu;
}

// One leaf-system renderable per drawn managed beam. Bounds and origin are
// managed code's; the rest is stock Beam_t's: translucent, always drawn, full blend.
class CBeamRenderable : public CDefaultClientRenderable
{
public:
	CBeamRenderable() : m_nIndex( -1 ), m_bLive( false )
	{
		m_vecOrigin.Init();
		m_vecMins.Init();
		m_vecMaxs.Init();
	}

	void Set( const float *p9 )
	{
		m_vecOrigin.Init( p9[0], p9[1], p9[2] );
		m_vecMins.Init( p9[3], p9[4], p9[5] );
		m_vecMaxs.Init( p9[6], p9[7], p9[8] );
	}

	const Vector &GetRenderOrigin( void ) override { return m_vecOrigin; }
	const QAngle &GetRenderAngles( void ) override { return vec3_angle; }

	const matrix3x4_t &RenderableToWorldTransform() override
	{
		SetIdentityMatrix( m_Transform );
		PositionMatrix( m_vecOrigin, m_Transform );
		return m_Transform;
	}

	void GetRenderBounds( Vector &mins, Vector &maxs ) override
	{
		mins = m_vecMins;
		maxs = m_vecMaxs;
	}

	bool ShouldDraw( void ) override { return true; }
	bool IsTransparent( void ) override { return true; }
	void ComputeFxBlend() override {}
	int GetFxBlend() override { return 255; }

	int DrawModel( int ) override
	{
		if ( IManagedBeamsClient *pBeams = ManagedBeamsClient() )
			pBeams->BeamsDraw( m_nIndex );
		return 0;
	}

	int m_nIndex;
	bool m_bLive;

private:
	Vector m_vecOrigin;
	Vector m_vecMins;
	Vector m_vecMaxs;
	matrix3x4_t m_Transform;
};

CUtlVector< CBeamRenderable * > g_BeamRenderables;
CUtlVector< int > g_FreeBeamRenderables;

CBeamRenderable *RenderableAt( int nIndex )
{
	if ( nIndex < 0 || nIndex >= g_BeamRenderables.Count() )
		return NULL;
	CBeamRenderable *p = g_BeamRenderables[nIndex];
	return p->m_bLive ? p : NULL;
}

enum BeamLifecycle_t
{
	BEAM_LIFECYCLE_INIT = 0,
	BEAM_LIFECYCLE_SHUTDOWN,
	BEAM_LIFECYCLE_CLEAR,
};

// The interface engine-facing code names, forwarding to managed code. Nothing
// creates a beam through it, so the creators answer no Beam_t.
class CSourceSharpBeams : public IViewRenderBeams
{
public:
	void InitBeams( void ) override
	{
		if ( IManagedBeamsClient *pBeams = ManagedBeamsClient() )
		{
			char szParticles[ 32 ];
			pBeams->BeamsLifecycle( BEAM_LIFECYCLE_INIT,
			                        ss::CommandLineValue( "-particles", "", szParticles, sizeof( szParticles ) )
			                            ? atoi( szParticles ) : -1 );
		}
	}

	void ShutdownBeams( void ) override
	{
		if ( IManagedBeamsClient *pBeams = ManagedBeamsClient() )
			pBeams->BeamsLifecycle( BEAM_LIFECYCLE_SHUTDOWN, 0 );
	}

	void ClearBeams( void ) override
	{
		if ( IManagedBeamsClient *pBeams = ManagedBeamsClient() )
			pBeams->BeamsLifecycle( BEAM_LIFECYCLE_CLEAR, 0 );
	}

	void UpdateTempEntBeams() override
	{
		if ( IManagedBeamsClient *pBeams = ManagedBeamsClient() )
			pBeams->BeamsUpdate( gpGlobals->curtime, gpGlobals->frametime );
	}

	void DrawBeam( Beam_t * ) override {}
	void DrawBeam( C_Beam *, ITraceFilter * ) override {}

	void KillDeadBeams( IGameEntity *pDead ) override {}

	// Not IViewRenderBeams': stock's takes the entity, and a shell is not one.
	void KillDeadBeams( const CBaseHandle &hDeadEntity )
	{
		if ( IManagedBeamsClient *pBeams = ManagedBeamsClient() )
			pBeams->BeamsKillDead( (uint32_t)hDeadEntity.ToInt(), gpGlobals->curtime );
	}

	Beam_t *CreateBeamEnts( BeamInfo_t & ) override { return NULL; }
	Beam_t *CreateBeamEntPoint( BeamInfo_t & ) override { return NULL; }
	Beam_t *CreateBeamPoints( BeamInfo_t & ) override { return NULL; }
	Beam_t *CreateBeamRing( BeamInfo_t & ) override { return NULL; }
	Beam_t *CreateBeamRingPoint( BeamInfo_t & ) override { return NULL; }
	Beam_t *CreateBeamCirclePoints( BeamInfo_t & ) override { return NULL; }
	Beam_t *CreateBeamFollow( BeamInfo_t & ) override { return NULL; }

	void FreeBeam( Beam_t * ) override {}
	void UpdateBeamInfo( Beam_t *, BeamInfo_t & ) override {}

	void CreateBeamEnts( int, int, int, int, float, float, float, float, float, float, float, float, int, float,
	                     float, float, float, int ) override {}
	void CreateBeamEntPoint( int, const Vector *, int, const Vector *, int, int, float, float, float, float, float,
	                         float, float, float, int, float, float, float, float ) override {}
	void CreateBeamPoints( Vector &, Vector &, int, int, float, float, float, float, float, float, float, float,
	                       int, float, float, float, float ) override {}
	void CreateBeamRing( int, int, int, int, float, float, float, float, float, float, float, float, int, float,
	                     float, float, float, int ) override {}
	void CreateBeamRingPoint( const Vector &, float, float, int, int, float, float, float, float, float, float,
	                          float, float, int, float, float, float, float, int ) override {}
	void CreateBeamCirclePoints( int, Vector &, Vector &, int, int, float, float, float, float, float, float,
	                             float, float, int, float, float, float, float ) override {}
	void CreateBeamFollow( int, int, int, float, float, float, float, float, float, float, float, float ) override {}
};

CSourceSharpBeams g_SourceSharpBeams;

} // namespace

void SourceSharp_KillDeadBeams( const CBaseHandle &hDeadEntity )
{
	g_SourceSharpBeams.KillDeadBeams( hDeadEntity );
}

IViewRenderBeams *beams = &g_SourceSharpBeams;

void SourceSharp_BeamView( int32_t bCurrentView, float *pOut42 )
{
	if ( !pOut42 )
		return;

	Vector *v = reinterpret_cast< Vector * >( pOut42 );
	if ( bCurrentView )
	{
		v[0] = CurrentViewOrigin();
		v[1] = CurrentViewForward();
		v[2] = CurrentViewRight();
		v[3] = CurrentViewUp();
		const QAngle &angles = CurrentViewAngles();
		v[4].Init( angles.x, angles.y, angles.z );

		// The render context's world-space camera: what a strip faces.
		CMatRenderContextPtr pRenderContext( materials );
		pRenderContext->GetWorldSpaceCameraPosition( reinterpret_cast< Vector * >( pOut42 + 39 ) );
	}
	v[5] = MainViewUp();
	v[6] = MainViewRight();

	const VMatrix &worldToScreen = engine->WorldToScreenMatrix();
	for ( int i = 0; i < 16; i++ )
		pOut42[21 + i] = worldToScreen.m[i / 4][i % 4];

	pOut42[37] = gpGlobals->curtime;
	pOut42[38] = gpGlobals->frametime;
}

int32_t SourceSharp_BeamModelInfo( int32_t nModelIndex, int32_t *pOutFrameCount )
{
	const model_t *pModel = modelinfo->GetModel( nModelIndex );
	if ( pOutFrameCount )
		*pOutFrameCount = pModel ? modelinfo->GetModelFrameCount( pModel ) : 0;
	return pModel ? 1 : 0;
}

int32_t SourceSharp_BeamModelIndex( const char *pszName )
{
	return pszName ? modelinfo->GetModelIndex( pszName ) : -1;
}

uint32_t SourceSharp_BeamEntityAt( int32_t nEntry )
{
	return RawFromEntity( SourceSharp_ClientEntityFromIndex( nEntry ) );
}

int32_t SourceSharp_BeamEntityHasModel( uint32_t hEntity )
{
	IClientEntity *pEnt = SourceSharp_ClientEntityFromHandle( HandleFromRaw( hEntity ) );
	return ( pEnt && pEnt->GetModel() ) ? 1 : 0;
}

// The facts a beam endpoint's position is decided from: which hierarchy holds
// the handle, whether it renders and is a player, and the candidate points (the
// attachment, asked only when managed code says to, the collision centre and the
// render origin).
enum BeamEntityFact_t
{
	BEAM_FACT_RESOLVED     = 0x01,
	BEAM_FACT_RENDERABLE   = 0x04,
	BEAM_FACT_PLAYER       = 0x08,
	BEAM_FACT_ATTACHMENT   = 0x10,
	BEAM_FACT_COLLIDEABLE  = 0x20,
};

int32_t SourceSharp_BeamEntityFacts( uint32_t hEntity, int32_t nAttachment, int32_t bTryAttachment, float *pOut9 )
{
	if ( !pOut9 )
		return 0;
	Vector *pOut = reinterpret_cast< Vector * >( pOut9 );

	IClientUnknown *pUnknown = SourceSharp_ClientUnknownFromHandle( HandleFromRaw( hEntity ) );
	if ( !pUnknown )
		return 0;

	int32_t nFacts = BEAM_FACT_RESOLVED;

	IClientRenderable *pRenderable = pUnknown->GetClientRenderable();
	if ( !pRenderable )
		return nFacts;
	nFacts |= BEAM_FACT_RENDERABLE;

	C_StandaloneEntity *pShell = SourceSharp_ClientShellFromHandleEntity( pUnknown );
	if ( pShell && pShell->IsPlayerShell() )
		nFacts |= BEAM_FACT_PLAYER;

	if ( bTryAttachment )
	{
		QAngle angles;
		if ( pRenderable->GetAttachment( nAttachment, pOut[0], angles ) )
			nFacts |= BEAM_FACT_ATTACHMENT;
	}

	if ( ICollideable *pCollideable = pUnknown->GetCollideable() )
	{
		pOut[1] = UTIL_CollideableWorldSpaceCenter( pCollideable );
		nFacts |= BEAM_FACT_COLLIDEABLE;
	}

	pOut[2] = pRenderable->GetRenderOrigin();
	return nFacts;
}

int32_t SourceSharp_BeamBoxVisibility( const float *pMinsXYZ, const float *pMaxsXYZ )
{
	const Vector &mins = *reinterpret_cast< const Vector * >( pMinsXYZ );
	const Vector &maxs = *reinterpret_cast< const Vector * >( pMaxsXYZ );
	return ( engine->IsBoxVisible( mins, maxs ) ? 1 : 0 ) | ( engine->CullBox( mins, maxs ) ? 2 : 0 );
}

int32_t SourceSharp_BeamScreenPosition( const float *pPointXYZ, float *pOutScreenXYZ )
{
	Vector screen;
	const int behind = debugoverlay->ScreenPosition( *reinterpret_cast< const Vector * >( pPointXYZ ), screen );
	VectorCopy( screen, *reinterpret_cast< Vector * >( pOutScreenXYZ ) );
	return behind;
}

int32_t SourceSharp_BeamRenderableAdd( const float *pOriginMinsMaxs9 )
{
	int nIndex;
	if ( g_FreeBeamRenderables.Count() )
	{
		nIndex = g_FreeBeamRenderables.Tail();
		g_FreeBeamRenderables.RemoveMultipleFromTail( 1 );
	}
	else
	{
		nIndex = g_BeamRenderables.AddToTail( new CBeamRenderable );
		g_BeamRenderables[nIndex]->m_nIndex = nIndex;
	}

	CBeamRenderable *p = g_BeamRenderables[nIndex];
	p->m_bLive = true;
	p->Set( pOriginMinsMaxs9 );
	ClientLeafSystem()->AddRenderable( p, RENDER_GROUP_TRANSLUCENT_ENTITY );
	return nIndex;
}

void SourceSharp_BeamRenderableChanged( int32_t nRenderable, const float *pOriginMinsMaxs9 )
{
	CBeamRenderable *p = RenderableAt( nRenderable );
	if ( !p )
		return;
	p->Set( pOriginMinsMaxs9 );
	ClientLeafSystem()->RenderableChanged( p->RenderHandle() );
}

void SourceSharp_BeamRenderableRemove( int32_t nRenderable )
{
	CBeamRenderable *p = RenderableAt( nRenderable );
	if ( !p )
		return;
	ClientLeafSystem()->RemoveRenderable( p->RenderHandle() );
	// RemoveRenderable does not reset it when the index is already gone (level
	// shutdown), and a pooled beam must not keep it.
	p->RenderHandle() = INVALID_CLIENT_RENDER_HANDLE;
	p->m_bLive = false;
	g_FreeBeamRenderables.AddToTail( nRenderable );
}

static IMaterial *g_pBeamWireframeMaterial;

static CEngineSprite *SpriteFor( int32_t nModelIndex )
{
	const model_t *pModel = modelinfo->GetModel( nModelIndex );
	return pModel ? (CEngineSprite *)modelinfo->GetModelExtraData( pModel ) : NULL;
}

// Binds a sprite model's material for a frame and render mode
// (Draw_SetSpriteTexture, with the wireframe choice made by the caller).
CEngineSprite *SourceSharp_BindSpriteModel( const model_t *pModel, int nFrame, int nRenderMode, bool bWireframe )
{
	CEngineSprite *pSprite = pModel ? (CEngineSprite *)modelinfo->GetModelExtraData( pModel ) : NULL;
	if ( !pSprite )
		return NULL;

	IMaterial *pMaterial = pSprite->GetMaterial( (RenderMode_t)nRenderMode, nFrame );
	if ( !pMaterial )
		return NULL;

	CMatRenderContextPtr pRenderContext( materials );
	if ( bWireframe )
	{
		if ( !g_pBeamWireframeMaterial )
			g_pBeamWireframeMaterial = materials->FindMaterial( "shadertest/wireframevertexcolor", TEXTURE_GROUP_OTHER );
		pRenderContext->Bind( g_pBeamWireframeMaterial, NULL );
		return pSprite;
	}

	pRenderContext->Bind( pMaterial );
	return pSprite;
}

int32_t SourceSharp_BeamBindSprite( int32_t nModelIndex, int32_t nFrame, int32_t nRenderMode, int32_t bWireframe )
{
	return SourceSharp_BindSpriteModel( modelinfo->GetModel( nModelIndex ), nFrame, nRenderMode, bWireframe != 0 ) ? 1 : 0;
}

void SourceSharp_BeamSetHdrColorScale( int32_t nModelIndex, int32_t nRenderMode, float flScale )
{
	static unsigned int s_nHdrColorScaleCache = 0;

	CEngineSprite *pSprite = SpriteFor( nModelIndex );
	IMaterial *pMaterial = pSprite ? pSprite->GetMaterial( (RenderMode_t)nRenderMode ) : NULL;
	if ( !pMaterial )
		return;

	if ( IMaterialVar *pVar = pMaterial->FindVarFast( "$hdrcolorscale", &s_nHdrColorScaleCache ) )
		pVar->SetFloatValue( flScale );
}

// A halo sprite's glow material: whether it has one, or bind it as it is.
int32_t SourceSharp_BeamGlow( int32_t nModelIndex, int32_t bBind )
{
	CEngineSprite *pSprite = SpriteFor( nModelIndex );
	IMaterial *pMaterial = pSprite ? pSprite->GetMaterial( kRenderGlow ) : NULL;
	if ( !pMaterial )
		return 0;

	if ( bBind )
	{
		CMatRenderContextPtr pRenderContext( materials );
		pRenderContext->Bind( pMaterial );
	}
	return 1;
}

// One strip over the bound material, two vertices a segment (beam_strip_vertex.h).
void SourceSharp_BeamDrawStrip( const float *pVerts, int32_t nVerts )
{
	if ( !pVerts || nVerts < 4 || ( nVerts & 1 ) )
		return;

	CMatRenderContextPtr pRenderContext( materials );
	ss::beamstrip::DrawStrip( pRenderContext->GetDynamicMesh(), pVerts, nVerts );
}

// One vertex as BeamVertex lays it out: position, r g b a, u v.
enum { BEAM_VERTEX_FLOATS = 9 };

enum BeamColorKind_t
{
	BEAM_COLOR_FLOAT = 0,	// Color3fv
	BEAM_COLOR_RGB,			// Color3ubv
	BEAM_COLOR_RGBA,		// Color4ubv
};

void SourceSharp_BeamDrawMesh( int32_t nPrimitive, int32_t nPrimitives, int32_t nColorKind, const float *pVerts,
                               int32_t nVerts )
{
	if ( !pVerts || nVerts <= 0 || nPrimitives <= 0 )
		return;

	CMatRenderContextPtr pRenderContext( materials );
	IMesh *pMesh = pRenderContext->GetDynamicMesh();

	CMeshBuilder meshBuilder;
	meshBuilder.Begin( pMesh, (MaterialPrimitiveType_t)nPrimitive, nPrimitives );

	for ( int i = 0; i < nVerts; i++ )
	{
		const float *v = pVerts + i * BEAM_VERTEX_FLOATS;

		switch ( nColorKind )
		{
		case BEAM_COLOR_RGB:
		{
			const unsigned char c[3] = { (unsigned char)v[3], (unsigned char)v[4], (unsigned char)v[5] };
			meshBuilder.Color3ubv( c );
			break;
		}
		case BEAM_COLOR_RGBA:
		{
			const unsigned char c[4] = { (unsigned char)v[3], (unsigned char)v[4], (unsigned char)v[5],
			                             (unsigned char)v[6] };
			meshBuilder.Color4ubv( c );
			break;
		}
		default:
			meshBuilder.Color3fv( v + 3 );
			break;
		}

		meshBuilder.TexCoord2f( 0, v[7], v[8] );
		meshBuilder.Position3fv( v );
		meshBuilder.AdvanceVertex();
	}

	meshBuilder.End();
	pMesh->Draw();
}
