using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>What a player wears, by cl_playermodel (CHL2MP_Player::SetPlayerModel and its neighbours).</summary>
public class PlayerModelsTests
{
    private const int Index = 3;
    private const string Female = "models/humans/group03/female_02.mdl";
    private const string Police = "models/police.mdl";

    private static readonly EntityHandle Player = new(0x0001_0003u);

    private sealed class Clients : IConnectedClients
    {
        public float? AvgLatency(int entIndex) => null;
        public string? Asked;
        public readonly List<string> Commands = [];
        public ulong SteamId64(int entIndex) => 0;
        public string? Address(int entIndex) => null;
        public string? ClientConVar(int entIndex, string name) => name == PlayerModels.ConVarName ? Asked : null;
        public int UserId(int entIndex) => entIndex + 10;
        public void Kick(int entIndex, string reason) { }
        public void ClientCommand(int entIndex, string command) => Commands.Add(command);
        public void Reconnect(int entIndex) { }
    }

    private sealed class Said : IChat
    {
        public readonly List<string> Lines = [];
        public void PrintAll(string text) => Lines.Add(text);
        public void Print(int entIndex, string text) => Lines.Add(text);
    }

    private sealed class Moves : ITeams
    {
        public readonly List<int> Joined = [];
        public int Team(EntityHandle player) => 0;
        public IReadOnlyList<EntityHandle> Players(int team) => [];
        public int Count(int team) => 0;
        public JoinTeamResult Join(EntityHandle player, int requested)
        {
            Joined.Add(requested);
            return default;
        }
        public EntityHandle SelectSpawn(EntityHandle player) => EntityHandle.None;
    }

    private sealed class Owners(params IPlayerModelOwner[] owners) : IHooks<IPlayerModelOwner>
    {
        public IReadOnlyList<IPlayerModelOwner> All => owners;
        public void Each(string what, Action<IPlayerModelOwner> call) { foreach (var o in owners) call(o); }
    }

    private sealed class Owner : IPlayerModelOwner;

    private sealed class Rig
    {
        public required PlayerModels Models;
        public required RecordingEngine Engine;
        public required Clients Clients;
        public required Said Said;
        public required Moves Teams;
        public float Now = 100f;

        public string? LastWorn => Engine.Last("EntityRender.SetModel")?.ArgAs<string>(1);
    }

    private static Rig Make(string? asked = Female, int team = 0, bool teamplay = false, params IPlayerModelOwner[] owners)
    {
        var engine = Fake();
        engine.Strict = false;
        var rig = new Rig
        {
            Models = null!, Engine = engine, Clients = new Clients { Asked = asked },
            Said = new Said(), Teams = new Moves(),
        };
        engine.Returns("Entities.GetIndex", Index).Returns("Players.GetHandle", Player)
              .Returns("Players.GetTeam", team).ReturnsEachCall("Clock.Now", () => rig.Now);
        var convars = ConVarRegistry.For(UnattachedConsole());
        convars.RegisterAll(typeof(PlayerModels).Assembly);
        if (teamplay)
            convars.Get<TeamPlayConVar>()!.NotifyChanged("1", "0");
        rig.Models = new PlayerModels(rig.Clients, engine, engine, engine, engine, engine, rig.Said, rig.Teams, convars,
                                      owners.Length == 0 ? null : new Owners(owners));
        return rig;
    }

    [Fact]
    public void A_player_spawns_in_the_model_they_asked_for()
    {
        Assert.Equal(Female, Make().Models.ForSpawn(Player));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("models/kleiner.mdl")]
    [InlineData(null)]
    public void A_choice_off_the_lists_falls_back_and_is_written_back(string? asked)
    {
        var rig = Make(asked);

        Assert.Equal(PlayerModels.Fallback, rig.Models.ForSpawn(Player));
        Assert.Equal([$"cl_playermodel {PlayerModels.Fallback}\n"], rig.Clients.Commands);
    }

    [Fact]
    public void The_lists_answer_whatever_the_case_the_client_wrote()
    {
        Assert.True(PlayerModels.IsValid("MODELS/Police.mdl"));
        Assert.False(PlayerModels.IsValid("models/alyx.mdl"));
    }

    [Fact]
    public void A_respawn_keeps_the_model_without_asking_again()
    {
        var rig = Make();
        rig.Models.ForSpawn(Player);
        rig.Clients.Asked = Police;

        Assert.Equal(Female, rig.Models.ForSpawn(Player));
    }

    [Fact]
    public void A_change_after_the_cooldown_is_worn_and_said()
    {
        var rig = Make();
        rig.Models.ForSpawn(Player);
        rig.Clients.Asked = Police;
        rig.Now += PlayerModels.ChangeInterval + 0.1f;

        rig.Models.SettingsChanged(Index);

        Assert.Equal(Police, rig.LastWorn);
        Assert.Equal([$"Your player model is: {Police}\n"], rig.Said.Lines);
        Assert.Equal(Police, rig.Models.ForSpawn(Player));
    }

    [Fact]
    public void A_change_inside_the_cooldown_is_refused_and_the_convar_put_back()
    {
        var rig = Make();
        rig.Models.ForSpawn(Player);
        rig.Clients.Asked = Police;
        rig.Now += 2f;

        rig.Models.SettingsChanged(Index);

        Assert.Null(rig.LastWorn);
        Assert.Equal([$"cl_playermodel {Female}\n"], rig.Clients.Commands);
        Assert.Equal(["Please wait 3 more seconds before trying to switch.\n"], rig.Said.Lines);
        Assert.Equal(Female, rig.Models.ForSpawn(Player));
    }

    [Fact]
    public void A_settings_change_that_leaves_the_model_alone_does_nothing()
    {
        var rig = Make();
        rig.Models.ForSpawn(Player);
        rig.Clients.Asked = Female.ToUpperInvariant();
        rig.Now += PlayerModels.ChangeInterval + 0.1f;

        rig.Models.SettingsChanged(Index);

        Assert.Null(rig.LastWorn);
        Assert.Empty(rig.Said.Lines);
    }

    [Fact]
    public void A_settings_change_before_the_first_spawn_waits_for_it()
    {
        var rig = Make();

        rig.Models.SettingsChanged(Index);

        Assert.Null(rig.LastWorn);
        Assert.Empty(rig.Said.Lines);
    }

    [Fact]
    public void A_team_in_deathmatch_does_not_override_the_choice()
    {
        Assert.Equal(Female, Make(team: PlayerModels.TeamCombine).Models.ForSpawn(Player));
    }

    /// <summary>SetPlayerTeamModel: a human on Combine, or a soldier on the rebels, takes the side's next model.</summary>
    [Theory]
    [InlineData(Female, PlayerModels.TeamCombine, "models/combine_soldier_prisonguard.mdl")]
    [InlineData(Police, PlayerModels.TeamRebels, "models/humans/group03/male_02.mdl")]
    [InlineData(Police, PlayerModels.TeamCombine, Police)]
    [InlineData(Female, PlayerModels.TeamRebels, Female)]
    public void Teamplay_dresses_a_player_for_their_side(string asked, int team, string worn)
    {
        Assert.Equal(worn, Make(asked, team, teamplay: true).Models.ForSpawn(Player));
    }

    /// <summary>ClientSettingsChanged's teamplay arm: the model picks the side.</summary>
    [Theory]
    [InlineData(Female, PlayerModels.TeamRebels)]
    [InlineData(Police, PlayerModels.TeamCombine)]
    public void In_teamplay_a_new_model_changes_sides(string asked, int side)
    {
        var rig = Make("models/combine_super_soldier.mdl", PlayerModels.TeamCombine, teamplay: true);
        rig.Models.ForSpawn(Player);
        rig.Clients.Asked = asked;
        rig.Now += PlayerModels.ChangeInterval + 0.1f;

        rig.Models.SettingsChanged(Index);

        Assert.Equal([side], rig.Teams.Joined);
    }

    [Fact]
    public void A_team_change_dresses_a_spawned_player_again()
    {
        var rig = Make();
        rig.Models.ForSpawn(Player);
        rig.Clients.Asked = Police;

        rig.Models.TeamChanged(Player, 0, 1);

        Assert.Equal(Police, rig.LastWorn);
    }

    [Fact]
    public void Both_lists_are_precached()
    {
        var rig = Make();

        ((IGameLifecycle)rig.Models).OnLevelInitPreEntity("dm_lockdown");

        var precached = rig.Engine.Calls.Where(c => c.Member == "Precache.PrecacheModel").Select(c => c.ArgAs<string>(0)).ToList();
        Assert.Equal(PlayerModels.Citizens.Count + PlayerModels.Combine.Count, precached.Count);
        Assert.Contains(Police, precached);
        Assert.Contains(Female, precached);
    }

    /// <summary>A class game's players never wear cl_playermodel, and its server never loads these models.</summary>
    [Fact]
    public void A_mod_that_dresses_its_own_players_turns_all_of_it_off()
    {
        var rig = Make(owners: new Owner());

        ((IGameLifecycle)rig.Models).OnLevelInitPreEntity("ctf_2fort");

        Assert.Null(rig.Models.ForSpawn(Player));
        rig.Models.SettingsChanged(Index);
        Assert.Empty(rig.Clients.Commands);
        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member == "Precache.PrecacheModel");
    }
}
