//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// trace_t::m_pEnt holds the engine's IHandleEntity pointer for the object hit. On the
// stock gametrace.h the field is typed CBaseEntity *, a different subobject here, so
// every read of the hit goes through TraceHit(), which reinterprets and never converts.
#pragma once

#include "gametrace.h"

class IHandleEntity;

namespace ss
{

inline IHandleEntity* TraceHitHandle(IHandleEntity* pHit)
{
	return pHit;
}

#include "igameentity_fwd.h"

#ifdef SS_GAME_ENTITY
// The field's stock type. Never a conversion: the bits are an IHandleEntity's.
inline IHandleEntity* TraceHitHandle(SS_GAME_ENTITY* pHit)
{
	return reinterpret_cast<IHandleEntity*>(pHit);
}
#endif

// What the trace struck, or NULL for nothing.
inline IHandleEntity* TraceHit(const CGameTrace& tr)
{
	return TraceHitHandle(tr.m_pEnt);
}

} // namespace ss
