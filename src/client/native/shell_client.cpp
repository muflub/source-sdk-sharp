//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — client shell ========================================//
// SS_REASON b  an interface resolved by name at load
//
// Client-side symbols client_base.vpc expects a mod to supply.
//
// Every base class used here (CInput, CPrediction, ClientModeShared) is already
// compiled into client_base, so these are instantiations and thin overrides —
// not reimplementations, and not borrowed mod code. See plan.md Part 8.
//
//=============================================================================//

#include "cbase.h"
#include "c_baseplayer.h"
#include "hud.h"
#include "vgui/ISurface.h"
#include "iclientmode.h"
#include "GameEventListener.h"
#include "cam_thirdperson.h"
#include "particlemgr.h"
#include "particles/particles.h"
#include "c_rumble.h"
#include "engine/IEngineSound.h"
#include "c_recipientfilter.h"
#include "gamesound.h"
#include "vgui/ILocalize.h"
#include "vgui/IPanel.h"
#include "shell_viewport_client.h"
#include "view_facts.h"
#include "ivmodemanager.h"
#include "vgui_int.h"
#include "ienginevgui.h"
#include "shell_diagnostics.h"
#include "ammodef.h"          // the client's ammo table, from shell_shared.cpp
#include "shell_standalone_client.h"
#include "ivieweffects.h"
#include "iinput.h"
// SourceSharpThirdPersonCamera and the managed-built area it is answered
// through (plan_client_vtables CE8): where the third-person camera sits is
// managed policy (plan_entity.md §3.10).
#include "sourcesharp.h"
#include "imanaged_areas_client.h"
#include <vgui_controls/Controls.h>   // vgui::surface(), ipanel(): the module's interface accessors
#include "shell_rawpanel_client.h"
#include "ifpspanel.h"
#include "inetgraphpanel.h"
#include "studio_stats.h"
#include "filesystem.h"
#include "sourcesharp_entrypoints.h"
#include "shell_view_targets.h"   // lane camproto: the second cameras' draw
#include "shell_particle_systems.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

//-----------------------------------------------------------------------------
// Prediction moved to shell_prediction.cpp (Phase 9c1): the singleton the
// engine asks for by name is a CPrediction SUBCLASS now, so the ring wiring
// hears the command numbers. The EXPOSE lives with it.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Client mode. ClientModeShared supplies the whole HUD/viewport lifecycle; the
// mod only has to own an instance and publish it.
//-----------------------------------------------------------------------------
//
// The viewport hosts every HUD element. ClientModeShared::Init dereferences
// m_pViewport without a null check — "Derived ClientMode class must make sure
// m_Viewport is instantiated" (clientmode_shared.cpp:352) — so a bare subclass
// segfaults during CHLClient::Init. CBaseViewport is shell_baseviewport_client.cpp's:
// it builds no stock panel and asks the managed UI for each one shown by name.

//-----------------------------------------------------------------------------
// The third-person camera, and the one fact the rest of the client reads off it.
//
// thirdperson_camera.h carries the arithmetic and the argument for why stock's
// cannot be called here; this is the engine half — the convars, the hull sweep
// and the write into CViewSetup.
//
// THE LAST FRACTION IS PUBLISHED because two other decisions depend on it and
// neither can recompute it: whether the player's own model draws
// (the local-player arm of C_StandaloneEntity::ShouldDraw) and whether the
// viewmodel does
// (C_StandaloneEntity::ShouldDraw). A camera that WANTED to be 150 units back
// and was pulled onto the eye by a wall is a first-person camera however
// `thirdperson` was typed, and a model drawn for it fills the screen with the
// inside of a head. Asking CAM_IsThirdPerson alone answers the mode and not
// the camera, which is the difference that produces that frame.
//-----------------------------------------------------------------------------
static bool s_bThirdPersonSeesOwner = false;

bool SourceSharp_ThirdPersonSeesOwner()
{
	return s_bThirdPersonSeesOwner;
}

// A second camera that shows the viewer (shell_view_targets.cpp, lane
// camproto): for its draw the flag answers as a third-person camera's would,
// and the body is moved into the leaf system and back out, as the camera
// above does when the mode changes.
bool ShellClient_ViewerSeenBegin()
{
	const bool bWas = s_bThirdPersonSeesOwner;
	s_bThirdPersonSeesOwner = true;
	if ( C_StandaloneEntity *pPlayer = SourceSharp_LocalPlayerShell() )
		pPlayer->UpdateVisibility();
	return bWas;
}

void ShellClient_ViewerSeenEnd( bool bWasSeen )
{
	s_bThirdPersonSeesOwner = bWasSeen;
	if ( C_StandaloneEntity *pPlayer = SourceSharp_LocalPlayerShell() )
		pPlayer->UpdateVisibility();
}

static void SourceSharp_ApplyThirdPersonCamera( C_StandaloneEntity *pPlayer,
                                                CViewSetup *pSetup )
{
	s_bThirdPersonSeesOwner = false;

	if ( !::input || !::input->CAM_IsThirdPerson() )
	{
		// Still tell the player: leaving third person is exactly the frame its
		// model has to come back OUT of the leaf system, and an early return
		// that skipped this would leave the body drawn from inside the head
		// for every frame after the first `firstperson`.
		if ( pPlayer )
			pPlayer->UpdateVisibility();

		return;
	}

	// Stock's names, so `thirdperson` and the cam_* commands mean here what
	// they mean everywhere else. ConVarRef rather than our own convars: these
	// are declared static inside in_camera.cpp and cannot be linked against,
	// and declaring a second set with the same names would be two variables
	// answering to one word.
	static ConVarRef s_IdealDist( "cam_idealdist" );
	static ConVarRef s_IdealDistRight( "cam_idealdistright" );
	static ConVarRef s_IdealDistUp( "cam_idealdistup" );
	static ConVarRef s_IdealPitch( "cam_idealpitch" );
	static ConVarRef s_IdealYaw( "cam_idealyaw" );
	static ConVarRef s_Collision( "cam_collision" );

	if ( !s_IdealDist.IsValid() )
		return;

	IManagedPredictionClient *pPrediction = ManagedPredictionClient();

	if ( !pPrediction )
	{
		// The runtime is not up. Refusing is right and a native fallback would be
		// wrong: a second copy of the placement rule is exactly what the move to
		// managed removed, and a camera that silently used it would be a
		// different camera nobody could tell apart from this one.
		if ( pPlayer )
			pPlayer->UpdateVisibility();

		return;
	}

	const Vector vecEye = pSetup->origin;

	// PHASE ONE: where does the camera want to be. Every number in the answer --
	// the orbit, its pitch clamp, the sign convention that back is negative
	// forward -- is managed's since the policy-player lane; this end supplies
	// the convars and the eye and does no arithmetic of its own.
	SourceSharpThirdPersonCamera io;
	V_memset( &io, 0, sizeof( io ) );
	io.struct_size       = sizeof( io );
	io.nPhase            = SOURCESHARP_THIRDPERSON_PHASE_PLACE;
	io.vecViewAngles[0]  = pSetup->angles[ PITCH ];
	io.vecViewAngles[1]  = pSetup->angles[ YAW ];
	io.vecViewAngles[2]  = pSetup->angles[ ROLL ];
	io.flIdealPitch      = s_IdealPitch.GetFloat();
	io.flIdealYaw        = s_IdealYaw.GetFloat();
	io.flIdealDist       = s_IdealDist.GetFloat();
	io.flIdealDistRight  = s_IdealDistRight.IsValid() ? s_IdealDistRight.GetFloat() : 0.0f;
	io.flIdealDistUp     = s_IdealDistUp.IsValid() ? s_IdealDistUp.GetFloat() : 0.0f;

	if ( !pPrediction->ThirdPersonCamera( &io ) )
		return;

	const QAngle angCamera( io.vecCameraAngles[0], io.vecCameraAngles[1],
	                        io.vecCameraAngles[2] );
	const Vector vecWish( io.vecWish[0], io.vecWish[1], io.vecWish[2] );
	const float flWishLength = io.flWishLength;

	// What the camera can actually have of it. A hull rather than a line,
	// because a camera is not a point: a line sweep clears a doorframe the
	// near plane then cuts through. CAM_HULL_OFFSET is stock's size for it.
	float flTraceFraction = 1.0f;

	if ( !s_Collision.IsValid() || s_Collision.GetBool() )
	{
		const float flHull = 14.0f;   // stock's CAM_HULL_OFFSET
		trace_t trace;

		UTIL_TraceHull( vecEye, vecEye + vecWish,
		                Vector( -flHull, -flHull, -flHull ),
		                Vector(  flHull,  flHull,  flHull ),
		                MASK_SOLID & ~CONTENTS_MONSTER, pPlayer, COLLISION_GROUP_NONE, &trace );

		flTraceFraction = trace.fraction;
	}

	// PHASE TWO: how much of that wish survives what the sweep found, and
	// whether a camera placed with it can see the player at all. The 30-unit
	// floor under which the answer collapses to zero is managed's too -- a
	// camera three units behind the eye draws the inside of a skull, and a
	// screenshot gate reading "the model did not draw" would be reading a camera
	// problem.
	io.nPhase          = SOURCESHARP_THIRDPERSON_PHASE_RESOLVE;
	io.flTraceFraction = flTraceFraction;

	if ( !pPrediction->ThirdPersonCamera( &io ) )
		return;

	const float flSurviving = io.flSurvivingFraction;

	pSetup->origin = vecEye + vecWish * flSurviving;
	pSetup->angles = angCamera;

	s_bThirdPersonSeesOwner = ( io.bSeesOwner != 0 );

	// The leaf system is where a renderable IS or ISN'T, and it never re-asks
	// ShouldDraw for a world entity — CViewRender consults it only for the
	// viewmodel list. Stock's C_BaseEntity::UpdateVisibility is what moves an
	// entity in and out on the answer, and this is that call for the one
	// entity whose answer changes on a camera command rather than on a packet.
	if ( pPlayer )
		pPlayer->UpdateVisibility();
}

//-----------------------------------------------------------------------------
// THE CLIENT MODE, OURS (plan_client_novalve C3 step 4). Stock's
// ClientModeShared (clientmode_shared.cpp) was a base every mod derived from,
// driving a vgui_controls viewport. Its decisions are managed now (the keys:
// IManagedUiClient::KeyInput; the chat notices: ChatNotices; cl_drawhud and the
// screenshot flag: managed convars) and its HUD layout file had no element left
// to place, so this implements IClientMode directly over the raw viewport
// (shell_viewport_client.h). What stays native is what only native code can do:
// the view setup, the sound DSP reset, the particle manager's post-render, the
// rumble update, the team-255 broadcast sound.
//-----------------------------------------------------------------------------

static ConVar cl_show_num_particle_systems( "cl_show_num_particle_systems", "0", FCVAR_CLIENTDLL,
	"Display the number of active particle systems." );

CON_COMMAND( cl_reload_localization_files, "Reloads all localization files" )
{
	g_pVGuiLocalize->ReloadLocalizationFiles();
}

// The view managed asked OverrideView to draw this frame (C7 item 5).
struct ViewOverride_t
{
	bool   bActive = false;
	bool   bSetViewAngles = false;
	Vector origin;
	QAngle angles;
};
static ViewOverride_t s_ViewOverride;

// The screen shake, managed's (ViewEffects): this frame's offset and roll,
// added as stock's CViewEffects::ApplyShake added them at factor 1.
static void ApplyManagedShake( Vector &origin, QAngle &angles )
{
	float out[4] = { 0, 0, 0, 0 };
	if ( IManagedRenderClient *p = ManagedRenderClient() )
		p->ViewEffectsCalcShake( gpGlobals->curtime, gpGlobals->frametime, out );
	VectorMA( origin, 1.0f, Vector( out[0], out[1], out[2] ), origin );
	angles.z += out[3] * 1.0f;
}

// Stock's CalcObserverView, decided by SourceSharp.Client's ObserverCamera at
// FRAME_RENDER_START and handed over through ViewOverrideSet, carried out:
// the view, the engine's view angles when in-eye asks, and the shake that
// stock applies after CalcView. False when there is no override this frame.
static bool ApplyViewOverride( CViewSetup *pSetup )
{
	if ( !s_ViewOverride.bActive )
		return false;
	pSetup->origin = s_ViewOverride.origin;
	pSetup->angles = s_ViewOverride.angles;
	if ( s_ViewOverride.bSetViewAngles )
		engine->SetViewAngles( pSetup->angles );
	ApplyManagedShake( pSetup->origin, pSetup->angles );
	return true;
}

void SourceSharp_ViewOverrideSet( int32_t nActive, const float *pOriginXYZ, const float *pAngles, int32_t nSetViewAngles )
{
	s_ViewOverride.bActive = nActive != 0 && pOriginXYZ && pAngles;
	if ( !s_ViewOverride.bActive )
		return;
	s_ViewOverride.origin.Init( pOriginXYZ[0], pOriginXYZ[1], pOriginXYZ[2] );
	s_ViewOverride.angles.Init( pAngles[0], pAngles[1], pAngles[2] );
	s_ViewOverride.bSetViewAngles = nSetViewAngles != 0;
}

class CSourceSharpClientMode : public IClientMode, public CGameEventListener
{
public:
	CSourceSharpClientMode() : m_pViewport( NULL ) { SOURCESHARP_TRACE(); }

	virtual ~CSourceSharpClientMode()
	{
		SOURCESHARP_TRACE();
		delete m_pViewport;
		m_pViewport = NULL;
	}

	virtual void InitViewport() OVERRIDE
	{
		SOURCESHARP_TRACE();
		m_pViewport = new CSourceSharpViewport();
		m_pViewport->Start( gameeventmanager );
	}

	virtual void Init() OVERRIDE
	{
		ListenForGameEvent( "teamplay_broadcast_audio" );
	}

	virtual void VGui_Shutdown() OVERRIDE
	{
		delete m_pViewport;
		m_pViewport = NULL;
	}

	virtual void Shutdown() OVERRIDE {}

	// Under the client root, shown, laid out.
	virtual void Enable() OVERRIDE
	{
		if ( !m_pViewport )
			return;
		if ( vgui::VPANEL root = VGui_GetClientDLLRootPanel() )
			m_pViewport->SetParent( root );
		m_pViewport->SetCursor( vgui::dc_none );
		vgui::surface()->SetCursor( vgui::dc_none );
		m_pViewport->SetVisible( true );
		Layout();
	}

	virtual void Disable() OVERRIDE
	{
		if ( !m_pViewport )
			return;
		m_pViewport->SetParent( (vgui::VPANEL)0 );
		m_pViewport->SetVisible( false );
	}

	// The viewport fills the client root.
	virtual void Layout() OVERRIDE
	{
		vgui::VPANEL root = VGui_GetClientDLLRootPanel();
		if ( !m_pViewport || !root )
			return;
		int nWide, nTall;
		vgui::ipanel()->GetSize( root, nWide, nTall );
		m_pViewport->SetBounds( 0, 0, nWide, nTall );
	}

	// No vgui_controls panel: the HUD and menu roots ask for the viewport's VPANEL.
	virtual vgui::Panel *GetViewport() OVERRIDE { return NULL; }
	virtual vgui::AnimationController *GetViewportAnimationController() OVERRIDE { return NULL; }
	vgui::VPANEL GetViewportVPanel() { return m_pViewport ? m_pViewport->GetVPanel() : 0; }

	virtual void ProcessInput( bool bActive ) OVERRIDE { gHUD.ProcessInput( bActive ); }
	virtual bool ShouldDrawDetailObjects() OVERRIDE { return true; }
	virtual bool ShouldDrawEntity( IClientUnknown *pEnt ) OVERRIDE { return true; }
	virtual bool ShouldDrawParticles() OVERRIDE { return true; }
	virtual bool ShouldDrawFog( void ) OVERRIDE { return true; }
	virtual bool ShouldDrawViewModel( void ) OVERRIDE { return true; }
	virtual bool ShouldDrawCrosshair( void ) OVERRIDE { return true; }

	// The camera pass answered it (thirdperson_camera.h): the body draws when the
	// camera got far enough behind the eye to see it.
	virtual bool ShouldDrawLocalPlayer() OVERRIDE { return SourceSharp_ThirdPersonSeesOwner(); }

	virtual void StartMessageMode( int iMessageModeType ) OVERRIDE {}
	virtual vgui::Panel *GetMessagePanel() OVERRIDE { return NULL; }
	virtual void OverrideMouseInput( float *x, float *y ) OVERRIDE {}
	virtual bool CreateMove( float flInputSampleTime, CUserCmd *cmd ) OVERRIDE { return true; }

	// game_newmap, faked client side as stock did (clients connect after the
	// server fired it): the managed UI and the viewport hear it. The player's
	// sound DSP back to none.
	virtual void LevelInit( const char *newmap ) OVERRIDE
	{
		if ( IGameEvent *event = gameeventmanager->CreateEvent( "game_newmap" ) )
		{
			event->SetString( "mapname", newmap );
			gameeventmanager->FireEventClientSide( event );
		}
		CLocalPlayerFilter filter;
		enginesound->SetPlayerDSP( filter, 0, true );
	}

	virtual void LevelShutdown( void ) OVERRIDE
	{
		g_ThirdPersonManager.Init();
		CLocalPlayerFilter filter;
		enginesound->SetPlayerDSP( filter, 0, true );
	}

	virtual void AdjustEngineViewport( int &x, int &y, int &width, int &height ) OVERRIDE {}
	// The second cameras' draw (lane camproto): before the main view, inside
	// View_Render, which is where stock's DrawMonitors draws point_cameras.
	virtual void PreRender( CViewSetup *pSetup ) OVERRIDE { ShellViewTargets_RenderQueued(); }
	virtual void PostRender( void ) OVERRIDE
	{
		ShellParticles_PostRender();	// the old-style effects, managed's
		ParticleMgr()->PostRender();
	}
	virtual void PostRenderVGui() OVERRIDE {}

	// No compiled code activates an in-game vgui context (stock's were for
	// vgui_screen entities, whose panels are not built here).
	virtual void ActivateInGameVGuiContext( vgui::Panel *pPanel ) OVERRIDE {}
	virtual void DeactivateInGameVGuiContext() OVERRIDE {}

	// viewmodel_fov, managed's declaration since view.cpp left (C7 step 3).
	virtual float GetViewModelFOV( void ) OVERRIDE
	{
		ConVarRef viewmodelFov( "viewmodel_fov", true );
		return viewmodelFov.IsValid() ? viewmodelFov.GetFloat() : 54.0f;
	}
	virtual bool CanRecordDemo( char *errorMsg, int length ) const OVERRIDE { return true; }
	virtual void ComputeVguiResConditions( KeyValues *pkvConditions ) OVERRIDE {}
	virtual wchar_t *GetServerName() OVERRIDE { return NULL; }
	virtual void SetServerName( wchar_t *name ) OVERRIDE {}
	virtual wchar_t *GetMapName() OVERRIDE { return NULL; }
	virtual void SetMapName( wchar_t *name ) OVERRIDE {}
	virtual bool DoPostScreenSpaceEffects( const CViewSetup *pSetup ) OVERRIDE { return true; }
	virtual void DisplayReplayMessage( const char *pLocalizeName, float flDuration, bool bUrgent,
	                                   const char *pSound, bool bDlg ) OVERRIDE {}

	// The rumble update, and stock's particle-system count when asked for.
	virtual void Update() OVERRIDE
	{
		UpdateRumbleEffects();

		if ( cl_show_num_particle_systems.GetBool() )
		{
			int nCount = 0;
			for ( int i = 0; i < g_pParticleSystemMgr->GetParticleSystemCount(); i++ )
			{
				CParticleSystemDefinition *pDef = g_pParticleSystemMgr->FindParticleSystem(
					g_pParticleSystemMgr->GetParticleSystemNameFromIndex( i ) );
				if ( !pDef )
					continue;
				for ( CParticleCollection *p = pDef->FirstCollection(); p; p = p->GetNextCollectionUsingSameDef() )
					++nCount;
			}
			engine->Con_NPrintf( 0, "# Active particle systems: %i", nCount );
		}
	}

	virtual bool ShouldBlackoutAroundHUD() OVERRIDE { return enginevgui->IsGameUIVisible(); }
	virtual HeadtrackMovementMode_t ShouldOverrideHeadtrackControl() OVERRIDE { return HMM_NOOVERRIDE; }
	virtual bool IsInfoPanelAllowed() OVERRIDE { return true; }
	virtual void InfoPanelDisplayed() OVERRIDE {}
	virtual bool IsHTMLInfoPanelAllowed() OVERRIDE { return true; }
	virtual void OnDemoRecordStart( char const *pDemoBaseName ) OVERRIDE {}
	virtual void OnDemoRecordStop() OVERRIDE {}

	// The one event left native: a broadcast sound to every team (255) plays
	// on the local player, as stock's arm for it did.
	virtual void FireGameEvent( IGameEvent *event ) OVERRIDE
	{
		if ( V_strcmp( event->GetName(), "teamplay_broadcast_audio" ) || event->GetInt( "team" ) != 255 )
			return;
		EmitSound_t et;
		et.m_pSoundName = event->GetString( "sound" );
		et.m_nFlags = event->GetInt( "additional_flags" );
		CLocalPlayerFilter filter;
		GameSound_Emit( filter, SOUND_FROM_LOCAL_PLAYER, et );
	}

	// A key the engine offers the client mode. The decision is managed's
	// (IManagedUiClient::KeyInput, stock ClientModeShared::KeyInput's chat and
	// spectator arms); this carries it out: 1 the engine runs the binding, 0
	// the key was used, -1 no decision -- and with no HUD element left to ask
	// (stock's last step), the engine runs it. With no managed side, the same.
	virtual int KeyInput( int down, ButtonCode_t keynum, const char *pszCurrentBinding ) OVERRIDE
	{
		IManagedUiClient *pUi = ManagedUiClient();
		if ( !pUi )
			return 1;
		const int nDecision = pUi->KeyInput( down, keynum, pszCurrentBinding,
			engine->Con_IsVisible() ? 1 : 0, gpGlobals->maxClients );
		return nDecision >= 0 ? nDecision : 1;
	}

	//-------------------------------------------------------------------------
	// The standalone local player's eyes (Phase 9a).
	//
	// The one seam that runs with or without a C_BasePlayer: SetUpViews calls
	// this unconditionally (view.cpp:715), AFTER its no-player branch zeroed
	// the origin and BEFORE the camera variables, the audio state and
	// g_vecRenderOrigin are derived from it — so filling it here is the whole
	// of "the view follows the player". Angles are the engine's: they are the
	// client's own authority every frame, exactly as CInput wrote them.
	//-------------------------------------------------------------------------
	virtual void OverrideView( CViewSetup *pSetup ) OVERRIDE
	{
		if ( C_StandaloneEntity *pPlayer = SourceSharp_LocalPlayerShell() )
		{
			// AN OBSERVER LOOKS FROM WHERE MANAGED SAYS (C7 item 5).
			if ( ApplyViewOverride( pSetup ) )
				return;

			pSetup->origin = pPlayer->EyePosition();
			engine->GetViewAngles( pSetup->angles );

			//-----------------------------------------------------------------
			// THE PUNCH AND THE SHAKE, which nothing here was applying.
			//
			// The server half of this was fixed first (util.cpp), and fixing it
			// alone would have changed NOTHING ON SCREEN — which is the trap
			// this pair is worth writing down for. Both of these are applied by
			// CBasePlayer::CalcPlayerView, in baseplayer_shared.cpp, and
			// client_csharp.vpc evicts that file. So the punch angle arrived
			// over the wire, decayed correctly in the predicted movement, and
			// was never added to a view; and the Shake user message was hooked,
			// parsed and accumulated into g_ViewEffects, where CalcShake was
			// never called to turn it into an offset.
			//
			// Two silences that look identical from the server: it sent the
			// message and got no error either time.
			//
			// The order is stock's. The punch is added to the angles before the
			// shake, and both land before the viewmodel is pinned below, so the
			// viewmodel inherits the shaken eye exactly as CalcViewModelView
			// gives it in stock — and then adds its own tenth on top from
			// CBaseViewModel::CalcViewModelView, which is compiled and has been
			// reading a permanently zero offset all along.
			//-----------------------------------------------------------------
			VectorAdd( pSetup->angles, pPlayer->PredictedPunchAngle(), pSetup->angles );

			ApplyManagedShake( pSetup->origin, pSetup->angles );

			// The viewmodel is these same eyes, by definition — so it is
			// pinned HERE, with the frame's actual view values, rather than
			// recomputed anywhere that could drift from them. Stock reaches
			// the same moment through CalcViewModelView; the standalone
			// player's seam is this one. (This is also where the corrected
			// eye pitch reaches the render: pSetup->angles is the client's
			// full view angle, where the player's BODY angles carry none.)
			SourceSharp_StandaloneViewModelToEye( pSetup->origin, pSetup->angles );

			// ...and THEN the camera may leave the eye. The order is the whole
			// point: the viewmodel is pinned to the EYE, which is where a pair
			// of arms belongs whatever the camera is doing, and the third
			// person offset below moves only the camera. Reversing the two
			// would drag the arms out to the camera and draw them in front of
			// the player's own model.
			SourceSharp_ApplyThirdPersonCamera( pPlayer, pSetup );
			return;
		}

		// No viewer: stock's orthographic camera, ::input's state and no entity's.
		if ( SourceSharp_HasLocalViewer() && !::input->CAM_IsThirdPerson() && ::input->CAM_IsOrthographic() )
		{
			pSetup->m_bOrtho = true;
			float w, h;
			::input->CAM_OrthographicSize( w, h );
			pSetup->m_OrthoLeft = -w * 0.5f;
			pSetup->m_OrthoTop = -h * 0.5f;
			pSetup->m_OrthoRight = w * 0.5f;
			pSetup->m_OrthoBottom = h * 0.5f;
		}
	}

private:
	CSourceSharpViewport *m_pViewport;
};

vgui::VPANEL SourceSharp_ClientViewportPanel();

static CSourceSharpClientMode g_ClientMode;
IClientMode *g_pClientMode = &g_ClientMode;

vgui::VPANEL SourceSharp_ClientViewportPanel()
{
	return g_ClientMode.GetViewportVPanel();
}

IClientMode *GetClientModeNormal()
{
	// Called constantly by the view/HUD code, so only trace when asked.
	SOURCESHARP_TRACE_HOT();

	return &g_ClientMode;
}

//-----------------------------------------------------------------------------
// Mode manager. Level load/unload hooks; nothing mod-specific is needed yet.
//-----------------------------------------------------------------------------
class CSourceSharpModeManager : public IVModeManager
{
public:
	virtual void Init() OVERRIDE          { SOURCESHARP_STUB(); }
	virtual void SwitchMode( bool commander, bool force ) OVERRIDE { SOURCESHARP_STUB(); }
	virtual void LevelInit( const char *newmap ) OVERRIDE
	{
		SOURCESHARP_TRACE_MSG( newmap );
		g_pClientMode->LevelInit( newmap );
	}
	virtual void LevelShutdown( void ) OVERRIDE
	{
		SOURCESHARP_TRACE();
		g_pClientMode->LevelShutdown();
	}
};

static CSourceSharpModeManager g_ModeManager;
IVModeManager *modemanager = &g_ModeManager;

//-----------------------------------------------------------------------------
// VGUI root panel. The engine parents all client VGUI under this.
//-----------------------------------------------------------------------------
static CSourceSharpRawPanel *g_pRootPanel = NULL;

//-----------------------------------------------------------------------------
// The root panel has to FILL THE SCREEN, and keep filling it.
//
// Creating a bare vgui::Panel leaves it at VGUI's default 64x24. Every client
// panel is parented under this one and VGUI clips a child to its parent, so the
// entire HUD and viewport were being cropped to a 64x24 box in the top-left —
// visible as a HUD that "does not show" except for the corner of anything drawn
// near the origin.
//
// CBaseViewport::Start even calls SetSize(10,10) to "quiet parent not sized yet
// spew", which is the same problem being papered over one level down.
//
// On every think rather than a one-off SetBounds at creation: the screen size is
// not final at creation time, and it changes on a resolution change.
//-----------------------------------------------------------------------------
void VGUI_CreateClientDLLRootPanel( void )
{
	SOURCESHARP_TRACE();
	// A raw vgui2 panel (shell_rawpanel_client.h): nothing it does needs
	// vgui_controls, and its children need nothing of it but a parent.
	g_pRootPanel = new CSourceSharpRawPanel( "SourceSharp Root Panel" );
	g_pRootPanel->SetParent( enginevgui->GetPanel( PANEL_CLIENTDLL ) );
	g_pRootPanel->SetPaintEnabled( false );
	g_pRootPanel->SetCursor( vgui::dc_none );
	g_pRootPanel->SetZPos( 0 );
	g_pRootPanel->SetVisible( true );
	g_pRootPanel->SetKeyBoardInputEnabled( false );
	g_pRootPanel->SetMouseInputEnabled( false );

	// Sized to the screen now and on every think after the screen changes, so
	// anything created before the first layout -- the viewport is -- sees a
	// sane parent.
	g_pRootPanel->FillScreen();
	int nScreenW, nScreenH;
	g_pRootPanel->GetSize( nScreenW, nScreenH );

	Msg( "[SourceSharp] VGUI root panel sized %dx%d\n", nScreenW, nScreenH );
}

void VGUI_DestroyClientDLLRootPanel( void )
{
	SOURCESHARP_TRACE();
	delete g_pRootPanel;
	g_pRootPanel = NULL;
}

vgui::VPANEL VGui_GetClientDLLRootPanel( void )
{
	SOURCESHARP_TRACE_HOT();

	return g_pRootPanel ? g_pRootPanel->GetVPanel() : 0;
}

//-----------------------------------------------------------------------------
// The FPS panel (and the blocking-I/O panel beside it) is deleted
// (plan_client_novalve C1, user ruling 2026-10-04): what it drew is the dev
// API's `frame` route. vgui_int.cpp (fork) still creates and destroys it through
// `fps`, which now names an object that does nothing. Absence row 8 in
// docs/absence-client.md; the pointer goes when vgui_int.cpp leaves (C3).
//-----------------------------------------------------------------------------
namespace
{
class CAbsentFPSPanel final : public IFPSPanel
{
public:
	void Create( vgui::VPANEL ) override {}
	void Destroy( void ) override {}
};
CAbsentFPSPanel s_AbsentFPSPanel;
}

IFPSPanel *fps = &s_AbsentFPSPanel;

//-----------------------------------------------------------------------------
// The net-graph panel is deleted the same way (C1, user ruling 2026-10-04):
// what it drew is the dev API's `netgraph` route, read through
// IShimNetClient. `netgraphpanel` answers vgui_int.cpp's Create/Destroy and
// does nothing (absence row 8).
//-----------------------------------------------------------------------------
namespace
{
class CAbsentNetGraphPanel final : public INetGraphPanel
{
public:
	void Create( vgui::VPANEL ) override {}
	void Destroy( void ) override {}
};
CAbsentNetGraphPanel s_AbsentNetGraphPanel;
}

INetGraphPanel *netgraphpanel = &s_AbsentNetGraphPanel;

//-----------------------------------------------------------------------------
// studio_stats.cpp is deleted (C1, docs/client-novalve-rulings.tsv: a modelling
// tool, r_studio_stats). viewrender.cpp (fork) still calls
// StudioStats_FindClosestEntity every main view and compares each opaque
// renderable with g_pStudioStatsEntity; with no entity ever chosen it draws
// nothing with STUDIO_GENERATE_STATS, which is what r_studio_stats 0 did.
// Absence row 10; both go when viewrender.cpp leaves (C7).
//-----------------------------------------------------------------------------
IClientRenderable *g_pStudioStatsEntity = NULL;

void StudioStats_FindClosestEntity( CClientRenderablesList * )
{
}

//=============================================================================//
// READING A GAME SCRIPT FOR C# (lane c2-sims, plan_client_novalve C2).
//
// The client's bodies of IShimEngine's FilesGameFileSize / FilesReadGameFile
// (docs/engineapi-areas.tsv, client_body), which were server-only slots: the
// client's decal table (decals.cpp, cluster 18) and its soundscape scripts are
// managed now, and both begin by reading a KeyValues script off the GAME path,
// which only the engine's filesystem can do. The same answers as the server's
// bodies (shell_engineservices.cpp). Here and not in a TU of their own: a new
// client TU is one more R5 incdir-forbidden-tus row, for two pass-throughs.
//=============================================================================//

//-----------------------------------------------------------------------------
// filesystem->Size on the GAME path id, or -1 when the file is not there (Size
// answers 0 for both an empty file and a missing one, so Open decides).
//-----------------------------------------------------------------------------
int32_t SourceSharp_ClientFilesGameFileSize( const char *pszPath )
{
	if ( pszPath == NULL || pszPath[ 0 ] == '\0' || filesystem == NULL )
		return -1;

	FileHandle_t hFile = filesystem->Open( pszPath, "rb", "GAME" );

	if ( hFile == FILESYSTEM_INVALID_HANDLE )
		return -1;

	unsigned int nSize = filesystem->Size( hFile );
	filesystem->Close( hFile );

	return (int32_t)nSize;
}

//-----------------------------------------------------------------------------
// filesystem->Read into the caller's buffer: the byte count, or -1. Not
// NUL-terminated (the caller is handed a length). A file larger than the
// buffer is refused, never truncated: half a KeyValues document parses into a
// table missing its tail.
//-----------------------------------------------------------------------------
int32_t SourceSharp_ClientFilesReadGameFile( const char *pszPath, char *pOut, int32_t nOutSize )
{
	if ( pszPath == NULL || pszPath[ 0 ] == '\0' || pOut == NULL || nOutSize <= 0 || filesystem == NULL )
		return -1;

	FileHandle_t hFile = filesystem->Open( pszPath, "rb", "GAME" );

	if ( hFile == FILESYSTEM_INVALID_HANDLE )
		return -1;

	unsigned int nSize = filesystem->Size( hFile );

	if ( (int32_t)nSize > nOutSize )
	{
		filesystem->Close( hFile );
		Warning( "[SourceSharp] game file '%s' is %u byte(s) and the buffer offered is %d; "
		         "refused rather than truncated\n", pszPath, nSize, nOutSize );
		return -1;
	}

	int nRead = filesystem->Read( pOut, (int)nSize, hFile );
	filesystem->Close( hFile );

	return (int32_t)nRead;
}

//=============================================================================//
// FROM shell_shared.cpp, WHERE THEY WERE #ifdef CLIENT_DLL (plan_server_native_v2
// T2, 2026-10-18): a shared TU keeps only what both DLLs import. Moved verbatim.
//=============================================================================//

//-----------------------------------------------------------------------------
// Ammo types, declared in C#.
//
// The table is deliberately NOT lazily initialised behind a "have we done this
// yet" flag. GetAmmoDef can be called before the managed runtime is up — it is
// reached from weapon script parsing, which the engine can do early — and a flag
// would then latch an empty table forever. Instead the table starts empty and
// valid, and C# fills it during LoadSide.
//
// CLIENT ONLY (plan_server_novalve.md P5, user ruling 2026-10-01). server.so's
// table, ss::CAmmoDef, was written by C# and read by nothing once the server gc
// cull deleted CalculateBulletDamageForce, so it is gone: the server's managed
// side no longer registers, and the server's slot (shell_shared.cpp) refuses.
//-----------------------------------------------------------------------------
static CAmmoDef g_AmmoDef;

CAmmoDef *GetAmmoDef()
{
	return &g_AmmoDef;
}

//-----------------------------------------------------------------------------
// Called by C# once per ammo type.
//
// Idempotent by name: C# re-registers on every side load and a hot reload runs
// it again, but CAmmoDef is native and survives that, so without this a reload
// would add duplicate types and shift every ammo index.
//-----------------------------------------------------------------------------
void SourceSharp_RegisterAmmoType( const char *pName, int32_t nDamageType, int32_t nMaxCarry,
                                   int32_t nTracerType, float flDamageForce, float flDamage )
{
	if ( !pName || !pName[0] )
		return;

	if ( g_AmmoDef.Index( pName ) != -1 )
	{
		if ( SourceSharp_GetVerbosity() > 1 )
			Msg( "[SourceSharp] ammo type '%s' already registered; kept\n", pName );
		return;
	}

	// The damage is per BULLET and it is the ammo's, not the weapon's, which is
	// how stock does it: FireBullets asks the ammo definition unless the shot
	// carries an override, and HL2 sets these through the sk_plr_dmg_* convars.
	// Registering zero — as this did — makes every bullet in the game do nothing,
	// and nothing complains.
	g_AmmoDef.AddAmmoType( pName, nDamageType, nTracerType,
	                       (int)flDamage, (int)flDamage,   // player and NPC damage
	                       nMaxCarry,
	                       (int)flDamageForce, 0 );

	if ( SourceSharp_GetVerbosity() > 0 )
		Msg( "[SourceSharp] ammo type '%s' max=%d damageType=%d\n",
			pName, nMaxCarry, nDamageType );
}
