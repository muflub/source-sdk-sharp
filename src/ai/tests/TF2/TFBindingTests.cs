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
/// and until the trees land its decider stands still.
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
            [.. typeof(global::TF2.Server.Player.TFServerActorFacts).Assembly.GetTypes(), .. typeof(VisionPass).Assembly.GetTypes(), .. Binding.GetTypes()]);
        Assert.Contains(typeof(IBotGame), registered);
        Assert.Contains(typeof(TFAiService), registered);
        Assert.Contains(typeof(AiSenses), registered);
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
        Assembly[] ai = [typeof(AiVector).Assembly, typeof(AiBots).Assembly, Binding, typeof(SourceSharp.Ai.Hl2mp.Hl2mpBotGame).Assembly];
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

    [Fact]
    public void TheTF2DeciderStandsStill()
    {
        var game = new TFBotGame(WeaponCatalog.Stock(), null!, null!, null!);
        var duel = new Trees.Duel(Trees.Duel.Preset("normal"));
        var bot = new ActorId(1, 1);
        IDecider decider = game.Decider(new BrainSetup(bot, 7UL, duel.Memory, duel.Params, 0.015f));
        IntentCommand intent = decider.Decide(new Tick(100), new PerceivedView(4), 3);
        Assert.Equal(bot, intent.Bot);
        Assert.Equal(MoveKind.Hold, intent.Move.Kind);
        Assert.Equal(ActionKind.None, intent.Action.Kind);
    }
}
