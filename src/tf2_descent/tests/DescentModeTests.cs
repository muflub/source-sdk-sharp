using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Server.Mode;
using TF2.Shared;

using Xunit;

namespace TF2.Descent.Tests;

/// <summary>
/// TF2 Descent's game mode (plan_descent_logic HE1): picked on every map, no
/// rounds, heroes on one team placed at join as the default class, no
/// friendly fire between heroes, the core's respawn, and TF2's loadout
/// closed to heroes.
/// </summary>
public sealed class DescentModeTests
{
    private static readonly EntityHandle Hero = new(3u);
    private static readonly EntityHandle Mate = new(4u);
    private static readonly EntityHandle Self = Hero;

    private sealed class ModeState(bool active) : IDescentModeState
    {
        public bool Active => active;
    }

    private sealed class Board : ITeams
    {
        public Dictionary<EntityHandle, int> Table { get; } = [];
        public List<(EntityHandle Player, int Team)> Joins { get; } = [];

        public int Team(EntityHandle player) => Table.GetValueOrDefault(player);
        public IReadOnlyList<EntityHandle> Players(int team) => [.. Table.Where(e => e.Value == team).Select(e => e.Key)];
        public int Count(int team) => Players(team).Count;
        public EntityHandle SelectSpawn(EntityHandle player) => EntityHandle.None;

        public JoinTeamResult Join(EntityHandle player, int requested)
        {
            Joins.Add((player, requested));
            Table[player] = requested;
            return JoinTeamResult.Playing;
        }
    }

    private sealed class BotList(params EntityHandle[] bots) : IBotPlayers
    {
        public EntityHandle Add(string name, IReadOnlyList<KeyValuePair<string, string>> userInfo, out string refusal, int team = 0)
        {
            refusal = "no bots here";
            return EntityHandle.None;
        }

        public bool IsBot(EntityHandle player) => bots.Contains(player);
        public bool Kick(EntityHandle bot, string reason) => false;
        public IReadOnlyList<BotPlayer> Bots => [];
    }

    private sealed class Picks : IHeroClassPick
    {
        public List<(EntityHandle Player, TFClass Class)> Made { get; } = [];
        public void Pick(EntityHandle player, TFClass cls) => Made.Add((player, cls));
    }

    private static IGameMode Mode() => new DescentMode();

    private static HeroPlacement Placement(bool active, Board teams, Picks picks, params EntityHandle[] bots)
        => new(new ModeState(active), teams, new BotList(bots), picks, new RecordingLog());

    private static HeroFriendlyFire FriendlyFire(int attackerTeam, int victimTeam, bool active = true)
    {
        var engine = RecordingEngine.Default();
        engine.ReturnsEachCall("Players.GetTeam",
                               () => engine.Calls[^1].Arg(0) is EntityHandle h && h == Mate ? attackerTeam : victimTeam);
        engine.Returns("Entities.IsPlayer", true);
        return new HeroFriendlyFire(new ModeState(active), engine, engine);
    }

    private static DamageContext Hit(EntityHandle victim, EntityHandle attacker) => new(victim, attacker, 50f, 0);

    // ---- the pick ----

    [Fact]
    public void The_selector_picks_the_Descent_mode()
    {
        Assert.Equal(DescentMode.ModeId, new DescentModeSelector().PickMode());
        Assert.Equal(DescentMode.ModeId, Mode().Id);
    }

    // ---- no rounds ----

    [Fact]
    public void The_round_never_ends_however_long_it_runs()
    {
        var rounds = Mode().Rounds;

        rounds.Think(1f);
        rounds.OnPlayerKilled(default);
        rounds.Think(100_000f);

        Assert.False(rounds.RoundOver);
    }

    [Fact]
    public void The_mode_prepares_no_round_and_scores_nothing()
    {
        var mode = Mode();

        Assert.Null(mode.Setup);
        Assert.Null(mode.Scoring);
    }

    [Fact]
    public void The_mode_offers_no_round_limits_and_needs_no_objective_entity()
    {
        var settings = Mode().Settings!;

        Assert.Empty(settings.Settings);
        Assert.Empty(settings.RequiresAnyOf);
    }

    // ---- respawn ----

    [Fact]
    public void The_respawn_stays_the_cores_under_the_death_gate()
    {
        Assert.Null(Mode().Respawn);
    }

    // ---- teams ----

    [Fact]
    public void A_player_may_join_the_heroes_and_no_other_team()
    {
        var join = Mode().JoinPolicy!;

        Assert.True(join.MayJoin(Hero, (int)TFTeam.Red, out _));
        Assert.False(join.MayJoin(Hero, (int)TFTeam.Blue, out string why));
        Assert.Equal(DescentJoinPolicy.HeroesOnlyReason, why);
    }

    [Fact]
    public void The_auto_assign_is_the_heroes()
    {
        var auto = Assert.IsAssignableFrom<ITeamAutoAssign>(Mode().JoinPolicy);

        Assert.Equal((int)TFTeam.Red, auto.AutoTeam(Hero));
    }

    [Fact]
    public void Heroes_are_one_team_for_team_chat()
    {
        Assert.True(Mode().Teamplay);
    }

    [Fact]
    public void A_joining_player_is_placed_on_the_heroes_as_a_soldier()
    {
        var teams = new Board();
        var picks = new Picks();

        Placement(active: true, teams, picks).OnClientActive(3);

        Assert.Equal([(Hero, (int)TFTeam.Red)], teams.Joins);
        Assert.Equal([(Hero, TFClass.Soldier)], picks.Made);
    }

    [Fact]
    public void A_bot_is_not_placed()
    {
        var teams = new Board();
        var picks = new Picks();

        Placement(active: true, teams, picks, Hero).OnClientActive(3);

        Assert.Empty(teams.Joins);
        Assert.Empty(picks.Made);
    }

    [Fact]
    public void Nobody_is_placed_under_another_mode()
    {
        var teams = new Board();
        var picks = new Picks();

        Placement(active: false, teams, picks).OnClientActive(3);

        Assert.Empty(teams.Joins);
        Assert.Empty(picks.Made);
    }

    // ---- friendly fire ----

    [Fact]
    public void A_hero_cannot_hurt_a_hero()
    {
        Assert.Equal(Decision.No, FriendlyFire((int)TFTeam.Red, (int)TFTeam.Red).Decide(Hit(Hero, Mate)));
    }

    [Fact]
    public void A_heros_own_blast_still_lands()
    {
        Assert.Equal(Decision.UseStockRule, FriendlyFire((int)TFTeam.Red, (int)TFTeam.Red).Decide(Hit(Hero, Self)));
    }

    [Fact]
    public void A_hit_across_teams_is_left_to_the_other_rules()
    {
        Assert.Equal(Decision.UseStockRule, FriendlyFire((int)TFTeam.Blue, (int)TFTeam.Red).Decide(Hit(Hero, Mate)));
    }

    [Fact]
    public void Teammates_hits_are_left_to_the_other_rules_under_another_mode()
    {
        Assert.Equal(Decision.UseStockRule,
                     FriendlyFire((int)TFTeam.Red, (int)TFTeam.Red, active: false).Decide(Hit(Hero, Mate)));
    }

    // ---- TF2's loadout ----

    [Fact]
    public void TF2s_loadout_is_closed_to_heroes()
    {
        Assert.False(new HeroInventoryGate(new ModeState(true)).IsOpen(3, out string why));
        Assert.Equal(HeroInventoryGate.Reason, why);
    }

    [Fact]
    public void TF2s_loadout_stays_open_under_another_mode()
    {
        Assert.True(new HeroInventoryGate(new ModeState(false)).IsOpen(3, out _));
    }
}
