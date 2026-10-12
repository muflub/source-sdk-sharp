using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_tmp.md Part 8 — the rewiring, as assertions.
///
/// <para>
/// <c>EffectsOverTempEnts</c> answers eight members of
/// <see cref="IEngineEffects"/> over <see cref="ITempEnts"/> and inherits the
/// other sixty from <see cref="EngineEffectsForwarder"/>. TWO THINGS CAN
/// GO WRONG AND ONLY ONE OF THEM IS OBVIOUS: a rewired member can send the
/// wrong thing, and a member can end up doing NEITHER — inherited by accident,
/// or overridden and left empty. The second is the one this port is scored
/// against, because it looks like an effect that never arrived rather than like
/// a bug.
/// </para>
///
/// <para>
/// So <see cref="EveryMemberEitherMovedOrWasForwardedAndNoneDidNothing"/> walks
/// all sixty-eight by reflection, calls each one, and asserts which of the two
/// happened to it. That test is the reason the per-member tests below can be
/// about VALUES rather than about coverage.
/// </para>
///
/// <para>
/// ONE DOUBLE FOR THREE AREAS. <see cref="RecordingEngine"/> implements every
/// engine area, so the same object is the inner
/// <see cref="IEngineEffects"/>, the <see cref="IEngineSound"/> and the
/// <see cref="IEngineEntities"/> — and its one call log then says, in order,
/// both what the rewired member asked of the engine and what it did not.
/// </para>
/// </summary>
public class EffectsRewiringTests
{
    // ---------------------------------------------------------------------
    // The eight rows of Part 8's table that moved.
    // ---------------------------------------------------------------------

    [Fact]
    public void SparksGoOutAsATempEntityThroughAPvsFilterAtTheOrigin()
    {
        var (effects, temps, engine) = Rig();

        effects.SparkEffect(new Vector(10f, 20f, 30f), magnitude: 4, trailLength: 7,
                            makeSound: false, direction: new Vector(0f, 0f, 1f));

        var call = temps.Only("Sparks");

        Assert.True(call.Filter.Kind == TeFilterKind.Pvs,
            "PVS and not PAS — CEffectsServer::Sparks's own filter, because sparks are a thing "
          + "you SEE. Its sound is a separate call with a separate filter");
        Assert.True(call.Filter.Origin.Equals(new Vector(10f, 20f, 30f)),
            "the filter is built from the position the sparks are at");
        Assert.True(call.Delay == 0f, "no delay, which is te->Sparks's 0.0");
        Assert.True(call.Arg<Vector>(0).Equals(new Vector(10f, 20f, 30f)), "at the origin asked for");
        Assert.True(call.Arg<int>(1) == 4, "the magnitude asked for");
        Assert.True(call.Arg<int>(2) == 7, "the trail length asked for");
        Assert.True(call.Arg<Vector?>(3)!.Value.Equals(new Vector(0f, 0f, 1f)),
            "and the direction asked for");

        Assert.True(engine.CountOf("Effects.SparkEffect") == 0,
            "and nothing reached the native shim: this is the rewiring, not a second path");
    }

    /// <summary>
    /// THE COMMENT THAT HAD TO TRAVEL. The sound is emitted at a POSITION and
    /// not on the sparking entity, because <c>DoSpark</c> — the helper next to
    /// <c>CEffects::Sparks</c> — calls <c>EmitSound</c> on that entity with no
    /// null check, and a managed <c>env_spark</c> is on the parallel hierarchy.
    /// Passing NULL segfaulted the server the first time one sparked audibly.
    /// </summary>
    [Fact]
    public void TheSparkSoundIsMadeAtThePositionAndOnlyWhenItWasAskedFor()
    {
        var (effects, temps, engine) = Rig();

        effects.SparkEffect(new Vector(1f, 2f, 3f), 1, 1, makeSound: true);

        Assert.True(engine.CountOf("Sound.EmitSoundAtPosition") == 1, "one sound");

        var sound = engine.Last("Sound.EmitSoundAtPosition")!.Value;

        Assert.True(sound.ArgAs<string>(0) == "DoSpark", "the sound stock's DoSpark plays");
        Assert.True(sound.ArgAs<Vector>(1).Equals(new Vector(1f, 2f, 3f)),
            "from the position — SOUND_FROM_WORLD — and not from an entity that does not exist "
          + "on this hierarchy");

        var (quiet, quietTemps, quietEngine) = Rig();
        quiet.SparkEffect(new Vector(1f, 2f, 3f), 1, 1, makeSound: false);

        Assert.True(quietEngine.CountOf("Sound.EmitSoundAtPosition") == 0,
            "and makeSound:false makes no sound at all");
        Assert.True(quietTemps.CountOf("Sparks") == 1,
            "while still making the sparks — the two halves are independent, which is why the "
          + "shim did them separately rather than calling DoSpark");
    }

    /// <summary>
    /// <c>ITempEnts.Sparks</c> writes <c>vec3_origin</c> for a null direction,
    /// which is stock's "no particular direction". The rewiring passes the null
    /// STRAIGHT THROUGH rather than substituting: two substitutions of one
    /// default are two opinions that can come to disagree.
    /// </summary>
    [Fact]
    public void ANullSparkDirectionStaysNullRatherThanBecomingAZeroVectorTwice()
    {
        var (effects, temps, _) = Rig();

        effects.SparkEffect(new Vector(0f, 0f, 0f), 2, 2, makeSound: false, direction: null);

        Assert.True(temps.Only("Sparks").Arg<Vector?>(3) is null,
            "the null reaches ITempEnts.Sparks as a null, and the substitution happens once, "
          + "where te_sparks.cpp:84 does it");
    }

    [Fact]
    public void TheExplosionGoesOutThroughAPasFilterBuiltFromItsOwnOrigin()
    {
        var (effects, temps, engine) = Rig();

        effects.ExplosionEffect(new Vector(-8f, 16f, 4f), modelIndex: 12, scale: 2.5f,
                                frameRate: 15, flags: 6, radius: 300, magnitude: 90);

        var call = temps.Only("Explosion");

        Assert.True(call.Filter.Kind == TeFilterKind.Pas,
            "PAS around the blast, which is SourceSharp_ExplosionEffect's own filter and the "
          + "whole risk of this row: an explosion written to an EMPTY filter is silently "
          + "invisible — damage lands, scorch mark appears, no fireball is ever drawn");
        Assert.True(call.Filter.Origin.Equals(new Vector(-8f, 16f, 4f)),
            "and it is built HERE from the origin rather than being a parameter");

        Assert.True(call.Arg<Vector>(0).Equals(new Vector(-8f, 16f, 4f)), "the position");
        Assert.True(call.Arg<int>(1) == 12, "the model index");
        Assert.True(call.Arg<float>(2) == 2.5f, "the scale");
        Assert.True(call.Arg<int>(3) == 15, "the frame rate");
        Assert.True(call.Arg<int>(4) == 6, "the TE_EXPLFLAG_* flags — not the entity's spawnflags");
        Assert.True(call.Arg<int>(5) == 300, "the radius");
        Assert.True(call.Arg<int>(6) == 90, "the magnitude");

        Assert.True(call.Arg<Vector?>(7) is null,
            "the normal is left at its default, which is what the shim did by not passing one: "
          + "te_explosion.cpp:126 turns the NULL into (0,0,1) — UP — rather than a zero-length "
          + "normal, and ITempEnts.Explosion does the same substitution");
        Assert.True(call.Arg<byte>(8) == (byte)'C',
            "and the material type is 'C' for concrete, stock's own declared default");

        Assert.True(engine.CountOf("Effects.ExplosionEffect") == 0, "nothing reached the shim");
    }

    [Fact]
    public void TheMuzzleFlashGoesOutThroughAPvsFilterWithItsAnglesRelabelled()
    {
        var (effects, temps, engine) = Rig();

        effects.MuzzleFlashEffect(new Vector(5f, 6f, 7f), new Vector(15f, 90f, -30f),
                                  scale: 1.5f, type: 3);

        var call = temps.Only("MuzzleFlash");

        Assert.True(call.Filter.Kind == TeFilterKind.Pvs, "PVS, CEffectsServer::MuzzleFlash's filter");
        Assert.True(call.Filter.Origin.Equals(new Vector(5f, 6f, 7f)), "built from the position");
        Assert.True(call.Arg<Vector>(0).Equals(new Vector(5f, 6f, 7f)), "the position");
        Assert.True(call.Arg<QAngle>(1) == new QAngle(15f, 90f, -30f),
            "the angles arrive as pitch/yaw/roll in the order QAngle stores them, which is the "
          + "order C#'s Vector already holds an entity's angles in — QAngle.FromVector is a "
          + "relabelling and not a conversion");
        Assert.True(call.Arg<float>(2) == 1.5f, "the scale");
        Assert.True(call.Arg<int>(3) == 3, "the type");

        Assert.True(engine.CountOf("Effects.MuzzleFlashEffect") == 0, "nothing reached the shim");
    }

    [Fact]
    public void TheFunnelBroadcastsAndAModellessOneIsNotSentAtAll()
    {
        var (effects, temps, engine) = Rig();

        effects.FunnelEffect(new Vector(0f, 0f, 64f), spriteIndex: 21, reversed: true);

        var call = temps.Only("LargeFunnel");

        Assert.True(call.Filter.Kind == TeFilterKind.Broadcast,
            "BROADCAST and not PAS, which is stock's own choice and not an oversight: a funnel "
          + "is large, silent and meant to be seen from across a map, so a filter that asks "
          + "whether you could HEAR it is the wrong question");
        Assert.True(call.Arg<Vector>(0).Equals(new Vector(0f, 0f, 64f)), "the position");
        Assert.True(call.Arg<int>(1) == 21, "the sprite index");
        Assert.True(call.Arg<int>(2) == 1,
            "reversed is an INT and not a bool, because stock's m_nReversed is one and the prop "
          + "is two bits wide rather than one");

        var (nomodel, noTemps, _) = Rig();
        nomodel.FunnelEffect(new Vector(0f, 0f, 64f), spriteIndex: 0, reversed: false);

        Assert.True(noTemps.CountOf("LargeFunnel") == 0,
            "a sprite index of zero is 'no model' and draws nothing, so the shim's guard travels "
          + "with it: sending it costs a message and produces silence at the far end, which is "
          + "indistinguishable from a table that did not pair");
        Assert.True(engine.CountOf("Effects.FunnelEffect") == 0, "nothing reached the shim");
    }

    [Fact]
    public void DustIsARealTempEntityAndItsDirectionIsNormalisedBeforeItGoes()
    {
        var (effects, temps, engine) = Rig();

        effects.DustEffect(new Vector(3f, 3f, 3f), new Vector(0f, 0f, 9f), size: 4f, speed: 12f);

        var call = temps.Only("Dust");

        Assert.True(call.Filter.Kind == TeFilterKind.Pvs, "PVS, CEffectsServer::Dust's filter");
        Assert.True(call.Arg<Vector>(0).Equals(new Vector(3f, 3f, 3f)), "the position");
        Assert.True(call.Arg<Vector>(1).Equals(new Vector(0f, 0f, 1f)),
            "the direction NORMALISED, which is TE_Dust's own assert turned into the thing it "
          + "was asserting: four bits per component over an explicit [-1, 1] means a longer "
          + "vector is CLAMPED by the encoder and arrives pointing somewhere else");
        Assert.True(call.Arg<float>(2) == 4f, "the size");
        Assert.True(call.Arg<float>(3) == 12f, "the speed");

        Assert.True(engine.CountOf("Effects.DustEffect") == 0,
            "and NOT through the native shim, and NOT as a DispatchEffect(\"dust\") — C_TEDust "
          + "is registered and waiting, and a dispatch would leave it unpaired and play a "
          + "different effect (ledger row S6)");
    }

    /// <summary>
    /// The zero guard is not the normalise, and they are here for different
    /// reasons: <c>Normalized</c> answers zero for a zero, so without this a
    /// mapper who left the angles alone would get a puff that goes nowhere.
    /// </summary>
    [Fact]
    public void AZeroDustDirectionBecomesUpRatherThanStayingZero()
    {
        var (effects, temps, _) = Rig();

        effects.DustEffect(Vector.Zero, Vector.Zero, 1f, 1f);

        Assert.True(temps.Only("Dust").Arg<Vector>(1).Equals(new Vector(0f, 0f, 1f)),
            "(0,0,1), which is what SourceSharp_DustEffect substituted: stock's assert catches "
          + "a direction that is too LONG and nothing in stock catches a zero one");
    }

    /// <summary>
    /// BEHAVIOUR LEDGER ROW C7 is why this test asserts the NAME rather than
    /// asserting that something visible happens. Nothing registered
    /// <c>"Impact"</c> on this client for the whole life of the build, so the
    /// effect did not play — and it was still sent under that name, because
    /// substituting one that resolves would have turned a tracked gap into an
    /// untracked one.
    ///
    /// <para>
    /// The row is CLOSED (lane <c>fx-impact</c>, which registers that spelling
    /// in <c>shell_impact_effects.cpp</c>) and this fact is unchanged: what it
    /// pins is the name on the wire, which is the thing the registration is
    /// matched against. The live half — that something is now actually drawn —
    /// is <c>tools/fx-impact-gate</c>, because no unit test can see a particle.
    /// </para>
    /// </summary>
    [Fact]
    public void TheImpactIsDispatchedByTheNameImpactAndNothingIsSubstitutedForIt()
    {
        var (effects, temps, engine) = Rig();
        engine.Returns("Entities.GetIndex", 17);

        effects.ImpactEffect(new Vector(0f, 0f, 0f), new Vector(0f, 64f, 0f),
                             surfaceProp: 42, damageType: DamageType.Bullet,
                             entity: new EntityHandle(99));

        var call = temps.Only("DispatchEffectByName");

        Assert.True(call.Arg<string>(0) == "Impact",
            "by the name 'Impact', which is what SourceSharp_ImpactEffect dispatched. For the "
          + "whole life of this build nothing on the client claimed that name and it was sent "
          + "anyway; shell_impact_effects.cpp claims it now, and this spelling is what the "
          + "registration is matched against");

        Assert.True(call.Filter.Kind == TeFilterKind.Pas
                 && call.Filter.Origin.Equals(new Vector(0f, 64f, 0f)),
            "through a PAS filter built from the data's own origin, which is what "
          + "DispatchEffect( name, data ) does (te_effect_dispatch.cpp:95)");

        TeEffectData data = call.Arg<TeEffectData>(1);

        Assert.True(data.Start.Equals(new Vector(0f, 0f, 0f)),
            "m_vStart is where the shot came FROM, because the client re-traces start->origin "
          + "itself in fx_impact.cpp to find the plane it decals");
        Assert.True(data.Origin.Equals(new Vector(0f, 64f, 0f)), "m_vOrigin is where it landed");
        Assert.True(data.SurfaceProp == 42,
            "the surfaceprop is a PARAMETER and not read off a trace — the shim's one difference "
          + "from CBaseEntity::ImpactTrace, and the reason a shot into the back of a pane of "
          + "glass does not leave a concrete decal");
        Assert.True(data.DamageType == (int)DamageType.Bullet, "the damage type");
        Assert.True(data.HitBox == 0, "no hitbox");
        Assert.True(data.EntIndex == 17, "the entity index the client attaches the impact to");

        Assert.True(data.Scale == 1f,
            "AND THE SCALE IS ONE. CEffectData's constructor sets m_flScale = 1.f and everything "
          + "else to zero (effect_dispatch_data.h:90); SourceSharp_ImpactEffect never assigns a "
          + "scale, so a 1 is what went on the wire. A managed default(TeEffectData) would have "
          + "sent 0 and nothing anywhere would have said so");

        Assert.True(engine.CountOf("Effects.ImpactEffect") == 0, "nothing reached the shim");
    }

    /// <summary>
    /// <c>SoundSourceIndex</c>'s floor: an entity with no edict answers -1, and
    /// -1 is not a sound source — it is <c>SOUND_FROM_LOCAL_PLAYER</c> on the
    /// client. Zero is the world, which is what stock writes for anything the
    /// client cannot name.
    /// </summary>
    [Fact]
    public void AnEntityWithNoEdictAttachesTheImpactToTheWorldRatherThanToMinusOne()
    {
        var (effects, temps, engine) = Rig();
        engine.Returns("Entities.GetIndex", -1);

        effects.ImpactEffect(Vector.Zero, Vector.Zero, 0, DamageType.Bullet, new EntityHandle(5));

        Assert.True(temps.Only("DispatchEffectByName").Arg<TeEffectData>(1).EntIndex == 0,
            "index 0 — SOUND_FROM_WORLD — and not -1, which the client reads as itself");
    }

    [Fact]
    public void TheSplashIsDispatchedAsWatersplashAndSlimeIsOneFlagBit()
    {
        var (effects, temps, engine) = Rig();

        effects.SplashEffect(new Vector(0f, 0f, 8f), scale: 6f, inSlime: true);

        var call = temps.Only("DispatchEffectByName");

        Assert.True(call.Arg<string>(0) == "watersplash", "by the name the shim dispatched");
        Assert.True(call.Filter.Kind == TeFilterKind.Pas, "PAS from the origin");

        TeEffectData data = call.Arg<TeEffectData>(1);

        Assert.True(data.Origin.Equals(new Vector(0f, 0f, 8f)), "at the point asked for");
        Assert.True(data.Normal.Equals(new Vector(0f, 0f, 1f)),
            "straight up, and not the surface's normal: a splash comes out of water, which is "
          + "level by definition");
        Assert.True(data.Scale == 6f, "the scale asked for, which overwrites CEffectData's 1");
        Assert.True(data.Flags == 0x1,
            "FX_WATER_IN_SLIME, shareddefs.h:608 — the whole difference between water and slime "
          + "on the client's side");

        var (water, waterTemps, _) = Rig();
        water.SplashEffect(new Vector(0f, 0f, 8f), 6f, inSlime: false);

        Assert.True(waterTemps.Only("DispatchEffectByName").Arg<TeEffectData>(1).Flags == 0,
            "and plain water carries no flag at all");

        Assert.True(engine.CountOf("Effects.SplashEffect") == 0, "nothing reached the shim");
    }

    /// <summary>
    /// THE TWO SCALES THAT LOOK LIKE A TYPO FOR EACH OTHER.
    /// <see cref="ImpactEffect"/> sends 1 by never touching it;
    /// <c>UTIL_Tracer</c> assigns <c>m_flScale = flVelocity</c>, which for the
    /// two-argument form is 0. Both are stock and they disagree on purpose.
    /// </summary>
    [Fact]
    public void TheTracerIsDispatchedAsTracerWithStocksDeclaredDefaultsWrittenOut()
    {
        var (effects, temps, engine) = Rig();

        effects.TracerEffect(new Vector(1f, 1f, 1f), new Vector(100f, 1f, 1f));

        var call = temps.Only("DispatchEffectByName");

        Assert.True(call.Arg<string>(0) == "Tracer",
            "UTIL_Tracer's own name, taken because pCustomTracerName defaults to NULL");

        TeEffectData data = call.Arg<TeEffectData>(1);

        Assert.True(data.Start.Equals(new Vector(1f, 1f, 1f)), "m_vStart is the muzzle end");
        Assert.True(data.Origin.Equals(new Vector(100f, 1f, 1f)), "m_vOrigin is where it lands");
        Assert.True(data.EntIndex == 0,
            "an entity index of zero and no attachment, which is stock's env_tracer exactly: "
          + "the streak is not coming out of a gun and has nothing to hang off");
        Assert.True(data.Scale == 0f,
            "AND THE SCALE IS ZERO, three members from an Impact that sends 1. UTIL_Tracer "
          + "assigns m_flScale = flVelocity over CEffectData's default, and flVelocity is 0 for "
          + "the two-argument form (util_shared.h:461)");
        Assert.True(data.AttachmentIndex == 0 && data.Flags == 0,
            "neither TRACER_FLAG_WHIZ nor TRACER_FLAG_USEATTACHMENT: bWhiz is false and the "
          + "attachment IS TRACER_DONT_USE_ATTACHMENT, which is the value the flag's guard tests");
        Assert.True(data.HitBox == 0, "and iParticleID is 0");

        Assert.True(engine.CountOf("Effects.TracerEffect") == 0, "nothing reached the shim");
    }

    // ---------------------------------------------------------------------
    // The three rows of Part 8's table that did NOT move, and the reason each
    // is still native. A row that quietly stopped being mentioned is the shape
    // this port is scored against, so each is asserted rather than described.
    // ---------------------------------------------------------------------

    [Fact]
    public void TheThreeRowsThatCouldNotMoveStillReachTheEngineUntouched()
    {
        var (effects, temps, engine) = Rig();

        // PaintDecal answers a bool, and this double is strict: a return nobody
        // stubbed is an error naming it rather than a silent zero. Stubbed true
        // so the forward's ANSWER is asserted too -- a forward that swallowed
        // the return would be as quiet as one that swallowed the call.
        engine.Returns("Effects.PaintDecal", true);

        // BubbleEffect: wants the entity's world-space AABB and a MASK_WATER
        // trace up out of the top of it. TraceMask has no water-only member and
        // SourceSharpEntity_t refuses the Origin() + Bounds() substitution in
        // its own comment, so the trace stays native (Part 8 says so) and a
        // native trace whose ANSWER reached C# is a second ABI slot.
        effects.BubbleEffect(new EntityHandle(3), count: 8, speed: 2f);

        // PaintDecal: the permanent projected form calls MakeInitMessage(), and
        // TeFilter carries no init-message bit on purpose. Every unnamed
        // info_projecteddecal paints permanent at level init.
        Assert.True(effects.PaintDecal(4, Vector.Zero, new Vector(0f, 90f, 0f), 64f,
                                       permanent: true, lowPriority: false),
            "and the answer comes back: PaintDecal tells its caller whether anything was "
          + "actually marked, and Decals.cs counts on it");

        // BloodEffect: RE-MEASURED BY LANE SRV, and the reason changed. The
        // violence gate is UTIL_ShouldShowBlood -- two convar reads, reachable
        // through FindConVarValue -- and NOT engine->IsLowViolence(), which
        // lives in UTIL_IsLowViolence and is on no arm of this row. What blocks
        // it is g_pGameRules->IsMultiplayer(), the amount * 5 scaling, which has
        // no managed route in either direction. BloodEffectPortabilityTests
        // asserts every clause of that against stock's own .cpp read off disk.
        effects.BloodEffect(Vector.Zero, new Vector(1f, 0f, 0f), BloodColor.Red, 50f);

        Assert.True(engine.CountOf("Effects.BubbleEffect") == 1, "the bubbles are still the shim's");
        Assert.True(engine.CountOf("Effects.PaintDecal") == 1, "the decal is still the shim's");
        Assert.True(engine.CountOf("Effects.BloodEffect") == 1, "the blood is still the shim's");

        Assert.True(temps.Count == 0,
            "and none of the three sent a temp entity from managed code. They fire today exactly "
          + "as they fired before the rewiring, which is the constraint: no intermediate state "
          + "where an effect that fires today stops firing");
    }

    // ---------------------------------------------------------------------
    // The gate the per-member tests cannot be.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Calls every member of <see cref="IEngineEffects"/> and asserts that each
    /// one did exactly one of two things: reached the inner engine, or sent a
    /// temp entity.
    ///
    /// <para>
    /// THE FAILURE THIS EXISTS FOR IS A MEMBER THAT DID NEITHER — an override
    /// left empty, or a guard that returns early on the arguments a sweep
    /// happens to pass. Seventy-eight members forwarded by hand is seventy-eight
    /// chances to drop one, and a dropped effect looks like a table that did not
    /// pair rather than like a missing line of code.
    /// </para>
    ///
    /// <para>
    /// The two members that legitimately answer neither are named, not
    /// defaulted: <c>FunnelEffect</c> refuses a sprite index of 0, which a
    /// default-argument sweep necessarily passes, and it has its own test above.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryMemberEitherMovedOrWasForwardedAndNoneDidNothing()
    {
        var members = typeof(IEngineEffects).GetMethods()
                                            .Where(m => !m.IsSpecialName)
                                            .OrderBy(m => m.Name, StringComparer.Ordinal)
                                            .ToList();

        Assert.True(members.Count == 55,
            $"IEngineEffects has fifty-five members ({members.Count} found) — if this "
          + "number moved, a member arrived and this sweep is the thing that decides whether "
          + "it was wired. It has moved twenty times (the twentieth DOWN: lane c5-particles took ClientBloodStream and ClientShowLine, CTEParticleRenderer's two effects being C# (TeParticles); the nineteenth DOWN: lane c5-particles took ClientDust, the dust puff being C# (DustPuff); the eighteenth DOWN: lane c5-particles took DustUpdate and DustDestroy, the dust motes being C# (DustVolumeSim); the seventeenth DOWN: lane c5-particles took ClientLargeFunnel, the funnel being C# (LargeFunnel); the sixteenth DOWN: lane c5-particles took ClientBloodPuff, the blood sprite's puff being C# (ImpactSprays.Blood); the fifteenth DOWN: lane c5-particles took ClientShatterSurface, the shattered pane being C# (ShardEffects); the fourteenth DOWN: lane c5-particles took ClientExplosionCore and ClientExplosionWater, fx_explosion.cpp's effects being C# (ExplosionEffects); the thirteenth DOWN: lane c5-particles took ClientGaussExplosion, the gauss discharge being C# (SparkEffects.GaussExplosion); the twelfth DOWN: lane c2-fx took TempEntPoolPlaySound, the tempent frame playing its bounce sound in C# (TempEntSounds); the eleventh DOWN: lane c2-fx took ClientDispatchEffect, whose one caller dispatches through the client's managed table now; the ninth DOWN: the C2 rope port took "
          + "RopeUpdate and RopeDestroy, the rope being C#'s now; the tenth DOWN: the C2 beam "
          + "port took the six BeamCreate* creators, DrawBeamPoints and DrawBeamQuadratic, "
          + "the beams being C#'s now): C-Phase 2 added fifteen client backends, "
          + "lane CP eight for the DT_TEParticleSystem subtree, lane C4 six for the legacy "
          + "pool, lane CE two, lane CX three for the decals it unblocked, and C-Phase 5 three "
          + "-- two for the pool's SPAWN side and the blood PUFF, which is the half of "
          + "CTEBloodSprite that was never the pool's (its two model questions went to "
          + "IEnginePrecache, where one of them already was), lane DECALS two for the "
          + "two arms of C_BaseEntity::AddDecal that closed behaviour ledger rows C2 and C3 "
          + "(its third slot, ClientEntityModelType, is a QUESTION and is on "
          + "IEngineWorldQuery), and lane FRAME two -- the write-once state "
          + "C_LocalTempEntity::Frame reads on takeover and the wind FTENT_WINDBLOWN "
          + "accelerates towards. Lane c2-sims took six away: SteamJetUpdate/Destroy, "
          + "SmokeStackUpdate/Destroy and SmokeVolumeUpdate/Destroy left with the steam jet's, "
          + "the smoke stack's and the smoke volume's native shells (their sims are C#'s, drawn "
          + "by the managed particle executor), then seven more: the six trails' updates "
          + "(SmokeTrail, DustTrail, RocketTrail, FireTrail, SporeTrail, SporeExplosion) and "
          + "TrailDestroy left with shell_trail_sim/render.cpp, then WorldTextUpdate/Destroy "
          + "when point_worldtext's layout became C#'s (its renderer is told the font page and "
          + "the bounds over IShimParticlesClient), then WindUpdate/Destroy when the client wind's "
          + "simulation became C#'s (it publishes the vector over WindPublish), then EmbersUpdate/"
          + "Destroy when env_embers' simulation did (drawn by the particle executor). Lane c2-fx took one away, ClientDispatchEffect, whose one "
          + "caller (CTEEffectDispatch's player) dispatches through the client's managed table now. "
          + "Each time this assertion is "
          + "what made forwarding them a decision rather than an omission");

        // The one member whose stock behaviour is to refuse the arguments a
        // reflective sweep can invent. Named rather than skipped silently.
        var refusesTheSweep = new HashSet<string>(StringComparer.Ordinal) { "FunnelEffect" };

        // And the one that cannot be invoked by reflection at all: its blob is
        // a ReadOnlySpan<byte>, and a ref struct cannot be boxed into an
        // object[]. Called by hand rather than skipped -- the point of this
        // sweep is that no member is exempt from being shown to do something.
        var byHand = new Dictionary<string, Action<EffectsOverTempEnts>>(StringComparer.Ordinal)
        {
            ["TempEntPlayback"] = e => e.TempEntPlayback(0, ReadOnlySpan<byte>.Empty,
                                                         TeFilter.Broadcast, 0f),
        };

        var silent = new List<string>();
        var moved = new List<string>();
        var forwarded = new List<string>();

        foreach (MethodInfo member in members)
        {
            var (effects, temps, engine) = Rig();
            engine.Strict = false;

            if (byHand.TryGetValue(member.Name, out var call))
            {
                call(effects);
            }
            else
            {
                object?[] args = member.GetParameters()
                                       .Select(p => Blank(p.ParameterType))
                                       .ToArray();

                member.Invoke(effects, args);
            }

            bool reachedEngine = engine.Calls.Any(c => c.Member.StartsWith("Effects.", StringComparison.Ordinal));
            bool sentTemp = temps.Count > 0;

            if (sentTemp)
                moved.Add(member.Name);
            else if (reachedEngine)
                forwarded.Add(member.Name);
            else if (!refusesTheSweep.Contains(member.Name))
                silent.Add(member.Name);
        }

        Assert.True(silent.Count == 0,
            "every member either sends a temp entity or reaches the engine; these did neither, "
          + "which is an effect that has stopped happening with nothing anywhere saying so: "
          + string.Join(", ", silent));

        // Seven and not eight: FunnelEffect is the one that refused the sweep,
        // and it is in refusesTheSweep above rather than in either list.
        Assert.True(moved.Count == 7,
            "seven members answered from managed temp entity tables (FunnelEffect is the "
          + "eighth and refuses a zero sprite index): " + string.Join(", ", moved));

        foreach (string expected in new[]
        {
            "SparkEffect", "ExplosionEffect", "MuzzleFlashEffect", "DustEffect",
            "ImpactEffect", "SplashEffect", "TracerEffect",
        })
        {
            Assert.True(moved.Contains(expected),
                $"{expected} is a Part 8 row and is answered over ITempEnts");
        }

        Assert.True(forwarded.Count == 47,
            "and forty-seven reached the engine unchanged — fifty-five less the seven "
          + "that moved and the one that refused the sweep: " + forwarded.Count + " found");

        foreach (string expected in new[] { "BubbleEffect", "PaintDecal", "BloodEffect" })
        {
            Assert.True(forwarded.Contains(expected),
                $"{expected} is the Part 8 row that could not move, and it still reaches the "
              + "engine — a row of that table doing nothing at all would be worse than one that "
              + "did not move");
        }
    }

    /// <summary>
    /// The name-taking <c>DispatchEffect</c> resolves through the ENGINE's
    /// string table and adds nothing of its own — and the index-taking overload
    /// is still there, because the native forwards call it with an index they
    /// have already resolved.
    /// </summary>
    [Fact]
    public void ANamedDispatchAsksTheEngineForTheIndexAndSendsExactlyThatNumber()
    {
        var engine = TestServices.Fake();
        engine.Returns("Effects.TempEntRegister", 11);
        engine.Returns("Effects.EffectDispatchIndex", 37);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ServerGame, engine);

        var temps = new SourceSharp.Server.TempEnts(new RecordingLog(), registry, engine);

        temps.DispatchEffect(TeFilter.Broadcast, 0f, "GlassImpact", new TeEffectData());

        Assert.True(engine.CountOf("Effects.EffectDispatchIndex") == 1,
            "the name went to the engine's own numbering — g_pStringTableEffectDispatch->"
          + "AddString — and not to a managed dictionary, because two numberings of one table "
          + "play the WRONG effect rather than none");
        Assert.True(engine.Last("Effects.EffectDispatchIndex")!.Value.ArgAs<string>(0) == "GlassImpact",
            "under the name the caller asked for");

        byte[] blob = engine.Last("Effects.TempEntPlayback")!.Value.ArgAs<byte[]>(4);

        Assert.True(BitConverter.ToInt32(blob, 72) == 37,
            "and the index the engine answered goes on the wire verbatim, with no +1 — the "
          + "client reads the NAME back out by this number");
    }

    // ---------------------------------------------------------------------

    /// <summary>
    /// A default-shaped value for a parameter, for the reflective sweep. Not
    /// meaningful — the sweep asserts that a member DID something, never what.
    /// </summary>
    private static object? Blank(Type t)
        => t == typeof(string) ? string.Empty
         : t == typeof(TeClass) ? new TeClass("x", "DT_x", null, 0, false, typeof(object), Array.Empty<TeField>())
         : t.IsValueType ? Activator.CreateInstance(t)
         : null;

    private static (EffectsOverTempEnts Effects, RecordingTempEnts Temps, RecordingEngine Engine) Rig()
    {
        var engine = TestServices.Fake();
        var temps = new RecordingTempEnts();

        return (new EffectsOverTempEnts(engine, temps, engine, engine), temps, engine);
    }

    /// <summary>One call an <see cref="ITempEnts"/> was asked to make.</summary>
    internal readonly record struct TempEntCall(string Member, TeFilter Filter, float Delay, object?[] Args)
    {
        public T Arg<T>(int index) => Args[index] is T value ? value : default!;
    }

    /// <summary>
    /// A recording <see cref="ITempEnts"/>. Derived from
    /// <see cref="TempEntsDouble"/> for the reason that class exists — the
    /// interface grows one lane at a time, and a double written out in full
    /// stops compiling the moment two lanes meet.
    ///
    /// <para>
    /// The two <c>DispatchEffect</c> overloads are recorded under DIFFERENT
    /// names, because which one a caller reached is the assertion: the rewiring
    /// must go by NAME, and the index-taking overload is what the native
    /// forwards still use.
    /// </para>
    /// </summary>
    internal sealed class RecordingTempEnts : TempEntsDouble
    {
        private readonly List<TempEntCall> _calls = new();

        public int Count => _calls.Count;

        public int CountOf(string member) => _calls.Count(c => c.Member == member);

        public TempEntCall Only(string member)
        {
            var matching = _calls.Where(c => c.Member == member).ToList();

            Assert.True(matching.Count == 1,
                $"exactly one {member} was sent ({matching.Count} of them, out of "
              + $"{string.Join(", ", _calls.Select(c => c.Member))})");

            return matching[0];
        }

        public override void Sparks(TeFilter filter, float delay, Vector position,
                                    int magnitude, int trailLength, Vector? direction)
            => _calls.Add(new("Sparks", filter, delay,
                              new object?[] { position, magnitude, trailLength, direction }));

        public override void Explosion(TeFilter filter, float delay, Vector position, int modelIndex,
                                       float scale, int frameRate, int flags, int radius,
                                       int magnitude, Vector? normal = null,
                                       byte materialType = (byte)'C')
            => _calls.Add(new("Explosion", filter, delay,
                              new object?[] { position, modelIndex, scale, frameRate, flags,
                                              radius, magnitude, normal, materialType }));

        public override void MuzzleFlash(TeFilter filter, float delay, Vector start,
                                         QAngle angles, float scale, int type)
            => _calls.Add(new("MuzzleFlash", filter, delay,
                              new object?[] { start, angles, scale, type }));

        public override void LargeFunnel(TeFilter filter, float delay, Vector position,
                                         int modelIndex, int reversed)
            => _calls.Add(new("LargeFunnel", filter, delay,
                              new object?[] { position, modelIndex, reversed }));

        public override void Dust(TeFilter filter, float delay, Vector position, Vector direction,
                                  float size, float speed)
            => _calls.Add(new("Dust", filter, delay,
                              new object?[] { position, direction, size, speed }));

        public override void DispatchEffect(TeFilter filter, float delay, string effectName,
                                            in TeEffectData data)
            => _calls.Add(new("DispatchEffectByName", filter, delay,
                              new object?[] { effectName, data }));

        public override void DispatchEffect(TeFilter filter, float delay, int effectNameIndex,
                                            in TeEffectData data)
            => _calls.Add(new("DispatchEffectByIndex", filter, delay,
                              new object?[] { effectNameIndex, data }));
    }
}
