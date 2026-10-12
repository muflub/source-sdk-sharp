//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp ======================================================//
//
// WHO MAY ent_fire, decided in slots rather than in pointers.
//
// ent_fire's two access rules were both written as CBasePlayer comparisons:
//
//     CBasePlayer *pPlayer = ToBasePlayer( UTIL_GetCommandClient() );
//     if ( !pPlayer ) return;                             // (1) silent
//     ...
//     if ( pPlayer != UTIL_GetListenServerHost() ) return; // (2) NULL == NULL
//
// Under SourceSharp the player is not a CBasePlayer, so (1) returned on every
// invocation and ent_fire accepted every word and queued nothing, with no
// message at all — an hour of one agent's time went into believing entities were
// ignoring inputs that had never been sent. And (2) is worse than dead: with
// neither side a CBasePlayer it compares NULL with NULL, so the check PASSES,
// and an access rule that passes by accident is not an access rule.
//
// A SLOT is a fact the engine states — UTIL_GetCommandClientIndex, and the host
// is always slot 1 (UTIL_GetListenServerHost is literally UTIL_PlayerByIndex(1)).
// The class of the object sitting in that slot is not, and never was, what
// either rule was about. This is the same correction the recipient filters
// needed, where every add funnelled through UTIL_PlayerByIndex and every filter
// came out empty.
//
// Engine-free on purpose: it is pure arithmetic over three facts, so it can be
// tested without a running game (sourcesharp/test/ent_fire_access_test.cpp).
// The stock build gets exactly the rules it had, because the slots it compares
// are the ones its pointers stood for.
//
//=============================================================================//
#ifndef ENT_FIRE_ACCESS_H
#define ENT_FIRE_ACCESS_H
#ifdef _WIN32
#pragma once
#endif

//-----------------------------------------------------------------------------
// Nobody: the slot number that means no client issued the command.
//-----------------------------------------------------------------------------
#define ENT_FIRE_NO_ISSUER 0

//-----------------------------------------------------------------------------
// Which slot the command should be attributed to.
//
// A client console names its own slot. The SERVER console names nobody — and on
// a LISTEN server that console belongs to the host, who is slot 1 and is allowed
// to fire. engine->ServerCommand is the console every tool drives the game
// through, this mod's development API included, so refusing it is a second way
// for ent_fire to look dead to everything that is not a human at a keyboard.
//
// A dedicated server console is deliberately NOT given the host, because there
// is no host there and the point_servercommand escalation ent_fire guards
// against is exactly the dedicated-server case.
//-----------------------------------------------------------------------------
inline int EntFire_IssuerSlot( int nCommandClientSlot, bool bDedicatedServer )
{
	if ( nCommandClientSlot > 0 )
		return nCommandClientSlot;

	return bDedicatedServer ? ENT_FIRE_NO_ISSUER : 1;
}

//-----------------------------------------------------------------------------
// May that slot fire, given the server it is on?
//
// bAutoKickDisabled is the dedicated server's own test and can only be answered
// by a CBasePlayer, so a caller with no CBasePlayer passes false — which refuses
// rather than admits. That direction is the whole point: an unanswerable access
// question is a NO.
//-----------------------------------------------------------------------------
inline bool EntFire_SlotMayFire( int nIssuerSlot, bool bDedicatedServer,
                                 int nMaxClients, bool bAutoKickDisabled )
{
	if ( nIssuerSlot <= ENT_FIRE_NO_ISSUER )
		return false;

	if ( bDedicatedServer )
		return bAutoKickDisabled;

	// Single player: whoever is here is the host by definition.
	if ( nMaxClients <= 1 )
		return true;

	// A listen server with room for company: only the host.
	return nIssuerSlot == 1;
}

#endif // ENT_FIRE_ACCESS_H
