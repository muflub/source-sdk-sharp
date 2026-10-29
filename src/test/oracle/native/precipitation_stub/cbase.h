//========= SourceSharp ==================================================//
//
// The one-TU stand-in for the client DLL's "cbase.h" that the PRISTINE
// game/client/c_effects.cpp includes first: the whole client world, of which
// the file uses an entity, a player, the traces, the particle manager's simple
// emitter and the renderer. All of it is declared in precip_harness.h; see its
// header for what is stock and what is stand-in.
//
//=============================================================================
#pragma once
#include "precip_harness.h"
