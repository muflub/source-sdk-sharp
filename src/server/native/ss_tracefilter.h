//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// ss::CTraceFilterSimple: util_shared.h's simple trace filter, server.so's own
// (plan_server_novalve.md P5, lane E), for TUs on the R5 include set.
//
// The engine sees a trace filter only through ITraceFilter's vtable
// (public/engine/IEngineTrace.h), so the class is server.so's business and
// takes an ss:: name rather than a second body under Valve's. Its two
// out-of-line members are defined in trace_filter_library.cpp beside
// ::CTraceFilterSimple's, and both run one file-static body: the filter logic
// exists once. ::CTraceFilterSimple stays for the legacy TUs and the client
// until the last legacy user moves.
//
// Fields and constructor are the fork class's. GetPassEntity and the two
// virtual setters are inline, as they were there.
//=============================================================================//

#ifndef SS_TRACEFILTER_H
#define SS_TRACEFILTER_H
#ifdef _WIN32
#pragma once
#endif

#include "engine/IEngineTrace.h"

class IHandleEntity;

namespace ss
{

// A caller's extra say on each candidate; false rejects it.
typedef bool (*ShouldHitFunc_t)( IHandleEntity *pHandleEntity, int contentsMask );

class CTraceFilterSimple : public CTraceFilter
{
public:
	CTraceFilterSimple( const IHandleEntity *passentity, int collisionGroup,
	                    ShouldHitFunc_t pExtraShouldHitCheckFn = NULL );

	// The standard rules, the pass entity, then the game entity's own say and
	// the rules' collision-group pairing. trace_filter_library.cpp.
	virtual bool ShouldHitEntity( IHandleEntity *pHandleEntity, int contentsMask );

	virtual void SetPassEntity( const IHandleEntity *pPassEntity ) { m_pPassEnt = pPassEntity; }
	virtual void SetCollisionGroup( int iCollisionGroup )          { m_collisionGroup = iCollisionGroup; }
	const IHandleEntity *GetPassEntity( void )                     { return m_pPassEnt; }

private:
	const IHandleEntity *m_pPassEnt;
	int                  m_collisionGroup;
	ShouldHitFunc_t      m_pExtraShouldHitCheckFunction;
};

} // namespace ss

#endif // SS_TRACEFILTER_H
