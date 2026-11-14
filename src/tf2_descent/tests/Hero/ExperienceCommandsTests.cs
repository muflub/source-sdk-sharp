using SourceSharp.Abi;
using SourceSharp.Host.Sdk;
using SourceSharp.Shared;

using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules.Bestiary;
using TF2.Descent.Server;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Horde;
using TF2.Descent.Server.Mode;
using TF2.Descent.Tests.Horde;

using Xunit;

using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Hero;

/// <summary>HE5's dev commands: <c>tf2_descent_xp</c> keeps the evidence honest; <c>tf2_descent_kill_robot</c> runs the kill chain.</summary>
public sealed class ExperienceCommandsTests
{
    private static SvCheatsConVar Cheats => DeclaredConVars.Holding<SvCheatsConVar>("1");

    private sealed class NoCheckpoints : IHeroLevelCheckpoint
    {
        public bool Checkpoint(int entIndex) => false;
    }

    private static (HeroRig Rig, HeroComponent Hero, HeroXpCommand Command) Xp(HostingMode mode = HostingMode.Unhosted)
    {
        var rig = new HeroRig(mode);
        if (mode == HostingMode.Hosted)
            Assert.Null(rig.Sheets.Leased(HeroRig.Index, "76561197960287931", null, "soldier", "Jane"));
        rig.Engine.Returns("Players.IsAlive", true);
        var hero = rig.Join()!;
        var xp = new HeroExperience(rig.Heroes, rig.Sheets, rig.Engine, rig.Engine, new ClientMessages(rig.Engine), new NoCheckpoints(), rig.Log);
        return (rig, hero, new HeroXpCommand(Cheats, rig.Heroes, xp, rig.Host, rig.Rules, rig.Log));
    }

    [Fact]
    public void A_dev_grant_raises_the_heros_xp_by_the_rules()
    {
        var (rig, hero, command) = Xp();
        long amount = rig.Rules.Heroes.XpAtLevel(3);

        command.Execute(HeroRig.Index, (int)amount);

        Assert.Equal(amount, hero.Sheet!.Xp);
        Assert.Equal(3, hero.Sheet.Level);
    }

    [Fact]
    public void A_dev_grants_evidence_is_explained_by_the_kills_it_records_and_counts_none_in_the_bestiary()
    {
        var (rig, hero, command) = Xp();
        const int amount = 123_456;

        command.Execute(HeroRig.Index, amount);

        var pending = rig.Sheets.Pending(HeroRig.Index);
        var (template, _) = HeroXpCommand.DevTemplate(rig.Rules);
        Assert.Equal(amount, pending.XpEarned);
        Assert.True(pending.KillsOf(template) > 0);
        Assert.True(pending.XpEarned <= rig.Rules.Checkpoints.MostXpFor(pending), "the evidence's XP is within what its kills could pay");
        Assert.Equal(0, BestiaryRules.Kills(hero.Sheet!, template));
    }

    [Fact]
    public void A_dev_grant_is_refused_hosted()
    {
        var (_, hero, command) = Xp(HostingMode.Hosted);
        Assert.NotNull(hero.Sheet);

        command.Execute(HeroRig.Index, 500);

        Assert.Equal(0, hero.Sheet!.Xp);
    }

    //-------------------------------------------------------------------------

    private sealed class Mode : IDescentModeState
    {
        public bool Active => true;
    }

    private sealed class Levels : IHordeLevelSource
    {
        public TF2.Descent.Ai.Level.HordeLevel? LevelFor(string mapName, int depth, ulong seed) => Row(3);
    }

    private sealed class Clock : IHordeClock
    {
        public long Tick { get; set; } = 500;
        public double TicksPerSecond => ServerHordeRig.Tps;
    }

    private sealed class Told : IRobotKillHook
    {
        public List<RobotKill> Kills { get; } = [];
        public void OnRobotKilled(in RobotKill kill) => Kills.Add(kill);
    }

    private sealed record Kit(ServerHordeRig Rig, HordeService Horde, Told Told, KillRobotCommand Command);

    private static Kit Horde()
    {
        var rig = new ServerHordeRig(begin: false);
        var horde = new HordeService(new Mode(), new Levels(), rig.Spawner, new Clock(), rig.Parking,
                                     new HordeCensus(rig.Spawner, rig.Parking, rig.Log), new HordeTickTimer(), rig.Log);
        horde.OnLevelReady("tf2_descent-1-s1");
        Assert.True(horde.Current!.Begun);
        var told = new Told();
        var kills = new RobotKills(new HordeKillRoster(horde), rig.Registry, rig.Engine, new HeroRig.ListHooks<IRobotKillHook>([told]), rig.Log);
        return new Kit(rig, horde, told, new KillRobotCommand(horde, kills, rig.Engine, rig.Engine, rig.Log));
    }

    [Fact]
    public void Killing_a_robot_with_no_body_tells_the_kill_once()
    {
        var k = Horde();
        var roster = k.Horde.Current!.Roster!;
        Assert.Equal(0, roster.Body(0));

        k.Command.Execute(0);
        k.Command.Execute(0);

        Assert.Equal(RobotState.Dead, roster.State(0));
        Assert.Equal(roster.Id(0), Assert.Single(k.Told.Kills).Robot);
    }

    [Fact]
    public void Killing_a_robot_with_a_body_hits_the_body_through_the_engine()
    {
        var k = Horde();
        var c = k.Horde.Current!;
        Assert.True(c.LendByHand(1, 500, out string why), why);
        var body = c.Bodies!.Body(c.Roster!.Body(1));

        k.Command.Execute(1);

        var hit = k.Rig.Engine.Last("Damage.TakeDamage") ?? throw new Xunit.Sdk.XunitException("no TakeDamage");
        Assert.Equal(body.Handle, hit.ArgAs<EntityHandle>(0));
        Assert.Equal(KillRobotCommand.Lethal, hit.ArgAs<float>(2));
        Assert.Equal(RobotState.Dead, c.Roster.State(1));
        Assert.Single(k.Told.Kills);
    }
}
