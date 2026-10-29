//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON f  a bit-level wire codec
// worldspawn's ClientClass, built from the C# declaration; the twin of the
// server's shell_world_nettable.cpp.

#include "client_class.h"

#include "shell_world_nettable_client.h"
#include "generic_payload.h"

// Everything this file remembers, as fields of one function-local static.
class CWorldNetTableClient
{
public:
	CWorldNetTableClient()
		: m_nBlobBytes(0)
		, m_nWords(0)
		, m_pProps(NULL)
		, m_pTable(NULL)
		, m_pClass(NULL)
		, m_bWarnedUndeclared(false)
	{
	}

	int BlobBytes() const { return m_nBlobBytes; }

	ClientClass* Class() const { return m_pClass; }

	int32_t Declare(int32_t nBlobBytes);
	void WarnUndeclaredOnce();

private:
	int m_nBlobBytes;
	int m_nWords;
	RecvProp* m_pProps;
	RecvTable* m_pTable;
	ClientClass* m_pClass;
	bool m_bWarnedUndeclared;
};

static CWorldNetTableClient& WorldNetTable()
{
	static CWorldNetTableClient s_Table;
	return s_Table;
}

// The table, built once, prop for prop the server's: the generic payload at
// this side's blob base. The class name "CWorld" and table name "DT_World" are
// stock's spellings (the server's table is "DT_WORLD"); the engine pairs the
// sides by server class name.
int32_t CWorldNetTableClient::Declare(int32_t nBlobBytes)
{
	if (m_pClass != NULL)
	{
		// A managed reload declares the same number again, silently. A
		// different one is refused, because the props are resolved.
		if (nBlobBytes == m_nBlobBytes)
			return m_nWords;

		Warning("[SourceSharp] worldspawn(client): %d declared blob byte(s) disagrees with "
				"the %d a table has already been built against; REFUSED\n",
				nBlobBytes, m_nBlobBytes);
		return -1;
	}

	const int nWords = SourceSharpPayload_WordCount(nBlobBytes);
	const int nBase = SourceSharp_WorldBlobOffset();

	if (nWords < 0 || !SourceSharpPayload_OffsetsFit(nBase, nWords))
	{
		Warning("[SourceSharp] worldspawn(client): %d declared blob byte(s) will not fit at "
				"blob base %d; NO table is built and the world's update will not pair\n",
				nBlobBytes, nBase);
		return -1;
	}

	// No base table: the world is not a C_BaseEntity, and the server sends none.
	// Stock forced model index 1, SOLID_BSP, the origin and the angles for
	// entity 0 anyway; C_World and its collideable answer those directly.
	m_pProps = new RecvProp[nWords > 0 ? nWords : 1];

	if (nWords > 0 && SourceSharpPayload_BuildRecvProps(m_pProps, nWords, nBase) != nWords)
	{
		Warning("[SourceSharp] worldspawn(client): payload build refused %d word(s) at blob "
				"base %d; no table built\n",
				nWords, nBase);
		delete[] m_pProps;
		m_pProps = NULL;
		return -1;
	}

	m_nBlobBytes = nBlobBytes;
	m_nWords = nWords;

	m_pTable = new RecvTable(m_pProps, nWords, "DT_World");
	m_pClass = new ClientClass("CWorld", SourceSharp_ClientWorldFactory, NULL, m_pTable);

	// The recv half of the fast local transfer: a listen server checks both ends.
	int nSlow = 0;

	for (int i = 0; i < nWords; i++)
	{
		if (!SourceSharpPayload_IsFastRecvProxy(m_pTable->GetProp(i)))
			nSlow++;
	}

	Msg("[SourceSharp] worldspawn(client) -> 'CWorld' / 'DT_World': no base table + %d "
		"payload word(s) = %d prop(s), %d blob byte(s) at blob base %d\n",
		nWords, nWords, nBlobBytes, nBase);

	if (nSlow > 0)
	{
		Warning("[SourceSharp] worldspawn(client): %d of %d payload word(s) did NOT resolve "
				"to m_Int32ToInt32 and would be copied slowly\n",
				nSlow, nWords);
	}

	return nWords;
}

void CWorldNetTableClient::WarnUndeclaredOnce()
{
	if (m_bWarnedUndeclared)
		return;

	m_bWarnedUndeclared = true;

	Warning("[SourceSharp] worldspawn(client) has NO ClientClass of its own: the managed side "
			"never called WorldNetDeclare, so SourceSharp.Shared.WorldNetState was not "
			"declared here. The world's update will not pair and the renderer will read "
			"zeroed world bounds, occlusion areas and detail sprite material\n");
}

int SourceSharp_WorldBlobBytes()
{
	return WorldNetTable().BlobBytes();
}

int32_t SourceSharp_WorldNetDeclare(int32_t nBlobBytes)
{
	return WorldNetTable().Declare(nBlobBytes);
}

ClientClass* SourceSharp_WorldClientClass()
{
	ClientClass* pClass = WorldNetTable().Class();

	if (pClass == NULL)
		WorldNetTable().WarnUndeclaredOnce();

	return pClass;
}
