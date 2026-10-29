//========= SourceSharp — tests for the entity op table ======================//
//
// plan_entity.md §3.4, step 1f. The op number is the whole mechanism: it is
// what one Dispatch slot carries in place of thirteen, and it is a BIT POSITION
// in the per-class override mask, which is what lets a class that does not
// override an op cost zero crossings for it.
//
// So three things are held here.
//
// 1. THE NUMBERS, pinned as literals. They cross the ABI as a bare int32_t.
//    Renumbering one side turns every touch into a think and nothing would say
//    so — the C# class would simply start receiving the wrong callback, which
//    reads as a bug in the C# and is not one. `EntityOpTests.OpNumbersAreFixed`
//    pins the same numbers on the far side, and the cross-read below is what
//    stops one pin going stale while the other moves. A pin on one side alone
//    cannot see a renumbering on the other; that is the whole failure this pair
//    exists to catch.
//
// 2. THE ARGUMENT LAYOUT, by offsetof against the C# declaration's own pins.
//    SourceSharpOpArgs crosses BY POINTER and is read field for field, so a
//    disagreement is a wild read and not a compile error.
//
// 3. THE SHELL ACTUALLY TESTS THE BIT. A relay that forgot ClassOverridesOp
//    would be correct, would pass every functional test there is, and would
//    silently put the crossing back — which is exactly the regression the mask
//    exists to prevent and exactly the kind nothing else would catch.
//
// Engine-free: entity_op.h takes nothing from Valve and sourcesharp.h
// deliberately knows nothing about Source, so both include cleanly here and the
// mechanism can be checked without a running game.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "../game/shared/entity_op.h"
#include "../host/sourcesharp.h"

#include <sys/stat.h>

#include <cstddef>
#include <fstream>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// The tree, found from wherever the binary was run.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The value C# gives one op, read out of EntityOp.cs. -1 for a name it does not
// declare; -2 when the file could not be read at all, so "no such op" and
// "no such file" are two different failures rather than one.
//
// A hand parse rather than a regex, because this test links gtest and nothing
// else. The declaration it reads is `    Name = 7,` — one per line, which the
// C# side's own OpValuesAreContiguousFromZero keeps true.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// ENTITY_OP_START_TOUCH is StartTouch. One convention, written once.
//-----------------------------------------------------------------------------
struct Op_t
{
	const char* pszNative;
	int nValue;
};

// EVERY op, with its number written out. The list is walked by the cross-read
// below, so an op added to the enum and left off this list is caught by
// EveryOpIsPinnedHere rather than being silently unchecked.
const Op_t kOps[] = {
	{"ENTITY_OP_SPAWN", 0},
	{"ENTITY_OP_KEY_VALUE", 1},
	{"ENTITY_OP_INPUT", 2},
	{"ENTITY_OP_THINK", 3},
	{"ENTITY_OP_REMOVED", 4},
	{"ENTITY_OP_START_TOUCH", 5},
	{"ENTITY_OP_TOUCH", 6},
	{"ENTITY_OP_END_TOUCH", 7},
	{"ENTITY_OP_IMPACT", 8},
	{"ENTITY_OP_MOVE_DONE", 9},
	{"ENTITY_OP_BLOCKED", 10},
	{"ENTITY_OP_DAMAGE", 11},
	{"ENTITY_OP_KILLED", 12},
	{"ENTITY_OP_FILTER_PASSES", 13},
	{"ENTITY_OP_POSITION_CHANGED", 14},
	{"ENTITY_OP_PHYSICS_CREATED", 15},
};

const size_t kOpCount = sizeof(kOps) / sizeof(kOps[0]);

} // namespace

//=============================================================================//
// 1. The numbers.
//=============================================================================//

//-----------------------------------------------------------------------------
// The pins themselves. Written out as literals rather than looped over the
// enum: a loop would re-derive the numbers from the declaration under test and
// pass whatever they became.
//-----------------------------------------------------------------------------
TEST(EntityOpNumbers, EveryOpHasItsFixedValue)
{
	EXPECT_EQ(0, (int)ENTITY_OP_SPAWN);
	EXPECT_EQ(1, (int)ENTITY_OP_KEY_VALUE);
	EXPECT_EQ(2, (int)ENTITY_OP_INPUT);
	EXPECT_EQ(3, (int)ENTITY_OP_THINK);
	EXPECT_EQ(4, (int)ENTITY_OP_REMOVED);
	EXPECT_EQ(5, (int)ENTITY_OP_START_TOUCH);
	EXPECT_EQ(6, (int)ENTITY_OP_TOUCH);
	EXPECT_EQ(7, (int)ENTITY_OP_END_TOUCH);
	EXPECT_EQ(8, (int)ENTITY_OP_IMPACT);
	EXPECT_EQ(9, (int)ENTITY_OP_MOVE_DONE);
	EXPECT_EQ(10, (int)ENTITY_OP_BLOCKED);
	EXPECT_EQ(11, (int)ENTITY_OP_DAMAGE);
	EXPECT_EQ(12, (int)ENTITY_OP_KILLED);
	EXPECT_EQ(13, (int)ENTITY_OP_FILTER_PASSES);
	EXPECT_EQ(14, (int)ENTITY_OP_POSITION_CHANGED);
	EXPECT_EQ(15, (int)ENTITY_OP_PHYSICS_CREATED);
}

//-----------------------------------------------------------------------------
// ENTITY_OP_COUNT is one past the last, which is what the shell range-checks
// against and what sizes the crossing counter array on the far side.
//-----------------------------------------------------------------------------
TEST(EntityOpNumbers, CountIsOnePastTheLastOp)
{
	EXPECT_EQ((int)kOpCount, (int)ENTITY_OP_COUNT);
	EXPECT_EQ(kOps[kOpCount - 1].nValue + 1, (int)ENTITY_OP_COUNT);
}

//-----------------------------------------------------------------------------
// The table above names every op the header declares. Without this an op could
// be added to entity_op.h, dispatched by the shell, and never appear in any
// pin -- free to be renumbered afterwards by either side.
//-----------------------------------------------------------------------------
//=============================================================================//
// The cross-read: the far side's numbers, out of its own declaration.
//=============================================================================//

//-----------------------------------------------------------------------------
// The file is there and parses. A cross-check that silently reads nothing
// passes for free, and this one's whole job is the two sides agreeing.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// Op for op. This is the check the two independent pin lists cannot make for
// themselves.
//-----------------------------------------------------------------------------
//=============================================================================//
// The mask.
//=============================================================================//

TEST(EntityOpMask, TheBitIsTheShift)
{
	for (size_t i = 0; i < kOpCount; i++)
	{
		EXPECT_EQ((uint32_t)(1u << kOps[i].nValue), ENTITY_OP_BIT(kOps[i].nValue));
	}
}

//-----------------------------------------------------------------------------
// THE FLOOR IS FOUR OPS, and exactly four.
//
// They are the ones where the managed side does BOOKKEEPING rather than merely
// calling a virtual a class may not have overridden: spawn and keyvalue create
// the C# instance, removed drops the registry row, and the filter answer has no
// virtual to be computed from. An op added to this set is a crossing nobody can
// ever switch off again; one removed from it is an entity with no managed half.
//-----------------------------------------------------------------------------
TEST(EntityOpMask, TheAlwaysMaskIsTheFourBookkeepingOps)
{
	const uint32_t expected = ENTITY_OP_BIT(ENTITY_OP_SPAWN) | ENTITY_OP_BIT(ENTITY_OP_KEY_VALUE) |
		ENTITY_OP_BIT(ENTITY_OP_REMOVED) | ENTITY_OP_BIT(ENTITY_OP_FILTER_PASSES);

	EXPECT_EQ(expected, (uint32_t)ENTITY_OP_ALWAYS_MASK);

	// And nothing else. Counted rather than compared alone, so an op OR'd in
	// with the same value by accident would still be caught.
	int nBits = 0;
	for (int i = 0; i < (int)ENTITY_OP_COUNT; i++)
	{
		if (ENTITY_OP_ALWAYS_MASK & ENTITY_OP_BIT(i))
			nBits++;
	}

	EXPECT_EQ(4, nBits);
}

//-----------------------------------------------------------------------------
// The default is ALL ONES and not zero, and the direction is the point: a class
// whose RegisterClassOps never arrived keeps working and merely pays the
// crossings it used to pay. Zero would silently stop delivering touches and
// thinks to entities that DO override them, which is a game broken in a way no
// build error names.
//-----------------------------------------------------------------------------
TEST(EntityOpMask, TheUnregisteredDefaultArmsEverything)
{
	for (int i = 0; i < (int)ENTITY_OP_COUNT; i++)
		EXPECT_NE(0u, ENTITY_OP_MASK_ALL & ENTITY_OP_BIT(i)) << "op " << i;
}

//=============================================================================//
// 2. The argument layout.
//=============================================================================//

//-----------------------------------------------------------------------------
// 64 bytes, and no interior padding anywhere.
//
// The struct crosses BY POINTER and is read field for field by a marshaller
// that has its own idea of the layout. Padding is where two declarations of one
// layout disagree without either being wrong, so the order is chosen to have
// none -- and the size is asserted rather than trusted, on both sides
// (EntityOpTests.OpArgsIsSixtyFourBytes).
//-----------------------------------------------------------------------------
TEST(EntityOpArgs, IsSixtyFourBytes)
{
	EXPECT_EQ((size_t)64, sizeof(SourceSharpOpArgs));
}

TEST(EntityOpArgs, EveryFieldIsWhereCsharpPinsIt)
{
	EXPECT_EQ((size_t)0, offsetof(SourceSharpOpArgs, pBlob));
	EXPECT_EQ((size_t)8, offsetof(SourceSharpOpArgs, nBlobBytes));
	EXPECT_EQ((size_t)12, offsetof(SourceSharpOpArgs, hOther));
	EXPECT_EQ((size_t)16, offsetof(SourceSharpOpArgs, pText0));
	EXPECT_EQ((size_t)24, offsetof(SourceSharpOpArgs, pText1));
	EXPECT_EQ((size_t)32, offsetof(SourceSharpOpArgs, pText2));
	EXPECT_EQ((size_t)40, offsetof(SourceSharpOpArgs, pVec0));
	EXPECT_EQ((size_t)48, offsetof(SourceSharpOpArgs, pVec1));
	EXPECT_EQ((size_t)56, offsetof(SourceSharpOpArgs, flFloat0));
	EXPECT_EQ((size_t)60, offsetof(SourceSharpOpArgs, nInt0));
}

//-----------------------------------------------------------------------------
// The two fixed-width words really are four bytes each. The rule the port runs
// on is that anything mirrored byte-for-byte uses int32_t and never int, and
// the fingerprint is computed from the C# side -- so a C++ width change is
// precisely the drift it cannot see.
//-----------------------------------------------------------------------------
TEST(EntityOpArgs, TheScalarsAreFixedWidth)
{
	SourceSharpOpArgs args;

	EXPECT_EQ((size_t)4, sizeof(args.nBlobBytes));
	EXPECT_EQ((size_t)4, sizeof(args.hOther));
	EXPECT_EQ((size_t)4, sizeof(args.flFloat0));
	EXPECT_EQ((size_t)4, sizeof(args.nInt0));
}

//=============================================================================//
// 3. The shell tests the bit.
//=============================================================================//

namespace
{

// One relay, and the op it must guard on before it crosses.
struct Relay_t
{
	const char* pszSignature;
	const char* pszOp;
};

} // namespace

//-----------------------------------------------------------------------------
// EVERY RELAY GUARDS ON ITS OWN OP BEFORE IT CROSSES.
//
// This is the fact the crossing budget rests on and it has no runtime symptom.
// A relay that dropped its ClassOverridesOp would be CORRECT -- the managed side
// would receive an op it does nothing with and return -- so every functional
// test in the tree would pass, the live gate would pass, and the only trace
// would be a crossing count that quietly went back up. The live gate's budget
// is what catches it after the fact; this catches it in the diff.
//
// The op is checked as well as the call, because guarding on the WRONG op is
// the same bug with an extra step: a think that tested the touch bit would
// cross for classes that touch and stay silent for classes that think.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// AND THE LIST ABOVE IS EVERY RELAY. Counted rather than listed, because the
// failure this guards against is a FOURTEENTH relay added later without a
// guard -- which the loop above cannot see, since it only walks what it was
// given.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE THREE TOUCH EDGES DO NOT CROSS AT ALL ANY MORE -- plan_entity.md step 2b.
//
// This test used to assert that OnManagedStartTouch, OnManagedTouch and
// OnManagedEndTouch each forwarded to ReportTouch, so that the count above
// could not balance while one of them grew an unguarded body of its own. All
// four are deleted, and the same hazard has the same shape: the count balances
// again the day somebody puts a touch relay back, and a relay that dispatches a
// touch from C++ is one inbound crossing per touching pair per frame.
//
// So the check is inverted rather than dropped. NONE of the four names may
// appear as a definition in the relay file.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// AND THE NATIVE SIDE ANSWERS THE ONE QUESTION IT KEPT.
//
// The report is what replaced them. Without this, deleting the relays above
// would pass every check in this file with nothing reporting an overlap at all
// -- which is a game where no trigger fires and no pickup can be collected, and
// which looks from outside exactly like a map whose outputs are not wired.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE GUARD ITSELF ANSWERS YES FOR A SHELL WITH NO CLASS.
//
// m_pClass is NULL between the allocation and PostConstruct. An entity that
// crossed when it did not need to is a wasted call; an entity that did NOT
// cross when it should have is a spawn that never reached C#. The two are not
// symmetrical, so the unknown case has to take the safe side -- and this is a
// source check because ClassOverridesOp is an inline member of a class no
// engine-free test can construct.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE CLASS THE STEP DELETED STAYS DELETED.
//
// CStandaloneScriptedEntity existed to hold thirteen relays into thirteen ABI
// slots. There is one slot now, so a subclass whose entire content is a switch
// on an integer is not a class -- and §3.1 says there is exactly ONE shell per
// side. The gate for the step is `nm -C server.so | grep -c` reading 0; this is
// the source half of it, which fails in the diff rather than after a link.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE OUTBOUND OP'S HANDLER IS THE ONE THE NATIVE PATH USES.
//
// ENTITY_OP_POSITION_CHANGED runs CStandaloneEntity::OnPositionChanged, which is
// also the tail of SetAbsTransform -- so the op path is not a copy of the
// native path and the two cannot drift. Were it a copy, the op's handler would
// be exercised only by its own test while every move in the game ran the other
// one.
//-----------------------------------------------------------------------------
