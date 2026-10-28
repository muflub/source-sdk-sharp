//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The engine block: the fixed prefix of every entity's blob. Managed code writes
// it and the engine's frequent getters (collision bounds, render origin) read it
// at constant offsets. The layout is generated from SourceSharp.Abi/EngineBlock.cs;
// both tails exist on both sides because the join compares one fingerprint.

#pragma once

// Generated from the C# declaration; brings its own includes.
#include "engine_block_generated.h"
