//========= SourceSharp — the client message's wire, writer against reader ==//
// ss_client_message.h. The client writes with tier1's bf_write
// (ss_client_message_write.h); the server decodes the bytes itself, since
// server.so links no tier1. These hold the two to the same bits at every
// starting offset, and the decoder to its refusals.
//=============================================================================//

#include "tier0/platform.h"
#include "tier1/bitbuf.h"
#include "ss_client_message_write.h"

#include <gtest/gtest.h>

#include <string.h>

namespace
{

struct Wire
{
	unsigned char bytes[8192];
	bf_write out;
	int nStart;

	explicit Wire(int nOffset)
	{
		memset(bytes, 0xA5, sizeof(bytes));
		out.StartWriting(bytes, sizeof(bytes));
		if (nOffset > 0) // bf_write takes no zero-bit write
			out.WriteUBitLong(0x5A3C96E1u & ((1u << nOffset) - 1u), nOffset);
		nStart = nOffset;
	}
};

// After the 6-bit type, which the engine reads before handing the message over.
int BodyStart(const Wire& w)
{
	return w.nStart + SOURCESHARP_NETMSG_TYPE_BITS;
}

}

TEST(ClientMessage, WhatTheClientWritesTheServerDecodesAtEveryOffset)
{
	uint8_t payload[300];
	for (int i = 0; i < 300; i++)
		payload[i] = (uint8_t)(i * 37 + 11);

	for (int off = 0; off < 32; off++)
	{
		Wire w(off);
		CSourceSharpClientMessage sent(NULL, SourceSharp_WriteClientMessage);
		ASSERT_TRUE(sent.Set("tf2_descent:ticket", payload, 300));
		ASSERT_TRUE(sent.WriteToBuffer(w.out));
		w.out.WriteUBitLong(0x2B, 7); // what follows the message on the wire

		bf_read in(w.bytes, w.out.GetNumBytesWritten(), w.out.GetNumBitsWritten());
		in.Seek(w.nStart);
		EXPECT_EQ(SOURCESHARP_NETMSG_CLIENT_MESSAGE, (int)in.ReadUBitLong(SOURCESHARP_NETMSG_TYPE_BITS)) << off;

		int used = 0;
		CSourceSharpClientMessage got;
		ASSERT_TRUE(got.Decode(w.bytes, BodyStart(w), w.out.GetNumBitsWritten(), &used)) << off;
		EXPECT_STREQ("tf2_descent:ticket", got.Key());
		ASSERT_EQ(300, got.DataLength());
		EXPECT_EQ(0, memcmp(payload, got.Data(), 300)) << off;

		// It consumed exactly its own bits: the next field reads back.
		in.Seek(BodyStart(w) + used);
		EXPECT_EQ(0x2Bu, in.ReadUBitLong(7)) << off;
	}
}

TEST(ClientMessage, ReadFromBufferSeeksPastItAndLeavesTheRestToTheEngine)
{
	Wire w(3);
	CSourceSharpClientMessage sent(NULL, SourceSharp_WriteClientMessage);
	const uint8_t bytes[] = {1, 2, 3};
	ASSERT_TRUE(sent.Set("k", bytes, 3));
	ASSERT_TRUE(sent.WriteToBuffer(w.out));
	w.out.WriteUBitLong(0x15, 5);

	bf_read in(w.bytes, w.out.GetNumBytesWritten(), w.out.GetNumBitsWritten());
	in.Seek(BodyStart(w));
	CSourceSharpClientMessage got;
	ASSERT_TRUE(got.ReadFromBuffer(in));
	EXPECT_FALSE(in.IsOverflowed());
	EXPECT_EQ(0x15u, in.ReadUBitLong(5));
	EXPECT_STREQ("k", got.Key());
	EXPECT_EQ(3, got.DataLength());
}

TEST(ClientMessage, AnEmptyPayloadTravels)
{
	Wire w(0);
	CSourceSharpClientMessage sent(NULL, SourceSharp_WriteClientMessage);
	ASSERT_TRUE(sent.Set("k", NULL, 0));
	ASSERT_TRUE(sent.WriteToBuffer(w.out));

	int used = 0;
	CSourceSharpClientMessage got;
	ASSERT_TRUE(got.Decode(w.bytes, BodyStart(w), w.out.GetNumBitsWritten(), &used));
	EXPECT_EQ(0, got.DataLength());
}

TEST(ClientMessage, ATruncatedMessageIsRefusedAndKeepsNothing)
{
	Wire w(0);
	CSourceSharpClientMessage sent(NULL, SourceSharp_WriteClientMessage);
	const uint8_t bytes[40] = {};
	ASSERT_TRUE(sent.Set("key", bytes, 40));
	ASSERT_TRUE(sent.WriteToBuffer(w.out));

	for (int cut = BodyStart(w); cut < w.out.GetNumBitsWritten(); cut += 7)
	{
		int used = 0;
		CSourceSharpClientMessage got;
		EXPECT_FALSE(got.Decode(w.bytes, BodyStart(w), cut, &used)) << cut;
		EXPECT_EQ(0, got.DataLength());
		EXPECT_STREQ("", got.Key());
	}

	// And through the engine's buffer: an overflowed read, so the engine drops it.
	bf_read in(w.bytes, (w.out.GetNumBitsWritten() - 8 + 7) / 8, w.out.GetNumBitsWritten() - 8);
	in.Seek(BodyStart(w));
	CSourceSharpClientMessage got;
	EXPECT_FALSE(got.ReadFromBuffer(in));
	EXPECT_TRUE(in.IsOverflowed());
}

TEST(ClientMessage, ALengthPastTheLimitAnEmptyKeyAndANulInTheKeyAreRefused)
{
	unsigned char bytes[64] = {};
	bf_write out(bytes, sizeof(bytes));

	out.WriteUBitLong(1, SOURCESHARP_CLIENT_MESSAGE_KEY_BITS);
	out.WriteByte('k');
	out.WriteUBitLong(SOURCESHARP_CLIENT_MESSAGE_MAX_BYTES + 1, SOURCESHARP_CLIENT_MESSAGE_LENGTH_BITS);
	int used = 0;
	CSourceSharpClientMessage got;
	EXPECT_FALSE(got.Decode(bytes, 0, 8 * (int)sizeof(bytes), &used));

	memset(bytes, 0, sizeof(bytes)); // key length 0
	EXPECT_FALSE(got.Decode(bytes, 0, 8 * (int)sizeof(bytes), &used));

	bf_write nul(bytes, sizeof(bytes));
	nul.WriteUBitLong(2, SOURCESHARP_CLIENT_MESSAGE_KEY_BITS);
	nul.WriteByte('k');
	nul.WriteByte(0);
	nul.WriteUBitLong(0, SOURCESHARP_CLIENT_MESSAGE_LENGTH_BITS);
	EXPECT_FALSE(got.Decode(bytes, 0, 8 * (int)sizeof(bytes), &used));
}

TEST(ClientMessage, SetRefusesWhatCannotTravel)
{
	CSourceSharpClientMessage m(NULL, SourceSharp_WriteClientMessage);
	static uint8_t big[SOURCESHARP_CLIENT_MESSAGE_MAX_BYTES + 1];
	char longKey[SOURCESHARP_CLIENT_MESSAGE_MAX_KEY + 2];
	memset(longKey, 'k', sizeof(longKey) - 1);
	longKey[sizeof(longKey) - 1] = 0;

	EXPECT_FALSE(m.Set("", big, 1));
	EXPECT_FALSE(m.Set(NULL, big, 1));
	EXPECT_FALSE(m.Set(longKey, big, 1));
	EXPECT_FALSE(m.Set("k", big, SOURCESHARP_CLIENT_MESSAGE_MAX_BYTES + 1));
	EXPECT_FALSE(m.Set("k", NULL, 4));
	EXPECT_TRUE(m.Set("k", big, SOURCESHARP_CLIENT_MESSAGE_MAX_BYTES));
}

TEST(ClientMessage, TheServersCopyHasNoWriterAndRefusesToWrite)
{
	unsigned char bytes[64];
	bf_write out(bytes, sizeof(bytes));
	CSourceSharpClientMessage serverSide;
	ASSERT_TRUE(serverSide.Set("k", NULL, 0));

	EXPECT_FALSE(serverSide.WriteToBuffer(out));
	EXPECT_EQ(0, out.GetNumBitsWritten());
}

TEST(ClientMessage, ItsTypeIsClearOfTheEnginesAndFitsTheTypeField)
{
	CSourceSharpClientMessage m;
	EXPECT_EQ(60, m.GetType());
	EXPECT_GT(m.GetType(), 17); // net_ 0-7 and clc_ 8-17 share a server's channel
	EXPECT_LT(m.GetType(), 1 << SOURCESHARP_NETMSG_TYPE_BITS);
}

namespace
{
class CCountingHandler : public IClientMessageHandler
{
public:
	bool OnClientMessage(CSourceSharpClientMessage& msg) override
	{
		m_nHandled += msg.Owner();
		return true;
	}

	int m_nHandled = 0;
};
}

TEST(ClientMessage, ProcessHandsTheMessageWithItsOwnerToTheHandler)
{
	CCountingHandler handler;
	CSourceSharpClientMessage m(&handler);
	m.SetOwner(7);

	EXPECT_TRUE(m.Process());
	EXPECT_EQ(7, handler.m_nHandled);
	EXPECT_TRUE(CSourceSharpClientMessage().Process()); // no handler: kept, not dropped
}
