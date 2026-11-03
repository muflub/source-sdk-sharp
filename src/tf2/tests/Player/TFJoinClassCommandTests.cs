using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Player;
using TF2.Shared.Rules;
using TF2.Tests.Entities;

using Xunit;

using Outcome = TF2.Server.Player.TFJoinClassCommand.Outcome;

namespace TF2.Tests.Player;

/// <summary>
/// <c>CTFPlayer::HandleCommand_JoinClass</c>'s rules: the team gates, the
/// desired class, the instant change (first pick, respawn room, dead past
/// the wave) and the change at the next spawn, the class limit, random,
/// and the round's refusals.
/// </summary>
public sealed class TFJoinClassCommandTests
{
    private static readonly EntityHandle Teammate = new(0x0001_0000u | 7);

    private sealed class Waves : ITeamRespawnWaves
    {
        public float Next { get; set; }
        public float NextRespawnWave(TFTeam team) => Next;
        public float NextRespawnWave(TFTeam team, EntityHandle player) => Next;
        public float RespawnWaveMaxLength(TFTeam team, bool scaleWithNumPlayers = true) => 10f;
        public void SetTeamRespawnWaveTime(TFTeam team, float seconds) { }
        public void AddTeamRespawnWaveTime(TFTeam team, float seconds) { }
    }

    private sealed class DeadRoster(TFWeaponOwnersTests.Roster inner) : ITeamRoundRoster
    {
        public float Died { get; set; } = -1f;
        public List<EntityHandle> Blue { get; } = [];
        public IReadOnlyList<EntityHandle> Players(TFTeam team) => team == TFTeam.Blue ? Blue : inner.Players(team);
        public TFTeam TeamOf(EntityHandle player) => inner.TeamOf(player);
        public bool IsAlive(EntityHandle player) => inner.IsAlive(player);
        public float DeathTime(EntityHandle player) => Died;
        public bool IsReadyToPlay(EntityHandle player) => true;
        public void ForceRespawn(EntityHandle player) => inner.ForceRespawn(player);
        public bool ChangeTeam(EntityHandle player, TFTeam team) => true;
        public string Name(EntityHandle player) => "player";
        public int EntIndex(EntityHandle player) => player.EntryIndex;
    }

    /// <summary>The rooms as the items lane's seam answers: the player touches a room of each team set.</summary>
    private sealed class Rooms : TF2.Server.Entities.Rooms.ITFRespawnRooms
    {
        public Dictionary<TFTeam, bool> Touching { get; } = new();
        public bool InRespawnRoom(EntityHandle player, bool sameTeamOnly)
            => Touching.GetValueOrDefault(TFTeam.Red) || (!sameTeamOnly && Touching.Count > 0);
        public bool PointInRespawnRoom(Vector point) => false;
        public bool PointInRespawnRoom(EntityHandle target, Vector origin, bool touchingSameTeamOnly)
            => InRespawnRoom(target, touchingSameTeamOnly);
        public bool PointsCrossRespawnRoomVisualizer(Vector start, Vector end, int teamToIgnore = 0) => false;
        public int TouchCount(EntityHandle player) => Touching.Count;
    }

    private sealed class Limits : ITFClassLimitSettings
    {
        public bool Highlander { get; set; }
        public int ClassLimit { get; set; }
    }

    private sealed class Rig
    {
        public readonly PlayerRig Player = new();
        public readonly TFWeaponOwnersTests.Roster Inner = new() { Red = [PlayerRig.Player] };
        public readonly DeadRoster Roster;
        public readonly Rooms Rooms = new();
        public readonly FakeRoundState Round = new();
        public readonly Waves Waves = new();
        public readonly FakeClock Clock = new() { Now = 100f };
        public readonly FakeRandom Random = new();
        public readonly Limits Limits = new();
        public readonly TFPlayerClass Class;
        public readonly TFJoinClassCommand Command;

        public Rig()
        {
            var engine = Player.Engine;
            engine.Returns("Players.GetObserverMode", ObserverMode.None);
            engine.Returns("Players.GetUserId", 9);
            Roster = new DeadRoster(Inner);
            Class = new TFPlayerClass(Player.Carriers, Player.Items);
            Player.Extra.Add(Class);
            Player.Join();
            Command = new TFJoinClassCommand(Player.Carriers, engine, Roster, Rooms, Player.Classes, Random,
                                             engine, engine, Clock, Player.Log, Round, Waves, Limits);
        }

        /// <summary>The player has spawned as <paramref name="cls"/> before.</summary>
        public Rig As(TFClass cls)
        {
            Player.Carriers.SetClass(PlayerRig.Index, cls);
            Class.Desired = cls;
            return this;
        }

        public Rig Watching()
        {
            Player.Engine.Returns("Players.GetObserverMode", ObserverMode.Chase);
            return this;
        }

        public Outcome Join(string name) => Command.JoinClass(PlayerRig.Player, name);

        public int Respawns => Inner.Respawned.Count;

        /// <summary>Places the player on <paramref name="team"/>: the engine's
        /// team read and the roster both.</summary>
        public Rig On(TFTeam team)
        {
            Player.Engine.Returns("Players.GetTeam", (int)team);
            if (team == TFTeam.Blue)
            {
                Inner.Red.Remove(PlayerRig.Player);
                Roster.Blue.Add(PlayerRig.Player);
            }
            return this;
        }

        /// <summary>One teammate per listed class, on that team, each at
        /// their own entry index.</summary>
        public Rig Teammates(TFTeam team, params TFClass[] classes)
        {
            ICollection<EntityHandle> list = team == TFTeam.Blue ? Roster.Blue : Inner.Red;
            var slot = 10;
            foreach (var cls in classes)
            {
                var mate = new EntityHandle(0x0001_0000u | (uint)slot++);
                list.Add(mate);
                Player.Join(mate.EntryIndex);
                Assert.True(Player.Carriers.SetClass(mate.EntryIndex, cls));
            }
            return this;
        }

        /// <summary>The class panels shown, decoded from the VGUIMenu payloads.</summary>
        public (string Name, bool Show)[] Menus()
        {
            var menus = new List<(string, bool)>();
            foreach (var c in Player.Engine.AllOf("UserMessages.Send"))
            {
                if (c.ArgAs<string>(0) != "VGUIMenu")
                    continue;
                var reader = new UserMessageReader(c.ArgAs<byte[]>(4));
                var menu = VguiMenu.Read(ref reader);
                menus.Add((menu.Name, menu.Show));
            }
            return menus.ToArray();
        }
    }

    [Fact]
    public void The_first_pick_respawns_at_once_as_the_desired_class()
    {
        var rig = new Rig().Watching();

        Assert.Equal(Outcome.Respawned, rig.Join("heavyweapons"));

        Assert.Equal(TFClass.HeavyWeapons, rig.Class.Desired);
        Assert.Equal(1, rig.Respawns);
    }

    [Fact]
    public void A_living_player_outside_a_respawn_room_changes_at_the_next_spawn()
    {
        var rig = new Rig().As(TFClass.Scout);

        Assert.Equal(Outcome.AtNextSpawn, rig.Join("heavyweapons"));

        Assert.Equal(TFClass.HeavyWeapons, rig.Class.Desired);
        Assert.Equal(TFClass.Scout, rig.Player.Carriers.ClassOf(PlayerRig.Player));
        Assert.Equal(0, rig.Respawns);
        Assert.Contains(rig.Player.Engine.AllOf("UserMessages.Send"), c => c.ArgAs<int>(2) == PlayerRig.Index);
    }

    [Fact]
    public void A_living_player_in_their_teams_respawn_room_changes_at_once()
    {
        var rig = new Rig().As(TFClass.Scout);
        rig.Rooms.Touching[TFTeam.Red] = true;

        Assert.Equal(Outcome.Respawned, rig.Join("heavyweapons"));
        Assert.Equal(1, rig.Respawns);
    }

    [Fact]
    public void The_enemy_teams_respawn_room_is_no_instant_change()
    {
        var rig = new Rig().As(TFClass.Scout);
        rig.Rooms.Touching[TFTeam.Blue] = true;

        Assert.Equal(Outcome.AtNextSpawn, rig.Join("heavyweapons"));
    }

    [Fact]
    public void A_granted_player_changes_at_once_though_alive_outside_a_room()
    {
        var rig = new Rig().As(TFClass.Scout);
        rig.Class.AllowInstantSpawn = true; // game_forcerespawn's grant

        Assert.Equal(Outcome.Respawned, rig.Join("heavyweapons"));
        Assert.Equal(1, rig.Respawns);
    }

    [Fact]
    public void A_granted_dead_player_picking_the_same_class_comes_back()
    {
        // tf_player.cpp:6929-6938: the grant catches the missed wave and re-pick.
        var rig = new Rig().As(TFClass.Scout).Watching();
        rig.Class.AllowInstantSpawn = true;

        Assert.Equal(Outcome.Respawned, rig.Join("scout"));
        Assert.Equal(1, rig.Respawns);
    }

    [Fact]
    public void The_winners_round_loser_loses_their_grant()
    {
        // tf_player.cpp:6838-6842.
        var rig = new Rig().As(TFClass.Scout);
        rig.Round.State = RoundState.TeamWin;
        rig.Round.WinningTeam = TFTeam.Blue;
        rig.Class.AllowInstantSpawn = true;

        Assert.Equal(Outcome.AtNextSpawn, rig.Join("heavyweapons"));
        Assert.False(rig.Class.AllowInstantSpawn);
        Assert.Equal(0, rig.Respawns);
    }

    [Fact]
    public void A_dead_player_past_the_wave_respawns_and_one_before_it_waits()
    {
        var rig = new Rig().As(TFClass.Scout).Watching();
        rig.Roster.Died = 90f;

        rig.Waves.Next = 105f;
        Assert.Equal(Outcome.AtNextSpawn, rig.Join("heavyweapons"));

        rig.Waves.Next = 95f;
        Assert.Equal(Outcome.Respawned, rig.Join("soldier"));
    }

    [Fact]
    public void A_dead_player_who_never_died_this_life_respawns()
    {
        var rig = new Rig().As(TFClass.Scout).Watching();

        Assert.Equal(Outcome.Respawned, rig.Join("medic"));
    }

    [Fact]
    public void Joining_a_class_fakes_the_menu_closed()
    {
        // tf_player.cpp:6781-6783, before the refusals that follow it.
        var rig = new Rig().As(TFClass.Scout).Watching();
        rig.Class.MenuOpen = true;

        Assert.Equal(Outcome.Respawned, rig.Join("heavyweapons"));
        Assert.False(rig.Class.MenuOpen);
    }

    [Fact]
    public void A_refused_choice_still_fakes_the_menu_closed()
    {
        var rig = new Rig().As(TFClass.Scout);
        rig.Class.MenuOpen = true;
        rig.Round.State = RoundState.TeamWin;
        rig.Round.WinningTeam = TFTeam.Blue; // a red loser's choice is refused, past the fake close

        Assert.Equal(Outcome.AtNextSpawn, rig.Join("heavyweapons"));
        Assert.False(rig.Class.MenuOpen);
    }

    [Fact]
    public void The_same_class_does_nothing_for_a_living_player()
    {
        var rig = new Rig().As(TFClass.Scout);

        Assert.Equal(Outcome.Unchanged, rig.Join("scout"));
        Assert.Equal(0, rig.Respawns);
    }

    [Theory]
    [InlineData(TFTeam.Unassigned)]
    [InlineData(TFTeam.Spectator)]
    public void No_class_without_a_playing_team(TFTeam team)
    {
        var rig = new Rig();
        rig.Player.Engine.Returns("Players.GetTeam", (int)team);

        Assert.Equal(Outcome.Refused, rig.Join("scout"));
        Assert.Equal(TFClass.Undefined, rig.Class.Desired);
    }

    [Fact]
    public void No_class_at_game_over()
    {
        var rig = new Rig();
        rig.Round.State = RoundState.GameOver;

        Assert.Equal(Outcome.Refused, rig.Join("scout"));
    }

    [Fact]
    public void A_loser_in_the_winners_round_never_respawns_from_joinclass()
    {
        var rig = new Rig().Watching();
        rig.Round.State = RoundState.TeamWin;
        rig.Round.WinningTeam = TFTeam.Blue;

        Assert.Equal(Outcome.AtNextSpawn, rig.Join("scout"));
        Assert.Equal(TFClass.Scout, rig.Class.Desired);
        Assert.Equal(0, rig.Respawns);
    }

    [Fact]
    public void Civilian_and_unknown_names_are_stocks_warning()
    {
        var rig = new Rig().As(TFClass.Soldier);

        Assert.Equal(Outcome.Invalid, rig.Join("civilian"));
        Assert.Equal(Outcome.Invalid, rig.Join("wizard"));
        Assert.Equal(TFClass.Soldier, rig.Class.Desired);
        Assert.Contains(rig.Player.Log.Lines, l => l.Warning && l.Text.Contains("HandleCommand_JoinClass( civilian ) - invalid class name."));
    }

    [Fact]
    public void The_class_limit_counts_teammates_of_the_class()
    {
        var rig = new Rig().As(TFClass.Scout);
        rig.Inner.Red.Add(Teammate);
        rig.Limits.ClassLimit = 1;
        Assert.Equal(1, rig.Command.ClassLimit());

        // This player (a scout) does not count against their own choice; to
        // their teammate, the one scout fills the limit.
        Assert.Equal(Outcome.AtNextSpawn, rig.Join("heavyweapons"));
        Assert.True(rig.Command.CanPlayerChooseClass(PlayerRig.Player, (int)TFTeam.Red, TFClass.Scout));
        Assert.False(rig.Command.CanPlayerChooseClass(Teammate, (int)TFTeam.Red, TFClass.Scout));
    }

    [Fact]
    public void A_refused_named_choice_reopens_the_teams_class_panel()
    {
        var rig = new Rig().As(TFClass.Scout);
        rig.Teammates(TFTeam.Red, TFClass.Soldier);
        rig.Limits.ClassLimit = 1;

        Assert.Equal(Outcome.Limited, rig.Join("soldier"));

        Assert.Equal(new[] { ("class_red", true) }, rig.Menus());
        Assert.Equal(0, rig.Respawns);
    }

    [Fact]
    public void A_refused_named_choice_on_the_blue_team_reopens_their_class_panel()
    {
        var rig = new Rig().As(TFClass.Scout).On(TFTeam.Blue);
        rig.Teammates(TFTeam.Blue, TFClass.Soldier);
        rig.Limits.ClassLimit = 1;

        Assert.Equal(Outcome.Limited, rig.Join("soldier"));

        Assert.Equal(new[] { ("class_blue", true) }, rig.Menus());
    }

    [Fact]
    public void An_accepted_named_choice_reopens_no_panel()
    {
        var rig = new Rig().As(TFClass.Scout);

        Assert.Equal(Outcome.AtNextSpawn, rig.Join("heavyweapons"));

        Assert.Empty(rig.Menus());
    }

    [Fact]
    public void Random_with_no_class_left_brings_the_class_menu_up_again()
    {
        var rig = new Rig().As(TFClass.Scout);
        rig.Limits.ClassLimit = 1;
        // Every class including the current Scout is filled, so random has no
        // choice AND the current class is refused: stock brings the menu up.
        rig.Teammates(TFTeam.Red, TFClass.Scout, TFClass.Sniper, TFClass.Soldier, TFClass.Demoman,
                      TFClass.Medic, TFClass.HeavyWeapons, TFClass.Pyro, TFClass.Spy, TFClass.Engineer);

        Assert.Equal(Outcome.Limited, rig.Join("random"));

        Assert.Equal(new[] { ("class_red", true) }, rig.Menus());
    }

    [Fact]
    public void Random_with_every_other_class_full_but_the_current_one_keeps_silently()
    {
        var rig = new Rig().As(TFClass.Scout);
        rig.Limits.ClassLimit = 1;
        // Scout (current) has no teammate, so random has no OTHER choice but
        // the current class stands: stock's silent return, no menu.
        rig.Teammates(TFTeam.Red, TFClass.Sniper, TFClass.Soldier, TFClass.Demoman,
                      TFClass.Medic, TFClass.HeavyWeapons, TFClass.Pyro, TFClass.Spy, TFClass.Engineer);

        Assert.Equal(Outcome.Limited, rig.Join("random"));

        Assert.Empty(rig.Menus());
    }

    [Fact]
    public void Highlander_is_one_of_each()
    {
        var rig = new Rig();
        Assert.Equal(TFJoinClassCommand.NoClassLimit, rig.Command.ClassLimit());

        rig.Limits.Highlander = true;

        Assert.Equal(1, rig.Command.ClassLimit());
    }

    [Fact]
    public void Random_draws_another_class_the_limit_allows()
    {
        var rig = new Rig().As(TFClass.Scout);
        rig.Random.Next = 0;

        Assert.Equal(Outcome.AtNextSpawn, rig.Join("random"));

        Assert.Equal(TFClass.Sniper, rig.Class.Desired);
    }

    [Fact]
    public void A_living_player_cannot_change_class_in_a_stalemate()
    {
        var rig = new Rig().As(TFClass.Scout);
        rig.Round.State = RoundState.Stalemate;
        rig.Round.StalemateStartTime = 70f;   // the window (70-90) has passed by Now=100

        Assert.Equal(Outcome.Refused, rig.Join("heavyweapons"));
        Assert.Equal(TFClass.Scout, rig.Class.Desired);
    }

    [Fact]
    public void The_stalemate_class_change_window_respawns_the_living_and_the_dead()
    {
        var rig = new Rig().As(TFClass.Scout).Watching();
        rig.Round.State = RoundState.Stalemate;
        rig.Round.StalemateStartTime = 90f;   // Now=100 is inside the 20 s window

        Assert.Equal(Outcome.Respawned, rig.Join("heavyweapons"));
        Assert.Equal(1, rig.Respawns);

        var living = new Rig().As(TFClass.Scout);
        living.Round.State = RoundState.Stalemate;
        living.Round.StalemateStartTime = 90f;

        Assert.Equal(Outcome.Respawned, living.Join("heavyweapons"));
        Assert.Equal(1, living.Respawns);
    }

    [Fact]
    public void A_dead_player_outside_the_window_waits_for_the_next_spawn()
    {
        var rig = new Rig().As(TFClass.Scout).Watching();
        rig.Round.State = RoundState.Stalemate;
        rig.Round.StalemateStartTime = 70f;

        Assert.Equal(Outcome.AtNextSpawn, rig.Join("heavyweapons"));
        Assert.Equal(0, rig.Respawns);
    }

    [Fact]
    public void Another_verb_passes_to_the_next_hook()
    {
        var rig = new Rig();

        Assert.False(rig.Command.Handle(PlayerRig.Player, "jointeam", ["red"]));
        Assert.True(rig.Command.Handle(PlayerRig.Player, "joinclass", []));
    }
}
