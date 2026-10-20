using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client-command hook (tf2 stage 0.6): <see cref="IClientCommandHook"/>
/// is dispatched by <see cref="ServerModule.OnClientCommand"/> before any of
/// the core's own verbs, in <see cref="IOrderedHook.HookOrder"/>'s order, and
/// the first <c>true</c> wins.
///
/// <para>
/// WHAT EACH FACT PINS, per the plan: a hook claiming <c>joinclass scout</c>
/// receives <c>("joinclass", ["scout"])</c> — the verb apart from the args —
/// and the core never sees the word; an unclaimed <c>spectate</c> still
/// reaches the stock arm, proved by the observer mode actually landing
/// (Observers); two hooks run in HookOrder, and the first <c>true</c> stops
/// the chain, proved by a RECORDING second hook that captures nothing; and a
/// hook that throws is named in the log and skipped rather than swallowing
/// everyone's commands. The no-hook arm — commands route as they did before
/// the seam existed — is what the spectate fact doubles as.
/// </para>
/// </summary>
public class ClientCommandHookTests
{
    private static readonly EntityHandle Player = new(0x010001);

    private sealed record Seen(EntityHandle Player, string Verb, string[] Args);

    private sealed class Hook(Func<EntityHandle, string, string[], bool> act, int order = 0)
        : IClientCommandHook, IOrderedHook
    {
        public List<Seen> Calls { get; } = [];
        public int HookOrder => order;

        public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
        {
            Calls.Add(new Seen(player, command, args.ToArray()));
            return act(player, command, args.ToArray());
        }
    }

    // The real Hooks<T> collects with ResolveAll, which keys by the contract
    // each hook was registered under: two instances of one class would
    // REPLACE, so the rig registers each under its own Boxed<marker> type and
    // lets the production Hooks<T> do the HookOrder sort it will do in game.
    private interface IMark { }
    private sealed class M1 : IMark;
    private sealed class M2 : IMark;
    private sealed class M3 : IMark;
    private sealed class Boxed<TMark>(IClientCommandHook inner) : IClientCommandHook, IOrderedHook
        where TMark : IMark
    {
        public int HookOrder => (inner as IOrderedHook)?.HookOrder ?? 0;
        public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
            => inner.Handle(player, command, args);
    }

    private sealed class Rig
    {
        public readonly EngineSide Engine = EngineSide.Create(Side.Server);
        public readonly ServerModule Module;
        public readonly RecordingEngine Double;
        public readonly List<(string Text, bool Warning)> Lines = [];
        public readonly ObserverTests.Clients Clients = new();

        public Rig()
        {
            Module = new ServerModule(new ModuleEngine(Engine));
            Module.ComposeEngineAreas();

            var services = (ServiceContainer)Module.Services;
            var registry = Module.Registry;
            registry.RegisterAll(typeof(SsPlayer).Assembly);
            // The entities are built over a test double, not the module's
            // real areas (an unattached EngineSide throws): the full mock
            // container, exactly as ObserverTests builds its players — the
            // one difference being that this rig's recorder is the field the
            // facts read.
            var entityServices = TestServices.Services(out Double);
            entityServices.AddInstance<IEntityDirectory>(registry);
            registry.Services = entityServices;

            Double.Returns("Entities.IsValid", true)
                  .Returns("Entities.GetIndex", 1)
                  .Returns("Entities.GetOrigin", Vector.Zero)
                  .Returns("Entities.GetEyeAngles", new Vector(0, 90, 0))
                  .Returns("WorldQuery.EntityTraceHull", new EntityHullTrace(new Vector(0, -64, 64), false, false))
                  .Ignores("Players.SetObserverMode", "PlayerMovement.SetMoveType", "EntityRender.SetVisible",
                           "Players.HolsterActiveWeapon", "Damage.SetTakeDamageMode", "Entities.Teleport",
                           "PlayerMovement.SnapEyeAngles", "EntityPhysics.SetNotSolid", "Players.RemoveAllItems",
                           "Players.Respawn", "Players.GetTeam");

            var convars = ConVarRegistry.For(TestServices.UnattachedConsole());
            convars.RegisterAll(typeof(GameRules).Assembly);
            convars.Get<AllowSpectatorsConVar>()!.NotifyChanged("1", "0");   // stock's default

            // The observers of the stock arm, built over the test's double so
            // the spectate fact can see the engine calls it must make.
            var observers = new Observers(new RecordingLog(), registry, Double, Double, Double, Double,
                                          Double, Double, Double, convars, Clients);
            services.AddInstance(observers);
            // The team service the jointeam and spectate verbs route to, over
            // the same double (no event handle: player_team is not this
            // file's subject, the store is).
            Double.Returns("GameEvents.EventCreate", IntPtr.Zero);
            services.AddInstance<ITeams>(new Teams(new RecordingLog(), Double, Double, registry,
                                                   new Players(new RecordingLog(), Double, Double, Double, TestServices.NoButtons),
                                                   observers, Double, Double));
            // The throw warning leaves through the module's Log — the side's
            // own console — which routes to the side's ConsoleLines while no
            // engine is attached; the tap is how the fact reads it.
            services.Require<ConsoleLines>().Tap = (text, warning) => Lines.Add((text, warning));

            registry.KeyValue(Player, EntityNames.SsPlayer, "targetname", "p0");
            var p = (SsPlayer)registry.Get(Player)!;
            p.State.BindLocal();
            p.State.Alive = true;
            p.State.ObserverTarget = EntityHandle.None.Value;
        }

        private int _boxed;
        public void Register(IClientCommandHook hook)
        {
            var marker = _boxed++ switch { 0 => typeof(M1), 1 => typeof(M2), _ => typeof(M3) };
            ((ServiceContainer)Module.Services).AddInstance(
                typeof(Boxed<>).MakeGenericType(marker),
                Activator.CreateInstance(typeof(Boxed<>).MakeGenericType(marker), hook)!);
        }

        public CommandResult Send(string command) => Module.OnClientCommand(Player, command);
    }

    //-- a claimed word: the hook gets verb and args apart, the core does not --

    [Fact]
    public void AClaimingHookReceivesTheVerbApartFromItsArgs()
    {
        var rig = new Rig();
        var claimed = new Hook((_, verb, args) => verb == "joinclass" && args.SequenceEqual(new[] { "scout" }), order: 5);
        rig.Register(claimed);

        var result = rig.Send("joinclass scout");

        Assert.Equal(CommandResult.Handled, result);
        var call = Assert.Single(claimed.Calls);
        Assert.Equal(Player, call.Player);
        Assert.Equal("joinclass", call.Verb);                    // the verb, apart...
        Assert.Equal(["scout"], call.Args);                      // ...from the args
    }

    [Fact]
    public void AClaimedWordReachesNoStockArmAndItsArgsStayWhole()
    {
        var rig = new Rig();
        // A hook that claims even a spectate-shaped word: the stock arm must
        // NOT run, so the player's observer mode must stay NONE. (The control
        // below sends the same word to a declining hook and the mode lands.)
        var thief = new Hook((_, _, _) => true);
        rig.Register(thief);

        Assert.Equal(CommandResult.Handled, rig.Send("spectate"));

        var p = (SsPlayer)rig.Module.Registry.Get(Player)!;
        Assert.Equal(0, p.State.ObserverMode);                   // the stock arm never ran
        Assert.Equal(0, rig.Double.CountOf("Players.RemoveAllItems"));
        Assert.Equal(0, rig.Double.CountOf("Players.SetTeam"));          // nor the team store
    }

    //-- the unclaimed word: today's behaviour, unchanged --

    [Fact]
    public void AnUnclaimedSpectateStillReachesTheStockObserversArm()
    {
        var rig = new Rig();
        rig.Clients.SpecMode = "7";                               // cl_spec_mode: roaming
        var declining = new Hook((_, _, _) => false, order: -5);  // asked, and passes
        rig.Register(declining);

        Assert.Equal(CommandResult.Handled, rig.Send("spectate"));

        // THE STIMULUS: the stock arm really ran — mode landed, items stripped.
        var p = (SsPlayer)rig.Module.Registry.Get(Player)!;
        Assert.Equal((int)ObserverMode.Roaming, p.State.ObserverMode);
        Assert.Single(declining.Calls);                                 // the hook WAS asked first
        Assert.Equal("spectate", declining.Calls[0].Verb);
        Assert.Contains(rig.Double.AllOf("Players.RemoveAllItems"), c => (bool)c.Args[1]!);

        // Stock's spectate is HandleCommand_JoinTeam(TEAM_SPECTATOR): the
        // team is stored, once, as team 1.
        var stored = Assert.Single(rig.Double.AllOf("Players.SetTeam"));
        Assert.Equal(Observers.TeamSpectator, Assert.IsType<int>(stored.Args[1]));
    }

    [Fact]
    public void WithNoHookRegisteredSpectateReachesTheStockObserversArm()
    {
        // The no-owner arm, with NOTHING registered (the facts above register
        // a declining hook): the chain is empty and the word is the core's.
        var rig = new Rig();
        rig.Clients.SpecMode = "7";                               // cl_spec_mode: roaming

        Assert.Empty(rig.Module.Services.Require<IHooks<IClientCommandHook>>().All);
        Assert.Equal(CommandResult.Handled, rig.Send("spectate"));

        var p = (SsPlayer)rig.Module.Registry.Get(Player)!;
        Assert.Equal((int)ObserverMode.Roaming, p.State.ObserverMode);
        Assert.Contains(rig.Double.AllOf("Players.RemoveAllItems"), c => (bool)c.Args[1]!);
        var stored = Assert.Single(rig.Double.AllOf("Players.SetTeam"));
        Assert.Equal(Observers.TeamSpectator, Assert.IsType<int>(stored.Args[1]));
    }

    [Fact]
    public void JointeamParsesItsArgumentAsStocksAtoi()
    {
        // CHL2MP_Player::ClientCommand: `int iTeam = atoi( args[1] )` — the
        // leading digits, so `jointeam 2x` is team 2; a strict parse made it
        // team 0 (refused with the invalid-team warning).
        var rig = new Rig();

        Assert.Equal(CommandResult.Handled, rig.Send("jointeam 2x"));

        var stored = Assert.Single(rig.Double.AllOf("Players.SetTeam"));
        Assert.Equal(2, Assert.IsType<int>(stored.Args[1]));
    }

    //-- the chain: order, and the first true stops it --

    [Fact]
    public void HooksRunInHookOrderAndTheFirstTrueStopsTheChain()
    {
        var rig = new Rig();
        var second = new Hook((_, _, _) => true, order: 10);
        // A plain recorder, no Assert.Fail: a throw here would be swallowed
        // by the very catch this seam has. Being reached shows in its Calls,
        // which the assert below reads.
        var third = new Hook((_, _, _) => true, order: 20);
        // Registered late-to-early on purpose: the order that dispatches is
        // HookOrder's, not registration's.
        rig.Register(third);
        rig.Register(second);

        Assert.Equal(CommandResult.Handled, rig.Send("joinclass scout"));

        Assert.Single(second.Calls);                               // the claimer, asked
        Assert.Empty(third.Calls);                                // and the next is never reached
    }

    [Fact]
    public void AFalsePassesToTheNextHookInOrder()
    {
        var rig = new Rig();
        var first = new Hook((_, _, _) => false, order: 1);
        var second = new Hook((_, _, _) => true, order: 2);
        rig.Register(second);
        rig.Register(first);

        Assert.Equal(CommandResult.Handled, rig.Send("joinclass scout"));

        Assert.Single(first.Calls);
        Assert.Single(second.Calls);
    }

    //-- a broken mod must not swallow everyone's words --

    [Fact]
    public void AThrowingHookIsNamedAndSkippedAndTheNextIsStillAsked()
    {
        var rig = new Rig();
        var broken = new Hook((_, _, _) => throw new InvalidOperationException("the mod is broken"), order: 1);
        var next = new Hook((_, _, _) => true, order: 2);
        rig.Register(next);
        rig.Register(broken);

        Assert.Equal(CommandResult.Handled, rig.Send("joinclass scout"));

        Assert.Single(broken.Calls);                    // it was asked, and it threw
        Assert.Single(next.Calls);                              // and the chain carried on
        Assert.Contains("the mod is broken", string.Concat(rig.Lines.Select(l => l.Text)));
        // Named: the warning says WHICH hook broke, and says it as a warning.
        Assert.Contains(nameof(Hook), string.Concat(rig.Lines.Select(l => l.Text)));
        Assert.Contains(rig.Lines, l => l.Warning && l.Text.Contains("the mod is broken"));
    }

    [Fact]
    public void WhenNobodyClaimsTheWordFallsThroughToTheCore()
    {
        var rig = new Rig();
        var declining = new Hook((_, _, _) => false, order: 1);
        rig.Register(declining);

        // spec_next is the core's own verb: unclaimed by the hook, it still
        // dispatched there (Handled, not NotHandled).
        Assert.Equal(CommandResult.Handled, rig.Send("spec_next"));
        Assert.Single(declining.Calls);
    }
}
