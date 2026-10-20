//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp ==================================================//
//
// What SOURCESHARP_TRACE_FILTER_GLOW lets stop a trace: c_pixel_visibility.cpp's
// CTraceFilterGlow::ShouldHitEntity, which asks only the entity's solid type
// before handing it to CTraceFilterSimple. Engine-free, so the pixel
// visibility oracle (pixelvis_oracle_test) holds it to the pristine filter
// class for every solid type, and shell_worldquery_client.cpp asks it of the
// collideable a trace reaches.
//
//=============================================================================

#ifndef SS_GLOW_TRACE_FILTER_H
#define SS_GLOW_TRACE_FILTER_H

#include "const.h"

inline bool GlowSightAdmits( int nSolid )
{
	return nSolid == SOLID_VPHYSICS || nSolid == SOLID_BSP;
}

#endif // SS_GLOW_TRACE_FILTER_H
