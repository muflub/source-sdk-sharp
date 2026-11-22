using SourceSharp.Ai.TF2.Senses;

namespace SourceSharp.Ai.TF2.Knowledge;

/// <summary>
/// A weapon's facts as the item in the bot's hands makes them (plan_bots_teams_tf2 P7; the Soldier research's ask):
/// the facts of its kind (the map's weapon table, from the script) with the item's own attributes applied, as its
/// tooltip states them and the game's hooks apply them: the clip (<c>mult_clipsize</c>: a Black Box holds 3), the
/// cadence (<c>mult_postfiredelay</c>), the reload (<c>mult_reload_time</c>, <c>mult_reload_time_hidden</c>) and the
/// projectile's speed (<c>mult_projectile_speed</c>: a Liberty Launcher's 1.4). A player knows them all.
/// </summary>
public static class TFItemFacts
{
    public const string ClipScale = "mult_clipsize", CadenceScale = "mult_postfiredelay", ReloadScale = "mult_reload_time",
                        HiddenReloadScale = "mult_reload_time_hidden", SpeedScale = "mult_projectile_speed";

    /// <summary><paramref name="kind"/>'s facts with <paramref name="item"/>'s attributes applied.</summary>
    public static WeaponFacts Apply(in WeaponFacts kind, TFKitItem item)
    {
        float clip = item.Attribute(ClipScale, 1f), cadence = item.Attribute(CadenceScale, 1f), speed = item.Attribute(SpeedScale, 1f);
        float reload = item.Attribute(ReloadScale, 1f) * item.Attribute(HiddenReloadScale, 1f);
        // Stock's CTFWeaponBase::GetMaxClip1 hooks mult_clipsize on a clip that has one (tf_weaponbase.cpp:571-575).
        int clipSize = kind.ClipSize > 0 ? (int)MathF.Round(kind.ClipSize * clip) : kind.ClipSize;
        return kind with
        {
            ClipSize = clipSize,
            CadenceSeconds = kind.CadenceSeconds * cadence,
            ReloadSeconds = kind.ReloadSeconds * reload,
            ProjectileSpeed = kind.ProjectileSpeed * speed,
        };
    }
}
