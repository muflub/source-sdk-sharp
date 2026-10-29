// The payload tiers: one SendTable per size rather than per class. The list is a
// fixed constant outside any handshake, so a managed type can change at runtime
// as long as it stays inside its tier. Managed reads this list through
// NetTierInfo. No engine headers, so it can be tested.
#pragma once

#include "generic_payload.h"

#include <cstddef>

// Which native shell hosts a managed type. The values are an ABI (NetShellKind
// in NetSchema.cs): 0 is reserved and refused by SourceSharp_NetPublishType.
enum SourceSharpNetShell
{
	// Reserved; nothing may publish on it.
	SS_SHELL_NETENTITY = 0,

	// Every entity is allocated one blob whatever its class, so the tier is the
	// shell's, not the type's.
	SS_SHELL_STANDALONE = 1,
};

// One tier: the class and table names the two sides pair on, and its payload bytes.
struct SourceSharpTier_t
{
	// Literals, because a SendProp keeps the pointer for the life of the process.
	const char* pClass;
	const char* pTable;

	int nBytes;
};

// Real props in front of the payload: the rows of StandaloneNetFields, which
// standalone_nettable.h static_asserts.
enum
{
	SOURCESHARP_TIER_PROPS_STANDALONE = 12,
};

// Takes nShell because it is part of the ABI.
inline int SourceSharp_TierRealProps(int nShell)
{
	(void)nShell;
	return SOURCESHARP_TIER_PROPS_STANDALONE;
}

// The tier list's bound, for sizing tier-indexed arrays. The list below is
// declared with it, so a row too many fails to compile.
enum
{
	SOURCESHARP_TIER_MAX = 7
};

// The tiers, smallest first and searched in order. Each row doubles, so a class
// pays at most twice its size. 2048 is the largest that fits MAX_DATATABLE_PROPS.
inline const SourceSharpTier_t* SourceSharp_Tiers(int* pOutCount)
{
	static const SourceSharpTier_t s_Tiers[SOURCESHARP_TIER_MAX] = {
		{"CSourceSharpStdEnt32", "DT_SourceSharpStdEnt32", 32},
		{"CSourceSharpStdEnt64", "DT_SourceSharpStdEnt64", 64},
		{"CSourceSharpStdEnt128", "DT_SourceSharpStdEnt128", 128},
		{"CSourceSharpStdEnt256", "DT_SourceSharpStdEnt256", 256},
		{"CSourceSharpStdEnt512", "DT_SourceSharpStdEnt512", 512},
		{"CSourceSharpStdEnt1024", "DT_SourceSharpStdEnt1024", 1024},
		{"CSourceSharpStdEnt2048", "DT_SourceSharpStdEnt2048", 2048},
	};

	if (pOutCount)
		*pOutCount = (int)(sizeof(s_Tiers) / sizeof(s_Tiers[0]));

	return s_Tiers;
}

inline int SourceSharp_TierCount()
{
	int nCount = 0;
	SourceSharp_Tiers(&nCount);
	return nCount;
}

// The index of the smallest tier that fits nBytes, or -1; never clamps, since a
// too-small tier would drop the type's tail from the wire. Zero bytes is tier 0.
inline int SourceSharp_TierForSize(int nBytes)
{
	if (nBytes < 0)
		return -1;

	int nCount = 0;
	const SourceSharpTier_t* pTiers = SourceSharp_Tiers(&nCount);

	for (int i = 0; i < nCount; i++)
	{
		if (nBytes <= pTiers[i].nBytes)
			return i;
	}

	return -1;
}

// The tier nBytes lands in, or NULL.
inline const SourceSharpTier_t* SourceSharp_TierForSizePtr(int nBytes)
{
	const int iTier = SourceSharp_TierForSize(nBytes);
	if (iTier < 0)
		return NULL;

	return SourceSharp_Tiers(NULL) + iTier;
}

// A tier's payload bytes, or 0 for an invalid index (callers use it as a length).
inline int SourceSharp_TierBytes(int iTier)
{
	if (iTier < 0 || iTier >= SourceSharp_TierCount())
		return 0;

	return SourceSharp_Tiers(NULL)[iTier].nBytes;
}

// A tier's payload props.
inline int SourceSharp_TierWords(int iTier)
{
	if (iTier < 0 || iTier >= SourceSharp_TierCount())
		return -1;

	return SourceSharpPayload_WordCount(SourceSharp_Tiers(NULL)[iTier].nBytes);
}

// A tier's names. Datatable names match case-insensitively, so the stem is
// "StdEnt" to keep clear of DT_SourceSharpStandalone.
inline const char* SourceSharp_TierClassName(int nShell, int iTier)
{
	(void)nShell;

	if (iTier < 0 || iTier >= SourceSharp_TierCount())
		return 0;

	return SourceSharp_Tiers(0)[iTier].pClass;
}

inline const char* SourceSharp_TierTableName(int nShell, int iTier)
{
	(void)nShell;

	if (iTier < 0 || iTier >= SourceSharp_TierCount())
		return 0;

	return SourceSharp_Tiers(0)[iTier].pTable;
}

// FNV-1a over the classname: m_nTypeId, computed independently on each side.
// Must match NetSchema.TypeIdOf.
inline unsigned int SourceSharp_HashClassName(const char* pName)
{
	const unsigned int nOffsetBasis = 2166136261u;
	const unsigned int nPrime = 16777619u;

	unsigned int nHash = nOffsetBasis;

	for (const char* p = pName; p && *p; p++)
	{
		nHash ^= (unsigned int)(unsigned char)*p;
		nHash *= nPrime;
	}

	return nHash;
}

// The published type table, built at startup from C#'s publish calls on each
// side. It maps type id to classname and schema hash, and name to tier. Hash
// collisions are refused here.
enum
{
	SOURCESHARP_TYPES_MAX = 512
};

enum
{
	SOURCESHARP_TYPE_NAME_BYTES = 64
};

struct SourceSharpNetType_t
{
	char szClass[SOURCESHARP_TYPE_NAME_BYTES];
	unsigned int nTypeId;
	unsigned int nSchemaHash;

	// The class's own layout size, not its tier's payload.
	int nLayoutBytes;

	int nShell;
	int iTier;
};

class CSourceSharpTypeTable
{
public:
	CSourceSharpTypeTable()
		: m_nCount(0)
		, m_nCollisions(0)
	{
	}

	// Records a type. Returns the index, or -1 for an oversized name, a full table
	// or a hash collision. Republishing a name updates its row.
	int Publish(const char* pClassName, int nShell, unsigned int nSchemaHash, int nLayoutBytes, int iTier)
	{
		if (!pClassName || !pClassName[0])
			return -1;

		int nLen = 0;
		while (pClassName[nLen])
			nLen++;

		if (nLen >= SOURCESHARP_TYPE_NAME_BYTES)
			return -1;

		const unsigned int nId = SourceSharp_HashClassName(pClassName);

		for (int i = 0; i < m_nCount; i++)
		{
			if (Same(m_Types[i].szClass, pClassName))
			{
				m_Types[i].nSchemaHash = nSchemaHash;
				m_Types[i].nLayoutBytes = nLayoutBytes;
				m_Types[i].nShell = nShell;
				m_Types[i].iTier = iTier;
				return i;
			}

			// Two different names with one id: refused, or one would decode as the other.
			if (m_Types[i].nTypeId == nId)
			{
				m_nCollisions++;
				return -1;
			}
		}

		if (m_nCount >= SOURCESHARP_TYPES_MAX)
			return -1;

		SourceSharpNetType_t& type = m_Types[m_nCount];

		for (int i = 0; i <= nLen; i++)
			type.szClass[i] = pClassName[i];

		type.nTypeId = nId;
		type.nSchemaHash = nSchemaHash;
		type.nLayoutBytes = nLayoutBytes;
		type.nShell = nShell;
		type.iTier = iTier;

		return m_nCount++;
	}

	const SourceSharpNetType_t* ById(unsigned int nTypeId) const
	{
		for (int i = 0; i < m_nCount; i++)
		{
			if (m_Types[i].nTypeId == nTypeId)
				return m_Types + i;
		}

		return 0;
	}

	const SourceSharpNetType_t* ByName(const char* pClassName) const
	{
		if (!pClassName)
			return 0;

		for (int i = 0; i < m_nCount; i++)
		{
			if (Same(m_Types[i].szClass, pClassName))
				return m_Types + i;
		}

		return 0;
	}

	int Count() const { return m_nCount; }

	int Collisions() const { return m_nCollisions; }

	const SourceSharpNetType_t* At(int i) const { return (i >= 0 && i < m_nCount) ? m_Types + i : 0; }

	void Reset()
	{
		m_nCount = 0;
		m_nCollisions = 0;
	}

private:
	// Case-sensitive, because the id hashes the exact bytes.
	static bool Same(const char* pA, const char* pB)
	{
		while (*pA && *pA == *pB)
		{
			pA++;
			pB++;
		}

		return *pA == *pB;
	}

	SourceSharpNetType_t m_Types[SOURCESHARP_TYPES_MAX];
	int m_nCount;
	int m_nCollisions;
};

// Whether a live entity's stamped schema hash must be redone after a managed
// reload. Unknown classes and unchanged hashes are left alone; on a change the
// caller also zeroes the blob, whose bytes were written through the old layout.
inline bool SourceSharp_SchemaNeedsRestamp(const CSourceSharpTypeTable& types, const char* pClassName,
										   unsigned int nStamped, unsigned int* pOutWanted)
{
	if (pOutWanted)
		*pOutWanted = nStamped;

	const SourceSharpNetType_t* pType = types.ByName(pClassName);

	if (!pType)
		return false;

	if (pOutWanted)
		*pOutWanted = pType->nSchemaHash;

	return pType->nSchemaHash != nStamped;
}

#ifdef CLIENT_DLL
// The client's table; the server's is its CServerNetTables'.
CSourceSharpTypeTable& SourceSharp_NetTypes();
#endif
