using SourceSharp.Abi;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server.Items;
using Xunit;
using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.Items.Ledger;

/// <summary>What the item ledger's dev commands print, and the registrations the ledger needs from the container.</summary>
public sealed class ItemCommandFacts
{
    private static readonly TF2.Descent.Rules.TF2DescentRules R = NewRules();

    private sealed class NoHooks : IHooks<IItemLedgerHook>
    {
        public IReadOnlyList<IItemLedgerHook> All => [];
        public void Each(string what, Action<IItemLedgerHook> call) { }
    }

    private sealed class RecordingRegistry : ISideServiceRegistry
    {
        public List<Type> Hooks { get; } = new();
        public List<Type> Factories { get; } = new();
        public void Add<TService, TImplementation>() where TService : class where TImplementation : class, TService { }
        public void AddInstance<TService>(TService instance) where TService : class { }
        public void AddFactory<TService>(Func<IServiceLocator, TService> factory) where TService : class => Factories.Add(typeof(TService));
        public void AddHooks<THook>() where THook : class => Hooks.Add(typeof(THook));
    }

    [Fact]
    public async Task The_inventory_print_shows_each_item_as_its_owner_may_see_it()
    {
        var ledger = new InMemoryItemLedger(R, new NoHooks(), new StartingKit(R));
        await ledger.Load("h");
        var hidden = R.ItemRules.Roller.Roll(Ctx(2, 10, table: "base:rocket_launcher", source: "dev_give"), 5);
        Assert.False(hidden.Identified);
        var floor = ledger.PlaceOnFloor(hidden, "h", default);
        await ledger.Claim("h", floor.Id);

        string text = new ItemReport(R).Inventory(ledger.Kind, ledger.Find("h")!);

        Assert.Contains($"backpack 3: #{floor.Id} {{\"base\":\"rocket_launcher\",\"rarity\":2,\"itemLevel\":10}}\n", text);
        Assert.Contains("backpack 0: #1 {\"base\":\"rocket_launcher\",\"rarity\":0,\"itemLevel\":1,\"identified\":true,", text);
        Assert.Contains("belt 2: ", text);
        Assert.StartsWith("[TF2Descent/Items] h (InMemory): soldier level 1, Australium 0, backpack 4, equipped 0, belt 3, stash 0\n", text);
    }

    [Fact]
    public void The_registrar_adds_the_change_hook_and_the_host_items()
    {
        var registry = new RecordingRegistry();

        new ItemLedgerRegistrar().Register(registry);

        Assert.Equal([typeof(IItemLedgerHook)], registry.Hooks);
        Assert.Equal([typeof(IHostItems)], registry.Factories);
    }
}
