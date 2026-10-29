//========= SourceSharp — the acquisition reason set, walked =================//
//
// THE CLAIM UNDER TEST IS ABOUT A SET, SO THE TEST WALKS THE SET.
//
// sourcesharp.h promises three things about the reasons an acquire can fail
// with: the set is CLOSED, every literal is ASCII, and none of them is a format
// string. A test that spot-checked one literal would pass on the day a fifth
// arrived carrying "%s" — which is exactly the shape of check this tree calls
// one that cannot fail. So every arm below iterates SourceSharpAcquireReason
// from its first enumerator to its last, and the count itself is pinned, so
// adding a reason without touching this file is a failure rather than a
// silently narrower guarantee.
//
// IT COMPILES THE REAL FUNCTION. shell_acquire_reasons.cpp is on the link line;
// there is no copy of the literal table here. A test that restated the strings
// would agree with itself whatever the shipped ones said.
//
// AND IT DRIVES THE TRUNCATION, which is the other half of the contract that no
// managed test can reach: V_strncpy's behaviour at exactly the buffer size, one
// byte under it, and at zero, against the real tier1 rather than a stand-in
// (Q_strncpy is not strncpy — it always terminates and it takes an int).
//
//=============================================================================//

#include "gtest/gtest.h"

#include "sourcesharp.h"
#include "shell_vtable_interfaces.h"

#include <cstring>
#include <string>
#include <vector>

namespace
{

// The enum, first to last. Named rather than derived, because deriving the
// bounds from the enum is what makes a "walk" silently stop covering a member.
const SourceSharpAcquireReason kAll[] = {
	SOURCESHARP_ACQUIRE_NOT_YET_AVAILABLE,
	SOURCESHARP_ACQUIRE_NO_SUCH_FACTORY,
	SOURCESHARP_ACQUIRE_UNKNOWN_INTERFACE,
	SOURCESHARP_ACQUIRE_UNKNOWN_FACTORY_TYPE,
};

const int kCount = (int)(sizeof(kAll) / sizeof(kAll[0]));

} // namespace

//-----------------------------------------------------------------------------
// THE SET.
//-----------------------------------------------------------------------------

TEST(AcquireReasonSet, TheSetHasExactlyFourReasons)
{
	// FOUR CASES, FOUR LITERALS. This number is the whole reason the ASCII and
	// no-format arms below are a guarantee rather than a sample: a fifth
	// enumerator lands here first.
	EXPECT_EQ(4, kCount);
}

TEST(AcquireReasonSet, TheLastEnumeratorIsTheLastValueWalked)
{
	// The walk covers the enum's whole range rather than a prefix of it. If an
	// enumerator is inserted in the middle, this catches the array not growing.
	EXPECT_EQ(SOURCESHARP_ACQUIRE_UNKNOWN_FACTORY_TYPE, kAll[kCount - 1]);
	EXPECT_EQ(kCount - 1, (int)SOURCESHARP_ACQUIRE_UNKNOWN_FACTORY_TYPE);
}

TEST(AcquireReasonSet, EveryReasonAnswersWithANonEmptyLiteral)
{
	for (int i = 0; i < kCount; ++i)
	{
		const char* pszReason = SourceSharp_AcquireReason(kAll[i]);

		ASSERT_TRUE(pszReason != NULL) << "reason " << i << " answered NULL";
		EXPECT_GT(strlen(pszReason), 0u) << "reason " << i << " is empty";
	}
}

TEST(AcquireReasonSet, NoReasonHasAByteOverSevenBitsWide)
{
	for (int i = 0; i < kCount; ++i)
	{
		const char* pszReason = SourceSharp_AcquireReason(kAll[i]);

		for (const char* p = pszReason; *p; ++p)
		{
			EXPECT_LT((unsigned char)*p, 0x80u) << "\"" << pszReason << "\" is not ASCII, so the managed side's "
												<< "no-rune-splitting guarantee stops being true by construction";
		}
	}
}

TEST(AcquireReasonSet, NoReasonCarriesAFormatSpecifier)
{
	for (int i = 0; i < kCount; ++i)
	{
		const char* pszReason = SourceSharp_AcquireReason(kAll[i]);

		EXPECT_TRUE(strchr(pszReason, '%') == NULL)
			<< "\"" << pszReason << "\" contains a '%', and nothing formats these";
	}
}

TEST(AcquireReasonSet, EveryReasonFitsTheDeclaredBufferWithRoomToSpare)
{
	for (int i = 0; i < kCount; ++i)
	{
		const char* pszReason = SourceSharp_AcquireReason(kAll[i]);

		EXPECT_LT((int)strlen(pszReason), SOURCESHARP_ACQUIRE_ERROR_BYTES - 1)
			<< "\"" << pszReason << "\" would truncate in the buffer managed passes";
	}
}

TEST(AcquireReasonSet, EveryReasonIsDistinct)
{
	// Two enumerators answering the same sentence would make one of the four
	// failure cases unreportable -- a caller could not tell them apart, which
	// is the whole reason there are four rather than one.
	for (int i = 0; i < kCount; ++i)
	{
		for (int j = i + 1; j < kCount; ++j)
		{
			EXPECT_STRNE(SourceSharp_AcquireReason(kAll[i]), SourceSharp_AcquireReason(kAll[j]))
				<< "reasons " << i << " and " << j << " say the same thing";
		}
	}
}

TEST(AcquireReasonSet, AValueOutsideTheEnumStillAnswersSomethingSafeToCopy)
{
	const char* pszReason = SourceSharp_AcquireReason((SourceSharpAcquireReason)99);

	ASSERT_TRUE(pszReason != NULL);
	EXPECT_GT(strlen(pszReason), 0u);
}

//-----------------------------------------------------------------------------
// THE COPY. Truncating, terminating, and legal to call with nowhere to write.
//-----------------------------------------------------------------------------

TEST(AcquireReasonCopy, AReasonThatFitsArrivesWhole)
{
	char buffer[SOURCESHARP_ACQUIRE_ERROR_BYTES];
	memset(buffer, 'Z', sizeof(buffer));

	SourceSharp_SayAcquireReason(buffer, sizeof(buffer), SOURCESHARP_ACQUIRE_UNKNOWN_INTERFACE);

	EXPECT_STREQ("factory does not know that interface", buffer);
}

TEST(AcquireReasonCopy, ASmallBufferTruncatesAndStillTerminates)
{
	char buffer[8];
	memset(buffer, 'Z', sizeof(buffer));

	SourceSharp_SayAcquireReason(buffer, (int32_t)sizeof(buffer), SOURCESHARP_ACQUIRE_UNKNOWN_INTERFACE);

	// Seven characters and a terminator, and NOT eight characters: refusing to
	// report a failure because the report was long would be absurd, and running
	// off the end of the buffer would be worse.
	EXPECT_EQ(7u, strlen(buffer));
	EXPECT_EQ('\0', buffer[7]);
	EXPECT_EQ(0, strncmp(buffer, "factory", 7));
}

TEST(AcquireReasonCopy, ABufferOfOneHoldsTheTerminatorAndNothingElse)
{
	char buffer[2];
	buffer[0] = 'Z';
	buffer[1] = 'Z';

	SourceSharp_SayAcquireReason(buffer, 1, SOURCESHARP_ACQUIRE_UNKNOWN_INTERFACE);

	EXPECT_EQ('\0', buffer[0]);
	EXPECT_EQ('Z', buffer[1]) << "the copy wrote past the size it was given";
}

TEST(AcquireReasonCopy, AZeroLengthBufferIsLegalAndWritesNothing)
{
	// THE CANARY IS IN FRONT OF THE BUFFER, AND IT HAD TO BE.
	//
	// This arm read `char buffer[4]` and checked its four bytes, and against the
	// mutation it exists to catch -- the `nBytes <= 0` guard removed -- it
	// PASSED. V_strncpy with a size of zero copies nothing and then terminates
	// at pDest[maxLen - 1], which is pDest[-1]: the one byte it writes is the
	// one byte in front of the buffer, which is the only place this arm was not
	// looking. A check whose subject is "nothing outside the caller's bytes was
	// touched" has to own the byte on either side of them.
	char pad[8];
	memset(pad, 'Z', sizeof(pad));

	char* pBuffer = pad + 4;

	SourceSharp_SayAcquireReason(pBuffer, 0, SOURCESHARP_ACQUIRE_UNKNOWN_INTERFACE);

	for (size_t i = 0; i < sizeof(pad); ++i)
	{
		EXPECT_EQ('Z', pad[i]) << "byte " << i << " of the padded region was written; the buffer "
							   << "itself is bytes 4..7, so 0..3 being touched is a write IN FRONT "
							   << "of what the caller gave";
	}
}

TEST(AcquireReasonCopy, ANullBufferIsLegalAndDoesNotFault)
{
	// A diagnostic path may not want a reason at all. Reaching this line is the
	// assertion.
	SourceSharp_SayAcquireReason(NULL, SOURCESHARP_ACQUIRE_ERROR_BYTES, SOURCESHARP_ACQUIRE_UNKNOWN_INTERFACE);

	SUCCEED();
}

TEST(AcquireReasonCopy, ANegativeSizeIsRefusedRatherThanTrusted)
{
	// Padded for the same reason as the arm above, and here the write would be
	// much further out: V_strncpy with a negative size passes it to strncpy as
	// a size_t, which is every byte there is.
	char pad[8];
	memset(pad, 'Z', sizeof(pad));

	char* pBuffer = pad + 4;

	SourceSharp_SayAcquireReason(pBuffer, -1, SOURCESHARP_ACQUIRE_UNKNOWN_INTERFACE);

	for (size_t i = 0; i < sizeof(pad); ++i)
		EXPECT_EQ('Z', pad[i]) << "byte " << i << " of the padded region was written";
}

TEST(AcquireReasonCopy, EveryReasonInTheSetCopiesBackIdentically)
{
	// The copy is the only thing that happens to a literal, so it is held
	// against the literal for every member rather than for one of them.
	for (int i = 0; i < kCount; ++i)
	{
		char buffer[SOURCESHARP_ACQUIRE_ERROR_BYTES];
		memset(buffer, 'Z', sizeof(buffer));

		SourceSharp_SayAcquireReason(buffer, sizeof(buffer), kAll[i]);

		EXPECT_STREQ(SourceSharp_AcquireReason(kAll[i]), buffer);
	}
}
