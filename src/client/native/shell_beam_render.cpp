//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the beam executor ==================================//
// SS_REASON g  leaf math over mdl/bsp data
//
// plan_client_novalve.md §2 cluster 5. Everything a beam DECIDES is in C#
// now (src/client/managed/Beams): creation by kind, lifetimes, noise, entity
// endpoints, ring growth, follow trails, the reaping of a dead entity's beams,
// and every vertex of every strip, quad and ring. What is left here is what
// managed code cannot hold:
//
//   * `beams`, the IViewRenderBeams the engine-facing code still names
//     (view.cpp's init and level clear, fx.cpp's two BeamInfo_t creators,
//     the frame's UpdateTempEntBeams, a shell's KillDeadBeams). Each call is
//     forwarded to IManagedBeamsClient; nothing is answered here.
//   * the renderables. A beam the leaf system sorts is a pointer it holds, so
//     the pool of CBeamRenderable lives here; managed code adds, moves and
//     removes them by a small integer, and their DrawModel calls back into
//     managed code with that integer.
//   * the draw. Binding a sprite's material (Draw_SetSpriteTexture), setting
//     its $hdrcolorscale, a strip of the camera-facing vertices managed
//     code's BeamSegDraw made (beam_strip_vertex.h) and a dynamic mesh over a
//     vertex list: the material system's calls, in the order the managed
//     builder makes them.
//   * the reads: the view, model frame counts and indices, the facts an
//     entity endpoint is decided from (which hierarchy, an attachment, the
//     collision centre, the render origin), the PVS/frustum box tests, the
//     halo's sight test and the overlay's screen position. The endpoint's
//     route (shell_beam_endpoint_client.cpp's, behaviour ledger row C8) is
//     C#'s now: BeamEndpoint in ClientBeamSystem.cs.
//
// The stock CViewRenderBeams and Beam_t left with this file; view_beams.cpp
// goes from the build in the same commit (the `beams` symbol is this file's).
//=============================================================================//

#include "cbase.h"
#include "iviewrender_beams.h"
#include "enginesprite.h"
#include "beam_strip_vertex.h"
#include "materialsystem/imaterialsystem.h"
#include "materialsystem/imaterial.h"
#include "materialsystem/imaterialvar.h"
#include "materialsystem/imesh.h"
#include "engine/ivdebugoverlay.h"
#include "clientleafsystem.h"
#include "view.h"
#include "tier0/icommandline.h"
#include "shell_cliententitylist.h"
#include "slot_table.h"
// c_env_fog_controller.h before the player shell: C_StandalonePlayer's
// CPlayerLocalData sets a CNetworkHandle< C_FogController >, which needs the
// class complete (the reason shell_beam_endpoint_client.cpp gave for it).
#include "c_env_fog_controller.h"
#include "shell_standalone_client.h"
#include "engine/ICollideable.h"
#include "collideable_center.h"
#include "imanaged_areas_client.h"
#include "shell_beam_render.h"
#include "ss_command_line.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

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

//-----------------------------------------------------------------------------
// One leaf-system renderable per managed beam that asked to be drawn. Its
// bounds and origin are managed code's (Beam.ComputeBounds, RenderOrigin);
// the rest is stock Beam_t's: translucent, always drawn, full blend.
//-----------------------------------------------------------------------------
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

//-----------------------------------------------------------------------------
// The payloads BeamsCreate carries (ClientBeamSystem.cs reads them in this
// order). BeamInfo_t's fields, with its two IClientEntity pointers as raw
// handles.
//-----------------------------------------------------------------------------
enum BeamCreateKind_t
{
	BEAM_CREATE_ENTS = 0,
	BEAM_CREATE_ENTPOINT,
	BEAM_CREATE_POINTS,
	BEAM_CREATE_RING,
	BEAM_CREATE_RINGPOINT,
	BEAM_CREATE_CIRCLEPOINTS,
	BEAM_CREATE_FOLLOW,
};

enum BeamLifecycle_t
{
	BEAM_LIFECYCLE_INIT = 0,
	BEAM_LIFECYCLE_SHUTDOWN,
	BEAM_LIFECYCLE_CLEAR,
};

void ForwardBeamInfo( BeamCreateKind_t eKind, const BeamInfo_t &info )
{
	IManagedBeamsClient *pBeams = ManagedBeamsClient();
	if ( !pBeams )
		return;

	const int32_t ints[11] =
	{
		info.m_nType,
		(int32_t)RawFromEntity( info.m_pStartEnt ),
		info.m_nStartAttachment,
		(int32_t)RawFromEntity( info.m_pEndEnt ),
		info.m_nEndAttachment,
		info.m_nModelIndex,
		info.m_nHaloIndex,
		info.m_nStartFrame,
		info.m_bRenderable ? 1 : 0,
		info.m_nSegments,
		info.m_nFlags,
	};

	const float floats[23] =
	{
		info.m_vecStart.x, info.m_vecStart.y, info.m_vecStart.z,
		info.m_vecEnd.x, info.m_vecEnd.y, info.m_vecEnd.z,
		info.m_vecCenter.x, info.m_vecCenter.y, info.m_vecCenter.z,
		info.m_flHaloScale, info.m_flLife, info.m_flWidth, info.m_flEndWidth,
		info.m_flFadeLength, info.m_flAmplitude, info.m_flBrightness, info.m_flSpeed,
		info.m_flFrameRate, info.m_flRed, info.m_flGreen, info.m_flBlue,
		info.m_flStartRadius, info.m_flEndRadius,
	};

	pBeams->BeamsCreate( eKind, ints, floats, info.m_pszModelName, info.m_pszHaloName, gpGlobals->curtime );
}

//-----------------------------------------------------------------------------
// `beams`: the interface the engine-facing code names, forwarding to C#.
//
// The returned Beam_t pointers are NULL: managed code holds the beam, and the
// only callers that read one (C_Beam and its kin) are not in this build.
// The flat creators had one caller, the temp entity backends, which now call
// the managed beam system directly; they are absences (docs/shim-reasons.md).
//-----------------------------------------------------------------------------
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

	void KillDeadBeams( IGameEntity *pDead ) override {}

	void KillDeadBeams( const CBaseHandle &hDeadEntity ) override
	{
		if ( IManagedBeamsClient *pBeams = ManagedBeamsClient() )
			pBeams->BeamsKillDead( (uint32_t)hDeadEntity.ToInt(), gpGlobals->curtime );
	}

	Beam_t *CreateBeamEnts( BeamInfo_t &info ) override { ForwardBeamInfo( BEAM_CREATE_ENTS, info ); return NULL; }
	Beam_t *CreateBeamEntPoint( BeamInfo_t &info ) override { ForwardBeamInfo( BEAM_CREATE_ENTPOINT, info ); return NULL; }
	Beam_t *CreateBeamPoints( BeamInfo_t &info ) override { ForwardBeamInfo( BEAM_CREATE_POINTS, info ); return NULL; }
	Beam_t *CreateBeamRing( BeamInfo_t &info ) override { ForwardBeamInfo( BEAM_CREATE_RING, info ); return NULL; }
	Beam_t *CreateBeamRingPoint( BeamInfo_t &info ) override { ForwardBeamInfo( BEAM_CREATE_RINGPOINT, info ); return NULL; }
	Beam_t *CreateBeamCirclePoints( BeamInfo_t &info ) override { ForwardBeamInfo( BEAM_CREATE_CIRCLEPOINTS, info ); return NULL; }
	Beam_t *CreateBeamFollow( BeamInfo_t &info ) override { ForwardBeamInfo( BEAM_CREATE_FOLLOW, info ); return NULL; }

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

IViewRenderBeams *beams = &g_SourceSharpBeams;

//=============================================================================
// The reads.
//=============================================================================

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

// What a beam endpoint's position is decided from (ClientBeamSystem.cs's
// BeamEndpoint): which hierarchy holds the handle, whether it renders and is a
// player, and the three candidate points -- the attachment (asked only when
// managed code says to), the collision centre and the render origin.
enum BeamEntityFact_t
{
	BEAM_FACT_RESOLVED     = 0x01,
	BEAM_FACT_SDK          = 0x02,
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
	if ( SDKEntityFromUnknown( pUnknown ) )
		return nFacts | BEAM_FACT_SDK;

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

//=============================================================================
// The renderables.
//=============================================================================

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
	// Not reset by it when the index is already gone (level shutdown): a pooled beam must not keep it (2026-10-12).
	p->RenderHandle() = INVALID_CLIENT_RENDER_HANDLE;
	p->m_bLive = false;
	g_FreeBeamRenderables.AddToTail( nRenderable );
}

//=============================================================================
// The draw.
//=============================================================================

static IMaterial *g_pBeamWireframeMaterial;

static CEngineSprite *SpriteFor( int32_t nModelIndex )
{
	const model_t *pModel = modelinfo->GetModel( nModelIndex );
	return pModel ? (CEngineSprite *)modelinfo->GetModelExtraData( pModel ) : NULL;
}

// Draw_SetSpriteTexture (beamdraw.cpp, gone), with the wireframe choice made
// by the caller: bind a sprite model's material for a frame and render mode.
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

// One strip over the bound material, as the vertices managed code's
// BeamSegDraw made of it (beam_strip_vertex.h's layout, two a segment).
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
