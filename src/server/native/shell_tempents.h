//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The server's temp entity calls, each written through shell_tempents.cpp's
// generic writer against a table C# declares.
#pragma once

#include "irecipientfilter.h"


namespace ss { struct CEffectData; }

// The generic writer: a temp entity id, named fields and a filter.
bool SourceSharpTe_Forward( int nId, IRecipientFilter &filter, float flDelay,
                            const float *pFloats, int nFloats,
                            const int *pInts, int nInts );

// The effects, with te.cpp's signatures.

void TE_BloodStream( IRecipientFilter& filter, float delay,
	const Vector* org, const Vector* dir, int r, int g, int b, int a, int amount );

void TE_Decal( IRecipientFilter& filter, float delay,
	const Vector* pos, const Vector* start, int entity, int hitbox, int index );

void TE_DispatchEffect( IRecipientFilter& filter, float delay,
	const Vector &pos, const char *pName, const ss::CEffectData &data );

void TE_Smoke( IRecipientFilter& filter, float delay,
	const Vector* pos, int modelindex, float scale, int framerate );

void TE_Sparks( IRecipientFilter& filter, float delay,
	const Vector* pos, int nMagnitude, int nTrailLength, const Vector *pDir );

void TE_WorldDecal( IRecipientFilter& filter, float delay,
	const Vector* pos, int index );
