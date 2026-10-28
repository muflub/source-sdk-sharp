//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON c  static-init registration

// Debug builds only.

#include "ss_world.h"

#include "native_command_body.h"

// The world's networked state now, which a runtime key write can change after
// the load-time report.
SS_NATIVE_COMMAND( cs_sv_world )
{
	CWorld *pWorld = GetWorldEntity();

	if ( pWorld == NULL )
	{
		Msg( "[SourceSharp] no world entity -- is a map loaded?\n" );
		return;
	}

	pWorld->ReportNetState();
}
