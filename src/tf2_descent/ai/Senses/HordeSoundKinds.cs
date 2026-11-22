using SourceSharp.Ai;
using TF2.Descent.Rules.Horde;

namespace TF2.Descent.Ai.Senses;

/// <summary>
/// What a sound the server emitted was, for the horde's hearing (plan_tf2_rpg
/// B.3: weapon fire, blasts, landings, steps, pain and death), from the name it
/// was emitted under: TF2's game sound scripts name a weapon's shot
/// <c>Weapon_*.Single</c> / <c>.SingleCrit</c> / <c>.Shoot</c>, a blast
/// <c>*.Explode</c> / <c>BaseExplosionEffect.Sound</c>, a step <c>*.Step*</c>,
/// a cry <c>*.Pain*</c>, a death <c>*.Death*</c> / <c>*Crit_death*</c>, a fall
/// <c>Player.FallDamage</c>. Anything else is <see cref="SoundKind.Unknown"/>,
/// which no robot listens for (ambience, the HUD, the robots' own bodies).
/// </summary>
public static class HordeSoundKinds
{
    public static SoundKind Classify(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Has(name, "Explo") || Has(name, "Detonate"))
            return SoundKind.Explosion;
        if (Has(name, ".Single") || Has(name, ".Shoot") || Has(name, ".Fire") || Has(name, "Shot"))
            return SoundKind.Gunfire;
        if (Has(name, "Death") || Has(name, "Die") || Has(name, "Crit_death"))
            return SoundKind.Death;
        if (Has(name, "Pain") || Has(name, "FallDamage") || Has(name, "Hurt"))
            return SoundKind.Pain;
        if (Has(name, ".Step") || Has(name, "footstep") || Has(name, "Land"))
            return SoundKind.Footstep;
        if (Has(name, "Door"))
            return SoundKind.Door;
        return SoundKind.Unknown;
    }

    /// <summary>
    /// How far a sound of <paramref name="kind"/> carries for the horde (horde.json's
    /// <c>senses.hearing</c>), in units of walk before the hearer's hearing scale: a blast jump's
    /// landing is <see cref="SoundKind.Physics"/>; a kind no robot listens for carries nowhere.
    /// </summary>
    public static float Reach(HearingSenses hearing, SoundKind kind)
    {
        ArgumentNullException.ThrowIfNull(hearing);
        return (float)(kind switch
        {
            SoundKind.Gunfire => hearing.Gunfire,
            SoundKind.Explosion => hearing.Explosion,
            SoundKind.Footstep => hearing.Footstep,
            SoundKind.Physics => hearing.Landing,
            SoundKind.Pain => hearing.Pain,
            SoundKind.Death => hearing.Death,
            SoundKind.Door => hearing.Door,
            _ => 0,
        });
    }

    /// <summary>Whether a robot listens for <paramref name="kind"/>.</summary>
    public static bool Heard(SoundKind kind) => kind is SoundKind.Gunfire or SoundKind.Explosion or SoundKind.Footstep or SoundKind.Pain
        or SoundKind.Death or SoundKind.Door;

    private static bool Has(string s, string part) => s.Contains(part, StringComparison.OrdinalIgnoreCase);
}
