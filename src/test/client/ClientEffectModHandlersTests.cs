using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A mod's client effects (stage 0.9): an <see cref="IClientEffectHandler"/>
/// a mod declares as a <c>[SideService(Side.Client)]</c> joins the managed
/// effects after the core's fixed list, and the dispatch asks it before the
/// native registrations (ClientEffectDispatch's order), as it asks the core's.
/// </summary>
public sealed class ClientEffectModHandlersTests
{
    private sealed class Natives(params string[] names) : IClientEffectRegistrations
    {
        public readonly List<int> Runs = [];

        public int Count => names.Length;

        public string? Name(int index) => index >= 0 && index < names.Length ? names[index] : null;

        public void Run(int index, in EffectData data) => Runs.Add(index);
    }

    /// <summary>A mod's effect, in the plan's spelling.</summary>
    [SideService(Side.Client, As = [typeof(IClientEffectHandler)])]
    public sealed class ModBurstEffect : IClientEffectHandler
    {
        public readonly List<EffectData> Runs = [];

        public string EffectName => "ModBurst";

        public void Dispatch(in EffectData data) => Runs.Add(data);
    }

    /// <summary>Two more under the plain mark: a contract claims once a side, its own type does not.</summary>
    [SideService(Side.Client)]
    public sealed class ModSparkEffect : IClientEffectHandler
    {
        public int Runs;

        public string EffectName => "ModSpark";

        public void Dispatch(in EffectData data) => Runs++;
    }

    [SideService(Side.Client)]
    public sealed class ModGlowEffect : IClientEffectHandler
    {
        public int Runs;

        public string EffectName => "ModGlow";

        public void Dispatch(in EffectData data) => Runs++;
    }

    /// <summary>A mod effect under a name the core already handles.</summary>
    [SideService(Side.Client)]
    public sealed class ModTracerEffect : IClientEffectHandler
    {
        public int Runs;

        public string EffectName => "Tracer";

        public void Dispatch(in EffectData data) => Runs++;
    }

    private sealed class CoreTracer : IClientEffectHandler
    {
        public int Runs;

        public string EffectName => "Tracer";

        public void Dispatch(in EffectData data) => Runs++;
    }

    private static ServiceContainer ModServices(params Type[] declared)
    {
        var services = new ServiceContainer();
        SideServices.Register(services, Side.Client, declared);
        return services;
    }

    [Fact]
    public void A_side_service_effect_handler_is_dispatched_for_its_effect_name()
    {
        var services = ModServices(typeof(ModBurstEffect));
        var handlers = new ClientEffectHandlers([new CoreTracer()]);
        Assert.Equal(1, handlers.AddFrom(services, new RecordingLog()));
        // A native registration of the same name too: the managed one is asked first.
        var natives = new Natives("Impact", "ModBurst");
        var dispatch = new ClientEffectDispatch(natives, TestServices.Fake().Returns("Console.FindConVarValue", "0"), handlers);

        dispatch.Dispatch("modburst", new EffectData { Scale = 3f });

        var mod = services.Require<ModBurstEffect>();
        Assert.Equal(3f, Assert.Single(mod.Runs).Scale);    // the mod's handler, with the data, case folded as stock
        Assert.Empty(natives.Runs);                        // and not the native registration
        Assert.Equal(1, dispatch.Ran);
    }

    [Fact]
    public void Several_mod_effect_handlers_join_under_the_plain_mark()
    {
        var services = ModServices(typeof(ModSparkEffect), typeof(ModGlowEffect));
        var handlers = new ClientEffectHandlers([]);
        Assert.Equal(2, handlers.AddFrom(services, new RecordingLog()));
        Assert.True(handlers.TryDispatch("ModSpark", new EffectData()) && handlers.TryDispatch("ModGlow", new EffectData()));
        Assert.Equal((1, 1), (services.Require<ModSparkEffect>().Runs, services.Require<ModGlowEffect>().Runs));
    }

    [Fact]
    public void A_mod_effect_under_a_core_effects_name_is_refused_and_said()
    {
        var services = ModServices(typeof(ModTracerEffect));
        var core = new CoreTracer();
        var handlers = new ClientEffectHandlers([core]);
        var log = new RecordingLog();

        Assert.Equal(0, handlers.AddFrom(services, log));
        handlers.TryDispatch("Tracer", new EffectData());

        Assert.Equal(1, core.Runs);                         // the core's keeps the name
        Assert.Equal(0, services.Require<ModTracerEffect>().Runs);
        Assert.Contains("ModTracerEffect handles effect 'Tracer', which", log.Text);
    }

    private sealed class CoreImpact : IClientEffectHandler
    {
        public int Runs;

        public string EffectName => "Impact";

        public void Dispatch(in EffectData data) => Runs++;
    }

    /// <summary>A side service that is not an effect: the container is not empty, it only holds no mod effect.</summary>
    [SideService(Side.Client)]
    public sealed class NotAnEffect;

    [Fact]
    public void With_no_mod_effect_handlers_the_core_list_is_exactly_as_before()
    {
        var services = ModServices(typeof(NotAnEffect));
        var tracer = new CoreTracer();
        var impact = new CoreImpact();
        var handlers = new ClientEffectHandlers([tracer, impact]);
        var log = new RecordingLog();

        Assert.Equal(0, handlers.AddFrom(services, log));

        Assert.True(handlers.TryDispatch("Tracer", new EffectData()) && handlers.TryDispatch("impact", new EffectData()),
            "both core effects still answer, case folded as stock");
        Assert.Equal((1, 1), (tracer.Runs, impact.Runs));
        Assert.False(handlers.TryDispatch("ModBurst", new EffectData()), "and nothing else joined");
        Assert.Equal(2, handlers.Counts.Count);
        Assert.Equal(string.Empty, log.Text);
    }
}
