//========= SourceSharp — what the view pass used to ask a player entity ======//
//
// plan_entity.md step 7c. The view, the client mode and the input files are
// "de-entitied IN PLACE": they stay in the build and stop naming entity types.
// Every fact they used to reach through `C_BasePlayer::GetLocalPlayer()` is
// declared here as a free function, answered from the engine block or from the
// managed player, and NULL/absent is a value with a name rather than an early
// return nobody can see.
//
// WHY A FACADE AND NOT `shell_standalone_client.h`. That header declares
// C_StandaloneEntity, an engine-facing C++ class with IClientRenderable,
// IClientNetworkable and a blob accessor on it. A stock TU that included it to
// ask "where are the eyes" would acquire the whole client entity surface, and
// the point of this step is that these TUs stop having one. Everything below
// is plain scalars, three floats, or an engine struct the ENGINE owns.
//
// WHAT `GetLocalPlayer()` ANSWERS TODAY, and why every one of these existed.
// `C_BasePlayer::GetLocalPlayer()` is defined ONCE on this link line, in
// src/sourcesharp/game/client/shell_player_orphans.cpp:63, and its whole body
// is `return NULL;` — the local player is a managed entity, never a
// C_BasePlayer. So every `if ( pPlayer )` arm in view.cpp, viewrender.cpp,
// clientmode_shared.cpp, in_main.cpp and in_camera.cpp is dead code that has
// not run since the flip. De-entitying them is therefore not "removing a
// branch": it is deciding, one fact at a time, whether the answer is
//
//   (a) already published somewhere else  — the eye, the punch, the shake and
//       the viewmodel pin are the shim's OverrideView (shell_client.cpp), and
//       "does the player's own body draw" is SourceSharp_ThirdPersonSeesOwner;
//   (b) knowable from the block           — the effects bits, below;
//   (c) genuinely absent on this client   — the fog and the 3D skybox, which
//       are per-player networked state no managed player carries yet. Those
//       two say so through the not-shipped ledger rather than answering a
//       plausible zero.
//
//=============================================================================//
#ifndef SOURCESHARP_VIEW_FACTS_H
#define SOURCESHARP_VIEW_FACTS_H
#ifdef _WIN32
#pragma once
#endif

class Vector;
class QAngle;
struct fogparams_t;
struct sky3dparams_t;

//-----------------------------------------------------------------------------
// Is there a viewer at all?
//
// The successor to `C_BasePlayer::GetLocalPlayer() != NULL` for the sites that
// only wanted the NULL check. False between maps, before the player entity has
// arrived, and on a client with no player — exactly the moments stock's
// answered NULL in.
//-----------------------------------------------------------------------------
bool SourceSharp_HasLocalViewer();

//-----------------------------------------------------------------------------
// Where the viewer's own entity IS, as opposed to where the CAMERA is.
//
// `getpos 2` / `spec_pos 2`'s subject: stock asked the player entity for its
// abs origin and angles so the two commands could report the player rather
// than a third-person camera. Returns false and writes nothing when there is
// no viewer.
//
// Reads the managed player's published pose, so it is the same origin the
// server's copy of this player is at, not a render-time interpolation.
//-----------------------------------------------------------------------------
bool SourceSharp_ViewerAbsPosition( Vector *pOrigin, QAngle *pAngles );

//-----------------------------------------------------------------------------
// LOOKING OUT OF SOMETHING ELSE'S EYES: where the engine's VIEW ENTITY is, when
// that entity is not this client's own player.
//
// Writes *pOrigin / *pAngles and returns true only when the engine names a view
// entity that is not the viewer AND that entity resolves. False -- and nothing
// written -- for the ordinary case of a player looking out of their own head,
// which is what `render->GetViewEntity() == engine->GetLocalPlayer()` means.
//
// WHY IT IS A FUNCTION AND NOT FOUR LINES AT THE CALL SITE. The resolve has to
// go through IClientEntity, the face BOTH hierarchies implement: a slot's
// C_BaseEntity is what stock asked for, and an entity on the managed hierarchy
// has none, so a managed point_viewcontrol could be the engine's view entity
// and answer NULL. Keeping the whole question here is also what takes the
// entity-list ask out of the stock view TU.
//-----------------------------------------------------------------------------
bool SourceSharp_ViewEntityPose( Vector *pOrigin, QAngle *pAngles );

//-----------------------------------------------------------------------------
// Are any of these EF_* effects bits set on the viewer?
//
// FROM THE ENGINE BLOCK — `EngineBlockHead::Effects`, which is where an
// entity's effects bits live for both sides. Stock reached the same word
// through `C_BaseEntity::IsEffectActive`. The one caller today asks for
// EF_DIMLIGHT, which is how the renderer knows the flashlight is on.
//
// False when there is no viewer, which is what an absent player's effects
// word answered before.
//-----------------------------------------------------------------------------
bool SourceSharp_ViewerEffectActive( int nEffectBits );

//-----------------------------------------------------------------------------
// Is the SOUND LISTENER underwater? — what the underwater DSP is switched on by.
//
// Stock's `C_BasePlayer::AudioStateIsUnderwater`, whose live arm is
// `GetWaterLevel() >= WL_Eyes`. FROM THE BLOCK: `EngineBlockHead::WaterLevel`
// is the same 0..3 GetWaterLevel reports, written by managed code, so this is
// the one copy read rather than a second opinion.
//
// The observer arm — a point-contents test at the view origin, for a spectator
// with no body of their own — is not here because no observer mode reaches this
// client; view.cpp says the same about the freeze cam. Argument taken instead
// of the parameter being dropped, so the seam is ready for it.
//
// False when there is no viewer.
//-----------------------------------------------------------------------------
bool SourceSharp_ViewerAudioIsUnderwater( const Vector &vecMainViewOrigin );

//-----------------------------------------------------------------------------
// The viewer's world fog, or NULL.
//
// SHIPPED SINCE STEP 8e-render. It was an unconditional NULL and a not-shipped
// ledger row: stock reads `CBasePlayer::GetFogParams()`, which resolves the fog
// controller the SERVER assigned this player out of CPlayerLocalData, and no
// managed player carried that state.
//
// WHY THE GAP WAS THE WIRE AND NOT THIS FILE. In stock, fog reaches a client
// through DT_FogController — CFogController's own send table — plus a handle in
// the player's local data. `fogcontroller.cpp` is entirely `#ifndef SOURCESHARP`
// here and env_fog_controller is EnvFogController.cs, so server.so declares no
// DT_FogController and there was NO SENDER at all: the numbers existed on the
// server and world fog rendered off on every frame since the flip. Step 7c
// re-measured that and correctly made it a row rather than a working feature.
//
// What answers now: the master controller's resolved values ride in
// PlayerNetState, the managed client pushes the LOCAL player's copy through
// EngineApi.SetViewerFogParams, and this hands back a pointer to that copy.
//
// STILL NULL WHEN THERE IS NOTHING TO SAY, and that is not the same as fog off.
// A map with no env_fog_controller, or a client that has not yet received its
// player, gets the NULL stock itself produces for a player with no fog
// controller — every GetFog* helper in viewrender.cpp already handles it —
// rather than a zeroed fogparams_t, which would be a REAL answer meaning "no
// fog" and would make "this map has no fog" and "nobody asked" look identical
// again. The not-shipped ledger row goes with the gap; the NULL arm keeps its
// own count instead (`cs_cl_fog_report`).
//-----------------------------------------------------------------------------
fogparams_t *SourceSharp_ViewerFogParams();

//-----------------------------------------------------------------------------
// What fog the viewer is standing in, pushed by managed code (step 8e-render).
//
// pParams NULL clears it, which is what a level change and a viewer with no fog
// controller both do. Called from EngineApi.SetViewerFogParams.
//-----------------------------------------------------------------------------
struct SourceSharpFogParams;
void SourceSharp_SetViewerFogParams( const SourceSharpFogParams *pParams );

//-----------------------------------------------------------------------------
// THE FOG AND 3D SKYBOX RENDER COUNTERS WENT IN PHASE 1c, and the argument for
// them is worth keeping because it is the reason they were right to exist.
//
// `CSkyboxView::Setup` asks two separate questions and a zero on the screen
// cannot tell them apart -- "is a 3D skybox visible from here" (the engine's
// answer, off the leaf the eye is in: a map with no sky face over the player
// says SKYBOX_NOT_VISIBLE and nothing downstream is wrong) and "did any
// parameters arrive" (where the whole 3D skybox defect lived). The fog's four
// were the same shape: a renderer that draws nothing does not crash, so "the
// fog reaches the client" is observable as a push count that moves and an ask
// count that stops being answered NULL, and as nothing else.
//
// THEY ARE GONE BECAUSE THE READOUT IS. The counters were reachable only
// through SourceSharp_ViewerFogCounts and SourceSharp_ViewerSkyboxCounts, and
// those two had exactly one caller each: cs_cl_fog_report and
// cs_cl_skybox_report in shell_view_facts.cpp. With the console commands gone
// no code in either DLL, no managed assembly and no tool could read a single
// one of the ten numbers -- so what was left was ten counters incrementing
// into nothing, which is the shape this phase exists to remove. The marks
// themselves (SourceSharp_SkyboxSetupMark, SourceSharp_SkyboxWorldDrawn) and
// their viewrender.cpp call sites went with them, because a mark whose only
// effect is to bump a counter nothing reads is the same thing said twice.
//
// WHAT STAYS IS THE BEHAVIOUR THEY MEASURED, and it is pinned by its own
// tests: SourceSharp_ViewerFogParams answers NULL rather than a zeroed struct
// (fog_wire_test), and PreRender3dSkyboxWorld's NULL path is unchanged.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// The viewer's 3D skybox parameters, or NULL.
//
// THE LEDGER ROW IS RETIRED (defect-skybox). It was the fog's twin and the
// fault behind it took two lanes, because it was two faults:
//
//   * REACHABILITY (phase4-exit). UpdateAllClientData walked UTIL_PlayerByIndex,
//     which cannot answer on this build, so ClientData_Update never ran and
//     SourceSharp_FillSkyboxParams was never reached. Fixed; the server has
//     been filling MvLocal().m_skybox3d ever since.
//   * THE WIRE (this lane). CStandalonePlayer networks PlayerNetState, not
//     CPlayerLocalData, so what the server filled went nowhere. Measured live:
//     12,270 consecutive frames with a 3D skybox visible from the eye and this
//     function answering NULL every one of them.
//
// What answers now: the sky camera's block rides in PlayerNetState, the managed
// client pushes the LOCAL player's copy through EngineApi.SetViewerSkyboxParams
// and this hands back a pointer to it.
//
// STILL NULL WHEN THERE IS NOTHING TO SAY, and NULL is not area 255 and neither
// is a zeroed struct -- see shell_view_facts.cpp, which sets the three out side
// by side. NULL makes CSkyboxView::PreRender3dSkyboxWorld answer "no 3D sky",
// which is what stock produces for a player with no sky camera.
//-----------------------------------------------------------------------------
sky3dparams_t *SourceSharp_ViewerSkyboxParams();

//-----------------------------------------------------------------------------
// What 3D skybox the viewer is shown, pushed by managed code (defect-skybox).
//
// pParams NULL clears it, which is what a level change and a viewer with no sky
// camera both do. Called from EngineApi.SetViewerSkyboxParams. The SERVER's
// SourceSharp_SetSkyboxParams is the other end of the same wire and takes the
// same struct -- one map's sky camera making one round trip.
//-----------------------------------------------------------------------------
struct SourceSharpSkyboxParams;
void SourceSharp_SetViewerSkyboxParams( const SourceSharpSkyboxParams *pParams );

//-----------------------------------------------------------------------------
// The camera mode changed: first to third person, or back.
//
// Stock's `C_BasePlayer::ThirdPersonSwitch`, whose base body is
// `UpdateVisibility()` plus the same for anything bone-attached. This build has
// no bone attachments (GetNumBoneAttachments is C_BaseAnimating's and nothing
// on the managed hierarchy has one), so the visibility refresh is the whole of
// it — and it is the frame the player's own model has to come out of, or back
// into, the leaf system.
//-----------------------------------------------------------------------------
void SourceSharp_ViewerCameraModeChanged();

//-----------------------------------------------------------------------------
// BONE ACCESS -- GONE. This block re-exported the bone-access gate
// (SourceSharp_BoneAccessPush / Pop, the scoped guard) from the bone leaf
// library. Nothing read the gate (stock's one reader was C_BaseAnimating::
// SetupBones' developer warning, which left with that file), so the gate, its
// three calls in the frame and the library went (2026-10-18).
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Whether the camera the last frame was set up with can actually SEE the
// viewer's own body — a third-person camera far enough back to have the player
// in front of it, rather than one a wall pulled onto the eye.
//
// The successor to `C_BasePlayer::ShouldDrawLocalPlayer()`, and NOT the same
// question as `::input->CAM_IsThirdPerson()`: the mode says what was typed, this
// says where the camera ended up. Declared here as well as in
// shell_standalone_client.h so a stock TU can ask it without the entity
// surface; the definition is shell_client.cpp's, and a TU that saw both
// declarations would not compile if they disagreed.
//-----------------------------------------------------------------------------
bool SourceSharp_ThirdPersonSeesOwner();

//-----------------------------------------------------------------------------
// THE TWO JOYSTICK ASKS (plan_entity.md step c7-view, in_joystick.cpp).
//
// in_joystick.cpp made two decisions about the local player entity, and they
// are the last two in the view/input cluster. Both were
// `C_BasePlayer::GetLocalPlayer()` asks and so both were dead the moment that
// static became `return NULL` (shell_player_orphans.cpp:63) -- but "dead" is
// not "gone": the entity spelling is what kept in_joystick.cpp in the census's
// entity-using bucket and it is what put g_pEntityList in in_joystick.o's
// undefined list.
//
// They are declared HERE rather than answered in place for the reason step 7c
// gives at the head of this file: a stock TU that included
// shell_standalone_client.h to ask a one-line question would acquire the whole
// client entity surface, which is the thing being removed.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Whether the viewer's aim is being assisted onto a target right now.
//
// Stock (in_joystick.cpp, AutoAimDampening, inside `#ifdef HL2_CLIENT_DLL`):
//
//     C_BaseHLPlayer *p = (C_BaseHLPlayer *)C_BasePlayer::GetLocalPlayer();
//     if ( p && p->m_HL2Local.m_bAutoAimTarget ) return joy_autoaimdampen...
//
// m_bAutoAimTarget is HL2 local player data (hl2_playerlocaldata.h:45), written
// by CHL2_Player::PlayerUse's autoaim pass (hl2_player.cpp:3070) and networked
// through DT_HL2Local. NEITHER SIDE OF THAT WIRE IS BUILT HERE -- neither
// game/server/hl2/hl2_player.cpp nor game/client/hl2/c_hl2_playerlocaldata.cpp
// is in the ninja object table for this project -- and HL2_CLIENT_DLL is not
// defined on this client either, so stock's own block is not even compiled.
//
// So this answers false, with a not-shipped ledger row, and the block that
// reads it is the SOURCESHARP arm of a text stock still owns. The point of
// asking at all is that in_joystick.cpp stops spelling C_BasePlayer and
// C_BaseHLPlayer: it is that spelling, and not the dead branch, that the census
// counts and that a later reader would take for a live entity ask.
//
// Removal owner: whoever networks the HL2 autoaim state to the managed player.
//-----------------------------------------------------------------------------
bool SourceSharp_ViewerAimIsAssisted();

#endif // SOURCESHARP_VIEW_FACTS_H
