//========= SourceSharp — the particles shell's entry points ================//
//
// shell_managed_particles.cpp (its second half): the new-style effects (CShellNewParticleEffect) the client
// starts, watched for /client/{slot}/particles (plan_client_novalve §3.4,
// §4.4). The slots managed calls are in sourcesharp_entrypoints.h.
//
//=============================================================================//

#ifndef SHELL_PARTICLE_SYSTEMS_H
#define SHELL_PARTICLE_SYSTEMS_H

class CShellNewParticleEffect;

// SourceSharpParticleSystemInfo_t's Kind.
enum
{
	SOURCESHARP_PARTICLE_SYSTEM_OWNED    = 1,   // a managed entity's, kept at its owner
	SOURCESHARP_PARTICLE_SYSTEM_ONE_SHOT = 2,   // started at a world point, unheld
};

// EntityHandle.None: the invalid handle is 0xFFFFFFFF, never 0 (CLAUDE.md).
#define SOURCESHARP_PARTICLE_NO_OWNER 0xFFFFFFFFu

// An impact's one-shot, watched (not held) so the dev API lists it.
void ShellParticles_WatchOneShot( CShellNewParticleEffect *pEffect );

// The sprite system's PreRender and LevelShutdownPostEntity call these, in the
// place the flame's sweep always ran.
void ShellParticles_PreRender();

// The old-style manager's three per-frame calls, managed's (C7 particles phase
// 2.3): the update where CParticleMgr::Simulate runs, the frame code at the
// view's IncrementFrameCode, PostRender's.
void ShellParticles_Frame( float flFrameTime );
void ShellParticles_FrameCode();
void ShellParticles_PostRender();
void ShellParticles_LevelShutdown();

#endif // SHELL_PARTICLE_SYSTEMS_H
