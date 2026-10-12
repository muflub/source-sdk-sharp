using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using TF2.Descent.Client.Ui.Parts;

namespace TF2.Descent.Client.Ui.Screens;

/// <summary>
/// What the bench is short of, per stack (view-model-built by the wiring; the
/// plan's <see cref="IDescentCraft"/> carries no per-metal deficiency, only
/// <c>CanCraft</c>). Optional: unwired, the bench just says "not enough
/// metal" instead of naming the stacks.
/// </summary>
public interface IDescentCraftShortage
{
    int ScrapShort { get; }
    int ReclaimedShort { get; }
    int RefinedShort { get; }
}

/// <summary>
/// The Engineer's crafting bench (plan §2.10, D24): the recipe list over the
/// five stances the interface names (craft-by-base, reroll, upgrade rarity,
/// combine potions, repair kit) on the left, the item under work in the
/// middle — its affix rows each locked (Lock chip) or re-rollable, with the
/// reroll cost multiplier rising with the rerolls already spent — and on the
/// right the Engineer's three stacks of metal, the output preview
/// ("a random &lt;base&gt; at your level") and <c>Craft</c>.
/// </summary>
public sealed class CraftBench(
    UiWindows windows,
    IDescentCraft craft,
    IDescentCraftActions? actions = null,
    IDescentCraftShortage? shortage = null) : UiScreen, IUiWindowed
{
    public override string Name => "craftbench";

    public string WindowId => "craftbench";

    /// <summary>Below this window width (du) the three sections stack in one scroll.</summary>
    public const float NarrowBelowDu = 1000;

    /// <summary>The recipe list's width, the bench column's width, and one row's height.</summary>
    public const float RecipesDu = 260, BenchDu = 360, RowDu = 44;

    /// <summary>Row keys live here so the two row lists in one window never share an id.</summary>
    private const int RecipeKey = 1_000, AffixKey = 2_000;

    /// <summary>
    /// 1120 x 680 by default (the plan's bench), clamped to the screen; at
    /// least 900 x 460: under 1000 wide the columns give way to the stacked
    /// narrow sections (260 + 360 plus gaps leaves the centre nothing), and
    /// under 460 tall the bench column's stacks, preview and button stop
    /// fitting (seen in the minimum sample). Its own narrow breakpoint.
    /// </summary>
    public static UiWindowSpec Spec(float screenW, float screenH)
        => new(MathF.Min(1120, MathF.Round(screenW * 0.94f)), MathF.Min(680, MathF.Round(screenH * 0.9f)),
               900, 460, screenW, screenH, NarrowBelowDu);

    private readonly RarityChip _chip = new();
    private readonly ItemSlot _well = new();

    public override void Describe(ref UiFrame ui)
    {
        using (ui.Window(windows, WindowId, "#Descent_Craft_Title", Spec(ui.DesignWidth, ui.DesignHeight)))
        {
            if (ui.Narrow)
            {
                // The three sections in one scroll: a squeezed window scrolls
                // its sections instead of cutting them. Fill: a region sized to
                // its content never overflows, so it drew no thumb and the frame
                // clipped CRAFT out of reach (the vendor's 0abf3404).
                using (ui.Scroll("sections", Space.L, ScrollAxes.Vertical).Size(Size.Fill, Size.Fill))
                {
                    RecipeList(ref ui, false);
                    ItemWork(ref ui);
                    Bench(ref ui, false);
                }
            }
            else
            {
                using (ui.Row(Space.L, Align.Stretch).Size(Size.Fill, Size.Fill))
                {
                    RecipeList(ref ui, true);
                    ItemWork(ref ui);
                    Bench(ref ui, true);
                }
            }
        }
    }

    /// <summary>
    /// The recipe list: one clickable plate per stance the interface names, in
    /// the toolkit's tab language (heading, accent rail under the selected
    /// row — <see cref="UiWidgets.Tabs"/> is display-only and horizontal, so
    /// the vertical list is drawn here and answers clicks to
    /// <see cref="IDescentCraftActions.SelectRecipeTab"/>).
    /// </summary>
    private void RecipeList(ref UiFrame ui, bool wide)
    {
        var plate = wide
            ? ui.Box().Size(Size.Du(RecipesDu), Size.Fill).Fill(Tone.Surface).Border(Tone.Line, 1)
                .Round(Radius.M).Key("recipes")
            : ui.Box().Size(Size.Fill, Size.Auto).Fill(Tone.Surface).Border(Tone.Line, 1)
                .Round(Radius.M).Key("recipes");
        using (plate)
            using (ui.Scroll("recipe-list", Space.None))
                using (ui.Column(Space.None, Align.Stretch).Size(Size.Fill, Size.Auto))
                    for (int i = 0; i < craft.RecipeTabCount; i++)
                    {
                        bool selected = craft.RecipeTabIndex == i;
                        var row = ui.Box().Key(RecipeKey + i).Size(Size.Fill, Size.Du(RowDu))
                                    .Fill(selected ? Tone.SurfaceRaised : Tone.None)
                                    .Pad(Space.S, Space.S);
                        var hit = ui.Interact(row);
                        using (row)
                            using (ui.Column(Space.XS, Align.Stretch))
                            {
                                ui.Text(craft.RecipeTabToken(i), Text.Heading, selected ? Tone.Text : Tone.TextMuted).Elide();
                                ui.Rect(selected ? Tone.Accent : Tone.None, Size.Fill, Size.Du(4));
                            }
                        if (hit.Clicked)
                            actions?.SelectRecipeTab(i);
                    }
    }

    /// <summary>
    /// The item under work: its card (name in its rung's colour, chip, ilvl,
    /// wear), what the recipe eats, the reroll line with the cost multiplier
    /// that rises with the rerolls already spent, and its affix rows — each
    /// locked (Lock chip) or re-rollable (Chevron); clicking a row asks the
    /// server to toggle its lock. Combining potions needs no item: the panel
    /// says so, it never goes blank.
    /// </summary>
    private void ItemWork(ref UiFrame ui)
    {
        var panel = ui.Narrow
            ? ui.Box().Size(Size.Fill, Size.Auto).Fill(Tone.Surface).Border(Tone.Line, 1).Round(Radius.M)
                .Pad(Space.M, Space.M).Key("work")
            : ui.Box().Size(Size.Fill, Size.Fill).Fill(Tone.Surface).Border(Tone.Line, 1).Round(Radius.M)
                .Pad(Space.M, Space.M).Key("work");
        using (panel)
            using (ui.Column(Space.M, Align.Stretch).Size(Size.Fill, ui.Narrow ? Size.Auto : Size.Fill))
        {
            if (craft.ItemUnderTest is not { } item)
            {
                ui.EmptyState(UiShape.Shield, "#Descent_Craft_NoItem_Title", "#Descent_Craft_NoItem_Body");
                return;
            }

            var paint = DescentPaints.Of(item.Rarity);
            // Wide: the well beside the name column. Narrow: the well would sit
            // over the column's plate edges (the shared ItemSlot's inner meter
            // lands 2.8 du off them — lint alignment); the chip+name rows carry
            // the card with more room at the minimum size.
            using (ui.Row(Space.S, Align.Centre))
            {
                if (!ui.Narrow)
                    ui.Sub("work-slot", _well, new ItemSlotProps(item, false, false, false, false))
                        .Size(Size.Du(ItemSlot.SizeDu), Size.Du(ItemSlot.SizeDu));
                using (ui.Column(Space.XS, Align.Start).Size(Size.Fill, Size.Auto))
                {
                    using (ui.Row(Space.XS, Align.Centre))
                    {
                        ui.Sub("work-chip", _chip, item.Rarity);
                        ui.Data(item.BaseName, Text.Heading, Tone.Text).Color(paint).Elide();
                    }
                    using (ui.Row(Space.S, Align.Centre))
                    {
                        ui.Text("#Descent_Craft_ItemLevel", Text.Caption, Tone.TextMuted);
                        ui.Number(item.ItemLevel, Text.Number);
                        Durability(ref ui, item);
                        // An Unusual is never craftable (D24): captioned once, not per row.
                        if (item.HasUnusual)
                            ui.Text("#Descent_Craft_NoUnusual", Text.Caption, Tone.Danger);
                    }
                }
            }

            ui.Text(craft.InputsToken, Text.Body, Tone.TextMuted).Elide();

            // The reroll line: the next reroll's cost, the multiplier that rose
            // with the rerolls already spent, and how many affixes the reroll keeps.
            int multiplier = 1 + craft.RerollCount;
            using (ui.Box().Size(Size.Fill, Size.Auto).Fill(Tone.SurfaceRaised).Round(Radius.M)
                     .Pad(Space.S, Space.M).Key("reroll"))
                using (ui.Row(Space.S, Align.Centre))
                {
                    ui.Text("#Descent_Craft_RerollCost", Text.Caption, Tone.TextMuted);
                    ui.Text($"{craft.RerollCost}", Text.Number, Tone.Text);
                    ui.Text($"×{multiplier}", Text.Number, Tone.Warning);
                    ui.Flex();
                    ui.Number(craft.LockedCount, Text.Number);
                    ui.Text($"/{craft.AffixLineCount}", Text.Caption, Tone.TextMuted);
                    ui.Text("#Descent_Craft_Locked", Text.Caption, Tone.TextMuted);
                }

            using (ui.Box().Size(Size.Fill, ui.Narrow ? Size.Auto : Size.Fill).Border(Tone.Line, 1).Round(Radius.S))
                AffixRows(ref ui, multiplier);
        }
    }

    /// <summary>One row per affix line: locked (Lock, muted) or re-rollable (Chevron, the ×N cost).</summary>
    private void AffixRows(ref UiFrame ui, int multiplier)
    {
        using (ui.Scroll("affixes", Space.None))
            using (ui.Column(Space.None, Align.Stretch).Size(Size.Fill, Size.Auto))
                for (int i = 0; i < craft.AffixLineCount; i++)
                {
                    DescentAffixLine line = craft.AffixLine(i);
                    bool locked = craft.RerollAffixLocked(i);
                    var row = ui.Box().Key(AffixKey + i).Size(Size.Fill, Size.Du(RowDu)).Pad(Space.S, Space.S);
                    var hit = ui.Interact(row);
                    using (row)
                        using (ui.Row(Space.S, Align.Centre))
                        {
                            ui.Icon(locked ? UiShape.Lock : UiShape.Chevron,
                                    locked ? Tone.TextMuted : Tone.Accent, 16);
                            ui.Data(line.Name, Text.Body, locked ? Tone.TextMuted : Tone.Text).Width(Size.Fill).Elide();
                            ui.Number(line.Value, Text.Number);
                            if (locked)
                                ui.Text("#Descent_Craft_LockedRow", Text.Caption, Tone.TextMuted);
                            else
                                ui.Text($"×{multiplier}", Text.Caption, Tone.Warning);
                        }
                    if (hit.Clicked)
                        actions?.ToggleLock(i);
                }
    }

    /// <summary>
    /// The right third (or the narrow section): the Engineer's three stacks of
    /// metal, the shortage named when the bench cannot fire, the output
    /// preview, and the <c>Craft</c> button, lit only when the bench can fire.
    /// </summary>
    private void Bench(ref UiFrame ui, bool wide)
    {
        var panel = wide
            ? ui.Box().Size(Size.Du(BenchDu), Size.Fill).Fill(Tone.Surface).Border(Tone.Line, 1)
                .Round(Radius.M).Pad(Space.M, Space.M).Key("bench")
            : ui.Box().Size(Size.Fill, Size.Auto).Fill(Tone.Surface).Border(Tone.Line, 1)
                .Round(Radius.M).Pad(Space.M, Space.M).Key("bench");
        using (panel)
            using (ui.Column(Space.M, Align.Stretch).Size(Size.Fill, wide ? Size.Fill : Size.Auto))
        {
            // Caption, not ui.Label: the window's type-role budget is four.
            ui.Text("#Descent_Craft_Metal", Text.Caption, Tone.TextMuted);
            // The shared MetalPockets draws its three stacks in one ~340-du
            // horizontal row, too wide for this column (the third stack spills
            // the plate); the bench lists them one per line — same dot-number-
            // label language — so each stack reads in full and the shortfall
            // can speak next to its own stack.
            Pocket(ref ui, DescentPaints.Capped, "#Descent_Craft_Scrap", craft.Scrap,
                   craft.CanCraft ? 0 : shortage?.ScrapShort ?? 0);
            Pocket(ref ui, Tone.Warning, "#Descent_Craft_Reclaimed", craft.Reclaimed,
                   craft.CanCraft ? 0 : shortage?.ReclaimedShort ?? 0);
            Pocket(ref ui, Tone.Accent, "#Descent_Craft_Refined", craft.Refined,
                   craft.CanCraft ? 0 : shortage?.RefinedShort ?? 0);
            if (!craft.CanCraft)
                ui.Text("#Descent_Craft_NeedMetal", Text.Caption, Tone.Danger);

            ui.Divider();
            // The output preview, straight from the source: "a random <base> at your level".
            ui.Text(craft.PreviewToken, Text.Body, Tone.TextMuted).Wrap(BenchDu - 48);
            using (ui.Row(Space.S, Align.Centre).Width(Size.Fill))
            {
                ui.Flex();
                if (ui.Button("craft", "#Descent_Craft_Craft", primary: true, enabled: craft.CanCraft).Clicked)
                    actions?.Craft();
            }
        }
    }

    /// <summary>
    /// One stack of metal, in the shared pockets' language (dot, count,
    /// label), and — when the bench cannot fire — how many more this stack
    /// still needs, in Danger.
    /// </summary>
    private void Pocket(ref UiFrame ui, UiPaint dot, string metalToken, int count, int short_)
    {
        using (ui.Box().Size(Size.Fill, Size.Auto).Fill(Tone.Surface).Border(Tone.Line, 1).Round(Radius.M)
                 .Pad(Space.S, Space.S).Key(metalToken))
            using (ui.Row(Space.S, Align.Centre))
            {
                ui.Shape(UiShape.Dot, dot, 12);
                ui.Number(count, Text.Number);
                ui.Text(metalToken, Text.Caption, Tone.TextMuted);
                ui.Flex();
                if (short_ > 0)
                    ui.Text($"+{short_}", Text.Number, Tone.Danger);
            }
    }

    /// <summary>The item's wear, or the "no wear" caption when the base type never wears (the 0 sentinel).</summary>
    private static void Durability(ref UiFrame ui, DescentItemView item)
    {
        if (item.MaxDurability > 0)
        {
            ui.Text("#Descent_Craft_Durability", Text.Caption, Tone.TextMuted);
            ui.Number(item.Durability, Text.Number);
            ui.Text($"/{item.MaxDurability}", Text.Caption, Tone.TextMuted);
        }
        else
            ui.Text("#Descent_Craft_NoDurability", Text.Caption, Tone.TextMuted);
    }
}
