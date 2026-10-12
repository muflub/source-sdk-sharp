using System;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using TF2.Descent.Client.Ui.Parts;

namespace TF2.Descent.Client.Ui.Screens;

/// <summary>
/// The stash screen (docs/plans/descent_ui.md §2.9): the account's shared
/// locker (D4) beside the backpack. Two <see cref="ItemGrid"/>s — one panel
/// instance, two <c>ui.Sub</c> keys — each under a capacity caption; the
/// stash's caption carries the highest Storage Plan rank note (D4). Cells past
/// the rolled capacity are the 2d withdraw-only tail (the grid draws them
/// dimmed with the lock chip), so a click moves only what may move: a backpack
/// cell inside its capacity deposits into the first free stash well; a stash
/// cell withdraws into the first free backpack well; both through
/// <see cref="IDescentItemActions.Move"/>.
/// Move's indices are the owner-screen keying: backpack cells are
/// [0, bpSpan) and stash cells [bpSpan, bpSpan + spSpan), each span the grid's
/// total (max(CellCount, Capacity)) — the same number of cells
/// <see cref="ItemGrid"/> draws, so a cell's index here is its index there.
/// </summary>
public sealed class Stash : UiScreen, IUiWindowed
{
    private readonly UiWindows _windows;
    private readonly IDescentItemGridSource _backpack;
    private readonly IDescentStash _stash;
    private readonly IDescentItemActions _actions;
    private readonly IDescentItemHover? _hover;
    private readonly ItemTooltip? _tooltip;
    private readonly Action<int> _useBackpack;
    private readonly Action<int> _useStash;
    private readonly Action<int> _hoverBackpack;
    private readonly Action<int> _hoverStash;

    // The tooltip's anchors: the last grid the pointer reported from (the
    // hover keys are the same owner-screen keying as Move's indices, so the
    // host knows which locker the cell lives in), and each side's grid box.
    private UiBox _bpAnchor;
    private UiBox _spAnchor;
    private int _lastHoverSide = 1;
    // The inspect modal (the Inventory's pattern): the cell key the grid's
    // button reports is the owner-screen keying Move uses - backpack cells
    // [0, BpSpan), stash cells from there on - so the host hands the button
    // the raw cell and shifts it per side.
    private readonly IDescentInspect? _inspect;
    private readonly IDescentInspectActions? _inspectActions;
    private readonly InspectModal? _inspectModal;
    private readonly IDescentInspectActions? _inspectBackpack, _inspectStash;

    public Stash(UiWindows windows, IDescentItemGridSource backpack, IDescentStash stash,
                 IDescentItemActions actions, ItemGrid grid,
                 IDescentItemHover? hover = null, ItemTooltip? tooltip = null,
                 IDescentInspect? inspect = null, IDescentInspectActions? inspectActions = null)
    {
        _windows = windows;
        _backpack = backpack;
        _stash = stash;
        _actions = actions;
        _hover = hover;
        _tooltip = tooltip;
        _inspect = inspect;
        _inspectActions = inspectActions;
        _inspectModal = inspect is null ? null : new InspectModal(inspect, inspectActions);
        Grid = grid;
        // The grid's click and hover callbacks, built once: Describe hands
        // out cached delegates, so the per-frame path allocates nothing
        // (SSUI001). The hover keys extend Move's owner-screen keying: a
        // backpack cell reports [0, bpSpan), a stash cell [bpSpan, …).
        _useBackpack = DepositFromBackpack;
        _useStash = WithdrawFromStash;
        _hoverBackpack = cell => { _lastHoverSide = 0; _actions.Hover(cell); };
        _hoverStash = cell => { _lastHoverSide = 1; _actions.Hover(BpSpan + cell); };
        // The slot's button hands the interface its own grid's cell; each
        // side's adapter shifts stash cells onto the Move keying (BpSpan on).
        _inspectBackpack = _inspectActions is null ? null : new SideActions(this, false);
        _inspectStash = _inspectActions is null ? null : new SideActions(this, true);
    }

    /// <summary>One side's inspect sink: the grid hands its raw cell; the
    /// stash side's cells join the Move keying the host's view-model
    /// expects. Close and the view switch pass straight through.</summary>
    private sealed class SideActions(Stash owner, bool stashSide) : IDescentInspectActions
    {
        public void OpenInspect(int slotOrCell) => owner._inspectActions?.OpenInspect(stashSide ? owner.BpSpan + slotOrCell : slotOrCell);
        public void Close() => owner._inspectActions?.Close();
        public void ShowMounted(bool mounted) => owner._inspectActions?.ShowMounted(mounted);
    }

    public ItemGrid Grid { get; }

    public override string Name => "stash";

    public string WindowId => "stash";

    /// <summary>The grid's columns; the cell is <see cref="ItemSlot.SizeDu"/> wide.</summary>
    public const int Columns = 8;

    /// <summary>The grid's width: the cells with the grid's hairline gaps.</summary>
    public const float GridDu = Columns * ItemSlot.SizeDu + (Columns - 1) * (float)Space.XS;

    /// <summary>Below this window width (du) the two lockers stack instead of standing side by side.</summary>
    public const float StackBelowDu = 980;

    /// <summary>
    /// Wide by default (1120 x 560 du, clamped to the screen): the side-by-side
    /// pair is two 412 du grids plus their panels. The minimum keeps one grid
    /// and its panel honest; below 980 du the pair stacks and the default
    /// height grows to keep both grids (captions included) inside the body.
    /// </summary>
    public static UiWindowSpec Spec(float screenW, float screenH)
    {
        float w = MathF.Min(1120f, MathF.Round(screenW * 0.92f));
        float h = MathF.Min(w < StackBelowDu ? 780f : 560f, MathF.Round(screenH * 0.92f));
        return new UiWindowSpec(w, h, 560, 430, screenW, screenH, StackBelowDu);
    }

    public override void Describe(ref UiFrame ui)
    {
        // Esc closes the modal first (the core dialogs' order).
        if (_inspect is { Showing: true } && ui.ConsumeKey((int)UiKey.Escape))
            _inspectActions?.Close();
        using (ui.Window(_windows, WindowId, "#Descent_Stash_Title", Spec(ui.DesignWidth, ui.DesignHeight)))
        {
            if (ui.Narrow)
            {
                Side(ref ui, "#Descent_Stash_Backpack", _backpack, _useBackpack, _hoverBackpack, 0);
                Side(ref ui, "#Descent_Stash_Stash", _stash, _useStash, _hoverStash, 1, _stash.StoragePlanRankShown);
            }
            else
            {
                using (ui.Row(Space.L, Align.Start))
                {
                    Side(ref ui, "#Descent_Stash_Backpack", _backpack, _useBackpack, _hoverBackpack, 0);
                    Side(ref ui, "#Descent_Stash_Stash", _stash, _useStash, _hoverStash, 1, _stash.StoragePlanRankShown);
                }
            }

            // The tooltip last (the Inventory's pattern): the ItemGrid part
            // owns its cells, so the popup hangs from the side the pointer
            // last reported from — while a sample that forces Hovering with
            // no pointer falls back to the stash side, the screen's subject.
            if (_hover is { Hovering: true } && _tooltip is not null)
            {
                using (ui.Overlay("tip").AnchorTo(_lastHoverSide == 0 ? _bpAnchor : _spAnchor,
                           UiAnchor.BottomRight, UiAnchor.TopRight).Inset(Space.None, Space.XS))
                    ui.Sub("itemtip", _tooltip);
            }

            // The inspect modal last and modal (the Inventory's pattern).
            if (_inspect is not null && _inspectModal is { } modal)
                using (ui.Overlay("inspect", modal: true))
                    ui.Sub("inspect", modal);
        }
    }

    /// <summary>
    /// One locker: a raised panel with a caption row — the side's name, its
    /// used/capacity numbers (amber when the backpack runs past capacity, 2d)
    /// and, on the stash side, the Storage Plan rank note (D4) — then the grid,
    /// or the empty state when the locker holds nothing.
    /// </summary>
    private void Side(ref UiFrame ui, string nameToken, IDescentItemGridSource source,
                      Action<int> onUse, Action<int> onHover, int which, int planRank = -1)
    {
        var sideInspect = which == 0 ? _inspectBackpack : _inspectStash;
        using (ui.Panel(Space.M, Space.S))
        {
            using (ui.Row(Space.S).Width(Size.Du(GridDu)))
            {
                ui.Text(nameToken, Text.Caption).Width(Size.Fill).Elide();
                if (planRank >= 0)
                {
                    ui.Text("#Descent_Stash_PlanRank", Text.Caption, Tone.TextMuted);
                    ui.Number(planRank, Text.Caption, Tone.TextMuted);
                }
                ui.Text($"{source.CellCount} / {source.Capacity}", Text.Number,
                        source.CellCount > source.Capacity ? Tone.Warning : Tone.TextMuted);
            }
            if (source.CellCount == 0 && !source.Overflow)
                ui.EmptyState(UiShape.Lock, "#Descent_Stash_Empty", "#Descent_Stash_EmptyBody")
                    .Width(Size.Du(GridDu));
            else
            {
                // The grid box is this side's tooltip anchor.
                var gridBox = ui.Sub(which, Grid,
                    new ItemGridProps(source, Columns, onHover, onUse, sideInspect));
                if (which == 0) _bpAnchor = gridBox; else _spAnchor = gridBox;
            }
        }
    }

    // ---- the moves (owner-screen keying; see the type's doc) -----------------

    /// <summary>A click on a backpack cell: deposit into the first free stash well.</summary>
    private void DepositFromBackpack(int cell)
    {
        // Past the rolled capacity the item may only leave towards the game (2d),
        // never into the locker; the grid already dims those cells.
        if (cell >= _backpack.Capacity || _backpack.Cell(cell) is null)
            return;
        int at = FirstFree(_stash, _stash.Capacity);
        if (at < 0)
            return;                                   // the locker is full: the item stays put
        _actions.Move(cell, BpSpan + at);
    }

    /// <summary>A click on a stash cell: withdraw into the first free backpack well.</summary>
    private void WithdrawFromStash(int cell)
    {
        if (_stash.Cell(cell) is null)
            return;
        int at = FirstFree(_backpack, _backpack.Capacity);
        if (at < 0)
            return;                                   // no room in the backpack
        _actions.Move(BpSpan + cell, at);
    }

    private int BpSpan => Math.Max(_backpack.CellCount, _backpack.Capacity);

    private static int FirstFree(IDescentItemGridSource g, int limit)
    {
        for (int i = 0; i < limit; i++)
            if (g.Cell(i) is null)
                return i;
        return -1;
    }
}
