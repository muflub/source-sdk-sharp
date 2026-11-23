using SourceSharp.Abi;

using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Inventory;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Items;
using TF2.Descent.Shared;
using TF2.Descent.Tests.Gear;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// A hero with a belt for the HE8 facts: <see cref="EquipmentRig"/>'s joined hero and
/// in-memory ledger, a fake belt world (the clock, alive, stunned, the TF health), the belt,
/// the consumables over a portal a fact may open, and the belt's sync over a recording channel.
/// </summary>
internal sealed class BeltRig
{
    public const int Index = EquipmentRig.Index;

    public readonly EquipmentRig Gear = new();
    public readonly World Clock;
    public readonly Portal Portals = new();
    public readonly RecordingEngine Messages = new();
    public readonly Channel Sent = new();
    public readonly HeroBelt Belt;
    public readonly HeroConsumables Consumables;
    public readonly BeltSync Sync;
    public readonly StatSource Stats = new();

    public BeltRig()
    {
        Clock = new World(Gear);
        var notices = new ClientMessages(Messages);
        Belt = new HeroBelt(Gear.Rules, Gear.Hero.Heroes, Gear.Keys, Gear.Ledger, Clock, notices, Gear.Log);
        Consumables = new HeroConsumables(Gear.Rules, Gear.Hero.Heroes, Gear.Keys, Gear.Ledger, Portals, Clock, notices, Gear.Log);
        Sync = new BeltSync(Gear.Rules, Belt, Gear.Hero.Heroes, Gear.Keys, Gear.Ledger, Sent, Gear.Hero.Engine);
        Gear.LedgerHooks.Add(Sync);
        Gear.Hero.Contributions.Add(Stats);
        Gear.Health = 100;
    }

    public HeroComponent Hero => Gear.HeroComponent;

    public LedgerHero Held => Gear.Held;

    public static string Key => EquipmentRig.Key;

    /// <summary>The belt slot (0-based) as the ledger holds it, or null.</summary>
    public Held? BeltSlot(int index) => Held.Inventory.Belt.GetValueOrDefault(index);

    /// <summary>Plants a belt slot (0-based) holding <paramref name="count"/> of a potion, telling no hook.</summary>
    public void PlantBelt(int index, string potion, int count, long id = 700)
    {
        var item = TF2.Descent.Tests.Items.ItemFixtures.Item(potion, 0, 1) with { Count = count, Durability = 0, MaxDurability = 0 };
        Gear.Ledger.Replace(Held with { Inventory = Held.Inventory with { Belt = Held.Inventory.Belt.SetItem(index, new Held(id, item)) } });
    }

    /// <summary>Puts an item in the backpack through the floor; answers its slot.</summary>
    public int Give(ItemInstance item) => Gear.Give(item);

    /// <summary>A potion (or anything) carried in the backpack: planted there, not picked up (a pickup belts a potion).</summary>
    public int Carry(ItemInstance item) => Gear.Carry(item);

    public static ItemInstance Carried(string baseId, int count = 1) =>
        TF2.Descent.Tests.Items.ItemFixtures.Item(baseId, 0, 1) with { Count = count, Durability = 0, MaxDurability = 0 };

    public BeltDrink Drink(int slot) => Belt.Drink(Index, slot).Result;

    public ItemUse Use(int slot, int target = -1) => Consumables.Use(Index, slot, target).Result;

    /// <summary>The frame's end, for the sync.</summary>
    public void Frame() => ((IFrameLifecycle)Sync).OnPostThink(0.015f);

    //-------------------------------------------------------------------------

    internal sealed class World(EquipmentRig rig) : IBeltWorld
    {
        public int Tick { get; set; } = 1000;
        public float TickInterval => 0.015f;
        public bool Alive { get; set; } = true;
        public bool IsStunned { get; set; }
        public bool IsAlive(EntityHandle player) => Alive;
        public bool Stunned(EntityHandle player) => IsStunned;
        public int Health(EntityHandle player) => rig.Health;
        public int MaxHealth(EntityHandle player) => rig.Resupply.MaxHealthNow;

        public int Heal(EntityHandle player, float amount)
        {
            int healed = Math.Min((int)amount, rig.Resupply.MaxHealthNow - rig.Health);
            rig.Health += Math.Max(0, healed);
            return Math.Max(0, healed);
        }
    }

    internal sealed class Portal : ITownPortal
    {
        /// <summary>What a portal that does not open says.</summary>
        public const string Why = "no portal here";

        public bool Opens { get; set; }
        public int Asked { get; private set; }

        public PortalAnswer Open(int playerIndex)
        {
            Asked++;
            return new PortalAnswer(Opens, Opens ? null : Why);
        }
    }

    internal sealed class Channel : IBeltChannel
    {
        public List<(int Player, Belt Belt)> Seen { get; } = [];
        public void Send(int playerIndex, Belt belt) => Seen.Add((playerIndex, belt));
    }

    /// <summary>A contribution source a fact sets (Field Rations' two stats).</summary>
    internal sealed class StatSource : IHeroContributionSource
    {
        public List<Contribution> Given { get; } = [];
        public IEnumerable<Contribution> ContributionsOf(int playerIndex, HeroSheet sheet) => Given;
    }
}
