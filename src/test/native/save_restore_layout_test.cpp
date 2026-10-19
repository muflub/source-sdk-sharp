//========= SourceSharp =====================================================//
//
// THE SAVE STRUCTS THE MANAGED SAVE CODEC READS BY LAYOUT
// (plan_server_native_v2 T4c, src/server/interop/SaveGameSession.cs).
//
// One case per offset of SaveRestoreLayout: the number here against the C++
// declaration, the same number in SaveGameSessionTests against the C#
// constant. tools/abi-parity holds these declarations to upstream's; this
// holds the managed numbers to these declarations.
//
// CSaveRestoreSegment's members are private, so its offsets are found through
// its own accessors: put a value in through the class, find it at the offset.
// That is a stronger check than offsetof would be, because it is the offset
// the engine's inline accessors use.
//
//=============================================================================//

#include "datamap.h"
#include "engineabi/saverestore.h"

#include <stddef.h>
#include <string.h>

#include "gtest/gtest.h"

namespace
{

template < typename T >
T At( const CSaveRestoreData &data, size_t nOffset )
{
	T value;
	memcpy( &value, (const char *)&data + nOffset, sizeof( T ) );
	return value;
}

size_t LevelInfoAt( const CSaveRestoreData &data, const void *pMember )
{
	return (size_t)( (const char *)pMember - (const char *)&data );
}

struct CPlaced
{
	char buffer[ 64 ];
	char *tokens[ 7 ];
	CSaveRestoreData data;

	CPlaced()
	{
		data.Init( buffer, (int)sizeof( buffer ) );
		data.InitSymbolTable( tokens, 7 );
		data.MoveCurPos( 5 );
	}
};

} // namespace

TEST( SaveRestoreLayout, BaseDataIsAt0 )
{
	CPlaced p;
	EXPECT_EQ( p.buffer, At< char * >( p.data, 0 ) );
}

TEST( SaveRestoreLayout, CurrentDataIsAt8 )
{
	CPlaced p;
	EXPECT_EQ( p.data.AccessCurPos(), At< char * >( p.data, 8 ) );
	EXPECT_EQ( p.buffer + 5, At< char * >( p.data, 8 ) );
}

TEST( SaveRestoreLayout, SizeIsAt16 )
{
	CPlaced p;
	EXPECT_EQ( 5, At< int >( p.data, 16 ) );
	EXPECT_EQ( p.data.GetCurPos(), At< int >( p.data, 16 ) );
}

TEST( SaveRestoreLayout, BufferSizeIsAt20 )
{
	CPlaced p;
	EXPECT_EQ( 64, At< int >( p.data, 20 ) );
	EXPECT_EQ( 59, p.data.BytesAvailable() );
}

TEST( SaveRestoreLayout, TokenCountIsAt24 )
{
	CPlaced p;
	EXPECT_EQ( 7, At< int >( p.data, 24 ) );
}

TEST( SaveRestoreLayout, TokensIsAt32 )
{
	CPlaced p;
	EXPECT_EQ( p.tokens, At< char ** >( p.data, 32 ) );
}

TEST( SaveRestoreLayout, ConnectionCountIsAt40 )
{
	CPlaced p;
	EXPECT_EQ( 40u, LevelInfoAt( p.data, &p.data.levelInfo.connectionCount ) );
}

TEST( SaveRestoreLayout, UseLandmarkIsAt1456 )
{
	CPlaced p;
	EXPECT_EQ( 1456u, LevelInfoAt( p.data, &p.data.levelInfo.fUseLandmark ) );
}

TEST( SaveRestoreLayout, LandmarkOffsetIsAt1480 )
{
	CPlaced p;
	EXPECT_EQ( 1480u, LevelInfoAt( p.data, &p.data.levelInfo.vecLandmarkOffset ) );
}

TEST( SaveRestoreLayout, TimeIsAt1492 )
{
	CPlaced p;
	EXPECT_EQ( 1492u, LevelInfoAt( p.data, &p.data.levelInfo.time ) );
	p.data.levelInfo.time = 42.5f;
	EXPECT_EQ( 42.5f, At< float >( p.data, 1492 ) );
	EXPECT_EQ( 42.5f, p.data.GetBaseTime() );
}

TEST( SaveRestoreLayout, TheSaveBlockIs1616Bytes )
{
	EXPECT_EQ( 1616u, sizeof( CSaveRestoreData ) );
}

TEST( TypeDescriptionLayout, FieldTypeIsAt0 )
{
	EXPECT_EQ( 0u, offsetof( typedescription_t, fieldType ) );
}

TEST( TypeDescriptionLayout, FieldNameIsAt8 )
{
	EXPECT_EQ( 8u, offsetof( typedescription_t, fieldName ) );
}

TEST( TypeDescriptionLayout, FieldOffsetIsAt16 )
{
	EXPECT_EQ( 16u, offsetof( typedescription_t, fieldOffset ) );
}

TEST( TypeDescriptionLayout, FieldSizeIsAt24AndTwoBytes )
{
	EXPECT_EQ( 24u, offsetof( typedescription_t, fieldSize ) );
	EXPECT_EQ( 2u, sizeof( ( (typedescription_t *)0 )->fieldSize ) );
}
