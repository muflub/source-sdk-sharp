using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// One hit, as the rules saw it: who was hurt, who hurt them, and the
/// engine's own numbers for the blow (tf2 stage 0.7 part C).
///
/// <para>
/// <b>WHAT THIS CARRIES, AND WHAT IT DELIBERATELY DOES NOT.</b> The plan
/// sketched a context with inflictor, hitgroup, position and force as well.
/// The one caller — the player's own damage path, <c>SsPlayer.OnDamagedAt</c>
/// asking <see cref="IGameRules.CanTakeDamage"/> as stock's
/// <c>CBasePlayer::OnTakeDamage</c> asks <c>FPlayerCanTakeDamage</c> — hands
/// over these values and nothing more: a context field that could only ever
/// read zero is a guess at a shape the build does not have, and this repo's
/// own failure catalogue is full of a plausible zero read as a real answer.
/// </para>
///
/// <para>
/// <b><see cref="HitGroup"/></b> arrived with tf2 stage 0.8: the bullet's
/// trace answers <c>tr.hitgroup</c> (<see cref="SurfaceTraceResult.HitGroup"/>)
/// and <see cref="DamageHitGroups"/> carries it to the victim.
/// <see cref="Abi.HitGroup.Generic"/> for every hit with no trace behind it — a
/// blast, a fall, a trigger — which is stock's zero. Inflictor, position and
/// force still need a caller that has them; when a field gets a real source it
/// is added here, one at a time, never all at once on spec.
/// </para>
///
/// <para>
/// <b><see cref="Critical"/></b> arrived with tf2 stage 1: a weapon behaviour's
/// <see cref="SourceSharp.Shared.ShotDamage"/> decides it, the arms open it
/// beside the damage call with the hitgroup (<see cref="DamageHitGroups.Critical"/>),
/// and the rules read it here. False for every hit no weapon called critical.
/// <see cref="Damage"/> is already the weapon's: a crit's multiplier is applied
/// by the mod that rolled it, not by the core.
/// </para>
/// </summary>
public readonly record struct DamageContext(EntityHandle Victim, EntityHandle Attacker,
                                            float Damage, int DamageType,
                                            HitGroup HitGroup = HitGroup.Generic,
                                            bool Critical = false);

/// <summary>
/// Whether one hit may hurt a player, asked of every registered rule after the
/// core's own answers (tf2 stage 0.7 part C).
///
/// <para>
/// A <b>CHAIN</b> (<see cref="IHooks{T}"/>, asked in
/// <see cref="IOrderedHook.HookOrder"/>'s order): a damage-immunity zone, a
/// mode's invulnerability window and a mod's god-mode all refuse for their own
/// reason, and each is a rule rather than an edit of the one that was there
/// first. The first rule to give an actual answer wins, exactly as the other
/// decision chains in this build; <see cref="Decision.UseStockRule"/> is "no
/// opinion", so a rule that has none costs nothing.
/// </para>
///
/// <para>
/// <b>WHERE IT SITS, AND WHICH WAY IT CAN MOVE THE ANSWER.</b> The rules are
/// asked only when the core's own checks — spawn protection and friendly fire —
/// DEFER. They can refuse a hit the core allowed, because "no" from a rule is
/// a player staying alive, which is the safe direction. They cannot ALLOW a
/// hit the core refused: spawn protection that a mod could overrule is not the
/// protection the convar promises, and a friendly-fire rule a chain could
/// reverse would make <c>sv_allhitscan</c>-style server policy depend on which
/// mods happened to be installed. A rule that wants the core's own guard
/// lifted changes the convar, not the chain.
/// </para>
///
/// <para>
/// <b>The answer is the only output.</b> The plan's name for this method was
/// <c>Modify(ref DamageContext)</c>, which promises a rule can rewrite the
/// damage. It cannot: the ABI that reaches the game DLL returns a
/// <see cref="Decision"/> and nothing else, so a rewritten damage number would
/// be read by nobody. Scaling a hit is <see cref="IDamageModifier"/>'s, which
/// the player's own damage path asks once the hit has been allowed.
/// </para>
/// </summary>
public interface IDamageRule
{
    /// <summary>
    /// Allow the hit, refuse it, or decline to say. Declining is the common
    /// answer and the cheap one; a rule that throws is named and treated as a
    /// declination, because a broken immunity zone must not kill the player it
    /// was guarding nor let every other rule off the hook.
    /// </summary>
    Decision Decide(in DamageContext hit);
}

/// <summary>
/// What a hit the rules allowed does to a player, folded over every registered
/// modifier (tf2 stage 2.7): stock's <c>ApplyOnDamageModifyRules</c> seat, where
/// TF scales a hit for its crit, its range and the victim's conditions.
///
/// <para>
/// <b>WHO ASKS, AND WHEN.</b> The player's own damage path
/// (<c>SsPlayer.OnDamagedAt</c>), through <see cref="IGameRules.ModifyDamage"/>,
/// after <see cref="IGameRules.CanTakeDamage"/> allowed the hit and before the
/// suit takes its share: stock's <c>CTFPlayer::OnTakeDamage</c> calls the
/// modify rules before <c>CBasePlayer</c>'s armour block. Both ends are managed
/// and the answer is what the player takes, so this needs no ABI: the number
/// a modifier returns is the number the health rule is handed.
/// </para>
///
/// <para>
/// A <b>FOLD</b> in <see cref="IOrderedHook.HookOrder"/>'s order: each modifier
/// is handed the running damage (the first gets <see cref="DamageContext.Damage"/>,
/// the hit as dealt) and returns the next. The context is the hit as dealt, the
/// same for every modifier. A modifier that throws is named and skipped, the
/// running damage unchanged: a broken modifier must not turn a hit into a kill
/// or a miss. With no modifier the hit lands as dealt, which is the core's game.
/// Zero or less is no damage.
/// </para>
/// </summary>
public interface IDamageModifier
{
    /// <summary>The damage the hit does after this modifier, from the running <paramref name="damage"/>.</summary>
    float Modify(in DamageContext hit, float damage);
}
