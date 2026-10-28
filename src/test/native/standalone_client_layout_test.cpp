//========= SourceSharp — the CLIENT shell reads the engine block ============//
//
// The client half of standalone_layout_test.cpp, and it asks the same question
// the server's does: is there exactly ONE copy of each value the engine asks a
// shell for?
//
// Step 1b deleted thirteen members of CStandaloneEntity in favour of the block.
// Step 5c does the same on this side, for sixteen — the seven head values the
// engine's DECODER now writes, and the nine render-tail values this side
// computes. The argument is 1b's word for word: two copies of an entity's
// origin is not a cost, it is a bug waiting for its first divergent writer, and
// here the two writers would be the network decoder and this shell's own code.
//
// WHY THE CHECKS ARE TEXTUAL. Nothing in a build can see a member and a block
// field holding the same value; both compile, both are read, and the game looks
// right until the two disagree. The same reason the server's file gives.
//
// THE CLIENT DIVERGES FROM THE SERVER IN ONE PLACE, deliberately, and this file
// is where that divergence is pinned rather than left to be rediscovered: the
// server's block is the BLOB'S PREFIX and this one is a MEMBER of the shell.
// See TheBlockIsAMemberAndNotTheBlobsPrefix for the two bugs the server's
// placement would be over here, and TheBlobFloorMatchesWhereTheBlockActuallyIs
// for the pair of lines that have to move together if that ever changes.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "cmake_graph.h"

#include <sys/stat.h>

#include <fstream>
#include <set>
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

const char *const kClientHeader = "sourcesharp/game/client/shell_standalone_client.h";
// The sixteen values that used to be members of C_StandaloneEntity and are
// fields of the engine block, paired with the block field each became.
//
// THE PAIRING IS THE POINT rather than the absence, exactly as on the server:
// "the header does not contain m_vecOrigin" is true of a header that never had
// an origin. What this says is that the DUPLICATION is gone — the block
// declares the value and the shell does not declare a second copy of it — so
// every row is checked in both directions.
struct BlockDuplicate_t
{
	const char *pszShellMember;
	const char *pszBlockField;
};

}  // namespace

//-----------------------------------------------------------------------------
// FACT 1. The shell declares no member the block also declares.
//
// One EXPECT per moved value rather than a loop with one message, because the
// failure worth reading is "m_nSkin is back", not "one of sixteen is back".
//-----------------------------------------------------------------------------
class ClientShellLayout : public ::testing::Test
{
protected:
	static const std::string &Body()
	{
		static std::string s_body = CodeOnly(
			ClassBody( Read( kClientHeader ), "class C_StandaloneEntity" ) );
		return s_body;
	}
};

TEST_F( ClientShellLayout, TheClassBodyWasFoundAtAll )
{
	// FIRST, because every absence below is vacuously true of an empty string.
	ASSERT_FALSE( Body().empty() )
		<< "class C_StandaloneEntity could not be read out of " << kClientHeader
		<< ". Every other fact in this file would pass on the empty string.";

	// And that it is the WHOLE class rather than a prefix: the block accessor
	// is near the top and the block pointer is the very first declaration, so a
	// body that stopped early would still contain them.
	EXPECT_TRUE( ContainsWord( Body(), "m_nEntIndex" ) )
		<< "ClassBody stopped before the private member section, so 'this "
		   "member is absent' below means 'ClassBody did not reach it'.";
}

//-----------------------------------------------------------------------------
// FACT 2. The block is a MEMBER here, not the blob's prefix — the one place
//         the two sides deliberately differ.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// FACT 3. The recv props address the block, with the server's arithmetic.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// FACT 4. The managed entity is made before this entity's own state decodes.
//
// THE STEP'S GATE: an entity exists in managed code BEFORE its first data
// update. On this side that means the type id's recv proxy, because the engine
// creates the shell, then decodes the whole update, and only then calls
// PostDataUpdate. A create
// at the top of PostDataUpdate is still a create after the update landed.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// FACT 5. The render-tail getters are one load each.
//
// §3.2's whole argument for a struct rather than a table of offsets: the
// renderer asks these per draw, so each has to be a load at a constant offset.
// A getter that has grown a branch or a computation is the sign that the value
// should have been written into the block instead.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// FACT 8. The render policy is WRITTEN ON CHANGE, and the shell only carries
//         it out (plan_entity.md step 6b, §3.9).
//
// Two halves, and the failure is different at each end.
//
//   INPUT  a setter that changes what an entity IS and does not bump the input
//          serial. Managed code never looks at that entity again, so it keeps
//          the render group, the draw answer and the shadow it had before the
//          change — silently, for as long as nothing else about it moves.
//
//   OUTPUT a shell that stopped reading the block, or a sweep that stopped
//          being edge-triggered. The first is native policy growing back; the
//          second is the leaf system and the shadow manager being told the same
//          thing every frame for every entity in the map.
//-----------------------------------------------------------------------------
namespace
{

}  // namespace

//-----------------------------------------------------------------------------
// FACT 9. What step 6b could NOT do, asserted so that it is done the moment it
//         becomes possible.
//-----------------------------------------------------------------------------

TEST( ClientRenderPolicyShell, ClientThinkListLeftAfterCBaseEntity )
{
	// plan_entity.md lists `client_thinklist.cpp (leaves)` under step 6b, and
	// §3.9 says client think is the managed scheduler with
	// IClientThinkable::GetClientThinkable answering NULL. The NULL half is
	// already true -- see shell_standalone_client.h -- and no entity on this
	// hierarchy has ever been on the think list. The TU still cannot leave, and
	// not because of anything this step did. Measured by removing its $File line
	// and linking:
	//
	//   c_baseanimating.cpp:621        CClientThinkList::RemoveThinkable
	//   c_baseentity.cpp:1084          CClientThinkList::RemoveThinkable
	//   c_baseentity.cpp:3671          CClientThinkList::SetNextClientThink
	//   shell_clientdll.cpp:1922       CClientThinkList::PerformThinkFunctions
	//
	// The first three are the two stock TUs 6a is also blocked by, and the
	// fourth is the once-a-frame driver that runs them: c_entitydissolve.cpp,
	// c_func_dust.cpp, c_rope.cpp and ragdoll_shared.cpp all call
	// SetNextClientThink, so dropping the driver would leave four stock entity
	// families with thinks that never run -- which is worse than compiling a TU
	// nothing on the managed hierarchy uses.
	//
	// THE TRIPWIRE HAS FIRED, AND WHAT IT FOUND IS NOT WHAT IT EXPECTED.
	//
	// It said: "THIS FACT IS A TRIPWIRE AND IS MEANT TO GO RED ... Phase 6's exit
	// takes c_baseanimating.cpp and c_baseentity.cpp out of the vpc together
	// with client_thinklist.cpp; when the first of them goes, this fails, and
	// whoever sees it should delete client_thinklist.cpp's $File line beside it
	// and drop shell_clientdll.cpp's PerformThinkFunctions call."
	//
	// THE FLIP TOOK THE FIRST THREE AND NOT THE FOURTH. Measured on this tree:
	//
	//     c_baseanimating.cpp    gone      c_entitydissolve.cpp   gone
	//     c_baseentity.cpp       gone      c_func_dust.cpp        gone
	//     client_thinklist.cpp   STAYS     c_rope.cpp             STAYS
	//                                      ragdoll_shared.cpp     STAYS
	//
	// So the instruction the tripwire left is WRONG NOW, and following it would
	// have been the damage: the list above named four SetNextClientThink callers
	// as the reason the driver has to stay, and TWO OF THEM ARE STILL ON THE
	// CLIENT LINK LINE. Dropping client_thinklist.cpp and
	// PerformThinkFunctions today would leave c_rope and the ragdoll builder
	// with thinks that never run -- a silent behaviour loss with a green build,
	// which is the exact failure mode this file's header is about.
	//
	// The fact is therefore rewritten to hold all three halves, each able to
	// redden alone, and the remaining work is recorded rather than done:
	//
	//   c_baseentity.cpp     ABSENT  -- red if a TU the flip removed comes back
	//   client_thinklist.cpp PRESENT -- red when somebody takes the driver out;
	//                                   check c_rope and ragdoll_shared first
	//   c_rope.cpp           PRESENT -- the surviving reason it cannot go, and
	//                                   the half that stops this becoming a
	//                                   one-sided "everything is gone"
	//
	// What the client compiles is client_sourcesharp's source list, read
	// through cmake_graph.h (src/client/native/CMakeLists.txt). The two PRESENT
	// halves are this fact's proof-of-read: a reader that saw nothing cannot
	// pass them, so it cannot pass the ABSENT half for free either.
	const std::set< std::string > client = cmake_graph::CompiledSources( cmake_graph::kClient );

	// By the path's TAIL, as the old vpc needle was: the TU under any directory.
	const std::string strGone = "/c_baseentity.cpp";
	for ( std::set< std::string >::const_iterator it = client.begin(); it != client.end(); ++it )
	{
		EXPECT_FALSE( it->size() >= strGone.size()
			&& it->compare( it->size() - strGone.size(), strGone.size(), strGone ) == 0 )
			<< *it << ": c_baseentity.cpp is back in the client build. The flip removed it; "
			   "if it has returned it calls SetNextClientThink through C_BaseEntity "
			   "again and step 6b is undone rather than unfinished.";
	}

	// AND THEN IT LEFT (plan_client_novalve C8, lane c6-input), checked first as
	// this fact asked: no object on the link line imports SetNextClientThink or
	// AddToThinkList -- ragdoll_shared.cpp, while it was compiled, imported neither -- and
	// ClientC8Evicted.NothingOnTheLinkSchedulesAClientThink keeps it so, so a
	// future scheduler fails loudly instead of silently never running.
	EXPECT_FALSE( client.count( "game/client/client_thinklist.cpp" ) )
		<< "client_thinklist.cpp is back in the client build.";

	// c_rope.cpp LEFT with the C2 rope port (its think was already the rope
	// system's, never CClientThinkList's). ragdoll_shared.cpp LEFT at
	// plan_physics_bones_native_zero Phase 3.
	EXPECT_FALSE( client.count( "game/client/c_rope.cpp" ) )
		<< "c_rope.cpp is back in the client build beside the managed rope.";
}

