extern alias client;

using SourceSharp.Abi;

using Xunit;

using CClient = client::SourceSharp.Client;
using CGroup = client::SourceSharp.Abi.CollisionGroup;
using CStandalone = client::SourceSharp.Client.Standalone;
using Engine = client::SourceSharp.Abi.RecordingEngine;
using Mirror = client::TF2.Client.Entities.ClientRespawnRoomVisualizer;
using Rule = client::TF2.Client.Entities.TFClientRespawnRoomCollide;
using RoundView = client::TF2.Shared.Rules.ITFRoundStateView;
using RoundStateId = client::TF2.Shared.Rules.RoundState;
using TFContents = client::TF2.Shared.TFContents;
using TFTeam = client::TF2.Shared.TFTeam;

namespace TF2.Tests.Client;

/// <summary>
/// The visualizer's client trace arm (<see cref="Rule"/>), which the
/// prediction's entity clip asks: stock's
/// <c>C_FuncRespawnRoomVisualizer::ShouldCollide</c> — an enemy's movement
/// stops at the wall, a teammate's passes, nobody stops in the win state, a
/// trace in another group passes, and any other entity is left to the other
/// arms. One behaviour per fact.
/// </summary>
public sealed class TFClientRespawnRoomCollideTests
{
    private static readonly EntityHandle Wall = new(0x0004_0031u);
    private static readonly EntityHandle Mover = new(0x0001_0001u);
    private static readonly CGroup PlayerMovement = (CGroup)8;

    // CTFGameMovement::PlayerSolidMask's team arm: a red mover's mask carries BLUE's bit, a blue one's RED's.
    private const int BlueMoversMask = 0x0201400B | TFContents.RedTeam;
    private const int RedMoversMask = 0x0201400B | TFContents.BlueTeam;

    private sealed class Standalones(params CStandalone.ClientStandaloneEntity[] all) : CClient.IClientStandalones
    {
        public IReadOnlyList<CStandalone.ClientStandaloneEntity> Live() => all;

        public CStandalone.ClientStandaloneEntity? GetByIndex(int entityIndex)
            => all.FirstOrDefault(e => e.Handle.EntryIndex == entityIndex);
    }

    private sealed class FrozenRound(RoundStateId state) : RoundView
    {
        public RoundStateId State { get; } = state;

        public TFTeam Winner { get; } = TFTeam.Unassigned;
    }

    private static Rule RuleOver(TFTeam wallTeam, RoundStateId round = RoundStateId.RoundRunning)
    {
        var engine = new Engine();
        var wall = new Mirror(engine, null!, null!, new FrozenRound(round), null!) { Handle = Wall };
        wall.State.BindLocal();
        wall.State.Team = (byte)wallTeam;
        return new Rule(new Standalones(wall), new FrozenRound(round));
    }

    [Fact]
    public void AnEnemysMovementStopsAtTheWall()
    {
        Assert.True(RuleOver(TFTeam.Red).ShouldCollide(Mover, Wall, PlayerMovement, BlueMoversMask));
    }

    [Fact]
    public void ATeammatesMovementPassesTheWall()
    {
        Assert.False(RuleOver(TFTeam.Red).ShouldCollide(Mover, Wall, PlayerMovement, RedMoversMask));
    }

    [Fact]
    public void NobodyStopsAtTheWallInTheWinState()
    {
        Assert.False(RuleOver(TFTeam.Red, RoundStateId.TeamWin).ShouldCollide(Mover, Wall, PlayerMovement, BlueMoversMask));
    }

    [Fact]
    public void ATraceInAnotherGroupPassesTheWall()
    {
        Assert.False(RuleOver(TFTeam.Red).ShouldCollide(Mover, Wall, (CGroup)0, BlueMoversMask));
    }

    [Fact]
    public void AnotherEntityIsLeftToTheOtherArms()
    {
        var prop = new EntityHandle(0x0002_00A1u);

        Assert.True(RuleOver(TFTeam.Red).ShouldCollide(Mover, prop, PlayerMovement, RedMoversMask));
    }

    [Fact]
    public void TheArmRegistersBesideTheClientsOtherCollideRules()
    {
        // Registered AS IEntityCollideRule it was claimed twice beside
        // TFClientTeamCollide, and the managed client refused to load (live,
        // cp_granary: "IEntityCollideRule is registered twice").
        var services = new client::SourceSharp.Abi.ServiceContainer();

        var registered = client::SourceSharp.Abi.SideServices.Register(services, Side.Client, typeof(Rule).Assembly.GetTypes());

        Assert.Contains(typeof(Rule), registered);
    }
}
