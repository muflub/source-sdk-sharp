//========= SourceSharp — the client bone leaf library, driven ================//
//
// plan_entity.md §6's bone-leaf-library row and §3.7. Lane `f-bone`.
//
// THE CLAIM UNDER TEST IS A BEHAVIOUR, NOT A MOVE. The gate, the generation
// counter and the two per-frame sweeps came off `C_BaseAnimating` and into
// `sourcesharp/game/client/shell_bone_leaf_library.cpp`. A test that read the
// source and checked the functions were THERE would pass on the day one of them
// arrived with its stack handling inverted, and the failure would then be a
// renderer that quietly refuses to set bones up — which looks like a content
// problem, not a port one.
//
// So every arm below drives the REAL function. shell_bone_leaf_library.cpp is on
// this test's link line; there is no restatement of the stack here. That is
// possible only because the translation unit is opted out of the cbase.h
// precompiled header, which is also the enforcement of §3.7's "no entity type in
// their interface" — a TU that does not include cbase.h cannot name one, and
// this file's very existence is the proof it does not.
//
// WHAT STOCK DID, WHICH IS WHAT THESE ARMS PIN.
// `game/client/c_baseanimating.cpp` before the move:
//
//   * `g_BoneAcessBase` starts zeroed, so the gate is CLOSED for both model
//     kinds until something pushes.
//   * `PushAllowBoneAccess` saves the current base onto a stack and overwrites
//     it; `PopBoneAccess` restores the top of the stack. So pushes NEST, and a
//     pop returns to whatever the enclosing push set — not to closed.
//   * `IsBoneAccessAllowed()` reads the view-model flag for a view model and the
//     normal-model flag otherwise. Those are independent: a push may open one
//     and leave the other shut, which is exactly what the render path relies on.
//   * `PopBoneAccess` on an empty stack RETURNS WITHOUT TOUCHING THE BASE.
//     Under NDEBUG, which is how this test builds, stock's Assert is gone and
//     the early return is the whole behaviour.
//   * `InvalidateBoneCaches()` increments one unsigned counter; every bone cache
//     compares its own stamp against it.
//
//===========================================================================//

#include "gtest/gtest.h"

#include "bone_leaf_library.h"

//-----------------------------------------------------------------------------
// The gate is shared global state, so every arm that pushes pops back to where
// it started. This helper makes an unbalanced arm fail loudly rather than
// leaking a gate state into whichever test runs next.
//-----------------------------------------------------------------------------
namespace
{
	struct GateState
	{
		bool bNormal;
		bool bView;
	};

	GateState ReadGate()
	{
		GateState s;
		s.bNormal = SourceSharp_BoneAccessAllowed( false );
		s.bView = SourceSharp_BoneAccessAllowed( true );
		return s;
	}

	int g_nSweepA = 0;
	int g_nSweepB = 0;
	int g_nSweepC = 0;

	void SweepA() { g_nSweepA++; }
	void SweepB() { g_nSweepB++; }
	void SweepC() { g_nSweepC++; }
}

//-----------------------------------------------------------------------------
// THE GATE
//-----------------------------------------------------------------------------

TEST( BoneLeafLibrary, TheGateIsShutForBothModelKindsBeforeAnythingPushes )
{
	// Stock's `g_BoneAcessBase` is default-constructed with both flags false,
	// and the render path leans on that: bones may not be read outside a push.
	const GateState base = ReadGate();
	EXPECT_FALSE( base.bNormal );
	EXPECT_FALSE( base.bView );
}

TEST( BoneLeafLibrary, APushOpensTheGateForNormalModelsOnly )
{
	SourceSharp_BoneAccessPush( true, false, "normal only" );
	EXPECT_TRUE( SourceSharp_BoneAccessAllowed( false ) );
	// THE TWO FLAGS ARE INDEPENDENT. A push that opened both would be a silent
	// widening: view models would become readable in passes that never meant to
	// allow it, and nothing would say so.
	EXPECT_FALSE( SourceSharp_BoneAccessAllowed( true ) );
	SourceSharp_BoneAccessPop( "normal only" );
}

TEST( BoneLeafLibrary, APushOpensTheGateForViewModelsOnly )
{
	SourceSharp_BoneAccessPush( false, true, "view only" );
	EXPECT_FALSE( SourceSharp_BoneAccessAllowed( false ) );
	EXPECT_TRUE( SourceSharp_BoneAccessAllowed( true ) );
	SourceSharp_BoneAccessPop( "view only" );
}

TEST( BoneLeafLibrary, APopRestoresTheStateThePushSaved )
{
	const GateState before = ReadGate();

	SourceSharp_BoneAccessPush( true, true, "both" );
	ASSERT_TRUE( SourceSharp_BoneAccessAllowed( false ) );
	ASSERT_TRUE( SourceSharp_BoneAccessAllowed( true ) );

	SourceSharp_BoneAccessPop( "both" );

	const GateState after = ReadGate();
	EXPECT_EQ( before.bNormal, after.bNormal );
	EXPECT_EQ( before.bView, after.bView );
}

TEST( BoneLeafLibrary, PushesNestAndAPopReturnsToTheEnclosingPushNotToShut )
{
	// This is the arm that catches a stack implemented as a single saved copy.
	// Such an implementation passes every single-push test above and then, on the
	// inner pop, shuts a gate the outer push had opened — which in the renderer
	// is an intermittent "bone access not allowed" in exactly the frames that
	// nest two passes.
	SourceSharp_BoneAccessPush( true, false, "outer" );
	SourceSharp_BoneAccessPush( false, true, "inner" );

	EXPECT_FALSE( SourceSharp_BoneAccessAllowed( false ) );
	EXPECT_TRUE( SourceSharp_BoneAccessAllowed( true ) );

	SourceSharp_BoneAccessPop( "inner" );

	EXPECT_TRUE( SourceSharp_BoneAccessAllowed( false ) )
		<< "the inner pop shut a gate the OUTER push had opened";
	EXPECT_FALSE( SourceSharp_BoneAccessAllowed( true ) );

	SourceSharp_BoneAccessPop( "outer" );
	EXPECT_FALSE( SourceSharp_BoneAccessAllowed( false ) );
}

TEST( BoneLeafLibrary, APopWithNothingPushedLeavesTheGateExactlyAsItWas )
{
	// Stock returns early when the stack is empty rather than zeroing the base.
	// The difference shows up only when a pop is unbalanced, which is the case
	// the Assert exists for — and this build has no Asserts.
	SourceSharp_BoneAccessPush( true, true, "held open" );

	const GateState open = ReadGate();
	ASSERT_TRUE( open.bNormal );
	ASSERT_TRUE( open.bView );

	SourceSharp_BoneAccessPop( "held open" );   // balances the push
	SourceSharp_BoneAccessPop( "unbalanced" );  // and this one has nothing to pop

	// The unbalanced pop must not have pulled anything out from under the base.
	EXPECT_FALSE( SourceSharp_BoneAccessAllowed( false ) );
	EXPECT_FALSE( SourceSharp_BoneAccessAllowed( true ) );
}

TEST( BoneLeafLibrary, TheScopedGuardPushesOnEntryAndPopsOnExit )
{
	// `CSourceSharpBoneAccessScope` is what `C_BaseAnimating::AutoAllowBoneAccess`
	// forwards to and what the re-pointed call sites in shell_clientdll.cpp and
	// ragdoll.cpp now spell, so the scope's own behaviour is load-bearing.
	ASSERT_FALSE( SourceSharp_BoneAccessAllowed( false ) );

	{
		CSourceSharpBoneAccessScope scope( true, false, ( const char * ) 1 );
		EXPECT_TRUE( SourceSharp_BoneAccessAllowed( false ) );
		EXPECT_FALSE( SourceSharp_BoneAccessAllowed( true ) );
	}

	EXPECT_FALSE( SourceSharp_BoneAccessAllowed( false ) )
		<< "the scope did not pop on destruction";
}

TEST( BoneLeafLibrary, NestedScopedGuardsUnwindInOrder )
{
	{
		CSourceSharpBoneAccessScope outer( true, true, "outer scope" );
		{
			CSourceSharpBoneAccessScope inner( false, false, "inner scope" );
			EXPECT_FALSE( SourceSharp_BoneAccessAllowed( false ) );
			EXPECT_FALSE( SourceSharp_BoneAccessAllowed( true ) );
		}
		EXPECT_TRUE( SourceSharp_BoneAccessAllowed( false ) );
		EXPECT_TRUE( SourceSharp_BoneAccessAllowed( true ) );
	}
	EXPECT_FALSE( SourceSharp_BoneAccessAllowed( false ) );
	EXPECT_FALSE( SourceSharp_BoneAccessAllowed( true ) );
}

//-----------------------------------------------------------------------------
// THE MODEL-BONE GENERATION COUNTER
//-----------------------------------------------------------------------------

TEST( BoneLeafLibrary, InvalidatingBoneCachesAdvancesTheGenerationCounterByOne )
{
	// RELATIVE, not absolute: other arms in this binary and the real client both
	// bump it, and an absolute expectation would be a test that passes only when
	// it runs first.
	const unsigned long before = SourceSharp_ModelBoneCounter();
	SourceSharp_InvalidateBoneCaches();
	EXPECT_EQ( before + 1, SourceSharp_ModelBoneCounter() );
}

TEST( BoneLeafLibrary, TheGenerationCounterOnlyMovesWhenSomethingInvalidates )
{
	// A counter that ticked on a read would make every cache permanently stale
	// and every frame rebuild every bone — a pure performance failure, which is
	// the kind nothing else in this suite would notice.
	const unsigned long a = SourceSharp_ModelBoneCounter();
	const unsigned long b = SourceSharp_ModelBoneCounter();
	EXPECT_EQ( a, b );

	SourceSharp_BoneAccessPush( true, true, "counter" );
	SourceSharp_BoneAccessPop( "counter" );
	EXPECT_EQ( a, SourceSharp_ModelBoneCounter() )
		<< "the bone-access gate moved the generation counter";
}

//-----------------------------------------------------------------------------
// THE SWEEP REGISTRY
//
// This is the mechanism that lets `ThreadedBoneSetup` and
// `UpdateClientSideAnimations` keep their entity-typed bodies where the entities
// are while the ENTRY POINT lives in a library that may not name an entity type.
// The failure mode it can have is a registration that is silently lost, so these
// arms check the count as well as the effect.
//-----------------------------------------------------------------------------

TEST( BoneLeafLibrary, ARegisteredSweepRunsWhenItsDriverIsCalled )
{
	const int before = SourceSharp_BoneSweepCount( kBoneSweep_ThreadedBoneSetup );
	SourceSharp_RegisterBoneSweep( kBoneSweep_ThreadedBoneSetup, &SweepA );
	ASSERT_EQ( before + 1, SourceSharp_BoneSweepCount( kBoneSweep_ThreadedBoneSetup ) )
		<< "the registration was silently lost";

	const int ran = g_nSweepA;
	SourceSharp_ThreadedBoneSetup();
	EXPECT_EQ( ran + 1, g_nSweepA );
}

TEST( BoneLeafLibrary, TheTwoSweepKindsAreSeparateLists )
{
	// A single shared list would make the per-frame bone setup also advance
	// client-side animation, which is a double cycle advance and is silent.
	SourceSharp_RegisterBoneSweep( kBoneSweep_ClientSideAnimations, &SweepB );

	const int b = g_nSweepB;
	SourceSharp_ThreadedBoneSetup();
	EXPECT_EQ( b, g_nSweepB )
		<< "a client-side-animation sweep ran from the bone-setup driver";

	SourceSharp_UpdateClientSideAnimations();
	EXPECT_EQ( b + 1, g_nSweepB );
}

TEST( BoneLeafLibrary, RegisteringTheSameSweepTwiceIsRefused )
{
	SourceSharp_RegisterBoneSweep( kBoneSweep_ClientSideAnimations, &SweepC );
	const int after1 = SourceSharp_BoneSweepCount( kBoneSweep_ClientSideAnimations );

	SourceSharp_RegisterBoneSweep( kBoneSweep_ClientSideAnimations, &SweepC );
	EXPECT_EQ( after1, SourceSharp_BoneSweepCount( kBoneSweep_ClientSideAnimations ) );

	// And it runs once, not twice.
	const int c = g_nSweepC;
	SourceSharp_UpdateClientSideAnimations();
	EXPECT_EQ( c + 1, g_nSweepC );
}

TEST( BoneLeafLibrary, ANullSweepIsRefusedRatherThanStoredAndCalled )
{
	const int before = SourceSharp_BoneSweepCount( kBoneSweep_ThreadedBoneSetup );
	SourceSharp_RegisterBoneSweep( kBoneSweep_ThreadedBoneSetup, NULL );
	EXPECT_EQ( before, SourceSharp_BoneSweepCount( kBoneSweep_ThreadedBoneSetup ) );

	// If it HAD been stored the next line would call through a null pointer.
	SourceSharp_ThreadedBoneSetup();
}

TEST( BoneLeafLibrary, AnOutOfRangeSweepKindRegistersNothingAndCountsZero )
{
	EXPECT_EQ( 0, SourceSharp_BoneSweepCount( kBoneSweep_Count ) );
	SourceSharp_RegisterBoneSweep( kBoneSweep_Count, &SweepA );
	EXPECT_EQ( 0, SourceSharp_BoneSweepCount( kBoneSweep_Count ) );
}
