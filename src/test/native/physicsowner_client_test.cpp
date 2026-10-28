//-----------------------------------------------------------------------------
// THE CLIENT ARM OF physicsowner.h DECODES A MANAGED OWNER'S GAME DATA
// (plan_client_novalve C6, cluster 9; lane c6-input).
//
// A client ragdoll owned by a managed entity carries the server's game-data
// word: the packed handle plus one, top bit set (physics_gamedata.h). The
// client's physics.cpp asks PhysicsOwnerOf( obj ).pBase at every callback and,
// when it is not NULL, CALLS through it (VPhysicsUpdate, GetCollisionGroup).
// So the client arm must read a tagged word as "no SDK owner", as the server's
// arm does, and never cast it to an IGameEntity *: a cast word is an address
// with the top bit set, and the first virtual call through it faults.
//
// The other arm is held too: a word without the tag is still the SDK pointer
// it always was, so nothing that was an SDK owner stops being one.
//
// Compiled with CLIENT_DLL against a stub client_entity.h (the client arm
// includes it and names nothing in it).
//-----------------------------------------------------------------------------

#include "gtest/gtest.h"

#include "basehandle.h"
#include "ihandleentity.h"

#include "../game/shared/physicsowner.h"

namespace
{

TEST( PhysicsOwnerClient, ThisIsTheClientArm )
{
#if defined( CLIENT_DLL )
	SUCCEED();
#else
	FAIL() << "physicsowner_client_test must be built with CLIENT_DLL, or it tests the server's arm";
#endif
}

TEST( PhysicsOwnerClient, AManagedOwnersGameDataIsNoSdkOwner )
{
	const CBaseHandle handle( 37, 5 );
	void *pGameData = PhysicsGameDataFromHandle( handle );

	// The stimulus is the tagged word, or this fact proves nothing.
	ASSERT_TRUE( PhysicsGameDataIsHandle( pGameData ) );

	const PhysicsOwner_t owner = PhysicsOwnerOfGameData( pGameData, NULL );
	EXPECT_EQ( (void *)NULL, (void *)owner.pBase )
		<< "the client cast a managed owner's handle word to an IGameEntity *";
}

TEST( PhysicsOwnerClient, AnSdkPointerIsStillItsOwner )
{
	static int s_Entity;
	void *pGameData = &s_Entity;

	ASSERT_FALSE( PhysicsGameDataIsHandle( pGameData ) );

	const PhysicsOwner_t owner = PhysicsOwnerOfGameData( pGameData, NULL );
	EXPECT_EQ( pGameData, (void *)owner.pBase );
}

TEST( PhysicsOwnerClient, NoGameDataIsNoOwner )
{
	const PhysicsOwner_t owner = PhysicsOwnerOfGameData( NULL, NULL );
	EXPECT_EQ( (void *)NULL, (void *)owner.pBase );
}

} // namespace
