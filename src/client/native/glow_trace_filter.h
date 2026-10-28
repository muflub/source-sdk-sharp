//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// CTraceFilterGlow::ShouldHitEntity's solid-type test, applied before CTraceFilterSimple.

#pragma once

#include "const.h"

inline bool GlowSightAdmits( int nSolid )
{
	return nSolid == SOLID_VPHYSICS || nSolid == SOLID_BSP;
}
