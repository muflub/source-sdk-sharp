//========= SourceSharp — the ABI slots that answer by doing nothing =========//
//
// WHY THIS FILE EXISTS.
//
// Lane 2e-abi found `ScriptedEntity.PlaySequence` — every dynamic prop's
// sequence call — still routed through a slot that Phase 2 had nulled. It
// returned false for every prop on every map, silently. No test covered it and
// the live gate passed over it twice, because a slot that does nothing is
// indistinguishable at every seam from a slot that had nothing to do.
//
// `shell_entityapi.cpp` holds twenty-eight more of the same shape. They are not
// a mistake: the state they used to read lived on CBasePlayer, which left the
// build in 13c, and answering "nothing" was the honest disposition at the time.
// What is a mistake is that the set is not written down anywhere a change can
// disagree with, so a twenty-ninth can appear without anyone noticing and a
// fixed one can stay on the ledger for ever.
//
// SO THIS TEST DERIVES THE SET FROM THE SOURCE and compares it against the
// recorded one. It is deliberately not a list of assertions about behaviour —
// there is no behaviour to assert about; that is the whole problem — it is an
// inventory that has to be kept honest by hand.
//
// A BODY IS A STUB WHEN NOTHING IN IT REACHES OUTSIDE ITS OWN ARGUMENTS: with
// comments stripped, it contains no `->`, no `::`, and no call to anything.
//
// THIS TEST IS THE INSTRUMENT, not a transcript of one. The census in
// docs/entity-shell.md was taken with a throwaway python classifier written to
// the same rule; the two were written independently and agree at 28, which is
// the only reason either is worth quoting. What survives is this one, because a
// number in a document that no committed check can re-take is a number nobody
// can disagree with.
//
// WHAT MAKES IT FAIL, WHICH IS THE POINT:
//
//   * a slot on the ledger stops being a stub  -> somebody fixed it; take the
//     row out of the ledger here and out of docs/entity-shell.md §2, in the
//     commit that fixed it.
//   * a slot NOT on the ledger becomes a stub  -> somebody emptied a body;
//     that is the 2e-abi shape, and it has to be argued for in the commit
//     message before the name is added here.
//
// Both directions were mutation-checked when this landed: adding `pOut->x = 0;`
// to one recorded body reddens the first, and deleting the one call out of
// EntitySetBounce reddens the second.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <algorithm>
#include <cctype>
#include <cstdio>
#include <set>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// The recorded set: twenty-eight bodies in shell_entityapi.cpp that answer
// without reaching anything.
//
// The three marked (a) have NO managed caller at all and are retired at the ABI
// (docs/abi-retirements.md). The five marked (b) are answered for hierarchy
// players by the ArsenalPlayers decorator before the native slot is reached.
// The remaining twenty, marked (c), have a live managed rule calling them that
// receives nothing back, and each one is a game rule that silently does not
// happen — docs/entity-shell.md §2 names the rule and the consequence for every
// one of them.
//
// THE THREE GROUPS ARE NOT SPELT IN THE DATA, deliberately. Which group a slot
// is in is a fact about the MANAGED tree, and this test cannot read that; a
// column here would be a claim the test could not check and would rot. The
// grouping lives in the doc, where its evidence is.
//-----------------------------------------------------------------------------
// EMPTY SINCE plan_server_native_v2 T2 (2026-10-18): every recorded stub was
// deleted with its slot, and its constant or no-op moved into EngineBinding
// with a comment naming the gap (docs/todo.md lists them). The derived set
// must now be empty too, so a body emptied from here on fails
// TheLedgerIsExactlyTheDerivedSet until it is argued for and recorded.
const std::vector< const char * > kInertSlots = {};

const size_t kNumInertSlots = kInertSlots.size();

//-----------------------------------------------------------------------------
// The three with no managed caller at all, retired at the ABI in this lane.
//
// Kept as their own list rather than folded into the one above, because their
// bodies are DELETED: a retired slot has no definition left to classify, and a
// test that expected one would red the moment the retirement it is describing
// happened.
//-----------------------------------------------------------------------------
const char *const kRetiredSlots[] =
{
	"SourceSharp_GetPlayerWeaponAmmo",
	"SourceSharp_SetPlayerWeaponClip",
	"SourceSharp_TakePlayerAmmo",
	// plan_server_native_v2 T2: eleven whose bodies were constants. The
	// constants are EngineBinding's EnginePlayers now, each with its gap.
	"SourceSharp_AddPlayerScore",
	"SourceSharp_GetPlayerActiveWeapon",
	"SourceSharp_GivePlayerAmmo",
	"SourceSharp_GivePlayerItem",
	"SourceSharp_PlayerDeployLastWeapon",
	"SourceSharp_PlayerDropActiveWeapon",
	"SourceSharp_PlayerGetActiveWeaponEntity",
	"SourceSharp_PlayerGetViewModelEntity",
	"SourceSharp_PlayerGetWeaponEntity",
	"SourceSharp_PlayerHolsterActiveWeapon",
	"SourceSharp_PlayerRemoveAllItems",
	// ...and the movement and entity ones whose bodies were empty or constant.
	"SourceSharp_PlayerSetLaggedMovement",
	"SourceSharp_PlayerSetButtonsDisabled",
	"SourceSharp_PlayerForceButtons",
	"SourceSharp_PlayerSetAllowAutoMovement",
	"SourceSharp_PlayerSetMovementConstraint",
	"SourceSharp_PlayerSetFrozen",
	"SourceSharp_PlayerGetButtons",
	"SourceSharp_EntitySetForceServerRagdoll",
	"SourceSharp_EntityGetArmor",
	"SourceSharp_EntitySetArmor",
	"SourceSharp_EntitySetFriction",
	"SourceSharp_EntitySetTakeDamageMode",
	"SourceSharp_EntityDetectInSkybox",
};

const size_t kNumRetiredSlots = sizeof( kRetiredSlots ) / sizeof( kRetiredSlots[0] );

//=============================================================================
// Reading the file.
//
// Relative to the SOURCE ROOT, which buildcpptests runs from -- the same shape
// every other source-reading test in this directory uses, and the reason none
// of them carries an absolute path.
//=============================================================================

const char *const kEntityApi = "sourcesharp/game/server/shell_entityapi.cpp";

std::string ReadSourceFile( const char *pRelative )
{
	FILE *fp = fopen( pRelative, "rb" );
	if ( !fp )
		return std::string();

	std::string out;
	char buf[ 8192 ];
	size_t n;
	while ( ( n = fread( buf, 1, sizeof( buf ), fp ) ) > 0 )
		out.append( buf, n );

	fclose( fp );
	return out;
}

const std::string &EntityApiSource()
{
	static const std::string s = ReadSourceFile( kEntityApi );
	return s;
}

//-----------------------------------------------------------------------------
// Comments out, string and character literals kept.
//
// A `//` inside a string literal is not a comment, and shell_entityapi.cpp has
// them: a stripper that did not know the difference would swallow the rest of
// the line and could turn a live body into an apparent stub -- which is a false
// entry in an inventory whose entire job is to be exact.
//-----------------------------------------------------------------------------
std::string StripComments( const std::string &src )
{
	std::string out;
	out.reserve( src.size() );

	enum { kCode, kLine, kBlock, kString, kChar } state = kCode;

	for ( size_t i = 0; i < src.size(); i++ )
	{
		const char c = src[i];
		const char d = ( i + 1 < src.size() ) ? src[i + 1] : '\0';

		switch ( state )
		{
		case kCode:
			if ( c == '/' && d == '/' ) { state = kLine;  i++; }
			else if ( c == '/' && d == '*' ) { state = kBlock; i++; }
			else
			{
				if ( c == '"' )  state = kString;
				else if ( c == '\'' ) state = kChar;
				out += c;
			}
			break;

		case kLine:
			if ( c == '\n' ) { state = kCode; out += c; }
			break;

		case kBlock:
			if ( c == '*' && d == '/' ) { state = kCode; i++; }
			else if ( c == '\n' ) out += c;
			break;

		case kString:
		case kChar:
			out += c;
			if ( c == '\\' && i + 1 < src.size() ) { out += d; i++; }
			else if ( ( state == kString && c == '"' ) || ( state == kChar && c == '\'' ) )
				state = kCode;
			break;
		}
	}

	return out;
}

//-----------------------------------------------------------------------------
// The body of a top-level definition of `pName`, braces included, or "" when
// the file does not define it.
//
// Found by the NAME followed by `(` at the start of a definition, then matched
// braces from the first `{` after the closing `)`. Crude on purpose: the
// alternative is a C++ parser, and the only thing this has to be right about is
// where one function's braces begin and end.
//-----------------------------------------------------------------------------
std::string FunctionBody( const std::string &code, const std::string &name )
{
	size_t from = 0;

	while ( true )
	{
		const size_t at = code.find( name, from );
		if ( at == std::string::npos )
			return std::string();

		from = at + name.size();

		// Not a longer identifier that happens to end with this one.
		if ( at > 0 && ( isalnum( (unsigned char)code[at - 1] ) || code[at - 1] == '_' ) )
			continue;

		size_t i = from;
		while ( i < code.size() && isspace( (unsigned char)code[i] ) )
			i++;

		if ( i >= code.size() || code[i] != '(' )
			continue;

		// Past the argument list.
		int depth = 0;
		for ( ; i < code.size(); i++ )
		{
			if ( code[i] == '(' ) depth++;
			else if ( code[i] == ')' && --depth == 0 ) { i++; break; }
		}

		while ( i < code.size() && isspace( (unsigned char)code[i] ) )
			i++;

		// A declaration, or a call. Only a definition has a brace here.
		if ( i >= code.size() || code[i] != '{' )
			continue;

		const size_t open = i;
		depth = 0;
		for ( ; i < code.size(); i++ )
		{
			if ( code[i] == '{' ) depth++;
			else if ( code[i] == '}' && --depth == 0 )
				return code.substr( open, i - open + 1 );
		}

		return std::string();
	}
}

//-----------------------------------------------------------------------------
// Does this body reach anything outside its own arguments?
//
// `->` and `::` are the two ways to touch something that is not a local, and a
// call is the third. Keywords that take a parenthesis are not calls; without
// that exclusion every `if (` in the file would read as a reach and nothing
// would ever be a stub -- a check that cannot fail.
//-----------------------------------------------------------------------------
bool BodyReachesOut( const std::string &body )
{
	if ( body.find( "->" ) != std::string::npos )
		return true;
	if ( body.find( "::" ) != std::string::npos )
		return true;

	static const char *const kKeywords[] =
	{
		"if", "for", "while", "switch", "return", "sizeof", "catch",
		"static_cast", "reinterpret_cast", "const_cast", "dynamic_cast",
	};
	static const size_t kNumKeywords = sizeof( kKeywords ) / sizeof( kKeywords[0] );

	for ( size_t i = 0; i < body.size(); i++ )
	{
		if ( !( isalpha( (unsigned char)body[i] ) || body[i] == '_' ) )
			continue;
		if ( i > 0 && ( isalnum( (unsigned char)body[i - 1] ) || body[i - 1] == '_' ) )
			continue;

		size_t j = i;
		while ( j < body.size() && ( isalnum( (unsigned char)body[j] ) || body[j] == '_' ) )
			j++;

		const std::string word = body.substr( i, j - i );

		size_t k = j;
		while ( k < body.size() && isspace( (unsigned char)body[k] ) )
			k++;

		i = j - 1;

		if ( k >= body.size() || body[k] != '(' )
			continue;

		bool bKeyword = false;
		for ( size_t w = 0; w < kNumKeywords; w++ )
		{
			if ( word == kKeywords[w] ) { bKeyword = true; break; }
		}

		if ( !bKeyword )
			return true;
	}

	return false;
}

//-----------------------------------------------------------------------------
// Every top-level `SourceSharp_*` definition in the file, by name.
//-----------------------------------------------------------------------------
std::vector< std::string > SourceSharpDefinitions( const std::string &code )
{
	std::vector< std::string > out;
	const std::string kPrefix = "SourceSharp_";

	size_t from = 0;
	while ( true )
	{
		const size_t at = code.find( kPrefix, from );
		if ( at == std::string::npos )
			break;

		from = at + kPrefix.size();

		if ( at > 0 && ( isalnum( (unsigned char)code[at - 1] ) || code[at - 1] == '_' ) )
			continue;

		size_t j = at;
		while ( j < code.size() && ( isalnum( (unsigned char)code[j] ) || code[j] == '_' ) )
			j++;

		const std::string name = code.substr( at, j - at );

		if ( FunctionBody( code, name ).empty() )
			continue;

		if ( std::find( out.begin(), out.end(), name ) == out.end() )
			out.push_back( name );
	}

	return out;
}

std::set< std::string > DerivedInertSet()
{
	const std::string code = StripComments( EntityApiSource() );
	const std::vector< std::string > defs = SourceSharpDefinitions( code );

	std::set< std::string > inert;
	for ( size_t i = 0; i < defs.size(); i++ )
	{
		if ( !BodyReachesOut( FunctionBody( code, defs[i] ) ) )
			inert.insert( defs[i] );
	}

	return inert;
}

std::set< std::string > RecordedInertSet()
{
	std::set< std::string > s;
	for ( size_t i = 0; i < kNumInertSlots; i++ )
		s.insert( kInertSlots[i] );
	return s;
}

//=============================================================================
// THE INSTRUMENT IS CHECKED BEFORE IT IS BELIEVED.
//
// Every assertion below rests on StripComments/FunctionBody/BodyReachesOut
// being right about this file. If they are not -- if the file cannot be found,
// or the definition finder returns nothing -- every set comparison would
// compare two empty sets and pass. These three are what stop that.
//=============================================================================

TEST( EntityShellInertSlots, TheSourceFileWasFound )
{
	ASSERT_FALSE( EntityApiSource().empty() )
		<< kEntityApi << " could not be read. buildcpptests runs from src/, and "
		<< "every source-reading test here is relative to it.";
}

TEST( EntityShellInertSlots, TheDefinitionFinderFindsTheWholeFile )
{
	const std::vector< std::string > defs =
		SourceSharpDefinitions( StripComments( EntityApiSource() ) );

	// Sixty-odd at the time of writing. The bound is loose on purpose: this
	// asserts the finder WORKS, not how many slots the file happens to have,
	// and a tight number here would red on every unrelated addition.
	EXPECT_GT( defs.size(), 50u )
		<< "only " << defs.size() << " SourceSharp_ definitions found in "
		<< kEntityApi << " -- the finder is broken, and every set comparison "
		<< "below would be comparing two empty sets.";
}

TEST( EntityShellInertSlots, ABodyThatReachesOutIsNotCalledAStub )
{
	// EntitySetBounce is the smallest live body in the file: a resolve and one
	// call. If the classifier called THIS a stub it would call anything one.
	const std::string code = StripComments( EntityApiSource() );
	const std::string body = FunctionBody( code, "SourceSharp_EntitySetBounce" );

	ASSERT_FALSE( body.empty() ) << "SourceSharp_EntitySetBounce is gone from "
		<< kEntityApi << "; pick another live body for this check.";
	EXPECT_TRUE( BodyReachesOut( body ) )
		<< "the classifier reads a resolve-and-call body as a stub.";
}

TEST( EntityShellInertSlots, AStubIsRecognisedAsOne )
{
	// A FIXTURE, since plan_server_native_v2 T2 deleted the last real stub
	// (PlayerSetLaggedMovement was this check's subject). The text is the shape
	// those bodies had: arguments named and ignored, a constant returned.
	const std::string code = StripComments(
		"int32_t SourceSharp_FixtureStub( uint32_t hPlayer, int32_t nValue )\n"
		"{\n"
		"\t(void)hPlayer;\n"
		"\treturn 0;\n"
		"}\n" );
	const std::string body = FunctionBody( code, "SourceSharp_FixtureStub" );

	ASSERT_FALSE( body.empty() ) << "the definition finder did not find the fixture.";
	EXPECT_FALSE( BodyReachesOut( body ) )
		<< "the classifier reads an empty body as reaching out.";
}

//=============================================================================
// THE INVENTORY.
//=============================================================================

TEST( EntityShellInertSlots, NoRecordedSlotHasQuietlyBeenFixed )
{
	const std::set< std::string > derived = DerivedInertSet();

	for ( size_t i = 0; i < kNumInertSlots; i++ )
	{
		EXPECT_EQ( 1u, derived.count( kInertSlots[i] ) )
			<< kInertSlots[i] << " is on the inert ledger but its body now "
			<< "reaches something. If it was fixed, take its row out of this "
			<< "list AND out of docs/entity-shell.md section 2 in the same "
			<< "commit -- a ledger that lists a working slot is worse than no "
			<< "ledger, because the next lane will not re-measure it.";
	}
}

TEST( EntityShellInertSlots, NoNewSlotHasQuietlyBecomeAStub )
{
	const std::set< std::string > derived = DerivedInertSet();
	const std::set< std::string > recorded = RecordedInertSet();

	for ( std::set< std::string >::const_iterator it = derived.begin();
	      it != derived.end(); ++it )
	{
		EXPECT_EQ( 1u, recorded.count( *it ) )
			<< *it << " answers without reaching anything and is not on the "
			<< "ledger. This is the lane 2e-abi shape: a slot that does nothing "
			<< "is indistinguishable from a slot with nothing to do, and the "
			<< "managed caller finds out at runtime or never. Grep the managed "
			<< "tree for callers of its EngineBinding.cs facade name -- NOT its "
			<< "slot name, which only the wrapper layer uses -- and say in the "
			<< "commit message which rules stop working.";
	}
}

TEST( EntityShellInertSlots, TheLedgerIsExactlyTheDerivedSet )
{
	const std::set< std::string > derived = DerivedInertSet();

	EXPECT_EQ( kNumInertSlots, derived.size() )
		<< "the file holds " << derived.size() << " slot bodies that reach "
		<< "nothing; the ledger records " << kNumInertSlots << ". The two "
		<< "assertions above say which way round.";
}

//=============================================================================
// THE THREE THAT WERE RETIRED.
//
// A retired slot keeps its field in sourcesharp.h and Abi.cs -- that is the ABI
// protocol, and removing one from the middle renumbers every slot after it --
// but its BODY is gone. This is the half of the retirement a later lane can
// check without reading the ABI header.
//=============================================================================

TEST( EntityShellInertSlots, TheRetiredThreeHaveNoBodyLeft )
{
	const std::string code = StripComments( EntityApiSource() );

	for ( size_t i = 0; i < kNumRetiredSlots; i++ )
	{
		EXPECT_TRUE( FunctionBody( code, kRetiredSlots[i] ).empty() )
			<< kRetiredSlots[i] << " is defined in " << kEntityApi << " again. "
			<< "It was retired because no managed caller reached it at all -- "
			<< "not through its Engine.cs wrapper and not through its "
			<< "IEnginePlayers facade name. If a caller has appeared, the "
			<< "retirement row in docs/abi-retirements.md is wrong and has to "
			<< "be answered, not quietly re-implemented.";
	}
}

} // namespace
