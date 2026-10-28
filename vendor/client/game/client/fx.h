//========= Copyright Valve Corporation, All rights reserved. ============//
#pragma once

#include "mathlib/vector.h"
#include "particles_simple.h"
#include "c_pixel_visibility.h"

class Vector;
class CGameTrace;
typedef CGameTrace trace_t;
//struct trace_t;

enum
{
	FX_ENERGYSPLASH_EXPLOSIVE		= 0x1,
	FX_ENERGYSPLASH_SMOKE			= 0x2,
	FX_ENERGYSPLASH_LITTLESPARKS	= 0x4,
	FX_ENERGYSPLASH_BIGSPARKS		= 0x8,
	FX_ENERGYSPLASH_BIGSPARKSCOLLIDE = 0x10,
	FX_ENERGYSPLASH_ENERGYBALLS		= 0x20,
	FX_ENERGYSPLASH_DLIGHT			= 0x40,
	
	FX_ENERGYSPLASH_DEFAULT = ~FX_ENERGYSPLASH_EXPLOSIVE,
	FX_ENERGYSPLASH_DEFAULT_EXPLOSIVE = ~0,
};

void FX_TracerSound( const Vector &start, const Vector &end, int iTracerType );

// Lighting information utility
void UTIL_GetNormalizedColorTintAndLuminosity( const Vector &color, Vector *tint = NULL, float *luminosity = NULL );

extern void FX_CacheMaterialHandles( void );

extern PMaterialHandle g_Mat_Fleck_Wood[2];
extern PMaterialHandle g_Mat_Fleck_Cement[2];
extern PMaterialHandle g_Mat_Fleck_Antlion[2];
extern PMaterialHandle g_Mat_Fleck_Tile[2];
extern PMaterialHandle g_Mat_DustPuff[2];
extern PMaterialHandle g_Mat_BloodPuff[2];
extern PMaterialHandle g_Mat_Fleck_Glass[2];
extern PMaterialHandle g_Mat_SMG_Muzzleflash[4];
extern PMaterialHandle g_Mat_Combine_Muzzleflash[3];
