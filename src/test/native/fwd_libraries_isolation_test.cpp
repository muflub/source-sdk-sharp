//========= SourceSharp — the forwarder libraries, as the build links them ====//
//
// plan_engine_forwarders.md. Each forwarder library (libss_fwd_server.so,
// libss_fwd_client.so) exports its one function and nothing else, needs no
// other library, libc included (its functions are handed the object, so there
// is nothing to connect to, and it links no runtime: NO_RUNTIME in
// src/fwd/native/CMakeLists.txt), and its own side's game DLL needs it by its
// SONAME, resolved beside it through RUNPATH $ORIGIN, and imports its export
// rather than carrying a copy of its own; the other side's DLL needs it not
// at all (ruled 2026-10-18: one library per side). Read off the built files with nm and readelf, as particles_lib_isolation_test reads
// libss_particles.so.
//=============================================================================//

#include <cstdio>
#include <set>
#include <string>

#include "gtest/gtest.h"
#include "cmake_graph.h"

namespace
{
std::string Run(const std::string& cmd)
{
	FILE* pPipe = popen((cmd + " 2>/dev/null").c_str(), "r");
	if (!pPipe)
		return std::string();
	std::string out;
	char buf[4096];
	while (fgets(buf, sizeof(buf), pPipe))
		out += buf;
	pclose(pPipe);
	return out;
}

// The names of `nm <args>` lines whose type letter is one of strTypes.
std::set<std::string> Names(const std::string& strArgs, const std::string& strTypes)
{
	std::set<std::string> out;
	const std::string text = Run("nm " + strArgs);
	size_t start = 0;
	while (start < text.size())
	{
		size_t end = text.find('\n', start);
		if (end == std::string::npos)
			end = text.size();
		const std::string line = text.substr(start, end - start);
		start = end + 1;
		if (line.size() < 20)
			continue;
		const char t = line[17];
		if (strTypes.find(t) == std::string::npos || line[18] != ' ')
			continue;
		std::string name = line.substr(19);
		const size_t at = name.find('@');
		if (at != std::string::npos)
			name = name.substr(0, at);
		out.insert(name);
	}
	return out;
}

std::string Out(const std::string& strFile)
{
	return "'" + cmake_graph::BuildDir() + "/out/" + strFile + "'";
}

// Each library: its file, the one function it exports, and the game DLL
// that links it.
struct Library
{
	const char* pszFile;
	const char* pszExport;
	const char* pszDll;
};

constexpr Library kLibraries[] = {
	{"libss_fwd_server.so", "SSFwdServer_Table", "server.so"},
	{"libss_fwd_client.so", "SSFwdClient_Table", "client.so"},
};
} // namespace

TEST(FwdLibrariesIsolation, EachLibraryExportsItsTableFunctionAndNothingElse)
{
	for (const Library& lib : kLibraries)
	{
		// Proof of read: the library defines its functions locally.
		ASSERT_GE(Names(Out(lib.pszFile), "t").size(), 10u) << lib.pszFile << " read as nearly empty";

		const std::set<std::string> exports = Names("-D --defined-only " + Out(lib.pszFile), "TDBRVWiu");
		EXPECT_EQ(std::set<std::string>{lib.pszExport}, exports) << lib.pszFile;
	}
}

TEST(FwdLibrariesIsolation, EachLibraryNeedsNoOtherLibraryAndImportsNoSymbol)
{
	// NO_RUNTIME: no SDK library, no C++ runtime, no libc and no system
	// library (libstdc++, libm, libuuid and libc came with the VPC link line),
	// and nothing left to resolve: not even the crt start files' weak
	// references to libc's __cxa_finalize.
	for (const Library& lib : kLibraries)
	{
		// ::Run: inside a TEST body, Run alone is gtest's Test::Run.
		const std::string dynamic = ::Run("readelf -d " + Out(lib.pszFile));
		ASSERT_NE(std::string::npos, dynamic.find(std::string("Library soname: [") + lib.pszFile + "]"))
			<< "readelf read " << lib.pszFile << " as having no dynamic section";

		std::set<std::string> needed;
		const std::string marker = "Shared library: [";
		for (size_t at = dynamic.find(marker); at != std::string::npos; at = dynamic.find(marker, at + 1))
		{
			const size_t start = at + marker.size();
			needed.insert(dynamic.substr(start, dynamic.find(']', start) - start));
		}
		EXPECT_EQ(std::set<std::string>{}, needed) << lib.pszFile;

		EXPECT_EQ(std::set<std::string>{}, Names("-D --undefined-only " + Out(lib.pszFile), "Uwv")) << lib.pszFile;
	}
}

TEST(FwdLibrariesIsolation, EachGameLibraryNeedsItsOwnSidesLibraryAloneAndImportsItsTableFunction)
{
	for (const char* pszDll : {"server.so", "client.so"})
	{
		const std::string dynamic = ::Run("readelf -d " + Out(pszDll));
		ASSERT_NE(std::string::npos, dynamic.find("(NEEDED)")) << "readelf read " << pszDll << " as empty";
		EXPECT_NE(std::string::npos, dynamic.find("Library runpath: [$ORIGIN]")) << pszDll;
		// A link by path would put the build tree into DT_NEEDED.
		EXPECT_EQ(std::string::npos, dynamic.find(cmake_graph::BuildDir())) << pszDll;

		const std::set<std::string> imports = Names("-D -u " + Out(pszDll), "U");
		const std::set<std::string> defines = Names("-D --defined-only " + Out(pszDll), "TDBRVWiu");

		for (const Library& lib : kLibraries)
		{
			const bool bNeeded =
				dynamic.find(std::string("Shared library: [") + lib.pszFile + "]") != std::string::npos;

			if (std::string(pszDll) != lib.pszDll)
			{
				// The other side's library: neither needed nor imported.
				EXPECT_FALSE(bNeeded) << pszDll << " needs " << lib.pszFile;
				EXPECT_FALSE(imports.count(lib.pszExport)) << pszDll << " imports " << lib.pszExport;
				continue;
			}

			EXPECT_TRUE(bNeeded) << pszDll << " does not need " << lib.pszFile;

			// Imported, never a copy of its own: the tables are the library's.
			EXPECT_TRUE(imports.count(lib.pszExport)) << pszDll << " does not import " << lib.pszExport;
			EXPECT_FALSE(defines.count(lib.pszExport)) << pszDll << " exports a " << lib.pszExport << " of its own";
		}
	}
}
