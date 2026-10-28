//========= Copyright Valve Corporation, All rights reserved. ============//

#pragma once

#ifdef _WIN32
#include <intrin.h>
#endif

#include <assert.h>
#include "tier0/platform.h"

PLATFORM_INTERFACE uint64 g_ClockSpeed;

PLATFORM_INTERFACE double g_ClockSpeedMicrosecondsMultiplier;
PLATFORM_INTERFACE double g_ClockSpeedMillisecondsMultiplier;
PLATFORM_INTERFACE double g_ClockSpeedSecondsMultiplier;

class CCycleCount
{
friend class CFastTimer;

public:
					CCycleCount();
					CCycleCount( uint64 cycles );

	void			Sample();	// Sample the clock. This takes about 34 clocks to execute (or 26,000 calls per millisecond on a P900).

	void			Init();		// Set to zero.
	void			Init( float initTimeMsec );
	void			Init( double initTimeMsec )		{ Init( (float)initTimeMsec ); }
	void			Init( uint64 cycles );
	bool			IsLessThan( CCycleCount const &other ) const;					// Compare two counts.

	uint64			GetLongCycles() const;

	double			GetMillisecondsF() const;

	CCycleCount&	operator+=( CCycleCount const &other );

	// dest = rSrc1 + rSrc2
	static void		Add( CCycleCount const &rSrc1, CCycleCount const &rSrc2, CCycleCount &dest );	// Add two samples together.
	
	uint64			m_Int64;
};

class PLATFORM_CLASS CClockSpeedInit
{
public:
	CClockSpeedInit()
	{
		Init();
	}

	static void Init();
};

class CFastTimer
{
public:
	// These functions are fast to call and should be called from your sampling code.
	void				Start();
	void				End();

	const CCycleCount &	GetDuration() const;	// Get the elapsed time between Start and End calls.

private:
	CCycleCount	m_Duration;
};

// This is a helper class that times whatever block of code it's in
class CTimeScope
{
public:
				CTimeScope( CFastTimer *pTimer );
				~CTimeScope();

private:	
	CFastTimer	*m_pTimer;
};

inline CTimeScope::CTimeScope( CFastTimer *pTotal )
{
	m_pTimer = pTotal;
	m_pTimer->Start();
}

inline CTimeScope::~CTimeScope()
{
	m_pTimer->End();
}

// This is a helper class that times whatever block of code it's in and
// adds the total (int microseconds) to a global counter.
class CTimeAdder
{
public:
				CTimeAdder( CCycleCount *pTotal );
				~CTimeAdder();

	void		End();

private:	
	CCycleCount	*m_pTotal;
	CFastTimer	m_Timer;
};

inline CTimeAdder::CTimeAdder( CCycleCount *pTotal )
{
	m_pTotal = pTotal;
	m_Timer.Start();
}

inline CTimeAdder::~CTimeAdder()
{
	End();
}

inline void CTimeAdder::End()
{
	if( m_pTotal )
	{
		m_Timer.End();
		*m_pTotal += m_Timer.GetDuration();
		m_pTotal = 0;
	}
}

// Simple tool to support timing a block of code, and reporting the results on
// program exit or at each iteration
//	Macros used because dbg.h uses this header, thus Msg() is unavailable

class CAverageCycleCounter
{
public:
	CAverageCycleCounter();
	
	void Init();
	void MarkIter( const CCycleCount &duration );
	
private:
	unsigned	m_nIters;
	CCycleCount m_Total;
	CCycleCount	m_Peak;
};

class CAverageTimeMarker
{
public:
	CAverageTimeMarker( CAverageCycleCounter *pCounter );
	~CAverageTimeMarker();
	
private:
	CAverageCycleCounter *m_pCounter;
	CFastTimer	m_Timer;
};

// CCycleCount inlines.

inline CCycleCount::CCycleCount()
{
	Init( (uint64)0 );
}

inline CCycleCount::CCycleCount( uint64 cycles )
{
	Init( cycles );
}

inline void CCycleCount::Init()
{
	Init( (uint64)0 );
}

inline void CCycleCount::Init( float initTimeMsec )
{
	if ( g_ClockSpeedMillisecondsMultiplier > 0 )
		Init( (uint64)(initTimeMsec / g_ClockSpeedMillisecondsMultiplier) );
	else
		Init( (uint64)0 );
}

inline void CCycleCount::Init( uint64 cycles )
{
	m_Int64 = cycles;
}

inline void CCycleCount::Sample()
{
	m_Int64 = Plat_Rdtsc();
}

inline CCycleCount& CCycleCount::operator+=( CCycleCount const &other )
{
	m_Int64 += other.m_Int64;
	return *this;
}

inline void CCycleCount::Add( CCycleCount const &rSrc1, CCycleCount const &rSrc2, CCycleCount &dest )
{
	dest.m_Int64 = rSrc1.m_Int64 + rSrc2.m_Int64;
}

inline bool CCycleCount::IsLessThan(CCycleCount const &other) const
{
	return m_Int64 < other.m_Int64;
}

inline uint64 CCycleCount::GetLongCycles() const
{
	return m_Int64;
}

inline double CCycleCount::GetMillisecondsF() const
{
	return (double)( m_Int64 * g_ClockSpeedMillisecondsMultiplier );
}

// CFastTimer inlines.
inline void CFastTimer::Start()
{
	m_Duration.Sample();
}

inline void CFastTimer::End()
{
	CCycleCount cnt;
	cnt.Sample();
	m_Duration.m_Int64 = cnt.m_Int64 - m_Duration.m_Int64;

}

inline CCycleCount const& CFastTimer::GetDuration() const
{
	return m_Duration;
}

// CAverageCycleCounter inlines

inline CAverageCycleCounter::CAverageCycleCounter()
 :	m_nIters( 0 )
{
}

inline void CAverageCycleCounter::Init()
{
	m_Total.Init();
	m_Peak.Init();
	m_nIters = 0;
}

inline void CAverageCycleCounter::MarkIter( const CCycleCount &duration )
{
	++m_nIters;
	m_Total += duration;
	if ( m_Peak.IsLessThan( duration ) )
		m_Peak = duration;
}

inline CAverageTimeMarker::CAverageTimeMarker( CAverageCycleCounter *pCounter )
{
	m_pCounter = pCounter;
	m_Timer.Start();
}

inline CAverageTimeMarker::~CAverageTimeMarker()
{
	m_Timer.End();
	m_pCounter->MarkIter( m_Timer.GetDuration() );
}

// CLimitTimer
// Use this to time whether a desired interval of time has passed.  It's extremely fast
// to check while running.  NOTE: CMicroSecOverage() and CMicroSecLeft() are not as fast to check.
class CLimitTimer
{
public:
	CLimitTimer() = default;
	CLimitTimer( uint64 cMicroSecDuration ) { SetLimit( cMicroSecDuration ); }
	void SetLimit( uint64 m_cMicroSecDuration );

private:
	uint64 m_lCycleLimit;
};

// Purpose: Initializes the limit timer with a period of time to measure.
// Input  : cMicroSecDuration -		How long a time period to measure
inline void CLimitTimer::SetLimit( uint64 cMicroSecDuration )
{
	uint64 dlCycles = ( ( uint64 ) cMicroSecDuration * g_ClockSpeed ) / ( uint64 ) 1000000L;
	CCycleCount cycleCount;
	cycleCount.Sample( );
	m_lCycleLimit = cycleCount.GetLongCycles( ) + dlCycles;
}
