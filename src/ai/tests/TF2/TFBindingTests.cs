using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.Tests.Boundary;
using SourceSharp.Ai.TF2;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// TF2's AI binding (plan_bots_teams_tf2 C.2, P6 step 1): it builds on the
/// generic server binding and answers <see cref="IBotGame"/>; it names no
/// HL2MP (the other game's binding is not its business, nor its vocabulary);
/// its services register beside the tf2 mod's list without a contract twice;
/// and its decider is TF2's tree.
/// </summary>
public sealed class TFBindingTests
{
    private static readonly Assembly Binding = typeof(TFBotGame).Assembly;

    [Fact]
    public void TheTF2BindingReferencesNoOtherGamesBinding()
        => Assert.DoesNotContain(Binding.GetReferencedAssemblies(), a => a.Name!.StartsWith("SourceSharp.Ai.Hl2mp", StringComparison.Ordinal));

    [Fact]
    public void TheTF2BindingNamesNoHl2mp()
        => Assert.Empty(CoreReferenceTests.Naming(Binding, "HL2MP"));

    [Fact]
    public void TheServerBindingNamesNoTF2()
        => Assert.Empty(CoreReferenceTests.Naming(typeof(AiBots).Assembly, "TF2"));

    /// <summary>The tf2 mod's server list, as mods.yaml has it: the mod, the generic binding, TF2's.</summary>
    [Fact]
    public void EveryServiceOfTheTF2ListRegistersOnce()
    {
        var services = new ServiceContainer();
        IReadOnlyList<Type> registered = SideServices.Register(services, Side.Server,
            [.. typeof(global::TF2.Server.Player.TFServerActorFacts).Assembly.GetTypes(), .. typeof(SourceSharp.Ai.Store.SqliteExperienceStores).Assembly.GetTypes(),
             .. typeof(VisionPass).Assembly.GetTypes(), .. Binding.GetTypes()]);
        Assert.Contains(typeof(IBotGame), registered);
        // The bots' experience database (Part G, G.3): the tf2 list names its assembly.
        Assert.Contains(typeof(IExperienceStores), registered);
        // The feed that fills it, and TF2's answers to the feed.
        Assert.Contains(typeof(SourceSharp.Ai.Server.Experience.ExperienceService), registered);
        Assert.Contains(typeof(SourceSharp.Ai.Server.Experience.IExperienceGame), registered);
        Assert.Contains(typeof(TFAiService), registered);
        // Without curves a brain has no decider: the TF2 list must bring its own.
        Assert.Contains(typeof(SourceSharp.Ai.Server.Skills.ISkillCurveSource), registered);
        Assert.Contains(typeof(AiSenses), registered);
    }

    /// <summary>
    /// The frame and the level sequences on a TF2 server, as the tf2 mod's list registers them: no
    /// two members at one order in the mods' range. Two at one order throw as the server side loads
    /// (StageCollisionException), which no fact saw: live, a tap of this binding given the
    /// experience feed's frame order stopped the game at its first map (plan_bots_teams_tf2_part_g G2).
    /// </summary>
    [Theory]
    [InlineData(typeof(IFrameLifecycle), nameof(IFrameLifecycle.FrameOrder))]
    [InlineData(typeof(IGameLifecycle), nameof(IGameLifecycle.LifecycleOrder))]
    public void NoTwoServicesOfTheTF2ListShareAnOrderInTheModsRange(Type sequence, string order)
    {
        var services = new ServiceContainer();
        IReadOnlyList<Type> registered = SideServices.Register(services, Side.Server,
            [.. typeof(global::TF2.Server.Player.TFServerActorFacts).Assembly.GetTypes(), .. typeof(SourceSharp.Ai.Store.SqliteExperienceStores).Assembly.GetTypes(),
             .. typeof(VisionPass).Assembly.GetTypes(), .. Binding.GetTypes()]);
        PropertyInfo read = sequence.GetProperty(order)!;
        var members = registered.Where(t => t.IsClass && !t.IsAbstract && sequence.IsAssignableFrom(t)).Distinct()
                                .Select(t => (Type: t, Order: (int)read.GetValue(System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t))!))
                                .Where(m => m.Order >= ModStages.First).ToArray();
        Assert.Contains(members, m => m.Type == typeof(SourceSharp.Ai.TF2.Senses.TFNemesisTap));
        Assert.Empty(members.GroupBy(m => m.Order).Where(g => g.Count() > 1).Select(g => $"{g.Key - ModStages.First}: {string.Join(", ", g.Select(m => m.Type.Name))}"));
    }

    /// <summary>
    /// Each hook chain the AI's services join on a TF2 server, as the tf2 mod's list registers it:
    /// no two members at one <see cref="IOrderedHook.HookOrder"/> (an unordered one is 0). Two at one
    /// order throw only at the chain's first call (StageUniqueness), so the boot passes and the
    /// facts stay green: live, every kill threw out of OnPlayerKilled (CombatTap and TFStickyTray).
    /// </summary>
    [Fact]
    public void NoHookChainAnAiServiceJoinsHasTwoMembersAtOneOrder()
    {
        var services = new ServiceContainer();
        IReadOnlyList<Type> registered = SideServices.Register(services, Side.Server,
            [.. typeof(global::TF2.Server.Player.TFServerActorFacts).Assembly.GetTypes(), .. typeof(VisionPass).Assembly.GetTypes(), .. Binding.GetTypes()]);
        Type[] concrete = [.. registered.Where(t => t.IsClass && !t.IsAbstract).Distinct()];
        Assembly[] ai = [typeof(VisionPass).Assembly, Binding];
        static int Order(Type t)
            => typeof(IOrderedHook).IsAssignableFrom(t)
                ? ((IOrderedHook)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t)).HookOrder
                : 0;
        var chains = concrete.Where(t => ai.Contains(t.Assembly))
                             .SelectMany(t => t.GetInterfaces().Where(i => (i.Name.EndsWith("Hook", StringComparison.Ordinal) && i != typeof(IOrderedHook))
                                                                           || i == typeof(Debug.Api.Server.Interfaces.IDebugRouteProvider)
                                                                           || i == typeof(SourceSharp.Ai.TF2.Senses.ITFHudMeters)))
                             .Distinct().ToArray();
        Assert.Contains(chains, c => c.Name == "IPlayerDeathHook");
        // The dev API's route providers are a chain too (lane G9: two routes at one order made every request fail live).
        Assert.Contains(chains, c => c.Name == "IDebugRouteProvider");
        // The class meter readers are a chain (P7: two readers at one order made every sense tick throw live).
        Assert.Contains(chains, c => c.Name == "ITFHudMeters");
        string[] clashes = [.. chains.SelectMany(chain => concrete.Where(chain.IsAssignableFrom).GroupBy(Order).Where(g => g.Count() > 1)
                                                         .Select(g => $"{chain.Name} at {g.Key}: {string.Join(", ", g.Select(t => t.Name))}"))];
        Assert.Empty(clashes);
    }

    /// <summary>
    /// What an AI service of <paramref name="types"/>' list asks for by constructor and no
    /// service of the list registers: the AI's own contracts (interfaces) only (the core's and the mod's
    /// services are the game's), and never a parameter with a default. A binding that left one
    /// out stopped the server at load ("AiBots cannot be constructed: ... IBotNameSource").
    /// </summary>
    internal static string[] Unfilled(IEnumerable<Type> types)
    {
        var services = new ServiceContainer();
        Type[] all = [.. types];
        IReadOnlyList<Type> registered = SideServices.Register(services, Side.Server, all);
        Assembly[] ai = [typeof(AiVector).Assembly, typeof(AiBots).Assembly, typeof(SourceSharp.Ai.Server.Nav.NavFollower).Assembly, Binding, typeof(SourceSharp.Ai.Hl2mp.Hl2mpBotGame).Assembly];
        return [.. registered.Where(t => ai.Contains(t.Assembly)).Distinct()
                             .SelectMany(t => t.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => (t, p)))
                             .Where(x => !x.p.HasDefaultValue && x.p.ParameterType.IsInterface && ai.Contains(x.p.ParameterType.Assembly) && !services.IsRegistered(x.p.ParameterType))
                             .Select(x => $"{x.t.Name}({x.p.ParameterType.Name} {x.p.Name})").Distinct().Order(StringComparer.Ordinal)];
    }

    [Fact]
    public void TheTestModsListLeavesNoAiContractUnfilled()
        // The control: the list that boots today.
        => Assert.Empty(Unfilled([.. typeof(VisionPass).Assembly.GetTypes(), .. typeof(SourceSharp.Ai.Hl2mp.Hl2mpBotGame).Assembly.GetTypes()]));

    [Fact]
    public void TheCensusSeesAContractNobodyFills()
        // Without a game's binding the generic one asks for what only a game answers.
        => Assert.Contains(Unfilled(typeof(VisionPass).Assembly.GetTypes()), u => u.Contains(nameof(IBotGame), StringComparison.Ordinal));

    [Fact]
    public void TheTF2ListLeavesNoAiContractUnfilled()
        => Assert.Empty(Unfilled([.. typeof(global::TF2.Server.Player.TFServerActorFacts).Assembly.GetTypes(), .. typeof(VisionPass).Assembly.GetTypes(), .. Binding.GetTypes()]));

    /// <summary>The game's decider is TF2's tree, a subtree per class (P6 step 6); before the class table loads it reads the Soldier's numbers.</summary>
    [Fact]
    public void TheTF2GameDecidesWithItsTree()
    {
        var classes = new global::TF2.Shared.TFClassTable(new NoLog(), DispatchProxy.Create<IEngineFiles, ServerNav.AiNavBuildTests.Files>());
        var game = new TFBotGame(WeaponCatalog.Stock(), null!, classes, null!, null!, TFFeelSwitchTests.Switches());
        var duel = new Trees.Duel(Trees.Duel.Preset("normal"));
        var bot = new ActorId(1, 1);
        IDecider decider = game.Decider(new BrainSetup(bot, 7UL, duel.Memory, duel.Params, 0.015f));
        Assert.IsType<SourceSharp.Ai.TF2.Trees.TFDecider>(decider);
        // Dead (the view's default self): it holds.
        IntentCommand intent = decider.Decide(new Tick(100), new PerceivedView(4), 3);
        Assert.Equal((bot, MoveKind.Hold, ActionKind.None), (intent.Bot, intent.Move.Kind, intent.Action.Kind));
    }

    /// <summary>The wiring: a bot's decider has the feel layer's switches as the convars stood when it was added, and a later change is the next bot's.</summary>
    [Fact]
    public void ADeciderHasTheFeelSwitchesAsTheConVarsStoodWhenItsBotWasAdded()
    {
        var classes = new global::TF2.Shared.TFClassTable(new NoLog(), DispatchProxy.Create<IEngineFiles, ServerNav.AiNavBuildTests.Files>());
        TFFeelGlanceConVar glance = DeclaredConVars.Fresh<TFFeelGlanceConVar>();
        var feel = new TFFeelSwitchConVars(DeclaredConVars.Fresh<TFFeelCarrierConVar>(), DeclaredConVars.Fresh<TFFeelSearchConVar>(), DeclaredConVars.Fresh<TFFeelAimConVar>(),
                                           DeclaredConVars.Fresh<TFFeelRetreatConVar>(), DeclaredConVars.Fresh<TFFeelFriendGateConVar>(), glance,
                                           DeclaredConVars.Fresh<TFFeelPrefireConVar>(), DeclaredConVars.Fresh<TFFeelJumpHealthConVar>(),
                                           DeclaredConVars.Fresh<TFFeelFightConVar>(), DeclaredConVars.Fresh<TFFeelRoutesConVar>());
        var game = new TFBotGame(WeaponCatalog.Stock(), null!, classes, null!, null!, feel);
        var duel = new Trees.Duel(Trees.Duel.Preset("normal"));
        var setup = new BrainSetup(new ActorId(1, 1), 7UL, duel.Memory, duel.Params, 0.015f);
        var before = (SourceSharp.Ai.TF2.Trees.TFDecider)game.Decider(setup);
        glance.Hold("0");
        var after = (SourceSharp.Ai.TF2.Trees.TFDecider)game.Decider(setup);
        var all = SourceSharp.Ai.TF2.Trees.TFFeelSwitches.All;
        Assert.Equal((all, all with { Glance = false }), (before.Feel, after.Feel));
    }

    private sealed class NoLog : ILog
    {
        public void Msg(string message)
        {
        }

        public void Warning(string message)
        {
        }
    }
}
