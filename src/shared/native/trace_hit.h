//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp ======================================================//
//
// WHAT A TRACE HIT, AS THE ENGINE WROTE IT: an IHandleEntity pointer.
//
// trace_t::m_pEnt holds the spatial partition's IHandleEntity for the object
// the trace struck. The engine stores exactly that, through a C-style cast to
// whatever the game's header calls the field's type. The patched gametrace.h
// types the field IHandleEntity *, which says so. Stock gametrace.h, which
// server.so is built on from plan_server_native_v2 D3, types it CBaseEntity *,
// and on this server CBaseEntity is the entity face (D1): a DIFFERENT
// subobject of a standalone entity, so reading the field as one would dispatch
// through the wrong vtable.
//
// So every read of the field that wants the hit goes through TraceHit(), which
// hands back the pointer the engine stored, typed as what it is. On the
// patched headers that is the field itself; on the stock server it is the same
// bits reinterpreted, never converted.
//
//=============================================================================//
#ifndef SOURCESHARP_TRACE_HIT_H
#define SOURCESHARP_TRACE_HIT_H
#ifdef _WIN32
#pragma once
#endif

#include "gametrace.h"

class IHandleEntity;

namespace ss
{

inline IHandleEntity *TraceHitHandle( IHandleEntity *pHit )
{
	return pHit;
}

#if defined( GAME_DLL ) && defined( SS_STOCK_PUBLIC_HEADERS )
// The field's stock type. Never a conversion: the bits are an IHandleEntity's.
inline IHandleEntity *TraceHitHandle( CBaseEntity *pHit )
{
	return reinterpret_cast< IHandleEntity * >( pHit );
}
#endif

// What the trace struck, or NULL for nothing.
inline IHandleEntity *TraceHit( const CGameTrace &tr )
{
	return TraceHitHandle( tr.m_pEnt );
}

} // namespace ss

#endif // SOURCESHARP_TRACE_HIT_H
