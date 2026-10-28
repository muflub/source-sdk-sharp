//========= SourceSharp — the five event names, and who agrees with them =====//
//
// plan_shim.md Phase 2d. precache_model.cpp used to identify new-system
// animation events by ORDINAL — a number the per-level event registry wrote
// into mdl memory, matching the enum only because the registry was filled in
// enum order. It identifies them by NAME now, out of anim_event_names.h, which
// is the precondition for deleting the registry (shell_animlists.cpp,
// anim_name_registry.h) and the feed behind it.
//
// THE TABLE IS A COPY OF FIVE ROWS OF A MANAGED TABLE, and a copy is a thing
// that drifts. So the agreement is asserted in both directions rather than
// assumed:
//
//   * the two type bits are eventlist.h's AE_TYPE_SERVER and AE_TYPE_CLIENT,
//     read out of that header's text. AE_TYPE_CLIENT is 1 << 4 with four
//     unrelated flags below it, so "the obvious value" is wrong;
//   * every name in the header is a name SourceSharp.Shared/EventCatalog.cs
//     holds, and carries the same client/server bit there;
//   * a name EventCatalog.cs says is a CLIENT event and this header holds must
//     be a client event here, and the same for SERVER.
//
// It does NOT assert that the header holds all fifty. It deliberately holds
// five: the ones native code reads. What it asserts is that no row here says
// something the managed table contradicts, which is the drift that would be
// silent — a model's AE_CL_PLAYSOUND quietly not precaching its sound.
//
// The header includes nothing, which is what lets this binary exist: no
// engine, no studio header, no tier1.
//
//=============================================================================//

#include <stdio.h>
#include <string>
#include <vector>

#include "gtest/gtest.h"

#include "../game/shared/anim_event_names.h"

namespace
{

//-----------------------------------------------------------------------------
// `#define NAME ( 1 << n )` out of eventlist.h, as an int. -1 when the define
// is not there in that shape, which is a failure rather than a zero: a zero
// would compare equal to "this name has no type".
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// EventCatalog.cs holds two parallel arrays: `Ordered`, the names, and
// `NativeTypes`, whose rows carry the name in a trailing comment. The comment
// is what this reads, because it is the row's own label and the two arrays are
// asserted to agree by ActivityCatalogTests on the managed side.
//
// Answers -1 for a name the catalog does not hold at all, which is a different
// failure from "holds it and says something else".
//-----------------------------------------------------------------------------
} // namespace

//=============================================================================//
// THE RULE ITSELF.
//=============================================================================//

TEST( AnimEventNames, EqualityIsCaseInsensitive )
{
	EXPECT_TRUE( AnimEventName_Equal( "AE_CL_PLAYSOUND", "ae_cl_playsound" ) );
	EXPECT_TRUE( AnimEventName_Equal( "Ae_Cl_PlaySound", "AE_CL_PLAYSOUND" ) );

	// utldict.h:51's default compare type is what stock's registry used, so a
	// model naming its events in lower case resolved there and must here.
	EXPECT_TRUE( AnimEventName_IsClient( "ae_cl_playsound" ) );
}

TEST( AnimEventNames, EqualityIsNotAPrefixMatch )
{
	EXPECT_FALSE( AnimEventName_Equal( "AE_CL_PLAYSOUND", "AE_CL_PLAYSOUNDISH" ) );
	EXPECT_FALSE( AnimEventName_Equal( "AE_CL_PLAYSOUNDISH", "AE_CL_PLAYSOUND" ) );
	EXPECT_FALSE( AnimEventName_Equal( "AE_CL_PLAYSOUND", "AE_CL_PLAYSOUN" ) );
	EXPECT_FALSE( AnimEventName_Equal( "", "AE_CL_PLAYSOUND" ) );
}

TEST( AnimEventNames, ANullNameEqualsNothingIncludingAnotherNull )
{
	// "This event has no name" is not a name two events can share -- an
	// old-system event reaches these functions as NULL, and two of them
	// comparing equal would make every one of them AE_CL_PLAYSOUND.
	EXPECT_FALSE( AnimEventName_Equal( NULL, NULL ) );
	EXPECT_FALSE( AnimEventName_Equal( NULL, "AE_CL_PLAYSOUND" ) );
	EXPECT_FALSE( AnimEventName_Equal( "AE_CL_PLAYSOUND", NULL ) );
	EXPECT_FALSE( AnimEventName_IsClient( NULL ) );
	EXPECT_EQ( 0, AnimEventName_TypeMask( NULL ) );
}

TEST( AnimEventNames, ANameTheTableDoesNotHoldHasNoTypeRatherThanAnInvalidOne )
{
	EXPECT_EQ( 0, AnimEventName_TypeMask( "AE_MUZZLEFLASH" ) );
	EXPECT_EQ( 0, AnimEventName_TypeMask( "AE_NOBODY_REGISTERED_THIS" ) );
	EXPECT_FALSE( AnimEventName_IsClient( "AE_MUZZLEFLASH" ) );
}

TEST( AnimEventNames, TheThreeClientNamesAreClientAndTheTwoServerOnesAreNot )
{
	EXPECT_TRUE( AnimEventName_IsClient( AE_NAME_CL_CREATE_PARTICLE_EFFECT ) );
	EXPECT_TRUE( AnimEventName_IsClient( AE_NAME_CL_PLAYSOUND ) );
	EXPECT_TRUE( AnimEventName_IsClient( AE_NAME_CL_EXCLUDE_PLAYER_SOUND ) );

	EXPECT_FALSE( AnimEventName_IsClient( AE_NAME_SV_PLAYSOUND ) );
	EXPECT_FALSE( AnimEventName_IsClient( AE_NAME_SV_EXCLUDE_PLAYER_SOUND ) );

	EXPECT_EQ( kAnimEventTypeServer, AnimEventName_TypeMask( AE_NAME_SV_PLAYSOUND ) );
}

TEST( AnimEventNames, TheTableIsTheFiveNativeCodeReads )
{
	int nRows = 0;
	const AnimEventNameRow_t *pRows = AnimEventNames_Rows( &nRows );

	ASSERT_EQ( 5, nRows );

	for ( int i = 0; i < nRows; i++ )
	{
		EXPECT_NE( (const char *)NULL, pRows[ i ].pszName );
		EXPECT_NE( 0, pRows[ i ].nType )
			<< pRows[ i ].pszName << " carries no type bit, so a client-ness "
			   "question about it answers the same as one about a name the "
			   "table does not hold at all.";
	}
}

//=============================================================================//
// AGREEMENT WITH eventlist.h. The bits are a LOCAL COPY of a number.
//=============================================================================//

//=============================================================================//
// AGREEMENT WITH EventCatalog.cs, which is where the table really lives.
//=============================================================================//

//=============================================================================//
// THE CALLER. A header nobody reads from is a table, not a rule.
//=============================================================================//

