//========= SourceSharp. ============================================//
//
// Whether a message keyed by a 32-bit id is being said for the first time.
//
// The client shell used to keep a single "last id warned about" latch for the
// unpublished-type message, and a single value is exactly wrong for the way
// the ids arrive: two unpublished entities in the PVS take one update apiece
// per frame, so the two ids ALTERNATE, each arrival overwrites the latch with
// the other id, and both warn every update — 110 lines each in a short
// session, burying everything else in the log. A set remembers them all, so
// each distinct id is said exactly once whatever order the ids arrive in.
//
// Header-only and free of engine types, so it is unit-tested in
// sourcesharp/test/said_once_per_id_test.cpp the way the other engine-free
// rules are.
//
//===========================================================================//
#ifndef SOURCESHARP_SAID_ONCE_PER_ID_H
#define SOURCESHARP_SAID_ONCE_PER_ID_H
#ifdef _WIN32
#pragma once
#endif

class CSaidOncePerId
{
public:
	CSaidOncePerId() : m_nCount( 0 ) {}

	// True exactly once per distinct id. A full table answers true for every
	// id it can no longer record — repeating a message is the recoverable
	// failure, silently swallowing one is not — but the capacity is sized an
	// order of magnitude past the largest id population seen (a map's worth of
	// unpublished types is ~80), so filling it means something else is wrong.
	bool FirstTime( unsigned int nId )
	{
		for ( int i = 0; i < m_nCount; i++ )
		{
			if ( m_Ids[ i ] == nId )
				return false;
		}

		if ( m_nCount < (int)( sizeof( m_Ids ) / sizeof( m_Ids[ 0 ] ) ) )
			m_Ids[ m_nCount++ ] = nId;

		return true;
	}

	int Count() const { return m_nCount; }

private:
	unsigned int m_Ids[ 1024 ];
	int m_nCount;
};

#endif // SOURCESHARP_SAID_ONCE_PER_ID_H
