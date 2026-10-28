// worldspawn's ServerClass, built once from the C# declaration. The DLL tier holds it:
// the props are resolved at DLL init and cannot move afterwards.

#pragma once

#include <cstdint>

class SendProp;
class SendTable;
class ServerClass;

class CWorldNetTable
{
public:
	CWorldNetTable()
		: m_nBlobBytes( 0 )
		, m_nWords( 0 )
		, m_pProps( NULL )
		, m_pTable( NULL )
		, m_pClass( NULL )
		, m_bWarnedUndeclared( false )
	{
	}

	int BlobBytes() const { return m_nBlobBytes; }

	ServerClass *Class() const { return m_pClass; }

	int32_t Declare( int32_t nBlobBytes );
	void WarnUndeclaredOnce();

private:
	int m_nBlobBytes;
	int m_nWords;
	SendProp *m_pProps;
	SendTable *m_pTable;
	ServerClass *m_pClass;
	bool m_bWarnedUndeclared;
};
