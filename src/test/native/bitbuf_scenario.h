// The bit-buffer scenario both halves of the bitbuf oracle run: pristine
// bitbuf.cpp (bitbuf_stock_oracle_test, which records it as
// src/test/client/Fixtures/bitbuf-stock.tsv) and client.so's members
// (bitbuf_client_test, which replays it). Seeded; the raw copies the client
// keeps (WriteBits, ReadBits), at every bit offset, from aligned and unaligned
// memory, and past the end. Only members both sides define are called, and the buffers
// are set up through bitbuf.h's public fields (the constructors are
// bitbuf.cpp's, which the client does not carry).

#pragma once

#include "tier1/bitbuf.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>

namespace bitbuf_scenario
{
struct Rng
{
	uint32_t s = 0x2545F491u;

	uint32_t Next()
	{
		s ^= s << 13;
		s ^= s >> 17;
		s ^= s << 5;
		return s;
	}

	int Pick(int lo, int hi) { return lo + (int)(Next() % (uint32_t)(hi - lo + 1)); }
};

const int kWords = 16;

struct Writer
{
	alignas(bf_write) unsigned char storage[sizeof(bf_write)];
	uint32 data[kWords];
	bf_write* w;

	Writer(int nBits, int start, Rng& r)
	{
		memset(storage, 0, sizeof(storage));
		for (int i = 0; i < kWords; i++)
			data[i] = r.Next(); // what was there before
		w = reinterpret_cast<bf_write*>(storage);
		w->m_pData = data;
		w->m_nDataBytes = nBits / 8;
		w->m_nDataBits = nBits;
		w->m_iCurBit = start;
	}
};

struct Reader
{
	alignas(bf_read) unsigned char storage[sizeof(bf_read)];
	bf_read* r;

	Reader(const uint32* data, int nBits, int start)
	{
		memset(storage, 0, sizeof(storage));
		r = reinterpret_cast<bf_read*>(storage);
		r->m_pData = reinterpret_cast<const unsigned char*>(data);
		r->m_nDataBytes = nBits / 8;
		r->m_nDataBits = nBits;
		r->m_iCurBit = start;
	}
};

inline void Hex(std::string& out, const void* p, int n)
{
	char b[4];
	for (int i = 0; i < n; i++)
	{
		snprintf(b, sizeof(b), "%02x", ((const unsigned char*)p)[i]);
		out += b;
	}
}

inline std::string Run()
{
	Rng r;
	std::string out = "# bitbuf scenario: case op nbits start | after: curbit overflow buffer [read bytes]\n";
	char line[160];
	for (int c = 0; c < 3000; c++)
	{
		const int nBits = 8 * r.Pick(4, kWords * 4);
		const int start = r.Pick(0, nBits);
		// 0 WriteBits, 5 a read only: the raw copies are what client.so keeps
		// (C9: every field-level encode is C#'s).
		const int op = r.Pick(0, 1) ? 0 : 5;
		Writer wr(nBits, start, r);
		unsigned char src[64 + 4];
		for (unsigned i = 0; i < sizeof(src); i++)
			src[i] = (unsigned char)r.Next();
		const int misalign = r.Pick(0, 3);
		int n = 0;
		bool ok = true;
		switch (op)
		{
			case 0:
				n = r.Pick(0, 8 * 60);
				ok = wr.w->WriteBits(src + misalign, n);
				break;
			case 5:
				break; // a read only
		}
		snprintf(line, sizeof(line), "W\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t", c, op, nBits, start, n, wr.w->m_iCurBit,
				 wr.w->IsOverflowed() ? 1 : 0);
		out += line;
		out += ok ? "1\t" : "0\t";
		Hex(out, wr.data, sizeof(wr.data));
		out += "\n";

		// Read back from a random point of the same words.
		const int rstart = r.Pick(0, nBits);
		Reader rd(wr.data, nBits, rstart);
		unsigned char dst[64 + 4];
		memset(dst, 0xA5, sizeof(dst));
		int rn = 0;
		rn = r.Pick(0, 8 * 60);
		rd.r->ReadBits(dst + r.Pick(0, 3), rn);
		snprintf(line, sizeof(line), "R\t%d\t%d\t%d\t%d\t%d\t", c, rstart, rn, rd.r->m_iCurBit,
				 rd.r->IsOverflowed() ? 1 : 0);
		out += line;
		Hex(out, dst, sizeof(dst));
		out += "\n";
	}
	return out;
}
} // namespace bitbuf_scenario
