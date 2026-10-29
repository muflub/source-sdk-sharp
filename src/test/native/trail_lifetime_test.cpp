//========= SourceSharp — tests for what the trail family lost with the entity =//
//
// The six trail classes stopped deriving from C_BaseEntity (shell_particle_effect.h)
// and with it lost two guarantees that had been holding them up silently. Both
// were re-provided by hand, so both are worth a test that can fail.
//
// WHY THE ALLOCATOR IS THE FIRST TEST. C_BaseEntity::operator new memsets the
// block — "All fields in the object are all initialized to 0" is the comment
// above it — and the six were written against that without saying so:
//
//   C_SporeTrail's constructor sets six of its seven fields and leaves m_bEmit
//   alone. "Nothing a map or a player can do will make one emit" is a
//   documented property of that entity, and it is true ONLY because the flag
//   arrives false. Under a non-zeroing allocator an env_sporetrail emits or
//   does not depending on what was last in the heap.
//
//   C_ParticleTrail's constructor sets nothing, and its destructor then reads
//   m_pParticleMgr and calls through it. That is a wild call on the first trail
//   whose Start() ever fails.
//
// Neither of those failures is a crash you would get on the first run, which is
// exactly why they are pinned here rather than left to a play-test.
//
// WHY THE LATCH IS THE SECOND. An entity had one owner and CHandle told every
// other holder when it went. A CTrailEffect has TWO callers that destroy it and
// neither knows about the other — Release() from shell_trail_render.cpp and
// NotifyRemove() from CParticleMgr's level-shutdown sweep — plus a third path
// that looks like a destroy and is not: the subclass destructor's own
// RemoveEffect() reaches CParticleMgr, which calls NotifyRemove on an object
// that is already half gone. A double delete there is a heap corruption in the
// middle of a level change, i.e. the least debuggable place in the game.
//
// Engine-free: a macro over an allocator function and a two-field latch.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../game/shared/trail_lifetime.h"

#include <stdlib.h>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// An allocator that hands back DIRTY memory, which is the whole point.
//
// A test that allocated with malloc and found zeroes would be a test that
// cannot fail: fresh pages from the OS are already zero, so the memset would be
// invisible and deleting it would leave the suite green. Every block here is
// filled with 0xCD first, so a missing memset shows up as 0xCDCDCDCD on the
// very first assertion.
//
// It also records every allocation and free, so the delete half of the macro is
// checked rather than assumed — a macro that defined operator new and forgot
// operator delete would still zero correctly and would leak every trail.
//-----------------------------------------------------------------------------
struct DirtyHeap
{
	static int s_nAllocs;
	static int s_nFrees;
	static std::vector<void*> s_Live;

	static void Reset()
	{
		s_nAllocs = 0;
		s_nFrees = 0;
		s_Live.clear();
	}

	static void* Alloc(size_t n)
	{
		void* p = malloc(n);
		memset(p, 0xCD, n);
		s_nAllocs++;
		s_Live.push_back(p);
		return p;
	}

	static void* Alloc(size_t n, const char* pFile, int nLine) { return Alloc(n); }

	static void Free(void* p)
	{
		s_nFrees++;
		for (size_t i = 0; i < s_Live.size(); i++)
		{
			if (s_Live[i] == p)
			{
				s_Live.erase(s_Live.begin() + i);
				break;
			}
		}
		free(p);
	}
};

int DirtyHeap::s_nAllocs = 0;
int DirtyHeap::s_nFrees = 0;
std::vector<void*> DirtyHeap::s_Live;

void* DirtyAlloc(size_t n)
{
	return DirtyHeap::Alloc(n);
}

void* DirtyAlloc(size_t n, const char* pFile, int nLine)
{
	return DirtyHeap::Alloc(n, pFile, nLine);
}

void DirtyFree(void* p)
{
	DirtyHeap::Free(p);
}

//-----------------------------------------------------------------------------
// C_SporeTrail's shape, and deliberately so: a constructor that sets some of
// its fields and not others, and a flag whose documented behaviour depends on
// arriving false. This is the class the allocator exists for.
//-----------------------------------------------------------------------------
class CSporeTrailShaped
{
public:
	SOURCESHARP_ZEROING_ALLOCATOR(DirtyAlloc, DirtyFree)

	CSporeTrailShaped()
	{
		// Six of the seven, exactly as C_SporeTrail::C_SporeTrail does.
		m_flSpawnRate = 10;
		m_flParticleLifetime = 5;
		m_flStartSize = 35;
		m_flEndSize = 55;
		m_flSpawnRadius = 0;
		m_pParticleMgr = NULL;
		// m_bEmit is NOT set. That is the bug this allocator prevents.
	}

	float m_flSpawnRate;
	float m_flParticleLifetime;
	float m_flStartSize;
	float m_flEndSize;
	float m_flSpawnRadius;
	void* m_pParticleMgr;
	bool m_bEmit;

	// C_ParticleTrail's shape too: a pointer nothing initialises that the
	// destructor reads.
	void* m_pNeverAssigned;
	char m_Padding[64];
};

//-----------------------------------------------------------------------------
// The latch's owner. A real one is a CTrailEffect; nothing here needs it to be.
//-----------------------------------------------------------------------------
class CFakeTrail
{
public:
	CFakeTrail()
		: m_nGoneCalls(0)
	{
	}

	CTrailDestroyLatch<CFakeTrail> m_Destroy;
	int m_nGoneCalls;
};

CFakeTrail* g_pLastGone = NULL;
int g_nGoneTotal = 0;

void OnGone(CFakeTrail* pTrail)
{
	g_pLastGone = pTrail;
	g_nGoneTotal++;
	pTrail->m_nGoneCalls++;
}

class TrailLatch : public ::testing::Test
{
protected:
	virtual void SetUp()
	{
		g_pLastGone = NULL;
		g_nGoneTotal = 0;
		DirtyHeap::Reset();
	}
};

//=============================================================================//
// The allocator.
//=============================================================================//

//-----------------------------------------------------------------------------
// The one that matters: a field the constructor never touches reads as zero.
//
// Note what makes this a real check rather than a tautology — the heap under it
// is filled with 0xCD, so without the memset m_bEmit is true, m_pNeverAssigned
// is 0xCDCDCDCDCDCDCDCD, and both assertions fail.
//-----------------------------------------------------------------------------
TEST_F(TrailLatch, AFieldTheConstructorNeverTouchesArrivesZeroed)
{
	CSporeTrailShaped* p = new CSporeTrailShaped;

	EXPECT_FALSE(p->m_bEmit);
	EXPECT_TRUE(p->m_pNeverAssigned == NULL);

	delete p;
}

//-----------------------------------------------------------------------------
// Every byte, not just the interesting ones. The padding is in the class for
// this test: a memset that used sizeof(some member) rather than the block size
// would pass the test above and fail this one.
//-----------------------------------------------------------------------------
TEST_F(TrailLatch, TheWholeBlockIsZeroedRatherThanTheFirstFields)
{
	CSporeTrailShaped* p = new CSporeTrailShaped;

	for (size_t i = 0; i < sizeof(p->m_Padding); i++)
	{
		EXPECT_EQ(0, p->m_Padding[i]) << "byte " << i << " of the tail was not zeroed";
	}

	delete p;
}

//-----------------------------------------------------------------------------
// The constructor still wins. Zeroing that ran AFTER the constructor rather
// than before it would wipe every default the class sets, which is the other
// way to get this wrong and is silent in exactly the same way.
//-----------------------------------------------------------------------------
TEST_F(TrailLatch, TheConstructorsOwnDefaultsSurviveTheZeroing)
{
	CSporeTrailShaped* p = new CSporeTrailShaped;

	EXPECT_FLOAT_EQ(10.0f, p->m_flSpawnRate);
	EXPECT_FLOAT_EQ(5.0f, p->m_flParticleLifetime);
	EXPECT_FLOAT_EQ(35.0f, p->m_flStartSize);
	EXPECT_FLOAT_EQ(55.0f, p->m_flEndSize);

	delete p;
}

//-----------------------------------------------------------------------------
// delete goes back to the same heap. A macro that declared operator new and
// left operator delete to the global one would leak every trail the game makes
// and would still pass every test above.
//-----------------------------------------------------------------------------
TEST_F(TrailLatch, DeleteReturnsTheBlockToTheAllocatorItCameFrom)
{
	CSporeTrailShaped* p = new CSporeTrailShaped;

	EXPECT_EQ(1, DirtyHeap::s_nAllocs);
	EXPECT_EQ(0, DirtyHeap::s_nFrees);

	delete p;

	EXPECT_EQ(1, DirtyHeap::s_nFrees);
	EXPECT_TRUE(DirtyHeap::s_Live.empty());
}

//-----------------------------------------------------------------------------
// The memdbg spelling, which is the one the game actually uses. memdbgon.h
// rewrites `new` into new(nBlockUse, __FILE__, __LINE__) in every .cpp of the
// build, so a class declaring only the plain pair quietly keeps the global
// ::operator new for its real allocations and the zeroing never happens where
// it counts.
//-----------------------------------------------------------------------------
TEST_F(TrailLatch, TheFileAndLineSpellingZeroesToo)
{
	CSporeTrailShaped* p = new (1, __FILE__, __LINE__) CSporeTrailShaped;

	EXPECT_FALSE(p->m_bEmit);
	EXPECT_TRUE(p->m_pNeverAssigned == NULL);
	EXPECT_EQ(1, DirtyHeap::s_nAllocs);

	delete p;

	EXPECT_EQ(1, DirtyHeap::s_nFrees);
}

//=============================================================================//
// The destruction latch.
//=============================================================================//

//-----------------------------------------------------------------------------
// One winner. This is the whole contract.
//-----------------------------------------------------------------------------
TEST_F(TrailLatch, OnlyTheFirstClaimSucceeds)
{
	CFakeTrail trail;

	EXPECT_TRUE(trail.m_Destroy.Claim());
	EXPECT_FALSE(trail.m_Destroy.Claim());
	EXPECT_FALSE(trail.m_Destroy.Claim());
}

//-----------------------------------------------------------------------------
// Before anything claims, nothing is deleting. A latch that started true would
// make Release() a no-op and leak every trail in the game — which is a change
// no other test here would notice.
//-----------------------------------------------------------------------------
TEST_F(TrailLatch, AFreshLatchIsNotDeleting)
{
	CFakeTrail trail;

	EXPECT_FALSE(trail.m_Destroy.IsDeleting());

	trail.m_Destroy.Claim();

	EXPECT_TRUE(trail.m_Destroy.IsDeleting());
}

//-----------------------------------------------------------------------------
// THE REAL SEQUENCE, both ways round, because the two destroy routes have no
// ordering between them:
//
//   Release() then a level change:  the shell wins, CParticleMgr's NotifyRemove
//                                   arrives at an object already being deleted.
//   Level change then Release():    RemoveAllEffects wins and the shell's own
//                                   teardown arrives second.
//
// Either way exactly one delete may happen.
//-----------------------------------------------------------------------------
TEST_F(TrailLatch, ReleaseThenNotifyRemoveDeletesOnce)
{
	CFakeTrail trail;

	int nDeletes = 0;

	if (trail.m_Destroy.Claim())
		nDeletes++; // Release()
	if (trail.m_Destroy.Claim())
		nDeletes++; // NotifyRemove()

	EXPECT_EQ(1, nDeletes);
}

TEST_F(TrailLatch, NotifyRemoveThenReleaseDeletesOnce)
{
	CFakeTrail trail;

	int nDeletes = 0;

	if (trail.m_Destroy.Claim())
		nDeletes++; // NotifyRemove()
	if (trail.m_Destroy.Claim())
		nDeletes++; // Release()

	EXPECT_EQ(1, nDeletes);
}

//-----------------------------------------------------------------------------
// The destructor's own claim is allowed to lose, and that is the third path:
// ~C_SmokeTrail calls m_pParticleMgr->RemoveEffect, which calls NotifyRemove,
// on an object whose Release() already claimed. Claim() answering false there
// is what stops the second delete.
//-----------------------------------------------------------------------------
TEST_F(TrailLatch, ADestructorReachedThroughAClaimFindsTheLatchAlreadySet)
{
	CFakeTrail trail;

	ASSERT_TRUE(trail.m_Destroy.Claim()); // Release() won

	// ~CTrailEffect runs, claims (losing), and the RemoveEffect it triggers
	// comes back round as NotifyRemove.
	EXPECT_FALSE(trail.m_Destroy.Claim()); // the destructor's own
	EXPECT_FALSE(trail.m_Destroy.Claim()); // NotifyRemove, re-entered
}

//-----------------------------------------------------------------------------
// The holder is told, once, with the right object.
//-----------------------------------------------------------------------------
TEST_F(TrailLatch, TheGoneCallbackFiresOnceWithTheOwner)
{
	CFakeTrail trail;
	trail.m_Destroy.SetGoneCallback(&OnGone);

	trail.m_Destroy.Announce(&trail);

	EXPECT_EQ(1, g_nGoneTotal);
	EXPECT_EQ(&trail, g_pLastGone);
	EXPECT_EQ(1, trail.m_nGoneCalls);
}

//-----------------------------------------------------------------------------
// AND ONLY ONCE, EVER. This is the one that keeps shell_trail_render.cpp's
// registry honest: ManagedTrails_Forget removes the row whose pointer matches,
// and a second announcement after the row has been reused for a different trail
// would remove the wrong one.
//-----------------------------------------------------------------------------
TEST_F(TrailLatch, TheGoneCallbackDoesNotFireTwice)
{
	CFakeTrail trail;
	trail.m_Destroy.SetGoneCallback(&OnGone);

	trail.m_Destroy.Announce(&trail);
	trail.m_Destroy.Announce(&trail);
	trail.m_Destroy.Announce(&trail);

	EXPECT_EQ(1, g_nGoneTotal);
}

//-----------------------------------------------------------------------------
// No callback set is not a crash. Nothing in the game creates one of these
// without a registry behind it today, but ~CTrailEffect announces
// unconditionally and a null there would fault in the destructor.
//-----------------------------------------------------------------------------
TEST_F(TrailLatch, AnnouncingWithNoCallbackIsSilent)
{
	CFakeTrail trail;

	trail.m_Destroy.Announce(&trail);

	EXPECT_EQ(0, g_nGoneTotal);
}

//-----------------------------------------------------------------------------
// Claiming and announcing are INDEPENDENT, which they have to be: the
// destructor announces after a Claim() that lost, and if Announce refused on a
// lost claim the registry would keep a row pointing at freed memory — the exact
// dangling pointer the callback was added to prevent.
//-----------------------------------------------------------------------------
TEST_F(TrailLatch, AnnounceStillFiresWhenTheDestructorsClaimLost)
{
	CFakeTrail trail;
	trail.m_Destroy.SetGoneCallback(&OnGone);

	ASSERT_TRUE(trail.m_Destroy.Claim()); // Release() won
	ASSERT_FALSE(trail.m_Destroy.Claim()); // ~CTrailEffect lost

	trail.m_Destroy.Announce(&trail); // ...and announces anyway

	EXPECT_EQ(1, g_nGoneTotal);
	EXPECT_EQ(&trail, g_pLastGone);
}

//-----------------------------------------------------------------------------
// Two trails do not share a latch. The registry keys on the pointer, so a
// static or otherwise shared flag would let the first trail's destruction
// silence the second's and leave a live row pointing at freed memory.
//-----------------------------------------------------------------------------
TEST_F(TrailLatch, EachTrailHasItsOwnLatch)
{
	CFakeTrail a, b;
	a.m_Destroy.SetGoneCallback(&OnGone);
	b.m_Destroy.SetGoneCallback(&OnGone);

	ASSERT_TRUE(a.m_Destroy.Claim());

	EXPECT_TRUE(a.m_Destroy.IsDeleting());
	EXPECT_FALSE(b.m_Destroy.IsDeleting());
	EXPECT_TRUE(b.m_Destroy.Claim());

	a.m_Destroy.Announce(&a);
	b.m_Destroy.Announce(&b);

	EXPECT_EQ(2, g_nGoneTotal);
	EXPECT_EQ(1, a.m_nGoneCalls);
	EXPECT_EQ(1, b.m_nGoneCalls);
}

//-----------------------------------------------------------------------------
// The zeroing allocator and the latch meet: a latch that arrives through the
// allocator is usable without its constructor having run at all.
//
// That is not hypothetical tidiness. The macro memsets the block and THEN the
// constructors run, so both orders produce a false latch — but a latch whose
// "not deleting" state was represented by a non-zero value would be born
// claimed by the memset, and every Release() in the game would become a no-op.
//-----------------------------------------------------------------------------
TEST_F(TrailLatch, AZeroedLatchIsAnUnclaimedLatch)
{
	CTrailDestroyLatch<CFakeTrail> latch;
	memset((void*)&latch, 0, sizeof(latch));

	EXPECT_FALSE(latch.IsDeleting());
	EXPECT_TRUE(latch.Claim());
}

} // namespace
