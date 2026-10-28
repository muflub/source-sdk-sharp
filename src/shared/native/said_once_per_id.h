// Whether a message keyed by a 32-bit id is being said for the first time.
#pragma once

class CSaidOncePerId
{
public:
	CSaidOncePerId()
		: m_nCount( 0 )
	{
	}

	// True exactly once per distinct id. A full table answers true for ids it can
	// no longer record: repeating a message beats swallowing one.
	bool FirstTime( unsigned int nId )
	{
		for ( int i = 0; i < m_nCount; i++ )
		{
			if ( m_Ids[i] == nId )
				return false;
		}

		if ( m_nCount < (int)( sizeof( m_Ids ) / sizeof( m_Ids[0] ) ) )
			m_Ids[m_nCount++] = nId;

		return true;
	}

	int Count() const { return m_nCount; }

private:
	unsigned int m_Ids[1024];
	int m_nCount;
};
