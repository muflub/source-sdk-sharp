// The server's managed runtime loader. Engine-free, so a gtest can compile it.

#pragma once

#include "sourcesharp_gamesystem.h"

// The counterpart of LoadManagedRuntime, shared by both sides.
void SourceSharp_UnloadManagedSide();

// IManagedRuntimeServer is managed-built; reach it through ManagedRuntimeServer(), which
// every load and reload acquires again. Never keep the pointer.
