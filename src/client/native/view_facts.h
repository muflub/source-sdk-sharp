// What the view, client mode and input code ask about the local viewer, as
// free functions answered from the engine block or the managed player. Plain
// scalars and engine structs only, so a stock TU can ask without including the
// client entity surface (shell_standalone_client.h).
#pragma once

class Vector;
class QAngle;
struct fogparams_t;
struct sky3dparams_t;

// Whether there is a viewer at all. False between maps, before the player
// entity arrives, and on a client with no player.
bool SourceSharp_HasLocalViewer();

// Where the viewer's own entity is, as opposed to the camera (`getpos 2`,
// `spec_pos 2`): the managed player's published pose. False and nothing written
// with no viewer.
bool SourceSharp_ViewerAbsPosition( Vector *pOrigin, QAngle *pAngles );

// Where the engine's view entity is, when it is not the viewer's own player
// (point_viewcontrol, a camera). True only when such an entity resolves;
// resolved through IClientEntity so a managed entity answers.
bool SourceSharp_ViewEntityPose( Vector *pOrigin, QAngle *pAngles );

// Whether any of these EF_* bits are set on the viewer, read from
// EngineBlockHead::Effects. False with no viewer.
bool SourceSharp_ViewerEffectActive( int nEffectBits );

// Whether the sound listener is underwater (EngineBlockHead::WaterLevel >=
// WL_Eyes). No observer mode reaches this client, so the view origin argument
// is unused for now. False with no viewer.
bool SourceSharp_ViewerAudioIsUnderwater( const Vector &vecMainViewOrigin );

// The viewer's world fog, as managed last pushed it, or NULL. NULL, not a
// zeroed struct, when there is nothing to say: viewrender handles NULL as stock
// does for a player with no fog controller, while a zeroed struct means "no fog".
fogparams_t *SourceSharp_ViewerFogParams();

// The fog the viewer stands in, pushed by managed code; NULL clears it.
struct SourceSharpFogParams;
void SourceSharp_SetViewerFogParams( const SourceSharpFogParams *pParams );

// The viewer's 3D skybox parameters, as managed last pushed them, or NULL.
// NULL makes CSkyboxView::PreRender3dSkyboxWorld answer "no 3D sky", as stock
// does for a player with no sky camera.
sky3dparams_t *SourceSharp_ViewerSkyboxParams();

// The 3D skybox the viewer is shown, pushed by managed code; NULL clears it.
struct SourceSharpSkyboxParams;
void SourceSharp_SetViewerSkyboxParams( const SourceSharpSkyboxParams *pParams );

// The camera mode changed between first and third person: refreshes the
// viewer's visibility so its own model enters or leaves the leaf system.
void SourceSharp_ViewerCameraModeChanged();

// Whether the last frame's camera can see the viewer's own body: where the
// camera ended up, not the third-person mode that was typed. Defined in
// shell_client.cpp; also declared in shell_standalone_client.h.
bool SourceSharp_ThirdPersonSeesOwner();

// Whether the viewer's aim is being assisted onto a target. Always false: the
// HL2 autoaim state is not networked to the managed player.
bool SourceSharp_ViewerAimIsAssisted();
