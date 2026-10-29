//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Hurting everything near a point, the way an explosion does.
///
/// <para>
/// <b>An interface because the seam moved.</b> Before this lane, an explosion's
/// traversal was one outbound ABI call — <c>Damage.ExplosionDamage</c> — and a
/// test could read what an entity asked for straight out of the engine double's
/// call log. The traversal is managed now, so the call log records the sweep's
/// conversation with the engine rather than the request that started it, and
/// the request needs a seam of its own or facts like "a rocket's blast is not
/// stopped by the rocket" have nowhere left to look.
/// </para>
/// </summary>
public interface IBlastSweep
{
    /// <inheritdoc cref="BlastSweep.Apply"/>
    void Apply(Vector origin, float magnitude, float radius,
               DamageType damageType,
               EntityHandle attacker,
               EntityHandle inflictor,
               EntityHandle ignored,
               float explicitForce = 0f);
}

/// <summary>
/// AN EXPLOSION LOOKING FOR THINGS TO HURT — the traversal that
/// <c>CGameRules::RadiusDamage</c> was, with the world asked from managed code
/// instead of from C++.
///
/// <para>
/// <b>What moved and what did not.</b> The decisions moved: who is in range,
/// whether the blast can see them, what stands in the way and how much of the
/// blast it keeps, how far the damage falls off, and whether a blast crosses
/// the surface of the water. What stayed native is the four traces themselves,
/// the sphere query and the delivery of damage — mechanism the engine owns,
/// reached through ABI slots that already existed. This lane added no new
/// engine-API function to make the move; see the two mask requests in
/// <c>.claude/entity-port/abi-requests/policy-rules.md</c> for the one thing it
/// could not express exactly.
/// </para>
///
/// <para>
/// <b>The crossing shape, stated because the wave asked for it.</b> Before,
/// managed code made ONE outbound call per explosion and the loop ran natively.
/// Now managed code makes the loop and issues roughly <c>1 trace + 1 damage
/// call</c> per victim it does not immediately reject, plus one point-contents
/// call and one sphere query for the whole blast. Every one of those is
/// OUTBOUND. The live-gate budget counts INBOUND crossings, so no budget row
/// moves; the measured per-op numbers are in this lane's handover.
/// </para>
///
/// <para>
/// <b>Four arms of the native body are gone, and each is gone because it was
/// already unreachable — not because it was inconvenient.</b>
/// </para>
/// <list type="number">
///   <item><b>The robust-radius deflection.</b> Guarded by
///   <c>ShouldUseRobustRadiusDamage</c>, which <c>shell_gamerules.h:196</c>
///   defines as <c>{ return false; }</c> with no override anywhere in this
///   build. Its two extra traces never ran.</item>
///   <item><b>The faction skip</b> (<c>iClassIgnore</c>). Both native entry
///   points pass <c>CLASS_NONE</c>, and the guard is
///   <c>iClassIgnore != CLASS_NONE &amp;&amp; …</c>. Nothing in this mod is an
///   NPC with a faction.</item>
///   <item><b><c>DispatchTraceAttack</c> versus <c>TakeDamage</c>.</b> A
///   victim the trace reached takes the traced hit only if it answers one
///   itself (<see cref="ITraceAttacked"/>, a pane of glass); for everything
///   else the two are the same call, as <c>anyentity.h</c> made them, because
///   hitgroups and armour are not on that side.</item>
///   <item><b><c>TraceAttackToTriggers</c></b> is walked along each victim's
///   line, over the triggers that answer a shot (broken glass).</item>
/// </list>
///
/// <para>
/// <b>The water muffle is not ported and is not missing.</b> Stock's "only
/// muffle the explosion if deeper than 2 feet in water" sits inside
/// <c>#ifdef HL2_DLL</c>, and neither <c>server_csharp.vpc</c> nor
/// <c>client_csharp.vpc</c> defines <c>HL2_DLL</c> — checked with grep, not
/// remembered. The arm has never been compiled into this mod.
/// </para>
/// </summary>
public sealed class BlastSweep : IBlastSweep
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IEngineWorldQuery _world;
    private readonly IEngineDamage _damage;
    private readonly IEngineEntities _entities;
    private readonly IEngineEntityPhysics _physics;
    private readonly DamageInflictors? _inflictors;

    /// <summary>What was hit, asked whether it answers a traced hit itself (DispatchTraceAttack).</summary>
    private readonly IEntityDirectory? _directory;

    /// <summary>The triggers that answer a shot, walked along each victim's line (TraceAttackToTriggers).</summary>
    private readonly IEntityRoster<ITraceAttacked>? _traceAttacked;
    private readonly IEngineConsole _console;
    private readonly IServerSoundEnt _sounds;

    /// <summary>
    /// <b>A registered service and not a static.</b> The sweep needs five
    /// engine areas — the world for the traces and the sphere query, damage to
    /// deliver the hit, the entity area for each victim's engine block, physics
    /// for a blocker's mass, and the console for one replicated ConVar. Handing
    /// five interfaces to every caller of an explosion would put the sweep's
    /// dependencies in the signature of every entity that can blow up; one
    /// injected object is what <c>EntityMotion</c> and <c>Flames</c> already
    /// do for the same reason.
    /// </summary>
    public BlastSweep(ILog log, IEngineWorldQuery world, IEngineDamage damage,
                      IEngineEntities entities, IEngineEntityPhysics physics,
                      IEngineConsole console, IServerSoundEnt sounds,
                      DamageInflictors? inflictors = null, IEntityDirectory? directory = null,
                      IEntityRoster<ITraceAttacked>? traceAttacked = null)
    {
        _directory = directory;
        _traceAttacked = traceAttacked;
        _inflictors = inflictors;
        _sounds = sounds;
        _log = log;
        _world = world;
        _damage = damage;
        _entities = entities;
        _physics = physics;
        _console = console;
    }

    /// <summary>
    /// <c>MASK_WATER</c> from <c>bspflags.h:112</c> —
    /// <c>CONTENTS_WATER | CONTENTS_MOVEABLE | CONTENTS_SLIME</c>, which is
    /// <c>0x20 | 0x4000 | 0x10</c>.
    ///
    /// <para>
    /// Spelled as the mask and not as <see cref="Contents.Water"/>, which is
    /// <c>CONTENTS_WATER</c> alone: reading "am I in liquid" off that bit would
    /// answer false in slime, and stock's radius damage asks the wider
    /// question. (<c>env_explosion</c>'s spark shower asks the narrower one and
    /// keeps <see cref="Contents.Water"/>; the two really are different
    /// questions.)
    /// </para>
    /// </summary>
    public const int WaterMask = 0x20 | 0x4000 | 0x10;

    /// <summary>
    /// How many entities one blast will consider. The engine's sphere query
    /// fills a caller-sized buffer, so this is the ceiling on a blast's victim
    /// count and not a tuning value: stock's own <c>MAX_SPHERE_QUERY</c> is 512
    /// and a 256-unit blast in this mod has never approached it.
    /// </summary>
    public const int MaxVictims = 512;

    /// <summary>
    /// Stock's <c>vecSrc.z += 1</c> — "in case grenade is lying on the ground".
    /// Named because it is the difference between a blast placed exactly on a
    /// floor brush and one that can trace out of it.
    /// </summary>
    public const float GroundLift = 1f;

    /// <summary>
    /// Hurt everything the blast can reach.
    /// </summary>
    /// <param name="origin">Where it went off, before <see cref="GroundLift"/>.</param>
    /// <param name="magnitude">Damage at the centre.</param>
    /// <param name="radius">Where the damage reaches zero.</param>
    /// <param name="attacker">Who is credited with the kill.</param>
    /// <param name="inflictor">
    /// The thing that exploded, if it is an entity. It is what the blast's four
    /// traces PASS THROUGH, and getting it wrong is the defect
    /// <see cref="BlastRules"/> describes: without it the first trace starts
    /// inside the exploding entity's own hull, stops at fraction zero, and
    /// every victim in range is scored at distance zero and takes the full
    /// magnitude.
    /// </param>
    /// <param name="ignored">
    /// An entity the blast neither hurts NOR is blocked by — what an entity
    /// that explodes where it stands passes for itself. It takes precedence
    /// over <paramref name="inflictor"/> as the thing traced through, which is
    /// stock's <c>pTraceIgnore ? pTraceIgnore : info.GetInflictorHandle()</c>.
    /// </param>
    /// <param name="explicitForce">
    /// A damage force the caller supplied, or zero. See the refusal in the
    /// body: this build cannot carry an explicit force through the damage ABI,
    /// and says so out loud rather than quietly manufacturing a different one.
    /// </param>
    public void Apply(Vector origin, float magnitude, float radius,
                      DamageType damageType,
                      EntityHandle attacker,
                      EntityHandle inflictor,
                      EntityHandle ignored,
                      float explicitForce = 0f)
    {
        // A zero radius damages nothing rather than everything: the falloff
        // divides by the radius, and a map that left the key unset must not be
        // read as asking to hurt the whole level. Both native entry points
        // guarded this before reaching the rule and the guard comes with it.
        if (radius <= 0f)
            return;

        // What the blast's line of sight passes through, which is a different
        // question from who it declines to hurt.
        EntityHandle passThrough = ignored != EntityHandle.None ? ignored : inflictor;

        bool blastInWater = ((int)_world.PointContents(origin) & WaterMask) != 0;

        Vector source = origin + new Vector(0f, 0f, GroundLift);

        if (explicitForce != 0f)
        {
            // DECLARED, NOT SILENTLY DROPPED. Stock decays a caller-supplied
            // force by the falloff and points it away from the blast; the
            // damage ABI manufactures its force from the base damage and has
            // nowhere to carry one. See ContractBaseline.Refusals.
            _log.Msg("[SourceSharp/Rules] blast at " + origin +
                    " asked for an explicit damage force of " +
                    explicitForce.ToString("F1") +
                    "; this build manufactures the force from the damage instead\n");
        }

        bool legacyBlocking = LegacyBlocking();

        Span<EntityHandle> nearby = stackalloc EntityHandle[MaxVictims];
        int count = _world.EntitiesInSphere(source, radius, nearby);

        for (int i = 0; i < count; i++)
        {
            EntityHandle victim = nearby[i];

            // EntityHandle.None (which default is) and not the raw handle 0:
            // that is entry zero, a real entity.
            if (victim == EntityHandle.None)
                continue;

            if (ignored != EntityHandle.None && victim == ignored)
                continue;

            // Stock's CanTakeDamage. On the parallel hierarchy anyentity.h
            // answers true for everything that is not the world, and the world
            // is what this rejects: TakesDamage is the engine block's own bit
            // and the world does not carry one.
            if (!CanTakeDamage(victim))
                continue;

            var block = new BlockView(_entities, victim);

            if (!BlastRules.ReachesThroughWater(blastInWater, block.WaterLevel))
                continue;

            // Where the blast aims: the OBB centre through the collision-to-
            // world transform, which is NOT the midpoint of the surrounding
            // box. See BlastRules.BodyTarget.
            Vector spot = block.BodyTarget(victim);

            // THE TRACE THE WHOLE THING TURNS ON.
            TraceResult tr = _world.TraceLine(source, spot, TraceMask.ShotHull, passThrough);

            float blockedShare = 0f;

            if (legacyBlocking)
            {
                // old_radiusdamage 1: anything in the way that is not the
                // victim stops the blast outright, with no share taken.
                if (tr.Fraction != 1f && tr.Entity != victim)
                    continue;
            }
            else if (tr.Fraction != 1f)
            {
                // IsExplosionTraceBlocked, and it is one clause now rather
                // than four. Stock's other three asked whether a DOOR was in
                // the way — MOVETYPE_PUSH plus three classnames — and
                // shell_gamerules already recorded that nothing on the
                // parallel hierarchy is one, so the whole door test was dead.
                // What is left is stock's first line, `if( ptr->DidHitWorld() )
                // return true`: a wall between the blast and the victim spares
                // the victim outright, and that is the rule that stops an RPG
                // killing its own shooter through cover.
                //
                // WORLDSPAWN IS EDICT ZERO AND THAT IS THE TEST. It is NOT the
                // same as "the trace named no entity": trace_t::m_pEnt is NULL
                // for a static prop, stock reads that as NOT blocked, and
                // conflating the two would either let blasts through walls or
                // have static props start shielding. The index is asked only
                // for a victim whose line was actually interrupted, so it costs
                // nothing on a clear shot.
                if (HitTheWorld(tr.Entity))
                    continue;

                EntityHandle blockerOwner = new BlockView(_entities, tr.Entity).Owner;

                if (BlastRules.BlockerCounts(victim, tr.Entity, blockerOwner))
                {
                    // Something is in the way. If the WORLD is also in the way,
                    // no damage at all.
                    TraceResult solid = _world.TraceLine(source, spot, TraceMask.Brushes, passThrough);

                    if (solid.Fraction != 1f)
                        continue;

                    // A mass of zero means no physics object at all:
                    // EntityPhysicsMotion.Mass answers 0 for a handle with no
                    // IPhysicsObject, and EntityPhysicsMotion.SetMass refuses a
                    // mass that is not positive, so the two cases cannot be
                    // confused.
                    float mass = _physics.GetMass(tr.Entity);
                    float share = BlastRules.BlockedShare(mass > 0f, mass);

                    // Absorbed all of it. A different event from taking zero
                    // damage: no force, no trigger walk, no sound.
                    if (share >= 1f)
                        continue;

                    blockedShare = share;
                }
            }

            float distance = (source - tr.End).Length;
            float adjusted = BlastRules.AtDistance(magnitude, radius, distance);

            if (adjusted <= 0f)
                continue;

            adjusted -= adjusted * blockedShare;

            Vector dir = (spot - source).Normalized;

            // The force is manufactured natively, from the damage and the
            // direction, by CalculateExplosiveDamageForce inside
            // SourceSharp_EntityTakeDamage -- including its per-victim random
            // jitter, which stays on the native side because the managed side
            // has no business being a source of values demo playback must
            // reproduce. DMG_PREVENT_PHYSICS_FORCE suppresses it, which is
            // stock's own guard and is expressed here by handing over no
            // direction at all.
            bool wantsForce = (damageType & DamageType.PreventPhysicsForce) == 0;

            // The hit comes FROM what exploded (stock's inflictor), credited
            // to the attacker. Nobody named means the blast itself, where it went off.
            using (_inflictors?.Push(inflictor != EntityHandle.None ? inflictor : attacker, origin))
            {
                // The trace reached the victim: it takes a traced hit, which an
                // entity that answers one itself (a pane of glass) takes in place
                // of the damage (DispatchTraceAttack); otherwise TakeDamage.
                if (tr.Fraction != 1f && tr.Entity == victim && _directory?.Get(victim) is ITraceAttacked attacked)
                {
                    attacked.TraceAttack(attacker, adjusted, damageType, source, tr.End);
                }
                else
                {
                    _damage.TakeDamage(victim, attacker, adjusted, damageType,
                                      direction: wantsForce ? dir : null,
                                      forceOrigin: wantsForce ? source : default);
                }

                // Now hit all triggers along the way that respond to damage.
                if (_traceAttacked is { } triggers)
                    TraceAttacks.ToTriggers(triggers, attacker, adjusted, damageType, source, tr.End);
            }
        }

        AnnounceBlast(origin, radius, damageType, inflictor);
    }

    /// <summary>
    /// <b>A blast tells the sound ledger it happened.</b> Stock's global
    /// <c>RadiusDamage</c> (basecombatcharacter.cpp) ran the rules' sweep and
    /// then, for blast damage, inserted a combat sound flagged as an explosion:
    /// a radius of 1.5 times the blast's and never under 128 units, for a
    /// quarter of a second, owned by the inflictor, at the unlifted origin.
    /// The native copy had no caller since the sweep moved here, and went in
    /// plan_server_native_v2 T2; this is that arm, ported.
    /// </summary>
    private void AnnounceBlast(Vector origin, float radius, DamageType damageType, EntityHandle inflictor)
    {
        if ((damageType & DamageType.Blast) == 0)
            return;

        // Stock hands the float radius to an int volume, which truncates.
        _sounds.Insert((int)(SoundType.Combat | SoundType.ContextExplosion), origin,
                       (int)MathF.Max(128f, radius * 1.5f), 0.25f, inflictor);
    }

    /// <summary>
    /// Whether a trace stopped on the world itself — stock's
    /// <c>trace_t::DidHitWorld</c>, which compares against the world entity.
    ///
    /// <para>
    /// <see cref="EntityHandle.None"/> is <c>INVALID_EHANDLE_INDEX</c> and is
    /// what <c>SourceSharp_HandleFromAnyEntity</c> answers for a NULL
    /// <c>m_pEnt</c> — a static prop, or a hit on nothing that is an entity.
    /// Stock's <c>IsExplosionTraceBlocked</c> answers FALSE for that case, so
    /// it must not be folded in here.
    /// </para>
    /// </summary>
    private bool HitTheWorld(EntityHandle hit)
        => hit != EntityHandle.None && _entities.GetIndex(hit) == 0;

    /// <summary>
    /// Stock's <c>CanTakeDamage</c>, which <c>anyentity.h</c> answers as
    /// "anything that is not the world".
    ///
    /// <para>
    /// <b>NOT the engine block's TakesDamage bit</b>, deliberately. The native
    /// sweep this replaces asked <c>AnyEntity_t::CanTakeDamage</c>, whose
    /// parallel-hierarchy arm is a bare <c>return true</c> — an entity with
    /// <c>m_takedamage == DAMAGE_NO</c> is still handed the damage and declines
    /// it itself. Filtering here instead would be a behaviour change smuggled
    /// into a move, and this is exactly where one would be invisible.
    /// </para>
    /// </summary>
    private bool CanTakeDamage(EntityHandle victim) => !HitTheWorld(victim);

    /// <summary>
    /// <c>old_radiusdamage</c>, which selects the pre-Orange-Box blocking rule.
    ///
    /// <para>
    /// <b>The one native ConVar this rule still reads, and it is read by name.</b>
    /// It is declared in <c>shell_gamerules.cpp</c> because the client's stock
    /// <c>gamerules.cpp</c> declares it too and it is <c>FCVAR_REPLICATED</c>;
    /// it is not one of the fifteen gameplay ConVars the shim audit assigned to
    /// a lane. One lookup per explosion, not per victim and not per frame.
    /// </para>
    /// </summary>
    private bool LegacyBlocking()
    {
        string? value = _console.FindConVarValue("old_radiusdamage");

        // GetBool() is "the float is non-zero", and the default is the string
        // "0.0" -- so a plain int parse would refuse the default itself.
        return float.TryParse(value, System.Globalization.NumberStyles.Float,
                              System.Globalization.CultureInfo.InvariantCulture,
                              out float f) && f != 0f;
    }

    /// <summary>
    /// The three fields of an entity's engine block this rule reads, and
    /// nothing else.
    ///
    /// <para>
    /// A struct over <see cref="IEngineEntities.GetEngineBlock"/> rather than three ABI
    /// slots. The block is the fixed prefix of every entity's allocation and
    /// already carries the water level, the owner, the OBB and the
    /// collision-to-world transform, so asking native code for them would be
    /// adding a crossing to read memory managed code can already see.
    /// </para>
    ///
    /// <para>
    /// An entity with no block — or one whose block is short — answers the
    /// neutral for each field: water level 0 (dry, which is what
    /// <c>anyentity.h</c> answers for anything with no water tracking), no
    /// owner, and its own origin as the body target.
    /// </para>
    /// </summary>
    private readonly ref struct BlockView
    {
        private readonly Span<byte> _block;
        private readonly IEngineEntities _entities;

        public BlockView(IEngineEntities entities, EntityHandle entity)
        {
            _entities = entities;
            Span<byte> block = entities.GetEngineBlock(entity);
            _block = block.Length >= EngineBlock.SizeBytes ? block : Span<byte>.Empty;
        }

        private static readonly int WaterLevelOffset =
            EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.WaterLevel));
        private static readonly int OwnerOffset =
            EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.OwnerHandle));
        private static readonly int MinsOffset =
            EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.Mins));
        private static readonly int MaxsOffset =
            EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.Maxs));
        private static readonly int TransformOffset =
            EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.WorldTransform));

        public int WaterLevel =>
            _block.IsEmpty ? 0 : MemoryMarshal.Read<int>(_block.Slice(WaterLevelOffset));

        public EntityHandle Owner =>
            _block.IsEmpty
                ? EntityHandle.None
                : new EntityHandle(MemoryMarshal.Read<uint>(_block.Slice(OwnerOffset)));

        public Vector BodyTarget(EntityHandle entity)
        {
            if (_block.IsEmpty)
                return _entities.GetOrigin(entity);

            var mins = MemoryMarshal.Read<Vector>(_block.Slice(MinsOffset));
            var maxs = MemoryMarshal.Read<Vector>(_block.Slice(MaxsOffset));
            var transform = MemoryMarshal.Read<Transform>(_block.Slice(TransformOffset));

            return BlastRules.BodyTarget(transform, mins, maxs);
        }
    }
}
