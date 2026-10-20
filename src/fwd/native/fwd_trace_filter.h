//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the trace filter's functions, as the table names them =====//
//
// plan_server_native_v2 T4f. fwd_trace_filter.cpp writes these and the server
// library's generated table (tools/engine-forwarders' HAND row TraceFilter)
// lays them out, in this order. They are the library's own: its version script
// exports none of them.
//=============================================================================//
#ifndef FWD_TRACE_FILTER_H
#define FWD_TRACE_FILTER_H
#pragma once

#include <stdint.h>

class IManagedTraceFilter;
class IHandleEntity;

// How many bytes Construct builds in: at most the words a managed block
// reserves for it (kManagedPhysicsCallbackWords), which managed holds this to.
int SSFwd_TraceFilter_SizeOf();

// Builds, in pMemory (SizeOf bytes, pointer-aligned: the block's reserved
// words), the ITraceFilter IEngineTrace::TraceRay calls back for every
// candidate entity, over its managed side, and answers it as that interface;
// its GetTraceType answers nTraceType (a TraceType_t), which managed chose.
// NULL, and nothing built, without memory or a target.
void *SSFwd_TraceFilter_Construct( void *pMemory, IManagedTraceFilter *pTarget, int32_t nTraceType );

// The handle of an entity the engine named (a trace_t's m_pEnt), as
// CBaseHandle::ToInt packs it, or INVALID_EHANDLE_INDEX for none. The engine's
// IHandleEntity answers it through its own vtable; managed reads no vtable.
uint32_t SSFwd_TraceFilter_HandleOf( IHandleEntity *pEntity );

#endif // FWD_TRACE_FILTER_H
