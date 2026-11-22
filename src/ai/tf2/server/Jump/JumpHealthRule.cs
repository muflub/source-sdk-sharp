using TF2.Server.Combat;
using TF2.Server.Entities.Items;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>
/// The health a Soldier may spend on a planned rocket jump (lane G9, the user's ruling of 2026-11-13: "should be a
/// planned jump to a health pack near by for fast mobility"). Two ways a jump is paid for:
/// <list type="bullet">
/// <item><b>Plain</b>: the health after the blast is at least the floor (<see cref="Trees.TFDecider.RocketJumpHealthFloor"/>, 90).</item>
/// <item><b>By health</b>: a heal place the bot may use lies within <see cref="HealWalk"/> of the landing along the graph
/// (<see cref="JumpHeals"/>), the health it lands with after the blast and the worst fall (<see cref="Fall"/>) is at least
/// <see cref="HardFloor"/>, and that and what the place gives back (<see cref="Pays"/>) is at least the floor: the place
/// pays for the jump. A cabinet refills a player, so it pays for anything over the hard floor.</item>
/// </list>
/// A jump that lands by no place keeps the plain rule. Stateless.
/// </summary>
public static class JumpHealthRule
{
    /// <summary>
    /// How far along the graph from a jump's landing a heal place may be (no blast link on the way): 1.6 s at a
    /// Soldier's 240 u/s run, so a bot under its floor is under it no longer than about three of a person's reactions
    /// before it stands on the pack, and "near by" means near.
    /// </summary>
    public const float HealWalk = 384f;

    /// <summary>
    /// The least a jump by health may land with, after its blast and its worst fall: a rocket's splash at the edge of its
    /// radius (the stock launcher's 90 halved, 45) with a margin, the commonest stray hit in a game of Soldiers; it also
    /// covers one more of the bot's own blasts at its feet (45 to 50).
    /// </summary>
    public const float HardFloor = 50f;

    /// <summary>The fall spread's top (<c>RandomFloat(0.8, 1.2)</c> in TF2's rule): the worst a landing can cost.</summary>
    public const float FallSpreadTop = 1.2f;

    /// <summary>The world's gravity a flight falls under (TF2's sv_gravity), units a second squared.</summary>
    public const float Gravity = 800f;

    /// <summary>
    /// The worst a landing costs a player of <paramref name="maxHealth"/> who falls <paramref name="drop"/> units from rest
    /// (a flight's apex over its touch): TF2's rule (<see cref="TFFallDamage.Damage"/>) at the top of its spread; 0 under
    /// the safe speed.
    /// </summary>
    public static float FallOf(float drop, int maxHealth)
    {
        float speed = drop > 0f ? MathF.Sqrt(2f * Gravity * drop) : 0f;
        return TFFallDamage.Damage(speed, maxHealth, disableSpread: false, static (_, top) => top);
    }

    /// <summary>The worst landing of <paramref name="plan"/>: the drop from its apex (over its takeoff) to its touch.</summary>
    public static float Fall(JumpPlan plan, int maxHealth)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return FallOf(plan.Recipe.Takeoff.Z + plan.Apex - plan.Touch.Z, maxHealth);
    }

    /// <summary>
    /// What a place of <paramref name="kind"/> gives a player of <paramref name="maxHealth"/>: a pack its share of the
    /// player's maximum, rounded up as the game rounds it (<c>HealthKit</c>: <c>ceil(MaxHealth × PackRatio)</c>, small 0.2,
    /// medium 0.5, full 1.0; a pack of no size as small); a cabinet (<paramref name="cabinet"/>) all of it.
    /// </summary>
    public static float Pays(bool cabinet, ItemKind kind, int maxHealth)
    {
        if (cabinet)
            return maxHealth;
        PowerupSize size = kind switch
        {
            ItemKind.HealthMedium => PowerupSize.Medium,
            ItemKind.HealthLarge => PowerupSize.Full,
            _ => PowerupSize.Small,
        };
        return MathF.Ceiling(maxHealth * TFPowerup.PackRatios(size));
    }

    /// <summary>Whether <paramref name="kind"/> is a health pack's.</summary>
    public static bool IsPack(ItemKind kind) => kind is ItemKind.HealthSmall or ItemKind.HealthMedium or ItemKind.HealthLarge or ItemKind.Health;

    /// <summary>The plain rule: <paramref name="health"/> less the blast's <paramref name="cost"/> is at least <paramref name="floor"/>.</summary>
    public static bool Plain(float health, float cost, float floor) => health - cost >= floor;

    /// <summary>
    /// The rule by health: what the bot lands with (<paramref name="health"/> less <paramref name="cost"/> and
    /// <paramref name="fall"/>) is at least <see cref="HardFloor"/>, and with what the place <paramref name="pays"/> at least
    /// <paramref name="floor"/>.
    /// </summary>
    public static bool ByHealth(float health, float cost, float fall, float pays, float floor)
    {
        float lands = health - cost - fall;
        return lands >= HardFloor && lands + pays >= floor;
    }

    /// <summary>
    /// What a route search spends on a link of <paramref name="cost"/> that lands by a place that <paramref name="pays"/>
    /// (with the worst <paramref name="fall"/>), against a budget of the health over <paramref name="floor"/>: spending it
    /// is allowed exactly when <see cref="Plain"/> or <see cref="ByHealth"/> holds. Both of the latter's parts read
    /// <c>cost + fall - k &lt;= health - floor</c> (k the place's pay, or the floor less the hard floor), so the spend is the
    /// cost and the fall less the smaller k, and never more than the cost (the plain rule). It may be under 0 (a cheap jump
    /// by a big pack): the budget it is set against is then under 0 too for a bot under the floor
    /// (<see cref="Nav.TFNavProfile.LinkHealthOf"/>), and the rule stays exact.
    /// </summary>
    public static float Spend(float cost, float fall, float pays, float floor)
        => MathF.Min(cost, cost + fall - MathF.Min(pays, floor - HardFloor));
}
