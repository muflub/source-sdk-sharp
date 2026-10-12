//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — drawing a shattered pane of glass ==================//
// SS_REASON a  a vtable the engine calls through
//
// See the header. The mesh building below is stock's, nearly line for line: how
// glass is drawn was never the part worth changing, and a rewrite would have
// replaced working code with equivalent code and a new set of mistakes.
//
// What IS different is where the state comes from. Stock reads recv'd members
// on a C_BaseEntity; this reads the entity's data blob at offsets C# computed,
// resolved by NAME from the table C# described. There is no second copy of the
// layout anywhere in this file.
//
//=============================================================================//

#include "cbase.h"
#include "shell_surface_render.h"
#include "shell_standalone_client.h"
#include "shell_diagnostics.h"
#include "engine/ivmodelinfo.h"
#include "materialsystem/imesh.h"
#include "model_types.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

// Must match ShatterSurface_t on the server side of the SDK.
enum
{
	SURFACE_GLASS = 0,
	SURFACE_TILE  = 1,
};

//-----------------------------------------------------------------------------
// The base texture of a material, which is what the detail swap replaces.
//-----------------------------------------------------------------------------
static ITexture *SurfaceBaseTexture( IMaterial *pMaterial )
{
	if ( !pMaterial )
		return NULL;

	bool bFound;
	IMaterialVar *pVar = pMaterial->FindVar( "$basetexture", &bFound, false );

	return bFound ? pVar->GetTextureValue() : NULL;
}

CSourceSharpSurfaceRenderer::CSourceSharpSurfaceRenderer()
	: m_nNumWide( SOURCESHARP_SURFACE_MAX_PANELS ),
	  m_nNumHigh( SOURCESHARP_SURFACE_MAX_PANELS ),
	  m_flPanelWidth( 0.0f ),
	  m_flPanelHeight( 0.0f ),
	  m_vNormal( 0, 0, 0 ),
	  m_vCorner( 0, 0, 0 ),
	  m_bIsBroken( false ),
	  m_nSurfaceType( SURFACE_GLASS ),
	  m_pDetailVar( NULL ),
	  m_bMaterialsReady( false ),
	  m_bHasCrackedMaterial( false ),
	  m_bDrawReported( false ),
	  m_nResolved( RESOLVE_PENDING )
{
	SOURCESHARP_TRACE();

	// Every pane present, which is what an unbroken sheet is.
	for ( int w = 0; w < SOURCESHARP_SURFACE_MAX_PANELS; w++ )
		for ( int h = 0; h < SOURCESHARP_SURFACE_MAX_PANELS; h++ )
			m_bPaneSolid[w][h] = true;
}

CSourceSharpSurfaceRenderer::~CSourceSharpSurfaceRenderer()
{
	SOURCESHARP_TRACE();
}

//=============================================================================//
// Which panes are still there.
//
// All that is left of what this class used to work out. The edges, the crack
// styles and the staleness that drove recomputing them are decided in C# and
// arrive through SetEdges — none of it ever touched a renderer.
//=============================================================================//

bool CSourceSharpSurfaceRenderer::InLegalRange( int w, int h ) const
{
	return w >= 0 && h >= 0 && w < m_nNumWide && h < m_nNumHigh;
}

bool CSourceSharpSurfaceRenderer::IsPanelSolid( int w, int h ) const
{
	return InLegalRange( w, h ) && m_bPaneSolid[w][h];
}

void CSourceSharpSurfaceRenderer::SetPanelSolid( int w, int h, bool bValue )
{
	if ( w < 0 || h < 0 || w >= SOURCESHARP_SURFACE_MAX_PANELS
	           || h >= SOURCESHARP_SURFACE_MAX_PANELS )
		return;

	m_bPaneSolid[w][h] = bValue;
}

//-----------------------------------------------------------------------------
// The edges C# decided. Four bytes each; see SurfaceSetEdges in sourcesharp.h.
//
// Replaced wholesale rather than merged: the list is small, it is rebuilt on the
// far side whenever the panes change, and a merge would be two descriptions of
// the same thing that could disagree.
//-----------------------------------------------------------------------------
void CSourceSharpSurfaceRenderer::SetEdges( const unsigned char *pEdges, int nCount )
{
	m_RenderList.RemoveAll();

	if ( !pEdges || nCount <= 0 )
		return;

	m_RenderList.EnsureCapacity( nCount );

	for ( int i = 0; i < nCount; i++ )
	{
		const unsigned char *p = pEdges + i * 4;

		SurfacePanel_t panel;
		panel.m_nWidth    = (char)p[0];
		panel.m_nHeight   = (char)p[1];
		panel.m_nSide     = (char)p[2];
		panel.m_nEdgeType = (char)( p[3] >> 2 );
		panel.m_nStyle    = (char)( p[3] & 0x03 );

		m_RenderList.AddToTail( panel );
	}
}

//=============================================================================//
// Reading the replicated state.
//=============================================================================//

//-----------------------------------------------------------------------------
// A packed field's whole placement, by name: the word it lives in and the bits
// of it that are the field's.
//
// TWO CALLS, ONCE. C# is asked and nothing here derives anything, which is the
// same rule the offset alone has followed since Step 7 and the whole reason a
// native renderer can read C#-declared state without a second copy of the
// layout. A placement that does not fit a word comes back unresolved rather
// than clamped — the caller draws nothing, which is visible, instead of reading
// a plausible number out of the field beside it, which is not.
//-----------------------------------------------------------------------------
SourceSharpPackedField_t CSourceSharpSurfaceRenderer::ResolvePacked( const char *pClass,
                                                                     const char *pField )
{
	return SourceSharpPayload_Place( SourceSharp_StandaloneFieldOffset( pClass, pField ),
	                                 SourceSharp_StandaloneFieldBits( pClass, pField ) );
}

//-----------------------------------------------------------------------------
// Where a described field lives, by name.
//
// The RecvTable the client built from C#'s description carries both, so this
// asks it rather than writing the offsets down again — which is the whole
// reason a native renderer can read C#-declared state at all without the two
// drifting.
//-----------------------------------------------------------------------------
void CSourceSharpSurfaceRenderer::Update( C_StandaloneEntity *pEntity, bool bCreated )
{
	if ( !pEntity )
		return;

	// Resolved ONCE, and once whether it worked or not. A surface whose fields
	// cannot be found is not going to find them on the next update either, and
	// this runs per entity per change — a failed lookup that retried would walk
	// the prop list and print the same warning sixty times a second.
	if ( m_nResolved == RESOLVE_FAILED )
		return;

	if ( m_nResolved == RESOLVE_PENDING )
	{
		const char *pClass = SOURCESHARP_SURFACE_CLASSNAME;

		// The four that PACK. Asking for the offset alone would answer the
		// word all four share, and reading a byte there returns Wide whichever
		// of them was asked for -- see SourceSharpPayload_Place.
		m_fldWide        = ResolvePacked( pClass, "Wide" );
		m_fldHigh        = ResolvePacked( pClass, "High" );
		m_fldBroken      = ResolvePacked( pClass, "Broken" );
		m_fldSurfaceType = ResolvePacked( pClass, "SurfaceType" );

		// And the ones that take their storage whole. A Vector and a float are
		// read as themselves; nothing about packing touches them, and giving
		// them a shift and a mask they never use would be ceremony.
		m_nOffPanelWidth  = SourceSharp_StandaloneFieldOffset( pClass, "PanelWidth" );
		m_nOffPanelHeight = SourceSharp_StandaloneFieldOffset( pClass, "PanelHeight" );
		m_nOffNormal      = SourceSharp_StandaloneFieldOffset( pClass, "Normal" );
		m_nOffCorner      = SourceSharp_StandaloneFieldOffset( pClass, "Corner" );

		for ( int i = 0; i < 8; i++ )
		{
			char szName[ 32 ];
			Q_snprintf( szName, sizeof( szName ), "PaneBits.%d", i );
			m_nOffPaneBits[i] = SourceSharp_StandaloneFieldOffset( pClass, szName );
		}

		if ( !m_fldWide.IsResolved() || !m_fldBroken.IsResolved()
		     || m_nOffCorner < 0 || m_nOffPaneBits[7] < 0 )
		{
			// Said once. A surface whose fields cannot be found draws as an
			// ordinary brush, which is wrong and visible, rather than reading
			// whatever happens to be at offset zero.
			Warning( "[SourceSharp] surface renderer: '%s' has no such described fields; "
			         "the glass will not crack\n", pClass );

			m_nResolved = RESOLVE_FAILED;
			return;
		}

		m_nResolved = RESOLVE_DONE;
	}

	const unsigned char *pBase = (const unsigned char *)pEntity;

	// CLAMPED, because these two are read off the wire and then used as LOOP
	// BOUNDS, and the loop below indexes a fixed 8-word array with
	// (w + h*MAX_PANELS) >> 5. A grid of 16x16 puts that index at 7, which is
	// the last word; a High of 254 puts it at 134, half a kilobyte past the end
	// of a stack buffer.
	//
	// A correct layout can never produce more than MAX_PANELS: the managed
	// declaration bounds the grid and the server clamps it at spawn. This is the
	// belt to the schema gate's braces (C_StandaloneEntity::OnDataChanged) —
	// that stops a KNOWN-wrong layout reaching here at all, and this stops an
	// unknown-wrong one from being a memory bug rather than a drawing bug.
	//
	// Clamping rather than refusing, because a surface whose grid does not fit
	// still has a real grid inside the part that does, and drawing the first
	// 16x16 of it is closer to right than drawing nothing.
	m_nNumWide      = MIN( (int)SourceSharpPayload_ReadPacked( pBase, m_fldWide ),
	                       SOURCESHARP_SURFACE_MAX_PANELS );
	m_nNumHigh      = MIN( (int)SourceSharpPayload_ReadPacked( pBase, m_fldHigh ),
	                       SOURCESHARP_SURFACE_MAX_PANELS );
	m_flPanelWidth  = *(const float *)( pBase + m_nOffPanelWidth );
	m_flPanelHeight = *(const float *)( pBase + m_nOffPanelHeight );
	m_vNormal       = *(const Vector *)( pBase + m_nOffNormal );
	m_vCorner       = *(const Vector *)( pBase + m_nOffCorner );
	m_bIsBroken     = SourceSharpPayload_ReadPacked( pBase, m_fldBroken ) != 0;
	m_nSurfaceType  = (int)SourceSharpPayload_ReadPacked( pBase, m_fldSurfaceType );

	if ( bCreated )
		m_bMaterialsReady = false;

	// Nothing to draw until the sheet breaks, and nothing to read either: the
	// grid does not exist before then.
	if ( !m_bIsBroken )
		return;

	if ( !m_bMaterialsReady )
		InitMaterials( pEntity->GetModel() );

	// Which panes are left, out of the eight words.
	//
	// The clamp on m_nNumWide/m_nNumHigh above is what makes this indexable
	// without a bounds check, and this says so in a form the compiler enforces:
	// the largest bit the loop can reach is (MAX-1) + (MAX-1)*MAX, and it has to
	// land inside these eight words.
	COMPILE_TIME_ASSERT( ( ( SOURCESHARP_SURFACE_MAX_PANELS - 1 )
	                     + ( SOURCESHARP_SURFACE_MAX_PANELS - 1 )
	                     * SOURCESHARP_SURFACE_MAX_PANELS ) >> 5 < 8 );

	uint32 nWords[8];
	for ( int i = 0; i < 8; i++ )
		nWords[i] = *(const uint32 *)( pBase + m_nOffPaneBits[i] );

	// The stride is the FULL grid, not this surface's width — see the note on
	// BreakableSurfaceNetState.Index for why that is deliberate.
	//
	// Only which panes are LEFT is read here. Which of their edges are cracked
	// is decided from the same bits on the managed side and arrives through
	// SetEdges, so there is one answer rather than two computed from one input.
	for ( int h = 0; h < m_nNumHigh; h++ )
	{
		for ( int w = 0; w < m_nNumWide; w++ )
		{
			const int nBit = w + h * SOURCESHARP_SURFACE_MAX_PANELS;
			SetPanelSolid( w, h, ( nWords[ nBit >> 5 ] & ( 1u << ( nBit & 31 ) ) ) != 0 );
		}
	}
}

//=============================================================================//
// Materials.
//=============================================================================//

//-----------------------------------------------------------------------------
// The cracked version of whatever the brush is skinned with, plus the twelve
// edge materials.
//
// The material names are stock's and the server precaches the same list, which
// is the only reason they can be named here at all.
//-----------------------------------------------------------------------------
void CSourceSharpSurfaceRenderer::InitMaterials( const model_t *pModel )
{
	m_bMaterialsReady = true;
	m_pCrackedMaterial.Shutdown();

	IMaterial *pMaterial = NULL;

	const int nMaterials = pModel
		? modelinfo->GetModelMaterialCount( const_cast< model_t * >( pModel ) )
		: 0;

	if ( nMaterials != 1 )
	{
		// Stock warns and draws nothing useful. Same here, and it is a content
		// error rather than a code one: a breakable surface is one face.
		Warning( "[SourceSharp] func_breakable_surf has %d materials, not 1; "
		         "the cracks will not draw\n", nMaterials );
		m_pCrackedMaterial.Init( "debug/debugempty", TEXTURE_GROUP_OTHER );
	}
	else
	{
		modelinfo->GetModelMaterials( const_cast< model_t * >( pModel ), 1, &pMaterial );

		bool bFound;
		IMaterialVar *pCrackName = pMaterial->FindVar( "$crackmaterial", &bFound, false );

		if ( bFound )
		{
			m_pCrackedMaterial.Init( pCrackName->GetStringValue(), TEXTURE_GROUP_CLIENT_EFFECTS );
			m_bHasCrackedMaterial = true;
		}
		else
		{
			// No cracked variant, so the surface's OWN material draws the
			// pieces. Stock lands here too, and the consequence is easy to miss:
			// the detail-texture swap below is driven by a material PROXY, and a
			// material that never asked for one never gets swapped. So the
			// pieces keep the glass's own appearance rather than being painted
			// with a crack sprite.
			m_pCrackedMaterial.Init( pMaterial );
			m_bHasCrackedMaterial = false;
		}
	}

	// The var the detail texture is written into. Held rather than looked up
	// per draw, and written directly rather than through a material proxy:
	// stock's proxy resolves its bind argument to a C_BaseEntity, and this
	// entity is not one, so it would find nothing and silently do nothing.
	//
	// ONLY FOR A REAL CRACKED MATERIAL, and that condition is the whole of
	// whether broken glass looks like glass. Stock's swap happens inside a
	// proxy that a material must ASK for in its .vmt, and only a purpose-made
	// cracked material does. Writing $basetexture unconditionally — which is
	// what doing it by hand invites — repaints the surface's own material with
	// a crack sprite, and the pieces come out the colour of the sprite instead
	// of the colour of the window.
	m_pDetailVar = NULL;

	if ( m_bHasCrackedMaterial )
	{
		bool bFoundBase;
		IMaterialVar *pVar = m_pCrackedMaterial->FindVar( "$basetexture", &bFoundBase, false );

		if ( bFoundBase )
			m_pDetailVar = pVar;
	}

	// Said once per surface. Whether the surface named a cracked variant decides
	// whether ANY crack sprite is drawn — stock swaps the detail texture inside
	// a proxy that only a purpose-made cracked material asks for — so a window
	// that shows clean rectangular holes and no cracks is answered here rather
	// than by staring at it.
	Msg( "[SourceSharp] surface renderer: material '%s', cracked variant %s\n",
		pMaterial ? pMaterial->GetName() : "?",
		m_bHasCrackedMaterial ? m_pCrackedMaterial->GetName() : "NONE" );

	m_pMaterialBox.Init( "models/brokenglass/glassbroken_solid", TEXTURE_GROUP_MODEL );
	m_pMaterialBoxTexture.Init( SurfaceBaseTexture( m_pMaterialBox ) );

	if ( m_nSurfaceType == SURFACE_TILE )
	{
		InitMaterial( SURF_EDGE_NONE,  0, "models/brokentile/tilebroken_03a" );
		InitMaterial( SURF_EDGE_FULL,  0, "models/brokentile/tilebroken_03b" );
		InitMaterial( SURF_EDGE_LEFT,  0, "models/brokentile/tilebroken_03c" );
		InitMaterial( SURF_EDGE_RIGHT, 0, "models/brokentile/tilebroken_03d" );

		InitMaterial( SURF_EDGE_NONE,  1, "models/brokentile/tilebroken_02a" );
		InitMaterial( SURF_EDGE_FULL,  1, "models/brokentile/tilebroken_02b" );
		InitMaterial( SURF_EDGE_LEFT,  1, "models/brokentile/tilebroken_02c" );
		InitMaterial( SURF_EDGE_RIGHT, 1, "models/brokentile/tilebroken_02d" );

		InitMaterial( SURF_EDGE_NONE,  2, "models/brokentile/tilebroken_01a" );
		InitMaterial( SURF_EDGE_FULL,  2, "models/brokentile/tilebroken_01b" );
		InitMaterial( SURF_EDGE_LEFT,  2, "models/brokentile/tilebroken_01c" );
		InitMaterial( SURF_EDGE_RIGHT, 2, "models/brokentile/tilebroken_01d" );
	}
	else
	{
		InitMaterial( SURF_EDGE_NONE,  0, "models/brokenglass/glassbroken_03a" );
		InitMaterial( SURF_EDGE_FULL,  0, "models/brokenglass/glassbroken_03b" );
		InitMaterial( SURF_EDGE_LEFT,  0, "models/brokenglass/glassbroken_03c" );
		InitMaterial( SURF_EDGE_RIGHT, 0, "models/brokenglass/glassbroken_03d" );

		InitMaterial( SURF_EDGE_NONE,  1, "models/brokenglass/glassbroken_02a" );
		InitMaterial( SURF_EDGE_FULL,  1, "models/brokenglass/glassbroken_02b" );
		InitMaterial( SURF_EDGE_LEFT,  1, "models/brokenglass/glassbroken_02c" );
		InitMaterial( SURF_EDGE_RIGHT, 1, "models/brokenglass/glassbroken_02d" );

		InitMaterial( SURF_EDGE_NONE,  2, "models/brokenglass/glassbroken_01a" );
		InitMaterial( SURF_EDGE_FULL,  2, "models/brokenglass/glassbroken_01b" );
		InitMaterial( SURF_EDGE_LEFT,  2, "models/brokenglass/glassbroken_01c" );
		InitMaterial( SURF_EDGE_RIGHT, 2, "models/brokenglass/glassbroken_01d" );
	}
}

void CSourceSharpSurfaceRenderer::InitMaterial( SurfaceWinEdge_t nEdgeType, int nStyle,
                                                const char *pMaterialName )
{
	m_pEdge[nEdgeType][nStyle].m_nRenderIndex = -1;
	m_pEdge[nEdgeType][nStyle].m_nStyle       = nStyle;
	m_pEdge[nEdgeType][nStyle].m_pMaterialEdge.Init( pMaterialName, TEXTURE_GROUP_CLIENT_EFFECTS );
	m_pEdge[nEdgeType][nStyle].m_pMaterialEdgeTexture.Init(
		SurfaceBaseTexture( m_pEdge[nEdgeType][nStyle].m_pMaterialEdge ) );
}

void CSourceSharpSurfaceRenderer::SetDetailTexture( ITexture *pTexture )
{
	if ( m_pDetailVar && pTexture )
		m_pDetailVar->SetTextureValue( pTexture );
}

//=============================================================================//
// Drawing.
//=============================================================================//

//-----------------------------------------------------------------------------
// The two directions a pane grid runs in, from the normal.
//
// The SERVER works these out from the same normal, through the same engine
// functions, to decide which pane a shot broke. Two implementations agreeing to
// the last bit is not something to assume, so both ask the engine.
//-----------------------------------------------------------------------------
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
	if ( !m_bIsBroken )
		return;

	// Said once per surface, because "is this being drawn at all" is the only
	// question that cannot be answered from outside the game. A mesh in the
	// wrong place is visible; one that is never asked for looks exactly like one
	// that is asked for and draws nothing, and the two have nothing in common to
	// fix. The same reason the standalone shell says it once for its models.
	if ( !m_bDrawReported )
	{
		m_bDrawReported = true;
		Msg( "[SourceSharp] surface renderer: drawing %dx%d panes of %.1fx%.1f, "
		     "corner (%.0f %.0f %.0f), %d edge(s) queued\n",
			m_nNumWide, m_nNumHigh, m_flPanelWidth, m_flPanelHeight,
			m_vCorner.x, m_vCorner.y, m_vCorner.z, m_RenderList.Count() );
	}

	render->InstallBrushSurfaceRenderer( this );
}

void CSourceSharpSurfaceRenderer::EndDraw()
{
	render->InstallBrushSurfaceRenderer( NULL );
}

bool CSourceSharpSurfaceRenderer::RenderBrushModelSurface( IClientEntity *pEntity,
                                                           IBrushSurface *pBrushSurface )
{
	if ( m_nSurfaceType == SURFACE_TILE )
		DrawRenderListHighlights( pBrushSurface );

	DrawSolidBlocks( pBrushSurface );
	DrawRenderList( pBrushSurface );

	// No decals on broken glass.
	return false;
}

//-----------------------------------------------------------------------------
// The panes that are still there, in vertical strips.
//
// A strip rather than a quad each: a column of unbroken panes is one mesh, and
// a window that has barely been touched is a handful of draws rather than 256.
//-----------------------------------------------------------------------------
void CSourceSharpSurfaceRenderer::DrawSolidBlocks( IBrushSurface *pBrushSurface )
{
	CMatRenderContextPtr pRenderContext( materials );

	SetDetailTexture( m_pMaterialBoxTexture );

	// Flushed because the base texture just changed underneath a material the
	// context may already have bound.
	pRenderContext->Flush( false );
	pRenderContext->Bind( m_pCrackedMaterial );

	IMesh *pMesh = pRenderContext->GetDynamicMesh();
	CMeshBuilder builder;

	Vector vWidthStep, vHeightStep;
	Steps( &vWidthStep, &vHeightStep );

	for ( int w = 0; w < m_nNumWide; w++ )
	{
		int h;
		int nRun = 0;

		for ( h = 0; h < m_nNumHigh; h++ )
		{
			if ( IsPanelSolid( w, h ) )
			{
				nRun++;
			}
			else if ( nRun > 0 )
			{
				const Vector vPos = m_vCorner + vWidthStep * w + vHeightStep * ( h - nRun );
				DrawOneBlock( pBrushSurface, pMesh, &builder, vPos, vWidthStep, vHeightStep * nRun );
				nRun = 0;
			}
		}

		if ( nRun > 0 )
		{
			const Vector vPos = m_vCorner + vWidthStep * w + vHeightStep * ( h - nRun );
			DrawOneBlock( pBrushSurface, pMesh, &builder, vPos, vWidthStep, vHeightStep * nRun );
		}
	}
}

void CSourceSharpSurfaceRenderer::DrawRenderList( IBrushSurface *pBrushSurface )
{
	Vector vWidthStep, vHeightStep;
	Steps( &vWidthStep, &vHeightStep );

	CMeshBuilder builder;
	IMesh *pMesh = NULL;
	int nCurStyle = -1;
	int nCurEdge  = -1;

	CMatRenderContextPtr pRenderContext( materials );

	for ( int i = 0; i < m_RenderList.Count(); i++ )
	{
		if ( nCurStyle != m_RenderList[i].m_nStyle || nCurEdge != m_RenderList[i].m_nEdgeType )
		{
			nCurStyle = m_RenderList[i].m_nStyle;
			nCurEdge  = m_RenderList[i].m_nEdgeType;

			SetDetailTexture( m_pEdge[nCurEdge][nCurStyle].m_pMaterialEdgeTexture );
			pRenderContext->Flush( false );
			pRenderContext->Bind( m_pCrackedMaterial );
			pMesh = pRenderContext->GetDynamicMesh();
		}

		const Vector vPos = m_vCorner
		                  + vWidthStep  * m_RenderList[i].m_nWidth
		                  + vHeightStep * m_RenderList[i].m_nHeight;

		DrawOneEdge( pBrushSurface, pMesh, &builder, vPos, vWidthStep, vHeightStep,
		             (SurfaceWinSide_t)m_RenderList[i].m_nSide );
	}
}

//-----------------------------------------------------------------------------
// Tile only: the grout showing through where a piece has gone.
//-----------------------------------------------------------------------------
void CSourceSharpSurfaceRenderer::DrawRenderListHighlights( IBrushSurface *pBrushSurface )
{
	Vector vWidthStep, vHeightStep;
	Steps( &vWidthStep, &vHeightStep );

	CMeshBuilder builder;
	IMesh *pMesh = NULL;
	int nCurStyle = -1;
	int nCurEdge  = -1;

	CMatRenderContextPtr pRenderContext( materials );

	for ( int i = 0; i < m_RenderList.Count(); i++ )
	{
		if ( nCurStyle != m_RenderList[i].m_nStyle || nCurEdge != m_RenderList[i].m_nEdgeType )
		{
			nCurStyle = m_RenderList[i].m_nStyle;
			nCurEdge  = m_RenderList[i].m_nEdgeType;

			IMaterial *pMat = m_pEdge[nCurEdge][nCurStyle].m_pMaterialEdge;
			pMesh = pRenderContext->GetDynamicMesh( true, NULL, NULL, pMat );
		}

		// Pulled a little off the surface so it does not z-fight the face it
		// is drawn against.
		const Vector vPos = m_vCorner
		                  + vWidthStep  * m_RenderList[i].m_nWidth
		                  + vHeightStep * m_RenderList[i].m_nHeight
		                  + m_vNormal * 0.30f;

		DrawOneHighlight( pBrushSurface, pMesh, &builder, vPos, vWidthStep, vHeightStep,
		                  (SurfaceWinSide_t)m_RenderList[i].m_nSide );
	}
}

//-----------------------------------------------------------------------------
// One quad, lit and textured as the surface it replaces.
//
// The texture and lightmap coordinates come from the BRUSH SURFACE rather than
// being computed: the quad is a piece of a face the world builder already
// mapped, and asking it is what keeps the glass lit like the wall it is in.
//-----------------------------------------------------------------------------
void CSourceSharpSurfaceRenderer::DrawOneBlock( IBrushSurface *pBrushSurface, IMesh *pMesh,
                                                CMeshBuilder *pBuilder, const Vector &vPos,
                                                const Vector &vWidth, const Vector &vHeight )
{
	pBuilder->Begin( pMesh, MATERIAL_QUADS, 1 );

	static const float s_Corners[4][2] = { { 0, 1 }, { 0, 0 }, { 1, 0 }, { 1, 1 } };

	Vector vAt = vPos;

	for ( int i = 0; i < 4; i++ )
	{
		Vector2D texCoord, lightCoord;
		pBrushSurface->ComputeTextureCoordinate( vAt, texCoord );
		pBrushSurface->ComputeLightmapCoordinate( vAt, lightCoord );

		pBuilder->Position3f( vAt.x, vAt.y, vAt.z );
		pBuilder->Color4ub( 255, 255, 255, 255 );
		pBuilder->TexCoord2f( 0, s_Corners[i][0], s_Corners[i][1] );
		pBuilder->TexCoord2fv( 1, lightCoord.Base() );
		pBuilder->TexCoord2fv( 2, texCoord.Base() );
		pBuilder->Normal3fv( m_vNormal.Base() );
		pBuilder->AdvanceVertex();

		// Round the quad: along, up, back.
		if ( i == 0 )      vAt = vAt + vWidth;
		else if ( i == 1 ) vAt = vAt + vHeight;
		else if ( i == 2 ) vAt = vAt - vWidth;
	}

	pBuilder->End();
	pMesh->Draw();
}

//-----------------------------------------------------------------------------
// One cracked edge. The four texture corners are rotated by which side of the
// pane the edge is on, which is what makes the crack run the right way.
//-----------------------------------------------------------------------------
void CSourceSharpSurfaceRenderer::DrawOneEdge( IBrushSurface *pBrushSurface, IMesh *pMesh,
                                               CMeshBuilder *pBuilder, const Vector &vStart,
                                               const Vector &vWStep, const Vector &vHStep,
                                               SurfaceWinSide_t nEdge )
{
	// Corner texture coordinates, indexed by side then by vertex.
	static const float s_Edge[4][4][2] =
	{
		/* BOTTOM */ { { 1, 1 }, { 0, 1 }, { 0, 0 }, { 1, 0 } },
		/* RIGHT  */ { { 1, 0 }, { 1, 1 }, { 0, 1 }, { 0, 0 } },
		/* TOP    */ { { 0, 0 }, { 1, 0 }, { 1, 1 }, { 0, 1 } },
		/* LEFT   */ { { 0, 1 }, { 0, 0 }, { 1, 0 }, { 1, 1 } },
	};

	pBuilder->Begin( pMesh, MATERIAL_QUADS, 1 );

	Vector vAt = vStart;

	for ( int i = 0; i < 4; i++ )
	{
		Vector2D texCoord, lightCoord;
		pBrushSurface->ComputeTextureCoordinate( vAt, texCoord );
		pBrushSurface->ComputeLightmapCoordinate( vAt, lightCoord );

		pBuilder->Position3f( vAt.x, vAt.y, vAt.z );
		pBuilder->Normal3fv( m_vNormal.Base() );
		pBuilder->Color4ub( 255, 255, 255, 255 );
		pBuilder->TexCoord2f( 0, s_Edge[nEdge][i][0], s_Edge[nEdge][i][1] );
		pBuilder->TexCoord2fv( 1, lightCoord.Base() );
		pBuilder->TexCoord2fv( 2, texCoord.Base() );
		pBuilder->AdvanceVertex();

		if ( i == 0 )      vAt = vAt + vWStep;
		else if ( i == 1 ) vAt = vAt + vHStep;
		else if ( i == 2 ) vAt = vAt - vWStep;
	}

	pBuilder->End();
	pMesh->Draw();
}

//-----------------------------------------------------------------------------
// The grout behind a fallen tile: the same quad, flat coloured and unlit by the
// surface's own texture.
//-----------------------------------------------------------------------------
void CSourceSharpSurfaceRenderer::DrawOneHighlight( IBrushSurface *pBrushSurface, IMesh *pMesh,
                                                    CMeshBuilder *pBuilder, const Vector &vStart,
                                                    const Vector &vWStep, const Vector &vHStep,
                                                    SurfaceWinSide_t nEdge )
{
	static const float s_Edge[4][4][2] =
	{
		/* BOTTOM */ { { 0, 1 }, { 1, 1 }, { 1, 0 }, { 0, 0 } },
		/* RIGHT  */ { { 1, 0 }, { 0, 0 }, { 0, 1 }, { 1, 1 } },
		/* TOP    */ { { 1, 1 }, { 0, 1 }, { 0, 0 }, { 1, 0 } },
		/* LEFT   */ { { 0, 0 }, { 1, 0 }, { 1, 1 }, { 0, 1 } },
	};

	// The colour of grout, which is stock's and arrived at by eye.
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
		pBuilder->Color4f( s_Grout.x, s_Grout.y, s_Grout.z, 1.0f );
		pBuilder->TexCoord2f( 0, s_Edge[nEdge][i][0], s_Edge[nEdge][i][1] );
		pBuilder->TexCoord2fv( 1, lightCoord.Base() );
		pBuilder->AdvanceVertex();

		if ( i == 0 )      vAt = vAt + vWStep;
		else if ( i == 1 ) vAt = vAt + vHStep;
		else if ( i == 2 ) vAt = vAt - vWStep;
	}

	pBuilder->End();
	pMesh->Draw();
}
