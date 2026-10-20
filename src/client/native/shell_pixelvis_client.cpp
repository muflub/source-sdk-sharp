//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp -- the client's pixel visibility, carried out =========//
// SS_REASON a  a vtable the engine calls through
//
// plan_client_novalve C7. c_pixel_visibility.cpp's decisions are managed's
// (SourceSharp.Client.Render.PixelVisibility.PixelVisibilitySystem: the sets,
// the queries, the fades, the proxies' pyramids); this file holds what the
// material system hands out and carries out what managed answers:
//
//   SourceSharp_PixelVisQuery*      IShimViewClient: the render context's
//                                   occlusion query objects, held in a table
//                                   and named to managed by id (1, 2, ...; 0
//                                   for none); their pixel counts, the
//                                   counting query's read first as stock did.
//   SourceSharp_PixelVisDiameters   IShimViewClient: ComputePixelDiameterOfSphere
//                                   for each sphere, one crossing for a view.
//   SourceSharp_PixelVisView        IShimViewClient: CurrentViewID and the
//                                   current view's origin and axes.
//   ShellPixelVisEndView,           the plan's PixelVisEndCurrentView and
//   ShellPixelVisEndScene           PixelVisEndScene ops: the view handed to
//                                   IManagedPixelVisClient, and the commands it
//                                   answers carried out (bind, depth range, a
//                                   proxy's four triangles inside its query).
//
// No decision is made here: the ifs are bounds checks, null guards and the
// switch over a command's kind.
//=============================================================================//

#include "cbase.h"
#include "view.h"
#include "viewrender.h"
#include "materialsystem/imesh.h"
#include "materialsystem/imaterial.h"
#include "imanaged_areas_client.h"
#include "sourcesharp_entrypoints.h"
#include "shell_pixelvis_client.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

IMaterial *SourceSharp_FxMaterialOf( int32_t nMaterial );

namespace
{

// The query objects managed holds by id: slot id - 1, INVALID when free.
CUtlVector< OcclusionQueryObjectHandle_t > s_Queries;

OcclusionQueryObjectHandle_t QueryOf( int32_t nQuery )
{
	if ( nQuery < 1 || nQuery > s_Queries.Count() )
		return INVALID_OCCLUSION_QUERY_OBJECT_HANDLE;
	return s_Queries[ nQuery - 1 ];
}

// The commands a view's end answers with (PixelVisDraw's, in C#).
CUtlVector< int32_t > s_Commands;

float FloatOf( int32_t nBits )
{
	float f;
	memcpy( &f, &nBits, sizeof( f ) );
	return f;
}

// One view's commands, in order.
void Execute( const int32_t *pCmds, int nWords )
{
	CMatRenderContextPtr pRenderContext( materials );
	int i = 0;
	while ( i < nWords )
	{
		const int32_t nKind = pCmds[ i ];
		if ( nKind == SS_PIXELVIS_CMD_BIND && i + 2 <= nWords )
		{
			pRenderContext->Bind( SourceSharp_FxMaterialOf( pCmds[ i + 1 ] ) );
			i += 2;
		}
		else if ( nKind == SS_PIXELVIS_CMD_DEPTHRANGE && i + 3 <= nWords )
		{
			pRenderContext->DepthRange( FloatOf( pCmds[ i + 1 ] ), FloatOf( pCmds[ i + 2 ] ) );
			i += 3;
		}
		else if ( nKind == SS_PIXELVIS_CMD_PROXY && i + SS_PIXELVIS_PROXY_WORDS <= nWords )
		{
			const OcclusionQueryObjectHandle_t hQuery = QueryOf( pCmds[ i + 1 ] );
			IMaterial *pMaterial = SourceSharp_FxMaterialOf( pCmds[ i + 2 ] );
			const int32_t *pVerts = pCmds + i + 3;
			pRenderContext->BeginOcclusionQueryDrawing( hQuery );
			CMeshBuilder meshBuilder;
			IMesh *pMesh = pRenderContext->GetDynamicMesh( false, NULL, NULL, pMaterial );
			meshBuilder.Begin( pMesh, MATERIAL_TRIANGLES, 4 );
			for ( int v = 0; v < 12; v++ )
			{
				const float pos[3] = { FloatOf( pVerts[ v * 3 ] ), FloatOf( pVerts[ v * 3 + 1 ] ), FloatOf( pVerts[ v * 3 + 2 ] ) };
				meshBuilder.Position3fv( pos );
				meshBuilder.AdvanceVertex();
			}
			meshBuilder.End();
			pMesh->Draw();
			pRenderContext->EndOcclusionQueryDrawing( hQuery );
			i += SS_PIXELVIS_PROXY_WORDS;
		}
		else
		{
			Warning( "[SourceSharp] pixel visibility: command %d at word %d of %d is not one; the rest is dropped\n",
			         nKind, i, nWords );
			return;
		}
	}
}

} // namespace

int32_t SourceSharp_PixelVisQueryCreate()
{
	if ( !materials )
		return 0;
	CMatRenderContextPtr pRenderContext( materials );
	const OcclusionQueryObjectHandle_t h = pRenderContext->CreateOcclusionQueryObject();
	if ( h == INVALID_OCCLUSION_QUERY_OBJECT_HANDLE )
		return 0;
	for ( int i = 0; i < s_Queries.Count(); i++ )
	{
		if ( s_Queries[ i ] == INVALID_OCCLUSION_QUERY_OBJECT_HANDLE )
		{
			s_Queries[ i ] = h;
			return i + 1;
		}
	}
	s_Queries.AddToTail( h );
	return s_Queries.Count();
}

void SourceSharp_PixelVisQueryDestroy( int32_t nQuery )
{
	const OcclusionQueryObjectHandle_t h = QueryOf( nQuery );
	if ( h == INVALID_OCCLUSION_QUERY_OBJECT_HANDLE || !materials )
		return;
	CMatRenderContextPtr pRenderContext( materials );
	pRenderContext->DestroyOcclusionQueryObject( h );
	s_Queries[ nQuery - 1 ] = INVALID_OCCLUSION_QUERY_OBJECT_HANDLE;
}

void SourceSharp_PixelVisQueryReset( int32_t nQuery )
{
	const OcclusionQueryObjectHandle_t h = QueryOf( nQuery );
	if ( h == INVALID_OCCLUSION_QUERY_OBJECT_HANDLE || !materials )
		return;
	CMatRenderContextPtr pRenderContext( materials );
	pRenderContext->ResetOcclusionQueryObject( h );
}

void SourceSharp_PixelVisQueryPixels( int32_t nVisible, int32_t nCount, int32_t *pOut )
{
	if ( !pOut )
		return;
	pOut[0] = -1;
	pOut[1] = -1;
	if ( !materials )
		return;
	CMatRenderContextPtr pRenderContext( materials );
	// The counting query first, as GetFractionVisible's partial arm read them.
	const OcclusionQueryObjectHandle_t hCount = QueryOf( nCount );
	if ( hCount != INVALID_OCCLUSION_QUERY_OBJECT_HANDLE )
		pOut[1] = pRenderContext->OcclusionQuery_GetNumPixelsRendered( hCount );
	const OcclusionQueryObjectHandle_t hVisible = QueryOf( nVisible );
	if ( hVisible != INVALID_OCCLUSION_QUERY_OBJECT_HANDLE )
		pOut[0] = pRenderContext->OcclusionQuery_GetNumPixelsRendered( hVisible );
}

void SourceSharp_PixelVisDiameters( const float *pSpheres, int32_t nSpheres, float *pOut )
{
	if ( !pSpheres || !pOut || nSpheres <= 0 || !materials )
		return;
	CMatRenderContextPtr pRenderContext( materials );
	for ( int i = 0; i < nSpheres; i++ )
	{
		const float *s = pSpheres + i * 4;
		pOut[ i ] = pRenderContext->ComputePixelDiameterOfSphere( Vector( s[0], s[1], s[2] ), s[3] );
	}
}

int32_t SourceSharp_PixelVisView( float *pOut, int32_t nFloats )
{
	if ( pOut && nFloats >= 12 )
	{
		const Vector *axes[] = { &CurrentViewOrigin(), &CurrentViewForward(), &CurrentViewRight(), &CurrentViewUp() };
		for ( int a = 0; a < 4; a++ )
		{
			pOut[ a * 3 ] = axes[ a ]->x;
			pOut[ a * 3 + 1 ] = axes[ a ]->y;
			pOut[ a * 3 + 2 ] = axes[ a ]->z;
		}
	}
	return CurrentViewID();
}

void ShellPixelVisEndView()
{
	IManagedPixelVisClient *pManaged = ManagedPixelVisClient();
	if ( !pManaged )
		return;

	float view[ SS_PIXELVIS_VIEW_FLOATS ];
	view[0] = (float)CurrentViewID();
	SourceSharp_PixelVisView( view + 1, 12 );
	const VMatrix &m = engine->WorldToScreenMatrix();
	for ( int r = 0; r < 4; r++ )
		for ( int c = 0; c < 4; c++ )
			view[ 13 + r * 4 + c ] = m[ r ][ c ];

	if ( s_Commands.Count() < 4096 )
		s_Commands.SetCount( 4096 );
	int32_t nWords = pManaged->EndView( view, SS_PIXELVIS_VIEW_FLOATS, s_Commands.Base(), s_Commands.Count() );
	if ( nWords > s_Commands.Count() )
	{
		s_Commands.SetCount( nWords );
		nWords = pManaged->Commands( s_Commands.Base(), s_Commands.Count() );
	}
	if ( nWords > 0 && nWords <= s_Commands.Count() )
		Execute( s_Commands.Base(), nWords );
}

void ShellPixelVisEndScene()
{
	if ( IManagedPixelVisClient *pManaged = ManagedPixelVisClient() )
		pManaged->EndScene();
}
