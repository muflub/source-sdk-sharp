//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the client's world render state ====================//
// SS_REASON d  a layout the engine reads at fixed offsets
//
// The far end of ClientSetWorldRenderState. Three stock map entities exist
// only so the client renders differently — shadow_control, water_lod_control
// and env_tonemap_controller — and each is a handful of numbers pushed into a
// client subsystem when they change and read from there every frame after.
//
// The entities themselves are C#: their keys, their inputs, and the deciding
// of when a number changes. What is left over is where the number goes, which
// is what this file is. It holds no state of its own on purpose — the state
// lives in the entity's replicated fields, and a second copy here would be a
// second thing to keep in step.
//
//=============================================================================//
#include "cbase.h"
#include "shell_standalone_client.h"
#include "sourcesharp.h"

#include "iviewrender.h"
#include "materialsystem/imaterialsystem.h"
#include "c_baseplayer.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

//-----------------------------------------------------------------------------
// The tonemap controls viewpostprocess.cpp held as plain globals: written here
// from the map's tonemap controller (the block below), read by the render
// executor's trace note. Defined here, by their one writer.
//-----------------------------------------------------------------------------
bool  g_bUseCustomAutoExposureMin = false;
bool  g_bUseCustomAutoExposureMax = false;
bool  g_bUseCustomBloomScale = false;
float g_flCustomAutoExposureMin = 0.0f;
float g_flCustomAutoExposureMax = 0.0f;
float g_flCustomBloomScale = 0.0f;
float g_flCustomBloomScaleMinimum = 0.0f;

//-----------------------------------------------------------------------------
// One block of world render state.
//
// The count is checked rather than trusted. What is on the other side of this
// call is managed code across a process-internal boundary, and a selector
// whose block arrived short would otherwise read whatever follows the caller's
// buffer — so a short block is dropped and said so, which is a bug report
// rather than a corrupted shadow direction.
//-----------------------------------------------------------------------------
static bool WorldRenderCountOk( const char *pWhat, int32_t nCount, int32_t nWanted )
{
	if ( nCount >= nWanted )
		return true;

	Warning( "[SourceSharp] ClientSetWorldRenderState(%s): %d values, wanted %d — ignored\n",
	         pWhat, nCount, nWanted );
	return false;
}

void SourceSharp_ClientSetWorldRenderState( int32_t nWhich, const float *pValues, int32_t nCount )
{
	if ( !pValues || nCount <= 0 )
		return;

	// At developer 2, because there is no other way to see that this ran.
	//
	// Everything downstream of here is a renderer global with no reader outside
	// the render path, so a client half that never decoded anything and a
	// client half that decoded zeroes look identical from a console, from a log
	// and from a datatable dump. The alternative to this line is joining and
	// looking at the screen, which is not a check a script can make.
	DevMsg( 2, "[SourceSharp] world render state %d, %d value(s)\n", nWhich, nCount );

	switch ( nWhich )
	{
	// SOURCESHARP_WORLDRENDER_SHADOW, shadow_control's direction, colour,
	// distance and switch, stood here: the client shadow manager is managed's
	// (C7 cluster 12 S2), and ClientShadowControl hands them to it directly.

	case SOURCESHARP_WORLDRENDER_WATERLOD:
	{
		if ( !WorldRenderCountOk( "waterlod", nCount, 2 ) )
			return;

		view->SetCheapWaterStartDistance( pValues[0] );
		view->SetCheapWaterEndDistance( pValues[1] );
		return;
	}

	case SOURCESHARP_WORLDRENDER_TONEMAP:
	{
		if ( !WorldRenderCountOk( "tonemap", nCount, 7 ) )
			return;

		g_bUseCustomAutoExposureMin = ( pValues[0] != 0.0f );
		g_bUseCustomAutoExposureMax = ( pValues[1] != 0.0f );
		g_bUseCustomBloomScale      = ( pValues[2] != 0.0f );
		g_flCustomAutoExposureMin   = pValues[3];
		g_flCustomAutoExposureMax   = pValues[4];
		g_flCustomBloomScale        = pValues[5];
		g_flCustomBloomScaleMinimum = pValues[6];
		return;
	}

	default:
		Warning( "[SourceSharp] ClientSetWorldRenderState: unknown selector %d\n", nWhich );
		return;
	}
}


//-----------------------------------------------------------------------------
// The client's colour-correction lookups, carried out on the material system
// (C9: colorcorrectionmgr.cpp left client.so).
//
// THE TABLE IS MANAGED (lane M1-A). The handles that name a lookup, which
// file and how much of it to apply, and when the frame's weights are reset
// before the entities re-weigh them, are all SourceSharp.Client's
// (ColorLookupTable, ColorCorrections). What stays here is the render
// context's own calls on the opaque ColorCorrectionHandle_t managed holds,
// and the count of non-zero weights since the reset that the post-process
// plan's input reads. Stock's FIXME holds: the count is of SetWeight calls
// with a non-zero weight, not of distinct lookups.
//-----------------------------------------------------------------------------
static int g_nActiveColorWeights;

void *SourceSharp_ColorLookupAdd( const char *pLookupFile )
{
	if ( !pLookupFile || !pLookupFile[0] )
		return NULL;

	// A copy, because what arrived is managed memory this side must not write
	// through.
	char szFile[ MAX_PATH ];
	Q_strncpy( szFile, pLookupFile, sizeof( szFile ) );

	CMatRenderContextPtr pRenderContext( g_pMaterialSystem );
	ColorCorrectionHandle_t hLookup = pRenderContext->AddLookup( szFile );

	if ( !hLookup )
	{
		Warning( "Cannot find color correction lookup file: '%s'\n", szFile );
		return NULL;
	}

	pRenderContext->LockLookup( hLookup );
	pRenderContext->LoadLookup( hLookup, szFile );
	pRenderContext->UnlockLookup( hLookup );

	// At developer 2, for the reason the world render state says at length:
	// everything downstream is a renderer global and a client half that never
	// ran looks exactly like one that ran and applied zero.
	DevMsg( 2, "[SourceSharp] colour lookup loaded from '%s'\n", szFile );

	return (void *)hLookup;
}

void SourceSharp_ColorLookupSetWeight( void *hLookup, float flWeight )
{
	if ( !hLookup )
		return;

	CMatRenderContextPtr pRenderContext( g_pMaterialSystem );
	pRenderContext->SetLookupWeight( (ColorCorrectionHandle_t)hLookup, flWeight );

	if ( flWeight != 0.0f )
		++g_nActiveColorWeights;
}

void SourceSharp_ColorLookupRemove( void *hLookup )
{
	if ( !hLookup )
		return;

	CMatRenderContextPtr pRenderContext( g_pMaterialSystem );
	pRenderContext->RemoveLookup( (ColorCorrectionHandle_t)hLookup );
}

void SourceSharp_ColorLookupResetWeights()
{
	CMatRenderContextPtr pRenderContext( g_pMaterialSystem );
	pRenderContext->ResetLookupWeights();
	g_nActiveColorWeights = 0;
}

// Whether any lookup was given a non-zero weight since the reset.
bool SourceSharp_ColorCorrectionActive()
{
	return g_nActiveColorWeights != 0;
}

//-----------------------------------------------------------------------------
// Where the local player is standing.
//
// The player's origin rather than the view origin, which is stock's choice in
// C_ColorCorrection and is the one that stays put when the camera does not: a
// correction that strengthened because somebody leaned would be a correction
// nobody asked for.
//
// Answers 0 rather than writing a zero when there is no local player. The
// difference matters -- a zero vector reads as the map origin, and a falloff
// measured from there is a correction that switches itself on for the whole
// map for the frames between connecting and spawning.
//-----------------------------------------------------------------------------
int32_t SourceSharp_GetLocalPlayerOrigin( float *pOutXYZ )
{
	if ( !pOutXYZ )
		return 0;

	Vector vecOrigin;

	if ( C_StandaloneEntity *pStandalone = SourceSharp_LocalPlayerShell() )
	{
		vecOrigin = pStandalone->GetAbsOrigin();
	}
	else
	{
		return 0;
	}

	pOutXYZ[0] = vecOrigin.x;
	pOutXYZ[1] = vecOrigin.y;
	pOutXYZ[2] = vecOrigin.z;
	return 1;
}
