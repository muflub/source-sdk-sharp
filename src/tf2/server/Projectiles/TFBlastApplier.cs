//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;
using TF2.Shared.Movement;
using TF2.Server.Combat;
using TF2.Server.Econ;
using TF2.Server.Player;

namespace TF2.Server.Projectiles;

/// <summary>
/// TF's blast, delivered: the traversal stock runs inside
/// <c>CTFGameRules::RadiusDamage</c> (<c>tf_gamerules.cpp:5625-5892</c>) with
/// the numbers <see cref="TFRadiusDamage"/> works out.
///
/// <para>
/// The core's <see cref="BlastSweep"/> is HL2's blast: its falloff is
/// magnitude/radius and its force is manufactured natively from the damage.
/// TF's is neither — the falloff comes from the weapon's bits
/// (<see cref="TFRadiusDamage.Falloff"/>), the crit rides
/// <see cref="DamageHitGroups"/> and the weapon name <see cref="TFDamageScopes"/>
/// so the damage layer (2.7) applies the multiplier, and the push is
/// <see cref="TFRadiusDamage.Push"/>, applied to the player's velocity here
/// because the core's damage path manufactures no force of its own.
/// </para>
///
/// <para>
/// The self pass is stock's second sweep (<c>:5661-5714</c>): with a
/// rocket-jump radius set the attacker is skipped in the main pass and hit
/// after it, at that radius and the launcher's base damage, so a launcher
/// with other damage or radius still jumps like the stock one. Pipes and
/// stickies pass <see cref="TFProjectileDefs.GrenadeJumpRadius"/>.
/// </para>
///
/// <para>
/// The blast attributes of the two passes — the weapon item's
/// <c>mult_dmg_falloff</c> and <c>add_health_on_radius_damage</c>, the self
/// arms <c>no_self_blast_dmg</c>, <c>blast_dmg_to_self</c> and
/// <c>mult_dmgself_push_force</c>, <c>rocket_jump_dmg_reduction</c> on the
/// attacker's own manager behind the <c>DamagedOtherPlayers</c> flag, and
/// <c>damage_force_reduction</c> on the victim's own manager in the
/// other-than-self push — are read through <see cref="AttributeHooks"/> as
/// stock's <c>CALL_ATTRIB_HOOK_*_ON_OTHER(...)</c> walks them. The precision
/// rune's arms stay deferred (<c>docs/plans/tf2/deferred.tsv</c>).
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFBlastApplier
{
    /// <summary>The engine sphere query's ceiling, as <see cref="BlastSweep.MaxVictims"/>.</summary>
    private const int MaxVictims = 512;

    /// <summary>Stock's <c>vecSrc.z += 1</c> — "in case grenade is lying on the ground".</summary>
    private const float GroundLift = 1f;

    /// <summary><c>INVALID_ITEM_DEF_INDEX</c>: stock's <c>weapon_def_index</c> for an itemless weapon.</summary>
    private const int NoItemDefIndex = -1;

    private readonly IEngineWorldQuery World;
    private readonly IEngineDamage Damage;
    private readonly IEngineEntities Entities;
    private readonly TFDamageScopes Scopes;
    private readonly DamageHitGroups HitGroups;
    private readonly DamageInflictors Inflictors;
    private readonly ITFPlayerClasses Classes;
    private readonly ILog Log;
    private readonly IEnginePlayers Players;
    private readonly ITFWeaponOwners Owners;
    private readonly AttributeHooks Hooks;
    private readonly ITFPlayerResupply Health;
    private readonly IEngineGameEvents Events;
    private readonly IEngineNetworking Networking;
    private readonly IEntityDirectory Directory;
    private readonly TFPlayerCarriers Carriers;
    private readonly TFPreroundPushFromDamageEnableConVar? PreroundPush;

    /// <summary>TFViewVectors' crouched hull size, the shape a self push folds (:10246-10252).</summary>
    private readonly Vector CrouchHullSize;

    public TFBlastApplier(IEngineWorldQuery world, IEngineDamage damage,
                          IEngineEntities entities, TFDamageScopes scopes,
                          DamageHitGroups hitGroups, DamageInflictors inflictors,
                          ITFPlayerClasses classes, ILog log,
                          IEnginePlayers players, ITFWeaponOwners owners, AttributeHooks hooks,
                          ITFPlayerResupply health, IEngineGameEvents events,
                          IEngineNetworking networking, IEntityDirectory directory,
                          TFPlayerCarriers carriers,
                          TFPreroundPushFromDamageEnableConVar? preroundPush = null)
    {
        World = world;
        Damage = damage;
        Entities = entities;
        Scopes = scopes;
        HitGroups = hitGroups;
        Inflictors = inflictors;
        Classes = classes;
        Log = log;
        Players = players;
        Owners = owners;
        Hooks = hooks;
        Health = health;
        Events = events;
        Networking = networking;
        Directory = directory;
        Carriers = carriers;
        PreroundPush = preroundPush;
        CrouchHullSize = new Vector(TFViewVectors.Params.DuckHullMax.X * 2f,
                                    TFViewVectors.Params.DuckHullMax.Y * 2f,
                                    TFViewVectors.Params.DuckHullMax.Z);
    }

    /// <summary>
    /// Hurts everything the blast reaches. <paramref name="directVictim"/> is
    /// what the projectile touched (scored at distance zero, stock's
    /// <c>GetEnemy()</c>); <paramref name="selfRadius"/> and
    /// <paramref name="selfBaseDamage"/> arm the attacker's own pass, and
    /// with them the own-pass attribute arm.
    /// </summary>
    public void Apply(Vector origin, in TFBlast blast, EntityHandle attacker, EntityHandle inflictor,
                      EntityHandle ignored, TFWeaponId weapon, string weaponName, bool critical,
                      EntityHandle directVictim = default, float selfRadius = 0f, float selfBaseDamage = 0f)
    {
        Vector source = origin + new Vector(0f, 0f, GroundLift);
        EntityHandle passThrough = ignored != EntityHandle.None ? ignored : inflictor;
        int count = 0;

        // The launcher item every hook reads, as stock's
        // CALL_ATTRIB_HOOK_*_ON_OTHER(weapon, ...).
        IAttributeProvider? item = attacker != EntityHandle.None
            ? Owners.ItemOf(Entities.GetIndex(attacker), weaponName)
            : null;

        // mult_dmg_falloff (CalculateFalloff, :5741-5744), once for both
        // passes: stock's quirk ADDS the whole mod when it is not 1.
        TFBlast active = blast;
        float falloffMod = Hooks.Float(1f, "mult_dmg_falloff", item);
        if (falloffMod != 1f)
            active = active.Falloffed(falloffMod);

        // nDamageDealt, stock's iDamageDeals: the enemy players' answered
        // damage, which scales the own-pass heal arm (:5676-5679). Beside it
        // iDamageEnemies (:5652-5655), whose count stock stamps as the
        // DamagedOtherPlayers flag only when an own pass is armed (:5673).
        float damageDealt = 0f;
        int enemiesDamaged = 0;

        if (active.Radius > 0f)
        {
            Span<EntityHandle> nearby = stackalloc EntityHandle[MaxVictims];
            count = World.EntitiesInSphere(source, active.Radius, nearby);

            for (int i = 0; i < count; i++)
            {
                EntityHandle victim = nearby[i];
                if (victim == EntityHandle.None || (ignored != EntityHandle.None && victim == ignored))
                    continue;

                // With an own pass armed the attacker is skipped here and hit
                // after the sweep (:5638-5640).
                if (selfRadius > 0f && victim == attacker)
                    continue;

                Vector center = Entities.GetWorldSpaceCenter(victim);
                Vector victimOrigin = Entities.GetOrigin(victim);

                // The sphere query is a box query, so stock retests each
                // result against the radius (CalcNearestPoint, :5642-5646):
                // the nearest point of the victim's collision box, whose
                // bounds the seam gives as offsets from its origin.
                (Vector mins, Vector maxs) = Entities.GetBounds(victim);
                if (!TFRadiusDamage.Reaches(source, active.Radius, victimOrigin + mins, victimOrigin + maxs))
                    continue;

                // Anything between but the victim themselves spares them
                // (ApplyToEntity, :5755-5788) — except what stock's filter
                // chain never blocks on: players, projectiles and the
                // attacker's friendly combat items (:5765-5769). The seam's
                // one-ignore trace cannot carry a chain, so the blockers are
                // named here instead; the friendly combat items need an
                // IsCombatItem the seam does not carry
                // (docs/plans/tf2/deferred.tsv).
                TraceResult tr = World.TraceLine(source, center, TraceMask.ShotHull, passThrough);
                if (tr.Fraction != 1f && BlocksBlast(tr.Entity, victim))
                    continue;

                float distance = TFRadiusDamage.VictimDistance(source, victim == directVictim,
                                                               Entities.IsPlayer(victim), center,
                                                               victimOrigin, tr.End);
                bool self = victim == attacker;
                float victimDamage = TFRadiusDamage.VictimDamage(active, distance, self, weapon);
                float forceDamage = victimDamage;
                float selfPushMult = 1f;
                if (self)
                {
                    forceDamage = victimDamage *= SoldierJumpScale(attacker, active.Bits);

                    // Stock's flag is zero unless an own pass was armed
                    // (:5673), so a self hit inside a sweep with no own pass
                    // keeps the reduction armed.
                    victimDamage = SelfBlastArms(item, victimDamage,
                                                 damagedOtherPlayers: selfRadius > 0f && enemiesDamaged > 0,
                                                 Entities.GetIndex(attacker));
                    selfPushMult = Hooks.Float(1f, "mult_dmgself_push_force", item);
                    ClaimBlastJump(attacker, victimDamage, active.Bits, item);
                }
                if (victimDamage <= 0f && !(self && forceDamage > 0f))
                    continue;

                // The push belongs to the hit: stock's ApplyPushFromDamage
                // runs inside OnTakeDamage_Alive, so a damage the victim
                // refuses (a zero answer) pushes nobody. A self blast the
                // arms zeroed is no refusal (below).
                int answered = victimDamage > 0f
                    ? Deliver(victim, attacker, victimDamage, active.Bits, weapon, weaponName, critical,
                              inflictor, origin)
                    : 0;
                if (answered <= 0 && victimDamage <= 0f)
                {
                    Push(victim, source, active, forceDamage, self: true, pyroFlareJump: false,
                         selfPushMult: selfPushMult);
                }
                else if (answered > 0)
                {
                    if (Entities.IsPlayer(victim) && attacker != EntityHandle.None
                        && Players.GetTeam(victim) != Players.GetTeam(attacker))
                    {
                        damageDealt += answered;
                        enemiesDamaged++;
                    }
                    Push(victim, source, active, forceDamage, self: self, pyroFlareJump: false,
                         selfPushMult: selfPushMult);
                }
            }
        }

        // The own-pass attribute arm (:5668-5705), armed with the rocket-jump
        // radius and before the second sweep, as stock runs it: the launcher's
        // add_health_on_radius_damage scaled by the enemy damage done.
        if (selfRadius > 0f && attacker != EntityHandle.None && damageDealt > 0f)
            HealRadiusDamage(item, attacker, weaponName, damageDealt, selfBaseDamage);

        // THE SELF PASS (stock's :5661-5714): the attacker at their own
        // radius and the launcher's base damage, whether or not the main
        // sweep's radius reached anyone — a zero-radius blast still jumps.
        if (selfRadius > 0f && Entities.IsPlayer(attacker) && attacker != directVictim)
        {
            Vector center = Entities.GetWorldSpaceCenter(attacker);
            Vector attackerOrigin = Entities.GetOrigin(attacker);

            // The same box-query retest, at the own pass's radius (:5708-5710):
            // the nearest point of the attacker's collision box.
            (Vector mins, Vector maxs) = Entities.GetBounds(attacker);
            if (TFRadiusDamage.Reaches(source, selfRadius, attackerOrigin + mins, attackerOrigin + maxs))
            {
                TraceResult tr = World.TraceLine(source, center, TraceMask.ShotHull, passThrough);
                if (tr.Fraction == 1f || !BlocksBlast(tr.Entity, attacker))
                {
                    float distance = TFRadiusDamage.VictimDistance(source, false, true, center,
                                                                   attackerOrigin, tr.End);
                    var selfBlast = active.SelfPass(selfRadius, selfBaseDamage);
                    float selfPushMult = Hooks.Float(1f, "mult_dmgself_push_force", item);
                    float forceDamage = TFRadiusDamage.VictimDamage(selfBlast, distance, self: true, weapon)
                                        * SoldierJumpScale(attacker, selfBlast.Bits);
                    float selfDamage = SelfBlastArms(item, forceDamage,
                        // Stock's flag was stamped before this pass; anything
                        // the sweep hurt silences the reduction (:5673,
                        // :7451).
                        damagedOtherPlayers: enemiesDamaged > 0,
                        Entities.GetIndex(attacker));
                    // A blast the arms zeroed still reaches OnTakeDamage_Alive
                    // (their answer is realDamage, :10535; only -1 stops it),
                    // and the push there reads the force damage (:10647-10656):
                    // the Rocket Jumper hurts nothing and throws as hard.
                    ClaimBlastJump(attacker, selfDamage, selfBlast.Bits, item);
                    bool hit = selfDamage <= 0f
                               || Deliver(attacker, attacker, selfDamage, selfBlast.Bits, weapon, weaponName, critical,
                                          inflictor, origin) > 0;
                    if (hit && forceDamage > 0f)
                        Push(attacker, source, selfBlast, forceDamage, self: true, pyroFlareJump: false,
                             selfPushMult: selfPushMult);
                }
            }
        }

        if (active.Damage > 0f)
            Log.Msg($"[TF2/Projectiles] blast at {origin} for {active.Damage:F0} over {active.Radius:F0}"
                  + $" ({count} near)\n");
    }

    /// <summary>
    /// The own-pass arm's health (:5681-5703): <c>add_health_on_radius_damage</c>
    /// on the launcher, scaled by <c>nDamageDealt / flBaseDamage</c> and
    /// truncated, taken with a generic blow; when anything healed, the
    /// <c>player_healonhit</c> event names the amount, the healed edict and
    /// the firing item's defindex (stock's -1 without one).
    /// </summary>
    private void HealRadiusDamage(IAttributeProvider? item, EntityHandle attacker, string weaponName,
                                  float damageDealt, float baseDamage)
    {
        int modHealth = Hooks.Int(0, "add_health_on_radius_damage", item);
        if (modHealth == 0)
            return;

        float scale = Math.Clamp(damageDealt / baseDamage, 0f, 1f);
        int healed = Health.TakeHealth(attacker, (int)(modHealth * scale));
        if (healed == 0)
            return;

        IntPtr ev = Events.EventCreate("player_healonhit");
        if (ev == IntPtr.Zero)
            return;
        Events.EventSetInt(ev, "amount", healed);
        Events.EventSetInt(ev, "entindex", Entities.GetIndex(attacker));
        Events.EventSetInt(ev, "weapon_def_index",
            item is AttributeContainer { Item: { } def } ? def.Defindex : NoItemDefIndex);
        Events.EventFire(ev);
    }

    /// <summary>
    /// The self arms of <c>ApplyAndScaleDamage</c> (:7449-7488), after the
    /// 0.75 the demoman weapons take in <see cref="TFRadiusDamage.VictimDamage"/>:
    /// a self blast that hurt no other player reads
    /// <c>rocket_jump_dmg_reduction</c> off the attacker's own manager
    /// (stock's <c>DamagedOtherPlayers</c> flag, :7451-7456 — the gate's
    /// DMG_BLAST arm and the TAUNTATK_GRENADE exception hold: these blasts
    /// carry the blast bit and a custom of None);
    /// <c>no_self_blast_dmg</c> == 2 zeroes every self blast, == 1 only the
    /// practice sticky (this port has no practice mode, so the blasts carry
    /// custom None and the single arm sleeps); <c>blast_dmg_to_self</c> then
    /// multiplies what is left — of a zero, whatever it multiplies.
    /// </summary>
    private float SelfBlastArms(IAttributeProvider? item, float damage, bool damagedOtherPlayers,
                                int attackerIndex)
    {
        if (!damagedOtherPlayers)
            damage = Hooks.Float(damage, "rocket_jump_dmg_reduction", Owners.ManagerOf(attackerIndex));
        int noSelf = Hooks.Int(0, "no_self_blast_dmg", item);
        if (noSelf == 2)
            return 0f;
        return Hooks.Float(damage, "blast_dmg_to_self", item);
    }

    /// <summary>
    /// <see cref="TFRadiusDamage.SelfDamageScale"/> for the attacker as this
    /// hit finds him. <c>FL_INWATER</c> is a water level above none
    /// (<c>baseplayer_shared.cpp:2014-2035</c>).
    /// </summary>
    private float SoldierJumpScale(EntityHandle attacker, DamageType bits)
        => TFRadiusDamage.SelfDamageScale(
            soldier: Classes.ClassOf(attacker) == TFClass.Soldier,
            blast: (bits & DamageType.Blast) != 0,
            onGround: IsOnGround(attacker),
            inWater: Directory.Get(attacker) is SsPlayer { State.WaterLevel: not 0 });

    /// <summary>
    /// The blast jump, stock's <c>OnTakeDamage</c> arms
    /// (<c>tf_player.cpp:8919-8922</c>, <c>:9150-9182</c>): a self blast — the
    /// attacker is the victim on both paths here — carrying <c>DMG_BLAST</c>,
    /// caught airborne and dry. The soldier's rocket jump also needs the
    /// damage to leave health (<c>m_iHealthBefore - flDamage &gt; 0</c>, the
    /// health as this hit finds him, before it lands); the demoman's sticky
    /// jump has no gate. <c>no_self_blast_dmg</c> on the firing weapon is the
    /// sound arm's <c>bPlaySound</c>, stock's
    /// <c>CALL_ATTRIB_HOOK_INT_ON_OTHER</c> reading it only when present.
    /// </summary>
    private void ClaimBlastJump(EntityHandle attacker, float damage, DamageType bits, IAttributeProvider? item)
    {
        if ((bits & DamageType.Blast) == 0
            || IsOnGround(attacker)
            || Directory.Get(attacker) is SsPlayer { State.WaterLevel: not 0 })
            return;

        TFBlastJump jump;
        if (Classes.ClassOf(attacker) == TFClass.Soldier)
        {
            if (Entities.GetHealth(attacker) - damage <= 0f) // m_iHealthBefore - flDamage > 0
                return;
            jump = TFBlastJump.RocketJumped;
        }
        else if (Classes.ClassOf(attacker) == TFClass.Demoman)
        {
            jump = TFBlastJump.StickyJumped;
        }
        else
        {
            return;
        }

        Carriers.Part<TFBlastJumpState>(attacker.EntryIndex)?.Set(jump,
            playSound: Hooks.Int(0, "no_self_blast_dmg", item) != 0);
    }

    private bool IsOnGround(EntityHandle player)
        => Networking.EntityGetState(player, out _).HasFlag(EntityState.OnGround);

    /// <summary>
    /// One hit, in the scopes the damage layer reads the weapon, the crit and
    /// the inflictor out of — the last is <see cref="DamageInflictors"/>, stock's
    /// <c>info.GetInflictor()</c> for the arms that ask what the hit came from
    /// (the reflected projectile's, <c>tf_gamerules.cpp:6159</c>); the amount
    /// the victim took, 0 for a refusal.
    ///
    /// <para>
    /// Stock runs the push (<see cref="Push"/>) inside <c>OnTakeDamage_Alive</c>,
    /// so a hit the victim refuses — no <c>DMG_TAKE_DAMAGE</c>, no health —
    /// never reaches it. The answered ask is where that lives here: the void
    /// <c>TakeDamage</c> could not tell a refusal from a hit, and pushed
    /// through both.
    /// </para>
    /// </summary>
    private int Deliver(EntityHandle victim, EntityHandle attacker, float victimDamage,
                        DamageType bits, TFWeaponId weapon, string weaponName, bool critical,
                        EntityHandle inflictor, Vector origin)
    {
        using (HitGroups.Push(HitGroup.Generic, critical))
        using (Scopes.Push(TFDamageCustom.None, weapon, weaponName))
        using (Inflictors.Push(inflictor, origin))
            return Damage.TakeDamageAnswered(victim, attacker, victimDamage, bits);
    }

    /// <summary>
    /// The push (<c>CTFPlayer::ApplyPushFromDamage</c>, <c>tf_player.cpp:10232-10475</c>):
    /// the core's damage path manufactures no force, so the impulse goes onto
    /// the player's velocity here, in the direction and the magnitude
    /// <see cref="TFRadiusDamage.Push"/> works out, from the damage that
    /// entered <c>ApplyOnDamageModifyRules</c> (<c>GetDamageForForceCalc</c>,
    /// <c>tf_gamerules.cpp:5916</c>), not what the rules left of it. A soldier
    /// reads the ground as stock does (:10265-10273). A self push is scaled whole by the
    /// launcher's <c>mult_dmgself_push_force</c> (:10237-10239), handed in by
    /// the caller that read it at the hit; any other push is scaled by the
    /// victim's own <c>damage_force_reduction</c> (:10465-10467).
    /// </summary>
    private void Push(EntityHandle victim, Vector source, in TFBlast blast, float victimDamage,
                      bool self, bool pyroFlareJump, float selfPushMult = 1f)
    {
        if (!Entities.IsPlayer(victim))
            return;

        // The freeze gate (tf_player.cpp:10234-10236): with the convar off,
        // the push waits on CanPlayerMove (tf_player_shared.cpp:10804-10849).
        // The refusing arms — the match-start countdown, the competitive and
        // movement-restart freezes — are windows this port has not built
        // (the restart hands over inside the tick, TeamRoundFlow's
        // EnterRestart), and the observer and noclip movetypes move
        // regardless: nobody is held, so the ask is yes, and the gate is
        // the convar's until a freeze lands in CanPlayerMove to refuse. The
        // absent convar is the default's off, read as the free player it
        // leaves.
        if (PreroundPush is { Value: false } && !CanPlayerMove())
            return;

        TFClass cls = Classes.ClassOf(victim);

        // DamageForce reads the victim's WorldAlignSize (:10245, :10365): the
        // OBB the seam gives as the victim's own bounds, not the class hull.
        // The self arm folds a crouched hull to the stock 55 (:10248-10252).
        (Vector mins, Vector maxs) = Entities.GetBounds(victim);
        Vector size = maxs - mins;
        if (self)
            size = TFRadiusDamage.SelfPushSize(size, CrouchHullSize);

        Vector direction = TFRadiusDamage.PushDirection(source, Entities.GetWorldSpaceCenter(victim));
        Vector impulse = TFRadiusDamage.Push(direction, size, victimDamage, self,
                                             TFRadiusDamage.SelfPushScale(soldier: cls == TFClass.Soldier,
                                                                          blast: (blast.Bits & DamageType.Blast) != 0,
                                                                          onGround: IsOnGround(victim),
                                                                          pyroFlareJump),
                                             heavy: cls == TFClass.HeavyWeapons);
        if (self)
        {
            impulse *= selfPushMult;
        }
        else
        {
            // damage_force_reduction (:10465-10467): the victim's own
            // manager's hook scales the push, in the other-than-self arm
            // only.
            impulse *= Hooks.Float(1f, "damage_force_reduction", Owners.ManagerOf(Entities.GetIndex(victim)));
        }
        // The soldier's push is also the jump's second claim
        // (:10275): BOTH of the self blast's ground arms — the grounded
        // badrj at :10266-10269 and the airborne rj at :10270-10273 — name
        // TF_PLAYER_ROCKET_JUMPED, with no health test and stock's silent
        // bPlaySound, so a grounded self rocket jumps too and an airborne
        // soldier is named by this arm and OnTakeDamage's (:9180) alike.
        // The duck reset beside it (:10277, m_Shared.SetAirDucked( 0 )) is
        // the air-duck counter's, deferred with the duck overrides
        // (docs/plans/tf2/deferred.tsv).
        if (self && cls == TFClass.Soldier && (blast.Bits & DamageType.Blast) != 0)
            Carriers.Part<TFBlastJumpState>(victim.EntryIndex)?.Set(TFBlastJump.RocketJumped);

        // Stock adds the impulse and nothing else (:10470): the next command's
        // PlayerMove lets go of the ground for a rise over its 250
        // (gamemovement.cpp:4612), so both passes hand over the impulse alone.
        Entities.ApplyVelocityImpulse(victim, impulse, clearGround: false);
    }

    /// <summary>
    /// Stock's <c>CTFPlayer::CanPlayerMove</c> (<c>tf_player_shared.cpp:10804</c>):
    /// the observer and noclip movetypes move whatever the round does, and
    /// the refusing arms behind them — the match-start countdown, the
    /// competitive restart freeze, the movement-restart freeze — are
    /// windows this port has not built. Nobody is held: yes, until a freeze
    /// lands to refuse.
    /// </summary>
    private static bool CanPlayerMove() => true;

    /// <summary>
    /// Whether <paramref name="hit"/> blocks the blast to
    /// <paramref name="victim"/>: stock's filter chain never blocks on a
    /// player (<c>CTraceFilterIgnorePlayers</c>), a projectile
    /// (<c>CTraceFilterIgnoreProjectiles</c>, the <c>CBaseProjectile</c>
    /// dynamic cast our classnames stand in for) or a friendly combat item
    /// (<c>CTraceFilterIgnoreFriendlyCombatItems</c>; no combat item exists
    /// in this port yet, so the arm has nothing to answer).
    /// </summary>
    private bool BlocksBlast(EntityHandle hit, EntityHandle victim)
        => hit != victim && !Entities.IsPlayer(hit)
           && !Entities.GetClassName(hit).StartsWith("tf_projectile_", StringComparison.Ordinal);

    private bool IsWorld(EntityHandle hit) => hit != EntityHandle.None && Entities.GetIndex(hit) == 0;
}
