// SS_SKIP_ON_CI(reason): the native tests' half of the CI skip
// (src/test/common/NotOnCiFactAttribute.cs says what may go under it and
// why). In a test body, before anything it would fail on: when
// SOURCESHARP_CI is exactly "1" (CI's workflow sets it, nothing else does)
// the test is skipped with the reason; everywhere else it runs and fails as
// it always did.
#pragma once

#include <cstdlib>
#include <cstring>

#include "gtest/gtest.h"

namespace ss_test
{
	inline bool IsCiValue( const char *pszValue )
	{
		return pszValue && std::strcmp( pszValue, "1" ) == 0;
	}

	inline bool OnCi()
	{
		return IsCiValue( std::getenv( "SOURCESHARP_CI" ) );
	}
}

#define SS_SKIP_ON_CI( reason )                                                \
	do                                                                         \
	{                                                                          \
		if ( ::ss_test::OnCi() )                                               \
			GTEST_SKIP() << "not on CI: " << ( reason );                       \
	} while ( 0 )
