//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Client.Player;
using TF2.Shared;

namespace TF2.Client.Weapons;

/// <summary>
/// The client's <see cref="ITFWeaponEffects"/>: the class special skill's
/// client arm, which stock reaches through <c>DoClassSpecialSkill</c> on both
/// sides (<c>tf_player_shared.cpp:12330</c>) — the spy's watch here lands in
/// the replay's <see cref="TFPlayerPrediction.CloakActivate"/>, so every
/// re-prediction of the toggle's command rewrites its state. The rest of the
/// arms are still no-ops: this side's shot visuals are the temp-entity lanes'.
/// </summary>
[SideService(Side.Client, As = [typeof(ITFWeaponEffects)])]
public sealed class TFClientWeaponEffects(TFPlayerPrediction players) : ITFWeaponEffects
{
    public bool ClassSpecialSkill(in WeaponAttackContext attack)
        => attack.Command.OwnerIndex > 0 && players.CloakActivate(attack.Command.OwnerIndex, attack.Now);

    /// <summary>The end is the server's grant: the crit is networked state, the replay reads it as it lands.</summary>
    public void EndClassSpecialSkill(in WeaponAttackContext attack) { }

    public void Projectile(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, float charge) { }

    public void Detonate(in WeaponAttackContext attack, TFWeaponScript script) { }

    /// <summary>The shove is the server's trace and impulse; the replay arms nothing.</summary>
    public void Shove(in WeaponAttackContext attack, TFWeaponScript script) { }

    /// <summary>The orb is the server's spawn; the client's muzzle fizzle
    /// (<c>dxhr_sniper_fizzle</c>, tf_weapon_mechanical_arm.cpp:294-299) waits
    /// with the particle arm, as every client effect here does.</summary>
    public void ShockAttack(in WeaponAttackContext attack, TFWeaponScript script) { }

    public void Flames(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot) { }

    public void AirBlast(in WeaponAttackContext attack, TFWeaponScript script) { }

    /// <summary>The grant is the server's extinguish; the count is networked state, the replay reads it as it lands.</summary>
    public bool ExtinguishBeam(in WeaponAttackContext attack, TFWeaponScript script) => false;

    public void Heal(in WeaponAttackContext attack, TFWeaponScript script, bool held) { }

    public void Zoom(in WeaponAttackContext attack, TFWeaponScript script, bool zoomed) { }

    public void Tracer(in WeaponAttackContext attack, TFWeaponScript script) { }

    public void SelectLast(in WeaponAttackContext attack, TFWeaponScript script) { }

    /// <summary>The smack rides the arms: the swing's own reach and the script's
    /// two hit sounds, the pair stock's <c>OnSwingHit</c> picks between
    /// (<c>shared/tf/tf_weaponbase_melee.cpp:686-689</c>).</summary>
    public void Melee(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, in SwingTrace trace)
    {
        var type = attack.Type;
        attack.Arms.MeleeSwing(in type, trace.Range,
                               script.Sound(WeaponScriptSound.MeleeHitWorld),
                               script.Sound(WeaponScriptSound.MeleeHit));
    }

    public void MeleeSwingStarted(in WeaponAttackContext attack, float swingRange) { }

    public void AttackEnemyProjectiles(in WeaponAttackContext attack, int projectiles, bool critical) { }

    public void RingOfFire(in WeaponAttackContext attack, int damage) { }

    public void Rearmed(in WeaponAttackContext attack) { }

    public void SpeakWeaponFire(in WeaponAttackContext attack, string concept) { }

    public void ClearWeaponFireScene(in WeaponAttackContext attack) { }

    public void FiringSpeech(in WeaponAttackContext attack) { }

    public void LunchboxEat(in WeaponAttackContext attack, TFWeaponScript script) { }

    public void LunchboxDrop(in WeaponAttackContext attack, TFWeaponScript script, int mode) { }
    public void DeployBuff(in WeaponAttackContext attack, int buffType) { }

    public void BlowBuffHorn(in WeaponAttackContext attack, int buffType) { }

    public void RaiseBuffBanner(in WeaponAttackContext attack, int buffType) { }

    public void UseRage(in WeaponAttackContext attack, int buffType) { }
}
