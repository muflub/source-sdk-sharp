// One side's engine console lines, buffered for managed to drain. nSeq is the
// side's single line counter; compare numbers by (int32_t)(a - b). The lock is
// held only for one copy or swap. A line that does not fit is counted in
// nOverflow. The layout is an ABI read by NativeConsoleBuffer.cs.

#pragma once

#include <stddef.h>

#include <atomic>
#include <thread>
#include <type_traits>

/// Bytes in each of the two halves: 2 x 512 KiB = 1 MiB per side.
static const uint32_t kConsoleLineHalfBytes = 512u * 1024u;

/// At offset 0 of the buffer; the halves follow it at offset 32.
struct ConsoleLineBufferHeader
{
	std::atomic<uint32_t> nSeq{0}; // the last number taken; the next line takes nSeq + 1
	uint32_t nOverflow = 0; // lines that did not fit since the last swap
	std::atomic<int32_t> nLock{0}; // 0 free, 1 held: a copy or a swap, nothing longer
	int32_t nActive = 0; // the half producers append to (0 or 1)
	uint32_t nUsed[2] = {0, 0}; // bytes written into each half
	uint32_t nHalfBytes = 0; // kConsoleLineHalfBytes, for the reader (Publish)
	uint32_t nReserved = 0;
};

// Managed takes the lock with Interlocked on the same four bytes and reads the
// counter at offset 0, so both atomics must be lock-free plain 32-bit words.
static_assert(std::atomic<uint32_t>::is_always_lock_free && sizeof(std::atomic<uint32_t>) == 4,
			  "the line counter is read by managed as a plain uint32");
static_assert(std::atomic<int32_t>::is_always_lock_free && sizeof(std::atomic<int32_t>) == 4,
			  "the lock word is taken by managed with Interlocked on a plain int32");
static_assert(std::is_standard_layout<ConsoleLineBufferHeader>::value,
			  "the header is an ABI: managed reads it at fixed offsets");

/// One line in a half: this, then nLength bytes of UTF-8 (no NUL), then
/// padding to the next multiple of 4.
struct ConsoleLineRecord
{
	uint32_t nSeq;
	uint32_t nLength;
	uint32_t nWarning; // 1: the decorated (warning) channel
};

inline uint32_t ConsoleLineRecordBytes(uint32_t nLength)
{
	return ((uint32_t)sizeof(ConsoleLineRecord) + nLength + 3u) & ~3u;
}

class CConsoleLineBuffer
{
public:
	CConsoleLineBuffer() = default; // all zero, so a global one lands in .bss

	void Publish() { m_Header.nHalfBytes = kConsoleLineHalfBytes; } // before the reader has the address

	CConsoleLineBuffer(const CConsoleLineBuffer&) = delete;
	CConsoleLineBuffer& operator=(const CConsoleLineBuffer&) = delete;

	/// The line in, numbered. False when it did not fit (counted in nOverflow).
	bool Append(const char* pText, int nWarning)
	{
		const size_t nLen = strlen(pText);
		Lock();
		// Under the lock, so the increment need order nothing by itself.
		const uint32_t nSeq = m_Header.nSeq.fetch_add(1, std::memory_order_relaxed) + 1;
		const int nHalf = m_Header.nActive;
		const uint32_t nUsed = m_Header.nUsed[nHalf];
		const uint64_t nNeed = nLen > 0xFFFFFFF0u ? UINT64_MAX : ConsoleLineRecordBytes((uint32_t)nLen);
		if (nNeed > (uint64_t)(kConsoleLineHalfBytes - nUsed))
		{
			++m_Header.nOverflow;
			Unlock();
			return false;
		}
		uint8_t* pAt = Half(nHalf) + nUsed;
		ConsoleLineRecord rec = {nSeq, (uint32_t)nLen, nWarning ? 1u : 0u};
		memcpy(pAt, &rec, sizeof(rec));
		memcpy(pAt + sizeof(rec), pText, nLen);
		m_Header.nUsed[nHalf] = nUsed + (uint32_t)nNeed;
		Unlock();
		return true;
	}

	void Lock()
	{
		for (int nSpins = 0; m_Header.nLock.exchange(1, std::memory_order_acquire) != 0;)
		{
			while (m_Header.nLock.load(std::memory_order_relaxed) != 0)
			{
				if (++nSpins > 64)
					std::this_thread::yield();
			}
		}
	}

	void Unlock() { m_Header.nLock.store(0, std::memory_order_release); }

	uint8_t* Half(int nHalf) { return m_Halves + (size_t)nHalf * kConsoleLineHalfBytes; }

	ConsoleLineBufferHeader m_Header;
	uint8_t m_Halves[2 * kConsoleLineHalfBytes];
};
