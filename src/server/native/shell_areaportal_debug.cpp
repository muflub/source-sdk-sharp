//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — shell_areaportal.cpp's diagnostic console command ===//
// SS_REASON c  static-init registration
//
// DEBUG BUILDS ONLY (user ruling 2026-10-01: debug commands live in debug files
// that CMake compiles only for a debug build -- server_sourcesharp_debug_sources).
// SourceSharp.Server's NativeCommands declares the matching row under #if DEBUG.
//
//=============================================================================//

#include "tier0/dbg.h"
#include "areaportal_list.h"
#include "native_command_body.h"

//-----------------------------------------------------------------------------
// cs_sv_areaportals — the list, printed.
//
// INSTRUMENTATION, AND IT IS DEBT: recorded in the lane's status notes
// with a removal owner. It is here because the list is the ONE thing about an
// area portal that nothing else in the tree can see. The entity is
// ScriptedEntityKind.Logical, so it has no shell, no edict and entity index -1
// -- `debug-api ents func_areaportal` finds nothing by design -- and the record
// this file makes is not an entity at all. Between them there is no way to ask
// a running game "is the portal in the list, with the number vbsp gave it, and
// is the map's switch reaching it".
//
// That question is not decoration. A portal absent from this list is walked by
// nothing, so its bit is never set, so the client reads the area beyond it as
// closed and the room stops drawing -- and the symptom is a room that does not
// draw, at the far end of a chain with no error in it.
//
// One line per portal, greppable, with no verdict: what to make of the numbers
// is the caller's, exactly as cs_sv_trace_subject and cs_sv_handle_cross_check
// leave it.
//-----------------------------------------------------------------------------
SS_NATIVE_COMMAND( cs_sv_areaportals )
{
	Msg( "[SourceSharp] area portals: %d in the list\n", (int)g_AreaPortals.size() );

	// Numbered by position in the list (creation order), which is what the
	// element index was while nothing had been removed.
	unsigned nElement = 0;
	for ( const CAreaPortal *pPortal : g_AreaPortals )
	{
		const unsigned i = nElement++;
		if ( !pPortal )
		{
			Msg( "[SourceSharp]   element %u: NULL -- a freed record left in the list\n", i );
			continue;
		}

		Msg( "[SourceSharp]   element %u: number=%d version=%d open=%d window=%d "
		     "fade=%.0f token=%u\n",
		     i, pPortal->m_portalNumber, pPortal->m_iPortalVersion,
		     pPortal->m_bOpen ? 1 : 0, pPortal->m_bWindow ? 1 : 0,
		     pPortal->m_flFadeDist, AreaPortalTokenOf( pPortal ) );
	}
}
