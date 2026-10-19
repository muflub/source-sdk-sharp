//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp =======================================================
// SS_REASON e  a synchronous mid-step answer
//
// THE SERVER'S SOUND ENVELOPE CONTROLLER, WHICH IS A SHIM
// -- plan_gamedll_after.md Phase A3, second pair.
//
// CSoundControllerImp was row 13 of kServerSystems[] and
// `game/shared/soundenvelope.cpp` was in server_csharp.vpc. Neither is true any
// more. The RAMPS -- a volume or a pitch approaching a target over a number of
// seconds -- the sound list, the time-sorted command queue and the
// envelope-point expansion are `SoundController` in SourceSharp.Server, an
// IFrameLifecycle participant at FrameStage.SoundController and an
// IGameLifecycle participant at LifecycleStage.SoundController.
//
// What is here is the half that is genuinely the ENGINE's, and the split runs
// exactly along that line: a CSoundPatch is a recipient filter, an entity
// handle, a sound-script lookup and an EmitSound_t. None of those four can
// cross this boundary, and none of them is a ramp.
//
//=============================================================================
// WHY soundenvelope.cpp LEFT THE SERVER'S PROJECT.
//
// It compiles into BOTH binaries and the class is ALREADY split down the middle
// by #ifdef CLIENT_DLL -- two base classes, two constructors, two per-frame
// stages. Carving the server's arm further is precisely T1's recorded failure
// mode: it compiles green on the server and silently stops initialising the
// client's copy.
//
// So the file left this project instead, which is decals.cpp's shape, and the
// client's copy is textually untouched. It is proved rather than argued by
// src/sourcesharp/test/client_shared_parity.sh, which preprocesses both
// revisions with -DCLIENT_DLL and diffs.
//
// WHAT THAT COSTS, and it is a real cost rather than a formality. The client
// keeps stock's whole CSoundPatch; the server now has the reduced one below.
// Two native implementations of "build an EmitSound_t and call GameSound_Emit"
// exist in the tree and nothing keeps them in step. That is worse than
// decals.cpp's outcome, where the server ended up with NO native copy at all,
// and it is the price of the file being shared while its consumers are not.
//
// THE ALTERNATIVE WAS MEASURED, NOT ASSUMED. The compiled consumers of
// CSoundEnvelopeController on this side are six calls in two files, and both
// files are SERVER-ONLY:
//
//     game/server/physics.cpp:2681,3567,3569,3574,3575   friction sounds
//     game/server/gameinterface.cpp:3067                 CheckLoopingSoundsForPlayer
//
// (game/server/physics.cpp and game/client/physics.cpp are DIFFERENT FILES -- a
// bare $File is project-relative -- so neither call site is shared.
// physconstraint.cpp names the controller too and is not in server_csharp.vpc.
// sourcesharp/game/server/shell_worldquery.cpp includes soundenvelope.h and
// uses nothing from it.)
//
// Six call sites in two server-only files is what made the removal cheap: the
// interface pointer keeps its spelling and no caller was edited.
//
//=============================================================================
// THE FIVE METHODS ITS ONE CALLER USES, AND NOTHING ELSE (P5 lane SP).
// CSoundEnvelopeController's other methods were implemented here because the
// class was abstract and its pointer was the tree's spelling. Its last server
// caller, physics.cpp's friction sounds, asks ss::SoundController()
// (ss_soundcontroller.h) now, and nm found no server object importing
// GetController() after that, so the Valve class, its sixteen other methods and
// GetSoundSaveRestoreOps left. The managed SoundController still owns the
// commands, envelopes and fades; nothing native asks for them.
//
//=============================================================================
// NO FILE-SCOPE STATE. The patch table is a MEMBER of the controller object,
// and that object is a function-local static -- the population A1's gate
// deliberately does not cover and the project rule permits, and the same shape
// shell_decals.cpp uses for the same reason.
//
//=============================================================================

#include "ss_soundcontroller.h"
#include "ss_recipient_filter.h"
#include "engine/IEngineSound.h"
#include "SoundEmitterSystem/isoundemittersystembase.h"
#include "gamesound.h"
#include "ss_gamestringpool.h"
#include "shell_diagnostics.h"
#include "sourcesharp.h"
#include "shell_server.h"
#include "utlvector.h"
#include "slot_table.h"
#include "shell_standalone.h"
#include "ss_string.h"
#include "shell_resolve.h"
#include "ss_physics_shared.h"   // physprops, for a surface's scrape sound


#include "imanaged_areas.h"   // plan_engineapi_vtables E6: IManagedSoundController

//-----------------------------------------------------------------------------
// A COPY of the filter the caller built.
//
// Stock's CCopyRecipientFilter, reduced to what this side uses. The copy is not
// an optimisation: SoundCreate takes an `IRecipientFilter &` that is almost
// always a stack temporary at the call site -- PhysFrictionSound builds a
// CReliableBroadcastRecipientFilter and lets it die -- and the patch outlives it
// by minutes.
//-----------------------------------------------------------------------------
class CShellCopyFilter : public IRecipientFilter
{
public:
	CShellCopyFilter() : m_bReliable( false ), m_bInitMessage( false ) {}

	void Init( IRecipientFilter *pSource )
	{
		m_bReliable    = pSource->IsReliable();
		m_bInitMessage = pSource->IsInitMessage();

		m_Recipients.RemoveAll();

		for ( int i = 0; i < pSource->GetRecipientCount(); i++ )
			m_Recipients.AddToTail( pSource->GetRecipientIndex( i ) );
	}

	virtual bool IsReliable( void ) const OVERRIDE { return m_bReliable; }
	virtual bool IsInitMessage( void ) const OVERRIDE { return m_bInitMessage; }
	virtual int  GetRecipientCount( void ) const OVERRIDE { return m_Recipients.Count(); }

	virtual int GetRecipientIndex( int nSlot ) const OVERRIDE
	{
		if ( nSlot < 0 || nSlot >= m_Recipients.Count() )
			return -1;

		return m_Recipients[ nSlot ];
	}

	bool IsActive() const { return m_Recipients.Count() > 0; }

private:
	bool m_bReliable;
	bool m_bInitMessage;
	CUtlVector< int > m_Recipients;
};

//-----------------------------------------------------------------------------
// The server's CSoundPatch: what a patch IS once the ramps have gone.
//
// The name is stock's because soundenvelope.h forward-declares `class
// CSoundPatch;` and every caller on this side holds one only as a pointer. With
// game/shared/soundenvelope.cpp out of this project, this is the server's
// definition of that class and nothing on this side can tell.
//
// NO DATADESC and NO FIXEDSIZE ALLOCATOR. The datadesc served the save cluster,
// which this port refuses (plan_gamedll.md Part 7) and whose engine answers
// "Can't save multiplayer games" to everything that could produce a save; the
// pool allocator served a class that stock churns per gunshot, and this side
// creates one patch per physics friction pair.
//-----------------------------------------------------------------------------
class CSoundPatch
{
public:
	CSoundPatch()
	{
		m_nHandle           = 0;
		m_iszSoundName      = NULL_STRING;
		m_iszScriptName     = NULL_STRING;
		m_nChannel          = CHAN_AUTO;
		m_SoundLevel        = SNDLVL_NORM;
		m_flScriptVolume    = 1.0f;
		m_nBaseFlags        = 0;
		m_bPlaying          = false;
		m_flCaptionDuration = 2.0f;
	}

	void Init( IRecipientFilter *pFilter, IHandleEntity *pEnt, int nChannel,
	           const char *pszSoundName, soundlevel_t soundlevel )
	{
		// A PLAIN CBaseHandle::Set, and the member below is a plain
		// CBaseHandle (plan_entity.md 1e). Stock held a typed handle here and
		// this file already had to reach past it -- the typed Set takes a
		// CBaseEntity * and HIDES the base's, so a patch attached to an entity
		// outside the SDK stored nothing at all. With the member untyped there
		// is no hidden overload left to reach past, and the handle stores a
		// handle without an opinion about what is on the end of it.
		m_hEnt.Set( pEnt );
		m_nChannel = nChannel;

		// THE SOUND SCRIPT, and this is the piece that cannot cross. A script
		// entry carries the volume, the sound level and the channel the mod
		// author chose, and the actual .wav behind a name that may be a random
		// set -- resolved ONCE here, because resolving it per emit would pick a
		// different wav on every pitch change.
		CSoundParameters params;

		if ( !ss::StrIStr( pszSoundName, ".wav" ) && !ss::StrIStr( pszSoundName, ".mp3" )
			&& GameSound_GetParameters( pszSoundName, params, NULL ) )
		{
			m_flScriptVolume = params.volume;
			m_iszScriptName  = AllocPooledString( pszSoundName );

			pszSoundName = params.soundname;
			m_SoundLevel = params.soundlevel;
			m_nChannel   = params.channel;
		}
		else
		{
			m_iszScriptName  = AllocPooledString( pszSoundName );
			m_flScriptVolume = 1.0f;
			m_SoundLevel     = soundlevel;
		}

		m_iszSoundName = AllocPooledString( pszSoundName );
		m_bPlaying     = false;
		m_nBaseFlags   = 0;

		m_Filter.Init( pFilter );
	}

	int Handle() const { return m_nHandle; }
	void SetHandle( int nHandle ) { m_nHandle = nHandle; }

	void SetBaseFlags( int nFlags ) { m_nBaseFlags = nFlags; }
	void SetCaptionDuration( float flDuration ) { m_flCaptionDuration = flDuration; }
	string_t Name() const { return m_iszSoundName; }

	bool FilterIsActive() const { return m_Filter.IsActive(); }

	// stock CSoundPatch::EntIndex, resolved through the SLOT TABLE
	// (plan_entity.md 3.3) rather than through the entity list.
	//
	// THE HANDLE IS STILL WHAT IS ASKED, and the serial is still what makes a
	// stale one answer -1: SourceSharp_ShellFromHandle compares it. What is
	// gone is the pair of steps after the lookup -- a static_cast to
	// IServerUnknown and a virtual to reach the networkable and then the edict
	// -- because the table already knows which entry the shell is bound at, and
	// for an edict-backed entry that entry number IS the entity index
	// (slot_table.h: entries below MAX_EDICTS are edicts).
	//
	// A SERVER-ONLY entity is bound in the engine-invisible half of the table
	// and has no index to give, so it answers -1 -- the same answer the old
	// route gave it, by a different road: GetEdict() is NULL for one, and
	// ENTINDEX( NULL ) is -1.
	int EntIndex() const
	{
		if ( !SourceSharp_ShellFromHandle( m_hEnt ) )
			return -1;

		const int nEntry = m_hEnt.GetEntryIndex();

		return CSourceSharpSlotTable::IsEdictEntry( nEntry ) ? nEntry : -1;
	}

	// stock CSoundPatch::Update's emit, and StartSound's, which differ only in
	// the flags and in whether a caption goes with it.
	void Emit( float flVolume01, float flPitch, int nFlags, float flStartTime,
	           bool bEmitCaption )
	{
		const int nEntIndex = EntIndex();
		if ( nEntIndex < 0 || !m_Filter.IsActive() )
			return;

		ss::EmitSound_t ep;
		ep.m_nChannel     = m_nChannel;
		ep.m_pSoundName   = STRING( m_iszSoundName );

		// THE SCRIPT'S VOLUME IS APPLIED HERE and not on the managed side. A
		// patch's envelope runs 0..1 and multiplies the volume the sound script
		// chose; the script's number is this object's and managed code has never
		// seen it.
		ep.m_flVolume     = m_flScriptVolume * flVolume01;
		ep.m_SoundLevel   = m_SoundLevel;
		ep.m_nFlags       = nFlags;
		ep.m_nPitch       = (int)flPitch;

		if ( bEmitCaption )
		{
			ep.m_bEmitCloseCaption = false;

			if ( flStartTime != 0.0f )
				ep.m_flSoundTime = flStartTime;
		}

		GameSound_Emit( m_Filter, nEntIndex, ep );

		if ( bEmitCaption )
		{
			GameSound_EmitCloseCaption( m_Filter, nEntIndex, STRING( m_iszScriptName ),
			                            ep.m_UtlVecSoundOrigin, m_flCaptionDuration, true );
			m_bPlaying = true;
		}
	}

	// stock CSoundPatch::Shutdown. The m_bPlaying gate is stock's and is not
	// bookkeeping: StopSound for a sound that never started is a warning from
	// the engine and a real one -- it means somebody lost track of a patch.
	void Stop()
	{
		if ( !m_bPlaying )
			return;

		const int nEntIndex = EntIndex();
		if ( nEntIndex >= 0 )
			GameSound_Stop( nEntIndex, m_nChannel, STRING( m_iszSoundName ) );

		m_bPlaying = false;
	}


	int BaseFlags() const { return m_nBaseFlags; }

private:
	int              m_nHandle;
	CBaseHandle      m_hEnt;
	int              m_nChannel;
	string_t         m_iszSoundName;
	string_t         m_iszScriptName;
	soundlevel_t     m_SoundLevel;
	float            m_flScriptVolume;
	int              m_nBaseFlags;
	bool             m_bPlaying;
	float            m_flCaptionDuration;
	CShellCopyFilter m_Filter;
};

//-----------------------------------------------------------------------------
// The controller the rest of the server reaches through
// CSoundEnvelopeController::GetController().
//
// Twenty methods, and every one of them is either a forward to managed code or
// the one thing managed code cannot do -- build a patch out of a filter the
// caller owns.
//-----------------------------------------------------------------------------
// ss::ISoundController only. soundenvelope.h's CSoundEnvelopeController and
// its sixteen other methods left with their last caller: nm finds no server
// object importing GetController() once physics.cpp asks ss::SoundController(),
// and no engine reads the class.
class CShellSoundController : public ss::ISoundController
{
public:
	CShellSoundController() : m_nNextHandle( 1 ) {}

	//-- the table -----------------------------------------------------------
	//
	// A HANDLE RATHER THAN A POINTER, in both directions. The managed side
	// tracks patches by an int it was given, and this is the only place the two
	// meet. Handles are never reused -- m_nNextHandle only rises -- so a stale
	// handle from a freed patch resolves to nothing rather than to whichever
	// patch took its slot.
	CSoundPatch *Resolve( int nHandle )
	{
		for ( int i = 0; i < m_Patches.Count(); i++ )
		{
			if ( m_Patches[ i ]->Handle() == nHandle )
				return m_Patches[ i ];
		}

		return NULL;
	}

	CSoundPatch *Track( CSoundPatch *pSound )
	{
		pSound->SetHandle( m_nNextHandle++ );
		m_Patches.AddToTail( pSound );

		IManagedSoundController *pApi = Ask();
		if ( pApi )
			pApi->ServerSoundControllerCreate( pSound->Handle() );

		return pSound;
	}

	void Forget( CSoundPatch *pSound )
	{
		m_Patches.FindAndRemove( pSound );
	}

	//-- the frame's two questions, answered together ------------------------
	void State( int nHandle, int32_t *pnEntIndex, int32_t *pbFilterActive )
	{
		CSoundPatch *pSound = Resolve( nHandle );

		if ( pnEntIndex )
			*pnEntIndex = pSound ? pSound->EntIndex() : -1;

		if ( pbFilterActive )
			*pbFilterActive = ( pSound && pSound->FilterIsActive() ) ? 1 : 0;
	}

	void Emit( int nHandle, float flVolume01, float flPitch, int nFlags,
	           float flStartTime, bool bEmitCaption )
	{
		CSoundPatch *pSound = Resolve( nHandle );
		if ( !pSound )
			return;

		pSound->Emit( flVolume01, flPitch, nFlags | pSound->BaseFlags(),
		              flStartTime, bEmitCaption );
	}

	void Stop( int nHandle )
	{
		CSoundPatch *pSound = Resolve( nHandle );
		if ( pSound )
			pSound->Stop();
	}

	// The managed side has already shut it down and forgotten it; this is only
	// the delete. A handle it does not know is silently nothing, which is what a
	// double free would otherwise be.
	void Free( int nHandle )
	{
		CSoundPatch *pSound = Resolve( nHandle );
		if ( !pSound )
			return;

		Forget( pSound );
		delete pSound;
	}

	//-- ss::ISoundController (what physics.cpp's friction sounds call) -------

	virtual void Play( CSoundPatch *pSound, float volume, float pitch,
	                   float flStartTime ) OVERRIDE
	{
		IManagedSoundController *pApi = Ask();
		if ( !pApi || !pSound ) return;

		pApi->ServerSoundControllerPlay( pSound->Handle(), volume, pitch, flStartTime );
	}

	virtual CSoundPatch *SoundCreate( IRecipientFilter &filter, int nEntIndex, int channel,
	                                  const char *pSoundName,
	                                  soundlevel_t soundlevel ) OVERRIDE
	{
		return Build( filter, nEntIndex, channel, pSoundName, soundlevel );
	}

	virtual void SoundDestroy( CSoundPatch *pSound ) OVERRIDE
	{
		if ( !pSound )
			return;

		// The managed side shuts it down and forgets it FIRST, because after
		// this returns the object is gone and a queued command naming it would
		// be a use-after-free the next frame.
		IManagedSoundController *pApi = Ask();
		if ( pApi )
			pApi->ServerSoundControllerDestroy( pSound->Handle() );

		Forget( pSound );
		delete pSound;
	}

	virtual void SoundChangePitch( CSoundPatch *pSound, float pitchTarget,
	                               float deltaTime ) OVERRIDE
	{
		IManagedSoundController *pApi = Ask();
		if ( !pApi || !pSound ) return;

		pApi->ServerSoundControllerChangePitch( pSound->Handle(), pitchTarget, deltaTime );
	}

	virtual void SoundChangeVolume( CSoundPatch *pSound, float volumeTarget,
	                                float deltaTime ) OVERRIDE
	{
		IManagedSoundController *pApi = Ask();
		if ( !pApi || !pSound ) return;

		pApi->ServerSoundControllerChangeVolume( pSound->Handle(), volumeTarget, deltaTime );
	}

private:
	CSoundPatch *Build( IRecipientFilter &filter, int nEntIndex, int nChannel,
	                    const char *pszSoundName, soundlevel_t soundlevel )
	{
		CSoundPatch *pSound = new CSoundPatch;

		// Stock's own FIXME, and stock's own comment on it: "This is done so we
		// don't have to futz with the public interface." The interface takes an
		// entity INDEX and the patch wants a handle.
		//
		// The index is an EDICT index, so it is a slot-table entry
		// (plan_entity.md 3.3) and the shell bound there is the answer.
		// ResolveByIndex, reached through SourceSharp_ShellFromIndex, has the
		// same contract LookupEntityByNetworkIndex had: whoever occupies that
		// entry RIGHT NOW, with no serial to check because the caller has none.
		IHandleEntity *pEnt = ( nEntIndex != -1 )
			? SourceSharp_ShellFromIndex( nEntIndex )
			: NULL;

		pSound->Init( &filter, pEnt, nChannel, pszSoundName, soundlevel );

		return Track( pSound );
	}

	// The managed table, or NULL.
	//
	// EVERY FORWARD ABOVE TESTS THIS, one line each, and there is deliberately
	// no do-nothing table behind it. A zeroed SourceSharpManagedApi is a struct
	// of NULL function pointers, so "return a table of stubs" would turn the
	// window before the CLR is up from a no-op into a null call -- which is the
	// shape that looks like a guard, reads like a guard, and crashes.
	//
	// The window is real rather than theoretical: this object is constructed on
	// first use, and DLLInit runs LoadManagedRuntime after the engine has
	// already handed the game DLL its interfaces.
	// plan_engineapi_vtables E6: the managed-built sound controller area,
	// acquired again on every load; NULL outside one.
	static IManagedSoundController *Ask()
	{
		return ManagedSoundController();
	}

	CUtlVector< CSoundPatch * > m_Patches;
	int m_nNextHandle;
};

//-----------------------------------------------------------------------------
// The instance.
//
// FUNCTION-LOCAL STATIC, so nothing at file scope holds state -- the project
// rule, and the same shape shell_decals.cpp uses. It is constructed on first
// use, which is the first SoundCreate or the first managed callback, and both
// are after the CLR is up.
//-----------------------------------------------------------------------------
static CShellSoundController *ShellSoundController()
{
	static CShellSoundController s_Controller;
	return &s_Controller;
}

//-----------------------------------------------------------------------------
// The three engine slots: what the managed frame pass asks of a patch.
//-----------------------------------------------------------------------------
void SourceSharp_SoundPatchEmit( int32_t hPatch, float flVolume01, float flPitch,
                                 int32_t nFlags, float flStartTime, int32_t bEmitCaption )
{
	SOURCESHARP_TRACE_HOT();

	ShellSoundController()->Emit( hPatch, flVolume01, flPitch, nFlags, flStartTime,
	                              bEmitCaption != 0 );
}

void SourceSharp_SoundPatchStop( int32_t hPatch )
{
	SOURCESHARP_TRACE();

	ShellSoundController()->Stop( hPatch );
}

void SourceSharp_SoundPatchState( int32_t hPatch, int32_t *pnEntIndex,
                                  int32_t *pbFilterActive )
{
	SOURCESHARP_TRACE_HOT();

	ShellSoundController()->State( hPatch, pnEntIndex, pbFilterActive );
}

//-----------------------------------------------------------------------------
// A DESTROY command reached its execute time inside the managed frame pass, so
// the decision is managed and the delete is not.
//
// The managed side has already shut it down and forgotten it by the time this
// is called, which is the order that matters: freeing first would leave a
// handle in the queue naming an object that is gone.
//-----------------------------------------------------------------------------
void SourceSharp_SoundPatchFree( int32_t hPatch )
{
	SOURCESHARP_TRACE();

	ShellSoundController()->Free( hPatch );
}

//-----------------------------------------------------------------------------
// THE THREE A MANAGED LOOPING SOUND NEEDS (physics stock parity D2): the
// friction scrape is stock's PhysFrictionSound, and its policy -- which sound,
// how loud, when to start, update and stop -- is C#'s. What only the engine
// can answer is below: a sound script's parameters, a patch built on an
// entity with a recipient filter, and a surface's scrape sound names.
//-----------------------------------------------------------------------------

// A sound patch on the entity, heard from where it stands: stock's
// SoundCreate( CPASAttenuationFilter( entity, level ), entindex, channel,
// name, level ). The handle the managed controller tracks it by, or 0.
int32_t SourceSharp_SoundPatchCreate( uint32_t hEntity, int32_t nChannel,
                                      const char *pSoundName, int32_t nSoundLevel )
{
	SOURCESHARP_TRACE();

	if ( !pSoundName )
		return 0;

	const SourceSharpEntity_t ent = SourceSharp_ResolveEntity( hEntity );
	if ( !ent.IsValid() )
		return 0;

	const soundlevel_t level = (soundlevel_t)nSoundLevel;
	ss::CPASAttenuationFilter filter( ent.Origin(), level );
	CSoundPatch *pPatch = ShellSoundController()->SoundCreate( filter, ent.SoundSourceIndex(),
	                                                           nChannel, pSoundName, level );
	return pPatch ? pPatch->Handle() : 0;
}
