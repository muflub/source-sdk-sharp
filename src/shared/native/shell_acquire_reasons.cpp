//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// SS_REASON h  the CoreCLR host and bootstrap dispatch
// The closed set of acquisition failure reasons, in a TU with no engine so a test can
// enumerate it. Native never echoes the caller's version string, so the error buffer
// only ever holds these ASCII literals.

#include "shell_vtable_interfaces.h"

#include "tier1/strtools.h"
#include "ss_string.h"

// A switch rather than an array so -Wswitch flags an unhandled enumerator.
const char* SourceSharp_AcquireReason(SourceSharpAcquireReason eReason)
{
	switch (eReason)
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

	return "unknown factory type";
}

// The reason into the caller's buffer, truncated to fit. A NULL or empty buffer writes nothing.
void SourceSharp_SayAcquireReason(char* pOut, int32_t nBytes, SourceSharpAcquireReason eReason)
{
	if (!pOut || nBytes <= 0)
		return;

	ss::StrCopy(pOut, SourceSharp_AcquireReason(eReason), nBytes);
}
