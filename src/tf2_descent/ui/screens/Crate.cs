using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using TF2.Descent.Client.Ui.Parts;

namespace TF2.Descent.Client.Ui.Screens;

/// <summary>
/// The crate stall (plan §2.10, D23): the chosen base type as a silhouette
/// card, the published odds table — every rarity rung with its weight out of
/// 100,000 and its percent — the Australium price, red when the hero is
/// short, and <c>Open</c>. No particle fantasy: an open crate lands a reveal
/// row where the rolled item's card sits in its rarity colour.
/// </summary>
public sealed class Crate(UiWindows windows, IDescentCrate crate, IDescentCrateActions? actions = null) : UiScreen, IUiWindowed
{
    public override string Name => "crate";

    public string WindowId => "crate";

    /// <summary>Below this window width (du) the sections stack in one scroll.</summary>
    public const float NarrowBelowDu = 560;

    /// <summary>The card's width, and the odds table's two number columns.</summary>
    public const float CardDu = 216, WeightColDu = 172, ChanceColDu = 92;

    /// <summary>
    /// 720 x 520 by default (the plan's dialog), clamped to the screen. The
    /// floor is 480 x 440: under it the wide body leaves the odds plate, the
    /// price row and the buttons no room (seen in the minimum sample); below
    /// 560 wide the sections stack in one scroll instead of cutting.
    /// </summary>
    public static UiWindowSpec Spec(float screenW, float screenH)
        => new(MathF.Min(720, MathF.Round(screenW * 0.9f)), MathF.Min(520, MathF.Round(screenH * 0.9f)),
               480, 440, screenW, screenH, NarrowBelowDu);

    private readonly ItemSlot _well = new();
    private readonly RarityChip _chip = new();
    private readonly CoinLabel _coin = new();

    public override void Describe(ref UiFrame ui)
    {
        using (ui.Window(windows, WindowId, "#Descent_Crate_Title", Spec(ui.DesignWidth, ui.DesignHeight)))
        {
            // The reveal state: Opening with a rolled item lands the reveal row.
            bool opened = crate.Opening && crate.OpenedItem is not null;
            if (ui.Narrow)
            {
                // Stacked sections in one scroll: a squeezed window scrolls its
                // card, odds and price instead of cutting them. Fill: a region
                // sized to its content never overflows, so it drew no thumb and
                // the frame clipped OPEN out of reach (the vendor's 0abf3404).
                using (ui.Scroll("sections", Space.L, ScrollAxes.Vertical).Size(Size.Fill, Size.Fill))
                {
                    BaseCard(ref ui);
                    OddsTable(ref ui);
                    Footer(ref ui, opened);
                }
            }
            else
            {
                using (ui.Row(Space.L, Align.Start).Size(Size.Fill, Size.Fill))
                {
                    BaseCard(ref ui);
                    OddsTable(ref ui);
                }
                Footer(ref ui, opened);
            }
        }
    }

    /// <summary>
    /// The base-type silhouette card: a recessed plate with the (still empty)
    /// well, the base name and the caption saying the rung stays secret until
    /// the roll. ItemSlot's own size is fixed at 48 du (its documented
    /// one-cell size), so the card is this screen's own plate housing a well
    /// rather than one grown slot.
    /// </summary>
    private void BaseCard(ref UiFrame ui)
    {
        using (ui.Box().Size(ui.Narrow ? Size.Fill : Size.Du(CardDu), Size.Auto)
                 .Fill(Tone.SurfaceRaised).Border(Tone.Line, 1).Round(Radius.L).Pad(Space.M, Space.M).Key("card"))
            using (ui.Column(Space.S, Align.Centre))
            {
                ui.Sub("well", _well, new ItemSlotProps(null, false, false, false, false));
                // The base type is game data, so it keeps its case; no rung colour yet.
                ui.Data(crate.BaseName, Text.Heading, Tone.Text).Elide();
                ui.Text("#Descent_Crate_Base", Text.Caption, Tone.TextMuted);
            }
    }

    /// <summary>
    /// The published odds (D23): the rung's name in its ladder colour (D5),
    /// the weight out of 100,000 and the percent as right-aligned numbers.
    /// The table lives in a scroll, and its rows are Auto, so a short window
    /// scrolls the rungs instead of cutting or squeezing them.
    /// </summary>
    private void OddsTable(ref UiFrame ui)
    {
        var plate = ui.Narrow
            ? ui.Box().Size(Size.Fill, Size.Auto).Fill(Tone.Surface).Border(Tone.Line, 1).Round(Radius.M)
                .Pad(Space.M, Space.M).Key("odds")
            : ui.Box().Size(Size.Fill, Size.Fill).Fill(Tone.Surface).Border(Tone.Line, 1).Round(Radius.M)
                .Pad(Space.M, Space.M).Key("odds");
        using (plate)
            using (ui.Scroll("odds-rows", Space.None))
                using (ui.Grid([Size.Fr(), Size.Du(WeightColDu), Size.Du(ChanceColDu)], Space.S, Space.XS)
                           .Size(Size.Fill, Size.Auto))
                {
                    ui.Text("#Descent_Crate_Odds", Text.Caption, Tone.TextMuted);
                    ui.Text("#Descent_Crate_Weight", Text.Caption, Tone.TextMuted).TextAlign(Align.End);
                    ui.Text("#Descent_Crate_Chance", Text.Caption, Tone.TextMuted).TextAlign(Align.End);
                    for (int i = 0; i < crate.OddsRowCount; i++)
                    {
                        DescentRarity rarity = crate.OddsRarity(i);
                        long weight = crate.OddsWeight(i);
                        var paint = DescentPaints.Of(rarity);
                        using (ui.Row(Space.XS, Align.Centre))
                        {
                            ui.Sub(i, _chip, rarity);
                            // The rung's published name, in the rung's own colour (D5).
                            ui.Text(RarityToken(rarity), Text.Body, Tone.Text).Color(paint).Elide();
                        }
                        Weight(ref ui, weight);
                        Percent(ref ui, weight);
                    }
                }
    }

    /// <summary>The price row (or the reveal row once the crate has opened), then <c>Open</c>.</summary>
    private void Footer(ref UiFrame ui, bool opened)
    {
        if (opened)
        {
            DescentItemView item = crate.OpenedItem!.Value;
            var paint = DescentPaints.Of(item.Rarity);
            // Opaque (Tone.Window, the same paint the window's own chrome uses):
            // the reveal is the one moment the game's world must not bleed through.
            using (ui.Box().Size(Size.Fill, Size.Auto).Fill(Tone.Window).Border(paint, 2).Round(Radius.M)
                     .Pad(Space.M, Space.M).Key("reveal"))
                using (ui.Row(Space.S, Align.Centre))
                {
                    ui.Sub("reveal-slot", _well, new ItemSlotProps(item, false, false, false, false));
                    using (ui.Column(Space.XS, Align.Start).Width(Size.Fill))
                    {
                        using (ui.Row(Space.XS, Align.Centre))
                        {
                            ui.Sub("reveal-chip", _chip, item.Rarity);
                            // The rolled item, revealed in its rung's colour (D5).
                            ui.Data(item.BaseName, Text.Heading, Tone.Text).Color(paint).Elide();
                        }
                        using (ui.Row(Space.S, Align.Centre))
                        {
                            ui.Text("#Descent_Crate_ItemLevel", Text.Caption, Tone.TextMuted);
                            ui.Number(item.ItemLevel, Text.Number);
                            ui.Text("#Descent_Crate_Affixes", Text.Caption, Tone.TextMuted);
                            ui.Number(item.AffixCount, Text.Number);
                            if (item.HasUnusual)
                                ui.Shape(UiShape.Star, paint, 16);
                        }
                    }
                }
        }
        else
        {
            // Opaque (Tone.Window, as the reveal row's plate): the row can sit in
            // the narrow scroll outside the window's body, over the world.
            using (ui.Box().Size(Size.Fill, Size.Auto).Fill(Tone.Window).Round(Radius.M)
                     .Pad(Space.S, Space.S).Key("price"))
            using (ui.Row(Space.S, Align.Centre).Width(Size.Fill))
            {
                ui.Text("#Descent_Crate_Price", Text.Caption, Tone.TextMuted);
                ui.Flex();
                if (!crate.Affordable)
                    ui.Text("#Descent_Crate_Short", Text.Caption, Tone.Danger);
                ui.Sub("coin", _coin, new CoinLabelProps(crate.PriceAustralium, !crate.Affordable));
            }
        }

        using (ui.DialogButtons().Width(Size.Fill))
        {
            ui.Flex();
            if (ui.Button("open", opened ? "#Descent_Crate_Opened" : "#Descent_Crate_Open",
                          primary: true, enabled: crate.Affordable && !opened).Clicked)
                actions?.Open();
        }
    }

    /// <summary>A weight out of 100,000 with its thousands comma: <c>"x,xxx / 100,000"</c>.</summary>
    private static void Weight(ref UiFrame ui, long weight)
    {
        if (weight < 1_000)
            ui.Text($"{weight} / 100,000", Text.Body, Tone.Text).TextAlign(Align.End);
        else
            ui.Text($"{weight / 1_000},{(int)(weight % 1_000):D3} / 100,000", Text.Body, Tone.Text).TextAlign(Align.End);
    }

    /// <summary>
    /// The same weight as a percent of 100,000: to one place for the common
    /// rungs, to two when a tenth of a place still matters (the 0.01 % rung).
    /// </summary>
    private static void Percent(ref UiFrame ui, long weight)
    {
        long whole = weight / 1_000, tenth = weight / 100 % 10, hundredth = weight / 10 % 10;
        if (hundredth != 0)
            ui.Text($"{whole}.{tenth}{hundredth}%", Text.Body, Tone.TextMuted).TextAlign(Align.End);
        else
            ui.Text($"{whole}.{tenth}%", Text.Body, Tone.TextMuted).TextAlign(Align.End);
    }

    /// <summary>The rung's published name; the odds table says which rungs a crate can roll.</summary>
    private static string RarityToken(DescentRarity rarity) => rarity switch
    {
        DescentRarity.Stock => "#Descent_Crate_Rarity_Stock",
        DescentRarity.Vintage => "#Descent_Crate_Rarity_Vintage",
        DescentRarity.Strange => "#Descent_Crate_Rarity_Strange",
        DescentRarity.Unusual => "#Descent_Crate_Rarity_Unusual",
        _ => "#Descent_Crate_Rarity_Australium",
    };
}
