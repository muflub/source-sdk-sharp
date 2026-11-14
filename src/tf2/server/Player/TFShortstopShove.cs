//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Combat;
using TF2.Server.Projectiles;
using TF2.Server.Weapons;

using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;
using TF2.Shared.Player;

namespace TF2.Server.Player;

/// <summary>
/// The Shortstop's shove (<c>CTFPistol_ScoutPrimary::Push</c>,
/// <c>tf_weapon_pistol.cpp:119-197</c>'s GAME_DLL half): the enemy player the
/// muzzle's hull sweep meets takes the point of damage and the generic
/// pushback impulse — 400 along the shooter-to-victim line, the victim's own
/// <c>airblast_vulnerability_multiplier</c> over it, a grounded victim's
/// upward component lifted to <c>JUMP_MIN_SPEED</c>, the ground flag stripped
/// and <c>TF_COND_KNOCKED_INTO_AIR</c> added
/// (<c>CTFPlayer::ApplyGenericPushbackImpulse</c>, tf_player.cpp:3369-3400).
///
/// <para>
/// The candidate walk is stock's enemy roster squared down to what the seam
/// asks: a player on the other team, alive, within 128 units origin to
/// origin, visible, and the hull's hit (:128-151, :169). The truce and rune
/// arms of the generic impulse (:3372-3379) wait with the FaN shove's, as the
/// row records; the map has no rune and no truce to gate.
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(ITFShoveEffects)])]
public sealed class TFShortstopShove(IEngineEntities entities, IEnginePlayers players,
                                     IEngineWorldQuery world, IEngineDamage damage,
                                     IEngineNetworking networking, ITFPlayerConditions conditions,
                                     ITFWeaponOwners owners, AttributeHooks hooks,
                                     ILagCompensation lagCompensation, ILog log)
    : ITFShoveEffects
{
    /// <summary>The impulse along the shooter-to-victim line (:173).</summary>
    public const float PushForce = 400f;

    /// <summary>The candidate radius, origin to origin (:147).</summary>
    public const float Reach = 128f;

    /// <summary>The hull sweep's length along the aim (:160).</summary>
    public const float MuzzleReach = 50f;

    /// <summary>The sweep's half extents (<c>vHull</c>, :157).</summary>
    public const float HullHalf = 16f;

    /// <summary>The point of damage the shove lands (:174).</summary>
    public const float ShoveDamage = 1f;

    /// <summary>The melee blow's force scale, the shove's 1/1 * 80 (:176).</summary>
    public const float ShoveForceScale = 80f;

    /// <summary>shareddefs.h's MAX_PLAYERS, the roster walk's span.</summary>
    private const int MaxPlayers = 33;

    /// <inheritdoc cref="ITFWeaponEffects.Shove"/>
    public void Shove(in WeaponAttackContext attack, TFWeaponScript script)
    {
        int ownerIndex = attack.Command.OwnerIndex;
        EntityHandle owner = players.GetHandle(ownerIndex);
        if (owner == EntityHandle.None || !entities.IsValid(owner))
            return;

        // The push runs where the shooter saw the others (:126, :193).
        lagCompensation.Start(owner);
        try
        {
            Push(in attack, owner);
        }
        finally
        {
            lagCompensation.Finish(owner);
        }
    }

    private void Push(in WeaponAttackContext attack, EntityHandle owner)
    {
        Vector eye = entities.GetEyePosition(owner);
        Vector forward = Angles.Forward(entities.GetEyeAngles(owner));
        Vector origin = entities.GetOrigin(owner);

        // CollectPlayers over the enemy team (:128-129): every player slot, so
        // no crowd of other entities can crowd a victim out.
        for (int i = 1; i < MaxPlayers; i++)
        {
            EntityHandle victim = players.GetHandle(i);
            if (victim == EntityHandle.None || victim == owner || !entities.IsValid(victim))
                continue;

            if (!players.IsAlive(victim) || players.GetTeam(victim) == players.GetTeam(owner))
                continue;

            // Stock's 128-unit gate, squared as the source's (:147).
            if ((origin - entities.GetOrigin(victim)).LengthSquared > Reach * Reach)
                continue;

            // FVisible (MASK_SOLID, :150): nothing solid between but the
            // victim themselves.
            Vector victimCenter = entities.GetWorldSpaceCenter(victim);
            TraceResult seen = world.TraceLine(eye, victimCenter, TraceMask.Solid, owner);
            if (seen.Fraction != 1f && seen.Entity != victim)
                continue;

            // The muzzle's hull sweep (:156-161): a 16-unit box half carried
            // 50 along the aim, the owner ignored.
            Vector half = new(HullHalf, HullHalf, HullHalf);
            HullResult smack = world.TraceHull(eye, eye + forward * MuzzleReach,
                                               Vector.Zero - half, half, TraceMask.Solid, owner);
            if (!smack.Blocked || smack.Entity != victim)
                continue;

            Vector toVictim = (entities.GetOrigin(victim) - origin).Normalized;

            // The generic impulse's body (:3381-3399). The push is the
            // vulnerability walk's answer, the ground floor the flag answers,
            // then the flag strip and the airborne cond with the impulse.
            Vector push = toVictim * PushForce;
            push *= hooks.Float(1f, "airblast_vulnerability_multiplier",
                                owners.ManagerOf(victim.EntryIndex));

            if (networking.EntityGetState(victim, out _).HasFlag(EntityState.OnGround)
                && push.Z < TFAirBlastDefs.JumpMinSpeed)
                push = new Vector(push.X, push.Y, TFAirBlastDefs.JumpMinSpeed);

            // The vertical vulnerability over the floored rise (:3394).
            push = new Vector(push.X, push.Y,
                              hooks.Float(push.Z, "airblast_vertical_vulnerability_multiplier",
                                          owners.ManagerOf(victim.EntryIndex)));

            conditions.Add(victim, TFCond.KnockedIntoAir);
            entities.ApplyVelocityImpulse(victim, push, clearGround: true);

            // The point of damage, the melee type and the trace's force at
            // 1/1 * the scale (:174-178) — the same shape the melee's hit
            // runs, the contact where the sweep ended.
            Vector force = DamageForceRules.MeleeForce(forward, ShoveDamage, scale: ShoveForceScale / ShoveDamage);
            damage.ApplyAt(victim, owner, ShoveDamage,
                           TFDamageBits.Melee | DamageType.NeverGib | DamageType.Club, force,
                           origin + forward * MuzzleReach);

            if (attack.EffectsLive)
                attack.Arms.Sound(TFWeaponBehaviour.ShoveImpactSound);

            log.Msg($"[TF2/Shove] {owner} shoved {victim} toward {toVictim}\n");

            // Stock breaks at the first victim the sweep met (:186).
            return;
        }
    }
}
