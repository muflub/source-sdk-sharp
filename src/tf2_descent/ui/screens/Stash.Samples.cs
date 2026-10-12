using System.Collections.Generic;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using TF2.Descent.Client.Ui.Parts;

namespace TF2.Descent.Client.Ui.Screens;

// The stash from fakes (the ui-screen loop's sample step). Item names are
// ui.Data (game copy, case kept); every #Descent_Stash_* token lives in
// game/mod_tf2_descent/resource/descent_english.txt.

public static class StashSamples
{
    /// <summary>A view built like the server's 2i projection; names read as game copy.</summary>
    public static DescentItemView Item(string name, DescentRarity rarity = DescentRarity.Stock,
                                       int level = 0, bool identified = true, int dur = 0, int durMax = 0,
                                       bool broken = false)
        => new(name, rarity, level, identified, dur, durMax, identified ? 2 : 0, false, broken, false, false);

    public static UiScreen Build(UiSampleServices s, IDescentItemGridSource backpack, IDescentStash stash,
                                 FakeStashActions actions, MemoryWindowStore? store = null,
                                 IDescentItemHover? hover = null)
    {
        if (!s.Has<IUiWindowStore>())
            s.Add<IUiWindowStore>(store ?? new MemoryWindowStore());
        s.Add(backpack);
        s.Add(stash);
        s.Add<IDescentItemActions>(actions);
        // The tooltip's subject: the container builds the shared ItemTooltip
        // part from it (the Inventory's pattern). The resting samples leave
        // it inactive — an inactive tooltip draws nothing, which is the
        // pointer-less state.
        s.Add<IDescentItemHover>(hover ??= new FakeInvHover());
        return s.Create<Stash>();
    }

    [UiSample("two grids populated")]
    private static UiScreen Both(UiSampleServices s)
        => Build(s, new FakeStashBag(), new FakeStashLocker(), new FakeStashActions());

    /// <summary>The pointer on a locker cell: the ItemTooltip hangs at the
    /// stash grid (the fake forces it; a real pointer reports through
    /// <see cref="IDescentItemActions.Hover"/> with the owner-screen keying).
    /// Hovering a stash cell — the screen's own side — anchors the popup
    /// there.</summary>
    [UiSample("cell-hovered")]
    private static UiScreen CellHovered(UiSampleServices s)
        => Build(s, new FakeStashBag(), new FakeStashLocker(), new FakeStashActions(),
                 hover: new FakeInvHover
                 {
                     Hovering = true,
                     Item = Item("The Shortstop", DescentRarity.Unusual, 41, dur: 18, durMax: 22),
                     Family = "shotgun",
                 });

    [UiSample("empty stash")]
    private static UiScreen EmptyStash(UiSampleServices s)
        => Build(s, new FakeStashBag(),
                 new FakeStashLocker { Cells = [], CellTotal = 0, RolledCapacity = 40 },
                 new FakeStashActions());
}

/// <summary>The backpack (2d): filled past its rolled capacity of 24 — the tail is withdraw-only.</summary>
public sealed class FakeStashBag : IDescentItemGridSource
{
    public DescentItemView[] Cells =
    [
        StashSamples.Item("Force-A-Nature", DescentRarity.Vintage, 1, dur: 47, durMax: 100),
        StashSamples.Item("The Soul Catcher", DescentRarity.Strange, 2),
        StashSamples.Item("Chargin' Tee", DescentRarity.Stock),
        StashSamples.Item("Deadhead", DescentRarity.Vintage, 3, dur: 0, durMax: 120, broken: true),
        StashSamples.Item("Scottish Resistance", DescentRarity.Stock),
        StashSamples.Item("Consifer Concocter", DescentRarity.Vintage, 1),
        StashSamples.Item("Clad Killer", DescentRarity.Strange, 2),
        StashSamples.Item("Winger", DescentRarity.Stock, dur: 88, durMax: 120),
        StashSamples.Item("Blast Cannon", DescentRarity.Unusual, 3),
        StashSamples.Item("Splendid Screen", DescentRarity.Vintage, 1),
        StashSamples.Item("Reserve Shooter", DescentRarity.Stock),
        StashSamples.Item("Beggars Bullets", DescentRarity.Vintage, 2),
        StashSamples.Item("Airdrier", DescentRarity.Strange, 1),
        StashSamples.Item("Back Scatter", DescentRarity.Stock),
        StashSamples.Item("Lady Killer", DescentRarity.Vintage, 4),
        StashSamples.Item("Sharp Dresser", DescentRarity.Vintage, 2),
        StashSamples.Item("Wears Your Tights", DescentRarity.Stock),
        StashSamples.Item("Hitman's Heatmaker", DescentRarity.Strange, 3),
        StashSamples.Item("Quickieboot Loop", DescentRarity.Vintage, 1),
        StashSamples.Item("Fixer's Chamber", DescentRarity.Stock),
        StashSamples.Item("Lounge Fighter", DescentRarity.Vintage, 2),
        StashSamples.Item("Cammera Chauger", DescentRarity.Strange, 1),
        // Past the rolled 24 (2d): these may only leave towards the game.
        StashSamples.Item("Sapper's Spyball", DescentRarity.Vintage, 2),
        StashSamples.Item("Shota's Boot", DescentRarity.Stock),
        StashSamples.Item("Escape Assist", DescentRarity.Vintage, 1),
        StashSamples.Item("Crossing Guard", DescentRarity.Strange, 3),
    ];

    public int CellCount => Cells.Length;
    public int Capacity => 24;
    public bool Overflow => true;
    public DescentItemView? Cell(int i) => i < Cells.Length ? Cells[i] : null;
}

/// <summary>The account's shared locker (D4): sparse, capacity = the highest Storage Plan rank.</summary>
public sealed class FakeStashLocker : IDescentStash
{
    public DescentItemView[] Cells =
    [
        StashSamples.Item("Golden Frying Pan", DescentRarity.Australium, 5),
        StashSamples.Item("A Fun Times", DescentRarity.Unusual, 3),
        StashSamples.Item("Third Degree", DescentRarity.Vintage, 2),
        StashSamples.Item("The Apoco Fists", DescentRarity.Strange, 1),
        StashSamples.Item("Targardite", DescentRarity.Stock),
        StashSamples.Item("Bolted Blasters", DescentRarity.Vintage, 1, identified: false),
    ];
    public int CellTotal = 6;
    public int RolledCapacity = 40;

    public int CellCount => CellTotal;
    public int Capacity => RolledCapacity;
    public bool Overflow => false;
    public int StoragePlanRankShown => 3;
    public DescentItemView? Cell(int i) => i < Cells.Length ? Cells[i] : null;
}

/// <summary>Records the moves the screen asks for; hover/use belong to the inventory's clicks.</summary>
public sealed class FakeStashActions : IDescentItemActions
{
    public List<(int From, int To)> Moves { get; } = [];
    public void Hover(int slotOrCell) { }
    public void Use(int slotOrCell) { }
    public void Move(int fromCell, int toCell) => Moves.Add((fromCell, toCell));
}
