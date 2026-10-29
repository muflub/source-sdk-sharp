// plan_particles_so.md section 3.3, the main technical risk of moving
// particles.a and dmxloader.a into libss_particles.so: the library carries its
// own tier1 (ConVar, KeyValues, strtools, CUtlBuffer...) and client.so carries
// its ports of the same names. Neither may interpose on the other. Held on the
// built binaries, by nm:
//
//   - the library's dynamic table exports SSParticleLib_Interface and nothing
//     else, and client.so links it like any library: DT_NEEDED on its SONAME;
//   - none of the library's dynamic imports is something client.so exports
//     (so no import of the library can bind into client.so);
//   - names both carry are defined LOCALLY in each (each resolves its own);
//   - client.so neither defines nor imports a particles.a/dmxloader.a name.

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

// "name" -> type letter, from `nm <args>` lines "address type name" or
// "                 U name". A version suffix (@GLIBC_2.2.5) is dropped.
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

std::string Lib()
{
	return "'" + cmake_graph::BuildDir() + "/out/libss_particles.so'";
}

std::string Client()
{
	return "'" + cmake_graph::BuildDir() + "/out/client.so'";
}
} // namespace

TEST(ParticlesLibIsolation, TheLibraryExportsItsInterfaceFunctionAndNothingElse)
{
	// Proof of read: the library's whole table is large and mostly local.
	ASSERT_GT(Names(Lib(), "t").size(), 1000u) << "libss_particles.so read as nearly empty";

	// Not CreateInterface: client.so exports one of its own, so it could not
	// name the library's.
	const std::set<std::string> exports = Names("-D --defined-only " + Lib(), "TDBRVWiu");
	EXPECT_EQ(std::set<std::string>{"SSParticleLib_Interface"}, exports);
}

// LINKED, NOT LOADED BY HAND (review, 2026-10-13): client.so names the library
// in DT_NEEDED by its SONAME, and the managed host by its own, never by a
// build-directory path, so both resolve beside client.so through RUNPATH
// $ORIGIN in the game's bin directory.
TEST(ParticlesLibIsolation, ClientSoNeedsBothLibrariesByTheirSonames)
{
	// ::Run: inside a TEST body, Run alone is gtest's Test::Run.
	const std::string lib = ::Run("readelf -d " + Lib());
	const std::string host = ::Run("readelf -d '" + cmake_graph::BuildDir() + "/out/libmanaged_host.so'");
	const std::string client = ::Run("readelf -d " + Client());
	ASSERT_NE(std::string::npos, client.find("(NEEDED)")) << "readelf read client.so as empty";

	EXPECT_NE(std::string::npos, lib.find("Library soname: [libss_particles.so]"));
	EXPECT_NE(std::string::npos, host.find("Library soname: [libmanaged_host.so]"));
	EXPECT_NE(std::string::npos, client.find("Shared library: [libss_particles.so]"));
	EXPECT_NE(std::string::npos, client.find("Shared library: [libmanaged_host.so]"));
	EXPECT_NE(std::string::npos, client.find("Library runpath: [$ORIGIN]"));
	// Nothing names the build tree: a link by path would put it in DT_NEEDED.
	EXPECT_EQ(std::string::npos, client.find(cmake_graph::BuildDir()));
}

TEST(ParticlesLibIsolation, NoImportOfTheLibraryIsAnExportOfClientSo)
{
	const std::set<std::string> imports = Names("-D -u " + Lib(), "Uw");
	const std::set<std::string> clientExports = Names("-D --defined-only " + Client(), "TDBRVWiu");
	// The stimulus reaches the subject: the library imports tier0's allocator,
	// and client.so exports at least the interface factory the engine asks for.
	ASSERT_TRUE(imports.count("g_pMemAlloc")) << "libss_particles.so's imports read as empty";
	ASSERT_TRUE(clientExports.count("CreateInterface")) << "client.so's exports read as empty";
	for (const std::string& name : imports)
		EXPECT_FALSE(clientExports.count(name))
			<< name << " is imported by libss_particles.so and exported by client.so";
}

TEST(ParticlesLibIsolation, TierOneNamesBothCarryAreLocalInEach)
{
	// Names client.so's ports and the library's tier1 copy both define: each
	// binds to its own, so each must be a local (t) definition on both sides.
	const char* const kShared[] = {
		"_ZN9KeyValuesC1EPKc", // KeyValues::KeyValues( const char * )
		// CUtlBuffer::CUtlBuffer( const void *, int, int ) stood here: client.so's
		// last caller, detailobjectsystem.cpp's lump read, left in C7 cluster 10
		// (the ( int, int, int ) form left with the screenshot encoders, C9).
		"_Z9V_strncpyPcPKci", // V_strncpy
	};
	const std::set<std::string> libLocal = Names(Lib(), "t");
	const std::set<std::string> clientLocal = Names(Client(), "t");
	for (const char* pszName : kShared)
	{
		EXPECT_TRUE(libLocal.count(pszName)) << pszName << " is not a local definition in libss_particles.so";
		EXPECT_TRUE(clientLocal.count(pszName)) << pszName << " is not a local definition in client.so";
	}
}

TEST(ParticlesLibIsolation, ClientSoCarriesNoParticlesArchiveCode)
{
	// particles.a's operators and dmxloader.a's element class are the
	// library's; client.so only forwards (particles_lib_client.cpp).
	const std::set<std::string> lib = Names(Lib(), "tT");
	const std::set<std::string> client = Names(Client(), "tTU");
	// Positive arm: the library has them.
	const char* const kArchive[] = {
		"_ZN18CParticleSystemMgr4InitEP20IParticleSystemQuery", // particles.a
		"_ZN11CDmxElementC2EPKc", // dmxloader.a
	};
	for (const char* pszName : kArchive)
	{
		EXPECT_TRUE(lib.count(pszName)) << pszName << " is not in libss_particles.so";
	}
	// client.so has the manager's Init only as its forward, which is defined
	// (T/t) -- never as an import -- and has no dmxloader code at all.
	const std::set<std::string> clientImports = Names("-u " + Client(), "U");
	EXPECT_FALSE(clientImports.count(kArchive[0]));
	EXPECT_FALSE(client.count(kArchive[1])) << "client.so carries dmxloader.a's CDmxElement";
}
