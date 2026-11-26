using TF2.Server.Econ;
using TF2.Shared;
using TF2.Shared.Movement;

namespace SourceSharp.Ai.TF2.Nav;

/// <summary>
/// What a player's own kit says of how it jumps (round 2's S2): the reach its routes plan with, read off its items as
/// it knows them (<see cref="ITFOwnKit"/>), so an Atomizer Scout plans a third jump. Only a player's own routes read it: what one player knows of another's reach is the class's
/// (<see cref="TFNavProfile.ReachOf(TFClass)"/>). Stateless.
/// <list type="bullet">
/// <item>A Scout dashes once (<c>tf_scout_air_dash_count</c> 1); a weapon's <c>air_dash_count</c> adds dashes while it is
/// in the hands (stock <c>CTFPlayer::CanAirDash</c>, tf_player_shared.cpp:12861-12873: the Atomizer's 1, a third jump).
/// The route plans with the most any weapon of its kit gives; the hands take that weapon out (TFDecider.Dash.cs).</item>
/// <item><c>set_scout_doublejump_disabled</c> 1 takes the dash away (:12875-12878).</item>
/// <item><c>hype_resets_on_jump</c> (the Baby Face's Blaster's 75) spends that much boost on each dash, the meter clamped
/// at 0 (tf_gamemovement.cpp:1008-1016, tf_player_shared.cpp's SetScoutHypeMeter): the dash is always there, free with an
/// empty meter (at spawn), so the BFB plans the double jump as any Scout does (the coordinator's ruling, 2026-11-22). Planning
/// by the bot's own meter is open (docs/todo.md).</item>
/// <item>The Soldier blast jumps whatever he carries; what it costs him is <see cref="TFJumpBudget"/>'s.</item>
/// </list>
/// </summary>
public static class TFJumpKit
{
    /// <summary>The dashes a Scout has with no item's: <c>tf_scout_air_dash_count</c>'s default (tf_player_shared.cpp:113).</summary>
    public const int StockDashes = 1;

    /// <summary>The reach of a player of <paramref name="playerClass"/> carrying <paramref name="kit"/>.</summary>
    public static JumpReach ReachOf(TFClass playerClass, IReadOnlyList<TFOwnKitItem> kit)
    {
        ArgumentNullException.ThrowIfNull(kit);
        if (playerClass != TFClass.Scout)
            return TFNavProfile.ReachOf(playerClass);
        if (Int(kit, TFJump.DoubleJumpDisabledAttrib) == 1)
            return JumpReach.None;
        return Dashes(kit) >= 2 ? JumpReach.Double | JumpReach.Triple : JumpReach.Double;
    }

    /// <summary>The most dashes any weapon of <paramref name="kit"/> gives while in the hands: the stock one and its <c>air_dash_count</c>.</summary>
    public static int Dashes(IReadOnlyList<TFOwnKitItem> kit)
    {
        ArgumentNullException.ThrowIfNull(kit);
        int most = StockDashes;
        foreach (TFOwnKitItem item in kit)
        {
            if (item.WeaponKind >= 0)
                most = Math.Max(most, StockDashes + (int)Value(item, TFJump.AirDashCountAttrib, 0f));
        }

        return most;
    }

    /// <summary>An integer attribute on the player: the first item of the kit that carries <paramref name="attributeClass"/>, else 0.</summary>
    private static int Int(IReadOnlyList<TFOwnKitItem> kit, string attributeClass)
    {
        foreach (TFOwnKitItem item in kit)
        {
            foreach (TFOwnKitAttribute a in item.Attributes)
            {
                if (a.Class == attributeClass)
                    return (int)a.Value;
            }
        }

        return 0;
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
