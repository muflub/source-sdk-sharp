//========= SourceSharp — shell_gamedll.cpp's transmit-set recorder ===========//
//
// What shell_gamedll_debug.cpp's cs_sv_transmitset needs from the game-DLL
// object, and nothing more: its Gate 5 recorder. The object (s_gameShellDll)
// stays file-scope in shell_gamedll.cpp, and the recording seam stays the three
// free functions shell_steporder.h declares -- shell_gameents.cpp records
// through those and must not include this header.
//
//=============================================================================//

#pragma once

class CShellTransmitSet;

// The game-DLL object's recorder. Non-const because `arm` writes.
CShellTransmitSet &ShellGameDll_TransmitSet();
