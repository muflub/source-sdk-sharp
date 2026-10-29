//====== SourceSharp — no static state in the server DLL, and it is measured ==//
//
// plan_globals.md step G0. `plan.md` and `plan_shim.md` both say the port's C++
// holds no static state, and until this test nothing measured it.
//
// STATE IS A SECTION, NOT A KEYWORD. The rule these files state in prose is
// "no file-scope statics in SourceSharp's C++" (shell_gamedll.cpp), and the
// tool that used to check it, `tools/statics-audit`, scanned the SOURCE TEXT.
// That is why a global moved inside an inline accessor passed it:
// `PhysicsShell()::s_Shell` is 266,392 bytes of state no grep for `^static`
// will ever find, and there are 134 more rows of that shape on this link line.
// So the instrument is the SYMBOL TABLE -- readelf over the objects on the
// ninja graph's link edge -- and this test is what keeps it running.
//
//=============================================================================
// BOTH DIRECTIONS, WHICH IS THE WHOLE POINT
//=============================================================================
//
//   up     a piece of state with no row in docs/globals-ledger.tsv reddens,
//          and the link-level count rising above its ratchet ceiling reddens.
//
//   stale  a LEDGER ROW WHOSE SUBJECT IS GONE reddens -- both against the
//          symbol table (the tool's own check) and against the tree (every
//          row names a translation unit, and this test opens it). A keep-list
//          nobody re-derives rots silently: a row naming something deleted
//          three phases ago still reads as a live claim because nothing
//          contradicts it. That exact failure was caught at a merge on this
//          project on 2026-09-20, which is why the ratchet's first two
//          ceilings are both ZERO and fail on the first row in either
//          direction.
//
// And the vocabulary can rot too, so the dispositions are checked from the
// ledger's text, independently of the tool: an eighth cannot be created by
// typing one into the file.
//
//=============================================================================
// THE POSITIVE CONTROL, BECAUSE "EXITS 0" IS A CHECK THAT CANNOT FAIL
//=============================================================================
//
// This project has counted more than fifty checks that could not fail. A gate
// that shells out to a tool and asserts exit 0 is a prime candidate: if the
// tool silently stopped reading the graph it would answer 0 statics, 0
// orphans, and pass forever.
//
// So `TheGateFailsWhenTheLedgerIsMissingARow` writes a COPY of the real ledger
// with one row taken out, points the gate at it, and requires it to FAIL --
// on this tree, today, with these objects. If that test ever goes green the
// gate is not measuring, whatever the others say.
//
//=============================================================================
// WHY A GTEST DRIVING A SCRIPT
//=============================================================================
//
// dead_conditionals_test.cpp's and entity_census_test's shape: a tool in
// tools/, run by popen from the native leg, with fixtures for the states it
// must refuse. The native leg already runs source-reading checks over the
// shim; a new leg would need its own leg-floor row for no new coverage.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <sys/stat.h>
#include <sys/types.h>
#include <unistd.h>

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include <fstream>
#include <sstream>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// The worktree root. buildcpptests runs each binary with src/ as the working
// directory; inside the sniper container the tree is mounted at /my_mod, so
// ".." is the root. Deliberately a short candidate list and NOT an upward walk
// to a repo root: a walk escapes a git worktree into the main checkout and then
// answers about the wrong tree.
//-----------------------------------------------------------------------------
const char* WorktreeRoot()
{
	static const char* kCandidates[] = {".", "..", "../..", "../../.."};
	for (size_t i = 0; i < sizeof(kCandidates) / sizeof(kCandidates[0]); i++)
	{
		struct stat st;
		const std::string tool = std::string(kCandidates[i]) + "/tools/globals-census";
		const std::string src = std::string(kCandidates[i]) + "/src";
		if (stat(tool.c_str(), &st) == 0 && stat(src.c_str(), &st) == 0 && S_ISDIR(st.st_mode))
			return kCandidates[i];
	}
	return NULL;
}

std::string ReadFile(const std::string& szPath)
{
	std::ifstream f(szPath.c_str(), std::ios::binary);
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

std::string ReadFromRoot(const std::string& szRel)
{
	const char* pszRoot = WorktreeRoot();
	if (pszRoot == NULL)
		return std::string();
	return ReadFile(std::string(pszRoot) + "/" + szRel);
}

bool ExistsUnderRoot(const std::string& szRel)
{
	const char* pszRoot = WorktreeRoot();
	if (pszRoot == NULL)
		return false;
	struct stat st;
	return stat((std::string(pszRoot) + "/" + szRel).c_str(), &st) == 0;
}

std::vector<std::string> Lines(const std::string& szText)
{
	std::vector<std::string> out;
	std::string cur;
	for (size_t i = 0; i < szText.size(); i++)
	{
		if (szText[i] == '\n')
		{
			out.push_back(cur);
			cur.clear();
		}
		else if (szText[i] != '\r')
		{
			cur += szText[i];
		}
	}
	if (!cur.empty())
		out.push_back(cur);
	return out;
}

std::vector<std::string> Split(const std::string& szLine, char c)
{
	std::vector<std::string> out;
	std::string cur;
	for (size_t i = 0; i < szLine.size(); i++)
	{
		if (szLine[i] == c)
		{
			out.push_back(cur);
			cur.clear();
		}
		else
		{
			cur += szLine[i];
		}
	}
	out.push_back(cur);
	return out;
}

//-----------------------------------------------------------------------------
// Running a command from the worktree root. popen reports the wait(2) status,
// so the exit code has to be pulled back out of it; a test comparing the raw
// status against 1 would be comparing against 256 and never fail.
//-----------------------------------------------------------------------------
struct Run_t
{
	int m_nExit;
	std::string m_szOutput;
};

Run_t RunAtRoot(const std::string& szCommand)
{
	Run_t run;
	run.m_nExit = -1;

	const char* pszRoot = WorktreeRoot();
	if (pszRoot == NULL)
		return run;

	const std::string szFull = std::string("cd '") + pszRoot + "' && " + szCommand + " 2>&1";
	FILE* pPipe = popen(szFull.c_str(), "r");
	if (pPipe == NULL)
		return run;

	char rgchBuf[4096];
	while (fgets(rgchBuf, sizeof(rgchBuf), pPipe))
		run.m_szOutput += rgchBuf;

	const int nStatus = pclose(pPipe);
	run.m_nExit = WIFEXITED(nStatus) ? WEXITSTATUS(nStatus) : -1;
	return run;
}

//-----------------------------------------------------------------------------
// docs/globals-ratchet.tsv: `metric \t subject \t ceiling`. -1 when absent,
// which every caller ASSERTs on -- a ceiling that quietly reads as zero would
// make its test pass for the wrong reason.
//-----------------------------------------------------------------------------
struct Ratchet_t
{
	std::string m_szSubject;
	long m_lCeiling;
};

Ratchet_t RatchetRow(const char* pszMetric)
{
	Ratchet_t row;
	row.m_lCeiling = -1;

	const std::vector<std::string> lines = Lines(ReadFromRoot("docs/globals-ratchet.tsv"));
	for (size_t i = 0; i < lines.size(); i++)
	{
		if (lines[i].empty() || lines[i][0] == '#')
			continue;
		const std::vector<std::string> f = Split(lines[i], '\t');
		if (f.size() < 3 || f[0] != pszMetric)
			continue;
		row.m_szSubject = f[1];
		row.m_lCeiling = strtol(f[2].c_str(), NULL, 10);
		return row;
	}
	return row;
}

long RatchetCeiling(const char* pszMetric)
{
	return RatchetRow(pszMetric).m_lCeiling;
}

//-----------------------------------------------------------------------------
// A `key \t value` line out of one of the census reports. -1 when absent, so a
// report whose shape changed reddens instead of comparing against nothing.
//-----------------------------------------------------------------------------
long ReportedNumber(const std::string& szOutput, const char* pszKey)
{
	const std::vector<std::string> lines = Lines(szOutput);
	for (size_t i = 0; i < lines.size(); i++)
	{
		if (lines[i].empty() || lines[i][0] == '#')
			continue;
		const std::vector<std::string> f = Split(lines[i], '\t');
		if (f.size() < 2)
			continue;
		std::string key = f[0];
		while (!key.empty() && key[0] == ' ')
			key.erase(0, 1);
		if (key == pszKey)
			return strtol(f[1].c_str(), NULL, 10);
	}
	return -1;
}

//-----------------------------------------------------------------------------
// The ledger's data rows, read as text. Independent of the tool on purpose:
// the vocabulary check and the "every row names a file that exists" check must
// not be able to pass because the tool stopped reading.
//-----------------------------------------------------------------------------
struct LedgerRow_t
{
	std::string m_szHome;
	std::string m_szMangled;
	std::string m_szName;
	std::string m_szTu;
	std::string m_szDisposition;
};

std::vector<LedgerRow_t> LedgerRows()
{
	std::vector<LedgerRow_t> out;
	const std::vector<std::string> lines = Lines(ReadFromRoot("docs/globals-ledger.tsv"));
	for (size_t i = 0; i < lines.size(); i++)
	{
		if (lines[i].empty() || lines[i][0] == '#')
			continue;
		const std::vector<std::string> f = Split(lines[i], '\t');
		if (f.size() < 11 || f[0] == "home")
			continue;
		LedgerRow_t row;
		row.m_szHome = f[0];
		row.m_szMangled = f[1];
		row.m_szName = f[2];
		row.m_szTu = f[3];
		row.m_szDisposition = f[8];
		out.push_back(row);
	}
	return out;
}

// The ledger's data lines counted as text, apart from the parse: rows that number fewer
// than these were dropped by it, and none at all leaves every assertion over them vacuous.
// A count, not a floor: the rows fall as globals leave, towards the permanent ones.
size_t LedgerDataLines()
{
	size_t n = 0;
	const std::vector<std::string> lines = Lines(ReadFromRoot("docs/globals-ledger.tsv"));
	for (size_t i = 0; i < lines.size(); i++)
		if (!lines[i].empty() && lines[i][0] != '#' && lines[i].compare(0, 5, "home\t") != 0)
			n++;
	return n;
}

const char* kDispositions[] = {"GONE", "DEAD", "CONST", "MANAGED", "MEMBER", "FLOOR", "UNRULED"};

bool IsADisposition(const std::string& sz)
{
	for (size_t i = 0; i < sizeof(kDispositions) / sizeof(kDispositions[0]); i++)
		if (sz == kDispositions[i])
			return true;
	return false;
}

//-----------------------------------------------------------------------------
// The gate run, taken once and shared: it reads 108 objects and is the
// expensive part of this binary.
//-----------------------------------------------------------------------------
const Run_t& Gate()
{
	static Run_t run = RunAtRoot("python3 tools/globals-census gate");
	return run;
}

const Run_t& Summary()
{
	static Run_t run = RunAtRoot("python3 tools/globals-census summary");
	return run;
}

//-----------------------------------------------------------------------------
// The SECOND instrument: the linked .so, partitioned. `--config auto` picks
// whichever config's graph-built archives are still the ones its .so was
// linked from -- lib/public/linux64/*.a is not config-qualified, both graphs
// write that one path, and so at most one config is reconcilable at a time.
// Without `auto` this fact would redden whenever somebody had run
// `make release` last, which is not a regression in anything.
//-----------------------------------------------------------------------------
const Run_t& Reconcile()
{
	static Run_t run = RunAtRoot("python3 tools/globals-census reconcile --config auto");
	return run;
}

//=============================================================================
// The instrument itself.
//=============================================================================

TEST(GlobalsLedger, TheInstrumentIsPresentAndItsOwnSelfTestPasses)
{
	ASSERT_TRUE(WorktreeRoot() != NULL) << "cannot find the worktree root from the test's working directory, "
										   "so nothing below measures anything";
	ASSERT_TRUE(ExistsUnderRoot("tools/globals-census")) << "tools/globals-census is gone, and with it every number in "
															"docs/globals-ratchet.tsv";

	const Run_t run = RunAtRoot("python3 tools/globals-census --self-test");
	EXPECT_EQ(run.m_nExit, 0) << "the census's own self-test is red, so nothing it reports about "
								 "this tree can be trusted:\n"
							  << run.m_szOutput;
}

//=============================================================================
// Membership, both ways. Both ceilings are zero on purpose.
//=============================================================================

TEST(GlobalsLedger, EveryPieceOfStateHasALedgerRow)
{
	const long lCeiling = RatchetCeiling("globals_symbols_with_no_row");
	ASSERT_GE(lCeiling, 0) << "docs/globals-ratchet.tsv has no globals_symbols_with_no_row row";

	const Run_t& run = Gate();
	ASSERT_NE(run.m_nExit, -1) << "the gate could not be run at all";
	const long lMissing = ReportedNumber(run.m_szOutput, "symbols_with_no_ledger_row");
	ASSERT_GE(lMissing, 0) << "the gate printed no symbols_with_no_ledger_row line, so its report "
							  "shape changed and this test was comparing against nothing:\n"
						   << run.m_szOutput;
	EXPECT_LE(lMissing, lCeiling) << lMissing
								  << " pieces of static-storage state on the server link "
									 "line have no row in docs/globals-ledger.tsv. Run `tools/"
									 "globals-census reseed` and rule them.\n"
								  << run.m_szOutput;
}

TEST(GlobalsLedger, EveryLedgerRowStillNamesASymbol)
{
	const long lCeiling = RatchetCeiling("globals_rows_with_no_symbol");
	ASSERT_GE(lCeiling, 0) << "docs/globals-ratchet.tsv has no globals_rows_with_no_symbol row, "
							  "so the direction a keep-list rots in is unwatched";

	const Run_t& run = Gate();
	const long lStale = ReportedNumber(run.m_szOutput, "ledger_rows_with_no_symbol");
	ASSERT_GE(lStale, 0) << "the gate printed no ledger_rows_with_no_symbol line:\n" << run.m_szOutput;
	EXPECT_LE(lStale, lCeiling) << lStale
								<< " ledger rows name a symbol that is no longer on the link "
								   "line. A row describing a tree that no longer exists still reads as "
								   "a claim and nothing else would ever contradict it.\n"
								<< run.m_szOutput;
}

TEST(GlobalsLedger, TheGateItselfPasses)
{
	const Run_t& run = Gate();
	EXPECT_EQ(run.m_nExit, 0) << "tools/globals-census gate is red:\n" << run.m_szOutput;
}

//=============================================================================
// THE POSITIVE CONTROL. Without this every assertion above could be passing
// because the gate stopped reading the graph.
//=============================================================================

TEST(GlobalsLedger, TheGateFailsWhenTheLedgerIsMissingARow)
{
	const std::vector<LedgerRow_t> rows = LedgerRows();
	ASSERT_GT(rows.size(), 0u) << "docs/globals-ledger.tsv has no rows";
	ASSERT_EQ(LedgerDataLines(), rows.size()) << "the parse dropped ledger lines";

	const char* pszRoot = WorktreeRoot();
	ASSERT_TRUE(pszRoot != NULL);

	// A copy of the real ledger with its LAST data row taken out.
	const std::vector<std::string> lines = Lines(ReadFromRoot("docs/globals-ledger.tsv"));
	std::string szBody;
	size_t iLastData = 0;
	for (size_t i = 0; i < lines.size(); i++)
		if (!lines[i].empty() && lines[i][0] != '#' && Split(lines[i], '\t').size() >= 11 &&
			Split(lines[i], '\t')[0] != "home")
			iLastData = i;
	ASSERT_GT(iLastData, 0u);
	for (size_t i = 0; i < lines.size(); i++)
		if (i != iLastData)
			szBody += lines[i] + "\n";

	// The copy lives in a private temp directory, never in the tree:
	// tools/valve-text-audit reads every untracked file in the tree, and a copy
	// written under tools/ was listed by it, deleted here, then read there (a
	// FileNotFoundError in make check on CI, 2026-10-13), as the client twin
	// found on 2026-10-04. The gate takes --ledger as a path, so an absolute
	// one works.
	const char* pszTmpRoot = getenv("TMPDIR");
	std::string szDir = std::string(pszTmpRoot && *pszTmpRoot ? pszTmpRoot : "/tmp") + "/globals-ledger.XXXXXX";
	std::vector<char> dir(szDir.begin(), szDir.end());
	dir.push_back('\0');
	ASSERT_TRUE(mkdtemp(dir.data()) != NULL) << "cannot make a temp directory under " << szDir;
	const std::string szTmp = std::string(dir.data()) + "/globals-ledger-control.tsv";
	{
		std::ofstream f(szTmp.c_str(), std::ios::binary);
		ASSERT_TRUE(f.good()) << "cannot write the control ledger " << szTmp;
		f << szBody;
	}

	const Run_t run = RunAtRoot(std::string("python3 tools/globals-census gate --ledger ") + szTmp);
	unlink(szTmp.c_str());
	rmdir(dir.data());

	EXPECT_NE(run.m_nExit, 0) << "the gate PASSED against a ledger with a row deliberately removed. "
								 "It is not measuring this tree, and every other assertion in this "
								 "file is a check that cannot fail.\n"
							  << run.m_szOutput;
	EXPECT_EQ(ReportedNumber(run.m_szOutput, "symbols_with_no_ledger_row"), 1)
		<< "the gate failed, but not for the reason the control created -- it "
		   "should report exactly the one symbol whose row was removed.\n"
		<< run.m_szOutput;
}

//=============================================================================
// The counts, against their ceilings.
//=============================================================================

TEST(GlobalsLedger, TheLinkLevelStateCountIsAtOrBelowItsCeiling)
{
	const long lCeiling = RatchetCeiling("globals_link_level");
	ASSERT_GE(lCeiling, 0) << "docs/globals-ratchet.tsv has no globals_link_level row, so the "
							  "number this whole plan exists to drive down is unwatched";

	const Run_t& run = Gate();
	const long lNow = ReportedNumber(run.m_szOutput, "link_level");
	ASSERT_GE(lNow, 0) << "the gate printed no link_level line:\n" << run.m_szOutput;
	EXPECT_LE(lNow, lCeiling) << lNow
							  << " pieces of static-storage state on the server link line, "
								 "against a ceiling of "
							  << lCeiling
							  << ". plan_globals.md 2's target "
								 "is ONE. Adding state moves the port backwards; if this rose for a "
								 "reason, raise the row deliberately and say what came back.";
	EXPECT_GT(lNow, 0) << "zero pieces of state would be the end of the plan, and is far more "
						  "likely to mean the census stopped reading the link edge";
}

TEST(GlobalsLedger, TheUnruledCountIsAtOrBelowItsCeiling)
{
	const long lCeiling = RatchetCeiling("globals_unruled");
	ASSERT_GE(lCeiling, 0) << "docs/globals-ratchet.tsv has no globals_unruled row";

	const Run_t& run = Gate();
	const long lNow = ReportedNumber(run.m_szOutput, "unruled");
	ASSERT_GE(lNow, 0) << "the gate printed no unruled line:\n" << run.m_szOutput;
	EXPECT_LE(lNow, lCeiling) << lNow << " ledger rows are UNRULED against a ceiling of " << lCeiling
							  << ". UNRULED is an admission that nobody has read the symbol; it is "
								 "the number this plan exists to drive to zero.";
}

TEST(GlobalsLedger, TheStaticInitialiserCountIsAtOrBelowItsCeiling)
{
	const long lCeiling = RatchetCeiling("globals_objects_with_static_init");
	ASSERT_GE(lCeiling, 0) << "docs/globals-ratchet.tsv has no globals_objects_with_static_init row";

	const Run_t& run = Summary();
	const long lNow = ReportedNumber(run.m_szOutput, "objects_with_a_static_initialiser");
	ASSERT_GE(lNow, 0) << "the summary printed no "
						  "objects_with_a_static_initialiser line:\n"
					   << run.m_szOutput;
	EXPECT_LE(lNow, lCeiling) << lNow
							  << " of the objects on the link line carry a static-initialiser "
								 "function, against a ceiling of "
							  << lCeiling
							  << ". Every one of "
								 "them runs before DLLInit, in link order, with no engine.";
}

TEST(GlobalsLedger, TheLocatorCallCountIsAtOrBelowItsCeiling)
{
	const long lCeiling = RatchetCeiling("globals_locator_calls");
	ASSERT_GE(lCeiling, 0) << "docs/globals-ratchet.tsv has no globals_locator_calls row";

	const Run_t& run = Summary();
	const long lNow = ReportedNumber(run.m_szOutput, "locator_calls");
	ASSERT_GE(lNow, 0) << "the summary printed no locator_calls line:\n" << run.m_szOutput;
	EXPECT_LE(lNow, lCeiling) << lNow << " calls find the root's state through an accessor, against a ceiling of "
							  << lCeiling
							  << ". A subsystem takes what it uses by constructor (plan_server_native_v2 "
								 "T6 part 3); a new accessor call is the lookup this ceiling retires.";
}

//=============================================================================
// The second instrument. One program's three numbers all come from one reader,
// so a bug in it moves them together and nothing inside the tool notices; the
// linked .so is a different reading of the same tree.
//=============================================================================

TEST(GlobalsLedger, TheTwoInstrumentsReconcileWithNothingLeftOver)
{
	const long lCeiling = RatchetCeiling("globals_reconcile_unattributed");
	ASSERT_GE(lCeiling, 0) << "docs/globals-ratchet.tsv has no globals_reconcile_unattributed row";

	const Run_t& run = Reconcile();
	ASSERT_NE(run.m_nExit, -1) << "the reconciliation could not be run";

	const long lLeftOver = ReportedNumber(run.m_szOutput, "UNATTRIBUTED");
	ASSERT_GE(lLeftOver, 0) << "the reconciliation printed no UNATTRIBUTED line, so its report "
							   "shape changed and this test was comparing against nothing:\n"
							<< run.m_szOutput;
	EXPECT_LE(lLeftOver, lCeiling) << lLeftOver
								   << " mutable symbols in the linked server.so belong to no "
									  "link-line object, no archive and no named crt/libgcc symbol. That "
									  "residue having to be EMPTY is what makes the reconciliation "
									  "falsifiable -- a leftover bucket that always balances is a check "
									  "that cannot fail.\n"
								   << run.m_szOutput;

	EXPECT_EQ(run.m_nExit, 0) << "tools/globals-census reconcile is red:\n" << run.m_szOutput;
}

//=============================================================================
// The ledger read as TEXT, independently of the tool.
//=============================================================================

TEST(GlobalsLedger, TheLedgerVocabularyCannotGrowAnEighthWord)
{
	const std::vector<LedgerRow_t> rows = LedgerRows();
	ASSERT_GT(rows.size(), 0u) << "docs/globals-ledger.tsv has no rows";
	ASSERT_EQ(LedgerDataLines(), rows.size()) << "the parse dropped ledger lines";

	for (size_t i = 0; i < rows.size(); i++)
		EXPECT_TRUE(IsADisposition(rows[i].m_szDisposition))
			<< rows[i].m_szName << " has disposition '" << rows[i].m_szDisposition
			<< "', which is not one of "
			   "plan_globals.md 1's six plus UNRULED. A seventh disposition is "
			   "a new licence to keep state and cannot be created by typing one.";
}

TEST(GlobalsLedger, EveryLedgerRowNamesATranslationUnitThatStillExists)
{
	const std::vector<LedgerRow_t> rows = LedgerRows();
	ASSERT_GT(rows.size(), 0u) << "docs/globals-ledger.tsv has no rows";
	ASSERT_EQ(LedgerDataLines(), rows.size()) << "the parse dropped ledger lines";

	size_t cMissing = 0;
	std::string szFirst;
	for (size_t i = 0; i < rows.size(); i++)
	{
		if (rows[i].m_szTu.empty() || rows[i].m_szTu == "?")
			continue;
		if (ExistsUnderRoot("src/" + rows[i].m_szTu))
			continue;
		cMissing++;
		if (szFirst.empty())
			szFirst = rows[i].m_szTu + " (" + rows[i].m_szName + ")";
	}
	EXPECT_EQ(cMissing, 0u) << cMissing
							<< " ledger rows name a source file that is not in the "
							   "tree, first: "
							<< szFirst
							<< ". This is the stale direction checked "
							   "WITHOUT the tool, so it still fires if the tool stops reading.";
}

TEST(GlobalsLedger, TheLedgerAndTheRatchetAgreeOnHowManyRowsThereAre)
{
	const long lCeiling = RatchetCeiling("globals_link_level");
	ASSERT_GE(lCeiling, 0);
	const std::vector<LedgerRow_t> rows = LedgerRows();
	EXPECT_EQ((long)rows.size(), lCeiling) << "docs/globals-ledger.tsv holds " << rows.size()
										   << " rows and "
											  "docs/globals-ratchet.tsv caps the link level at "
										   << lCeiling
										   << ". The ledger has a row per link-level symbol, so when the count "
											  "goes down both move together in the same commit.";
}

TEST(GlobalsLedger, EveryRatchetRowNamesASubjectThatStillExists)
{
	static const char* kMetrics[] = {
		"globals_symbols_with_no_row",
		"globals_rows_with_no_symbol",
		"globals_link_level",
		"globals_unruled",
		"globals_objects_with_static_init",
		"globals_reconcile_unattributed",
		"globals_locator_calls",
	};
	for (size_t i = 0; i < sizeof(kMetrics) / sizeof(kMetrics[0]); i++)
	{
		const Ratchet_t row = RatchetRow(kMetrics[i]);
		ASSERT_GE(row.m_lCeiling, 0) << "docs/globals-ratchet.tsv has no " << kMetrics[i] << " row";
		EXPECT_TRUE(ExistsUnderRoot(row.m_szSubject)) << kMetrics[i] << " caps a number about '" << row.m_szSubject
													  << "', which is not in the tree. A ceiling whose subject is gone "
														 "describes a tree that no longer exists.";
	}
}

} // namespace
