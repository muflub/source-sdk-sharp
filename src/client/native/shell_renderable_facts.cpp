//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the render path's facts, answered ==================//
// SS_REASON a  a vtable the engine calls through
//
// The definitions behind renderable_facts.h. plan_entity.md step 7d.
//
// This file is the ONLY place step 7d's de-entitying lands a native body, and
// after lane 4c-render it holds ONE thing: the handle -> renderable resolve, in
// the SAME two-arm order the engine's own IClientEntityList uses, so the render
// path and the engine cannot disagree about which renderable a handle names.
// Its counters are the meter tools/7d-shadow-gate and clientshadowmgr.cpp's own
// report read.
//
// SIX OTHER BODIES USED TO BE HERE and renderable_facts.h says where each went:
// the FOV distance factor (exactly 1.0f on every path), the two bone-merge
// bounds arms, the shadow clip plane and the NPC/player gate (all `return
// false`), and the shadow bone-setup queue (an Add() with an empty body, so
// Run() issued two zero-length ParallelProcess calls per frame). Every one of
// them resolved a renderable back to an SDK C_BaseEntity, and this link line has
// no such class -- so none of them ever answered anything else in any build that
// ran. They are deleted, with their eleven stock call sites.
//
// Nothing here DECIDES anything the managed side could decide. Which arm
// answered is three lines at the resolve itself; it used to be
// RenderableSubject::ClassifyResolve in a header whose only remaining reason to
// exist was those three lines.
//
//=============================================================================//

#include "cbase.h"

#include "renderable_facts.h"
#include "slot_table.h"
#include "shell_standalone_client.h"
#include "cliententitylist.h"
#include "shell_cliententitylist.h"

// memdbgon must be the last include file in a .cpp file!!!
#include "tier0/memdbgon.h"

//-----------------------------------------------------------------------------
// The counters. File-static rather than a member of anything: there is exactly
// one client render path and an object holding four ints would be a class whose
// only reason to exist is to be a namespace.
//-----------------------------------------------------------------------------
static SourceSharpRenderableResolveStats_t s_ResolveStats = { 0, 0, 0, 0 };

const SourceSharpRenderableResolveStats_t &SourceSharp_RenderableResolveStats()
{
	return s_ResolveStats;
}

void SourceSharp_ResetRenderableResolveStats()
{
	s_ResolveStats.nAsks       = 0;
	s_ResolveStats.nFromTable  = 0;
	s_ResolveStats.nFromLegacy = 0;
	s_ResolveStats.nUnresolved = 0;
}

//-----------------------------------------------------------------------------
// THE RESOLVE.
//
// The order — slot table, then the legacy CClientEntityList — is not a
// preference, it is the order CSourceSharpClientEntityList::
// ClientUnknownFromHandle uses (shell_cliententitylist.cpp). The engine holds
// that object; the render path now asks the same question the same way round,
// so a handle can no longer name one renderable to the engine and another to
// the shadow manager.
//
// The renderable is taken off the IClientUnknown in both arms rather than off
// the shell type in one and the unknown in the other. GetClientRenderable() is
// IClientUnknown's own method: asking it of the shell would be the same call
// through a type this header is deliberately not allowed to name, and would
// diverge the day a shell answers something other than itself.
//
// WHICH ARM ANSWERED, INLINE. The four counters are what makes this seam
// falsifiable, and nUnresolved is the one that matters: a NULL for an INVALID
// handle is a caller asking about nothing, but a NULL for a VALID handle is a
// renderable the render path has lost, and a lost renderable draws nothing,
// crashes nothing and reports nothing. The two cases are counted separately
// here so that "nobody asked" can never be read as "everybody resolved".
//-----------------------------------------------------------------------------
IClientRenderable *SourceSharp_RenderableFromHandle( const CBaseHandle &hEnt )
{
	++s_ResolveStats.nAsks;

	IClientUnknown *pFromTable = NULL;

	if ( C_StandaloneEntity *pShell = SourceSharp_ClientShellFromHandle( hEnt ) )
		pFromTable = pShell->GetIClientUnknown();

	IClientUnknown *pFromLegacy = pFromTable ? NULL
	                                         : SourceSharp_LegacyListUnknownFromHandle( hEnt );

	if ( pFromTable != NULL )
		++s_ResolveStats.nFromTable;
	else if ( pFromLegacy != NULL )
		++s_ResolveStats.nFromLegacy;
	else if ( hEnt.IsValid() )
		++s_ResolveStats.nUnresolved;

	IClientUnknown *pUnknown = pFromTable ? pFromTable : pFromLegacy;

	return pUnknown ? pUnknown->GetClientRenderable() : NULL;
}

//-----------------------------------------------------------------------------
// THE METER FOR THE SEAM.
//
// nUnresolved is the number this exists for. A renderable the render path
// cannot find is not an error anywhere: the shadow manager's call sites either
// `return` or Assert (and Assert is compiled out of a release build), so the
// shadow simply is not drawn and nothing anywhere says so. This is the command
// tools/7d-shadow-gate reads.
//
// Explicit only — it prints nothing per frame and nothing at level transitions.
// The recorded reason is 3f-shadow's instrumentation, which wrote a file from
// inside the game on every teardown and had to be gated off at merge.
//-----------------------------------------------------------------------------
CON_COMMAND( cs_cl_renderable_report,
	"cs_cl_renderable_report — how the render path's handle->renderable resolves are answering" )
{
	const SourceSharpRenderableResolveStats_t &s = SourceSharp_RenderableResolveStats();

	Msg( "[SourceSharp] renderable resolve: asks %d  fromTable %d  fromLegacy %d  unresolved %d\n",
	     s.nAsks, s.nFromTable, s.nFromLegacy, s.nUnresolved );

	if ( s.nAsks == 0 )
	{
		Msg( "[SourceSharp] renderable resolve: NOBODY ASKED since the last reset -- this "
		     "report says nothing about the seam, only that no shadow was updated\n" );
	}

	SourceSharp_ResetRenderableResolveStats();
}
