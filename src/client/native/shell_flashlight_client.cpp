//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the flashlight's executor ==========================//
// SS_REASON d  a layout the engine reads at fixed offsets
//
// plan_client_novalve C7 cluster 12. Stock's player flashlight
// (flashlighteffect.cpp's CFlashlightEffect, driven by
// C_BasePlayer::UpdateFlashlight) decides where the projected texture is, how
// it is shaped and when it exists; that is C#'s now
// (SourceSharp.Client.Flashlight, held to stock by FlashlightOracleTests over
// Fixtures/flashlight-stock.tsv). What is left native is what it hands its
// answer to: the client shadow manager, a C++ object that takes a
// FlashlightState_t by layout and keeps the ITexture pointer in it.
//
// IShimFlashlightClient's bodies, each one call carried out:
//
//   FlashlightTexture          CTextureReference::Init on a name managed chose
//                              (stock: effects/flashlight_border or
//                              effects/flashlight001), held for the DLL's life
//                              so the pointer the shadow manager keeps stays
//                              referenced; answers the id the state names.
//   FlashlightSupportsBorderColor  the hardware config's answer, which is the
//                              question stock's constructor asks to choose.
//   FlashlightCreate/Update    a FlashlightState_t from managed's fields, into
//                              CreateFlashlight / UpdateFlashlightState.
//   FlashlightProject          UpdateProjectedTexture( h, true ).
//   FlashlightDestroy          DestroyFlashlight.
//
// No decision is made here: managed says when to create, update (or not, for
// r_flashlightlockposition), project and destroy, and what every field is.
//=============================================================================//

#include "cbase.h"
#include "iclientshadowmgr.h"
#include "materialsystem/imaterialsystemhardwareconfig.h"
#include "materialsystem/MaterialSystemUtil.h"
#include "sourcesharp_entrypoints.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

namespace
{

// The textures managed has asked for, by name, each held by a reference for
// the DLL's life. Stock held one per effect (CFlashlightEffect's member); the
// flashlight asks for one of two names, so two slots are all it uses, and a
// third name is a refusal rather than a growth. Pointers, made on the first
// ask, so the table has no static initialiser to run before Init.
const int kMaxFlashlightTextures = 4;
CTextureReference *g_FlashlightTextures[ kMaxFlashlightTextures ];
char g_szFlashlightTextureNames[ kMaxFlashlightTextures ][ MAX_PATH ];
int g_nFlashlightTextures = 0;

void StateFromWire( const SourceSharpFlashlightState &w, FlashlightState_t &s )
{
	s.m_vecLightOrigin.Init( w.OriginXYZ[ 0 ], w.OriginXYZ[ 1 ], w.OriginXYZ[ 2 ] );
	s.m_quatOrientation.Init( w.Orientation[ 0 ], w.Orientation[ 1 ], w.Orientation[ 2 ], w.Orientation[ 3 ] );
	s.m_NearZ = w.NearZ;
	s.m_FarZ = w.FarZ;
	s.m_fHorizontalFOVDegrees = w.HorizontalFovDegrees;
	s.m_fVerticalFOVDegrees = w.VerticalFovDegrees;
	s.m_fQuadraticAtten = w.QuadraticAtten;
	s.m_fLinearAtten = w.LinearAtten;
	s.m_fConstantAtten = w.ConstantAtten;
	for ( int i = 0; i < 4; i++ )
		s.m_Color[ i ] = w.Color[ i ];
	s.m_pSpotlightTexture = ( w.Texture >= 0 && w.Texture < g_nFlashlightTextures )
	                      ? (ITexture *)*g_FlashlightTextures[ w.Texture ]
	                      : NULL;
	s.m_nSpotlightTextureFrame = w.TextureFrame;
	s.m_bEnableShadows = w.EnableShadows != 0;
	s.m_flShadowMapResolution = w.ShadowMapResolution;
	s.m_flShadowAtten = w.ShadowAtten;
	s.m_flShadowSlopeScaleDepthBias = w.ShadowSlopeScaleDepthBias;
	s.m_flShadowDepthBias = w.ShadowDepthBias;
}

} // namespace

int32_t SourceSharp_FlashlightTexture( const char *pszName )
{
	if ( !pszName || !pszName[ 0 ] )
		return -1;

	for ( int i = 0; i < g_nFlashlightTextures; i++ )
	{
		if ( !V_strcmp( g_szFlashlightTextureNames[ i ], pszName ) )
			return i;
	}

	if ( g_nFlashlightTextures >= kMaxFlashlightTextures )
		return -1;

	const int i = g_nFlashlightTextures++;
	V_strncpy( g_szFlashlightTextureNames[ i ], pszName, sizeof( g_szFlashlightTextureNames[ i ] ) );
	// Stock's call: the texture group and the complaint are CFlashlightEffect's.
	g_FlashlightTextures[ i ] = new CTextureReference;
	g_FlashlightTextures[ i ]->Init( pszName, TEXTURE_GROUP_OTHER, true );
	return i;
}

int32_t SourceSharp_FlashlightSupportsBorderColor( void )
{
	return ( g_pMaterialSystemHardwareConfig && g_pMaterialSystemHardwareConfig->SupportsBorderColor() ) ? 1 : 0;
}

int32_t SourceSharp_FlashlightCreate( const SourceSharpFlashlightState *pState )
{
	if ( !pState || !g_pClientShadowMgr )
		return -1;

	FlashlightState_t state;
	StateFromWire( *pState, state );
	const ClientShadowHandle_t h = g_pClientShadowMgr->CreateFlashlight( state );
	return h == CLIENTSHADOW_INVALID_HANDLE ? -1 : (int32_t)h;
}

void SourceSharp_FlashlightUpdate( int32_t nHandle, const SourceSharpFlashlightState *pState )
{
	if ( !pState || !g_pClientShadowMgr || nHandle < 0 )
		return;

	FlashlightState_t state;
	StateFromWire( *pState, state );
	g_pClientShadowMgr->UpdateFlashlightState( (ClientShadowHandle_t)nHandle, state );
}

void SourceSharp_FlashlightProject( int32_t nHandle )
{
	if ( g_pClientShadowMgr && nHandle >= 0 )
		g_pClientShadowMgr->UpdateProjectedTexture( (ClientShadowHandle_t)nHandle, true );
}

void SourceSharp_FlashlightDestroy( int32_t nHandle )
{
	if ( g_pClientShadowMgr && nHandle >= 0 )
		g_pClientShadowMgr->DestroyFlashlight( (ClientShadowHandle_t)nHandle );
}
