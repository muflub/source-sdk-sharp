//========= SourceSharp — the C++ half of the CGlobalVars layout check ========//
//
// plan_gamedll.md Part 0a #1. sourcesharp/game/shared/globalvars_layout.h holds
// one table of offsets that the managed mirror (SourceSharp.Abi/Globals.cs) and
// the engine's CGlobalVars both answer to, and the numbers in it are
// static_asserts against the real offsetof. THIS FILE IS WHAT COMPILES THEM.
//
// WHY A TEST FILE AT ALL, WHEN THE CHECK IS A static_assert. Because a
// static_assert in a header nobody includes is not a check: it is a comment
// that happens to be syntax. globalvars_layout.h has no other native consumer
// until the shell picks it up (Part 2), so without this the whole native half
// of the parity argument would compile nowhere and the managed test would be
// reading numbers no compiler had ever agreed with. Including the header is the
// point; the gtest bodies below are the part that reports rather than the part
// that checks.
//
// So there are two failure modes and they look different:
//
//   a row whose offset is wrong  -> this file does not COMPILE, and the error
//                                   names the field and the wrong number.
//   a row missing from the table -> the assertions below are red, because they
//                                   count and re-derive what the table claims.
//
//=============================================================================//
#include <stddef.h>
#include <string.h>

#include "gtest/gtest.h"

#include "sourcesharp/game/shared/globalvars_layout.h"

// Same reason as in the header: CGlobalVars is not standard-layout, so offsetof
// on it is "conditionally-supported". It is supported here, and asking is the
// only alternative to guessing.
#if defined(__GNUC__) || defined(__clang__)
#pragma GCC diagnostic ignored "-Winvalid-offsetof"
#endif

namespace
{

// Every row, once, as data -- so the assertions can say something about the
// table as a whole rather than only about fields named one at a time.
struct LayoutRow
{
	const char* name;
	size_t offset;
	size_t size;
	const char* reach;
};

#define SS_GV_ROW(name, offset, size, reach) {#name, (offset), (size), #reach},

const LayoutRow kRows[] = {SOURCESHARP_GLOBALVARS_LAYOUT(SS_GV_ROW)};

#undef SS_GV_ROW

const size_t kRowCount = sizeof(kRows) / sizeof(kRows[0]);

} // namespace

// The table describes the whole struct and not only the part `Globals` reads.
// A field left out does not merely go unread: every field after it in the
// managed mirror moves, and the mirror's total size stops matching.
TEST(GlobalVarsLayout, TheTableCoversEveryByteOfTheStruct)
{
	size_t covered = 0;

	for (size_t i = 0; i < kRowCount; ++i)
		covered += kRows[i].size;

	// 112 bytes of struct, 25 fields, 97 bytes of field. The other fifteen are
	// padding, in four places and every one of them a place a managed mirror
	// can silently absorb an extra field without any offset moving:
	//
	//   44 -> 48   four, aligning pSaveData
	//   57 -> 60   three, the bool-then-int that is the whole `bool` trap
	//   68 -> 72   four, the base class's own tail (and the reason a
	//              four-byte string_t would put mapname at 68 instead)
	//   84 -> 88   four, aligning startspot
	//
	// Counting them is what makes an inserted field visible: it either moves an
	// offset, which the rows catch, or it eats padding, which this catches.
	EXPECT_EQ(97u, covered) << "the table's fields account for 97 of the struct's bytes";
	EXPECT_EQ(15u, sizeof(CGlobalVars) - covered) << "the remaining fifteen bytes are padding";
	EXPECT_EQ((size_t)SOURCESHARP_GLOBALVARS_SIZEOF, sizeof(CGlobalVars));
}

// The rows are in ascending offset order and none of them overlap. A table that
// is merely a list of correct offsets can still have a row missing from the
// middle; a table that is contiguous-or-padded from 0 to the last field cannot
// hide one bigger than its own padding.
TEST(GlobalVarsLayout, TheRowsRunForwardAndDoNotOverlap)
{
	ASSERT_GT(kRowCount, 0u);
	EXPECT_EQ(0u, kRows[0].offset) << "the first field is at the start of the struct";

	for (size_t i = 1; i < kRowCount; ++i)
	{
		EXPECT_GE(kRows[i].offset, kRows[i - 1].offset + kRows[i - 1].size)
			<< kRows[i].name << " starts after " << kRows[i - 1].name << " ends";

		// No gap bigger than seven bytes anywhere: alignment can never need
		// more than that here, so a larger one is a field nobody wrote down.
		EXPECT_LT(kRows[i].offset - (kRows[i - 1].offset + kRows[i - 1].size), 8u)
			<< "the gap in front of " << kRows[i].name << " is padding, not a missing field";
	}

	const LayoutRow& last = kRows[kRowCount - 1];
	EXPECT_LT(sizeof(CGlobalVars) - (last.offset + last.size), 8u) << "nothing follows " << last.name << " but padding";
}

// The private tail is in the table. It is the part a mirror written from the
// public fields alone leaves out, and leaving it out costs sixteen bytes in the
// middle of the struct rather than at the end -- mapname lands at 56.
TEST(GlobalVarsLayout, ThePrivateTailIsInTheTable)
{
	int privateRows = 0;

	for (size_t i = 0; i < kRowCount; ++i)
	{
		if (strcmp(kRows[i].reach, "PRIVATE") == 0)
			++privateRows;
	}

	EXPECT_EQ(3, privateRows) << "CGlobalVarsBase's three private members are rows in the table";

	EXPECT_EQ((size_t)SOURCESHARP_GLOBALVARS_BASE_SIZEOF, sizeof(CGlobalVarsBase));
	EXPECT_EQ(sizeof(CGlobalVarsBase), offsetof(CGlobalVars, mapname))
		<< "CGlobalVars's own fields begin where the base class ends";
}

// x64, and string_t pointer-sized, restated as a running check beside the
// compile-time one in the header. Both are the reason every offset from 48 on
// is what it is, and a build where either changed would want to fail loudly
// rather than compile a header nobody read.
TEST(GlobalVarsLayout, TheTableDescribesX64)
{
	EXPECT_EQ(8u, sizeof(void*));
	EXPECT_EQ(8u, sizeof(string_t)) << "string_t is a pointer here, not a four-byte handle";
	EXPECT_EQ(48u, offsetof(CGlobalVars, pSaveData));
	EXPECT_EQ(72u, offsetof(CGlobalVars, mapname))
		<< "a four-byte string_t would put mapname at 68, in the base's tail padding";
}

// The four consecutive bools, which is where a widened managed bool does its
// damage: one byte each, back to back, with maxEntities immediately after.
TEST(GlobalVarsLayout, TheFourBoolsAreOneByteEachAndAdjacent)
{
	EXPECT_EQ(1u, sizeof(((CGlobalVars*)0)->bMapLoadFailed));
	EXPECT_EQ(offsetof(CGlobalVars, bMapLoadFailed) + 1, offsetof(CGlobalVars, deathmatch));
	EXPECT_EQ(offsetof(CGlobalVars, deathmatch) + 1, offsetof(CGlobalVars, coop));
	EXPECT_EQ(offsetof(CGlobalVars, coop) + 1, offsetof(CGlobalVars, teamplay));
	EXPECT_EQ(offsetof(CGlobalVars, teamplay) + 1, offsetof(CGlobalVars, maxEntities))
		<< "maxEntities sits directly on the fourth bool, with no padding to absorb a widened one";
}
