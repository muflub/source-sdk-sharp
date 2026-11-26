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

    /// <inheritdoc cref="ITFWeaponEffects.ShockAttack"/>
    void ShockAttack(in WeaponAttackContext attack, TFWeaponScript script);
}

/// <summary>Stage 2.11's arm: a flamethrower tick's flames and the airblast. One owner, <c>As = [typeof(ITFFlameEffects)]</c>.</summary>
public interface ITFFlameEffects
{
    /// <inheritdoc cref="ITFWeaponEffects.Flames"/>
    void Flames(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot);

    /// <inheritdoc cref="ITFWeaponEffects.AirBlast"/>
    void AirBlast(in WeaponAttackContext attack, TFWeaponScript script);

    /// <inheritdoc cref="ITFWeaponEffects.ExtinguishBeam"/>
    bool ExtinguishBeam(in WeaponAttackContext attack, TFWeaponScript script);
}

/// <summary>The Shortstop's shove (<c>CTFPistol_ScoutPrimary::Push</c>). One owner, <c>As = [typeof(ITFShoveEffects)]</c>.</summary>
public interface ITFShoveEffects
{
    /// <inheritdoc cref="ITFWeaponEffects.Shove"/>
    void Shove(in WeaponAttackContext attack, TFWeaponScript script);
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

/// <summary>The sniper's rail tracer on a tracer item's shot. One owner, <c>As = [typeof(ITFTracerEffects)]</c>.</summary>
public interface ITFTracerEffects
{
    /// <inheritdoc cref="ITFWeaponEffects.Tracer"/>
    void Tracer(in WeaponAttackContext attack, TFWeaponScript script);
}

/// <summary>Stage 2.7's arm: the melee smack's trace and hit. One owner, <c>As = [typeof(ITFMeleeEffects)]</c>.</summary>
public interface ITFMeleeEffects
{
    /// <inheritdoc cref="ITFWeaponEffects.Melee"/>
    void Melee(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, in SwingTrace swing);

    /// <inheritdoc cref="ITFWeaponEffects.MeleeSwingStarted"/>
    void MeleeSwingStarted(in WeaponAttackContext attack, float swingRange);
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

    /// <inheritdoc cref="ITFWeaponEffects.EndClassSpecialSkill"/>
    void EndSpecialSkill(int ownerIndex);
}

/// <summary>
/// The soldier's buff pulse: the deploy's reach and its condition
/// (<c>CTFPlayerShared::PulseRageBuff</c>, <c>tf_player_shared.cpp:13923-14019</c>).
/// One owner, <c>As = [typeof(ITFBuffEffects)]</c>.
/// </summary>
public interface ITFBuffEffects
{
    /// <inheritdoc cref="ITFWeaponEffects.DeployBuff"/>
    void DeployBuff(int ownerIndex, int buffType);

    /// <inheritdoc cref="ITFWeaponEffects.BlowBuffHorn"/>
    void BlowBuffHorn(int ownerIndex, int buffType);

    /// <inheritdoc cref="ITFWeaponEffects.RaiseBuffBanner"/>
    void RaiseBuffBanner(int ownerIndex, int buffType);

    /// <inheritdoc cref="ITFWeaponEffects.UseRage"/>
    void UseRage(int ownerIndex, int buffType);
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

/// <summary>The lunchbox's arms: the eat's taunt, the thrown plate and the bite. One owner, <c>As = [typeof(ITFLunchboxEffects)]</c>.</summary>
public interface ITFLunchboxEffects
{
    /// <inheritdoc cref="ITFWeaponEffects.LunchboxEat"/>
    void LunchboxEat(in WeaponAttackContext attack, TFWeaponScript script);

    /// <inheritdoc cref="ITFWeaponEffects.LunchboxDrop"/>
    void LunchboxDrop(in WeaponAttackContext attack, TFWeaponScript script, int mode);

    /// <summary>
    /// One bite of the held lunch (<c>CTFLunchBox::ApplyBiteEffects</c> through
    /// the heavy's eat arm, tf_player.cpp:18555-18584): the heal, the mode's
    /// buff — asked by the taunt attack while the eat runs.
    /// </summary>
    void Bite(int playerIndex);

    /// <summary>
    /// The scout's drink (<c>DoTauntAttack</c>'s scout arm through the held
    /// energy drink, tf_player.cpp:18636-18675): the buff the held drink's mode
    /// names, the steered hand and the last item back — asked by the taunt
    /// attack at the end of the raise.
    /// </summary>
    void Drink(int playerIndex);

    /// <summary>
    /// The lunchbox's thrown pack goes with the weapon that threw it
    /// (<c>CTFLunchBox::UpdateOnRemove</c>, shared/tf/tf_weapon_lunchbox.cpp:100-112's
    /// GAME_DLL arm): when the weapon entity is removed — the spawn's
    /// <c>RemoveAllItems</c>, the class switch and the respawn alike — the pack
    /// still out from the last throw goes too, so it cannot be taken back by
    /// switching away and back. The no-op default is the stubs' shape; the
    /// lunchbox owner removes the thrown kit.
    /// </summary>
    void OnWeaponRemoved(int playerIndex) { }
}

/// <summary>
/// The server's <see cref="ITFWeaponEffects"/>: one owner for the shared
/// behaviours' effect calls, which hands each to the stage that owns that
/// effect, so the projectile, flame, medigun and scope lanes each register an
/// owner of their own seam without touching this file. Every seam is a
/// required dependency: the composition holds an owner for each (the boot
/// audit named all thirteen live), and a registration missing one fails at
/// ServiceActivator instead of spending the shot on nothing.
/// </summary>
[SideService(Side.Server, As = [typeof(ITFWeaponEffects)])]
public sealed class TFWeaponEffects(ITFProjectileEffects projectiles, ITFFlameEffects flames,
                                    ITFHealEffects heal, ITFZoomEffects zoom,
                                    ITFClassSkillEffects skills, ITFItemSwitchEffects itemSwitch,
                                    ITFMeleeEffects melee, ITFMinigunEffects minigun,
                                    ITFTracerEffects tracer, ITFLunchboxEffects lunchbox,
                                    ITFBuffEffects buffs, ITFShoveEffects shove,
                                    TFConceptSpeech speech) : ITFWeaponEffects
{
    private readonly TFConceptSpeech _speech = speech;

    private readonly ITFShoveEffects _shove = shove;

    public void Projectile(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, float charge)
        => projectiles.Projectile(in attack, script, shot, charge);

    public void Detonate(in WeaponAttackContext attack, TFWeaponScript script)
        => projectiles.Detonate(in attack, script);

    public void Shove(in WeaponAttackContext attack, TFWeaponScript script)
        => _shove.Shove(in attack, script);

    public void ShockAttack(in WeaponAttackContext attack, TFWeaponScript script)
        => projectiles.ShockAttack(in attack, script);

    public void Flames(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot)
        => flames.Flames(in attack, script, shot);

    public void AirBlast(in WeaponAttackContext attack, TFWeaponScript script)
        => flames.AirBlast(in attack, script);

    public bool ExtinguishBeam(in WeaponAttackContext attack, TFWeaponScript script)
        => flames.ExtinguishBeam(in attack, script);

    public void Heal(in WeaponAttackContext attack, TFWeaponScript script, bool held)
        => heal.Heal(in attack, script, held);

    public void Zoom(in WeaponAttackContext attack, TFWeaponScript script, bool zoomed)
        => zoom.Zoom(in attack, script, zoomed);

    public void Tracer(in WeaponAttackContext attack, TFWeaponScript script)
        => tracer.Tracer(in attack, script);

    public bool ClassSpecialSkill(in WeaponAttackContext attack)
        => skills.DoSpecialSkill(attack.Command.OwnerIndex);

    public void EndClassSpecialSkill(in WeaponAttackContext attack)
        => skills.EndSpecialSkill(attack.Command.OwnerIndex);

    public void SelectLast(in WeaponAttackContext attack, TFWeaponScript script)
        => itemSwitch.SelectLast(in attack, script);

    public void Melee(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, in SwingTrace trace)
        => melee.Melee(in attack, script, shot, in trace);

    public void MeleeSwingStarted(in WeaponAttackContext attack, float swingRange)
        => melee.MeleeSwingStarted(in attack, swingRange);

    public void AttackEnemyProjectiles(in WeaponAttackContext attack, int projectiles, bool critical)
        => minigun.AttackEnemyProjectiles(in attack, projectiles, critical);

    public void RingOfFire(in WeaponAttackContext attack, int damage)
        => minigun.RingOfFire(in attack, damage);

    public void Rearmed(in WeaponAttackContext attack)
        => minigun.Rearmed(in attack);

    public void LunchboxEat(in WeaponAttackContext attack, TFWeaponScript script)
        => lunchbox.LunchboxEat(in attack, script);

    public void LunchboxDrop(in WeaponAttackContext attack, TFWeaponScript script, int mode)
        => lunchbox.LunchboxDrop(in attack, script, mode);

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
        if (owner <= 0)
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
        if (owner <= 0)
            return;

        if (_firingGate.TryGetValue(owner, out float next) && next >= attack.Now)
            return;

        _firingGate[owner] = attack.Now + SpeechWindow;
        _speech.Speak(owner, TFConceptSpeech.MinigunFireWeapon, attack.Type.Name,
                      TFMinigunBehaviour.FiringDuration(attack.Weapon, attack.Now));
    }

    public void DeployBuff(in WeaponAttackContext attack, int buffType)
        => buffs.DeployBuff(attack.Command.OwnerIndex, buffType);

    public void BlowBuffHorn(in WeaponAttackContext attack, int buffType)
        => buffs.BlowBuffHorn(attack.Command.OwnerIndex, buffType);

    /// <summary>
    /// The raise, then the battle cry (<c>RaiseFlag</c>'s order :242-252):
    /// the seam runs the event and the flag sound, the cry rides the voice
    /// lane after them, still before the buff's raise in
    /// <see cref="DeployBuff"/>.
    /// </summary>
    public void RaiseBuffBanner(in WeaponAttackContext attack, int buffType)
    {
        int owner = attack.Command.OwnerIndex;
        buffs.RaiseBuffBanner(owner, buffType);
        if (owner > 0)
            _speech.Speak(owner, TFConceptSpeech.PlayerBattlecry);
    }

    public void UseRage(in WeaponAttackContext attack, int buffType)
        => buffs.UseRage(attack.Command.OwnerIndex, buffType);

    /// <summary><c>ActivateRageBuff</c>'s case 6 (tf_player_shared.cpp:13788-13789): the Focus's battle cry.</summary>
    public void FocusBattleCry(in WeaponAttackContext attack)
    {
        int owner = attack.Command.OwnerIndex;
        if (owner > 0)
            _speech.Speak(owner, TFConceptSpeech.PlayerBattlecry);
    }
}
