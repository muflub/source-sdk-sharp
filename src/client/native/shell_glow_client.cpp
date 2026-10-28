//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON a  a vtable the engine calls through
// Carries out the managed glow overlays (GlowOverlaySystem): material lookup by
// name and group (no reference taken), the GlowDrawOverlays plan op (hands the
// view to IManagedGlowClient::DrawOverlays and runs the commands it answers),
// and the GlowUpdateSkyOverlays op.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "view.h"
#include "iviewrender.h"
#include "view_shared.h"
#include "materialsystem/imesh.h"

#include "materialsystem/imaterialvar.h"
#include "clienteffectprecachesystem.h"

#include "client_entity.h"
#include "imanaged_areas_client.h"

#include "shell_glow_client.h"

// The materials stock's glow_overlay.cpp and warp_overlay.cpp precached, in
// their files' order.
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
CUtlVector<int32_t> s_Commands;

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
		const int32_t nKind = pCmds[i];
		if ( nKind == SS_GLOW_CMD_CLIP && i + 2 <= nWords )
		{
			const int32_t nMode = pCmds[i + 1];
			if ( nMode == 0 )
				bSaved = pRenderContext->EnableClipping( true );
			else
				pRenderContext->EnableClipping( nMode == 1 ? false : bSaved );
			i += 2;
		}
		else if ( nKind == SS_GLOW_CMD_HDRSCALE && i + 3 <= nWords )
		{
			if ( IMaterial *pMaterial = SourceSharp_FxMaterialOf( pCmds[i + 1] ) )
			{
				static unsigned int nHDRColorScaleCache = 0;
				if ( IMaterialVar *pVar = pMaterial->FindVarFast( "$hdrcolorscale", &nHDRColorScaleCache ) )
					pVar->SetFloatValue( FloatOf( pCmds[i + 2] ) );
			}
			i += 3;
		}
		else if ( nKind == SS_GLOW_CMD_QUAD && i + SS_GLOW_QUAD_WORDS <= nWords )
		{
			IMaterial *pMaterial = SourceSharp_FxMaterialOf( pCmds[i + 1] );
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
			IMaterial *pMaterial = SourceSharp_FxMaterialOf( pCmds[i + 1] );
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

	float in[SS_GLOW_VIEW_FLOATS];
	const Vector &origin = CurrentViewOrigin();
	const Vector &forward = CurrentViewForward();
	in[0] = origin.x;
	in[1] = origin.y;
	in[2] = origin.z;
	in[3] = forward.x;
	in[4] = forward.y;
	in[5] = forward.z;
	in[6] = view->GetViewSetup()->zFar;
	const VPlane *pFrustum = view->GetFrustum();
	for ( int p = 0; p < 5; p++ )
	{
		in[7 + p * 4] = pFrustum[p].m_Normal.x;
		in[8 + p * 4] = pFrustum[p].m_Normal.y;
		in[9 + p * 4] = pFrustum[p].m_Normal.z;
		in[10 + p * 4] = pFrustum[p].m_Dist;
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
