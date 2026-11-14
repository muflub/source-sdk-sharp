using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Descent.Rules.Hero;
using TF2.Descent.Server;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Items;

using Xunit;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// The item ledger's copy of a hero's sheet follows the live one
/// (<see cref="HeroLedgerSheet"/>): after a level-up and after
/// <c>tf2_descent_setlevel</c> the ledger holds the hero's sheet; a hero the
/// ledger has not loaded is skipped; nothing is sent while the stats stand.
/// </summary>
public sealed class HeroLedgerSheetTests
{
    private sealed class NoHooks : IHooks<IItemLedgerHook>
    {
        public IReadOnlyList<IItemLedgerHook> All => [];
        public void Each(string what, Action<IItemLedgerHook> call) { }
    }

    /// <summary>A ledger that counts the sheets it is handed and passes every call to the in-memory one.</summary>
    public class CountingLedger : DispatchProxy
    {
        public IItemLedger Inner { get; set; } = null!;
        public int Sets { get; private set; }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IItemLedger.SetSheet))
                Sets++;
            return method.Invoke(Inner, args);
        }
    }

    private sealed class NoCheckpoints : IHeroLevelCheckpoint
    {
        public bool Checkpoint(int entIndex) => false;
    }

    private sealed record Kit(HeroRig Rig, IItemLedger Ledger, CountingLedger Counted, string Key);

    private static async Task<Kit> Make(bool load = true)
    {
        var rig = new HeroRig();
        rig.Engine.Returns("Players.IsAlive", true);
        var inner = new InMemoryItemLedger(rig.Rules, new NoHooks(), new StartingKit(rig.Rules));
        var ledger = DispatchProxy.Create<IItemLedger, CountingLedger>();
        var counted = (CountingLedger)(object)ledger;
        counted.Inner = inner;
        var keys = rig.Keys();
        string key = keys.Of(HeroRig.Index)!;
        Assert.Equal(HeroRig.SteamId.ToString(System.Globalization.CultureInfo.InvariantCulture), key);
        if (load)
            Assert.True((await ledger.Load(key)).Ok);
        rig.Watchers.Add(new HeroLedgerSheet(keys, ledger));
        return new Kit(rig, ledger, counted, key);
    }

    [Fact]
    public async Task After_a_level_up_the_ledger_holds_the_heros_sheet()
    {
        var k = await Make();
        var hero = k.Rig.Join()!;
        var xp = new HeroExperience(k.Rig.Heroes, k.Rig.Sheets, k.Rig.Engine, k.Rig.Engine, new Party.OneParty(), new ClientMessages(k.Rig.Engine),
                                    new NoCheckpoints(), k.Rig.Log);

        Assert.NotNull(xp.Grant(hero, k.Rig.Rules.Heroes.XpAtLevel(4), new Dictionary<string, int>(), null, "a fact"));

        Assert.Equal(4, hero.Sheet!.Level);
        Assert.Same(hero.Sheet, k.Ledger.Find(k.Key)!.Sheet);
    }

    [Fact]
    public async Task After_setlevel_the_ledger_holds_the_heros_sheet()
    {
        var k = await Make();
        var hero = k.Rig.Join()!;

        new HeroSetLevelCommand(DeclaredConVars.Holding<SvCheatsConVar>("1"), k.Rig.Heroes, k.Rig.Log).Execute(HeroRig.Index, 10);

        Assert.Equal(10, hero.Sheet!.Level);
        Assert.Equal(10, k.Ledger.Find(k.Key)!.Sheet.Level);
    }

    [Fact]
    public async Task A_hero_the_ledger_has_not_loaded_is_skipped()
    {
        var k = await Make(load: false);
        var hero = k.Rig.Join()!;

        hero.Apply(s => k.Rig.AtLevel(s, 3));

        Assert.Null(k.Ledger.Find(k.Key));
        Assert.Equal(0, k.Counted.Sets);
    }

    [Fact]
    public async Task Nothing_is_sent_while_the_stats_stand()
    {
        var k = await Make();
        var hero = k.Rig.Join()!;
        int afterJoin = k.Counted.Sets;
        Assert.True(afterJoin > 0, "the stimulus: the arriving sheet went to the ledger");

        hero.Tick(1f);
        hero.Tick(2f);

        Assert.Equal(afterJoin, k.Counted.Sets);
    }
}
