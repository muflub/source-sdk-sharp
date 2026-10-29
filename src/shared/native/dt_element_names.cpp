//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON f  a bit-level wire codec

#include "dt_common.h"
#include "dt_utlvector_common.h"

// "000" to "999", then "1000" to "2047", written on first use so the table is .bss.
// A prop keeps the returned pointer, so a slot is never written twice.
static char s_DTElementNames[MAX_ARRAY_ELEMENTS][5]; // four digits and the terminator

const char* DT_ArrayElementNameForIdx(size_t i)
{
	Assert(i < MAX_ARRAY_ELEMENTS);
	char* pszName = s_DTElementNames[i];
	if (pszName[0] == 0)
	{
		// Written out rather than formatted: the server links no tier1.
		const int n = (int)i;
		char* p = pszName;
		if (n >= 1000)
			*p++ = (char)('0' + n / 1000);
		p[0] = (char)('0' + n / 100 % 10);
		p[1] = (char)('0' + n / 10 % 10);
		p[2] = (char)('0' + n % 10);
	}
	return pszName;
}
