//========= SourceSharp — shell_gamedll.cpp's transmit-set recorder ===========//
//
// What shell_gamedll_debug.cpp's cs_sv_transmitset needs from the game-DLL
// object, and nothing more: its Gate 5 recorder. The object (s_gameShellDll)
// stays file-scope in shell_gamedll.cpp, and the recording seam stays the three
// free functions shell_steporder.h declares -- shell_gameents.cpp records
// through those and must not include this header.
//
//=============================================================================//

#ifndef SOURCESHARP_SHELL_GAMEDLL_INTERNAL_H
#define SOURCESHARP_SHELL_GAMEDLL_INTERNAL_H
#ifdef _WIN32
#pragma once
#endif

class CShellTransmitSet;

// The game-DLL object's recorder. Non-const because `arm` writes.
CShellTransmitSet &ShellGameDll_TransmitSet();

#endif // SOURCESHARP_SHELL_GAMEDLL_INTERNAL_H
