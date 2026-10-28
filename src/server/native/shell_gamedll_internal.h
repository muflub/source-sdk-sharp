// The game-DLL object's transmit-set recorder, for cs_sv_transmitset.

#pragma once

class CShellTransmitSet;

// Non-const because `arm` writes.
CShellTransmitSet &ShellGameDll_TransmitSet();
