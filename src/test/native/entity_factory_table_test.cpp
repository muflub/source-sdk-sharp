//========= SourceSharp — the port's entity factory table, exercised ==========//
//
// `src/sourcesharp/game/server/entity_factory_table.h` is the name half of the
// dictionary `EntityFactoryDictionary()` hands back -- the replacement for
// `CEntityFactoryDictionary` (`src/game/server/util.cpp:111`), which is a class
// local to a stock translation unit this port is trying to stop compiling.
//
// THESE ARE BEHAVIOURAL FACTS, NOT SOURCE-TEXT FACTS, and that is the reason
// the table is a header. Every other test that could reach this subsystem reads
// the .cpp as a string, because `shell_standalone_class.cpp` includes `cbase.h`
// and drags the server in behind it. A scanner cannot tell that `Find` is
// case-insensitive, that a second install of the same pointer is a no-op, or
// that `CanonicalName` no longer indexes an element vector at 0xFFFF. Each of
// those is asserted below by calling it.
//
// The factory type is a local fake, which is the whole point of the template:
// `IEntityFactory` lives in `util.h` and would have brought the server back.
//
//=============================================================================//

#include <gtest/gtest.h>
#include <cstdio>
#include <string>
#include <vector>

#include "entity_factory_table.h"

namespace
{

//-----------------------------------------------------------------------------
// A stand-in for IEntityFactory: the table stores a pointer and never calls it,
// so the only thing the type has to provide is identity.
//-----------------------------------------------------------------------------
struct FakeFactory
{
	explicit FakeFactory( const char *pName )
		: name( pName )
	{
	}

	std::string name;
};

typedef CEntityFactoryTable<FakeFactory> Table;

} // namespace

//=============================================================================//
// 1. INSTALL AND FIND -- the two the port cannot work without.
//=============================================================================//

TEST( EntityFactoryTable, AnInstalledFactoryIsFoundUnderItsName )
{
	Table table;
	FakeFactory prop( "prop" );

	EXPECT_EQ( kEntityFactoryInstalled, table.Install( &prop, "ss_prop" ) );
	EXPECT_EQ( &prop, table.Find( "ss_prop" ) );
}

TEST( EntityFactoryTable, AnUninstalledNameFindsNothing )
{
	Table table;
	FakeFactory prop( "prop" );
	table.Install( &prop, "ss_prop" );

	// The three classnames dm_lockdown asks for and this build has no factory
	// for -- see .claude/entity-port/status/p4-flip.md. NULL is what makes the
	// refusal printable instead of a jump through a garbage pointer.
	EXPECT_TRUE( table.Find( "func_useableladder" ) == NULL );
	EXPECT_TRUE( table.Find( "info_ladder_dismount" ) == NULL );
	EXPECT_TRUE( table.Find( "info_node" ) == NULL );
}

TEST( EntityFactoryTable, AnEmptyTableFindsNothingRatherThanFaulting )
{
	Table table;
	EXPECT_TRUE( table.Find( "worldspawn" ) == NULL );
	EXPECT_EQ( 0, table.Count() );
}

//=============================================================================//
// 2. THE CASE RULE.
//
// `CEntityFactoryDictionary` constructs `m_Factories( true, 0, 128 )`, and
// `true` is 1, which `utldict.h:27` names k_eDictCompareTypeCaseInsensitive.
// The shim's own parallel walk (`FindStandaloneClass`) uses Q_stricmp and so
// agrees with it. A replacement that quietly became case-SENSITIVE would pass
// every source-text check in the tree and would stop a map spelling
// `Info_Player_Deathmatch` from finding its factory.
//=============================================================================//

TEST( EntityFactoryTable, FindIsCaseInsensitive )
{
	Table table;
	FakeFactory player( "player" );
	table.Install( &player, "info_player_deathmatch" );

	EXPECT_EQ( &player, table.Find( "INFO_PLAYER_DEATHMATCH" ) );
	EXPECT_EQ( &player, table.Find( "Info_Player_Deathmatch" ) );
}

TEST( EntityFactoryTable, ANameDifferingOnlyByCaseIsTheSameName )
{
	Table table;
	FakeFactory first( "first" );
	FakeFactory second( "second" );

	EXPECT_EQ( kEntityFactoryInstalled, table.Install( &first, "ss_thing" ) );
	EXPECT_EQ( kEntityFactoryNameTaken, table.Install( &second, "SS_THING" ) );
	EXPECT_EQ( 1, table.Count() );
}

//=============================================================================//
// 3. RE-INSTALL, WHICH IS WHAT A cs_sv_reload DOES.
//
// The stock dictionary answered this with `Assert( FindFactory(..) == NULL )`
// and then inserted anyway, so a release build stacked duplicate keys silently
// -- 284 of them per reload, measured. See entity_factory_table.h.
//=============================================================================//

TEST( EntityFactoryTable, ReinstallingTheSamePointerUnderTheSameNameIsANoOp )
{
	Table table;
	FakeFactory prop( "prop" );

	EXPECT_EQ( kEntityFactoryInstalled, table.Install( &prop, "ss_prop" ) );
	EXPECT_EQ( kEntityFactoryAlreadyInstalled, table.Install( &prop, "ss_prop" ) );
	EXPECT_EQ( kEntityFactoryAlreadyInstalled, table.Install( &prop, "ss_prop" ) );

	EXPECT_EQ( 1, table.Count() );
	EXPECT_EQ( &prop, table.Find( "ss_prop" ) );
}

TEST( EntityFactoryTable, ADifferentPointerUnderATakenNameKeepsTheFirst )
{
	Table table;
	FakeFactory first( "first" );
	FakeFactory second( "second" );

	EXPECT_EQ( kEntityFactoryInstalled, table.Install( &first, "ss_prop" ) );
	EXPECT_EQ( kEntityFactoryNameTaken, table.Install( &second, "ss_prop" ) );

	// THE FIRST, not the last. The engine may already hold instances made
	// through it, and the map that names the class must keep reaching the same
	// object it reached a moment ago.
	EXPECT_EQ( &first, table.Find( "ss_prop" ) );
	EXPECT_EQ( 1, table.Count() );
}

TEST( EntityFactoryTable, AnEmptyOrNullNameIsRefusedRatherThanInserted )
{
	Table table;
	FakeFactory prop( "prop" );

	EXPECT_EQ( kEntityFactoryNoName, table.Install( &prop, "" ) );
	EXPECT_EQ( kEntityFactoryNoName, table.Install( &prop, NULL ) );
	EXPECT_EQ( 0, table.Count() );
}

//=============================================================================//
// 4. THE CANONICAL NAME, AND THE STOCK READ IT REPLACES.
//
// `CEntityFactoryDictionary::GetCannonicalName` is
//
//     return m_Factories.GetElementName( m_Factories.Find( pClassName ) );
//
// with the Find untested, so an unknown classname reaches GetElementName with
// InvalidIndex() -- 0xFFFF -- and indexes the element vector there. Its three
// live callers in this build are all `physics_main.cpp` Warning() calls about
// an entity that is already misbehaving.
//=============================================================================//

TEST( EntityFactoryTable, TheCanonicalNameIsTheSpellingItWasInstalledUnder )
{
	Table table;
	FakeFactory prop( "prop" );
	table.Install( &prop, "ss_Prop" );

	ASSERT_TRUE( table.CanonicalName( "SS_PROP" ) != NULL );
	EXPECT_STREQ( "ss_Prop", table.CanonicalName( "SS_PROP" ) );
}

TEST( EntityFactoryTable, TheCanonicalNameOfAnUnknownClassIsNullRatherThanARead )
{
	Table table;
	FakeFactory prop( "prop" );
	table.Install( &prop, "ss_prop" );

	EXPECT_TRUE( table.CanonicalName( "func_useableladder" ) == NULL );
	EXPECT_TRUE( table.CanonicalName( "" ) == NULL );
	EXPECT_TRUE( table.CanonicalName( NULL ) == NULL );
}

//=============================================================================//
// 5. ENUMERATION -- what the console command prints and what the managed side
// compares its own declarations against.
//=============================================================================//

TEST( EntityFactoryTable, EnumerationIsDenseAndInInstallOrder )
{
	Table table;
	FakeFactory a( "a" ), b( "b" ), c( "c" );

	// Deliberately NOT alphabetical, so a table that read the dictionary's own
	// red-black order instead of the install order fails here.
	table.Install( &c, "ss_zulu" );
	table.Install( &a, "ss_alpha" );
	table.Install( &b, "ss_mike" );

	ASSERT_EQ( 3, table.Count() );
	EXPECT_STREQ( "ss_zulu", table.NameAt( 0 ) );
	EXPECT_STREQ( "ss_alpha", table.NameAt( 1 ) );
	EXPECT_STREQ( "ss_mike", table.NameAt( 2 ) );

	EXPECT_EQ( &c, table.FactoryAt( 0 ) );
	EXPECT_EQ( &a, table.FactoryAt( 1 ) );
	EXPECT_EQ( &b, table.FactoryAt( 2 ) );
}

TEST( EntityFactoryTable, EnumerationOutOfRangeAnswersNullRatherThanReading )
{
	Table table;
	FakeFactory a( "a" );
	table.Install( &a, "ss_alpha" );

	EXPECT_TRUE( table.NameAt( -1 ) == NULL );
	EXPECT_TRUE( table.NameAt( 1 ) == NULL );
	EXPECT_TRUE( table.FactoryAt( -1 ) == NULL );
	EXPECT_TRUE( table.FactoryAt( 1 ) == NULL );
}

TEST( EntityFactoryTable, ARefusedInstallDoesNotGrowTheEnumeration )
{
	Table table;
	FakeFactory a( "a" ), b( "b" );

	table.Install( &a, "ss_alpha" );
	table.Install( &a, "ss_alpha" ); // already installed
	table.Install( &b, "ss_alpha" ); // name taken
	table.Install( &b, "" ); // no name

	EXPECT_EQ( 1, table.Count() );
	EXPECT_STREQ( "ss_alpha", table.NameAt( 0 ) );
	EXPECT_TRUE( table.NameAt( 1 ) == NULL );
}

TEST( EntityFactoryTable, EnumerationSurvivesTheDictionaryRebalancing )
{
	// THE ONE THING THE INSTALL-ORDER VECTOR DEPENDS ON, asserted rather than
	// read off utlrbtree.h. m_Order holds the index CUtlDict::Insert returned,
	// so every one of those indices must still name the same element after the
	// tree has rotated around it many times.
	//
	// It does -- CUtlRBTree::NewNode allocates a slot and RotateLeft/Right
	// relink rather than move (utlrbtree.h:739, :801) -- but "I read the header"
	// is not a check, and 156 factories is the live count on this build, so the
	// number here is chosen to be past it.
	Table table;
	std::vector<FakeFactory *> made;
	std::vector<std::string> names;

	for ( int i = 0; i < 200; i++ )
	{
		char name[32];
		// Deliberately NOT ascending: an ascending key order is the one shape a
		// red-black tree rotates least on.
		snprintf( name, sizeof( name ), "ss_%03d", ( i * 97 ) % 200 );
		names.push_back( name );
		made.push_back( new FakeFactory( name ) );
		ASSERT_EQ( kEntityFactoryInstalled, table.Install( made.back(), name ) ) << "at " << i << " (" << name << ")";
	}

	ASSERT_EQ( 200, table.Count() );

	for ( int i = 0; i < 200; i++ )
	{
		EXPECT_STREQ( names[i].c_str(), table.NameAt( i ) ) << "at " << i;
		EXPECT_EQ( made[i], table.FactoryAt( i ) ) << "at " << i;
		EXPECT_EQ( made[i], table.Find( names[i].c_str() ) ) << "at " << i;
	}

	for ( size_t i = 0; i < made.size(); i++ )
		delete made[i];
}

//=============================================================================//
// 6. THE PORT'S OWN REGISTRATION SHAPE, END TO END.
//
// The three kinds of factory this build actually installs, in the order the
// process installs them: `SOURCESHARP_LINK_ENTITY` and `CWorldFactory` from
// static initialisers, then one per `[ScriptedEntity]` when C# comes up, then
// the same set again on a cs_sv_reload.
//=============================================================================//

TEST( EntityFactoryTable, AReloadReinstallsEveryClassAndTheTableDoesNotGrow )
{
	Table table;
	FakeFactory world( "world" );
	FakeFactory prop( "prop" ), player( "player" ), trigger( "trigger" );

	table.Install( &world, "worldspawn" );
	table.Install( &prop, "ss_prop" );
	table.Install( &player, "ss_player" );
	table.Install( &trigger, "ss_trigger" );

	ASSERT_EQ( 4, table.Count() );

	// cs_sv_reload: managed declares every class again, against factory objects
	// the shim re-describes rather than re-creates.
	for ( int pass = 0; pass < 3; pass++ )
	{
		EXPECT_EQ( kEntityFactoryAlreadyInstalled, table.Install( &prop, "ss_prop" ) );
		EXPECT_EQ( kEntityFactoryAlreadyInstalled, table.Install( &player, "ss_player" ) );
		EXPECT_EQ( kEntityFactoryAlreadyInstalled, table.Install( &trigger, "ss_trigger" ) );
	}

	EXPECT_EQ( 4, table.Count() );
	EXPECT_EQ( &player, table.Find( "ss_player" ) );
	EXPECT_EQ( &world, table.Find( "worldspawn" ) );
}
