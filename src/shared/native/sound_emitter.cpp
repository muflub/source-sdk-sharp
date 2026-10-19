//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the sound emitter, as mechanism =====================//
// SS_REASON b  an interface resolved by name at load
//
// game/shared/SoundEmitterSystem.cpp, moved here by plan_entity.md step 4b:
// "the emitter moves under the shim as mechanism".
//
// WHAT IT IS. Everything between "a name from a soundscript" and "the engine
// plays a wave": CSoundEmitterSystem, which parses the game_sounds manifest at
// level load and holds it; the GameSound_* free functions, which are the whole
// public surface; UTIL_EmitAmbientSound; and the developer commands that
// inspect the tables. It DECIDES NOTHING. Which sound plays, when, and from
// what, are decisions, and every one of them is in C# — shell_sound.cpp is the
// four calls that cross.
//
// WHAT LEFT WITH THE MOVE. This file no longer defines a single entity member
// and no longer resolves an entity from an index:
//
//   * the NINE non-static CBaseEntity / C_BaseEntity sound members went to
//     game/shared/baseentity_sound_members.cpp, a stock quarantine step 4f
//     deletes whole with baseentity.cpp. They are the last callers of this
//     file that need a `this`, and they are the only reason it had any.
//   * the two ModifyEmitSoundParams lookups in GameSound_Emit, the two owner
//     walks that set `fromplayer`, and the caption-attenuation loop. All five
//     were already guarded out of this build, and each one is replaced by a
//     note saying what it did, why it could not answer, and who owns the
//     question now. THAT IS NOT TIDINESS: tools/entity-census reads the TEXT
//     of a shim file, so a guarded-out cast in one is a cast the gate counts.
//
// THE SEVENTEEN STATIC MEMBERS had already been lifted to GameSound_* by an
// earlier phase (see game/shared/gamesound.h); soundemitter_lift_test.cpp is
// the guard on that lift and reads this file at its new address.
//
// COMPILED BY BOTH VPCS, unchanged. The client's own arms are still here and
// still say what they said; step 4b is a SERVER deletion and did not touch
// them.
//
//=============================================================================//

#ifdef CLIENT_DLL
#include "cbase.h"
#include "igamesystem.h"
// The client's "who is at this index" ask, over the slot table (§3.3). This
// file is in client_csharp.vpc and server_csharp.vpc only -- never a stock
// vpc -- so there is no !SOURCESHARP arm to keep.
#include "shell_cliententitylist.h"
#include "c_recipientfilter.h"
#else
// THE SERVER'S HALF IS ON THE R5 INCLUDE SET (plan_server_novalve.md P5 lane SP):
// public/ headers and our own, with the engine globals and the two helpers our
// own TUs define declared.
#include "edict.h"
#include "iserverentity.h"
#include "ss_util.h"               // engine, ss::INDEXENT
#include "shell_serversystem.h"	// plan_gamedll.md Phase 7a / T1
#include "ss_recipient_filter.h"
#include "imanaged_areas.h"        // IManagedUserMessages: the close caption is C#'s
class IFileSystem;
class IEngineSound;
extern CGlobalVars *gpGlobals;
extern IEngineSound *enginesound;
extern IFileSystem *filesystem;
#endif
// Each DLL's own: the server's is shell_sentences.cpp; the client's (the
// engine's sentence index) client_util_library.cpp since plan_server_native_v2 T2.
int SENTENCEG_Lookup( const char *sample );
#include <ctype.h>
#include <KeyValues.h>
#include "engine/IEngineSound.h"
#include "SoundEmitterSystem/isoundemittersystembase.h"
#ifdef CLIENT_DLL
#include "igamesystem.h"
#endif
#include "soundchars.h"
#include "filesystem.h"
#include "checksum_crc.h"
#include "ss_command_line.h"
#include "engine_utlvector.h"
#include "gamesound.h"         // GameSoundParams_t, which KeepEngineOrigins takes

//-----------------------------------------------------------------------------
// The positions the engine played a sound at. IEngineSound::EmitSound appends
// them to the vector it is handed, growing it with ITS copy of tier1's
// template, so the block is libtier0's -- and EmitSound_t's own
// m_UtlVecSoundOrigin would be freed by this module when the EmitSound_t goes
// (a foreign free: the live gate's allocation report counted 56 per run). So
// the engine fills an EngineFilledUtlVector, which gives the block back to the
// engine's allocator, and the positions are copied into the caller's vector,
// which this module owns (plan_server_novalve.md 3.6: copy-out).
//-----------------------------------------------------------------------------
static void KeepEngineOrigins( const ss::EngineFilledUtlVector< Vector > &origins, const GameSoundParams_t &ep )
{
	for ( const Vector &v : origins.View() )
		ep.m_UtlVecSoundOrigin.AddToTail( v );
}
#ifdef CLIENT_DLL
#include "util_shared.h"
#endif



#ifdef CLIENT_DLL
#include <vgui_controls/Controls.h>
#include <vgui/IVGui.h>
#include "hud_closecaption.h"
#define CRecipientFilter C_RecipientFilter
#endif

// The lifted precache/tick/prediction/tracer statics. They used to be
// reached as CBaseEntity::Foo() through one-line forwarders in
// game/shared/baseentity_shared_forwarders.cpp; that quarantine is retired
// and the calls below name the free functions directly.
#include "entity_shared_statics.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "managed_convar_ref.h"

// The three "shown once" sets below are std::sets of owned copies, compared
// case-sensitively as the default CUtlSymbolTables they replaced compared
// (plan_server_novalve.md P3: tier1's utlsymbol.cpp was linked for them).
#include <set>
#include <string>
#include "ss_string.h"

// The client's caption arm below unescapes "\\n" in a caption token with
// this; it lives in client_util_library.cpp since plan_server_native_v2 T2,
// because the server never calls it.
void Hack_FixEscapeChars( char *str );

// The server's recipient filters are server.so's own (ss_recipient_filter.h,
// plan_server_novalve.md P5); the client spells its base C_RecipientFilter
// (the #define above). Both are renamed AFTER every include, so only this
// file's own code sees the new spelling.
#ifndef CLIENT_DLL
#define CRecipientFilter ss::CRecipientFilter
#define RecipientFilter_RemoveIfNotCloseCaptioning ss::RecipientFilter_RemoveIfNotCloseCaptioning
#endif


// sv_soundemitter_trace MOVED TO MANAGED CODE, plan_shim.md Phase 5 (lane 5d):
// SourceSharp.Server/MoveVars.cs and SourceSharp.Client/MoveVars.cs declare it,
// both halves because it is FCVAR_REPLICATED and this file is compiled into
// both DLLs. tools/7e-particle-sound-gate drives it BY NAME, which a managed
// declaration answers exactly as this one did -- Engine.RegisterConVar builds a
// real native ConVar in the engine's registry. What moved is where the default
// and the meaning are written down, not whether the name resolves.
//
// CManagedConVarRef rather than ConVarRef, for the reason managed_convar_ref.h
// gives: a ConVarRef built at DLL load caches its miss forever.
static CManagedConVarRef sv_soundemitter_trace( "sv_soundemitter_trace" );

extern ISoundEmitterSystemBase *soundemitterbase;
static ConVar *g_pClosecaption = NULL;

#ifdef _XBOX
int LookupStringFromCloseCaptionToken( char const *token );
const wchar_t *GetStringForIndex( int index );
#endif
bool g_bPermitDirectSoundPrecache = false;


void WaveTrace( char const *wavname, char const *funcname )
{
	if ( IsX360() && !IsDebug() )
	{
		return;
	}

	static std::set< std::string > s_WaveTrace;

	// Make sure we only show the message once
	if ( s_WaveTrace.insert( wavname ).second )
	{
		DevMsg( "%s directly referenced wave %s (should use game_sounds.txt system instead)\n", 
			funcname, wavname );
	}
}

//-----------------------------------------------------------------------------
// Purpose: 
//-----------------------------------------------------------------------------
#ifdef CLIENT_DLL
class CSoundEmitterSystem : public CClientSystem
#else
class CSoundEmitterSystem : public CServerSystem
#endif
{
public:
	virtual char const *Name() { return "CSoundEmitterSystem"; }

#if !defined( CLIENT_DLL )
	bool			m_bLogPrecache;
	FileHandle_t	m_hPrecacheLogFile;
	std::set< std::string > m_PrecachedScriptSounds;
public:
	CSoundEmitterSystem( char const *pszName ) :
		m_bLogPrecache( false ),
		m_hPrecacheLogFile( FILESYSTEM_INVALID_HANDLE )
	{
	}

	void LogPrecache( char const *soundname )
	{
		if ( !m_bLogPrecache )
			return;

		// Make sure we only show the message once
		if ( m_PrecachedScriptSounds.count( soundname ) )
			return;

		if (m_hPrecacheLogFile == FILESYSTEM_INVALID_HANDLE)
		{
			StartLog();
		}

		m_PrecachedScriptSounds.insert( soundname );

		if (m_hPrecacheLogFile != FILESYSTEM_INVALID_HANDLE)
		{
			filesystem->Write("\"", 1, m_hPrecacheLogFile);
			filesystem->Write(soundname, (int)strlen(soundname), m_hPrecacheLogFile);
			filesystem->Write("\"\n", 2, m_hPrecacheLogFile);
		}
		else
		{
			Warning( "Disabling precache logging due to file i/o problem!!!\n" );
			m_bLogPrecache = false;
		}
	}

	void StartLog()
	{
		m_PrecachedScriptSounds.clear();

		if ( !m_bLogPrecache )
			return;

		if ( FILESYSTEM_INVALID_HANDLE != m_hPrecacheLogFile )
		{
			return;
		}

		filesystem->CreateDirHierarchy("reslists", "DEFAULT_WRITE_PATH");

		// open the new level reslist
		char path[_MAX_PATH];
		ss::Format(path, sizeof(path), "reslists\\%s.snd", gpGlobals->mapname.ToCStr() );
		m_hPrecacheLogFile = filesystem->Open(path, "wt", "GAME");
	}

	void FinishLog()
	{
		if ( FILESYSTEM_INVALID_HANDLE != m_hPrecacheLogFile )
		{
			filesystem->Close( m_hPrecacheLogFile );
			m_hPrecacheLogFile = FILESYSTEM_INVALID_HANDLE;
		}

		m_PrecachedScriptSounds.clear();
	}
#else
	CSoundEmitterSystem( char const *name )
	{
	}

#endif

	// IServerSystem stuff
	virtual bool Init()
	{
		Assert( soundemitterbase );
#if !defined( CLIENT_DLL )
		m_bLogPrecache = ss::CommandLineHas( "-makereslists" );
#endif
		g_pClosecaption = cvar->FindVar("closecaption");
		Assert(g_pClosecaption);
		return soundemitterbase->ModInit();
	}

	virtual void Shutdown()
	{
		Assert( soundemitterbase );
#if !defined( CLIENT_DLL )
		FinishLog();
#endif
		soundemitterbase->ModShutdown();
	}


	virtual void TraceEmitSound( char const *fmt, ... )
	{
		if ( !sv_soundemitter_trace.GetBool() )
			return;

		va_list	argptr;
		char string[256];
		va_start (argptr, fmt);
		ss::FormatV( string, sizeof( string ), fmt, argptr );
		va_end (argptr);

		// Spew to console.
		//
		// SOURCESHARP: was CBaseEntity::IsServer(), a static whose whole body
		// is `return true;` on the server and `return false;` on the client
		// (baseentity.h:2182, c_baseentity.h:2103). It is a per-DLL COMPILE-TIME
		// constant that borrows the entity class for a namespace, and this file
		// already asks the same question with the preprocessor eleven times
		// over. Under `using CBaseEntity = IGameEntity;` an alias has no
		// statics, so the borrow stops compiling; asking the way the rest of
		// the file asks costs nothing and says the same thing.
#if defined( CLIENT_DLL )
		Msg( "%s %s", "(cl)", string );
#else
		Msg( "%s %s", "(sv)", string );
#endif
	}

	// Precache all wave files referenced in wave or rndwave keys
	virtual void LevelInitPreEntity()
	{
		char mapname[ 256 ];
#if !defined( CLIENT_DLL )
		StartLog();
		ss::Format( mapname, sizeof( mapname ), "maps/%s", STRING( gpGlobals->mapname ) );
#else
		ss::Format( mapname, sizeof( mapname ), "maps/%s", ss::GetFileName( engine->GetLevelName() ) );
#endif

		ss::FixSlashes( mapname );
		ss::StrLower( mapname );

		// GetCleanMapName went (plan_server_native_min P1): an identity
		// function (its TF arms were deleted before the move); the name is
		// already clean, and the buffer that was its out-parameter goes with
		// it.
		const char *pszCleanMapName = mapname;

		// Load in any map specific overrides
		char scriptfile[ 512 ];
		// STOCK HAD A `#if defined( TF_CLIENT_DLL ) || defined( TF_DLL )` ARM HERE
		// that loaded Mann-vs-Machine's four extra override scripts for a map
		// whose name contains "mvm". It is deleted, not guarded: this is not
		// Team Fortress and never will be, and a shim file may not carry a
		// conditional this build can never satisfy (tools/dead-conditionals).
		// What is left is stock's own `#else` arm, unchanged -- the one this
		// build has always taken.
		ss::StripExtension( pszCleanMapName, scriptfile, sizeof( scriptfile ) );
		ss::StrAppend( scriptfile, "_level_sounds.txt", sizeof( scriptfile ), COPY_ALL_CHARACTERS );

		if ( filesystem->FileExists( scriptfile, "GAME" ) )
		{
			soundemitterbase->AddSoundOverrides( scriptfile );
		}

#if !defined( CLIENT_DLL )
		for ( int i=soundemitterbase->First(); i != soundemitterbase->InvalidIndex(); i=soundemitterbase->Next( i ) )
		{
			CSoundParametersInternal *pParams = soundemitterbase->InternalGetParametersForSound( i );
			if ( pParams->ShouldPreload() )
			{
				InternalPrecacheWaves( i );
			}
		}
#endif
	}

	virtual void LevelInitPostEntity()
	{
	}

	virtual void LevelShutdownPostEntity()
	{
		soundemitterbase->ClearSoundOverrides();

#if !defined( CLIENT_DLL )
		FinishLog();
#endif
	}

	void Flush()
	{
		Assert( soundemitterbase );
#if !defined( CLIENT_DLL )
		FinishLog();
#endif
		soundemitterbase->Flush();
	}
		
	void InternalPrecacheWaves( int soundIndex )
	{
		CSoundParametersInternal *internal = soundemitterbase->InternalGetParametersForSound( soundIndex );
		if ( !internal )
			return;

		int waveCount = internal->NumSoundNames();
		if ( !waveCount )
		{
			DevMsg( "CSoundEmitterSystem:  sounds.txt entry '%s' has no waves listed under 'wave' or 'rndwave' key!!!\n",
				soundemitterbase->GetSoundName( soundIndex ) );
		}
		else
		{
			g_bPermitDirectSoundPrecache = true;

			for( int wave = 0; wave < waveCount; wave++ )
			{
				GameSound_Precache( soundemitterbase->GetWaveName( internal->GetSoundNames()[ wave ].symbol ) );
			}

			g_bPermitDirectSoundPrecache = false;
		}
	}

	void InternalPrefetchWaves( int soundIndex )
	{
		CSoundParametersInternal *internal = soundemitterbase->InternalGetParametersForSound( soundIndex );
		if ( !internal )
			return;

		int waveCount = internal->NumSoundNames();
		if ( !waveCount )
		{
			DevMsg( "CSoundEmitterSystem:  sounds.txt entry '%s' has no waves listed under 'wave' or 'rndwave' key!!!\n",
				soundemitterbase->GetSoundName( soundIndex ) );
		}
		else
		{
			for( int wave = 0; wave < waveCount; wave++ )
			{
				GameSound_Prefetch( soundemitterbase->GetWaveName( internal->GetSoundNames()[ wave ].symbol ) );
			}
		}
	}

	HSOUNDSCRIPTHANDLE PrecacheScriptSound( const char *soundname )
	{
		int soundIndex = soundemitterbase->GetSoundIndex( soundname );
		if ( !soundemitterbase->IsValidIndex( soundIndex ) )
		{
			if ( ss::StrIStr( soundname, ".wav" ) || strstr( soundname, ".mp3" ) )
			{
				g_bPermitDirectSoundPrecache = true;
	
				GameSound_Precache( soundname );
	
				g_bPermitDirectSoundPrecache = false;
				return SOUNDEMITTER_INVALID_HANDLE;
			}

#if !defined( CLIENT_DLL )
			if ( soundname[ 0 ] )
			{
				static std::set< std::string > s_PrecacheScriptSoundFailures;

				// Make sure we only show the message once
				if ( s_PrecacheScriptSoundFailures.insert( soundname ).second )
				{
					DevMsg( "PrecacheScriptSound '%s' failed, no such sound script entry\n", soundname );
				}
			}
#endif
			return (HSOUNDSCRIPTHANDLE)soundIndex;
		}
#if !defined( CLIENT_DLL )
		LogPrecache( soundname );
#endif

		InternalPrecacheWaves( soundIndex );
		return (HSOUNDSCRIPTHANDLE)soundIndex;
	}

	void PrefetchScriptSound( const char *soundname )
	{
		int soundIndex = soundemitterbase->GetSoundIndex( soundname );
		if ( !soundemitterbase->IsValidIndex( soundIndex ) )
		{
			if ( ss::StrIStr( soundname, ".wav" ) || strstr( soundname, ".mp3" ) )
			{
				GameSound_Prefetch( soundname );
			}
			return;
		}

		InternalPrefetchWaves( soundIndex );
	}
public:

	void EmitSoundByHandle( IRecipientFilter& filter, int entindex, const GameSoundParams_t & ep, HSOUNDSCRIPTHANDLE& handle )
	{
		// Pull data from parameters
		CSoundParameters params;

		// Try to deduce the actor's gender
		gender_t gender = GENDER_NONE;
#ifdef CLIENT_DLL
		// plan_entity.md step 7e, CLIENT ARM: THE ENTITY LIST, NOT THE SDK CLASS.
		//
		// This was a fork -- `C_BaseEntity::Instance( entindex )->GetModelName()`
		// live, with the entity-list form behind `#ifdef SS_CBASEENTITY_ALIAS` as
		// the answer somebody would need later. Step 7e takes the later form as
		// THE form, and it is not a tidy-up: the live arm was LOSING WORK.
		//
		// C_BaseEntity::Instance answers NULL for every entity on the parallel
		// hierarchy -- a C_StandaloneEntity is an IClientEntity, not a
		// C_BaseEntity -- so every managed entity's sound was emitted as
		// GENDER_NONE. A soundscript with `gender_male` / `gender_female` entries
		// then picks its fallback wave, or none, and nothing anywhere reports it.
		// That is the same class of defect as the silent `return;` step 4b turned
		// into a refusal one file away, in a quieter key.
		//
		// THE ENTITY LIST IS THE ENGINE BOUNDARY AND HOLDS BOTH HIERARCHIES, which
		// is the whole reason the port addresses entities through it. The
		// renderable's model is the model m_ModelName named -- C_BaseEntity::SetModel
		// sets both from one index -- and modelinfo turns it back into the path
		// GetActorGender parses. So this asks the same question the server arm
		// below asks its edict, at the same boundary, and gets an answer for every
		// entity rather than for half of them.
		//
		// AND IT COSTS TWO CROSS-EDGES: the deleted arm was the only shim consumer
		// of CClientEntityList::GetBaseEntity(int) (cliententitylist.cpp) and of
		// C_BaseEntity::GetModelName() const (c_baseentity.cpp). Two of the six
		// edges step 4b's move added to the client, named in its handover.
		//
		// LANE sub-entitylist: THE ROUTE CHANGED, THE QUESTION DID NOT.
		//
		// 7e wrote this as `ClientEntityList().GetClientEntity( entindex )` and
		// lane entlist-server left it standing as "the client's arm, in a file
		// the server lane owns". It is the SAME move fx.cpp and particlemgr.cpp
		// already took (step p7-exit): SourceSharp_ClientEntityFromIndex is
		// shell_cliententitylist.h's union -- the SLOT TABLE first, the stock
		// list as the fallback that exists only while stock client entities do
		// -- and its body is stock's own GetClientEntity written over that one
		// primitive (`pUnknown ? pUnknown->GetIClientEntity() : NULL`).
		//
		// NOTHING OBSERVABLE CHANGES TODAY and saying so is the point:
		// SourceSharp_ClientEntityBind registers every shell in the stock list
		// too, so both spellings resolve the same objects while that seam
		// exists. What changes is the DIRECTION -- this file names no entity
		// list (plan_entity.md clause 2, gate 2's own row), and when
		// cliententitylist.cpp and the SourceSharp_LegacyList* arms leave
		// together this site keeps working with no further edit instead of
		// having to be found and rewritten inside the atomic flip.
		IClientEntity *pClientEnt = SourceSharp_ClientEntityFromIndex( entindex );
		const model_t *pGenderModel = pClientEnt ? pClientEnt->GetModel() : NULL;
		if ( pGenderModel )
		{
			gender = soundemitterbase->GetActorGender( modelinfo->GetModelName( pGenderModel ) );
		}
#else
		// Phase 13 (e) retype, server arm: GetModelName() is IServerEntity's
		// own virtual and CBaseEntity's body is its one implementation
		// (m_ModelName), so the question is answerable at the ENGINE BOUNDARY
		// and never needed the SDK resolve. Asking the edict for its
		// IServerEntity is movehelper_server.cpp's own recipe, taken there for
		// the same reason: the SDK class's static Instance answers NULL for an
		// entity outside that hierarchy, which HAS a model name and an edict --
		// so this is not only alias-proof, it stops silently losing the gender
		// of every entity this port owns.
		edict_t *pEdict = ss::INDEXENT( entindex );
		IServerEntity *pSE = pEdict ? pEdict->GetIServerEntity() : NULL;
		if ( pSE )
		{
			gender = soundemitterbase->GetActorGender( STRING( pSE->GetModelName() ) );
		}
#endif

		if ( !soundemitterbase->GetParametersForSoundEx( ep.m_pSoundName, handle, params, gender, true ) )
		{
			return;
		}

		if ( !params.soundname[0] )
			return;


		// handle SND_CHANGEPITCH/SND_CHANGEVOL and other sound flags.etc.
		if( ep.m_nFlags & SND_CHANGE_PITCH )
		{
			params.pitch = ep.m_nPitch;
		}


		if( ep.m_nFlags & SND_CHANGE_VOL )
		{
			params.volume = ep.m_flVolume;
		}


#if defined( _DEBUG ) && !defined( CLIENT_DLL )
		if ( !enginesound->IsSoundPrecached( params.soundname ) )
		{
			Msg( "Sound %s:%s was not precached\n", ep.m_pSoundName, params.soundname );
		}
#endif

		float st = ep.m_flSoundTime;
		if ( !st && 
			params.delay_msec != 0 )
		{
			st = gpGlobals->curtime + (float)params.delay_msec / 1000.f;
		}

		ss::EngineFilledUtlVector< Vector > origins;
		enginesound->EmitSound( 
			filter, 
			entindex, 
			params.channel, 
			params.soundname,
			params.volume,
			(soundlevel_t)params.soundlevel,
			ep.m_nFlags,
			params.pitch,
			ep.m_nSpecialDSP,
			ep.m_pOrigin,
			NULL,
			origins.ForEngine< CUtlVector< Vector > >(),
			true,
			st,
			ep.m_nSpeakerEntity );
		KeepEngineOrigins( origins, ep );
		if ( ep.m_pflSoundDuration )
		{
			*ep.m_pflSoundDuration = enginesound->GetSoundDuration( params.soundname );
		}

		// THE RECIPIENT COUNT IS PART OF THE TRACE, added at plan_entity.md step
		// 4b, and it is the difference between "the emit ran" and "somebody
		// could hear it". enginesound->EmitSound is called above whatever the
		// filter holds, so a trace without this number says exactly as much
		// about an EMPTY filter as about a full one -- and an empty filter is
		// the failure mode of every change to how a sound's source position is
		// worked out, this step's included.
		TraceEmitSound( "EmitSound:  '%s' emitted as '%s' (ent %i, %i recipients)\n",
			ep.m_pSoundName, params.soundname, entindex,
			filter.GetRecipientCount() );


		// Don't caption modulations to the sound
		if ( !( ep.m_nFlags & ( SND_CHANGE_PITCH | SND_CHANGE_VOL ) ) )
		{
			EmitCloseCaption( filter, entindex, params, ep );
		}
		// NVNT's HapticProcessSound stood here, under WIN32, and went in
		// plan_server_native_v2 T2: its body was empty on both sides (the
		// Novint haptics.dll is not shipped, and Linux never had one).
	}

	void EmitSound( IRecipientFilter& filter, int entindex, const GameSoundParams_t & ep )
	{


		if ( ep.m_pSoundName && 
			( ss::StrIStr( ep.m_pSoundName, ".wav" ) || 
			  ss::StrIStr( ep.m_pSoundName, ".mp3" ) || 
			  ep.m_pSoundName[0] == '!' ) )
		{

			if ( ep.m_bWarnOnDirectWaveReference && 
				ss::StrIStr( ep.m_pSoundName, ".wav" ) )
			{
				WaveTrace( ep.m_pSoundName, "Emitsound" );
			}

#if defined( _DEBUG ) && !defined( CLIENT_DLL )
			if ( !enginesound->IsSoundPrecached( ep.m_pSoundName ) )
			{
				Msg( "Sound %s was not precached\n", ep.m_pSoundName );
			}
#endif
			ss::EngineFilledUtlVector< Vector > origins;
			enginesound->EmitSound( 
				filter, 
				entindex, 
				ep.m_nChannel, 
				ep.m_pSoundName, 
				ep.m_flVolume, 
				ep.m_SoundLevel, 
				ep.m_nFlags, 
				ep.m_nPitch, 
				ep.m_nSpecialDSP,
				ep.m_pOrigin,
				NULL, 
				origins.ForEngine< CUtlVector< Vector > >(),
				true, 
				ep.m_flSoundTime,
				ep.m_nSpeakerEntity );
			KeepEngineOrigins( origins, ep );
			if ( ep.m_pflSoundDuration )
			{
				*ep.m_pflSoundDuration = enginesound->GetSoundDuration( ep.m_pSoundName );
			}

			TraceEmitSound( "EmitSound:  Raw wave emitted '%s' (ent %i)\n",
				ep.m_pSoundName, entindex );
			return;
		}

		if ( ep.m_hSoundScriptHandle == SOUNDEMITTER_INVALID_HANDLE )
		{
			ep.m_hSoundScriptHandle = (HSOUNDSCRIPTHANDLE)soundemitterbase->GetSoundIndex( ep.m_pSoundName );
		}

		if ( ep.m_hSoundScriptHandle == -1 )
			return;

		EmitSoundByHandle( filter, entindex, ep, ep.m_hSoundScriptHandle );
	}

	void EmitCloseCaption( IRecipientFilter& filter, int entindex, bool fromplayer, char const *token, CUtlVector< Vector >& originlist, float duration, bool warnifmissing /*= false*/ )
	{
#if !defined( CLIENT_DLL )
		// THE CAPTION IS C#'s (user-message payload model, 2026-10-04):
		// SourceSharp.Server's CloseCaptions decides whether one is sent at
		// all, builds the token (lowercased, escapes fixed), the duration hint
		// and the flags, and sends the CloseCaption payload to each recipient.
		// What stays here are the facts only native code can look up -- who
		// the filter holds, a wave's duration, the speaker's gender -- and
		// they are gathered only when C# says a caption is wanted.
		IManagedUserMessages *pMessages = ManagedUserMessages();
		if ( !pMessages || !pMessages->CloseCaptionsWanted() )
		{
			return;
		}

		// A negative duration means fill it in from the wav file if possible
		if ( duration < 0.0f )
		{
			char const *wav = soundemitterbase->GetWavFileForSound( token, GENDER_NONE );
			duration = wav ? enginesound->GetSoundDuration( wav ) : 2.0f;
		}

		// NOTE:  We must make a copy or else if the filter is owned by a SoundPatch, we'll end up destructively removing
		//  all players from it!!!!
		CRecipientFilter filterCopy;
		filterCopy.CopyFrom( (CRecipientFilter &)filter );

		// Remove any players who don't want close captions
		RecipientFilter_RemoveIfNotCloseCaptioning( (CRecipientFilter &)filterCopy );

		int32_t slots[ ABSOLUTE_PLAYER_LIMIT ];
		int32_t nSlots = 0;
		for ( int i = 0; i < filterCopy.GetRecipientCount() && nSlots < ABSOLUTE_PLAYER_LIMIT; i++ )
		{
			slots[ nSlots++ ] = filterCopy.GetRecipientIndex( i );
		}

		// The speaker's gender, from its model: the same edict -> IServerEntity
		// resolve as EmitSoundByHandle above (IServerEntity is where
		// GetModelName lives, and CBaseEntity::Instance answered NULL for every
		// entity this port owns).
		int32_t nGender = GENDER_NONE;
		edict_t *pActorEdict = ss::INDEXENT( entindex );
		IServerEntity *pActorSE = pActorEdict ? pActorEdict->GetIServerEntity() : NULL;
		if ( pActorSE )
		{
			nGender = soundemitterbase->GetActorGender( STRING( pActorSE->GetModelName() ) );
		}

		pMessages->EmitCloseCaption( slots, nSlots, filterCopy.IsReliable() ? 1 : 0, token, duration,
		                             fromplayer ? 1 : 0, warnifmissing ? 1 : 0, nGender );
#else
		// No close captions in multiplayer...
		if ( gpGlobals->maxClients > 1 || (gpGlobals->maxClients==1 && !g_pClosecaption->GetBool()))
		{
			return;
		}

		// A negative duration means fill it in from the wav file if possible
		if ( duration < 0.0f )
		{
			char const *wav = soundemitterbase->GetWavFileForSound( token, GENDER_NONE );
			if ( wav )
			{
				duration = enginesound->GetSoundDuration( wav );
			}
			else
			{
				duration = 2.0f;
			}
		}

		char lowercase[ 256 ];
		ss::StrCopy( lowercase, token, sizeof( lowercase ) );
		ss::StrLower( lowercase );
		if ( strstr( lowercase, "\\" ) )
		{
			Hack_FixEscapeChars( lowercase );
		}

		// NOTE:  We must make a copy or else if the filter is owned by a SoundPatch, we'll end up destructively removing
		//  all players from it!!!!
		CRecipientFilter filterCopy;
		filterCopy.CopyFrom( (CRecipientFilter &)filter );

		// Remove any players who don't want close captions
		RecipientFilter_RemoveIfNotCloseCaptioning( (CRecipientFilter &)filterCopy );

		// STOCK'S CAPTION-ATTENUATION LOOP WAS HERE, and it never ran its body
		// in this build. Every iteration began with UTIL_PlayerByIndex, which
		// util.cpp documents as "NULL, always, in this build -- nothing here
		// constructs an SDK player", so its `continue` took every slot and no
		// recipient was ever removed. Deleting it leaves the filter exactly as
		// it left it.
		//
		// It is not merely dead: the subject was an SDK player, so reaching
		// GetCollideable() through it is that hierarchy's own inheritance.
		// Whoever answers close-caption attenuation for a standalone player
		// starts from the standalone player, and from the AttenuateCaption
		// declared in sceneentity.cpp, which is not in either vpc.
		// Anyone left?
		if ( filterCopy.GetRecipientCount() > 0 )
		{

			// Direct dispatch
			CHudCloseCaption *cchud = GET_HUDELEMENT( CHudCloseCaption );
			if ( cchud )
			{
				cchud->ProcessCaption( lowercase, duration, fromplayer );
			}
		}
	#endif
	}

	void EmitCloseCaption( IRecipientFilter& filter, int entindex, const CSoundParameters & params, const GameSoundParams_t & ep )
	{
#if !defined( CLIENT_DLL )
		// Whether a caption is wanted at all is C#'s (CloseCaptions.Wanted);
		// asked first, so a sound's duration is only looked up for one.
		IManagedUserMessages *pMessages = ManagedUserMessages();
		if ( !pMessages || !pMessages->CloseCaptionsWanted() )
		{
			return;
		}
#else
		// No close captions in multiplayer...
		if ( gpGlobals->maxClients > 1 || (gpGlobals->maxClients==1 && !g_pClosecaption->GetBool()))
		{
			return;
		}
#endif

		if ( !ep.m_bEmitCloseCaption )
		{
			return;
		}

		// NOTE:  We must make a copy or else if the filter is owned by a SoundPatch, we'll end up destructively removing
		//  all players from it!!!!
		CRecipientFilter filterCopy;
		filterCopy.CopyFrom( (CRecipientFilter &)filter );

		// Remove any players who don't want close captions
		RecipientFilter_RemoveIfNotCloseCaptioning( (CRecipientFilter &)filterCopy );

		// Anyone left?
		if ( filterCopy.GetRecipientCount() <= 0 )
		{
			return;
		}

		float duration = 0.0f;
		if ( ep.m_pflSoundDuration )
		{
			duration = *ep.m_pflSoundDuration;
		}
		else
		{
			duration = enginesound->GetSoundDuration( params.soundname );
		}

		// STOCK'S OWNER WALK IS DELETED, not guarded. It resolved the sound's
		// entity index to a base entity and climbed the owner chain looking
		// for a player; the resolve is a static of the SDK class, so the
		// subject was always an SDK entity, and the only SDK class whose
		// IsPlayer() answers true is CBasePlayer -- whose three translation
		// units are on neither link line. `fromplayer` was therefore false on
		// every path already.
		//
		// It stayed as guarded-out stock text while this file was stock. It is
		// a shim mechanism file now (see the banner), and a shim file carrying
		// a spelling of a cast the §3.16 census counts is a site the census
		// counts, guard or no guard -- the census reads TEXT.
		//
		// WHOEVER ANSWERS "did a player make this noise" AFTER THIS starts
		// from the standalone player, not from here: the caption's
		// CLOSE_CAPTION_FROMPLAYER flag is the only thing the answer feeds.
		bool fromplayer = false;
		EmitCloseCaption( filter, entindex, fromplayer, ep.m_pSoundName, ep.m_UtlVecSoundOrigin, duration, ep.m_bWarnOnMissingCloseCaption );
	}

	void EmitAmbientSound( int entindex, const Vector& origin, const char *soundname, float flVolume, int iFlags, int iPitch, float soundtime /*= 0.0f*/, float *duration /*=NULL*/ )
	{
		// Pull data from parameters
		CSoundParameters params;

		if ( !soundemitterbase->GetParametersForSound( soundname, params, GENDER_NONE ) )
		{
			return;
		}


		if( iFlags & SND_CHANGE_PITCH )
		{
			params.pitch = iPitch;
		}

		if( iFlags & SND_CHANGE_VOL )
		{
			params.volume = flVolume;
		}

#if defined( CLIENT_DLL )
		enginesound->EmitAmbientSound( params.soundname, params.volume, params.pitch, iFlags, soundtime );
#else
		engine->EmitAmbientSound(entindex, origin, params.soundname, params.volume, params.soundlevel, iFlags, params.pitch, soundtime );
#endif

		bool needsCC = !( iFlags & ( SND_STOP | SND_CHANGE_VOL | SND_CHANGE_PITCH ) );

		float soundduration = 0.0f;
		
		if ( duration || needsCC )
		{
			soundduration = enginesound->GetSoundDuration( params.soundname );
			if ( duration )
			{
				*duration = soundduration;
			}
		}

		TraceEmitSound( "EmitAmbientSound:  '%s' emitted as '%s' (ent %i)\n",
			soundname, params.soundname, entindex );

		// We only want to trigger the CC on the start of the sound, not on any changes or halting of the sound
		if ( needsCC )
		{
			CRecipientFilter filter;
			filter.AddAllPlayers();
			filter.MakeReliable();

			CUtlVector< Vector > dummy;
			EmitCloseCaption( filter, entindex, false, soundname, dummy, soundduration, false );
		}

	}

	void StopSoundByHandle( int entindex, const char *soundname, HSOUNDSCRIPTHANDLE& handle )
	{
		if ( handle == SOUNDEMITTER_INVALID_HANDLE )
		{
			handle = (HSOUNDSCRIPTHANDLE)soundemitterbase->GetSoundIndex( soundname );
		}

		if ( handle == SOUNDEMITTER_INVALID_HANDLE )
			return;

		CSoundParametersInternal *params;

		params = soundemitterbase->InternalGetParametersForSound( (int)handle );
		if ( !params )
		{
			return;
		}

		// HACK:  we have to stop all sounds if there are > 1 in the rndwave section...
		int c = params->NumSoundNames();
		for ( int i = 0; i < c; ++i )
		{
			char const *wavename = soundemitterbase->GetWaveName( params->GetSoundNames()[ i ].symbol );
			Assert( wavename );

			enginesound->StopSound( 
				entindex, 
				params->GetChannel(), 
				wavename );

			TraceEmitSound( "StopSound:  '%s' stopped as '%s' (ent %i)\n",
				soundname, wavename, entindex );
		}
	}

	void StopSound( int entindex, const char *soundname )
	{
		HSOUNDSCRIPTHANDLE handle = (HSOUNDSCRIPTHANDLE)soundemitterbase->GetSoundIndex( soundname );
		if ( handle == SOUNDEMITTER_INVALID_HANDLE )
		{
			return;
		}

		StopSoundByHandle( entindex, soundname, handle );
	}


	void StopSound( int iEntIndex, int iChannel, const char *pSample )
	{
		if ( pSample && ( ss::StrIStr( pSample, ".wav" ) || ss::StrIStr( pSample, ".mp3" ) || pSample[0] == '!' ) )
		{
			enginesound->StopSound( iEntIndex, iChannel, pSample );

			TraceEmitSound( "StopSound:  Raw wave stopped '%s' (ent %i)\n",
				pSample, iEntIndex );
		}
		else
		{
			// Look it up in sounds.txt and ignore other parameters
			StopSound( iEntIndex, pSample );
		}
	}

	void EmitAmbientSound( int entindex, const Vector &origin, const char *pSample, float volume, soundlevel_t soundlevel, int flags, int pitch, float soundtime /*= 0.0f*/, float *duration /*=NULL*/ )
	{


		if ( pSample && ( ss::StrIStr( pSample, ".wav" ) || ss::StrIStr( pSample, ".mp3" )) )
		{
#if defined( CLIENT_DLL )
			enginesound->EmitAmbientSound( pSample, volume, pitch, flags, soundtime );
#else
			engine->EmitAmbientSound( entindex, origin, pSample, volume, soundlevel, flags, pitch, soundtime );
#endif

			if ( duration )
			{
				*duration = enginesound->GetSoundDuration( pSample );
			}

			TraceEmitSound( "EmitAmbientSound:  Raw wave emitted '%s' (ent %i)\n",
				pSample, entindex );
		}
		else
		{
			EmitAmbientSound( entindex, origin, pSample, volume, flags, pitch, soundtime, duration );
		}
	}
};

static CSoundEmitterSystem g_SoundEmitterSystem( "CSoundEmitterSystem" );

#ifdef CLIENT_DLL
CClientSystem *SoundEmitterSystem()
#else
CServerSystem *SoundEmitterSystem()
#endif
{
	return &g_SoundEmitterSystem;
}


//-----------------------------------------------------------------------------
// THE DEVELOPER CONSOLE COMMANDS ARE GONE -- plan_shim.md §5 3f, lane 3f.
//
// sv_soundemitter_flush / cl_soundemitter_flush, sv_soundemitter_filecheck,
// sv_findsoundname, and `playgamesound` with its completion callback and
// S_SoundEmitterSystemFlush, which was the flush commands' only caller.
//
// A ConCommand IS REACHED BY NAME AND NO LINK-LINE CENSUS SEES IT, so the
// proof for these is not the census -- it is a grep of every place a name can
// come from: tools/, docs/, game/mod_sharp/cfg/ and the managed tree. None of
// the five appears in any of them. (sv_soundemitter_trace DOES:
// tools/7e-particle-sound-gate drives it at two sites, so it stays, and the
// plan's "registers from managed" for it is a MOVE that has not happened.)
//
// `playgamesound` IS THE ONE WORTH EXPLAINING, because deleting a shipped
// console command normally is a behaviour change and this one is not. Its body
// could not do anything: C_BasePlayer::GetLocalPlayer() is defined ONCE on this
// link line, in sourcesharp/game/client/shell_player_orphans.cpp:96, and its
// whole body is `return NULL;` -- the local player is a C_StandalonePlayer and
// c_baseplayer.cpp is not compiled. So the command printed "Can't play until a
// game is started." on every invocation this build has ever had, and the arm
// that would have played a sound reached C_BasePlayer through the inheritance
// the alias removes. Removing it removes an error message.
//
// THE LIVE SUCCESSOR IS MANAGED and does more than stock's ever did:
// SourceSharp.Server DevCommandsLive.cs `playentitysound <soundscript>
// [entity]` emits a script from a CHOSEN entity on the server, including from
// the shell-less entities that have no edict for stock's route to name.
//-----------------------------------------------------------------------------




//-----------------------------------------------------------------------------
// Purpose: 
// Input  : filter - 
//			iEntIndex - 
//			*soundname - 
//			*pOrigin - 
//-----------------------------------------------------------------------------
void GameSound_Emit( IRecipientFilter& filter, int iEntIndex, const char *soundname, const Vector *pOrigin /*= NULL*/, float soundtime /*= 0.0f*/, float *duration /*=NULL*/ )
{
	if ( !soundname )
		return;


	// VPROF( "CBaseEntity::EmitSound" );
	GameSoundParams_t params;
	params.m_pSoundName = soundname;
	params.m_flSoundTime = soundtime;
	params.m_pOrigin = pOrigin;
	params.m_pflSoundDuration = duration;
	params.m_bWarnOnDirectWaveReference = true;

	GameSound_Emit( filter, iEntIndex, params, params.m_hSoundScriptHandle );
}

//-----------------------------------------------------------------------------
// Purpose: 
// Input  : filter - 
//			iEntIndex - 
//			params - 
//-----------------------------------------------------------------------------
void GameSound_Emit( IRecipientFilter& filter, int iEntIndex, const GameSoundParams_t & params )
{

	// STOCK LOOKED THE ENTITY UP HERE and asked it to modify the parameters.
	// That lookup is DELETED, and it bought a no-op. Measured, not assumed: the
	// only definition of ModifyEmitSoundParams either link compiles is the SDK
	// base's own (baseentity_shared.cpp), whose server body is EMPTY and whose
	// client body is one call to
	// GameRules()->TranslateEffectForVisionFilter( "sounds", ... ) -- and the
	// only implementation of THAT which this build compiles is gamerules.h:207,
	// `return pchEffectName;`. Every override of either lives in
	// game/shared/tf/ or game/server/tf/, which neither vpc compiles.
	//
	// It also cost: the lookup answered NULL for a managed entity, so a sound
	// emitted on behalf of one already took the `else` of a branch whose `then`
	// did nothing.
	//
	// IF AN OVERRIDE IS EVER WANTED ON THIS SIDE it is a managed one, asked of
	// the entity by HANDLE before the emit -- not an SDK lookup by index. This
	// file is the mechanism; choosing the parameters is the game's decision.
	// VPROF( "CBaseEntity::EmitSound" );
	// Call into the sound emitter system...
	g_SoundEmitterSystem.EmitSound( filter, iEntIndex, params );
}

//-----------------------------------------------------------------------------
// Purpose: 
// Input  : filter - 
//			iEntIndex - 
//			params - 
//-----------------------------------------------------------------------------
void GameSound_Emit( IRecipientFilter& filter, int iEntIndex, const GameSoundParams_t & params, HSOUNDSCRIPTHANDLE& handle )
{

	// The same no-op override lookup as the overload above, deleted for the
	// same reasons; the argument is written out there.
	// VPROF( "CBaseEntity::EmitSound" );
	// Call into the sound emitter system...
	g_SoundEmitterSystem.EmitSoundByHandle( filter, iEntIndex, params, handle );
}


//-----------------------------------------------------------------------------
// Purpose: 
// Input  : iEntIndex - 
//			*soundname - 
//-----------------------------------------------------------------------------
// SIX FREE FUNCTIONS ARE GONE -- plan_shim.md §5 3f, lane 3f.
//
// GameSound_Duration, GameSound_PrefetchScript, GameSound_EmitAmbient,
// GameSound_StopByHandle and both GameSound_LookupSoundLevel overloads. Each
// had 0 importers on either link line AND no caller inside this translation
// unit -- both halves, because the census cannot see the second and four of
// this file's other orphans (GameSound_Prefetch, WaveTrace,
// Hack_FixEscapeChars, SENTENCEG_Lookup) have one and are therefore alive.
//
// THE DECLARATIONS STAY in game/shared/soundemittersystembase.h and
// game/shared/igamesystem-adjacent headers, as stock spells them, so an
// unported caller still compiles.
//
// THIS FILE STAYS NATIVE (plan_shim.md reason b) and the reason is worth
// repeating where somebody might doubt it: it does NOT parse game_sounds.
// `soundemitterbase->` is the `soundemittersystem` APP SYSTEM, which the
// engine loads by name and which owns the scripts; what is here is the game's
// side of that interface plus the precache bookkeeping, and the precache log
// is kept because `-makereslists` is a real engine mode this shim uses
// elsewhere (the soundscape's reslist touch, shell_soundscape_audio_client.cpp).
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
void GameSound_Stop( int iEntIndex, const char *soundname )
{
	g_SoundEmitterSystem.StopSound( iEntIndex, soundname );
}

void GameSound_Stop( int iEntIndex, int iChannel, const char *pSample )
{
	g_SoundEmitterSystem.StopSound( iEntIndex, iChannel, pSample );
}

void UTIL_EmitAmbientSound( int entindex, const Vector &vecOrigin, const char *samp, float vol, soundlevel_t soundlevel, int fFlags, int pitch, float soundtime /*= 0.0f*/, float *duration /*=NULL*/ )
{

	if (samp && *samp == '!')
	{
		int sentenceIndex = SENTENCEG_Lookup(samp);
		if (sentenceIndex >= 0)
		{
			char name[32];
			ss::Format( name, sizeof(name), "!%d", sentenceIndex );
#if !defined( CLIENT_DLL )
			engine->EmitAmbientSound( entindex, vecOrigin, name, vol, soundlevel, fFlags, pitch, soundtime );
#else
			enginesound->EmitAmbientSound( name, vol, pitch, fFlags, soundtime );
#endif
			if ( duration )
			{
				*duration = enginesound->GetSoundDuration( name );
			}

			g_SoundEmitterSystem.TraceEmitSound( "UTIL_EmitAmbientSound:  Sentence emitted '%s' (ent %i)\n",
				name, entindex );
		}
	}
	else
	{
		g_SoundEmitterSystem.EmitAmbientSound( entindex, vecOrigin, samp, vol, soundlevel, fFlags, pitch, soundtime, duration );
	}
}

// NOT YET PORTED, kept rather than deleted: stock's CBaseEntity::GetSoundDuration
// resolved a script sound name through this before asking the engine for the
// wave's length, and no managed caller asks for a duration yet. It moves to C#
// with that caller (docs/plans/plan_native_bindings_only.md N1).
[[maybe_unused]] static const char *UTIL_TranslateSoundName( const char *soundname, const char *actormodel )
{
	Assert( soundname );

	if ( ss::StrIStr( soundname, ".wav" ) || ss::StrIStr( soundname, ".mp3" ) )
	{
		if ( ss::StrIStr( soundname, ".wav" ) )
		{
			WaveTrace( soundname, "UTIL_TranslateSoundName" );
		}
		return soundname;
	}

	return soundemitterbase->GetWavFileForSound( soundname, actormodel );
}


bool GameSound_GetParameters( const char *soundname, CSoundParameters &params, const char *actormodel )
{
	gender_t gender = soundemitterbase->GetActorGender( actormodel );
	
	return soundemitterbase->GetParametersForSound( soundname, params, gender );
}

HSOUNDSCRIPTHANDLE GameSound_PrecacheScript( const char *soundname )
{
#if !defined( CLIENT_DLL )
	return g_SoundEmitterSystem.PrecacheScriptSound( soundname );
#else
	return soundemitterbase->GetSoundIndex( soundname );
#endif
}
//-----------------------------------------------------------------------------
// Purpose: 
// Input  : filter - 
//			*token - 
//			duration - 
//			warnifmissing - 
//-----------------------------------------------------------------------------
void GameSound_EmitCloseCaption( IRecipientFilter& filter, int entindex, char const *token, CUtlVector< Vector >& soundorigin, float duration, bool warnifmissing /*= false*/ )
{
	// The same owner walk, deleted for the same three reasons --
	// see CSoundEmitterSystem::EmitSoundByHandle above.
	bool fromplayer = false;

	g_SoundEmitterSystem.EmitCloseCaption( filter, entindex, fromplayer, token, soundorigin, duration, warnifmissing );
}

//-----------------------------------------------------------------------------
// Purpose: 
// Input  : *name - 
//			preload - 
// Output : Returns true on success, false on failure.
//-----------------------------------------------------------------------------
bool GameSound_Precache( const char *name )
{
	if ( IsPC() && !g_bPermitDirectSoundPrecache )
	{
		Warning( "Direct precache of %s\n", name );
	}

	// If this is out of order, warn
	if ( !EntityPrecache_IsAllowed() )
	{
		if ( !enginesound->IsSoundPrecached( name ) )
		{
			Assert( !"CBaseEntity::PrecacheSound:  too late" );

			Warning( "Late precache of %s\n", name );
		}
	}

	bool bret = enginesound->PrecacheSound( name, true );
	return bret;
}

