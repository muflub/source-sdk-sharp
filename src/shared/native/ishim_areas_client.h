//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// THE CLIENT'S CONTRACT (hand-kept; tools/shim-areas reads it). Append-only: an edit is an ABI change.
//
// plan_client_vtables CC: every client-only EngineApi slot, as a slot of one of
// these interfaces, served by the client's shim root by the version string
// beside it. Kept apart from ishim_areas.h, whose methods are the server's
// (EngineApiCensusTests reads that file as the server's slots). Rules as
// ishim_areas.h: no virtual destructor, blittable arguments, APPEND-ONLY.

#ifndef ISHIM_AREAS_CLIENT_H
#define ISHIM_AREAS_CLIENT_H
#pragma once

#include <stdint.h>
#include "tier0/platform.h"
#include "sourcesharp.h"

#define SOURCESHARP_SHIM_HUDCLIENT_VERSION "SourceSharpHudClient001"
#define SOURCESHARP_SHIM_PREDICTIONCLIENT_VERSION "SourceSharpPredictionClient001"
#define SOURCESHARP_SHIM_TEMPENTPOOLCLIENT_VERSION "SourceSharpTempEntPoolClient001"
#define SOURCESHARP_SHIM_TEMPENTBACKENDSCLIENT_VERSION "SourceSharpTempEntBackendsClient001"
#define SOURCESHARP_SHIM_EFFECTSRENDERCLIENT_VERSION "SourceSharpEffectsRenderClient001"
#define SOURCESHARP_SHIM_STANDALONECLIENT_VERSION "SourceSharpStandaloneClient001"
#define SOURCESHARP_SHIM_VIEWCLIENT_VERSION "SourceSharpViewClient001"
#define SOURCESHARP_SHIM_USERMESSAGESCLIENT_VERSION "SourceSharpUserMessagesClient001"
#define SOURCESHARP_SHIM_NETCLIENT_VERSION "SourceSharpNetClient001"
#define SOURCESHARP_SHIM_DIAGNOSTICSCLIENT_VERSION "SourceSharpDiagnosticsClient001"
#define SOURCESHARP_SHIM_UICLIENT_VERSION "SourceSharpUiClient001"
#define SOURCESHARP_SHIM_PARTICLESCLIENT_VERSION "SourceSharpParticlesClient001"
#define SOURCESHARP_SHIM_FXCLIENT_VERSION "SourceSharpFxClient001"
#define SOURCESHARP_SHIM_SOUNDSCAPECLIENT_VERSION "SourceSharpSoundscapeClient001"
#define SOURCESHARP_SHIM_PRECIPITATIONCLIENT_VERSION "SourceSharpPrecipitationClient001"
#define SOURCESHARP_SHIM_MATERIALPROXYCLIENT_VERSION "SourceSharpMaterialProxyClient001"
#define SOURCESHARP_SHIM_INPUTCLIENT_VERSION "SourceSharpInputClient001"
#define SOURCESHARP_SHIM_PHYSICSCLIENT_VERSION "SourceSharpPhysicsClient001"
#define SOURCESHARP_SHIM_CONVARSCLIENT_VERSION "SourceSharpConVarsClient001"
#define SOURCESHARP_SHIM_FLASHLIGHTCLIENT_VERSION "SourceSharpFlashlightClient001"

abstract_class IShimHudClient
{
public:
	virtual int32_t GetLocalPlayerHealth() = 0;
	virtual int32_t GetLocalPlayerIndex() = 0;
	virtual void HudLocalise( const char *pToken, char *pOut, int32_t nOutSize ) = 0;
	virtual void HudLookupBinding( const char *pBinding, char *pOut, int32_t nOutSize ) = 0;
	virtual int32_t HudTextMessageGet( const char *pName, SourceSharpTextMessage *pOut ) = 0;
};

abstract_class IShimPredictionClient
{
public:
	virtual int32_t ClientGetUserCmd( int32_t nCommandNumber, SourceSharpUserCmd *pOut ) = 0;
	virtual void ClientPlayerPredictionDrive( uint32_t hEntity, const SourceSharpClientPlayerView *pView ) = 0;
	virtual int32_t MoveBeginClientCommand( uint32_t hPlayer, int32_t nCommandNumber, SourceSharpMoveState *pOutConstants ) = 0;
	virtual void MoveEndClientCommand( int32_t bRan, int32_t *pOutGroundIndex ) = 0;
};

abstract_class IShimTempEntPoolClient
{
public:
	virtual int32_t ClientEntityModelBounds( int32_t nEntityIndex, float *pOutMinsXYZ, float *pOutMaxsXYZ, float *pOutAnglesPYR ) = 0;
	virtual int32_t ClientEntityOrigin( int32_t nEntityIndex, float *pOutXYZ ) = 0;
	virtual void ClientWindSpeed( float flTime, float *pOutXYZ ) = 0;
	virtual int32_t TempEntPoolAddVisible( int32_t nSlot ) = 0;
	virtual int32_t TempEntPoolCapacity() = 0;
	virtual int32_t TempEntPoolCreate( int32_t nSlot, const float *pOriginXYZ, int32_t nModelIndex, int32_t nPriority ) = 0;
	virtual void TempEntPoolDestroy( int32_t nSlot ) = 0;
	virtual int32_t TempEntPoolReadLife( int32_t nSlot, SourceSharpTempEntLife_t *pOutLife ) = 0;
	virtual int32_t TempEntPoolReadStatics( int32_t nSlot, SourceSharpTempEntStatics_t *pOut ) = 0;
	virtual int32_t TempEntPoolReadTransform( int32_t nSlot, SourceSharpTempEntTransform_t *pOutTransform ) = 0;
	virtual int32_t TempEntPoolSimulate( int32_t nSlot, float flFrameTime, int32_t nFrameNumber ) = 0;
	virtual int32_t TempEntPoolWriteLife( int32_t nSlot, const SourceSharpTempEntLife_t *pLife ) = 0;
	virtual int32_t TempEntPoolWriteSpawn( int32_t nSlot, const SourceSharpTempEntSpawn_t *pSpawn ) = 0;
	virtual int32_t TempEntPoolWriteTransform( int32_t nSlot, const SourceSharpTempEntTransform_t *pTransform ) = 0;
};

abstract_class IShimTempEntBackendsClient
{
public:
	virtual void ClientDecalBrushModel( int32_t nEntity, const float *pDecalCenterXYZ, int32_t nDecalIndex ) = 0;
	virtual int32_t ClientDecalIndexFromName( const char *pName ) = 0;
	virtual int32_t ClientDecalNameFromIndex( int32_t nDecalIndex, char *pOut, int32_t nOutSize ) = 0;
	virtual void ClientDecalShoot( int32_t nDecalIndex, int32_t nEntity, const float *pPositionXYZ, const float *pSAxisXYZ, int32_t nFlags ) = 0;
	virtual void ClientDecalStaticProp( const float *pStartXYZ, const float *pEndXYZ, int32_t nStaticPropIndex, int32_t nDecalIndex ) = 0;
	virtual void ClientDecalStudioModel( int32_t nEntity, const float *pRayStartXYZ, const float *pRayEndXYZ, int32_t nDecalIndex ) = 0;
	virtual void ClientEnergySplash( const float *pPosXYZ, const float *pDirXYZ, int32_t bExplosive ) = 0;
	virtual int32_t ClientEntityModelType( int32_t nEntity ) = 0;
	virtual void ClientExplosionFireball( const float *pOriginXYZ ) = 0;
	virtual int32_t ClientIsLowViolence() = 0;
	virtual void ClientMetalSparks( const float *pPositionXYZ, const float *pDirectionXYZ ) = 0;
	virtual void ClientMuzzleFlash( const float *pOriginXYZ, const float *pAnglesPYR, float flScale, int32_t nType ) = 0;
	virtual void ClientPlayerDecalShoot( const float *pPositionXYZ, int32_t nPlayer, int32_t nEntity ) = 0;
	virtual void ClientRicochet( const float *pPositionXYZ, const float *pDirectionXYZ ) = 0;
	virtual void ClientSmoke( const float *pOriginXYZ, int32_t nModelIndex, float flScale, float flFrameRate ) = 0;
	virtual void ClientSparks( const float *pPositionXYZ, int32_t nMagnitude, int32_t nTrailLength, const float *pDirectionXYZ ) = 0;
	virtual void EffectDispatchName( int32_t nIndex, char *pOut, int32_t nOutBytes ) = 0;
	// The client's temp entity tables, planned in C# (ClientTempEntEngine) and
	// stored by native: one length-prefixed block per table, and the size of the
	// blob its receiving shell decodes into.
	virtual int32_t TempEntBuildClient( const void * pBlock, int32_t nBlockBytes, int32_t nBlobBytes, SourceSharpTeRecvBuilt_t * pOut ) = 0;
};

abstract_class IShimEffectsRenderClient
{
public:
	virtual int32_t DrawSprite( uint32_t hOwner, int32_t nModelIndex, float flFrame, int32_t nRenderMode, int32_t nRenderFx, int32_t r, int32_t g, int32_t b, int32_t a, float flScale, int32_t bWorldSpaceScale, float flHDRColorScale, float flGlowProxySize ) = 0;
	virtual int32_t DrawSpriteOffset( uint32_t hOwner, const float *pOffsetXYZ, int32_t nModelIndex, float flFrame, int32_t nRenderMode, int32_t nRenderFx, int32_t r, int32_t g, int32_t b, int32_t a, float flScale, int32_t bWorldSpaceScale, float flHDRColorScale, float flGlowProxySize ) = 0;
	virtual int32_t ParticleSystemFollowPoints( uint32_t hOwner, int32_t nPointMask ) = 0;
	virtual int32_t ParticleSystemStart( uint32_t hOwner, const char *pszName ) = 0;
	virtual void ParticleSystemStop( uint32_t hOwner ) = 0;
	virtual void SpriteTrailDestroy( uint32_t hOwner ) = 0;
	virtual void SpriteTrailUpdate( uint32_t hOwner, int32_t nRenderMode, int32_t bVisible ) = 0;
	virtual void WindPublish( const float *pWind, const float *pSway, int32_t nWinds ) = 0;
	virtual int32_t RopeMaterial( const char *pszName, int32_t *pOutTextureHeight, int32_t *pOutHasBack ) = 0;
	virtual void RopeDraw( int32_t nMaterial, const float *pSegs, int32_t nSegs, const float *pSolid, int32_t nSolid, int32_t nFlags ) = 0;
	virtual void ClientViewState( int32_t bCurrentView, float *pOutForwardXYZ, float *pOutOriginXYZ, float *pOutMainOriginXYZ, float *pOutCameraXYZ, int32_t *pOutScreenWidth, int32_t *pOutDxLevel, int32_t *pOutAAEnabled ) = 0;
	virtual void ClientComputeLighting( const float *pPosXYZ, float *pOutColorRGB, float *pOutBoxRGB18 ) = 0;
	virtual float ClientTraceHullWorld( const float *pStartXYZ, const float *pEndXYZ, const float *pMinsXYZ, const float *pMaxsXYZ, int32_t nMask, int32_t *pOutSolidFlags, float *pOutPlaneXYZD ) = 0;
	virtual void BeamView( int32_t bCurrentView, float *pOut42 ) = 0;
	virtual int32_t BeamModelInfo( int32_t nModelIndex, int32_t *pOutFrameCount ) = 0;
	virtual int32_t BeamModelIndex( const char *pszName ) = 0;
	virtual uint32_t BeamEntityAt( int32_t nEntry ) = 0;
	virtual int32_t BeamEntityHasModel( uint32_t hEntity ) = 0;
	virtual int32_t BeamEntityFacts( uint32_t hEntity, int32_t nAttachment, int32_t bTryAttachment, float *pOut9 ) = 0;
	virtual int32_t BeamBoxVisibility( const float *pMinsXYZ, const float *pMaxsXYZ ) = 0;
	virtual float BeamHaloVisibility( const float *pOriginXYZ, float flProxySize ) = 0;
	virtual int32_t BeamScreenPosition( const float *pPointXYZ, float *pOutScreenXYZ ) = 0;
	virtual int32_t BeamRenderableAdd( const float *pOriginMinsMaxs9 ) = 0;
	virtual void BeamRenderableChanged( int32_t nRenderable, const float *pOriginMinsMaxs9 ) = 0;
	virtual void BeamRenderableRemove( int32_t nRenderable ) = 0;
	virtual int32_t BeamBindSprite( int32_t nModelIndex, int32_t nFrame, int32_t nRenderMode, int32_t bWireframe ) = 0;
	virtual void BeamSetHdrColorScale( int32_t nModelIndex, int32_t nRenderMode, float flScale ) = 0;
	virtual int32_t BeamGlow( int32_t nModelIndex, int32_t bBind ) = 0;
	virtual void BeamDrawStrip( const float *pSegs, int32_t nSegs ) = 0;
	virtual void BeamDrawMesh( int32_t nPrimitive, int32_t nPrimitives, int32_t nColorKind, const float *pVerts, int32_t nVerts ) = 0;
};

abstract_class IShimStandaloneClient
{
public:
	virtual void ActivateOccluder( int32_t nOccluderIndex, int32_t bActive ) = 0;
	virtual void ** ClientInterpBlocks( int32_t *pOutCount, float *pOutInterpAmount ) = 0;
	virtual void ** ClientPolicyBlocks( int32_t *pOutCount ) = 0;
	virtual void DynamicLightRelease( int32_t nKey ) = 0;
	virtual void DynamicLightUpdate( const SourceSharpDynamicLight *pLight ) = 0;
	virtual int32_t StandaloneIsDormant( uint32_t hEntity ) = 0;
	virtual void StandaloneSetBlending( uint32_t hEntity, int32_t bTranslucent, int32_t bIgnoreZ, int32_t nAlpha ) = 0;
	virtual void StandaloneSetCustomDraw( uint32_t hEntity, const float *pMinsXYZ, const float *pMaxsXYZ ) = 0;
	virtual void StandaloneSetFade( uint32_t hEntity, int32_t nStyle, float flStartDist, float flEndDist, float flMinAlpha ) = 0;
	virtual void StandaloneSetOverlayModel( uint32_t hEntity, int32_t nModelIndex ) = 0;
	virtual void StandaloneSetShadowCast( uint32_t hEntity, int32_t nChoice ) = 0;
	virtual void StandaloneSetViewModel( uint32_t hEntity, int32_t bEnable ) = 0;
	virtual int32_t StandaloneShadowCast( uint32_t hEntity, int32_t *pOutChoice, int32_t *pOutHasShadow ) = 0;
	virtual void SurfaceSetEdges( uint32_t hEntity, const unsigned char *pEdges, int32_t nCount ) = 0;
	virtual int32_t ClientRecvProp( int32_t iIndex, char *pOutClass, int32_t nClassBytes, char *pOutTable, int32_t nTableBytes, char *pOutProp, int32_t nPropBytes, int32_t *pOutType, int32_t *pOutFlags, int32_t *pOutOffset, int32_t *pOutElements ) = 0;
	virtual int32_t ClientSchemaRefusal( int32_t iIndex, uint32_t *pOutTypeId, uint32_t *pOutExpected, uint32_t *pOutSeen, int32_t *pOutCount, char *pOutClass, int32_t nClassBytes, int32_t *pOutRefusedUpdates ) = 0;
	virtual void * StandaloneEngineBlock( uint32_t hEntity, int32_t *pOutBytes ) = 0;
	virtual void StandaloneStoreSequence( uint32_t hEntity, int32_t nSequence, float flPlaybackRate, float flAnimTime, float flDuration, int32_t bLooping ) = 0;
	virtual void StandaloneSetRagdoll( uint32_t hEntity, int32_t bRagdoll ) = 0;
	virtual void EntitySetSkin( uint32_t hEntity, int32_t nSkin ) = 0;
	virtual void RegisterAmmoType( const char * pName, int32_t nDamageType, int32_t nMaxCarry, int32_t nTracerType, float flDamageForce, float flDamage ) = 0;
};

abstract_class IShimViewClient
{
public:
	virtual void ConNPrintf( void *pEngineClient, int32_t nPos, const char *pszText ) = 0;
	virtual void ConNXPrintf( void *pEngineClient, int32_t nIndex, float flTimeToLive, const float *pColorRGB, int32_t bFixedWidthFont, const char *pszText ) = 0;
	virtual void GetLightForPoint( void *pEngineClient, const float *pPosXYZ, int32_t bClamp, float *pOutXYZ ) = 0;
	virtual void GetLightForPointFast( void *pEngineClient, const float *pPosXYZ, int32_t bClamp, float *pOutXYZ ) = 0;
	virtual int32_t ClientColorCorrection( int32_t nOp, int32_t nHandle, const char *pLookupFile, float flWeight ) = 0;
	virtual void ClientSetWorldRenderState( int32_t nWhich, const float *pValues, int32_t nCount ) = 0;
	virtual int32_t GetLocalPlayerOrigin( float *pOutXYZ ) = 0;
	virtual void SetViewerFogParams( const SourceSharpFogParams *pParams ) = 0;
	virtual void SetViewerSkyboxParams( const SourceSharpSkyboxParams *pParams ) = 0;
	virtual int32_t ClientTraceLine( const float *pStartXYZ, const float *pEndXYZ, int32_t nMask, int32_t nFilter, int32_t nPassEntityIndex, float *pOutFraction, float *pOutEndXYZ, float *pOutNormalXYZ, int32_t *pOutHitbox, int32_t *pOutEntityIndex, int32_t *pOutSurfaceProps, int32_t *pOutSurfaceFlags ) = 0;
	// The observer's view (plan_client_novalve C7 item 5): a hull trace with a
	// stock filter and a pass entity, an entity's drawn pose by index, and the
	// view the client mode's OverrideView draws this frame instead of the
	// player's eye (managed decides it; 0 hands the view back).
	virtual int32_t ClientTraceHull( const float *pStartXYZ, const float *pEndXYZ, const float *pMinsXYZ, const float *pMaxsXYZ, int32_t nMask, int32_t nFilter, int32_t nPassEntityIndex, float *pOutFraction, float *pOutEndXYZ ) = 0;
	virtual int32_t ClientEntityRenderPose( int32_t nEntityIndex, float *pOutOriginXYZ, float *pOutAngles ) = 0;
	virtual void ViewOverrideSet( int32_t nActive, const float *pOriginXYZ, const float *pAngles, int32_t nSetViewAngles ) = 0;
	virtual int32_t EntitiesUpdateVisibilityAll() = 0;
	// A second camera into a named render target (lane camproto, ahead of
	// plan_client_novalve C7): managed queues a view for this frame's render;
	// shell_view_targets.cpp carries it out at the client mode's PreRender, the
	// point stock's DrawMonitors draws, and answers the target's facts.
	virtual int32_t RenderViewToTarget( int32_t nTarget, const SourceSharpViewTargetSetup *pSetup ) = 0;
	virtual int32_t ViewTargetInfo( int32_t nTarget, int32_t *pOut4 ) = 0;
	// rendertexture.cpp's getters (C7): one slot's texture, group, FindTexture
	// arguments and place in the release's order, as managed's
	// RenderTargetCatalog decided them; shell_render_targets_client.cpp finds
	// on the slot's first use and drops the reference at the release.
	virtual int32_t RenderTargetDeclare( int32_t nSlot, const char *pszName, const char *pszGroup, int32_t bComplain, int32_t nCreationFlags, int32_t nReleaseOrder ) = 0;
};

// plan_side_isolation §2.2a: the client half of user messages. Managed reads a
// message's payload through the first and decides what it does; the rest carry
// out what the stock hooks did, over values C# parsed.
abstract_class IShimUserMessagesClient
{
public:
	virtual int32_t ClientViewportQuery( const char *pPanel ) = 0;
	virtual void ClientViewportShow( const char *pPanel, int32_t bShow, const unsigned char *pPairs, int32_t nPairBytes, int32_t nFlags ) = 0;
};

// The client's net channel: a SourceSharp message to the server
// (ss_client_message.h), sent reliably. 1 when it was queued, 0 when there is
// no channel or the message does not fit.
abstract_class IShimNetClient
{
public:
	virtual int32_t SendServerMessage( const char *pKey, const uint8_t *pData, int32_t nLen ) = 0;
	virtual int32_t ClientNetFlow( int32_t nFlow, float *pOut, int32_t nOut ) = 0;
	virtual int32_t ClientNetChannel( float *pOut, int32_t nOut ) = 0;
	virtual int32_t ClientNetPacket( int32_t nFlow, int32_t nFrame, float *pOut, int32_t nOut ) = 0;
};

// The client's diagnostics ledgers, read for the dev API's absences route
// (plan_client_novalve §4.4): the not-shipped capabilities and the client
// DLL's refused methods, one row per index.
abstract_class IShimDiagnosticsClient
{
public:
	virtual int32_t ClientNotShipped( int32_t iIndex, char *pOutName, int32_t nNameBytes, char *pOutReason, int32_t nReasonBytes, int32_t *pOutCount, int32_t *pOutTotal ) = 0;
	virtual int32_t ClientRefusal( int32_t iIndex, char *pOutMethod, int32_t nMethodBytes, int32_t *pOutCount, int32_t *pOutTotal ) = 0;
};

// The UI's draw primitives over vgui::ISurface (plan_client_ui §8 U2): what
// VguiBackend turns a render list into. Each is one surface call; none decides
// anything. Colours are packed 0xRRGGBBAA. Draw slots refuse outside a paint
// pass (shell_ui.cpp). Text is UTF-16, as C# holds it.
abstract_class IShimUiClient
{
public:
	virtual void UiDrawFilledRect( int32_t x0, int32_t y0, int32_t x1, int32_t y1, uint32_t nRgba ) = 0;
	virtual void UiDrawFilledRectFade( int32_t x0, int32_t y0, int32_t x1, int32_t y1, uint32_t nRgba, int32_t nAlpha0, int32_t nAlpha1, int32_t bHorizontal ) = 0;
	virtual void UiDrawOutlinedRect( int32_t x0, int32_t y0, int32_t x1, int32_t y1, uint32_t nRgba ) = 0;
	virtual void UiDrawLine( int32_t x0, int32_t y0, int32_t x1, int32_t y1, uint32_t nRgba ) = 0;
	virtual void UiDrawTexturedRect( int32_t nTexture, int32_t x0, int32_t y0, int32_t x1, int32_t y1, float s0, float t0, float s1, float t1, uint32_t nRgba ) = 0;
	virtual void UiDrawPolygon( int32_t nTexture, const float *pXyuv, int32_t nVertices, uint32_t nRgba ) = 0;
	virtual void UiDrawText( int32_t nFont, int32_t x, int32_t y, const uint16_t *pText, int32_t nLength, uint32_t nRgba ) = 0;
	virtual void UiMeasureText( int32_t nFont, const uint16_t *pText, int32_t nLength, int32_t *pOutWidth, int32_t *pOutHeight ) = 0;
	virtual void UiSetClip( int32_t bEnabled, int32_t x0, int32_t y0, int32_t x1, int32_t y1 ) = 0;
	virtual int32_t UiTextureCreate() = 0;
	virtual void UiTextureUpload( int32_t nTexture, const uint8_t *pRgba, int32_t nWide, int32_t nTall ) = 0;
	virtual int32_t UiFontAddFile( const char *pPath ) = 0;
	virtual int32_t UiFontCreate( const char *pFamily, int32_t nTall, int32_t nWeight, int32_t nFlags ) = 0;
	virtual void UiFontMetrics( int32_t nFont, int32_t *pOutTall, int32_t *pOutAscent ) = 0;
	virtual int32_t UiFontAdvances( int32_t nFont, int32_t nFirst, int32_t nCount, int32_t *pOutAdvances ) = 0;
	virtual void UiScreenSize( int32_t *pOutWide, int32_t *pOutTall ) = 0;
	virtual void UiPlaySound( const char *pPath ) = 0;
	virtual void UiSetMenuInput( int32_t bMouse, int32_t bKeyboard ) = 0;
	virtual void UiSetCursor( int32_t nShape ) = 0;
	virtual int32_t UiDebugOverlayText( int32_t nIndex, float *pOutX, float *pOutY, int32_t *pOutLine, uint32_t *pOutRgba, uint8_t *pText, int32_t nTextBytes ) = 0;
	virtual void UiDebugOverlayClearDead( void ) = 0;
	// g_pClientMode->Layout() (C7, vgui_game_viewport.cpp's CViewportClientSystem
	// asked it at its Init; managed's ViewportClientSystem asks it now).
	virtual void ClientModeLayout( void ) = 0;
};

// plan_client_novalve C2 (lane c2-sims): the executor for the client's simple
// particle sims, which are C#'s (shell_managed_particles.cpp). One effect per
// (owner, channel); managed decides the material, the cull radius and whether
// it simulates out of view, and native registers it with CParticleMgr.
abstract_class IShimParticlesClient
{
public:
	virtual int32_t ParticlesCreate( uint32_t hOwner, int32_t nChannel, const char *pszMaterial, float flCullRadius, int32_t bAlwaysSimulate ) = 0;
	virtual void ParticlesConfigure( uint32_t hOwner, int32_t nChannel, float flCullRadius ) = 0;
	virtual void ParticlesDestroy( uint32_t hOwner, int32_t nChannel ) = 0;
	virtual void ParticlesSetLights( uint32_t hOwner, int32_t nChannel, const float *pLights, int32_t nFloats ) = 0;
	virtual int32_t ParticlesTraceLine( const float *pStartXYZ, const float *pEndXYZ, int32_t nMask, float *pOut, int32_t nOut ) = 0;
	virtual int32_t WorldTextMeshUpdate( uint32_t hOwner, const char *pszMaterial, const float *pBounds, int32_t nFloats ) = 0;
	virtual void WorldTextMeshDestroy( uint32_t hOwner ) = 0;
	// /client/{slot}/particles (lane c5-particles): one started system by index, the count the return.
	virtual int32_t ParticleSystemsRead( int32_t nIndex, SourceSharpParticleSystemInfo_t *pOut, char *pszName, int32_t nNameMax ) = 0;
	// Full reads of the particle manifest's definitions since the process started.
	virtual int32_t ParticleDefinitionReads() = 0;
	// The particle query's calls that found no managed answer, per method (lane c5-particles); the count written.
	virtual int32_t ParticleQueryUnanswered( int32_t *pOut, int32_t nMax ) = 0;
	// One integer var of an executor's material (the lit sims' $USINGPIXELSHADER, lane c5-particles): 1 and the value, or 0.
	virtual int32_t ParticlesMaterialVarInt( uint32_t hOwner, int32_t nChannel, const char *pszVar, int32_t *pValue ) = 0;
	// An executor for an ownerless effect at a world point (lane c5-particles), keyed by (0xFFFFFFFF, nId).
	virtual int32_t ParticlesCreateAt( int32_t nId, const char *pszMaterial, const float *pOriginXYZ, float flCullRadius, int32_t bAlwaysSimulate ) = 0;
	// The ParticleEffect client effect's carry-out (lane c5-particles): a system's name by its table index, and a one-shot at a point.
	virtual int32_t ParticleSystemNameFromIndex( int32_t nIndex, char *pszOut, int32_t nOutMax ) = 0;
	virtual int32_t ParticleSystemOneShot( const char *pszName, const float *pSortOriginXYZ, const float *pPoints, int32_t nPoints, const float *pOrientation0 ) = 0;
	// The particle collision's one trace (lane c5-particles): UTIL_TraceLine MASK_SOLID_BRUSHONLY, COLLISION_GROUP_NONE; out fraction, endpos xyz, normal xyz, dist, allsolid, startsolid.
	virtual int32_t ParticlesWorldTrace( const float *pStartXYZ, const float *pEndXYZ, float *pOut, int32_t nOut ) = 0;
	// The view the particle manager is drawing (lane c5-particles): its model-view rows (12 floats), CurrentViewID(),
	// and CurrentViewOrigin() / CurrentViewForward() (6 floats). Managed reads it once per view, when the key
	// IManagedParticlesClient::Draw carries changes.
	virtual void ParticlesView( float *pModelView12, int32_t *pViewId, float *pOriginForward6 ) = 0;
	// PixelVisibility_FractionVisible at a point, through the caller's query handle (kept managed).
	virtual float ParticlesPixelVisible( const float *pOriginXYZ, int32_t *pQueryHandle ) = 0;
	// g_pParticleSystemMgr->ParticleThrottleScaling(): 1 unrestricted .. 0 fully, which the metal sparks scale their count by.
	// A line through the world with the contents mask managed names (lane c5-particles: fx_water's MASK_WATER, fx_explosion's
	// CONTENTS_SOLID), COLLISION_GROUP_NONE; out fraction, endpos xyz, normal xyz, startsolid, fractionleftsolid.
	virtual int32_t ParticlesTraceMask( const float *pStartXYZ, const float *pEndXYZ, uint32_t nMask, float *pOut, int32_t nOut ) = 0;
	// An executor's space (lane c5-particles: the temp entities' muzzle flashes, PoolEmitters): flags 1 its particles are in
	// the entity attachment's space (CLocalSpaceEmitter), 2 placed for the view model's FOV (FLE_VIEWMODEL), 4 drawn
	// before the view model (SetDrawBeforeViewModel).
	virtual int32_t ParticlesSetSpace( uint32_t hOwner, int32_t nChannel, uint32_t hEntity, int32_t nAttachment, int32_t nFlags, SourceSharpParticleFollow_t *pFollow ) = 0;
	// The contents at a point inside a managed brush entity's own brush (lane c5-particles: the dust motes'
	// spawn test, enginetrace->GetPointContents_Collideable over the owner as stock's SOLID_BSP dust volume).
	virtual int32_t ParticlesBrushPointContents( uint32_t hOwner, const float *pPointXYZ ) = 0;
};

// The client's effects (plan_client_novalve §2 clusters 3 and 4): the effect
// registrations, read and run by walk index; the dispatch-by-name decision is
// managed's (EffectDispatchTable).
abstract_class IShimFxClient
{
public:
	virtual int32_t EffectRegistrationCount() = 0;
	virtual int32_t EffectRegistrationName( int32_t iIndex, char *pOut, int32_t nOutSize ) = 0;
	virtual void EffectRegistrationRun( int32_t iIndex, const SourceSharpEffectData_t *pData ) = 0;
	// IEffects' FX_Dust, carried out for the managed decisions (ClientEffects;
	// fx.cpp's routines are C#, FxEffects), and the two answers stock's SuppressTE reads.
	virtual int32_t FxCanPredict() = 0;
	virtual int32_t FxRecipientCount( const float *pOriginXYZ ) = 0;
	// The client-side effect list's carry-out (ClientEffectList): materials by
	// id, one quad-mesh draw, the view reads, the near-miss sound, and what the
	// tracer effects ask of the engine and the entities.
	virtual int32_t FxMaterialFind( const char *pName ) = 0;
	virtual void FxMaterialAddRef( int32_t nMaterial ) = 0;
	virtual void FxMaterialRelease( int32_t nMaterial ) = 0;
	virtual int32_t FxMaterialName( int32_t nMaterial, char *pOut, int32_t nOutSize ) = 0;
	virtual void FxDrawQuads( int32_t nMaterial, int32_t nVertices, const float *pVertices, int32_t bByteColor ) = 0;
	virtual void FxView( float *pOut ) = 0;
	virtual void FxMainViewOrigin( float *pOutXYZ ) = 0;
	virtual int32_t FxLocalPlayer() = 0;
	virtual void FxViewAngles( float *pOutPYR ) = 0;
	virtual int32_t FxEntityAttachment( int32_t nEntIndex, int32_t nAttachment, float *pOutXYZ, char *pModelOut, int32_t nModelSize ) = 0;
	virtual void FxDispatchParticleEffect( int32_t nSystem, const float *pStartXYZ, const float *pEndXYZ, const float *pAnglesPYR, int32_t nEntIndex ) = 0;
	// The shell-eject effects' two (ShellEjectEffect): the owner's render
	// angles, and the legacy temp entity pool's brass.
	virtual int32_t FxEntityRenderAngles( int32_t nEntIndex, float *pOutPYR ) = 0;
	virtual void FxEjectBrass( const float *pOriginXYZ, const float *pAnglesPYR, const float *pGunAnglesPYR, int32_t nType ) = 0;
	// The sprite planner's two engine answers (SpriteRenderer): the pixel
	// visibility of the sprite being planned, on its renderer's query handle,
	// and GlowSightDistance.
	virtual float FxSpriteGlowFraction( const float *pPositionXYZ, float flProxySize, float flProxyAspect ) = 0;
	virtual float FxGlowSightDistance( const float *pPositionXYZ ) = 0;
	// A managed sprite planned in managed (ClientSpriteHost.DrawSprite): the
	// request, then the plan carried out -- no crossing back for the plan.
	virtual int32_t FxSpriteDrawBegin( uint32_t hOwner, const float *pOffsetXYZ, int32_t nModelIndex, float flFrame, int32_t nRenderMode, int32_t nRenderFx, int32_t r, int32_t g, int32_t b, int32_t a, float flScale, int32_t bWorldSpaceScale, float flHDRColorScale, float flGlowProxySize, float *pIn ) = 0;
	virtual int32_t FxSpriteDrawEnd( const float *pOut ) = 0;
	virtual int32_t FxSpriteDrawStore( const float *pOut ) = 0;
	// The client effect precache registry (ClientEffectPrecache): its size,
	// one registration's Cache, and the two level-start caches.
	virtual int32_t FxPrecacheCount() = 0;
	virtual void FxPrecacheCache( int32_t nIndex, int32_t bPrecache ) = 0;
	virtual void FxCacheUsedMaterials() = 0;
	virtual void FxCacheMaterialHandles() = 0;
	// The legacy tempents' muzzle flashes' engine reads (IFxFlashReads; their emitters are
	// managed, PoolEmitters, lane c5-particles).
	virtual int32_t FxRenderableAttachment( uint32_t hEntity, int32_t nAttachment, float *pOut6 ) = 0;
	virtual int32_t FxHandleEntityIndex( uint32_t hEntity ) = 0;
	virtual void FxElight( int32_t nKey, const float *pOriginXYZ, int32_t r, int32_t g, int32_t b, int32_t nExponent, float flRadius, float flDecay, float flDie ) = 0;
	virtual void FxVectorRandom( float flLo, float flHi, float *pOutXYZ ) = 0;
	virtual int32_t FxMaterialsCached() = 0;
	// fx.cpp's FX_GunshipImpact overlay (lane c5-particles: FxEffects.GunshipImpact): a warp overlay at the
	// position managed placed, in its base colour, ramped as stock's CImpactOverlay.
	virtual void FxImpactOverlay( const float *pPositionXYZ, const float *pColorRGB ) = 0;
	// The tempents' bounce sounds (TempEntSounds), and the models they spawn by name.
	virtual int32_t FxTempEntCreateNamed( int32_t nSlot, const float *pOriginXYZ, const char *pModel, int32_t nPriority ) = 0;
	virtual void FxLoadModel( const char *pModel ) = 0;
	virtual int32_t FxModelFrameCount( const char *pModel ) = 0;
};

// The client soundscape system's executors (sound cluster 17, lane c2-sims):
// the engine calls managed ClientSoundscapes decides, carried out as stock's
// C_SoundscapeSystem made them, and the listener's main view it places
// sounds around.
abstract_class IShimSoundscapeClient
{
public:
	virtual void SoundscapeSetRoomType( int32_t nRoomType ) = 0;
	virtual void SoundscapeSetPlayerDsp( int32_t nDspType ) = 0;
	virtual void SoundscapeMainView( float *pOut9 ) = 0;
	virtual int32_t SoundscapeMakingResLists() = 0;
	virtual void SoundscapeTouchSoundFile( const char *pszWave ) = 0;
	// The client's sound patches (plan_server_native_v2 T4a): IShimEngine's
	// five SoundPatch slots, the client's alone since the server's patches
	// are managed (ServerSoundPatches).
	virtual void SoundPatchEmit( int32_t hPatch, float flVolume01, float flPitch, int32_t nFlags, float flStartTime, int32_t bEmitCaption ) = 0;
	virtual void SoundPatchStop( int32_t hPatch ) = 0;
	virtual void SoundPatchState( int32_t hPatch, int32_t *pnEntIndex, int32_t *pbFilterActive ) = 0;
	virtual void SoundPatchFree( int32_t hPatch ) = 0;
	virtual int32_t SoundPatchCreate( uint32_t hEntity, int32_t nChannel, const char *pSoundName, int32_t nSoundLevel ) = 0;
};

// The client weather's world (plan_client_novalve C2, lane c2-sims): the
// traces, contents, PVS, player, world bounds and screen width the managed
// PrecipitationSystem asks, each one engine read.
abstract_class IShimPrecipitationClient
{
public:
	virtual int32_t PrecipitationTraceLine( const float *pStartXYZ, const float *pEndXYZ, int32_t nMask, int32_t bIgnorePlayer, float *pOut ) = 0;
	virtual int32_t PrecipitationTraceVolume( uint32_t hOwner, const float *pStartXYZ, const float *pEndXYZ, const float *pHullMinXYZ, const float *pHullMaxXYZ, float *pOut ) = 0;
	virtual int32_t PrecipitationPointContents( const float *pXYZ ) = 0;
	virtual int32_t PrecipitationInPvs( uint32_t hOwner ) = 0;
	virtual int32_t PrecipitationPlayer( float *pOut ) = 0;
	virtual void PrecipitationWorldBounds( float *pOut6 ) = 0;
	virtual int32_t PrecipitationScreenWidth() = 0;
};

// The material proxies (plan_client_novalve §2 cluster 1, lane c5-proxies):
// what a managed proxy reads through the engine, by the proxy's handle --
// FindVar and FindTexture at Init (native holds the pointers, managed the
// slots), GetTextureValue, the bind argument as an entity, the view and the
// world, a player's spray -- and the not-shipped row a refusal raises.
abstract_class IShimMaterialProxyClient
{
public:
	virtual int32_t ProxyFindVar( int32_t hProxy, const char *pszName, int32_t bComplain, int32_t *pFound ) = 0;
	virtual int32_t ProxyFindTexture( int32_t hProxy, const char *pszName, const char *pszGroup, int32_t bComplain, int32_t *pIsError ) = 0;
	virtual int32_t ProxyVarTexture( int32_t hProxy, int32_t nSlot, char *pOutName, int32_t nOutSize ) = 0;
	virtual int32_t ProxyBindEntity( int32_t hProxy, SourceSharpProxyEntity_t *pOut ) = 0;
	virtual int32_t ProxyReadView( int32_t nKind, float *pOut, int32_t nFloats ) = 0;
	virtual int32_t ProxyPlayerCustomFile( int32_t nPlayer, uint32_t *pCrc ) = 0;
	virtual void ProxyNotShipped( const char *pszCapability, const char *pszReason ) = 0;
};

// plan_client_novalve §2 cluster 13 (lane c6-input): what the managed input
// (ClientInput) asks of the native client that the engine's own vtables do not
// answer: vgui's cursor, the HUD's facts, the view's pitch drift, prediction's
// view angles, the viewer seam and the viewport's scoreboard.
abstract_class IShimInputClient
{
public:
	virtual void InputCursorState( int32_t *pVisible, int32_t *pLocked ) = 0;
	virtual void InputGetCursorPos( int32_t *pX, int32_t *pY ) = 0;
	virtual void InputSetCursorPos( int32_t x, int32_t y ) = 0;
	virtual void InputSetCursorArrow() = 0;
	virtual void InputVguiScreenSize( int32_t *pWide, int32_t *pTall ) = 0;
	virtual void InputHudFacts( float *pSensitivity, int32_t *pKeyBits, float *pFovAdjust ) = 0;
	virtual void InputStopPitchDrift() = 0;
	virtual void InputSetLocalViewAngles( const float *pAngles ) = 0;
	virtual void InputViewerCameraModeChanged() = 0;
	virtual int32_t InputViewerAimIsAssisted() = 0;
	virtual void InputShowScoreboard( int32_t bShow ) = 0;
	virtual void * InputMailbox() = 0;
};

// plan_client_novalve C6, cluster 9 (lane c6-input): what the client's managed
// physics callbacks ask of the native client -- a surfaceprop's impact audio,
// the emit of one impact sound, and the splash's reads and effects.
abstract_class IShimPhysicsClient
{
public:
	virtual int32_t PhysicsClientSurfaceAudio( int32_t nSurfaceProps, char *pHard, int32_t nHard, char *pSoft, int32_t nSoft, float *pOut3 ) = 0;
	virtual void PhysicsClientCollideExtent( void *pObject, const float *pOrigin, const float *pAngles, const float *pDirection, float *pOut ) = 0;
	virtual int32_t PhysicsClientPointContents( const float *pPoint ) = 0;
	virtual void PhysicsClientSplashLighting( const float *pPoint, float *pOut4 ) = 0;
	virtual void PhysicsClientSplashEffect( int32_t nKind, const float *pOrigin, const float *pNormal, float flScale, const float *pColor, float flLifetime, float flAlpha ) = 0;
};

// plan_client_novalve C8 (lane c6-input): convars the managed client watches
// and the values it decides for native objects the engine reads through
// their vtable. ConVarsClientWatch names a convar (the engine's or ours) whose
// changes the global change callback forwards through ConVarChanged with the
// answered id (-1: no such convar). ConVarsClientSetValue stores a decided
// value under a name native reads from (the server-bounded convars' GetFloat,
// GetClientInterpAmount); 0 for a name nothing reads.
abstract_class IShimConVarsClient
{
public:
	virtual int32_t ConVarsClientWatch( const char *pName ) = 0;
	virtual int32_t ConVarsClientSetValue( const char *pName, float flValue ) = 0;
};

// plan_client_novalve C7 cluster 12: the flashlight's executor
// (shell_flashlight_client.cpp). SourceSharp.Client.Flashlight decides the
// projected texture; these carry it to the client shadow manager.
// FlashlightTexture holds a texture by name and answers its id (-1: none);
// Create answers the shadow handle (-1: none).
abstract_class IShimFlashlightClient
{
public:
	virtual int32_t FlashlightTexture( const char *pszName ) = 0;
	virtual int32_t FlashlightSupportsBorderColor() = 0;
	virtual int32_t FlashlightCreate( const SourceSharpFlashlightState *pState ) = 0;
	virtual void FlashlightUpdate( int32_t nHandle, const SourceSharpFlashlightState *pState ) = 0;
	virtual void FlashlightProject( int32_t nHandle ) = 0;
	virtual void FlashlightDestroy( int32_t nHandle ) = 0;
};

#endif // ISHIM_AREAS_CLIENT_H
