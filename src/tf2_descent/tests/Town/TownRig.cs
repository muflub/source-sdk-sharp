using SourceSharp.Abi;
using SourceSharp.Host.Contracts;
using SourceSharp.Server;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Items;
using TF2.Descent.Server.OwnerViews;
using TF2.Descent.Server.Town;
using TF2.Descent.Shared;
using TF2.Descent.Tests.OwnerViews;

using Xunit;

namespace TF2.Descent.Tests.Town;

/// <summary>
/// The town's server pieces (plan_descent_logic WO8) over <see cref="OwnerRig"/>'s unhosted hero:
/// the real ledger, his hero component and the recording wire, a fake engine for the town
/// (<see cref="World"/>), and the town, its sender and bench over them. The hero stands at the
/// stall he uses; a fact moves him.
/// </summary>
internal sealed class TownRig
{
    public const int Index = OwnerRig.Index;
    public const string Town = "tf2_descent_town";

    public readonly OwnerRig Owner;
    public readonly FakeWorld World = new();
    public readonly TownService Service;
    public readonly TownCraft Craft;

    public TownRig(bool place = true)
    {
        Owner = new OwnerRig();
        World.Hero = Key;
        Craft = new TownCraft(Rules);
        Service = new TownService(World, Owner.Gear.Ledger, Owner.Gear.Hero.Heroes, Rules,
                                  new TownSender(Owner.Wire, new OwnerItemViews(Rules), Rules), Craft, Owner.Gear.Log);
        Owner.Gear.LedgerHooks.Add(Service);
        Owner.Gear.Hero.Watchers.Add(Service);
        if (place)
            Service.Place(Town);
    }

    public TF2DescentRules Rules => Owner.Rules;

    public static string Key => TF2.Descent.Tests.Gear.EquipmentRig.Key;

    public InMemoryItemLedger Ledger => Owner.Gear.Ledger;

    public LedgerHero Held => Ledger.Find(Key)!;

    public long Purse => Held.Inventory.Australium;

    /// <summary>The hero's purse set as it stands, telling no hook.</summary>
    public void SetPurse(long australium) =>
        Ledger.Replace(Held with { Inventory = Held.Inventory with { Australium = australium } });

    /// <summary>The stall's index by its id.</summary>
    public static int StallOf(string id)
    {
        int i = TownStalls.IndexOf(id);
        Assert.True(i >= 0, $"no stall {id}");
        return i;
    }

    /// <summary>The hero walks up to a stall and uses it: its window opens.</summary>
    public void Use(string stall)
    {
        var placed = Service.Placed.Single(p => p.Stall == StallOf(stall));
        World.Feet = placed.At + new Vector(48f, 0f, 0f);
        Service.Use(placed.Entity, Index);
        Assert.Equal(TownStalls.All[StallOf(stall)].Screen, Service.Open(Index)?.Screen);
    }

    /// <summary>A rolled item into the hero's backpack; answers its slot.</summary>
    public int Give(ItemInstance item) => Owner.Gear.Give(item);

    /// <summary>An item of a base, rolled as a craft rolls one.</summary>
    public ItemInstance Roll(string baseId, int tier = 0, int itemLevel = 1, ulong seed = 1, int count = 1) =>
        Rules.ItemRules.Roller.Roll(new ItemRollContext("soldier", 1, 0, itemLevel, tier, "base:" + baseId, "craft"), seed) with { Count = count };

    /// <summary>Every message the wire carried from <paramref name="from"/> on: its key, its player, its bytes.</summary>
    public IReadOnlyList<(string Key, int Player, byte[] Bytes)> Sent(int from = 0) => [.. Owner.Sent.Skip(from)];

    public int Mark => Owner.Sent.Count;

    public IReadOnlyList<T> Of<T>(int from = 0) where T : IUserMessagePayload<T> => Owner.Of<T>(from);

    /// <summary>The last status line told.</summary>
    public TownStatusView Status => Of<TownStatus>().Last().View();

    /// <summary>The town's engine, faked: the town's six markers, the floor at 0, one hero, a clock and what was asked.</summary>
    internal sealed class FakeWorld : ITownWorld
    {
        public string Hero = "";
        public Vector? Feet;
        public double Clock;
        public int Reached;
        public List<int> Kills = [];
        public string? DescendRefusal;
        public readonly List<(int Player, string Panel, bool Show)> Panels = [];
        public readonly List<(int Index, TownStall Stall, Vector At, float Yaw)> Made = [];
        public readonly List<(int Player, int Depth)> Descents = [];

        /// <summary>The town's markers (src/mapgen/TF2DescentTown.cs: x along the street, 48 in front of each building, 64 up).</summary>
        public readonly Dictionary<string, Vector> Markers = new()
        {
            ["shop_general_store"] = new(-800f, 240f, 64f),
            ["shop_assay_office"] = new(-150f, 240f, 64f),
            ["shop_saloon"] = new(500f, 240f, 64f),
            ["shop_bunkhouse_1"] = new(-800f, -240f, 64f),
            ["shop_bunkhouse_2"] = new(-150f, -240f, 64f),
            ["shop_smithy"] = new(500f, -240f, 64f),
            ["shop_engineer"] = new(324f, -240f, 64f),
            ["shop_bench"] = new(676f, -240f, 64f),
        };

        public bool IsTown(string map) => map == Town;

        public Vector? Marker(string name) => Markers.TryGetValue(name, out var at) ? at : null;

        public Vector Floor(Vector at) => new(at.X, at.Y, 0f);

        public string? HeroOf(int player) => player == Index ? Hero : null;

        Vector? ITownWorld.Feet(int player) => player == Index ? Feet : null;

        public int MakeStall(int index, TownStall stall, Vector at, float yaw)
        {
            Made.Add((index, stall, at, yaw));
            return 100 + index;
        }

        public int StallOf(int entity) => entity >= 100 && entity - 100 < TownStalls.All.Count ? entity - 100 : -1;

        public void Panel(int player, string panel, bool show) => Panels.Add((player, panel, show));

        public readonly List<(int Entity, string Sequence)> Animations = [];

        public bool Animate(int entity, string sequence)
        {
            Animations.Add((entity, sequence));
            return true;
        }

        public double Now => Clock;

        public IReadOnlyList<int> Destinations(int player) => TownDescents.Destinations(Reached, Kills);

        public string? Descend(int player, int depth)
        {
            if (DescendRefusal is { } why)
                return why;
            Descents.Add((player, depth));
            return null;
        }
    }

    /// <summary>The skills' passive stats on the hero's chain (Haggler's price stats), as the skills runtime adds them.</summary>
    internal sealed class SkillPassives(TF2DescentRules rules) : IHeroContributionSource
    {
        public IEnumerable<Contribution> ContributionsOf(int playerIndex, HeroSheet sheet) =>
            rules.Skills.Contributions(sheet, TF2.Descent.Rules.Skills.SkillBonuses.None);
    }
}
