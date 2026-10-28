//========= SourceSharp — lane T shard T6's .cpp arms, read as source ========//
//
// Phase 13 makes `CBaseEntity` a typedef for `IGameEntity`. Route step 5's
// `.cpp` half is the work of getting each site through that, and shard T6 is
// fifteen files of it (docs/typedef-dryrun/route/s5_shards.json).
//
// WHY A SOURCE-READING TEST, when the armed survey compiles the real thing.
// Because the survey is a measurement run by hand and these files are not.
// Between surveys the arms can rot in ways nothing else notices:
//
//   * a guard whose `#else` or `#endif` is lost to a merge splice. This tree
//     has been bitten by exactly that — a keep-both merge swallowing a brace —
//     and an `#ifndef` that loses its `#else` still compiles unarmed while
//     silently deleting the SDK text from the shipping build;
//   * a DEBUG-ONLY site coming back. See TheDebugOnlyAsksAreRerouted — the
//     armed survey patches the RELEASE ninja graph, so every armed measurement
//     in this port is taken with -DNDEBUG, and the body of an `Assert()` and
//     anything inside `#ifdef _DEBUG` is invisible to all of them.
//
// THE SIX STOCK FILES OF THIS SHARD (c_te_projecteddecal.cpp,
// cdll_client_int.cpp, entity_tools_server.cpp, debugoverlay_shared.cpp,
// physics_saverestore.cpp, saverestore.cpp) are no longer compiled by
// client.so or server.so and have left the source tree. Their rows, the
// STATIC-NS reroutes (all in cdll_client_int.cpp and saverestore.cpp), the
// client debug-only rows, and the armed-arm rule that only they could exercise
// were culled with them. Every file left in the table carries zero regions.
//
// The COMPILE-side evidence is src/survey_run with the alias armed, plus (for
// the debug-only half) the same command line with -DNDEBUG swapped for
// -D_DEBUG. Both were run for this shard; neither is a standing test.
//
// Same shape and the same helpers as header_alias_test.cpp, which does this for
// lane H's headers.
//
//=============================================================================//

#include "gtest/gtest.h"

#include <sys/stat.h>

#include <cstddef>
#include <fstream>
#include <string>
#include <vector>

namespace
{

//-----------------------------------------------------------------------------
// Finding the tree, and reading it.
//-----------------------------------------------------------------------------
const char *SrcRoot()
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

std::string ReadFile( const std::string &strRelative )
{
	const std::vector< std::string > lines = ReadLines( strRelative );
	std::string out;
	for ( size_t i = 0; i < lines.size(); i++ )
		out += lines[ i ] + "\n";
	return out;
}

//-----------------------------------------------------------------------------
// COMMENTS AND STRING LITERALS ARE BOTH STRIPPED BEFORE ANY MATCH, and each
// removal is load-bearing rather than tidy.
//
// COMMENTS, because every arm in this shard explains itself, and the
// explanations name the thing that went: "CBaseEntity::Instance is a wrapper
// round GetContainingEntity" sits directly above the line that no longer says
// CBaseEntity::Instance. Matching raw text, the rule "no qualified static ask
// survives" reads the ask out of the sentence saying it was removed and reports
// the corrected file as wrong. header_alias_test.cpp records being caught by
// exactly this, and so was this file during development.
//
// STRING LITERALS, because cdll_client_int.cpp:2053 is
// `VPROF_("C_BaseEntity::Simulate", ...)` — a profiler NODE NAME. It is not a
// call, it does not compile to one, and the alias does not touch it. Left in,
// it is a permanent false positive that would have to be waived by name, and a
// waiver is how a real site later hides.
//-----------------------------------------------------------------------------
std::string StripCommentsAndStrings( const std::string &strIn )
{
	std::string out;
	const size_t n = strIn.size();
	size_t i = 0;
	while ( i < n )
	{
		if ( i + 1 < n && strIn[ i ] == '/' && strIn[ i + 1 ] == '*' )
		{
			const size_t j = strIn.find( "*/", i + 2 );
			i = ( j == std::string::npos ) ? n : j + 2;
			continue;
		}
		if ( i + 1 < n && strIn[ i ] == '/' && strIn[ i + 1 ] == '/' )
		{
			while ( i < n && strIn[ i ] != '\n' ) i++;
			continue;   // the '\n' itself is copied on the next turn
		}
		if ( strIn[ i ] == '"' )
		{
			size_t j = i + 1;
			while ( j < n && strIn[ j ] != '"' )
			{
				if ( strIn[ j ] == '\\' ) j++;
				j++;
			}
			i = ( j < n ) ? j + 1 : n;
			continue;
		}
		out += strIn[ i ];
		i++;
	}
	return out;
}

bool Contains( const std::string &hay, const char *needle )
{
	return hay.find( needle ) != std::string::npos;
}

size_t CountOf( const std::string &hay, const std::string &needle )
{
	size_t n = 0, at = 0;
	while ( ( at = hay.find( needle, at ) ) != std::string::npos ) { n++; at += needle.size(); }
	return n;
}

//-----------------------------------------------------------------------------
// One alias-conditional region, split into arms.
//
// THREE SPELLINGS, not one, and a scanner that knew only the first would read
// this shard as barely guarded at all:
//
//   #ifndef SS_CBASEENTITY_ALIAS          the SDK arm first
//   #ifdef  SS_CBASEENTITY_ALIAS          the armed arm first (short answers)
//   #if !defined( CLIENT_DLL ) && !defined( SS_CBASEENTITY_ALIAS )
//                                         COMPOUND -- a per-DLL arm that also
//                                         has to go when armed, which the two
//                                         shared files are full of
//-----------------------------------------------------------------------------
enum GuardShape_t { kGuardPlainNdef, kGuardPlainDef, kGuardCompound };

struct Guard_t
{
	std::string strFile;
	int nLine;
	GuardShape_t shape;
	bool bHasElse;
	bool bClosed;
	std::vector< std::string > unarmed;
	std::vector< std::string > armed;

	Guard_t() : nLine( 0 ), shape( kGuardPlainNdef ), bHasElse( false ), bClosed( false ) {}

	bool ArmedHasCode() const   { return HasCode( armed ); }
	bool UnarmedHasCode() const { return HasCode( unarmed ); }
	std::string ArmedText() const   { return Joined( armed ); }
	std::string UnarmedText() const { return Joined( unarmed ); }

private:
	static bool IsBlankOrComment( const std::string &L )
	{
		const std::string t = Trimmed( L );
		return t.empty() || ( t.size() >= 2 && t.compare( 0, 2, "//" ) == 0 );
	}
	static bool HasCode( const std::vector< std::string > &v )
	{
		for ( size_t i = 0; i < v.size(); i++ )
			if ( !IsBlankOrComment( v[ i ] ) ) return true;
		return false;
	}
	static std::string Joined( const std::vector< std::string > &v )
	{
		std::string s;
		for ( size_t i = 0; i < v.size(); i++ ) s += v[ i ] + "\n";
		return StripCommentsAndStrings( s );
	}
};

bool IsGuardOpen( const std::string &strLine, GuardShape_t *pShape, bool *pbArmedFirst )
{
	const std::string t = Trimmed( strLine );
	if ( t.empty() || t[ 0 ] != '#' )
		return false;
	if ( !Contains( t, "SS_CBASEENTITY_ALIAS" ) )
		return false;
	if ( t.compare( 0, 3, "#if" ) != 0 )
		return false;   // an #endif comment naming the macro is not an opening

	if ( t == "#ifndef SS_CBASEENTITY_ALIAS" )
	{
		*pShape = kGuardPlainNdef; *pbArmedFirst = false; return true;
	}
	if ( t == "#ifdef SS_CBASEENTITY_ALIAS" )
	{
		*pShape = kGuardPlainDef; *pbArmedFirst = true; return true;
	}
	// Compound. `!defined( SS_CBASEENTITY_ALIAS )` puts the SDK arm first;
	// `defined( SS_CBASEENTITY_ALIAS )` puts the armed one first.
	*pShape = kGuardCompound;
	*pbArmedFirst = !Contains( t, "!defined( SS_CBASEENTITY_ALIAS )" )
	             && !Contains( t, "!defined(SS_CBASEENTITY_ALIAS)" );
	return true;
}

bool IsAnyConditionalOpen( const std::string &L )
{
	const std::string t = Trimmed( L );
	return t.size() >= 3 && t.compare( 0, 3, "#if" ) == 0;
}
bool IsEndif( const std::string &L )
{
	const std::string t = Trimmed( L );
	return t.size() >= 6 && t.compare( 0, 6, "#endif" ) == 0;
}
bool IsElse( const std::string &L )
{
	const std::string t = Trimmed( L );
	return t == "#else" || ( t.size() >= 5 && t.compare( 0, 5, "#elif" ) == 0 );
}

std::vector< Guard_t > GuardsIn( const std::string &strFile )
{
	const std::vector< std::string > lines = ReadLines( strFile );
	std::vector< Guard_t > out;

	for ( size_t i = 0; i < lines.size(); i++ )
	{
		GuardShape_t shape = kGuardPlainNdef;
		bool bArmedFirst = false;
		if ( !IsGuardOpen( lines[ i ], &shape, &bArmedFirst ) )
			continue;

		Guard_t g;
		g.strFile = strFile;
		g.nLine = (int)i + 1;
		g.shape = shape;

		int nDepth = 0;
		bool bInElse = false;
		size_t j = i + 1;
		for ( ; j < lines.size(); j++ )
		{
			const std::string &L = lines[ j ];
			if ( IsAnyConditionalOpen( L ) ) { nDepth++; }
			else if ( IsEndif( L ) )
			{
				if ( nDepth == 0 ) { g.bClosed = true; break; }
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
// THE SIXTEEN FILES, WITH THEIR GUARD COUNTS. (Fifteen when shard T6 landed;
// Phase A4 added shell_debugoverlays.cpp, which is where two of
// gameinterface.cpp's guards went.)
//
// EXACT counts, not a floor, and listed per file rather than totalled. A floor
// only catches loss; an exact count also catches a guard appearing where nobody
// meant one, and naming the file means the failure says which. This tree has
// already recorded that a green suite does not prove a mechanical edit kept
// what it was supposed to keep.
//
// sceneentity_shared.cpp is deliberately ZERO. All three of its sites were
// CBaseEntity::IsServer(), which is a compile-time per-DLL constant, so its fix
// is a CLIENT_DLL constant and needs no alias arm at all. A file at zero is a
// result here, not an omission — and pinning it stops a future edit from
// "fixing" it with a guard it does not need.
//-----------------------------------------------------------------------------
// AN ANCHOR PER FILE, AND IT IS NOT DECORATION. A row whose count is ZERO is
// an ABSENCE assertion, and an absence assertion over a file that failed to
// open passes vacuously: GuardsIn() on an unreadable path answers an empty
// vector, which equals zero. Six of the rows below are zeros now, so every one
// of them is paired with a string that must be FOUND in the same file -- the
// positive control that says the scanner really read this file and not some
// other, or nothing at all.
//
// Each anchor is chosen out of code the alias flip does not touch and the
// residue wave could not evict: a member definition or a shim entry point that
// is live in the armed build. An anchor a change can delete is not an anchor.
struct FileGuards_t { const char *pszFile; size_t nGuards; const char *pszAnchor; };

//
// SIX STOCK ROWS LEFT THIS TABLE WITH THEIR FILES: c_te_projecteddecal.cpp (1),
// cdll_client_int.cpp (3), entity_tools_server.cpp (11), debugoverlay_shared.cpp
// (1), physics_saverestore.cpp (7) and saverestore.cpp (17) -- all 40 of the
// shard's remaining regions. Neither client.so nor server.so compiles them any
// more, so they are not in the source tree and the rows were culled. Every row
// left is a ZERO, pinned by its anchor.
const FileGuards_t kT6Files[] = {
	// 1 -> 0 BY plan_entity.md Phase 5 step 5e, AND ITS SUBJECT LEFT THE FILE.
	// The region wrapped SourceSharp_IsTheWorld's body, and it existed because
	// the arm read ClientEntityList().GetBaseEntity( 0 ) -- which armed answers
	// an IGameEntity *, not on the IHandleEntity chain. 5e took C_World off
	// C_BaseEntity, so that call answers NULL for the world on BOTH arms and
	// the world arm asks the singleton instead. There is nothing left for the
	// alias flip to change in this file, which is what a zero here means and
	// why it is pinned rather than dropped: a future edit must not "fix" it
	// with a guard it does not need.
	// (The row itself left with gametrace_client.cpp in C6, lane c6-input: the
	// file is out of the build, its callers ask SourceSharp_IsTheWorld.)
	// (recvproxy.cpp's zero row left with the file in C8, lane c6-input: it had
	// no importer on the link line.)
	// 1 -> 0 BY THE `residue` WAVE (74b071771..a46e8d645), AND THE SUBJECT WAS
	// DEAD SOURCE RATHER THAN A CAPABILITY.
	//
	// The region opened at game/client/view_beams.cpp:301 in 74b071771 as
	// `#ifndef SS_CBASEENTITY_ALIAS`, inside ComputeBeamEntPosition, and closed
	// at :353 on an `#else` arm of `return false;`. The armed configuration of
	// this build is SS_CBASEENTITY_ALIAS and SOURCESHARP BOTH DEFINED, and it is
	// the only configuration compiled -- so the unarmed arm (GetAttachment,
	// GetBaseAnimating, IsPlayer, WorldSpaceCenter, GetRenderOrigin, all off
	// C_BaseEntity) was never fed to a compiler here. The wave deleted the dead
	// arm and left the armed answer as the whole function; the object file did
	// not move, and neither did any symbol.
	//
	// SO A ZERO HERE IS NOT A LOST GUARD. It is "there is nothing left in this
	// file for the flip to change", the same thing gametrace_client.cpp's and
	// recvproxy.cpp's zeros say. The row is pinned rather than dropped for the
	// reason sceneentity_shared.cpp's zero is pinned: a later edit must not
	// re-introduce a guard this file does not need, and the anchor beside it is
	// what stops the zero passing on a file that did not open.
	// (The row itself left with view_beams.cpp at the C2 beam port: the file is
	// out of the build, its beam system is C#.)
	// 18 -> 20 by lane shards-a, shard 16, and it is two rather than the five
	// sites that shard closed BECAUSE four of them needed no guard: GetHealth
	// and the three FL_ writes went to IMovementPlayer, a base CBasePlayer
	// declares itself, which survives the alias. What is left is AddSolidFlags
	// (a deletion guard -- solidity is the collision property's and this
	// hierarchy has none) and EarPosition (a substitution guard -- EyePosition
	// is not the stand-in; its definition is in the struck baseplayer_shared.cpp).
	// 20 -> 18 by plan_gamedll_after.md Phase A4, and the two did NOT go away:
	// they went with DrawAllDebugOverlays to shell_debugoverlays.cpp, listed
	// below. The SUM is unchanged, which is the point -- an armed-site count is
	// not monotonic and is not an invariant on its own; where the guards ARE is.
	// 18 -> 7 by plan_gamedll_after.md Phase A9 step 1, and the eleven again did
	// NOT go away: they went with CServerGameEnts -- MarkEntitiesAsTouching,
	// BaseEntityToEdict and CheckTransmit's transmit walk -- to
	// shell_gameents.cpp, listed below. The SUM is unchanged again.
	// 7 -> 2 by A9 step 2, the same way: five went with CServerGameClients to
	// shell_gameclients.cpp, also listed below. The SUM is unchanged again.
	//
	// 2 -> 1 by A9 step 3, AND THIS ONE IS NOT A MOVE. Stock's CServerGameDLL
	// was deleted, and one of the two guards was inside it -- ServerActivate's
	// gEntList walk, which called Activate() on every CBaseEntity. There is no
	// new address for it, so the shard TOTAL falls by one for the first time.
	// A guard count is not monotonic and is not an invariant; what this table
	// pins is where the guards are, and a fall has to be explained rather than
	// absorbed. The one left is EndRestoreEntities' OnRestore walk.
	//
	// 1 -> 0, AND THE FILE IS GONE: A9 step 4 deleted gameinterface.cpp and the
	// listed below. A row of 0 for a file that does not exist would fail this
	// table's own "is missing or unreadable" assertion, so the row is REMOVED
	// and the successor's row carries the guard. The shard total is unchanged
	// across step 4, which is what says the last one moved rather than died.
	// gametrace_dll.cpp's row, at its NEW ADDRESS. plan_entity.md step 4a took
	// the stock file out of server_csharp.vpc and off disk; both its guards --
	// SourceSharp_IsTheWorld's and GetEntityIndex's -- moved with their
	// functions to sourcesharp/game/server/world_subject_dll.cpp. The row is
	// MOVED rather than removed-and-forgotten, so the shard SUM below is
	// unchanged, which is what says these two were relocated and not spent.
	//
	// 2 -> 1 BY LANE worldspawn, AND IT IS SourceSharp_IsTheWorld's THAT WENT.
	// That region's armed arm was `return false` -- not a parallel spelling of
	// the same answer, which is what every other region in this table is, but a
	// DIFFERENT ANSWER. Its premise ("armed, CBaseEntity is IGameEntity, which
	// is NOT on the IHandleEntity chain") was about a class the alias does not
	// touch: CWorld came off CBaseEntity in plan_cworld_trace.md T3 and is
	// `public IServerEntity` in BOTH builds, so the cast the arm was avoiding is
	// legal armed -- shell_entityslots.cpp makes the identical one on a CWorld *
	// in an armed TU. Removing it is a BUG FIX and not the residue wave: the
	// guarded arm was wrong, rather than dead.
	//
	// world_subject_test.cpp now pins the absence, so the region cannot come
	// back without a red test rather than only a moved number here.
	// 1 -> 0 BY `the-close` 1/N (f288a5e11), which owned
	// src/sourcesharp/** the way `residue` owned the stock tree. server_csharp.vpc
	// compiles the shim with SS_CBASEENTITY_ALIAS and SOURCESHARP defined and
	// this project builds nothing else, so the `#ifndef` side it deleted is the
	// side the preprocessor was already discarding: 588 objects before and
	// after, identical DEFINED and UNDEFINED symbol sets, 0 differing.
	//
	// The one region was CGameTrace::GetEntityIndex's `TraceEntity( *this )`
	// walk. TraceEntity IS ITSELF DELETED NOW (`the-close` 4/N): it bottomed
	// out in IServerUnknown/IClientUnknown::GetBaseEntity(), and all eight
	// implementations of that in the two DLLs are `mov $0x0,%eax; ret`, so the
	// arm asked a constant NULL for an entindex. TraceGameEntity is what asks
	// the question now, through the virtual GetGameEntity(), and it exists on
	// both sides.
	{ "sourcesharp/game/server/world_subject_dll.cpp",       0, "SourceSharp_IsTheWorld" },
	// 4 -> 0 BY THE `residue` WAVE (74b071771..a46e8d645), SAME RULING AS
	// view_beams.cpp ABOVE AND FOR THE SAME REASON.
	//
	// The four regions opened at game/shared/particlesystemquery.cpp:175, :229,
	// :432 and :505 in 74b071771, every one of them `#ifndef
	// SS_CBASEENTITY_ALIAS` -- MovePointInsideControllingObject,
	// GetRandomPointsOnControllingObjectHitBox, GetControllingObjectHitBoxInfo
	// and IsPointInControllingObjectHitBox, the four queries that reached a
	// controlling object through C_BaseEntity. None of those arms was ever
	// compiled: the armed configuration is the only one built, and armed the
	// `#ifndef` arm is the side that is thrown away. The wave deleted them and
	// the armed answers are now the whole of each function.
	//
	// The row went with the file: particlesystemquery.cpp left the client with
	// lane c5-particles (the particle query is C#'s), so there is nothing left
	// to hold at zero.
	// (sceneentity_shared.cpp's zero row left with the file in C8, lane c6-input.)
	// The two guards A4 moved out of gameinterface.cpp with DrawAllDebugOverlays
	// -- FindPickerEntity's answer and CAI_Hint's owner, both C-cast to
	// CBaseEntity. debugoverlays_move_test.cpp counts them at their new address
	// too; this list is what keeps them inside T6's total.
	// 2 -> 0 by plan_entity.md Phase 4 step 4c, and this one is a REAL FALL
	// rather than a move: both regions were DELETED WITH THEIR SUBJECT and
	// neither turned up in another file. The first guarded the picker overlay's
	// SDK arm, whose FindPickerEntity has answered NULL since 13c; the second
	// guarded §3.16 FAMILY 6, the AI hint node's owner turned into an entity
	// through a void pointer. Both sat inside `if (engine->IsInEditMode())`,
	// which opened with g_pAINetworkManager->GetEditOps() and no null test --
	// and shell_ai_absent.cpp defines that pointer NULL, so the block did not
	// draw the wrong thing, it dereferenced NULL. What replaces the first for
	// the parallel hierarchy is SourceSharp_DrawEntityOverlay, which draws for a
	// HANDLE and is recorded in the managed ContractBaseline with the second.
	//
	// A file at zero is pinned rather than dropped from the list, for
	// sceneentity_shared.cpp's reason: it stops a later edit "fixing" it with a
	// guard it does not need.
	{ "sourcesharp/game/server/shell_debugoverlays.cpp",     0, "DrawAllDebugOverlays" },
	// The eleven guards A9 step 1 moved out of gameinterface.cpp with
	// CServerGameEnts. gameinterface_split_test.cpp counts them at their new
	// address too, with a needle that carries its newline; this list is what
	// keeps them inside T6's total.
	// 11 -> 0 BY `the-close` 1/N (f288a5e11), which owned
	// src/sourcesharp/** the way `residue` owned the stock tree. server_csharp.vpc
	// compiles the shim with SS_CBASEENTITY_ALIAS and SOURCESHARP defined and
	// this project builds nothing else, so the `#ifndef` side it deleted is the
	// side the preprocessor was already discarding: 588 objects before and
	// after, identical DEFINED and UNDEFINED symbol sets, 0 differing.
	//
	// TheDebugOnlyAsksAreRerouted below carries the one of the eleven that had
	// a fact of its own -- CheckTransmit's Assert pair, now a single
	// unconditional assertion -- and states the absence of the disarmed half
	// beside the presence of the armed one.
	{ "sourcesharp/game/server/shell_gameents.cpp",          0, "SourceSharp_SkyboxAreaForClient" },
	// The five A9 step 2 moved out of gameinterface.cpp with CServerGameClients:
	// ClientActive's PostClientActive walk, ClientDisconnect's AddSolidFlags,
	// ClientSetupVisibility, GetPlayerState and GetBugReportInfo.
	//
	// 5 -> 4 BY plan-audit C5, AND IT IS A MOVE. ClientEarPosition's SDK read
	// became a step in shell_clientsteps.cpp, listed below, and the guard went
	// WITH THE CALL -- it exists because CBasePlayer::EarPosition is defined in
	// the struck baseplayer_shared.cpp, so a guard left here would be guarding
	// a call that is not here. The SUM below is unchanged, which is what says
	// this was a move and not a guard being spent.
	// 4 -> 0 BY `the-close` 1/N (f288a5e11), which owned
	// src/sourcesharp/** the way `residue` owned the stock tree. server_csharp.vpc
	// compiles the shim with SS_CBASEENTITY_ALIAS and SOURCESHARP defined and
	// this project builds nothing else, so the `#ifndef` side it deleted is the
	// side the preprocessor was already discarding: 588 objects before and
	// after, identical DEFINED and UNDEFINED symbol sets, 0 differing.
	//
	// The four were ClientActive's `gEntList.FirstEnt()` PostClientActive walk,
	// ClientDisconnect's `player->AddSolidFlags( FSOLID_NOT_SOLID )`,
	// ClientSetupVisibility's `pVE->EyePosition()` and GetPlayerState's. The
	// same commit also took this file's two `#ifndef SOURCESHARP` stock arms,
	// which is why kStockArmInstanceCount below is now zero and the rule that
	// used to except this file is universal.
	{ "sourcesharp/game/server/shell_gameclients.cpp",       0, "SourceSharp_ClientViewOrigin" },
	// The one plan-audit C5 moved out of shell_gameclients.cpp with
	// ClientEarPosition's SDK read. Its armed arm answers 0 rather than a
	// stand-in position: UTIL_SDKPlayerFromEdict answers NULL for every edict
	// in this build, and a step that cannot answer says so rather than
	// inventing a position for the sound listener.
	// 1 -> 0 BY `the-close` 1/N (f288a5e11), which owned
	// src/sourcesharp/** the way `residue` owned the stock tree. server_csharp.vpc
	// compiles the shim with SS_CBASEENTITY_ALIAS and SOURCESHARP defined and
	// this project builds nothing else, so the `#ifndef` side it deleted is the
	// side the preprocessor was already discarding: 588 objects before and
	// after, identical DEFINED and UNDEFINED symbol sets, 0 differing.
	//
	// The region was SourceSharp_ClientsSdkPlayerEarPosition's
	// `pPlayer->EarPosition()` read. The armed answer it selected -- 0, "this
	// step cannot answer" -- is what the function does unconditionally now, and
	// that is the point the original note made: a step that cannot answer says
	// so rather than inventing a position for the sound listener. The anchor is
	// unchanged and is now the only thing separating "no regions" from "no
	// file".
	{ "sourcesharp/game/server/shell_clientsteps.cpp",       0, "SourceSharp_ClientsStandalonePlayerEarPosition" },
	// The last one out of gameinterface.cpp, with EndRestoreEntities at A9
	// step 4. Its ledger KEY changed (d00e8845 -> 598e8514) because the restore
	// list stopped being a file static and the guard's first body line reads
	// s_Restored.List()[i] -- a body edit re-keys deliberately. The guarded
	// TEXT is the same two calls, IsDormant() and OnRestore().
	// 1 -> 3, armed-link: the two SDK delete-list drains on the save-restore path
	// are guarded. The path is dead -- BeginRestoreEntities has no caller -- but
	// the bodies are kept whole because a managed save/load comes back in this
	// shape, so the calls are guarded rather than deleted.
	// 3 -> 0 BY `the-close` 1/N (f288a5e11), which owned
	// src/sourcesharp/** the way `residue` owned the stock tree. server_csharp.vpc
	// compiles the shim with SS_CBASEENTITY_ALIAS and SOURCESHARP defined and
	// this project builds nothing else, so the `#ifndef` side it deleted is the
	// side the preprocessor was already discarding: 588 objects before and
	// after, identical DEFINED and UNDEFINED symbol sets, 0 differing.
	//
	// The three were the `s_Restored` walk's `pEntity->IsDormant()` /
	// `OnRestore()` pair and TWO `gEntList.CleanupDeleteList()` calls. The
	// delete list is the SDK entity list's own and `gEntList` is a gate-2 name
	// this port holds at zero, so there is no successor call to point at --
	// what is left is the walk with an empty body and the printed refusal at
	// the tail, which is §7's shape rather than a silent no-op.
	//
	// THE ANCHOR HAD TO CHANGE WITH THE COUNT. `SourceSharp_ResolveEntity` was
	// the anchor, and it was named ONLY inside the deleted arm -- so it went
	// with it, and the row would have read "this file does not exist" rather
	// than "this file has no regions". `EndRestoreEntities` is the function the
	// three regions lived in and it is still here.
	// shell_restoreentities.cpp's row left with the file
	// (plan_server_native_min P1): the restore path is deleted whole.
	// 31 -> 30 (plan_cworld_trace.md T3). SourceSharp_EntityGetClassName held a
	// guard around its own copy of the two hierarchy arms; it asks
	// SourceSharpEntity_t::Classname() now, which is the projection that already
	// knew the answer -- and the guard went with the copy rather than being
	// deleted from a live arm. It had to: the projection gained a world arm and
	// this duplicate did not, so every enumeration reported worldspawn with an
	// empty classname.
	//
	// 30 -> 33 (lane f-srvshim). THREE ADDED, AND A RISE IS THE ORDINARY CASE
	// for this file rather than a defect: `guard-count-is-not-an-invariant`, and
	// the fall recorded above is the unusual half. The three are
	// SourceSharp_EntitySpawn's `DispatchSpawn( ent.pBase )`,
	// SourceSharp_EntityRemove's `UTIL_Remove( ent.pBase )` and
	// SourceSharp_TemplateSpawnInstance's `SpawnHierarchicalList(...)` -- each an
	// SDK arm that is UNREACHABLE once the alias is armed, because ent.pBase
	// comes from GetBaseEntity() and armed there is no SDK entity to name.
	// Each has a complete parallel arm already beside it: ent.pStandalone->Spawn(),
	// CStandaloneEntity::Remove, and StandaloneEntity_PlaceAndSpawn, which places
	// AND spawns each standalone template before SpawnHierarchicalList runs.
	// No capability goes with any of the three; what goes is the shim's last
	// three asks of a hierarchy that has left.
	// The third of the three went out of the tree entirely on 2026-10-07:
	// ShimSlotsTests' router walk found SourceSharp_TemplateSpawnInstance
	// generated-but-never-called since plan_server_novalve P5, and the whole
	// refused slot -- body, arms and all -- was cut with its rows.
	// 34 -> 35, armed-link: one #ifdef region supplying the three stock removal
	// spellings to the armed build. Their only definitions were in util.cpp,
	// which the flip evicts; unarmed that file still defines them, so this arm
	// is armed-only by necessity and not by preference.
	// 35 -> 0 BY `the-close` 1/N (f288a5e11), which owned
	// src/sourcesharp/** the way `residue` owned the stock tree. server_csharp.vpc
	// compiles the shim with SS_CBASEENTITY_ALIAS and SOURCESHARP defined and
	// this project builds nothing else, so the `#ifndef` side it deleted is the
	// side the preprocessor was already discarding: 588 objects before and
	// after, identical DEFINED and UNDEFINED symbol sets, 0 differing.
	//
	// THIRTY-FIVE IS THE LARGEST FALL IN THIS TABLE AND EVERY ONE OF THEM WAS
	// THE SAME SHAPE: an `if ( ent.pBase ) ... ent.pBase->Something()`
	// fall-through, reached only when SourceSharp_ResolveEntity had NOT
	// answered on `pStandalone`. `ent.pBase` comes from GetBaseEntity(), which
	// returns a constant NULL in both DLLs, so control fell past every one of
	// them on every armed call.
	//
	// TheShimsSdkFallThroughsAreGone below is the successor fact, and it is one
	// clause covering all thirty-five and any thirty-sixth.
	{ "sourcesharp/game/server/shell_entityapi.cpp",         0, "SourceSharp_RefHandleFromEntity" },
	// 1 -> 0 BY `the-close` 1/N (f288a5e11), which owned
	// src/sourcesharp/** the way `residue` owned the stock tree. server_csharp.vpc
	// compiles the shim with SS_CBASEENTITY_ALIAS and SOURCESHARP defined and
	// this project builds nothing else, so the `#ifndef` side it deleted is the
	// side the preprocessor was already discarding: 588 objects before and
	// after, identical DEFINED and UNDEFINED symbol sets, 0 differing.
	//
	// The region was SourceSharp_OnRespawn's `pEntity->entindex()` arm around
	// the managed OnRespawn call. Its armed side was empty, so what the
	// deletion leaves is the answer the guard was already selecting.
	{ "sourcesharp/game/shared/sourcesharp_gamesystem.cpp",  0, "SourceSharp_FatalInitFailure" },
};
const size_t kT6FileCount = sizeof( kT6Files ) / sizeof( kT6Files[ 0 ] );

//-----------------------------------------------------------------------------
// THE TWO QUALIFIED ASKS THAT DELIBERATELY STAYED, AND NOW DO NOT.
//
// They were gameinterface.cpp's until plan_gamedll_after.md Phase A9 step 2 and
// shell_gameclients.cpp's after it, in ClientSettingsChanged and ClientVoice:
// two `CBaseEntity::Instance( pEdict )` calls, both inside `#ifndef
// SOURCESHARP`, the STOCK arm this mod never compiles. The allowance existed
// because rewriting dead stock code would grow the C++ diff for nothing, which
// is the opposite of this port's rule.
//
// 2 -> 0 BY `the-close`, AND BY BOTH HALVES OF IT AT ONCE, which is why the
// allowance is not merely unused but unwritable:
//
//   f288a5e11 deleted the two `#ifndef SOURCESHARP` arms with the shim's other
//   238, on the ruling that both vpcs define SOURCESHARP for this TU and no
//   other configuration is built. What the arms held -- ClientSettingsChanged
//   fetching a CBasePlayer off the edict and forwarding to the game rules, and
//   ClientVoice's twin -- was never compiled here; the edict holds a standalone
//   player, never a CBasePlayer, which the arm's own comment said.
//
//   And `CBaseEntity::Instance` IS ITSELF DELETED (`the-close` 4/N and 5/N).
//   It, EntityFromEntityHandle, TraceEntity and GetContainingEntity all bottom
//   out in IServerUnknown/IClientUnknown::GetBaseEntity(), and all eight
//   implementations of that in the two DLLs are `mov $0x0,%eax; ret`. So there
//   is no function of that name for a stock arm to call back.
//
// THE RULE BELOW IS THEREFORE UNIVERSAL NOW, and that is strictly stronger than
// it was: every file in kT6Files holds ZERO qualified entity statics, with no
// excepted file and no allowance that has to be argued for. The exception is
// kept as a NAMED ZERO rather than deleted so that a later edit putting an
// `Instance` call back has to change this constant on purpose.
//-----------------------------------------------------------------------------
const size_t kStockArmInstanceCount = 0;

} // namespace


//-----------------------------------------------------------------------------
// 1. EVERY GUARD IS WELL FORMED, AND THE COUNTS ARE EXACTLY WHAT LANDED.
//-----------------------------------------------------------------------------
TEST( CppAliasT6, EveryGuardIsWellFormedAndTheCountsAreExact )
{
	ASSERT_TRUE( SrcRoot() != NULL ) << "cannot find the source tree from the cwd";

	for ( size_t i = 0; i < kT6FileCount; i++ )
	{
		const std::string f = kT6Files[ i ].pszFile;
		const std::vector< std::string > lines = ReadLines( f );
		ASSERT_FALSE( lines.empty() ) << f << " is missing or unreadable";

		// THE POSITIVE CONTROL THAT MAKES A ZERO ROW MEAN SOMETHING. Six rows
		// below expect zero regions, and `GuardsIn()` answers zero for a file
		// it could not read just as readily as for a file that has none. So the
		// anchor has to be found in the same text before the count is believed.
		std::string strAll;
		for ( size_t n = 0; n < lines.size(); n++ ) strAll += lines[ n ] + "\n";
		ASSERT_TRUE( Contains( strAll, kT6Files[ i ].pszAnchor ) )
			<< f << " does not contain its anchor '" << kT6Files[ i ].pszAnchor
			<< "' — the scanner opened something, but not this file, so the "
			<< "count below would be a statement about nothing.";

		const std::vector< Guard_t > guards = GuardsIn( f );

		EXPECT_EQ( guards.size(), kT6Files[ i ].nGuards )
			<< f << " — the number of alias-conditional regions changed. They may "
			<< "change on purpose; they may not change by accident. If a guard was "
			<< "added or removed deliberately, move the count in kT6Files with it.";

		for ( size_t g = 0; g < guards.size(); g++ )
		{
			const Guard_t &G = guards[ g ];
			const std::string at = f + ":" + std::to_string( G.nLine );

			EXPECT_TRUE( G.bClosed )
				<< at << " — an alias guard with no #endif. This is the merge-splice "
				<< "failure: it compiles unarmed and takes the wrong arm armed.";

			// THE RULE HAS TWO POLARITIES NOW, and it used to have one.
			//
			// An `#ifndef` guard DELETES when armed, so its unarmed arm must
			// carry code -- a guard around nothing deletes nothing, which is the
			// original rule and is unchanged.
			//
			// An `#ifdef` guard is the mirror: it SUPPLIES when armed. That shape
			// arrived with the armed link, where a body whose only definition was
			// in a translation unit the flip evicts has to be defined somewhere
			// the armed build still compiles -- and defining it in BOTH
			// configurations would be two definitions of one symbol at two
			// addresses, which this port has a scar from. So for `#ifdef` the arm
			// that must carry code is the ARMED one, and an empty unarmed arm is
			// the whole point rather than a defect.
			//
			// Checked by SHAPE and not by "either arm has something", because
			// that weaker reading passes for a guard with both arms empty.
			if ( G.shape == kGuardPlainDef )
			{
				EXPECT_TRUE( G.ArmedHasCode() )
					<< at << " — an #ifdef alias guard whose ARMED arm carries no "
					<< "code. That shape supplies a definition to the armed build; "
					<< "one that supplies nothing is a guard around nothing.";
			}
			else
			{
				EXPECT_TRUE( G.UnarmedHasCode() )
					<< at << " — the unarmed arm carries no code. A guard around nothing "
					<< "deletes nothing when armed and is not an answer to the flip.";
			}

			if ( G.bHasElse )
			{
				EXPECT_TRUE( G.ArmedHasCode() )
					<< at << " — the armed arm is empty or comment-only. If the armed "
					<< "answer really is 'do nothing', the guard should have no #else "
					<< "at all rather than an #else that says nothing.";
			}
		}
	}
}

//-----------------------------------------------------------------------------
// 1b. THE PREPROCESSOR NESTING BALANCES, PER FILE.
//
// THIS TEST EXISTS BECAUSE THE ONE ABOVE COULD NOT FAIL ON THE FAILURE IT WAS
// WRITTEN FOR. Deleting a guard's `#endif` and running the mutation harness
// left the whole suite green, and the reason is a property of any scanner
// shaped like GuardsIn: with its own `#endif` gone, a guard simply consumes the
// NEXT one, reports bClosed == true, and the opening count is unchanged because
// openings are what is counted. Every arm below it shifts by one region and
// nothing says so.
//
// Counting `#if*` against `#endif` over the whole file catches it immediately,
// and catches it wherever it happens rather than only inside an alias guard.
// It is a weaker statement about the guards and a much stronger one about the
// file, which is the right trade for this failure: a lost `#endif` is a merge
// artefact, and a merge does not aim.
//-----------------------------------------------------------------------------
TEST( CppAliasT6, EveryFilesPreprocessorNestingBalances )
{
	ASSERT_TRUE( SrcRoot() != NULL );

	for ( size_t i = 0; i < kT6FileCount; i++ )
	{
		const std::string f = kT6Files[ i ].pszFile;
		const std::vector< std::string > lines = ReadLines( f );
		ASSERT_FALSE( lines.empty() ) << f << " is missing or unreadable";

		int nOpen = 0, nClose = 0, nDepth = 0, nWentNegative = 0;
		for ( size_t j = 0; j < lines.size(); j++ )
		{
			if ( IsAnyConditionalOpen( lines[ j ] ) ) { nOpen++; nDepth++; }
			else if ( IsEndif( lines[ j ] ) )
			{
				nClose++; nDepth--;
				if ( nDepth < 0 ) nWentNegative++;
			}
		}

		EXPECT_EQ( nOpen, nClose )
			<< f << " — " << nOpen << " #if directives and " << nClose
			<< " #endif. A guard has lost its #endif (or gained one). The scanner "
			<< "above cannot see this on its own: a guard whose #endif is missing "
			<< "silently takes the NEXT one and still reports as closed.";

		EXPECT_EQ( nWentNegative, 0 )
			<< f << " — an #endif with no matching #if, " << nWentNegative
			<< " time(s).";

		EXPECT_EQ( nDepth, 0 ) << f << " — conditionals left open at end of file";
	}
}

//-----------------------------------------------------------------------------
// 4. THE SITES NO RELEASE SURVEY CAN SEE.
//-----------------------------------------------------------------------------
TEST( CppAliasT6, TheDebugOnlyAsksAreRerouted )
{
	ASSERT_TRUE( SrcRoot() != NULL );

	// The four client Assert bodies this used to pin were in
	// cdll_client_int.cpp, which client.so no longer compiles and which has
	// left the source tree; that table was culled with it.
	//
	// The CheckTransmit assertion, which is the same hazard in its other shape:
	// the ask is guarded rather than rerouted, because IsPlayer() is a real
	// virtual on a real subject and there is nothing to reroute it to.
	//
	// It moved with CheckTransmit to shell_gameents.cpp at Phase A9 step 1.
	//
	// AND ITS SPELLING CHANGED AT plan-audit C4, which turned this fact red for
	// the work being done RIGHT -- the fourth time that shape has been recorded
	// in this tree. The armed arm read `Assert( bStandaloneRecipient );`, and
	// bStandaloneRecipient was a LOCAL of the `#ifdef SOURCESHARP` disjunction
	// C4 moved into C#: the local is gone, so an anchor naming it could only be
	// kept by keeping a variable nothing writes.
	//
	// THE PROPERTY IS UNCHANGED and is what the new anchor says. Armed, the SDK
	// half cannot be true, so the assertion is "a recipient was found AND it
	// came from the other hierarchy" -- exactly what `bStandaloneRecipient`
	// meant, with the two facts now arriving from the managed answer and from
	// the SDK pointer rather than from one bool. Dropping the Assert entirely
	// would still lose the check, which is the thing this fact is for.
	const std::string gi = StripCommentsAndStrings( ReadFile( "sourcesharp/game/server/shell_gameents.cpp" ) );
	EXPECT_EQ( CountOf( gi, "Assert( bHaveRecipient && !pRecipientEntity );" ), (size_t)1 )
		<< "shell_gameents.cpp — CheckTransmit's armed assertion is gone. Armed the "
		<< "SDK half of its disjunction cannot be true, and the standalone half is "
		<< "the whole assertion; dropping the Assert entirely would lose the check.";

	// THE DISARMED ARM IS GONE, AND THAT IS THE WHOLE PAIR COLLAPSING RATHER
	// THAN HALF OF IT BEING LOST.
	//
	// The clause that stood here required BOTH halves of an `#ifndef` /
	// `#else` pair -- the armed `Assert( bHaveRecipient && !pRecipientEntity );`
	// and the disarmed `Assert( bHaveRecipient && ( !pRecipientEntity ||
	// pRecipientEntity->IsPlayer() ) );` -- on the reasoning that an edit which
	// took the disarmed half away would leave the armed one passing on an
	// assertion nobody compiles.
	//
	// `the-close` 1/N (f288a5e11) deleted the disarmed half with the shim's
	// other 239 dead arms. The reasoning above does not survive the deletion,
	// because there is no longer an unarmed configuration for it to be about:
	// server_csharp.vpc defines SS_CBASEENTITY_ALIAS and SOURCESHARP for this
	// TU, that is the only configuration built, and the assertion that runs in
	// it is the one that is left. What the disarmed half checked --
	// "an SDK recipient which exists is a player" -- is checked more strongly
	// by what replaced it: armed there is no SDK recipient AT ALL, and
	// `!pRecipientEntity` says exactly that.
	//
	// SO THE SUCCESSOR IS A PAIR, and both halves are needed. The absence alone
	// would pass over a file with no Assert in it; the presence alone would
	// pass over a file that grew the pair back.
	EXPECT_EQ( CountOf( gi, "pRecipientEntity->IsPlayer()" ), (size_t)0 )
		<< "shell_gameents.cpp — CheckTransmit asks an SDK recipient for its "
		<< "type again. Armed there is no SDK recipient: pRecipientEntity is "
		<< "initialised NULL and nothing assigns it, which is what the surviving "
		<< "assertion states.";
	EXPECT_EQ( CountOf( gi, "IGameEntity *pRecipientEntity = NULL;" ), (size_t)1 )
		<< "shell_gameents.cpp — the NULL the surviving assertion is about is "
		<< "gone, so the assertion above is a statement about nothing.";
}


//-----------------------------------------------------------------------------
// 4b. THE SHIM'S SDK FALL-THROUGHS, WHICH ARE WHAT FORTY-SIX OF THIS TABLE'S
//     DELETED REGIONS WERE.
//
// shell_entityapi.cpp lost 35 alias regions and shell_scripted.cpp -- which
// cpp_alias_t5_test.cpp holds -- lost 10, and every single one was the same
// two lines:
//
//     if ( ent.pBase )
//         return ent.pBase->Something();
//
// reached only when `SourceSharp_ResolveEntity` had NOT answered on
// `pStandalone`. `SourceSharpEntity_t::pBase` is filled from
// `IServerUnknown::GetBaseEntity()`, and every implementation of that in
// server.so is `mov $0x0,%eax; ret` -- so control fell past all forty-five of
// them on every call this build has ever made.
//
// A COUNT ROW FALLING 35 -> 0 IS NOT A CHECK, which is the whole reason this
// fact exists. `ent.pBase->` is the one spelling all thirty-five shared, so the
// absence of it is one clause that covers every one of them and any thirty-sixth
// somebody writes.
//
// THE ONE THAT IS NOT ZERO IS THE POINT OF THE OTHER HALF.
// SourceSharp_EntityGetCollisionGroup keeps a live `ent.pBase->GetCollisionGroup()`
// in the `else if` of an unguarded chain -- it never had a guard, so the wave
// had nothing to delete. Pinning it at exactly one rather than at zero is what
// stops this fact being satisfied by someone deleting the live branch too, and
// it is what stops the clause being written as a lazy zero that happens to hold.
//-----------------------------------------------------------------------------
TEST( CppAliasT6, TheShimsSdkFallThroughsAreGone )
{
	ASSERT_TRUE( SrcRoot() != NULL );

	const std::string api =
		StripCommentsAndStrings( ReadFile( "sourcesharp/game/server/shell_entityapi.cpp" ) );
	ASSERT_FALSE( api.empty() ) << "shell_entityapi.cpp is missing or unreadable";

	// PRESENCE FIRST. Everything below is a count, and a count over a file that
	// did not open is zero as readily as a count over a file that is right.
	// 40 at ABI 252, after the name walk left; the floor fell 30 -> 10 when
	// plan_server_native_v2 T4d began moving this file's slots to managed.
	EXPECT_LT( (size_t)10, CountOf( api, "SourceSharp_ResolveEntity(" ) )
		<< "shell_entityapi.cpp no longer resolves handles, so the counts below "
		<< "are about nothing";

	// THE ABSENCE: exactly one live SDK fall-through left, and it is the one
	// that never had a guard. Thirty-five is what the wave took.
	EXPECT_EQ( CountOf( api, "ent.pBase->" ), (size_t)1 );
	EXPECT_EQ( CountOf( api, "ent.pBase->GetCollisionGroup()" ), (size_t)1 )
		<< "the one surviving SDK fall-through is not the one this fact allows. "
		<< "It is SourceSharp_EntityGetCollisionGroup's `else if`, which was "
		<< "never guarded and so was never the wave's to delete.";

	// THE SUCCESSOR. Every deleted arm's question is answered on the parallel
	// hierarchy by the branch that ran first in every armed call anyway.
	// Floor 40 -> 10 with T4d's moves (the file shrinks; what this holds is
	// that the standalone arm is still the one asked).
	EXPECT_LT( (size_t)10, CountOf( api, "ent.pStandalone" ) )
		<< "shell_entityapi.cpp has stopped asking the standalone hierarchy, "
		<< "which is what the deleted arms were falling through FROM";
}

//-----------------------------------------------------------------------------
// 5. THE TWO ASKS THAT STAY, STAY — AND NOTHING ELSE DOES.
//-----------------------------------------------------------------------------
TEST( CppAliasT6, OnlyTheStockArmKeepsAQualifiedEntityStatic )
{
	ASSERT_TRUE( SrcRoot() != NULL );

	size_t nSurviving = 0;
	for ( size_t i = 0; i < kT6FileCount; i++ )
	{
		const std::string f = kT6Files[ i ].pszFile;
		const std::string code = StripCommentsAndStrings( ReadFile( f ) );
		ASSERT_FALSE( code.empty() ) << f << " is missing or unreadable";

		// BOTH SPELLINGS, explicitly. `CBaseEntity` is NOT a substring of
		// `C_BaseEntity` — the underscore — and a rule written on one of them
		// silently measures nothing on the other side. That mistake is recorded
		// in this port more than once.
		const size_t nServer = CountOf( code, "CBaseEntity::" );
		const size_t nClient = CountOf( code, "C_BaseEntity::" );
		// "CBaseEntity::" is a substring of "C_BaseEntity::"? No — but
		// "CBaseEntity::" IS a substring of nothing else here, and the client
		// spelling contains no occurrence of the server one. Counted separately
		// on purpose so neither can hide the other.
		const size_t n = nServer + nClient;

		// plan_gamedll_after.md Phase A9 step 2 moved BOTH stock-arm calls out
		// of gameinterface.cpp with CServerGameClients -- ClientSettingsChanged
		// and ClientVoice. The allowance follows the code, not the filename, and
		// gameinterface.cpp is held at ZERO by the general rule below, which is
		// what says the move was a move.
		if ( f == std::string( "sourcesharp/game/server/shell_gameclients.cpp" ) )
		{
			// THE ALLOWANCE IS ZERO NOW, and the clause is kept rather than
			// dropped because a named zero is a check and a deleted branch is a
			// hole: an `Instance` call reappearing in this file has to move
			// kStockArmInstanceCount, deliberately, in front of a reader.
			EXPECT_EQ( n, kStockArmInstanceCount )
				<< f << " — a qualified entity static survives here. The two "
				<< "`CBaseEntity::Instance( pEdict )` calls this file used to be "
				<< "excepted for went with their `#ifndef SOURCESHARP` arms, and "
				<< "CBaseEntity::Instance itself is deleted.";
			EXPECT_EQ( CountOf( code, "CBaseEntity::Instance" ), kStockArmInstanceCount )
				<< f << " — CBaseEntity::Instance is named again. It bottoms out "
				<< "in IServerUnknown::GetBaseEntity(), which is `mov $0x0,%eax; "
				<< "ret` in server.so, so there is nothing for it to answer.";

			// THE PRESENCE HALF, and without it the two clauses above pass over
			// any file at all -- including one that failed to open, which this
			// tree has shipped more than forty times. Both functions the
			// deleted stock arms lived in are STILL HERE and still reached: one
			// keeps the spawn check that was the arm's own precondition, the
			// other declares its parameter unused, which is what makes "this
			// build does nothing here" written down rather than implied.
			EXPECT_TRUE( Contains( code, "void CServerGameClients::ClientSettingsChanged( edict_t *pEdict )" ) )
				<< f << " — ClientSettingsChanged is gone, so the absence clauses "
				<< "above are statements about nothing.";
			EXPECT_TRUE( Contains( code, "void CServerGameClients::ClientVoice( edict_t *pEdict )" ) )
				<< f << " — ClientVoice is gone, so the absence clauses above are "
				<< "statements about nothing.";

			nSurviving += n;
			continue;
		}

		EXPECT_EQ( n, (size_t)0 )
			<< f << " — a qualified entity static survives in code. Every one of "
			<< "this shard's was either rerouted onto its free function or, where "
			<< "there was no free function, answered by the CLIENT_DLL constant.";
	}

	EXPECT_EQ( nSurviving, kStockArmInstanceCount );
}

//-----------------------------------------------------------------------------
// 6. The IsServer() asks became compile-time constants, not guards.
//-----------------------------------------------------------------------------
TEST( CppAliasT6, TheIsServerAsksBecameACompileTimeConstant )
{
	ASSERT_TRUE( SrcRoot() != NULL );

	// The client half left with sceneentity_shared.cpp (plan_client_novalve C8,
	// lane c6-input): it had no importer, and its two ConVars are managed. What
	// stays is the server half.
	// gameinterface.cpp is a server-only TU, so its three IsServer() calls
	// folded to the literal `true` rather than to a constant.
	//
	// AND ONE OF THE THREE WAS DELETED at plan_gamedll_after.md Phase A9 step 3:
	// LoadMessageOfTheDay was a private method of stock's CServerGameDLL and
	// went with the class, so the fold it held went with it. The sum is TWO. A
	// folded literal cannot be moved back to a call by accident; it can only be
	// lost with its code, which is what happened, and the count says so rather
	// than absorbing it.
	//
	// TWO OF THE THREE MOVED at plan_gamedll_after.md Phase A4:
	// PrecacheMaterial and PrecacheParticleSystem went to shell_stringtables.cpp
	// with the four other string-table accessors. The folded literal is what
	// this fact is about and it is not about which file holds it, so the count
	// is taken over BOTH files summed and stays at three. Splitting it 1 and 2
	// would pin the split rather than the fold, and the next move would break it
	// again for no reason.
	// gameinterface.cpp itself was deleted at A9 step 4, so the sum is taken
	// over the one file that still holds folded literals. shell_gameclients.cpp
	// is read too, because it is where the twenty per-player bodies went and is
	// the file a re-introduced IsServer() would most plausibly land in.
	const std::string gi = StripCommentsAndStrings( ReadFile( "sourcesharp/game/server/shell_gameclients.cpp" ) );
	const std::string st = StripCommentsAndStrings( ReadFile( "sourcesharp/game/server/shell_stringtables.cpp" ) );
	ASSERT_FALSE( gi.empty() );
	ASSERT_FALSE( st.empty() );

	EXPECT_FALSE( Contains( gi, "IsServer()" ) )
		<< "shell_gameclients.cpp — CBaseEntity::IsServer() is back in a "
		<< "server-only translation unit, where it is the literal true.";
	EXPECT_FALSE( Contains( st, "IsServer()" ) )
		<< "shell_stringtables.cpp — CBaseEntity::IsServer() came back with the "
		<< "accessors A4 moved into it. It is the literal true there too.";
	EXPECT_EQ( CountOf( gi, "AddString( true," ) + CountOf( st, "AddString( true," ),
	           (size_t)2 )
		<< "expected two string-table AddString calls whose first argument "
		<< "folded from CBaseEntity::IsServer() to true — both in "
		<< "shell_stringtables.cpp (PrecacheMaterial, PrecacheParticleSystem), "
		<< "gameinterface.cpp's third having gone with LoadMessageOfTheDay. "
		<< "shell_gameclients.cpp has " << CountOf( gi, "AddString( true," )
		<< ", shell_stringtables.cpp has " << CountOf( st, "AddString( true," ) << ".";
}


//=============================================================================//
// THE SCANNER'S OWN TESTS.
//
// Everything above is only as good as StripCommentsAndStrings and GuardsIn, and
// both have a failure mode that reads as a pass: strip too much and every
// "must not contain" check succeeds vacuously; recognise too few guard
// spellings and the counts above are satisfied by a shorter list.
//=============================================================================//

TEST( CppAliasT6Scanner, StripCommentsAndStringsRemovesBothAndKeepsCode )
{
	const std::string in =
		"int a; // CBaseEntity::IsServer() in a line comment\n"
		"/* CBaseEntity::Instance in a block comment */\n"
		"const char *p = \"CBaseEntity::Simulate\";\n"
		"int b = CBaseEntity::PrecacheModel( x );\n";
	const std::string out = StripCommentsAndStrings( in );

	EXPECT_FALSE( Contains( out, "IsServer" ) ) << "the line comment survived";
	EXPECT_FALSE( Contains( out, "Instance" ) ) << "the block comment survived";
	EXPECT_FALSE( Contains( out, "Simulate" ) ) << "the string literal survived";

	// ...and the code did NOT go. Without this the three above pass on an empty
	// string, which is the whole hazard.
	EXPECT_TRUE( Contains( out, "CBaseEntity::PrecacheModel" ) )
		<< "the stripper ate real code, so every 'must not contain' check above "
		<< "it would pass by having nothing left to find";
	EXPECT_TRUE( Contains( out, "int a;" ) );
	EXPECT_TRUE( Contains( out, "const char *p =" ) );
}

TEST( CppAliasT6Scanner, AllThreeGuardSpellingsAreRecognisedAndArmsAssignedRight )
{
	GuardShape_t shape = kGuardPlainNdef;
	bool bArmedFirst = true;

	EXPECT_TRUE( IsGuardOpen( "\t#ifndef SS_CBASEENTITY_ALIAS", &shape, &bArmedFirst ) );
	EXPECT_EQ( shape, kGuardPlainNdef );
	EXPECT_FALSE( bArmedFirst );

	EXPECT_TRUE( IsGuardOpen( "#ifdef SS_CBASEENTITY_ALIAS", &shape, &bArmedFirst ) );
	EXPECT_EQ( shape, kGuardPlainDef );
	EXPECT_TRUE( bArmedFirst );

	EXPECT_TRUE( IsGuardOpen( "#if !defined( CLIENT_DLL ) && !defined( SS_CBASEENTITY_ALIAS )",
	                          &shape, &bArmedFirst ) );
	EXPECT_EQ( shape, kGuardCompound );
	EXPECT_FALSE( bArmedFirst ) << "a compound guard that NEGATES the macro puts the "
		"SDK arm first, like #ifndef does";

	// An #endif comment naming the macro is not an opening. Without this the
	// scanner counts every guard twice and the exact counts above are wrong in
	// a way that still looks deliberate.
	EXPECT_FALSE( IsGuardOpen( "\t#endif // !SS_CBASEENTITY_ALIAS", &shape, &bArmedFirst ) );
	EXPECT_FALSE( IsGuardOpen( "\t// SS_CBASEENTITY_ALIAS in prose", &shape, &bArmedFirst ) );
	EXPECT_FALSE( IsGuardOpen( "#ifdef CLIENT_DLL", &shape, &bArmedFirst ) );
}

TEST( CppAliasT6Scanner, TheFilesWereReallyOpenedAndCarryTheirGuards )
{
	ASSERT_TRUE( SrcRoot() != NULL );

	size_t nTotal = 0;
	for ( size_t i = 0; i < kT6FileCount; i++ )
	{
		const std::vector< std::string > lines = ReadLines( kT6Files[ i ].pszFile );
		EXPECT_GT( lines.size(), (size_t)10 )
			<< kT6Files[ i ].pszFile << " — read as fewer than ten lines. Every "
			<< "'must not contain' check in this file would pass on that.";

		// "More than ten lines" says the read happened; it does not say WHICH
		// file was read. The anchor does, and it is what stops the sum below
		// being a sum over the wrong sixteen files.
		std::string strAll;
		for ( size_t n = 0; n < lines.size(); n++ ) strAll += lines[ n ] + "\n";
		EXPECT_TRUE( Contains( strAll, kT6Files[ i ].pszAnchor ) )
			<< kT6Files[ i ].pszFile << " does not contain its anchor '"
			<< kT6Files[ i ].pszAnchor << "'";

		nTotal += GuardsIn( kT6Files[ i ].pszFile ).size();
	}

	// THE SHARD TOTAL WAS 40, and a fall to zero was to be read as a failure:
	// all 40 regions sat in the six STOCK files (c_te_projecteddecal.cpp 1,
	// cdll_client_int.cpp 3, entity_tools_server.cpp 11,
	// debugoverlay_shared.cpp 1, physics_saverestore.cpp 7, saverestore.cpp 17)
	// that neither wave was going to reach. Neither client.so nor server.so
	// compiles those six any more, so their rows left the table with them, and
	// what is left sums to exactly zero -- which the per-row exact counts in
	// EveryGuardIsWellFormedAndTheCountsAreExact already pin, file by file.
	EXPECT_EQ( nTotal, (size_t)0 )
		<< "shard T6's alias-conditional regions, summed over the files that are "
		<< "still compiled. Every one of them is a zero row; a region appearing "
		<< "in one is a guard nobody meant.";
}
