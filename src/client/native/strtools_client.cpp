//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// SS_REASON x  NO LETTER FITS -- see docs/shim-reasons.md
// The strtools.h functions only client.so calls; the ones both DLLs call are in
// tier1_port.cpp. Each forwards to ss_string.h or the C library call tier1 made;
// where tier1 called Error, so do these.

#include "tier0/dbg.h"

#include "ss_string.h"

#include <wchar.h>

void _V_memcpy(const char*, int, void* dest, const void* src, int count)
{
	memcpy(dest, src, (size_t)count);
}

int _V_memcmp(const char*, int, const void* m1, const void* m2, int count)
{
	return memcmp(m1, m2, (size_t)count);
}

// CUtlVector's ShiftElements* call these two (V_memset in debug builds).
void _V_memset(const char*, int, void* dest, int fill, int count)
{
	memset(dest, fill, count);
}

void _V_memmove(const char*, int, void* dest, const void* src, int count)
{
	memmove(dest, src, count);
}

int _V_strlen(const char*, int, const char* str)
{
	return (int)strlen(str);
}

char* _V_strstr(const char*, int, const char* s1, const char* search)
{
	return const_cast<char*>(strstr(s1, search));
}

int _V_strcmp(const char*, int, const char* s1, const char* s2)
{
	return strcmp(s1, s2);
}

// strncpy's semantics: the rest is zero-filled, then the last byte is always
// the terminator. Not strncpy itself: inlined into a caller (a unity batch), gcc
// calls a strncpy it can see truncate -Wstringop-truncation, whatever follows it.
void V_strncpy(char* pDest, const char* pSrc, int maxLen)
{
	if (maxLen <= 0)
		return;
	const size_t nCopy = strnlen(pSrc, (size_t)maxLen - 1);
	memcpy(pDest, pSrc, nCopy);
	memset(pDest + nCopy, 0, (size_t)maxLen - nCopy);
}

// ASCII case folding, with V_stricmp's sign.
int V_stricmp(const char* s1, const char* s2)
{
	return ss::StrICmp(s1, s2);
}

const char* V_stristr(const char* pStr, const char* pSearch)
{
	return ss::StrIStr(pStr, pSearch);
}

int V_vsnprintf(char* pDest, int maxLen, const char* pFormat, va_list params)
{
	return ss::FormatV(pDest, maxLen, pFormat, params);
}

int V_snprintf(char* pDest, int maxLen, const char* pFormat, ...)
{
	va_list params;
	va_start(params, pFormat);
	const int n = ss::FormatV(pDest, maxLen, pFormat, params);
	va_end(params);
	return n;
}

void V_FileBase(const char* in, char* out, int maxlen)
{
	ss::FileBase(in, out, maxlen);
}
