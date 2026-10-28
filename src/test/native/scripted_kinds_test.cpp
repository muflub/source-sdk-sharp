//========= SourceSharp — tests for the scripted-kind lookup =================//
//
// The test that would have caught the null dereference.
//
// `SourceSharp_RegisterScriptedEntity` used to be a `switch` whose every `case`
// ended in `return`, followed by
//
//     EntityFactoryDictionary()->InstallFactory( pClass, pClass->GetClassName() );
//
// on a `pClass` that was initialised to NULL and assigned nowhere. That line
// dereferences NULL. It never ran, because every path returned first — the code
// was correct only by being unreachable, and nothing in the build said so: gcc
// does not warn about unreachable statements, and a variable that is read but
// never written is not "uninitialised" to the compiler when it has an
// initialiser, even a NULL one.
//
// The property that was actually wanted is "every kind has an answer", and that
// is a property of a total function returning a value, which is what
// scripted_kinds.h now is. These tests assert totality — including for kinds
// that do not exist and for the retired 4 — so a kind added without a case
// fails here rather than falling off the end of a function.
//
// Header-only and engine-free, like standalone_names_test: the lookup is four
// values and a switch, and it needs no running Source to be wrong.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../game/server/scripted_kinds.h"

#include <string.h>

namespace
{

//-----------------------------------------------------------------------------
// The direct replacement for the unreachable tail: there is no path out of this
// function that does not produce a value. Every int, not just the named kinds —
// the kind arrives from C# across the ABI, so "an int nobody declared" is a
// reachable input and not a hypothetical.
//-----------------------------------------------------------------------------
TEST( ScriptedKindParams, EveryIntHasAnAnswer )
{
	for ( int i = -8; i < 64; i++ )
	{
		SourceSharpScriptedKindParams_t p = SourceSharp_ScriptedKindParams( i );

		// SolidType_t only runs to SOLID_LAST == 7.
		EXPECT_GE( p.nSolidType, 0 ) << "kind " << i;
		EXPECT_LE( p.nSolidType, 6 ) << "kind " << i;

		// FSOLID_MAX_BITS is 10, so nothing above 0x3FF is a solid flag.
		EXPECT_EQ( 0, p.nSolidFlags & ~0x3FF ) << "kind " << i;
	}
}

//-----------------------------------------------------------------------------
// Anything the native half does not recognise falls back to LOGICAL, which is
// what the old `default:` did by sharing LOGICAL's case. A managed build newer
// than the native one must still register its classes; a server-only entity with
// no collision is the cheapest thing to be wrong about.
//-----------------------------------------------------------------------------
TEST( ScriptedKindParams, UnknownKindsFallBackToLogical )
{
	SourceSharpScriptedKindParams_t logical = SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_LOGICAL );

	// 7 left this list when it became the Player (Phase 9a).
	const int kUnknown[] = { -1, 8, 99, 1000 };

	for ( size_t i = 0; i < sizeof( kUnknown ) / sizeof( kUnknown[0] ); i++ )
	{
		SourceSharpScriptedKindParams_t p = SourceSharp_ScriptedKindParams( kUnknown[i] );

		EXPECT_EQ( logical.bServerOnly, p.bServerOnly ) << "kind " << kUnknown[i];
		EXPECT_EQ( logical.nSolidType, p.nSolidType ) << "kind " << kUnknown[i];
		EXPECT_EQ( logical.nSolidFlags, p.nSolidFlags ) << "kind " << kUnknown[i];
		EXPECT_EQ( logical.bTouchEveryTick, p.bTouchEveryTick ) << "kind " << kUnknown[i];
	}
}

//-----------------------------------------------------------------------------
// 4 was SURFACE and is retired: no C# class ever declared it, and
// func_breakable_surf declares Brush. It must NOT quietly become some other
// kind's number — an old managed build sending 4 gets the harmless fallback,
// not a brush and not a prop.
//-----------------------------------------------------------------------------
TEST( ScriptedKindParams, RetiredKindFourIsTheFallback )
{
	SourceSharpScriptedKindParams_t four = SourceSharp_ScriptedKindParams( 4 );
	SourceSharpScriptedKindParams_t logical = SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_LOGICAL );

	EXPECT_EQ( logical.bServerOnly, four.bServerOnly );
	EXPECT_EQ( logical.nSolidType, four.nSolidType );
	EXPECT_EQ( logical.nSolidFlags, four.nSolidFlags );
}

//-----------------------------------------------------------------------------
// The ABI numbers themselves. C# picks these; a renumbering on either side turns
// every beam into a logic entity with no error anywhere, so they are pinned.
//-----------------------------------------------------------------------------
TEST( ScriptedKindParams, KindNumbersAreTheOnesCSharpSends )
{
	EXPECT_EQ( 0, (int)SOURCESHARP_SCRIPTED_LOGICAL );
	EXPECT_EQ( 1, (int)SOURCESHARP_SCRIPTED_ITEM );
	EXPECT_EQ( 2, (int)SOURCESHARP_SCRIPTED_BRUSH );
	EXPECT_EQ( 3, (int)SOURCESHARP_SCRIPTED_PROP );
	EXPECT_EQ( 5, (int)SOURCESHARP_SCRIPTED_PROJECTILE );
	EXPECT_EQ( 6, (int)SOURCESHARP_SCRIPTED_EFFECT );
	EXPECT_EQ( 7, (int)SOURCESHARP_SCRIPTED_PLAYER );
}

//-----------------------------------------------------------------------------
// Logical is the only server-only kind. Everything else exists to be drawn, so
// everything else needs an edict.
//-----------------------------------------------------------------------------
TEST( ScriptedKindParams, OnlyLogicalIsServerOnly )
{
	EXPECT_TRUE( SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_LOGICAL ).bServerOnly );

	EXPECT_FALSE( SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_ITEM ).bServerOnly );
	EXPECT_FALSE( SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_BRUSH ).bServerOnly );
	EXPECT_FALSE( SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_PROP ).bServerOnly );
	EXPECT_FALSE( SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_PROJECTILE ).bServerOnly );
	EXPECT_FALSE( SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_EFFECT ).bServerOnly );
	EXPECT_FALSE( SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_PLAYER ).bServerOnly );
}

//-----------------------------------------------------------------------------
// An item is the only kind that reports every tick of overlap. A pickup has to
// keep asking "can this player take it yet"; everything else fires on the edges,
// and a brush trigger reporting sixty-six times a second is a bug that looks
// like working code.
//-----------------------------------------------------------------------------
TEST( ScriptedKindParams, OnlyItemsTouchEveryTick )
{
	EXPECT_TRUE( SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_ITEM ).bTouchEveryTick );

	EXPECT_FALSE( SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_LOGICAL ).bTouchEveryTick );
	EXPECT_FALSE( SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_BRUSH ).bTouchEveryTick );
	EXPECT_FALSE( SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_PROP ).bTouchEveryTick );
	EXPECT_FALSE( SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_PROJECTILE ).bTouchEveryTick );
	EXPECT_FALSE( SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_EFFECT ).bTouchEveryTick );
	EXPECT_FALSE( SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_PLAYER ).bTouchEveryTick );
}

//-----------------------------------------------------------------------------
// The player is the only kind whose instances are the player subclass, and it
// collides as the box a player has always been. Pinned the same way the
// numbers are: a player registered as anything else walks through walls, and
// nothing reports it.
//-----------------------------------------------------------------------------
TEST( ScriptedKindParams, OnlyThePlayerIsThePlayer )
{
	SourceSharpScriptedKindParams_t p = SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_PLAYER );

	EXPECT_TRUE( p.bPlayer );
	EXPECT_EQ( SOURCESHARP_KIND_SOLID_BBOX, p.nSolidType );
	EXPECT_EQ( 0, p.nSolidFlags );

	EXPECT_FALSE( SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_LOGICAL ).bPlayer );
	EXPECT_FALSE( SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_ITEM ).bPlayer );
	EXPECT_FALSE( SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_BRUSH ).bPlayer );
	EXPECT_FALSE( SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_PROP ).bPlayer );
	EXPECT_FALSE( SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_PROJECTILE ).bPlayer );
	EXPECT_FALSE( SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_EFFECT ).bPlayer );

	// The fallback for an unknown kind must never be the player either — an
	// old native half handed a kind it has never heard of must fail SAFE.
	EXPECT_FALSE( SourceSharp_ScriptedKindParams( 999 ).bPlayer );
}

//-----------------------------------------------------------------------------
// The exact solidity each kind installs with. Restated here rather than derived,
// so a change to the table is a change to this file too and has to be meant.
//-----------------------------------------------------------------------------
TEST( ScriptedKindParams, ItemIsATriggerThatIsNotSolid )
{
	SourceSharpScriptedKindParams_t p = SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_ITEM );

	EXPECT_EQ( SOURCESHARP_KIND_SOLID_BBOX, p.nSolidType );
	EXPECT_TRUE( ( p.nSolidFlags & SOURCESHARP_KIND_FSOLID_TRIGGER ) != 0 );
	EXPECT_TRUE( ( p.nSolidFlags & SOURCESHARP_KIND_FSOLID_NOT_SOLID ) != 0 );
}

TEST( ScriptedKindParams, BrushCollidesAsItsSubmodelAndStartsSolid )
{
	SourceSharpScriptedKindParams_t p = SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_BRUSH );

	EXPECT_EQ( SOURCESHARP_KIND_SOLID_BSP, p.nSolidType );
	EXPECT_EQ( 0, p.nSolidFlags );
}

TEST( ScriptedKindParams, PropCollidesAsItsCompiledHull )
{
	SourceSharpScriptedKindParams_t p = SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_PROP );

	EXPECT_EQ( SOURCESHARP_KIND_SOLID_VPHYSICS, p.nSolidType );
	EXPECT_EQ( 0, p.nSolidFlags );
}

//-----------------------------------------------------------------------------
// A projectile is a box and nothing may stand on it. Both halves matter: the box
// is why a bolt hits what it was aimed at, and NOT_STANDABLE is why a rocket
// passing under a player's feet is not a lift.
//-----------------------------------------------------------------------------
TEST( ScriptedKindParams, ProjectileIsABoxNothingCanStandOn )
{
	SourceSharpScriptedKindParams_t p = SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_PROJECTILE );

	EXPECT_EQ( SOURCESHARP_KIND_SOLID_BBOX, p.nSolidType );
	EXPECT_TRUE( ( p.nSolidFlags & SOURCESHARP_KIND_FSOLID_NOT_STANDABLE ) != 0 );
	EXPECT_TRUE( ( p.nSolidFlags & SOURCESHARP_KIND_FSOLID_TRIGGER ) == 0 );
}

//-----------------------------------------------------------------------------
// An effect is drawn and nothing else. It differs from Logical in exactly one
// field — being networked — which is the whole reason the kind exists.
//-----------------------------------------------------------------------------
TEST( ScriptedKindParams, EffectIsLogicalPlusAnEdict )
{
	SourceSharpScriptedKindParams_t effect = SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_EFFECT );
	SourceSharpScriptedKindParams_t logical = SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_LOGICAL );

	EXPECT_NE( logical.bServerOnly, effect.bServerOnly );
	EXPECT_EQ( logical.nSolidType, effect.nSolidType );
	EXPECT_EQ( logical.nSolidFlags, effect.nSolidFlags );
	EXPECT_EQ( logical.bTouchEveryTick, effect.bTouchEveryTick );
}

//-----------------------------------------------------------------------------
// An effect spawns as stock's point entities do: SOLID_NONE and no solid flags.
// The few stock classes that add FSOLID_NOT_SOLID (point_spotlight, anything
// FollowEntity attaches) add it themselves.
//-----------------------------------------------------------------------------
TEST( ScriptedKindParams, EffectIsAPointEntityWithNoSolidFlags )
{
	SourceSharpScriptedKindParams_t effect = SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_EFFECT );

	EXPECT_EQ( (int)SOURCESHARP_KIND_SOLID_NONE, effect.nSolidType );
	EXPECT_EQ( 0, effect.nSolidFlags );
}

//-----------------------------------------------------------------------------
// Nothing is solid AND a trigger AND not-solid in a combination Source rejects.
// The one legal-looking contradiction is TRIGGER|NOT_SOLID, which is how Source
// spells "in the trigger list, out of the solid one" — every OTHER kind that
// sets NOT_SOLID must have nothing to collide with in the first place.
//-----------------------------------------------------------------------------
TEST( ScriptedKindParams, NotSolidOnlyAppearsWhereItMeansSomething )
{
	const int kKinds[] = {
		SOURCESHARP_SCRIPTED_LOGICAL, SOURCESHARP_SCRIPTED_ITEM,       SOURCESHARP_SCRIPTED_BRUSH,
		SOURCESHARP_SCRIPTED_PROP,    SOURCESHARP_SCRIPTED_PROJECTILE, SOURCESHARP_SCRIPTED_EFFECT,
		SOURCESHARP_SCRIPTED_PLAYER,
	};

	for ( size_t i = 0; i < sizeof( kKinds ) / sizeof( kKinds[0] ); i++ )
	{
		SourceSharpScriptedKindParams_t p = SourceSharp_ScriptedKindParams( kKinds[i] );

		if ( ( p.nSolidFlags & SOURCESHARP_KIND_FSOLID_NOT_SOLID ) == 0 )
			continue;

		const bool bTrigger = ( p.nSolidFlags & SOURCESHARP_KIND_FSOLID_TRIGGER ) != 0;
		const bool bNoShape = ( p.nSolidType == SOURCESHARP_KIND_SOLID_NONE );

		EXPECT_TRUE( bTrigger || bNoShape ) << "kind " << kKinds[i] << " is not solid and has a shape nobody can enter";
	}
}

//-----------------------------------------------------------------------------
// plan_entity.md step 2e -- LOGICAL IS THE ONLY KIND THAT MAKES NO SHELL AT ALL.
//
// §3.3: "A shell is created only for an entity that is networked, in the
// partition, or attached to a physics object." That is three conditions and
// LOGICAL is the one kind for which all three fail, which is why this reads as
// the same set as bServerOnly today -- and why it is a field of its own rather
// than a second reading of that one. A future kind that is server-only for some
// OTHER reason must not silently lose its shell.
//-----------------------------------------------------------------------------
TEST( ScriptedKindParams, OnlyLogicalIsShellLess )
{
	EXPECT_TRUE( SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_LOGICAL ).bShellLess );

	EXPECT_FALSE( SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_ITEM ).bShellLess );
	EXPECT_FALSE( SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_BRUSH ).bShellLess );
	EXPECT_FALSE( SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_PROP ).bShellLess );
	EXPECT_FALSE( SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_PROJECTILE ).bShellLess );
	EXPECT_FALSE( SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_EFFECT ).bShellLess );
	EXPECT_FALSE( SourceSharp_ScriptedKindParams( SOURCESHARP_SCRIPTED_PLAYER ).bShellLess );
}

//-----------------------------------------------------------------------------
// A kind the native half does not recognise falls back to LOGICAL, so it falls
// back to SHELL-LESS too -- and that is the direction to be wrong in. A managed
// build newer than the native one gets an entity that exists in the directory
// and answers its own keyvalues; the other way round it would get a shell no
// managed code knows about, in the engine's entity list, for ever.
//-----------------------------------------------------------------------------
TEST( ScriptedKindParams, AnUnknownKindIsShellLessToo )
{
	EXPECT_TRUE( SourceSharp_ScriptedKindParams( 4 ).bShellLess );
	EXPECT_TRUE( SourceSharp_ScriptedKindParams( 99 ).bShellLess );
	EXPECT_TRUE( SourceSharp_ScriptedKindParams( -1 ).bShellLess );
}

//-----------------------------------------------------------------------------
// EVERY int has an answer for this field as well. The struct is returned by
// value, so a case that forgot to assign it would hand back whatever the
// caller's stack held -- and "true" from uninitialised memory means a class
// that silently installs no factory and can never be created.
//-----------------------------------------------------------------------------
TEST( ScriptedKindParams, ShellLessIsAssignedForEveryInt )
{
	for ( int i = -8; i < 64; i++ )
	{
		// A pattern the answer is not, so an unassigned field is visible rather
		// than being zero by luck of the stack.
		SourceSharpScriptedKindParams_t p;
		memset( &p, 0xCD, sizeof( p ) );

		p = SourceSharp_ScriptedKindParams( i );

		const bool bLogical = ( i == SOURCESHARP_SCRIPTED_LOGICAL ) ||
			( i != SOURCESHARP_SCRIPTED_ITEM && i != SOURCESHARP_SCRIPTED_BRUSH && i != SOURCESHARP_SCRIPTED_PROP &&
			  i != SOURCESHARP_SCRIPTED_PROJECTILE && i != SOURCESHARP_SCRIPTED_EFFECT &&
			  i != SOURCESHARP_SCRIPTED_PLAYER );

		EXPECT_EQ( bLogical, p.bShellLess ) << "kind " << i;
	}
}

//-----------------------------------------------------------------------------
// The kind that makes no shell is exactly the kind that would have made a
// server-only one. Written as an IMPLICATION rather than as an equality,
// because the two are allowed to come apart the day a kind is server-only for
// a reason other than having nothing to be: what must never happen is a kind
// that is shell-less and NOT server-only, which would be a class the engine is
// told to network and that has nothing to network from.
//-----------------------------------------------------------------------------
TEST( ScriptedKindParams, ShellLessImpliesServerOnly )
{
	for ( int i = -8; i < 64; i++ )
	{
		SourceSharpScriptedKindParams_t p = SourceSharp_ScriptedKindParams( i );

		// Braced: gtest's EXPECT_TRUE expands to an if/else, so an unbraced
		// body makes the `else` ambiguous (-Wdangling-else).
		if ( p.bShellLess )
		{
			EXPECT_TRUE( p.bServerOnly ) << "kind " << i;
		}
	}
}

} // namespace
