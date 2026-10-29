//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON f  a bit-level wire codec
// The client's half of publishing a managed type and reporting a tier, plus a
// walk of the recv tables. The server's half is in shell_standalone.cpp; the two
// must stay mirrors, or a type published on one side has no pair on the other.

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"

#include "client_entity.h"

#include "shell_standalone_client.h"

#include "generic_nettable.h"

#include "sourcesharp.h"

// One tier as this side registered it, reporting the props native code built.
int32_t SourceSharp_NetTierInfo(int32_t nShell, int32_t iTier, const char** ppOutTable, int32_t* pOutBytes,
								int32_t* pOutWords, int32_t* pOutProps, int32_t* pOutLive)
{
	// The tiers are registered at DLL init and SourceSharp_StandaloneTierIsInUse
	// forces them again; the rest is compile-time constants.
	if (iTier < 0 || iTier >= SourceSharp_TierCount())
		return 0;

	const int nWords = SourceSharp_TierWords(iTier);
	const int nReal = SourceSharp_TierRealProps(nShell);

	if (ppOutTable)
		*ppOutTable = SourceSharp_TierTableName(nShell, iTier);
	if (pOutBytes)
		*pOutBytes = SourceSharp_Tiers(NULL)[iTier].nBytes;
	if (pOutWords)
		*pOutWords = nWords;
	if (pOutProps)
		*pOutProps = nReal + nWords;

	// "Live" is the tier entities are created through; every tier is registered so
	// a type can grow into a bigger one, so a registered empty tier is normal.
	if (pOutLive)
		*pOutLive = SourceSharp_StandaloneTierIsInUse(iTier) ? 1 : 0;

	return 1;
}

// Publishes one managed type, client half. Must mirror the server's: an
// arriving entity's type id is resolved through this table.
int32_t SourceSharp_NetPublishType(const char* pClassName, int32_t nShell, uint32_t nSchemaHash, int32_t nLayoutBytes,
								   uint32_t* pOutTypeId)
{
	if (pOutTypeId)
		*pOutTypeId = pClassName ? SourceSharp_HashClassName(pClassName) : 0;

	if (!pClassName || !pClassName[0])
		return -1;

	// SS_SHELL_NETENTITY has no shell and is refused by name. Must mirror the server's.
	if (nShell == SS_SHELL_NETENTITY)
	{
		Warning("[SourceSharp] type '%s': shell %d is the RETIRED runtime-networked "
				"entity, which no longer exists; NOT published. A managed build "
				"publishing on it is older than this native one.\n",
				pClassName, (int)nShell);
		return -1;
	}

	// Both sides pick the tier by the same rule from the same declared size, so no
	// tier goes on the wire. Must mirror the server's.
	const int iTier = SourceSharp_TierForSize(nLayoutBytes);

	if (iTier < 0)
	{
		Warning("[SourceSharp] type '%s': %d layout byte(s) exceeds the largest tier; "
				"NOT published\n",
				pClassName, nLayoutBytes);
		return -1;
	}

	if (SourceSharp_NetTypes().Publish(pClassName, nShell, nSchemaHash, nLayoutBytes, iTier) < 0)
	{
		Warning("[SourceSharp] type '%s' (id 0x%08x) was REFUSED by the type table: either a "
				"name collision with a type already published, or the table is full "
				"(%d entries). Entities of it cannot be paired with a managed class.\n",
				pClassName, SourceSharp_HashClassName(pClassName), SOURCESHARP_TYPES_MAX);
		return -1;
	}

	return iTier;
}

// The client's recv tables as the engine walks them, one prop per index: every
// table reachable from the ClientClass list once, props in table order. Answers
// the total; an index past the end fills nothing. Walked per call, not cached.
static void CopyName(char* pOut, int32_t nBytes, const char* pName)
{
	if (pOut && nBytes > 0)
		V_strncpy(pOut, pName ? pName : "", nBytes);
}

int32_t SourceSharp_ClientRecvProp(int32_t iIndex, char* pOutClass, int32_t nClassBytes, char* pOutTable,
								   int32_t nTableBytes, char* pOutProp, int32_t nPropBytes, int32_t* pOutType,
								   int32_t* pOutFlags, int32_t* pOutOffset, int32_t* pOutElements)
{
	CUtlVector<RecvTable*> tables;
	CUtlVector<const char*> owners;

	for (ClientClass* pClass = g_pClientClassHead; pClass; pClass = pClass->m_pNext)
	{
		if (!pClass->m_pRecvTable || tables.Find(pClass->m_pRecvTable) >= 0)
			continue;

		tables.AddToTail(pClass->m_pRecvTable);
		owners.AddToTail(pClass->GetName());
	}

	// Breadth over the nested tables, appended as they are found.
	for (int t = 0; t < tables.Count(); t++)
	{
		RecvTable* pTable = tables[t];
		for (int i = 0; i < pTable->GetNumProps(); i++)
		{
			RecvTable* pNested = pTable->GetProp(i)->GetDataTable();
			if (pNested && tables.Find(pNested) < 0)
			{
				tables.AddToTail(pNested);
				owners.AddToTail("");
			}
		}
	}

	int32_t nTotal = 0;
	for (int t = 0; t < tables.Count(); t++)
	{
		RecvTable* pTable = tables[t];
		for (int i = 0; i < pTable->GetNumProps(); i++, nTotal++)
		{
			if (nTotal != iIndex)
				continue;

			const RecvProp* pProp = pTable->GetProp(i);
			CopyName(pOutClass, nClassBytes, owners[t]);
			CopyName(pOutTable, nTableBytes, pTable->GetName());
			CopyName(pOutProp, nPropBytes, pProp->GetName());
			if (pOutType)
				*pOutType = (int32_t)pProp->GetType();
			if (pOutFlags)
				*pOutFlags = pProp->GetFlags();
			if (pOutOffset)
				*pOutOffset = pProp->GetOffset();
			if (pOutElements)
				*pOutElements = pProp->GetNumElements();
		}
	}

	return nTotal;
}

// Answers no refusals; kept because the shim tables only append.
int32_t SourceSharp_ClientSchemaRefusal(int32_t iIndex, uint32_t* pOutTypeId, uint32_t* pOutExpected,
										uint32_t* pOutSeen, int32_t* pOutCount, char* pOutClass, int32_t nClassBytes,
										int32_t* pOutRefusedUpdates)
{
	(void)iIndex;
	(void)pOutTypeId;
	(void)pOutExpected;
	(void)pOutSeen;
	(void)pOutCount;
	(void)pOutClass;
	(void)nClassBytes;

	if (pOutRefusedUpdates)
		*pOutRefusedUpdates = 0;

	return 0;
}
