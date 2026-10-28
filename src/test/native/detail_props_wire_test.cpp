//========= SourceSharp — detail_props_wire.h, read ===========================//
//
// The readers native carries the managed detail props' answers out with
// (shell_detail_props.cpp), over the layouts DetailPropsWire writes (C#'s
// DetailPropsTests hold the writer to the same words): every field handed
// over in order, a malformed buffer refused at the word it runs out on, and
// nothing read past a buffer's length.
//=============================================================================//

#include <stdint.h>
#include <string.h>
#include <string>
#include <vector>

#include "gtest/gtest.h"

#include "detail_props_wire.h"

namespace
{

int32_t Bits( float f )
{
	int32_t w;
	memcpy( &w, &f, sizeof( w ) );
	return w;
}

// A buffer: its length in word 0, then the words.
std::vector<int32_t> Buffer( std::vector<int32_t> words )
{
	words.insert( words.begin(), (int32_t)words.size() + 1 );
	return words;
}

struct Level
{
	std::string log;

	void Precache( bool b ) { log += b ? "P1;" : "P0;"; }

	void Object( int i, void *pModel, const float *o, const float *a )
	{
		log += "O" + std::to_string( i ) + ":" + std::to_string( (uintptr_t)pModel ) + ":" +
			std::to_string( (int)o[2] ) + ":" + std::to_string( (int)a[1] ) + ";";
	}

	void Run( int leaf, int first, int count, bool fast )
	{
		log += "R" + std::to_string( leaf ) + ":" + std::to_string( first ) + ":" + std::to_string( count ) +
			( fast ? "f;" : ";" );
	}
};

struct View
{
	std::string log;

	void Proxy( void *pModel ) { log += "X" + std::to_string( (uintptr_t)pModel ) + ";"; }

	void Model( int i, unsigned char alpha, bool setAngles, const float *a, const float *c )
	{
		log += "M" + std::to_string( i ) + ":" + std::to_string( alpha ) + ( setAngles ? "a" : "" ) + ":" +
			std::to_string( (int)a[1] ) + ":" + std::to_string( (int)( c[0] * 10 ) ) + ";";
	}
};

struct Mesh
{
	std::string log;

	void Begin( int quads ) { log += "B" + std::to_string( quads ) + ";"; }

	void Vertex( const float *xyz, const unsigned char *rgba, const float *st )
	{
		log += "V" + std::to_string( (int)xyz[0] ) + ":" + std::to_string( rgba[0] ) + "," + std::to_string( rgba[3] ) +
			":" + std::to_string( (int)( st[1] * 4 ) ) + ";";
	}

	void EndDraw() { log += "E;"; }

	void ModelAlpha( int object, unsigned char alpha )
	{
		log += "A" + std::to_string( object ) + ":" + std::to_string( alpha ) + ";";
	}
};

} // namespace

TEST( DetailPropsWire, ALevelHandsOverItsPrecacheObjectsAndRunsInOrder )
{
	// One model (pointer 0x1_00000002), one sprite, two runs.
	std::vector<int32_t> b =
		Buffer( { 1,         2, 2, 1,         Bits( 0 ), Bits( 0 ), Bits( 7 ), Bits( 0 ),  Bits( 90 ),
				  Bits( 0 ), 0, 0, Bits( 0 ), Bits( 0 ), Bits( 3 ), Bits( 0 ), Bits( 45 ), Bits( 0 ),
				  2,         4, 0, 1,         0,         9,         1,         1,          1 } );
	Level level;
	EXPECT_TRUE( ss_detail::ReadLevel( b.data(), level ) );
	EXPECT_EQ( level.log, "P1;O0:4294967298:7:90;O1:0:3:45;R4:0:1;R9:1:1f;" );
}

TEST( DetailPropsWire, ALevelThatRunsOutStopsWithoutReadingPastItsLength )
{
	// Says two objects and holds one.
	std::vector<int32_t> b = Buffer( { 0, 2, 0, 0, Bits( 1 ), Bits( 2 ), Bits( 3 ), 0, 0, 0 } );
	b.push_back( 12345 ); // past the length: must not be read as an object's
	Level level;
	EXPECT_FALSE( ss_detail::ReadLevel( b.data(), level ) );
	EXPECT_EQ( level.log, "P0;O0:0:3:0;" );
}

TEST( DetailPropsWire, AViewThatBuiltHandsOverItsRadiusProxiesAndModelStates )
{
	std::vector<int32_t> b = Buffer(
		{ 1, Bits( 1200 ), 1, 3, 0, 1, 140 | ss_detail::kModelSetAngles, 0, Bits( 180 ), 0, Bits( 0.5f ), 0, 0 } );
	View view;
	float radius = 0;
	EXPECT_TRUE( ss_detail::ReadView( b.data(), view, &radius ) );
	EXPECT_EQ( radius, 1200.0f );
	EXPECT_EQ( view.log, "X3;M0:140a:180:5;" );
}

TEST( DetailPropsWire, AViewThatBuiltNothingSaysSoWhateverItsRadius )
{
	std::vector<int32_t> b = Buffer( { 0, Bits( 1200 ), 0, 0 } );
	View view;
	float radius = -7;
	EXPECT_FALSE( ss_detail::ReadView( b.data(), view, &radius ) );
	EXPECT_EQ( radius, -7.0f );
	EXPECT_EQ( view.log, "" );

	// A radius of 0 is a build.
	std::vector<int32_t> zero = Buffer( { 1, Bits( 0 ), 0, 0 } );
	EXPECT_TRUE( ss_detail::ReadView( zero.data(), view, &radius ) );
	EXPECT_EQ( radius, 0.0f );
}

TEST( DetailPropsWire, APreparedDrawHandsOverItsAlphasAndAnswersItsMaterial )
{
	std::vector<int32_t> b = Buffer( { ss_detail::kMaterialWireframe, 2, 4, 17, 9, 0 } );
	Mesh mesh;
	EXPECT_EQ( ss_detail::ReadPrepare( b.data(), mesh ), (int)ss_detail::kMaterialWireframe );
	EXPECT_EQ( mesh.log, "A4:17;A9:0;" );

	// One alpha short: no draw.
	std::vector<int32_t> shortOne = Buffer( { ss_detail::kMaterialSprite, 2, 4, 17 } );
	EXPECT_EQ( ss_detail::ReadPrepare( shortOne.data(), mesh ), (int)ss_detail::kMaterialNone );
	EXPECT_EQ( ss_detail::ReadPrepare( NULL, mesh ), (int)ss_detail::kMaterialNone );
}

TEST( DetailPropsWire, AnEmitIsCarriedOutCommandByCommandUntilEnd )
{
	const int32_t rgba = 10 | ( 20 << 8 ) | ( 30 << 16 ) | ( 200 << 24 );
	std::vector<int32_t> b = Buffer( { ss_detail::kBegin,
									   2,
									   ss_detail::kVertices,
									   2,
									   Bits( 1 ),
									   Bits( 2 ),
									   Bits( 3 ),
									   rgba,
									   Bits( 0 ),
									   Bits( 0.25f ),
									   Bits( 4 ),
									   Bits( 5 ),
									   Bits( 6 ),
									   rgba,
									   Bits( 0 ),
									   Bits( 0.5f ),
									   ss_detail::kModelAlpha,
									   3,
									   99,
									   ss_detail::kEndDraw,
									   ss_detail::kBegin,
									   -2,
									   ss_detail::kEndDraw,
									   ss_detail::kEnd } );
	Mesh mesh;
	EXPECT_TRUE( ss_detail::RunEmit( b.data(), mesh ) );
	EXPECT_EQ( mesh.log, "B2;V1:10,200:1;V4:10,200:2;A3:99;E;B-2;E;" );
}

TEST( DetailPropsWire, AnEmitWhoseVerticesRunPastItsLengthDrawsNoneOfThem )
{
	std::vector<int32_t> b =
		Buffer( { ss_detail::kBegin, 1, ss_detail::kVertices, 2, Bits( 1 ), Bits( 2 ), Bits( 3 ), 0, 0, 0 } );
	Mesh mesh;
	EXPECT_FALSE( ss_detail::RunEmit( b.data(), mesh ) );
	EXPECT_EQ( mesh.log, "B1;" );

	std::vector<int32_t> unknown = Buffer( { 77 } );
	EXPECT_FALSE( ss_detail::RunEmit( unknown.data(), mesh ) );
	std::vector<int32_t> noEnd = Buffer( { ss_detail::kEndDraw } );
	EXPECT_FALSE( ss_detail::RunEmit( noEnd.data(), mesh ) );
}
