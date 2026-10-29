//========= SourceSharp ==================================================//
//
// Where a native oracle reads upstream Valve code: the pristine clone at
// valve-sdk.lock, named by $SOURCESHARP_VALVE_SDK (the oracle CMakeLists sets it
// in every oracle test's environment). Never the farm (cwd), never the fork.
//
// A missing variable, a missing clone or a missing file is a FAILURE with the
// fix named, and the read answers empty -- callers then assert on what they
// parsed, so nothing can pass by reading nothing.
//
//=============================================================================//

#pragma once

#include <cstdlib>
#include <fstream>
#include <iterator>
#include <string>

#include "gtest/gtest.h"

// The clone's root, or "" (with a failure recorded) when there is none.
inline std::string ValveSdkRoot()
{
	const char *pszRoot = getenv( "SOURCESHARP_VALVE_SDK" );
	if ( !pszRoot || !*pszRoot )
	{
		ADD_FAILURE() << "SOURCESHARP_VALVE_SDK is not set: the oracle has no "
						 "pristine Valve SDK to read. Run `make setup`.";
		return std::string();
	}
	return pszRoot;
}

// A file of the clone by its path from the clone's root
// ("src/game/shared/multiplay_gamerules.cpp").
inline std::string ReadValve( const std::string &rel )
{
	const std::string root = ValveSdkRoot();
	if ( root.empty() )
		return std::string();
	std::ifstream f( ( root + "/" + rel ).c_str(), std::ios::binary );
	if ( !f.is_open() )
	{
		ADD_FAILURE() << "the pristine Valve SDK at " << root << " has no " << rel
					  << ": an oracle's reference must exist. Run `make setup`.";
		return std::string();
	}
	return std::string( ( std::istreambuf_iterator<char>( f ) ), std::istreambuf_iterator<char>() );
}
