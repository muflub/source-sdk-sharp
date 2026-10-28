// client.so's calls on libss_particles.so.

#pragma once

#include <stdint.h>

#include "tier1/interface.h"

struct SSParticleLibCounters_t;

// CHLClient::Init / Shutdown: the library's tier1/tier2 globals, ConVars and
// mathlib, against the engine's factory.
bool ParticleLib_Connect( CreateInterfaceFn appSystemFactory );
void ParticleLib_Disconnect();

// The library's tier2 stand-in counters.
void ParticleLib_Counters( SSParticleLibCounters_t *pOut );

// The internal factory's row for SS_PARTICLE_LIB_INTERFACE_VERSION, so managed
// binds the same object client.so's forwards call.
void *ParticleLib_Acquire();
