//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// util.h helpers for server TUs that do not include the fork's util.h; never include
// both, as they declare the same helpers. Functions defined in our code keep the fork's
// signatures at global scope; replacements for the fork's inline helpers are in ss::.

#pragma once

#include "eiface.h"

#include "engine/IEngineTrace.h"

class IServerNetworkable;

// Defined in our TUs.
void UTIL_ClearTrace(trace_t& trace);
bool UTIL_ShouldShowBlood(int bloodColor);

namespace ss
{

// Case-insensitive string equality, true for the same pointer (NULL included).
inline bool FStrEq(const char* sz1, const char* sz2)
{
	return (sz1 == sz2 || V_stricmp(sz1, sz2) == 0);
}

} // namespace ss
