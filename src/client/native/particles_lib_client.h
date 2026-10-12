//========= SourceSharp — client.so's side of libss_particles.so (plan_particles_so.md spike)
//
// particles_lib_client.cpp loads the library when client.so loads; these are
// the calls the rest of client.so makes on it.
//=============================================================================//

#ifndef PARTICLES_LIB_CLIENT_H
#define PARTICLES_LIB_CLIENT_H

#include <stdint.h>

#include "tier1/interface.h"

struct SSParticleLibCounters_t;

// CHLClient::Init / Shutdown: the library's tier1/tier2 globals, ConVars and
// mathlib, against the engine's factory.
bool ParticleLib_Connect( CreateInterfaceFn appSystemFactory );
void ParticleLib_Disconnect();

// The library's tier2 stand-in counters.
void ParticleLib_Counters( SSParticleLibCounters_t *pOut );

// The internal factory's row for SS_PARTICLE_LIB_INTERFACE_VERSION: the
// library's ISSParticleLib and its slot count (shell_internalfactory.cpp), so
// managed binds the same object client.so's forwards call.
void *ParticleLib_Acquire( int32_t *pnEntries );

#endif // PARTICLES_LIB_CLIENT_H
