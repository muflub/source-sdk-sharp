// The server's managed runtime loader. Engine-free, so a gtest can compile it.

#pragma once

#include "sourcesharp_gamesystem.h"

// The counterpart of LoadManagedRuntime, shared by both sides.

// IManagedRuntimeServer is managed-built; read it from the holders, which every load and
// reload fills again. Never keep the pointer.
