//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the trace filter, built natively (libss_fwd_server) ======//
// SS_REASON a  a vtable the engine calls through
//
// plan_server_native_v2 T4f (plan_server_native_min P3f, Decision 2: managed).
// IEngineTrace::TraceRay takes an ITraceFilter * and calls ShouldHitEntity on
// it once per candidate entity, mid-trace, through the filter's own C++
// vtable, so the object it is handed is built here, deriving ITraceFilter, and
// ShouldHitEntity calls one managed-built object whose table managed wrote
// (IManagedTraceFilter, generated into imanaged_physics_callbacks.h from
// docs/managed-interfaces.tsv). WHAT A TRACE MAY HIT IS MANAGED'S DECISION:
// the rule (stock's CTraceFilterSimple: the shape clause, the pass entity and
// its owner, the collision-group matrix) is SourceSharp.Server.Interop's
// TraceFilterRules, and this class only hands it the candidate.
//
// THE CANDIDATE CROSSES AS ITS HANDLE. A pointer would mean nothing to managed,
// which reads no vtable; the handle is what the engine's IHandleEntity answers
// for itself, and it names a static prop too (staticpropmgr's serial).
//
// THE TRACE TYPE IS MANAGED'S TOO, HANDED OVER ONCE. The engine asks
// GetTraceType about four times a trace (measured: 6.3 crossings a server frame
// at rest on dm_lockdown against 1.0 for ShouldHitEntity), and the answer is
// fixed for the filter's life, so managed chooses it when it builds the object
// and the object answers it from the block's third reserved word.
//
// ITraceFilter has no destructor, so unlike the recipient filter this class's
// vtable names no operator delete. It is freed with its managed block.
//=============================================================================//
#include "ss_fwd_sdk.h"
#include "fwd_trace_filter.h"

#include <new>
#include <stddef.h>

#include "engine/IEngineTrace.h"
#include "ihandleentity.h"
#include "basehandle.h"
#include "imanaged_physics_callbacks.h"
#include "trace_layout.h"   // the Ray_t and trace_t managed builds and reads

namespace
{
	class SSTraceFilter final : public ITraceFilter
	{
	public:
		SSTraceFilter( IManagedTraceFilter *pTarget, TraceType_t eType ) : m_pTarget( pTarget ), m_eType( eType ) {}

		bool ShouldHitEntity( IHandleEntity *pEntity, int contentsMask ) override
		{
			return m_pTarget->ShouldHitEntity( SSFwd_TraceFilter_HandleOf( pEntity ), contentsMask ) != 0;
		}

		TraceType_t GetTraceType() const override { return m_eType; }

	private:
		IManagedTraceFilter *m_pTarget;
		TraceType_t m_eType;
	};

	// A vptr, one pointer and the trace type: the block's reserved words hold it.
	static_assert( sizeof( SSTraceFilter ) <= kManagedPhysicsCallbackWords * sizeof( void * ), "fits the block's reserved words" );
}

int SSFwd_TraceFilter_SizeOf()
{
	return int( kManagedPhysicsCallbackWords * sizeof( void * ) );
}

void *SSFwd_TraceFilter_Construct( void *pMemory, IManagedTraceFilter *pTarget, int32_t nTraceType )
{
	if ( !pMemory || !pTarget )
		return nullptr;

	return static_cast< ITraceFilter * >( new ( pMemory ) SSTraceFilter( pTarget, (TraceType_t)nTraceType ) );
}

uint32_t SSFwd_TraceFilter_HandleOf( IHandleEntity *pEntity )
{
	if ( !pEntity )
		return INVALID_EHANDLE_INDEX;

	return (uint32_t)pEntity->GetRefEHandle().ToInt();
}
