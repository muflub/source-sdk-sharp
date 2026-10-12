//========= SourceSharp — the identity every GetAbsOrigin retype rests on ====//
//
// THE CLAIM THIS FILE GUARDS IS ABOUT A DIFFERENT FILE'S SOURCE, so this test
// reads source rather than running any — the same method as
// sdk_player_cast_test, and for the same reason.
//
// plan_baseentity.md Phase 13 precondition (e) retypes call sites that held a
// CBaseEntity * for GetAbsOrigin() so they hold the interface and ask
// ICollideable instead:
//
//     pEnt->GetAbsOrigin()   ==>   pEnt->GetCollideable()->GetCollisionOrigin()
//
// That substitution is only sound because the collideable's origin is the
// entity's abs origin UNCONDITIONALLY — no solid-type test, no flag test, no
// branch, parented entities included. Add a condition to it and every retyped
// site silently starts reading a different vector for some entities and the
// same one for the rest, which is the worst available failure: correct on the
// map you tested and wrong on the one you did not.
//
//=============================================================================//
// WHERE THAT IDENTITY LIVED, AND WHERE IT LIVES NOW — THE `residue` WAVE
//=============================================================================//
//
// Until the `residue` wave this file read the STOCK body:
//
//     const Vector& CCollisionProperty::GetCollisionOrigin() const
//     {
//         return m_pOuter->GetAbsOrigin();
//     }
//
// at 74b071771:src/game/shared/collisionproperty.cpp:816, with
// GetCollisionAngles at :821. BOTH OF THEM SAT INSIDE THE
// `#ifndef SS_CBASEENTITY_ALIAS` ARM OPENED AT
// 74b071771:src/game/shared/collisionproperty.cpp:421 AND CLOSED AT :1647 —
// a 1,227-line region with no `#else` that carried the WHOLE of
// CCollisionProperty's out-of-line half. The armed configuration is
// SS_CBASEENTITY_ALIAS and SOURCESHARP both DEFINED, which is the only
// configuration this project builds, so NOT ONE BYTE OF THAT REGION WAS EVER
// COMPILED. The `residue` wave (74b071771..a46e8d645) deleted it, along with
// 5,282 other lines of dead guarded arms across 44 stock TUs, and the object
// set did not move: 588 objects, identical DEFINED and UNDEFINED symbols.
//
// So the subject was dead source, and the fact is re-pointed rather than
// dropped — at the hierarchy that ACTUALLY answers ICollideable in this build,
// which is the port's own shell and is under src/sourcesharp/ where a stock-TU
// residue pass cannot reach it:
//
//     sourcesharp/game/server/shell_standalone.h        CStandaloneEntity
//     sourcesharp/game/client/shell_standalone_client.h C_StandaloneEntity
//
// Both spell the identity more strongly than stock did — the collideable
// origin and the abs origin are THE SAME EXPRESSION, `Block().Head.Origin`,
// read straight out of the engine block — and both live in ordinary class
// bodies inside nothing but the header's own include guard, so the text this
// test reads is text the compiler reads.
//
// AND THE OTHER HALF, WHICH IS THE HALF THAT ACTUALLY BIT. GetAbsAngles is NOT
// a general identity and must never be swept the way GetAbsOrigin was. Stock's
// reason was its own body:
//
//     const QAngle& CCollisionProperty::GetCollisionAngles() const
//     {
//         if ( IsBoundsDefinedInEntitySpace() )
//             return m_pOuter->GetAbsAngles();
//         return vec3_angle;
//     }
//
// which answered vec3_angle for SOLID_NONE, SOLID_BBOX and
// FSOLID_FORCE_WORLD_ALIGNED, so the sweep silently ZEROED every point
// entity's orientation. That body is gone with the rest of the dead arm, and
// the rule it justified is NOT gone: it is what
// game/shared/collideable_center.h and collideable_center_math.h branch on,
// over a general ICollideable, in code this build compiles. That branch is
// what GetCollisionAnglesIsNotAnIdentity reads now.
//
// THE PORT'S OWN SHELL COLLAPSES THE TWO, AND THAT IS NOT A LICENCE TO SWEEP.
// C_StandaloneEntity/CStandaloneEntity answer `Block().Head.Angles` from BOTH
// GetCollisionAngles() and GetAbsAngles(), which is sound for THAT hierarchy
// because the block stores one angles value that is both. It says nothing
// about any other ICollideable, and the test below asserts the collapse and
// the surviving general branch TOGETHER so that neither can be read as the
// other.
//
// WHY A SOURCE SCANNER RATHER THAN A CALL. Neither shell can be constructed
// without an engine, an entity block and a server or client DLL, so there is
// no binary in which this can be asked at runtime today. The claim is a claim
// about the text, so the text is what is read.
//
// THE SCANNER IS ITSELF CHECKED, because a source scanner that reads nothing
// passes every claim it is asked to enforce — the failure mode this project has
// now found forty-three instances of. The fixtures below assert that every file
// was really opened and carries an ANCHOR the change under test cannot evict,
// that both function bodies were really located, and that the body-extractor
// still reports what it should when pointed at hand-written positive and
// negative fixtures.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <sys/stat.h>

#include <cstring>
#include <fstream>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// Locating the tree. buildcpptests runs each binary with src/ as the working
// directory; the list lets the binary also run by hand from the repo root, and
// keeps "no tree found" distinguishable from "tree found, file missing".
//
// BOTH `game` AND `sourcesharp` must be there. This test now reads shim headers
// as well as stock source, and a root that had only one of the two would open
// half the files and silently skip the rest.
//-----------------------------------------------------------------------------
const char *SourceRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[ i ] ) + "/game";
		const std::string shim = std::string( kCandidates[ i ] ) + "/sourcesharp";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode )
			&& stat( shim.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
		{
			return kCandidates[ i ];
		}
	}
	return NULL;
}

std::string FileUnderSrc( const char *pszRelative )
{
	const char *pszRoot = SourceRoot();
	if ( !pszRoot )
		return std::string();

	std::string strPath( pszRoot );
	strPath += "/";
	strPath += pszRelative;

	std::ifstream file( strPath.c_str() );
	if ( !file.is_open() )
		return std::string();

	std::string strAll( ( std::istreambuf_iterator< char >( file ) ),
	                    std::istreambuf_iterator< char >() );
	return strAll;
}

//-----------------------------------------------------------------------------
// The files, named once. The two shells are the SUBJECT; the two collideable
// helpers carry the general angles rule; collisionproperty.cpp is read only to
// assert what is NO LONGER in it.
//-----------------------------------------------------------------------------
const char *kServerShell   = "sourcesharp/game/server/shell_standalone.h";
const char *kClientShell   = "sourcesharp/game/client/shell_standalone_client.h";
const char *kCenterAdapter = "sourcesharp/game/shared/collideable_center.h";
const char *kCenterMath    = "sourcesharp/game/shared/collideable_center_math.h";
const char *kCollisionCpp  = "sourcesharp/game/shared/collisionproperty.cpp";

std::string CollisionPropertySource()
{
	return FileUnderSrc( kCollisionCpp );
}

//-----------------------------------------------------------------------------
// Whitespace-squeezed form, so that formatting can change without this test
// pretending the meaning did.
//-----------------------------------------------------------------------------
std::string Squeezed( const std::string &s )
{
	std::string out;
	out.reserve( s.size() );
	for ( size_t i = 0; i < s.size(); i++ )
	{
		const char c = s[ i ];
		if ( c != ' ' && c != '\t' && c != '\r' && c != '\n' )
			out += c;
	}
	return out;
}

//-----------------------------------------------------------------------------
// The body of the named out-of-line member, squeezed, braces excluded.
//
// Finds "CCollisionProperty::<name>(", walks forward to the first '{' and then
// matches braces. Returns the empty string when the definition is not found,
// which the fixtures below distinguish from "found and empty".
//
// STILL HERE AFTER THE `residue` WAVE, AND STILL LOAD-BEARING, for the opposite
// reason it was written: it is now what says the stock definitions are GONE
// from collisionproperty.cpp rather than what reads them.
//-----------------------------------------------------------------------------
std::string BodyOf( const std::string &strSource, const char *pszMember )
{
	std::string strNeedle( "CCollisionProperty::" );
	strNeedle += pszMember;

	// Every occurrence is a candidate. A CALL and a DECLARATION both reach a
	// ';' before they reach a '{'; only a DEFINITION reaches the '{' first. So
	// a rejected candidate ADVANCES the search rather than ending it -- the
	// definition may still be further down the file, which is exactly the case
	// the fixture below pins.
	size_t at = 0;
	for ( ;; )
	{
		at = strSource.find( strNeedle, at );
		if ( at == std::string::npos )
			return std::string();

		const size_t after = at + strNeedle.size();
		at = after;

		// The next non-space character must open the argument list, or the
		// needle matched a longer name that merely starts the same way.
		size_t p = after;
		while ( p < strSource.size() && ( strSource[ p ] == ' ' || strSource[ p ] == '\t' ) )
			p++;
		if ( p >= strSource.size() || strSource[ p ] != '(' )
			continue;

		const size_t open = strSource.find( '{', p );
		if ( open == std::string::npos )
			return std::string();

		const size_t semi = strSource.find( ';', p );
		if ( semi != std::string::npos && semi < open )
			continue; // a call, or a declaration

		int depth = 0;
		size_t i = open;
		for ( ; i < strSource.size(); i++ )
		{
			if ( strSource[ i ] == '{' ) depth++;
			else if ( strSource[ i ] == '}' )
			{
				depth--;
				if ( depth == 0 )
					break;
			}
		}
		if ( depth != 0 )
			return std::string();

		return Squeezed( strSource.substr( open + 1, i - open - 1 ) );
	}
}

//-----------------------------------------------------------------------------
// The body that follows a DECLARATOR given verbatim, squeezed, braces excluded.
//
// The shells define these members INLINE in the class body, so there is no
// `Class::member` spelling for BodyOf to key on. The needle is therefore the
// declarator text itself -- "virtual const Vector &GetCollisionOrigin() const
// OVERRIDE" -- which is also what keeps this off the prose: every comment in
// those headers that discusses GetCollisionOrigin says the NAME, and none of
// them spells the whole declarator.
//
// Empty means not found, exactly as BodyOf does, and every caller asserts
// non-empty before reading it.
//-----------------------------------------------------------------------------
std::string BodyAfter( const std::string &strSource, const char *pszDeclarator )
{
	const size_t at = strSource.find( pszDeclarator );
	if ( at == std::string::npos )
		return std::string();

	const size_t open = strSource.find( '{', at + strlen( pszDeclarator ) );
	if ( open == std::string::npos )
		return std::string();

	int depth = 0;
	size_t i = open;
	for ( ; i < strSource.size(); i++ )
	{
		if ( strSource[ i ] == '{' ) depth++;
		else if ( strSource[ i ] == '}' )
		{
			depth--;
			if ( depth == 0 )
				break;
		}
	}
	if ( depth != 0 )
		return std::string();

	return Squeezed( strSource.substr( open + 1, i - open - 1 ) );
}

bool Has( const std::string &hay, const char *needle )
{
	return hay.find( needle ) != std::string::npos;
}

// Stock's body, kept because the scanner fixtures at the bottom drive BodyOf
// over synthetic input shaped like it.
const char *kOriginBody = "returnm_pOuter->GetAbsOrigin();";

// The shells' bodies. ONE EXPRESSION EACH, and the collideable answer and the
// abs answer are the same expression -- which is the identity, spelled rather
// than argued.
const char *kShimOriginBody = "returnBlock().Head.Origin;";
const char *kShimAnglesBody = "returnBlock().Head.Angles;";

// The declarators. Full text, so a comment naming the member cannot match.
const char *kSrvCollideOrigin = "virtual const Vector &GetCollisionOrigin() const OVERRIDE";
const char *kSrvCollideAngles = "virtual const QAngle &GetCollisionAngles() const OVERRIDE";
const char *kSrvAbsOrigin     = "const Vector &GetAbsOrigin() const";
const char *kSrvAbsAngles     = "const QAngle &GetAbsAngles() const";

const char *kCliCollideOrigin = "virtual const Vector &GetCollisionOrigin() const OVERRIDE";
const char *kCliCollideAngles = "virtual const QAngle &GetCollisionAngles() const OVERRIDE";
const char *kCliAbsOrigin     = "virtual const Vector &GetAbsOrigin( void ) const OVERRIDE";
const char *kCliAbsAngles     = "virtual const QAngle &GetAbsAngles( void ) const OVERRIDE";

} // namespace

//-----------------------------------------------------------------------------
// The identity itself, on both shells: the collideable origin and the abs
// origin are one expression, with nothing else in either body.
//
// Goes red on ANY added condition, any added statement, any change of subject,
// on either side -- and on the two halves drifting apart, which is the failure
// the retype would actually suffer.
//
// AND THE ABSENCE, asserted here rather than assumed: stock's own
// GetCollisionOrigin body is GONE from game/shared/collisionproperty.cpp. It
// was inside the never-compiled `#ifndef SS_CBASEENTITY_ALIAS` arm at
// 74b071771:src/game/shared/collisionproperty.cpp:421-1647 (the body at :816)
// and the `residue` wave deleted it. Without this half the test above could
// go green over a tree that had quietly grown a SECOND, conditional origin
// answer on the stock class -- which is exactly the state this file was
// written to catch.
//-----------------------------------------------------------------------------
TEST( CollisionOriginIdentity, GetCollisionOriginIsUnconditionalGetAbsOrigin )
{
	const std::string strServer = FileUnderSrc( kServerShell );
	const std::string strClient = FileUnderSrc( kClientShell );
	ASSERT_FALSE( strServer.empty() ) << kServerShell << " was not read; the scan proves nothing.";
	ASSERT_FALSE( strClient.empty() ) << kClientShell << " was not read; the scan proves nothing.";

	const std::string strSrvCollide = BodyAfter( strServer, kSrvCollideOrigin );
	const std::string strSrvAbs     = BodyAfter( strServer, kSrvAbsOrigin );
	ASSERT_FALSE( strSrvCollide.empty() )
		<< "CStandaloneEntity::GetCollisionOrigin's definition was not found in " << kServerShell;
	ASSERT_FALSE( strSrvAbs.empty() )
		<< "CStandaloneEntity::GetAbsOrigin's definition was not found in " << kServerShell;

	EXPECT_EQ( std::string( kShimOriginBody ), strSrvCollide )
		<< "the SERVER shell's GetCollisionOrigin is no longer the unconditional\n"
		   "engine-block origin. Every 'pEnt->GetCollideable()->GetCollisionOrigin()'\n"
		   "written by plan_baseentity.md Phase 13's retype now reads a different\n"
		   "vector than the GetAbsOrigin() it replaced. Found body: " << strSrvCollide;
	EXPECT_EQ( strSrvAbs, strSrvCollide )
		<< "the SERVER shell's GetAbsOrigin and GetCollisionOrigin have drifted\n"
		   "apart. They are the retype's two sides; equal is the whole claim.";

	const std::string strCliCollide = BodyAfter( strClient, kCliCollideOrigin );
	const std::string strCliAbs     = BodyAfter( strClient, kCliAbsOrigin );
	ASSERT_FALSE( strCliCollide.empty() )
		<< "C_StandaloneEntity::GetCollisionOrigin's definition was not found in " << kClientShell;
	ASSERT_FALSE( strCliAbs.empty() )
		<< "C_StandaloneEntity::GetAbsOrigin's definition was not found in " << kClientShell;

	EXPECT_EQ( std::string( kShimOriginBody ), strCliCollide )
		<< "the CLIENT shell's GetCollisionOrigin is no longer the unconditional\n"
		   "engine-block origin. Found body: " << strCliCollide;
	EXPECT_EQ( strCliAbs, strCliCollide )
		<< "the CLIENT shell's GetAbsOrigin and GetCollisionOrigin have drifted apart.";

	// ...and stock's conditional-capable original is GONE rather than merely
	// unused. `residue` wave, 74b071771..a46e8d645.
	const std::string strStock = CollisionPropertySource();
	ASSERT_TRUE( Has( strStock, "CDirtySpatialPartitionEntityList" ) )
		<< kCollisionCpp << " was not read, so the absence below proves nothing.";
	EXPECT_TRUE( BodyOf( strStock, "GetCollisionOrigin" ).empty() )
		<< kCollisionCpp << " defines CCollisionProperty::GetCollisionOrigin again.\n"
		   "The `residue` wave deleted it with the rest of the never-compiled\n"
		   "`#ifndef SS_CBASEENTITY_ALIAS` arm at 74b071771:...collisionproperty.cpp:421.\n"
		   "A second origin answer on the stock class is a second thing the retype\n"
		   "could be reading.";
}

//-----------------------------------------------------------------------------
// Stated separately and deliberately: the body contains no branch at all. The
// test above already implies it, but this one names the failure, so a red run
// says which of the two properties broke.
//
// PAIRED WITH A POSITIVE, because "no branch" is an absence and an absence over
// an empty body is free: each body must still NAME the block field it reads.
//-----------------------------------------------------------------------------
TEST( CollisionOriginIdentity, GetCollisionOriginHasNoBranch )
{
	const char *kFiles[]       = { kServerShell,      kClientShell      };
	const char *kDeclarators[] = { kSrvCollideOrigin, kCliCollideOrigin };

	for ( size_t i = 0; i < 2; i++ )
	{
		const std::string strSource = FileUnderSrc( kFiles[ i ] );
		ASSERT_FALSE( strSource.empty() ) << kFiles[ i ] << " was not read.";

		const std::string strBody = BodyAfter( strSource, kDeclarators[ i ] );
		ASSERT_FALSE( strBody.empty() ) << kFiles[ i ] << " — GetCollisionOrigin's body was not found.";

		EXPECT_NE( std::string::npos, strBody.find( "Head.Origin" ) )
			<< kFiles[ i ] << " — GetCollisionOrigin no longer reads the engine block's\n"
			   "origin at all, so the 'no branch' checks below would pass over a body\n"
			   "that answers something else entirely. Found body: " << strBody;

		EXPECT_EQ( std::string::npos, strBody.find( "if(" ) )
			<< kFiles[ i ] << " — GetCollisionOrigin has grown a condition; the retype\n"
			   "identity is now true for some entities and false for others.";
		EXPECT_EQ( std::string::npos, strBody.find( "?" ) )
			<< kFiles[ i ] << " — GetCollisionOrigin has grown a conditional expression.";
	}
}

//-----------------------------------------------------------------------------
// The NON-identity, which is the half that was got wrong twice.
//
// WHAT THIS FACT NOW READS, AND WHY IT MOVED. Stock's reason was
// CCollisionProperty::GetCollisionAngles' own conditional body at
// 74b071771:src/game/shared/collisionproperty.cpp:821 — inside the
// never-compiled `#ifndef SS_CBASEENTITY_ALIAS` arm opened at :421, deleted by
// the `residue` wave (74b071771..a46e8d645). The RULE it justified did not go
// with it. Over a GENERAL ICollideable the world-space arithmetic still
// branches on the angles, and that branch is compiled: it is
// game/shared/collideable_center.h's adapter and collideable_center_math.h's
// two-arm computation. Losing either of those is losing the reason not to
// sweep GetAbsAngles -> GetCollisionAngles, so that is what this asserts.
//
// AND THE PORT'S OWN SHELL COLLAPSES THE TWO, WHICH IS ASSERTED HERE RATHER
// THAN LEFT TO BE DISCOVERED. C_StandaloneEntity and CStandaloneEntity answer
// `Block().Head.Angles` from both GetCollisionAngles() and GetAbsAngles(). That
// is sound for THAT hierarchy — one stored value is genuinely both — and it is
// not a general licence. Both halves are stated together so that a reader who
// finds the collapse cannot mistake it for the general rule having changed.
//
// If this ever goes red, re-read the plan's mapping section before touching a
// single angles call site.
//-----------------------------------------------------------------------------
TEST( CollisionOriginIdentity, GetCollisionAnglesIsNotAnIdentity )
{
	// 1. THE GENERAL RULE, in the code that computes over a general ICollideable.
	const std::string strAdapter = FileUnderSrc( kCenterAdapter );
	const std::string strMath    = FileUnderSrc( kCenterMath );
	ASSERT_FALSE( strAdapter.empty() ) << kCenterAdapter << " was not read.";
	ASSERT_FALSE( strMath.empty() ) << kCenterMath << " was not read.";

	EXPECT_TRUE( Has( strAdapter, "pCollide->GetCollisionAngles() == vec3_angle" ) )
		<< kCenterAdapter << " no longer branches on the collideable's angles.\n"
		   "That branch IS the standing reason not to sweep GetAbsAngles ->\n"
		   "GetCollisionAngles: off the zero-angles arm the world box is a\n"
		   "TRANSFORM and not an add, and for SOLID_NONE, SOLID_BBOX and\n"
		   "FSOLID_FORCE_WORLD_ALIGNED a stock collideable answers vec3_angle\n"
		   "whatever the entity's own angles are.";

	EXPECT_TRUE( Has( strMath, "if ( bAnglesAreZero )" ) )
		<< kCenterMath << " no longer has a zero-angles arm, so the adapter's\n"
		   "branch above feeds a computation that ignores it.";
	EXPECT_TRUE( Has( strMath, "VectorTransform" ) )
		<< kCenterMath << " no longer names the rotated arm. A one-armed\n"
		   "CollideableCenter_Compute is the angles sweep, written out.";

	// 2. STOCK'S CONDITIONAL BODY IS GONE, not merely unused. This is the half
	//    the `residue` wave removed, and asserting its absence is what stops the
	//    rule above being quietly re-derived from a body nothing compiles.
	const std::string strStock = CollisionPropertySource();
	ASSERT_TRUE( Has( strStock, "CDirtySpatialPartitionEntityList" ) )
		<< kCollisionCpp << " was not read, so the absence below proves nothing.";
	EXPECT_TRUE( BodyOf( strStock, "GetCollisionAngles" ).empty() )
		<< kCollisionCpp << " defines CCollisionProperty::GetCollisionAngles again.\n"
		   "It was never compiled — the arm at 74b071771:...:421 has no #else — so a\n"
		   "definition reappearing there is dead text pretending to be the rule.";

	// 3. THE SHELLS COLLAPSE ANGLES ONTO ABS ANGLES, on purpose, and that must
	//    stay a statement about the SHELLS. Both bodies, both sides.
	const char *kFiles[]   = { kServerShell,      kClientShell      };
	const char *kCollide[] = { kSrvCollideAngles, kCliCollideAngles };
	const char *kAbs[]     = { kSrvAbsAngles,     kCliAbsAngles     };

	for ( size_t i = 0; i < 2; i++ )
	{
		const std::string strSource = FileUnderSrc( kFiles[ i ] );
		ASSERT_FALSE( strSource.empty() ) << kFiles[ i ] << " was not read.";

		const std::string strCollide = BodyAfter( strSource, kCollide[ i ] );
		const std::string strAbs     = BodyAfter( strSource, kAbs[ i ] );
		ASSERT_FALSE( strCollide.empty() ) << kFiles[ i ] << " — GetCollisionAngles' body was not found.";
		ASSERT_FALSE( strAbs.empty() ) << kFiles[ i ] << " — GetAbsAngles' body was not found.";

		EXPECT_EQ( std::string( kShimAnglesBody ), strCollide )
			<< kFiles[ i ] << " — the shell's GetCollisionAngles is no longer the\n"
			   "engine block's angles. If it has grown stock's entity-space condition\n"
			   "the block no longer holds one value that is both, and the collapse\n"
			   "below stops being sound. Found body: " << strCollide;
		EXPECT_EQ( strAbs, strCollide )
			<< kFiles[ i ] << " — the shell's GetAbsAngles and GetCollisionAngles have\n"
			   "drifted apart. On THIS hierarchy they are one stored value; if that\n"
			   "stops being true, every angles ask on it has to pick a side.";
	}
}

//=============================================================================//
// The scanner, checked. Everything below tests this file, not the SDK.
//=============================================================================//

//-----------------------------------------------------------------------------
// WHAT WAS READ, PROVED BY ANCHOR AND NOT BY SIZE.
//
// This fact used to be `collisionproperty.cpp is bigger than 20,000 bytes`, and
// the `residue` wave took it to 13,675 by deleting the never-compiled
// `#ifndef SS_CBASEENTITY_ALIAS` arm at
// 74b071771:src/game/shared/collisionproperty.cpp:421-1647. A size floor is a
// bad anchor for exactly that reason — it is crossed by edits that are not
// about this file's subject at all, and lane 1 shipped one that tripped on its
// first mutation for the wrong reason. Every file this test reads is now
// proved by a string the change under test cannot evict, and the two that
// matter most are under src/sourcesharp/, which no stock-TU pass touches.
//-----------------------------------------------------------------------------
TEST( CollisionOriginIdentityScanner, ReallyReadTheFile )
{
	ASSERT_TRUE( SourceRoot() != NULL )
		<< "no tree carrying both game/ and sourcesharp/ was found from the working "
		   "directory; nothing was scanned.";

	struct Anchor_t { const char *pszFile; const char *pszAnchor; };
	const Anchor_t kAnchors[] = {
		{ kServerShell,   "class CStandaloneEntity" },
		{ kClientShell,   "class C_StandaloneEntity" },
		{ kCenterAdapter, "UTIL_CollideableWorldSpaceCenter" },
		{ kCenterMath,    "CollideableCenter_Compute" },
		// The class whose out-of-line half went; this one is COMPILED and is
		// what says collisionproperty.cpp is still the real file.
		{ kCollisionCpp,  "CDirtySpatialPartitionEntityList" },
	};

	for ( size_t i = 0; i < sizeof( kAnchors ) / sizeof( kAnchors[ 0 ] ); i++ )
	{
		const std::string strSource = FileUnderSrc( kAnchors[ i ].pszFile );
		ASSERT_FALSE( strSource.empty() )
			<< kAnchors[ i ].pszFile << " came back empty; it did not open.";
		EXPECT_TRUE( Has( strSource, kAnchors[ i ].pszAnchor ) )
			<< kAnchors[ i ].pszFile << " does not contain '" << kAnchors[ i ].pszAnchor
			<< "' — the scanner opened something, but not this file.";
	}
}

//-----------------------------------------------------------------------------
// The body-extractor finds what is there, and reports empty for what is not.
//-----------------------------------------------------------------------------
TEST( CollisionOriginIdentityScanner, BodyOfFindsAndMisses )
{
	const std::string strFixture =
		"const Vector& CCollisionProperty::Yes() const\n"
		"{\n"
		"\treturn m_pOuter->GetAbsOrigin();\n"
		"}\n";

	EXPECT_EQ( std::string( kOriginBody ), BodyOf( strFixture, "Yes" ) );
	EXPECT_TRUE( BodyOf( strFixture, "NotPresent" ).empty() );
}

//-----------------------------------------------------------------------------
// The extractor is not fooled by a DECLARATION, which has no body, nor by a
// CALL to the member from somewhere else in the file.
//-----------------------------------------------------------------------------
TEST( CollisionOriginIdentityScanner, BodyOfSkipsDeclarationsAndCalls )
{
	const std::string strDecl = "const Vector& CCollisionProperty::Yes() const;\n";
	EXPECT_TRUE( BodyOf( strDecl, "Yes" ).empty() );

	const std::string strCallThenDef =
		"void Elsewhere() { CCollisionProperty::Yes(); }\n"
		"const Vector& CCollisionProperty::Yes() const\n"
		"{\n"
		"\treturn m_pOuter->GetAbsOrigin();\n"
		"}\n";
	// The call is found FIRST. An extractor that stops at the first occurrence
	// answers "" here, and would then answer "" for any real file that happened
	// to call the member before defining it -- a scan that silently proves
	// nothing while reporting green. The real definition is what must come back.
	EXPECT_EQ( std::string( kOriginBody ), BodyOf( strCallThenDef, "Yes" ) );

	// And a declaration ahead of the definition, which is the ordinary shape of
	// a header included above a .cpp's own body.
	const std::string strDeclThenDef =
		"const Vector& CCollisionProperty::Yes() const;\n"
		"const Vector& CCollisionProperty::Yes() const\n"
		"{\n"
		"\treturn m_pOuter->GetAbsOrigin();\n"
		"}\n";
	EXPECT_EQ( std::string( kOriginBody ), BodyOf( strDeclThenDef, "Yes" ) );
}

//-----------------------------------------------------------------------------
// The DECLARATOR extractor, checked the same way, because the three facts above
// now rest on it entirely.
//
// The negative matters more than the positive here: BodyAfter answering "" for
// a declarator that IS present would make every equality above compare two
// empty strings and pass. Each caller asserts non-empty for that reason, and
// this fixture is what says the empty answer means what those asserts think.
//-----------------------------------------------------------------------------
TEST( CollisionOriginIdentityScanner, BodyAfterFindsAndMisses )
{
	const std::string strFixture =
		"class CShell\n"
		"{\n"
		"\t// GetCollisionOrigin is discussed at length in prose like this.\n"
		"\tvirtual const Vector &GetCollisionOrigin() const OVERRIDE { return Block().Head.Origin; }\n"
		"\tvirtual const QAngle &GetCollisionAngles() const OVERRIDE { return Block().Head.Angles; }\n"
		"};\n";

	EXPECT_EQ( std::string( kShimOriginBody ),
	           BodyAfter( strFixture, "virtual const Vector &GetCollisionOrigin() const OVERRIDE" ) );
	EXPECT_EQ( std::string( kShimAnglesBody ),
	           BodyAfter( strFixture, "virtual const QAngle &GetCollisionAngles() const OVERRIDE" ) );

	// A declarator that is not there at all.
	EXPECT_TRUE( BodyAfter( strFixture, "virtual int NotPresent() const OVERRIDE" ).empty() );

	// THE PROSE DOES NOT MOVE THE ANSWER. The fixture's comment names
	// GetCollisionOrigin one line ABOVE the declarator, which is the shape both
	// shells really have; the needle is the whole declarator, so the first match
	// is the definition and not the sentence about it. The EXPECT_EQ above is
	// what says so -- a name-only scan would have taken the comment's line and
	// walked to the same brace, which is why the needles are declarators.

	// Nested braces inside a body are matched rather than stopped at.
	const std::string strNested =
		"int F() { if ( a ) { return 1; } return 0; }\n";
	EXPECT_EQ( std::string( "if(a){return1;}return0;" ), BodyAfter( strNested, "int F()" ) );
}

//-----------------------------------------------------------------------------
// The positive claim goes red on a mutated body: this is the mutation check for
// the assertion that matters, run in place rather than promised.
//-----------------------------------------------------------------------------
TEST( CollisionOriginIdentityScanner, AMutatedBodyIsRejected )
{
	const std::string strMutated =
		"const Vector& CCollisionProperty::GetCollisionOrigin() const\n"
		"{\n"
		"\tif ( IsBoundsDefinedInEntitySpace() )\n"
		"\t\treturn m_pOuter->GetAbsOrigin();\n"
		"\treturn vec3_origin;\n"
		"}\n";

	const std::string strBody = BodyOf( strMutated, "GetCollisionOrigin" );
	ASSERT_FALSE( strBody.empty() );
	EXPECT_NE( std::string( kOriginBody ), strBody );
	EXPECT_NE( std::string::npos, strBody.find( "if(" ) );

	// ...and the shells' shape is rejected the same way, on the extractor the
	// three facts above actually use.
	const std::string strShimMutated =
		"\tvirtual const Vector &GetCollisionOrigin() const OVERRIDE "
		"{ return IsBoundsDefinedInEntitySpace() ? Block().Head.Origin : vec3_origin; }\n";
	const std::string strShimBody =
		BodyAfter( strShimMutated, "virtual const Vector &GetCollisionOrigin() const OVERRIDE" );
	ASSERT_FALSE( strShimBody.empty() );
	EXPECT_NE( std::string( kShimOriginBody ), strShimBody );
	EXPECT_NE( std::string::npos, strShimBody.find( "?" ) );
}
