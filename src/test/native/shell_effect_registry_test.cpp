//========= SourceSharp — the registry the de-entified effects hang on =======//
//
// shell_effect_registry.h replaces a guarantee rather than adding a helper. The
// `_render` shells used to hold CHandles, which read as null of their own
// accord once the engine destroyed the entity behind them; the effects are not
// entities any more (shell_particle_effect.h), so the row is removed by the
// EFFECT, from its destructor, and the shell must not remove it again.
//
// Every failure that inversion can produce is silent:
//
//   * Release-then-Remove deletes a NEIGHBOUR'S row. That neighbour's effect is
//     then pointed at by nothing and freed by nobody — a leak that only shows
//     up as a level change getting slower, in a build nobody is measuring.
//   * a forwards walk that removes rows skips the row after each removal, so a
//     shutdown that removes two adjacent effects leaves one behind.
//   * `while ( Count() ) list[0]->Release()` hangs the client on a level change
//     the day somebody forgets to hook the gone callback at a factory in a
//     different file.
//
// None of those is a crash on the frame it happens, which is why they are
// pinned here rather than left to a play-test.
//
// Engine-free: a std::vector of a row struct and a fake effect that removes its
// own row when released, which is exactly the contract the real one has.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../game/shared/shell_effect_registry.h"

#include <vector>
#include <fstream>
#include <sstream>
#include <string>
#include <sys/stat.h>

namespace
{

//-----------------------------------------------------------------------------
// A CUtlVector's surface, over a std::vector, because the header is templated
// on the container precisely so this file does not need the engine's one.
//
// Count() returns int and Remove(i) removes by index, which is what
// shell_effect_registry.h uses and all it uses.
//-----------------------------------------------------------------------------
template <typename T>
struct FakeList
{
	std::vector<T> v;

	int Count() const { return (int)v.size(); }

	T& operator[](int i) { return v[i]; }

	const T& operator[](int i) const { return v[i]; }

	void Remove(int i) { v.erase(v.begin() + i); }

	int AddToTail(const T& row)
	{
		v.push_back(row);
		return (int)v.size() - 1;
	}
};

struct FakeEffect;

struct Row
{
	unsigned int hOwner;
	FakeEffect* pEffect;
};

typedef FakeList<Row> List;

//-----------------------------------------------------------------------------
// A stand-in for CShellParticleEffect. Release() does what the real one does:
// it reaches the registry through the gone callback and removes the row, and
// only then is the object gone.
//
// m_bHooked is what a factory that forgot SetGoneCallback looks like. It is not
// a hypothetical: it is one line at a call site in another file, and the
// consequence lands here.
//-----------------------------------------------------------------------------
struct FakeEffect
{
	List* m_pList;
	bool m_bHooked;
	int m_nReleases;

	FakeEffect(List* pList, bool bHooked = true)
		: m_pList(pList)
		, m_bHooked(bHooked)
		, m_nReleases(0)
	{
	}

	void Release()
	{
		m_nReleases++;

		if (m_bHooked)
			ShellEffects_Forget(*m_pList, this);
	}
};

void Add(List& list, unsigned int hOwner, FakeEffect* pEffect)
{
	Row row;
	row.hOwner = hOwner;
	row.pEffect = pEffect;
	list.AddToTail(row);
}

//=============================================================================
// Find
//=============================================================================

TEST(ShellEffectRegistry, FindAnswersMinusOneForAnEmptyList)
{
	List list;
	EXPECT_EQ(-1, ShellEffects_Find(list, 7u));
}

TEST(ShellEffectRegistry, FindAnswersTheIndexOfTheOwner)
{
	List list;
	FakeEffect a(&list), b(&list), c(&list);

	Add(list, 10u, &a);
	Add(list, 20u, &b);
	Add(list, 30u, &c);

	EXPECT_EQ(0, ShellEffects_Find(list, 10u));
	EXPECT_EQ(1, ShellEffects_Find(list, 20u));
	EXPECT_EQ(2, ShellEffects_Find(list, 30u));
	EXPECT_EQ(-1, ShellEffects_Find(list, 40u));
}

TEST(ShellEffectRegistry, FindAnswersTheFirstOfTwoRowsForOneOwner)
{
	// A second row for one owner is a bug elsewhere. The answer still has to be
	// the same one every time it is asked, or the bug reads as intermittent.
	List list;
	FakeEffect a(&list), b(&list);

	Add(list, 10u, &a);
	Add(list, 10u, &b);

	EXPECT_EQ(0, ShellEffects_Find(list, 10u));
}

//=============================================================================
// Forget
//=============================================================================

TEST(ShellEffectRegistry, ForgetRemovesTheRowAndLeavesTheOthersWhereTheyPoint)
{
	List list;
	FakeEffect a(&list), b(&list), c(&list);

	Add(list, 10u, &a);
	Add(list, 20u, &b);
	Add(list, 30u, &c);

	ShellEffects_Forget(list, &b);

	ASSERT_EQ(2, list.Count());
	EXPECT_EQ(&a, list[0].pEffect);
	EXPECT_EQ(&c, list[1].pEffect);
	EXPECT_EQ(10u, list[0].hOwner);
	EXPECT_EQ(30u, list[1].hOwner);
}

TEST(ShellEffectRegistry, ForgetIsANoOpForAnEffectThatIsNotInTheList)
{
	List list;
	FakeEffect a(&list), stranger(&list);

	Add(list, 10u, &a);

	ShellEffects_Forget(list, &stranger);

	ASSERT_EQ(1, list.Count());
	EXPECT_EQ(&a, list[0].pEffect);
}

TEST(ShellEffectRegistry, ForgetRemovesBOTHRowsWhenTwoPointAtOneEffect)
{
	// The failure this half exists for: leave the second row behind and the
	// next sweep dereferences a freed pointer. A forwards walk that removed the
	// first would skip straight over the second, which is why the loop counts
	// down.
	List list;
	FakeEffect a(&list), doubled(&list);

	Add(list, 10u, &doubled);
	Add(list, 20u, &doubled);
	Add(list, 30u, &a);

	ShellEffects_Forget(list, &doubled);

	ASSERT_EQ(1, list.Count());
	EXPECT_EQ(&a, list[0].pEffect);
}

TEST(ShellEffectRegistry, ForgetRemovesADJACENTRowsForOneEffect)
{
	// The same skip, in the shape it actually occurs in: two rows next to each
	// other. A forwards walk removes index 1, the list shifts, the loop moves
	// to index 2, and the row that just became index 1 is never looked at.
	List list;
	FakeEffect a(&list), doubled(&list), c(&list);

	Add(list, 10u, &a);
	Add(list, 20u, &doubled);
	Add(list, 30u, &doubled);
	Add(list, 40u, &c);

	ShellEffects_Forget(list, &doubled);

	ASSERT_EQ(2, list.Count());
	EXPECT_EQ(&a, list[0].pEffect);
	EXPECT_EQ(&c, list[1].pEffect);
}

//=============================================================================
// ReleaseAll
//=============================================================================

TEST(ShellEffectRegistry, ReleaseAllOnAnEmptyListReleasesNothing)
{
	List list;
	EXPECT_EQ(0, ShellEffects_ReleaseAll(list));
	EXPECT_EQ(0, list.Count());
}

TEST(ShellEffectRegistry, ReleaseAllEmptiesTheListAndReleasesEachOnce)
{
	List list;
	FakeEffect a(&list), b(&list), c(&list);

	Add(list, 10u, &a);
	Add(list, 20u, &b);
	Add(list, 30u, &c);

	EXPECT_EQ(3, ShellEffects_ReleaseAll(list));
	EXPECT_EQ(0, list.Count());

	EXPECT_EQ(1, a.m_nReleases);
	EXPECT_EQ(1, b.m_nReleases);
	EXPECT_EQ(1, c.m_nReleases);
}

TEST(ShellEffectRegistry, ReleaseAllStopsRatherThanSpinningWhenTheCallbackIsMissing)
{
	// A factory that forgot SetGoneCallback. Without the bound this is a frozen
	// client on every level change; with it, it is a leak the caller can see.
	List list;
	FakeEffect a(&list), unhooked(&list, false);

	Add(list, 10u, &a);
	Add(list, 20u, &unhooked);

	// One released, then the list stopped shrinking.
	EXPECT_EQ(-1, ShellEffects_ReleaseAll(list));

	EXPECT_EQ(1, a.m_nReleases);
	EXPECT_EQ(1, unhooked.m_nReleases);
	EXPECT_EQ(1, list.Count());
}

TEST(ShellEffectRegistry, ReleaseAllStopsOnTheVeryFirstRowWhenNothingIsHooked)
{
	List list;
	FakeEffect unhooked(&list, false);

	Add(list, 10u, &unhooked);

	// Nothing released before it gave up, so the answer is a negative zero's
	// only honest spelling: zero, with a list that is not empty.
	EXPECT_EQ(0, ShellEffects_ReleaseAll(list));
	EXPECT_EQ(1, list.Count());
	EXPECT_EQ(1, unhooked.m_nReleases);
}

//=============================================================================
// The inversion itself: what the CALLER must not do.
//=============================================================================

TEST(ShellEffectRegistry, ReleaseAloneIsEnoughAndRemovingAsWellEatsANeighbour)
{
	// This is the rule the header states, asserted from both sides so that the
	// "do not also Remove" comment is a fact rather than advice.
	List list;
	FakeEffect a(&list), b(&list);

	Add(list, 10u, &a);
	Add(list, 20u, &b);

	// The right way: release the row's effect and stop.
	const int iEntry = ShellEffects_Find(list, 10u);
	ASSERT_EQ(0, iEntry);
	list[iEntry].pEffect->Release();

	ASSERT_EQ(1, list.Count());
	EXPECT_EQ(&b, list[0].pEffect) << "the release removed its own row";

	// The wrong way, shown on a fresh pair: the extra Remove( iEntry ) takes the
	// neighbour that shifted into that slot.
	List wrong;
	FakeEffect c(&wrong), d(&wrong);

	Add(wrong, 10u, &c);
	Add(wrong, 20u, &d);

	const int iWrong = ShellEffects_Find(wrong, 10u);
	wrong[iWrong].pEffect->Release();
	wrong.Remove(iWrong);

	EXPECT_EQ(0, wrong.Count()) << "d's row was eaten by the redundant Remove";
}

} // namespace

//=============================================================================
// PART 2 — AND EVERY SHELL ASKS IT, WHICH IS A DIFFERENT KIND OF FACT.
//
// Everything above DRIVES the header: a std::vector, a fake effect, and the
// contract exercised. That says the rule is right. It says nothing at all about
// whether anybody uses it — and when this lane arrived, six of the ten `_render`
// shells in the client shim had the Find loop WRITTEN OUT AGAIN beside the
// include of the file that holds it.
//
// A second copy of a rule is not a style problem here. ShellEffects_Find's
// FIRST-match is argued in the header: one managed entity owns at most one
// effect of a family, so a second row for one owner is a bug elsewhere, and
// finding the first keeps the answer STABLE while that bug is found. A copy that
// answers "last" or "any" makes the same bug intermittent, in one family only,
// and nothing says which.
//
// So this half is a source scan, and it is an ABSENCE paired with a count: no
// shell declares its own lookup, AND the shared one is asked the number of times
// the shells between them need it. The count is exact rather than a floor
// because a floor passes for a shell that quietly grew a copy beside its calls.
//
// Lane 4c-render, plan_shim.md §6.
//=============================================================================

namespace
{

// Every `_render` shell in the client shim that keeps an owner-keyed pool, with
// how many times it asks the shared lookup. Written out rather than globbed so
// that a shell DELETED is a red test and not a silently shorter list.
// nOwnLoops is how many hand-rolled lookups the shell is ALLOWED, which is zero
// everywhere but one — and the one is not a leftover.
struct Shell_t
{
	const char* pszFile;
	size_t nCalls;
	size_t nOwnLoops;
};

} // namespace
