//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// Registration of the classnames the standalone hierarchy hosts. Op routing is
// shell_dispatch.cpp.
#pragma once

#include "shell_standalone.h"

// Registers a classname on the standalone hierarchy. False when the name could not be
// taken, which the caller reports.
bool SourceSharp_InstallStandaloneClass( const char *pClassName, const char **ppOutputNames, int nOutputCount,
										 bool bServerOnly, SolidType_t nSolidType, int nSolidFlags,
										 bool bTouchEveryTick, bool bPlayer );

// Stores the override mask (bit per EntityOp_t) on a registered class; separate from
// install because a reload can change it alone. False for a name never registered.
bool SourceSharp_SetStandaloneClassOps( const char *pClassName, uint32_t nOpMask );

// The next entity of a classname after pStartAfter. NULL both when there is none and
// when pStartAfter has been removed.
CStandaloneEntity *StandaloneEntities_FindByClassname( CStandaloneEntity *pStartAfter, const char *pClassName );

// The factory dictionary, enumerated; its interface cannot list its contents.

// Installed classnames in install order; NULL past the end.
int SourceSharp_EntityFactoryCount();
const char *SourceSharp_EntityFactoryNameAt( int i );

// Classnames asked for that no factory answers, and how often. Kept for the process,
// not reset per level.
int SourceSharp_EntityFactoryRefusalCount();
const char *SourceSharp_EntityFactoryRefusalNameAt( int i );
int SourceSharp_EntityFactoryRefusalTimesAt( int i );

// The i'th factory's entity size, 0 past the end.
size_t SourceSharp_EntityFactorySizeAt( int i );
