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
/// The TF2 team menu (stock's <c>CTeamMenu</c>, game/client/tf/vgui/
/// tf_teammenu.cpp) at stock's own layout: teammenu.res's canvas through
/// <see cref="StockRes"/> — the three 124x310 doors at ypos 101 (auto
/// c-290, BLU c-29, RED c159, teambutton0-2, :66-146), the 82x57 spectate
/// button at c-140 232 (teambutton3, :148-172), the counts over the team
/// doors (BlueCount c5 53, RedCount c193 53, :333-379) and the
/// <c>#TF_SelectATeam</c> title at 30 r40 — with the loadout gradient's
/// shaded bar at the bottom (0 r50, f0x50, :310-331). The doors themselves
/// are stock's CModelPanels (UI_team01_*.mdl, :280-308, :381-623, deferred
/// with the MenuBG in docs/plans/tf2/deferred.tsv); a team-tone plate with
/// the team's name stands in for each until they are ported, and the auto
/// door is named by its caption (stock's TeamMenuAuto label, :225-251).
///
/// <para>
/// The arms are here: Spectate rides <c>mp_allowspectators</c>
/// (:836-854), Cancel is only visible once a team is chosen (:472-490 —
/// a forced first pick cannot be cancelled), the counts are stock's
/// <c>bluecount</c>/<c>redcount</c> dialog variables
/// (<c>Get_Number_Players</c>, :774-775), and a click goes to
/// <see cref="ITFTeamMenuActions"/>, which sends the named
/// <c>jointeam</c> and closes — the class panel afterwards is the
/// server's to show (tf_player.cpp:6450-6455).
/// </para>
/// </summary>
public sealed class TFTeamMenu : UiScreen
{
    private readonly ITFTeamMenuSource _source;
    private readonly TFTeamCard _card;
    private readonly IUiTextureLibrary _textures;
    private readonly ITFTeamMenuActions? _actions;

    /// <param name="textures">The art library the shaded bar comes from; a
    /// miss leaves the layout without it.</param>
    /// <param name="actions">What a choice does (the named <c>jointeam</c>
    /// send and the closing click); none in a sample.</param>
    public TFTeamMenu(ITFTeamMenuSource source, TFTeamCard card, IUiTextureLibrary textures,
                      ITFTeamMenuActions? actions = null)
    {
        _source = source;
        _card = card;
        _textures = textures;
        _actions = actions;
    }

    public override string Name => "tf2teammenu";

    public override string? Panel => "team";

    /// <summary>The choice last clicked (null until one is): what U3's
    /// command sender reads, as the core menu records <c>Chosen</c>.</summary>
    public TFTeamChoice? Chosen { get; private set; }

    private void Choose(TFTeamChoice choice)
    {
        Chosen = choice;
        _actions?.Choose(choice);
    }

    public override void Describe(ref UiFrame ui)
    {
        // MenuBG (CModelPanel, models/vgui/UI_team01.mdl, :280-308) is a
        // deferred 3D model; the window surface is the backdrop under the
        // layout until the model panels are ported (docs/plans/tf2/
        // deferred.tsv).
        ui.Rect(Tone.Window, Size.Percent(100), Size.Percent(100))
            .AtRes(UiAnchor.BottomLeft, 0, 0).Layered().Key("backdrop");

        // The bar under the title and Cancel (stock's zpos: ShadedBar 5,
        // TeamMenuSelect 6).
        ShadedBar(ref ui);

        if (ui.Narrow)
            DescribeStacked(ref ui);
        else
            DescribeStock(ref ui);
    }

    // ---- the stock canvas (wide screens) ----

    /// <summary>The .res boxes on the 640x480 canvas teammenu.res is
    /// written in (:3-16, 0,0 f0x480), resolved to du per Describe. A "c"
    /// xpos is the element's LEFT edge about the screen's centre, an "r"
    /// ypos its TOP edge above the bottom.</summary>
    internal static class Stock
    {
        public static readonly StockRect Canvas = StockRect.Screen;

        // The doors: 124x310 at ypos 101 (teambutton2 auto c-290 :122-146,
        // teambutton0 BLU c-29 :66-92, teambutton1 RED c159 :94-120).
        public static readonly StockBox Auto = new("c-290", "101", 124, 310);
        public static readonly StockBox Blue = new("c-29", "101", 124, 310);
        public static readonly StockBox Red = new("c159", "101", 124, 310);

        // The spectate button: 82x57 at c-140 232 (teambutton3 :148-172),
        // its label on it (TeamMenuSpectate c-112 255 44x20 :253-278).
        public static readonly StockBox Spectate = new("c-140", "232", 82, 57);

        // The counts over the team doors: 90x30 at ypos 53 (BlueCount c5
        // :333-355, RedCount c193 :357-379); the auto door's caption
        // 102x24 at c-280 55 (TeamMenuAuto :225-251).
        public static readonly StockBox BlueCount = new("c5", "53", 90, 30);
        public static readonly StockBox RedCount = new("c193", "53", 90, 30);
        public static readonly StockBox AutoCaption = new("c-280", "55", 102, 24);

        // TeamMenuSelect: 30 r40 450x30 west (:199-223); CancelButton:
        // r190 r40 150x30 (:174-197); ShadedBar: 0 r50 f0x50 (:310-331).
        public static readonly StockBox Select = new("30", "r40", 450, 30);
        public static readonly StockBox Cancel = new("r190", "r40", 150, 30);
        public static readonly StockBox Shaded = new("0", "r50", 640, 50);
    }

    private void DescribeStock(ref UiFrame ui)
    {
        // The three doors in stock's places; the plate is the model's
        // stand-in (docs/plans/tf2/deferred.tsv).
        Door(ref ui, "auto", Stock.Auto, TFTeamChoice.Auto,
             new TFTeamCardProps(0, "", false));
        Door(ref ui, "blue", Stock.Blue, TFTeamChoice.Blue,
             new TFTeamCardProps(3, "BLU", _source.CurrentTeam == 3));
        Door(ref ui, "red", Stock.Red, TFTeamChoice.Red,
             new TFTeamCardProps(2, "RED", _source.CurrentTeam == 2));

        // The counts over the team doors (stock's dialog variables,
        // :774-775) and the auto door's caption (:225-251).
        Count(ref ui, _source.BluePlayers, Stock.BlueCount, "count-3");
        Count(ref ui, _source.RedPlayers, Stock.RedCount, "count-2");
        var caption = Stock.AutoCaption.In(Stock.Canvas);
        ui.Text("#TF2_TeamMenu_Auto", Text.Label, Tone.Text)
            .Width(Size.Du(caption.Wide)).TextAlign(Align.Centre)
            .AtRes(UiAnchor.Bottom, caption.CentreX, caption.Bottom).Layered().Key("auto caption");

        // Spectate: stock's button and its label read as one box
        // (teambutton3 :148-172, TeamMenuSpectate :253-278); stock adds it
        // only with mp_allowspectators (:836-854).
        if (_source.AllowsSpectators)
        {
            var spot = Stock.Spectate.In(Stock.Canvas);
            var box = ui.Box().Size(Size.Du(spot.Wide), Size.Du(spot.Tall))
                             .Fill(Tone.SurfaceRaised).Round(Radius.S)
                             .AtRes(UiAnchor.Bottom, spot.CentreX, spot.Bottom).Layered().Key("spectate");
            var hit = ui.Interact(box);
            using (box)
                ui.Text("#TF2_TeamMenu_Spectate", Text.Body, Tone.Text)
                    .At(UiAnchor.Centre).TextAlign(Align.Centre).Layered();
            if (hit.Clicked)
                Choose(TFTeamChoice.Spectate);
        }

        // The title (TeamMenuSelect, :199-223).
        var select = Stock.Select.In(Stock.Canvas);
        ui.Text("#TF2_TeamMenu_Title", Text.Title, Tone.Text)
            .Width(Size.Du(select.Wide)).TextAlign(Align.Start)
            .AtRes(UiAnchor.BottomLeft, select.Left, select.Bottom).Layered().Key("select");

        // Stock shows Cancel only with a team already chosen (:472-490).
        if (_source.CurrentTeam > 1)
        {
            var spot = Stock.Cancel.In(Stock.Canvas);
            var box = ui.Box().Size(Size.Du(spot.Wide), Size.Du(spot.Tall))
                             .Fill(Tone.SurfaceRaised).Round(Radius.S)
                             .AtRes(UiAnchor.Bottom, spot.CentreX, spot.Bottom).Layered().Key("cancel");
            var hit = ui.Interact(box);
            using (box)
                ui.Text("#SS_UI_Cancel", Text.Body, Tone.Text)
                    .At(UiAnchor.Centre).TextAlign(Align.Centre).Layered();
            if (hit.Clicked)
                _actions?.Cancel();
        }
    }

    private void Door(ref UiFrame ui, string key, StockBox box, TFTeamChoice choice, in TFTeamCardProps props)
    {
        var s = box.In(Stock.Canvas);
        ui.Sub(key, _card, props)
            .Size(Size.Du(s.Wide), Size.Du(s.Tall))
            .AtRes(UiAnchor.Bottom, s.CentreX, s.Bottom).Layered();
        if (_card.Clicked)
            Choose(choice);
    }

    private static void Count(ref UiFrame ui, int players, StockBox box, string name)
    {
        var s = box.In(Stock.Canvas);
        ui.Number(players, Text.Heading, Tone.Text)
            .Width(Size.Du(s.Wide)).TextAlign(Align.Centre)
            .AtRes(UiAnchor.Bottom, s.CentreX, s.Bottom).Layered().Key(name).Name(name);
    }

    private void ShadedBar(ref UiFrame ui)
    {
        int shade = _textures.Find("vgui/loadout_bottom_gradient");
        if (shade != 0)
            ui.Texture(shade, Size.Percent(100), Size.Du(Stock.Shaded.Rect(Stock.Canvas).Tall * StockRes.Scale))
                .AtRes(UiAnchor.BottomLeft, 0, 0).Layered().Key("shaded bar");
    }

    // ---- the stacked canvas (narrow screens) ----

    private void DescribeStacked(ref UiFrame ui)
    {
        // The 640-unit row cannot fit: the doors stack in stock's order,
        // scaled to what is left under the title bar and the shaded bar.
        var stock = Stock.Red.Rect(Stock.Canvas);
        float doorW = MathF.Min(stock.Wide * StockRes.Scale, ui.DesignWidth - 64f);
        float doorH = MathF.Min(stock.Tall * StockRes.Scale, (ui.DesignHeight - 320f) / 3f);

        using (ui.Column(Space.M, Align.Centre).Size(Size.Percent(100), Size.Percent(100))
                   .Pad(Space.L, Space.L))
        {
            StackDoor(ref ui, "red", new TFTeamCardProps(2, "RED", _source.CurrentTeam == 2),
                      doorW, doorH, _source.RedPlayers, "count-2", TFTeamChoice.Red);
            StackDoor(ref ui, "blue", new TFTeamCardProps(3, "BLU", _source.CurrentTeam == 3),
                      doorW, doorH, _source.BluePlayers, "count-3", TFTeamChoice.Blue);

            // The auto door's caption over it, as the wide canvas's is.
            ui.Text("#TF2_TeamMenu_Auto", Text.Label, Tone.Text);
            StackDoor(ref ui, "auto", new TFTeamCardProps(0, "", false),
                      doorW, doorH, null, null, TFTeamChoice.Auto);

            ui.Flex();

            using (ui.Row(Space.M, Align.Centre).Width(Size.Fill).Height(Size.Du(48f)))
            {
                // Stock adds Spectate only with mp_allowspectators
                // (:836-854) and shows Cancel only with a team chosen
                // (:472-490).
                if (_source.AllowsSpectators && ui.Button("spectate", "#TF2_TeamMenu_Spectate").Clicked)
                    Choose(TFTeamChoice.Spectate);
                if (_source.CurrentTeam > 1 && ui.Button("cancel", "#SS_UI_Cancel").Clicked)
                    _actions?.Cancel();
                ui.Flex();
                ui.Text("#TF2_TeamMenu_Title", Text.Title, Tone.Text);
            }
        }
    }

    private void StackDoor(ref UiFrame ui, string key, TFTeamCardProps props,
                           float doorW, float doorH, int? players, string? countName, TFTeamChoice choice)
    {
        using (ui.Column(Space.XS, Align.Centre))
        {
            if (players is int n)
                ui.Number(n, Text.Heading, Tone.Text).Name(countName!);
            ui.Sub(key, _card, props).Width(Size.Du(doorW)).Height(Size.Du(doorH));
            if (_card.Clicked)
                Choose(choice);
        }
    }
}

/// <summary>What one team door shows: the team's raw number (its colour
/// follows from it), its name, and whether the local player is on it. The
/// auto door is named by its caption, not by a plate of its own.</summary>
public readonly record struct TFTeamCardProps(int Team, string Name, bool Current);

/// <summary>
/// One team door on the TF2 team menu: stock's CTFTeamButton
/// (teammenu.res's teambutton0-2) — a plate in the team's colour standing
/// in for the door model (UI_team01_*.mdl, deferred), the team's name on
/// it, and the mark when the local player already chose it. The whole
/// door is the click target; stock's door opens on hover (the model's
/// <c>hoveropen</c>), and the border's lift is that arm here.
/// </summary>
public sealed class TFTeamCard : UiPanel<TFTeamCardProps>
{
    /// <summary>Whether the last described door was clicked.</summary>
    public bool Clicked { get; private set; }

    public override void Describe(ref UiFrame ui, in TFTeamCardProps p)
    {
        var tone = TFHudTeams.ToneOf(p.Team);
        var door = ui.Box().Size(Size.Fill, Size.Fill)
                      .Key("door").Name(p.Team switch { 2 => "team-card-2", 3 => "team-card-3", _ => "team-card-auto" });
        var hit = ui.Interact(door);
        Clicked = hit.Clicked;
        float lift = ui.Animate(door, hit.Hovered ? 1f : 0f, Motion.Quick);
        door.Fill(tone);
        door.Border(((UiPaint)tone).WithOpacity(0.35f + 0.65f * lift), 2);

        using (door)
        {
            if (p.Team is 2 or 3)
                ui.Data(p.Name, Text.Body, Tone.OnAccent)
                    .At(UiAnchor.Centre).TextAlign(Align.Centre).Layered();
            if (p.Current)
                ui.Text("#SS_UI_TeamMenu_Current", Text.Label, Tone.Text)
                    .Fill(Tone.Surface).At(UiAnchor.Bottom).Inset(Space.None, Space.S)
                    .Width(Size.Fill).TextAlign(Align.Centre).Layered();
        }
    }
}
