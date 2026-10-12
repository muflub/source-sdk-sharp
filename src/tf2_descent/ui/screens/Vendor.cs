using System;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using TF2.Descent.Client.Ui.Parts;

namespace TF2.Descent.Client.Ui.Screens;

/// <summary>
/// The vendor panel (plan §2.8, D15): ONE screen wearing four skins. The shop
/// stances (weapons, armour, healing, magic, gambling) are a wares list on the
/// left and the player's grid on the right, with the Buy / Sell / Buy-back tabs
/// and the purse in the footer; the service stances (identify, repair) flip it:
/// the grid on the left, the picked item, the fee plates and the action on the
/// right. Which skin it wears comes from <see cref="IDescentVendor.Kind"/> —
/// the code never picks a vendor by name. The wares list is a
/// <see cref="UiFrame.VirtualList"/> over <see cref="DescentWare"/> rows
/// (rarity chip, rarity-coloured name, ilvl, right-aligned price with the
/// Australium mark, Danger when unaffordable); the grid is a second
/// <see cref="ItemGrid"/> instance, not a fork of it.
/// </summary>
public sealed class Vendor : UiScreen, IUiWindowed
{
    private readonly UiWindows _windows;
    private readonly IDescentVendor _vendor;
    private readonly IDescentVendorActions _actions;
    private readonly IDescentItemGridSource _backpack;
    private readonly IDescentVendorHover? _wareHover;
    private readonly IDescentItemHover? _itemHover;
    private readonly HintTip? _hint;
    private readonly ItemTooltip? _itemTip;
    // The player's grid offers the inspect button (the wares rows are not
    // slots, they are a VirtualList of DescentWare rows: no cell to button);
    // unwired, the grid draws none.
    private readonly IDescentInspect? _inspect;
    private readonly IDescentInspectActions? _inspectActions;
    private readonly InspectModal? _inspectModal;
    // The 3D portrait (D15): an engine surface on the window's right, behind
    // the right column's plate. Uninjected or inactive: nothing draws, so
    // every existing sample renders unchanged.
    private readonly IDescentVendorPortrait? _portrait;
    private readonly CoinLabel _purse;
    private readonly ItemGrid _grid;

    private int _tab;                     // 0 Buy, 1 Sell, 2 Buy-back
    private int _ware = -1;               // selected stock row (Buy tab)
    private int _sold = -1;               // selected sold row (Buy-back tab)
    private int _sellCell = -1;           // selected backpack cell (Sell tab)
    private int _svcCell = -1;            // selected backpack cell (service stances)
    private long _feeShown = -1;          // cached fee strings: Describe allocates nothing
    private long _feeAllShown = -1;
    private string _feeText = "";
    private string _feeAllText = "";

    // The two popups' anchors, captured during the content draw: the hovered
    // ware row's grid box, and the player grid's box (the ItemGrid part owns
    // its cells, like the Inventory anchors to the bag box).
    private UiBox _wareAnchor;
    private bool _hasWareAnchor;
    private UiBox _gridAnchor;

    private readonly WareRow _wareRow;
    private readonly WareRow _soldRow;
    private readonly VendorTabRow _tabs;
    private readonly RarityChip _chip = new();
    private readonly NumberPlate _plate = new();
    private readonly Action<int> _pickSell;
    private readonly Action<int> _pickService;
    private readonly Action<int> _hoverCell;

    public Vendor(UiWindows windows, IDescentVendor vendor, IDescentVendorActions actions,
                  IDescentItemGridSource backpack, CoinLabel purse, ItemGrid grid,
                  IDescentVendorHover? wareHover = null, IDescentItemHover? itemHover = null,
                  HintTip? hint = null, ItemTooltip? itemTip = null,
                  IDescentInspect? inspect = null, IDescentInspectActions? inspectActions = null,
                  IDescentVendorPortrait? portrait = null)
    {
        _windows = windows;
        _vendor = vendor;
        _actions = actions;
        _backpack = backpack;
        _wareHover = wareHover;
        _itemHover = itemHover;
        _hint = hint;
        _itemTip = itemTip;
        _purse = purse;
        _grid = grid;
        _wareRow = new WareRow(this, false);
        _soldRow = new WareRow(this, true);
        _tabs = new VendorTabRow(this);
        _inspect = inspect;
        _inspectActions = inspectActions;
        _portrait = portrait;
        _inspectModal = inspect is null ? null : new InspectModal(inspect, inspectActions);
        _pickSell = PickSell;
        _pickService = PickService;
        // The pointer on the player's grid reports the cell (the identify
        // bench's and crate stall's items); the host answers with the
        // tooltip's item through IDescentItemHover.
        _hoverCell = new Action<int>(actions.HoverBackpack);
    }

    public override string Name => "vendor";

    public string WindowId => "vendor";

    /// <summary>
    /// 1040 x 680 by default, at least 760 x 520 du: below that the two columns
    /// (the 412 du grid plus the wares list's floor) and the footer stop fitting
    /// side by side anyway; narrow below 900 du stacks them.
    /// </summary>
    public static UiWindowSpec Spec(float screenW, float screenH)
        => new(MathF.Min(1040, screenW), MathF.Min(680, screenH), 760, 520, screenW, screenH, 900);

    /// <summary>A wares row's height, du.</summary>
    public const float RowDu = 44;

    /// <summary>The portrait's width: the service column's plate width.</summary>
    public const float PortraitDu = 320;

    private void PickSell(int cell) => _sellCell = cell;
    private void PickService(int cell) => _svcCell = cell;

    /// <summary>The first cell the service could act on: a broken/worn piece for the
    /// Repairman, an unidentified one for the Spy; -1 when the pack holds neither.</summary>
    private int FirstServiceable(bool repair)
    {
        for (int i = 0; i < _backpack.CellCount; i++)
        {
            if (_backpack.Cell(i) is not { } it)
                continue;
            bool worth = repair
                ? it.Broken || (it.MaxDurability > 0 && it.Durability < it.MaxDurability)
                : !it.Identified;
            if (worth)
                return i;
        }
        return -1;
    }

    public override void Describe(ref UiFrame ui)
    {
        _hasWareAnchor = false;
        // Esc closes the inspect modal before the window feels it (the core
        // dialogs' order): consumed here, the window widget's own Esc never
        // sees the press while a modal is up.
        if (_inspect is { Showing: true } && ui.ConsumeKey((int)UiKey.Escape))
            _inspectActions?.Close();
        using (ui.Window(_windows, WindowId, _vendor.VendorNameToken, Spec(ui.DesignWidth, ui.DesignHeight)))
        {
            DescentVendorKind kind = _vendor.Kind;
            bool service = kind is DescentVendorKind.Identify or DescentVendorKind.Repair;
            bool gamble = kind == DescentVendorKind.Gambling;
            bool narrow = ui.Narrow;

            // The header: the vendor's one-line stance (the plan's caption line).
            using (ui.Row(Space.S, Align.Centre).Width(Size.Fill))
            {
                ui.Rect(Tone.Accent, Size.Du(4), Size.Du(20)).Round(Radius.S);
                ui.Text(_vendor.StanceToken, Text.Body, Tone.TextMuted).Width(Size.Fill).Elide();
            }

            // The portrait first (drawn under the plates that follow: the
            // column's translucent surfaces let it through), on the body's
            // right edge, its width the service column's (the plate it sits
            // behind). The content column above it fills the same box, so
            // the layout is exactly the plain window's.
            if (_portrait is { Active: true } port)
            {
                using (ui.Box().Size(Size.Fill, Size.Fill))
                {
                    ui.Texture(port.Texture, Size.Du(PortraitDu), Size.Fill)
                      .At(UiAnchor.Right).Layered().Name("portrait");
                    using (ui.Column(Space.None, Align.Stretch).Size(Size.Fill, Size.Fill).Layered())
                    {
                        if (service) ServiceBody(ref ui, narrow);
                        else if (gamble) GambleBody(ref ui);
                        else ShopBody(ref ui, narrow);
                    }
                }
            }
            else if (service) ServiceBody(ref ui, narrow);
            else if (gamble) GambleBody(ref ui);
            else ShopBody(ref ui, narrow);

            // The footer: the shop's tabs (left), the purse, the posture's action.
            if (service)
            {
                using (ui.Row(Space.L, Align.Centre).Width(Size.Fill))
                    ui.Sub("purse", _purse, new CoinLabelProps(_vendor.PlayerAustralium, false));
            }
            else if (gamble)
            {
                using (ui.Row(Space.L, Align.Centre).Width(Size.Fill))
                {
                    ui.Sub("purse", _purse, new CoinLabelProps(_vendor.PlayerAustralium, false));
                    ui.Flex();
                    if (ui.Button("act-buy", "#Descent_Vendor_Btn_Buy", true, null, GambleBuyEnabled()).Clicked)
                        _actions.Buy(0);
                }
            }
            else
            {
                using (ui.Row(Space.L, Align.Centre).Width(Size.Fill))
                {
                    if (_vendor.SellTab || _vendor.SellCount > 0)
                    {
                        ClampTab();
                        ui.Sub("tabs", _tabs, _tab);
                    }
                    ui.Flex();
                    ui.Sub("purse", _purse, new CoinLabelProps(_vendor.PlayerAustralium, false));
                    if (ui.Button("act", TabButtonToken(), true, null, ActionEnabled(out int argIndex)).Clicked)
                    {
                        if (_tab == 1) _actions.Sell(argIndex);
                        else _actions.Buy(argIndex);
                    }
                }
            }

            // The popups last (the Inventory's tooltip pattern): a hovered
            // ware row wears the HintTip; a hovered player item (the identify
            // bench's items, the crate stall's stack) wears the ItemTooltip —
            // an item is an item here, with its affixes and compare. Content
            // is the host's view-model either way.
            if (_wareHover is { Active: true } && _hint is not null)
            {
                var (at, self) = _hasWareAnchor
                    ? (UiAnchor.BottomRight, UiAnchor.TopLeft)
                    : (UiAnchor.BottomRight, UiAnchor.TopRight);
                using (ui.Overlay("hint").AnchorTo(_hasWareAnchor ? _wareAnchor : _gridAnchor, at, self)
                           .Inset(Space.None, Space.XS))
                    ui.Sub("hint", _hint);
            }
            if (_itemHover is { Hovering: true } && _itemTip is not null)
            {
                using (ui.Overlay("itemtip").AnchorTo(_gridAnchor, UiAnchor.BottomRight, UiAnchor.TopRight)
                           .Inset(Space.None, Space.XS))
                    ui.Sub("itemtip", _itemTip);
            }

            // The inspect modal last and modal (the Inventory's pattern): the
            // player's own items are items here too; the wares list is rows,
            // not slots, so it wears none.
            if (_inspect is not null && _inspectModal is { } modal)
                using (ui.Overlay("inspect", modal: true))
                    ui.Sub("inspect", modal);
        }
    }

    // ---- stances ---------------------------------------------------------------

    /// <summary>The shop: wares left, the player's grid right (stacked when narrow).</summary>
    private void ShopBody(ref UiFrame ui, bool narrow)
    {
        if (narrow)
        {
            using (ui.Column(Space.M, Align.Stretch).Size(Size.Fill, Size.Fill))
            {
                WarePane(ref ui);
                GridPane(ref ui, narrow);
            }
        }
        else
        {
            using (ui.Row(Space.L, Align.Stretch).Size(Size.Fill, Size.Fill))
            {
                WarePane(ref ui);
                GridPane(ref ui, narrow);
            }
        }
    }

    /// <summary>The service: the grid left, the picked item and the fee right.</summary>
    private void ServiceBody(ref UiFrame ui, bool narrow)
    {
        if (narrow)
        {
            // The grid keeps its natural height and the fee/action column
            // takes the rest, clipped: at the window's minimum (the plan's
            // tightest place) the picked item, the fee plates, the two
            // primaries and the purse row together are taller than the body
            // column, and unclipped they paint over the footer's purse - the
            // repair stance at 640x420 put REPAIR ALL on top of the
            // Australium count. A scroll region is the toolkit's answer (the
            // scoreboard's teams): the column stops above the footer, and
            // nothing becomes unreachable - the last primary scrolls in.
            using (ui.Column(Space.M, Align.Stretch).Size(Size.Fill, Size.Fill))
            {
                GridPane(ref ui, narrow);
                using (ui.Scroll("svc-pane", Space.M).Size(Size.Fill, Size.Fill))
                    ServicePane(ref ui);
            }
            return;
        }
        using (ui.Row(Space.L, Align.Start).Size(Size.Fill, Size.Fill))
        {
            GridPane(ref ui, narrow);
            using (ui.Column(Space.S, Align.Start).Size(Size.Du(320), Size.Fill))
                ServicePane(ref ui);
        }
    }

    /// <summary>The crate stall: one price row and the Scout's promise; no tabs, no grid.</summary>
    private void GambleBody(ref UiFrame ui)
    {
        using (ui.Column(Space.M, Align.Stretch).Size(Size.Fill, Size.Fill))
        {
            if (_vendor.BuyCount == 0)
            {
                using (ui.Box().Size(Size.Fill, Size.Fill))
                    ui.EmptyState(UiShape.Dot, "#Descent_Vendor_Empty_Wares_Title", "#Descent_Vendor_Empty_Wares_Body")
                      .At(UiAnchor.Centre);
            }
            else
            {
                using (ui.Box().Width(Size.Fill).Height(Size.Du(RowDu)))
                    ui.Sub("gamble-ware", _wareRow, 0);
                ui.Text("#Descent_Vendor_Note_Odds", Text.Body, Tone.TextMuted);
            }
        }
    }

    // ---- panes -----------------------------------------------------------------

    /// <summary>Wares: the column header and the list (or its empty state). Fill in both axes.</summary>
    private void WarePane(ref UiFrame ui)
    {
        // Buy-back shows the sold list; every other shop tab the stock.
        bool back = _tab == 2;
        int count = back ? _vendor.SellCount : _vendor.BuyCount;
        using (ui.Column(Space.None, Align.Stretch).Size(Size.Fill, Size.Fill))
        {
            Span<UiSize> header = stackalloc UiSize[4];
            WareColumns(header);
            using (ui.Grid(header, Space.S).Width(Size.Fill).Height(Size.Du(32))
                       .Fill(Tone.SurfaceRaised).Round(Radius.S).Pad(Space.S, Space.M))
            {
                ui.Spacer(Size.Du(1), Size.Du(1));                 // the chip column
                ui.Text("#Descent_Vendor_Col_Wares", Text.Body, Tone.TextMuted).Elide();
                ui.Text("#Descent_Vendor_Col_Level", Text.Body, Tone.TextMuted).Elide().TextAlign(Align.End);
                ui.Text("#Descent_Vendor_Col_Price", Text.Body, Tone.TextMuted).Elide().TextAlign(Align.End);
            }
            if (count == 0)
            {
                using (ui.Box().Size(Size.Fill, Size.Fill))
                    ui.EmptyState(UiShape.Dot, "#Descent_Vendor_Empty_Wares_Title", "#Descent_Vendor_Empty_Wares_Body")
                      .At(UiAnchor.Centre);
            }
            else
            {
                ui.VirtualList(back ? "sold" : "wares", count, RowDu, back ? _soldRow : _wareRow)
                  .Size(Size.Fill, Size.Fill);
            }
        }
    }

    /// <summary>The player's grid with its caption; the Sell and service postures pick with it.</summary>
    private void GridPane(ref UiFrame ui, bool narrow)
    {
        using (ui.Column(Space.S, Align.Start))
        {
            using (ui.Row(Space.S, Align.Centre))
            {
                ui.Text("#Descent_Vendor_Caption_YourItems", Text.Body, Tone.TextMuted);
                ui.Text($"{_backpack.CellCount} / {_backpack.Capacity}", Text.Body, Tone.TextMuted);
            }
            bool service = _vendor.Kind is DescentVendorKind.Identify or DescentVendorKind.Repair;
            // The grid box is the item tooltip's anchor (the part owns its
            // cells; the Inventory hangs its bag tooltip the same way). The
            // cells report the pointer; the host answers with the item.
            _gridAnchor = ui.Sub(service ? "svc-grid" : "sell-grid", _grid,
                                 new ItemGridProps(_backpack, narrow ? 6 : 8, _hoverCell,
                                                   service ? _pickService : _pickSell, _inspectActions));
        }
    }

    /// <summary>The service's column: the picked item, the fee plates, the action.</summary>
    private void ServicePane(ref UiFrame ui)
    {
        bool repair = _vendor.Kind == DescentVendorKind.Repair;
        // The service always prices something: the first item it could act on
        // is picked when nothing (or an empty well) holds the selection.
        if (_svcCell < 0 || _backpack.Cell(_svcCell) is null)
            _svcCell = FirstServiceable(repair);
        var picked = _svcCell >= 0 ? _backpack.Cell(_svcCell) : null;
        if (picked is { } it)
        {
            using (ui.Row(Space.S, Align.Centre).Width(Size.Fill))
            {
                ui.Sub("pick-chip", _chip, it.Rarity);
                ui.Data(it.BaseName, Text.Body).Color(DescentPaints.Of(it.Rarity)).Width(Size.Fill).Elide();
            }
        }
        else
        {
            ui.Text("#Descent_Vendor_Hint_Pick", Text.Body, Tone.TextMuted).Wrap(280);
        }
        ui.Divider();

        long purse = _vendor.PlayerAustralium;
        if (repair)
        {
            FeeRow(ref ui, "fee-one", "#Descent_Field_RepairFee", FeeText(_vendor.ServiceFee, 0),
                   _vendor.ServiceFee > purse ? (UiPaint)Tone.Danger : (UiPaint)Tone.Text);
            FeeRow(ref ui, "fee-all", "#Descent_Field_RepairAll", FeeText(_vendor.ServiceFeeAll, 1),
                   _vendor.ServiceFeeAll > purse ? (UiPaint)Tone.Danger : (UiPaint)Tone.Text);
            bool brokenPicked = picked is { } b
                && (b.Broken || (b.MaxDurability > 0 && b.Durability < b.MaxDurability));
            if (ui.Button("svc-repair", "#Descent_Field_RepairFee", true, null,
                          brokenPicked && _vendor.ServiceFee > 0 && _vendor.ServiceFee <= purse).Clicked)
                _actions.ServiceOne(_svcCell);
            if (ui.Button("svc-all", "#Descent_Field_RepairAll", false, null,
                          _vendor.ServiceFeeAll > 0 && _vendor.ServiceFeeAll <= purse).Clicked)
                _actions.ServiceAll();
        }
        else
        {
            FeeRow(ref ui, "fee-one", "#Descent_Vendor_Btn_Identify", FeeText(_vendor.ServiceFee, 0),
                   _vendor.ServiceFee > purse ? (UiPaint)Tone.Danger : (UiPaint)Tone.Text);
            bool identify = picked is { Identified: false } && _vendor.ServiceFee > 0 && _vendor.ServiceFee <= purse;
            if (ui.Button("svc-identify", "#Descent_Vendor_Btn_Identify", true, null, identify).Clicked)
                _actions.ServiceOne(_svcCell);
        }
    }

    /// <summary>
    /// One fee readout: the label gives way to an elide, the value sits in a
    /// <see cref="NumberPlate"/>. (The shared <c>NumberField</c> pins its label
    /// to 160 du without an elide, which clips an unlocalised token name in the
    /// narrow test's language — reported for the lead.)
    /// </summary>
    private void FeeRow(ref UiFrame ui, string key, string labelToken, string value, UiPaint paint)
    {
        using (ui.Row(Space.S, Align.Centre))
        {
            ui.Text(labelToken, Text.Body, Tone.TextMuted).Width(Size.Fill).Elide();
            ui.Sub(key, _plate, new NumberPlateProps(value, paint, Text.Number, 96));
        }
    }

    // ---- selection + enable rules ----------------------------------------------

    /// <summary>A tab the vendor cannot offer this frame falls back to Buy.</summary>
    private void ClampTab()
    {
        if ((_tab == 1 && !_vendor.SellTab) || (_tab == 2 && _vendor.SellCount == 0))
            _tab = 0;
    }

    private string TabButtonToken() => _tab switch
    {
        1 => "#Descent_Vendor_Btn_Sell",
        2 => "#Descent_Vendor_Btn_BuyBack",
        _ => "#Descent_Vendor_Btn_Buy",
    };

    /// <summary>The footer button's enable, and the list index its action takes.</summary>
    private bool ActionEnabled(out int argIndex)
    {
        long purse = _vendor.PlayerAustralium;
        if (_tab == 0)
        {
            argIndex = _ware;
            return _ware >= 0 && _ware < _vendor.BuyCount && _vendor.Buy(_ware).Affordable && purse > 0;
        }
        if (_tab == 1)
        {
            argIndex = _sellCell;
            return _sellCell >= 0 && _backpack.Cell(_sellCell) is not null;
        }
        argIndex = _sold;
        return _sold >= 0 && _sold < _vendor.SellCount && _vendor.Sell(_sold).Affordable && purse > 0;
    }

    private bool GambleBuyEnabled()
        => _vendor.BuyCount > 0 && _vendor.Buy(0).Affordable && _vendor.PlayerAustralium > 0;

    /// <summary>The fee plate's value: cached so Describe allocates nothing.</summary>
    private string FeeText(long fee, int slot)
    {
        if (slot == 0 ? _feeShown != fee : _feeAllShown != fee)
        {
            string text = fee <= 0 ? "-" : fee.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (slot == 0)
            {
                _feeShown = fee;
                _feeText = text;
            }
            else
            {
                _feeAllShown = fee;
                _feeAllText = text;
            }
        }
        return slot == 0 ? _feeText : _feeAllText;
    }

    /// <summary>Ware rows and the header share this column template (chip, name, ilvl, price).</summary>
    private static void WareColumns(Span<UiSize> c)
    {
        c[0] = Size.Du(18);
        c[1] = Size.Fr();
        c[2] = Size.Du(116);           // fits the widest thing the cell shows: "Unidentified"
        c[3] = Size.Du(92);
    }

    /// <summary>Clamped price for the plate: Australium totals past int are unheard of, but the rows never lie.</summary>
    private static int PriceDigits(long price) => price > int.MaxValue ? int.MaxValue : (int)price;

    /// <summary>
    /// One wares row: the rarity chip, the name in its rarity colour, the ilvl
    /// (unidentified stock says the D22 caption instead), the price right-aligned
    /// with the Australium mark, Danger when unaffordable. Clicking selects it
    /// for the footer button. Props are the row index; the two list keys (stock,
    /// sold) pick which list <c>sold</c> says.
    /// </summary>
    private sealed class WareRow(Vendor owner, bool sold) : UiPanel<int>
    {
        private readonly RarityChip _chip = new();

        public override void Describe(ref UiFrame ui, in int i)
        {
            var ware = sold ? owner._vendor.Sell(i) : owner._vendor.Buy(i);
            var item = ware.Item;
            int selected = sold ? owner._sold : owner._ware;
            var paint = DescentPaints.Of(item.Rarity);
            var fill = i == selected ? ((UiPaint)Tone.Accent).WithOpacity(0.18f)
                     : i % 2 == 1 ? ((UiPaint)Tone.SurfaceRaised).WithOpacity(0.35f) : Tone.None;

            Span<UiSize> columns = stackalloc UiSize[4];
            WareColumns(columns);
            var row = ui.Grid(columns, Space.S).Size(Size.Fill, Size.Fill).Fill(fill).Pad(Space.S, Space.M);
            var hit = ui.Interact(row);
            // The pointer on a stock row reports it; the view-model answers
            // on the next frame naming the row it put hint content for, and
            // that row's grid box becomes the popup's anchor. (The buy-back
            // list is the player's own sold goods — no hint subject there.)
            if (!sold && hit.Hovered)
                owner._actions.HoverWare(i);
            if (!sold && owner._wareHover is { WareIndex: var w } && w == i)
            {
                owner._wareAnchor = row;
                owner._hasWareAnchor = true;
            }
            using (row)
            {
                ui.Sub("chip", _chip, item.Rarity);
                ui.Data(item.BaseName, Text.Body).Color(paint).Elide();
                if (item.Identified)
                    ui.Number(item.ItemLevel, Text.Body, Tone.TextMuted).TextAlign(Align.End);
                else
                    ui.Text("#Descent_Tip_Unidentified", Text.Body, Tone.Warning).TextAlign(Align.End).Elide();
                using (ui.Row(Space.XS, Align.Centre).Align(Align.End, Align.Centre))
                {
                    ui.Shape(UiShape.Dot, DescentPaints.Australium, 12);
                    ui.Number(PriceDigits(ware.Price), Text.Body, ware.Affordable ? Tone.Text : Tone.Danger);
                }
            }
            if (hit.Clicked)
            {
                // Selecting a row belongs to the list that drew it.
                if (sold) owner._sold = i;
                else owner._ware = i;
            }
        }
    }

    /// <summary>
    /// The footer's tab strip: Buy / Sell / Buy-back (the interactive twin of
    /// the display-only <c>ui.Tabs</c>: same cells, same underline, and a click
    /// sets the screen's tab). The third cell only appears when the clamp allows.
    /// </summary>
    private sealed class VendorTabRow(Vendor owner) : UiPanel<int>
    {
        public override void Describe(ref UiFrame ui, in int selected)
        {
            using (ui.Row(Space.L, Align.End))
            {
                for (int i = 0; i < 3; i++)
                {
                    if (i == 1 && !owner._vendor.SellTab) continue;
                    if (i == 2 && owner._vendor.SellCount == 0) continue;
                    bool on = i == selected;
                    var cell = ui.Column(Space.XS, Align.Stretch).Key(i).Size(Size.Auto, Size.Du(44));
                    var hit = ui.Interact(cell);
                    using (cell)
                    {
                        ui.Text(Token(i), Text.Heading, on ? Tone.Text : Tone.TextMuted);
                        ui.Rect(on ? Tone.Accent : Tone.None, Size.Fill, Size.Du(4));
                    }
                    if (hit.Clicked) owner._tab = i;
                }
            }
        }

        private static string Token(int tab) => tab switch
        {
            1 => "#Descent_Vendor_Tab_Sell",
            2 => "#Descent_Vendor_Tab_BuyBack",
            _ => "#Descent_Vendor_Tab_Buy",
        };
    }
}
