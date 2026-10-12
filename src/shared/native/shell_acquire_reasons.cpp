//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

//========= SourceSharp — the closed set of acquisition failure reasons =======//
// SS_REASON h  the CoreCLR host and bootstrap dispatch
//
// ONE SWITCH AND ONE COPY, IN A TRANSLATION UNIT WITH NO ENGINE IN IT.
//
// This is deliberately not part of shell_vtable_interfaces.cpp, and the reason
// is testability rather than tidiness. That file includes cbase.h and half the
// SDK; nothing that includes cbase.h can be compiled into a standalone gtest.
// The claim these literals carry — that the set is CLOSED, that every member is
// ASCII, and that none of them is a format string — is a claim a test has to be
// able to walk, and walking it needs the real function rather than a copy of
// the table written out again in the test.
//
// SO THE SET LIVES WHERE A TEST CAN REACH IT. sourcesharp/test/
// acquire_reason_test.cpp compiles THIS FILE and enumerates
// SourceSharpAcquireReason, which is what makes the guarantee complete rather
// than a sample: a fifth reason arriving with a "%s" in it fails that test on
// the day it lands.
//
//-----------------------------------------------------------------------------
// WHY NATIVE REPORTS THE REASON AND MANAGED COMPOSES THE MESSAGE.
//
// The contract says the error buffer only ever holds ASCII, and the way to make
// that true BY CONSTRUCTION rather than by discipline is for native never to
// echo anything the caller passed in. The one string that could carry a byte
// over 0x7F is the caller's own version string — so it is not interpolated
// here, and managed, which already has it, adds it to its own message.
//
// That is also why nothing here formats. There is no vsnprintf, no %s, and no
// buffer for one to overflow: a reason is a literal, and the only thing that
// happens to it is a bounded copy.
//
//=============================================================================//

#include "sourcesharp.h"
#include "shell_vtable_interfaces.h"

#include "tier1/strtools.h"
#include "ss_string.h"

//-----------------------------------------------------------------------------
// One reason's literal.
//
// A SWITCH RATHER THAN AN ARRAY INDEXED BY THE ENUM, on purpose: -Wswitch tells
// the compiler to say so when an enumerator is added and this function does not
// handle it, which an array subscript cannot do.
//-----------------------------------------------------------------------------
const char *SourceSharp_AcquireReason( SourceSharpAcquireReason eReason )
{
	switch ( eReason )
	{
	case SOURCESHARP_ACQUIRE_NOT_YET_AVAILABLE:
		return "factory not yet available";
	case SOURCESHARP_ACQUIRE_NO_SUCH_FACTORY:
		return "no factory of that kind on this side";
	case SOURCESHARP_ACQUIRE_UNKNOWN_INTERFACE:
		return "factory does not know that interface";
	case SOURCESHARP_ACQUIRE_UNKNOWN_FACTORY_TYPE:
		return "unknown factory type";
	}

	// Not reachable through the enum, and a literal rather than an assert
	// because this function's whole job is to hand back something safe to copy.
	return "unknown factory type";
}

//-----------------------------------------------------------------------------
// The reason, into the caller's buffer.
//
// IT TRUNCATES AND NEVER REFUSES, which is the contract and not an accident.
// An acquisition error is prose for a human: a truncated reason is still a
// reason, and refusing to report a failure because the report was long would be
// absurd. That is ListingBuffer's rule rather than NameBuffer's — NameBuffer
// refuses on overflow because half a path names a different file, which is not
// something a sentence can do.
//
// V_strncpy NUL-terminates within the size it is given (its own contract, and
// the difference between it and strncpy), so truncation is the copy's own
// behaviour and there is no helper to write here.
//
// A NULL or zero-length buffer is LEGAL and writes nothing: a diagnostic path
// may not want a reason at all.
//-----------------------------------------------------------------------------
void SourceSharp_SayAcquireReason( char *pOut, int32_t nBytes,
                                   SourceSharpAcquireReason eReason )
{
	if ( !pOut || nBytes <= 0 )
		return;

	ss::StrCopy( pOut, SourceSharp_AcquireReason( eReason ), nBytes );
}
