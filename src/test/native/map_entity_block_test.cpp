//========= SourceSharp — the golden table for the map-entity-block parser ====//
//
// The tokeniser the BSP's entity lump is read with moved to C#
// (SourceSharp.Shared/MapEntityBlock.cs), because it is what stood between the
// template store and managed code: every line of it is arithmetic over a byte
// buffer and none of it is engine work.
//
// This is the half of the agreement that faces NATIVE. Every row of
// map_entity_block_cases.txt is recomputed here by the REAL
// game/shared/mapentities_shared.cpp — MapEntity_ParseToken,
// MapEntity_GetNumKeysInEntity, MapEntity_ExtractValue and CEntityMapData —
// compiled into this binary against a one-TU cbase.h shim, and asserted
// against what the file says.
//
// WHY THIS EXISTS AND NOT JUST THE C# TEST. A fixture that stands in for native
// drifts silently: the managed test would agree with the table, the table would
// agree with nothing, and a wrong answer would be checked against itself
// forever. See docs/writing-tests.md, "A fixture that stands in for native must
// be checked against it". The rows here are pinned to the code they were taken
// from, so MapEntityBlockTests.cs on the other side of the table is a real
// comparison rather than a self-portrait.
//
// The five things this file is actually guarding, each of which is a place a
// tidier port would silently disagree:
//
//   * a quoted token runs to its closing quote, so it is the only kind that can
//     contain a space -- and an unterminated one runs to end of data;
//   * `{ } ( ) '` are each a whole token by themselves, mid-word;
//   * a `//` comment is skipped and so is the whitespace after it;
//   * GetNextKey trims trailing spaces off a KEY and not off a value;
//   * SetValue moves the walk cursor by the length difference, which is what
//     lets Templates_ReconnectIOForGroup rewrite a value from inside its own
//     GetNextKey walk without losing its place. That one is behavioural here:
//     the row records the keys the walk still produces AFTER the rewrite.
//
// Run with SOURCESHARP_EMIT_CASES=1 in the environment to print the rows this
// code computes, which is how the table was made and how it is re-made when a
// row is added.
//
//=============================================================================//

#include "gtest/gtest.h"

#include "mapentities_shared.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

int MapEntity_GetNumKeysInEntity( const char *pEntData );

namespace
{

//-----------------------------------------------------------------------------
// The table, found by walking up from wherever the test binary was run. Not an
// absolute path and not a compiled-in copy: a compiled-in copy would be a
// SECOND record of the answers and the two would part company, which is the
// failure this whole file exists to prevent.
//-----------------------------------------------------------------------------
std::string FindCases()
{
	const char *kRelative = "sourcesharp/test/map_entity_block_cases.txt";
	std::string prefix;

	for ( int i = 0; i < 8; i++ )
	{
		std::string candidate = prefix + kRelative;
		if ( FILE *f = fopen( candidate.c_str(), "r" ) )
		{
			fclose( f );
			return candidate;
		}
		prefix += "../";
	}

	return std::string();
}

//-----------------------------------------------------------------------------
// The escape scheme. Map text has quotes and newlines in it and the table is
// line-oriented and tab-separated, so both have to be spelled.
//
//   \n newline   \t tab   \q "   \e 0x1b (VMF_IOPARAM_STRING_DELIMITER)
//   \s ~ (the stream separator)   \\ backslash
//-----------------------------------------------------------------------------
std::string Unescape( const std::string &in )
{
	std::string out;
	for ( size_t i = 0; i < in.size(); i++ )
	{
		if ( in[i] != '\\' || i + 1 >= in.size() )
		{
			out += in[i];
			continue;
		}

		switch ( in[++i] )
		{
		case 'n':  out += '\n'; break;
		case 't':  out += '\t'; break;
		case 'q':  out += '"';  break;
		case 'e':  out += '\x1b'; break;
		case 's':  out += '~';  break;
		case '\\': out += '\\'; break;
		default:   out += in[i]; break;
		}
	}
	return out;
}

std::string Escape( const std::string &in )
{
	std::string out;
	for ( size_t i = 0; i < in.size(); i++ )
	{
		switch ( in[i] )
		{
		case '\n': out += "\\n"; break;
		case '\t': out += "\\t"; break;
		case '"':  out += "\\q"; break;
		case '\x1b': out += "\\e"; break;
		case '~':  out += "\\s"; break;
		case '\\': out += "\\\\"; break;
		default:   out += in[i]; break;
		}
	}
	return out;
}

std::vector< std::string > Split( const std::string &line, char sep )
{
	std::vector< std::string > out;
	std::string cur;
	for ( char c : line )
	{
		if ( c == sep )
		{
			out.push_back( cur );
			cur.clear();
			continue;
		}
		cur += c;
	}
	out.push_back( cur );
	return out;
}

struct Row
{
	std::string kind;
	std::vector< std::string > field;   // already unescaped
	int         line;

	// A row whose LAST expected field is empty loses it to the tab-stripping
	// every editor does, so a missing trailing field reads as "" rather than
	// running off the end. Anything the row genuinely has to carry is asserted
	// by arity before this is used.
	const std::string &At( size_t i ) const
	{
		static const std::string kEmpty;
		return i < field.size() ? field[i] : kEmpty;
	}
};

std::vector< Row > LoadRows()
{
	std::vector< Row > rows;
	const std::string path = FindCases();
	if ( path.empty() )
		return rows;

	FILE *f = fopen( path.c_str(), "r" );
	if ( !f )
		return rows;

	char buf[8192];
	int lineNo = 0;
	while ( fgets( buf, sizeof( buf ), f ) )
	{
		lineNo++;
		std::string line( buf );
		while ( !line.empty() && ( line.back() == '\n' || line.back() == '\r' ) )
			line.pop_back();

		if ( line.empty() || line[0] == '#' )
			continue;

		std::vector< std::string > parts = Split( line, '\t' );
		Row row;
		row.kind = parts[0];
		row.line = lineNo;
		for ( size_t i = 1; i < parts.size(); i++ )
			row.field.push_back( Unescape( parts[i] ) );

		rows.push_back( row );
	}

	fclose( f );
	return rows;
}

bool Emitting()
{
	const char *p = getenv( "SOURCESHARP_EMIT_CASES" );
	return p && p[0] && p[0] != '0';
}

//-----------------------------------------------------------------------------
// The four things the table records, each computed by the real parser.
//-----------------------------------------------------------------------------

// Every token the tokeniser produces, in order, up to end of data.
std::string TokenStream( const std::string &block )
{
	char token[MAPKEY_MAXLENGTH];
	const char *at = block.c_str();
	std::string out;
	int guard = 0;

	while ( at && guard++ < 512 )
	{
		at = MapEntity_ParseToken( at, token );
		if ( !at && token[0] == 0 )
			break;

		if ( !out.empty() )
			out += '~';
		out += token;

		if ( !at )
			break;
	}

	return out;
}

// Every key/value pair a walk produces, as "key=value".
std::string WalkStream( CEntityMapData &data )
{
	char keyName[MAPKEY_MAXLENGTH];
	char value[MAPKEY_MAXLENGTH];
	std::string out;

	if ( !data.GetFirstKey( keyName, value ) )
		return out;

	do
	{
		if ( !out.empty() )
			out += '~';
		out += keyName;
		out += '=';
		out += value;
	}
	while ( data.GetNextKey( keyName, value ) );

	return out;
}

} // namespace

//-----------------------------------------------------------------------------

TEST( MapEntityBlock, TheTableIsThere )
{
	ASSERT_FALSE( FindCases().empty() )
		<< "sourcesharp/test/map_entity_block_cases.txt not found from the cwd. "
		   "The table is the record; without it neither side of the agreement "
		   "is checked.";

	EXPECT_GE( LoadRows().size(), 20u )
		<< "the table has lost rows -- it is the only record of what native "
		   "answers for these blocks.";
}

//-----------------------------------------------------------------------------
// THE SPAWN ORDER -- plan_entity.md step 2d.
//
// The loop at the top of MapEntity_ParseAllEntities (game/server/mapentities.cpp
// :609-621) with the creation taken out of it: parse the opening brace, read the
// classname, skip to the matching close, go round again. That loop is what
// decided the order every entity in every map was made in, and until step 2d it
// was the only thing that could -- the cursor lived in C++, so every key of every
// entity had to be handed UP one at a time.
//
// SourceSharp.Shared/MapEntityBlock.SplitBlocks is that arithmetic in C# now, and
// this is what stops it from quietly meaning something else. Recomputed here with
// the REAL MapEntity_ParseToken / _SkipToNextEntity / _ExtractValue.
//
// The `?` is a block with no classname. Stock calls Error() there, which is not a
// value this table can carry; what the row is recording is WHICH BLOCK, and the
// blocks either side of it are the point.
//-----------------------------------------------------------------------------
std::string SpawnOrder( const std::string &lump )
{
	std::string out;

	const char *pMapData = lump.c_str();
	char work[MAPKEY_MAXLENGTH];

	for ( ; true; pMapData = MapEntity_SkipToNextEntity( pMapData, work ) )
	{
		if ( !pMapData )
			break;

		char token[MAPKEY_MAXLENGTH];
		pMapData = MapEntity_ParseToken( pMapData, token );

		if ( !pMapData )
			break;

		// Stock Error()s here and then continues, which cannot both happen.
		// Stopping is the half that is reachable, and it is what the managed
		// splitter does.
		if ( token[0] != '{' || token[1] != 0 )
			break;

		char className[MAPKEY_MAXLENGTH];
		if ( !MapEntity_ExtractValue( pMapData, "classname", className ) )
			strcpy( className, "?" );

		if ( !out.empty() )
			out += '~';
		out += className;
	}

	return out;
}

TEST( MapEntityBlock, EveryTokenRowIsWhatTheRealTokeniserProduces )
{
	int checked = 0;

	for ( const Row &row : LoadRows() )
	{
		if ( row.kind != "tokens" )
			continue;

		ASSERT_GE( row.field.size(), 2u ) << "line " << row.line;

		const std::string got = TokenStream( row.At(1) );

		if ( Emitting() )
			printf( "tokens\t%s\t%s\t%s\n", row.At(0).c_str(),
			        Escape( row.At(1) ).c_str(), Escape( got ).c_str() );

		EXPECT_EQ( got, row.At(2) )
			<< "row '" << row.At(0) << "' (line " << row.line << ") no longer "
			   "describes MapEntity_ParseToken.";
		checked++;
	}

	EXPECT_GT( checked, 0 ) << "no token rows ran";
}

TEST( MapEntityBlock, EveryKeyCountRowIsWhatTheRealCounterProduces )
{
	int checked = 0;

	for ( const Row &row : LoadRows() )
	{
		if ( row.kind != "keys" )
			continue;

		ASSERT_GE( row.field.size(), 3u ) << "line " << row.line;

		const int got = MapEntity_GetNumKeysInEntity( row.At(1).c_str() );

		if ( Emitting() )
			printf( "keys\t%s\t%s\t%d\n", row.At(0).c_str(),
			        Escape( row.At(1) ).c_str(), got );

		EXPECT_EQ( got, atoi( row.At(2).c_str() ) )
			<< "row '" << row.At(0) << "' (line " << row.line << ") no longer "
			   "describes MapEntity_GetNumKeysInEntity.";
		checked++;
	}

	EXPECT_GT( checked, 0 ) << "no key-count rows ran";
}

TEST( MapEntityBlock, EveryExtractRowIsWhatTheRealExtractorProduces )
{
	int checked = 0;

	for ( const Row &row : LoadRows() )
	{
		if ( row.kind != "extract" )
			continue;

		ASSERT_GE( row.field.size(), 4u ) << "line " << row.line;

		char value[MAPKEY_MAXLENGTH];
		value[0] = 0;
		const bool found = MapEntity_ExtractValue( row.At(1).c_str(),
		                                           row.At(2).c_str(), value );

		if ( Emitting() )
			printf( "extract\t%s\t%s\t%s\t%d\t%s\n", row.At(0).c_str(),
			        Escape( row.At(1) ).c_str(), Escape( row.At(2) ).c_str(),
			        found ? 1 : 0, Escape( found ? value : "" ).c_str() );

		EXPECT_EQ( found ? 1 : 0, atoi( row.At(3).c_str() ) )
			<< "row '" << row.At(0) << "' (line " << row.line << ")";
		if ( found )
		{
			EXPECT_EQ( std::string( value ), row.At(4) )
				<< "row '" << row.At(0) << "' (line " << row.line << ")";
		}
		checked++;
	}

	EXPECT_GT( checked, 0 ) << "no extract rows ran";
}

TEST( MapEntityBlock, EveryWalkRowIsWhatTheRealCursorProduces )
{
	int checked = 0;

	for ( const Row &row : LoadRows() )
	{
		if ( row.kind != "walk" )
			continue;

		ASSERT_GE( row.field.size(), 2u ) << "line " << row.line;

		std::vector< char > buf( row.At(1).begin(), row.At(1).end() );
		buf.push_back( 0 );

		CEntityMapData data( buf.data(), (int)buf.size() );
		const std::string got = WalkStream( data );

		if ( Emitting() )
			printf( "walk\t%s\t%s\t%s\n", row.At(0).c_str(),
			        Escape( row.At(1) ).c_str(), Escape( got ).c_str() );

		EXPECT_EQ( got, row.At(2) )
			<< "row '" << row.At(0) << "' (line " << row.line << ") no longer "
			   "describes CEntityMapData::GetFirstKey/GetNextKey.";
		checked++;
	}

	EXPECT_GT( checked, 0 ) << "no walk rows ran";
}

//-----------------------------------------------------------------------------
// The row that matters most: a value rewritten from INSIDE the walk.
//
// Templates_ReconnectIOForGroup does exactly this -- it walks a template's keys
// and calls SetValue on the one it is standing on -- and it only works because
// SetValue moves the cursor by the length difference. A port that rebuilt the
// buffer and left the cursor alone would produce a walk that repeats or skips a
// key, and every assertion about the RESULTING TEXT would still pass.
//
// So the row records the text AND the keys the walk still produces afterwards.
//-----------------------------------------------------------------------------
TEST( MapEntityBlock, EveryRewriteRowIsWhatTheRealSetValueProduces )
{
	int checked = 0;

	for ( const Row &row : LoadRows() )
	{
		if ( row.kind != "walkset" )
			continue;

		ASSERT_GE( row.field.size(), 6u ) << "line " << row.line;

		const int capacity = atoi( row.At(2).c_str() );
		std::vector< char > buf( (size_t)capacity, 0 );
		memcpy( buf.data(), row.At(1).data(),
		        std::min( (size_t)capacity - 1, row.At(1).size() ) );

		CEntityMapData data( buf.data(), capacity );

		const std::string atKey    = row.At(3);
		const std::string newValue = row.At(4);

		char keyName[MAPKEY_MAXLENGTH];
		char value[MAPKEY_MAXLENGTH];
		std::string rest;
		int instances = 0;

		if ( data.GetFirstKey( keyName, value ) )
		{
			do
			{
				if ( atKey == keyName )
				{
					char mutableValue[MAPKEY_MAXLENGTH];
					strncpy( mutableValue, newValue.c_str(), sizeof( mutableValue ) - 1 );
					mutableValue[sizeof( mutableValue ) - 1] = 0;
					data.SetValue( keyName, mutableValue, instances );
					instances++;
					continue;
				}

				if ( !rest.empty() )
					rest += '~';
				rest += keyName;
				rest += '=';
				rest += value;
			}
			while ( data.GetNextKey( keyName, value ) );
		}

		const std::string text( buf.data() );

		if ( Emitting() )
			printf( "walkset\t%s\t%s\t%d\t%s\t%s\t%s\t%s\n", row.At(0).c_str(),
			        Escape( row.At(1) ).c_str(), capacity,
			        Escape( atKey ).c_str(), Escape( newValue ).c_str(),
			        Escape( rest ).c_str(), Escape( text ).c_str() );

		EXPECT_EQ( rest, row.At(5) )
			<< "row '" << row.At(0) << "' (line " << row.line << "): the keys "
			   "the walk produces after the rewrite changed -- that is the cursor "
			   "adjustment, and it is what the reconnect pass rides on.";
		EXPECT_EQ( text, row.At(6) )
			<< "row '" << row.At(0) << "' (line " << row.line << "): the "
			   "rewritten text changed.";
		checked++;
	}

	EXPECT_GT( checked, 0 ) << "no rewrite rows ran";
}

//-----------------------------------------------------------------------------
// The order rows. See SpawnOrder above for what they are.
//-----------------------------------------------------------------------------
TEST( MapEntityBlock, EverySpawnOrderRowIsWhatTheRealParseLoopProduces )
{
	int checked = 0;

	for ( const Row &row : LoadRows() )
	{
		if ( row.kind != "spawnorder" )
			continue;

		ASSERT_GE( row.field.size(), 2u ) << "line " << row.line;

		const std::string got = SpawnOrder( row.At(1) );

		if ( Emitting() )
			printf( "spawnorder\t%s\t%s\t%s\n", row.At(0).c_str(),
			        Escape( row.At(1) ).c_str(), Escape( got ).c_str() );

		EXPECT_EQ( got, row.At(2) )
			<< "row '" << row.At(0) << "' (line " << row.line << ") no longer "
			   "describes the block walk MapEntity_ParseAllEntities makes.";
		checked++;
	}

	EXPECT_GT( checked, 0 ) << "no spawn-order rows ran";
}
