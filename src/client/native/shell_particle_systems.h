// The kinds the dev API reads a watched particle system as (shell_particle_systems_client.h).

#pragma once

// SourceSharpParticleSystemInfo_t's Kind.
enum
{
	SOURCESHARP_PARTICLE_SYSTEM_OWNED = 1, // a managed entity's, kept at its owner
	SOURCESHARP_PARTICLE_SYSTEM_ONE_SHOT = 2, // started at a world point, unheld
};

// The invalid entity handle is 0xFFFFFFFF, never 0.
#define SOURCESHARP_PARTICLE_NO_OWNER 0xFFFFFFFFu
