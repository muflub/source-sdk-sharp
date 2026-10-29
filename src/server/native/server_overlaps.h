// The frame's overlap pairs: who overlaps whom, as the engine's partition and the
// physics solver report it. Level tier: every handle in it names an entity of this
// map, so it dies with the map.

#pragma once

#include <cstdint>
#include <vector>

class CServerOverlaps
{
public:
	CServerOverlaps() = default;
	CServerOverlaps(const CServerOverlaps&) = delete;
	CServerOverlaps& operator=(const CServerOverlaps&) = delete;

	// A ceiling, not a size: the list is drained every frame, and only grows
	// unbounded when managed code never reads it.
	static constexpr int kMaxPairs = 8192;

	// Accumulates the engine's reports throughout the frame.
	std::vector<uint32_t> m_Pending;

	// The frame's answer, built once and not consumed by reading: a second ask must
	// see the same pairs, or the diff reads a departure. Flat handle pairs, element
	// 2n and 2n+1, the shape the ABI hands over.
	std::vector<uint32_t> m_Report;
	int m_nReportFrame = -1;

	// Pairs the solver holds open: reported every frame until it says they ended.
	std::vector<uint32_t> m_SolverHeld;

	bool m_bOverflowed = false;

	// Appends a pair once; the engine can report the same pair twice in a frame.
	// True only on the append that first finds the list full, for the caller to say so.
	bool Append(std::vector<uint32_t>& list, uint32_t hOne, uint32_t hTwo)
	{
		if (hOne == kInvalid || hTwo == kInvalid || hOne == hTwo)
			return false;

		if ((int)list.size() >= kMaxPairs * 2)
		{
			const bool bFirst = !m_bOverflowed;
			m_bOverflowed = true;
			return bFirst;
		}

		for (size_t i = 0; i + 1 < list.size(); i += 2)
		{
			if ((list[i] == hOne && list[i + 1] == hTwo) || (list[i] == hTwo && list[i + 1] == hOne))
				return false;
		}

		list.push_back(hOne);
		list.push_back(hTwo);
		return false;
	}

	bool DrainPending(std::vector<uint32_t>& into)
	{
		bool bOverflowed = false;

		for (size_t i = 0; i + 1 < m_Pending.size(); i += 2)
			bOverflowed |= Append(into, m_Pending[i], m_Pending[i + 1]);

		m_Pending.clear();
		return bOverflowed;
	}

	void Forget(std::vector<uint32_t>& list, uint32_t hOne, uint32_t hTwo)
	{
		for (size_t i = 0; i + 1 < list.size(); i += 2)
		{
			if ((list[i] == hOne && list[i + 1] == hTwo) || (list[i] == hTwo && list[i + 1] == hOne))
			{
				list.erase(list.begin() + i, list.begin() + i + 2);
				return;
			}
		}
	}

	void Clear()
	{
		m_Pending.clear();
		m_Report.clear();
		m_SolverHeld.clear();
		m_nReportFrame = -1;
		m_bOverflowed = false;
	}

private:
	// INVALID_EHANDLE_INDEX.
	static constexpr uint32_t kInvalid = 0xFFFFFFFFu;
};
