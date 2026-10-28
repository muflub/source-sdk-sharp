// What the server does not need lives in a client-only file (user ruling
// 2026-10-12): each such symbol beside a shared port is held here, per
// symbol, to "no object on server.so's link imports it and server.so does not
// define it", with the client file that now defines it still on client.so's
// link. A server object that starts calling one again reddens this, and the
// symbol goes back to the shared file.

#include <cstdio>
#include <string>
#include <vector>

#include "gtest/gtest.h"
#include "cmake_graph.h"

namespace
{
struct Split_t
{
	const char *pszSymbol;      // as nm prints it, mangled
	const char *pszClientFile;  // farm-relative
};

const Split_t kSplits[] = {
	// DevWarning( int level, ... ): dbg.h declares it extern "C".
	{ "DevWarning", "sourcesharp/game/client/tier0_spew_client.cpp" },
	{ "ThreadInMainThread", "sourcesharp/game/client/tier0_thread_client.cpp" },
	// tier1 convar.cpp's constructors only client objects call (C9 tier1).
	{ "_ZN6ConVarC1EPKcS1_iS1_", "sourcesharp/game/client/convar_client.cpp" },
	{ "_ZN6ConVarC1EPKcS1_iS1_PFvP7IConVarS1_fE", "sourcesharp/game/client/convar_client.cpp" },
	{ "_ZN9ConVarRefC1EPKcb", "sourcesharp/game/client/convar_client.cpp" },
	{ "_ZN10ConCommandC1EPKcPFvRK8CCommandES1_iPFiS1_PA64_cE", "sourcesharp/game/client/convar_client.cpp" },
	{ "_ZN10ConCommandC1EPKcPFvvES1_iPFiS1_PA64_cE", "sourcesharp/game/client/convar_client.cpp" },
	// plan_server_native_v2 T5: the mathlib members, tier0 thread calls and strtools
	// members server.so's objects stopped importing (nm U over its link edge), each
	// one client.so still links. The mathlib members only collisionutils.cpp reaches
	// are not held here: client.so's link drops them with that file's sections.
	{ "_Z10MatrixCopyRK11matrix3x4_tRS_", "sourcesharp/game/client/mathlib_port_client.cpp" },
	{ "_Z11AngleMatrixRK6QAngleR11matrix3x4_t", "sourcesharp/game/client/mathlib_port_client.cpp" },
	{ "_Z11ClearBoundsR6VectorS0_", "sourcesharp/game/client/mathlib_port_client.cpp" },
	// 2026-10-19, C7 cluster 12 S2: clientshadowmgr.cpp, the client's last fork TU, left client.so, and
	// with it the only client importer of fifteen of this file's rows (AngleIMatrix, CrossProduct,
	// MatrixInvert, VectorRotate, MatrixTranspose, ConcatTransforms, MatrixBuildScale, QuaternionAngles,
	// QuaternionMatrix, SignbitsForPlane, MatrixBuildPerspective, GeneratePerspectiveFrustum,
	// CalculateAABBFromProjectionMatrix, R_CullBox, VMatrix::Transpose): client.so defines none of them now
	// (nm), so their rows went; the managed shadow manager ports what it needs (ShadowMath.cs).
	{ "_Z13TransformAABBRK11matrix3x4_tRK6VectorS4_RS2_S5_", "sourcesharp/game/client/mathlib_port_client.cpp" },
	// 2026-10-25, lane M1-C: VectorVectors' last client importer was
	// shell_impact_effects.cpp's SetImpactControlPoint, which left with the
	// impact chain (the orientation is BulletImpactEffect's, FxMath.VectorVectors);
	// client.so defines it no more (nm), so its row and its forward went.
	{ "_Z14MatrixMultiplyRK7VMatrixS1_RS_", "sourcesharp/game/client/mathlib_port_client.cpp" },
	{ "_Z15MatrixSetColumnRK6VectoriR11matrix3x4_t", "sourcesharp/game/client/mathlib_port_client.cpp" },
	{ "_Z15VectorTransformPKfRK11matrix3x4_tPf", "sourcesharp/game/client/mathlib_port_client.cpp" },
	{ "_Z16AddPointToBoundsRK6VectorRS_S2_", "sourcesharp/game/client/mathlib_port_client.cpp" },
	{ "_Z16Vector3DMultiplyRK7VMatrixRK6VectorRS2_", "sourcesharp/game/client/mathlib_port_client.cpp" },
	{ "_Z17MatrixSetIdentityR7VMatrix", "sourcesharp/game/client/mathlib_port_client.cpp" },
	{ "_Z20MatrixInverseGeneralRK7VMatrixRS_", "sourcesharp/game/client/mathlib_port_client.cpp" },
	// CalcSqrDistanceToAABB's row went with lane M1-A (2026-10-25): its one client
	// importer, C_StandaloneEntity::ComputeFxBlend's portal-window fade, is
	// managed's StandaloneFade, and client.so's link drops the wrapper (nm).
	{ "_Z22LinearToGammaFullRangef", "sourcesharp/game/client/mathlib_port_client.cpp" },
	{ "_Z34Vector3DMultiplyPositionProjectiveRK7VMatrixRK6VectorRS2_", "sourcesharp/game/client/mathlib_port_client.cpp" },
	{ "_ZN7VMatrixaSERKS_", "sourcesharp/game/client/mathlib_port_client.cpp" },
	{ "ThreadGetCurrentId", "sourcesharp/game/client/tier0_thread_port.cpp" },
	{ "_ZNV16CThreadFastMutex4LockEyj", "sourcesharp/game/client/tier0_thread_port.cpp" },
	{ "_Z10_V_memmovePKciPvPKvi", "sourcesharp/game/client/strtools_client.cpp" },
	{ "_Z9_V_memsetPKciPvii", "sourcesharp/game/client/strtools_client.cpp" },
	{ "_Z9V_stricmpPKcS0_", "sourcesharp/game/client/strtools_client.cpp" },
	{ "DevMsg", "sourcesharp/game/client/tier0_spew_client.cpp" },
	{ "MemAllocOOMError", "sourcesharp/game/client/tier0_spew_client.cpp" },
	{ "_ZN2ss19RequireLoadedSymbolEPKcS1_", "sourcesharp/game/client/tier_bindings_client.cpp" },
	{ "_Z35EntityPrediction_SetSeedFromCommandPK8CUserCmd", "sourcesharp/game/client/shell_prediction.cpp" },
};

std::string NmRun( const std::string &cmd )
{
	FILE *pPipe = popen( ( cmd + " 2>/dev/null" ).c_str(), "r" );
	if ( !pPipe )
		return std::string();
	std::string out;
	char buf[ 4096 ];
	while ( fgets( buf, sizeof( buf ), pPipe ) )
		out += buf;
	pclose( pPipe );
	return out;
}

std::string ObjectsOf( const char *pszTarget, size_t *pn )
{
	const std::string strBuild = cmake_graph::BuildDir();
	std::string objs;
	*pn = 0;
	for ( const std::string &rel : cmake_graph::LinkObjects( pszTarget ) )
	{
		if ( rel.size() > 2 && rel.compare( rel.size() - 2, 2, ".o" ) == 0 )
		{
			objs += " '" + ( rel[ 0 ] == '/' ? rel : strBuild + "/" + rel ) + "'";
			( *pn )++;
		}
	}
	return objs;
}

bool HasLine( const std::string &text, const std::string &suffix )
{
	size_t start = 0;
	while ( start < text.size() )
	{
		size_t end = text.find( '\n', start );
		if ( end == std::string::npos )
			end = text.size();
		const std::string line = text.substr( start, end - start );
		if ( line.size() >= suffix.size() && line.compare( line.size() - suffix.size(), suffix.size(), suffix ) == 0 )
			return true;
		start = end + 1;
	}
	return false;
}
} // namespace

TEST( ServerDeadSplit, NoServerObjectImportsASplitSymbolAndServerSoDoesNotDefineIt )
{
	size_t nObjects = 0;
	const std::string undef = NmRun( "nm -u" + ObjectsOf( cmake_graph::kServer, &nObjects ) );
	// 80, not 100: the server's link edge is 100 objects since
	// plan_server_native_v2 T4e (shell_serversystems.o left).
	ASSERT_GT( nObjects, 80u ) << "the server's link edge named too few objects";
	// Proof of read: something the server does import.
	ASSERT_TRUE( HasLine( undef, " U malloc" ) ) << "nm -u over the server objects read nothing";

	const std::string so = NmRun( "nm '" + cmake_graph::BuildDir() + "/out/server.so'" );
	ASSERT_GT( so.size(), 10000u ) << "server.so read as nearly empty";

	for ( const Split_t &s : kSplits )
	{
		EXPECT_FALSE( HasLine( undef, std::string( " U " ) + s.pszSymbol ) )
			<< s.pszSymbol << " is imported by a server object again: it goes back to the shared port";
		EXPECT_FALSE( HasLine( so, std::string( " " ) + s.pszSymbol ) )
			<< s.pszSymbol << " is defined in server.so";
	}
}

TEST( ServerDeadSplit, TheClientFileStillDefinesEachSplitSymbol )
{
	for ( const Split_t &s : kSplits )
		EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, s.pszClientFile ) )
			<< s.pszClientFile << " left client.so, taking " << s.pszSymbol << " with it";

	const std::string so = NmRun( "nm '" + cmake_graph::BuildDir() + "/out/client.so'" );
	ASSERT_GT( so.size(), 10000u ) << "client.so read as nearly empty";
	for ( const Split_t &s : kSplits )
		EXPECT_TRUE( HasLine( so, std::string( " T " ) + s.pszSymbol ) || HasLine( so, std::string( " t " ) + s.pszSymbol ) )
			<< "client.so does not define " << s.pszSymbol;
}
