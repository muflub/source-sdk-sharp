//===== SourceSharp — the voice ban oracle's file system =====================//
//
// voice_banmgr.cpp reads and writes voice_ban.dt through `filesystem`. This is
// one in-memory file, so the oracle can hand stock a file's bytes and read back
// the bytes stock writes. Only the five calls the subject makes exist.
//
//=============================================================================//
#pragma once

#include <cstring>
#include <string>

typedef void *FileHandle_t;

class COracleFileSystem
{
public:
	std::string m_Data;      // the file's bytes
	bool m_bExists = false;
	size_t m_nPos = 0;

	FileHandle_t Open( const char *pName, const char *pMode, const char *pPath = 0 )
	{
		m_strLastName = pName;
		if ( pMode[ 0 ] == 'w' )
		{
			m_Data.clear();
			m_bExists = true;
		}
		else if ( !m_bExists )
		{
			return 0;
		}
		m_nPos = 0;
		return this;
	}

	int Read( void *pOut, int nSize, FileHandle_t )
	{
		const size_t n = m_nPos + nSize <= m_Data.size() ? (size_t)nSize : m_Data.size() - m_nPos;
		memcpy( pOut, m_Data.data() + m_nPos, n );
		m_nPos += n;
		return (int)n;
	}

	int Write( const void *pIn, int nSize, FileHandle_t )
	{
		m_Data.append( (const char *)pIn, nSize );
		return nSize;
	}

	unsigned int Size( FileHandle_t ) { return (unsigned int)m_Data.size(); }
	void Close( FileHandle_t ) {}

	std::string m_strLastName;
};

extern COracleFileSystem *filesystem;
