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
// SERVER ONLY -- 6 slots.
//
// Served by the server's shim areas (docs/engineapi-areas.tsv), so the
// client has no body to declare.
//---------------------------------------------------------------------------
#ifndef CLIENT_DLL
void* SourceSharp_GetGlobalVarsPtr();
int32_t SourceSharp_MoveEffect(int32_t nKind, const float* pXYZ, float flValue, int32_t nValue);
void SourceSharp_MoveTrace(const float* pStartXYZ, const float* pEndXYZ, const float* pMinsXYZ, const float* pMaxsXYZ,
						   int32_t nMask, int32_t nCollisionGroup, SourceSharpMoveTraceResult* pOut);
void SourceSharp_RegisterDamageRules(const SourceSharpDamageRules* pRules);
void SourceSharp_RegisterGameRulesPolicy(const SourceSharpGameRulesPolicy* pPolicy);
void SourceSharp_RegisterMovementParams(const SourceSharpMovementParams* pParams);
#endif

//---------------------------------------------------------------------------
// CLIENT ONLY -- 6 slots.
//
// The fill wires these inside `#ifdef CLIENT_DLL`, so the server has no body
// to declare.
//---------------------------------------------------------------------------
#ifdef CLIENT_DLL
int32_t SourceSharp_ClientEntitiesInvalidateMdlCacheAll();
int32_t SourceSharp_ClientNotShipped(int32_t iIndex, char* pOutName, int32_t nNameBytes, char* pOutReason,
									 int32_t nReasonBytes, int32_t* pOutCount, int32_t* pOutTotal);
void SourceSharp_ConNPrintf(void* pEngineClient, int32_t nPos, const char* pszText);
void SourceSharp_ConNXPrintf(void* pEngineClient, int32_t nIndex, float flTimeToLive, const float* pColorRGB,
							 int32_t bFixedWidthFont, const char* pszText);
void SourceSharp_GetLightForPoint(void* pEngineClient, const float* pPosXYZ, int32_t bClamp, float* pOutXYZ);
void SourceSharp_GetLightForPointFast(void* pEngineClient, const float* pPosXYZ, int32_t bClamp, float* pOutXYZ);
#endif
