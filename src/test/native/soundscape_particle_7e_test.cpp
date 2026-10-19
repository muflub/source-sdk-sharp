//========= SourceSharp — step 7e's subjects, checked in the source ===========//
//
// plan_entity.md step 7e moved the client's sound cluster under the shim and
// de-entitied it. Three of the things it changed are INVISIBLE TO THE COMPILER
// and to a run that does not happen to touch them, which is exactly the class
// of change this project keeps losing:
//
//  1. THE VIEW ACCESSOR. CurrentViewOrigin/Forward/Right assert
//     s_bCanAccessCurrentView, which is only true INSIDE the render pass, and
//     every read in the soundscape file is outside it. `playsoundscape labs_mp`
//     took the game down with three of those asserts on this step's FIRST gate
//     run. Nothing about the fix is visible in a build: both spellings compile,
//     both link, and the wrong one is a crash only when somebody forces a
//     soundscape. So the spelling is a test.
//
//  2. THE ENTITY ASKS. The census reads a stock TU's TEXT, and a shim TU's text
//     is not counted the same way -- so a C_BasePlayer ask coming back into the
//     moved file would not move a census number. It is asserted here instead,
//     comment-stripped, because the file's own prose explains at length what
//     used to be there.
//
//  3. THE LANDING SITE AND ITS CALLER. Soundscape_Update was the function a
//     soundscape push landed on. It had NO CALLER at all when 7e wrote this
//     suite -- that was 7e's own open gate -- and it was KEPT deliberately so
//     that wiring the transport would be a caller and not a rewrite. Step
//     8e-sound wired it, and 7e's printed refusal was deleted rather than
//     re-conditioned. Sound cluster 17 (lane c2-sims) moved the system to C#:
//     the landing site is ClientSoundscapes.Push and its caller the client
//     module's SoundscapeMessage sink, with no crossing between them. Both
//     halves are asserted, because either alone is weak.
//
// AND ONE ABOUT THE INSTRUMENT. cs_cl_particle_report must not read a control
// point through GetControlPointAtCurrentTime: that accessor asserts
// `nControlPoint <= GetHighestControlPoint()` and
// `m_pDef->ReadsControlPoint( n )`, which is the right contract for an OPERATOR
// and the wrong one for a REPORT whose job is to say what an UNDECLARED point
// holds. env_fire_small declares point 0 only and the first version of the
// command fired particles.h:2392 twice.
//
// Source-text facts, so no engine and no game.
//
//=============================================================================//
#include "gtest/gtest.h"
#include "cmake_graph.h"

#include <sys/stat.h>

#include <set>
#include <fstream>
#include <sstream>
#include <string>

namespace
{

const char *SrcRoot()
{
	static const char *kCandidates[] = { ".", "src", "../src" };
	for ( size_t i = 0; i < sizeof( kCandidates ) / sizeof( kCandidates[ 0 ] ); i++ )
	{
		struct stat st;
		const std::string game = std::string( kCandidates[ i ] ) + "/game";
		const std::string pub  = std::string( kCandidates[ i ] ) + "/public";
		if ( stat( game.c_str(), &st ) == 0 && S_ISDIR( st.st_mode )
			&& stat( pub.c_str(), &st ) == 0 && S_ISDIR( st.st_mode ) )
		{
			return kCandidates[ i ];
		}
	}
	return NULL;
}

std::string ReadFile( const std::string &strRelative )
{
	const char *pszRoot = SrcRoot();
	if ( !pszRoot )
		return std::string();

	std::ifstream file( ( std::string( pszRoot ) + "/" + strRelative ).c_str() );
	if ( !file.is_open() )
		return std::string();

	std::stringstream ss;
	ss << file.rdbuf();
	return ss.str();
}

//-----------------------------------------------------------------------------
// The file with its comments taken out.
//
// EVERY "IS IT GONE" FACT NEEDS THIS. The moved files carry long notes naming
// the spellings that left and why, so a raw search for `CurrentViewOrigin`
// finds THE SENTENCE SAYING IT WENT and fails -- the recorded needle trap, met
// twice already on this plan (7b's C_BaseTempEntity, 6d's hitbox). String
// literals are preserved so a "//" inside a path is not read as a comment.
//-----------------------------------------------------------------------------
std::string StripComments( const std::string &strIn )
{
	std::string out;
	out.reserve( strIn.size() );

	enum { CODE, LINE_COMMENT, BLOCK_COMMENT, STRING, CHARLIT } state = CODE;

	for ( size_t i = 0; i < strIn.size(); i++ )
	{
		const char c = strIn[ i ];
		const char n = ( i + 1 < strIn.size() ) ? strIn[ i + 1 ] : '\0';

		switch ( state )
		{
		case CODE:
			if ( c == '/' && n == '/' ) { state = LINE_COMMENT; i++; }
			else if ( c == '/' && n == '*' ) { state = BLOCK_COMMENT; i++; }
			else
			{
				if ( c == '"' ) state = STRING;
				else if ( c == '\'' ) state = CHARLIT;
				out += c;
			}
			break;

		case LINE_COMMENT:
			if ( c == '\n' ) { state = CODE; out += c; }
			break;

		case BLOCK_COMMENT:
			if ( c == '*' && n == '/' ) { state = CODE; i++; }
			else if ( c == '\n' ) out += c;
			break;

		case STRING:
			out += c;
			if ( c == '\\' && i + 1 < strIn.size() ) { out += n; i++; }
			else if ( c == '"' ) state = CODE;
			break;

		case CHARLIT:
			out += c;
			if ( c == '\\' && i + 1 < strIn.size() ) { out += n; i++; }
			else if ( c == '\'' ) state = CODE;
			break;
		}
	}

	return out;
}

bool Has( const std::string &hay, const char *needle )
{
	return hay.find( needle ) != std::string::npos;
}

// The soundscape's native half (the client-system shell and the executors)
// and its managed system and commands, since sound cluster 17 (lane c2-sims).
const char *kSoundscape = "sourcesharp/game/client/shell_soundscape_audio_client.cpp";
const char *kSoundscapeManaged = "sourcesharp/managed/SourceSharp.Client/Soundscapes/ClientSoundscapes.cs";
const char *kSoundscapeCommands = "sourcesharp/managed/SourceSharp.Client/Soundscapes/SoundscapeCommands.cs";
const char *kClientModule = "sourcesharp/managed/SourceSharp.Client/ClientModule.cs";
// The client's emitter, managed since sound_emitter.cpp left client.so
// (SourceSharp.Client.Sounds); its speaker lookup is ClientModule's.
const char *kEmitter    = "sourcesharp/managed/SourceSharp.Client/Sounds/ClientSoundEmitter.cs";
const char *kFilter     = "sourcesharp/game/client/shell_recipientfilter_client.cpp";
// cs_cl_particle_report, managed since particlemgr.cpp left client.so (C7
// particles phase 4), over the particles shell's per-system read and the
// client entity origin read (shell_te_pool_client.cpp).
const char *kParticleCommands = "sourcesharp/managed/SourceSharp.Client/Effects/ParticleCommands.cs";
const char *kTePool = "sourcesharp/game/client/shell_te_pool_client.cpp";
// The managed particle systems' start and follow sweep (the flame's control
// points), in the particles shell since C5 moved them out of shell_sprite_render.cpp.
const char *kManagedParticles = "sourcesharp/game/client/shell_managed_particles.cpp";
// What each side compiles: client_sourcesharp (src/client/native/CMakeLists.txt)
// and server_sourcesharp (src/server/native/CMakeLists.txt), read through
// cmake_graph.h, whose readers throw on a missing, empty or stale input.
const char *kProofOfRead = "sourcesharp/game/shared/shell_shared.cpp";

// Does the target compile a source whose farm-relative path ends in strTail?
// By the TAIL because the old vpc needles were substrings of the row: a copy
// of the file under any prefix counts. The proof-of-read is asserted first, so
// an absence is never answered by a reader that saw nothing.
bool CompilesTail( const char *pszTarget, const std::string &strTail )
{
	const std::set< std::string > src = cmake_graph::CompiledSources( pszTarget );
	EXPECT_TRUE( src.count( kProofOfRead ) > 0 )
		<< pszTarget << " does not compile " << kProofOfRead
		<< " -- the reader is not reading its source list";
	for ( std::set< std::string >::const_iterator it = src.begin(); it != src.end(); ++it )
	{
		if ( it->size() >= strTail.size()
			&& it->compare( it->size() - strTail.size(), strTail.size(), strTail ) == 0 )
			return true;
	}
	return false;
}

std::string Code( const char *pszFile )
{
	return StripComments( ReadFile( pszFile ) );
}

} // namespace

//=============================================================================
// The four files exist where the source lists say they do.
//
// FIRST, because every fact below is vacuous over an empty string: ReadFile
// answers "" for a path it cannot open, and a `find` on "" is npos, so a
// "spelling is gone" check passes perfectly over a file that is not there. That
// is the recorded "check that cannot fail" in its most ordinary shape.
//=============================================================================
TEST( Step7eSubjects, EveryFileThisSuiteReadsIsActuallyThere )
{
	// A FLOOR PLUS AN ANCHOR, not a floor. The floors here were 20,000 / 3,000 /
	// 4,000 bytes, and a size is the wrong instrument twice over. It cannot tell
	// a truncated file from a SHORTENED one -- and two of these five subjects
	// were stock (particlemgr.cpp, client_csharp.vpc) in a port whose whole job
	// is to shorten stock, while the other three are shim files this port keeps
	// splitting up. The identical shape has gone stale ten times across this
	// suite already (9326017e6, 05237b9c8, c1420d0ac), every time on a correct
	// tree and every time at the cost of a full suite run.
	//
	// Each floor drops to a value only an UNOPENED or TRUNCATED read can miss --
	// ReadFile answers "" for a path it cannot open, which is what this fact
	// exists to catch -- and each subject is identified by something in its own
	// text, which is what the size was standing in for and could never do.
	struct Subject_t { const char *pszPath; size_t nFloor; const char *pszAnchor; };
	static const Subject_t kSubjects[] = {
		{ kSoundscape,  500u, "class C_SoundscapeSystem" },
		{ kSoundscapeManaged, 500u, "public sealed class ClientSoundscapes" },
		{ kSoundscapeCommands, 500u, "cs_cl_soundscape_report" },
		{ kClientModule, 500u, "public sealed class ClientModule" },
		{ kEmitter,     500u, "public sealed class ClientSoundEmitter" },
		{ kFilter,      500u, "C_RecipientFilter::" },
		{ kParticleCommands, 500u, "cs_cl_particle_report" },
		{ kTePool, 500u, "int32_t SourceSharp_ClientEntityOrigin(" },
		{ kManagedParticles, 500u, "void ShellParticles_PreRender()" },
		// (client_csharp.vpc was the fifth subject. Its successor, the client's
		// CMake source list, is read by cmake_graph.h, which throws on a
		// missing or empty file itself -- see section 6.)
	};

	for ( size_t i = 0; i < sizeof( kSubjects ) / sizeof( kSubjects[ 0 ] ); i++ )
	{
		const std::string text = ReadFile( kSubjects[ i ].pszPath );

		EXPECT_GT( text.size(), kSubjects[ i ].nFloor )
			<< kSubjects[ i ].pszPath << " read as empty or trivial -- every "
			   "`find` below it would be npos, which is what a "
			   "\"the spelling is gone\" check reports as a pass";
		EXPECT_NE( text.find( kSubjects[ i ].pszAnchor ), std::string::npos )
			<< kSubjects[ i ].pszPath << " does not contain `"
			<< kSubjects[ i ].pszAnchor << "`, so what was read is not the file "
			   "this suite means to be reading.";
	}
}

//=============================================================================
// 1. THE VIEW ACCESSOR
//=============================================================================
TEST( Step7eSubjects, TheSoundscapeNeverReadsTheCURRENTView )
{
	const std::string code = Code( kSoundscape );
	ASSERT_FALSE( code.empty() );

	EXPECT_FALSE( Has( code, "CurrentViewOrigin()" ) )
		<< "CurrentViewOrigin() asserts s_bCanAccessCurrentView, which is only "
		   "true inside the render pass. Soundscape work runs from a client "
		   "system update and from console commands, so this is a crash: "
		   "`playsoundscape labs_mp` took the game down with three of them.";
	EXPECT_FALSE( Has( code, "CurrentViewForward()" ) );
	EXPECT_FALSE( Has( code, "CurrentViewRight()" ) );
}

TEST( Step7eSubjects, TheSoundscapeReadsTheMAINViewInstead )
{
	const std::string code = Code( kSoundscape );
	ASSERT_FALSE( code.empty() );

	// Not merely "the bad spelling is gone": a file that stopped asking for a
	// view at all would pass the fact above and would have lost the forced
	// soundscape's positional sources, which is the thing 7e RESTORED.
	EXPECT_TRUE( Has( code, "MainViewOrigin()" ) )
		<< "the soundscape stopped asking where the player is, so a forced "
		   "soundscape's positioned sounds have no positions -- which is the "
		   "state 7e found and fixed, arrived at from the other side.";
	EXPECT_TRUE( Has( code, "MainViewForward()" ) );
	EXPECT_TRUE( Has( code, "MainViewRight()" ) );
}

//=============================================================================
// 2. THE ENTITY ASKS
//=============================================================================
TEST( Step7eSubjects, TheSoundscapeAsksNoPlayerEntity )
{
	const std::string code = Code( kSoundscape );
	ASSERT_FALSE( code.empty() );

	EXPECT_FALSE( Has( code, "C_BasePlayer::GetLocalPlayer" ) )
		<< "it is `return NULL;` in this mod (shell_player_orphans.cpp:63), so "
		   "the arm behind it is dead code that reads like live code.";
	EXPECT_FALSE( Has( code, "CanSetSoundMixer" ) )
		<< "CanSetSoundMixer is GetObserverMode() != OBS_MODE_FREEZECAM and "
		   "nothing in this build can enter freezecam.";
	EXPECT_FALSE( Has( code, "c_baseplayer.h" ) )
		<< "the include came back, which drags C_BasePlayer into a shim file "
		   "that no longer names it.";
}

TEST( Step7eSubjects, TheClientRecipientFilterNamesNoEntityList )
{
	const std::string code = Code( kFilter );
	ASSERT_FALSE( code.empty() );

	// The three symbols step 4b's move made client cross-edges are this file's,
	// and it is under the shim so that they stop being cross-edges. What must
	// not come back is the SDK resolve: the file addresses players by SLOT.
	EXPECT_FALSE( Has( code, "C_BaseEntity::Instance" ) );
	EXPECT_FALSE( Has( code, "ToBasePlayer" ) );
	EXPECT_TRUE( Has( code, "MvEntIndex()" ) )
		<< "the IMovementPlayer reroute went; a player's slot is asked through "
		   "the entity again.";
}

//=============================================================================
// 3. THE LANDING SITE, AND THE REFUSAL
//=============================================================================
TEST( Step7eSubjects, TheSoundscapeLandingSiteIsKept )
{
	const std::string code = Code( kSoundscapeManaged );
	ASSERT_FALSE( code.empty() );

	EXPECT_TRUE( Has( code, "public void Push(int soundscapeIndex, int entIndex, int localBits, ReadOnlySpan<Vector> localPositions)" ) )
		<< "ClientSoundscapes.Push is where a soundscape push LANDS (stock's "
		   "Soundscape_Update, which step 7e kept so that wiring the transport "
		   "would be a caller and not a rewrite). Deleting it takes the whole "
		   "soundscape path with it.";
}

//-----------------------------------------------------------------------------
// THE CALLER 7e KEPT THE LANDING SITE FOR -- plan_entity.md step 8e-sound,
// and since sound cluster 17 a managed call with no crossing.
//
// This test REPLACED TheRefusalIsPrintedAndOnlyWhenThereIsSomethingToRefuse:
// the transport is wired, so 7e's refusal was deleted rather than
// re-conditioned. BOTH DIRECTIONS ARE ASSERTED, because either alone is weak.
//-----------------------------------------------------------------------------
TEST( Step7eSubjects, TheLandingSiteHasItsCallerAndTheRefusalIsGone )
{
	const std::string module = Code( kClientModule );
	ASSERT_FALSE( module.empty() );

	EXPECT_TRUE( Has( module, "_soundscapes.Push(index, entIndex, bits, positions)" ) )
		<< "the client module no longer hands the Soundscape message's pushes to "
		   "ClientSoundscapes.Push, so a push reaches nothing.";

	// RAW, not stripped, for the reason the native file had: prose explaining
	// the refusal must not hide a live one beside it.
	const std::string raw = ReadFile( kSoundscapeManaged );
	ASSERT_FALSE( raw.empty() );

	EXPECT_FALSE( Has( raw, "[SourceSharp] soundscapes:" ) )
		<< "the refusal is printed again. Its subject is fixed, so the line is "
		   "a false statement with a citation attached.";

	EXPECT_TRUE( Has( raw, "refused rather than remembered" ) )
		<< "a push this side cannot honour (an index it did not parse) is "
		   "latched again, and the dedupe then answers 'already current' for "
		   "that index for the rest of the map.";
}

TEST( Step7eSubjects, ThePushCounterExistsSoTheGateCanReadIt )
{
	const std::string code = Code( kSoundscapeManaged );
	const std::string commands = Code( kSoundscapeCommands );
	ASSERT_FALSE( code.empty() );
	ASSERT_FALSE( commands.empty() );

	EXPECT_TRUE( Has( code, "_pushes++" ) )
		<< "tools/7e-particle-sound-gate reads this through "
		   "cs_cl_soundscape_report. Without it 'nothing pushes a soundscape' "
		   "is an inference from silence.";
	EXPECT_TRUE( Has( commands, "[ConCommand(\"cs_cl_soundscape_report\"" ) );
	EXPECT_TRUE( Has( commands, "pushed {soundscapes.Pushes}" ) );
}

//=============================================================================
// 4. THE EMITTER'S CLIENT ARM
//=============================================================================
TEST( Step7eSubjects, TheEmittersClientArmAsksTheEntityList )
{
	// THE QUESTION STEP 7e ANSWERED HAS MOVED TO C# WITH THE EMITTER. 7e's
	// sound_emitter.cpp asked the entity list (SourceSharp_ClientEntityFromIndex)
	// rather than C_BaseEntity::Instance, which answered NULL for every entity
	// on the parallel hierarchy, so every managed entity's sound went out as
	// GENDER_NONE. The managed ClientSoundEmitter asks ClientModule's
	// SpeakerModel, which asks the client's own registry by index and the
	// renderable's model by handle: the same question of the entities this port
	// owns, and no SDK class in it.
	const std::string module = Code( kClientModule );
	ASSERT_FALSE( module.empty() );

	EXPECT_TRUE( Has( module, "standalones.GetByIndex(index) is { } speaker ? render.GetModelName(speaker.Handle)" ) )
		<< "the client's speaker lookup is gone. Deleting it is GENDER_NONE for "
		   "every entity, which from the console looks like nothing.";
	EXPECT_TRUE( Has( module, "new Sounds.ClientSoundEmitter(engine, _services.Require<IGameClock>(), context," ) )
		<< "the client's emitter is not built over the context the speaker lookup is in.";

	const std::string code = Code( kEmitter );
	ASSERT_FALSE( code.empty() );
	EXPECT_FALSE( Has( code, "C_BaseEntity::Instance" ) );
	EXPECT_FALSE( CompilesTail( cmake_graph::kClient, "sourcesharp/game/client/sound_emitter.cpp" ) )
		<< "sound_emitter.cpp is back on the client's link line beside the managed emitter.";
}

//=============================================================================
// 5. THE INSTRUMENT
//=============================================================================
TEST( Step7eSubjects, TheParticleReportDoesNotUseTheAssertingAccessor )
{
	// THE REPORT'S OWN READ, NOT THE WHOLE FILE, and the first run of this
	// suite is why: GetControlPointAtCurrentTime is correct where a definition
	// declared the point, and a whole-file needle went red over code it has no
	// business asserting about. Since C7 particles phase 4 the report is
	// managed's (ParticleCommands.cs), and the control points it prints are
	// read natively by SourceSharp_ParticleSystemsRead, which is the body
	// scoped here.
	ASSERT_TRUE( Has( Code( kParticleCommands ), "systems.Read()" ) )
		<< "the report no longer prints the particles shell's read";
	const std::string whole = Code( kManagedParticles );
	ASSERT_FALSE( whole.empty() );

	const size_t begin = whole.find( "int32_t SourceSharp_ParticleSystemsRead(" );
	ASSERT_NE( std::string::npos, begin )
		<< "the report's read is gone, or was renamed. Everything below is "
		   "about its body and would be vacuous without it.";

	const size_t end = whole.find( "\n}\n", begin );
	ASSERT_NE( std::string::npos, end );

	const std::string code = whole.substr( begin, end - begin );

	EXPECT_FALSE( Has( code, "GetControlPointAtCurrentTime" ) )
		<< "that accessor asserts nControlPoint <= GetHighestControlPoint() and "
		   "m_pDef->ReadsControlPoint( n ). A REPORT's job is to say what an "
		   "undeclared control point holds; env_fire_small declares point 0 "
		   "only and this fired particles.h:2392 twice.";
	EXPECT_TRUE( Has( code, "m_ControlPoints[0].m_Position" ) );
	EXPECT_TRUE( Has( code, "m_ControlPoints[1].m_Position" ) );
}

TEST( Step7eSubjects, TheParticleReportCanNameTheClientsOwnCopyOfAnEntity )
{
	// C7 PARTICLES PHASE 4 MOVED THE REPORT, AND THE FACT IS THE SAME FACT.
	// particlemgr.cpp left client.so; cs_cl_particle_report is managed's
	// (ParticleCommands.cs), and its entity line reads IEngineEffects'
	// ClientEntityOrigin, which native answers in shell_te_pool_client.cpp
	// through the same SourceSharp_ClientEntityFromIndex the old report's
	// SSClientEntityFromIndex wrapped. Both halves are asserted below.
	const std::string code = Code( kParticleCommands );
	ASSERT_FALSE( code.empty() );

	// Without this line the gate cannot tell a broken follow sweep from a move
	// that never networked, and its third run could not.
	EXPECT_TRUE( Has( code, "effects.ClientEntityOrigin" ) );
	EXPECT_TRUE( Has( code, ": origin (" ) )
		<< "the report no longer prints where this side thinks the entity is, "
		   "in the line tools/7e-particle-sound-gate reads.";
	// STEP p7-exit MOVED THIS LINE, AND THE FACT IS THE SAME FACT.
	//
	// 7e wrote it as `ClientEntityList().GetClientEntity( nEntIndex )` and gave
	// the reason: "the entity list is the engine boundary and holds BOTH
	// hierarchies; C_BaseEntity::Instance answers NULL for every entity this
	// port owns." That reason is why the assertion exists and it has not
	// changed -- what changed is which object holds both hierarchies.
	// SSClientEntityFromIndex is particlemgr.cpp's guarded helper over
	// SourceSharp_ClientEntityFromIndex, which asks the SLOT TABLE first and
	// falls back to the stock list. It answers for the same entities the old
	// line did -- every shell is in both -- so this is a route change, not a
	// widening. plan_entity.md clause 2 is what forced it: no compiled TU may
	// name the stock client entity list.
	//
	// The needle is the CALL, not the helper's declaration, so deleting the
	// call and leaving the helper reddens this.
	const std::string native = Code( kTePool );
	ASSERT_FALSE( native.empty() );
	EXPECT_TRUE( Has( native, "SourceSharp_ClientEntityFromIndex( nEntityIndex )" ) )
		<< "the report no longer resolves the entity index at all. It must ask "
		   "something that holds BOTH hierarchies, which is 7e's own reason: "
		   "C_BaseEntity::Instance answers NULL for every entity this port "
		   "owns.";
}

//=============================================================================
// 6. THE SOURCE ROWS
//
// A file needed by both sides needs a row in BOTH targets' source lists, and a
// one-sided row is silently wrong. These two are CLIENT-ONLY and the assertion
// is two-sided on purpose: present in one, absent from the other, and the stock
// path gone from both.
//=============================================================================
//=============================================================================
// THE SORT ORIGIN FOLLOWS WITH THE CONTROL POINTS. Stock's
// CParticleProperty::UpdateControlPoint (particle_property.cpp) ends every
// control point update with SetSortOrigin, and the client leaf system sorts and
// culls a system by it. The follow sweep moved the points and not the sort
// origin, so tools/7e-particle-sound-gate read cp0 400 units on and the sort
// origin 0 (3 of 3, once the client saw its entity move at all).
//=============================================================================
TEST( Step7eSubjects, TheFollowSweepMovesTheSortOriginWithTheControlPoints )
{
	const std::string whole = ReadFile( kManagedParticles );
	const size_t begin = whole.find( "void ShellParticles_PreRender()" );
	ASSERT_NE( std::string::npos, begin ) << "the follow sweep is gone, or was renamed";
	const size_t end = whole.find( "\n}\n", begin );
	ASSERT_NE( std::string::npos, end );
	const std::string code = whole.substr( begin, end - begin );

	EXPECT_TRUE( Has( code, "SetControlPoint( nPoint, vecOrigin )" ) )
		<< "control: the sweep still moves the control points";
	EXPECT_TRUE( Has( code, "SetSortOrigin( vecOrigin )" ) )
		<< "the sweep moves the control points and leaves the sort origin where "
		   "the system was created";
}

TEST( Step7eSubjects, AStartedSystemsSortOriginIsItsOwner )
{
	const std::string whole = ReadFile( kManagedParticles );
	const size_t begin = whole.find( "int32_t SourceSharp_ParticleSystemStart(" );
	ASSERT_NE( std::string::npos, begin ) << "the start is gone, or was renamed";
	const size_t end = whole.find( "\n}\n", begin );
	ASSERT_NE( std::string::npos, end );
	const std::string code = whole.substr( begin, end - begin );

	EXPECT_TRUE( Has( code, "SetControlPoint( 0, pOwner->GetAbsOrigin() )" ) )
		<< "control: the start still places control point 0 at the owner";
	EXPECT_TRUE( Has( code, "SetSortOrigin( pOwner->GetAbsOrigin() )" ) )
		<< "a started system's sort origin is left at the map origin";
}

TEST( Step7eSubjects, TheMovedFilesAreInTheClientAndNotTheServer )
{
	// The soundscape's native half is shell_soundscape_audio_client.cpp since
	// sound cluster 17 (lane c2-sims): the system itself is managed.
	static const char *kRows[] = {
		"sourcesharp/game/client/shell_soundscape_audio_client.cpp",
		"sourcesharp/game/client/shell_recipientfilter_client.cpp",
	};

	for ( size_t i = 0; i < sizeof( kRows ) / sizeof( kRows[ 0 ] ); i++ )
	{
		EXPECT_TRUE( cmake_graph::Compiles( cmake_graph::kClient, kRows[ i ] ) )
			<< kRows[ i ] << " is not compiled by client_sourcesharp";
		EXPECT_FALSE( CompilesTail( cmake_graph::kServer, kRows[ i ] ) )
			<< kRows[ i ] << " is compiled by server_sourcesharp. Both are #ifdef "
			<< "CLIENT_DLL-side files and neither is reachable from the server.";
	}
}

TEST( Step7eSubjects, TheStockPathsAreGoneFromBothTargets )
{
	static const char *kGone[] = {
		"game/client/c_soundscape.cpp",
		"game/client/c_recipientfilter.cpp",
	};

	for ( size_t i = 0; i < sizeof( kGone ) / sizeof( kGone[ 0 ] ); i++ )
	{
		EXPECT_FALSE( CompilesTail( cmake_graph::kClient, kGone[ i ] ) )
			<< kGone[ i ] << " is still compiled by client_sourcesharp and the "
			<< "file is not on disk any more; the build fails on a stale row.";
		EXPECT_FALSE( CompilesTail( cmake_graph::kServer, kGone[ i ] ) )
			<< kGone[ i ] << " is compiled by server_sourcesharp.";
	}
}

TEST( Step7eSubjects, TheMovedFilesAreOnDiskAtTheirListedPaths )
{
	// The pairing the two facts above cannot make on their own: a row that
	// names a file nobody wrote passes both of them and fails the build. Same
	// check the integrator ran by hand over every vpc $File row after step 4b
	// warned that a stale row on either side is fatal.
	EXPECT_FALSE( ReadFile( kSoundscape ).empty() );
	EXPECT_FALSE( ReadFile( kFilter ).empty() );
}
