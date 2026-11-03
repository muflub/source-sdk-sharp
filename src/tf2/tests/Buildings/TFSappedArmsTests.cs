using SourceSharp.Abi;
using TF2.Server.Buildings;
using TF2.Server.Medigun;
using TF2.Shared;
using TF2.Tests.Entities.Items;
using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>
/// The sapped arms (the sapper hooks of tf_obj_sentrygun.cpp,
/// tf_obj_dispenser.cpp and tf_obj_teleporter.cpp): while the sapper rides,
/// the building stops working, and takes its work up again when it is gone.
/// </summary>
public class TFSappedArmsTests
{
    /// <summary>The blue spy's sapper planted on <paramref name="building"/>, finished and draining.</summary>
    private static TFSapper Sapped(BuildingsRig rig, TFBaseObject building)
    {
        EntityHandle spy = rig.AddBuilder(TFTeam.Blue, TFClass.Spy);
        var sapper = rig.Spawn<TFSapper>(TFSapper.MapClass);
        sapper.StartPlacement(spy);
        sapper.AttachTo(building);
        sapper.StartBuilding();
        sapper.OnThink();   // the instant build: attached
        return sapper;
    }

    //---------------------------------------------------------------------
    // The turret
    //---------------------------------------------------------------------

    private static (BuildingsRig Rig, TFSentryGun Sentry, EntityHandle Enemy) AWorkingSentryWithAnEnemy()
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle builder = rig.AddBuilder(TFTeam.Red);
        var sentry = rig.Spawn<TFSentryGun>(TFSentryGun.MapClass);
        sentry.StartPlacement(builder);
        rig.Clock.Now = 100f;
        sentry.StartBuilding();
        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.BuildTime;
        sentry.OnThink();

        EntityHandle enemy = rig.AddBuilder(TFTeam.Blue);
        rig.Centers[enemy] = new Vector(300f, 0f, TFSentryGun.EyeHeightForLevel(1));
        rig.FillsSphere(enemy);
        sentry.OnThink("SentryThink");   // the find
        return (rig, sentry, enemy);
    }

    [Fact]
    public void TheSappedSentryLetsNoEnemyIn()
    {
        var (rig, sentry, _) = AWorkingSentryWithAnEnemy();
        Assert.True(sentry.IsAttacking);

        Sapped(rig, sentry);
        sentry.OnThink("SentryThink");

        Assert.False(sentry.IsAttacking);
        Assert.Equal(EntityHandle.None, sentry.Target);
        Assert.Empty(rig.Engine.AllOf("Damage.TakeDamage"));
    }

    [Fact]
    public void TheSappedSentryEasesItsBarrelDown()
    {
        var (rig, sentry, _) = AWorkingSentryWithAnEnemy();

        Sapped(rig, sentry);
        for (int i = 0; i < 40; i++)
        {
            rig.Clock.Now += TFSentryGun.SentryThinkDelay;
            sentry.OnThink("SentryThink");
        }

        // OnStartDisabled pins the goal where the turret stands
        // (tf_obj_sentrygun.cpp:1770-1779) and the disabled arm of
        // SentryRotate eases it to 30 down on arrival (:1725-1729) — the
        // pitch is stock's, down-positive: DisabledPitch.
        Assert.Equal(TFSentryGun.DisabledPitch, sentry.Pitch);
    }

    [Fact]
    public void TheSentryTakesUpTheFightAgainWhenTheSapperIsGone()
    {
        var (rig, sentry, _) = AWorkingSentryWithAnEnemy();
        TFSapper sapper = Sapped(rig, sentry);
        sentry.OnThink("SentryThink");
        Assert.False(sentry.IsAttacking);

        sapper.DestroyObject();
        sentry.OnThink("SentryThink");   // the find again, a find's delay off the first shell
        rig.Clock.Now += 0.3f;
        sentry.OnThink("SentryThink");   // aimed, and the beat has run: the shells fire

        Assert.True(sentry.IsAttacking);
        Assert.NotEmpty(rig.Engine.AllOf("Damage.TakeDamage"));
    }

    //---------------------------------------------------------------------
    // The dispenser
    //---------------------------------------------------------------------

    /// <summary>The service ticked at the stock period, the clock landed exactly at the end.
    /// The patient's own tick rides along when one is named, as the medigun component pays the heal.</summary>
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

    /// <summary>A working dispenser with a wounded friendly standing in its radius.</summary>
    private static (BuildingsRig Rig, TFDispenser Dispenser, FakeMapPlayer State, EntityHandle Mate) AWorkingDispenserWithAMate()
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle builder = rig.AddBuilder(TFTeam.Red);
        var dispenser = rig.Spawn<TFDispenser>(TFDispenser.MapClass);
        dispenser.StartPlacement(builder);
        rig.Clock.Now = 100f;
        dispenser.StartBuilding();
        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Dispenser)!.BuildTime;
        dispenser.OnThink();

        (EntityHandle mate, FakeMapPlayer state) = rig.Players.Add(TFTeam.Red);
        state.Health = 10;
        rig.Healths[mate] = 10;                 // the shell's store the accumulation reads
        rig.HealingResupply.Maxes[mate.EntryIndex] = 150;
        rig.Centers[mate] = new Vector(40f, 0f, 0f);
        rig.Centers[dispenser.Handle] = Vector.Zero;
        rig.FillsSphere(mate);
        return (rig, dispenser, state, mate);
    }

    [Fact]
    public void TheSappedDispenserHealsNothingAndRefillsNoMetal()
    {
        var (rig, dispenser, state, mate) = AWorkingDispenserWithAMate();

        Sapped(rig, dispenser);
        int metalBefore = dispenser.Metal;

        Tick(rig, dispenser, 1f, rig.Healing, mate);

        Assert.Equal(0, rig.HealingResupply.Taken.GetValueOrDefault(mate.EntryIndex));
        Assert.Equal(metalBefore, dispenser.Metal);
    }

    [Fact]
    public void TheDispenserServesAgainWhenTheSapperIsGone()
    {
        var (rig, dispenser, state, mate) = AWorkingDispenserWithAMate();

        TFSapper sapper = Sapped(rig, dispenser);
        sapper.DestroyObject();

        Tick(rig, dispenser, 1f, rig.Healing, mate);

        // a tenth of a second pays a whole point, ten over the second — paid
        // through the shared's accumulation, not the player's TakeHealth
        Assert.Equal(10, rig.HealingResupply.Taken.GetValueOrDefault(mate.EntryIndex));
    }

    //---------------------------------------------------------------------
    // The pair
    //---------------------------------------------------------------------

    [Fact]
    public void TheSappedExitStandsItsEntranceDown()
    {
        var (rig, entrance, exit, _) = APair();
        TickBoth(rig, entrance, exit, 1f);
        Assert.Equal(TFTeleporterState.Ready, entrance.State);

        EntityHandle rider = AStandingRider(rig);
        entrance.OnTouch(rider);
        Assert.Equal(rider, entrance.Reserved);

        Sapped(rig, exit);   // the exit takes the sapper, not the entrance
        TickBoth(rig, entrance, exit, 0.5f);

        // Stock's TeleporterThink refuses while the partner is disabled too
        // (tf_obj_teleporter.cpp:1051-1060, with IsMatchingTeleporterReady's
        // !IsDisabled at :735-749): the entrance stands down to idle, and
        // the booked rider is never sent.
        Assert.Equal(TFTeleporterState.Idle, entrance.State);
        Assert.Empty(rig.Engine.AllOf("Entities.Teleport"));
    }

    [Fact]
    public void TheSappedEntranceStandsDownAndServesNobody()
    {
        var (rig, entrance, exit, _) = APair();
        TickBoth(rig, entrance, exit, 1f);
        Assert.Equal(TFTeleporterState.Ready, entrance.State);

        TFSapper sapper = Sapped(rig, entrance);

        entrance.OnThink("TeleporterThink");
        Assert.Equal(TFTeleporterState.Idle, entrance.State);

        EntityHandle rider = AStandingRider(rig);
        entrance.OnTouch(rider);
        Assert.Equal(EntityHandle.None, entrance.Reserved);
    }

    [Fact]
    public void TheEntranceTakesRidersAgainWhenTheSapperIsGone()
    {
        var (rig, entrance, exit, _) = APair();
        TickBoth(rig, entrance, exit, 1f);
        TFSapper sapper = Sapped(rig, entrance);
        entrance.OnThink("TeleporterThink");
        Assert.Equal(TFTeleporterState.Idle, entrance.State);

        sapper.DestroyObject();
        TickBoth(rig, entrance, exit, 0.2f);

        EntityHandle rider = AStandingRider(rig);
        entrance.OnTouch(rider);
        Assert.Equal(rider, entrance.Reserved);
    }

    /// <summary>One engineer's entrance and exit, both built and thinking at t=120.</summary>
    private static (BuildingsRig Rig, TFTeleporter Entrance, TFTeleporter Exit, EntityHandle Builder) APair()
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle builder = rig.AddBuilder();
        var entrance = rig.Spawn<TFTeleporter>(TFTeleporter.MapClass);
        var exit = rig.Spawn<TFTeleporter>(TFTeleporter.MapClass);

        foreach (var (end, mode) in new[] { (entrance, 0), (exit, 1) })
        {
            end.StartPlacement(builder);
            end.SetObjectMode(mode);
            end.StartBuilding();
        }

        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Teleporter)!.BuildTime;
        entrance.OnThink();   // the base think's last tick for each end
        exit.OnThink();

        return (rig, entrance, exit, builder);
    }

    /// <summary>The pair thinks together at its own fast beat, the clock landed exactly at the end.</summary>
    private static void TickBoth(BuildingsRig rig, TFTeleporter entrance, TFTeleporter exit, float seconds)
    {
        int steps = (int)(seconds / TFTeleporter.TeleporterThinkDelay + 0.5f);
        float start = rig.Clock.Now, end = start + seconds;
        for (int step = 1; step <= steps; step++)
        {
            rig.Clock.Now = step == steps ? end : start + step * TFTeleporter.TeleporterThinkDelay;
            entrance.OnThink("TeleporterThink");
            exit.OnThink("TeleporterThink");
        }
    }

    private static EntityHandle AStandingRider(BuildingsRig rig)
    {
        (EntityHandle handle, _) = rig.Players.Add();
        rig.Engine.Returns("Entities.GetVelocity", Vector.Zero);
        return handle;
    }
}
