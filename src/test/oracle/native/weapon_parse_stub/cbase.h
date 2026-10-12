//========= SourceSharp ==================================================//
//
// The one-TU stand-in for "cbase.h" that the PRISTINE game/shared/
// weapon_parse.cpp and weapon_parse_default.cpp include first. The game's
// cbase.h is the whole precompiled world of one DLL (GAME_DLL or CLIENT_DLL);
// what the two files actually use from it is the containers, the two
// utility headers and weapon_parse.h below, which is what our fork's copy of
// weapon_parse.cpp includes in its place. Supplying it here keeps the subject byte-for-byte
// the upstream file, which is the point of the oracle.
//
//=============================================================================

#ifndef SOURCESHARP_TEST_WEAPONPARSE_CBASE_H
#define SOURCESHARP_TEST_WEAPONPARSE_CBASE_H

#include <utlvector.h>
#include "cdll_util.h"
#include <util_shared.h>
#include "weapon_parse.h"

#endif // SOURCESHARP_TEST_WEAPONPARSE_CBASE_H
