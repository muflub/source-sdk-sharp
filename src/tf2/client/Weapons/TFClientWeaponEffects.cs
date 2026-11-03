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

    public void Projectile(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, float charge) { }

    public void Detonate(in WeaponAttackContext attack, TFWeaponScript script) { }

    public void Flames(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot) { }

    public void AirBlast(in WeaponAttackContext attack, TFWeaponScript script) { }

    public void Heal(in WeaponAttackContext attack, TFWeaponScript script, bool held) { }

    public void Zoom(in WeaponAttackContext attack, TFWeaponScript script, bool zoomed) { }

    public void SelectLast(in WeaponAttackContext attack, TFWeaponScript script) { }

    public void Melee(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, in SwingTrace trace) { }

    public void AttackEnemyProjectiles(in WeaponAttackContext attack, int projectiles, bool critical) { }

    public void RingOfFire(in WeaponAttackContext attack, int damage) { }

    public void Rearmed(in WeaponAttackContext attack) { }

    public void SpeakWeaponFire(in WeaponAttackContext attack, string concept) { }

    public void ClearWeaponFireScene(in WeaponAttackContext attack) { }

    public void FiringSpeech(in WeaponAttackContext attack) { }
}
