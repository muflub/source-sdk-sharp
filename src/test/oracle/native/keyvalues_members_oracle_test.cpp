// Pristine tier1/KeyValues.cpp over keyvalues_scenario.h, recorded as the
// golden src/test/client/Fixtures/keyvalues-members-stock.tsv that
// keyvalues_client_test replays against client.so's own KeyValues members
// (src/client/native/keyvalues_client.cpp, plan_client_novalve C9 K5). This
// asserts stock still produces exactly the golden; regenerating it changes it
// only when the scenario does (touch keyvalues_members_golden.regen beside
// this file, run, delete the marker, say why in the commit).
//=============================================================================//

#include "gtest/gtest.h"

#include "keyvalues_scenario.h"

#include <cstdlib>
#include <fstream>
#include <sstream>
#include <string>

namespace
{
std::string RepoPath( const char *pszRel )
{
	const char *pszRepo = getenv( "SOURCESHARP_REPO" );
	return std::string( pszRepo ? pszRepo : "." ) + "/" + pszRel;
}
}

TEST( KeyValuesMembersOracle, StockProducesTheGolden )
{
	const std::string run = keyvalues_scenario::Run();
	const std::string golden = RepoPath( "src/test/client/Fixtures/keyvalues-members-stock.tsv" );
	if ( std::ifstream( RepoPath( "src/test/oracle/native/keyvalues_members_golden.regen" ) ).good() )
		std::ofstream( golden ) << run;

	std::ifstream in( golden );
	ASSERT_TRUE( in.good() ) << golden << " is missing";
	std::stringstream ss;
	ss << in.rdbuf();
	ASSERT_GT( ss.str().size(), 2000u ) << "the golden is empty or truncated";
	ASSERT_TRUE( ss.str() == run ) << "pristine KeyValues.cpp no longer writes the golden; do NOT regenerate it to pass";
}
