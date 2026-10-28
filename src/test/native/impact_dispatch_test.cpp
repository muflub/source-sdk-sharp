//========= SourceSharp — a bullet leaves a mark, and both ends of saying so ==//
//
// THE BUG. Shooting anything in this build produced NO impact effect at all --
// no decal, no flecks, no dust, no impact sound -- and produced it in SILENCE,
// which is the one shape plan_entity.md §7 forbids outright ("a behaviour that
// is refused is a ContractBaseline entry with a reason, never a silent no-op").
// Lane `live-batch` measured it with the whole firing chain asserted first: a
// crate at the aim point, `debug-api aim` naming prop_physics, carried rounds
// 90 -> 63 so the gun demonstrably fired, sprite quads +2115 so the client was
// demonstrably rendering -- and both effect ledgers flat, temp entities 58 ->
// 58 and impact particles 0 -> 0.
//
// IT HAD THREE PARTS AND EACH ONE ALONE MAKES THE OTHER TWO INVISIBLE, which is
// why each gets its own assertion here rather than one test of "an impact
// happens":
//
//   1. THE SERVER NEVER ASKED. Arsenal.FireBullets traced, applied damage and
//      bled, and never called Effects.ImpactEffect. Not a regression: `git log
//      -S'ImpactEffect' --follow` over Arsenal.cs names no commit at all across
//      the sixteen that have touched it.
//
//   2. THE CLIENT COULD NOT HAVE ANSWERED IF IT HAD. DECLARE_CLIENT_EFFECT(
//      "Impact", ImpactCallback ) is written only in tf/, hl2/ and sdk/, and
//      client_csharp.vpc compiles no file from any of them -- behaviour ledger
//      row C7, which c_te_effect_dispatch.cpp records as open and says may be
//      closed "only by making the capability real".
//
//   3. AND THE OBVIOUS CALLBACK STILL DRAWS NOTHING. CEffectData::GetEntity()
//      is ClientEntityList().GetBaseEntityFromHandle, which filters to
//      GetGameEntity() != NULL and so answers NULL for the parallel hierarchy
//      -- which on this client is everything, WORLDSPAWN INCLUDED. A faithful
//      copy of stock's ImpactCallback reads that NULL as "the thing that was
//      hit has been destroyed", plays a sound and returns. Measured, with the
//      other two fixed and a probe in the callback, one line per round:
//
//          impact ent=(nil) surfprop=13 material=84(T) dmg=2 hitbox=0
//
//      -- the surfaceprop crossing correctly and the entity NULL, for the
//      FLOOR. CEffectData::entindex() is the spelling that answers: it goes
//      through IClientNetworkable, which both hierarchies implement.
//
// WHY A SOURCE SCAN. Every one of these is a SHAPE AT A CALL SITE, in code that
// cannot run without an engine: a registration list built at static init, a
// client entity list, a decal system and a particle manager. sdk_player_cast_
// test.cpp and impact_subject_test.cpp beside it established the idiom for
// exactly this, and its rule is followed here: read with whole-line comments
// STRIPPED, because this file's own prose names every symbol it looks for and
// a check a comment can satisfy is a check about comments.
//
// THE LIVE HALF IS tools/fx-impact-gate, which is where "and it actually draws"
// is measured -- the native impact ledger moving, scaling with the rounds
// spent, staying put with the trigger not pulled, and the row that moves being
// the right one. This file is what stops the wiring being undone by an edit
// that still compiles.
//
// THE CLIENT HALF IS C# SINCE LANE M1-C (2026-10-25): the "Impact" client
// effect is SourceSharp.Client.TempEnts.BulletImpactEffect, whose decisions
// BulletImpactEffectTests holds one behaviour a fact. What is checked here is
// the wiring a source scan can see: the managed handler claims the name, the
// client's list registers it, nothing native claims it again, it resolves its
// entity by index, and the shim it calls decides nothing.
//
// PROVEN RED, each independently. Drop `BulletImpact(),` from ClientModule's
// handler list and SomethingClaimsTheImpactName fails. Drop the ImpactEffect
// call from FireBullets and TheShotDispatchesAnImpact fails. Move that call
// inside the `_entities.IsValid(trace.Entity)` block and
// TheImpactIsNotGatedOnNamingWhatWasHit fails while every other test here stays
// green -- which is the point of it being separate: that edit leaves every shot
// into a WALL silent again, and a wall is the common case.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <dirent.h>

#include <fstream>
#include <sstream>
#include <string>
#include <vector>

namespace
{

// buildcpptests runs each binary with src/ as the working directory. A list
// rather than one path so that running the binary by hand from the repo root
// works too, and so "no directory" is distinguishable from "a directory with
// nothing in it". impact_subject_test.cpp's, unchanged.
const char *SourceRoot()
{
	static const char *kCandidates[] = { "sourcesharp", "src/sourcesharp", "../src/sourcesharp" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		DIR *pDir = opendir( kCandidates[ i ] );
		if ( pDir )
		{
			closedir( pDir );
			return kCandidates[ i ];
		}
	}
	return NULL;
}

// The file's lines with whole-line comments dropped -- `//` and C#'s `///`
// alike, both of which begin with the same two characters.
std::vector< std::string > CodeLines( const char *pszRelative )
{
	std::vector< std::string > out;

	const char *pszRoot = SourceRoot();
	if ( !pszRoot )
		return out;

	std::ifstream file( ( std::string( pszRoot ) + "/" + pszRelative ).c_str() );
	if ( !file.is_open() )
		return out;

	std::string line;
	while ( std::getline( file, line ) )
	{
		const size_t nFirst = line.find_first_not_of( " \t" );
		if ( nFirst != std::string::npos && line.compare( nFirst, 2, "//" ) == 0 )
			continue;

		out.push_back( line );
	}

	return out;
}

// The index of the first code line naming something, or -1.
int FirstNaming( const std::vector< std::string > &lines, const char *pszNeedle )
{
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		if ( lines[ i ].find( pszNeedle ) != std::string::npos )
			return (int)i;
	}
	return -1;
}

int CountNaming( const std::vector< std::string > &lines, const char *pszNeedle )
{
	int n = 0;
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		if ( lines[ i ].find( pszNeedle ) != std::string::npos )
			n++;
	}
	return n;
}

// ONE METHOD'S LINES, from its signature to the brace that closes it at the
// SAME INDENTATION.
//
// Scoped rather than whole-file, and that is a finding rather than tidiness:
// the first draft of the "not on the narrow trace" test below banned
// `_world.TraceLine(` across Arsenal.cs and went red on a correct tree, because
// MeleeSwing and the weapon-drop floor probe both take it and both are right
// to. A ban with the wrong scope is a test that has to be weakened rather than
// obeyed, which is how a real rule gets deleted.
std::vector< std::string > BodyOf( const std::vector< std::string > &lines,
                                   const char *pszSignature )
{
	std::vector< std::string > out;

	int nStart = -1;
	std::string strIndent;
	for ( size_t i = 0; i < lines.size(); i++ )
	{
		if ( lines[ i ].find( pszSignature ) == std::string::npos )
			continue;
		nStart = (int)i;
		strIndent = lines[ i ].substr( 0, lines[ i ].find_first_not_of( " \t" ) );
		break;
	}

	if ( nStart < 0 )
		return out;

	const std::string strClose = strIndent + "}";
	for ( size_t i = (size_t)nStart; i < lines.size(); i++ )
	{
		out.push_back( lines[ i ] );
		if ( i > (size_t)nStart && lines[ i ] == strClose )
			break;
	}

	return out;
}

const char *kCallback = "game/client/shell_impact_effects.cpp";
const char *kHandler  = "managed/SourceSharp.Client/TempEnts/BulletImpactEffect.cs";
const char *kModule   = "managed/SourceSharp.Client/ClientModule.cs";
const char *kArsenal  = "managed/SourceSharp.Server/Arsenal.cs";
const char *kAbi      = "host/sourcesharp.h";
const char *kTrace    = "game/server/shell_worldquery.cpp";

//-----------------------------------------------------------------------------
// THE CHECK THAT WOULD OTHERWISE NOT BE ABLE TO FAIL.
//
// Several of the tests below assert that something is ABSENT, and a file that
// could not be read is absent in the same way -- so a rename, a move, or a
// working directory nobody expected would turn them green and mean nothing.
// Every file is asserted readable first, and every ban's ANCHOR is asserted
// present, so "no hits" means "not spelled" rather than "looking at nothing".
//-----------------------------------------------------------------------------
TEST( ImpactDispatch, TheFilesTheseTestsAreAboutWereActuallyRead )
{
	ASSERT_TRUE( SourceRoot() != NULL )
		<< "no src/sourcesharp/ from here, so every scan below is vacuous";

	ASSERT_FALSE( CodeLines( kCallback ).empty() ) << kCallback;
	ASSERT_FALSE( CodeLines( kHandler ).empty() )  << kHandler;
	ASSERT_FALSE( CodeLines( kModule ).empty() )   << kModule;
	ASSERT_FALSE( CodeLines( kArsenal ).empty() )  << kArsenal;
	ASSERT_FALSE( CodeLines( kAbi ).empty() )      << kAbi;
	ASSERT_FALSE( CodeLines( kTrace ).empty() )    << kTrace;

	EXPECT_NE( -1, FirstNaming( CodeLines( kHandler ), "public void Dispatch(in EffectData data)" ) )
		<< kHandler << " no longer has a dispatch at all, so the claims about "
		   "how it resolves its entity are looking at nothing";
	EXPECT_NE( -1, FirstNaming( CodeLines( kCallback ), "SourceSharp_ClientShotTrace" ) )
		<< kCallback << " no longer carries the impact's trace, so the bans on "
		   "what it decides are looking at nothing";
	EXPECT_NE( -1, FirstNaming( CodeLines( kArsenal ), "public void FireBullets" ) )
		<< kArsenal << " no longer defines FireBullets, so the shot-path "
		   "assertions are looking at nothing";
}

//-----------------------------------------------------------------------------
// LEDGER ROW C7. Something in THIS build claims the name the server sends.
//
// The name itself is not negotiable and is checked as a literal: the row's own
// rule is that it "may be closed only by making the capability real -- never by
// substituting a name that resolves", so a registration under any other
// spelling would close it by hiding it. Since lane M1-C it is the managed
// BulletImpactEffect, registered in the client's effect handler list, which the
// dispatch asks before the native registry.
//-----------------------------------------------------------------------------
TEST( ImpactDispatch, SomethingClaimsTheImpactName )
{
	EXPECT_EQ( 1, CountNaming( CodeLines( kHandler ), "public string EffectName => \"Impact\";" ) )
		<< kHandler << " answers to the dispatch name \"Impact\" exactly once";
	EXPECT_EQ( 1, CountNaming( CodeLines( kModule ), "BulletImpact()," ) )
		<< kModule << " puts the handler in the client's effect handler list. "
		   "Without it the dispatch finds no \"Impact\" and prints \"effect "
		   "'Impact' not found on client\" -- which is what this build did for "
		   "its whole life.";
}

//-----------------------------------------------------------------------------
// AND NOTHING NATIVE CLAIMS IT AS WELL: two owners of one name is a dispatch
// that answers whichever was asked first, and a native one would be a second
// set of decisions about every bullet.
//-----------------------------------------------------------------------------
TEST( ImpactDispatch, NoNativeRegistrationClaimsTheImpactName )
{
	EXPECT_EQ( 0, CountNaming( CodeLines( kCallback ), "DECLARE_CLIENT_EFFECT( \"Impact\"," ) )
		<< kCallback << " registers \"Impact\" again; the managed BulletImpactEffect owns it";
}

//-----------------------------------------------------------------------------
// AND IT RESOLVES THE ENTITY THE WAY THIS CLIENT CAN ANSWER: the effect's
// INDEX, clipped through the slot that resolves it for both hierarchies
// (CEffectData::GetEntity() filters to C_BaseEntity and is NULL for every
// entity here, worldspawn included -- measured, and why the impact was silent).
//-----------------------------------------------------------------------------
TEST( ImpactDispatch, TheHandlerResolvesTheEntityByIndex )
{
	const std::vector< std::string > lines = CodeLines( kHandler );

	EXPECT_NE( -1, FirstNaming( lines, "engine.ClipToEntity(start, traceExt, data.EntIndex)" ) )
		<< "the handler clips the shot to the entity the effect's index names";
	EXPECT_NE( -1, FirstNaming( CodeLines( kCallback ), "SourceSharp_ClientEntityFromIndex( nEntity )" ) )
		<< "and the slot resolves that index through the lookup that answers for "
		   "both hierarchies";
}

//-----------------------------------------------------------------------------
// THE SERVER ASKS AT ALL. Stock's DoImpactEffect, which
// CBaseEntity::FireBullets calls for every pellet that lands.
//-----------------------------------------------------------------------------
TEST( ImpactDispatch, TheShotDispatchesAnImpact )
{
	const std::vector< std::string > lines = CodeLines( kArsenal );

	EXPECT_NE( -1, FirstNaming( lines, "_effects.ImpactEffect(" ) )
		<< "Arsenal.FireBullets is the port's whole bullet path and must "
		   "dispatch an impact for a round that lands. Its absence is silent: "
		   "no refusal is printed and no ledger moves.";
}

//-----------------------------------------------------------------------------
// AND IT IS NOT GATED ON NAMING WHAT WAS HIT.
//
// A SEPARATE TEST FROM THE ONE ABOVE, and separate on purpose: the edit that
// tidies the impact into the `_entities.IsValid(trace.Entity)` block beside the damage
// leaves every shot into a WALL OR A FLOOR silent again, and a wall is the
// common case. Stock dispatches for those too -- UTIL_ImpactTrace resolves such
// a trace to worldspawn and CWorld::ImpactTrace answers for it.
//
// Asserted as an ORDER within the method rather than as a string, because what
// is wrong with the bad version is where the call sits, not how it is spelled.
//-----------------------------------------------------------------------------
TEST( ImpactDispatch, TheImpactIsNotGatedOnNamingWhatWasHit )
{
	const std::vector< std::string > lines = CodeLines( kArsenal );

	const int nFire   = FirstNaming( lines, "public void FireBullets" );
	const int nImpact = FirstNaming( lines, "_effects.ImpactEffect(" );
	const int nSubject = FirstNaming( lines, "_entities.IsValid(trace.Entity)" );

	ASSERT_NE( -1, nFire );
	ASSERT_NE( -1, nImpact );
	ASSERT_NE( -1, nSubject )
		<< "the damage arm's own guard is gone, so there is no longer an order "
		   "to assert and this test is looking at nothing";

	EXPECT_GT( nImpact, nFire )
		<< "the impact is dispatched inside FireBullets";
	EXPECT_LT( nImpact, nSubject )
		<< "and BEFORE the guard that requires a named subject, which the "
		   "damage arm needs and the impact does not";
}

//-----------------------------------------------------------------------------
// AND THE SHOT ASKS FOR THE SURFACE.
//
// The client chooses the decal and the impact sound from the MATERIAL, and the
// material comes out of the surfaceprop the SERVER sent -- which is what
// CBaseEntity::ImpactTrace writes into its CEffectData. A shot path on the
// narrow trace has no surfaceprop to send and leaves a wooden crate taking a
// concrete decal and cement flecks: visible, and wrong, which is the failure
// mode hardest to notice.
//-----------------------------------------------------------------------------
TEST( ImpactDispatch, TheShotAsksForTheSurfaceAndNotTheNarrowTrace )
{
	const std::vector< std::string > body =
		BodyOf( CodeLines( kArsenal ), "public void FireBullets" );

	ASSERT_FALSE( body.empty() )
		<< "FireBullets' body could not be delimited, so this test is looking "
		   "at nothing";

	EXPECT_NE( -1, FirstNaming( body, "TraceLineSurface(" ) )
		<< "FireBullets must take the trace that answers the surfaceprop";
	EXPECT_EQ( 0, CountNaming( body, "_world.TraceLine(" ) )
		<< "and the shot path must not be on the narrow one, which cannot "
		   "answer it. SCOPED TO THIS METHOD: MeleeSwing and the weapon-drop "
		   "floor probe both take the narrow trace and both are right to.";
}

//-----------------------------------------------------------------------------
// THE TWO TRACE SLOTS AND THEIR "NO SURFACE IS -1" RULE LEFT NATIVE
// (plan_server_native_v2 T4f). TraceLine and TraceLineSurface retired with the
// server's other trace slots: managed traces through IEngineTrace::TraceRay over
// the managed trace filter and reads the engine's trace_t itself, so there is no
// shipped slot left to widen. The rule that a line which hit nothing names
// surfaceprop -1 rather than `default` (index 0) is ServerTraceResults.Surface's
// now, held by ServerTraceResultsTests.ALineThatHitNothingNamesNoSurface.
//-----------------------------------------------------------------------------

//=============================================================================
// AND THE CHOICE ABOVE IT IS MANAGED (plan_shim.md §6, lane 4b-effects; the
// whole impact since lane M1-C).
//
// Everything above is about an impact HAPPENING. These are about who decides
// what it looks like: which effect family the surface gets, which particle
// system the new-effects table names for it, whether flecks are drawn, and
// which decal is left. All four were literals in shell_impact_effects.cpp --
// a `switch`, a twenty-six row array, a two-line boolean written twice, and a
// string -- and all four are in SourceSharp.Client/TempEnts/ImpactChoice.cs,
// asked by BulletImpactEffect. What the decisions answer is
// ImpactChoiceTests' and BulletImpactEffectTests'; these stop a piece of one
// coming back to the shim in an edit that still compiles.
//=============================================================================

const char *kChoice = "managed/SourceSharp.Client/TempEnts/ImpactChoice.cs";

//-----------------------------------------------------------------------------
// THE SHIM DECIDES NOTHING ABOUT AN IMPACT. One needle per piece that left: the
// material chain, the particle table (a plain row, a no-flecks row and the warp
// shield), the decal name and the fleck suppression's two spellings.
//-----------------------------------------------------------------------------
TEST( ImpactChoiceSeam, TheShimDecidesNothingAboutAnImpact )
{
	const std::vector< std::string > lines = CodeLines( kCallback );

	ASSERT_NE( -1, FirstNaming( lines, "SourceSharp_ClientShotTrace" ) )
		<< kCallback << " is not the file these bans are about";

	EXPECT_EQ( 0, CountNaming( lines, "CHAR_TEX_" ) );
	EXPECT_EQ( 0, CountNaming( lines, "impact_concrete" ) );
	EXPECT_EQ( 0, CountNaming( lines, "warp_shield_impact" ) );
	EXPECT_EQ( 0, CountNaming( lines, "\"Impact.Concrete\"" ) );
	EXPECT_EQ( 0, CountNaming( lines, "bool bNoFlecks =" ) );
	EXPECT_EQ( 0, CountNaming( lines, "FLAGS_CUSTIOM_EFFECTS_NOFLECKS" ) )
		<< kCallback << " decides something about an impact again; the decision "
		   "is " << kChoice << "'s and " << kHandler << "'s";

	// The positive control on the other end: the answers exist somewhere.
	EXPECT_NE( -1, FirstNaming( CodeLines( kChoice ), "impact_concrete" ) )
		<< kChoice << " does not name the concrete particle system, so the "
		   "table did not move -- it was deleted";
	EXPECT_NE( -1, FirstNaming( CodeLines( kChoice ), "\"Impact.Concrete\"" ) )
		<< kChoice << " does not hold the decal name, so the name was deleted "
		   "rather than moved and every impact leaves no mark";
}

//-----------------------------------------------------------------------------
// ONE IMPACT IS ONE CHOICE: the decal name and the effect family are answers
// to the same six inputs, so the handler asks the policy once.
//-----------------------------------------------------------------------------
TEST( ImpactChoiceSeam, TheHandlerAsksThePolicyOncePerImpact )
{
	const std::vector< std::string > body =
		BodyOf( CodeLines( kHandler ), "public void Dispatch(in EffectData data)" );

	ASSERT_FALSE( body.empty() ) << "BulletImpactEffect.Dispatch's body was not found";

	EXPECT_EQ( 1, CountNaming( body, "ImpactChoicePolicy.Choose(" ) )
		<< "the handler asks the policy for the impact's choice exactly once";
}

} // namespace
