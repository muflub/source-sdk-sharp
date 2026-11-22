using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using TF2.Descent.Client.Ui.Parts;

namespace TF2.Descent.Client.Ui.Screens;

/// <summary>
/// The inventory (D4, R29): the paper doll and the backpack in one window,
/// 960×640. Left, TF2's silhouette as ItemSlot cells - the weapon column, the
/// hat/body/feet column, the trinket+amulet cluster; unearned slots read as
/// Lock-marked outlines. Right, the backpack as an ItemGrid of 48-du cells in
/// 8 columns, the withdraw-only tail dimmed (2d). The belt strip rides along
/// the bottom like the HUD draws it, Australium sits top-right, and the
/// tooltip is the ItemTooltip part in an overlay anchored near the hovered
/// slot, so it reads over the busy grid. Clicks go to IDescentItemActions:
/// a backpack cell by its index, a doll slot as <c>-1 - slot</c>.
/// The doll's cells are the belt's width (112 du), so an item's name reads
/// whole over two lines. A row under the bag's caption picks what a click on
/// a bag cell does: equip it (the default), move it (a click picks the item
/// up, framed in the accent; a click on another cell puts it there, swapping
/// with what is in it; a belt slot takes a potion in hand, or is picked up
/// for a bag cell to take it back), or drop it at the hero's feet. The toolkit has no
/// drag and no second button, so the mode is the context. The window's
/// status line (the last notice: a refusal's reason) sits along the bottom.
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
    IDescentInspectActions? inspectActions = null,
    IDescentItemDrop? drop = null,
    StatusRow? status = null) : UiScreen, IUiWindowed
{
    /// <summary>A doll cell's width: the belt's slot, room for a name on two lines.</summary>
    public const float DollCellDu = BeltStrip.SlotDu;

    /// <summary>What a click on a bag cell does.</summary>
    public enum CellMode : byte { Equip, Move, Drop }

    /// <summary>The bag's click mode (the row under its caption).</summary>
    public CellMode Mode { get; private set; }

    /// <summary>The bag cell picked up for a move; -1 for none.</summary>
    public int Picked { get; private set; } = -1;

    /// <summary>The belt slot picked up for a move into the bag; -1 for none.</summary>
    public int PickedBelt { get; private set; } = -1;

    private Action<int>? _onCell;
    private Action<int>? _onBelt;

    public override string Name => "inventory";
    /// <summary>The viewport panel the server opens this screen with (VGUIMenu): <c>descent_&lt;screen&gt;</c> (plan_descent_logic UI1).</summary>
    public override string? Panel => "descent_inventory";
    public string WindowId => "inventory";
    private const float ColumnDu = ItemSlot.SizeDu + (float)Space.XS;
    private const float DollColumnDu = DollCellDu + (float)Space.M;
    private readonly Action<int>? _onCellHover = actions is null ? null : new Action<int>(actions.Hover);
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
            // Beside the wide doll the bag takes the columns the rest of the width holds, up to 8.
            float bagW = ui.Narrow ? bodyW : bodyW - (3 * DollColumnDu - (float)Space.M) - (float)Space.L;
            int cols = Math.Clamp((int)((bagW + (float)Space.XS) / ColumnDu), 4, 8);
            var props = new ItemGridProps(backpack, cols, _onCellHover, actions is null ? null : CellAction(), _inspectActions,
                                          Picked, UseEmpty: Mode == CellMode.Move && (Picked >= 0 || PickedBelt >= 0));

            UiBox bagBox;
            if (ui.Narrow)
            {
                DollRow(ref ui);
                BagCaption(ref ui);
                ModeRow(ref ui);
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
                        ModeRow(ref ui);
                        using (ui.Scroll("bagscroll", Space.None).Width(Size.Fill).Height(Size.Fill))
                            bagBox = ui.Sub("bag", grid, props);
                    }
                }
            }

            // The potion belt along the bottom, centred, the HUD's strip; in the move mode its
            // slots take a potion from the bag or give one back (the whole run's F20).
            using (ui.Row(Space.None, Align.Centre).Width(Size.Fill))
                ui.Sub("belt", belt, Mode == CellMode.Move && actions is not null
                    ? new BeltStripProps(BeltAction(), PickedBelt)
                    : default(BeltStripProps));
            if (status is not null)
                ui.Sub("status", status);

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
            // Only while an item is under inspection: a modal overlay dims the
            // window and takes every click even when the part inside draws
            // nothing, and live the inventory could not be used at all (UI3).
            if (_inspect is { Showing: true } && _inspectModal is { } modal)
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
        var box = ui.Box().Size(Size.Du(DollCellDu), Size.Du(ItemSlot.SizeDu)).Key(i);
        var hit = ui.Interact(box);
        if (hit.Hovered)
        {
            _anchor = box;
            _hasAnchor = true;
            // A doll slot is keyed -1 - slot, a backpack cell by its index: one
            // actions sink takes both (plan_descent_logic UI3).
            if (item is not null)
                actions?.Hover(-1 - i);
        }
        using (box)
            ui.Sub(-1 - i, slot, new ItemSlotProps(item, !earned, false, item is not null, hit.Hovered,
                                                   item is not null && _inspectActions is not null, i,
                                                   _inspectActions, DollCellDu));
        if (hit.Clicked && item is not null)
            actions?.Use(-1 - i);
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

    /// <summary>
    /// What a bag cell's click does, by the mode: equip, pick up then put down
    /// (a second click on the picked cell puts it back), or drop.
    /// </summary>
    public void OnCell(int cell)
    {
        switch (Mode)
        {
            case CellMode.Move when PickedBelt >= 0:
                actions?.OffBelt(PickedBelt, cell);
                PickedBelt = -1;
                break;
            case CellMode.Move when Picked < 0:
                if (backpack.Cell(cell) is not null)
                    Picked = cell;
                break;
            case CellMode.Move:
                if (cell != Picked)
                    actions?.Move(Picked, cell);
                Picked = -1;
                break;
            case CellMode.Drop:
                if (backpack.Cell(cell) is not null)
                    drop?.Drop(cell);
                break;
            default:
                actions?.Use(cell);
                break;
        }
    }

    /// <summary>
    /// What a belt slot's click does in the move mode: a bag item in hand goes onto that slot; with
    /// none, the slot is picked up (a second click on it puts it back) for a bag cell to take.
    /// </summary>
    public void OnBeltSlot(int slot)
    {
        if (Mode != CellMode.Move)
            return;
        if (Picked >= 0)
        {
            actions?.ToBelt(Picked, slot);
            Picked = -1;
            return;
        }
        PickedBelt = PickedBelt == slot ? -1 : slot;
    }

    /// <summary>A closed window opens again equipping: a drop mode left on would drop the next click (seen live on a reopen).</summary>
    public override void Closed() => SetMode(CellMode.Equip);

    /// <summary>The bag's click mode changes; a pick in hand is put back.</summary>
    public void SetMode(CellMode mode)
    {
        Mode = mode;
        Picked = -1;
        PickedBelt = -1;
    }

    /// <summary>Not a describe method: the cell action is made once, on first need.</summary>
    private Action<int> CellAction() => _onCell ??= OnCell;

    /// <summary>Not a describe method: the belt action is made once, on first need.</summary>
    private Action<int> BeltAction() => _onBelt ??= OnBeltSlot;

    /// <summary>A mode chip's width: three and their gaps fit the bag's narrowest (four-column) width.</summary>
    private const float ChipDu = 64;

    /// <summary>The bag's click modes as a row of chips (the selected one in the accent); what a click does now under them.</summary>
    private void ModeRow(ref UiFrame ui)
    {
        if (actions is null)
            return;
        using (ui.Row(Space.S, Align.Centre).Width(Size.Fill))
        {
            ModeChip(ref ui, CellMode.Equip, "#Descent_Inv_Mode_Equip");
            ModeChip(ref ui, CellMode.Move, "#Descent_Inv_Mode_Move");
            if (drop is not null)
                ModeChip(ref ui, CellMode.Drop, "#Descent_Inv_Mode_Drop");
        }
        if (Mode == CellMode.Move)
            ui.Text(Picked >= 0 || PickedBelt >= 0 ? "#Descent_Inv_Move_Put" : "#Descent_Inv_Move_Pick", Text.Caption, Tone.TextMuted).Elide().Width(Size.Fill);
        else if (Mode == CellMode.Drop)
            ui.Text("#Descent_Inv_Drop_Hint", Text.Caption, Tone.Warning).Elide().Width(Size.Fill);
    }

    /// <summary>One mode chip: a 64 x 32 click target, accent-filled while its mode is on.</summary>
    private void ModeChip(ref UiFrame ui, CellMode mode, string token)
    {
        bool on = Mode == mode;
        var chip = ui.Box().Key(600 + (int)mode).Size(Size.Du(ChipDu), Size.Du(32)).Round(Radius.M)
                     .Fill(on ? Tone.Accent : Tone.SurfaceRaised).Border(on ? Tone.Accent : Tone.Line, 1);
        var hit = ui.Interact(chip);
        using (chip)
            ui.Text(token, Text.Caption, on ? Tone.OnAccent : Tone.Text).At(UiAnchor.Centre).Elide();
        if (hit.Clicked)
            SetMode(mode);
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
