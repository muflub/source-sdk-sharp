//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
// The quad building is stock's (c_func_breakablesurf.cpp's DrawOneBlock,
// DrawOneEdge and DrawOneHighlight); what is drawn is managed code's plan.

#include <stdio.h>
#include <stdlib.h>
#include "tier0/platform.h"
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
#include "engine/ivmodelinfo.h"
#include "materialsystem/imesh.h"
#include "engine/ishadowmgr.h"
#include "materialsystem/MaterialSystemUtil.h"
#include "materialsystem/imaterial.h"
#include "materialsystem/imaterialsystem.h"
#include "materialsystem/imaterialvar.h"
#include "materialsystem/itexture.h"
#include "mathlib/mathlib.h"
#include "mathlib/vector.h"
#include "mathlib/vector2d.h"
#include "tier0/basetypes.h"
#include "tier0/dbg.h"
#include "tier1/refcount.h"
#include "tier1/strtools.h"
#include "tier1/utlvector.h"
#include "cdll_client_int.h"
#include "collisionproperty.h"
#include "icliententitylist.h"
#include "ivrenderview.h"
#include "texture_group_names.h"
#include "shell_surface_render.h"
#include "shell_standalone_client.h"
#include "shell_diagnostics.h"

// The base texture of a material, which is what the detail swap replaces.
static ITexture *SurfaceBaseTexture( IMaterial *pMaterial )
{
	if ( !pMaterial )
		return NULL;

	bool bFound;
	IMaterialVar *pVar = pMaterial->FindVar( "$basetexture", &bFound, false );

	return bFound ? pVar->GetTextureValue() : NULL;
}

CSourceSharpSurfaceRenderer::CSourceSharpSurfaceRenderer()
	: m_vNormal( 0, 0, 0 ),
	  m_vCorner( 0, 0, 0 ),
	  m_flPanelWidth( 0.0f ),
	  m_flPanelHeight( 0.0f ),
	  m_pDetailVar( NULL ),
	  m_bMaterialsReady( false ),
	  m_bDrawReported( false )
{
	SOURCESHARP_TRACE();
}

CSourceSharpSurfaceRenderer::~CSourceSharpSurfaceRenderer()
{
	SOURCESHARP_TRACE();
}

// The materials managed code chose, each with its base texture for the swap.
// The detail var is held only when managed code says the pieces' material takes
// the swap: writing $basetexture on any other material would repaint the window
// with a crack sprite.
void CSourceSharpSurfaceRenderer::SetMaterials( const char *const *ppNames, int nFlags )
{
	if ( !ppNames )
		return;

	m_bMaterialsReady = true;

	m_pCrackedMaterial.Shutdown();
	m_pCrackedMaterial.Init( ppNames[0], ( nFlags & SURFACE_MATERIALS_DEBUG ) ? TEXTURE_GROUP_OTHER
	                                                                          : TEXTURE_GROUP_CLIENT_EFFECTS );

	m_pDetailVar = NULL;

	if ( nFlags & SURFACE_MATERIALS_SWAPS_DETAIL )
	{
		bool bFoundBase;
		IMaterialVar *pVar = m_pCrackedMaterial->FindVar( "$basetexture", &bFoundBase, false );

		if ( bFoundBase )
			m_pDetailVar = pVar;
	}

	m_pMaterialBox.Init( ppNames[1], TEXTURE_GROUP_MODEL );
	m_pMaterialBoxTexture.Init( SurfaceBaseTexture( m_pMaterialBox ) );

	for ( int nEdge = 0; nEdge < SOURCESHARP_SURFACE_EDGE_TYPES; nEdge++ )
	{
		for ( int nStyle = 0; nStyle < SOURCESHARP_SURFACE_EDGE_STYLES; nStyle++ )
		{
			SurfaceEdgeTexture_t &edge = m_pEdge[nEdge][nStyle];
			edge.m_pMaterialEdge.Init( ppNames[2 + nEdge * SOURCESHARP_SURFACE_EDGE_STYLES + nStyle],
			                           TEXTURE_GROUP_CLIENT_EFFECTS );
			edge.m_pMaterialEdgeTexture.Init( SurfaceBaseTexture( edge.m_pMaterialEdge ) );
		}
	}
}

// Kept until the next plan. A short or partial tail is dropped, not read.
void CSourceSharpSurfaceRenderer::SetPlan( const int32_t *pWords, int nWords )
{
	m_Plan.RemoveAll();

	if ( !pWords || nWords < SURFACE_PLAN_HEADER_WORDS )
		return;

	float flHeader[ SURFACE_PLAN_HEADER_WORDS ];
	V_memcpy( flHeader, pWords, sizeof( flHeader ) );

	m_vNormal.Init( flHeader[0], flHeader[1], flHeader[2] );
	m_vCorner.Init( flHeader[3], flHeader[4], flHeader[5] );
	m_flPanelWidth  = flHeader[6];
	m_flPanelHeight = flHeader[7];

	const int nEntries = ( nWords - SURFACE_PLAN_HEADER_WORDS ) / SURFACE_PLAN_ENTRY_WORDS;
	m_Plan.CopyArray( pWords + SURFACE_PLAN_HEADER_WORDS, nEntries * SURFACE_PLAN_ENTRY_WORDS );
}

void CSourceSharpSurfaceRenderer::SetDetailTexture( ITexture *pTexture )
{
	if ( m_pDetailVar && pTexture )
		m_pDetailVar->SetTextureValue( pTexture );
}

// The pane grid's two directions from the normal, through the engine's mathlib,
// as the server computes them to decide which pane a shot broke.
void CSourceSharpSurfaceRenderer::Steps( Vector *pWidthStep, Vector *pHeightStep ) const
{
	QAngle vAngles;
	VectorAngles( -1 * m_vNormal, vAngles );
	AngleVectors( vAngles, NULL, pWidthStep, pHeightStep );

	*pWidthStep  *= m_flPanelWidth;
	*pHeightStep *= m_flPanelHeight;
}

void CSourceSharpSurfaceRenderer::BeginDraw()
{
	if ( m_Plan.Count() == 0 || !m_bMaterialsReady )
		return;

	// Said once per surface: nothing outside the game shows whether it is drawn.
	if ( !m_bDrawReported )
	{
		m_bDrawReported = true;
		Msg( "[SourceSharp] surface renderer: drawing %d plan entries of %.1fx%.1f panes, "
		     "corner (%.0f %.0f %.0f)\n",
			m_Plan.Count() / SURFACE_PLAN_ENTRY_WORDS, m_flPanelWidth, m_flPanelHeight,
			m_vCorner.x, m_vCorner.y, m_vCorner.z );
	}

	render->InstallBrushSurfaceRenderer( this );
}

void CSourceSharpSurfaceRenderer::EndDraw()
{
	render->InstallBrushSurfaceRenderer( NULL );
}

// Carries out the plan in order. A new mesh is begun whenever the material or
// swapped texture changes. An entry whose edge, style or side is out of range is
// skipped: the words come across the ABI.
bool CSourceSharpSurfaceRenderer::RenderBrushModelSurface( IClientEntity *pEntity,
                                                           IBrushSurface *pBrushSurface )
{
	NOTE_UNUSED( pEntity );

	// Corner texture coordinates: the solid block's, then by side then by vertex
	// for an edge and a highlight (stock's tables).
	static const float s_Block[4][2] = { { 0, 1 }, { 0, 0 }, { 1, 0 }, { 1, 1 } };
	static const float s_Edge[4][4][2] =
	{
		/* BOTTOM */ { { 1, 1 }, { 0, 1 }, { 0, 0 }, { 1, 0 } },
		/* RIGHT  */ { { 1, 0 }, { 1, 1 }, { 0, 1 }, { 0, 0 } },
		/* TOP    */ { { 0, 0 }, { 1, 0 }, { 1, 1 }, { 0, 1 } },
		/* LEFT   */ { { 0, 1 }, { 0, 0 }, { 1, 0 }, { 1, 1 } },
	};
	static const float s_Highlight[4][4][2] =
	{
		/* BOTTOM */ { { 0, 1 }, { 1, 1 }, { 1, 0 }, { 0, 0 } },
		/* RIGHT  */ { { 1, 0 }, { 0, 0 }, { 0, 1 }, { 1, 1 } },
		/* TOP    */ { { 1, 1 }, { 0, 1 }, { 0, 0 }, { 1, 0 } },
		/* LEFT   */ { { 0, 0 }, { 1, 0 }, { 1, 1 }, { 0, 1 } },
	};

	Vector vWidthStep, vHeightStep;
	Steps( &vWidthStep, &vHeightStep );

	CMatRenderContextPtr pRenderContext( materials );
	CMeshBuilder builder;
	IMesh *pMesh = NULL;
	int nBound = -1;

	for ( int i = 0; i + SURFACE_PLAN_ENTRY_WORDS <= m_Plan.Count(); i += SURFACE_PLAN_ENTRY_WORDS )
	{
		const int nKind = m_Plan[i];
		const int w     = m_Plan[i + 1];
		const int h     = m_Plan[i + 2];
		const int nArg  = m_Plan[i + 3];

		if ( nKind == SURFACE_PLAN_SOLID )
		{
			if ( nBound != 0 )
			{
				nBound = 0;
				SetDetailTexture( m_pMaterialBoxTexture );
				// Flushed because the base texture changed under a material the context may
				// already have bound.
				pRenderContext->Flush( false );
				pRenderContext->Bind( m_pCrackedMaterial );
				pMesh = pRenderContext->GetDynamicMesh();
			}

			const Vector vPos = m_vCorner + vWidthStep * w + vHeightStep * h;
			DrawQuad( pBrushSurface, pMesh, &builder, vPos, vWidthStep, vHeightStep * nArg, s_Block, false );
			continue;
		}

		const int nSide  = nArg & 3;
		const int nEdge  = ( nArg >> 2 ) & 3;
		const int nStyle = nArg >> 4;

		if ( nStyle < 0 || nStyle >= SOURCESHARP_SURFACE_EDGE_STYLES )
			continue;

		const int nKey = 1 + nKind * 16 + nEdge * SOURCESHARP_SURFACE_EDGE_STYLES + nStyle;

		if ( nKind == SURFACE_PLAN_EDGE )
		{
			if ( nBound != nKey )
			{
				nBound = nKey;
				SetDetailTexture( m_pEdge[nEdge][nStyle].m_pMaterialEdgeTexture );
				pRenderContext->Flush( false );
				pRenderContext->Bind( m_pCrackedMaterial );
				pMesh = pRenderContext->GetDynamicMesh();
			}

			const Vector vPos = m_vCorner + vWidthStep * w + vHeightStep * h;
			DrawQuad( pBrushSurface, pMesh, &builder, vPos, vWidthStep, vHeightStep, s_Edge[nSide], false );
		}
		else if ( nKind == SURFACE_PLAN_HIGHLIGHT )
		{
			if ( nBound != nKey )
			{
				nBound = nKey;
				IMaterial *pMat = m_pEdge[nEdge][nStyle].m_pMaterialEdge;
				pMesh = pRenderContext->GetDynamicMesh( true, NULL, NULL, pMat );
			}

			// Pulled a little off the surface so it does not z-fight.
			const Vector vPos = m_vCorner + vWidthStep * w + vHeightStep * h + m_vNormal * 0.30f;
			DrawQuad( pBrushSurface, pMesh, &builder, vPos, vWidthStep, vHeightStep, s_Highlight[nSide], true );
		}
	}

	// No decals on broken glass.
	return false;
}

// One quad, with texture and lightmap coordinates from the brush surface so the
// glass is lit like the face it is part of. A highlight is the grout behind a
// fallen tile: flat-coloured, without the surface's coordinates.
void CSourceSharpSurfaceRenderer::DrawQuad( IBrushSurface *pBrushSurface, IMesh *pMesh,
                                            CMeshBuilder *pBuilder, const Vector &vStart,
                                            const Vector &vWStep, const Vector &vHStep,
                                            const float ( *pCorners )[2], bool bGrout )
{
	// Stock's grout colour.
	static const Vector s_Grout( 0.41f, 0.35f, 0.24f );

	pBuilder->Begin( pMesh, MATERIAL_QUADS, 1 );

	Vector vAt = vStart;

	for ( int i = 0; i < 4; i++ )
	{
		Vector2D texCoord, lightCoord;
		pBrushSurface->ComputeTextureCoordinate( vAt, texCoord );
		pBrushSurface->ComputeLightmapCoordinate( vAt, lightCoord );

		pBuilder->Position3f( vAt.x, vAt.y, vAt.z );
		pBuilder->Normal3fv( m_vNormal.Base() );
		if ( bGrout )
			pBuilder->Color4f( s_Grout.x, s_Grout.y, s_Grout.z, 1.0f );
		else
			pBuilder->Color4ub( 255, 255, 255, 255 );
		pBuilder->TexCoord2f( 0, pCorners[i][0], pCorners[i][1] );
		pBuilder->TexCoord2fv( 1, lightCoord.Base() );
		if ( !bGrout )
			pBuilder->TexCoord2fv( 2, texCoord.Base() );
		pBuilder->AdvanceVertex();

		// Round the quad: along, up, back.
		if ( i == 0 )      vAt = vAt + vWStep;
		else if ( i == 1 ) vAt = vAt + vHStep;
		else if ( i == 2 ) vAt = vAt - vWStep;
	}

	pBuilder->End();
	pMesh->Draw();
}

// The brush model's material count and, when it is exactly one, that material's
// name and its $crackmaterial. -1 for a handle that names no shell.
int32_t SourceSharp_SurfaceMaterialFacts( uint32_t hEntity, char *pOutMaterial, int32_t nMaterialBytes,
                                          char *pOutCrack, int32_t nCrackBytes, int32_t *pOutHasCrack )
{
	if ( pOutMaterial && nMaterialBytes > 0 )
		pOutMaterial[0] = '\0';
	if ( pOutCrack && nCrackBytes > 0 )
		pOutCrack[0] = '\0';
	if ( pOutHasCrack )
		*pOutHasCrack = 0;

	C_StandaloneEntity *pEntity = SourceSharp_StandaloneFromHandle( hEntity );

	if ( !pEntity )
		return -1;

	model_t *pModel = const_cast< model_t * >( pEntity->GetModel() );

	if ( !pModel )
		return 0;

	const int nMaterials = modelinfo->GetModelMaterialCount( pModel );

	if ( nMaterials != 1 )
		return nMaterials;

	IMaterial *pMaterial = NULL;
	modelinfo->GetModelMaterials( pModel, 1, &pMaterial );

	if ( !pMaterial )
		return nMaterials;

	if ( pOutMaterial && nMaterialBytes > 0 )
		Q_strncpy( pOutMaterial, pMaterial->GetName(), nMaterialBytes );

	bool bFound;
	IMaterialVar *pCrackName = pMaterial->FindVar( "$crackmaterial", &bFound, false );

	if ( bFound && pCrackName )
	{
		if ( pOutHasCrack )
			*pOutHasCrack = 1;
		if ( pOutCrack && nCrackBytes > 0 )
			Q_strncpy( pOutCrack, pCrackName->GetStringValue(), nCrackBytes );
	}

	return nMaterials;
}

// The materials managed code chose, NUL-separated in SurfaceSetMaterials' order;
// the renderer is made on first ask. A block missing a name is refused.
void SourceSharp_SurfaceSetMaterials( uint32_t hEntity, const char *pNames, int32_t nBytes, int32_t nFlags )
{
	C_StandaloneEntity *pEntity = SourceSharp_StandaloneFromHandle( hEntity );

	if ( !pEntity || !pNames || nBytes <= 0 )
		return;

	const char *ppNames[ SOURCESHARP_SURFACE_MATERIAL_NAMES ];
	int nAt = 0;

	for ( int i = 0; i < SOURCESHARP_SURFACE_MATERIAL_NAMES; i++ )
	{
		const void *pNul = ( nAt < nBytes ) ? memchr( pNames + nAt, 0, nBytes - nAt ) : NULL;

		if ( !pNul )
		{
			Warning( "[SourceSharp] SurfaceSetMaterials: %d of %d names; ignored\n", i,
			         (int)SOURCESHARP_SURFACE_MATERIAL_NAMES );
			return;
		}

		ppNames[i] = pNames + nAt;
		nAt = (int)( (const char *)pNul - pNames ) + 1;
	}

	const bool bNew = pEntity->SurfaceRenderer( false ) == NULL;
	pEntity->SurfaceRenderer( true )->SetMaterials( ppNames, nFlags );

	// Said once per surface: nothing outside the game shows whether it attached.
	if ( bNew )
		Msg( "[SourceSharp] standalone(client): entity %d has a surface renderer\n", pEntity->entindex() );
}

// Hands the shell's surface renderer a plan, making the renderer if needed.
void SourceSharp_SurfaceSetPlan( uint32_t hEntity, const int32_t *pWords, int32_t nWords )
{
	C_StandaloneEntity *pEntity = SourceSharp_StandaloneFromHandle( hEntity );

	if ( !pEntity )
		return;

	if ( nWords <= 0 && !pEntity->SurfaceRenderer( false ) )
		return;

	pEntity->SurfaceRenderer( true )->SetPlan( pWords, nWords );
}
