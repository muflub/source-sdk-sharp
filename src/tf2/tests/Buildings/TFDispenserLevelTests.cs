using SourceSharp.Abi;
using TF2.Server.Buildings;
using TF2.Shared;
using TF2.Tests.Entities.Items;
using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>
/// The servicer above level 1: the heal and ammo tables climb
/// (g_flDispenserHealRates, g_flDispenserAmmoRates,
/// tf_obj_dispenser.cpp:92-106) and the metal give and self-refill with them
/// (:584, :606-613).
/// </summary>
public class TFDispenserLevelTests
{
    private static string MetalName => TFAmmoNames.Name(TFAmmo.Metal);

    /// <summary>A standing dispenser raised to <paramref name="level"/>, its build just finished at t=100.</summary>
    private static (BuildingsRig Rig, TFDispenser Dispenser, EntityHandle Builder) ADispenserAtLevel(int level)
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle builder = rig.AddBuilder();
        var dispenser = rig.Spawn<TFDispenser>(TFDispenser.MapClass);

        dispenser.StartPlacement(builder);
        rig.Clock.Now = 100f;
        dispenser.StartBuilding();
        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Dispenser)!.BuildTime;
        dispenser.OnThink();

        float duration = TFObjectInfo.Of(TFObjectType.Dispenser)!.UpgradeDuration;
        rig.Purse.Pockets[MetalName] = 200 * level;
        for (int l = 2; l <= level; l++)
        {
            for (int swing = 0; swing < 8; swing++)   // 200 metal at 25 the swing
                dispenser.WrenchHit(builder);
            rig.Clock.Now += duration;
            dispenser.OnThink();
        }

        return (rig, dispenser, builder);
    }

    /// <summary>The one clock: the service ticked at the stock period, the clock stood exactly at the end.
    /// The heal rides the shared's accumulation, so the patient's own tick pays it out.</summary>
    private static void Tick(BuildingsRig rig, TFDispenser dispenser, float seconds, EntityHandle patient = default)
    {
        int steps = (int)(seconds / TFDispenser.DispenseThinkDelay + 0.5f);
        float start = rig.Clock.Now, end = start + seconds;
        for (int step = 1; step <= steps; step++)
        {
            rig.Clock.Now = step == steps ? end : start + step * TFDispenser.DispenseThinkDelay;
            dispenser.OnThink("DispenseThink");
            if (patient != EntityHandle.None)
                rig.Healing.TickPatient(patient, rig.Clock.Now, TFDispenser.DispenseThinkDelay);
        }
    }

    private static (EntityHandle Player, FakeMapPlayer State) AServiced(BuildingsRig rig, TFDispenser dispenser,
                                                                        float health = 10f)
    {
        (EntityHandle handle, FakeMapPlayer state) = rig.Players.Add(TFTeam.Red);
        state.Health = (int)health;
        rig.Healths[handle] = (int)health;      // the shell's store the accumulation reads
        rig.HealingResupply.Maxes[handle.EntryIndex] = 150;
        rig.Centers[handle] = new Vector(40f, 0f, 0f);
        rig.Centers[dispenser.Handle] = Vector.Zero;
        rig.FillsSphere(handle);
        return (handle, state);
    }

    [Fact]
    public void TheLevelTwoDispenserHealsFifteenASecond()
    {
        var (rig, dispenser, _) = ADispenserAtLevel(2);
        (EntityHandle mate, FakeMapPlayer state) = AServiced(rig, dispenser);

        Tick(rig, dispenser, 1f, mate);

        // The heal rides the shared's accumulation: the payout is what the
        // patient's tick took (TFDispenserTests' lens), not the fake player's
        // own health, which the rig's resupply does not write.
        Assert.Equal(15, rig.HealingResupply.Taken.GetValueOrDefault(mate.EntryIndex));
    }

    [Fact]
    public void TheLevelThreeDispenserHealsTwentyASecond()
    {
        var (rig, dispenser, _) = ADispenserAtLevel(3);
        (EntityHandle mate, FakeMapPlayer state) = AServiced(rig, dispenser);

        Tick(rig, dispenser, 1f, mate);

        // The shared's payout lens, as the level-1 facts read it.
        Assert.Equal(20, rig.HealingResupply.Taken.GetValueOrDefault(mate.EntryIndex));
    }

    [Fact]
    public void TheLevelTwoBeatGivesThreeTenthsOfEachAmmo()
    {
        var (rig, dispenser, _) = ADispenserAtLevel(2);
        (EntityHandle mate, FakeMapPlayer state) = AServiced(rig, dispenser, health: 200f);   // at full, no heal
        state.MaxAmmo[TFAmmo.Primary] = 100;
        state.MaxAmmo[TFAmmo.Secondary] = 100;

        Tick(rig, dispenser, 0.6f);   // the first beat

        Assert.Equal(30, state.Ammo.GetValueOrDefault(TFAmmo.Primary));
        Assert.Equal(30, state.Ammo.GetValueOrDefault(TFAmmo.Secondary));
    }

    [Fact]
    public void TheLevelThreeBeatGivesTwoFifthsOfEachAmmo()
    {
        var (rig, dispenser, _) = ADispenserAtLevel(3);
        (EntityHandle mate, FakeMapPlayer state) = AServiced(rig, dispenser, health: 200f);
        state.MaxAmmo[TFAmmo.Primary] = 100;
        state.MaxAmmo[TFAmmo.Secondary] = 100;

        Tick(rig, dispenser, 0.6f);

        Assert.Equal(40, state.Ammo.GetValueOrDefault(TFAmmo.Primary));
        Assert.Equal(40, state.Ammo.GetValueOrDefault(TFAmmo.Secondary));
    }

    [Fact]
    public void TheLevelThreePoolRefillsFortyAtATime()
    {
        var (rig, dispenser, _) = ADispenserAtLevel(3);
        rig.FillsSphere();   // nobody near: the pool refills undisturbed

        dispenser.OnThink("DispenseThink");   // the clock ran past the refill's beat during the upgrade

        Assert.Equal(TFDispenser.StartMetal + TFDispenser.RefillMetalForLevel(3), dispenser.Metal);
    }

    [Fact]
    public void TheLevelThreeGivePoursSixtyMetal()
    {
        var (rig, dispenser, _) = ADispenserAtLevel(3);
        (EntityHandle mate, FakeMapPlayer state) = AServiced(rig, dispenser, health: 200f);
        state.MaxAmmo[TFAmmo.Metal] = 200;

        Tick(rig, dispenser, 0.6f);   // the tick refills the pool first, then the beat pours

        // The refill lands first (RefillThink before the beat in
        // DispenseThink), then the give pays out to the level's drop
        // (DISPENSER_DROP_METAL + ((level−1) × 10), tf_obj_dispenser.cpp:584).
        Assert.Equal(60, state.Ammo.GetValueOrDefault(TFAmmo.Metal));
        Assert.Equal(TFDispenser.StartMetal + TFDispenser.RefillMetalForLevel(3) - 60, dispenser.Metal);
    }
}
