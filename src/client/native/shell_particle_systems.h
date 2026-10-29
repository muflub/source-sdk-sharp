// The particle shell's entry points; the slots managed code calls are in sourcesharp_entrypoints.h.

#pragma once

class CShellNewParticleEffect;

// SourceSharpParticleSystemInfo_t's Kind.
enum
{
	SOURCESHARP_PARTICLE_SYSTEM_OWNED = 1, // a managed entity's, kept at its owner
	SOURCESHARP_PARTICLE_SYSTEM_ONE_SHOT = 2, // started at a world point, unheld
};

// The invalid entity handle is 0xFFFFFFFF, never 0.
#define SOURCESHARP_PARTICLE_NO_OWNER 0xFFFFFFFFu

// An impact's one-shot, watched (not held) so the dev API lists it.
void ShellParticles_WatchOneShot(CShellNewParticleEffect* pEffect);

// Called from the sprite system's PreRender and LevelShutdownPostEntity.
void ShellParticles_PreRender();

// The old-style particle manager's per-frame calls: Simulate's update, the
// view's frame code, and PostRender.
void ShellParticles_Frame(float flFrameTime);
void ShellParticles_FrameCode();
void ShellParticles_PostRender();
void ShellParticles_LevelShutdown();
