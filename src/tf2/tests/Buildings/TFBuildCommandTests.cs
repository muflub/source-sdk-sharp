using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Tests;
using TF2.Server.Buildings;
using TF2.Server.Entities.Items;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;
using TF2.Tests.Entities.Items;
using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>The engineer's two console orders: arm the builder, take them down.</summary>
public class TFBuildCommandTests
{
    /// <summary>A funded engineer at the origin, facing yaw 0, over the command.</summary>
    private static (BuildingsRig Rig, TFBuildCommand Build, TFDestroyCommand Destroy, EntityHandle Engineer) AField()
    {
        var rig = new BuildingsRig();
        EntityHandle engineer = rig.AddBuilder();
        rig.Purse.Pockets[TFAmmoNames.Name(TFAmmo.Metal)] = 500;
        rig.Engine.Returns("Players.GetHandle", engineer)   // the armed builder's ask
                  .Returns("Entities.SetParent", true);      // the lift's mount, for the carried fact
        return (rig, rig.BuildCommand(), rig.DestroyCommand(), engineer);
    }

    [Fact]
    public void TheBuildCommandAnswersOnlyBuild()
    {
        var (_, build, _, engineer) = AField();

        Assert.False(build.Handle(engineer, "destroy", ["2"]));
    }

    [Fact]
    public void TheDestroyCommandAnswersOnlyDestroy()
    {
        var (_, _, destroy, engineer) = AField();

        Assert.False(destroy.Handle(engineer, "build", ["2"]));
    }

    [Fact]
    public void TheBuildOrderArmsTheBuilderAndSpawnsNothingYet()
    {
        var (rig, build, _, engineer) = AField();

        Assert.True(build.Handle(engineer, "build", ["2", "0"]));

        Assert.Empty(rig.Spawner.Placed);   // the blueprint is the deploy's (tf_weapon_builder.cpp:165-172)
        Assert.Equal(["switch " + engineer + " tf_weapon_builder"], rig.BuilderArms.Asked);
    }

    [Fact]
    public void TheDeployConsumesTheOrderAndTheTriggerPlaces()
    {
        var (rig, build, _, engineer) = AField();
        rig.Engine.Returns("Players.GetHandle", engineer)
                  .Returns("Entities.GetEyeAngles", Vector.Zero)
                  .Returns("Entities.GetEyePosition", new Vector(0f, 0f, 64f));
        rig.Centers[engineer] = new Vector(0f, 0f, 41f);
        var placement = rig.Placement();
        var command = rig.BuildCommand(placement);

        Assert.True(command.Handle(engineer, "build", ["2", "0"]));
        Assert.Empty(rig.Spawner.Placed);

        placement.StartPlacement(3);        // the deploy
        Assert.Single(rig.Spawner.Placed);  // the blueprint out

        Assert.True(placement.Place(3));    // the trigger
        TFBaseObject standing = Assert.Single(rig.Buildings().OwnedBy(engineer, TFObjectType.Sentrygun));
        Assert.IsType<TFSentryGun>(standing);
        Assert.Equal(370, rig.Purse.Pockets[TFAmmoNames.Name(TFAmmo.Metal)]);
    }

    [Fact]
    public void ARefusedOrderArmsNoBuilder()
    {
        var (rig, build, _, engineer) = AField();
        EntityHandle soldier = rig.AddBuilder(TFTeam.Red, TFClass.Soldier);

        Assert.True(build.Handle(soldier, "build", ["2", "0"]));

        Assert.Empty(rig.Spawner.Placed);
        Assert.Empty(rig.BuilderArms.Asked);
        Assert.Contains(rig.Log.Lines, line => line.Text.Contains("refused", StringComparison.Ordinal));
    }

    [Fact]
    public void TheDeadEngineerBuildsNothing()
    {
        var (rig, build, _, engineer) = AField();
        rig.Players.All[engineer].Alive = false;

        Assert.True(build.Handle(engineer, "build", ["2"]));

        Assert.Empty(rig.Spawner.Placed);
        Assert.Empty(rig.BuilderArms.Asked);
        Assert.Contains(rig.Log.Lines, line => line.Text.Contains("dead", StringComparison.Ordinal));
    }

    [Fact]
    public void TheCarryingEngineerIsRefusedTheCommand()
    {
        var (rig, build, _, engineer) = AField();
        TFSentryGun sentry = rig.Spawn<TFSentryGun>("obj_sentrygun");
        sentry.StartPlacement(engineer);
        sentry.MakeCarriedObject(engineer);

        Assert.True(build.Handle(engineer, "build", ["2", "0"]));

        Assert.Empty(rig.Spawner.Placed);    // no spawn answered the order
        Assert.Empty(rig.BuilderArms.Asked);
        Assert.Contains(rig.Log.Lines, line => line.Text.Contains("carrying", StringComparison.Ordinal));
    }

    [Fact]
    public void TheBareBuildIsOnlyTheUsageLine()
    {
        var (rig, build, _, engineer) = AField();

        Assert.True(build.Handle(engineer, "build", []));

        // Stock's bare arm is the usage line alone (tf_player.cpp:7636):
        // the command opens no menu — that is the client's show arm, the
        // build PDA the hands hold (tf_weapon_pda.cpp:157-180) — and
        // switches nothing.
        Assert.Empty(rig.Spawner.Placed);
        Assert.Empty(rig.BuilderArms.Asked);
        Assert.Contains(rig.Log.Lines, line => line.Text.Contains("Usage: build", StringComparison.Ordinal));
    }

    [Fact]
    public void TheCarryingGateRefusesTheBareBuildToo()
    {
        var (rig, build, _, engineer) = AField();
        TFSentryGun sentry = rig.Spawn<TFSentryGun>("obj_sentrygun");
        sentry.StartPlacement(engineer);
        sentry.MakeCarriedObject(engineer);

        Assert.True(build.Handle(engineer, "build", []));

        // The carrying gate stands over the bare arm too
        // (tf_player.cpp:7601-7603 precedes the parse): no usage line
        // behind it.
        Assert.Empty(rig.BuilderArms.Asked);
        Assert.Contains(rig.Log.Lines, line => line.Text.Contains("carrying", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Log.Lines, line => line.Text.Contains("Usage: build", StringComparison.Ordinal));
    }

    [Fact]
    public void TheTauntingEngineerBuildsNothing()
    {
        var (rig, build, _, engineer) = AField();
        rig.Players.AddCond(engineer, TFCond.Taunting, 5f);

        Assert.True(build.Handle(engineer, "build", ["2", "0"]));

        // IsTaunting holds the command (tf_player.cpp:7605-7606).
        Assert.Empty(rig.Spawner.Placed);
        Assert.Empty(rig.BuilderArms.Asked);
        Assert.Contains(rig.Log.Lines, line => line.Text.Contains("taunting", StringComparison.Ordinal));
    }

    [Fact]
    public void ANonNumericTypeIsAnsweredWithTheNumbers()
    {
        var (rig, build, _, engineer) = AField();

        Assert.True(build.Handle(engineer, "build", ["sentry"]));

        Assert.Empty(rig.Spawner.Placed);
        Assert.Contains(rig.Log.Lines, line => line.Text.Contains("the type is a number", StringComparison.Ordinal));
    }

    [Fact]
    public void TheLegacyThreeOrdersTheTeleporterExit()
    {
        var (rig, _, _, engineer) = AField();
        rig.Engine.Returns("Players.GetHandle", engineer)
                  .Returns("Entities.GetEyeAngles", Vector.Zero)
                  .Returns("Entities.GetEyePosition", new Vector(0f, 0f, 64f));
        rig.Centers[engineer] = new Vector(0f, 0f, 41f);
        var placement = rig.Placement();
        var command = rig.BuildCommand(placement);

        Assert.True(command.Handle(engineer, "build", ["3"]));

        placement.StartPlacement(3);        // the deploy: the exit blueprint out
        TFTeleporter exit = Assert.Single(rig.Buildings().OwnedBy(engineer, TFObjectType.Teleporter)
                                                 .OfType<TFTeleporter>());
        Assert.Equal(1, exit.Mode);
    }

    [Fact]
    public void TheDestroyTakesDownTheEngineersSentry()
    {
        var (rig, _, destroy, engineer) = AField();
        Assert.True(rig.Builder().Build(engineer, TFObjectType.Sentrygun, 0, Vector.Zero, Vector.Zero).Result
                    == TFCanBuildResult.CanBuild);

        Assert.True(destroy.Handle(engineer, "destroy", ["2"]));

        Assert.Empty(rig.Buildings().OwnedBy(engineer, TFObjectType.Sentrygun));
        Assert.Contains("destroyed Sentrygun 0", rig.Announcer.Said);
    }

    [Fact]
    public void DestroyingNothingSaysSo()
    {
        var (rig, _, destroy, engineer) = AField();

        Assert.True(destroy.Handle(engineer, "destroy", ["2"]));

        Assert.Contains(rig.Log.Lines, line => line.Text.Contains("nothing of type 2 mode 0 to destroy", StringComparison.Ordinal));
    }

    [Fact]
    public void TheDestroyTakesDownOneOfTheModeNamed()
    {
        var (rig, _, destroy, engineer) = AField();
        rig.Builder().Build(engineer, TFObjectType.Teleporter, 0, Vector.Zero, Vector.Zero);   // the entrance
        rig.Builder().Build(engineer, TFObjectType.Teleporter, 1, new Vector(200f, 0f, 0f), Vector.Zero);   // the exit

        Assert.True(destroy.Handle(engineer, "destroy", ["1", "1"]));

        // One of the pair: GetObjectOfType names one (tf_player.cpp:8189).
        Assert.Single(rig.Buildings().OwnedBy(engineer, TFObjectType.Teleporter));
        Assert.Contains("destroyed Teleporter 1", rig.Announcer.Said);
    }

    [Fact]
    public void TheBareDestroyThreeIsTheOldTeleporterExitBind()
    {
        var (rig, _, destroy, engineer) = AField();
        rig.Builder().Build(engineer, TFObjectType.Teleporter, 0, Vector.Zero, Vector.Zero);
        rig.Builder().Build(engineer, TFObjectType.Teleporter, 1, new Vector(200f, 0f, 0f), Vector.Zero);   // the exit the legacy bind names

        Assert.True(destroy.Handle(engineer, "destroy", ["3"]));

        // Fixup old binds (tf_player.cpp:7676-7685): 3 became mode 1.
        TFTeleporter entrance = Assert.Single(rig.Buildings().OwnedBy(engineer, TFObjectType.Teleporter)
                                                          .OfType<TFTeleporter>());
        Assert.Equal(0, entrance.Mode);
        Assert.Contains("destroyed Teleporter 1", rig.Announcer.Said);
    }

    [Fact]
    public void TheSappedBuildingRefusesItsOwnHand()
    {
        var (rig, _, destroy, engineer) = AField();
        rig.Builder().Build(engineer, TFObjectType.Sentrygun, 0, Vector.Zero, Vector.Zero);
        var sentry = Assert.Single(rig.Buildings().OwnedBy(engineer, TFObjectType.Sentrygun));
        EntityHandle spy = rig.AddBuilder(TFTeam.Blue, TFClass.Spy);
        var sapper = rig.Spawn<TFSapper>(TFSapper.MapClass);
        sapper.StartPlacement(spy);
        sapper.AttachTo(sentry);
        sapper.StartBuilding();
        sapper.OnThink();

        Assert.True(destroy.Handle(engineer, "destroy", ["2"]));

        // HasSapper refuses the detonation (tf_player.cpp:8193).
        Assert.Single(rig.Buildings().OwnedBy(engineer, TFObjectType.Sentrygun));
    }

    [Fact]
    public void TheSpyDestroysNothing()
    {
        var (rig, _, destroy, engineer) = AField();
        rig.Builder().Build(engineer, TFObjectType.Sentrygun, 0, Vector.Zero, Vector.Zero);
        (EntityHandle spy, _) = rig.Players.Add(TFTeam.Red, TFClass.Spy);

        Assert.True(destroy.Handle(spy, "destroy", ["2"]));

        // Spies can't destroy buildings (sappers) — the class gate
        // (tf_player.cpp:7669).
        Assert.Single(rig.Buildings().OwnedBy(engineer, TFObjectType.Sentrygun));
    }

    [Fact]
    public void TheDeadEngineerDestroysNothing()
    {
        var (rig, _, destroy, engineer) = AField();
        Assert.True(rig.Builder().Build(engineer, TFObjectType.Sentrygun, 0, Vector.Zero, Vector.Zero).Result
                    == TFCanBuildResult.CanBuild);
        rig.Players.All[engineer].Alive = false;

        Assert.True(destroy.Handle(engineer, "destroy", ["2"]));

        Assert.Single(rig.Buildings().OwnedBy(engineer, TFObjectType.Sentrygun));   // still standing
    }

    [Fact]
    public void TheMeleeOnlySuddenDeathRefusesTheBuildOrder()
    {
        var (rig, _, _, engineer) = AField();
        var convars = ConVarRegistry.For(TestServices.UnattachedConsole());
        convars.RegisterAll(typeof(TFStalemateConVars).Assembly);
        var melee = convars.Declared<MpStalemateMeleeOnlyConVar>();
        melee.Bind(-1, "mp_stalemate_meleeonly", "1");
        var changeTime = convars.Declared<TFStalemateChangeClassTimeConVar>();
        changeTime.Bind(-1, "tf_stalematechangeclasstime", "20");
        var build = rig.BuildCommand(round: new Stalemate(), stalemate: new TFStalemateConVars(melee, changeTime));

        Assert.True(build.Handle(engineer, "build", ["2", "0"]));

        Assert.Empty(rig.Spawner.Placed);
        Assert.Empty(rig.BuilderArms.Asked);
        Assert.Contains(rig.Log.Lines, line => line.Text.Contains("melee-only", StringComparison.Ordinal));
    }

    [Fact]
    public void TheBuildAndDestroyStandAtTheirStagesOfThousand()
    {
        Assert.Equal(1_000_000 - 1_000 + 930, TFBuildCommand.Order);
        Assert.Equal(1_000_000 - 1_000 + 931, TFDestroyCommand.Order);
    }

    /// <summary>A sudden death in progress.</summary>
    private sealed class Stalemate : ITeamRoundState
    {
        public RoundState State => RoundState.Stalemate;
        public bool InWaitingForPlayers => false;
        public bool InSetup => false;
        public float MapResetTime => 0f;
        public float StalemateStartTime => -1f;
        public TFTeam WinningTeam => TFTeam.Unassigned;
    }
}
