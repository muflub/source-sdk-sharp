//========= SourceSharp — the leaf-system link a de-entified renderable keeps //
//
// shell_leaf_link.h is three guards standing in for something C_BaseEntity used
// to do for free, and all three fail quietly:
//
//   a second add strands the first leaf-system row, because AddRenderable
//   overwrites the handle it wrote last time and nothing can reach the old one;
//
//   a remove on something that was never added hands RemoveRenderable an
//   INVALID handle, which survives today only because IsValidIndex happens to
//   reject it;
//
//   a move that is not reported leaves the object drawing from the leaves it
//   was FIRST added to, which looks completely correct until the camera is
//   somewhere that should see it.
//
// The last is the one worth the file. It is not a crash, not a log line, and not
// reproducible from a screenshot taken where the object started.
//
// The fakes here RECORD rather than assert, so the tests can say what did NOT
// happen as well as what did -- "the leaf system was not told" is the actual
// content of two of these.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../game/shared/shell_leaf_link.h"

#include <vector>

namespace
{

const int kInvalidHandle = -1;

struct FakeRenderable;

//-----------------------------------------------------------------------------
// The client leaf system's three calls, recorded.
//
// AddRenderable writes the handle THROUGH THE RENDERABLE, which is what the real
// one does -- `pRenderable->RenderHandle() = ...` inside NewRenderable -- and it
// is the reason a double add is a stranded row rather than a duplicate.
//-----------------------------------------------------------------------------
struct FakeLeaf
{
	std::vector<int> added; // handles handed out
	std::vector<int> removed; // handles removed
	std::vector<int> changed; // handles reported as moved
	int nNextHandle;

	FakeLeaf()
		: nNextHandle(100)
	{
	}

	void AddRenderable(FakeRenderable* pRenderable, int group);

	void RemoveRenderable(int handle) { removed.push_back(handle); }

	void RenderableChanged(int handle) { changed.push_back(handle); }
};

struct FakeRenderable
{
	int m_hRenderHandle;

	FakeRenderable()
		: m_hRenderHandle(kInvalidHandle)
	{
	}

	int& RenderHandle() { return m_hRenderHandle; }

	bool IsInLeafSystem() const { return m_hRenderHandle != kInvalidHandle; }
};

void FakeLeaf::AddRenderable(FakeRenderable* pRenderable, int group)
{
	const int h = nNextHandle++;
	added.push_back(h);
	pRenderable->RenderHandle() = h; // exactly what NewRenderable does
}

//=============================================================================
// Add
//=============================================================================

TEST(ShellLeafLink, AddPutsAnUnlinkedRenderableIn)
{
	FakeLeaf leaf;
	FakeRenderable r;

	ShellLeaf_Add(&leaf, &r, 0);

	ASSERT_EQ(1u, leaf.added.size());
	EXPECT_TRUE(r.IsInLeafSystem());
	EXPECT_EQ(leaf.added[0], r.RenderHandle());
}

TEST(ShellLeafLink, AddTWICEDoesNotStrandTheFirstRow)
{
	// Without the guard the second AddRenderable overwrites the handle and the
	// first row can never be removed by anybody.
	FakeLeaf leaf;
	FakeRenderable r;

	ShellLeaf_Add(&leaf, &r, 0);
	const int hFirst = r.RenderHandle();

	ShellLeaf_Add(&leaf, &r, 0);

	EXPECT_EQ(1u, leaf.added.size()) << "the second add was refused";
	EXPECT_EQ(hFirst, r.RenderHandle()) << "the handle is the one that can be removed";
}

//=============================================================================
// Remove
//=============================================================================

TEST(ShellLeafLink, RemoveTakesTheHandleTheAddHandedOut)
{
	FakeLeaf leaf;
	FakeRenderable r;

	ShellLeaf_Add(&leaf, &r, 0);
	const int h = r.RenderHandle();

	ShellLeaf_Remove(&leaf, &r);

	ASSERT_EQ(1u, leaf.removed.size());
	EXPECT_EQ(h, leaf.removed[0]);
}

TEST(ShellLeafLink, RemoveOnSomethingNeverAddedTellsTheLeafSystemNOTHING)
{
	FakeLeaf leaf;
	FakeRenderable r;

	ShellLeaf_Remove(&leaf, &r);

	EXPECT_TRUE(leaf.removed.empty()) << "an INVALID handle must not reach RemoveRenderable at all";
}

//=============================================================================
// Moved — the silent one
//=============================================================================

TEST(ShellLeafLink, MovedReportsTheHandleOnceLinked)
{
	FakeLeaf leaf;
	FakeRenderable r;

	ShellLeaf_Add(&leaf, &r, 0);
	const int h = r.RenderHandle();

	ShellLeaf_Moved(&leaf, &r);
	ShellLeaf_Moved(&leaf, &r);

	ASSERT_EQ(2u, leaf.changed.size()) << "every move is reported, not just the first";
	EXPECT_EQ(h, leaf.changed[0]);
	EXPECT_EQ(h, leaf.changed[1]);
}

TEST(ShellLeafLink, MovedBeforeTheAddTellsTheLeafSystemNOTHING)
{
	// The ordinary factory shape: place the object, then add it. There is no
	// handle to report against yet, and reporting one anyway is a call into the
	// leaf system with INVALID.
	FakeLeaf leaf;
	FakeRenderable r;

	ShellLeaf_Moved(&leaf, &r);

	EXPECT_TRUE(leaf.changed.empty());

	// ...and adding afterwards still works, so the guard is not a latch.
	ShellLeaf_Add(&leaf, &r, 0);
	ShellLeaf_Moved(&leaf, &r);

	ASSERT_EQ(1u, leaf.changed.size());
	EXPECT_EQ(r.RenderHandle(), leaf.changed[0]);
}

//=============================================================================
// The whole life, in order
//=============================================================================

TEST(ShellLeafLink, PlaceAddMoveRemoveIsTheWholeLifeAndItBalances)
{
	FakeLeaf leaf;
	FakeRenderable r;

	ShellLeaf_Moved(&leaf, &r); // placed by the factory, not linked yet
	ShellLeaf_Add(&leaf, &r, 0); // into the leaf system
	const int h = r.RenderHandle();
	ShellLeaf_Moved(&leaf, &r); // followed for a frame
	ShellLeaf_Moved(&leaf, &r); // and another
	ShellLeaf_Remove(&leaf, &r); // destroyed
	r.RenderHandle() = kInvalidHandle; // what RemoveRenderable does
	ShellLeaf_Moved(&leaf, &r); // a stray follow after the release
	ShellLeaf_Remove(&leaf, &r); // and a second remove

	EXPECT_EQ(1u, leaf.added.size());
	EXPECT_EQ(1u, leaf.removed.size()) << "removed exactly as many times as added";
	EXPECT_EQ(h, leaf.removed[0]);
	EXPECT_EQ(2u, leaf.changed.size()) << "only the two moves made while linked";
}

} // namespace
