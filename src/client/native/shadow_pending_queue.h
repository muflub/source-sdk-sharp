// The shadow manager's pending queue: what entity shells tell the managed
// shadow manager between its calls, three int32 per entry, so the entries ride
// in the manager's next call.
//   - a dirty mark for an entity already marked ORs into that entry;
//   - a destroy drops the entity's earlier entries (the renderable may be freed);
//   - a full queue is handed to the sink before the next entry goes in;
//   - what leaves is a copy, so the reader may queue again while it reads.
#pragma once

class CShadowPendingQueue
{
public:
	enum
	{
		DIRTY = 1,
		REFRESH = 2,
		DESTROY = 3,
		WORDS = 3,
		// Sized above a busy map's per-frame peak.
		MAX = 512,
	};

	// Where a full queue, or one handed over before a call, goes.
	typedef void (*Sink_t)(const int32_t* pEntries, int32_t nCount, void* pContext);

	CShadowPendingQueue()
		: m_nCount(0)
		, m_nHandedOver(0)
		, m_pfnSink(0)
		, m_pSinkContext(0)
	{
	}

	void SetSink(Sink_t pfnSink, void* pContext)
	{
		m_pfnSink = pfnSink;
		m_pSinkContext = pContext;
	}

	void Dirty(uint32_t hEntity, int32_t bTextureDirty)
	{
		for (int i = 0; i < m_nCount; ++i)
		{
			int32_t* p = m_Entries + WORDS * i;
			if (p[0] == DIRTY && (uint32_t)p[1] == hEntity)
			{
				p[2] |= bTextureDirty ? 1 : 0;
				return;
			}
		}
		Append(DIRTY, hEntity, bTextureDirty ? 1 : 0);
	}

	void Refresh(uint32_t hEntity, int32_t nTypeAndFlags) { Append(REFRESH, hEntity, nTypeAndFlags); }

	void Destroy(uint32_t hEntity)
	{
		int nKept = 0;
		for (int i = 0; i < m_nCount; ++i)
		{
			const int32_t* p = m_Entries + WORDS * i;
			if ((uint32_t)p[1] == hEntity)
				continue;
			int32_t* q = m_Entries + WORDS * nKept++;
			q[0] = p[0];
			q[1] = p[1];
			q[2] = p[2];
		}
		m_nCount = nKept;
		Append(DESTROY, hEntity, 0);
	}

	// The entries, emptied, copied into pOut (WORDS * MAX words), for a call
	// that carries them in its arguments; their count. A copy, as the sink's
	// is: the call's work may queue again before it has read them all.
	int32_t Take(int32_t* pOut)
	{
		const int n = m_nCount;
		m_nCount = 0;
		for (int i = 0; i < WORDS * n; ++i)
			pOut[i] = m_Entries[i];
		return n;
	}

	// The entries to the sink, emptied; nothing when there are none. The sink
	// reads a copy, so what it does may queue again.
	void HandOver()
	{
		const int n = m_nCount;
		m_nCount = 0;
		if (n <= 0 || !m_pfnSink)
			return;
		int32_t copy[WORDS * MAX];
		for (int i = 0; i < WORDS * n; ++i)
			copy[i] = m_Entries[i];
		++m_nHandedOver;
		m_pfnSink(copy, n, m_pSinkContext);
	}

	// The entries dropped (no manager to tell: outside a load).
	void Clear() { m_nCount = 0; }

	int Count() const { return m_nCount; }

	const int32_t* Entries() const { return m_Entries; }

	int HandedOver() const { return m_nHandedOver; }

private:
	void Append(int32_t kind, uint32_t hEntity, int32_t value)
	{
		if (m_nCount == MAX)
			HandOver();
		if (m_nCount == MAX)
			m_nCount = 0; // no sink: the oldest go, as a call with no manager drops them
		int32_t* p = m_Entries + WORDS * m_nCount++;
		p[0] = kind;
		p[1] = (int32_t)hEntity;
		p[2] = value;
	}

	int m_nCount;
	int m_nHandedOver;
	Sink_t m_pfnSink;
	void* m_pSinkContext;
	int32_t m_Entries[WORDS * MAX];
};
