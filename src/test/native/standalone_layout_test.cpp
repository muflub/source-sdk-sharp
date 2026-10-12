//========= SourceSharp — CStandaloneEntity has ONE layout, for everybody ====//
//
// WHY THIS FILE EXISTS.
//
// CStandaloneEntity's members are defined out of line in THREE translation
// units — 49 in shell_standalone_base.cpp, 9 in shell_standalone_hierarchy.cpp
// and one, GetServerClass, in shell_standalone.cpp — from shell_standalone.h,
// which none of them is the swap partner of. Step 5's shard manifest handed the
// first two to DIFFERENT AGENTS, T3 and T2, and every instrument that was
// supposed to notice was blind to it:
//
//   * `no class spans two shards` is keyed off gcc SCOPE LINES, and the
//     diagnostics put the class in shell_standalone_base.cpp alone.
//   * `no paired header is claimed by two shards` counts CLAIMS, and neither
//     shard claimed shell_standalone.h.
//
// (Both are fixed now: s5_source.py measures it from the source and
// s5_verify.py's ninth invariant reports it. That is the model. This file is
// the C++ side of the same question.)
//
// The split did no damage, and the reason it did none is worth pinning rather
// than re-deriving: neither shard touched the header, so no member moved. But
// "no member moved" is not the invariant. THE INVARIANT IS THAT EVERY TU SEES
// THE SAME CLASS, and the T shards' entire edit vocabulary was
// `#ifndef SS_CBASEENTITY_ALIAS`. One of those placed around a MEMBER, by
// either shard, in a header neither owns, and the two halves of the port
// disagree about sizeof — silently:
//
//   * the blob lives at `(unsigned char *)this + SourceSharp_StandaloneBlobOffset()`,
//     one non-inline function, one number for the whole process. It would not
//     move. The MEMBERS under it would.
//   * SourceSharp_EntityGetDataBlob answers the standalone arm FIRST, through
//     NON-VIRTUAL DataBlob() and BlobBytes(), so there is no dispatch anywhere
//     to disagree and nothing to report.
//   * a C# write through that pointer would land in a member on the TU that
//     compiled the class short, and nothing between there and the heap looks.
//
// Nothing in the build says a word about any of that. `Assert( nBytes <=
// SourceSharp_StandaloneBlobOffset() )` in operator new is the only runtime
// guard and it is debug-only, one-directional, and about the SUBCLASS size,
// not about two TUs disagreeing.
//
// SO THE CHECKS ARE TEXTUAL, and they are checks a compiler structurally
// cannot make: an ODR violation between two TUs is not diagnosable, which is
// the whole reason it is worth a test.
//
// This file is the same shape as shim_underive_verdict_test.cpp, which already
// pins the blob offset's FORMULA, the Assert, and the non-virtuality of the two
// accessors. It does not pin either of the two facts below.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <sys/stat.h>

#include <fstream>
#include <string>
#include <vector>

namespace
{

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[ i ] ) + "/game";
		const std::string pub = std::string( kCandidates[ i ] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode )
			&& stat( pub.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
		{
			return kCandidates[ i ];
		}
	}
	return NULL;
}

std::string Read( const std::string &strRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + strRelative ).c_str() );
	if ( !file.is_open() )
		return std::string();

	std::string all, line;
	while ( std::getline( file, line ) )
	{
		if ( !line.empty() && line[ line.size() - 1 ] == '\r' )
			line.erase( line.size() - 1 );
		all += line;
		all += '\n';
	}
	return all;
}

const char *const kStandaloneHeader = "sourcesharp/game/server/shell_standalone.h";

// The whole `class CStandaloneEntity ... };` body, brace-counted.
//
// Brace-counted rather than "to the next line starting with };", because the
// class contains nested types and the cheaper rule stops at the first of them.
// Comments and string literals are skipped, because a `{` inside either is not
// a brace — the ownership model this file is the C++ half of had a phantom in
// it for exactly that reason, a `/* */` block read as a definition.
std::string ClassBody( const std::string &strFile, const std::string &strDecl )
{
	const size_t start = strFile.find( strDecl );
	if ( start == std::string::npos )
		return std::string();

	const size_t open = strFile.find( '{', start );
	if ( open == std::string::npos )
		return std::string();

	int nDepth = 0;
	bool bBlock = false, bLine = false, bStr = false, bChr = false;
	for ( size_t i = open; i < strFile.size(); i++ )
	{
		const char c = strFile[ i ];
		const char n = ( i + 1 < strFile.size() ) ? strFile[ i + 1 ] : '\0';

		if ( bBlock ) { if ( c == '*' && n == '/' ) { bBlock = false; i++; } continue; }
		if ( bLine )  { if ( c == '\n' ) bLine = false; continue; }
		if ( bStr )   { if ( c == '\\' ) i++; else if ( c == '"' ) bStr = false; continue; }
		if ( bChr )   { if ( c == '\\' ) i++; else if ( c == '\'' ) bChr = false; continue; }

		if ( c == '/' && n == '*' ) { bBlock = true; i++; continue; }
		if ( c == '/' && n == '/' ) { bLine = true; i++; continue; }
		if ( c == '"' )  { bStr = true; continue; }
		if ( c == '\'' ) { bChr = true; continue; }

		if ( c == '{' )
			nDepth++;
		else if ( c == '}' )
		{
			nDepth--;
			if ( nDepth == 0 )
				return strFile.substr( open, i - open + 1 );
		}
	}
	return std::string();
}

// Every `#if`, `#ifdef`, `#ifndef`, `#else`, `#elif` and `#endif` at the start
// of a line, with the line it is on, so a failure names the directive.
std::vector< std::string > Conditionals( const std::string &strBody )
{
	std::vector< std::string > out;
	size_t i = 0;
	while ( i < strBody.size() )
	{
		size_t eol = strBody.find( '\n', i );
		if ( eol == std::string::npos )
			eol = strBody.size();

		std::string line = strBody.substr( i, eol - i );
		size_t j = line.find_first_not_of( " \t" );
		if ( j != std::string::npos && line[ j ] == '#' )
		{
			const std::string rest = line.substr( j + 1 );
			size_t k = rest.find_first_not_of( " \t" );
			if ( k != std::string::npos )
			{
				const std::string word = rest.substr( k, rest.find_first_of( " \t", k ) - k );
				if ( word == "if" || word == "ifdef" || word == "ifndef"
					|| word == "else" || word == "elif" || word == "endif" )
				{
					out.push_back( line );
				}
			}
		}
		i = eol + 1;
	}
	return out;
}

//-----------------------------------------------------------------------------
// FACT 1. Every TU that includes shell_standalone.h sees the SAME
//         CStandaloneEntity, whatever any shard did to any .cpp.
//
// This is what makes the T2/T3 split harmless, and it is one `#ifndef` away
// from not being true. A conditional inside this class body is an ODR
// violation between the server's own translation units, and the C++ standard
// requires no diagnostic for it.
//
// Guarding a member with SS_CBASEENTITY_ALIAS is the specific way it would
// happen -- the macro is defined per configuration, not per TU, so a whole
// build would agree today and the file would still be a trap the day anything
// includes it under a different command line. It is refused outright rather
// than tolerated when consistent, because "consistent so far" is not a
// property a test can check and this one can.
//-----------------------------------------------------------------------------
TEST( StandaloneLayout, ClassBodyCarriesNoPreprocessorConditional )
{
	const std::string header = Read( kStandaloneHeader );
	ASSERT_FALSE( header.empty() )
		<< kStandaloneHeader << " could not be read; every check in this file "
		   "would otherwise pass vacuously.";

	const std::string body = ClassBody( header, "class CStandaloneEntity" );
	ASSERT_FALSE( body.empty() )
		<< "could not find the body of class CStandaloneEntity in "
		<< kStandaloneHeader << ". If the class was renamed, this file is "
		   "measuring nothing -- fix it rather than deleting it.";

	// Non-vacuity: the body really is the class, not two lines of it. The class
	// is ~880 lines; anything under a few thousand characters means ClassBody
	// stopped early and every check below is about a fragment.
	EXPECT_GT( body.size(), size_t( 8000 ) )
		<< "the extracted class body is only " << body.size() << " bytes. "
		   "ClassBody stopped early, so the absence it reports is an absence "
		   "from a fragment.";

	const std::vector< std::string > cond = Conditionals( body );
	std::string strFound;
	for ( size_t i = 0; i < cond.size(); i++ )
		strFound += "\n    " + cond[ i ];

	EXPECT_TRUE( cond.empty() )
		<< "class CStandaloneEntity's body carries " << cond.size()
		<< " preprocessor conditional(s):" << strFound
		<< "\n\nEvery TU including this header must see the SAME class. Its "
		   "members are defined out of line in three .cpp files that step 5's "
		   "manifest handed to two different agents, and the blob lives at a "
		   "fixed offset past those members -- SourceSharp_EntityGetDataBlob "
		   "reaches it through NON-VIRTUAL DataBlob()/BlobBytes(), so two TUs "
		   "disagreeing about sizeof is a C# write into a member with nothing "
		   "anywhere to report it. If a member genuinely has to be conditional, "
		   "the conditional belongs around the WHOLE class or in a separate "
		   "type -- not around a field.";
}

//-----------------------------------------------------------------------------
// FACT 2. The blob offset covers every shell that will ever stand in front of
//         a blob.
//
// SourceSharp_StandaloneBlobOffset() is
// ALIGN_VALUE( MAX( sizeof(CStandaloneEntity), sizeof(CStandalonePlayer) ), 16 )
// -- shim_underive_verdict_test.cpp pins that formula. What it does not pin is
// that those two are the ONLY shells: the offset is one number for the whole
// hierarchy, so a second subclass of CStandaloneEntity would allocate its blob
// ON TOP of its own members with only operator new's debug-only Assert to say
// so.
//
// THE CHAIN LOST A LINK AT STEP 1f. It was CStandaloneEntity <-
// CStandaloneScriptedEntity <- CStandalonePlayer; the middle class held
// thirteen relays into thirteen ABI slots, and plan_entity.md §3.4 replaced
// those with one Dispatch slot and an op number, so the relays are four lines
// each and live on the base (§3.1: one shell per side, final). What is left is
// CStandaloneEntity <- CStandalonePlayer, and because a derived class is never
// smaller than its base, MAX over the two covers both. Add a THIRD off the side
// and it does not -- which is the population this fact counts, and the reason
// it is a count and not a list.
//
// This reads the headers rather than sizeof(), deliberately: the gtests in
// this tree are engine-free and cannot include shell_standalone.h at all. The
// LIVE number is printed by the server at tier registration --
// "[SourceSharp] standalone: blob offset %d (entity shell %d, player shell %d)"
// -- and that is what checks the arithmetic. This checks the population.
//-----------------------------------------------------------------------------
TEST( StandaloneLayout, TheBlobOffsetCoversEveryShellOnTheHierarchy )
{
	static const char *const kFiles[] = {
		"sourcesharp/game/server/shell_standalone.h",
		"sourcesharp/game/server/shell_standalone_class.h",
		"sourcesharp/game/server/shell_standalone_player.h",
	};

	// Anything deriving from CStandaloneEntity must be CStandalonePlayer. The
	// two of them are what the offset's MAX is taken over.
	static const char *const kAllowed[] = {
		"class CStandalonePlayer : public CStandaloneEntity",
	};

	size_t nSeen = 0;
	for ( size_t i = 0; i < sizeof( kFiles ) / sizeof( kFiles[ 0 ] ); i++ )
	{
		const std::string body = Read( kFiles[ i ] );
		ASSERT_FALSE( body.empty() ) << kFiles[ i ] << " could not be read.";

		for ( size_t j = 0; j < sizeof( kAllowed ) / sizeof( kAllowed[ 0 ] ); j++ )
		{
			if ( body.find( kAllowed[ j ] ) != std::string::npos )
				nSeen++;
		}
	}

	EXPECT_EQ( nSeen, size_t( 1 ) )
		<< "the derivation the blob offset is sized over is no longer spelled "
		   "the way this test looks for it. It is measuring nothing until that "
		   "is fixed -- do not delete it.";

	// The population itself: every `public CStandaloneEntity` in the SERVER
	// tree, counted. `public CStandaloneScriptedEntity` was the second base
	// this counted and is not a type any more; a reader who reintroduces it has
	// reintroduced the class §3.4 deleted, and that is a change to the plan
	// rather than an addition to this list.
	static const char *const kBases[] = {
		"public CStandaloneEntity",
	};
	static const char *const kSearch[] = {
		"sourcesharp/game/server/shell_standalone.h",
		"sourcesharp/game/server/shell_standalone_class.h",
		"sourcesharp/game/server/shell_standalone_player.h",
		"sourcesharp/game/server/shell_standalone_base.cpp",
		"sourcesharp/game/server/shell_standalone_hierarchy.cpp",
		"sourcesharp/game/server/shell_standalone_class.cpp",
		"sourcesharp/game/server/shell_dispatch.cpp",
		"sourcesharp/game/server/shell_standalone_player.cpp",
		"sourcesharp/game/server/shell_standalone.cpp",
		"sourcesharp/game/server/shell_scripted.cpp",
		"sourcesharp/game/server/shell_entityapi.cpp",
	};

	int nDerivations = 0;
	std::string strWhere;
	for ( size_t i = 0; i < sizeof( kSearch ) / sizeof( kSearch[ 0 ] ); i++ )
	{
		const std::string body = Read( kSearch[ i ] );
		ASSERT_FALSE( body.empty() ) << kSearch[ i ] << " could not be read.";

		for ( size_t j = 0; j < sizeof( kBases ) / sizeof( kBases[ 0 ] ); j++ )
		{
			size_t at = body.find( kBases[ j ] );
			while ( at != std::string::npos )
			{
				nDerivations++;
				strWhere += std::string( "\n    " ) + kSearch[ i ] + "  "
					+ kBases[ j ];
				at = body.find( kBases[ j ], at + 1 );
			}
		}
	}

	EXPECT_EQ( nDerivations, 1 )
		<< "the standalone hierarchy has " << nDerivations << " derivation(s), "
		   "not the one the blob offset is sized over:" << strWhere
		<< "\n\nSourceSharp_StandaloneBlobOffset() is ONE number for the whole "
		   "hierarchy -- MAX(sizeof(CStandaloneEntity), "
		   "sizeof(CStandalonePlayer)) aligned to 16. A shell outside that "
		   "pair and larger than both allocates its blob on top of its own "
		   "members, and the only thing that would say so is operator new's "
		   "Assert, which is debug-only. Either add the new shell to the MAX in "
		   "shell_standalone_class.cpp, or add its file here and say why it "
		   "cannot be the largest.";
}


//-----------------------------------------------------------------------------
// Everything below is step 1b: the shell reads the engine block, and the block
// is the only place these values live.
//-----------------------------------------------------------------------------

// The class body with comments and string literals removed.
//
// EVERY CHECK BELOW NEEDS THIS AND NONE OF THE ONES ABOVE DID. What they look
// for is a member NAME, and the header talks about the members it no longer has
// -- "m_vecOrigin and m_nModelIndex were public for one reason" is a sentence
// whose whole job is to say the field is gone, and a raw substring search reads
// it as the field being present. A check that failed on its own explanation
// would be deleted within a week, and the field would come back with it.
std::string CodeOnly( const std::string &strText )
{
	std::string out;
	out.reserve( strText.size() );

	bool bBlock = false, bLine = false, bStr = false, bChr = false;
	for ( size_t i = 0; i < strText.size(); i++ )
	{
		const char c = strText[ i ];
		const char n = ( i + 1 < strText.size() ) ? strText[ i + 1 ] : '\0';

		if ( bBlock ) { if ( c == '*' && n == '/' ) { bBlock = false; i++; } continue; }
		if ( bLine )  { if ( c == '\n' ) { bLine = false; out += c; } continue; }

		if ( !bStr && !bChr )
		{
			if ( c == '/' && n == '*' ) { bBlock = true; i++; continue; }
			if ( c == '/' && n == '/' ) { bLine = true; i++; continue; }
		}

		if ( bStr )      { if ( c == '\\' ) { out += c; i++; if ( i < strText.size() ) out += strText[i]; continue; } if ( c == '"' ) bStr = false; }
		else if ( bChr ) { if ( c == '\\' ) { out += c; i++; if ( i < strText.size() ) out += strText[i]; continue; } if ( c == '\'' ) bChr = false; }
		else if ( c == '"' )  bStr = true;
		else if ( c == '\'' ) bChr = true;

		out += c;
	}

	return out;
}

bool IsIdentChar( char c )
{
	return ( c >= 'a' && c <= 'z' ) || ( c >= 'A' && c <= 'Z' )
		|| ( c >= '0' && c <= '9' ) || c == '_';
}

// A WHOLE-WORD find. m_vecMins is a prefix of nothing here, but m_nSolidType is
// a prefix of nothing only by luck and m_vecOrigin is a substring of
// m_vecOriginPrev the day somebody writes one.
bool ContainsWord( const std::string &strHay, const std::string &strWord )
{
	size_t at = strHay.find( strWord );
	while ( at != std::string::npos )
	{
		const bool bLeft  = ( at == 0 ) || !IsIdentChar( strHay[ at - 1 ] );
		const size_t end  = at + strWord.size();
		const bool bRight = ( end >= strHay.size() ) || !IsIdentChar( strHay[ end ] );

		if ( bLeft && bRight )
			return true;

		at = strHay.find( strWord, at + 1 );
	}

	return false;
}

// THE GENERATED HEADER AND NOT engine_block.h, because that is where the block
// is DECLARED. engine_block.h used to declare EngineBlockHead_t and its tails
// by hand beside the C# declaration they mirrored; the structs are emitted from
// the C# side now and engine_block.h is the prose and one #include. Reading it
// here would search a file with no field names in it and every row below would
// report "EngineBlock_t no longer declares X" about a block that declares all
// of them.
const char *const kEngineBlockHeader =
	"sourcesharp/game/shared/engine_block_generated.h";

// The thirteen values that used to be members of CStandaloneEntity and are
// fields of the engine block, paired with the block field each became.
//
// THE PAIRING IS THE POINT rather than the absence. "shell_standalone.h does
// not contain m_vecOrigin" is true of a header that never had an origin at all;
// what this file has to say is that the DUPLICATION is gone -- that the block
// declares the value and the shell does not declare a second copy of it. So
// every row is checked in both directions.
struct BlockDuplicate_t
{
	const char *pszShellMember;
	const char *pszBlockField;
};

const BlockDuplicate_t kMovedIntoTheBlock[] = {
	{ "m_vecOrigin",            "Origin"              },
	{ "m_angRotation",          "Angles"              },
	{ "m_vecMins",              "Mins"                },
	{ "m_vecMaxs",              "Maxs"                },
	{ "m_Transform",            "WorldTransform"      },
	{ "m_nSolidType",           "SolidType"           },
	{ "m_nSolidFlags",          "SolidFlags"          },
	{ "m_nCollisionGroup",      "CollisionGroup"      },
	{ "m_nModelIndex",          "ModelIndex"          },
	{ "m_nCollisionModelIndex", "CollisionModelIndex" },
	{ "m_RefEHandle",           "Handle"              },
	{ "m_hOwner",               "OwnerHandle"         },
	{ "m_PVSInfo",              "PvsInfo"             },
};

//-----------------------------------------------------------------------------
// FACT 3. The shell declares NO field the engine block also has.
//
// plan_entity.md §3.2 puts these values in memory managed code writes, and the
// gate for step 1b is that the duplication is GONE rather than that the block
// exists beside it. Two copies of an entity's origin is not a cost, it is a bug
// waiting for its first divergent writer: managed code writes the block --
// which is what the block is for -- and native code writes the member, and the
// spatial partition, the trace and the wire then disagree about where the
// entity is depending on which of the two the reader happened to reach. Nothing
// reports that. Both values are well-formed.
//
// TEXTUAL, like everything else in this file, because the gtests here are built
// without the server's headers and cannot ask sizeof or offsetof about
// CStandaloneEntity at all. What they can do is read the declaration, which is
// the thing that would have to change for the duplication to come back.
//-----------------------------------------------------------------------------
TEST( StandaloneLayout, TheShellDeclaresNoFieldTheEngineBlockAlsoHas )
{
	const std::string header = Read( kStandaloneHeader );
	ASSERT_FALSE( header.empty() ) << kStandaloneHeader << " could not be read.";

	const std::string body = CodeOnly( ClassBody( header, "class CStandaloneEntity" ) );
	ASSERT_GT( body.size(), size_t( 4000 ) )
		<< "the extracted class body is only " << body.size() << " bytes of code. "
		   "ClassBody or CodeOnly stopped early, so every absence below is an "
		   "absence from a fragment.";

	const std::string blockHeader = CodeOnly( Read( kEngineBlockHeader ) );
	ASSERT_FALSE( blockHeader.empty() ) << kEngineBlockHeader << " could not be read.";

	// NON-VACUITY, and it is doing real work: if ContainsWord were broken, or
	// CodeOnly had eaten the declarations along with the comments, every
	// expectation below would pass by finding nothing anywhere.
	ASSERT_TRUE( ContainsWord( body, "m_pEngineBlock" ) )
		<< "the shell no longer declares m_pEngineBlock, so this search is "
		   "finding nothing rather than finding an absence.";
	// m_nSpawnFlags, and it used to be m_flNextThink. plan_entity.md step 1g
	// DELETED that member: the think schedule is a managed heap now
	// (SourceSharp.Abi/ThinkScheduler.cs) and the shell holds no part of it, so
	// the anchor moved to a member that is still the shell's own and is not in
	// the block.
	ASSERT_TRUE( ContainsWord( body, "m_nSpawnFlags" ) )
		<< "a member that is supposed to still be here was not found.";

	// AND THE DELETED ONE IS REALLY GONE, which is the other half of the same
	// claim: a shell that grew m_flNextThink back would be keeping a schedule
	// beside the managed one, and the two would disagree about when an entity
	// thinks with nothing to report it.
	ASSERT_FALSE( ContainsWord( body, "m_flNextThink" ) )
		<< "the shell declares m_flNextThink again; the think schedule is "
		   "managed (plan_entity.md §3.4, step 1g) and there is no native half.";

	for ( size_t i = 0; i < sizeof( kMovedIntoTheBlock ) / sizeof( kMovedIntoTheBlock[0] ); i++ )
	{
		const BlockDuplicate_t &row = kMovedIntoTheBlock[ i ];

		EXPECT_TRUE( ContainsWord( blockHeader, row.pszBlockField ) )
			<< "EngineBlock_t no longer declares '" << row.pszBlockField
			<< "'. This row is measuring nothing -- fix the pairing rather than "
			   "deleting the row, because a row that names no block field turns "
			   "the check below into 'the shell does not have this member', "
			   "which is true of a header that never had it.";

		EXPECT_FALSE( ContainsWord( body, row.pszShellMember ) )
			<< "class CStandaloneEntity declares '" << row.pszShellMember
			<< "', which is a second copy of EngineBlockHead_t::"
			<< row.pszBlockField << ".\n\n"
			   "plan_entity.md §3.2: these values live in the blob's engine "
			   "block because managed code writes them and the engine's getters "
			   "read them per trace and per frame. A member beside the block is "
			   "a value with two writers and no rule about which one wins -- and "
			   "both of them hold a well-formed number, so nothing anywhere "
			   "reports the disagreement.";
	}
}

//-----------------------------------------------------------------------------
// FACT 4. The getters the engine calls per trace read the block, and read it
//         with no branch and no computation in the way.
//
// §3.2 is specific about why: the spatial partition asks GetCollisionOrigin and
// OBBMins per trace and the shell must answer with one load at a constant
// offset. A getter that grew an `if` would still answer correctly -- which is
// exactly why this is pinned textually rather than left to a benchmark nobody
// runs.
//-----------------------------------------------------------------------------
TEST( StandaloneLayout, TheHotGettersAreOneLoadOutOfTheBlock )
{
	const std::string header = Read( kStandaloneHeader );
	ASSERT_FALSE( header.empty() ) << kStandaloneHeader << " could not be read.";

	const std::string body = CodeOnly( ClassBody( header, "class CStandaloneEntity" ) );
	ASSERT_GT( body.size(), size_t( 4000 ) ) << "the class body did not extract.";

	static const char *const kGetters[] = {
		"virtual const Vector &GetCollisionOrigin() const OVERRIDE { return Block().Head.Origin; }",
		"virtual const QAngle &GetCollisionAngles() const OVERRIDE { return Block().Head.Angles; }",
		"virtual const Vector &OBBMins() const OVERRIDE { return Block().Head.Mins; }",
		"virtual const Vector &OBBMaxs() const OVERRIDE { return Block().Head.Maxs; }",
		"virtual int GetSolidFlags() const OVERRIDE { return Block().Head.SolidFlags; }",
		"virtual int GetCollisionGroup() const OVERRIDE { return Block().Head.CollisionGroup; }",
		"virtual int GetModelIndex( void ) const OVERRIDE { return Block().Head.ModelIndex; }",
		"virtual int GetCollisionModelIndex() OVERRIDE { return Block().Head.CollisionModelIndex; }",
	};

	for ( size_t i = 0; i < sizeof( kGetters ) / sizeof( kGetters[0] ); i++ )
	{
		EXPECT_NE( body.find( kGetters[ i ] ), std::string::npos )
			<< "this getter is no longer exactly:\n    " << kGetters[ i ]
			<< "\n\nIt is called by the spatial partition per trace. If it has "
			   "grown a branch or a computation, that is the sign that the value "
			   "should have been written into the block by managed code instead "
			   "-- plan_entity.md §3.2. If it was only reformatted, update this "
			   "line and say so.";
	}
}

//-----------------------------------------------------------------------------
// FACT 5. The blob a shell is allocated is at least as long as the block that
//         is its prefix.
//
// Step 1a moved the block into the blob and left the blob's FLOOR where it was:
// tier 0, thirty-two bytes, against a block of three hundred and fifty-two. The
// floor is reached in a normal run -- ResolveTier answers tier 0 for a
// classname the type table has not published yet -- so this was not a
// hypothetical. It would have been a three-hundred-and-twenty-byte write past
// an engine allocation, inside the constructor, before the entity was bound to
// anything that could report it.
//
// The rule is now SourceSharp_StandaloneMinTier: the smallest tier that holds
// the block. This checks the two places that must use it, and that neither has
// been put back to a literal 0 -- which would compile, run, and corrupt the
// heap somewhere else entirely.
//-----------------------------------------------------------------------------
TEST( StandaloneLayout, TheBlobFloorHoldsTheEngineBlock )
{
	const std::string src = CodeOnly(
		Read( "sourcesharp/game/server/shell_standalone_base.cpp" ) );
	ASSERT_FALSE( src.empty() ) << "shell_standalone_base.cpp could not be read.";

	// The rule itself: a search over the tier table for the first tier at least
	// sizeof( EngineBlock_t ) long.
	EXPECT_NE( src.find( "SourceSharp_TierBytes( i ) >= (int)sizeof( EngineBlock_t )" ),
	           std::string::npos )
		<< "SourceSharp_StandaloneMinTier no longer searches the tier table for "
		   "the first tier that holds the block. A constant here is right until "
		   "somebody inserts a row into the shared tier table.";

	// operator new's floor.
	EXPECT_NE( src.find( "if ( nBlobBytes < SourceSharp_StandaloneMinBlobBytes() )" ),
	           std::string::npos )
		<< "operator new's floor is no longer the block's tier. Tier 0 is "
		   "thirty-two bytes and the block the constructor writes is three "
		   "hundred and fifty-two.";

	EXPECT_EQ( src.find( "if ( nBlobBytes < SourceSharp_TierBytes( 0 ) )" ),
	           std::string::npos )
		<< "operator new is back to flooring at tier 0.";

	// And the tier the instance REPORTS, which is what BlobBytes() and the
	// ServerClass are chosen by. An allocation of 512 reported as a tier of 32
	// hands C# the wrong length for the blob it was given.
	EXPECT_NE( src.find( ": SourceSharp_StandaloneMinTier();" ), std::string::npos )
		<< "PostConstruct no longer falls back to the block's tier for a class "
		   "the type table has not published.";
}

}  // namespace
