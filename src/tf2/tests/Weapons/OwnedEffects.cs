using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Player;
using TF2.Server.Voice;
using TF2.Server.Weapons;
using TF2.Shared;
using TF2.Tests.Entities;

namespace TF2.Tests.Weapons;

/// <summary>
/// The effects composite the behaviour facts build: the seams under test held,
/// every seam a fact does not follow a silent stand — the no-answer the
/// constructor's optional seat used to default, now spelled as the object it
/// is — and a speech whose rules no behaviour fact reads.
/// </summary>
internal static class OwnedEffects
{
    /// <summary>The silent stand behind every seam a fact leaves out.</summary>
    private sealed class SilentSeams : ITFProjectileEffects, ITFFlameEffects, ITFShoveEffects, ITFHealEffects,
                                       ITFZoomEffects, ITFClassSkillEffects, ITFItemSwitchEffects, ITFMeleeEffects,
                                       ITFMinigunEffects, ITFTracerEffects, ITFLunchboxEffects, ITFBuffEffects
    {
        public void Projectile(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, float charge) { }
        public void Detonate(in WeaponAttackContext attack, TFWeaponScript script) { }
        public void ShockAttack(in WeaponAttackContext attack, TFWeaponScript script) { }
        public void Flames(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot) { }
        public void AirBlast(in WeaponAttackContext attack, TFWeaponScript script) { }
        public bool ExtinguishBeam(in WeaponAttackContext attack, TFWeaponScript script) => false;
        public void Shove(in WeaponAttackContext attack, TFWeaponScript script) { }
        public void Heal(in WeaponAttackContext attack, TFWeaponScript script, bool held) { }
        public void Zoom(in WeaponAttackContext attack, TFWeaponScript script, bool zoomed) { }
        public bool DoSpecialSkill(int ownerIndex) => false;
        public void EndSpecialSkill(int ownerIndex) { }
        public void SelectLast(in WeaponAttackContext attack, TFWeaponScript script) { }
        public void Melee(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, in SwingTrace trace) { }
        public void MeleeSwingStarted(in WeaponAttackContext attack, float swingRange) { }
        public void AttackEnemyProjectiles(in WeaponAttackContext attack, int projectiles, bool critical) { }
        public void RingOfFire(in WeaponAttackContext attack, int damage) { }
        public void Rearmed(in WeaponAttackContext attack) { }
        public void Tracer(in WeaponAttackContext attack, TFWeaponScript script) { }
        public void LunchboxEat(in WeaponAttackContext attack, TFWeaponScript script) { }
        public void LunchboxDrop(in WeaponAttackContext attack, TFWeaponScript script, int mode) { }
        public void Bite(int playerIndex) { }
        public void Drink(int playerIndex) { }
        public void DeployBuff(int ownerIndex, int buffType) { }
        public void BlowBuffHorn(int ownerIndex, int buffType) { }
        public void RaiseBuffBanner(int ownerIndex, int buffType) { }
        public void UseRage(int ownerIndex, int buffType) { }
    }

    /// <summary>
    /// One speech over silent rules, built once: the behaviour facts never
    /// speak, and the speech facts hold their rig's own.
    /// </summary>
    private static readonly Lazy<TFConceptSpeech> Speech = new(() =>
    {
        var engine = TestServices.Ignoring("Players.GetHandle");
        return new TFConceptSpeech(new FakeConceptRules(), new TFPlayerCarriers(), new FakeConceptTeams(),
                                   engine.Resolve<IEnginePlayers>()!, ObjectiveRig.ConceptSceneImage(),
                                   engine.Resolve<IEngineSound>()!, new FakeRandom());
    });

    /// <summary>The dispatcher with <paramref name="speech"/> and the named seams held, the rest silent.</summary>
    public static TFWeaponEffects Of(ITFProjectileEffects? projectiles = null, ITFFlameEffects? flames = null,
                                     ITFHealEffects? heal = null, ITFZoomEffects? zoom = null,
                                     ITFClassSkillEffects? skills = null, ITFItemSwitchEffects? itemSwitch = null,
                                     ITFMeleeEffects? melee = null, ITFMinigunEffects? minigun = null,
                                     ITFTracerEffects? tracer = null, ITFLunchboxEffects? lunchbox = null,
                                     ITFBuffEffects? buffs = null, ITFShoveEffects? shove = null,
                                     TFConceptSpeech? speech = null)
    {
        var silent = new SilentSeams();
        return new TFWeaponEffects(projectiles ?? silent, flames ?? silent, heal ?? silent, zoom ?? silent,
                                   skills ?? silent, itemSwitch ?? silent, melee ?? silent, minigun ?? silent,
                                   tracer ?? silent, lunchbox ?? silent, buffs ?? silent, shove ?? silent,
                                   speech ?? Speech.Value);
    }
}
