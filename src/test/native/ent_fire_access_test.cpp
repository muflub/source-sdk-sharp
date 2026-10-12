//========= SourceSharp — who may ent_fire ===================================//
//
// The two access rules ent_fire had, written as pointer comparisons and both
// wrong under this mod:
//
//   (1) "there is a console client" was ToBasePlayer(...) != NULL, so a player
//       who is not a CBasePlayer read as nobody and the command returned in
//       silence — every ent_fire in the game, doing nothing, saying nothing.
//
//   (2) "the issuer is the listen server host" was pPlayer != pHostPlayer, and
//       when NEITHER is a CBasePlayer that is NULL != NULL, which is false, so
//       the check passes. An access rule that passes by accident is the one
//       kind of bug a green test suite is least likely to catch, because the
//       behaviour it produces is the behaviour everybody wanted anyway.
//
// Both are now arithmetic over slot numbers, which is what they always meant.
// This file is the whole of that arithmetic, and every case here is one a live
// game cannot easily be put into — a dedicated server, a full listen server, a
// second player firing — which is exactly why it is worth having off the game.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "../../game/server/ent_fire_access.h"

namespace
{

//-----------------------------------------------------------------------------
// Which slot the command is attributed to.
//-----------------------------------------------------------------------------

TEST( EntFireAccess, AClientConsoleNamesItsOwnSlot )
{
	EXPECT_EQ( 1, EntFire_IssuerSlot( 1, false ) );
	EXPECT_EQ( 7, EntFire_IssuerSlot( 7, false ) );
	EXPECT_EQ( 7, EntFire_IssuerSlot( 7, true ) );
}

TEST( EntFireAccess, AListenServerConsoleIsTheHost )
{
	// engine->ServerCommand names no client, and on a listen server that
	// console is the host's own. This is the case every tool drives the game
	// through, including the development API.
	EXPECT_EQ( 1, EntFire_IssuerSlot( ENT_FIRE_NO_ISSUER, false ) );
}

TEST( EntFireAccess, ADedicatedServerConsoleIsNobody )
{
	// There is no host there, and the point_servercommand escalation ent_fire
	// guards against is exactly the dedicated-server case.
	EXPECT_EQ( ENT_FIRE_NO_ISSUER, EntFire_IssuerSlot( ENT_FIRE_NO_ISSUER, true ) );
}

//-----------------------------------------------------------------------------
// Whether that slot may fire.
//-----------------------------------------------------------------------------

TEST( EntFireAccess, NobodyMayNotFire )
{
	EXPECT_FALSE( EntFire_SlotMayFire( ENT_FIRE_NO_ISSUER, false, 1, true ) );
	EXPECT_FALSE( EntFire_SlotMayFire( ENT_FIRE_NO_ISSUER, true, 16, true ) );
	EXPECT_FALSE( EntFire_SlotMayFire( -1, false, 1, true ) );
}

TEST( EntFireAccess, SinglePlayerLetsWhoeverIsHereFire )
{
	EXPECT_TRUE( EntFire_SlotMayFire( 1, false, 1, false ) );
}

TEST( EntFireAccess, OnlyTheHostFiresOnAListenServerWithRoomForCompany )
{
	EXPECT_TRUE ( EntFire_SlotMayFire( 1, false, 16, false ) );
	EXPECT_FALSE( EntFire_SlotMayFire( 2, false, 16, false ) );
	EXPECT_FALSE( EntFire_SlotMayFire( 16, false, 16, false ) );
}

TEST( EntFireAccess, ADedicatedServerAsksAboutAutokickAndNothingElse )
{
	// Not the host rule: slot 1 on a dedicated server is an ordinary player.
	EXPECT_TRUE ( EntFire_SlotMayFire( 1, true, 16, true ) );
	EXPECT_FALSE( EntFire_SlotMayFire( 1, true, 16, false ) );
	EXPECT_TRUE ( EntFire_SlotMayFire( 9, true, 16, true ) );
}

TEST( EntFireAccess, AnUnanswerableAutokickQuestionIsANo )
{
	// A caller with no CBasePlayer to ask passes false. The direction matters:
	// the old code could not ask at all and dereferenced whatever it had.
	EXPECT_FALSE( EntFire_SlotMayFire( 1, true, 16, false ) );
}

//-----------------------------------------------------------------------------
// The two together, on the shapes this mod actually runs in.
//-----------------------------------------------------------------------------

TEST( EntFireAccess, TheDevelopmentApiConsoleOnAListenServerMayFire )
{
	const int nSlot = EntFire_IssuerSlot( ENT_FIRE_NO_ISSUER, false );

	EXPECT_EQ( 1, nSlot );
	EXPECT_TRUE( EntFire_SlotMayFire( nSlot, false, 1, false ) );
	EXPECT_TRUE( EntFire_SlotMayFire( nSlot, false, 16, false ) );
}

TEST( EntFireAccess, TheDedicatedServerConsoleStillMayNot )
{
	const int nSlot = EntFire_IssuerSlot( ENT_FIRE_NO_ISSUER, true );

	EXPECT_EQ( ENT_FIRE_NO_ISSUER, nSlot );
	EXPECT_FALSE( EntFire_SlotMayFire( nSlot, true, 16, true ) );
}

// The comparison the old rule made, spelled out. Two players who are not
// CBasePlayer both resolve to NULL, so `pPlayer != pHostPlayer` is FALSE and a
// player in slot 9 walks through a check meant to stop them. The slot rule says
// no, which is the whole reason this file exists.
TEST( EntFireAccess, TheNullEqualsNullHoleIsClosed )
{
	const void *pIssuerAsBasePlayer = NULL;			// slot 9, not a CBasePlayer
	const void *pHostAsBasePlayer   = NULL;			// slot 1, not a CBasePlayer

	EXPECT_FALSE( pIssuerAsBasePlayer != pHostAsBasePlayer );	// the old check PASSED
	EXPECT_FALSE( EntFire_SlotMayFire( 9, false, 16, false ) );	// the new one does not
}

}  // namespace
