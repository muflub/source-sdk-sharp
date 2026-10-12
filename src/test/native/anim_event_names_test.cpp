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

const char *const kEventListH  = "game/shared/eventlist.h";
const char *const kCatalogCs   = "sourcesharp/managed/SourceSharp.Shared/EventCatalog.cs";

std::string ReadFile( const char *pPath )
{
	FILE *fp = fopen( pPath, "rb" );

	if ( !fp )
		return std::string();

	std::string out;
	char buf[ 4096 ];
	size_t n;

	while ( ( n = fread( buf, 1, sizeof( buf ), fp ) ) > 0 )
		out.append( buf, n );

	fclose( fp );
	return out;
}

bool Has( const std::string &hay, const std::string &needle )
{
	return hay.find( needle ) != std::string::npos;
}

int CountOf( const std::string &hay, const std::string &needle )
{
	if ( needle.empty() )
		return 0;

	int n = 0;
	size_t at = 0;

	while ( ( at = hay.find( needle, at ) ) != std::string::npos )
	{
		n++;
		at += needle.size();
	}

	return n;
}

// anim_cluster_ledger_test.cpp's stripper.
std::string StripComments( const std::string &src )
{
	std::string out;
	out.reserve( src.size() );

	size_t i = 0;

	while ( i < src.size() )
	{
		if ( src.compare( i, 2, "//" ) == 0 )
		{
			while ( i < src.size() && src[ i ] != '\n' )
				i++;

			continue;
		}

		if ( src.compare( i, 2, "/*" ) == 0 )
		{
			const size_t end = src.find( "*/", i + 2 );

			if ( end == std::string::npos )
				break;

			i = end + 2;
			continue;
		}

		out += src[ i ];
		i++;
	}

	return out;
}

std::string Code( const char *pPath )
{
	return StripComments( ReadFile( pPath ) );
}

//-----------------------------------------------------------------------------
// `#define NAME ( 1 << n )` out of eventlist.h, as an int. -1 when the define
// is not there in that shape, which is a failure rather than a zero: a zero
// would compare equal to "this name has no type".
//-----------------------------------------------------------------------------
int BitFromEventListH( const std::string &header, const char *pszDefine )
{
	const std::string key = std::string( "#define " ) + pszDefine;
	const size_t at = header.find( key );

	if ( at == std::string::npos )
		return -1;

	const size_t shift = header.find( "<<", at );
	const size_t eol   = header.find( '\n', at );

	if ( shift == std::string::npos || eol == std::string::npos || shift > eol )
		return -1;

	const int n = atoi( header.c_str() + shift + 2 );

	if ( n < 0 || n > 30 )
		return -1;

	return 1 << n;
}

//-----------------------------------------------------------------------------
// EventCatalog.cs holds two parallel arrays: `Ordered`, the names, and
// `NativeTypes`, whose rows carry the name in a trailing comment. The comment
// is what this reads, because it is the row's own label and the two arrays are
// asserted to agree by ActivityCatalogTests on the managed side.
//
// Answers -1 for a name the catalog does not hold at all, which is a different
// failure from "holds it and says something else".
//-----------------------------------------------------------------------------
int CatalogTypeOf( const std::string &cs, const char *pszName )
{
	const std::string needle = std::string( "// " ) + pszName;
	size_t at = cs.find( needle );

	// It must be a WHOLE label: "// AE_CL_PLAYSOUND" must not match
	// "// AE_CL_PLAYSOUNDISH". The rows end at the newline.
	while ( at != std::string::npos )
	{
		const size_t end = at + needle.size();
		const char c = end < cs.size() ? cs[ end ] : '\n';

		if ( c == '\n' || c == '\r' )
			break;

		at = cs.find( needle, at + 1 );
	}

	if ( at == std::string::npos )
		return -1;

	// The row's value is the text before the comment, on the same line.
	const size_t bol = cs.rfind( '\n', at );

	if ( bol == std::string::npos )
		return -1;

	const std::string row = cs.substr( bol + 1, at - bol - 1 );

	int mask = 0;

	if ( Has( row, "TypeServer" ) ) mask |= kAnimEventTypeServer;
	if ( Has( row, "TypeClient" ) ) mask |= kAnimEventTypeClient;

	return mask;
}

const char *const kFive[] =
{
	AE_NAME_CL_CREATE_PARTICLE_EFFECT,
	AE_NAME_SV_PLAYSOUND,
	AE_NAME_SV_EXCLUDE_PLAYER_SOUND,
	AE_NAME_CL_PLAYSOUND,
	AE_NAME_CL_EXCLUDE_PLAYER_SOUND,
};

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

TEST( AnimEventNames, TheBitsAreEventListsOwn )
{
	const std::string header = ReadFile( kEventListH );

	ASSERT_FALSE( header.empty() )
		<< kEventListH << " could not be read; every comparison below would "
		   "pass for the wrong reason.";

	const int nServer = BitFromEventListH( header, "AE_TYPE_SERVER" );
	const int nClient = BitFromEventListH( header, "AE_TYPE_CLIENT" );

	ASSERT_NE( -1, nServer ) << "AE_TYPE_SERVER is not a `( 1 << n )` define in "
		<< kEventListH << " any more, so this test cannot read it.";
	ASSERT_NE( -1, nClient ) << "AE_TYPE_CLIENT is not a `( 1 << n )` define in "
		<< kEventListH << " any more.";

	EXPECT_EQ( nServer, (int)kAnimEventTypeServer )
		<< "anim_event_names.h's server bit is " << (int)kAnimEventTypeServer
		<< " and eventlist.h's is " << nServer << ".";

	EXPECT_EQ( nClient, (int)kAnimEventTypeClient )
		<< "anim_event_names.h's client bit is " << (int)kAnimEventTypeClient
		<< " and eventlist.h's is " << nClient << ". AE_TYPE_CLIENT is 1 << 4 "
		   "with four unrelated flags below it, so the obvious value is wrong.";
}

TEST( AnimEventNames, TheNewEventSystemBitIsEventListsOwn )
{
	const std::string header = ReadFile( kEventListH );

	ASSERT_FALSE( header.empty() )
		<< kEventListH << " could not be read; the comparison below would "
		   "pass for the wrong reason.";

	const int nNew = BitFromEventListH( header, "AE_TYPE_NEWEVENTSYSTEM" );

	ASSERT_NE( -1, nNew ) << "AE_TYPE_NEWEVENTSYSTEM is not a `( 1 << n )` "
		"define in " << kEventListH << " any more.";

	EXPECT_EQ( nNew, (int)kAnimEventTypeNewEventSystem )
		<< "anim_event_names.h's new-event-system bit is "
		<< (int)kAnimEventTypeNewEventSystem << " and eventlist.h's is " << nNew
		<< ". model_descriptor.cpp reads an event's name only when it is set.";
}

//=============================================================================//
// AGREEMENT WITH EventCatalog.cs, which is where the table really lives.
//=============================================================================//

TEST( AnimEventNames, TheCatalogReaderFindsARowItShouldAndRefusesOneItShouldNot )
{
	const std::string cs = ReadFile( kCatalogCs );

	ASSERT_FALSE( cs.empty() ) << kCatalogCs << " could not be read.";

	// The positive control: a row this reader is not otherwise asked about.
	EXPECT_EQ( kAnimEventTypeClient, CatalogTypeOf( cs, "AE_MUZZLEFLASH" ) )
		<< "the EventCatalog.cs row reader is not matching rows any more, so "
		   "every agreement below would pass by finding nothing.";

	// And the negative one: a name that is not in the file at all.
	EXPECT_EQ( -1, CatalogTypeOf( cs, "AE_NOBODY_REGISTERED_THIS" ) );
}

TEST( AnimEventNames, EveryNameHereIsANameTheManagedCatalogHolds )
{
	const std::string cs = ReadFile( kCatalogCs );

	ASSERT_FALSE( cs.empty() ) << kCatalogCs << " could not be read.";

	for ( size_t i = 0; i < sizeof( kFive ) / sizeof( kFive[ 0 ] ); i++ )
	{
		EXPECT_EQ( 1, CountOf( cs, std::string( "\"" ) + kFive[ i ] + "\"" ) )
			<< kFive[ i ] << " is not in " << kCatalogCs << "'s Ordered array "
			   "exactly once. Native code identifies events by this name and "
			   "managed code fills the table from that array.";
	}
}

TEST( AnimEventNames, EveryRowHereCarriesTheTypeTheManagedCatalogGivesIt )
{
	const std::string cs = ReadFile( kCatalogCs );

	ASSERT_FALSE( cs.empty() ) << kCatalogCs << " could not be read.";

	int nRows = 0;
	const AnimEventNameRow_t *pRows = AnimEventNames_Rows( &nRows );

	for ( int i = 0; i < nRows; i++ )
	{
		const int nCatalog = CatalogTypeOf( cs, pRows[ i ].pszName );

		ASSERT_NE( -1, nCatalog )
			<< pRows[ i ].pszName << " has no row in " << kCatalogCs;

		// The CLIENT bit is the one native code acts on, and the one that
		// decides whether a sound or a particle gets precached at all.
		EXPECT_EQ( ( nCatalog & kAnimEventTypeClient ) != 0,
		           ( pRows[ i ].nType & kAnimEventTypeClient ) != 0 )
			<< pRows[ i ].pszName << ": this header says client="
			<< ( ( pRows[ i ].nType & kAnimEventTypeClient ) != 0 )
			<< " and " << kCatalogCs << " says client="
			<< ( ( nCatalog & kAnimEventTypeClient ) != 0 ) << ".";

		EXPECT_EQ( ( nCatalog & kAnimEventTypeServer ) != 0,
		           ( pRows[ i ].nType & kAnimEventTypeServer ) != 0 )
			<< pRows[ i ].pszName << ": the SERVER bits disagree with "
			<< kCatalogCs << ".";
	}
}

//=============================================================================//
// THE CALLER. A header nobody reads from is a table, not a rule.
//=============================================================================//

// THE CALLER MOVED TO C# -- plan_server_novalve.md P2.4/P3. The model's
// event walks (the sound list the model sounds cache held, and the particle
// effects) are SourceSharp.Server.ModelComponents now, and the same two rules
// are held there: names are compared by name, and an old-system event's name
// is never read.
static const char *const kComponents = "sourcesharp/managed/SourceSharp.Server/ModelComponents.cs";

TEST( AnimEventNames, ModelComponentsComparesNamesAndNotRegistryOrdinals )
{
	const std::string code = Code( kComponents );

	ASSERT_FALSE( code.empty() ) << kComponents << " could not be read.";

	for ( const char *pszName : { "\"AE_SV_PLAYSOUND\"", "\"AE_SV_EXCLUDE_PLAYER_SOUND\"",
	                              "\"AE_CL_PLAYSOUND\"", "\"AE_CL_EXCLUDE_PLAYER_SOUND\"",
	                              "\"AE_CL_CREATE_PARTICLE_EFFECT\"" } )
		EXPECT_EQ( 1, CountOf( code, pszName ) ) << pszName << " is not compared by name";

	// The one ordinal compare that stays, and stays on purpose: an OLD-system
	// event carries no name, so its number is its identity.
	EXPECT_EQ( 1, CountOf( code, "e.Id == AeClCreateParticleEffectOrdinal" ) )
		<< kComponents << " lost the old-system arm of the particle check";
}

TEST( AnimEventNames, ModelComponentsGuardsTheNameReadOnTheFlag )
{
	const std::string code = Code( kComponents );

	ASSERT_FALSE( code.empty() ) << kComponents << " could not be read.";

	// szeventindex is meaningless without AE_TYPE_NEWEVENTSYSTEM. The sound
	// walk tests the flag before any name; the particle walk reads the name's
	// side only under it.
	EXPECT_EQ( 1, CountOf( code, "if ((e.Type & TypeNewEventSystem) != 0)" ) );
	EXPECT_EQ( 1, CountOf( code, "bool newSystem = (e.Type & TypeNewEventSystem) != 0;" ) );
	EXPECT_EQ( 1, CountOf( code, "(newSystem && IsClientName(e.Name))" ) );
}
