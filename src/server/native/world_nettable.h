// worldspawn's ServerClass, built once from the C# declaration. The DLL tier holds it:
// the props are resolved at DLL init and cannot move afterwards.

#pragma once

#include <cstdint>

class SendProp;
class SendTable;
class ServerClass;
class CSourceSharpPayloadPropNames;

// worldspawn's ServerClass, built at DLL init from the C# declaration
// (SourceSharp.Shared.WorldNetState).
class CWorldNetTable
{
public:
	CWorldNetTable()
		: m_nBlobBytes(0)
		, m_nWords(0)
		, m_pProps(NULL)
		, m_pTable(NULL)
		, m_pClass(NULL)
		, m_bWarnedUndeclared(false)
	{
	}

	int BlobBytes() const { return m_nBlobBytes; }

	ServerClass* Class() const { return m_pClass; }

	// C# declares worldspawn's blob size and this builds the ServerClass around it. Must
	// run inside DLL init, since the engine walks the server class list once. Returns the
	// payload word count, or -1 for a size no payload can name or a second declaration
	// that disagrees with the first.
	int32_t Declare(int32_t nBlobBytes, CSourceSharpPayloadPropNames& names);

	// What CWorld::GetServerClass() answers: NULL, warned once, until C# has declared;
	// CWorld then falls back to its base's.
	ServerClass* DeclaredClass();

	void WarnUndeclaredOnce();

private:
	int m_nBlobBytes;
	int m_nWords;
	SendProp* m_pProps;
	SendTable* m_pTable;
	ServerClass* m_pClass;
	bool m_bWarnedUndeclared;
};
