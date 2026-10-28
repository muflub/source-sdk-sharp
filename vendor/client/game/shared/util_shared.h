//========= Copyright Valve Corporation, All rights reserved. ============//
// Purpose: Shared util code between client and server.

#pragma once

#include "mathlib/vector.h"
#include "cmodel.h"
#include "utlvector.h"
#include "networkvar.h"
#include "engine/IEngineTrace.h"
#include "engine/IStaticPropMgr.h"
#include "shared_classnames.h"
#include "steam/steamuniverse.h"

#include "cdll_client_int.h"

// Forward declarations
class CGameTrace;
class CBasePlayer;
typedef CGameTrace trace_t;

// Language IDs.
#define LANGUAGE_ENGLISH				0
#define LANGUAGE_GERMAN					1

// Standard collision filters...
bool PassServerEntityFilter( const IHandleEntity *pTouch, const IHandleEntity *pPass );
bool StandardFilterRules( IHandleEntity *pHandleEntity, int fContentsMask );

typedef bool (*ShouldHitFunc_t)( IHandleEntity *pHandleEntity, int contentsMask );

// traceline methods
class CTraceFilterSimple : public CTraceFilter
{
public:
	// It does have a base, but we'll never network anything below here..
	DECLARE_CLASS_NOBASE( CTraceFilterSimple );

	CTraceFilterSimple( const IHandleEntity *passentity, int collisionGroup, ShouldHitFunc_t pExtraShouldHitCheckFn = NULL );
	virtual bool ShouldHitEntity( IHandleEntity *pHandleEntity, int contentsMask );
	virtual void SetPassEntity( const IHandleEntity *pPassEntity ) { m_pPassEnt = pPassEntity; }
	virtual void SetCollisionGroup( int iCollisionGroup ) { m_collisionGroup = iCollisionGroup; }

private:
	const IHandleEntity *m_pPassEnt;
	int m_collisionGroup;
	ShouldHitFunc_t m_pExtraShouldHitCheckFunction;

};

class CTraceFilterSkipTwoEntities : public CTraceFilterSimple
{
public:
	// It does have a base, but we'll never network anything below here..
	DECLARE_CLASS( CTraceFilterSkipTwoEntities, CTraceFilterSimple );
	
	CTraceFilterSkipTwoEntities( const IHandleEntity *passentity, const IHandleEntity *passentity2, int collisionGroup );
	virtual bool ShouldHitEntity( IHandleEntity *pHandleEntity, int contentsMask );
	virtual void unused_SetPassEntity2() = 0;

private:
	const IHandleEntity *m_pPassEnt2;
};

class CTraceFilterSkipClassname : public CTraceFilterSimple
{
public:
	CTraceFilterSkipClassname( const IHandleEntity *passentity, const char *pchClassname, int collisionGroup );
	virtual bool ShouldHitEntity( IHandleEntity *pHandleEntity, int contentsMask );

private:

	const char *m_pchClassname;
};

class CTraceFilterSkipTwoClassnames : public CTraceFilterSkipClassname
{
public:
	// It does have a base, but we'll never network anything below here..
	DECLARE_CLASS( CTraceFilterSkipTwoClassnames, CTraceFilterSkipClassname );

	CTraceFilterSkipTwoClassnames( const IHandleEntity *passentity, const char *pchClassname, const char *pchClassname2, int collisionGroup );
	virtual bool ShouldHitEntity( IHandleEntity *pHandleEntity, int contentsMask );

private:
	const char *m_pchClassname2;
};

// helper
void DebugDrawLine( const Vector& vecAbsStart, const Vector& vecAbsEnd, int r, int g, int b, bool test, float duration );

extern ConVar r_visualizetraces;

inline void UTIL_TraceLine( const Vector& vecAbsStart, const Vector& vecAbsEnd, unsigned int mask, 
					 const IHandleEntity *ignore, int collisionGroup, trace_t *ptr )
{
	Ray_t ray;
	ray.Init( vecAbsStart, vecAbsEnd );
	CTraceFilterSimple traceFilter( ignore, collisionGroup );

	enginetrace->TraceRay( ray, mask, &traceFilter, ptr );

	if( r_visualizetraces.GetBool() )
	{
		DebugDrawLine( ptr->startpos, ptr->endpos, 255, 0, 0, true, -1.0f );
	}
}

inline void UTIL_TraceLine( const Vector& vecAbsStart, const Vector& vecAbsEnd, unsigned int mask, 
					 ITraceFilter *pFilter, trace_t *ptr )
{
	Ray_t ray;
	ray.Init( vecAbsStart, vecAbsEnd );

	enginetrace->TraceRay( ray, mask, pFilter, ptr );

	if( r_visualizetraces.GetBool() )
	{
		DebugDrawLine( ptr->startpos, ptr->endpos, 255, 0, 0, true, -1.0f );
	}
}

inline void UTIL_TraceHull( const Vector &vecAbsStart, const Vector &vecAbsEnd, const Vector &hullMin, 
					 const Vector &hullMax,	unsigned int mask, const IHandleEntity *ignore, 
					 int collisionGroup, trace_t *ptr )
{
	Ray_t ray;
	ray.Init( vecAbsStart, vecAbsEnd, hullMin, hullMax );
	CTraceFilterSimple traceFilter( ignore, collisionGroup );

	enginetrace->TraceRay( ray, mask, &traceFilter, ptr );

	if( r_visualizetraces.GetBool() )
	{
		DebugDrawLine( ptr->startpos, ptr->endpos, 255, 255, 0, true, -1.0f );
	}
}

inline void UTIL_TraceHull( const Vector &vecAbsStart, const Vector &vecAbsEnd, const Vector &hullMin, 
					 const Vector &hullMax,	unsigned int mask, ITraceFilter *pFilter, trace_t *ptr )
{
	Ray_t ray;
	ray.Init( vecAbsStart, vecAbsEnd, hullMin, hullMax );

	enginetrace->TraceRay( ray, mask, pFilter, ptr );

	if( r_visualizetraces.GetBool() )
	{
		DebugDrawLine( ptr->startpos, ptr->endpos, 255, 255, 0, true, -1.0f );
	}
}

inline void UTIL_TraceRay( const Ray_t &ray, unsigned int mask, 
						  const IHandleEntity *ignore, int collisionGroup, trace_t *ptr, ShouldHitFunc_t pExtraShouldHitCheckFn = NULL )
{
	CTraceFilterSimple traceFilter( ignore, collisionGroup, pExtraShouldHitCheckFn );

	enginetrace->TraceRay( ray, mask, &traceFilter, ptr );
	
	if( r_visualizetraces.GetBool() )
	{
		DebugDrawLine( ptr->startpos, ptr->endpos, 255, 0, 0, true, -1.0f );
	}
}

// Old style, non-particle system, tracers
void		UTIL_Tracer( const Vector &vecStart, const Vector &vecEnd, int iEntIndex = 0, int iAttachment = TRACER_DONT_USE_ATTACHMENT, float flVelocity = 0, bool bWhiz = false, const char *pCustomTracerName = NULL, int iParticleID = 0 );

bool		UTIL_IsLowViolence();
bool		UTIL_ShouldShowBlood( int bloodColor );
void		UTIL_BloodDrips( const Vector &origin, const Vector &direction, int color, int amount );

void		UTIL_BloodImpact( const Vector &pos, const Vector &dir, int color, int amount );

/**
 * Given a position and a ray, return the shortest distance between the two.
 * If 'pos' is beyond either end of the ray, the returned distance is negated.
 */

/**
* Macro for creating an interface that when inherited from automatically maintains a list of instances
* that inherit from that interface.
*/

// interface for entities that want to a auto maintained global list

// Creates the auto add/remove constructor/destructor...
// Pass false to the constructor to not auto add

/**
 * Simple class for tracking intervals of game time.
 * Upon creation, the timer is invalidated.  To measure time intervals, start the timer via Start().
 */
class IntervalTimer
{
public:
	IntervalTimer()
	{
		m_timestamp = -1.0f;
	}

	void Reset()
	{
		m_timestamp = Now();
	}		

private:
	float m_timestamp;
	float Now() const;		// work-around since client header doesn't like inlined gpGlobals->curtime
};

/**
 * Simple class for counting down a short interval of time.
 * Upon creation, the timer is invalidated.  Invalidated countdown timers are considered to have elapsed.
 */
class CountdownTimer
{
public:
	CountdownTimer()
	{
		m_timestamp = -1.0f;
		m_duration = 0.0f;
	}

	void Reset()
	{
		m_timestamp = Now() + m_duration;
	}		

private:
	float m_duration;
	float m_timestamp;
	virtual float Now() const;		// work-around since client header doesn't like inlined gpGlobals->curtime
};

class RealTimeCountdownTimer : public CountdownTimer
{
	virtual float Now() const OVERRIDE
	{
		return Plat_FloatTime();
	}
};

// Holidays

/*EHoliday*/ ;
