/*
 * CORE: SPENDING AMMO. Arsenal.TakeAmmo is stock's
 * CBaseCombatCharacter::RemoveAmmo (basecombatcharacter_shared.cpp) —
 * "MAX( m_iAmmo[iAmmoIndex] - iCount, 0 )", a request past the balance
 * spends the whole pool and leaves it empty — and the door the engineer's
 * building costs are paid through. The give side is held by
 * AmmoCarryTests; the dead player here is the arm that has no stock twin on
 * the give side (GiveAmmo refuses a corpse, RemoveAmmo never looked).
 */

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

public class TakeAmmoTests
{
    private static readonly EntityHandle One = new(0x0001_0001u);

    /// <summary>One arsenal over the mock engine and its log; the fact flips <c>Players.IsAlive</c> on the engine.</summary>
    private static (Arsenal Arsenal, RecordingEngine Engine, RecordingLog Log) Rig()
    {
        var engine = Mock().Returns("Entities.IsValid", true).Returns("Entities.GetIndex", 1)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Players.IsAlive", true);
        engine.Strict = false;

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = Services(out _);
        registry.KeyValue(One, EntityNames.Player, "targetname", "one");

        var log = new RecordingLog();
        var arsenal = new Arsenal(log, engine, engine, engine, engine, engine, engine, engine,
                                  engine, registry, engine, new DamageHitGroups(), messages: engine,
                                  carryLimits: AmmoCarry.Stock());
        return (arsenal, engine, log);
    }

    [Fact]
    public void TakeAmmoSubtractsFromThePool()
    {
        var (arsenal, _, _) = Rig();
        arsenal.GiveAmmo(One, 100, "Pistol");

        Assert.Equal(30, arsenal.TakeAmmo(One, 30, "Pistol"));
        Assert.Equal(70, arsenal.Pools(One)["Pistol"]);
    }

    [Fact]
    public void TakeAmmoPastTheBalanceSpendsTheWholePoolAndLeavesItEmpty()
    {
        var (arsenal, _, _) = Rig();
        arsenal.GiveAmmo(One, 10, "Pistol");

        Assert.Equal(10, arsenal.TakeAmmo(One, 100, "Pistol"));
        Assert.Equal(0, arsenal.Pools(One)["Pistol"]);
    }

    [Fact]
    public void TakeAmmoFromAnEmptyPoolSpendsNothing()
    {
        var (arsenal, _, _) = Rig();

        Assert.Equal(0, arsenal.TakeAmmo(One, 10, "Pistol"));
        Assert.False(arsenal.Pools(One).TryGetValue("Pistol", out int have) && have != 0);
    }

    [Fact]
    public void TakeAmmoOfNothingAskedSpendsNothing()
    {
        var (arsenal, _, _) = Rig();
        arsenal.GiveAmmo(One, 100, "Pistol");

        Assert.Equal(0, arsenal.TakeAmmo(One, 0, "Pistol"));
        Assert.Equal(0, arsenal.TakeAmmo(One, -5, "Pistol"));
        Assert.Equal(100, arsenal.Pools(One)["Pistol"]);
    }

    [Fact]
    public void TakeAmmoOfAnUnknownRowSpendsNothingAndNamesIt()
    {
        var (arsenal, _, log) = Rig();
        arsenal.GiveAmmo(One, 100, "Pistol");

        Assert.Equal(0, arsenal.TakeAmmo(One, 10, "NoSuchAmmo"));
        Assert.Equal(100, arsenal.Pools(One)["Pistol"]);
        Assert.Single(log.Lines, l => l.Warning && l.Text.Contains("NoSuchAmmo"));
    }

    [Fact]
    public void TakeAmmoSpendsFromTheDeadAsStocksRemoveAmmoDoes()
    {
        var (arsenal, engine, _) = Rig();
        arsenal.GiveAmmo(One, 100, "Pistol");

        engine.Returns("Players.IsAlive", false);

        // The control: the corpse refuses a GIVE — the spend not refusing it
        // only means something while the give still does.
        Assert.Equal(0, arsenal.GiveAmmo(One, 10, "Pistol"));
        Assert.Equal(10, arsenal.TakeAmmo(One, 10, "Pistol"));
        Assert.Equal(90, arsenal.Pools(One)["Pistol"]);
    }
}
