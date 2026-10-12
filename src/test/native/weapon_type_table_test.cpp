//========= SourceSharp — the weapon-type table, driven for real =============//
//
// weapon_type_table.h is what SourceSharp_RegisterWeaponType writes and what the
// attack path reads mid-shot (shell_shared.cpp). It was a vector of 64-byte
// name fields walked with StrICmp; it is a case-blind map now. These pin what
// the callers rely on: re-registration updates in place (and says so), a
// lookup ignores case, an unknown or empty name answers nothing, and a name of
// any length is found -- the 64-byte field cut a long name short and then
// never matched it.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "weapon_type_table.h"

#include <string>

namespace
{

const WeaponStats_t kPistol  = { 0.25f, 1.5f, 1 };
const WeaponStats_t kShotgun = { 0.9f, 8.0f, 7 };

TEST( WeaponTypeTable, ARegisteredClassIsFoundWithItsNumbers )
{
	CWeaponTypeTable table;
	EXPECT_TRUE( table.Register( "weapon_pistol", kPistol ) );

	const WeaponStats_t *p = table.Find( "weapon_pistol" );
	ASSERT_TRUE( p != NULL );
	EXPECT_FLOAT_EQ( 0.25f, p->flFireRate );
	EXPECT_FLOAT_EQ( 1.5f, p->flSpreadDegrees );
	EXPECT_EQ( 1, p->nPellets );
}

TEST( WeaponTypeTable, ReRegistrationUpdatesInPlaceAndIsNotNew )
{
	CWeaponTypeTable table;
	ASSERT_TRUE( table.Register( "weapon_shotgun", kPistol ) );
	EXPECT_FALSE( table.Register( "weapon_shotgun", kShotgun ) ) << "a second registration is not a new class";

	EXPECT_EQ( 1, table.Count() );
	const WeaponStats_t *p = table.Find( "weapon_shotgun" );
	ASSERT_TRUE( p != NULL );
	EXPECT_EQ( 7, p->nPellets ) << "the second registration's numbers win";
	EXPECT_FLOAT_EQ( 8.0f, p->flSpreadDegrees );
}

TEST( WeaponTypeTable, LookupAndReRegistrationIgnoreCase )
{
	CWeaponTypeTable table;
	ASSERT_TRUE( table.Register( "Weapon_Crowbar", kPistol ) );

	EXPECT_TRUE( table.Find( "weapon_crowbar" ) != NULL );
	EXPECT_TRUE( table.Find( "WEAPON_CROWBAR" ) != NULL );
	EXPECT_FALSE( table.Register( "weapon_CROWBAR", kShotgun ) ) << "another case is the same class";
	EXPECT_EQ( 1, table.Count() );
	EXPECT_EQ( 7, table.Find( "Weapon_Crowbar" )->nPellets );
}

TEST( WeaponTypeTable, AnUnknownOrEmptyNameFindsNothing )
{
	CWeaponTypeTable table;
	ASSERT_TRUE( table.Register( "weapon_pistol", kPistol ) );

	EXPECT_TRUE( table.Find( "weapon_smg1" ) == NULL );
	EXPECT_TRUE( table.Find( "" ) == NULL );
	EXPECT_TRUE( table.Find( NULL ) == NULL );
	EXPECT_TRUE( table.Find( "weapon_pisto" ) == NULL ) << "a prefix is not the name";
}

TEST( WeaponTypeTable, ANameOfAnyLengthIsFound )
{
	// 100 characters: past the 63 the old 64-byte field kept.
	const std::string strLong = "weapon_" + std::string( 93, 'x' );
	ASSERT_EQ( 100u, strLong.size() );

	CWeaponTypeTable table;
	ASSERT_TRUE( table.Register( strLong.c_str(), kShotgun ) );

	const WeaponStats_t *p = table.Find( strLong.c_str() );
	ASSERT_TRUE( p != NULL ) << "a long name was stored cut short and never matched";
	EXPECT_EQ( 7, p->nPellets );
	EXPECT_TRUE( table.Find( strLong.substr( 0, 63 ).c_str() ) == NULL ) << "and its first 63 characters are another name";
}

TEST( WeaponTypeTable, AFoundEntryStaysPutAcrossLaterRegistrations )
{
	CWeaponTypeTable table;
	ASSERT_TRUE( table.Register( "weapon_pistol", kPistol ) );
	const WeaponStats_t *pFirst = table.Find( "weapon_pistol" );

	for ( int i = 0; i < 2000; i++ )
		table.Register( ( "weapon_" + std::to_string( i ) ).c_str(), kShotgun );

	EXPECT_EQ( pFirst, table.Find( "weapon_pistol" ) );
	EXPECT_EQ( 1, pFirst->nPellets );
}

} // namespace
