//========= SourceSharp ==================================================//
//
// A `cbase.h` FOR ONE TRANSLATION UNIT, so that entitylist_slot0_test can
// compile the REAL game/shared/entitylist_base.cpp.
//
// entitylist_base.cpp opens with `#include "cbase.h"`, which in the game build
// is the client's or the server's precompiled prelude -- the whole SDK. The
// native test legs have no engine in them and are compiled with plain g++, so
// the real prelude is not available and is not wanted: what is under test here
// is the entity list's handle arithmetic, which has no engine in it either.
//
// So this directory goes on the include path AHEAD of src/, and the .cpp gets
// the four headers it actually uses. Measured, not guessed: with Assert
// compiled out by -DNDEBUG, the object file's only undefined symbol is
// `Warning`, which the test defines. Nothing else in the prelude is referenced.
//
// This shim is on the include path of exactly ONE build_and_run line in
// src/buildcpptests. It is not reachable from the game build, whose include
// paths come from the .vpc files and do not name this directory.
//
//=============================================================================

#pragma once

#include "tier0/platform.h"
#include "tier0/basetypes.h"
#include "tier0/dbg.h"
#include "const.h"
