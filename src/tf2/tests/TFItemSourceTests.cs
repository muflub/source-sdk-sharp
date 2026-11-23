using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Econ;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Tests.Player;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// The item source chain (<see cref="ITFItemSource"/>): a source loaded over
/// TF2's own supplies the items of the players it claims, slot by slot, and
/// leaves everyone else's kit to the inventory.
/// </summary>
public sealed class TFItemSourceTests
{
    private const int Seat = 3;
    private const int OtherSeat = 4;
    private const string Launcher = "tf_weapon_rocketlauncher";

    private readonly RecordingLog _log = new();
    private readonly ItemSchema _schema = EconTestFiles.TrimmedSchema();

    private static EntityHandle Player => new((uint)Seat);

    private sealed class Classes(TFClass cls) : ITFPlayerClasses
    {
        public TFClass ClassOf(EntityHandle player) => cls;
    }

    /// <summary>A source answering from a function; what it was asked is on record.</summary>
    private sealed class Source(Func<int, TFClass, TFLoadoutSlot, TFSlotItem> answer) : ITFItemSource
    {
        public int Asked { get; private set; }

        public TFSlotItem ItemInLoadout(int entIndex, TFClass cls, TFLoadoutSlot slot)
        {
            Asked++;
            return answer(entIndex, cls, slot);
        }
    }

    /// <summary>A server nobody has an inventory on: every slot is the class's base item.</summary>
    private LocalInventory Inventory() => new(
        new InventoryStore(new ModInfo(_ => null), _schema, _log), _schema, new TFWeaponOwnersTests.NoClients(), _log);

    private TFLoadoutProvider Provider(TFPlayerItems? items = null, params ITFItemSource[] sources)
        => LoadoutDeps.Loadout(Inventory(), _schema, new Classes(TFClass.Soldier), items: items, log: _log,
                               sources: sources);

    /// <summary>The stock rocket launcher (defindex 18) as an owned instance carrying <c>mult_dmg</c> 1.5.</summary>
    private ItemInstance RolledLauncher()
    {
        var damage = new AttributeDefinition(9001, "damage bonus", "mult_dmg", AttributeDescriptionFormat.Percentage,
                                             AttributeEffectType.Positive, false, false,
                                             AttributeValueType.Default, MergedKeys.Block("x"));
        return new ItemInstance(77, _schema.Item(18)!, 6, [AttributeValue.OfFloat(damage, 1.5f)]);
    }

    private static ItemInstance? InSlot(TFLoadoutProvider provider, int seat, TFLoadoutSlot slot)
        => provider.Loadout(seat, TFClass.Soldier).Where(e => e.Slot == slot).Select(e => e.Item).SingleOrDefault();

    [Fact]
    public void ASourceWithNoOpinionLeavesTheInventorysKit()
    {
        var silent = new Source((_, _, _) => TFSlotItem.NoOpinion);

        int[] alone = [.. Provider().Loadout(Seat, TFClass.Soldier).Select(e => e.Item.Defindex)];
        int[] chained = [.. Provider(null, silent).Loadout(Seat, TFClass.Soldier).Select(e => e.Item.Defindex)];

        Assert.NotEmpty(alone);
        Assert.True(silent.Asked > 0, "the source was never asked");
        Assert.Equal(alone, chained);
    }

    [Fact]
    public void AClassedPlayerWhoseSourcesEmptyEveryWeaponSlotOwnsAnEmptyWeaponColumn()
    {
        var empty = new Source((_, _, slot) => slot is >= TFLoadoutSlot.Primary and <= TFLoadoutSlot.Melee
            ? TFSlotItem.Empty
            : TFSlotItem.NoOpinion);
        var provider = Provider(null, empty);

        Assert.NotEmpty(Provider().Weapons(Player));   // the control: the inventory's kit
        Assert.Empty(provider.Weapons(Player));
        Assert.True(provider.OwnsWeapons(Player), "an empty kit is no weapons, not the core's HL2 kit");
    }

    [Fact]
    public void APlayerWithNoClassOwnsNoWeaponColumn()
    {
        var provider = LoadoutDeps.Loadout(Inventory(), _schema, new Classes(TFClass.Undefined), log: _log);

        Assert.False(provider.OwnsWeapons(Player));
    }

    [Fact]
    public void ASourcesItemIsTheOneInItsSlot()
    {
        var rolled = RolledLauncher();
        var source = new Source((seat, _, slot) => seat == Seat && slot == TFLoadoutSlot.Primary
            ? TFSlotItem.Of(rolled)
            : TFSlotItem.NoOpinion);

        Assert.Same(rolled, InSlot(Provider(null, source), Seat, TFLoadoutSlot.Primary));
    }

    [Fact]
    public void ASourcesInstanceAttributeReachesTheWeaponsDamageHook()
    {
        var rolled = RolledLauncher();
        var source = new Source((seat, _, slot) => seat == Seat && slot == TFLoadoutSlot.Primary
            ? TFSlotItem.Of(rolled)
            : TFSlotItem.NoOpinion);
        var items = new TFPlayerItems();
        var hooks = new AttributeHooks();

        Assert.Contains(Launcher, Provider(items, source).Weapons(Player));

        // The launcher's item, as the weapon's damage roll asks it
        // (ITFWeaponOwners.ItemOf): 100 through mult_dmg is 150.
        Assert.Equal(150f, hooks.Float(100f, "mult_dmg", items.Of(Seat, Launcher)), 0.001f);
    }

    [Fact]
    public void TheInventorysLauncherCarriesNoDamageBonus()
    {
        // The control for the fact above: the same ask of the base kit.
        var items = new TFPlayerItems();
        var hooks = new AttributeHooks();

        Assert.Contains(Launcher, Provider(items).Weapons(Player));

        Assert.Equal(100f, hooks.Float(100f, "mult_dmg", items.Of(Seat, Launcher)), 0.001f);
    }

    [Fact]
    public void ASourceHoldsASlotEmpty()
    {
        var source = new Source((seat, _, slot) => seat == Seat && slot == TFLoadoutSlot.Secondary
            ? TFSlotItem.Empty
            : TFSlotItem.NoOpinion);

        Assert.NotNull(InSlot(Provider(), Seat, TFLoadoutSlot.Secondary)); // the base shotgun, unclaimed
        Assert.Null(InSlot(Provider(null, source), Seat, TFLoadoutSlot.Secondary));
    }

    [Fact]
    public void APlayerTheSourceDoesNotClaimKeepsTheInventorysItem()
    {
        var rolled = RolledLauncher();
        var source = new Source((seat, _, slot) => seat == Seat && slot == TFLoadoutSlot.Primary
            ? TFSlotItem.Of(rolled)
            : TFSlotItem.NoOpinion);

        var other = InSlot(Provider(null, source), OtherSeat, TFLoadoutSlot.Primary);

        Assert.NotNull(other);
        Assert.True(other!.IsBaseItem);
    }

    [Fact]
    public void TheLastSourceWithAnOpinionWins()
    {
        var first = RolledLauncher();
        var second = RolledLauncher();
        var early = new Source((_, _, slot) => slot == TFLoadoutSlot.Primary ? TFSlotItem.Of(first) : TFSlotItem.NoOpinion);
        var late = new Source((_, _, slot) => slot == TFLoadoutSlot.Primary ? TFSlotItem.Of(second) : TFSlotItem.NoOpinion);

        Assert.Same(second, InSlot(Provider(null, early, late), Seat, TFLoadoutSlot.Primary));
    }

    [Fact]
    public void ASourceThatThrowsLeavesTheKitThatStoodBeforeIt()
    {
        var broken = new Source((_, _, _) => throw new InvalidOperationException("no hero"));

        var primary = InSlot(Provider(null, broken), Seat, TFLoadoutSlot.Primary);

        Assert.NotNull(primary);
        Assert.Equal(18, primary!.Defindex);
    }

    [Fact]
    public void ASourceThatThrowsIsNamedOnTheLog()
    {
        var broken = new Source((_, _, _) => throw new InvalidOperationException("no hero"));

        Provider(null, broken).Loadout(Seat, TFClass.Soldier);

        Assert.Contains(_log.Warnings(), line => line.Contains(nameof(Source)) && line.Contains("no hero"));
    }

    /// <summary>
    /// The bot's seeded loadout reads through the slot's REAL handle, serial
    /// and all: after a changelevel the handle carries a serial, and a bare
    /// index crosses the engine's handle table as another slot, so the IsBot
    /// ask would miss the bot and the inventory's kit would answer for it too.
    /// </summary>
    [Fact]
    public void ABotsSeededLoadoutReadsThroughTheSerialHandle()
    {
        var serial = new EntityHandle(0x0001_0000u | Seat);

        var engine = new RecordingEngine();
        engine.Returns("Players.GetHandle", serial);
        engine.Returns("Players.GetActiveWeapon", string.Empty);
        engine.Returns("Precache.PrecacheModel", 0);

        var files = new EconTestFiles().With(BotInventory.TablePath,
            "defindex\tname\titem_class\tslot\tclasses\tfamily\tattributes\tstatus\tevidence\n" +
            "22\tTF_WEAPON_PISTOL\ttf_weapon_pistol\tsecondary\tscout\t\t\tworks\t\n");
        var bots = new SerialHandleBots(serial);

        var provider = LoadoutDeps.Loadout(Inventory(), _schema, new Classes(TFClass.Scout),
                                           botInventory: new BotInventory(_schema, files, bots, _log),
                                           bots: bots, players: engine, log: _log);

        ItemInstance? secondary = provider.Loadout(Seat, TFClass.Scout)
            .Where(e => e.Slot == TFLoadoutSlot.Secondary)
            .Select(e => e.Item)
            .SingleOrDefault();

        Assert.NotNull(secondary);
        Assert.Equal(22, secondary!.Defindex);

        // The IsBot ask itself carried the slot's REAL handle, serial and all:
        // packed value 0x00010003, not the bare entry index 0x00000003.
        Assert.Equal(0x0001_0003u, bots.Seen);
    }

    /// <summary>A roster whose <see cref="IsBot"/> answers only for the slot's serial handle, and records what it was asked with.</summary>
    private sealed class SerialHandleBots(EntityHandle serialHandle) : IBotPlayers
    {
        public EntityHandle Add(string name, IReadOnlyList<KeyValuePair<string, string>> userInfo, out string refusal, int team = 0)
        {
            refusal = "no bots";
            return EntityHandle.None;
        }

        /// <summary>The packed handle of the last <see cref="IsBot"/> ask.</summary>
        public uint Seen { get; private set; }

        public bool IsBot(EntityHandle player)
        {
            Seen = player.Value;
            return player == serialHandle;
        }

        public bool Kick(EntityHandle bot, string reason) => false;

        public IReadOnlyList<BotPlayer> Bots { get; } = [new BotPlayer(serialHandle, Seat, "Delta")];
    }
}
