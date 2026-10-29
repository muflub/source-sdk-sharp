//========= SourceSharp — the user-message append, against bf_write's own ===//
// plan_server_novalve.md P2.2. server.so's user messages are C# payloads
// appended by SourceSharp_AppendBits (usermessage_append.h), where they were
// bf_write's WriteByte/WriteShort/WriteFloat/WriteString. This links tier1 --
// the stock writers -- and holds the two to the same bits, at every starting
// bit offset a message can begin at. The payload bytes are the C# payloads'
// pinned layouts (UserMessageGoldenBytesTests), so a stock bf_read hook reads
// what C# wrote.
//=============================================================================//

#include "tier0/platform.h"
#include "tier1/bitbuf.h"
#include "usermessage_append.h"

#include <gtest/gtest.h>

#include <string.h>

namespace
{

struct Pair
{
	unsigned char stock[64];
	unsigned char ours[64];
	bf_write a, b;

	Pair(int nPrefixBits, unsigned int prefix)
	{
		memset(stock, 0xA5, sizeof(stock));
		memset(ours, 0xA5, sizeof(ours));
		a.StartWriting(stock, sizeof(stock));
		b.StartWriting(ours, sizeof(ours));
		if (nPrefixBits > 0)
		{
			a.WriteUBitLong(prefix, nPrefixBits);
			b.WriteUBitLong(prefix, nPrefixBits);
		}
	}

	void Same(const char* what, int nOffset)
	{
		ASSERT_EQ(a.GetNumBitsWritten(), b.GetNumBitsWritten()) << what << " at offset " << nOffset;
		EXPECT_EQ(0, memcmp(stock, ours, (a.GetNumBitsWritten() + 7) / 8))
			<< what << " differs at bit offset " << nOffset;
	}
};

const unsigned int kPrefix = 0x5A3C96E1u;

} // namespace

TEST(UserMessageAppend, TheShakePayloadIsBitIdenticalAtEveryOffset)
{
	for (int off = 0; off < 32; off++)
	{
		Pair p(off, kPrefix);
		p.a.WriteByte(1);
		p.a.WriteFloat(12.5f);
		p.a.WriteFloat(-3.25f);
		p.a.WriteFloat(0.1f);

		// Shake { Command = 1, Amplitude = 12.5, Frequency = -3.25, Duration = 0.1 }
		float floats[3] = {12.5f, -3.25f, 0.1f};
		unsigned char payload[13];
		payload[0] = 1;
		memcpy(payload + 1, floats, sizeof(floats));
		SourceSharp_AppendBits(&p.b, payload, (int)sizeof(payload) * 8);

		p.Same("Shake", off);
	}
}

TEST(UserMessageAppend, TheFadePayloadIsBitIdenticalAtEveryOffset)
{
	for (int off = 0; off < 32; off++)
	{
		Pair p(off, kPrefix);
		p.a.WriteShort(1500);
		p.a.WriteShort(-2);
		p.a.WriteShort(0x0011);
		p.a.WriteByte(255);
		p.a.WriteByte(0);
		p.a.WriteByte(128);
		p.a.WriteByte(7);

		// Fade { Duration = 1500, HoldTime = 0xFFFE, Flags = 0x11, Color = (255, 0, 128, 7) }
		const unsigned char payload[10] = {0xDC, 0x05, 0xFE, 0xFF, 0x11, 0x00, 255, 0, 128, 7};
		SourceSharp_AppendBits(&p.b, payload, (int)sizeof(payload) * 8);

		p.Same("Fade", off);
	}
}

TEST(UserMessageAppend, TheCloseCaptionPayloadIsBitIdenticalAtEveryOffset)
{
	for (int off = 0; off < 32; off++)
	{
		Pair p(off, kPrefix);
		p.a.WriteString("hl2mp.combine.alert");
		p.a.WriteShort(42);
		p.a.WriteByte(3);

		// CloseCaption { Token = "hl2mp.combine.alert", Duration = 42, Flags = 3 }
		unsigned char payload[32];
		const size_t n = strlen("hl2mp.combine.alert") + 1;
		memcpy(payload, "hl2mp.combine.alert", n);
		payload[n] = 42;
		payload[n + 1] = 0;
		payload[n + 2] = 3;
		SourceSharp_AppendBits(&p.b, payload, (int)(n + 3) * 8);

		p.Same("CloseCaption", off);
	}
}

TEST(UserMessageAppend, APayloadThatDoesNotFitOverflowsAndWritesNothing)
{
	unsigned char buf[4];
	memset(buf, 0, sizeof(buf));
	bf_write w(buf, sizeof(buf));
	w.WriteUBitLong(3, 5);

	const unsigned char big[8] = {1, 2, 3, 4, 5, 6, 7, 8};
	SourceSharp_AppendBits(&w, big, 64);

	EXPECT_TRUE(w.IsOverflowed());
	EXPECT_EQ(5, w.GetNumBitsWritten());
	EXPECT_EQ(3, buf[0]);
	EXPECT_EQ(0, buf[1]);
}

// APayloadRefusesToGrowPastItsCapacity WENT with CUserMessagePayload: a C#
// payload that does not fit the engine's 255 bytes is refused before it is
// sent (UserMessagePayloadTests.APayloadOverTheLimitIsNotSent).
