//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — who keeps their close captions ======================//
//
// One recipient's worth of the rule inside
// RecipientFilter_RemoveIfNotCloseCaptioning (recipientfilter.cpp), lifted out
// so it can be asked without an engine.
//
// The rule reads as if it were "remove anyone whose closecaption convar is 0",
// and it is NOT. There are three cases and only two answers:
//
//   ""    the client has not reported the convar to the server yet  -> KEEP
//   "0"   the client has close captions off                         -> REMOVE
//   "1"   .. or anything else that parses non-zero                  -> KEEP
//
// The first two are the ones that matter, because atoi("") and atoi("0") are
// BOTH 0. An implementation that goes straight to atoi -- the obvious
// simplification, and the one anyone tidying this function would reach for --
// silently removes every client whose convar has not arrived yet, which on a
// listen server is every client for the first frames after they connect. The
// symptom is missing captions for some players some of the time and nothing in
// any log, so the empty-string check is load-bearing and is why this rule is a
// named function with tests rather than four lines inside a loop.
//
// plan_baseentity.md Phase 13: the containing function is one of the step 5
// gates. It removes NOBODY today -- the fifteenth C-cast site meant its
// per-recipient body was never reached -- so step 5 is the first moment its
// behaviour is observable at all, and the rule it will start applying that day
// is the one pinned here.
//
// Engine-free on purpose: it takes the convar's raw string, which is exactly
// what engine->GetClientConVarValue hands back, and nothing else.
//
//=============================================================================//
#ifndef CLOSECAPTION_RECIPIENT_H
#define CLOSECAPTION_RECIPIENT_H
#ifdef _WIN32
#pragma once
#endif

#include <stdlib.h>

//-----------------------------------------------------------------------------
// Should this recipient be dropped from a close-caption filter?
//
// pszConVarValue is the client's "closecaption" convar as the engine reports
// it. A NULL or empty string means the value is not known yet and the recipient
// is KEPT -- see the header comment; this is not the same as "0".
//-----------------------------------------------------------------------------
inline bool CloseCaption_ShouldRemoveRecipient( const char *pszConVarValue )
{
	// Not reported yet. Keeping them is the safe direction: a caption shown to
	// somebody who turned them off is a cosmetic annoyance, one withheld from
	// somebody who wants them is a missing subtitle.
	if ( !pszConVarValue || !pszConVarValue[ 0 ] )
		return false;

	return atoi( pszConVarValue ) == 0;
}

#endif // CLOSECAPTION_RECIPIENT_H
