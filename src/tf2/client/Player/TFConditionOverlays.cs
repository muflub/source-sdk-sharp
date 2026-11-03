//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;

using TF2.Shared;
using TF2.Shared.Conditions;

namespace TF2.Client.Player;

/// <summary>
/// The condition screen overlays, the local player's halves of stock's
/// OnAddBurning / OnRemoveBurning / OnAddInvulnerable / OnRemoveInvulnerable /
/// OnAddUrine / OnRemoveUrine / OnAddBleeding / OnRemoveBleeding
/// (<c>tf_player_shared.cpp:7308-7316, 6873-6882, 3442-3467, 3507-3519,
/// 3600-3607, 3629-3638, 4121-4131, 4141-4152</c>): the full-screen material
/// the burning, the invulnerable, the jarated and the bleeding local player
/// wears, and the removal clearing it only when the standing overlay is its
/// own — an invulnerable going off leaves a burning's overlay, stock's
/// name-checked clear.
///
/// <para>
/// The invulnerable overlay wears the player's team, blue by stock's switch
/// default (:3446-3455). The milk's overlay is commented out in stock
/// (:3832-3839, 3857-3863) and the mega-heal's pair (:6391-6411) is its own
/// condition's later arm; the stealth and gas overlays' conditions are not
/// this lane's.
/// </para>
/// </summary>
[SideService(Side.Client)]
public sealed class TFConditionOverlays(
    IEngineHud hud,
    TFClientPlayerResources resources) : ITFClientConditionHook, IOrderedHook
{
    /// <summary>The overlays, stock's material defines (:223-229).</summary>
    public const string Burning = "effects/imcookin";
    public const string InvulnerableRed = "effects/invuln_overlay_red";
    public const string InvulnerableBlue = "effects/invuln_overlay_blue";
    public const string Urine = "effects/jarate_overlay";
    public const string Bleeding = "effects/bleed_overlay";

    /// <summary>The overlay a removal may clear, per condition: stock's name check.</summary>
    private static string? Clearable(TFCond cond) => cond switch
    {
        TFCond.Burning => Burning,
        TFCond.Invulnerable => null, // either invuln material, decided at the clear
        TFCond.Urine => Urine,
        TFCond.Bleeding => Bleeding,
        _ => null,
    };

    public int HookOrder => TFClientConditionOrders.Overlays;

    public void OnAdded(EntityHandle player, TFCond cond)
    {
        if (player.EntryIndex != hud.LocalPlayerIndex)
            return;

        switch (cond)
        {
            case TFCond.Burning:
                hud.ScreenOverlaySet(Burning);

                break;

            case TFCond.Invulnerable:
                hud.ScreenOverlaySet(TeamOf(player) == (int)TFTeam.Red ? InvulnerableRed : InvulnerableBlue);

                break;

            case TFCond.Urine:
                hud.ScreenOverlaySet(Urine);

                break;

            case TFCond.Bleeding:
                hud.ScreenOverlaySet(Bleeding);

                break;
        }
    }

    public void OnRemoved(EntityHandle player, TFCond cond)
    {
        if (player.EntryIndex != hud.LocalPlayerIndex)
            return;

        string? clearable = cond == TFCond.Invulnerable
            ? null
            : Clearable(cond);
        string standing = hud.ScreenOverlayGet();

        bool mine = cond == TFCond.Invulnerable
            ? standing is InvulnerableRed or InvulnerableBlue
            : standing == clearable;

        if (mine)
            hud.ScreenOverlaySet(string.Empty);
    }

    /// <summary>The scoreboard's team row; 0 is stock's switch default, blue.</summary>
    private int TeamOf(EntityHandle player)
        => resources.Live is { } resource ? resource.State.Team[player.EntryIndex] : 0;
}
