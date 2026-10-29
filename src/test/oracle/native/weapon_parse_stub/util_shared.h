//========= SourceSharp ==================================================//
//
// A `util_shared.h` FOR ONE TRANSLATION UNIT, so that
// weapon_script_oracle_test can compile the REAL game/shared/weapon_parse.cpp.
//
// Same shape and same reason as sourcesharp/test/map_entity_block_stub/cbase.h.
// weapon_parse.cpp opens with `#include <util_shared.h>`, and the real header
// pulls in the whole entity world -- IServerUnknown, staticpropmgr, the trace
// filters -- none of which FileWeaponInfo_t::Parse touches.
//
// Measured, not guessed: the ONLY name weapon_parse.cpp uses out of
// util_shared.h is UTIL_DecodeICE, called once in ReadEncryptedKVFile on the
// encrypted-script path. It is declared here and defined by the test.
//
// This shim is on the include path of exactly ONE build_and_run line in
// src/buildcpptests. It is not reachable from the game build, whose include
// paths come from the .vpc files and do not name this directory.
//
//=============================================================================

#pragma once

void UTIL_DecodeICE( unsigned char *buffer, int size, const unsigned char *key );
