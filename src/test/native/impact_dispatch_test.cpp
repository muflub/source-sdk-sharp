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
// PROVEN RED, each independently. Delete the DECLARE_CLIENT_EFFECT line and
// SomethingClaimsTheImpactName fails. Spell the callback's resolve back to
// `C_BaseEntity *pEntity = ParseImpactData(...)` and both
// TheCallbackResolvesTheEntityWidely and TheNarrowResolveIsBanned fail. Drop
// the ImpactEffect call from FireBullets and TheShotDispatchesAnImpact fails.
// Move that call inside the `_entities.IsValid(trace.Entity)` block and
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
const char *kArsenal  = "managed/SourceSharp.Server/Arsenal.cs";
const char *kAbi      = "host/sourcesharp.h";
const char *kTrace    = "game/server/shell_worldquery.cpp";

//-----------------------------------------------------------------------------
// THE CHECK THAT WOULD OTHERWISE NOT BE ABLE TO FAIL.
//
// Two of the tests below assert that something is ABSENT, and a file that could
// not be read is absent in the same way -- so a rename, a move, or a working
// directory nobody expected would turn them green and mean nothing. Every file
// is asserted readable first, and every ban's ANCHOR is asserted present, so
// "no hits" means "not spelled" rather than "looking at nothing".
//-----------------------------------------------------------------------------
TEST( ImpactDispatch, TheFilesTheseTestsAreAboutWereActuallyRead )
{
	ASSERT_TRUE( SourceRoot() != NULL )
		<< "no src/sourcesharp/ from here, so every scan below is vacuous";

	ASSERT_FALSE( CodeLines( kCallback ).empty() ) << kCallback;
	ASSERT_FALSE( CodeLines( kArsenal ).empty() )  << kArsenal;
	ASSERT_FALSE( CodeLines( kAbi ).empty() )      << kAbi;
	ASSERT_FALSE( CodeLines( kTrace ).empty() )    << kTrace;

	EXPECT_NE( -1, FirstNaming( CodeLines( kCallback ), "SourceSharp_ImpactCallback" ) )
		<< kCallback << " no longer has a callback at all, so the bans on how "
		   "it resolves its entity are looking at nothing";
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
// spelling would close it by hiding it.
//-----------------------------------------------------------------------------
TEST( ImpactDispatch, SomethingClaimsTheImpactName )
{
	const std::vector< std::string > lines = CodeLines( kCallback );

	EXPECT_EQ( 1, CountNaming( lines, "DECLARE_CLIENT_EFFECT( \"Impact\"," ) )
		<< "exactly one registration of the dispatch name \"Impact\" in "
		<< kCallback << ". Without it DispatchEffectToCallback walks a registry "
		   "with no such entry and prints \"effect 'Impact' not found on "
		   "client\" -- which is what this build did for its whole life.";
}

//-----------------------------------------------------------------------------
// AND IT RESOLVES THE ENTITY THE WAY THIS CLIENT CAN ANSWER.
//-----------------------------------------------------------------------------
TEST( ImpactDispatch, TheCallbackResolvesTheEntityWidely )
{
	const std::vector< std::string > lines = CodeLines( kCallback );

	EXPECT_NE( -1, FirstNaming( lines, "data.entindex()" ) )
		<< "the callback must take the entity from CEffectData::entindex(), "
		   "which goes through IClientNetworkable and answers for BOTH "
		   "hierarchies";
	EXPECT_NE( -1, FirstNaming( lines, "SourceSharp_ClientEntityFromIndex" ) )
		<< "and resolve that index through the lookup that answers for both, "
		   "which hands back an IClientEntity rather than a C_BaseEntity";
}

//-----------------------------------------------------------------------------
// THE NARROW RESOLVE IS BANNED, and it is banned by the spelling stock uses --
// because stock's spelling is exactly the one that was measured answering NULL
// for worldspawn on this client. A callback that took it would compile, run,
// play a sound, and draw nothing, for every shot.
//-----------------------------------------------------------------------------
TEST( ImpactDispatch, TheNarrowResolveIsBanned )
{
	const std::vector< std::string > lines = CodeLines( kCallback );

	EXPECT_EQ( 0, CountNaming( lines, "C_BaseEntity *pEntity = ParseImpactData" ) )
		<< "ParseImpactData's return value is GetEntity(), which filters to "
		   "GetGameEntity() != NULL and is NULL for every entity on this "
		   "client. Binding it is the shape that makes the callback silent.";
	EXPECT_EQ( 0, CountNaming( lines, "data.GetEntity()" ) )
		<< "and the same ask spelled directly";
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
// AND THE CHOICE ABOVE IT IS MANAGED (plan_shim.md §6, lane 4b-effects).
//
// Everything above is about an impact HAPPENING. These are about who decides
// what it looks like: which effect family the surface gets, which particle
// system the new-effects table names for it, whether flecks are drawn, and
// which decal is left. All four were literals in shell_impact_effects.cpp --
// a `switch`, a twenty-six row array, a two-line boolean written twice, and a
// string -- and all four are in SourceSharp.Client/TempEnts/ImpactChoice.cs.
//
// A SOURCE SCAN FOR THE SAME REASON AS EVERY TEST ABOVE: this is a shape at a
// call site, in code that cannot run without a particle manager and a decal
// system. What the DECISION answers is asked from managed code instead --
// ImpactChoiceTests has thirty-nine facts on it, each mutation-verified -- and
// what the LIVE half does is tools/fx-impact-gate's. These are what stop the
// seam being undone by an edit that still compiles.
//
// PROVEN RED, each independently, by putting a piece back:
//   * a second SourceSharp_ImpactChoice call in the callback fails
//     TheCallbackAsksManagedOncePerImpact;
//   * `iMaterial == CHAR_TEX_CONCRETE` anywhere in the file fails
//     TheMaterialChainIsGoneFromTheShim;
//   * the literal "impact_concrete" fails TheParticleNameTableIsGoneFromTheShim;
//   * the literal "Impact.Concrete" fails TheDecalNameIsGoneFromTheShim;
//   * dropping `bChose &&` from the callback's guard fails
//     ARefusedChoiceDrawsNothing.
//=============================================================================

const char *kChoice = "managed/SourceSharp.Client/TempEnts/ImpactChoice.cs";

//-----------------------------------------------------------------------------
// THE CHECK THAT WOULD OTHERWISE NOT BE ABLE TO FAIL, again: four of the five
// below assert an ABSENCE, and an unreadable file is absent the same way.
//-----------------------------------------------------------------------------
TEST( ImpactChoiceSeam, TheFilesTheseTestsAreAboutWereActuallyRead )
{
	ASSERT_TRUE( SourceRoot() != NULL );
	ASSERT_FALSE( CodeLines( kCallback ).empty() ) << kCallback;
	ASSERT_FALSE( CodeLines( kChoice ).empty() )   << kChoice;

	// The ANCHOR for every ban: the seam itself. If this is gone the file has
	// stopped asking at all, and "the literals are absent" would be true of a
	// build that simply does not draw impacts.
	EXPECT_NE( -1, FirstNaming( CodeLines( kCallback ), "SourceSharp_ImpactChoice" ) )
		<< kCallback << " no longer asks managed code anything about an impact, "
		   "so every ban below is satisfied by a file that does nothing";

	// And the POSITIVE control on the other end: the answers exist somewhere.
	EXPECT_NE( -1, FirstNaming( CodeLines( kChoice ), "impact_concrete" ) )
		<< kChoice << " does not name the concrete particle system, so the "
		   "table did not move -- it was deleted";
}

//-----------------------------------------------------------------------------
// ONE IMPACT IS ONE CROSSING.
//
// The callback wants the decal name AND the effect family, both answers to the
// same six inputs. Asking twice would be two crossings per bullet for one
// decision, which is the shape SS_CHOICE_IN_* exists to avoid.
//-----------------------------------------------------------------------------
TEST( ImpactChoiceSeam, TheCallbackAsksManagedOncePerImpact )
{
	const std::vector< std::string > body =
		BodyOf( CodeLines( kCallback ), "void SourceSharp_ImpactCallback(" );

	ASSERT_FALSE( body.empty() ) << "SourceSharp_ImpactCallback's body was not found";

	EXPECT_EQ( 1, CountNaming( body, "SourceSharp_ImpactChoice(" ) )
		<< "the callback asks managed code for the impact's choice exactly "
		   "once. Two calls is two crossings per bullet for one decision made "
		   "from one set of numbers.";

	EXPECT_EQ( 0, CountNaming( body, "GetImpactDecal(" ) )
		<< "the callback reaches the decal through the choice it already has. "
		   "GetImpactDecal asks for itself, so calling it here is the second "
		   "crossing this test exists to prevent.";
}

//-----------------------------------------------------------------------------
// AND WHAT IT DOES WITH A REFUSAL IS STOCK'S FALL-THROUGH.
//-----------------------------------------------------------------------------
TEST( ImpactChoiceSeam, ARefusedChoiceDrawsNothing )
{
	const std::vector< std::string > body =
		BodyOf( CodeLines( kCallback ), "void SourceSharp_ImpactCallback(" );

	ASSERT_FALSE( body.empty() );

	EXPECT_NE( -1, FirstNaming( body, "if ( bChose && tr.DidHit() )" ) )
		<< "the custom effect must be gated on the choice having been ANSWERED "
		   "as well as on the trace having hit: an unanswered choice carries "
		   "action NOTHING, and acting on it would be acting on a default "
		   "rather than on a decision.";
}

//-----------------------------------------------------------------------------
// THE CHAIN IS GONE FROM C++.
//
// Not "mostly gone": the material comparison is the decision, and one arm left
// behind is one surface whose effect two languages disagree about.
//-----------------------------------------------------------------------------
TEST( ImpactChoiceSeam, TheMaterialChainIsGoneFromTheShim )
{
	const std::vector< std::string > lines = CodeLines( kCallback );

	EXPECT_EQ( 0, CountNaming( lines, "CHAR_TEX_" ) )
		<< kCallback << " still compares a surface material against a "
		   "CHAR_TEX_* name. That comparison IS the choice this move took to "
		   "managed code, and an arm left here is a surface the two sides "
		   "answer differently for.";
}

//-----------------------------------------------------------------------------
// SO IS THE TWENTY-SIX ROW TABLE.
//-----------------------------------------------------------------------------
TEST( ImpactChoiceSeam, TheParticleNameTableIsGoneFromTheShim )
{
	const std::vector< std::string > lines = CodeLines( kCallback );

	// One name per shape of row: a plain one, a no-flecks variant, and the
	// warp shield, which is the only name the CHAIN also carried.
	EXPECT_EQ( 0, CountNaming( lines, "impact_concrete" ) );
	EXPECT_EQ( 0, CountNaming( lines, "impact_concrete_noflecks" ) );
	EXPECT_EQ( 0, CountNaming( lines, "warp_shield_impact" ) )
		<< kCallback << " still names a particle system for a surface. The "
		   "table moved to " << kChoice << "; a name left here is a row the "
		   "two sides can disagree about.";
}

//-----------------------------------------------------------------------------
// AND SO IS THE DECAL NAME.
//-----------------------------------------------------------------------------
TEST( ImpactChoiceSeam, TheDecalNameIsGoneFromTheShim )
{
	const std::vector< std::string > lines = CodeLines( kCallback );

	EXPECT_EQ( 0, CountNaming( lines, "\"Impact.Concrete\"" ) )
		<< kCallback << " still spells the decal name. It is "
		   "ImpactChoicePolicy.DefaultDecal now, and a copy here would be a "
		   "second answer nothing chooses between.";

	EXPECT_NE( -1, FirstNaming( CodeLines( kChoice ), "\"Impact.Concrete\"" ) )
		<< kChoice << " does not hold the decal name, so the name was deleted "
		   "rather than moved and every impact leaves no mark";
}

//-----------------------------------------------------------------------------
// THE FLECK SUPPRESSION IS DECIDED ONCE.
//
// Stock computed `bNoFlecks` in BOTH PerformCustomEffects and
// PerformNewCustomEffects, from the same two inputs, in the same four lines.
// Two copies of one rule is a rule that can be half-changed.
//-----------------------------------------------------------------------------
TEST( ImpactChoiceSeam, TheFleckSuppressionIsNotRecomputedNatively )
{
	const std::vector< std::string > lines = CodeLines( kCallback );

	// The needle is the COMPUTATION, not the word: `bNoFlecks` is still a
	// parameter name on FX_DebrisFlecks and a field on the choice struct, and
	// banning the identifier would ban carrying the answer as well as making
	// it. Stock's two copies were each
	//     bool bNoFlecks = !r_drawflecks.GetBool();
	//     if ( !bNoFlecks ) bNoFlecks = ( nFlags & FLAGS_CUSTIOM_EFFECTS_NOFLECKS ) != 0;
	// so those are the two spellings that may not come back.
	EXPECT_EQ( 0, CountNaming( lines, "bool bNoFlecks =" ) )
		<< kCallback << " still computes the fleck suppression. Managed code "
		   "answers it in SS_CHOICE_OUT_NOFLECKS, and a native copy beside the "
		   "managed one is exactly the divergence this move removes.";

	EXPECT_EQ( 0, CountNaming( lines, "FLAGS_CUSTIOM_EFFECTS_NOFLECKS" ) )
		<< kCallback << " still tests the caller's no-flecks flag. That test is "
		   "half of the suppression rule, and half a rule in each language is "
		   "worse than the whole of it in one.";

	// AND THE ANSWER IS STILL CARRIED, which the two bans above cannot show:
	// a file that stopped passing the flag to the emitter would satisfy both.
	EXPECT_NE( -1, FirstNaming( lines, "choice.bNoFlecks" ) )
		<< kCallback << " never passes the managed answer to an emitter, so "
		   "the suppression was deleted rather than moved";

	// r_drawflecks is still READ here -- it is one of the six inputs, and that
	// read is a load into the engine's own ConVar rather than a decision.
	EXPECT_NE( -1, FirstNaming( lines, "r_drawflecks.GetInt()" ) )
		<< "r_drawflecks must still be read natively and handed up as an "
		   "input: a managed read of it would be a second crossing per impact "
		   "for a value this side already has";
}

} // namespace
