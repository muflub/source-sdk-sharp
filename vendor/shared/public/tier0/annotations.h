//========= Copyright Valve Corporation, All rights reserved. ============//
#pragma once

#if _MSC_VER >= 1600 // VS 2010 and above.
// Upgrading important helpful warnings to errors
#pragma warning( error : 4789 ) // warning C4789: destination of memory copy is too small

// Suppress some code analysis warnings
#endif // _MSC_VER >= 1600 // VS 2010 and above.

#ifndef ANALYZE_SUPPRESS
#define ANALYZE_SUPPRESS( wnum )

#define PRINTF_FORMAT_STRING
#define SCANF_FORMAT_STRING
#define IN_Z

#define OUT_Z_CAP( x )
#define OUT_CAP( x )

#define OUT_Z_BYTECAP( x )

#define INOUT_Z_CAP( x )

#define OUT_Z_ARRAY
#define INOUT_Z_ARRAY

#endif
