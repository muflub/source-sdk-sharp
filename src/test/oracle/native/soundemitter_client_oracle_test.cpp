//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp ==================================================//
//
// THE CLIENT SOUND EMITTER ORACLE: stock's CSoundEmitterSystem as the client
// compiles it -- the emit path (EmitSound, EmitSoundByHandle: the raw-wave
// arm, the script lookup, the speaker's gender, the caller's pitch and volume,
// the script's delay, the trace), the close captions (both EmitCloseCaption
// overloads: multiplayer, closecaption, the filter copy, the duration, the
// token's case and escapes, the owner walk, the HUD's direct dispatch), the
// stops (the raw wave, every wave of a script), the ambient sounds
// (UTIL_EmitAmbientSound's sentence arm, the wave and the script overloads,
// the caption to every player), GetParametersForSound, PrecacheScriptSound
// and the system's Init / Shutdown / level stages -- run offline from the
// PRISTINE game/shared/SoundEmitterSystem.cpp over a scripted world, and
// written down as the golden src/test/client/Fixtures/soundemitter-client-stock.tsv.
// Each call is driven through the entry point SourceSharp's client called it
// by: the C_BaseEntity statics GameSound_Emit / GameSound_Stop /
// GameSound_EmitCloseCaption / GameSound_GetParameters fronted, and
// UTIL_EmitAmbientSound.
//
// WHY OFFLINE. Captions never run in a multiplayer game, a script with a
// delay or a gender-split wave list is rare in the shipped scripts, and a
// live recording hears one map's worth of sounds. Here every arm is driven on
// purpose.
//
// THE SUBJECT IS INCLUDED, NOT LINKED: g_SoundEmitterSystem and
// sv_soundemitter_trace are file-static, so the harness includes the file
// into this translation unit. soundemitter_stub/soundemitter_harness.h says
// what is stock and what is stand-in.
//
// BOTH SIDES ARE PINNED TO THE GOLDEN. This test asserts that stock produces
// exactly it; SourceSharp.Client.Tests' ClientSoundEmitterOracleTests asserts
// the managed port does. Regenerating the golden to make this test green
// deletes the check: the golden changes only when the scenario does, and then
// on purpose (touch soundemitter_client_golden.regen beside this file, run,
// delete the marker, and say why in the commit).
//
//=============================================================================

#include <stdarg.h>
#include <fstream>
#include <map>
#include <sstream>
#include <string>
#include <vector>

#include "gtest/gtest.h"

#include "soundemitter_harness.h"
#include "SoundEmitterSystem/isoundemittersystembase.h"

//-----------------------------------------------------------------------------
// The log.
//-----------------------------------------------------------------------------
static std::string g_Log;

void SoundEmitterLog( const char *pFmt, ... )
{
	char szLine[2048];
	va_list ap;
	va_start( ap, pFmt );
	vsnprintf( szLine, sizeof( szLine ), pFmt, ap );
	va_end( ap );
	g_Log += szLine;
	g_Log += '\n';
}

unsigned int SoundEmitterBits( float f )
{
	unsigned int u;
	memcpy( &u, &f, sizeof( u ) );
	return u;
}

namespace
{

#define B( f ) SoundEmitterBits( f )

// Text as one TSV cell: a newline is "\n", a tab "\t", a backslash "\\".
std::string Cell( const char *psz )
{
	if ( !psz )
		return "-";
	std::string out;
	for ( const char *p = psz; *p; ++p )
	{
		if ( *p == '\n' )
			out += "\\n";
		else if ( *p == '\t' )
			out += "\\t";
		else if ( *p == '\\' )
			out += "\\\\";
		else
			out += *p;
	}
	return out;
}

std::string VecCell( const Vector *pv )
{
	if ( !pv )
		return "-";
	char sz[64];
	snprintf( sz, sizeof( sz ), "%08x,%08x,%08x", B( pv->x ), B( pv->y ), B( pv->z ) );
	return sz;
}

std::string FilterCell( const IRecipientFilter &filter )
{
	std::string out;
	const int c = filter.GetRecipientCount();
	for ( int i = 0; i < c; ++i )
	{
		if ( i )
			out += ",";
		out += std::to_string( filter.GetRecipientIndex( i ) );
	}
	if ( out.empty() )
		out = "-";
	out += filter.IsReliable() ? "/1" : "/0";
	return out;
}

//-----------------------------------------------------------------------------
// tier0's spew: Msg, DevMsg and Warning, each a row.
//-----------------------------------------------------------------------------
SpewRetval_t CaptureSpew( SpewType_t type, const tchar *pMsg )
{
	const char *pszGroup = GetSpewOutputGroup();
	const char *pszKind = "M";
	if ( type == SPEW_WARNING )
		pszKind = "W";
	else if ( type == SPEW_ASSERT )
		pszKind = "ASSERT";
	else if ( pszGroup && !strcmp( pszGroup, "developer" ) )
		pszKind = "D";
	SoundEmitterLog( "%s\t%s", pszKind, Cell( pMsg ).c_str() );
	return SPEW_CONTINUE;
}

//-----------------------------------------------------------------------------
// The world.
//-----------------------------------------------------------------------------
struct Script_t
{
	const char *pszName;
	int nChannel;
	float flVolume;
	int nLevel;
	int nPitch, nPitchLow, nPitchHigh;
	int nDelayMsec;
	std::vector<const char *> waves; // GetParametersForSound picks waves[ gender ] when there are three
	bool bParamsFail; // a valid index whose parameters the table will not give
};

std::vector<Script_t> s_Scripts;
std::map<std::string, float> s_Durations;
std::map<std::string, int> s_Sentences;
std::map<int, C_BaseEntity> s_Entities;
std::vector<std::string> s_Files;
std::vector<std::string> s_WaveNames; // the wave symbol table: symbol = index
std::vector<CSoundParametersInternal *> s_Internals;
const char *s_pszLevelName = "";
int s_nLocalSlot = 1;
bool s_bHud = true;

SoundEmitterGlobals_t s_Globals;

int ScriptIndex( const char *pszName )
{
	if ( !pszName )
		return -1;
	for ( size_t i = 0; i < s_Scripts.size(); i++ )
	{
		if ( !Q_stricmp( s_Scripts[i].pszName, pszName ) )
			return (int)i;
	}
	return -1;
}

int WaveSymbol( const char *pszWave )
{
	for ( size_t i = 0; i < s_WaveNames.size(); i++ )
	{
		if ( s_WaveNames[i] == pszWave )
			return (int)i;
	}
	s_WaveNames.push_back( pszWave );
	return (int)s_WaveNames.size() - 1;
}

const char *GenderName( int gender )
{
	return gender == 1 ? "1" : gender == 2 ? "2" : "0";
}

void FillParameters( const Script_t &s, int gender, CSoundParameters &params )
{
	params.channel = s.nChannel;
	params.volume = s.flVolume;
	params.pitch = s.nPitch;
	params.pitchlow = s.nPitchLow;
	params.pitchhigh = s.nPitchHigh;
	params.soundlevel = (soundlevel_t)s.nLevel;
	params.delay_msec = s.nDelayMsec;
	const char *pszWave = "";
	if ( s.waves.size() == 3 )
		pszWave = s.waves[gender];
	else if ( !s.waves.empty() )
		pszWave = s.waves[0];
	Q_strncpy( params.soundname, pszWave, sizeof( params.soundname ) );
}

void LogParameters( const char *pszAsk, const char *pszIn, bool ok, const CSoundParameters &params )
{
	SoundEmitterLog( "A\t%s\t%s\t=\t%d\t%d\t%08x\t%d\t%d\t%d\t%d\t%d\t%s", pszAsk, pszIn, ok ? 1 : 0, params.channel,
					 B( params.volume ), (int)params.soundlevel, params.pitch, params.pitchlow, params.pitchhigh,
					 params.delay_msec, Cell( ok ? params.soundname : NULL ).c_str() );
}

//-----------------------------------------------------------------------------
// The script table: ISoundEmitterSystemBase, answering from s_Scripts and
// writing every question it is asked, with its answer.
//-----------------------------------------------------------------------------
class CHarnessSoundEmitterBase final : public ISoundEmitterSystemBase
{
public:
	virtual bool Connect( CreateInterfaceFn ) { return true; }

	virtual void Disconnect() {}

	virtual void *QueryInterface( const char * ) { return NULL; }

	virtual InitReturnVal_t Init() { return INIT_OK; }

	virtual void Shutdown() {}

	virtual bool ModInit()
	{
		SoundEmitterLog( "A\tModInit\t=\t1" );
		return true;
	}

	virtual void ModShutdown() { SoundEmitterLog( "O\tModShutdown" ); }

	virtual int GetSoundIndex( const char *pName ) const
	{
		const int i = ScriptIndex( pName );
		SoundEmitterLog( "A\tSoundIndex\t%s\t=\t%d", Cell( pName ).c_str(), i );
		return i;
	}

	virtual bool IsValidIndex( int index )
	{
		const bool ok = index >= 0 && index < (int)s_Scripts.size();
		SoundEmitterLog( "A\tIsValidIndex\t%d\t=\t%d", index, ok ? 1 : 0 );
		return ok;
	}

	virtual int GetSoundCount( void ) { return (int)s_Scripts.size(); }

	virtual const char *GetSoundName( int index )
	{
		const char *psz = ( index >= 0 && index < (int)s_Scripts.size() ) ? s_Scripts[index].pszName : NULL;
		SoundEmitterLog( "A\tSoundName\t%d\t=\t%s", index, Cell( psz ).c_str() );
		return psz;
	}

	virtual bool GetParametersForSound( const char *soundname, CSoundParameters &params, gender_t gender,
										bool isbeingemitted = false )
	{
		const int i = ScriptIndex( soundname );
		const bool ok = i >= 0 && !s_Scripts[i].bParamsFail;
		if ( ok )
			FillParameters( s_Scripts[i], gender, params );
		char szIn[512];
		snprintf( szIn, sizeof( szIn ), "%s\t%s\t%d", Cell( soundname ).c_str(), GenderName( gender ),
				  isbeingemitted ? 1 : 0 );
		LogParameters( "Params", szIn, ok, params );
		return ok;
	}

	virtual const char *GetWaveName( CUtlSymbol &sym )
	{
		const int id = (UtlSymId_t)sym;
		return ( id >= 0 && id < (int)s_WaveNames.size() ) ? s_WaveNames[id].c_str() : NULL;
	}

	virtual CUtlSymbol AddWaveName( const char *name ) { return CUtlSymbol( (UtlSymId_t)WaveSymbol( name ) ); }

	virtual soundlevel_t LookupSoundLevel( const char *soundname )
	{
		const int i = ScriptIndex( soundname );
		return i >= 0 ? (soundlevel_t)s_Scripts[i].nLevel : SNDLVL_NONE;
	}

	virtual const char *GetWavFileForSound( const char *soundname, char const *actormodel )
	{
		return GetWavFileForSound( soundname, GetActorGender( actormodel ) );
	}

	virtual const char *GetWavFileForSound( const char *soundname, gender_t gender )
	{
		const int i = ScriptIndex( soundname );
		const char *psz = NULL;
		if ( i >= 0 && !s_Scripts[i].waves.empty() )
			psz = s_Scripts[i].waves.size() == 3 ? s_Scripts[i].waves[gender] : s_Scripts[i].waves[0];
		SoundEmitterLog( "A\tWavFile\t%s\t%s\t=\t%s", Cell( soundname ).c_str(), GenderName( gender ),
						 Cell( psz ).c_str() );
		return psz;
	}

	virtual int CheckForMissingWavFiles( bool ) { return 0; }

	virtual const char *GetSourceFileForSound( int ) const { return "scripts/game_sounds_harness.txt"; }

	virtual int First() const { return 0; }

	virtual int Next( int i ) const { return i + 1 < (int)s_Scripts.size() ? i + 1 : -1; }

	virtual int InvalidIndex() const { return -1; }

	virtual CSoundParametersInternal *InternalGetParametersForSound( int index )
	{
		CSoundParametersInternal *p = ( index >= 0 && index < (int)s_Internals.size() ) ? s_Internals[index] : NULL;
		if ( !p )
		{
			SoundEmitterLog( "A\tInternal\t%d\t=\t-", index );
			return NULL;
		}
		std::string waves;
		for ( int w = 0; w < p->NumSoundNames(); w++ )
		{
			if ( w )
				waves += ",";
			waves += s_WaveNames[(UtlSymId_t)p->GetSoundNames()[w].symbol];
		}
		SoundEmitterLog( "A\tInternal\t%d\t=\t%d\t%s", index, p->GetChannel(), waves.empty() ? "-" : waves.c_str() );
		return p;
	}

	virtual bool AddSound( const char *, const char *, const CSoundParametersInternal & ) { return false; }

	virtual void RemoveSound( const char * ) {}

	virtual void MoveSound( const char *, const char * ) {}

	virtual void RenameSound( const char *, const char * ) {}

	virtual void UpdateSoundParameters( const char *, const CSoundParametersInternal & ) {}

	virtual int GetNumSoundScripts() const { return 1; }

	virtual char const *GetSoundScriptName( int ) const { return "scripts/game_sounds_harness.txt"; }

	virtual bool IsSoundScriptDirty( int ) const { return false; }

	virtual int FindSoundScript( const char * ) const { return 0; }

	virtual void SaveChangesToSoundScript( int ) {}

	virtual void ExpandSoundNameMacros( CSoundParametersInternal &, char const * ) {}

	virtual gender_t GetActorGender( char const *actormodel )
	{
		gender_t g = GENDER_NONE;
		if ( actormodel && Q_stristr( actormodel, "female" ) )
			g = GENDER_FEMALE;
		else if ( actormodel && Q_stristr( actormodel, "male" ) )
			g = GENDER_MALE;
		SoundEmitterLog( "A\tActorGender\t%s\t=\t%s", Cell( actormodel ).c_str(), GenderName( g ) );
		return g;
	}

	virtual void GenderExpandString( char const *, char const *in, char *out, int maxlen )
	{
		Q_strncpy( out, in, maxlen );
	}

	virtual void GenderExpandString( gender_t, char const *in, char *out, int maxlen ) { Q_strncpy( out, in, maxlen ); }

	virtual bool IsUsingGenderToken( char const * ) { return false; }

	virtual unsigned int GetManifestFileTimeChecksum() { return 0; }

	virtual void AddSoundOverrides( char const *scriptfile, bool bPreload = false )
	{
		SoundEmitterLog( "O\tAddOverrides\t%s\t%d", Cell( scriptfile ).c_str(), bPreload ? 1 : 0 );
	}

	virtual void ClearSoundOverrides() { SoundEmitterLog( "O\tClearOverrides" ); }

	virtual bool GetParametersForSoundEx( const char *soundname, HSOUNDSCRIPTHANDLE &handle, CSoundParameters &params,
										  gender_t gender, bool isbeingemitted = false )
	{
		char szIn[512];
		snprintf( szIn, sizeof( szIn ), "%s\t%d\t%s\t%d", Cell( soundname ).c_str(), (int)handle, GenderName( gender ),
				  isbeingemitted ? 1 : 0 );
		if ( handle == SOUNDEMITTER_INVALID_HANDLE )
			handle = (HSOUNDSCRIPTHANDLE)ScriptIndex( soundname );
		const bool ok = handle >= 0 && handle < (int)s_Scripts.size() && !s_Scripts[handle].bParamsFail;
		if ( ok )
			FillParameters( s_Scripts[handle], gender, params );
		// The handle the table leaves is part of the answer.
		std::string in = std::string( szIn ) + "\t=>" + std::to_string( (int)handle );
		LogParameters( "ParamsEx", in.c_str(), ok, params );
		return ok;
	}

	virtual soundlevel_t LookupSoundLevelByHandle( char const *soundname, HSOUNDSCRIPTHANDLE & )
	{
		return LookupSoundLevel( soundname );
	}

	virtual void ReloadSoundEntriesInList( IFileList * ) {}

	virtual void Flush() { SoundEmitterLog( "O\tFlush" ); }
};

CHarnessSoundEmitterBase s_SoundEmitterBase;

//-----------------------------------------------------------------------------
// The engine's sound, writing every call and every question.
//-----------------------------------------------------------------------------
class CHarnessEngineSound final : public IEngineSound
{
public:
	virtual bool PrecacheSound( const char *pSample, bool bPreload = false, bool = false )
	{
		SoundEmitterLog( "O\tPrecache\t%s\t%d", Cell( pSample ).c_str(), bPreload ? 1 : 0 );
		return true;
	}

	virtual bool IsSoundPrecached( const char *pSample )
	{
		SoundEmitterLog( "A\tIsPrecached\t%s\t=\t1", Cell( pSample ).c_str() );
		return true;
	}

	virtual void PrefetchSound( const char *pSample ) { SoundEmitterLog( "O\tPrefetch\t%s", Cell( pSample ).c_str() ); }

	virtual float GetSoundDuration( const char *pSample )
	{
		std::map<std::string, float>::const_iterator it = s_Durations.find( pSample ? pSample : "" );
		const float f = it == s_Durations.end() ? 0.0f : it->second;
		SoundEmitterLog( "A\tDuration\t%s\t=\t%08x", Cell( pSample ).c_str(), B( f ) );
		return f;
	}

	virtual void EmitSound( IRecipientFilter &filter, int iEntIndex, int iChannel, const char *pSample, float flVolume,
							float flAttenuation, int iFlags = 0, int iPitch = PITCH_NORM, int iSpecialDSP = 0,
							const Vector *pOrigin = NULL, const Vector *pDirection = NULL, CUtlVector<Vector> * = NULL,
							bool = true, float soundtime = 0.0f, int speakerentity = -1 )
	{
		SoundEmitterLog( "O\tEmitSoundAttn\t%s\t%d\t%d\t%s\t%08x\t%08x\t%d\t%d\t%d\t%s\t%s\t%08x\t%d",
						 FilterCell( filter ).c_str(), iEntIndex, iChannel, Cell( pSample ).c_str(), B( flVolume ),
						 B( flAttenuation ), iFlags, iPitch, iSpecialDSP, VecCell( pOrigin ).c_str(),
						 VecCell( pDirection ).c_str(), B( soundtime ), speakerentity );
	}

	virtual void EmitSound( IRecipientFilter &filter, int iEntIndex, int iChannel, const char *pSample, float flVolume,
							soundlevel_t iSoundlevel, int iFlags = 0, int iPitch = PITCH_NORM, int iSpecialDSP = 0,
							const Vector *pOrigin = NULL, const Vector *pDirection = NULL, CUtlVector<Vector> * = NULL,
							bool = true, float soundtime = 0.0f, int speakerentity = -1 )
	{
		SoundEmitterLog( "O\tEmitSound\t%s\t%d\t%d\t%s\t%08x\t%d\t%d\t%d\t%d\t%s\t%s\t%08x\t%d",
						 FilterCell( filter ).c_str(), iEntIndex, iChannel, Cell( pSample ).c_str(), B( flVolume ),
						 (int)iSoundlevel, iFlags, iPitch, iSpecialDSP, VecCell( pOrigin ).c_str(),
						 VecCell( pDirection ).c_str(), B( soundtime ), speakerentity );
	}

	virtual void EmitSentenceByIndex( IRecipientFilter &, int, int, int, float, soundlevel_t, int = 0, int = PITCH_NORM,
									  int = 0, const Vector * = NULL, const Vector * = NULL,
									  CUtlVector<Vector> * = NULL, bool = true, float = 0.0f, int = -1 )
	{
	}

	virtual void StopSound( int iEntIndex, int iChannel, const char *pSample )
	{
		SoundEmitterLog( "O\tStopSound\t%d\t%d\t%s", iEntIndex, iChannel, Cell( pSample ).c_str() );
	}

	virtual void StopAllSounds( bool ) {}

	virtual void SetRoomType( IRecipientFilter &, int ) {}

	virtual void SetPlayerDSP( IRecipientFilter &, int, bool ) {}

	virtual void EmitAmbientSound( const char *pSample, float flVolume, int iPitch = PITCH_NORM, int flags = 0,
								   float soundtime = 0.0f )
	{
		SoundEmitterLog( "O\tAmbient\t%s\t%08x\t%d\t%d\t%08x", Cell( pSample ).c_str(), B( flVolume ), iPitch, flags,
						 B( soundtime ) );
	}

	virtual float GetDistGainFromSoundLevel( soundlevel_t, float ) { return 0.0f; }

	virtual int GetGuidForLastSoundEmitted() { return 0; }

	virtual bool IsSoundStillPlaying( int ) { return false; }

	virtual void StopSoundByGuid( int ) {}

	virtual void SetVolumeByGuid( int, float ) {}

	virtual void GetActiveSounds( CUtlVector<SndInfo_t> & ) {}

	virtual void PrecacheSentenceGroup( const char * ) {}

	virtual void NotifyBeginMoviePlayback() {}

	virtual void NotifyEndMoviePlayback() {}

	virtual IAudioOutputStream *CreateOutputStream( uint, uint, uint ) { return NULL; }

	virtual void DestroyOutputStream( IAudioOutputStream * ) {}

	virtual void ManualUpdate( const AudioState_t * ) {}

	virtual void ExtraUpdate() {}
};

CHarnessEngineSound s_EngineSound;

//-----------------------------------------------------------------------------
// The console: closecaption is found by name.
//-----------------------------------------------------------------------------
class CHarnessCvar final : public ICvar
{
public:
	virtual bool Connect( CreateInterfaceFn ) { return true; }

	virtual void Disconnect() {}

	virtual void *QueryInterface( const char * ) { return NULL; }

	virtual InitReturnVal_t Init() { return INIT_OK; }

	virtual void Shutdown() {}

	virtual CVarDLLIdentifier_t AllocateDLLIdentifier() { return 1; }

	virtual void RegisterConCommand( ConCommandBase * ) {}

	virtual void UnregisterConCommand( ConCommandBase * ) {}

	virtual void UnregisterConCommands( CVarDLLIdentifier_t ) {}

	virtual const char *GetCommandLineValue( const char * ) { return NULL; }

	virtual ConCommandBase *FindCommandBase( const char * ) { return NULL; }

	virtual const ConCommandBase *FindCommandBase( const char * ) const { return NULL; }

	virtual ConVar *FindVar( const char *pszName );

	virtual const ConVar *FindVar( const char * ) const { return NULL; }

	virtual ConCommand *FindCommand( const char * ) { return NULL; }

	virtual const ConCommand *FindCommand( const char * ) const { return NULL; }

	virtual ConCommandBase *GetCommands( void ) { return NULL; }

	virtual const ConCommandBase *GetCommands( void ) const { return NULL; }

	virtual void InstallGlobalChangeCallback( FnChangeCallback_t ) {}

	virtual void RemoveGlobalChangeCallback( FnChangeCallback_t ) {}

	virtual void CallGlobalChangeCallbacks( ConVar *, const char *, float ) {}

	virtual void InstallConsoleDisplayFunc( IConsoleDisplayFunc * ) {}

	virtual void RemoveConsoleDisplayFunc( IConsoleDisplayFunc * ) {}

	virtual void ConsoleColorPrintf( const Color &, const char *, ... ) const {}

	virtual void ConsolePrintf( const char *, ... ) const {}

	virtual void ConsoleDPrintf( const char *, ... ) const {}

	virtual void RevertFlaggedConVars( int ) {}

	virtual void InstallCVarQuery( ICvarQuery * ) {}

	virtual void PublishToVXConsole() {}

	virtual bool IsMaterialThreadSetAllowed() const { return true; }

	virtual void QueueMaterialThreadSetValue( ConVar *, const char * ) {}

	virtual void QueueMaterialThreadSetValue( ConVar *, int ) {}

	virtual void QueueMaterialThreadSetValue( ConVar *, float ) {}

	virtual bool HasQueuedMaterialThreadConVarSets() const { return false; }

	virtual int ProcessQueuedMaterialThreadConVarSets() { return 0; }

	virtual ICVarIteratorInternal *FactoryInternalIterator( void ) { return NULL; }
};

CHarnessCvar s_Cvar;

IStubEngineClient s_EngineClient;
IStubFileSystem s_FileSystem;
CStubClientEntityList s_EntityList;
CHudCloseCaption s_CloseCaption;

} // namespace

//-----------------------------------------------------------------------------
// The globals the subject names.
//-----------------------------------------------------------------------------
SoundEmitterGlobals_t *gpGlobals = &s_Globals;

IEngineSound *enginesound = &s_EngineSound;
IStubEngineClient *engine = &s_EngineClient;
IStubFileSystem *filesystem = &s_FileSystem;
ISoundEmitterSystemBase *soundemitterbase = &s_SoundEmitterBase;

// c_baseentity.cpp's, which the client's close captions read by name.
ConVar closecaption( "closecaption", "0", 0, "Enable close captioning." );

ConVar *CHarnessCvar::FindVar( const char *pszName )
{
	return !strcmp( pszName, "closecaption" ) ? &closecaption : NULL;
}

const char *IStubEngineClient::GetLevelName()
{
	SoundEmitterLog( "A\tLevelName\t=\t%s", Cell( s_pszLevelName ).c_str() );
	return s_pszLevelName;
}

int IStubEngineClient::SentenceIndexFromName( const char *pszName )
{
	std::map<std::string, int>::const_iterator it = s_Sentences.find( pszName ? pszName : "" );
	const int n = it == s_Sentences.end() ? -1 : it->second;
	SoundEmitterLog( "A\tSentence\t%s\t=\t%d", Cell( pszName ).c_str(), n );
	return n;
}

bool IStubFileSystem::FileExists( const char *pFileName, const char *pPathID )
{
	bool ok = false;
	for ( size_t i = 0; i < s_Files.size(); i++ )
		ok = ok || s_Files[i] == ( pFileName ? pFileName : "" );
	SoundEmitterLog( "A\tFileExists\t%s\t%s\t=\t%d", Cell( pFileName ).c_str(), Cell( pPathID ).c_str(), ok ? 1 : 0 );
	return ok;
}

CStubClientEntityList &ClientEntityList()
{
	return s_EntityList;
}

C_BaseEntity *C_BaseEntity::Instance( int iEnt )
{
	std::map<int, C_BaseEntity>::iterator it = s_Entities.find( iEnt );
	return it == s_Entities.end() ? NULL : &it->second;
}

bool C_BaseEntity::IsPrecacheAllowed()
{
	return true;
}

// c_baseentity.cpp's client body: closecaption off empties the filter.
void C_BaseEntity::RemoveRecipientsIfNotCloseCaptioning( C_RecipientFilter &filter )
{
	if ( !closecaption.GetBool() )
		filter.Reset();
}

void C_RecipientFilter::CopyFrom( const C_RecipientFilter &src )
{
	m_bReliable = src.IsReliable();
	for ( int i = 0; i < src.GetRecipientCount(); i++ )
		m_Recipients.AddToTail( src.GetRecipientIndex( i ) );
}

void C_RecipientFilter::Reset()
{
	m_bReliable = false;
	m_Recipients.RemoveAll();
}

void C_RecipientFilter::AddAllPlayers()
{
	if ( s_nLocalSlot < 1 )
		return;
	m_Recipients.RemoveAll();
	AddRecipientByPlayerIndex( s_nLocalSlot );
}

void C_RecipientFilter::AddRecipientByPlayerIndex( int nSlot )
{
	if ( m_Recipients.Find( nSlot ) == m_Recipients.InvalidIndex() )
		m_Recipients.AddToTail( nSlot );
}

bool CHudCloseCaption::ProcessCaption( char const *tokenname, float duration, bool fromplayer, bool direct )
{
	SoundEmitterLog( "O\tCaption\t%s\t%08x\t%d\t%d", Cell( tokenname ).c_str(), B( duration ), fromplayer ? 1 : 0,
					 direct ? 1 : 0 );
	return true;
}

void *SoundEmitterHudElement( const char *pszName )
{
	return ( s_bHud && !strcmp( pszName, "CHudCloseCaption" ) ) ? &s_CloseCaption : NULL;
}

const char *GetCleanMapName( const char *pszUnCleanMapName, char ( & )[256] )
{
	return pszUnCleanMapName;
}

// CSoundParametersInternal's out-of-line members live in the soundemittersystem
// library, which the test does not link: the harness's own, over the header's
// storage contract (one sound file packed into the pointer, more in an array).
CSoundParametersInternal::CSoundParametersInternal()
{
	m_pSoundNames = NULL;
	m_pConvertedNames = NULL;
	m_nSoundNames = 0;
	m_nConvertedNames = 0;
	channel = CHAN_AUTO;
	delay_msec = 0;
	play_to_owner_only = false;
	had_missing_wave_files = false;
	uses_gender_token = false;
	m_bShouldPreload = false;
	reserved = 0;
}

CSoundParametersInternal::~CSoundParametersInternal()
{
	if ( m_nSoundNames > 1 )
		free( m_pSoundNames );
	if ( m_nConvertedNames > 1 )
		free( m_pConvertedNames );
}

void CSoundParametersInternal::AddToTail( SoundFile **pDest, uint16 *pDestCount, const SoundFile &source )
{
	const uint16 n = *pDestCount;
	if ( n == 0 )
	{
		*(SoundFile *)pDest = source;
	}
	else
	{
		SoundFile *pArray = (SoundFile *)malloc( ( n + 1 ) * sizeof( SoundFile ) );
		if ( n == 1 )
			pArray[0] = *(SoundFile *)pDest;
		else
		{
			for ( uint16 i = 0; i < n; i++ )
				pArray[i] = ( *pDest )[i];
			free( *pDest );
		}
		pArray[n] = source;
		*pDest = pArray;
	}
	*pDestCount = n + 1;
}

//-----------------------------------------------------------------------------
// THE SUBJECT.
//-----------------------------------------------------------------------------
#include "SoundEmitterSystem.cpp"

IGameSystem::~IGameSystem() {}

namespace
{

//-----------------------------------------------------------------------------
// The scenario's world, set row by row.
//-----------------------------------------------------------------------------
void Script( const char *pszName, int nChannel, float flVolume, int nLevel, int nPitch, int nPitchLow, int nPitchHigh,
			 int nDelay, std::vector<const char *> waves, bool bParamsFail = false )
{
	Script_t s;
	s.pszName = pszName;
	s.nChannel = nChannel;
	s.flVolume = flVolume;
	s.nLevel = nLevel;
	s.nPitch = nPitch;
	s.nPitchLow = nPitchLow;
	s.nPitchHigh = nPitchHigh;
	s.nDelayMsec = nDelay;
	s.waves = waves;
	s.bParamsFail = bParamsFail;
	s_Scripts.push_back( s );

	CSoundParametersInternal *pInternal = new CSoundParametersInternal;
	pInternal->SetChannel( nChannel );
	for ( size_t i = 0; i < waves.size(); i++ )
	{
		SoundFile file;
		file.symbol = CUtlSymbol( (UtlSymId_t)WaveSymbol( waves[i] ) );
		pInternal->AddSoundName( file );
	}
	s_Internals.push_back( pInternal );

	std::string w;
	for ( size_t i = 0; i < waves.size(); i++ )
		w += ( i ? "," : "" ) + std::string( waves[i] );
	SoundEmitterLog( "SCRIPT\t%d\t%s\t%d\t%08x\t%d\t%d\t%d\t%d\t%s\t%d", (int)s_Scripts.size() - 1, pszName, nChannel,
					 B( flVolume ), nLevel, nPitch, nDelay, (int)waves.size(), w.empty() ? "-" : w.c_str(),
					 bParamsFail ? 1 : 0 );
}

void Duration( const char *pszSample, float f )
{
	s_Durations[pszSample] = f;
	SoundEmitterLog( "DUR\t%s\t%08x", pszSample, B( f ) );
}

void Entity( int nIndex, const char *pszModel, bool bPlayer, int nOwner )
{
	C_BaseEntity &e = s_Entities[nIndex];
	e.m_nIndex = nIndex;
	e.m_pszModel = pszModel;
	e.m_bPlayer = bPlayer;
	e.m_pOwner = nOwner >= 0 ? &s_Entities[nOwner] : NULL;
	SoundEmitterLog( "ENT\t%d\t%s\t%d\t%d", nIndex, Cell( pszModel ).c_str(), bPlayer ? 1 : 0, nOwner );
}

void Globals( int nMaxClients, float flCurtime )
{
	s_Globals.maxClients = nMaxClients;
	s_Globals.curtime = flCurtime;
	SoundEmitterLog( "G\t%d\t%08x", nMaxClients, B( flCurtime ) );
}

void Var( ConVar &var, const char *pszValue )
{
	var.SetValue( pszValue );
	SoundEmitterLog( "V\t%s\t%s", var.GetName(), var.GetString() );
}

void Hud( bool bPresent )
{
	s_bHud = bPresent;
	SoundEmitterLog( "HUD\t%d", bPresent ? 1 : 0 );
}

void Local( int nSlot )
{
	s_nLocalSlot = nSlot;
	SoundEmitterLog( "LOCAL\t%d", nSlot );
}

// A filter as the caller built it: these slots, reliable or not.
struct FilterSpec_t
{
	std::vector<int> slots;
	bool bReliable;
};

FilterSpec_t Filter( std::vector<int> slots, bool bReliable = false )
{
	FilterSpec_t f;
	f.slots = slots;
	f.bReliable = bReliable;
	return f;
}

void Build( const FilterSpec_t &spec, C_RecipientFilter &f )
{
	for ( size_t i = 0; i < spec.slots.size(); i++ )
		f.m_Recipients.AddToTail( spec.slots[i] );
	f.m_bReliable = spec.bReliable;
}

std::string Opt( const char *psz )
{
	return Cell( psz );
}

//-----------------------------------------------------------------------------
// The calls, each written down as it is made, its results after.
//-----------------------------------------------------------------------------

// GameSound_Emit( filter, ent, name, origin, soundtime ).
void EmitName( const FilterSpec_t &spec, int nEnt, const char *pszName, const Vector *pOrigin = NULL,
			   float flTime = 0.0f )
{
	C_RecipientFilter filter;
	Build( spec, filter );
	SoundEmitterLog( "C\temitname\t%s\t%d\t%s\t%s\t%08x", FilterCell( filter ).c_str(), nEnt, Opt( pszName ).c_str(),
					 VecCell( pOrigin ).c_str(), B( flTime ) );
	C_BaseEntity::EmitSound( filter, nEnt, pszName, pOrigin, flTime, NULL );
}

// GameSound_Emit( filter, ent, name, handle, origin, soundtime ).
void EmitNameHandle( const FilterSpec_t &spec, int nEnt, const char *pszName, HSOUNDSCRIPTHANDLE &handle,
					 const Vector *pOrigin = NULL, float flTime = 0.0f )
{
	C_RecipientFilter filter;
	Build( spec, filter );
	SoundEmitterLog( "C\temitnamehandle\t%s\t%d\t%s\t%d\t%s\t%08x", FilterCell( filter ).c_str(), nEnt,
					 Opt( pszName ).c_str(), (int)handle, VecCell( pOrigin ).c_str(), B( flTime ) );
	C_BaseEntity::EmitSound( filter, nEnt, pszName, handle, pOrigin, flTime, NULL );
	SoundEmitterLog( "R\thandle\t%d", (int)handle );
}

struct Ep_t
{
	int nChannel = 0;
	const char *pszName = NULL;
	float flVolume = VOL_NORM;
	int nLevel = SNDLVL_NONE;
	int nFlags = 0;
	int nPitch = PITCH_NORM;
	int nDsp = 0;
	const Vector *pOrigin = NULL;
	float flTime = 0.0f;
	bool bWantDuration = false;
	bool bCaption = true;
	bool bWarnCaption = false;
	bool bWarnDirect = false;
	int nSpeaker = -1;
	int nHandle = -1;
};

void FillEp( const Ep_t &in, EmitSound_t &ep, float *pflDuration )
{
	ep.m_nChannel = in.nChannel;
	ep.m_pSoundName = in.pszName;
	ep.m_flVolume = in.flVolume;
	ep.m_SoundLevel = (soundlevel_t)in.nLevel;
	ep.m_nFlags = in.nFlags;
	ep.m_nPitch = in.nPitch;
	ep.m_nSpecialDSP = in.nDsp;
	ep.m_pOrigin = in.pOrigin;
	ep.m_flSoundTime = in.flTime;
	ep.m_pflSoundDuration = in.bWantDuration ? pflDuration : NULL;
	ep.m_bEmitCloseCaption = in.bCaption;
	ep.m_bWarnOnMissingCloseCaption = in.bWarnCaption;
	ep.m_bWarnOnDirectWaveReference = in.bWarnDirect;
	ep.m_nSpeakerEntity = in.nSpeaker;
	ep.m_hSoundScriptHandle = (HSOUNDSCRIPTHANDLE)in.nHandle;
}

std::string EpCell( const Ep_t &in )
{
	char sz[1024];
	snprintf( sz, sizeof( sz ), "%d\t%s\t%08x\t%d\t%d\t%d\t%d\t%s\t%08x\t%d\t%d\t%d\t%d\t%d\t%d", in.nChannel,
			  Opt( in.pszName ).c_str(), B( in.flVolume ), in.nLevel, in.nFlags, in.nPitch, in.nDsp,
			  VecCell( in.pOrigin ).c_str(), B( in.flTime ), in.bWantDuration ? 1 : 0, in.bCaption ? 1 : 0,
			  in.bWarnCaption ? 1 : 0, in.bWarnDirect ? 1 : 0, in.nSpeaker, in.nHandle );
	return sz;
}

// GameSound_Emit( filter, ent, params ).
void EmitParams( const FilterSpec_t &spec, int nEnt, const Ep_t &in )
{
	C_RecipientFilter filter;
	Build( spec, filter );
	SoundEmitterLog( "C\temitparams\t%s\t%d\t%s", FilterCell( filter ).c_str(), nEnt, EpCell( in ).c_str() );
	EmitSound_t ep;
	float flDuration = -1.0f;
	FillEp( in, ep, &flDuration );
	C_BaseEntity::EmitSound( filter, nEnt, ep );
	if ( in.bWantDuration )
		SoundEmitterLog( "R\tduration\t%08x", B( flDuration ) );
	SoundEmitterLog( "R\tephandle\t%d", (int)ep.m_hSoundScriptHandle );
}

// GameSound_Emit( filter, ent, params, handle ).
void EmitParamsHandle( const FilterSpec_t &spec, int nEnt, const Ep_t &in, HSOUNDSCRIPTHANDLE &handle )
{
	C_RecipientFilter filter;
	Build( spec, filter );
	SoundEmitterLog( "C\temitparamshandle\t%s\t%d\t%s\t%d", FilterCell( filter ).c_str(), nEnt, EpCell( in ).c_str(),
					 (int)handle );
	EmitSound_t ep;
	float flDuration = -1.0f;
	FillEp( in, ep, &flDuration );
	C_BaseEntity::EmitSound( filter, nEnt, ep, handle );
	if ( in.bWantDuration )
		SoundEmitterLog( "R\tduration\t%08x", B( flDuration ) );
	SoundEmitterLog( "R\thandle\t%d", (int)handle );
}

// GameSound_Stop( ent, name ).
void StopName( int nEnt, const char *pszName )
{
	SoundEmitterLog( "C\tstopname\t%d\t%s", nEnt, Opt( pszName ).c_str() );
	C_BaseEntity::StopSound( nEnt, pszName );
}

// GameSound_Stop( ent, channel, sample ).
void StopChannel( int nEnt, int nChannel, const char *pszSample )
{
	SoundEmitterLog( "C\tstopchannel\t%d\t%d\t%s", nEnt, nChannel, Opt( pszSample ).c_str() );
	C_BaseEntity::StopSound( nEnt, nChannel, pszSample );
}

// UTIL_EmitAmbientSound.
void Ambient( int nEnt, const Vector &origin, const char *pszSample, float flVolume, int nLevel, int nFlags, int nPitch,
			  float flTime = 0.0f, bool bWantDuration = false )
{
	SoundEmitterLog( "C\tambient\t%d\t%s\t%s\t%08x\t%d\t%d\t%d\t%08x\t%d", nEnt, VecCell( &origin ).c_str(),
					 Opt( pszSample ).c_str(), B( flVolume ), nLevel, nFlags, nPitch, B( flTime ),
					 bWantDuration ? 1 : 0 );
	float flDuration = -1.0f;
	UTIL_EmitAmbientSound( nEnt, origin, pszSample, flVolume, (soundlevel_t)nLevel, nFlags, nPitch, flTime,
						   bWantDuration ? &flDuration : NULL );
	if ( bWantDuration )
		SoundEmitterLog( "R\tduration\t%08x", B( flDuration ) );
}

// GameSound_EmitCloseCaption( filter, ent, token, origins, duration, warn ).
void Caption( const FilterSpec_t &spec, int nEnt, const char *pszToken, float flDuration, bool bWarn )
{
	C_RecipientFilter filter;
	Build( spec, filter );
	SoundEmitterLog( "C\tcaption\t%s\t%d\t%s\t%08x\t%d", FilterCell( filter ).c_str(), nEnt, Opt( pszToken ).c_str(),
					 B( flDuration ), bWarn ? 1 : 0 );
	CUtlVector<Vector> origins;
	C_BaseEntity::EmitCloseCaption( filter, nEnt, pszToken, origins, flDuration, bWarn );
}

// GameSound_GetParameters( name, params, actormodel ).
void Parameters( const char *pszName, const char *pszActor )
{
	SoundEmitterLog( "C\tparams\t%s\t%s", Opt( pszName ).c_str(), Opt( pszActor ).c_str() );
	CSoundParameters params;
	const bool ok = C_BaseEntity::GetParametersForSound( pszName, params, pszActor );
	SoundEmitterLog( "R\tparams\t%d\t%d\t%08x\t%d\t%d\t%d\t%d\t%d\t%s", ok ? 1 : 0, params.channel, B( params.volume ),
					 (int)params.soundlevel, params.pitch, params.pitchlow, params.pitchhigh, params.delay_msec,
					 Cell( params.soundname ).c_str() );
}

// GameSound_PrecacheScript( name ).
void PrecacheScript( const char *pszName )
{
	SoundEmitterLog( "C\tprecachescript\t%s", Opt( pszName ).c_str() );
	SoundEmitterLog( "R\thandle\t%d", (int)C_BaseEntity::PrecacheScriptSound( pszName ) );
}

// The system's stages, as the client's game-system table runs them.
void Stage( const char *pszStage )
{
	SoundEmitterLog( "C\t%s", pszStage );
	if ( !strcmp( pszStage, "init" ) )
		SoundEmitterLog( "R\tinit\t%d", g_SoundEmitterSystem.Init() ? 1 : 0 );
	else if ( !strcmp( pszStage, "shutdown" ) )
		g_SoundEmitterSystem.Shutdown();
	else if ( !strcmp( pszStage, "levelinit" ) )
		g_SoundEmitterSystem.LevelInitPreEntity();
	else if ( !strcmp( pszStage, "levelinitpost" ) )
		g_SoundEmitterSystem.LevelInitPostEntity();
	else if ( !strcmp( pszStage, "levelshutdown" ) )
		g_SoundEmitterSystem.LevelShutdownPostEntity();
}

void Begin( const char *pszName )
{
	SoundEmitterLog( "S\t%s", pszName );
}

//-----------------------------------------------------------------------------
// The scenarios.
//-----------------------------------------------------------------------------
const Vector kOrigin( 128.0f, -64.5f, 32.25f );

void World()
{
	Begin( "world" );
	// CHAN_WEAPON 1, CHAN_VOICE 2, CHAN_BODY 4, CHAN_STATIC 6.
	Script( "Weapon_Pistol.Single", 1, 0.8f, 80, 100, 98, 102, 0,
			{ "weapons/pistol/pistol_fire2.wav", "weapons/pistol/pistol_fire3.wav" } );
	Script( "Player.FallDamage", 4, 1.0f, 75, 100, 100, 100, 0, { "player/pl_fallpain1.wav" } );
	Script( "Delayed.Thing", 0, 0.5f, 70, 95, 95, 95, 250, { "ambient/delayed.wav" } );
	Script( "Empty.Script", 0, 1.0f, 75, 100, 100, 100, 0, {} );
	Script( "Broken.Script", 0, 1.0f, 75, 100, 100, 100, 0, { "broken/never.wav" }, true );
	Script( "Npc.Voice", 2, 0.9f, 85, 100, 100, 100, 0, { "vo/none.wav", "vo/male.wav", "vo/female.wav" } );
	Script( "Ambient.Hum", 6, 0.7f, 60, 100, 100, 100, 0, { "ambient/hum.wav" } );
	Script( "HL2Player.Caption", 2, 1.0f, 75, 100, 100, 100, 0, { "vo/caption.wav" } );
	Duration( "weapons/pistol/pistol_fire2.wav", 0.35f );
	Duration( "player/pl_fallpain1.wav", 1.25f );
	Duration( "ambient/delayed.wav", 2.5f );
	Duration( "vo/male.wav", 3.0f );
	Duration( "vo/none.wav", 2.75f );
	Duration( "ambient/hum.wav", 4.0f );
	Duration( "vo/caption.wav", 1.5f );
	Duration( "weapons/raw/shot.WAV", 0.5f );
	Duration( "music/track.mp3", 90.0f );
	Duration( "!12", 1.75f );
	s_Sentences["HG_ALERT0"] = 12;
	SoundEmitterLog( "SENT\tHG_ALERT0\t12" );
	Entity( 3, "models/combine_male_07.mdl", false, -1 );
	Entity( 4, "models/female_01.mdl", false, 1 );
	Entity( 1, "models/player.mdl", true, -1 );
	Entity( 5, NULL, false, -1 );
	Entity( 6, "models/props/crate.mdl", false, 4 );
	Local( 1 );
	Hud( true );
	Globals( 16, 100.0f );
	Var( sv_soundemitter_trace, "0" );
	Var( closecaption, "0" );
}

void SystemStages()
{
	Begin( "system" );
	Stage( "init" );
	s_pszLevelName = "maps/DM_Lockdown.bsp";
	Stage( "levelinit" );
	Stage( "levelinitpost" );
	Stage( "levelshutdown" );
	s_Files.push_back( "maps/dm_overrides_level_sounds.txt" );
	s_pszLevelName = "maps\\dm_Overrides.bsp";
	Stage( "levelinit" );
	Stage( "levelshutdown" );
	s_pszLevelName = "dm_noprefix";
	Stage( "levelinit" );
	Stage( "levelshutdown" );
}

void RawWaves()
{
	Begin( "raw" );
	Var( sv_soundemitter_trace, "1" );
	EmitName( Filter( { 1 } ), 3, "weapons/raw/shot.WAV", &kOrigin, 101.5f );
	// WaveTrace shows a wave once.
	EmitName( Filter( { 1 } ), 3, "weapons/raw/shot.WAV" );
	EmitName( Filter( { 1 } ), 3, "weapons/raw/other.wav" );
	// The raw-wave arm is EmitSound_t's alone: a .wav warned about once.
	Ep_t wave;
	wave.nChannel = 1;
	wave.pszName = "weapons/raw/shot.WAV";
	wave.nLevel = 85;
	wave.pOrigin = &kOrigin;
	wave.bWarnDirect = true;
	EmitParams( Filter( { 1 } ), 3, wave );
	EmitParams( Filter( { 1 } ), 3, wave );
	Ep_t quiet;
	quiet.pszName = "weapons/raw/other.wav";
	EmitParams( Filter( { 1 } ), 3, quiet );
	Ep_t mp3;
	mp3.nChannel = 6;
	mp3.pszName = "music/track.mp3";
	mp3.flVolume = 0.25f;
	mp3.nLevel = 0;
	mp3.nFlags = SND_SHOULDPAUSE;
	mp3.nPitch = 110;
	mp3.nDsp = 3;
	mp3.flTime = 250.0f;
	mp3.bWantDuration = true;
	mp3.bWarnDirect = true;
	mp3.nSpeaker = 7;
	EmitParams( Filter( { 1 }, true ), 0, mp3 );
	Ep_t sentence;
	sentence.pszName = "!HG_ALERT0";
	sentence.nLevel = 75;
	sentence.bWarnDirect = true;
	EmitParams( Filter( {} ), 2, sentence );
	Var( sv_soundemitter_trace, "0" );
	EmitName( Filter( { 1 } ), 3, "weapons/raw/third.wav" );
	EmitName( Filter( { 1 } ), 3, NULL );
}

void Scripts()
{
	Begin( "scripts" );
	Var( sv_soundemitter_trace, "1" );
	EmitName( Filter( { 1 } ), 3, "No.Such.Script" );
	EmitName( Filter( { 1 } ), 0, "Weapon_Pistol.Single", &kOrigin );
	EmitName( Filter( { 1 } ), 99, "Weapon_Pistol.Single" );
	EmitName( Filter( {} ), 3, "Npc.Voice" );
	EmitName( Filter( { 1 } ), 4, "Npc.Voice" );
	EmitName( Filter( { 1 } ), 5, "Npc.Voice" );
	EmitName( Filter( { 1 } ), 6, "Npc.Voice" );
	EmitName( Filter( { 1 } ), 3, "Empty.Script" );
	EmitName( Filter( { 1 } ), 3, "Broken.Script" );
	// The delay: from now when no time is given, the caller's time otherwise.
	EmitName( Filter( { 1 } ), 3, "Delayed.Thing" );
	EmitName( Filter( { 1 } ), 3, "Delayed.Thing", NULL, 300.0f );
	Ep_t pitch;
	pitch.pszName = "Player.FallDamage";
	pitch.nFlags = SND_CHANGE_PITCH;
	pitch.nPitch = 80;
	pitch.flVolume = 0.1f;
	pitch.bWantDuration = true;
	EmitParams( Filter( { 1 } ), 1, pitch );
	Ep_t vol;
	vol.pszName = "Player.FallDamage";
	vol.nFlags = SND_CHANGE_VOL;
	vol.flVolume = 0.3f;
	vol.nPitch = 60;
	EmitParams( Filter( { 1 } ), 1, vol );
	Ep_t both;
	both.pszName = "Player.FallDamage";
	both.nFlags = SND_CHANGE_VOL | SND_CHANGE_PITCH | SND_STOP;
	both.flVolume = 0.0f;
	both.nPitch = 150;
	both.nSpeaker = 3;
	both.nDsp = 9;
	both.pOrigin = &kOrigin;
	EmitParams( Filter( { 1, 2 }, true ), 1, both );
	Ep_t handled;
	handled.pszName = "Weapon_Pistol.Single";
	handled.nHandle = 1; // a handle that names another script wins
	EmitParams( Filter( { 1 } ), 3, handled );
	HSOUNDSCRIPTHANDLE h = SOUNDEMITTER_INVALID_HANDLE;
	EmitNameHandle( Filter( { 1 } ), 3, "Weapon_Pistol.Single", h, &kOrigin );
	EmitNameHandle( Filter( { 1 } ), 3, "Weapon_Pistol.Single", h );
	HSOUNDSCRIPTHANDLE none = SOUNDEMITTER_INVALID_HANDLE;
	EmitNameHandle( Filter( { 1 } ), 3, "No.Such.Script", none );
	HSOUNDSCRIPTHANDLE h2 = SOUNDEMITTER_INVALID_HANDLE;
	Ep_t byHandle;
	byHandle.pszName = "Delayed.Thing";
	byHandle.bWantDuration = true;
	EmitParamsHandle( Filter( { 1 } ), 0, byHandle, h2 );
	HSOUNDSCRIPTHANDLE h3 = 6;
	Ep_t raw;
	raw.pszName = "weapons/raw/shot.WAV"; // a handle overload skips the raw-wave arm
	EmitParamsHandle( Filter( { 1 } ), 0, raw, h3 );
	Var( sv_soundemitter_trace, "0" );
	EmitName( Filter( { 1 } ), 3, "Weapon_Pistol.Single" );
}

void Captions()
{
	Begin( "captions" );
	// Multiplayer: never.
	EmitName( Filter( { 1 } ), 3, "HL2Player.Caption" );
	Caption( Filter( { 1 } ), 3, "HL2Player.Caption", 1.0f, false );
	// No player count at all (maxClients 0): the multiplayer test passes, so
	// the filter copy's closecaption removal is what stops it -- and with
	// closecaption on, it is shown.
	Globals( 0, 100.0f );
	EmitName( Filter( { 1 } ), 3, "HL2Player.Caption" );
	Caption( Filter( { 1 } ), 3, "HL2Player.Caption", 1.0f, false );
	Var( closecaption, "1" );
	EmitName( Filter( { 1 } ), 3, "HL2Player.Caption" );
	Var( closecaption, "0" );
	// One player, captions off: never.
	Globals( 1, 100.0f );
	EmitName( Filter( { 1 } ), 3, "HL2Player.Caption" );
	Caption( Filter( { 1 } ), 3, "HL2Player.Caption", 1.0f, false );
	// On.
	Var( closecaption, "1" );
	EmitName( Filter( { 1 } ), 3, "HL2Player.Caption" );
	EmitName( Filter( { 1 } ), 1, "HL2Player.Caption" );
	EmitName( Filter( { 1 } ), 6, "HL2Player.Caption" );
	EmitName( Filter( {} ), 3, "HL2Player.Caption" );
	Ep_t dur;
	dur.pszName = "Npc.Voice";
	dur.bWantDuration = true;
	EmitParams( Filter( { 1 } ), 3, dur );
	Ep_t off;
	off.pszName = "Npc.Voice";
	off.bCaption = false;
	EmitParams( Filter( { 1 } ), 3, off );
	Ep_t warn;
	warn.pszName = "Npc.Voice";
	warn.bWarnCaption = true;
	EmitParams( Filter( { 1 } ), 4, warn );
	Ep_t modulated;
	modulated.pszName = "Npc.Voice";
	modulated.nFlags = SND_CHANGE_VOL;
	EmitParams( Filter( { 1 } ), 3, modulated );
	Caption( Filter( { 1 } ), 3, "HL2Player.Caption", 1.0f, false );
	Caption( Filter( { 1 } ), 1, "Npc.Voice", -1.0f, true );
	Caption( Filter( { 1 } ), 6, "No.Such.Token", -1.0f, false );
	Caption( Filter( { 1 } ), 3, "Mixed.CASE\\nSecond\\tLine\\", 0.5f, false );
	Caption( Filter( {} ), 3, "HL2Player.Caption", 1.0f, false );
	// No caption element.
	Hud( false );
	Caption( Filter( { 1 } ), 3, "HL2Player.Caption", 1.0f, false );
	EmitName( Filter( { 1 } ), 3, "HL2Player.Caption" );
	Hud( true );
	// Two players: never again.
	Globals( 2, 100.0f );
	Caption( Filter( { 1 } ), 3, "HL2Player.Caption", 1.0f, false );
	Globals( 16, 100.0f );
	Var( closecaption, "0" );
}

void Stops()
{
	Begin( "stops" );
	Var( sv_soundemitter_trace, "1" );
	StopChannel( 3, 1, "weapons/raw/shot.WAV" );
	StopChannel( 3, 6, "music/track.mp3" );
	StopChannel( 3, 2, "!HG_ALERT0" );
	StopChannel( 3, 0, "Weapon_Pistol.Single" );
	StopChannel( 3, 0, "No.Such.Script" );
	StopName( 4, "Npc.Voice" );
	StopName( 4, "Empty.Script" );
	StopName( 4, "No.Such.Script" );
	Var( sv_soundemitter_trace, "0" );
	StopName( 4, "Player.FallDamage" );
}

void Ambients()
{
	Begin( "ambient" );
	Var( sv_soundemitter_trace, "1" );
	Ambient( 3, kOrigin, "!HG_ALERT0", 0.75f, 80, 0, 105, 0.0f, true );
	Ambient( 3, kOrigin, "!NO_SUCH", 0.75f, 80, 0, 105, 0.0f, true );
	Ambient( 3, kOrigin, "!HG_ALERT0", 0.75f, 80, 0, 105 );
	Ambient( 3, kOrigin, "weapons/raw/shot.WAV", 0.5f, 70, SND_SHOULDPAUSE, 90, 110.0f, true );
	Ambient( 3, kOrigin, "music/track.mp3", 0.5f, 70, 0, 90 );
	Ambient( 3, kOrigin, "Ambient.Hum", 0.2f, 70, 0, 50, 0.0f, true );
	Ambient( 3, kOrigin, "Ambient.Hum", 0.2f, 70, SND_CHANGE_VOL, 50 );
	Ambient( 3, kOrigin, "Ambient.Hum", 0.2f, 70, SND_CHANGE_PITCH, 50, 0.0f, true );
	Ambient( 3, kOrigin, "Ambient.Hum", 0.0f, 70, SND_STOP, 50 );
	Ambient( 3, kOrigin, "No.Such.Script", 0.2f, 70, 0, 50, 0.0f, true );
	// The script's caption goes to every player, reliably.
	Globals( 1, 100.0f );
	Var( closecaption, "1" );
	Ambient( 3, kOrigin, "Ambient.Hum", 0.2f, 70, 0, 50 );
	Local( 0 );
	Ambient( 3, kOrigin, "Ambient.Hum", 0.2f, 70, 0, 50 );
	Local( 1 );
	Var( closecaption, "0" );
	Globals( 16, 100.0f );
	Var( sv_soundemitter_trace, "0" );
	Ambient( 3, kOrigin, "Ambient.Hum", 0.2f, 70, 0, 50 );
}

void Lookups()
{
	Begin( "lookups" );
	Parameters( "Npc.Voice", NULL );
	Parameters( "Npc.Voice", "models/female_01.mdl" );
	Parameters( "Npc.Voice", "models/male_02.mdl" );
	Parameters( "No.Such.Script", NULL );
	Parameters( "Broken.Script", NULL );
	PrecacheScript( "Ambient.Hum" );
	PrecacheScript( "No.Such.Script" );
	Stage( "shutdown" );
}

std::string RunScenarioOnce()
{
	g_Log.clear();
	s_Scripts.clear();
	s_Durations.clear();
	s_Sentences.clear();
	s_Entities.clear();
	s_Files.clear();
	s_WaveNames.clear();
	for ( size_t i = 0; i < s_Internals.size(); i++ )
		delete s_Internals[i];
	s_Internals.clear();
	s_pszLevelName = "";

	g_pCVar = &s_Cvar;
	cvar = &s_Cvar;
	SpewOutputFunc( CaptureSpew );
	SpewActivate( "developer", 1 );

	SoundEmitterLog(
		"# soundemitter-client-stock.tsv: written by src/test/oracle/native/soundemitter_client_oracle_test.cpp" );
	SoundEmitterLog(
		"# from the pristine game/shared/SoundEmitterSystem.cpp (CLIENT_DLL); README-soundemitter-client-stock.md says how to read it." );
	World();
	SystemStages();
	RawWaves();
	Scripts();
	Captions();
	Stops();
	Ambients();
	Lookups();
	SoundEmitterLog( "END" );

	SpewOutputFunc( NULL );
	return g_Log;
}

// Once per process: the subject's WaveTrace keeps a file-static "shown once"
// set, so a second run would not show the first run's direct waves again.
// Every test reads the one run.
const std::string &RunScenario()
{
	static const std::string s_Run = RunScenarioOnce();
	return s_Run;
}

std::string RepoFile( const char *pszRel )
{
	const char *pszRepo = getenv( "SOURCESHARP_REPO" );
	return std::string( pszRepo ? pszRepo : "." ) + "/" + pszRel;
}

std::string GoldenPath()
{
	return RepoFile( "src/test/client/Fixtures/soundemitter-client-stock.tsv" );
}

std::string MarkerPath()
{
	return RepoFile( "src/test/oracle/native/soundemitter_client_golden.regen" );
}

} // namespace

TEST( SoundEmitterClientOracle, StockProducesTheGolden )
{
	const std::string &run = RunScenario();

	if ( std::ifstream( MarkerPath() ).good() )
	{
		std::ofstream out( GoldenPath() );
		out << run;
	}

	std::ifstream in( GoldenPath() );
	ASSERT_TRUE( in.good() ) << GoldenPath() << " is missing";
	std::stringstream ss;
	ss << in.rdbuf();
	const std::string golden = ss.str();

	ASSERT_GT( golden.size(), 10000u ) << "the golden is empty or truncated";

	std::istringstream a( run ), b( golden );
	std::string la, lb;
	int n = 0;
	while ( true )
	{
		const bool ha = (bool)std::getline( a, la );
		const bool hb = (bool)std::getline( b, lb );
		++n;
		if ( !ha && !hb )
			break;
		ASSERT_TRUE( ha && hb && la == lb )
			<< "line " << n << ": stock now writes\n  " << ( ha ? la : "<end>" ) << "\nand the golden has\n  "
			<< ( hb ? lb : "<end>" ) << "\nDo NOT regenerate the golden to make this pass; see the file header.";
	}
}

// Each arm the scenario exists to reach, found in the run: a check that the
// golden is not silently a recording of one path.
TEST( SoundEmitterClientOracle, TheScenarioReachesEveryArm )
{
	const std::string &run = RunScenario();

	struct Arm_t
	{
		const char *pszNeedle;
		const char *pszWhat;
	};

	static const Arm_t kArms[] = {
		{ "\nA\tModInit\t=\t1\n", "Init loaded the scripts" },
		{ "\nO\tModShutdown\n", "Shutdown unloaded them" },
		{ "\nO\tAddOverrides\tmaps/dm_overrides_level_sounds.txt\t0\n", "a level's overrides were added" },
		{ "\nA\tFileExists\tmaps/dm_lockdown_level_sounds.txt\tGAME\t=\t0\n",
		  "the map name was lowered and asked for" },
		{ "\nO\tClearOverrides\n", "a level's overrides were cleared" },
		{ "\nD\tEmitsound directly referenced wave weapons/raw/shot.WAV (should use game_sounds.txt system instead)\\n\n",
		  "a direct wave was traced" },
		{ "\nM\t(cl) EmitSound:  Raw wave emitted 'weapons/raw/shot.WAV' (ent 3)\\n\n", "the raw-wave trace" },
		{ "\nM\t(cl) EmitSound:  'Weapon_Pistol.Single' emitted as 'weapons/pistol/pistol_fire2.wav' (ent 0)\\n\n",
		  "the script trace" },
		{ "\nO\tEmitSound\t1/0\t3\t0\tambient/delayed.wav\t", "a delayed script was emitted" },
		{ "\nA\tParamsEx\tNpc.Voice\t-1\t2\t1\t=>5\t=\t1\t", "a female speaker's wave was asked for" },
		{ "\nA\tParamsEx\tNpc.Voice\t-1\t1\t1\t=>5\t=\t1\t", "a male speaker's wave was asked for" },
		{ "\nO\tCaption\thl2player.caption\t", "a caption reached the HUD" },
		{ "\nO\tCaption\tnpc.voice\t", "a script's caption reached the HUD" },
		{ "\nO\tCaption\thl2player.caption\t3fc00000\t1\t0\n", "a caption was from a player" },
		{ "\nO\tStopSound\t4\t2\tvo/male.wav\n", "every wave of a script was stopped" },
		{ "\nM\t(cl) StopSound:  Raw wave stopped 'weapons/raw/shot.WAV' (ent 3)\\n\n", "the raw stop trace" },
		{ "\nO\tAmbient\t!12\t", "a sentence was played by its index" },
		{ "\nO\tAmbient\tambient/hum.wav\t", "an ambient script was played" },
		{ "\nM\t(cl) UTIL_EmitAmbientSound:  Sentence emitted '!12' (ent 3)\\n\n", "the sentence trace" },
		{ "\nR\tparams\t1\t2\t", "a script's parameters were answered" },
		{ "\nR\thandle\t6\n", "PrecacheScriptSound answered the index" },
	};
	for ( size_t i = 0; i < sizeof( kArms ) / sizeof( kArms[0] ); i++ )
		EXPECT_NE( run.find( kArms[i].pszNeedle ), std::string::npos ) << kArms[i].pszWhat << ": no";
}
