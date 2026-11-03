using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Server.Voice;
using TF2.Shared;

namespace TF2.Server.Weapons;

/// <summary>Stage 2.8's arms: a TF weapon's projectile and the stickybomb launcher's detonation. One owner, <c>As = [typeof(ITFProjectileEffects)]</c>.</summary>
public interface ITFProjectileEffects
{
    /// <inheritdoc cref="ITFWeaponEffects.Projectile"/>
    void Projectile(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, float charge);

    /// <inheritdoc cref="ITFWeaponEffects.Detonate"/>
    void Detonate(in WeaponAttackContext attack, TFWeaponScript script);
}

/// <summary>Stage 2.11's arm: a flamethrower tick's flames and the airblast. One owner, <c>As = [typeof(ITFFlameEffects)]</c>.</summary>
public interface ITFFlameEffects
{
    /// <inheritdoc cref="ITFWeaponEffects.Flames"/>
    void Flames(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot);

    /// <inheritdoc cref="ITFWeaponEffects.AirBlast"/>
    void AirBlast(in WeaponAttackContext attack, TFWeaponScript script);
}

/// <summary>Stage 2.9's arm: the medigun's beam. One owner, <c>As = [typeof(ITFHealEffects)]</c>.</summary>
public interface ITFHealEffects
{
    /// <inheritdoc cref="ITFWeaponEffects.Heal"/>
    void Heal(in WeaponAttackContext attack, TFWeaponScript script, bool held);
}

/// <summary>The PDA's item switch (the core's last-item switch). One owner, <c>As = [typeof(ITFItemSwitchEffects)]</c>.</summary>
public interface ITFItemSwitchEffects
{
    /// <inheritdoc cref="ITFWeaponEffects.SelectLast"/>
    void SelectLast(in WeaponAttackContext attack, TFWeaponScript script);
}

/// <summary>The sniper rifle's scope (the player's field of view). One owner, <c>As = [typeof(ITFZoomEffects)]</c>.</summary>
public interface ITFZoomEffects
{
    /// <inheritdoc cref="ITFWeaponEffects.Zoom"/>
    void Zoom(in WeaponAttackContext attack, TFWeaponScript script, bool zoomed);
}

/// <summary>Stage 2.7's arm: the melee smack's trace and hit. One owner, <c>As = [typeof(ITFMeleeEffects)]</c>.</summary>
public interface ITFMeleeEffects
{
    /// <inheritdoc cref="ITFWeaponEffects.Melee"/>
    void Melee(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, in SwingTrace swing);
}

/// <summary>
/// The class special skill a weapon's secondary reaches
/// (<c>CTFPlayer::DoClassSpecialSkill</c>, <c>tf_player_shared.cpp:12330-12403</c>).
/// One owner, <c>As = [typeof(ITFClassSkillEffects)]</c>.
/// </summary>
public interface ITFClassSkillEffects
{
    /// <inheritdoc cref="ITFWeaponEffects.ClassSpecialSkill"/>
    bool DoSpecialSkill(int ownerIndex);
}

/// <summary>Stage 2.3's arm: the minigun's projectile sweep and ring of fire. One owner, <c>As = [typeof(ITFMinigunEffects)]</c>.</summary>
public interface ITFMinigunEffects
{
    /// <inheritdoc cref="ITFWeaponEffects.AttackEnemyProjectiles"/>
    void AttackEnemyProjectiles(in WeaponAttackContext attack, int projectiles, bool critical);

    /// <inheritdoc cref="ITFWeaponEffects.RingOfFire"/>
    void RingOfFire(in WeaponAttackContext attack, int damage);

    /// <inheritdoc cref="ITFWeaponEffects.Rearmed"/>
    void Rearmed(in WeaponAttackContext attack);
}

/// <summary>
/// The server's <see cref="ITFWeaponEffects"/>: one owner for the shared
/// behaviours' effect calls, which hands each to the stage that owns that
/// effect, so the projectile, flame, medigun and scope lanes each register an
/// owner of their own seam without touching this file. A seam nobody owns
/// does nothing, as <see cref="NoTFWeaponEffects"/> does for the whole set:
/// the shot is spent and nothing appears.
/// </summary>
[SideService(Side.Server, As = [typeof(ITFWeaponEffects)])]
public sealed class TFWeaponEffects(ITFProjectileEffects? projectiles = null, ITFFlameEffects? flames = null,
                                    ITFHealEffects? heal = null, ITFZoomEffects? zoom = null,
                                    ITFClassSkillEffects? skills = null, ITFItemSwitchEffects? itemSwitch = null,
                                    ITFMeleeEffects? melee = null, ITFMinigunEffects? minigun = null,
                                    TFConceptSpeech? speech = null) : ITFWeaponEffects
{
    private readonly TFConceptSpeech? _speech = speech;

    public void Projectile(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, float charge)
        => projectiles?.Projectile(in attack, script, shot, charge);

    public void Detonate(in WeaponAttackContext attack, TFWeaponScript script)
        => projectiles?.Detonate(in attack, script);

    public void Flames(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot)
        => flames?.Flames(in attack, script, shot);

    public void AirBlast(in WeaponAttackContext attack, TFWeaponScript script)
        => flames?.AirBlast(in attack, script);

    public void Heal(in WeaponAttackContext attack, TFWeaponScript script, bool held)
        => heal?.Heal(in attack, script, held);

    public void Zoom(in WeaponAttackContext attack, TFWeaponScript script, bool zoomed)
        => zoom?.Zoom(in attack, script, zoomed);

    public bool ClassSpecialSkill(in WeaponAttackContext attack)
        => skills?.DoSpecialSkill(attack.Command.OwnerIndex) ?? false;

    public void SelectLast(in WeaponAttackContext attack, TFWeaponScript script)
        => itemSwitch?.SelectLast(in attack, script);

    public void Melee(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, in SwingTrace trace)
        => melee?.Melee(in attack, script, shot, in trace);

    public void AttackEnemyProjectiles(in WeaponAttackContext attack, int projectiles, bool critical)
        => minigun?.AttackEnemyProjectiles(in attack, projectiles, critical);

    public void RingOfFire(in WeaponAttackContext attack, int damage)
        => minigun?.RingOfFire(in attack, damage);

    public void Rearmed(in WeaponAttackContext attack)
        => minigun?.Rearmed(in attack);

    /// <summary>The scene clock's window and the firing gate's (tf_player.cpp:20158-20183).</summary>
    public const float SpeechWindow = 5f;

    private readonly Dictionary<int, float> _speechGate = [];
    private readonly Dictionary<int, float> _firingGate = [];

    /// <summary>
    /// <c>CTFPlayer::SpeakWeaponFire</c> (:20158-20183): the empty concept is
    /// the plain fire concept behind the scene clock — it waits while the
    /// clock is ahead (:20162), and the ask re-arms it five seconds out; the
    /// scene's own length overwrite is a recorded divergence. A named concept
    /// always speaks (:20170).
    /// </summary>
    public void SpeakWeaponFire(in WeaponAttackContext attack, string concept)
    {
        int owner = attack.Command.OwnerIndex;
        if (owner <= 0 || _speech is null)
            return;

        if (concept.Length == 0)
        {
            if (_speechGate.TryGetValue(owner, out float next) && next > attack.Now)
                return;

            concept = TFConceptSpeech.FireWeapon;
            _speechGate[owner] = attack.Now + SpeechWindow;
        }

        _speech.Speak(owner, concept);
    }

    /// <summary>ClearWeaponFireScene (:20185-20188): the clock drops to now, so the next plain ask gets in.</summary>
    public void ClearWeaponFireScene(in WeaponAttackContext attack)
    {
        int owner = attack.Command.OwnerIndex;
        if (owner > 0)
            _speechGate[owner] = attack.Now;
    }

    /// <summary>
    /// The minigun's firing concept (<c>tf_weapon_minigun.cpp:336-341</c>):
    /// the shot asks once, and the ask speaks when its own clock has passed,
    /// re-arming five seconds out; the clock lives beside the scene's, never
    /// on it.
    /// </summary>
    public void FiringSpeech(in WeaponAttackContext attack)
    {
        int owner = attack.Command.OwnerIndex;
        if (owner <= 0 || _speech is null)
            return;

        if (_firingGate.TryGetValue(owner, out float next) && next >= attack.Now)
            return;

        _firingGate[owner] = attack.Now + SpeechWindow;
        _speech.Speak(owner, TFConceptSpeech.MinigunFireWeapon);
    }
}
