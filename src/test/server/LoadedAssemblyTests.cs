using System.Reflection;
using System.Runtime.Loader;
using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What discovery can see.
///
/// Every registry used to read one assembly, so where a class was written
/// decided whether it existed — a command in Shared, or an entity class in a
/// second game assembly, was never found. These pin the replacement: one
/// assembly in, every loaded mod assembly out, in an order fixed enough to put
/// on the wire.
///
/// <para>
/// The bulk <c>[Fact]</c> these replace ended by CALLING two other facts in this
/// class, so <c>Discovery</c> and <c>SideContext</c> ran twice per suite and the
/// second run reported under the first one's name. They are ordinary facts now
/// and nothing calls them.
/// </para>
/// </summary>
public class LoadedAssemblyTests
{
    private static readonly Assembly Anchor = typeof(LoadedAssemblyTests).Assembly;

    private static string?[] Names() => LoadedAssemblies.All(Anchor, new RecordingLog()).Select(a => a.GetName().Name).ToArray();

    //-------------------------------------------------------------------------
    // What is in the set.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheAnchorIsScanned()
        => Assert.True(Names().Contains("SourceSharp.Server.Tests"), "the anchor is scanned");

    [Fact]
    public void TheContractAssemblyIsScanned()
        => Assert.True(Names().Contains("SourceSharp.Abi"), "the contract assembly is scanned");

    [Fact]
    public void TheSidesInteropAssemblyIsScanned()
        // Since plan_side_isolation P7 the shared SOURCE is in each side's
        // assembly, and since plan_shim_areas_split phase 3 the shared interop
        // is in each side's interop: what the server's scan must reach beside
        // its own assembly is the server's interop.
        => Assert.True(Names().Contains("SourceSharp.Server.Interop"), "the side's interop assembly is scanned");

    [Fact]
    public void AReferencedAssemblyIsScanned()
    {
        // The point of the whole change: an assembly the anchor merely
        // references is scanned too, so a declaration written there is found.
        Assert.True(Names().Contains("SourceSharp.Server"), "a referenced assembly is scanned");
    }

    [Fact]
    public void AnAssemblyWithNothingToDeclareIsNotScanned()
    {
        // The rule that leaves it out is exact rather than lucky: MapGen uses no
        // ABI type, so the compiler emitted no reference to the ABI, so it
        // cannot be carrying a declaration.
        Assert.True(!Names().Contains("SourceSharp.MapGen"), "an assembly with nothing to declare is not scanned");
    }

    [Fact]
    public void TheFrameworkIsNotScanned()
    {
        // The rule that keeps it out is "does it reference the ABI" rather than
        // a list of name prefixes.
        Assert.True(!Names().Any(n => n is not null &&
                (n.StartsWith("System", StringComparison.Ordinal) ||
                 n.StartsWith("Microsoft", StringComparison.Ordinal) ||
                 n == "netstandard" || n == "mscorlib")), "the framework is not scanned");
    }

    [Fact]
    public void EveryScannedAssemblyReferencesTheAbi()
        => Assert.True(LoadedAssemblies.All(Anchor, new RecordingLog()).All(a => a == typeof(ILog).Assembly ||
                         a.GetReferencedAssemblies().Any(r => r.Name == "SourceSharp.Abi")), "every scanned assembly references the ABI");

    [Fact]
    public void NothingIsScannedTwice()
    {
        var names = Names();

        Assert.True(names.Distinct().Count() == names.Length, "nothing is scanned twice");
    }

    //-------------------------------------------------------------------------
    // Order is protocol, not tidiness: user message indices and the layout
    // fingerprint are derived from this sequence, and both sides must walk it
    // identically.
    //-------------------------------------------------------------------------

    [Fact]
    public void AssembliesComeBackInNameOrder()
    {
        var names = Names();

        Assert.True(names.SequenceEqual(names.OrderBy(n => n, StringComparer.Ordinal)), "assemblies come back in name order");
    }

    [Fact]
    public void TheSameScanTwiceIsTheSameList()
        => Assert.True(LoadedAssemblies.All(Anchor, new RecordingLog()).Select(a => a.GetName().Name).SequenceEqual(Names()), "the same scan twice is the same list");

    [Fact]
    public void TheAnchorDoesNotChangeTheSet()
    {
        // Any assembly in the set is as good an anchor as any other: what is
        // scanned is what is loaded, not what was pointed at.
        Assert.True(LoadedAssemblies.All(typeof(ILog).Assembly, new RecordingLog())
                .Select(a => a.GetName().Name).SequenceEqual(Names()), "the anchor does not change the set");
    }

    //-------------------------------------------------------------------------
    // Types come back grouped by assembly in name order and, inside one, in
    // declaration order — the only sequence both sides can agree on, since a
    // metadata token means nothing across two assemblies.
    //-------------------------------------------------------------------------

    [Fact]
    public void EveryScannedAssemblyContributesItsTypes()
    {
        var types = LoadedAssemblies.Types(Anchor, new RecordingLog());

        Assert.True(LoadedAssemblies.All(Anchor, new RecordingLog()).All(a => types.Any(t => t.Assembly == a)), "every scanned assembly contributes its types");
    }

    [Fact]
    public void TypesAreGroupedByAssembly()
    {
        var (grouped, _, _) = WalkTypes();

        Assert.True(grouped, "types are grouped by assembly");
    }

    [Fact]
    public void AssemblyGroupsAreInNameOrder()
    {
        var (_, _, seen) = WalkTypes();

        Assert.True(seen.Select(a => a.GetName().Name)
                .SequenceEqual(seen.Select(a => a.GetName().Name).OrderBy(n => n, StringComparer.Ordinal)), "assembly groups are in name order");
    }

    [Fact]
    public void TypesWithinAnAssemblyAreInDeclarationOrder()
    {
        var (_, ordered, _) = WalkTypes();

        Assert.True(ordered, "types within an assembly are in declaration order");
    }

    private static (bool Grouped, bool Ordered, List<Assembly> Seen) WalkTypes()
    {
        var types = LoadedAssemblies.Types(Anchor, new RecordingLog());

        bool grouped = true;
        bool ordered = true;
        var seen = new List<Assembly>();

        for (int i = 0; i < types.Count; i++)
        {
            var assembly = types[i].Assembly;

            if (seen.Count == 0 || seen[^1] != assembly)
            {
                // A group that starts a second time means the assemblies are
                // interleaved, which would make the order depend on reflection.
                if (seen.Contains(assembly))
                    grouped = false;

                seen.Add(assembly);
                continue;
            }

            if (types[i].MetadataToken < types[i - 1].MetadataToken)
                ordered = false;
        }

        return (grouped, ordered, seen);
    }

    //-------------------------------------------------------------------------
    // The force-load is what makes discovery independent of what happens to have
    // run first, and this is the only place it can be seen: SourceSharp.Shared
    // is loaded into a side's context here by nothing but the scan, so the copy
    // that comes back being that context's — and not the one the default context
    // has held all along — is the walk having done it.
    //-------------------------------------------------------------------------

    private static (SideContext Side, IReadOnlyList<Assembly> Scanned) Scanned()
    {
        var side = new SideContext();
        var server = side.FromBin("SourceSharp.Server");

        Assert.True(server is not null, "a side context can load the server assembly");

        return (side, LoadedAssemblies.All(server!, new RecordingLog()));
    }

    [Fact]
    public void ASideContextScansTheSidesInteropAssembly()
    {
        var (_, scanned) = Scanned();

        Assert.True(scanned.Any(a => a.GetName().Name == "SourceSharp.Server.Interop"), "a side context scans the side's interop assembly");
    }

    [Fact]
    public void AnUntouchedAssemblyIsLoadedByTheScanItself()
    {
        var (side, scanned) = Scanned();
        var interop = scanned.FirstOrDefault(a => a.GetName().Name == "SourceSharp.Server.Interop");

        Assert.True(interop is not null && AssemblyLoadContext.GetLoadContext(interop) == side, "an untouched assembly is loaded by the scan itself");
    }

    [Fact]
    public void TheAnchorsContextWinsOverTheDefaultOne()
    {
        // The default context holds a copy of both of these too — this process
        // loaded them the ordinary way long before the side existed — and the
        // side's must win, name for name. A scan that mixed the two would hand a
        // registry types from an assembly its own side never loaded.
        var (side, scanned) = Scanned();

        Assert.True(scanned.Where(a => a.GetName().Name is "SourceSharp.Server" or "SourceSharp.Shared.Interop")
                   .All(a => AssemblyLoadContext.GetLoadContext(a) == side), "the anchor's context wins over the default one");
    }

    [Fact]
    public void TheSharedContractAssemblyIsStillTheOneAndOnly()
    {
        var (_, scanned) = Scanned();

        Assert.True(scanned.Any(a => ReferenceEquals(a, typeof(ILog).Assembly)), "the shared contract assembly is still the one and only");
    }

    //-------------------------------------------------------------------------
    // The behaviour all of this exists for: a registry handed one assembly
    // registers what another one declared.
    //-------------------------------------------------------------------------

    [Fact]
    public void EntityClassesAreFoundOutsideTheAssemblyAskedAbout()
    {
        // Anchored on the ABI, which declares no entity classes at all — so
        // anything registered here came from somewhere else entirely.
        var registry = new ScriptedEntityRegistry();

        Assert.True(registry.RegisterAll(typeof(ILog).Assembly) > 0, "entity classes are found outside the assembly asked about");
    }

    [Fact]
    public void AClassDeclaredInTheModsServerAssemblyIsRegistered()
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(ILog).Assembly);

        Assert.True(registry.IsRegistered("prop_physics"), "a class declared in the mod's server assembly is registered");
    }
}
