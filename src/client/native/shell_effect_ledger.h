// Counts of primitives the leaf effect renderers actually emitted, read by
// cs_cl_effects_report. Each counter is incremented after the last early-out on
// its draw path, once per primitive emitted, never on entry.

#pragma once

// Main thread only.
struct SourceSharpEffectLedger_t
{
	int nSpriteQuads; // DrawSpriteModel: one per quad handed to the mesh builder
	int nDustMotes; // the managed dust volume: one per mote rendered
	int nImpactFlecks; // FX_DebrisFlecks: one per fleck or smoke particle
	int nImpactDust; // FX_DustImpact: one per particle
	int nRopeDraws; // C_RopeKeyframe::DrawModel, past its own early-outs
	int nDissolveDraws; // C_EntityDissolve::DrawModel, past its own early-outs
	int nDrawBatches; // one per view whose custom draws were prepared in one crossing
	int nDrawBatchedSprites; // a sprite drawn from its prepared plan, no crossing
	int nDrawBatchFallbacks; // a custom draw in a batch that drew through its own crossing after all
	int nSingletonDraws; // DrawParticleSingletons: one per managed singleton executor drawn there (the dust puff)
};

extern SourceSharpEffectLedger_t g_SourceSharpEffects;
