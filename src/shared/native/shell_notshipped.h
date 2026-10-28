// Capabilities this build deliberately does not ship, named with a reason so a
// deliberate absence can be told from a broken export. Interface rows declare
// names the engine may ask client.so's factory for; SourceSharp_NotShipped()
// counts reaches of capability paths. Engine-free; the warning is defined by
// whoever links this.

#pragma once

#include <string.h>

// nTotal is the running total across all rows.
void ShellNotShipped_Warning( const char *pName, const char *pReason, int nTotal );

struct ShellNotShippedRow_t
{
	const char *pszName;
	const char *pszReason;
};

// Interfaces deliberately not exported. Each reason is what the console shows.
inline const ShellNotShippedRow_t *ShellNotShipped_Interfaces( int *pCount )
{
	static const ShellNotShippedRow_t s_Rows[] =
	{
		// The engine asks for this only when the mod directory is tf or tf_beta.
		{ "ClientReplay001",
		  "replay is not shipped (plan_entity.md 7g); the engine only asks for "
		  "it when the mod directory is tf or tf_beta" },
	};

	if ( pCount != NULL )
		*pCount = (int)( sizeof( s_Rows ) / sizeof( s_Rows[ 0 ] ) );

	return s_Rows;
}

// The declared row for an interface name, or NULL if the absence is not declared.
// Exact match: ClientReplay001 is a suffix of EngineClientReplay001.
inline const ShellNotShippedRow_t *ShellNotShipped_FindInterface( const char *pszName )
{
	if ( pszName == NULL )
		return NULL;

	int nCount = 0;
	const ShellNotShippedRow_t *pRows = ShellNotShipped_Interfaces( &nCount );

	for ( int i = 0; i < nCount; i++ )
	{
		if ( strcmp( pRows[ i ].pszName, pszName ) == 0 )
			return &pRows[ i ];
	}

	return NULL;
}

// Counts reaches per name and in total. Names are compared by strcmp, not pointer.
class CShellNotShipped
{
public:
	enum { kMaxNames = 32 };

	CShellNotShipped() : m_nTotal( 0 ), m_nNames( 0 ) {}

	// Something reached a path this build does not ship. Warns once per name,
	// since some paths run every frame; the count is unlimited.
	void Reached( const char *pName, const char *pReason )
	{
		m_nTotal++;

		bool bFirst = true;

		if ( pName != NULL )
		{
			int iRow = Find( pName );

			if ( iRow < 0 && m_nNames < kMaxNames )
			{
				iRow = m_nNames++;
				m_pNames[ iRow ] = pName;
				m_pReasons[ iRow ] = pReason;
				m_nCounts[ iRow ] = 0;
			}

			// An overflowed table still counts the total, and the name warns every time.
			if ( iRow >= 0 )
			{
				bFirst = ( m_nCounts[ iRow ] == 0 );
				m_nCounts[ iRow ]++;
			}
		}

		if ( bFirst )
			ShellNotShipped_Warning( pName, pReason, m_nTotal );
	}

	// Zero means nothing reached a not-shipped path.
	int Total() const { return m_nTotal; }

	int NameCount() const { return m_nNames; }

	const char *NameAt( int i ) const
	{
		return ( i >= 0 && i < m_nNames ) ? m_pNames[ i ] : NULL;
	}

	const char *ReasonAt( int i ) const
	{
		return ( i >= 0 && i < m_nNames ) ? m_pReasons[ i ] : NULL;
	}

	int CountAt( int i ) const
	{
		return ( i >= 0 && i < m_nNames ) ? m_nCounts[ i ] : 0;
	}

	int CountOf( const char *pName ) const
	{
		const int iRow = Find( pName );
		return ( iRow < 0 ) ? 0 : m_nCounts[ iRow ];
	}

private:
	int Find( const char *pName ) const
	{
		if ( pName == NULL )
			return -1;

		for ( int i = 0; i < m_nNames; i++ )
		{
			if ( strcmp( m_pNames[ i ], pName ) == 0 )
				return i;
		}

		return -1;
	}

	int          m_nTotal;
	int          m_nNames;
	const char  *m_pNames[ kMaxNames ];
	const char  *m_pReasons[ kMaxNames ];
	int          m_nCounts[ kMaxNames ];
};
