//========= SourceSharp — tests for how long a puff of a jet lives ===========//
//
// This rule exists because of a real, reproduced defect:
//
//   public/materialsystem/imesh.h (1665) : IsFinite(x) && IsFinite(y) && IsFinite(z)
//
// A map-placed env_smokestack with no JetLength key made
// m_InvLifetime = m_Speed / 0 = +Inf; C_SmokeStack::RenderParticles multiplied a
// particle's age by it and FLerped the particle's SIZE with the result; and the
// size is what CVertexBuilder::Position3f is handed. Three env_smokestacks
// spawned with default keys produced five thousand of these assertions in
// ninety seconds.
//
// What the tests pin, in the order the bug used them:
//
//   THE DIVISION IS NOT THE ONLY DEGENERATE CASE. `if (x != 0)` is the guard
//   that was there, and a zero NUMERATOR sails through it to produce a finite
//   zero — which is then divided BY, one layer down, in both renderers. So the
//   rule's contract is not "no division by zero", it is "finite and strictly
//   positive", and there are tests for the zero-numerator case specifically.
//
//   NaN IS NOT ORDERED. Every guard on these paths is a `<` or a `>`, and every
//   comparison with NaN is false, so a NaN walks through the lot. A rule written
//   as `if (x < 0) return default;` would pass NaN straight out again, so the
//   NaN cases are tested from both sides rather than assumed.
//
//   OVERFLOW AND UNDERFLOW ARE REACHABLE FROM FINITE INPUTS. 1e38 / 1e-38 is
//   infinity and 1e-38 / 1e38 is zero, from two perfectly finite floats, so
//   checking the inputs is not sufficient and the OUTPUT is checked too.
//
//   THE TWO CALLERS ARE RECIPROCALS OF EACH OTHER. C_SteamJet wants a duration
//   and C_SmokeStack wants its inverse, so the same pair of map keys goes in the
//   opposite way up. Getting that backwards is invisible — both answers are
//   finite, positive, plausible numbers — so it is pinned against the arithmetic
//   rather than eyeballed.
//
// Engine-free: this is arithmetic over two floats.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../game/shared/jet_lifetime.h"

#include <limits>

namespace
{

const float kNaN      = std::numeric_limits<float>::quiet_NaN();
const float kInf      = std::numeric_limits<float>::infinity();

const float kSteamDefault = SOURCESHARP_STEAMJET_DEFAULT_LENGTH
                          / SOURCESHARP_STEAMJET_DEFAULT_SPEED;
const float kStackDefault = SOURCESHARP_SMOKESTACK_DEFAULT_SPEED
                          / SOURCESHARP_SMOKESTACK_DEFAULT_LENGTH;

// The contract, as a predicate. Every test below ends here, because "finite and
// strictly positive" is the whole guarantee and anything else is a NaN pump.
bool Usable( float f )
{
	return ( f == f ) && ( f < kInf ) && ( f > -kInf ) && ( f > 0.0f );
}

//-----------------------------------------------------------------------------
// The finiteness helper the rule is built on.
//-----------------------------------------------------------------------------

TEST( JetIsUsable, OrdinaryNumbersAreUsable )
{
	EXPECT_TRUE( SourceSharp_JetIsUsable( 0.0f ) );
	EXPECT_TRUE( SourceSharp_JetIsUsable( 1.0f ) );
	EXPECT_TRUE( SourceSharp_JetIsUsable( -1.0f ) );
	EXPECT_TRUE( SourceSharp_JetIsUsable( 3.4e38f ) );
	EXPECT_TRUE( SourceSharp_JetIsUsable( -3.4e38f ) );
}

TEST( JetIsUsable, NaNAndBothInfinitiesAreNot )
{
	EXPECT_FALSE( SourceSharp_JetIsUsable( kNaN ) );
	EXPECT_FALSE( SourceSharp_JetIsUsable( kInf ) );
	EXPECT_FALSE( SourceSharp_JetIsUsable( -kInf ) );
}

//-----------------------------------------------------------------------------
// The steam jet's duration.
//-----------------------------------------------------------------------------

TEST( SteamJetLifetime, AConfiguredJetDividesNormally )
{
	EXPECT_FLOAT_EQ( 0.5f, SourceSharp_SteamJetLifetime( 60.0f, 120.0f ) );
	EXPECT_FLOAT_EQ( 2.0f, SourceSharp_SteamJetLifetime( 240.0f, 120.0f ) );
}

// The reported case: a map that set no keys at all.
TEST( SteamJetLifetime, BothKeysZeroIsTheDefaultAndNotNaN )
{
	const float f = SourceSharp_SteamJetLifetime( 0.0f, 0.0f );

	EXPECT_TRUE( Usable( f ) ) << f;
	EXPECT_FLOAT_EQ( kSteamDefault, f );
}

TEST( SteamJetLifetime, ZeroSpeedIsTheDefaultAndNotInfinity )
{
	const float f = SourceSharp_SteamJetLifetime( 80.0f, 0.0f );

	EXPECT_TRUE( Usable( f ) ) << f;
	EXPECT_FLOAT_EQ( kSteamDefault, f );
}

// The case `if (flSpeed != 0.0f)` let through: a finite ZERO, which is not an
// infinity and is just as fatal, because RenderParticles divides BY the die time
// it becomes.
TEST( SteamJetLifetime, ZeroLengthIsTheDefault_AZeroIsNotSafeJustBecauseItIsFinite )
{
	const float f = SourceSharp_SteamJetLifetime( 0.0f, 120.0f );

	EXPECT_TRUE( Usable( f ) ) << f;
	EXPECT_NE( 0.0f, f );
	EXPECT_FLOAT_EQ( kSteamDefault, f );
}

TEST( SteamJetLifetime, NegativeInputsAreTheDefault )
{
	EXPECT_FLOAT_EQ( kSteamDefault, SourceSharp_SteamJetLifetime( -80.0f, 120.0f ) );
	EXPECT_FLOAT_EQ( kSteamDefault, SourceSharp_SteamJetLifetime( 80.0f, -120.0f ) );
	EXPECT_FLOAT_EQ( kSteamDefault, SourceSharp_SteamJetLifetime( -80.0f, -120.0f ) );
}

TEST( SteamJetLifetime, NaNAndInfinityInEitherArgumentAreTheDefault )
{
	EXPECT_FLOAT_EQ( kSteamDefault, SourceSharp_SteamJetLifetime( kNaN, 120.0f ) );
	EXPECT_FLOAT_EQ( kSteamDefault, SourceSharp_SteamJetLifetime( 80.0f, kNaN ) );
	EXPECT_FLOAT_EQ( kSteamDefault, SourceSharp_SteamJetLifetime( kNaN, kNaN ) );
	EXPECT_FLOAT_EQ( kSteamDefault, SourceSharp_SteamJetLifetime( kInf, 120.0f ) );
	EXPECT_FLOAT_EQ( kSteamDefault, SourceSharp_SteamJetLifetime( 80.0f, kInf ) );
	EXPECT_FLOAT_EQ( kSteamDefault, SourceSharp_SteamJetLifetime( -kInf, 120.0f ) );
	EXPECT_FLOAT_EQ( kSteamDefault, SourceSharp_SteamJetLifetime( 80.0f, -kInf ) );
}

//-----------------------------------------------------------------------------
// The smoke stack's reciprocal — the one the vertex assert came out of.
//-----------------------------------------------------------------------------

TEST( SmokeStackInvLifetime, AConfiguredStackDividesNormally )
{
	// Speed over length: a stack 180 long at 30 a second lives six seconds, so
	// the inverse lifetime is one sixth.
	EXPECT_FLOAT_EQ( 30.0f / 180.0f,
	                 SourceSharp_SmokeStackInvLifetime( 30.0f, 180.0f ) );
}

// THE REPRODUCED DEFECT. Speed set, JetLength left at the C# property's
// uninitialised zero.
TEST( SmokeStackInvLifetime, ZeroJetLengthIsTheDefaultAndNotInfinity )
{
	const float f = SourceSharp_SmokeStackInvLifetime( 30.0f, 0.0f );

	EXPECT_TRUE( Usable( f ) ) << f;
	EXPECT_FLOAT_EQ( kStackDefault, f );
}

TEST( SmokeStackInvLifetime, BothKeysZeroIsTheDefaultAndNotNaN )
{
	const float f = SourceSharp_SmokeStackInvLifetime( 0.0f, 0.0f );

	EXPECT_TRUE( Usable( f ) ) << f;
	EXPECT_FLOAT_EQ( kStackDefault, f );
}

TEST( SmokeStackInvLifetime, ZeroSpeedIsTheDefaultAndNotZero )
{
	const float f = SourceSharp_SmokeStackInvLifetime( 0.0f, 180.0f );

	EXPECT_TRUE( Usable( f ) ) << f;
	EXPECT_NE( 0.0f, f );
}

TEST( SmokeStackInvLifetime, NaNAndInfinityInEitherArgumentAreTheDefault )
{
	EXPECT_FLOAT_EQ( kStackDefault, SourceSharp_SmokeStackInvLifetime( kNaN, 180.0f ) );
	EXPECT_FLOAT_EQ( kStackDefault, SourceSharp_SmokeStackInvLifetime( 30.0f, kNaN ) );
	EXPECT_FLOAT_EQ( kStackDefault, SourceSharp_SmokeStackInvLifetime( kInf, 180.0f ) );
	EXPECT_FLOAT_EQ( kStackDefault, SourceSharp_SmokeStackInvLifetime( 30.0f, kInf ) );
}

// The two callers take the SAME pair of map keys the opposite way up, and
// getting that backwards produces a finite, positive, entirely plausible wrong
// number that nothing would ever report.
TEST( JetLifetime, TheTwoCallersAreReciprocalsOfEachOther )
{
	const float flLength = 240.0f;
	const float flSpeed  = 60.0f;

	const float flDuration = SourceSharp_SteamJetLifetime( flLength, flSpeed );
	const float flInverse  = SourceSharp_SmokeStackInvLifetime( flSpeed, flLength );

	EXPECT_FLOAT_EQ( 4.0f, flDuration );
	EXPECT_FLOAT_EQ( 1.0f / 4.0f, flInverse );
	EXPECT_FLOAT_EQ( 1.0f, flDuration * flInverse );
}

//-----------------------------------------------------------------------------
// Finite inputs are not sufficient: the OUTPUT has to be checked too.
//-----------------------------------------------------------------------------

TEST( JetLifetime, FiniteInputsThatOverflowToInfinityAreTheDefault )
{
	// 1e38 / 1e-38 is not representable. Both arguments are perfectly finite.
	const float f = SourceSharp_SteamJetLifetime( 1e38f, 1e-38f );

	EXPECT_TRUE( Usable( f ) ) << f;
	EXPECT_FLOAT_EQ( kSteamDefault, f );
}

TEST( JetLifetime, FiniteInputsThatUnderflowToZeroAreTheDefault )
{
	// 1e-38 / 1e38 flushes to zero, which is the divide-by-zero case one layer
	// down wearing a different hat.
	const float f = SourceSharp_SteamJetLifetime( 1e-38f, 1e38f );

	EXPECT_TRUE( Usable( f ) ) << f;
	EXPECT_NE( 0.0f, f );
	EXPECT_FLOAT_EQ( kSteamDefault, f );
}

//-----------------------------------------------------------------------------
// The contract itself, swept. Whatever goes in, what comes out is usable — and
// what comes out for a well-formed jet is the quotient and not the default.
//-----------------------------------------------------------------------------

TEST( JetLifetime, EveryDegenerateInputPairStillProducesAUsableNumber )
{
	const float rgInputs[] =
	{
		0.0f, -0.0f, 1.0f, -1.0f, 1e-45f, 1e38f, 1e-38f, 3.4e38f,
		kNaN, kInf, -kInf, 120.0f, -120.0f
	};
	const int nInputs = (int)( sizeof( rgInputs ) / sizeof( rgInputs[ 0 ] ) );

	for ( int i = 0; i < nInputs; ++i )
	{
		for ( int j = 0; j < nInputs; ++j )
		{
			const float flDuration = SourceSharp_SteamJetLifetime( rgInputs[ i ], rgInputs[ j ] );
			const float flInverse  = SourceSharp_SmokeStackInvLifetime( rgInputs[ i ], rgInputs[ j ] );

			EXPECT_TRUE( Usable( flDuration ) )
				<< "length " << rgInputs[ i ] << " speed " << rgInputs[ j ]
				<< " gave " << flDuration;
			EXPECT_TRUE( Usable( flInverse ) )
				<< "speed " << rgInputs[ i ] << " length " << rgInputs[ j ]
				<< " gave " << flInverse;
		}
	}
}

// ...and the other half of that sweep, which is the one a rule that simply
// returned the default for EVERYTHING would fail. Without this, "always usable"
// is satisfied by a function that ignores its arguments.
TEST( JetLifetime, AWellFormedJetGetsItsOwnAnswerAndNotTheDefault )
{
	EXPECT_FLOAT_EQ( 3.0f, SourceSharp_SteamJetLifetime( 360.0f, 120.0f ) );
	EXPECT_NE( kSteamDefault, SourceSharp_SteamJetLifetime( 360.0f, 120.0f ) );

	EXPECT_FLOAT_EQ( 0.25f, SourceSharp_SmokeStackInvLifetime( 45.0f, 180.0f ) );
	EXPECT_NE( kStackDefault, SourceSharp_SmokeStackInvLifetime( 45.0f, 180.0f ) );
}

//-----------------------------------------------------------------------------
// The defaults are stock's own constructor pairs, restated here. A restated
// constant that drifts is the stale-fixture failure this tree has had before:
// the check would still pass, against the wrong number, forever.
//-----------------------------------------------------------------------------
TEST( JetLifetime, TheDefaultsAreStocksOwnConstructorPairs )
{
	// C_SteamJet::C_SteamJet   — m_JetLength = 80,  m_Speed = 120
	EXPECT_FLOAT_EQ( 80.0f,  SOURCESHARP_STEAMJET_DEFAULT_LENGTH );
	EXPECT_FLOAT_EQ( 120.0f, SOURCESHARP_STEAMJET_DEFAULT_SPEED );

	// C_SmokeStack::C_SmokeStack — m_JetLength = 180, m_Speed = 30
	EXPECT_FLOAT_EQ( 180.0f, SOURCESHARP_SMOKESTACK_DEFAULT_LENGTH );
	EXPECT_FLOAT_EQ( 30.0f,  SOURCESHARP_SMOKESTACK_DEFAULT_SPEED );
}

} // namespace
