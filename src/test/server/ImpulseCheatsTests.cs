using System;
using System.Collections.Generic;
using System.Linq;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;
using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The impulse cheats (ImpulseCheats.cs) — stock's CheatImpulseCommands, ported
/// managed after the -player flip evicted player.cpp and impulse 101 with it.
///
/// What can regress: the sv_cheats gate (both directions), the give-everything
/// inventory being THIS mod's registry rather than a hand-kept list, the heal
/// rule, and the crosshair impulses' refusals.
/// </summary>
public class ImpulseCheatsTests
{
    /// <summary>Records what the cheats asked the arsenal for.</summary>
    private sealed class RecordingGiver : IWeaponGiver
    {
        public readonly List<string> Weapons = new();
        public readonly List<(string Ammo, int Count)> Ammo = new();

        /// <summary>Both kinds in arrival order, for asserting ammo-then-weapons.</summary>
        public readonly List<string> Sequence = new();

        public bool GiveWeapon(EntityHandle player, string className)
        {
            Weapons.Add(className);
            Sequence.Add("weapon:" + className);
            return true;
        }

        public int GiveAmmo(EntityHandle player, int count, string ammoName)
        {
            Ammo.Add((ammoName, count));
            Sequence.Add("ammo:" + ammoName);
            return count;
        }
    }

    private static readonly EntityHandle Player = new(3u);

    private static (ImpulseCheats Cheats, RecordingGiver Giver, RecordingEngine Engine)
        Rig(bool cheats = true, bool alive = true)
    {
        var engine = Fake();
        engine.Returns("Players.IsAlive", alive);

        var giver = new RecordingGiver();
        var impulses = new ImpulseCheats(new RecordingLog(), giver, engine, engine, engine, engine)
        {
            CheatsEnabled = () => cheats,
        };

        return (impulses, giver, engine);
    }

    //-------------------------------------------------------------------------
    // The gate.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheGateReadsTheConVarsValueAndFailsClosed()
    {
        Assert.True(!ImpulseCheats.CheatsOn(null), "no engine, or no such convar, means cheats are OFF");
        Assert.True(!ImpulseCheats.CheatsOn(""), "an empty value means cheats are off");
        Assert.True(!ImpulseCheats.CheatsOn("0"), "sv_cheats 0 means cheats are off");
        Assert.True(!ImpulseCheats.CheatsOn("nonsense"), "a value the gate cannot read keeps it shut");
        Assert.True(ImpulseCheats.CheatsOn("1"), "sv_cheats 1 means cheats are on");
        Assert.True(ImpulseCheats.CheatsOn("2"), "any nonzero integer means cheats are on, as GetBool reads it");
    }

    [Fact]
    public void NothingIsGivenWithCheatsOff()
    {
        var (cheats, giver, engine) = Rig(cheats: false);

        Assert.True(!cheats.Run(Player, 101), "impulse 101 is refused at sv_cheats 0");
        Assert.True(giver.Weapons.Count == 0 && giver.Ammo.Count == 0,
            "a refused impulse gives nothing at all");
        Assert.True(engine.Calls.Count == 0,
            "a refused impulse never touches the engine either");
    }

    [Fact]
    public void ImpulseZeroNeverEvenConsultsTheGate()
    {
        var (cheats, _, _) = Rig();
        bool asked = false;
        cheats.CheatsEnabled = () => { asked = true; return true; };

        Assert.True(!cheats.Run(Player, 0), "a command with no impulse is not an impulse");
        Assert.True(!asked, "the zero case returns before the gate — most commands carry no impulse");
    }

    [Fact]
    public void ADeadPlayerGetsNothing()
    {
        var (cheats, giver, _) = Rig(alive: false);

        Assert.True(!cheats.Run(Player, 101), "a dead player's impulse does nothing — 101 is a resupply, not a revive");
        Assert.True(giver.Weapons.Count == 0 && giver.Ammo.Count == 0,
            "a corpse is given nothing");
    }

    [Fact]
    public void AnUnknownImpulseIsStocksSilence()
    {
        var (cheats, giver, engine) = Rig();

        // 82 is stock's create-a-jeep, deliberately dropped with the vehicles.
        Assert.True(!cheats.Run(Player, 82), "an impulse this build has no answer to is silently unhandled");
        Assert.True(giver.Weapons.Count == 0 && giver.Ammo.Count == 0
            && engine.CountOf("Entities.Remove") == 0,
            "the unknown impulse changed nothing");
    }

    //-------------------------------------------------------------------------
    // 101: everything this mod actually has.
    //-------------------------------------------------------------------------

    [Fact]
    public void Impulse101GivesEveryRegisteredWeaponNotAHandKeptList()
    {
        var (cheats, giver, engine) = Rig();
        engine.Returns("Entities.GetHealth", 100);

        Assert.True(cheats.Run(Player, 101), "impulse 101 is handled with cheats on");
        Assert.True(giver.Weapons.SequenceEqual(WeaponTypes.All.Select(w => w.Name)),
            "101 gives exactly the weapon registry, in registry order — a weapon added to WeaponTypes.All is in 101 for free");
    }

    [Fact]
    public void Impulse101FillsEveryAmmoPoolToItsCarryLimit()
    {
        var (cheats, giver, engine) = Rig();
        engine.Returns("Entities.GetHealth", 100);

        cheats.Run(Player, 101);

        Assert.True(giver.Ammo.SequenceEqual(AmmoTypes.All.Select(a => (a.Name, a.MaxCarry))),
            "101 asks for every pool's carry limit — GiveAmmo clamps, so the limit IS stock's effective 255");
        Assert.True(giver.Sequence.Take(AmmoTypes.All.Length).All(s => s.StartsWith("ammo:")),
            "every pool is filled BEFORE any weapon arrives, so each weapon lands on full reserves");
    }

    [Fact]
    public void Impulse101HealsAQuarterOnlyWhenHurt()
    {
        var (cheats, _, engine) = Rig();
        engine.Returns("Entities.GetHealth", 50);

        cheats.Run(Player, 101);

        Assert.True(engine.Last("Entities.SetHealth") is EngineCall heal && (int)heal.Args[1]! == 75,
            "stock's TakeHealth(25): fifty health becomes seventy-five");

        engine.Clear();
        engine.Returns("Entities.GetHealth", 100);
        cheats.Run(Player, 101);

        Assert.True(engine.CountOf("Entities.SetHealth") == 0,
            "a player already at full health is left alone, as stock's < 100 check left them");
    }

    [Fact]
    public void TheHealRuleIsStocksQuarterCappedAtFull()
    {
        Assert.True(ImpulseCheats.HealedTo(50) == 75, "a quarter back when hurt");
        Assert.True(ImpulseCheats.HealedTo(99) == 100, "capped at the spawn maximum");
        Assert.True(ImpulseCheats.HealedTo(80) == 100, "eighty plus twenty-five caps at one hundred");
        Assert.True(ImpulseCheats.HealedTo(100) == 100, "full health is left alone");
        Assert.True(ImpulseCheats.HealedTo(120) == 120, "over-max health is not clawed back");
        Assert.True(ImpulseCheats.HealedTo(0) == 0, "dead stays dead — 101 is not a revive");
    }

    //-------------------------------------------------------------------------
    // The crosshair impulses.
    //-------------------------------------------------------------------------

    private static RecordingEngine Aiming(RecordingEngine engine, EntityHandle hit, string className)
    {
        engine.Returns("WorldQuery.TracePlayerAim", hit)
              .Ignores("WorldQuery.TracePlayerAim.hitPoint");
        engine.Returns("Entities.GetClassName", className);
        return engine;
    }

    [Fact]
    public void Impulse203RemovesTheAimedEntity()
    {
        var (cheats, _, engine) = Rig();
        var prop = new EntityHandle(9u);
        Aiming(engine, prop, "prop_physics");

        Assert.True(cheats.Run(Player, 203), "impulse 203 is handled with cheats on");

        Assert.True(engine.Last("Entities.Remove") is EngineCall removed
            && (EntityHandle)removed.Args[0]! == prop,
            "the entity under the crosshair is the one removed");
    }

    [Fact]
    public void Impulse203RefusesTheWorldPlayersAndAMiss()
    {
        var (cheats, _, world) = Rig();
        Aiming(world, new EntityHandle(0u), "worldspawn");
        cheats.Run(Player, 203);
        Assert.True(world.CountOf("Entities.Remove") == 0, "the world cannot be removed");

        var (cheats2, _, other) = Rig();
        Aiming(other, new EntityHandle(4u), EntityNames.Player);
        cheats2.Run(Player, 203);
        Assert.True(other.CountOf("Entities.Remove") == 0, "a player cannot be removed — stock's crash, not kept");

        var (cheats3, _, miss) = Rig();
        Aiming(miss, EntityHandle.None, "unasked");
        cheats3.Run(Player, 203);
        Assert.True(miss.CountOf("Entities.Remove") == 0
            && miss.CountOf("Entities.GetClassName") == 0,
            "aiming at nothing removes nothing and asks nothing about it");
    }

    [Fact]
    public void Impulse106IdentifiesWithoutTouching()
    {
        var (cheats, giver, engine) = Rig();
        Aiming(engine, new EntityHandle(9u), "func_button");
        engine.Returns("Entities.GetName", "exit_button");

        Assert.True(cheats.Run(Player, 106), "impulse 106 is handled with cheats on");
        Assert.True(engine.Called("Entities.GetClassName") && engine.Called("Entities.GetName"),
            "106 reads what it prints: classname and targetname");
        Assert.True(engine.CountOf("Entities.Remove") == 0 && giver.Weapons.Count == 0,
            "identifying an entity changes nothing");
    }
}
