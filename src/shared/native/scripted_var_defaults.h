//========= SourceSharp — a map key that nobody wrote, and the divide below it ===//
//
// jet_lifetime.h is the worked example of one instance of this. This is the
// shape, for the rest of them.
//
// THE SHAPE. A stock client effect divides by a value that arrived over the wire
// from a C++ server class. The managed replacement declares that value as
//
//     public float Foo { get; set; }
//
// with no initialiser, so a map that omits the key sends ZERO, and a divide that
// was safe for a decade becomes an infinity or a NaN. Three things make it hard
// to see, and all three are why this is a header rather than an `if` at each
// site:
//
//   THE BAD VALUE IS USUALLY A SIZE OR AN ALPHA, NOT A POSITION. It becomes a
//   vertex only at the last step, `pos.x ± size`, which is why the assert names
//   materialsystem/imesh.h and not the entity.
//
//   EVERY GUARD ON THESE PATHS IS A `<` OR A `>`. Every comparison with NaN is
//   false, so `if ( alpha < 0.5f ) return;` does not discard a NaN particle and
//   `if ( tLifetime >= 1 ) RemoveParticle` does not kill one. The bad particles
//   are drawn AND immortal. Source's own clamp() is written `a > max ? max :
//   (a < min ? min : a)` and therefore passes a NaN straight through, which is
//   the single most surprising fact on this page.
//
//   A FINITE ZERO IS NOT SAFE. `if ( x != 0 )` on the DENOMINATOR lets a zero
//   NUMERATOR through, and the legitimate-looking 0.0f it produces is what the
//   next layer divides by.
//
// NaN AND INFINITY ARE REACHABLE FROM ORDINARY MAP TEXT, which is easy to
// assume away. The managed key parser is
//
//     float.TryParse( value, NumberStyles.Float, InvariantCulture, out result )
//
// and, measured on this build's runtime rather than remembered:
//
//     "nan"     parses, giving NaN            (and (int)NaN is 0)
//     "1e40"    parses, giving +Infinity      (and (int)+Inf is 2147483647)
//
// So "the mapper would have to try" is not a defence. An ordinary large number
// in an ordinary key is an infinity.
//
// THE INVARIANT, which is the same one jet_lifetime.h states:
//
//     A VALUE THAT IS DIVIDED BY IS ALWAYS FINITE AND STRICTLY POSITIVE.
//
// Not "not zero". Not "not NaN".
//
// TWO ANSWERS, AND CHOOSING BETWEEN THEM IS THE WHOLE JUDGEMENT.
//
//   Where stock's own C++ class had a CONSTRUCTOR DEFAULT, the port lost a real
//   number and the fix is to put that number back — SourceSharp_UsableOr with
//   stock's value. The result looks like stock's unconfigured entity rather than
//   like a number somebody chose to make an assert stop firing.
//
//   Where stock had NO default either — CEmbers, CFuncSmokeVolume and
//   CSpriteTrail each set nothing — there IS no stock number, and inventing one
//   would be dressing a guess as a port. Then the honest answer is that the
//   entity was never configured, and the effect declines to draw:
//   SourceSharp_IsUsablePositive as a predicate at the point of use. An
//   unconfigured effect drawing nothing is what stock does too, once you follow
//   what its arithmetic actually produces.
//
// Engine-free on purpose, like jet_lifetime.h: arithmetic over floats, so the
// rule can be tested with no Source behind it.
//
//=============================================================================//

#ifndef SOURCESHARP_SCRIPTED_VAR_DEFAULTS_H
#define SOURCESHARP_SCRIPTED_VAR_DEFAULTS_H
#ifdef _WIN32
#pragma once
#endif

//-----------------------------------------------------------------------------
// Finite, without mathlib.
//
// x != x is true only for NaN. The magnitude test catches both infinities.
// Written out rather than calling IsFinite() so this header needs nothing but
// itself and can be compiled into a test binary with no engine behind it — the
// same reason jet_lifetime.h spells it out, and deliberately the same code.
//
// THE NaN BRANCH BELOW IS REDUNDANT, AND SAYING SO IS THE POINT. Deleting it
// changes no answer and fails no test, because the magnitude test underneath is
// `( f <= huge ) && ( f >= -huge )` and BOTH of those comparisons are already
// false for a NaN. That was measured, not assumed: mutating `if ( f != f )` to
// `if ( false )` produced ZERO reds out of twenty tests.
//
// It is kept anyway, and this note is here so nobody later reads its survival as
// evidence of anything. What it guards against is a REPHRASING: written the
// other, equally natural way round — `return !( f > huge || f < -huge );` — the
// magnitude test calls a NaN FINITE, and this branch is what would still stop
// it. A line that cannot be shown to matter is exactly the thing this project
// keeps finding fifteen of, so it is recorded as deliberate redundancy rather
// than left looking load-bearing.
//
// jet_lifetime.h's SourceSharp_JetIsUsable carries the same line, for the same
// reason and with the same non-effect.
//-----------------------------------------------------------------------------
inline bool SourceSharp_VarIsFinite( float f )
{
	if ( f != f )
		return false;                       // NaN — redundant; see the note above

	// A float's largest finite magnitude. Anything at or past it is an infinity.
	const float flHuge = 3.402823466e+38f;

	return ( f <= flHuge ) && ( f >= -flHuge );
}

//-----------------------------------------------------------------------------
// The predicate: is this value one a divide may stand on?
//
// Use where stock had no default of its own, so there is nothing to fall back
// TO and the honest answer is that the effect was never configured.
//
// STRICTLY positive: zero is the case this exists for, and a denormal is a zero
// wearing a different hat — 1.0f / 1e-40f is an infinity.
//-----------------------------------------------------------------------------
inline bool SourceSharp_IsUsablePositive( float f )
{
	return SourceSharp_VarIsFinite( f ) && ( f > 0.0f );
}

//-----------------------------------------------------------------------------
// The same question about an INTEGER key.
//
// Separate because the managed parser reads an int key by parsing a float and
// truncating, and .NET's float-to-int conversion SATURATES rather than trapping:
// "1e40" arrives as 2147483647 and "nan" as 0. An int can therefore be absurd
// but never NaN, so the whole test is "is it above zero" — spelled out here so
// a caller does not have to know which of the two rules applies to the key in
// front of it.
//-----------------------------------------------------------------------------
inline bool SourceSharp_IsUsablePositive( int n )
{
	return n > 0;
}

//-----------------------------------------------------------------------------
// The value, or stock's own default in its place.
//
// Use where stock's C++ class HAD a constructor default and the port dropped it.
// The fallback is not checked: it is a compile-time constant taken from stock,
// and a caller passing a bad one has a different bug that hiding here would only
// delay.
//-----------------------------------------------------------------------------
inline float SourceSharp_UsableOr( float f, float flStockDefault )
{
	return SourceSharp_IsUsablePositive( f ) ? f : flStockDefault;
}

//-----------------------------------------------------------------------------
// A quotient guaranteed finite and strictly positive.
//
// The result is re-checked rather than trusted, because finite arguments are not
// enough: 1e38f / 1e-38f overflows to infinity and 1e-38f / 1e38f underflows to
// zero, and a zero here is the divide-by-zero one layer down.
//
// GUARANTEE: the return value is always finite and strictly positive.
//-----------------------------------------------------------------------------
inline float SourceSharp_UsableQuotient( float flNumerator, float flDenominator,
                                         float flStockDefault )
{
	if ( !SourceSharp_IsUsablePositive( flNumerator ) )
		return flStockDefault;

	if ( !SourceSharp_IsUsablePositive( flDenominator ) )
		return flStockDefault;

	const float flResult = flNumerator / flDenominator;

	if ( !SourceSharp_IsUsablePositive( flResult ) )
		return flStockDefault;

	return flResult;
}

//-----------------------------------------------------------------------------
// A fraction of the way through a lifetime: the single most common shape in the
// particle code, written once.
//
//     flLifetime / flDieTime,  clamped to [0,1]
//
// Two things it fixes that the open-coded version does not. A die time of zero
// makes the quotient 0/0 = NaN or x/0 = Inf, and Source's clamp() PASSES A NaN
// THROUGH — `a > max ? max : (a < min ? min : a)` is false both ways — so the
// NaN reaches whatever the fraction was going to size, colour or position.
//
// A particle with no die time has no meaningful position in its own life, and
// the answer that keeps every caller safe is 1: fully aged, which is what every
// caller reads as "finished". Callers that kill a finished particle then do.
//-----------------------------------------------------------------------------
inline float SourceSharp_LifeFraction( float flLifetime, float flDieTime )
{
	if ( !SourceSharp_IsUsablePositive( flDieTime ) )
		return 1.0f;

	if ( !SourceSharp_VarIsFinite( flLifetime ) || flLifetime <= 0.0f )
		return 0.0f;

	const float flFraction = flLifetime / flDieTime;

	if ( !SourceSharp_VarIsFinite( flFraction ) )
		return 1.0f;

	if ( flFraction < 0.0f )
		return 0.0f;

	if ( flFraction > 1.0f )
		return 1.0f;

	return flFraction;
}

#endif // SOURCESHARP_SCRIPTED_VAR_DEFAULTS_H
