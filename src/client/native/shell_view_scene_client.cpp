//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp -- what view_scene.cpp gave the rest of client.so =====//
// SS_REASON c  static-init registration
// plan_client_novalve C7 step 2: view_scene.cpp leaves client.so. Four of its
// names are still imported, and they are here:
//   * r_updaterefracttexture and g_viewscene_refractUpdateFrame, which
//     view_scene.h's inline UpdateRefractTexture reads (particlemgr.cpp, the
//     render plan's executor, stock's viewrender while it stays);
//   * UpdateFullScreenDepthTexture, which stock's viewrender calls (the plan's
//     executor carries the same copy as its own op);
//   * ScreenTransform, c_pixel_visibility.cpp's world-to-screen projection.
// ViewTransform, HudTransform and FrustumTransform's other callers, and the
// console multiple-refract system, had no importer and went with the TU.
//=============================================================================//

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
#include "materialsystem/imaterialsystem.h"
#include "materialsystem/imaterialvar.h"
#include "materialsystem/imaterialsystemhardwareconfig.h"
#include "materialsystem/imaterial.h"
#include "materialsystem/itexture.h"
#include "mathlib/vector.h"
#include "mathlib/vmatrix.h"
#include "tier1/convar.h"
#include "tier1/iconvar.h"
#include "tier1/refcount.h"
#include "cdll_client_int.h"
#include "cdll_int.h"
#include "client_entity.h"
#include "collisionproperty.h"
#include "texture_group_names.h"
#include "shell_render_targets.h"
#include "view_frustum_transform.h"

// memdbgon must be the last include file in a .cpp file!!!

ConVar r_updaterefracttexture( "r_updaterefracttexture", "1", FCVAR_CHEAT );
static ConVar r_depthoverlay( "r_depthoverlay", "0", FCVAR_CHEAT,
                              "Replaces opaque objects with their grayscaled depth values. r_showz_power scales the output." );

int g_viewscene_refractUpdateFrame = 0;
bool g_bAllowMultipleRefractUpdatesPerScenePerFrame = false;

int ScreenTransform( const Vector &point, Vector &screen )
{
	const VMatrix &m = engine->WorldToScreenMatrix();
	float rows[16];
	for ( int r = 0; r < 4; ++r )
		for ( int c = 0; c < 4; ++c )
			rows[ r * 4 + c ] = m[r][c];
	return ViewFrustumTransform( rows, &point.x, &screen.x );
}

// The depth buffer copied into the full-frame depth texture, marked valid,
// and r_depthoverlay's debug quad over it. Nothing without ps_2_b.
void UpdateFullScreenDepthTexture( void )
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
