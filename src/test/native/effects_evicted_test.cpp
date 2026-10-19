//========= SourceSharp — game/server/effects.cpp is out of the build =========//
//
// effects.cpp held sixteen entity classes in 2,545 lines and is out of the
// server build (it was `-$File` out of server_csharp.vpc; it is absent from
// server_sourcesharp's list in src/server/native/CMakeLists.txt now). The .cpp stays ON DISK — props.cpp's and
// physpropclientside.cpp's do too — so nothing about that exclusion is visible
// to a compiler, and every fact it rests on is a fact only a source scan can
// defend.
//
// WHAT THE LINK ALREADY AGREES ABOUT, and therefore what this does NOT need to
// re-check: `./evict_probe release server effects.o` answered exit 1 with
// exactly ONE undefined reference, UTIL_BloodSpray, and after the lift to
// shell_entity_orphans.cpp the server links clean in BOTH configurations,
// including `make debug GC_SECTIONS=0` — the diagnostic Makefile:30 recommends,
// and the one that catches an eviction whose symbols a RETAINED file still
// names. What the link cannot tell you is WHY, and it is silent about the
// difference between "nothing calls this" and "the caller is itself dead".
// Every fact below is of the second kind.
//
// THE REVOCABLE EDITS, each of which produces no diagnostic anywhere:
//
//   * un-exclude the .cpp and sixteen dead classes are back in server.so, three
//     of them registering classnames C# already answers to (env_bubbles,
//     env_blood, gibshooter and ten more) — a duplicate factory entry for every
//     one, of which the dictionary keeps ONE and silently drops the other;
//   * put UTIL_BloodSpray or EffectsPrecache back into effects.cpp and the
//     first is a duplicate symbol (loud) while the second is a SECOND
//     PRECACHE_REGISTER_FN of the same function (silent — it would simply
//     precache the same six sounds twice);
//   * delete either from shell_entity_orphans.cpp: UTIL_BloodSpray fails the
//     link, and EffectsPrecache does NOT, because nothing names it. The splash,
//     the underwater bullet impact, the ricochet and BaseExplosionEffect.Sound
//     would go silent on their first play with nothing reporting it. That is
//     the one this file exists for.
//
// AND ONE FIXTURE-AGAINST-NATIVE CHECK. BloodSprayCommand.Everything in
// SourceSharp.Server/DevCommandsLive.cs stands in for stock's
// FX_BLOODSPRAY_ALL, which is 0xFF, on the argument that the client reads
// exactly three of those bits. C# cannot see the client. This asserts the
// agreement, so a fourth bit added to fx_blood.cpp is a red test rather than an
// effect the managed command can no longer ask for. Same rule as TierParityTests
// and AbiParityTests.
//
// THE STRIPPER IS LOAD-BEARING rather than tidy: this file's own prose spells
// `LINK_ENTITY_TO_CLASS( env_bubbles`, `UTIL_BloodSpray` and
// `PRECACHE_REGISTER_FN` in the paragraphs explaining where they may and may not
// be, and an unstripped scan would read the explanation as the violation. Same
// rule as clientside_prop_evicted_test.cpp and header_alias_test.cpp.
//
// ANCHORS, NOT LINE COUNTS. Every file below is asserted to still contain a
// string that identifies it. A source-scanning test whose file has been renamed
// or rewritten reads an empty string and reports every absence invariant as
// satisfied — the check that cannot fail.
//
//=============================================================================//

#include <string>
#include <fstream>
#include <set>
#include <vector>
#include <cctype>
#include <sys/stat.h>
#include "gtest/gtest.h"

#include "cmake_graph.h"

namespace
{

//-----------------------------------------------------------------------------
// Finding the tree, and reading it. Same shape as
// clientside_prop_evicted_test.cpp.
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

// Whole file, comments gone. Empty means "could not open", which every caller
// must distinguish from "opened and found nothing".
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
		all += line;
		all += '\n';
	}
	return StripComments( all );
}


struct File_t
{
	const char *pszRel;
	const char *pszAnchor;
};

// The anchor is a string the file must still contain for this test to be
// reasoning about the file it thinks it is.
//
// WHAT THE SERVER COMPILES is server_sourcesharp's source list, read through
// cmake_graph.h (compile_commands.json cross-checked against
// src/server/native/CMakeLists.txt). Its proof-of-read is the PORT'S OWN shim,
// which no flip can take. (It used to be
// `$SRCDIR\game\shared\baseentity_shared.cpp`, and THE FLIP REMOVED THAT ROW along with the rest of
// the entity hierarchy: a stock TU is not an anchor for this port.)
const char *const kServerProofOfRead = "sourcesharp/game/shared/shell_shared.cpp";
// ANCHOR RE-POINTED, AND FOR THE REASON THE NOTE ABOVE GIVES rather than a new
// one -- plan_shim.md 4a item 11, lane 5a-physresidue. It was
// `CSkyCamera *GetCurrentSkyCamera()`, whose last importer
// (shell_playerlocaldata.o's ClientData_Update) was deleted by lane
// 3b-enginebind, so the definition went on a 0-importer reading over both link
// lines. An anchor a later phase can evict is not an anchor.
//
// RadiusDamage is the replacement because it is the opposite kind of row: it
// is in this same file, it is LIVE -- shell_worldquery.o imports it for
// env_explosion's blast and EntityFlame's burn tick -- and no assertion in this
// file names it, which is this family's rule that an anchor must not be implied
// by the thing it guards.
// RE-POINTED AGAIN in plan_server_native_v2 T2: RadiusDamage went (no managed
// caller; its sound-ledger arm is BlastSweep's now). ss::SetPlayerSurface is
// the same kind of row: in this file, live (shell_movement's surface funnel),
// and named by no assertion here.
const File_t kOrphans = { "sourcesharp/game/server/shell_absent.cpp",
	"void ss::SetPlayerSurface(" };
const File_t kWorldQuery = { "sourcesharp/game/server/shell_worldquery.cpp",
	"void SourceSharp_BloodEffect(" };
// The client's reader of the spray bits is C# since lane c5-particles
// (fx_blood.cpp left client.so): BloodEffects.BloodSpray.
const File_t kFxBlood = { "sourcesharp/managed/SourceSharp.Client/Effects/BloodEffects.cs",
	"public BloodBurst BloodSpray(" };
const File_t kDevCommands = { "sourcesharp/managed/SourceSharp.Server/DevCommandsLive.cs",
	"public sealed class BloodSprayCommand" };
const File_t kSpriteShell = { "sourcesharp/game/client/shell_sprite_render.cpp",
	"class CSourceSharpSpriteRenderer : public C_SpriteRenderer" };

std::string Load( const File_t &f )
{
	const std::string s = ReadStripped( f.pszRel );
	EXPECT_FALSE( s.empty() ) << "could not open " << f.pszRel
		<< " — every absence invariant below would pass vacuously";
	EXPECT_NE( s.find( f.pszAnchor ), std::string::npos )
		<< f.pszRel << " does not contain its anchor \"" << f.pszAnchor
		<< "\"; the file this test reasons about has moved or been rewritten";
	return s;
}

// Is the server compiling ANY source whose path contains the needle? The
// read is proved first, so an unread list cannot answer "no" for free.
bool ServerCompilesAnyNaming( const std::string &strNeedle )
{
	const std::set< std::string > compiled = cmake_graph::CompiledSources( cmake_graph::kServer );
	EXPECT_TRUE( compiled.count( kServerProofOfRead ) )
		<< "server_sourcesharp does not compile " << kServerProofOfRead
		<< " -- the reader is not reading the server's source list";
	for ( std::set< std::string >::const_iterator it = compiled.begin(); it != compiled.end(); ++it )
		if ( it->find( strNeedle ) != std::string::npos )
			return true;
	return false;
}

size_t CountOf( const std::string &strHaystack, const std::string &strNeedle )
{
	size_t n = 0;
	for ( size_t at = strHaystack.find( strNeedle );
	      at != std::string::npos;
	      at = strHaystack.find( strNeedle, at + strNeedle.size() ) )
	{
		n++;
	}
	return n;
}

} // namespace

//-----------------------------------------------------------------------------
// The exclusion itself.
//-----------------------------------------------------------------------------
TEST( EffectsEvicted, TheServerExcludesEffectsCpp )
{
	ASSERT_TRUE( cmake_graph::Compiles( cmake_graph::kServer, kServerProofOfRead ) )
		<< "server_sourcesharp does not compile " << kServerProofOfRead
		<< " -- the reader is not reading the server's source list";

	EXPECT_FALSE( cmake_graph::Compiles( cmake_graph::kServer, "game/server/effects.cpp" ) )
		<< "effects.cpp is back in the server build. Sixteen entity classes come "
		   "with it, thirteen of which register classnames a managed class "
		   "already answers to — and the factory dictionary keeps ONE "
		   "registration per name and drops the other in silence";
}

//-----------------------------------------------------------------------------
// The two tenants, and where each of them may live.
//
// ONE definition each, in the shell and nowhere else. A second definition in
// effects.cpp would be a duplicate symbol IF the file came back, which is
// exactly the state the first test forbids and this one makes survivable to
// diagnose.
//-----------------------------------------------------------------------------
TEST( EffectsEvicted, TheShellDefinesBothTenantsExactlyOnce )
{
	const std::string s = Load( kOrphans );

	EXPECT_EQ( CountOf( s, "void UTIL_BloodSpray(" ), 1u )
		<< "shell_entity_orphans.cpp defines UTIL_BloodSpray once. It is the only "
		   "symbol outside effects.cpp that anything in the server still names, "
		   "and its one live caller is SourceSharp_BloodEffect — our own shim, "
		   "acting for the managed env_blood";

	EXPECT_EQ( CountOf( s, "void EffectsPrecache(" ), 1u )
		<< "shell_entity_orphans.cpp defines EffectsPrecache once";

	EXPECT_EQ( CountOf( s, "PRECACHE_REGISTER_FN( EffectsPrecache )" ), 1u )
		<< "and registers it exactly once. THIS IS THE ONE NO LINKER CHECKS: "
		   "nothing names EffectsPrecache, so losing the registration costs no "
		   "diagnostic at all — it costs the splash, the underwater bullet "
		   "impact, the ricochet and BaseExplosionEffect.Sound, each silent the "
		   "first time the managed side plays it";
}

TEST( EffectsEvicted, TheShellPrecachesEverySoundEffectsCppDid )
{
	const std::string s = Load( kOrphans );

	// Stock's five unconditional ones and the multiplayer one. Named
	// individually rather than counted, because a count of six is satisfied by
	// six copies of one name.
	const char *kSounds[] = {
		"\"Underwater.BulletImpact\"",
		"\"FX_RicochetSound.Ricochet\"",
		"\"Physics.WaterSplash\"",
		"\"BaseExplosionEffect.Sound\"",
		"\"Splash.SplashSound\"",
		"\"HudChat.Message\"",
	};

	for ( size_t i = 0; i < sizeof( kSounds ) / sizeof( kSounds[ 0 ] ); i++ )
	{
		EXPECT_NE( s.find( kSounds[ i ] ), std::string::npos )
			<< "the lifted EffectsPrecache still precaches " << kSounds[ i ];
	}
}

TEST( EffectsEvicted, TheOnlyLiveCallerOfBloodSprayIsOurOwnShim )
{
	// The inverted shape, asserted rather than remembered. If a NATIVE caller
	// ever appears, UTIL_BloodSpray stops being an orphan and this file's whole
	// argument changes — so the fact is worth failing on.
	const std::string s = Load( kWorldQuery );

	EXPECT_NE( s.find( "UTIL_BloodSpray(" ), std::string::npos )
		<< "SourceSharp_BloodEffect still calls UTIL_BloodSpray. If it has "
		   "stopped, the function has no caller at all and belongs nowhere "
		   "rather than in the orphans file";
}

//-----------------------------------------------------------------------------
// The classname that came with it.
//-----------------------------------------------------------------------------
TEST( EffectsEvicted, BloodsprayIsAnsweredByAManagedCommand )
{
	const std::string s = Load( kDevCommands );

	EXPECT_NE( s.find( "[ConCommand(\"bloodspray\"" ), std::string::npos )
		<< "bloodspray is registered by BloodSprayCommand. effects.cpp's "
		   "ConCommand went with the file, and an unregistered word answers "
		   "\"Unknown command\" — silent by a different route, which is the "
		   "exact failure DevCommands.cs exists to end";
}

//-----------------------------------------------------------------------------
// THE FIXTURE-AGAINST-NATIVE CHECK.
//
// The managed command passes Drops|Gore|Cloud where stock passed
// FX_BLOODSPRAY_ALL (0xFF), on the argument that the client reads exactly those
// three bits. That argument is about a file C# cannot see.
//-----------------------------------------------------------------------------
TEST( EffectsEvicted, TheClientReadsExactlyTheThreeSprayBitsTheManagedCommandSends )
{
	const std::string s = Load( kFxBlood );

	EXPECT_NE( s.find( "SprayDrops = 0x01, SprayGore = 0x02, SprayCloud = 0x04;" ), std::string::npos )
		<< "the client's three spray bits are stock's FX_BLOODSPRAY_DROPS, _GORE and _CLOUD";
	EXPECT_NE( s.find( "(flags & SprayDrops)" ), std::string::npos )
		<< "the client still reads the drops bit";
	EXPECT_NE( s.find( "(flags & SprayGore)" ), std::string::npos )
		<< "the client still reads the gore bit";
	EXPECT_NE( s.find( "(flags & SprayCloud)" ), std::string::npos )
		<< "the client still reads the cloud bit";

	// AND NOTHING ELSE. FX_BLOODSPRAY_ALL is 0xFF, so five bits are unclaimed;
	// a fourth one given a meaning here is an effect BloodSprayCommand.Everything
	// would stop asking for, with nothing anywhere saying so. Every identifier
	// that BEGINS with "Spray" is one of the three bits (or the Sprays count).
	std::vector< std::string > others;
	for ( size_t at = s.find( "Spray" ); at != std::string::npos; at = s.find( "Spray", at + 1 ) )
	{
		if ( at > 0 && ( isalnum( (unsigned char)s[ at - 1 ] ) || s[ at - 1 ] == '_' ) )
			continue;
		size_t end = at;
		while ( end < s.size() && ( isalnum( (unsigned char)s[ end ] ) || s[ end ] == '_' ) )
			end++;
		const std::string token = s.substr( at, end - at );
		if ( token != "SprayDrops" && token != "SprayGore" && token != "SprayCloud" && token != "Sprays" )
			others.push_back( token );
	}
	EXPECT_TRUE( others.empty() )
		<< "BloodEffects.cs names a spray bit that is not DROPS, GORE or CLOUD (" << ( others.empty() ? "" : others[0] ) << "). "
		   "BloodSprayCommand.Everything in DevCommandsLive.cs is those three and "
		   "stands in for stock's 0xFF; it no longer does";
}

//-----------------------------------------------------------------------------
// The three classes that left with no managed replacement, and the evidence
// that each was already unreachable. All three are LINK_ENTITY_TO_CLASS
// registrations that were guarded away before this lane — the point is that the
// guard is what made them unreachable, so un-guarding one is the regression.
//-----------------------------------------------------------------------------
TEST( EffectsEvicted, TheHelicopterThatWantedTheRotorShooterIsStillNotBuilt )
{
	// GetRotorWashShooter's ONLY caller is hl2/cbasehelicopter.cpp, which lived
	// in Valve's server_hl2.vpc and has never been on this link line — asserted
	// here by the absence of any helicopter file from what the server compiles,
	// under any directory.
	EXPECT_FALSE( ServerCompilesAnyNaming( "cbasehelicopter" ) )
		<< "server_sourcesharp (src/server/native/CMakeLists.txt) now compiles "
		   "a cbasehelicopter file. env_rotorshooter's "
		   "CRotorWashShooter left with effects.cpp on the strength of "
		   "GetRotorWashShooter having no compiled caller, and cbasehelicopter.cpp "
		   "is that caller";
}

//-----------------------------------------------------------------------------
// game/shared/Sprite.cpp, and THE MEASUREMENT THIS TEST ASKED FOR HAS BEEN
// TAKEN.
//
// This section used to assert that shell_sprite_render.cpp still did
// `new C_SpriteTrail`, with the note: "If nothing builds one, SpriteTrail.o
// stops needing C_Sprite and BOTH files become evictable from the client --
// which is a measurement worth taking rather than a regression."
//
// Taken. Measured with nm over the built debug client objects -- the
// defined-symbols of one against the undefined-symbols of every other:
//
//   BEFORE   Sprite.o        2 referrers: SpriteTrail.o (12 symbols) and
//                            shell_sprite_render.o (SetBrightness, SetScale)
//            SpriteTrail.o   1 referrer:  shell_sprite_render.o, seven setters
//                            and the constructor
//   AFTER    SpriteTrail.o   ZERO referrers
//            Sprite.o        one referrer, SpriteTrail.o, which goes with it
//
// So the trail WAS the reason and it is not a reason any more. The ribbon lives
// on a CShellRenderable in shell_sprite_render.cpp -- the same de-entified base
// C_PointWorldText moved onto -- and both stock files are `-$File` out of the
// client build.
//
// SERVER. Unchanged, and not this lane's: what holds Sprite.o on the server link
// is gib.cpp's two dynamic_cast blocks, which are dead but belong to the
// prop-gibs lane. It was written down in server_csharp.vpc and is in Sprite.cpp.
// (gib.cpp and Sprite.cpp have since both left server_sourcesharp.)
//
// WHAT IS ASSERTED NOW is the trail's move, plus the one behaviour consequence
// below:
//
//   * the managed shooter attaches no sprite to its gibs.
//-----------------------------------------------------------------------------
TEST( SpriteAfterEffects, TheTrailNoLongerHoldsSpriteOnTheClientLink )
{
	const std::string shell = Load( kSpriteShell );

	EXPECT_EQ( shell.find( "C_SpriteTrail" ), std::string::npos )
		<< "shell_sprite_render.cpp names C_SpriteTrail again. That was the ONLY "
		   "thing on the client that wanted one -- nothing sends DT_SpriteTrail, "
		   "and CSpriteTrail::SpriteTrailCreate has no compiled caller -- so this "
		   "single call is what put both SpriteTrail.o and Sprite.o on the link "
		   "line. Re-measure before assuming it is a fix.";

	// The ribbon still has to EXIST, or this test would pass just as well over a
	// file that had deleted the trail rather than moved it. The two halves of what
	// makes it draw:
	EXPECT_NE( shell.find( "class CSourceSharpSpriteTrail : public CShellRenderable" ), std::string::npos )
		<< "the managed trail has no renderable. A trail is the one member of the "
		   "sprite family that SIMULATES -- a ring of 256 points aged over time -- "
		   "and that state has to live somewhere between frames.";

	EXPECT_NE( shell.find( "AddToLeafSystem( RENDER_GROUP_TRANSLUCENT_ENTITY )" ), std::string::npos )
		<< "the ribbon is not put into the client leaf system. That call is the "
		   "whole of what InitializeAsClientEntity was doing on this path: without "
		   "it nothing ever calls DrawModel, and the trail is simulated every frame "
		   "and never drawn.";
}

TEST( SpriteAfterEffects, TheManagedShooterAttachesNoSpriteToItsGibs )
{
	const std::string shim = Load( kWorldQuery );

	// A DOCUMENTED GAP, pinned as a gap. CGibShooter::InitPointGib was the only
	// caller of CGib::SetSprite in the build, and it left with effects.cpp; the
	// managed env_shooter goes through SourceSharp_ShootGib, which builds the
	// gib and stops. So `shootmodel` naming a .vmt no longer produces a sprite
	// gib. If this ever becomes false the gap has been closed, and the two
	// dynamic_cast blocks in gib.cpp stop being dead — which changes whether
	// Sprite.o can leave the server link.
	EXPECT_EQ( shim.find( "SetSprite(" ), std::string::npos )
		<< "shell_worldquery.cpp now calls CGib::SetSprite. That closes the "
		   "sprite-gib gap effects.cpp's eviction left — good — but it also "
		   "revives gib.cpp's dynamic_cast<CSprite*> blocks, so Sprite.cpp's "
		   "header comment needs rewriting and whether the server must compile "
		   "Sprite.cpp again (src/server/native/CMakeLists.txt) re-measured";
}

//=============================================================================
// THE SPRITE SHELL'S DUPLICATION, FOLDED (lane 4c-render, plan_shim.md §6).
//
// Two things were written out more than once in shell_sprite_render.cpp: the
// render-mode bounds check (twice) and the "find the row for this owner" loop
// (three times, while sourcesharp/game/shared/shell_effect_registry.h already
// held it with a test that drives it).
//
// The render-mode fold has exactly ONE way to go wrong and it is invisible: if
// the two call sites share one warn-once flag, the second caller's first bad
// render mode is silent whenever the first caller has already warned. They are
// fed by two different managed call paths, so that is a real case, and the
// symptom is a sprite that draws nothing with nothing in the console.
//
// These live in this file rather than a new one because kSpriteShell is already
// here with its anchor, and a new binary is a buildcpptests edit.
//=============================================================================

TEST( SpriteAfterEffects, TheRenderModeCheckIsOneFunctionAndNotTwoCopies )
{
	const std::string shim = Load( kSpriteShell );

	// The rule itself, once. Counted exactly: a second copy is the thing this
	// fact exists to catch, and a floor would pass for it.
	EXPECT_EQ( CountOf( shim, "nRenderMode >= 0 && nRenderMode < kRenderModeCount" ), 1u )
		<< "the render-mode bounds test is written out more than once again. "
		   "CEngineSprite::GetMaterial subscripts a kRenderModeCount array with "
		   "no check of its own, so a copy that drifts is a segfault.";

	EXPECT_NE( shim.find( "static bool SpriteRenderModeRefused(" ), std::string::npos )
		<< "the shared render-mode check is gone; both call sites are now "
		   "either unchecked or carrying their own copy";
}

TEST( SpriteAfterEffects, TheTwoCallersKeepTheirOwnWarnOnceFlags )
{
	const std::string shim = Load( kSpriteShell );

	// THE FLAG IS PASSED IN, which is the whole reason the helper takes three
	// arguments. A `static bool` inside SpriteRenderModeRefused would be one
	// flag for both callers and the second one to see a bad mode would report
	// nothing.
	EXPECT_NE( shim.find( "bool &bWarned" ), std::string::npos )
		<< "SpriteRenderModeRefused no longer takes the warn-once flag by "
		   "reference, so the two call sites share one";

	// And the two flags are two OBJECTS, not one name used twice.
	EXPECT_EQ( CountOf( shim, "static bool s_bWarnedDrawSprite = false;" ), 1u )
		<< "DrawSprite's warn-once flag is gone or duplicated";
	EXPECT_EQ( CountOf( shim, "static bool s_bWarnedTrail = false;" ), 1u )
		<< "SpriteTrailUpdate's warn-once flag is gone or duplicated";
	EXPECT_EQ( CountOf( shim, "SpriteRenderModeRefused( nRenderMode," ), 2u )
		<< "the shared check is not asked at both call sites";
}

TEST( SpriteAfterEffects, TheOwnerLookupIsTheSharedRegistryAndNotThreeCopies )
{
	const std::string shim = Load( kSpriteShell );

	// Two of the three hand-rolled "find the row for this owner" loops are
	// ShellEffects_Find now. The third — RendererFor — is not, and must not be
	// quietly made so: its list holds POINTERS to heap rows because a
	// CSourceSharpSpriteRenderer is handed out by address, and a CUtlVector of
	// values moves its elements when it grows.
	EXPECT_NE( shim.find( "#include \"shell_effect_registry.h\"" ), std::string::npos )
		<< "shell_sprite_render.cpp no longer includes the shared registry";
	EXPECT_EQ( CountOf( shim, "static int FindTrail(" ), 0u )
		<< "the trail's own owner-lookup loop is back beside the shared one";
	EXPECT_EQ( CountOf( shim, "static int FindParticleSystem(" ), 0u )
		<< "the particle system's own owner-lookup loop is back";
	// The particle systems' three left with them for shell_managed_particles.cpp
	// (lane c5-particles); shell_effect_registry_test.cpp counts them there.
	EXPECT_EQ( CountOf( shim, "ShellEffects_Find(" ), 2u )
		<< "the shared lookup is not asked at every site that used to hand-roll "
		   "one: two for the trails";

	// The positive half for RendererFor's exemption, so that "it is not using
	// the helper" cannot be satisfied by deleting it.
	EXPECT_NE( shim.find( "CUtlVector< SourceSharpSprite_t * > g_ManagedSprites" ), std::string::npos )
		<< "the renderer pool is no longer a list of POINTERS. If it is a list "
		   "of values now, every CSourceSharpSpriteRenderer address handed out "
		   "by RendererFor dangles the next time the vector grows — which is a "
		   "pixel-visibility query handle read after free, once per frame.";
}
