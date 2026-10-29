//========= SourceSharp — what a projectile's impact is allowed to NAME =======//
//
// THE BUG. A projectile on the parallel hierarchy flies by tracing a line each
// tick and telling managed code what it hit. What it hit is a pointer the
// ENGINE wrote, at the entity's IHandleEntity offset, and the engine has no
// idea whether the thing there is a CBaseEntity — gametrace.h says exactly that
// at the field. The shell narrowed it anyway:
//
//     OnManagedImpact( TraceEntity( tr ) );          // shell_standalone_base.cpp
//     ... SourceSharp_HandleFromEntity( pOther )     // shell_dispatch.cpp
//
// TraceEntity()'s job is to answer NULL for anything outside the SDK's tree,
// and util_shared.h calls that NULL "the SAFE answer for an entity outside the
// SDK — it reads as hit the world". It is safe for a caller about to
// dereference a CBaseEntity. This caller does not dereference: it hands the
// answer over AS A HANDLE, and a handle never cared which hierarchy its owner
// lived on. So a managed rocket striking a managed crate produced the same
// invalid handle as a rocket striking nothing at all, and the managed side
// could not tell the two apart. Silently — an impact nobody could name looks
// exactly like an impact against the world.
//
// TWO HALVES, because the fault has two halves.
//
//   THE DISAGREEMENT is real code and is exercised: handle_cross.h's
//   SourceSharp_SDKEntityFromUnknown — the function EntityFromEntityHandle's
//   server arm IS — driven by a mock standalone, showing that it
//   answers NULL for a standalone entity while the handle-table pointer names
//   it. Not a stand-in for the narrowing; the narrowing itself.
//
//   THE BAN is a source scan, in sdk_player_cast_test.cpp's idiom and for its
//   reason: the thing that must stay true is a SHAPE at three call sites, and
//   no value a gtest can compute would notice somebody spelling it back. Read
//   with whole-line comments stripped, because this file's own prose names both
//   functions repeatedly and a check a comment can satisfy is a check about
//   comments.
//
// WHERE THE CALL SITE WENT. plan_entity.md step 2f moved one frame of flight
// into SourceSharp.Server/EntityMotion.cs: the shell no longer traces the step,
// no longer decides what an impact means, and no longer calls OnManagedImpact
// at all. The narrowing this file bans has one place left where it could be
// spelled — the handle of the trace's entity (SourceSharp_TraceLine's *pOutHit
// write until plan_server_native_v2 T4f, libss_fwd_server's
// SSFwd_TraceFilter_HandleOf since), which is what every managed impact in the
// build now reads — so BAN A follows it there. The
// other two halves are untouched: OnManagedImpact still exists, is still
// declared IHandleEntity-wide, and still crosses by the wide spelling; nothing
// calls it, and the request in .claude/entity-port/abi-requests/2f.md asks for
// it and ENTITY_OP_IMPACT to be retired together, because the op enum is not a
// file one lane may edit.
//
// PROVEN RED. Narrow the trace's subject — write
// `*pOutHit = SourceSharp_HandleFromEntity( tr.m_pEnt );` in
// shell_worldquery.cpp — and TheTraceNamesWhatItHitWidely fails twice, once
// on the banned spelling and once on the wide one being absent. Put
// `void CStandaloneEntity::UpdateFlight()` back in shell_standalone_base.cpp
// and TheShellNoLongerFliesAProjectileItself fails. Spell
// SourceSharp_HandleFromEntity back into the scripted handler and
// TheScriptedImpactCrossesWidely fails the same way. Retype the declaration
// back to CBaseEntity * and TheImpactBoundaryTakesAHandleEntity fails. Each
// half of the change has its own failure, so a partial revert cannot pass.
//
//=============================================================================//
#include <gtest/gtest.h>

#include <dirent.h>

#include <fstream>
#include <iterator>
#include <sstream>
#include <string>
#include <vector>

#include "../game/shared/handle_cross.h"

namespace
{

//=============================================================================//
// HALF ONE — the disagreement, on mocks of the two hierarchies.
//=============================================================================//
//
// The server's chain, not the client's: IHandleEntity -> IServerUnknown ->
// IServerEntity is single inheritance, so the partition is reached by a cast
// that adjusts nothing, and the game-entity boundary is a SEPARATE base. That
// separateness is what makes the comparison mean anything — on a CBaseEntity
// every view of the object is the same address and both answers agree.

class FakeGameEntity;

class FakeHandleEntity // ~ IHandleEntity
{
public:
	virtual ~FakeHandleEntity() {}

	virtual int RefEHandle() const = 0;
};

class FakeGameEntity // ~ IGameEntity, CBaseEntity on the server
{
public:
	virtual ~FakeGameEntity() {}

	virtual int WhoAreYou() const = 0;
};

// ONE ACCESSOR SINCE plan_server_native_v2 T1.3: stock IServerUnknown's
// GetBaseEntity(), typed by the entity face. The SDK's tree this half used to
// model beside the standalone (FakeBase, whose GetBaseEntity() answered
// itself) is gone from the server, so the narrowing below answers NULL for
// every object there is -- which is the bug's premise in its strongest form.
class FakeUnknown : public FakeHandleEntity // ~ IServerUnknown
{
public:
	virtual FakeGameEntity* GetBaseEntity() = 0;
};

class FakeServerEntity : public FakeUnknown // ~ IServerEntity
{
};

// A padded base BEFORE the boundary, so the IGameEntity subobject is not at
// offset zero — exactly as CStandaloneEntity's is its fourth base. Nothing here
// casts to it; the layout is what makes "the two views are different addresses"
// a fact this file can show rather than repeat.
class FakePadding
{
public:
	virtual ~FakePadding() {}

	int m_nPadding[4];
};

class FakeStandalone : public FakeServerEntity, public FakePadding, public FakeGameEntity
{
public:
	virtual FakeGameEntity* GetBaseEntity() { return this; }

	virtual int RefEHandle() const { return 0x57A10; }

	virtual int WhoAreYou() const { return 0x57A10; }
};

// What the shell used to do to the trace's pointer, spelled with the REAL
// function: EntityFromEntityHandle's server arm is this call and nothing else.
FakeGameEntity* NarrowToTheSdksTree(FakeHandleEntity* pHit)
{
	if (!pHit)
		return 0;

	FakeUnknown* pUnknown = static_cast<FakeUnknown*>(pHit);

	return SourceSharp_SDKEntityFromUnknown<FakeServerEntity>(pUnknown);
}

TEST(ImpactSubject, NothingHitNamesNothingEitherWay)
{
	EXPECT_EQ((FakeGameEntity*)0, NarrowToTheSdksTree(0));
}

TEST(ImpactSubject, StandaloneIsNamedWhereNarrowingDropsIt)
{
	FakeStandalone standalone;
	FakeHandleEntity* pHit = &standalone;

	// The premise: the standalone HAS a face, so the NULL below is the
	// narrowing's verdict and not an empty object.
	ASSERT_NE((FakeGameEntity*)0, standalone.GetBaseEntity());

	// The narrowing answers NULL, exactly as designed — and the shell then
	// turned that NULL into the invalid handle and told C# "you hit nothing".
	EXPECT_EQ((FakeGameEntity*)0, NarrowToTheSdksTree(pHit));

	// The handle-table pointer names it, and under its own handle. This is the
	// number the managed side gets now and did not get before.
	EXPECT_EQ(0x57A10, pHit->RefEHandle());
}

TEST(ImpactSubject, TheStandalonesTwoViewsAreDifferentAddresses)
{
	FakeStandalone standalone;

	// The premise the parallel hierarchy rests on, made visible: the
	// handle-table view and the game-entity view of ONE object are different
	// addresses, so naming by handle and naming by interface are genuinely
	// different acts rather than two spellings of a cast.
	FakeHandleEntity* pTableView = &standalone;
	FakeGameEntity* pBoundaryView = standalone.GetBaseEntity();

	EXPECT_NE((void*)pTableView, (void*)pBoundaryView);
	EXPECT_EQ(0x57A10, pBoundaryView->WhoAreYou());
}

//=============================================================================//
// HALF TWO — the ban, read out of the source.
//=============================================================================//

// Every code line naming something, with its line number. Returning the hits
// rather than a count is what lets each assertion print the offending line.
struct Hit_t
{
	int nLine;
	std::string strText;
};

} // namespace
