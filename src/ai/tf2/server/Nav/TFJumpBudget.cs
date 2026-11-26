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
        float share = 1f;
        foreach (TFOwnKitItem item in kit)
        {
            if (item.Slot == TFLoadoutSlot.Primary && item.WeaponKind >= 0)
                share *= Value(item, LauncherDamageScale, 1f);
        }

        foreach (TFOwnKitItem item in kit)
        {
            if (Has(item, NoSelfBlastDamage))
                return 0f;
            share *= Value(item, RocketJumpCostScale, 1f);
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

    private static bool Has(TFOwnKitItem item, string attributeClass)
    {
        foreach (TFOwnKitAttribute a in item.Attributes)
        {
            if (a.Class == attributeClass)
                return true;
        }

        return false;
    }

    private static float Value(TFOwnKitItem item, string attributeClass, float none)
    {
        foreach (TFOwnKitAttribute a in item.Attributes)
        {
            if (a.Class == attributeClass)
                return a.Value;
        }

        return none;
    }
}
