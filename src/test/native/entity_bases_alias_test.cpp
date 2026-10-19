//===== SourceSharp — sourcesharp/game/server/entity_bases.h, and the arm it lost =====//
//
// entity_bases.h is in the SERVER'S PRECOMPILED PRELUDE. cbase.h force-includes
// it into all 197 server translation units through baseentity_shared.h, so
// whatever it says, every server object says. Phase 13 step 4's trial flip
// measured what that costs when the header is wrong: armed, 180 of 196 server
// TUs failed and 86 of them had NOTHING else wrong -- three lines of prelude
// standing in front of the 2,741 sites the survey exists to count
// (docs/typedef-dryrun/floor_server_armed.txt, summary_server_armed.txt).
//
//-----------------------------------------------------------------------------
// READ THE ASSERTIONS, NOT THE FILE NAME. THE ALIAS IS RETIRED.
//-----------------------------------------------------------------------------
//
// This file used to compile entity_bases.h TWICE in one binary -- once here
// against a stand-in for the SDK's CBaseEntity (the UNARMED arm), once in
// entity_bases_alias_armed.cpp against the real public/igameentity.h (the ARMED
// arm) -- because the header carried an `#ifndef SS_CBASEENTITY_ALIAS` / `#else`
// pair at each of three sites and a preprocessor branch nobody compiles is a
// branch nobody knows is broken.
//
// the-close 3/N (0bc727ed9) DELETED public/ss_cbaseentity_alias.h. `CBaseEntity`
// is not a name in the compiled tree any more, the-close 1/N and 2/N deleted the
// 453 dead guarded arms, and entity_bases.h now carries ZERO guards: the three
// sites are the armed text, unconditionally. So THE SUBJECT OF THE UNARMED HALF
// OF THIS PAIR IS GONE, and no stand-in for `CBaseEntity` can be built, because
// the header no longer names it.
//
// WHAT THIS FILE IS NOW, and why it is not simply deleted. Two things survived
// the retirement and both are worth more than they were:
//
//   1. THE HEADER'S THREE SITES HAVE A SUCCESSOR PROPERTY, and it is stronger
//      than "the armed arm compiles". `CPointEntity::ObjectCaps()` used to be
//      `BaseClass::ObjectCaps() & ~FCAP_ACROSS_TRANSITION`; it is now a literal
//      `return 0`. Compiling it against the REAL IGameEntity -- which is what
//      entity_bases_alias_armed.cpp does -- cannot tell those two apart, because
//      IGameEntity has no ObjectCaps for the base term to have come from either
//      way. So this TU compiles the same header against a stand-in `IGameEntity`
//      THAT DOES ANSWER ObjectCaps, with a non-zero value, and requires the
//      override to answer zero anyway. That is the flip's actual claim -- "the
//      base term is gone, not merely unreachable" -- and nothing else in the
//      tree asserts it.
//
//   2. THE ABSENCES THEMSELVES. `#ifndef SS_CBASEENTITY_ALIAS` guards, a
//      `BaseClass::ObjectCaps()` term, and a `CBaseEntity( true )` constructor
//      call must all be gone from a prelude header, and "gone" here is not a
//      matter of taste: a guard back in this file is a dead arm in all 197
//      server TUs, and SS_CBASEENTITY_ALIAS is STILL DEFINED by both vpcs
//      (server_csharp.vpc:69, client_csharp.vpc:74) and selects nothing, so an
//      `#ifndef` on it silently deletes whatever it wraps.
//
// Every assertion below is written so that an EMPTY OR MISSING entity_bases.h
// fails it rather than passing it -- the absences are always paired with the
// live text that must be there instead.
//
//-----------------------------------------------------------------------------
// THE STAND-IN, AND WHAT PINS IT TO THE REAL INTERFACE
//-----------------------------------------------------------------------------
//
// The stand-in deliberately DIFFERS from the real IGameEntity: it has an
// ObjectCaps and a bServerOnly constructor, which the real interface does not.
// That is the whole instrument -- the differences are the two things the flip
// deleted from these classes, re-supplied so their absence from the DERIVED
// classes can be measured. A stand-in that nothing checks is a fixture agreeing
// with itself (docs/writing-tests.md), so TheStandInDiffersOnlyWhereItSaysSo
// below reads public/igameentity.h and requires those two members to be absent
// from the real thing, and entity_bases_alias_armed.cpp compiles the SAME header
// against the real interface for real.
//
//=============================================================================//

#include <string.h>

#include <fstream>
#include <string>
#include <vector>

#include "gtest/gtest.h"

#include "mathlib_test_env.h"

//-----------------------------------------------------------------------------
// The names entity_bases.h asks the world for. It asked for three; since the
// alias retired it asks for IGameEntity where it used to ask for CBaseEntity.
//-----------------------------------------------------------------------------

// game/shared/baseentity_shared.h:21. Only this one bit was load-bearing when
// the overrides masked it off; it is kept because it is half of the non-zero
// value the stand-in base answers with.
#define FCAP_ACROSS_TRANSITION 0x00000002
// game/shared/baseentity_shared.h:26. The other half, and the bit the deleted
// unarmed arm passed THROUGH -- so a stand-in base answering both bits can tell
// "returns zero", "passes the base through" and "masks one bit off" apart.
#define FCAP_IMPULSE_USE 0x00000010

// public/networkvar.h's non-VALIDATE_DECLARE_CLASS arm, which is the arm every
// GCC build of this tree takes (VALIDATE_DECLARE_CLASS is set only under
// _MSC_VER). Pinned by TheDeclareClassStandInMatchesNetworkvar below.
#define DECLARE_CLASS( className, baseClassName ) \
	typedef baseClassName BaseClass;              \
	typedef className ThisClass;

namespace standin
{

//-----------------------------------------------------------------------------
// NOT the real IGameEntity, and the two differences are the instrument.
//
//   ObjectCaps()        the real interface has none -- asserted below. Here it
//                       answers m_nBaseCaps, so a derived override that still
//                       consulted its base would be visible.
//   bServerOnly ctor    the real interface has none -- asserted below. Here it
//                       records what it was passed, so a derived class that
//                       still passed `true` would be visible.
//
// Everything else is the minimum a base class needs to be derived from.
//-----------------------------------------------------------------------------
class IGameEntity
{
public:
	IGameEntity( bool bServerOnly = false )
		: m_nBaseCaps( 0 ), m_bServerOnly( bServerOnly ) {}
	virtual ~IGameEntity() {}

	virtual int ObjectCaps( void ) { return m_nBaseCaps; }

	int m_nBaseCaps;
	bool m_bServerOnly;
};

#include "../game/server/entity_bases.h"

// The four out-of-line bodies live in sourcesharp/game/server/entity_bases.cpp, which needs
// the prelude. Stubs, because none of them is this test's subject -- but the
// vtables reference them, so without these nothing here can be instantiated.
void CPointEntity::Spawn( void ) {}
bool CPointEntity::KeyValue( const char *, const char * ) { return true; }
bool CServerOnlyPointEntity::KeyValue( const char *, const char * ) { return true; }
bool CLogicalEntity::KeyValue( const char *, const char * ) { return true; }

}  // namespace standin

namespace
{

//-----------------------------------------------------------------------------
// Reading the tree, in alias_mechanics_test.cpp's idiom: buildcpptests runs
// each binary with src/ as the working directory.
//-----------------------------------------------------------------------------
const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		std::ifstream probe(
			( std::string( kCandidates[ i ] ) + "/sourcesharp/game/server/entity_bases.h" ).c_str() );
		if ( probe.is_open() )
			return kCandidates[ i ];
	}
	return NULL;
}

std::vector< std::string > ReadLinesFromRoot( const char *pszRelative )
{
	std::vector< std::string > lines;
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return lines;

	std::ifstream file( ( std::string( pszRoot ) + "/" + pszRelative ).c_str() );
	std::string line;
	while ( std::getline( file, line ) )
	{
		if ( !line.empty() && line[ line.size() - 1 ] == '\r' )
			line.erase( line.size() - 1 );
		lines.push_back( line );
	}
	return lines;
}

// Whitespace-insensitive "does this line contain this code", so that a retab or
// a reflow of the header does not read as a deleted arm.
std::string Squeezed( const std::string &s )
{
	std::string out;
	for ( size_t i = 0; i < s.size(); i++ )
	{
		if ( s[ i ] == ' ' || s[ i ] == '\t' )
			continue;
		out += s[ i ];
	}
	return out;
}

bool Contains( const std::string &strHaystack, const char *pszNeedle )
{
	return strHaystack.find( pszNeedle ) != std::string::npos;
}

// The COMMENTS in these files still say CBaseEntity, deliberately: 0bc727ed9's
// renamer carried a code/comment/string mask because a blind rename turned
// "only ever written with an SDK CBaseEntity, none exists here" into nonsense.
// So every count of a NAME below is over code lines only.
bool IsCommentLine( const std::string &strLine )
{
	const size_t at = strLine.find_first_not_of( " \t" );
	if ( at == std::string::npos )
		return true;
	return strLine.compare( at, 2, "//" ) == 0;
}

int CountCodeLinesContaining( const std::vector< std::string > &lines, const char *pszSqueezedNeedle )
{
	int n = 0;
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		if ( IsCommentLine( lines[ i ] ) )
			continue;
		if ( Contains( Squeezed( lines[ i ] ), pszSqueezedNeedle ) )
			n++;
	}
	return n;
}

}  // namespace

//-----------------------------------------------------------------------------
// THE SITE THE FLIP CHANGED, MEASURED AGAINST A BASE THAT ANSWERS.
//
// Deleted text:  `return BaseClass::ObjectCaps() & ~FCAP_ACROSS_TRANSITION;`
// Live text:     `return 0;`
//
// Against the real IGameEntity those two are indistinguishable -- it has no
// ObjectCaps, so there is no base term either way. Against this file's stand-in,
// which answers FCAP_ACROSS_TRANSITION|FCAP_IMPULSE_USE, they are three
// different numbers: the deleted text would answer FCAP_IMPULSE_USE, a bare
// `BaseClass::ObjectCaps()` would answer both bits, and the live text answers 0.
//-----------------------------------------------------------------------------
TEST( EntityBasesStandIn, PointEntityIgnoresItsBaseAndAnswersZero )
{
	standin::CPointEntity ent;
	ent.m_nBaseCaps = FCAP_ACROSS_TRANSITION | FCAP_IMPULSE_USE;

	// The base really does answer non-zero, so the zero below is the override's
	// answer and not a base that happens to return nothing.
	EXPECT_EQ( FCAP_ACROSS_TRANSITION | FCAP_IMPULSE_USE,
		ent.standin::IGameEntity::ObjectCaps() );

	EXPECT_EQ( 0, ent.ObjectCaps() )
		<< "CPointEntity::ObjectCaps consulted its base. The flip's claim is "
		   "that the base term is GONE -- IGameEntity has no parent to forward "
		   "+USE bits from -- not that it happens to be zero today.";
}

TEST( EntityBasesStandIn, ServerOnlyEntityIgnoresItsBaseAndAnswersZero )
{
	standin::CServerOnlyEntity ent;
	ent.m_nBaseCaps = FCAP_ACROSS_TRANSITION | FCAP_IMPULSE_USE;

	EXPECT_EQ( FCAP_ACROSS_TRANSITION | FCAP_IMPULSE_USE,
		ent.standin::IGameEntity::ObjectCaps() );

	EXPECT_EQ( 0, ent.ObjectCaps() );
}

//-----------------------------------------------------------------------------
// THE CONSTRUCTOR SITE, SAME SHAPE.
//
// Deleted text:  `CServerOnlyEntity() : CBaseEntity( true ) {}`
// Live text:     `CServerOnlyEntity() {}`
//
// The `true` was CBaseEntity's bServerOnly and all it did was
// AddEFlags( EFL_SERVER_ONLY ). Armed there is no such constructor and no such
// flag, and EFL_SERVER_ONLY -- the SDK's "this one is never networked" -- is the
// DEFINING property of the IGameEntity hierarchy rather than a bit it sets.
//
// So the fact is no longer "the base is told true"; it is "NOTHING is told
// anything", asserted against a stand-in base that WOULD record it.
//-----------------------------------------------------------------------------
TEST( EntityBasesStandIn, NothingPassesServerOnlyToTheBaseAnyMore )
{
	// The flag is capable of being set, so `false` below is a measurement.
	standin::IGameEntity witness( true );
	ASSERT_TRUE( witness.m_bServerOnly )
		<< "the stand-in base no longer records bServerOnly, so every "
		   "EXPECT_FALSE below is vacuous";

	standin::CServerOnlyEntity ent;
	EXPECT_FALSE( ent.m_bServerOnly )
		<< "CServerOnlyEntity still passes something to its base constructor.";

	// And its two subclasses, which inherit whatever it does.
	standin::CServerOnlyPointEntity pt;
	EXPECT_FALSE( pt.m_bServerOnly );
	standin::CLogicalEntity logical;
	EXPECT_FALSE( logical.m_bServerOnly );

	// CPointEntity never passed it and still does not.
	standin::CPointEntity point;
	EXPECT_FALSE( point.m_bServerOnly );
}

//-----------------------------------------------------------------------------
// THE STAND-IN DIFFERS FROM THE REAL INTERFACE ONLY WHERE IT SAYS IT DOES.
//
// READ THE ASSERTIONS, NOT THE NAME. This was TheStandInMatchesTheRealClass and
// it read game/server/baseentity.h for `virtual int ObjectCaps( void );` and
// `CBaseEntity( bool bServerOnly=false );`. Both of those are GONE -- so is the
// class -- and a fact that keeps reading for them would pass or fail on text
// nothing compiles. Re-pointed at the class the header actually derives from.
//
// The two members the stand-in ADDS must be absent from the real interface. If
// IGameEntity ever grows an ObjectCaps, the two behavioural facts above stop
// measuring "the base term is gone" and start measuring "the base term is
// something else", with no visible change.
//
// The three CBaseEntity absences are asserted alongside, because "IGameEntity
// has no ObjectCaps" is also true of a file that was emptied.
//-----------------------------------------------------------------------------
TEST( EntityBasesStandIn, TheStandInDiffersOnlyWhereItSaysSo )
{
	const std::vector< std::string > iface = ReadLinesFromRoot( "public/igameentity.h" );
	ASSERT_FALSE( iface.empty() ) << "could not read public/igameentity.h";

	// The interface is really there and really is the base entity_bases.h names.
	EXPECT_EQ( 1, CountCodeLinesContaining( iface, "abstract_classIGameEntity" ) )
		<< "public/igameentity.h no longer declares `abstract_class IGameEntity`, "
		   "so the absences below are absences from something else";
	EXPECT_EQ( 1, CountCodeLinesContaining( iface, "virtual~IGameEntity(){}" ) )
		<< "IGameEntity is no longer destructible through the interface";

	EXPECT_EQ( 0, CountCodeLinesContaining( iface, "ObjectCaps" ) )
		<< "IGameEntity has acquired an ObjectCaps. Either it belongs there -- in "
		   "which case entity_bases.h's overrides should call it and the two "
		   "behavioural facts above should say so -- or it does not, and "
		   "igameentity.h's membership rule has been broken.";
	EXPECT_EQ( 0, CountCodeLinesContaining( iface, "IGameEntity(bool" ) )
		<< "IGameEntity has acquired a bServerOnly-shaped constructor, which is "
		   "the SDK bookkeeping the flip exists to leave behind.";

	// And the class the OLD stand-in stood in for is gone from the header that
	// used to define it. This read game/server/baseentity.h for the three
	// CBaseEntity spellings and for its include of igameentity.h. That header
	// is compiled by nothing since plan_server_novalve.md P5 and left the farm
	// in P6: the include fact retired with it, and the three absences take
	// their stronger form -- the header that held CBaseEntity cannot be read at
	// all -- anchored on a sibling the farm still links.
	ASSERT_FALSE( ReadLinesFromRoot( "game/shared/shareddefs.h" ).empty() )
		<< "could not read game/shared/shareddefs.h, so the absence below would "
		   "be answered by a reader that sees nothing";
	EXPECT_TRUE( ReadLinesFromRoot( "game/server/baseentity.h" ).empty() )
		<< "game/server/baseentity.h is readable from the farm again. It is the "
		   "header that defined `CBaseEntity`, which was retired in 0bc727ed9 "
		   "and the whole of this file assumes is gone.";
}

// The same, for the macro. GCC takes networkvar.h's second DECLARE_CLASS arm --
// the two bare typedefs re-spelled above. If that arm grows a member, BaseClass
// here stops being what BaseClass is in the build.
TEST( EntityBasesStandIn, TheDeclareClassStandInMatchesNetworkvar )
{
	const std::vector< std::string > lines =
		ReadLinesFromRoot( "public/networkvar.h" );
	ASSERT_FALSE( lines.empty() ) << "could not read public/networkvar.h";

	// Find the #else arm -- the one guarded OUT when VALIDATE_DECLARE_CLASS is
	// set, i.e. the one every GCC build takes.
	int iElse = -1;
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		if ( Squeezed( lines[ i ] ) == "#ifdefVALIDATE_DECLARE_CLASS" )
		{
			for ( size_t j = i + 1; j < lines.size(); j++ )
			{
				if ( Squeezed( lines[ j ] ) == "#else" )
				{
					iElse = (int)j;
					break;
				}
			}
			break;
		}
	}
	ASSERT_GE( iElse, 0 ) << "public/networkvar.h no longer has the "
							 "#ifdef VALIDATE_DECLARE_CLASS / #else pair";

	ASSERT_LT( (size_t)iElse + 3, lines.size() );
	EXPECT_EQ( "#defineDECLARE_CLASS(className,baseClassName)\\",
		Squeezed( lines[ iElse + 1 ] ) );
	EXPECT_EQ( "typedefbaseClassNameBaseClass;\\", Squeezed( lines[ iElse + 2 ] ) );
	EXPECT_EQ( "typedefclassNameThisClass;", Squeezed( lines[ iElse + 3 ] ) );
}

//-----------------------------------------------------------------------------
// NO GUARD, AND NO SITE, IS LEFT -- AND THE TEXT THAT REPLACED THEM IS THERE.
//
// READ THE ASSERTIONS, NOT THE NAME. This was EveryFlipSiteCarriesBothArms and
// it required THREE `#ifndef SS_CBASEENTITY_ALIAS` guards, three `#else` arms
// and no unguarded flip site. Its subject is gone: the alias retired and the
// three guards went with it, so the header now carries the armed text
// unconditionally.
//
// The polarity of the guard fact is therefore INVERTED rather than deleted, and
// that inversion has teeth of its own. SS_CBASEENTITY_ALIAS is STILL DEFINED by
// server_csharp.vpc:69 and client_csharp.vpc:74 -- 0bc727ed9 left both vpcs
// alone deliberately -- so an `#ifndef SS_CBASEENTITY_ALIAS` reintroduced here
// does not restore a choice; it SILENTLY DELETES whatever it wraps, in a header
// that every one of the 197 server TUs reads.
//
// "No guards" alone would pass over an empty file, so each absence is paired
// with the live text that must be there in its place.
//-----------------------------------------------------------------------------
TEST( EntityBasesShape, NoGuardAndNoFlipSiteRemainsAndTheArmedTextIsLive )
{
	const std::vector< std::string > lines =
		ReadLinesFromRoot( "sourcesharp/game/server/entity_bases.h" );
	ASSERT_FALSE( lines.empty() ) << "could not read sourcesharp/game/server/entity_bases.h";

	// ---- the live text, first: the three sites, as the flip left them.
	EXPECT_EQ( 1, CountCodeLinesContaining( lines, "classCPointEntity:publicIGameEntity" ) )
		<< "CPointEntity no longer derives from IGameEntity";
	EXPECT_EQ( 1, CountCodeLinesContaining( lines, "classCServerOnlyEntity:publicIGameEntity" ) )
		<< "CServerOnlyEntity no longer derives from IGameEntity";
	EXPECT_EQ( 2, CountCodeLinesContaining( lines, "virtualintObjectCaps(void){return0;}" ) )
		<< "the two ObjectCaps overrides are no longer the literal `return 0` the "
		   "flip left; that literal IS the site, and the behavioural facts above "
		   "are what say why it is zero";
	EXPECT_EQ( 1, CountCodeLinesContaining( lines, "CServerOnlyEntity(){}" ) )
		<< "CServerOnlyEntity's constructor is no longer the empty one the flip "
		   "left it as";

	// ---- and the absences.
	int nGuards = 0, nElse = 0, nEndif = 0;
	std::string strGuards;
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		if ( IsCommentLine( lines[ i ] ) )
			continue;
		const std::string sq = Squeezed( lines[ i ] );
		if ( sq.size() && sq[ 0 ] != '#' )
			continue;

		if ( Contains( sq, "SS_CBASEENTITY_ALIAS" ) )
		{
			nGuards++;
			strGuards += "\n    " + lines[ i ];
		}
		else if ( sq == "#else" )
			nElse++;
		else if ( sq == "#endif" )
			nEndif++;
	}

	EXPECT_EQ( 0, nGuards )
		<< "entity_bases.h conditions on SS_CBASEENTITY_ALIAS again. That macro "
		   "is DEFINED by both vpcs and names nothing: an `#ifndef` on it deletes "
		   "its arm outright in all 197 server TUs, and an `#ifdef` is a "
		   "conditional that is never not taken." << strGuards;
	EXPECT_EQ( 0, nElse )
		<< "entity_bases.h has a conditional arm again; there is nothing left in "
		   "this header for a build to choose between.";
	// The include guard's own #endif, and no other.
	EXPECT_EQ( 1, nEndif );

	EXPECT_EQ( 0, CountCodeLinesContaining( lines, "BaseClass::ObjectCaps" ) )
		<< "the base term is back in a prelude header. BaseClass is IGameEntity "
		   "here and IGameEntity has no ObjectCaps, so this does not compile in "
		   "any of the 197 server TUs -- which is the 180-TU failure "
		   "docs/typedef-dryrun/floor_server_armed.txt recorded.";
	EXPECT_EQ( 0, CountCodeLinesContaining( lines, "CBaseEntity(true)" ) )
		<< "the bServerOnly constructor call is back; CBaseEntity is not a name.";
	EXPECT_EQ( 0, CountCodeLinesContaining( lines, "CBaseEntity" ) )
		<< "`CBaseEntity` is spelled in CODE in entity_bases.h. It is not a name "
		   "in the compiled tree (0bc727ed9). Comments still say it and that is "
		   "deliberate -- this counts code lines only.";
}
