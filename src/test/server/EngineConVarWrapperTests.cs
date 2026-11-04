using System.Reflection;
using System.Reflection.Emit;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>[EngineConVar]: a variable the engine holds, as a class like any other convar.</summary>
public class EngineConVarWrapperTests
{
    private const int WatchId = 0x10000;

    private sealed class FakeEngine : IConVarBackend
    {
        public readonly Dictionary<string, string> Held = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Registered = [];
        public readonly List<string> Watched = [];
        public int Reads { get; set; }

        public int Register(string name, string defaultValue, string help, int flags, float? min, float? max)
        {
            Registered.Add(name);
            return 1;
        }

        public int ExistingFlags(string name) => -1;
        public void Set(int id, string value) => throw new InvalidOperationException("a held variable is set by name");
        public string Get(int id) => throw new InvalidOperationException("a held variable is read by name");

        public string? ValueOf(string name)
        {
            Reads++;
            return Held.TryGetValue(name, out string? value) ? value : null;
        }

        public int Watch(string name)
        {
            Watched.Add(name);
            return WatchId + Watched.Count - 1;
        }

        public void SetNamed(string name, string value) => Held[name] = value;
    }

    [EngineConVar("r_test_held", Fallback = "7")]
    private sealed class Held : IntConVar { }

    // In a dynamic assembly, which a side's scan skips: a composition in this process would
    // otherwise find the two declarations and refuse to start.
    private static Type DeclaredToo()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("EngineConVarFixture"), AssemblyBuilderAccess.Run);
        TypeBuilder type = assembly.DefineDynamicModule("fixture").DefineType(
            "DeclaredToo", TypeAttributes.Public | TypeAttributes.Sealed, typeof(IntConVar));
        type.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(ConVarAttribute).GetConstructor([typeof(string), typeof(string)])!, ["r_test_held", "1"]));
        type.DefineDefaultConstructor(MethodAttributes.Public);
        return type.CreateType();
    }

    private static (ConVarRegistry Registry, FakeEngine Engine, Held Convar) Bound(string? engineValue)
    {
        var engine = new FakeEngine();
        if (engineValue is not null)
            engine.Held["r_test_held"] = engineValue;
        var registry = new ConVarRegistry(engine);
        registry.RegisterTypes([typeof(Held)]);
        return (registry, engine, Assert.IsType<Held>(registry.All.Single()));
    }

    [Fact]
    public void It_reads_what_the_engine_holds_not_its_fallback()
        => Assert.Equal(3, Bound("3").Convar.Value);

    [Fact]
    public void Nothing_is_registered_with_the_engine()
        => Assert.Empty(Bound("3").Engine.Registered);

    [Fact]
    public void Its_change_arrives_under_the_id_the_watch_answered()
    {
        var (registry, _, convar) = Bound("3");

        Assert.True(registry.NotifyChanged(WatchId, "9", "3"));

        Assert.Equal(9, convar.Value);
    }

    [Fact]
    public void A_read_asks_the_engine_nothing()
    {
        var (_, engine, convar) = Bound("3");
        int before = engine.Reads;

        Assert.Equal(3, convar.Value);

        Assert.True(before > 0);
        Assert.Equal(before, engine.Reads);
    }

    [Fact]
    public void Setting_it_goes_to_the_engine_by_name_and_keeps_what_the_engine_answers()
    {
        var (_, engine, convar) = Bound("3");

        convar.SetValue(5);

        Assert.Equal("5", engine.Held["r_test_held"]);
        Assert.Equal(5, convar.Value);
    }

    [Fact]
    public void Until_the_engine_holds_it_the_value_is_the_fallback()
        => Assert.Equal(7, Bound(null).Convar.Value);

    [Fact]
    public void A_refresh_picks_up_a_variable_that_has_appeared()
    {
        var (registry, engine, convar) = Bound(null);
        engine.Held["r_test_held"] = "11";

        registry.RefreshHeldByEngine();

        Assert.Equal(11, convar.Value);
    }

    [Fact]
    public void A_refresh_with_nothing_new_tells_no_one()
    {
        var (registry, _, convar) = Bound("3");
        int told = 0;
        convar.Subscribe((_, _) => told++);

        registry.RefreshHeldByEngine();

        Assert.Equal(0, told);
    }

    [Fact]
    public void Declaring_the_same_name_as_well_is_refused()
    {
        var registry = new ConVarRegistry(new FakeEngine());

        var failure = Assert.Throws<InvalidOperationException>(() => registry.RegisterTypes([DeclaredToo(), typeof(Held)]));

        Assert.Contains("r_test_held", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_container_holds_it_under_its_class_and_its_name()
    {
        var (registry, _, convar) = Bound("3");
        var services = new ServiceContainer();

        registry.PublishTo(services);

        Assert.Same(convar, services.Resolve<Held>());
        Assert.Same(convar, services.ResolveNamed<Held>("r_test_held"));
    }
}
