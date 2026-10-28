//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON c  static-init registration
// Debug builds only; NativeCommands declares the matching row under #if DEBUG.

#include "tier0/dbg.h"
#include "tier1/convar.h"
#include "shell_steporder.h"
#include "shell_gamedll_internal.h"
#include "native_command_body.h"

// The transmit set for one client on one frame. `arm` arms; no argument dumps,
// one ascending index per line.
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
        // Distinct from an empty set.
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
