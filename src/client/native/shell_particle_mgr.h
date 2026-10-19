//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//========= SourceSharp — what was left of CParticleMgr: its mechanics ======//
//
// C7 particles phase 4. particlemgr.cpp's CParticleMgr held the old-style and
// new-style effects' lists and decided their frames; phases 2.3 and 3 moved
// those decisions to managed (ClientParticleManager, NewParticleManager) and
// the lists to the shells (shell_managed_particles.cpp's executors,
// shell_new_particles_client.cpp's effects). What the client still called of
// it is here, as free functions over this file's own state, and nothing of it
// decides:
//
//   the PMaterial table   GetPMaterial / PMaterialToIMaterial / RepairPMaterial
//                         / FindOrAddSubTextureGroup: a material by name, held
//                         with its texture page and texcoords, released at
//                         level shutdown and re-acquired by its handle after
//   the draw state        the model-view matrix and the directional light the
//                         old-style draw sets and the particle light proxy
//                         reads, and the argument particle binds carry (stock:
//                         the manager; ParticleProxies.cs tests it for NULL)
//   the library's setup   Init, Term and LevelInit's calls into the particle
//                         library (libss_particles.so)
//
// CParticleMgr itself (particlemgr.h) is no longer defined in client.so.
//=============================================================================//

#ifndef SHELL_PARTICLE_MGR_H
#define SHELL_PARTICLE_MGR_H
#ifdef _WIN32
#pragma once
#endif

#include "particlemgr.h"

class IMaterialSystem;

// CParticleMgr::Init: the library's manager with the client's particle query
// and its built-in operators, and the particle manifests read (sheets loaded).
bool ShellParticleMgr_Init( IMaterialSystem *pMaterials );

// CParticleMgr::Term: the table's materials released, the library's systems
// uncached and the material system's unused materials with them.
void ShellParticleMgr_Term();

// CParticleMgr::LevelInit: the library's clock at the level's start.
void ShellParticleMgr_LevelInit();

// CParticleMgr::RemoveAllEffects' material half: every held material released
// (the handles stay valid: a material is re-acquired by its handle's next use).
void ShellParticleMgr_LevelShutdown();

// CParticleMgr::GetPMaterial: the handle for a material by name, or NULL when
// the material system has none of that name.
PMaterialHandle ShellParticleMaterial_Find( const char *pszName );

// CParticleMgr::PMaterialToIMaterial: the handle's material, re-acquired if a
// level shutdown released it.
IMaterial *ShellParticleMaterial_Material( PMaterialHandle hMaterial );

// The draw state: the model-view matrix of the draw in progress, the light the
// particle light proxy reads, and the argument particle binds carry.
VMatrix &ShellParticleDraw_ModelView();
void ShellParticleDraw_GetLight( CParticleLightInfo &info );
void ShellParticleDraw_SetLight( const CParticleLightInfo &info );
void *ShellParticleDraw_BindArg();

#endif // SHELL_PARTICLE_MGR_H
