//========= SourceSharp — the usercmd codec is C# on both sides =============//
//
// usercmd.cpp was the native usercmd codec, relocated by plan_shim.md Phase 2d
// and kept under reason (f). The server's half (ReadUsercmd) moved to C# in
// plan_server_novalve.md P2.2 and the client's (WriteUsercmd, and ReadUsercmd
// for demo playback) in plan_client_novalve C9 (UserCmdCodec.Format: Write and
// Read side by side, held to pristine usercmd.cpp by UserCmdEncodeTests and to
// stock's decode by UserCmdCodecTests). So the file is gone, and these hold
// that: no DLL compiles a usercmd.cpp again, and the three IInput slots that
// called it are the managed crossings now.
//
//=============================================================================//

#include <stdio.h>
#include <string>

#include "gtest/gtest.h"

#include "cmake_graph.h"

namespace
{
}

TEST( UserCmdCodec, NoDllCompilesAUsercmdCpp )
{
	// Proof of read: a shared file both targets do compile.
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, "sourcesharp/game/shared/tier_connect.cpp" ) );
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, "sourcesharp/game/shared/tier_connect.cpp" ) );

	for ( const char *pszTarget : { cmake_graph::kClient, cmake_graph::kServer } )
	{
		EXPECT_FALSE( cmake_graph::Compiles( pszTarget, "sourcesharp/game/shared/usercmd.cpp" ) ) << pszTarget;
		EXPECT_FALSE( cmake_graph::Compiles( pszTarget, "game/shared/usercmd.cpp" ) ) << pszTarget;
	}
}
