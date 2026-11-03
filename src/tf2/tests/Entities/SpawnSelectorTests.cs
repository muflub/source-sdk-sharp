using SourceSharp.Abi;
using SourceSharp.Server;
using TF2.Server.Entities;
using TF2.Shared;
using TF2.Shared.Movement;

using Xunit;

namespace TF2.Tests.Entities;

/// <summary>
/// info_player_teamspawn and TFSpawnSelector (stage 2.4): a red or blue
/// player gets an enabled spawn of their own team and never the enemy's.
/// </summary>
public sealed class SpawnSelectorTests
{
    private sealed class FakeTeams : ITeams
    {
        public Dictionary<EntityHandle, int> Of { get; } = new();
        public int Team(EntityHandle player) => Of.TryGetValue(player, out int t) ? t : 0;
        public IReadOnlyList<EntityHandle> Players(int team) => [];
        public int Count(int team) => 0;
        public JoinTeamResult Join(EntityHandle player, int requested) => throw new NotSupportedException();
        public EntityHandle SelectSpawn(EntityHandle player) => EntityHandle.None;
    }

    private sealed class FakeClasses : TF2.Server.Econ.ITFPlayerClasses
    {
        public TFClass Class { get; set; }
        public TFClass ClassOf(EntityHandle player) => Class;
    }

    private static (ObjectiveRig Rig, TFSpawnSelector Selector, FakeTeams Teams) Rig(FakeClasses? classes = null)
    {
        var rig = new ObjectiveRig();
        var teams = new FakeTeams();
        var selector = new TFSpawnSelector(teams, rig.Entities, rig.Engine, rig.Engine, classes);
        return (rig, selector, teams);
    }

    /// <summary>
    /// SelectSpawnSpotByType's class pass (tf_player.cpp): a spawn whose
    /// spawnflags do not admit the player's class (bit class-1) is skipped in
    /// the first pass, and taken in the next only when no spawn admits it.
    /// </summary>
    [Fact]
    public void A_spawn_that_excludes_the_players_class_is_skipped()
    {
        var classes = new FakeClasses { Class = TFClass.Scout };
        var (rig, selector, teams) = Rig(classes);
        var soldiersOnly = Spawn(rig, TFTeam.Red);
        var scoutsOnly = Spawn(rig, TFTeam.Red);
        var flags = new Dictionary<EntityHandle, int>
        {
            [soldiersOnly.Handle] = 1 << ((int)TFClass.Soldier - 1),
            [scoutsOnly.Handle] = 1 << ((int)TFClass.Scout - 1),
        };
        rig.Engine.ReturnsEachCall("Entities.GetSpawnFlags",
            () => flags.GetValueOrDefault(rig.Engine.Calls[^1].ArgAs<EntityHandle>(0)));
        var player = new EntityHandle(7u | (1u << 16));
        teams.Of[player] = (int)TFTeam.Red;

        Assert.Equal(scoutsOnly.Handle, selector.SelectSpawn(player));

        // The control: a soldier walks on from the scout spawn to the first one,
        // which admits soldiers; and a class no spawn admits still gets one.
        classes.Class = TFClass.Soldier;
        Assert.Equal(soldiersOnly.Handle, selector.SelectSpawn(player));
        classes.Class = TFClass.Medic;
        Assert.NotEqual(EntityHandle.None, selector.SelectSpawn(player));
    }

    [Fact]
    public void The_selectors_hull_is_the_mods_one_copy_of_the_tf_view_vectors()
    {
        Assert.Equal(TFViewVectors.Params.HullMin, TFSpawnSelector.HullMin);
        Assert.Equal(new Vector(24f, 24f, 82f), TFSpawnSelector.HullMax);
    }

    private static InfoPlayerTeamSpawn Spawn(ObjectiveRig rig, TFTeam team, bool disabled = false, string point = "")
        => rig.Spawn<InfoPlayerTeamSpawn>(InfoPlayerTeamSpawn.MapClass, ("TeamNum", ((int)team).ToString()),
                                          ("StartDisabled", disabled ? "1" : "0"), ("controlpoint", point));

    [Fact]
    public void A_player_gets_a_spawn_of_their_own_team()
    {
        var (rig, selector, teams) = Rig();
        Spawn(rig, TFTeam.Blue);
        var red = Spawn(rig, TFTeam.Red);
        var player = new EntityHandle(7u | (1u << 16));
        teams.Of[player] = (int)TFTeam.Red;

        Assert.Equal(red.Handle, selector.SelectSpawn(player));
    }

    [Fact]
    public void A_player_never_gets_the_enemys_spawn()
    {
        var (rig, selector, teams) = Rig();
        Spawn(rig, TFTeam.Blue);
        Spawn(rig, TFTeam.Blue);
        var player = new EntityHandle(7u | (1u << 16));
        teams.Of[player] = (int)TFTeam.Red;

        Assert.Equal(EntityHandle.None, selector.SelectSpawn(player));
    }

    [Fact]
    public void A_disabled_spawn_is_passed_over()
    {
        var (rig, selector, teams) = Rig();
        Spawn(rig, TFTeam.Red, disabled: true);
        var on = Spawn(rig, TFTeam.Red);
        var player = new EntityHandle(7u | (1u << 16));
        teams.Of[player] = (int)TFTeam.Red;

        Assert.Equal(on.Handle, selector.SelectSpawn(player));

        rig.Registry.Input(on.Handle, "Disable", string.Empty, 0f, EntityHandle.None);
        Assert.Equal(EntityHandle.None, selector.SelectSpawn(player));
    }

    [Fact]
    public void Successive_spawns_walk_on_from_the_last()
    {
        var (rig, selector, teams) = Rig();
        var first = Spawn(rig, TFTeam.Red);
        var second = Spawn(rig, TFTeam.Red);
        var player = new EntityHandle(7u | (1u << 16));
        teams.Of[player] = (int)TFTeam.Red;

        Assert.Equal(first.Handle, selector.SelectSpawn(player));
        Assert.Equal(second.Handle, selector.SelectSpawn(player));
        Assert.Equal(first.Handle, selector.SelectSpawn(player));
    }

    [Fact]
    public void A_spectator_is_left_to_the_cores_walk()
    {
        var (rig, selector, teams) = Rig();
        Spawn(rig, TFTeam.Spectator);
        var player = new EntityHandle(7u | (1u << 16));
        teams.Of[player] = (int)TFTeam.Spectator;

        Assert.Equal(EntityHandle.None, selector.SelectSpawn(player));
    }

    [Fact]
    public void A_blocked_hull_is_used_only_when_every_spawn_is_blocked()
    {
        var (rig, selector, teams) = Rig();
        var only = Spawn(rig, TFTeam.Red);
        var player = new EntityHandle(7u | (1u << 16));
        teams.Of[player] = (int)TFTeam.Red;
        rig.Engine.Returns("WorldQuery.TraceHull", new HullResult(true, true, EntityHandle.None));

        // Blocked against the world in every pass: nothing.
        Assert.Equal(EntityHandle.None, selector.SelectSpawn(player));

        rig.Engine.Returns("WorldQuery.TraceHull", new HullResult(false, false, EntityHandle.None));
        Assert.Equal(only.Handle, selector.SelectSpawn(player));
    }

    [Fact]
    public void The_spawns_of_a_teams_farthest_point_are_the_live_ones()
    {
        var rig = new ObjectiveRig();
        rig.Point("cp_a", 0, TFTeam.Red);
        rig.Point("cp_b", 1, TFTeam.Red);
        rig.Point("cp_c", 2, TFTeam.Blue);
        var atA = Spawn(rig, TFTeam.Red, point: "cp_a");
        var atB = Spawn(rig, TFTeam.Red, point: "cp_b");
        rig.Master();
        rig.StartRound();

        rig.Rules.SetupControlPointSpawns();

        Assert.True(atA.Disabled);
        Assert.False(atB.Disabled);
    }

    /// <summary>
    /// The core's walk kills for a taken spot on stock HL2MP's rule, and a TF2 spectator
    /// reaches that walk (the selector answers nothing): CTFPlayer has no such kill. The
    /// mark is what registers the rule on the server side; its answer is what the walk asks.
    /// </summary>
    [Fact]
    public void TF2RefusesTheCoresSpawnKillOnTheServerSide()
    {
        SideServiceAttribute mark = Assert.Single(typeof(TFSpawnNoFrag).GetCustomAttributes(typeof(SideServiceAttribute), false).Cast<SideServiceAttribute>());
        Assert.Equal(Side.Server, mark.Side);
        Assert.Contains(typeof(ISpawnFragRule), mark.As);
        Assert.False(new TFSpawnNoFrag().Frags(new EntityHandle(0x010001)));
    }
}
