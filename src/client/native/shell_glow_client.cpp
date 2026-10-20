//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp -- the client's glow overlays, carried out ===========//
// SS_REASON a  a vtable the engine calls through
//
// plan_client_novalve C7. glow_overlay.cpp's and warp_overlay.cpp's decisions
// are managed's (SourceSharp.Client.Render.GlowOverlays.GlowOverlaySystem: the
// list, the obstruction, the sprites' size, colour and basis, the cull); this
// file hands it what it reads and carries out what it answers:
//
//   SourceSharp_GlowMaterialFind   IShimViewClient: materials->FindMaterial by
//                                  name and group, as the overlays found theirs
//                                  (no reference taken), named by the effect
//                                  list's material id.
//   ShellGlowDrawOverlays          the plan's GlowDrawOverlays op: the view (the
//                                  current view, the view setup's zFar, the
//                                  frustum's first five planes, the wireframe
//                                  question, r_drawsprites) handed to
//                                  IManagedGlowClient::DrawOverlays, and the
//                                  commands it answers carried out: the
//                                  clipping switch (its first answer kept), a
//                                  material's $hdrcolorscale, a quad, a
//                                  wireframe quad.
//   ShellGlowUpdateSkyOverlays     the plan's GlowUpdateSkyOverlays op.
//
// No decision is made here: the ifs are bounds checks, null guards and the
// switch over a command's kind.
//=============================================================================//

#include "cbase.h"
#include "view.h"
#include "iviewrender.h"
#include "view_shared.h"
#include "materialsystem/imesh.h"
#include "materialsystem/imaterial.h"
#include "materialsystem/imaterialvar.h"
#include "imanaged_areas_client.h"
#include "sourcesharp_entrypoints.h"
#include "shell_glow_client.h"

#include "clienteffectprecachesystem.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

//-----------------------------------------------------------------------------
// glow_overlay.cpp's and warp_overlay.cpp's precache lists, which left with
// them (C7): the overlays are C#'s (GlowOverlaySystem) and draw through the
// executor below; the precache system is still native, so the materials stock's
// two files registered are registered here, unchanged and in their files' order.
//-----------------------------------------------------------------------------
CLIENTEFFECT_REGISTER_BEGIN( PrecacheEffectGlow )
CLIENTEFFECT_MATERIAL( "sun/overlay" )
CLIENTEFFECT_MATERIAL( "sprites/light_glow02_add_noz" )
CLIENTEFFECT_REGISTER_END()

CLIENTEFFECT_REGISTER_BEGIN( PrecacheEffectWarp )
CLIENTEFFECT_MATERIAL( "sun/overlay" )
CLIENTEFFECT_REGISTER_END()

IMaterial *SourceSharp_FxMaterialOf( int32_t nMaterial );
int32_t SourceSharp_FxMaterialIdOf( IMaterial *pMaterial );
extern ConVar r_drawsprites;

namespace
{

// The commands a draw answers with.
CUtlVector< int32_t > s_Commands;

float FloatOf( int32_t nBits )
{
	float f;
	memcpy( &f, &nBits, sizeof( f ) );
	return f;
}

void Execute( const int32_t *pCmds, int nWords )
{
	CMatRenderContextPtr pRenderContext( materials );
	bool bSaved = false;
	int i = 0;
	while ( i < nWords )
	{
		const int32_t nKind = pCmds[ i ];
		if ( nKind == SS_GLOW_CMD_CLIP && i + 2 <= nWords )
		{
			const int32_t nMode = pCmds[ i + 1 ];
			if ( nMode == 0 )
				bSaved = pRenderContext->EnableClipping( true );
			else
				pRenderContext->EnableClipping( nMode == 1 ? false : bSaved );
			i += 2;
		}
		else if ( nKind == SS_GLOW_CMD_HDRSCALE && i + 3 <= nWords )
		{
			if ( IMaterial *pMaterial = SourceSharp_FxMaterialOf( pCmds[ i + 1 ] ) )
			{
				static unsigned int nHDRColorScaleCache = 0;
				if ( IMaterialVar *pVar = pMaterial->FindVarFast( "$hdrcolorscale", &nHDRColorScaleCache ) )
					pVar->SetFloatValue( FloatOf( pCmds[ i + 2 ] ) );
			}
			i += 3;
		}
		else if ( nKind == SS_GLOW_CMD_QUAD && i + SS_GLOW_QUAD_WORDS <= nWords )
		{
			IMaterial *pMaterial = SourceSharp_FxMaterialOf( pCmds[ i + 1 ] );
			IMesh *pMesh = pRenderContext->GetDynamicMesh( false, 0, 0, pMaterial );
			CMeshBuilder builder;
			builder.Begin( pMesh, MATERIAL_QUADS, 1 );
			for ( int v = 0; v < 4; v++ )
			{
				const int32_t *p = pCmds + i + 2 + v * 8;
				const float pos[3] = { FloatOf( p[0] ), FloatOf( p[1] ), FloatOf( p[2] ) };
				builder.Position3fv( pos );
				builder.Color4f( FloatOf( p[3] ), FloatOf( p[4] ), FloatOf( p[5] ), 1 );
				builder.TexCoord2f( 0, FloatOf( p[6] ), FloatOf( p[7] ) );
				builder.AdvanceVertex();
			}
			builder.End( false, true );
			i += SS_GLOW_QUAD_WORDS;
		}
		else if ( nKind == SS_GLOW_CMD_WIREQUAD && i + SS_GLOW_WIREQUAD_WORDS <= nWords )
		{
			IMaterial *pMaterial = SourceSharp_FxMaterialOf( pCmds[ i + 1 ] );
			pRenderContext->Bind( pMaterial );
			IMesh *pMesh = pRenderContext->GetDynamicMesh( false, 0, 0, pMaterial );
			CMeshBuilder builder;
			builder.Begin( pMesh, MATERIAL_QUADS, 1 );
			for ( int v = 0; v < 4; v++ )
			{
				const int32_t *p = pCmds + i + 2 + v * 3;
				const float pos[3] = { FloatOf( p[0] ), FloatOf( p[1] ), FloatOf( p[2] ) };
				builder.Position3fv( pos );
				builder.Color3f( 1.0f, 0.0f, 0.0f );
				builder.AdvanceVertex();
			}
			builder.End( false, true );
			i += SS_GLOW_WIREQUAD_WORDS;
		}
		else
		{
			Warning( "[SourceSharp] glow overlays: command %d at word %d of %d is not one; the rest is dropped\n",
			         nKind, i, nWords );
			return;
		}
	}
}

} // namespace

int32_t SourceSharp_GlowMaterialFind( const char *pszName, const char *pszGroup )
{
	if ( !pszName || !pszGroup || !materials )
		return 0;
	return SourceSharp_FxMaterialIdOf( materials->FindMaterial( pszName, pszGroup ) );
}

void ShellGlowDrawOverlays( bool bCacheFullSceneState )
{
	IManagedGlowClient *pManaged = ManagedGlowClient();
	if ( !pManaged || !view )
		return;

	float in[ SS_GLOW_VIEW_FLOATS ];
	const Vector &origin = CurrentViewOrigin();
	const Vector &forward = CurrentViewForward();
	in[0] = origin.x; in[1] = origin.y; in[2] = origin.z;
	in[3] = forward.x; in[4] = forward.y; in[5] = forward.z;
	in[6] = view->GetViewSetup()->zFar;
	const VPlane *pFrustum = view->GetFrustum();
	for ( int p = 0; p < 5; p++ )
	{
		in[ 7 + p * 4 ] = pFrustum[ p ].m_Normal.x;
		in[ 8 + p * 4 ] = pFrustum[ p ].m_Normal.y;
		in[ 9 + p * 4 ] = pFrustum[ p ].m_Normal.z;
		in[ 10 + p * 4 ] = pFrustum[ p ].m_Dist;
	}
	in[27] = ShouldDrawInWireFrameMode() ? 1.0f : 0.0f;
	in[28] = (float)r_drawsprites.GetInt();

	if ( s_Commands.Count() < 2048 )
		s_Commands.SetCount( 2048 );
	int32_t nWords = pManaged->DrawOverlays( in, SS_GLOW_VIEW_FLOATS, bCacheFullSceneState ? 1 : 0, s_Commands.Base(),
	                                         s_Commands.Count() );
	if ( nWords > s_Commands.Count() )
	{
		s_Commands.SetCount( nWords );
		nWords = pManaged->Commands( s_Commands.Base(), s_Commands.Count() );
	}
	if ( nWords > 0 && nWords <= s_Commands.Count() )
		Execute( s_Commands.Base(), nWords );
}

void ShellGlowUpdateSkyOverlays( float flZFar, bool bCacheFullSceneState )
{
	if ( IManagedGlowClient *pManaged = ManagedGlowClient() )
	{
		const Vector &origin = CurrentViewOrigin();
		const float in[3] = { origin.x, origin.y, origin.z };
		pManaged->UpdateSkyOverlays( in, flZFar, bCacheFullSceneState ? 1 : 0 );
	}
}
