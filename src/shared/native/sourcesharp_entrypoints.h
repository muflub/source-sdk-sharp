//========= SourceSharp — the shim's entry points, GENERATED =================//
//
// DO NOT EDIT. Written by tools/entrypoint-decls from
//
//     src/sourcesharp/host/sourcesharp.h                   the signatures
//     src/sourcesharp/game/shared/sourcesharp_gamesystem.cpp   which, and which side
//
// One prototype per engine-API slot whose body is in some other shim file.
// These were 483 hand-written lines at the top of sourcesharp_gamesystem.cpp,
// each a second spelling of an argument list SourceSharpEngineApi already
// declares -- so `int32_t` where the field said `int`, or a dropped `const`,
// was a difference no compiler and no reviewer would see. They are derived now.
//
// WHERE THE PROSE WENT. Each block carried a paragraph naming the file the body
// lives in and why it is there. That is in docs/engine-boundary.md, keyed by
// slot name, and this file carries the one fact a generator can keep honest:
// the signature, and the side.
//
// TO REGENERATE:  tools/entrypoint-decls --write
// TO CHECK:       tools/entrypoint-decls        (exit 1 on drift)
//
//=============================================================================//

#pragma once

// sourcesharp.h is what these signatures are derived FROM, so it has to be
// included before them: every argument type below is one of its typedefs or a
// plain C type.
#include "sourcesharp.h"

//---------------------------------------------------------------------------
// BOTH SIDES -- 3 slots.
//
// A body on the server and a body on the client, or one shared body compiled
// into each.
//---------------------------------------------------------------------------
float SourceSharp_CurrentGravity();
void* SourceSharp_GetGlobalVarsPtr();
void SourceSharp_RegisterMovementParams(const SourceSharpMovementParams* pParams);

//---------------------------------------------------------------------------
// SERVER ONLY -- 4 slots.
//
// Served by the server's shim areas (docs/engineapi-areas.tsv), so the
// client has no body to declare.
//---------------------------------------------------------------------------
#ifndef CLIENT_DLL
int32_t SourceSharp_MoveEffect(int32_t nKind, const float* pXYZ, float flValue, int32_t nValue);
void SourceSharp_MoveTrace(const float* pStartXYZ, const float* pEndXYZ, const float* pMinsXYZ, const float* pMaxsXYZ,
						   int32_t nMask, int32_t nCollisionGroup, SourceSharpMoveTraceResult* pOut);
void SourceSharp_RegisterDamageRules(const SourceSharpDamageRules* pRules);
void SourceSharp_RegisterGameRulesPolicy(const SourceSharpGameRulesPolicy* pPolicy);
#endif

//---------------------------------------------------------------------------
// CLIENT ONLY -- 46 slots.
//
// The fill wires these inside `#ifdef CLIENT_DLL`, so the server has no body
// to declare.
//---------------------------------------------------------------------------
#ifdef CLIENT_DLL
void SourceSharp_ClientComputeLighting(const float* pPosXYZ, float* pOutColorRGB, float* pOutBoxRGB18);
int32_t SourceSharp_ClientDecalNameFromIndex(int32_t nDecalIndex, char* pOut, int32_t nOutSize);
void SourceSharp_ClientEnergySplash(const float* pPosXYZ, const float* pDirXYZ, int32_t bExplosive);
int32_t SourceSharp_ClientEntitiesInvalidateMdlCacheAll();
void SourceSharp_ClientImpactLedger(int32_t nFlecks, int32_t nDust);
void SourceSharp_ClientMetalSparks(const float* pPositionXYZ, const float* pDirectionXYZ);
void SourceSharp_ClientModeLayout();
void SourceSharp_ClientMuzzleFlash(const float* pOriginXYZ, const float* pAnglesPYR, float flScale, int32_t nType);
int32_t SourceSharp_ClientNotShipped(int32_t iIndex, char* pOutName, int32_t nNameBytes, char* pOutReason,
									 int32_t nReasonBytes, int32_t* pOutCount, int32_t* pOutTotal);
void SourceSharp_ClientPlayerDecalShoot(const float* pPositionXYZ, int32_t nPlayer, int32_t nEntity);
int32_t SourceSharp_ClientRefusal(int32_t iIndex, char* pOutMethod, int32_t nMethodBytes, int32_t* pOutCount,
								  int32_t* pOutTotal);
void SourceSharp_ClientRicochet(const float* pPositionXYZ, const float* pDirectionXYZ);
void SourceSharp_ClientSetWorldRenderState(int32_t nWhich, const float* pValues, int32_t nCount);
void SourceSharp_ClientSmoke(const float* pOriginXYZ, int32_t nModelIndex, float flScale, float flFrameRate);
void SourceSharp_ClientSparks(const float* pPositionXYZ, int32_t nMagnitude, int32_t nTrailLength,
							  const float* pDirectionXYZ);
float SourceSharp_ClientTraceHullWorld(const float* pStartXYZ, const float* pEndXYZ, const float* pMinsXYZ,
									   const float* pMaxsXYZ, int32_t nMask, int32_t* pOutSolidFlags,
									   float* pOutPlaneXYZD);
void* SourceSharp_ColorLookupAdd(const char* pLookupFile);
void SourceSharp_ColorLookupRemove(void* hLookup);
void SourceSharp_ColorLookupResetWeights();
void SourceSharp_ColorLookupSetWeight(void* hLookup, float flWeight);
void SourceSharp_ConNPrintf(void* pEngineClient, int32_t nPos, const char* pszText);
void SourceSharp_ConNXPrintf(void* pEngineClient, int32_t nIndex, float flTimeToLive, const float* pColorRGB,
							 int32_t bFixedWidthFont, const char* pszText);
void SourceSharp_EffectDispatchName(int32_t nIndex, char* pOut, int32_t nOutBytes);
int32_t SourceSharp_EffectRegistrationCount();
int32_t SourceSharp_EffectRegistrationName(int32_t iIndex, char* pOut, int32_t nOutSize);
int32_t SourceSharp_FlashlightSupportsBorderColor();
int32_t SourceSharp_FxRecipientCount(const float* pOriginXYZ);
void SourceSharp_GetLightForPoint(void* pEngineClient, const float* pPosXYZ, int32_t bClamp, float* pOutXYZ);
void SourceSharp_GetLightForPointFast(void* pEngineClient, const float* pPosXYZ, int32_t bClamp, float* pOutXYZ);
int32_t SourceSharp_GetLocalPlayerIndex();
void SourceSharp_HudLocalise(const char* pToken, char* pOut, int32_t nOutSize);
void SourceSharp_HudLookupBinding(const char* pBinding, char* pOut, int32_t nOutSize);
int32_t SourceSharp_HudTextMessageGet(const char* pName, SourceSharpTextMessage* pOut);
int32_t SourceSharp_NetHierarchyDeclare(int32_t nBlobBytes);
int32_t SourceSharp_NetPublishType(const char* pClassName, int32_t nShell, uint32_t nSchemaHash, int32_t nLayoutBytes,
								   uint32_t* pOutTypeId);
int32_t SourceSharp_NetTierInfo(int32_t nShell, int32_t iTier, const char** ppOutTable, int32_t* pOutBytes,
								int32_t* pOutWords, int32_t* pOutProps, int32_t* pOutLive);
int32_t SourceSharp_ParticleSystemNameFromIndex(int32_t nIndex, char* pszOut, int32_t nOutMax);
int32_t SourceSharp_ParticlesTraceLine(const float* pStartXYZ, const float* pEndXYZ, int32_t nMask, float* pOut,
									   int32_t nOut);
int32_t SourceSharp_ParticlesTraceMask(const float* pStartXYZ, const float* pEndXYZ, uint32_t nMask, float* pOut,
									   int32_t nOut);
int32_t SourceSharp_ParticlesWorldTrace(const float* pStartXYZ, const float* pEndXYZ, float* pOut, int32_t nOut);
int32_t SourceSharp_RenderTargetDeclare(int32_t nSlot, const char* pszName, const char* pszGroup, int32_t bComplain,
										int32_t nCreationFlags, int32_t nReleaseOrder);
void SourceSharp_RopeDraw(int32_t nMaterial, const float* pSegs, int32_t nSegs, const float* pSolid, int32_t nSolid,
						  int32_t nFlags);
int32_t SourceSharp_RopeMaterial(const char* pszName, int32_t* pOutTextureHeight, int32_t* pOutHasBack);
void SourceSharp_UiDebugOverlayClearDead();
int32_t SourceSharp_UiDebugOverlayText(int32_t nIndex, float* pOutX, float* pOutY, int32_t* pOutLine,
									   uint32_t* pOutRgba, uint8_t* pText, int32_t nTextBytes);
int32_t SourceSharp_WorldNetDeclare(int32_t nBlobBytes);
#endif
