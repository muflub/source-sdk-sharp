using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The container, on its own, with nothing else registered in it.
///
/// Worth testing closely rather than trusting to a smoke test, because every
/// mistake it can make is silent from the outside: an entity handed the wrong
/// instance still spawns, still takes keyvalues and still thinks — it just
/// consults something that is not the live system, and nothing anywhere says
/// so. The failures this checks for are all of that shape.
/// </summary>
public class ServiceContainerTests
{
    //-------------------------------------------------------------------------
    // Fixtures. Deliberately trivial: what is under test is the wiring, and a
    // fixture with behaviour of its own would only make a failure ambiguous.
    //-------------------------------------------------------------------------

    public interface IThing { int Id { get; } }
    public interface IOther { }

    public sealed class Thing : IThing
    {
        public Thing() { Id = ++s_made; }
        private static int s_made;
        public int Id { get; }

        /// <summary>How many have ever been built. A lazy singleton makes one.</summary>
        public static int Made => s_made;
    }

    public sealed class NeedsThing
    {
        public NeedsThing(IThing thing) { Thing = thing; }
        public IThing Thing { get; }
    }

    public sealed class NeedsThingOptionally
    {
        public NeedsThingOptionally(IOther? other = null) { Other = other; }
        public IOther? Other { get; }
    }

    public sealed class NeedsLocator
    {
        public NeedsLocator(IServiceLocator services) { Services = services; }
        public IServiceLocator Services { get; }
    }

    public sealed class NeedsNamedConVar
    {
        public NeedsNamedConVar([Named("test_speed")] ConVarBase speed) { Speed = speed; }
        public ConVarBase Speed { get; }
    }

    public sealed class NeedsNamedAsWrongType
    {
        public NeedsNamedAsWrongType([Named("test_speed")] IConCommand command) { Command = command; }
        public IConCommand Command { get; }
    }

    public sealed class Ambiguous
    {
        public Ambiguous(IThing a, IOther b) { }
        public Ambiguous(IOther a, IThing b) { }
    }

    public sealed class Marked
    {
        public Marked(IThing a, IOther b) { Which = "wide"; }

        [Inject]
        public Marked(IThing a) { Which = "marked"; }

        public string Which { get; }
    }

    public sealed class Widest
    {
        public Widest() { Which = "empty"; }
        public Widest(IThing a) { Which = "one"; }
        public string Which { get; }
    }

    public sealed class Throws
    {
        public Throws() => throw new InvalidOperationException("the constructor said no");
    }

    // A pair that want each other. Only reachable through lazy registration —
    // two instances handed over ready-made cannot cycle, because they already
    // exist.
    public sealed class Ping { public Ping(Pong pong) { } }
    public sealed class Pong { public Pong(Ping ping) { } }

    private sealed class TestConVar : StringConVar { }

    private sealed class TestCommand : IConCommand
    {
        public void Execute(string args) { }
    }

    //-------------------------------------------------------------------------

    [Fact]
    public void Instances()
    {
        var container = new ServiceContainer();
        var thing = new Thing();

        container.AddInstance<IThing>(thing);

        Assert.True(ReferenceEquals(container.Resolve<IThing>(), thing), "an instance comes back by its contract");
        Assert.True(container.Resolve(typeof(Thing)) is null, "and not by its concrete type, which was not registered");
        Assert.True(container.Resolve<IOther>() is null, "a type nobody registered resolves to null");
        Assert.True(container.IsRegistered<IThing>() && !container.IsRegistered<IOther>(), "IsRegistered agrees");

        // The container answers for itself, which is what a registry that
        // creates entities later on needs.
        Assert.True(ReferenceEquals(container.Resolve<IServiceLocator>(), container), "the container resolves as its own locator");
    }

    [Fact]
    public void LazySingletons()
    {
        int before = Thing.Made;

        var container = new ServiceContainer();
        container.Add<IThing, Thing>();

        Assert.True(Thing.Made == before, "declaring a lazy service builds nothing yet");

        var first = container.Resolve<IThing>();
        var second = container.Resolve<IThing>();

        Assert.True(Thing.Made == before + 1, "the first ask builds it");
        Assert.True(ReferenceEquals(first, second), "the second ask hands back the same one");
        Assert.True(Thing.Made == before + 1, "and builds nothing more");
    }

    [Fact]
    public void NamedLookup()
    {
        var container = new ServiceContainer();
        var convar = new TestConVar();
        var command = new TestCommand();

        container.AddNamed("test_speed", convar);
        container.AddNamed("test_go", command);

        Assert.True(ReferenceEquals(container.ConVar("test_speed"), convar), "a convar comes back by its console name");
        Assert.True(ReferenceEquals(container.Command("test_go"), command), "a command comes back by its console name");
        Assert.True(ReferenceEquals(container.ConVar("TEST_SPEED"), convar), "the console's case-insensitivity is honoured");
        Assert.True(container.ResolveNamed("test_nothing") is null, "a name nobody registered is null");

        // Asking for the wrong kind under a real name is the mistake a string
        // key makes possible, so it must not come back as something.
        Assert.True(container.Command("test_speed") is null, "a convar asked for as a command is null");

        Assert.True(container.IsNameRegistered("test_go") && !container.IsNameRegistered("test_nothing"), "IsNameRegistered agrees");
    }

    [Fact]
    public void Activation()
    {
        var container = new ServiceContainer();
        var thing = new Thing();
        var convar = new TestConVar();

        container.AddInstance<IThing>(thing);
        container.AddNamed("test_speed", convar);

        var needs = container.Create<NeedsThing>();
        Assert.True(ReferenceEquals(needs.Thing, thing), "a constructor parameter is filled from the container");

        var named = container.Create<NeedsNamedConVar>();
        Assert.True(ReferenceEquals(named.Speed, convar), "[Named] fills from the name table");

        var locator = container.Create<NeedsLocator>();
        Assert.True(ReferenceEquals(locator.Services, container), "a class may ask for the container itself");

        var optional = container.Create<NeedsThingOptionally>();
        Assert.True(optional.Other is null, "an unregistered service with a default falls back to it");

        // Two instances of the same type are two objects. The container holds
        // one of each SERVICE, not one of everything it can build — which is
        // the whole distinction an entity depends on.
        var a = container.Create<NeedsThing>();
        var b = container.Create<NeedsThing>();
        Assert.True(!ReferenceEquals(a, b), "Create makes a new instance each time");
        Assert.True(ReferenceEquals(a.Thing, b.Thing), "and hands both the same service");
    }

    [Fact]
    public void ConstructorChoice()
    {
        var container = new ServiceContainer();
        container.AddInstance<IThing>(new Thing());

        Assert.True(container.Create<Marked>().Which == "marked", "[Inject] wins over the wider constructor");

        Assert.True(container.Create<Widest>().Which == "one", "with no [Inject] the widest is chosen");

        // ...but only if it can actually be filled. An empty container cannot
        // fill the wide one, and the rule is deliberately NOT "fall back to a
        // narrower one": that would make the constructor a class runs depend on
        // what happens to be registered, which is invisible at the call site.
        var empty = new ServiceContainer();
        Assert.True(Threw(() => empty.Create<Widest>()), "and the choice does not silently narrow when it cannot be filled");
    }

    [Fact]
    public void Failures()
    {
        var container = new ServiceContainer();

        Assert.True(Threw(() => container.Create<NeedsThing>()), "a missing dependency with no default is an error");

        Assert.True(Threw(() => container.Create<Ambiguous>()), "two equally wide constructors are an error rather than a guess");

        Assert.True(Threw(() => container.Create(typeof(IThing))), "an interface cannot be built");

        // Registering something that is not what it claims to be.
        Assert.True(Threw(() => container.AddInstance(typeof(IOther), new Thing())), "an instance that is not the contract is refused");
        Assert.True(Threw(() => container.Add(typeof(IOther), typeof(Thing))), "an implementation that is not the contract is refused");

        // The cycle. Reported as a cycle, not as a stack overflow.
        var cyclic = new ServiceContainer();
        cyclic.Add<Ping>();
        cyclic.Add<Pong>();
        Assert.True(Threw(() => cyclic.Resolve<Ping>()), "services that depend on each other report a cycle");

        // TryCreate is what the registries use: a failure is a report, and the
        // next class in the loop still gets its turn.
        Assert.True(!container.TryCreate(typeof(NeedsThing), out var none, out string missing)
            && none is null && missing.Length > 0, "TryCreate reports a missing dependency rather than throwing");

        Assert.True(!container.TryCreate(typeof(Throws), out _, out string threw)
            && threw.Contains("the constructor said no"), "TryCreate also catches a constructor that throws");

        Assert.True(container.TryCreate(typeof(NeedsThingOptionally), out var made, out _) && made is not null, "and TryCreate succeeds where Create would");
    }

    [Fact]
    public void Listings()
    {
        var container = new ServiceContainer();
        container.AddInstance<IThing>(new Thing());
        container.AddNamed("test_speed", new TestConVar());

        Assert.True(container.ServiceTypes.Contains(typeof(IThing)), "the service listing includes what was registered");
        Assert.True(container.ServiceTypes.Contains(typeof(IServiceLocator)), "and the container itself, which is registered too");
        Assert.True(container.Names.Contains("test_speed"), "the name listing includes what was registered");

        // A second claim on one console name keeps the first. The engine will
        // already have refused one of the two declarations, and answering with
        // the loser would hand out the object whose writes go nowhere.
        var first = new TestConVar();
        var second = new TestConVar();
        var contested = new ServiceContainer();
        contested.AddNamed("test_dup", first);
        contested.AddNamed("test_dup", second);
        Assert.True(ReferenceEquals(contested.ResolveNamed("test_dup"), first), "a contested console name keeps the first claim");
    }

    //-------------------------------------------------------------------------
    // AddFactory: a service built by a function, once, on first resolve.
    //-------------------------------------------------------------------------

    [Fact]
    public void A_factory_runs_once_on_first_resolve()
    {
        var container = new ServiceContainer();
        int runs = 0;
        container.AddFactory<IOther>(_ => { runs++; return new Other(); });

        Assert.Equal(0, runs);
        var first = container.Resolve<IOther>();
        var second = container.Resolve<IOther>();

        Assert.Equal(1, runs);
        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void A_factory_is_given_the_container()
    {
        var container = new ServiceContainer();
        container.Add<IThing, Thing>();
        container.AddFactory(locator => new NeedsThing(locator.Require<IThing>()));

        Assert.Same(container.Resolve<IThing>(), container.Require<NeedsThing>().Thing);
    }

    [Fact]
    public void A_factory_aliases_a_service_under_a_second_contract_without_a_second_instance()
    {
        var container = new ServiceContainer();
        container.Add<Thing>();
        container.AddFactory<IThing>(locator => locator.Require<Thing>());

        Assert.Same(container.Resolve<Thing>(), container.Resolve<IThing>());
        Assert.Single(container.ResolveAll<IThing>());
    }

    [Fact]
    public void ResolveAll_finds_a_factory_registration_before_it_has_run()
    {
        var container = new ServiceContainer();
        container.AddFactory<IThing>(_ => new Thing());

        Assert.Single(container.ResolveAll<IThing>());
    }

    [Fact]
    public void A_factory_that_returns_null_or_the_wrong_type_is_refused()
    {
        var nulls = new ServiceContainer();
        nulls.AddFactory(typeof(IOther), _ => null!);
        var wrong = new ServiceContainer();
        wrong.AddFactory(typeof(IOther), _ => new Thing());

        Assert.True(Threw(() => nulls.Resolve(typeof(IOther))), "a null from a factory is refused");
        Assert.True(Threw(() => wrong.Resolve(typeof(IOther))), "a factory's object that is not the contract is refused");
    }

    [Fact]
    public void A_factory_cycle_is_reported_with_its_path()
    {
        var container = new ServiceContainer();
        container.AddFactory<IThing>(locator => { locator.Resolve<IOther>(); return new Thing(); });
        container.AddFactory<IOther>(locator => { locator.Resolve<IThing>(); return new Other(); });

        var e = Assert.Throws<ServiceResolutionException>(() => container.Resolve<IThing>());
        Assert.Contains("IThing -> IOther -> IThing", e.Message);
    }

    [Fact]
    public void Describe_lists_an_unbuilt_factory_as_a_factory()
    {
        var container = new ServiceContainer();
        container.AddFactory<IOther>(_ => new Other());

        var row = container.Describe().Single(r => r.Key == nameof(IOther));
        Assert.Equal("factory", row.Kind);
        Assert.False(row.Built);
    }

    public sealed class Other : IOther { }

    private static bool Threw(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ServiceResolutionException)
        {
            return true;
        }
    }
}
