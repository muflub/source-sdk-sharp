//========= SourceSharp — one spelling of "is this solid", and why it is safe ==//
//
// PHASE 1B: THE CONSUMER THIS FILE WAS NAMED FOR IS GONE, AND THE CHAIN IS NOT.
// CTriggerTraceEnum and the whole EntityShot_* family were deleted with
// shell_ai_orphans.cpp -- no object on the server link line imported any of the
// three -- and the fact that read them is retired below, in place, with the
// measurement. What stays is the four-link chain the substitution was priced
// on, because several LIVE shim files rest on it: trace_filter_library.cpp:172,
// shell_entityapi.cpp:2308 and :2413, and shell_gameents.cpp:409 each take a
// GetCollideable() and read solidity or position off it. Read the paragraphs
// below as the reasoning that chain was written down for, not as a description
// of a caller that is still there.
//
// sourcesharp/game/server/shell_ai_orphans.cpp's CTriggerTraceEnum used to ask
// solidity TWO ways: `pEnt->IsSolid()` for a CBaseEntity and
// `::IsSolid( pCollide->GetSolid(), pCollide->GetSolidFlags() )` for everything
// else. It now asks the second one for BOTH, which is what took two of that
// file's three Phase 13 step-6 armed sites out.
//
// THAT SUBSTITUTION IS ONLY CORRECT BECAUSE OF A FOUR-LINK CHAIN, and every
// link is in a file this lane does not own:
//
//   1. CBaseEntity::IsSolid()            -> CollisionProp()->IsSolid()
//   2. CBaseEntity::CollisionProp()      -> &m_Collision
//   3. CBaseEntity::GetCollideable()     -> &m_Collision      (the SAME object,
//                                           which is what makes the ICollideable
//                                           the enumerator holds be the entity's
//                                           own collision property and not some
//                                           other view of it)
//   4. CCollisionProperty::IsSolid()     -> ::IsSolid( m_nSolidType, m_usSolidFlags )
//      CCollisionProperty::GetSolid()    -> m_nSolidType
//      CCollisionProperty::GetSolidFlags -> m_usSolidFlags
//
// Break any one of them and the two spellings stop being the same answer —
// SILENTLY, because both still compile, both still return a bool, and a shot
// that stops on a trigger it should have passed through looks like a gameplay
// bug and not like a port defect. Nothing in the build checks a chain of
// definitions, so this does.
//
// A RED HERE MEANS RE-CHECK THE SUBSTITUTION, not "regression": if link 3 stops
// returning the collision property, the enumerator has to go back to asking the
// entity, and it cannot, because at step 6 there is no entity to ask.
//
// WHAT THIS CANNOT DO, said plainly rather than left to be discovered. It is a
// SOURCE-CHAIN proof, not a runtime one: it reads definitions and checks that
// they still say what the substitution was priced on. It cannot see a change to
// ::IsSolid itself (both spellings call it, so both move together — that is the
// point), and it cannot see an entity whose GetCollideable is overridden to
// return something else. The second is what the anchors are for: link 3 is
// asserted against the file that declares it non-overridden-here, so a subclass
// that starts overriding it is a change to a file this test reads.
//
// THE STRIPPER IS LOAD-BEARING: the prose above spells the very strings the
// assertions look for, so an unstripped scan would read the explanation as the
// fact. Same rule as effects_evicted_test.cpp and shim_underive_verdict_test.cpp.
//
// ANCHORS: every file is identified before anything is asserted about it, so a
// renamed or moved file is a red test and not a vacuous pass.
//
//=============================================================================//

#include <string>
#include <vector>
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
		const std::string pub = std::string( kCandidates[ i ] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode )
			&& stat( pub.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
		{
			return kCandidates[ i ];
		}
	}
	return NULL;
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

	std::ifstream file( ( std::string( pszRoot ) + "/" + strRelative ).c_str() );
	if ( !file.is_open() )
		return std::string();

	std::string all, line;
	while ( std::getline( file, line ) )
	{
		if ( !line.empty() && line[ line.size() - 1 ] == '\r' )
			line.erase( line.size() - 1 );
		all += line; all += '\n';
	}
	return StripComments( all );
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

//-----------------------------------------------------------------------------
// R2's INSTRUMENT: is the line this test reads one the compiler actually SEES?
//
// A fact whose reference sits inside an arm the armed build never compiles is
// green and measuring nothing. That happened to this very test once — see the
// RESIDUE note on TheCollideableSpellingReadsTheSameTwoMembers — so the anchors
// and the presence halves below are checked against LIVE text rather than
// against the file.
//
// LiveText() returns the source with every arm the ARMED SERVER build discards
// removed. Known-defined: SS_CBASEENTITY_ALIAS, SOURCESHARP,
// SOURCESHARP_SHELL_GAMEDLL, GAME_DLL (game/server/server_csharp.vpc:69).
// Known-undefined: CLIENT_DLL. `#if 0` is dead and `#if 1` is live.
//
// EVERY OTHER CONDITION KEEPS BOTH ARMS — include guards, HL2_DLL, _DEBUG. So
// the instrument is CONSERVATIVE IN ONE DIRECTION: it can report a dead line as
// live, never a live line as dead. That is the safe direction for "my reference
// is live" and the WRONG one for "this text is dead", so no fact below uses it
// to prove deadness; absence halves read the plain file.
//-----------------------------------------------------------------------------
struct Arm_t { bool bKnown; bool bDead; };

bool IsKnownDefined( const std::string &strMacro, bool *pbKnown )
{
	static const char *kDefined[] = { "SS_CBASEENTITY_ALIAS", "SOURCESHARP",
	                                  "SOURCESHARP_SHELL_GAMEDLL", "GAME_DLL" };
	static const char *kUndefined[] = { "CLIENT_DLL" };

	for ( size_t i = 0; i < sizeof( kDefined ) / sizeof( kDefined[ 0 ] ); i++ )
	{
		if ( strMacro == kDefined[ i ] ) { *pbKnown = true; return true; }
	}
	for ( size_t i = 0; i < sizeof( kUndefined ) / sizeof( kUndefined[ 0 ] ); i++ )
	{
		if ( strMacro == kUndefined[ i ] ) { *pbKnown = true; return false; }
	}
	*pbKnown = false;
	return false;
}

std::string LiveText( const std::string &strIn )
{
	std::vector< Arm_t > stack;
	int nDead = 0;
	std::string out;

	size_t i = 0;
	while ( i < strIn.size() )
	{
		size_t e = strIn.find( '\n', i );
		if ( e == std::string::npos )
			e = strIn.size();

		const std::string line = strIn.substr( i, e - i );
		i = e + 1;

		const size_t w = line.find_first_not_of( " \t" );
		const std::string t = ( w == std::string::npos ) ? std::string() : line.substr( w );

		if ( !t.empty() && t[ 0 ] == '#' )
		{
			const size_t sp = t.find_first_of( " \t" );
			const std::string word = t.substr( 0, sp );
			const std::string rest = NoWhitespace( sp == std::string::npos
			                                       ? std::string() : t.substr( sp ) );

			if ( word == "#if" || word == "#ifdef" || word == "#ifndef" )
			{
				Arm_t arm; arm.bKnown = false; arm.bDead = false;

				if ( word == "#if" )
				{
					if ( rest == "0" ) { arm.bKnown = true; arm.bDead = true; }
					else if ( rest == "1" ) { arm.bKnown = true; arm.bDead = false; }
				}
				else
				{
					bool bKnown = false;
					const bool bDefined = IsKnownDefined( rest, &bKnown );
					if ( bKnown )
					{
						arm.bKnown = true;
						arm.bDead = ( word == "#ifdef" ) ? !bDefined : bDefined;
					}
				}

				stack.push_back( arm );
				if ( arm.bDead )
					nDead++;
			}
			else if ( word == "#else" )
			{
				if ( !stack.empty() && stack.back().bKnown )
				{
					if ( stack.back().bDead )
						nDead--;
					stack.back().bDead = !stack.back().bDead;
					if ( stack.back().bDead )
						nDead++;
				}
			}
			else if ( word == "#elif" )
			{
				// An unknown condition from here on: keep the rest live rather
				// than guess. Conservative in the same single direction.
				if ( !stack.empty() )
				{
					if ( stack.back().bDead )
						nDead--;
					stack.back().bDead = false;
					stack.back().bKnown = false;
				}
			}
			else if ( word == "#endif" )
			{
				if ( !stack.empty() )
				{
					if ( stack.back().bDead )
						nDead--;
					stack.pop_back();
				}
			}
		}

		if ( nDead == 0 )
		{
			out += line;
			out += '\n';
		}
	}

	return out;
}

// The file as the compiler sees it, whitespace-squeezed like everything else
// here. Used for every PRESENCE half.
std::string ReadLive( const std::string &strRelative )
{
	return NoWhitespace( LiveText( ReadStripped( strRelative ) ) );
}

struct File_t { const char *pszRel; const char *pszAnchor; };

// THE SERVER ENTITY HEADER IS GONE. game/server/baseentity.h is compiled by
// nothing since plan_server_novalve.md P5 and left the farm in P6, so it has
// no anchor any more; its absence is asserted instead, against a sibling in the
// same directory that IS in the farm, so a tree from which game/server cannot be
// read at all does not pass it.
const char *const kServerEntityGone = "game/server/baseentity.h";
const File_t kServerSibling  = { "game/shared/shareddefs.h",
	"#defineSHAREDDEFS_H" };
const File_t kGameEntity     = { "public/igameentity.h",
	"abstract_classIGameEntity" };
const File_t kCollisionH     = { "game/shared/collisionproperty.h",
	"classCCollisionProperty:publicICollideable" };
// LIVE anchor, and it has now moved TWICE. It was
// `SolidType_tCCollisionProperty::GetSolid()const` until the residue wave --
// dead source, see the RESIDUE note on the fact below. The replacement was
// spelled `CBaseEntity*pEntity`, and the close renamed that parameter's TYPE to
// `IGameEntity` in code (0bc727ed9) while deliberately leaving prose alone. So
// the anchor is the same declaration under the name it now has.
// And a THIRD time: the server gc cull deleted AddEntity (no caller in either
// DLL), so the anchor is the same class's Shutdown, outside every guard.
const File_t kCollisionCpp   = { "sourcesharp/game/shared/collisionproperty.cpp",
	"voidCDirtySpatialPartitionEntityList::Shutdown()" };
const File_t kStandalone     = { "sourcesharp/game/server/shell_standalone.h",
	"classCStandaloneEntity:publicIServerEntity,publicIServerNetworkable," };
const File_t kAnyEntity      = { "sourcesharp/game/server/anyentity.h",
	"structAnyEntity_t" };

// THE FILE AS THE COMPILER SEES IT, identified before anything is asserted
// about it. The anchor is looked for in LIVE text, so an anchor that survives
// only inside a dead arm is a RED anchor and not a silent pass -- which is the
// exact failure mode finding 3 of status/residue-tests.md filed against this
// test.
std::string ReadAnchored( const File_t &file )
{
	const std::string body = ReadLive( file.pszRel );

	EXPECT_FALSE( body.empty() )
		<< file.pszRel << " could not be read; every fact this test rests on "
		   "would otherwise pass vacuously.";

	if ( !body.empty() )
	{
		EXPECT_NE( body.find( file.pszAnchor ), std::string::npos )
			<< file.pszRel << " no longer contains its anchor \""
			<< file.pszAnchor << "\" — RE-CHECK THE SUBSTITUTION.";
	}
	return body;
}

// The WHOLE file, dead arms and all, squeezed the same way. Every ABSENCE half
// reads this and never ReadAnchored: "gone" must mean gone from the file, not
// merely gone from the arm that compiles.
std::string ReadWhole( const File_t &file )
{
	return NoWhitespace( ReadStripped( file.pszRel ) );
}

size_t CountOccurrences( const std::string &strHay, const std::string &strNeedle )
{
	size_t n = 0;
	for ( size_t at = strHay.find( strNeedle ); at != std::string::npos;
	      at = strHay.find( strNeedle, at + 1 ) )
	{
		n++;
	}
	return n;
}

} // namespace

//=============================================================================//
// THE INSTRUMENT, CHECKED FIRST, because three facts below rest on its answer.
//
// A liveness scanner that returned its whole input would make every presence
// half pass without measuring anything -- which is the SAME defect the facts
// below exist to close, one level up. The nested case is the one that matters:
// the obvious rfind("#endif") form of this check lets an INNER #endif reopen an
// outer dead region, and would report `nested_dead()` as live.
//=============================================================================//
TEST( ShotPathSolidity, TheLivenessScannerTellsADeadArmFromALiveOne )
{
	const std::string kSample =
		"live_one();\n"
		"#ifndef SS_CBASEENTITY_ALIAS\n"
		"dead_one();\n"
		"#else\n"
		"armed_one();\n"
		"#endif\n"
		"after_one();\n"
		"#ifdef SOURCESHARP\n"
		"also_armed();\n"
		"#else\n"
		"also_dead();\n"
		"#endif\n"
		"#ifndef COLLISIONPROPERTY_H\n"
		"unknown_kept();\n"
		"#endif\n"
		"#ifdef CLIENT_DLL\n"
		"client_only();\n"
		"#endif\n"
		"#if 0\n"
		"switched_off();\n"
		"#endif\n"
		"#ifndef SS_CBASEENTITY_ALIAS\n"
		"#ifdef SOURCESHARP\n"
		"nested_dead();\n"
		"#endif\n"
		"#endif\n"
		"tail();\n";

	const std::string live = NoWhitespace( LiveText( kSample ) );

	EXPECT_NE( live.find( "live_one();" ), std::string::npos );
	EXPECT_NE( live.find( "armed_one();" ), std::string::npos );
	EXPECT_NE( live.find( "after_one();" ), std::string::npos )
		<< "the scan ran past an #endif, which would report every later line in "
		   "a file as dead";
	EXPECT_NE( live.find( "also_armed();" ), std::string::npos );
	EXPECT_NE( live.find( "unknown_kept();" ), std::string::npos )
		<< "an UNKNOWN condition must keep both arms. Reporting an include "
		   "guard's body as dead would make this instrument reject every "
		   "reference in every header.";
	EXPECT_NE( live.find( "tail();" ), std::string::npos );

	EXPECT_EQ( live.find( "dead_one();" ), std::string::npos );
	EXPECT_EQ( live.find( "also_dead();" ), std::string::npos );
	EXPECT_EQ( live.find( "client_only();" ), std::string::npos );
	EXPECT_EQ( live.find( "switched_off();" ), std::string::npos );
	EXPECT_EQ( live.find( "nested_dead();" ), std::string::npos )
		<< "an INNER #endif closed the OUTER dead region. Every nested arm in "
		   "the tree would then read as live and this instrument would certify "
		   "exactly the references it exists to reject.";
}

//=============================================================================//
// LINKS 1-3, which were in the server entity header and ARE NOT THERE ANY MORE.
//
// READ THE ASSERTIONS, NOT THE NAME. The name is kept so this fact's identity
// survives the wave; what it asserts is now the opposite of what it says.
//
// Finding 3 of .claude/entity-port/status/residue-tests.md filed this fact as
// GREEN AND HOLLOW: it read game/server/baseentity.h:2861/2906/2930, and all
// three lines were inside the `#ifndef SS_CBASEENTITY_ALIAS` opened at :2561
// and closed at :3109. SS_CBASEENTITY_ALIAS is defined by
// game/server/server_csharp.vpc:69, so the armed build -- the only build there
// is -- never compiled one of them. Three assertions, describing text the
// compiler had never seen.
//
// The close then DELETED that arm with the other 453 (f288a5e11..HEAD), and
// `CBaseEntity` stopped being a name at all (0bc727ed9). So links 1-3 are not
// "moved", they are GONE, and R1 says a gone subject is asserted ABSENT
// TOGETHER WITH ITS LIVE SUCCESSOR -- absence alone would pass over an empty
// file, and this file is 632 lines shorter than it was.
//
// THE LIVE SUCCESSOR IS STRONGER THAN THE CHAIN IT REPLACES. Links 1-3 existed
// to prove that two DIFFERENT functions returned the SAME object, so that
// "ask the entity" and "ask the collideable" could not drift apart. In the
// armed build there are no longer two functions and no longer two objects:
// CStandaloneEntity IS its own ICollideable and returns `this`, and IGameEntity
// -- the interface every live site now holds -- HAS NO SOLIDITY MEMBER AT ALL,
// deliberately (public/igameentity.h's membership rule: a question ICollideable
// answers is answered THERE). There is one spelling because there is only one
// place to ask.
//=============================================================================//
TEST( ShotPathSolidity, TheEntitySpellingBottomsOutInItsCollisionProperty )
{
	// ABSENT, and now in the strongest form there is: the three links were
	// searched for in game/server/baseentity.h, and that header is compiled by
	// nothing since plan_server_novalve.md P5 and left the farm in P6. So the
	// file itself is asserted unreadable -- which no restore of links 1-3 can
	// get past without bringing the header back -- and the sibling read first
	// is what stops that absence passing over a tree with no game/server in it.
	const std::string sibling = ReadWhole( kServerSibling );
	ASSERT_NE( sibling.find( kServerSibling.pszAnchor ), std::string::npos )
		<< kServerSibling.pszRel << " could not be read or lost its anchor \""
		<< kServerSibling.pszAnchor << "\"; the absence below would otherwise "
		   "pass vacuously.";

	EXPECT_TRUE( ReadStripped( kServerEntityGone ).empty() )
		<< kServerEntityGone << " is readable from the farm again. Nothing "
		   "compiles it; if it is back, check whether links 1-3 -- and "
		   "`#ifndef SS_CBASEENTITY_ALIAS` -- came back with it.";

	//-------------------------------------------------------------------------
	// LIVE SUCCESSOR, HALF ONE: there is no second spelling to drift from.
	//-------------------------------------------------------------------------
	const std::string ig = ReadAnchored( kGameEntity );

	EXPECT_EQ( ig.find( "IsSolid(" ), std::string::npos )
		<< "IGameEntity has grown an IsSolid(). ICollideable already answers "
		   "that question for every entity on this boundary, and a second "
		   "answer beside it is exactly the drift links 1-3 existed to rule "
		   "out -- except that now nothing checks the two agree.";

	EXPECT_EQ( ig.find( "GetSolid(" ), std::string::npos )
		<< "IGameEntity has grown a GetSolid(). See igameentity.h's own "
		   "membership rule, which names GetSolid as absent ON PURPOSE.";

	EXPECT_EQ( ig.find( "CollisionProp(" ), std::string::npos )
		<< "IGameEntity has grown a CollisionProp(). shell_ai_orphans.cpp asks "
		   "the collideable; a second route to the same geometry is a second "
		   "answer that can differ.";

	//-------------------------------------------------------------------------
	// LIVE SUCCESSOR, HALF TWO: the entity and its collideable are ONE OBJECT,
	// which is links 2 and 3 collapsed into a fact that cannot come apart.
	//-------------------------------------------------------------------------
	const std::string ss = ReadAnchored( kStandalone );

	EXPECT_NE( ss.find( "virtualICollideable*GetCollideable()OVERRIDE{returnthis;}" ),
	           std::string::npos )
		<< "CStandaloneEntity no longer returns itself from GetCollideable(), "
		   "so the ICollideable CTriggerTraceEnum holds is no longer the entity "
		   "-- and there is no CBaseEntity left to fall back on. RE-CHECK THE "
		   "SUBSTITUTION.";
}

//=============================================================================//
// LINK 4, in the collision property itself: its IsSolid and the two getters the
// enumerator calls read the SAME two members.
//
// RESIDUE WAVE, 2026-09: this fact used to close link 4 by reading
// `SolidType_t CCollisionProperty::GetSolid() const` out of
// game/shared/collisionproperty.cpp. THAT BODY WAS NEVER COMPILED. At
// 74b071771 it sat inside the `#ifndef SS_CBASEENTITY_ALIAS` opened at
// collisionproperty.cpp:421, so the armed build -- the only build there is --
// took the other arm, and this test was reading DEAD SOURCE as its reference.
// The residue wave deleted that arm with the other 5,282 lines of dead arm, and
// the deletion lost no symbol from any object because it was in none.
//
// So the out-of-line half is asserted ABSENT rather than present: if that body
// comes back, a dead arm has been restored and this test's reference is a lie
// again. The two header halves are unchanged -- collisionproperty.h's inlines
// are outside every guard in that file and are live text.
//
// And link 4's LIVE successor is added beside them. In the armed build the
// entity IS its own collideable (shell_standalone.h returns `this` from
// GetCollideable), and GetSolid/GetSolidFlags read two members of ONE head
// block -- which is the same property the stock chain was priced on, in the
// code that actually compiles.
//=============================================================================//
TEST( ShotPathSolidity, TheCollideableSpellingReadsTheSameTwoMembers )
{
	const std::string h = ReadAnchored( kCollisionH );

	EXPECT_NE( h.find( "inlineboolCCollisionProperty::IsSolid()const"
	                   "{return::IsSolid((SolidType_t)(unsignedchar)m_nSolidType,m_usSolidFlags);}" ),
	           std::string::npos )
		<< "CCollisionProperty::IsSolid() is no longer "
		   "::IsSolid( m_nSolidType, m_usSolidFlags ). That equality is the "
		   "whole of why the collideable spelling answers what the entity "
		   "spelling answered — RE-CHECK THE SUBSTITUTION.";

	EXPECT_NE( h.find( "inlineintCCollisionProperty::GetSolidFlags(void)const{returnm_usSolidFlags;}" ),
	           std::string::npos )
		<< "CCollisionProperty::GetSolidFlags() no longer returns m_usSolidFlags. "
		   "RE-CHECK THE SUBSTITUTION.";

	// ABSENT: the stock out-of-line body, which was dead source. Its return
	// STILL comes back through the deleted arm if anyone restores it, so this
	// is the half that notices a revert rather than a regression.
	//
	// ANCHORED against LIVE text, then searched over the WHOLE file: the
	// restore this watches for would land inside a restored dead arm, where an
	// anchored read would not see it.
	ReadAnchored( kCollisionCpp );
	const std::string cpp = ReadWhole( kCollisionCpp );

	EXPECT_EQ( cpp.find( "SolidType_tCCollisionProperty::GetSolid()const{return(SolidType_t)m_nSolidType.Get();}" ),
	           std::string::npos )
		<< "CCollisionProperty::GetSolid()'s out-of-line body is back in "
		   "collisionproperty.cpp. It was inside `#ifndef SS_CBASEENTITY_ALIAS` "
		   "and was never compiled; the residue wave deleted it. Restoring it "
		   "does not make it a reference — check whether the GUARD came back "
		   "with it, which would be a much larger revert.";

	// PRESENT: link 4's live successor, in the port's own shim.
	const std::string ss = ReadAnchored( kStandalone );

	EXPECT_NE( ss.find( "virtualICollideable*GetCollideable()OVERRIDE{returnthis;}" ),
	           std::string::npos )
		<< "CStandaloneEntity no longer returns itself from GetCollideable(), so "
		   "the ICollideable CTriggerTraceEnum holds is no longer the entity "
		   "itself — links 2 and 3's live successor is broken.";

	EXPECT_NE( ss.find( "virtualSolidType_tGetSolid()constOVERRIDE{return(SolidType_t)Block().Head.SolidType;}" ),
	           std::string::npos )
		<< "CStandaloneEntity::GetSolid() no longer reads Block().Head.SolidType.";

	EXPECT_NE( ss.find( "virtualintGetSolidFlags()constOVERRIDE{returnBlock().Head.SolidFlags;}" ),
	           std::string::npos )
		<< "CStandaloneEntity::GetSolidFlags() no longer reads "
		   "Block().Head.SolidFlags. The two getters must read two members of "
		   "the SAME head block, which is what makes one ::IsSolid() call over "
		   "them answer what the entity answered.";
}

//=============================================================================//
// The enumerator itself: it asks the engine's spelling, for both kinds, once.
//
// Both directions, because the obvious one-sided form of this check cannot
// fail: "the collideable ask is present" was already true BEFORE the change
// (it was the else-arm), so presence alone would have passed against the very
// code this replaced. The ABSENCE of the two member asks is what notices.
//=============================================================================//
// TheTriggerEnumeratorHasOneSpellingOfEach STOOD HERE and is retired with the
// code it pinned (Phase 1b).
//
// Its subject was CTriggerTraceEnum in shell_ai_orphans.cpp -- the
// IEntityEnumerator that EntityShot_TraceAttackToTriggers walked so that a
// bullet crossing a trigger was noticed by it. The whole family went: nothing
// on the server link line imported EntityShot_TraceAttackToTriggers,
// EntityShot_CreateBubbleTrail or EntityShot_UpdateStatistics. Measured on a
// cold build over the objects the ninja graph's link line names, with the
// objects-on-disk count checked against it so no evicted translation unit's
// object could answer:
//
//   ./1b-census.sh sym server EntityShot_TraceAttackToTriggers  ->  0
//   ./1b-census.sh sym server EntityShot_CreateBubbleTrail      ->  0
//   ./1b-census.sh sym server EntityShot_UpdateStatistics       ->  0
//
// The six assertions that go with it were: the collideable solidity ask is
// present; the AnyEntityOf resolve is present; the AnyEntity_t dispatch is
// present; `pEnt->IsSolid()` and `pEnt->DispatchTraceAttack(` are absent (the
// two Phase 13 step-6 armed sites a revert would restore); and ApplyMultiDamage
// is still gated on the SDK arm. None of the six is a claim about this build any
// more, because the code all six read is not in it.
//
// THE FOUR FACTS BELOW AND ABOVE STAY, and they are not left over. The chain
// they prove -- CollisionProp() and GetCollideable() returning the SAME
// &m_Collision, so that an ICollideable held by a caller IS the entity's own
// collision property -- is what several LIVE shim files rest on:
// trace_filter_library.cpp:172, shell_entityapi.cpp:2308 and :2413 and
// shell_gameents.cpp:409 all take a GetCollideable() and read solidity or
// position off it. What changed is the consumer this file was named for, not
// the chain.
//
// BULLETS NO LONGER HIT TRIGGERS, and that is a behaviour difference from stock
// rather than a tidy-up. plan_shim.md §10 records it as an open question with
// the experiment that settles it: shoot a trigger_hurt with a managed weapon on
// the live gate. If the answer is that they must, what comes back is the
// enumerator AND a caller for it -- and this fact with them.

//=============================================================================//
// And the thing it now delegates to.
//
// READ THE ASSERTIONS, NOT THE NAME. The name is kept for identity and it is
// now a lie in both halves: the value type does NOT carry both arms, and the
// guard this fact used to require is gone.
//
// WHAT HAPPENED. AnyEntity_t::DispatchTraceAttack had an SDK arm
// (`pBase->DispatchTraceAttack( info, vecDir, ptr );`) wrapped in
// `#ifndef SS_CBASEENTITY_ALIAS`. That macro is defined by
// game/server/server_csharp.vpc:69, so that arm was NEVER COMPILED, and this
// fact's third assertion -- "the guard is present" -- was asserting that its
// own subject was excluded from the build. The close deleted the arm and the
// guard together (3db4aaad5).
//
// So the SDK arm is asserted ABSENT, and the LIVE SUCCESSOR is asserted beside
// it in TWO places, because "the SDK arm is gone" alone would also pass over a
// gutted header -- which is precisely the failure this fact was written to
// catch in the first place:
//
//   * the parallel-hierarchy arm is PRESENT AND LIVE (checked against the file
//     the compiler sees, not the file on disk), so the hitgroup path still
//     lands damage;
//   * pBase is now a CONSTANT NULL and SAYS SO -- `ent.pBase = NULL;` in
//     AnyEntityOf, where it used to be EntityFromEntityHandle, which the close
//     proved was a constant NULL and deleted (d3a0bfa3a). That is the fact
//     that makes "the SDK arm is gone" a correctness statement rather than a
//     deletion: there was never an entity on that arm to dispatch to.
//   * and pGame's resolution moved to GameEntityFromEntityHandle, the live
//     successor that asks the virtual GetGameEntity().
//=============================================================================//
TEST( ShotPathSolidity, TheDualValueTypeStillCarriesBothArms )
{
	const std::string any = ReadAnchored( kAnyEntity );
	const std::string anyWhole = ReadWhole( kAnyEntity );

	ASSERT_FALSE( anyWhole.empty() )
		<< kAnyEntity.pszRel << " could not be read; the absences below would "
		   "otherwise pass vacuously.";

	// PRESENT AND LIVE: the arm that actually lands the damage.
	EXPECT_NE( any.find( "StandaloneEntity_TakeDamage(pGame,info);" ), std::string::npos )
		<< "AnyEntity_t::DispatchTraceAttack has lost its parallel-hierarchy "
		   "arm. That arm is what stopped the radius damage crashing on the "
		   "barrel; CTriggerTraceEnum delegates its whole hitgroup path to it "
		   "rather than carrying its own copy, and it is the ONLY arm left.";

	// ABSENT, over the whole file: the arm that was never compiled.
	EXPECT_EQ( anyWhole.find( "pBase->DispatchTraceAttack(info,vecDir,ptr);" ), std::string::npos )
		<< "AnyEntity_t::DispatchTraceAttack's SDK arm is back. It was inside "
		   "`#ifndef SS_CBASEENTITY_ALIAS` and was never compiled; restoring it "
		   "does not restore any behaviour -- check whether the GUARD came back "
		   "with it, which would be a much larger revert.";

	EXPECT_EQ( anyWhole.find( "#ifndefSS_CBASEENTITY_ALIAS" ), std::string::npos )
		<< "anyentity.h guards on SS_CBASEENTITY_ALIAS again. The guard went "
		   "with the member call it was protecting; a new one here means a new "
		   "arm this build does not compile.";

	// PRESENT AND LIVE: pBase is a constant NULL and says so.
	EXPECT_NE( any.find( "ent.pBase=NULL;" ), std::string::npos )
		<< "AnyEntityOf computes pBase again. Every implementation of "
		   "IServerUnknown::GetBaseEntity() in server.so returns a constant "
		   "NULL, so anything computed here is a NULL with a lookup in front of "
		   "it -- and a reader could mistake it for a resolved entity.";

	// COUNTED RATHER THAN SEARCHED, and that is not fussiness: the deleted
	// name is a SUFFIX of its live successor, so a plain
	// `find( "EntityFromEntityHandle(" ) == npos` can never pass while the
	// successor is present. Equal counts say every occurrence in the file is
	// the Game- spelling -- the same "a needle must be REMOVED, not decorated"
	// rule the residue wave's own instrument broke on.
	EXPECT_EQ( CountOccurrences( anyWhole, "EntityFromEntityHandle(" ),
	           CountOccurrences( anyWhole, "GameEntityFromEntityHandle(" ) )
		<< "anyentity.h names EntityFromEntityHandle on its own again. That "
		   "family was deleted (d3a0bfa3a) after being proved a constant NULL; "
		   "GameEntityFromEntityHandle is the live successor.";

	EXPECT_LT( 0u, CountOccurrences( anyWhole, "GameEntityFromEntityHandle(" ) )
		<< "neither spelling is in the file, which would make the equality "
		   "above 0 == 0 and this fact vacuous.";

	// PRESENT AND LIVE: the successor that actually resolves something.
	// ss:: since P5 lane E: anyentity.h is on public/ headers (ss_entityhandle.h).
	EXPECT_NE( any.find( "ent.pGame=ss::GameEntityFromEntityHandle(pHandleEntity);" ),
	           std::string::npos )
		<< "AnyEntityOf no longer resolves the parallel hierarchy through "
		   "GameEntityFromEntityHandle. That is the one arm of this value type "
		   "that can be non-NULL; without it DispatchTraceAttack above is a "
		   "no-op for every entity in the game.";
}
