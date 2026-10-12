//========= SourceSharp — who owns a console variable's name ================//
//
// convar_ownership.h: managed code never aliases a convar the engine defines.
// A variable carrying neither game-DLL flag is the engine's; one carrying
// either is a game DLL's, and a client twin of the server's links as stock's
// replicated pairs do.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "convar_ownership.h"

TEST( ConVarOwnership, NoDllFlagIsTheEngines )
{
	EXPECT_TRUE( SourceSharp_ConVarOwnedByEngine( 0 ) );
}

TEST( ConVarOwnership, OtherFlagsDoNotMakeItOurs )
{
	EXPECT_TRUE( SourceSharp_ConVarOwnedByEngine( FCVAR_REPLICATED | FCVAR_ARCHIVE | FCVAR_CHEAT | FCVAR_NOTIFY ) );
}

TEST( ConVarOwnership, TheServersIsNotTheEngines )
{
	EXPECT_FALSE( SourceSharp_ConVarOwnedByEngine( FCVAR_GAMEDLL ) );
	EXPECT_FALSE( SourceSharp_ConVarOwnedByEngine( FCVAR_GAMEDLL | FCVAR_REPLICATED ) );
}

TEST( ConVarOwnership, TheClientsIsNotTheEngines )
{
	EXPECT_FALSE( SourceSharp_ConVarOwnedByEngine( FCVAR_CLIENTDLL ) );
}

TEST( ConVarOwnership, TheRefusalIsBelowZeroAndDistinctFromMinusOne )
{
	EXPECT_EQ( -2, SOURCESHARP_CONVAR_ENGINE_OWNED );
}
