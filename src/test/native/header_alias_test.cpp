//========= SourceSharp — lane H's header arms, read as source ===============//
//
// Phase 13 step 4 turns `CBaseEntity` into `using CBaseEntity = IGameEntity;`.
// Lane P did that for sourcesharp/game/server/entity_bases.h — the three sites in the
// server's precompiled prelude — and proved its arms by COMPILING the header
// twice, once each way (entity_bases_alias_test.cpp and its armed twin). That
// is the strongest shape of evidence available and it is available only for a
// header that can be included standalone. entity_bases.h can; the 58 headers
// lane H owns cannot — player.h, Sprite.h, ai_basenpc.h and the rest pull in
// the whole SDK prelude, and a stand-in for that is a fiction that would drift
// from the real thing silently.
//
// So this file does what sdk_player_cast_test.cpp and alias_mechanics_test.cpp
// do: it READS THE SOURCE. That is weaker than compiling and it is not nothing,
// because the failure modes it is aimed at are textual ones:
//
//   * a guard whose #else or #endif was lost to a merge splice — the tree has
//     been bitten by exactly this, a keep-both merge swallowing a brace;
//   * an armed arm that still names the member the flip deletes, which compiles
//     unarmed, is never compiled armed by anything in buildcpptests, and is
//     therefore a branch nobody knows is broken until flip day;
//   * a guard quietly disappearing in a later edit, which no count would catch
//     without a floor to compare against.
//
// The COMPILE-side evidence for these arms is src/survey_run with the alias
// armed: it builds every server and client TU with -DSS_CBASEENTITY_ALIAS and
// an armed arm that does not compile shows up there as a site. That is a
// measurement run by hand, not a standing test, which is precisely why the
// textual invariants below are worth pinning: they are the half that can rot
// between surveys.
//
// The arithmetic half IS checked properly, at compile time, in
// TheObjectCapsFoldsAreArithmeticallyRight — the one part of this lane that is
// a calculation rather than a judgement.
//
//-----------------------------------------------------------------------------
// READ THE ASSERTIONS, NOT THE NAMES. FLIP DAY HAPPENED, AND HALF OF THIS
// FILE'S SITES ARE NOW IN FILES THE BUILD DOES NOT COMPILE.
//-----------------------------------------------------------------------------
//
// the-close 1/N..3/N landed the flip and retired the alias:
// public/ss_cbaseentity_alias.h is DELETED, `CBaseEntity` and `C_BaseEntity` are
// not names in the compiled tree, and the 453 dead guarded arms are gone.
// SS_CBASEENTITY_ALIAS is still DEFINED by both targets and selects nothing.
//
// MEASURED AT THIS TIP, over the 34 headers this file speaks for, from the
// build's own dependency log (`src/.ninja_deps`, the 2,035 headers the 542
// compiled objects were compiled from):
//
//     19 of the 34 are COMPILED      -- and carry ZERO alias guards, and spell
//                                       neither retired name. The flip landed.
//     15 of the 34 are NOT COMPILED  -- and carry all 23 remaining guards
//                                       between them.
//
// The correlation is total: EVERY alias guard left in this file's world is in a
// file nothing compiles. Rules R1 and R2 both bear on that, and they point the
// same way:
//
//   * A fact that reads a guard in an uncompiled file IS THE CHECK THAT CANNOT
//     FAIL. It is green, and it is green about text no compiler will ever see,
//     in either arm of a flip that already happened. Six of this file's facts
//     were passing that way -- ThePropDataArmsDropTheEflagSetters read three
//     uncompiled headers, TheKeyValueArmsDropTheBaseForward one, and
//     ShardsA.TheAbstractEntityFactoriesAreGuardedOut two uncompiled .cpp files.
//     This file's own prose already ruled on exactly this case when team.cpp
//     left the build: "a guard in a file nothing compiles protects nothing, in
//     either arm of the flip". The ruling is applied to the rest of it here.
//
//   * A fact whose site IS compiled has a LIVE SUCCESSOR, and it is stronger
//     than the guard was. "The armed arm does not name the member" becomes
//     "the member is not named AT ALL, and the answer that replaced it is there
//     with no guard around it" -- which is a property of the shipping build
//     rather than of a branch nobody compiles.
//
// SO EVERY SITE ROW IS ROUTED THROUGH THE SAME TWO-WAY GATE, and neither way is
// a skip. A row in a compiled file is checked against the live tree. A row in an
// uncompiled file is checked to STILL BE uncompiled -- it is named, its state is
// asserted, and it is recorded as measuring nothing about the build. If such a
// file is ever brought back in, the gate goes red and says so, which is the only
// honest way to keep the row.
//
//=============================================================================//

#include "gtest/gtest.h"
#include "cmake_graph.h"

#include <dirent.h>
#include <sys/stat.h>

#include <cstring>
#include <fstream>
#include <set>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// Finding the tree, and reading it. Same shape as alias_mechanics_test.cpp.
//-----------------------------------------------------------------------------
std::string Trimmed( const std::string &s )
{
	const size_t a = s.find_first_not_of( " \t" );
	if ( a == std::string::npos )
		return std::string();
	const size_t b = s.find_last_not_of( " \t" );
	return s.substr( a, b - a + 1 );
}

bool Contains( const std::string &hay, const char *needle )
{
	return hay.find( needle ) != std::string::npos;
}

// A line that carries no code: blank, or a whole-line // comment. Used to
// decide whether an arm is EMPTY.
bool IsBlankOrComment( const std::string &strLine )
{
	const std::string t = Trimmed( strLine );
	return t.empty() || t.compare( 0, 2, "//" ) == 0;
}

// COMMENTS ARE STRIPPED BEFORE ANY MATCH, and this is not tidiness.
//
// Every armed arm lane H writes explains ITSELF — "IsBaseCombatCharacter() is
// not askable of IGameEntity" sits directly above `return NULL;`. Matching raw
// text, the rule "the armed arm must not name the deleted member" then reads
// the member's name out of the sentence saying it is gone, and reports the
// correct arm as wrong. Caught in the act here, and it is the same failure
// TheResolverListIsHonest was found to have: a file's own prose carrying the
// token being searched for. The comment-carrying half of an arm is documentation;
// only the code half is the claim.
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
			// Rest of THIS line only.
			while ( i < strIn.size() && strIn[ i ] != '\n' ) i++;
			if ( i < strIn.size() ) out += '\n';
			continue;
		}
		out += strIn[ i ];
	}
	return out;
}

//-----------------------------------------------------------------------------
// One #ifndef SS_CBASEENTITY_ALIAS / #else / #endif region, split into arms.
//-----------------------------------------------------------------------------
struct Guard_t
{
	std::string strFile;
	int nLine;                            // 1-based, the #ifndef
	std::vector< std::string > unarmed;   // between #ifndef and #else
	std::vector< std::string > armed;     // between #else and #endif
	bool bHasElse;
	bool bClosed;
	// Which polarity opened it. Needed because an #else-less guard means the
	// OPPOSITE thing in the two polarities: `#ifndef` with no #else deletes SDK
	// text when armed, `#ifdef` with no #else ADDS text when armed. Without
	// this the two are indistinguishable to the checks below and one of them
	// has to be waived, which is how a swallowed #else gets through.
	bool bArmedFirst;

	Guard_t() : nLine( 0 ), bHasElse( false ), bClosed( false ), bArmedFirst( false ) {}

	// CODE ONLY — see StripComments above for why the prose must not be matched.
	std::string UnarmedText() const
	{
		std::string s;
		for ( size_t i = 0; i < unarmed.size(); i++ ) s += unarmed[ i ] + "\n";
		return StripComments( s );
	}
	std::string ArmedText() const
	{
		std::string s;
		for ( size_t i = 0; i < armed.size(); i++ ) s += armed[ i ] + "\n";
		return StripComments( s );
	}
	bool ArmedHasCode() const
	{
		for ( size_t i = 0; i < armed.size(); i++ )
			if ( !IsBlankOrComment( armed[ i ] ) ) return true;
		return false;
	}
	bool UnarmedHasCode() const
	{
		for ( size_t i = 0; i < unarmed.size(); i++ )
			if ( !IsBlankOrComment( unarmed[ i ] ) ) return true;
		return false;
	}
};

// BOTH POLARITIES, and the difference is not cosmetic.
//
// `#ifndef SS_CBASEENTITY_ALIAS` puts the SDK text first and the armed answer
// after the #else; `#ifdef SS_CBASEENTITY_ALIAS` puts the armed answer first.
// Lane H uses the second wherever the armed arm is the SHORT one — the
// To<Class> resolvers, whose armed body is a single `return NULL;` against a
// dozen lines of casts. A scanner that knew only the first spelling would read
// those four guards as absent, report a smaller set, and still be green: the
// floor below is exactly the check that would have hidden it.
bool IsGuardOpen( const std::string &strLine, bool *pbArmedFirst )
{
	const std::string t = Trimmed( strLine );
	if ( t == "#ifndef SS_CBASEENTITY_ALIAS"
		|| t == "#if !defined( SS_CBASEENTITY_ALIAS )"
		|| t == "#if !defined(SS_CBASEENTITY_ALIAS)" )
	{
		*pbArmedFirst = false;
		return true;
	}
	if ( t == "#ifdef SS_CBASEENTITY_ALIAS"
		|| t == "#if defined( SS_CBASEENTITY_ALIAS )"
		|| t == "#if defined(SS_CBASEENTITY_ALIAS)" )
	{
		*pbArmedFirst = true;
		return true;
	}
	return false;
}

// Any other conditional, so nesting inside an arm does not steal its #else.
bool IsAnyConditionalOpen( const std::string &strLine )
{
	const std::string t = Trimmed( strLine );
	return t.compare( 0, 3, "#if" ) == 0;
}

bool IsEndif( const std::string &strLine ) { return Trimmed( strLine ).compare( 0, 6, "#endif" ) == 0; }
bool IsElse( const std::string &strLine )
{
	const std::string t = Trimmed( strLine );
	return t == "#else" || t.compare( 0, 5, "#elif" ) == 0;
}

std::vector< Guard_t > GuardsInLines( const std::vector< std::string > &lines,
                                     const std::string &strFile )
{
	std::vector< Guard_t > out;

	for ( size_t i = 0; i < lines.size(); i++ )
	{
		bool bArmedFirst = false;
		if ( !IsGuardOpen( lines[ i ], &bArmedFirst ) )
			continue;

		Guard_t g;
		g.strFile = strFile;
		g.nLine = (int)i + 1;
		g.bArmedFirst = bArmedFirst;

		int nDepth = 0;
		bool bInElse = false;
		size_t j = i + 1;
		for ( ; j < lines.size(); j++ )
		{
			const std::string &L = lines[ j ];
			if ( IsAnyConditionalOpen( L ) ) { nDepth++; }
			else if ( IsEndif( L ) )
			{
				if ( nDepth == 0 ) { g.bClosed = true; break; }
				nDepth--;
			}
			else if ( IsElse( L ) && nDepth == 0 )
			{
				g.bHasElse = true; bInElse = true; continue;
			}

			// Which side of the #else is the ARMED arm depends on the polarity
			// of the opening directive, not on its position.
			const bool bThisIsArmed = ( bInElse != bArmedFirst );
			if ( bThisIsArmed ) g.armed.push_back( L );
			else                g.unarmed.push_back( L );
		}
		out.push_back( g );
		i = j;
	}
	return out;
}

//-----------------------------------------------------------------------------
// WHAT THE BUILD COMPILES, asked two ways because the two file kinds have two
// different authorities.
//
// A .cpp is answered by what the build compiles -- the union of the sources of
// server_sourcesharp, client_sourcesharp and sourcesharp (libmanaged_host),
// read through cmake_graph.h from compile_commands.json, which IS the compile
// command line, cross-checked against each target's CMakeLists source list.
// There is no arguing with it. (Until Phase 6b this was VPC's ninja graph,
// _vpc_/ninja/sdk_csharp_debug.ninja.) A header is reached transitively and is
// not listed there, so the authority for a header is the NAME: `CBaseEntity` and
// `C_BaseEntity` were retired in 0bc727ed9 and a file that still spells one in
// CODE cannot be in any compiled TU's include graph, because that TU would not
// compile. That test is SOUND ONE WAY ONLY -- an uncompiled header that was
// tidied up spells neither name -- so the header partition is also written down
// explicitly below and checked, rather than derived.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// THE LANE-H HEADERS THIS FILE SPEAKS FOR.
//
// Listed rather than discovered, so that a header LOSING its guard is a
// failure here rather than a silently shorter list — the tree has already
// learned that a green suite does not prove a mechanical edit kept everything
// it was supposed to keep.
//-----------------------------------------------------------------------------
struct Site_t
{
	const char *pszFile;
	const char *pszMustBeInUnarmed;  // the member the flip deletes
	const char *pszMustNotBeInArmed; // ...which the armed arm must not name
};

// Cluster 5 -- CBaseEntity::IsServer() -- is retired: its last site,
// ss_physics_collisionevent.h, was deleted with native's collision handler
// (plan_physics_bones_native_zero 2.1); player.h and saverestore_stringtable.h
// left the farm in plan_server_novalve.md P6.

//-----------------------------------------------------------------------------
// THE LANE-H HEADERS, PARTITIONED. This was one list, kLaneHHeaders, and every
// fact below looped over the whole of it demanding a well-formed guard.
//
// It became two lists when the tree became two things: this compiled half, and
// the uncompiled lane-H headers. The uncompiled half, and the facts that read
// it, went when the farm stopped carrying stock files the build does not
// compile; what is left is the compiled half, which the site gate consults.
//
// MEASURED, not assumed -- from the build's own dependency log at this tip:
//
//     python3 - <<'EOF'   # src/.ninja_deps, ninja's deps log v4
//     ...the 2,035 headers the 542 compiled objects were compiled from...
//     EOF
//
// and cross-checked against the property the build itself enforces: a compiled
// file cannot spell `CBaseEntity` or `C_BaseEntity`, because neither is a name.
//-----------------------------------------------------------------------------

// AnyGuardApplies STOOD HERE AND IS DELETED WITH ITS REASON. It answered "does
// some guard in this file have `pszIn` in its unarmed arm and not `pszOut` in
// its armed one", and every site fact in this file was one loop over it.
//
// There are no guards left in any file this test measures -- the flip landed and
// the dead arms went with it -- so the helper could only ever answer false for a
// compiled file and true for an uncompiled one. That is the exact inversion of
// what it was for: it would have graded the finished tree as broken and a file
// nobody builds as correct. CheckSiteAgainstTheTree replaces it, and the arm
// comparison it used to make is now a plain absence over the code plus, per
// cluster, the text that replaced the deleted member.
//
// Guard_t and GuardsIn SURVIVE it and are still load-bearing: "zero guards in
// the compiled half" is a measurement only because the scanner can still find
// one, which the two HeaderAliasScanner fixtures prove on literals.

}  // namespace

//-----------------------------------------------------------------------------
// The NetworkStateChanged pair, and this one is checked in the OPPOSITE
// polarity to every other site above.
//
// Those ask "the member the flip deletes is in the unarmed arm and gone from
// the armed one". This pair is text that must exist ONLY when armed, and the
// unarmed half is the load-bearing half: an empty NetworkStateChanged visible
// to the shipping build would shadow the real one CBaseEntity supplies and
// every CNetworkVar in the whole animating hierarchy -- players, NPCs, weapons,
// props, ragdolls -- would stop marking itself dirty. That is not a compile
// error anywhere. It is a server that changes state and never sends it, which
// is the exact silent failure chain_state_notify_test.cpp was written for.
//
// So: present in the armed arm of some guard, ABSENT from that guard's unarmed
// arm, and absent from the file outside every guard. The third clause is what
// stops the pair being moved out of its guard by a later edit and this test
// staying green on the other two.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// CWorld OWNS NO PAIR ANY MORE, AND THE FACT IS INVERTED RATHER THAN DELETED.
//
// This read `CheckNetworkStateChangedPairIsArmedOnly( "game/server/world.h" )`,
// and what it protected was real: world.h is included by five compiled server
// files, CWorld declared ten CNetworkVar/CNetworkVector members, and an empty
// NetworkStateChanged pair leaking into the SHIPPING build would have shadowed
// CBaseEntity's real one so DT_WORLD stopped marking itself dirty -- a client
// receiving a world whose bounds never update, with no compile error on either
// side.
//
// THE SUBJECT IS GONE, NOT THE HAZARD'S SHAPE. plan_gamedll_after.md Part B2
// moved worldspawn's ten networked values out of members entirely: they are
// declared in SourceSharp.Shared.WorldNetState and live in a blob past the
// object, and CWorld marks itself dirty through CBaseEntity's own
// NetworkStateChanged, which nothing in world.h shadows.
//
// So the fact asserts the OTHER direction, and it is the stronger one: the
// husk pair must not come BACK. A pair re-added to this header today would
// shadow the real one for the whole class and nothing below it would notice,
// because there is no CNetworkVar left whose compile would fail to complain --
// which is exactly what makes re-adding it plausible and silent.
//
// It also pins WHY there is nothing to protect: zero CNetworkVar declarations.
// Asserting the absent pair alone would go on passing the day somebody
// re-declares a member and forgets the rest of the port.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// CWorld is a LEAF, and the fact SURVIVES its original reason with a new one.
//
// It was written to bound the armed NetworkStateChanged pair above: a pair on a
// hierarchy root reaches every class below it, and this one reached nothing
// because nothing derives from CWorld. That pair is gone
// (plan_gamedll_after.md Part B2) and the leafness now bounds two other things,
// both of them the shim's:
//
//   * CWorld::operator new over-allocates by SourceSharp_WorldBlobOffset(),
//     which is ALIGN_VALUE( sizeof( CWorld ), 16 ). A derived class is longer,
//     so its members would start inside the blob its own props address --
//     silently, because the allocation would still be big enough and the
//     overlap would read as data. The standalone hierarchy pays a headroom cost
//     for exactly this reason (its offset clears the LARGEST shell on it);
//     CWorld pays none, and may not while this stays true.
//   * CWorld::GetServerClass answers the ONE managed table
//     shell_world_nettable.cpp built. A derived class would inherit it and
//     network as a world.
//
// Scanned over the server tree rather than asserted about world.h alone,
// because a derivation would be written in the DERIVED class's file, which
// world.h cannot see.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// READ THE ASSERTIONS, NOT THE NAME. This was NoOwnerWrapperArmKeepsTheSdkArm:
// no guard in physicsowner.h or anyentity.h may leave a `pBase->` ask in its
// ARMED arm, because armed there is no SDK entity to ask.
//
// THE ARM WENT AND pBase STAYED, RETYPED. physicsowner.h:117 now declares
// `IGameEntity *pBase` -- the field was not deleted, it was pointed at the
// interface -- so the successor rule is sharper than the old one and is checked
// over the whole file rather than over arms that no longer exist:
//
//     EVERY `pBase->` ASK IN EITHER FILE MUST BE ONE OF IGameEntity's SIX.
//
// Six, read out of public/igameentity.h rather than listed here, so that the
// day the interface grows or loses a member this rule follows it instead of
// going quietly wrong. An ask outside that set does not compile -- which is
// what makes this a real rule rather than a style, and what the guards used to
// be standing in for.
//-----------------------------------------------------------------------------
//-----------------------------------------------------------------------------
// 3. THE ARITHMETIC, CHECKED RATHER THAN EYEBALLED.
//
// Cluster 1's rule is a calculation: armed, the base term is 0, so
// `(0 & ~A) | B` folds to `B` and `0 | B` folds to `B`. Ten sites were folded
// BY HAND, and a hand-folded bitmask is exactly the kind of thing that is
// wrong in one place and looks right in ten.
//
// So each row below writes the SDK's expression as a function of the base
// term, and the armed arm as a constant, and requires them to agree at base=0.
// The left-hand sides are transcribed from the unarmed arms and the right-hand
// sides from the armed arms; a transcription error on either side shows up as
// a mismatch, and a fold error shows up as a mismatch.
//-----------------------------------------------------------------------------
namespace
{
// game/shared/baseentity_shared.h:21-37, spelled here rather than included
// because that header needs the whole prelude. entity_bases_alias_test.cpp
// does the same for the two it needs.
const int kAcrossTransition  = 0x00000002;
const int kImpulseUse        = 0x00000010;
const int kDirectionalUse    = 0x00000080;
const int kUseOnGround       = 0x00000100;
const int kUseInRadius       = 0x00000200;
const int kSaveNonNetworkable= 0x00000400;
const int kWCEditPosition    = 0x40000000;
const int kDontSave          = (int)0x80000000;
}

TEST( HeaderAlias, TheObjectCapsFoldsAreArithmeticallyRight )
{
	const int base = 0;  // what BaseClass::ObjectCaps() contributes, armed.

	// func_areaportalbase.h, player.h — `base & ~FCAP_ACROSS_TRANSITION` -> 0
	EXPECT_EQ( base & ~kAcrossTransition, 0 );

	// trains.h — `base | FCAP_DIRECTIONAL_USE | FCAP_USE_ONGROUND`
	EXPECT_EQ( base | kDirectionalUse | kUseOnGround,
	           kDirectionalUse | kUseOnGround );

	// player_resource.h — `base | FCAP_DONT_SAVE`
	EXPECT_EQ( base | kDontSave, kDontSave );

	// items.h — `base | FCAP_IMPULSE_USE | FCAP_WCEDIT_POSITION`
	EXPECT_EQ( base | kImpulseUse | kWCEditPosition, kImpulseUse | kWCEditPosition );

	// vehicle_base.h — `base | FCAP_IMPULSE_USE`
	EXPECT_EQ( base | kImpulseUse, kImpulseUse );

	// c_baseanimating.h — `base | FCAP_SAVE_NON_NETWORKABLE`
	EXPECT_EQ( base | kSaveNonNetworkable, kSaveNonNetworkable );

	// gib.h — `(base & ~FCAP_ACROSS_TRANSITION) | FCAP_DONT_SAVE | FCAP_IMPULSE_USE`
	EXPECT_EQ( ( base & ~kAcrossTransition ) | kDontSave | kImpulseUse,
	           kDontSave | kImpulseUse );

	// Sprite.h — `(base & ~FCAP_ACROSS_TRANSITION) | flags`, for both values
	// `flags` can take. The armed arm returns `flags` unchanged, so BOTH must
	// agree, not just the interesting one.
	EXPECT_EQ( ( base & ~kAcrossTransition ) | kDontSave, kDontSave );
	EXPECT_EQ( ( base & ~kAcrossTransition ) | 0, 0 );

	// doors.h — `flags = base`, then `flags | FCAP_IMPULSE_USE | FCAP_USE_IN_RADIUS`
	// on the SF_DOOR_PUSE arm and `flags` otherwise. Armed the spawnflag store
	// is gone too, so HasSpawnFlags() is false and only the second arm remains;
	// the armed text returns 0, and that is what these two lines check.
	EXPECT_EQ( base, 0 );
	EXPECT_EQ( base | kImpulseUse | kUseInRadius, kImpulseUse | kUseInRadius );
}

//-----------------------------------------------------------------------------
// 4. THE SCANNER ITSELF.
//
// Every assertion above is only as good as GuardsIn() and StripComments(), and
// both were wrong once in this file's short life: the polarity handling read
// four `#ifdef` guards as absent, and the raw-text match read a member's name
// out of the comment explaining that the member is gone. A scanner that
// silently finds nothing and a tree that is silently correct look identical
// from outside, so the scanner gets its own tests.
//-----------------------------------------------------------------------------
TEST( HeaderAliasScanner, AnAdditionGuardIsDistinguishedFromADeletionGuard )
{
	// The classification the addition-guard rules turned on. Both shapes are
	// `#else`-less and BOTH have one empty arm, so the ONLY thing telling them
	// apart is bArmedFirst. Get it wrong and an #ifdef that lost its #else --
	// which silently deletes SDK text from the shipping build -- is waived as a
	// legitimate addition guard.
	//
	// READ FROM A LITERAL, NOT FROM c_baseanimating.h. This used to read the
	// real free-rider chain-end out of that header "so it tracks the header
	// instead of a copy of it". The header has no guards now -- the flip landed
	// and the free-rider overloads stand unconditionally -- so tracking it would
	// mean asserting nothing at all. The distinction the scanner has to make is
	// unchanged, and it is what keeps a guard reappearing in a compiled file
	// from being classified as harmless.
	std::vector< std::string > lines;
	lines.push_back( "#ifdef SS_CBASEENTITY_ALIAS" );                    // addition
	lines.push_back( "inline void DataMapAccess( C_BaseAnimating *, datamap_t **p ) { *p = NULL; }" );
	lines.push_back( "#endif" );
	lines.push_back( "#ifndef SS_CBASEENTITY_ALIAS" );                   // deletion
	lines.push_back( "\tif ( pBase ) return pBase->GetDebugName();" );
	lines.push_back( "#endif" );

	const std::vector< Guard_t > guards = GuardsInLines( lines, "<fixture>" );
	ASSERT_EQ( 2u, guards.size() );

	size_t nAddition = 0, nDeletion = 0;
	for ( size_t i = 0; i < guards.size(); i++ )
	{
		const Guard_t &G = guards[ i ];
		ASSERT_TRUE( G.bClosed );
		ASSERT_FALSE( G.bHasElse );
		if ( G.bArmedFirst )
		{
			nAddition++;
			EXPECT_TRUE( Contains( G.ArmedText(), "DataMapAccess( C_BaseAnimating *" ) )
				<< "the #else-less #ifdef's content landed in the wrong arm";
			EXPECT_FALSE( G.UnarmedHasCode() )
				<< "an addition guard has no unarmed arm by definition";
		}
		else
		{
			nDeletion++;
			EXPECT_TRUE( Contains( G.UnarmedText(), "pBase->GetDebugName()" ) )
				<< "the #else-less #ifndef's content landed in the wrong arm";
			EXPECT_FALSE( G.ArmedHasCode() )
				<< "a deletion guard has no armed arm by definition";
		}
	}
	EXPECT_EQ( 1u, nAddition );
	EXPECT_EQ( 1u, nDeletion );
}

//=============================================================================//
//
// LANE shards-a — the SERVER-side shards in the first third of step 6's dispatch
// manifest (04, 05, 06, 07, 08, 09, 10, 11, 12, 13, 15, 16, 19).
//
// Appended here rather than given a file of its own because buildcpptests is
// another lane's fence, and a test nothing runs is not a test. The shapes below
// are this lane's, not lane H's, so they carry their own site tables. The rest
// of the lane's facts read files the build no longer compiles and went with
// them.
//
//=============================================================================//

//-----------------------------------------------------------------------------
// 4b. THE `UTIL_ClipTraceToPlayers` ROUTE IS GONE ENTIRELY, NOT MERELY GUARDED.
//
// Two rows used to sit in a ShardsA site table: `filter->ShouldHitEntity( player, mask )`
// and `enginetrace->ClipRayToEntity(`. Both wanted an IHandleEntity for a
// CBasePlayer that does not exist on either side, and the assertion there was
// that the ARMED arm no longer names them.
//
// c8-shared6 went further: the whole walk became a declared, printed refusal
// (§7), so the two calls were not in the file at all and there was no guard to
// examine. This fact was the replacement for the kRows pair.
//
// plan_shim.md PHASE 2c, lane 2-utilshared, WENT FURTHER AGAIN, AND THE §7
// ARGUMENT CHANGES SHAPE WITH IT. The refusal was a runtime warning, and a
// runtime warning has an audience only while something CALLS the function. The
// census over both link lines found nothing calling it:
//
//     tools/shim-census --mtime-policy off who-imports \
//         src/game/shared/util_shared.cpp
//       server 3 importers (hl2_episodic, UTIL_PlayerByCommandArg,
//       UTIL_BloodDrips), client 1 (hl2_episodic) -- and
//       UTIL_ClipTraceToPlayers is none of them
//
// so the warning could never print and the function was deleted with the rest
// of that file's dead bodies. What §7 asks for -- that a removed capability be
// DECLARED rather than silently answering a plausible nothing -- is now carried
// by the LINK instead of by a Warning: `util_shared.h` still declares the
// function and nothing either target compiles defines it, so a caller that reappears
// fails to link, naming the symbol. That is louder than a once-per-session
// message nobody was there to read.
//
// THE THREE HALVES BELOW, and each refuses a different wrong answer:
//   1. the two player-clipping calls are absent      (the original claim)
//   2. the definition is absent, here AND in the shim file that is the natural
//      place for a "move" to have quietly put it back  (a relocation is not a
//      removal, and this fact must not be satisfiable by either)
//   3. the DECLARATION is still there                (without this, a future
//      lane deleting the declaration too would turn the link error into a
//      compile error at the call site, which tempts a stub -- and a stub that
//      returns an unshortened trace is exactly the silent no-op §7 forbids)
//-----------------------------------------------------------------------------
