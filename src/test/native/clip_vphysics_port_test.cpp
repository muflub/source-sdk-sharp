//===== SourceSharp — func_clip_vphysics is C#'s, and here is what holds it up ===//
//
// bmodels.cpp's last classname moved to C#. bmodels_verdict_test.cpp recorded
// the REFUSAL to move it and is replaced by this file, which records the port
// and the three facts it rests on. Every one of those facts is in a file
// somebody else owns, and none of them is visible from FuncClipVphysics.cs —
// which is exactly why they are a test and not a paragraph.
//
// WHAT A CLIP BRUSH IS. An invisible brush that stops physics objects and
// nothing else: a player walks through it, a bullet goes through it, a trace
// does not find it, and a crate rolling at it stops dead. dm_lockdown places
// THREE.
//
// HOW STOCK DID IT. SOLID_VPHYSICS with FSOLID_NOT_SOLID on top, and then an
// override to undo the consequence: CCollisionEvent::ShouldCollide rejects any
// pair where either side is FSOLID_NOT_SOLID, so CFuncVPhysicsClip overrode
// CBaseEntity::ForceVPhysicsCollide and PhysicsOwner_t::ForcesCollisionWith
// asked it BEFORE that rule was reached. The class existed to answer one
// virtual.
//
// HOW C# DOES IT, and it is not a reimplementation of that override — it is
// the observation that the rule never applies to this hierarchy in the first
// place. PhysicsOwner_t::GetSolidFlags answers 0 for an entity outside the SDK,
// deliberately, with the reason written beside it: the neutral is what
// InitPhysics sets the moment such an entity gets an object at all, which is
// "SOLID_VPHYSICS and solid", and an entity that later stops being solid to the
// world is "a divergence recorded elsewhere". So a managed brush that is
// not-solid to TRACES is still solid to the SOLVER. That is the whole of a clip
// brush, for free, with no new C++ anywhere.
//
// THE COST OF THE PORT, stated so it can be checked: ZERO lines of C++ added.
// One `#ifndef SOURCESHARP` around a LINK_ENTITY_TO_CLASS, then the file left
// the build.
//
// WHAT WOULD BREAK IT. Three things, and each is asserted below:
//
//   1. PhysicsOwner_t::GetSolidFlags learning to read ICollideable for the
//      non-SDK arm. That would be a reasonable-looking improvement — the
//      information IS there — and it would silently turn dm_lockdown's three
//      clip brushes off. THE LOAD-BEARING ONE.
//
//   2. CStandaloneEntity::InitPhysics no longer clearing FSOLID_NOT_SOLID on
//      its way out. Harmless in itself, but FuncClipVphysics.cs orders its two
//      calls around that behaviour and says so; if it changes, the comment and
//      FuncClipVphysicsTests are describing something that no longer happens.
//
//   3. physics.cpp reading solid flags from somewhere other than the owner.
//      The rejection this port relies on NOT firing is
//      `(nSolidFlags0|nSolidFlags1) & FSOLID_NOT_SOLID`, and both halves come
//      from PhysicsOwner_t::GetSolidFlags. Ask ICollideable there instead and
//      fact 1 stops mattering, which is the same outcome by another route.
//
// A RED HERE IS NOT AUTOMATICALLY A REGRESSION. Read the message, re-measure,
// and either update the fact or take the consequence — the same standard
// gib_verdict_test.cpp and the file this replaces are written to.
//
// SAME STRIPPER RULE: this file's own prose quotes GetSolidFlags and
// FSOLID_NOT_SOLID in the paragraphs explaining them, and so does
// FuncClipVphysics.cs, so every scan over a C++ source reads it with comments
// REMOVED. The .cs scans are deliberately NOT stripped — see the note there.
//
//=============================================================================//

#include <string>
#include <vector>
#include <fstream>
#include <cstring>
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

std::string LoadStripped( const char *pszRel, const char *pszAnchor )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	const std::string all = ReadRaw( std::string( pszRoot ) + "/" + pszRel );

	EXPECT_FALSE( all.empty() ) << "could not open " << pszRel
		<< " — every invariant below would pass vacuously";

	const std::string stripped = StripComments( all );

	EXPECT_NE( NoWhitespace( stripped ).find( NoWhitespace( pszAnchor ) ), std::string::npos )
		<< pszRel << " does not contain its anchor \"" << pszAnchor
		<< "\" outside of comments; the file this test reasons about has been "
		<< "rewritten and its arithmetic is stale";

	return stripped;
}

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

//-----------------------------------------------------------------------------
// WHICH CLASSNAMES A SOURCE FILE ACTUALLY REGISTERS, as opposed to merely
// spells. Carried over from bmodels_verdict_test.cpp, which this file replaces,
// because the question outlives the refusal: `LINK_ENTITY_TO_CLASS( func_wall,
// CFuncWall )` inside `#ifndef SOURCESHARP` registers NOTHING, and a grep
// cannot tell the two apart.
//
// THE FIRST WRITING OF THE CHECK BELOW DID NOT USE THIS, and it could not fail.
// It looked for the registration and then asked `rfind( "#ifndefSOURCESHARP",
// at )` — a backwards search from the hit, which found one of the SIX OTHER
// guards earlier in the same file and answered "guarded" however the
// registration was actually written. Un-guarding it left the test green. The
// scan has to walk the nesting; a proximity search over a file with six other
// guards in it is measuring the file's history, not the site.
//-----------------------------------------------------------------------------
struct Registration_t
{
	std::string strClassName;
	bool bGuardedOut;      // inside #ifndef SOURCESHARP, so not compiled here
};

std::vector<Registration_t> RegistrationsIn( const std::string &strStripped )
{
	std::vector<Registration_t> out;
	std::vector<bool> stack;          // true = this level is a SOURCESHARP guard
	size_t nGuardDepth = 0;

	size_t pos = 0;
	while ( pos < strStripped.size() )
	{
		size_t eol = strStripped.find( '\n', pos );
		if ( eol == std::string::npos )
			eol = strStripped.size();
		std::string line = strStripped.substr( pos, eol - pos );
		pos = eol + 1;

		size_t b = line.find_first_not_of( " \t" );
		const std::string trimmed = ( b == std::string::npos ) ? std::string() : line.substr( b );

		if ( trimmed.compare( 0, 19, "#ifndef SOURCESHARP" ) == 0 )
		{
			stack.push_back( true );
			nGuardDepth++;
			continue;
		}
		if ( trimmed.size() > 2 && trimmed[ 0 ] == '#'
			&& ( trimmed.compare( 0, 3, "#if" ) == 0 ) )
		{
			stack.push_back( false );
			continue;
		}
		if ( trimmed.compare( 0, 6, "#endif" ) == 0 )
		{
			if ( !stack.empty() )
			{
				if ( stack.back() )
					nGuardDepth--;
				stack.pop_back();
			}
			continue;
		}
		if ( trimmed.compare( 0, 5, "#else" ) == 0 )
		{
			// The other arm of a `#ifndef SOURCESHARP` is the SOURCESHARP arm,
			// so the guard inverts rather than ending.
			if ( !stack.empty() && stack.back() )
			{
				stack.back() = false;
				nGuardDepth--;
			}
			continue;
		}

		const size_t hit = line.find( "LINK_ENTITY_TO_CLASS" );
		if ( hit == std::string::npos )
			continue;

		const size_t open = line.find( '(', hit );
		if ( open == std::string::npos )
			continue;
		size_t s = line.find_first_not_of( " \t", open + 1 );
		size_t e = line.find_first_of( " \t,)", s );
		if ( s == std::string::npos || e == std::string::npos )
			continue;

		Registration_t r;
		r.strClassName = line.substr( s, e - s );
		r.bGuardedOut = ( nGuardDepth > 0 );
		// The macro's own definition, not a use of it.
		if ( r.strClassName != "mapClassName" )
			out.push_back( r );
	}
	return out;
}

const Registration_t *Find( const std::vector<Registration_t> &v, const char *pszName )
{
	for ( size_t i = 0; i < v.size(); i++ )
		if ( v[ i ].strClassName == pszName )
			return &v[ i ];
	return NULL;
}

const char *kPhysicsOwner = "sourcesharp/game/shared/physicsowner.h";
const char *kShellBase    = "sourcesharp/game/server/shell_standalone_base.cpp";
const char *kPhysics      = "sourcesharp/game/server/physics.cpp";
// physics_shell.h's armed rule table stood here until plan_physics_bones_native_zero
// 2.3 deleted it; fact 3 reads the managed rules below instead.
// Managed's solver: the live ShouldCollide and, since
// plan_physics_bones_native_zero 2.3, the SS-PHYSWORLD report's evaluation.
const char *kSolverCs      = "sourcesharp/managed/SourceSharp.Server/PhysicsSolver.cs";
const char *kSolverWorldCs = "sourcesharp/managed/SourceSharp.Server/PhysicsSolverWorld.cs";
const char *kSolverRulesCs = "sourcesharp/managed/SourceSharp.Server/PhysicsSolverRules.cs";
const char *kCreationCs   = "sourcesharp/managed/SourceSharp.Server/EntityPhysicsCreation.cs";
const char *kManagedClip  =
	"sourcesharp/managed/SourceSharp.Server/Entities/Visibility/FuncClipVphysics.cs";

// How many times a needle appears. A `find` says "at least one", and the fact
// below is about exactly two -- one call site per arm of the ordering convar.
size_t CountOccurrences( const std::string &hay, const std::string &needle )
{
	size_t n = 0;
	for ( size_t at = hay.find( needle ); at != std::string::npos;
	      at = hay.find( needle, at + 1 ) )
		n++;
	return n;
}

} // namespace

//-----------------------------------------------------------------------------
// FACT 1, and the whole port rests on it.
//
// Asserted as the WHOLE conditional expression rather than as "the file
// mentions GetSolidFlags", because the failure this guards against is the arm
// changing while the name stays.
//-----------------------------------------------------------------------------
TEST( ClipVphysicsPort, TheSolverSeesARealNotSolidAndTheBrushForcesItsWayBack )
{
	// RE-RULED by physics stock parity A5/A6 (2026-10-02). This fact used to pin
	// `int GetSolidFlags() const { return 0; }` -- the neutral that made a
	// managed clip brush solid to the solver by hiding every managed entity's
	// FSOLID_NOT_SOLID from it. Its own message asked for "a real mechanism
	// first", and stock's is now the mechanism:
	//   - the owner answers the entity's real flags from its collideable, so the
	//     brush's NOT_SOLID reaches the solver as in stock; and
	//   - the brush's object carries kPhysFlagForceCollideMoveable, which the
	//     rule table answers with CFuncVPhysicsClip's no-filter rule before the
	//     solid rules (physics_shell_test: AClipBrushStopsAMoveablePhysicsObject,
	//     AClipBrushLetsAPlayerThrough; FuncClipVphysicsTests sets the bit).
	const std::string tight = NoWhitespace(
		LoadStripped( kPhysicsOwner, "int GetSolidFlags() const" ) );
	EXPECT_NE( tight.find( "if(pServer)returnpServer->GetCollideable()->GetSolidFlags();" ), std::string::npos )
		<< "PhysicsOwner_t::GetSolidFlags no longer reads a managed entity's real "
		<< "flags, so NOT_SOLID and FSOLID_TRIGGER stop reaching the solver.";
	EXPECT_EQ( tight.find( "intGetSolidFlags()const{return0;}" ), std::string::npos )
		<< "the neutral 0 is back";

	// The rules are managed's since plan_physics_bones_native_zero 2.3 deleted
	// physics_shell.h's table: PhysicsSolverRules answers the force flag
	// before the solid rules (PhysicsSolverRulesTests: A_clip_brush_*).
	const std::string shell = NoWhitespace( LoadStripped( kSolverRulesCs, "publicsealedclassPhysicsSolverRules" ) );
	EXPECT_NE( shell.find( "if(ForcesCollision(s0,s1)||ForcesCollision(s1,s0))return1;" ), std::string::npos )
		<< "the rules no longer answer the clip brush's force flag before "
		<< "the solid rules; with real flags, dm_lockdown's three clip brushes "
		<< "would stop clipping.";
}

//-----------------------------------------------------------------------------
// FACT 3: the rule that reads it is still asking the OWNER.
//
// Separate from fact 1 because it fails independently: the answer can be right
// and the caller can stop asking for it.
//
// REPOINTED BY THE `residue` WAVE, NOT RELAXED. THE ASSERTION IS THE SAME ONE;
// ONLY THE FILE IT IS ASKED OF MOVED, AND IT MOVED BECAUSE THE OLD ONE WAS
// DEAD SOURCE.
//
// WHAT THIS USED TO READ. Two lines of stock's CCollisionEvent::ShouldCollide
// in game/server/physics.cpp -- `int nSolidFlags0 = owner0.GetSolidFlags();` at
// 74b071771's physics.cpp:1816 and `if ( (nSolidFlags0|nSolidFlags1) &
// FSOLID_NOT_SOLID )` at 74b071771's physics.cpp:1871.
//
// WHY IT WENT RED, AND IT IS THE HAZARD THIS WAVE EXISTS FOR. Both lines were
// in the `#else` arm of the `#ifdef SOURCESHARP` that opens ShouldCollide's
// body, `#else` at 74b071771's physics.cpp:1683 and `#endif // SOURCESHARP` at
// 74b071771's physics.cpp:1907. The armed build defines SOURCESHARP, so the
// arm this fact read was NEVER COMPILED: it was describing a body no object in
// this tree has ever contained. The residue wave deleted it and the fact went
// red -- correctly, and late.
//
// WHERE THE SAME RULE LIVES NOW, ALL OF IT ARMED. The nine rules became fields
// of SourceSharpPhysicsRules and the reads became one POD fill:
//
//   * physics.cpp still takes BOTH owners from PhysicsOwnerOf and hands them,
//     and nothing else, to the fill;
//   * physics_shell.h's SourceSharp_FillCollisionSide takes nSolidFlags from
//     PhysicsOwner_t::GetSolidFlags -- inside SOURCESHARP_PHYSICS_SHELL_ENGINE,
//     which physics.cpp defines at physics.cpp:80, so it IS compiled;
//   * physics_shell.h's rule evaluation rejects the pair on the same mask, and
//     the mask is still literally FSOLID_NOT_SOLID -- seeded as 0x0004u and
//     pinned to the enum by a COMPILE_TIME_ASSERT in the same header. That arm
//     is in plain include-guard scope, so it is compiled unconditionally.
//
// The property fact 1 depends on is therefore unchanged: both halves of the
// not-solid test still come from PhysicsOwner_t::GetSolidFlags, which answers 0
// for a managed brush.
//
// AND SINCE plan_physics_bones_native_zero 2.3 ALL OF IT IS MANAGED: the
// fill is PhysicsSolver.Side over the owner's engine block, the rule is
// PhysicsSolverRules' not-solid clause, and physics_shell.h's table, fill and
// literal pins are deleted. The property is the same: both halves of the
// not-solid test come from the owner's own solid flags.
//
// AND THE ABSENCE IS ASSERTED TOO, because a rewrite that only checked the new
// site would pass just as happily on a tree that had grown a SECOND,
// ICollideable-based read back into physics.cpp. `GetSolidFlags` must not
// appear in physics.cpp's code at all.
//-----------------------------------------------------------------------------
TEST( ClipVphysicsPort, TheSolverStillTakesItsSolidFlagsFromTheOwner )
{
	// THE LIVE ANSWER IS MANAGED'S since plan_physics_bones_native_zero 2.1:
	// vphysics asks PhysicsSolver, which reads both sides' solid flags off the
	// owner's engine block (EnginePhysicsSolverWorld) -- PhysicsSolverTests'
	// The_world_is_owned_and_a_not_solid_entity_falls_through_it holds the
	// not-solid rule over that read. Since 2.3 the SS-PHYSWORLD report's
	// evaluation against the world is managed's too
	// (PhysicsSolver.AgainstTheWorld), so the caller half is held there: the
	// side's solid flags are the owner's block's, read in one place.
	const std::string solver = NoWhitespace( LoadStripped( kSolverCs, "privateCollisionSideSide(" ) );
	EXPECT_NE( solver.find( "side.SolidFlags=owner.Facts.SolidFlags;" ), std::string::npos )
		<< "managed's collision side no longer takes its solid flags from the owner's facts.";
	const std::string world = NoWhitespace( LoadStripped( kSolverWorldCs,
		"publicboolTryEntity(EntityHandleentity,outSolverEntityfacts)" ) );
	EXPECT_NE( world.find( "MemoryMarshal.Read<int>(block.Slice(SolidFlagsOffset))," ), std::string::npos )
		<< "the solver's entity facts no longer read solid flags from the engine block.";
	EXPECT_NE( NoWhitespace( LoadStripped( kSolverCs, "publicWorldVerdictAgainstTheWorld(" ) )
	               .find( "CollisionSideside=Side(owner,physicsObject);" ),
	           std::string::npos )
		<< "the report's question no longer builds its side the way ShouldCollide does.";

	// physics.cpp, which no longer holds the report: it must still not read
	// solid flags at all (the absence below), and the file is still the one
	// this test reasons about.
	const std::string tight = NoWhitespace(
		LoadStripped( kPhysics, "CWorld *PhysicsWorldOwner( const IPhysicsObject *pObject )" ) );

	// THE ABSENCE, over the same file. physics.cpp must ask for solid flags
	// NOWHERE -- not through the owner and not through anything else. A second
	// read appearing here is the ICollideable route coming back by another name,
	// and the presence assertions above would not see it.
	EXPECT_EQ( tight.find( "GetSolidFlags" ), std::string::npos )
		<< "physics.cpp reads solid flags directly again.\n"
		<< "Every solid-flag read in the collision path belongs in "
		<< "SourceSharp_FillCollisionSide, off PhysicsOwner_t. A read here is "
		<< "either a duplicate that can disagree with it, or -- if it is an "
		<< "ICollideable read -- the thing that makes dm_lockdown's three clip "
		<< "brushes stop clipping, with no diagnostic anywhere.";

	// THE RULE HALF, managed since plan_physics_bones_native_zero 2.3 deleted
	// physics_shell.h's rule table: PhysicsSolverRules rejects the pair on the
	// not-solid mask, unless the other side is a vphysics trigger, and the
	// mask is FSOLID_NOT_SOLID's value -- pinned to the engine's enum by
	// PhysicsSolverRulesTests.The_rule_literals_are_the_engines_own_values,
	// which is what the native COMPILE_TIME_ASSERT did.
	const std::string rules = NoWhitespace( LoadStripped( kSolverRulesCs, "publicsealedclassPhysicsSolverRules" ) );

	EXPECT_NE( rules.find( "if(((s0.SolidFlags|s1.SolidFlags)&NotSolidFlag)!=0)" ), std::string::npos )
		<< "the rules no longer reject a pair on the not-solid mask.\n"
		<< "That is not a problem for this port \u2014 it is the rule being sidestepped, "
		<< "not the mechanism \u2014 but the reasoning recorded in FuncClipVphysics.cs "
		<< "and in this file names it, so both would be describing something that "
		<< "does not happen. Re-read them before trusting either.";
	EXPECT_NE( rules.find( "publicconstintNotSolidFlag=0x0004;" ), std::string::npos )
		<< "the not-solid rule is no longer FSOLID_NOT_SOLID's value.";
}

//-----------------------------------------------------------------------------
// FACT 2: why the ORDER of the two calls in FuncClipVphysics.cs matters.
//-----------------------------------------------------------------------------
TEST( ClipVphysicsPort, InitPhysicsStillClearsNotSolidSoTheOrderMatters )
{
	// REPOINTED TO MANAGED (2026-10-12), NOT RELAXED. The entity's physics init
	// is managed's EntityPhysicsCreation now (native's CStandaloneEntity::
	// InitPhysics is deleted); the clearing moved with it, still read once into
	// a local so both arms of sourcesharp_solidity_before_physics hand
	// SetSolidity the same value (EntityPhysicsCreationTests'
	// StocksOrderSetsSolidityBeforeTheCreateWithNotSolidCleared and
	// TheOldOrderSetsSolidityAfterTheWake hold the behaviour; this holds the
	// line FuncClipVphysics.cs's comment names).
	const std::string tight = NoWhitespace(
		LoadStripped( kCreationCs, "public bool Create(EntityHandle entity, bool isStatic, bool asleep, bool debris)" ) );

	EXPECT_NE( tight.find( "SolidFlagsflags=f.SolidFlags&~SolidFlags.NotSolid;" ), std::string::npos )
		<< "EntityPhysicsCreation.Create no longer clears FSOLID_NOT_SOLID.\n"
		<< "That is not a break — it would make FuncClipVphysics.cs's ordering "
		<< "unnecessary rather than wrong — but the class comment and "
		<< "FuncClipVphysicsTests.ThePhysicsObjectIsMadeBeforeTheBrushGoesNotSolid "
		<< "both explain themselves by naming this line. Update them, and check "
		<< "no OTHER brush was relying on the clearing.";

	EXPECT_EQ( 2u, CountOccurrences( tight, "_host.SetSolidity(entity,SolidType.VPhysics,flags);" ) )
		<< "Create no longer hands the cleared flags to SetSolidity in both arms "
		<< "of sourcesharp_solidity_before_physics.";

	// And the native body it replaced is gone, not merely unreached.
	EXPECT_EQ( std::string::npos, NoWhitespace( LoadStripped( kShellBase, "void CStandaloneEntity::DestroyPhysics()" ) )
	                                  .find( "CStandaloneEntity::InitPhysics(" ) )
		<< "CStandaloneEntity::InitPhysics is back in " << kShellBase;
}

//-----------------------------------------------------------------------------
// The classname has exactly one owner, and the map still has instances.
//
// The .cs is read WITHOUT stripping comments, deliberately and in the opposite
// direction to every other scan here: this asks for PRESENCE, so a commented-out
// declaration only makes the check weaker in the safe direction... which is
// precisely why the [ScriptedEntity] attribute is matched with its bracket
// rather than the bare string, and why the C++ half is asked for ABSENCE from a
// STRIPPED file.
//-----------------------------------------------------------------------------
TEST( ClipVphysicsPort, CSharpServesTheClassnameAndTheTrackedMapStillHasThree )
{
	const char *pszRoot = SrcRoot();
	ASSERT_TRUE( pszRoot != NULL );

	const std::string managed = ReadRaw( std::string( pszRoot ) + "/" + kManagedClip );

	ASSERT_FALSE( managed.empty() )
		<< kManagedClip << " is gone. bmodels.cpp was deleted from the build on "
		<< "the strength of that file existing; with both gone, "
		<< "func_clip_vphysics is served by NOBODY and dm_lockdown's three "
		<< "brushes are an engine warning and three holes where physics used to "
		<< "stop.";

	EXPECT_NE( NoWhitespace( managed ).find( "[ScriptedEntity(EntityNames.FuncClipVphysics," ),
	           std::string::npos )
		<< kManagedClip << " no longer declares itself for the classname. See "
		<< "above.";

	// And the C++ registration stays guarded out, wherever bmodels.cpp is. It
	// is out of the build entirely as this is written, so this is asked of the
	// SOURCE TREE rather than of the vpc: a file that came back with an
	// unguarded registration would be a second factory for one classname.
	//
	// Through the nesting walker, NOT a backwards search for a nearby `#ifndef`
	// — see the note above RegistrationsIn. bmodels.cpp has six other guards in
	// it and a proximity search finds one of those whatever this site says.
	struct stat st;
	const std::string bmodels = std::string( pszRoot ) + "/game/server/bmodels.cpp";
	if ( stat( bmodels.c_str(), &st ) == 0 )
	{
		const std::vector<Registration_t> regs =
			RegistrationsIn( StripComments( ReadRaw( bmodels ) ) );

		const Registration_t *pClip = Find( regs, "func_clip_vphysics" );

		EXPECT_TRUE( pClip == NULL || pClip->bGuardedOut )
			<< "game/server/bmodels.cpp is back AND registers func_clip_vphysics "
			<< "unguarded. C# serves that classname; two factories for one name "
			<< "is a conflict, not a preference.";
	}

	const char *pszDir = MapDir();
	ASSERT_TRUE( pszDir != NULL )
		<< "no game/mod_sharp/maps directory found from the working directory; "
		<< "this test would otherwise pass by reading nothing";

	const std::string strTracked = std::string( pszDir ) + "/dm_lockdown.bsp";
	const std::string lump = EntityLump( strTracked );
	ASSERT_FALSE( lump.empty() )
		<< strTracked << " has no readable entity lump. It is a TRACKED file, so "
		<< "this is not a missing build artefact.";

	// The reader, checked before it is believed.
	ASSERT_GE( CountClassname( lump, "worldspawn" ), 1 )
		<< "no worldspawn in " << strTracked << "; what this test read is not an "
		<< "entity lump";

	EXPECT_EQ( CountClassname( lump, "func_clip_vphysics" ), 3 )
		<< strTracked << " no longer carries exactly three func_clip_vphysics.\n"
		<< "Three is the number the whole argument for porting this classname "
		<< "was built on — route_to_arm.md §3b read ss_sandbox.bsp alone, found "
		<< "none, and concluded no map classname was served only by C++. If the "
		<< "map has changed, re-take the decision rather than adjusting the "
		<< "number: at zero the port has no live customer at all.";
}

//-----------------------------------------------------------------------------
// The scanner the check above rests on, checked against the thing it claims to
// model. Carried over from bmodels_verdict_test.cpp with its reason intact:
// without this the guard-awareness is unfalsifiable, because a scanner that
// answered "guarded" for everything would satisfy that EXPECT_TRUE — which is
// exactly what the FIRST writing of it did, silently, by searching BACKWARDS
// for a nearby `#ifndef` in a file that has six of them. Un-guarding the
// registration left the test green.
//
// This one always runs, including once bmodels.cpp has left the tree and the
// check above has nothing to read.
//-----------------------------------------------------------------------------
TEST( ClipVphysicsPort, TheGuardScannerDistinguishesGuardedFromUnguarded )
{
	const std::string kSynthetic =
		"LINK_ENTITY_TO_CLASS( plain_one, CPlainOne );\n"
		"#ifndef SOURCESHARP\n"
		"LINK_ENTITY_TO_CLASS( guarded_one, CGuardedOne );\n"
		"#endif\n"
		"#ifdef SOMETHING_ELSE\n"
		"LINK_ENTITY_TO_CLASS( other_conditional, COther );\n"
		"#endif\n"
		"#ifndef SOURCESHARP\n"
		"#if 1\n"
		"LINK_ENTITY_TO_CLASS( nested_guarded, CNested );\n"
		"#endif\n"
		"#else\n"
		"LINK_ENTITY_TO_CLASS( in_the_sourcesharp_arm, CArm );\n"
		"#endif\n"
		"LINK_ENTITY_TO_CLASS( plain_two, CPlainTwo );\n";

	const std::vector<Registration_t> regs = RegistrationsIn( kSynthetic );
	ASSERT_EQ( regs.size(), 6u );

	EXPECT_FALSE( Find( regs, "plain_one" )->bGuardedOut );
	EXPECT_TRUE( Find( regs, "guarded_one" )->bGuardedOut );
	EXPECT_FALSE( Find( regs, "other_conditional" )->bGuardedOut )
		<< "a conditional that is not a SOURCESHARP guard must not be read as one";
	EXPECT_TRUE( Find( regs, "nested_guarded" )->bGuardedOut )
		<< "a guard must survive a nested #if";
	EXPECT_FALSE( Find( regs, "in_the_sourcesharp_arm" )->bGuardedOut )
		<< "the #else arm of `#ifndef SOURCESHARP` IS the SourceSharp build";

	// The failure mode that let the un-guarded case through: `plain_two` comes
	// AFTER two closed guards and must still read as unguarded, which is
	// precisely what a backwards proximity search gets wrong.
	EXPECT_FALSE( Find( regs, "plain_two" )->bGuardedOut )
		<< "a registration AFTER a closed guard reads as guarded. That is the "
		<< "bug that made the un-guarded-registration check above impossible to "
		<< "fail: bmodels.cpp's func_clip_vphysics sits after six closed "
		<< "`#ifndef SOURCESHARP` blocks.";
}
