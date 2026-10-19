//========= SourceSharp — step p7-exit ========================================//
//
// THE STOCK CLIENT TUs THAT STAY COMPILED ASK THE SLOT TABLE, NOT THE LIST.
//
// plan_entity.md clause 2 of the definition of done: no compiled translation
// unit may name `cl_entitylist` / `ClientEntityList()`. §6 says which client
// files legitimately stay native when the port is finished -- the leaf system,
// the shadow manager, view render, client mode, input, the HUD canvas, the leaf
// renderers -- so for those files "leave the build" was never the answer and
// "stop naming the entity list" always was.
//
// Step 5a had already done this for the SHIM (shell_cliententitylist.h's banner
// records the 34 sites it collapsed). This step does it for the STOCK client
// TUs that stay: the beam list, the legacy tempent list, the think list, the
// particle systems, the impact effects, the move helper and the client's own
// utility TU. Each one's asks are one of seven questions -- an index or a handle
// in, one engine interface out -- and each is answered by
// SourceSharp_Client*From{Index,Handle}, which tries the slot table FIRST.
//
// WHAT IT CHANGES TODAY: NOTHING OBSERVABLE. An earlier draft of this banner
// claimed the migration was a WIDENING -- that cl_entitylist held stock client
// entities only, so these sites had been answering NULL for managed ones. That
// was WRONG. SourceSharp_ClientEntityBind registers every shell in the stock
// list as well as in the slot table (SourceSharp_LegacyListBind is
// cl_entitylist->AddNetworkableEntity), so both spellings resolve the same
// objects. The retraction is kept here rather than deleted, because a false
// "this is a fix" is precisely what a text gate cannot catch and this one was
// quoted twice before the bind path was read.
//
// WHAT IT DOES CHANGE is the direction: clause 2 is satisfied for these files,
// and the resolvers ask the TABLE FIRST, so when the seam goes they keep
// working with no further edit. TheResolversTryTheSlotTableBeforeTheSeam is
// what pins the order -- a body that asked the seam first would answer the
// stock object for an entry both hold, and would keep answering after the
// table stopped being consulted at all.
//
// WHAT IS DELIBERATELY NOT DELETED. Every migrated site keeps stock's own call
// in its `#ifndef SOURCESHARP` arm, because these files are in client_base.vpc
// as well as client_csharp.vpc and the port's rule is the smallest possible diff
// to stock. TheStockArmSurvivesEveryMigration is that clause, asserted rather
// than assumed -- a "no compiled TU names the list" gate is satisfied just as
// well by deleting the stock arm, and that is the cheapest wrong move here.
//
// THE WAIVER LIST IS RECOMPUTED, NOT LISTED. TheOnlyClientTusLeftNamingTheList
// walks every source client_sourcesharp compiles, strips comments, strips the
// preprocessor arms this build does not take, and requires the set of files
// whose LIVE text still names the stock list to be exactly the named set. A new
// stock TU reaching for `cl_entitylist` reddens it without anybody remembering
// to add a name here, and a waived file that stops needing the waiver reddens it
// too.
//
// PROSE IS NOT A SITE and GUARDS ARE NOT CODE. Both passes are the ones
// orphan_clientclass_test.cpp already needed and for the same reasons: this
// file's own banner names `cl_entitylist` four times, and c_te_legacytempents
// names it in a comment about tempents having no row in it.
//
//=============================================================================//

#include <algorithm>
#include <string>
#include <vector>
#include <set>
#include <map>
#include <fstream>
#include <sstream>
#include <sys/stat.h>
#include "gtest/gtest.h"

#include "cmake_graph.h"

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

bool Exists( const std::string &rel )
{
	const char *pszRoot = SrcRoot();
	if ( pszRoot == NULL )
		return false;
	struct stat st;
	const std::string path = std::string( pszRoot ) + "/" + rel;
	return stat( path.c_str(), &st ) == 0 && S_ISREG( st.st_mode );
}

std::string ReadRaw( const std::string &rel )
{
	const char *pszRoot = SrcRoot();
	EXPECT_TRUE( pszRoot != NULL ) << "cannot locate src/ from the working directory";
	if ( pszRoot == NULL )
		return std::string();

	const std::string path = std::string( pszRoot ) + "/" + rel;
	std::ifstream f( path.c_str() );
	EXPECT_TRUE( f.good() ) << "cannot open " << path;
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

//-----------------------------------------------------------------------------
// Comments out. The prose in this tree names the very macros the scans look
// for -- this file does it in its own banner -- so an unstripped scan reads the
// explanation as the thing explained.
//-----------------------------------------------------------------------------
std::string StripComments( const std::string &in )
{
	std::string out;
	out.reserve( in.size() );
	bool bBlock = false;
	for ( size_t i = 0; i < in.size(); i++ )
	{
		if ( bBlock )
		{
			if ( i + 1 < in.size() && in[ i ] == '*' && in[ i + 1 ] == '/' )
			{
				bBlock = false; i++;
			}
			else if ( in[ i ] == '\n' )
			{
				out += '\n';   // keep the line structure the guard pass needs
			}
			continue;
		}
		if ( i + 1 < in.size() && in[ i ] == '/' && in[ i + 1 ] == '*' )
		{
			bBlock = true; i++; continue;
		}
		if ( i + 1 < in.size() && in[ i ] == '/' && in[ i + 1 ] == '/' )
		{
			while ( i < in.size() && in[ i ] != '\n' ) i++;
			if ( i < in.size() ) out += '\n';
			continue;
		}
		out += in[ i ];
	}
	return out;
}

std::string NoWhitespace( const std::string &in )
{
	std::string out;
	out.reserve( in.size() );
	for ( size_t i = 0; i < in.size(); i++ )
		if ( !isspace( (unsigned char)in[ i ] ) )
			out += in[ i ];
	return out;
}

//-----------------------------------------------------------------------------
// The body of one function, braces counted rather than the first `}` taken.
//-----------------------------------------------------------------------------
std::string BodyOf( const std::string &strStripped, const char *pszSignature )
{
	const size_t sig = strStripped.find( pszSignature );
	EXPECT_NE( sig, std::string::npos ) << "no such function: " << pszSignature;
	if ( sig == std::string::npos )
		return std::string();

	const size_t open = strStripped.find( '{', sig );
	EXPECT_NE( open, std::string::npos );
	if ( open == std::string::npos )
		return std::string();

	int nDepth = 0;
	for ( size_t i = open; i < strStripped.size(); i++ )
	{
		if ( strStripped[ i ] == '{' ) nDepth++;
		else if ( strStripped[ i ] == '}' )
		{
			nDepth--;
			if ( nDepth == 0 )
				return strStripped.substr( open, i - open + 1 );
		}
	}
	ADD_FAILURE() << "unbalanced braces after " << pszSignature;
	return std::string();
}

//-----------------------------------------------------------------------------
// EVERY source the client compiles, as farm-relative paths: client_sourcesharp's
// list, read through cmake_graph.h (compile_commands.json cross-checked against
// src/client/native/CMakeLists.txt).
//
// This was a `$File` walker over client_csharp.vpc, which had to skip `-$File`
// subtractions and follow backslash-continued rows (one carried 16 files) --
// and which took only `$SRCDIR\...` spellings, so the vpc's bare-basename rows
// were never scanned. The CMake list has one spelling for every row, so every
// compiled source is scanned now.
//-----------------------------------------------------------------------------
std::vector< std::string > ClientCompiledSources()
{
	const std::set< std::string > compiled = cmake_graph::CompiledSources( cmake_graph::kClient );
	return std::vector< std::string >( compiled.begin(), compiled.end() );
}

// The -D definitions of one compile command, as NAME or NAME=VALUE, whole.
std::set< std::string > DefinesOf( const std::string &strCommand )
{
	std::set< std::string > out;
	std::istringstream toks( strCommand );
	std::string strTok;
	while ( toks >> strTok )
		if ( strTok.compare( 0, 2, "-D" ) == 0 && strTok.size() > 2 )
			out.insert( strTok.substr( 2 ) );
	return out;
}

//-----------------------------------------------------------------------------
// A MINIMAL PREPROCESSOR, and it is load-bearing rather than tidy.
//
// It knows exactly the four guard names this build decides:
//   SOURCESHARP          DEFINED on both targets (both target_compile_definitions)
//   CLIENT_DLL           defined on the client project only
//   GAME_DLL             defined on the server project only
//   SS_CBASEENTITY_ALIAS NOT defined anywhere -- the armed guard
// Any OTHER condition keeps BOTH arms, which can only ever over-report a class
// and so cannot hide one.
//
// Without this pass the scan reads c_te_effect_dispatch.cpp's
// `#ifndef SOURCESHARP` arm and invents a fifth client class, CTEEffectDispatch,
// that is in no binary.
//-----------------------------------------------------------------------------
struct GuardWorld
{
	bool bClient;
	bool Known( const std::string &name, bool &bValue ) const
	{
		if ( name == "SOURCESHARP" )          { bValue = true;      return true; }
		if ( name == "CLIENT_DLL" )           { bValue = bClient;   return true; }
		if ( name == "GAME_DLL" )             { bValue = !bClient;  return true; }
		if ( name == "SS_CBASEENTITY_ALIAS" ) { bValue = false;     return true; }
		return false;
	}
};

std::string FirstIdentifier( const std::string &in )
{
	size_t i = 0;
	while ( i < in.size() && !( isalpha( (unsigned char)in[ i ] ) || in[ i ] == '_' ) )
		i++;
	size_t start = i;
	while ( i < in.size() && ( isalnum( (unsigned char)in[ i ] ) || in[ i ] == '_' ) )
		i++;
	return in.substr( start, i - start );
}

std::string StripDeadArms( const std::string &in, const GuardWorld &world )
{
	std::vector< std::pair< bool, bool > > stack;   // (known, emitting)
	std::string out;
	size_t pos = 0;
	while ( pos <= in.size() )
	{
		size_t nl = in.find( '\n', pos );
		const std::string line =
			in.substr( pos, ( nl == std::string::npos ? in.size() : nl ) - pos );

		std::string t = line;
		size_t a = t.find_first_not_of( " \t\r" );
		t = ( a == std::string::npos ) ? std::string() : t.substr( a );

		bool bDirective = false;
		if ( !t.empty() && t[ 0 ] == '#' )
		{
			std::string rest = t.substr( 1 );
			size_t b = rest.find_first_not_of( " \t" );
			rest = ( b == std::string::npos ) ? std::string() : rest.substr( b );
			const std::string word = FirstIdentifier( rest );
			const std::string tail = rest.substr( word.size() );

			if ( word == "ifdef" || word == "ifndef" || word == "if" )
			{
				bDirective = true;
				std::string name;
				bool bWant = true;
				bool bParsed = false;
				if ( word == "ifdef" )       { name = FirstIdentifier( tail ); bWant = true;  bParsed = true; }
				else if ( word == "ifndef" ) { name = FirstIdentifier( tail ); bWant = false; bParsed = true; }
				else
				{
					const std::string cond = NoWhitespace( tail );
					if ( cond.compare( 0, 8, "defined(" ) == 0
					    || cond.compare( 0, 7, "defined" ) == 0 )
					{
						name = FirstIdentifier( cond.substr( 7 ) );
						bWant = true; bParsed = true;
					}
					else if ( cond.compare( 0, 8, "!defined" ) == 0 )
					{
						name = FirstIdentifier( cond.substr( 8 ) );
						bWant = false; bParsed = true;
					}
					// A `defined(A) && defined(B)` reads as its FIRST name only,
					// which keeps both arms unless that one name settles it --
					// see the comment above: over-reporting is safe here.
					if ( bParsed && NoWhitespace( tail ).find( "&&" ) != std::string::npos )
						bParsed = false;
					if ( bParsed && NoWhitespace( tail ).find( "||" ) != std::string::npos )
						bParsed = false;
				}
				bool bValue = false;
				if ( bParsed && world.Known( name, bValue ) )
					stack.push_back( std::make_pair( true, bValue == bWant ) );
				else
					stack.push_back( std::make_pair( false, true ) );
			}
			else if ( word == "else" )
			{
				bDirective = true;
				if ( !stack.empty() )
				{
					const bool bKnown = stack.back().first;
					stack.back().second = bKnown ? !stack.back().second : true;
				}
			}
			else if ( word == "elif" )
			{
				bDirective = true;
				if ( !stack.empty() )
					stack.back() = std::make_pair( false, true );
			}
			else if ( word == "endif" )
			{
				bDirective = true;
				if ( !stack.empty() )
					stack.pop_back();
			}
		}

		bool bEmit = true;
		for ( size_t i = 0; i < stack.size(); i++ )
			if ( !stack[ i ].second ) bEmit = false;

		out += ( bDirective || !bEmit ) ? std::string() : line;
		out += '\n';

		if ( nl == std::string::npos )
			break;
		pos = nl + 1;
	}
	return out;
}


//-----------------------------------------------------------------------------
// The gate-2 spellings, as plan_entity.md §5 and tools/entity-gates spell them.
// `ClientEntityList` is matched with its opening parenthesis so that the
// resolver names in shell_cliententitylist.h -- SourceSharp_ClientEntityList is
// not one of them, but CSourceSharpClientEntityList IS a class in
// shell_cliententitylist.cpp -- cannot be confused for a call to the stock
// accessor. A leading identifier character before `ClientEntityList` means it
// is part of a longer name; the check below requires the boundary.
//-----------------------------------------------------------------------------
bool NamesTheStockList( const std::string &live )
{
	if ( live.find( "cl_entitylist" ) != std::string::npos )
		return true;

	for ( size_t p = live.find( "ClientEntityList" ); p != std::string::npos;
	      p = live.find( "ClientEntityList", p + 1 ) )
	{
		if ( p > 0 )
		{
			const char c = live[ p - 1 ];
			if ( isalnum( (unsigned char)c ) || c == '_' )
				continue;              // CSourceSharpClientEntityList, and kin
		}

		size_t q = p + strlen( "ClientEntityList" );
		while ( q < live.size() && ( live[ q ] == ' ' || live[ q ] == '\t' ) )
			q++;
		if ( q < live.size() && live[ q ] == '(' )
			return true;
	}
	return false;
}

// The client build's own preprocessor world: SOURCESHARP and CLIENT_DLL on,
// SS_CBASEENTITY_ALIAS off. Taken from the client's compile definitions (then
// client_csharp.vpc's $PreprocessorDefinitions; client_sourcesharp's
// target_compile_definitions now), the same four names
// orphan_clientclass_test.cpp reads, and asserted against the build below
// rather than trusted.
GuardWorld ClientWorld()
{
	GuardWorld w;
	w.bClient = true;
	return w;
}

std::string LiveText( const std::string &rel )
{
	return StripDeadArms( StripComments( ReadRaw( rel ) ), ClientWorld() );
}

const char *kResolvers  = "sourcesharp/game/client/shell_cliententitylist.cpp";

//-----------------------------------------------------------------------------
// The files this step moved. Named here ONLY so that the "the stock arm is
// still there" fact has a subject; membership of the waiver set is recomputed,
// never read from this list.
//-----------------------------------------------------------------------------
//
// THE LIST SPLIT IN TWO AT THE `residue` WAVE (74b071771..a46e8d645), and the
// split is the whole of what changed here.
//
// Every one of these files carried its stock call inside a
// `#ifndef SOURCESHARP` arm -- which is precisely what the second half of
// TheStockArmSurvivesEveryMigration used to assert. Both projects that build
// this tree define SOURCESHARP (client_csharp.vpc:74, server_csharp.vpc:69
// then; client_sourcesharp's and server_sourcesharp's definitions now), so
// those arms were never compiled by anything, and the wave deleted them.
//
// `kMigratedStockArmGone` is the nine whose arm went -- four now:
// view_beams.cpp left the build with the C2 beam port (its beam system is C#),
// c_te_legacytempents.cpp with the C2 tempent port (its thunk,
// shell_legacy_tempents_client.cpp, resolves no entity through either list),
// and fx.cpp and particles_localspace.cpp with lane c5-particles (fx.cpp's
// routines are C#'s FxEffects; the local-space emitters are PoolEmitters on the
// executor's attachment space),
// and movehelper_client.cpp with C6 (lane c6-input: stock's client move helper
// is ClientPredictionMoveWorld, C#), and cdll_util.cpp with C8 (lane c6-input:
// no importer on the link line), and client_thinklist.cpp with C8 too (no
// client think is scheduled). The last two, particlemgr.cpp and
// particles_new.cpp, left with C7 particles phase 4: no stock TU on the
// client's link line is a migrated one now. Their entity asks (the draw's
// camera object and skip-render entity, the control points' owners) are the
// new-style effects' shell's, which includes the same resolver shim, so the
// row reads it: the successor holds the fact the two files held.
const char *kMigratedStockArmGone[] = {
	"sourcesharp/game/client/shell_new_particles_client.cpp",
};
const size_t kMigratedStockArmGoneCount =
	sizeof( kMigratedStockArmGone ) / sizeof( kMigratedStockArmGone[ 0 ] );

// THE PROOF-OF-READ FOR THE NINE, AND WHY IT IS THIS STRING. An absence over a
// file that did not open reads exactly like an absence over a file that really
// has no stock arm, so each row needs a positive anchor -- and an anchor the
// change under test can evict is not an anchor. `shell_cliententitylist.h` is
// the PORT'S OWN shim header: it is what every one of these files was migrated
// ONTO, no residue pass can delete it (it is live code in the one configuration
// that builds), and a file that stops including it has stopped being a migrated
// file, which is a red this fact should give.
const char *kMigratedAnchor = "#include \"shell_cliententitylist.h\"";

//-----------------------------------------------------------------------------
// The compiled client TUs whose LIVE text may still name the stock list, and
// the reason each is waived. Every one of them leaves with the base cluster --
// they are the cluster -- so this list is the Phase 7 exit's remaining work
// written as a test rather than as a sentence.
//-----------------------------------------------------------------------------
struct Waiver { const char *pszFile; const char *pszWhy; };

// THE WAIVER TABLE. It is EMPTY, and the prose below is its history -- every
// row that was ever in it, why it was waived, and which lane took it out.

	// LANE armed-link TOOK FOUR OUT HERE, AND THE FLIP IS WHY.
	//
	// game/client/cliententitylist.cpp, c_baseentity.cpp,
	// c_baseentity_statics.cpp and c_baseanimating.cpp were waived because they
	// were the stock list and the class it is a cycle around. The armed flip
	// removes all four from client_csharp.vpc, so this scan no longer reaches
	// them and every one of their waivers matched nothing.
	//
	// Removed rather than left standing, for the reason the fact below prints
	// and for the reason five earlier lanes removed theirs: "a waiver that
	// matches nothing excuses whatever is written there next." A waiver on a
	// file that is not in the build is not a smaller waiver -- it is an open
	// door with no one watching it.
	// LANE c8-listseam MOVED THE PORT'S OWN SEAM CODE INTO THIS FILE, and that
	// is why it is here rather than being fixed.
	//
	// Six SourceSharp_LegacyList* bodies and four ask counters were DEFINED in
	// game/client/cliententitylist.cpp -- the port's own code inside the
	// translation unit the client flip deletes, so the lane that deleted the
	// vpc row would have deleted eleven port definitions with it. They are now
	// at the foot of shell_cliententitylist.cpp, and naming `cl_entitylist` is
	// what they ARE: they are the seam, not a caller of it.
	//
	// This is a trade and not a fix, so it is written down. Before the move,
	// the shim named no entity list and the port's coupling was six functions
	// in a file that leaves; after it, the coupling is the same six functions
	// in one delimited region of a file the port owns. tools/entity-gates
	// row 2 counts the same mentions either way -- they moved file, they did
	// not become fewer. What changed is that deleting them is one edit in one
	// place with a named owner, instead of a side effect of somebody else's
	// vpc commit.
	// (shell_cliententitylist.cpp -- "IT IS THE SEAM ITSELF" -- and
	// shell_clientdll.cpp -- "ITERATION AND RELEASE" -- stood here until
	// `the-close`. See the paragraph at the foot of this block.

	// NOT THIS LANE'S FILES. Everything under game/shared/ and
	// sourcesharp/game/shared/ is owned WHOLE, on both sides, by lane p4-exit,
	// which ran concurrently with p7-exit. A shared file's virtuals differ per
	// side, so a one-sided edit here compiles green on the client and breaks
	// the server. The ask is written into
	// .claude/entity-port/requests/p7-exit.md; each row is an index or a handle
	// lookup and each takes the same resolver the client TUs above now take.
	// AND TWO MORE, THE SAME WAY AND IN THE SAME COMMIT.
	//
	// game/shared/baseentity_shared.cpp and game/shared/gamerules.cpp were
	// p4-exit's, waived as shared files whose server arm a one-sided edit would
	// break. The flip removes both from the CLIENT's list, so the hazard that
	// argued for the waiver is gone on this side and the waiver matched nothing.
	// The server keeps its own copy of each and this scan never read that.
	// game/shared/util_shared.cpp WAS HERE, AND LANE c8-shared6 TOOK IT OUT
	// BECAUSE THE TEST SAID TO. Its one row was the client's
	// `UTIL_PlayerByIndex`, `ToBasePlayer( ClientEntityList().GetEnt( i ) )`.
	// That is now a declared, printed refusal: the viewer is a
	// C_StandalonePlayer, IClientUnknown::GetBaseEntity() is NULL for it, and
	// client.so links no C_BasePlayer at all, so the lookup could only ever
	// answer NULL — and `cliententitylist.cpp` leaves in the client flip while
	// util_shared.cpp stays (§6's trace-filter row).
	//
	// Removed rather than left standing for the reason this fact prints:
	// "a waiver that matches nothing excuses whatever is written there next."
	// game/shared/voice_status.cpp, game/shared/collisionproperty.cpp and
	// game/shared/effect_dispatch_data.cpp WERE HERE AND LANE `f-list` TOOK ALL
	// THREE OUT, because the fact below says to and a waiver that matches
	// nothing excuses whatever is written there next.
	//
	// The shared-file hazard the "p4-exit's: shared, both sides" reason names is
	// real and none of the three had it: every row was inside an
	// `#ifdef CLIENT_DLL` / `#else` pair that already existed in the file, so the
	// server arm is untouched in all three by construction.
	//
	// EACH KEPT ITS FACE RATHER THAN BEING WIDENED. GetBaseEntityFromHandle
	// became SourceSharp_ClientSdkEntityFromHandle -- the FILTERED ask, which
	// answers NULL for a managed entity by design -- in both files that used it,
	// because both then call CBaseEntity members on the result.
	// LANE sub-entitylist REMOVED TWO ROWS HERE, and this note is what stops
	// them being put back as waivers instead of fixed.
	//
	// sourcesharp/game/client/sound_emitter.cpp and
	// sourcesharp/game/shared/util_shared_library.cpp were waived as
	// "p4-exit's: the shim's shared half". Neither is a shared-file HAZARD:
	// both rows were CLIENT arms inside `#ifdef CLIENT_DLL`, both were an
	// index lookup, and neither file is in a stock vpc, so there is no server
	// virtual and no !SOURCESHARP arm to disagree with. They take
	// shell_cliententitylist.h's resolvers now, exactly as the nine migrated
	// files above do (kMigratedStockArmGone) -- see TheShimsSharedClientArmsResolveThroughTheSlotTable.
	//
	// The two facts below are the positive half: this list going empty of them
	// says only that they stopped NAMING the list, which deletion satisfies
	// just as well as routing.
	//
	// THE LAST TWO ROWS WENT IN `the-close`, AND THE LIST IS NOW EMPTY.
	//
	// sourcesharp/game/client/shell_cliententitylist.cpp ("it is the seam
	// itself") and sourcesharp/game/client/shell_clientdll.cpp ("iteration and
	// release") were the two survivors. Every line in either of them that named
	// the stock list was inside `#ifndef SS_CBASEENTITY_ALIAS` -- an arm no
	// project compiles, since both targets define that macro -- and `the-close
	// 1/N` deleted all of them. Both files still NAME cl_entitylist in prose,
	// which is why the scan reads LiveText and not the raw file.
	//
	// Removed rather than left standing, for the reason this fact prints and
	// for the reason six earlier lanes removed theirs: "a waiver that matches
	// nothing excuses whatever is written there next."
	//
	// AN EMPTY WAIVER LIST IS THE CLAUSE LANDING, NOT THE FACT WEAKENING. The
	// scan's first direction -- no compiled client TU may name the stock list
	// -- now has no exceptions at all, and it is the direction that catches a
	// new one. The second direction has nothing left to check, which is what
	// "zero waivers" means. And because "nobody names it" is also what an empty
	// file answers, the two survivors' LIVE SUCCESSORS are asserted by name in
	// TheSeamBlockStillAnswersWithoutTheStockList below.
	//
	// A NULL table rather than a zero-length array: C++ has no empty one, and
	// a placeholder row would be a waiver.
const Waiver *const kWaived = NULL;
const size_t kWaivedCount = 0;

} // namespace

//-----------------------------------------------------------------------------
// 1. The three resolvers this step added are built out of the SAME table-first
//    primitive as the two 5a wrote, and not out of the stock list.
//
//    This is the fact that makes the migration a widening rather than a rename:
//    if these bodies asked cl_entitylist, every call site would have been
//    renamed and nothing would have changed.
//-----------------------------------------------------------------------------
TEST( ClientEntityResolve, TheRenderableResolverGoesThroughTheTableFirstPrimitive )
{
	const std::string body = NoWhitespace( BodyOf( StripComments( ReadRaw( kResolvers ) ),
	    "IClientRenderable *SourceSharp_ClientRenderableFromHandle( const CBaseHandle &hEnt )" ) );

	EXPECT_NE( body.find( "ClientUnknownFromHandle(hEnt)" ), std::string::npos )
	    << "SourceSharp_ClientRenderableFromHandle no longer asks "
	       "ClientUnknownFromHandle, which is the one function that tries the "
	       "slot table before the stock seam. Every stock client TU this step "
	       "migrated resolves renderables through here.";
	EXPECT_EQ( body.find( "cl_entitylist" ), std::string::npos )
	    << "the renderable resolver reaches for the stock list directly, which "
	       "makes every migrated call site a rename instead of a fix.";
}

TEST( ClientEntityResolve, TheThinkableResolverGoesThroughTheTableFirstPrimitive )
{
	const std::string body = NoWhitespace( BodyOf( StripComments( ReadRaw( kResolvers ) ),
	    "IClientThinkable *SourceSharp_ClientThinkableFromHandle( const CBaseHandle &hEnt )" ) );

	EXPECT_NE( body.find( "ClientUnknownFromHandle(hEnt)" ), std::string::npos )
	    << "SourceSharp_ClientThinkableFromHandle no longer asks "
	       "ClientUnknownFromHandle, the one primitive both hierarchies resolve "
	       "through (client_thinklist.cpp, its first caller, left in C8).";
	EXPECT_EQ( body.find( "cl_entitylist" ), std::string::npos )
	    << "the thinkable resolver reaches for the stock list directly.";
}

// The SDK-only arms (SourceSharp_ClientSdkEntityFromIndex / ...FromHandle) left
// 2026-10-18 with C_StandaloneEntity's IGameEntity base: they answered NULL for
// every entity on this side (every client GetBaseEntity() answers NULL), so
// their callers now say NULL themselves. Not WIDENED to the shell: a caller
// that wants the shell asks a shell resolver by name.
TEST( ClientEntityResolve, TheSdkOnlyArmsAreGoneNotWidened )
{
	const std::string raw = StripComments( ReadRaw( kResolvers ) );
	ASSERT_GT( raw.size(), 1000u ) << kResolvers << " did not read";
	ASSERT_NE( raw.find( "SourceSharp_ClientShellFromHandle(" ), std::string::npos )
	    << "the control: the slot-table resolver is not in " << kResolvers;

	EXPECT_EQ( raw.find( "SourceSharp_ClientSdkEntityFrom" ), std::string::npos )
	    << "an SDK-only arm is back in " << kResolvers << ". On this side it can "
	       "only answer NULL, or hand out something that is not a C_BaseEntity.";
}

//-----------------------------------------------------------------------------
// 2. The primitive itself asks the table BEFORE the seam. Order, not presence:
//    a body that asked the seam first would answer the stock entity for an
//    entry both hold, and the counters would read zero while the port was
//    unused.
//-----------------------------------------------------------------------------
TEST( ClientEntityResolve, TheResolversTryTheSlotTableBeforeTheSeam )
{
	const std::string body = NoWhitespace( BodyOf( StripComments( ReadRaw( kResolvers ) ),
	    "static IClientUnknown *ClientUnknownFromHandle( const CBaseHandle &hEnt )" ) );

	const size_t table  = body.find( "SourceSharp_ClientShellFromHandle(" );
	const size_t legacy = body.find( "SourceSharp_LegacyListUnknownFromHandle(" );

	ASSERT_NE( table,  std::string::npos ) << "the slot-table arm is gone";
	ASSERT_NE( legacy, std::string::npos ) << "the stock seam is gone";
	EXPECT_LT( table, legacy )
	    << "ClientUnknownFromHandle asks the stock seam before the slot table. "
	       "Table first, seam second, in that order and never the other way "
	       "round -- shell_cliententitylist.cpp says so at the primitive.";
}

//-----------------------------------------------------------------------------
// 3. THE RECOMPUTED WAIVER SET. Every source client_sourcesharp compiles, comments
//    stripped and dead preprocessor arms stripped: the set of files whose LIVE
//    text still names the stock client entity list must be exactly kWaived.
//
//    Both directions are asserted. A new file naming the list is a failure;
//    so is a waived file that no longer needs its waiver, because a stale
//    waiver is a hole that widens every time somebody edits the file.
//-----------------------------------------------------------------------------
TEST( ClientEntityResolve, TheOnlyClientTusLeftNamingTheListAreTheWaivedOnes )
{
	const std::vector< std::string > files = ClientCompiledSources();

	// Proof-of-read, so a broken read fails instead of passing vacuously. (The
	// vpc parse also had a "more than 200 rows" floor; it is retired because
	// CompiledSources throws unless compile_commands.json and the CMakeLists
	// list agree exactly, which an empty or truncated read cannot.)
	ASSERT_TRUE( std::find( files.begin(), files.end(), std::string( kResolvers ) ) != files.end() )
	    << "client_sourcesharp does not compile " << kResolvers
	    << " -- the reader is not reading the client's source list.";

	std::set< std::string > waived;
	for ( size_t i = 0; i < kWaivedCount; i++ )
		waived.insert( kWaived[ i ].pszFile );

	std::set< std::string > found;
	for ( size_t i = 0; i < files.size(); i++ )
	{
		if ( files[ i ].size() < 4
		    || files[ i ].compare( files[ i ].size() - 4, 4, ".cpp" ) != 0 )
			continue;
		// A compiled source that cannot be read is a failure, not a skip: a
		// skip is where a file naming the list would hide.
		if ( !Exists( files[ i ] ) )
		{
			ADD_FAILURE() << "client_sourcesharp compiles " << files[ i ]
			              << ", which is not on disk from the test's cwd";
			continue;
		}
		if ( NamesTheStockList( LiveText( files[ i ] ) ) )
			found.insert( files[ i ] );
	}

	for ( std::set< std::string >::const_iterator it = found.begin();
	      it != found.end(); ++it )
	{
		EXPECT_TRUE( waived.count( *it ) != 0 )
		    << *it << " names the stock client entity list in code this build "
		       "compiles. plan_entity.md clause 2 says no compiled TU may. If "
		       "it is an index or a handle lookup, ask "
		       "sourcesharp/game/client/shell_cliententitylist.h instead; if it "
		       "is iteration, it leaves with the list and belongs in kWaived "
		       "with its reason.";
	}

	for ( size_t i = 0; i < kWaivedCount; i++ )
	{
		EXPECT_TRUE( found.count( kWaived[ i ].pszFile ) != 0 )
		    << kWaived[ i ].pszFile << " is waived and no longer needs to be. "
		       "Take it out of kWaived: a waiver that matches nothing excuses "
		       "whatever is written there next.";
	}
}

//-----------------------------------------------------------------------------
// 3a. THE LIVE SUCCESSOR OF THE LAST TWO WAIVERS.
//
//     The fact above now reads "no compiled client TU names the stock list",
//     with no exceptions -- and an empty file, a deleted one, and a gutted one
//     all satisfy that. The two files that held the last two waivers therefore
//     have their surviving jobs asserted BY NAME here. Neither job was the
//     naming; the naming was how each did it before the list went.
//-----------------------------------------------------------------------------
TEST( ClientEntityResolve, TheSeamBlockStillAnswersWithoutTheStockList )
{
	const std::string seam =
	    LiveText( "sourcesharp/game/client/shell_cliententitylist.cpp" );
	ASSERT_FALSE( seam.empty() );

	// The four SourceSharp_LegacyList* bodies that still HAVE a caller are
	// still DEFINED here. They were moved out of
	// game/client/cliententitylist.cpp by lane c8-listseam precisely so that
	// the client flip's vpc commit could not delete the port's own code as a
	// side effect, and that reason outlives the naming.
	//
	// IT WAS SIX UNTIL PHASE 1c, and the two that went are not a narrowing of
	// this fact. ...HighestEntityIndex and ...NumberOfEntities were read in one
	// place each -- an `if ( nLegacy > ours )` arm whose only effect was to
	// bump a fallback counter -- and against `return -1` and `return 0` those
	// comparisons can never be true. Their absence is asserted separately
	// below, so a reintroduction cannot slip in as a shortened list.
	static const char *const kSeamBodies[] = {
		"CBaseHandle SourceSharp_LegacyListBind( IClientUnknown *pUnknown, int nEntNum, int nSerialNum )",
		"void SourceSharp_LegacyListUnbind( const CBaseHandle &hEnt )",
		"IClientUnknown *SourceSharp_LegacyListUnknownFromIndex( int nEntry )",
		"IClientUnknown *SourceSharp_LegacyListUnknownFromHandle( const CBaseHandle &hEnt )",
	};

	for ( size_t i = 0; i < sizeof( kSeamBodies ) / sizeof( kSeamBodies[ 0 ] ); i++ )
	{
		EXPECT_NE( seam.find( kSeamBodies[ i ] ), std::string::npos )
		    << kSeamBodies[ i ] << " is no longer defined in "
		       "shell_cliententitylist.cpp. The stock list it used to read is "
		       "gone; the SEAM is not, because ClientUnknownFromHandle and "
		       "ClientUnknownFromIndex still call it as their second arm.";
	}

	// AND THE TWO WITH NO CALLER ARE GONE RATHER THAN KEPT AS CONSTANTS. A
	// `return -1` nothing reads is not a seam, it is a line that makes the
	// seam look wider than it is -- and the counter it used to feed read zero
	// on every run that could ever be taken.
	EXPECT_EQ( seam.find( "SourceSharp_LegacyListHighestEntityIndex" ), std::string::npos )
	    << "SourceSharp_LegacyListHighestEntityIndex is back. Its only reader "
	       "was a comparison against a constant -1, which is never true; if it "
	       "is wanted again, say which caller reads it.";
	EXPECT_EQ( seam.find( "SourceSharp_LegacyListNumberOfEntities" ), std::string::npos )
	    << "SourceSharp_LegacyListNumberOfEntities is back. Its only reader "
	       "was a comparison against a constant 0, which is never true; if it "
	       "is wanted again, say which caller reads it.";

	// AND THE ONE THAT IS NOT AN ABSENCE. Three of the four now answer "the
	// stock list contributes nothing" -- NULL -- which is true and which every
	// caller already guards for. The bind is different: the stock list's
	// AddNetworkableEntity did two things, and the one with a subject left is
	// the SetRefEHandle it made on the way through. That is how a shell and the
	// client world come to hold their handle at all.
	EXPECT_NE( seam.find( "pUnknown->SetRefEHandle( hEnt );" ), std::string::npos )
	    << "SourceSharp_LegacyListBind no longer assigns the handle. Nothing "
	       "else issues a client handle, so every shell and the client world "
	       "would carry an invalid one and nothing would report it.";

	// The other file's surviving job: the teardown, which is what its waived
	// iteration-and-release was ever for.
	const std::string dll = LiveText( "sourcesharp/game/client/shell_clientdll.cpp" );
	ASSERT_FALSE( dll.empty() );

	EXPECT_NE( dll.find( "SourceSharp_StandaloneClientsLevelShutdown();" ),
	           std::string::npos )
	    << "CShellClientDLL::LevelShutdown no longer destroys the standalone "
	       "shells. The stock walk it used to be waived for is gone, so this "
	       "is the only thing left that tears a shell down.";
}

//-----------------------------------------------------------------------------
// 3b. THE SHIM'S TWO SHARED FILES, ROUTED RATHER THAN EMPTIED (lane
//     sub-entitylist).
//
//     Fact 3 above now proves neither file NAMES the stock list. That is
//     satisfied just as well by deleting the lookup, which is the failure mode
//     the whole wave brief exists to prevent -- deleting sound_emitter's would
//     silently emit every managed entity's sound as GENDER_NONE, which is the
//     exact defect step 7e closed. So the route is asserted by name.
//
//     The "stock arm survived" rule -- once every migrated row's --
//     deliberately does NOT apply to these two: they
//     are shim files, compiled only by client_sourcesharp and
//     server_sourcesharp, so a !SOURCESHARP arm would be dead text. That
//     assumption is asserted rather than assumed, below.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// 3c. THE FOUR STOCK SHARED FILES LANE `f-list` UN-WAIVED, ROUTED RATHER THAN
//     EMPTIED -- the positive half, one fact per file.
//
//     Fact 3 above only proves these stopped NAMING the stock list, and the
//     comment at the head of kWaived says why that is not enough: deleting the
//     lookup satisfies it exactly as well as routing the lookup does, and
//     COMMON.md's "a compile gate selects for deletion" is this port's own
//     recorded failure mode. So each substitution is asserted BY NAME, and the
//     face it must keep is asserted with it.
//-----------------------------------------------------------------------------
TEST( ClientEntityResolve, VoiceStatusResolvesItsHeadLabelIndexThroughTheSlotTable )
{
	// voice_status.cpp left the client (plan_client_novalve C3 step 4, lane ui):
	// the voice manager is SourceSharp.Client's. Its DrawHeadLabels resolved the
	// speaking player's index and then drew nothing in this build (the draw arm
	// was an entity this client does not have), so the shim's answers the call
	// with no body -- and must not grow a stock-list lookup back.
	const std::string body = NoWhitespace( LiveText( "sourcesharp/game/client/shell_voicestatus_client.cpp" ) );
	ASSERT_FALSE( body.empty() );

	EXPECT_NE( body.find( "voidCVoiceStatus::DrawHeadLabels(){}" ), std::string::npos )
	    << "DrawHeadLabels does something again; stock's drew nothing in this build";
	EXPECT_EQ( body.find( "GetClientEntity" ), std::string::npos )
	    << "the voice shim asks the stock entity list";
}

// effect_dispatch_data.cpp's RecvProxy_EntIndex left client.so with its recv
// table (C2, lane c2-fx): the managed CTEEffectDispatch decodes the index, and
// the one place it becomes a handle again is the registration call's copy in
// shell_effect_dispatch_client.cpp, which this reads instead.
TEST( ClientEntityResolve, TheEffectDataRecvProxyTurnsItsIndexIntoAHandleThroughTheTable )
{
	const std::string body = NoWhitespace( LiveText( "sourcesharp/game/client/shell_effect_dispatch_client.cpp" ) );
	ASSERT_FALSE( body.empty() );

	EXPECT_NE( body.find( "SourceSharp_ClientHandleFromIndex(pData->EntIndex)" ),
	           std::string::npos )
	    << "the effect data's index is no longer turned into a handle through "
	       "the slot table. m_hEntity is what every effect attaches to, so an "
	       "invalid handle is an effect at the world origin with nothing saying why.";

	EXPECT_NE( body.find( "(pData->EntIndex>=0)?SourceSharp_ClientHandleFromIndex(" ), std::string::npos )
	    << "the negative guard is gone. -1 is the wire's \"no entity\"; "
	       "without the guard it is an entry to go looking for.";
}

TEST( ClientEntityResolve, TheEffectDatasThreeAccessorsKeepTheirOwnFaces )
{
	// The three accessors moved out of effect_dispatch_data.cpp with C2.
	const std::string body = NoWhitespace( LiveText( "sourcesharp/game/client/shell_effect_dispatch_client.cpp" ) );
	ASSERT_FALSE( body.empty() );

	EXPECT_NE( body.find( "SourceSharp_ClientRenderableFromHandle(m_hEntity)" ),
	           std::string::npos )
	    << "CEffectData::GetRenderable does not resolve a renderable.";

	// NOT THE WIDE ONE. GetEntity() hands back an IGameEntity *, and no client
	// entity is one: it answers NULL, as the SDK-filtered resolve it used to
	// ask did for every entity (2026-10-18).
	EXPECT_NE( body.find( "IGameEntity*CEffectData::GetEntity()const{returnNULL;}" ),
	           std::string::npos )
	    << "CEffectData::GetEntity no longer answers NULL. It returns an "
	       "IGameEntity *, so any resolve would hand its callers a pointer to an "
	       "object that is not one.";

	EXPECT_NE( body.find( "SourceSharp_ClientNetworkableFromHandle(m_hEntity)" ),
	           std::string::npos )
	    << "CEffectData::entindex no longer resolves the networkable face. "
	       "Phase 13 (e) moved this ask UP to IClientNetworkable precisely so "
	       "that an effect on a managed entity stops reporting -1; resolving it "
	       "as the SDK entity again puts that defect back.";
}

TEST( ClientEntityResolve, TheDirtyPartitionDrainKeepsItsFilteredAskAndItsServerArm )
{
	// READ THE ASSERTIONS, NOT THE NAME. The drain keeps neither ask now. The
	// name is kept so the fact keeps its identity across the rewrite.
	//
	// THE PREMISE THIS FACT WAS BUILT ON WAS WRONG, and that is the part worth
	// carrying away. It read: "This file is compiled into both DLLs and the
	// client-side edit must not have touched the server's half." Both halves of
	// it were true and the conclusion still did not follow, because at 74b071771
	// the client arm AND the server arm were inside ONE
	// `#ifndef SS_CBASEENTITY_ALIAS` region -- game/shared/collisionproperty.cpp
	// :288 to :331 -- with the `#ifndef CLIENT_DLL` / `#else` pair NESTED inside
	// it:
	//
	//     #ifndef SS_CBASEENTITY_ALIAS
	//     #ifndef CLIENT_DLL
	//         CBaseEntity *pEntity = gEntList.GetBaseEntity( handle );
	//     #else
	//         CBaseEntity *pEntity = SourceSharp_ClientSdkEntityFromHandle( handle );
	//     #endif
	//         ... IsEFlagSet / CollisionProp()->UpdatePartition() ...
	//     #endif
	//
	// SS_CBASEENTITY_ALIAS is defined by BOTH projects -- client_csharp.vpc:74
	// and server_csharp.vpc:69 then, both targets' definitions now -- so the outer guard was armed on the SERVER side
	// too. The server arm this fact was protecting was as dead as the client one.
	// A per-side reading of the inner guard answered the wrong question because
	// the outer guard had already decided both.
	//
	// The `residue` wave (74b071771..a46e8d645) deleted the whole region.
	//
	// WHAT THE SUCCESSOR ASSERTS. The site`s own comment, which the wave left in
	// place, says why the BODY was guarded rather than the LOOP: so that the
	// DRAIN still happens -- a handle that somehow arrived would still be
	// detached and freed, and m_DirtyEntities would not grow without bound behind
	// a `#ifndef`. That is the invariant with something to lose, and it is now
	// the presence half here. The absence half is that neither resolver spelling,
	// nor the CollisionProp() re-entry they fed, is anywhere in the file --
	// checked RAW, so a spelling hiding in a dead arm would still be found.
	const std::string body = NoWhitespace( LiveText( "sourcesharp/game/shared/collisionproperty.cpp" ) );
	ASSERT_FALSE( body.empty() );

	// PRESENCE: the drain, in text this build compiles.
	ASSERT_NE( body.find( "while((pCurrent=m_DirtyEntities.Detach())!=NULL)" ),
	           std::string::npos )
	    << "CCollisionProperty's dirty list is no longer detached at all. This is "
	       "the loop the guarded body sat inside, and the reason the BODY was "
	       "guarded rather than the loop; without it the absences below are about "
	       "a function that no longer exists.";

	EXPECT_NE( body.find( "m_DirtyEntities.FreeNode(pCurrent);" ), std::string::npos )
	    << "the dirty-partition drain no longer frees the nodes it detached. The "
	       "drain is the whole of what survived the guard: armed the loop runs "
	       "zero times because nothing can reach AddEntity, but a handle that did "
	       "arrive must still leave the list.";

	// THE FILE IS READ RAW FOR THE ABSENCES, so that a spelling parked in an arm
	// this build does not compile is still found. LiveText would strip it and the
	// absence would pass for the wrong reason -- the failure mode this fact was
	// originally written to avoid, on the other side of the same guard.
	const std::string raw = NoWhitespace( StripComments( ReadRaw( "sourcesharp/game/shared/collisionproperty.cpp" ) ) );
	ASSERT_FALSE( raw.empty() );

	EXPECT_EQ( raw.find( "SourceSharp_ClientSdkEntityFromHandle(handle)" ),
	           std::string::npos )
	    << "the dirty-partition drain resolves a handle again. Armed, this loop "
	       "runs zero times -- AddEntity is the only thing that pushes and nothing "
	       "can reach it -- so a resolve here is dead code that the next residue "
	       "pass removes, and if it is NOT dead then something is pushing handles "
	       "and that is the thing to find.";

	EXPECT_EQ( raw.find( "gEntList.GetBaseEntity(handle)" ), std::string::npos )
	    << "the SERVER arm of the dirty-partition drain is back. It was inside the "
	       "same `#ifndef SS_CBASEENTITY_ALIAS` as the client arm and the server "
	       "target defines that macro too (server_sourcesharp), so it is not "
	       "'stock's half' being protected -- it is dead source on both sides.";

	EXPECT_EQ( raw.find( "pEntity->CollisionProp()->UpdatePartition();" ), std::string::npos )
	    << "the drain calls back into CollisionProp()->UpdatePartition() again. "
	       "That is the re-entry the two resolves existed to feed, and "
	       "CollisionProp() is the accessor that dies with the class.";
}

// TheSoundPatchResolvesItsIndexPerSideAndItsHandleThroughGet stood here. Its
// subject, game/shared/soundenvelope.cpp's SoundPatchEntityFromNetworkIndex,
// is in neither DLL now: the server's patches are shell_soundenvelope.cpp's
// and the client's shell_soundenvelope_client.cpp's (sound cluster 17, lane
// c2-sims), which keeps the index its one caller passes (0, the world).

TEST( ClientEntityResolve, TheTempEntOwnerHeaderInlineResolvesThroughTheSlotTable )
{
	const std::string body = NoWhitespace( StripComments( ReadRaw( "game/client/tempent.h" ) ) );
	ASSERT_FALSE( body.empty() );

	EXPECT_NE( body.find( "SourceSharp_ClientUnknownFromHandle(m_hOwnerEntity)" ),
	           std::string::npos )
	    << "TempEntOwnerHandle no longer resolves through the port's resolver. "
	       "It is the ignore-subject of the tempent collision trace, so a NULL "
	       "there makes a projectile collide with whoever fired it on its "
	       "first frame.";

	EXPECT_EQ( body.find( "ClientEntityList()" ), std::string::npos )
	    << "tempent.h names the stock client entity list again. It is a HEADER, "
	       "so fact 3 above cannot see it -- every TU that includes it emits a "
	       "weak copy of ClientEntityList() carrying cl_entitylist, and the "
	       "client flip's link is what discovers it.";
}

TEST( ClientEntityResolve, TheShimsSharedClientArmsResolveThroughTheSlotTable )
{
	const std::string emitter =
	    NoWhitespace( LiveText( "sourcesharp/game/client/sound_emitter.cpp" ) );
	ASSERT_FALSE( emitter.empty() );

	EXPECT_NE( emitter.find( "SourceSharp_ClientEntityFromIndex(entindex)" ),
	           std::string::npos )
	    << "sound_emitter.cpp's client arm no longer resolves the entity index "
	       "at all. It must ask something that holds BOTH hierarchies -- step "
	       "7e's own reason: C_BaseEntity::Instance answers NULL for every "
	       "entity this port owns, so every managed entity's sound went out as "
	       "GENDER_NONE and a soundscript with gender_male/gender_female "
	       "entries silently took its fallback.";

	const std::string tracer =
	    NoWhitespace( LiveText( "sourcesharp/game/shared/util_shared_library.cpp" ) );
	ASSERT_FALSE( tracer.empty() );

	EXPECT_NE( tracer.find( "SourceSharp_ClientHandleFromIndex(iEntIndex)" ),
	           std::string::npos )
	    << "UTIL_Tracer's client arm no longer turns its entity index into a "
	       "handle. CEffectData::m_hEntity is what the tempent attaches to, so "
	       "an invalid handle is a tracer that starts at the world origin.";
}

TEST( ClientEntityResolve, TheShimsSharedFilesAreBuiltOnlyWithSOURCESHARPSoTheyKeepNoStockArm )
{
	// The reason the "stock arm survived" rule
	// does not reach these two: their client arm names shell_cliententitylist.h,
	// which no SOURCESHARP-less build has, so a !SOURCESHARP arm would be dead
	// text in every build that compiles them.
	//
	// RE-POINTED IN PHASE 6b. This read Valve's stock client_base.vpc and
	// server_base.vpc and asserted neither named the two files. Those describe
	// Valve's stock build, which this repo never builds, and they have left the
	// repo. The property that argument was evidence FOR is about OUR build, and
	// is asked of it directly: every target that compiles either file defines
	// SOURCESHARP. A target added without it is the "stock project" the old
	// fact was the reminder for -- put the !SOURCESHARP arm back, the way
	// game/client/fx.cpp carries one.
	const char *kShimShared[] = {
		"sourcesharp/game/client/sound_emitter.cpp",
		"sourcesharp/game/shared/util_shared_library.cpp",
	};
	const char *kTargets[] = { cmake_graph::kServer, cmake_graph::kClient, cmake_graph::kHost };

	int nCompiling = 0;
	for ( size_t t = 0; t < sizeof( kTargets ) / sizeof( kTargets[ 0 ] ); t++ )
	{
		for ( size_t f = 0; f < sizeof( kShimShared ) / sizeof( kShimShared[ 0 ] ); f++ )
		{
			if ( !cmake_graph::Compiles( kTargets[ t ], kShimShared[ f ] ) )
				continue;
			nCompiling++;
			const std::set< std::string > defs = DefinesOf(
				cmake_graph::CompileCommand( kTargets[ t ], kShimShared[ f ] ) );
			EXPECT_TRUE( defs.count( "SOURCESHARP" ) )
			    << kTargets[ t ] << " (" << cmake_graph::ListFile( kTargets[ t ] )
			    << ") compiles " << kShimShared[ f ] << " without -DSOURCESHARP. "
			       "Its client arm names shell_cliententitylist.h, which no "
			       "stock build has -- put the !SOURCESHARP arm back, the way "
			       "game/client/fx.cpp carries one.";
		}
	}

	// The proof-of-read: the client compiles both. A reader that found neither
	// file anywhere would make every EXPECT above pass by never running.
	EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kShimShared[ 0 ] )
	          && cmake_graph::Compiles( cmake_graph::kClient, kShimShared[ 1 ] ) )
	    << "client_sourcesharp does not compile both shim-shared files, so "
	       "this fact is not measuring what it says (" << nCompiling
	    << " target/file pairs compiled).";
}

//-----------------------------------------------------------------------------
// 4. THE MIGRATION LEFT NOTHING NAMING THE STOCK LIST -- IN EITHER ARM, FOR
//    NINE OF THE TEN.
//
//    THIS SECTION USED TO READ: "THE STOCK ARM SURVIVED EVERY MIGRATION. The
//    gate this step moves is a text count, and a text count is satisfied just
//    as well by deleting stock's call as by routing around it. These files are
//    in client_base.vpc too. So every migrated file must still contain the
//    stock spelling somewhere -- which, given fact 3 above proved none of them
//    names it in LIVE text, can only be inside a `#ifndef SOURCESHARP` arm."
//
//    THE REASONING IS STILL RIGHT AND ITS PREMISE IS NOT. "These files are in
//    client_base.vpc too" was true of the .vpc file on disk and false of this
//    tree's builds: the only two projects here were client_csharp.vpc and
//    server_csharp.vpc (client_sourcesharp and server_sourcesharp now), both
//    define SOURCESHARP, and nothing ever built client_base.vpc. So the
//    `#ifndef SOURCESHARP` arms those nine files
//    carried were source that no compiler in this tree had ever seen, and the
//    `residue` wave (74b071771..a46e8d645) deleted them. It deleted 5,282 lines
//    across 44 stock TUs and the client's objects came out identical -- same
//    count, same DEFINED and UNDEFINED symbol sets.
//
//    WHAT THE FACT PROTECTS NOW. The original worry is unchanged and still
//    live: a text-count gate is satisfied just as well by DELETING stock's call
//    as by routing around it, and this fact is where that would show. What
//    changed is which observation distinguishes the two. It used to be "the
//    stock spelling is still in the file"; it is now "the port's own resolver
//    shim is still included, and the stock spelling is in NEITHER arm". A file
//    that had its call deleted rather than routed loses the include too -- it
//    has nothing to include it for -- so the anchor is the discriminator the
//    deleted arm used to be.
//
//    THE SCANNER'S POSITIVE CONTROL is TheGuardPassIsWhatSeparatesTheTwoArms
//    (a): it asserts NamesTheStockList finds the stock spelling in a two-armed
//    fragment this file owns, so the nine absences below cannot pass because
//    the scanner stopped working.
//-----------------------------------------------------------------------------
TEST( ClientEntityResolve, TheStockArmSurvivesEveryMigration )
{
	// READ THE ASSERTIONS, NOT THE NAME: for nine of the ten rows the stock arm
	// did NOT survive, and that is the correct outcome. The name is kept so the
	// fact keeps its identity across the rewrite.
	for ( size_t i = 0; i < kMigratedStockArmGoneCount; i++ )
	{
		const std::string stripped = StripComments( ReadRaw( kMigratedStockArmGone[ i ] ) );

		// PRESENCE, and it is the port's own shim so that no removal wave can
		// evict the proof-of-read.
		ASSERT_NE( stripped.find( kMigratedAnchor ), std::string::npos )
		    << kMigratedStockArmGone[ i ] << " no longer includes "
		       "shell_cliententitylist.h. Either the file was not read at all -- in "
		       "which case both absences below are vacuous -- or it has stopped "
		       "being a migrated file, which is the deletion-instead-of-routing this "
		       "fact exists to catch.";

		EXPECT_FALSE( NamesTheStockList( stripped ) )
		    << kMigratedStockArmGone[ i ] << " names the stock client entity list "
		       "again, in one arm or the other. If it is in LIVE text the migration "
		       "has been undone; if it is in a `#ifndef SOURCESHARP` arm it is dead "
		       "source -- nothing in this tree compiles that arm -- and the residue "
		       "wave removed nine of exactly those from these nine files.";

		EXPECT_FALSE( NamesTheStockList( LiveText( kMigratedStockArmGone[ i ] ) ) )
		    << kMigratedStockArmGone[ i ] << " still names the stock client entity "
		       "list in code this build compiles.";
	}
}

//-----------------------------------------------------------------------------
// 4b. THE BEAM'S ENDPOINT. view_beams.cpp (Beam_t::entity as a CBaseHandle,
//     BeamEntHandle, the seven BeamEndpointFromIndex lookups) left the build
//     with the C2 beam port: a managed beam holds its endpoints as raw
//     EntityHandle values, and the one native lookup left is the executor's
//     BeamEntityAt -- through the slot-table resolver, never the stock list.
//-----------------------------------------------------------------------------
TEST( ClientEntityResolve, TheBeamEndpointResolverIsTheSlotTableOne )
{
	const std::string live = LiveText( "sourcesharp/game/client/shell_beam_render.cpp" );

	EXPECT_NE( NoWhitespace( live ).find( "uint32_tSourceSharp_BeamEntityAt(int32_tnEntry)"
	                      "{returnRawFromEntity(SourceSharp_ClientEntityFromIndex(nEntry));}" ),
	           std::string::npos )
	    << "the beam executor's BeamEntityAt no longer resolves an entry through "
	       "the slot-table resolver";
	EXPECT_FALSE( NamesTheStockList( live ) )
	    << "shell_beam_render.cpp names the stock client entity list";
}

//-----------------------------------------------------------------------------
// 5. The guard pass is doing something, checked against a case that proves it.
//
//    Without StripDeadArms a file with two arms reads as naming the list in
//    both, so facts 3 and 4 would agree with each other for the wrong reason.
//    This is the control.
//
//    THE CONTROL SUBJECT MOVED, AND THE ASSERTION DID NOT. It used to be
//    game/client/view_beams.cpp, which at 74b071771 carried a
//    `#ifndef SOURCESHARP` arm naming the stock list. The `residue` wave
//    (74b071771..a46e8d645) deleted that arm -- nothing in this tree compiles a
//    !SOURCESHARP arm, so it had never been seen by a compiler -- and with it
//    the only thing that made view_beams.cpp a two-armed file. `ASSERT_TRUE(
//    NamesTheStockList( raw ) )` on it is now false, and a control whose input
//    the change under test can delete was never a control.
//
//    SO THE CONTROL IS TAKEN IN TWO PLACES, and the first of them is the one
//    that cannot be evicted at all:
//
//      (a) AN INLINE SOURCE THIS FILE OWNS. StripDeadArms is handed a
//          two-armed fragment written out below. No wave, flip or vpc edit can
//          reach it. It is also where the OTHER direction is asserted: the pass
//          must KEEP the SOURCESHARP arm, not merely drop the stock one -- a
//          pass that returned the empty string would satisfy every
//          `EXPECT_FALSE( NamesTheStockList( live ) )` in this file, and that
//          is the failure this control exists to make impossible.
//
//      (c) A COMPILED FILE THE PASS MUST NOT EMPTY, so that (a) is not just a
//          test agreeing with itself about a string it wrote.
//-----------------------------------------------------------------------------
TEST( ClientEntityResolve, TheGuardPassIsWhatSeparatesTheTwoArms )
{
	// (a) THE INLINE CONTROL. Both arms, both directions.
	static const char kTwoArmed[] =
	    "static inline C_BaseEntity *SSClientSdkEntityFromHandle( const CBaseHandle &h )\n"
	    "{\n"
	    "#ifdef SOURCESHARP\n"
	    "\treturn SourceSharp_ClientSdkEntityFromHandle( h );\n"
	    "#else\n"
	    "\treturn ClientEntityList().GetBaseEntityFromHandle( h );\n"
	    "#endif\n"
	    "}\n";

	const std::string fragment( kTwoArmed );
	const std::string stripped = StripDeadArms( fragment, ClientWorld() );

	ASSERT_TRUE( NamesTheStockList( fragment ) )
	    << "the inline control fragment does not name the stock list, so it is not "
	       "a control for a pass that is supposed to remove the naming.";

	EXPECT_FALSE( NamesTheStockList( stripped ) )
	    << "the guard pass let a `#else` of `#ifdef SOURCESHARP` through. With it "
	       "not stripping, every fact in this file passes for a reason that has "
	       "nothing to do with the migration.";

	// THE OTHER DIRECTION, and it is the one that matters most: a pass that
	// returned nothing at all would satisfy the line above.
	EXPECT_NE( stripped.find( "SourceSharp_ClientSdkEntityFromHandle" ), std::string::npos )
	    << "the guard pass dropped the SOURCESHARP arm as well as the stock one. A "
	       "pass that strips everything makes every absence in this file vacuous, "
	       "which is the single way this control could have been the check that "
	       "cannot fail.";

	EXPECT_NE( stripped.find( "SSClientSdkEntityFromHandle" ), std::string::npos )
	    << "the guard pass dropped unconditional code outside any guard.";

	// (c) AND A COMPILED FILE THE PASS MUST NOT EMPTY. This is the on-disk half
	//     of (a)'s second direction: a StripDeadArms that returned nothing would
	//     pass every absence in this file, so one real migrated TU is
	//     held to still have its live text after the pass.
	// particlemgr.cpp since the C2 beam port took view_beams.cpp, then the
	// new-style effects' shell (C7 particles phase 4), and since 2026-10-18,
	// when that shell's one resolve (the SDK-only arm) left with
	// C_StandaloneEntity's IGameEntity base, the beam renderer: a compiled TU
	// resolving its entities through the port's slot table.
	const std::string beamsLive = LiveText( "sourcesharp/game/client/shell_beam_render.cpp" );

	EXPECT_NE( beamsLive.find( "SourceSharp_ClientShellFromHandleEntity(" ), std::string::npos )
	    << "shell_beam_render.cpp's live text no longer contains the slot-table resolver "
	       "after the guard pass. Either the pass is stripping code this build "
	       "compiles -- in which case every absence in this file is vacuous -- or "
	       "the file has stopped resolving its entities through the port.";

	EXPECT_GT( beamsLive.size(), 1000u )
	    << "shell_beam_render.cpp's live text is " << beamsLive.size() << " bytes after "
	       "the guard pass. A pass that empties a compiled TU satisfies every "
	       "`EXPECT_FALSE( NamesTheStockList( live ) )` in this file for a reason "
	       "that has nothing to do with the migration.";
}

//-----------------------------------------------------------------------------
// 6. The preprocessor world this file assumes is the one the build sets. If
//    client_sourcesharp stopped defining SOURCESHARP, StripDeadArms would keep
//    the stock arms and facts 3 and 4 would both be measuring nothing.
//-----------------------------------------------------------------------------
TEST( ClientEntityResolve, TheClientProjectStillDefinesTheGuardThisFileAssumes )
{
	// THE DEFINE MUST BE THE WHOLE NAME. A bare substring needle misses ADDED
	// TEXT: over the vpc, `$BASE;SOURCESHARP` on its own matched a list that
	// defined SOURCESHARPX and nothing else, and the needle then needed a
	// terminator that broke every time a define was appended after it. The
	// CMake definitions are one name per token, so the question is a set
	// lookup: SOURCESHARPX is not SOURCESHARP, and a later define is not this
	// fact's business.
	//
	// Asked twice: the list src/client/native/CMakeLists.txt writes, and the
	// compile command the compiler ran (the proof the list reached a TU).
	const std::vector< std::string > listed = cmake_graph::ListedDefinitions( cmake_graph::kClient );
	const std::set< std::string > compiled = cmake_graph::CompileDefinitions( cmake_graph::kClient );

	// Proof-of-read: CLIENT_DLL, the other half of ClientWorld().
	ASSERT_TRUE( std::find( listed.begin(), listed.end(), std::string( "CLIENT_DLL" ) ) != listed.end()
	          && compiled.count( "CLIENT_DLL" ) )
	    << "client_sourcesharp does not define CLIENT_DLL -- the reader is not "
	       "reading the client's definitions, or ClientWorld() is wrong.";

	EXPECT_TRUE( std::find( listed.begin(), listed.end(), std::string( "SOURCESHARP" ) ) != listed.end() )
	    << "src/client/native/CMakeLists.txt no longer defines SOURCESHARP for "
	       "client_sourcesharp, so the guard world in this file is wrong and "
	       "every arm it strips is one the build actually compiles.";
	EXPECT_TRUE( compiled.count( "SOURCESHARP" ) )
	    << "client_sourcesharp's compile command carries no -DSOURCESHARP, so "
	       "the guard world in this file is wrong.";
}

//-----------------------------------------------------------------------------
// THE IMPACT'S CONTROL-POINT ENTITY IS THE SHELL THE TRACE HIT (2026-10-18).
//
// The impact effects bind points 0-2 to the entity the bullet hit; the
// particle query reads only whether a point has an object (m_pObject != NULL),
// which is what managed's hit-box answers are keyed on. That entity came from
// TraceGameEntity, i.e. GetGameEntity(), which answers NULL for every client
// object once C_StandaloneEntity is no IGameEntity -- so the binding would have
// gone silently. It comes from the slot table now.
//
// AND THE HANDLE IS A REAL ONE. The owners were an EHANDLE assigned from an
// IGameEntity *, which networkvar.h's SourceSharp_EntityHandle refuses: a
// Warning, an AssertMsg and the INVALID handle, for every managed entity a
// bullet hit. A CBaseHandle from the shell's own GetRefEHandle() is the handle.
//-----------------------------------------------------------------------------
static size_t CountNeedle( const std::string &hay, const std::string &needle )
{
	size_t n = 0;
	for ( size_t at = hay.find( needle ); at != std::string::npos; at = hay.find( needle, at + needle.size() ) )
		n++;
	return n;
}

TEST( ClientEntityResolve, TheImpactControlPointsBindTheShellTheTraceHit )
{
	const std::string impact = NoWhitespace( LiveText( "sourcesharp/game/client/shell_impact_effects.cpp" ) );
	ASSERT_GT( impact.size(), 1000u ) << "shell_impact_effects.cpp did not read";

	EXPECT_EQ( 3u, CountNeedle( impact, "SourceSharp_ClientShellFromTraceHit(ss::TraceHit(tr)));" ) )
	    << "the three SetImpactControlPoint calls no longer hand it the shell the "
	       "trace hit. TraceGameEntity answers NULL for every client object, so "
	       "an impact on a managed entity would bind no object to its points.";
	EXPECT_EQ( std::string::npos, impact.find( "TraceGameEntity(" ) )
	    << "the impact effects ask TraceGameEntity again, which answers NULL on "
	       "this side.";
}

TEST( ClientEntityResolve, TheControlPointOwnersAreRealHandles )
{
	const std::string header = NoWhitespace( StripComments( ReadRaw( "sourcesharp/game/client/shell_new_particles.h" ) ) );
	const std::string body = NoWhitespace( LiveText( "sourcesharp/game/client/shell_new_particles_client.cpp" ) );
	ASSERT_GT( header.size(), 500u ) << "shell_new_particles.h did not read";
	ASSERT_GT( body.size(), 1000u ) << "shell_new_particles_client.cpp did not read";

	EXPECT_NE( std::string::npos, header.find( "CBaseHandlem_hControlPointOwners[MAX_PARTICLE_CONTROL_POINTS];" ) )
	    << "the control points' owners are not plain handles. An EHANDLE assigned "
	       "from an entity pointer goes through networkvar.h's refusal and stores "
	       "the INVALID handle.";
	EXPECT_NE( std::string::npos, body.find( "m_hControlPointOwners[nWhichPoint]=pEntity->GetRefEHandle();" ) )
	    << "SetControlPointEntity no longer stores the entity's own handle.";
	EXPECT_NE( std::string::npos, body.find( "returnSourceSharp_ClientShellFromHandle(m_hControlPointOwners[nWhichPoint]);" ) )
	    << "GetControlPointEntity no longer resolves its handle through the slot table.";
}

//-----------------------------------------------------------------------------
// THE CLIENT SHELL IS NO IGameEntity (2026-10-18). The build's own gate is
// shell_standalone_client.cpp's static_assert, shown failing against the
// shell while it still derived; this pins that the gate is there and that the
// class head says so.
//-----------------------------------------------------------------------------
TEST( ClientEntityResolve, TheClientShellIsNoGameEntityAndTheBuildSaysSo )
{
	const std::string header = NoWhitespace( StripComments( ReadRaw( "sourcesharp/game/client/shell_standalone_client.h" ) ) );
	const std::string body = NoWhitespace( LiveText( "sourcesharp/game/client/shell_standalone_client.cpp" ) );
	ASSERT_GT( header.size(), 1000u ) << "shell_standalone_client.h did not read";
	ASSERT_GT( body.size(), 1000u ) << "shell_standalone_client.cpp did not read";

	EXPECT_NE( std::string::npos, header.find( "classC_StandaloneEntity:publicIClientEntity,publicICollideable{" ) )
	    << "C_StandaloneEntity's base list changed. It is IClientEntity and "
	       "ICollideable; IGameEntity left with its six inert slots.";
	EXPECT_NE( std::string::npos, body.find( "static_assert(!std::is_base_of<IGameEntity,C_StandaloneEntity>::value," ) )
	    << "the build-time gate that the client shell is no IGameEntity is gone.";
	EXPECT_EQ( std::string::npos, header.find( "GetGameEntity()OVERRIDE" ) )
	    << "the shell answers GetGameEntity() again; on this side it is "
	       "IClientEntity's NULL.";
}
