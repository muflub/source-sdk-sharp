//========= SourceSharp ========================================================//
//
// slot_table_test.cpp -- the slot table of plan_entity.md §3.3, and the two
// properties step 1c's gate names:
//
//   1. THE SERIAL-REUSE TEST. Bind an entry, unbind it, rebind the SAME entry,
//      and the old handle must resolve to NULL rather than to the new occupant.
//      This is the only thing guarding the one downcast plan_entity.md permits
//      on an entity path: that cast is allowlisted in
//      tools/entity-gates-allowlist, an allowlist entry is a hole in gate 3,
//      and the hole is excused on the grounds that this file covers the site.
//      So the coverage has to be real -- deleting the serial comparison in
//      CSourceSharpSlotTable::Resolve makes SerialReuse.* red, and that was
//      mutation-verified rather than assumed.
//
//   2. THE TABLE CANNOT ITERATE. Definition-of-done clause 2 is that no native
//      entity list survives the port, and a table that can be walked is one.
//      That is not assertable from inside gtest: every assertion a test could
//      make about a member that does not exist passes whether or not the member
//      is absent. So it is a COMPILE ERROR instead -- the block at the bottom of
//      this file is compiled five more times, once per -DSLOT_TABLE_NOCOMPILE=n,
//      by build_must_not_compile in src/buildcpptests, and every one of the five
//      is required to be refused BY THE COMPILER AND AT THE RIGHT ERROR. Same
//      mechanism entity_funcslot_test.cpp uses, for the same reason.
//
// ENGINE-FREE, like every other leg here: the only Valve headers it needs are
// basehandle.h and the four that sourcesharp/test/entitylist_stub/cbase.h
// gathers, which is the same set entitylist_slot0_test.cpp compiles against.
// Nothing in the slot table takes an engine.
//
//=============================================================================

#include <string.h>

#include "gtest/gtest.h"

#include "basehandle.h"
#include "ihandleentity.h"

#include "../game/shared/slot_table.h"
#include "../game/server/standalone_netserial.h"

namespace
{

//-----------------------------------------------------------------------------
// A stand-in for the shell. The table stores a void * and never dereferences
// it, so what matters about this type is only that two instances have two
// addresses -- which is exactly the question "did the old handle come back with
// the new occupant?" turns into.
//-----------------------------------------------------------------------------
struct FakeShell
{
	int nWhich;
};

//-----------------------------------------------------------------------------
// A stand-in for edict_t, carrying the one field the mirror writes.
//
// standalone_netserial.h is templated over the edict for precisely this: the
// rule it holds is one masked store, and the store has to be checkable without
// an engine. FIXTURE MUST BE CHECKED AGAINST NATIVE -- so the field is declared
// with the type the real edict_t has (`short m_NetworkSerialNumber`,
// public/edict.h), and TheMirrorMasksToTenBits below pins the value against
// STANDALONE_NETSERIAL_BITS rather than against a number typed in here.
//-----------------------------------------------------------------------------
struct FakeEdict
{
	short m_NetworkSerialNumber;
};

// The first entry of the engine-invisible half. Derived, not typed in: §3.3's
// split is at MAX_EDICTS, and a literal 2048 here would stop tracking const.h.
const int kFirstLogicalEntry = MAX_EDICTS;
const int kLastEntry = NUM_ENT_ENTRIES - 1;

} // namespace

//=============================================================================
// SIZE AND SHAPE -- the two fixed facts of plan_entity.md §2
//=============================================================================

//-----------------------------------------------------------------------------
// 8,192 entries, and the number comes from const.h rather than from this file.
//-----------------------------------------------------------------------------
TEST(SlotTableRange, TheTableSpansTheWholeHandleRangeAndNotJustTheEdicts)
{
	EXPECT_EQ(8192, CSourceSharpSlotTable::EntryCount());
	EXPECT_EQ(1 << (MAX_EDICT_BITS + 2), CSourceSharpSlotTable::EntryCount());
	EXPECT_EQ(NUM_ENT_ENTRIES, CSourceSharpSlotTable::EntryCount());

	// Four times as many entries as edicts: the point of the +2.
	EXPECT_EQ(4 * MAX_EDICTS, CSourceSharpSlotTable::EntryCount());
}

//-----------------------------------------------------------------------------
// The split the engine can see, and the half it cannot.
//-----------------------------------------------------------------------------
TEST(SlotTableRange, EdictEntriesAreTheFirstQuarterAndTheRestAreInvisible)
{
	EXPECT_TRUE(CSourceSharpSlotTable::IsEdictEntry(0));
	EXPECT_TRUE(CSourceSharpSlotTable::IsEdictEntry(MAX_EDICTS - 1));
	EXPECT_FALSE(CSourceSharpSlotTable::IsEdictEntry(kFirstLogicalEntry));
	EXPECT_FALSE(CSourceSharpSlotTable::IsEdictEntry(kLastEntry));

	// Every one of them is still an ENTRY. The engine-invisible half is
	// addressable or step 2e's shell-less entities have nowhere to live.
	EXPECT_TRUE(CSourceSharpSlotTable::IsEntry(kFirstLogicalEntry));
	EXPECT_TRUE(CSourceSharpSlotTable::IsEntry(kLastEntry));
}

TEST(SlotTableRange, AnEntryOutsideTheTableIsRefusedRatherThanWrapped)
{
	EXPECT_FALSE(CSourceSharpSlotTable::IsEntry(-1));
	EXPECT_FALSE(CSourceSharpSlotTable::IsEntry(NUM_ENT_ENTRIES));

	// ENT_ENTRY_MASK is sixteen bits wide and this table is thirteen, so a
	// masked handle really can name an entry past the end. That is the value
	// the bound check exists for.
	EXPECT_FALSE(CSourceSharpSlotTable::IsEntry(ENT_ENTRY_MASK));
	EXPECT_GT(ENT_ENTRY_MASK, NUM_ENT_ENTRIES - 1);
}

//-----------------------------------------------------------------------------
// The serial wraps where stock wraps: fifteen bits (entitylist_base.cpp:16).
//-----------------------------------------------------------------------------
TEST(SlotTableRange, TheSerialMaskIsFifteenBitsAndNotSixteen)
{
	EXPECT_EQ(0x7fff, (int)SOURCESHARP_SLOT_SERIAL_MASK);
	EXPECT_EQ((1 << (NUM_SERIAL_NUM_BITS - 1)) - 1, (int)SOURCESHARP_SLOT_SERIAL_MASK);

	// The spare top bit is what keeps every serial a legal CBaseHandle::Init
	// argument and clear of the 0xFFFF an invalid handle carries.
	EXPECT_LT((int)SOURCESHARP_SLOT_SERIAL_MASK, 1 << NUM_SERIAL_NUM_BITS);
}

//=============================================================================
// BIND, UNBIND, RESOLVE
//=============================================================================

TEST(SlotTableBind, AFreshTableHasNothingBound)
{
	CSourceSharpSlotTable table;

	EXPECT_EQ(0, table.BoundCount());
	EXPECT_FALSE(table.IsBound(0));
	EXPECT_FALSE(table.IsBound(kLastEntry));
	EXPECT_TRUE(table.ResolveByIndex(0) == NULL);
	EXPECT_FALSE(table.HandleOf(0).IsValid());
}

TEST(SlotTableBind, WhatWasBoundIsWhatComesBack)
{
	CSourceSharpSlotTable table;
	FakeShell shell = {7};

	const CBaseHandle h = table.Bind(12, &shell);

	ASSERT_TRUE(h.IsValid());
	EXPECT_EQ(12, h.GetEntryIndex());
	EXPECT_EQ(&shell, table.Resolve(h));
	EXPECT_EQ(&shell, table.ResolveByIndex(12));
	EXPECT_EQ(1, table.BoundCount());
	EXPECT_TRUE(table.IsBound(12));
}

//-----------------------------------------------------------------------------
// The engine-invisible half behaves exactly like the edict half. Step 2e's
// logic_relay lives up here and must resolve by handle like anything else.
//-----------------------------------------------------------------------------
TEST(SlotTableBind, AnEntryInTheEngineInvisibleHalfBindsAndResolvesTheSame)
{
	CSourceSharpSlotTable table;
	FakeShell relay = {1};

	const CBaseHandle h = table.Bind(kFirstLogicalEntry, &relay);

	ASSERT_TRUE(h.IsValid());
	EXPECT_FALSE(CSourceSharpSlotTable::IsEdictEntry(h.GetEntryIndex()));
	EXPECT_EQ(&relay, table.Resolve(h));
	EXPECT_EQ(&relay, table.ResolveByIndex(kFirstLogicalEntry));
}

TEST(SlotTableBind, TheLastEntryInTheTableIsUsable)
{
	CSourceSharpSlotTable table;
	FakeShell shell = {2};

	const CBaseHandle h = table.Bind(kLastEntry, &shell);

	ASSERT_TRUE(h.IsValid());
	EXPECT_EQ(&shell, table.Resolve(h));
}

TEST(SlotTableBind, BindingOutsideTheTableIsRefused)
{
	CSourceSharpSlotTable table;
	FakeShell shell = {3};

	EXPECT_FALSE(table.Bind(-1, &shell).IsValid());
	EXPECT_FALSE(table.Bind(NUM_ENT_ENTRIES, &shell).IsValid());
	EXPECT_EQ(0, table.BoundCount());
	EXPECT_EQ(2, table.Refused());
}

//-----------------------------------------------------------------------------
// A binding to NULL is refused, because it is a state with no observable
// difference from not being bound -- and accepting it would let BoundCount say
// one while every resolve says nothing.
//-----------------------------------------------------------------------------
TEST(SlotTableBind, BindingNullIsRefused)
{
	CSourceSharpSlotTable table;

	EXPECT_FALSE(table.Bind(5, NULL).IsValid());
	EXPECT_EQ(0, table.BoundCount());
	EXPECT_FALSE(table.IsBound(5));
}

//-----------------------------------------------------------------------------
// AN ENTRY HAS ONE OCCUPANT. Stock asserts this; an assert compiles out, and
// the silent overwrite it leaves behind gives two live entities one handle.
//-----------------------------------------------------------------------------
TEST(SlotTableBind, BindingOverALiveBindingIsRefusedAndLeavesTheFirstAlone)
{
	CSourceSharpSlotTable table;
	FakeShell first = {1}, second = {2};

	const CBaseHandle h = table.Bind(20, &first);
	ASSERT_TRUE(h.IsValid());

	EXPECT_FALSE(table.Bind(20, &second).IsValid());

	EXPECT_EQ(&first, table.Resolve(h));
	EXPECT_EQ(&first, table.ResolveByIndex(20));
	EXPECT_EQ(1, table.BoundCount());
}

TEST(SlotTableBind, UnbindingEmptiesTheEntryAndTheCount)
{
	CSourceSharpSlotTable table;
	FakeShell shell = {4};

	table.Bind(33, &shell);
	table.Unbind(33);

	EXPECT_EQ(0, table.BoundCount());
	EXPECT_FALSE(table.IsBound(33));
	EXPECT_TRUE(table.ResolveByIndex(33) == NULL);
	EXPECT_FALSE(table.HandleOf(33).IsValid());
}

//-----------------------------------------------------------------------------
// Idempotent, and it has to be: a shell is destroyed on paths that cannot know
// whether it was ever bound. A second unbind that bumped the serial again would
// leak serial space silently.
//-----------------------------------------------------------------------------
TEST(SlotTableBind, UnbindingTwiceIsANoOpAndDoesNotMoveTheSerialAgain)
{
	CSourceSharpSlotTable table;
	FakeShell shell = {5};

	table.Bind(41, &shell);
	table.Unbind(41);

	const int nAfterFirst = table.SerialAt(41);

	table.Unbind(41);
	table.Unbind(41);

	EXPECT_EQ(nAfterFirst, table.SerialAt(41));
	EXPECT_EQ(0, table.BoundCount());
	EXPECT_EQ(1, table.Unbinds());
}

TEST(SlotTableBind, UnbindingSomethingNeverBoundIsSafe)
{
	CSourceSharpSlotTable table;

	table.Unbind(77);
	table.Unbind(-1);
	table.Unbind(NUM_ENT_ENTRIES);

	EXPECT_EQ(0, table.BoundCount());
	EXPECT_EQ(0, table.SerialAt(77));
}

TEST(SlotTableBind, ResetClearsEveryBinding)
{
	CSourceSharpSlotTable table;
	FakeShell a = {1}, b = {2};

	const CBaseHandle ha = table.Bind(3, &a);
	const CBaseHandle hb = table.Bind(kLastEntry, &b);

	table.Reset();

	EXPECT_EQ(0, table.BoundCount());
	EXPECT_TRUE(table.Resolve(ha) == NULL);
	EXPECT_TRUE(table.Resolve(hb) == NULL);
	EXPECT_TRUE(table.ResolveByIndex(3) == NULL);
}

//=============================================================================
// THE SERIAL-REUSE TEST -- step 1c's gate, and the cover for the one
// allowlisted downcast
//=============================================================================

//-----------------------------------------------------------------------------
// THE POINT OF THE WHOLE FILE. Bind, unbind, rebind the same entry with a
// DIFFERENT occupant, and the old handle must answer NULL -- not the new
// occupant, which is the answer a resolver that checked only the pointer would
// give and the answer that is wrong without ever looking wrong.
//-----------------------------------------------------------------------------
TEST(SerialReuse, TheOldHandleDoesNotResolveToTheNewOccupant)
{
	CSourceSharpSlotTable table;
	FakeShell dead = {1}, alive = {2};

	const CBaseHandle hOld = table.Bind(100, &dead);
	ASSERT_TRUE(hOld.IsValid());
	ASSERT_EQ(&dead, table.Resolve(hOld));

	table.Unbind(100);

	const CBaseHandle hNew = table.Bind(100, &alive);
	ASSERT_TRUE(hNew.IsValid());

	// The same entry, a different occupant, and the two handles are NOT equal.
	EXPECT_EQ(hOld.GetEntryIndex(), hNew.GetEntryIndex());
	EXPECT_NE(hOld.GetSerialNumber(), hNew.GetSerialNumber());
	EXPECT_TRUE(hOld != hNew);

	// The new one resolves. The old one does not, and above all does not
	// resolve to `alive`.
	EXPECT_EQ(&alive, table.Resolve(hNew));
	EXPECT_TRUE(table.Resolve(hOld) == NULL);
	EXPECT_NE(&alive, table.Resolve(hOld));
}

//-----------------------------------------------------------------------------
// The first half of the same property, on its own: an entry that is merely
// EMPTY already refuses the old handle. Kept separate from the rebind case
// because a resolver that checked only the pointer passes this one, and a test
// that only ever ran this would report the serial check as covered when it is
// not executed at all.
//-----------------------------------------------------------------------------
TEST(SerialReuse, AHandleToAnUnboundEntryResolvesToNull)
{
	CSourceSharpSlotTable table;
	FakeShell dead = {1};

	const CBaseHandle hOld = table.Bind(101, &dead);
	table.Unbind(101);

	EXPECT_TRUE(table.Resolve(hOld) == NULL);
}

//-----------------------------------------------------------------------------
// Unbind is what moves the serial, and it moves it by one -- so the staleness
// exists from the moment the entity goes and not from the moment its entry is
// reused.
//-----------------------------------------------------------------------------
TEST(SerialReuse, UnbindAdvancesTheEntrysSerialByOne)
{
	CSourceSharpSlotTable table;
	FakeShell shell = {1};

	EXPECT_EQ(0, table.SerialAt(55));

	table.Bind(55, &shell);
	EXPECT_EQ(0, table.SerialAt(55)); // bind takes the serial, never moves it

	table.Unbind(55);
	EXPECT_EQ(1, table.SerialAt(55));

	table.Bind(55, &shell);
	table.Unbind(55);
	EXPECT_EQ(2, table.SerialAt(55));
}

//-----------------------------------------------------------------------------
// EVERY generation of an entry is distinct, not just the second: a handle from
// three occupants ago must miss as surely as the last one.
//-----------------------------------------------------------------------------
TEST(SerialReuse, EveryGenerationOfAnEntryGetsItsOwnHandle)
{
	CSourceSharpSlotTable table;
	FakeShell shells[4] = {{0}, {1}, {2}, {3}};
	CBaseHandle handles[4];

	for (int i = 0; i < 4; ++i)
	{
		handles[i] = table.Bind(200, &shells[i]);
		ASSERT_TRUE(handles[i].IsValid());
		if (i < 3)
			table.Unbind(200);
	}

	// Only the live one resolves; the three dead ones all miss.
	EXPECT_EQ(&shells[3], table.Resolve(handles[3]));
	for (int i = 0; i < 3; ++i)
		EXPECT_TRUE(table.Resolve(handles[i]) == NULL) << "generation " << i;
}

//-----------------------------------------------------------------------------
// The serial-reuse rule holds in the engine-invisible half too. Step 2e's
// entities die and are replaced like any other, and they have no edict whose
// own serial could stand in for the check.
//-----------------------------------------------------------------------------
TEST(SerialReuse, TheRuleHoldsInTheEngineInvisibleHalf)
{
	CSourceSharpSlotTable table;
	FakeShell dead = {1}, alive = {2};

	const CBaseHandle hOld = table.Bind(kFirstLogicalEntry, &dead);
	table.Unbind(kFirstLogicalEntry);
	const CBaseHandle hNew = table.Bind(kFirstLogicalEntry, &alive);

	EXPECT_EQ(&alive, table.Resolve(hNew));
	EXPECT_TRUE(table.Resolve(hOld) == NULL);
}

//-----------------------------------------------------------------------------
// The serial wraps rather than growing, and a wrapped serial is still a legal
// handle. 32,768 generations of one entry, which is the whole space.
//-----------------------------------------------------------------------------
TEST(SerialReuse, TheSerialWrapsAtTheMaskAndStaysALegalHandle)
{
	CSourceSharpSlotTable table;
	FakeShell shell = {1};

	for (int i = 0; i <= (int)SOURCESHARP_SLOT_SERIAL_MASK; ++i)
	{
		ASSERT_EQ(i, table.SerialAt(9)) << "generation " << i;
		table.Bind(9, &shell);
		table.Unbind(9);
	}

	EXPECT_EQ(0, table.SerialAt(9));

	const CBaseHandle h = table.Bind(9, &shell);
	EXPECT_TRUE(h.IsValid());
	EXPECT_EQ(&shell, table.Resolve(h));
}

//-----------------------------------------------------------------------------
// A handle whose ENTRY is right and whose serial was never issued misses. This
// is the same compare as the rebind case reached from the other side -- a
// hand-made handle rather than a recycled one -- and it is what a corrupted or
// forged handle looks like.
//-----------------------------------------------------------------------------
TEST(SerialReuse, AHandleWithASerialThatWasNeverIssuedResolvesToNull)
{
	CSourceSharpSlotTable table;
	FakeShell shell = {1};

	const CBaseHandle hReal = table.Bind(60, &shell);
	ASSERT_EQ(&shell, table.Resolve(hReal));

	const CBaseHandle hForged(60, hReal.GetSerialNumber() + 1);
	EXPECT_TRUE(table.Resolve(hForged) == NULL);
}

//=============================================================================
// SLOT ZERO AND THE INVALID HANDLE
//
// entitylist_slot0_test.cpp records what happens when a resolver confuses these
// two: entry 0 / serial 0 is a handle whose whole m_Index is 0, it is the
// client world's handle on every map load, and a guard written as
// `m_Index == 0` returns NULL for exactly one entity in the game. The slot
// table inherits the hazard, so it inherits the tests.
//=============================================================================

TEST(SlotTableSlotZero, EntryZeroSerialZeroIsARealHandleAndResolves)
{
	CSourceSharpSlotTable table;
	FakeShell world = {1};

	const CBaseHandle h = table.Bind(0, &world);

	ASSERT_TRUE(h.IsValid());
	EXPECT_EQ(0, h.GetEntryIndex());
	EXPECT_EQ(0, h.GetSerialNumber());
	EXPECT_EQ(0, h.ToInt()); // the whole handle is the integer zero
	EXPECT_EQ(&world, table.Resolve(h));
}

//-----------------------------------------------------------------------------
// And the fix cannot be "delete the guard": the genuinely invalid handle must
// still miss. It carries 0xFFFFFFFF, whose GetEntryIndex() answers
// NUM_ENT_ENTRIES-1 by design -- a real, bindable entry in this table -- so an
// unguarded resolve would answer with whatever is bound at 8191.
//-----------------------------------------------------------------------------
TEST(SlotTableSlotZero, TheInvalidHandleResolvesToNullEvenWhenTheLastEntryIsBound)
{
	CSourceSharpSlotTable table;
	FakeShell last = {1};

	const CBaseHandle hLast = table.Bind(kLastEntry, &last);
	ASSERT_EQ(&last, table.Resolve(hLast));

	const CBaseHandle hInvalid;
	ASSERT_FALSE(hInvalid.IsValid());
	ASSERT_EQ(kLastEntry, hInvalid.GetEntryIndex()); // the bug-submarining hack

	EXPECT_TRUE(table.Resolve(hInvalid) == NULL);
	EXPECT_NE(&last, table.Resolve(hInvalid));
}

//-----------------------------------------------------------------------------
// NO HANDLE THIS TABLE ISSUES CAN COLLIDE WITH THE INVALID ONE, and it is worth
// pinning rather than reasoning about at the call site: INVALID_EHANDLE_INDEX
// is 0xFFFFFFFF, which is entry 0xFFFF with serial 0xFFFF -- and 0xFFFF is not
// an entry in a table thirteen bits wide. So "the table answered with a handle"
// and "the table answered no entity" can never be the same 32 bits, whatever
// the serial has wrapped to.
//-----------------------------------------------------------------------------
TEST(SlotTableSlotZero, NoIssuedHandleCanEqualTheInvalidOne)
{
	CSourceSharpSlotTable table;
	FakeShell shell = {1};

	// The worst case: the highest entry carrying the widest serial a
	// CBaseHandle can hold.
	const CBaseHandle hWorst(kLastEntry, (1 << NUM_SERIAL_NUM_BITS) - 1);
	ASSERT_TRUE(table.BindAt(hWorst, &shell));

	const CBaseHandle hIssued = table.HandleOf(kLastEntry);

	EXPECT_TRUE(hIssued.IsValid());
	EXPECT_NE((int)INVALID_EHANDLE_INDEX, hIssued.ToInt());
	EXPECT_EQ(&shell, table.Resolve(hIssued));
}

TEST(SlotTableSlotZero, BindingTheInvalidHandleIsRefused)
{
	CSourceSharpSlotTable table;
	FakeShell shell = {1};

	const CBaseHandle hInvalid;
	EXPECT_FALSE(table.BindAt(hInvalid, &shell));
	EXPECT_FALSE(table.IsBound(kLastEntry));
	EXPECT_EQ(0, table.BoundCount());
}

//=============================================================================
// BINDAT -- a handle assigned elsewhere
//=============================================================================

//-----------------------------------------------------------------------------
// The client is TOLD its serials. BindAt adopts the handle's serial so that the
// two sides' handles agree, which is what an EHANDLE prop on the wire requires.
//-----------------------------------------------------------------------------
TEST(SlotTableBindAt, TheHandlesSerialIsAdoptedRatherThanIgnored)
{
	CSourceSharpSlotTable table;
	FakeShell shell = {1};

	const CBaseHandle hForced(17, 4242);
	ASSERT_TRUE(table.BindAt(hForced, &shell));

	EXPECT_EQ(4242, table.SerialAt(17));
	EXPECT_EQ(&shell, table.Resolve(hForced));
	EXPECT_TRUE(table.HandleOf(17) == hForced);
}

//-----------------------------------------------------------------------------
// And a handle carrying the serial the entry USED to have still misses after
// the adoption -- so BindAt does not reopen the door the serial check closes.
//-----------------------------------------------------------------------------
TEST(SlotTableBindAt, AnEarlierSerialStillMissesAfterAdoption)
{
	CSourceSharpSlotTable table;
	FakeShell dead = {1}, alive = {2};

	const CBaseHandle hOld(18, 3);
	ASSERT_TRUE(table.BindAt(hOld, &dead));
	table.Unbind(18);

	const CBaseHandle hNew(18, 9);
	ASSERT_TRUE(table.BindAt(hNew, &alive));

	EXPECT_EQ(&alive, table.Resolve(hNew));
	EXPECT_TRUE(table.Resolve(hOld) == NULL);
}

//-----------------------------------------------------------------------------
// A serial ABOVE the table's own wrap is adopted as given, not masked into
// range. Masking here would store a different number than the handle carries
// and make that entity unreachable through its own handle -- one silent NULL,
// for one entity, with nothing to report it. Sixteen bits is everything a
// CBaseHandle can carry, so this is the widest a forced serial can be.
//-----------------------------------------------------------------------------
TEST(SlotTableBindAt, ASerialAboveTheTablesOwnWrapIsAdoptedRatherThanMasked)
{
	CSourceSharpSlotTable table;
	FakeShell shell = {1};

	const int nWide = (1 << NUM_SERIAL_NUM_BITS) - 2; // 65534, above the 15-bit wrap
	ASSERT_GT(nWide, (int)SOURCESHARP_SLOT_SERIAL_MASK);

	const CBaseHandle hForced(21, nWide);
	ASSERT_TRUE(table.BindAt(hForced, &shell));

	EXPECT_EQ(nWide, table.SerialAt(21));
	EXPECT_EQ(&shell, table.Resolve(hForced));
}

//-----------------------------------------------------------------------------
// And the table's OWN wrap still governs what it issues next: unbinding an
// adopted out-of-range serial brings the entry back inside the mask rather than
// leaving it to overflow the field.
//-----------------------------------------------------------------------------
TEST(SlotTableBindAt, UnbindingAnAdoptedWideSerialWrapsBackIntoTheTablesRange)
{
	CSourceSharpSlotTable table;
	FakeShell shell = {1};

	const CBaseHandle hForced(22, (1 << NUM_SERIAL_NUM_BITS) - 1); // 65535
	ASSERT_TRUE(table.BindAt(hForced, &shell));

	table.Unbind(22);

	EXPECT_LE(table.SerialAt(22), (int)SOURCESHARP_SLOT_SERIAL_MASK);
	EXPECT_TRUE(table.Resolve(hForced) == NULL);
}

TEST(SlotTableBindAt, BindingOverALiveBindingIsRefused)
{
	CSourceSharpSlotTable table;
	FakeShell first = {1}, second = {2};

	const CBaseHandle h(19, 1);
	ASSERT_TRUE(table.BindAt(h, &first));
	EXPECT_FALSE(table.BindAt(h, &second));
	EXPECT_EQ(&first, table.ResolveByIndex(19));
}

//=============================================================================
// RESOLVE BY INDEX -- the engine's route, which carries no serial
//=============================================================================

TEST(SlotTableByIndex, AnIndexAnswersWhoeverIsBoundThereNow)
{
	CSourceSharpSlotTable table;
	FakeShell dead = {1}, alive = {2};

	table.Bind(30, &dead);
	EXPECT_EQ(&dead, table.ResolveByIndex(30));

	table.Unbind(30);
	EXPECT_TRUE(table.ResolveByIndex(30) == NULL);

	table.Bind(30, &alive);
	EXPECT_EQ(&alive, table.ResolveByIndex(30));
}

TEST(SlotTableByIndex, AnIndexOutsideTheTableAnswersNullRatherThanReadingPastTheEnd)
{
	CSourceSharpSlotTable table;
	FakeShell shell = {1};
	table.Bind(0, &shell);

	EXPECT_TRUE(table.ResolveByIndex(-1) == NULL);
	EXPECT_TRUE(table.ResolveByIndex(NUM_ENT_ENTRIES) == NULL);
	EXPECT_TRUE(table.ResolveByIndex(ENT_ENTRY_MASK) == NULL);
}

//-----------------------------------------------------------------------------
// HandleOf is the inverse: an index in, something holdable out.
//-----------------------------------------------------------------------------
TEST(SlotTableByIndex, HandleOfGivesBackTheHandleBindReturned)
{
	CSourceSharpSlotTable table;
	FakeShell shell = {1};

	const CBaseHandle h = table.Bind(31, &shell);

	EXPECT_TRUE(table.HandleOf(31) == h);
	EXPECT_EQ(&shell, table.Resolve(table.HandleOf(31)));

	table.Unbind(31);
	EXPECT_FALSE(table.HandleOf(31).IsValid());
}

//=============================================================================
// ISSUE -- THE HANDLE A BIND WOULD PRODUCE, WITHOUT BINDING
//
// plan_entity.md §3.3 gives the table the job CGlobalEntityList::AddEntityAtSlot
// used to do on this side: choosing the serial an entity's handle carries. That
// call issued AND bound in one step and called SetRefEHandle from inside itself,
// which is why the shim's `SetRefEHandle( g_pEntityList->Add...( ... ) )` called
// SetRefEHandle twice per entity. Here the two steps are two calls, because the
// shell's own SetRefEHandle is the single binder -- so the registration path
// needs the handle BEFORE anything is bound.
//
// The property that makes that safe is the one below: what IssueHandle answers
// is exactly what the following Bind returns, and an entry that already has an
// occupant answers nothing at all.
//=============================================================================

TEST(SlotTableIssue, TheIssuedHandleIsTheOneTheFollowingBindReturns)
{
	CSourceSharpSlotTable table;
	FakeShell shell = {1};

	// THE ENTRY IS AGED FIRST, and that is not decoration: a fresh entry's
	// serial is zero, so on an untouched table an issuer that ignored the
	// serial array entirely and always answered zero would pass this test. Two
	// generations put a non-zero serial in the entry, so the assertion below
	// can tell "read the entry's serial" from "made one up".
	FakeShell aged = {9};
	table.Bind(31, &aged);
	table.Unbind(31);
	table.Bind(31, &aged);
	table.Unbind(31);
	ASSERT_NE(0, table.SerialAt(31));

	const CBaseHandle issued = table.IssueHandle(31);

	ASSERT_TRUE(issued.IsValid());
	EXPECT_EQ(31, issued.GetEntryIndex());
	EXPECT_EQ(table.SerialAt(31), issued.GetSerialNumber());

	const CBaseHandle bound = table.Bind(31, &shell);

	EXPECT_TRUE(bound == issued) << "issue and bind disagreed about the serial: the entity would hold a "
									"handle the table cannot resolve";
	EXPECT_EQ(&shell, table.Resolve(issued));
}

TEST(SlotTableIssue, IssuingDoesNotBindAndDoesNotMoveTheSerial)
{
	CSourceSharpSlotTable table;

	const int nBefore = table.SerialAt(12);

	const CBaseHandle first = table.IssueHandle(12);
	const CBaseHandle second = table.IssueHandle(12);

	EXPECT_TRUE(first == second) << "issuing twice handed out two identities";
	EXPECT_EQ(nBefore, table.SerialAt(12));
	EXPECT_FALSE(table.IsBound(12));
	EXPECT_EQ(0, table.BoundCount());
	EXPECT_EQ(0, table.Binds());
	EXPECT_EQ(NULL, table.Resolve(first)) << "an issued-but-unbound handle resolved to something";
}

//-----------------------------------------------------------------------------
// An occupied entry answers an INVALID handle rather than the occupant's.
//
// Handing back the live handle would let a second entity take the first one's
// identity, and Bind's "an entry has ONE occupant" refusal would then arrive one
// step after the caller had already stored it.
//-----------------------------------------------------------------------------
TEST(SlotTableIssue, AnOccupiedEntryIssuesNothing)
{
	CSourceSharpSlotTable table;
	FakeShell a = {1}, b = {2};

	const CBaseHandle ha = table.Bind(44, &a);
	ASSERT_TRUE(ha.IsValid());

	const CBaseHandle refused = table.IssueHandle(44);

	EXPECT_FALSE(refused.IsValid());
	EXPECT_FALSE(refused == ha);

	// And the occupant is untouched by the ask.
	EXPECT_EQ(&a, table.Resolve(ha));
	EXPECT_EQ(1, table.BoundCount());
	(void)b;
}

TEST(SlotTableIssue, AnEntryOutsideTheTableIssuesNothing)
{
	CSourceSharpSlotTable table;

	EXPECT_FALSE(table.IssueHandle(-1).IsValid());
	EXPECT_FALSE(table.IssueHandle(NUM_ENT_ENTRIES).IsValid());
	EXPECT_FALSE(table.IssueHandle(NUM_ENT_ENTRIES + 4096).IsValid());
}

//-----------------------------------------------------------------------------
// THE STALE-HANDLE PROPERTY, STATED OVER THE ISSUER.
//
// SerialReuse below proves it of Bind. It has to hold of IssueHandle too, or
// the registration path would hand a new entity the DEAD one's handle: the
// entity list bumped the serial on removal and this must read the bumped value,
// not the one the entry carried when it was last occupied.
//-----------------------------------------------------------------------------
TEST(SlotTableIssue, AfterAnUnbindTheNextIssueIsADifferentIdentity)
{
	CSourceSharpSlotTable table;
	FakeShell first = {1}, second = {2};

	const CBaseHandle hOld = table.Bind(63, &first);
	ASSERT_TRUE(hOld.IsValid());

	table.Unbind(63);

	const CBaseHandle hNew = table.IssueHandle(63);

	ASSERT_TRUE(hNew.IsValid());
	EXPECT_FALSE(hNew == hOld) << "the entry re-issued the dead entity's handle";
	EXPECT_NE(hOld.GetSerialNumber(), hNew.GetSerialNumber());

	table.Bind(63, &second);

	EXPECT_EQ(&second, table.Resolve(hNew));
	EXPECT_EQ(NULL, table.Resolve(hOld)) << "the dead entity's handle came back with its successor";
}

//-----------------------------------------------------------------------------
// ENTRY ZERO IS THE WORLD, AND IT ISSUES LIKE ANY OTHER ENTRY.
//
// This is the standing regression in this port -- a default handle is entry
// zero and a VALID one -- said about the issuer. worldspawn is bound at entry 0
// on every map load by CWorld::AttachToEdictZero, so an issuer that treated
// entry 0 as "nothing" would refuse the one entity every trace that hits
// nothing hits.
//-----------------------------------------------------------------------------
TEST(SlotTableIssue, EntryZeroIssuesAHandleTheWorldCanBeBoundWith)
{
	CSourceSharpSlotTable table;
	FakeShell world = {0};

	const CBaseHandle hWorld = table.IssueHandle(0);

	ASSERT_TRUE(hWorld.IsValid());
	EXPECT_EQ(0, hWorld.GetEntryIndex());

	ASSERT_TRUE(table.BindAt(hWorld, &world));
	EXPECT_EQ(&world, table.Resolve(hWorld));
	EXPECT_EQ(&world, table.ResolveByIndex(0));
}

//-----------------------------------------------------------------------------
// The engine-invisible half issues exactly like the edict half.
//-----------------------------------------------------------------------------
TEST(SlotTableIssue, TheEngineInvisibleHalfIssuesTheSameWay)
{
	CSourceSharpSlotTable table;
	FakeShell shell = {7};

	const int nEntry = MAX_EDICTS + 17;

	ASSERT_FALSE(CSourceSharpSlotTable::IsEdictEntry(nEntry));

	const CBaseHandle issued = table.IssueHandle(nEntry);

	ASSERT_TRUE(issued.IsValid());
	EXPECT_TRUE(table.Bind(nEntry, &shell) == issued);
	EXPECT_EQ(&shell, table.Resolve(issued));
}

//=============================================================================
// CLEARING BETWEEN MAPS -- AND WHY IT IS NOT Reset()
//
// The serials are the only thing in this table that must survive a level
// change, and Reset()'s own comment used to argue the opposite: "nothing holds
// a handle across a level change". That was true while CGlobalEntityList
// assigned the serials. It stopped being true the moment this table did,
// because the EDICT holds one -- edict_t::m_NetworkSerialNumber, the mirror
// standalone_netserial.h exists for, which the engine keeps across a map load
// and which SV_PackEntity compares to decide whether an edict changed
// occupants.
//
// THIS WAS A CRASH, twice, on the live gate: the second save/load in one
// process died with SIGSEGV inside the engine's static-prop draw, on the first
// frame after the client's level init, while every step of the first pass
// passed. The untouched tree survived the same sequence twice on the same box.
//=============================================================================

TEST(SlotTableClear, ClearingBetweenMapsBumpsEverySerialRatherThanRewindingIt)
{
	CSourceSharpSlotTable table;
	FakeShell a = {1}, b = {2};

	const CBaseHandle hFirstMap = table.Bind(5, &a);
	ASSERT_TRUE(hFirstMap.IsValid());

	table.ClearBindings();

	const CBaseHandle hSecondMap = table.Bind(5, &b);

	ASSERT_TRUE(hSecondMap.IsValid());
	EXPECT_NE(hFirstMap.GetSerialNumber(), hSecondMap.GetSerialNumber())
		<< "the entry re-issued the previous map's serial. The edict's mirror "
		   "does not move, so the engine packs this entity as a CONTINUATION of "
		   "the dead one.";
	EXPECT_FALSE(hSecondMap == hFirstMap);
	EXPECT_EQ(NULL, table.Resolve(hFirstMap)) << "the previous map's handle still resolves";
	EXPECT_EQ(&b, table.Resolve(hSecondMap));
}

//-----------------------------------------------------------------------------
// Reset IS still the rewind, and it is the constructor's and the tests'. Kept
// apart from ClearBindings on purpose: a fresh table has to start somewhere,
// and a test that wants a known serial has to be able to ask for one.
//-----------------------------------------------------------------------------
TEST(SlotTableClear, ResetIsStillTheRewindAndIsNotWhatALevelChangeUses)
{
	CSourceSharpSlotTable table;
	FakeShell a = {1};

	table.Bind(5, &a);
	table.Unbind(5);
	ASSERT_NE(0, table.SerialAt(5));

	table.Reset();

	EXPECT_EQ(0, table.SerialAt(5)) << "Reset no longer rewinds. The constructor calls it, so a fresh table "
									   "would start on whatever was in the memory it was handed.";
}

//-----------------------------------------------------------------------------
// It clears, and it clears everything -- an entry whose shell never unbound
// itself included, which is the only reason the walk exists at all.
//-----------------------------------------------------------------------------
TEST(SlotTableClear, ClearingLeavesNothingBoundAnywhereInEitherHalf)
{
	CSourceSharpSlotTable table;
	FakeShell a = {1}, b = {2}, c = {3};

	const CBaseHandle hEdict = table.Bind(7, &a);
	const CBaseHandle hInvisible = table.Bind(MAX_EDICTS + 3, &b);
	const CBaseHandle hLast = table.Bind(kLastEntry, &c);

	ASSERT_EQ(3, table.BoundCount());

	table.ClearBindings();

	EXPECT_EQ(0, table.BoundCount());
	EXPECT_EQ(NULL, table.Resolve(hEdict));
	EXPECT_EQ(NULL, table.Resolve(hInvisible));
	EXPECT_EQ(NULL, table.Resolve(hLast));
	EXPECT_EQ(NULL, table.ResolveByIndex(7));
	EXPECT_EQ(NULL, table.ResolveByIndex(MAX_EDICTS + 3));
	EXPECT_EQ(NULL, table.ResolveByIndex(kLastEntry));
}

//-----------------------------------------------------------------------------
// And an entry that was ALREADY unbound is not bumped a second time -- which is
// the ordinary case, because every shell unbinds itself in its destructor and
// the level teardown runs before this does.
//-----------------------------------------------------------------------------
TEST(SlotTableClear, ClearingDoesNotBumpAnEntryThatWasAlreadyUnbound)
{
	CSourceSharpSlotTable table;
	FakeShell a = {1};

	table.Bind(9, &a);
	table.Unbind(9);

	const int nAfterTheShellWentAway = table.SerialAt(9);

	table.ClearBindings();

	EXPECT_EQ(nAfterTheShellWentAway, table.SerialAt(9))
		<< "the level teardown bumped a serial nobody was using. Doing that "
		   "once per map per entry leaks the serial space at 8192 a map.";
	EXPECT_EQ(0, table.SerialAt(10)) << "an entry that was never bound at all was bumped";
}

//=============================================================================
// COUNTS
//=============================================================================

TEST(SlotTableCounts, BindAndUnbindMaintainTheCount)
{
	CSourceSharpSlotTable table;
	FakeShell shells[5] = {{0}, {1}, {2}, {3}, {4}};

	for (int i = 0; i < 5; ++i)
		table.Bind(400 + i, &shells[i]);

	EXPECT_EQ(5, table.BoundCount());

	table.Unbind(402);
	EXPECT_EQ(4, table.BoundCount());

	// A refused unbind must not move it.
	table.Unbind(402);
	EXPECT_EQ(4, table.BoundCount());

	// Nor a refused bind.
	EXPECT_FALSE(table.Bind(400, &shells[0]).IsValid());
	EXPECT_EQ(4, table.BoundCount());
}

TEST(SlotTableCounts, TheCountSurvivesTheWholeTableBeingFilled)
{
	CSourceSharpSlotTable table;
	FakeShell shell = {1};

	for (int i = 0; i < NUM_ENT_ENTRIES; ++i)
		ASSERT_TRUE(table.Bind(i, &shell).IsValid()) << "entry " << i;

	EXPECT_EQ(NUM_ENT_ENTRIES, table.BoundCount());

	for (int i = 0; i < NUM_ENT_ENTRIES; ++i)
		table.Unbind(i);

	EXPECT_EQ(0, table.BoundCount());
}

//=============================================================================
// THE EDICT SERIAL MIRROR
//
// "serial mirrored into the edict as standalone_netserial.h does" -- step 1c's
// paragraph. The engine keys "what did I last send for this edict" on
// edict_t::m_NetworkSerialNumber, and an edict that changes occupants without
// that field moving is packed as a continuation of the dead entity. The rule
// and the abort it prevents are written up in standalone_netserial.h; what is
// checked here is that the serial the SLOT TABLE assigns is the one that
// reaches the field.
//=============================================================================

TEST(SlotTableMirror, TheSerialTheTableAssignedIsWhatReachesTheEdict)
{
	CSourceSharpSlotTable table;
	FakeShell shell = {1};
	FakeEdict edict;
	edict.m_NetworkSerialNumber = -1;

	const CBaseHandle h = table.Bind(64, &shell);
	StandaloneNetSerial_Mirror(&edict, (unsigned long)h.GetSerialNumber());

	EXPECT_EQ(h.GetSerialNumber(), (int)edict.m_NetworkSerialNumber);
}

//-----------------------------------------------------------------------------
// A reused edict publishes a DIFFERENT serial to its second occupant. This is
// the whole reason the mirror exists: same edict, same entry, two entities, and
// the engine has to be able to tell them apart.
//-----------------------------------------------------------------------------
TEST(SlotTableMirror, AReusedEdictPublishesADifferentSerialForTheNewOccupant)
{
	CSourceSharpSlotTable table;
	FakeShell dead = {1}, alive = {2};
	FakeEdict edict;
	edict.m_NetworkSerialNumber = -1;

	const CBaseHandle hOld = table.Bind(65, &dead);
	StandaloneNetSerial_Mirror(&edict, (unsigned long)hOld.GetSerialNumber());
	const short nFirst = edict.m_NetworkSerialNumber;

	table.Unbind(65);

	const CBaseHandle hNew = table.Bind(65, &alive);
	StandaloneNetSerial_Mirror(&edict, (unsigned long)hNew.GetSerialNumber());

	EXPECT_NE(nFirst, edict.m_NetworkSerialNumber);
	EXPECT_EQ(hNew.GetSerialNumber(), (int)edict.m_NetworkSerialNumber);
}

//-----------------------------------------------------------------------------
// The mirror masks to ten bits, which is what stock's EHANDLE props put on the
// wire. Pinned against STANDALONE_NETSERIAL_BITS rather than against a typed-in
// 1023, so the fixture cannot drift from the header it is checking.
//-----------------------------------------------------------------------------
TEST(SlotTableMirror, TheMirrorMasksToTenBits)
{
	FakeEdict edict;
	edict.m_NetworkSerialNumber = -1;

	const unsigned long nWide = (1ul << STANDALONE_NETSERIAL_BITS) + 5ul;
	StandaloneNetSerial_Mirror(&edict, nWide);

	EXPECT_EQ(5, (int)edict.m_NetworkSerialNumber);
	EXPECT_EQ((int)(nWide & ((1ul << STANDALONE_NETSERIAL_BITS) - 1ul)), (int)edict.m_NetworkSerialNumber);
}

//-----------------------------------------------------------------------------
// A server-only entity has no edict, and mirroring onto nothing is a no-op
// rather than a crash. Every entry above MAX_EDICTS is one of these.
//-----------------------------------------------------------------------------
TEST(SlotTableMirror, AnEntityWithNoEdictMirrorsNothing)
{
	CSourceSharpSlotTable table;
	FakeShell relay = {1};

	const CBaseHandle h = table.Bind(kFirstLogicalEntry, &relay);
	StandaloneNetSerial_Mirror((FakeEdict*)NULL, (unsigned long)h.GetSerialNumber());

	SUCCEED();
}

//=============================================================================
// THE RESOLVER'S SHAPE
//
// The typed resolver in the side's own TU is one line on top of Resolve():
//
//     return static_cast< CStandaloneEntity * >( SourceSharp_SlotTable().Resolve( h ) );
//
// It is the one downcast plan_entity.md permits on an entity path and the only
// entry in tools/entity-gates-allowlist. It cannot be exercised from here --
// CStandaloneEntity needs the whole SDK -- so what is checked here is the thing
// that makes the cast sound: the void * that comes back is byte-for-byte the
// pointer that went in, so a static_cast back to the stored type recovers it.
//=============================================================================

TEST(SlotTableResolver, ThePointerOutIsTheSameAddressAsThePointerIn)
{
	CSourceSharpSlotTable table;
	FakeShell shell = {99};

	const CBaseHandle h = table.Bind(70, &shell);

	// The round trip a typed resolver makes: concrete pointer -> void * ->
	// concrete pointer, with the serial checked in the middle.
	void* pErased = table.Resolve(h);
	FakeShell* pBack = static_cast<FakeShell*>(pErased);

	ASSERT_TRUE(pBack != NULL);
	EXPECT_EQ(&shell, pBack);
	EXPECT_EQ(99, pBack->nWhich);
}

//-----------------------------------------------------------------------------
// And the miss comes back as a NULL a static_cast keeps NULL -- so the typed
// resolver answers NULL rather than casting a stale pointer.
//-----------------------------------------------------------------------------
TEST(SlotTableResolver, AStaleHandleRoundTripsToANullOfTheTargetType)
{
	CSourceSharpSlotTable table;
	FakeShell dead = {1}, alive = {2};

	const CBaseHandle hOld = table.Bind(71, &dead);
	table.Unbind(71);
	table.Bind(71, &alive);

	FakeShell* pBack = static_cast<FakeShell*>(table.Resolve(hOld));

	EXPECT_TRUE(pBack == NULL);
}

//=============================================================================
// THE TABLE CANNOT ITERATE -- five programs that must NOT compile.
//
// build_must_not_compile in src/buildcpptests compiles this file once per value
// of SLOT_TABLE_NOCOMPILE with -fsyntax-only and requires g++ to REFUSE, at the
// named error rather than at a typo. A probe that compiles is reported as the
// failure it is, so ADDING any of these five members to CSourceSharpSlotTable
// turns a green leg red -- which is the only way "it cannot iterate" is a fact
// about the build rather than a sentence in a comment.
//
// The five are not arbitrary. They are the five shapes somebody would actually
// reach for, and one of them -- the ActiveCount/ActiveAt pair -- is the shape
// the tree's OTHER eight-thousand-entry table already has
// (game/shared/entity_slots.h), where a per-frame drain genuinely does need to
// walk what is open. That near-miss is the reason this file exists: the two
// tables look alike, and only one of them is allowed to be walked.
//=============================================================================
#if defined(SLOT_TABLE_NOCOMPILE)

void SlotTableNoCompileProbe(CSourceSharpSlotTable& table);

void SlotTableNoCompileProbe(CSourceSharpSlotTable& table)
{
#if SLOT_TABLE_NOCOMPILE == 1
	// A range-for: the STL spelling of "walk it".
	for (void* p : table)
		(void)p;
#elif SLOT_TABLE_NOCOMPILE == 2
	// entity_slots.h's pair, which is what makes this probe a near-miss rather
	// than a strawman.
	for (int i = 0; i < table.ActiveCount(); ++i)
		(void)table.ActiveAt(i);
#elif SLOT_TABLE_NOCOMPILE == 3
	// A pointer into the storage -- the shortest path from "cannot iterate" to
	// a caller walking it anyway. entity_slots.h has this one too.
	(void)table.Slots();
#elif SLOT_TABLE_NOCOMPILE == 4
	// The engine list's own spelling: CBaseEntityList::FirstHandle/NextHandle.
	(void)table.FirstHandle();
#elif SLOT_TABLE_NOCOMPILE == 5
	// The callback spelling, which needs no cursor and is the easiest of the
	// five to add "just for one caller".
	table.ForEach(SlotTableNoCompileProbe);
#else
#error "SLOT_TABLE_NOCOMPILE must be 1..5"
#endif
}

#endif // SLOT_TABLE_NOCOMPILE
