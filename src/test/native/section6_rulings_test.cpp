//========= SourceSharp ========================================================//
//
// section6_rulings_test.cpp -- lane `loose-ends`, the two §6 rulings nobody
// had made.
//
// WHAT THIS PINS, AND WHY A RULING NEEDS PINNING AT ALL.
//
// `c7-fx` found `EffectsClient.cpp` on no §6 list and `c7-leaf` found
// `toolframework_client.cpp` on no §6 list, and both lanes correctly declined
// to rule rather than acting on a list that did not mention their file. Lane
// `loose-ends` ruled: both STAY NATIVE and are de-entitied in place, and both
// already are. §6 now carries both rows and both files carry the argument.
//
// A ruling is prose, and prose drifts silently. What can be checked is the
// three things the ruling RESTS on, so that a tree in which any of them stops
// being true fails here instead of quietly making §6 wrong:
//
//   1. §6 names both files. A ruling that is only in a source comment is a
//      ruling the next census lane will not find.
//   2. EffectsClient.cpp is still entity-free IN CODE. Its whole argument for
//      staying is that it decides nothing and touches no entity; the day it
//      grows an entity read, the ruling has to be made again.
//   3. toolframework_client.cpp's tool-recording ASKS are still inside a
//      SS_CBASEENTITY_ALIAS region, the last region still reaches past the
//      last ask, and NO entity token survives outside every region.
//      c7-leaf's finding was that extracting the IsToolRecording() gate alone
//      would leave the two GetToolHandle() asks compiled and unguarded, so
//      "guarded" means the WHOLE tail, not the gate.
//
// AND WRITING THESE FACTS CORRECTED THE RULING TWICE, which is the argument for
// having them at all. The ruling first said "the two entity edges are inside
// ONE region": there were TWO regions, and the first version of this file found
// only the first and called the second's contents unguarded. It also said the
// file was entity-free: it was not. `CRecordEffectOwner`'s constructor took a
// `C_BaseEntity *` in its SIGNATURE, outside every guard, and the armed arm of
// its body never read it. That was pinned here as exactly one, with a named
// follow-on, rather than smoothed over.
//
// THE FOLLOW-ON WAS TAKEN, by lane `defect-convar-notify`, and the numbers this
// file pins MOVED because of it: the parameter is an `IClientEntity *`, the
// body asks `clienttools->GetToolHandleForEntityByIndex( pEntity->entindex() )`
// -- a question BOTH hierarchies answer -- and the region that used to wrap it
// is gone. So the unguarded-token count is 0 and was 1, and the region count is
// 1 and was 2. Both are still pinned, because a count that is allowed to drift
// downward on its own cannot tell a retype from a deletion; the fact
// RecordEffectOwnerAsksTheToolFrameworkNotTheEntity is what says which happened.
//
// COMMENTS AND STRING LITERALS ARE STRIPPED FIRST, and this file is the reason
// why: the headers this lane added to both files QUOTE the very tokens these
// facts search for. A raw grep would find `IsToolRecording` in a comment about
// `IsToolRecording` and pass on a file whose code had been deleted.
//
//=============================================================================//
#include <gtest/gtest.h>

#include <fstream>
#include <sstream>
#include <string>
#include <cstring>
#include <unistd.h>
#include <utility>
#include <vector>

namespace
{

// Copied in shape from standalone_solidity_order_test.cpp, which needs the same
// two things and cannot share them: these tests each link exactly one .cpp.
const char *SrcRoot()
{
	static std::string s_strRoot;
	if ( !s_strRoot.empty() )
		return s_strRoot.c_str();

	std::string strPath = ".";
	for ( int i = 0; i < 8; i++ )
	{
		std::ifstream probe( ( strPath + "/game/client/toolframework_client.cpp" ).c_str() );
		if ( probe.good() )
		{
			s_strRoot = strPath;
			return s_strRoot.c_str();
		}
		strPath += "/..";
	}
	return NULL;
}

std::string ReadRaw( const std::string &strRel )
{
	const char *pszRoot = SrcRoot();
	EXPECT_TRUE( pszRoot != NULL ) << "cannot locate src/ from the working directory";
	if ( pszRoot == NULL )
		return std::string();

	const std::string path = std::string( pszRoot ) + "/" + strRel;
	std::ifstream f( path.c_str() );
	EXPECT_TRUE( f.good() ) << "cannot open " << path;
	std::stringstream ss;
	ss << f.rdbuf();
	return ss.str();
}

std::string StripCommentsAndStrings( const std::string &s )
{
	std::string out;
	out.reserve( s.size() );
	enum { CODE, LINE_COMMENT, BLOCK_COMMENT, STRING, CHARLIT } state = CODE;

	for ( size_t i = 0; i < s.size(); i++ )
	{
		const char c = s[ i ];
		const char n = ( i + 1 < s.size() ) ? s[ i + 1 ] : '\0';
		switch ( state )
		{
		case CODE:
			if ( c == '/' && n == '/' ) { state = LINE_COMMENT; i++; }
			else if ( c == '/' && n == '*' ) { state = BLOCK_COMMENT; i++; }
			else if ( c == '"' ) { state = STRING; out += ' '; }
			else if ( c == '\'' ) { state = CHARLIT; out += ' '; }
			else out += c;
			break;
		case LINE_COMMENT:
			if ( c == '\n' ) { state = CODE; out += '\n'; }
			break;
		case BLOCK_COMMENT:
			if ( c == '*' && n == '/' ) { state = CODE; i++; }
			else if ( c == '\n' ) out += '\n';
			break;
		case STRING:
			if ( c == '\\' ) i++;
			else if ( c == '"' ) state = CODE;
			break;
		case CHARLIT:
			if ( c == '\\' ) i++;
			else if ( c == '\'' ) state = CODE;
			break;
		}
	}
	return out;
}

int CountOf( const std::string &hay, const std::string &needle )
{
	int n = 0;
	for ( size_t at = hay.find( needle ); at != std::string::npos;
	      at = hay.find( needle, at + needle.size() ) )
		n++;
	return n;
}

// Every `#ifndef SS_CBASEENTITY_ALIAS` ... `#endif` region, as [start, end).
//
// A LIST, NOT THE FIRST ONE. This file has TWO of them, and the first version
// of this test found only the first and reported the second region's contents
// as unguarded. The pairing is naive on purpose -- the next `#endif` -- and
// ToolframeworkHasTwoGuardRegionsAndNeitherIsNested is the fact that says so
// out loud, because a
// nested `#if` would silently close a region early and turn every fact here
// into a pass.
std::vector< std::pair< size_t, size_t > > GuardRegions( const std::string &code )
{
	std::vector< std::pair< size_t, size_t > > out;
	const std::string open = "#ifndef SS_CBASEENTITY_ALIAS";
	for ( size_t at = code.find( open ); at != std::string::npos;
	      at = code.find( open, at + open.size() ) )
	{
		// THE WHOLE TOKEN. `#ifndef SS_CBASEENTITY_ALIAS_SOMETHING` is not this
		// guard, and a prefix match on it opens a region that does not exist --
		// found by a mutation of this very file, which invented such a name and
		// watched two facts that should have gone red stay green.
		const char after = ( at + strlen( open.c_str() ) < code.size() )
			? code[ at + open.size() ] : '\n';
		if ( after != ' ' && after != '\t' && after != '\r' && after != '\n' )
			continue;

		const size_t end = code.find( "#endif", at );
		if ( end == std::string::npos )
			break;
		out.push_back( std::make_pair( at, end ) );
	}
	return out;
}

bool Inside( const std::vector< std::pair< size_t, size_t > > &regions, size_t at )
{
	for ( size_t i = 0; i < regions.size(); i++ )
	{
		if ( at > regions[ i ].first && at < regions[ i ].second )
			return true;
	}
	return false;
}

// ---------------------------------------------------------------------------
// THE DE-ENTITY BILL'S COUNTER, RE-IMPLEMENTED HERE BECAUSE IT DOES NOT EXIST.
//
// `c8-rulings` ruled on the 65 unruled client-closure TUs and quantified the
// "de-entitied in place" bill for the 48 that stay. It was forbidden `src/` and
// `tools/`, so ONLY THE PROSE LANDED: the script that produced 159/109/34 is
// not in the tree and cannot be re-run. A number in §6 that no longer has a
// measurement behind it is exactly what the ruling's own closing paragraph
// warns about -- "a ruling's prose is not a ruling's measurement, and only the
// second is checked by a test" -- so the measurement is rebuilt here.
//
// IT IS CALIBRATED, NOT ASSUMED. Run over the whole 48 this counter reproduces
// two of the ruling's published numbers exactly: `cdll_util.cpp` reads 26, and
// 34 of the 48 are already entity-free. That agreement on two independent
// quantities is what licenses it to disagree about the third -- see
// RagdollSplitIsSixBeforeTheClassThatLeaves.
// ---------------------------------------------------------------------------

bool IsWordChar( char c )
{
	return ( c >= 'a' && c <= 'z' ) || ( c >= 'A' && c <= 'Z' )
	    || ( c >= '0' && c <= '9' ) || c == '_';
}

// WORD-BOUNDED ON BOTH SIDES, and `game/client/ragdoll.cpp` is why. It contains
// `CBaseAnimatingHandle` twice and a prefix match on `CBaseAnimating` counts
// them as entity tokens. It also contains `C_BaseAnimating`, which a prefix
// match would find inside `C_BaseAnimatingOverlay`. With the boundary check the
// order of the table below does not matter.
const char *kEntityTypes[] = {
	"C_BaseEntity", "CBaseEntity",
	"C_BasePlayer", "CBasePlayer",
	"C_BaseAnimating", "CBaseAnimating",
	"C_BaseAnimatingOverlay", "CBaseAnimatingOverlay",
	"C_BaseCombatCharacter", "CBaseCombatCharacter",
	"C_BaseCombatWeapon", "CBaseCombatWeapon",
	"C_BaseFlex", "CBaseFlex",
	"CWorld", "C_World",
};

int CountEntityTokens( const std::string &code )
{
	int n = 0;
	for ( size_t i = 0; i < sizeof( kEntityTypes ) / sizeof( kEntityTypes[ 0 ] ); i++ )
	{
		const std::string tok( kEntityTypes[ i ] );
		for ( size_t at = code.find( tok ); at != std::string::npos;
		      at = code.find( tok, at + 1 ) )
		{
			if ( at > 0 && IsWordChar( code[ at - 1 ] ) )
				continue;
			const size_t end = at + tok.size();
			if ( end < code.size() && IsWordChar( code[ end ] ) )
				continue;
			n++;
		}
	}
	return n;
}

// Blank out every line inside a DEAD preprocessor arm, tracking nesting by
// DEPTH rather than by "the next #endif".
//
//   #ifndef SS_CBASEENTITY_ALIAS  -- the port's guard: the arm is stock, dead
//   #ifndef SOURCESHARP           -- same
//   #ifdef  SOURCESHARP           -- the port's arm is live, its #else is dead
//
// A token in a dead arm is text the flip already removed. This is the
// difference between §6's "tokens in the text" and "still live", and it is what
// makes `physics_main_client.cpp` read 20 and 0 rather than 20 and 20.
std::string LiveCodeOnly( const std::string &code )
{
	std::vector< std::pair< int, int > > stack;   // (arm 0 live?, arm 1 live?)
	std::vector< int > arm;
	std::string out;

	size_t at = 0;
	while ( at <= code.size() )
	{
		size_t eol = code.find( '\n', at );
		if ( eol == std::string::npos )
			eol = code.size();
		const std::string line = code.substr( at, eol - at );

		size_t h = line.find_first_not_of( " \t" );
		bool bDirective = ( h != std::string::npos && line[ h ] == '#' );
		std::string d, name;
		if ( bDirective )
		{
			size_t p = line.find_first_not_of( " \t", h + 1 );
			if ( p != std::string::npos )
			{
				size_t q = p;
				while ( q < line.size() && IsWordChar( line[ q ] ) ) q++;
				d = line.substr( p, q - p );
				size_t r = line.find_first_not_of( " \t", q );
				if ( r != std::string::npos )
				{
					size_t s = r;
					while ( s < line.size() && IsWordChar( line[ s ] ) ) s++;
					name = line.substr( r, s - r );
				}
			}
		}

		if ( bDirective && ( d == "ifndef" || d == "ifdef" || d == "if" ) )
		{
			if ( d == "ifndef" && ( name == "SS_CBASEENTITY_ALIAS" || name == "SOURCESHARP" ) )
				stack.push_back( std::make_pair( 0, 1 ) );
			else if ( d == "ifdef" && name == "SOURCESHARP" )
				stack.push_back( std::make_pair( 1, 0 ) );
			else
				stack.push_back( std::make_pair( 1, 1 ) );
			arm.push_back( 0 );
		}
		else if ( bDirective && ( d == "else" || d == "elif" ) )
		{
			if ( !arm.empty() )
				arm.back() = 1;
		}
		else if ( bDirective && d == "endif" )
		{
			if ( !stack.empty() ) { stack.pop_back(); arm.pop_back(); }
		}
		else
		{
			bool bDead = false;
			for ( size_t i = 0; i < stack.size(); i++ )
			{
				const int live = arm[ i ] == 0 ? stack[ i ].first : stack[ i ].second;
				if ( !live ) { bDead = true; break; }
			}
			if ( !bDead )
				out += line;
		}
		out += '\n';

		if ( eol == code.size() )
			break;
		at = eol + 1;
	}
	return out;
}

int LiveEntityTokens( const std::string &strRel )
{
	return CountEntityTokens( LiveCodeOnly( StripCommentsAndStrings( ReadRaw( strRel ) ) ) );
}

// The slice of a markdown document between two headings, so a fact about §6's
// tables reads the table and not the whole plan.
std::string PlanSlice( const std::string &plan, const char *pszFrom, const char *pszTo )
{
	const size_t a = plan.find( pszFrom );
	if ( a == std::string::npos )
		return std::string();
	const size_t b = plan.find( pszTo, a );
	return plan.substr( a, ( b == std::string::npos ? plan.size() : b ) - a );
}

// EffectsClient.cpp LEFT in plan_client_novalve C2 (lane c2-fx): IEffects is a
// thunk onto C#'s decisions now, and the file that carries the vtable, the
// export and the FX_* calls is ours. The facts below that rested on "it is
// entity-free and forwards into the leaf renderers" are true of the thunk
// too, so they read it rather than retire.
const char *kEffects = "sourcesharp/game/client/shell_effects_client.cpp";
const char *kToolFw  = "game/client/toolframework_client.cpp";
const char *kPlan    = "../docs/plans/plan_entity.md";  // was ../plan_entity.md before source-sdk-sharp

}  // namespace

//-----------------------------------------------------------------------------
// 0. THE STRIP MUST ACTUALLY STRIP, or every fact below is a grep over prose.
//-----------------------------------------------------------------------------

TEST( Section6Rulings, TheStripRemovesCommentsAndKeepsCode )
{
	const std::string s = StripCommentsAndStrings(
		"int a; // C_BaseEntity in a line comment\n"
		"/* C_BaseEntity in a block */ int b;\n"
		"const char *p = \"C_BaseEntity in a string\";\n" );

	EXPECT_EQ( 0, CountOf( s, "C_BaseEntity" ) );
	EXPECT_NE( std::string::npos, s.find( "int a;" ) );
	EXPECT_NE( std::string::npos, s.find( "int b;" ) );
}

TEST( Section6Rulings, BothRuledFilesReallyCarryTheirHeaders )
{
	// The positive control for every absence below: a fact that cannot open a
	// file reads as "the token is not there", which is how an absence check
	// passes over a path that has moved.
	EXPECT_NE( std::string::npos, ReadRaw( kEffects ).find( "the client's IEffects, a thunk" ) );
	EXPECT_NE( std::string::npos, ReadRaw( kToolFw ).find( "SOURCESHARP: THIS FILE STAYS NATIVE" ) );
}

//-----------------------------------------------------------------------------
// 1. §6 NAMES BOTH FILES.
//-----------------------------------------------------------------------------

TEST( Section6Rulings, SectionSixNamesEffectsClient )
{
	const std::string plan = ReadRaw( kPlan );
	ASSERT_FALSE( plan.empty() );
	EXPECT_NE( std::string::npos, plan.find( "## 6. What remains C++ when this is done" ) );
	EXPECT_NE( std::string::npos, plan.find( "`EffectsClient.cpp`" ) );
}

TEST( Section6Rulings, SectionSixNamesToolframeworkClient )
{
	const std::string plan = ReadRaw( kPlan );
	ASSERT_FALSE( plan.empty() );
	EXPECT_NE( std::string::npos, plan.find( "`toolframework_client.cpp`" ) );
}

TEST( Section6Rulings, SectionSixRecordsTheServerEffectsDivergence )
{
	// The lane brief says a divergence between the two sides is fine when it is
	// STATED. The client IEffects calls FX_* directly and the server one sends
	// temp entities; if that sentence goes, the asymmetry stops being stated
	// and becomes accidental.
	EXPECT_NE( std::string::npos, ReadRaw( kPlan ).find( "EffectsServer.cpp" ) );
}

//-----------------------------------------------------------------------------
// 2. EffectsClient.cpp DECIDES NOTHING AND TOUCHES NO ENTITY.
//-----------------------------------------------------------------------------

TEST( Section6Rulings, EffectsClientNamesNoEntityType )
{
	const std::string code = StripCommentsAndStrings( ReadRaw( kEffects ) );
	ASSERT_FALSE( code.empty() );

	EXPECT_EQ( 0, CountOf( code, "C_BaseEntity" ) );
	EXPECT_EQ( 0, CountOf( code, "CBaseEntity" ) );
	EXPECT_EQ( 0, CountOf( code, "GetBaseEntity" ) );
}

TEST( Section6Rulings, EffectsClientOnlyEntityTokenIsTheInvalidSentinel )
{
	// THREE, and named rather than bounded: a sentinel PASSED is not an entity
	// READ, which is the whole distinction the ruling turns on. A fourth would
	// be a new call site to look at, not a failure -- but it has to be looked at.
	//
	// EVERY `EHANDLE` IS PART OF AN `INVALID_EHANDLE`, which is the fact and
	// not the count on its own: a file holding one real EHANDLE and three
	// sentinels would pass a count of three. The first version of this fact
	// searched for "EHANDLE " with a trailing space, which "INVALID_EHANDLE )"
	// matches, and it read 3 where it expected 0.
	//
	// NOW ZERO (lane c5-particles): the three sentinels were the muzzle-flash
	// routines' owner, and those routines are C# (FxEffects), so the file names
	// no entity handle at all. Asserted as zero, with the positive control in
	// the next fact showing the file still reads.
	const std::string code = StripCommentsAndStrings( ReadRaw( kEffects ) );
	EXPECT_EQ( 0, CountOf( code, "INVALID_EHANDLE" ) );
	EXPECT_EQ( CountOf( code, "INVALID_EHANDLE" ), CountOf( code, "EHANDLE" ) );
}

TEST( Section6Rulings, EffectsClientStillForwardsIntoTheLeafRenderers )
{
	// The other half of the ruling, and the half an "it references nothing"
	// check would pass for an EMPTY file. It stays because it forwards INTO
	// §6's leaf-renderer row; a version of it that forwarded nowhere would be
	// a stub, which is the deletion this wave is scored into making.
	//
	// NO routine left (lane c5-particles): the sparks', fx.cpp's and FX_Dust
	// (DustPuff) are C#. What stays is the IEffects thunk the engine and
	// stock callers reach, forwarding into managed.
	const std::string code = StripCommentsAndStrings( ReadRaw( kEffects ) );
	EXPECT_EQ( 0, CountOf( code, "FX_Dust(" ) );
	EXPECT_EQ( 1, CountOf( code, "EXPOSE_SINGLE_INTERFACE_GLOBALVAR" ) );
	EXPECT_LE( 1, CountOf( code, "g_pEffects" ) );
}

//-----------------------------------------------------------------------------
// 3. toolframework_client.cpp's ENTITY EDGES ARE GUARDED, WHOLE.
//-----------------------------------------------------------------------------

TEST( Section6Rulings, ToolframeworkHasTwoGuardRegionsAndNeitherIsNested )
{
	const std::string code = StripCommentsAndStrings( ReadRaw( kToolFw ) );
	const std::vector< std::pair< size_t, size_t > > regions = GuardRegions( code );

	// ZERO, and it was ONE, and before that TWO. c7-leaf's handover read as
	// one, `loose-ends` measured two -- CRecordEffectOwner's body and
	// RecordMaterialParams' -- and `defect-convar-notify` REMOVED the first by
	// retyping the constructor to IClientEntity, so the ask it guarded became
	// answerable rather than compiled out.
	//
	// THE LAST ONE WENT IN THE `residue` WAVE (74b071771..a46e8d645). At
	// 74b071771 it was game/client/toolframework_client.cpp:351-451, the
	// `#ifndef SS_CBASEENTITY_ALIAS` tail of ToolFramework_RecordMaterialParams.
	// This project builds with SS_CBASEENTITY_ALIAS and SOURCESHARP both
	// DEFINED, so that arm was never compiled; the wave deleted it with 5,282
	// other dead-arm lines across 44 stock TUs and the object set did not move.
	//
	// SO THE COUNT IS ZERO -- AND ZERO ALONE IS THE DANGEROUS ASSERTION HERE,
	// because it is equally green two ways: the dead ARM was deleted (what
	// happened), or somebody deleted just the `#ifndef` and its `#endif` and
	// left the body COMPILED. The second is the regression this ruling exists
	// to prevent, so the successor property is both at once: no regions, AND no
	// entity ask anywhere in the file's code. Only the first of those moved.
	//
	// The positive anchor is the function itself: it still exists and still
	// takes its recording-mode early-out, so this is not an absence measured
	// over a file that failed to open or was emptied.
	EXPECT_EQ( 0u, regions.size() )
		<< "toolframework_client.cpp has a SS_CBASEENTITY_ALIAS region again. "
		<< "That is a decision, not a drift: something in this file needs arming "
		<< "off, and NoUnguardedEntityTokenSurvivesInToolframeworkClient's zero "
		<< "will no longer mean what it says";

	EXPECT_EQ( 0, CountOf( code, "IsToolRecording" ) )
		<< "the entity-side recording gate is back in toolframework_client.cpp's "
		<< "CODE. If the guard count above is still zero, it is back UNGUARDED -- "
		<< "which is the way the region's deletion could have gone wrong";

	EXPECT_EQ( 1, CountOf( code, "void ToolFramework_RecordMaterialParams( IMaterial *pMaterial )" ) )
		<< "ToolFramework_RecordMaterialParams is gone from the file, so every "
		<< "absence above is being measured over something that is not there";
	EXPECT_EQ( 2, CountOf( code, "clienttools->IsInRecordingMode()" ) )
		<< "one of the two recording-MODE reads is gone -- CRecordEffectOwner's "
		<< "m_bToolsEnabled and ToolFramework_RecordMaterialParams' early-out. "
		<< "Neither is about an entity, so neither was the deleted region's to "
		<< "take, and losing one means the deletion reached further than the "
		<< "`#ifndef` did";

	for ( size_t i = 0; i < regions.size(); i++ )
	{
		// The pairing is "the next #endif", so a nested #if would close the
		// region early and every fact below would pass over unguarded code.
		const std::string body = code.substr(
			regions[ i ].first, regions[ i ].second - regions[ i ].first );
		const size_t nested = body.find( "#if", strlen( "#ifndef SS_CBASEENTITY_ALIAS" ) );
		EXPECT_EQ( std::string::npos, nested )
			<< "region " << i << " has a nested #if; the naive pairing here is no "
			<< "longer safe and this scan has to learn to nest";
	}
}

TEST( Section6Rulings, EveryToolRecordingAskIsInsideAGuardRegion )
{
	// THE ASK SITES, which is what c7-leaf measured and what the ruling is
	// about: armed, the tool framework asks nothing of any entity.
	//
	// REWRITTEN BY THE `residue` WAVE (74b071771..a46e8d645), AND THE
	// SUCCESSOR IS THE STRONGER STATEMENT.
	//
	// Every one of the three asks this fact used to walk lived inside
	// game/client/toolframework_client.cpp:351-451 at 74b071771 -- the
	// `#ifndef SS_CBASEENTITY_ALIAS` tail of ToolFramework_RecordMaterialParams:
	// the `pEnt->IsToolRecording()` gate and the two `pEnt->GetToolHandle()`
	// asks at its foot. SS_CBASEENTITY_ALIAS is DEFINED in the one
	// configuration this project builds, so none of them was ever compiled, and
	// the wave deleted that arm outright.
	//
	// "EVERY ASK IS INSIDE A REGION" NOW PASSES VACUOUSLY -- there are no asks
	// and no regions, so the loop body never runs and the old EXPECT_LE(3) was
	// the only thing keeping it honest. The successor property is "THERE ARE
	// NONE", which is different and stronger, so it is written as such: the
	// walk still runs, and it must find zero entity asks either inside or
	// outside a region.
	//
	// AND THE SCANNER IS PROVED LIVE, which is what a zero needs. The needle
	// `GetToolHandle` still MATCHES in this file -- once, on
	// `clienttools->GetToolHandleForEntityByIndex`, the ask that replaced
	// CRecordEffectOwner's own region -- and that match is skipped BY NAME
	// rather than by the needle failing. So nSkipped is the positive control:
	// if the file could not be read, or the strip ate the code, it reads 0 and
	// this fact goes red instead of passing on an empty string.
	const std::string code = StripCommentsAndStrings( ReadRaw( kToolFw ) );
	const std::vector< std::pair< size_t, size_t > > regions = GuardRegions( code );

	// GetToolHandle is a PREFIX of GetToolHandleForEntityByIndex, and the two
	// are opposite things: the first is asked OF AN ENTITY and is why the
	// region existed, the second is asked of `clienttools` about an index and is
	// what replaced the region CRecordEffectOwner used to need. Counting the
	// second here would make this fact demand a guard around the very call that
	// removed the need for one. Skipped by name, not by loosening the fact.
	const char *aszAsks[] = { "IsToolRecording", "GetToolHandle" };
	const std::string notAnEntityAsk = "GetToolHandleForEntityByIndex";
	int nAsks = 0, nSkipped = 0;
	for ( size_t i = 0; i < sizeof( aszAsks ) / sizeof( aszAsks[ 0 ] ); i++ )
	{
		const std::string needle( aszAsks[ i ] );
		for ( size_t at = code.find( needle ); at != std::string::npos;
		      at = code.find( needle, at + needle.size() ) )
		{
			if ( code.compare( at, notAnEntityAsk.size(), notAnEntityAsk ) == 0 )
			{
				nSkipped++;
				continue;
			}

			nAsks++;
			EXPECT_TRUE( Inside( regions, at ) )
				<< needle << " at offset " << at << " is outside every "
				<< "SS_CBASEENTITY_ALIAS region";
		}
	}

	// THE POSITIVE CONTROL, and it comes first because the zero below is worth
	// exactly what it is worth.
	EXPECT_EQ( 1, nSkipped )
		<< "the ask walk matched " << nSkipped << " GetToolHandleForEntityByIndex "
		<< "rather than 1. Either toolframework_client.cpp did not read, or the "
		<< "re-routed ask is gone -- and in both cases the zero below is a check "
		<< "that cannot fail";

	EXPECT_EQ( 0, nAsks )
		<< nAsks << " tool-recording ask(s) of an ENTITY survive in "
		<< "toolframework_client.cpp. The three that used to be here were dead "
		<< "`#ifndef SS_CBASEENTITY_ALIAS` text and the `residue` wave deleted "
		<< "them; a new one is a new site, and with no guard regions left it is "
		<< "a COMPILED one";
}

TEST( Section6Rulings, NoUnguardedEntityTokenSurvivesInToolframeworkClient )
{
	// MEASURED, AND IT IS NOW ZERO -- it was ONE, and the differential is the
	// point of this fact rather than the number.
	//
	// `loose-ends` measured exactly one: `CRecordEffectOwner`'s constructor took
	// a `C_BaseEntity *` in its SIGNATURE, declared in toolframework_client.h
	// and defined in the .cpp, outside every guard, with the armed arm of its
	// body never reading the parameter. `defect-convar-notify` retyped it to
	// `IClientEntity *` and asks `clienttools` for the handle by entindex, so
	// the ruling's "stays native, de-entitied in place" is now true in code and
	// not only in prose.
	//
	// Pinned at zero so a NEW unguarded entity token is a decision somebody
	// makes rather than a drift -- the same job the count of one did before.
	const std::string code = StripCommentsAndStrings( ReadRaw( kToolFw ) );
	const std::vector< std::pair< size_t, size_t > > regions = GuardRegions( code );

	int nOutside = 0;
	const std::string needle = "C_BaseEntity";
	for ( size_t at = code.find( needle ); at != std::string::npos;
	      at = code.find( needle, at + needle.size() ) )
	{
		if ( !Inside( regions, at ) )
			nOutside++;
	}

	EXPECT_EQ( 0, nOutside );
}

TEST( Section6Rulings, RecordEffectOwnerAsksTheToolFrameworkNotTheEntity )
{
	// The retype's SUBSTANCE, so that the zero above cannot be satisfied by
	// deleting the constructor's ask instead of re-routing it -- "a compile gate
	// selects for deletion", and a bare count of C_BaseEntity is exactly such a
	// gate.
	//
	// Three things together: the parameter is the interface, the body asks
	// clienttools by ENTINDEX (answerable for a managed shell, which
	// GetToolHandle() over C_BaseEntity's own m_ToolHandle was not), and the ask
	// is NOT wrapped in a guard.
	const std::string code = StripCommentsAndStrings( ReadRaw( kToolFw ) );
	const std::vector< std::pair< size_t, size_t > > regions = GuardRegions( code );

	const size_t ctor = code.find( "CRecordEffectOwner::CRecordEffectOwner" );
	ASSERT_NE( std::string::npos, ctor );

	EXPECT_NE( std::string::npos,
	           code.find( "IClientEntity *pEntity", ctor ) )
		<< "CRecordEffectOwner's parameter is no longer the IClientEntity the "
		<< "ask needs";

	const size_t ask = code.find( "GetToolHandleForEntityByIndex", ctor );
	ASSERT_NE( std::string::npos, ask )
		<< "the constructor no longer asks the tool framework for the handle; "
		<< "if the ask was deleted rather than re-routed, the entity-token "
		<< "count above went to zero for the wrong reason";

	EXPECT_NE( std::string::npos, code.find( "entindex", ctor ) )
		<< "the ask is not keyed on the entindex, which is the part both "
		<< "hierarchies can answer";

	EXPECT_FALSE( Inside( regions, ask ) )
		<< "the re-routed ask is inside a SS_CBASEENTITY_ALIAS region, so it is "
		<< "still compiled out and nothing was actually gained";
}

TEST( Section6Rulings, TheLastGuardRegionStillReachesPastTheFinalAsk )
{
	// c7-leaf's finding, and the reason the second region was not the gate
	// alone: the two GetToolHandle() asks are at the FOOT of the function,
	// after the gate, so a guard that stopped at the gate would leave them
	// compiled. The region's #endif therefore had to come after the last one.
	//
	// REWRITTEN BY THE `residue` WAVE (74b071771..a46e8d645). THE REGION IS
	// GONE, AND c7-LEAF'S FINDING IS NOW ABOUT THE DELETION INSTEAD.
	//
	// The region was game/client/toolframework_client.cpp:351-451 at
	// 74b071771, inside `#ifndef SS_CBASEENTITY_ALIAS`, and this project
	// defines SS_CBASEENTITY_ALIAS, so it was never compiled. The wave deleted
	// the arm. There is no `#endif` left to measure and no ask left to measure
	// it against -- which is exactly the shape rule 4 warns about, because
	// "the last region reaches past the last ask" would now pass vacuously if
	// its two ASSERTs were simply relaxed.
	//
	// THE FINDING SURVIVES THE CHANGE OF SUBJECT WORD FOR WORD: the thing that
	// removes the tail has to reach PAST the final ask, or the asks stay
	// compiled. A deletion that took only the gate -- the exact mistake
	// c7-leaf caught the guard about to make -- would leave the body behind
	// and, with no region left, leave it LIVE. So the measurement is the same
	// question asked of the deletion: the whole tail is gone, from the gate,
	// through the parameter walk in the middle, to the two asks and the message
	// post at the foot.
	//
	// The head of the function is asserted PRESENT in the same slice. That is
	// the anchor that stops this from being an absence measured over a file
	// that did not open, and it is also the fact that the deletion stopped
	// where the `#ifndef` did: the two checks above the gate are about the
	// MATERIAL and the recording MODE, not about an entity, and they answer the
	// same way either way, so they were never the region's to take.
	const std::string code = StripCommentsAndStrings( ReadRaw( kToolFw ) );
	const std::vector< std::pair< size_t, size_t > > regions = GuardRegions( code );

	const size_t fn = code.find( "void ToolFramework_RecordMaterialParams( IMaterial *pMaterial )" );
	ASSERT_NE( std::string::npos, fn )
		<< "ToolFramework_RecordMaterialParams is gone from " << kToolFw
		<< ", so there is nothing here to measure";
	const std::string tail = code.substr( fn );

	// ---- the head stayed ----------------------------------------------------
	EXPECT_EQ( 1, CountOf( tail, "Assert( pMaterial )" ) )
		<< "the material null-check went with the guarded tail; the deletion "
		<< "reached FURTHER than the `#ifndef` did";
	EXPECT_EQ( 1, CountOf( tail, "clienttools->IsInRecordingMode()" ) )
		<< "the recording-mode early-out went with the guarded tail; it is about "
		<< "the MODE, not about an entity";

	// ---- and the tail went whole, past the final ask ------------------------
	EXPECT_EQ( 0, CountOf( tail, "IsToolRecording" ) )
		<< "the entity gate is back at the head of the tail";
	EXPECT_EQ( 0, CountOf( tail, "ShaderParamCount" ) )
		<< "the shader-parameter walk -- the MIDDLE of the removed region -- is "
		<< "back, so something restored part of the tail without its guard";
	EXPECT_EQ( 0, CountOf( tail, "GetToolHandle" ) )
		<< "c7-leaf's two asks at the FOOT of the function are back. This is the "
		<< "exact failure the region's `#endif` position was pinned against: "
		<< "whatever removes this tail has to reach past the last ask, and with "
		<< "no guard region left these would be COMPILED";
	EXPECT_EQ( 0, CountOf( tail, "ToolFramework_PostToolMessage" ) )
		<< "the tail's message post is back, after the asks it needs";

	// The two above are a pair: an ask with no post, or a post with no ask, is
	// half a restoration and either half is a site.
	EXPECT_TRUE( regions.empty() )
		<< "a SS_CBASEENTITY_ALIAS region is back in " << kToolFw << "; if the "
		<< "tail came back with it, re-point this fact at the region rather than "
		<< "at the deletion -- the two ASSERTs it used to make are in git at "
		<< "74b071771";
}

TEST( Section6Rulings, ToolframeworkClientStillServesTheSectionSixCallers )
{
	// It stays because the view, clientshadowmgr.cpp and the leaf renderers
	// call it -- and the view's SetUpViews calls SetupEngineView
	// UNCONDITIONALLY, which is what makes deleting this file impossible rather
	// than merely unwise. (view.cpp's SetUpViews until C7 step 3,
	// shell_viewrender_client.cpp's since; the executor adjusts the viewport.)
	const std::string code = StripCommentsAndStrings( ReadRaw( kToolFw ) );
	EXPECT_LE( 1, CountOf( code, "ToolFramework_SetupEngineView" ) );
	EXPECT_LE( 1, CountOf( code, "ToolFramework_AdjustEngineViewport" ) );
	EXPECT_LE( 1, CountOf( code, "ToolFramework_PostToolMessage" ) );

	const std::string view = StripCommentsAndStrings( ReadRaw( "sourcesharp/game/client/shell_viewrender_client.cpp" ) );
	EXPECT_LE( 1, CountOf( view, "ToolFramework_SetupEngineView" ) );
	const std::string executor = StripCommentsAndStrings( ReadRaw( "sourcesharp/game/client/shell_view_executor_client.cpp" ) );
	EXPECT_LE( 1, CountOf( executor, "ToolFramework_AdjustEngineViewport" ) );
}

//-----------------------------------------------------------------------------
// 4. THE 65 UNRULED CLIENT-CLOSURE ROWS, RULED BY `c8-rulings`: 48 STAY, 17 LEAVE.
//
// Four facts nobody could write when the ruling landed, because that lane was
// forbidden `src/`. Each one re-runs a measurement rather than restating a
// sentence -- the ruling's own argument for why prose is not enough.
//-----------------------------------------------------------------------------

TEST( Section6Rulings, TheEntityTokenCounterReadsZeroOnBothCalibrationFiles )
{
	// THE FIXTURE, AND IT MUST COME FIRST. Every count below is only worth what
	// this one is: §6 already ruled on these two files and the ruling rests on
	// them being entity-free IN LIVE CODE. `c8-rulings` ran its counter over
	// these two BEFORE the other 65 for exactly this reason, and that is what
	// found the stale paragraph about toolframework's guard regions.
	//
	// toolframework_client.cpp WAS the sharper of the two: it carried ONE entity
	// token in its text and ZERO live, so a counter that ignored the guards
	// would read 1 there and pass every fact below while counting dead text.
	//
	// THAT CALIBRATION IS GONE, AND IT WAS TAKEN LEGITIMATELY. The one token
	// was `C_BaseEntity *pEnt = SDKEntityFromUnknown( ... )` at
	// game/client/toolframework_client.cpp:355 as of 74b071771, inside the
	// `#ifndef SS_CBASEENTITY_ALIAS` region at :351-451. SS_CBASEENTITY_ALIAS
	// is DEFINED in the one configuration this project builds, so that line was
	// never compiled, and the `residue` wave (74b071771..a46e8d645) deleted the
	// arm with 5,282 other dead-arm lines across 44 stock TUs. Both calibration
	// files now read 0 in the TEXT as well as 0 live -- which is the right
	// answer and a USELESS fixture, because a counter that did nothing at all
	// would agree with it.
	//
	// SO THE FIXTURE IS RE-POINTED AT AN ANCHOR NO SOURCE CHANGE CAN EVICT: an
	// inline calibration string, right here, carrying one token in each of the
	// four preprocessor positions this project has. It is written as already-
	// stripped code (no comments, no string literals) so it goes to the two
	// counters exactly as a stripped file would.
	//
	// BOTH DIRECTIONS, because one of them alone is a lie:
	//
	//   * the DEAD tokens must not be counted live -- that is the guard
	//     awareness the whole file rests on, and it is what the toolframework
	//     file used to demonstrate;
	//   * the LIVE tokens must still be counted -- without this half, a
	//     LiveCodeOnly that returned an empty string would read 0 everywhere
	//     and make every `LiveEntityTokens( ... ) == 0` below, and in the other
	//     facts, pass for nothing.
	const std::string kCalibration =
		"C_BaseEntity *pTopLevel;\n"
		"#ifndef SS_CBASEENTITY_ALIAS\n"
		"C_BasePlayer *pAliasDead;\n"
		"#endif\n"
		"#ifndef SOURCESHARP\n"
		"C_BaseAnimating *pSourceSharpDead;\n"
		"#endif\n"
		"#ifdef SOURCESHARP\n"
		"C_BaseFlex *pArmed;\n"
		"#else\n"
		"C_BaseCombatWeapon *pStockDead;\n"
		"#endif\n";

	EXPECT_EQ( 5, CountEntityTokens( kCalibration ) )
		<< "the text counter does not see all five tokens in the calibration "
		   "string, so it is not the grep the live counter is supposed to differ "
		   "from";
	EXPECT_EQ( 2, CountEntityTokens( LiveCodeOnly( kCalibration ) ) )
		<< "the live counter reads the calibration string as something other "
		   "than its two ARMED tokens (the top-level one and the `#ifdef "
		   "SOURCESHARP` arm). 0 means LiveCodeOnly is blanking live code too; "
		   "5 means it is not reading the guards at all and every zero below is "
		   "a grep's zero";

	// AND THE TWO RULED FILES, still measured -- both counters, both files, and
	// now all four zeroes. They no longer calibrate anything, but they are the
	// §6 ruling's own claim and it still has to be true.
	EXPECT_EQ( 0, LiveEntityTokens( kEffects ) );
	EXPECT_EQ( 0, LiveEntityTokens( kToolFw ) );

	EXPECT_EQ( 0, CountEntityTokens( StripCommentsAndStrings( ReadRaw( kEffects ) ) ) );
	EXPECT_EQ( 0, CountEntityTokens( StripCommentsAndStrings( ReadRaw( kToolFw ) ) ) )
		<< "toolframework_client.cpp carries an entity token in its TEXT again. "
		   "The one it used to carry was dead `#ifndef SS_CBASEENTITY_ALIAS` "
		   "text; with that region gone there is nothing to make a new one dead, "
		   "so LiveEntityTokens above should have caught it as live -- if it did "
		   "not, the guard-awareness calibration is what to look at first";

	// The positive control for the four zeroes: a file that failed to open reads
	// zero tokens too.
	EXPECT_EQ( 1, CountOf( StripCommentsAndStrings( ReadRaw( kEffects ) ), "EXPOSE_SINGLE_INTERFACE_GLOBALVAR" ) )
		<< kEffects << " read as nothing; its zeroes above mean nothing";
	EXPECT_EQ( 1, CountOf( StripCommentsAndStrings( ReadRaw( kToolFw ) ),
	                       "void ToolFramework_RecordMaterialParams( IMaterial *pMaterial )" ) )
		<< kToolFw << " read as nothing; its zeroes above mean nothing";
}

TEST( Section6Rulings, RagdollSplitIsSixBeforeTheClassThatLeaves )
{
	// §6's split ruling on game/client/ragdoll.cpp (the file stayed, class
	// C_ServerRagdoll left it) is closed: the whole file left client.so in plan
	// physics_bones_native_zero Phase 3 and is no longer in the farm. Its
	// CRagdoll is SourceSharp.Client's RagdollSolver / ClientRagdolls.
	std::ifstream in( "game/client/ragdoll.cpp" );
	EXPECT_FALSE( in.good() )
		<< "game/client/ragdoll.cpp is in the farm again; §6's split ruling and "
		   "its token counts apply to it once more";
	std::ifstream anchor( "game/client/physics.cpp" );
	EXPECT_TRUE( anchor.good() )
		<< "game/client/physics.cpp did not open: the farm is not where this "
		   "test runs, and the absence above measured nothing";
}

TEST( Section6Rulings, EverySeventeenThatLeavesNamesWhatDoesTheThingAfterwards )
{
	// §6 states this as a rule it holds itself to: "every LEAVE names what does
	// the thing afterwards". A LEAVE with an empty successor column is a
	// capability being deleted with nothing put in its place, which is the
	// expensive direction of getting a ruling wrong.
	//
	// 17 TUs across 12 rows -- several rows carry two or three TUs joined by
	// `·`, so the row count and the TU count are different numbers and the
	// heading's 17 is the second one.
	const std::string plan = ReadRaw( kPlan );
	ASSERT_FALSE( plan.empty() );

	const std::string leave = PlanSlice( plan,
		"#### The 17 that LEAVE, and what does the thing afterwards",
		"#### The four hardest" );
	ASSERT_FALSE( leave.empty() ) << "§6 no longer carries the 17-LEAVE table";

	int nRows = 0, nTUs = 0, nEmptySuccessor = 0;
	size_t at = 0;
	while ( at < leave.size() )
	{
		size_t eol = leave.find( '\n', at );
		if ( eol == std::string::npos )
			eol = leave.size();
		const std::string line = leave.substr( at, eol - at );
		at = eol + 1;

		if ( line.empty() || line[ 0 ] != '|' )
			continue;
		if ( line.find( "|---" ) == 0 )          // the separator
			continue;
		if ( line.find( "| TU |" ) == 0 )        // the header
			continue;

		// | <tu> | <why> | <what does it afterwards> |
		const size_t c1 = line.find( '|', 1 );
		const size_t c2 = ( c1 == std::string::npos ) ? c1 : line.find( '|', c1 + 1 );
		const size_t c3 = ( c2 == std::string::npos ) ? c2 : line.find( '|', c2 + 1 );
		if ( c1 == std::string::npos || c2 == std::string::npos || c3 == std::string::npos )
			continue;

		nRows++;
		nTUs += 1 + CountOf( line.substr( 1, c1 - 1 ), "\xc2\xb7" );   // U+00B7
		if ( c3 - c2 < 12 )
			nEmptySuccessor++;
	}

	EXPECT_EQ( 12, nRows );
	EXPECT_EQ( 17, nTUs );
	EXPECT_EQ( 0, nEmptySuccessor );
}

//-----------------------------------------------------------------------------
// 5. `physics_fx.cpp` -- RULED A §6 KEEP, THEN DELETED (2026-10-11).
//
// The keep rested on two legs: the file was entity-free in code, and the one
// symbol it owed (CBaseEntity::GetWaterType) came from physicsowner.h's
// guarded inline rather than its own text. The file itself went when its only
// function, PhysicsSplash, was found to have no caller: SourceSharp.Server's
// PhysicsSplashPlan is the splash (PhysicsFluidEventsTests). The first leg
// retires with the file; physics_fx.cpp's absence is asserted below, against a
// sibling that must read.
//
// The second leg is about physicsowner.h, which outlives the file, and stays.
//-----------------------------------------------------------------------------

TEST( Section6Rulings, PhysicsFxLeftTheServer )
{
	ASSERT_FALSE( ReadRaw( "sourcesharp/game/server/physics.cpp" ).empty() )
		<< "physics.cpp did not read, so the absence below is not evidence";
	// ReadRaw asserts the file opens, so the absence is probed directly.
	const char *pszRoot = SrcRoot();
	ASSERT_TRUE( pszRoot != NULL );
	std::ifstream fx( ( std::string( pszRoot ) + "/sourcesharp/game/server/physics_fx.cpp" ).c_str() );
	EXPECT_FALSE( fx.good() )
		<< "physics_fx.cpp is back in the farm. Its PhysicsSplash had no caller; "
		   "the managed PhysicsSplashPlan is the splash";
}

TEST( Section6Rulings, PhysicsOwnersWaterTypeWrapperReachesNoEntity )
{
	// THE SECOND LEG, AND THE GUARD IT TURNED ON IS GONE -- WHICH IS THE LEG
	// BEING MET RATHER THAN LOST.
	//
	// This required `#ifndef SS_CBASEENTITY_ALIAS` around the wrapper's entity
	// arm, and `pBase->GetWaterType()` inside it, "so that the ARMED build owes
	// nothing". Both needles were in the arm the compiler never saw: the guard
	// is `#ifndef`, and this project defines SS_CBASEENTITY_ALIAS, so the
	// forwarding call was never compiled and the armed build has never owed
	// CBaseEntity::GetWaterType(). The close deleted the dead arm and the guard
	// with it, and the wrapper now returns the neutral unconditionally.
	//
	// So the ruling's leg is asserted at the state it was always aiming at, and
	// asserted as a PAIR. "No guard" alone would pass over a deleted wrapper --
	// and a deleted wrapper is its callers asking the entity method
	// directly, which is the first leg's failure. So: the wrapper is here, it
	// answers, it answers the stated neutral, and it reaches no entity to do it.
	const std::string owner = ReadRaw( "sourcesharp/game/shared/physicsowner.h" );
	ASSERT_FALSE( owner.empty() );

	const size_t at = owner.find( "int GetWaterType() const" );
	ASSERT_NE( std::string::npos, at ) << "physicsowner.h no longer wraps GetWaterType";

	const std::string body = owner.substr( at, 400 );
	EXPECT_EQ( std::string::npos, body.find( "SS_CBASEENTITY_ALIAS" ) )
		<< "the wrapper's entity arm is guarded again. Nothing defines that macro "
		   "since public/ss_cbaseentity_alias.h was deleted, so a guard here is "
		   "either dead text or -- if it is an #ifdef -- an arm that vanished "
		   "without the wrapper losing its declaration.";
	EXPECT_EQ( std::string::npos, body.find( "pBase->GetWaterType()" ) )
		<< "the wrapper forwards to an entity again. That is the call that makes "
		   "physics_fx.o owe CBaseEntity::GetWaterType(), which is a gate-1 "
		   "symbol; the ruling's whole second leg is that it owes nothing.";

	EXPECT_NE( std::string::npos, body.find( "return 0;" ) )
		<< "the wrapper no longer answers. §6 records this as a stated "
		   "DIVERGENCE and not a mechanism: an entity outside the SDK tracks no "
		   "water level, so it reads as plain water, and closing the gap means "
		   "giving that hierarchy water tracking rather than inventing a number "
		   "here. A wrapper that stopped answering would be the gap reopened "
		   "silently. Body was: " << body;
}
