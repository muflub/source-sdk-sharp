//====== SourceSharp — ORACLE: the save codec's time sentinels are upstream's ======//
//
// Moved out of save_field_codec_test.cpp (the codec's other checks stay there).
// shell_savegame.h's SAVEFIELD_ZERO_TIME and SAVEFIELD_INVALID_TIME must be the
// multiples of FLT_MAX that upstream's ZERO_TIME (game/shared/saverestore.cpp)
// and INVALID_TIME (public/datamap.h) are. Both are read from the pristine
// Valve SDK clone (valve_sdk.h): saverestore.cpp is no longer compiled, so the
// farm no longer carries it.
//
//=============================================================================//

#include <float.h>
#include <stdlib.h>
#include <string>

#include "gtest/gtest.h"
#include "valve_sdk.h"
#include "shell_savegame.h"

namespace
{

// The multiplier stock applies to FLT_MAX in one of its `#define`s -- -0.5 for
// ZERO_TIME in game/shared/saverestore.cpp, -1.0 for INVALID_TIME in
// public/datamap.h. Read out of the C++ for the same reason DatamapValueOf
// reads the enum: a sentinel this file also spelled out would agree with the
// header for no reason at all. 0.0 if the define is not there, which is never a
// value stock uses and therefore fails the fact rather than passing it.
double FltMaxMultiplierIn( const char *pszRelPath, const char *pszDefine )
{
	const std::string text = ReadValve( pszRelPath );

	const size_t at = text.find( std::string( "#define " ) + pszDefine );
	EXPECT_NE( at, std::string::npos ) << pszDefine << " is no longer defined in " << pszRelPath;
	if ( at == std::string::npos )
		return 0.0;

	const size_t eol = text.find( '\n', at );
	const std::string line = text.substr( at, eol - at );

	const size_t flt = line.find( "FLT_MAX" );
	EXPECT_NE( flt, std::string::npos ) << pszDefine << " is no longer a multiple of FLT_MAX";
	if ( flt == std::string::npos )
		return 0.0;

	const size_t star = line.find( '*', flt );
	if ( star == std::string::npos )
		return 0.0;

	return strtod( line.c_str() + star + 1, NULL );
}

} // namespace

TEST( SaveFieldCodec, TheTwoTimeSentinelsAreStocksOwnValues )
{
	// Read out of the C++ rather than written down here, on the same terms as
	// the six type numbers: a constant this file also spelled `FLT_MAX * -0.5`
	// would agree with the header for no reason.
	//
	// ZERO_TIME is game/shared/saverestore.cpp's, INVALID_TIME is
	// public/datamap.h's, and they are DIFFERENT multiples of FLT_MAX -- which
	// is the property that makes each a sentinel the other cannot be mistaken
	// for.
	const double flZero = FltMaxMultiplierIn( "src/game/shared/saverestore.cpp", "ZERO_TIME" );
	const double flInvalid = FltMaxMultiplierIn( "src/public/datamap.h", "INVALID_TIME" );

	EXPECT_DOUBLE_EQ( -0.5, flZero ) << "stock's ZERO_TIME has moved";
	EXPECT_DOUBLE_EQ( -1.0, flInvalid ) << "stock's INVALID_TIME has moved";

	EXPECT_FLOAT_EQ( (float)( FLT_MAX * flZero ), SAVEFIELD_ZERO_TIME );
	EXPECT_FLOAT_EQ( (float)( FLT_MAX * flInvalid ), SAVEFIELD_INVALID_TIME );
	EXPECT_NE( SAVEFIELD_ZERO_TIME, SAVEFIELD_INVALID_TIME );
}
