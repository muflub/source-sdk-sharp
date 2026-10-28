//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — the SDK headers, as a forwarder library sees them =====//
//
// plan_engine_forwarders.md. Included FIRST by each forwarder library's
// translation units, before any interface header, and only there: it defines
// _fltused for the CRT-free Windows build.
//
// THE ASSERT MACHINERY IS SWITCHED OFF HERE. A forwarder library links no
// runtime (NO_RUNTIME): no tier0, no libc, no CRT. Its own code is one call per
// forwarder and asserts nothing, but the headers it includes for the classes
// carry inline code that does: mathlib/vector2d.h constructs two file-static
// constants (vec2_origin, vec2_invalid) whose constructor is
// `Assert( IsValid() )`, and in a debug build (_DEBUG turns DBGFLAG_ASSERT on)
// that assert calls into tier0, which this library cannot link. So the two
// macros every Assert form expands to are made empty for the rest of the
// translation unit: header code is compiled exactly as written otherwise, and
// nothing a forwarder calls changes, because a forwarder calls only virtual
// methods through the object it is handed.
//
// The constants' initialisers stay (a debug build runs them on Linux; Windows,
// with no entry point, never does). Neither touches anything outside the
// library, and no forwarder reads either constant.
//=============================================================================//
#ifndef SS_FWD_SDK_H
#define SS_FWD_SDK_H

#include "tier0/dbg.h"

#undef _AssertMsg
#undef _AssertMsgOnce
#define _AssertMsg( _exp, _msg, _executeExp, _bFatal ) do { } while ( 0 )
#define _AssertMsgOnce( _exp, _msg, _bFatal ) do { } while ( 0 )

#if defined( _MSC_VER ) && defined( SS_NO_CRT )
// MSVC references _fltused from an object that touches floating point (the
// SDK headers' inline math does), and the CRT defines it. A forwarder library
// links no CRT (NO_RUNTIME), so it defines it, as code built without a CRT
// does. A test that compiles a library's source into an executable with a CRT
// has no SS_NO_CRT and leaves it to the CRT. Every forwarder TU includes this
// header, so the definition is selectany: one survives the link.
extern "C" __declspec( selectany ) int _fltused = 0;
#endif

#endif // SS_FWD_SDK_H
