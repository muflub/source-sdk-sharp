//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

/// <summary>
/// The respawn countdown (stock's <c>UpdateReinforcements</c>,
/// game/client/tf/tf_spectatorgui.cpp:343-447) at stock's own look and
/// layout: spectator.res's ReinforcementsLabel at c-300 50 600x18 —
/// centred on the screen, 50 units from its top — in the
/// HudFontMediumSmallSecondary role. The arms are stock's (:363-447): a
/// sudden-death stalemate names itself ("No respawning in Sudden Death"),
/// a team win says to wait for the next round, a wave not yet due says
/// "Prepare to respawn", a single second takes its own sentence and the
/// rest count down through the #TF2_Respawn_InSecs token's %s1.
///
/// <para>
/// The label rides only while the local player is dead (stock's gate:
/// the spectator gui draws it for a dying player, :343-360) — the wave
/// time is 0 while alive, so an ungated label would show the stalemate
/// and next-round arms through the living rounds too.
/// </para>
/// </summary>
public sealed class TFRespawnTimer(ITFLocalPlayer local, ITFRespawnCountdown countdown,
                                   IUiLocalisation localise) : UiScreen
{
    /// <summary>The countdown sentence split around its %s1 once: the
    /// token's word order is the language's, and Describe may not
    /// allocate (SSUI001) — the constructor composes the two halves.</summary>
    private readonly string[] _halves = Halves(localise);

    public override string Name => "tf2respawntimer";

    public override UiLayerKind Layer => UiLayerKind.Hud;

    /// <summary>Stock's gate: the label is the dying player's (:343-360).
    /// The wave time is 0 while alive; the stalemate and next-round arms
    /// hold through the living rounds, so death carries them.</summary>
    public override bool Visible => local.Team is Red or Blue && local.Health <= 0
        && (countdown.Stalemate || countdown.TeamWin || countdown.HasWave);

    public override void Describe(ref UiFrame ui)
    {
        var at = StockRes.Spot(Stock.Label.Rect(StockRect.Screen), StockRect.Screen);
        // The plate stands in for stock's text outline over the deathcam
        // (our contrast rule's Surface requirement, as the freeze panel's
        // plates do).
        ui.Rect(Tone.Surface, Size.Du(Math.Min(at.Wide, ui.DesignWidth)), Size.Du(at.Tall))
            .AtRes(UiAnchor.Top, at.CentreX, at.Top).Layered().Key("respawn plate");
        UiBox label = countdown.Stalemate ? ui.Text("#TF2_Respawn_Stalemate", Text.Body, Tone.Text)
            : countdown.TeamWin ? ui.Text("#TF2_Respawn_NextRound", Text.Body, Tone.Text)
            : countdown.WaitSeconds <= 0 ? ui.Text("#TF2_Respawn_Now", Text.Body, Tone.Text)
            : countdown.WaitSeconds == 1 ? ui.Text("#TF2_Respawn_InSec", Text.Body, Tone.Text)
            : ui.Text($"{_halves[0]}{countdown.WaitSeconds}{_halves[1]}", Text.Body, Tone.Text);
        label.Width(Size.Du(Math.Min(at.Wide, ui.DesignWidth)))
            .TextAlign(Align.Centre)
            .AtRes(UiAnchor.Top, at.CentreX, at.Top).Layered().Key("respawn");
    }

    /// <summary>The sentence around its %s1 (TextMessages' %s1..%s4): the
    /// token's word order is the language's.</summary>
    private static string[] Halves(IUiLocalisation localise)
    {
        string token = localise.Find("#TF2_Respawn_InSecs") ?? "";
        int cut = token.IndexOf("%s1", StringComparison.Ordinal);
        return cut < 0 ? [token, ""] : [token[..cut], token[(cut + 3)..]];
    }

    /// <summary>The .res box on the 640x480 canvas spectator.res is
    /// written in: the ReinforcementsLabel at c-300 50 600x18
    /// (spectator.res:69-97) — the "c-300" xpos is the 600-wide label's
    /// LEFT edge 300 units left of the canvas centre, which puts its
    /// centre on the screen's; ypos 50 its top edge from the screen's
    /// top. Its centre alignment makes the clamp to the du width the
    /// screen's own.</summary>
    internal static class Stock
    {
        public static readonly StockBox Label = new("c-300", "50", 600, 18);
    }

    // The raw TF team numbers (tf_shareddefs.h): 2 RED, 3 BLU.
    private const int Red = 2;
    private const int Blue = 3;
}
