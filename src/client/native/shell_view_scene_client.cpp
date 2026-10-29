//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The view_scene.cpp names the rest of client.so still imports.
// SS_REASON c  static-init registration

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"

#include "materialsystem/imaterialvar.h"

#include "materialsystem/itexture.h"

#include "client_entity.h"

#include "shell_render_targets.h"
#include "view_frustum_transform.h"

ConVar r_updaterefracttexture( "r_updaterefracttexture", "1", FCVAR_CHEAT );
static ConVar r_depthoverlay(
	"r_depthoverlay", "0", FCVAR_CHEAT,
	"Replaces opaque objects with their grayscaled depth values. r_showz_power scales the output." );

int g_viewscene_refractUpdateFrame = 0;
bool g_bAllowMultipleRefractUpdatesPerScenePerFrame = false;

int ScreenTransform( const Vector &point, Vector &screen )
{
	const VMatrix &m = engine->WorldToScreenMatrix();
	float rows[16];
	for ( int r = 0; r < 4; ++r )
		for ( int c = 0; c < 4; ++c )
			rows[r * 4 + c] = m[r][c];
	return ViewFrustumTransform( rows, &point.x, &screen.x );
}

// The depth buffer copied into the full-frame depth texture, marked valid,
// and r_depthoverlay's debug quad over it. Nothing without ps_2_b.
void UpdateFullScreenDepthTexture()
{
	if ( !g_pMaterialSystemHardwareConfig->SupportsPixelShaders_2_b() )
		return;

	ITexture *pDepthTex = ShellRenderTarget( SS_RT_FullFrameDepth );
	CMatRenderContextPtr pRenderContext( materials );
	pRenderContext->CopyRenderTargetToTextureEx( pDepthTex, 0, NULL, NULL );
	pRenderContext->SetFullScreenDepthTextureValidityFlag( true );

	if ( !r_depthoverlay.GetBool() )
		return;

	IMaterial *pMaterial = materials->FindMaterial( "debug/showz", TEXTURE_GROUP_OTHER, true );
	pMaterial->IncrementReferenceCount();
	IMaterialVar *pBaseTexture = pMaterial->FindVar( "$basetexture", NULL, false );
	IMaterialVar *pDepthInAlpha = pMaterial->FindVar( "$ALPHADEPTH", NULL, false );
	pDepthInAlpha->SetIntValue( 1 );
	pBaseTexture->SetTextureValue( pDepthTex );
	pRenderContext->OverrideDepthEnable( true, false );
	pRenderContext->DrawScreenSpaceQuad( pMaterial );
	pRenderContext->OverrideDepthEnable( false, true );
	pMaterial->DecrementReferenceCount();
}
