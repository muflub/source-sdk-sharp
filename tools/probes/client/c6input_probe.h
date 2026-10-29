//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// tools/probes/client/c6input_probe.h -- lane c6-input's probe hooks, for the
// probe copies of in_main.cpp, in_camera.cpp and in_steamcontroller.cpp
// (probe_copies.cmake). The bodies are in c6input_probes_debug.cpp. Every
// call is a no-op until cs_cl_probe_input arms the recording.

#pragma once

struct kbutton_t;

// One fixture line (a '\n' is added), while armed.
void C6P_Log( const char *pFmt, ... ) __attribute__( ( format( printf, 1, 2 ) ) );
bool C6P_Armed();

// A kbutton_t's name for the "K" lines; the two TUs that own buttons register
// theirs once.
void C6P_NameButton( const kbutton_t *pButton, const char *pName );
const char *C6P_ButtonName( const kbutton_t *pButton );

// The scripted inputs for the next command: called at the top of
// CInput::CreateMove, before anything there reads a device.
void C6P_BeforeCreateMove( int nSequence );
void C6P_BeforeExtraMouseSample();
