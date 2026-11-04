/*
 * TF2 STAGE 1 CORE: THE CARRY LIMIT IS THE PLAYER'S.
 *
 * AmmoType.MaxCarry is one number for everybody; TF2's class file names its
 * own (the AmmoMax block), so the core folds the row's limit through the mods'
 * IAmmoCarryRule chain (HookOrder, ties refused) wherever it clamps or fills a
 * reserve. No rule: the row's own, which is every fact below's control arm.
 * The client's half (the HUD limit, and both sides agreeing) is
 * AmmoCarryParityTests.
 */

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

public class AmmoCarryTests
{
    private static readonly AmmoType Pistol = AmmoCatalog.Stock().For("Pistol")!.Value;

    /// <summary>Raises one player's pistol limit to a fixed number.</summary>
    private sealed class RaisesPistol(int player, int to, int order = 0) : IAmmoCarryRule, IOrderedHook
    {
        public int HookOrder => order;

        public int MaxCarry(int playerIndex, in AmmoType ammo, int maxCarry)
            => playerIndex == player && ammo.Name == "Pistol" ? to : maxCarry;
    }

    private sealed class Adds(int amount, int order) : IAmmoCarryRule, IOrderedHook
    {
        public int HookOrder => order;
        public int MaxCarry(int playerIndex, in AmmoType ammo, int maxCarry) => maxCarry + amount;
    }

    private sealed class Throws(int order) : IAmmoCarryRule, IOrderedHook
    {
        public int HookOrder => order;
        public int MaxCarry(int playerIndex, in AmmoType ammo, int maxCarry) => throw new InvalidOperationException("broken rule");
    }

    /// <summary>The limits over a side container holding <paramref name="rules"/>, registered as [SideService] does.</summary>
    private static AmmoCarry Over(RecordingLog log, params IAmmoCarryRule[] rules)
    {
        var container = new ServiceContainer();
        foreach (var rule in rules)
            container.AddInstance(rule.GetType(), rule);
        return new AmmoCarry(new Hooks<IAmmoCarryRule>(container, log), log);
    }

    [Fact]
    public void WithNoRulesEveryPlayerCarriesTheRowsLimit()
    {
        Assert.Equal(Pistol.MaxCarry, AmmoCarry.Stock().MaxFor(1, in Pistol));
        Assert.Equal(Pistol.MaxCarry, Over(new RecordingLog()).MaxFor(1, in Pistol));
    }

    [Fact]
    public void ARulesLimitIsThatPlayersAndNotTheNext()
    {
        var limits = Over(new RecordingLog(), new RaisesPistol(player: 1, to: 200));

        Assert.Equal((200, Pistol.MaxCarry), (limits.MaxFor(1, in Pistol), limits.MaxFor(2, in Pistol)));
    }

    [Fact]
    public void RulesFoldInHookOrderNotRegistrationOrder()
    {
        // Registered add-then-raise; ordered raise (10) then add (20): 200 + 5.
        // The other order would answer 200.
        var limits = Over(new RecordingLog(), new Adds(5, order: 20), new RaisesPistol(player: 1, to: 200, order: 10));

        Assert.Equal(205, limits.MaxFor(1, in Pistol));
    }

    [Fact]
    public void AThrowingRuleIsNamedAndTheLimitBeforeItStands()
    {
        var log = new RecordingLog();
        var limits = Over(log, new RaisesPistol(player: 1, to: 200, order: 10), new Throws(order: 20), new Adds(1, order: 30));

        Assert.Equal(201, limits.MaxFor(1, in Pistol));
        Assert.Single(log.Lines, l => l.Warning && l.Text.Contains(typeof(Throws).FullName!) && l.Text.Contains("keeps 200"));
    }

    [Fact]
    public void ANegativeLimitCarriesNothing()
    {
        var limits = Over(new RecordingLog(), new Adds(-1000, order: 0));

        Assert.Equal(0, limits.MaxFor(1, in Pistol));
    }

    //-------------------------------------------------------------------------
    // The arsenal clamps to it.
    //-------------------------------------------------------------------------

    private static readonly EntityHandle One = new(0x0001_0001u);
    private static readonly EntityHandle Two = new(0x0001_0002u);

    private static Arsenal Arsenal(AmmoCarry limits)
    {
        var engine = Mock().Returns("Entities.IsValid", true).Returns("Entities.GetIndex", 1)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Players.IsAlive", true);
        engine.Strict = false;

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = Services(out _);
        registry.KeyValue(One, EntityNames.Player, "targetname", "one");
        registry.KeyValue(Two, EntityNames.Player, "targetname", "two");

        return new Arsenal(new RecordingLog(), engine, engine, engine, engine, engine, engine, engine, engine,
                           registry, engine, new DamageHitGroups(), messages: engine, carryLimits: limits);
    }

    [Fact]
    public void GiveAmmoFillsToThePlayersOwnLimitAndRefusesPastIt()
    {
        var arsenal = Arsenal(Over(new RecordingLog(), new RaisesPistol(player: 1, to: 200)));

        Assert.Equal(200, arsenal.GiveAmmo(One, 500, "Pistol"));
        Assert.Equal(0, arsenal.GiveAmmo(One, 1, "Pistol"));
        Assert.Equal(Pistol.MaxCarry, arsenal.GiveAmmo(Two, 500, "Pistol"));
        Assert.Equal((200, Pistol.MaxCarry), (arsenal.MaxCarryOf(One, in Pistol), arsenal.MaxCarryOf(Two, in Pistol)));
    }

    [Fact]
    public void WithNoRulesGiveAmmoFillsToTheRowsLimit()
    {
        var arsenal = Arsenal(AmmoCarry.Stock());

        Assert.Equal(Pistol.MaxCarry, arsenal.GiveAmmo(One, 500, "Pistol"));
    }

    [Fact]
    public void CsGiveFillsTheWeaponsPoolToThePlayersOwnLimit()
    {
        var engine = Mock().Returns("Entities.IsValid", true).Returns("Entities.GetIndex", 1)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Entities.GetOrigin", new Vector(0f, 0f, 0f))
            .Returns("Players.IsAlive", true)
            .Returns("Players.GetHandle", One)
            .Returns("Clock.Now", 10f);
        engine.Strict = false;

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = Services(out _);
        registry.KeyValue(One, EntityNames.Player, "targetname", "one");

        var arsenal = new Arsenal(new RecordingLog(), new WeaponSwitchTests.SpawningEntities(engine, registry),
                                  engine, engine, engine, engine, engine, engine, engine, registry, engine,
                                  new DamageHitGroups(), messages: engine,
                                  carryLimits: Over(new RecordingLog(), new RaisesPistol(player: 1, to: 200)));

        new GiveWeaponCommand(new RecordingLog(), arsenal, engine, engine).RunLine(EntityNames.WeaponPistol);

        Assert.Equal([(EntityNames.WeaponPistol, 18, true)], arsenal.Holdings(One));
        Assert.Equal(EntityNames.WeaponPistol, arsenal.ActiveWeaponName(One));
        Assert.Equal("", arsenal.ActiveWeaponName(new EntityHandle(0x0100_0999u)));
        Assert.Equal(200, arsenal.Pools(One)["Pistol"]);
    }
}
