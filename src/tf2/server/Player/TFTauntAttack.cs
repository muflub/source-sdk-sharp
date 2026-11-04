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
using TF2.Server.Entities.Items;
using TF2.Server.Weapons;

using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;

namespace TF2.Server.Player;

/// <summary>
/// One taunt's timed attack: the keying of <c>CTFPlayer::Taunt</c> on the
/// scene the response rules answered (tf_player.cpp:17979-18068 — pyro
/// <c>taunt02</c> hadouken at 2.1 s, heavy <c>taunt03_v1</c> high noon at
/// 1.8 s, any spy <c>taunt03</c> fencing slash at 1.8 s), the PostThink
/// window that fires it (:2691-2695), and <c>DoTauntAttack</c>'s two arms —
/// the box arm's volume, visibility test, damage and -45° launch
/// (:18387-18451); the high noon's trace and team gate (:18613-18635). The
/// fencing chain re-arms itself, whoever stood in the box: slash B 0.47 s
/// after the slash A, the stab 1.73 s later still (:18436-18445).
///
/// <para>
/// Each hit goes out through the damage layer under a scope naming the
/// custom (<c>TF_DMG_CUSTOM_TAUNTATK_*</c>) and the held weapon — the
/// inflictor and weapon of stock's <c>CTakeDamageInfo</c>, whose caller's
/// bits stand because the layer keeps a scoped hit's.
/// </para>
/// </summary>
public sealed class TFTauntAttack(TFPlayerCarriers carriers, ITFMapPlayers players, IEngineEntities entities,
                                  IEngineWorldQuery world, IEngineDamage damage, IEnginePlayers enginePlayers,
                                  IEngineNetworking networking, TFWeaponScripts scripts, TFDamageScopes scopes,
                                  IEntityDirectory directory)
    : IPlayerComponent
{
    /// <summary>After the taunt think (26): the same PostThink, stock's stop asked before the window fires.</summary>
    public int Order => 27;

    /// <summary>The pyro hadouken's scene, whose attack lands 2.1 s in (:17982-17986).</summary>
    public const string PyroHadoukenScene = "scenes/player/pyro/low/taunt02.vcd";

    /// <summary>The heavy high noon's scene, 1.8 s in (:18020-18024).</summary>
    public const string HeavyHighNoonScene = "scenes/player/heavy/low/taunt03_v1.vcd";

    /// <summary>The spy fencing scenes' shared head — taunt03_v1 and taunt03_v2 (:18064; 29 characters, case-insensitive).</summary>
    public const string SpyFencingScenePrefix = "scenes/player/spy/low/taunt03";

    /// <summary>The fencing slash B, 0.47 s after the slash A (:18439).</summary>
    public const float FencingSlashDelay = 0.47f;

    /// <summary>The killing stab, 1.73 s after the slash B (:18444).</summary>
    public const float FencingStabDelay = 1.73f;

    /// <summary>The box arm's reach along the flat forward (:18394).</summary>
    public const float BoxReach = 64f;

    /// <summary>The box's half extent, on each axis (:18395).</summary>
    public const float BoxHalfExtent = 24f;

    /// <summary>The hit's position, the attacker's centre carried three quarters of the way to the victim's (:18412-18413).</summary>
    public const float HitLerp = 0.75f;

    /// <summary>The high noon's trace, eye along the full view (:18618).</summary>
    public const float HighNoonRange = 500f;

    /// <summary>The box query's ceiling, stock's <c>pList[256]</c> (:18396).</summary>
    public const int MaxVictims = 256;

    /// <summary>The launch's pitch, up and over (:18401, :18631): the force's direction, never the aim's.</summary>
    public const float LaunchPitch = -45f;

    /// <summary>The attack waiting to land.</summary>
    private enum Attack
    {
        None = 0,
        PyroHadouken,
        HeavyHighNoon,
        SpyFencingSlashA,
        SpyFencingSlashB,
        SpyFencingStab,
    }

    private Attack _attack = Attack.None;
    private float _fireAt;
    private EntityHandle _player = EntityHandle.None;

    public void Attach(EntityHandle player)
    {
        _player = player;
        Clear();
        carriers.EnterPart(player.EntryIndex, this);
    }

    public void Detach()
    {
        Clear();
        _player = EntityHandle.None;
    }

    /// <summary>
    /// The keying, where stock's <c>Taunt</c> lands it after the speak
    /// (:17874-17875 clearing, then :17979): any pending attack dies with the
    /// new taunt, and the class's scene arms the next. The heavy's fist
    /// alternates (:18025-18033) are keyed on the weapon, not the scene, and
    /// wait with the rest of the non-kill arms.
    /// </summary>
    public void Arm(string scene, float now)
    {
        Clear();

        switch (players.ClassOf(_player))
        {
            case TFClass.Pyro when SceneIs(scene, PyroHadoukenScene):
                Arm(Attack.PyroHadouken, 2.1f, now);
                break;

            case TFClass.HeavyWeapons when SceneIs(scene, HeavyHighNoonScene):
                Arm(Attack.HeavyHighNoon, 1.8f, now);
                break;

            case TFClass.Spy when scene.StartsWith(SpyFencingScenePrefix, StringComparison.OrdinalIgnoreCase):
                Arm(Attack.SpyFencingSlashA, 1.8f, now);
                break;
        }
    }

    /// <summary>The pending attack, dropped: stock's zeroing before the keying (:17874-17875).</summary>
    public void Clear() => (_attack, _fireAt) = (Attack.None, 0f);

    /// <summary>
    /// PostThink's window (:2691-2695): a set time past <paramref name="now"/>
    /// fires once, the time cleared first so the arm's own re-arm is the one
    /// that stands.
    /// </summary>
    public void Tick(float now)
    {
        if (_attack == Attack.None || _fireAt == 0f || _fireAt >= now)
            return;

        _fireAt = 0f;
        Fire(_attack, now);
    }

    /// <summary><c>DoTauntAttack</c>'s gate (:18379-18382): no taunt or no life, nothing — and the chain with it.</summary>
    private void Fire(Attack attack, float now)
    {
        _attack = Attack.None;

        if (!players.InCond(_player, TFCond.Taunting) || !players.IsAlive(_player))
            return;

        if (attack == Attack.HeavyHighNoon)
            HighNoon();
        else
            BoxAttack(attack);

        // The chain, whether or not the box held anyone (:18436-18445).
        switch (attack)
        {
            case Attack.SpyFencingSlashA:
                Arm(Attack.SpyFencingSlashB, FencingSlashDelay, now);
                break;
            case Attack.SpyFencingSlashB:
                Arm(Attack.SpyFencingStab, FencingStabDelay, now);
                break;
        }
    }

    private void Arm(Attack attack, float delay, float now) => (_attack, _fireAt) = (attack, now + delay);

    /// <summary>
    /// The box arm (:18387-18451): everything in the volume an arm's reach
    /// ahead, seen from the centre, but the taunter themselves and whoever a
    /// wall hides, taking the hit at the point carried three quarters of the
    /// way over, thrown up at 45°.
    /// </summary>
    private void BoxAttack(Attack attack)
    {
        float yaw = entities.GetEyeAngles(_player).Y;
        Vector flatForward = Angles.Forward(new Vector(0f, yaw, 0f)); // :18393, the pitch flattened away
        Vector center = entities.GetWorldSpaceCenter(_player) + flatForward * BoxReach;
        Vector halfExtent = new(BoxHalfExtent, BoxHalfExtent, BoxHalfExtent);

        (float hurt, DamageType type, TFDamageCustom custom, float push) = attack switch
        {
            Attack.PyroHadouken => (500f, DamageType.Burn | TFDamageBits.Ignite, TFDamageCustom.TauntatkHadouken, 25000f),
            Attack.SpyFencingSlashA or Attack.SpyFencingSlashB
                => (25f, DamageType.Slash | DamageType.PreventPhysicsForce, TFDamageCustom.TauntatkFencing, 100f),
            Attack.SpyFencingStab => (500f, DamageType.Slash, TFDamageCustom.TauntatkFencing, 20000f),
            _ => (0f, DamageType.None, TFDamageCustom.None, 0f),
        };
        if (hurt <= 0f)
            return;

        // The launch, one direction for the whole swing (:18401): no push
        // rides the slashes, so the victim stays in the stab's box.
        Vector launch = Angles.Forward(new Vector(LaunchPitch, yaw, 0f)) * push;

        Span<EntityHandle> nearby = stackalloc EntityHandle[MaxVictims];
        int count = world.EntitiesInSphere(center, BoxCornerRadius, nearby);

        for (int i = 0; i < count; i++)
        {
            EntityHandle victim = nearby[i];
            if (victim == EntityHandle.None || victim == _player)
                continue;

            // FL_CLIENT | FL_OBJECT (const.h:118, :141) as this build says it:
            // a player, or a solid entity of ours the directory carries. The
            // player's own data carrier is an effect, and no list member.
            if (!networking.EntityGetState(victim, out _).HasFlag(EntityState.Solid)
                || !(entities.IsPlayer(victim) || directory.Get(victim) is ScriptedEntity))
                continue;

            // The box, exact: the sphere is the sweep's candidate cut, the
            // collision bounds the retest stock's partition makes.
            if (!OverlapsBox(victim, center - halfExtent, center + halfExtent))
                continue;

            // FVisible (MASK_SOLID, :18409): nothing but the victim stops the
            // line from the eye to its centre (baseentity.cpp's FVisible).
            Vector victimCenter = entities.GetWorldSpaceCenter(victim);
            TraceResult seen = world.TraceLine(entities.GetEyePosition(_player), victimCenter, TraceMask.Solid, _player);
            if (seen.Hit && seen.Entity != victim)
                continue;

            Vector myCenter = entities.GetWorldSpaceCenter(_player);
            Vector hit = myCenter + (victimCenter - myCenter) * HitLerp;

            using (scopes.Push(custom, WeaponOf(), WeaponNameOf()))
                damage.ApplyAt(victim, _player, hurt, type, launch, hit);
        }
    }

    /// <summary>
    /// The heavy's high noon (:18613-18635): the trace from the eye along the
    /// full view — hitboxes included, <see cref="TraceMask.SolidHitbox"/>'s
    /// reason — and what it stops on, a player of another real team, takes
    /// the shot, launched as the box arm's are.
    /// </summary>
    private void HighNoon()
    {
        Vector eyeAngles = entities.GetEyeAngles(_player);
        Vector eye = entities.GetEyePosition(_player);
        TraceResult tr = world.TraceLine(eye, eye + Angles.Forward(eyeAngles) * HighNoonRange, TraceMask.SolidHitbox, _player);

        if (tr.Fraction >= 1f || tr.Entity == EntityHandle.None || !entities.IsPlayer(tr.Entity))
            return;

        // :18628 — past the shared teams, and not ours.
        int team = enginePlayers.GetTeam(tr.Entity);
        if (team <= (int)TFTeam.Spectator || team == enginePlayers.GetTeam(_player))
            return;

        Vector launch = Angles.Forward(new Vector(LaunchPitch, eyeAngles.Y, 0f)) * 25000f;
        using (scopes.Push(TFDamageCustom.TauntatkHighNoon, WeaponOf(), WeaponNameOf()))
            damage.ApplyAt(tr.Entity, _player, 500f, DamageType.Bullet, launch, entities.GetWorldSpaceCenter(_player));
    }

    private TFWeaponId WeaponOf()
    {
        string name = WeaponNameOf();
        return name.Length > 0 ? scripts.Get(name)?.Id ?? TFWeaponId.None : TFWeaponId.None;
    }

    private string WeaponNameOf() => enginePlayers.GetActiveWeapon(_player);

    /// <summary>The victim's world box against the query's, corner pairs over each axis.</summary>
    private bool OverlapsBox(EntityHandle victim, Vector mins, Vector maxs)
    {
        (Vector lo, Vector hi) = entities.GetBounds(victim);
        Vector origin = entities.GetOrigin(victim);

        return origin.X + lo.X <= maxs.X && origin.X + hi.X >= mins.X
            && origin.Y + lo.Y <= maxs.Y && origin.Y + hi.Y >= mins.Y
            && origin.Z + lo.Z <= maxs.Z && origin.Z + hi.Z >= mins.Z;
    }

    private static bool SceneIs(string scene, string expected) => string.Equals(scene, expected, StringComparison.OrdinalIgnoreCase);

    /// <summary>Half the box's diagonal: the sphere that cannot miss anything the box holds, the sweep's candidate cut.</summary>
    private static float BoxCornerRadius => BoxHalfExtent * MathF.Sqrt(3f);
}
