//========= Copyright Valve Corporation, All rights reserved. ============//
// Purpose:	All of our code is completely Unicode.  Instead of char, you should
//			use wchar, uint8, or char8, as explained below.

#pragma once

#ifdef _INC_TCHAR
#error ("Must include tier0 type headers before tchar.h")
#endif

// Temporarily turn off Valve defines
#include "tier0/valve_off.h"

#if !defined(_WCHAR_T_DEFINED) && !defined(GNUC)
typedef unsigned short wchar_t;
#define _WCHAR_T_DEFINED
#endif

// uint8
// uint8 is equivalent to byte (but is preferred over byte for clarity).  Use this
// whenever you mean a byte (for example, one byte of a network packet).
typedef unsigned char uint8;
typedef unsigned char BYTE;
typedef unsigned char byte;

//typedef char wchar;

// __WFILE__
// This is a Unicode version of __FILE__
#define WIDEN2(x) L ## x
#define WIDEN(x) WIDEN2(x)
#define __WFILE__ WIDEN(__FILE__)

#ifdef _WIN32
#include <tchar.h>
#else

#define _vsntprintf _vsnprintf

#define _T(s) s
#endif

#if defined(_UNICODE)
typedef wchar tchar;
#define tstring wstring
#define __TFILE__ __WFILE__
#define TCHAR_IS_WCHAR
#else
typedef char tchar;

#define __TFILE__ __FILE__

#endif

#if defined( _MSC_VER ) || defined( WIN32 )
typedef wchar_t uchar16;
typedef unsigned int uchar32;
#else
typedef unsigned short uchar16;
typedef wchar_t uchar32;
#endif

#ifdef FORCED_UNICODE
#undef _UNICODE
#endif

// Turn valve defines back on
#include "tier0/valve_on.h"
