//====== SourceSharp — CAmbientGeneric is DELETED; SENTENCEG_* is not =========//
//
// game/server/sound.cpp was two subsystems sharing a file:
//
//   CAmbientGeneric   the `ambient_generic` looping-sound ENTITY, ~1200 lines
//                     of it, whose registration had been inside
//                     `#ifndef SOURCESHARP` for batches. C# serves the
//                     classname (Entities/Env/AmbientGeneric.cs) and
//                     dm_lockdown places TWENTY-ONE of them.
//
//   SENTENCEG_*       the sentence-group free functions, plus the suit-speaker
//                     emits and TEXTURETYPE_Find. Nothing entity-shaped, and
//                     the only reason the file is on the link line at all.
//
// The entity half is DELETED rather than left compiled behind a guard, and the
// reason is Phase 13 rather than tidiness: once CBaseEntity is a typedef for
// IGameEntity — six pure virtuals and NO members — a class that goes on
// deriving from it cannot compile a single member access. "Leave it compiled"
// stopped being one of the options.
//
// UTIL_RestartAmbientSounds went with it. It walked gEntList for
// "ambient_generic" and called a method on what it found, so it could not
// outlive the type. Its only caller is game/server/player.cpp, which is
// EXCLUDED from the server build — and that exclusion is asserted here,
// because it is the whole of the safety argument and it lives in somebody
// else's file.
//
// WHAT THIS FILE IS FOR. Every fact above is a fact about a file somebody may
// change for their own reasons. Red here is not automatically a regression:
// read the message, re-measure, and either update the fact or take the
// consequence.
//
// SAME STRIPPER RULE as bmodels_verdict_test.cpp: the sentence file's own
// header comment names CAmbientGeneric, UTIL_RestartAmbientSounds and all ten
// of the functions 4a deleted, in the paragraphs explaining why they are gone,
// so a scan that does not remove comments reads the explanation as the
// violation. LoadStripped removes comments before any needle is looked for,
// and that is load-bearing rather than tidy.
//
// WHERE THE FILE IS NOW. plan_entity.md step 4a took game/server/sound.cpp out
// of server_csharp.vpc and off disk. Its three link-demanded functions --
// SENTENCEG_Lookup, SENTENCEG_Init, SENTENCEG_PlaySequentialSz -- are
// sourcesharp/game/server/shell_sentences.cpp, and the ten nothing referenced
// were DELETED rather than carried across. Their declarations stayed in
// game/server/util.h so a returning caller would get a link error naming the
// symbol; that header is compiled by nothing since plan_server_novalve.md P5
// and left the farm in P6, so only the deletion half is still asserted below.
// "Moved the file" and "moved the file and deleted the dead half" are
// different claims and neither implies the other.
//
//=============================================================================//

#include <string>
#include <vector>
#include <fstream>
#include <cstring>
#include <sys/stat.h>
#include "gtest/gtest.h"
#include "cmake_graph.h"

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

// MOVED BY plan_entity.md step 4a, and the name is kept so that every fact
// below still reads as a fact about the same subject. game/server/sound.cpp
// is out of server_sourcesharp and off disk; its three link-demanded
// functions are sourcesharp/game/server/shell_sentences.cpp now and its ten
// unreferenced ones were deleted rather than carried across. LoadStripped
// fails loudly on a file it cannot read, so a stale path here would have been
// red rather than vacuously green.
const char *kSoundCpp  = "sourcesharp/game/server/shell_sentences.cpp";

// game/server/util.h, which DECLARED all thirteen, is compiled by nothing
// since plan_server_novalve.md P5 and left the farm in P6; the facts about
// its declarations retired with it.
// WHAT THE SERVER COMPILES is server_sourcesharp's source list, read through
// cmake_graph.h (compile_commands.json cross-checked against
// src/server/native/CMakeLists.txt). CMake lists every compiled source by its
// farm-relative path, so "out of the build" is exactly "not in that set"; the
// vpc-era `-$File` subtractive arm has no CMake equivalent and went with VPC.
const char *const kServerProofOfRead = "sourcesharp/game/shared/shell_shared.cpp";

} // namespace

//-----------------------------------------------------------------------------
// The entity half is gone.
//-----------------------------------------------------------------------------
TEST( AmbientGenericEvicted, SoundCppHoldsNoEntityAtAll )
{
	const std::string tight = NoWhitespace( LoadStripped( kSoundCpp, "SENTENCEG_Init" ) );

	EXPECT_EQ( tight.find( "classCAmbientGeneric" ), std::string::npos )
		<< "CAmbientGeneric is back in sound.cpp.\n"
		<< "It cannot compile past Phase 13: CBaseEntity becomes a typedef for "
		<< "IGameEntity — six pure virtuals and no members — so every member "
		<< "access in a class that keeps deriving is an error and no retype "
		<< "makes one legal. C# serves ambient_generic and dm_lockdown places "
		<< "21 of them.";

	EXPECT_EQ( tight.find( "LINK_ENTITY_TO_CLASS(ambient_generic" ), std::string::npos )
		<< "sound.cpp registers ambient_generic again. Two factories for one "
		<< "classname is a conflict, not a preference — C# declares this name "
		<< "and a live game answers `debug-api classes` with it.";

	// The whole class is gone, not just its registration: a datadesc left
	// behind would name members that no longer exist.
	EXPECT_EQ( tight.find( "BEGIN_DATADESC(CAmbientGeneric)" ), std::string::npos )
		<< "CAmbientGeneric's DATADESC is still here without the class.";

	EXPECT_EQ( tight.find( "UTIL_RestartAmbientSounds" ), std::string::npos )
		<< "UTIL_RestartAmbientSounds is defined in sound.cpp again. Its body "
		<< "walks gEntList for `ambient_generic` and calls a method on what it "
		<< "finds, so it cannot compile without CAmbientGeneric — if it is back, "
		<< "so is the class.";
}

//-----------------------------------------------------------------------------
// ...and the classname it served has an owner, with instances to prove it.
//
// Asked for PRESENCE in C# and in the map, so that "C++ dropped it" and
// "nobody serves it" cannot look the same.
//-----------------------------------------------------------------------------
TEST( AmbientGenericEvicted, CSharpServesTheClassnameAndTheTrackedMapUsesIt )
{
	const char *pszRoot = SrcRoot();
	ASSERT_TRUE( pszRoot != NULL );

	const std::string managed = ReadRaw( std::string( pszRoot )
		+ "/sourcesharp/managed/SourceSharp.Server/Entities/Env/AmbientGeneric.cs" );

	EXPECT_FALSE( managed.empty() )
		<< "SourceSharp.Server/Entities/Env/AmbientGeneric.cs is gone. C++ "
		<< "deleted its CAmbientGeneric on the strength of that file existing; "
		<< "with both gone dm_lockdown's twenty-one ambients are served by "
		<< "NOBODY and every one is an engine warning and silence.";

	EXPECT_NE( managed.find( "EntityNames.AmbientGeneric" ), std::string::npos )
		<< "AmbientGeneric.cs no longer declares itself for the ambient_generic "
		<< "classname. See above — the C++ class was deleted on the strength of "
		<< "this declaration.";

	const char *pszDir = MapDir();
	ASSERT_TRUE( pszDir != NULL )
		<< "no game/mod_sharp/maps directory found from the working directory; "
		<< "this test would otherwise pass by reading nothing";

	const std::string strTracked = std::string( pszDir ) + "/dm_lockdown.bsp";
	const std::string lump = EntityLump( strTracked );
	ASSERT_FALSE( lump.empty() )
		<< strTracked << " has no readable entity lump. It is a TRACKED file, "
		<< "so this is not a missing build artefact.";

	// The reader, checked before it is believed.
	ASSERT_GE( CountClassname( lump, "worldspawn" ), 1 )
		<< "no worldspawn in " << strTracked << "; what this test read is not "
		<< "an entity lump";

	EXPECT_GT( CountClassname( lump, "ambient_generic" ), 0 )
		<< strTracked << " no longer places any ambient_generic. That does not "
		<< "put the C++ class back — it means the strongest live evidence this "
		<< "deletion had is gone, and the next lane to check it will find "
		<< "nothing to look at.";
}

//-----------------------------------------------------------------------------
// The half that STAYS, spelled the way its referrers spell it.
//
// A deleted symbol is a link error. A renamed one is a link error too — but a
// renamed one that something ELSE in the tree happens to define is not, and
// that is the shape this port has found forty-four times. So these are
// asserted where they live.
//-----------------------------------------------------------------------------
TEST( AmbientGenericEvicted, TheSentenceFunctionsItsReferrersNameAreStillDefinedHere )
{
	const std::string tight = NoWhitespace( LoadStripped( kSoundCpp, "SENTENCEG_Init" ) );

	// Exactly the undefined references four built objects hold into sound.o,
	// measured with `nm --undefined-only` over the debug server objects:
	//   SENTENCEG_Lookup            baseentity.o, SoundEmitterSystem.o
	//   SENTENCEG_PlaySequentialSz  shell_entity_orphans.o
	//   SENTENCEG_Init              world_adapter.o
	//
	// SENTENCEG_PlaySequentialSz LEFT IN THE SERVER GC CULL (2026-10-01): its
	// referrer had gone, nothing on the link line imported it, and
	// --print-gc-sections dropped it; asserted absent after the loop.
	static const char *kWanted[] = {
		"intSENTENCEG_Lookup(constchar*sample)",
		"voidSENTENCEG_Init()",
	};

	for ( size_t i = 0; i < sizeof( kWanted ) / sizeof( kWanted[ 0 ] ); i++ )
	{
		EXPECT_NE( tight.find( kWanted[ i ] ), std::string::npos )
			<< "sound.cpp no longer defines `" << kWanted[ i ] << "`.\n"
			<< "Objects outside this file hold an undefined reference to "
			<< "these sentence functions. Deleting the ENTITY half of this "
			<< "file was allowed; deleting the half the linker actually wants "
			<< "was not.";
	}
	EXPECT_EQ( tight.find( "SENTENCEG_PlaySequentialSz(" ), std::string::npos )
		<< "SENTENCEG_PlaySequentialSz is back; nothing imported it when the "
		<< "server gc cull deleted it.";

}

//-----------------------------------------------------------------------------
// ...AND THE TEN NOTHING REFERENCED ARE GONE.
//
// The other half of step 4a's argument, and the half that could quietly not be
// true. "Moved the file" and "moved the file and deleted the dead half" look
// identical to the test above, because that one only asks about the three the
// linker demands.
//
// Each of these was measured, per symbol, with tools/4a-owes over the debug
// server objects: no object in the server link holds an undefined reference to
// any of them. The one call site in a COMPILED file -- TEXTURETYPE_Find at
// gamemovement.cpp:939 -- is inside stock's own block comment, which is why
// `nm -C -u gamemovement.o` names neither it nor any SENTENCEG_.
//
// The declaration half (each one still declared in game/server/util.h) is
// retired: game/server/util.h is compiled by nothing since
// plan_server_novalve.md P5 and left the farm in P6.
//-----------------------------------------------------------------------------
TEST( AmbientGenericEvicted, TheUnreferencedSentenceHelpersAreDeleted )
{
	const std::string tight = NoWhitespace( LoadStripped( kSoundCpp, "SENTENCEG_Init" ) );

	static const char *kDeleted[] = {
		"intSENTENCEG_PlayRndI(",
		"intSENTENCEG_PickRndSz(",
		"voidSENTENCEG_PlaySentenceIndex(",
		"intSENTENCEG_PlayRndSz(",
		"voidSENTENCEG_Stop(",
		"intSENTENCEG_GetIndex(",
		"voidUTIL_EmitSoundSuit(",
		"intUTIL_EmitGroupIDSuit(",
		"intUTIL_EmitGroupnameSuit(",
		"charTEXTURETYPE_Find(",
	};

	for ( size_t i = 0; i < sizeof( kDeleted ) / sizeof( kDeleted[ 0 ] ); i++ )
	{
		EXPECT_EQ( tight.find( kDeleted[ i ] ), std::string::npos )
			<< "`" << kDeleted[ i ] << "` is defined again in the "
			<< "sentence file. Nothing in the server link referenced it when 4a "
			<< "deleted it; if something does now, that is the thing to look at.";
	}
}

//-----------------------------------------------------------------------------
// ...and they emit through the FREE function, not through the class static.
//
// CBaseEntity::EmitSentenceByIndex is an INLINE FORWARDER in baseentity.h onto
// GameSound_EmitSentenceByIndex, which is declared in baseentity_statics.h and
// survives Phase 13 by design. The forwarder does not: it is a member of a
// class that becomes IGameEntity, so a call through it is one of the four
// CBASEENTITY-STATIC sites the survey reported for this file, and naming the
// free function is what took sound.cpp to zero.
//
// This is exactly the shape somebody tidies BACK, because the forwarder is
// shorter -- it supplies six defaults that the direct call has to spell. So it
// is asserted in both directions.
//-----------------------------------------------------------------------------
TEST( AmbientGenericEvicted, TheSentenceEmitsNameTheFreeFunctionNotTheClassStatic )
{
	const std::string tight = NoWhitespace( LoadStripped( kSoundCpp, "SENTENCEG_Init" ) );

	EXPECT_EQ( tight.find( "CBaseEntity::EmitSentenceByIndex(" ), std::string::npos )
		<< "sound.cpp emits a sentence through CBaseEntity::EmitSentenceByIndex "
		<< "again.\n"
		<< "That is an inline forwarder on a class Phase 13 turns into a typedef "
		<< "for IGameEntity, so the call goes with it: four CBASEENTITY-STATIC "
		<< "sites, in a file that is otherwise at zero. Call "
		<< "GameSound_EmitSentenceByIndex directly and spell the four trailing "
		<< "defaults the forwarder supplied -- NULL, NULL, true, 0.0f.";

	// The other direction: "does not call the static" and "does not emit at
	// all" look identical to the check above, and deleting the emits would
	// satisfy it.
	//
	// The file also DECLARES the function since P5 lane SP (baseentity_statics.h
	// reaches shareddefs.h, which its R5 include set does not), so that one
	// declaration -- `void` in front, whitespace stripped -- is not a call.
	size_t n = 0;
	size_t nDecl = 0;
	for ( size_t at = tight.find( "GameSound_EmitSentenceByIndex(" );
	      at != std::string::npos;
	      at = tight.find( "GameSound_EmitSentenceByIndex(", at + 1 ) )
	{
		if ( at >= 4 && tight.compare( at - 4, 4, "void" ) == 0 )
			nDecl++;
		else
			n++;
	}
	// 1 -> 0 IN THE SERVER GC CULL (2026-10-01): the last emit was
	// SENTENCEG_PlaySequentialSz's, which nothing called; it, the declaration
	// and GameSound_EmitSentenceByIndex itself went.
	EXPECT_EQ( nDecl, 0u )
		<< "the sentence file declares GameSound_EmitSentenceByIndex again";

	// 4 -> 1 BY plan_entity.md step 4a, AND IT IS A DELETION RATHER THAN A
	// MOVE, which is why it is spelled out. Three of the four emits were
	// SENTENCEG_PlayRndI, SENTENCEG_PlaySentenceIndex and SENTENCEG_PlayRndSz,
	// and NOTHING in the server link referenced any of them -- their callers
	// are the NPC talker code and player.cpp, both out of server_sourcesharp.
	// They have no new address, so unlike the guard moves in cpp_alias_t6_test
	// there is no second row to carry the difference. The one left is
	// SENTENCEG_PlaySequentialSz's, which shell_entity_orphans.o still demands.
	EXPECT_EQ( n, 0u )
		<< "the sentence file makes " << n << " calls to "
		<< "GameSound_EmitSentenceByIndex, and the function is gone; ask which "
		<< "object started referencing a sentence emit again.";
}

//-----------------------------------------------------------------------------
// THE SAFETY ARGUMENT, asserted where it lives.
//
// UTIL_RestartAmbientSounds is DECLARED in game/server/util.h and no longer
// DEFINED anywhere. That is deliberate — a link error is the right answer for
// anyone who puts its caller back — but it is only safe while the caller is
// out of the build, and the caller is in a file this lane does not own.
//-----------------------------------------------------------------------------
TEST( AmbientGenericEvicted, TheOnlyCallerOfTheDeletedHelperIsStillOutOfTheBuild )
{
	// THE ANCHOR MOVED; NOTHING THIS FILE ASSERTS DID.
	//
	// It used to be `$SRCDIR\game\shared\baseentity_shared.cpp`, and THE FLIP
	// REMOVED THAT ROW from both vpcs along with the rest of the entity hierarchy.
	// A stock TU is not an anchor for this port -- removing stock TUs is precisely
	// what the port does, so an anchor on one is a proof-of-read the flip can evict.
	// Stale here is the worst case: this anchor is the only thing stopping every
	// absence invariant it guards from passing vacuously against a file it is not
	// really reading.
	//
	// So the proof-of-read is now the PORT'S OWN shim, which no flip can take, and
	// it is the anchor the client-side entries in this family already use.
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kServerProofOfRead ) )
		<< "server_sourcesharp does not compile " << kServerProofOfRead
		<< " -- the reader is not reading the server's source list";

	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, "game/server/player.cpp" ) )
		<< "game/server/player.cpp is back in the server build.\n"
		<< "It calls UTIL_RestartAmbientSounds, which is declared in "
		<< "game/server/util.h and DEFINED NOWHERE — its body walked gEntList "
		<< "for `ambient_generic` and could not outlive CAmbientGeneric. The "
		<< "link will fail naming the symbol, which is the intended outcome "
		<< "rather than an accident: decide whether that call still means "
		<< "anything now that the classname is C#'s, and either drop it or "
		<< "give it a managed answer.";

	// The declaration half of this argument (UTIL_RestartAmbientSounds still
	// declared in util.h) is retired: game/server/util.h is compiled by nothing
	// since plan_server_novalve.md P5 and left the farm in P6.
}
