extern alias client;

using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;
using SourceSharp.Shared.Interop;

using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Player;
using TF2.Tests.Rules;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// Stock's teammate collision (tf2 F05): the mask arm
/// (<see cref="TFMovementRules.PlayerSolidMask"/>, stock's
/// <c>CTFGameMovement::PlayerSolidMask</c>, tf_gamemovement.cpp:259-284) and
/// the per-entity rule (<see cref="TFTeamCollide"/>, stock's
/// <c>CTFPlayer::ShouldCollide</c>, tf_player.cpp:10159-10178) together make
/// a teammate pass through a player-movement trace and an enemy stop, the
/// rule by the roster's real team so a disguised spy keeps its colours'
/// collisions.
/// </summary>
public class TFTeamCollideTests
{
    private const int PlayerMovement = (int)EngineCollisionGroup.PlayerMovement;

    private static readonly EntityHandle Mover = new(0x0001_0001);
    private static readonly EntityHandle Other = new(0x0002_0001);

    private sealed class Candidates : ITraceCandidates
    {
        public readonly Dictionary<EntityHandle, TraceCandidate> Blocks = [];

        public bool TryRead(EntityHandle entity, out TraceCandidate candidate)
            => Blocks.TryGetValue(entity, out candidate);

        public bool IsWorld(EntityHandle entity) => false;

        public bool IsBrushModel(int modelIndex) => false;
    }

    private static TraceCandidate Player => new(PlayerMovement, SolidType.VPhysics, 0, EntityHandle.None);

    private static TraceFilterRules Filter(IHooks<IEntityCollideRule> rules, EntityHandle first, EntityHandle second)
    {
        var blocks = new Candidates();
        blocks.Blocks[first] = Player;
        blocks.Blocks[second] = Player;
        return TraceFilterRules.Over(blocks, CollisionRules.BuildMatrix(), rules);
    }

    private static bool HitBy(TraceFilterRules filter, EntityHandle candidate, uint mask, int group = PlayerMovement)
    {
        TraceFilterQuery query = filter.Resolve(default, group);
        return filter.ShouldHitEntity(query, candidate.Value, (int)mask);
    }

    /// <summary>The rule over a roster whose words are the fact's, the convar at its default.</summary>
    private static (TFTeamCollide Rule, FakeRoster Roster, TFAvoidTeammatesConVar Avoid) Rig()
    {
        var vars = new ConVarRegistry(new NamedConVars(new Dictionary<string, string>()));
        vars.RegisterTypes(
        [
            typeof(TFAvoidTeammatesConVar),
            typeof(TFAvoidTeammatesPushawayConVar),
        ]);
        var roster = new FakeRoster(new FakeClock());
        return (new TFTeamCollide(vars.Declared<TFAvoidTeammatesConVar>(), roster), roster,
                vars.Declared<TFAvoidTeammatesConVar>());
    }

    private static uint MovementMask(TFTeam mover)
        => mover switch
        {
            TFTeam.Red => GameMovement.MaskPlayerSolid | TFContents.BlueTeam,
            TFTeam.Blue => GameMovement.MaskPlayerSolid | TFContents.RedTeam,
            _ => GameMovement.MaskPlayerSolid,
        };

    /// <summary>
    /// The mask arm's word, both sides alike: a mover's mask carries the OTHER
    /// team's contents bit over the stock mask, and a teamless player's is the
    /// stock mask untouched — stock's switch fall-through
    /// (tf_gamemovement.cpp:269-283).
    /// </summary>
    [Fact]
    public void TheMaskCarriesTheOtherTeamsBit()
    {
        var players = new TFMovementFixtures.Players();
        players.Add(1, TFClass.Scout);
        var red = TFMovementFixtures.Rules(players, teams: new TFMovementFixtures.AllRed(TFTeam.Red));
        var blue = TFMovementFixtures.Rules(players, teams: new TFMovementFixtures.AllRed(TFTeam.Blue));
        var none = TFMovementFixtures.Rules(players);

        Assert.Equal(GameMovement.MaskPlayerSolid | TFContents.BlueTeam,
                     red.PlayerSolidMask(1, GameMovement.MaskPlayerSolid, false));
        Assert.Equal(GameMovement.MaskPlayerSolid | TFContents.RedTeam,
                     blue.PlayerSolidMask(1, GameMovement.MaskPlayerSolid, false));
        Assert.Equal(GameMovement.MaskPlayerSolid,
                     none.PlayerSolidMask(1, GameMovement.MaskPlayerSolid, false));
    }

    /// <summary>The same arm, the client's compilation of it.</summary>
    [Fact]
    public void TheClientMaskCarriesTheOtherTeamsBit()
    {
        var players = new TFMovementFixtures.ClientPlayers();
        players.Add(1, TFClass.Scout);
        var red = TFMovementFixtures.ClientServices(players,
                       teams: new TFMovementFixtures.ClientAllRed(client::TF2.Shared.TFTeam.Red))
            .Create<client::TF2.Shared.Movement.TFMovementRules>();

        Assert.Equal(GameMovement.MaskPlayerSolid | TFContents.BlueTeam,
                     red.PlayerSolidMask(1, GameMovement.MaskPlayerSolid, false));
    }

    /// <summary>
    /// The ghost's arm (tf_gamemovement.cpp:264-267): in the Halloween ghost
    /// mode the mask is the brush-only one, team or none aside.
    /// </summary>
    [Fact]
    public void AGhostWalksTheBrushOnlyMask()
    {
        var players = new TFMovementFixtures.Players();
        players.Add(1, TFClass.Scout).SetConditions(TFCondMask.Of(TFCond.HalloweenGhostMode));
        var rules = TFMovementFixtures.Rules(players, teams: new TFMovementFixtures.AllRed(TFTeam.Red));

        Assert.Equal(GameMovement.MaskPlayerSolidBrushOnly,
                     rules.PlayerSolidMask(1, GameMovement.MaskPlayerSolid, false));
    }

    /// <summary>
    /// A teammate is not hit: the red mover's mask (the arm's, the blue bit
    /// over the stock mask) asked about a red player through the filter with
    /// the rule chained — the rule refuses it, stock's
    /// <c>ShouldCollide</c> finding no RED bit in the mask.
    /// </summary>
    [Fact]
    public void TeammatesDoNotCollideInMovement()
    {
        var (rule, roster, _) = Rig();
        var red = roster.Add(TFTeam.Red);
        var mate = roster.Add(TFTeam.Red);

        Assert.False(HitBy(Filter(new ListHooks<IEntityCollideRule>(rule), red, mate), mate, MovementMask(TFTeam.Red)));
    }

    /// <summary>
    /// An enemy is hit: the same red mover's mask asked about a blue player —
    /// the blue player carrying the bit the mover's mask carries.
    /// </summary>
    [Fact]
    public void EnemiesStillCollide()
    {
        var (rule, roster, _) = Rig();
        var red = roster.Add(TFTeam.Red);
        var foe = roster.Add(TFTeam.Blue);

        Assert.True(HitBy(Filter(new ListHooks<IEntityCollideRule>(rule), red, foe), foe, MovementMask(TFTeam.Red)));
    }

    /// <summary>
    /// A disguised enemy still collides: the red spy's row holds the disguise
    /// (the enemy's colours); the rule answers by the roster, the real team,
    /// so the blue mover's mask — the red bit — stops him. A rule that read
    /// the disguise would demand BLUE of a mask carrying red, and the spy
    /// would pass his enemy.
    /// </summary>
    [Fact]
    public void ADisguisedEnemyStillCollides()
    {
        var (rule, roster, _) = Rig();
        var blue = roster.Add(TFTeam.Blue);
        var spy = roster.Add(TFTeam.Red);

        Assert.True(HitBy(Filter(new ListHooks<IEntityCollideRule>(rule), blue, spy), spy, MovementMask(TFTeam.Blue)));
    }

    /// <summary>
    /// With <c>tf_avoidteammates</c> off the rule stands down and the
    /// teammate is hit again, the mask's bit notwithstanding.
    /// </summary>
    [Fact]
    public void WithAvoidTeammatesOffTeammatesCollide()
    {
        var (rule, roster, avoid) = Rig();
        avoid.SetValue(false);
        var red = roster.Add(TFTeam.Red);
        var mate = roster.Add(TFTeam.Red);

        Assert.True(HitBy(Filter(new ListHooks<IEntityCollideRule>(rule), red, mate), mate, MovementMask(TFTeam.Red)));
    }

    /// <summary>
    /// A trace that is not the player-movement group is untouched: the rule
    /// answers yes and the matrix alone decides, so bullets stop on
    /// teammates as before.
    /// </summary>
    [Fact]
    public void BulletsStillStopOnATeammate()
    {
        var (rule, roster, _) = Rig();
        var red = roster.Add(TFTeam.Red);
        var mate = roster.Add(TFTeam.Red);

        Assert.True(HitBy(Filter(new ListHooks<IEntityCollideRule>(rule), red, mate),
                          mate, GameMovement.MaskPlayerSolid, 0));
    }
}
