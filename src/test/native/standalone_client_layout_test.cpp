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
const char *const kClientSource = "sourcesharp/game/client/shell_standalone_client.cpp";

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

const BlockDuplicate_t kMoved[] =
{
	// The head. The engine's recv props address the first three directly.
	{ "m_vecOrigin",       "Origin" },
	{ "m_angRotation",     "Angles" },
	{ "m_nModelIndex",     "ModelIndex" },
	{ "m_vecMins",         "Mins" },
	{ "m_vecMaxs",         "Maxs" },
	{ "m_Transform",       "WorldTransform" },
	{ "m_RefEHandle",      "Handle" },

	// The render tail — §3.2's "render origin and angles, render bounds,
	// render colour and fx blend, skin, body, shadow choice, render group, and
	// the animation inputs the bone code reads".
	{ "m_nFxBlend",        "FxBlend" },
	{ "m_nSkin",           "Skin" },
	{ "m_nShadowChoice",   "ShadowChoice" },
	{ "m_nSequence",       "Sequence" },
	{ "m_flPlaybackRate",  "PlaybackRate" },
	{ "m_flSequenceStart", "AnimTime" },
	{ "m_vecDrawMins",     "RenderMins" },
	{ "m_vecDrawMaxs",     "RenderMaxs" },
	{ "m_nCustomAlpha",    "RenderColorA" },

	// AND THE RENDER POLICY'S INPUTS (plan_entity.md step 6b). §3.9's "should
	// draw, render group, translucency, fade, shadow type" is decided in C# and
	// written into the block, so what the shell used to hold about WHAT AN
	// ENTITY IS went with the decision. Eight more members, same rule, same
	// failure if one comes back.
	{ "m_bDormant",        "Dormant" },
	{ "m_bViewModel",      "ViewModel" },
	{ "m_bCustomDraw",     "CustomDraw" },
	{ "m_bTranslucent",    "Translucent" },
	{ "m_nFadeStyle",      "FadeStyle" },
	{ "m_flFadeStartDist", "FadeStartDistance" },
	{ "m_flFadeEndDist",   "FadeEndDistance" },
	{ "m_flFadeMinAlpha",  "FadeMinAlpha" },
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

TEST_F( ClientShellLayout, TheShellDeclaresNoMemberTheBlockAlsoHas )
{
	ASSERT_FALSE( Body().empty() );

	for ( size_t i = 0; i < sizeof( kMoved ) / sizeof( kMoved[0] ); i++ )
	{
		// The DECLARATION, not the mention. The header names all sixteen in the
		// comment that records where each one went, and CodeOnly strips
		// comments, so what is left is code — and in a class body, code naming
		// a member is a declaration or a use.
		EXPECT_FALSE( ContainsWord( Body(), kMoved[ i ].pszShellMember ) )
			<< kMoved[ i ].pszShellMember << " is back in C_StandaloneEntity, "
			   "beside EngineBlockClientTail_t/EngineBlockHead_t::"
			<< kMoved[ i ].pszBlockField
			<< ". Two copies of one value is plan_entity.md §3.2's named "
			   "failure: the network decoder writes the block, this shell "
			   "would write the member, and the renderer, the leaf system and "
			   "the collision partition would each read whichever they "
			   "happened to reach.";
	}
}

TEST_F( ClientShellLayout, EveryValueThatLeftTheShellIsDeclaredInTheBlock )
{
	// The other direction, and it is not decoration: a member deleted and NOT
	// replaced by a block field is a value the engine still asks for and
	// nothing holds.
	// BOTH FILES, concatenated. engine_block.h is the header the game includes
	// and, since the generation lane landed, the PROSE ONLY: the structs
	// themselves are emitted into engine_block_generated.h from the C#
	// declaration. Reading both means this fact does not care which of the two
	// holds the fields, and the ASSERT below is what stops "both files were
	// unreadable" from looking like "every field is present".
	const std::string block =
		CodeOnly( Read( "sourcesharp/game/shared/engine_block.h" ) ) +
		CodeOnly( Read( "sourcesharp/game/shared/engine_block_generated.h" ) );

	ASSERT_NE( block.find( "struct EngineBlockClientTail_t" ), std::string::npos )
		<< "neither engine_block.h nor engine_block_generated.h declares "
		   "EngineBlockClientTail_t, so every field below would report missing "
		   "for a reason having nothing to do with the field.";

	for ( size_t i = 0; i < sizeof( kMoved ) / sizeof( kMoved[0] ); i++ )
	{
		EXPECT_TRUE( ContainsWord( block, kMoved[ i ].pszBlockField ) )
			<< kMoved[ i ].pszBlockField << " is not declared in engine_block.h, "
			   "so " << kMoved[ i ].pszShellMember << " was deleted from the "
			   "client shell and replaced by nothing.";
	}
}

//-----------------------------------------------------------------------------
// FACT 2. The block is a MEMBER here, not the blob's prefix — the one place
//         the two sides deliberately differ.
//-----------------------------------------------------------------------------
TEST( ClientBlockPlacement, TheBlockIsAMemberAndNotTheBlobsPrefix )
{
	const std::string src = CodeOnly( Read( kClientSource ) );
	ASSERT_FALSE( src.empty() ) << "shell_standalone_client.cpp could not be read.";

	EXPECT_NE( src.find( "return (int)( (intp)&pNull->m_EngineBlock );" ), std::string::npos )
		<< "C_StandaloneEntity::BlockBase() is no longer the offset of the "
		   "m_EngineBlock MEMBER.";

	// AND SPECIFICALLY NOT THE BLOB BASE. Mirroring the server here would put
	// the block inside the payload window, and the payload window is the
	// server's own block arriving over the wire: Head.Handle would be decoded
	// with the SERVER's serial over this client's, and every handle the engine
	// holds for the entity would stop resolving.
	EXPECT_EQ( src.find( "int C_StandaloneEntity::BlockBase()\n{\n\treturn SourceSharp_StandaloneBlobOffset();" ),
	           std::string::npos )
		<< "BlockBase() has been pointed at the blob. The blob's first bytes "
		   "are the SERVER's engine block, delivered as payload words, and its "
		   "first four are the server's CBaseHandle — a different serial from "
		   "this client's.";
}

TEST( ClientBlockPlacement, TheHandleOverlayIsAssertedAndNotAssumed )
{
	// BlockHandle() reinterprets the block's uint32 as a CBaseHandle. That is
	// only sound while CBaseHandle is one uint32 and nothing else, and the
	// failure if it stopped being one would be silent in the worst direction:
	// the handle would read ClassnameIndex -- the next field -- as its serial,
	// and every handle on the hierarchy would resolve to nothing.
	const std::string src = CodeOnly( Read( kClientSource ) );
	ASSERT_FALSE( src.empty() );

	EXPECT_NE( src.find( "static_assert( sizeof( CBaseHandle ) == sizeof( uint32_t )" ),
	           std::string::npos )
		<< "the client shell no longer asserts that CBaseHandle is one uint32, "
		   "which is what BlockHandle()'s reinterpret_cast rests on. The "
		   "server's twin is in shell_standalone_base.cpp.";
}

TEST( ClientBlockPlacement, TheBlobFloorMatchesWhereTheBlockActuallyIs )
{
	// THE PAIR THAT HAS TO MOVE TOGETHER. The server's floor is
	// SourceSharp_StandaloneMinBlobBytes() because the server's block IS the
	// blob's prefix and tier 0 is thirty-two bytes against a block of three
	// hundred and fifty-two. This side's floor is tier 0 and that is correct —
	// but only while the block is a member. Both halves are asserted so that
	// changing one alone is red rather than a 320-byte overrun.
	const std::string src = CodeOnly( Read( kClientSource ) );
	ASSERT_FALSE( src.empty() );

	const bool bBlockIsAMember =
		src.find( "return (int)( (intp)&pNull->m_EngineBlock );" ) != std::string::npos;
	const bool bFloorIsTierZero =
		src.find( "if ( nBlobBytes < SourceSharp_TierBytes( 0 ) )" ) != std::string::npos;

	EXPECT_EQ( bBlockIsAMember, bFloorIsTierZero )
		<< "The block's placement and operator new's floor disagree. Tier 0 is "
		   "thirty-two bytes; the block is three hundred and fifty-two. A block "
		   "at the blob base with a tier-0 floor is a 320-byte write past the "
		   "allocation, in the constructor, before the entity is bound to "
		   "anything that could report it — which is exactly what step 1b found "
		   "on the server.";
}

//-----------------------------------------------------------------------------
// FACT 3. The recv props address the block, with the server's arithmetic.
//-----------------------------------------------------------------------------
TEST( ClientBlockNetworking, TheThreeNetworkedHeadPropsAddressTheBlock )
{
	const std::string src = CodeOnly( Read( kClientSource ) );
	ASSERT_FALSE( src.empty() );

	const char *const kFields[] = { "Origin", "Angles", "ModelIndex" };

	for ( size_t i = 0; i < sizeof( kFields ) / sizeof( kFields[0] ); i++ )
	{
		const std::string wanted =
			std::string( "SS_CLIENT_BLOCK_HEAD_OFFSET( " ) + kFields[ i ] + " )";

		EXPECT_NE( src.find( wanted ), std::string::npos )
			<< "the recv prop for " << kFields[ i ] << " no longer addresses the "
			   "engine block. plan_entity.md §3.2: on the client the engine's "
			   "decoder writes the networked part of the block DIRECTLY, "
			   "because the recv props address it.";
	}

	EXPECT_NE( src.find( "C_StandaloneEntity::BlockBase()\n\t                                              + SS_CLIENT_BLOCK_HEAD_OFFSET" ),
	           std::string::npos )
		<< "the offsets are no longer BlockBase() plus the field's offset "
		   "inside the block — the same arithmetic the server's SendProp "
		   "builder uses.";
}

//-----------------------------------------------------------------------------
// FACT 4. The managed entity is made before this entity's own state decodes.
//
// THE STEP'S GATE: an entity exists in managed code BEFORE its first data
// update. On this side that means the type id's recv proxy, because the engine
// creates the shell, then decodes the whole update, and only then calls
// PostDataUpdate. A create
// at the top of PostDataUpdate is still a create after the update landed.
//-----------------------------------------------------------------------------
TEST( ClientManagedCreate, TheTypeIdPropCarriesTheCreateProxy )
{
	const std::string src = CodeOnly( Read( kClientSource ) );
	ASSERT_FALSE( src.empty() );

	EXPECT_NE( src.find( "RecvProxy_StandaloneTypeId" ), std::string::npos )
		<< "the type id's recv proxy is gone. Without it the managed half is "
		   "made from PostDataUpdate, which the engine calls AFTER "
		   "RecvTable_Decode has written the whole first update.";

	EXPECT_NE( src.find( "( i == STANDALONE_NET_FIELD_TYPEID )" ), std::string::npos )
		<< "the proxy is no longer attached to the type id prop specifically.";

	// RE-SPELLED, not moved: the-close 8/N replaced fifteen hand-written
	// downcasts with three named inlines, and this C cast became
	// SourceSharp_ClientShellFromRecvStruct -- which is the same static_cast
	// and is checked against dynamic_cast in debug.
	EXPECT_NE( src.find( "SourceSharp_ClientShellFromRecvStruct( pStruct )->EnsureManagedHalf();" ),
	           std::string::npos )
		<< "the proxy no longer makes the managed half.";
}

TEST( ClientManagedCreate, TheProxyStoresTheValueBeforeItUsesIt )
{
	// ORDER INSIDE THE PROXY. EnsureManagedHalf resolves the classname FROM
	// m_nTypeId, so a proxy that called it before writing the value would pair
	// every entity against the PREVIOUS type id — which for a fresh entity is
	// zero, and zero pairs with nothing.
	const std::string src = CodeOnly( Read( kClientSource ) );
	ASSERT_FALSE( src.empty() );

	const size_t store = src.find( "*( (uint32 *)pOut ) = (uint32)pData->m_Value.m_Int;" );
	const size_t call  = src.find( "SourceSharp_ClientShellFromRecvStruct( pStruct )->EnsureManagedHalf();" );

	ASSERT_NE( store, std::string::npos );
	ASSERT_NE( call, std::string::npos );
	EXPECT_LT( store, call )
		<< "the proxy calls EnsureManagedHalf before storing the type id it "
		   "resolves the class from.";
}

TEST( ClientManagedCreate, TheTypeIdIsDecodedBeforeThePayloadWords )
{
	// WHAT THE PROXY RESTS ON, and it is a fact about the shared field list
	// rather than about this file: props decode in table order, the six shared
	// props are built before the payload words, and the type id is one of the
	// six. If the type id were ever moved after the payload, the proxy would
	// still fire — but it would fire after this entity's own state had already
	// been written, which is precisely what the gate forbids.
	const std::string table = CodeOnly(
		Read( "sourcesharp/game/shared/standalone_nettable.h" ) );
	ASSERT_FALSE( table.empty() ) << "standalone_nettable.h could not be read.";

	const size_t typeId = table.find( "STANDALONE_NET_FIELD_TYPEID" );
	const size_t words  = table.find( "STANDALONE_NET_FIELD_PAYLOADWORDS" );

	ASSERT_NE( typeId, std::string::npos );
	ASSERT_NE( words, std::string::npos );
	EXPECT_LT( typeId, words )
		<< "the type id is no longer ahead of the payload in the shared field "
		   "list, so the create proxy would fire after this entity's own state "
		   "had decoded.";

	const std::string src = CodeOnly( Read( kClientSource ) );
	const size_t shared  = src.find( "BuildStandaloneRecvProps( g_pTierProps[ iTier ], nBuilt );" );
	const size_t payload = src.find( "SourceSharpPayload_BuildRecvProps( g_pTierProps[ iTier ] + nBuilt," );

	ASSERT_NE( shared, std::string::npos );
	ASSERT_NE( payload, std::string::npos );
	EXPECT_LT( shared, payload )
		<< "the payload words are built before the shared props, so the type "
		   "id no longer has a lower prop index than this entity's state.";
}

TEST( ClientManagedCreate, TheManagedHalfIsNotGatedOnDataUpdateCreated )
{
	// THE HOLE THIS STEP CLOSED. The create used to sit inside
	// `if ( updateType == DATA_UPDATE_CREATED )`, so an entity whose first
	// update carried a type id this client had not published yet never got a
	// managed half, however many later updates arrived with the type known.
	const std::string src = CodeOnly( Read( kClientSource ) );
	ASSERT_FALSE( src.empty() );

	// ONE TAB, WHICH IS THE WHOLE CHECK. Written as a substring search for the
	// indented call, the fact passed under the mutation it was written for:
	// "\tEnsureManagedHalf();" is also a substring of "\t\tEnsureManagedHalf();",
	// and a call moved back under `if ( updateType == DATA_UPDATE_CREATED )` is
	// still ahead of the branch it was moved into. Anchored to a newline and a
	// single tab, it is the statement's own indentation and a nested call does
	// not match it.
	const size_t ensure  = src.find( "\n\tEnsureManagedHalf();\n" );
	const size_t created = src.find( "\n\tif ( updateType == DATA_UPDATE_CREATED )\n\t{" );

	ASSERT_NE( ensure, std::string::npos )
		<< "PostDataUpdate no longer calls EnsureManagedHalf unconditionally at "
		   "the top level of the function. An entity whose first update carried "
		   "a type id this client had not published yet is then never paired, "
		   "however many later updates arrive with the type known.";
	ASSERT_NE( created, std::string::npos );
	EXPECT_LT( ensure, created )
		<< "the managed half is made after the DATA_UPDATE_CREATED branch, so "
		   "the leaf-system add and the surface renderer run before the entity "
		   "exists in managed code.";

	// And nowhere else, indented under anything.
	EXPECT_EQ( src.find( "\t\tEnsureManagedHalf();" ), std::string::npos )
		<< "EnsureManagedHalf is called from inside a branch. The only two "
		   "callers are the type id's recv proxy and the top level of "
		   "PostDataUpdate.";
}

TEST( ClientManagedCreate, ARefusedClassnameIsAskedOnceAndNotOncePerUpdate )
{
	// A CROSSING COUNT, PINNED. EnsureManagedHalf is now called on every update
	// (and again from the recv proxy), and OnStandaloneCreate answers 0 for
	// every entity whose classname no managed class claims -- which is most of
	// this hierarchy, BY DESIGN. Unlatched, that is one crossing per such
	// entity per update for the life of the map: on dm_lockdown, most of 473
	// entities at the tick rate. plan_entity.md's standing rule is that each
	// phase may only LOWER the crossing count.
	//
	// Latched on the TYPE ID rather than on a bool, so the retry that closes the
	// "type published late" hole survives: an entity whose id changes is a
	// different class and is offered again.
	const std::string src = CodeOnly( Read( kClientSource ) );
	ASSERT_FALSE( src.empty() );

	EXPECT_NE( src.find( "m_nTypeId == m_nManagedTriedTypeId" ), std::string::npos )
		<< "EnsureManagedHalf no longer remembers which type id it already "
		   "offered this entity under, so every entity with no managed class "
		   "crosses the ABI on every update.";

	EXPECT_NE( src.find( "m_nManagedTriedTypeId = m_nTypeId;" ), std::string::npos )
		<< "the latch is read and never written.";

	// AND SET BEFORE THE CALL, not after it: OnStandaloneCreate runs managed
	// code, which can reach back into this shell. A latch written afterwards
	// would be unset for the whole of the call it is meant to bound.
	const size_t set  = src.find( "m_nManagedTriedTypeId = m_nTypeId;" );
	const size_t call = src.find( "m_bHasManaged = pStandalone->OnStandaloneCreate(" );

	ASSERT_NE( set, std::string::npos );
	ASSERT_NE( call, std::string::npos );
	EXPECT_LT( set, call );

	// The one case that must NOT latch: no managed runtime yet. That is not an
	// answer about this entity, and latching it would leave every entity made
	// before the runtime attached permanently without a managed half.
	const size_t noApi = src.find( "if ( !pStandalone )" );

	ASSERT_NE( noApi, std::string::npos );
	EXPECT_LT( noApi, set )
		<< "the latch is set before the no-runtime early return, so an entity "
		   "that arrived before the managed runtime did is never offered again.";
}

TEST( ClientManagedCreate, TheManagedHalfIsMadeBeforeTheDataHandOver )
{
	// The end of the same story: whatever else moves, OnStandaloneCreate has to
	// precede OnStandaloneDataChanged for one entity. A class handed its state
	// before it exists is a state update delivered to nothing.
	const std::string src = CodeOnly( Read( kClientSource ) );
	ASSERT_FALSE( src.empty() );

	const size_t create = src.find( "pStandalone->OnStandaloneCreate(" );
	const size_t data   = src.find( "pStandalone->OnStandaloneDataChanged(" );

	ASSERT_NE( create, std::string::npos );
	ASSERT_NE( data, std::string::npos );
	EXPECT_LT( create, data )
		<< "the blob is handed to managed code before the managed half is "
		   "made.";

	// And exactly one create call site, so the ordering above is about the only
	// one there is. The second call site the step deleted was the one inside
	// the DATA_UPDATE_CREATED branch.
	EXPECT_EQ( src.find( "pStandalone->OnStandaloneCreate(", create + 1 ), std::string::npos )
		<< "there is more than one OnStandaloneCreate call site again, so "
		   "'the create comes first' is only true of whichever one this test "
		   "happened to find.";
}

//-----------------------------------------------------------------------------
// FACT 5. The render-tail getters are one load each.
//
// §3.2's whole argument for a struct rather than a table of offsets: the
// renderer asks these per draw, so each has to be a load at a constant offset.
// A getter that has grown a branch or a computation is the sign that the value
// should have been written into the block instead.
//-----------------------------------------------------------------------------
TEST( ClientRenderTail, TheRenderGettersAreOneBlockLoadEach )
{
	const std::string body = CodeOnly(
		ClassBody( Read( kClientHeader ), "class C_StandaloneEntity" ) );
	ASSERT_FALSE( body.empty() );

	const char *const kGetters[] =
	{
		"virtual const QAngle &GetRenderAngles( void ) OVERRIDE { return Block().Client.RenderAngles; }",
		"virtual const matrix3x4_t &RenderableToWorldTransform() OVERRIDE { return Block().Client.RenderTransform; }",
		"virtual const matrix3x4_t &CollisionToWorldTransform() const OVERRIDE { return Block().Head.WorldTransform; }",
		"virtual bool IsTransparent( void ) OVERRIDE { return Block().Client.Transparent != 0; }",
		"virtual bool IsDormant( void ) OVERRIDE { return Block().Client.Dormant != 0; }",
		"virtual int  GetBody() OVERRIDE { return Block().Client.Body; }",
		"virtual int  GetSkin() OVERRIDE { return Block().Client.Skin; }",
		"virtual int  GetFxBlend( void ) OVERRIDE { return Block().Client.FxBlend; }",
		"virtual const Vector &OBBMins() const OVERRIDE { return Block().Head.Mins; }",
		"virtual const Vector &OBBMaxs() const OVERRIDE { return Block().Head.Maxs; }",
		"virtual const Vector &GetCollisionOrigin() const OVERRIDE { return Block().Head.Origin; }",
		"virtual const QAngle &GetCollisionAngles() const OVERRIDE { return Block().Head.Angles; }",
		"virtual int GetCollisionModelIndex() OVERRIDE { return Block().Head.ModelIndex; }",
	};

	for ( size_t i = 0; i < sizeof( kGetters ) / sizeof( kGetters[0] ); i++ )
	{
		EXPECT_NE( body.find( kGetters[ i ] ), std::string::npos )
			<< "this getter is no longer exactly:\n    " << kGetters[ i ]
			<< "\n\nThe renderer and the spatial partition call it per draw and "
			   "per trace. If it has grown a branch or a computation, the value "
			   "should have been written into the block instead — "
			   "plan_entity.md §3.2. If it was only reformatted, update this "
			   "line and say so.";
	}
}

TEST( ClientRenderTail, TheRenderOriginIsTheTailsAndNotTheHeads )
{
	// IT USED TO BE THE ONE GETTER WITH A BRANCH, AND STEP 6e TOOK IT OUT.
	//
	// The branch was `m_pRagdoll ? RagdollOrigin() : Block().Client.RenderOrigin`
	// -- a ragdoll's origin comes from the solver rather than from the network,
	// so the render path asked the solver, per entity, per pass, per frame. The
	// pose changes exactly ONCE PER PHYSICS STEP, so the step writes it into the
	// tail instead (RagdollLibrary_StepPoses) and this is a load like the rest.
	//
	// What still matters is what it always did: the answer is the CLIENT TAIL's
	// pose and not the head's placement, because step 6a puts the interpolated
	// result in the tail and leaves the networked one in the head, and a getter
	// reading the head would silently stop interpolating that day.
	const std::string body = CodeOnly(
		ClassBody( Read( kClientHeader ), "class C_StandaloneEntity" ) );
	ASSERT_FALSE( body.empty() );

	EXPECT_NE( body.find( "return Block().Client.RenderOrigin;" ), std::string::npos )
		<< "GetRenderOrigin no longer answers from the client tail's "
		   "RenderOrigin.";
}

// THE OTHER HALF OF 6e's CHANGE, AND IT IS THE ONE THAT WOULD HAVE BEEN MISSED.
//
// PublishRenderPose overwrites the tail's origin with the HEAD's on every
// position update. That was harmless while GetRenderOrigin branched -- nothing
// read the tail for a ragdoll -- and the moment the branch became a load it
// stopped being harmless: a corpse would snap back to where the server last
// said it was, once per update, for as long as the entity is transmitted.
TEST( ClientRenderTail, ABodyTheSolverOwnsDoesNotPublishTheNETWORKEDPose )
{
	const std::string src = CodeOnly( Read( kClientSource ) );
	ASSERT_FALSE( src.empty() );

	const size_t fn = src.find( "void C_StandaloneEntity::PublishRenderPose()" );
	ASSERT_NE( fn, std::string::npos );

	const size_t end = src.find( "\n}\n", fn );
	ASSERT_NE( end, std::string::npos );

	const std::string body = src.substr( fn, end - fn );

	const size_t guard = body.find( "if ( IsRagdoll() )" );
	const size_t write = body.find( "Block().Client.RenderOrigin = Block().Head.Origin;" );

	ASSERT_NE( guard, std::string::npos )
		<< "PublishRenderPose no longer refuses a body the solver owns. The "
		   "physics step writes that body's pose into the tail and this would "
		   "overwrite it with the networked origin on the next update.";
	ASSERT_NE( write, std::string::npos );

	EXPECT_LT( guard, write )
		<< "the guard is AFTER the write, so it guards nothing";
}

TEST( ClientRenderTail, TheDrawnPoseIsPublishedWhereThePoseSettles )
{
	// The tail's pose has to be WRITTEN by the one funnel every position change
	// passes through, or GetRenderOrigin answers a stale frame.
	const std::string src = CodeOnly( Read( kClientSource ) );
	ASSERT_FALSE( src.empty() );

	const size_t fn = src.find( "void C_StandaloneEntity::PublishRenderPose()" );
	ASSERT_NE( fn, std::string::npos )
		<< "PublishRenderPose is gone. The tail's pose is what GetRenderOrigin "
		   "answers; unwritten, it answers whatever it last held.";

	const size_t end = src.find( "\n}\n", fn );
	ASSERT_NE( end, std::string::npos );

	const std::string body = src.substr( fn, end - fn );

	EXPECT_NE( body.find( "Block().Client.RenderOrigin = Block().Head.Origin;" ),
	           std::string::npos );
	EXPECT_NE( body.find( "Block().Client.RenderAngles = Block().Head.Angles;" ),
	           std::string::npos );
}

TEST( ClientRenderTail, TheOrdinaryPathPublishesThePoseFromTheHead )
{
	// OnPositionChanged is the path every networked move takes, and it publishes
	// by COPY: the head has just been given the pose, the two are equal at that
	// moment, and PublishRenderPose is the copy.
	const std::string src = CodeOnly( Read( kClientSource ) );
	ASSERT_FALSE( src.empty() );

	const size_t fn = src.find( "void C_StandaloneEntity::OnPositionChanged()" );
	ASSERT_NE( fn, std::string::npos );

	const size_t end = src.find( "\n}\n", fn );
	ASSERT_NE( end, std::string::npos );

	EXPECT_NE( src.substr( fn, end - fn ).find( "PublishRenderPose();" ), std::string::npos )
		<< "OnPositionChanged moves the entity without publishing the drawn "
		   "pose into the client tail, so GetRenderOrigin answers a stale "
		   "position for everything it moves.";
}

TEST( ClientRenderTail, TheEyePinWritesTheTailAndLeavesTheHeadAlone )
{
	// THE SPLIT STEP 6b OWNS, and the bug it fixes.
	//
	// MoveToEye is the viewmodel's path and it deliberately does NOT go through
	// OnPositionChanged, because a viewmodel-pass renderable is collated from
	// its own list and re-leafing it is work with no effect. It used to write
	// Head.Origin, Head.Angles AND Head.WorldTransform, because
	// RenderableToWorldTransform returned the head's matrix -- so pinning the
	// weapon to the eye moved the ENTITY's collision origin to the eye too,
	// every frame, for the one entity in the level that is deliberately not
	// drawn where it is.
	//
	// With the tail carrying a transform of its own, the eye pin is three tail
	// writes and NO head write at all. A head write reappearing here is the old
	// coupling coming back, and nothing but a trace against a viewmodel would
	// report it.
	const std::string src = CodeOnly( Read( kClientSource ) );
	ASSERT_FALSE( src.empty() );

	const size_t fn = src.find(
		"void C_StandaloneEntity::MoveToEye( const Vector &vecEye, const QAngle &angEye )" );
	ASSERT_NE( fn, std::string::npos ) << "MoveToEye could not be found.";

	const size_t end = src.find( "\n}\n", fn );
	ASSERT_NE( end, std::string::npos );

	const std::string body = src.substr( fn, end - fn );

	EXPECT_NE( body.find( "Block().Client.RenderOrigin = vecEye;" ), std::string::npos )
		<< "the eye pin no longer writes the tail's render origin, so the "
		   "viewmodel draws at wherever the entity last was.";
	EXPECT_NE( body.find( "Block().Client.RenderAngles = angEye;" ), std::string::npos );
	EXPECT_NE( body.find( "Block().Client.RenderTransform" ), std::string::npos )
		<< "the eye pin no longer builds the tail's own transform, so "
		   "RenderableToWorldTransform answers a matrix nothing at the eye "
		   "wrote.";

	EXPECT_EQ( body.find( "Block().Head." ), std::string::npos )
		<< "MoveToEye writes the HEAD again. The head is where the entity IS -- "
		   "it is what CollisionToWorldTransform, GetCollisionOrigin and the "
		   "spatial partition read -- and the viewmodel is not at the eye, it "
		   "is only DRAWN there. This is exactly the coupling step 6b removed "
		   "by giving the tail a transform of its own.";
}

TEST( ClientRenderTail, TheGroupTheLeafSystemIsGivenIsTheOneManagedCodeWrote )
{
	// THE DIRECTION REVERSED IN STEP 6b, and that is the whole of the fact.
	//
	// This used to read "const RenderGroup_t group = RenderGroup();" -- a native
	// rule choosing the group and publishing what it chose. Which list the
	// renderer walks an entity in is a DECISION, so it is ClientRenderPolicy's,
	// and the block is where the answer arrives. The block's copy is no longer a
	// record of what the shell decided; it is the instruction the shell is
	// carrying out.
	//
	// The failure this guards is a native rule growing back beside the managed
	// one -- two opinions about one group, with the leaf system holding whichever
	// ran last.
	const std::string src = CodeOnly( Read( kClientSource ) );
	ASSERT_FALSE( src.empty() );

	const size_t fn = src.find( "void C_StandaloneEntity::AddToLeafSystem()" );
	ASSERT_NE( fn, std::string::npos );

	const std::string body = src.substr( fn, 1800 );

	EXPECT_NE( body.find( "const RenderGroup_t group = (RenderGroup_t)Block().Client.RenderGroup;" ),
	           std::string::npos )
		<< "the group the leaf system is given is no longer a load of what "
		   "managed code decided.";
	EXPECT_NE( body.find( "SetRenderGroup( RenderHandle(), group )" ), std::string::npos )
		<< "the leaf system is given something other than the group the block "
		   "was written with.";
	EXPECT_NE( body.find( "ShellLeaf_Add( ClientLeafSystem(), this, group )" ), std::string::npos )
		<< "the leaf system is given something other than the group the block "
		   "was written with, or the add no longer goes through "
		   "shell_leaf_link.h's guard.";

	EXPECT_EQ( src.find( "RenderGroup_t C_StandaloneEntity::RenderGroup" ), std::string::npos )
		<< "a native RenderGroup() rule is back beside the managed one. Two "
		   "opinions about which list an entity is drawn in, with the leaf "
		   "system holding whichever ran last.";
}

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

// The function bodies this fact reads, by the line that opens each.
std::string FunctionBody( const std::string &src, const char *pszSignature )
{
	const size_t fn = src.find( pszSignature );

	if ( fn == std::string::npos )
		return std::string();

	const size_t end = src.find( "\n}\n", fn );

	return ( end == std::string::npos ) ? std::string() : src.substr( fn, end - fn );
}

}  // namespace

TEST( ClientRenderPolicyShell, EverySetterThatChangesWhatAnEntityIsBumpsTheInputSerial )
{
	const std::string src = CodeOnly( Read( kClientSource ) );
	ASSERT_FALSE( src.empty() );

	// The six writers of a policy input. SetBlending is here for the
	// translucency bit; m_bIgnoreZ and the alpha it also writes are draw-call
	// parameters and decide nothing.
	const char *const kSetters[] =
	{
		"void C_StandaloneEntity::SetDormant( bool bDormant )",
		"void C_StandaloneEntity::PublishModelFacts()",
		"void C_StandaloneEntity::SetShadowCast( int nChoice )",
		"void C_StandaloneEntity::SetFade( int nStyle, float flStartDist, float flEndDist,",
		"void C_StandaloneEntity::SetCustomDraw( const Vector &mins, const Vector &maxs )",
		"void C_StandaloneEntity::SetBlending( bool bTranslucent, bool bIgnoreZ, int nAlpha )",
		"void C_StandaloneEntity::SetViewModelPass( bool bEnable )",
	};

	for ( size_t i = 0; i < sizeof( kSetters ) / sizeof( kSetters[0] ); i++ )
	{
		const std::string body = FunctionBody( src, kSetters[ i ] );

		ASSERT_FALSE( body.empty() ) << kSetters[ i ] << " could not be found.";

		EXPECT_NE( body.find( "BumpPolicyInputs();" ), std::string::npos )
			<< kSetters[ i ] << " changes a render-policy input without bumping "
			   "the input serial. The managed pass decides for an entity when "
			   "that serial moves and at no other time, so this entity keeps "
			   "the render group, the draw answer and the shadow it had before "
			   "the change — until something else about it moves.";
	}
}

TEST( ClientRenderPolicyShell, TheModelFactsAreOnlyReadWhenTheModelIndexMoved )
{
	// The studio header comes out of mdlcache. The model index is networked and
	// the engine's decoder writes it into the block with nothing on this side
	// running, so PostDataUpdate compares — an unconditional call would pay for
	// that read on every tick of every entity that is networked every tick, to
	// learn nothing.
	const std::string src = CodeOnly( Read( kClientSource ) );
	ASSERT_FALSE( src.empty() );

	const std::string body =
		FunctionBody( src, "void C_StandaloneEntity::PostDataUpdate( DataUpdateType_t updateType )" );
	ASSERT_FALSE( body.empty() );

	EXPECT_NE( body.find( "if ( Block().Head.ModelIndex != m_nPublishedModelIndex )" ),
	           std::string::npos )
		<< "PostDataUpdate no longer notices a model index change, so the "
		   "shadow rule is decided from the facts of whatever model the entity "
		   "wore first.";
	EXPECT_NE( body.find( "PublishModelFacts();" ), std::string::npos );
}

TEST( ClientRenderPolicyShell, TheApplySweepIsEdgeTriggeredOffTheOutputSerial )
{
	const std::string src = CodeOnly( Read( kClientSource ) );
	ASSERT_FALSE( src.empty() );

	const std::string body = FunctionBody( src, "void C_StandaloneEntity::ApplyRenderPolicy()" );
	ASSERT_FALSE( body.empty() ) << "ApplyRenderPolicy is gone: nothing hands a decision to the "
	                                "leaf system or the shadow manager.";

	EXPECT_NE( body.find( "if ( Block().Client.PolicyOutputs == m_nAppliedPolicy )" ),
	           std::string::npos )
		<< "the sweep is no longer edge-triggered, so every entity in the map "
		   "is re-inserted into the leaf system and re-offered to the shadow "
		   "manager every frame.";

	EXPECT_NE( body.find( "m_nAppliedPolicy = Block().Client.PolicyOutputs;" ), std::string::npos )
		<< "the sweep never records what it applied, so the edge above is a "
		   "level and fires forever.";
}

TEST( ClientRenderPolicyShell, TheSweepRunsBeforeThePosesAreRepublished )
{
	// ORDER, and it is load-bearing. The sweep is where an entity that has just
	// started drawing is INSERTED into the leaf system; PostInterpolate then
	// tells the leaf system the renderable moved, which does nothing at all for
	// a renderable that is not in it yet.
	const std::string src = CodeOnly( Read( kClientSource ) );
	ASSERT_FALSE( src.empty() );

	const size_t apply = src.find( "StandaloneClients_ApplyRenderPolicy();" );
	const size_t post  = src.find( "StandaloneClients_PostInterpolate();" );

	// The definitions come before the PreRender that calls them, so the LAST of
	// each is the call site.
	const size_t applyCall = src.rfind( "StandaloneClients_ApplyRenderPolicy();" );
	const size_t postCall  = src.rfind( "StandaloneClients_PostInterpolate();" );

	ASSERT_NE( apply, std::string::npos ) << "nothing sweeps the render policy.";
	ASSERT_NE( post, std::string::npos );

	EXPECT_LT( applyCall, postCall )
		<< "the policy sweep runs after the pose republish, so an entity that "
		   "started drawing this frame is told it moved before it is in the "
		   "leaf system to be told about.";
}

TEST( ClientRenderPolicyShell, TheShellDecidesNothingAboutTheShadowAnyMore )
{
	// shadow_cast_policy.h is deleted and its rule is C#'s. What is left native
	// is reading a studio header, which is mdlcache's business. A ShadowCastType
	// that started grading a model again would be a second opinion about every
	// prop's shadow, with the manager holding whichever ran last.
	const std::string src = CodeOnly( Read( kClientSource ) );
	ASSERT_FALSE( src.empty() );

	const std::string body = FunctionBody( src, "ShadowType_t C_StandaloneEntity::ShadowCastType()" );
	ASSERT_FALSE( body.empty() );

	EXPECT_NE( body.find( "return (ShadowType_t)Block().Client.ShadowType;" ), std::string::npos )
		<< "ShadowCastType is no longer a load of what managed code decided.";

	EXPECT_EQ( src.find( "shadow_cast_policy.h" ), std::string::npos )
		<< "the deleted policy header is included again.";
	EXPECT_EQ( src.find( "StandaloneShadowCastType(" ), std::string::npos )
		<< "the native shadow rule is back.";
}

TEST( ClientRenderPolicyShell, TheShadowFlagsCreateShadowIsGivenComeOutOfTheBlock )
{
	const std::string src = CodeOnly( Read( kClientSource ) );
	ASSERT_FALSE( src.empty() );

	const std::string body = FunctionBody( src, "void C_StandaloneEntity::RefreshShadow()" );
	ASSERT_FALSE( body.empty() );

	EXPECT_NE( body.find( "Block().Client.ShadowFlags" ), std::string::npos )
		<< "CreateShadow is given flags the shell worked out rather than the "
		   "ones the policy decided. Getting them wrong is a shadow that exists "
		   "and is the wrong KIND, which no error reports and only a photograph "
		   "shows.";
}

TEST( ClientRenderPolicyShell, TheDrawAnswerIsALoadAndOneLiveEngineFact )
{
	// The three rules that used to be here are ClientRenderPolicy.Drawn's. What
	// stays is the AND with the third-person camera, because the camera is
	// solved during view setup — after every stage a managed pass can run on,
	// and immediately before CViewRender asks this of the viewmodel list.
	const std::string src = CodeOnly( Read( kClientSource ) );
	ASSERT_FALSE( src.empty() );

	const std::string body = FunctionBody( src, "bool C_StandaloneEntity::ShouldDraw( void )" );
	ASSERT_FALSE( body.empty() );

	EXPECT_NE( body.find( "if ( tail.Drawn == 0 )" ), std::string::npos )
		<< "ShouldDraw no longer opens with the answer managed code wrote.";

	EXPECT_NE( body.find( "tail.HideWhenOwnerSeen == 0 || !SourceSharp_ThirdPersonSeesOwner()" ),
	           std::string::npos )
		<< "the one term that cannot be precomputed is gone. Folded into "
		   "Client.Drawn it would be one frame stale, which is a pair of arms "
		   "on screen for a frame after every `thirdperson`.";

	// And the rules are NOT back beside it.
	EXPECT_EQ( body.find( "GetModel()" ), std::string::npos )
		<< "ShouldDraw asks the model again. Whether an entity with no model "
		   "draws is a decision (a beam has none by design), and it is C#'s.";
}

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

TEST( ClientRenderPolicyShell, NoStandaloneEntityIsEverOnTheClientThinkList )
{
	// THE HALF OF §3.9 THAT IS ALREADY TRUE, and it is worth a fact of its own
	// so that the tripwire above is not read as "client think is unported". It
	// is: GetClientThinkable answers NULL, which is what keeps every entity on
	// this hierarchy off CClientThinkList whatever the TU's presence in the
	// build. The scheduler is managed; the TU is dead weight, not a live path.
	const std::string body = CodeOnly(
		ClassBody( Read( kClientHeader ), "class C_StandaloneEntity" ) );
	ASSERT_FALSE( body.empty() );

	EXPECT_NE( body.find( "virtual IClientThinkable *GetClientThinkable() OVERRIDE { return NULL; }" ),
	           std::string::npos )
		<< "GetClientThinkable answers something other than NULL, so entities on "
		   "this hierarchy can reach CClientThinkList -- and the managed "
		   "scheduler and the native one would both be running thinks.";
}
