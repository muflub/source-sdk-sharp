using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Ai.Roster;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Horde;

using Xunit;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The kill seam (<see cref="RobotKills"/>, HE5): every robot death is told to
/// <see cref="IRobotKillHook"/> exactly once, by its killing hit (the core's
/// entity death after <c>RobotBodies.Hit</c>) or, for a death no hit reported
/// (a burn far from any hero), by the frame's sweep of the roster; each with
/// the level's next kill number, which a new level's roster starts again.
/// </summary>
public sealed class RobotKillsTests
{
    private static readonly EntityHandle Shooter = new(0x0001_0000u | 2);

    private sealed class Rosters(ServerHordeRig rig) : IKillRoster
    {
        public RobotRoster? Roster { get; set; } = rig.Roster;
    }

    private sealed class Told : IRobotKillHook
    {
        public List<RobotKill> Kills { get; } = [];
        public void OnRobotKilled(in RobotKill kill) => Kills.Add(kill);
    }

    private sealed class OneHook(IRobotKillHook hook) : IHooks<IRobotKillHook>
    {
        public IReadOnlyList<IRobotKillHook> All => [hook];
        public void Each(string what, Action<IRobotKillHook> call) => call(hook);
    }

    private sealed record Kit(ServerHordeRig Rig, Rosters Rosters, Told Told, RobotKills Kills);

    private static Kit Make()
    {
        var rig = new ServerHordeRig();
        rig.Engine.Returns("Entities.IsValid", true);
        rig.Engine.ReturnsEachCall("Entities.IsPlayer", () => rig.Engine.Calls[^1].ArgAs<EntityHandle>(0) == Shooter);
        var rosters = new Rosters(rig);
        var told = new Told();
        return new Kit(rig, rosters, told, new RobotKills(rosters, rig.Registry, rig.Engine, new OneHook(told), rig.Log));
    }

    /// <summary>A killing hit on <paramref name="body"/> by <paramref name="attacker"/>, then the shell's death as the core tells it.</summary>
    private static void Shoot(Kit k, RobotBody body, EntityHandle attacker)
    {
        body.OnDamagedAt(attacker, 1_000_000f, DamageType.Blast, default, default, HitGroup.Chest);
        k.Kills.OnEntityDied(new EntityDeath(body.Handle, attacker, new Vector(10, 20, 30)));
    }

    /// <summary>A death no hit reports: the row's life burned away (a status effect far from any hero).</summary>
    private static void Burn(Kit k, int row) => Assert.True(k.Rig.Roster.Hurt(row, k.Rig.Roster.Life(row)));

    [Fact]
    public void A_killing_hit_is_told_once_with_its_killer_and_where_it_died()
    {
        var k = Make();
        var (id, body) = k.Rig.Lent("soldier");

        Shoot(k, body, Shooter);
        k.Kills.Sweep();

        var kill = Assert.Single(k.Told.Kills);
        Assert.Equal(id, kill.Robot);
        Assert.Equal("soldier", kill.Template);
        Assert.Equal(2, kill.Killer);
        Assert.Equal(Shooter, kill.Attacker);
        Assert.Equal(new Vector(10, 20, 30), kill.Position);
        Assert.Equal(k.Rig.Roster.Stats(id.Index), kill.Stats);
        Assert.Equal(k.Rig.Roster.MonsterLevel(id.Index), kill.Level);
    }

    [Fact]
    public void A_death_with_no_hit_is_told_once_by_the_sweep()
    {
        var k = Make();
        int row = 0;
        Assert.NotEqual(RobotState.Dead, k.Rig.Roster.State(row));

        Burn(k, row);
        int first = k.Kills.Sweep();
        int second = k.Kills.Sweep();

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        var kill = Assert.Single(k.Told.Kills);
        Assert.Equal(k.Rig.Roster.Id(row), kill.Robot);
        Assert.Equal(0, kill.Killer);
        Assert.False(kill.ByPlayer);
    }

    [Fact]
    public void The_frame_sweeps_the_roster()
    {
        var k = Make();

        Burn(k, 0);
        ((IFrameLifecycle)k.Kills).OnThink(0.015f);

        Assert.Single(k.Told.Kills);
    }

    [Fact]
    public void A_body_death_after_the_sweep_told_it_is_not_told_again()
    {
        var k = Make();
        var (_, body) = k.Rig.Lent("soldier");
        body.OnDamagedAt(Shooter, 1_000_000f, DamageType.Blast, default, default, HitGroup.Chest);

        k.Kills.Sweep();
        k.Kills.OnEntityDied(new EntityDeath(body.Handle, Shooter, default));

        Assert.Single(k.Told.Kills);
    }

    [Fact]
    public void A_world_kill_has_no_killer()
    {
        var k = Make();
        var (_, body) = k.Rig.Lent("soldier");

        Shoot(k, body, EntityHandle.None);

        Assert.Equal(0, Assert.Single(k.Told.Kills).Killer);
    }

    [Fact]
    public void A_body_whose_robot_still_lives_tells_nothing()
    {
        var k = Make();
        var (_, body) = k.Rig.Lent("soldier");

        k.Kills.OnEntityDied(new EntityDeath(body.Handle, Shooter, default));

        Assert.Empty(k.Told.Kills);
    }

    [Fact]
    public void Kill_numbers_count_up_from_zero_and_never_repeat_within_a_level()
    {
        var k = Make();
        var (_, body) = k.Rig.Lent("soldier");

        Burn(k, 0);
        k.Kills.Sweep();
        Shoot(k, body, Shooter);
        Burn(k, 1);
        Burn(k, 2);
        k.Kills.Sweep();

        Assert.Equal([0u, 1u, 2u, 3u], k.Told.Kills.Select(x => x.Sequence));
        Assert.Equal(4, k.Told.Kills.Select(x => x.Robot).Distinct().Count());
        Assert.Equal(4u, k.Kills.Told);
    }

    [Fact]
    public void A_new_levels_roster_starts_the_kill_numbers_again()
    {
        var k = Make();
        Burn(k, 0);
        Burn(k, 1);
        k.Kills.Sweep();

        var next = new ServerHordeRig();
        k.Rosters.Roster = next.Roster;
        Assert.True(next.Roster.Hurt(0, next.Roster.Life(0)));
        k.Kills.Sweep();

        Assert.Equal([0u, 1u, 0u], k.Told.Kills.Select(x => x.Sequence));
    }

    [Fact]
    public void A_level_with_no_horde_tells_nothing()
    {
        var k = Make();
        Burn(k, 0);
        k.Rosters.Roster = null;

        Assert.Equal(0, k.Kills.Sweep());
        Assert.Empty(k.Told.Kills);
    }
}
