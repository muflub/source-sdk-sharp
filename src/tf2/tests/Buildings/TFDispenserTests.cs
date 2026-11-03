using SourceSharp.Abi;
using TF2.Server.Buildings;
using TF2.Server.Medigun;
using TF2.Shared;
using TF2.Tests.Entities.Items;
using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>The level 1 field servicer: the heal, the ammo beat, the metal pool.</summary>
public class TFDispenserTests
{
    /// <summary>A standing dispenser of <paramref name="team"/>, its build just finished at t=120.</summary>
    private static (BuildingsRig Rig, TFDispenser Dispenser, EntityHandle Builder) AWorkingDispenser(TFTeam team = TFTeam.Red)
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle builder = rig.AddBuilder(team);

        var dispenser = rig.Spawn<TFDispenser>(TFDispenser.MapClass);

        dispenser.StartPlacement(builder);
        rig.Clock.Now = 100f;
        dispenser.StartBuilding();
        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Dispenser)!.BuildTime;
        dispenser.OnThink();   // the base think's last tick: constructs to full and goes active

        return (rig, dispenser, builder);
    }

    /// <summary>
    /// The one clock: the service ticked at the stock period, exactly the
    /// counted number of ticks, the clock stood exactly at the end —
    /// accumulating the period by addition drifts across the beat seconds.
    /// The patient's own tick rides along when one is named, as the medigun
    /// component pays the heal in production.
    /// </summary>
    private static void Tick(BuildingsRig rig, TFDispenser dispenser, float seconds,
                             TFPlayerHealing? healing = null, EntityHandle patient = default)
    {
        int steps = (int)(seconds / TFDispenser.DispenseThinkDelay + 0.5f);
        float start = rig.Clock.Now, end = start + seconds;
        for (int step = 1; step <= steps; step++)
        {
            rig.Clock.Now = step == steps ? end : start + step * TFDispenser.DispenseThinkDelay;
            dispenser.OnThink("DispenseThink");
            healing?.TickPatient(patient, rig.Clock.Now, TFDispenser.DispenseThinkDelay);
        }
    }

    private static (EntityHandle Player, FakeMapPlayer State) AServiced(BuildingsRig rig, TFDispenser dispenser,
                                                                        TFTeam team = TFTeam.Red, float health = 10f)
    {
        (EntityHandle handle, FakeMapPlayer state) = rig.Players.Add(team);
        state.Health = (int)health;
        rig.Healths[handle] = (int)health;      // the shell's store the accumulation reads
        rig.HealingResupply.Maxes[handle.EntryIndex] = 150;
        rig.Centers[handle] = new Vector(40f, 0f, 0f);
        rig.Centers[dispenser.Handle] = Vector.Zero;
        rig.FillsSphere(handle);
        return (handle, state);
    }

    [Fact]
    public void TheDispenserComesUpAtTheStockPool()
    {
        var (_, dispenser, _) = AWorkingDispenser();

        Assert.Equal(TFDispenser.StartMetal, dispenser.Metal);
    }

    [Fact]
    public void TheDispenserHealsItsOwnTeamAtTheStockRate()
    {
        var (rig, dispenser, _) = AWorkingDispenser();
        (EntityHandle mate, _) = AServiced(rig, dispenser);

        Tick(rig, dispenser, 1f, rig.Healing, mate);    // ten ticks at the rate: a tenth of a second pays a whole point

        Assert.Equal(10, rig.HealingResupply.Taken.GetValueOrDefault(mate.EntryIndex));
    }

    [Fact]
    public void TheDispenserIgnoresTheOtherTeam()
    {
        var (rig, dispenser, _) = AWorkingDispenser();
        (EntityHandle enemy, _) = AServiced(rig, dispenser, TFTeam.Blue);

        Tick(rig, dispenser, 1f, rig.Healing, enemy);

        Assert.Equal(0, rig.HealingResupply.Taken.GetValueOrDefault(enemy.EntryIndex));
    }

    [Fact]
    public void TheDispenserHealsOnlyWhatItCanSee()
    {
        var (rig, dispenser, _) = AWorkingDispenser();
        (EntityHandle mate, _) = AServiced(rig, dispenser);
        rig.BlockedEnds.Add(rig.Centers[mate]);   // a wall between the centre and the man

        Tick(rig, dispenser, 1f, rig.Healing, mate);

        Assert.Equal(0, rig.HealingResupply.Taken.GetValueOrDefault(mate.EntryIndex));
    }

    [Fact]
    public void APlayerStandingInTheRadiusIsHandedToTheHealingOnce()
    {
        var (rig, dispenser, _) = AWorkingDispenser();
        (EntityHandle mate, _) = AServiced(rig, dispenser);

        Tick(rig, dispenser, 1f, rig.Healing, mate);   // ten ticks, one attach

        // Stock's DispenseThink edge (tf_obj_dispenser.cpp:694-698): StartHealing
        // only for a player who is not already a healing target. The re-arm the
        // port ran every tick instead asked the shared's Heal — and its HealthBuff
        // add — on every tick.
        Assert.Equal(1, rig.HealingConditions.Calls
            .Count(c => c.StartsWith($"add {mate.EntryIndex} HealthBuff", StringComparison.Ordinal)));
    }

    [Fact]
    public void AnUpgradeStopsTheHealingSoTheNewRateAttachesFresh()
    {
        var (rig, dispenser, _) = AWorkingDispenser();
        (EntityHandle mate, _) = AServiced(rig, dispenser);
        Tick(rig, dispenser, 0.1f, rig.Healing, mate);   // attached at level 1

        rig.Engine.Returns("EntityRender.LookupSequenceByActivity", 9);   // the upgrade body's ACT_OBJ_UPGRADING
        dispenser.StartUpgrading();   // stock clears the healing list here (:328-330)

        Assert.Contains($"remove {mate.EntryIndex} HealthBuff", rig.HealingConditions.Calls);

        // Stock's DispenseThink is not gated on the upgrading state (:626-641):
        // the next tick re-attaches at the level 2 rate on its own edge.
        Tick(rig, dispenser, 0.1f, rig.Healing, mate);

        Assert.Equal(2, rig.HealingConditions.Calls
            .Count(c => c.StartsWith($"add {mate.EntryIndex} HealthBuff", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheHealStopsWhenThePlayerWalksOutOfTheRadius()
    {
        var (rig, dispenser, _) = AWorkingDispenser();
        (EntityHandle mate, _) = AServiced(rig, dispenser);
        Tick(rig, dispenser, 0.1f, rig.Healing, mate);

        rig.FillsSphere();                      // nobody in the sphere now
        Tick(rig, dispenser, 0.1f, rig.Healing, mate);

        Assert.Contains($"remove {mate.EntryIndex} HealthBuff", rig.HealingConditions.Calls);
    }

    [Fact]
    public void TheHealSightIsTheDispensersMaskNotTheBrushes()
    {
        var (rig, dispenser, _) = AWorkingDispenser();
        (EntityHandle mate, _) = AServiced(rig, dispenser);

        Tick(rig, dispenser, 0.1f);

        // MASK_DISPENSER (tf_obj_dispenser.cpp:896) — blocklos stops it, a
        // grating does not; the brushes fold has both the other way.
        EngineCall sight = Assert.Single(rig.Engine.AllOf("WorldQuery.TraceLine"));
        Assert.Equal(TraceMask.Dispenser, sight.ArgAs<TraceMask>(2));
    }

    [Fact]
    public void TheDispenserGivesAFifthOfEachAmmoOnTheBeat()
    {
        var (rig, dispenser, _) = AWorkingDispenser();
        (EntityHandle mate, FakeMapPlayer state) = AServiced(rig, dispenser, health: 200f);   // at full, no heal
        state.MaxAmmo[TFAmmo.Primary] = 100;
        state.MaxAmmo[TFAmmo.Secondary] = 100;

        Tick(rig, dispenser, 0.6f);   // the first beat, half a second after it went active

        Assert.Equal(20, state.Ammo.GetValueOrDefault(TFAmmo.Primary));
        Assert.Equal(20, state.Ammo.GetValueOrDefault(TFAmmo.Secondary));
    }

    [Fact]
    public void TheMetalGiveComesOutOfTheDispensersOwnPool()
    {
        var (rig, dispenser, _) = AWorkingDispenser();
        (EntityHandle mate, FakeMapPlayer state) = AServiced(rig, dispenser, health: 200f);
        state.MaxAmmo[TFAmmo.Metal] = 100;

        Tick(rig, dispenser, 0.6f);   // the beat gives the pool's 25, capped at the drop's 40

        Assert.Equal(25, state.Ammo.GetValueOrDefault(TFAmmo.Metal));
        Assert.Equal(0, dispenser.Metal);
    }

    [Fact]
    public void TheMetalPoolRefillsTwentyAtATimeOnItsClock()
    {
        var (rig, dispenser, _) = AWorkingDispenser();
        rig.Centers[dispenser.Handle] = Vector.Zero;   // nothing else near: the pool refills undisturbed
        rig.FillsSphere();

        Tick(rig, dispenser, 3f);   // the first refill, three seconds after it stood

        // Stock's auto-refill is half tfc's amount, twice as often
        // (tf_obj_dispenser.cpp:606-613): 400 × 0.1 at level 1.
        Assert.Equal(TFDispenser.StartMetal + TFDispenser.RefillMetalForLevel(1), dispenser.Metal);
    }
}
