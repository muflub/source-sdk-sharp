//========= SourceSharp — mathlib has to be told to start ====================//
//
// Include this from every gtest binary that links mathlib.a. It registers a
// gtest global Environment whose SetUp() calls MathLib_Init(), so the library
// is up before the first TEST body runs.
//
// WHY IT IS A FILE RATHER THAN SIX COPIES.
//
// NOT calling MathLib_Init() is a GREEN TEST WITH AN ASSERTION BEHIND IT, and
// that is the whole reason this header exists.  mathlib_base.cpp guards ~30 of
// its entry points with `Assert( s_bMathlibInitialized )` — AngleMatrix at
// :1199 and VectorTransform at :261 among them.  In these binaries Assert is
// the tier0 one, which PRINTS
//
//     mathlib_base.cpp (1199) : Assertion Failed: s_bMathlibInitialized
//
// and returns.  gtest never hears about it, so the test carries on and passes.
// Worse, it passes for a reason that is not evidence: MathLib_Init() is what
// chooses between the generic and the SSE implementations of VectorNormalize,
// Sqrt, RSqrt and InvRSquared, so an uninitialised run silently measures the
// OTHER library.  When a test computes both sides of a comparison from those
// same uninitialised tables the two agree, and the agreement means nothing.
//
// collideable_center_adapter_test was found in exactly that state: four tests,
// six printed assertions, all green.  world_aligned_bounds_test had had the
// Environment since it was written and said so in a comment; the comment did
// not stop the next four binaries from being written without it.  A header the
// build can be checked against does.
//
// mathlib_init_guard_test.cpp is the check: it reads buildcpptests, finds every
// `build_and_run_mathlib` invocation, and fails if a named source does not
// include this file.  buildcpptests itself fails the run if any binary prints
// the assertion, which catches the case the scanner cannot see.
//
// MathLib_Init() early-returns when it has already run, so including this from
// more than one translation unit of the same binary is harmless.
//
//=============================================================================//

#pragma once

#include "gtest/gtest.h"

#include "mathlib/mathlib.h"

namespace
{

class MathLibEnvironment : public ::testing::Environment
{
public:
	virtual void SetUp() { MathLib_Init(); }
};

// A global rather than a fixture: some of these files build their case tables
// at static init, so the environment has to be registered before main() and up
// before the first TEST body.
::testing::Environment* const g_pSourceSharpMathLib = ::testing::AddGlobalTestEnvironment(new MathLibEnvironment);

} // namespace
