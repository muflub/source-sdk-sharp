// SFM is dead (user ruling 2026-10-19): client.so's tools-off stand-ins
// (src/client/native/shell_tools_off_client.cpp), linked here as shipped. The
// Valve TUs still compiled into client.so (clientshadowmgr.cpp,
// flashlighteffect.cpp) guard their tool arms on these answers, so each is
// held to "tools are off".

#include "gtest/gtest.h"

class IMaterial;
class KeyValues;

#include "toolframework_client.h"

// cdll_client_int.h's declaration, which flashlighteffect.cpp reads it by.
extern IClientTools *clienttools;

TEST( ShellToolsOffClient, ToolsAreNotEnabled )
{
	EXPECT_FALSE( ToolsEnabled() );
}

TEST( ShellToolsOffClient, ClientToolsIsAnObjectThatIsNotRecording )
{
	// flashlighteffect.cpp dereferences it unguarded, so NULL would crash.
	ASSERT_NE( nullptr, clienttools );
	EXPECT_FALSE( clienttools->IsInRecordingMode() );
}

TEST( ShellToolsOffClient, EnablingRecordingModeDoesNotStartRecording )
{
	ASSERT_NE( nullptr, clienttools );
	clienttools->EnableRecordingMode( true );
	EXPECT_FALSE( clienttools->IsInRecordingMode() );
	clienttools->EnableRecordingMode( false );
	EXPECT_FALSE( clienttools->IsInRecordingMode() );
}

TEST( ShellToolsOffClient, NoEntityHasAToolHandle )
{
	ASSERT_NE( nullptr, clienttools );
	EXPECT_EQ( (HTOOLHANDLE)HTOOLHANDLE_INVALID, clienttools->GetToolHandleForEntityByIndex( 1 ) );
	EXPECT_FALSE( clienttools->IsValidHandle( 1 ) );
}

TEST( ShellToolsOffClient, TheMessageFunctionsReadNothing )
{
	// Never reached while ToolsEnabled() is false; if one is, it must not
	// touch its arguments (the caller owns and deletes the KeyValues).
	ToolFramework_PostToolMessage( HTOOLHANDLE_INVALID, nullptr );
	ToolFramework_RecordMaterialParams( nullptr );
	EXPECT_FALSE( ToolsEnabled() );
}
