//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The client-only shim areas, served by the client's shim root by version string.
// Hand-kept and read by tools/shim-areas. Slots are an ABI: append only, blittable
// arguments, no virtual destructor.

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
	virtual void HudLocalise(const char* pToken, char* pOut, int32_t nOutSize) = 0;
	virtual void HudLookupBinding(const char* pBinding, char* pOut, int32_t nOutSize) = 0;
	virtual int32_t HudTextMessageGet(const char* pName, SourceSharpTextMessage* pOut) = 0;
};

abstract_class IShimPredictionClient
{
public:
	virtual int32_t ClientGetUserCmd(int32_t nCommandNumber, SourceSharpUserCmd* pOut) = 0;
	virtual void ClientPlayerPredictionDrive(uint32_t hEntity, const SourceSharpClientPlayerView* pView) = 0;
	virtual int32_t MoveBeginClientCommand(uint32_t hPlayer, int32_t nCommandNumber,
										   SourceSharpMoveState* pOutConstants) = 0;
	virtual void MoveEndClientCommand(int32_t bRan, int32_t* pOutGroundIndex) = 0;
};

abstract_class IShimTempEntPoolClient
{
public:
	virtual int32_t ClientEntityModelBounds(int32_t nEntityIndex, float* pOutMinsXYZ, float* pOutMaxsXYZ,
											float* pOutAnglesPYR) = 0;
	virtual int32_t ClientEntityOrigin(int32_t nEntityIndex, float* pOutXYZ) = 0;
	virtual void ClientWindSpeed(float flTime, float* pOutXYZ) = 0;
	virtual int32_t TempEntPoolAddVisible(int32_t nSlot) = 0;
	virtual int32_t TempEntPoolCapacity() = 0;
	virtual int32_t TempEntPoolCreate(int32_t nSlot, const float* pOriginXYZ, int32_t nModelIndex,
									  int32_t nPriority) = 0;
	virtual void TempEntPoolDestroy(int32_t nSlot) = 0;
	virtual int32_t TempEntPoolReadLife(int32_t nSlot, SourceSharpTempEntLife_t* pOutLife) = 0;
	virtual int32_t TempEntPoolReadStatics(int32_t nSlot, SourceSharpTempEntStatics_t* pOut) = 0;
	virtual int32_t TempEntPoolReadTransform(int32_t nSlot, SourceSharpTempEntTransform_t* pOutTransform) = 0;
	virtual int32_t TempEntPoolWriteLife(int32_t nSlot, const SourceSharpTempEntLife_t* pLife) = 0;
	virtual int32_t TempEntPoolWriteSpawn(int32_t nSlot, const SourceSharpTempEntSpawn_t* pSpawn) = 0;
	virtual int32_t TempEntPoolWriteTransform(int32_t nSlot, const SourceSharpTempEntTransform_t* pTransform) = 0;
};

abstract_class IShimTempEntBackendsClient
{
public:
	virtual void ClientDecalBrushModel(int32_t nEntity, const float* pDecalCenterXYZ, int32_t nDecalIndex) = 0;
	virtual int32_t ClientDecalIndexFromName(const char* pName) = 0;
	virtual int32_t ClientDecalNameFromIndex(int32_t nDecalIndex, char* pOut, int32_t nOutSize) = 0;
	virtual void ClientDecalShoot(int32_t nDecalIndex, int32_t nEntity, const float* pPositionXYZ,
								  const float* pSAxisXYZ, int32_t nFlags) = 0;
	virtual void ClientDecalStaticProp(const float* pStartXYZ, const float* pEndXYZ, int32_t nStaticPropIndex,
									   int32_t nDecalIndex) = 0;
	virtual void ClientDecalStudioModel(int32_t nEntity, const float* pRayStartXYZ, const float* pRayDeltaXYZ,
										int32_t nDecalIndex) = 0;
	virtual void ClientEnergySplash(const float* pPosXYZ, const float* pDirXYZ, int32_t bExplosive) = 0;
	virtual int32_t ClientEntityModelType(int32_t nEntity) = 0;
	virtual void ClientMetalSparks(const float* pPositionXYZ, const float* pDirectionXYZ) = 0;
	virtual void ClientMuzzleFlash(const float* pOriginXYZ, const float* pAnglesPYR, float flScale, int32_t nType) = 0;
	virtual void ClientPlayerDecalShoot(const float* pPositionXYZ, int32_t nPlayer, int32_t nEntity) = 0;
	virtual void ClientRicochet(const float* pPositionXYZ, const float* pDirectionXYZ) = 0;
	virtual void ClientSmoke(const float* pOriginXYZ, int32_t nModelIndex, float flScale, float flFrameRate) = 0;
	virtual void ClientSparks(const float* pPositionXYZ, int32_t nMagnitude, int32_t nTrailLength,
							  const float* pDirectionXYZ) = 0;
	virtual void EffectDispatchName(int32_t nIndex, char* pOut, int32_t nOutBytes) = 0;
	// Stores one length-prefixed temp entity table block, and the blob size its shell decodes into.
	virtual int32_t TempEntBuildClient(const void* pBlock, int32_t nBlockBytes, int32_t nBlobBytes,
									   SourceSharpTeRecvBuilt_t* pOut) = 0;
	// A bullet impact's MASK_SHOT trace, surface lighting reads and effect ledger rows.
	virtual int32_t ClientShotTrace(const float* pStartXYZ, const float* pEndXYZ, int32_t nEntity, float* pOutFloats,
									int32_t nOutFloats, int32_t* pOutInts, int32_t nOutInts) = 0;
	virtual void ClientWorldLighting(const float* pStartXYZ, const float* pEndXYZ, float* pOut6) = 0;
	virtual void ClientStaticPropLighting(const float* pStartXYZ, const float* pEndXYZ, const float* pNormalXYZ,
										  int32_t nStaticProp, float* pOut6) = 0;
	virtual void ClientImpactLedger(int32_t nFlecks, int32_t nDust) = 0;
	// A player's spray: the logo material and the downloaded texture, by name.
	virtual void ClientPlayerLogoDecal(const float* pPositionXYZ, int32_t nPlayer, int32_t nEntity,
									   const char* pszMaterial, const char* pszTexture) = 0;
};

abstract_class IShimEffectsRenderClient
{
public:
	virtual int32_t DrawSprite(uint32_t hOwner, int32_t nModelIndex, float flFrame, int32_t nRenderMode,
							   int32_t nRenderFx, int32_t r, int32_t g, int32_t b, int32_t a, float flScale,
							   int32_t bWorldSpaceScale, float flHDRColorScale, float flGlowProxySize) = 0;
	virtual int32_t DrawSpriteOffset(uint32_t hOwner, const float* pOffsetXYZ, int32_t nModelIndex, float flFrame,
									 int32_t nRenderMode, int32_t nRenderFx, int32_t r, int32_t g, int32_t b, int32_t a,
									 float flScale, int32_t bWorldSpaceScale, float flHDRColorScale,
									 float flGlowProxySize) = 0;
	virtual int32_t ParticleSystemFollowPoints(uint32_t hOwner, int32_t nPointMask) = 0;
	virtual int32_t ParticleSystemStart(uint32_t hOwner, const char* pszName) = 0;
	virtual void ParticleSystemStop(uint32_t hOwner) = 0;
	virtual void SpriteTrailDestroy(uint32_t hOwner) = 0;
	virtual void SpriteTrailUpdate(uint32_t hOwner, int32_t nRenderMode, int32_t bVisible) = 0;
	virtual void WindPublish(const float* pWind, const float* pSway, int32_t nWinds) = 0;
	virtual int32_t RopeMaterial(const char* pszName, int32_t* pOutTextureHeight, int32_t* pOutHasBack) = 0;
	virtual void RopeDraw(int32_t nMaterial, const float* pSegs, int32_t nSegs, const float* pSolid, int32_t nSolid,
						  int32_t nFlags) = 0;
	virtual void ClientViewState(int32_t bCurrentView, float* pOutForwardXYZ, float* pOutOriginXYZ,
								 float* pOutMainOriginXYZ, float* pOutCameraXYZ, int32_t* pOutScreenWidth,
								 int32_t* pOutDxLevel, int32_t* pOutAAEnabled) = 0;
	virtual void ClientComputeLighting(const float* pPosXYZ, float* pOutColorRGB, float* pOutBoxRGB18) = 0;
	virtual float ClientTraceHullWorld(const float* pStartXYZ, const float* pEndXYZ, const float* pMinsXYZ,
									   const float* pMaxsXYZ, int32_t nMask, int32_t* pOutSolidFlags,
									   float* pOutPlaneXYZD) = 0;
	virtual void BeamView(int32_t bCurrentView, float* pOut42) = 0;
	virtual int32_t BeamModelInfo(int32_t nModelIndex, int32_t* pOutFrameCount) = 0;
	virtual int32_t BeamModelIndex(const char* pszName) = 0;
	virtual uint32_t BeamEntityAt(int32_t nEntry) = 0;
	virtual int32_t BeamEntityHasModel(uint32_t hEntity) = 0;
	virtual int32_t BeamEntityFacts(uint32_t hEntity, int32_t nAttachment, int32_t bTryAttachment, float* pOut9) = 0;
	virtual int32_t BeamBoxVisibility(const float* pMinsXYZ, const float* pMaxsXYZ) = 0;
	virtual int32_t BeamScreenPosition(const float* pPointXYZ, float* pOutScreenXYZ) = 0;
	virtual int32_t BeamRenderableAdd(const float* pOriginMinsMaxs9) = 0;
	virtual void BeamRenderableChanged(int32_t nRenderable, const float* pOriginMinsMaxs9) = 0;
	virtual void BeamRenderableRemove(int32_t nRenderable) = 0;
	virtual int32_t BeamBindSprite(int32_t nModelIndex, int32_t nFrame, int32_t nRenderMode, int32_t bWireframe) = 0;
	virtual void BeamSetHdrColorScale(int32_t nModelIndex, int32_t nRenderMode, float flScale) = 0;
	virtual int32_t BeamGlow(int32_t nModelIndex, int32_t bBind) = 0;
	virtual void BeamDrawStrip(const float* pSegs, int32_t nSegs) = 0;
	virtual void BeamDrawMesh(int32_t nPrimitive, int32_t nPrimitives, int32_t nColorKind, const float* pVerts,
							  int32_t nVerts) = 0;
};

abstract_class IShimStandaloneClient
{
public:
	virtual void ActivateOccluder(int32_t nOccluderIndex, int32_t bActive) = 0;
	virtual void** ClientInterpBlocks(int32_t* pOutCount, float* pOutInterpAmount) = 0;
	virtual void** ClientPolicyBlocks(int32_t* pOutCount) = 0;
	virtual void DynamicLightRelease(int32_t nKey) = 0;
	virtual void DynamicLightUpdate(const SourceSharpDynamicLight* pLight) = 0;
	virtual int32_t StandaloneIsDormant(uint32_t hEntity) = 0;
	virtual void StandaloneSetBlending(uint32_t hEntity, int32_t bTranslucent, int32_t bIgnoreZ, int32_t nAlpha) = 0;
	virtual void StandaloneSetCustomDraw(uint32_t hEntity, const float* pMinsXYZ, const float* pMaxsXYZ) = 0;
	virtual void StandaloneSetFade(uint32_t hEntity, int32_t nStyle, float flStartDist, float flEndDist,
								   float flMinAlpha) = 0;
	virtual void StandaloneSetOverlayModel(uint32_t hEntity, int32_t nModelIndex) = 0;
	virtual void StandaloneSetViewModel(uint32_t hEntity, int32_t bEnable) = 0;
	virtual int32_t StandaloneShadowCast(uint32_t hEntity, int32_t* pOutChoice) = 0;
	virtual int32_t ClientRecvProp(int32_t iIndex, char* pOutClass, int32_t nClassBytes, char* pOutTable,
								   int32_t nTableBytes, char* pOutProp, int32_t nPropBytes, int32_t* pOutType,
								   int32_t* pOutFlags, int32_t* pOutOffset, int32_t* pOutElements) = 0;
	virtual int32_t ClientSchemaRefusal(int32_t iIndex, uint32_t* pOutTypeId, uint32_t* pOutExpected,
										uint32_t* pOutSeen, int32_t* pOutCount, char* pOutClass, int32_t nClassBytes,
										int32_t* pOutRefusedUpdates) = 0;
	virtual void* StandaloneEngineBlock(uint32_t hEntity, int32_t* pOutBytes) = 0;
	virtual void StandaloneStoreSequence(uint32_t hEntity, int32_t nSequence, float flPlaybackRate, float flAnimTime,
										 float flDuration, int32_t bLooping) = 0;
	virtual void StandaloneSetRagdoll(uint32_t hEntity, int32_t bRagdoll) = 0;
	virtual void EntitySetSkin(uint32_t hEntity, int32_t nSkin) = 0;
	virtual void RegisterAmmoType(const char* pName, int32_t nDamageType, int32_t nMaxCarry, int32_t nTracerType,
								  float flDamageForce, float flDamage) = 0;
	// Surrounding boxes (mins, maxs) of the shells the handles name; returns how many resolved.
	virtual int32_t StandaloneSurroundingBounds(const uint32_t* pHandles, int32_t nCount, float* pOutMinsMaxs) = 0;
	// A broken surface's draw: the brush material facts, then the plan managed made.
	virtual int32_t SurfaceMaterialFacts(uint32_t hEntity, char* pOutMaterial, int32_t nMaterialBytes, char* pOutCrack,
										 int32_t nCrackBytes, int32_t* pOutHasCrack) = 0;
	virtual void SurfaceSetMaterials(uint32_t hEntity, const char* pNames, int32_t nBytes, int32_t nFlags) = 0;
	virtual void SurfaceSetPlan(uint32_t hEntity, const int32_t* pWords, int32_t nWords) = 0;
	// The entities interpolation admitted, which PreRender re-transforms.
	virtual void ClientInterpAdmitted(const uint32_t* pHandles, int32_t nCount) = 0;
};

abstract_class IShimViewClient
{
public:
	virtual void ConNPrintf(void* pEngineClient, int32_t nPos, const char* pszText) = 0;
	virtual void ConNXPrintf(void* pEngineClient, int32_t nIndex, float flTimeToLive, const float* pColorRGB,
							 int32_t bFixedWidthFont, const char* pszText) = 0;
	virtual void GetLightForPoint(void* pEngineClient, const float* pPosXYZ, int32_t bClamp, float* pOutXYZ) = 0;
	virtual void GetLightForPointFast(void* pEngineClient, const float* pPosXYZ, int32_t bClamp, float* pOutXYZ) = 0;
	virtual void ClientSetWorldRenderState(int32_t nWhich, const float* pValues, int32_t nCount) = 0;
	virtual int32_t GetLocalPlayerOrigin(float* pOutXYZ) = 0;
	virtual void SetViewerFogParams(const SourceSharpFogParams* pParams) = 0;
	virtual void SetViewerSkyboxParams(const SourceSharpSkyboxParams* pParams) = 0;
	virtual int32_t ClientTraceLine(const float* pStartXYZ, const float* pEndXYZ, int32_t nMask, int32_t nFilter,
									int32_t nPassEntityIndex, float* pOutFraction, float* pOutEndXYZ,
									float* pOutNormalXYZ, int32_t* pOutHitbox, int32_t* pOutEntityIndex,
									int32_t* pOutSurfaceProps, int32_t* pOutSurfaceFlags) = 0;
	// The observer's view: a hull trace, an entity's drawn pose, and the view override
	// (0 hands the view back to the player's eye).
	virtual int32_t ClientTraceHull(const float* pStartXYZ, const float* pEndXYZ, const float* pMinsXYZ,
									const float* pMaxsXYZ, int32_t nMask, int32_t nFilter, int32_t nPassEntityIndex,
									float* pOutFraction, float* pOutEndXYZ) = 0;
	virtual int32_t ClientEntityRenderPose(int32_t nEntityIndex, float* pOutOriginXYZ, float* pOutAngles) = 0;
	virtual void ViewOverrideSet(int32_t nActive, const float* pOriginXYZ, const float* pAngles,
								 int32_t nSetViewAngles) = 0;
	virtual int32_t EntitiesUpdateVisibilityAll() = 0;
	// A second camera into a named render target, drawn at the client mode's PreRender.
	virtual int32_t RenderViewToTarget(int32_t nTarget, const SourceSharpViewTargetSetup* pSetup) = 0;
	virtual int32_t ViewTargetInfo(int32_t nTarget, int32_t* pOut4) = 0;
	// One render target slot's texture, group and FindTexture arguments.
	virtual int32_t RenderTargetDeclare(int32_t nSlot, const char* pszName, const char* pszGroup, int32_t bComplain,
										int32_t nCreationFlags, int32_t nReleaseOrder) = 0;
	virtual int32_t PixelVisQueryCreate() = 0;
	virtual void PixelVisQueryDestroy(int32_t nQuery) = 0;
	virtual void PixelVisQueryReset(int32_t nQuery) = 0;
	virtual void PixelVisQueryPixels(int32_t nVisible, int32_t nCount, int32_t* pOut) = 0;
	virtual void PixelVisDiameters(const float* pSpheres, int32_t nSpheres, float* pOut) = 0;
	virtual int32_t PixelVisView(float* pOut, int32_t nFloats) = 0;
	virtual int32_t GlowMaterialFind(const char* pszName, const char* pszGroup) = 0;
	// Colour-correction lookups; the handle table is managed's.
	virtual void* ColorLookupAdd(const char* pLookupFile) = 0;
	virtual void ColorLookupSetWeight(void* hLookup, float flWeight) = 0;
	virtual void ColorLookupRemove(void* hLookup) = 0;
	virtual void ColorLookupResetWeights() = 0;
	// Standalone shells are in no spatial partition, so this clips a ray against the
	// named entities (solid ones only) and answers the nearest hit.
	virtual int32_t ClientClipRayToEntities(
		const float* pStartXYZ, const float* pEndXYZ, int32_t nMask, const int32_t* pEntityIndices, int32_t nEntities,
		float* pOutFraction, float* pOutEndXYZ, float* pOutNormalXYZ, int32_t* pOutHitbox, int32_t* pOutEntityIndex,
		int32_t* pOutSurfaceProps, int32_t* pOutSurfaceFlags) = 0;
	// The same for a box: the nearest stop, the entity it stopped on and its origin.
	virtual int32_t ClientTraceHullEntities(
		const float* pStartXYZ, const float* pEndXYZ, const float* pMinsXYZ, const float* pMaxsXYZ, int32_t nMask,
		int32_t nPassEntityIndex, const int32_t* pEntityIndices, int32_t nEntities, float* pOutFraction,
		float* pOutEndXYZ, int32_t* pOutEntityIndex, float* pOutEntityOriginXYZ) = 0;
};

// User messages: managed reads the payload and decides; the rest carry out stock hooks.
abstract_class IShimUserMessagesClient
{
public:
	virtual int32_t ClientViewportQuery(const char* pPanel) = 0;
	virtual void ClientViewportShow(const char* pPanel, int32_t bShow, const unsigned char* pPairs, int32_t nPairBytes,
									int32_t nFlags) = 0;
};

// Sends a SourceSharp message to the server reliably; 1 when queued, 0 otherwise.
abstract_class IShimNetClient
{
public:
	virtual int32_t SendServerMessage(const char* pKey, const uint8_t* pData, int32_t nLen) = 0;
	virtual int32_t ClientNetFlow(int32_t nFlow, float* pOut, int32_t nOut) = 0;
	virtual int32_t ClientNetChannel(float* pOut, int32_t nOut) = 0;
	virtual int32_t ClientNetPacket(int32_t nFlow, int32_t nFrame, float* pOut, int32_t nOut) = 0;
};

// The client's diagnostics ledgers: not-shipped capabilities and refused methods.
abstract_class IShimDiagnosticsClient
{
public:
	virtual int32_t ClientNotShipped(int32_t iIndex, char* pOutName, int32_t nNameBytes, char* pOutReason,
									 int32_t nReasonBytes, int32_t* pOutCount, int32_t* pOutTotal) = 0;
	virtual int32_t ClientRefusal(int32_t iIndex, char* pOutMethod, int32_t nMethodBytes, int32_t* pOutCount,
								  int32_t* pOutTotal) = 0;
};

// UI draw primitives over vgui::ISurface. Colours are 0xRRGGBBAA, text is UTF-16, and
// draw slots refuse outside a paint pass.
abstract_class IShimUiClient
{
public:
	virtual void UiDrawFilledRect(int32_t x0, int32_t y0, int32_t x1, int32_t y1, uint32_t nRgba) = 0;
	virtual void UiDrawFilledRectFade(int32_t x0, int32_t y0, int32_t x1, int32_t y1, uint32_t nRgba, int32_t nAlpha0,
									  int32_t nAlpha1, int32_t bHorizontal) = 0;
	virtual void UiDrawOutlinedRect(int32_t x0, int32_t y0, int32_t x1, int32_t y1, uint32_t nRgba) = 0;
	virtual void UiDrawLine(int32_t x0, int32_t y0, int32_t x1, int32_t y1, uint32_t nRgba) = 0;
	virtual void UiDrawTexturedRect(int32_t nTexture, int32_t x0, int32_t y0, int32_t x1, int32_t y1, float s0,
									float t0, float s1, float t1, uint32_t nRgba) = 0;
	virtual void UiDrawPolygon(int32_t nTexture, const float* pXyuv, int32_t nVertices, uint32_t nRgba) = 0;
	virtual void UiDrawText(int32_t nFont, int32_t x, int32_t y, const uint16_t* pText, int32_t nLength,
							uint32_t nRgba) = 0;
	virtual void UiMeasureText(int32_t nFont, const uint16_t* pText, int32_t nLength, int32_t* pOutWidth,
							   int32_t* pOutHeight) = 0;
	virtual void UiSetClip(int32_t bEnabled, int32_t x0, int32_t y0, int32_t x1, int32_t y1) = 0;
	virtual int32_t UiTextureCreate() = 0;
	virtual void UiTextureUpload(int32_t nTexture, const uint8_t* pRgba, int32_t nWide, int32_t nTall) = 0;
	virtual int32_t UiFontAddFile(const char* pPath) = 0;
	virtual int32_t UiFontCreate(const char* pFamily, int32_t nTall, int32_t nWeight, int32_t nFlags) = 0;
	virtual void UiFontMetrics(int32_t nFont, int32_t* pOutTall, int32_t* pOutAscent) = 0;
	virtual int32_t UiFontAdvances(int32_t nFont, int32_t nFirst, int32_t nCount, int32_t* pOutAdvances) = 0;
	virtual void UiPlaySound(const char* pPath) = 0;
	virtual void UiSetMenuInput(int32_t bMouse, int32_t bKeyboard) = 0;
	virtual void UiSetCursor(int32_t nShape) = 0;
	virtual int32_t UiDebugOverlayText(int32_t nIndex, float* pOutX, float* pOutY, int32_t* pOutLine,
									   uint32_t* pOutRgba, uint8_t* pText, int32_t nTextBytes) = 0;
	virtual void UiDebugOverlayClearDead() = 0;
	virtual void ClientModeLayout() = 0;
};

// The executor for managed particle sims, one per (owner, channel), registered with
// CParticleMgr.
abstract_class IShimParticlesClient
{
public:
	virtual int32_t ParticlesCreate(uint32_t hOwner, int32_t nChannel, const char* pszMaterial, float flCullRadius,
									int32_t bAlwaysSimulate) = 0;
	virtual void ParticlesConfigure(uint32_t hOwner, int32_t nChannel, float flCullRadius) = 0;
	virtual void ParticlesDestroy(uint32_t hOwner, int32_t nChannel) = 0;
	virtual void ParticlesSetLights(uint32_t hOwner, int32_t nChannel, const float* pLights, int32_t nFloats) = 0;
	virtual int32_t ParticlesTraceLine(const float* pStartXYZ, const float* pEndXYZ, int32_t nMask, float* pOut,
									   int32_t nOut) = 0;
	virtual int32_t WorldTextMeshUpdate(uint32_t hOwner, const char* pszMaterial, const float* pBounds,
										int32_t nFloats) = 0;
	virtual void WorldTextMeshDestroy(uint32_t hOwner) = 0;
	// One started system by index; returns the count.
	virtual int32_t ParticleSystemsRead(int32_t nIndex, SourceSharpParticleSystemInfo_t* pOut, char* pszName,
										int32_t nNameMax) = 0;
	virtual int32_t ParticleDefinitionReads() = 0;
	// Particle query calls with no managed answer, per method; returns the count written.
	virtual int32_t ParticleQueryUnanswered(int32_t* pOut, int32_t nMax) = 0;
	// One integer material var of an executor's material: 1 and the value, or 0.
	virtual int32_t ParticlesMaterialVarInt(uint32_t hOwner, int32_t nChannel, const char* pszVar, int32_t* pValue) = 0;
	// An executor for an ownerless effect at a world point, keyed by (0xFFFFFFFF, nId).
	virtual int32_t ParticlesCreateAt(int32_t nId, const char* pszMaterial, const float* pOriginXYZ, float flCullRadius,
									  int32_t bAlwaysSimulate) = 0;
	virtual int32_t ParticleSystemNameFromIndex(int32_t nIndex, char* pszOut, int32_t nOutMax) = 0;
	virtual int32_t ParticleSystemOneShot(const char* pszName, const float* pSortOriginXYZ, const float* pPoints,
										  int32_t nPoints, const float* pOrientation0) = 0;
	// UTIL_TraceLine with MASK_SOLID_BRUSHONLY; out fraction, endpos xyz, normal xyz, ...
	virtual int32_t ParticlesWorldTrace(const float* pStartXYZ, const float* pEndXYZ, float* pOut, int32_t nOut) = 0;
	// The view being drawn: model-view rows (12 floats), view id, and origin/forward (6 floats).
	virtual void ParticlesView(float* pModelView12, int32_t* pViewId, float* pOriginForward6) = 0;
	// 1 unrestricted .. 0 fully throttled.
	// A line trace with the given contents mask; out fraction, endpos xyz, normal xyz,
	// startsolid, fractionleftsolid.
	virtual int32_t ParticlesTraceMask(const float* pStartXYZ, const float* pEndXYZ, uint32_t nMask, float* pOut,
									   int32_t nOut) = 0;
	// Flags: 1 attachment space, 2 view model FOV, 4 drawn before the view model.
	virtual int32_t ParticlesSetSpace(uint32_t hOwner, int32_t nChannel, uint32_t hEntity, int32_t nAttachment,
									  int32_t nFlags, SourceSharpParticleFollow_t* pFollow) = 0;
	// Contents at a point inside the owner brush entity's own brush.
	virtual int32_t ParticlesBrushPointContents(uint32_t hOwner, const float* pPointXYZ) = 0;
	// A bullet impact's particle system with its three oriented control points.
	virtual int32_t ParticleSystemImpact(const char* pszName, const float* pPoints, int32_t nEntity, float flScale,
										 const float* pColorXYZ) = 0;
};

// The client's effects: registrations run by walk index, and the engine reads they need.
abstract_class IShimFxClient
{
public:
	virtual int32_t EffectRegistrationCount() = 0;
	virtual int32_t EffectRegistrationName(int32_t iIndex, char* pOut, int32_t nOutSize) = 0;
	virtual void EffectRegistrationRun(int32_t iIndex, const SourceSharpEffectData_t* pData) = 0;
	virtual int32_t FxCanPredict() = 0;
	virtual int32_t FxRecipientCount(const float* pOriginXYZ) = 0;
	virtual int32_t FxMaterialFind(const char* pName) = 0;
	virtual void FxMaterialAddRef(int32_t nMaterial) = 0;
	virtual void FxMaterialRelease(int32_t nMaterial) = 0;
	virtual int32_t FxMaterialName(int32_t nMaterial, char* pOut, int32_t nOutSize) = 0;
	virtual void FxDrawQuads(int32_t nMaterial, int32_t nVertices, const float* pVertices, int32_t bByteColor) = 0;
	virtual void FxView(float* pOut) = 0;
	virtual void FxMainViewOrigin(float* pOutXYZ) = 0;
	virtual int32_t FxLocalPlayer() = 0;
	virtual void FxViewAngles(float* pOutPYR) = 0;
	virtual int32_t FxEntityAttachment(int32_t nEntIndex, int32_t nAttachment, float* pOutXYZ, char* pModelOut,
									   int32_t nModelSize) = 0;
	virtual void FxDispatchParticleEffect(int32_t nSystem, const float* pStartXYZ, const float* pEndXYZ,
										  const float* pAnglesPYR, int32_t nEntIndex) = 0;
	virtual int32_t FxEntityRenderAngles(int32_t nEntIndex, float* pOutPYR) = 0;
	virtual void FxEjectBrass(const float* pOriginXYZ, const float* pAnglesPYR, const float* pGunAnglesPYR,
							  int32_t nType) = 0;
	// Draws a sprite from a managed plan.
	virtual int32_t FxSpriteDrawBegin(uint32_t hOwner, const float* pOffsetXYZ, int32_t nModelIndex, float flFrame,
									  int32_t nRenderMode, int32_t nRenderFx, int32_t r, int32_t g, int32_t b,
									  int32_t a, float flScale, int32_t bWorldSpaceScale, float flHDRColorScale,
									  float flGlowProxySize, float* pIn) = 0;
	virtual int32_t FxSpriteDrawEnd(const float* pOut) = 0;
	virtual int32_t FxSpriteDrawStore(const float* pOut) = 0;
	virtual int32_t FxPrecacheCount() = 0;
	virtual void FxPrecacheCache(int32_t nIndex, int32_t bPrecache) = 0;
	virtual void FxCacheUsedMaterials() = 0;
	virtual void FxCacheMaterialHandles() = 0;
	virtual int32_t FxRenderableAttachment(uint32_t hEntity, int32_t nAttachment, float* pOut6) = 0;
	virtual int32_t FxHandleEntityIndex(uint32_t hEntity) = 0;
	virtual void FxElight(int32_t nKey, const float* pOriginXYZ, int32_t r, int32_t g, int32_t b, int32_t nExponent,
						  float flRadius, float flDecay, float flDie) = 0;
	virtual void FxVectorRandom(float flLo, float flHi, float* pOutXYZ) = 0;
	virtual int32_t FxMaterialsCached() = 0;
	virtual int32_t FxTempEntCreateNamed(int32_t nSlot, const float* pOriginXYZ, const char* pModel,
										 int32_t nPriority) = 0;
	virtual void FxLoadModel(const char* pModel) = 0;
	virtual int32_t FxModelFrameCount(const char* pModel) = 0;
};

// Executors for the soundscape decisions managed makes, and the listener's main view.
abstract_class IShimSoundscapeClient
{
public:
	virtual void SoundscapeSetRoomType(int32_t nRoomType) = 0;
	virtual void SoundscapeSetPlayerDsp(int32_t nDspType) = 0;
	virtual void SoundscapeMainView(float* pOut9) = 0;
	virtual int32_t SoundscapeMakingResLists() = 0;
	virtual void SoundscapeTouchSoundFile(const char* pszWave) = 0;
	virtual void SoundPatchEmit(int32_t hPatch, float flVolume01, float flPitch, int32_t nFlags, float flStartTime,
								int32_t bEmitCaption) = 0;
	virtual void SoundPatchStop(int32_t hPatch) = 0;
	virtual void SoundPatchState(int32_t hPatch, int32_t* pnEntIndex, int32_t* pbFilterActive) = 0;
	virtual void SoundPatchFree(int32_t hPatch) = 0;
	virtual int32_t SoundPatchCreate(uint32_t hEntity, int32_t nChannel, const char* pSoundName,
									 int32_t nSoundLevel) = 0;
};

// The engine reads the managed precipitation system needs.
abstract_class IShimPrecipitationClient
{
public:
	virtual int32_t PrecipitationTraceLine(const float* pStartXYZ, const float* pEndXYZ, int32_t nMask,
										   int32_t bIgnorePlayer, float* pOut) = 0;
	virtual int32_t PrecipitationTraceVolume(uint32_t hOwner, const float* pStartXYZ, const float* pEndXYZ,
											 const float* pHullMinXYZ, const float* pHullMaxXYZ, float* pOut) = 0;
	virtual int32_t PrecipitationPointContents(const float* pXYZ) = 0;
	virtual int32_t PrecipitationInPvs(uint32_t hOwner) = 0;
	virtual int32_t PrecipitationPlayer(float* pOut) = 0;
	virtual void PrecipitationWorldBounds(float* pOut6) = 0;
	virtual int32_t PrecipitationScreenWidth() = 0;
};

// Engine reads for managed material proxies, by proxy handle; native holds the pointers.
abstract_class IShimMaterialProxyClient
{
public:
	virtual int32_t ProxyFindVar(int32_t hProxy, const char* pszName, int32_t bComplain, int32_t* pFound) = 0;
	virtual int32_t ProxyFindTexture(int32_t hProxy, const char* pszName, const char* pszGroup, int32_t bComplain,
									 int32_t* pIsError) = 0;
	virtual int32_t ProxyVarTexture(int32_t hProxy, int32_t nSlot, char* pOutName, int32_t nOutSize) = 0;
	virtual int32_t ProxyBindEntity(int32_t hProxy, SourceSharpProxyEntity_t* pOut) = 0;
	virtual int32_t ProxyReadView(int32_t nKind, float* pOut, int32_t nFloats) = 0;
	virtual int32_t ProxyPlayerCustomFile(int32_t nPlayer, uint32_t* pCrc) = 0;
	virtual void ProxyNotShipped(const char* pszCapability, const char* pszReason) = 0;
};

// What managed input needs from the native client that the engine's vtables do not answer.
abstract_class IShimInputClient
{
public:
	virtual void InputCursorState(int32_t* pVisible, int32_t* pLocked) = 0;
	virtual void InputGetCursorPos(int32_t* pX, int32_t* pY) = 0;
	virtual void InputSetCursorPos(int32_t x, int32_t y) = 0;
	virtual void InputSetCursorArrow() = 0;
	virtual void InputVguiScreenSize(int32_t* pWide, int32_t* pTall) = 0;
	virtual void InputHudFacts(float* pSensitivity, int32_t* pKeyBits, float* pFovAdjust) = 0;
	virtual void InputStopPitchDrift() = 0;
	virtual void InputSetLocalViewAngles(const float* pAngles) = 0;
	virtual void InputViewerCameraModeChanged() = 0;
	virtual int32_t InputViewerAimIsAssisted() = 0;
	virtual void InputShowScoreboard(int32_t bShow) = 0;
	virtual void* InputMailbox() = 0;
};

// Surfaceprop impact audio, impact sound emit and splash effects for managed physics callbacks.
abstract_class IShimPhysicsClient
{
public:
	virtual int32_t PhysicsClientSurfaceAudio(int32_t nSurfaceProps, char* pHard, int32_t nHard, char* pSoft,
											  int32_t nSoft, float* pOut3) = 0;
	virtual void PhysicsClientCollideExtent(void* pObject, const float* pOrigin, const float* pAngles,
											const float* pDirection, float* pOut) = 0;
	virtual int32_t PhysicsClientPointContents(const float* pPoint) = 0;
	virtual void PhysicsClientSplashLighting(const float* pPoint, float* pOut4) = 0;
	virtual void PhysicsClientSplashEffect(int32_t nKind, const float* pOrigin, const float* pNormal, float flScale,
										   const float* pColor, float flLifetime, float flAlpha) = 0;
};

// ConVarsClientWatch forwards a convar's changes through ConVarChanged (-1: no such convar).
// ConVarsClientSetValue stores a value native reads; 0 for a name nothing reads.
abstract_class IShimConVarsClient
{
public:
	virtual int32_t ConVarsClientWatch(const char* pName) = 0;
	virtual int32_t ConVarsClientSetValue(const char* pName, float flValue) = 0;
};

// FlashlightTexture holds a texture by name and answers its id (-1: none).
abstract_class IShimFlashlightClient
{
public:
	virtual int32_t FlashlightTexture(const char* pszName) = 0;
	virtual int32_t FlashlightSupportsBorderColor() = 0;
};
