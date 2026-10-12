extern alias client;
using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Server;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A mod's assemblies join a side's container by attribute and by registrar
/// (plan_host_sdk §2.2): <c>[SideService]</c> classes and
/// <see cref="ISideServiceRegistrar"/>s, found by the scan, registered after
/// the core and never over it, connected to hooks by the drivers.
/// </summary>
public sealed class SideServicesTests
{
    public interface ICoreThing;
    public sealed class CoreThing : ICoreThing;

    public interface IGreeter { string Greet(); }

    [SideService(Side.Server, As = [typeof(IGreeter)])]
    public sealed class Greeter(ICoreThing core) : IGreeter
    {
        public ICoreThing Core { get; } = core;
        public string Greet() => "hello";
    }

    [SideService(Side.Server)]
    public sealed class UsesGreeter(IGreeter greeter)
    {
        public IGreeter Greeter { get; } = greeter;
    }

    [SideService(Side.Client)]
    public sealed class ClientOnly;

    public sealed class Unmarked : IFrameLifecycle
    {
        public int FrameOrder => ModStages.First + 99;
    }

    [SideService(Side.Server)]
    public sealed class FrameHook : IFrameLifecycle
    {
        public int Thinks { get; private set; }
        public int FrameOrder => ModStages.First + 1;
        public void OnThink(float frameTime) => Thinks++;
    }

    public interface IFromRegistrar;
    public sealed class FromRegistrar : IFromRegistrar;
    public interface IMade { int Serial { get; } }
    public sealed record Made(int Serial) : IMade;
    public interface IHanded;
    public sealed class Handed : IHanded;

    [SideService(Side.Server)]
    public sealed class Registrar(ICoreThing core) : ISideServiceRegistrar
    {
        public static int FactoryRuns;
        public static ICoreThing? SeenCore;
        public static readonly Handed TheInstance = new();

        public void Register(ISideServiceRegistry registry)
        {
            SeenCore = core;
            registry.Add<IFromRegistrar, FromRegistrar>();
            registry.AddInstance<IHanded>(TheInstance);
            registry.AddFactory<IMade>(_ => new Made(++FactoryRuns));
        }
    }

    [SideService(Side.Client)]
    public sealed class ClientRegistrar : ISideServiceRegistrar
    {
        public static bool Ran;
        public void Register(ISideServiceRegistry registry) => Ran = true;
    }

    private static ServiceContainer Core()
    {
        var c = new ServiceContainer();
        c.AddInstance<ICoreThing>(new CoreThing());
        return c;
    }

    private static IReadOnlyList<Type> Register(ServiceContainer c, params Type[] types) =>
        SideServices.Register(c, Side.Server, types);

    [Fact]
    public void A_marked_service_is_resolved_with_core_services_injected()
    {
        var c = Core();
        Register(c, typeof(Greeter));

        var greeter = c.Require<Greeter>();
        Assert.Same(c.Require<ICoreThing>(), greeter.Core);
    }

    [Fact]
    public void As_registers_the_same_instance_under_the_contract()
    {
        var c = Core();
        Register(c, typeof(Greeter));

        Assert.Same(c.Require<Greeter>(), c.Require<IGreeter>());
    }

    [Fact]
    public void A_mod_service_is_injected_into_another_mod_service()
    {
        var c = Core();
        Register(c, typeof(Greeter), typeof(UsesGreeter));

        Assert.Same(c.Require<IGreeter>(), c.Require<UsesGreeter>().Greeter);
    }

    [Fact]
    public void The_other_sides_services_and_registrars_are_not_taken()
    {
        var c = Core();
        ClientRegistrar.Ran = false;
        var registered = Register(c, typeof(ClientOnly), typeof(ClientRegistrar), typeof(Greeter));

        Assert.False(c.IsRegistered(typeof(ClientOnly)));
        Assert.False(ClientRegistrar.Ran);
        Assert.Contains(typeof(Greeter), registered);   // positive arm: the scan did run
    }

    [Fact]
    public void An_unmarked_hook_is_not_picked_up_and_a_marked_one_is_driven()
    {
        var c = Core();
        Register(c, typeof(Unmarked), typeof(FrameHook));

        var driver = new FrameDriver(new RecordingLog(), c);

        Assert.DoesNotContain(driver.Participants, p => p is Unmarked);
        var hook = Assert.Single(driver.Participants.OfType<FrameHook>());
        driver.Think(0.015f);
        Assert.Equal(1, hook.Thinks);
    }

    [Fact]
    public void A_registrar_binds_adds_an_instance_and_a_lazy_factory()
    {
        var c = Core();
        Registrar.FactoryRuns = 0;
        Register(c, typeof(Registrar));

        Assert.Same(c.Require<ICoreThing>(), Registrar.SeenCore);
        Assert.IsType<FromRegistrar>(c.Require<IFromRegistrar>());
        Assert.Same(Registrar.TheInstance, c.Require<IHanded>());
        Assert.Equal(0, Registrar.FactoryRuns);
        Assert.Same(c.Require<IMade>(), c.Require<IMade>());
        Assert.Equal(1, Registrar.FactoryRuns);
    }

    [SideService(Side.Server, As = [typeof(ICoreThing)])]
    public sealed class ReplacesCore : ICoreThing;

    [Fact]
    public void Replacing_a_core_registration_is_refused_naming_the_mod()
    {
        var c = Core();

        var e = Assert.Throws<ServiceResolutionException>(() => Register(c, typeof(ReplacesCore)));
        Assert.Contains(nameof(ReplacesCore), e.Message);
        Assert.Contains("core already registered", e.Message);
        Assert.IsType<CoreThing>(c.Require<ICoreThing>());
    }

    [SideService(Side.Server, As = [typeof(IGreeter)])]
    public sealed class SecondGreeter : IGreeter { public string Greet() => "hi"; }

    [Fact]
    public void Two_mod_registrations_of_one_contract_are_refused_naming_both()
    {
        var c = Core();

        var e = Assert.Throws<ServiceResolutionException>(() => Register(c, typeof(Greeter), typeof(SecondGreeter)));
        Assert.Contains(nameof(Greeter), e.Message);
        Assert.Contains(nameof(SecondGreeter), e.Message);
    }

    [SideService(Side.Server)]
    public sealed class CycleA(CycleB b) { public CycleB B { get; } = b; }

    [SideService(Side.Server)]
    public sealed class CycleB(CycleA a) { public CycleA A { get; } = a; }

    [Fact]
    public void A_cycle_between_mod_services_is_reported_with_its_path()
    {
        var c = Core();
        Register(c, typeof(CycleA), typeof(CycleB));

        var e = Assert.Throws<ServiceResolutionException>(() => c.Resolve<CycleA>());
        Assert.Contains("CycleA -> CycleB -> CycleA", e.Message);
    }

    [SideService(Side.Server, As = [typeof(IGreeter)])]
    public sealed class LiesAboutContract;

    [Fact]
    public void As_a_contract_the_class_does_not_implement_is_refused()
    {
        Assert.Throws<ServiceResolutionException>(() => Register(Core(), typeof(LiesAboutContract)));
    }

    [SideService(Side.Server)]
    public sealed class Disposes : IDisposable
    {
        public static int Disposed;
        public void Dispose() => Disposed++;
    }

    [SideService(Side.Server)]
    public sealed class NeverBuilt : IDisposable
    {
        public static int Built;
        public NeverBuilt() => Built++;
        public void Dispose() { }
    }

    [Fact]
    public void Shutdown_disposes_what_was_built_once_and_builds_nothing()
    {
        var c = Core();
        Disposes.Disposed = 0;
        NeverBuilt.Built = 0;
        var registered = Register(c, typeof(Disposes), typeof(NeverBuilt));
        c.Require<Disposes>();

        int n = SideServices.DisposeBuilt(c, registered, new RecordingLog());

        Assert.Equal(1, n);
        Assert.Equal(1, Disposes.Disposed);
        Assert.Equal(0, NeverBuilt.Built);
    }

    [Fact]
    public void The_core_assemblies_declare_no_side_services()
    {
        // The test mod lists only the core's assemblies in mods.yaml, so this
        // is what keeps its containers exactly as they were: nothing in them is
        // marked, and so nothing new is registered.
        var core = new[]
        {
            typeof(SideServices).Assembly,
            typeof(ServerModule).Assembly,
            typeof(client::SourceSharp.Client.ClientModule).Assembly,
        };
        var types = core.SelectMany(a => a.GetTypes()).ToArray();

        var c = Core();
        var before = c.ServiceTypes.Count;
        var registered = SideServices.Register(c, Side.Server, types)
            .Concat(SideServices.Register(c, Side.Client, types)).ToArray();

        Assert.Empty(registered);
        Assert.Equal(before, c.ServiceTypes.Count);
        Assert.True(types.Length > 1000, $"the scan saw {types.Length} types; the core has thousands");
    }
}
