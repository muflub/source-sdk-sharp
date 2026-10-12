//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — shell_gamedll.cpp's diagnostic console command ======//
// SS_REASON c  static-init registration
//
// DEBUG BUILDS ONLY (user ruling 2026-10-01: debug commands live in debug files
// that CMake compiles only for a debug build -- server_sourcesharp_debug_sources).
// SourceSharp.Server's NativeCommands declares the matching row under #if DEBUG.
//
// THE RECORDER IS NOT MOVED, only its dump. The capture seam (the three
// ShellGameDll_TransmitCapture* functions shell_gameents.cpp records through)
// stays in shell_gamedll.cpp in every build; this file reads the recorder
// through ShellGameDll_TransmitSet (shell_gamedll_internal.h).
//
//=============================================================================//

#include "tier0/dbg.h"
#include "tier1/convar.h"            // CCommand
#include "shell_steporder.h"
#include "shell_gamedll_internal.h"
#include "native_command_body.h"

//-----------------------------------------------------------------------------
// The transmit set for one client on one frame. `arm` arms; no argument dumps.
//
// The dump prints the indices ASCENDING and one per line rather than as a
// packed list, because the two consumers are a diff and a human, and a diff
// over a packed list reports one changed line for one changed edict in a way
// that names neither.
//-----------------------------------------------------------------------------
SS_NATIVE_COMMAND( cs_sv_transmitset )
{
    CShellTransmitSet &set = ShellGameDll_TransmitSet();

    if ( args.ArgC() > 1 )
    {
        // `arm` takes the next client; `arm N` waits for client edict N.
        const int nOnly = args.ArgC() > 2 ? atoi( args.Arg( 2 ) ) : -1;
        set.Arm( nOnly );
        Msg( "[SourceSharp] TRANSMITSET: armed %d\n", nOnly );
        return;
    }

    if ( !set.HaveCapture() )
    {
        // NOT an empty set. A client that received nothing and a capture that
        // never happened are the same six characters in a log if this line is
        // not here, and they are opposite findings.
        Msg( "[SourceSharp] TRANSMITSET: nocapture\n" );
        return;
    }

    Msg( "[SourceSharp] TRANSMITSET: recipient=%d count=%d skyarea=%d\n",
         set.Recipient(), set.Count(), set.SkyboxArea() );

    for ( int i = 0; i < CShellTransmitSet::kMaxEdicts; i++ )
    {
        if ( set.IsSet( i ) )
            Msg( "[SourceSharp] TRANSMITSET: %d\n", i );
    }

    Msg( "[SourceSharp] TRANSMITSET: end\n" );
}
