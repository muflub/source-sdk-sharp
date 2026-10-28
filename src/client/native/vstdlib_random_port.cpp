//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — vstdlib's random stream, owned by client.so =========//
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
//
// plan_server_novalve.md P4 (the P0 random probe, section 7) wrote this for
// server.so, whose callers needed vstdlib's ALGORITHM, not its process-wide
// instance. The server stopped drawing; the file is client.so's now
// (plan_client_novalve C9, 2026-10-12), one client-owned instance of the same
// generator for randoverride.cpp's rand(), bone_setup's attachment pick,
// util_shared_library's seeded draws, viewpostprocess and particles.a.
// vstdlib_random_oracle_test holds it to the game's own libvstdlib.so, draw
// for draw.
//
// The generator is Park-Miller's minimal standard (multiplier 16807, modulus
// 2^31 - 1, by Schrage's method) behind a 32-entry Bays-Durham shuffle --
// Numerical Recipes' ran1, which is what vstdlib's CUniformRandomStream is.
// Seeding stores the seed negated, so the first draw after RandomSeed warms
// the table up again. Defined with hidden visibility, as the tier0 ports are.
//=============================================================================//

// vstdlib.h declares these VSTDLIB_INTERFACE, dllimport on Windows, and MSVC
// exports a definition of a dllimport function from the DLL that holds it. Read
// as plain C declarations here, so client.dll exports none of them
// (game_dll_exports_test); every other file's import binds to these through
// SS_BIND_IMPORT below.
#if defined( _MSC_VER )
	#undef VSTDLIB_INTERFACE
	#define VSTDLIB_INTERFACE extern "C"
#endif

#include <math.h>
#include <mutex>
#include "ss_platform.h"	// SS_BIND_IMPORT
#include "vstdlib/random.h"
#ifdef _MSC_VER
// This file defines functions the engine DLLs' headers declare dllimport
// (C4273); SS_BIND_IMPORT at the end binds this module's calls to them.
#pragma warning( disable : 4273 )
#endif

namespace
{
enum
{
	kA = 16807,          // multiplier
	kM = 2147483647,     // modulus, 2^31 - 1
	kQ = 127773,         // kM / kA
	kR = 2836,           // kM % kA
	kTable = 32,
	kDiv = 1 + ( kM - 1 ) / kTable,
};
const unsigned kMaxRandomRange = 0x7FFFFFFFu;
const double kScale = 1.0 / kM;
const double kMaxFraction = 1.0 - 1.2e-7;   // the largest fraction a draw maps to

class CStream
{
public:
	void Seed( int iSeed )
	{
		std::lock_guard< std::mutex > hold( m_Lock );
		m_nState = iSeed < 0 ? iSeed : -iSeed;
		m_nLast = 0;
	}

	int Draw()
	{
		std::lock_guard< std::mutex > hold( m_Lock );
		return DrawLocked();
	}

	// A fraction in [0, kMaxFraction], held in a float before any scaling.
	float Fraction()
	{
		float fl = (float)( kScale * Draw() );
		if ( fl > kMaxFraction )
			fl = (float)kMaxFraction;
		return fl;
	}

	// iLow + a uniform pick of the x = iHigh - iLow + 1 values, by rejecting
	// the draws above the largest multiple of x so none is favoured.
	int Pick( int iLow, int iHigh )
	{
		const unsigned x = (unsigned)iHigh - (unsigned)iLow + 1u;
		if ( x <= 1 || kMaxRandomRange < x - 1 )
			return iLow;

		const unsigned maxAcceptable = kMaxRandomRange - ( ( kMaxRandomRange + 1u ) % x );
		unsigned n;
		do
		{
			n = (unsigned)Draw();
		} while ( n > maxAcceptable );
		return iLow + (int)( n % x );
	}

private:
	// One step of the minimal standard: state = kA * state mod kM.
	static int Step( int n )
	{
		const int k = n / kQ;
		n = kA * ( n - k * kQ ) - kR * k;
		return n < 0 ? n + kM : n;
	}

	int DrawLocked()
	{
		if ( m_nState <= 0 || m_nLast == 0 )
		{
			// (Re)seeded: a non-positive state is a seed waiting. Warm up
			// eight steps, then fill the table from the next thirty-two.
			m_nState = -m_nState < 1 ? 1 : -m_nState;
			for ( int j = kTable + 7; j >= 0; --j )
			{
				m_nState = Step( m_nState );
				if ( j < kTable )
					m_Table[ j ] = m_nState;
			}
			m_nLast = m_Table[ 0 ];
		}

		m_nState = Step( m_nState );
		int j = m_nLast / kDiv;
		if ( j < 0 || j >= kTable )
			j = ( j % kTable ) & 0x7fffffff;
		m_nLast = m_Table[ j ];
		m_Table[ j ] = m_nState;
		return m_nLast;
	}

	std::mutex m_Lock;
	int m_nState = 0;    // seeded with 0, as vstdlib's global stream is
	int m_nLast = 0;
	int m_Table[ kTable ] = {};
};

CStream s_Stream;
}

void RandomSeed( int iSeed )
{
	s_Stream.Seed( iSeed );
}

float RandomFloat( float flMinVal, float flMaxVal )
{
	return s_Stream.Fraction() * ( flMaxVal - flMinVal ) + flMinVal;
}

int RandomInt( int iMinVal, int iMaxVal )
{
	return s_Stream.Pick( iMinVal, iMaxVal );
}

// Every call in this module of the functions above, made through the
// import pointer, reaches these definitions (ss_platform.h).
SS_BIND_IMPORT( RandomSeed )
SS_BIND_IMPORT( RandomFloat )
SS_BIND_IMPORT( RandomInt )
