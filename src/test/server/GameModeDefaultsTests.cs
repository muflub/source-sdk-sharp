using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The two default members beside <see cref="IGameMode.Spawns"/>: the active
/// mode says whether it plays with teams (<see cref="HostSay.Teammates"/>,
/// the say_team gate), and it can own the join rule
/// (<see cref="Teams.Join"/> asks <see cref="IGameMode.JoinPolicy"/> before
/// the registered single-owner <see cref="IJoinTeamPolicy"/>). Both sit behind
/// the core's own answer, so HL2MP and a game with no mode keep every
/// behaviour they have today — the no-mode facts here are that proof.
/// </summary>
public class GameModeDefaultsTests
{
    private static readonly EntityHandle[] Handles = { new(0x010001), new(0x010002) };
    private static readonly EntityHandle First = Handles[0];
    private static readonly EntityHandle Second = Handles[1];

    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private sealed class Selector(string id) : IGameModeSelector
    {
        public string? PickMode() => id;
    }

    private sealed class NoRounds : IRoundFlow
    {
        public void Think(float now) { }
        public bool RoundOver => false;
        public void ResetRound() { }
        public void OnPlayerSpawned(EntityHandle player, float now) { }
        public void OnPlayerKilled(PlayerDeathArgs death) { }
    }

    /// <summary>The mode under test: its id, and the two answers the facts set.</summary>
    private sealed class TeamplayMode(bool teamplay, IJoinTeamPolicy? joinPolicy) : IGameMode
    {
        public string Id => "teamy";
        public IRoundFlow Rounds { get; } = new NoRounds();
        public bool Teamplay => teamplay;
        public IJoinTeamPolicy? JoinPolicy => joinPolicy;
    }

    /// <summary>A join answer with its ask counted, so a fact can see who was asked.</summary>
    private sealed class Policy(bool accepts, string why = "") : IJoinTeamPolicy
    {
        public int Asks { get; private set; }

        public bool MayJoin(EntityHandle player, int requested, out string reason)
        {
            Asks++;
            reason = accepts ? string.Empty : why;
            return accepts;
        }
    }

    private sealed class Clients : IConnectedClients
    {
        public float? AvgLatency(int entIndex) => null;
        public ulong SteamId64(int entIndex) => 0;
        public string? Address(int entIndex) => null;
        public string? ClientConVar(int entIndex, string name) => null;
        public int UserId(int entIndex) => 0;
        public void Kick(int entIndex, string reason) { }
        public void ClientCommand(int entIndex, string command) { }
        public void Reconnect(int entIndex) { }
    }

    /// <summary>The roster Teams walks; no records, membership alone.</summary>
    private sealed class NoRecords : IPlayerDirectory
    {
        public PlayerRecord this[EntityHandle player] => new();
        public bool Knows(EntityHandle player) => false;
        public IEnumerable<(EntityHandle Player, PlayerRecord Record)> All => [];
        public float WaitRemaining(EntityHandle player, float now) => 0f;
        public float RespawnDelay { get; set; } = 3f;
        public ObserverMode DeathView { get; set; }
        public bool DropWeaponOnDeath { get; set; }
        public float DropSpeed { get; set; }
        public void OnSpawned(EntityHandle player, float now) { }
        public void OnKilled(EntityHandle player, EntityHandle attacker, float now) { }
        public void Forget(EntityHandle player) { }
    }

    /// <summary>
    /// One engine double answers players, entities, clock and events (the
    /// TeamJoinTests shape), the registry's records make the handles managed,
    /// and everyone sits on team 2 — the arm where only a team-playing mode
    /// can make the pair teammates.
    /// </summary>
    private sealed class Rig
    {
        public readonly RecordingEngine Engine = TestServices.Fake();
        public readonly ScriptedEntityRegistry Registry = new(TestEngines.Server());
        public readonly RecordingLog Log = new();
        public readonly ServiceContainer Services = TestServices.Services();
        public readonly ConVarRegistry ConVars = ConVarRegistry.For(TestServices.UnattachedConsole());
        public readonly Clients Listeners = new();
        public readonly Observers Observers;

        public Rig()
        {
            Registry.RegisterAll(typeof(SsPlayer).Assembly);
            Engine.Returns("Entities.IsValid", true)
                  .ReturnsEachCall("Entities.GetIndex",
                      () => Engine.Calls[^1].Args[0] is EntityHandle h ? Array.IndexOf(Handles, h) + 1 : 0)
                  .Returns("Players.GetTeam", 2)
                  .Returns("GameEvents.EventCreate", IntPtr.Zero);
            Engine.Ignores("GameEvents.EventSetInt", "GameEvents.EventSetBool",
                           "GameEvents.EventSetString", "GameEvents.EventFire");
            Services.AddInstance<IEntityDirectory>(Registry);
            Registry.Services = Services;
            for (int i = 0; i < Handles.Length; i++)
                Registry.KeyValue(Handles[i], EntityNames.Player, "targetname", "p" + i);

            ConVars.RegisterAll(typeof(GameRules).Assembly);   // the modes' convars

            Observers = new Observers(Log, Registry, Engine, Engine, Engine, Engine,
                                      Engine, Engine, Engine, ConVars, Listeners);
        }

        /// <summary>
        /// The relay, over the rig's engine. Attached modes make the active
        /// mode's Teamplay answer visible to say_team; an unattached relay
        /// keeps HL2MP's own answer.
        /// </summary>
        public HostSay BuildSay(GameModes? modes = null)
        {
            var say = new HostSay(Log, Engine, Engine, Registry, Engine, Listeners, Engine, Engine);
            if (modes is not null)
                say.AttachModes(modes);

            return say;
        }

        /// <summary>Registers the mode and names it, the way the container and a selector do.</summary>
        public GameModes UseMode(IGameMode mode)
        {
            Services.AddInstance(mode.GetType(), mode);
            Services.AddInstance<IHooks<IGameModeSelector>>(new ListHooks<IGameModeSelector>(new Selector(mode.Id)));
            return new GameModes(Log, Engine, Services, ConVars);
        }

        /// <summary>Teams over the same container, the owners registered as the scan registers them.</summary>
        public Teams BuildTeams(GameModes? modes, params IJoinTeamPolicy[] owners)
        {
            foreach (var owner in owners)
                Services.AddInstance(owner.GetType(), owner);

            return new Teams(Log, Engine, Engine, Registry, new NoRecords(), Observers, Engine, Engine,
                             null, Services, null, modes, null, null);
        }
    }

    //-- the say_team gate: the active mode's Teamplay -------------------------

    /// <summary>The core's own answer: HL2MP without teamplay has no teammates.</summary>
    [Fact]
    public void NoModeMeansNoTeamplay()
    {
        var rig = new Rig();

        // Team 2 for everyone, and still nobody's teammate: the team check is
        // behind the mode's answer, not a replacement for it.
        Assert.False(rig.BuildSay().Teammates(First, Second), "no mode: HL2MP's own answer");
    }

    /// <summary>A mode that plays with teams makes one game team teammates.</summary>
    [Fact]
    public void AModesTeamplayMakesSameTeamPlayersTeammates()
    {
        var rig = new Rig();
        var modes = rig.UseMode(new TeamplayMode(teamplay: true, joinPolicy: null));
        var say = rig.BuildSay(modes);

        Assert.True(say.Teammates(First, Second), "the mode plays with teams, and both are on team 2");

        // THE DIFFERENTIAL: another game team is enemies even under the flag.
        rig.Engine.ReturnsEachCall("Players.GetTeam",
            () => rig.Engine.Calls[^1].Args[0] is EntityHandle h && h == First ? 2 : 3);
        Assert.False(say.Teammates(First, Second), "team 3 is not their side");
    }

    //-- the join ask: the mode's JoinPolicy before the registered owner -------

    /// <summary>
    /// The active mode owns the join of its round: its refusal stands over the
    /// registered owner's yes, and the owner is not asked behind that answer.
    /// </summary>
    [Fact]
    public void AModesJoinPolicyIsAskedBeforeTheRegisteredOwner()
    {
        var rig = new Rig();
        var refusal = new Policy(accepts: false, why: "the round is full");   // the mode's own rule
        var owner = new Policy(accepts: true);                                 // the registered owner
        var modes = rig.UseMode(new TeamplayMode(teamplay: true, joinPolicy: refusal));
        var teams = rig.BuildTeams(modes, owner);

        Assert.Equal(JoinTeamResult.Refused, teams.Join(First, 3));

        // The stimulus reached the mode and the refusal is its reason, said
        // out loud; the owner, behind it, never was asked.
        Assert.Equal(1, refusal.Asks);
        Assert.Equal(0, owner.Asks);
        Assert.Contains("the round is full", string.Concat(rig.Log.Lines));
        Assert.Equal(0, rig.Engine.CountOf("Players.SetTeam"));
    }

    /// <summary>
    /// A mode with no join rule leaves the single-owner rule exactly where it
    /// was: the one registered owner answers, as in the TeamJoinTests.
    /// </summary>
    [Fact]
    public void NoJoinPolicyKeepsTheSingleOwnerRule()
    {
        var rig = new Rig();
        var owner = new Policy(accepts: false, why: "the roster is full");
        var modes = rig.UseMode(new TeamplayMode(teamplay: false, joinPolicy: null));
        var teams = rig.BuildTeams(modes, owner);

        Assert.Equal(JoinTeamResult.Refused, teams.Join(First, 2));

        Assert.Equal(1, owner.Asks);                       // the owner answered, once
        Assert.Contains("the roster is full", string.Concat(rig.Log.Lines));
        Assert.Equal(0, rig.Engine.CountOf("Players.SetTeam"));
    }
}
