using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The team service (tf2 stage 0.5): <see cref="ITeams"/>'s join, its
/// single-owner <see cref="IJoinTeamPolicy"/>, and the <see cref="ISpawnSelector"/>
/// chain the spawn ask consults first.
///
/// <para>
/// WHAT EACH ARM PINS: an accepted join calls the native store exactly once
/// with the requested team and raises the stock player_team event (before
/// the store, and not at all for the team the player is already on); a
/// refused policy means ZERO store calls and the refusal out loud (the accept
/// arm is the positive control that proves the same route reaches the
/// store); the policy is resolved from registrations after the mods' scan
/// (the module's order, replicated), and two are an error naming both owners;
/// zero policies accepts any real team (a divergence row); team 1 is stored
/// and then watched, and neither it nor an out-of-range number reaches a
/// mod's policy; the active mode's spawns answer before the selector chain,
/// whose two named arms — a handle wins, None falls through — have a
/// non-zero stimulus each.
/// </para>
/// </summary>
public class TeamJoinTests
{
    private static readonly EntityHandle[] Handles = { new(0x010001), new(0x010002), new(0x010003) };
    private static readonly EntityHandle Spot = new(0x020042);

    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private sealed class Policy(bool accepts, string why = "not today") : IJoinTeamPolicy
    {
        public int Asks { get; private set; }
        public int LastTeam { get; private set; } = -1;

        public bool MayJoin(EntityHandle player, int requested, out string reason)
        {
            Asks++; LastTeam = requested;
            reason = accepts ? string.Empty : why;
            return accepts;
        }
    }

    private sealed class Selector(EntityHandle spot, bool throws = false) : ISpawnSelector
    {
        public int Asks { get; private set; }

        public EntityHandle SelectSpawn(EntityHandle player)
        {
            Asks++;
            if (throws) throw new InvalidOperationException("the mod is broken");
            return spot;
        }
    }

    /// <summary>
    /// The ObserverTests rig's own shape (same registry, same stubs), with the
    /// pieces Teams needs added: a recording clock, an answer for the event
    /// handle, and the player_team set/fire arms let through.
    /// </summary>
    private sealed class Rig
    {
        public readonly RecordingEngine Engine = TestServices.Fake();
        public readonly ScriptedEntityRegistry Registry = new();
        public readonly Observers Observers = null!;
        public readonly RecordingLog Log = new();
        public readonly SsPlayer[] Players;
        public readonly ObserverTests.Clients Clients = new();
        public readonly ConVarRegistry ConVars = new(TestServices.UnattachedConsole());

        public Rig()
        {
            Registry.RegisterAll(typeof(SsPlayer).Assembly);
            var services = TestServices.Services();
            Engine.Returns("Entities.IsValid", true)
                  .ReturnsEachCall("Entities.GetIndex",
                      () => Engine.Calls[^1].Args[0] is EntityHandle h ? Array.IndexOf(Handles, h) + 1 : 0)
                  .Returns("Entities.GetOrigin", Vector.Zero)
                  .Returns("Players.GetTeam", 0)
                  .Returns("Entities.GetEyeAngles", new Vector(0, 90, 0))
                  .Returns("WorldQuery.EntityTraceHull", new EntityHullTrace(new Vector(0, -64, 64), false, false))
                  .Returns("Players.GetUserId", 21)
                  .Returns("GameEvents.EventCreate", IntPtr.Zero);
            Engine.Ignores("Players.SetObserverMode", "PlayerMovement.SetMoveType", "EntityRender.SetVisible",
                           "Players.HolsterActiveWeapon", "Damage.SetTakeDamageMode", "Entities.Teleport",
                           "PlayerMovement.SnapEyeAngles", "EntityPhysics.SetNotSolid", "Players.RemoveAllItems",
                           "Players.Respawn", "GameEvents.EventSetInt", "GameEvents.EventSetBool",
                           "GameEvents.EventSetString", "GameEvents.EventFire");
            services.AddInstance<IEntityDirectory>(Registry);
            Registry.Services = services;
            for (int i = 0; i < Handles.Length; i++)
                Registry.KeyValue(Handles[i], EntityNames.SsPlayer, "targetname", "p" + i);
            Players = Handles.Select(h => (SsPlayer)Registry.Get(h)!).ToArray();
            foreach (var p in Players)
            {
                p.State.BindLocal();
                p.State.Alive = true;
                p.State.ObserverTarget = EntityHandle.None.Value;
            }

            ConVars.RegisterAll(typeof(GameRules).Assembly);
            ConVars.Get<AllowSpectatorsConVar>()!.NotifyChanged("1", "0");   // stock's default

            Observers = new Observers(Log, Registry, Engine, Engine, Engine, Engine,
                                      Engine, Engine, Engine, ConVars, Clients);
        }

        /// <summary>
        /// Teams over its own container, each policy registered under its own
        /// type the way the mods' scan registers a marked class — the policy is
        /// resolved from registrations, so a fact hands it registrations.
        /// </summary>
        public Teams BuildTeams(IJoinTeamPolicy[]? policies = null,
                                IHooks<ISpawnSelector>? selectors = null,
                                IPlayerDirectory? roster = null)
        {
            var services = new ServiceContainer();
            foreach (var policy in policies ?? [])
                services.AddInstance(policy.GetType(), policy);

            return new(Log, Engine, Engine, Registry, roster ?? new NoRecords(),
                       Observers, Engine, Engine, null, services, selectors);
        }
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

    //-- the accepted join --

    [Fact]
    public void AnAcceptedJoinCallsTheStoreExactlyOnceWithTheRequestedTeam()
    {
        var rig = new Rig();
        var accept = new Policy(accepts: true);
        var teams = rig.BuildTeams(policies: [accept]);

        var outcome = teams.Join(Handles[0], 2);

        Assert.Equal(JoinTeamResult.Playing, outcome);
        Assert.Equal(1, accept.Asks);
        Assert.Equal(2, accept.LastTeam);
        Assert.Equal(1, rig.Engine.CountOf("Players.SetTeam"));
        var call = rig.Engine.Last("Players.SetTeam")!.Value;
        Assert.Equal(Handles[0], Assert.IsType<EntityHandle>(call.Args[0]));
        Assert.Equal(2, Assert.IsType<int>(call.Args[1]));
    }

    [Fact]
    public void AnAcceptedJoinFiresTheStockPlayerTeamEventWithItsFields()
    {
        var rig = new Rig();
        rig.Engine.Returns("GameEvents.EventCreate", new IntPtr(42));
        var teams = rig.BuildTeams();

        teams.Join(Handles[0], 2);

        Assert.Equal(1, rig.Engine.CountOf("GameEvents.EventCreate"));
        Assert.Equal("player_team", Assert.IsType<string>(rig.Engine.Last("GameEvents.EventCreate")!.Value.Args[0]));
        Assert.Equal(1, rig.Engine.CountOf("GameEvents.EventFire"));
        var set = rig.Engine.AllOf("GameEvents.EventSetInt").ToList();
        Assert.Contains(set, c => (string)c.Args[1]! == "team" && (int)c.Args[2]! == 2);
        Assert.Contains(set, c => (string)c.Args[1]! == "oldteam");   // the previous team, read first
        Assert.Contains(set, c => (string)c.Args[1]! == "userid");
        Assert.Contains(rig.Engine.AllOf("GameEvents.EventSetString"), c => (string)c.Args[1]! == "name");
    }

    [Fact]
    public void NoPolicyAcceptsAnyRealTeamAndCarriesTheOldTeamIntoTheEvent()
    {
        var rig = new Rig();
        rig.Engine.Returns("GameEvents.EventCreate", new IntPtr(42));
        var teams = rig.BuildTeams();

        Assert.Equal(JoinTeamResult.Playing, teams.Join(Handles[0], 3));

        var set = rig.Engine.AllOf("GameEvents.EventSetInt").ToList();
        Assert.Contains(set, c => (string)c.Args[1]! == "oldteam" && (int)c.Args[2]! == 0);
        Assert.Contains(set, c => (string)c.Args[1]! == "team" && (int)c.Args[2]! == 3);
    }

    [Fact]
    public void JoiningTheTeamYouAreOnStoresNothingAndAnnouncesNothing()
    {
        // CBasePlayer::ChangeTeam (player.cpp:7657-7661): "if this is our
        // current team, just abort" — before the event and the store. The
        // observer half (StopObserverMode, STATE_ACTIVE) still ran, in
        // HandleCommand_JoinTeam before ChangeTeam.
        var rig = new Rig();
        rig.Engine.Returns("GameEvents.EventCreate", new IntPtr(42))
                  .Returns("Players.GetTeam", 2);
        var teams = rig.BuildTeams();

        Assert.Equal(JoinTeamResult.Playing, teams.Join(Handles[0], 2));

        Assert.Equal(0, rig.Engine.CountOf("Players.SetTeam"));
        Assert.Equal(0, rig.Engine.CountOf("GameEvents.EventCreate"));

        // The control: the same rig on a DIFFERENT team stores and announces.
        Assert.Equal(JoinTeamResult.Playing, teams.Join(Handles[0], 3));
        Assert.Equal(1, rig.Engine.CountOf("Players.SetTeam"));
        Assert.Equal(1, rig.Engine.CountOf("GameEvents.EventFire"));
    }

    [Fact]
    public void ThePlayerTeamEventFiresBeforeTheStore()
    {
        // Stock fires player_team FIRST "so that all user messages that follow
        // as a result of the team change come after this one" (player.cpp:7663-7678),
        // then stores (BaseClass::ChangeTeam, :7692).
        var rig = new Rig();
        rig.Engine.Returns("GameEvents.EventCreate", new IntPtr(42));
        var teams = rig.BuildTeams();

        teams.Join(Handles[0], 2);

        var members = rig.Engine.Calls.Select(c => c.Member).ToList();
        int fired = members.IndexOf("GameEvents.EventFire");
        int stored = members.IndexOf("Players.SetTeam");
        Assert.True(fired >= 0 && stored > fired, $"event at {fired}, store at {stored}");
    }

    //-- the refusal --

    [Fact]
    public void ARefusingPolicyMovesNothingAndIsSaidOutLoud()
    {
        var rig = new Rig();
        var refuse = new Policy(accepts: false, why: "team 2 is full");
        var teams = rig.BuildTeams(policies: [refuse]);

        var outcome = teams.Join(Handles[0], 2);

        Assert.Equal(JoinTeamResult.Refused, outcome);
        Assert.Equal(1, refuse.Asks);
        // THE DIFFERENTIAL: the accept arm above proves this same route
        // reaches the store exactly once; here the same count must be zero,
        // and the event must not fire either.
        Assert.Equal(0, rig.Engine.CountOf("Players.SetTeam"));
        Assert.Equal(0, rig.Engine.CountOf("GameEvents.EventFire"));
        Assert.Contains("refused team 2", string.Concat(rig.Log.Lines));
        Assert.Contains("team 2 is full", string.Concat(rig.Log.Lines));
    }

    [Fact]
    public void TwoPoliciesAreAnErrorNamingBothOwnersWhenThePolicyIsResolved()
    {
        var rig = new Rig();
        var teams = rig.BuildTeams(policies: [new Policy(true), new SecondPolicy()]);

        // Policy is what the module reads right after the mods' scan: the
        // startup moment this error belongs to.
        var error = Assert.Throws<InvalidOperationException>(() => teams.Policy);

        Assert.Contains("2 owners", error.Message);
        Assert.Contains(nameof(Policy), error.Message);
        Assert.Contains(nameof(SecondPolicy), error.Message);
    }

    private sealed class SecondPolicy : IJoinTeamPolicy
    {
        public bool MayJoin(EntityHandle player, int requested, out string reason)
        {
            reason = string.Empty;
            return true;
        }
    }

    //-- the module's order: Teams is built BEFORE the mods register --

    /// <summary>A mod's join policy as the scan finds one: a marked class, no order.</summary>
    [SideService(Side.Server)]
    private sealed class ModRefusesTeamTwo : IJoinTeamPolicy
    {
        public int Asks { get; private set; }

        public bool MayJoin(EntityHandle player, int requested, out string reason)
        {
            Asks++;
            reason = requested == 2 ? "team 2 is the mod's" : string.Empty;
            return requested != 2;
        }
    }

    /// <summary>A second mod's policy, also unordered: the single-owner clash.</summary>
    [SideService(Side.Server)]
    private sealed class ModAcceptsEveryone : IJoinTeamPolicy
    {
        public bool MayJoin(EntityHandle player, int requested, out string reason)
        {
            reason = string.Empty;
            return true;
        }
    }

    /// <summary>
    /// ServerModule.Initialize's order over a real container: the hook
    /// registrations at the top, Teams built and registered as ITeams with the
    /// core services, and only THEN the mods' scan (SideServices.Register)
    /// — so a fact built on this sees what the live server sees, not a hook
    /// list handed in after the fact.
    /// </summary>
    private static (ServiceContainer Services, Teams Teams) Composed(Rig rig, params Type[] mods)
        => Composed(rig, _ => { }, mods);

    /// <summary>
    /// The same, with what the module registers before Teams (the mode proxy,
    /// registered at ServerModule's "Create&lt;GameModes&gt;" a hundred lines
    /// above Teams) put in by <paramref name="before"/>.
    /// </summary>
    private static (ServiceContainer Services, Teams Teams) Composed(Rig rig, Action<ServiceContainer> before,
                                                                       params Type[] mods)
    {
        var c = new ServiceContainer();
        c.AddInstance<ILog>(rig.Log);
        c.AddInstance<IEnginePlayers>(rig.Engine);
        c.AddInstance<IEngineEntities>(rig.Engine);
        c.AddInstance<IGameClock>(rig.Engine);
        c.AddInstance<IEngineGameEvents>(rig.Engine);
        c.AddInstance<IEntityDirectory>(rig.Registry);
        c.AddInstance<IPlayerDirectory>(new NoRecords());
        c.AddInstance(rig.Observers);
        // (No IHooks<IJoinTeamPolicy>: the policy is single-owner, resolved
        // from registrations, exactly as the module no longer registers one.)
        c.AddFactory<IHooks<ISpawnSelector>>(l => new Hooks<ISpawnSelector>(l, rig.Log));
        before(c);

        var teams = c.Create<Teams>();
        c.AddInstance<ITeams>(teams);

        SideServices.Register(c, Side.Server, mods);
        return (c, teams);
    }

    [Fact]
    public void TheStartupAskAfterTheScanNamesTheModsPolicy()
    {
        var rig = new Rig();
        var (services, teams) = Composed(rig, typeof(ModRefusesTeamTwo));

        // What the module logs right after SideServices.Register: the owner.
        Assert.Same(services.Resolve<ModRefusesTeamTwo>(), teams.Policy);

        // And the no-owner arm of the same ask: null, any real team accepted.
        var (_, bare) = Composed(new Rig());
        Assert.Null(bare.Policy);
    }

    [Fact]
    public void AModsPolicyRegisteredAfterTeamsIsAskedInTheModulesOrder()
    {
        var rig = new Rig();
        var (services, teams) = Composed(rig, typeof(ModRefusesTeamTwo));

        var outcome = teams.Join(Handles[0], 2);

        // THE STIMULUS REACHED THE MOD: asked once, and its refusal stood.
        // Collected at Teams' construction, the policy list was empty forever
        // and this join went through to the store.
        Assert.Equal(1, services.Resolve<ModRefusesTeamTwo>()!.Asks);
        Assert.Equal(JoinTeamResult.Refused, outcome);
        Assert.Equal(0, rig.Engine.CountOf("Players.SetTeam"));
    }

    [Fact]
    public void TwoUnorderedModPoliciesAreTheSingleOwnerRefusalNamingBoth()
    {
        var rig = new Rig();
        var (_, teams) = Composed(rig, typeof(ModRefusesTeamTwo), typeof(ModAcceptsEveryone));

        // The OWNERSHIP refusal, not the chain's "both declare HookOrder 0"
        // (StageCollisionException): no order makes two owners coherent, so
        // the message must name the real fix — exactly one assembly owns this.
        var error = Assert.Throws<InvalidOperationException>(() => teams.Join(Handles[0], 2));

        Assert.Contains(nameof(ModRefusesTeamTwo), error.Message);
        Assert.Contains(nameof(ModAcceptsEveryone), error.Message);
        Assert.Contains("single-owner", error.Message);
        Assert.Equal(0, rig.Engine.CountOf("Players.SetTeam"));
    }

    private sealed class RecordingChat : IChat
    {
        public List<(int EntIndex, string Text)> Lines { get; } = [];
        public void PrintAll(string text) => Lines.Add((0, text));
        public void Print(int entIndex, string text) => Lines.Add((entIndex, text));
    }

    [Fact]
    public void ARefusalIsToldToThePlayerWhoAsked()
    {
        // The server log is the operator's; the player who typed jointeam 2
        // must hear why nothing happened. IChat is registered before Teams in
        // ServerModule, so the module's order hands it in.
        var rig = new Rig();
        var chat = new RecordingChat();
        var (_, teams) = Composed(rig, c => c.AddInstance<IChat>(chat), typeof(ModRefusesTeamTwo));

        Assert.Equal(JoinTeamResult.Refused, teams.Join(Handles[1], 2));

        var told = Assert.Single(chat.Lines);
        Assert.Equal(2, told.EntIndex);                       // Handles[1] is slot 2: that player, not everyone
        Assert.Contains("team 2 is the mod's", told.Text);

        // And an accepted join tells nobody anything.
        Assert.Equal(JoinTeamResult.Playing, teams.Join(Handles[1], 3));
        Assert.Single(chat.Lines);
    }

    //-- the arms that stay the observers' --

    [Fact]
    public void TheSpectatorTeamIsStoredThenWatchedAndNeverAsksThePolicy()
    {
        var rig = new Rig();
        rig.Clients.SpecMode = "7";                       // cl_spec_mode: roaming
        var asked = new Policy(accepts: true);
        var teams = rig.BuildTeams(policies: [asked]);

        var outcome = teams.Join(Handles[0], Observers.TeamSpectator);

        Assert.Equal(JoinTeamResult.Spectating, outcome);
        Assert.Equal(0, asked.Asks);
        Assert.Equal((int)ObserverMode.Roaming, rig.Players[0].State.ObserverMode);

        // Stock's HandleCommand_JoinTeam(TEAM_SPECTATOR) is ChangeTeam(TEAM_SPECTATOR):
        // team 1 is STORED, exactly once...
        Assert.Equal(1, rig.Engine.CountOf("Players.SetTeam"));
        var call = rig.Engine.Last("Players.SetTeam")!.Value;
        Assert.Equal(Handles[0], Assert.IsType<EntityHandle>(call.Args[0]));
        Assert.Equal(Observers.TeamSpectator, Assert.IsType<int>(call.Args[1]));

        // ...BEFORE the observer half (CHL2MP_Player::ChangeTeam runs
        // BaseClass::ChangeTeam, then its spectator arm's RemoveAllItems and
        // STATE_OBSERVER_MODE), so the observer mode is chosen for a player
        // already on team 1 (mp_forcecamera reads the team).
        var members = rig.Engine.Calls.Select(c => c.Member).ToList();
        int stored = members.IndexOf("Players.SetTeam");
        int stripped = members.IndexOf("Players.RemoveAllItems");
        Assert.True(stored >= 0 && stripped > stored, $"store at {stored}, strip at {stripped}");
    }

    [Fact]
    public void ARefusedSpectatorJoinStoresNothing()
    {
        var rig = new Rig();
        rig.ConVars.Get<AllowSpectatorsConVar>()!.NotifyChanged("0", "1");   // mp_allowspectators 0
        var teams = rig.BuildTeams();

        Assert.Equal(JoinTeamResult.SpectatorsNotAllowed, teams.Join(Handles[0], Observers.TeamSpectator));

        Assert.Equal(0, rig.Engine.CountOf("Players.SetTeam"));
        Assert.Contains("#Cannot_Be_Spectator", string.Concat(rig.Log.Lines));
    }

    [Fact]
    public void AnOutOfRangeTeamKeepsStocksWarningAndNeverAsksThePolicy()
    {
        var rig = new Rig();
        var asked = new Policy(accepts: true);
        var teams = rig.BuildTeams(policies: [asked]);

        Assert.Equal(JoinTeamResult.Invalid, teams.Join(Handles[0], 0));    // team 0: no such side
        Assert.Equal(JoinTeamResult.Invalid, teams.Join(Handles[0], 9));    // past the last team
        Assert.Equal(0, asked.Asks);                                        // refused before mod code
        Assert.Equal(0, rig.Engine.CountOf("Players.SetTeam"));
        Assert.Contains("invalid team index", string.Concat(rig.Log.Lines));
    }

    //-- the roster walk the scoreboard reads --

    [Fact]
    public void CountAndPlayersWalkTheRosterAndGroupByTheRealTeam()
    {
        var rig = new Rig();
        // THE STIMULUS: three tracked players on teams 2, 2 and 3. The
        // differential is teams.Count: a walk that read no team would say 0
        // for every team, and the arm below with no players says 0 too — so
        // the non-zero arm is what proves the walk works.
        var roster = new TrackedRoster(Handles[0], Handles[1], Handles[2]);
        rig.Engine.ReturnsEachCall("Players.GetTeam", () => roster.NextTeam());
        var teams = rig.BuildTeams(roster: roster);

        Assert.Equal(2, teams.Count(2));
        Assert.Equal(1, teams.Count(3));
        Assert.Equal(0, teams.Count(1));
        Assert.Equal(new[] { Handles[0], Handles[1] }, teams.Players(2));

        // The empty arm, same service shape: nothing walked, nothing found.
        var empty = rig.BuildTeams();
        Assert.Equal(0, empty.Count(2));
    }

    private sealed class TrackedRoster(params EntityHandle[] players) : IPlayerDirectory
    {
        private int _cursor;

        public int NextTeam()
        {
            // teams in the order the walk yields them: 2, 2, 3
            int[] teams = [2, 2, 3];
            return teams[_cursor++ % teams.Length];
        }

        public PlayerRecord this[EntityHandle player] => new();
        public bool Knows(EntityHandle player) => true;
        public IEnumerable<(EntityHandle Player, PlayerRecord Record)> All
            => players.Select(p => (p, new PlayerRecord()));
        public float WaitRemaining(EntityHandle player, float now) => 0f;
        public float RespawnDelay { get; set; } = 3f;
        public ObserverMode DeathView { get; set; }
        public bool DropWeaponOnDeath { get; set; }
        public float DropSpeed { get; set; }
        public void OnSpawned(EntityHandle player, float now) { }
        public void OnKilled(EntityHandle player, EntityHandle attacker, float now) { }
        public void Forget(EntityHandle player) { }
    }

    //-- the spawn ask --

    [Fact]
    public void ASelectorThatNamesASpotWins()
    {
        var rig = new Rig();
        var named = new Selector(Spot);
        var teams = rig.BuildTeams(selectors: new ListHooks<ISpawnSelector>(named));

        Assert.Equal(Spot, teams.SelectSpawn(Handles[0]));
        Assert.Equal(1, named.Asks);   // stimulus: it WAS asked
    }

    [Fact]
    public void ASelectorThatDeclinesFallsThroughToTheNextAndThenToNone()
    {
        var rig = new Rig();
        var declined = new Selector(EntityHandle.None);
        var named = new Selector(Spot);
        var teams = rig.BuildTeams(selectors: new ListHooks<ISpawnSelector>(declined, named));

        // The decline is an answer, not an absence: it must fall through and
        // the NEXT selector's name must win (this is the arm a mutant that
        // returns any selector's answer, None included, cannot satisfy).
        Assert.Equal(Spot, teams.SelectSpawn(Handles[0]));
        Assert.Equal(1, declined.Asks);
        Assert.Equal(1, named.Asks);

        // Alone, a decline is the core's walk: None.
        var alone = rig.BuildTeams(selectors: new ListHooks<ISpawnSelector>(new Selector(EntityHandle.None)));
        Assert.Equal(EntityHandle.None, alone.SelectSpawn(Handles[0]));
    }

    [Fact]
    public void AThrowingSelectorFallsThroughAndTheChainKeepsGoing()
    {
        var rig = new Rig();
        var broken = new Selector(Spot, throws: true);
        var next = new Selector(Spot);
        var teams = rig.BuildTeams(selectors: new ListHooks<ISpawnSelector>(broken, next));

        Assert.Equal(Spot, teams.SelectSpawn(Handles[0]));
        Assert.Equal(1, broken.Asks);
        Assert.Equal(1, next.Asks);
        Assert.Contains("spawn selector", string.Concat(rig.Log.Lines));   // and it said so
    }

    [Fact]
    public void NoSelectorsAskNothingAndNameNothing()
    {
        var rig = new Rig();
        var teams = rig.BuildTeams();

        Assert.Equal(EntityHandle.None, teams.SelectSpawn(Handles[0]));
    }

    //-- the active mode's spawn concern (IGameMode.Spawns) --

    private static readonly EntityHandle ChainSpot = new(0x020099);

    private sealed class NoRounds : IRoundFlow
    {
        public void Think(float now) { }
        public bool RoundOver => false;
        public void ResetRound() { }
        public void OnPlayerSpawned(EntityHandle player, float now) { }
        public void OnPlayerKilled(PlayerDeathArgs death) { }
    }

    private sealed class SpawningMode(ISpawnSelector? spawns) : IGameMode
    {
        public string Id => "arena";
        public IRoundFlow Rounds { get; } = new NoRounds();
        public ISpawnSelector? Spawns { get; } = spawns;
    }

    /// <summary>
    /// The module's order with the mode proxy registered before Teams (as
    /// ServerModule does), <paramref name="mode"/> switched to, and one
    /// registered chain selector answering <see cref="ChainSpot"/>.
    /// </summary>
    private static (Teams Teams, Selector Chain) WithActiveMode(Rig rig, IGameMode mode)
    {
        var chain = new Selector(ChainSpot);
        var (services, teams) = Composed(rig, c =>
        {
            var modes = new GameModes(rig.Log, rig.Engine, c, rig.ConVars);
            c.AddInstance(modes);
            c.AddInstance(mode.GetType(), mode);
        });
        services.AddInstance(chain.GetType(), chain);
        rig.Engine.Returns("Clock.Now", 1f);
        Assert.True(services.Resolve<GameModes>()!.SwitchTo(mode.Id));
        return (teams, chain);
    }

    [Fact]
    public void TheActiveModesSpawnSelectorAnswersBeforeTheRegisteredChain()
    {
        var rig = new Rig();
        var modeSpawns = new Selector(Spot);
        var (teams, chain) = WithActiveMode(rig, new SpawningMode(modeSpawns));

        // The round's owner speaks first: its spot wins and the mod-wide
        // chain is not asked (the plan: SelectSpawnPoint consults
        // GameModes.Active).
        Assert.Equal(Spot, teams.SelectSpawn(Handles[0]));
        Assert.Equal(1, modeSpawns.Asks);
        Assert.Equal(0, chain.Asks);
    }

    [Fact]
    public void AModeThatDeclinesOrThrowsFallsThroughToTheRegisteredChain()
    {
        var rig = new Rig();
        var declines = new Selector(EntityHandle.None);
        var (teams, chain) = WithActiveMode(rig, new SpawningMode(declines));

        Assert.Equal(ChainSpot, teams.SelectSpawn(Handles[0]));
        Assert.Equal(1, declines.Asks);                     // the mode WAS asked
        Assert.Equal(1, chain.Asks);                        // and then the chain

        var broken = new Rig();
        var throws = new Selector(Spot, throws: true);
        var (brokenTeams, brokenChain) = WithActiveMode(broken, new SpawningMode(throws));
        Assert.Equal(ChainSpot, brokenTeams.SelectSpawn(Handles[0]));
        Assert.Equal(1, throws.Asks);
        Assert.Equal(1, brokenChain.Asks);
        Assert.Contains("spawn selector", string.Concat(broken.Log.Lines));
    }
}
