//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — plan_entity.md step 7f =========================//
// SS_REASON h  the CoreCLR host and bootstrap dispatch
//
// THE CLIENT UTILITY LIBRARY: the six things `game/client/cdll_util.cpp`
// still owed the shim, moved whole, with their declarations left where they
// were so not one caller changed.
//
// WHY THIS FILE EXISTS. Step 7f's gate is "cross-edges zero on the client"
// over the shared movement and collision residue, and `cdll_util.cpp` held six
// of them:
//
//     ScreenWidth()  ScreenHeight()  TextMessageGet()  VarArgs()
//     UpdateLocalPlayerVisionFlags()  g_MakingDevShots
//
// Every one is client-DLL mechanism the engine and VGUI drive, which
// plan_entity.md §6 lists as legitimately native — the screen size comes from
// the surface, the text message from the engine, and `VarArgs` is a printf
// into a static buffer. None of them decides anything about what the game
// does, so none is policy this step could have moved into managed code
// instead; what they were was STOCK-OWNED, and that is what changes here.
//
// THE VISION-FLAG FAMILY MOVES WHOLE, and that is not tidiness. Its two
// counters are file statics:
//
//     static int g_nLocalPlayerVisionFlagsWeaponsCheck;
//     static int g_nLocalPlayerVisionFlags;
//
// `UpdateLocalPlayerVisionFlags` writes them and `GetLocalPlayerVisionFilterFlags`
// reads them. Moving the writer alone would have meant EXPORTING the two
// statics so the reader could still see them — which trades one cross-edge for
// two and puts a mutable global on the boundary. The reader, the writer, the
// predicate they serve and the ConVar the predicate publishes to therefore all
// live here.
//
// `C_BasePlayer::GetLocalPlayer()` is NOT a stock symbol in this build — it is
// defined in `sourcesharp/game/client/shell_player_orphans.cpp`, checked with
//
//     nm -C --defined-only --print-file-name <every client object> \
//       | grep 'C_BasePlayer::GetLocalPlayer'
//
// which names that shim object and no other. So the move converts no edge: the
// call was shim->stock inside cdll_util.cpp's object and is shim->shim here.
// It answers NULL in this mod anyway (the local player is C_StandalonePlayer),
// which is why both counters stay zero — that is the answer the mod already
// gets and this move does not change it.
//
//=======================================================================//

#include "cbase.h"

#include "c_baseplayer.h"
#include "cdll_util.h"
#include "gamerules.h"
#include "hud.h"
#include "itextmessage.h"
#include "ss_clock.h"        // CheckEmitReasonablePhysicsSpew, from entity_shared_statics.cpp
// The block from util_shared_library.cpp at the end of this file: the managed
// violence convars, the shared-random seed hash and the particle tracer.
#include "managed_convar_ref.h"
#include "checksum_crc.h"
#include "entity_prediction_seed.h"
#include <vstdlib/random.h>
#include "particle_parse.h"
#include "ss_string.h"         // ss::StrCopy, for Hack_FixEscapeChars
// The sound block from sound_emitter.cpp at the end of this file.
#include "engine/IEngineSound.h"
#include "SoundEmitterSystem/isoundemittersystembase.h"
#include "gamesound.h"
extern ISoundEmitterSystemBase *soundemitterbase;   // shell_clientdll.cpp

#include <stdarg.h>

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

//-----------------------------------------------------------------------------
// Set by the -makedevshots command line, read by cdll_util.cpp's own
// `AllowedDrawing` test. The DEFINITION moves; the `extern bool` declaration
// stays in game/client/cdll_util.h, so every reader is unchanged.
//-----------------------------------------------------------------------------
bool g_MakingDevShots = false;

//-----------------------------------------------------------------------------
// Purpose: Performs a var args printf into a static return buffer
// Input  : *format -
//			... -
// Output : char
//-----------------------------------------------------------------------------
char *VarArgs( const char *format, ... )
{
	va_list		argptr;
	static char		string[1024];

	va_start (argptr, format);
	Q_vsnprintf (string, sizeof( string ), format,argptr);
	va_end (argptr);

	return string;
}

//-----------------------------------------------------------------------------
// THE VISION-FLAG FAMILY. The ConVar comes with it because
// IsLocalPlayerUsingVisionFilterFlags is the only thing that writes it and the
// only thing that reads it back is the shader the same call publishes for.
//-----------------------------------------------------------------------------
ConVar localplayer_visionflags( "localplayer_visionflags", "0", FCVAR_DEVELOPMENTONLY );

// NOTE: cache these because this gets executed hundreds of times per frame
static int g_nLocalPlayerVisionFlagsWeaponsCheck = 0;
static int g_nLocalPlayerVisionFlags = 0;

int GetLocalPlayerVisionFilterFlags( bool bWeaponsCheck /*= false */ )
{
	return bWeaponsCheck ? g_nLocalPlayerVisionFlagsWeaponsCheck : g_nLocalPlayerVisionFlags;
}

void UpdateLocalPlayerVisionFlags()
{
	g_nLocalPlayerVisionFlagsWeaponsCheck = 0;
	g_nLocalPlayerVisionFlags = 0;
	C_BasePlayer *pPlayer = C_BasePlayer::GetLocalPlayer();
	if ( pPlayer )
	{
		g_nLocalPlayerVisionFlagsWeaponsCheck = pPlayer->GetVisionFilterFlags( true );
		g_nLocalPlayerVisionFlags = pPlayer->GetVisionFilterFlags( false );
	}
}

bool IsLocalPlayerUsingVisionFilterFlags( int nFlags, bool bWeaponsCheck /* = false */ )
{
	int nLocalPlayerFlags = GetLocalPlayerVisionFilterFlags( bWeaponsCheck );

	if ( !bWeaponsCheck )
	{
		// We can only modify the RJ flags with normal checks that won't take the forced kill cam flags that can happen in weapon checks
		int nRJShaderFlags = nLocalPlayerFlags;
		if ( nRJShaderFlags != 0 && GameRules() && !GameRules()->AllowMapVisionFilterShaders() )
		{
			nRJShaderFlags = 0;
		}

		if ( nRJShaderFlags != localplayer_visionflags.GetInt() )
		{
			localplayer_visionflags.SetValue( nRJShaderFlags );
		}
	}

	return ( nLocalPlayerFlags & nFlags ) == nFlags;
}

//-----------------------------------------------------------------------------
// The engine's own message table. A one-line passthrough, and it is here
// rather than managed for the reason §6 gives: it is resolved by the engine
// interface and returns a pointer into the engine's own storage.
//-----------------------------------------------------------------------------
client_textmessage_t *TextMessageGet( const char *pName )
{
	return engine->TextMessageGet( pName );
}

//-----------------------------------------------------------------------------
// Purpose: ScreenHeight returns the height of the screen, in pixels
// Output : int
//-----------------------------------------------------------------------------
int ScreenHeight( void )
{
	int w, h;
	GetHudSize( w, h );
	return h;
}

//-----------------------------------------------------------------------------
// Purpose: ScreenWidth returns the width of the screen, in pixels
// Output : int
//-----------------------------------------------------------------------------
int ScreenWidth( void )
{
	int w, h;
	GetHudSize( w, h );
	return w;
}

//=============================================================================//
// FROM entity_shared_statics.cpp, WHERE THEY WERE #ifdef CLIENT_DLL.
//
// plan_server_native_v2 T2 (2026-10-18): a shared TU keeps only what both DLLs
// import (user ruling 2026-10-12), and these three were compiled only into the
// client. Moved verbatim; the reasonableness limits they read
// (k_flMaxEntitySpeed and the rest) stay in entity_shared_statics.cpp, which
// both DLLs compile. None has a caller in client.so today (nm: no symbol, the
// gc drops them); they are stock's, kept for the port that needs them.
//=============================================================================//

// Utility func to throttle rate at which the "reasonable position" spew goes out
static double s_LastEntityReasonableEmitTime;
bool CheckEmitReasonablePhysicsSpew()
{

	// Reported recently?
	double now = ss::SecondsSinceStart();
	if ( now >= s_LastEntityReasonableEmitTime && now < s_LastEntityReasonableEmitTime + 5.0 )
	{
		// Already reported recently
		return false;
	}

	// Not reported recently.  Report it now
	s_LastEntityReasonableEmitTime = now;
	return true;
}

//-----------------------------------------------------------------------------
// Returns:
//   -1 - velocity is really, REALLY bad and probably should be rejected.
//   0  - velocity was suspicious and clamped.
//   1  - velocity was OK and not modified
//-----------------------------------------------------------------------------
int CheckEntityVelocity( Vector &v )
{
	float r = k_flMaxEntitySpeed;
	if (
		v.x > -r && v.x < r &&
		v.y > -r && v.y < r &&
		v.z > -r && v.z < r)
	{
		// The usual case.  It's totally reasonable
		return 1;
	}
	float speed = v.Length();
	if ( speed < k_flMaxEntitySpeed * 100.0f )
	{
		// Sort of suspicious.  Clamp it
		v *= k_flMaxEntitySpeed / speed;
		return 0;
	}

	// A terrible, horrible, no good, very bad velocity.
	return -1;
}

//-----------------------------------------------------------------------------
// Where a tracer for a shot from vecShotSrc should be drawn from.
//
// Multiplayer answers a sentinel and not a position: the caller in
// baseentity_shared.cpp forces the attachment position in MakeTracer there, so
// the result is never read, and stock spelled that as (999,999,999) rather than
// as a branch at the call site. Kept verbatim.
//-----------------------------------------------------------------------------
void EntityTracer_ComputeStartPosition( const Vector &vecShotSrc, Vector *pVecTracerStart )
{
	// The `#ifndef HL2MP` this clause stood inside went with the move: neither
	// of this mod's link lines defines that name, so the guard was text and the
	// clause always ran. Deleted rather than ledgered, on tools/dead-conditionals'
	// own instruction.
	if ( g_pGameRules->IsMultiplayer() )
	{
		// NOTE: we do this because in MakeTracer, we force it to use the attachment position
		// in multiplayer, so the results from this function should never actually get used.
		pVecTracerStart->Init( 999, 999, 999 );
		return;
	}

	*pVecTracerStart = vecShotSrc;
}

//=============================================================================//
// FROM util_shared_library.cpp, WHERE THEY WERE #ifdef CLIENT_DLL.
//
// plan_server_native_v2 T2 (2026-10-18): a shared TU keeps only what both DLLs
// import (user ruling 2026-10-12), and these were compiled only into the
// client. Moved verbatim, between two files of the same relocated-Valve-code
// ruling. The four violence refs are this file's own: the shared file keeps
// its four for UTIL_ShouldShowBlood, which the server calls.
//=============================================================================//

static CManagedConVarRef	violence_hblood( "violence_hblood" );
static CManagedConVarRef	violence_hgibs( "violence_hgibs" );
static CManagedConVarRef	violence_ablood( "violence_ablood" );
static CManagedConVarRef	violence_agibs( "violence_agibs" );

bool UTIL_IsLowViolence( void )
{
	// These convars are no longer necessary -- the engine is the final arbiter of
	// violence settings -- but they're here for legacy support and for testing low
	// violence when the engine is in normal violence mode.
	if ( !violence_hblood.GetBool() || !violence_ablood.GetBool() || !violence_hgibs.GetBool() || !violence_agibs.GetBool() )
		return true;

	return engine->IsLowViolence();
}

//-----------------------------------------------------------------------------
// Purpose: 
//-----------------------------------------------------------------------------
const char* UTIL_GetActiveHolidayString()
{
	// USES_ECON_ITEMS is not on either link line, so stock's arm here was
	// EconHolidays_GetActiveHolidayString() and was never reachable. See the
	// mod-guard paragraph in the banner.
	return NULL;
}

//-----------------------------------------------------------------------------
// Purpose: 
//-----------------------------------------------------------------------------
const char* UTIL_GetActiveOperationString()
{
	// TF_DLL / TF_CLIENT_DLL, likewise: stock walks the econ item schema's
	// operation definitions here and this build has no schema to walk.
	return NULL;
}

//-----------------------------------------------------------------------------
// 7. THE SHARED-RANDOM FAMILY -- MOVED HERE, WHOLE AND UNEDITED, from
//    game/shared/util_shared.cpp by the Phase 4 exit lane.
//
//    A family in this file's own sense: SeedFileLineHash is file-static
//    storage that only these four can see, so it comes with them or they read
//    a different hash. Their only other dependencies are header-inline
//    (EntityPrediction_Seed, entity_prediction_seed.h) or library
//    (CRC32_*, RandomSeed/RandomFloat/RandomInt) -- no entity type, either
//    side.
//
//    WHY IT MOVED. SharedRandomInt was game/shared/animation.cpp's ONLY tie
//    into the stock link graph's single cycle -- the one that is the Phase 4
//    exit's irreducible batch -- so animation.cpp was inside that batch on the
//    strength of one symbol. It is not now. tools/p4-exit-batches measures it;
//    shared_random_move_test.cpp pins the move.
//
//    Not one caller changed: util_shared.h still declares all four.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Purpose: Helper function get get determinisitc random values for shared/prediction code
// Input  : seedvalue - 
//			*module - 
//			line - 
// Output : static int
//-----------------------------------------------------------------------------
static int SeedFileLineHash( int seedvalue, const char *sharedname, int additionalSeed )
{
	CRC32_t retval;

	CRC32_Init( &retval );

	CRC32_ProcessBuffer( &retval, (void *)&seedvalue, sizeof( int ) );
	CRC32_ProcessBuffer( &retval, (void *)&additionalSeed, sizeof( int ) );
	CRC32_ProcessBuffer( &retval, (void *)sharedname, (int)strlen( sharedname ) );
	
	CRC32_Final( &retval );

	return (int)( retval );
}

float SharedRandomFloat( const char *sharedname, float flMinVal, float flMaxVal, int additionalSeed /*=0*/ )
{
	Assert( EntityPrediction_Seed() != -1 );

	int seed = SeedFileLineHash( EntityPrediction_Seed(), sharedname, additionalSeed );
	RandomSeed( seed );
	return RandomFloat( flMinVal, flMaxVal );
}

int SharedRandomInt( const char *sharedname, int iMinVal, int iMaxVal, int additionalSeed /*=0*/ )
{
	Assert( EntityPrediction_Seed() != -1 );

	int seed = SeedFileLineHash( EntityPrediction_Seed(), sharedname, additionalSeed );
	RandomSeed( seed );
	return RandomInt( iMinVal, iMaxVal );
}

Vector SharedRandomVector( const char *sharedname, float minVal, float maxVal, int additionalSeed /*=0*/ )
{
	Assert( EntityPrediction_Seed() != -1 );

	int seed = SeedFileLineHash( EntityPrediction_Seed(), sharedname, additionalSeed );
	RandomSeed( seed );
	// HACK:  Can't call RandomVector/Angle because it uses rand() not vstlib Random*() functions!
	// Get a random vector.
	Vector vRandom;
	vRandom.x = RandomFloat( minVal, maxVal );
	vRandom.y = RandomFloat( minVal, maxVal );
	vRandom.z = RandomFloat( minVal, maxVal );
	return vRandom;
}

QAngle SharedRandomAngle( const char *sharedname, float minVal, float maxVal, int additionalSeed /*=0*/ )
{
	Assert( EntityPrediction_Seed() != -1 );

	int seed = SeedFileLineHash( EntityPrediction_Seed(), sharedname, additionalSeed );
	RandomSeed( seed );

	// HACK:  Can't call RandomVector/Angle because it uses rand() not vstlib Random*() functions!
	// Get a random vector.
	Vector vRandom;
	vRandom.x = RandomFloat( minVal, maxVal );
	vRandom.y = RandomFloat( minVal, maxVal );
	vRandom.z = RandomFloat( minVal, maxVal );
	return QAngle( vRandom.x, vRandom.y, vRandom.z );
}

//-----------------------------------------------------------------------------
// Purpose: Make a tracer using a particle effect
//-----------------------------------------------------------------------------
void UTIL_ParticleTracer( const char *pszTracerEffectName, const Vector &vecStart, const Vector &vecEnd, 
				 int iEntIndex, int iAttachment, bool bWhiz )
{
	int iParticleIndex = GetParticleSystemIndex( pszTracerEffectName );
	UTIL_Tracer( vecStart, vecEnd, iEntIndex, iAttachment, 0, bWhiz, "ParticleTracer", iParticleIndex );
}

//=============================================================================//
// FROM sound_emitter.cpp, WHERE THEY WERE #ifdef CLIENT_DLL (plan_server_native_v2
// T2, 2026-10-18): a shared TU keeps only what both DLLs import. Moved verbatim,
// between two files of the same relocated-Valve-code ruling, but for
// ReloadSoundEntriesInList: it called CSoundEmitterSystem's member of that name,
// a one-line forwarder to soundemitterbase with no other caller, so it makes
// that call itself and the member went.
//=============================================================================//

void ReloadSoundEntriesInList( IFileList *pFilesToReload )
{
	soundemitterbase->ReloadSoundEntriesInList( pFilesToReload );
}

//-----------------------------------------------------------------------------
// Purpose: 
// Input  : filter - 
//			iEntIndex - 
//			*soundname - 
//			*pOrigin - 
//-----------------------------------------------------------------------------
void GameSound_Emit( IRecipientFilter& filter, int iEntIndex, const char *soundname, HSOUNDSCRIPTHANDLE& handle, const Vector *pOrigin /*= NULL*/, float soundtime /*= 0.0f*/, float *duration /*=NULL*/ )
{

	//VPROF( "CBaseEntity::EmitSound" );
	GameSoundParams_t params;
	params.m_pSoundName = soundname;
	params.m_flSoundTime = soundtime;
	params.m_pOrigin = pOrigin;
	params.m_pflSoundDuration = duration;
	params.m_bWarnOnDirectWaveReference = true;

	GameSound_Emit( filter, iEntIndex, params, handle );
}

// HACK HACK:  Do we need to pull the entire SENTENCEG_* wrapper over to the client .dll?
int SENTENCEG_Lookup(const char *sample)
{
	return engine->SentenceIndexFromName( sample + 1 );
}

bool GameSound_GetParameters( const char *soundname, HSOUNDSCRIPTHANDLE& handle, CSoundParameters &params, const char *actormodel )
{
	gender_t gender = soundemitterbase->GetActorGender( actormodel );
	
	return soundemitterbase->GetParametersForSoundEx( soundname, handle, params, gender );
}

//-----------------------------------------------------------------------------
// Purpose: 
// Input  : *name - 
//-----------------------------------------------------------------------------
void GameSound_Prefetch( const char *name )
{
	 enginesound->PrefetchSound( name );
}

//-----------------------------------------------------------------------------
// From sound_emitter.cpp (plan_server_native_v2 T2): its one caller is the
// client's close-caption arm there, so server.so never needed it. Turns the
// two characters "\\n" in a caption token into a newline, in place.
//-----------------------------------------------------------------------------
void Hack_FixEscapeChars( char *str )
{
	int len = (int)strlen( str ) + 1;
	char *i = str;
	char *o = (char *)_alloca( len );
	char *osave = o;
	while ( *i )
	{
		if ( *i == '\\' )
		{
			switch (  *( i + 1 ) )
			{
			case 'n':
				*o = '\n';
				++i;
				break;
			default:
				*o = *i;
				break;
			}
		}
		else
		{
			*o = *i;
		}

		++i;
		++o;
	}
	*o = 0;
	ss::StrCopy( str, osave, len );
}
