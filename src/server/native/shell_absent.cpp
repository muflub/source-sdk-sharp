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

uint32_t SourceSharp_HandleFromEntity(IGameEntity* pEntity);
class CFunctor;
