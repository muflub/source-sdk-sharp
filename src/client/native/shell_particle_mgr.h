//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
// CParticleMgr's remaining mechanics as free functions: the PMaterial table, the
// old-style draw state, and the particle library's setup.

#pragma once

#include "particlemgr.h"
#include "materialsystem/imaterialvar.h"
#include "cdll_int.h"
#include "particledraw.h"
#include "view.h"
#include "voice_status.h"

class IMaterialSystem;

// The library's manager with the client's particle query and built-in operators,
// and the particle manifests read.
bool ShellParticleMgr_Init( IMaterialSystem *pMaterials );

// Releases the table's materials, uncaches the library's systems and the
// material system's unused materials.
void ShellParticleMgr_Term();

void ShellParticleMgr_LevelInit();

// Releases every held material; handles stay valid and re-acquire on next use.
void ShellParticleMgr_LevelShutdown();

// The handle for a material by name, or NULL when the material system has none.
PMaterialHandle ShellParticleMaterial_Find( const char *pszName );

// The handle's material, re-acquired if a level shutdown released it.
IMaterial *ShellParticleMaterial_Material( PMaterialHandle hMaterial );

// The draw state: the model-view matrix of the draw in progress, the light the
// particle light proxy reads, and the argument particle binds carry.
VMatrix &ShellParticleDraw_ModelView();
void ShellParticleDraw_GetLight( CParticleLightInfo &info );
void ShellParticleDraw_SetLight( const CParticleLightInfo &info );
void *ShellParticleDraw_BindArg();
