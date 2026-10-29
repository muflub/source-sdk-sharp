// Pristine tier1/mempool.cpp over mempool_scenario.h, recorded as the golden
// src/test/client/Fixtures/mempool-stock.tsv that mempool_client_test replays
// against client.so's own CUtlMemoryPool members (src/client/native/
// tier1_client.cpp, plan_client_novalve C9). This asserts stock still
// produces exactly the golden; regenerate it only when the scenario changes
// (touch mempool_golden.regen beside this file, run, delete the marker).

#include "gtest/gtest.h"

#include "mempool_scenario.h"

#include <cstdlib>
#include <fstream>
#include <sstream>
#include <string>

namespace
{
std::string RepoPath(const char* pszRel)
{
	const char* pszRepo = getenv("SOURCESHARP_REPO");
	return std::string(pszRepo ? pszRepo : ".") + "/" + pszRel;
}
}

TEST(MempoolStockOracle, StockProducesTheGolden)
{
	const std::string run = mempool_scenario::Run();
	const std::string golden = RepoPath("src/test/client/Fixtures/mempool-stock.tsv");
	if (std::ifstream(RepoPath("src/test/oracle/native/mempool_golden.regen")).good())
		std::ofstream(golden) << run;

	std::ifstream in(golden);
	ASSERT_TRUE(in.good()) << golden << " is missing";
	std::stringstream ss;
	ss << in.rdbuf();
	ASSERT_GT(ss.str().size(), 2000u) << "the golden is empty or truncated";
	ASSERT_TRUE(ss.str() == run) << "pristine mempool.cpp no longer writes the golden; do NOT regenerate it to pass";
}
