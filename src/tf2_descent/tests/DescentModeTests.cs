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

        /// <summary>Each join's silent flag, in join order (a two-argument join is loud).</summary>
        public List<bool> Silent { get; } = [];

        public JoinTeamResult Join(EntityHandle player, int requested) => Join(player, requested, silent: false);

        public JoinTeamResult Join(EntityHandle player, int requested, bool silent)
        {
            Joins.Add((player, requested));
            Silent.Add(silent);
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

    private sealed class SheetClass(TFClass cls) : IHeroClass
    {
        public TFClass ClassOf(int entIndex) => cls;
    }

    private static HeroPlacement Placement(bool active, Board teams, Picks picks, params EntityHandle[] bots)
        => Placement(active, teams, picks, DescentMode.DefaultClass, bots);

    private static HeroPlacement Placement(bool active, Board teams, Picks picks, TFClass sheetClass, params EntityHandle[] bots)
        => Placement(active, Hero, teams, picks, sheetClass, bots);

    /// <summary>A placement whose engine answers <paramref name="slotHandle"/> as the player in slot 3.</summary>
    private static HeroPlacement Placement(bool active, EntityHandle slotHandle, Board teams, Picks picks, TFClass sheetClass,
                                           params EntityHandle[] bots)
    {
        var engine = RecordingEngine.Default();
        engine.Returns("Players.GetUserId", 2);
        return Placement(active, slotHandle, teams, picks, sheetClass, engine, bots);
    }

    private static HeroPlacement Placement(bool active, EntityHandle slotHandle, Board teams, Picks picks, TFClass sheetClass,
                                           RecordingEngine engine, params EntityHandle[] bots)
    {
        engine.Returns("Players.GetHandle", slotHandle);
        return new(new ModeState(active), engine, teams, new BotList(bots), picks, new SheetClass(sheetClass), new RecordingLog());
    }

    /// <summary>One placement whose engine names the player in slot 3 by <paramref name="userId"/> until the fact changes it.</summary>
    private static (HeroPlacement Placement, RecordingEngine Engine) PlacementWithUser(Board teams, int userId)
    {
        var engine = RecordingEngine.Default();
        engine.Returns("Players.GetUserId", userId);
        return (Placement(active: true, Hero, teams, new Picks(), TFClass.Soldier, engine), engine);
    }

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
    public void A_joining_player_is_placed_as_the_class_of_their_hero_sheet()
    {
        var teams = new Board();
        var picks = new Picks();

        Placement(active: true, teams, picks, TFClass.Pyro).OnClientActive(3);

        Assert.Equal([(Hero, TFClass.Pyro)], picks.Made);
    }

    /// <summary>
    /// HE1b: a level change frees and refills the player's entity slot, so the
    /// player in slot 3 is entry 3 with a later serial. The placement moves the
    /// player the engine names for the slot, never a handle made from the bare
    /// index (serial 0), which the engine answers as no player (team -1).
    /// </summary>
    [Fact]
    public void A_player_whose_slot_was_refilled_by_a_level_change_is_placed_by_the_slots_own_handle()
    {
        var later = new EntityHandle(0x0001_0003u);
        var teams = new Board();
        var picks = new Picks();

        Placement(active: true, later, teams, picks, TFClass.Soldier).OnClientActive(3);

        Assert.Equal([(later, (int)TFTeam.Red)], teams.Joins);
        Assert.Equal([(later, TFClass.Soldier)], picks.Made);
    }

    [Fact]
    public void A_players_first_placement_of_the_session_joins_aloud()
    {
        var teams = new Board();
        var (placement, _) = PlacementWithUser(teams, 7);

        placement.OnClientActive(3);

        Assert.Equal([false], teams.Silent);
    }

    [Fact]
    public void The_same_player_placed_again_after_a_level_change_joins_silently()
    {
        // A level change puts the player back on no team: he joins the heroes again, and the
        // chat has said so once already.
        var teams = new Board();
        var (placement, _) = PlacementWithUser(teams, 7);

        placement.OnClientActive(3);
        teams.Table.Clear();
        placement.OnClientActive(3);

        Assert.Equal(2, teams.Joins.Count);
        Assert.Equal([false, true], teams.Silent);
    }

    [Fact]
    public void Another_player_in_the_same_slot_joins_aloud()
    {
        var teams = new Board();
        var (placement, engine) = PlacementWithUser(teams, 7);

        placement.OnClientActive(3);
        teams.Table.Clear();
        engine.Returns("Players.GetUserId", 8);
        placement.OnClientActive(3);

        Assert.Equal([false, false], teams.Silent);
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

    // ---- the class is the sheet's ----

    /// <summary>The class gate over a sheet of <paramref name="sheetClass"/>, the engine answering <see cref="Hero"/> for slot 3.</summary>
    private static HeroClassGate ClassGate(bool active, TFClass sheetClass, params EntityHandle[] bots)
    {
        var engine = RecordingEngine.Default();
        engine.Returns("Players.GetHandle", Hero);
        return new HeroClassGate(new ModeState(active), engine, new BotList(bots), new SheetClass(sheetClass));
    }

    [Fact]
    public void A_hero_may_not_choose_a_class_other_than_his_sheets()
    {
        Assert.False(ClassGate(true, TFClass.Soldier).MayChoose(3, TFClass.Pyro, out string why));
        Assert.Equal(HeroClassGate.Reason, why);
    }

    [Fact]
    public void A_hero_may_choose_his_sheets_class()
    {
        Assert.True(ClassGate(true, TFClass.Soldier).MayChoose(3, TFClass.Soldier, out string why));
        Assert.Equal(string.Empty, why);
    }

    [Fact]
    public void The_class_gate_is_open_under_another_mode()
    {
        Assert.True(ClassGate(false, TFClass.Soldier).MayChoose(3, TFClass.Pyro, out _));
    }

    [Fact]
    public void The_class_gate_leaves_a_bot_alone()
    {
        Assert.True(ClassGate(true, TFClass.Soldier, Hero).MayChoose(3, TFClass.Pyro, out _));
    }
}
