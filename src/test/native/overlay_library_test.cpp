//========= SourceSharp — the overlay library, and the clip it restored =======//
//
// plan_entity.md Phase 4, step 4c. overlay_library.h is the drawing half of
// game/server/ndebugoverlay.cpp and game/shared/debugoverlay_shared.cpp, pulled
// out from under the entity, and this is what pins it.
//
// WHY THIS TEST EXISTS AND NOT JUST A LOOK AT THE DIFF.
//
// Six of the entries this library replaces had become `#ifdef SOURCESHARP
// return;` -- Line, Triangle, Text, DrawTickMarkedLine,
// DrawGroundCrossHairOverlay and EntityBounds -- because each clipped against a
// local player held as a pointer to a class this build no longer has. Six
// silent no-ops. Every one of them is exactly the shape plan_entity.md §7
// forbids, and every one of them is invisible to a test that only asserts "the
// call did not crash": a function whose body is `return;` passes that.
//
// So the subject here is the CLIP, and the readout beside it. A recording
// IVDebugOverlay says what actually reached the engine, and OverlayCounts_t says
// which branch a call that reached nothing took -- drawn, clipped, no viewer, no
// overlay interface. A build that stopped drawing reports drawn == 0 with a
// non-zero reason, which is a different fact from 0 and 0.
//
// THE CLIP IS NOT ONE RULE APPLIED TWICE, and that is the fact most worth
// pinning. A POINT is kept when it is near AND in front. A SEGMENT is kept when
// EITHER end is near and EITHER end is in front -- a line whose near end is
// behind you and whose far end is in front of you is a line you can see part
// of, and stock kept it. Written as PointIsVisible( start ) || PointIsVisible(
// end ) the two disagree only for a segment that is near-and-behind at one end
// and far-and-in-front at the other, which is most of the traces
// r_visualizetraces exists to draw. SegmentEitherEndInFront and
// SegmentNearEndBehindFarEndAhead are the two that separate the spellings.
//
// AND ONE FACT IS ABOUT THE FILES rather than the code, on bone_library_test's
// model and for its reason: the library's whole point is that it names no
// entity type, so that step 7's client half can call it, and that property is
// one #include away from being lost by an include that COMPILES. Comments are
// stripped before the scan and the stripper is itself tested -- unstripped, the
// scan would fail on this library's own header block, which explains at length
// what it does not contain.
//
// Links mathlib: DotProduct, LengthSqr and the yaw conversion are the subject.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "cmake_graph.h"

#include <fstream>
#include <sstream>
#include <string>
#include <vector>
#include <cstdio>
#include <unistd.h>

#include "mathlib/vector.h"
#include "mathlib/mathlib.h"
#include "Color.h"
#include "engine/ivdebugoverlay.h"

#include "overlay_library.h"

#include "mathlib_test_env.h"

// The one global the library reads, exactly as both files it replaces did. The
// game defines it per side (shell_engineboundary.cpp / shell_clientdll.cpp); here
// the test owns it, which is what lets a fact point it at a recorder and at NULL.
IVDebugOverlay *debugoverlay = NULL;

//-----------------------------------------------------------------------------
// THE RECORDING OVERLAY.
//
// The library reads one global, `debugoverlay`, exactly as both files it
// replaces did, and it is defined here so the test owns it. Every draw the
// library makes lands in one of these vectors, so "it drew" is a recorded fact
// rather than the absence of a crash.
//-----------------------------------------------------------------------------
namespace
{

struct RecordedLine
{
	Vector vecOrigin;
	Vector vecDest;
	int    r, g, b;
	bool   bNoDepthTest;
	float  flDuration;
};

struct RecordedBox
{
	Vector vecOrigin;
	Vector vecMins;
	Vector vecMaxs;
	QAngle angles;
	int    r, g, b, a;
	float  flDuration;
};

struct RecordedText
{
	Vector      vecOrigin;
	int         nLine;
	std::string sText;
	int         r, g, b, a;
};

class CRecordingOverlay : public IVDebugOverlay
{
public:
	std::vector< RecordedLine > lines;
	std::vector< RecordedBox >  boxes;
	std::vector< RecordedText > rgbText;
	std::vector< RecordedText > plainText;
	std::vector< RecordedText > entityText;
	int nGrids;
	int nClears;
	int nSweptBoxes;

	CRecordingOverlay() : nGrids( 0 ), nClears( 0 ), nSweptBoxes( 0 ) {}

	void Reset()
	{
		lines.clear();
		boxes.clear();
		rgbText.clear();
		plainText.clear();
		entityText.clear();
		nGrids = nClears = nSweptBoxes = 0;
	}

	virtual void AddEntityTextOverlay( int ent_index, int line_offset, float, int r, int g, int b, int a,
	                                   const char *format, ... ) OVERRIDE
	{
		RecordedText t;
		t.vecOrigin.Init( (float)ent_index, 0, 0 );
		t.nLine = line_offset;
		t.sText = format ? format : "";
		t.r = r; t.g = g; t.b = b; t.a = a;
		entityText.push_back( t );
	}

	virtual void AddBoxOverlay( const Vector &origin, const Vector &mins, const Vector &maxs,
	                            QAngle const &orientation, int r, int g, int b, int a, float duration ) OVERRIDE
	{
		RecordedBox box;
		box.vecOrigin = origin; box.vecMins = mins; box.vecMaxs = maxs; box.angles = orientation;
		box.r = r; box.g = g; box.b = b; box.a = a; box.flDuration = duration;
		boxes.push_back( box );
	}

	virtual void AddTriangleOverlay( const Vector &, const Vector &, const Vector &,
	                                 int, int, int, int, bool, float ) OVERRIDE {}

	virtual void AddLineOverlay( const Vector &origin, const Vector &dest, int r, int g, int b,
	                             bool noDepthTest, float duration ) OVERRIDE
	{
		RecordedLine line;
		line.vecOrigin = origin; line.vecDest = dest;
		line.r = r; line.g = g; line.b = b;
		line.bNoDepthTest = noDepthTest; line.flDuration = duration;
		lines.push_back( line );
	}

	virtual void AddTextOverlay( const Vector &origin, float, const char *format, ... ) OVERRIDE
	{
		RecordedText t;
		t.vecOrigin = origin; t.nLine = 0;
		t.sText = format ? format : "";
		t.r = t.g = t.b = t.a = 0;
		plainText.push_back( t );
	}

	virtual void AddTextOverlay( const Vector &origin, int line_offset, float, const char *format, ... ) OVERRIDE
	{
		RecordedText t;
		t.vecOrigin = origin; t.nLine = line_offset;
		t.sText = format ? format : "";
		t.r = t.g = t.b = t.a = 0;
		plainText.push_back( t );
	}

	virtual void AddScreenTextOverlay( float, float, float, int, int, int, int, const char * ) OVERRIDE {}

	virtual void AddSweptBoxOverlay( const Vector &, const Vector &, const Vector &, const Vector &,
	                                 const QAngle &, int, int, int, int, float ) OVERRIDE { nSweptBoxes++; }

	virtual void AddGridOverlay( const Vector & ) OVERRIDE { nGrids++; }

	virtual int ScreenPosition( const Vector &, Vector & ) OVERRIDE { return 0; }
	virtual int ScreenPosition( float, float, Vector & ) OVERRIDE { return 0; }

	virtual OverlayText_t *GetFirst( void ) OVERRIDE { return NULL; }
	virtual OverlayText_t *GetNext( OverlayText_t * ) OVERRIDE { return NULL; }
	virtual void ClearDeadOverlays( void ) OVERRIDE {}
	virtual void ClearAllOverlays() OVERRIDE { nClears++; }

	virtual void AddTextOverlayRGB( const Vector &origin, int line_offset, float, float r, float g,
	                                float b, float alpha, const char *format, ... ) OVERRIDE
	{
		RecordedText t;
		t.vecOrigin = origin; t.nLine = line_offset;
		t.sText = format ? format : "";
		t.r = (int)r; t.g = (int)g; t.b = (int)b; t.a = (int)alpha;
		rgbText.push_back( t );
	}

	virtual void AddTextOverlayRGB( const Vector &origin, int line_offset, float, int r, int g,
	                                int b, int a, const char *format, ... ) OVERRIDE
	{
		RecordedText t;
		t.vecOrigin = origin; t.nLine = line_offset;
		t.sText = format ? format : "";
		t.r = r; t.g = g; t.b = b; t.a = a;
		rgbText.push_back( t );
	}

	virtual void AddLineOverlayAlpha( const Vector &, const Vector &, int, int, int, int, bool, float ) OVERRIDE {}
	virtual void AddBoxOverlay2( const Vector &, const Vector &, const Vector &, QAngle const &,
	                             const Color &, const Color &, float ) OVERRIDE {}
	virtual void AddScreenTextOverlay2( float, float, int, float, int, int, int, int, const char * ) OVERRIDE {}
};

CRecordingOverlay g_Recorder;

// Far enough to be out of range: MAX_OVERLAY_DIST_SQR is 9e7, so 9,487 units.
const float kBeyondRange = 12000.0f;

// A fixture, so that every fact starts from a known viewer, an empty recorder
// and zeroed counters. A counter left over from the previous fact is exactly
// the kind of agreement-with-itself this file exists to avoid.
class OverlayLibrary : public ::testing::Test
{
protected:
	void SetUp() OVERRIDE
	{
		g_Recorder.Reset();
		Overlay_ResetCounts();
		Overlay_ClearViewer();
	}

	void TearDown() OVERRIDE
	{
		Overlay_ClearViewer();
	}

	// Eyes at the origin looking down +X.
	void SetLookingAlongX()
	{
		Overlay_SetViewer( Vector( 0, 0, 0 ), Vector( 1, 0, 0 ) );
	}
};

//-----------------------------------------------------------------------------
// The comment stripper, and the entity-name scan it feeds. bone_library_test's,
// restated here rather than shared, because a header shared between two test
// binaries is a third thing to keep in step.
//-----------------------------------------------------------------------------
} // anonymous namespace

//=============================================================================//
// THE CLIP -- points
//=============================================================================//

//=============================================================================//
// THE CLIP -- segments. NOT the point rule applied twice.
//=============================================================================//

TEST_F( OverlayLibrary, SegmentWithNoViewerIsNotVisible )
{
	EXPECT_FALSE( Overlay_SegmentIsVisible( Vector( 10, 0, 0 ), Vector( 20, 0, 0 ) ) );
}

TEST_F( OverlayLibrary, SegmentWithBothEndsAheadAndNearIsVisible )
{
	SetLookingAlongX();
	EXPECT_TRUE( Overlay_SegmentIsVisible( Vector( 10, 0, 0 ), Vector( 200, 0, 0 ) ) );
}

// The near end is BEHIND the eye and the far end is in front. Point-by-point,
// the near end fails; as a segment it is kept, because part of it is visible.
TEST_F( OverlayLibrary, SegmentNearEndBehindFarEndAheadIsVisible )
{
	SetLookingAlongX();

	const Vector vecBehind( -50, 0, 0 );
	const Vector vecAhead( 300, 0, 0 );

	EXPECT_TRUE( Overlay_SegmentIsVisible( vecBehind, vecAhead ) );
}

TEST_F( OverlayLibrary, SegmentWithBothEndsBehindIsNotVisible )
{
	SetLookingAlongX();
	EXPECT_FALSE( Overlay_SegmentIsVisible( Vector( -50, 0, 0 ), Vector( -300, 0, 0 ) ) );
}

// One end out of range and one end in it is kept: the distance rule needs BOTH
// ends out of range before a line is dropped.
TEST_F( OverlayLibrary, SegmentWithOneEndInRangeIsVisible )
{
	SetLookingAlongX();
	EXPECT_TRUE( Overlay_SegmentIsVisible( Vector( 10, 0, 0 ), Vector( kBeyondRange, 0, 0 ) ) );
}

TEST_F( OverlayLibrary, SegmentWithBothEndsOutOfRangeIsNotVisible )
{
	SetLookingAlongX();
	EXPECT_FALSE( Overlay_SegmentIsVisible( Vector( kBeyondRange, 0, 0 ),
	                                        Vector( kBeyondRange + 100, 0, 0 ) ) );
}

//=============================================================================//
// THE READOUT -- which branch a call that drew nothing took
//=============================================================================//

TEST_F( OverlayLibrary, LineWithNoViewerCountsNoViewerAndDrawsNothing )
{
	debugoverlay = &g_Recorder;

	Overlay_Line( Vector( 10, 0, 0 ), Vector( 20, 0, 0 ), 255, 0, 0, false, 0 );

	EXPECT_EQ( 0u, Overlay_Counts().nDrawn );
	EXPECT_EQ( 1u, Overlay_Counts().nNoViewer );
	EXPECT_EQ( 0u, g_Recorder.lines.size() );
}

TEST_F( OverlayLibrary, ClippedLineCountsClippedAndDrawsNothing )
{
	debugoverlay = &g_Recorder;
	SetLookingAlongX();

	Overlay_Line( Vector( -50, 0, 0 ), Vector( -300, 0, 0 ), 255, 0, 0, false, 0 );

	EXPECT_EQ( 0u, Overlay_Counts().nDrawn );
	EXPECT_EQ( 1u, Overlay_Counts().nClipped );
	EXPECT_EQ( 0u, Overlay_Counts().nNoViewer );
	EXPECT_EQ( 0u, g_Recorder.lines.size() );
}

TEST_F( OverlayLibrary, VisibleLineReachesTheOverlayWithItsColour )
{
	debugoverlay = &g_Recorder;
	SetLookingAlongX();

	Overlay_Line( Vector( 10, 1, 2 ), Vector( 200, 3, 4 ), 11, 22, 33, true, 5.0f );

	ASSERT_EQ( 1u, g_Recorder.lines.size() );
	EXPECT_EQ( 11, g_Recorder.lines[0].r );
	EXPECT_EQ( 22, g_Recorder.lines[0].g );
	EXPECT_EQ( 33, g_Recorder.lines[0].b );
	EXPECT_TRUE( g_Recorder.lines[0].bNoDepthTest );
	EXPECT_FLOAT_EQ( 5.0f, g_Recorder.lines[0].flDuration );
	EXPECT_EQ( 1u, Overlay_Counts().nDrawn );
}

TEST_F( OverlayLibrary, WithNoOverlayInterfaceADrawCountsNoOverlay )
{
	debugoverlay = NULL;
	SetLookingAlongX();

	Overlay_Line( Vector( 10, 0, 0 ), Vector( 200, 0, 0 ), 255, 0, 0, false, 0 );

	EXPECT_EQ( 0u, Overlay_Counts().nDrawn );
	EXPECT_EQ( 1u, Overlay_Counts().nNoOverlay );
}

TEST_F( OverlayLibrary, ResetCountsZeroesEveryCounter )
{
	debugoverlay = &g_Recorder;
	SetLookingAlongX();
	Overlay_Line( Vector( 10, 0, 0 ), Vector( 200, 0, 0 ), 1, 2, 3, false, 0 );
	ASSERT_EQ( 1u, Overlay_Counts().nDrawn );

	Overlay_ResetCounts();

	EXPECT_EQ( 0u, Overlay_Counts().nDrawn );
	EXPECT_EQ( 0u, Overlay_Counts().nClipped );
	EXPECT_EQ( 0u, Overlay_Counts().nNoViewer );
	EXPECT_EQ( 0u, Overlay_Counts().nNoOverlay );
}

//=============================================================================//
// THE TWO FAMILIES ARE DIFFERENT -- unclipped entries do not want a viewer
//=============================================================================//

TEST_F( OverlayLibrary, TextAtPositionDrawsWithNoViewer )
{
	debugoverlay = &g_Recorder;

	Overlay_TextAtPosition( Vector( 1, 2, 3 ), 4, "hello", 0.5f, 10, 20, 30, 40 );

	ASSERT_EQ( 1u, g_Recorder.rgbText.size() );
	EXPECT_EQ( 4, g_Recorder.rgbText[0].nLine );
	EXPECT_EQ( 10, g_Recorder.rgbText[0].r );
	EXPECT_EQ( 40, g_Recorder.rgbText[0].a );
	EXPECT_EQ( 1u, Overlay_Counts().nDrawn );
	EXPECT_EQ( 0u, Overlay_Counts().nNoViewer );
}

//=============================================================================//
// BOXES
//=============================================================================//

TEST_F( OverlayLibrary, BoxIsDrawnUnrotated )
{
	debugoverlay = &g_Recorder;

	Overlay_Box( Vector( 5, 6, 7 ), Vector( -1, -2, -3 ), Vector( 1, 2, 3 ), 9, 8, 7, 6, 2.0f );

	ASSERT_EQ( 1u, g_Recorder.boxes.size() );
	EXPECT_FLOAT_EQ( 0.0f, g_Recorder.boxes[0].angles.x );
	EXPECT_FLOAT_EQ( 0.0f, g_Recorder.boxes[0].angles.y );
	EXPECT_FLOAT_EQ( 0.0f, g_Recorder.boxes[0].angles.z );
	EXPECT_FLOAT_EQ( 5.0f, g_Recorder.boxes[0].vecOrigin.x );
	EXPECT_EQ( 6, g_Recorder.boxes[0].a );
}

// THE PADDED LINE's two facts (PaddedLineLiftsBothEndsInZOnly,
// PaddedLineIsClippedLikeAnyOtherLine) went with Overlay_LinePadded and
// DebugDrawLine at plan_server_native_v2 T5: no server object called either.

//=============================================================================//
// THE POOL
//=============================================================================//

// THE POOL HAS NO RESET since the server gc cull took Overlay_ResetPool (no
// caller but these tests), so it carries whatever earlier facts added. Each
// fact below FILLS every slot first: after OVERLAY_POOL_LINES adds the pool's
// contents are exactly these lines whatever ran before, so the counts are exact
// rather than order-dependent.
static void FillPool( int r )
{
	for ( int i = 0; i < OVERLAY_POOL_LINES; i++ )
		Overlay_AddLine( Vector( 10.0f + i, 0, 0 ), Vector( 20.0f + i, 0, 0 ), true, r, 2, 3 );
}

TEST_F( OverlayLibrary, DrawingThePoolDrawsEveryArmedSlot )
{
	debugoverlay = &g_Recorder;
	SetLookingAlongX();

	FillPool( 7 );
	Overlay_DrawPooledLines();

	ASSERT_EQ( (size_t)OVERLAY_POOL_LINES, g_Recorder.lines.size() );
	EXPECT_EQ( (unsigned)OVERLAY_POOL_LINES, Overlay_Counts().nDrawn );
	for ( size_t i = 0; i < g_Recorder.lines.size(); i++ )
		EXPECT_EQ( 7, g_Recorder.lines[i].r ) << "slot " << i << " drew a line this fact did not add";
}

TEST_F( OverlayLibrary, DrawingThePoolWithNoViewerDrawsNothingAndSaysWhy )
{
	debugoverlay = &g_Recorder;

	FillPool( 1 );
	Overlay_DrawPooledLines();

	EXPECT_EQ( 0u, g_Recorder.lines.size() );
	EXPECT_EQ( (unsigned)OVERLAY_POOL_LINES, Overlay_Counts().nNoViewer );
}

//=============================================================================//
// THE VIEWER ITSELF
//=============================================================================//

TEST_F( OverlayLibrary, ClearingTheViewerUnsetsIt )
{
	SetLookingAlongX();
	ASSERT_TRUE( Overlay_ViewerIsSet() );

	Overlay_ClearViewer();

	EXPECT_FALSE( Overlay_ViewerIsSet() );
}

//=============================================================================//
// THE FILES
//=============================================================================//

namespace
{

std::string ResolvedCwd()
{
	char buf[ 4096 ];
	if ( getcwd( buf, sizeof( buf ) ) )
		return std::string( buf );
	return std::string( "<getcwd failed>" );
}

} // namespace

//=============================================================================//
// THE LINK LINE -- who still asks for an overlay entry this build does not have
//
// WHY A LINK-LEVEL FACT AND NOT A TEXT ONE. `-ffunction-sections` is on the
// compile and `-Wl,--gc-sections` is on the link, so an object may reference a
// symbol NOTHING IN THIS BUILD DEFINES and the link still succeeds: the linker
// drops the referencing function first and `-Wl,--no-undefined` never sees the
// reference. That is not hypothetical. At this lane's base, physics.o carried
// three undefined `NDebugOverlay::` rows -- Box, Line and Text -- left behind
// when step 4c moved those bodies to `Overlay_*` and did not move one caller's
// call sites with them. Nothing was red, because DebugDrawContactPoints has no
// caller in this build either. The day it gets one, the link breaks.
//
// So: NO OBJECT ON THE SERVER LINK LINE MAY ASK FOR AN `NDebugOverlay::` ENTRY.
// Nothing in this build defines that namespace and nothing is meant to --
// `Overlay_*` is the spelling now, and a census that reads 0 importers for an
// overlay entry is only trustworthy if this is true.
//
// THE GRAPH IS CMAKE'S SINCE PHASE 6b: server_sourcesharp's link edge in
// <build>/build.ninja, read through cmake_graph::LinkObjects (it was VPC's
// _vpc_/ninja/sdk_csharp_debug.ninja). The reader throws on a missing graph,
// a missing edge or an edge with no objects.
//
// THE SCAN PROVES IT MEASURED SOMETHING, twice. The graph has to name a link
// line with the server's own overlay object on it, and the undefined-symbol text has to contain a row
// this test knows is there. Without both, an nm that printed nothing -- no
// build, no nm, a renamed graph -- reads exactly like a clean tree.
//=============================================================================//

namespace
{

// The objects on server_sourcesharp's link edge, as paths nm can open from
// the test's cwd (the edge names them relative to the build dir).
std::vector< std::string > ServerLinkLine()
{
	const std::string strBuild = cmake_graph::BuildDir();
	const std::vector< std::string > rel = cmake_graph::LinkObjects( cmake_graph::kServer );

	std::vector< std::string > out;
	for ( size_t i = 0; i < rel.size(); i++ )
	{
		if ( rel[ i ].size() > 2 && rel[ i ].compare( rel[ i ].size() - 2, 2, ".o" ) == 0 )
			out.push_back( rel[ i ][ 0 ] == '/' ? rel[ i ] : strBuild + "/" + rel[ i ] );
	}
	return out;
}

// The proof-of-read for the link line: the object of the server's own overlay
// shim, which imports the overlay entries the positive control below names.
bool HasOverlayShimObject( const std::vector< std::string > &objs )
{
	// CMake names an object after its source's path: this repo's
	// src/server/native/shell_debugoverlays.cpp compiles to
	// .../server_sourcesharp.dir/shell_debugoverlays.cpp.o.
	const std::string kObj = "/server_sourcesharp.dir/shell_debugoverlays.cpp.o";
	for ( size_t i = 0; i < objs.size(); i++ )
	{
		if ( objs[ i ].size() >= kObj.size()
		     && objs[ i ].compare( objs[ i ].size() - kObj.size(), kObj.size(), kObj ) == 0 )
			return true;
	}
	return false;
}

// `nm -C --undefined-only` over every object at once. Demangled, because the
// namespace is only spelled `NDebugOverlay::` after demangling.
std::string UndefinedSymbolsOnTheServerLink( const std::vector< std::string > &objs )
{
	if ( objs.empty() )
		return std::string();

	std::string cmd = "nm -C --undefined-only";
	for ( size_t i = 0; i < objs.size(); i++ )
		cmd += " '" + objs[ i ] + "'";
	cmd += " 2>/dev/null";

	FILE *pPipe = popen( cmd.c_str(), "r" );
	if ( !pPipe )
		return std::string();

	std::string out;
	char buf[ 1024 ];
	while ( fgets( buf, sizeof( buf ), pPipe ) )
		out += buf;
	pclose( pPipe );
	return out;
}

} // namespace

TEST( OverlayLinkLine, TheGraphNamesAServerLinkWithObjectsOnIt )
{
	const std::vector< std::string > objs = ServerLinkLine();

	// RED AND NOT SKIPPED when there is no graph. A fact that passes because it
	// could not measure is the shape this tree has recorded 51 times. (The
	// `>= 50 objects` floor that stood here is RETIRED IN 6b: cmake_graph
	// throws on a missing graph or an empty edge, and the anchor below says the
	// edge is the server's.)
	ASSERT_TRUE( HasOverlayShimObject( objs ) )
		<< "read " << objs.size() << " objects off server_sourcesharp's link edge in "
		<< cmake_graph::BuildDir() << "/build.ninja and none is shell_debugoverlays.cpp.o"
		<< " -- run `make debug` first. Working directory: " << ResolvedCwd();
}

TEST( OverlayLinkLine, TheScanReachesTheOverlayConsumers )
{
	const std::vector< std::string > objs = ServerLinkLine();
	ASSERT_TRUE( HasOverlayShimObject( objs ) ) << "no server link line; working directory: " << ResolvedCwd();

	const std::string undefined = UndefinedSymbolsOnTheServerLink( objs );

	// THE POSITIVE CONTROL. shell_debugoverlays.o and shell_worldquery.o both
	// import this entry, so an nm that ran and read the right objects says so.
	// Without this, an empty string passes the absence below.
	EXPECT_NE( std::string::npos, undefined.find( "Overlay_TextAtPosition" ) )
		<< "nm over " << objs.size() << " objects found no importer of a known "
		   "imported overlay entry, so it measured nothing. Working directory: "
		<< ResolvedCwd();
}

TEST( OverlayLinkLine, NoObjectAsksForAnNDebugOverlayEntry )
{
	const std::vector< std::string > objs = ServerLinkLine();
	ASSERT_TRUE( HasOverlayShimObject( objs ) ) << "no server link line; working directory: " << ResolvedCwd();

	const std::string undefined = UndefinedSymbolsOnTheServerLink( objs );
	ASSERT_NE( std::string::npos, undefined.find( "Overlay_TextAtPosition" ) )
		<< "the scan measured nothing; working directory: " << ResolvedCwd();

	const size_t at = undefined.find( "NDebugOverlay::" );
	EXPECT_EQ( std::string::npos, at )
		<< "an object on the server link line asks for an NDebugOverlay entry "
		   "that nothing in this build defines. It links today only because "
		   "--gc-sections drops the caller first; a caller makes it a link "
		   "error. The spelling is Overlay_* now:\n"
		<< ( at == std::string::npos ? std::string() : undefined.substr( at, 200 ) );
}

//=============================================================================//
// WHAT 6c TOOK OUT, AND THE PROOF THAT THIS READ THE RIGHT TREE
//
// An absence check is worth nothing if it cannot show what it read: a helper
// that walks upward for a repo root escapes a worktree into the main checkout,
// where the deleted file still exists, and the absence then answers about
// somebody else's tree. This file's ReadFile takes a path relative to the
// working directory and never walks, and buildcpptests runs from src/ -- so the
// tree read is the tree the runner is in. That is asserted rather than assumed:
// every fact below first requires a LIVE entry to be present in the same text,
// so a read that returned nothing is red instead of vacuously clean, and the
// resolved working directory is printed when one fails.
//=============================================================================//

namespace
{

} // namespace

