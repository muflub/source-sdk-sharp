// client.so's CUtlMemoryPool members (src/client/native/tier1_client.cpp,
// plan_client_novalve C9) over mempool_scenario.h, held line for line to the
// golden pristine mempool.cpp wrote (mempool_stock_oracle_test). This links
// tier1.a too, so a member the scenario calls that tier1_client.cpp does not
// define would pull the archive's mempool.o and fail the link on duplicates.

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

std::string Golden()
{
	std::ifstream in(RepoPath("src/test/client/Fixtures/mempool-stock.tsv"));
	std::stringstream ss;
	ss << in.rdbuf();
	return ss.str();
}
}

TEST(MempoolClient, TheScenarioIsStocksLineForLine)
{
	const std::string golden = Golden();
	ASSERT_GT(golden.size(), 2000u) << "the golden is missing or truncated";
	const std::string run = mempool_scenario::Run();

	std::istringstream g(golden), r(run);
	std::string gl, rl;
	int n = 0;
	while (std::getline(g, gl))
	{
		ASSERT_TRUE((bool)std::getline(r, rl)) << "ours ended at line " << n << "; stock goes on: " << gl;
		ASSERT_EQ(gl, rl) << "line " << n;
		n++;
	}
	EXPECT_FALSE((bool)std::getline(r, rl)) << "ours has more lines than stock: " << rl;
	EXPECT_GT(n, 10000);
}
