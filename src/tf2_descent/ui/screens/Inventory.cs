using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using TF2.Descent.Client.Ui.Parts;

namespace TF2.Descent.Client.Ui.Screens;

/// <summary>The Australium the character carries (D12). Lane-local: the vendor
/// lane carries it for vendors, and §4 has no shared purse interface yet.</summary>
public interface IDescentInvPurse
{
    long Australium { get; }
}

/// <summary>
/// The inventory (D4, R29): the paper doll and the backpack in one window,
/// 960×640. Left, TF2's silhouette as ItemSlot cells - the weapon column, the
/// hat/body/feet column, the trinket+amulet cluster; unearned slots read as
/// Lock-marked outlines. Right, the backpack as an ItemGrid of 48-du cells in
/// 8 columns, the withdraw-only tail dimmed (2d). The belt strip rides along
/// the bottom like the HUD draws it, Australium sits top-right, and the
/// tooltip is the ItemTooltip part in an overlay anchored near the hovered
/// slot, so it reads over the busy grid. Clicks go to IDescentItemActions.
/// Narrow (a dragged-narrow window): the doll stacks above the backpack and
/// the grid thins to the columns the width allows.
/// </summary>
public sealed class Inventory(
    UiWindows windows,
    IDescentEquipment equip,
    IDescentItemGridSource backpack,
    IDescentItemHover hover,
    IDescentInvPurse purse,
    ItemGrid grid,
    ItemTooltip tooltip,
    BeltStrip belt,
    CoinLabel coin,
    ItemSlot slot,
    IDescentItemActions? actions = null,
    IDescentInspect? inspect = null,
    IDescentInspectActions? inspectActions = null) : UiScreen, IUiWindowed
{
    public override string Name => "inventory";
    public string WindowId => "inventory";
    private const float ColumnDu = ItemSlot.SizeDu + (float)Space.XS;
    private readonly Action<int>? _onCellHover = actions is null ? null : new Action<int>(actions.Hover);
    private readonly Action<int>? _onCellUse = actions is null ? null : new Action<int>(actions.Use);
    private UiBox _anchor;
    private bool _hasAnchor;
    // The inspect modal is the screen's (the overlay is the screen's surface);
    // unwired, no slot wears a button and no modal can show.
    private readonly IDescentInspect? _inspect = inspect;
    private readonly IDescentInspectActions? _inspectActions = inspectActions;
    // The part is built once here (Vendor's _chip/_plate pattern): an
    // unwired screen never news it, and Describe allocates nothing.
    private readonly InspectModal? _inspectModal =
        inspect is null ? null : new InspectModal(inspect, inspectActions);

    /// <summary>The menu window: centred 960×640, the plan's size; 640×480 is
    /// the floor the stack layout still fits; the screen clamps the ceiling.</summary>
    public static UiWindowSpec Spec(float screenW, float screenH)
        => new(MathF.Min(960, screenW), MathF.Min(640, screenH),
               MathF.Min(640, screenW), MathF.Min(480, screenH), screenW, screenH, 720);

    public override void Describe(ref UiFrame ui)
    {
        _hasAnchor = false;
        // Esc closes the modal before the window feels it (the core dialogs'
        // order: UiPageStack pops, the window widget closes the top one).
        bool escInspect = _inspect is { Showing: true } && ui.ConsumeKey((int)UiKey.Escape);
        if (escInspect)
            _inspectActions?.Close();
        using (ui.Window(windows, WindowId, "#Descent_Inv_Title", Spec(ui.DesignWidth, ui.DesignHeight)))
        {
            // Australium top-right (D12): the window's own title row is chrome;
            // this is the first content row, pushed to the right edge. The
            // word takes the Label role — the window already spends its
            // Caption allowance inside the item cells and its Number on the
            // purse, so the four-role budget stays shut.
            using (ui.Row(Space.S, Align.End).Width(Size.Fill))
            {
                ui.Text("#Descent_Inv_Australium", Text.Label, Tone.TextMuted);
                ui.Sub("coin", coin, new CoinLabelProps(purse.Australium, false));
            }

            // The grid thins to the width when the window is dragged narrow;
            // the body then stacks doll over backpack.
            float bodyW = ui.DesignWidth - 2 * UiWindowWidget.Ring - 2 * (float)Space.L;
            int cols = ui.Narrow ? Math.Clamp((int)((bodyW + (float)Space.XS) / ColumnDu), 4, 8) : 8;
            var props = new ItemGridProps(backpack, cols, _onCellHover, _onCellUse, _inspectActions);

            UiBox bagBox;
            if (ui.Narrow)
            {
                DollRow(ref ui);
                BagCaption(ref ui);
                using (ui.Scroll("bagscroll", Space.None).Size(Size.Fill, Size.Fill))
                    bagBox = ui.Sub("bag", grid, props);
            }
            else
            {
                using (ui.Row(Space.L, Align.Start).Size(Size.Fill, Size.Fill))
                {
                    DollRow(ref ui);
                    using (ui.Column(Space.S, Align.Stretch))
                    {
                        BagCaption(ref ui);
                        using (ui.Scroll("bagscroll", Space.None).Width(Size.Fill).Height(Size.Fill))
                            bagBox = ui.Sub("bag", grid, props);
                    }
                }
            }

            // The potion belt along the bottom, centred, the HUD's strip.
            using (ui.Row(Space.None, Align.Centre).Width(Size.Fill))
                ui.Sub("belt", belt);

            // The tooltip last: the overlay draws above the grid it covers,
            // anchored to the hovered doll slot, or to the bag when the grid
            // (whose cells the ItemGrid part owns) is what the pointer is on.
            // The bag box's right edge sits near the window's right, so the
            // bag-anchored panel hangs leftward from it (its top right on the
            // box's bottom right) and stays on screen at every scale; a
            // hovered doll slot is far from the edges and hangs rightward.
            if (hover.Hovering)
            {
                var (at, self) = _hasAnchor
                    ? (UiAnchor.BottomRight, UiAnchor.TopLeft)
                    : (UiAnchor.BottomRight, UiAnchor.TopRight);
                using (ui.Overlay("tip").AnchorTo(_hasAnchor ? _anchor : bagBox, at, self)
                           .Inset(Space.None, Space.XS))
                    ui.Sub("itemtip", tooltip);
            }

            // The inspect modal last and modal: it dims the screen and takes
            // every click (the tooltip's overlay pattern, the Dialog's
            // centring), so the grid under it cannot be picked while the
            // item is being looked at.
            if (_inspect is not null && _inspectModal is { } modal)
                using (ui.Overlay("inspect", modal: true))
                    ui.Sub("inspect", modal);
        }
    }

    /// <summary>TF2's silhouette: weapons | body | trinkets+amulet, three
    /// columns of ItemSlot cells.</summary>
    private void DollRow(ref UiFrame ui)
    {
        using (ui.Row(Space.M, Align.Start))
        {
            using (ui.Column(Space.XS, Align.Start))
            {
                DollCell(ref ui, IndexOf(DescentEquipSlot.Primary));
                DollCell(ref ui, IndexOf(DescentEquipSlot.Secondary));
                DollCell(ref ui, IndexOf(DescentEquipSlot.Melee));
            }
            using (ui.Column(Space.XS, Align.Start))
            {
                DollCell(ref ui, IndexOf(DescentEquipSlot.Hat));
                DollCell(ref ui, IndexOf(DescentEquipSlot.Body));
                DollCell(ref ui, IndexOf(DescentEquipSlot.Feet));
            }
            using (ui.Column(Space.XS, Align.Start))
            {
                DollCell(ref ui, IndexOf(DescentEquipSlot.Trinket1));
                DollCell(ref ui, IndexOf(DescentEquipSlot.Trinket2));
                DollCell(ref ui, IndexOf(DescentEquipSlot.Amulet));
            }
        }
    }

    /// <summary>One doll cell: the ItemSlot part inside a box we can hit-test,
    /// so hover anchors the tooltip right and a click reaches the actions.</summary>
    private void DollCell(ref UiFrame ui, int i)
    {
        if (i < 0)
            return;
        var item = equip.SlotItem(i);
        bool earned = equip.SlotEarned(i);
        var box = ui.Box().Size(Size.Du(ItemSlot.SizeDu), Size.Du(ItemSlot.SizeDu)).Key(i);
        var hit = ui.Interact(box);
        if (hit.Hovered)
        {
            _anchor = box;
            _hasAnchor = true;
            if (item is not null)
                actions?.Hover(i);
        }
        using (box)
            ui.Sub(-1 - i, slot, new ItemSlotProps(item, !earned, false, item is not null, hit.Hovered,
                                                   item is not null && _inspectActions is not null, i,
                                                   _inspectActions));
        if (hit.Clicked && item is not null)
            actions?.Use(i);
    }

    /// <summary>The backpack's caption row: name, count over capacity, and the
    /// withdraw-only flag when the bag has spilled past its roll (2d).</summary>
    private void BagCaption(ref UiFrame ui)
    {
        using (ui.Row(Space.S, Align.Centre).Width(Size.Fill))
        {
            ui.Text("#Descent_Inv_Backpack", Text.Caption, Tone.TextMuted);
            ui.Flex();
            ui.Text($"{backpack.CellCount} / {backpack.Capacity}", Text.Number,
                backpack.Overflow ? Tone.Warning : Tone.TextMuted);
            if (backpack.Overflow)
                ui.Text("#Descent_Inv_Overflow", Text.Caption, Tone.Warning);
        }
    }

    /// <summary>The doll's cell for a silhouette position; the source owns the
    /// order, so find it rather than trusting it (-1 when the class lacks it).</summary>
    private int IndexOf(DescentEquipSlot want)
    {
        for (int i = 0; i < equip.SlotCount; i++)
            if (equip.Slot(i) == want)
                return i;
        return -1;
    }
}
