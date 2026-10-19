//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Effects;

// C7 particles phase 4: particlemgr.cpp's and particles_new.cpp's live
// convars, declared here since both left client.so, with stock's names,
// defaults, flags and help. Native reads them by name (CManagedConVarRef in
// shell_new_particles_client.cpp and shell_managed_particles.cpp) and hands
// the values to the managed decisions in the frame's and the draw's buffers.
//
// Not carried, because nothing reads them any more: particle_simulateoverflow
// (CParticleEffectBinding::AddParticle's stress test; the old-style particles
// are managed's), r_threaded_particles (the new effects step in one
// single-threaded batch, the phase 3 spike's decision) and
// particle_sim_alt_cores (an Xbox 360 thread pool).

/// <summary>r_drawparticles: both managers' DrawModel draw nothing at 0.</summary>
[ConVar("r_drawparticles", "1", Flags = ConVarFlags.Cheat, Help = "Enable/disable particle rendering")]
public sealed class DrawParticlesConVar : IntConVar { }

/// <summary>cl_particleeffect_aabb_buffer: DetectChanges' slack before the leaf system is told.</summary>
[ConVar("cl_particleeffect_aabb_buffer", "2", Flags = ConVarFlags.Cheat,
        Help = "Add this amount to a particle effect's bbox in the leaf system so if it's growing slowly, it won't have to be reinserted as often.")]
public sealed class ParticleEffectAabbBufferConVar : FloatConVar { }

/// <summary>cl_particle_show_bbox: DebugDrawBbox after each draw.</summary>
[ConVar("cl_particle_show_bbox", "0", Flags = ConVarFlags.Cheat)]
public sealed class ParticleShowBBoxConVar : IntConVar { }

/// <summary>cl_particle_show_bbox_cost: the debug box coloured by particle count.</summary>
[ConVar("cl_particle_show_bbox_cost", "0", Flags = ConVarFlags.Cheat,
        Help = "Show # of particles: green->blue->red. Use a negative number to show ALL particles even cheap ones")]
public sealed class ParticleShowBBoxCostConVar : IntConVar { }

/// <summary>r_particle_timescale: UpdateNewEffects' dt scale.</summary>
[ConVar("r_particle_timescale", "1.0", Flags = ConVarFlags.Cheat | ConVarFlags.DevelopmentOnly)]
public sealed class ParticleTimescaleConVar : FloatConVar { }

/// <summary>
/// cl_particle_retire_cost: EarlyRetireParticleSystems' budget, in thousands
/// of pixels; 0 turns retirement off. materialsystem.so names it among the
/// convars a dxsupport config may set; no dxsupport*.cfg of this install sets it.
/// </summary>
[ConVar("cl_particle_retire_cost", "0", Flags = ConVarFlags.Cheat | ConVarFlags.AllowedInCompetitive)]
public sealed class ParticleRetireCostConVar : FloatConVar { }

/// <summary>r_particle_sim_spike_threshold_ms: EndSimulateParticles keeps a frame's profile past it.</summary>
[ConVar("r_particle_sim_spike_threshold_ms", "5")]
public sealed class ParticleSimSpikeThresholdConVar : IntConVar { }

/// <summary>cl_particle_stats_trigger_count: stock's stats spew (empty in this tree) past this count.</summary>
[ConVar("cl_particle_stats_trigger_count", "0", Help = "Dump stats if the particle count exceeds this number.")]
public sealed class ParticleStatsTriggerCountConVar : IntConVar { }
