//========= SourceSharp — a console command's native body, found by name =====//
//
// plan_server_novalve.md P2.3c. The carrier C#'s registration builds finds a
// body by the command's name; these pin the lookup it relies on. The C# half
// (NativeCommandTableTests) holds the table of names to the bodies in the tree.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "native_command_body.h"

#include <algorithm>
#include <string>
#include <vector>

class CCommand
{
public:
	int m_nMarker;
};

namespace
{

int g_nCalledWith = 0;

}

SS_NATIVE_COMMAND( ss_test_body_alpha )
{
	g_nCalledWith = args.m_nMarker;
}

SS_NATIVE_COMMAND( ss_test_body_beta )
{
	g_nCalledWith = -args.m_nMarker;
}

TEST( NativeCommandBody, EveryBodyDefinedIsOnTheList )
{
	std::vector< std::string > names;
	for ( const CNativeCommandBody *p = CNativeCommandBody::First(); p; p = p->Next() )
		names.push_back( p->Name() );

	ASSERT_EQ( 2u, names.size() );
	EXPECT_NE( names.end(), std::find( names.begin(), names.end(), "ss_test_body_alpha" ) );
	EXPECT_NE( names.end(), std::find( names.begin(), names.end(), "ss_test_body_beta" ) );
}

TEST( NativeCommandBody, FindAnswersTheBodyOfThatNameAndRunsIt )
{
	const CNativeCommandBody *p = CNativeCommandBody::Find( "ss_test_body_beta" );
	ASSERT_NE( nullptr, p );

	CCommand args;
	args.m_nMarker = 7;
	g_nCalledWith = 0;
	p->Run( args );
	EXPECT_EQ( -7, g_nCalledWith );
}

TEST( NativeCommandBody, FindIgnoresCaseAsTheConsoleDoes )
{
	EXPECT_EQ( CNativeCommandBody::Find( "ss_test_body_alpha" ),
	           CNativeCommandBody::Find( "SS_Test_Body_ALPHA" ) );
	EXPECT_NE( nullptr, CNativeCommandBody::Find( "SS_Test_Body_ALPHA" ) );
}

TEST( NativeCommandBody, ANameWithNoBodyIsNull )
{
	EXPECT_EQ( nullptr, CNativeCommandBody::Find( "ss_test_body_gamma" ) );
	EXPECT_EQ( nullptr, CNativeCommandBody::Find( "ss_test_body_alph" ) );
	EXPECT_EQ( nullptr, CNativeCommandBody::Find( NULL ) );
}
