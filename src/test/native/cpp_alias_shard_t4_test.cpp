//========= SourceSharp — shard T4's .cpp arms, read as source ===============//
//
// Route step 5's `.cpp` half, lane T shard T4
// (docs/typedef-dryrun/route/s5_shards.json). Fourteen files, 197 armed sites,
// all of them now at zero. This file speaks for what that took, and it is
// cpp_alias_lane1_test.cpp's shape for the same reason: these files drag in the
// whole SDK prelude, so compiling them twice is not available and a stand-in for
// the prelude would drift from the real thing silently.
//
// The compile-side evidence is `src/survey_run server` / `client` with the alias
// armed — an armed arm that does not compile shows up there as a site. That is a
// measurement run by hand, not a standing test, which is exactly why the textual
// invariants below are worth pinning: they are the half that rots between
// surveys.
//
// WHAT IS LEFT HERE is the part of the shard that lives in files the build
// still compiles: the files the `residue` and `the-close` waves cleared of
// their dead arms (TheResidueWaveTookTheDeadArmsWhole,
// TheCloseWaveTookTheShimsDeadArmsWhole), and
//
//   * AN UNCONDITIONAL RETYPE, WITH NO GUARD AT ALL. Three of this shard's
//      edits are not arms: they stop naming the entity outright, because the
//      answer that survives the flip is also the right answer today. Nothing in
//      the build notices one of those rotting back to the old spelling — the old
//      spelling compiles perfectly — so they are pinned here in BOTH directions:
//      the new name must be present AND the old one must be gone. See
//      TheThreeUnconditionalRetypesHeldTheirGround.
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
// Finding the tree, and reading it. Same shape as cpp_alias_lane1_test.cpp.
//-----------------------------------------------------------------------------
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

std::string Trimmed( const std::string &s )
{
	const size_t a = s.find_first_not_of( " \t" );
	if ( a == std::string::npos )
		return std::string();
	const size_t b = s.find_last_not_of( " \t" );
	return s.substr( a, b - a + 1 );
}

std::vector< std::string > ReadLines( const std::string &strRelative )
{
	std::vector< std::string > lines;
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return lines;

	std::ifstream file( ( std::string( pszRoot ) + "/" + strRelative ).c_str() );
	if ( !file.is_open() )
		return lines;

	std::string line;
	while ( std::getline( file, line ) )
	{
		if ( !line.empty() && line[ line.size() - 1 ] == '\r' )
			line.erase( line.size() - 1 );
		lines.push_back( line );
	}
	return lines;
}

bool Contains( const std::string &hay, const char *needle )
{
	return hay.find( needle ) != std::string::npos;
}

// COMMENTS ARE STRIPPED BEFORE ANY MATCH, and this is not tidiness.
//
// Every arm in this shard explains itself, and the explanation names the very
// member the arm stopped calling — entitylist.cpp's armed regions are preceded
// by prose naming GetRefEHandle, GetFlags, IsEFlagSet and AcceptInput in the
// sentences saying none of them crosses. Matching raw text, "the armed arm must
// not name the deleted member" reads the member's name straight out of the
// sentence saying it is gone and reports a correct arm as wrong.
//
// That is not hypothetical: header_alias_test.cpp was caught doing exactly this,
// and TheResolverListIsHonest before it. In THIS shard it is load-bearing rather
// than precautionary — several armed arms carry a comment naming the member they
// replaced, and StrippingIsWhatStopsTheProseFalsePositive proves the mechanism
// on synthetic input as well.
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

// A whole file as CODE: read, joined, comments stripped. The absence claims in
// TheResidueWaveTookTheDeadArmsWhole need this, and for the reason spelled out
// above StripComments -- every arm the `residue` wave deleted left its PROSE
// behind, and several of those sentences name the very member they say is gone.
std::string CodeOf( const char *pszFile )
{
	const std::vector< std::string > lines = ReadLines( pszFile );
	std::string strAll;
	for ( size_t i = 0; i < lines.size(); i++ )
		strAll += lines[ i ] + "\n";
	return StripComments( strAll );
}

//-----------------------------------------------------------------------------
// One SS_CBASEENTITY_ALIAS region, split into arms. Both polarities, because
// the armed arm is whichever side the opening directive says it is and not
// whichever side comes second — header_alias_test.cpp found a scanner that knew
// only #ifndef reading four correct #ifdef guards as absent while staying green.
//-----------------------------------------------------------------------------
struct Guard_t
{
	std::string strFile;
	int nLine;                            // 1-based, the opening directive
	int nEndifLine;                       // 1-based, the closing #endif
	std::vector< std::string > unarmed;
	std::vector< std::string > armed;
	bool bHasElse;
	bool bClosed;

	Guard_t() : nLine( 0 ), nEndifLine( 0 ), bHasElse( false ), bClosed( false ) {}
};

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

std::vector< Guard_t > GuardsIn( const std::string &strFile )
{
	const std::vector< std::string > lines = ReadLines( strFile );
	std::vector< Guard_t > out;

	for ( size_t i = 0; i < lines.size(); i++ )
	{
		bool bArmedFirst = false;
		if ( !IsGuardOpen( lines[ i ], &bArmedFirst ) )
			continue;

		Guard_t g;
		g.strFile = strFile;
		g.nLine = (int)i + 1;

		int nDepth = 0;
		bool bInElse = false;
		size_t j = i + 1;
		for ( ; j < lines.size(); j++ )
		{
			const std::string &L = lines[ j ];
			if ( IsAnyConditionalOpen( L ) ) { nDepth++; }
			else if ( IsEndif( L ) )
			{
				if ( nDepth == 0 ) { g.bClosed = true; g.nEndifLine = (int)j + 1; break; }
				nDepth--;
			}
			else if ( IsElse( L ) && nDepth == 0 )
			{
				g.bHasElse = true; bInElse = true; continue;
			}

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
// SHAPE 4 — THE UNCONDITIONAL RETYPES.
//
// No guard at all: the spelling that survives the flip is also the right answer
// today, so there is nothing to branch on. Nothing in the build notices one of
// these rotting back — the OLD spelling compiles perfectly — so each is pinned
// in both directions.
//-----------------------------------------------------------------------------
struct Retype_t
{
	const char *pszFile;
	const char *pszMustBePresent;
	const char *pszMustBeAbsent;
};

const Retype_t kUnconditionalRetypes[] = {
	// CEffectData::entindex resolved through GetBaseEntityFromHandle, which
	// answers NULL for anything on the parallel hierarchy, and so reported -1
	// ("no entity") for an effect attached to a standalone entity. The same list
	// lookup one face up answers for both.
	//
	// THE FORBIDDEN NEEDLE IS THE WHOLE DECLARATION, not "GetBaseEntityFromHandle
	// ( m_hEntity )": CEffectData::GetEntity, eight lines above, resolves through
	// that same call and is SUPPOSED to — it returns the SDK entity, which is
	// what its callers want, and it compiles armed because the return type moves
	// with the alias. The short needle matched it and reported a correct file as
	// rotted the first time this ran. Scope a scan to what it is actually about.
	// AND THE RESOLVER UNDER IT CHANGED WITHOUT THE RETYPE CHANGING. Lane
	// `f-list` re-pointed both spellings at the port's resolvers -- the stock
	// list leaves with the client flip -- so the needles move from
	// `ClientEntityList().GetClientNetworkableFromHandle` to
	// `SourceSharp_ClientNetworkableFromHandle` and from
	// `ClientEntityList().GetBaseEntityFromHandle` to
	// `SourceSharp_ClientSdkEntityFromHandle`.
	//
	// THE SUBJECT IS UNCHANGED AND THAT IS THE POINT OF UPDATING RATHER THAN
	// DELETING. This row is about which FACE entindex() asks for -- the
	// NETWORKABLE one, because C_StandaloneEntity implements it and the SDK
	// entity spelling answers NULL for every managed entity, which is the
	// defect the retype closed. Both resolvers preserve their face exactly:
	// ...NetworkableFromHandle for the networkable, and the SDK-FILTERED
	// ...SdkEntityFromHandle for GetEntity, which still returns a C_BaseEntity *
	// and must still answer NULL rather than a widened pointer.
	//
	// The forbidden needle is still the WHOLE declaration for the reason above:
	// CEffectData::GetEntity resolves through the SDK arm and is supposed to.
	// (CEffectData's accessors moved to shell_effect_dispatch_client.cpp in C2.)
	{ "sourcesharp/game/client/shell_effect_dispatch_client.cpp",
	  "SourceSharp_ClientNetworkableFromHandle( m_hEntity )",
	  "C_BaseEntity *pEnt = SourceSharp_ClientSdkEntityFromHandle( m_hEntity );" },

	// imovementplayer.h's own mechanical rule -- "player->X() becomes
	// player->MvX()" -- applied to the one thing the recipient filter wants from
	// a player. IMovementPlayer is a base CBasePlayer declares itself, so the
	// call site does not depend on what lane U decides about that class.
	//
	// Since P5 the server's filters are ss:: types that take player SLOTS only
	// (ss_recipient_filter.h): the two pointer overloads these rows pinned are
	// gone, so the fact moves to where it now lives. The header must not grow
	// a CBasePlayer overload back, and the one caller that still holds a player
	// pointer must ask it for MvEntIndex(), not entindex().
	{ "sourcesharp/game/server/ss_recipient_filter.h",
	  "void AddRecipientByPlayerIndex( int playerindex );",
	  "AddRecipient( const CBasePlayer" },
	// shell_soundenvelope.cpp's caller is gone with the rest of the dead
	// CBasePlayer surface (plan_server_novalve.md P5 wave 8b), and since P5
	// lane SP so is the interface method that took the pointer: the controller
	// implements ss::ISoundController only, which names no player. Pinned both
	// ways: that is the class's one base, and no CBasePlayer comes back.
	{ "sourcesharp/game/server/shell_soundenvelope.cpp",
	  "class CShellSoundController : public ss::ISoundController\n",
	  "CBasePlayer" },
};

const size_t kNumRetypes = sizeof( kUnconditionalRetypes ) / sizeof( kUnconditionalRetypes[ 0 ] );

} // namespace

//-----------------------------------------------------------------------------
// THE SUCCESSOR PROPERTY FOR THE THREE FILES THE `residue` WAVE CLEARED.
//
// "Every entity ask here is inside the alias arm" is true of a file with no
// asks and no arms, so on these three it now passes VACUOUSLY. What is actually
// true of them is "THERE ARE NONE", and the two halves of that are different
// claims that a deletion can get right separately:
//
//   * A WHOLE-BODY ARM (`#ifndef` with no `#else`) is deleted correctly by
//     taking the body with the guard. The successor is an ABSENCE, so the
//     mutation that breaks it is an ADDITION -- a substring needle cannot miss
//     what is not there, but it also cannot notice what comes back unless it is
//     looking for it. That is what kGone below is for.
//
//   * A SUBSTITUTING ARM (`#ifndef` / `#else`) is deleted correctly by KEEPING
//     THE ARMED ARM. Keeping the SDK arm instead, or neither, compiles exactly
//     as cleanly in this build and is silently wrong -- and no guard is left to
//     say which way round it went. That is what kSurvivingArmedAnswers is for,
//     and it is the half this fact exists to carry.
//
// EVERY ABSENCE IS PAIRED WITH A POSITIVE OUT OF THE SAME FILE. An
// `EXPECT_FALSE( Contains(...) )` over a file that failed to open passes for
// free; this project has found forty-three checks that could not fail, several
// of exactly that shape.
//-----------------------------------------------------------------------------
TEST( CppAliasShardT4, TheResidueWaveTookTheDeadArmsWhole )
{
	struct Cleared_t
	{
		const char *pszFile;
		const char *pszStillThere;
		const char *pszGone[ 5 ];   // NULL-terminated
	};

	const Cleared_t kCleared[] = {
		// (client_thinklist.cpp's row left with the file in C8, lane c6-input.)

		// 74b071771:...:351 -- RecordMaterialParams' body, guarded from the
		// SDKEntityFromUnknown resolve to the closing brace. The function stays
		// and is still called.
		{ "game/client/toolframework_client.cpp", "ToolFramework_RecordMaterialParams",
		  { "IsToolRecording", "SDKEntityFromUnknown", NULL } },

		// 74b071771:...:171 (whole-body), :295 and :683 (substituting), plus
		// :396 and :440. What must be gone is the SDK half of each.
		{ "game/client/particles_new.cpp", "CNewParticleEffect::ReplaceWith",
		  { "OnParticleSystemDeleted", "ParticleProp", "GetRootMoveParent",
		    "pEntity->entindex()", NULL } },
	};
	const size_t kNumCleared = sizeof( kCleared ) / sizeof( kCleared[ 0 ] );

	for ( size_t i = 0; i < kNumCleared; i++ )
	{
		const std::string strCode = CodeOf( kCleared[ i ].pszFile );
		ASSERT_FALSE( strCode.empty() ) << kCleared[ i ].pszFile << " read as empty.";

		ASSERT_TRUE( Contains( strCode, kCleared[ i ].pszStillThere ) )
			<< kCleared[ i ].pszFile << " no longer contains '"
			<< kCleared[ i ].pszStillThere << "'. Either the scanner opened "
			<< "something else, or the wave took more than the dead arm -- which "
			<< "is a different and much larger claim. Either way the absences "
			<< "below would prove nothing.";

		for ( size_t n = 0; kCleared[ i ].pszGone[ n ] != NULL; n++ )
		{
			EXPECT_FALSE( Contains( strCode, kCleared[ i ].pszGone[ n ] ) )
				<< kCleared[ i ].pszFile << " names " << kCleared[ i ].pszGone[ n ]
				<< " again. It was inside a `#ifndef SS_CBASEENTITY_ALIAS` arm at "
				<< "74b071771 and was never compiled. If a real ask has come back "
				<< "it needs a real arm; if this "
				<< "is the dead text returning, this is what says so.";
		}

		EXPECT_EQ( (size_t)0, GuardsIn( kCleared[ i ].pszFile ).size() )
			<< kCleared[ i ].pszFile << " carries an alias-conditional region again.";
	}

	// THE HALF A DELETION CAN GET BACKWARDS. Two of the ten regions had an
	// `#else`; these are the answers those `#else` arms carried, and they must
	// now stand unconditionally. If either had been dropped in favour of the SDK
	// arm the build would still compile -- and would ask an IGameEntity * for
	// entindex() and GetRootMoveParent(), neither of which it has.
	struct Survivor_t { const char *pszFile; const char *pszArmedAnswer; };
	const Survivor_t kSurvivingArmedAnswers[] = {
		// 74b071771:src/game/client/particles_new.cpp:297, the `#else` of :295.
		{ "game/client/particles_new.cpp", "state.m_nObject = -1" },
		// 74b071771:src/game/client/particles_new.cpp:685, the `#else` of :683.
		{ "game/client/particles_new.cpp", "pRootMove = pEntity" },
	};
	const size_t kNumSurvivors =
		sizeof( kSurvivingArmedAnswers ) / sizeof( kSurvivingArmedAnswers[ 0 ] );

	for ( size_t i = 0; i < kNumSurvivors; i++ )
	{
		const std::string strCode = CodeOf( kSurvivingArmedAnswers[ i ].pszFile );
		ASSERT_FALSE( strCode.empty() )
			<< kSurvivingArmedAnswers[ i ].pszFile << " read as empty.";

		EXPECT_TRUE( Contains( strCode, kSurvivingArmedAnswers[ i ].pszArmedAnswer ) )
			<< kSurvivingArmedAnswers[ i ].pszFile << " no longer says '"
			<< kSurvivingArmedAnswers[ i ].pszArmedAnswer << "'. That was the "
			<< "ARMED arm of a substituting guard the `residue` wave removed, and "
			<< "it is the arm this build compiles. Losing it is not a tidy-up: it "
			<< "is the deletion having kept the wrong side.";
	}
}

//-----------------------------------------------------------------------------
// THE SAME CLAIM FOR THE FIVE SHIM FILES `the-close` 1/N (f288a5e11) CLEARED,
// AND IT IS WHERE NINE ROWS FROM THE THREE TABLES ABOVE WENT.
//
// A separate fact from the `residue` one rather than nine more entries in its
// table, because the two waves are two events and a single fact covering both
// could not say which one a regression came from. Same wave ruling: the shim is
// compiled with SS_CBASEENTITY_ALIAS and SOURCESHARP defined by both vpcs, this
// project builds no other configuration, so every `#ifndef` arm deleted was the
// arm the preprocessor already threw away -- 588 objects before and after,
// identical DEFINED and UNDEFINED symbol sets, 0 differing.
//
// EVERY ROW IS A PAIR, and the pair is the whole design. "The member is gone"
// alone passes over an emptied file, a renamed file, a file that failed to
// open, and -- worst -- a deletion that kept the SDK arm and threw away the
// ARMED one, which compiles and is wrong. So each row also names the answer the
// deleted arm was choosing AGAINST, and that answer must still be the code.
//-----------------------------------------------------------------------------
TEST( CppAliasShardT4, TheCloseWaveTookTheShimsDeadArmsWhole )
{
	struct ClosedSite_t
	{
		const char *pszFile;
		const char *pszGone;          // the spelling the deleted arm carried
		const char *pszArmedSurvives; // the answer it was choosing against
		const char *pszWasRow;        // which table above the row came from
	};

	const ClosedSite_t kSites[] = {
		// shell_recipientfilter.cpp's substituting row (an unlocatable source
		// reaching nobody through the default CPASFilter()) WENT IN THE SERVER
		// GC CULL (2026-10-01) with the entity-taking CPASAttenuationFilter
		// constructors it lived in: nothing constructed one.

		// WAS A SUBSTITUTING ROW, twice. variant_t's two entity-name reads.
		// StandaloneEntity_Name answers for the hierarchy that exists.
		{ "sourcesharp/game/server/shell_globals.cpp",
		  "GetEntityName", "StandaloneEntity_Name( Entity() )", "substituting" },
		{ "sourcesharp/game/server/shell_globals.cpp",
		  // ss::variant_t (P5 lane E) holds a CBaseHandle, so the name is read
		  // through Entity(), the typed handle's old Get, rather than eVal.
		  "found.pBase", "MAKE_STRING( StandaloneEntity_Name( Entity() ) )", "substituting" },

		// shell_physicsobjects.cpp's two substituting rows (SourceSharp_
		// EntityInitPhysics' SDK arm) WENT WITH THE FUNCTION (2026-10-12): an
		// entity's physics object is managed's EntityPhysicsCreation, and the
		// file keeps no arm to choose between.

		// WAS A WHOLE-BODY ROW. The one ask that existed only in an armed
		// survey; the standalone arm is what actually marks the state. It used
		// to be counted as well, by a `g_nSourceSharpStateChangedCalls` in
		// shell_standalone_base.cpp; that counter went with plan_shim.md §4a
		// item 10 once cs_sv_dirty_stats -- the only thing that ever read it --
		// left. The row below is what pins the ask itself, and it is unchanged
		// by the counter's going.
		{ "sourcesharp/game/server/shell_entityslots.cpp",
		  "ent.pBase->NetworkStateChanged()",
		  "ent.pStandalone->NetworkStateChanged();", "whole-body" },

		// WAS AN ELSE-SPLIT ROW. DispatchParticleEffect's entity branch. The
		// `else` this guard closed on is not empty -- it CLEARS the entity
		// field -- so losing it would leave m_nEntIndex / m_hEntity at whatever
		// CEffectData was constructed with rather than at the explicit "no
		// entity" this function has always written. That is the half a
		// deletion can get backwards, and it is what the presence column pins.
		{ "sourcesharp/game/shared/particle_manifest.cpp",
		  "data.m_nDamageType = PATTACH_CUSTOMORIGIN",
		  "data.m_nEntIndex = 0;", "else-split" },

		// ...AND THE OTHER SIDE OF THE SAME BLOCK, because the block is two
		// lines under a CLIENT_DLL split and a check that read only one of them
		// would be measuring one side of a shared file. R2's hazard exactly.
		{ "sourcesharp/game/shared/particle_manifest.cpp",
		  "data.m_fFlags |= PARTICLE_DISPATCH_FROM_ENTITY",
		  "data.m_hEntity = NULL;", "else-split" },
	};
	const size_t kNumSites = sizeof( kSites ) / sizeof( kSites[ 0 ] );

	for ( size_t i = 0; i < kNumSites; i++ )
	{
		const ClosedSite_t &c = kSites[ i ];
		SCOPED_TRACE( std::string( c.pszFile ) + " (" + c.pszWasRow + " row: " + c.pszGone + ")" );

		const std::string strCode = CodeOf( c.pszFile );
		ASSERT_FALSE( strCode.empty() ) << c.pszFile << " read as empty.";

		// PRESENCE FIRST, deliberately: it is what makes the absence below a
		// statement about this file rather than about a failed read.
		EXPECT_TRUE( Contains( strCode, c.pszArmedSurvives ) )
			<< c.pszFile << " no longer says '" << c.pszArmedSurvives
			<< "'. That is the answer the deleted arm was choosing AGAINST, and "
			<< "it is the arm this build compiles -- losing it is not a tidy-up, "
			<< "it is the deletion having kept the wrong side.";

		EXPECT_FALSE( Contains( strCode, c.pszGone ) )
			<< c.pszFile << " names '" << c.pszGone << "' again. It was inside a "
			<< "`#ifndef SS_CBASEENTITY_ALIAS` arm and was never compiled. If a "
			<< "real ask has come back it needs a real arm.";

		EXPECT_EQ( (size_t)0, GuardsIn( c.pszFile ).size() )
			<< c.pszFile << " carries an alias-conditional region again.";
	}

	// THE TABLE IS EIGHT ROWS. It was nine -- five substituting, one whole-body,
	// two else-split, plus the second half of the particle block. The ninth,
	// cs_sv_dirty_stats' classname pick in shell_entityslots.cpp, is gone with
	// the whole command: the readout had no reader, so the arm this row pinned
	// as surviving (`pName = ent.pStandalone->GetClassname();`) does not exist
	// to survive, and the row would assert the absence of a line in a function
	// that is not there. The OTHER shell_entityslots.cpp row, over
	// SourceSharp_MarkStateChanged's `ent.pBase->NetworkStateChanged()`, is
	// unaffected and stays. Pinned so that a row quietly dropped from here is a
	// failure rather than a shorter loop.
	//
	// SEVEN SINCE THE SERVER GC CULL (2026-10-01): shell_recipientfilter.cpp's
	// substituting row went with the CPASAttenuationFilter constructors it was in.
	// FIVE SINCE THE ENTITY PHYSICS INIT WENT MANAGED (2026-10-12):
	// shell_physicsobjects.cpp's two rows went with SourceSharp_EntityInitPhysics.
	EXPECT_EQ( (size_t)5, kNumSites );
}


//-----------------------------------------------------------------------------
// THE THREE UNCONDITIONAL RETYPES HELD THEIR GROUND.
//
// Both directions, because only one of them can fail loudly: the NEW spelling
// disappearing would be caught by an armed survey, but the OLD spelling coming
// BACK compiles clean unarmed and armed alike — MvEntIndex and entindex are both
// real, GetClientNetworkableFromHandle and GetBaseEntityFromHandle are both
// real. Nothing in the build notices, which is precisely why this is here.
//-----------------------------------------------------------------------------
TEST( CppAliasShardT4, TheThreeUnconditionalRetypesHeldTheirGround )
{
	for ( size_t i = 0; i < kNumRetypes; i++ )
	{
		const Retype_t &r = kUnconditionalRetypes[ i ];
		const std::vector< std::string > lines = ReadLines( r.pszFile );
		ASSERT_FALSE( lines.empty() ) << r.pszFile << " read as zero lines";

		std::string strAll;
		for ( size_t n = 0; n < lines.size(); n++ ) strAll += lines[ n ] + "\n";
		const std::string strCode = StripComments( strAll );

		EXPECT_TRUE( Contains( strCode, r.pszMustBePresent ) )
			<< r.pszFile << " no longer contains the retyped spelling '"
			<< r.pszMustBePresent << "'";

		EXPECT_FALSE( Contains( strCode, r.pszMustBeAbsent ) )
			<< r.pszFile << " has gone back to '" << r.pszMustBeAbsent
			<< "', which compiles clean and silently answers NULL for an entity "
			   "on the parallel hierarchy";
	}
}

//-----------------------------------------------------------------------------
// SCANNER SELF-TESTS.
//
// header_alias_test.cpp's scanner has been wrong twice — once matching raw text
// through a comment, once blind to #ifdef polarity — and both times the rules
// above it stayed green. These exist so this scanner's own faults are caught by
// something other than luck.
//-----------------------------------------------------------------------------
TEST( CppAliasShardT4Scanner, StripCommentsRemovesBothCommentKinds )
{
	EXPECT_EQ( StripComments( "a // GetRefEHandle\nb\n" ), "a \nb\n" );
	EXPECT_EQ( StripComments( "a /* GetRefEHandle */ b" ), "a  b" );
	EXPECT_FALSE( Contains( StripComments( "/*\n GetFlags(\n*/\nx();\n" ), "GetFlags(" ) );
	// ...and it must not eat code.
	EXPECT_TRUE( Contains( StripComments( "/* gone */ GetFlags( a );\n" ), "GetFlags(" ) );
}

TEST( CppAliasShardT4Scanner, StrippingIsWhatStopsTheProseFalsePositive )
{
	// The failure reproduced on SYNTHETIC input rather than on the tree: an
	// armed arm that is CORRECT — it does not call the member — but whose
	// comment explains which member it stopped calling. Judged on raw text, the
	// rule "the armed arm must not name the deleted member" fires and a correct
	// arm reads as broken.
	const std::string strArmedArm =
		"\t// GetRefEHandle is IHandleEntity's, so this does nothing.\n"
		"\treturn;\n";

	EXPECT_TRUE( Contains( strArmedArm, "GetRefEHandle" ) )
		<< "the synthetic arm no longer reproduces the false positive";
	EXPECT_FALSE( Contains( StripComments( strArmedArm ), "GetRefEHandle" ) )
		<< "stripping failed to remove the prose mention — the rule above would "
		   "report this correct arm as broken";

	// The converse, which is what stops the stripping from being TOO greedy: a
	// genuinely broken armed arm — one that still CALLS the member — must
	// survive stripping and be caught. Without this, StripComments could return
	// the empty string and every rule above would pass vacuously.
	const std::string strBrokenArm =
		"\t// GetRefEHandle is IHandleEntity's.\n"
		"\tSetPlayerSurface( pPlayer->GetRefEHandle(), gameMaterial );\n";

	EXPECT_TRUE( Contains( StripComments( strBrokenArm ), "GetRefEHandle" ) )
		<< "stripping ate the code as well as the comment — the rule above would "
		   "then be unable to fail";
}

TEST( CppAliasShardT4Scanner, BothGuardPolaritiesAreRecognised )
{
	bool bArmedFirst = true;
	EXPECT_TRUE( IsGuardOpen( "#ifndef SS_CBASEENTITY_ALIAS", &bArmedFirst ) );
	EXPECT_FALSE( bArmedFirst );

	EXPECT_TRUE( IsGuardOpen( "#ifdef SS_CBASEENTITY_ALIAS", &bArmedFirst ) );
	EXPECT_TRUE( bArmedFirst );

	EXPECT_TRUE( IsGuardOpen( "\t#if !defined( SS_CBASEENTITY_ALIAS )", &bArmedFirst ) );
	EXPECT_FALSE( bArmedFirst );

	EXPECT_FALSE( IsGuardOpen( "#ifndef SOMETHING_ELSE", &bArmedFirst ) );
}
