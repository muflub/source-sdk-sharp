//========= SourceSharp: what an impact effect is made of, as numbers =========//
//
// plan_entity.md §3.9, SHIM_WAVE Lane 3. CLIENT ONLY.
//
// shell_impact_effects.cpp used to be 1,380 lines with ZERO SourceSharp_ entry
// points: a switch on the surface material choosing fleck counts, speed ranges,
// gravity, dampening and angular spray, plus 112 other chosen constants. §6's
// nearest row is "leaf renderers ... mesh building and material calls" and it
// does not reach -- this file builds no mesh and calls no material; it chose how
// many particles of what kind at what velocity and handed them to the emitter
// that does.
//
// §3.9 names the shape it should have: managed picks a PARAMETER BLOCK and ONE
// call hands it to a native emitter loop. This header is that block's layout,
// and it is the contract between the two sides.
//
// ONE CALL PER EFFECT AND NOT ONE PER PARTICLE. The emitter loops stay native
// and sample INSIDE the ranges the block gives them; the block carries the
// ranges, never the samples. A per-particle crossing is precisely what this
// design exists to avoid, and SHIM_WAVE Lane 3's gate 3 asserts it from the
// per-op counter.
//
// WHY THE BURSTS ARE GENERIC RATHER THAN ONE NAMED FIELD PER CONSTANT. Every
// one of the seven effects is the same shape N times over -- a count, a
// lifetime range, a direction spray, a speed range, a size range, an alpha
// range, a roll-delta range and a colour ramp -- so a named field per constant
// would be about three hundred declarations on each side of the boundary, kept
// in step by hand. Twenty-four generic slots cover all of them, the effect's own
// native loop reads the ones it uses, and ImpactEffectParamsTests compares this
// header against the managed enum off disk rather than trusting the two to stay
// in step (the check TempEntForwardIdTests already does for the forwards).
//
// THE ORDER IS THE CONTRACT. These are array indices in all but name: a value
// inserted in the middle silently repoints every field after it at a different
// number, which would read as an effect that still draws and draws wrong.
// Append only.
//
//===========================================================================//

#ifndef SOURCESHARP_IMPACT_EFFECTS_H
#define SOURCESHARP_IMPACT_EFFECTS_H
#ifdef _WIN32
#pragma once
#endif

//-----------------------------------------------------------------------------
// One id per native entry point that asks managed for its numbers.
//
// Each line carries BOTH names -- the C++ function and the managed
// ImpactEffect value -- for the reason tempent_forwards.h gives: spelling the
// managed name here rather than deriving it means there is no stripping rule
// for the two sides to disagree about.
//-----------------------------------------------------------------------------
enum SourceSharpImpactEffect_t
{
	SOURCESHARP_IMPACT_DEBRIS_FLECKS = 0,   // FX_DebrisFlecks -> DebrisFlecks
	SOURCESHARP_IMPACT_GLASS         = 1,   // FX_GlassImpact -> Glass
	SOURCESHARP_IMPACT_ANTLION       = 2,   // FX_AntlionImpact -> Antlion
	SOURCESHARP_IMPACT_BUG_BLOOD     = 3,   // FX_BugBlood -> BugBlood
	SOURCESHARP_IMPACT_BLOOD         = 4,   // FX_Blood -> Blood
	SOURCESHARP_IMPACT_DUST          = 5,   // FX_DustImpact -> Dust
	SOURCESHARP_IMPACT_GAUSS         = 6,   // FX_GaussExplosion -> Gauss

	SOURCESHARP_IMPACT_EFFECT_COUNT  = 7,
};

//-----------------------------------------------------------------------------
// WHAT MANAGED DECIDES FROM. Native already holds every one of these; they go
// up so that the decision is a function of its inputs, which is the shape
// ClientRenderFacts and ShadowCastFacts_t both have.
//-----------------------------------------------------------------------------
enum SourceSharpImpactIn_t
{
	SS_IMPACT_IN_MATERIAL = 0,   // the CHAR_TEX_* the trace's surface carries
	SS_IMPACT_IN_SCALE    = 1,   // the impact's scale, as the caller passed it
	SS_IMPACT_IN_NOFLECKS = 2,   // r_drawflecks, or the caller's own suppression

	SS_IMPACT_IN_COUNT    = 3,
};

//-----------------------------------------------------------------------------
// THE BLOCK'S HEADER -- what is true of the whole effect rather than of one
// burst. Ten floats, then SS_IMPACT_HEADER_COUNT * 1 burst records.
//-----------------------------------------------------------------------------
enum SourceSharpImpactHeader_t
{
	SS_IMPACT_BURSTS        = 0,   // how many burst records follow
	SS_IMPACT_GRAVITY       = 1,   // CParticleCollision::Setup gravity
	SS_IMPACT_DAMPEN        = 2,   // CParticleCollision::Setup dampening
	SS_IMPACT_ANGULAR_SPRAY = 3,   // CParticleCollision::Setup angular spray
	SS_IMPACT_SPAWN_OFFSET  = 4,   // units along the surface normal to spawn at
	SS_IMPACT_COLL_MIN      = 5,   // CParticleCollision::Setup min speed
	SS_IMPACT_COLL_MAX      = 6,   // CParticleCollision::Setup max speed
	SS_IMPACT_HEAD_A        = 7,   // per-effect, named in the effect's own code
	SS_IMPACT_HEAD_B        = 8,
	SS_IMPACT_HEAD_C        = 9,

	SS_IMPACT_HEADER_COUNT  = 10,
};

//-----------------------------------------------------------------------------
// ONE BURST. A count and the ranges the emitter samples inside.
//
// A range whose two ends are equal is a fixed value, which is how a constant
// that stock wrote as a literal (m_flDieTime = 1.0f) is carried without a
// second kind of field. A burst that does not use a slot leaves it zero, and
// the effect's own loop simply does not read it -- an unread zero is not a
// silent default, because nothing reads it.
//-----------------------------------------------------------------------------
enum SourceSharpImpactBurst_t
{
	SS_BURST_COUNT_MIN      =  0,
	SS_BURST_COUNT_MAX      =  1,
	SS_BURST_LIFE_MIN       =  2,
	SS_BURST_LIFE_MAX       =  3,
	SS_BURST_SPRAY_MIN      =  4,   // per-axis jitter added to the base direction
	SS_BURST_SPRAY_MAX      =  5,
	SS_BURST_SPEED_MIN      =  6,
	SS_BURST_SPEED_MAX      =  7,
	SS_BURST_Z_MIN          =  8,   // the downward bias stock applies to vel[2]
	SS_BURST_Z_MAX          =  9,
	SS_BURST_SIZE_MIN       = 10,
	SS_BURST_SIZE_MAX       = 11,
	SS_BURST_SIZE_END_MUL   = 12,   // end size = start size * this
	SS_BURST_ALPHA_MIN      = 13,
	SS_BURST_ALPHA_MAX      = 14,
	SS_BURST_ROLL_MIN       = 15,
	SS_BURST_ROLL_MAX       = 16,
	SS_BURST_ROLL_DELTA_MIN = 17,
	SS_BURST_ROLL_DELTA_MAX = 18,
	SS_BURST_RAMP_MIN       = 19,   // the colour ramp the surface colour is
	SS_BURST_RAMP_MAX       = 20,   // multiplied by, per particle
	SS_BURST_MATERIAL       = 21,   // a SS_IMPACT_MAT_* slot, not a handle
	SS_BURST_EXTRA_A        = 22,   // per-burst, named in the effect's own code
	SS_BURST_EXTRA_B        = 23,

	// THE BURST'S OWN COLOUR, or all three zero for "use the colour the caller
	// or the surface supplies". Glass shards are near-white and the dust behind
	// them is blue-grey; bug blood is yellow; antlion dust is warm. Each of
	// those is a choice about what the thing being hit IS, and each was a
	// literal in the emitter until §3.9 moved it.
	SS_BURST_TINT_R         = 24,
	SS_BURST_TINT_G         = 25,
	SS_BURST_TINT_B         = 26,

	SS_IMPACT_BURST_FIELDS  = 27,
};

//-----------------------------------------------------------------------------
// WHICH MATERIAL, as a slot rather than a handle.
//
// The `switch ( materialType )` that chose between the wood and the cement
// fleck sheets was the file's headline decision and it is managed now; what
// crosses is this number, and shell_impact_effects.cpp's own table turns it
// into the PMaterialHandle it cached at level load. A handle cannot cross and
// should not: it is a pointer into the material system.
//-----------------------------------------------------------------------------
enum SourceSharpImpactMaterial_t
{
	SS_IMPACT_MAT_NONE          = 0,
	SS_IMPACT_MAT_DUST_PUFF     = 1,   // g_Mat_DustPuff[0]
	SS_IMPACT_MAT_DUST_PUFF_2   = 2,   // g_Mat_DustPuff[1]
	SS_IMPACT_MAT_BLOOD_PUFF    = 3,   // g_Mat_BloodPuff[0]
	SS_IMPACT_MAT_BLOOD_PUFF_2  = 4,   // g_Mat_BloodPuff[1]
	SS_IMPACT_MAT_DUST_PUFF2    = 5,   // g_DustPuff2
	SS_IMPACT_MAT_FLECK_CEMENT  = 6,   // g_Mat_Fleck_Cement, a PAIR
	SS_IMPACT_MAT_FLECK_WOOD    = 7,   // g_Mat_Fleck_Wood, a PAIR
	SS_IMPACT_MAT_FLECK_ANTLION = 8,   // g_Mat_Fleck_Antlion, a PAIR
	SS_IMPACT_MAT_FLECK_GLASS   = 9,   // g_Mat_Fleck_Glass, a PAIR

	SS_IMPACT_MAT_COUNT         = 10,
};

// The widest block any effect answers: ten header floats and eight bursts.
// Native allocates this on the stack and passes the size, so a managed side
// that has grown a burst since native was built is refused rather than
// overrunning it.
#define SS_IMPACT_MAX_BURSTS   8
#define SS_IMPACT_MAX_FLOATS   ( SS_IMPACT_HEADER_COUNT + \
                                 SS_IMPACT_MAX_BURSTS * SS_IMPACT_BURST_FIELDS )

// Where burst n's fields start in the block.
#define SS_IMPACT_BURST_AT( n ) ( SS_IMPACT_HEADER_COUNT + (n) * SS_IMPACT_BURST_FIELDS )

//=============================================================================
// WHICH EFFECT FAMILY AN IMPACT IS, WHICH DECAL IT LEAVES, AND WHETHER FLECKS
// ARE DRAWN -- plan_shim.md §6, lane 4b-effects.
//
// The block above is what ONE effect family is made of. This is the decision
// ABOVE it: the `iMaterial == CHAR_TEX_*` chain in PerformCustomEffects, the
// twenty-six-row surface->particle-system table PerformNewCustomEffects
// indexes, the fleck suppression both of them recompute, and the decal name
// GetImpactDecal hands to the engine's translator. Every one of those is a
// choice about what a surface IS, written as a literal in C++, and none of them
// touches a mesh, a material or the particle manager.
//
// ONE CALL PER IMPACT, ASKED ONCE AND BEFORE THE TRACE.
// SourceSharp_ImpactCallback asks as soon as it has unpacked the wire data and
// uses the answer twice: the decal name immediately, the family only if the
// trace hit something. Asking in two places would have been two crossings per
// bullet for one decision made from the same inputs.
//
// NOT IN HERE, AND SAID RATHER THAN LEFT TO BE FOUND:
//
//   * the SURF_SKY|SURF_NODRAW|SURF_HINT|SURF_SKIP early-out stays native. It
//     is a fact about the surface the ENGINE traced, arriving on the trace_t
//     this side already holds, and moving it would mean crossing the trace to
//     ask a question about the trace.
//
//   * PlayImpactSound stays native, which contradicts the analysis lane's
//     ruling. Read for a chosen constant it has none: it tests whether the
//     server named a surfaceprop, asks physprops->GetSurfaceData for that
//     prop's bulletImpact sound and emits it. Three engine lookups and no
//     policy -- the sound a surface makes is authored in the surfaceprop file,
//     not in this code.
//
//   * the reflect vector for metal, the control points for a new particle
//     effect and the angles for a dispatched one are arithmetic over the
//     trace's own normal, and they stay with the emitter that uses them.
//=============================================================================

//-----------------------------------------------------------------------------
// WHAT THE CHOICE IS MADE FROM. Native holds all six; they go up so the
// decision is a function of its inputs, the shape SourceSharpImpactIn_t above
// already has. INTEGERS, not floats: every one of them is a material id, a
// flag word or a convar's int value, and the scale is stock's own `int iScale`.
//-----------------------------------------------------------------------------
enum SourceSharpImpactChoiceIn_t
{
	SS_CHOICE_IN_MATERIAL    = 0,   // the CHAR_TEX_* the trace's surface carries
	SS_CHOICE_IN_SCALE       = 1,   // the impact's scale, as the caller passed it
	SS_CHOICE_IN_FLAGS       = 2,   // the caller's FLAGS_CUSTIOM_EFFECTS_* word
	SS_CHOICE_IN_DRAW_FLECKS = 3,   // r_drawflecks
	SS_CHOICE_IN_NEW_EFFECTS = 4,   // cl_new_impact_effects
	SS_CHOICE_IN_DAMAGE_TYPE = 5,   // the impact's DMG_* word, for the decal

	SS_CHOICE_IN_COUNT       = 6,
};

//-----------------------------------------------------------------------------
// WHAT NATIVE DOES ABOUT IT. One arm per emitter PerformCustomEffects can
// reach, plus the two that name a particle system rather than calling an
// emitter, plus "nothing" -- which is a real answer and the one every material
// with no entry in stock's chain already got.
//
// THE ORDER IS THE CONTRACT, like every other enum in this header: these cross
// as numbers. Append only.
//-----------------------------------------------------------------------------
enum SourceSharpImpactAction_t
{
	SS_IMPACT_ACT_NOTHING       = 0,   // stock's fall-through: no custom effect
	SS_IMPACT_ACT_DEBRIS_FLECKS = 1,   // FX_DebrisFlecks( origin, &tr, mat, scale, noFlecks )
	SS_IMPACT_ACT_DUST          = 2,   // FX_DustImpact( origin, &tr, scale )
	SS_IMPACT_ACT_ANTLION       = 3,   // FX_AntlionImpact( origin, &tr )
	SS_IMPACT_ACT_METAL_SPARK   = 4,   // the jittered reflect, then FX_MetalSpark
	SS_IMPACT_ACT_SPARKS        = 5,   // g_pEffects->Sparks, one unit off the normal
	SS_IMPACT_ACT_PARTICLE      = 6,   // DispatchParticleEffect( the name below )
	SS_IMPACT_ACT_NEW_PARTICLE  = 7,   // CNewParticleEffect::Create( NULL, the name below )

	SS_IMPACT_ACT_COUNT         = 8,
};

//-----------------------------------------------------------------------------
// THE ANSWER, as ints. SS_CHOICE_OUT_SCALE and _NOFLECKS come back rather than
// being recomputed natively because managed decided them: the fleck
// suppression is `!r_drawflecks || (nFlags & FLAGS_CUSTIOM_EFFECTS_NOFLECKS)`,
// which stock computes TWICE in two functions, and a native copy of it beside
// a managed one is the divergence this move exists to remove.
//-----------------------------------------------------------------------------
enum SourceSharpImpactChoiceOut_t
{
	SS_CHOICE_OUT_ACTION   = 0,   // a SS_IMPACT_ACT_*
	SS_CHOICE_OUT_SCALE    = 1,   // the scale to pass the emitter
	SS_CHOICE_OUT_NOFLECKS = 2,   // 1 when flecks are suppressed

	SS_CHOICE_OUT_COUNT    = 3,
};

// The two names one choice can carry. Neither is ever both: a particle-system
// name belongs to the two ACT_*PARTICLE arms and the decal name to every
// impact, so they are separate buffers rather than one used two ways.
//
// 64 is stock's own ceiling for both -- the longest entry in the effect table
// is "impact_concrete_noflecks" at 24 and the longest decal name here is
// "Impact.Concrete" at 15 -- and the writer is told the size, so a managed side
// that grew a longer name is refused rather than truncated.
#define SS_IMPACT_NAME_MAX   64

#endif // SOURCESHARP_IMPACT_EFFECTS_H
