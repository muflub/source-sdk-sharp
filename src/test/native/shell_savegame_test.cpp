//====== SourceSharp — the server's load-game player gate ====================//
//
// sourcesharp/game/server/shell_savegame.h is what is left of the native save
// code: ShellSave_LoadGameNeedsPlayer, which shell_gameclients.cpp's
// ClientActive asks on a savegame load. The save field codec the header
// carried, and the framing facts this file held for it, are managed now:
// SaveBlockCodec (src/shared/interop), held to stock's CSave::WriteFields /
// CRestore::ReadFields byte for byte by SaveCodecOracleTests, with its own
// facts in SaveBlockCodecTests and SaveGameSessionTests.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "../game/server/shell_savegame.h"

//-----------------------------------------------------------------------------
// THE GATE THAT DECIDES WHETHER A LOAD HAS A PLAYER IN IT.
//
// ShellSave_LoadGameNeedsPlayer's header says why: on a load-game the engine
// does not call ClientPutInServer, so nothing has created the player and
// ClientActive has to. Stock does not need
// this because ITS Restore builds the player out of the save's entity table
// and this port writes no entity table.
//
// FOUR FACTS FOR FOUR INPUTS, AND THE POSITIVE ONE FIRST. A gate is only worth
// testing if the input would otherwise be handled: the "true" case is what
// makes the three "false" cases mean something, because a predicate that
// answered false to everything would satisfy them all.
//
// The creation itself is NOT here and cannot be -- it needs the entity
// factory, the edict array and the managed runtime, which is why
// shell_gameclients.cpp has no gtest of its own (shell_clientsteps_test.cpp
// says the same about the file next to it). What is here is the half with two
// silent wrong answers.
//-----------------------------------------------------------------------------
TEST(LoadGamePlayer, ALoadWithNoPlayerBoundNeedsOne)
{
	EXPECT_TRUE(ShellSave_LoadGameNeedsPlayer(true, false));
}

TEST(LoadGamePlayer, ALoadThatALREADYHasAPlayerIsLeftAlone)
{
	// The engine would have to have called ClientPutInServer after all, or a
	// previous pass created one. Building a second player onto a live client's
	// edict is the worse of the two wrong answers.
	EXPECT_FALSE(ShellSave_LoadGameNeedsPlayer(true, true));
}

TEST(LoadGamePlayer, AFreshConnectIsNotThisArmsBusiness)
{
	// bLoadGame false: ClientPutInServer HAS been called and is where the
	// player comes from. Answering true here would double-create on every
	// ordinary join, which is why the flag is the first term and not a
	// belt-and-braces second one.
	EXPECT_FALSE(ShellSave_LoadGameNeedsPlayer(false, false));
}

TEST(LoadGamePlayer, AFreshConnectWithItsPlayerAlreadyStandingIsAlsoNo)
{
	EXPECT_FALSE(ShellSave_LoadGameNeedsPlayer(false, true));
}
