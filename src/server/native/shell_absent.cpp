//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
//
// Definitions the server link still imports from removed stock files.

#include <cstddef>

#include "ss_shareddefs.h"

#include "igamerules.h"
#include "ss_effect_dispatch.h"

#include "ss_takedamageinfo.h"

uint32_t SourceSharp_HandleFromEntity( IGameEntity *pEntity );
extern CGlobalVars *gpGlobals;
class CFunctor;

// Blood in the air, at a point and in a direction (effects.cpp). Serves the managed
// env_blood and bullet impacts through SourceSharp_BloodEffect.
void UTIL_BloodSpray( const Vector &pos, const Vector &dir, int color, int amount, int flags )
{
	if ( color == ss::DONT_BLEED )
		return;

	ss::CEffectData data;

	data.m_vOrigin = pos;
	data.m_vNormal = dir;
	data.m_flScale = (float)amount;
	data.m_fFlags = flags;
	data.m_nColor = color;

	DispatchEffect( "bloodspray", data );
}
