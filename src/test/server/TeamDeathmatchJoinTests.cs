using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;
using static SourceSharp.Tests.TeamDeathmatchTests;

namespace SourceSharp.Tests;

/// <summary>
/// Team deathmatch's join rule (<see cref="TeamDeathmatchJoinPolicy"/>, plan
/// A.3 item 3): <c>jointeam 0</c> is stock <c>PickDefaultSpawnTeam</c>'s
/// smaller team and a seeded random one on a tie; a move between the two
/// teams kills a living player (stock <c>ChangeTeam</c>'s <c>bKill</c>); the
/// unbalance limit refuses a join, and 0 never does. The <see cref="Teams"/>
/// facts drive the real join, where the core's deathmatch keeps stock's
/// refusal of team 0.
/// </summary>
public class TeamDeathmatchJoinTests
{
    private static readonly EntityHandle Joiner = new(0x0001_0009u);

    private sealed class PolicyRig
    {
        public readonly Dictionary<EntityHandle, int> Table = new();
        public readonly RecordingEngine Engine = Fake();
        public readonly ConVarRegistry ConVars = ConVarRegistry.For(UnattachedConsole());
        public readonly TeamDeathmatchJoinPolicy Policy;

        public PolicyRig(int combine, int rebels, int seed = 1)
        {
            for (int i = 0; i < combine; i++)
                Table[new EntityHandle((uint)(0x0002_0000 + i))] = TeamDeathmatchMode.TeamCombine;
            for (int i = 0; i < rebels; i++)
                Table[new EntityHandle((uint)(0x0003_0000 + i))] = TeamDeathmatchMode.TeamRebels;
            Table[Joiner] = 0;

            ConVars.RegisterAll(typeof(GameRules).Assembly);
            Policy = new TeamDeathmatchJoinPolicy(new TableTeams(Table), Engine, Engine,
                                                  ConVars.Declared<TeamsUnbalanceLimitConVar>(), new Random(seed));
        }

        public void Limit(int n) => ConVars.Declared<TeamsUnbalanceLimitConVar>().Bind(-1, "cs_sv_teams_unbalance_limit", n.ToString());
    }

    //-------------------------------------------------------------------------
    // jointeam 0: the smaller team, random on a tie.
    //-------------------------------------------------------------------------

    [Fact]
    public void AutoAssignPicksTheSmallerTeam()
    {
        Assert.Equal(TeamDeathmatchMode.TeamRebels, new PolicyRig(combine: 2, rebels: 1).Policy.AutoTeam(Joiner));
        Assert.Equal(TeamDeathmatchMode.TeamCombine, new PolicyRig(combine: 0, rebels: 1).Policy.AutoTeam(Joiner));
    }

    [Fact]
    public void AutoAssignOnATieIsTheSeededRandom()
    {
        var picks = Enumerable.Range(0, 16).Select(seed => new PolicyRig(1, 1, seed).Policy.AutoTeam(Joiner)).ToArray();

        // random->RandomInt( TEAM_COMBINE, TEAM_REBELS ), the same draw for the same seed.
        Assert.Equal(Enumerable.Range(0, 16).Select(seed => new Random(seed).Next(2, 4)), picks);
        Assert.Contains(TeamDeathmatchMode.TeamCombine, picks);
        Assert.Contains(TeamDeathmatchMode.TeamRebels, picks);
    }

    [Fact]
    public void AnAssignedPlayerIsCountedOutAndKeepsTheirTeamOnATie()
    {
        var rig = new PolicyRig(combine: 1, rebels: 1);
        rig.Table[Joiner] = TeamDeathmatchMode.TeamRebels;   // 1 Combine against the other Rebel: a tie without them

        Assert.Equal(TeamDeathmatchMode.TeamRebels, rig.Policy.AutoTeam(Joiner));
    }

    //-------------------------------------------------------------------------
    // The switch kill.
    //-------------------------------------------------------------------------

    [Fact]
    public void ASwitchBetweenTheTeamsKillsALivingPlayerAsTheirOwnAttacker()
    {
        var rig = new PolicyRig(1, 1);
        rig.Engine.Returns("Players.IsAlive", true);

        rig.Policy.TeamChanged(Joiner, TeamDeathmatchMode.TeamCombine, TeamDeathmatchMode.TeamRebels);

        var hit = Assert.Single(rig.Engine.AllOf("Damage.Apply"));
        Assert.Equal((Joiner, Joiner, PlayerSelfCommand.SuicideDamage, DamageType.Generic),
                     (hit.ArgAs<EntityHandle>(0), hit.ArgAs<EntityHandle>(1), hit.ArgAs<float>(2), hit.ArgAs<DamageType>(3)));
    }

    [Fact]
    public void AJoinFromUnassignedOrToSpectatorOrOfTheDeadKillsNobody()
    {
        var rig = new PolicyRig(1, 1);
        rig.Engine.Returns("Players.IsAlive", true);
        rig.Policy.TeamChanged(Joiner, 0, TeamDeathmatchMode.TeamCombine);
        rig.Policy.TeamChanged(Joiner, TeamDeathmatchMode.TeamCombine, 1);

        rig.Engine.Returns("Players.IsAlive", false);
        rig.Policy.TeamChanged(Joiner, TeamDeathmatchMode.TeamCombine, TeamDeathmatchMode.TeamRebels);

        Assert.False(rig.Engine.Called("Damage.Apply"), rig.Engine.Describe());
    }

    //-------------------------------------------------------------------------
    // The unbalance limit.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheUnbalanceLimitIsOffByDefault()
    {
        var rig = new PolicyRig(combine: 5, rebels: 0);

        Assert.True(rig.Policy.MayJoin(Joiner, TeamDeathmatchMode.TeamCombine, out _));
    }

    [Fact]
    public void TheUnbalanceLimitRefusesAJoinThatPutsATeamTooFarAhead()
    {
        var rig = new PolicyRig(combine: 2, rebels: 1);
        rig.Limit(1);

        Assert.False(rig.Policy.MayJoin(Joiner, TeamDeathmatchMode.TeamCombine, out string why));
        Assert.Equal(TeamDeathmatchJoinPolicy.UnbalancedReason, why);
        Assert.True(rig.Policy.MayJoin(Joiner, TeamDeathmatchMode.TeamRebels, out _), "the smaller team is always open");
    }

    //-------------------------------------------------------------------------
    // Through the real join.
    //-------------------------------------------------------------------------

    /// <summary>Teams over a registry whose two players are managed, with the mode as the active one.</summary>
    private sealed class JoinRig
    {
        private static readonly EntityHandle[] Handles = [new(0x0001_0001u), new(0x0001_0002u)];
        public static EntityHandle Asking => Handles[0];
        public static EntityHandle Other => Handles[1];

        public readonly RecordingEngine Engine = Fake();
        public readonly ScriptedEntityRegistry Registry = new(TestEngines.Server());
        public readonly RecordingLog Log = new();
        public readonly ServiceContainer Services = TestServices.Services();
        public readonly ConVarRegistry ConVars = ConVarRegistry.For(UnattachedConsole());
        public readonly Dictionary<EntityHandle, int> Table = new() { [Handles[0]] = 0, [Handles[1]] = 2 };
        public readonly Teams Teams;

        private sealed class Roster(EntityHandle[] players) : IPlayerDirectory
        {
            public PlayerRecord this[EntityHandle player] => new();
            public bool Knows(EntityHandle player) => true;
            public IEnumerable<(EntityHandle Player, PlayerRecord Record)> All => players.Select(p => (p, new PlayerRecord()));
            public float WaitRemaining(EntityHandle player, float now) => 0f;
            public float RespawnDelay { get; set; } = 3f;
            public ObserverMode DeathView { get; set; }
            public bool DropWeaponOnDeath { get; set; }
            public float DropSpeed { get; set; }
            public void OnSpawned(EntityHandle player, float now) { }
            public void OnKilled(EntityHandle player, EntityHandle attacker, float now) { }
            public void Forget(EntityHandle player) { }
        }

        private sealed class Selector(string id) : IGameModeSelector
        {
            public string? PickMode() => id;
        }

        private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
        {
            public IReadOnlyList<T> All => hooks;
            public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
        }

        public JoinRig(string mode)
        {
            Registry.RegisterAll(typeof(SsPlayer).Assembly);
            Engine.Returns("Entities.IsValid", true)
                  .ReturnsEachCall("Entities.GetIndex",
                      () => Engine.Calls[^1].Args[0] is EntityHandle h ? Array.IndexOf(Handles, h) + 1 : 0)
                  .ReturnsEachCall("Players.GetTeam",
                      () => Engine.Calls[^1].Args[0] is EntityHandle h && Table.TryGetValue(h, out int t) ? t : -1)
                  .WhenCalled("Players.SetTeam", c => Table[c.ArgAs<EntityHandle>(0)] = c.ArgAs<int>(1))
                  .Returns("Players.IsAlive", true)
                  .Returns("Players.GetUserId", 0)
                  .Returns("GameEvents.EventCreate", IntPtr.Zero);
            Engine.Ignores("Players.GetObserverMode", "Players.Respawn");
            Services.AddInstance<IEntityDirectory>(Registry);
            Registry.Services = Services;
            for (int i = 0; i < Handles.Length; i++)
                Registry.KeyValue(Handles[i], EntityNames.Player, "targetname", "p" + i);

            ConVars.RegisterAll(typeof(GameRules).Assembly);
            var observers = new Observers(Log, Registry, Engine, Engine, Engine, Engine, Engine, Engine, Engine,
                                          ConVars.Declare<ForceCameraConVar>(), ConVars.Declare<AllowSpectatorsConVar>(), null);
            Services.AddInstance<IHooks<IGameModeSelector>>(new ListHooks<IGameModeSelector>(new Selector(mode)));
            var modes = new GameModes(Log, Engine, Services, ConVars.Declare<SvModeSwitchWarmupConVar>(),
                                      ConVars.Declare<MpWaitingForPlayersTimeConVar>());
            Teams = new Teams(Log, Engine, Engine, Registry, new Roster(Handles), observers, Engine, Engine,
                              null, Services, null, modes, null, null);

            var spawns = new SpawnPoints(Engine, Engine, Engine, Engine, ConVars.Declared<Hl2mpSpawnFragFallbackRadiusConVar>(),
                                         new Random(1));
            Services.AddInstance(new TeamDeathmatchMode(modes, Engine, spawns, Teams, Teams, Engine,
                                                        ConVars.Declared<TeamsUnbalanceLimitConVar>(), Engine, null, new Random(1)));
        }
    }

    [Fact]
    public void JointeamZeroUnderTdmJoinsTheSmallerTeam()
    {
        var rig = new JoinRig(TeamDeathmatchMode.ModeId);

        rig.Teams.Join(JoinRig.Asking, 0);

        Assert.Equal(TeamDeathmatchMode.TeamRebels, rig.Table[JoinRig.Asking]);
    }

    [Fact]
    public void JointeamZeroUnderTheCoresRulesIsStillRefused()
    {
        var rig = new JoinRig(GameModes.NullModeId);

        Assert.Equal(JoinTeamResult.Invalid, rig.Teams.Join(JoinRig.Asking, 0));

        Assert.Equal(0, rig.Table[JoinRig.Asking]);
        Assert.Contains(rig.Log.Lines, l => l.Text.Contains("HandleCommand_JoinTeam( 0 ) - invalid team index."));
    }

    [Fact]
    public void SwitchingTeamsUnderTdmKillsThePlayer()
    {
        var rig = new JoinRig(TeamDeathmatchMode.ModeId);

        rig.Teams.Join(JoinRig.Other, 3);

        Assert.Equal(3, rig.Table[JoinRig.Other]);
        var hit = Assert.Single(rig.Engine.AllOf("Damage.Apply"));
        Assert.Equal(JoinRig.Other, hit.ArgAs<EntityHandle>(0));
    }

    [Fact]
    public void SwitchingTeamsUnderTheCoresRulesKillsNobody()
    {
        var rig = new JoinRig(GameModes.NullModeId);

        rig.Teams.Join(JoinRig.Other, 3);

        Assert.Equal(3, rig.Table[JoinRig.Other]);
        Assert.False(rig.Engine.Called("Damage.Apply"), rig.Engine.Describe());
    }
}
