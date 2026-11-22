using System.Collections.Immutable;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Host.Sdk;
using SourceSharp.Server;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Inventory;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server;
using TF2.Descent.Server.Gear;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Items;
using TF2.Descent.Server.Mode;
using TF2.Descent.Tests.Hero;
using TF2.Server.Buildings;
using TF2.Server.Econ;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Econ;

using Xunit;

using ItemInstance = TF2.Descent.Rules.Items.ItemInstance;
using TFItem = TF2.Server.Econ.ItemInstance;

namespace TF2.Descent.Tests.Gear;

/// <summary>
/// A hero for the HE4 facts, unhosted: <see cref="HeroRig"/>'s hero component and sheets,
/// the in-memory ledger, TF's shipped item schema, the equipment pieces over them, and a kit
/// that asks the item source as TF2's refresh does (<see cref="Kit"/>). The hero joins and
/// is loaded, so his first kit is on.
/// </summary>
internal sealed class EquipmentRig
{
    public const int Index = HeroRig.Index;

    /// <summary>The shipped schema, parsed once for every fact and loaded before any reads it (a load is not thread-safe, and facts run in parallel).</summary>
    private static readonly Lazy<ItemSchema> ShippedSchema = new(() =>
    {
        var schema = new ItemSchema(new SchemaFiles(TF2.Descent.Tests.Items.ItemFixtures.ItemsGameText()), new RecordingLog());
        Assert.True(schema.EnsureLoaded(), schema.Failure);
        return schema;
    });

    public readonly HeroRig Hero = new();
    public readonly List<IItemLedgerHook> LedgerHooks = [];
    public readonly InMemoryItemLedger Ledger;
    public readonly HeroKeys Keys;
    public readonly HeroEquipment Equipment;
    public readonly HeroKitSource Source;
    public readonly HeroKitSettle Settle;
    public readonly HeroGearContributions Gear;
    public readonly Kit Refresh;
    public readonly Supply Resupply = new();
    public readonly Purse Ammo = new();
    public readonly BotSet BotList = new();
    public readonly RecordingLog Log = new();
    public bool ModeActive { get; set; } = true;
    public int Health { get; set; } = 150;

    /// <summary>The hero's TF class as TF2 has it (Undefined before he has picked one).</summary>
    public TFClass Class { get; set; } = TFClass.Soldier;

    public EquipmentRig(bool join = true)
    {
        Ledger = new InMemoryItemLedger(Rules, new HeroRig.ListHooks<IItemLedgerHook>(LedgerHooks), new StartingKit(Rules));
        var link = new UnhostedLink();
        var players = new PlayerService(link, Hero.Host, Hero.Players, new ClientMessages(new RecordingEngine()),
                                        new NeverScheduler(), Log, Hero.Sheets, new HeroRig.ListHooks<TF2.Descent.Server.IHeroJoinHook>([]));
        Keys = new HeroKeys(Hero.Host, players, Hero.Players);
        Equipment = new HeroEquipment(Ledger, Keys, Hero.Sheets, Rules, Schema, new Mode(this), BotList, Hero.Engine, Log);
        Source = new HeroKitSource(Equipment);
        Refresh = new Kit(Source);
        Settle = new HeroKitSettle(Equipment, Source, Hero.Heroes, new StartingKit(Rules), Refresh, Resupply, Ammo, new Classes(this),
                                   Hero.Engine, Hero.Engine, Log);
        Gear = new HeroGearContributions(Equipment, Settle);
        LedgerHooks.Add(Settle);
        Hero.Contributions.Add(Gear);

        Hero.Engine.Returns("Players.IsAlive", true);
        Hero.Engine.ReturnsEachCall("Entities.GetHealth", () => Health);
        Hero.Engine.WhenCalled("Entities.SetHealth", call => Health = call.ArgAs<int>(1));

        if (join)
            Join();
    }

    public TF2DescentRules Rules => Hero.Rules;

    public ItemSchema Schema => ShippedSchema.Value;

    /// <summary>The ledger's key of the hero: the engine's SteamID, unhosted.</summary>
    public static string Key => HeroRig.SteamId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static EntityHandle Player => HeroRig.PlayerOf(Index);

    public HeroComponent HeroComponent => Hero.Heroes.Of(Index)!;

    public LedgerHero Held => Ledger.Find(Key)!;

    /// <summary>The player joins (his hero component attaches) and the ledger loads him, as ItemLedger does at the client's activation.</summary>
    public void Join()
    {
        Hero.Join();
        Assert.True(Ledger.Load(Key).Result.Ok);
    }

    /// <summary>A rolled item into the hero's backpack through the floor; answers its backpack slot.</summary>
    public int Give(ItemInstance item)
    {
        var floor = Ledger.PlaceOnFloor(item, Key, default);
        var claimed = Ledger.Claim(Key, floor.Id).Result;
        Assert.True(claimed.Ok, claimed.Refusal);
        return Held.Inventory.Backpack.Single(p => p.Value.Id == floor.Id).Key;
    }

    /// <summary>
    /// Plants an item in the first free backpack slot, telling no hook: a potion carried in the
    /// backpack (a pickup puts one on the belt while it has room).
    /// </summary>
    public int Carry(ItemInstance item, long id = 800)
    {
        int slot = Enumerable.Range(0, 1000).First(i => !Held.Inventory.Backpack.ContainsKey(i));
        Ledger.Replace(Held with { Inventory = Held.Inventory with { Backpack = Held.Inventory.Backpack.Add(slot, new Held(id, item)) } });
        return slot;
    }

    /// <summary>Equips a backpack item through the equipment's own path (the ledger told the live sheet).</summary>
    public LedgerResult Equip(int from, string slot) => Equipment.Equip(Index, from, slot).Result;

    public LedgerResult Unequip(string slot) => Equipment.Unequip(Index, slot).Result;

    /// <summary>Plants the hero's equipment as it stands in the ledger, telling no hook (a broken item, a state the rules would refuse to reach by equipping).</summary>
    public void Plant(string slot, ItemInstance item, long id = 900)
    {
        var h = Held;
        Ledger.Replace(h with { Inventory = h.Inventory with { Equipped = h.Inventory.Equipped.SetItem(slot, new Held(id, item)) } });
    }

    /// <summary>The source's answer for a TF slot as a soldier.</summary>
    public TFSlotItem Answer(TFLoadoutSlot slot, int index = Index) => Source.ItemInLoadout(index, TFClass.Soldier, slot);

    /// <summary>An item of a base with exactly these affixes, identified, at full durability (ItemFixtures.Item).</summary>
    public static ItemInstance Item(string baseId, params RolledAffix[] affixes) =>
        TF2.Descent.Tests.Items.ItemFixtures.Item(baseId, affixes.Length == 0 ? 0 : 2, 1, affixes);

    /// <summary>The frame's end.</summary>
    public void Frame() => ((IFrameLifecycle)Settle).OnPostThink(0.015f);

    //-------------------------------------------------------------------------

    /// <summary>TF2's kit, as far as the settle sees it: each refresh asks the source for the weapon slots, as TF2's loadout walk does, and keeps what it built.</summary>
    internal sealed class Kit(HeroKitSource source) : ITFKitRefresh
    {
        public int Asked { get; private set; }
        public int Rebuilt { get; private set; }
        public Dictionary<TFLoadoutSlot, TFItem?> Holding { get; } = new();

        /// <summary>What the refresh does to the maxima besides (a launcher with fewer rockets).</summary>
        public Action? Changes { get; set; }

        public bool Refresh(EntityHandle player)
        {
            Asked++;
            bool changed = false;
            for (var slot = TFLoadoutSlot.Primary; slot <= TFLoadoutSlot.Melee; slot++)
            {
                var answer = source.ItemInLoadout((int)(player.EntryIndex & 0xFFFF), TFClass.Soldier, slot);
                var now = answer.HasOpinion ? answer.Item : null;
                Holding.TryGetValue(slot, out var was);
                if ((now is null) != (was is null) || (now is not null && !now.SameAs(was!)))
                    changed = true;
                Holding[slot] = now;
            }
            if (changed)
            {
                Rebuilt++;
                Changes?.Invoke();
            }
            return changed;
        }
    }

    internal sealed class Supply : ITFPlayerResupply
    {
        public int MaxHealthNow { get; set; } = 200;
        public Dictionary<TFAmmo, int> Max { get; } = new() { [TFAmmo.Primary] = 20, [TFAmmo.Secondary] = 32, [TFAmmo.Metal] = 200 };
        public void Regenerate(EntityHandle player, bool refillHealthAndAmmo = true) { }
        public int MaxHealth(EntityHandle player) => MaxHealthNow;
        public int TakeHealth(EntityHandle player, float health, bool ignoreMaxHealth = false) => 0;
        public int MaxAmmo(EntityHandle player, TFAmmo ammo) => Max.GetValueOrDefault(ammo);
        public int GiveAmmo(EntityHandle player, int count, TFAmmo ammo) => 0;
    }

    internal sealed class Purse : ITFAmmoPurse
    {
        public Dictionary<string, int> Counts { get; } = new(StringComparer.Ordinal)
        {
            [TFAmmoNames.Name(TFAmmo.Primary)] = 20,
            [TFAmmoNames.Name(TFAmmo.Secondary)] = 32,
        };

        public int TakeAmmo(EntityHandle player, int count, string ammoName)
        {
            int had = Counts.GetValueOrDefault(ammoName);
            int taken = Math.Min(had, count);
            Counts[ammoName] = had - taken;
            return taken;
        }

        public IReadOnlyDictionary<string, int> Pools(EntityHandle player) => Counts;
    }

    internal sealed class BotSet : IBotPlayers
    {
        public HashSet<EntityHandle> Handles { get; } = [];

        public EntityHandle Add(string name, IReadOnlyList<KeyValuePair<string, string>> userInfo, out string refusal, int team = 0)
        {
            refusal = "no bots here";
            return EntityHandle.None;
        }

        public bool IsBot(EntityHandle player) => Handles.Contains(player);
        public bool Kick(EntityHandle bot, string reason) => false;
        public IReadOnlyList<BotPlayer> Bots => [];
    }

    private sealed class Mode(EquipmentRig rig) : IDescentModeState
    {
        public bool Active => rig.ModeActive;
    }

    private sealed class Classes(EquipmentRig rig) : ITFPlayerClasses
    {
        public TFClass ClassOf(EntityHandle player) => rig.Class;
    }

    /// <summary>The engine's GAME path holding the shipped schema at its stock path.</summary>
    private sealed class SchemaFiles(string text) : IEngineFiles
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(text);
        public int GameFileSize(string path) => path == ItemSchema.FilePath ? _bytes.Length : -1;
        public string? ReadGameFile(string path) => path == ItemSchema.FilePath ? text : null;
        public byte[]? ReadGameFileBytes(string path) => path == ItemSchema.FilePath ? _bytes : null;
        public byte[]? ReadFileOnPath(string path, string pathId) => pathId == "GAME" ? ReadGameFileBytes(path) : null;
    }

    private sealed class NeverScheduler : IFrameScheduler
    {
        public Task Delay(TimeSpan delay, CancellationToken cancellation = default) => new TaskCompletionSource().Task;
    }

    /// <summary>A link that says the server runs unhosted and is never asked anything else.</summary>
    private sealed class UnhostedLink : IHostLink
    {
        public HostingMode Mode() => HostingMode.Unhosted;
        public void Start() { }
        public Task<HostResult<BootInfo>> Booting(string map) => throw new NotSupportedException();
        public Task<HostResult<int>> MapReady(int port, string map) => throw new NotSupportedException();
        public void Players(int count) { }
        public Task<JoinOutcome> Join(string peer, string engineSteamId, BootInfo boot, string className) => throw new NotSupportedException();
        public Task<bool> Checkpoint(string steamId, SheetCheckpoint? sheet) => throw new NotSupportedException();
        public Task<bool> Leave(string steamId, string peer, SheetCheckpoint? sheet) => throw new NotSupportedException();
        public void ReplyExec(string commandId, string output) { }
        public Task<TravelAnswer> Travel(string steamId, TravelDirection direction, BootInfo boot, int depth) => throw new NotSupportedException();
        public Task<TicketAnswer> SubmitTicket(string steamId, string nonce, byte[] ticket) => throw new NotSupportedException();
        public Task<bool> RecordDeath(string steamId, Vector position) => throw new NotSupportedException();
        public Task<LootAnswer> LootCorpse(string steamId) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
