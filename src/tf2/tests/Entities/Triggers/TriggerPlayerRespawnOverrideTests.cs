using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Entities;
using TF2.Server.Entities.Triggers;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Tests.Entities.Items;

using Xunit;

namespace TF2.Tests.Entities.Triggers;

/// <summary>
/// trigger_player_respawn_override, the player's respawn override pair and the
/// death's walk between them: stock's <c>CTriggerPlayerRespawnOverride</c>
/// (tf_triggers.cpp:515-527), the <c>CTFPlayer</c> pair it feeds
/// (tf_player.h:1090-1092, :1449-1450) and the <c>OnDeath</c> walk that sets
/// it (tf_player.cpp:12884-12901).
/// </summary>
public sealed class TriggerPlayerRespawnOverrideTests
{
    private static (MapItemRig Rig, TFPlayerCarriers Carriers, TriggerPlayerRespawnOverride Trigger,
                    TFRespawnOverrideWalk Walk, TFRespawnOverride Overrides, (EntityHandle Handle, FakeMapPlayer Fake) Player)
        Rig(params (string, string)[] keys)
    {
        var rig = new MapItemRig();
        var carriers = new TFPlayerCarriers();
        rig.Services.AddInstance(carriers);
        var trigger = rig.Spawn<TriggerPlayerRespawnOverride>(TriggerPlayerRespawnOverride.MapClass, keys);
        var player = rig.Players.Add();
        var overrides = new TFRespawnOverride(carriers, rig.Clock);
        overrides.Attach(player.Handle);
        return (rig, carriers, trigger, new TFRespawnOverrideWalk(rig.Registry, carriers), overrides, player);
    }

    private static void Die(TFRespawnOverrideWalk walk, EntityHandle player)
        => walk.OnPlayerDied(new PlayerDeath(player, player.EntryIndex, EntityHandle.None, Vector.Zero, EntityHandle.None));

    [Fact]
    public void The_trigger_starts_at_stocks_zeroed_members()
    {
        var (_, _, trigger, _, _, _) = Rig();

        Assert.Equal(0f, trigger.RespawnVars.RespawnTime);            // no constructor: the float stands 0
        Assert.Equal(string.Empty, trigger.RespawnVars.RespawnName);  // and the name NULL_STRING
    }

    [Fact]
    public void A_player_inside_the_volume_takes_the_pairs_time_and_spot()
    {
        var (rig, _, trigger, walk, ov, player) = Rig(("RespawnTime", "5"), ("RespawnName", "red_room"));
        rig.Touch(trigger, player.Handle);

        rig.Clock.Now = 30f;
        Die(walk, player.Handle);

        Assert.True(ov.HasOverride);
        Assert.Equal(5f, ov.OverrideTime);
        Assert.Equal("red_room", ov.Location);
        Assert.Equal(35f, ov.ReleaseAt); // the walk's frame plus the time
    }

    [Fact]
    public void A_player_the_volume_never_held_is_cleared_at_death()
    {
        var (rig, _, _, walk, ov, player) = Rig(("RespawnTime", "5"));
        ov.Apply(7f, "x"); // the old life's pair, standing until the death walk says otherwise

        Die(walk, player.Handle);

        Assert.False(ov.HasOverride);
        Assert.Null(ov.Location);
    }

    [Fact]
    public void A_disabled_volume_releases_the_player()
    {
        var (rig, _, trigger, walk, ov, player) = Rig(("RespawnTime", "5"), ("RespawnName", "room"));
        rig.Touch(trigger, player.Handle);
        rig.Input(trigger, "Disable");

        Die(walk, player.Handle);

        Assert.False(ov.HasOverride);
    }

    [Fact]
    public void The_inputs_set_the_pair()
    {
        var (rig, _, trigger, walk, ov, player) = Rig(("RespawnTime", "5"), ("RespawnName", "red_room"));
        rig.Touch(trigger, player.Handle);
        rig.Registry.Input(trigger.Handle, "SetRespawnTime", "0", 0f, EntityHandle.None);
        rig.Registry.Input(trigger.Handle, "SetRespawnName", "blue_room", 0f, EntityHandle.None);

        rig.Clock.Now = 50f;
        Die(walk, player.Handle);

        Assert.Equal(0f, trigger.RespawnVars.RespawnTime);
        Assert.Equal("blue_room", trigger.RespawnVars.RespawnName);
        Assert.Equal(50f, ov.ReleaseAt); // the time the input left behind, off this frame
        Assert.Equal("blue_room", ov.Location);
    }

    [Fact]
    public void The_volume_that_let_go_does_not_hold_the_dead_player()
    {
        var (rig, _, trigger, walk, ov, player) = Rig(("RespawnTime", "5"));
        ov.Apply(7f, "x");
        rig.Touch(trigger, player.Handle);
        rig.Registry.EndTouch(trigger.Handle, player.Handle);

        Die(walk, player.Handle);

        Assert.False(ov.HasOverride);
    }

    [Fact]
    public void The_first_volume_holding_the_dead_player_wins_the_pair()
    {
        var rig = new MapItemRig();
        var carriers = new TFPlayerCarriers();
        rig.Services.AddInstance(carriers);
        var first = rig.Spawn<TriggerPlayerRespawnOverride>(TriggerPlayerRespawnOverride.MapClass,
                                                            ("RespawnTime", "5"), ("RespawnName", "a_room"));
        var second = rig.Spawn<TriggerPlayerRespawnOverride>(TriggerPlayerRespawnOverride.MapClass,
                                                             ("RespawnTime", "9"), ("RespawnName", "b_room"));
        var player = rig.Players.Add();
        var ov = new TFRespawnOverride(carriers, rig.Clock);
        ov.Attach(player.Handle);
        var walk = new TFRespawnOverrideWalk(rig.Registry, carriers);
        rig.Touch(first, player.Handle);
        rig.Touch(second, player.Handle);

        Die(walk, player.Handle);

        Assert.Equal(5f, ov.OverrideTime); // :12896: the first match, with the break
        Assert.Equal("a_room", ov.Location);
    }

    // ---- the pair itself ----

    [Fact]
    public void The_pair_answers_nothing_before_a_death()
    {
        var rig = new MapItemRig();
        var carriers = new TFPlayerCarriers();
        var player = rig.Players.Add();
        var ov = new TFRespawnOverride(carriers, rig.Clock);
        ov.Attach(player.Handle);

        Assert.False(ov.HasOverride);
        Assert.Equal(-1f, ov.OverrideTime); // the pair's "no override" answer
        Assert.Null(ov.Location);
    }

    [Fact]
    public void The_pair_clears_on_spawn()
    {
        var rig = new MapItemRig();
        var carriers = new TFPlayerCarriers();
        var player = rig.Players.Add();
        var ov = new TFRespawnOverride(carriers, rig.Clock);
        ov.Attach(player.Handle);
        ov.Apply(120f, "room");

        ov.ClearOnSpawn(); // CTFPlayer::Spawn's SetRespawnOverride(-1.f, NULL_STRING)

        Assert.False(ov.HasOverride);
        Assert.Null(ov.Location);
        Assert.Equal(0f, ov.ReleaseAt);
    }

    // ---- the spawn walk's named-spot arm (tf_player.cpp:5745-5757) ----

    private sealed class FakeTeams : ITeams
    {
        public Dictionary<EntityHandle, int> Of { get; } = new();
        public int Team(EntityHandle player) => Of.TryGetValue(player, out int t) ? t : 0;
        public IReadOnlyList<EntityHandle> Players(int team) => [];
        public int Count(int team) => 0;
        public JoinTeamResult Join(EntityHandle player, int requested) => throw new NotSupportedException();
        public EntityHandle SelectSpawn(EntityHandle player) => EntityHandle.None;
    }

    private static InfoPlayerTeamSpawn TeamSpawn(ObjectiveRig rig, TFTeam team, string? name = null)
        => rig.Spawn<InfoPlayerTeamSpawn>(InfoPlayerTeamSpawn.MapClass,
            ("TeamNum", ((int)team).ToString()),
            ("targetname", name ?? string.Empty));

    [Fact]
    public void A_triggered_player_gets_the_named_spawn_when_it_is_valid()
    {
        var rig = new ObjectiveRig();
        var teams = new FakeTeams();
        var carriers = new TFPlayerCarriers();
        TeamSpawn(rig, TFTeam.Red, "plain"); // the walk's own answer, met first
        var named = TeamSpawn(rig, TFTeam.Red, "red_room");
        var selector = new TFSpawnSelector(teams, rig.Entities, rig.Engine, rig.Engine, null, carriers, rig.Registry);
        var player = rig.NewHandle();
        teams.Of[player] = (int)TFTeam.Red;
        var ov = new TFRespawnOverride(carriers, rig.Clock);
        ov.Attach(player);
        ov.Apply(30f, "red_room");

        Assert.Equal(named.Handle, selector.SelectSpawn(player));
    }

    [Fact]
    public void A_named_spawn_the_player_cannot_use_falls_through_to_the_walk()
    {
        var rig = new ObjectiveRig();
        var teams = new FakeTeams();
        var carriers = new TFPlayerCarriers();
        TeamSpawn(rig, TFTeam.Blue, "blue_room"); // not this red player's spot
        var redSpawn = TeamSpawn(rig, TFTeam.Red);
        var selector = new TFSpawnSelector(teams, rig.Entities, rig.Engine, rig.Engine, null, carriers, rig.Registry);
        var player = rig.NewHandle();
        teams.Of[player] = (int)TFTeam.Red;
        var ov = new TFRespawnOverride(carriers, rig.Clock);
        ov.Attach(player);
        ov.Apply(30f, "blue_room");

        Assert.Equal(redSpawn.Handle, selector.SelectSpawn(player));
    }

    [Fact]
    public void A_player_without_the_pair_walks_the_team_spawns_as_usual()
    {
        var rig = new ObjectiveRig();
        var teams = new FakeTeams();
        var carriers = new TFPlayerCarriers();
        var first = TeamSpawn(rig, TFTeam.Red, "plain");
        TeamSpawn(rig, TFTeam.Red, "red_room"); // the name stands, but the pair does not
        var selector = new TFSpawnSelector(teams, rig.Entities, rig.Engine, rig.Engine, null, carriers, rig.Registry);
        var player = rig.NewHandle();
        teams.Of[player] = (int)TFTeam.Red;
        var ov = new TFRespawnOverride(carriers, rig.Clock);
        ov.Attach(player); // attached, never applied

        Assert.Equal(first.Handle, selector.SelectSpawn(player));
    }
}
