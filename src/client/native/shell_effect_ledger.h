//========= SourceSharp — what the leaf effect renderers actually drew ========//
//
// plan_entity.md step 7a's gate is "each effect draws on the sandbox map's
// station". Every word of that is a PICTURE, and a picture is the evidence this
// port has been fooled by before: a sprite drawn at scale zero, a dust volume
// that spawns no motes, an impact that emits particles into a material handle
// that was never cached, and a rope whose render cache is reset every frame and
// never filled all produce the SAME screenshot — the station, with nothing on
// it — and "the game did not crash" is true of all four.
//
// So each effect this step owns increments a counter at the point where it
// hands geometry to the material system or the particle manager, and
// `cs_cl_effects_report` prints them. A number that stays at zero after the
// station has been in front of the camera is the failure the screenshot cannot
// show, and `tools/7a-effect-gate` is what reads it.
//
// WHAT A COUNTER HERE MEANS, exactly, because a loose one would be the check
// that cannot fail: it is incremented AFTER the last early-out on the draw
// path, once per primitive actually emitted (a sprite quad, a particle, a rope
// draw call). It is NOT incremented on entry, and it is NOT a count of entities
// that exist — an entity that exists and draws nothing is precisely the bug.
//
// DEBT, with a removal owner. This is step 7a's gate instrumentation and
// nothing else reads it. It goes with step 8c, which folds the live gates into
// a `make test` leg, or sooner if `tools/live-gate` grows an effects step.
// Cost: nine `int` in one static struct and one `++` per emitted primitive.
//
//=============================================================================//

#pragma once

//-----------------------------------------------------------------------------
// One row per effect step 7a names. Plain ints: every increment below is on the
// client's main thread, on the draw path, and nothing reads them except the
// console command in shell_effect_ledger.cpp.
//-----------------------------------------------------------------------------
struct SourceSharpEffectLedger_t
{
	int nSpriteQuads;      // DrawSpriteModel: one per quad handed to the mesh builder
	int nDustMotes;        // the managed dust volume: one per mote rendered
	int nImpactFlecks;     // FX_DebrisFlecks: one per fleck or smoke particle
	int nImpactDust;       // FX_DustImpact: one per particle
	int nRopeDraws;        // C_RopeKeyframe::DrawModel, past its own early-outs
	int nDissolveDraws;    // C_EntityDissolve::DrawModel, past its own early-outs
	int nDrawBatches;      // C7 item 7: one per view whose custom draws were prepared in one crossing
	int nDrawBatchedSprites; // a sprite drawn from its prepared plan, no crossing
	int nDrawBatchFallbacks; // a custom draw in a batch that drew through its own crossing after all
	int nSingletonDraws;   // DrawParticleSingletons: one per managed singleton executor drawn there (the dust puff)
	// particles.a's tier2 stand-ins count in libss_particles.so (ISSParticleLib::Counters).
};

extern SourceSharpEffectLedger_t g_SourceSharpEffects;
