// CServerDebugOverlays: what managed code's overlay slots draw, over the overlay library
// it is handed and a fake engine overlay.

#include "gtest/gtest.h"

#include "shell_debugoverlays.h"
#include "overlay_library.h"
#include "fakes/fake_debug_overlay.g.h"
#include "basehandle.h"

#include "mathlib_test_env.h"

#include <vector>

#include "server_entities_fixture.h"
#include "ss_world.h"

// The links this file's subject imports: no entity resolves here.
void SourceSharp_Trace( const char *, const char * ) {}

int SourceSharp_GetVerbosity()
{
	return 0;
}

const model_t *CWorldCollideable::GetCollisionModel()
{
	return nullptr;
}

namespace
{
struct DrawnLine_t
{
	Vector vecStart;
	Vector vecEnd;
	float flDuration;
};

class CRecordingOverlay : public CFakeVDebugOverlay
{
public:
	void AddLineOverlay( const Vector &origin, const Vector &dest, int r, int g, int b, bool noDepthTest,
						 float duration ) override
	{
		m_Lines.push_back( { origin, dest, duration } );
	}

	std::vector<DrawnLine_t> m_Lines;
};

class ServerDebugOverlays : public ::testing::Test
{
protected:
	CRecordingOverlay m_Engine;
	COverlayLibrary m_Library{ &m_Engine };
	CTestServerEntities m_Server;
	CServerDebugOverlays m_Overlays{ m_Library, m_Server.m_Resolver };

	const float m_vecStart[3] = { 10, 0, 0 };
	const float m_vecEnd[3] = { 20, 0, 0 };
};
} // namespace

TEST_F( ServerDebugOverlays, ATimedLineReachesTheEngineWithItsDuration )
{
	m_Library.SetViewer( Vector( 0, 0, 0 ), Vector( 1, 0, 0 ) );

	m_Overlays.AddLineOverlay( m_vecStart, m_vecEnd, 255, 0, 0, 0, 5.0f );

	ASSERT_EQ( 1u, m_Engine.m_Lines.size() );
	EXPECT_EQ( 5.0f, m_Engine.m_Lines[0].flDuration );
	EXPECT_EQ( Vector( 10, 0, 0 ), m_Engine.m_Lines[0].vecStart );
}

TEST_F( ServerDebugOverlays, AnUntimedLineIsPooledNotDrawn )
{
	m_Library.SetViewer( Vector( 0, 0, 0 ), Vector( 1, 0, 0 ) );

	m_Overlays.AddLineOverlay( m_vecStart, m_vecEnd, 255, 0, 0, 0, 0.0f );
	EXPECT_TRUE( m_Engine.m_Lines.empty() ) << "a pooled line waits for the frame's draw";

	m_Library.DrawPooledLines();
	EXPECT_EQ( 1u, m_Engine.m_Lines.size() );
}

TEST_F( ServerDebugOverlays, TheFrameDrawHasNoViewerSoPooledLinesCountAsUnseen )
{
	m_Library.SetViewer( Vector( 0, 0, 0 ), Vector( 1, 0, 0 ) );
	m_Overlays.AddLineOverlay( m_vecStart, m_vecEnd, 255, 0, 0, 0, 0.0f );

	m_Overlays.DrawAll();

	EXPECT_TRUE( m_Engine.m_Lines.empty() );
	EXPECT_FALSE( m_Library.ViewerIsSet() );
	EXPECT_EQ( 1u, m_Library.Counts().nNoViewer );
}

TEST_F( ServerDebugOverlays, ALineWithoutBothEndsIsIgnored )
{
	m_Library.SetViewer( Vector( 0, 0, 0 ), Vector( 1, 0, 0 ) );

	m_Overlays.AddLineOverlay( nullptr, m_vecEnd, 255, 0, 0, 0, 5.0f );
	m_Overlays.AddLineOverlay( m_vecStart, nullptr, 255, 0, 0, 0, 5.0f );

	EXPECT_TRUE( m_Engine.m_Lines.empty() );
	EXPECT_EQ( 0u, m_Library.Counts().nNoViewer + m_Library.Counts().nDrawn );
}

TEST_F( ServerDebugOverlays, TheCountsReadOutIsTheLibrarysAndTakesNullOutputs )
{
	m_Library.SetViewer( Vector( 0, 0, 0 ), Vector( 1, 0, 0 ) );
	m_Overlays.AddLineOverlay( m_vecStart, m_vecEnd, 255, 0, 0, 0, 5.0f );
	m_Library.ClearViewer();
	m_Overlays.AddLineOverlay( m_vecStart, m_vecEnd, 255, 0, 0, 0, 5.0f );

	uint32_t nDrawn = 99, nClipped = 99, nNoViewer = 99, nNoOverlay = 99;
	int32_t bViewerSet = 99;
	m_Overlays.OverlayCounts( &nDrawn, &nClipped, &nNoViewer, &nNoOverlay, &bViewerSet );

	EXPECT_EQ( 1u, nDrawn );
	EXPECT_EQ( 0u, nClipped );
	EXPECT_EQ( 1u, nNoViewer );
	EXPECT_EQ( 0u, nNoOverlay );
	EXPECT_EQ( 0, bViewerSet );

	m_Overlays.OverlayCounts( nullptr, nullptr, nullptr, nullptr, nullptr );
}

TEST_F( ServerDebugOverlays, AnInvalidOrUnresolvedHandleDrawsNoEntityOverlay )
{
	EXPECT_EQ( 0, m_Overlays.DrawEntityOverlay( (uint32_t)INVALID_EHANDLE_INDEX, 0, "x", 1.0f, 255, 255, 255, 255 ) );
	EXPECT_EQ( 0, m_Overlays.DrawEntityOverlay( 5, 0, "x", 1.0f, 255, 255, 255, 255 ) );
	EXPECT_EQ( 0u, m_Library.Counts().nDrawn );
}
