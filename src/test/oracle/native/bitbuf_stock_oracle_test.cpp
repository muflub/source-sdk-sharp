// Pristine tier1 bitbuf.cpp over bitbuf_scenario.h, recorded as the golden
// src/test/client/Fixtures/bitbuf-stock.tsv that bitbuf_client_test replays
// against client.so's own bitbuf members (src/client/native/bitbuf_client.cpp,
// plan_client_novalve C9). This test asserts stock still produces exactly the
// golden. Regenerating it to make a test green deletes the check: it changes
// only when the scenario does, and then on purpose (touch
// bitbuf_golden.regen beside this file, run, delete the marker, and say why
// in the commit).

#include "gtest/gtest.h"

#include "bitbuf_scenario.h"

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

TEST(BitbufStockOracle, StockProducesTheGolden)
{
	const std::string run = bitbuf_scenario::Run();
	const std::string golden = RepoPath("src/test/client/Fixtures/bitbuf-stock.tsv");

	if (std::ifstream(RepoPath("src/test/oracle/native/bitbuf_golden.regen")).good())
		std::ofstream(golden) << run;

	std::ifstream in(golden);
	ASSERT_TRUE(in.good()) << golden << " is missing";
	std::stringstream ss;
	ss << in.rdbuf();
	ASSERT_GT(ss.str().size(), 100000u) << "the golden is empty or truncated";
	ASSERT_TRUE(ss.str() == run) << "pristine bitbuf.cpp no longer writes the golden; do NOT regenerate it to pass";
}
