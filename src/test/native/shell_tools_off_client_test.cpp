// SFM is dead (user ruling 2026-10-19): client.so's tools-off stand-ins
// (src/client/native/shell_tools_off_client.cpp), linked here as shipped. The
// one Valve TU still compiled into client.so that names them
// (clientshadowmgr.cpp) guards its tool arms on these answers, so each is held
// to "tools are off". `clienttools` and ToolFramework_PostToolMessage left with
// flashlighteffect.cpp (C7 cluster 12).

#include "gtest/gtest.h"

class IMaterial;
class KeyValues;

#include "toolframework_client.h"

TEST( ShellToolsOffClient, ToolsAreNotEnabled )
{
	EXPECT_FALSE( ToolsEnabled() );
}

TEST( ShellToolsOffClient, RecordingMaterialParamsReadsNothing )
{
	// Never reached while ToolsEnabled() is false; if it is, it must not
	// touch its argument.
	ToolFramework_RecordMaterialParams( nullptr );
	EXPECT_FALSE( ToolsEnabled() );
}
