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
/// The death notices, top right (stock's <c>CTFHudDeathNotice</c>,
/// tf_hud_deathnotice.cpp over hud_basedeathnotice.cpp), in the stock
/// layout: each <see cref="ITFKillFeed"/> line is a rounded bar whose right
/// edge sits 12 units from the screen's (hudlayout.res:498-528 —
/// HudDeathNotice xpos r640 wide 628 on the 640-wide .res space), the first
/// bar's top <c>ypos 18 + YRES(16) + 1</c> down (Paint's
/// <c>GetDeathMessageStartHeight</c>, baseviewport.cpp:752-755, and the
/// y+1 polygon, hud_basedeathnotice.cpp:252), the next 20 units lower
/// (LineHeight 16 + LineSpacing 4, hudlayout.res:512-513) — all × the 2.25
/// du scale. Each bar carries the killer's name in their team's tone, the
/// death icon (<see cref="IconFor"/>: a line the local player is in asks the
/// inverted <c>dneg_</c> art first, hud_basedeathnotice.cpp:891-918, and a
/// miss falls to the skull, :802-807), and the victim's name in theirs
/// (<c>GetTeamColor</c>); a line the local player is in takes the light bar
/// and the dark name colour (LocalBackgroundColor 245 229 196 is the scheme's
/// TanLight — the theme's Text tone; LocalPlayerColor HUDBlack is
/// GetInfoTextColor's local arm, tf_hud_deathnotice.cpp:1617-1625). Stock's
/// team tones ride the translucent bars (alpha 220/200); here the bars are
/// the opaque tones, as the core feed ruled (the theme's contrast rule), and
/// on the light bar both names take the dark ink where stock kept the team
/// tones at under 2:1. A crit's <c>d_crit</c> glyph rides behind the icon at
/// its own box (hud_basedeathnotice.cpp:290-294). A suicide draws no killer
/// — stock clears the name and keeps the weapon's icon (:513-520).
/// </summary>
public sealed class TFKillFeed(ITFKillFeed feed, IUiTextureLibrary textures) : UiScreen
{
    public override string Name => "tfkillfeed";

    public override UiLayerKind Layer => UiLayerKind.Hud;

    // The core HUD's kill feed: this screen replaces it (Hud.cs's hud.kills gate).
    private readonly string[] _takes = ["hud.kills"];

    public override IReadOnlyList<string> Takes => _takes;

    // The stock numbers, in the 640x480 .res space; resolved to du per Describe.
    private static class Stock
    {
        public const float PanelTop = 18;      // hudlayout.res:504
        public const float StartHeight = 16;   // YRES(16), baseviewport.cpp:752-755
        public const float LineHeight = 16;    // hudlayout.res:512
        public const float RightInset = 12;    // 640 − wide 628 (:503,:507)
        public const float PolygonTop = 1;     // the bg polygon's y+1 (:252)
        public const float IconInset = 2;      // iconTallDesired = iLineTall − YRES(2) (:183)
    }

    public override void Describe(ref UiFrame ui)
    {
        var f = feed;
        if (f.LineCount == 0)
            return;

        // The bar: 14 units tall (the y+1 .. y+15 polygon, :252) — the icon
        // scales to the same (iLineTall − YRES(2), :183-189). The pitch is the
        // bar plus the line spacing (20 units, hudlayout.res:512-513): the
        // column's token step carries it (stock's 13.5 du gap is the nearest
        // step down).
        float barTall = (Stock.LineHeight - Stock.IconInset) * StockRes.Scale;
        float inset = Stock.RightInset * StockRes.Scale;
        float top = (Stock.PanelTop + Stock.StartHeight + Stock.PolygonTop) * StockRes.Scale;

        using (ui.Column(Space.M, Align.End).AtRes(UiAnchor.TopRight, inset, top))
        {
            for (int i = 0; i < f.LineCount; i++)
            {
                var l = f.Line(i);
                using (ui.Row(Space.XS, Align.Centre)
                           .Height(Size.Du(barTall))
                           .Fill(l.LocalInvolved ? Tone.Text : Tone.Window)
                           .Round(Radius.L)
                           .Pad(Space.XL, Space.None)
                           .Key(i))
                {
                    var ink = l.LocalInvolved ? Tone.OnAccent : Tone.Text;
                    if (l.Killer.Length > 0)
                        ui.Data(l.Killer, Text.Body, NameColour(l.KillerTeam, ink))
                            .Elide().MaxWidth(280).Name("killer");

                    // The death icon, over the crit glyph at the same box
                    // (:290-294), scaled to the bar keeping its table aspect
                    // (:179-189). No icon, no box — stock's icon simply does
                    // not draw (the skull fallback answered in IconFor).
                    if (IconFor(textures, l) is { } icon && icon.Tall > 0)
                    {
                        float iconWide = barTall * icon.Wide / icon.Tall;
                        using (ui.Box().Size(Size.Du(iconWide), Size.Du(barTall)).Name("icon box"))
                        {
                            if (l.Crit && textures.Icon("d_crit") is { } crit)
                                Draw(ref ui, "crit", crit, iconWide, barTall);
                            Draw(ref ui, "icon", icon, iconWide, barTall);
                        }
                    }

                    ui.Data(l.Victim, Text.Body, NameColour(l.VictimTeam, ink))
                        .Elide().MaxWidth(280).Name("victim");
                }
            }
        }
    }

    /// <summary>GetTeamColor (hud_basedeathnotice.cpp): a red or blue name in
    /// its team's tone, anything else in stock's white — or the dark local
    /// ink on the line that is the local player's (the LocalPlayerColor
    /// arm).</summary>
    private static Tone NameColour(int team, Tone ink) =>
        team is 2 or 3 && ink == Tone.Text ? TFHudTeams.ToneOf(team) : ink;

    /// <summary>
    /// GetIcon (hud_basedeathnotice.cpp:891-918, :793-807): a line the local
    /// player is in asks the inverted <c>dneg_</c> art first, then the
    /// standard name; a name nothing answers (or no weapon named at all)
    /// falls to the skull, itself inverted-aware. The names are icon-table
    /// entries (scripts/mod_textures.txt through CHud::GetIcon), not
    /// materials. Null for none — the caller draws nothing.
    /// </summary>
    public static UiTextureIcon? IconFor(IUiTextureLibrary textures, in TFKillFeedLine l)
    {
        UiTextureIcon? icon = null;
        if (l.LocalInvolved && l.LocalIcon.Length > 0)
            icon = textures.Icon(l.LocalIcon);
        if (icon is null && l.Icon.Length > 0)
            icon = textures.Icon(l.Icon);
        if (icon is null)
        {
            icon = textures.Icon(l.LocalInvolved ? "dneg_skull_tf" : "d_skull_tf");
            if (icon is null && l.LocalInvolved)
                icon = textures.Icon("d_skull_tf");
        }
        return icon;
    }

    /// <summary>The icon's rect of its atlas in the line's icon box (the
    /// panels' shared idiom, keyed for the facts).</summary>
    private static void Draw(ref UiFrame ui, string key, UiTextureIcon icon, float wide, float tall)
        => ui.Texture(icon.Handle, Size.Du(wide), Size.Du(tall), icon.Uv)
             .AtRes(UiAnchor.BottomLeft, 0f, 0f).Layered().Key(key);
}
