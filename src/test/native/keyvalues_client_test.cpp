// client.so's KeyValues members (src/client/native/keyvalues_client.cpp,
// plan_client_novalve C9 K5) over keyvalues_scenario.h, held line for line
// to the golden pristine KeyValues.cpp wrote
// (keyvalues_members_oracle_test). This binary links tier1.a too, so a
// member the scenario calls that keyvalues_client.cpp does not define would
// pull the archive's KeyValues.o and fail the link on duplicates: the
// members the test reaches are ours.
//=============================================================================//

#include "gtest/gtest.h"

#include "keyvalues_scenario.h"
#include "engine_heap.h"

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
	std::ifstream in(RepoPath("src/test/client/Fixtures/keyvalues-members-stock.tsv"));
	std::stringstream ss;
	ss << in.rdbuf();
	return ss.str();
}
}

TEST(KeyValuesClient, TheScenarioIsStocksLineForLine)
{
	const std::string golden = Golden();
	ASSERT_GT(golden.size(), 2000u) << "the golden is missing or truncated";
	const std::string run = keyvalues_scenario::Run();

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
	EXPECT_GT(n, 100);
}

// The two heaps the port uses are the process's real ones: the scenario ran
// on them above, and they are found here, not stood in for.
TEST(KeyValuesClient, ItsHeapIsTier0s)
{
	EXPECT_NE(ss::FindEngineHeap(), (ss::IEngineHeap*)NULL);
	KeyValues* kv = new KeyValues("HeapCheck");
	kv->SetString("s", "on the engine heap");
	EXPECT_STREQ("on the engine heap", kv->GetString("s"));
	kv->deleteThis();
}
