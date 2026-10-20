//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the server's temp entity channel ====================//
//
// WHAT THIS REPLACES. `te.h` was one line — `#include "itempents.h"` — and every
// server-side effect reached the wire as `te->Something( filter, delay, ... )`,
// a virtual on the CTempEntsSystem singleton in te.cpp whose whole body was
// `if ( !SuppressTE( filter ) ) TE_Something( filter, delay, ... );`.
//
// Those TE_*() functions are not stock's any more. Every one of them was
// rewritten by a lane over shell_tempents.cpp's generic writer, against a table
// whose props and offsets come from a C# declaration
// (SourceSharp.Shared/TempEnts/*.cs) — so the managed table is already the last
// link of every chain that starts at a te-> call. plan_tmp.md Part 7 deletes the
// two hops in front of it, and this header is what the callers name instead.
//
// THE SUPPRESSION IS NOT DROPPED WITH THE CLASS. CTempEntsSystem::SuppressTE is
// the prediction filter: when a player is predicting an entity locally the
// server must not also send that player the entity's effects. It was applied on
// every te-> call and it is applied here, once, in SourceSharpTe_SuppressFilter
// — because a rewiring that quietly stopped culling would be a behaviour change
// with no symptom until two people played at once.
//
// It answers false for everybody in THIS build and that is measured rather than
// assumed: IPredictionSystem::m_pSuppressHost is only ever written by
// SuppressHostEvents / SetSuppressHost, physics_main.cpp:2193's writer is inside
// `#ifndef SOURCESHARP` (no CBaseEntity resolves to a CBasePlayer any more), and
// the only other writer left hands it NULL. recipientfilter.cpp:147 says the
// same thing about its own copy of this test. Carrying the code anyway is the
// difference between a capability that is inert and a capability that is gone.
//
//=============================================================================//
#ifndef SOURCESHARP_SHELL_TEMPENTS_H
#define SOURCESHARP_SHELL_TEMPENTS_H
#ifdef _WIN32
#pragma once
#endif

#include "irecipientfilter.h"
#include "mathlib/vector.h"

namespace ss { struct CEffectData; }

//-----------------------------------------------------------------------------
// CTempEntsSystem::SuppressTE, verbatim, in the one place the callers share.
//
// Returns true for "do not send at all": the suppress host has been dropped
// from the filter and nobody is left to send to.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// The generic writer, from shell_tempents.cpp. No per-TE knowledge on either
// side of this boundary: a handle, some named fields, and a filter.
//
// DECLARED ONCE, HERE. It was declared eight times, once at the top of each
// shell_te_fwd_*.cpp, because the eight files were written in parallel by lanes
// that could not edit a shared header without colliding. Those files are one
// file now and the declaration belongs where the rest of this side's
// temp-entity contract already is: a signature repeated eight times is eight
// places for it to drift out of step with the definition, and the compiler
// checks each copy only against the callers in its own translation unit.
//-----------------------------------------------------------------------------
bool SourceSharpTe_Forward( int nId, IRecipientFilter &filter, float flDelay,
                            const float *pFloats, int nFloats,
                            const int *pInts, int nInts );

// SourceSharpTe_PredictionSystem and ss::TePrediction went in
// plan_server_native_v2 T6: their one caller was the server's multi-damage
// apply, which nothing could reach.

// BEHAVIOUR LEDGER ROW S5 (TriggerTempEntity, stock's tool-build-only stub)
// has no server half any more. Its refusal, SourceSharpTe_ToolsRefused, was
// reached only through CServerTools::GetTempEntsSystem; that path left with
// entity_tools_server.cpp, and the server gc cull (2026-10-01) deleted the
// refusal with no caller left. The client's ITempEntsSystem still answers the
// row (shell_te_forwards_client.cpp).

//=============================================================================//
// The effects themselves.
//
// These are the signatures te.cpp declared at its top and forwarded to, carried
// across UNCHANGED — parameter order, pointer-ness, defaults and all. The
// bodies live in shell_te_forwards.cpp — ONE file now rather than the eight
// per-lane shell_te_fwd_*.cpp this list was written against — each writing a
// managed table through shell_tempents.cpp's generic writer.
//
// Only the fourteen this build still calls are declared (eighteen until the
// server gc cull took four nothing called). A TE_*() with no
// caller left is not listed, deliberately: the list is the reachable set, and a
// declaration for something nothing calls is how a dead effect keeps looking
// alive. It said nineteen until tools/shim-census over the debug link line at
// c5eab9deb read TE_PlayerDecal at zero importers: the declaration had outlived
// its only caller and was the exact shape this paragraph warns about.
//=============================================================================//

void TE_BloodStream( IRecipientFilter& filter, float delay,
	const Vector* org, const Vector* dir, int r, int g, int b, int a, int amount );

void TE_Decal( IRecipientFilter& filter, float delay,
	const Vector* pos, const Vector* start, int entity, int hitbox, int index );

void TE_DispatchEffect( IRecipientFilter& filter, float delay,
	const Vector &pos, const char *pName, const ss::CEffectData &data );

void TE_Smoke( IRecipientFilter& filter, float delay,
	const Vector* pos, int modelindex, float scale, int framerate );

void TE_Sparks( IRecipientFilter& filter, float delay,
	const Vector* pos, int nMagnitude, int nTrailLength, const Vector *pDir );

void TE_WorldDecal( IRecipientFilter& filter, float delay,
	const Vector* pos, int index );

#endif // SOURCESHARP_SHELL_TEMPENTS_H
