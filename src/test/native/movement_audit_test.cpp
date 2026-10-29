//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// plan_shim.md 4a item 16's instrument, tested against the readings that
// produced the defect report.
// WHAT IS UNDER TEST AND WHAT IS NOT. shell_movement_audit.cpp is compiled into
// this binary for real -- there is no engine in it, which is the reason it is a
// translation unit of its own. The half that asks the engine which value a
// ConVar holds lives in shell_movement.cpp and is not testable without a
// running game; what IS testable is the decision, and the decision is where the
// defect report went wrong twice.
// The numbers below are not invented. They are the readings in
// docs/defect-convar-order.md: sv_gravity declared 800, this mod's tuning 600;
// sv_stopspeed declared 100, tuning 75; and the six whose tuning IS the
// declaration, which are the reason "not what we registered" cannot be the
// test.

#include "gtest/gtest.h"

#include "shell_movement_audit.h"

#include <string>

namespace
{

// The two readings the defect was reported as. Named rather than spelled at
// each use, because a test that says 600 and 800 in nine places is a test
// nobody can re-point when the mod's tuning changes.
const float kGravityDeclared = 800.0f;
const float kGravityRegistered = 600.0f;

} // namespace

//-----------------------------------------------------------------------------
// The three verdicts.
//-----------------------------------------------------------------------------

TEST(MovementAudit, TheValueTheModSetIsNotAFault)
{
	EXPECT_EQ(kMovementConVarAsRegistered,
			  SourceSharp_ClassifyMovementConVar(kGravityRegistered, kGravityDeclared, kGravityRegistered));
}

TEST(MovementAudit, BackAtTheDeclaredDefaultIsTheDefect)
{
	// The exact reading plan_shim.md 4a item 16 is about: the mod set 600 and
	// the convar holds the .cpp's 800.
	EXPECT_EQ(kMovementConVarRevertedToDeclared,
			  SourceSharp_ClassifyMovementConVar(kGravityRegistered, kGravityDeclared, kGravityDeclared));
}

TEST(MovementAudit, AThirdNumberIsAnOverrideAndNotTheDefect)
{
	// `sv_gravity 700` from a server config. The eight are written as DEFAULTS
	// precisely so this wins, so calling it a fault would make the warning
	// useless on any server that tunes anything.
	EXPECT_EQ(kMovementConVarOverridden,
			  SourceSharp_ClassifyMovementConVar(kGravityRegistered, kGravityDeclared, 700.0f));
}

//-----------------------------------------------------------------------------
// The two orderings that make the decision non-obvious.
//-----------------------------------------------------------------------------

TEST(MovementAudit, WhenTheTuningIsTheDeclarationNothingIsReported)
{
	// sv_accelerate: declared "10", this mod's tuning 10. Both tests match, and
	// the honest answer is the first one -- nothing has moved. Ordering the two
	// comparisons the other way would report the defect on six of the eight
	// convars on every healthy run, which is a warning nobody would ever read
	// again.
	EXPECT_EQ(kMovementConVarAsRegistered, SourceSharp_ClassifyMovementConVar(10.0f, 10.0f, 10.0f));
}

TEST(MovementAudit, TheDecimalStringRoundTripIsNotAChange)
{
	// ConVar::SetValue(float) formats with "%f" and the read parses that back,
	// so a value with more precision than six decimals never compares equal to
	// itself. An exact comparison here reports the defect on every run.
	const float flRegistered = 0.33333334f;
	const float flAfterRoundTrip = 0.333333f;

	EXPECT_EQ(kMovementConVarAsRegistered, SourceSharp_ClassifyMovementConVar(flRegistered, 1.0f, flAfterRoundTrip));
}

TEST(MovementAudit, AStopspeedRevertIsCaughtToo)
{
	// The second of the two convars whose tuning differs from its declaration,
	// and the second reading in the defect document: 100 declared, 75 set.
	EXPECT_EQ(kMovementConVarRevertedToDeclared, SourceSharp_ClassifyMovementConVar(75.0f, 100.0f, 100.0f));
}

//-----------------------------------------------------------------------------
// The name table.
//-----------------------------------------------------------------------------

TEST(MovementAudit, TheTableIsTheEightConVarsTheWriteTargets)
{
	// The list SourceSharp_RegisterMovementParams writes, in its order. Held
	// here rather than derived, because a ninth SetEngineConVar call with no
	// row in the table would be unaudited and nothing else in the build would
	// say so.
	const char* const kExpected[] = {
		"sv_gravity",   "sv_friction",    "sv_accelerate", "sv_airaccelerate",
		"sv_stopspeed", "sv_maxvelocity", "sv_stepsize",   "sv_maxspeed",
	};

	ASSERT_EQ((int)(sizeof(kExpected) / sizeof(kExpected[0])), SourceSharp_MovementConVarCount());

	for (int i = 0; i < SourceSharp_MovementConVarCount(); i++)
	{
		EXPECT_EQ(std::string(kExpected[i]), std::string(SourceSharp_MovementConVarName(i))) << "row " << i;
	}
}

TEST(MovementAudit, ShellMovementsSlotArrayIsBigEnoughForTheTable)
{
	// shell_movement.cpp sizes its two parallel arrays with
	// `enum { kMovementConVarSlots = 8 }`, which cannot see this table. This is
	// the other half of that pin: a ninth name reddens here rather than writing
	// one float past the end of a file-scope array.
	EXPECT_EQ(8, SourceSharp_MovementConVarCount());
}

TEST(MovementAudit, EveryNameResolvesToItsOwnIndex)
{
	for (int i = 0; i < SourceSharp_MovementConVarCount(); i++)
	{
		EXPECT_EQ(i, SourceSharp_MovementConVarIndex(SourceSharp_MovementConVarName(i)));
	}
}

TEST(MovementAudit, AConVarWeDoNotWriteIsNotOurs)
{
	// The audit runs from a GLOBAL change callback, so it is handed every
	// convar in the process -- about 1,369 of them on a live dm_lockdown. The
	// filter is this function, and a false positive here is a warning about
	// somebody else's convar.
	EXPECT_EQ(-1, SourceSharp_MovementConVarIndex("sv_cheats"));
	EXPECT_EQ(-1, SourceSharp_MovementConVarIndex("sv_skyname"));
	EXPECT_EQ(-1, SourceSharp_MovementConVarIndex("sv_waterfriction"));
	EXPECT_EQ(-1, SourceSharp_MovementConVarIndex(""));
	EXPECT_EQ(-1, SourceSharp_MovementConVarIndex(NULL));
}

TEST(MovementAudit, APrefixOfOneOfTheEightIsNotOneOfTheEight)
{
	// sv_max is a prefix of sv_maxspeed and sv_maxvelocity. A match written
	// with strncmp would claim both.
	EXPECT_EQ(-1, SourceSharp_MovementConVarIndex("sv_max"));
	EXPECT_EQ(-1, SourceSharp_MovementConVarIndex("sv_gravity2"));
}
