//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

// The small effects, as C# rather than as a native shim.
//
// plan_tmp.md PART 8, "Rewiring the callers": IEngineEffects keeps its call
// sites unchanged and its IMPLEMENTATION moves from the native shims in
// shell_worldquery.cpp to this class, over ITempEnts. Every entity in
// Entities/Effects and Entities/Env goes on asking for exactly what it asked
// for before; what changed is that eight of those asks no longer cross the ABI
// to reach a temp entity, because the table on the other side is managed now.
//
// WHY A DECORATOR AND NOT A REPLACEMENT. Fifty-three members, eight of which
// Part 8 names. The other forty-five are drawing primitives, traces, particle
// systems and per-entity effect handles that have nothing to do with temp
// entities and no managed table behind them; forwarding them is not a stopgap,
// it is the correct answer.
//
// THE FORWARDING IS IN EngineEffectsForwarder (shared source). SideAuditTests
// Audit 2 fails any IEngine* member this assembly reaches that the SERVER leaves
// null, and spelling `=> _engine.RopeUpdate(...)` out here would name thirty-five
// client-only members that can never be reached from here. The audit treats a
// pure forwarder as pass-through (SideReach), so the forwarder's one-line
// forwards are not uses; what this class overrides and calls is. That set IS
// the eight, which is the thing Audit 2 was trying to ask about.
//
// WHAT DID NOT MOVE, AND WHY — because a row of Part 8's table that quietly
// stopped being mentioned is exactly the shape this port is scored against:
//
//   BubbleEffect  MOVED in plan_server_native_v2 T4f. It stayed native for its
//   PaintDecal    traces: the bubbles' MASK_WATER line up out of the volume, and
//                 the decal's short line whose trace_t named the entity, its
//                 model and the position in its frame. Managed traces through
//                 IEngineTrace::TraceRay over the managed filter now and reads
//                 the engine's trace_t itself, so both are answered below: the
//                 bubbles over ITempEnts.Bubbles, the decal over
//                 ITempEnts.ProjectDecal / BSPDecal and IVEngineServer::
//                 StaticDecal. The projected form's MakeInitMessage was never
//                 read by the engine's PlaybackTempEntity (it copies only the
//                 recipients), so the TeFilter's missing init bit costs nothing.
//
//   BloodEffect   its three arms are UTIL_BloodStream, UTIL_BloodDrips and
//                 UTIL_BloodSpray. RE-MEASURED BY LANE SRV, and the sentence
//                 that used to be here was wrong in a way worth keeping visible:
//                 it said the gate was "two violence convars and
//                 engine->IsLowViolence". It is not. UTIL_ShouldShowBlood
//                 (util_shared.cpp:1277) reads violence_hblood or
//                 violence_ablood and returns; engine->IsLowViolence() is in
//                 UTIL_IsLowViolence, a DIFFERENT function that no arm of this
//                 row calls. So the low-violence half was reachable all along --
//                 FindConVarValue -- and the roadmap's question ("is the
//                 server's UTIL_IsLowViolence equivalent reachable?") does not
//                 have a bearing on this row at all.
//
//                 WHAT ACTUALLY BLOCKS IT is one term nobody had named:
//                 g_pGameRules->IsMultiplayer(), which multiplies the blood
//                 amount by five. There is no managed route to it in either
//                 direction -- the ABI has no gamerules query, and
//                 SourceSharp.Server.IGameRules is the shell asking C#, not C#
//                 asking the shell. It is `return true` in this build because
//                 CSourceSharpGameRules derives from CMultiplayRules, which is
//                 a fact about shell_gamerules.h and not about blood: hardcoding
//                 it here would be right today for a reason stated in another
//                 file, and would go silently wrong the day that base changed.
//                 Lane CD's rule, exactly.
//
//                 The German substitution (g_Language is a replicated ConVar),
//                 the BLOOD_COLOR_MECH sparks-and-smoke branch and the
//                 bloodimpact/bloodspray dispatches are all reachable; two new
//                 ITempEnts members (BloodStream, Smoke) would be needed and
//                 neither would cost an ABI slot. ONE bool is the whole of what
//                 is left, and BloodEffectPortabilityTests asserts every clause
//                 of this paragraph against stock's own .cpp read off disk --
//                 including that the ABI still has no IsMultiplayer, so the day
//                 one arrives a test says this row is now portable.
//
// Those three are reported to the integrator as design questions, not taken as
// slots. Nothing about them changed in this commit: they fire today exactly as
// they fired before it.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// <see cref="IEngineEffects"/> over <see cref="ITempEnts"/> — plan_tmp.md
/// Part 8's rewiring.
///
/// <para>
/// It DECORATES the real engine binding rather than replacing it: the eight
/// members Part 8 names are answered here, in managed code, over managed temp
/// entity tables, and every other member is inherited from
/// <see cref="EngineEffectsForwarder"/> and reaches the engine unchanged. A
/// member that stopped being either would be a member that silently does
/// nothing, so <c>EffectsRewiringTests</c> calls all fifty-three and asserts
/// which of the two happened to each.
/// </para>
///
/// <para>
/// EACH REWIRED MEMBER CARRIES ITS SHIM'S DECISIONS AND ITS SHIM'S COMMENTS.
/// Which recipient filter, whether a sound is made, what a NULL direction means,
/// and — the expensive one — that <c>SparkEffect</c> must not call
/// <c>DoSpark</c>. Those were discovered once each, at a cost; moving the code
/// without them would mean discovering them again.
/// </para>
/// </summary>
public sealed class EffectsOverTempEnts : EngineEffectsForwarder
{
    private readonly ITempEnts _temps;
    private readonly IEngineSound _sound;
    private readonly IEngineEntities _entities;
    private readonly IEffectsWorld _world;
    private readonly Action<string> _warning;

    /// <param name="engine">
    /// The REAL engine binding, which every member this class does not override
    /// is forwarded to. Never this object: a decorator that forwarded to its own
    /// contract would be an infinite loop the moment anything asked for a rope.
    /// </param>
    /// <param name="world">The traces, the static decal and the sprite precache
    /// <see cref="PaintDecal"/> and <see cref="BubbleEffect"/> ask for.</param>
    /// <param name="warning">Where a refused decal says so.</param>
    public EffectsOverTempEnts(IEngineEffects engine, ITempEnts temps,
                               IEngineSound sound, IEngineEntities entities,
                               IEffectsWorld world, Action<string> warning)
        : base(engine)
    {
        _temps = temps;
        _sound = sound;
        _entities = entities;
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _warning = warning ?? throw new ArgumentNullException(nameof(warning));
    }

    //=========================================================================
    // PART 8's TABLE. Eight rows, in the order Part 8 lists them.
    //=========================================================================

    /// <summary>
    /// A shower of sparks. Part 8: <c>TempEnts.Sparks</c> plus the existing
    /// <c>DoSpark</c> sound.
    ///
    /// <para>
    /// NOT <c>DoSpark</c>, THE HELPER — and this is the note that must travel.
    /// <c>CEffects::Sparks</c>' neighbour <c>DoSpark</c> takes the sparking
    /// ENTITY and calls <c>EmitSound</c> on it without a null check, and a
    /// managed <c>env_spark</c> is on the parallel hierarchy: there is no
    /// <c>CBaseEntity</c> to hand it. Passing NULL segfaulted the server the
    /// first time one sparked audibly. The two halves it does are both reachable
    /// directly, and the sound wants to come from the POSITION anyway rather
    /// than from an entity — which is what <c>SOUND_FROM_WORLD</c> means, and
    /// what <see cref="IEngineSound.EmitSoundAtPosition"/> is.
    /// </para>
    ///
    /// <para>
    /// PVS AND NOT PAS, which is <c>CEffectsServer::Sparks</c>'s own choice: the
    /// sparks are a thing you SEE. Its sound is a separate call with a separate
    /// filter, and that one is PAS with attenuation.
    /// </para>
    /// </summary>
    public override void SparkEffect(Vector origin, int magnitude, int trailLength,
                            bool makeSound = true, Vector? direction = null)
    {
        // The null direction is passed STRAIGHT THROUGH and not substituted
        // here. ITempEnts.Sparks writes vec3_origin for it, which is stock's
        // "no particular direction" rather than a direction of zero length that
        // would have to be special-cased downstream (te_sparks.cpp:84) — and
        // substituting in both places would be two opinions about one default.
        _temps.Sparks(TeFilters.Pvs(origin), 0f, origin, magnitude, trailLength, direction);

        if (!makeSound)
            return;

        _sound.EmitSoundAtPosition("DoSpark", origin);
    }

    /// <summary>
    /// The fireball. Part 8: <c>TempEnts.Explosion</c>.
    ///
    /// <para>
    /// THE FILTER IS THE WHOLE RISK AND IT IS NOT A PARAMETER. It is a PAS query
    /// around the blast, built from the origin, and it is built here for the
    /// reason the shim built it there: an explosion written to an EMPTY filter
    /// is silently invisible — the damage lands, the scorch mark appears, the
    /// sound plays for nobody and no fireball is ever drawn. This mod shipped
    /// precisely that bug once.
    /// </para>
    ///
    /// <para>
    /// <paramref name="flags"/> is <c>TE_EXPLFLAG_*</c> and NOT the entity's
    /// spawnflags. The two number different bits and the translation lives in
    /// <c>EnvExplosion.EffectFlags</c>.
    /// </para>
    ///
    /// <para>
    /// The normal and the material type are left at their defaults, which is
    /// what the shim did by not passing them:
    /// <c>ITempEntsSystem::Explosion</c> declares <c>const Vector *normal =
    /// NULL</c> and <c>unsigned char materialType = 'C'</c>, and
    /// <c>te_explosion.cpp:126</c> turns the NULL into <c>(0,0,1)</c> — UP —
    /// rather than a zero-length normal. <see cref="ITempEnts.Explosion"/>
    /// declares the same two defaults and does the same substitution.
    /// </para>
    /// </summary>
    public override void ExplosionEffect(Vector origin, int modelIndex, float scale, int frameRate,
                                int flags, int radius, int magnitude)
        => _temps.Explosion(TeFilters.Pas(origin), 0f, origin, modelIndex, scale, frameRate,
                            flags, radius, magnitude);

    /// <summary>
    /// A muzzle flash at a point, pointed somewhere. Part 8:
    /// <c>TempEnts.MuzzleFlash</c>.
    ///
    /// <para>
    /// THE REASON THIS EFFECT COULD CROSS AND ITS NEIGHBOURS COULD NOT. Every
    /// other row in the particle/trail/smoke family delivers itself by BEING a
    /// networked entity whose stock client class runs an emitter, and a managed
    /// entity never appears on the wire as that class. This one is a temp entity
    /// carrying a position and a set of angles which the client reads straight
    /// out of the message — no entity index, nothing to resolve, nothing that
    /// can be NULL for a managed entity.
    /// </para>
    ///
    /// <para>
    /// The angles arrive as pitch/yaw/roll in the order <c>QAngle</c> stores
    /// them, which is the order C#'s <see cref="Vector"/> already holds an
    /// entity's angles in — so <see cref="QAngle.FromVector"/> is a relabelling
    /// and not a conversion. PVS, which is
    /// <c>CEffectsServer::MuzzleFlash</c>'s filter.
    /// </para>
    /// </summary>
    public override void MuzzleFlashEffect(Vector origin, Vector angles, float scale, int type = 1)
        => _temps.MuzzleFlash(TeFilters.Pvs(origin), 0f, origin, QAngle.FromVector(angles),
                              scale, type);

    /// <summary>
    /// The funnel of sprites that converges on — or flies out of — a point.
    /// Part 8: <c>TempEnts.LargeFunnel</c>.
    ///
    /// <para>
    /// BROADCAST RATHER THAN PAS, which is stock's own choice and is not an
    /// oversight: a funnel is large, silent and meant to be seen from across a
    /// map, so a filter that asks whether you could HEAR it is the wrong
    /// question.
    /// </para>
    ///
    /// <para>
    /// The sprite guard is the shim's. A model index of zero is "no model" and
    /// the effect draws nothing, so sending it costs a message and produces
    /// silence at the far end — which is indistinguishable from a table that
    /// did not pair.
    /// </para>
    /// </summary>
    public override void FunnelEffect(Vector origin, int spriteIndex, bool reversed)
    {
        if (spriteIndex <= 0)
            return;

        // An int and not a bool, because stock's m_nReversed is an int and the
        // prop is two bits wide rather than one.
        _temps.LargeFunnel(TeFilters.Broadcast, 0f, origin, spriteIndex, reversed ? 1 : 0);
    }

    /// <summary>
    /// A puff of dust, thrown along a direction. Part 8: <c>TempEnts.Dust</c> —
    /// a real <c>DT_TEDust</c> and <b>not</b> a <c>DispatchEffect("dust")</c>.
    /// <c>C_TEDust</c> is registered and waiting; substituting a dispatch would
    /// leave it unpaired and play a different effect. BEHAVIOUR LEDGER ROW S6,
    /// which was never a gap and is not one now.
    /// </summary>
    public override void DustEffect(Vector origin, Vector direction, float size, float speed)
    {
        // THE ZERO GUARD IS NOT THE NORMALISE, and the shim carried both for
        // different reasons. Normalising is what TE_Dust asserts: m_vecDirection
        // is four bits per component over an explicit [-1, 1], so a longer
        // vector is CLAMPED by the encoder and arrives pointing somewhere else
        // rather than being refused. What stock does not guard against is a ZERO
        // vector, which is what a mapper who left the angles alone hands it —
        // and Normalized answers zero for a zero, so without this the puff
        // would go nowhere at all.
        Vector unit = direction.LengthSquared < 1e-6f
            ? new Vector(0f, 0f, 1f)
            : direction.Normalized;

        _temps.Dust(TeFilters.Pvs(origin), 0f, origin, unit, size, speed);
    }

    /// <summary>
    /// The engine's own bullet impact at a point. Part 8:
    /// <c>TempEnts.DispatchEffect</c>, by the name <c>"Impact"</c>.
    ///
    /// <para>
    /// BEHAVIOUR LEDGER ROW C7 SAID THIS EFFECT NEVER PLAYED IN THIS BUILD, and
    /// it was wired by name anyway. <c>DECLARE_CLIENT_EFFECT( "Impact",
    /// ImpactCallback )</c> existed only in <c>tf/tf_fx_impacts.cpp</c>,
    /// <c>hl2/fx_hl2_impacts.cpp</c> and <c>sdk/sdk_fx_impacts.cpp</c>, none of
    /// which is in this client's build, so nothing registered the name and the
    /// client logged <c>"effect 'Impact' not found on client"</c>. Substituting
    /// a name that did resolve would have made the impact APPEAR to work and
    /// turned a tracked gap into an untracked one.
    /// </para>
    ///
    /// <para>
    /// THE ROW IS CLOSED — lane <c>fx-impact</c> — and holding the name still
    /// is what let it be closed properly:
    /// <c>sourcesharp/game/client/shell_impact_effects.cpp</c> registers a
    /// callback under this exact spelling, so every caller here started drawing
    /// with no change on this side at all. That includes
    /// <c>FuncBreakableSurf</c>, which was this method's only caller for its
    /// whole life and had never drawn anything.
    /// </para>
    ///
    /// <para>
    /// The surfaceprop is a PARAMETER, which is the shim's one difference from
    /// <c>CBaseEntity::ImpactTrace</c>. Stock reads it off the trace, which is
    /// why a shot into the back of a pane of glass leaves a concrete decal and a
    /// concrete noise — the back of the pane really is nodraw. A caller that
    /// knows better says so; a caller that does not passes a negative and gets
    /// stock's answer.
    /// </para>
    ///
    /// <para>
    /// NOTHING IS TRACED HERE, deliberately. The client re-traces
    /// start-&gt;origin itself in <c>fx_impact.cpp</c>'s <c>Impact()</c> to find
    /// the plane it decals, and the SOUND — the half this call exists for — is
    /// chosen from the surfaceprop the server SENT rather than from what that
    /// trace found: <c>PlayImpactSound</c> reads <c>nServerSurfaceProp</c> first
    /// and only falls back to the trace. A server-side trace would therefore buy
    /// nothing and cost the one thing that matters — a breakable surface stops
    /// being solid to <c>MASK_SHOT</c> the moment its first pane goes, so the
    /// re-trace would sail through the pane the shot just hit and put the impact
    /// on the wall behind it.
    /// </para>
    /// </summary>
    public override void ImpactEffect(Vector start, Vector end, int surfaceProp,
                             DamageType damageType, EntityHandle entity)
    {
        TeEffectData data = NewEffectData();

        data.Start = start;
        data.Origin = end;
        data.SurfaceProp = (short)surfaceProp;
        data.DamageType = (int)damageType;
        data.HitBox = 0;
        data.EntIndex = SoundSourceIndex(entity);

        Dispatch("Impact", in data);
    }

    /// <summary>
    /// A splash of water at a point. Part 8: <c>TempEnts.DispatchEffect</c>, by
    /// the name <c>"watersplash"</c>.
    ///
    /// <para>
    /// The normal is <c>(0,0,1)</c> and not the surface's: a splash comes
    /// straight up out of water, which is level by definition.
    /// <c>FX_WATER_IN_SLIME</c> is <c>shareddefs.h:608</c>'s <c>0x1</c>, and it
    /// is the whole difference between water and slime on the client's side.
    /// </para>
    /// </summary>
    public override void SplashEffect(Vector origin, float scale, bool inSlime = false)
    {
        TeEffectData data = NewEffectData();

        data.Origin = origin;
        data.Normal = new Vector(0f, 0f, 1f);
        data.Scale = scale;
        data.Flags = inSlime ? FxWaterInSlime : 0;

        Dispatch("watersplash", in data);
    }

    /// <summary>
    /// One bullet streak, drawn between two points. Part 8:
    /// <c>TempEnts.DispatchEffect("Tracer")</c>.
    ///
    /// <para>
    /// <c>UTIL_Tracer</c>'s two-argument form, with its DECLARED DEFAULTS
    /// written out (<c>util_shared.h:461</c>): entity index 0, attachment
    /// <c>TRACER_DONT_USE_ATTACHMENT</c>, velocity 0, no whiz, no custom name,
    /// particle id 0. An entity index of zero and no attachment is stock's
    /// <c>env_tracer</c> exactly — the streak is not coming out of a gun and has
    /// nothing to hang off.
    /// </para>
    ///
    /// <para>
    /// THE SCALE IS ZERO AND THAT IS NOT AN OMISSION. <c>CEffectData</c>'s
    /// constructor sets <c>m_flScale = 1.f</c> and every other field to zero;
    /// <c>UTIL_Tracer</c> then assigns <c>m_flScale = flVelocity</c>, which for
    /// this overload is 0. So the tracer's scale really is 0 on the wire, while
    /// <see cref="ImpactEffect"/> three members up sends 1 by never touching it.
    /// The two look like a typo for each other and are not.
    /// </para>
    ///
    /// <para>
    /// Neither <c>TRACER_FLAG_WHIZ</c> nor <c>TRACER_FLAG_USEATTACHMENT</c> is
    /// set: <c>bWhiz</c> is false and the attachment IS
    /// <c>TRACER_DONT_USE_ATTACHMENT</c>, which is the value the flag's guard
    /// tests against.
    /// </para>
    /// </summary>
    public override void TracerEffect(Vector start, Vector end)
    {
        TeEffectData data = NewEffectData();

        data.Start = start;
        data.Origin = end;
        data.EntIndex = 0;
        data.Scale = 0f;
        data.HitBox = 0;

        Dispatch("Tracer", in data);
    }

    //=========================================================================
    // The pieces the eight share.
    //=========================================================================

    /// <summary><c>FX_WATER_IN_SLIME</c>, shareddefs.h:608.</summary>
    private const int FxWaterInSlime = 0x1;

    /// <summary>
    /// A <see cref="TeEffectData"/> as <c>CEffectData</c>'s own constructor
    /// leaves one.
    ///
    /// <para>
    /// EXACTLY ONE FIELD DIFFERS FROM ALL-ZEROES and it has already cost this
    /// port a reading: <c>m_flScale</c> starts at <b>1</b>, not 0
    /// (<c>effect_dispatch_data.h:90</c>). A caller that never assigns a scale —
    /// <c>SourceSharp_ImpactEffect</c> is one — sends a 1, and a managed
    /// <c>default(TeEffectData)</c> would silently send a 0 instead. Everything
    /// else the constructor writes is a zero, including
    /// <c>PATTACH_ABSORIGIN</c>, which is 0.
    /// </para>
    /// </summary>
    private static TeEffectData NewEffectData() => new() { Scale = 1f };

    /// <summary>
    /// <c>DispatchEffect( name, data )</c>, whole: the PAS filter built from the
    /// data's own origin, and a delay of zero.
    ///
    /// <para>
    /// The filter is stock's — <c>te_effect_dispatch.cpp:95</c> builds
    /// <c>CPASFilter( data.m_vOrigin )</c> — and so is the zero delay:
    /// <c>TE_DispatchEffect</c> calls <c>Create( filter, 0 )</c> and never reads
    /// its own <c>delay</c> parameter. Passing anything else would be an
    /// improvement, and an improvement in a rewiring is a behaviour difference
    /// with two candidate explanations.
    /// </para>
    /// </summary>
    private void Dispatch(string name, in TeEffectData data)
        => _temps.DispatchEffect(TeFilters.Pas(data.Origin), 0f, name, in data);

    /// <summary>
    /// The entity index the client attaches an effect to.
    ///
    /// <para>
    /// <c>SourceSharpEntity_t::SoundSourceIndex</c>, which is
    /// <c>entindex()</c> with a floor: an entity with no edict answers -1, and
    /// -1 is not a sound source — <c>SOUND_FROM_LOCAL_PLAYER</c> has that value
    /// on the client. Zero is <c>SOUND_FROM_WORLD</c>, and the world is what
    /// stock writes for anything the client cannot name.
    /// </para>
    /// </summary>
    private int SoundSourceIndex(EntityHandle entity)
    {
        int index = _entities.GetIndex(entity);

        return index > 0 ? index : 0;
    }

    //=========================================================================
    // THE TWO TRACED EFFECTS (plan_server_native_v2 T4f). They stayed native
    // because their traces did: the managed trace (IEngineTrace::TraceRay over
    // the managed filter) and the engine's own trace_t made both portable.
    // SourceSharp_PaintDecal and SourceSharp_BubbleEffect, line for line.
    //=========================================================================

    // bspflags.h
    private const uint MaskSolid = 0x1u | 0x4000u | 0x2u | 0x2000000u | 0x8u;
    private const uint MaskSolidBrushOnly = 0x1u | 0x4000u | 0x2u | 0x8u;
    private const uint MaskWater = 0x20u | 0x4000u | 0x10u;

    private static readonly int OriginOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.Origin));
    private static readonly int MinsOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.Mins));
    private static readonly int MaxsOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.Maxs));
    private static readonly int ModelOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.ModelIndex));
    private static readonly int TransformOffset = EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.WorldTransform));

    /// <summary>
    /// Paints one map decal, in whichever of the four forms was asked for.
    ///
    /// <para>
    /// <paramref name="angles"/> picks the form. Non-null throws the mark along
    /// that facing (info_projecteddecal), and needs no trace here because the
    /// engine's own projection does one. Null traces a short line through the
    /// position and stamps what it found (infodecal): five units either side,
    /// stock's box, deliberately tiny -- a decal is placed ON a surface by the
    /// mapper, not thrown at one.
    /// </para>
    ///
    /// <para>
    /// THE PROJECTED FORM GOES TO WHOEVER IS CONNECTED, PERMANENT OR NOT.
    /// Stock (and the native shim) called <c>MakeInitMessage()</c> on the
    /// permanent one's filter, and <c>IVEngineServer::PlaybackTempEntity</c>
    /// never reads it: the engine copies the filter's recipients into the event
    /// and nothing else. So the init bit
    /// changed nothing, and a broadcast is the whole of the delivery.
    /// </para>
    ///
    /// <para>
    /// A FIX RIDES ALONG: both temp entity forms were dead since 5a1941ff3,
    /// which deleted the native <c>SourceSharpTe_SuppressFilter</c> guard and
    /// left its <c>return 1;</c> in front of <c>TE_ProjectDecal</c> and
    /// <c>TE_BSPDecal</c>: every projected and every sprayed decal answered
    /// "painted" and sent nothing.
    /// </para>
    ///
    /// <para>
    /// The permanent traced form is stored against the entity the trace hit and
    /// its model, in that entity's frame, so a decal on a door moves with it;
    /// an entity with no model is refused, as stock refuses it.
    /// </para>
    /// </summary>
    public override bool PaintDecal(int texture, Vector origin, Vector? angles, float projectDistance,
                                    bool permanent, bool lowPriority)
    {
        if (texture < 0)
            return false;

        if (angles is { } facing)
        {
            _temps.ProjectDecal(TeFilters.Broadcast, 0f, origin, QAngle.FromVector(facing), projectDistance, texture);
            return true;
        }

        var box = new Vector(5f, 5f, 5f);
        if (_world.Trace(origin - box, origin + box, permanent ? MaskSolid : MaskSolidBrushOnly) is not { } trace)
            return false;

        // The surface of EITHER hierarchy, by handle: index 0 is the world, and
        // anything that is not an entity at all (a static prop) answers 0 too,
        // which is the answer it always had.
        int entityIndex = trace.Hit == EntityHandle.None ? 0 : Math.Max(0, _world.EntityIndex(trace.Hit));

        if (!permanent)
        {
            // The sprayed form: one temp entity, in world space, to whoever is
            // connected. The engine works out the surface for itself.
            _temps.BSPDecal(TeFilters.Broadcast, 0f, origin, entityIndex, texture);
            return true;
        }

        Vector position = origin;
        int modelIndex = 0;

        Span<byte> block = entityIndex != 0 ? _world.Block(trace.Hit) : Span<byte>.Empty;
        if (block.Length >= TransformOffset + Unsafe.SizeOf<Transform>())
        {
            modelIndex = MemoryMarshal.Read<int>(block[ModelOffset..]);
            position = InverseTransformPoint(origin, MemoryMarshal.Read<Transform>(block[TransformOffset..]));

            if (modelIndex == 0)
            {
                (string className, string name) = _world.Names(trace.Hit);
                _warning($"[SourceSharp] decal would have hit entity {entityIndex} (class:{className}, "
                         + $"name:{name}) with modelindex 0; suppressed\n");
                return false;
            }
        }

        _world.StaticDecal(position, texture, entityIndex, modelIndex, lowPriority);
        return true;
    }

    /// <summary>
    /// Bubbles rising out of an entity's volume: <c>te->Bubbles</c> rather than
    /// <c>te->Fizz</c>, because Fizz networks an entity index the client
    /// resolves through its own list (NULL for this hierarchy) while Bubbles
    /// carries the corners themselves. The height is worked out here, once: out
    /// of the top of the volume, up a MASK_WATER line, until the water ends; a
    /// volume with no water above it gets its own height, as stock's client does.
    /// </summary>
    public override void BubbleEffect(EntityHandle entity, int count, float speed)
    {
        Span<byte> block = _world.Block(entity);
        if (block.Length < Math.Max(OriginOffset, Math.Max(MinsOffset, MaxsOffset)) + Unsafe.SizeOf<Vector>())
            return;

        // CStandaloneEntity::WorldSpaceSurroundingBounds: origin + mins .. origin + maxs, never rotated.
        Vector at = MemoryMarshal.Read<Vector>(block[OriginOffset..]);
        Vector mins = at + MemoryMarshal.Read<Vector>(block[MinsOffset..]);
        Vector maxs = at + MemoryMarshal.Read<Vector>(block[MaxsOffset..]);

        // The bubble's own sprite, not the brush's model: the brush is invisible.
        int sprite = _world.PrecacheModel("sprites/bubble.vmt");
        if (sprite <= 0)
            return;

        var top = new Vector((mins.X + maxs.X) * 0.5f, (mins.Y + maxs.Y) * 0.5f, maxs.Z);
        float height = maxs.Z - mins.Z;

        if (_world.Trace(top, top + new Vector(0f, 0f, 1024f), MaskWater) is { } trace && trace.Trace.Fraction < 1f)
            height = trace.Trace.EndPos.Z - mins.Z;

        _temps.Bubbles(TeFilters.Broadcast, 0f, mins, maxs, height, sprite, count, speed);
    }

    /// <summary>
    /// UTIL_BubbleTrail: the water surface above each end (UTIL_FindWaterSurface:
    /// a MASK_WATER line from 256 above down to the point, which answers the
    /// point's own Z for one out of the water); nothing when both ends are dry;
    /// one end dry, the line cut where it meets the surface and the count cut to
    /// the wet part; the trail to everyone, at speed 8.
    /// </summary>
    public override void BubbleTrailEffect(Vector from, Vector to, int count)
    {
        float startHeight = WaterSurface(from) - from.Z;
        float endHeight = WaterSurface(to) - to.Z;
        if (startHeight == 0f && endHeight == 0f)
            return;

        float waterZ = startHeight + from.Z;
        Vector wetFrom = from, wetTo = to;
        if (startHeight == 0f || endHeight == 0f)
        {
            if (startHeight == 0f)
                waterZ = endHeight + to.Z;

            // IntersectRayWithAAPlane on z, and the count by the wet length.
            float t = to.Z != from.Z ? (waterZ - from.Z) / (to.Z - from.Z) : 0f;
            Vector waterPoint = from + (to - from) * t;
            if (startHeight == 0f)
            {
                wetFrom = waterPoint;
                count = (int)(count * (1f - t));
            }
            else
            {
                wetTo = waterPoint;
                count = (int)(count * t);
            }
        }

        int sprite = _world.PrecacheModel("sprites/bubble.vmt");
        if (sprite <= 0)
            return;

        _temps.BubbleTrail(TeFilters.Broadcast, 0f, wetFrom, wetTo, waterZ, sprite, count, 8f);
    }

    /// <summary>UTIL_FindWaterSurface( position, position.z, position.z + 256 ).</summary>
    private float WaterSurface(Vector position)
    {
        var top = new Vector(position.X, position.Y, position.Z + 256f);
        return _world.Trace(top, position, MaskWater) is { } trace ? trace.Trace.EndPos.Z : position.Z;
    }

    /// <summary>
    /// <c>VectorITransform( in, matrix, out )</c> (mathlib.cpp): the point in
    /// the frame <paramref name="frame"/> maps to the world, for a frame whose
    /// rotation is orthonormal.
    /// </summary>
    internal static Vector InverseTransformPoint(Vector point, Transform frame)
    {
        float x = point.X - frame.M03;
        float y = point.Y - frame.M13;
        float z = point.Z - frame.M23;

        return new Vector(
            x * frame.M00 + y * frame.M10 + z * frame.M20,
            x * frame.M01 + y * frame.M11 + z * frame.M21,
            x * frame.M02 + y * frame.M12 + z * frame.M22);
    }
}
