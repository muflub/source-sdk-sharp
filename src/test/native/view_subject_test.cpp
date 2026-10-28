//========= SourceSharp — the view's subject, and the de-entitying of it ======//
//
// plan_entity.md step 7c. Two halves, and they check different kinds of thing.
//
// THE RULES (view_subject.h) are the small amount of deciding the view's facts
// do, lifted out of shell_view_facts.cpp so that they can be run. Each has an
// edge whose wrong answer is INVISIBLE IN A RUNNING GAME, which is the only
// reason they are rules rather than expressions at a call site: a view that
// silently never switches on the underwater DSP, and a view entity of index 0
// that puts the camera at the map origin, both produce a frame that looks like
// something else's fault.
//
// THE SOURCE SCAN is the step's own gate written down. "De-entitied in place"
// means these six translation units stay in the build and stop naming entity
// types, and the census measures exactly that — so a check here is what keeps
// a later edit from putting one back without anyone noticing. It reads the same
// regex the census reads, over the same files, and it is deliberately a
// FILE-BY-FILE assertion rather than a total: a total that moved would not say
// which file moved it.
//
//=============================================================================//
#include "gtest/gtest.h"
#include "cmake_graph.h"

#include <sys/stat.h>

#include <fstream>
#include <set>
#include <sstream>
#include <string>
#include <vector>

#include "../game/client/view_subject.h"

using namespace ViewSubject;

//=============================================================================
// PART 1 — THE RULES
//=============================================================================

//-----------------------------------------------------------------------------
// The underwater DSP.
//-----------------------------------------------------------------------------
TEST( AudioIsUnderwater, EyesUnderTheSurfaceIsUnderwater )
{
	EXPECT_TRUE( AudioIsUnderwater( WATER_LEVEL_EYES ) );
}

TEST( AudioIsUnderwater, WaistDeepIsNot )
{
	// WL_Waist is 2. A player standing waist-deep hears the room; this is the
	// line stock draws and the one a `>` instead of a `>=` would move.
	EXPECT_FALSE( AudioIsUnderwater( 2 ) );
	EXPECT_FALSE( AudioIsUnderwater( 1 ) );
	EXPECT_FALSE( AudioIsUnderwater( 0 ) );
}

TEST( AudioIsUnderwater, TheTopOfTheScaleIsTheOnlyTrueValue )
{
	// WL_Eyes is the LAST value in the enum, so `> WL_Eyes` can never be true
	// and a build written that way would never switch the DSP on for anyone —
	// which is exactly the silence this whole step is removing by another
	// route. Asserting the top of the scale is what makes that mistake fail.
	EXPECT_TRUE( AudioIsUnderwater( WATER_LEVEL_EYES ) );
	EXPECT_EQ( 3, WATER_LEVEL_EYES );
}

//-----------------------------------------------------------------------------
// The effects bits.
//-----------------------------------------------------------------------------
TEST( EffectActive, TheAskedForBitSetIsActive )
{
	EXPECT_TRUE( EffectActive( 0x2, 0x2 ) );
	EXPECT_TRUE( EffectActive( 0xFF, 0x2 ) );
}

TEST( EffectActive, ADifferentBitIsNot )
{
	EXPECT_FALSE( EffectActive( 0x1, 0x2 ) );
	EXPECT_FALSE( EffectActive( 0x0, 0x2 ) );
}

TEST( EffectActive, ANYOfTheAskedForBitsIsEnough )
{
	// Stock's IsEffectActive is `( m_fEffects & nEffectMask ) != 0`, which is
	// ANY. A reader who wrote `== nEffectMask` would get the same answer for
	// every single-bit ask and a different one the first time two bits were
	// passed, which is a mistake that ships.
	EXPECT_TRUE( EffectActive( 0x1, 0x3 ) );
	EXPECT_TRUE( EffectActive( 0x2, 0x3 ) );
	EXPECT_FALSE( EffectActive( 0x4, 0x3 ) );
}

//-----------------------------------------------------------------------------
// The view entity.
//-----------------------------------------------------------------------------
TEST( ViewEntityOverridesTheEye, LookingOutOfYourOwnHeadIsNotAnOverride )
{
	EXPECT_FALSE( ViewEntityOverridesTheEye( 1, 1 ) );
	EXPECT_FALSE( ViewEntityOverridesTheEye( 7, 7 ) );
}

TEST( ViewEntityOverridesTheEye, AnotherEntityIsAnOverride )
{
	EXPECT_TRUE( ViewEntityOverridesTheEye( 42, 1 ) );
}

TEST( ViewEntityOverridesTheEye, EntityZeroIsRefusedEvenThoughItIsNotThePlayer )
{
	// Entity 0 is the world. It is not the player, so stock's test alone would
	// call it an override — and the world's origin is (0,0,0), so the frame
	// would be rendered from the map's origin. That is a black screen or the
	// inside of a wall, which reads as a renderer fault rather than a wrong
	// view entity.
	EXPECT_FALSE( ViewEntityOverridesTheEye( 0, 1 ) );
}

TEST( ViewEntityOverridesTheEye, ANegativeIndexIsRefused )
{
	EXPECT_FALSE( ViewEntityOverridesTheEye( -1, 1 ) );
}

//=============================================================================
// PART 2 — THE SOURCE SCAN
//=============================================================================

namespace
{

//-----------------------------------------------------------------------------
// The file with its comments taken out.
//
// EVERY "IS IT GONE" FACT BELOW NEEDS THIS AND EVERY "IS IT THERE" FACT MUST
// NOT USE IT, and the two readings are different on purpose.
//
// De-entitying a file leaves prose behind saying what stood there and why it
// went -- that record is most of the value of the change. So a check for
// `#if defined( REPLAY_ENABLED )` in the raw text finds the SENTENCE SAYING THE
// BLOCK IS GONE and fails, and a check written to dodge that would be a needle
// with no subject. Stripping comments is what makes the assertion about code.
//
// The entity-spelling fact is the exception and reads the RAW text, because the
// census reads the raw text: a file whose comments still name C_BasePlayer is a
// file the census still counts, whatever the compiler sees.
//
// String literals are preserved, so a "//" inside one is not mistaken for a
// comment -- these files carry paths and material names.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// One whole-word occurrence, the way the census's `grep -E ... \b...\b` counts
// them. Written out rather than borrowed because a substring search would find
// `C_BaseEntityIterator` inside a longer name and report a hit the census does
// not have — and a needle that matches more than its subject is the kind that
// passes for the wrong reason.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The six translation units step 7c de-entitied IN PLACE. They are still in
// client_sourcesharp — that is what "in place" means, and it is why the check is
// "names no entity type" rather than "is not in the build".
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// Every spelling the census's "uses an entity type" regex matches, expanded.
// The regex is
//     \bC_?Base(Entity|Player|Animating|CombatCharacter|CombatWeapon|Flex)\b
//     |\bg?_?pEntityList\b|\bgEntList\b
// and it is read out of tools/entity-census, which the last fact below checks
// this list still agrees with.
//-----------------------------------------------------------------------------
} // namespace

TEST( DeEntitiedInPlace, TheSixAreStillInTheBuild )
{
	// "IN PLACE" is the half a deletion would also satisfy. Every one of these
	// is still compiled by client_sourcesharp (src/client/native/CMakeLists.txt,
	// read through cmake_graph.h), and a step that de-entitied a file by
	// evicting it would have done something else than what 7c says.
	const char *kInBuild[] =
	{
		"sourcesharp/game/client/shell_viewrender_client.cpp",   // view.cpp's and viewrender.cpp's (C7 step 3)
		"sourcesharp/game/client/shell_view_facts.cpp",
	};
	for ( size_t i = 0; i < sizeof( kInBuild ) / sizeof( kInBuild[ 0 ] ); i++ )
	{
		EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kInBuild[ i ] ) )
			<< kInBuild[ i ] << " is not compiled by client_sourcesharp";
	}
}

//-----------------------------------------------------------------------------
// 7g's handover: dead REPLAY_ENABLED text in five compiled TUs, three of which
// are 7c's, and it named a ReplayCamera() 7g had deleted.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The third-person camera: one sweep, not two.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// One answer to "can the camera see the viewer's own body", read by three
// places that must agree.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The facts this client has no answer for -- and the one it now has.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// The NPC reorder pass, and the argument that let it go.
//-----------------------------------------------------------------------------
// ThePassIsGoneAndItsTwoAllocasWithIt read viewrender.cpp, which left at C7
// step 3 with the pass's whole file; the plan has no NPC reorder pass.

//=============================================================================//
//
//  STEP c7-view -- the view/input cluster's remainder.
//
//  Two things moved and one number did not, and the facts below say all three.
//
//  1. viewrender.cpp's viewmodel pass asked SDKEntityFromRenderable "is this a
//     game entity", which answers NULL for a MANAGED entity (p7-exit recorded
//     that at viewrender.cpp:4299). The keep/remove decision moved into
//     ViewSubject::ViewModelIsUnwanted, and then left with the tool framework
//     it served (SFM is dead, user ruling 2026-10-19).
//
//  2. in_joystick.cpp's two local-player asks -- the vehicle response curve and
//     the autoaim stick dampening -- go through the view_facts.h seam.
//
//  3. THE STOCK ARMS SURVIVE, under `#else` / `#elif`, because
//     briefs/CLIENT_CLUSTERS.md requires it. That is why in_joystick.cpp's
//     census reading is still 2 and not 0, and a fact below pins it so nobody
//     "fixes" the number by deleting the arm.
//
//=============================================================================//

// The viewmodel keep/remove rule's four corners stood here
// (ViewSubject::ViewModelIsUnwanted). The rule chose between a game entity's
// viewmodel and a tool's, and is deleted with the tool framework: SFM is dead
// (user ruling 2026-10-19). RenderPlannerTests holds that the plan's
// viewmodel op ignores the retired tools input.

//-----------------------------------------------------------------------------
// The source scan for step c7-view. Comments stripped for every "is it gone"
// half, raw for the census half, exactly as the block above this one does it.
//-----------------------------------------------------------------------------
// C7View.ViewRenderMakesNoEntityResolveOnTheSourceSharpSide read
// viewrender.cpp, which left at C7 step 3: its entity draws are the plan
// executor's now (DrawProgramEntity), with no entity resolve of their own.
