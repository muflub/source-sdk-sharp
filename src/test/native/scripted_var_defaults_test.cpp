//========= SourceSharp — tests for the shape jet_lifetime.h was one of ======//
//
// env_smokestack's missing JetLength put an infinity into a vertex and
// jet_lifetime.h fixed that one. A sweep for the same SHAPE — a managed scripted
// var with no initialiser feeding a divide — found three more live sites, and
// scripted_var_defaults.h is the rule they share. These are the tests for the
// rule; the sites that call it have their own reasons recorded where they are.
//
// What is pinned, and why each is a case somebody would otherwise get wrong:
//
//   NaN IS NOT ORDERED, SO A `<` OR `>` GUARD DOES NOT SEE IT. This is the
//   whole reason the header exists rather than an `if` per site. Every predicate
//   below is checked with a NaN from BOTH sides, because a rule written
//   `if ( f < 0.0f ) return fallback;` looks right and passes NaN straight out.
//
//   SOURCE'S OWN clamp() PASSES A NaN THROUGH. `a > max ? max : (a < min ? min :
//   a)` is false at both branches for a NaN, so clamping is not sanitising.
//   SourceSharp_LifeFraction exists because six particle renderers each wrote
//   that clamp around a divide by a die time.
//
//   A ZERO NUMERATOR IS NOT SAFE. It passes `!= 0` on the denominator and yields
//   a finite, innocent-looking zero, which the next layer divides BY. The
//   quotient's contract is checked on its OUTPUT for that reason, not only on
//   its inputs.
//
//   A DENORMAL IS A ZERO WEARING A DIFFERENT HAT. 1.0f / 1e-40f is an infinity,
//   so "strictly positive" has to mean strictly positive after the divide too.
//
//   AN INT KEY CANNOT BE NaN BUT CAN BE ABSURD. The managed parser reads an int
//   key by parsing a float and truncating, and .NET SATURATES: measured on this
//   build's runtime, "1e40" arrives as 2147483647 and "nan" as 0. So the int
//   overload's whole job is "above zero" and it is tested at the boundary.
//
// Engine-free: arithmetic over floats.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../game/shared/scripted_var_defaults.h"

#include <cmath>
#include <limits>

namespace
{

const float kNaN     = std::numeric_limits<float>::quiet_NaN();
const float kInf     = std::numeric_limits<float>::infinity();
const float kDenorm  = 1e-40f;

// The contract, restated independently of the header so a test is not asking
// the thing under test whether it is right. std::isfinite rather than the
// header's own magnitude comparison, for the same reason.
bool FiniteAndPositive( float f )
{
	return std::isfinite( f ) && ( f > 0.0f );
}

} // namespace

//-----------------------------------------------------------------------------
// SourceSharp_VarIsFinite
//-----------------------------------------------------------------------------

TEST( ScriptedVarDefaults, OrdinaryNumbersAreFinite )
{
	EXPECT_TRUE( SourceSharp_VarIsFinite( 0.0f ) );
	EXPECT_TRUE( SourceSharp_VarIsFinite( -1.0f ) );
	EXPECT_TRUE( SourceSharp_VarIsFinite( 3.402823466e+38f ) );
	EXPECT_TRUE( SourceSharp_VarIsFinite( kDenorm ) );
}

TEST( ScriptedVarDefaults, NeitherInfinityIsFinite )
{
	EXPECT_FALSE( SourceSharp_VarIsFinite( kInf ) );
	EXPECT_FALSE( SourceSharp_VarIsFinite( -kInf ) );
}

TEST( ScriptedVarDefaults, NaNIsNotFinite )
{
	// The case a magnitude test alone gets wrong: both `f <= huge` and
	// `f >= -huge` are FALSE for a NaN, so a test written only as
	// `return !(f > huge || f < -huge)` would call a NaN finite.
	EXPECT_FALSE( SourceSharp_VarIsFinite( kNaN ) );
	EXPECT_FALSE( SourceSharp_VarIsFinite( -kNaN ) );
}

//-----------------------------------------------------------------------------
// SourceSharp_IsUsablePositive — the predicate for "stock had no default here"
//-----------------------------------------------------------------------------

TEST( ScriptedVarDefaults, ZeroIsNotUsable )
{
	// The whole case. A map that omits a key sends zero.
	EXPECT_FALSE( SourceSharp_IsUsablePositive( 0.0f ) );
	EXPECT_FALSE( SourceSharp_IsUsablePositive( -0.0f ) );
}

TEST( ScriptedVarDefaults, NegativesAreNotUsable )
{
	EXPECT_FALSE( SourceSharp_IsUsablePositive( -1.0f ) );
	EXPECT_FALSE( SourceSharp_IsUsablePositive( -kDenorm ) );
}

TEST( ScriptedVarDefaults, NaNAndInfinityAreNotUsable )
{
	EXPECT_FALSE( SourceSharp_IsUsablePositive( kNaN ) );
	EXPECT_FALSE( SourceSharp_IsUsablePositive( kInf ) );
	EXPECT_FALSE( SourceSharp_IsUsablePositive( -kInf ) );
}

TEST( ScriptedVarDefaults, AnythingAboveZeroIsUsable )
{
	EXPECT_TRUE( SourceSharp_IsUsablePositive( 1.0f ) );
	EXPECT_TRUE( SourceSharp_IsUsablePositive( 1e-6f ) );
	EXPECT_TRUE( SourceSharp_IsUsablePositive( 3.0e38f ) );
}

TEST( ScriptedVarDefaults, TheIntegerOverloadIsAboveZeroAndNothingElse )
{
	// "1e40" in an int key arrives as 2147483647 through the managed parser's
	// saturating float-to-int, and "nan" arrives as 0. So the boundary is the
	// whole rule, and the absurd-but-positive value is deliberately USABLE:
	// refusing it would be a different judgement made in the wrong place.
	EXPECT_FALSE( SourceSharp_IsUsablePositive( 0 ) );
	EXPECT_FALSE( SourceSharp_IsUsablePositive( -1 ) );
	EXPECT_TRUE( SourceSharp_IsUsablePositive( 1 ) );
	EXPECT_TRUE( SourceSharp_IsUsablePositive( 2147483647 ) );
}

//-----------------------------------------------------------------------------
// SourceSharp_UsableOr — the value, or stock's own constructor default
//-----------------------------------------------------------------------------

TEST( ScriptedVarDefaults, AUsableValueIsKept )
{
	EXPECT_FLOAT_EQ( 42.0f, SourceSharp_UsableOr( 42.0f, 80.0f ) );
}

TEST( ScriptedVarDefaults, EveryDegenerateValueBecomesTheStockDefault )
{
	EXPECT_FLOAT_EQ( 80.0f, SourceSharp_UsableOr( 0.0f, 80.0f ) );
	EXPECT_FLOAT_EQ( 80.0f, SourceSharp_UsableOr( -5.0f, 80.0f ) );
	EXPECT_FLOAT_EQ( 80.0f, SourceSharp_UsableOr( kNaN, 80.0f ) );
	EXPECT_FLOAT_EQ( 80.0f, SourceSharp_UsableOr( kInf, 80.0f ) );
}

//-----------------------------------------------------------------------------
// SourceSharp_UsableQuotient
//-----------------------------------------------------------------------------

TEST( ScriptedVarDefaults, AnOrdinaryQuotientIsTheArithmetic )
{
	EXPECT_FLOAT_EQ( 0.5f, SourceSharp_UsableQuotient( 60.0f, 120.0f, 7.0f ) );
	EXPECT_FLOAT_EQ( 2.0f, SourceSharp_UsableQuotient( 240.0f, 120.0f, 7.0f ) );
}

TEST( ScriptedVarDefaults, AZeroNumeratorIsRefusedAndNotReturnedAsZero )
{
	// The half of this that `if ( denominator != 0 )` gets wrong. A zero
	// numerator over a good denominator is a perfectly finite 0.0f, and the
	// layer below divides BY it.
	const float f = SourceSharp_UsableQuotient( 0.0f, 120.0f, 7.0f );

	EXPECT_FLOAT_EQ( 7.0f, f );
	EXPECT_TRUE( FiniteAndPositive( f ) );
}

TEST( ScriptedVarDefaults, AZeroDenominatorIsRefused )
{
	const float f = SourceSharp_UsableQuotient( 80.0f, 0.0f, 7.0f );

	EXPECT_FLOAT_EQ( 7.0f, f );
	EXPECT_TRUE( FiniteAndPositive( f ) );
}

TEST( ScriptedVarDefaults, OverflowFromTwoFiniteArgumentsIsRefused )
{
	// 1e38 / 1e-38 is an infinity from two finite, positive floats, so checking
	// the inputs is not enough and the OUTPUT is checked as well.
	const float f = SourceSharp_UsableQuotient( 1e38f, 1e-38f, 7.0f );

	EXPECT_FLOAT_EQ( 7.0f, f );
	EXPECT_TRUE( FiniteAndPositive( f ) );
}

TEST( ScriptedVarDefaults, UnderflowFromTwoFiniteArgumentsIsRefused )
{
	// And the other way: a result that underflows to zero is the
	// divide-by-zero case one layer down wearing a different hat.
	const float f = SourceSharp_UsableQuotient( 1e-38f, 1e38f, 7.0f );

	EXPECT_FLOAT_EQ( 7.0f, f );
	EXPECT_TRUE( FiniteAndPositive( f ) );
}

TEST( ScriptedVarDefaults, EveryQuotientTheRuleReturnsIsFiniteAndPositive )
{
	// The guarantee itself, over the whole awkward set at once. This is the
	// property every caller relies on and no caller re-checks.
	const float awkward[] = { 0.0f, -0.0f, 1.0f, -1.0f, kNaN, kInf, -kInf,
	                          kDenorm, -kDenorm, 1e38f, -1e38f, 120.0f };

	for ( float a : awkward )
	{
		for ( float b : awkward )
		{
			EXPECT_TRUE( FiniteAndPositive( SourceSharp_UsableQuotient( a, b, 7.0f ) ) )
				<< "quotient of " << a << " and " << b;
		}
	}
}

//-----------------------------------------------------------------------------
// SourceSharp_LifeFraction — the shape six particle renderers share
//-----------------------------------------------------------------------------

TEST( ScriptedVarDefaults, HalfWayThroughALifeIsAHalf )
{
	EXPECT_FLOAT_EQ( 0.5f, SourceSharp_LifeFraction( 2.0f, 4.0f ) );
	EXPECT_FLOAT_EQ( 0.0f, SourceSharp_LifeFraction( 0.0f, 4.0f ) );
	EXPECT_FLOAT_EQ( 1.0f, SourceSharp_LifeFraction( 4.0f, 4.0f ) );
}

TEST( ScriptedVarDefaults, PastTheEndIsTheEnd )
{
	EXPECT_FLOAT_EQ( 1.0f, SourceSharp_LifeFraction( 9.0f, 4.0f ) );
}

TEST( ScriptedVarDefaults, NoDieTimeReadsAsFinishedRatherThanAsNaN )
{
	// env_embers with no "lifetime" key: m_flDieTime = 0, and the open-coded
	// version is 0.0f / 0.0f = NaN on the frame the particle is born. That NaN
	// becomes the particle's SIZE through CSimpleEmitter::UpdateScale and the
	// size is what CVertexBuilder::Position3f is handed.
	//
	// One is the answer every caller already reads as "this particle is done".
	EXPECT_FLOAT_EQ( 1.0f, SourceSharp_LifeFraction( 0.0f, 0.0f ) );
	EXPECT_FLOAT_EQ( 1.0f, SourceSharp_LifeFraction( 0.25f, 0.0f ) );
	EXPECT_FLOAT_EQ( 1.0f, SourceSharp_LifeFraction( 1.0f, -3.0f ) );
}

TEST( ScriptedVarDefaults, ADegenerateLifetimeNeverEscapesTheFraction )
{
	// The guarantee: whatever goes in, what comes out is in [0,1] and is a
	// number. Source's clamp() cannot say this — it passes a NaN through both
	// of its branches — which is exactly why this is a function.
	const float awkward[] = { 0.0f, -0.0f, 1.0f, -1.0f, kNaN, kInf, -kInf,
	                          kDenorm, -kDenorm, 1e38f, -1e38f, 4.0f };

	for ( float life : awkward )
	{
		for ( float die : awkward )
		{
			const float f = SourceSharp_LifeFraction( life, die );

			EXPECT_TRUE( f == f ) << "life " << life << " die " << die << " gave a NaN";
			EXPECT_GE( f, 0.0f );
			EXPECT_LE( f, 1.0f );
		}
	}
}
