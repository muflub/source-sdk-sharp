//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON f  a bit-level wire codec
// Entity-free utilities from game/shared/util_shared.cpp, for both DLLs; the
// declarations stay in util_shared.h.

#ifdef CLIENT_DLL

#include "predictable_entity.h"
#include "cdll_util.h"
#include "util_shared.h"
#include "vphysics_interface.h"
#include "client_entity.h"
#endif

#include "managed_convar_ref.h"
#ifdef CLIENT_DLL
#include "igamerules.h"
#else
#include "igamerules.h"
#endif
#ifdef CLIENT_DLL
#include "util_shared.h"
#endif

// The random stream, the particle-system index and the per-side
// temp-entity dispatch the effect emitters below use.

#ifdef CLIENT_DLL
#include "client_entity.h"
#include <vgui/ILocalize.h>
extern vgui::ILocalize *g_pVGuiLocalize;
#include "c_te_effect_dispatch.h"
// The slot table's index -> handle ask.
#include "shell_cliententitylist.h"
#else
#include "ss_effect_dispatch.h"
using ss::CEffectData;
#endif

#ifndef CLIENT_DLL
// The server half's replacements for cbase.h, util_shared.h and sharedInterface.h.
#include "ss_util.h"
#include "ss_shareddefs.h"
// shareddefs.h's names for the constants this file uses.
using ss::BLOOD_COLOR_RED;
#endif

#include "ss_string.h"
