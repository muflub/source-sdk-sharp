using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Plan A.7: teammates walk through each other in a mode that says so
/// (<see cref="IGameMode.TeammatesPass"/>), through the core's collide rule,
/// only for movement, unless <c>mp_teammates_collide</c> says they collide.
/// One behaviour per fact.
/// </summary>
public class TeammatesPassRuleTests
{
    private const int PlayerMovement = (int)EngineCollisionGroup.PlayerMovement;
    private const int Bullet = (int)EngineCollisionGroup.None;
    private const int MaskPlayerSolid = 0x201400B;
    private const int Combine = 2;
    private const int Rebels = 3;

    private static readonly EntityHandle Mover = new(0x0001_0001u);
    private static readonly EntityHandle Teammate = new(0x0002_0002u);
    private static readonly EntityHandle Enemy = new(0x0003_0003u);
    private static readonly EntityHandle Spectator = new(0x0004_0004u);
    private static readonly EntityHandle Crate = new(0x0040_0005u);

    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private sealed class Flow : IRoundFlow
    {
        public bool RoundOver => false;
        public void Think(float now) { }
        public void ResetRound() { }
        public void OnPlayerSpawned(EntityHandle player, float now) { }
        public void OnPlayerKilled(PlayerDeathArgs death) { }
    }

    /// <summary>A team mode whose teammates pass, as the tdm mode will answer.</summary>
    private sealed class PassingMode : IGameMode
    {
        public string Id => "passtest";
        public IRoundFlow Rounds { get; } = new Flow();
        public bool Teamplay => true;
        public bool TeammatesPass => true;
    }

    /// <summary>A mode that leaves the member at the contract's default.</summary>
    private sealed class PlainMode : IGameMode
    {
        public string Id => "plain";
        public IRoundFlow Rounds { get; } = new Flow();
    }

    private sealed class Rig
    {
        public readonly ConVarRegistry ConVars = new(new NamedConVars(new Dictionary<string, string>()));
        public readonly RecordingEngine Engine = TestServices.Fake();
        public readonly TeammatesPassRule Rule;
        public readonly Dictionary<EntityHandle, int> Teams = new()
        {
            [Mover] = Combine, [Teammate] = Combine, [Enemy] = Rebels, [Spectator] = 1, [Crate] = -1,
        };

        public Rig(IGameMode? mode)
        {
            ServiceContainer services = TestServices.Services();
            if (mode is not null)
            {
                services.AddInstance(mode.GetType(), mode);
                services.AddInstance<IHooks<IGameModeSelector>>(new ListHooks<IGameModeSelector>(new Selector(mode.Id)));
            }
            else
            {
                services.AddInstance<IHooks<IGameModeSelector>>(new ListHooks<IGameModeSelector>());
            }

            Engine.Returns("Host.ActiveSide", Side.Server);
            Engine.WhenCalled("Players.GetTeam",
                              call => Engine.Returns("Players.GetTeam", Teams[(EntityHandle)call.Args[0]!]));
            var modes = new GameModes(new RecordingLog(), Engine, services, ConVars.Declare<SvModeSwitchWarmupConVar>(),
                                      ConVars.Declare<MpWaitingForPlayersTimeConVar>());
            Rule = new TeammatesPassRule(modes, ConVars.Declare<MpTeammatesCollideConVar>(),
                                         ConVars.Declare<MpTeammatesPassConVar>(), Engine);
        }

        public bool Blocks(EntityHandle entity, int group = PlayerMovement, EntityHandle? mover = null)
            => Rule.ShouldCollide(mover ?? Mover, entity, (CollisionGroup)group, MaskPlayerSolid);

        public int TeamAsks => Engine.Calls.Count(c => c.Member == "Players.GetTeam");
    }

    private sealed class Selector(string id) : IGameModeSelector
    {
        public string? PickMode() => id;
    }

    /// <summary>The contract's no-owner answer: a mode that says nothing keeps everyone solid.</summary>
    [Fact]
    public void AModeThatDoesNotAnswerKeepsTeammatesSolid()
        => Assert.False(((IGameMode)new PlainMode()).TeammatesPass);

    [Fact]
    public void TwoTeammatesPassUnderAPassingMode()
        => Assert.False(new Rig(new PassingMode()).Blocks(Teammate));

    [Fact]
    public void TwoEnemiesStillBlockUnderAPassingMode()
        => Assert.True(new Rig(new PassingMode()).Blocks(Enemy));

    /// <summary>
    /// Deathmatch (a mode that leaves the member at its default) keeps a pair
    /// that would be teammates solid, and asks no team at all: the rule costs a
    /// deathmatch trace nothing beyond the mode's answer.
    /// </summary>
    [Fact]
    public void ADeathmatchPairStillBlocksAndNoTeamIsAsked()
    {
        var rig = new Rig(new PlainMode());

        Assert.True(rig.Blocks(Teammate));
        Assert.Equal(0, rig.TeamAsks);
    }

    [Fact]
    public void WithNoModeActiveEveryoneBlocks()
        => Assert.True(new Rig(null).Blocks(Teammate));

    /// <summary>Only movement: a bullet's trace (another group) still stops on a teammate.</summary>
    [Fact]
    public void ABulletStillStopsOnATeammate()
    {
        var rig = new Rig(new PassingMode());

        Assert.False(rig.Blocks(Teammate));                // the stimulus: movement passes
        Assert.True(rig.Blocks(Teammate, Bullet));
    }

    [Fact]
    public void TeammatesCollideMakesTeammatesBlockAgainAtOnce()
    {
        var rig = new Rig(new PassingMode());
        Assert.False(rig.Blocks(Teammate));

        rig.ConVars.Declared<MpTeammatesCollideConVar>().Hold("1");

        Assert.True(rig.Blocks(Teammate));
    }

    [Fact]
    public void SpectatorsAreNobodysTeammates()
    {
        var rig = new Rig(new PassingMode());
        rig.Teams[Mover] = 1;

        Assert.True(rig.Blocks(Spectator));
    }

    [Fact]
    public void ANonPlayerIsNeverPassed()
        => Assert.True(new Rig(new PassingMode()).Blocks(Crate));

    [Fact]
    public void ATraceWithNoMoverPassesNobody()
        => Assert.True(new Rig(new PassingMode()).Blocks(Teammate, mover: EntityHandle.None));

    /// <summary>The answer the clients predict with is published, and follows the setting.</summary>
    [Fact]
    public void PublishWritesTheAnswerTheClientsRead()
    {
        var rig = new Rig(new PassingMode());
        var published = rig.ConVars.Declared<MpTeammatesPassConVar>();
        Assert.False(published.Value);

        rig.Rule.Publish();
        Assert.True(published.Value);

        rig.ConVars.Declared<MpTeammatesCollideConVar>().Hold("1");
        rig.Rule.Publish();
        Assert.False(published.Value);
    }

    [Fact]
    public void TheSharedAnswerPassesOnlyMovementBetweenTeammates()
    {
        Assert.True(TeammateCollision.Passes(true, PlayerMovement, Combine, Combine));
        Assert.False(TeammateCollision.Passes(false, PlayerMovement, Combine, Combine));
        Assert.False(TeammateCollision.Passes(true, Bullet, Combine, Combine));
        Assert.False(TeammateCollision.Passes(true, PlayerMovement, Combine, Rebels));
        Assert.False(TeammateCollision.Passes(true, PlayerMovement, 0, 0));
    }

    private sealed class UnorderedRule : IEntityCollideRule
    {
        public bool ShouldCollide(EntityHandle mover, EntityHandle entity, CollisionGroup group, int contentsMask) => true;
    }

    /// <summary>
    /// The core's rule sits on the chain beside a mod's that leaves its order
    /// at 0 (TF2's team rule does): the chain resolves, the two not tying.
    /// </summary>
    [Fact]
    public void TheCoreRuleDoesNotTieWithAModsUnorderedRule()
    {
        var rig = new Rig(new PassingMode());
        ServiceContainer services = TestServices.Services();
        services.AddInstance(rig.Rule);
        services.AddInstance(new UnorderedRule());

        var chain = new Hooks<IEntityCollideRule>(services, new RecordingLog());

        Assert.Equal(2, chain.All.Count);
    }
}
