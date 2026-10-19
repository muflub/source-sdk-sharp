//========= SourceSharp — the client prop shell answers collisions, on paper ==//
//
// The client's bullet-impact effect asks the ENTITY the server named — it clips
// the impact ray against the client shell of the managed prop (the IMPACT_NODECAL
// arm, shell_impact_effects.cpp's ClipRayToEntity at the MASK_SHOT line) and the
// custom metal-spark effect is gated on that trace hitting
// (ApplyImpactChoice behind tr.DidHit()). A clip asks an ICollideable four
// questions — GetSolid, GetSolidFlags, and for a custom test TestCollision /
// TestHitboxes — and the standalone client shell answered all four by hardcoded
// inertness: SOLID_NONE, FSOLID_NOT_SOLID, and two `return false;` stubs. The
// bullet passed through every managed prop, and the report was exactly that:
// "a bullet impact on a metal prop takes no custom effect".
//
// The server twin answered the solidity pair from the replicated block head
// (shell_standalone.h) and wired TestHitboxes to the BoneLibrary hitbox trace
// (shell_standalone_hierarchy.cpp). The fix is parity: the client's two solidity
// answers are the SERVER TWIN'S WORDS, TestCollision is stock's
// C_BaseAnimating::TestCollision gate (c_baseanimating.cpp:5280), and
// TestHitboxes runs BoneLibrary_TraceHitboxes over the bones the client shell
// already keeps for rendering.
//
// WHY A SCAN AND NOT AN ENGINE TEST: constructing a C_StandaloneEntity drives
// the engine's entity allocator (a ctor that touches engine globals), which the
// engine-free cpptest harness cannot pay — Main's ruling for this lane. So the
// RED is extraction-based, and the rules for a scan apply: needles anchor on
// WHOLE CONSTRUCTS (the signature, and the body between balanced braces — not a
// substring that prose could fake), comments are stripped before anything is
// matched so this explanation cannot satisfy its own assertions, a MISSING
// EXTRACTION IS A FAILURE (never a pass), and the twin arm carries its
// POSITIVE CONTROL: the server's bodies — which this fix does not touch — are
// asserted against their expected text, so a moved or gutted server file is red
// here and the twin comparison is never comparing two absences.
//
// THE COUNT CROSS-CHECK IS THE WIRE-FORMAT GUARD: solidity reaches the client
// only if the props tier SENDS it, and the tier's field list, its native count
// and the managed count are three files that must move together. Scanning all
// three in one fact is the one place where spelling the number ten is load-
// bearing: it is what turns "the rows were added on one side only" red, which
// is the join-failure shape the standalone_nettable.h header exists to prevent.
//
//=============================================================================//

#include <string>
#include <fstream>
#include <sys/stat.h>
#include "gtest/gtest.h"

namespace
{

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[ i ] ) + "/game";
		const std::string pub  = std::string( kCandidates[ i ] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode )
			&& stat( pub.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
		{
			return kCandidates[ i ];
		}
	}
	return NULL;
}

std::string ReadRaw( const std::string &strAbsolute )
{
	std::ifstream file( strAbsolute.c_str() );
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

std::string StripComments( const std::string &strIn )
{
	std::string out;
	bool bInBlock = false;
	for ( size_t i = 0; i < strIn.size(); i++ )
	{
		if ( bInBlock )
		{
			if ( i + 1 < strIn.size() && strIn[ i ] == '*' && strIn[ i + 1 ] == '/' )
			{
				bInBlock = false;
				i++;
			}
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '*' )
		{
			bInBlock = true;
			i++;
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '/' )
		{
			while ( i < strIn.size() && strIn[ i ] != '\n' )
				i++;
			if ( i < strIn.size() )
				out += '\n';
			continue;
		}
		out += strIn[ i ];
	}
	return out;
}

// The comment-stripped file, or the empty string when it cannot be read — and
// every caller below FAILS on the empty string, so an unreadable input is
// never a pass.
std::string ReadStripped( const std::string &strRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	return StripComments( ReadRaw( std::string( pszRoot ) + "/" + strRelative ) );
}

std::string NoWhitespace( const std::string &s )
{
	std::string out;
	for ( size_t i = 0; i < s.size(); i++ )
	{
		const char c = s[ i ];
		if ( c != ' ' && c != '\t' && c != '\n' && c != '\r' )
			out += c;
	}
	return out;
}

size_t CountSubstring( const std::string &hay, const std::string &needle )
{
	size_t n = 0;
	for ( size_t at = hay.find( needle ); at != std::string::npos;
	      at = hay.find( needle, at + needle.size() ) )
	{
		++n;
	}
	return n;
}

// The body of the first construct whose whitespace-stripped signature starts
// with `sig`, taken between BALANCED BRACES after it. False when the construct
// is absent or unbalanced — callers must treat false as a failure, and each
// needle here is a whole signature, so a mention in prose or a comment (both
// stripped anyway) cannot satisfy it.
bool ExtractBody( const std::string &noWs, const std::string &sig, std::string &body )
{
	const size_t at = noWs.find( sig );
	if ( at == std::string::npos )
		return false;

	const size_t open = noWs.find( '{', at + sig.size() );
	if ( open == std::string::npos )
		return false;

	int depth = 0;
	for ( size_t i = open; i < noWs.size(); i++ )
	{
		if ( noWs[ i ] == '{' )
			++depth;
		else if ( noWs[ i ] == '}' )
		{
			--depth;
			if ( depth == 0 )
			{
				body = noWs.substr( open + 1, i - open - 1 );
				return true;
			}
		}
	}
	return false;
}
} // namespace


// ---------------------------------------------------------------------------
// THE POSITIVE CONTROLS FIRST: the four scan subjects exist and are readable,
// and the server twin's bodies — the side this fix must NOT touch — still read
// as the block-head answers and the BoneLibrary call they are today. If a
// control goes red, the twin assertions below are comparing nothing and this
// file's whole verdict is suspect; that is the point of pinning them.
// ---------------------------------------------------------------------------

TEST( ClientPropCollision, ServerTwinSolidityAnswersAreTheBlockHead )
{
	const std::string server = NoWhitespace(
		ReadStripped( "sourcesharp/game/server/shell_standalone.h" ) );
	ASSERT_FALSE( server.empty() ) << "shell_standalone.h is unreadable";

	std::string body;
	ASSERT_TRUE( ExtractBody( server, "SolidType_tGetSolid()constOVERRIDE", body ) )
		<< "the server twin's GetSolid signature moved; the twin scan needs "
		   "re-anchoring, not a pass";
	EXPECT_EQ( body, "return(SolidType_t)Block().Head.SolidType;" );

	ASSERT_TRUE( ExtractBody( server, "intGetSolidFlags()constOVERRIDE", body ) )
		<< "the server twin's GetSolidFlags signature moved";
	EXPECT_EQ( body, "returnBlock().Head.SolidFlags;" );
}

// The server twin's hitbox trace is the MANAGED skeleton's since
// physics_bones_native_zero 1.2: the body is the IManagedBones thunk, handing
// over the ray, the trace and the entity's block (ServerSkeletonsTests holds
// what managed does with them).
TEST( ClientPropCollision, ServerTwinTestHitboxesAsksTheManagedSkeleton )
{
	const std::string server = NoWhitespace(
		ReadStripped( "sourcesharp/game/server/shell_standalone_hierarchy.cpp" ) );
	ASSERT_FALSE( server.empty() ) << "shell_standalone_hierarchy.cpp is unreadable";

	std::string body;
	ASSERT_TRUE( ExtractBody( server,
		"boolCStandaloneEntity::TestHitboxes(constRay_t&ray,unsignedintfContentsMask,trace_t&tr)",
		body ) )
		<< "the server twin's TestHitboxes definition moved; re-anchor the twin scan";
	EXPECT_TRUE( body.find( "IManagedBones*pBones=ManagedBones();" ) != std::string::npos );
	EXPECT_TRUE( body.find( "pBones->TestHitboxes(HandleOf(this),&Block(),flRay,(int32_t)fContentsMask,flTrace,nTrace)" )
	             != std::string::npos );
	EXPECT_EQ( std::string::npos, body.find( "BoneLibrary_" ) )
		<< "the server twin decides nothing about bones any more";
}

// ---------------------------------------------------------------------------
// THE DIVERGENCE, one behaviour per fact, red at ead39a59.
// ---------------------------------------------------------------------------

// The client prop's solidity is the server's answer, not a constant. This is
// what the impact clip asks first: ClipRayToEntity at MASK_SHOT reads GetSolid
// and dispatches on it, and SOLID_NONE is "nothing is here", which is why the
// custom-effect gate never fired.
TEST( ClientPropCollision, ClientShellSolidityMatchesTheServerTwin )
{
	const std::string client = NoWhitespace(
		ReadStripped( "sourcesharp/game/client/shell_standalone_client.h" ) );
	ASSERT_FALSE( client.empty() ) << "shell_standalone_client.h is unreadable";
	const std::string server = NoWhitespace(
		ReadStripped( "sourcesharp/game/server/shell_standalone.h" ) );
	ASSERT_FALSE( server.empty() ) << "shell_standalone.h is unreadable";

	std::string clientSolid, serverSolid;
	ASSERT_TRUE( ExtractBody( client, "SolidType_tGetSolid()constOVERRIDE", clientSolid ) )
		<< "the client shell's GetSolid signature moved";
	ASSERT_TRUE( ExtractBody( server, "SolidType_tGetSolid()constOVERRIDE", serverSolid ) )
		<< "the server twin's GetSolid signature moved";
	// Twin equality — and the control fact above says the server side of this
	// comparison is the block head, so this only passes when the client moved
	// TO it.
	EXPECT_EQ( clientSolid, serverSolid )
		<< "the client shell's GetSolid does not answer what its server twin does";

	std::string clientFlags, serverFlags;
	ASSERT_TRUE( ExtractBody( client, "intGetSolidFlags()constOVERRIDE", clientFlags ) )
		<< "the client shell's GetSolidFlags signature moved";
	ASSERT_TRUE( ExtractBody( server, "intGetSolidFlags()constOVERRIDE", serverFlags ) )
		<< "the server twin's GetSolidFlags signature moved";
	EXPECT_EQ( clientFlags, serverFlags )
		<< "the client shell's GetSolidFlags does not answer what its server twin does";
}

// TestCollision is stock's C_BaseAnimating::TestCollision (c_baseanimating.cpp:
// 5280): the CUSTOMRAYTEST/CUSTOMBOXTEST gate that routes to TestHitboxes, not
// the shell's stub. (Stock's terminal `Assert(0)` is deliberately NOT part of
// the contract: it is a no-op in shipping builds where stock returns false, and
// the shell has paths stock's C_BaseAnimating never walked — see the divergence
// note in the source.)
TEST( ClientPropCollision, ClientShellTestCollisionIsStocksCustomTestGate )
{
	const std::string header = NoWhitespace(
		ReadStripped( "sourcesharp/game/client/shell_standalone_client.h" ) );
	ASSERT_FALSE( header.empty() ) << "shell_standalone_client.h is unreadable";

	std::string body;
	ASSERT_TRUE( ExtractBody( header,
		"boolTestCollision(constRay_t&ray,unsignedintfContentsMask,trace_t&tr)OVERRIDE",
		body ) )
		<< "the client shell's TestCollision moved, or is still the unnamed-parameter "
		   "stub — either way the stock gate is not there";
	EXPECT_TRUE( body.find( "ray.m_IsRay" ) != std::string::npos );
	EXPECT_TRUE( body.find( "FSOLID_CUSTOMRAYTEST" ) != std::string::npos );
	EXPECT_TRUE( body.find( "!TestHitboxes(ray,fContentsMask,tr)" ) != std::string::npos );
	EXPECT_TRUE( body.find( "FSOLID_CUSTOMBOXTEST" ) != std::string::npos );
}

// TestHitboxes is the substantive dispatch the server twin carries. Since plan
// physics_bones_native_zero 1.2's client half it is a THUNK, as the server's
// is: the ray and the trace cross flat to IManagedBonesClient with this
// entity's block, and ClientSkeletons traces the managed skeleton's hitboxes
// at the block's hitbox set and collision origin (ClientSkeletonsTests.
// AHitboxTraceIsWrittenBackOnlyWhenTested). The name is historical.
TEST( ClientPropCollision, ClientShellTestHitboxesRunsTheBoneLibrary )
{
	const std::string source = NoWhitespace(
		ReadStripped( "sourcesharp/game/client/shell_standalone_client.cpp" ) );
	ASSERT_FALSE( source.empty() ) << "shell_standalone_client.cpp is unreadable";

	std::string body;
	ASSERT_TRUE( ExtractBody( source,
		"boolC_StandaloneEntity::TestHitboxes(constRay_t&ray,unsignedintfContentsMask,trace_t&tr)",
		body ) )
		<< "the client shell has no TestHitboxes definition — the stub returned false";
	EXPECT_TRUE( body.find( "pBones->TestHitboxes(" ) != std::string::npos );
	EXPECT_TRUE( body.find( "&Block()" ) != std::string::npos );
	EXPECT_TRUE( body.find( "flTrace[SS_BONE_TRACE_FRACTION]" ) != std::string::npos );
	EXPECT_TRUE( body.find( "BoneLibrary_TraceHitboxes" ) == std::string::npos );

	// And the half that answers: the managed skeleton traces at the block's
	// hitbox set, from the block's origin -- GetCollisionOrigin()'s answer.
	const std::string managed = NoWhitespace(
		ReadStripped( "sourcesharp/managed/SourceSharp.Client/ClientSkeletons.cs" ) );
	ASSERT_FALSE( managed.empty() ) << "ClientSkeletons.cs is unreadable";
	EXPECT_TRUE( managed.find( "read.Client.HitboxSet" ) != std::string::npos );
	EXPECT_TRUE( managed.find( "NVectorcollisionOrigin=new(read.Head.Origin.X" ) != std::string::npos );
}

// The solidity answers are only true on a client if the props tier sends them.
// Three files state the tier's shape and all three must move at once: the
// shared field list (the one copy the handshake pairs on), the native count it
// static_asserts against, and the managed count the debug/net-table readers
// project. And the count is TEN: the two new rows are what carry a prop's
// GetSolid answer across, and a half-landed table — rows on one side only — is
// the bitstream that stops making sense partway through.
TEST( ClientPropCollision, StandaloneTableSendsSolidityOnBothSides )
{
	const std::string table = NoWhitespace(
		ReadStripped( "sourcesharp/game/shared/standalone_nettable.h" ) );
	ASSERT_FALSE( table.empty() ) << "standalone_nettable.h is unreadable";

	// The rows: name, kind, in the one list. Whole initialiser entries, not
	// the bare names — the names in the header's prose cannot fake them.
	EXPECT_EQ( CountSubstring( table, "{\"m_nSolidType\",STANDALONE_NET_INT," ), 1u )
		<< "the props tier does not send m_nSolidType";
	EXPECT_EQ( CountSubstring( table, "{\"m_nSolidFlags\",STANDALONE_NET_INT," ), 1u )
		<< "the props tier does not send m_nSolidFlags";
	// The index names both sides' offset arrays use.
	EXPECT_TRUE( table.find( "STANDALONE_NET_FIELD_SOLIDTYPE," ) != std::string::npos );
	EXPECT_TRUE( table.find( "STANDALONE_NET_FIELD_SOLIDFLAGS," ) != std::string::npos );

	// The list's length, counted from its own entries rather than trusted from
	// a comment: ten entries, and the ten index names before COUNT.
	const size_t listHead = table.find( "staticconstStandaloneNetField_ts_Fields[]={");
	ASSERT_TRUE( listHead != std::string::npos ) << "the shared field list moved";
	const size_t listTail = table.find( "};", listHead );
	ASSERT_TRUE( listTail != std::string::npos );
	const std::string list = table.substr( listHead, listTail - listHead );
	const size_t nRows = CountSubstring( list, "{\"m_" );
	EXPECT_EQ( nRows, 11u ) << "StandaloneNetFields carries " << nRows << " rows";   // 11 since m_nSkin

	const std::string generic = NoWhitespace(
		ReadStripped( "sourcesharp/game/shared/generic_nettable.h" ) );
	ASSERT_FALSE( generic.empty() ) << "generic_nettable.h is unreadable";
	EXPECT_TRUE( generic.find( "SOURCESHARP_TIER_PROPS_STANDALONE=11," ) != std::string::npos )
		<< "the native real-prop count did not move with the list";

	const std::string managed = NoWhitespace(
		ReadStripped( "sourcesharp/managed/SourceSharp.Shared.Interop/NetSchema.cs" ) );
	ASSERT_FALSE( managed.empty() )
		<< "NetSchema.cs is unreadable (the managed count would drift in silence)";
	EXPECT_TRUE( managed.find( "StandaloneSharedProps=11;" ) != std::string::npos )
		<< "the managed real-prop count did not move with the list";
}

// The offsets each side reads its rows from: the server's SendProp builder and
// the client's RecvProp builder must address the new fields in THEIR OWN block
// layouts — the same arithmetic as their MarkBlockChanged offsets, spelled per
// side by design (standalone_nettable.h's contract: names and order once,
// offsets twice). Red while either side's array has no entry for the row.
TEST( ClientPropCollision, BothBuildersAddressTheSolidityFields )
{
	const std::string server = NoWhitespace(
		ReadStripped( "sourcesharp/game/server/shell_standalone.cpp" ) );
	ASSERT_FALSE( server.empty() ) << "shell_standalone.cpp is unreadable";
	EXPECT_TRUE( server.find(
		"nOffsets[STANDALONE_NET_FIELD_SOLIDTYPE]=CStandaloneEntity::BlockBase()"
		"+SS_BLOCK_HEAD_OFFSET(SolidType);" ) != std::string::npos )
		<< "the server's send builder has no SolidType offset — SetSolidity's "
		   "MarkBlockChanged offset would match no prop";

	const std::string client = NoWhitespace(
		ReadStripped( "sourcesharp/game/client/shell_standalone_client.cpp" ) );
	ASSERT_FALSE( client.empty() ) << "shell_standalone_client.cpp is unreadable";
	EXPECT_TRUE( client.find(
		"nOffsets[STANDALONE_NET_FIELD_SOLIDTYPE]=C_StandaloneEntity::BlockBase()"
		"+SS_CLIENT_BLOCK_HEAD_OFFSET(SolidType);" ) != std::string::npos )
		<< "the client's recv builder has no SolidType offset — the replicated "
		   "value would decode past the payload";
	EXPECT_TRUE( client.find(
		"nOffsets[STANDALONE_NET_FIELD_SOLIDFLAGS]=C_StandaloneEntity::BlockBase()"
		"+SS_CLIENT_BLOCK_HEAD_OFFSET(SolidFlags);" ) != std::string::npos );
	EXPECT_TRUE( server.find(
		"nOffsets[STANDALONE_NET_FIELD_SOLIDFLAGS]=CStandaloneEntity::BlockBase()"
		"+SS_BLOCK_HEAD_OFFSET(SolidFlags);" ) != std::string::npos );
}

// The skin row (2026-10-19): both builders address the block's Client.Skin, so
// the server's mark (NetworkedSkin.Offset, inside the block) matches its prop
// and the client decodes straight into what GetSkin answers.
TEST( ClientPropCollision, BothBuildersAddressTheSkin )
{
	const std::string server = NoWhitespace(
		ReadStripped( "sourcesharp/game/server/shell_standalone.cpp" ) );
	const std::string client = NoWhitespace(
		ReadStripped( "sourcesharp/game/client/shell_standalone_client.cpp" ) );
	ASSERT_FALSE( server.empty() );
	ASSERT_FALSE( client.empty() );
	EXPECT_TRUE( server.find(
		"nOffsets[STANDALONE_NET_FIELD_SKIN]=CStandaloneEntity::BlockBase()"
		"+(int)offsetof(EngineBlock_t,Client)+(int)offsetof(EngineBlockClientTail_t,Skin);" ) != std::string::npos )
		<< "the server's send builder has no skin offset";
	EXPECT_TRUE( client.find(
		"nOffsets[STANDALONE_NET_FIELD_SKIN]=C_StandaloneEntity::BlockBase()"
		"+(int)offsetof(EngineBlock_t,Client)+(int)offsetof(EngineBlockClientTail_t,Skin);" ) != std::string::npos )
		<< "the client's recv builder has no skin offset";
}
