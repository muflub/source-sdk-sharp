//========= Copyright Valve Corporation, All rights reserved. ============//
// Purpose: A class to wrap data for transport over a boundary like a thread
//			or window.

#pragma once
#define UTLENVELOPE_H

class CUtlDataEnvelope
{
public:
	CUtlDataEnvelope( const void *pData, int nBytes );
	CUtlDataEnvelope( const CUtlDataEnvelope &from );
	~CUtlDataEnvelope();

	operator void *();
	operator void *() const;

private:
	void Assign( const void *pData, int nBytes );
	void Assign( const CUtlDataEnvelope &from );
	void Purge();

	// TODO: switch to a reference counted array?
	union
	{
		byte *m_pData;
		byte m_data[4];
	};
	int m_nBytes;
};

inline void CUtlDataEnvelope::Assign( const void *pData, int nBytes )
{
	if ( pData )
	{
		m_nBytes = nBytes;
		if ( m_nBytes > 4 )
		{
			m_pData = new byte[nBytes];
			memcpy( m_pData, pData, nBytes );
		}
		else
		{
			memcpy( m_data, pData, nBytes );
		}
	}
	else
	{
		m_pData = NULL;
		m_nBytes = 0;
	}
}

inline void CUtlDataEnvelope::Assign( const CUtlDataEnvelope &from )
{
	Assign( from.operator void *(), from.m_nBytes );
}

inline void CUtlDataEnvelope::Purge()
{
	if (m_nBytes > 4)
		delete [] m_pData;
	m_nBytes = 0;
}

inline CUtlDataEnvelope::CUtlDataEnvelope( const void *pData, int nBytes )
{
	Assign( pData, nBytes );
}

inline CUtlDataEnvelope::CUtlDataEnvelope( const CUtlDataEnvelope &from )
{
	Assign( from );
}

inline CUtlDataEnvelope::~CUtlDataEnvelope()
{
	Purge();
}

inline CUtlDataEnvelope::operator void *()
{
	if ( !m_nBytes )
	{
		return NULL;
	}

	return ( m_nBytes > 4) ? m_pData : m_data;
}

inline CUtlDataEnvelope::operator void *() const
{
	if ( !m_nBytes )
	{
		return NULL;
	}

	return ( m_nBytes > 4) ? (void *)m_pData : (void *)m_data;
}
