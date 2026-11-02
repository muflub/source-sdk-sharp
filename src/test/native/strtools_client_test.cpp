// V_strncpy (src/client/native/strtools_client.cpp): strncpy's bytes, then the last
// byte always the terminator. Written as strnlen + memcpy + memset since gcc, given
// the definition in a unity batch, inlines it into callers and calls strncpy with the
// destination's whole size a truncation (-Wstringop-truncation); these hold the bytes.

#include "gtest/gtest.h"

#include <cstring>

void V_strncpy(char* pDest, const char* pSrc, int maxLen);

// tier0's Error, which strtools_client.cpp's other members call; nothing here reaches it.
extern "C" void Error(const char*, ...)
{
	ADD_FAILURE() << "Error() was called";
}

namespace
{
// A buffer filled with a sentinel, so a byte V_strncpy did not write shows.
struct Buffer
{
	char m_Bytes[8];
	Buffer() { memset(m_Bytes, '#', sizeof(m_Bytes)); }
};

TEST(StrtoolsClient, AShortStringIsCopiedAndTheRestZeroFilled)
{
	Buffer b;
	V_strncpy(b.m_Bytes, "ab", 6);
	EXPECT_EQ(0, memcmp(b.m_Bytes, "ab\0\0\0\0##", 8));
}

TEST(StrtoolsClient, AStringThatFillsAllButOneByteKeepsItsTerminator)
{
	Buffer b;
	V_strncpy(b.m_Bytes, "abcde", 6);
	EXPECT_EQ(0, memcmp(b.m_Bytes, "abcde\0##", 8));
}

TEST(StrtoolsClient, ALongerStringIsCutAndTerminatedInsideTheBound)
{
	Buffer b;
	V_strncpy(b.m_Bytes, "abcdefghij", 6);
	EXPECT_EQ(0, memcmp(b.m_Bytes, "abcde\0##", 8));
}

TEST(StrtoolsClient, ABoundOfOneWritesOnlyTheTerminator)
{
	Buffer b;
	V_strncpy(b.m_Bytes, "abc", 1);
	EXPECT_EQ(0, memcmp(b.m_Bytes, "\0#######", 8));
}

TEST(StrtoolsClient, ABoundOfZeroOrLessWritesNothing)
{
	Buffer b;
	V_strncpy(b.m_Bytes, "abc", 0);
	V_strncpy(b.m_Bytes, "abc", -3);
	EXPECT_EQ(0, memcmp(b.m_Bytes, "########", 8));
}
} // namespace
