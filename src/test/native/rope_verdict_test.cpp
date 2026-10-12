//====== SourceSharp — game/client/c_rope.cpp: the verdict, re-taken =========//
//
// RE-TAKEN A SECOND TIME, BY THE C2 ROPE PORT (plan_client_novalve.md §2
// cluster 6), AND THE ANSWER IS NOW "GONE". Both blockers this file asserted
// went the way it said they would: shell_rope_render.cpp no longer builds a
// C_RopeKeyframe (the simulation and the build are C#'s,
// src/client/managed/Ropes, pinned bit for bit by RopeStockReplayTests), and
// the RopeManager() the two retained callers reach is the shell's executor.
// The facts below are re-pointed at that state; the history above them is
// left as the record of why it took two passes.
//
// Lane U3, docs/typedef-dryrun/lane_u/ledger.md cluster 7. The ledger's verdict
// for C_RopeKeyframe is DELETE, and this file records why the deletion was NOT
// taken here, with each blocker asserted rather than written down. A red in this
// file is not a regression: it means somebody removed a blocker for their own
// reasons and the verdict is worth RE-TAKING. Same standard as
// gib_verdict_test.cpp, bmodels_verdict_test.cpp and
// shim_underive_verdict_test.cpp.
//
// RE-TAKEN ONCE, BY STEP c8-ropeanchor, AND THE ANSWER IS STILL "STAYS" -- for
// a smaller reason than before. The expensive half of the ledger's case was
// never the file: it was the LINE `class C_RopeKeyframe : public C_BaseEntity`,
// which owed 160 of the 161 vtable-only symbol pairs in the entire client
// hierarchy relink. That line is gone. The class now derives from
// CShellRenderable -- an IClientRenderable with a place and a leaf-system row,
// which is all the entity was ever supplying here -- so keeping it costs the
// client no part of the entity hierarchy, and the "delete it to get the vtable
// back" argument has nothing left to buy. The two blockers below are unchanged
// and still real, and the capability argument (this is what DRAWS every rope in
// the game) was always the stronger one.
//
// The word "entity" survives in some of the prose below because the history is
// what it is. Where a FACT is asserted about the class being an entity, it has
// been re-pointed; where the prose describes what the ledger said in 2026, it
// is left alone.
//
// THE LEDGER'S EVIDENCE IS CORRECT AND IT IS NOT THE WHOLE QUESTION.
// C_RopeKeyframe is N-DEAD: it pairs on ServerClass "CRopeKeyframe" and no such
// ServerClass is compiled, because server_sourcesharp (src/server/native/
// CMakeLists.txt) does not compile game/server/rope.cpp. Nothing registers a rope classname on the client. dm_lockdown's
// ropes -- one move_rope and eight keyframe_rope -- are served by managed code,
// Ropes.cs on the server and ClientRope.cs on the client. All of that is true
// and all of it is re-measured below.
//
// WHAT THE LEDGER'S (S) COLUMN UNDERSTATES is that two of the four referrers are
// not incidental link demand, they are the RENDER PATH. server_csharp.vpc's own
// comment beside the rope.cpp exclusion said so in as many words (VPC is gone
// since Phase 6b; the quotation is kept as the record):
//
//     "the drawing is the stock C_RopeKeyframe, built client-side by
//      shell_rope_render.cpp -- c_rope.cpp therefore STAYS in the client build
//      as the render backend, its recv table merely unpaired."
//
// So the managed rope is NOT drawn by managed code. It is drawn by this class,
// which the shim constructs to carry it -- the same shape U2's sprite and dust
// clusters have, and there the fix is to rewrite the shim renderer to draw
// directly. Here the shim is sourcesharp/game/client/shell_rope_render.cpp.
// Deleting the drawer without rewriting it takes the ropes out of dm_lockdown
// with nothing failing at build time.
//
// (c8-ropeanchor did open the shim and did not take that fix, deliberately.
// plan_entity.md §6 keeps the leaf renderers native and names the rope among
// them; a verlet ring with wind, collision and its own mesh builder is that
// renderer, not policy. What it took instead was the ENTITY out of the
// renderer, which is the part §6 does not keep.)
//
// THE FOUR REFERRERS, measured with nm on the debug client at this tip:
//
//   shell_rope_render.o   C_RopeKeyframe::Create, C_RopeKeyframe::
//                         RecomputeSprings                    <-- BLOCKER, T3's
//   c_baseanimating.o     C_RopeKeyframe::CreateFromKeyValues <-- BLOCKER, and
//                         the model-cables path in DelayedInitModelEffects.
//                         CBaseAnimating IS a class this server sends, so it is
//                         reachable, unlike the two dead client (K) routes.
//   cdll_client_int.o     RopeManager(), Rope_ResetCounters()
//   viewrender.o          RopeManager()                       <-- KEEP: free
//                         functions for the rope RENDERER, not for the entity.
//                         The ledger says keep them and it is right.
//
// AND THE "KEEP THE MANAGER" HALF IS NOT FREE, which is the part worth writing
// down for whoever takes this next. IRopeManager::AddToRenderCache is declared
//     virtual void AddToRenderCache( C_RopeKeyframe *pRope ) = 0;
// so the interface the two retained callers reach the manager through is TYPED
// ON THE ENTITY. "Delete the entity, keep the manager" is an interface change
// that lands in the shim as well. Asserted below so the next agent meets it as
// a fact rather than discovering it mid-edit.
//
// A VIRTUAL CALL EMITS NO SYMBOL, so nm's four referrers are a FLOOR on the
// coupling and not a measure of it: every AddToRenderCache / DrawRenderCache /
// OnRenderStart call reaches the manager through RopeManager()'s vtable and
// appears in no symbol table. That is why this file scans SOURCE for the two
// blockers rather than trusting the link.
//
// WHAT WOULD CHANGE THE VERDICT, in one sentence each:
//   * shell_rope_render.cpp stops naming C_RopeKeyframe -- the shim draws ropes
//     itself and the entity's last render caller is gone.
//   * c_baseanimating.cpp stops naming CreateFromKeyValues -- model cables are
//     served some other way, or C_BaseAnimating leaves (ledger cluster 12).
// Either one is a red here.
//
// ANCHORS, NOT LINE NUMBERS, and every scanned source is read with comments
// STRIPPED: this file's own prose spells C_RopeKeyframe, CreateFromKeyValues and
// IMPLEMENT_SERVERCLASS_ST_NOBASE in the paragraphs explaining them, and an
// unstripped scan would read the explanation as the violation. Same rule as
// gib_verdict_test.cpp. Every named file is also asserted to still contain a
// string that identifies it -- a source-scanning test whose subject was renamed
// reads an empty string and reports every absence invariant as satisfied: the
// check that cannot fail.
//
//=============================================================================//

#include <string>
#include <vector>
#include <fstream>
#include <cstring>
#include <sys/stat.h>
#include "gtest/gtest.h"

#include "cmake_graph.h"

#include <set>

namespace
{

//-----------------------------------------------------------------------------
// Finding the tree. Same shape as bmodels_verdict_test.cpp / gib_verdict_test:
// the tests run with src/ as the working directory, but that is not asserted
// anywhere, so try the three places and identify the right one by its contents.
//-----------------------------------------------------------------------------
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

// The mod's map directory, which is beside src/ rather than under it.
const char *MapDir()
{
	static const char *kCandidates[] = { "../game/mod_sharp/maps",
	                                     "game/mod_sharp/maps",
	                                     "../../game/mod_sharp/maps" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		if ( stat( kCandidates[ i ], &st ) == 0 && S_ISDIR( st.st_mode ) )
			return kCandidates[ i ];
	}
	return NULL;
}

std::string ReadRaw( const std::string &strAbsolute )
{
	std::ifstream file( strAbsolute.c_str(), std::ios::binary );
	if ( !file.is_open() )
		return std::string();

	return std::string( ( std::istreambuf_iterator<char>( file ) ),
	                    std::istreambuf_iterator<char>() );
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
				bInBlock = false; i++;
			}
			continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '*' )
		{
			bInBlock = true; i++; continue;
		}
		if ( i + 1 < strIn.size() && strIn[ i ] == '/' && strIn[ i + 1 ] == '/' )
		{
			while ( i < strIn.size() && strIn[ i ] != '\n' ) i++;
			if ( i < strIn.size() ) out += '\n';
			continue;
		}
		out += strIn[ i ];
	}
	return out;
}

std::string ReadStripped( const std::string &strRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	const std::string all = ReadRaw( std::string( pszRoot ) + "/" + strRelative );
	return all.empty() ? all : StripComments( all );
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

// Whole-token search: "C_RopeKeyframe" must not be satisfied by
// "C_RopeKeyframeThing", and "RopeManager" must not be satisfied by
// "CRopeManager" -- the leading C is the exact shape of the CBaseEntity /
// C_BaseEntity substring trap this port has recorded repeatedly.
bool IsIdentChar( char c )
{
	return ( c >= 'a' && c <= 'z' ) || ( c >= 'A' && c <= 'Z' )
	    || ( c >= '0' && c <= '9' ) || c == '_';
}

bool HasWholeToken( const std::string &hay, const std::string &needle )
{
	for ( size_t at = hay.find( needle ); at != std::string::npos;
	      at = hay.find( needle, at + 1 ) )
	{
		const bool bLeft = ( at == 0 ) || !IsIdentChar( hay[ at - 1 ] );
		const size_t end = at + needle.size();
		const bool bRight = ( end >= hay.size() ) || !IsIdentChar( hay[ end ] );
		if ( bLeft && bRight )
			return true;
	}
	return false;
}

struct File_t
{
	const char *pszRel;
	const char *pszAnchor;
};

// The anchor was the client-class registration macro until step 8e-classes
// REMOVED it -- the conclusion of this file's own verdict, see
// TheClientRopeNoLongerPairsOnAnyServerName below. It is now a definition the
// rope cannot be the rope without: RecomputeSprings is what both surviving
// client-only creation paths call, and it is named nowhere else.
const File_t kClientRope   = { "game/client/c_rope.cpp",
	"void C_RopeKeyframe::RecomputeSprings()" };
const File_t kClientRopeHdr = { "game/client/c_rope.h",
	"abstract_class IRopeManager" };
// THE ANCHOR MOVED; NOTHING THIS FILE ASSERTS DID.
//
// It used to be `$SRCDIR\game\shared\baseentity_shared.cpp`, and THE FLIP
// REMOVED THAT ROW from both vpcs along with the rest of the entity hierarchy.
// A stock TU is not an anchor for this port -- removing stock TUs is precisely
// what the port does, so an anchor on one is a proof-of-read the flip can evict.
// Stale here is the worst case: this anchor is the only thing stopping every
// absence invariant it guards from passing vacuously against a file it is not
// really reading.
//
// So the proof-of-read is now the PORT'S OWN shim, which no flip can take, and
// it is the anchor the client-side entries in this family already use.
//
// WHAT EACH SIDE COMPILES is read through cmake_graph.h: server_sourcesharp
// (src/server/native/CMakeLists.txt) and client_sourcesharp
// (src/client/native/CMakeLists.txt). Until Phase 6b it was server_csharp.vpc
// and client_csharp.vpc. The proof-of-read is the same shim on both sides.
const char *const kProofOfRead = "sourcesharp/game/shared/shell_shared.cpp";
const File_t kRopeShim     = { "sourcesharp/game/client/shell_rope_render.cpp",
	"void SourceSharp_RopeDraw(" };
// REPOINTED BY plan_entity.md 5b, not weakened. The caller of
// Rope_ResetCounters is the client DLL's FrameStageNotify, and that method --
// with the whole of CHLClient -- moved out of the stock translation unit into
// sourcesharp/game/client/shell_clientdll.cpp when cdll_client_int.cpp came off
// client_csharp.vpc. The fact this file states is "the caller is still there
// and is still compiled"; the file it lives in changed and the fact did not.
const File_t kCdll         = { "sourcesharp/game/client/shell_clientdll.cpp",
	"RopeManager()->OnRenderStart()" };
// The rope pass's caller: viewrender.cpp's until C7 step 3, the plan
// executor's since (RopeResetCache / RopeDrawCache ops).
const File_t kViewRender   = { "sourcesharp/game/client/shell_view_executor_client.cpp",
	"RopeManager()->DrawRenderCache(" };
const File_t kManagedRopes = { "sourcesharp/managed/SourceSharp.Server/Entities/Effects/Ropes.cs",
	"public sealed class RopeKeyframe : ScriptedEntity" };
const File_t kEntityNames  = { "sourcesharp/managed/SourceSharp.Shared/EntityNames.cs",
	"public const string MoveRope" };

std::string Load( const File_t &f )
{
	const std::string s = ReadStripped( f.pszRel );
	EXPECT_FALSE( s.empty() ) << "could not open " << f.pszRel
		<< " — every invariant below would pass vacuously";
	EXPECT_NE( s.find( f.pszAnchor ), std::string::npos )
		<< f.pszRel << " does not contain its anchor \"" << f.pszAnchor
		<< "\"; the file this test reasons about has moved or been rewritten";
	return s;
}

//-----------------------------------------------------------------------------
// The entity lump of a compiled .bsp, which is plain text. Header: 'VBSP', int
// version, then 64 lump_t of { fileofs, filelen, version, fourCC }; lump 0 is
// the entities. Same reader as bmodels_verdict_test.cpp.
//-----------------------------------------------------------------------------
int ReadInt( const std::string &s, size_t at )
{
	if ( at + 4 > s.size() )
		return -1;
	int v = 0;
	memcpy( &v, s.data() + at, 4 );
	return v;
}

std::string EntityLump( const std::string &strMapPath )
{
	const std::string data = ReadRaw( strMapPath );
	if ( data.size() < 8 + 64 * 16 || data.compare( 0, 4, "VBSP" ) != 0 )
		return std::string();

	const int ofs = ReadInt( data, 8 );
	const int len = ReadInt( data, 12 );
	if ( ofs < 0 || len < 0 || (size_t)( ofs + len ) > data.size() )
		return std::string();

	return data.substr( ofs, len );
}

int CountClassname( const std::string &strLump, const char *pszClassName )
{
	const std::string needle = std::string( "\"classname\" \"" ) + pszClassName + "\"";
	int n = 0;
	for ( size_t at = strLump.find( needle ); at != std::string::npos;
	      at = strLump.find( needle, at + 1 ) )
	{
		n++;
	}
	return n;
}

} // namespace

//-----------------------------------------------------------------------------
// THE VERDICT ITSELF.
//-----------------------------------------------------------------------------
TEST( RopeVerdict, TheClientBuildNoLongerBuildsCRopeCpp )
{
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kProofOfRead ) )
		<< "client_sourcesharp does not compile " << kProofOfRead
		<< " -- the reader is not reading the client's source list";

	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kClient, "game/client/c_rope.cpp" ) )
		<< "c_rope.cpp is back in the CLIENT build. The rope's simulation and "
		   "build are C#'s and shell_rope_render.cpp's executor answers "
		   "RopeManager(): a second definition will not link, and a stock rope "
		   "drawing beside the managed one would be every cable twice.";

	// And it is not even linked into the farm any more (sdk-links/client.txt
	// lost its line): a file only the fork holds is a file nothing here reads.
	EXPECT_TRUE( ReadStripped( kClientRope.pszRel ).empty() )
		<< "game/client/c_rope.cpp is linked into the farm again; sdk-links "
		   "names a stock TU no target compiles.";
}

//-----------------------------------------------------------------------------
// BLOCKER A, REMOVED: the shim no longer builds one. It hands a built strip to
// its own executor.
//-----------------------------------------------------------------------------
TEST( RopeVerdict, TheManagedRopeIsDrawnByTheShellExecutorNotTheStockRope )
{
	const std::string shim = Load( kRopeShim );

	EXPECT_EQ( shim.find( "C_RopeKeyframe::Create" ), std::string::npos )
		<< "shell_rope_render.cpp builds a stock C_RopeKeyframe again. The "
		   "managed rope is simulated and built in C#; the stock class would be "
		   "a second rope on the same span.";

	EXPECT_NE( NoWhitespace( shim ).find( "IRopeManager*RopeManager()" ), std::string::npos )
		<< "shell_rope_render.cpp no longer defines RopeManager(). The plan "
		   "executor resets and draws the rope cache through it; without a definition the "
		   "client does not link, and with c_rope.cpp's the managed strips are "
		   "never drawn.";
}

//-----------------------------------------------------------------------------
// WHAT HAD TO SURVIVE THE DELETION, AND DID.
//
// RopeManager() kept its two callers; Rope_ResetCounters went with the stock
// rope (it zeroed c_rope.cpp's VPROF tick counters).
//-----------------------------------------------------------------------------
TEST( RopeVerdict, TheRopeManagerOutlivedTheStockRopeAndTheCountersDidNot )
{
	const std::string cdll = Load( kCdll );
	const std::string view = Load( kViewRender );

	EXPECT_TRUE( HasWholeToken( cdll, "RopeManager" ) );
	EXPECT_FALSE( HasWholeToken( cdll, "Rope_ResetCounters" ) )
		<< "shell_clientdll.cpp calls Rope_ResetCounters again; it was c_rope.cpp's "
		   "and nothing defines it now.";
	EXPECT_TRUE( HasWholeToken( view, "RopeManager" ) )
		<< "the plan executor has stopped calling RopeManager(): the rope pass is not "
		   "drawn any more, or moved, and the executor's interface needs re-reading.";

	// Both callers must actually be compiled, or the demand above is imaginary.
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, "sourcesharp/game/client/shell_clientdll.cpp" ) );
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, "sourcesharp/game/client/shell_view_executor_client.cpp" ) );
}

//-----------------------------------------------------------------------------
// AND THE CLASS IS NO LONGER AN ENTITY, which is what c8-ropeanchor changed and
// the largest single fact in this file's subject.
//
// Asserted HERE as well as in orphan_clientclass_test.cpp, and the duplication
// is deliberate: that file's fact is about the Phase 5 class-list diff, this
// one is about the verdict. If C_RopeKeyframe is put back on the entity, the
// ledger's cluster-7 arithmetic comes back with it and this verdict has to be
// re-taken a third time.
//-----------------------------------------------------------------------------
TEST( RopeVerdict, TheRopeIsARenderableAndNotAnEntity )
{
	const std::string hdr = Load( kClientRopeHdr );
	const std::string tight = NoWhitespace( hdr );

	EXPECT_NE( tight.find( "classC_RopeKeyframe:publicCShellRenderable{" ),
	           std::string::npos )
		<< "C_RopeKeyframe is not on CShellRenderable. Either it was deleted -- "
		   "see the capability argument at the top of this file -- or it was "
		   "put on some other base, and the vtable arithmetic the ledger's "
		   "cluster 7 rests on needs re-measuring either way.";

	EXPECT_EQ( tight.find( "classC_RopeKeyframe:publicC_BaseEntity{" ),
	           std::string::npos )
		<< "C_RopeKeyframe derives from the client base entity again. That line "
		   "owed 160 of the 161 vtable-only symbol pairs in the whole client "
		   "hierarchy removal set.";

	// The network half (OnDataChanged, ReceiveMessage) went with c8-ropeanchor
	// and the rest of the .cpp with the C2 rope port; the header stays for
	// IRopeManager, which the executor calls.
}

//-----------------------------------------------------------------------------
// AND THE CARVE-OUT IS NOT FREE.
//
// "Delete the entity, keep the manager" reads like a clean split. It is not one:
// the manager's own interface is typed on the entity, so the split is an
// interface change that lands in the shim as well as in this file. Written down
// as an assertion so the next agent meets it before editing rather than during.
//-----------------------------------------------------------------------------
TEST( RopeVerdict, TheRopeManagerInterfaceIsTypedOnTheEntityItWouldOutlive )
{
	const std::string hdr = Load( kClientRopeHdr );

	EXPECT_NE( NoWhitespace( hdr ).find( "AddToRenderCache(C_RopeKeyframe*pRope)" ),
	           std::string::npos )
		<< "IRopeManager::AddToRenderCache no longer takes a C_RopeKeyframe*. If "
		   "the interface has been re-typed off the entity, the expensive half of "
		   "cluster 7 is already done and the verdict is worth re-taking.";
}

//-----------------------------------------------------------------------------
// THE ROPES ARE REAL, which is why any of this matters.
//
// If dm_lockdown had no ropes the deletion would be free whatever the linker
// said. It has nine, and managed serves both classnames — so the render path
// this file protects is one a player looks at.
//-----------------------------------------------------------------------------
TEST( RopeVerdict, TheShippingMapPlacesRopesAndManagedServesThem )
{
	const char *pszMaps = MapDir();
	ASSERT_TRUE( pszMaps != NULL ) << "could not find the mod's map directory";

	const std::string lump = EntityLump( std::string( pszMaps ) + "/dm_lockdown.bsp" );
	ASSERT_FALSE( lump.empty() )
		<< "dm_lockdown.bsp is missing or not a VBSP. It is TRACKED, so this is "
		   "not the ss_sandbox case; every count below would read zero and the "
		   "test would pass by measuring nothing.";

	EXPECT_EQ( CountClassname( lump, "keyframe_rope" ), 8 );
	EXPECT_EQ( CountClassname( lump, "move_rope" ), 1 )
		<< "dm_lockdown no longer places the rope classnames this verdict is "
		   "about. Either the map changed or the reader did; re-measure before "
		   "concluding the ropes are gone.";

	// The positive control for the reader: a classname the map definitely has.
	// Without it a broken lump parser scores zero everywhere and reads as "no
	// ropes", which is the direction that would wave a bad deletion through.
	EXPECT_GT( CountClassname( lump, "env_sprite" ), 0 )
		<< "the entity-lump reader found no env_sprite either, so it is not "
		   "reading the lump — the rope counts above mean nothing";

	// And managed really does answer for them. TWO LINKS, because the
	// registration and the string are in different files: Ropes.cs registers
	// under the SYMBOL EntityNames.MoveRope, and EntityNames.cs is what binds
	// that symbol to the classname the map above actually spells. Asserting
	// only the first would be satisfied by a symbol bound to anything; asserting
	// only the second would be satisfied by a constant nothing registers under.
	const std::string ropes = Load( kManagedRopes );
	const std::string names = Load( kEntityNames );

	EXPECT_NE( ropes.find( "[ScriptedEntity(EntityNames.MoveRope" ), std::string::npos )
		<< "Ropes.cs no longer registers under EntityNames.MoveRope. The server "
		   "half of the rope port is what makes game/server/rope.cpp's exclusion "
		   "safe; if it moved, find where before trusting this verdict.";
	EXPECT_NE( ropes.find( "[ScriptedEntity(EntityNames.KeyframeRope" ), std::string::npos )
		<< "Ropes.cs no longer registers under EntityNames.KeyframeRope";

	EXPECT_NE( NoWhitespace( names ).find( "MoveRope=\"move_rope\"" ), std::string::npos )
		<< "EntityNames.MoveRope is no longer the string \"move_rope\", so the "
		   "registration above and the map classname counted above are no longer "
		   "the same name";
	EXPECT_NE( NoWhitespace( names ).find( "KeyframeRope=\"keyframe_rope\"" ), std::string::npos )
		<< "EntityNames.KeyframeRope is no longer the string \"keyframe_rope\"";
}
