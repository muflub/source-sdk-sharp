using SourceSharp.Ai.TF2.Jump;
using TF2.Server.Econ;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Nav;

/// <summary>
/// What a blast jump costs a bot by its own kit, as its route planner budgets it (round 2's S13): the level's links are
/// costed for the stock launcher and no wearables (<c>BlastJumpSims</c>), so a bot's kit scales each link's blast by
/// <see cref="CostShare"/>, the same share the Soldier's tree pays at the link (the Soldier lane's <c>BlastCostShare</c>,
/// TFDecider.Jump.cs, abc405d4c), so the plan and the tree agree. The floor is the tree's
/// (<see cref="Trees.TFDecider.RocketJumpHealthFloor"/>) for every kit. Stateless.
/// </summary>
public static class TFJumpBudget
{
    /// <summary>The kit attributes that scale a jump's cost, as the tree reads them.</summary>
    public const string RocketJumpCostScale = "rocket_jump_dmg_reduction", NoSelfBlastDamage = "no_self_blast_dmg", LauncherDamageScale = "mult_dmg";

    /// <summary>
    /// The share of a link's blast <paramref name="kit"/> pays: the self blast is the rocket's own damage, which the
    /// launcher's <c>mult_dmg</c> scales (stock's <c>GetProjectileDamage</c>: the Direct Hit's 1.25), times every item's
    /// <c>rocket_jump_dmg_reduction</c> (<c>TFBlastApplier</c>: the Gunboats' 0.4); 0 with <c>no_self_blast_dmg</c> on any
    /// item (the Rocket Jumper). 1 for the stock kit.
    /// </summary>
    public static float CostShare(IReadOnlyList<TFOwnKitItem> kit)
    {
        ArgumentNullException.ThrowIfNull(kit);
        return Share(kit, static i => i.Slot, static i => i.WeaponKind, static i => i.Attributes);
    }

    /// <summary>The same share over the bot's HUD kit, as its tree reads it at the link (<c>TFDecider.BlastCostShare</c>):
    /// one rule for the plan and the tree.</summary>
    public static float CostShare(IEnumerable<Senses.TFKitItem> kit)
    {
        ArgumentNullException.ThrowIfNull(kit);
        return Share(kit, static i => i.Slot, static i => i.WeaponKind, static i => i.Attributes);
    }

    private static float Share<T>(IEnumerable<T> kit, Func<T, TFLoadoutSlot> slotOf, Func<T, int> kindOf, Func<T, IReadOnlyList<TFOwnKitAttribute>> attributesOf)
    {
        float share = 1f;
        foreach (T item in kit)
        {
            if (slotOf(item) == TFLoadoutSlot.Primary && kindOf(item) >= 0)
                share *= Value(attributesOf(item), LauncherDamageScale, 1f);
        }

        foreach (T item in kit)
        {
            IReadOnlyList<TFOwnKitAttribute> attributes = attributesOf(item);
            if (Has(attributes, NoSelfBlastDamage))
                return 0f;
            share *= Value(attributes, RocketJumpCostScale, 1f);
        }

        return MathF.Max(0f, share);
    }

    /// <summary>
    /// What a bot whose blasts cost <paramref name="share"/> of the stock's spends on each of <paramref name="set"/>'s links:
    /// the link's published spend (its blast, less what a pack by its landing gives back: <see cref="JumpHealthRule.Spend"/>)
    /// with its blast scaled. The pack's part (the fall less the pay) does not move with the blast, so the spend is the
    /// published one less the blast's unpaid share, exactly. Null for the stock share: the links' own.
    /// </summary>
    public static float[]? Spends(JumpLinkSet set, float share)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (share == 1f)
            return null;
        var spends = new float[set.Links.Count];
        for (int i = 0; i < spends.Length; i++)
            spends[i] = set.Links[i].Health - (set.JumpOf(i).Health * (1f - share));
        return spends;
    }

    private static bool Has(IReadOnlyList<TFOwnKitAttribute> attributes, string attributeClass)
    {
        foreach (TFOwnKitAttribute a in attributes)
        {
            if (a.Class == attributeClass)
                return true;
        }

        return false;
    }

    private static float Value(IReadOnlyList<TFOwnKitAttribute> attributes, string attributeClass, float none)
    {
        foreach (TFOwnKitAttribute a in attributes)
        {
            if (a.Class == attributeClass)
                return a.Value;
        }

        return none;
    }
}
