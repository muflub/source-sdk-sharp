using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_side_isolation §2.1: every protocol list is in KEY ORDER, whatever
/// order its declarations arrive in. Once the shared projects fold into each
/// side, the two sides compile the same declarations into different
/// assemblies, so an order that came from the assembly scan would differ
/// between them; an order that comes from the keys cannot.
///
/// <para>
/// Each fact hands one registry the same declarations twice, forwards and
/// reversed, and requires the same result both times and a non-trivial
/// population, so a list of one cannot pass by accident.
/// </para>
/// </summary>
public class ProtocolKeyOrderTests
{
    private static List<Type> SharedTypesWith<TAttribute>() where TAttribute : Attribute =>
        TestServices.ServerGame.GetTypes()
            .Where(t => t.GetCustomAttribute<TAttribute>() is not null)
            .OrderBy(t => t.MetadataToken)
            .ToList();

    [Fact]
    public void String_tables_are_created_in_name_order()
    {
        string[] Created(params Type[] declarations)
        {
            var engine = TestServices.Mock();
            engine.Returns("StringTables.Create", 4);
            var registry = new StringTableRegistry((IEngineStringTables)engine, new StringTableWriter((IEngineStringTables)engine), new RecordingLog());
            registry.Register(declarations, new ServiceContainer());
            registry.CreateAll();
            return engine.Calls.Where(c => c.Member == "StringTables.Create")
                                .Select(c => (string)c.Arg(0)!).ToArray();
        }

        // "SourceSharpTestProbe" sorts after "SourceSharpManaged", so handing
        // the probe first is the arm an assembly-order registry gets wrong.
        string[] probeFirst   = Created(typeof(ProbeStringTable), typeof(ManagedStringTable));
        string[] managedFirst = Created(typeof(ManagedStringTable), typeof(ProbeStringTable));

        Assert.Equal(["SourceSharpManaged", "SourceSharpTestProbe"], probeFirst);
        Assert.Equal(probeFirst, managedFirst);
    }

    [Fact]
    public void Temp_entities_register_in_the_same_sequence_whatever_order_they_arrive_in()
    {
        string[] Registered(IEnumerable<Type> declarations)
        {
            var engine = TestServices.Mock();
            engine.Returns("Effects.TempEntRegister", 0);
            new TempEntRegistry(new RecordingLog()).Register("Test", declarations, engine);
            return engine.Calls.Where(c => c.Member == "Effects.TempEntRegister")
                                .Select(c => c.ArgAs<TeClass>(0).ClassName).ToArray();
        }

        var declared = SharedTypesWith<TempEntityAttribute>();
        Assert.True(declared.Count > 2, $"only {declared.Count} temp entity declaration(s) found");

        string[] forwards = Registered(declared);
        string[] reversed = Registered(Enumerable.Reverse(declared));

        Assert.NotEmpty(forwards);
        Assert.Equal(forwards, reversed);
    }

    [Fact]
    public void Networked_states_are_discovered_in_classname_order()
    {
        var declared = SharedTypesWith<StandaloneNetStateAttribute>();
        Assert.True(declared.Count > 2, $"only {declared.Count} networked state declaration(s) found");

        string[] forwards = StandaloneNetStates.Discover(declared).Select(s => s.ClassName).ToArray();
        string[] reversed = StandaloneNetStates.Discover(Enumerable.Reverse(declared))
                                               .Select(s => s.ClassName).ToArray();

        Assert.Equal(forwards.OrderBy(n => n, StringComparer.Ordinal), forwards);
        Assert.Equal(forwards, reversed);
    }

    private sealed class LayoutA
    {
        [EntityField] public int Health { get; set; }
    }

    private sealed class LayoutB
    {
        [EntityField] public float Speed { get; set; }
        [EntityField] public int Armor { get; set; }
    }

    [Fact]
    public void The_layout_fingerprint_does_not_depend_on_discovery_order()
    {
        var forwards = new EntityLayoutRegistry(new RecordingLog());
        var reversed = new EntityLayoutRegistry(new RecordingLog());

        Assert.Equal(2, forwards.Discover([typeof(LayoutA), typeof(LayoutB)]));
        Assert.Equal(2, reversed.Discover([typeof(LayoutB), typeof(LayoutA)]));

        // The fingerprint mixes type names in sequence, so two orders of two
        // distinct layouts hash differently unless the registry orders them.
        Assert.Equal(forwards.Fingerprint, reversed.Fingerprint);
    }
}
