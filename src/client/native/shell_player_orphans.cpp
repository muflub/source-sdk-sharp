//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the last tenants of the client player cluster ======//
// SS_REASON d  a layout the engine reads at fixed offsets
//
// shell_client_orphans.cpp did this for evicted client files generally; this
// file is specifically what c_baseplayer.cpp and its kin still owed the rest
// of the client when the player cluster left the build (Phase 13c):
//
//   1. The prediction datamaps of CPlayerState and CPlayerLocalData, moved
//      VERBATIM. These two classes are not player-cluster classes any more —
//      the standalone player carries both (CPlayerState because the engine
//      reads it through GetPlayerState, CPlayerLocalData because the shared
//      movement loop compound-assigns its members) — but their datamaps, and
//      with them the classes' vtables, were emitted from c_baseplayer.cpp.
//
//   2. Stubs for the C_BasePlayer statics and methods that survive as call
//      sites in still-compiled code. Every one is either genuinely answerable
//      (GetLocalPlayer: there IS no C_BasePlayer local player — the local
//      player is a C_StandalonePlayer — so NULL is the truth, and it is what
//      this returned at runtime since the flip) or unreachable-by-construction
//      (the instance methods: with every constructor evicted, no C_BasePlayer
//      can ever exist for them to be called on; the neutral values match what
//      stock returns for an unremarkable player).
//
// Found by removing the files and reading the linker, which is also how
// entries come off this list.
//
//=============================================================================//

#include <stdio.h>
#include <stdlib.h>
#include "tier0/platform.h"
#include "tier0/dbg.h"
#include "tier1/strtools.h"
#include "vstdlib/random.h"
#include "utlvector.h"
#include "const.h"
#include "string_t.h"
#include "icvar.h"
#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#include "shareddefs.h"

// memdbgon must be the last include file in a .cpp file!!!

//-----------------------------------------------------------------------------
// (1) THE TWO PREDICTION DATAMAPS ARE GONE (plan_entity.md step 6f).
//
// CPlayerState and CPlayerLocalData carried DECLARE_PREDICTABLE, and this file
// held the BEGIN_PREDICTION_DATA_NO_BASE blocks that satisfied it — moved here
// verbatim when the player cluster left the build, because the two classes were
// members of C_StandalonePlayer and GetPredDescMap is each class's key function:
// without a definition somewhere, neither class had a vtable anywhere.
//
// NOTHING ON THIS SIDE HOLDS EITHER CLASS ANY MORE. The client player's state
// is the managed player's — one C# declaration (PlayerNetState), predicted by
// managed code, judged by PredictionJudge against the [Predicted] tolerances on
// that same declaration. C_StandalonePlayer is deleted and with it the last
// CPlayerState and CPlayerLocalData a client entity carried, so the vtables
// have no user and the datamaps have nothing to describe.
//
// THE DELETION IS CHECKED, not assumed: client_player_orphans_test asserts that
// neither BEGIN_PREDICTION_DATA_NO_BASE block is in this file and that no
// client translation unit declares a member of either class. A datamap quietly
// coming back would be a second declaration of predicted player state beside
// the C# one, which is the exact drift §3.10 removes.
//
//-----------------------------------------------------------------------------
// (2) THE STATICS ARE GONE. LocalPlayerInFirstPersonView went with its
// tool-framework caller; C_BasePlayer::GetLocalPlayer, which answered NULL
// (the local player is a C_StandalonePlayer, never a C_BasePlayer), went with
// its last importer, client_util_library.cpp's vision-flag family (lane M1-B,
// 2026-10-25: nm -u over client.so's link edge).
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// (2b) THE ONE INSTANCE METHOD AND THE ONE GLOBAL THAT ANYTHING STILL
// IMPORTS, AND NOTHING ELSE.
//
// TWENTY-THREE DEFINITIONS LEFT THIS FILE. It held twenty-six; the client link
// line imports three. Twenty-one of them were entries the linker named when
// the player cluster was evicted and has since stopped naming, as their callers
// were themselves evicted -- `GetViewModel`, `EyeVectors`,
// `EyePositionAndVectors`, `GetTimeBase`, `NotePredictionError`, the two
// previously-predicted-origin accessors, `UpdateButtonState`,
// `AudioStateIsUnderwater`, `InFirstPersonView`,
// `LocalPlayerInFirstPersonView`, `GetPlayerName`, `IsPlayerDead`,
// `GetUserID`, `ShouldDrawThisPlayer`, `ShouldDrawLocalPlayer`,
// `UsingStandardWeaponsInVehicle`, `RemoveFromPlayerSimulationList`,
// `GetFinalPredictedTime`, `C_BaseCombatCharacter::GetAmmoCount` and
// `UseHWMorphModels`. `IsLocalPlayer` followed when c_te_legacytempents.o, its
// last importer, left the client (C2 fx, 2026-10-05; tools/shim-orphan-reach),
// and `GetDefaultFOV` when shell_effects_sim.o went with the weather (C2 sims).
//
// THE FILE'S OWN RULE, APPLIED: "found by removing the files and reading the
// linker, which is also how entries come off this list." The reading was taken
// BEFORE the removal rather than after, object by object over all 253 client
// objects on the ninja graph -- `nm -C -u` against `nm -C --defined-only` with a
// symbol-type filter, so a weak inline emission is not counted as a definition
// of this file's. .lane4a/orphan_reach.py is the script; it refuses if any
// object on the graph is not on disk, because an unbuilt tree answers "nobody
// imports anything".
//
// EyePositionAndVectors CALLED EyeVectors, and both are gone together. A
// sibling call is the seventh way a symbol census has been wrong in this tree
// (util_shared_library's "130 dead lines" was 7), so the transitive set was
// taken rather than the flat one -- here they coincide, because the only
// internal edge ran between two symbols that are both dead.
//
// NOTHING HERE IS REACHED BY VTABLE EITHER, which is the fourth way. `nm -C`
// over the same 253 objects finds NO `vtable for C_BasePlayer`: every
// constructor left the build in 13c, so no instance exists and no virtual call
// can dispatch to one of these. A count of undefined references is a floor for
// virtual calls in general, and this is the check that says it is exact here.
//-----------------------------------------------------------------------------

// C_BasePlayer::GetFOVDistanceAdjustFactor stood here, read by cdll_util.o,
// which left client.so in C8 (lane c6-input) with no importer of its own.

// Read by view.o, and this one CAN genuinely be read: it is a global, not a
// call on an instance that cannot exist. No kill cam.
int g_nKillCamMode = OBS_MODE_NONE;
