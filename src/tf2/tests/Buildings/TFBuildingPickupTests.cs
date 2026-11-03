using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Buildings;
using TF2.Server.Entities.Items;
using TF2.Server.Player;

using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>
/// The engineer's +attack2 lift (<see cref="TFBuildingPickup"/>) over a
/// built sentry: the reach, the refusals stock names, the carried state's
/// quiet, the builder the lift arms, and the redeploy the trigger places.
/// </summary>
public class TFBuildingPickupTests
{
    /// <summary>The lift's sight, on any field: the eye at (0, 0, 64) looking +X.</summary>
    private static void ASight(BuildingsRig rig)
        => rig.Engine.Returns("Entities.GetEyePosition", new Vector(0f, 0f, 64f))
                     .Returns("Entities.GetEyeAngles", Vector.Zero)   // looking +X
                     .Returns("Entities.SetParent", true);            // the lift's mount

    /// <summary>
    /// A funded engineer who built a sentry at (48, 0, 0) and let it finish.
    /// <paramref name="standing"/> false leaves the sentry under construction.
    /// </summary>
    private static (BuildingsRig Rig, EntityHandle Engineer, TFSentryGun Sentry) AField(bool standing = true)
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle engineer = rig.AddBuilder();
        rig.Purse.Pockets[TFAmmoNames.Name(TFAmmo.Metal)] = 500;

        Assert.True(rig.Builder().Build(engineer, TFObjectType.Sentrygun, 0, new Vector(48f, 0f, 0f), Vector.Zero)
                    .Result == TFCanBuildResult.CanBuild);
        var sentry = (TFSentryGun)Assert.Single(rig.Buildings().OwnedBy(engineer, TFObjectType.Sentrygun));

        if (standing)
        {
            rig.Clock.Now += 100f;      // past the sentry's 10 s build
            sentry.OnThink();
        }

        ASight(rig);
        rig.Engine.Returns("Players.GetHandle", engineer);   // the armed builder's ask
        rig.Origins[sentry.Handle] = new Vector(48f, 0f, 0f);
        rig.Centers[sentry.Handle] = new Vector(48f, 0f, 24f);
        return (rig, engineer, sentry);
    }

    [Fact]
    public void TheLiftTakesTheSentryFacingHim()
    {
        var (rig, engineer, sentry) = AField();

        Assert.True(rig.Pickup().TryPickup(engineer));

        Assert.True(sentry.IsCarried);
        Assert.Same(sentry, rig.Pickup().Carried(engineer));
    }

    [Fact]
    public void TheLiftArmsTheBuilderWithTheBuildingInItsArms()
    {
        var (rig, engineer, sentry) = AField();

        Assert.True(rig.Pickup().TryPickup(engineer));

        Assert.True(sentry.IsCarried);
        Assert.Contains($"switch {engineer} tf_weapon_builder", rig.BuilderArms.Asked);
    }

    [Fact]
    public void TheCarriedSentryLeavesTheWorld()
    {
        var (rig, engineer, sentry) = AField();

        Assert.True(rig.Pickup().TryPickup(engineer));

        Assert.True(sentry.NextThinkDelay() < 0f,
                    $"a carried building kept a think booking ({sentry.NextThinkDelay()} s)");
        Assert.True(sentry.NextThinkDelay("SentryThink") < 0f,
                    "a carried sentry kept its own think booking");
    }

    [Fact]
    public void TheLiftRemembersTheWounds()
    {
        var (rig, engineer, sentry) = AField();
        var enemy = rig.AddBuilder(TFTeam.Blue);
        Assert.Equal(50, BuildingsRig.Hit(sentry, enemy, 50f, DamageType.Bullet));

        Assert.True(rig.Pickup().TryPickup(engineer));

        Assert.Equal(100, sentry.HealthOnPickup);
    }

    [Fact]
    public void TheArmsHoldOneBuildingAtATime()
    {
        var (rig, engineer, sentry) = AField();
        var pickup = rig.Pickup();

        Assert.True(pickup.TryPickup(engineer));
        Assert.False(pickup.TryPickup(engineer));

        Assert.True(sentry.IsCarried);
    }

    [Fact]
    public void TheTriggerPutsTheCarriedSentryBackOnItsFeet()
    {
        var (rig, engineer, sentry) = AField();
        EntityHandle handle = sentry.Handle;
        rig.Engine.Returns("Players.GetHandle", engineer)
                  .Returns("Entities.GetEyeAngles", Vector.Zero)
                  .Returns("Entities.GetEyePosition", new Vector(0f, 0f, 64f));
        rig.Centers[engineer] = new Vector(0f, 0f, 41f);
        var placement = rig.Placement();

        Assert.True(rig.Pickup(builder: placement).TryPickup(engineer));
        Assert.True(sentry.IsCarried);

        placement.StartPlacement(3);        // the deploy's carried branch
        Assert.True(sentry.IsPlacing);      // back out of the arms
        Assert.True(placement.Place(3));    // the trigger redeploys

        Assert.False(sentry.IsCarried);
        Assert.Equal(handle, sentry.Handle);   // the same entity: no second spawn
    }

    [Fact]
    public void TheRedeployPaysNoMetal()
    {
        var (rig, engineer, sentry) = AField();
        Assert.True(rig.Pickup().TryPickup(engineer));
        int paidSoFar = rig.Purse.Paid.Count;

        sentry.StartRedeploy(new Vector(48f, 0f, 0f), Vector.Zero, 3f);

        Assert.Equal(paidSoFar, rig.Purse.Paid.Count);
    }

    [Fact]
    public void TheRedeployKeepsTheWoundsAndTheLevel()
    {
        var (rig, engineer, sentry) = AField();
        var enemy = rig.AddBuilder(TFTeam.Blue);
        Assert.Equal(50, BuildingsRig.Hit(sentry, enemy, 50f, DamageType.Bullet));
        Assert.True(rig.Pickup().TryPickup(engineer));

        sentry.StartRedeploy(new Vector(48f, 0f, 0f), Vector.Zero, 3f);

        Assert.Equal(100, sentry.Health);
        Assert.Equal(1, sentry.Level);
    }

    [Fact]
    public void TheRedeployBuildsAtTheCarrySpeed()
    {
        var (rig, engineer, sentry) = AField();
        Assert.True(rig.Pickup().TryPickup(engineer));

        sentry.StartRedeploy(new Vector(48f, 0f, 0f), Vector.Zero, 3f);
        rig.Clock.Now += 3.4f;          // the sentry's 10 s at the ×3 carry rate
        sentry.OnThink();

        Assert.True(sentry.IsBuilt);
    }

    [Fact]
    public void WhileCarryingAWrongTypeBuildIsRefused()
    {
        var (rig, engineer, sentry) = AField();
        var command = rig.BuildCommand();
        Assert.True(rig.Pickup().TryPickup(engineer));

        Assert.True(command.Handle(engineer, "build", ["0", "0"]));

        Assert.Single(rig.Spawner.Placed);   // the sentry, and no dispenser came out
        Assert.Same(sentry, rig.Pickup().Carried(engineer));
        Assert.Contains(rig.Log.Lines, line => line.Text.Contains("carrying", StringComparison.Ordinal));
    }

    [Fact]
    public void ASappedSentryIsNotLifted()
    {
        var (rig, engineer, sentry) = AField();
        sentry.OnAddSapper(new EntityHandle(0x0050_0001u));

        Assert.False(rig.Pickup().TryPickup(engineer));

        Assert.False(sentry.IsCarried);
    }

    [Fact]
    public void AnEnemySentryIsNotOffered()
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        ASight(rig);
        rig.Purse.Pockets[TFAmmoNames.Name(TFAmmo.Metal)] = 1000;
        EntityHandle red = rig.AddBuilder(TFTeam.Red);
        EntityHandle blue = rig.AddBuilder(TFTeam.Blue);
        Assert.True(rig.Builder().Build(blue, TFObjectType.Sentrygun, 0, Vector.Zero, Vector.Zero).Result
                    == TFCanBuildResult.CanBuild);
        var blueSentry = (TFSentryGun)Assert.Single(rig.Buildings().OwnedBy(blue, TFObjectType.Sentrygun));
        rig.Clock.Now += 100f;          // the blue sentry stands
        blueSentry.OnThink();
        rig.Origins[blueSentry.Handle] = new Vector(48f, 0f, 0f);
        rig.Centers[blueSentry.Handle] = new Vector(48f, 0f, 24f);

        Assert.False(rig.Pickup().TryPickup(red));

        Assert.False(blueSentry.IsCarried);
        Assert.Null(rig.Pickup().Carried(red));
    }

    [Fact]
    public void AnOutOfRangeSentryIsNotLifted()
    {
        var (rig, engineer, sentry) = AField();
        rig.Origins[sentry.Handle] = new Vector(200f, 0f, 0f);
        rig.Centers[sentry.Handle] = new Vector(200f, 0f, 24f);

        Assert.False(rig.Pickup().TryPickup(engineer));

        Assert.False(sentry.IsCarried);
    }

    [Fact]
    public void AWallBetweenRefusesTheLift()
    {
        var (rig, engineer, sentry) = AField();
        rig.BlockedEnds.Add(new Vector(48f, 0f, 24f));   // the world before the sentry's centre

        Assert.False(rig.Pickup().TryPickup(engineer));

        Assert.False(sentry.IsCarried);
    }

    [Fact]
    public void AConstructingSentryIsNotLifted()
    {
        var (rig, engineer, sentry) = AField(standing: false);

        Assert.False(rig.Pickup().TryPickup(engineer));

        Assert.False(sentry.IsCarried);
    }

    [Fact]
    public void AStunnedEngineerLiftsNothing()
    {
        var (rig, engineer, sentry) = AField();
        rig.Players.All[engineer].Conditions.Add(TFCond.Stunned);

        Assert.False(rig.Pickup().TryPickup(engineer));

        Assert.False(sentry.IsCarried);
    }

    [Fact]
    public void AControlOrLoserStunnedEngineerLiftsNothing()
    {
        // CanPickupBuilding's two stuns (:12467) on the carrier's words: the
        // entry, the condition and either bit.
        var (rig, engineer, sentry) = AField();
        ACarrier(rig, engineer);
        var state = rig.Carriers.Get(engineer.EntryIndex)!;
        state.StunIndex = 0;
        state.StunFlags = (int)TFStunFlags.Controls;
        state.SetConditions(TFCondMask.Of(TFCond.Stunned));

        Assert.False(rig.Pickup().TryPickup(engineer));

        Assert.False(sentry.IsCarried);
    }

    [Fact]
    public void AMovementStunnedEngineerStillLifts()
    {
        // TF_STUN_MOVEMENT alone is neither IsControlStunned nor
        // IsLoserStateStunned: the hands stay free.
        var (rig, engineer, sentry) = AField();
        ACarrier(rig, engineer);
        var state = rig.Carriers.Get(engineer.EntryIndex)!;
        state.StunIndex = 0;
        state.StunFlags = (int)TFStunFlags.Movement;
        state.SetConditions(TFCondMask.Of(TFCond.Stunned));

        Assert.True(rig.Pickup().TryPickup(engineer));

        Assert.True(sentry.IsCarried);
    }

    [Fact]
    public void ATauntingEngineerLiftsNothing()
    {
        var (rig, engineer, sentry) = AField();
        rig.Players.All[engineer].Conditions.Add(TFCond.Taunting);

        Assert.False(rig.Pickup().TryPickup(engineer));

        Assert.False(sentry.IsCarried);
    }

    [Fact]
    public void TheCarriedSentryTakesNoWound()
    {
        var (rig, engineer, sentry) = AField();
        var enemy = rig.AddBuilder(TFTeam.Blue);
        Assert.True(rig.Pickup().TryPickup(engineer));

        Assert.Equal(0, BuildingsRig.Hit(sentry, enemy, 60f, DamageType.Bullet));
    }

    [Fact]
    public void TheCarryingEngineersDeathDestroysHisBuilding()
    {
        var (rig, engineer, sentry) = AField();
        var pickup = rig.Pickup();
        Assert.True(pickup.TryPickup(engineer));

        ((IPlayerDeathHook)pickup).OnPlayerDied(new PlayerDeath(engineer, 3, EntityHandle.None, Vector.Zero, EntityHandle.None));

        Assert.True(sentry.IsDead);
    }

    [Fact]
    public void TheEngineersSpecialSkillReachesTheLift()
    {
        var (rig, engineer, sentry) = AField();
        rig.Engine.Returns("Players.IsAlive", true)
                  .Returns("Players.GetHandle", engineer);
        var states = new TFMovementFixtures.Players();
        states.Add(3, TFClass.Engineer);
        var skill = new TFClassSpecialSkill(states, rig.Engine, pickup: rig.Pickup());

        Assert.True(skill.DoSpecialSkill(3));

        Assert.True(sentry.IsCarried);
    }

    [Fact]
    public void TheLiftStandsAtItsStageOfThousand()
    {
        Assert.Equal(1_000_000 - 1_000 + 932, TFBuildingPickup.Order);
    }

    // ---- the carrier word (m_bCarryingObject, tf_player_shared.cpp:14562) -----

    /// <summary>
    /// A real player_data carrier for the builder, attached as the composite
    /// does: the attach's make answers a fixed handle and its spawn binds
    /// through the rig's registry.
    /// </summary>
    private static void ACarrier(BuildingsRig rig, EntityHandle engineer)
    {
        var carrierHandle = new EntityHandle(0x0002_0007u);
        rig.Engine.Returns("Entities.Create", carrierHandle)   // the attach's make; these facts lift no bread
                  .ReturnsEachCall("Entities.GetIndex", () => engineer.EntryIndex);
        rig.Engine.WhenCalled("Entities.Spawn",
                              c => rig.Registry.Spawn(c.ArgAs<EntityHandle>(0), TFEntityNames.PlayerData));
        new TFPlayerCarrier(rig.Engine, rig.Registry, rig.Carriers, rig.Log).Attach(engineer);
    }

    [Fact]
    public void TheCarriedBuildingRaisesTheCarriersWord()
    {
        var (rig, engineer, sentry) = AField();
        ACarrier(rig, engineer);
        Assert.Equal(1, rig.Carriers.Count);
        var state = rig.Carriers.Get(engineer.EntryIndex);
        Assert.NotNull(state);
        Assert.False(state!.CarryingObject);

        Assert.True(rig.Pickup().TryPickup(engineer));

        Assert.True(state.CarryingObject, $"count={rig.Carriers.Count} stateLive={state.IsLive}");
    }

    [Fact]
    public void TheRedeployLowersTheCarriersWord()
    {
        var (rig, engineer, sentry) = AField();
        ACarrier(rig, engineer);
        var state = rig.Carriers.Get(engineer.EntryIndex);
        Assert.True(rig.Pickup().TryPickup(engineer));
        Assert.True(state!.CarryingObject);

        sentry.StartRedeploy(new Vector(48f, 0f, 0f), Vector.Zero, buildRate: 1f);

        Assert.False(state.CarryingObject);
    }

    [Fact]
    public void TheCarriedBuildingsDeathLowersTheCarriersWord()
    {
        var (rig, engineer, sentry) = AField();
        ACarrier(rig, engineer);
        var state = rig.Carriers.Get(engineer.EntryIndex);
        Assert.True(rig.Pickup().TryPickup(engineer));
        Assert.True(state!.CarryingObject);

        ((IPlayerDeathHook)rig.Pickup()).OnPlayerDied(new PlayerDeath(engineer, 3, EntityHandle.None, Vector.Zero, EntityHandle.None));

        Assert.False(state.CarryingObject);
    }
}
