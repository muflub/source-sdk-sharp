//========= SourceSharp — a user command's random seed, from C# ==============//
//
// client/native/md5_pseudorandom.cpp defines MD5_PseudoRandom by asking the
// managed-built IManagedDiagnosticsClient (plan_client_vtables CE9a), so
// tier1's checksum_md5.cpp is out of the build. It must forward the seed and
// answer what the area answers, and with no area it must stop rather than seed
// every command with 0.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "checksum_md5.h"
#include "imanaged_areas_client.h"

namespace
{

uint32_t g_nSeenSeed = 0;
int g_nCalls = 0;

// The area as managed builds it, faked: an object whose first word is its
// table (plan_forwarders_everywhere.md phase 4). Release, Validate and the
// report are the area's other slots; MD5_PseudoRandom must reach none of them.
class CFakeDiagnostics : public IManagedDiagnosticsClient
{
public:
	CFakeDiagnostics() : IManagedDiagnosticsClient( &kTable ) {}

private:
	static void Released( IManagedObject * ) { ADD_FAILURE() << "MD5_PseudoRandom released the area"; }
	static int32_t Validated( IManagedObject *, const uint32_t *, int32_t )
	{
		ADD_FAILURE() << "MD5_PseudoRandom validated the area";
		return 0;
	}
	static int32_t Reported( IManagedDiagnosticsClient *, char *, int32_t, int32_t )
	{
		ADD_FAILURE() << "MD5_PseudoRandom asked for the crossings report";
		return -1;
	}
	static uint32_t Hashed( IManagedDiagnosticsClient *, uint32_t nSeed )
	{
		g_nSeenSeed = nSeed;
		g_nCalls++;
		return nSeed * 3u + 1u;
	}

	static const SSManagedDiagnosticsClientTable kTable;
};

const SSManagedDiagnosticsClientTable CFakeDiagnostics::kTable = { &Released, &Validated, &Reported, &Hashed };

CFakeDiagnostics g_Diagnostics;
IManagedDiagnosticsClient *g_pInstalled = &g_Diagnostics;

}

IManagedDiagnosticsClient *ManagedDiagnosticsClient()
{
	return g_pInstalled;
}

TEST( Md5PseudoRandomShim, ForwardsTheSeedAndAnswersTheArea )
{
	g_pInstalled = &g_Diagnostics;
	g_nCalls = 0;

	EXPECT_EQ( 3u * 4242u + 1u, MD5_PseudoRandom( 4242u ) );
	EXPECT_EQ( 4242u, g_nSeenSeed );
	EXPECT_EQ( 1, g_nCalls );
}

TEST( Md5PseudoRandomShim, TheWholeSeedCrossesUnsigned )
{
	g_pInstalled = &g_Diagnostics;

	MD5_PseudoRandom( 0xffffffffu );
	EXPECT_EQ( 0xffffffffu, g_nSeenSeed );
}

TEST( Md5PseudoRandomShimDeathTest, NoAreaStopsTheProcess )
{
	g_pInstalled = NULL;
	EXPECT_DEATH( MD5_PseudoRandom( 1u ), "" );
	g_pInstalled = &g_Diagnostics;
}
