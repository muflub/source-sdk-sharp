//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// IClientEntityList's counts, maintained by bind and unbind. Header-only and
// engine-free so the native tests drive the class the client links.
// GetHighestEntityIndex bounds the engine's own walks: too small hides entities,
// and the engine-invisible range (2048 and up) must not count towards it.

#pragma once

class CClientEntityCounts
{
public:
	// A reference, not a copy: RecomputeHighest asks which entries are still bound.
	explicit CClientEntityCounts(const CSourceSharpSlotTable& table)
		: m_Table(table)
		, m_nMaxServerEnts(0)
		, m_nHighestUsed(-1)
		, m_nNetworkedBound(0)
		, m_nNonNetworkedBound(0)
	{
	}

	// Called only after the table accepted a bind, never for a refused one.
	void OnBound(int nEntry)
	{
		if (!CSourceSharpSlotTable::IsEntry(nEntry))
			return;

		if (CSourceSharpSlotTable::IsEdictEntry(nEntry))
		{
			++m_nNetworkedBound;

			if (nEntry > m_nHighestUsed)
				m_nHighestUsed = nEntry;
		}
		else
		{
			// The engine-invisible range: counted for NumberOfEntities( true ) but not in
			// the highest index, which bounds an engine walk that stops at MAX_EDICTS.
			++m_nNonNetworkedBound;
		}
	}

	// Call after the table's Unbind: RecomputeHighest reads the table.
	void OnUnbound(int nEntry)
	{
		if (!CSourceSharpSlotTable::IsEntry(nEntry))
			return;

		if (CSourceSharpSlotTable::IsEdictEntry(nEntry))
		{
			--m_nNetworkedBound;

			// >=, so an entry somehow above the recorded high-water mark still recomputes.
			if (nEntry >= m_nHighestUsed)
				RecomputeHighest();
		}
		else
		{
			--m_nNonNetworkedBound;
		}
	}

	// -1 when nothing is bound, as stock's CClientEntityList answers.
	int HighestEntityIndex() const { return m_nHighestUsed; }

	int NumberOfEntities(bool bIncludeNonNetworkable) const
	{
		return bIncludeNonNetworkable ? m_nNetworkedBound + m_nNonNetworkedBound : m_nNetworkedBound;
	}

	void SetMaxEntities(int nMaxEnts) { m_nMaxServerEnts = nMaxEnts; }

	int MaxEntities() const { return m_nMaxServerEnts; }

private:
	// Removing the highest entity can lower it by any amount, so it is recomputed,
	// as stock's RecomputeHighestEntityUsed does.
	void RecomputeHighest()
	{
		m_nHighestUsed = -1;

		for (int i = MAX_EDICTS - 1; i >= 0; i--)
		{
			if (m_Table.IsBound(i))
			{
				m_nHighestUsed = i;
				break;
			}
		}
	}

	CClientEntityCounts(const CClientEntityCounts&);
	CClientEntityCounts& operator=(const CClientEntityCounts&);

	const CSourceSharpSlotTable& m_Table;

	int m_nMaxServerEnts;
	int m_nHighestUsed;
	int m_nNetworkedBound;
	int m_nNonNetworkedBound;
};
